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
    readonly NumericUpDown _delay = Number(0, 3600, 0.5m, "0.#"), _pathPause = Number(0, (decimal)PhotonCanvas.Core.Job.MaxPathPauseS, 0.5m, "0.##");
    readonly StackPanel _delayRow, _pathRow, _passesArea;
    readonly TextBlock _pathPauseLabel = new() { Text = "s after each path", FontSize = 12, Foreground = Brush("TextMuted"), VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock _summary = new() { FontSize = 12, Foreground = Brush("TextMuted"), TextTrimming = TextTrimming.CharacterEllipsis };
    readonly TextBlock _caption = new() { FontSize = 11.5, Foreground = Brush("TextMuted"), TextTrimming = TextTrimming.CharacterEllipsis };
    readonly TextBlock _estimate = new() { FontSize = 22, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Right };
    readonly JobTimeline _timeline = new();
    readonly Button _checks = new() { Classes = { "chip" } };
    readonly StackPanel _messages = new() { Spacing = 8, Width = 380 };
    readonly ToggleButton _order = new() { Classes = { "chip" } };
    // Spread the heat across the job's layers and objects (kept with the design).
    readonly Button _heat = new() { Classes = { "chip" } };
    readonly CheckBox _heatOn = new() { Content = "Spread the heat across layers and objects" };
    readonly ComboBox _heatSheet = new() { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = SheetMetal.All.Select(m => m.Name).ToList() };
    readonly ComboBox _heatHeld = new() { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = SpreadHeat.Backings.Select(b => b.Name).ToList() };
    readonly NumericUpDown _heatThick = Number(0.05m, 5, 0.05m, "0.0#"), _heatLimit = Number(1, 200, 1, "0"), _heatCool = Number(0, (decimal)SpreadHeat.MaxCooldownS, 1, "0.#");
    readonly TextBlock _heatSays = Hint(), _heatResult = Hint();
    bool _heatLoading;
    readonly Button _send = new() { Classes = { "primary" }, Width = 140, Height = 48, Padding = new(12, 0), CornerRadius = new(8), VerticalContentAlignment = VerticalAlignment.Center };
    readonly ProgressBar _progress = new() { Minimum = 0, Maximum = 1, IsVisible = false, Height = 4, Margin = new(0, 3, 0, 0) };
    // Working out a heat order (the finer model takes a while): how far it's got, so it doesn't look stuck.
    readonly ProgressBar _planBar = new() { Minimum = 0, Maximum = 1, MinWidth = 40, Height = 6, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new(8, 0, 6, 0) };
    readonly TextBlock _planText = new() { FontSize = 11.5, Foreground = Brush("TextMuted"), VerticalAlignment = VerticalAlignment.Center };
    readonly Grid _planRow = new() { ColumnDefinitions = new("Auto,*"), IsVisible = false };
    CancellationTokenSource? _buildCancel;
    readonly System.Diagnostics.Stopwatch _buildClock = new();
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
    // The machine's id for the job sent from here: another job on the machine isn't ours to follow.
    string? _sentTaskId;
    double? _machineElapsed;
    DateTime _machineElapsedAt;
    // The machine's own progress (0–1) at its last report, while it gives one.
    double? _machineFraction;
    // A stopped rotary job left the object turned: how far, and the button that turns it back.
    readonly Button _rotaryBack = new() { Classes = { "chip" }, IsVisible = false };
    readonly TextBlock _rotaryBackSays = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12, Width = 300 };

    /// <summary>
    /// The job sent from here while it's the one on the machine; null once the machine has another
    /// (from the phone, xTool Studio or its own screen), so that one isn't followed, timed or logged as
    /// this design. Firmware that doesn't report job ids: the last job sent, as before.
    /// </summary>
    BuiltJob? OwnJob => IsOurs ? _sentJob : null;

    /// <summary>The last job sent from here in this session, and when (for add-ons).</summary>
    public (BuiltJob Job, DateTimeOffset SentAt)? LastSent { get; private set; }
    double? OwnEstimate => IsOurs ? _sentEstimate : null;
    bool IsOurs => _sentTaskId is { } id && (_machine.LoadedTaskId is not { Length: > 0 } loaded || loaded == id);

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
    // Getting ready to ship (building the job if it isn't built yet): the button greyed, "Shipping it" with dots going round.
    bool _preparing;
    readonly DispatcherTimer _shipDots = new() { Interval = TimeSpan.FromMilliseconds(300) };
    int _dots;
    readonly PathIcon _sendIcon = new() { Width = 17, Height = 17, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center };
    // The design and settings the shown job was built from, and the build under way: Ship It uses them rather than building again.
    (DocState State, JobOptions Options)? _jobFor;
    (DocState State, JobOptions Options, Task<BuiltJob> Build)? _pending;
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
                _sendIcon,
                _sendText,
            },
        };
        _sendIcon.Data = Icon("IcSend");
        _shipDots.Tick += (_, _) => _sendText.Text = "Shipping it" + new string('.', _dots = (_dots + 1) % 6);
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
        _pathPause.Value = (decimal)Math.Clamp(settings.PathPauseS, 0, PhotonCanvas.Core.Job.MaxPathPauseS);
        foreach (var n in new[] { _delay, _pathPause })
        {
            n.Width = 52;
            n.ShowButtonSpinner = false;
            n.HorizontalContentAlignment = HorizontalAlignment.Center;
            n.MinHeight = 22;
            n.Height = 24;
            n.FontSize = 12;
        }
        ToolTip.SetTip(_delay, "Seconds to wait between rounds, e.g. to let the material cool or to check progress.");
        ToolTip.SetTip(_pathPause, "Seconds to wait after each cut or score path, laser off, so heat can leave before the next one (thin metal, masks, anything that warps). After every pass of it, round by round or path by path. The checks say how much time it adds.");
        TextBlock Muted(string t) => new() { Text = t, FontSize = 12, Foreground = Brush("TextMuted"), VerticalAlignment = VerticalAlignment.Center };
        _delayRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { Muted("pause"), _delay, Muted("s between rounds") } };
        _pathRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { Muted("·"), _pathPause, _pathPauseLabel } };
        _passMode.SelectionChanged += (_, _) => { _settings.WholeJobPasses = _passMode.SelectedIndex == 0; _settings.Save(); UpdateDelayRow(); Schedule(); };
        _delay.ValueChanged += (_, _) => { _settings.PassDelayS = (double)(_delay.Value ?? 0); _settings.Save(); Schedule(); };
        _pathPause.ValueChanged += (_, _) => { _settings.PathPauseS = (double)(_pathPause.Value ?? 0); _settings.Save(); Schedule(); };
        ToolTip.SetTip(_estimate, "Estimated time, from how the machine was timed doing the same kinds of work (Help: The estimate).");
        var passes = _passesArea = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 6, Margin = new(12, 0, 0, 0),
            Children = { _passesLabel, _passMode, _delayRow, _pathRow },
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
        ToolTip.SetTip(_order, "Show the processing order on the work area: paths coloured from first (blue) to last (red), with a key, and the travel moves between them on small jobs.");
        _order.IsCheckedChanged += (_, _) => _canvas.Preview = _order.IsChecked == true ? _job : null;
        var more = _export;
        var exportXf = new MenuItem { Header = "Job package for the machine (.xf)…" };
        exportXf.Click += async (_, _) => await Export(true);
        var exportGc = new MenuItem { Header = "G-code as text (.gcode)…" };
        exportGc.Click += async (_, _) => await Export(false);
        more.Flyout = new MenuFlyout { Placement = PlacementMode.TopEdgeAlignedRight, Items = { exportXf, exportGc } };
        ToolTip.SetTip(more, "Save the job to a file instead of sending it.");
        _heat.Flyout = new Flyout
        {
            Placement = PlacementMode.TopEdgeAlignedRight,
            Content = new StackPanel
            {
                Spacing = 6, Width = 340,
                Children =
                {
                    new TextBlock { Text = "SPREAD THE HEAT", Classes = { "h" } },
                    _heatOn,
                    Hint("Each layer, and each separate shape on an engrave or picture layer, runs where the plate is coolest right now, so a hot square never starts on a spot its neighbour just warmed. " +
                         "Waits are added only where the plate needs them. For thin metal that warps and goes out of focus, like MOPA colour tests."),
                    Field("Sheet metal", _heatSheet),
                    Row(Field("Thickness (mm)", _heatThick), Field("Keep warming under (°C)", _heatLimit, tip: "How much warmer than at the start any patch of the plate may get, averaged over a few mm. 25 °C keeps thin stainless flat and in focus.")),
                    Field("How it's held", _heatHeld, tip: "What's under the plate decides how fast heat leaves it. A plate taped or clamped to a metal block cools many times faster than one lying on the honeycomb (rough figures)."),
                    Field("Cool down after each part (s)", _heatCool, tip: "Wait at least this long after every part, laser off, whatever the heat model says. 0 = only the waits the heat needs."),
                    _heatSays,
                    _heatResult,
                },
            },
        };
        ToolTip.SetTip(_heat, "Spread the heat: run the job's layers and objects where the plate is coolest, with waits where it needs them (thin metal that warps).");
        _heat.Flyout.Opened += (_, _) => LoadHeat();
        _heatOn.IsCheckedChanged += (_, _) => HeatChanged();
        _heatSheet.SelectionChanged += (_, _) => HeatChanged();
        _heatHeld.SelectionChanged += (_, _) => HeatChanged();
        foreach (var n in new[] { _heatThick, _heatLimit, _heatCool }) n.ValueChanged += (_, _) => HeatChanged();
        var turnBack = new Button { Content = "Turn it back", Classes = { "primary" }, HorizontalAlignment = HorizontalAlignment.Stretch };
        var already = new Button { Content = "It's already back", HorizontalAlignment = HorizontalAlignment.Stretch };
        _rotaryBack.Flyout = new Flyout
        {
            Placement = PlacementMode.TopEdgeAlignedRight,
            Content = new StackPanel { Spacing = 8, Children = { _rotaryBackSays, Row(already, turnBack) } },
        };
        turnBack.Click += async (_, _) =>
        {
            _rotaryBack.Flyout.Hide();
            if (TopLevel.GetTopLevel(this) is Window w) await RotaryTurn.ReturnAsync(w, _settings, _machine);
            UpdateRotaryBack();
        };
        already.Click += (_, _) =>
        {
            _rotaryBack.Flyout.Hide();
            _settings.RotaryLeftU = null;
            _settings.Save();
            UpdateRotaryBack();
        };
        ToolTip.SetTip(_rotaryBack, "A rotary job was stopped part way, so the object is still turned. Click to turn it back to where that job started.");
        UpdateRotaryBack();
        var chips = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new(8, 0, 0, 0), Children = { _rotaryBack, _checks, _order, _heat, more } };
        foreach (var c in new Control[] { _rotaryBack, _checks, _order, _heat, more }) { c.Height = 24; ((TemplatedControl)c).Padding = new(8, 0); }

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
        _planRow.Children.Add(_planText);
        Grid.SetColumn(_planBar, 1);
        _planRow.Children.Add(_planBar);
        foot.Children.Add(new Panel { Children = { _caption, _progress, _planRow } });
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
        ShowHeatChip();
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

    public bool CanSend => !_sending && !_preparing && _job is not null && (_machine.IsConnected || _settings.ActiveSetup().Kind == MachineKind.Grbl);
    /// <summary>Framing is the xTool machine's: a GRBL machine frames from its own Send window.</summary>
    public bool CanFrame => _machine.IsFraming || CanSend && _settings.ActiveSetup().Kind != MachineKind.Grbl;

    void UpdateDelayRow() => _delayRow.IsVisible = _passMode.SelectedIndex == 0;

    // The saved settings (shared with the remote server), with this bar's own controls and choices.
    JobOptions Options => JobSettings.From(_settings) with
    {
        WholeJobPasses = _passMode.SelectedIndex == 0,
        PassDelayS = (double)(_delay.Value ?? 0),
        PathPauseS = (double)(_pathPause.Value ?? 0),
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
    /// <summary>A build under way, once it's done; null if it failed or was stopped (then Ship It builds its own).</summary>
    static async Task<BuiltJob?> Finished(Task<BuiltJob> build)
    {
        try { return await build; }
        catch (Exception e) when (e is JobException or OperationCanceledException) { return null; }
    }

    /// <summary>Ready (or given up): the button back to "Ship It".</summary>
    void DoneShipping()
    {
        if (!_preparing) return;
        _preparing = false;
        _shipDots.Stop();
        _sendText.Text = "Ship It";
        _sendIcon.IsVisible = true;
        UpdateSend();
    }

    Task<BuiltJob> BuildNow()
    {
        var state = _doc.State;
        var opt = Options;
        return Task.Run(() => JobBuilder.Build(state, opt));
    }

    /// <summary>Layers that spread the heat work out their own waits and passes: settings that don't touch them aren't shown.</summary>
    void UpdatePassControls()
    {
        if (_pathRow is null) return; // still being built
        var working = _doc.State.Layers.Where(l => l.Output && _doc.State.ShapesFor(l).Any()).ToList();
        static bool Heat(Layer l) => l.Order == PathOrder.Heat && l.Mode is LayerMode.Cut or LayerMode.Score;
        var cutting = working.Where(l => l.Mode is LayerMode.Cut or LayerMode.Score).ToList();
        bool someHeat = cutting.Any(Heat);
        _pathRow.IsVisible = !(cutting.Count > 0 && cutting.All(Heat));
        _passesArea.IsVisible = !(working.Count > 0 && working.All(Heat));
        _pathPauseLabel.Text = someHeat ? "s after each path*" : "s after each path";
        ToolTip.SetTip(_pathPauseLabel, someHeat ? "*Not on layers that spread the heat: they work out their own waits." : null);
    }

    /// <summary>The heat chip says whether it's on.</summary>
    void ShowHeatChip()
    {
        bool on = _doc.State.Spread.On;
        _heat.Content = _compact == true ? Chip("IcBolt", on ? "On" : null) : Chip("IcBolt", on ? "Heat: spread" : "Heat");
    }

    /// <summary>The heat settings from the design (opening the flyout, undo).</summary>
    void LoadHeat()
    {
        var h = _doc.State.Spread;
        _heatLoading = true;
        _heatOn.IsChecked = h.On;
        _heatSheet.SelectedIndex = SheetMetal.All.ToList().FindIndex(m => m.Name == SheetMetal.Find(h.Sheet).Name);
        _heatHeld.SelectedIndex = SpreadHeat.Backings.ToList().FindIndex(b => b.Backing == h.Backing);
        void Set(NumericUpDown n, double v) { if (n.Value != (decimal)v) n.Value = Math.Clamp((decimal)v, n.Minimum, n.Maximum); }
        Set(_heatThick, h.ThicknessMm);
        Set(_heatLimit, h.LimitC);
        Set(_heatCool, h.CooldownS);
        _heatLoading = false;
        ShowHeatText(h);
    }

    void HeatChanged()
    {
        if (_heatLoading) return;
        var old = _doc.State.Spread;
        var h = old with
        {
            On = _heatOn.IsChecked == true,
            Sheet = SheetMetal.All[Math.Max(0, _heatSheet.SelectedIndex)].Name,
            ThicknessMm = (double)(_heatThick.Value ?? 0.5m),
            LimitC = (double)(_heatLimit.Value ?? 25),
            Backing = SpreadHeat.Backings[Math.Max(0, _heatHeld.SelectedIndex)].Backing,
            CooldownS = (double)(_heatCool.Value ?? 0),
        };
        ShowHeatText(h);
        if (h == old) return;
        _doc.Commit(h.On == old.On ? "Spread the heat settings" : h.On ? "Spread the heat" : "Spread the heat off", _doc.State with { Spread = h }, coalesceKey: h.On == old.On ? "spreadheat" : null);
    }

    /// <summary>What the settings mean, spelled out: how long this plate takes to lose its heat on its own.</summary>
    void ShowHeatText(SpreadHeat h)
    {
        foreach (var c in new Control[] { _heatSheet, _heatThick, _heatLimit, _heatHeld, _heatCool }) c.IsEnabled = h.On;
        var metal = SheetMetal.Find(h.Sheet);
        double tau = metal.HeatPerMm3K * Math.Max(0.01, h.ThicknessMm) / h.LossWmm2K;
        string held = SpreadHeat.Backings.First(b => b.Backing == h.Backing).Name.ToLowerInvariant();
        _heatSays.Text = h.On
            ? $"{h.ThicknessMm:0.0#} mm {metal.Name.ToLowerInvariant()} {held}: a warm patch loses about two thirds of its heat in {TimeText.Duration(tau)} (rough figure). " +
              $"No patch should get more than {h.LimitC:0} °C warmer than at the start." + (h.CooldownS > 0 ? $" At least {h.CooldownS:0.#} s between parts." : "")
            : "Off: layers run top to bottom, as the layer list shows.";
    }

    void Schedule()
    {
        UpdatePassControls();
        ShowHeatChip();
        _debounce.Stop();
        _debounce.Start();
    }

    /// <summary>Build off the UI thread (document states are immutable), keep only the newest result.</summary>
    async Task Rebuild()
    {
        int v = ++_version;
        var state = _doc.State;
        // A build still working out a heat order for an older design: stop it.
        _buildCancel?.Cancel();
        var cts = _buildCancel = new CancellationTokenSource();
        _buildClock.Restart();
        var plain = Options;
        var opt = plain with
        {
            Cancel = cts.Token,
            HeatProgress = f => Avalonia.Threading.Dispatcher.UIThread.Post(() => { if (v == _version) ShowPlanning(f); }),
        };
        BuiltJob? job = null;
        string? error = null;
        var build = Task.Run(() => JobBuilder.Build(state, opt));
        _pending = (state, plain, build);
        try { job = await build; }
        catch (JobException e) { error = e.Message; }
        catch (OperationCanceledException) { return; }
        if (v != _version) return;
        ShowPlanning(null);
        _job = job;
        _jobFor = (state, plain);
        _error = error;
        Show();
        JobBuilt?.Invoke(job);
        if (_machine.IsFraming) _ = UpdateFrame();
    }

    /// <summary>The heat order's progress under the timeline (after a moment, so quick builds don't flash it); null hides it.</summary>
    void ShowPlanning(double? done)
    {
        bool show = done is not null && _buildClock.ElapsedMilliseconds > 400;
        _planRow.IsVisible = show;
        _caption.IsVisible = !show;
        if (done is not { } f) return;
        _planBar.Value = f;
        _planText.Text = $"Working out the heat order… {f:P0}";
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
            int layers = j.LayerTimes.Select(t => t.LayerId).Distinct().Count();
            int rounds = j.LayerTimes.Count == 0 ? 1 : j.LayerTimes.Max(t => t.Passes);
            _summary.Text = (_section is { } sec ? $"{char.ToUpper(sec.Label[0])}{sec.Label[1..]} · " : "") +
                            (_onlyLayer is { } ol && _doc.State.FindLayer(ol) is { } oll ? $"Only {oll.Name} · " : "") + (WorkModes.Usable(_settings) && _settings.WorkMode != "Flat" ? $"{_settings.WorkMode} · " : "") +
                            $"{layers} layer{(layers == 1 ? "" : "s")} · {j.Paths:N0} path{(j.Paths == 1 ? "" : "s")}" + (j.Steps.Count > j.Paths ? $" ({j.Steps.Count:N0} cuts with the passes)" : "") +
                            (j.RoundDelayS is not null && rounds > 1 ? $" · {rounds} rounds" : "") +
                            $" · {j.Bounds.Width:0.#} × {j.Bounds.Height:0.#} mm at {j.Bounds.MinX:0.#}, {j.Bounds.MinY:0.#}";
            foreach (var w in j.Warnings) _messages.Children.Add(Message(w, "Warn"));
            _heatResult.Text = string.Join("\n", j.Warnings.Where(w => w.StartsWith("Spread the heat", StringComparison.Ordinal)));
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
        UpdatePassControls();
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
            _sendState = !_machine.IsConnected && _settings.ActiveSetup().Kind != MachineKind.Grbl ? "Connect the machine (right end of the ribbon) to send or frame."
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
                if (OwnEstimate is { } est) _runTimes += $" About {Clock(est)} once started.";
                break;
            case "P_WORKING":
                ShowRunning(true);
                if (_pausedAt is { } p) _pausedFor += (DateTime.Now - p).TotalSeconds;
                _pausedAt = null;
                if (_startedAt is null) RunStarted?.Invoke(OwnJob is null ? "Job" : _sentFrom is { } nm ? Path.GetFileNameWithoutExtension(nm) : "Untitled");
                _startedAt ??= DateTime.Now;
                _machineElapsedAt = DateTime.Now;
                if (OwnJob is not null) _follow.Start();
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
                    if (OwnJob is { } sj)
                    {
                        JobHistory.Add(new JobRecord(DateTime.Now, _sentFrom is { } f ? Path.GetFileNameWithoutExtension(f) : "Untitled", sj.EstimateS, took, finished, sj.LayerTimes.Select(t => t.LayerId).Distinct().Count(), sj.Bounds.Width, sj.Bounds.Height));
                        if (finished) Chime();
                    }
                    if (finished && OwnJob is not null && !_sentMidSections) RunFinished?.Invoke(_sentFrom);
                }
                if (_startedAt is not null && mode is not ("P_WORK_DONE" or "P_FINISH")) NoteRotaryLeft();
                if (_startedAt is not null) RunEnded?.Invoke(mode is "P_WORK_DONE" or "P_FINISH");
                _follow.Stop();
                _canvas.ShowRun(null, null);
                _machineElapsed = null;
                _machineFraction = null;
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
                if (fraction is { } mf) _machineFraction = mf;
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
        else if (OwnEstimate is { } est) remaining = Math.Max(0, est * Math.Clamp(TimeScale(), 0.3, 5) - elapsed);
        _timeline.Progress = fraction ?? (OwnEstimate is { } e2 && e2 > 0 ? Math.Min(0.99, elapsed / (e2 * Math.Clamp(TimeScale(), 0.3, 5))) : 0);
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

    /// <summary>
    /// A rotary job of ours ended part way: the object is left turned (the job's end, which turns it
    /// back, didn't run). Where it got to: the machine's own progress if it gives one, else the time.
    /// </summary>
    void NoteRotaryLeft()
    {
        if (OwnJob is not { Job.Mode: WorkMode.Rotary rot } job) return;
        double? u = _machineFraction is { } f and > 0
            ? RotaryReturn.UAtFraction(job, f)
            : RotaryReturn.UAt(job, RunningSeconds() / Math.Clamp(TimeScale(), 0.3, 5));
        if (u is not { } left || Math.Abs(left) < RotaryReturn.MinTurnMm) return;
        RotaryReturn.NoteLeft(_settings, left, rot);
        _settings.Save();
        UpdateRotaryBack();
        if (_settings.RotaryLeftU is not null)
            _sendState = $"The job ended. The rotary is still turned about {RotaryTurn.Describe(_settings)}: click Rotary {RotaryTurn.Describe(_settings)} to turn it back.";
    }

    /// <summary>Show or hide the "turn the rotary back" chip.</summary>
    void UpdateRotaryBack()
    {
        _rotaryBack.IsVisible = _settings.RotaryLeftU is not null;
        if (!_rotaryBack.IsVisible) return;
        string deg = RotaryTurn.Describe(_settings);
        _rotaryBack.Content = ChipText("IcRotary", $"Rotary {deg}", "Warn");
        _rotaryBackSays.Text = $"A rotary job was stopped part way, so the object is still turned about {deg} from where it started " +
            "(worked out from how far the job had got, so it can be a little out). Turn it back now (laser off), " +
            "or say it's already back if you turned it by hand.";
    }

    /// <summary>Move the "laser is here" marker on the work area.</summary>
    void Follow()
    {
        if (OwnJob is not { } job || _startedAt is null) return;
        // Scale real time back onto the estimate, by what past jobs showed.
        double est = RunningSeconds() / Math.Clamp(TimeScale(), 0.3, 5);
        _canvas.ShowRun(job, RunTracker.At(job, est));
    }

    /// <summary>After a finished job: how much longer it took than estimated, blended into what's learned.</summary>
    void LearnTiming(double tookSeconds)
    {
        if (OwnJob is not { EstimateS: > 10 } job) return;
        double ratio = Math.Clamp(tookSeconds / job.EstimateS, 0.5, 4);
        _settings.JobTimeScale = Math.Round(TimeScale() * 0.6 + ratio * 0.4, 3);
        _settings.Save();
    }

    /// <summary>What's been learned, started again at 1 when the estimate itself changed.</summary>
    double TimeScale()
    {
        const int Model = 2;
        if (_settings.JobTimeModel != Model)
        {
            _settings.JobTimeScale = 1;
            _settings.JobTimeModel = Model;
            _settings.Save();
        }
        return _settings.JobTimeScale;
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
        else
        {
            // Written as it is: a big wobble job is more G-code than one string holds.
            await using var gcode = File.Create(path);
            job.MotionText.WriteTo(gcode);
        }
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

    /// <summary>A job started running on the machine (any job, also one started there): its name.</summary>
    public event Action<string>? RunStarted;

    /// <summary>A job that was running stopped: true when it finished, false when it was stopped or ended otherwise.</summary>
    public event Action<bool>? RunEnded;

    string? _sentFrom;
    bool _sentMidSections;

    /// <param name="quick">Quick processing: no confirmation when the job has nothing to warn about (the machine still waits for its button).</param>
    public async Task Send(bool quick = false)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner || _sending) return;
        // Dates and times as of now (fields are otherwise filled in when the design changes).
        var fresh = VariableText.Refresh(_doc.State, DateTime.Now);
        if (!ReferenceEquals(fresh, _doc.State)) _doc.Commit("Update date and time", fresh);
        BuiltJob job;
        _preparing = true;
        _dots = 0;
        _sendText.Text = "Shipping it";
        _sendIcon.IsVisible = false;
        _shipDots.Start();
        UpdateSend();
        try
        {
            // Already built for this very design and settings (for the estimate), or being built: use that.
            var plain = Options;
            if (_job is { } built && _jobFor is { } f && ReferenceEquals(f.State, _doc.State) && f.Options == plain) job = built;
            else if (_pending is { } p && ReferenceEquals(p.State, _doc.State) && p.Options == plain && await Finished(p.Build) is { } done) job = done;
            else job = await BuildNow();
        }
        catch (JobException e)
        {
            DoneShipping();
            await Dialogs.Message(owner, "Can't send", e.Message);
            return;
        }
        finally { DoneShipping(); }
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

        var review = JobChecks.Review(_doc.State, job, Options, _settings, _machine.Distance);
        if (review.Refusal is { } refusal)
        {
            await Dialogs.Message(owner, "Can't send", refusal);
            return;
        }
        var sb = new StringBuilder(JobChecks.Text(review));
        if (_machine.Mode == "P_WORK")
            sb.AppendLine($"ⓘ A job is already loaded on the machine and not started. It will be replaced by this one. ({_machine.LoadedJobDescription() ?? "Loaded by an unknown program."})");
        sb.AppendLine();
        sb.Append("After upload the machine waits. Nothing moves or fires until you press its start button (or start it from the phone, if you've allowed that in Remote access).");
        if ((!quick || job.Warnings.Count > 0) && !await Dialogs.Confirm(owner, "Ship it to the machine?", sb.ToString(), "Ship It", "Cancel")) return;

        _sending = true;
        _progress.IsVisible = true;
        _progress.Value = 0;
        _sendState = "Uploading…";
        UpdateSend();
        try
        {
            var progress = new Progress<double>(f => _progress.Value = f);
            // Sending again replaces a job that's loaded but not started (never one that's running).
            _sentTaskId = await _machine.UploadJobAsync(job.Xf, progress, CancellationToken.None, replaceLoadedJob: true,
                name: _doc.FilePath is { } fp ? Path.GetFileNameWithoutExtension(fp) : "Untitled");
            _sentEstimate = job.EstimateS;
            _sentJob = job;
            LastSent = (job, DateTimeOffset.Now);
            _sentFrom = _doc.FilePath;
            // A section of a bigger design: the fields move on only after the last one.
            _sentMidSections = _section is { } s && s.Index < s.Count - 1
                // Layer by layer: the piece is done after its last layer, not each one.
                || _onlyLayer is { } onlyOne && JobBuilder.OutputLayers(_doc.State).Where(l => l.ParentId is null && _doc.State.ShapesFor(l).Any()).LastOrDefault()?.Id != onlyOne;
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
