using System.Collections.Immutable;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using PhotonCanvas.Core;
using PhotonCanvas.Core.Camera;
using PhotonCanvas.Device;
using PhotonCanvas.Doc;
using static PhotonCanvas.App.UiKit;

namespace PhotonCanvas.App;

/// <summary>
/// Curved surface: measure the piece with the pointer and the close-up camera (or type in a
/// cylinder or slatted drum), see it in 3D, and mark the design on it. The head then steps through
/// height bands that follow the surface.
/// </summary>
public sealed class CurvedSurfaceWindow : Window
{
    readonly Document _doc;
    readonly MachinePanel _machine;
    CurvedSurface _curve;
    List<int> _suspect = [];
    CancellationTokenSource? _cts;

    readonly CheckBox _on = new() { Content = "Mark the design on this surface" };
    readonly ComboBox _source = new() { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = new[] { "Measure it with the pointer", "Type in a cylinder or drum" } };
    readonly SurfaceView _view = new() { MinHeight = 380 };
    readonly TextBlock _fitInfo = new() { TextWrapping = TextWrapping.Wrap };
    readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    readonly ProgressBar _busy = new() { IsIndeterminate = true, IsVisible = false, Height = 4 };
    // Measuring
    readonly NumericUpDown _ax = Number(0, 220, 1, "0.#"), _ay = Number(0, 220, 1, "0.#"), _aw = Number(2, 220, 1, "0.#"), _ah = Number(2, 220, 1, "0.#");
    readonly NumericUpDown _rows = Number(3, 40, 1, "0"), _cols = Number(3, 40, 1, "0");
    readonly NumericUpDown _board = Number(0.5m, 150, 0.5m, "0.#");
    readonly ComboBox _shape = new() { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = new[] { "Any shape (smooth)", "Cylinder", "Drum of flat slats", "Flat, at a slant" } };
    readonly Slider _smooth = new() { Minimum = 0, Maximum = 1, Value = 0.3, HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly NumericUpDown _sides = Number(3, 200, 1, "0");
    readonly TextBlock _calState = new() { TextWrapping = TextWrapping.Wrap };
    readonly TextBlock _calHow = Hint(
        "What to do:\n" +
        "1. Put a flat, matt board (plywood, MDF or card, not shiny) in the middle of the bed.\n" +
        "2. Auto-focus on it, or measure it with calipers and type its thickness below.\n" +
        "3. Press Calibrate. The head steps 8 mm up and down and the dot is photographed at 25 spots in the middle of the bed, at each height. About two minutes.");
    readonly TextBlock _focusState = new() { TextWrapping = TextWrapping.Wrap };
    readonly TextBlock _planInfo = new() { TextWrapping = TextWrapping.Wrap };
    readonly Button _calibrate = new() { Content = "Calibrate…", Classes = { "primary" }, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
    readonly Button _again = Button("Measure the red points again");
    readonly Control _measurePanel, _typedPanel, _fitPanel, _sidesField, _tSidesField;
    // Typed
    readonly ComboBox _tShape = new() { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = new[] { "Cylinder", "Drum of flat slats" } };
    readonly ComboBox _axisDir = new() { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = new[] { "Front to back (along Y)", "Left to right (along X)" } };
    readonly NumericUpDown _axisX = Number(-100, 320, 0.5m, "0.#"), _axisY = Number(-100, 320, 0.5m, "0.#"), _top = Number(1, 150, 0.5m, "0.#"), _dia = Number(2, 400, 0.5m, "0.#"), _tSides = Number(3, 200, 1, "0");
    readonly NumericUpDown _band = Number(0.05m, 1, 0.05m, "0.##");

    public CurvedSurfaceWindow(Document doc, MachinePanel machine)
    {
        _doc = doc;
        _machine = machine;
        _curve = doc.State.Curve;
        Title = "Curved surface";
        Width = 1100;
        Height = 760;
        MinWidth = 860;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");

        var design = DesignBounds();
        var area = _curve.Area.IsEmpty ? (design.IsEmpty ? new Rect2(85, 85, 135, 135) : design.Inflate(3)) : _curve.Area;
        _ax.Value = (decimal)Math.Round(area.MinX, 1); _ay.Value = (decimal)Math.Round(area.MinY, 1);
        _aw.Value = (decimal)Math.Round(area.Width, 1); _ah.Value = (decimal)Math.Round(area.Height, 1);
        _rows.Value = 9; _cols.Value = 9;
        // The board used last time (it's usually the same one), else a typical 3 mm sheet. Not the
        // current focus height: that's wherever the head was last focused, often not on a board.
        _board.Value = (decimal)Math.Round(SurfaceProbe.CalibrationInfo()?.BoardMm ?? 3, 1);
        _on.IsChecked = _curve.On;
        _source.SelectedIndex = _curve.Typed ? 1 : 0;
        _shape.SelectedIndex = (int)_curve.Shape;
        _smooth.Value = _curve.Smoothing;
        _sides.Value = _curve.Sides;
        _tShape.SelectedIndex = _curve.Shape == SurfaceShape.Prism ? 1 : 0;
        _axisDir.SelectedIndex = Math.Abs(_curve.AxisDeg) < 45 ? 1 : 0;
        _axisX.Value = (decimal)_curve.AxisPoint.X; _axisY.Value = (decimal)_curve.AxisPoint.Y;
        _top.Value = (decimal)_curve.TopMm; _dia.Value = (decimal)_curve.DiameterMm; _tSides.Value = _curve.Sides;
        _band.Value = (decimal)_curve.BandMm;

        var fromDesign = Button("Use the design's area");
        fromDesign.Click += (_, _) =>
        {
            var d = DesignBounds();
            if (d.IsEmpty) return;
            d = d.Inflate(3);
            _ax.Value = (decimal)Math.Round(d.MinX, 1); _ay.Value = (decimal)Math.Round(d.MinY, 1); _aw.Value = (decimal)Math.Round(d.Width, 1); _ah.Value = (decimal)Math.Round(d.Height, 1);
        };
        ToolTip.SetTip(_calibrate, "Needs the flat board in the middle of the bed. The head moves up and down and the red pointer is aimed and photographed; the laser never fires.");
        _calibrate.Click += async (_, _) => await Calibrate();
        var boardFocus = Button("Auto-focus on the board", tip: "Auto-focus on the board now: its focus height becomes the board thickness. The head moves; the laser doesn't fire.");
        boardFocus.Click += async (_, _) =>
        {
            if (!_machine.IsConnected) { _status.Text = "Connect to the machine first."; return; }
            _status.Text = "Auto-focusing on the board… the head will move.";
            if (await _machine.AutoFocusNowAsync() is { } d) { _board.Value = (decimal)Math.Round(d, 1); _status.Text = $"The board's focus height is {d:0.0} mm: that's now the board thickness."; }
            else _status.Text = "Auto-focus didn't finish (see the machine panel).";
            Update();
        };
        var pieceFocus = Button("Auto-focus now", tip: "Auto-focus where the head is now (the middle of the bed). The head moves; the laser doesn't fire.");
        pieceFocus.Click += async (_, _) =>
        {
            if (!_machine.IsConnected) { _status.Text = "Connect to the machine first."; return; }
            _status.Text = "Auto-focusing… the head will move.";
            _status.Text = await _machine.AutoFocusNowAsync() is { } d ? $"Focused at {d:0.0} mm." : "Auto-focus didn't finish (see the machine panel).";
            Update();
        };
        var measure = new Button { Content = "Measure", Classes = { "primary" }, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
        measure.Click += async (_, _) => await Measure(null);
        _again.Click += async (_, _) => await Measure(_suspect);
        var stop = Button("Stop");
        stop.Click += (_, _) => _cts?.Cancel();
        var axisOnSel = Button("Axis through the design's middle");
        axisOnSel.Click += (_, _) =>
        {
            var d = DesignBounds();
            if (d.IsEmpty) return;
            _axisX.Value = (decimal)Math.Round(d.Center.X, 1); _axisY.Value = (decimal)Math.Round(d.Center.Y, 1);
        };

        _measurePanel = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                Explained("1 · Calibrate the pointer (once)",
                    "The red pointer shines in at a slant, so when a surface is higher or lower its dot lands a little to one side, and the close-up camera in the head sees it shift. " +
                    "Calibrating measures how far it shifts for each mm, on a flat board. It's kept for good: do it again only if the head or its camera gets knocked.",
                    _calState,
                    _calHow,
                    Field("Board thickness", _board, Hint("mm. Must be right: any error here is added to every height measured later."),
                        tip: "How thick the flat calibration board is (its focus height as it lies on the bed). Auto-focusing on it gets this exactly."),
                    boardFocus,
                    _calibrate),
                Explained("2 · Focus on the piece",
                    "Put the curved piece in and focus on its middle (auto-focus there, or set it in the machine panel). Heights are read from that focus height, up to 8 mm above or below it.",
                    _focusState,
                    pieceFocus),
                Explained("3 · Where to measure",
                    "A rectangle on the bed, in mm from the bed's top-left corner as it shows on the canvas. The pointer is aimed at Rows × Columns spots spread evenly over it. The picture on the left shows it.",
                    Field("X (left edge)", _ax, tip: "How far the rectangle's left edge is from the left of the bed, in mm."),
                    Field("Y (top edge)", _ay, tip: "How far the rectangle's top edge is from the top (back) of the bed, in mm."),
                    Field("Width (mm)", _aw), Field("Height (mm)", _ah),
                    fromDesign,
                    Field("Rows", _rows), Field("Columns", _cols),
                    Hint("More spots follow the shape better: one every 5 mm or so for slats or tight curves."),
                    _planInfo),
                Explained("4 · Measure",
                    "The head goes to the focus height and the red dot is aimed at each spot and photographed. The laser never fires.",
                    Row(measure, stop),
                    _again),
            },
        };
        _typedPanel = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                Hint("A round object lying on its side. Measure it with calipers: it's far more exact than measuring with the pointer."),
                Field("Shape", _tShape),
                (_tSidesField = Field("Slats round it", _tSides)),
                Field("Axis runs", _axisDir),
                Field("Axis X", _axisX), Field("Axis Y", _axisY),
                axisOnSel,
                Field("Top height", _top, tip: "Height of the top above the bed, as you'd focus on it."),
                Field("Diameter", _dia, tip: "For slats: across the flats."),
            },
        };
        _fitPanel = Explained("5 · Check the shape",
            "The measured spots (white; red ones disagree with their neighbours and are left out) and the surface fitted through them, in 3D on the left. Then press Apply.",
            Field("Fit as", _shape), Field("Smoothing", _smooth), _sidesField = Field("Slats round it", _sides), _fitInfo);

