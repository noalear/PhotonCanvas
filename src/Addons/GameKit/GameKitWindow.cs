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

namespace PhotonCanvas.Addon.GameKit;

/// <summary>
/// The game accessories maker: a list of what to cut (each with how many), the chosen one's size and
/// settings, and the sheet it's all cut from; the chosen one put together in 3D and its pieces flat;
/// then every piece goes into the design, nested onto the bed. Built like the furniture maker's window.
/// </summary>
public sealed class GameKitWindow : Window
{
    readonly Document _doc;
    readonly MachineProfile _machine;
    readonly MaterialLibrary? _library;
    readonly string? _dataFolder;
    (Guid Id, AccessorySet Spec)? _editing;
    AccessorySet _set;
    int _sel;

    // The list
    readonly ListBox _list = new() { MinHeight = 110, MaxHeight = 170 };
    readonly ComboBox _addKind = Combo([.. Defaults.Kinds.Select(k => k.Name)]);
    readonly Button _add = UiKit.Button("Add"), _copy = UiKit.Button("Copy"), _remove = UiKit.Button("Remove");

    // The chosen accessory
    readonly TextBlock _title = new() { FontWeight = FontWeight.SemiBold }, _about = Hint();
    readonly NumericUpDown _count = Number(1, 200, 1, "0");
    readonly NumericUpDown _w = Number(1, 2000, 1, "0.#"), _d = Number(1, 2000, 1, "0.#"), _h = Number(1, 2000, 1, "0.#");
    readonly TextBlock _wLabel = Label("Width"), _dLabel = Label("Depth"), _hLabel = Label("Height");
    Control _wRow = null!, _dRow = null!, _hRow = null!;
    // Dice tower
    readonly NumericUpDown _tray = Number(5, 1000, 1, "0.#"), _rim = Number(3, 500, 1, "0.#"), _baffles = Number(1, 12, 1, "0"), _die = Number(5, 100, 1, "0.#");
    readonly CheckBox _battlements = new() { Content = "Battlements round the top" };
    // Card stand
    readonly NumericUpDown _tiers = Number(1, 6, 1, "0"), _slot = Number(0.5m, 20, 0.5m, "0.#"), _ribs = Number(2, 12, 1, "0");
    // Deck tray
    readonly NumericUpDown _cardW = Number(15, 500, 1, "0.#"), _cardH = Number(15, 500, 1, "0.#"), _piles = Number(1, 8, 1, "0");
    readonly ComboBox _cardSize = Combo("Your own size", "Poker (63 × 88)", "Bridge (57 × 89)", "Mini (41 × 63)", "Tarot (70 × 120)");
    static readonly (double W, double H)[] CardSizes = [(0, 0), (63, 88), (57, 89), (41, 63), (70, 120)];
    // Initiative tracker
    readonly NumericUpDown _tiles = Number(1, 40, 1, "0"), _tileW = Number(8, 200, 1, "0.#"), _tileH = Number(10, 200, 1, "0.#");
    readonly ComboBox _tileTop = Combo("Rounded corners", "Arched", "Square");
    readonly TextBox _names = new() { AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, MinHeight = 90, MaxHeight = 160, Watermark = "One name a line (blank: a number)" };
    // Gauges, rulers, templates
    readonly ComboBox _units = Combo([.. Defaults.UnitNames]), _shape = Combo("Circle", "Cone", "Square");
    readonly NumericUpDown _segments = Number(2, 40, 1, "0"), _step = Number(0.25m, 20, 0.25m, "0.##"), _length = Number(0.25m, 100, 0.5m, "0.##");
    readonly TextBlock _lengthLabel = Label("Length");
    Control _towerRows = null!, _standRows = null!, _trayRows = null!, _initRows = null!, _unitsRow = null!, _gaugeRows = null!, _lengthRow = null!, _shapeRow = null!;
    // A unit of your own
    readonly NumericUpDown _unitMm = Number(1, 1000, 0.5m, "0.##"), _unitCounts = Number(0.01m, 1000, 1, "0.##");
    readonly TextBox _unitName = new() { Watermark = "MU, hexes, paces…" };
    Control _ownUnitRows = null!;

