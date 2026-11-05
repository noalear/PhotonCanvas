using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using PhotonCanvas.App;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Materials;
using static PhotonCanvas.App.UiKit;

namespace PhotonCanvas.Addon.Furniture;

/// <summary>
/// The furniture maker: a list of the furniture to cut (each with how many), the chosen one's size and
/// style, and the sheet it's all cut from; the chosen one's pieces flat and put together in 3D; then
/// every piece goes into the design, nested onto the bed.
/// </summary>
public sealed class FurnitureWindow : Window
{
    readonly Document _doc;
    readonly MachineProfile _machine;
    readonly MaterialLibrary? _library;
    readonly string? _dataFolder;
    (Guid Id, FurnitureSet Spec)? _editing;
    FurnitureSet _set;
    int _sel;

    // The list
    readonly ListBox _list = new() { MinHeight = 120, MaxHeight = 180 };
    readonly ComboBox _addKind = Combo([.. Defaults.Kinds.Select(k => k.Name)]);
    readonly Button _add = UiKit.Button("Add"), _copy = UiKit.Button("Copy"), _remove = UiKit.Button("Remove");

    // The chosen piece of furniture
    readonly TextBlock _title = new() { FontWeight = FontWeight.SemiBold }, _about = Hint();
    readonly NumericUpDown _count = Number(1, 500, 1, "0");
    readonly NumericUpDown _w = Number(1, 1000, 0.5m, "0.#"), _d = Number(0, 1000, 0.5m, "0.#"), _h = Number(1, 1000, 0.5m, "0.#");
    readonly TextBlock _wLabel = Label("Width"), _dLabel = Label("Depth"), _hLabel = Label("Height");
    Control _dRow = null!;
    readonly ComboBox _legs = Combo("Four legs", "Solid ends", "Trestles", "Round, crossed legs");
    readonly CheckBox _rail = new() { Content = "A rail low down between the legs" };
    readonly NumericUpDown _legW = Number(0, 50, 0.5m, "0.#");
    readonly ComboBox _top = Combo("Round", "Square");
    readonly NumericUpDown _seat = Number(2, 500, 0.5m, "0.#");
    readonly ComboBox _back = Combo("Solid", "Upright slats", "Ladder (rails across)");
    readonly NumericUpDown _shelves = Number(0, 30, 1, "0");
    readonly CheckBox _backPanel = new() { Content = "A back panel" }, _books = new() { Content = "Books etched on the back" };
    readonly ComboBox _lid = Combo();
    readonly CheckBox _bands = new();
    readonly TextBox _label = new() { Watermark = "Picked to suit", MaxLength = 24 };
    Control _labelRow = null!;
    readonly NumericUpDown _foot = Number(1, 500, 0.5m, "0.#"), _mattress = Number(2, 500, 0.5m, "0.#");
    readonly CheckBox _bedding = new() { Content = "A pillow and a blanket etched on" };
    readonly ComboBox _doorShape = Combo("Square top", "Round arch", "Pointed arch"), _hinge = Combo("On the left", "On the right");
    readonly NumericUpDown _wallT = Number(0.5m, 50, 0.5m, "0.##"), _wallSheet = Number(0.5m, 20, 0.5m, "0.##"), _overlap = Number(0, 10, 0.5m, "0.#"), _slot = Number(2, 30, 0.5m, "0.#");
    readonly Button _match = UiKit.Button("Match the walls in the design", tip: "Reads the doorway, the wall's thickness and the mount slots from walls made with Walls (the ones selected, or the first in the design).");
    readonly TextBlock _matchNote = Hint();
    Control _legRows = null!, _topRow = null!, _seatRow = null!, _backRow = null!, _shelvesRow = null!, _shelfRows = null!, _boxRows = null!, _bedRows = null!, _doorRows = null!, _shelfWallRows = null!, _wallRows = null!, _legWRow = null!;
    readonly TextBlock _hLabelRow = Label("");
    Control _hRow = null!;
    // The newer kinds' options: a style (front, kind of seat, pattern...) and the sizes and choices that go with it.
    readonly TextBlock _styleLabel = Label("Style");
    readonly ComboBox _style = Combo();
    readonly ComboBox _crown = Combo("Flat", "Cornice (two layers)", "Pediment", "Arch");
    readonly ComboBox _contents = Combo("Books", "Bottles", "Jars", "Plates", "Scrolls", "Machines", "Nothing");
    readonly ComboBox _backTop = Combo("Square", "Rounded", "Arched", "Pointed", "Winged");
    readonly NumericUpDown _cols = Number(1, 12, 1, "0"), _rows = Number(1, 12, 1, "0"), _arm = Number(2, 500, 0.5m, "0.#"), _hang = Number(0, 500, 0.5m, "0.#");
    readonly TextBlock _colsLabel = Label("Across"), _rowsLabel = Label("Rows");
    readonly ComboBox _look = Combo(["The list's theme", .. FurnitureThemes.Names]);
    Control _styleRow = null!, _crownRow = null!, _contentsRow = null!, _backTopRow = null!, _colsRow = null!, _rowsRow = null!, _armRow = null!, _hangRow = null!, _lookRow = null!;

