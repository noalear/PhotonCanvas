using System.Text;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using YTool.Core;
using YTool.Device;
using YTool.Doc;
using static YTool.App.UiKit;

namespace YTool.App;

/// <summary>
/// Builds the job continuously as the design changes, so the estimate,
/// warnings and errors are always current, and sends it to the machine.
/// Sending only uploads: the machine waits for its own start button.
/// </summary>
public sealed class JobPanel : UserControl
{
    readonly Document _doc;
    readonly AppSettings _settings;
    readonly CanvasView _canvas;
    readonly MachinePanel _machine;

    readonly ComboBox _passMode = new()
    {
        HorizontalAlignment = HorizontalAlignment.Stretch,
        ItemsSource = new[] { "Whole job, pass by pass", "Per path (stock xTool)" },
    };
    readonly NumericUpDown _delay = Number(0, 3600, 0.5m, "0.#");
    readonly Control _delayRow;
    readonly TextBlock _summary = new() { TextWrapping = TextWrapping.Wrap };
    readonly TextBlock _estimate = new() { FontSize = 22, FontWeight = FontWeight.SemiBold };
    readonly StackPanel _messages = new() { Spacing = 6 };
    readonly ToggleSwitch _preview = new() { OnContent = "Showing processing order", OffContent = "Show processing order" };
    readonly Button _send = new() { Classes = { "primary" } };
    readonly ProgressBar _progress = new() { Minimum = 0, Maximum = 1, IsVisible = false, Height = 6 };
    readonly TextBlock _sendState = Hint();

    // running job
    readonly Border _runCard;
    readonly TextBlock _runState = new() { FontWeight = FontWeight.SemiBold };
    readonly ProgressBar _runProgress = new() { Minimum = 0, Maximum = 1, Height = 8 };
    readonly TextBlock _runTimes = Hint();
    readonly Button _pause = new() { Content = "Pause", HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
    readonly Button _stop = new() { Content = "Stop job", HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
    DateTime? _startedAt;
    double? _sentEstimate;
    double _pausedFor;
    DateTime? _pausedAt;
    readonly DispatcherTimer _runTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    BuiltJob? _job;
    string? _error;
    int _version;
    bool _sending;
    readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(250) };

    public JobPanel(Document doc, AppSettings settings, CanvasView canvas, MachinePanel machine)
    {
        _doc = doc;
        _settings = settings;
        _canvas = canvas;
        _machine = machine;
        _passMode.SelectedIndex = settings.WholeJobPasses ? 0 : 1;
        _delay.Value = (decimal)settings.PassDelayS;
        _delayRow = Field("Pause between", _delay, Hint("Seconds to wait between passes, e.g. to let the material cool or check progress."));
        _send.Content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { new PathIcon { Data = Icon("IcSend"), Width = 14, Height = 14, Foreground = Brushes.White }, new TextBlock { Text = "Send to machine" } },
        };
        ToolTip.SetTip(_send, "Uploads the job. Nothing moves or fires until you press the start button on the machine.");

        var exportXf = Button("Export .xf…", tip: "Save the job package the machine runs.");
        exportXf.Click += async (_, _) => await Export(true);
        var exportGc = Button("Export G-code…", tip: "Save the motion G-code as text, to inspect or archive.");
        exportGc.Click += async (_, _) => await Export(false);

        _passMode.SelectionChanged += (_, _) => { _settings.WholeJobPasses = _passMode.SelectedIndex == 0; _settings.Save(); UpdateDelayRow(); Schedule(); };
        _delay.ValueChanged += (_, _) => { _settings.PassDelayS = (double)(_delay.Value ?? 0); _settings.Save(); Schedule(); };
        _preview.IsCheckedChanged += (_, _) => _canvas.Preview = _preview.IsChecked == true ? _job : null;
        _send.Click += async (_, _) => await Send();
        _machine.ConnectionChanged += UpdateSend;
        _stop.Background = Brush("Danger");
        _stop.Foreground = Brushes.White;
        ToolTip.SetTip(_stop, "Cancel the job now. It can't be resumed.");
        ToolTip.SetTip(_pause, "Pause or continue. The machine's button also pauses (short press) and cancels (long press while paused).");
        _pause.Click += async (_, _) => await PauseOrResume();
        _stop.Click += async (_, _) => await StopJob();
        _runCard = Card("Running job", _runState, _runProgress, _runTimes, Row(_pause, _stop));
        _runCard.IsVisible = false;
        _machine.ModeChanged += OnModeChanged;
        _runTimer.Tick += async (_, _) => await TickRun();
        _doc.Changed += content => { if (content) Schedule(); };
        _debounce.Tick += (_, _) => { _debounce.Stop(); _ = Rebuild(); };

        Content = new ScrollViewer
        {
            Content = new StackPanel
            {
                Children =
                {
                    _runCard,
                    Card("Passes",
                        Field("Run passes", _passMode, tip: "Whole job: every layer's first pass, then every layer's second pass, and so on (layers with fewer passes drop out). Stock: each path repeats all its passes before moving on."),
                        _delayRow),
                    Card("Estimate", _estimate, _summary, _preview),
                    Card("Checks", _messages),
                    Card("Output", _send, _progress, _sendState, Row(exportXf, exportGc)),
                },
            },
        };
        UpdateDelayRow();
        UpdateSend();
        Schedule();
    }

