using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using PhotonCanvas.Addon.Buildings;
using PhotonCanvas.Addon.Furniture;
using PhotonCanvas.Addon.Walls;
using PhotonCanvas.App;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Materials;
using static PhotonCanvas.App.UiKit;
using Finding = PhotonCanvas.Addon.Buildings.Finding;
using Severity = PhotonCanvas.Addon.Buildings.Severity;
using PiecesView = PhotonCanvas.Addon.Buildings.PiecesView;
using PlaceOptions = PhotonCanvas.Addon.Buildings.PlaceOptions;
using HingeSide = PhotonCanvas.Addon.Walls.HingeSide;

namespace PhotonCanvas.Addon.HouseEditor;

/// <summary>
/// The building editor: the floor plan on the left, where walls, rooms, doors, windows and floor are drawn
/// floor by floor; the building put together in 3D on the right (the near walls cut away to see in); the
/// look of the walls and what's picked on the plan down the side. Every piece then goes into the design,
/// nested onto the bed.
/// </summary>
public sealed class HouseEditorWindow : Window
{
    readonly Document _doc;
    readonly MachineProfile _machine;
    readonly MaterialLibrary? _library;
    readonly string? _dataFolder;
    (Guid Id, HouseDesign Design)? _editing;
    HouseDesign _design;
    readonly List<HouseDesign> _undo = [], _redo = [];

    readonly PlanEditor _plan = new();
    readonly HouseView _view = new();
    readonly PiecesView _pieces = new();
    readonly Dictionary<PlanTool, ToggleButton> _tools = [];
    readonly ComboBox _level = Combo();
    readonly Button _addLevel = UiKit.Button("Add a floor above", tip: "A new, empty floor on top. Draw its walls over the faint plan of the floor below; its rooms get a floor under them."),
        _copyUp = UiKit.Button("Copy walls up", tip: "Draws the floor below's walls on this floor too (doors and windows aren't copied)."),
        _removeLevel = UiKit.Button("Remove this floor"),
        _undoButton = UiKit.Button("Undo"), _redoButton = UiKit.Button("Redo");
    readonly ComboBox _walls = Combo("Everything, roofs on", "Walls cut away", "Walls down"), _upTo = Combo("Every floor");

    // What's picked on the plan.
    readonly StackPanel _picked = new() { Spacing = 8 };
    readonly TextBlock _pickedNone = Hint("Pick Select and click a door or window on the plan to change it (or drop one in a wall with Door, Doorway or Window), or click inside a room. Pick Roof and click a roof to change it.");
    readonly ComboBox _oKind = Combo("Door that opens", "Doorway (left open)", "Window");
    readonly ComboBox _oShape = Combo("Square", "Round arch", "Pointed arch", "Round", "Arrow slit", "Angled corners (sci-fi)", "Rough (cave)");
    readonly NumericUpDown _oW = Number(2, 300, 0.5m, "0.#"), _oH = Number(2, 300, 0.5m, "0.#"), _oSill = Number(1, 300, 0.5m, "0.#");
    readonly ComboBox _oLook = Combo("Planks", "Planks with iron studs", "Metal blast door", "Bars (a portcullis)", "Stone slab"), _oHinge = Combo("Left (seen from outside)", "Right (seen from outside)");
    readonly ComboBox _oBars = Combo("None", "A cross", "Upright bars"), _oFill = Combo("Nothing (bars left in the wall)", "A frame with its bars", "A shutter, glued in", "Shutters that open");
    readonly Button _oAll = UiKit.Button("Make every one on this floor like it"), _oRemove = UiKit.Button("Take it away");
    Control _oSillRow = null!, _oLookRow = null!, _oHingeRow = null!, _oBarsRow = null!, _oFillRow = null!;
    // A roof picked.
    readonly StackPanel _roofPanel = new() { Spacing = 8 };
    readonly TextBlock _rInfo = Hint();
    readonly ComboBox _rKind = Combo("Gable (two slopes)", "Hipped (four slopes)", "Lean-to (one slope)", "Flat");
    readonly ComboBox _rTurn = Combo(), _rCover = Combo(), _rWindow = Combo("None", "Round", "Arched", "Square", "Pointed", "Angled (sci-fi)");
    readonly NumericUpDown _rPitch = Number(10, 75, 1, "0"), _rEave = Number(0, 40, 0.5m, "0.#");
    readonly Button _rRemove = UiKit.Button("Take the roof away");
    Control _rTurnRow = null!, _rPitchRow = null!, _rEaveRow = null!, _rWindowRow = null!;
    // A room picked.
    readonly StackPanel _roomPanel = new() { Spacing = 8 };
    readonly TextBlock _roomInfo = Hint();
    readonly ComboBox _roomFloor = Combo(["The building's floor", .. PhotonCanvas.Addon.FloorTiles.FloorTextures.Presets.Select(p => p.Name)]);
    // This floor.
    readonly CheckBox _railings = new() { Content = "Railings round its open edges" };
    readonly Button _roofIt = UiKit.Button("Roof it", tip: "Puts a roof over the top floor's rooms, in place of any roofs there (gable, the ridge along the longer side). Pick one on the plan with Roof to change it.");

    // The look.
    readonly ComboBox _theme = Combo([.. BuildingThemes.Names]);
    readonly ComboBox _scale = Combo("15 mm", "28 mm", "32 mm", "54 mm");
    static readonly double[] Scales = [15, 28, 32, 54];
    readonly ComboBox _build = Combo("Boxes: a front, back, top and bottom", "Flat: one sheet thick");
    readonly NumericUpDown _wallMm = Number(6, 60, 1, "0.#"), _sheet = Number(0.5m, 20, 0.5m, "0.##"), _column = Number(5, 200, 1, "0.#"), _storeyH = Number(10, 500, 1, "0.#"),
        _square = Number(10, 100, 0.5m, "0.##"), _fit = Number(-0.5m, 2, 0.05m, "0.##"), _seed = Number(1, 9999, 1, "0");
    readonly ComboBox _texture = Combo(), _upper = Combo(), _etch = Combo("Grooves (etched areas, deeper)", "Lines (scored, faster)");
    readonly ComboBox _detail = Combo("Plain", "Wood grain", "Planks and grain", "Metal deck plates (sci-fi)", "Stone");
    readonly CheckBox _tiles = new() { Content = "Floor tiles under the building" };
    readonly ComboBox _tileTexture = Combo([.. PhotonCanvas.Addon.FloorTiles.FloorTextures.Presets.Select(p => p.Name)]);
    readonly List<string> _textures = [.. Textures.Presets.Select(p => p.Name)];
    readonly ComboBox _cut = Combo(), _etchWith = Combo();
    readonly TextBlock _cutNote = Hint();
    Control _wallMmRow = null!, _tileRow = null!;
    List<(Material M, Recipe R)?> _cuts = [], _etches = [];

