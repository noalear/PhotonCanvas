using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using PhotonCanvas.App;
using PhotonCanvas.App.Addons;
using PhotonCanvas.Core;
using PhotonCanvas.Core.Imaging;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Geometry;
using static PhotonCanvas.App.UiKit;
using PathGeometry = PhotonCanvas.Doc.PathGeometry;

namespace PhotonCanvas.Addon.ObjectJig;

/// <summary>Saved laser settings the window offers (from the material library).</summary>
sealed record JigRecipe(string Label, Layer Settings, double? ThicknessMm);

/// <summary>
/// The object jig maker: photograph the object (or use its drawn outline), choose how the jig is made,
/// see it with the design on every object, and make it. Nothing changes in the document until
/// Make; Make is one undo step. Only the camera is used: the head never moves and the laser stays off.
/// </summary>
public sealed class JigWindow : Window
{
    const double MmPerPx = 0.4;

    readonly IAddonHost _host;
    readonly JigStore _store;
    readonly List<JigRecipe> _cutRecipes, _engraveRecipes;
    PathGeometry? _object;
    Guid? _objectShape;
    JigPlan? _plan;
    bool _busyNow, _loading;

    // Object
    readonly NumericUpDown _distance = Number(0, 200, 0.1m, "0.0");
    readonly NumericUpDown _height = Number(0.5m, 200, 0.5m, "0.0#");
    readonly TextBlock _cameraState = new() { TextWrapping = TextWrapping.Wrap };
    readonly Slider _tolerance = new() { Minimum = 3, Maximum = 60, Value = 18, HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly Button _photo = Wide("Take photo and find the object", primary: true);
    readonly Button _useSelection = Wide("Use the selected outline");
    readonly ProgressBar _busy = new() { IsIndeterminate = true, IsVisible = false, Height = 4 };
    readonly TextBlock _found = new() { TextWrapping = TextWrapping.Wrap };
    readonly ComboBox _saved = new() { HorizontalAlignment = HorizontalAlignment.Stretch, PlaceholderText = "Open a saved jig…" };
    readonly Button _save = new() { Content = "Save jig" }, _delete = new() { Content = "Delete" };

    // Jig
    readonly TextBox _name = new() { Watermark = "e.g. Pen", HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly ComboBox _build = Combo("One sheet, cut through", "Stacked sheets on a solid base", "Stacked sheets, no base");
    readonly NumericUpDown _sheet = Number(0.5m, 30, 0.5m, "0.0#"), _depth = Number(0.5m, 100, 0.5m, "0.0#");
    readonly NumericUpDown _gap = Number(0, 10, 0.1m, "0.0#"), _round = Number(0, 10, 0.5m, "0.0#");
    readonly ComboBox _notches = Combo("A notch each side", "One notch, at the front", "No notches");
    readonly NumericUpDown _notch = Number(0, 60, 1, "0.#");
    readonly CheckBox _straight = new() { Content = "Lay the object straight (long side left to right)", IsChecked = true };
    readonly NumericUpDown _cols = Number(1, 50, 1, "0"), _rows = Number(1, 50, 1, "0");
    readonly NumericUpDown _wall = Number(0, 100, 0.5m, "0.0#"), _margin = Number(2, 200, 1, "0.#"), _corner = Number(0, 50, 1, "0.#");
    readonly NumericUpDown _marker = Number(0, 60, 1, "0.#"), _pins = Number(0, 12, 0.5m, "0.0#");
    readonly CheckBox _engraveName = new() { Content = "Engrave the name on the jig", IsChecked = true };
    readonly NumericUpDown _labelSize = Number(2, 30, 0.5m, "0.0#");
    readonly CheckBox _whereItLay = new() { Content = "Put the jig where the object lay", IsChecked = true };
    readonly NumericUpDown _cx = Number(-2000, 2000, 1, "0.#"), _cy = Number(-2000, 2000, 1, "0.#");
    readonly ComboBox _cutWith = new() { HorizontalAlignment = HorizontalAlignment.Stretch }, _engraveWith = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly Control _depthRow, _pinsRow;

    // Design
    readonly TextBlock _designInfo = new() { TextWrapping = TextWrapping.Wrap };
    readonly ComboBox _placement = Combo("Where it is on the canvas", "In the middle of the object");
    readonly CheckBox _turn = new() { Content = "Turn it along the object", IsChecked = true };
    readonly CheckBox _fit = new() { Content = "Size it to fit inside the object", IsChecked = false };
    readonly NumericUpDown _fitMargin = Number(0, 50, 0.5m, "0.0#");
    readonly CheckBox _cutPages = new() { Content = "Make the jig's pages (untick when you have this jig already)", IsChecked = true };

    // Result
    readonly JigPreview _preview = new() { Height = 400 };
    readonly TextBlock _summary = new() { TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.SemiBold };
    readonly TextBlock _warnings = new() { TextWrapping = TextWrapping.Wrap };
    readonly Button _make = new() { Content = "Make the jig", MinWidth = 140, IsEnabled = false, Classes = { "primary" } };

    internal JigWindow(IAddonHost host)
    {
        _host = host;
        _store = new JigStore(host.DataFolder);
        Title = "Object jig";
        Width = 1040;
        Height = 780;
        MinWidth = 780;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");

        _cutRecipes = [.. host.Library.Materials.SelectMany(m => m.Recipes.Where(r => r.Settings.Mode == LayerMode.Cut)
            .Select(r => new JigRecipe($"{m.Label} · {r.Title}", r.Settings.ApplyTo(Layer.Default(0, LayerMode.Cut)), m.ThicknessMm)))];
        _engraveRecipes = [.. host.Library.Materials.SelectMany(m => m.Recipes.Where(r => r.Settings.Mode is LayerMode.Fill or LayerMode.Score)
            .Select(r => new JigRecipe($"{m.Label} · {r.Title}", r.Settings.ApplyTo(Layer.Default(0, r.Settings.Mode)), m.ThicknessMm)))];
        _cutWith.ItemsSource = _cutRecipes.Select(r => r.Label).Prepend("Default cut settings").ToList();
        _engraveWith.ItemsSource = _engraveRecipes.Select(r => r.Label).Prepend("Default engrave settings").ToList();
        _cutWith.SelectedIndex = 0;
        _engraveWith.SelectedIndex = _engraveRecipes.Count > 0 ? 1 : 0;
        _cutWith.SelectionChanged += (_, _) => { if (Cut()?.ThicknessMm is { } t && !_loading) _sheet.Value = (decimal)t; Replan(); };

        _distance.Value = host.FocusDistanceMm is { } d ? (decimal)Math.Round(d, 1) : null;
        _height.Value = 10; _sheet.Value = 3; _depth.Value = 6; _gap.Value = 0.6m; _round.Value = 1; _notch.Value = 18;
        _cols.Value = 1; _rows.Value = 1; _wall.Value = 6; _margin.Value = 10; _corner.Value = 3; _marker.Value = 10; _pins.Value = 3;
        _labelSize.Value = 5; _fitMargin.Value = 2;
        _build.SelectedIndex = 0; _notches.SelectedIndex = 0; _placement.SelectedIndex = 0;
        var area = WorkArea;
        _cx.Value = (decimal)Math.Round(area.Center.X); _cy.Value = (decimal)Math.Round(area.Center.Y);

        _distance.ValueChanged += (_, _) => CheckCamera();
        _height.ValueChanged += (_, _) => { CheckCamera(); SuggestDepth(); Replan(); };
        _photo.Click += async (_, _) => await FindByCamera();
        _useSelection.Click += (_, _) => UseSelection();
        _saved.SelectionChanged += (_, _) => OpenSaved();
        _save.Click += async (_, _) => await Save();
        _delete.Click += async (_, _) => await Delete();
        foreach (var n in new[] { _sheet, _depth, _gap, _round, _notch, _cols, _rows, _wall, _margin, _corner, _marker, _pins, _labelSize, _cx, _cy, _fitMargin })
            n.ValueChanged += (_, _) => Replan();
        foreach (var c in new[] { _build, _notches, _engraveWith, _placement }) c.SelectionChanged += (_, _) => Replan();
        foreach (var c in new[] { _straight, _engraveName, _whereItLay, _turn, _fit, _cutPages }) c.IsCheckedChanged += (_, _) => Replan();
        _build.SelectionChanged += (_, _) => SuggestDepth();
        _name.TextChanged += (_, _) => Replan();
        _make.Click += (_, _) => Make();
        var close = new Button { Content = "Close", IsCancel = true, MinWidth = 100 };
        close.Click += (_, _) => Close();
        ToolTip.SetTip(_tolerance, "How different from the bed a colour must be to count as the object. Raise it if the bed's honeycomb shows up; lower it if a pale or shiny object is missed.");
        ToolTip.SetTip(_saved, "Jigs you saved: the object's outline and the jig's settings. Open one to make the objects page for a new design.");

        _depthRow = Field("Depth (mm)", _depth, tip: "How deep the object sits: rounded up to whole sheets. About half the object's height holds it well.");
        _pinsRow = Field("Dowel holes (mm)", _pins, tip: "Holes in three corners of every sheet: push dowels or cocktail sticks through them to line the stack up while the glue sets. 0 = none.");

        var settings = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap,
                    Text = "A sheet with a pocket the object drops into, so every one is marked in the same place. " +
                           "Lay the object on the bed, flat side down, on its own.",
                },
                Row(_saved, _save, _delete).With(g => g.ColumnDefinitions = new("*,Auto,Auto")),
                Heading("1  THE OBJECT"),
                Field("Distance (mm)", _distance, tip: "From the laser to the bed the object lies on (as the machine's focus measures it)."),
                Field("Height (mm)", _height, tip: "How tall the object is. The camera is lined up halfway up it, where its outline is widest for most objects."),
                _cameraState,
                Field("Tolerance", _tolerance),
                _photo,
                _useSelection,
                _busy,
                _found,
                Heading("2  THE JIG"),
                Field("Name", _name, tip: "Names the jig's pages and the saved jig, and is engraved on it."),
                Field("Made as", _build, tip: "One sheet: the pockets are cut through one sheet as thick as you want them deep. Stacked: several sheets cut the same and glued, with or without a solid base."),
                Field("Sheet (mm)", _sheet, tip: "The thickness of one sheet."),
                _depthRow,
                Field("Gap (mm)", _gap, tip: "Room round the object, each side, so it drops in. 0.5–1 mm suits a camera outline; less for a drawn one."),
                Field("Round inside (mm)", _round, tip: "Inside corners and narrow slots this size are rounded off and filled: the laser can't cut a slot thinner than its beam, and the object drops in more easily."),
                Field("Finger notches", _notches),
                Field("Notch size (mm)", _notch, tip: "The notch's diameter: 15–20 mm fits a fingertip to lift the object out."),
                _straight,
                Field("Across", _cols, tip: "Pockets side by side: one jig for a batch."),
                Field("Down", _rows, tip: "Rows of pockets, back to front."),
                Field("Wall (mm)", _wall, tip: "Wood left between two pockets."),
                Field("Margin (mm)", _margin, tip: "Wood round the pockets."),
                Field("Corners (mm)", _corner, tip: "The jig's corner radius."),
                Field("Cut-off corner (mm)", _marker, tip: "The back-left corner is cut off, so the jig only goes one way round and the camera can tell which way it lies. 0 = square."),
                _pinsRow,
                _engraveName,
                Field("Name size (mm)", _labelSize),
                _whereItLay,
                Field("Middle X (mm)", _cx, tip: "Where the jig's middle goes on the bed."),
                Field("Middle Y (mm)", _cy),
                Field("Cut with", _cutWith, tip: "Settings for cutting the sheets, from your material library (its thickness fills in Sheet)."),
                Field("Engrave with", _engraveWith, tip: "Settings for the name, and for the object's outline on the base sheet."),
                Heading("3  THE DESIGN"),
                _designInfo,
                Field("Put the design", _placement, tip: "Where it is on the canvas: over the object's photo or outline, as you placed it. In the middle: its middle on the object's middle."),
                _turn,
                _fit,
                Field("Fit margin (mm)", _fitMargin, tip: "How far in from the object's edge the design stays when it's sized to fit."),
                _cutPages,
            },
        };
        var result = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                Heading("4  PREVIEW"),
                new Border { Background = Brush("Bg0"), CornerRadius = new(6), Child = _preview, ClipToBounds = true },
                _summary,
                _warnings,
            },
        };
        var grid = new Grid { ColumnDefinitions = new("420,16,*"), RowDefinitions = new("*,Auto") };
        grid.Children.Add(new ScrollViewer { Content = settings, Padding = new(0, 0, 18, 0) });
        Grid.SetColumn(result, 2);
        grid.Children.Add(result);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = new(0, 12, 0, 0), Children = { close, _make } };
        Grid.SetRow(buttons, 1); Grid.SetColumnSpan(buttons, 3);
        grid.Children.Add(buttons);
        Content = new Border { Padding = new Thickness(20), Child = grid };

        RefreshSaved();
        CheckCamera();
        if (SelectedOutline() is not null) UseSelection();
        else Replan();
    }

    Rect2 WorkArea => new(0, 0, _host.Machine.FieldW, _host.Machine.FieldH);

    JigRecipe? Cut() => _cutWith.SelectedIndex >= 1 && _cutWith.SelectedIndex - 1 < _cutRecipes.Count ? _cutRecipes[_cutWith.SelectedIndex - 1] : null;
    JigRecipe? Engrave() => _engraveWith.SelectedIndex >= 1 && _engraveWith.SelectedIndex - 1 < _engraveRecipes.Count ? _engraveRecipes[_engraveWith.SelectedIndex - 1] : null;

    static double V(NumericUpDown n, double fallback) => n.Value is { } v ? (double)v : fallback;

    JigOptions Options() => new()
    {
        Build = _build.SelectedIndex switch { 1 => JigBuild.StackedWithBase, 2 => JigBuild.StackedNoBase, _ => JigBuild.OneSheet },
        ObjectHeightMm = V(_height, 10), SheetMm = V(_sheet, 3), DepthMm = V(_depth, 6),
        GapMm = V(_gap, 0.6), RoundInsideMm = V(_round, 1),
        Notches = _notches.SelectedIndex switch { 1 => JigNotches.OneSide, 2 => JigNotches.None, _ => JigNotches.BothSides },
        NotchDiameterMm = V(_notch, 18), LayStraight = _straight.IsChecked == true,
        Columns = (int)V(_cols, 1), Rows = (int)V(_rows, 1), WallMm = V(_wall, 6), MarginMm = V(_margin, 10),
        CornerRadiusMm = V(_corner, 3), MarkerMm = V(_marker, 10), PinDiameterMm = V(_pins, 3),
        Name = _name.Text?.Trim() ?? "", EngraveName = _engraveName.IsChecked == true, LabelSizeMm = V(_labelSize, 5),
        Centre = _whereItLay.IsChecked == true ? null : new Vec2(V(_cx, 0), V(_cy, 0)),
        CutSettings = Cut()?.Settings,
        EngraveSettings = Engrave()?.Settings,
        Placement = _placement.SelectedIndex == 1 ? DesignPlacement.Centred : DesignPlacement.AsPlaced,
        TurnDesign = _turn.IsChecked == true, FitDesign = _fit.IsChecked == true, FitMarginMm = V(_fitMargin, 2),
    };

    /// <summary>The biggest closed outline among the selected shapes (the object's drawn or traced outline), if any.</summary>
    PathShape? SelectedOutline()
    {
        var doc = _host.Document;
        return doc.State.Shapes.Where(s => doc.Selection.Contains(s.Id)).OfType<PathShape>()
            .Where(p => p is not TextShape && p.World.Subpaths.Any(sp => sp.Closed))
            .Select(p => (P: p, A: ShapeGeometry.Area(p.World)))
            .Where(x => x.A > 1)
            .OrderByDescending(x => x.A).Select(x => x.P).FirstOrDefault();
    }

    /// <summary>The design: the selected shapes, without the one used as the object's outline.</summary>
    List<Shape> Design()
    {
        var doc = _host.Document;
        var ids = Ops.WithGroups(doc.State, doc.Selection);
        return [.. doc.State.Shapes.Where(s => ids.Contains(s.Id) && s.Id != _objectShape)];
    }

    bool CheckCamera()
    {
        string text;
        bool ok = false;
        if (!_host.IsConnected) text = "Connect to the machine to photograph the object (or select its drawn outline).";
        else if (_distance.Value is not { } d) text = "Enter the distance to the bed the object lies on.";
        else if (PhotoDistance((double)d) is not { } at || at <= 0) text = "The object is taller than the distance: check both.";
        else if (!_host.CameraCalibratedFor(at)) text = $"The camera isn't calibrated near {at:0.#} mm (halfway up the object) yet: Camera tab → Calibrate.";
        else { ok = true; text = $"Camera ready (lined up at {at:0.#} mm, halfway up the object)."; }
        _cameraState.Text = (ok ? "✓ " : "⚠ ") + text;
        _cameraState.Foreground = Brush(ok ? "Ok" : "Warn");
        _photo.IsEnabled = ok && !_busyNow;
        return ok;
    }

    double? PhotoDistance(double bed) => bed - V(_height, 0) / 2;

    async Task FindByCamera()
    {
        if (!CheckCamera() || _distance.Value is not { } d || PhotoDistance((double)d) is not { } at) return;
        var area = WorkArea;
        _busyNow = true;
        _photo.IsEnabled = _make.IsEnabled = false;
        _busy.IsVisible = true;
        _found.Text = "Taking a photo of the bed…";
        try
        {
            using var bmp = await _host.PhotographAreaAsync(area, MmPerPx, at);
            var photo = CameraImaging.ToRaster(bmp);
            _found.Text = "Outlining the object…";
            double tol = _tolerance.Value;
            var things = await Task.Run(() => JigMaker.FromMask(JigFinder.NotBed(photo, tol), photo.Width, photo.Height, area, MmPerPx, 50));
            if (things.Count == 0)
            {
                _found.Text = "Nothing found. Check the object stands out from the bed (try another tolerance), and that it's inside the work area.";
                return;
            }
            SetObject(things[0], null);
            var b = things[0].Bounds(0.1);
            _found.Text = $"Found the object: {b.Width:0.#} × {b.Height:0.#} mm." +
                (things.Count > 1 ? $" {things.Count - 1} smaller thing{(things.Count == 2 ? "" : "s")} on the bed left out (the biggest is taken as the object)." : "");
        }
        catch (Exception e) when (e is InvalidOperationException or FormatException or IOException || e.GetType().Name == "DeviceException")
        {
            _found.Text = $"Couldn't take the photo: {e.Message}";
        }
        finally
        {
            _busyNow = false;
            _busy.IsVisible = false;
            CheckCamera();
            Replan();
        }
    }

    void UseSelection()
    {
        if (SelectedOutline() is not { } shape)
        {
            _found.Text = "Select the object's outline first (drawn, or traced from a photo): the biggest closed shape selected is taken as the object.";
            return;
        }
        SetObject(shape.World, shape.Id);
        var b = shape.World.Bounds(0.1);
        _found.Text = $"The object is the selected outline \"{shape.Name}\": {b.Width:0.#} × {b.Height:0.#} mm. Other selected shapes are the design.";
    }

    void SetObject(PathGeometry outline, Guid? shapeId)
    {
        _object = outline;
        _objectShape = shapeId;
        Replan();
    }

    /// <summary>Stacked: about half the object's height, in whole sheets.</summary>
    void SuggestDepth()
    {
        if (_loading || _build.SelectedIndex == 0) return;
        double sheet = Math.Max(0.5, V(_sheet, 3)), h = V(_height, 10);
        _depth.Value = (decimal)(Math.Max(1, Math.Round(h / 2 / sheet)) * sheet);
    }

    void Replan()
    {
        if (_loading) return;
        bool stacked = _build.SelectedIndex != 0;
        _depthRow.IsVisible = _pinsRow.IsVisible = stacked;
        _cx.IsEnabled = _cy.IsEnabled = _whereItLay.IsChecked != true;
        bool centred = _placement.SelectedIndex == 1;
        _turn.IsEnabled = _fit.IsEnabled = centred;
        _fitMargin.IsEnabled = centred && _fit.IsChecked == true;
        _save.IsEnabled = _object is not null;

        var design = Design();
        _designInfo.Text = design.Count == 0
            ? "Nothing selected to mark: the objects page gets the objects' outlines to design on. To mark a design, select it before opening this window."
            : $"The design: {design.Count} selected shape{(design.Count == 1 ? "" : "s")}, marked on every object (texts and codes with fields count up from one object to the next).";

        _plan = null;
        if (_object is null)
        {
            _summary.Text = "Photograph the object, select its outline, or open a saved jig.";
            _warnings.Text = "";
            _preview.Show(null, [], []);
            _make.IsEnabled = false;
            return;
        }
        var o = Options();
        try { _plan = JigMaker.Plan(_object, o, WorkArea); }
        catch (InvalidOperationException e) { _summary.Text = e.Message; _warnings.Text = ""; _preview.Show(null, [], []); _make.IsEnabled = false; return; }
        var p = _plan;
        if (_whereItLay.IsChecked == true)
        {
            _loading = true;
            _cx.Value = (decimal)Math.Round(p.Board.Center.X, 1);
            _cy.Value = (decimal)Math.Round(p.Board.Center.Y, 1);
            _loading = false;
        }
        int n = p.ObjectMoves.Count;
        string how = o.Build switch
        {
            JigBuild.OneSheet => $"cut from one {o.SheetMm:0.##} mm sheet",
            JigBuild.StackedWithBase => $"{o.CutSheets} sheet{(o.CutSheets == 1 ? "" : "s")} of {o.SheetMm:0.##} mm with the pockets, on a solid base: {o.ThicknessMm:0.##} mm in all",
            _ => $"{o.CutSheets} sheet{(o.CutSheets == 1 ? "" : "s")} of {o.SheetMm:0.##} mm with the pockets cut through",
        };
        _summary.Text = $"Jig {p.Board.Width:0} × {p.Board.Height:0} mm, {n} pocket{(n == 1 ? "" : "s")} {o.PocketDepthMm:0.##} mm deep, {how}.";
        var warn = p.Warnings.ToList();
        var points = JigMaker.DesignPoints(design);
        var (onObject, fitWarning) = JigMaker.DesignOnObject(_object, points, o);
        if (fitWarning is not null) warn.Add(fitWarning);
        if (design.Count > 0 && !centred && !JigMaker.Outers(_object).Any(g => Rect2.Bounds(points).Intersects(g.Bounds(0.1))))
            warn.Add("The design isn't over the object on the canvas, so it would land beside each pocket. Move it onto the object (its photo or outline), or put it in the middle of the object.");
        if (_cutPages.IsChecked != true) warn.Add("Only the objects page is made: lay your jig on the bed and use Find the jig to line it up.");
        _warnings.Text = string.Join("\n", warn.Select(w => "⚠ " + w));
        _warnings.Foreground = Brush("Warn");
        var designLoops = DesignLoops(design, onObject);
        _preview.Show(p, designLoops, p.ObjectMoves);
        _make.IsEnabled = true;
    }

    /// <summary>The design's outlines on the object as it lay (for the preview).</summary>
    static List<List<Vec2>> DesignLoops(List<Shape> design, Affine onObject)
    {
        var o = new List<List<Vec2>>();
        foreach (var s in design)
        {
            if (s is PathShape p && s is not ImageShape)
                foreach (var f in p.World.Flatten(0.2)) o.Add(f.Closed ? [.. f.Points.Select(onObject.Apply), onObject.Apply(f.Points[0])] : [.. f.Points.Select(onObject.Apply)]);
            else if (!s.Bounds.IsEmpty)
            {
                var b = s.Bounds;
                o.Add([.. new[] { b.TopLeft, new Vec2(b.MaxX, b.MinY), b.BottomRight, new Vec2(b.MinX, b.MaxY), b.TopLeft }.Select(onObject.Apply)]);
            }
        }
        return o;
    }

    void Make()
    {
        if (_object is null) return;
        try
        {
            var design = Design();
            var o = Options();
            var s = JigMaker.Make(_host.Document.State, _object, o, design, WorkArea, cutPages: _cutPages.IsChecked == true);
            _host.Document.Commit("Object jig", s);
            _host.Status(_cutPages.IsChecked == true
                ? $"\"{o.Title}\" is on new pages: cut the jig from the first, leave it on the bed, put the objects in, then mark them from \"{o.Title} · objects\"."
                : $"\"{o.Title} · objects\" is ready: lay the jig on the bed and use Find the jig (Camera tab) to line the design up with it.");
            Close();
        }
        catch (InvalidOperationException e) { _summary.Text = e.Message; }
    }

    // ---- saved jigs -----------------------------------------------------------------------------

    List<SavedJig> _savedJigs = [];

    void RefreshSaved()
    {
        _savedJigs = _store.All();
        _loading = true;
        _saved.ItemsSource = _savedJigs.Select(j => j.Name).ToList();
        _saved.SelectedIndex = -1;
        _loading = false;
        _delete.IsEnabled = _savedJigs.Count > 0;
    }

    void OpenSaved()
    {
        if (_loading || _saved.SelectedIndex < 0 || _saved.SelectedIndex >= _savedJigs.Count) return;
        var j = _savedJigs[_saved.SelectedIndex];
        PathGeometry outline;
        try { outline = j.ObjectOutline(); }
        catch (Exception e) when (e is FormatException or ArgumentException) { _found.Text = $"Couldn't read the saved jig \"{j.Name}\": {e.Message}"; return; }
        var o = j.ApplyTo(Options());
        _loading = true;
        _name.Text = o.Name;
        _height.Value = (decimal)o.ObjectHeightMm;
        _build.SelectedIndex = o.Build switch { JigBuild.StackedWithBase => 1, JigBuild.StackedNoBase => 2, _ => 0 };
        _sheet.Value = (decimal)o.SheetMm; _depth.Value = (decimal)o.DepthMm; _gap.Value = (decimal)o.GapMm; _round.Value = (decimal)o.RoundInsideMm;
        _notches.SelectedIndex = o.Notches switch { JigNotches.OneSide => 1, JigNotches.None => 2, _ => 0 };
        _notch.Value = (decimal)o.NotchDiameterMm; _straight.IsChecked = o.LayStraight;
        _cols.Value = o.Columns; _rows.Value = o.Rows; _wall.Value = (decimal)o.WallMm; _margin.Value = (decimal)o.MarginMm;
        _corner.Value = (decimal)o.CornerRadiusMm; _marker.Value = (decimal)o.MarkerMm; _pins.Value = (decimal)o.PinDiameterMm;
        _engraveName.IsChecked = o.EngraveName; _labelSize.Value = (decimal)o.LabelSizeMm;
        _whereItLay.IsChecked = o.Centre is null;
        if (o.Centre is { } c) { _cx.Value = (decimal)c.X; _cy.Value = (decimal)c.Y; }
        _cutPages.IsChecked = false;
        _loading = false;
        _objectShape = null;
        _object = outline;
        var b = outline.Bounds(0.1);
        _found.Text = $"Opened the saved jig \"{j.Name}\" (object {b.Width:0.#} × {b.Height:0.#} mm). Its pages to cut aren't made again unless you tick that below.";
        Replan();
    }

    async Task Save()
    {
        if (_object is null) return;
        var name = await Dialogs.Prompt(this, "Save jig", "A name for this jig (the object's outline and the jig's settings are kept; laser settings and the design aren't):",
            string.IsNullOrWhiteSpace(_name.Text) ? "Jig" : _name.Text.Trim());
        if (string.IsNullOrWhiteSpace(name)) return;
        try
        {
            _store.Save(SavedJig.From(name, _object, Options() with { Name = name.Trim() }));
            _name.Text = name.Trim();
            RefreshSaved();
            _found.Text = $"Saved \"{name.Trim()}\".";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _found.Text = $"Couldn't save the jig: {e.Message}"; }
    }

    async Task Delete()
    {
        if (_saved.SelectedIndex < 0 || _saved.SelectedIndex >= _savedJigs.Count)
        {
            _found.Text = "Pick the saved jig to delete in the list first.";
            return;
        }
        var j = _savedJigs[_saved.SelectedIndex];
        if (!await Dialogs.Confirm(this, "Delete saved jig", $"Delete the saved jig \"{j.Name}\"? Jigs already made in designs aren't touched.", "Delete")) return;
        try { _store.Delete(j.Name); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _found.Text = $"Couldn't delete it: {e.Message}"; return; }
        RefreshSaved();
    }

    // ---- small helpers --------------------------------------------------------------------------

    static TextBlock Heading(string text) => new() { Text = text, Classes = { "h" }, Margin = new(0, 6, 0, 0) };

    static Button Wide(string text, bool primary = false)
    {
        var b = new Button { Content = text, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
        if (primary) b.Classes.Add("primary");
        return b;
    }

    static ComboBox Combo(params string[] items) => new() { ItemsSource = items, HorizontalAlignment = HorizontalAlignment.Stretch };
}

