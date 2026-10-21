using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using PhotonCanvas.Core;
using PhotonCanvas.Device;
using PhotonCanvas.Doc;
using static PhotonCanvas.App.UiKit;

namespace PhotonCanvas.App;

/// <summary>
/// The job, always in view along the bottom: Send and Frame on the left, the run order as a
/// timeline in the middle (each layer as long as it takes), the total time and pass mode on
/// the right, then the checks. The job is rebuilt as the design changes, so everything here
/// is always current. While a job runs, Send becomes Pause/Stop and the timeline fills up.
/// Sending only uploads: the machine waits for its own start button.
/// </summary>
public sealed class JobBar : UserControl
{
    readonly Document _doc;
    readonly AppSettings _settings;
    readonly CanvasView _canvas;
    readonly MachinePanel _machine;

    readonly ComboBox _passMode = new()
    {
        ItemsSource = new[] { "Round by round", "Path by path" },
        MinWidth = 0, FontSize = 11.5, MinHeight = 22, Height = 24, Padding = new(8, 0),
    };
    readonly NumericUpDown _delay = Number(0, 3600, 0.5m, "0.#");
    readonly StackPanel _delayRow;
    readonly TextBlock _summary = new() { FontSize = 12, Foreground = Brush("TextMuted"), TextTrimming = TextTrimming.CharacterEllipsis };
    readonly TextBlock _caption = new() { FontSize = 11.5, Foreground = Brush("TextMuted"), TextTrimming = TextTrimming.CharacterEllipsis };
    readonly TextBlock _estimate = new() { FontSize = 22, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Right };
    readonly JobTimeline _timeline = new();
    readonly Button _checks = new() { Classes = { "chip" } };
    readonly StackPanel _messages = new() { Spacing = 8, Width = 380 };
    readonly ToggleButton _order = new() { Classes = { "chip" } };
    readonly Button _send = new() { Classes = { "primary" }, Width = 140, Height = 48, Padding = new(12, 0), CornerRadius = new(8), VerticalContentAlignment = VerticalAlignment.Center };
    readonly ProgressBar _progress = new() { Minimum = 0, Maximum = 1, IsVisible = false, Height = 4, Margin = new(0, 3, 0, 0) };
    string _sendState = "";
    string? _hoverText;

    // running job
    readonly StackPanel _runPanel;
    readonly TextBlock _runState = new() { FontWeight = FontWeight.SemiBold, FontSize = 12.5 };
    readonly Button _pause = new() { Content = "Pause", Width = 75, HorizontalContentAlignment = HorizontalAlignment.Center };
    readonly Button _stop = new() { Content = "Stop", Width = 75, HorizontalContentAlignment = HorizontalAlignment.Center };
    DateTime? _startedAt;
    double? _sentEstimate;
    double _pausedFor;
    DateTime? _pausedAt;
    string _runTimes = "";
    readonly DispatcherTimer _runTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    // Where the laser is: the job as sent, followed by elapsed time (the machine only reports that).
    readonly DispatcherTimer _follow = new() { Interval = TimeSpan.FromMilliseconds(100) };
    BuiltJob? _sentJob;
    double? _machineElapsed;
    DateTime _machineElapsedAt;

    // framing
    readonly SplitButton _frame = new() { Height = 54, Width = 96 };
    readonly NumericUpDown _framePower = UiKit.Number(0.5m, (decimal)PhotonCanvas.Core.Job.FramePowerMax, 0.5m, "0.#"), _frameSpeed = UiKit.Number(100, 20000, 500, "0");
    readonly RadioButton _frameRect = new() { Content = "Rectangle around everything", GroupName = "frame" };
    readonly RadioButton _frameOutline = new() { Content = "Snug outline (round or angled designs)", GroupName = "frame" };
    bool _frameConfirmed, _frameUpdating, _frameDirty;

