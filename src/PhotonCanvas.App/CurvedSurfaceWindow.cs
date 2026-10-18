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
    readonly Control _measurePanel, _typedPanel, _fitPanel;
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
        _board.Value = (decimal)Math.Round(machine.Distance ?? 3, 1);
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
        var calibrate = Button("Calibrate the pointer…", tip: "Once: on a flat board, the head steps up and down and the dot is photographed at each height. About two minutes.");
        calibrate.Click += async (_, _) => await Calibrate();
        var measure = new Button { Content = "Measure", Classes = { "primary" }, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
        measure.Click += async (_, _) => await Measure(null);
        var again = Button("Measure the red points again");
        again.Click += async (_, _) => await Measure(_suspect);
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
            Spacing = 8,
            Children =
            {
                Hint("Focus on the middle of the piece first (the focus height is where the head measures from). The area must be where the close-up camera can see the dot."),
                Field("Area X", _ax), Field("Area Y", _ay), Field("Width", _aw), Field("Height", _ah),
                fromDesign,
                Field("Rows", _rows), Field("Columns", _cols),
                Hint("More points follow the shape better: one every 5 mm or so for slats or tight curves."),
                _calState,
                Field("Board (mm)", _board, tip: "Thickness of the flat board used to calibrate the pointer."),
                calibrate,
                Row(measure, stop),
                again,
            },
        };
        _typedPanel = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                Hint("A round object lying on its side. Measure it with calipers: it's far more exact than measuring with the pointer."),
                Field("Shape", _tShape),
                Field("Slats round it", _tSides),
                Field("Axis runs", _axisDir),
                Field("Axis X", _axisX), Field("Axis Y", _axisY),
                axisOnSel,
                Field("Top height", _top, tip: "Height of the top above the bed, as you'd focus on it."),
                Field("Diameter", _dia, tip: "For slats: across the flats."),
            },
        };
        _fitPanel = new StackPanel
        {
            Spacing = 8,
            Children = { Field("Fit as", _shape), Field("Smoothing", _smooth), Field("Slats round it", _sides), _fitInfo },
        };

        foreach (var c in new Control[] { _shape, _tShape, _axisDir, _source }) if (c is ComboBox cb) cb.SelectionChanged += (_, _) => Update();
        foreach (var n in new[] { _sides, _axisX, _axisY, _top, _dia, _tSides, _band, _ax, _ay, _aw, _ah }) n.ValueChanged += (_, _) => Update();
        _smooth.PropertyChanged += (_, e) => { if (e.Property == Avalonia.Controls.Primitives.RangeBase.ValueProperty) Update(); };

        var apply = new Button { Content = "Apply", Classes = { "primary" }, MinWidth = 110 };
        apply.Click += (_, _) => { Apply(); Close(); };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 100 };
        cancel.Click += (_, _) => Close();
        Closing += (_, _) => _cts?.Cancel();

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
        _sides.IsVisible = _shape.SelectedIndex == 2;
        _tSides.IsVisible = _tShape.SelectedIndex == 1;
        _calState.Text = SurfaceProbe.LoadCalibration() is null ? "⚠ The pointer isn't calibrated yet: do that once first (a flat board of known thickness)." : "✓ Pointer calibrated.";
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
        _view.Show(s, area, typed ? [] : _curve.Points, _suspect.ToHashSet(), design);
    }

    async Task Calibrate()
    {
        if (_machine.Client is not { IsConnected: true } client) { _status.Text = "Connect to the machine first."; return; }
        var owner = this;
        if (!await Dialogs.Confirm(owner, "Calibrate the pointer",
                $"Put a flat, matt board {_board.Value:0.#} mm thick (change it below if not) under the head, covering the middle of the bed.\n\n" +
                "The head moves up and down 8 mm either side of it and the red pointer is aimed at a grid of points; the laser is not fired. About two minutes.",
                "Start", "Cancel"))
            return;
        await Run(async (p, ct) =>
        {
            var aims = SurfaceProbe.Grid(new Rect2(95, 95, 125, 125), 5, 5);
            await new SurfaceProbe(client).CalibrateAsync((double)(_board.Value ?? 3), aims, p, ct);
            return "Pointer calibrated.";
        });
    }

    async Task Measure(IReadOnlyCollection<int>? only)
    {
        if (_machine.Client is not { IsConnected: true } client) { _status.Text = "Connect to the machine first."; return; }
        if (SurfaceProbe.LoadCalibration() is not { } cal) { _status.Text = "Calibrate the pointer first."; return; }
        if (_machine.Distance is not { } focus) { _status.Text = "Set the focus on the piece first (auto-focus, or type its height in the machine panel)."; return; }
        var aims = only is { Count: > 0 } ? [.. only.Select(i => _curve.Points[i].At)] : SurfaceProbe.Grid(Area, (int)(_rows.Value ?? 9), (int)(_cols.Value ?? 9));
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
