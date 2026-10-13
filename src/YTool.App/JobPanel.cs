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
        _doc.Changed += content => { if (content) Schedule(); };
        _debounce.Tick += (_, _) => { _debounce.Stop(); _ = Rebuild(); };

        Content = new ScrollViewer
        {
            Content = new StackPanel
            {
                Children =
                {
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

    public async Task Export(bool xf)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner) return;
        BuiltJob job;
        try { job = await Task.Run(() => JobBuilder.Build(_doc.State, Options)); }
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
        try { job = await Task.Run(() => JobBuilder.Build(_doc.State, Options)); }
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
