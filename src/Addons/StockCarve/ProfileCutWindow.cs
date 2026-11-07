using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using PhotonCanvas.App;
using PhotonCanvas.App.Addons;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Materials;
using static PhotonCanvas.App.UiKit;
using Tri = PhotonCanvas.Doc.Import.StlDepth.Triangle;
using Bitmap = Avalonia.Media.Imaging.Bitmap;
using PathGeometry = PhotonCanvas.Doc.PathGeometry;

namespace PhotonCanvas.Addon.StockCarve;

/// <summary>Where the profile cuts had got to, and their settings (kept in the add-on's data folder).</summary>
public sealed record ProfileSaved
{
    public ProfileOptions Options { get; init; } = new();
    public string? Recipe { get; init; }
    /// <summary>The model and where it was, for telling whether the progress below is for it.</summary>
    public string? ModelFile { get; init; }
    public Placement? Placement { get; init; }
    public int Step { get; init; }
    /// <summary>Which way the part is turned now (degrees from where the scan started).</summary>
    public double AtDeg { get; init; }
    /// <summary>The views cut through so far (degrees).</summary>
    public List<double> Done { get; init; } = [];
}

/// <summary>
/// Profile cuts: the part held still in the rotary, turned to each view in turn, and cut straight down through round
/// the model's outline seen that way, so the waste drops off before the carving. Between cuts the camera (laser off)
/// compares the part before and after, to tell whether the cut went through or needs more passes.
/// </summary>
public sealed class ProfileCutWindow : Window
{
    readonly IAddonHost _host;
    readonly StockMap _stock;
    readonly IReadOnlyList<Tri> _placed;
    readonly Placement _placement;
    readonly string? _modelFile;
    readonly Action<StockMap, string> _carveFrom;

    List<ProfileStep> _steps = [];
    int _index;
    double _atDeg;
    readonly List<double> _done = [];
    Remaining? _remaining;
    double _wholeMm3;
    ProfilePlan? _plan;
    bool _loading, _waiting;
    CancellationTokenSource? _work;
    (byte[] Grey, int W, int H, Rect2 Area, Bitmap Shown)? _before;

