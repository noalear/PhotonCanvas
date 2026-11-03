using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using PhotonCanvas.App;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Materials;
using static PhotonCanvas.App.UiKit;

namespace PhotonCanvas.Addon.Walls;

/// <summary>
/// The wall maker: the wall's size, its texture (a preset or your own), its top, doors, windows and
/// mount points, and the columns that join walls, with a preview of every piece; then the pieces go
/// into the design, nested onto the bed.
/// </summary>
public sealed class WallsWindow : Window
{
    readonly Document _doc;
    readonly MachineProfile _machine;
    readonly MaterialLibrary? _library;
    readonly string? _dataFolder;
    readonly CustomTextures _own;
    (Guid Id, WallSpec Spec)? _editing;

    // Theme
    readonly ComboBox _theme = Combo([.. WallThemes.Names]);
    readonly TextBlock _themeAbout = Hint();

    // Wall
    readonly NumericUpDown _length = Number(10, 2000, 5, "0.#"), _height = Number(5, 1000, 1, "0.#"), _thick = Number(0.5m, 20, 0.5m, "0.##"), _walls = Number(1, 100, 1, "0");
    readonly ComboBox _measure = Combo("The panel as cut", "Column centre to centre");
    readonly CheckBox _twoSided = new() { Content = "Two panels back to back (texture on both faces)" };
    readonly ComboBox _build = Combo("A box: front, back, top, bottom and ends", "A flat panel");
    readonly NumericUpDown _wallT = Number(1, 200, 0.5m, "0.##"), _plinth = Number(0, 20, 0.5m, "0.#"), _ribSpacing = Number(15, 500, 5, "0");
    readonly CheckBox _intoColumns = new() { Content = "Ends go into columns (tabs into their slots)", IsChecked = true };
    readonly CheckBox _texBack = new() { Content = "Texture the back face too", IsChecked = true }, _texSides = new() { Content = "Texture the ends and the top too" };
    readonly TextBlock _thickLabel = Label("Thickness"), _buildNote = Hint();
    Control _boxRows = null!, _flatRows = null!;
    readonly ComboBox _cutWith = Combo(), _etchWith = Combo();
    readonly TextBlock _cutNote = Hint();
    List<(Material M, Recipe R)?> _cuts = [], _etches = [];

    // Texture
    readonly ComboBox _texture = Combo();
    readonly List<string> _textureNames = [];
    readonly NumericUpDown _sizeA = Number(0.5m, 300, 0.5m, "0.##"), _sizeB = Number(0.5m, 300, 0.5m, "0.##"), _groove = Number(0.1m, 3, 0.1m, "0.##"), _seed = Number(1, 9999, 1, "0");
    readonly TextBlock _sizeALabel = Label("Size"), _sizeBLabel = Label("Size"), _textureAbout = Hint();
    Control _sizeARow = null!, _sizeBRow = null!, _grooveRow = null!, _seedRow = null!;
    // How worn the texture is and how fine its marks (Shared/Terrain/Weathering.cs).
    readonly NumericUpDown _wear = Number(0, 100, 5, "0"), _detail = Number(0.2m, 5, 0.1m, "0.##");
    Control _wearRow = null!, _detailRow = null!;
    readonly ComboBox _etch = Combo("Grooves (etched areas, deeper)", "Lines (scored, faster)");
    readonly CheckBox _plainEnds = new() { Content = "Keep the ends smooth where they go into the columns", IsChecked = true };
    readonly TextBox _ownName = new() { Watermark = "A name for your texture" };
    readonly NumericUpDown _ownGap = Number(0, 100, 0.5m, "0.##"), _ownShift = Number(0, 100, 5, "0");
    readonly Button _ownDelete = UiKit.Button("Delete this texture");
    readonly TextBlock _ownNote = Hint();

    // Top
    readonly ComboBox _top = Combo("Straight", "Battlements (parapet)");
    readonly ComboBox _merlons = Combo("All the same height", "Every other one taller", "Taller at the ends");
    readonly ComboBox _broken = Combo([.. Tops.Styles.Select(t => t.Name)]), _ruinAt = Combo("Anywhere", "Left", "Middle", "Right");
    readonly NumericUpDown _merlonStep = Number(0.5m, 50, 0.5m, "0.#");
    readonly TextBlock _brokenNote = Hint();
    readonly NumericUpDown _merlonW = Number(1, 100, 0.5m, "0.#"), _crenelW = Number(1, 100, 0.5m, "0.#"), _merlonH = Number(1, 100, 0.5m, "0.#"), _walkDepth = Number(4, 60, 1, "0.#"), _ruin = Number(5, 80, 5, "0");
    readonly CheckBox _walkway = new() { Content = "Walkway behind the battlements (a strip on brackets)" };
    Control _battleRows = null!, _ruinRow = null!, _merlonStepRow = null!;

    // Doors and windows
    readonly ComboBox _door = Combo("No door", "Doorway (cut through)", "Closed door (etched)", "Doorway and a door to fit it");
    readonly ComboBox _doorShape = Combo("Square top", "Round arch", "Pointed arch", "Angled corners (sci-fi)", "Rough (cave mouth)");
    readonly ComboBox _doorLook = Combo("Planks", "Planks with iron studs", "Metal blast door", "Bars (a portcullis)", "Stone slab");
    readonly NumericUpDown _doorW = Number(3, 500, 0.5m, "0.#"), _doorH = Number(3, 500, 0.5m, "0.#"), _doorAt = Number(-1000, 1000, 1, "0.#");
    readonly NumericUpDown _windows = Number(0, 50, 1, "0"), _winW = Number(1, 300, 0.5m, "0.#"), _winH = Number(1, 300, 0.5m, "0.#"), _sill = Number(0, 1000, 0.5m, "0.#"), _barW = Number(0.6m, 10, 0.1m, "0.#"), _frame = Number(0, 10, 0.5m, "0.#");
    readonly ComboBox _winShape = Combo("Square top", "Round arch", "Pointed arch", "Round", "Arrow slit", "Angled corners (sci-fi)", "Rough (cave)");
    readonly ComboBox _bars = Combo("No bars", "A cross", "Upright bars");
    readonly CheckBox _doorOpens = new() { Content = "The door opens (it hangs on pins)", IsChecked = true };
    readonly ComboBox _hinge = Combo("Hinged on the left", "Hinged on the right");
    readonly ComboBox _winFill = Combo("Nothing (bars left in the front face)", "A frame with its bars", "A shutter, glued in", "Shutters that open");
    readonly NumericUpDown _insertGroove = Number(0.8m, 2, 0.1m, "0.0");
    Control _doorRows = null!, _winRows = null!, _fillRows = null!, _hingeRow = null!, _grooveInRow = null!, _winFillRow = null!;

    // Mounts
    const int MountsTabIndex = 4;

