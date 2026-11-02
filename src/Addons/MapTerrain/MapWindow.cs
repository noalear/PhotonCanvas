using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using PhotonCanvas.Addon.Buildings;
using PhotonCanvas.Addon.FloorTiles;
using PhotonCanvas.Addon.Walls;
using PhotonCanvas.App;
using PhotonCanvas.Core;
using PhotonCanvas.Core.Imaging;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Materials;
using static PhotonCanvas.App.UiKit;
using Severity = PhotonCanvas.Addon.Buildings.Severity;
using Finding = PhotonCanvas.Addon.Buildings.Finding;

namespace PhotonCanvas.Addon.MapTerrain;

/// <summary>
/// The map maker: draw a floor plan on a grid (or read one from a battle map picture, or from shapes in
/// the design), pick a look, the walls' and floor's settings, see the terrain put together in 3D and its
/// pieces flat; then every piece goes into the design, nested onto the bed, a sheet at a time.
/// </summary>
public sealed class MapWindow : Window
{
    readonly Document _doc;
    readonly MachineProfile _machine;
    readonly MaterialLibrary? _library;
    readonly string? _dataFolder;
    (Guid Id, MapSpec Spec)? _editing;
    MapSpec _spec;

    // Plan
    readonly ComboBox _theme = Combo([.. MapSpec.Themes.Select(t => t.Name)]);
    readonly TextBlock _themeAbout = Hint();
    readonly NumericUpDown _across = Number(1, MapPlan.MaxSide, 1, "0"), _down = Number(1, MapPlan.MaxSide, 1, "0"), _square = Number(10, 150, 0.5m, "0.##");
    readonly Button _clear = UiKit.Button("Clear"), _trim = UiKit.Button("Trim to the floor"), _sample = UiKit.Button("Sample dungeon");

    // Battle map
    readonly Button _open = UiKit.Button("Open a picture…"), _usePicture = UiKit.Button("Use the selected picture"), _findGrid = UiKit.Button("Find the grid"), _read = new() { Content = "Read the map", Classes = { "primary" } };
    readonly NumericUpDown _px = Number(4, 2000, 0.5m, "0.##"), _offX = Number(-2000, 2000, 1, "0.#"), _offY = Number(-2000, 2000, 1, "0.#"), _picAcross = Number(1, MapPlan.MaxSide, 1, "0");
    readonly ComboBox _floorTone = Combo("Worked out from the picture", "The lighter part", "The darker part");
    readonly CheckBox _lines = new() { Content = "Find walls drawn as lines along the grid", IsChecked = true };
    readonly CheckBox _autoSplit = new() { Content = "Find the split by itself", IsChecked = true };
    readonly NumericUpDown _split = Number(0, 1, 0.02m, "0.##");
    readonly CheckBox _showPic = new() { Content = "Show the picture under the plan", IsChecked = true };
    readonly TextBlock _picNote = Hint();
    Control _picRows = null!;
    RasterImage? _picture;
    string? _picturePath;

    // From the design
    readonly Button _useShapes = UiKit.Button("Use the selected shapes");

    // Walls
    readonly CheckBox _walls = new() { Content = "Walls (the wall maker's)", IsChecked = true };
    readonly NumericUpDown _height = Number(6, 300, 1, "0.#"), _sheet = Number(0.5m, 20, 0.5m, "0.##"), _fit = Number(-0.5m, 2, 0.05m, "0.##"), _column = Number(4, 100, 1, "0.#"), _longest = Number(1, 20, 1, "0"), _seed = Number(1, 9999, 1, "0");
    readonly CheckBox _twoSided = new() { Content = "Two panels back to back (textured both sides)" }, _caps = new() { Content = "A cap on top of each column", IsChecked = true };
    readonly ComboBox _wallTexture = Combo([.. Textures.Presets.Select(p => p.Name)]);
    readonly ComboBox _top = Combo("Straight", "Battlements", "Broken (ruined)");
    readonly ComboBox _etch = Combo("Grooves (etched areas, deeper)", "Lines (scored, faster)");
    readonly ComboBox _cut = Combo(), _etchWith = Combo(), _doorCut = Combo();
    readonly TextBlock _cutNote = Hint();
    Control _wallRows = null!;
    List<(Material M, Recipe R)?> _cuts = [], _etches = [];

    // Doors
    readonly ComboBox _door = Combo("Hung on clips: they swing open", "Loose in the doorway", "Just a doorway");
    readonly ComboBox _doorShape = Combo("Square top", "Round arch", "Pointed arch");
    readonly NumericUpDown _doorW = Number(4, 200, 0.5m, "0.#"), _doorH = Number(6, 300, 0.5m, "0.#"), _doorSheet = Number(0.3m, 20, 0.5m, "0.##"), _doorFit = Number(-0.5m, 2, 0.05m, "0.##");
    Control _doorSheetRows = null!;

