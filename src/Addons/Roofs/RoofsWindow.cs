using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using PhotonCanvas.Addon.Walls;
using PhotonCanvas.App;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Materials;
using static PhotonCanvas.App.UiKit;

namespace PhotonCanvas.Addon.Roofs;

/// <summary>
/// The roof and tower maker. For a roof: its shape and size (or the walls' in the design), the shingles,
/// the gable ends and how it sits on the walls. For a tower: its size, stonework, top, door and windows,
/// and slots for walls. The model is shown put together in 3D and as flat pieces; then the pieces go
/// into the design, nested onto the bed.
/// </summary>
public sealed class RoofsWindow : Window
{
    readonly Document _doc;
    readonly MachineProfile _machine;
    readonly MaterialLibrary? _library;
    readonly string? _dataFolder;
    readonly bool _tower;
    (Guid Id, RoofsDesign Spec)? _editing;

    // Material (both)
    readonly ComboBox _cutWith = Combo(), _etchWith = Combo();
    readonly TextBlock _cutNote = Hint();
    List<(Material M, Recipe R)?> _cuts = [], _etches = [];
    readonly Button _match = UiKit.Button("Match the walls in the design", tip: "Reads the size, sheet, fit and texture of walls made with Walls (the ones selected first, or any in the design).");
    readonly TextBlock _matchNote = Hint().With(h => h.IsVisible = false);

    // Roof
    readonly ComboBox _style = Combo("Gable (two slopes)", "Hipped (four slopes, or a pyramid)", "Lean-to (one slope)", "Cone (for a round tower)");
    readonly NumericUpDown _length = Number(20, 2000, 1, "0.##"), _width = Number(16, 2000, 1, "0.##"), _pitch = Number(10, 80, 5, "0.#"), _eave = Number(0, 50, 0.5m, "0.#"),
        _overhang = Number(0, 50, 0.5m, "0.#"), _thick = Number(0.5m, 20, 0.5m, "0.##"), _count = Number(1, 50, 1, "0");
    readonly TextBlock _lengthLabel = Label("Length"), _widthLabel = Label("Width");
    Control _lengthCell = null!, _overhangRow = null!;
    readonly ComboBox _shingle = Combo([.. Shingles.Presets.Select(p => p.Name)]);
    readonly NumericUpDown _shA = Number(0.5m, 100, 0.5m, "0.##"), _shB = Number(0.5m, 200, 0.5m, "0.##"), _shGroove = Number(0.1m, 3, 0.1m, "0.##"), _seed = Number(1, 9999, 1, "0");
    readonly TextBlock _shALabel = Label("Size"), _shBLabel = Label("Size"), _shAbout = Hint();
    Control _shARow = null!, _shBRow = null!, _shGrooveRow = null!, _seedRow = null!, _flatShingles = null!, _coneRows = null!;
    readonly ComboBox _etch = Combo("Grooves (etched areas, deeper)", "Lines (scored, faster)");
    readonly CheckBox _caps = new() { Content = "Caps over the ridge and hips, with ridge tiles etched on", IsChecked = true };
    readonly ComboBox _coneEdge = Combo("Scalloped (round-ended shingles)", "Notched (slates)", "Smooth");
    readonly NumericUpDown _coneRing = Number(3, 50, 0.5m, "0.#"), _pin = Number(0, 10, 0.1m, "0.#"), _coneShingle = Number(1, 50, 0.5m, "0.#");
    readonly ComboBox _gTex = Combo();
    readonly List<string> _gNames = [];
    readonly NumericUpDown _gA = Number(0.5m, 300, 0.5m, "0.##"), _gB = Number(0.5m, 300, 0.5m, "0.##"), _gGroove = Number(0.1m, 3, 0.1m, "0.##"), _gSeed = Number(1, 9999, 1, "0");
    readonly TextBlock _gALabel = Label("Size"), _gBLabel = Label("Size");
    Control _gARow = null!, _gBRow = null!, _gGrooveRow = null!, _gSeedRow = null!, _gableBody = null!;
    readonly TextBlock _gableNone = Hint("A hipped or cone roof has no gable ends: its slopes come down on every side.");
    CustomTexture? _gCustom;
    readonly ComboBox _gWindow = Combo("No window", "Round", "Round arch", "Square", "Pointed arch");
    readonly NumericUpDown _gWindowW = Number(2, 100, 0.5m, "0.#");
    readonly NumericUpDown _truss = Number(0, 500, 5, "0"), _inset = Number(0, 100, 0.5m, "0.##"), _corner = Number(0, 200, 0.5m, "0.##"), _rest = Number(0, 50, 0.5m, "0.##"),
        _wallT = Number(0.5m, 40, 0.5m, "0.##"), _fit = Number(-1, 2, 0.05m, "0.##");
    Control _trussRow = null!, _cornerRow = null!;

