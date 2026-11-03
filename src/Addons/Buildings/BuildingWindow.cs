using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using PhotonCanvas.Addon.Furniture;
using PhotonCanvas.Addon.Walls;
using PhotonCanvas.App;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Materials;
using static PhotonCanvas.App.UiKit;

namespace PhotonCanvas.Addon.Buildings;

/// <summary>
/// The building maker: pick a cottage, tavern or watchtower, change its size, storeys, door and windows,
/// its walls' texture and sheet, and the furniture in each room; see it put together in 3D and its pieces
/// flat; then every piece goes into the design, nested onto the bed, a sheet at a time.
/// </summary>
public sealed class BuildingWindow : Window
{
    readonly Document _doc;
    readonly MachineProfile _machine;
    readonly MaterialLibrary? _library;
    readonly string? _dataFolder;
    (Guid Id, BuildingSpec Spec)? _editing;
    BuildingSpec _spec;
    int _sel;

    // Building
    readonly ComboBox _kind = Combo([.. BuildingSpec.Kinds.Select(k => k.Name)]);
    readonly TextBlock _kindAbout = Hint();
    readonly ComboBox _theme = Combo([.. BuildingThemes.Names]);
    readonly TextBlock _themeAbout = Hint();
    readonly ComboBox _scale = Combo("15 mm", "28 mm", "32 mm", "54 mm");
    static readonly double[] Scales = [15, 28, 32, 54];
    readonly NumericUpDown _width = Number(30, 2000, 5, "0.#"), _depth = Number(30, 2000, 5, "0.#"), _storeys = Number(1, 4, 1, "0"), _storeyH = Number(10, 500, 1, "0.#"), _parapet = Number(8, 200, 1, "0.#");
    readonly ComboBox _top = Combo("Straight (for a roof)", "A floor on top behind battlements");
    readonly ComboBox _wayUp = Combo("Stairs", "Spiral stairs", "Ladder", "None (a plain floor)");
    Control _parapetRow = null!;
    readonly ComboBox _door = Combo("Hung on clips: it swings open", "Loose in the doorway", "Just a doorway");
    readonly ComboBox _doorShape = Combo("Square top", "Round arch", "Pointed arch", "Angled corners (sci-fi)", "Rough (cave mouth)");
    static readonly OpeningShape[] DoorShapes = [OpeningShape.Rectangle, OpeningShape.Arched, OpeningShape.Pointed, OpeningShape.Chamfered, OpeningShape.Rough];
    readonly ComboBox _doorLook = Combo("Planks", "Planks with iron studs", "Metal blast door", "Bars (a portcullis)", "Stone slab");
    readonly NumericUpDown _doorW = Number(4, 500, 0.5m, "0.#"), _doorH = Number(6, 500, 0.5m, "0.#");
    readonly NumericUpDown _winFront = Number(0, 20, 1, "0"), _winSide = Number(0, 20, 1, "0"), _winW = Number(1, 200, 0.5m, "0.#"), _winH = Number(1, 200, 0.5m, "0.#"), _sill = Number(1, 300, 0.5m, "0.#");
    readonly ComboBox _winShape = Combo("Square", "Round arch", "Pointed arch", "Round", "Arrow slit", "Angled corners (sci-fi)", "Rough (cave)"), _bars = Combo("None", "A cross", "Upright bars");

    // Floor tiles
    readonly CheckBox _tiles = new() { Content = "Floor tiles under the building (the floor tile maker's)" };
    readonly ComboBox _tileTexture = Combo([.. PhotonCanvas.Addon.FloorTiles.FloorTextures.Presets.Select(p => p.Name)]);
    readonly NumericUpDown _square = Number(5, 300, 0.5m, "0.##"), _yard = Number(0, 500, 5, "0.#");
    Control _tileRows = null!;

    // Walls
    readonly NumericUpDown _wallT = Number(0.5m, 20, 0.5m, "0.##"), _fit = Number(-0.5m, 2, 0.05m, "0.##"), _column = Number(5, 200, 1, "0.#"), _seed = Number(1, 9999, 1, "0");
    readonly CheckBox _twoSided = new() { Content = "Two panels back to back (textured inside too)" };
    readonly ComboBox _build = Combo("Boxes: a front, back, top and bottom", "Flat: one sheet thick");
    readonly NumericUpDown _wallMm = Number(6, 60, 1, "0.#");
    readonly ComboBox _winFill = Combo("Nothing (bars left in the wall)", "A frame with its bars", "A shutter, glued in", "Shutters that open");
    Control _wallMmRow = null!, _winFillRow = null!;
    readonly ComboBox _texture = Combo(), _upper = Combo(), _etch = Combo("Grooves (etched areas, deeper)", "Lines (scored, faster)");
    readonly List<string> _textures = [.. Textures.Presets.Select(p => p.Name)];
    readonly ComboBox _wallCut = Combo(), _etchWith = Combo();
    readonly TextBlock _wallCutNote = Hint();
    List<(Material M, Recipe R)?> _cuts = [], _etches = [];