    // The wall's own mount points, a card for each; the picked one is marked on the preview.
    List<MountPoint> _mountList = [];
    int _mountPicked = -1;
    readonly StackPanel _mountCards = new() { Spacing = 8 };
    readonly List<(Border Card, NumericUpDown X, NumericUpDown Y)> _mountEditors = [];
    readonly ComboBox _accTex = Combo(["None", .. Textures.Presets.Where(p => p.Name != Textures.Plain).Select(p => p.Name)]);
    readonly NumericUpDown _shelfDepth = Number(5, 60, 1, "0.#");

    // Connectors
    readonly Dictionary<ConnectorKind, NumericUpDown> _counts = [];
    readonly NumericUpDown _customAngle = Number(20, 340, 5, "0.#"), _columnW = Number(3, 200, 0.5m, "0.#"), _slotDepth = Number(0.5m, 50, 0.5m, "0.#"), _fit = Number(-1, 2, 0.05m, "0.##"), _pin = Number(0, 10, 0.1m, "0.#");
    readonly ComboBox _column = Combo("Auto (to suit the angles)", "Square", "Octagon", "Round", "Hexagon", "Chamfered square", "Fluted");
    readonly CheckBox _cap = new() { Content = "A wider cap on top", IsChecked = true }, _foot = new() { Content = "A wider foot at the bottom" };
    readonly ComboBox _columnBuild = Combo("Boxes: four sides, a top and a bottom", "Stacks of layers");
    readonly ComboBox _columnTop = Combo("Flush with the sides", "A wider cap", "A stepped cap", "A stepped point", "Battlements");
    readonly ComboBox _columnBase = Combo("Flat", "A wider foot", "A stepped plinth");
    readonly NumericUpDown _columnLip = Number(0, 20, 0.5m, "0.#");
    Control _boxColumnRows = null!, _flatColumnRows = null!;