    // Floor
    readonly CheckBox _floor = new() { Content = "Floor tiles (the floor tile maker's)", IsChecked = true };
    readonly ComboBox _floorTexture = Combo([.. FloorTextures.Presets.Select(p => p.Name)]);
    readonly NumericUpDown _tile = Number(1, 20, 1, "0"), _tab = Number(1, 30, 0.5m, "0.#");
    readonly ComboBox _join = Combo("Jigsaw edges", "Clips", "Plain edges");
    readonly CheckBox _under = new() { Content = "Reach under the walls round the edge", IsChecked = true };
    Control _floorRows = null!;

    // Right-hand side
    readonly TabControl _tabs = new(), _views = new();
    readonly PlanEditor _editor = new();
    readonly Dictionary<PlanTool, Button> _toolButtons = [];
    readonly Button _undo = UiKit.Button("Undo");
    readonly BuildingView _view = new();
    readonly PiecesView _pieces = new(), _doorPieces = new();
    readonly TextBlock _summary = new() { TextWrapping = TextWrapping.Wrap };
    readonly StackPanel _findings = new() { Spacing = 4 };
    readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Classes = { "muted" } };
    readonly Button _make = new() { Content = "Put in the design", Classes = { "primary" }, MinWidth = 160 };
    readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(300) };
    readonly Stack<MapPlan> _undoStack = new();
    bool _loading;
    int _generation;
    BuildingKit? _kit;
    string? _kitFrom;

    public MapWindow(Document doc, MachineProfile machine, MaterialLibrary? library, string? dataFolder, (Guid Id, MapSpec Spec)? editing)
    {
        _doc = doc;
        _machine = machine;
        _library = library;
        _dataFolder = dataFolder;
        _editing = editing;
        _spec = editing?.Spec ?? LoadLast() ?? MapSpec.Sample();
        Title = "Map to terrain";
        Width = 1240;
        Height = 820;
        MinWidth = 940;
        MinHeight = 620;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");

        Library();
        _tabs.Items.Add(Tab("Plan", PlanTab()));
        _tabs.Items.Add(Tab("Walls", WallsTab()));
        _tabs.Items.Add(Tab("Floor", FloorTab()));
        var left = new Border { Width = 440, Padding = new Thickness(14, 10, 8, 0), Child = _tabs };

        _views.Items.Add(new TabItem { Header = "Plan", Content = PlanView() });
        _views.Items.Add(new TabItem { Header = "Put together", Content = Framed(_view) });
        _views.Items.Add(new TabItem { Header = "Pieces", Content = Framed(_pieces) });
        _views.Items.Add(new TabItem { Header = "Door pieces", Content = Framed(_doorPieces) });
        var results = new StackPanel { Spacing = 8, Children = { _summary, _findings } };
        var right = new Grid { RowDefinitions = new("*,Auto"), Margin = new Thickness(0, 6, 14, 0), RowSpacing = 8 };
        right.Children.Add(_views);
        var resultScroll = new ScrollViewer { Content = results, MaxHeight = 170, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(resultScroll, 1);
        right.Children.Add(resultScroll);

        var bottom = new Grid { ColumnDefinitions = new("*,Auto,Auto"), Margin = new Thickness(14, 8, 14, 14), ColumnSpacing = 12 };
        bottom.Children.Add(_status);
        Grid.SetColumn(_make, 1);
        bottom.Children.Add(_make);
        var close = new Button { Content = "Close", IsCancel = true, MinWidth = 100 };
        close.Click += (_, _) => Close();
        Grid.SetColumn(close, 2);
        bottom.Children.Add(close);

        var body = new Grid { ColumnDefinitions = new("Auto,*"), RowDefinitions = new("*,Auto") };
        body.Children.Add(left);
        Grid.SetColumn(right, 1);
        body.Children.Add(right);
        Grid.SetRow(bottom, 1);
        Grid.SetColumnSpan(bottom, 2);
        body.Children.Add(bottom);
        Content = body;

        _editor.LayoutFor = g => _spec.Walls ? MapLayoutMaker.Make(g, _spec.LongestSquares, _spec.SquareMm, MapKitMaker.Doors(_spec)) : null;
        _editor.Edited += PlanEdited;
        _debounce.Tick += (_, _) => { _debounce.Stop(); _ = Rebuild(); };
        _make.Click += (_, _) => Make();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Z && e.KeyModifiers.HasFlag(KeyModifiers.Control)) { Undo(); e.Handled = true; }
        };
        Hook();
        Load();
        LoadPictureFromSpec();
        if (editing is not null) _make.Content = "Update in the design";
        Closed += (_, _) => { _debounce.Stop(); _generation++; SaveLast(); };
        Rebuild().ContinueWith(_ => { }, TaskScheduler.Default);
    }

    static Border Framed(Control c) => new() { CornerRadius = new(8), ClipToBounds = true, BorderBrush = Brush("Line"), BorderThickness = new(1), Margin = new(0, 6, 0, 0), Child = c };

    static TabItem Tab(string header, Control content) => new()
    {
        Header = header,
        Content = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = new StackPanel { Spacing = 10, Margin = new Thickness(0, 10, 10, 10), Children = { content } } },
    };

    static ComboBox Combo(params string[] items) => new() { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = items.ToList(), SelectedIndex = items.Length > 0 ? 0 : -1 };

    static StackPanel Stack(params Control[] c)
    {
        var s = new StackPanel { Spacing = 8 };
        foreach (var x in c) s.Children.Add(x);
        return s;
    }

    static Grid Buttons(params Control[] c)
    {
        var g = new Grid { ColumnSpacing = 6 };
        for (int i = 0; i < c.Length; i++)
        {
            // Shared out by the labels' length, so a long label isn't cut short next to a short one.
            double w = c[i] is Button { Content: string t } ? t.Length + 4 : 10;
            g.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(w, GridUnitType.Star)));
            if (c[i] is Button b) b.HorizontalAlignment = HorizontalAlignment.Stretch;
            Grid.SetColumn(c[i], i);
            g.Children.Add(c[i]);
        }
        return g;
    }

    // ---- the tabs ----

    Control PlanTab()
    {
        _picRows = Stack(
            Field("Square", _px, tip: "One grid square in the picture (pixels)."),
            Field("Grid starts at", Buttons(_offX, _offY), tip: "Where a grid line crosses near the picture's top left corner (pixels across, then down)."),
            Field("Squares across", _picAcross, tip: "For a map with no grid lines: how many squares across the picture is. The grid then starts at its corner."),
            _findGrid,
            Field("Floor is", _floorTone, tip: "Usually worked out: the edge of a map is mostly not floor. Pick the lighter or darker part if it's read the wrong way round."),
            _lines,
            _autoSplit,
            Field("Split at", _split, tip: "The brightness (0 black, 1 white) between floor and the rest, for when the split it finds isn't right."),
            _read,
            _showPic,
            _picNote);
        return Stack(
            Explained("Look", "Picks the walls' and floor's textures, the top of the walls and the doors' shape that go together. They can each be changed after on the Walls and Floor tabs.",
                Field("Look", _theme), _themeAbout),
            Explained("Plan", "Draw the floor on the grid on the right: drag out rooms, paint and rub out squares, add walls between rooms and click a wall to put a door in it. Walls go round the floor by themselves.",
                Field("Squares across", _across), Field("Squares down", _down),
                Field("Grid square", _square, tip: "One square of the grid (mm): 25.4 (an inch) for 28–32 mm figures."),
                Buttons(_clear, _trim, _sample)),
            Explained("Read a battle map", "Open a picture of a map with a square grid (or pick one in the design). The grid is found and the floor read from it; check it on the plan and fix what's wrong with the tools. Doors are marked by hand.",
                Buttons(_open, _usePicture), _picRows),
            Explained("From the design", "Draw the rooms in the design with the rectangle or any closed shape, select them and make them the plan: every square whose middle is inside is floor.",
                _useShapes));
    }

    Control PlanView()
    {
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 6, 0, 0) };
        foreach (var (tool, label, tip) in new[]
        {
            (PlanTool.Room, "Room", "Drag out a rectangle of floor (with the right button, rub one out)"),
            (PlanTool.Floor, "Floor", "Paint floor a square at a time (the right button rubs out)"),
            (PlanTool.Erase, "Rub out", "Rub out floor a square at a time"),
            (PlanTool.Wall, "Wall", "Drag along grid lines to add walls, or along walls to take them away (the right button puts an edge back as it was)"),
            (PlanTool.Door, "Door", "Click a wall to put a door in it, click the door to take it out"),
        })
        {
            var b = UiKit.Button(label, tip: tip);
            b.MinWidth = 64;
            b.Click += (_, _) => SetTool(tool);
            _toolButtons[tool] = b;
            bar.Children.Add(b);
        }
        bar.Children.Add(new Border { Width = 12 });
        bar.Children.Add(_undo);
        SetTool(PlanTool.Room);
        var g = new Grid { RowDefinitions = new("Auto,*") };
        g.Children.Add(bar);
        var framed = Framed(_editor);
        Grid.SetRow(framed, 1);
        g.Children.Add(framed);
        return g;
    }

    void SetTool(PlanTool tool)
    {
        _editor.Tool = tool;
        foreach (var (t, b) in _toolButtons)
        {
            if (t == tool) { if (!b.Classes.Contains("primary")) b.Classes.Add("primary"); }
            else b.Classes.Remove("primary");
        }
    }

    Control WallsTab()
    {
        var shuffle = UiKit.Button("Shuffle");
        shuffle.Click += (_, _) => _seed.Value = (_seed.Value ?? 1) % 9999 + 1;
        _wallRows = Stack(
            Field("Height", _height, tip: "How high the walls are (mm), rounded to whole layers of the sheet."),
            Field("Top", _top, tip: "Straight, with battlements, or broken away as a ruin."),
            Field("Texture", _wallTexture, tip: "The wall maker's textures, etched on the face that looks into the rooms."),
            Field("Etched as", _etch),
            Field("Variation", new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 6, Children = { _seed, shuffle.With(b => Grid.SetColumn(b, 1)) } },
                tip: "Stones and planks are laid out at random: each number gives a different layout."),
            Field("Longest wall", _longest, tip: "The longest panel, in squares. A longer wall is split into panels with a straight column between them."),
            _twoSided,
            Field("Columns", _column, tip: "How wide the columns the walls slide into are (mm)."),
            _caps,
            Field("Slot fit", _fit, tip: "Added to the wall's thickness for the columns' slots (mm): more is looser."));
        _doorSheetRows = Stack(
            Field("Door sheet", _doorSheet, tip: "The sheet the doors and their clips are cut from (mm)."),
            Field("Door fit", _doorFit, tip: "Added to the door sheet's thickness for its slots (mm)."),
            Field("Cut doors with", _doorCut));
        return Stack(
            Explained("Sheet", "The walls, the columns and the floor tiles are cut from this sheet: its thickness sets the columns' slots and the walls' height.",
                Field("Thickness", _sheet, tip: "The sheet's thickness (mm)."),
                Field("Cut with", _cut), _cutNote,
                Field("Etch with", _etchWith, tip: "Settings for the textures (an engrave/fill recipe from your library).")),
            Explained("Walls", "The wall maker's panels on every wall of the plan, sliding into a column at every corner, junction and end.",
                _walls, _wallRows),
            Explained("Doors", "In the middle of their square, or a little off it to keep clear of a column. A door in a wall only a square long between two columns has no room and is left as an open doorway.",
                Field("Doors", _door, tip: "Hung on clips (the furniture maker's) they swing; loose, they just sit in the doorway."),
                Field("Shape", _doorShape), Field("Width", _doorW), Field("Height", _doorH),
                _doorSheetRows));
    }

    Control FloorTab()
    {
        _floorRows = Stack(
            Field("Texture", _floorTexture),
            Field("Biggest tile", _tile, tip: "The floor is cut into tiles of up to this many squares each way."),
            Field("Joins", _join, tip: "Jigsaw tabs and sockets on every square where two tiles meet, clips that bridge them, or plain edges."),
            Field("Tab size", _tab, tip: "A jigsaw tab's round head, or a clip's width (mm)."),
            _under);
        return Stack(
            Explained("Floor tiles", "Tiles for every floor square of the plan, cut from the walls' sheet: the walls stand on them.",
                _floor, _floorRows));
    }

    // ---- the library ----

    void Library()
    {
        var mats = _library?.Materials ?? [];
        _cuts = [null, .. mats.SelectMany(m => m.Recipes.Where(r => r.Settings.Mode == LayerMode.Cut).Select(r => ((Material, Recipe)?)(m, r)))];
        _etches = [null, .. mats.SelectMany(m => m.Recipes.Where(r => r.Settings.Mode == LayerMode.Fill).Select(r => ((Material, Recipe)?)(m, r)))];
        var cutNames = _cuts.Select(c => c is { } x ? $"{x.M.Label} · {x.R.Title}" + (x.R.Settings.KerfMm > 0 ? $" (kerf {x.R.Settings.KerfMm:0.###} mm)" : "") : "As set on the cut layer").ToList();
        _cut.ItemsSource = cutNames;
        _doorCut.ItemsSource = cutNames.ToList();
        _etchWith.ItemsSource = _etches.Select(c => c is { } x ? $"{x.M.Label} · {x.R.Title}" : "As set on the etch layer").ToList();
        _cut.SelectedIndex = _doorCut.SelectedIndex = _etchWith.SelectedIndex = 0;
    }

    (Material M, Recipe R)? Recipe(ComboBox box) => box.SelectedIndex > 0 && box.SelectedIndex < _cuts.Count ? _cuts[box.SelectedIndex] : null;
    (Material M, Recipe R)? EtchRecipe => _etchWith.SelectedIndex > 0 && _etchWith.SelectedIndex < _etches.Count ? _etches[_etchWith.SelectedIndex] : null;

    void CutChosen(ComboBox box, NumericUpDown thick, NumericUpDown fit, TextBlock? note)
    {
        if (!_loading && Recipe(box) is { } c)
        {
            _loading = true;
            if (c.M.ThicknessMm is { } t) thick.Value = (decimal)Math.Clamp(t, (double)thick.Minimum, (double)thick.Maximum);
            if (c.R.SlotFitMm is { } f) fit.Value = (decimal)Math.Clamp(f, -0.5, 2);
            int same = _etches.FindIndex(e => e is { } x && x.M.Id == c.M.Id);
            if (same > 0 && EtchRecipe is null) _etchWith.SelectedIndex = same;
            _loading = false;
        }
        if (note is not null)
            note.Text = Recipe(box) is not { } r
                ? "No kerf allowance: slots come out a little loose. Pick your material's cut settings (Laser → Tests → Cut test measures the kerf) for slots at their drawn size."
                : r.R.Settings.KerfMm > 0 ? $"{r.M.Label}: kerf {r.R.Settings.KerfMm:0.###} mm, allowed for on the cut layer so slots come out their drawn width."
                : $"{r.M.Label}: no kerf measured yet, so slots come out a little loose (run a cut test).";
        FromControls();
    }

    // ---- settings in and out ----

    void Hook()
    {
        foreach (var n in new[] { _square, _height, _sheet, _fit, _column, _longest, _seed, _doorW, _doorH, _doorSheet, _doorFit, _tile, _tab })
            n.ValueChanged += (_, _) => FromControls();
        foreach (var c in new[] { _wallTexture, _top, _etch, _door, _doorShape, _floorTexture, _join, _etchWith })
            c.SelectionChanged += (_, _) => FromControls();
        foreach (var c in new[] { _walls, _twoSided, _caps, _floor, _under })
            c.IsCheckedChanged += (_, _) => FromControls();
        _cut.SelectionChanged += (_, _) => CutChosen(_cut, _sheet, _fit, _cutNote);
        _doorCut.SelectionChanged += (_, _) => CutChosen(_doorCut, _doorSheet, _doorFit, null);
        _theme.SelectionChanged += (_, _) => ThemeChosen();
        _across.ValueChanged += (_, _) => PlanSizeChanged();
        _down.ValueChanged += (_, _) => PlanSizeChanged();
        _clear.Click += (_, _) => NewPlan(new PlanGrid(_spec.Plan.Across, _spec.Plan.Down), "Clear the plan");
        _trim.Click += (_, _) => NewPlan(_spec.Plan.Grid().Trimmed(1), "Trim the plan");
        _sample.Click += (_, _) => NewPlan(MapSpec.Sample().Plan.Grid(), "Sample dungeon");
        _undo.Click += (_, _) => Undo();
        _open.Click += (_, _) => _ = OpenPicture();
        _usePicture.Click += (_, _) => UseSelectedPicture();
        _findGrid.Click += (_, _) => FindGrid();
        _read.Click += (_, _) => ReadMap();
        _picAcross.ValueChanged += (_, _) => PictureAcross();
        foreach (var n in new[] { _px, _offX, _offY }) n.ValueChanged += (_, _) => PictureGridChanged();
        _autoSplit.IsCheckedChanged += (_, _) => _split.IsEnabled = _autoSplit.IsChecked != true;
        _showPic.IsCheckedChanged += (_, _) => _editor.ShowPicture = _showPic.IsChecked == true;
        _useShapes.Click += (_, _) => UseSelectedShapes();
    }

    static decimal D(double v) => (decimal)v;
    static double V(NumericUpDown n, double fallback) => n.Value is { } v ? (double)v : fallback;
    static int Index(ComboBox c) => Math.Max(0, c.SelectedIndex);

    void Load()
    {
        var s = _spec;
        _loading = true;
        int ti = MapSpec.Themes.ToList().FindIndex(t => t.Name == s.Theme);
        _theme.SelectedIndex = Math.Max(0, ti);
        _themeAbout.Text = MapSpec.Themes[Math.Max(0, ti)].About;
        _across.Value = s.Plan.Across; _down.Value = s.Plan.Down; _square.Value = D(s.SquareMm);
        _walls.IsChecked = s.Walls; _wallRows.IsVisible = s.Walls;
        _height.Value = D(s.WallHeightMm); _sheet.Value = D(s.SheetMm); _fit.Value = D(s.FitMm); _column.Value = D(s.ColumnMm);
        _longest.Value = Math.Clamp(s.LongestSquares, 1, 20); _seed.Value = Math.Clamp(s.Seed, 1, 9999);
        _twoSided.IsChecked = s.TwoSided; _caps.IsChecked = s.Caps;
        _wallTexture.SelectedIndex = Math.Max(0, Textures.Presets.ToList().FindIndex(p => p.Name == s.WallTexture));
        _top.SelectedIndex = (int)s.Top; _etch.SelectedIndex = (int)s.Etch;
        _door.SelectedIndex = (int)s.Door;
        _doorShape.SelectedIndex = s.DoorShape switch { OpeningShape.Rectangle => 0, OpeningShape.Pointed => 2, _ => 1 };
        _doorW.Value = D(s.DoorWidthMm); _doorH.Value = D(s.DoorHeightMm); _doorSheet.Value = D(s.DoorSheetMm); _doorFit.Value = D(s.DoorFitMm);
        _doorSheetRows.IsVisible = s.Door == DoorStyle.Swinging;
        _floor.IsChecked = s.Floor; _floorRows.IsVisible = s.Floor;
        _floorTexture.SelectedIndex = Math.Max(0, FloorTextures.Presets.ToList().FindIndex(p => p.Name == s.FloorTexture));
        _tile.Value = Math.Clamp(s.TileSquares, 1, 20); _join.SelectedIndex = (int)s.Join; _tab.Value = D(s.TabMm); _under.IsChecked = s.UnderWalls;
        if (s.Picture is { } pic) { _px.Value = D(pic.SquarePx); _offX.Value = D(pic.OffsetX); _offY.Value = D(pic.OffsetY); }
        _split.IsEnabled = _autoSplit.IsChecked != true;
        _loading = false;
        PictureShows();
        _editor.Show(s.Plan.Grid());
    }

    void FromControls()
    {
        if (_loading) return;
        _spec = _spec with
        {
            SquareMm = V(_square, 25.4),
            Walls = _walls.IsChecked == true, WallHeightMm = V(_height, 40), SheetMm = V(_sheet, 3), FitMm = V(_fit, 0.15), ColumnMm = V(_column, 12),
            LongestSquares = (int)V(_longest, 4), Seed = (int)V(_seed, 1), TwoSided = _twoSided.IsChecked == true, Caps = _caps.IsChecked == true,
            WallTexture = Textures.Presets[Math.Clamp(Index(_wallTexture), 0, Textures.Presets.Count - 1)].Name,
            Top = (TopStyle)Index(_top), Etch = (EtchStyle)Index(_etch),
            Door = (DoorStyle)Index(_door),
            DoorShape = Index(_doorShape) switch { 0 => OpeningShape.Rectangle, 2 => OpeningShape.Pointed, _ => OpeningShape.Arched },
            DoorWidthMm = V(_doorW, 16), DoorHeightMm = V(_doorH, 28), DoorSheetMm = V(_doorSheet, 2), DoorFitMm = V(_doorFit, 0.1),
            Floor = _floor.IsChecked == true,
            FloorTexture = FloorTextures.Presets[Math.Clamp(Index(_floorTexture), 0, FloorTextures.Presets.Count - 1)].Name,
            TileSquares = (int)V(_tile, 2), Join = (MapJoin)Index(_join), TabMm = V(_tab, 5), UnderWalls = _under.IsChecked == true,
        };
        _wallRows.IsVisible = _spec.Walls;
        _floorRows.IsVisible = _spec.Floor;
        _doorSheetRows.IsVisible = _spec.Door == DoorStyle.Swinging;
        _editor.Relayout();
        Changed();
    }

    void ThemeChosen()
    {
        if (_loading) return;
        _spec = _spec.WithTheme(MapSpec.Themes[Math.Clamp(Index(_theme), 0, MapSpec.Themes.Count - 1)].Name);
        Load();
        Changed();
    }

    // ---- the plan ----

    void PlanEdited(PlanGrid g, string what)
    {
        _undoStack.Push(_spec.Plan);
        _spec = _spec with { Plan = g.ToPlan() };
        _status.Text = "";
        Changed();
    }

    /// <summary>A whole new plan (cleared, trimmed, read from a picture or shapes), which can be undone.</summary>
    void NewPlan(PlanGrid g, string what)
    {
        _undoStack.Push(_spec.Plan);
        _spec = _spec with { Plan = g.ToPlan() };
        _loading = true;
        _across.Value = g.Across;
        _down.Value = g.Down;
        _loading = false;
        _editor.Show(g);
        _status.Text = $"{what}: Undo (Ctrl+Z) puts the plan back.";
        Changed();
    }

    void Undo()
    {
        if (_undoStack.Count == 0) return;
        var p = _undoStack.Pop();
        _spec = _spec with { Plan = p };
        _loading = true;
        _across.Value = p.Across;
        _down.Value = p.Down;
        _loading = false;
        _editor.Show(p.Grid());
        Changed();
    }

    void PlanSizeChanged()
    {
        if (_loading) return;
        int a = (int)V(_across, 10), d = (int)V(_down, 8);
        if (a == _spec.Plan.Across && d == _spec.Plan.Down) return;
        _undoStack.Push(_spec.Plan);
        var g = _spec.Plan.Grid().Resized(a, d);
        _spec = _spec with { Plan = g.ToPlan() };
        _editor.Show(g);
        Changed();
    }

    // ---- battle maps ----

    void PictureShows()
    {
        bool has = _picture is not null;
        _picRows.IsVisible = has;
        _findGrid.IsEnabled = _read.IsEnabled = has;
        _usePicture.IsEnabled = SelectedPicture() is not null;
        _useShapes.IsEnabled = SelectedOutlines().Count > 0;
    }

    ImageShape? SelectedPicture()
    {
        var sel = _doc.Selection;
        return _doc.State.Shapes.OfType<ImageShape>().FirstOrDefault(s => sel.Contains(s.Id));
    }

    List<IReadOnlyList<(double X, double Y)>> SelectedOutlines()
    {
        var sel = _doc.Selection;
        return [.. _doc.State.Shapes.OfType<PathShape>().Where(s => sel.Contains(s.Id))
            .SelectMany(s => s.Flat.Where(f => f.Closed && f.Points.Count >= 3).Select(f => (IReadOnlyList<(double X, double Y)>)f.Points.Select(q => (q.X, q.Y)).ToList()))];
    }

    async Task OpenPicture()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Battle map picture",
            FileTypeFilter =
            [
                new FilePickerFileType("Pictures") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.webp", "*.bmp", "*.gif"] },
                new FilePickerFileType("All files") { Patterns = ["*"] },
            ],
        });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path) return;
        _status.Text = $"Reading {Path.GetFileName(path)}…";
        try
        {
            var img = await Task.Run(() => RasterImage.Decode(File.ReadAllBytes(path)));
            await UsePicture(img, path);
        }
        catch (Exception e) when (e is FormatException or IOException or UnauthorizedAccessException or OutOfMemoryException)
        {
            _status.Text = $"Couldn't read it: {e.Message}";
        }
    }

    void UseSelectedPicture()
    {
        if (SelectedPicture() is not { } pic) return;
        _ = UsePicture(pic.Source.Pixels, null);
    }

    /// <summary>Shows a battle map under the plan, finds its grid and reads it (also how the tests and the help pictures load one).</summary>
    public async Task UsePicture(RasterImage img, string? path)
    {
        _picture = img;
        _picturePath = path;
        _status.Text = "Finding the grid…";
        var grid = await Task.Run(() => MapReader.FindGrid(img));
        _picNote.Text = grid is null
            ? "No grid lines found: type how many squares across the picture is."
            : $"Found a grid of {grid.SquarePx:0.#} pixel squares: {img.Width / grid.SquarePx:0.#} × {img.Height / grid.SquarePx:0.#} squares.";
        grid ??= MapReader.GridFor(img, 20);
        SetPictureGrid(grid);
        _editor.SetPicture(PictureView.ToBitmap(img, 1600), img.Width, img.Height, grid);
        PictureShows();
        ReadMap();
    }

    void LoadPictureFromSpec()
    {
        if (_spec.Picture is not { Path: { } path } pic || !File.Exists(path)) return;
        try
        {
            var img = RasterImage.Decode(File.ReadAllBytes(path));
            _picture = img;
            _picturePath = path;
            _editor.SetPicture(PictureView.ToBitmap(img, 1600), img.Width, img.Height, pic);
            SetPictureGrid(pic);
            PictureShows();
        }
        catch (Exception e) when (e is FormatException or IOException or UnauthorizedAccessException or OutOfMemoryException) { }
    }

    void SetPictureGrid(MapPicture g)
    {
        _loading = true;
        _px.Value = D(Math.Clamp(g.SquarePx, 4, 2000));
        _offX.Value = D(g.OffsetX);
        _offY.Value = D(g.OffsetY);
        if (_picture is { } img) _picAcross.Value = Math.Clamp((int)Math.Round(img.Width / g.SquarePx), 1, MapPlan.MaxSide);
        _loading = false;
    }

    MapPicture PictureGrid() => new() { Path = _picturePath, SquarePx = V(_px, 70), OffsetX = V(_offX, 0), OffsetY = V(_offY, 0) };

    void PictureGridChanged()
    {
        if (_loading || _picture is not { } img) return;
        _editor.SetPicture(PictureView.ToBitmap(img, 1600), img.Width, img.Height, PictureGrid());
    }

    void PictureAcross()
    {
        if (_loading || _picture is not { } img) return;
        SetPictureGrid(MapReader.GridFor(img, (int)V(_picAcross, 20)));
        PictureGridChanged();
    }

    void FindGrid()
    {
        if (_picture is not { } img) return;
        var g = MapReader.FindGrid(img);
        if (g is null) { _picNote.Text = "No grid lines found: type how many squares across the picture is."; return; }
        SetPictureGrid(g);
        PictureGridChanged();
        _picNote.Text = $"Found a grid of {g.SquarePx:0.#} pixel squares.";
    }

    void ReadMap()
    {
        if (_picture is not { } img) return;
        var grid = PictureGrid();
        var o = new ReadOptions
        {
            Floor = (FloorTone)Index(_floorTone),
            Threshold = _autoSplit.IsChecked == true ? double.NaN : V(_split, 0.5),
            WallLines = _lines.IsChecked == true,
        };
        var r = MapReader.Read(img, grid, o);
        _loading = true;
        _split.Value = D(Math.Clamp(r.Threshold, 0, 1));
        _loading = false;
        _spec = _spec with { Picture = grid };
        _editor.SetPicture(PictureView.ToBitmap(img, 1600), img.Width, img.Height, grid);
        NewPlan(r.Plan, "Read the map");
        _picNote.Text = string.Join(" ", r.Notes);
        _status.Text = $"Read {r.Plan.FloorCount} floor squares from the map: check them on the plan. Undo (Ctrl+Z) puts the plan back.";
    }

    void UseSelectedShapes()
    {
        var loops = SelectedOutlines();
        if (loops.Count == 0) { _status.Text = "Select closed shapes in the design first."; return; }
        var g = MapReader.FromOutlines(loops, _spec.SquareMm);
        _spec = _spec with { Picture = null };
        _picture = null;
        _picturePath = null;
        _editor.SetPicture(null, 0, 0, null);
        PictureShows();
        NewPlan(g, "Plan from the selected shapes");
    }

    // ---- remembering the last one ----

    string? LastFile => _dataFolder is null ? null : Path.Combine(_dataFolder, "last.json");

    MapSpec? LoadLast()
    {
        try { return LastFile is { } f && File.Exists(f) ? MapSpec.FromJson(File.ReadAllText(f)) : null; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    void SaveLast()
    {
        try
        {
            if (LastFile is not { } f) return;
            Directory.CreateDirectory(Path.GetDirectoryName(f)!);
            File.WriteAllText(f, _spec.ToJson());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    // ---- preview ----

    void Changed()
    {
        if (_loading) return;
        _debounce.Stop();
        _debounce.Start();
    }

    async Task Rebuild()
    {
        int gen = ++_generation;
        var spec = _spec;
        BuildingKit kit;
        _status.Text = _status.Text is { Length: > 0 } t && !t.StartsWith("Making", StringComparison.Ordinal) ? t : "Making the pieces…";
        try { kit = await Task.Run(() => MapKitMaker.Make(spec)); }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or ArithmeticException)
        {
            if (gen == _generation) { _view.Show(null, e.Message); _pieces.Show(null, e.Message); _doorPieces.Show(null, e.Message); _status.Text = ""; }
            return;
        }
        if (gen != _generation) return;
        if (_status.Text == "Making the pieces…") _status.Text = "";
        _kit = kit;
        _kitFrom = spec.ToJson();
        var all = kit.Findings.Concat(MapPlacement.TooBig(kit, _machine)).ToList();
        string? bad = all.FirstOrDefault(f => f.Level == Severity.Bad)?.Text;
        _view.Show(kit.Solids.Count > 0 ? kit : null, bad ?? "Nothing to show.");
        _pieces.Show([.. kit.Pieces.Where(p => p.Sheet == Sheet.Walls)], bad ?? "No pieces.");
        _doorPieces.Show([.. kit.Pieces.Where(p => p.Sheet == Sheet.Furniture)], bad ?? "No doors that swing: pick them under Doors on the Walls tab.");
        _findings.Children.Clear();
        foreach (var f in all.OrderByDescending(f => f.Level)) _findings.Children.Add(FindingRow(f));
        int main = kit.Count(Sheet.Walls), doors = kit.Count(Sheet.Furniture);
        _summary.Text = kit.PieceCount == 0 ? "" :
            $"{main} piece{(main == 1 ? "" : "s")} from the {spec.SheetMm:0.##} mm sheet" +
            (doors > 0 ? $" and {doors} for the doors from the {spec.DoorSheetMm:0.##} mm sheet" : "") + ".";
        _make.IsEnabled = kit.Ok && kit.PieceCount > 0 && bad is null;
        PictureShows();
    }

    static Control FindingRow(Finding f)
    {
        var (mark, brush) = f.Level switch
        {
            Severity.Bad => ("✕", Brush("Danger")),
            Severity.Warn => ("!", Brush("Warn")),
            _ => ("•", Brush("TextMuted")),
        };
        var g = new Grid { ColumnDefinitions = new("20,*") };
        g.Children.Add(new TextBlock { Text = mark, Foreground = brush, FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center });
        var t = new TextBlock { Text = f.Text, TextWrapping = TextWrapping.Wrap };
        Grid.SetColumn(t, 1);
        g.Children.Add(t);
        return g;
    }

    // ---- putting it in ----

    bool _making;

    static Func<Layer, Layer>? Apply((Material M, Recipe R)? r) =>
        r is { } c ? l => c.R.Settings.ApplyTo(l) with { Material = new MaterialRef(c.M.Id, c.M.Label, c.R.Id, c.R.Title) } : null;

    async void Make()
    {
        if (_making) return;
        _making = true;
        _make.IsEnabled = false;
        _status.Text = "Putting the pieces in the design…";
        try
        {
            var spec = _spec;
            var built = _kit is not null && _kitFrom == spec.ToJson() ? _kit : null;
            var options = new MapPlaceOptions
            {
                WallCut = Apply(Recipe(_cut)), FurnitureCut = Apply(Recipe(_doorCut)), EtchSettings = Apply(EtchRecipe),
                Replace = _editing is { } ed && MapPlacement.CountEverywhere(_doc.State, ed.Id) > 0 ? ed.Id : null,
            };
            var before = _doc.State;
            var (kit, s, pages, id) = await Task.Run(() =>
            {
                var kit = built ?? MapKitMaker.Make(spec);
                if (!kit.Ok) throw new InvalidOperationException(kit.Findings.First(f => f.Level == Severity.Bad).Text);
                var (s, pages, id) = MapPlacement.Place(before, kit, spec, _machine, options);
                return (kit, s, pages, id);
            });
            if (!ReferenceEquals(_doc.State, before)) { _status.Text = "The design changed meanwhile: press again to put the pieces in."; return; }
            _doc.Commit(options.Replace is null ? "Map terrain" : "Update the map terrain", s, MapPlacement.ShapesOf(s, id));
            _editing = (id, spec);
            _make.Content = "Update in the design";
            _status.Text = $"{(options.Replace is null ? "Put" : "Updated")} {kit.PieceCount} pieces in the design over {pages} page{(pages == 1 ? "" : "s")} (a bed each)" +
                           $": outlines on \"{MapPlacement.CutLayer}\", the textures on \"{MapPlacement.EtchLayer}\" and \"{MapPlacement.LineLayer}\" (done first).";
        }
        catch (InvalidOperationException ex) { _status.Text = ex.Message; }
        catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException or ArithmeticException or NullReferenceException)
        {
            AppLog.Error("mapterrain", ex);
            _status.Text = $"Couldn't put the terrain in: {ex.Message}";
        }
        finally
        {
            _making = false;
            _make.IsEnabled = _kit?.Ok ?? true;
        }
    }
}