/// <summary>The jig drawn to scale: wood, pockets, dowel holes, the objects in them (green) and the design on each (blue).</summary>
sealed class JigPreview : Control
{
    JigPlan? _plan;
    List<List<Vec2>> _design = [];
    IReadOnlyList<Affine> _moves = [];

    public void Show(JigPlan? plan, List<List<Vec2>> design, IReadOnlyList<Affine> moves)
    {
        _plan = plan;
        _design = design;
        _moves = moves;
        InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        var size = Bounds.Size;
        if (_plan is not { } p)
        {
            var hint = new FormattedText("The jig shows here.", System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 13, Brushes.Gray);
            ctx.DrawText(hint, new Point((size.Width - hint.Width) / 2, (size.Height - hint.Height) / 2));
            return;
        }
        var b = p.Board.Inflate(4);
        double k = Math.Min(size.Width / b.Width, size.Height / b.Height);
        double ox = (size.Width - b.Width * k) / 2 - b.MinX * k, oy = (size.Height - b.Height * k) / 2 - b.MinY * k;
        Point P(Vec2 v) => new(ox + v.X * k, oy + v.Y * k);
        Geometry G(PathGeometry g)
        {
            var sg = new StreamGeometry();
            using var c = sg.Open();
            c.SetFillRule(FillRule.EvenOdd);
            foreach (var f in g.Flatten(0.2))
            {
                if (f.Points.Count < 2) continue;
                c.BeginFigure(P(f.Points[0]), true);
                foreach (var pt in f.Points.Skip(1)) c.LineTo(P(pt));
                c.EndFigure(f.Closed);
            }
            return sg;
        }
        var wood = new SolidColorBrush(Color.FromRgb(0xD9, 0xB3, 0x82));
        var pocket = new SolidColorBrush(Color.FromRgb(0x4A, 0x36, 0x26));
        var edge = new Pen(new SolidColorBrush(Color.FromRgb(0xE6, 0x39, 0x46)), 1.2);
        var objectPen = new Pen(new SolidColorBrush(Color.FromArgb(220, 0x3C, 0xE6, 0x78)), 1.2);
        var designPen = new Pen(new SolidColorBrush(Color.FromRgb(0x5B, 0xA8, 0xFF)), 1);
        ctx.DrawGeometry(wood, edge, G(new PathGeometry([.. p.Outline.Subpaths, .. p.PinHoles.Subpaths])));
        ctx.DrawGeometry(pocket, edge, G(p.Pockets));
        ctx.DrawGeometry(null, objectPen, G(p.ObjectOutlines));
        foreach (var mv in _moves)
            foreach (var loop in _design)
            {
                if (loop.Count < 2) continue;
                var sg = new StreamGeometry();
                using (var c = sg.Open())
                {
                    c.BeginFigure(P(mv.Apply(loop[0])), false);
                    foreach (var pt in loop.Skip(1)) c.LineTo(P(mv.Apply(pt)));
                    c.EndFigure(false);
                }
                ctx.DrawGeometry(null, designPen, sg);
            }
        // Pocket numbers (the order serial numbers and spreadsheet rows count in).
        var objects = p.ObjectOutlines.Subpaths;
        for (int i = 0; i < objects.Length && objects.Length > 1; i++)
        {
            var ob = new PathGeometry([objects[i]]).Bounds(0.2);
            var at = P(new Vec2(ob.MinX, ob.Center.Y)) + new Vector(-9, 0);
            var ft = new FormattedText($"{i + 1}", System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 11, Brushes.White);
            ctx.DrawText(ft, new Point(at.X - ft.Width / 2, at.Y - ft.Height / 2));
        }
        if (p.Label is { } l)
        {
            var ft = new FormattedText(l.Text, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, Math.Max(6, l.Box.Height * k), new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)));
            var c = P(l.Centre);
            ctx.DrawText(ft, new Point(c.X - ft.Width / 2, c.Y - ft.Height / 2));
        }
    }
}
