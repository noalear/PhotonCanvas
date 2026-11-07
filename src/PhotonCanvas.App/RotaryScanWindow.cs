using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using PhotonCanvas.Core;
using PhotonCanvas.Device;
using PhotonCanvas.Doc;
using static PhotonCanvas.App.UiKit;

namespace PhotonCanvas.App;

/// <summary>
/// Scan the part: the part in the rotary is turned a step at a time and the height of its top is measured along the
/// laser line at each step (the red pointer and close-up camera, or the machine's auto-focus). The readings make a
/// model of the stock: how far its surface is from the axis all the way round. The laser is never fired.
/// </summary>
public sealed class RotaryScanWindow : Window
{
    readonly Document _doc;
    readonly AppSettings _settings;
    readonly MachinePanel _machine;
    RotaryScan? _scan;
    // What's shown and saved as the stock: the scan itself, or the standard stock fitted to it.
    RotaryScan? _model;
    StockFit? _fit;
    bool _updating;
    string _savedStockKey = "";
    CancellationTokenSource? _cts;
    // How far from where the last scan started the part was left by a scan that was stopped (kept while the program runs).
    static double _leftDeg;

    readonly ComboBox _method = new() { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = new[] { "Red pointer and close-up camera", "The machine's auto-focus" } };
    readonly NumericUpDown _x0 = Number(0, 220, 1, "0.#"), _x1 = Number(0, 220, 1, "0.#"), _points = Number(1, 60, 1, "0");
    readonly NumericUpDown _stops = Number(2, 360, 1, "0"), _from = Number(-3600, 3600, 15, "0.#"), _to = Number(-3600, 3600, 15, "0.#");
    readonly NumericUpDown _settle = Number(0, 10, 0.25m, "0.##");
    readonly ComboBox _stockKind = new() { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = new[] { "As scanned", "Round rod", "Square bar", "Rectangular bar", "Hexagon bar" } };
    readonly ComboBox _units = new() { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = new[] { "mm", "inches" } };
    readonly NumericUpDown _stockW = Number(0.01m, 1000, 0.5m, "0.###"), _stockH = Number(0.01m, 1000, 0.5m, "0.###");
    readonly Control _stockSize, _stockHField;
    bool Inches => _units.SelectedIndex == 1;
    readonly CheckBox _axisKnown = new() { Content = "I know the axis height" };
    readonly NumericUpDown _axis = Number(-200, 200, 0.5m, "0.##"), _rod = Number(1, 200, 0.5m, "0.##");
    readonly TextBlock _methodNote = Hint(), _status = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap }, _results = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    readonly ProgressBar _busy = new() { IsIndeterminate = true, IsVisible = false, Height = 4 };
    readonly Control _alongPanel;
    readonly Button _scanBtn = new() { Content = "Scan", Classes = { "primary" }, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
    readonly Button _returnBtn = Button("Return to start", tip: "Turn the part back to where the stopped scan started.");
    readonly Button _highBtn = Button("Turn the high side up", tip: "Turn the part so its highest point is on top, under the laser line.");
    readonly StockView _view = new() { MinHeight = 300 };
    readonly ScanChart _chart = new() { Height = 230 };
    readonly Slider _slice = new() { Minimum = 0, Maximum = 0, SmallChange = 1, LargeChange = 1, IsSnapToTickEnabled = true, TickFrequency = 1 };

    static string ScanFile => Path.Combine(AppIdentity.DataDir, "rotary-scan.json");
    static string StockFile => Path.Combine(AppIdentity.DataDir, "rotary-stock.json");

    /// <summary>
    /// The stock model to work from: the last scan, or the standard stock fitted to it when a standard stock was chosen,
    /// with <see cref="RotaryScan.AxisHeightMm"/> the axis height it was made with (null if unknown). Null if none.
    /// </summary>
    public static RotaryScan? LoadStock()
    {
        try { return File.Exists(StockFile) ? RotaryScan.FromJson(File.ReadAllText(StockFile)) : null; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>The last scan saved (null if none, or it can't be read).</summary>
    public static RotaryScan? LoadLast()
    {
        try { return File.Exists(ScanFile) ? RotaryScan.FromJson(File.ReadAllText(ScanFile)) : null; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    static void SaveLast(RotaryScan s)
    {
        try
        {
            Directory.CreateDirectory(AppIdentity.DataDir);
            File.WriteAllText(ScanFile, s.ToJson());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    public RotaryScanWindow(Document doc, AppSettings settings, MachinePanel machine)
    {
        _doc = doc;
        _settings = settings;
        _machine = machine;
        Title = "Scan the part";
        Width = 1100;
        Height = 780;
        MinWidth = 860;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");

        _method.SelectedIndex = settings.RotaryScanPointer ? 0 : 1;
        _x0.Value = (decimal)Math.Clamp(settings.RotaryScanX0, 0, 220);
        _x1.Value = (decimal)Math.Clamp(settings.RotaryScanX1, 0, 220);
        _points.Value = Math.Clamp(settings.RotaryScanPoints, 1, 60);
        _stops.Value = Math.Clamp(settings.RotaryScanStops, 2, 360);
        _from.Value = (decimal)settings.RotaryScanFromDeg;
        _to.Value = (decimal)settings.RotaryScanToDeg;
        _settle.Value = (decimal)Math.Clamp(settings.RotaryScanSettleS, 0, 10);
        _axisKnown.IsChecked = settings.RotaryAxisHeightMm is not null;
        _axis.Value = (decimal)Math.Clamp(settings.RotaryAxisHeightMm ?? 20, -200, 200);
        _rod.Value = 20;
        _stockKind.SelectedIndex = (int)settings.RotaryStockKind;
        _units.SelectedIndex = settings.RotaryStockInches ? 1 : 0;
        _stockW.Value = (decimal)Math.Round(settings.RotaryStockWidthMm / (settings.RotaryStockInches ? 25.4 : 1), 3);
        _stockH.Value = (decimal)Math.Round(settings.RotaryStockHeightMm / (settings.RotaryStockInches ? 25.4 : 1), 3);
        ToolTip.SetTip(_stockKind, "As scanned: the model is what was measured. A standard stock (a 2\" square bar, a round rod): the scan only finds how it sits in the chuck (turned, off-centre, and the axis height if you don't know it), and the model is the exact shape.");
        ToolTip.SetTip(_stockW, "The stock's size: a rod's diameter, a bar's width, a hexagon across the flats.");
        ToolTip.SetTip(_stockH, "A rectangular bar's other side.");
        _stockHField = Field("Height", _stockH);
        _stockSize = new StackPanel { Spacing = 8, Children = { Field("Size", _stockW), Field("Units", _units), _stockHField } };

        ToolTip.SetTip(_method, "The pointer reads many places along the laser line at each step (calibrate it once in Curved surface). The auto-focus reads one place, where the machine focuses, but reaches any height.");
        ToolTip.SetTip(_x0, "Where along the rotary's axis to start measuring (the work area's X, mm).");
        ToolTip.SetTip(_x1, "Where along the axis to stop measuring.");
        ToolTip.SetTip(_points, "How many places to measure between them, at every step.");
        ToolTip.SetTip(_stops, "How many angles to measure at. 24 is every 15° over a whole turn.");
        ToolTip.SetTip(_from, "First angle, in degrees from where the part is now. Positive is the way a design runs down the page.");
        ToolTip.SetTip(_to, "Last angle. From 0 to 360 is a whole turn (360 itself isn't measured again, it's where it started).");
        ToolTip.SetTip(_settle, "Extra wait after each turn, for the part to stop moving before it's measured.");
        ToolTip.SetTip(_axis, "Height of the rotary's axis above the bed, the way you'd type a focus height. A part's top height minus this is how far it reaches from the axis.");
        ToolTip.SetTip(_rod, "Diameter of a round rod you scanned, measured with calipers.");

        var fromSel = Button("Use the selection", tip: "Measure along the selected shapes, and round as far as they go on the rotary page.");
        fromSel.Click += (_, _) => UseSelection();
        _scanBtn.Click += async (_, _) => await Scan();
        var stop = Button("Stop", tip: "Stop the scan. The part isn't turned back: use Return to start.");
        stop.Click += async (_, _) =>
        {
            _cts?.Cancel();
            try { if (_machine.IsConnected) await _machine.StopRotaryAsync(); }
            catch (DeviceException) { }
        };
        _returnBtn.Click += async (_, _) => await TurnBack();
        _highBtn.Click += async (_, _) => await HighSideUp();
        var setRod = Button("It was a round rod", tip: "Work out the axis height from this scan of a round rod of the diameter above.");
        setRod.Click += (_, _) => SetFromRod();
        var csv = Button("Save as CSV…", tip: "Save the readings as a spreadsheet (distance from the axis when the axis height is known).");
        csv.Click += async (_, _) => await SaveCsv();
        var stl = Button("Save as STL…", tip: "Save the stock model as a 3D file (needs a whole turn and the axis height).");
        stl.Click += async (_, _) => await SaveStl();
        var close = new Button { Content = "Close", IsCancel = true, MinWidth = 100 };
        close.Click += (_, _) => Close();
        Closing += (_, _) => { _cts?.Cancel(); SaveFields(); };

        _alongPanel = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                Field("From X (mm)", _x0), Field("To X (mm)", _x1),
                Field("Places along it", _points),
                fromSel,
            },
        };
        var side = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                Hint("The part turns a step at a time and the height of its top is measured along the laser line at each step. The laser isn't fired. Keep hands clear of the chuck."),
                new TextBlock { Text = "HOW IT MEASURES", Classes = { "h" } },
                Field("Measure with", _method),
                _methodNote,
                new TextBlock { Text = "ALONG THE AXIS", Classes = { "h" } },
                _alongPanel,
                new TextBlock { Text = "ROUND THE PART", Classes = { "h" } },
                Field("Angles", _stops),
                Field("From (°)", _from), Field("To (°)", _to),
                Field("Wait after a turn (s)", _settle),
                Row(_scanBtn, stop),
                _returnBtn,
                _busy,
                _status,
                new TextBlock { Text = "STOCK MODEL", Classes = { "h" } },
                Field("Stock", _stockKind),
                _stockSize,
                _axisKnown,
                Field("Axis height (mm)", _axis),
                Hint("Don't know it? Scan a round rod (a drill blank, a dowel), type its diameter here and click It was a round rod."),
                Field("Rod Ø (mm)", _rod), setRod,
                _results,
                _highBtn,
                Row(csv, stl),
            },
        };
        _method.SelectionChanged += (_, _) => Update();
        _axisKnown.IsCheckedChanged += (_, _) => Update();
        _stockKind.SelectionChanged += (_, _) => Update();
        _stockW.ValueChanged += (_, _) => Update();
        _stockH.ValueChanged += (_, _) => Update();
        int unitsWas = _units.SelectedIndex;
        _units.SelectionChanged += (_, _) =>
        {
            // Keep the size, in the new units.
            double k = unitsWas == 1 && _units.SelectedIndex == 0 ? 25.4 : unitsWas == 0 && _units.SelectedIndex == 1 ? 1 / 25.4 : 1;
            unitsWas = _units.SelectedIndex;
            _updating = true;
            _stockW.Value = (decimal)Math.Round((double)(_stockW.Value ?? 1) * k, 3);
            _stockH.Value = (decimal)Math.Round((double)(_stockH.Value ?? 1) * k, 3);
            _updating = false;
            Update();
        };
        _axis.ValueChanged += (_, _) => Update();
        _slice.PropertyChanged += (_, e) => { if (e.Property == Avalonia.Controls.Primitives.RangeBase.ValueProperty) Update(); };

        var left = new Grid { RowDefinitions = new("*,Auto,Auto"), RowSpacing = 8 };
        left.Children.Add(new Border { CornerRadius = new(8), ClipToBounds = true, Child = _view });
        var sliceRow = new Grid { ColumnDefinitions = new("Auto,*"), ColumnSpacing = 8 };
        sliceRow.Children.Add(new TextBlock { Text = "Place along it", VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(_slice, 1);
        sliceRow.Children.Add(_slice);
        Grid.SetRow(sliceRow, 1);
        left.Children.Add(sliceRow);
        var chartBox = new Border { CornerRadius = new(8), ClipToBounds = true, Child = _chart };
        Grid.SetRow(chartBox, 2);
        left.Children.Add(chartBox);

        var grid = new Grid { ColumnDefinitions = new("*,16,350") };
        grid.Children.Add(left);
        var sideScroll = new ScrollViewer { Content = side };
        Grid.SetColumn(sideScroll, 2);
        grid.Children.Add(sideScroll);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = new(0, 10, 0, 0), Children = { close } };
        var root = new DockPanel { Margin = new(16) };
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        root.Children.Add(grid);
        Content = root;

        Show(LoadLast());
        if (_scan is not null) _status.Text = $"The last scan ({_scan.When.LocalDateTime:g}).";
    }

    bool Pointer => _method.SelectedIndex == 0;

    double? AxisHeight => _axisKnown.IsChecked == true ? (double)(_axis.Value ?? 0) : null;

    /// <summary>The axis height the model uses: typed, or found by fitting a standard stock.</summary>
    double? ModelAxis => AxisHeight ?? (_fit is { } f && _model?.Method == "standard" ? f.AxisHeightMm : null);

    StockShape Shape()
    {
        double k = Inches ? 25.4 : 1;
        return new StockShape((StockKind)Math.Max(0, _stockKind.SelectedIndex), (double)(_stockW.Value ?? 1) * k, (double)(_stockH.Value ?? 1) * k);
    }

    void Show(RotaryScan? scan)
    {
        _scan = scan;
        _axisFound = null;
        int nx = scan?.Xs.Length ?? 0;
        _slice.Maximum = Math.Max(0, nx - 1);
        if (_slice.Value > _slice.Maximum || nx > 0 && _slice.Value == 0) _slice.Value = Math.Max(0, (nx - 1) / 2);
        Update();
    }

    void Update()
    {
        if (_updating) return;
        bool pointer = Pointer;
        var shape = Shape();
        _stockSize.IsVisible = shape.Kind != StockKind.Scanned;
        _stockHField.IsVisible = shape.Kind == StockKind.Rectangle;
        _fit = null;
        _model = _scan;
        if (shape.IsStandard && _scan is { Read: > 0 } raw && StockFitter.Fit(raw, shape, AxisHeight) is { } fit)
        {
            _fit = fit;
            _model = fit.ToScan(raw.Xs, raw.LineY, 2, raw.When, Inches);
            if (AxisHeight is null)
            {
                // Found from the fit: keep it, so the stock model (and anything carved from it) has it.
                _updating = true;
                _axisKnown.IsChecked = true;
                _axis.Value = (decimal)Math.Clamp(Math.Round(fit.AxisHeightMm, 2), -200, 200);
                _updating = false;
                _axisFound = fit.AxisHeightMm;
            }
        }
        _alongPanel.IsVisible = pointer;
        _axis.IsEnabled = _axisKnown.IsChecked == true;
        if (pointer)
        {
            var cal = SurfaceProbe.LoadCalibration();
            _methodNote.Text = cal is null
                ? "⚠ The pointer isn't calibrated yet: calibrate it once in Laser → Curved surface (a flat board), then come back."
                : $"Focus on the top of the part first: the head starts there. It reads about {Reach(cal):0} mm either side of where it's focused and tries other heights for places it misses. " +
                  $"The close-up camera was calibrated over X {cal.Aims.Min(a => a.X):0}–{cal.Aims.Max(a => a.X):0} and Y {cal.Aims.Min(a => a.Y):0}–{cal.Aims.Max(a => a.Y):0} mm: places outside that may not be read.";
        }
        else
            _methodNote.Text = "One reading an angle, where the machine focuses (the middle of the field), so put the part's axis under the middle. The head moves to the part's height each time.";
        _returnBtn.IsVisible = Math.Abs(_leftDeg) > 0.05;
        _returnBtn.Content = $"Return to start ({-_leftDeg:0.#}°)";
        int xi = (int)Math.Round(_slice.Value);
        double? axis = ModelAxis;
        _axis.IsEnabled = _axisKnown.IsChecked == true;
        _view.Show(_model, axis, xi);
        _chart.Show(_model, axis, xi, _model == _scan ? null : _scan);
        _results.Text = shape.IsStandard && _scan is { Read: > 0 }
            ? (_fit is { } f2 ? DescribeFit(f2, Inches, _axisFound) + "\n" + Describe(_model, axis, readings: false) : $"Couldn't fit a {shape.Describe(Inches)} to the scan: it needs readings at three or more angles (four if the axis height isn't known).")
            : Describe(_scan, axis);
        bool flat = _fit is { Shape.Kind: not StockKind.Round };
        _highBtn.Content = flat ? "Turn a flat face up" : "Turn the high side up";
        ToolTip.SetTip(_highBtn, flat ? "Turn the stock so the flat face nearest the top lies level on top, under the laser line." : "Turn the part so its highest point is on top, under the laser line.");
        _highBtn.IsEnabled = flat || _scan?.Highest() is not null;
        SaveStock(axis);
    }

    double? _axisFound;

    /// <summary>The stock model as it stands, for other tools to work from (only when it changed).</summary>
    void SaveStock(double? axis)
    {
        if (_model is not { Read: > 0 } m) return;
        string key = $"{m.When:O}|{m.Method}|{m.Stock}|{axis}|{(_fit is { } f ? $"{f.TurnDeg:0.###}|{f.Centre}" : "")}";
        if (key == _savedStockKey) return;
        _savedStockKey = key;
        try
        {
            Directory.CreateDirectory(AppIdentity.DataDir);
            File.WriteAllText(StockFile, (m with { AxisHeightMm = axis }).ToJson());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>How a standard stock sits, in words.</summary>
    internal static string DescribeFit(StockFit f, bool inches, double? axisFound)
    {
        string U(double mm) => inches ? $"{mm / 25.4:0.###}\"" : $"{mm:0.##} mm";
        var o = new List<string>
        {
            $"Fits a {f.Shape.Describe(inches)} to within ±{U(f.RmsMm)} ({f.Readings} readings)." +
            (f.RmsMm > 0.5 ? " ⚠ That's a loose fit: check the size, or that the stock really is that shape." : ""),
        };
        string where = f.OffAxisMm < 0.05 ? "on the axis" : $"{U(f.OffAxisMm)} off the axis (towards {((Math.Atan2(f.Centre.X, f.Centre.Y) * 180 / Math.PI) + 360) % 360:0}°)";
        o.Add(f.Shape.Kind == StockKind.Round ? $"Its middle is {where}." : $"It's turned {f.TurnDeg:0.#}° in the chuck and its middle is {where}.");
        if (axisFound is { } a) o.Add($"The axis height ({a:0.##} mm) was found from the fit and set below the stock.");
        return string.Join("\n", o);
    }

    static double Reach(Core.Camera.PointerCalibration cal) => cal.Depths.Count == 0 ? 8 : Math.Min(Math.Abs(cal.Depths.Min()), Math.Abs(cal.Depths.Max()));

    /// <summary>What a scan found, in words.</summary>
    internal static string Describe(RotaryScan? s, double? axis, bool readings = true)
    {
        if (s is null || s.Read == 0) return "No scan yet.";
        var lines = new List<string>();
        if (readings) lines.Add($"Read {s.Read} of {s.Total} points ({s.AnglesDeg.Length} angles × {s.Xs.Length} places).");
        else
        {
            if (axis is { } ax0)
            {
                var r0 = s.Radii(ax0).SelectMany(row => row).OfType<double>().ToList();
                if (r0.Count > 0)
                    lines.Add($"From the axis: {r0.Min():0.##}–{r0.Max():0.##} mm, so it needs a {2 * r0.Max():0.#} mm circle to turn in.");
            }
            return string.Join("\n", lines);
        }
        if (s.Highest() is { } hi && s.Lowest() is { } lo)
            lines.Add($"Top heights {lo.H:0.##}–{hi.H:0.##} mm. Highest at {hi.Deg:0.#}° (X {hi.X:0.#}).");
        var fits = Enumerable.Range(0, s.Xs.Length).Select(i => (i, f: s.FitRound(i))).Where(p => p.f is not null).ToList();
        if (fits.Count > 0 && s.WholeTurn)
        {
            var worst = fits.MaxBy(p => p.f!.Value.Swing);
            var f = worst.f!.Value;
            lines.Add($"It rises and falls up to {f.Swing:0.##} mm as it turns (at X {s.Xs[worst.i]:0.#}). As if round, it sits {f.Offset:0.##} mm off the axis there, high side at {f.HighDeg:0}°" +
                      (f.RmsMm > 0.3 ? $"; it isn't round (±{f.RmsMm:0.##} mm from round)." : "."));
        }
        if (axis is { } ax)
        {
            var r = s.Radii(ax).SelectMany(row => row).OfType<double>().ToList();
            if (r.Count > 0)
                lines.Add($"From the axis: {r.Min():0.##}–{r.Max():0.##} mm, so it needs a {2 * r.Max():0.#} mm circle to turn in, and the cleanest round you could cut it to is {2 * r.Min():0.#} mm.");
        }
        return string.Join("\n", lines);
    }

    void SaveFields()
    {
        _settings.RotaryScanPointer = Pointer;
        _settings.RotaryScanX0 = (double)(_x0.Value ?? 95);
        _settings.RotaryScanX1 = (double)(_x1.Value ?? 125);
        _settings.RotaryScanPoints = (int)(_points.Value ?? 7);
        _settings.RotaryScanStops = (int)(_stops.Value ?? 24);
        _settings.RotaryScanFromDeg = (double)(_from.Value ?? 0);
        _settings.RotaryScanToDeg = (double)(_to.Value ?? 360);
        _settings.RotaryScanSettleS = (double)(_settle.Value ?? 0.5m);
        _settings.RotaryAxisHeightMm = AxisHeight;
        var shape = Shape();
        _settings.RotaryStockKind = shape.Kind;
        _settings.RotaryStockInches = Inches;
        _settings.RotaryStockWidthMm = shape.WidthMm;
        _settings.RotaryStockHeightMm = shape.HeightMm;
        _settings.Save();
    }

    /// <summary>The plan the fields describe.</summary>
    RotaryScanPlan Plan()
    {
        double x0 = (double)(_x0.Value ?? 95), x1 = (double)(_x1.Value ?? 125);
        if (x1 < x0) (x0, x1) = (x1, x0);
        double from = (double)(_from.Value ?? 0), to = (double)(_to.Value ?? 360);
        if (to < from) (from, to) = (to, from);
        var xs = Pointer ? RotaryScanPlan.Places(x0, x1, (int)(_points.Value ?? 7)) : [MachineProfile.F2Ultra.FieldW / 2];
        double line = Math.Clamp(_settings.RotaryLineMm, 1, MachineProfile.F2Ultra.FieldH - 1);
        return new RotaryScanPlan(from, Math.Min(to, from + 360), (int)(_stops.Value ?? 24), xs, line, Pointer ? "pointer" : "autofocus");
    }

    /// <summary>Along the selected shapes, and round as far as they reach on the rotary page.</summary>
    void UseSelection()
    {
        IReadOnlySet<Guid> ids = _doc.Selection.Count > 0 ? _doc.Selection : _doc.State.Shapes.Select(s => s.Id).ToHashSet();
        var b = _doc.State.Bounds(ids);
        if (b.IsEmpty) { _status.Text = "Select the shapes that cover the area to scan first."; return; }
        _x0.Value = (decimal)Math.Clamp(Math.Round(b.MinX, 1), 0, 220);
        _x1.Value = (decimal)Math.Clamp(Math.Round(b.MaxX, 1), 0, 220);
        if (WorkModes.From(_settings) is WorkMode.Rotary)
        {
            // What faces the laser at the start of a job: the rotary start line, or the middle of the whole design.
            var all = _doc.State.Bounds(_doc.State.Shapes.Select(s => s.Id).ToHashSet());
            double origin = _doc.State.Guides.FirstOrDefault(g => g.RotaryStart && !g.Vertical)?.At ?? (all.MinY + all.MaxY) / 2;
            double circ = Math.PI * Math.Max(1, _settings.RotaryDiameterMm);
            double from = (b.MinY - origin) / circ * 360, to = (b.MaxY - origin) / circ * 360;
            if (to - from >= 350) { from = 0; to = 360; }
            _from.Value = (decimal)Math.Round(from, 1);
            _to.Value = (decimal)Math.Round(to, 1);
            _status.Text = $"Along X {b.MinX:0.#}–{b.MaxX:0.#} mm and round from {from:0.#}° to {to:0.#}°, with the part turned to where a job starts (its start line facing the laser).";
        }
        else _status.Text = $"Along X {b.MinX:0.#}–{b.MaxX:0.#} mm.";
    }

    async Task Scan()
    {
        SaveFields();
        if (_machine.Client is not { IsConnected: true } client) { _status.Text = "Connect to the machine first."; return; }
        if (WorkModes.From(_settings) is not WorkMode.Rotary) { _status.Text = "Choose the rotary in Laser → Work mode first."; return; }
        if (_machine.Mode is "P_WORKING" or "P_PAUSE" or "P_FRAMING" or "P_WORK") { _status.Text = "The machine has a job on it. Wait for it to finish (or stop it) first."; return; }
        var plan = Plan();
        Core.Camera.PointerCalibration? cal = null;
        double startFocus = 0;
        if (Pointer)
        {
            if ((cal = SurfaceProbe.LoadCalibration()) is null) { _status.Text = "Calibrate the pointer first (Laser → Curved surface → Calibrate the pointer)."; return; }
            if (_machine.Distance is not { } focus) { _status.Text = "Focus on the top of the part first (auto-focus, or type its height in the machine panel)."; return; }
            startFocus = focus;
        }
        var angles = plan.Angles();
        string what = $"The part turns to {angles.Length} angles ({angles[0]:0.#}° to {angles[^1]:0.#}°" +
                      (angles.Length > 1 ? $", {angles[1] - angles[0]:0.#}° apart" : "") + ") and " +
                      (Pointer ? $"the head moves up and down while the red pointer measures {plan.Xs.Count} places along the laser line at each."
                               : "the machine auto-focuses on its top at each.") +
                      " Then it turns back to where it started.\n\nThe laser isn't fired. Keep hands clear of the chuck; Stop stops it where it is.";
        if (Math.Abs(_leftDeg) > 0.05)
            what += $"\n\nThe last scan was stopped {_leftDeg:0.#}° from its start: this one starts from where the part is now.";
        if (!await Dialogs.Confirm(this, "Scan the part", what, "Scan", "Cancel")) return;

        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        _busy.IsVisible = true;
        _scanBtn.IsEnabled = false;
        var progress = new Progress<string>(s => _status.Text = s);
        double leftBefore = _leftDeg;
        void Turned(double deg) => Dispatch(() => { _leftDeg = leftBefore + deg; Update(); });
        void Partial(RotaryScan s) => Dispatch(() => Show(s));
        try
        {
            // Asleep, the machine answers but doesn't turn or measure.
            if (_machine.Mode == "P_SLEEP")
            {
                await client.ReturnToIdleAsync();
                await Task.Delay(1500, cts.Token);
            }
            RotaryScan scan;
            double settle = (double)(_settle.Value ?? 0.5m);
            if (Pointer)
            {
                var probe = new SurfaceProbe(client);
                var rig = new PointerScanRig(_settings, _machine, settle, probe, cal!, plan.LineY, startFocus, progress);
                RotaryScan? got = null;
                await probe.RunAsync(async () => got = await RotaryScanner.RunAsync(plan, rig, progress, Turned, Partial, cts.Token), cts.Token);
                scan = got!;
            }
            else
                scan = await RotaryScanner.RunAsync(plan, new AutoFocusScanRig(_settings, _machine, settle), progress, Turned, Partial, cts.Token);
            _leftDeg = leftBefore;
            SaveLast(scan);
            Show(scan);
            _status.Text = $"Done: read {scan.Read} of {scan.Total} points, and turned back to where it started." +
                           (scan.Read < scan.Total ? " Gaps are filled from the readings either side." : "");
        }
        catch (RotaryScanStopped e)
        {
            _leftDeg = leftBefore + e.LeftDeg;
            if (e.SoFar.Read > 0) { SaveLast(e.SoFar); Show(e.SoFar); }
            _status.Text = (e.Cancelled ? "Stopped." : $"The scan stopped: {e.Message}") +
                           (Math.Abs(_leftDeg) > 0.05 ? $" The part was left {_leftDeg:0.#}° from where it started: click Return to start to turn it back." : "");
        }
        catch (OperationCanceledException) { _status.Text = "Stopped."; }
        catch (Exception e) when (e is DeviceException or InvalidOperationException or IOException or FormatException or ArgumentOutOfRangeException)
        {
            _status.Text = e.Message + (Math.Abs(_leftDeg) > 0.05 ? $" The part was left {_leftDeg:0.#}° from where it started." : "");
        }
        finally
        {
            _busy.IsVisible = false;
            _scanBtn.IsEnabled = true;
            Update();
        }
    }

    static void Dispatch(Action a) => Avalonia.Threading.Dispatcher.UIThread.Post(a);

    async Task TurnBy(double degrees, string done)
    {
        if (!_machine.IsConnected) { _status.Text = "Connect to the machine first."; return; }
        try
        {
            double u = await RotaryTurn.FeedUnitsAsync(_settings, _machine, degrees)
                       ?? throw new InvalidOperationException("Choose the rotary in Laser → Work mode first.");
            await _machine.TurnRotaryNowAsync(u);
            _status.Text = done;
        }
        catch (Exception e) when (e is DeviceException or InvalidOperationException) { _status.Text = e.Message; }
    }

    async Task TurnBack()
    {
        double back = -_leftDeg;
        await TurnBy(back, $"Turned {back:0.#}° back to where the scan started.");
        if (!_status.Text!.StartsWith("Turned")) return;
        _leftDeg = 0;
        Update();
    }

    async Task HighSideUp()
    {
        if (_fit is { Shape.Kind: not StockKind.Round } fit)
        {
            double t = fit.FlatUpDeg() - _leftDeg;
            t = ((t % 360) + 540) % 360 - 180;
            if (!await Dialogs.Confirm(this, "Turn a flat face up", $"Turn the stock {t:0.#}° so a flat face lies level on top? Keep hands clear of the chuck.", "Turn it", "Cancel"))
                return;
            await TurnBy(t, $"Turned {t:0.#}°: a flat face is on top. (Turn it {-t:0.#}° to go back to where the scan started.)");
            return;
        }
        if (_scan?.Highest() is not { } hi) return;
        // A finished scan leaves the part where it started; one stopped part way leaves it _leftDeg on.
        double turn = hi.Deg - _leftDeg;
        turn = ((turn % 360) + 540) % 360 - 180; // the shorter way
        if (!await Dialogs.Confirm(this, "Turn the high side up", $"Turn the part {turn:0.#}° so its highest point (read at {hi.Deg:0.#}°) is on top? Keep hands clear of the chuck.", "Turn it", "Cancel"))
            return;
        await TurnBy(turn, $"Turned {turn:0.#}°: the high side is on top. (Turn it {-turn:0.#}° to go back to where the scan started.)");
    }

    void SetFromRod()
    {
        if (_scan is null || _scan.Read == 0) { _status.Text = "Scan a round rod first."; return; }
        if (!_scan.WholeTurn) { _status.Text = "Scan the rod over a whole turn (0 to 360°) for this."; return; }
        if (_scan.AxisFromRod((double)(_rod.Value ?? 20)) is not { } ax) { _status.Text = "Too little was read to work it out."; return; }
        _axisKnown.IsChecked = true;
        _axis.Value = (decimal)Math.Clamp(Math.Round(ax, 2), -200, 200);
        SaveFields();
        _status.Text = $"Axis height set to {ax:0.##} mm. It stays the same until the rotary is moved, so scan the real part next.";
    }

    async Task SaveCsv()
    {
        if (_scan is null || _scan.Read == 0) { _status.Text = "Nothing scanned yet."; return; }
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save the scan",
            DefaultExtension = "csv",
            SuggestedFileName = $"rotary-scan_{_scan.When.LocalDateTime:yyyyMMdd_HHmm}.csv",
        });
        if (file?.TryGetLocalPath() is { } path) await File.WriteAllTextAsync(path, (_model ?? _scan).ToCsv(ModelAxis));
    }

    async Task SaveStl()
    {
        if (_scan is null || _scan.Read == 0) { _status.Text = "Nothing scanned yet."; return; }
        if (ModelAxis is not { } ax) { _status.Text = "Set the axis height first."; return; }
        if ((_model ?? _scan).ToStl(ax) is not { } bytes) { _status.Text = "A 3D file needs a whole turn (0 to 360°) and at least two places along the axis."; return; }
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save the stock model",
            DefaultExtension = "stl",
            SuggestedFileName = $"stock_{_scan.When.LocalDateTime:yyyyMMdd_HHmm}.stl",
        });
        if (file?.TryGetLocalPath() is { } path) await File.WriteAllBytesAsync(path, bytes);
    }
}