    // Ready-made furniture from the catalog, for the theme picked
    readonly ComboBox _room = Combo(["Every room", .. FurnitureCatalog.Rooms]);
    readonly ComboBox _ready = Combo();
    readonly TextBlock _readyAbout = Hint();
    readonly Button _addReady = UiKit.Button("Add");
    List<CatalogEntry> _readyList = [];

    // The sheet
    readonly ComboBox _scale = Combo("15 mm", "28 mm", "32 mm", "54 mm");
    static readonly double[] Scales = [15, 28, 32, 54];
    readonly NumericUpDown _thick = Number(0.3m, 20, 0.5m, "0.##"), _fit = Number(-0.5m, 2, 0.05m, "0.##"), _seed = Number(1, 9999, 1, "0");
    readonly ComboBox _detail = Combo("None", "Wood grain", "Planks and grain", "Metal panels and rivets (sci-fi)", "Stone (cracks and pits)");
    readonly ComboBox _theme = Combo([.. FurnitureThemes.Names]);
    readonly TextBlock _themeAbout = Hint();
    readonly Button _starter = UiKit.Button("Start the list from this theme", tip: "Replaces the list with furniture that suits the theme: bunks, lockers and crates for sci-fi, a trestle table, benches and chests for a dungeon, and so on.");
    readonly ComboBox _joints = Combo("Snug, to glue", "Push fit (grip ribs, no glue)");
    readonly NumericUpDown _grip = Number(0.02m, 0.5m, 0.05m, "0.##");
    Control _gripRow = null!;
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
    readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(200) };
    bool _loading;
    int _generation;
    SetKit? _kit;
    string? _kitFrom;

    public FurnitureWindow(Document doc, MachineProfile machine, MaterialLibrary? library, string? dataFolder, (Guid Id, FurnitureSet Spec)? editing)
    {
        _doc = doc;
        _machine = machine;
        _library = library;
        _dataFolder = dataFolder;
        _editing = editing;
        _set = editing?.Spec ?? LoadLast() ?? new FurnitureSet();
        if (_set.Items.Count == 0) _set = _set with { Items = [Defaults.For(ItemKind.Table)] };
        Title = "Mini furniture";
        Width = 1100;
        Height = 780;
        MinWidth = 860;
        MinHeight = 580;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");

        Library();
        var tabs = new TabControl { Items = { Tab("Furniture", FurnitureTab()), Tab("Sheet", SheetTab()) } };
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
        LoadSheet();
        FillReady();
        FillList(0);
        if (editing is not null) _make.Content = "Update in the design";
        Closed += (_, _) => { _debounce.Stop(); _generation++; SaveLast(); };
        Rebuild().ContinueWith(_ => { }, TaskScheduler.Default);
    }

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

    Control FurnitureTab()
    {
        var addRow = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 6, Children = { _addKind, _add.With(b => { Grid.SetColumn(b, 1); b.MinWidth = 70; }) } };
        ToolTip.SetTip(_addKind, "Pick a piece of furniture and press Add to put it on the list.");
        _legWRow = Field("Leg width", _legW, tip: "How wide the legs are (mm). 0 = to suit the sheet.");
        _legRows = Stack(Field("Legs", _legs), _rail);
        _topRow = Field("Seat shape", _top);
        _seatRow = Field("Seat height", _seat, tip: "How high the top of the seat is (mm).");
        _backRow = Field("Back", _back);
        _shelvesRow = Field("Shelves", _shelves, tip: "Shelves between the bottom one and the top.");
        _shelfRows = Stack(_backPanel, _books);
        _styleRow = Line(_styleLabel, _style);
        _crownRow = Field("Top", _crown, tip: "What's on top: a flat top, a stepped cornice, a pointed pediment or an arch.");
        _contentsRow = Field("On the shelves", _contents, tip: "What's etched on the back between the shelves, where it shows through open shelves or glass.");
        _backTopRow = Field("Back's top", _backTop);
        _armRow = Field("Arm height", _arm, tip: "How high the arms are (mm).");
        _colsRow = Line(_colsLabel, _cols);
        _rowsRow = Line(_rowsLabel, _rows);
        _hangRow = Field("Hangs at", _hang, tip: "How high its bottom edge is up the wall (mm).");
        _lookRow = Field("Look", _look, tip: "The theme this piece is made in: its doors, handles and ornament. Ready-made pieces keep their own, so a sci-fi console can stand in a medieval list.");
        _labelRow = Field("Stencilled", _label, tip: "The words painted on a cargo or military crate's long sides. Left empty, each crate gets some to suit (SUPPLIES, FRAGILE, AMMO, RATIONS…).");
        _boxRows = Stack(Field("Lid", _lid), _bands, _labelRow);
        _bedRows = Stack(Field("Foot board", _foot, tip: "How high the foot board is (mm)."), Field("Mattress", _mattress, tip: "How high the top of the mattress is (mm)."), _bedding);
        _doorRows = Stack(Field("Doorway", _doorShape, tip: "The doorway's shape, as cut by Walls."), Field("Hinges", _hinge, tip: "Which side of the doorway the door hangs on, seen from the side it opens to."),
            Field("Covers by", _overlap, tip: "How far the door reaches over the wall round the doorway (mm)."));
        _shelfWallRows = Stack(Field("Wall's sheet", _wallSheet, tip: "The sheet the wall was cut from (mm): its mount slots are that wide."), Field("Slot height", _slot, tip: "How tall the wall's mount slots are (mm)."));
        _wallRows = Stack(Field("Wall thickness", _wallT, tip: "How thick the wall is (mm): both panels if it's two-sided."), _shelfWallRows, _match, _matchNote);
        _dRow = Line(_dLabel, _d);
        _hRow = Line(_hLabel, _h);
        var readyRow = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 6, Children = { _ready, _addReady.With(b => { Grid.SetColumn(b, 1); b.MinWidth = 70; }) } };
        ToolTip.SetTip(_ready, "Furniture already set up for the theme and the room: press Add to put it on the list.");
        return Stack(
            Explained("Theme", "A look to start from, the same as the wall maker's: it sets the detail on the faces (planks, rough grain, metal panels or stone) and the doors' shape. Start the list from it for furniture that suits it.",
                Field("Theme", _theme), _themeAbout, _starter),
            Explained("Ready-made", "Pieces already set up for the theme above and a room: four-posters and wardrobes, thrones on a dais, lockers and bunks for a ship, a forge, a sarcophagus. Each keeps its theme's look whatever the list's theme.",
                Field("Room", _room), readyRow, _readyAbout),
            Explained("Furniture to cut", "Everything on the list is cut from the same sheet and nested onto the bed together. Pick one to change it.",
                _list,
                addRow,
                Row(_copy, _remove)),
            Explained("This piece", "",
                _title, _about,
                Field("How many", _count),
                _styleRow,
                Line(_wLabel, _w), _dRow, _hRow,
                _legRows, _topRow, _legWRow, _seatRow, _armRow, _backRow, _backTopRow, _crownRow, _colsRow, _rowsRow, _shelvesRow, _contentsRow, _shelfRows, _boxRows, _bedRows, _hangRow, _doorRows, _wallRows, _lookRow));
    }

    Control SheetTab()
    {
        var shuffle = UiKit.Button("Shuffle");
        shuffle.HorizontalAlignment = HorizontalAlignment.Left;
        shuffle.Click += (_, _) => _seed.Value = (_seed.Value ?? 1) % 9999 + 1;
        return Stack(
            Explained("Sheet", "Everything is cut from one sheet: its thickness sets every slot and tab. Thin sheet (1.5 to 2 mm) looks best at 28 mm.",
                Field("Thickness", _thick, tip: "The sheet's thickness (mm)."),
                Field("Slot fit", _fit, tip: "Added to the sheet's thickness for the slots (mm): more is looser. 0.1 is a snug fit to glue."),
                Field("Figures", _scale, tip: "Changing it scales every size on the list to suit figures that size.")),
            Explained("Joints", "Snug joints want a drop of glue. Push fit puts small ribs in every slot and finger joint that squeeze flat as the pieces go together, so they hold without glue and come apart again.",
                Field("Joints", _joints),
                _gripRow = Field("Grip", _grip, tip: "How much tighter than the sheet the ribs make each joint (mm). More holds harder but is harder to push together; 0.15 suits plywood and MDF.")),
            Explained("Detail", "Etched and scored on the faces that show.",
                Field("Faces", _detail, tip: "Grain is scored lines; planks add etched grooves between boards; metal is a panel line and rivets; stone is cracks and pits."),
                Field("Variation", new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 6, Children = { _seed, shuffle.With(b => Grid.SetColumn(b, 1)) } },
                    tip: "Grain and plank joints are laid out at random: each number gives a different layout.")),
            Explained("Material", "Picking your material's settings brings its thickness and, from your cut test, its kerf and slot fit.",
                Field("Cut with", _cutWith),
                Field("Etch with", _etchWith, tip: "Settings for the planks, bands and books (an engrave/fill recipe from your library)."),
                _cutNote));
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
            if (c.M.ThicknessMm is { } t) _thick.Value = (decimal)Math.Clamp(t, 0.3, 20);
            if (c.R.SlotFitMm is { } fit) _fit.Value = (decimal)Math.Clamp(fit, -0.5, 2);
            int same = _etches.FindIndex(e => e is { } x && x.M.Id == c.M.Id);
            if (same > 0 && EtchRecipe is null) _etchWith.SelectedIndex = same;
            _loading = false;
        }
        _cutNote.Text = CutRecipe is not { } r
            ? "No kerf allowance: the laser burns each edge back by half its kerf, so slots come out a little loose. Pick your material's cut settings (Laser → Tests → Cut test measures the kerf) for slots at their drawn size."
            : r.R.Settings.KerfMm > 0 ? $"{r.M.Label}: kerf {r.R.Settings.KerfMm:0.###} mm, allowed for on the cut layer so slots come out their drawn width."
            : $"{r.M.Label}: no kerf measured yet, so slots come out a little loose (run a cut test).";
        SheetChanged();
    }

    // ---- settings in and out ----

    void Hook()
    {
        foreach (var n in new[] { _count, _w, _d, _h, _legW, _seat, _shelves, _foot, _mattress, _wallT, _wallSheet, _overlap, _slot, _cols, _rows, _arm, _hang })
            n.ValueChanged += (_, _) => ItemChanged();
        _style.SelectionChanged += (_, _) => { if (!_loading) { ItemChanged(); LoadItem(); } };
        foreach (var c in new[] { _legs, _top, _back, _lid, _doorShape, _hinge, _crown, _contents, _backTop, _look })
            c.SelectionChanged += (_, _) => ItemChanged();
        foreach (var c in new[] { _rail, _backPanel, _books, _bands, _bedding })
            c.IsCheckedChanged += (_, _) => ItemChanged();
        _label.TextChanged += (_, _) => ItemChanged();
        foreach (var n in new[] { _thick, _fit, _seed, _grip }) n.ValueChanged += (_, _) => SheetChanged();
        _joints.SelectionChanged += (_, _) => { _gripRow.IsVisible = _joints.SelectedIndex == 1; SheetChanged(); };
        _detail.SelectionChanged += (_, _) => SheetChanged();
        _theme.SelectionChanged += (_, _) => ThemeChosen();
        _starter.Click += (_, _) => StartFromTheme();
        _etchWith.SelectionChanged += (_, _) => SheetChanged();
        _cutWith.SelectionChanged += (_, _) => CutChosen();
        _scale.SelectionChanged += (_, _) => ScaleChosen();
        _list.SelectionChanged += (_, _) => { if (!_loading && _list.SelectedIndex >= 0 && _list.SelectedIndex != _sel) { _sel = _list.SelectedIndex; LoadItem(); Changed(); } };
        _add.Click += (_, _) => AddItem(Defaults.For(Defaults.Kinds[Math.Max(0, _addKind.SelectedIndex)].Kind));
        _room.SelectionChanged += (_, _) => FillReady();
        _ready.SelectionChanged += (_, _) => _readyAbout.Text = ReadyChosen?.About ?? "";
        _addReady.Click += (_, _) => { if (ReadyChosen is { } e) AddItem(e.Item); };
        _copy.Click += (_, _) => AddItem(_set.Items[_sel]);
        _remove.Click += (_, _) => RemoveItem();
        _match.Click += (_, _) => MatchWalls();
    }

    static decimal D(double v) => (decimal)v;
    static double V(NumericUpDown n, double fallback) => n.Value is { } v ? (double)v : fallback;
    FurnitureItem Item => _set.Items[Math.Clamp(_sel, 0, _set.Items.Count - 1)];

    string ThemeName => FurnitureThemes.Names[Math.Clamp(_theme.SelectedIndex, 0, FurnitureThemes.Names.Count - 1)];

    /// <summary>A theme picked: its detail and door shape replace the ones set.</summary>
    void ThemeChosen()
    {
        _themeAbout.Text = FurnitureThemes.About(ThemeName);
        FillReady();
        if (_loading) return;
        _set = FurnitureThemes.Apply(_set, ThemeName);
        LoadSheet();
        FillList(_sel);
        Changed();
    }

    /// <summary>The list replaced by the theme's starter furniture, sized for the figures chosen.</summary>
    void StartFromTheme()
    {
        double k = _set.ScaleMm / 28;
        _set = FurnitureThemes.Apply(_set with { Items = [.. FurnitureThemes.Starter(ThemeName).Select(it => Math.Abs(k - 1) < 1e-9 ? it : Scaled(it, k))] }, ThemeName);
        LoadSheet();
        FillList(0);
        Changed();
    }

    void FillList(int select)
    {
        bool was = _loading;
        _loading = true;
        _sel = Math.Clamp(select, 0, _set.Items.Count - 1);
        _list.ItemsSource = _set.Items.Select((it, i) => FurnitureKit.Label(_set, i) + (it.Count > 1 ? $" × {it.Count}" : "")).ToList();
        _list.SelectedIndex = _sel;
        _remove.IsEnabled = _set.Items.Count > 1;
        _loading = was;
        LoadItem();
    }

    void AddItem(FurnitureItem it)
    {
        // A new piece of furniture is sized for the figures chosen.
        if (it.Count == 1 && ReferenceEquals(it, _set.Items.ElementAtOrDefault(_sel)) is false && Math.Abs(_set.ScaleMm - 28) > 1e-9)
            it = Scaled(it, _set.ScaleMm / 28);
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

    static FurnitureItem Scaled(FurnitureItem it, double k) => FurnitureCatalog.Scale(it, k);

    void ScaleChosen()
    {
        if (_loading) return;
        double to = Scales[Math.Clamp(_scale.SelectedIndex, 0, Scales.Length - 1)], k = to / Math.Max(1, _set.ScaleMm);
        if (Math.Abs(k - 1) < 1e-9) return;
        _set = _set with { ScaleMm = to, Items = [.. _set.Items.Select(it => Scaled(it, k))] };
        FillList(_sel);
        Changed();
    }

    void LoadSheet()
    {
        _loading = true;
        _thick.Value = D(_set.ThicknessMm); _fit.Value = D(_set.FitMm); _seed.Value = Math.Clamp(_set.Seed, 1, 9999);
        _detail.SelectedIndex = (int)_set.Detail;
        _theme.SelectedIndex = Math.Max(0, FurnitureThemes.Names.ToList().IndexOf(_set.Theme)); _themeAbout.Text = FurnitureThemes.About(_set.Theme);
        _joints.SelectedIndex = _set.PushFit ? 1 : 0;
        _grip.Value = D(Math.Clamp(_set.GripMm, 0.02, 0.5));
        _gripRow.IsVisible = _set.PushFit;
        int sc = Array.FindIndex(Scales, x => Math.Abs(x - _set.ScaleMm) < 1e-6);
        _scale.SelectedIndex = sc < 0 ? 1 : sc;
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
        _title.Text = string.IsNullOrWhiteSpace(it.Name) ? info.Name : $"{it.Name} ({info.Name.ToLowerInvariant()})";
        _about.Text = info.About;
        _count.Value = Math.Max(1, it.Count);
        _w.Value = D(it.WidthMm); _d.Value = D(it.DepthMm); _h.Value = D(it.HeightMm);
        _legs.SelectedIndex = (int)it.Legs; _rail.IsChecked = it.Stretcher; _legW.Value = D(it.LegMm);
        _top.SelectedIndex = it.Top == TopShape.Round ? 0 : 1;
        _seat.Value = D(it.SeatMm); _back.SelectedIndex = (int)it.Back;
        _shelves.Value = it.Shelves; _backPanel.IsChecked = it.BackPanel; _books.IsChecked = it.Books;
        _lid.ItemsSource = kind == ItemKind.Crate ? new List<string> { "Closed (glued on)", "Open top" } : new List<string> { "Lifts off (a rim under it)", "Glued on", "None (open)" };
        _lid.SelectedIndex = kind == ItemKind.Crate ? (it.Lid == LidStyle.None ? 1 : 0) : (int)it.Lid;
        _bands.Content = kind == ItemKind.Crate ? "Boards round the edge and a brace etched on" : "Iron bands and a lock plate etched on";
        _bands.IsChecked = it.Bands;
        _bands.IsVisible = kind != ItemKind.Crate;
        _label.Text = it.Label ?? "";
        _labelRow.IsVisible = kind == ItemKind.Crate && it.Bands && it.CrateStyle is (CrateStyle.Cargo or CrateStyle.Military);
        _foot.Value = D(it.FootMm); _mattress.Value = D(it.MattressMm); _bedding.IsChecked = it.Bedding;
        _doorShape.SelectedIndex = (int)it.Shape; _hinge.SelectedIndex = (int)it.Hinge; _overlap.Value = D(it.OverlapMm);
        _wallT.Value = D(it.WallMm); _wallSheet.Value = D(it.WallSheetMm); _slot.Value = D(it.MountSlotMm);
        _matchNote.Text = "";
        var styles = StylesOf(kind);
        _styleRow.IsVisible = styles is not null;
        if (styles is { } st)
        {
            _styleLabel.Text = st.Label;
            _style.ItemsSource = st.Names.ToList();
            _style.SelectedIndex = Math.Clamp(StyleOf(it), 0, st.Names.Length - 1);
        }
        _crown.SelectedIndex = (int)it.Crown; _contents.SelectedIndex = (int)it.Contents; _backTop.SelectedIndex = (int)it.BackTop;
        _cols.Value = Math.Max(1, it.Columns); _rows.Value = Math.Max(1, it.Rows); _arm.Value = D(it.ArmMm); _hang.Value = D(it.HangMm);
        int lookAt = it.Theme is { } th ? FurnitureThemes.Names.ToList().IndexOf(th) : -1;
        _look.SelectedIndex = lookAt + 1;

        (string W, string? D, string H) labels = kind switch
        {
            ItemKind.Table when it.Legs == LegStyle.Round => ("Across", null, "Height"),
            ItemKind.Table or ItemKind.Bench => ("Length", "Depth", "Height"),
            ItemKind.Stool => ("Across", null, "Height"),
            ItemKind.Chair => ("Seat width", "Seat depth", "Back height"),
            ItemKind.Bookshelf or ItemKind.Chest or ItemKind.Crate => ("Width", "Depth", "Height"),
            ItemKind.Bed => ("Length", "Width", "Head board"),
            ItemKind.WallDoor => ("Doorway width", null, "Doorway height"),
            ItemKind.WallShelf => ("Length", "Depth", "Bracket size"),
            ItemKind.Armchair => ("Width", "Depth", "Back height"),
            ItemKind.Coffin => ("Length", "Width", "Height"),
            ItemKind.Rug => (it.Rug == RugPattern.Round ? "Across" : "Length", it.Rug == RugPattern.Round ? null : "Width", ""),
            ItemKind.WallHanging => ("Width", null, "Height"),
            ItemKind.Candlestand => ("Spread", "Foot", "Height"),
            _ => ("Width", "Depth", "Height"),
        };
        _wLabel.Text = labels.W;
        _hLabel.Text = labels.H;
        _hRow.IsVisible = labels.H != "";
        _dLabel.Text = labels.D ?? "";
        _dRow.IsVisible = labels.D is not null;
        _legRows.IsVisible = kind is ItemKind.Table or ItemKind.Bench;
        _legWRow.IsVisible = kind is ItemKind.Table or ItemKind.Bench or ItemKind.Chair or ItemKind.Bed;
        _topRow.IsVisible = kind == ItemKind.Stool;
        _seatRow.IsVisible = kind is ItemKind.Chair or ItemKind.Armchair;
        _backRow.IsVisible = kind == ItemKind.Chair;
        _armRow.IsVisible = _backTopRow.IsVisible = kind == ItemKind.Armchair && it.Seat != SeatStyle.Pew;
        _crownRow.IsVisible = kind == ItemKind.Cabinet;
        _contentsRow.IsVisible = kind == ItemKind.Cabinet && it.Front is CabinetFront.Open or CabinetFront.Glass or CabinetFront.Hutch;
        _shelvesRow.IsVisible = kind == ItemKind.Bookshelf || kind == ItemKind.Cabinet && it.Front is not (CabinetFront.Clock or CabinetFront.Altar or CabinetFront.Drawers);
        (string? cols, string? rows) counts = kind switch
        {
            ItemKind.Cabinet => (it.Front is CabinetFront.Clock or CabinetFront.Altar or CabinetFront.Open ? null : "Across", it.Front is CabinetFront.Drawers or CabinetFront.DrawersOverDoors ? "Drawer rows" : null),
            ItemKind.Armchair when it.Seat is SeatStyle.Sofa or SeatStyle.Pew => ("Seats", null),
            ItemKind.Screen => ("Panels", null),
            ItemKind.WeaponRack => (null, "Weapons"),
            _ => (null, null),
        };
        _colsLabel.Text = counts.cols ?? ""; _colsRow.IsVisible = counts.cols is not null;
        _rowsLabel.Text = counts.rows ?? ""; _rowsRow.IsVisible = counts.rows is not null;
        _hangRow.IsVisible = kind == ItemKind.WallHanging;
        _lookRow.IsVisible = (int)kind >= (int)ItemKind.Cabinet;
        _shelfRows.IsVisible = kind == ItemKind.Bookshelf;
        _boxRows.IsVisible = kind is ItemKind.Chest or ItemKind.Crate;
        _bedRows.IsVisible = kind == ItemKind.Bed;
        _doorRows.IsVisible = kind == ItemKind.WallDoor;
        _shelfWallRows.IsVisible = kind == ItemKind.WallShelf;
        _wallRows.IsVisible = kind is ItemKind.WallDoor or ItemKind.WallShelf;
        _loading = was;
    }

    /// <summary>The chosen piece of furniture as the settings say.</summary>
    FurnitureItem FromControls()
    {
        var it = Item;
        var kind = it.Kind;
        return it with
        {
            Count = (int)V(_count, 1),
            WidthMm = V(_w, it.WidthMm), DepthMm = V(_d, it.DepthMm), HeightMm = V(_h, it.HeightMm),
            Legs = (LegStyle)Math.Max(0, _legs.SelectedIndex), Stretcher = _rail.IsChecked == true, LegMm = V(_legW, 0),
            Top = _top.SelectedIndex == 1 ? TopShape.Square : TopShape.Round,
            SeatMm = V(_seat, it.SeatMm), Back = (BackStyle)Math.Max(0, _back.SelectedIndex),
            Shelves = (int)V(_shelves, it.Shelves), BackPanel = _backPanel.IsChecked == true, Books = _books.IsChecked == true,
            Lid = kind == ItemKind.Crate ? (_lid.SelectedIndex == 1 ? LidStyle.None : LidStyle.Flat) : (LidStyle)Math.Max(0, _lid.SelectedIndex),
            Bands = _bands.IsChecked == true,
            Label = string.IsNullOrWhiteSpace(_label.Text) ? null : _label.Text.Trim(),
            FootMm = V(_foot, it.FootMm), MattressMm = V(_mattress, it.MattressMm), Bedding = _bedding.IsChecked == true,
            Shape = (DoorShape)Math.Max(0, _doorShape.SelectedIndex), Hinge = (HingeSide)Math.Max(0, _hinge.SelectedIndex), OverlapMm = V(_overlap, it.OverlapMm),
            WallMm = V(_wallT, it.WallMm), WallSheetMm = V(_wallSheet, it.WallSheetMm), MountSlotMm = V(_slot, it.MountSlotMm),
            Crown = (Crown)Math.Max(0, _crown.SelectedIndex), Contents = (ShelfContents)Math.Max(0, _contents.SelectedIndex), BackTop = (BackTop)Math.Max(0, _backTop.SelectedIndex),
            Columns = (int)V(_cols, it.Columns), Rows = (int)V(_rows, it.Rows), ArmMm = V(_arm, it.ArmMm), HangMm = V(_hang, it.HangMm),
            Theme = _look.SelectedIndex > 0 ? FurnitureThemes.Names[_look.SelectedIndex - 1] : null,
        } is var made && StylesOf(kind) is not null && _style.SelectedIndex >= 0 ? WithStyle(made, _style.SelectedIndex) : made;
    }

    // ---- the newer kinds' styles ----

    static (string Label, string[] Names)? StylesOf(ItemKind k) => k switch
    {
        ItemKind.Cabinet => ("Front", ["Doors", "Drawers", "Drawers over doors", "Glass doors", "Open shelves", "Dresser (shelves over a cupboard)", "Panelled doors", "Lockers", "Machine panels", "Clock", "Lattice doors", "Altar"]),
        ItemKind.Armchair => ("Kind", ["Armchair", "Throne on a dais", "Sofa or settle", "Pew", "Command chair"]),
        ItemKind.Bed => ("Kind", ["Plain", "Four-poster", "Bunk beds", "Pallet on sleepers"]),
        ItemKind.Fireplace => ("Kind", ["Fireplace", "Bread oven", "Forge"]),
        ItemKind.SlantDesk => ("Kind", ["Lectern", "Writing desk (scriptorium)", "Control console", "Map table"]),
        ItemKind.Stand => ("Kind", ["Armour", "Training dummy", "Coat stand"]),
        ItemKind.Coffin => ("Kind", ["Coffin", "Sarcophagus", "Sleeper pod"]),
        ItemKind.Rug => ("Pattern", ["Bordered", "Kilim", "Damask", "Hide", "Deck mat", "Round"]),
        ItemKind.WallHanging => ("Kind", ["Tapestry", "Banner", "Painting", "Shield and swords", "Map", "Screen"]),
        ItemKind.Candlestand => ("Kind", ["Candelabra", "Lamp", "Torch"]),
        ItemKind.Screen => ("Kind", ["Lattice", "Tracery", "Plain panels", "Barrier"]),
        ItemKind.Desk => ("On top", ["Papers, a book and ink", "A screen and keyboard"]),
        ItemKind.Crate => ("Boards", Crates.StyleNames),
        _ => null,
    };

    static int StyleOf(FurnitureItem it) => it.Kind switch
    {
        ItemKind.Cabinet => (int)it.Front,
        ItemKind.Armchair => (int)it.Seat,
        ItemKind.Bed => (int)it.BedStyle,
        ItemKind.Fireplace => (int)it.Hearth,
        ItemKind.SlantDesk => (int)it.Slant,
        ItemKind.Stand => (int)it.Stand,
        ItemKind.Coffin => (int)it.Coffin,
        ItemKind.Rug => (int)it.Rug,
        ItemKind.WallHanging => (int)it.Hanging,
        ItemKind.Candlestand => (int)it.Candle,
        ItemKind.Screen => (int)it.ScreenStyle,
        ItemKind.Desk => it.Contents == ShelfContents.Tech ? 1 : 0,
        ItemKind.Crate => it.Bands ? (int)it.CrateStyle + 1 : 0,
        _ => 0,
    };

    static FurnitureItem WithStyle(FurnitureItem it, int i) => it.Kind switch
    {
        ItemKind.Cabinet => it with { Front = (CabinetFront)i },
        ItemKind.Armchair => it with { Seat = (SeatStyle)i },
        ItemKind.Bed => it with { BedStyle = (BedStyle)i },
        ItemKind.Fireplace => it with { Hearth = (HearthStyle)i },
        ItemKind.SlantDesk => it with { Slant = (SlantStyle)i },
        ItemKind.Stand => it with { Stand = (StandStyle)i },
        ItemKind.Coffin => it with { Coffin = (CoffinStyle)i },
        ItemKind.Rug => it with { Rug = (RugPattern)i },
        ItemKind.WallHanging => it with { Hanging = (HangingStyle)i },
        ItemKind.Candlestand => it with { Candle = (CandleStyle)i },
        ItemKind.Screen => it with { ScreenStyle = (ScreenStyle)i },
        ItemKind.Desk => it with { Contents = i == 1 ? ShelfContents.Tech : ShelfContents.Books },
        ItemKind.Crate => it with { Bands = i > 0, CrateStyle = (CrateStyle)Math.Max(0, i - 1) },
        _ => it,
    };

    // ---- ready-made furniture ----

    CatalogEntry? ReadyChosen => _ready.SelectedIndex >= 0 && _ready.SelectedIndex < _readyList.Count ? _readyList[_ready.SelectedIndex] : null;

    /// <summary>The ready-made list for the theme and room picked.</summary>
    void FillReady()
    {
        string? room = _room.SelectedIndex > 0 ? FurnitureCatalog.Rooms[_room.SelectedIndex - 1] : null;
        _readyList = [.. FurnitureCatalog.For(ThemeName, room)];
        _ready.ItemsSource = _readyList.Select(e => room is null ? $"{e.Name} ({e.Room.ToLowerInvariant()})" : e.Name).ToList();
        _ready.SelectedIndex = _readyList.Count > 0 ? 0 : -1;
        _addReady.IsEnabled = _readyList.Count > 0;
        _readyAbout.Text = ReadyChosen?.About ?? "Nothing ready-made for this room in this theme yet: try another room.";
    }

    void ItemChanged()
    {
        if (_loading) return;
        var before = Item;
        var it = FromControls();
        var items = _set.Items.ToList();
        items[_sel] = it;
        _set = _set with { Items = items };
        // The list shows how many; the labels follow the table's legs.
        if (it.Count != before.Count || it.Legs != before.Legs) FillList(_sel);
        Changed();
    }

    void SheetChanged()
    {
        if (_loading) return;
        _set = _set with { ThicknessMm = V(_thick, 2), FitMm = V(_fit, 0.1), Seed = (int)V(_seed, 1), Detail = (WoodDetail)Math.Max(0, _detail.SelectedIndex),
                        PushFit = _joints.SelectedIndex == 1, GripMm = V(_grip, 0.15) };
        Changed();
    }

    void MatchWalls()
    {
        if (WallsLink.Find(_doc.State, _doc.Selection) is not { } w)
        {
            _matchNote.Text = "There are no walls from Walls in the design. Type the doorway's size and the wall's thickness instead.";
            return;
        }
        var items = _set.Items.ToList();
        items[_sel] = WallsLink.Fit(Item, w);
        _set = _set with { Items = items };
        LoadItem();
        _matchNote.Text = Item.Kind == ItemKind.WallDoor
            ? (w.HasDoorway ? $"Matched: a {w.DoorWidthMm:0.#} × {w.DoorHeightMm:0.#} mm doorway in a {w.WallMm:0.##} mm wall." : $"Matched the {w.WallMm:0.##} mm wall, but its door isn't cut through: in Walls, set Door to Doorway (cut through).")
            : $"Matched: {w.SheetMm:0.##} mm wall sheet, {w.MountSlotMm:0.#} mm mount slots{(w.Mounts == 0 ? " (the walls have no mount points yet: add them on the Mounts tab in Walls)" : "")}.";
        Changed();
    }

    string? LastFile => _dataFolder is null ? null : Path.Combine(_dataFolder, "last.json");

    FurnitureSet? LoadLast()
    {
        try { return LastFile is { } f && File.Exists(f) ? FurnitureSet.FromJson(File.ReadAllText(f)) : null; }
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
        SetKit kit;
        try { kit = await Task.Run(() => FurnitureKit.Make(set)); }
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
            string prefix = kit.Items.Count > 1 ? FurnitureKit.Label(set, i) + ": " : "";
            all.AddRange(kit.Items[i].Findings.Concat(Placement.TooBig(kit.Items[i], _machine))
                .Where(f => i == sel || f.Level != Severity.Note)
                .Select(f => f with { Text = prefix + f.Text }));
        }
        string? bad = mine.Findings.Concat(Placement.TooBig(mine, _machine)).FirstOrDefault(f => f.Level == Severity.Bad)?.Text;
        _pieces.Show(mine.Parts.Count > 0 ? mine : null, bad ?? "Nothing to show.");
        _assembled.Show(mine.Parts.Count > 0 ? mine : null, set.ThicknessMm, bad ?? "Nothing to show.");
        _findings.Children.Clear();
        foreach (var f in all.OrderByDescending(f => f.Level)) _findings.Children.Add(FindingRow(f));
        int pieces = kit.PieceCount;
        _summary.Text = pieces == 0 ? "" :
            $"{pieces} piece{(pieces == 1 ? "" : "s")} to cut for " + ListWords(set.Items.Select((it, i) => (it.Count > 1 ? $"{it.Count} × " : "") + Lower(FurnitureKit.Label(set, i)))) +
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
            var built = _kit is not null && _kitFrom == set.ToJson() ? _kit : null;
            Func<Layer, Layer>? cut = CutRecipe is { } c ? l => c.R.Settings.ApplyTo(l) with { Material = new MaterialRef(c.M.Id, c.M.Label, c.R.Id, c.R.Title) } : null;
            Func<Layer, Layer>? etch = EtchRecipe is { } e ? l => e.R.Settings.ApplyTo(l) with { Material = new MaterialRef(e.M.Id, e.M.Label, e.R.Id, e.R.Title) } : null;
            Guid? replace = _editing is { } ed && Placement.ShapesOf(_doc.State, ed.Id).Count + _doc.State.OtherPages.Sum(p => p.Shapes.Count(x => x.AddonData.TryGetValue(Placement.IdKey, out var v) && v == ed.Id.ToString())) > 0 ? ed.Id : null;
            var before = _doc.State;
            var (kit, s, sheets, id) = await Task.Run(() =>
            {
                var kit = built ?? FurnitureKit.Make(set);
                if (!kit.Ok) throw new InvalidOperationException(kit.Items.SelectMany(i => i.Findings).First(f => f.Level == Severity.Bad).Text);
                var (s, sheets, id) = Placement.Place(before, kit, set, _machine, new PlaceOptions { CutSettings = cut, EtchSettings = etch, Replace = replace });
                return (kit, s, sheets, id);
            });
            if (!ReferenceEquals(_doc.State, before)) { _status.Text = "The design changed meanwhile: press again to put the pieces in."; return; }
            _doc.Commit(replace is null ? "Furniture" : "Update the furniture", s, Placement.ShapesOf(s, id));
            _editing = (id, set);
            _make.Content = "Update in the design";
            int pieces = kit.PieceCount;
            _status.Text = $"{(replace is null ? "Put" : "Updated")} {pieces} piece{(pieces == 1 ? "" : "s")} in the design" +
                           (sheets > 1 ? $", over {sheets} pages (a bed each)" : "") +
                           $": outlines on \"{Placement.CutLayer}\", the detail on \"{Placement.EtchLayer}\" and \"{Placement.LineLayer}\" (done first).";
        }
        catch (InvalidOperationException ex) { _status.Text = ex.Message; }
        catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException or ArithmeticException or NullReferenceException)
        {
            AppLog.Error("furniture", ex);
            _status.Text = $"Couldn't put the furniture in: {ex.Message}";
        }
        finally
        {
            _making = false;
            _make.IsEnabled = _kit?.Ok ?? true;
        }
    }
}