    readonly TextBlock _summary = new() { TextWrapping = TextWrapping.Wrap };
    readonly StackPanel _findings = new() { Spacing = 4 };
    readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Classes = { "muted" } };
    readonly Button _make = new() { Content = "Put in the design", Classes = { "primary" }, MinWidth = 160 };
    readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(300) };
    bool _loading;
    int _generation;
    HouseKit? _kit;
    string? _kitFrom;

    public HouseEditorWindow(Document doc, MachineProfile machine, MaterialLibrary? library, string? dataFolder, (Guid Id, HouseDesign Design)? editing)
    {
        _doc = doc;
        _machine = machine;
        _library = library;
        _dataFolder = dataFolder;
        _editing = editing;
        _design = editing?.Design ?? LoadLast() ?? HouseDesign.Starter();
        if (_design.Levels.Count == 0) _design = _design with { Levels = [new()] };
        Title = "Building editor";
        Width = 1320;
        Height = 860;
        MinWidth = 1000;
        MinHeight = 640;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");

        Library();
        _texture.ItemsSource = _textures.ToList();
        _upper.ItemsSource = new[] { "The same as the ground floor" }.Concat(_textures).ToList();

        // The tools along the top.
        var bar = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(14, 10, 14, 4) };
        void Tool(PlanTool t, string text, string tip)
        {
            var b = new ToggleButton { Content = text, Margin = new Thickness(0, 0, 4, 4), MinWidth = 64 };
            ToolTip.SetTip(b, tip);
            b.Click += (_, _) => PickTool(t);
            _tools[t] = b;
            bar.Children.Add(b);
        }
        Tool(PlanTool.Select, "Select", "Click a door or window to change it (on the right) or drag it along its wall; Delete takes it away. Click inside a room to give it a floor of its own.");
        Tool(PlanTool.Room, "Room", "Drag out a rectangle: four walls round it at once.");
        Tool(PlanTool.Wall, "Wall", "Drag from one grid point to another: straight along, across or corner to corner.");
        Tool(PlanTool.Erase, "Erase", "Drag along walls to take them away, or click one square of wall, or a door or window.");
        bar.Children.Add(Gap());
        Tool(PlanTool.Door, "Door", "Click a wall to put a door in it (it hangs on pins and swings open in box walls). Drag it along the wall.");
        Tool(PlanTool.Doorway, "Doorway", "Click a wall for an open doorway, with nothing in it: between rooms, say.");
        Tool(PlanTool.Window, "Window", "Click a wall to put a window in it. Drag it along the wall.");
        bar.Children.Add(Gap());
        Tool(PlanTool.Floor, "Floor", "Drag over squares to lay floor outside the rooms (a balcony or a landing); drag from a laid square to take it up. Rooms get a floor anyway.");
        Tool(PlanTool.Roof, "Roof", "Drag out a rectangle over rooms for a roof that rests on their walls; click a roof to change its shape, pitch and covering (on the right).");
        _roofIt.Margin = new Thickness(0, 0, 4, 4);
        bar.Children.Add(_roofIt);
        bar.Children.Add(Gap());
        bar.Children.Add(new TextBlock { Text = "Floor", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 4), Classes = { "muted" } });
        _level.MinWidth = 110;
        _level.Margin = new Thickness(0, 0, 4, 4);
        bar.Children.Add(_level);
        foreach (var b in new[] { _addLevel, _copyUp, _removeLevel }) { b.Margin = new Thickness(0, 0, 4, 4); bar.Children.Add(b); }
        bar.Children.Add(Gap());
        foreach (var b in new[] { _undoButton, _redoButton }) { b.Margin = new Thickness(0, 0, 4, 4); bar.Children.Add(b); }
        ToolTip.SetTip(_undoButton, "Ctrl+Z");
        ToolTip.SetTip(_redoButton, "Ctrl+Y");

        // The plan and the building side by side.
        var showRow = new Grid { ColumnDefinitions = new("Auto,170,Auto,150"), ColumnSpacing = 8 };
        showRow.Children.Add(new TextBlock { Text = "Show", VerticalAlignment = VerticalAlignment.Center, Classes = { "muted" } });
        showRow.Children.Add(_walls.With(c => Grid.SetColumn(c, 1)));
        showRow.Children.Add(new TextBlock { Text = "up to", VerticalAlignment = VerticalAlignment.Center, Classes = { "muted" } }.With(c => Grid.SetColumn(c, 2)));
        showRow.Children.Add(_upTo.With(c => Grid.SetColumn(c, 3)));
        var views = new TabControl();
        var assembled = new Grid { RowDefinitions = new("Auto,*"), Margin = new Thickness(0, 6, 0, 0) };
        assembled.Children.Add(showRow);
        assembled.Children.Add(Framed(_view).With(c => Grid.SetRow(c, 1)));
        views.Items.Add(new TabItem { Header = "Put together", Content = assembled });
        views.Items.Add(new TabItem { Header = "Pieces", Content = Framed(_pieces) });
        var middle = new Grid { ColumnDefinitions = new("*,8,*"), Margin = new Thickness(14, 0, 8, 0) };
        middle.Children.Add(new Grid { RowDefinitions = new("Auto,*"), Children = { new TextBlock { Text = "Plan (from above)", Classes = { "muted" }, Margin = new Thickness(2, 10, 0, 0) }, Framed(_plan).With(c => Grid.SetRow(c, 1)) } });
        middle.Children.Add(views.With(c => Grid.SetColumn(c, 2)));

        // Down the side: what's picked, the look.
        var side = new TabControl();
        side.Items.Add(Tab("Picked", PickedTab()));
        side.Items.Add(Tab("Look", LookTab()));
        var results = new StackPanel { Spacing = 8, Children = { _summary, _findings } };
        var resultScroll = new ScrollViewer { Content = results, Height = 170, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var right = new Grid { Width = 360, RowDefinitions = new("*,Auto"), Margin = new Thickness(0, 4, 14, 0), RowSpacing = 8 };
        right.Children.Add(side);
        right.Children.Add(resultScroll.With(c => Grid.SetRow(c, 1)));

        var bottom = new Grid { ColumnDefinitions = new("*,Auto,Auto"), Margin = new Thickness(14, 8, 14, 14), ColumnSpacing = 12 };
        bottom.Children.Add(_status);
        bottom.Children.Add(_make.With(c => Grid.SetColumn(c, 1)));
        var close = new Button { Content = "Close", IsCancel = true, MinWidth = 100 };
        close.Click += (_, _) => Close();
        bottom.Children.Add(close.With(c => Grid.SetColumn(c, 2)));

        var body = new Grid { ColumnDefinitions = new("*,Auto"), RowDefinitions = new("Auto,*,Auto") };
        body.Children.Add(bar.With(c => Grid.SetColumnSpan(c, 2)));
        body.Children.Add(middle.With(c => Grid.SetRow(c, 1)));
        body.Children.Add(right.With(c => { Grid.SetRow(c, 1); Grid.SetColumn(c, 1); }));
        body.Children.Add(bottom.With(c => { Grid.SetRow(c, 2); Grid.SetColumnSpan(c, 2); }));
        Content = body;

        _debounce.Tick += (_, _) => { _debounce.Stop(); _ = Rebuild(); };
        _make.Click += (_, _) => Make();
        Hook();
        LoadLook();
        PickTool(PlanTool.Room);
        ShowLevel(0);
        if (editing is not null) _make.Content = "Update in the design";
        Closed += (_, _) => { _debounce.Stop(); _generation++; SaveLast(); };
        KeyDown += OnKey;
        Rebuild().ContinueWith(_ => { }, TaskScheduler.Default);
    }

    static Control Gap() => new Border { Width = 14 };

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

    Control PickedTab()
    {
        _oSillRow = Field("Sill", _oSill, tip: "How high the window's bottom is above its floor (mm).");
        _oLookRow = Field("Looks like", _oLook);
        _oHingeRow = Field("Hinged on the", _oHinge);
        _oBarsRow = Field("Bars", _oBars);
        _oFillRow = Field("In the window", _oFill, tip: "What fills a box wall's window: a frame with its bars, shutters, or nothing.");
        var buttons = new StackPanel { Spacing = 6, Children = { _oAll, _oRemove } };
        _picked.Children.Add(Explained("Door or window", "What's picked on the plan. A new one dropped in a wall is like the one picked last.",
            Field("Kind", _oKind), Field("Shape", _oShape),
            Field("Width", _oW, tip: "Across the opening (mm)."), Field("Height", _oH, tip: "From its bottom to the top of its arch (mm)."),
            _oSillRow, _oLookRow, _oHingeRow, _oBarsRow, _oFillRow, buttons));
        _rTurnRow = Field("Runs", _rTurn);
        _rPitchRow = Field("Pitch", _rPitch, tip: "How steep the slopes are (degrees from flat).");
        _rEaveRow = Field("Eaves", _rEave, tip: "How far the roof reaches past the walls at the bottom of its slopes (mm).");
        _rWindowRow = Field("Gable window", _rWindow, tip: "A window in each end wall of the roof.");
        _roofPanel.Children.Add(Explained("Roof", "It rests on the column caps round its rectangle and lifts off to show the rooms. Drag out another with Roof: it's like this one.",
            _rInfo, Field("Shape", _rKind), _rTurnRow, _rPitchRow, Field("Covering", _rCover), _rEaveRow, _rWindowRow, _rRemove));
        _roomPanel.Children.Add(Explained("Room", "A room's floor can have a pattern of its own. Upstairs it's etched on the floor; on the ground floor it's a loose floor that drops in between the walls.",
            _roomInfo, Field("Floor", _roomFloor)));
        return Stack(_pickedNone, _picked, _roofPanel, _roomPanel);
    }

    Control LookTab()
    {
        _wallMmRow = Field("Wall thickness", _wallMm, tip: "A box wall's thickness, outside face to inside face (mm).");
        _tileRow = Field("Tiles", _tileTexture);
        return Stack(
            Explained("Look", "A look for the whole building, the same as the other terrain makers': the walls' texture, the doors and windows dropped from now on, the floor tiles.",
                Field("Theme", _theme),
                Field("Figures", _scale, tip: "Changing it scales the walls, doors and windows to suit figures that size.")),
            Explained("Walls", "Every straight stretch of wall between two columns is one wall: a box (thick, stands on its own) or a flat panel. A column goes at every corner, end and junction.",
                Field("Walls", _build), _wallMmRow,
                Field("Panel thickness", _sheet, tip: "The sheet everything is cut from (mm)."),
                Field("Columns", _column, tip: "How wide each column is (mm)."),
                Field("Storey height", _storeyH, tip: "Each floor's walls, rounded to whole layers of the sheet (mm)."),
                Field("Texture", _texture), Field("Upstairs", _upper), Field("Etch as", _etch),
                Field("Floors", _detail, tip: "What's etched on the floors between storeys."),
                Field("Slot fit", _fit, tip: "Gap added to slots so the pieces slide together (mm), on top of the kerf."),
                Field("Layout", _seed, tip: "Stones, planks and grain are laid out at random: each number gives a different layout.")),
            Explained("This floor", "Railings go round the edges of a floor with no wall on them: a balcony, or a terrace on the roof of the rooms below.",
                _railings),
            Explained("Grid and ground", "The plan's squares: walls run along their lines. 25.4 mm (an inch) suits most tabletop games.",
                Field("Square", _square, tip: "One square of the plan (mm)."),
                _tiles, _tileRow),
            Explained("Cutting", "Your material's cut settings make slots come out their drawn width (the kerf is allowed for).",
                Field("Cut with", _cut), _cutNote, Field("Etch with", _etchWith)));
    }

    void Library()
    {
        var mats = _library?.Materials ?? [];
        _cuts = [null, .. mats.SelectMany(m => m.Recipes.Where(r => r.Settings.Mode == LayerMode.Cut).Select(r => ((Material, Recipe)?)(m, r)))];
        _etches = [null, .. mats.SelectMany(m => m.Recipes.Where(r => r.Settings.Mode == LayerMode.Fill).Select(r => ((Material, Recipe)?)(m, r)))];
        _cut.ItemsSource = _cuts.Select(c => c is { } x ? $"{x.M.Label} · {x.R.Title}" + (x.R.Settings.KerfMm > 0 ? $" (kerf {x.R.Settings.KerfMm:0.###} mm)" : "") : "As set on the cut layer").ToList();
        _etchWith.ItemsSource = _etches.Select(c => c is { } x ? $"{x.M.Label} · {x.R.Title}" : "As set on the etch layer").ToList();
        _cut.SelectedIndex = _etchWith.SelectedIndex = 0;
    }

    (Material M, Recipe R)? CutRecipe => _cut.SelectedIndex > 0 && _cut.SelectedIndex < _cuts.Count ? _cuts[_cut.SelectedIndex] : null;
    (Material M, Recipe R)? EtchRecipe => _etchWith.SelectedIndex > 0 && _etchWith.SelectedIndex < _etches.Count ? _etches[_etchWith.SelectedIndex] : null;

    void CutChosen()
    {
        if (!_loading && CutRecipe is { } c)
        {
            _loading = true;
            if (c.M.ThicknessMm is { } t) _sheet.Value = (decimal)Math.Clamp(t, (double)_sheet.Minimum, (double)_sheet.Maximum);
            if (c.R.SlotFitMm is { } f) _fit.Value = (decimal)Math.Clamp(f, -0.5, 2);
            int same = _etches.FindIndex(e => e is { } x && x.M.Id == c.M.Id);
            if (same > 0 && EtchRecipe is null) _etchWith.SelectedIndex = same;
            _loading = false;
        }
        _cutNote.Text = CutRecipe is not { } r
            ? "No kerf allowance: slots come out a little loose. Pick your material's cut settings (Laser → Tests → Cut test measures the kerf)."
            : r.R.Settings.KerfMm > 0 ? $"{r.M.Label}: kerf {r.R.Settings.KerfMm:0.###} mm, allowed for on the cut layer."
            : $"{r.M.Label}: no kerf measured yet, so slots come out a little loose (run a cut test).";
        LookChanged();
    }

    // ---- wiring ----

    void Hook()
    {
        _plan.Changed += (d, what) => Apply(d, what, fromPlan: true);
        _plan.SelectionChanged += ShowPicked;
        _roofIt.Click += (_, _) => RoofIt();
        _railings.IsCheckedChanged += (_, _) =>
        {
            if (_loading) return;
            SetLevel(_design.Levels[_plan.Level] with { Railings = _railings.IsChecked == true }, "Railings");
        };
        _rKind.SelectionChanged += (_, _) => RoofChanged(kindChanged: true);
        foreach (var c in new[] { _rTurn, _rCover, _rWindow }) c.SelectionChanged += (_, _) => RoofChanged();
        foreach (var n in new[] { _rPitch, _rEave }) n.ValueChanged += (_, _) => RoofChanged();
        _rRemove.Click += (_, _) =>
        {
            int i = _plan.SelectedRoof;
            if (i < 0 || i >= _design.Roofs.Count) return;
            _plan.SelectRoof(-1);
            Apply(_design with { Roofs = [.. _design.Roofs.Where((_, j) => j != i)] }, "Take away a roof");
        };
        _roomFloor.SelectionChanged += (_, _) => RoomFloorChanged();
        _level.SelectionChanged += (_, _) => { if (!_loading && _level.SelectedIndex >= 0) ShowLevel(_level.SelectedIndex); };
        _addLevel.Click += (_, _) => AddLevel();
        _copyUp.Click += (_, _) => CopyUp();
        _removeLevel.Click += (_, _) => RemoveLevel();
        _undoButton.Click += (_, _) => Undo();
        _redoButton.Click += (_, _) => Redo();
        _walls.SelectedIndex = 1;
        _walls.SelectionChanged += (_, _) => _view.SetWalls((WallsShown)Math.Max(0, _walls.SelectedIndex));
        _upTo.SelectionChanged += (_, _) => { if (!_loading) _view.SetUpTo(_upTo.SelectedIndex <= 0 ? int.MaxValue : _upTo.SelectedIndex - 1); };

        foreach (var c in new[] { _oKind, _oShape, _oLook, _oHinge, _oBars, _oFill }) c.SelectionChanged += (_, _) => PickedChanged();
        foreach (var n in new[] { _oW, _oH, _oSill }) n.ValueChanged += (_, _) => PickedChanged();
        _oAll.Click += (_, _) => AllLikePicked();
        _oRemove.Click += (_, _) =>
        {
            int i = _plan.Selected;
            if (i < 0) return;
            _plan.Select(-1);
            var level = _design.Levels[_plan.Level];
            SetLevel(level with { Openings = [.. level.Openings.Where((_, j) => j != i)] }, "Take away a door or window");
        };

        foreach (var n in new[] { _wallMm, _sheet, _column, _storeyH, _square, _fit, _seed }) n.ValueChanged += (_, _) => LookChanged();
        foreach (var c in new[] { _texture, _upper, _etch, _detail, _tileTexture, _etchWith }) c.SelectionChanged += (_, _) => LookChanged();
        _tiles.IsCheckedChanged += (_, _) => LookChanged();
        _build.SelectionChanged += (_, _) => BuildChosen();
        _theme.SelectionChanged += (_, _) => ThemeChosen();
        _scale.SelectionChanged += (_, _) => ScaleChosen();
        _cut.SelectionChanged += (_, _) => CutChosen();
    }

    void OnKey(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.Z) { if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) Redo(); else Undo(); e.Handled = true; }
        else if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.Y) { Redo(); e.Handled = true; }
        else if (e.KeyModifiers == KeyModifiers.None && FocusManager?.GetFocusedElement() is not (TextBox or NumericUpDown))
        {
            PlanTool? t = e.Key switch { Key.S => PlanTool.Select, Key.R => PlanTool.Room, Key.W => PlanTool.Wall, Key.E => PlanTool.Erase, Key.D => PlanTool.Door, Key.O => PlanTool.Doorway, Key.N => PlanTool.Window, Key.L => PlanTool.Floor, Key.T => PlanTool.Roof, _ => null };
            if (t is { } tool) { PickTool(tool); e.Handled = true; }
        }
    }

    void PickTool(PlanTool t)
    {
        _plan.Tool = t;
        foreach (var (k, b) in _tools) b.IsChecked = k == t;
    }

    /// <summary>A change to the design: kept for Undo, shown, and the pieces made again.</summary>
    void Apply(HouseDesign d, string what, bool fromPlan = false)
    {
        _undo.Add(_design);
        if (_undo.Count > 200) _undo.RemoveAt(0);
        _redo.Clear();
        _design = d;
        if (!fromPlan) _plan.Show(_design, _plan.Level);
        Buttons();
        ShowPicked();
        Changed();
        _ = what;
    }

    void SetLevel(Level level, string what)
    {
        var levels = _design.Levels.ToList();
        levels[_plan.Level] = level;
        Apply(_design with { Levels = levels }, what);
    }

    void Undo()
    {
        if (_undo.Count == 0) return;
        _redo.Add(_design);
        _design = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        Reload();
    }

    void Redo()
    {
        if (_redo.Count == 0) return;
        _undo.Add(_design);
        _design = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        Reload();
    }

    void Reload()
    {
        _plan.Select(-1);
        LoadLook();
        ShowLevel(Math.Min(_plan.Level, _design.Levels.Count - 1));
        Changed();
    }

    void Buttons()
    {
        _undoButton.IsEnabled = _undo.Count > 0;
        _redoButton.IsEnabled = _redo.Count > 0;
        _removeLevel.IsEnabled = _design.Levels.Count > 1;
        _addLevel.IsEnabled = _design.Levels.Count < HouseKitMaker.MaxLevels;
        _copyUp.IsEnabled = _plan.Level > 0;
    }

    void ShowLevel(int k)
    {
        _loading = true;
        _level.ItemsSource = Enumerable.Range(1, _design.Levels.Count).Select(i => i == 1 ? "1 (ground)" : i.ToString()).ToList();
        _level.SelectedIndex = k;
        _upTo.ItemsSource = new[] { "Every floor" }.Concat(Enumerable.Range(1, _design.Levels.Count).Select(i => $"Floor {i}")).ToList();
        _upTo.SelectedIndex = 0;
        _loading = false;
        _view.SetUpTo(int.MaxValue);
        _plan.Show(_design, k);
        _loading = true;
        _railings.IsChecked = _design.Levels[k].Railings;
        _railings.IsEnabled = k > 0;
        _loading = false;
        Buttons();
        ShowPicked();
    }

    void AddLevel()
    {
        if (_design.Levels.Count >= HouseKitMaker.MaxLevels) return;
        Apply(_design with { Levels = [.. _design.Levels, new Level()] }, "Add a floor");
        ShowLevel(_design.Levels.Count - 1);
    }

    void CopyUp()
    {
        int k = _plan.Level;
        if (k == 0) return;
        var level = _design.Levels[k];
        SetLevel(level with { Walls = [.. level.Walls.Union(_design.Levels[k - 1].Walls)] }, "Copy the walls up");
    }

    void RemoveLevel()
    {
        if (_design.Levels.Count <= 1) return;
        int k = _plan.Level;
        Apply(_design with { Levels = [.. _design.Levels.Where((_, i) => i != k)] }, "Remove a floor");
        ShowLevel(Math.Max(0, k - 1));
    }

    // ---- what's picked ----

    static readonly OpeningShape[] DoorShapes = [OpeningShape.Rectangle, OpeningShape.Arched, OpeningShape.Pointed, OpeningShape.Round, OpeningShape.ArrowSlit, OpeningShape.Chamfered, OpeningShape.Rough];

    Opening? Picked => _plan.Selected >= 0 && _plan.Selected < _design.Levels[_plan.Level].Openings.Count ? _design.Levels[_plan.Level].Openings[_plan.Selected] : null;

    void ShowPicked()
    {
        var o = Picked;
        var roof = PickedRoof;
        var room = PickedRoom;
        _picked.IsVisible = o is not null;
        _roofPanel.IsVisible = roof is not null;
        _roomPanel.IsVisible = room is not null;
        _pickedNone.IsVisible = o is null && roof is null && room is null;
        if (roof is not null) ShowRoof(roof);
        if (room is { } rm) ShowRoom(rm.Room, rm.Plan);
        if (o is null) return;
        _loading = true;
        _oKind.SelectedIndex = (int)o.Kind;
        _oShape.SelectedIndex = Math.Max(0, Array.IndexOf(DoorShapes, o.Shape));
        _oW.Value = (decimal)o.WidthMm; _oH.Value = (decimal)o.HeightMm; _oSill.Value = (decimal)o.SillMm;
        _oLook.SelectedIndex = (int)o.Look; _oHinge.SelectedIndex = (int)o.Hinge; _oBars.SelectedIndex = (int)o.Bars; _oFill.SelectedIndex = (int)o.Fill;
        _loading = false;
        PickedRows(o);
    }

    void PickedRows(Opening o)
    {
        bool window = o.Kind == OpeningKind.Window;
        _oSillRow.IsVisible = _oBarsRow.IsVisible = window;
        _oFillRow.IsVisible = window && _design.Style.BoxWalls;
        _oLookRow.IsVisible = _oHingeRow.IsVisible = o.Kind == OpeningKind.Door;
    }

    void PickedChanged()
    {
        if (_loading || Picked is not { } o) return;
        var kind = (OpeningKind)Math.Max(0, _oKind.SelectedIndex);
        var next = o with
        {
            Kind = kind, Shape = DoorShapes[Math.Clamp(_oShape.SelectedIndex, 0, DoorShapes.Length - 1)],
            WidthMm = V(_oW, o.WidthMm), HeightMm = V(_oH, o.HeightMm), SillMm = V(_oSill, o.SillMm),
            Look = (DoorLook)Math.Max(0, _oLook.SelectedIndex), Hinge = (HingeSide)Math.Max(0, _oHinge.SelectedIndex),
            Bars = (WindowBars)Math.Max(0, _oBars.SelectedIndex), Fill = (WindowFill)Math.Max(0, _oFill.SelectedIndex),
        };
        // A window made into a door (or back) takes the usual size for one.
        if (kind != o.Kind && (kind == OpeningKind.Window) != (o.Kind == OpeningKind.Window))
        {
            var s = _design.Style;
            next = kind == OpeningKind.Window
                ? next with { Shape = s.WindowShape, WidthMm = s.WindowWidthMm, HeightMm = s.WindowHeightMm }
                : next with { Shape = s.DoorShape, WidthMm = s.DoorWidthMm, HeightMm = s.DoorHeightMm };
        }
        var level = _design.Levels[_plan.Level];
        var list = level.Openings.ToList();
        list[_plan.Selected] = next;
        SetLevel(level with { Openings = list }, "Change a door or window");
        if (kind != o.Kind) ShowPicked(); else PickedRows(next);
    }

    void AllLikePicked()
    {
        if (Picked is not { } o) return;
        var level = _design.Levels[_plan.Level];
        SetLevel(level with
        {
            Openings = [.. level.Openings.Select(x => x.Kind == o.Kind ? o with { X = x.X, Y = x.Y, Hinge = x.Hinge } : x)],
        }, "Make the doors or windows alike");
    }

    // ---- a roof or a room picked ----

    RoofArea? PickedRoof => _plan.SelectedRoof >= 0 && _plan.SelectedRoof < _design.Roofs.Count ? _design.Roofs[_plan.SelectedRoof] : null;

    (Room Room, PlanShape Plan)? PickedRoom
    {
        get
        {
            if (_plan.SelectedRoom is not { } at) return null;
            var plan = PlanShape.Of(_design.Levels[_plan.Level].Walls);
            int i = plan.RoomAt(at.X, at.Y);
            return i >= 0 ? (plan.Rooms[i], plan) : null;
        }
    }

    static readonly string[] Ridges = ["Ridge left to right", "Ridge top to bottom"];
    static readonly string[] Slopes = ["Down towards the bottom of the plan", "Down towards the right", "Down towards the top", "Down towards the left"];
    static List<string> Coverings(RoofKind kind) => kind == RoofKind.Flat
        ? [.. PhotonCanvas.Addon.FloorTiles.FloorTextures.Presets.Select(p => p.Name)]
        : [.. PhotonCanvas.Addon.Roofs.Shingles.Presets.Select(p => p.Name)];

    void ShowRoof(RoofArea r)
    {
        _loading = true;
        _rInfo.Text = $"{r.X1 - r.X0} × {r.Y1 - r.Y0} squares on top of floor {r.Level + 1}{(r.Level == 0 ? " (the ground floor)" : "")}.";
        _rKind.SelectedIndex = (int)r.Kind;
        _rTurn.ItemsSource = r.Kind == RoofKind.LeanTo ? Slopes.ToList() : Ridges.ToList();
        _rTurn.SelectedIndex = r.Kind == RoofKind.LeanTo ? ((r.Turn % 4) + 4) % 4 : ((r.Turn % 2) + 2) % 2;
        var covers = Coverings(r.Kind);
        _rCover.ItemsSource = covers;
        _rCover.SelectedIndex = Math.Max(0, covers.IndexOf(r.Covering));
        _rPitch.Value = (decimal)Math.Clamp(r.PitchDeg, 10, 75);
        _rEave.Value = (decimal)Math.Clamp(r.EaveMm, 0, 40);
        _rWindow.SelectedIndex = (int)r.Window;
        _rTurnRow.IsVisible = r.Kind is RoofKind.Gable or RoofKind.LeanTo;
        _rPitchRow.IsVisible = _rEaveRow.IsVisible = r.Kind != RoofKind.Flat;
        _rWindowRow.IsVisible = r.Kind is RoofKind.Gable or RoofKind.LeanTo;
        _loading = false;
    }

    void RoofChanged(bool kindChanged = false)
    {
        if (_loading || PickedRoof is not { } r) return;
        var kind = (RoofKind)Math.Max(0, _rKind.SelectedIndex);
        int turn = Math.Max(0, _rTurn.SelectedIndex);
        string cover = _rCover.SelectedItem as string ?? r.Covering;
        if (kindChanged)
        {
            // A lean-to keeps the way the ridge ran; a flat roof takes a floor's covering, a pitched one a roof's.
            turn = ((r.Turn % 2) + 2) % 2;
            if ((kind == RoofKind.Flat) != (r.Kind == RoofKind.Flat))
                cover = kind == RoofKind.Flat ? "Stone blocks" : PhotonCanvas.Addon.Roofs.Shingles.Preset(_design.Style.RoofTexture) is not null ? _design.Style.RoofTexture : "Slate";
        }
        var next = r with
        {
            Kind = kind, Turn = turn, Covering = cover,
            PitchDeg = V(_rPitch, r.PitchDeg), EaveMm = V(_rEave, r.EaveMm), Window = (PhotonCanvas.Addon.Roofs.GableWindow)Math.Max(0, _rWindow.SelectedIndex),
        };
        if (next == r) return;
        var roofs = _design.Roofs.ToList();
        roofs[_plan.SelectedRoof] = next;
        Apply(_design with { Roofs = roofs }, "Change a roof");
    }

    /// <summary>A roof over the top floor's rooms: one over them all if together they make a rectangle, otherwise one over each rectangular room.</summary>
    void RoofIt()
    {
        int top = _design.Levels.Count - 1;
        while (top > 0 && _design.Levels[top].Walls.Count == 0) top--;
        var plan = PlanShape.Of(_design.Levels[top].Walls);
        if (plan.Rooms.Count == 0) { _status.Text = "There are no rooms on the top floor to roof: close some in first."; return; }
        var like = PickedRoof ?? _design.Roofs.LastOrDefault();
        RoofArea Over(Clipper2Lib.RectD b)
        {
            var r = RoofArea.Over(top, new GridPt((int)Math.Round(b.left), (int)Math.Round(b.top)), new GridPt((int)Math.Round(b.right), (int)Math.Round(b.bottom)), _design.Style);
            return like is null ? r : r with { Kind = like.Kind == RoofKind.LeanTo ? RoofKind.Gable : like.Kind, PitchDeg = like.PitchDeg, Covering = like.Covering, EaveMm = like.EaveMm, Window = like.Window };
        }
        var all = Walls.Geo.Bounds(new Clipper2Lib.PathsD(plan.Rooms.SelectMany(r => r.Area)));
        var roofs = new List<RoofArea>();
        double total = plan.Rooms.Sum(r => r.AreaSquares);
        if (Math.Abs(total - (all.right - all.left) * (all.bottom - all.top)) < 1e-6) roofs.Add(Over(all));
        else
        {
            foreach (var room in plan.Rooms)
            {
                var b = Walls.Geo.Bounds(room.Area);
                if (Math.Abs(room.AreaSquares - (b.right - b.left) * (b.bottom - b.top)) < 1e-6) roofs.Add(Over(b));
            }
            if (roofs.Count < plan.Rooms.Count) _status.Text = "Rooms that aren't rectangles have no roof yet: drag one over them with Roof, or give them a flat one.";
        }
        Apply(_design with { Roofs = [.. _design.Roofs.Where(r => r.Level != top), .. roofs] }, "Roof it");
    }

    void ShowRoom(Room room, PlanShape plan)
    {
        _loading = true;
        _roomInfo.Text = $"Room {room.Index + 1} on floor {_plan.Level + 1}: {room.AreaSquares:0.#} squares.";
        var tex = HouseKitMaker.RoomTexture(_design.Levels[_plan.Level], plan, room);
        var names = PhotonCanvas.Addon.FloorTiles.FloorTextures.Presets.Select(p => p.Name).ToList();
        _roomFloor.SelectedIndex = tex is null ? 0 : names.IndexOf(tex) + 1;
        _loading = false;
    }

    void RoomFloorChanged()
    {
        if (_loading || PickedRoom is not { } rm || _plan.SelectedRoom is not { } at) return;
        var level = _design.Levels[_plan.Level];
        // Any other mark in the same room goes: one pattern a room.
        var rest = level.RoomFloors.Where(f => rm.Plan.RoomAt(f.X, f.Y) != rm.Room.Index).ToList();
        var names = PhotonCanvas.Addon.FloorTiles.FloorTextures.Presets.Select(p => p.Name).ToList();
        if (_roomFloor.SelectedIndex > 0 && _roomFloor.SelectedIndex - 1 < names.Count)
            rest.Add(new RoomFloor { X = Math.Round(at.X, 2), Y = Math.Round(at.Y, 2), Texture = names[_roomFloor.SelectedIndex - 1] });
        SetLevel(level with { RoomFloors = rest }, "Change a room's floor");
    }

    // ---- the look ----

    static decimal Dm(double v) => (decimal)v;
    static double V(NumericUpDown n, double fallback) => n.Value is { } v ? (double)v : fallback;

    void LoadLook()
    {
        var s = _design.Style;
        _loading = true;
        _theme.SelectedIndex = Math.Max(0, BuildingThemes.Names.ToList().IndexOf(s.Theme));
        int sc = Array.FindIndex(Scales, x => Math.Abs(x - s.ScaleMm) < 1e-6);
        _scale.SelectedIndex = sc < 0 ? 1 : sc;
        _build.SelectedIndex = s.BoxWalls ? 0 : 1;
        _wallMm.Value = Dm(s.WallMm); _sheet.Value = Dm(s.WallSheetMm); _column.Value = Dm(s.ColumnMm); _storeyH.Value = Dm(s.StoreyHeightMm);
        _square.Value = Dm(_design.SquareMm); _fit.Value = Dm(s.FitMm); _seed.Value = Math.Clamp(s.Seed, 1, 9999);
        _texture.SelectedIndex = Math.Max(0, _textures.IndexOf(s.Texture));
        _upper.SelectedIndex = s.UpperTexture is { Length: > 0 } up && _textures.IndexOf(up) is >= 0 and var ui ? ui + 1 : 0;
        _etch.SelectedIndex = (int)s.Etch;
        _detail.SelectedIndex = (int)s.Detail;
        _tiles.IsChecked = s.FloorTiles;
        _tileTexture.SelectedIndex = Math.Max(0, PhotonCanvas.Addon.FloorTiles.FloorTextures.Presets.ToList().FindIndex(p => p.Name == s.FloorTexture));
        _wallMmRow.IsVisible = s.BoxWalls;
        _tileRow.IsVisible = s.FloorTiles;
        _loading = false;
    }

    void LookChanged()
    {
        if (_loading) return;
        string? upper = _upper.SelectedIndex > 0 && _upper.SelectedIndex - 1 < _textures.Count ? _textures[_upper.SelectedIndex - 1] : null;
        var presets = PhotonCanvas.Addon.FloorTiles.FloorTextures.Presets;
        var style = _design.Style with
        {
            BoxWalls = _build.SelectedIndex == 0, WallMm = V(_wallMm, 12), WallSheetMm = V(_sheet, 3), ColumnMm = V(_column, 24), StoreyHeightMm = V(_storeyH, 48),
            FitMm = V(_fit, 0.15), Seed = (int)V(_seed, 1),
            Texture = _textures[Math.Clamp(_texture.SelectedIndex, 0, _textures.Count - 1)], UpperTexture = upper, Etch = (EtchStyle)Math.Max(0, _etch.SelectedIndex),
            Detail = (WoodDetail)Math.Max(0, _detail.SelectedIndex),
            FloorTiles = _tiles.IsChecked == true, FloorTexture = presets[Math.Clamp(_tileTexture.SelectedIndex, 0, presets.Count - 1)].Name,
        };
        double square = V(_square, 25.4);
        _wallMmRow.IsVisible = style.BoxWalls;
        _tileRow.IsVisible = style.FloorTiles;
        Apply(_design with { Style = style with { SquareMm = square }, SquareMm = square }, "Change the look");
    }

    void BuildChosen()
    {
        if (_loading) return;
        if (_build.SelectedIndex == 0)
        {
            double need = Math.Ceiling(_design.Style.NarrowestBoxColumn + 1);
            if (V(_column, 24) < need) { _loading = true; _column.Value = Dm(need); _loading = false; }
        }
        LookChanged();
    }

    void ThemeChosen()
    {
        if (_loading) return;
        string theme = BuildingThemes.Names[Math.Clamp(_theme.SelectedIndex, 0, BuildingThemes.Names.Count - 1)];
        var style = BuildingThemes.Apply(_design.Style, theme) with { Items = [] };
        Apply(_design with { Style = style }, "Change the theme");
        LoadLook();
    }

    void ScaleChosen()
    {
        if (_loading) return;
        double to = Scales[Math.Clamp(_scale.SelectedIndex, 0, Scales.Length - 1)], k = to / Math.Max(1, _design.Style.ScaleMm);
        if (Math.Abs(k - 1) < 1e-9) return;
        var style = _design.Style.Scaled(k) with { Items = [] };
        Apply(_design with
        {
            Style = style,
            Levels = [.. _design.Levels.Select(l => l with { Openings = [.. l.Openings.Select(o => o with { WidthMm = Math.Round(o.WidthMm * k, 1), HeightMm = Math.Round(o.HeightMm * k, 1), SillMm = Math.Round(o.SillMm * k, 1) })] })],
        }, "Change the figures");
        LoadLook();
    }

    // ---- saved between times ----

    string? LastFile => _dataFolder is null ? null : Path.Combine(_dataFolder, "last.json");

    HouseDesign? LoadLast()
    {
        try { return LastFile is { } f && File.Exists(f) ? HouseDesign.FromJson(File.ReadAllText(f)) : null; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    void SaveLast()
    {
        try
        {
            if (LastFile is not { } f) return;
            Directory.CreateDirectory(Path.GetDirectoryName(f)!);
            File.WriteAllText(f, _design.ToJson());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    // ---- making the pieces ----

    double Longest => Math.Max(Math.Max(_machine.FieldW, _machine.FieldH), 50) - 4;

    void Changed()
    {
        _debounce.Stop();
        _debounce.Start();
    }

    async Task Rebuild()
    {
        int gen = ++_generation;
        var design = _design;
        double longest = Math.Min(Longest, Math.Min(_machine.FieldW, _machine.FieldH) - 4);
        HouseKit kit;
        _status.Text = "Making the pieces…";
        try { kit = await Task.Run(() => HouseKitMaker.Make(design, longest)); }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or ArithmeticException or IndexOutOfRangeException)
        {
            if (gen == _generation) { _view.Show(null, e.Message); _pieces.Show(null, e.Message); _status.Text = ""; }
            return;
        }
        if (gen != _generation) return;
        _status.Text = "";
        _kit = kit;
        _kitFrom = design.ToJson();
        _plan.ShowMade(kit.Walls);
        var all = kit.Findings.Concat(HousePlacement.TooBig(kit, _machine)).ToList();
        string? bad = all.FirstOrDefault(f => f.Level == Severity.Bad)?.Text;
        _view.Show(kit.Solids.Count > 0 ? kit : null, bad ?? "Nothing to show yet: draw some walls on the plan.");
        _pieces.Show(kit.Pieces, bad ?? "No pieces yet.");
        _findings.Children.Clear();
        foreach (var f in all.OrderByDescending(f => f.Level)) _findings.Children.Add(FindingRow(f));
        _summary.Text = kit.PieceCount == 0 ? "" : $"{kit.PieceCount} pieces from the {design.Style.WallSheetMm:0.##} mm sheet: {kit.Walls.Count} walls, labelled on the plan.";
        _make.IsEnabled = kit.Ok && kit.PieceCount > 0 && bad is null;
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
        g.Children.Add(new TextBlock { Text = f.Text, TextWrapping = TextWrapping.Wrap }.With(t => Grid.SetColumn(t, 1)));
        return g;
    }

    bool _making;

    static Func<Layer, Layer>? ApplyRecipe((Material M, Recipe R)? r) =>
        r is { } c ? l => c.R.Settings.ApplyTo(l) with { Material = new MaterialRef(c.M.Id, c.M.Label, c.R.Id, c.R.Title) } : null;

    async void Make()
    {
        if (_making) return;
        _making = true;
        _make.IsEnabled = false;
        _status.Text = "Putting the pieces in the design…";
        try
        {
            var design = _design;
            double longest = Math.Min(Longest, Math.Min(_machine.FieldW, _machine.FieldH) - 4);
            var built = _kit is not null && _kitFrom == design.ToJson() ? _kit : null;
            var options = new PlaceOptions
            {
                WallCut = ApplyRecipe(CutRecipe), FurnitureCut = ApplyRecipe(CutRecipe), EtchSettings = ApplyRecipe(EtchRecipe),
                Replace = _editing is { } ed && HousePlacement.CountEverywhere(_doc.State, ed.Id) > 0 ? ed.Id : null,
            };
            var before = _doc.State;
            var (kit, s, pages, id) = await Task.Run(() =>
            {
                var kit = built ?? HouseKitMaker.Make(design, longest);
                if (!kit.Ok) throw new InvalidOperationException(kit.Findings.First(f => f.Level == Severity.Bad).Text);
                var (s, pages, id) = HousePlacement.Place(before, kit, design, _machine, options);
                return (kit, s, pages, id);
            });
            if (!ReferenceEquals(_doc.State, before)) { _status.Text = "The design changed meanwhile: press again to put the pieces in."; return; }
            _doc.Commit(options.Replace is null ? "Building" : "Update the building", s, HousePlacement.ShapesOf(s, id));
            _editing = (id, design);
            _make.Content = "Update in the design";
            _status.Text = $"{(options.Replace is null ? "Put" : "Updated")} {kit.PieceCount} pieces in the design over {pages} page{(pages == 1 ? "" : "s")} (a bed each)" +
                           $": outlines on \"{HousePlacement.CutLayer}\", the texture and detail on \"{HousePlacement.EtchLayer}\" and \"{HousePlacement.LineLayer}\" (done first).";
        }
        catch (InvalidOperationException ex) { _status.Text = ex.Message; }
        catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException or ArithmeticException or NullReferenceException)
        {
            AppLog.Error("houseeditor", ex);
            _status.Text = $"Couldn't put the building in: {ex.Message}";
        }
        finally
        {
            _making = false;
            _make.IsEnabled = _kit?.Ok ?? true;
        }
    }
}