    // Tower
    readonly NumericUpDown _tDia = Number(16, 1000, 1, "0.#"), _tHeight = Number(10, 2000, 1, "0.#"), _tRing = Number(2, 100, 0.5m, "0.#"), _tStoreys = Number(1, 20, 1, "0"), _tCount = Number(1, 50, 1, "0");
    readonly ComboBox _tTex = Combo([.. Rings.Textures]);
    readonly NumericUpDown _tA = Number(1, 300, 0.5m, "0.##"), _tB = Number(0.5m, 100, 0.5m, "0.##"), _tGroove = Number(0.1m, 3, 0.1m, "0.##"), _tSeed = Number(1, 9999, 1, "0");
    readonly TextBlock _tAbout = Hint();
    readonly CheckBox _tPlanks = new() { Content = "Floorboards etched on the floors", IsChecked = true };
    readonly ComboBox _tTop = Combo("Battlements round a deck", "A cone roof", "Open (no top)");
    readonly NumericUpDown _merW = Number(1, 100, 0.5m, "0.#"), _creW = Number(1, 100, 0.5m, "0.#"), _merH = Number(1, 100, 0.5m, "0.#"), _parapet = Number(3, 200, 1, "0.#"),
        _cPitch = Number(30, 80, 5, "0.#"), _cEave = Number(0, 50, 0.5m, "0.#");
    readonly ComboBox _tConeEdge = Combo("Scalloped (round-ended shingles)", "Notched (slates)", "Smooth");
    Control _battleRows = null!, _towerConeRows = null!;
    readonly CheckBox _tDoor = new() { Content = "A doorway at the bottom", IsChecked = true };
    readonly ComboBox _tDoorShape = Combo("Square top", "Round arch", "Pointed arch"), _tWinShape = Combo("Square top", "Round arch", "Pointed arch", "Round", "Arrow slit");
    readonly NumericUpDown _tDoorW = Number(3, 200, 0.5m, "0.#"), _tDoorH = Number(3, 500, 0.5m, "0.#"), _tDoorAt = Number(0, 359, 5, "0"),
        _tWin = Number(0, 12, 1, "0"), _tWinW = Number(1, 100, 0.5m, "0.#"), _tWinH = Number(1, 200, 0.5m, "0.#"), _tSill = Number(0, 200, 0.5m, "0.#");
    Control _doorRows = null!, _winRows = null!;
    readonly ComboBox _join = Combo(["No walls", .. Connectors.Kinds.Select(k => k.Name)]);
    readonly NumericUpDown _joinFirst = Number(0, 359, 5, "0"), _joinAngle = Number(20, 340, 5, "0.#"), _jWall = Number(0.5m, 40, 0.5m, "0.##"), _jHeight = Number(1, 2000, 1, "0.#"),
        _jDepth = Number(0.5m, 50, 0.5m, "0.#"), _jFit = Number(-1, 2, 0.05m, "0.##");
    Control _joinRows = null!;