    BuiltJob? _job;
    string? _error;
    int _version;
    bool _sending;
    readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(250) };

    /// <summary>A new job was built (null when the design can't be processed).</summary>
    public event Action<BuiltJob?>? JobBuilt;

    /// <summary>A layer's block on the timeline was clicked.</summary>
    public event Action<Guid>? LayerClicked;

    public BuiltJob? Job => _job;

    public JobBar(Document doc, AppSettings settings, CanvasView canvas, MachinePanel machine)
    {
        _doc = doc;
        _settings = settings;
        _canvas = canvas;
        _machine = machine;

        // ---- send / run (left) ----
        _send.Content = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 9, HorizontalAlignment = HorizontalAlignment.Center,
            Children =
            {
                new PathIcon { Data = Icon("IcSend"), Width = 17, Height = 17, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center },
                _sendText,
            },
        };
        ToolTip.SetTip(_send, "Uploads the job. Nothing moves or fires until you press the start button on the machine.");
        _send.Click += async (_, _) => await Send();
        _stop.Background = Brush("Danger");
        _stop.Foreground = Brushes.White;
        ToolTip.SetTip(_stop, "Cancel the job now. It can't be resumed.");
        ToolTip.SetTip(_pause, "Pause or continue. The machine's button also pauses (short press) and cancels (long press while paused).");
        _pause.Click += async (_, _) => await PauseOrResume();
        _stop.Click += async (_, _) => await StopJob();
        _runPanel = new StackPanel
        {
            Width = 158, Spacing = 4, IsVisible = false, VerticalAlignment = VerticalAlignment.Center,
            Children = { _runState, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _pause, _stop } } },
        };
        var sendArea = new Panel { Children = { _send, _runPanel }, VerticalAlignment = VerticalAlignment.Center };

        // ---- frame ----
        _frame.Content = new StackPanel
        {
            Spacing = 2, HorizontalAlignment = HorizontalAlignment.Center,
            Children =
            {
                new PathIcon { Data = Icon("IcFrame"), Width = 18, Height = 18, HorizontalAlignment = HorizontalAlignment.Center },
                new TextBlock { Text = "Frame", FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center },
            },
        };
        (_settings.FrameOutline ? _frameOutline : _frameRect).IsChecked = true;
        _framePower.Value = (decimal)Math.Clamp(_settings.FramePowerPct, 0.5, PhotonCanvas.Core.Job.FramePowerMax);
        _frameSpeed.Value = (decimal)Math.Clamp(_settings.FrameSpeedMmS, 100, 20000);
        _framePower.ValueChanged += (_, _) => { _settings.FramePowerPct = (double)(_framePower.Value ?? 6.4m); _settings.Save(); _ = UpdateFrame(); };
        _frameSpeed.ValueChanged += (_, _) => { _settings.FrameSpeedMmS = (double)(_frameSpeed.Value ?? 16000); _settings.Save(); _ = UpdateFrame(); };
        _frameRect.IsCheckedChanged += (_, _) => FrameShapeChanged();
        _frame.Flyout = new Flyout
        {
            Placement = PlacementMode.TopEdgeAlignedLeft,
            Content = new StackPanel
            {
                Spacing = 6, Width = 300,
                Children =
                {
                    new TextBlock { Text = "WHAT TO TRACE", Classes = { "h" } },
                    _frameRect, _frameOutline,
                    new TextBlock { Text = "TRACE", Classes = { "h" }, Margin = new(0, 6, 0, 0) },
                    UiKit.Row(UiKit.Field("Power %", _framePower), UiKit.Field("mm/s", _frameSpeed)),
                    Hint("The head traces around the job with a faint laser dot, over and over, until you stop it. Move the design and the frame follows. Keep the power low: it's the real laser."),
                },
            },
        };
        ToolTip.SetTip(_frame, "Show where the job will land: the head traces around it with a faint dot until you click again. The arrow chooses rectangle or outline.");
        _frame.Click += async (_, _) => await ToggleFrame();
        _machine.FramingChanged += UpdateFrameButton;

        // ---- timeline (middle) ----
        _timeline.LayerClicked += id => LayerClicked?.Invoke(id);
        _timeline.Hovered += t => { _hoverText = t; ShowCaption(); };

        // ---- time and passes (right) ----
        _passMode.SelectedIndex = settings.WholeJobPasses ? 0 : 1;
        ToolTip.SetTip(_passMode, "Round by round: every layer's first pass, then every layer's second pass, and so on (layers with fewer passes drop out). Path by path: each path repeats all its passes before moving on (what xTool Studio does).");
        _delay.Value = (decimal)settings.PassDelayS;
        _delay.Width = 52;
        _delay.ShowButtonSpinner = false;
        _delay.HorizontalContentAlignment = HorizontalAlignment.Center;
        _delay.MinHeight = 22;
        _delay.Height = 24;
        _delay.FontSize = 12;
        ToolTip.SetTip(_delay, "Seconds to wait between rounds, e.g. to let the material cool or to check progress.");
        _delayRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { new TextBlock { Text = "pause", FontSize = 12, Foreground = Brush("TextMuted"), VerticalAlignment = VerticalAlignment.Center }, _delay, new TextBlock { Text = "s", FontSize = 12, Foreground = Brush("TextMuted"), VerticalAlignment = VerticalAlignment.Center } } };
        _passMode.SelectionChanged += (_, _) => { _settings.WholeJobPasses = _passMode.SelectedIndex == 0; _settings.Save(); UpdateDelayRow(); Schedule(); };
        _delay.ValueChanged += (_, _) => { _settings.PassDelayS = (double)(_delay.Value ?? 0); _settings.Save(); Schedule(); };
        ToolTip.SetTip(_estimate, "Estimated time. It ignores acceleration, so real jobs run a little longer.");
        var passes = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 6, Margin = new(12, 0, 0, 0),
            Children = { _passesLabel, _passMode, _delayRow },
        };
        var right = new StackPanel
        {
            Spacing = 0, VerticalAlignment = VerticalAlignment.Center,
            Children = { _estimate, _estimatedLabel },
        };

        // ---- checks, order, export ----
        _checks.Flyout = new Flyout { Placement = PlacementMode.TopEdgeAlignedRight, Content = _messages };
        ToolTip.SetTip(_checks, "Things worth knowing before you send. Click for the list.");
        Compact(false);
        ToolTip.SetTip(_order, "Show the processing order on the work area: numbered paths and the travel moves between them.");
        _order.IsCheckedChanged += (_, _) => _canvas.Preview = _order.IsChecked == true ? _job : null;
        var more = _export;
        var exportXf = new MenuItem { Header = "Job package for the machine (.xf)…" };
        exportXf.Click += async (_, _) => await Export(true);
        var exportGc = new MenuItem { Header = "G-code as text (.gcode)…" };
        exportGc.Click += async (_, _) => await Export(false);
        more.Flyout = new MenuFlyout { Placement = PlacementMode.TopEdgeAlignedRight, Items = { exportXf, exportGc } };
        ToolTip.SetTip(more, "Save the job to a file instead of sending it.");
        var chips = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new(8, 0, 0, 0), Children = { _checks, _order, more } };
        foreach (var c in new Control[] { _checks, _order, more }) { c.Height = 24; ((TemplatedControl)c).Padding = new(8, 0); }

        var title = new TextBlock { Text = "JOB", Classes = { "h" }, Margin = new(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        var head = new DockPanel();
        DockPanel.SetDock(title, Dock.Left);
        DockPanel.SetDock(passes, Dock.Right);
        head.Children.Add(title);
        head.Children.Add(passes);
        _summary.VerticalAlignment = VerticalAlignment.Center;
        head.Children.Add(_summary);
        var foot = new DockPanel();
        DockPanel.SetDock(chips, Dock.Right);
        foot.Children.Add(chips);
        _caption.VerticalAlignment = VerticalAlignment.Center;
        foot.Children.Add(new Panel { Children = { _caption, _progress } });
        var middle = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center, Children = { head, _timeline, foot } };

        var grid = new Grid { ColumnDefinitions = new("Auto,Auto,*,Auto"), ColumnSpacing = 12, Margin = new(10, 8) };
        grid.Children.Add(sendArea);
        Grid.SetColumn(_frame, 1); grid.Children.Add(_frame);
        Grid.SetColumn(middle, 2); grid.Children.Add(middle);
        Grid.SetColumn(right, 3); grid.Children.Add(right);
        Content = new Border { Background = Brush("Bg1"), BorderBrush = Brush("Line"), BorderThickness = new(0, 1, 0, 0), Child = grid, ClipToBounds = true };
        SizeChanged += (_, e) => Compact(e.NewSize.Width < 840);

        _machine.ConnectionChanged += UpdateSend;
        _machine.ModeChanged += OnModeChanged;
        _machine.LoadedJobChanged += () => { if (_machine.Mode == "P_WORK") ShowLoaded(); };
        _runTimer.Tick += async (_, _) => await TickRun();
        _follow.Tick += (_, _) => Follow();
        _doc.Changed += content => { if (content) Schedule(); else if (!_doc.InEdit) ShowTimeline(); };
        _debounce.Tick += (_, _) => { _debounce.Stop(); _ = Rebuild(); };
        UpdateDelayRow();
        UpdateSend();
        Schedule();
    }

    readonly TextBlock _passesLabel = new() { Text = "Passes", FontSize = 12, Foreground = Brush("TextMuted"), VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock _estimatedLabel = new() { Text = "estimated", FontSize = 10.5, Foreground = Brush("TextMuted"), HorizontalAlignment = HorizontalAlignment.Right };
    readonly Button _export = new() { Classes = { "chip" } };
    readonly TextBlock _sendText = new() { Text = "Ship It", FontSize = 15, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
    bool? _compact;

    /// <summary>Narrow (a small window): drop the secondary labels so the timeline keeps its room.</summary>
    void Compact(bool on)
    {
        if (_compact == on) return;
        _compact = on;
        _send.Width = on ? 124 : 140;
        _runPanel.Width = on ? 132 : 158;
        _passesLabel.IsVisible = _estimatedLabel.IsVisible = !on;
        _frame.Width = on ? 88 : 96;
        _order.Content = on ? Chip("IcPreview", null) : Chip("IcPreview", "Order");
        _export.Content = on ? Chip("IcExport", null) : Chip("IcExport", "Export");
        _estimate.FontSize = on ? 18 : 22;
    }

    static Control Chip(string icon, string? text) => text is null
        ? new PathIcon { Data = Icon(icon), Width = 12, Height = 12, VerticalAlignment = VerticalAlignment.Center }
        : new StackPanel
    {
        Orientation = Orientation.Horizontal, Spacing = 5,
        Children = { new PathIcon { Data = Icon(icon), Width = 12, Height = 12, VerticalAlignment = VerticalAlignment.Center }, new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center } },
    };

    /// <summary>Show the processing order on the work area.</summary>
    public bool ShowOrder
    {
        get => _order.IsChecked == true;
        set => _order.IsChecked = value;
    }

    public bool CanSend => !_sending && _job is not null && (_machine.IsConnected || _settings.ActiveSetup().Kind == MachineKind.Grbl);
    public bool CanFrame => _machine.IsFraming || CanSend;

    void UpdateDelayRow() => _delayRow.IsVisible = _passMode.SelectedIndex == 0;

    JobOptions Options => new()
    {
        WholeJobPasses = _passMode.SelectedIndex == 0,
        PassDelayS = (double)(_delay.Value ?? 0),
        Mode = WorkModes.From(_settings),
        Machine = WorkModes.Area(_settings),
        Section = _section,
        OnlyLayer = _onlyLayer,
    };

    Guid? _onlyLayer;

    /// <summary>Mark only this layer (layer by layer: one screen or one colour at a time); null = all.</summary>
    public Guid? OnlyLayer
    {
        get => _onlyLayer;
        set
        {
            _onlyLayer = value;
            Schedule();
        }
    }

    Section? _section;

    /// <summary>Mark only this section of a bigger design (null: the whole design).</summary>
    public Section? Section
    {
        get => _section;
        set
        {
            _section = value;
            _canvas.SectionArea = value?.Clip;
            Schedule();
        }
    }

    /// <summary>The job as built now (null when it can't be built).</summary>
    public BuiltJob? CurrentJob => _job;

    TimeLapse? _timeLapse;

    async Task FinishTimeLapse(TimeLapse lapse)
    {
        if (await lapse.FinishAsync() is not { } saved) return;
        _sendState = File.Exists(saved) ? $"Time-lapse saved: {saved}" : $"Time-lapse photos saved in {saved} (install ffmpeg to get a video).";
        ShowCaption();
    }

    /// <summary>A short sound when a job finishes (the system's own sound player on a Mac or Linux).</summary>
    static void Chime()
    {
        _ = Task.Run(() =>
        {
            try
            {
                if (OperatingSystem.IsWindows()) { Console.Beep(880, 140); Console.Beep(1320, 220); }
                else if (OperatingSystem.IsMacOS()) Play("afplay", "/System/Library/Sounds/Glass.aiff");
                else if (OperatingSystem.IsLinux()) Play("canberra-gtk-play", "--id=complete");
            }
            catch (Exception e) when (e is IOException or PlatformNotSupportedException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
        });

        static void Play(string program, string arg)
        {
            using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(program, arg) { UseShellExecute = false, CreateNoWindow = true });
            p?.WaitForExit(3000);
        }
    }

    /// <summary>The work mode or attachment changed: build again.</summary>
    public void Refresh() => Schedule();

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
        JobBuilt?.Invoke(job);
        if (_machine.IsFraming) _ = UpdateFrame();
    }

    void ShowTimeline() => _timeline.Show(_job, _doc.State, _doc.ActiveLayer, _error);

    void Show()
    {
        _messages.Children.Clear();
        _messages.Children.Add(new TextBlock { Text = "CHECKS", Classes = { "h" } });
        ShowTimeline();
        if (_job is { } j)
        {
            _estimate.Text = Clock(j.EstimateS);
            int layers = j.LayerTimes.Count;
            int rounds = j.LayerTimes.Count == 0 ? 1 : j.LayerTimes.Max(t => t.Passes);
            _summary.Text = (_section is { } sec ? $"{char.ToUpper(sec.Label[0])}{sec.Label[1..]} · " : "") +
                            (_onlyLayer is { } ol && _doc.State.FindLayer(ol) is { } oll ? $"Only {oll.Name} · " : "") + (WorkModes.Usable(_settings) && _settings.WorkMode != "Flat" ? $"{_settings.WorkMode} · " : "") +
                            $"{layers} layer{(layers == 1 ? "" : "s")} · {j.Steps.Count} path{(j.Steps.Count == 1 ? "" : "s")}" +
                            (j.RoundDelayS is not null && rounds > 1 ? $" · {rounds} rounds" : "") +
                            $" · {j.Bounds.Width:0.#} × {j.Bounds.Height:0.#} mm at {j.Bounds.MinX:0.#}, {j.Bounds.MinY:0.#}";
            foreach (var w in j.Warnings) _messages.Children.Add(Message(w, "Warn"));
            if (j.Warnings.Count == 0) _messages.Children.Add(Message("No problems found.", "Ok"));
            _checks.Content = j.Warnings.Count == 0 ? ChipText("IcCheck", "All clear", "Ok") : ChipText("IcWarn", j.Warnings.Count == 1 ? "1 check" : $"{j.Warnings.Count} checks", "Warn");
        }
        else
        {
            _estimate.Text = "–";
            _summary.Text = "";
            _messages.Children.Add(Message(_error ?? "Nothing to process.", "Danger"));
            _checks.Content = _doc.State.Shapes.Count == 0 ? ChipText("IcCheck", "Empty", "TextMuted") : ChipText("IcWarn", "Can't build", "Danger");
        }
        if (_order.IsChecked == true) _canvas.Preview = _job;
        UpdateSend();
    }

    static Control ChipText(string icon, string text, string brush) => new StackPanel
    {
        Orientation = Orientation.Horizontal, Spacing = 5,
        Children =
        {
            new PathIcon { Data = Icon(icon), Width = 12, Height = 12, Foreground = Brush(brush), VerticalAlignment = VerticalAlignment.Center },
            new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center },
        },
    };

    static Control Message(string text, string brush) => new DockPanel
    {
        Children =
        {
            new PathIcon { Data = Icon(brush == "Ok" ? "IcCheck" : "IcWarn"), Width = 13, Height = 13, Foreground = Brush(brush), Margin = new(0, 2, 8, 0), VerticalAlignment = VerticalAlignment.Top, [DockPanel.DockProperty] = Dock.Left },
            new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 12 },
        },
    };

    void ShowCaption()
    {
        _caption.Text = _hoverText ?? (_runPanel.IsVisible && _runTimes.Length > 0 ? _runTimes : _sendState);
    }

    void UpdateSend()
    {
        _send.IsEnabled = CanSend;
        UpdateFrameButton();
        if (!_sending)
            _sendState = !_machine.IsConnected ? "Connect the machine (right end of the ribbon) to send or frame."
                : _job is null ? "Fix the problem in Checks to send." : "Ready to send.";
        ShowCaption();
    }

    void ShowLoaded()
    {
        _runState.Text = "Loaded – press start";
        _runTimes = (_machine.LoadedJobDescription() is { } who ? $"{who} " : "") + "Press the start button on the machine, or Stop to cancel it.";
        ShowCaption();
    }

    void ShowRunning(bool on)
    {
        _runPanel.IsVisible = on;
        _send.IsVisible = !on;
        if (!on) _timeline.Progress = null;
        ShowCaption();
    }

    void OnModeChanged(string mode)
    {
        if (_machine.IsFraming)
        {
            ShowRunning(false); // framing isn't a job
            return;
        }
        switch (mode)
        {
            case "P_WORK":
                ShowRunning(true);
                ShowLoaded();
                _pause.IsEnabled = false;
                _stop.IsEnabled = true;
                _timeline.Progress = 0;
                if (_sentEstimate is { } est) _runTimes += $" About {Clock(est)} once started.";
                break;
            case "P_WORKING":
                ShowRunning(true);
                if (_pausedAt is { } p) _pausedFor += (DateTime.Now - p).TotalSeconds;
                _pausedAt = null;
                if (_startedAt is null && _settings.TimeLapse && _timeLapse is null)
                    _timeLapse = TimeLapse.Start(_machine.TimeLapseFrameAsync, _settings.TimeLapseSeconds, _sentFrom is { } nm ? Path.GetFileNameWithoutExtension(nm) : "Untitled");
                _startedAt ??= DateTime.Now;
                _machineElapsedAt = DateTime.Now;
                if (_sentJob is not null) _follow.Start();
                _runState.Text = "Running";
                _pause.Content = "Pause";
                _pause.IsEnabled = _stop.IsEnabled = true;
                _runTimer.Start();
                break;
            case "P_PAUSE":
                ShowRunning(true);
                _pausedAt ??= DateTime.Now;
                _follow.Stop();
                Follow();
                _runState.Text = "Paused";
                _pause.Content = "Continue";
                _pause.IsEnabled = _stop.IsEnabled = true;
                break;
            default:
                // Finished, cancelled or idle: say how it ended, then go back to Send.
                if (_runPanel.IsVisible && _startedAt is not null)
                {
                    double took = (DateTime.Now - _startedAt.Value).TotalSeconds - _pausedFor;
                    bool finished = mode is "P_WORK_DONE" or "P_FINISH";
                    _sendState = finished ? $"Finished in {Clock(took)}." : "The job ended.";
                    if (finished) LearnTiming(took);
                    if (_sentJob is { } sj)
                    {
                        JobHistory.Add(new JobRecord(DateTime.Now, _sentFrom is { } f ? Path.GetFileNameWithoutExtension(f) : "Untitled", sj.EstimateS, took, finished, sj.LayerTimes.Count, sj.Bounds.Width, sj.Bounds.Height));
                        if (finished) Chime();
                    }
                    if (finished && _sentJob is not null && !_sentMidSections) RunFinished?.Invoke(_sentFrom);
                }
                if (_timeLapse is { } lapse)
                {
                    _timeLapse = null;
                    _ = FinishTimeLapse(lapse);
                }
                _follow.Stop();
                _canvas.ShowRun(null, null);
                _machineElapsed = null;
                ShowRunning(false);
                _runTimer.Stop();
                _runTimes = "";
                _startedAt = null;
                _pausedAt = null;
                _pausedFor = 0;
                ShowCaption();
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
                if (prog.ElapsedS is { } e && e > 0)
                {
                    elapsed = e;
                    _machineElapsed = e;
                    _machineElapsedAt = DateTime.Now;
                }
            }
        }
        catch (DeviceException) { }
        // Remaining: from the machine's progress when it gives one, else from our estimate.
        double? remaining = null;
        if (fraction is { } f && f > 0.02) remaining = elapsed / f * (1 - f);
        else if (_sentEstimate is { } est) remaining = Math.Max(0, est * Math.Clamp(_settings.JobTimeScale, 0.3, 5) - elapsed);
        _timeline.Progress = fraction ?? (_sentEstimate is { } e2 && e2 > 0 ? Math.Min(0.99, elapsed / (e2 * Math.Clamp(_settings.JobTimeScale, 0.3, 5))) : 0);
        _runTimes = $"Elapsed {Clock(elapsed)}" + (remaining is { } r ? $" · about {Clock(r)} left" : "");
        ShowCaption();
    }

    /// <summary>Seconds the laser has been working: the machine's own count when it gives one, carried forward smoothly between reports.</summary>
    double RunningSeconds()
    {
        if (_startedAt is not { } start) return 0;
        double paused = _pausedFor + (_pausedAt is { } p ? (DateTime.Now - p).TotalSeconds : 0);
        if (_machineElapsed is { } m)
            return m + (_pausedAt is null ? (DateTime.Now - _machineElapsedAt).TotalSeconds : 0);
        return (DateTime.Now - start).TotalSeconds - paused;
    }

    /// <summary>Move the "laser is here" marker on the work area.</summary>
    void Follow()
    {
        if (_sentJob is not { } job || _startedAt is null) return;
        // The estimate leaves out acceleration: scale real time back onto it, by what past jobs showed.
        double est = RunningSeconds() / Math.Clamp(_settings.JobTimeScale, 0.3, 5);
        _canvas.ShowRun(job, RunTracker.At(job, est));
    }

    /// <summary>After a finished job: how much longer it took than estimated, blended into what's learned.</summary>
    void LearnTiming(double tookSeconds)
    {
        if (_sentJob is not { EstimateS: > 10 } job) return;
        double ratio = Math.Clamp(tookSeconds / job.EstimateS, 0.5, 4);
        _settings.JobTimeScale = Math.Round(_settings.JobTimeScale * 0.6 + ratio * 0.4, 3);
        _settings.Save();
    }

    async Task PauseOrResume()
    {
        try
        {
            if (_machine.Mode == "P_PAUSE") await _machine.ResumeJobAsync();
            else await _machine.PauseJobAsync();
        }
        catch (DeviceException e) { _runTimes = e.Message; ShowCaption(); }
    }

    async Task StopJob()
    {
        try { await _machine.StopJobAsync(); }
        catch (DeviceException e) { _runTimes = e.Message; ShowCaption(); }
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

    FrameShape CurrentFrameShape => _frameOutline.IsChecked == true ? FrameShape.Outline : FrameShape.Rectangle;

    void FrameShapeChanged()
    {
        _settings.FrameOutline = _frameOutline.IsChecked == true;
        _settings.Save();
        _ = UpdateFrame();
    }

    void UpdateFrameButton()
    {
        bool on = _machine.IsFraming;
        if (_frame.Content is StackPanel { Children: [_, TextBlock label] }) label.Text = on ? "Stop framing" : "Frame";
        _frame.Background = on ? Brush("Danger") : null;
        _frame.Foreground = on ? Brushes.White : null;
        _frame.IsEnabled = CanFrame;
        if (!on && _sendState.StartsWith("Framing", StringComparison.Ordinal))
        {
            _sendState = "Framing stopped.";
            ShowCaption();
        }
    }

    public async Task ToggleFrame()
    {
        if (TopLevel.GetTopLevel(this) is not Window owner) return;
        if (_machine.IsFraming)
        {
            try { await _machine.StopFrameAsync(); }
            catch (DeviceException e) { _sendState = $"Couldn't stop framing: {e.Message} The machine's button also stops it."; ShowCaption(); }
            return;
        }
        BuiltJob job;
        try { job = await BuildNow(); }
        catch (JobException e)
        {
            await Dialogs.Message(owner, "Can't frame", e.Message);
            return;
        }
        bool loaded = _machine.Mode == "P_WORK";
        if (!_frameConfirmed || loaded)
        {
            var text = "Framing starts straight away, without the machine's button: the head moves and a faint laser dot traces " +
                       $"around the design ({job.Bounds.Width:0.#} × {job.Bounds.Height:0.#} mm), over and over, until you click Stop framing." +
                       (loaded ? "\n\nThe job waiting on the machine will be cleared. Send it again when you're done framing." : "") +
                       "\n\nDon't look into the beam. If the lid is open, wear your safety glasses.";
            if (!await Dialogs.Confirm(owner, "Start framing?", text, "Start framing", "Cancel")) return;
            _frameConfirmed = true;
        }
        _frame.IsEnabled = false;
        _sendState = "Starting the frame…";
        ShowCaption();
        try
        {
            await _machine.StartFrameAsync(JobBuilder.FrameGcode(job, CurrentFrameShape, _settings.FramePowerPct, _settings.FrameSpeedMmS));
            _sendState = "Framing. Move the design to adjust; click Stop framing when it's where you want it.";
        }
        catch (DeviceException e)
        {
            _sendState = e.Message;
        }
        finally { UpdateFrameButton(); ShowCaption(); }
    }

    /// <summary>Re-trace after the design or the frame shape changed (one update at a time, newest wins).</summary>
    async Task UpdateFrame()
    {
        if (!_machine.IsFraming) return;
        if (_frameUpdating) { _frameDirty = true; return; }
        _frameUpdating = true;
        try
        {
            do
            {
                _frameDirty = false;
                if (_job is not { } job) break;
                try { await _machine.UpdateFrameAsync(JobBuilder.FrameGcode(job, CurrentFrameShape, _settings.FramePowerPct, _settings.FrameSpeedMmS)); }
                catch (DeviceException e) { _sendState = $"Couldn't move the frame: {e.Message}"; ShowCaption(); break; }
            } while (_frameDirty && _machine.IsFraming);
        }
        finally { _frameUpdating = false; }
    }

    /// <summary>A job this bar sent finished on the machine (not cancelled): the design it came from, by file.</summary>
    public event Action<string?>? RunFinished;

    string? _sentFrom;
    bool _sentMidSections;

    public async Task Send()
    {
        if (TopLevel.GetTopLevel(this) is not Window owner || _sending) return;
        // Dates and times as of now (fields are otherwise filled in when the design changes).
        var fresh = VariableText.Refresh(_doc.State, DateTime.Now);
        if (!ReferenceEquals(fresh, _doc.State)) _doc.Commit("Update date and time", fresh);
        BuiltJob job;
        try { job = await BuildNow(); }
        catch (JobException e)
        {
            await Dialogs.Message(owner, "Can't send", e.Message);
            return;
        }
        // A GRBL machine: the job as GRBL G-code, sent over USB from its own window.
        if (_settings.ActiveSetup() is { Kind: MachineKind.Grbl } grbl)
        {
            string title = _doc.FilePath is { } fp ? Path.GetFileNameWithoutExtension(fp) : "Untitled";
            string gcode;
            try { gcode = GrblWriter.Write(job.Job, grbl, title); }
            catch (JobException e) { await Dialogs.Message(owner, "Can't send", e.Message); return; }
            await new GrblWindow(grbl, gcode, job.Bounds, title).ShowDialog(owner);
            return;
        }

        var sb = new StringBuilder();
        var sent = JobBuilder.OutputLayers(_doc.State).Where(l => _doc.State.ShapesFor(l).Any()).ToList();
        static string Line(Layer l)
        {
            var src = l.Laser.Light == LightSource.Red ? $"MOPA {l.Laser.PulseWidthNs} ns {l.Laser.FrequencyKhz} kHz" : "diode";
            return $"• {l.Name}: {l.Mode}, {l.Laser.Power:0.#}% at {l.Laser.Speed:0.#} mm/s × {l.Laser.Passes} ({src})";
        }
        if (sent.Count <= 8)
            foreach (var l in sent) sb.AppendLine(Line(l));
        else
        {
            // Many layers (a test grid): the ranges say more than 50 lines would.
            static string Range(IEnumerable<double> v, string unit) { var a = v.Min(); var b = v.Max(); return a == b ? $"{a:0.#}{unit}" : $"{a:0.#}–{b:0.#}{unit}"; }
            sb.AppendLine($"{sent.Count} layers: power {Range(sent.Select(l => l.Laser.Power), "%")}, speed {Range(sent.Select(l => l.Laser.Speed), " mm/s")}" +
                          (sent.Any(l => l.Laser.Light == LightSource.Red)
                              ? $", MOPA {Range(sent.Where(l => l.Laser.Light == LightSource.Red).Select(l => (double)l.Laser.PulseWidthNs), " ns")} at {Range(sent.Where(l => l.Laser.Light == LightSource.Red).Select(l => (double)l.Laser.FrequencyKhz), " kHz")}"
                              : "") + ".");
            sb.AppendLine($"First: {Line(sent[0])[2..]}");
            sb.AppendLine($"Last: {Line(sent[^1])[2..]}");
        }
        var opt = Options;
        sb.AppendLine();
        sb.AppendLine(opt.WholeJobPasses
            ? $"Passes run over the whole job{(opt.PassDelayS > 0 ? $", {opt.PassDelayS:0.#} s pause between" : "")}."
            : "Each path runs all its passes before the next.");
        sb.AppendLine($"Size {job.Bounds.Width:0.#} × {job.Bounds.Height:0.#} mm · about {Duration(job.EstimateS)}.");
        if (_machine.Distance is { } d) sb.AppendLine($"Machine focus distance: {d:0.0} mm. Make sure this matches your material.");
        if (VariableText.Describe(_doc.State) is { } marks) sb.AppendLine($"Fields: {marks}.");
        if (WorkModes.Usable(_settings) && _settings.WorkMode != "Flat") sb.AppendLine($"Work mode: {WorkModes.Describe(_settings)} (experimental).");
        if (_section is { } sec) sb.AppendLine($"Marking {sec.Label} only. Is the material where it should be for it?");
        if (_onlyLayer is { } only && _doc.State.FindLayer(only) is { } ol) sb.AppendLine($"Marking the layer \"{ol.Name}\" only.");
        // Step-down and focus offsets lower the head: never let it come near the material.
        double offset = job.Job.Blocks.Select(b => b.FocusOffsetMm).DefaultIfEmpty(0).Max();
        double deepest = JobBuilder.OutputLayers(_doc.State).Where(l => l.Mode is LayerMode.Cut or LayerMode.Score && (l.StepDownMm > 0 || l.StepDownFirstMm > 0))
            .Select(l => l.FocusOffsetMm + l.StepDownFirstMm + (Math.Max(1, l.Laser.Passes) - 1) * l.StepDownMm).Append(offset).Max();
        if (deepest > 0 && _machine.Distance is { } focus && deepest > focus - 3)
        {
            await Dialogs.Message(owner, "Can't send", $"Step-down and focus offsets would lower the head {deepest:0.##} mm, but it's only {focus:0.0} mm from the material. Reduce them.");
            return;
        }
        if (job.Job.Blocks.Any(b => b.FocusOffsetMm != 0))
            sb.AppendLine($"The head moves for focus offsets (down at most {Math.Max(0, offset):0.##} mm) and comes back at the end.");
        foreach (var w in job.Warnings.Except(job.LeftOut)) sb.AppendLine($"⚠ {w}");
        if (job.LeftOut.Count > 0)
        {
            // First thing in the message: things that look like part of the job but won't be done.
            var head = new System.Text.StringBuilder().AppendLine("NOT IN THIS JOB:");
            foreach (var w in job.LeftOut) head.AppendLine($"✖ {w}");
            sb.Insert(0, head.AppendLine().ToString());
        }
        if (_machine.Mode == "P_WORK")
            sb.AppendLine($"ⓘ A job is already loaded on the machine and not started. It will be replaced by this one. ({_machine.LoadedJobDescription() ?? "Loaded by an unknown program."})");
        sb.AppendLine();
        sb.Append("After upload the machine waits. Nothing moves or fires until you press its start button.");
        if (!await Dialogs.Confirm(owner, "Ship it to the machine?", sb.ToString(), "Ship It", "Cancel")) return;

        _sending = true;
        _progress.IsVisible = true;
        _progress.Value = 0;
        _sendState = "Uploading…";
        UpdateSend();
        try
        {
            var progress = new Progress<double>(f => _progress.Value = f);
            // Sending again replaces a job that's loaded but not started (never one that's running).
            await _machine.UploadJobAsync(job.Xf, progress, CancellationToken.None, replaceLoadedJob: true,
                name: _doc.FilePath is { } fp ? Path.GetFileNameWithoutExtension(fp) : "Untitled");
            _sentEstimate = job.EstimateS;
            _sentJob = job;
            _sentFrom = _doc.FilePath;
            // A section of a bigger design: the fields move on only after the last one.
            _sentMidSections = _section is { } s && s.Index < s.Count - 1;
            _sendState = "Uploaded. Press the start button on the machine to begin (long-press while paused to cancel).";
        }
        catch (DeviceException e)
        {
            _sendState = "Upload failed.";
            await Dialogs.Message(owner, "Upload failed", e.Message);
        }
        finally
        {
            _sending = false;
            _progress.IsVisible = false;
            UpdateSend();
        }
    }
}