    readonly Border _tabsHost = new() { Width = 440, Padding = new Thickness(14, 10, 8, 0) };
    readonly WallPreview _preview = new();
    readonly AssembledView _assembled = new();
    readonly CheckBox _openDoors = new() { Content = "Open the doors", IsVisible = false, Margin = new Thickness(10, 6), VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left };
    readonly TabControl _views = new();
    readonly TextBlock _summary = new() { TextWrapping = TextWrapping.Wrap };
    readonly StackPanel _findings = new() { Spacing = 4 };
    readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Classes = { "muted" } };
    readonly Button _make = new() { Content = "Put in the design", Classes = { "primary" }, MinWidth = 160 };
    readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(200) };
    bool _loading;
    int _generation;
    WallKit? _kit;
    WallSpec? _kitFrom;

    public WallsWindow(Document doc, MachineProfile machine, MaterialLibrary? library, string? dataFolder, (Guid Id, WallSpec Spec)? editing)
    {
        _doc = doc;
        _machine = machine;
        _library = library;
        _dataFolder = dataFolder;
        _own = new CustomTextures(dataFolder);
        _editing = editing;
        Title = "Textured walls";
        Width = 1080;
        Height = 760;
        MinWidth = 820;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");

        foreach (var k in Connectors.Kinds) _counts[k.Id] = Number(0, 200, 1, "0");
        Library();

        var tabs = new TabControl
        {
            Items =
            {
                Tab("Wall", WallTab()),
                Tab("Texture", TextureTab()),
                Tab("Top", TopTab()),
                Tab("Doors & windows", OpeningsTab()),
                Tab("Mounts", MountsTab()),
                Tab("Connectors", ConnectorsTab()),
            },
        };
        var left = _tabsHost;
        left.Child = tabs;

        var results = new StackPanel { Spacing = 8, Children = { _summary, _findings } };
        var right = new Grid { RowDefinitions = new("*,Auto"), Margin = new Thickness(0, 14, 14, 0), RowSpacing = 8 };
        static Border Framed(Control c) => new() { CornerRadius = new(8), ClipToBounds = true, BorderBrush = Brush("Line"), BorderThickness = new(1), Child = c };
        _openDoors.IsCheckedChanged += (_, _) => _assembled.Open = _openDoors.IsChecked == true;
        ToolTip.SetTip(_openDoors, "Swing the door and shutters open (and lift a portcullis) to see how they work.");
        _views.Items.Add(new TabItem { Header = "Put together", Content = Framed(new Panel { Children = { _assembled, _openDoors } }) });
        _views.Items.Add(new TabItem { Header = "Pieces", Content = Framed(_preview) });
        right.Children.Add(_views);
        var resultScroll = new ScrollViewer { Content = results, MaxHeight = 200, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
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

        _debounce.Tick += (_, _) => { _debounce.Stop(); _ = Rebuild(); };
        _make.Click += (_, _) => Make();
        Hook();
        _preview.MountPicked += i => { PickMount(i); if (tabs.SelectedIndex != MountsTabIndex) tabs.SelectedIndex = MountsTabIndex; };
        _preview.MountMoved += MountDragged;
        // The mount points are dragged about on the pieces, so that's the view that goes with their tab.
        tabs.SelectionChanged += (_, e) =>
        {
            if (e.Source != tabs) return;
            _preview.FaceOnly = tabs.SelectedIndex == MountsTabIndex;
            if (_preview.FaceOnly) _views.SelectedIndex = 1;
        };
        Load(editing?.Spec ?? LoadLast() ?? WallSpec.NewBoxed);
        if (editing is not null) _make.Content = "Update in the design";
        Closed += (_, _) => { _debounce.Stop(); _generation++; SaveLast(); };
        Rebuild().ContinueWith(_ => { }, TaskScheduler.Default);
    }

    // ---- building the tabs ----

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

    /// <summary>A setting with its name above it (for two side by side).</summary>
    static Control Small(string label, Control editor, string? tip = null)
    {
        var l = Muted(label);
        if (tip is not null) { ToolTip.SetTip(l, tip); ToolTip.SetTip(editor, tip); }
        return Labelled(l, editor);
    }

    static StackPanel Stack(params Control[] c)
    {
        var s = new StackPanel { Spacing = 8 };
        foreach (var x in c) s.Children.Add(x);
        return s;
    }

    Control WallTab() => Stack(
        Explained("Theme", "A look to start from: it sets the texture, the doors and windows, the accessories and the columns to match the floor tiles, roofs, scatter and furniture of the same theme. Change anything afterwards.",
            Field("Theme", _theme),
            _themeAbout),
        Explained("Build", "A box stands up on its own and looks solid from every side: a textured front and back, a base it stands on, a top, two ends and ribs inside. A flat panel is a single sheet (or two back to back) that slides into the columns' slots.",
            Field("Build it as", _build),
            _buildNote),
        Explained("Size", "Panel thickness is your sheet's: the slots, tabs and mount points follow it.",
            Row(Small("Length", _length, tip: "How long the wall is (mm)."), Small("Height", _height, tip: "How high the wall is (mm), to the top of any battlements.")),
            Field("Length is", _measure, tip: "The face as cut, or the distance between the centres of the columns at its ends: handy for laying walls out on a grid."),
            _boxRows = Stack(
                Field("Wall thickness", _wallT, tip: "The whole wall, front face to back face (mm). Two panels plus a whole number of panels between them fits exactly: 12 mm for 3 mm sheet."),
                Field("Plinth", _plinth, tip: "How far the base sticks out past the faces (mm), like a footing course. 0 = flush."),
                Field("Ribs every", _ribSpacing, tip: "Most room between the ribs inside that hold the faces apart (mm)."),
                _intoColumns),
            Line(_thickLabel, _thick, "The sheet's thickness (mm)."),
            _flatRows = Stack(_twoSided),
            Field("How many walls", _walls, tip: "How many walls like this to cut.")),
        Explained("Material", "Picking your material's settings brings its thickness and, from your cut test, its kerf and slot fit.",
            Field("Cut with", _cutWith),
            Field("Etch with", _etchWith, tip: "Settings for the texture (an engrave/fill recipe from your library)."),
            _cutNote));

    Control TextureTab()
    {
        _sizeARow = Line(_sizeALabel, _sizeA);
        _sizeBRow = Line(_sizeBLabel, _sizeB);
        _grooveRow = Field("Groove", _groove, tip: "How wide the etched gaps between bricks, stones or planks are (mm).");
        var shuffle = UiKit.Button("Shuffle");
        shuffle.HorizontalAlignment = HorizontalAlignment.Left;
        shuffle.Click += (_, _) => _seed.Value = (_seed.Value ?? 1) % 9999 + 1;
        _seedRow = Field("Variation", new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 6, Children = { _seed, shuffle.With(b => Grid.SetColumn(b, 1)) } },
            tip: "Stones, blocks and planks are laid out at random: each number gives a different layout.");
        _wearRow = Field("Wear %", _wear, tip: "How weathered the texture looks: 0 is new and crisp, 50 the usual look, 100 ancient and battered (more nicks, chipped corners, pits, cracks and scratches).");
        _detailRow = Field("Detail size", _detail, tip: "Size of the smallest marks in mm: pits, nicks, wood grain, bolts, vent slats and greebles. Bigger marks show up better on a coarse material or a big wall.");
        var fromSelection = UiKit.Button("From the selected shapes", tip: "Select some shapes on the canvas first: they become one tile of the texture.");
        var fromSvg = UiKit.Button("From an SVG file…", tip: "An SVG drawing becomes one tile of the texture, at the size the file gives.");
        fromSelection.Click += (_, _) => AddOwn(null);
        fromSvg.Click += async (_, _) => await AddOwnFromFile();
        _ownDelete.Click += (_, _) => DeleteOwn();
        return Stack(
            Explained("Texture", "Etched on the face of each wall. Pick a preset, or one of your own (they're listed after the presets).",
                Field("Texture", _texture),
                _textureAbout,
                _sizeARow, _sizeBRow, _grooveRow, _seedRow,
                _wearRow, _detailRow,
                Field("Etch as", _etch, tip: "Grooves etch the gaps as areas (deeper, the most real). Lines score round each brick or stone (much quicker)."),
                _plainEnds, _texBack, _texSides),
            Explained("Your own texture", "Shapes you drew (or an SVG) repeated over the face in rows. Closed shapes are etched as areas, open ones as lines. It's kept in the list for next time.",
                Field("Name", _ownName),
                Row(Small("Gap", _ownGap, tip: "Space between one tile and the next (mm)."), Small("Row shift %", _ownShift, tip: "Move every other row along by this much of a tile: 50 for a brick-like offset.")),
                Row(fromSelection, fromSvg),
                _ownDelete,
                _ownNote));
    }

    Control TopTab()
    {
        _battleRows = Stack(
            Row(Small("Merlon width", _merlonW, tip: "The raised blocks along the top (mm)."), Small("Gap width", _crenelW, tip: "The gaps between them (mm).")),
            Field("Merlon height", _merlonH, tip: "How deep the gaps go (mm)."),
            Field("Merlon heights", _merlons, tip: "All the same, every other one taller, or taller at the ends (like towers at the corners)."),
            _merlonStepRow = Field("Shorter by", _merlonStep, tip: "How much lower the shorter merlons are than the tall ones (mm)."),
            _walkway,
            Field("Walkway depth", _walkDepth, tip: "How far the walkway sticks out behind the wall (mm). Its brackets plug into slots in the wall."));
        _ruinRow = Stack(
            Field("Broken down %", _ruin, tip: "How far down the broken part goes, as a share of the height."),
            Field("Where", _ruinAt, tip: "Where the break is deepest. For a fallen end, which end has fallen."));
        return Stack(
            Explained("The top", "A straight top, or battlements: a parapet with gaps, and a walkway behind if you like.",
                Field("Top", _top), _battleRows),
            Explained("Broken top", "Break the top for a ruin, straight or battlemented. On brick and stone the break follows the joints, taking whole stones away; on planks, metal or rock it's a jagged crack. The front and back break differently round the same place, and the ends stay up so they still go into the columns.",
                Field("Broken", _broken), _brokenNote, _ruinRow));
    }

    Control OpeningsTab()
    {
        _doorRows = Stack(
            Field("Door shape", _doorShape),
            Field("Door look", _doorLook, tip: "What's etched on the door: planks, planks with iron studs, a metal blast door, a portcullis (one cut out has its gaps cut through) or a stone slab."),
            Row(Small("Door width", _doorW), Small("Door height", _doorH)),
            Field("Door position", _doorAt, tip: "Where the door's middle is, in mm from the middle of the wall (less than 0 = to the left)."));
        _winRows = Stack(
            Field("Window shape", _winShape),
            Row(Small("Window width", _winW), Small("Window height", _winH)),
            Field("Sill height", _sill, tip: "How high the bottom of the windows is above the bottom of the wall (mm)."),
            Row(Small("Bars", _bars), Small("Bar width", _barW, tip: "How wide the bars left across the window are (mm). Very thin bars break: 1.5 mm or more in plywood.")));
        _hingeRow = Field("Hinges", _hinge, tip: "Which side the door (or a window's single shutter) turns on.");
        _grooveInRow = Field("Groove depth", _insertGroove, tip: "How far the glued-in pieces reach into the groove round the opening (mm). Deeper holds better; the lining round it is 3 mm wide.");
        _fillRows = Explained("Inside the openings", "In a box wall the door, the windows' frames and shutters are cut out too, and sit between the faces. A door that opens hangs on square pins cut on it: the bottom one turns in a hole in the base, the top one in a plate across the doorway inside the wall. Shutters hang the same way, between a sill plate and a head plate. A portcullis slides up inside the wall. Anything that doesn't open is glued into a groove cut in the lining round the opening.",
            _doorOpens, _hingeRow,
            _winFillRow = Field("In the windows", _winFill, tip: "A frame with the bars in it, a solid shutter glued in, or shutters that swing open (a pair when the window is wide enough, one when it's narrow). The front face's window is cut clear when the window is filled."),
            _grooveInRow);
        return Stack(
            Explained("Door", "Cut through, etched on as a closed door, or cut through with a door cut out to fit it, with planks, iron studs, a blast door, a portcullis or a stone slab etched on.",
                Field("Door", _door), _doorRows),
            Explained("Windows", "Spread evenly along the wall, clear of the door and the ends.",
                Field("Windows", _windows), _winRows),
            _fillRows,
            Field("Frame", _frame, tip: "A smooth band round each door and window where the texture stops, edged with a groove (mm). 0 = none."));
    }

    Control MountsTab()
    {
        var add = new Button { Content = "Add a mount point", HorizontalAlignment = HorizontalAlignment.Left };
        add.Click += (_, _) => AddMount();
        return Stack(
            Explained("Mount points",
                "Slots through the wall's face that accessories plug into: pegs for pictures, shields and banners, torch sconces, shelves on brackets and lamps. Add as many as you like, each with its own slots and accessory, and drag them about on the wall in the Pieces view, or type where they go.",
                _mountCards, add),
            Explained("Accessories", "Cut from the same sheet as the wall. A shelf comes with its brackets, their tops through slots in it.",
                Field("Etch them with", _accTex, tip: "A texture etched on the accessories and shelves, smaller than on the wall: wood planks for shelves, metal plates for lamps."),
                Field("Shelf depth", _shelfDepth, tip: "How far shelves and their brackets stick out from the wall (mm).")));
    }

    /// <summary>The mount points' cards made again from the list (after one's added or taken away, or a wall's loaded).</summary>
    void MountCards()
    {
        _mountCards.Children.Clear();
        _mountEditors.Clear();
        if (_mountList.Count == 0) _mountCards.Children.Add(Hint("No mount points yet."));
        for (int i = 0; i < _mountList.Count; i++) _mountCards.Children.Add(MountCard(i));
        PickMount(Math.Min(_mountPicked, _mountList.Count - 1));
    }

    Control MountCard(int i)
    {
        var m = _mountList[i];
        bool was = _loading;
        _loading = true;
        NumericUpDown N(decimal min, decimal max, decimal step, double v, string f = "0.#") { var n = Number(min, max, step, f); n.Value = D(v); return n; }
        var x = N(-500, 5000, 0.5m, m.XMm); var y = N(0, 2000, 0.5m, m.YMm);
        var holes = N(1, 8, 1, m.Count, "0"); var spacing = N(1, 300, 0.5m, m.SpacingMm);
        var across = Combo("One above another", "Side by side"); across.SelectedIndex = m.Across ? 1 : 0;
        var sw = N(0, 30, 0.1m, m.SlotWidthMm, "0.##"); var sh = N(2, 30, 0.5m, m.SlotHeightMm);
        var item = Combo([.. Mounts.Items.Select(t => t.Name)]); item.SelectedIndex = Math.Max(0, Mounts.Items.ToList().FindIndex(t => t.Item == m.Item));
        var side = Combo("The front", "The back (inside)"); side.SelectedIndex = m.OnBack ? 1 : 0;
        _loading = was;
        var remove = new Button { Content = "Remove", HorizontalAlignment = HorizontalAlignment.Right };
        var title = new TextBlock { Text = $"Mount point {i + 1}", FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        var head = new Grid { ColumnDefinitions = new("*,Auto") };
        head.Children.Add(title);
        Grid.SetColumn(remove, 1);
        head.Children.Add(remove);
        var acrossRow = Field("Slots are", across, tip: "One above another (one accessory with a tab in each) or side by side (one each). A shelf's are side by side, a bracket in each.");
        var spacingRow = Row(Small("Slots", holes, tip: "How many slots this mount point has."), Small("Spacing", spacing, tip: "From the middle of one slot to the next (mm)."));
        void Rows()
        {
            int n = (int)V(holes, 1);
            acrossRow.IsVisible = n > 1 && Mounts.Items[Math.Max(0, item.SelectedIndex)].Item != MountItem.Shelf;
        }
        Rows();
        void Update()
        {
            if (_loading || i >= _mountList.Count) return;
            _mountList[i] = new MountPoint
            {
                XMm = V(x, 50), YMm = V(y, 30), Holes = (int)V(holes, 1), SpacingMm = V(spacing, 12), Across = across.SelectedIndex == 1,
                SlotWidthMm = V(sw, 0), SlotHeightMm = V(sh, 6), Item = Mounts.Items[Math.Max(0, item.SelectedIndex)].Item, OnBack = side.SelectedIndex == 1,
            };
            Rows();
            MarkMounts();
            Changed();
        }
        foreach (var n in new[] { x, y, holes, spacing, sw, sh }) n.ValueChanged += (_, _) => Update();
        foreach (var c in new[] { across, item, side }) c.SelectionChanged += (_, _) => Update();
        remove.Click += (_, _) =>
        {
            _mountList.RemoveAt(i);
            if (_mountPicked >= _mountList.Count) _mountPicked = _mountList.Count - 1;
            MountCards();
            MarkMounts();
            Changed();
        };
        var card = new Border
        {
            CornerRadius = new(6), BorderThickness = new(1), BorderBrush = Brush("Line"), Padding = new Thickness(10, 8),
            Child = Stack(head,
                Field("Accessory", item, tip: "What plugs into it. A shelf comes with a bracket in each slot."),
                Field("On", side, tip: "Which face of a box wall it's on: the front, or the back for the inside of a room. A flat wall's slots go right through."),
                Row(Small("Along", x, tip: "The middle of its slots from the wall's left end (mm)."), Small("Up", y, tip: "The middle of its slots above the bottom of the wall (mm).")),
                Row(Small("Slot width", sw, tip: "How wide each slot is (mm). 0 = your sheet's thickness and the slot fit, for the accessories cut here."), Small("Slot height", sh, tip: "How tall each slot is (mm).")),
                spacingRow, acrossRow),
        };
        card.PointerPressed += (_, _) => PickMount(i);
        _mountEditors.Add((card, x, y));
        return card;
    }

    void AddMount()
    {
        double L = V(_length, 100), H = V(_height, 50);
        var last = _mountList.LastOrDefault();
        var m = last is null
            ? new MountPoint { XMm = Math.Round(L / 2 * 2) / 2, YMm = Math.Round(Math.Min(30, H * 0.55) * 2) / 2 }
            : last with { XMm = Math.Round(Math.Min(L - 8, last.XMm + Math.Max(15, last.Bounds(MountSpec()).X1 - last.Bounds(MountSpec()).X0 + 8)) * 2) / 2 };
        _mountList.Add(m);
        _mountPicked = _mountList.Count - 1;
        MountCards();
        MarkMounts();
        Changed();
    }

    WallSpec MountSpec() => new() { ThicknessMm = V(_thick, 3), FitMm = V(_fit, 0.15) };

    void PickMount(int i)
    {
        _mountPicked = i;
        for (int k = 0; k < _mountEditors.Count; k++)
            _mountEditors[k].Card.BorderBrush = Brush(k == i ? "Accent" : "Line");
        if (i >= 0 && i < _mountEditors.Count) _mountEditors[i].Card.BringIntoView();
        MarkMounts();
    }

    /// <summary>The mount points marked on the wall's face in the preview.</summary>
    void MarkMounts()
    {
        var s = MountSpec();
        _preview.ShowMounts([.. _mountList.Select(m => new WallPreview.Mark(m.Slots(s), m.XMm, m.YMm, m.OnBack))], _mountPicked);
    }

    void MountDragged(int i, double x, double y)
    {
        if (i < 0 || i >= _mountEditors.Count) return;
        // Setting both, then making the wall once.
        _loading = true;
        _mountEditors[i].X.Value = D(x);
        _mountEditors[i].Y.Value = D(y);
        _loading = false;
        _mountList[i] = _mountList[i] with { XMm = x, YMm = y };
        MarkMounts();
        Changed();
    }

    Control ConnectorsTab()
    {
        var counts = Stack([.. Connectors.Kinds.Select(k => Field(k.Name, _counts[k.Id]))]);
        return Stack(
            Explained("Connectors", "Columns the walls slide into, stacked from layers of the same sheet. Say how many of each to cut: for a square room, four corners.",
                counts,
                Field("Your angle (°)", _customAngle, tip: "The angle between the two walls for \"Your angle\" columns (degrees)."),
                _boxColumnRows = Stack(
                    Field("Columns are", _columnBuild, tip: "Boxes for straight runs, corners, T junctions, crosses and ends; columns at other angles are always stacks of layers."),
                    Field("Top", _columnTop, tip: "Flush, a wider cap, a stepped cap (wider at the top), a stepped point, or battlements round the top like a turret."),
                    Field("Bottom", _columnBase, tip: "What the column stands on: a flat plate, a wider foot or a stepped plinth. Where a wall comes in, it's notched so the wall reaches the column."),
                    Field("Lip", _columnLip, tip: "How far each step of the cap, foot or plinth sticks out (mm).")),
                Field("Column shape", _column, tip: "Auto is square for straight runs, corners, T and crosses, an octagon for 45° steps and round otherwise."),
                Row(Small("Column width", _columnW, tip: "Across the column's flats (mm)."), Small("Slot depth", _slotDepth, tip: "How far a flat wall goes into the column (mm). The walls' ends are kept smooth that far."))),
            Explained("Fit", "Slots are the sheet's thickness (or a flat wall's) plus the fit. With your material's cut settings picked (Wall tab), the kerf is allowed for too.",
                Row(Small("Slot fit", _fit, tip: "Added to the thickness for the slots (mm): more is looser."), Small("Pin hole Ø", _pin, tip: "A hole up the middle of each layer of a stacked column, for a cocktail stick or wire that lines the stack up (mm). 0 = none.")),
                _flatColumnRows = Stack(_cap, _foot)));
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
            if (c.R.SlotFitMm is { } fit) _fit.Value = (decimal)Math.Clamp(fit, -1, 2);
            // An etch recipe for the same material, if there is one.
            int same = _etches.FindIndex(e => e is { } x && x.M.Id == c.M.Id);
            if (same > 0 && EtchRecipe is null) _etchWith.SelectedIndex = same;
            _loading = false;
        }
        _cutNote.Text = CutRecipe is not { } r
            ? "No kerf allowance: the laser burns each edge back by half its kerf, so slots come out a little loose. Pick your material's cut settings (Laser → Tests → Cut test measures the kerf) for slots at their drawn size."
            : r.R.Settings.KerfMm > 0 ? $"{r.M.Label}: kerf {r.R.Settings.KerfMm:0.###} mm, allowed for on the cut layer so slots come out their drawn width."
            : $"{r.M.Label}: no kerf measured yet, so slots come out a little loose (run a cut test).";
        Changed();
    }

    // ---- settings in and out ----

    void Hook()
    {
        foreach (var n in new[] { _length, _height, _thick, _walls, _wallT, _plinth, _ribSpacing, _columnLip, _sizeA, _sizeB, _groove, _seed, _merlonW, _crenelW, _merlonH, _walkDepth, _ruin, _merlonStep, _shelfDepth, _doorW, _doorH, _doorAt, _windows, _winW, _winH, _sill, _barW, _frame, _insertGroove, _customAngle, _columnW, _slotDepth, _fit, _pin })
            n.ValueChanged += (_, _) => Changed();
        foreach (var n in _counts.Values) n.ValueChanged += (_, _) => Changed();
        foreach (var n in new[] { _wear, _detail }) n.ValueChanged += (_, _) => Changed();
        foreach (var c in new[] { _measure, _etch, _top, _merlons, _broken, _ruinAt, _door, _doorShape, _doorLook, _winShape, _bars, _winFill, _hinge, _accTex, _column, _etchWith, _columnBuild, _columnTop, _columnBase })
            c.SelectionChanged += (_, _) => Changed();
        foreach (var c in new[] { _twoSided, _plainEnds, _walkway, _cap, _foot, _intoColumns, _texBack, _texSides, _doorOpens })
            c.IsCheckedChanged += (_, _) => Changed();
        _cutWith.SelectionChanged += (_, _) => CutChosen();
        _texture.SelectionChanged += (_, _) => TextureChosen();
        _theme.SelectionChanged += (_, _) => ThemeChosen();
        _build.SelectionChanged += (_, _) => BuildChosen();
    }

    bool BoxPicked => _build.SelectedIndex == 0;

    /// <summary>Box or flat picked: a box gets columns wide enough for it.</summary>
    void BuildChosen()
    {
        if (_loading) return;
        if (BoxPicked)
        {
            _loading = true;
            var s = Current();
            double narrowest = BoxColumns.NarrowestBoxed(s);
            if (s.ColumnMm < narrowest) _columnW.Value = (decimal)Math.Ceiling(narrowest + 1);
            _loading = false;
        }
        Changed();
    }

    static readonly OpeningShape[] DoorShapes = [OpeningShape.Rectangle, OpeningShape.Arched, OpeningShape.Pointed, OpeningShape.Chamfered, OpeningShape.Rough];

    string ThemeName => WallThemes.Names[Math.Clamp(_theme.SelectedIndex, 0, WallThemes.Names.Count - 1)];

    /// <summary>A theme picked: its texture, openings and accessories replace the ones set.</summary>
    void ThemeChosen()
    {
        _themeAbout.Text = WallThemes.About(ThemeName);
        if (_loading) return;
        Load(WallThemes.Apply(Current(), ThemeName));
        Changed();
    }

    void FillTextures(string select)
    {
        _textureNames.Clear();
        _textureNames.AddRange(Textures.Presets.Select(p => p.Name));
        _textureNames.AddRange(_own.All.Select(t => t.Name).Where(n => !_textureNames.Contains(n)));
        if (!_textureNames.Contains(select) && _editing?.Spec.Custom is { } c && c.Name == select) _textureNames.Add(select);
        bool was = _loading;
        _loading = true;
        _texture.ItemsSource = _textureNames.Select(n => Textures.Preset(n) is null ? $"{n} (yours)" : n).ToList();
        _texture.SelectedIndex = Math.Max(0, _textureNames.IndexOf(select));
        _loading = was;
        ShowTextureRows();
    }

    string TextureName => _texture.SelectedIndex >= 0 && _texture.SelectedIndex < _textureNames.Count ? _textureNames[_texture.SelectedIndex] : "Brick";

    CustomTexture? OwnTexture => Textures.Preset(TextureName) is null ? _own.Get(TextureName) ?? (_editing?.Spec.Custom is { } c && c.Name == TextureName ? c : null) : null;

    void TextureChosen()
    {
        if (_loading) return;
        _loading = true;
        if (Textures.Preset(TextureName) is { } p)
        {
            if (p.SizeALabel is not null) _sizeA.Value = (decimal)p.SizeA;
            if (p.SizeBLabel is not null) _sizeB.Value = (decimal)p.SizeB;
            if (p.UsesGroove) _groove.Value = (decimal)p.Groove;
            _detail.Value = (decimal)p.Detail;
        }
        else if (OwnTexture is { } own) _sizeA.Value = (decimal)Math.Clamp(own.TileWidthMm, 0.5, 300);
        _loading = false;
        ShowTextureRows();
        Changed();
    }

    void ShowTextureRows()
    {
        if (Textures.Preset(TextureName) is { } p)
        {
            _sizeALabel.Text = p.SizeALabel ?? "";
            _sizeBLabel.Text = p.SizeBLabel ?? "";
            _sizeARow.IsVisible = p.SizeALabel is not null;
            _sizeBRow.IsVisible = p.SizeBLabel is not null;
            _grooveRow.IsVisible = p.UsesGroove;
            _seedRow.IsVisible = p.Random;
            _wearRow.IsVisible = _detailRow.IsVisible = p.Name != Textures.Plain;
            _textureAbout.Text = p.About;
            _ownDelete.IsEnabled = false;
        }
        else
        {
            _sizeALabel.Text = "Tile width";
            _sizeARow.IsVisible = true;
            _sizeBRow.IsVisible = _grooveRow.IsVisible = _seedRow.IsVisible = false;
            _wearRow.IsVisible = _detailRow.IsVisible = false;
            var own = OwnTexture;
            _textureAbout.Text = own is null ? "" : $"Your texture: a {own.TileWidthMm:0.#} × {own.TileHeightMm:0.#} mm tile, scaled to the tile width, repeated in rows.";
            _ownDelete.IsEnabled = _own.Get(TextureName) is not null;
        }
    }

    void ShowRows()
    {
        bool box = BoxPicked;
        _boxRows.IsVisible = _boxColumnRows.IsVisible = box;
        _flatRows.IsVisible = _flatColumnRows.IsVisible = _plainEnds.IsVisible = !box;
        _texBack.IsVisible = _texSides.IsVisible = box;
        _thickLabel.Text = box ? "Panel thickness" : "Thickness";
        _buildNote.Text = box
            ? "Each wall is cut as a front, a back, a base, a top, two ends and ribs; doors and windows are lined between the faces."
            : "One sheet thick (two with panels back to back): the ends slide into the columns' slots.";
        _battleRows.IsVisible = _top.SelectedIndex == 1;
        int broken = Math.Max(0, _broken.SelectedIndex);
        _ruinRow.IsVisible = broken > 0;
        _brokenNote.Text = Tops.Styles[broken].About;
        _merlonStepRow.IsVisible = _merlons.SelectedIndex > 0;
        _doorRows.IsVisible = _door.SelectedIndex > 0;
        _winRows.IsVisible = (_windows.Value ?? 0) > 0;
        bool doorFill = _door.SelectedIndex == (int)DoorKind.DoorwayAndDoor, winFill = _winRows.IsVisible;
        var fill = (WindowFill)Math.Max(0, _winFill.SelectedIndex);
        _fillRows.IsVisible = box && (doorFill || winFill);
        _doorOpens.IsVisible = doorFill;
        _doorOpens.Content = _doorLook.SelectedIndex == (int)DoorLook.Bars ? "The portcullis slides up inside the wall" : "The door opens (it hangs on pins)";
        _hingeRow.IsVisible = doorFill && _doorOpens.IsChecked == true && _doorLook.SelectedIndex != (int)DoorLook.Bars || winFill && fill == WindowFill.Shutters;
        _winFillRow.IsVisible = winFill;
        _grooveInRow.IsVisible = doorFill || winFill && fill != WindowFill.Open;
    }

    static decimal D(double v) => (decimal)v;
    static double V(NumericUpDown n, double fallback) => n.Value is { } v ? (double)v : fallback;

    void Load(WallSpec s)
    {
        _loading = true;
        _length.Value = D(s.LengthMm); _height.Value = D(s.HeightMm); _thick.Value = D(s.ThicknessMm); _walls.Value = s.Walls;
        _measure.SelectedIndex = (int)s.Measure; _twoSided.IsChecked = s.TwoSided;
        _build.SelectedIndex = s.Boxed ? 0 : 1; _wallT.Value = D(s.WallMm); _plinth.Value = D(s.PlinthMm); _ribSpacing.Value = D(s.RibSpacingMm);
        _intoColumns.IsChecked = s.EndsIntoColumns; _texBack.IsChecked = s.TextureBack; _texSides.IsChecked = s.TextureSides;
        _columnBuild.SelectedIndex = (int)s.ColumnBuild; _columnTop.SelectedIndex = (int)s.ColumnTop; _columnBase.SelectedIndex = (int)s.ColumnBase; _columnLip.Value = D(s.ColumnLipMm);
        _sizeA.Value = D(s.SizeA); _sizeB.Value = D(s.SizeB); _groove.Value = D(s.GrooveMm); _seed.Value = Math.Clamp(s.Seed, 1, 9999);
        _wear.Value = D(Math.Clamp(s.Wear, 0, 1) * 100); _detail.Value = D(Math.Clamp(s.DetailMm > 0 ? s.DetailMm : Textures.Preset(s.Texture)?.Detail ?? 0.5, 0.2, 5));
        _etch.SelectedIndex = (int)s.Etch; _plainEnds.IsChecked = s.PlainEnds;
        _top.SelectedIndex = s.Top == TopStyle.Battlements ? 1 : 0;
        _broken.SelectedIndex = Math.Max(0, Tops.Styles.ToList().FindIndex(t => t.Style == s.Breaks)); _ruinAt.SelectedIndex = (int)s.RuinAt;
        _merlons.SelectedIndex = (int)s.Merlons; _merlonStep.Value = D(s.MerlonStepMm);
        _merlonW.Value = D(s.MerlonWidthMm); _crenelW.Value = D(s.CrenelWidthMm); _merlonH.Value = D(s.MerlonHeightMm);
        _walkway.IsChecked = s.Walkway; _walkDepth.Value = D(s.WalkwayDepthMm); _ruin.Value = D(Math.Round(s.RuinDepth * 100));
        _theme.SelectedIndex = Math.Max(0, WallThemes.Names.ToList().IndexOf(s.Theme)); _themeAbout.Text = WallThemes.About(s.Theme);
        _door.SelectedIndex = (int)s.Door; _doorShape.SelectedIndex = Math.Max(0, Array.IndexOf(DoorShapes, s.DoorShape is OpeningShape.Round or OpeningShape.ArrowSlit ? OpeningShape.Arched : s.DoorShape));
        _doorLook.SelectedIndex = (int)s.DoorLook;
        _doorW.Value = D(s.DoorWidthMm); _doorH.Value = D(s.DoorHeightMm); _doorAt.Value = D(s.DoorOffsetMm);
        _windows.Value = s.Windows; _winShape.SelectedIndex = (int)s.WindowShape; _winW.Value = D(s.WindowWidthMm); _winH.Value = D(s.WindowHeightMm); _sill.Value = D(s.SillMm);
        _bars.SelectedIndex = (int)s.Bars; _barW.Value = D(s.BarMm); _frame.Value = D(s.FrameMm);
        _doorOpens.IsChecked = s.DoorOpens; _hinge.SelectedIndex = (int)s.DoorHinge; _winFill.SelectedIndex = (int)s.WindowFill; _insertGroove.Value = D(Math.Clamp(s.InsertGrooveMm, 0.8, 2));
        // A wall saved with only a number of mount points: they're listed where it spread them.
        _mountList = s.MountPoints.Count > 0 ? [.. s.MountPoints] : s.Mounts > 0 ? [.. WallKitMaker.Make(s).MountsUsed] : [];
        _accTex.SelectedIndex = Math.Max(0, _accTex.ItemsSource!.Cast<string>().ToList().IndexOf(s.AccessoryTexture)); _shelfDepth.Value = D(s.ShelfDepthMm);
        foreach (var (k, n) in _counts) n.Value = s.ConnectorCount(k);
        _customAngle.Value = D(s.CustomAngleDeg); _column.SelectedIndex = (int)s.Column; _columnW.Value = D(s.ColumnMm); _slotDepth.Value = D(s.SlotDepthMm);
        _fit.Value = D(s.FitMm); _pin.Value = D(s.PinMm); _cap.IsChecked = s.Cap; _foot.IsChecked = s.Foot;
        _loading = false;
        MountCards();
        FillTextures(s.Texture);
        ShowRows();
        CutChosen();
    }

    WallSpec Current()
    {
        var own = OwnTexture;
        return new WallSpec
        {
            LengthMm = V(_length, 100), Measure = (LengthMeasure)Math.Max(0, _measure.SelectedIndex), HeightMm = V(_height, 50), ThicknessMm = V(_thick, 3),
            TwoSided = _twoSided.IsChecked == true, Walls = (int)V(_walls, 1), Theme = ThemeName,
            Build = BoxPicked ? WallBuild.Box : WallBuild.Flat, WallMm = V(_wallT, 12), PlinthMm = V(_plinth, 1.5), RibSpacingMm = V(_ribSpacing, 45),
            EndsIntoColumns = _intoColumns.IsChecked == true, TextureBack = _texBack.IsChecked == true, TextureSides = _texSides.IsChecked == true,
            ColumnBuild = (ColumnBuild)Math.Max(0, _columnBuild.SelectedIndex), ColumnTop = (ColumnTop)Math.Max(0, _columnTop.SelectedIndex),
            ColumnBase = (ColumnBase)Math.Max(0, _columnBase.SelectedIndex), ColumnLipMm = V(_columnLip, 2),
            Texture = TextureName, Custom = own, SizeA = V(_sizeA, 8), SizeB = V(_sizeB, 3.5), GrooveMm = V(_groove, 0.6), Seed = (int)V(_seed, 1),
            Wear = V(_wear, 50) / 100, DetailMm = V(_detail, 0.5),
            Etch = (EtchStyle)Math.Max(0, _etch.SelectedIndex), PlainEnds = _plainEnds.IsChecked == true,
            Top = _top.SelectedIndex == 1 ? TopStyle.Battlements : TopStyle.Straight,
            Broken = Tops.Styles[Math.Max(0, _broken.SelectedIndex)].Style, RuinAt = (RuinPlace)Math.Max(0, _ruinAt.SelectedIndex),
            Merlons = (MerlonPattern)Math.Max(0, _merlons.SelectedIndex), MerlonStepMm = V(_merlonStep, 3),
            MerlonWidthMm = V(_merlonW, 7), CrenelWidthMm = V(_crenelW, 5), MerlonHeightMm = V(_merlonH, 6),
            Walkway = _walkway.IsChecked == true, WalkwayDepthMm = V(_walkDepth, 10), RuinDepth = V(_ruin, 40) / 100,
            Door = (DoorKind)Math.Max(0, _door.SelectedIndex), DoorShape = DoorShapes[Math.Clamp(_doorShape.SelectedIndex, 0, DoorShapes.Length - 1)], DoorLook = (DoorLook)Math.Max(0, _doorLook.SelectedIndex),
            DoorWidthMm = V(_doorW, 16), DoorHeightMm = V(_doorH, 28), DoorOffsetMm = V(_doorAt, 0),
            Windows = (int)V(_windows, 0), WindowShape = (OpeningShape)Math.Max(0, _winShape.SelectedIndex), WindowWidthMm = V(_winW, 8), WindowHeightMm = V(_winH, 12), SillMm = V(_sill, 25),
            Bars = (WindowBars)Math.Max(0, _bars.SelectedIndex), BarMm = V(_barW, 1.5), FrameMm = V(_frame, 1.5),
            DoorOpens = _doorOpens.IsChecked == true, DoorHinge = (HingeSide)Math.Max(0, _hinge.SelectedIndex), WindowFill = (WindowFill)Math.Max(0, _winFill.SelectedIndex), InsertGrooveMm = V(_insertGroove, 1.2),
            // The count and first slot are kept too, for the other makers that read them.
            MountPoints = [.. _mountList], Mounts = _mountList.Count, MountHeightMm = _mountList.FirstOrDefault()?.YMm ?? 30, MountSlotMm = _mountList.FirstOrDefault()?.SlotHeightMm ?? 6,
            AccessoryTexture = _accTex.SelectedIndex > 0 ? (string)_accTex.SelectedItem! : Textures.Plain, ShelfDepthMm = V(_shelfDepth, 10),
            FitMm = V(_fit, 0.15),
            Connectors = _counts.Where(kv => (kv.Value.Value ?? 0) > 0).ToDictionary(kv => kv.Key, kv => (int)kv.Value.Value!.Value),
            CustomAngleDeg = V(_customAngle, 120), Column = (ColumnShape)Math.Max(0, _column.SelectedIndex), ColumnMm = V(_columnW, 16), SlotDepthMm = V(_slotDepth, 4),
            PinMm = V(_pin, 2), Cap = _cap.IsChecked == true, Foot = _foot.IsChecked == true,
        };
    }

    string? LastFile => _dataFolder is null ? null : Path.Combine(_dataFolder, "last.json");

    WallSpec? LoadLast()
    {
        try { return LastFile is { } f && File.Exists(f) ? WallSpec.FromJson(File.ReadAllText(f)) : null; }
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

    // ---- your own textures ----

    void AddOwn(CustomTexture? made)
    {
        string name = (_ownName.Text ?? "").Trim();
        if (name.Length == 0) { _ownNote.Text = "Type a name for it first."; return; }
        if (Textures.Preset(name) is not null) { _ownNote.Text = "That's the name of a preset: pick another."; return; }
        if (made is null)
        {
            var shapes = _doc.State.Shapes.Where(x => _doc.Selection.Contains(x.Id)).OfType<PathShape>().Select(x => x.World).ToList();
            if (shapes.Count == 0) { _ownNote.Text = "Select some drawn shapes on the canvas first (close this window, select, and open it again)."; return; }
            made = CustomTextures.FromGeometry(name, shapes, V(_ownGap, 0), V(_ownShift, 0) / 100);
            if (made is null) { _ownNote.Text = "Those shapes have no size."; return; }
        }
        try { _own.Put(made); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _ownNote.Text = $"Couldn't save it: {e.Message}"; return; }
        _ownNote.Text = $"\"{made.Name}\" is in the list: a {made.TileWidthMm:0.#} × {made.TileHeightMm:0.#} mm tile.";
        FillTextures(made.Name);
        TextureChosen();
    }

    async Task AddOwnFromFile()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "An SVG drawing for your texture",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("SVG drawings") { Patterns = ["*.svg"] }],
        });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path) return;
        if ((_ownName.Text ?? "").Trim().Length == 0) _ownName.Text = Path.GetFileNameWithoutExtension(path);
        try
        {
            var t = CustomTextures.FromSvg(_ownName.Text!, path, V(_ownGap, 0), V(_ownShift, 0) / 100);
            if (t is null) { _ownNote.Text = "That file has no shapes in it."; return; }
            AddOwn(t);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException or System.Xml.XmlException or InvalidOperationException)
        {
            _ownNote.Text = $"Couldn't read it: {e.Message}";
        }
    }

    void DeleteOwn()
    {
        string name = TextureName;
        if (_own.Get(name) is null) return;
        try { _own.Remove(name); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _ownNote.Text = $"Couldn't delete it: {e.Message}"; return; }
        _ownNote.Text = $"\"{name}\" deleted (walls already made with it keep it).";
        FillTextures("Brick");
        TextureChosen();
    }

    // ---- preview ----

    void Changed()
    {
        if (_loading) return;
        ShowRows();
        _debounce.Stop();
        _debounce.Start();
    }

    async Task Rebuild()
    {
        int gen = ++_generation;
        var spec = Current();
        WallKit kit;
        try { kit = await Task.Run(() => WallKitMaker.Make(spec)); }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or ArithmeticException)
        {
            if (gen == _generation) { _preview.Show(null, e.Message); _assembled.Show(null, spec.ThicknessMm, e.Message); }
            return;
        }
        if (gen != _generation) return;
        _kit = kit;
        _kitFrom = spec;
        var findings = kit.Findings.Concat(Placement.TooBig(kit, _machine)).ToList();
        string? bad = findings.FirstOrDefault(f => f.Level == Severity.Bad)?.Text;
        _preview.Show(kit.Parts.Count > 0 ? kit : null, bad ?? "Nothing to show.");
        _assembled.Show(kit.Parts.Count > 0 ? kit : null, spec.ThicknessMm, bad ?? "Nothing to show.");
        _openDoors.IsVisible = _assembled.HasMoving;
        _findings.Children.Clear();
        foreach (var f in findings.OrderByDescending(f => f.Level)) _findings.Children.Add(FindingRow(f));
        int pieces = kit.PieceCount;
        _summary.Text = kit.Parts.Count == 0 ? "" :
            $"{pieces} piece{(pieces == 1 ? "" : "s")} to cut: " + ListWords(kit.Parts.Select(p => p.Copies > 1 ? $"{p.Copies} × {Lower(p.Name)}" : Lower(p.Name))) + ".";
        _make.IsEnabled = kit.Ok && kit.Parts.Count > 0 && !findings.Any(f => f.Level == Severity.Bad);
    }

    /// <summary>"Corner column layer" → "corner column layer", but "T column layer" stays.</summary>
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
            var built = _kit is not null && _kitFrom?.ToJson() == spec.ToJson() ? _kit : null;
            Func<Layer, Layer>? cut = CutRecipe is { } c ? l => c.R.Settings.ApplyTo(l) with { Material = new MaterialRef(c.M.Id, c.M.Label, c.R.Id, c.R.Title) } : null;
            Func<Layer, Layer>? etch = EtchRecipe is { } e ? l => e.R.Settings.ApplyTo(l) with { Material = new MaterialRef(e.M.Id, e.M.Label, e.R.Id, e.R.Title) } : null;
            Guid? replace = _editing is { } ed && Placement.ShapesOf(_doc.State, ed.Id).Count + _doc.State.OtherPages.Sum(p => p.Shapes.Count(x => x.AddonData.TryGetValue(Placement.IdKey, out var v) && v == ed.Id.ToString())) > 0 ? ed.Id : null;
            var before = _doc.State;
            var (kit, s, sheets, id) = await Task.Run(() =>
            {
                var kit = built ?? WallKitMaker.Make(spec);
                if (!kit.Ok) throw new InvalidOperationException(kit.Findings.First(f => f.Level == Severity.Bad).Text);
                var (s, sheets, id) = Placement.Place(before, kit, spec, _machine, new PlaceOptions { CutSettings = cut, EtchSettings = etch, Replace = replace });
                return (kit, s, sheets, id);
            });
            if (!ReferenceEquals(_doc.State, before)) { _status.Text = "The design changed meanwhile: press again to put the pieces in."; return; }
            _doc.Commit(replace is null ? "Walls" : "Update the walls", s, Placement.ShapesOf(s, id));
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
            AppLog.Error("walls", ex);
            _status.Text = $"Couldn't put the walls in: {ex.Message}";
        }
        finally
        {
            _making = false;
            _make.IsEnabled = _kit?.Ok ?? true;
        }
    }
}