    readonly Border _tabsHost = new() { Width = 440, Padding = new Thickness(14, 10, 8, 0) };
    readonly TabControl _views = new();
    readonly ModelView _model = new();
    readonly WallPreview _pieces = new();
    readonly TextBlock _summary = new() { TextWrapping = TextWrapping.Wrap };
    readonly StackPanel _findings = new() { Spacing = 4 };
    readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Classes = { "muted" } };
    readonly Button _make = new() { Content = "Put in the design", Classes = { "primary" }, MinWidth = 160 };
    readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(250) };
    bool _loading;
    int _generation;
    RoofKit? _kit;
    string? _kitFrom;

    public RoofsWindow(Document doc, MachineProfile machine, MaterialLibrary? library, string? dataFolder, (Guid Id, RoofsDesign Spec)? editing, bool tower)
    {
        _doc = doc;
        _machine = machine;
        _library = library;
        _dataFolder = dataFolder;
        _editing = editing;
        _tower = tower;
        Title = tower ? "Round towers" : "Roofs";
        Width = 1100;
        Height = 780;
        MinWidth = 840;
        MinHeight = 580;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");
        Library();

        var tabs = new TabControl();
        if (tower)
        {
            tabs.Items.Add(Tab("Tower", TowerTab()));
            tabs.Items.Add(Tab("Stonework", StoneTab()));
            tabs.Items.Add(Tab("Top", TopTab()));
            tabs.Items.Add(Tab("Doors & windows", OpeningsTab()));
            tabs.Items.Add(Tab("Walls", WallsTab()));
        }
        else
        {
            tabs.Items.Add(Tab("Roof", RoofTab()));
            tabs.Items.Add(Tab("Shingles", ShinglesTab()));
            tabs.Items.Add(Tab("Gables", GablesTab()));
            tabs.Items.Add(Tab("Fit", FitTab()));
        }
        _tabsHost.Child = tabs;

        _views.Items.Add(new TabItem { Header = "Put together", Content = Framed(_model) });
        _views.Items.Add(new TabItem { Header = "Pieces", Content = Framed(_pieces) });
        var results = new StackPanel { Spacing = 8, Children = { _summary, _findings } };
        var right = new Grid { RowDefinitions = new("*,Auto"), Margin = new Thickness(0, 8, 14, 0), RowSpacing = 8 };
        right.Children.Add(_views);
        var resultScroll = new ScrollViewer { Content = results, MaxHeight = 190, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
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
        body.Children.Add(_tabsHost);
        Grid.SetColumn(right, 1);
        body.Children.Add(right);
        Grid.SetRow(bottom, 1);
        Grid.SetColumnSpan(bottom, 2);
        body.Children.Add(bottom);
        Content = body;

        _debounce.Tick += (_, _) => { _debounce.Stop(); _ = Rebuild(); };
        _make.Click += (_, _) => Make();
        _match.Click += (_, _) => MatchWalls(quiet: false);
        Hook();
        if (editing?.Spec is { } ed && (tower ? ed.Tower is not null : ed.Roof is not null)) Load(ed);
        else
        {
            Load(LoadLast() ?? (tower ? new RoofsDesign { Tower = new TowerSpec() } : new RoofsDesign { Roof = new RoofSpec() }));
            MatchWalls(quiet: true);
        }
        if (editing is not null) _make.Content = "Update in the design";
        Closed += (_, _) => { _debounce.Stop(); _generation++; SaveLast(); };
        Rebuild().ContinueWith(_ => { }, TaskScheduler.Default);
    }

    // ---- building the tabs ----

    static Control Framed(Control c) => new Border { CornerRadius = new(8), ClipToBounds = true, BorderBrush = Brush("Line"), BorderThickness = new(1), Child = c, Margin = new Thickness(0, 6, 0, 0) };

    static TabItem Tab(string header, Control content) => new()
    {
        Header = header,
        Content = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = new StackPanel { Spacing = 10, Margin = new Thickness(0, 10, 10, 10), Children = { content } } },
    };

    static ComboBox Combo(params string[] items) => new() { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = items.ToList(), SelectedIndex = items.Length > 0 ? 0 : -1 };

    static TextBlock Label(string text) => new() { Text = text, VerticalAlignment = VerticalAlignment.Center, Classes = { "muted" } };

    /// <summary>Like <see cref="UiKit.Field"/>, with a label that can change.</summary>
    static Control Line(TextBlock label, Control editor, string? tip = null)
    {
        var grid = new Grid { ColumnDefinitions = new("110,*") };
        grid.Children.Add(label);
        Grid.SetColumn(editor, 1);
        grid.Children.Add(editor);
        if (tip is not null) { ToolTip.SetTip(label, tip); ToolTip.SetTip(editor, tip); }
        return grid;
    }

    static Control Small(string label, Control editor, string? tip = null)
    {
        var l = Muted(label);
        if (tip is not null) { ToolTip.SetTip(l, tip); ToolTip.SetTip(editor, tip); }
        return Labelled(l, editor);
    }

    static Control SmallL(TextBlock label, Control editor) => Labelled(label, editor);

    static StackPanel Stack(params Control[] c)
    {
        var s = new StackPanel { Spacing = 8 };
        foreach (var x in c) s.Children.Add(x);
        return s;
    }

    Control Shuffle(NumericUpDown seed)
    {
        var b = UiKit.Button("Shuffle");
        b.Click += (_, _) => seed.Value = (seed.Value ?? 1) % 9999 + 1;
        return new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 6, Children = { seed, b.With(x => Grid.SetColumn(x, 1)) } };
    }

    Control MaterialBox() => Explained("Material", "Picking your material's settings brings its thickness and, from your cut test, its kerf and slot fit.",
        Field("Cut with", _cutWith),
        Field("Etch with", _etchWith, tip: "Settings for the etched texture (an engrave/fill recipe from your library)."),
        _cutNote);

    Control RoofTab()
    {
        _lengthCell = SmallL(_lengthLabel, _length);
        _overhangRow = Field("Past the ends", _overhang, tip: "How far the slopes reach past the gable ends (mm).");
        return Stack(
            Explained("Shape", "The roof lifts off the building as one piece. Its size is the outside of what it sits on: for walls from Walls, the outside of the corner columns' caps.",
                Field("Roof", _style),
                Row(_lengthCell, SmallL(_widthLabel, _width)),
                _match, _matchNote,
                Row(Small("Pitch (°)", _pitch, tip: "How steep the slopes are, in degrees from flat: 45 for a cottage, 30 for a barn, 60 for a steep gothic roof."),
                    Small("Eaves", _eave, tip: "How far the slopes reach past the walls at the bottom (mm, along the slope).")),
                _overhangRow,
                Field("Thickness", _thick, tip: "The sheet's thickness (mm)."),
                Field("How many", _count, tip: "How many roofs like this to cut.")),
            MaterialBox());
    }

    Control ShinglesTab()
    {
        _shARow = Line(_shALabel, _shA);
        _shBRow = Line(_shBLabel, _shB);
        _shGrooveRow = Field("Groove", _shGroove, tip: "How wide the etched gaps between shingles are (mm).");
        _seedRow = Field("Variation", Shuffle(_seed), tip: "Shingles of uneven size and thatch are laid out at random: each number gives a different layout.");
        _flatShingles = Stack(Field("Texture", _shingle), _shAbout, _shARow, _shBRow, _shGrooveRow, _seedRow,
            Field("Etch as", _etch, tip: "Grooves etch the gaps as areas (deeper, the most real). Lines score round each shingle (much quicker)."),
            _caps);
        _coneRows = Stack(
            Field("Ring edges", _coneEdge, tip: "A cone is stacked from rings, each a step smaller: their edges are shaped so the steps read as courses of shingles."),
            Field("Shingle width", _coneShingle, tip: "How wide each scallop or slate round the edge is (mm)."),
            Field("Ring width", _coneRing, tip: "How wide each ring is (mm): the cone is hollow until the rings get small."),
            Field("Spire pin Ø", _pin, tip: "A hole down the top for a cocktail stick or wire as the spire's point (mm). 0 = none."));
        return Stack(Explained("Shingles", "Etched on the slopes in courses from the eaves up, each course overlapping the one below.", _flatShingles, _coneRows));
    }

    Control GablesTab()
    {
        _gARow = Line(_gALabel, _gA);
        _gBRow = Line(_gBLabel, _gB);
        _gGrooveRow = Field("Groove", _gGroove);
        _gSeedRow = Field("Variation", Shuffle(_gSeed));
        _gableBody = Stack(
            Field("Texture", _gTex, tip: "One of the wall maker's textures; Match the walls in the design picks the walls' own."),
            _gARow, _gBRow, _gGrooveRow, _gSeedRow,
            Field("Window", _gWindow, tip: "An attic window in each gable."),
            Field("Window width", _gWindowW));
        return Stack(Explained("Gable ends", "The triangles at the ends of a gable or lean-to roof (and a lean-to's back wall), with the wall's texture etched on so they carry on from the walls under them.",
            _gableNone, _gableBody));
    }

    Control FitTab()
    {
        _trussRow = Field("Trusses every", _truss, tip: "Trusses between the gables (or across a hipped roof), about this far apart (mm). 0 = none.");
        _cornerRow = Field("Column corners", _corner, tip: "Square corners the locator keeps clear of, measured in from the outer corner (mm): the walls' columns and caps. 0 = none.");
        return Stack(
            Explained("Frame", "The roof is built on a base plate the size of the building; the gables, trusses and ridge board stand on it with tabs in slots.",
                _trussRow,
                Field("Slot fit", _fit, tip: "Added to the tabs' thickness for their slots (mm): more is looser.")),
            Explained("Sitting on the walls", "A locator under the base drops inside the walls, so the roof lifts off but can't slide. Match the walls in the design fills these in.",
                Field("Walls inside", _inset, tip: "From the outer edge of the roof's base to the inside face of the walls (mm). 0 = no locator."),
                Field("Wall thickness", _wallT, tip: "The walls' thickness (mm): the gables stand over their middle."),
                _cornerRow,
                Field("Rests above walls", _rest, tip: "How far what the roof rests on (the column caps) is above the walls' tops (mm): the locator reaches 2 mm below the tops.")));
    }

    Control TowerTab() => Stack(
        Explained("Size", "The tower is stacked from rings of the sheet, glued in order. Each storey lifts off the one below, so figures can go inside.",
            Row(Small("Diameter", _tDia, tip: "Across the tower (mm)."), Small("Height", _tHeight, tip: "From the ground to the top of the battlements, or to where a cone roof sits (mm).")),
            Field("Thickness", _thick, tip: "The sheet's thickness (mm): each ring is one layer."),
            Field("Ring width", _tRing, tip: "How thick the tower's wall is (mm)."),
            Field("Storeys", _tStoreys, tip: "Storeys that lift apart, each with its own floor. Battlements add a deck on top."),
            Field("How many", _tCount, tip: "How many towers like this to cut.")),
        MaterialBox());

    Control StoneTab() => Stack(
        Explained("Stonework", "A laser can't etch round a tower, so the joints between blocks are notched into each ring's edge, and the layers' edges are the courses.",
            Field("Texture", _tTex), _tAbout,
            Field("Block length", _tA, tip: "How long the blocks, bricks or planks are round the tower (mm)."),
            Field("Course height", _tB, tip: "How tall each course is (mm), rounded to whole layers: 6 mm is two 3 mm rings with the same joints."),
            Field("Groove", _tGroove, tip: "How wide the notches between blocks are (mm)."),
            Field("Variation", Shuffle(_tSeed), tip: "Stone blocks and fieldstone are laid out at random: each number gives a different layout."),
            _tPlanks));

    Control TopTab()
    {
        _battleRows = Stack(
            Row(Small("Merlon width", _merW, tip: "The raised blocks round the top (mm)."), Small("Gap width", _creW, tip: "The gaps between them (mm).")),
            Row(Small("Merlon height", _merH, tip: "How deep the gaps go (mm)."), Small("Parapet", _parapet, tip: "How high the wall round the deck is (mm), merlons and all: about 15 mm hides a 28 mm figure to the chest.")));
        _towerConeRows = Stack(
            Row(Small("Pitch (°)", _cPitch, tip: "How steep the cone is (degrees from flat)."), Small("Eaves", _cEave, tip: "How far the cone reaches past the tower's wall (mm).")),
            Field("Ring edges", _tConeEdge, tip: "The cone's rings get smaller step by step; their edges are shaped so the steps read as courses of shingles."),
            Field("Spire pin Ø", _pin, tip: "A hole down the cone's top for a cocktail stick or wire as the spire's point (mm). 0 = none."));
        return Stack(Explained("The top", "Battlements round a deck for figures to stand on, a cone roof that lifts off, or an open top.",
            Field("Top", _tTop), _battleRows, _towerConeRows));
    }

    Control OpeningsTab()
    {
        _doorRows = Stack(
            Field("Door shape", _tDoorShape),
            Row(Small("Door width", _tDoorW), Small("Door height", _tDoorH)),
            Field("Faces (°)", _tDoorAt, tip: "Which way the door faces, in degrees round the tower seen from above (270 = towards you in the preview)."));
        _winRows = Stack(
            Field("Window shape", _tWinShape),
            Row(Small("Window width", _tWinW), Small("Window height", _tWinH)),
            Field("Sill height", _tSill, tip: "How high the bottom of the windows is above each storey's floor (mm)."));
        return Stack(
            Explained("Door", "A doorway through the bottom storey. The rings it passes come as a C, lined up by marks on the floor.", _tDoor, _doorRows),
            Explained("Windows", "Spread evenly round each storey, clear of the door and the walls, turned a little from one storey to the next.",
                Field("Per storey", _tWin), _winRows));
    }

    Control WallsTab()
    {
        _joinRows = Stack(
            Field("First wall at (°)", _joinFirst, tip: "Which way the first wall leaves the tower, in degrees seen from above."),
            Field("Your angle (°)", _joinAngle, tip: "The angle between the two walls for \"Your angle\"."),
            Row(Small("Wall thickness", _jWall, tip: "Both panels, if the walls are two-sided (mm)."), Small("Wall height", _jHeight, tip: "The slots go this high (mm).")),
            Row(Small("Slot depth", _jDepth, tip: "How far the walls go into the tower (mm): the same as the walls' slot depth, so their smooth ends fit."),
                Small("Slot fit", _jFit, tip: "Added to the wall's thickness for the slots (mm): more is looser.")));
        return Stack(Explained("Walls", "Slots up the outside of the tower that walls from Walls slide into, like its columns: a corner tower takes two walls at 90°.",
            Field("Walls", _join), _joinRows, _match, _matchNote));
    }

    // ---- the library ----

    void Library()
    {
        var mats = _library?.Materials ?? [];
        _cuts = [null, .. mats.SelectMany(m => m.Recipes.Where(r => r.Settings.Mode == LayerMode.Cut).Select(r => ((Material, Recipe)?)(m, r)))];
        _etches = [null, .. mats.SelectMany(m => m.Recipes.Where(r => r.Settings.Mode == LayerMode.Fill).Select(r => ((Material, Recipe)?)(m, r)))];
        _cutWith.ItemsSource = _cuts.Select(c => c is { } x ? $"{x.M.Label} · {x.R.Title}" + (x.R.Settings.KerfMm > 0 ? $" (kerf {x.R.Settings.KerfMm:0.###} mm)" : "") : "As set on the cut layer").ToList();
        _etchWith.ItemsSource = _etches.Select(c => c is { } x ? $"{x.M.Label} · {x.R.Title}" : "As set on the etch layer").ToList();
        _cutWith.SelectedIndex = 0;
        _etchWith.SelectedIndex = 0;
    }

    (Material M, Recipe R)? CutRecipe => _cutWith.SelectedIndex > 0 && _cutWith.SelectedIndex < _cuts.Count ? _cuts[_cutWith.SelectedIndex] : null;
    (Material M, Recipe R)? EtchRecipe => _etchWith.SelectedIndex > 0 && _etchWith.SelectedIndex < _etches.Count ? _etches[_etchWith.SelectedIndex] : null;

    void CutChosen()
    {
        if (!_loading && CutRecipe is { } c)
        {
            _loading = true;
            if (c.M.ThicknessMm is { } t) _thick.Value = (decimal)Math.Clamp(t, 0.5, 20);
            if (c.R.SlotFitMm is { } fit) { _fit.Value = (decimal)Math.Clamp(fit, -1, 2); _jFit.Value = (decimal)Math.Clamp(fit, -1, 2); }
            int same = _etches.FindIndex(e => e is { } x && x.M.Id == c.M.Id);
            if (same > 0 && EtchRecipe is null) _etchWith.SelectedIndex = same;
            _loading = false;
        }
        _cutNote.Text = CutRecipe is not { } r
            ? "No kerf allowance: slots and rings come out a little loose. Pick your material's cut settings (Laser → Tests → Cut test measures the kerf) for parts at their drawn size."
            : r.R.Settings.KerfMm > 0 ? $"{r.M.Label}: kerf {r.R.Settings.KerfMm:0.###} mm, allowed for on the cut layer."
            : $"{r.M.Label}: no kerf measured yet, so slots come out a little loose (run a cut test).";
        Changed();
    }

    // ---- matching the walls ----

    void MatchWalls(bool quiet)
    {
        var walls = WallsFit.Find(_doc.State, _doc.Selection);
        if (walls.Count == 0)
        {
            if (!quiet) Note("There are no walls made with Walls in this design: make them first, or type the sizes in.");
            return;
        }
        var cur = Current();
        if (_tower && cur.Tower is { } t)
        {
            Load(new RoofsDesign { Tower = WallsFit.Fit(t, walls[0]) });
            Note($"Made to take the {walls[0].HeightMm:0.#} mm walls ({WallKitMaker.WallThickness(walls[0]):0.#} mm thick) in the design.");
        }
        else if (cur.Roof is { } r)
        {
            var other = walls.Skip(1).FirstOrDefault(w => Math.Abs(WallsFit.CentreToCentre(w) - WallsFit.CentreToCentre(walls[0])) > 0.5);
            if (r.Style == RoofStyle.Cone) r = r with { Style = RoofStyle.Gable, PitchDeg = 45 };
            var fitted = WallsFit.Fit(r, walls[0], other);
            Load(new RoofsDesign { Roof = fitted });
            Note($"Made to sit on a {fitted.LengthMm:0.#} × {fitted.WidthMm:0.#} mm room of the walls in the design" + (other is null ? " (four walls the same)." : "."));
        }
        Changed();
    }

    void Note(string text)
    {
        _matchNote.Text = text;
        _matchNote.IsVisible = text.Length > 0;
    }

    // ---- settings in and out ----

    void Hook()
    {
        foreach (var n in new[] { _length, _width, _pitch, _eave, _overhang, _thick, _count, _shA, _shB, _shGroove, _seed, _coneRing, _pin, _coneShingle, _gA, _gB, _gGroove, _gSeed, _gWindowW,
                     _truss, _inset, _corner, _rest, _wallT, _fit, _tDia, _tHeight, _tRing, _tStoreys, _tCount, _tA, _tB, _tGroove, _tSeed, _merW, _creW, _merH, _parapet, _cPitch, _cEave,
                     _tDoorW, _tDoorH, _tDoorAt, _tWin, _tWinW, _tWinH, _tSill, _joinFirst, _joinAngle, _jWall, _jHeight, _jDepth, _jFit })
            n.ValueChanged += (_, _) => Changed();
        foreach (var c in new[] { _etch, _coneEdge, _gWindow, _tTop, _tConeEdge, _tDoorShape, _tWinShape, _join, _etchWith, _tTex })
            c.SelectionChanged += (_, _) => Changed();
        foreach (var c in new[] { _caps, _tPlanks, _tDoor })
            c.IsCheckedChanged += (_, _) => Changed();
        _cutWith.SelectionChanged += (_, _) => CutChosen();
        _style.SelectionChanged += (_, _) => StyleChosen();
        _shingle.SelectionChanged += (_, _) => ShingleChosen();
        _gTex.SelectionChanged += (_, _) => GableTextureChosen();
        _tTex.SelectionChanged += (_, _) => { if (!_loading && Textures.Preset(TowerTexture) is { } p && p.SizeALabel is not null) { _loading = true; _tA.Value = (decimal)Math.Max(1, p.SizeA); _tGroove.Value = (decimal)Math.Max(0.1, p.Groove); _loading = false; } ShowRows(); };
    }

    string TowerTexture => _tTex.SelectedIndex >= 0 && _tTex.SelectedIndex < Rings.Textures.Count ? Rings.Textures[_tTex.SelectedIndex] : "Stone blocks";
    string ShingleName => _shingle.SelectedIndex >= 0 && _shingle.SelectedIndex < Shingles.Presets.Count ? Shingles.Presets[_shingle.SelectedIndex].Name : "Slate";
    string GableName => _gTex.SelectedIndex >= 0 && _gTex.SelectedIndex < _gNames.Count ? _gNames[_gTex.SelectedIndex] : "Brick";
    RoofStyle Style => (RoofStyle)Math.Max(0, _style.SelectedIndex);

    void StyleChosen()
    {
        if (_loading) return;
        // A cone wants a steeper pitch; the others a cottage's.
        _loading = true;
        if (Style == RoofStyle.Cone && (_pitch.Value ?? 45) < 50) _pitch.Value = 60;
        else if (Style != RoofStyle.Cone && (_pitch.Value ?? 45) >= 60) _pitch.Value = 45;
        _loading = false;
        ShowRows();
        Changed();
    }

    void ShingleChosen()
    {
        if (_loading) return;
        if (Shingles.Preset(ShingleName) is { } p)
        {
            _loading = true;
            if (p.SizeALabel is not null) _shA.Value = (decimal)p.SizeA;
            if (p.SizeBLabel is not null) _shB.Value = (decimal)p.SizeB;
            if (p.UsesGroove) _shGroove.Value = (decimal)p.Groove;
            _loading = false;
        }
        ShowRows();
        Changed();
    }

    void GableTextureChosen()
    {
        if (_loading) return;
        if (Textures.Preset(GableName) is { } p)
        {
            _loading = true;
            if (p.SizeALabel is not null) _gA.Value = (decimal)p.SizeA;
            if (p.SizeBLabel is not null) _gB.Value = (decimal)p.SizeB;
            if (p.UsesGroove) _gGroove.Value = (decimal)p.Groove;
            _loading = false;
        }
        ShowRows();
        Changed();
    }

    void FillGableTextures(string select, CustomTexture? custom)
    {
        _gCustom = custom is not null && custom.Name == select ? custom : null;
        _gNames.Clear();
        _gNames.AddRange(Textures.Presets.Select(p => p.Name));
        if (_gCustom is not null && !_gNames.Contains(_gCustom.Name)) _gNames.Add(_gCustom.Name);
        bool was = _loading;
        _loading = true;
        _gTex.ItemsSource = _gNames.Select(n => Textures.Preset(n) is null ? $"{n} (the walls' own)" : n).ToList();
        _gTex.SelectedIndex = Math.Max(0, _gNames.IndexOf(select));
        _loading = was;
    }

    void ShowRows()
    {
        if (_tower)
        {
            _battleRows.IsVisible = _tTop.SelectedIndex == 0;
            _towerConeRows.IsVisible = _tTop.SelectedIndex == 1;
            _doorRows.IsVisible = _tDoor.IsChecked == true;
            _winRows.IsVisible = (_tWin.Value ?? 0) > 0;
            _joinRows.IsVisible = _join.SelectedIndex > 0;
            _tAbout.Text = Textures.Preset(TowerTexture)?.About ?? "";
            return;
        }
        var style = Style;
        bool cone = style == RoofStyle.Cone;
        _lengthCell.IsVisible = !cone;
        _widthLabel.Text = cone ? "Tower diameter" : style == RoofStyle.LeanTo ? "Depth (front to back)" : "Width";
        _lengthLabel.Text = style == RoofStyle.LeanTo ? "Length (along the front)" : "Length (along the ridge)";
        _overhangRow.IsVisible = style is RoofStyle.Gable or RoofStyle.LeanTo;
        _flatShingles.IsVisible = !cone;
        _coneRows.IsVisible = cone;
        _caps.IsVisible = style is RoofStyle.Gable or RoofStyle.Hip;
        bool gables = style is RoofStyle.Gable or RoofStyle.LeanTo;
        _gableBody.IsVisible = gables;
        _gableNone.IsVisible = !gables;
        _trussRow.IsVisible = !cone;
        _cornerRow.IsVisible = !cone;
        _match.IsVisible = !cone;
        if (Shingles.Preset(ShingleName) is { } p)
        {
            _shALabel.Text = p.SizeALabel ?? "";
            _shBLabel.Text = p.SizeBLabel ?? "";
            _shARow.IsVisible = p.SizeALabel is not null;
            _shBRow.IsVisible = p.SizeBLabel is not null;
            _shGrooveRow.IsVisible = p.UsesGroove;
            _seedRow.IsVisible = p.Random;
            _shAbout.Text = p.About;
        }
        if (Textures.Preset(GableName) is { } g)
        {
            _gALabel.Text = g.SizeALabel ?? "";
            _gBLabel.Text = g.SizeBLabel ?? "";
            _gARow.IsVisible = g.SizeALabel is not null;
            _gBRow.IsVisible = g.SizeBLabel is not null;
            _gGrooveRow.IsVisible = g.UsesGroove;
            _gSeedRow.IsVisible = g.Random;
        }
        else
        {
            _gALabel.Text = "Tile width";
            _gARow.IsVisible = true;
            _gBRow.IsVisible = _gGrooveRow.IsVisible = _gSeedRow.IsVisible = false;
        }
    }

    static decimal D(double v) => (decimal)v;
    static double V(NumericUpDown n, double fallback) => n.Value is { } v ? (double)v : fallback;

    static int DoorIndex(OpeningShape s) => s switch { OpeningShape.Rectangle => 0, OpeningShape.Pointed => 2, _ => 1 };
    static OpeningShape DoorShape(int i) => i switch { 0 => OpeningShape.Rectangle, 2 => OpeningShape.Pointed, _ => OpeningShape.Arched };

    void Load(RoofsDesign d)
    {
        _loading = true;
        if (d.Roof is { } r)
        {
            _style.SelectedIndex = (int)r.Style;
            _length.Value = D(r.LengthMm); _width.Value = D(r.WidthMm); _pitch.Value = D(r.PitchDeg); _eave.Value = D(r.EaveMm); _overhang.Value = D(r.GableOverhangMm);
            _thick.Value = D(r.ThicknessMm); _count.Value = Math.Max(1, r.Roofs); _fit.Value = D(r.FitMm);
            _shingle.SelectedIndex = Math.Max(0, Shingles.Presets.ToList().FindIndex(p => p.Name == r.Shingle));
            _shA.Value = D(r.ShingleA); _shB.Value = D(r.ShingleB); _shGroove.Value = D(r.ShingleGrooveMm); _seed.Value = Math.Clamp(r.Seed, 1, 9999);
            _etch.SelectedIndex = (int)r.Etch; _caps.IsChecked = r.Caps;
            _coneEdge.SelectedIndex = (int)r.ConeEdge; _coneRing.Value = D(r.ConeRingMm); _pin.Value = D(r.PinMm); _coneShingle.Value = D(Math.Max(1, r.ShingleA));
            _gA.Value = D(r.GableA); _gB.Value = D(r.GableB); _gGroove.Value = D(r.GableGrooveMm); _gSeed.Value = Math.Clamp(r.GableSeed, 1, 9999);
            _gWindow.SelectedIndex = (int)r.Window; _gWindowW.Value = D(r.WindowMm);
            _truss.Value = D(r.TrussSpacingMm); _inset.Value = D(r.InsetMm); _corner.Value = D(r.CornerMm); _rest.Value = D(r.RestAboveWallsMm); _wallT.Value = D(r.WallMm);
            _loading = false;
            FillGableTextures(r.GableTexture, r.GableCustom);
        }
        if (d.Tower is { } t)
        {
            _tDia.Value = D(t.DiameterMm); _tHeight.Value = D(t.HeightMm); _thick.Value = D(t.ThicknessMm); _tRing.Value = D(t.RingMm); _tStoreys.Value = Math.Max(1, t.Storeys); _tCount.Value = Math.Max(1, t.Towers);
            _tTex.SelectedIndex = Math.Max(0, Rings.Textures.ToList().IndexOf(t.Texture));
            _tA.Value = D(t.SizeA); _tB.Value = D(t.SizeB); _tGroove.Value = D(t.GrooveMm); _tSeed.Value = Math.Clamp(t.Seed, 1, 9999); _tPlanks.IsChecked = t.FloorPlanks;
            _tTop.SelectedIndex = (int)t.Top; _merW.Value = D(t.MerlonWidthMm); _creW.Value = D(t.CrenelWidthMm); _merH.Value = D(t.MerlonHeightMm); _parapet.Value = D(t.ParapetMm);
            _cPitch.Value = D(t.ConePitchDeg); _cEave.Value = D(t.ConeEaveMm); _tConeEdge.SelectedIndex = (int)t.ConeEdge; _pin.Value = D(t.PinMm);
            _tDoor.IsChecked = t.Door; _tDoorShape.SelectedIndex = DoorIndex(t.DoorShape); _tDoorW.Value = D(t.DoorWidthMm); _tDoorH.Value = D(t.DoorHeightMm); _tDoorAt.Value = D(((t.DoorAngleDeg % 360) + 360) % 360);
            _tWin.Value = t.WindowsPerStorey; _tWinShape.SelectedIndex = (int)t.WindowShape; _tWinW.Value = D(t.WindowWidthMm); _tWinH.Value = D(t.WindowHeightMm); _tSill.Value = D(t.SillMm);
            _join.SelectedIndex = t.Join is { } j ? 1 + Connectors.Kinds.ToList().FindIndex(k => k.Id == j) : 0;
            _joinFirst.Value = D(((t.JoinFirstDeg % 360) + 360) % 360); _joinAngle.Value = D(t.JoinAngleDeg); _jWall.Value = D(t.WallMm); _jHeight.Value = D(t.WallHeightMm); _jDepth.Value = D(t.SlotDepthMm); _jFit.Value = D(t.FitMm);
            _loading = false;
        }
        _loading = false;
        ShowRows();
        CutChosen();
    }

    RoofsDesign Current()
    {
        if (_tower)
            return new RoofsDesign
            {
                Tower = new TowerSpec
                {
                    DiameterMm = V(_tDia, 60), HeightMm = V(_tHeight, 100), ThicknessMm = V(_thick, 3), RingMm = V(_tRing, 8), Storeys = (int)V(_tStoreys, 2), Towers = (int)V(_tCount, 1),
                    Texture = TowerTexture, SizeA = V(_tA, 12), SizeB = V(_tB, 6), GrooveMm = V(_tGroove, 0.8), Seed = (int)V(_tSeed, 1), FloorPlanks = _tPlanks.IsChecked == true,
                    Top = (TowerTop)Math.Max(0, _tTop.SelectedIndex), MerlonWidthMm = V(_merW, 8), CrenelWidthMm = V(_creW, 6), MerlonHeightMm = V(_merH, 6), ParapetMm = V(_parapet, 15),
                    ConePitchDeg = V(_cPitch, 60), ConeEaveMm = V(_cEave, 4), ConeEdge = (ConeEdge)Math.Max(0, _tConeEdge.SelectedIndex), PinMm = V(_pin, 2),
                    Door = _tDoor.IsChecked == true, DoorShape = DoorShape(_tDoorShape.SelectedIndex), DoorWidthMm = V(_tDoorW, 14), DoorHeightMm = V(_tDoorH, 24), DoorAngleDeg = V(_tDoorAt, 270),
                    WindowsPerStorey = (int)V(_tWin, 2), WindowShape = (OpeningShape)Math.Max(0, _tWinShape.SelectedIndex), WindowWidthMm = V(_tWinW, 3), WindowHeightMm = V(_tWinH, 12), SillMm = V(_tSill, 8),
                    Join = _join.SelectedIndex > 0 ? Connectors.Kinds[_join.SelectedIndex - 1].Id : null,
                    JoinFirstDeg = V(_joinFirst, 0), JoinAngleDeg = V(_joinAngle, 120), WallMm = V(_jWall, 3), WallHeightMm = V(_jHeight, 50), SlotDepthMm = V(_jDepth, 4), FitMm = V(_jFit, 0.15),
                },
            };
        var style = Style;
        return new RoofsDesign
        {
            Roof = new RoofSpec
            {
                Style = style, LengthMm = V(_length, 128.5), WidthMm = V(_width, 128.5), PitchDeg = V(_pitch, 45), EaveMm = V(_eave, 4), GableOverhangMm = V(_overhang, 3),
                ThicknessMm = V(_thick, 3), Roofs = (int)V(_count, 1), FitMm = V(_fit, 0.15),
                Shingle = ShingleName, ShingleA = style == RoofStyle.Cone ? V(_coneShingle, 6) : V(_shA, 6), ShingleB = V(_shB, 4.5), ShingleGrooveMm = V(_shGroove, 0.5), Seed = (int)V(_seed, 1),
                Etch = (EtchStyle)Math.Max(0, _etch.SelectedIndex), Caps = _caps.IsChecked == true,
                ConeEdge = (ConeEdge)Math.Max(0, _coneEdge.SelectedIndex), ConeRingMm = V(_coneRing, 6), PinMm = V(_pin, 2),
                GableTexture = GableName, GableCustom = Textures.Preset(GableName) is null ? _gCustom : null, GableA = V(_gA, 8), GableB = V(_gB, 3.5), GableGrooveMm = V(_gGroove, 0.6), GableSeed = (int)V(_gSeed, 1),
                Window = (GableWindow)Math.Max(0, _gWindow.SelectedIndex), WindowMm = V(_gWindowW, 10),
                TrussSpacingMm = V(_truss, 60), InsetMm = V(_inset, 11.5), CornerMm = V(_corner, 20), RestAboveWallsMm = V(_rest, 4), WallMm = V(_wallT, 3),
            },
        };
    }

    string? LastFile => _dataFolder is null ? null : Path.Combine(_dataFolder, _tower ? "last-tower.json" : "last-roof.json");

    RoofsDesign? LoadLast()
    {
        try { return LastFile is { } f && File.Exists(f) && RoofsDesign.FromJson(File.ReadAllText(f)) is { } d && (_tower ? d.Tower is not null : d.Roof is not null) ? d : null; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    void SaveLast()
    {
        try
        {
            if (LastFile is not { } f) return;
            Directory.CreateDirectory(Path.GetDirectoryName(f)!);
            File.WriteAllText(f, Current().ToJson());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    // ---- preview ----

    void Changed()
    {
        if (_loading) return;
        ShowRows();
        _debounce.Stop();
        _debounce.Start();
    }

    static RoofKit Build(RoofsDesign d) => d.Tower is { } t ? TowerMaker.Make(t) : RoofMaker.Make(d.Roof!);

    async Task Rebuild()
    {
        int gen = ++_generation;
        var spec = Current();
        RoofKit kit;
        try { kit = await Task.Run(() => Build(spec)); }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or ArithmeticException)
        {
            if (gen == _generation) { _model.Show(null, e.Message); _pieces.Show(null, e.Message); }
            return;
        }
        if (gen != _generation) return;
        _kit = kit;
        _kitFrom = spec.ToJson();
        var findings = kit.Findings.Concat(Placement.TooBig(kit, _machine)).ToList();
        string empty = findings.FirstOrDefault(f => f.Level == Severity.Bad)?.Text ?? "Nothing to show.";
        _model.Show(kit.Parts.Count > 0 ? kit : null, empty);
        _pieces.Show(kit.Parts.Count > 0 ? kit.AsWallKit() : null, empty);
        _findings.Children.Clear();
        foreach (var f in findings.OrderByDescending(f => f.Level)) _findings.Children.Add(FindingRow(f));
        int pieces = kit.PieceCount;
        _summary.Text = kit.Parts.Count == 0 ? "" : $"{pieces} piece{(pieces == 1 ? "" : "s")} to cut: " + Summary(kit) + ".";
        _make.IsEnabled = kit.Ok && kit.Parts.Count > 0 && !findings.Any(f => f.Level == Severity.Bad);
    }

    /// <summary>The pieces in words, the tower's numbered rings counted together.</summary>
    static string Summary(RoofKit kit)
    {
        var groups = kit.Parts.GroupBy(p => p.Name.StartsWith("Ring ") ? "ring" : p.Name.StartsWith("Cone ring") ? "cone ring" : p.Name.Contains("(layer") ? "floor" : Lower(p.Name));
        return ListWords(groups.Select(g => { int n = g.Sum(p => p.Copies); return n > 1 ? $"{n} × {Plural(g.Key, g.Count() > 1)}" : g.Key; }));
    }

    static string Plural(string name, bool numbered) => numbered ? name + "s" : name;

    static string Lower(string name) => name.Length > 1 && char.IsLower(name[1]) ? char.ToLowerInvariant(name[0]) + name[1..] : name;

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

    async void Make()
    {
        if (_making) return;
        _making = true;
        _make.IsEnabled = false;
        _status.Text = "Putting the pieces in the design…";
        try
        {
            var spec = Current();
            var built = _kit is not null && _kitFrom == spec.ToJson() ? _kit : null;
            Func<Layer, Layer>? cut = CutRecipe is { } c ? l => c.R.Settings.ApplyTo(l) with { Material = new MaterialRef(c.M.Id, c.M.Label, c.R.Id, c.R.Title) } : null;
            Func<Layer, Layer>? etch = EtchRecipe is { } e ? l => e.R.Settings.ApplyTo(l) with { Material = new MaterialRef(e.M.Id, e.M.Label, e.R.Id, e.R.Title) } : null;
            Guid? replace = _editing is { } ed && Placement.ShapesOf(_doc.State, ed.Id).Count + _doc.State.OtherPages.Sum(p => p.Shapes.Count(x => x.AddonData.TryGetValue(Placement.IdKey, out var v) && v == ed.Id.ToString())) > 0 ? ed.Id : null;
            var before = _doc.State;
            var (kit, s, sheets, id) = await Task.Run(() =>
            {
                var kit = built ?? Build(spec);
                if (!kit.Ok) throw new InvalidOperationException(kit.Findings.First(f => f.Level == Severity.Bad).Text);
                var (s, sheets, id) = Placement.Place(before, kit, spec, _machine, new PlaceOptions { CutSettings = cut, EtchSettings = etch, Replace = replace });
                return (kit, s, sheets, id);
            });
            if (!ReferenceEquals(_doc.State, before)) { _status.Text = "The design changed meanwhile: press again to put the pieces in."; return; }
            string what = _tower ? "tower" : "roof";
            _doc.Commit(replace is null ? (_tower ? "Tower" : "Roof") : $"Update the {what}", s, Placement.ShapesOf(s, id));
            _editing = (id, spec);
            _make.Content = "Update in the design";
            int pieces = kit.PieceCount;
            _status.Text = $"{(replace is null ? "Put" : "Updated")} {pieces} piece{(pieces == 1 ? "" : "s")} in the design" +
                           (sheets > 1 ? $", over {sheets} pages (a bed each)" : "") +
                           $": outlines on \"{Placement.CutLayer}\", the texture on \"{Placement.EtchLayer}\" and \"{Placement.LineLayer}\" (done first).";
        }
        catch (InvalidOperationException ex) { _status.Text = ex.Message; }
        catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException or ArithmeticException or NullReferenceException)
        {
            AppLog.Error("roofs", ex);
            _status.Text = $"Couldn't put the {(_tower ? "tower" : "roof")} in: {ex.Message}";
        }
        finally
        {
            _making = false;
            _make.IsEnabled = _kit?.Ok ?? true;
        }
    }
}