    void UpdateDelayRow() => _delayRow.IsVisible = _passMode.SelectedIndex == 0;

    JobOptions Options => new()
    {
        WholeJobPasses = _passMode.SelectedIndex == 0,
        PassDelayS = (double)(_delay.Value ?? 0),
    };

    /// <summary>
    /// Build the current design off the UI thread. The document state and the
    /// options are read here, on the UI thread: Avalonia controls may only be
    /// touched from it.
    /// </summary>
    Task<BuiltJob> BuildNow()
    {
        var state = _doc.State;
        var opt = Options;
        return Task.Run(() => JobBuilder.Build(state, opt));
    }

    void Schedule()
    {
        _debounce.Stop();
        _debounce.Start();
    }

    /// <summary>Build off the UI thread (document states are immutable), keep only the newest result.</summary>
    async Task Rebuild()
    {
        int v = ++_version;
        var state = _doc.State;
        var opt = Options;
        BuiltJob? job = null;
        string? error = null;
        try { job = await Task.Run(() => JobBuilder.Build(state, opt)); }
        catch (JobException e) { error = e.Message; }
        if (v != _version) return;
        _job = job;
        _error = error;
        Show();
    }

    void Show()
    {
        _messages.Children.Clear();
        if (_job is { } j)
        {
            _estimate.Text = $"≈ {Duration(j.EstimateS)}";
            var layers = JobBuilder.OutputLayers(_doc.State).Count(l => _doc.State.ShapesOn(l.Id).Any());
            _summary.Text = $"{j.Steps.Count} paths on {layers} layer{(layers == 1 ? "" : "s")} · " +
                            $"{j.Bounds.Width:0.#} × {j.Bounds.Height:0.#} mm at ({j.Bounds.MinX:0.#}, {j.Bounds.MinY:0.#}).\n" +
                            "The estimate ignores acceleration; real jobs run a little longer.";
            foreach (var w in j.Warnings) _messages.Children.Add(Message(w, "Warn"));
            if (j.Warnings.Count == 0) _messages.Children.Add(Message("No problems found.", "Ok"));
        }
        else
        {
            _estimate.Text = "–";
            _summary.Text = "";
            _messages.Children.Add(Message(_error ?? "Nothing to process.", "Danger"));
        }
        if (_preview.IsChecked == true) _canvas.Preview = _job;
        UpdateSend();
    }