        foreach (var c in new Control[] { _shape, _tShape, _axisDir, _source }) if (c is ComboBox cb) cb.SelectionChanged += (_, _) => Update();
        foreach (var n in new[] { _sides, _axisX, _axisY, _top, _dia, _tSides, _band, _ax, _ay, _aw, _ah, _rows, _cols, _board }) n.ValueChanged += (_, _) => Update();
        _smooth.PropertyChanged += (_, e) => { if (e.Property == Avalonia.Controls.Primitives.RangeBase.ValueProperty) Update(); };

        var apply = new Button { Content = "Apply", Classes = { "primary" }, MinWidth = 110 };
        apply.Click += (_, _) => { Apply(); Close(); };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 100 };
        cancel.Click += (_, _) => Close();
        Closing += (_, _) => _cts?.Cancel();
        // Keep "focused at …" up to date when the focus changes in the machine panel.
        Action<string> machineChanged = _ => Avalonia.Threading.Dispatcher.UIThread.Post(ShowFocus);
        _machine.StatusChanged += machineChanged;
        Closed += (_, _) => _machine.StatusChanged -= machineChanged;

        var side = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                _on,
                Field("The surface", _source),
                _measurePanel,
                _typedPanel,
                _fitPanel,
                Field("Height steps (mm)", _band, tip: "How far the head moves between bands. xTool Studio uses 0.2 mm; smaller follows the surface more closely and takes longer."),
                _busy,
                _status,
            },
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = new(0, 10, 0, 0), Children = { cancel, apply } };
        var grid = new Grid { ColumnDefinitions = new("*,16,360") };
        grid.Children.Add(new Border { CornerRadius = new(8), ClipToBounds = true, Child = _view });
        var sideScroll = new ScrollViewer { Content = side };
        Grid.SetColumn(sideScroll, 2);
        grid.Children.Add(sideScroll);
        var root = new DockPanel { Margin = new(16) };
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        root.Children.Add(grid);
        Content = root;
        Update();
    }

    Rect2 DesignBounds()
    {
        IReadOnlySet<Guid> ids = _doc.Selection.Count > 0 ? _doc.Selection : _doc.State.Shapes.Select(s => s.Id).ToHashSet();
        return _doc.State.Bounds(ids);
    }

    Rect2 Area => new((double)(_ax.Value ?? 85), (double)(_ay.Value ?? 85), (double)((_ax.Value ?? 85) + (_aw.Value ?? 50)), (double)((_ay.Value ?? 85) + (_ah.Value ?? 50)));

    /// <summary>The settings on screen as a surface.</summary>
    CurvedSurface Current()
    {
        bool typed = _source.SelectedIndex == 1;
        // Points that disagree with their neighbours are left out of the fit.
        var good = _curve.Points.Where((_, i) => !_suspect.Contains(i)).ToImmutableList();
        return _curve with
        {
            Points = good,
            On = _on.IsChecked == true,
            Typed = typed,
            Shape = typed ? (_tShape.SelectedIndex == 1 ? SurfaceShape.Prism : SurfaceShape.Cylinder) : (SurfaceShape)Math.Max(0, _shape.SelectedIndex),
            Smoothing = _smooth.Value,
            Sides = (int)(typed ? _tSides.Value ?? 12 : _sides.Value ?? 12),
            BandMm = (double)(_band.Value ?? 0.2m),
            Area = typed ? _curve.Area : Area,
            AxisDeg = _axisDir.SelectedIndex == 1 ? 0 : 90,
            AxisPoint = new Vec2((double)(_axisX.Value ?? 110), (double)(_axisY.Value ?? 110)),
            TopMm = (double)(_top.Value ?? 50),
            DiameterMm = (double)(_dia.Value ?? 80),
        };
    }

    void Update()
    {
        bool typed = _source.SelectedIndex == 1;
        _measurePanel.IsVisible = !typed;
        _fitPanel.IsVisible = !typed;
        _typedPanel.IsVisible = typed;
        // The whole row, label and all.
        _sidesField.IsVisible = _shape.SelectedIndex == 2;
        _tSidesField.IsVisible = _tShape.SelectedIndex == 1;
        var cal = SurfaceProbe.LoadCalibration();
        var calInfo = cal is null ? null : SurfaceProbe.CalibrationInfo();
        _calState.Text = cal is null ? "⚠ Not calibrated yet. Measuring needs this first."
            : "✓ Calibrated" + (calInfo?.BoardMm is { } bmm ? $" on a {bmm:0.#} mm board" : "") + (calInfo?.When is { } when ? $", {when:d MMM yyyy}" : "") + ". Nothing more to do here.";
        _calState.Foreground = Brush(cal is null ? "Warn" : "Ok");
        _calHow.IsVisible = cal is null;
        _calibrate.Content = cal is null ? "Calibrate…" : "Calibrate again…";
        ShowFocus();
        _again.IsVisible = _suspect.Count > 0;
        if (!typed) _planInfo.Text = PlanText(cal);
        var c = Current();
        Surface? s = null;
        try { s = c.Build(); } catch (ArgumentException) { }
        if (!typed)
        {
            _fitInfo.Text = c.Points.Count == 0 ? "Not measured yet."
                : s is null ? $"{c.Points.Count} points: not enough to fit."
                : $"{c.Points.Count} points; they sit within ±{s.RmsMm:0.00} mm of the surface (typically)." +
                  (c.Shape == SurfaceShape.Cylinder ? $" Diameter {s.Radius * 2:0.#} mm." : c.Shape == SurfaceShape.Prism ? $" {s.Radius * 2 * Math.Cos(Math.PI / Math.Max(3, s.Sides)):0.#} mm across the flats." : "") +
                  (_suspect.Count > 0 ? $" {_suspect.Count} point(s) disagree with their neighbours (red) and are left out: measure them again." : "");
        }
        var design = _doc.State.Shapes.OfType<PathShape>().SelectMany(p => p.Flat).Select(f => (IReadOnlyList<Vec2>)(f.Closed ? [.. f.Points, f.Points[0]] : f.Points)).ToList();
        var area = typed ? DesignBounds().Inflate(10) : Area;
        _view.Plan = typed ? null : new SurfaceView.SurfacePlan(Bed, cal is null ? SurfaceProbe.CalibrationSquare : Rect2.Bounds(cal.Aims), Area, Aims());
        _view.Show(s, area, typed ? [] : _curve.Points, _suspect.ToHashSet(), design);
    }

    void ShowFocus()
    {
        _focusState.Text = _machine.Distance is { } focus
            ? $"The machine is focused at {focus:0.0} mm now. If that isn't on this piece's middle, auto-focus again."
            : _machine.IsConnected ? "⚠ Not focused yet." : "⚠ Not connected to the machine.";
        _focusState.Foreground = Brush(_machine.Distance is null ? "Warn" : "TextMuted");
    }

    static readonly Rect2 Bed = new(0, 0, MachineProfile.F2Ultra.FieldW, MachineProfile.F2Ultra.FieldH);

    List<Vec2> Aims() => SurfaceProbe.Grid(Area, (int)(_rows.Value ?? 9), (int)(_cols.Value ?? 9));

    /// <summary>How many spots, how far apart, how long, and anything wrong with the area.</summary>
    string PlanText(PointerCalibration? cal)
    {
        var a = Area;
        int rows = (int)(_rows.Value ?? 9), cols = (int)(_cols.Value ?? 9), n = rows * cols;
        var t = $"{n} spots, one every {a.Width / Math.Max(1, cols - 1):0.#} mm across and {a.Height / Math.Max(1, rows - 1):0.#} mm down. About {Duration(n * SurfaceProbe.SecondsPerPhoto + 5)}.";
        if (!Bed.Contains(a))
            return t + $"\n⚠ The area runs off the bed (it reaches X {a.MaxX:0.#}, Y {a.MaxY:0.#}; the bed is {Bed.Width:0} × {Bed.Height:0} mm). Make it smaller or move it, or use the design's area.";
        if (cal is not null && !Rect2.Bounds(cal.Aims).Inflate(10).Contains(a))
        {
            var c = Rect2.Bounds(cal.Aims);
            t += $"\nThe pointer was calibrated on the middle square (X {c.MinX:0}–{c.MaxX:0}, Y {c.MinY:0}–{c.MaxY:0} mm, green on the left). Spots well outside it are worked out from it: they may be less exact, or not read at all where the close-up camera can't see the dot.";
        }
        return t;
    }

    async Task Calibrate()
    {
        if (_machine.Client is not { IsConnected: true } client) { _status.Text = "Connect to the machine first."; return; }
        var owner = this;
        if (!await Dialogs.Confirm(owner, "Calibrate the pointer",
                $"Is a flat, matt board {_board.Value:0.#} mm thick lying in the middle of the bed? (If it's a different thickness, press Cancel and change Board thickness first: every height measured later depends on it.)\n\n" +
                "The head moves to 8 mm above and below the board's surface in 2 mm steps. At each step the red pointer is aimed at 25 spots in the middle of the bed and photographed with the close-up camera. The laser is not fired. About two minutes.",
                "Start", "Cancel"))
            return;
        await Run(async (p, ct) =>
        {
            var aims = SurfaceProbe.Grid(SurfaceProbe.CalibrationSquare, 5, 5);
            await new SurfaceProbe(client).CalibrateAsync((double)(_board.Value ?? 3), aims, p, ct);
            return "Pointer calibrated. Next: put the curved piece in and focus on its middle (step 2).";
        });
    }

    async Task Measure(IReadOnlyCollection<int>? only)
    {
        if (_machine.Client is not { IsConnected: true } client) { _status.Text = "Connect to the machine first."; return; }
        if (SurfaceProbe.LoadCalibration() is not { } cal) { _status.Text = "Calibrate the pointer first (step 1)."; return; }
        if (only is not { Count: > 0 } && !Bed.Contains(Area)) { _status.Text = "The area runs off the bed: fix it in step 3 first."; return; }
        if (_machine.Distance is not { } focus) { _status.Text = "Set the focus on the piece first (auto-focus, or type its height in the machine panel)."; return; }
        var aims = only is { Count: > 0 } ? [.. only.Select(i => _curve.Points[i].At)] : Aims();
        await Run(async (p, ct) =>
        {
            var heights = await new SurfaceProbe(client).MeasureAsync(aims, focus, cal, p, ct);
            var pts = only is { Count: > 0 } ? _curve.Points.ToList() : [];
            int missed = 0;
            for (int i = 0; i < aims.Count; i++)
            {
                if (only is { Count: > 0 })
                {
                    int at = pts.FindIndex(q => q.At.Dist(aims[i]) < 1e-6);
                    if (heights[i] is { } h2 && at >= 0) pts[at] = new SurfacePoint(aims[i], h2);
                    else missed++;
                }
                else if (heights[i] is { } h) pts.Add(new SurfacePoint(aims[i], h));
                else missed++;
            }
            _curve = _curve with { Points = [.. pts], Area = Area, Typed = false };
            _suspect = SurfaceCheck.Outliers(_curve.Points);
            return $"Measured {aims.Count - missed} of {aims.Count} points." + (missed > 0 ? $" {missed} couldn't be read (the dot wasn't seen, or the surface is more than 8 mm from the focus)." : "");
        });
    }

    async Task Run(Func<IProgress<string>, CancellationToken, Task<string>> work)
    {
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        _busy.IsVisible = true;
        var progress = new Progress<string>(s => _status.Text = s);
        try { _status.Text = await work(progress, cts.Token); }
        catch (OperationCanceledException) { _status.Text = "Stopped."; }
        catch (Exception e) when (e is DeviceException or InvalidOperationException or FormatException or IOException) { _status.Text = e.Message; }
        finally { _busy.IsVisible = false; Update(); }
    }

    void Apply()
    {
        var c = Current();
        if (c == _doc.State.Curve) return;
        _doc.Commit(c.On ? "Curved surface" : "Curved surface off", _doc.State with { Curve = c });
    }
}