    readonly ComboBox _views = new() { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = new[] { "Top and side", "Top, side and the 45s", "Top, side, the 45s and the 22.5s" } };
    readonly CheckBox _bothSides = new() { Content = "Cut each view from both sides", IsChecked = true };
    readonly NumericUpDown _start = Number(-360, 360, 5, "0.#");
    readonly ComboBox _chuck = new() { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = new[] { "Left end (low X)", "Right end (high X)" } };
    readonly NumericUpDown _core = Number(1, 60, 0.5m, "0.#");
    readonly CheckBox _bothEnds = new() { Content = "Keep a bar to the other end too (a support holds it)" };
    readonly ComboBox _with = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly NumericUpDown _passDepth = Number(0.01m, 5, 0.05m, "0.0#"), _focusStep = Number(0.2m, 10, 0.5m, "0.#"), _trench = Number(0.1m, 6, 0.1m, "0.0#"),
        _gap = Number(0.02m, 1, 0.01m, "0.0#"), _leave = Number(0, 5, 0.1m, "0.0#"), _overlap = Number(0, 5, 0.25m, "0.0#");
    readonly NumericUpDown _now = Number(-720, 720, 1, "0.#");
    readonly List<(Material M, Recipe R)> _cuts;
    readonly ProfileView _view = new();
    readonly TextBlock _stepText = new() { FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
    readonly TextBlock _planText = new() { TextWrapping = TextWrapping.Wrap };
    readonly TextBlock _checkText = new() { TextWrapping = TextWrapping.Wrap };
    readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Classes = { "muted" } };
    readonly Image _beforeImg = new() { Stretch = Stretch.Uniform, MaxHeight = 150 }, _afterImg = new() { Stretch = Stretch.Uniform, MaxHeight = 150 };
    readonly Button _turn = new() { Content = "Turn to this view", MinWidth = 150 };
    readonly Button _send = new() { Content = "Send the cut", Classes = { "primary" }, MinWidth = 130 };
    readonly Button _check = UiKit.Button("Check with the camera", tip: "Takes a photo of the part (nothing moves, the laser stays off) and compares it with the one from before the cut.");
    readonly Button _more = UiKit.Button("More passes", tip: "Sends the deepest two focus heights of this cut again.");
    readonly Button _next = UiKit.Button("It's through: next", tip: "The waste has dropped away: on to the next view.");
    readonly Button _skip = UiKit.Button("Skip", tip: "Leave this cut out and go on to the next.");
    readonly Button _back = UiKit.Button("Back", tip: "Back to the cut before.");
    readonly Button _restart = UiKit.Button("Start over", tip: "Forget the cuts made so far (the part as scanned again).");
    readonly Button _carve = UiKit.Button("Carve what's left", tip: "Hands what's left after the views cut through so far to Carve to model, as its stock, so the carving starts from there.");
    readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(200) };

    public ProfileCutWindow(IAddonHost host, StockMap stock, IReadOnlyList<Tri> placed, Placement placement, string? modelFile, Action<StockMap, string> carveFrom)
    {
        _host = host;
        _stock = stock;
        _placed = placed;
        _placement = placement;
        _modelFile = modelFile;
        _carveFrom = carveFrom;
        Title = "Profile cuts";
        Width = 1150;
        Height = 820;
        MinWidth = 880;
        MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");

        _cuts = [.. (host.Library?.Materials ?? []).SelectMany(m => m.Recipes.Where(r => r.Settings.Mode == LayerMode.Cut).Select(r => (m, r)))];
        _with.ItemsSource = _cuts.Select(x => $"{x.M.Label} · {x.R.Title}").Prepend("Default cut settings").ToList();
        _with.SelectedIndex = 0;

        var top = UiKit.Button("The model's top", tip: "Start with the view looking down on the model's top, as it's placed in the stock.");
        top.Click += (_, _) => _start.Value = (decimal)Math.Round(ProfileCut.Wrap180(_placement.SpinDeg), 1);

        var left = new StackPanel
        {
            Width = 404,
            Spacing = 12,
            Children =
            {
                Explained("Views", "The part turns to each view and the laser cuts straight down through it round the model's outline seen that way. The waste outside drops off.",
                    Field("Views", _views),
                    _bothSides,
                    Field("First view (°)", Row(_start, top), tip: "Which way round the first view looks (degrees from where the scan started). The next is a quarter turn on, then the 45s and the 22.5s.")),
                Explained("Holding it", "A bar is kept from the chuck to the model so it stays held while everything round it is cut away.",
                    Field("Chuck holds", _chuck),
                    Field("Bar across (mm)", _core, tip: "How wide (and deep) the bar kept from the chuck to the model is."),
                    _bothEnds),
                Explained("Cut", "Cut settings that cut well into the material. Deep cuts are made a focus height at a time, the head coming down as the cut goes deeper.",
                    Field("Cut with", _with, tip: "Settings from the material library (cut recipes). The passes are set here."),
                    Field("Depth a pass (mm)", _passDepth, tip: "About how deep one pass of these settings cuts. Each focus height gets enough passes to cut its depth."),
                    Field("Focus step (mm)", _focusStep, tip: "How far the focus comes down between focus heights."),
                    Field("Cut width (mm)", _trench, tip: "Lines side by side round the outline, out into the waste, so a deep cut is wide enough for the beam to reach its bottom."),
                    Field("Line gap (mm)", _gap, tip: "How far apart those lines are."),
                    Field("Leave on (mm)", _leave, tip: "Left outside the model's outline, for the carving to take off."),
                    Field("Past the middle (mm)", _overlap, tip: "Cutting from both sides: how far past the middle each cut goes, so the two meet.")),
                Explained("The part now", "Which way the part is turned now, from where the scan started. Change it if you turned it by hand.",
                    Field("Turned (°)", _now)),
            },
        };
        _passDepth.Value = 0.25m; _focusStep.Value = 2; _trench.Value = 1.2m; _gap.Value = 0.15m; _leave.Value = 0.5m; _overlap.Value = 1; _core.Value = 8;
        _views.SelectedIndex = 2;
        _chuck.SelectedIndex = 0;
        var leftScroll = new ScrollViewer
        {
            Width = 440, Padding = new Thickness(14, 10, 16, 10), Content = left,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };

        var photos = new Grid { ColumnDefinitions = new("*,*"), ColumnSpacing = 8 };
        photos.Children.Add(new StackPanel { Spacing = 2, Children = { Muted("Before the cut"), _beforeImg } });
        var afterBox = new StackPanel { Spacing = 2, Children = { Muted("After"), _afterImg } };
        Grid.SetColumn(afterBox, 1);
        photos.Children.Add(afterBox);

        var actions = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 8, LineSpacing = 8 };
        foreach (var b in new Control[] { _turn, _send, _check, _more, _next, _skip, _back, _restart }) actions.Children.Add(b);

        var info = new StackPanel { Spacing = 8, Children = { _stepText, _planText, actions, _checkText, photos } };
        var right = new Grid { RowDefinitions = new("*,Auto"), Margin = new Thickness(0, 10, 14, 0), RowSpacing = 8 };
        right.Children.Add(new Border { BorderBrush = Brush("Line"), BorderThickness = new(1), CornerRadius = new(6), ClipToBounds = true, Child = _view });
        var infoScroll = new ScrollViewer { Content = info, MaxHeight = 380, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(infoScroll, 1);
        right.Children.Add(infoScroll);

        var bottom = new Grid { ColumnDefinitions = new("*,Auto,Auto"), Margin = new Thickness(14, 8, 14, 14), ColumnSpacing = 12 };
        bottom.Children.Add(_status);
        Grid.SetColumn(_carve, 1);
        bottom.Children.Add(_carve);
        var close = new Button { Content = "Close", IsCancel = true, MinWidth = 100 };
        close.Click += (_, _) => Close();
        Grid.SetColumn(close, 2);
        bottom.Children.Add(close);

        var body = new Grid { ColumnDefinitions = new("Auto,*"), RowDefinitions = new("*,Auto") };
        body.Children.Add(leftScroll);
        Grid.SetColumn(right, 1);
        body.Children.Add(right);
        Grid.SetRow(bottom, 1);
        Grid.SetColumnSpan(bottom, 2);
        body.Children.Add(bottom);
        Content = body;

        _debounce.Tick += (_, _) => { _debounce.Stop(); Replan(); };
        foreach (var n in new[] { _passDepth, _focusStep, _trench, _gap, _leave, _overlap, _core }) n.ValueChanged += (_, _) => Changed();
        _start.ValueChanged += (_, _) => { if (!_loading) { MakeSteps(); Changed(); } };
        _views.SelectionChanged += (_, _) => { if (!_loading) { MakeSteps(); Changed(); } };
        _bothSides.IsCheckedChanged += (_, _) => { if (!_loading) { MakeSteps(); Changed(); } };
        _chuck.SelectionChanged += (_, _) => Changed();
        _bothEnds.IsCheckedChanged += (_, _) => Changed();
        _with.SelectionChanged += (_, _) => Save();
        _now.ValueChanged += (_, _) => { if (!_loading) { _atDeg = (double)(_now.Value ?? 0); Save(); UpdateButtons(); } };
        _turn.Click += async (_, _) => await TurnToView();
        _send.Click += async (_, _) => await SendCut(null);
        _check.Click += async (_, _) => await Check();
        _more.Click += async (_, _) => { if (_plan is { } p) await SendCut(ProfileCut.Deepest(p, 2).ToList()); };
        _next.Click += (_, _) => Through();
        _skip.Click += (_, _) => GoTo(_index + 1);
        _back.Click += (_, _) => GoTo(_index - 1);
        _restart.Click += async (_, _) => await StartOver();
        _carve.Click += (_, _) => CarveRest();
        host.JobEnded += OnJobEnded;
        Closed += (_, _) => { host.JobEnded -= OnJobEnded; _work?.Cancel(); Save(); };

        Load();
    }

    ProfileOptions Options() => new()
    {
        Directions = _views.SelectedIndex switch { 0 => 2, 1 => 4, _ => 8 },
        BothSides = _bothSides.IsChecked == true,
        StartDeg = (double)(_start.Value ?? 0),
        LeaveMm = (double)(_leave.Value ?? 0.5m),
        CoreMm = (double)(_core.Value ?? 8) / 2,
        ChuckOnLeft = _chuck.SelectedIndex != 1,
        CoreToBothEnds = _bothEnds.IsChecked == true,
        TrenchMm = (double)(_trench.Value ?? 1.2m),
        LineGapMm = (double)(_gap.Value ?? 0.15m),
        FocusStepMm = (double)(_focusStep.Value ?? 2),
        PassDepthMm = (double)(_passDepth.Value ?? 0.25m),
        OverlapMm = (double)(_overlap.Value ?? 1),
    };

    (Material M, Recipe R)? Cut() => _with.SelectedIndex >= 1 && _with.SelectedIndex <= _cuts.Count ? _cuts[_with.SelectedIndex - 1] : null;

    Layer CutLayer() => Cut() is { } c
        ? c.R.Settings.ApplyTo(Layer.Default(0, LayerMode.Cut)) with { Material = new MaterialRef(c.M.Id, c.M.Label, c.R.Id, c.R.Title) }
        : Layer.Default(0, LayerMode.Cut);

    double LineY => Math.Clamp(_host.AppSettings.RotaryLineMm, 1, MachineProfile.F2Ultra.FieldH - 1);

    ProfileStep? Current => _index >= 0 && _index < _steps.Count ? _steps[_index] : null;

    void MakeSteps()
    {
        _steps = ProfileCut.Steps(Options());
        _index = Math.Clamp(_index, 0, _steps.Count);
    }

    void Changed()
    {
        if (_loading) return;
        _debounce.Stop();
        _debounce.Start();
    }

    /// <summary>What's left now (the stock less the views cut through), and the cut for the step we're on.</summary>
    async void Replan()
    {
        Save();
        _work?.Cancel();
        var cts = _work = new CancellationTokenSource();
        var o = Options();
        var step = Current;
        var done = _done.ToList();
        double lineY = LineY, focusX = MachineProfile.F2Ultra.FieldW / 2;
        _status.Text = "Working out the cut…";
        try
        {
            var (rem, plan, wholeMm3) = await Task.Run(() =>
            {
                var r = Remaining.From(_stock);
                double whole = r.VolumeMm3();
                foreach (var v in done)
                {
                    cts.Token.ThrowIfCancellationRequested();
                    r = r.After(ProfileCut.Keep(_placed, v, o, r.MinX, r.MaxX), v);
                }
                var p = step is null ? null : ProfileCut.Plan(r, _placed, step, o, lineY, focusX, cts.Token);
                return (r, p, whole);
            });
            if (cts.IsCancellationRequested) return;
            _remaining = rem;
            _plan = plan;
            _wholeMm3 = wholeMm3;
            _status.Text = "";
        }
        catch (OperationCanceledException) { return; }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // An async void: nothing may escape it.
            _remaining = null;
            _plan = null;
            _status.Text = e is ArgumentException ? e.Message : $"Couldn't work out the cut: {e.Message}";
        }
        finally
        {
            if (_work == cts) _work = null;
        }
        ShowPlan();
    }

    void ShowPlan()
    {
        var o = Options();
        var step = Current;
        double left = _remaining is { } r && _wholeMm3 > 0 ? 100 * r.VolumeMm3() / _wholeMm3 : 100;
        if (step is null)
        {
            _stepText.Text = $"All {_steps.Count} cuts done: {_done.Count} view{(_done.Count == 1 ? "" : "s")} cut through, {left:0}% of the stock left. Carve what's left to finish it.";
            _planText.Text = "";
        }
        else
        {
            int views = ProfileCut.Views(o).Length;
            double turn = ProfileCut.TurnBetween(_atDeg, step.ViewDeg);
            _stepText.Text = $"Cut {step.Index + 1} of {_steps.Count}. {step.Label(o, views)}." +
                             (Math.Abs(turn) > 0.05 ? $" The part is at {ProfileCut.Wrap360(_atDeg):0.#}°: turn it {turn:+0.#;-0.#}° first." : " The part is turned to it.");
            _planText.Text = _plan is { } p ? ProfileCut.Describe(p, o, LineY) + (step.Side == 0 && o.BothSides ? " This side goes just past the middle; the other side finishes it." : "") : "";
        }
        _view.Show(_plan, LineY, _remaining is null ? null : $"{_done.Count} view{(_done.Count == 1 ? "" : "s")} cut through · {left:0}% of the stock left");
        _carve.IsEnabled = _remaining is not null && _done.Count > 0;
        UpdateButtons();
    }

    void UpdateButtons()
    {
        var step = Current;
        bool at = step is not null && Math.Abs(ProfileCut.TurnBetween(_atDeg, step.ViewDeg)) < 0.05;
        _turn.Content = step is null ? "Turn to this view" : at ? "Turned to it ✓" : $"Turn {ProfileCut.TurnBetween(_atDeg, step.ViewDeg):+0.#;-0.#}° to it";
        _turn.IsEnabled = step is not null && !at && !_waiting;
        _send.IsEnabled = step is not null && at && _plan is { Bands.Count: > 0 } && !_waiting;
        _check.IsEnabled = step is not null && !_waiting && _before is not null;
        _more.IsEnabled = step is not null && at && _plan is { Bands.Count: > 0 } && !_waiting;
        _next.IsEnabled = step is not null && !_waiting;
        _next.Content = step is { EndsView: false } ? "This side's done" : "It's through: next";
        _skip.IsEnabled = step is not null && !_waiting;
        _back.IsEnabled = _index > 0 && !_waiting;
        _restart.IsEnabled = !_waiting;
        bool was = _loading;
        _loading = true;
        _now.Value = (decimal)Math.Round(ProfileCut.Wrap180(_atDeg), 1);
        _loading = was;
    }

    void GoTo(int index)
    {
        _index = Math.Clamp(index, 0, _steps.Count);
        _before = null;
        _beforeImg.Source = _afterImg.Source = null;
        _checkText.Text = "";
        Save();
        Replan();
    }

    async Task TurnToView()
    {
        if (Current is not { } step) return;
        double turn = ProfileCut.TurnBetween(_atDeg, step.ViewDeg);
        if (Math.Abs(turn) < 0.05) return;
        if (!await RotaryTurn.TurnDegreesAsync(this, _host.AppSettings, _host.MachinePanel, turn,
                $"Turn the part {Math.Abs(turn):0.#}° to {ProfileCut.Wrap360(step.ViewDeg):0.#}° for the next profile cut. Keep hands clear of the chuck."))
            return;
        _atDeg = step.ViewDeg;
        _before = null;
        _beforeImg.Source = _afterImg.Source = null;
        _checkText.Text = "";
        _status.Text = $"Turning {turn:+0.#;-0.#}°. Once it has stopped, focus on the top of the part at {_plan?.FocusAt(LineY) ?? "the X shown"} and send the cut.";
        Save();
        UpdateButtons();
    }

    /// <summary>Sends the cut (or only the focus heights given) as a flat job, the part held still in the rotary.</summary>
    async Task SendCut(List<int>? only)
    {
        if (Current is not { } step || _plan is not { } plan || plan.Bands.Count == 0) return;
        var machine = _host.MachinePanel;
        if (!machine.IsConnected)
        {
            await Dialogs.Message(this, "Not connected", "Connect to the machine first (Machine, at the right end of the ribbon).");
            return;
        }
        if (machine.Mode is "P_WORKING" or "P_PAUSE")
        {
            await Dialogs.Message(this, "A job is running", "Wait for the job on the machine to finish (or stop it) first.");
            return;
        }
        var state = ProfileCut.Job(plan, CutLayer(), only);
        var settings = _host.AppSettings;
        var opt = JobSettings.From(settings) with
        {
            // The part is held still: a flat job, deep focus allowed (the cut goes down through it), each path's passes in turn.
            Mode = new WorkMode.Flat(),
            Machine = MachineProfile.F2Ultra,
            FocusLimitMm = Job.MaxRotaryFocusOffsetMm,
            WholeJobPasses = false,
        };
        BuiltJob job;
        try { job = await Task.Run(() => JobBuilder.Build(state, opt)); }
        catch (Exception e) when (e is JobException or OutsideWorkAreaException)
        {
            await Dialogs.Message(this, "Can't make the cut", e.Message);
            return;
        }
        var review = JobChecks.Review(state, job, opt, settings, machine.Distance);
        if (review.Refusal is { } refusal)
        {
            await Dialogs.Message(this, "Can't send", refusal);
            return;
        }
        // A photo of the part as it is, to compare with afterwards (only the cut that finishes a view needs one; a fresh one each time).
        string photoNote = "";
        if (step.EndsView && (only is null || _before is null))
        {
            try { _before = await Photo(plan); }
            catch (Exception e) when (e is InvalidOperationException or IOException || e.GetType().Name == "DeviceException") { photoNote = $"\n\n(No photo before the cut: {e.Message})"; }
            if (_before is { } bp) { _beforeImg.Source = bp.Shown; _afterImg.Source = null; }
        }
        string what = only is null ? $"the profile cut at {ProfileCut.Wrap360(step.ViewDeg):0.#}°" : $"{only.Count} more focus height{(only.Count == 1 ? "" : "s")} of the cut at {ProfileCut.Wrap360(step.ViewDeg):0.#}°";
        var text = $"Send {what}?\n\n" +
                   $"The part is cut on the flat, held still in the rotary (Work mode stays as it is). Focus on the top of the part at {plan.FocusAt(LineY)} first: the focus heights go down from there, to {plan.DeepestMm:0.#} mm below it.\n\n" +
                   string.Join("\n", review.Lines.Where(l => l.Length > 0 && !l.StartsWith("Work mode:"))) +
                   (review.Warnings.Count > 0 ? "\n\n" + string.Join("\n", review.Warnings.Select(w => "⚠ " + w)) : "") +
                   "\n\nWaste drops off as the cut goes through: keep the bed under the part clear. After upload the machine waits until you press its start button." + photoNote;
        if (!await Dialogs.Confirm(this, "Send the profile cut?", text, "Send it", "Cancel")) return;
        try
        {
            await machine.UploadJobAsync(job.Xf, null, CancellationToken.None, replaceLoadedJob: true, name: $"Profile cut {ProfileCut.Wrap360(step.ViewDeg):0.#}°");
            _waiting = true;
            _status.Text = "Uploaded. Press the start button on the machine. When it's done the camera checks the cut.";
        }
        catch (Exception e) when (e.GetType().Name == "DeviceException")
        {
            await Dialogs.Message(this, "Upload failed", e.Message);
        }
        UpdateButtons();
    }

    void OnJobEnded(bool finished) => Dispatcher.UIThread.Post(async () =>
    {
        if (!_waiting) return;
        _waiting = false;
        if (!finished)
        {
            _status.Text = "The cut was stopped. Send it again, or give it more passes.";
            UpdateButtons();
            return;
        }
        if (Current is { EndsView: false } step)
        {
            _status.Text = $"First side done. On to the other side: turn the part half a turn.";
            GoTo(step.Index + 1);
            return;
        }
        _status.Text = "Cut done. Checking with the camera (the laser's off)…";
        UpdateButtons();
        await Check();
    });

    /// <summary>A photo of the part from above (the laser off, nothing moves), as grey levels for comparing and as a picture to show.</summary>
    async Task<(byte[] Grey, int W, int H, Rect2 Area, Bitmap Shown)> Photo(ProfilePlan plan)
    {
        if (!_host.IsConnected) throw new InvalidOperationException("Not connected to the machine.");
        if (_host.FocusDistanceMm is not { } d) throw new InvalidOperationException("Focus on the part first, so the camera knows how far away its top is.");
        if (!_host.CameraCalibratedFor(d)) throw new InvalidOperationException($"The camera isn't calibrated near {d:0.#} mm yet (Camera tab → Calibrate).");
        var box = Rect2.Bounds(StackSliceLoops(plan.Shadow).SelectMany(l => l).Select(p => new Vec2(p.X, LineY - p.Y)));
        var field = MachineProfile.F2Ultra;
        double x0 = Math.Max(0, box.MinX - 6), y0 = Math.Max(0, box.MinY - 6), x1 = Math.Min(field.FieldW, box.MaxX + 6), y1 = Math.Min(field.FieldH, box.MaxY + 6);
        if (!(x1 - x0 > 2 && y1 - y0 > 2)) throw new InvalidOperationException("The part isn't on the bed's field.");
        var area = new Rect2(x0, y0, x1, y1);
        const double MmPerPx = 0.5;
        using var bmp = await _host.PhotographAreaAsync(area, MmPerPx, d);
        var raster = CameraImaging.ToRaster(bmp);
        var shown = CameraImaging.ToAvalonia(bmp);
        return (ThroughCheck.Grey(raster.Rgba, raster.Width, raster.Height), raster.Width, raster.Height, area, shown);
    }

    static List<List<Vec2>> StackSliceLoops(PathGeometry g) => g.IsEmpty ? [] : PhotonCanvas.Doc.Geometry.StackSlicer.Loops(g);

    async Task Check()
    {
        if (_plan is not { } plan || _before is not { } before)
        {
            _checkText.Text = "No photo from before the cut to compare with: look at the part. If the waste has dropped away, it's through.";
            return;
        }
        _check.IsEnabled = false;
        try
        {
            var after = await Photo(plan);
            _afterImg.Source = after.Shown;
            if (after.W != before.W || after.H != before.H)
            {
                _checkText.Text = "The two photos aren't the same size (the focus changed?): look at the part.";
                return;
            }
            var zones = ThroughCheck.Zones(plan, Options(), LineY, before.Area, 0.5, out int w, out int h);
            if (w != before.W || h != before.H)
            {
                _checkText.Text = "Couldn't line the photo up with the cut: look at the part.";
                return;
            }
            var result = ThroughCheck.Judge(before.Grey, after.Grey, zones, w, h);
            _checkText.Text = result.Text + (result.Verdict == ThroughVerdict.Through ? " Click \"It's through: next\" if it looks right." : " Click \"More passes\", or \"It's through: next\" if you can see it is.");
            _checkText.Foreground = Brush(result.Verdict == ThroughVerdict.Through ? "Ok" : "Warn");
        }
        catch (Exception e)
        {
            // Runs from the job-ended handler too (an async void): nothing may escape it.
            _checkText.Text = $"Couldn't check it with the camera: {e.Message} Look at the part: if the waste has dropped away, it's through.";
        }
        finally { UpdateButtons(); }
    }

    /// <summary>This view is through: what's outside its outline is gone. On to the next cut.</summary>
    void Through()
    {
        if (Current is not { } step) return;
        if (step.EndsView && !_done.Any(d => Math.Abs(ProfileCut.Wrap180(d - step.ViewDeg)) < 0.01 || Math.Abs(ProfileCut.Wrap180(d - step.ViewDeg - 180)) < 0.01))
            _done.Add(step.ViewDeg);
        GoTo(step.Index + 1);
    }

    async Task StartOver()
    {
        if (!await Dialogs.Confirm(this, "Start over", "Forget the cuts made so far and start again from the first view, with the part as scanned?", "Start over", "Cancel")) return;
        _done.Clear();
        GoTo(0);
    }

    void CarveRest()
    {
        if (_remaining is not { } r) return;
        _carveFrom(r.ToStock(), $"What's left after {_done.Count} profile cut view{(_done.Count == 1 ? "" : "s")}");
        _status.Text = "Carve to model now starts from what's left. Make its engraving there.";
    }

    string SavedPath => Path.Combine(_host.DataFolder, "profile.json");

    void Load()
    {
        _loading = true;
        ProfileSaved? saved = null;
        try { saved = File.Exists(SavedPath) ? JsonSerializer.Deserialize<ProfileSaved>(File.ReadAllText(SavedPath)) : null; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { }
        saved ??= new ProfileSaved { Options = new ProfileOptions { StartDeg = Math.Round(ProfileCut.Wrap180(_placement.SpinDeg), 1) } };
        var o = saved.Options;
        _views.SelectedIndex = o.Directions <= 2 ? 0 : o.Directions < 8 ? 1 : 2;
        _bothSides.IsChecked = o.BothSides;
        _start.Value = (decimal)Math.Clamp(o.StartDeg, -360, 360);
        _chuck.SelectedIndex = o.ChuckOnLeft ? 0 : 1;
        _core.Value = (decimal)Math.Clamp(o.CoreMm * 2, 1, 60);
        _bothEnds.IsChecked = o.CoreToBothEnds;
        _passDepth.Value = (decimal)Math.Clamp(o.PassDepthMm, 0.01, 5);
        _focusStep.Value = (decimal)Math.Clamp(o.FocusStepMm, 0.2, 10);
        _trench.Value = (decimal)Math.Clamp(o.TrenchMm, 0.1, 6);
        _gap.Value = (decimal)Math.Clamp(o.LineGapMm, 0.02, 1);
        _leave.Value = (decimal)Math.Clamp(o.LeaveMm, 0, 5);
        _overlap.Value = (decimal)Math.Clamp(o.OverlapMm, 0, 5);
        int ri = _cuts.FindIndex(c => c.R.Id.ToString() == saved.Recipe);
        _with.SelectedIndex = ri >= 0 ? ri + 1 : 0;
        MakeSteps();
        // The progress is only for the same model in the same place.
        if (saved.ModelFile == _modelFile && saved.Placement == _placement)
        {
            _index = Math.Clamp(saved.Step, 0, _steps.Count);
            _atDeg = saved.AtDeg;
            _done.AddRange(saved.Done);
        }
        else
        {
            _index = 0;
            _atDeg = 0;
        }
        _loading = false;
        Replan();
    }

    void Save()
    {
        if (_loading) return;
        try
        {
            Directory.CreateDirectory(_host.DataFolder);
            var saved = new ProfileSaved
            {
                Options = Options(), Recipe = Cut()?.R.Id.ToString(), ModelFile = _modelFile, Placement = _placement,
                Step = _index, AtDeg = _atDeg, Done = [.. _done],
            };
            File.WriteAllText(SavedPath, JsonSerializer.Serialize(saved, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}

/// <summary>A cut seen from above, on the bed: where the part is (grey), what's kept (green), and the cut's lines (by focus height).</summary>
sealed class ProfileView : Control
{
    ProfilePlan? _plan;
    double _lineY;
    string? _note;

    public void Show(ProfilePlan? plan, double lineY, string? note)
    {
        _plan = plan;
        _lineY = lineY;
        _note = note;
        InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        // A problem drawing must never take the program down.
        try { Draw(ctx); }
        catch (Exception) { }
    }

    void Draw(DrawingContext ctx)
    {
        var size = Bounds.Size;
        ctx.FillRectangle(UiKit.Brush("Bg2"), new Rect(size));
        if (_plan is not { } p)
        {
            Text(ctx, "Working it out…", new Point(12, 12));
            return;
        }
        var shadow = Loops(p.Shadow);
        var keep = Loops(p.Keep);
        var all = shadow.Concat(keep).SelectMany(l => l).ToList();
        if (all.Count == 0) return;
        var box = Rect2.Bounds(all);
        double pad = 24, s = Math.Min((size.Width - 2 * pad) / Math.Max(1, box.Width), (size.Height - 2 * pad - 36) / Math.Max(1, box.Height));
        if (!(s > 0)) return;
        Point At(Vec2 v) => new(pad + (v.X - box.MinX) * s, pad + 36 + (v.Y - box.MinY) * s);
        Geometry Geo(List<List<Vec2>> loops)
        {
            var g = new StreamGeometry();
            using var c = g.Open();
            c.SetFillRule(FillRule.EvenOdd);
            foreach (var l in loops)
            {
                c.BeginFigure(At(l[0]), true);
                foreach (var v in l.Skip(1)) c.LineTo(At(v));
                c.EndFigure(true);
            }
            return g;
        }
        ctx.DrawGeometry(new SolidColorBrush(Color.FromArgb(70, 150, 150, 150)), new Pen(new SolidColorBrush(Color.FromRgb(140, 140, 140)), 1), Geo(shadow));
        ctx.DrawGeometry(new SolidColorBrush(Color.FromArgb(90, 60, 170, 90)), new Pen(new SolidColorBrush(Color.FromRgb(60, 150, 80)), 1), Geo(keep));
        foreach (var b in p.Bands)
        {
            uint c = Layer.Palette[b.Index % Layer.Palette.Count];
            var pen = new Pen(new SolidColorBrush(Color.FromRgb((byte)(c >> 16), (byte)(c >> 8), (byte)c)), 1.2);
            foreach (var f in b.Lines.Flatten(0.1))
                for (int i = 1; i < f.Points.Count; i++)
                    ctx.DrawLine(pen, At(Bed(f.Points[i - 1])), At(Bed(f.Points[i])));
        }
        // The laser line (the axis, seen from above).
        var dash = new Pen(UiKit.Brush("TextMuted"), 1) { DashStyle = new DashStyle([4, 4], 0) };
        ctx.DrawLine(dash, At(new Vec2(box.MinX, 0)), At(new Vec2(box.MaxX, 0)));
        Text(ctx, $"Seen from above at {ProfileCut.Wrap360(p.Step.ViewDeg):0.#}°, the machine's back at the top." + (_note is null ? "" : $"  {_note}."), new Point(12, 6));
        Text(ctx, "Grey: the part. Green: kept. Lines: the cut, a colour for each focus height.", new Point(12, 22));
    }

    /// <summary>A line point on the bed (design X, Y) as drawn here: (x, −h), so h (toward the machine's back) is up.</summary>
    Vec2 Bed(Vec2 v) => new(v.X, v.Y - _lineY);

    static List<List<Vec2>> Loops(PathGeometry g) => g.IsEmpty ? [] : [.. PhotonCanvas.Doc.Geometry.StackSlicer.Loops(g).Select(l => l.Select(v => new Vec2(v.X, -v.Y)).ToList())];

    static void Text(DrawingContext ctx, string s, Point at) =>
        ctx.DrawText(new FormattedText(s, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 12, UiKit.Brush("TextMuted")), at);
}