    static Control Message(string text, string brush) => new DockPanel
    {
        Children =
        {
            new PathIcon { Data = Icon(brush == "Ok" ? "IcCheck" : "IcWarn"), Width = 13, Height = 13, Foreground = Brush(brush), Margin = new(0, 2, 8, 0), VerticalAlignment = VerticalAlignment.Top, [DockPanel.DockProperty] = Dock.Left },
            new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 12 },
        },
    };

    void UpdateSend()
    {
        _send.IsEnabled = !_sending && _job is not null && _machine.IsConnected;
        if (!_sending)
            _sendState.Text = !_machine.IsConnected ? "Connect to the machine on the Machine tab to send."
                : _job is null ? "Fix the problem above to send." : "Ready to send.";
    }

    void OnModeChanged(string mode)
    {
        switch (mode)
        {
            case "P_WORK":
                _runCard.IsVisible = true;
                _runState.Text = "Loaded – press the start button on the machine.";
                _pause.IsEnabled = false;
                _stop.IsEnabled = true;
                _runProgress.Value = 0;
                _runTimes.Text = _sentEstimate is { } est ? $"About {Duration(est)} once started." : "";
                break;
            case "P_WORKING":
                _runCard.IsVisible = true;
                if (_pausedAt is { } p) _pausedFor += (DateTime.Now - p).TotalSeconds;
                _pausedAt = null;
                _startedAt ??= DateTime.Now;
                _runState.Text = "Running";
                _pause.Content = "Pause";
                _pause.IsEnabled = _stop.IsEnabled = true;
                _runTimer.Start();
                break;
            case "P_PAUSE":
                _runCard.IsVisible = true;
                _pausedAt ??= DateTime.Now;
                _runState.Text = "Paused";
                _pause.Content = "Continue";
                _pause.IsEnabled = _stop.IsEnabled = true;
                break;
            default:
                // Finished, cancelled or idle: show the outcome briefly, then hide.
                if (_runCard.IsVisible && _startedAt is not null)
                {
                    _runState.Text = mode is "P_WORK_DONE" or "P_FINISH" ? "Finished" : "Ended";
                    _runProgress.Value = mode is "P_WORK_DONE" or "P_FINISH" ? 1 : _runProgress.Value;
                    _pause.IsEnabled = _stop.IsEnabled = false;
                }
                else _runCard.IsVisible = false;
                _runTimer.Stop();
                _startedAt = null;
                _pausedAt = null;
                _pausedFor = 0;
                break;
        }
    }

    async Task TickRun()
    {
        if (_startedAt is not { } start) return;
        double elapsed = (DateTime.Now - start).TotalSeconds - _pausedFor - (_pausedAt is { } p ? (DateTime.Now - p).TotalSeconds : 0);
        double? fraction = null;
        try
        {
            if (await _machine.GetJobProgressAsync() is { } prog)
            {
                fraction = prog.Fraction;
                if (prog.ElapsedS is { } e && e > 0) elapsed = e;
            }
        }
        catch (DeviceException) { }
        // Remaining: from the machine's progress when it gives one, else from our estimate.
        double? remaining = null;
        if (fraction is { } f && f > 0.02) remaining = elapsed / f * (1 - f);
        else if (_sentEstimate is { } est) remaining = Math.Max(0, est - elapsed);
        _runProgress.Value = fraction ?? (_sentEstimate is { } e2 && e2 > 0 ? Math.Min(0.99, elapsed / e2) : 0);
        _runTimes.Text = $"Elapsed {Duration(elapsed)}" + (remaining is { } r ? $" · about {Duration(r)} left" : "");
    }

    async Task PauseOrResume()
    {
        try
        {
            if (_machine.Mode == "P_PAUSE") await _machine.ResumeJobAsync();
            else await _machine.PauseJobAsync();
        }
        catch (DeviceException e) { _runState.Text = e.Message; }
    }

    async Task StopJob()
    {
        try { await _machine.StopJobAsync(); }
        catch (DeviceException e) { _runState.Text = e.Message; }
    }

    public async Task Export(bool xf)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner) return;
        BuiltJob job;
        try { job = await BuildNow(); }
        catch (JobException e)
        {
            await Dialogs.Message(owner, "Can't export", e.Message);
            return;
        }
        var baseName = _doc.FilePath is { } p ? Path.GetFileNameWithoutExtension(p) : "job";
        var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = xf ? "Export job package" : "Export G-code",
            DefaultExtension = xf ? "xf" : "gcode",
            SuggestedFileName = baseName + (xf ? ".xf" : ".gcode"),
            FileTypeChoices = [new FilePickerFileType(xf ? "xTool job package" : "G-code") { Patterns = [xf ? "*.xf" : "*.gcode"] }],
        });
        if (file?.TryGetLocalPath() is not { } path) return;
        if (xf) await File.WriteAllBytesAsync(path, job.Xf);
        else await File.WriteAllTextAsync(path, job.Motion, new UTF8Encoding(false));
    }

    async Task Send()
    {
        if (TopLevel.GetTopLevel(this) is not Window owner || _sending) return;
        BuiltJob job;
        try { job = await BuildNow(); }
        catch (JobException e)
        {
            await Dialogs.Message(owner, "Can't send", e.Message);
            return;
        }

        var sb = new StringBuilder();
        foreach (var l in JobBuilder.OutputLayers(_doc.State).Where(l => _doc.State.ShapesOn(l.Id).Any()))
        {
            var src = l.Laser.Light == LightSource.Red ? $"fiber {l.Laser.PulseWidthNs} ns {l.Laser.FrequencyKhz} kHz" : "diode";
            sb.AppendLine($"• {l.Name}: {l.Mode}, {l.Laser.Power:0.#}% at {l.Laser.Speed:0.#} mm/s × {l.Laser.Passes} ({src})");
        }
        var opt = Options;
        sb.AppendLine();
        sb.AppendLine(opt.WholeJobPasses
            ? $"Passes run over the whole job{(opt.PassDelayS > 0 ? $", {opt.PassDelayS:0.#} s pause between" : "")}."
            : "Each path runs all its passes before the next.");
        sb.AppendLine($"Size {job.Bounds.Width:0.#} × {job.Bounds.Height:0.#} mm · about {Duration(job.EstimateS)}.");
        if (_machine.Distance is { } d) sb.AppendLine($"Machine focus distance: {d:0.0} mm. Make sure this matches your material.");
        foreach (var w in job.Warnings) sb.AppendLine($"⚠ {w}");
        sb.AppendLine();
        sb.Append("After upload the machine waits. Nothing moves or fires until you press its start button.");
        if (!await Dialogs.Confirm(owner, "Send job to machine", sb.ToString(), "Upload", "Cancel")) return;

        _sending = true;
        _progress.IsVisible = true;
        _progress.Value = 0;
        _sendState.Text = "Uploading…";
        UpdateSend();
        try
        {
            var progress = new Progress<double>(f => _progress.Value = f);
            await _machine.UploadJobAsync(job.Xf, progress, CancellationToken.None);
            _sentEstimate = job.EstimateS;
            _sendState.Text = "Uploaded. Press the start button on the machine to begin (long-press while paused to cancel).";
        }
        catch (DeviceException e)
        {
            _sendState.Text = "Upload failed.";
            await Dialogs.Message(owner, "Upload failed", e.Message);
        }
        finally
        {
            _sending = false;
            _progress.IsVisible = false;
            _send.IsEnabled = _job is not null && _machine.IsConnected;
        }
    }
}
