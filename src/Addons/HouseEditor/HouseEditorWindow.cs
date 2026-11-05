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
    readonly ComboBox _walls = Combo("Everything, roofs on", "Everything, but the roof off", "Walls cut away", "Walls down"), _upTo = Combo("Every floor");
    static readonly double[] Depths = [0.3, 0.6, 1, 1.5];
    readonly CheckBox _realEtch = new() { Content = "Realistic engraving", IsChecked = true, VerticalAlignment = VerticalAlignment.Center };
    readonly ComboBox _depth = Combo("0.3 mm deep", "0.6 mm deep", "1 mm deep", "1.5 mm deep");

    // What's picked on the plan.
    readonly StackPanel _picked = new() { Spacing = 8 };
    readonly TextBlock _pickedNone = Hint("Pick Select and click a door or window on the plan to change it (or drop one in a wall with Door, Doorway or Window), click a wall, a column or a railing, a roof, or inside a room (click a roofed room again to swap between the roof and the room).");
    readonly ComboBox _oKind = Combo("Door that opens", "Doorway (left open)", "Window");
    readonly ComboBox _oShape = Combo("Square", "Round arch", "Pointed arch", "Round", "Arrow slit", "Angled corners (sci-fi)", "Rough (cave)");
    readonly NumericUpDown _oW = Number(2, 300, 0.5m, "0.#"), _oH = Number(2, 300, 0.5m, "0.#"), _oSill = Number(1, 300, 0.5m, "0.#");
    readonly ComboBox _oLook = Combo("Planks", "Planks with iron studs", "Metal blast door", "Bars (a portcullis)", "Stone slab"), _oHinge = Combo("Left (seen from outside)", "Right (seen from outside)");
    readonly ComboBox _oBars = Combo("None", "A cross", "Upright bars"), _oFill = Combo("Nothing (bars left in the wall)", "A frame with its bars", "A shutter, glued in", "Shutters that open");
    readonly Button _oAll = UiKit.Button("Make every one on this floor like it"), _oRemove = UiKit.Button("Take it away");
    readonly CheckBox _oOpens = new() { Content = "Opens on hinges" };
    readonly CheckBox _doorsOpen = new() { Content = "Doors open", VerticalAlignment = VerticalAlignment.Center };
    readonly CheckBox _realistic = new() { Content = "Realistic look", VerticalAlignment = VerticalAlignment.Center };
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
    // A wall picked.
    readonly StackPanel _wallPanel = new() { Spacing = 8 };
    readonly TextBlock _wInfo = Hint();
    readonly ComboBox _wTexture = Combo();
    readonly NumericUpDown _wWear = Number(0, 100, 5, "0"), _wDetail = Number(0, 10, 0.1m, "0.#");
    readonly Button _wAll = UiKit.Button("Make every wall on this floor like it"), _wRest = UiKit.Button("Like the rest of the building");
    // Its other side.
    readonly TextBlock _wSides = Hint();
    readonly ComboBox _wBack = Combo();
    readonly NumericUpDown _wBackWear = Number(0, 100, 5, "0");
    Control _wBackWearRow = null!;
    // Furniture and ways up: what to put down, and one picked.
    readonly StackPanel _placePanel = new() { Spacing = 8 };
    readonly ListBox _placeList = new() { MaxHeight = 260, SelectionMode = SelectionMode.Single };
    readonly StackPanel _wayPlacePanel = new() { Spacing = 8 };
    static readonly string[] WayKinds = ["Stairs (open, with a handrail)", "Solid stairs (stacked layers)", "Spiral stairs", "Leaning ladder", "Hooked ladder"];
    readonly ComboBox _wayPlace = Combo(WayKinds);
    readonly StackPanel _itemPanel = new() { Spacing = 8 };
    readonly TextBlock _iInfo = Hint();
    readonly ItemEditor _iEdit = new(count: false);
    readonly Button _iLeft = UiKit.Button("⟲ Turn left"), _iRight = UiKit.Button("⟳ Turn right"), _iMore = UiKit.Button("Put down more like it"), _iRemove = UiKit.Button("Take it away");
    readonly StackPanel _wayPanel = new() { Spacing = 8 };
    readonly TextBlock _wInfoWay = Hint();
    readonly ComboBox _wayKind = Combo(WayKinds);
    // The stairs maker's settings for the way up picked.
    readonly NumericUpDown _wStairW = Number(5, 300, 1, "0.#"), _wRise = Number(1, 100, 0.5m, "0.#"), _wGoing = Number(3, 60, 0.5m, "0.#");
    readonly ComboBox _wRail = Combo("No handrail", "On the left", "On the right", "Both sides");
    readonly CheckBox _wTreads = new() { Content = "Etch the floor's texture on the steps" };
    readonly NumericUpDown _wSpiralR = Number(10, 200, 1, "0.#"), _wSpiralStep = Number(10, 90, 5, "0.#"), _wHub = Number(4, 100, 0.5m, "0.#"), _wPin = Number(1, 10, 0.5m, "0.#");
    readonly ComboBox _wSpin = Combo("Clockwise going up", "Anticlockwise going up");
    readonly NumericUpDown _wLadderW = Number(4, 100, 0.5m, "0.#"), _wRung = Number(3, 30, 0.5m, "0.#"), _wLean = Number(55, 85, 1, "0");
    readonly Button _wSuit = UiKit.Button("Size them to suit the figures", tip: "Back to the sizes the building's figures want: step height and depth, width, and the rest.");
    Control _wStairRows = null!, _wRailRow = null!, _wRiseRows = null!, _wSpiralRows = null!, _wLadderRows = null!, _wLeanRow = null!;
    readonly Button _wLeft = UiKit.Button("⟲ Turn left"), _wRight = UiKit.Button("⟳ Turn right"), _wRemove = UiKit.Button("Take them away");
    List<FurnitureItem> _placeItems = [];
    List<string> _placeAbout = [];
    readonly ComboBox _placeTheme = Combo([.. FurnitureCatalog.Themes]);
    readonly ComboBox _placeRoom = Combo(["Every room", .. FurnitureCatalog.Rooms, "Plain pieces (every kind)"]);
    readonly TextBlock _placeInfo = new() { TextWrapping = TextWrapping.Wrap, Classes = { "muted" } };
    readonly Button _banana = UiKit.Button("Banana for scale", tip: "Puts a life-size banana on the plan, to see how big the building really is in the 3D view. It's not cut.");
    // A column picked.
    static readonly ColumnShape[] ColumnShapes = [ColumnShape.Auto, ColumnShape.Square, ColumnShape.Octagon, ColumnShape.Round, ColumnShape.Hexagon, ColumnShape.Chamfered, ColumnShape.Fluted];
    static readonly string[] ColumnNames = ["Matching the walls' angles", "Square", "Octagonal", "Round", "Hexagonal", "Chamfered (corners cut off)", "Fluted (grooved, round)"];
    readonly StackPanel _columnPanel = new() { Spacing = 8 };
    readonly TextBlock _cInfo = Hint();
    readonly ComboBox _cShape = Combo(["Like the rest of the building", .. ColumnNames.Skip(1)]);
    readonly Button _cAll = UiKit.Button("Make every column on this floor like it");
    readonly ComboBox _columnShape = Combo(ColumnNames);
    static readonly ColumnMade[] Builds = [ColumnMade.Usual, ColumnMade.Boxed, ColumnMade.Stacked];
    readonly ComboBox _columnBuild = Combo("Boxes where they're square (usual)", "Boxes, any shape", "Stacked layers on a pin"), _columnTexture = Combo();
    readonly ComboBox _cBuild = Combo("Like the rest of the building", "A box", "Stacked layers on a pin"), _cTexture = Combo();
    // A railing picked.
    readonly StackPanel _railPanel = new() { Spacing = 8 };
    readonly TextBlock _railInfo = Hint();
    readonly CheckBox _railHere = new() { Content = "A railing along here" }, _railAll = new() { Content = "Railings round all this floor's open edges" };
    // This floor.
    readonly CheckBox _railings = new() { Content = "Railings round its open edges" };
    static readonly string[] Yards = ["As drawn", "None: up to the walls", "1 square", "2 squares", "3 squares", "4 squares", "6 squares"];
    static readonly int?[] YardSquares = [null, 0, 1, 2, 3, 4, 6];
    readonly ComboBox _yard = Combo(Yards);
    readonly Button _roofIt = UiKit.Button("Roof it", tip: "Puts a roof over the top floor's rooms, in place of any roofs there (gable, the ridge along the longer side). Pick one on the plan with Roof to change it.");

    // The look.
    readonly ComboBox _theme = Combo([.. BuildingThemes.Names]);
    readonly ComboBox _scale = Combo("15 mm", "28 mm", "32 mm", "54 mm");
    static readonly double[] Scales = [15, 28, 32, 54];
    readonly ComboBox _build = Combo("Thick: boxes with a front, back, top and bottom", "Single panel: one sheet thick");
    readonly NumericUpDown _wallMm = Number(6, 60, 1, "0.#"), _sheet = Number(0.5m, 20, 0.5m, "0.##"), _column = Number(5, 200, 1, "0.#"), _storeyH = Number(10, 500, 1, "0.#"),
        _square = Number(10, 100, 0.5m, "0.##"), _fit = Number(-0.5m, 2, 0.05m, "0.##"), _seed = Number(1, 9999, 1, "0");
    readonly ComboBox _texture = Combo(), _upper = Combo(), _etch = Combo("Grooves (etched areas, deeper)", "Lines (scored, faster)");
    readonly NumericUpDown _wear = Number(0, 100, 5, "0"), _detailSize = Number(0, 10, 0.1m, "0.#");
    readonly ComboBox _base = Combo("Jigsaw floor tiles", "One plate, shaped to the building", "Nothing: it stands on the table");
    readonly ComboBox _tileTexture = Combo([.. PhotonCanvas.Addon.FloorTiles.FloorTextures.Presets.Select(p => p.Name)]);
    readonly ComboBox _upperFloor = Combo([.. PhotonCanvas.Addon.FloorTiles.FloorTextures.Presets.Select(p => p.Name)]);
    readonly List<string> _textures = [.. Textures.Presets.Select(p => p.Name)];
    readonly ComboBox _cut = Combo(), _etchWith = Combo();
    readonly TextBlock _cutNote = Hint();
    Control _wallMmRow = null!, _tileRow = null!;
    List<(Material M, Recipe R)?> _cuts = [], _etches = [];

    readonly TextBlock _summary = new() { TextWrapping = TextWrapping.Wrap };
    readonly StackPanel _findings = new() { Spacing = 4 };
    readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Classes = { "muted" } };
    readonly Button _make = new() { Content = "Put in the design", Classes = { "primary" }, MinWidth = 160 };
    readonly Button _booklet = new() { Content = "Building booklet", MinWidth = 140 };
    readonly CheckBox _numbers = new() { Content = "Number the pieces" };
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
        _wTexture.ItemsSource = _textures.ToList();
        _wBack.ItemsSource = new[] { "The same as the front" }.Concat(_textures).ToList();
        _columnTexture.ItemsSource = new[] { "Like the walls" }.Concat(_textures).ToList();
        _cTexture.ItemsSource = new[] { "Like the rest of the building" }.Concat(_textures).ToList();

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
        Tool(PlanTool.Select, "Select", "Click a door or window to change it (on the right) or drag it along its wall; Delete takes it away. Click a wall to give it a texture and wear of its own, a column to change its shape, a railing to take it away, a roof to change it, or inside a room to give it a floor of its own (click again to swap between a roof and the room under it).");
        Tool(PlanTool.Room, "Room", "Drag out a rectangle: four walls round it at once.");
        Tool(PlanTool.Wall, "Wall", "Drag from one grid point to another: straight along, across or corner to corner.");
        Tool(PlanTool.Erase, "Erase", "Drag along walls to take them away, or click one square of wall, or a door or window.");
        bar.Children.Add(Gap());
        Tool(PlanTool.Door, "Door", "Click a wall to put a door in it, and drag it along the wall. Tick Opens on hinges (on the right) to make it swing, and pick its hinge side.");
        Tool(PlanTool.Doorway, "Doorway", "Click a wall for an open doorway, with nothing in it: between rooms, say.");
        Tool(PlanTool.Window, "Window", "Click a wall to put a window in it. Drag it along the wall.");
        bar.Children.Add(Gap());
        Tool(PlanTool.Floor, "Floor", "Drag over squares to lay floor outside the rooms (a balcony or a landing); drag from a laid square to take it up. Rooms get a floor anyway.");
        Tool(PlanTool.Furnish, "Furniture", "Pick a piece on the right, then click a room to put it down. Drag it about; R turns it 45° (Shift+R back), and the mouse wheel while dragging or [ and ] a quarter. Delete takes it away. (U)");
        Tool(PlanTool.Stairs, "Stairs", "Click to put stairs or a ladder up to the floor above: an opening is cut in that floor over them. Drag them about; turn them with R, the mouse wheel while dragging, or [ and ]. (A)");
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
        // What the 3D view shows: wraps onto a second line when the window is narrow.
        var showRow = new WrapPanel { Orientation = Orientation.Horizontal };
        Control Pair(string label, Control c, double width)
        {
            c.Width = width;
            return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 0, 10, 4), Children = { new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Classes = { "muted" } }, c } };
        }
        showRow.Children.Add(Pair("Show", _walls, 170));
        showRow.Children.Add(Pair("up to", _upTo, 120));
        _realEtch.Margin = new Thickness(0, 0, 6, 4);
        showRow.Children.Add(_realEtch);
        ToolTip.SetTip(_realEtch, "On: the engraving is sunk into the pieces as deep as your etch will go, so you can see how the textures stand out. Off: it's painted on flat, which is quicker to turn round.");
        _depth.Width = 120;
        _depth.Margin = new Thickness(0, 0, 10, 4);
        showRow.Children.Add(_depth);
        ToolTip.SetTip(_depth, "How deep the realistic engraving is shown.");
        _doorsOpen.Margin = new Thickness(0, 0, 10, 4);
        showRow.Children.Add(_doorsOpen);
        ToolTip.SetTip(_doorsOpen, "Shows the doors that open swung out on their hinges (and portcullises slid up), so you can see which side each is hinged.");
        _realistic.Margin = new Thickness(0, 0, 0, 4);
        showRow.Children.Add(_realistic);
        ToolTip.SetTip(_realistic, "Draws the building like a photo of the real kit: wood grain on the pieces, charred laser-cut edges, burnt engraving, sunlight with soft shadows (onto the table too) and darker corners. A little slower to turn. The same switch is the bulb button in the corner of every 3D view.");
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

        var bottom = new Grid { ColumnDefinitions = new("*,Auto,Auto,Auto"), Margin = new Thickness(14, 8, 14, 14), ColumnSpacing = 12 };
        bottom.Children.Add(_status);
        ToolTip.SetTip(_booklet, "Step-by-step building instructions as a PDF, like a building set's: every numbered piece, then the building going up a few pieces at a time, the new ones picked out with their numbers.");
        bottom.Children.Add(_booklet.With(c => Grid.SetColumn(c, 1)));
        bottom.Children.Add(_make.With(c => Grid.SetColumn(c, 2)));
        var close = new Button { Content = "Close", IsCancel = true, MinWidth = 100 };
        close.Click += (_, _) => Close();
        bottom.Children.Add(close.With(c => Grid.SetColumn(c, 3)));

        var body = new Grid { ColumnDefinitions = new("*,Auto"), RowDefinitions = new("Auto,*,Auto") };
        body.Children.Add(bar.With(c => Grid.SetColumnSpan(c, 2)));
        body.Children.Add(middle.With(c => Grid.SetRow(c, 1)));
        body.Children.Add(right.With(c => { Grid.SetRow(c, 1); Grid.SetColumn(c, 1); }));
        body.Children.Add(bottom.With(c => { Grid.SetRow(c, 2); Grid.SetColumnSpan(c, 2); }));
        Content = body;

        _debounce.Tick += (_, _) => { _debounce.Stop(); _ = Rebuild(); };
        _make.Click += (_, _) => Make();
        _booklet.Click += (_, _) => _ = Booklet();
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

    void RealisticChanged() => _realistic.IsChecked = PhotonCanvas.App.View3D.Look3D.Realistic;

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
        ToolTip.SetTip(_oOpens, "The door hangs on pins inside the wall and swings open. An arched or pointed door then has a fixed panel over it, because only a square-topped leaf can swing in a thick wall. Off, it's glued in and fills the whole opening.");
        _oBarsRow = Field("Bars", _oBars);
        _oFillRow = Field("In the window", _oFill, tip: "What fills a box wall's window: a frame with its bars, shutters, or nothing.");
        var buttons = new StackPanel { Spacing = 6, Children = { _oAll, _oRemove } };
        _picked.Children.Add(Explained("Door or window", "What's picked on the plan. A new one dropped in a wall is like the one picked last.",
            Field("Kind", _oKind), Field("Shape", _oShape),
            Field("Width", _oW, tip: "Across the opening (mm)."), Field("Height", _oH, tip: "From its bottom to the top of its arch (mm)."),
            _oSillRow, _oLookRow, _oOpens, _oHingeRow, _oBarsRow, _oFillRow, buttons));
        _rTurnRow = Field("Runs", _rTurn);
        _rPitchRow = Field("Pitch", _rPitch, tip: "How steep the slopes are (degrees from flat).");
        _rEaveRow = Field("Eaves", _rEave, tip: "How far the roof reaches past the walls at the bottom of its slopes (mm).");
        _rWindowRow = Field("Gable window", _rWindow, tip: "A window in each end wall of the roof.");
        _roofPanel.Children.Add(Explained("Roof", "It rests on the column caps round its rectangle and lifts off to show the rooms. Drag out another with Roof: it's like this one.",
            _rInfo, Field("Shape", _rKind), _rTurnRow, _rPitchRow, Field("Covering", _rCover), _rEaveRow, _rWindowRow, _rRemove));
        _wallPanel.Children.Add(Explained("Wall", "A straight wall, column to column, can look different from the rest: a brick wall in a stone house, say, or one more worn. A wall with a look of its own is drawn lighter on the plan.",
            _wInfo, Field("Texture", _wTexture),
            Field("Wear %", _wWear, tip: "0 is new and crisp, 100 ancient and battered; 50 is the texture's usual look."),
            Field("Detail size", _wDetail, tip: "The size of the smallest marks (pits, nicks, grain, bolts) in mm; 0 is the texture's own."),
            _wSides,
            Field("Other side", _wBack, tip: "The texture on the wall's other side: the inside of an outside wall. Single-panel walls get it engraved after the cut pieces are turned over (a page of its own); a box wall's back face just takes it."),
            _wBackWearRow = Field("Other side's wear %", _wBackWear, tip: "How worn the other side looks: 0 new and crisp, 100 ancient and battered."),
            new StackPanel { Spacing = 6, Children = { _wAll, _wRest } }));
        _columnPanel.Children.Add(Explained("Column", "A column can have a shape of its own. A shape that a wall would meet at a corner (a square column where walls meet at 45°) is made to suit there instead. A column with a shape of its own is drawn lighter.",
            _cInfo, Field("Shape", _cShape),
            Field("Built as", _cBuild, tip: "A box: a side on each flat, standing in slots in a plate at the bottom and held by a plate at the top, the walls going into its sides. Stacked: layers on a pin, with slots for the walls. Fluted columns are always stacked."),
            Field("Texture", _cTexture, tip: "What's etched on a boxed column's sides."),
            _cAll));
        _railPanel.Children.Add(Explained("Railing", "Railings go round a floor's open edges by themselves. Take one stretch away for steps down, a gap, or a wall you'll add later; a stretch taken away shows dashed, and you can click it to put it back.",
            _railInfo, _railHere, _railAll));
        _roomPanel.Children.Add(Explained("Room", "A room's floor can have a pattern of its own. Upstairs it's etched on the floor; on the ground floor it's a loose floor that drops in between the walls.",
            _roomInfo, Field("Floor", _roomFloor)));
        _placeTheme.SelectedIndex = Math.Max(0, FurnitureCatalog.Themes.ToList().IndexOf(_design.Style.Theme));
        _placeList.ItemsSource = PlaceNames();
        _placePanel.Children.Add(Explained("Furniture to put down", "Pick a theme and a room, click a piece, then click on the plan to stand it there. Its dark edge is its front. It's made by the furniture maker, from the furniture's own sheet, and keeps its theme's look in any building.",
            Field("Theme", _placeTheme), Field("Room", _placeRoom), _placeList, _placeInfo, _banana));
        _wayPlacePanel.Children.Add(Explained("Ways up", "Click on the plan to put a way up to the floor above there: an opening is cut in that floor over it. The arrow shows which way it climbs.", Field("Put in", _wayPlace)));
        _itemPanel.Children.Add(Explained("Furniture", "Drag it to move it (hold Shift to place it to the millimetre). R turns it 45° (Shift+R back); the buttons, [ and ], or the mouse wheel while dragging turn it a quarter. Everything the furniture maker can change about it is here too.",
            _iInfo, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { _iLeft, _iRight } },
            _iEdit,
            new StackPanel { Spacing = 6, Children = { _iMore, _iRemove } }));
        _wRiseRows = Stack(Field("Step height", _wRise, tip: "How high each step is (mm). The climb is shared out evenly; solid and spiral stairs use whole layers of the sheet."), Field("Step depth", _wGoing, tip: "How deep each step is, front to back (mm)."));
        _wRailRow = Field("Handrail", _wRail, tip: "A rail on posts along the open side of the stairs, part of the side piece. Left and right as you go up.");
        _wStairRows = Stack(Field("Width", _wStairW, tip: "How wide the stairs are, between the sides (mm)."), _wRailRow);
        _wSpiralRows = Stack(
            Field("Radius", _wSpiralR, tip: "From the pin to the end of each step (mm)."), Field("Turn per step °", _wSpiralStep, tip: "How far round each step is turned from the one under it (degrees)."),
            Field("Centre Ø", _wHub, tip: "The round middle of each step and the spacers between them (mm)."), Field("Pin Ø", _wPin, tip: "The pin they're threaded on (mm): a bamboo skewer is about 3 mm."),
            Field("Turns", _wSpin));
        _wLeanRow = Field("Lean °", _wLean, tip: "How steeply the ladder leans (degrees from the floor).");
        _wLadderRows = Stack(Field("Width", _wLadderW, tip: "Between the rails (mm)."), Field("Rung spacing", _wRung, tip: "Height between rungs (mm)."), _wLeanRow);
        _wayPanel.Children.Add(Explained("Way up", "It climbs to the floor above, through an opening cut in that floor. Drag it to move it; R or the buttons turn it. Its own settings are the stairs maker's.",
            _wInfoWay, Field("Kind", _wayKind), new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { _wLeft, _wRight } },
            _wRiseRows, _wStairRows, _wSpiralRows, _wLadderRows, _wTreads, _wSuit, _wRemove));
        return Stack(_placePanel, _wayPlacePanel, _pickedNone, _picked, _roofPanel, _roomPanel, _wallPanel, _railPanel, _columnPanel, _itemPanel, _wayPanel);
    }

    Control LookTab()
    {
        _wallMmRow = Field("Wall thickness", _wallMm, tip: "A box wall's thickness, outside face to inside face (mm).");
        _tileRow = Field("Ground floor", _tileTexture, tip: "The pattern on what the building stands on. A room can have its own: click inside it with Select.");
        return Stack(
            Explained("Look", "A look for the whole building, the same as the other terrain makers': the walls' texture, the doors and windows dropped from now on, the floor tiles.",
                Field("Theme", _theme),
                Field("Figures", _scale, tip: "Changing it scales the walls, doors and windows to suit figures that size.")),
            Explained("Walls", "Every straight stretch of wall between two columns is one wall: a box (thick, stands on its own) or a flat panel. A column goes at every corner, end and junction.",
                Field("Walls", _build), _wallMmRow,
                Field("Panel thickness", _sheet, tip: "The sheet everything is cut from (mm)."),
                Field("Columns", _column, tip: "How wide each column is (mm)."),
                Field("Column shape", _columnShape, tip: "The columns' shape. A column can have its own: click it with Select."),
                Field("Columns built as", _columnBuild, tip: "Boxes: a side on each flat, locked into slotted plates at the top and bottom, the walls going into their sides (wider columns are needed for octagons and hexagons). Stacked layers on a pin take any angle. Usual: square columns of thick walls are boxes and the rest are stacked."),
                Field("Column texture", _columnTexture, tip: "What's etched on boxed columns' sides."),
                Field("Storey height", _storeyH, tip: "Each floor's walls, rounded to whole layers of the sheet (mm)."),
                Field("Texture", _texture), Field("Upstairs", _upper), Field("Etch as", _etch),
                Field("Wear %", _wear, tip: "How worn the walls' and floors' textures are: 0 is new and crisp, 100 ancient and battered; 50 is each texture's usual look. A wall can have its own: click it with Select."),
                Field("Detail size", _detailSize, tip: "The size of the textures' smallest marks (pits, nicks, grain, bolts) in mm; 0 is each texture's own."),
                Field("Slot fit", _fit, tip: "Gap added to slots so the pieces slide together (mm), on top of the kerf."),
                Field("Layout", _seed, tip: "Stones, planks and grain are laid out at random: each number gives a different layout.")),
            Explained("This floor", "Railings go round the edges of a floor with no wall on them: a balcony, or a terrace on the roof of the rooms below.",
                Field("Yard", _yard, tip: "How far this floor's floor reaches past its own rooms and walls. On the ground floor the base reaches that far all round. Upstairs the floor stops there, leaving the rest of the rooms below open for a roof of their own. As drawn: the ground floor's base covers its rooms and the floor you laid, and an upper floor covers all the rooms below it."),
                _railings),
            Explained("Floors", "What the building stands on, and the patterns etched on its floors. A room can have a pattern of its own: click inside it with Select.",
                Field("Base", _base, tip: "Jigsaw tiles that lock together, one plate (cut along walls only if it's bigger than the bed), or nothing."),
                _tileRow,
                Field("Upper floors", _upperFloor, tip: "The pattern on the floors between storeys.")),
            Explained("Grid", "The plan's squares: walls run along their lines. 25.4 mm (an inch) suits most tabletop games.",
                Field("Square", _square, tip: "One square of the plan (mm).")),
            Explained("Cutting", "Your material's cut settings make slots come out their drawn width (the kerf is allowed for).",
                Field("Cut with", _cut), _cutNote, Field("Etch with", _etchWith),
                _numbers.With(c => ToolTip.SetTip(c, "Each piece's number (the one in the building booklet) scored small and light on it, low down and clear of its texture, on a layer of its own (turn it off to cut without them)."))));
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
        _yard.SelectionChanged += (_, _) =>
        {
            if (_loading) return;
            var level = _design.Levels[_plan.Level];
            var yard = YardSquares[Math.Clamp(_yard.SelectedIndex, 0, YardSquares.Length - 1)];
            if (yard != level.Yard) SetLevel(level with { Yard = yard }, "Yard");
        };
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
        _wTexture.SelectionChanged += (_, _) => WallLookChanged();
        _wBack.SelectionChanged += (_, _) => WallLookChanged();
        _wBackWear.ValueChanged += (_, _) => WallLookChanged();
        _railHere.IsCheckedChanged += (_, _) => RailingChanged();
        _cShape.SelectionChanged += (_, _) => ColumnChanged();
        _cBuild.SelectionChanged += (_, _) => ColumnChanged();
        _cTexture.SelectionChanged += (_, _) => ColumnChanged();
        _placeTheme.SelectionChanged += (_, _) => _placeList.ItemsSource = PlaceNames();
        _placeRoom.SelectionChanged += (_, _) => _placeList.ItemsSource = PlaceNames();
        _placeList.SelectionChanged += (_, _) =>
        {
            int i = _placeList.SelectedIndex;
            if (i >= 0) _plan.BananaToPlace = false;
            else if (_plan.BananaToPlace) return;
            _plan.ToPlace = i >= 0 && i < _placeItems.Count ? _placeItems[i] : null;
            _placeInfo.Text = i >= 0 && i < _placeAbout.Count ? _placeAbout[i] : "";
            if (_plan.ToPlace is not null && _plan.Tool != PlanTool.Furnish) PickTool(PlanTool.Furnish);
        };
        _banana.Click += (_, _) =>
        {
            _plan.BananaToPlace = true;
            _plan.ToPlace = null;
            _placeList.SelectedIndex = -1;
            _placeInfo.Text = $"A banana as big as a real one ({HouseKitMaker.BananaLongMm:0} mm long): click on the plan to lay it down. It shows only in the 3D view and isn't cut.";
            PickTool(PlanTool.Furnish);
        };
        _wayPlace.SelectionChanged += (_, _) => _plan.WayToPlace = (WayKind)Math.Max(0, _wayPlace.SelectedIndex);
        _iLeft.Click += (_, _) => _plan.TurnPicked(1);
        _iRight.Click += (_, _) => _plan.TurnPicked(-1);
        _wLeft.Click += (_, _) => _plan.TurnPicked(1);
        _wRight.Click += (_, _) => _plan.TurnPicked(-1);
        _iEdit.Changed += (_, item) => ItemChanged(item);
        foreach (var n in new[] { _wStairW, _wRise, _wGoing, _wSpiralR, _wSpiralStep, _wHub, _wPin, _wLadderW, _wRung, _wLean }) n.ValueChanged += (_, _) => WayChanged();
        foreach (var c in new[] { _wRail, _wSpin }) c.SelectionChanged += (_, _) => WayChanged();
        _wTreads.IsCheckedChanged += (_, _) => WayChanged();
        _wSuit.Click += (_, _) =>
        {
            if (PickedWay is not { } w) return;
            SetWay(new PlacedWay { X = w.X, Y = w.Y, Turn = w.Turn, Kind = w.Kind }, "Size a way up to suit the figures");
        };
        _iMore.Click += (_, _) =>
        {
            if (PickedItem is not { } it) return;
            _plan.BananaToPlace = it.Banana;
            _plan.ToPlace = it.Banana ? null : it.Item;
            _placeList.SelectedIndex = -1;
            PickTool(PlanTool.Furnish);
        };
        _iRemove.Click += (_, _) =>
        {
            int i = _plan.SelectedItem;
            var level = _design.Levels[_plan.Level];
            if (i < 0 || i >= level.Furniture.Count) return;
            _plan.SelectItem(-1);
            SetLevel(level with { Furniture = [.. level.Furniture.Where((_, j) => j != i)] }, "Take away furniture");
        };
        _wayKind.SelectionChanged += (_, _) =>
        {
            if (_loading || _plan.SelectedWay is var i && (i < 0 || i >= _design.Levels[_plan.Level].WaysUp.Count)) return;
            var level = _design.Levels[_plan.Level];
            var list = level.WaysUp.ToList();
            list[i] = list[i] with { Kind = (WayKind)Math.Max(0, _wayKind.SelectedIndex) };
            SetLevel(level with { WaysUp = list }, "Change a way up");
            if (PickedWay is { } now) ShowWay(now);
        };
        _wRemove.Click += (_, _) =>
        {
            int i = _plan.SelectedWay;
            var level = _design.Levels[_plan.Level];
            if (i < 0 || i >= level.WaysUp.Count) return;
            _plan.SelectWay(-1);
            SetLevel(level with { WaysUp = [.. level.WaysUp.Where((_, j) => j != i)] }, "Take away a way up");
        };
        _cAll.Click += (_, _) => AllColumnsLikePicked();
        _railAll.IsCheckedChanged += (_, _) =>
        {
            if (_loading) return;
            SetLevel(_design.Levels[_plan.Level] with { Railings = _railAll.IsChecked == true }, "Railings");
        };
        foreach (var n in new[] { _wWear, _wDetail }) n.ValueChanged += (_, _) => WallLookChanged();
        _wRest.Click += (_, _) => WallLookChanged(reset: true);
        _wAll.Click += (_, _) => AllWallsLikePicked();
        _level.SelectionChanged += (_, _) => { if (!_loading && _level.SelectedIndex >= 0) ShowLevel(_level.SelectedIndex); };
        _addLevel.Click += (_, _) => AddLevel();
        _copyUp.Click += (_, _) => CopyUp();
        _removeLevel.Click += (_, _) => RemoveLevel();
        _undoButton.Click += (_, _) => Undo();
        _redoButton.Click += (_, _) => Redo();
        _walls.SelectedIndex = (int)WallsShown.CutAway;
        _depth.SelectedIndex = 1;
        _doorsOpen.IsCheckedChanged += (_, _) => _view.SetDoorsOpen(_doorsOpen.IsChecked == true);
        // One switch for every 3D view (Look3D): this box and the bulb button in the view's corner stay in step.
        _realistic.IsChecked = PhotonCanvas.App.View3D.Look3D.Realistic;
        _realistic.IsCheckedChanged += (_, _) => PhotonCanvas.App.View3D.Look3D.Realistic = _realistic.IsChecked == true;
        PhotonCanvas.App.View3D.Look3D.Changed += RealisticChanged;
        Closed += (_, _) => PhotonCanvas.App.View3D.Look3D.Changed -= RealisticChanged;
        void EtchShown()
        {
            bool real = _realEtch.IsChecked == true;
            _depth.IsEnabled = real;
            _view.SetEtchDepth(real ? Depths[Math.Clamp(_depth.SelectedIndex, 0, Depths.Length - 1)] : 0);
        }
        _depth.SelectionChanged += (_, _) => EtchShown();
        _realEtch.IsCheckedChanged += (_, _) => EtchShown();
        _walls.SelectionChanged += (_, _) => _view.SetWalls((WallsShown)Math.Max(0, _walls.SelectedIndex));
        _upTo.SelectionChanged += (_, _) => { if (!_loading) _view.SetUpTo(_upTo.SelectedIndex <= 0 ? int.MaxValue : _upTo.SelectedIndex - 1); };

        foreach (var c in new[] { _oKind, _oShape, _oLook, _oHinge, _oBars, _oFill }) c.SelectionChanged += (_, _) => PickedChanged();
        foreach (var n in new[] { _oW, _oH, _oSill }) n.ValueChanged += (_, _) => PickedChanged();
        _oOpens.IsCheckedChanged += (_, _) => PickedChanged();
        _oAll.Click += (_, _) => AllLikePicked();
        _oRemove.Click += (_, _) =>
        {
            int i = _plan.Selected;
            if (i < 0) return;
            _plan.Select(-1);
            var level = _design.Levels[_plan.Level];
            SetLevel(level with { Openings = [.. level.Openings.Where((_, j) => j != i)] }, "Take away a door or window");
        };

        foreach (var n in new[] { _wallMm, _sheet, _column, _storeyH, _square, _fit, _seed, _wear, _detailSize }) n.ValueChanged += (_, _) => LookChanged();
        foreach (var c in new[] { _texture, _upper, _etch, _tileTexture, _upperFloor, _base, _etchWith, _columnShape, _columnBuild, _columnTexture }) c.SelectionChanged += (_, _) => LookChanged();
        _build.SelectionChanged += (_, _) => BuildChosen();
        _numbers.IsCheckedChanged += (_, _) => LookChanged();
        _theme.SelectionChanged += (_, _) => ThemeChosen();
        _scale.SelectionChanged += (_, _) => ScaleChosen();
        _cut.SelectionChanged += (_, _) => CutChosen();
    }

    void OnKey(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.Z) { if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) Redo(); else Undo(); e.Handled = true; }
        else if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.Y) { Redo(); e.Handled = true; }
        else if (e.Key == Key.R && e.KeyModifiers is KeyModifiers.None or KeyModifiers.Shift && FocusManager?.GetFocusedElement() is not (TextBox or NumericUpDown))
        {
            // R turns the furniture picked (or about to be put down) 45°; Shift+R turns it back.
            _plan.TurnKey(back: e.KeyModifiers == KeyModifiers.Shift);
            e.Handled = true;
        }
        else if (e.KeyModifiers == KeyModifiers.None && FocusManager?.GetFocusedElement() is not (TextBox or NumericUpDown))
        {
            PlanTool? t = e.Key switch { Key.S => PlanTool.Select, Key.W => PlanTool.Wall, Key.E => PlanTool.Erase, Key.D => PlanTool.Door, Key.O => PlanTool.Doorway, Key.N => PlanTool.Window, Key.L => PlanTool.Floor, Key.T => PlanTool.Roof, Key.U => PlanTool.Furnish, Key.A => PlanTool.Stairs, _ => null };
            if (t is { } tool) { PickTool(tool); e.Handled = true; }
        }
    }

    void PickTool(PlanTool t)
    {
        _plan.Tool = t;
        foreach (var (k, b) in _tools) b.IsChecked = k == t;
        ShowPicked();
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
        _yard.SelectedIndex = Math.Max(0, Array.IndexOf(YardSquares, _design.Levels[k].Yard));
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
        var wall = _plan.PickedRun;
        var rail = _plan.PickedRailing;
        var column = _plan.SelectedColumn;
        var item = PickedItem;
        var way = PickedWay;
        _picked.IsVisible = o is not null;
        _roofPanel.IsVisible = roof is not null;
        _roomPanel.IsVisible = room is not null;
        _wallPanel.IsVisible = wall is not null;
        _railPanel.IsVisible = rail is not null;
        _columnPanel.IsVisible = column is not null;
        _itemPanel.IsVisible = item is not null;
        _wayPanel.IsVisible = way is not null;
        _placePanel.IsVisible = _plan.Tool == PlanTool.Furnish;
        _wayPlacePanel.IsVisible = _plan.Tool == PlanTool.Stairs;
        _pickedNone.IsVisible = o is null && roof is null && room is null && wall is null && rail is null && column is null && item is null && way is null
            && _plan.Tool is not (PlanTool.Furnish or PlanTool.Stairs);
        if (item is not null) ShowItem(item);
        if (way is not null) ShowWay(way);
        if (column is { } cp) ShowColumn(cp);
        if (rail is not null) ShowRailing(rail);
        if (roof is not null) ShowRoof(roof);
        if (wall is not null) ShowWall(wall);
        if (room is { } rm) ShowRoom(rm.Room, rm.Plan);
        if (o is null) return;
        _loading = true;
        _oKind.SelectedIndex = (int)o.Kind;
        _oShape.SelectedIndex = Math.Max(0, Array.IndexOf(DoorShapes, o.Shape));
        _oW.Value = (decimal)o.WidthMm; _oH.Value = (decimal)o.HeightMm; _oSill.Value = (decimal)o.SillMm;
        _oOpens.IsChecked = o.Opens;
        _oLook.SelectedIndex = (int)o.Look; _oHinge.SelectedIndex = (int)o.Hinge; _oBars.SelectedIndex = (int)o.Bars; _oFill.SelectedIndex = (int)o.Fill;
        _loading = false;
        PickedRows(o);
    }

    void PickedRows(Opening o)
    {
        bool window = o.Kind == OpeningKind.Window;
        _oSillRow.IsVisible = _oBarsRow.IsVisible = window;
        _oFillRow.IsVisible = window && _design.Style.BoxWalls;
        _oLookRow.IsVisible = o.Kind == OpeningKind.Door;
        _oOpens.IsVisible = o.Kind == OpeningKind.Door;
        _oHingeRow.IsVisible = o.Kind == OpeningKind.Door && o.Opens;
        ToolTip.SetTip(_oOpens, _design.Style.BoxWalls
            ? "The door hangs on pins inside the wall and swings open. An arched or pointed door then has a fixed panel over it, because only a square-topped leaf can swing in a thick wall. Off, it's glued in and fills the whole opening."
            : "The door hangs on two clips slid onto the side of the doorway, and swings outwards. It's cut a little bigger than the doorway, so it covers it. Off, it's glued into the doorway.");
    }

    void PickedChanged()
    {
        if (_loading || Picked is not { } o) return;
        var kind = (OpeningKind)Math.Max(0, _oKind.SelectedIndex);
        var next = o with
        {
            Kind = kind, Shape = DoorShapes[Math.Clamp(_oShape.SelectedIndex, 0, DoorShapes.Length - 1)],
            WidthMm = V(_oW, o.WidthMm), HeightMm = V(_oH, o.HeightMm), SillMm = V(_oSill, o.SillMm),
            Look = (DoorLook)Math.Max(0, _oLook.SelectedIndex), Hinge = (HingeSide)Math.Max(0, _oHinge.SelectedIndex), Opens = _oOpens.IsChecked == true,
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
            var plan = _design.Levels[_plan.Level].Shape();
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
        var plan = _design.Levels[top].Shape();
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

    // ---- a wall picked ----

    /// <summary>The texture a floor's walls have when they've no look of their own.</summary>
    string BuildingTexture(int k) => k > 0 && _design.Style.UpperTexture is { Length: > 0 } up ? up : _design.Style.Texture;

    /// <summary>The squares of wall in a straight wall.</summary>
    static IEnumerable<WallEdge> EdgesOf(Run r) => Enumerable.Range(0, r.Points.Count - 1).Select(i => WallEdge.Of(r.Points[i], r.Points[i + 1]));

    void ShowWall(Run run)
    {
        var level = _design.Levels[_plan.Level];
        var look = level.LookOf(WallEdge.Of(run.Points[0], run.Points[1]));
        _loading = true;
        double mm = HouseKitMaker.Length(run, _design.SquareMm);
        _wInfo.Text = $"{run.Steps} square{(run.Steps == 1 ? "" : "s")} long ({mm:0} mm, column to column) on floor {_plan.Level + 1}. "
            + (look is null ? "It looks like the rest of the building." : "It has a look of its own.");
        _wTexture.SelectedIndex = Math.Max(0, _textures.IndexOf(look?.Texture ?? BuildingTexture(_plan.Level)));
        _wWear.Value = (decimal)Math.Round((look?.Wear ?? _design.Wear) * 100);
        _wDetail.Value = (decimal)Math.Clamp(look?.DetailMm ?? _design.DetailMm, 0, 10);
        _wRest.IsEnabled = look is not null;
        _wBack.SelectedIndex = look?.BackTexture is { } back ? Math.Max(0, _textures.IndexOf(back) + 1) : 0;
        _wBackWear.Value = (decimal)Math.Round((look?.BackTexture is null ? look?.Wear ?? _design.Wear : look.BackWear) * 100);
        _wBackWearRow.IsVisible = _wBack.SelectedIndex > 0;
        _wSides.Text = SidesText(run);
        _loading = false;
    }

    /// <summary>Which way the wall's texture faces, and so which is its other side (the kit turns a wall's texture out of the building).</summary>
    string SidesText(Run run)
    {
        var (left, right) = _design.Levels[_plan.Level].Shape().Sides(run.Points[0], run.Points[1]);
        string single = _design.Style.BoxWalls || _design.Style.TwoSided ? "" : " Its other side is engraved after the cut pieces are turned over.";
        if ((left == -1) != (right == -1)) return "Its texture faces out of the building; the other side is inside." + single;
        int dx = run.Points[1].X - run.Points[0].X, dy = run.Points[1].Y - run.Points[0].Y;
        static string Dir(int x, int y) => string.Join(" and ", new[] { y > 0 ? "down" : y < 0 ? "up" : null, x > 0 ? "right" : x < 0 ? "left" : null }.Where(w => w is not null));
        return $"Its texture faces {Dir(-dy, dx)} the plan; the other side faces {Dir(dy, -dx)}." + single;
    }

    void WallLookChanged(bool reset = false)
    {
        if (_loading || _plan.PickedRun is not { } run) return;
        var level = _design.Levels[_plan.Level];
        var edges = EdgesOf(run).ToHashSet();
        var rest = level.WallLooks.Where(l => !edges.Contains(l.Edge)).ToList();
        if (!reset)
        {
            var look = new WallLook
            {
                Texture = _textures[Math.Clamp(_wTexture.SelectedIndex, 0, _textures.Count - 1)],
                Wear = Math.Clamp(V(_wWear, 50) / 100, 0, 1), DetailMm = Math.Max(0, V(_wDetail, 0)),
                BackTexture = _wBack.SelectedIndex > 0 ? _textures[Math.Clamp(_wBack.SelectedIndex - 1, 0, _textures.Count - 1)] : null,
                BackWear = Math.Clamp(V(_wBackWear, 50) / 100, 0, 1),
            };
            // The same as the rest of the building is no look of its own.
            bool plain = look.Texture == BuildingTexture(_plan.Level) && Math.Abs(look.Wear - _design.Wear) < 1e-9 && Math.Abs(look.DetailMm - _design.DetailMm) < 1e-9
                && look.BackTexture is null;
            if (!plain) rest.AddRange(edges.Select(e => look with { Edge = e }));
        }
        SetLevel(level with { WallLooks = rest }, reset ? "Make a wall like the rest" : "Change a wall's look");
        if (_plan.PickedRun is { } now) ShowWall(now);
    }

    void AllWallsLikePicked()
    {
        if (_plan.PickedRun is not { } run) return;
        var level = _design.Levels[_plan.Level];
        var look = level.LookOf(WallEdge.Of(run.Points[0], run.Points[1]));
        SetLevel(level with { WallLooks = look is null ? [] : [.. level.Walls.Select(e => look with { Edge = e })] }, "Make the walls alike");
    }

    // ---- furniture and ways up ----

    /// <summary>
    /// The furniture that can be put down, sized for the building's figures: the furniture maker's ready-made pieces for the
    /// theme and room picked, or a plain piece of every kind it makes.
    /// </summary>
    List<string> PlaceNames()
    {
        var themes = FurnitureCatalog.Themes;
        string theme = themes[Math.Clamp(_placeTheme.SelectedIndex, 0, themes.Count - 1)];
        int room = Math.Max(0, _placeRoom.SelectedIndex);
        _placeInfo.Text = "";
        if (room > FurnitureCatalog.Rooms.Count)
        {
            _placeItems = [.. Defaults.Kinds.Where(k => k.Kind is not (ItemKind.WallDoor)).Select(k => HouseKitMaker.NewItem(_design.Style, k.Kind))];
            _placeAbout = [.. Defaults.Kinds.Where(k => k.Kind is not (ItemKind.WallDoor)).Select(_ => "A plain one, to change on the Picked tab once it's down.")];
            return [.. _placeItems.Select(HouseKitMaker.ItemName)];
        }
        var entries = FurnitureCatalog.For(theme, room == 0 ? null : FurnitureCatalog.Rooms[room - 1]).ToList();
        _placeItems = [.. entries.Select(e => HouseKitMaker.FromCatalog(_design.Style, e))];
        _placeAbout = [.. entries.Select(e => room == 0 ? $"{e.Room}: {e.About}" : e.About)];
        return [.. entries.Select(e => e.Name)];
    }

    PlacedItem? PickedItem => _plan.SelectedItem >= 0 && _plan.SelectedItem < _design.Levels[_plan.Level].Furniture.Count ? _design.Levels[_plan.Level].Furniture[_plan.SelectedItem] : null;
    PlacedWay? PickedWay => _plan.SelectedWay >= 0 && _plan.SelectedWay < _design.Levels[_plan.Level].WaysUp.Count ? _design.Levels[_plan.Level].WaysUp[_plan.SelectedWay] : null;

    static readonly string[] Facings = ["towards the bottom of the plan", "towards the bottom right", "to the right", "towards the top right", "towards the top", "towards the top left", "to the left", "towards the bottom left"];

    /// <summary>Which way a piece's front faces on the plan, in words.</summary>
    public static string FacingWords(double turn) => Facings[(int)Math.Round(HouseKitMaker.TurnDeg(turn) / 45) % 8];

    void ShowItem(PlacedItem p)
    {
        _loading = true;
        _iEdit.IsVisible = !p.Banana;
        if (p.Banana)
        {
            _iInfo.Text = $"A banana for scale on floor {_plan.Level + 1}: as big as a real one ({HouseKitMaker.BananaLongMm:0} mm long), to see how big the building really is. It shows only in the 3D view and isn't cut.";
            _loading = false;
            return;
        }
        var (w, d) = HouseKitMaker.Footprint(_design.Style, p.Item);
        _iInfo.Text = $"{HouseKitMaker.ItemName(p.Item)} on floor {_plan.Level + 1}, its front {FacingWords(p.Turn)}: it takes {w:0.#} × {d:0.#} mm of floor.";
        _iEdit.Show(p.Item);
        _loading = false;
    }

    void ItemChanged(FurnitureItem item)
    {
        if (_loading || PickedItem is not { } p || p.Banana || item == p.Item) return;
        var level = _design.Levels[_plan.Level];
        var list = level.Furniture.ToList();
        list[_plan.SelectedItem] = p with { Item = item };
        SetLevel(level with { Furniture = list }, "Change furniture");
    }

    void SetWay(PlacedWay w, string what)
    {
        int i = _plan.SelectedWay;
        var level = _design.Levels[_plan.Level];
        if (i < 0 || i >= level.WaysUp.Count || level.WaysUp[i] == w) return;
        var list = level.WaysUp.ToList();
        list[i] = w;
        SetLevel(level with { WaysUp = list }, what);
    }

    /// <summary>The way up picked as its settings say: only what differs from its kind's sizes for the figures is kept on it, so the rest still follows the building.</summary>
    void WayChanged()
    {
        if (_loading || PickedWay is not { } w) return;
        var b = HouseKitMaker.WaySpec(_design.Style, w.Kind);
        double? N(NumericUpDown n, double usual) => n.Value is { } v && Math.Abs((double)v - usual) > 1e-6 ? (double)v : null;
        var rail = (Stairs.Railing)Math.Max(0, _wRail.SelectedIndex);
        bool clockwise = _wSpin.SelectedIndex != 1, treads = _wTreads.IsChecked == true;
        SetWay(w with
        {
            WidthMm = N(_wStairW, b.StairWidthMm), RiseMm = N(_wRise, b.RiseMm), GoingMm = N(_wGoing, b.GoingMm),
            Rail = rail == b.Rail ? null : rail, Treads = treads == b.TreadTexture ? null : treads,
            SpiralRadiusMm = N(_wSpiralR, b.SpiralRadiusMm), SpiralStepDeg = N(_wSpiralStep, b.SpiralStepDeg), SpiralHubMm = N(_wHub, b.SpiralHubMm), SpiralPinMm = N(_wPin, b.SpiralPinMm),
            Clockwise = clockwise == b.Clockwise ? null : clockwise,
            LadderWidthMm = N(_wLadderW, b.LadderWidthMm), RungStepMm = N(_wRung, b.RungStepMm), LadderAngleDeg = N(_wLean, b.LadderAngleDeg),
        }, "Change a way up");
    }

    void ShowWay(PlacedWay w)
    {
        _loading = true;
        var climb = new[] { "to the right", "towards the top", "to the left", "towards the bottom" }[((w.Turn % 4) + 4) % 4];
        _wInfoWay.Text = _plan.Level + 1 >= _design.Levels.Count
            ? $"{HouseKitMaker.WayName(w.Kind)} on the top floor: add a floor above for them to climb to."
            : $"{HouseKitMaker.WayName(w.Kind)} from floor {_plan.Level + 1} up to floor {_plan.Level + 2}, climbing {climb} of the plan.";
        _wayKind.SelectedIndex = (int)w.Kind;
        var spec = HouseKitMaker.WaySpec(_design.Style, w);
        static decimal D(double v, NumericUpDown n) => Math.Clamp((decimal)v, n.Minimum, n.Maximum);
        _wStairW.Value = D(spec.StairWidthMm, _wStairW); _wRise.Value = D(spec.RiseMm, _wRise); _wGoing.Value = D(spec.GoingMm, _wGoing);
        _wRail.SelectedIndex = (int)spec.Rail; _wTreads.IsChecked = spec.TreadTexture;
        _wSpiralR.Value = D(spec.SpiralRadiusMm, _wSpiralR); _wSpiralStep.Value = D(spec.SpiralStepDeg, _wSpiralStep); _wHub.Value = D(spec.SpiralHubMm, _wHub); _wPin.Value = D(spec.SpiralPinMm, _wPin);
        _wSpin.SelectedIndex = spec.Clockwise ? 0 : 1;
        _wLadderW.Value = D(spec.LadderWidthMm, _wLadderW); _wRung.Value = D(spec.RungStepMm, _wRung); _wLean.Value = D(spec.LadderAngleDeg, _wLean);
        bool stairs = w.Kind is WayKind.Stairs or WayKind.SolidStairs, ladder = w.Kind is WayKind.Ladder or WayKind.HookedLadder;
        _wStairRows.IsVisible = stairs;
        _wRailRow.IsVisible = w.Kind == WayKind.Stairs;
        _wRiseRows.IsVisible = _wTreads.IsVisible = !ladder;
        _wSpiralRows.IsVisible = w.Kind == WayKind.Spiral;
        _wLadderRows.IsVisible = ladder;
        _wLeanRow.IsVisible = w.Kind == WayKind.Ladder;
        _wSuit.IsEnabled = w != new PlacedWay { X = w.X, Y = w.Y, Turn = w.Turn, Kind = w.Kind };
        _loading = false;
    }

    // ---- a column picked ----

    void ShowColumn(GridPt at)
    {
        var level = _design.Levels[_plan.Level];
        var plan = level.Shape();
        if (!plan.Joints.TryGetValue(at, out var dirs)) return;
        var made = HouseKitMaker.ColumnShapeAt(level, _design.Style, at, dirs);
        var own = level.ColumnAt(at);
        _loading = true;
        string where = dirs.Count switch { 1 => "the end of a wall", 2 => "a corner", 3 => "a T where three walls meet", _ => $"a junction of {dirs.Count} walls" };
        _cInfo.Text = $"The column at {where} on floor {_plan.Level + 1}: it's made {HouseKitMaker.ShapeName(made).ToLowerInvariant()}"
            + (own is { } o && o != made ? $" ({HouseKitMaker.ShapeName(o).ToLowerInvariant()} doesn't suit its walls' angles)." : ".");
        _cShape.SelectedIndex = own is { } x ? Math.Max(1, Array.IndexOf(ColumnShapes, x)) : 0;
        var pick = level.PickAt(at);
        _cBuild.SelectedIndex = pick?.Build is { } b ? Math.Max(1, Array.IndexOf(Builds, b)) : 0;
        _cTexture.SelectedIndex = pick?.Texture is { } t && _textures.IndexOf(t) is >= 0 and var ti ? ti + 1 : 0;
        bool boxed = HouseKitMaker.BoxedAt(_design, level, at, dirs);
        _cInfo.Text += boxed ? " It's a box." : " It's a stack of layers on a pin.";
        _cTexture.IsEnabled = boxed;
        _loading = false;
    }

    void ColumnChanged()
    {
        if (_loading || _plan.SelectedColumn is not { } at) return;
        var level = _design.Levels[_plan.Level];
        var rest = level.Columns.Where(c => c.At != at).ToList();
        var pick = new ColumnPick
        {
            At = at,
            Shape = _cShape.SelectedIndex > 0 ? ColumnShapes[Math.Clamp(_cShape.SelectedIndex, 1, ColumnShapes.Length - 1)] : null,
            Build = _cBuild.SelectedIndex > 0 ? Builds[Math.Clamp(_cBuild.SelectedIndex, 1, Builds.Length - 1)] : null,
            Texture = _cTexture.SelectedIndex > 0 && _cTexture.SelectedIndex - 1 < _textures.Count ? _textures[_cTexture.SelectedIndex - 1] : null,
        };
        if (!pick.Empty) rest.Add(pick);
        SetLevel(level with { Columns = rest }, "Change a column");
    }

    void AllColumnsLikePicked()
    {
        if (_plan.SelectedColumn is not { } at) return;
        var level = _design.Levels[_plan.Level];
        var own = level.PickAt(at);
        SetLevel(level with { Columns = own is { Empty: false } p ? [.. level.Shape().Joints.Keys.Select(g => p with { At = g })] : [] }, "Make the columns alike");
    }

    // ---- a railing picked ----

    void ShowRailing(RailingRun run)
    {
        var level = _design.Levels[_plan.Level];
        var edges = PlanEditor.EdgesOf(run).ToList();
        _loading = true;
        _railInfo.Text = $"{run.Steps} square{(run.Steps == 1 ? "" : "s")} of floor edge with no wall on it, on floor {_plan.Level + 1}.";
        _railHere.IsChecked = level.Railings && !edges.Any(level.NoRailing.Contains);
        _railHere.IsEnabled = level.Railings;
        _railAll.IsChecked = level.Railings;
        _railings.IsChecked = level.Railings;
        _loading = false;
    }

    void RailingChanged()
    {
        if (_loading || _plan.PickedRailing is not { } run) return;
        var level = _design.Levels[_plan.Level];
        var edges = PlanEditor.EdgesOf(run).ToHashSet();
        var rest = level.NoRailing.Where(e => !edges.Contains(e)).ToList();
        if (_railHere.IsChecked != true) rest.AddRange(edges);
        SetLevel(level with { NoRailing = rest }, _railHere.IsChecked == true ? "Put a railing back" : "Take a railing away");
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
        _columnShape.SelectedIndex = Math.Max(0, Array.IndexOf(ColumnShapes, s.Column));
        _columnBuild.SelectedIndex = Math.Max(0, Array.IndexOf(Builds, _design.ColumnBuild));
        _columnTexture.SelectedIndex = _design.ColumnTexture is { } ct && _textures.IndexOf(ct) is >= 0 and var cti ? cti + 1 : 0;
        _wear.Value = (decimal)Math.Round(Math.Clamp(_design.Wear, 0, 1) * 100);
        _detailSize.Value = (decimal)Math.Clamp(_design.DetailMm, 0, 10);
        _base.SelectedIndex = !s.FloorTiles ? 2 : _design.Base == BaseKind.Plate ? 1 : 0;
        _tileTexture.SelectedIndex = Math.Max(0, PhotonCanvas.Addon.FloorTiles.FloorTextures.Presets.ToList().FindIndex(p => p.Name == s.FloorTexture));
        _upperFloor.SelectedIndex = Math.Max(0, PhotonCanvas.Addon.FloorTiles.FloorTextures.Presets.ToList().FindIndex(p => p.Name == _design.UpperFloor));
        _wallMmRow.IsVisible = s.BoxWalls;
        _tileRow.IsVisible = s.FloorTiles;
        _numbers.IsChecked = _design.NumberPieces;
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
            Column = ColumnShapes[Math.Clamp(_columnShape.SelectedIndex, 0, ColumnShapes.Length - 1)],
            FloorTiles = _base.SelectedIndex != 2, FloorTexture = presets[Math.Clamp(_tileTexture.SelectedIndex, 0, presets.Count - 1)].Name,
        };
        double square = V(_square, 25.4);
        _wallMmRow.IsVisible = style.BoxWalls;
        _tileRow.IsVisible = style.FloorTiles;
        Apply(_design with
        {
            Style = style with { SquareMm = square }, SquareMm = square,
            Base = _base.SelectedIndex == 1 ? BaseKind.Plate : BaseKind.Tiles,
            UpperFloor = presets[Math.Clamp(_upperFloor.SelectedIndex, 0, presets.Count - 1)].Name,
            Wear = Math.Clamp(V(_wear, 50) / 100, 0, 1), DetailMm = Math.Max(0, V(_detailSize, 0)),
            ColumnBuild = Builds[Math.Clamp(_columnBuild.SelectedIndex, 0, Builds.Length - 1)],
            ColumnTexture = _columnTexture.SelectedIndex > 0 && _columnTexture.SelectedIndex - 1 < _textures.Count ? _textures[_columnTexture.SelectedIndex - 1] : null,
            NumberPieces = _numbers.IsChecked == true,
        }, "Change the look");
    }

    void BuildChosen()
    {
        if (_loading) return;
        if (_build.SelectedIndex == 0)
        {
            double need = Math.Ceiling(_design.Style.NarrowestBoxColumn + 1);
            if (V(_column, 24) < need) { _loading = true; _column.Value = Dm(need); _loading = false; }
        }
        else
        {
            // Single-panel walls need far slimmer columns than boxes: the flat building's own.
            double slim = Math.Round(BuildingSpec.FlatPreset(_design.Style.Kind).ColumnMm * _design.Style.ScaleMm / 28);
            if (V(_column, 24) > slim) { _loading = true; _column.Value = Dm(slim); _loading = false; }
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
        // The furniture to put down follows the building's theme (it can still be picked from another).
        if (FurnitureCatalog.Themes.ToList().IndexOf(theme) is >= 0 and var ti) _placeTheme.SelectedIndex = ti;
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
        _placeList.ItemsSource = PlaceNames();
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
        _booklet.IsEnabled = _make.IsEnabled && !_bookleting;
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

    bool _bookleting;

    /// <summary>What the building's called: its design's file name, when it's saved.</summary>
    string BuildingName() => _doc.FilePath is { } saved && Path.GetFileNameWithoutExtension(saved) is { Length: > 0 } n ? n : "Building";

    /// <summary>Where the booklet goes: beside the design when it's saved, else in the data folder's Booklets.</summary>
    string BookletPath()
    {
        var name = string.Concat(BuildingName().Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
        var dir = _doc.FilePath is { } saved && Path.GetDirectoryName(saved) is { Length: > 0 } d ? d : Path.Combine(AppIdentity.DataDir, "Booklets");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, $"{name} building instructions.pdf");
    }

    /// <summary>Writes the building booklet (in the background: a step a page, each with a picture) and opens it.</summary>
    async Task Booklet()
    {
        if (_bookleting) return;
        _bookleting = true;
        _booklet.IsEnabled = false;
        _status.Text = "Making the building booklet…";
        try
        {
            var design = _design;
            double longest = Math.Min(Longest, Math.Min(_machine.FieldW, _machine.FieldH) - 4);
            var built = _kit is not null && _kitFrom == design.ToJson() ? _kit : null;
            var o = new HouseBooklet.Options { Title = BuildingName(), Marked = design.NumberPieces };
            var path = BookletPath();
            int pages = await Task.Run(() =>
            {
                var kit = built ?? HouseKitMaker.Make(design, longest);
                if (!kit.Ok) throw new InvalidOperationException(kit.Findings.First(f => f.Level == Severity.Bad).Text);
                return HouseBooklet.SavePdf(kit, design, o, path);
            });
            _status.Text = $"Building booklet ({pages} pages): {path}";
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true })?.Dispose(); }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { AppLog.Error("building booklet", e); }
        }
        catch (IOException e) { _status.Text = $"Couldn't save the building booklet (is it open in a PDF viewer?): {e.Message}"; }
        catch (InvalidOperationException e) { _status.Text = e.Message; }
        catch (Exception e) when (e is UnauthorizedAccessException or ArgumentException or ArithmeticException or IndexOutOfRangeException or NullReferenceException)
        {
            AppLog.Error("building booklet", e);
            _status.Text = $"Couldn't make the building booklet: {e.Message}";
        }
        finally
        {
            _bookleting = false;
            _booklet.IsEnabled = _kit?.Ok ?? true;
        }
    }
}