    // Markings (gauges, rulers, templates)
    readonly TextBlock _marksFor = new() { FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
    readonly TextBlock _marksNone = Hint("Pick a movement gauge, range ruler or area template on the Accessories tab (or add one) to set how it's marked.");
    Control _marksBody = null!;
    readonly MarkGallery _designs = new(), _borders = new(), _ornaments = new();
    readonly TextBlock _designAbout = Hint();
    readonly ComboBox _markFont = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    List<string> _markFonts = [];
    readonly CheckBox _bold = new() { Content = "Bold" }, _italic = new() { Content = "Italic" }, _showUnit = new() { Content = "Write the unit along the bottom" };
    readonly ComboBox _look = Combo("Engraved (filled in)", "Outlined (scored round)", "Single lines (one pass, fastest)");
    readonly ComboBox _numbering = Combo("1  2  3", "1\"  2\"  3\"  (with the unit)", "I  II  III  (Roman)", "Dice pips (up to 9)", "Tally marks (up to 25)", "No numbers");
    readonly NumericUpDown _every = Number(1, 10, 1, "0"), _textScale = Number(50, 150, 5, "0");
    readonly TextBox _markTitle = new() { Watermark = "MOVE, CHARGE, FIREBALL, your army's name…" };
    readonly ComboBox _place = Combo("At the ends", "Each side of the title", "With every number");
    readonly ComboBox _ticks = Combo();
    int[] _tickParts = [0];
    readonly ComboBox _borderArt = Combo();
    readonly Button _upload = UiKit.Button("Upload a design…"), _removeArt = UiKit.Button("Remove from the list"), _allMarks = UiKit.Button("Use these on every gauge, ruler and template");
    readonly TextBlock _artNote = Hint();
    Control _ticksRow = null!, _borderArtRow = null!, _everyRow = null!;
    readonly ArtStore? _artStore;
    List<MarkArt> _arts = [];

    // The sheet
    readonly NumericUpDown _thick = Number(0.5m, 12, 0.5m, "0.##"), _fit = Number(-0.5m, 2, 0.05m, "0.##"), _seed = Number(1, 9999, 1, "0"), _scaleTex = Number(0.25m, 10, 0.25m, "0.##");
    readonly ComboBox _finish = Combo([.. Defaults.FinishNames]);
    readonly ComboBox _theme = Combo([.. GameKitThemes.Names]);
    readonly TextBlock _themeAbout = Hint();
    readonly ComboBox _font = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    List<string> _fonts = [];
    readonly ComboBox _cutWith = Combo(), _etchWith = Combo();
    readonly TextBlock _cutNote = Hint();
    List<(Material M, Recipe R)?> _cuts = [], _etches = [];

    readonly TabControl _views = new();
    readonly PiecesView _pieces = new();
    readonly AssembledView _assembled = new();
    readonly TextBlock _summary = new() { TextWrapping = TextWrapping.Wrap };
    readonly StackPanel _findings = new() { Spacing = 4 };
    readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Classes = { "muted" } };
    readonly Button _make = new() { Content = "Put in the design", Classes = { "primary" }, MinWidth = 160 };
    readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(250) };
    bool _loading;
    int _generation;
    SetKit? _kit;
    string? _kitFrom;

    public GameKitWindow(Document doc, MachineProfile machine, MaterialLibrary? library, string? dataFolder, (Guid Id, AccessorySet Spec)? editing)
    {
        _doc = doc;
        _machine = machine;
        _library = library;
        _dataFolder = dataFolder;
        _editing = editing;
        HandFonts.Folder = HandFonts.FolderBeside(dataFolder);
        _artStore = dataFolder is null ? null : new ArtStore(Path.Combine(dataFolder, "designs"));
        _set = editing?.Spec ?? LoadLast() ?? new AccessorySet();
        if (_set.Items.Count == 0) _set = _set with { Items = [Defaults.For(AccessoryKind.DiceTower)] };
        Title = "Game accessories";
        Width = 1100;
        Height = 780;
        MinWidth = 860;
        MinHeight = 580;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");

        Library();
        Fonts();
        _arts = _artStore?.All() ?? [];
        var tabs = new TabControl { Items = { Tab("Accessories", ItemsTab()), Tab("Markings", MarksTab()), Tab("Sheet", SheetTab()) } };
        var left = new Border { Width = 450, Padding = new Thickness(14, 10, 8, 0), Child = tabs };

        _views.Items.Add(new TabItem { Header = "Put together", Content = Framed(_assembled) });
        _views.Items.Add(new TabItem { Header = "Pieces", Content = Framed(_pieces) });
        var results = new StackPanel { Spacing = 8, Children = { _summary, _findings } };
        var right = new Grid { RowDefinitions = new("*,Auto"), Margin = new Thickness(0, 6, 14, 0), RowSpacing = 8 };
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
        _shownFont = _set.Font;
        RefreshArts();
        LoadSheet();
        FillList(0);
        if (editing is not null) _make.Content = "Update in the design";
        Closed += (_, _) => { _debounce.Stop(); _generation++; SaveLast(); };
        Rebuild().ContinueWith(_ => { }, TaskScheduler.Default);
    }

    /// <summary>The longest piece the bed takes, with room round it.</summary>
    double MaxLength => Math.Max(_machine.FieldW, _machine.FieldH) - 6;

    static Border Framed(Control c) => new() { CornerRadius = new(8), ClipToBounds = true, BorderBrush = Brush("Line"), BorderThickness = new(1), Margin = new(0, 6, 0, 0), Child = c };

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

    static StackPanel Stack(params Control[] c)
    {
        var s = new StackPanel { Spacing = 8 };
        foreach (var x in c) s.Children.Add(x);
        return s;
    }

    Control ItemsTab()
    {
        var addRow = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 6, Children = { _addKind, _add.With(b => { Grid.SetColumn(b, 1); b.MinWidth = 70; }) } };
        ToolTip.SetTip(_addKind, "Pick an accessory and press Add to put it on the list.");
        _wRow = Line(_wLabel, _w);
        _dRow = Line(_dLabel, _d);
        _hRow = Line(_hLabel, _h);
        _towerRows = Stack(
            Field("Tray length", _tray, tip: "How far the tray reaches out in front of the tower (mm)."),
            Field("Tray sides", _rim, tip: "How high the tray's sides are (mm)."),
            Field("Ramps", _baffles, tip: "Ramps inside the tower that tumble the dice; the bottom one sends them out of the gate."),
            Field("Biggest die", _die, tip: "The biggest die it has to pass (mm across): sets the gaps between the ramps and the gate's size. A d20 is about 20 to 22 mm."),
            _battlements);
        _standRows = Stack(
            Field("Rows", _tiers, tip: "Rows of slots, each behind and above the one in front."),
            Field("Slot width", _slot, tip: "How wide the card slots are (mm): a big hand wants more."),
            Field("Ribs", _ribs, tip: "Upright ribs the slots are cut in, spread along the stand."));
        _trayRows = Stack(
            Field("Cards", _cardSize, tip: "Pick a common card size, or type your own below."),
            Field("Card width", _cardW, tip: "The cards' short side (mm)."),
            Field("Card length", _cardH, tip: "The cards' long side (mm)."),
            Field("Piles", _piles, tip: "Piles side by side (a draw pile and a discard pile…), with a divider between each."));
        _initRows = Stack(
            Field("Tiles", _tiles, tip: "One tile for each player and monster."),
            Field("Tile width", _tileW, tip: "How wide each tile is (mm)."),
            Field("Tile height", _tileH, tip: "How tall each tile is (mm), the part in the groove included."),
            Field("Tile tops", _tileTop),
            Field("Names", _names, tip: "Etched on the tiles in order, one a line. Tiles without a name get a number."));
        _unitsRow = Field("Marked in", _units, tip: "Inches for most wargames; grid squares count 5 ft an inch, for role-playing games; or a unit of your own.");
        _ownUnitRows = Stack(
            Field("Unit length", _unitMm, tip: "How long one of your units is (mm): 25.4 is an inch, 31.75 a 1¼ inch hex."),
            Field("Counts as", _unitCounts, tip: "What each unit adds to the numbers: 1 counts 1, 2, 3…; 5 counts 5, 10, 15 (feet on a grid)."),
            Field("Called", _unitName, tip: "Its name, written after the numbers (with the unit) and on the ruler."));
        _gaugeRows = Stack(
            Field("Segments", _segments, tip: "How many segments; they're joined by pins, so the gauge bends."),
            Field("Each segment", _step, tip: "How long each segment is, in the units above."));
        _lengthRow = Line(_lengthLabel, _length);
        _shapeRow = Field("Shape", _shape, tip: "A circle (blasts, spheres), a cone (as wide as it is far) or a square (cubes).");
        return Stack(
            Explained("Accessories to cut", "Everything on the list is cut from the same sheet and nested onto the bed together. Pick one to change it.",
                _list,
                addRow,
                Row(_copy, _remove)),
            Explained("This accessory", "",
                _title, _about,
                Field("How many", _count),
                _shapeRow, _unitsRow, _ownUnitRows, _wRow, _dRow, _hRow, _lengthRow,
                _towerRows, _standRows, _trayRows, _initRows, _gaugeRows));
    }

    Control SheetTab()
    {
        var shuffle = UiKit.Button("Shuffle");
        shuffle.HorizontalAlignment = HorizontalAlignment.Left;
        shuffle.Click += (_, _) => _seed.Value = (_seed.Value ?? 1) % 9999 + 1;
        return Stack(
            Explained("Sheet", "Everything is cut from one sheet: its thickness sets every slot and tab. 3 mm plywood suits towers and trays.",
                Field("Thickness", _thick, tip: "The sheet's thickness (mm)."),
                Field("Slot fit", _fit, tip: "Added to the sheet's thickness for the slots (mm): more is looser. 0.1 is a snug fit to glue.")),
            Explained("Theme", "A look to start from, the same as the wall maker's: it sets the finish, the dice towers' battlements and the initiative tiles' tops to match the scenery of that theme.",
                Field("Theme", _theme), _themeAbout),
            Explained("Finish", "Etched on the faces that show: the tower's walls, the tray's sides, the stand's front rail and the top of the tracker's base.",
                Field("Faces", _finish, tip: "Wood like the furniture maker's, or one of the wall maker's textures."),
                Field("Texture size", _scaleTex, tip: "The texture's stones, bricks or planks are the wall maker's sizes times this."),
                Field("Variation", new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 6, Children = { _seed, shuffle.With(b => Grid.SetColumn(b, 1)) } },
                    tip: "Stones, planks and grain are laid out at random: each number gives a different layout."),
                Field("Font", _font, tip: "For the numbers and names.")),
            Explained("Material", "Picking your material's settings brings its thickness and, from your cut test, its kerf and slot fit.",
                Field("Cut with", _cutWith),
                Field("Etch with", _etchWith, tip: "Settings for the texture, numbers and names (an engrave/fill recipe from your library)."),
                _cutNote));
    }

    Control MarksTab()
    {
        _everyRow = Field("Number every", _every, tip: "A number on every mark (1), every second (2), every fifth (5)…; the last always has one.");
        _ticksRow = Field("Finest ticks", _ticks, tip: "The ruler's smallest ticks.");
        _borderArtRow = Field("Design", _borderArt, tip: "Which of your designs is repeated along the border (upload one under Ornament).");
        _upload.HorizontalAlignment = HorizontalAlignment.Left;
        _allMarks.HorizontalAlignment = HorizontalAlignment.Left;
        ToolTip.SetTip(_upload, "A drawing (SVG, DXF, PDF, Illustrator, EPS…) or a picture (PNG, JPEG…; its dark parts are traced) to engrave as the ornament or along the border.");
        ToolTip.SetTip(_allMarks, "Copy these markings to every movement gauge, ruler and template on the list.");
        ToolTip.SetTip(_textScale, "The numbers' and title's size, as a share of what fits (%).");
        _marksBody = Stack(
            Explained("Design", "A border, ornament and numbers that go together. Pick one to start from, then change anything below.",
                _designs, _designAbout),
            Explained("Numbers and title", "",
                Field("Font", _markFont, tip: "Fonts on this computer, and your handwriting fonts (✎, made with Import → Generate → Handwriting) written as single lines."),
                Row(_bold, _italic),
                Field("Burned as", _look, tip: "Engraved fills the letters and designs in; outlined scores round them; single lines draws each letter as one pass down its middle (fastest)."),
                Field("Numbers", _numbering),
                _everyRow,
                Field("Size", _textScale),
                Field("Title", _markTitle, tip: "Words to etch on it: on the gauge's first segment, along the ruler's bottom, round the circle's edge."),
                _showUnit,
                _ticksRow),
            Explained("Border", "Round each segment, the ruler's bottom band or the template's edge.",
                _borders, _borderArtRow),
            Explained("Ornament", "",
                _ornaments,
                Field("Goes", _place),
                Row(_upload, _removeArt),
                _artNote),
            _allMarks);
        return Stack(_marksFor, _marksNone, _marksBody);
    }

    // ---- markings: the pictures to pick from ----

    const double DesignTile = 84, BorderTile = 84, OrnTile = 46;

    void Galleries()
    {
        string font = _set.Font;
        _designs.SetTiles(MarkDesigns.All.Select(d =>
        {
            var (cut, etched, lines) = Measures.Sample(MarkDesigns.Apply(new Markings(), d.Name), font, 34, 12.5);
            return ((Control)new MarkPicture(cut, etched, lines, DesignTile, 31), d.Name, (string?)d.About);
        }), DesignTile);
        _borders.SetTiles(Borders.All.Select(b =>
        {
            if (b.Style == BorderStyle.Art)
            {
                var art = _arts.FirstOrDefault() ?? Item.Marks.BorderArt;
                var mk = new Markings { Border = art is null ? BorderStyle.None : BorderStyle.Art, BorderArt = art };
                var (c0, e0, l0) = Measures.Sample(mk, font, 34, 11, null);
                return ((Control)new MarkPicture(c0, e0, l0, BorderTile, 27), b.Name, (string?)"One of your designs (upload one under Ornament), repeated along the border.");
            }
            var (cut, etched, lines) = Measures.Sample(new Markings { Border = b.Style }, font, 34, 11, null);
            return ((Control)new MarkPicture(cut, etched, lines, BorderTile, 27), b.Name, (string?)null);
        }), BorderTile);
        OrnamentTiles();
    }

    /// <summary>None, the built-in ornaments, then your designs (and the accessory's own, if it isn't in your list).</summary>
    List<(string Name, MarkArt? Art)> _ornList = [];

    void OrnamentTiles()
    {
        _ornList = [("", null), .. Ornaments.Names.Select(n => (n, (MarkArt?)null)), .. _arts.Select(a => (Ornaments.ArtName, (MarkArt?)a))];
        if (MarkDesigns.Marked(Item.Kind) && Item.Marks.Art is { } own && !_arts.Any(a => a.Name == own.Name && a.Areas == own.Areas))
            _ornList.Add((Ornaments.ArtName, own));
        _ornaments.SetTiles(_ornList.Select(o =>
        {
            if (o.Name.Length == 0) return ((Control)new MarkPicture(null, [], [], OrnTile, OrnTile), "None", (string?)"No ornament");
            if (o.Art is { } art)
            {
                var (a, l) = MarkArts.Unit(art);
                return ((Control)new MarkPicture(null, a, l, OrnTile, OrnTile), art.Name, (string?)$"Your design: {art.Name}");
            }
            return ((Control)new MarkPicture(null, Ornaments.Shape(o.Name), null, OrnTile, OrnTile), o.Name, (string?)null);
        }), OrnTile);
    }

    void MarkFonts()
    {
        var hands = HandFonts.All().Select(f => HandFonts.Prefix + f.Name);
        _markFonts = ["", .. hands, .. _fonts];
        var own = Item.Marks.Font;
        if (!string.IsNullOrEmpty(own) && !_markFonts.Contains(own)) _markFonts.Insert(1, own);
        _markFont.ItemsSource = _markFonts.Select(f => f.Length == 0 ? $"Same as the sheet ({_set.Font})" : f).ToList();
    }

    void RefreshArts()
    {
        _arts = _artStore?.All() ?? [];
        _borderArt.ItemsSource = _arts.Count == 0 ? ["(upload a design first)"] : _arts.Select(a => a.Name).ToList();
        Galleries();
    }

    // ---- markings in and out ----

    /// <summary>The markings controls from the accessory picked (if it's marked).</summary>
    void LoadMarks()
    {
        var it = Item;
        bool marked = MarkDesigns.Marked(it.Kind);
        _marksBody.IsVisible = marked;
        _marksNone.IsVisible = !marked;
        _marksFor.Text = marked ? $"For the {Lower(GameKitMaker.Label(_set, _sel))}" : "";
        if (!marked) return;
        bool was = _loading;
        _loading = true;
        var mk = it.Marks;
        MarkFonts();
        OrnamentTiles();
        _markFont.SelectedIndex = Math.Max(0, _markFonts.IndexOf(mk.Font));
        _bold.IsChecked = mk.Bold;
        _italic.IsChecked = mk.Italic;
        _look.SelectedIndex = (int)mk.Look;
        _numbering.SelectedIndex = (int)mk.Numbers;
        _every.Value = Math.Clamp(mk.Every, 1, 10);
        _textScale.Value = (decimal)Math.Round(Math.Clamp(mk.TextScale, 0.5, 1.5) * 100);
        if (_markTitle.Text != mk.Title) _markTitle.Text = mk.Title;
        _showUnit.IsChecked = mk.ShowUnit;
        _place.SelectedIndex = (int)mk.Place;
        _showUnit.IsVisible = it.Kind == AccessoryKind.Ruler;
        _ticksRow.IsVisible = it.Kind == AccessoryKind.Ruler;
        _everyRow.IsVisible = it.Kind != AccessoryKind.Template || it.Shape != TemplateShape.Square;
        var (labels, parts) = TickChoices(it.Units);
        _tickParts = parts;
        _ticks.ItemsSource = labels;
        _ticks.SelectedIndex = Math.Max(0, Array.IndexOf(parts, mk.Ticks));
        _borders.Select(Borders.All.ToList().FindIndex(b => b.Style == mk.Border));
        _borderArtRow.IsVisible = mk.Border == BorderStyle.Art;
        _borderArt.SelectedIndex = mk.BorderArt is { } ba ? Math.Max(0, _arts.FindIndex(a => a.Name == ba.Name)) : 0;
        _ornaments.Select(OrnIndex(mk));
        _removeArt.IsVisible = mk.Ornament == Ornaments.ArtName && mk.Art is { } art && _arts.Any(a => a.Name == art.Name);
        ShowDesign(mk);
        _loading = was;
    }

    int OrnIndex(Markings mk)
    {
        if (string.IsNullOrEmpty(mk.Ornament)) return 0;
        if (mk.Ornament == Ornaments.ArtName)
            return mk.Art is { } art ? _ornList.FindIndex(o => o.Art is { } a && a.Name == art.Name && a.Areas == art.Areas) is var i and >= 0 ? i : _ornList.FindIndex(o => o.Art?.Name == art.Name) : -1;
        return _ornList.FindIndex(o => o.Art is null && string.Equals(o.Name, mk.Ornament, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The design picked shows as picked while its border, ornament and numbers are still the design's.</summary>
    void ShowDesign(Markings mk)
    {
        var d = MarkDesigns.Find(mk.Design);
        bool same = d is not null && d.Border == mk.Border && d.Ornament == mk.Ornament && d.Numbers == mk.Numbers && d.Place == mk.Place;
        _designs.Select(same ? MarkDesigns.All.ToList().IndexOf(d!) : -1);
        _designAbout.Text = same ? d!.About : "Your own mix of border, ornament and numbers.";
    }

    static (List<string> Labels, int[] Parts) TickChoices(Units u) => u switch
    {
        Units.Centimetres => (["The usual (millimetres)", "Centimetres", "Half centimetres", "2 mm", "Millimetres"], [0, 1, 2, 5, 10]),
        Units.GridFeet => (["The usual (half squares)", "Squares", "Half squares", "Quarter squares"], [0, 1, 2, 4]),
        Units.Custom => (["The usual", "Whole units", "Halves", "Quarters", "Fifths", "Tenths"], [0, 1, 2, 4, 5, 10]),
        _ => (["The usual (quarter inches)", "Inches", "Half inches", "Quarter inches", "Eighths", "Sixteenths"], [0, 1, 2, 4, 8, 16]),
    };

    /// <summary>The picked accessory's markings changed to <paramref name="mk"/>.</summary>
    void SetMarks(Markings mk)
    {
        if (!MarkDesigns.Marked(Item.Kind)) return;
        var items = _set.Items.ToList();
        items[_sel] = Item with { Marks = mk };
        _set = _set with { Items = items };
        ShowDesign(mk);
        _borderArtRow.IsVisible = mk.Border == BorderStyle.Art;
        _removeArt.IsVisible = mk.Ornament == Ornaments.ArtName && mk.Art is { } art && _arts.Any(a => a.Name == art.Name);
        Changed();
    }

    void MarksChanged()
    {
        if (_loading || !MarkDesigns.Marked(Item.Kind)) return;
        var mk = Item.Marks;
        int f = _markFont.SelectedIndex;
        SetMarks(mk with
        {
            Font = f >= 0 && f < _markFonts.Count ? _markFonts[f] : mk.Font,
            Bold = _bold.IsChecked == true,
            Italic = _italic.IsChecked == true,
            Look = (MarkLook)Math.Max(0, _look.SelectedIndex),
            Numbers = (Numbering)Math.Max(0, _numbering.SelectedIndex),
            Every = (int)V(_every, 1),
            TextScale = V(_textScale, 100) / 100,
            Title = _markTitle.Text ?? "",
            ShowUnit = _showUnit.IsChecked == true,
            Place = (OrnamentPlace)Math.Max(0, _place.SelectedIndex),
            Ticks = _ticks.SelectedIndex >= 0 && _ticks.SelectedIndex < _tickParts.Length ? _tickParts[_ticks.SelectedIndex] : 0,
            BorderArt = mk.Border == BorderStyle.Art && _borderArt.SelectedIndex >= 0 && _borderArt.SelectedIndex < _arts.Count ? _arts[_borderArt.SelectedIndex] : mk.BorderArt,
        });
    }

    void DesignPicked(int i)
    {
        if (_loading || i < 0 || i >= MarkDesigns.All.Count || !MarkDesigns.Marked(Item.Kind)) return;
        SetMarks(MarkDesigns.Apply(Item.Marks, MarkDesigns.All[i].Name));
        LoadMarks();
    }

    void BorderPicked(int i)
    {
        if (_loading || i < 0 || i >= Borders.All.Count || !MarkDesigns.Marked(Item.Kind)) return;
        var style = Borders.All[i].Style;
        var mk = Item.Marks with { Border = style };
        if (style == BorderStyle.Art)
        {
            if (_arts.Count == 0 && mk.BorderArt is null) { _artNote.Text = "Upload a design first (under Ornament), then pick Your design here to repeat it along the border."; return; }
            mk = mk with { BorderArt = mk.BorderArt ?? _arts[0] };
        }
        SetMarks(mk);
        LoadMarks();
    }

    void OrnamentPicked(int i)
    {
        if (_loading || i < 0 || i >= _ornList.Count || !MarkDesigns.Marked(Item.Kind)) return;
        var (name, art) = _ornList[i];
        SetMarks(Item.Marks with { Ornament = name, Art = art ?? (name == Ornaments.ArtName ? Item.Marks.Art : null) });
        LoadMarks();
    }

    async Task Upload()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Upload a design to engrave",
            FileTypeFilter =
            [
                new FilePickerFileType("Drawings and pictures") { Patterns = [.. MarkArts.Extensions.Select(e => "*" + e)] },
                new FilePickerFileType("All files") { Patterns = ["*"] },
            ],
        });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path) return;
        _artNote.Text = $"Reading {Path.GetFileName(path)}…";
        MarkArt art;
        try { art = await Task.Run(() => MarkArts.FromFile(path)); }
        catch (Exception e) when (e is FormatException or IOException or UnauthorizedAccessException or OutOfMemoryException or InvalidOperationException)
        {
            _artNote.Text = $"Couldn't use it: {e.Message}";
            return;
        }
        try { if (_artStore is not null) art = _artStore.Save(art); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _artNote.Text = $"It's on this accessory, but couldn't be kept for next time: {e.Message}"; }
        RefreshArts();
        if (MarkDesigns.Marked(Item.Kind)) SetMarks(Item.Marks with { Ornament = Ornaments.ArtName, Art = art });
        LoadMarks();
        _artNote.Text = $"\"{art.Name}\" is the ornament now. Pick Your design under Border to repeat it along the border too.";
    }

    void RemoveArt()
    {
        if (Item.Marks.Art is not { } art || _artStore is null) return;
        try { _artStore.Delete(art.Name); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _artNote.Text = $"Couldn't remove it: {e.Message}"; return; }
        RefreshArts();
        LoadMarks();
        _artNote.Text = $"\"{art.Name}\" is off the list (accessories already using it keep their copy).";
    }

    void MarksOnAll()
    {
        if (!MarkDesigns.Marked(Item.Kind)) return;
        var mk = Item.Marks;
        // Ticks are the ruler's own; the rest goes on every marked accessory.
        _set = _set with { Items = [.. _set.Items.Select(i => MarkDesigns.Marked(i.Kind) ? i with { Marks = mk with { Ticks = i.Marks.Ticks } } : i)] };
        int n = _set.Items.Count(i => MarkDesigns.Marked(i.Kind));
        _artNote.Text = n > 1 ? $"All {n} gauges, rulers and templates are marked the same now." : "There's only this one on the list.";
        Changed();
    }

    // ---- the library and fonts ----

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

    void Fonts()
    {
        try { _fonts = [.. TextLayout.FontFamilies()]; }
        catch (Exception e) when (e is InvalidOperationException or TypeInitializationException or DllNotFoundException) { _fonts = []; }
        if (!_fonts.Contains(_set.Font)) _fonts.Insert(0, _set.Font);
        _font.ItemsSource = _fonts;
    }

    (Material M, Recipe R)? CutRecipe => _cutWith.SelectedIndex > 0 && _cutWith.SelectedIndex < _cuts.Count ? _cuts[_cutWith.SelectedIndex] : null;
    (Material M, Recipe R)? EtchRecipe => _etchWith.SelectedIndex > 0 && _etchWith.SelectedIndex < _etches.Count ? _etches[_etchWith.SelectedIndex] : null;

    void CutChosen()
    {
        if (!_loading && CutRecipe is { } c)
        {
            _loading = true;
            if (c.M.ThicknessMm is { } t) _thick.Value = (decimal)Math.Clamp(t, 0.5, 12);
            if (c.R.SlotFitMm is { } fit) _fit.Value = (decimal)Math.Clamp(fit, -0.5, 2);
            int same = _etches.FindIndex(e => e is { } x && x.M.Id == c.M.Id);
            if (same > 0 && EtchRecipe is null) _etchWith.SelectedIndex = same;
            _loading = false;
        }
        _cutNote.Text = CutRecipe is not { } r
            ? "No kerf allowance: the laser burns each edge back by half its kerf, so slots come out a little loose. Pick your material's cut settings (Materials & Tests → Tests → Cut test measures the kerf) for slots at their drawn size."
            : r.R.Settings.KerfMm > 0 ? $"{r.M.Label}: kerf {r.R.Settings.KerfMm:0.###} mm, allowed for on the cut layer so slots come out their drawn width."
            : $"{r.M.Label}: no kerf measured yet, so slots come out a little loose (run a cut test).";
        SheetChanged();
    }

    // ---- settings in and out ----

    void Hook()
    {
        foreach (var n in new[] { _count, _w, _d, _h, _tray, _rim, _baffles, _die, _tiers, _slot, _ribs, _cardW, _cardH, _piles, _tiles, _tileW, _tileH, _segments, _step, _length })
            n.ValueChanged += (_, _) => ItemChanged();
        foreach (var c in new[] { _tileTop, _units, _shape })
            c.SelectionChanged += (_, _) => ItemChanged();
        _battlements.IsCheckedChanged += (_, _) => ItemChanged();
        _names.TextChanged += (_, _) => ItemChanged();
        _cardSize.SelectionChanged += (_, _) => CardSizeChosen();
        foreach (var n in new[] { _thick, _fit, _seed, _scaleTex }) n.ValueChanged += (_, _) => SheetChanged();
        _finish.SelectionChanged += (_, _) => SheetChanged();
        _theme.SelectionChanged += (_, _) => ThemeChosen();
        _font.SelectionChanged += (_, _) => SheetChanged();
        _etchWith.SelectionChanged += (_, _) => SheetChanged();
        _cutWith.SelectionChanged += (_, _) => CutChosen();
        _list.SelectionChanged += (_, _) => { if (!_loading && _list.SelectedIndex >= 0 && _list.SelectedIndex != _sel) { _sel = _list.SelectedIndex; LoadItem(); Changed(); } };
        _add.Click += (_, _) => AddItem(Defaults.For(Defaults.Kinds[Math.Max(0, _addKind.SelectedIndex)].Kind));
        _copy.Click += (_, _) => AddItem(_set.Items[_sel]);
        _remove.Click += (_, _) => RemoveItem();
        foreach (var n in new[] { _unitMm, _unitCounts }) n.ValueChanged += (_, _) => ItemChanged();
        _unitName.TextChanged += (_, _) => ItemChanged();
        // Markings
        _designs.Picked += DesignPicked;
        _borders.Picked += BorderPicked;
        _ornaments.Picked += OrnamentPicked;
        foreach (var c in new[] { _markFont, _look, _numbering, _place, _ticks, _borderArt }) c.SelectionChanged += (_, _) => MarksChanged();
        foreach (var n in new[] { _every, _textScale }) n.ValueChanged += (_, _) => MarksChanged();
        foreach (var b in new[] { _bold, _italic, _showUnit }) b.IsCheckedChanged += (_, _) => MarksChanged();
        _markTitle.TextChanged += (_, _) => MarksChanged();
        _upload.Click += async (_, _) => await Upload();
        _removeArt.Click += (_, _) => RemoveArt();
        _allMarks.Click += (_, _) => MarksOnAll();
    }

    static decimal D(double v) => (decimal)v;
    static double V(NumericUpDown n, double fallback) => n.Value is { } v ? (double)v : fallback;
    AccessoryItem Item => _set.Items[Math.Clamp(_sel, 0, _set.Items.Count - 1)];

    void FillList(int select)
    {
        bool was = _loading;
        _loading = true;
        _sel = Math.Clamp(select, 0, _set.Items.Count - 1);
        _list.ItemsSource = _set.Items.Select((it, i) => GameKitMaker.Label(_set, i) + (it.Count > 1 ? $" × {it.Count}" : "")).ToList();
        _list.SelectedIndex = _sel;
        _remove.IsEnabled = _set.Items.Count > 1;
        _loading = was;
        LoadItem();
    }

    void AddItem(AccessoryItem it)
    {
        _set = _set with { Items = [.. _set.Items, it] };
        FillList(_set.Items.Count - 1);
        Changed();
    }

    void RemoveItem()
    {
        if (_set.Items.Count < 2) return;
        var items = _set.Items.ToList();
        items.RemoveAt(_sel);
        _set = _set with { Items = items };
        FillList(Math.Min(_sel, items.Count - 1));
        Changed();
    }

    void CardSizeChosen()
    {
        if (_loading || _cardSize.SelectedIndex <= 0) return;
        var (w, h) = CardSizes[_cardSize.SelectedIndex];
        _loading = true;
        _cardW.Value = D(w);
        _cardH.Value = D(h);
        _loading = false;
        ItemChanged();
    }

    string ThemeName => GameKitThemes.Names[Math.Clamp(_theme.SelectedIndex, 0, GameKitThemes.Names.Count - 1)];

    /// <summary>A theme picked: its finish and tops replace the ones set, on every accessory in the list.</summary>
    void ThemeChosen()
    {
        _themeAbout.Text = GameKitThemes.About(ThemeName);
        if (_loading) return;
        _set = GameKitThemes.Apply(_set, ThemeName);
        LoadSheet();
        LoadItem();
        Changed();
    }

    void LoadSheet()
    {
        _loading = true;
        _theme.SelectedIndex = Math.Max(0, GameKitThemes.Names.ToList().IndexOf(_set.Theme)); _themeAbout.Text = GameKitThemes.About(_set.Theme);
        _thick.Value = D(_set.ThicknessMm); _fit.Value = D(_set.FitMm); _seed.Value = Math.Clamp(_set.Seed, 1, 9999);
        _finish.SelectedIndex = (int)_set.Finish;
        _scaleTex.Value = D(Math.Clamp(_set.TextureScale, 0.25, 10));
        _font.SelectedIndex = Math.Max(0, _fonts.IndexOf(_set.Font));
        _loading = false;
        CutChosen();
    }

    void LoadItem()
    {
        var it = Item;
        var kind = it.Kind;
        bool was = _loading;
        _loading = true;
        var info = Defaults.Info(kind);
        _title.Text = info.Name;
        _about.Text = info.About;
        _count.Value = Math.Max(1, it.Count);
        _w.Value = D(Math.Max(1, it.WidthMm)); _d.Value = D(Math.Max(1, it.DepthMm)); _h.Value = D(Math.Max(1, it.HeightMm));
        _tray.Value = D(it.TrayMm); _rim.Value = D(it.RimMm); _baffles.Value = it.Baffles; _die.Value = D(it.DieMm); _battlements.IsChecked = it.Battlements;
        _tiers.Value = it.Tiers; _slot.Value = D(it.SlotMm); _ribs.Value = it.Ribs;
        _cardW.Value = D(it.CardWMm); _cardH.Value = D(it.CardHMm); _piles.Value = it.Compartments;
        int size = Array.FindIndex(CardSizes, s => Math.Abs(s.W - it.CardWMm) < 0.01 && Math.Abs(s.H - it.CardHMm) < 0.01);
        _cardSize.SelectedIndex = Math.Max(0, size);
        _tiles.Value = it.Tiles; _tileW.Value = D(it.TileWMm); _tileH.Value = D(it.TileHMm); _tileTop.SelectedIndex = (int)it.Top;
        if (_names.Text != it.Names) _names.Text = it.Names;
        _units.SelectedIndex = (int)it.Units; _shape.SelectedIndex = (int)it.Shape;
        _segments.Value = it.Segments; _step.Value = D(it.StepUnits); _length.Value = D(it.LengthUnits);
        _unitMm.Value = D(Math.Clamp(it.UnitMm, 1, 1000)); _unitCounts.Value = D(Math.Clamp(it.UnitCounts, 0.01, 1000));
        if (_unitName.Text != it.UnitName) _unitName.Text = it.UnitName;

        (string? W, string? D, string? H) labels = kind switch
        {
            AccessoryKind.DiceTower => ("Width", "Depth", "Height"),
            AccessoryKind.CardStand => ("Length", "Depth", "Height"),
            AccessoryKind.DeckTray => (null, null, "Deck height"),
            AccessoryKind.MoveGauge or AccessoryKind.Ruler => ("Width", null, null),
            _ => (null, null, null),
        };
        _wLabel.Text = labels.W ?? ""; _dLabel.Text = labels.D ?? ""; _hLabel.Text = labels.H ?? "";
        _wRow.IsVisible = labels.W is not null;
        _dRow.IsVisible = labels.D is not null;
        _hRow.IsVisible = labels.H is not null;
        ToolTip.SetTip(_hRow, kind == AccessoryKind.DeckTray ? "How deep the piles are inside (mm): the height of a stacked deck." : null);
        _towerRows.IsVisible = kind == AccessoryKind.DiceTower;
        _standRows.IsVisible = kind == AccessoryKind.CardStand;
        _trayRows.IsVisible = kind == AccessoryKind.DeckTray;
        _initRows.IsVisible = kind == AccessoryKind.Initiative;
        bool measure = kind is AccessoryKind.MoveGauge or AccessoryKind.Ruler or AccessoryKind.Template;
        _unitsRow.IsVisible = measure;
        _ownUnitRows.IsVisible = measure && it.Units == Units.Custom;
        _gaugeRows.IsVisible = kind == AccessoryKind.MoveGauge;
        _shapeRow.IsVisible = kind == AccessoryKind.Template;
        _lengthRow.IsVisible = kind is AccessoryKind.Ruler or AccessoryKind.Template;
        _lengthLabel.Text = kind == AccessoryKind.Ruler ? "Length" : it.Shape switch { TemplateShape.Circle => "Radius", TemplateShape.Cone => "Length", _ => "Side" };
        ToolTip.SetTip(_lengthRow, "In the units above.");
        LoadMarks();
        _loading = was;
    }

    /// <summary>The chosen accessory as the settings say.</summary>
    AccessoryItem FromControls()
    {
        var it = Item;
        bool w = _wRow.IsVisible, d = _dRow.IsVisible, h = _hRow.IsVisible;
        return it with
        {
            Count = (int)V(_count, 1),
            WidthMm = w ? V(_w, it.WidthMm) : it.WidthMm, DepthMm = d ? V(_d, it.DepthMm) : it.DepthMm, HeightMm = h ? V(_h, it.HeightMm) : it.HeightMm,
            TrayMm = V(_tray, it.TrayMm), RimMm = V(_rim, it.RimMm), Baffles = (int)V(_baffles, it.Baffles), DieMm = V(_die, it.DieMm), Battlements = _battlements.IsChecked == true,
            Tiers = (int)V(_tiers, it.Tiers), SlotMm = V(_slot, it.SlotMm), Ribs = (int)V(_ribs, it.Ribs),
            CardWMm = V(_cardW, it.CardWMm), CardHMm = V(_cardH, it.CardHMm), Compartments = (int)V(_piles, it.Compartments),
            Tiles = (int)V(_tiles, it.Tiles), TileWMm = V(_tileW, it.TileWMm), TileHMm = V(_tileH, it.TileHMm), Top = (TileTop)Math.Max(0, _tileTop.SelectedIndex), Names = _names.Text ?? "",
            Units = (Units)Math.Max(0, _units.SelectedIndex), Shape = (TemplateShape)Math.Max(0, _shape.SelectedIndex),
            Segments = (int)V(_segments, it.Segments), StepUnits = V(_step, it.StepUnits), LengthUnits = V(_length, it.LengthUnits),
            UnitMm = V(_unitMm, it.UnitMm), UnitCounts = V(_unitCounts, it.UnitCounts), UnitName = _unitName.Text ?? it.UnitName,
        };
    }

    void ItemChanged()
    {
        if (_loading) return;
        var before = Item;
        var it = FromControls();
        var items = _set.Items.ToList();
        items[_sel] = it;
        _set = _set with { Items = items };
        if (it.Count != before.Count) FillList(_sel);
        // The length's label follows the template's shape; the card size its numbers.
        else if (it.Shape != before.Shape || it.Units != before.Units || it.CardWMm != before.CardWMm || it.CardHMm != before.CardHMm) LoadItem();
        Changed();
    }

    void SheetChanged()
    {
        if (_loading) return;
        _set = _set with
        {
            ThicknessMm = V(_thick, 3), FitMm = V(_fit, 0.1), Seed = (int)V(_seed, 1), Finish = (Finish)Math.Max(0, _finish.SelectedIndex),
            TextureScale = V(_scaleTex, 2), Font = _font.SelectedItem as string ?? _set.Font,
        };
        if (_set.Font != _shownFont)
        {
            // The markings' "same as the sheet" and the pictures follow the sheet's font.
            _shownFont = _set.Font;
            Galleries();
            LoadMarks();
        }
        Changed();
    }

    string? _shownFont;

    string? LastFile => _dataFolder is null ? null : Path.Combine(_dataFolder, "last.json");

    AccessorySet? LoadLast()
    {
        try { return LastFile is { } f && File.Exists(f) ? AccessorySet.FromJson(File.ReadAllText(f)) : null; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    void SaveLast()
    {
        try
        {
            if (LastFile is not { } f) return;
            Directory.CreateDirectory(Path.GetDirectoryName(f)!);
            File.WriteAllText(f, _set.ToJson());
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
        var set = _set;
        int sel = Math.Clamp(_sel, 0, set.Items.Count - 1);
        double max = MaxLength;
        SetKit kit;
        try { kit = await Task.Run(() => GameKitMaker.Make(set, max)); }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or ArithmeticException)
        {
            if (gen == _generation) { _pieces.Show(null, e.Message); _assembled.Show(null, set.ThicknessMm, e.Message); }
            return;
        }
        if (gen != _generation) return;
        _kit = kit;
        _kitFrom = set.ToJson();
        var mine = kit.Items[sel];
        var all = new List<Finding>();
        for (int i = 0; i < kit.Items.Count; i++)
        {
            string prefix = kit.Items.Count > 1 ? GameKitMaker.Label(set, i) + ": " : "";
            all.AddRange(kit.Items[i].Findings.Concat(Placement.TooBig(kit.Items[i], _machine))
                .Where(f => i == sel || f.Level != Severity.Note)
                .Select(f => f with { Text = prefix + f.Text }));
        }
        string? bad = mine.Findings.Concat(Placement.TooBig(mine, _machine)).FirstOrDefault(f => f.Level == Severity.Bad)?.Text;
        _pieces.Show(mine.Parts.Count > 0 ? mine : null, bad ?? "Nothing to show.");
        _assembled.Show(mine.Parts.Count > 0 ? mine : null, kit.ThicknessMm, bad ?? "Nothing to show.");
        _findings.Children.Clear();
        foreach (var f in all.OrderByDescending(f => f.Level)) _findings.Children.Add(FindingRow(f));
        int pieces = kit.PieceCount;
        _summary.Text = pieces == 0 ? "" :
            $"{pieces} piece{(pieces == 1 ? "" : "s")} to cut for " + ListWords(set.Items.Select((it, i) => (it.Count > 1 ? $"{it.Count} × " : "") + Lower(GameKitMaker.Label(set, i)))) +
            $" ({mine.Parts.Sum(p => p.Copies)} for each {Lower(Defaults.Info(mine.Item.Kind).Name)}).";
        _make.IsEnabled = kit.Ok && pieces > 0 && !all.Any(f => f.Level == Severity.Bad);
    }

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
            var set = _set;
            double max = MaxLength;
            var built = _kit is not null && _kitFrom == set.ToJson() ? _kit : null;
            Func<Layer, Layer>? cut = CutRecipe is { } c ? l => c.R.Settings.ApplyTo(l) with { Material = new MaterialRef(c.M.Id, c.M.Label, c.R.Id, c.R.Title) } : null;
            Func<Layer, Layer>? etch = EtchRecipe is { } e ? l => e.R.Settings.ApplyTo(l) with { Material = new MaterialRef(e.M.Id, e.M.Label, e.R.Id, e.R.Title) } : null;
            Guid? replace = _editing is { } ed && Placement.ShapesOf(_doc.State, ed.Id).Count + _doc.State.OtherPages.Sum(p => p.Shapes.Count(x => x.AddonData.TryGetValue(Placement.IdKey, out var v) && v == ed.Id.ToString())) > 0 ? ed.Id : null;
            var before = _doc.State;
            var (kit, s, sheets, id) = await Task.Run(() =>
            {
                var kit = built ?? GameKitMaker.Make(set, max);
                if (!kit.Ok) throw new InvalidOperationException(kit.Items.SelectMany(i => i.Findings).First(f => f.Level == Severity.Bad).Text);
                var (s, sheets, id) = Placement.Place(before, kit, set, _machine, new PlaceOptions { CutSettings = cut, EtchSettings = etch, Replace = replace });
                return (kit, s, sheets, id);
            });
            if (!ReferenceEquals(_doc.State, before)) { _status.Text = "The design changed meanwhile: press again to put the pieces in."; return; }
            _doc.Commit(replace is null ? "Game accessories" : "Update the game accessories", s, Placement.ShapesOf(s, id));
            _editing = (id, set);
            _make.Content = "Update in the design";
            int pieces = kit.PieceCount;
            _status.Text = $"{(replace is null ? "Put" : "Updated")} {pieces} piece{(pieces == 1 ? "" : "s")} in the design" +
                           (sheets > 1 ? $", over {sheets} pages (a bed each)" : "") +
                           $": outlines on \"{Placement.CutLayer}\", the etching on \"{Placement.EtchLayer}\" and \"{Placement.LineLayer}\" (done first).";
        }
        catch (InvalidOperationException ex) { _status.Text = ex.Message; }
        catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException or ArithmeticException or NullReferenceException)
        {
            AppLog.Error("gamekit", ex);
            _status.Text = $"Couldn't put the accessories in: {ex.Message}";
        }
        finally
        {
            _making = false;
            _make.IsEnabled = _kit?.Ok ?? true;
        }
    }
}