    // Furniture
    readonly ListBox _list = new() { MinHeight = 140, MaxHeight = 220 };
    static readonly ItemKind[] Kinds = [.. Defaults.Kinds.Select(k => k.Kind).Where(k => k is not (ItemKind.WallDoor or ItemKind.WallShelf))];
    readonly ComboBox _addKind = Combo([.. Kinds.Select(k => Defaults.Info(k).Name)]);
    readonly Button _add = UiKit.Button("Add"), _remove = UiKit.Button("Remove");
    readonly ComboBox _itemStorey = Combo("Storey 1");
    readonly NumericUpDown _count = Number(0, 100, 1, "0"), _iw = Number(1, 1000, 0.5m, "0.#"), _id = Number(1, 1000, 0.5m, "0.#"), _ih = Number(1, 1000, 0.5m, "0.#");
    readonly TextBlock _itemAbout = Hint();
    Control _itemRows = null!;
    readonly NumericUpDown _furnT = Number(0.3m, 20, 0.5m, "0.##"), _furnFit = Number(-0.5m, 2, 0.05m, "0.##"), _grip = Number(0.02m, 0.5m, 0.05m, "0.##");
    readonly ComboBox _joints = Combo("Snug, to glue", "Push fit (grip ribs, no glue)");
    Control _gripRow = null!;
    readonly ComboBox _detail = Combo("None", "Wood grain", "Planks and grain", "Metal panels and rivets (sci-fi)", "Stone (cracks and pits)"), _furnCut = Combo();
    readonly TextBlock _furnCutNote = Hint();

    readonly TabControl _tabs = new(), _views = new();
    readonly BuildingView _view = new();
    readonly ComboBox _show = Combo("The whole building", "With the front cut away");
    readonly PiecesView _wallPieces = new(), _furnPieces = new();
    readonly TextBlock _summary = new() { TextWrapping = TextWrapping.Wrap };
    readonly StackPanel _findings = new() { Spacing = 4 };
    readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Classes = { "muted" } };
    readonly Button _make = new() { Content = "Put in the design", Classes = { "primary" }, MinWidth = 160 };
    readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(250) };
    bool _loading;
    int _generation;
    BuildingKit? _kit;
    string? _kitFrom;

    public BuildingWindow(Document doc, MachineProfile machine, MaterialLibrary? library, string? dataFolder, (Guid Id, BuildingSpec Spec)? editing)
    {
        _doc = doc;
        _machine = machine;
        _library = library;
        _dataFolder = dataFolder;
        _editing = editing;
        _spec = editing?.Spec ?? LoadLast() ?? BuildingSpec.Preset(BuildingKind.Cottage);
        Title = "Whole buildings";
        Width = 1180;
        Height = 800;
        MinWidth = 900;
        MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");

        Library();
        _tabs.Items.Add(Tab("Building", BuildingTab()));
        _tabs.Items.Add(Tab("Walls", WallsTab()));
        _tabs.Items.Add(Tab("Furniture", FurnitureTab()));
        var left = new Border { Width = 460, Padding = new Thickness(14, 10, 8, 0), Child = _tabs };

        var showRow = new Grid { ColumnDefinitions = new("Auto,220"), ColumnSpacing = 8, Margin = new Thickness(0, 6, 0, 0) };
        showRow.Children.Add(new TextBlock { Text = "Show", VerticalAlignment = VerticalAlignment.Center, Classes = { "muted" } });
        showRow.Children.Add(_show.With(c => Grid.SetColumn(c, 1)));
        var assembled = new Grid { RowDefinitions = new("Auto,*") };
        assembled.Children.Add(showRow);
        var framed = Framed(_view);
        Grid.SetRow(framed, 1);
        assembled.Children.Add(framed);
        _views.Items.Add(new TabItem { Header = "Put together", Content = assembled });
        _views.Items.Add(new TabItem { Header = "Wall pieces", Content = Framed(_wallPieces) });
        _views.Items.Add(new TabItem { Header = "Furniture pieces", Content = Framed(_furnPieces) });
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
        Load();
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

    // ---- the tabs ----

    Control BuildingTab()
    {
        _parapetRow = Field("Battlements", _parapet, tip: "How high the low wall round the top floor is, to the top of its battlements (mm).");
        return Stack(
            Explained("Building", "Picking a building starts again from it, sized for the figures chosen. Everything below can then be changed.",
                Field("Building", _kind), _kindAbout,
                Field("Figures", _scale, tip: "Changing it scales every size, the furniture's too, to suit figures that size.")),
            Explained("Theme", "A look for the whole building, the same as the other terrain makers': it sets the walls' texture, the door and windows, the roof, the floor tiles, the furniture's detail and the way up. Sizes, storeys and furniture stay as they are.",
                Field("Theme", _theme), _themeAbout),
            Explained("Size", "Measured between the corner columns' centres. Each storey is rounded to whole layers of the wall sheet, so the columns come out flush with the walls.",
                Field("Width", _width, tip: "Left to right, between the corner columns' centres (mm)."),
                Field("Depth", _depth, tip: "Front to back, between the corner columns' centres (mm)."),
                Field("Storeys", _storeys, tip: "Storeys with rooms. A floor lies on the walls between each."),
                Field("Storey height", _storeyH, tip: "How high each storey's walls are (mm)."),
                Field("Top", _top, tip: "Straight walls take a roof; battlements put a floor on the top storey with a low wall round it."),
                _parapetRow,
                Field("Way up", _wayUp, tip: "What goes up through an opening in each floor, from the stair maker. It stands in the back right corner of the storey below.")),
            Explained("Floor tiles", "Jigsaw tiles the building stands on, cut from the wall sheet, as big as its outside and any yard round it.",
                _tiles,
                _tileRows = Stack(
                    Field("Texture", _tileTexture),
                    Field("Grid square", _square, tip: "One square of the grid the tiles are laid out on (mm): 25.4 (an inch) for 28–32 mm figures."),
                    Field("Yard", _yard, tip: "How far the tiles reach past the building's outside, all round (mm)."))),
            Explained("Door", "In the middle of the front of the ground floor.",
                Field("Door", _door, tip: "Box walls hang the door on pins so it swings open, or glue it into the doorway if it's loose. Flat walls hang it on clips (from the furniture maker), or it just sits loose in the doorway."),
                Field("Shape", _doorShape, tip: "In flat walls a door that swings is made for square, arched and pointed doorways; the others get a loose door."),
                Field("Look", _doorLook, tip: "What the wall maker's door looks like (box walls, or a loose door)."),
                Field("Width", _doorW), Field("Height", _doorH)),
            Explained("Windows", "On every storey; the ground floor's front fits them round the door.",
                Field("Front and back", _winFront, tip: "Windows in the front wall and in the back wall of each storey."),
                Field("Each side", _winSide, tip: "Windows in each side wall of each storey."),
                Field("Shape", _winShape), Field("Width", _winW), Field("Height", _winH),
                Field("Sill", _sill, tip: "How high the bottom of the windows is above the floor (mm)."),
                Field("Bars", _bars),
                _winFillRow = Field("In the windows", _winFill, tip: "Box walls: a frame with the bars in it, a solid shutter glued in, or shutters that swing open.")));
    }

    Control WallsTab()
    {
        var shuffle = UiKit.Button("Shuffle");
        shuffle.Click += (_, _) => _seed.Value = (_seed.Value ?? 1) % 9999 + 1;
        _texture.ItemsSource = _textures.ToList();
        _upper.ItemsSource = new List<string> { "The same as the ground floor" }.Concat(_textures).ToList();
        return Stack(
            Explained("Sheet", "The walls, the corner columns and the floors are cut from this sheet: its thickness sets the columns' slots and the storeys' heights.",
                Field("Walls", _build, tip: "Box walls are thick and stand up on their own: a textured outside and inside, a base, a top, ends and ribs, and boxed corner columns. Flat walls are one sheet (or two) thick, in columns of stacked layers."),
                _wallMmRow = Field("Wall thickness", _wallMm, tip: "How thick a box wall is, outside to inside (mm): a whole number of sheets fits best, 12 mm for 3 mm sheet."),
                Field("Panel thickness", _wallT, tip: "The wall sheet's thickness (mm): a box wall's faces, ends and plates, the columns, floors, stairs, roof and floor tiles are cut from it."),
                _twoSided,
                Field("Slot fit", _fit, tip: "Added to the wall's thickness for the columns' slots (mm): more is looser."),
                Field("Columns", _column, tip: "How wide the corner columns are (mm)."),
                Field("Cut with", _wallCut), _wallCutNote),
            Explained("Texture", "Etched on the outside of every wall, as the wall maker does it.",
                Field("Ground floor", _texture),
                Field("Upper storeys", _upper),
                Field("Etched as", _etch),
                Field("Variation", new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 6, Children = { _seed, shuffle.With(b => Grid.SetColumn(b, 1)) } },
                    tip: "Stones, planks and grain are laid out at random: each number gives a different layout."),
                Field("Etch with", _etchWith, tip: "Settings for the texture and the furniture's detail (an engrave/fill recipe from your library).")));
    }

    Control FurnitureTab()
    {
        var addRow = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 6, Children = { _addKind, _add.With(b => { Grid.SetColumn(b, 1); b.MinWidth = 70; }) } };
        _itemRows = Stack(_itemAbout, Field("Storey", _itemStorey), Field("How many", _count, tip: "0 leaves it out."),
            Field("Width", _iw, tip: "Left to right (mm): a table's or bed's length."), Field("Depth", _id, tip: "Front to back (mm)."), Field("Height", _ih, tip: "Overall height (mm)."));
        return Stack(
            Explained("In the rooms", "The furniture maker's pieces, in the storey they stand on. Pick one to change it; for more styles, make it with Furniture instead.",
                _list, addRow, _remove, _itemRows),
            Explained("Sheet", "The furniture and a door that swings are cut from this sheet. When it's not the walls' sheet, they go on pages of their own.",
                Field("Thickness", _furnT, tip: "The furniture sheet's thickness (mm)."),
                Field("Slot fit", _furnFit, tip: "Added to the sheet's thickness for the furniture's slots (mm): more is looser."),
                Field("Joints", _joints, tip: "Snug joints want a drop of glue; push fit puts small ribs in every slot that squeeze flat, so the furniture holds without glue."),
                _gripRow = Field("Grip", _grip, tip: "How much tighter than the sheet the ribs make each joint (mm); 0.15 suits plywood and MDF."),
                Field("Faces", _detail, tip: "Grain is scored lines; planks add etched grooves between boards; metal is a panel line and rivets; stone is cracks and pits. The floors between storeys match: planks, deck plates or stone."),
                Field("Cut with", _furnCut), _furnCutNote));
    }

    // ---- the library ----

    void Library()
    {
        var mats = _library?.Materials ?? [];
        _cuts = [null, .. mats.SelectMany(m => m.Recipes.Where(r => r.Settings.Mode == LayerMode.Cut).Select(r => ((Material, Recipe)?)(m, r)))];
        _etches = [null, .. mats.SelectMany(m => m.Recipes.Where(r => r.Settings.Mode == LayerMode.Fill).Select(r => ((Material, Recipe)?)(m, r)))];
        var cutNames = _cuts.Select(c => c is { } x ? $"{x.M.Label} · {x.R.Title}" + (x.R.Settings.KerfMm > 0 ? $" (kerf {x.R.Settings.KerfMm:0.###} mm)" : "") : "As set on the cut layer").ToList();
        _wallCut.ItemsSource = cutNames;
        _furnCut.ItemsSource = cutNames.ToList();
        _etchWith.ItemsSource = _etches.Select(c => c is { } x ? $"{x.M.Label} · {x.R.Title}" : "As set on the etch layer").ToList();
        _wallCut.SelectedIndex = _furnCut.SelectedIndex = _etchWith.SelectedIndex = 0;
    }

    (Material M, Recipe R)? Recipe(ComboBox box) => box.SelectedIndex > 0 && box.SelectedIndex < _cuts.Count ? _cuts[box.SelectedIndex] : null;
    (Material M, Recipe R)? EtchRecipe => _etchWith.SelectedIndex > 0 && _etchWith.SelectedIndex < _etches.Count ? _etches[_etchWith.SelectedIndex] : null;

    void CutChosen(ComboBox box, NumericUpDown thick, NumericUpDown fit, TextBlock note)
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
        note.Text = Recipe(box) is not { } r
            ? "No kerf allowance: slots come out a little loose. Pick your material's cut settings (Laser → Tests → Cut test measures the kerf) for slots at their drawn size."
            : r.R.Settings.KerfMm > 0 ? $"{r.M.Label}: kerf {r.R.Settings.KerfMm:0.###} mm, allowed for on the cut layer so slots come out their drawn width."
            : $"{r.M.Label}: no kerf measured yet, so slots come out a little loose (run a cut test).";
        FromControls();
    }

    // ---- settings in and out ----

    void Hook()
    {
        foreach (var n in new[] { _width, _depth, _storeys, _storeyH, _parapet, _doorW, _doorH, _winFront, _winSide, _winW, _winH, _sill, _wallT, _wallMm, _fit, _column, _seed, _furnT, _furnFit, _grip, _square, _yard })
            n.ValueChanged += (_, _) => FromControls();
        _theme.SelectionChanged += (_, _) => ThemeChosen();
        foreach (var c in new[] { _top, _wayUp, _door, _doorShape, _doorLook, _winShape, _bars, _winFill, _texture, _upper, _etch, _detail, _etchWith, _joints, _tileTexture })
            c.SelectionChanged += (_, _) => FromControls();
        _twoSided.IsCheckedChanged += (_, _) => FromControls();
        _build.SelectionChanged += (_, _) => BuildChosen();
        _tiles.IsCheckedChanged += (_, _) => FromControls();
        _wallCut.SelectionChanged += (_, _) => CutChosen(_wallCut, _wallT, _fit, _wallCutNote);
        _furnCut.SelectionChanged += (_, _) => CutChosen(_furnCut, _furnT, _furnFit, _furnCutNote);
        _kind.SelectionChanged += (_, _) => KindChosen();
        _scale.SelectionChanged += (_, _) => ScaleChosen();
        _show.SelectionChanged += (_, _) => ShowChosen();
        _list.SelectionChanged += (_, _) => { if (!_loading && _list.SelectedIndex >= 0) { _sel = _list.SelectedIndex; LoadItem(); } };
        foreach (var n in new[] { _count, _iw, _id, _ih }) n.ValueChanged += (_, _) => ItemChanged();
        _itemStorey.SelectionChanged += (_, _) => ItemChanged();
        _add.Click += (_, _) => AddItem();
        _remove.Click += (_, _) => RemoveItem();
    }

    static decimal D(double v) => (decimal)v;
    static double V(NumericUpDown n, double fallback) => n.Value is { } v ? (double)v : fallback;
    static int Index(ComboBox c) => Math.Max(0, c.SelectedIndex);

    void Load()
    {
        var s = _spec;
        _loading = true;
        _kind.SelectedIndex = (int)s.Kind;
        _kindAbout.Text = BuildingSpec.Info(s.Kind).About;
        _theme.SelectedIndex = Math.Max(0, BuildingThemes.Names.ToList().IndexOf(s.Theme)); _themeAbout.Text = BuildingThemes.About(s.Theme);
        int sc = Array.FindIndex(Scales, x => Math.Abs(x - s.ScaleMm) < 1e-6);
        _scale.SelectedIndex = sc < 0 ? 1 : sc;
        _width.Value = D(s.WidthMm); _depth.Value = D(s.DepthMm); _storeys.Value = Math.Clamp(s.Storeys, 1, 4); _storeyH.Value = D(s.StoreyHeightMm);
        _top.SelectedIndex = (int)s.Top; _parapet.Value = D(s.ParapetHeightMm); _wayUp.SelectedIndex = (int)s.WayUp;
        _door.SelectedIndex = (int)s.Door;
        _doorShape.SelectedIndex = Math.Max(0, Array.IndexOf(DoorShapes, s.DoorShape)); _doorLook.SelectedIndex = (int)s.DoorLook;
        _doorW.Value = D(s.DoorWidthMm); _doorH.Value = D(s.DoorHeightMm);
        _winFront.Value = s.WindowsFront; _winSide.Value = s.WindowsSide;
        _winShape.SelectedIndex = (int)s.WindowShape; _winW.Value = D(s.WindowWidthMm); _winH.Value = D(s.WindowHeightMm); _sill.Value = D(s.SillMm);
        _bars.SelectedIndex = (int)s.Bars;
        _tiles.IsChecked = s.FloorTiles; _tileRows.IsVisible = s.FloorTiles;
        _tileTexture.SelectedIndex = Math.Max(0, PhotonCanvas.Addon.FloorTiles.FloorTextures.Presets.ToList().FindIndex(p => p.Name == s.FloorTexture));
        _square.Value = D(s.SquareMm); _yard.Value = D(s.YardMm);
        _build.SelectedIndex = s.BoxWalls ? 0 : 1; _wallMm.Value = D(s.WallMm); _winFill.SelectedIndex = (int)s.WindowFill;
        _wallT.Value = D(s.WallSheetMm); _twoSided.IsChecked = s.TwoSided; _fit.Value = D(s.FitMm); _column.Value = D(s.ColumnMm); _seed.Value = Math.Clamp(s.Seed, 1, 9999);
        _texture.SelectedIndex = Math.Max(0, _textures.IndexOf(s.Texture));
        _upper.SelectedIndex = s.UpperTexture is { Length: > 0 } up && _textures.IndexOf(up) is >= 0 and var ui ? ui + 1 : 0;
        _etch.SelectedIndex = (int)s.Etch;
        _furnT.Value = D(s.FurnitureSheetMm); _furnFit.Value = D(s.FurnitureFitMm); _detail.SelectedIndex = (int)s.Detail;
        _joints.SelectedIndex = s.FurniturePushFit ? 1 : 0; _grip.Value = D(Math.Clamp(s.FurnitureGripMm, 0.02, 0.5)); _gripRow.IsVisible = s.FurniturePushFit;
        _wallMmRow.IsVisible = _winFillRow.IsVisible = s.BoxWalls; _twoSided.IsVisible = !s.BoxWalls;
        _parapetRow.IsVisible = s.Top == BuildingTop.Battlements;
        _loading = false;
        FillList(_sel);
        Shows();
    }

    void FromControls()
    {
        if (_loading) return;
        string? upper = _upper.SelectedIndex > 0 && _upper.SelectedIndex - 1 < _textures.Count ? _textures[_upper.SelectedIndex - 1] : null;
        int storeysBefore = _spec.Storeys;
        var top = (BuildingTop)Index(_top);
        _spec = _spec with
        {
            WidthMm = V(_width, 110), DepthMm = V(_depth, 80), Storeys = (int)V(_storeys, 1), StoreyHeightMm = V(_storeyH, 48),
            Top = top, ParapetHeightMm = V(_parapet, 21), WayUp = (WayUpStyle)Index(_wayUp),
            Door = (DoorStyle)Index(_door),
            DoorShape = DoorShapes[Math.Clamp(Index(_doorShape), 0, DoorShapes.Length - 1)], DoorLook = (DoorLook)Math.Max(0, Index(_doorLook)),
            DoorWidthMm = V(_doorW, 16), DoorHeightMm = V(_doorH, 28),
            WindowsFront = (int)V(_winFront, 0), WindowsSide = (int)V(_winSide, 0),
            WindowShape = (OpeningShape)Index(_winShape), WindowWidthMm = V(_winW, 8), WindowHeightMm = V(_winH, 12), SillMm = V(_sill, 22),
            Bars = (WindowBars)Index(_bars), WindowFill = (WindowFill)Index(_winFill),
            BoxWalls = _build.SelectedIndex == 0, WallMm = V(_wallMm, 12),
            FloorTiles = _tiles.IsChecked == true, FloorTexture = PhotonCanvas.Addon.FloorTiles.FloorTextures.Presets[Math.Clamp(Index(_tileTexture), 0, PhotonCanvas.Addon.FloorTiles.FloorTextures.Presets.Count - 1)].Name,
            SquareMm = V(_square, 25.4), YardMm = V(_yard, 0),
            WallSheetMm = V(_wallT, 3), TwoSided = _twoSided.IsChecked == true, FitMm = V(_fit, 0.15), ColumnMm = V(_column, 16), Seed = (int)V(_seed, 1),
            Texture = _textures[Math.Clamp(Index(_texture), 0, _textures.Count - 1)], UpperTexture = upper, Etch = (EtchStyle)Index(_etch),
            FurnitureSheetMm = V(_furnT, 2), FurnitureFitMm = V(_furnFit, 0.1), Detail = (WoodDetail)Index(_detail),
            FurniturePushFit = _joints.SelectedIndex == 1, FurnitureGripMm = V(_grip, 0.15),
        };
        _gripRow.IsVisible = _spec.FurniturePushFit;
        _wallMmRow.IsVisible = _winFillRow.IsVisible = _spec.BoxWalls;
        _twoSided.IsVisible = !_spec.BoxWalls;
        _parapetRow.IsVisible = top == BuildingTop.Battlements;
        _tileRows.IsVisible = _spec.FloorTiles;
        if (_spec.Storeys != storeysBefore) FillList(_sel);
        Shows();
        Changed();
    }

    /// <summary>A theme picked: the building's looks change to it (see <see cref="BuildingThemes"/>).</summary>
    void ThemeChosen()
    {
        _themeAbout.Text = BuildingThemes.About(BuildingThemes.Names[Math.Clamp(_theme.SelectedIndex, 0, BuildingThemes.Names.Count - 1)]);
        if (_loading) return;
        _spec = BuildingThemes.Apply(_spec, BuildingThemes.Names[Math.Clamp(_theme.SelectedIndex, 0, BuildingThemes.Names.Count - 1)]);
        Load();
        Changed();
    }

    /// <summary>Box or flat walls picked: box walls get corner columns wide enough for them.</summary>
    void BuildChosen()
    {
        if (_loading) return;
        if (_build.SelectedIndex == 0)
        {
            double need = Math.Ceiling(_spec.NarrowestBoxColumn + 1);
            if (V(_column, 16) < need) { _loading = true; _column.Value = D(need); _loading = false; }
        }
        FromControls();
    }

    void KindChosen()
    {
        if (_loading) return;
        var kind = (BuildingKind)Index(_kind);
        // Start again from the building, at the figures chosen; the sheets stay as they are.
        var s = _spec.BoxWalls ? BuildingSpec.Preset(kind) : BuildingSpec.FlatPreset(kind);
        s = s.Scaled(_spec.ScaleMm / s.ScaleMm);
        _spec = s with
        {
            WallSheetMm = _spec.WallSheetMm, TwoSided = _spec.TwoSided, FitMm = _spec.FitMm, WindowFill = _spec.WindowFill,
            FurnitureSheetMm = _spec.FurnitureSheetMm, FurnitureFitMm = _spec.FurnitureFitMm, FurniturePushFit = _spec.FurniturePushFit, FurnitureGripMm = _spec.FurnitureGripMm, Seed = _spec.Seed, Etch = _spec.Etch,
        };
        _sel = 0;
        Load();
        Changed();
    }

    void ScaleChosen()
    {
        if (_loading) return;
        double to = Scales[Math.Clamp(_scale.SelectedIndex, 0, Scales.Length - 1)], k = to / Math.Max(1, _spec.ScaleMm);
        if (Math.Abs(k - 1) < 1e-9) return;
        _spec = _spec.Scaled(k) with { ScaleMm = to };
        Load();
        Changed();
    }

    /// <summary>The "Show" choices: the whole building, the front cut away, then each storey on its own.</summary>
    void Shows()
    {
        bool was = _loading;
        _loading = true;
        int at = Math.Max(0, _show.SelectedIndex);
        var items = new List<string> { "The whole building", "With the front cut away" };
        int storeys = Math.Clamp(_spec.Storeys, 1, 6);
        if (storeys > 1 || _spec.Top == BuildingTop.Battlements)
        {
            for (int k = 0; k < storeys; k++) items.Add($"Storey {k + 1} on its own");
            if (_spec.Top == BuildingTop.Battlements) items.Add("The top on its own");
        }
        if (_show.ItemsSource is not List<string> old || !old.SequenceEqual(items)) _show.ItemsSource = items;
        _show.SelectedIndex = Math.Min(at, items.Count - 1);
        _loading = was;
    }

    void ShowChosen()
    {
        if (_loading) return;
        int i = Math.Max(0, _show.SelectedIndex);
        if (i == 0) _view.SetMode(ViewMode.Whole);
        else if (i == 1) _view.SetMode(ViewMode.CutAway);
        else _view.SetMode(ViewMode.Storey, i - 2);
    }

    // ---- the furniture list ----

    void FillList(int select)
    {
        bool was = _loading;
        _loading = true;
        int storeys = Math.Clamp(_spec.Storeys, 1, 6);
        _itemStorey.ItemsSource = Enumerable.Range(1, storeys).Select(k => storeys == 1 ? "Ground floor" : $"Storey {k}").ToList();
        _list.ItemsSource = _spec.Items.Select((r, i) =>
            $"{_spec.Label(i)}{(r.Item.Count != 1 ? $" × {r.Item.Count}" : "")}{(storeys > 1 ? $"  (storey {Math.Clamp(r.Storey, 0, storeys - 1) + 1})" : "")}").ToList();
        _sel = _spec.Items.Count == 0 ? -1 : Math.Clamp(select, 0, _spec.Items.Count - 1);
        _list.SelectedIndex = _sel;
        _remove.IsEnabled = _itemRows.IsEnabled = _sel >= 0;
        _loading = was;
        LoadItem();
    }

    void LoadItem()
    {
        if (_sel < 0 || _sel >= _spec.Items.Count) { _itemRows.IsVisible = false; return; }
        _itemRows.IsVisible = true;
        var r = _spec.Items[_sel];
        bool was = _loading;
        _loading = true;
        _itemAbout.Text = Defaults.Info(r.Item.Kind).About;
        _itemStorey.SelectedIndex = Math.Clamp(r.Storey, 0, Math.Clamp(_spec.Storeys, 1, 6) - 1);
        _count.Value = Math.Max(0, r.Item.Count);
        _iw.Value = D(r.Item.WidthMm); _id.Value = D(Math.Max(1, r.Item.DepthMm)); _ih.Value = D(r.Item.HeightMm);
        _loading = was;
    }

    void ItemChanged()
    {
        if (_loading || _sel < 0 || _sel >= _spec.Items.Count) return;
        var r = _spec.Items[_sel];
        var items = _spec.Items.ToList();
        items[_sel] = r with
        {
            Storey = Index(_itemStorey),
            Item = r.Item with { Count = (int)V(_count, 1), WidthMm = V(_iw, r.Item.WidthMm), DepthMm = V(_id, r.Item.DepthMm), HeightMm = V(_ih, r.Item.HeightMm) },
        };
        bool relist = items[_sel].Storey != r.Storey || items[_sel].Item.Count != r.Item.Count;
        _spec = _spec with { Items = items };
        if (relist) FillList(_sel);
        Changed();
    }

    void AddItem()
    {
        var kind = Kinds[Math.Clamp(_addKind.SelectedIndex, 0, Kinds.Length - 1)];
        var it = BuildingSpec.ScaledItem(Defaults.For(kind), _spec.ScaleMm / 28);
        int storey = _sel >= 0 && _sel < _spec.Items.Count ? _spec.Items[_sel].Storey : 0;
        _spec = _spec with { Items = [.. _spec.Items, new RoomItem { Item = it, Storey = storey }] };
        FillList(_spec.Items.Count - 1);
        Changed();
    }

    void RemoveItem()
    {
        if (_sel < 0 || _sel >= _spec.Items.Count) return;
        var items = _spec.Items.ToList();
        items.RemoveAt(_sel);
        _spec = _spec with { Items = items };
        FillList(Math.Min(_sel, items.Count - 1));
        Changed();
    }

    string? LastFile => _dataFolder is null ? null : Path.Combine(_dataFolder, "last.json");

    BuildingSpec? LoadLast()
    {
        try { return LastFile is { } f && File.Exists(f) ? BuildingSpec.FromJson(File.ReadAllText(f)) : null; }
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
        _status.Text = "Making the pieces…";
        try { kit = await Task.Run(() => BuildingKitMaker.Make(spec)); }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or ArithmeticException)
        {
            if (gen == _generation) { _view.Show(null, e.Message); _wallPieces.Show(null, e.Message); _furnPieces.Show(null, e.Message); _status.Text = ""; }
            return;
        }
        if (gen != _generation) return;
        _status.Text = "";
        _kit = kit;
        _kitFrom = spec.ToJson();
        var all = kit.Findings.Concat(Placement.TooBig(kit, _machine)).ToList();
        string? bad = all.FirstOrDefault(f => f.Level == Severity.Bad)?.Text;
        _view.Show(kit.Solids.Count > 0 ? kit : null, bad ?? "Nothing to show.");
        _wallPieces.Show([.. kit.Pieces.Where(p => p.Sheet == Sheet.Walls)], bad ?? "No wall pieces.");
        _furnPieces.Show([.. kit.Pieces.Where(p => p.Sheet == Sheet.Furniture)], bad ?? "No furniture: add some on the Furniture tab.");
        _findings.Children.Clear();
        foreach (var f in all.OrderByDescending(f => f.Level)) _findings.Children.Add(FindingRow(f));
        int walls = kit.Count(Sheet.Walls), furniture = kit.Count(Sheet.Furniture);
        _summary.Text = kit.PieceCount == 0 ? "" :
            $"{walls} piece{(walls == 1 ? "" : "s")} from the {spec.WallSheetMm:0.##} mm wall sheet" +
            (furniture > 0 ? $" and {furniture} from the {spec.FurnitureSheetMm:0.##} mm furniture sheet" : "") + ".";
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
            var options = new PlaceOptions
            {
                WallCut = Apply(Recipe(_wallCut)), FurnitureCut = Apply(Recipe(_furnCut)), EtchSettings = Apply(EtchRecipe),
                Replace = _editing is { } ed && Placement.CountEverywhere(_doc.State, ed.Id) > 0 ? ed.Id : null,
            };
            var before = _doc.State;
            var (kit, s, pages, id) = await Task.Run(() =>
            {
                var kit = built ?? BuildingKitMaker.Make(spec);
                if (!kit.Ok) throw new InvalidOperationException(kit.Findings.First(f => f.Level == Severity.Bad).Text);
                var (s, pages, id) = Placement.Place(before, kit, spec, _machine, options);
                return (kit, s, pages, id);
            });
            if (!ReferenceEquals(_doc.State, before)) { _status.Text = "The design changed meanwhile: press again to put the pieces in."; return; }
            string name = BuildingSpec.Info(spec.Kind).Name;
            _doc.Commit(options.Replace is null ? name : $"Update the {name.ToLowerInvariant()}", s, Placement.ShapesOf(s, id));
            _editing = (id, spec);
            _make.Content = "Update in the design";
            _status.Text = $"{(options.Replace is null ? "Put" : "Updated")} {kit.PieceCount} pieces in the design over {pages} page{(pages == 1 ? "" : "s")} (a bed each)" +
                           $": outlines on \"{Placement.CutLayer}\", the texture and detail on \"{Placement.EtchLayer}\" and \"{Placement.LineLayer}\" (done first).";
        }
        catch (InvalidOperationException ex) { _status.Text = ex.Message; }
        catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException or ArithmeticException or NullReferenceException)
        {
            AppLog.Error("buildings", ex);
            _status.Text = $"Couldn't put the building in: {ex.Message}";
        }
        finally
        {
            _making = false;
            _make.IsEnabled = _kit?.Ok ?? true;
        }
    }
}
