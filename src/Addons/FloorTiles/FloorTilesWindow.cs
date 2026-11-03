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

namespace PhotonCanvas.Addon.FloorTiles;

/// <summary>
/// The floor tile maker: the grid square, a board or loose tiles, the texture, how the tiles lock
/// together, with a preview of the tiles put together; then the pieces go into the design, nested onto
/// the bed.
/// </summary>
public sealed class FloorTilesWindow : Window
{
    readonly Document _doc;
    readonly MachineProfile _machine;
    readonly MaterialLibrary? _library;
    readonly string? _dataFolder;
    readonly CustomTextures _own;
    (Guid Id, TileSpec Spec)? _editing;

    // Tiles
    readonly NumericUpDown _square = Number(5, 300, 0.1m, "0.##"), _thick = Number(0.5m, 20, 0.5m, "0.##");
    readonly ComboBox _mode = Combo("A board, split into tiles", "Loose tiles");
    readonly NumericUpDown _boardA = Number(0.5m, 200, 1, "0.##"), _boardD = Number(0.5m, 200, 1, "0.##"), _tileA = Number(1, 20, 1, "0"), _tileD = Number(1, 20, 1, "0");
    readonly CheckBox _outside = new() { Content = "Joints round the outside of the board too" };
    readonly TextBlock _outsideAbout = Hint();
    readonly StackPanel _sizes = new() { Spacing = 6 };
    readonly List<SizeRow> _sizeRows = [];
    Control _boardRows = null!, _tilesRows = null!;
    readonly ComboBox _cutWith = Combo(), _etchWith = Combo();
    readonly TextBlock _cutNote = Hint(), _wallsNote = Hint();
    List<(Material M, Recipe R)?> _cuts = [], _etches = [];

    // Texture
    readonly ComboBox _theme = Combo([.. FloorThemes.Names]);
    readonly TextBlock _themeAbout = Hint();
    readonly ComboBox _texture = Combo();
    readonly List<string> _textureNames = [];
    readonly NumericUpDown _sizeA = Number(0, 300, 0.5m, "0.##"), _sizeB = Number(0.5m, 300, 0.5m, "0.##"), _groove = Number(0.1m, 3, 0.1m, "0.##"), _seed = Number(1, 9999, 1, "0"), _cracks = Number(0, 100, 5, "0");
    readonly TextBlock _sizeALabel = Label("Size"), _sizeBLabel = Label("Size"), _textureAbout = Hint();
    Control _sizeARow = null!, _sizeBRow = null!, _grooveRow = null!, _seedRow = null!, _cracksRow = null!;
    readonly NumericUpDown _wear = Number(0, 100, 5, "0"), _detail = Number(0.2m, 5, 0.1m, "0.##");
    Control _wearRow = null!, _detailRow = null!;
    readonly ComboBox _etch = Combo("Grooves (etched areas, deeper)", "Lines (scored, faster)", "Relief (carved in depth, slowest)");
    readonly ComboBox _grid = Combo("Not marked (the texture shows them)", "Scored lines", "Etched grooves");

    // Joints
    readonly ComboBox _join = Combo("Jigsaw edges", "Clips (bowties)", "Magnets", "None (straight edges)");
    readonly NumericUpDown _tab = Number(1, 40, 0.5m, "0.#"), _fit = Number(-1, 2, 0.05m, "0.##"), _magnet = Number(1, 30, 0.5m, "0.#");
    readonly TextBlock _tabLabel = Label("Tab size"), _joinAbout = Hint();
    readonly CheckBox _base = new() { Content = "A base layer under each tile (glued on)" };
    Control _tabRow = null!, _magnetRow = null!;

    readonly TilePreview _preview = new();
    readonly TextBlock _summary = new() { TextWrapping = TextWrapping.Wrap };
    readonly StackPanel _findings = new() { Spacing = 4 };
    readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Classes = { "muted" } };
    readonly Button _make = new() { Content = "Put in the design", Classes = { "primary" }, MinWidth = 160 };
    readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(200) };
    bool _loading;
    int _generation;
    TileKit? _kit;
    TileSpec? _kitFrom;

    public FloorTilesWindow(Document doc, MachineProfile machine, MaterialLibrary? library, string? dataFolder, (Guid Id, TileSpec Spec)? editing)
    {
        _doc = doc;
        _machine = machine;
        _library = library;
        _dataFolder = dataFolder;
        // Your own textures are the wall maker's: made there, kept in its data folder, listed here too.
        _own = new CustomTextures(dataFolder is null ? null : Path.Combine(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(dataFolder)) ?? dataFolder, "walls"));
        _editing = editing;
        Title = "Floor tiles";
        Width = 1080;
        Height = 760;
        MinWidth = 820;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");

        Library();
        var tabs = new TabControl
        {
            Items =
            {
                Tab("Tiles", TilesTab()),
                Tab("Texture", TextureTab()),
                Tab("Joints", JointsTab()),
            },
        };
        var left = new Border { Width = 440, Padding = new Thickness(14, 10, 8, 0), Child = tabs };

        var results = new StackPanel { Spacing = 8, Children = { _summary, _findings } };
        var right = new Grid { RowDefinitions = new("*,Auto"), Margin = new Thickness(0, 14, 14, 0), RowSpacing = 8 };
        right.Children.Add(new Border { CornerRadius = new(8), ClipToBounds = true, BorderBrush = Brush("Line"), BorderThickness = new(1), Child = _preview });
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
        Load(editing?.Spec ?? LoadLast() ?? new TileSpec());
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

    Control TilesTab()
    {
        _boardRows = Stack(
            Row(Small("Squares across", _boardA, tip: "How wide the board is, in grid squares. A part square (4.5) makes the last column narrower."), Small("Squares down", _boardD, tip: "How deep the board is, in grid squares.")),
            Row(Small("Tile up to (across)", _tileA, tip: "The biggest tile the board is cut into, in squares across."), Small("Tile up to (down)", _tileD, tip: "The biggest tile, in squares down.")));
        var add = UiKit.Button("Add a tile size");
        add.HorizontalAlignment = HorizontalAlignment.Left;
        add.Click += (_, _) => { AddSizeRow(new TileSize { Count = 1 }); Changed(); };
        _tilesRows = Stack(
            new Grid { ColumnDefinitions = new("70,70,60,*,32"), ColumnSpacing = 6, Children = { Muted("Across"), Muted("Down").With(t => Grid.SetColumn(t, 1)), Muted("How many").With(t => Grid.SetColumn(t, 2)), Muted("Straight sides").With(t => Grid.SetColumn(t, 3)) } },
            _sizes, add);
        var match = UiKit.Button("Match the walls in the design", tip: "Takes the sheet thickness and slot fit from walls made with the wall maker.");
        match.HorizontalAlignment = HorizontalAlignment.Left;
        match.Click += (_, _) => MatchWalls();
        return Stack(
            Explained("Grid", "Tiles are made on a square grid, so they line up with each other and with your game's movement squares. 25.4 mm (an inch) suits 28–32 mm figures.",
                Field("Grid square", _square, tip: "One square of the grid (mm)."),
                Field("Make", _mode, tip: "A whole board, split into tiles (straight round the outside, or with joints there too: see Joints), or tiles of the sizes you list."),
                _boardRows, _tilesRows),
            Explained("Sheet", "Picking your material's settings brings its thickness and, from your cut test, its kerf and slot fit.",
                Field("Thickness", _thick, tip: "The sheet's thickness (mm)."),
                Field("Cut with", _cutWith),
                Field("Etch with", _etchWith, tip: "Settings for the texture (an engrave/fill recipe from your library)."),
                _cutNote,
                match, _wallsNote));
    }

    Control TextureTab()
    {
        _sizeARow = Line(_sizeALabel, _sizeA);
        _sizeBRow = Line(_sizeBLabel, _sizeB);
        _grooveRow = Field("Groove", _groove, tip: "How wide the etched gaps between stones, boards or bricks are (mm).");
        var shuffle = UiKit.Button("Shuffle");
        shuffle.HorizontalAlignment = HorizontalAlignment.Left;
        shuffle.Click += (_, _) => _seed.Value = (_seed.Value ?? 1) % 9999 + 1;
        _seedRow = Field("Variation", new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 6, Children = { _seed, shuffle.With(b => Grid.SetColumn(b, 1)) } },
            tip: "Stones and boards are laid out at random, differently on every tile: each number gives a different set.");
        _cracksRow = Field("Cracks %", _cracks, tip: "How many of the stones have a crack scored across them.");
        _wearRow = Field("Wear %", _wear, tip: "How weathered the texture looks: 0 is new and crisp, 50 the usual look, 100 ancient and battered (more nicks, chipped corners, pits, cracks and scratches).");
        _detailRow = Field("Detail size", _detail, tip: "Size of the smallest marks in mm: pits, nicks, wood grain, bolts, vent slats and greebles.");
        return Stack(
            Explained("Theme", "A look to start from, the same as the wall maker's, so floors match the walls, roofs and scatter of that theme. It picks the texture below; change it afterwards if you like.",
                Field("Theme", _theme),
                _themeAbout),
            Explained("Texture", "Etched on the face of every tile. Your own textures, made in the wall maker (Walls → Texture), are listed after the presets.",
                Field("Texture", _texture),
                _textureAbout,
                _sizeARow, _sizeBRow, _grooveRow, _seedRow, _cracksRow,
                _wearRow, _detailRow,
                Field("Etch as", _etch, tip: "Grooves etch the gaps as areas (deeper, the most real). Lines score round each stone or board (much quicker). Relief carves a greyscale depth map in slices: each piece at its own height, edges rounded into the grooves (the most real, and much slower).")),
            Explained("Grid squares", "Mark the squares on the tiles for games that count movement. Dungeon flagstones follow the grid already.",
                Field("Mark squares", _grid)));
    }

    Control JointsTab()
    {
        _tabRow = Line(_tabLabel, _tab, "How big a jigsaw tab's round head is, or how wide a clip is (mm). Bigger is stronger but needs bigger squares.");
        _magnetRow = Field("Magnet size", _magnet, tip: "The round magnets' diameter (mm). Use magnets no thicker than the sheet.");
        return Stack(
            Explained("How they lock together", "Every square of every edge gets the same joint, so any tile fits against any other on the grid, turned any way round.",
                Field("Joints", _join),
                _joinAbout,
                _tabRow,
                _magnetRow,
                Field("Fit", _fit, tip: "Play between a tab or clip and its socket (mm): more is looser. With your material's cut settings picked, the kerf is allowed for too.")),
            Explained("Outside edges", "Straight round the outside, the board ends neatly. With joints there too, it locks onto other boards made on the same grid, to build a bigger floor from several.",
                _outside,
                _outsideAbout),
            Explained("Base layer", "A second layer glued under each tile makes them thicker and flatter. The joints then go in the base layer only, under a plain top that hides them; magnets always have one.",
                _base));
    }

    /// <summary>One line of the loose tiles' list.</summary>
    sealed class SizeRow
    {
        public readonly NumericUpDown Across = Number(0.5m, 100, 1, "0.##"), Down = Number(0.5m, 100, 1, "0.##"), Count = Number(0, 400, 1, "0");
        public readonly ComboBox Plain = Combo("None", "One side (edge)", "Two (corner)", "Two opposite", "Three (end)", "All four");
        public readonly Button Remove = new() { Content = "✕", Padding = new Thickness(6, 2), MinWidth = 0 };
        public Grid Row = null!;
        public TileSize Value => new()
        {
            Across = V(Across, 2), Down = V(Down, 2), Count = (int)V(Count, 1), Plain = (PlainSides)Math.Max(0, Plain.SelectedIndex),
        };
    }

    void AddSizeRow(TileSize t)
    {
        var r = new SizeRow();
        r.Across.Value = D(t.Across); r.Down.Value = D(t.Down); r.Count.Value = t.Count; r.Plain.SelectedIndex = (int)t.Plain;
        r.Row = new Grid { ColumnDefinitions = new("70,70,60,*,32"), ColumnSpacing = 6 };
        Control[] cells = [r.Across, r.Down, r.Count, r.Plain, r.Remove];
        for (int i = 0; i < cells.Length; i++) { Grid.SetColumn(cells[i], i); r.Row.Children.Add(cells[i]); }
        ToolTip.SetTip(r.Plain, "Sides left straight, for the outside of a layout: one side for an edge, two for a corner. Turn the tile to put them where you want.");
        ToolTip.SetTip(r.Remove, "Take this size off the list");
        foreach (var n in new[] { r.Across, r.Down, r.Count }) n.ValueChanged += (_, _) => Changed();
        r.Plain.SelectionChanged += (_, _) => Changed();
        r.Remove.Click += (_, _) =>
        {
            _sizeRows.Remove(r);
            _sizes.Children.Remove(r.Row);
            Changed();
        };
        _sizeRows.Add(r);
        _sizes.Children.Add(r.Row);
    }

    // ---- the library and the walls ----

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
            int same = _etches.FindIndex(e => e is { } x && x.M.Id == c.M.Id);
            if (same > 0 && EtchRecipe is null) _etchWith.SelectedIndex = same;
            _loading = false;
        }
        _cutNote.Text = CutRecipe is not { } r
            ? "No kerf allowance: the laser burns each edge back by half its kerf, so tabs come out a little loose. Pick your material's cut settings (Laser → Tests → Cut test measures the kerf) for joints at their drawn size."
            : r.R.Settings.KerfMm > 0 ? $"{r.M.Label}: kerf {r.R.Settings.KerfMm:0.###} mm, allowed for on the cut layer so tabs and sockets come out their drawn size."
            : $"{r.M.Label}: no kerf measured yet, so tabs come out a little loose (run a cut test).";
        Changed();
    }

    /// <summary>The sheet and fit of walls in the design (the wall maker's settings saved on them).</summary>
    void MatchWalls()
    {
        WallSpec? walls = null;
        foreach (var shapes in new[] { (IEnumerable<Shape>)_doc.State.Shapes }.Concat(_doc.State.OtherPages.Select(p => (IEnumerable<Shape>)p.Shapes)))
        {
            var withSpec = shapes.Where(x => x.AddonData.ContainsKey(Placement.SpecKey)).ToList();
            var pick = withSpec.FirstOrDefault(x => _doc.Selection.Contains(x.Id)) ?? withSpec.FirstOrDefault();
            if (pick is not null && WallSpec.FromJson(pick.AddonData[Placement.SpecKey]) is { } w) { walls = w; break; }
        }
        if (walls is null) { _wallsNote.Text = "There are no walls from the wall maker in the design."; return; }
        _thick.Value = (decimal)Math.Clamp(walls.ThicknessMm, 0.5, 20);
        _fit.Value = (decimal)Math.Clamp(walls.FitMm, -1, 2);
        _wallsNote.Text = $"From the walls: {walls.ThicknessMm:0.##} mm sheet, {walls.FitMm:0.##} mm fit.";
    }

    // ---- settings in and out ----

    void Hook()
    {
        foreach (var n in new[] { _square, _thick, _boardA, _boardD, _tileA, _tileD, _sizeA, _sizeB, _groove, _seed, _cracks, _tab, _fit, _magnet, _wear, _detail })
            n.ValueChanged += (_, _) => Changed();
        foreach (var c in new[] { _mode, _etch, _grid, _join, _etchWith })
            c.SelectionChanged += (_, _) => Changed();
        foreach (var c in new[] { _outside, _base })
            c.IsCheckedChanged += (_, _) => Changed();
        _cutWith.SelectionChanged += (_, _) => CutChosen();
        _texture.SelectionChanged += (_, _) => TextureChosen();
        _theme.SelectionChanged += (_, _) => ThemeChosen();
    }

    string ThemeName => FloorThemes.Names[Math.Clamp(_theme.SelectedIndex, 0, FloorThemes.Names.Count - 1)];

    void ThemeChosen()
    {
        _themeAbout.Text = FloorThemes.About(ThemeName);
        if (_loading) return;
        Load(FloorThemes.Apply(Current(), ThemeName));
        Changed();
    }

    void FillTextures(string select)
    {
        _textureNames.Clear();
        _textureNames.AddRange(FloorTextures.Presets.Select(p => p.Name));
        _textureNames.AddRange(_own.All.Select(t => t.Name).Where(n => !_textureNames.Contains(n)));
        if (!_textureNames.Contains(select) && _editing?.Spec.Custom is { } c && c.Name == select) _textureNames.Add(select);
        bool was = _loading;
        _loading = true;
        _texture.ItemsSource = _textureNames.Select(n => FloorTextures.Preset(n) is null ? $"{n} (yours)" : n).ToList();
        _texture.SelectedIndex = Math.Max(0, _textureNames.IndexOf(select));
        _loading = was;
        ShowTextureRows();
    }

    string TextureName => _texture.SelectedIndex >= 0 && _texture.SelectedIndex < _textureNames.Count ? _textureNames[_texture.SelectedIndex] : FloorTextures.Presets[0].Name;

    CustomTexture? OwnTexture => FloorTextures.Preset(TextureName) is null ? _own.Get(TextureName) ?? (_editing?.Spec.Custom is { } c && c.Name == TextureName ? c : null) : null;

    void TextureChosen()
    {
        if (_loading) return;
        _loading = true;
        if (FloorTextures.Preset(TextureName) is { } p)
        {
            var (a, b) = FloorTextures.SizesFor(p, V(_square, 25.4));
            if (p.SizeALabel is not null) _sizeA.Value = D(Math.Round(a, 2));
            if (p.SizeBLabel is not null) _sizeB.Value = D(Math.Round(b, 2));
            if (p.UsesGroove) _groove.Value = D(p.Groove);
            _detail.Value = D(p.Detail);
        }
        else if (OwnTexture is { } own) _sizeA.Value = D(Math.Clamp(own.TileWidthMm, 0.5, 300));
        _loading = false;
        ShowTextureRows();
        Changed();
    }

    void ShowTextureRows()
    {
        if (FloorTextures.Preset(TextureName) is { } p)
        {
            _sizeALabel.Text = p.SizeALabel ?? "";
            _sizeBLabel.Text = p.SizeBLabel ?? "";
            _sizeARow.IsVisible = p.SizeALabel is not null;
            _sizeBRow.IsVisible = p.SizeBLabel is not null;
            _grooveRow.IsVisible = p.UsesGroove;
            _seedRow.IsVisible = p.Random;
            _cracksRow.IsVisible = p.Stone;
            _wearRow.IsVisible = _detailRow.IsVisible = p.Name is not (FloorTextures.Plain or "Tread plate" or "Grating" or "Sand");
            _textureAbout.Text = p.About;
        }
        else
        {
            _sizeALabel.Text = "Tile width";
            _sizeARow.IsVisible = true;
            _sizeBRow.IsVisible = _grooveRow.IsVisible = _seedRow.IsVisible = _cracksRow.IsVisible = false;
            _wearRow.IsVisible = _detailRow.IsVisible = false;
            var own = OwnTexture;
            _textureAbout.Text = own is null ? "" : $"Your texture: a {own.TileWidthMm:0.#} × {own.TileHeightMm:0.#} mm tile, scaled to the tile width, repeated in rows.";
        }
    }

    void ShowRows()
    {
        bool board = _mode.SelectedIndex == 0;
        _boardRows.IsVisible = board;
        _tilesRows.IsVisible = !board;
        var join = (TileJoin)Math.Max(0, _join.SelectedIndex);
        _tabRow.IsVisible = join is TileJoin.Jigsaw or TileJoin.Clips;
        _tabLabel.Text = join == TileJoin.Clips ? "Clip size" : "Tab size";
        _magnetRow.IsVisible = join == TileJoin.Magnets;
        _base.IsEnabled = join != TileJoin.Magnets;
        _outside.IsVisible = board;
        _outside.IsEnabled = join != TileJoin.None;
        _outsideAbout.Text = !board
            ? "Loose tiles have joints on every side but the straight sides you pick for each size (Tiles tab): pick None for joints all round."
            : _outside.IsChecked == true ? "The tiles round the outside get joints on their outer sides as well, the same as between tiles." : "";
        _outsideAbout.IsVisible = _outsideAbout.Text.Length > 0;
        _joinAbout.Text = join switch
        {
            TileJoin.Jigsaw => "A jigsaw tab and a socket on every square of every edge. They push together from above and hold the floor flat.",
            TileJoin.Clips => "A half-bowtie socket in the middle of every square of every edge, and bowtie clips to push in where two tiles meet. Take the clips out to take the floor apart.",
            TileJoin.Magnets => "Two round magnets on every square of every edge, in a base layer under the tile: north up in one, south up in the other, so tiles pull together whichever way they're turned.",
            _ => "Plain straight edges: the tiles just sit side by side.",
        };
    }

    static decimal D(double v) => (decimal)v;
    static double V(NumericUpDown n, double fallback) => n.Value is { } v ? (double)v : fallback;

    void Load(TileSpec s)
    {
        _loading = true;
        _square.Value = D(s.SquareMm); _thick.Value = D(s.ThicknessMm); _mode.SelectedIndex = (int)s.Mode;
        _boardA.Value = D(s.BoardAcross); _boardD.Value = D(s.BoardDown); _tileA.Value = Math.Clamp(s.TileAcross, 1, 20); _tileD.Value = Math.Clamp(s.TileDown, 1, 20);
        _outside.IsChecked = !s.PlainBorder;
        _sizeRows.Clear();
        _sizes.Children.Clear();
        foreach (var t in s.Tiles) AddSizeRow(t);
        _sizeA.Value = D(s.SizeA); _sizeB.Value = D(Math.Max(0.5, s.SizeB)); _groove.Value = D(s.GrooveMm); _seed.Value = Math.Clamp(s.Seed, 1, 9999); _cracks.Value = D(s.CracksPct);
        _wear.Value = D(Math.Clamp(s.Wear, 0, 1) * 100); _detail.Value = D(Math.Clamp(s.DetailMm > 0 ? s.DetailMm : FloorTextures.Preset(s.Texture)?.Detail ?? 0.6, 0.2, 5));
        _etch.SelectedIndex = s.Relief ? 2 : (int)s.Etch; _grid.SelectedIndex = (int)s.Grid;
        _theme.SelectedIndex = Math.Max(0, FloorThemes.Names.ToList().IndexOf(s.Theme)); _themeAbout.Text = FloorThemes.About(s.Theme);
        _join.SelectedIndex = (int)s.Join; _tab.Value = D(s.TabMm); _fit.Value = D(s.FitMm); _magnet.Value = D(s.MagnetMm); _base.IsChecked = s.BaseLayer;
        _loading = false;
        FillTextures(s.Texture);
        ShowRows();
        CutChosen();
    }

    TileSpec Current() => new()
    {
        SquareMm = V(_square, 25.4), ThicknessMm = V(_thick, 3), Mode = (TileMode)Math.Max(0, _mode.SelectedIndex),
        BoardAcross = V(_boardA, 6), BoardDown = V(_boardD, 6), TileAcross = (int)V(_tileA, 2), TileDown = (int)V(_tileD, 2), PlainBorder = _outside.IsChecked != true,
        Tiles = [.. _sizeRows.Select(r => r.Value)],
        Theme = ThemeName, Texture = TextureName, Custom = OwnTexture, SizeA = V(_sizeA, 50), SizeB = V(_sizeB, 0), GrooveMm = V(_groove, 0.8), Seed = (int)V(_seed, 1), CracksPct = V(_cracks, 15),
        Wear = V(_wear, 50) / 100, DetailMm = V(_detail, 0.6),
        Etch = _etch.SelectedIndex == 1 ? EtchStyle.Lines : EtchStyle.Grooves, Relief = _etch.SelectedIndex == 2, Grid = (GridMarks)Math.Max(0, _grid.SelectedIndex),
        Join = (TileJoin)Math.Max(0, _join.SelectedIndex), TabMm = V(_tab, 5), FitMm = V(_fit, 0.15), MagnetMm = V(_magnet, 5), BaseLayer = _base.IsChecked == true,
    };

    string? LastFile => _dataFolder is null ? null : Path.Combine(_dataFolder, "last.json");

    TileSpec? LoadLast()
    {
        try { return LastFile is { } f && File.Exists(f) ? TileSpec.FromJson(File.ReadAllText(f)) : null; }
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

    async Task Rebuild()
    {
        int gen = ++_generation;
        var spec = Current();
        TileKit kit;
        try { kit = await Task.Run(() => TileKitMaker.Make(spec)); }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or ArithmeticException)
        {
            if (gen == _generation) _preview.Show(null, e.Message);
            return;
        }
        if (gen != _generation) return;
        _kit = kit;
        _kitFrom = spec;
        var findings = kit.Findings.Concat(TilePlacement.TooBig(kit, _machine)).ToList();
        _preview.Show(kit.Parts.Count > 0 ? kit : null, findings.FirstOrDefault(f => f.Level == Severity.Bad)?.Text ?? "Nothing to show.");
        _findings.Children.Clear();
        foreach (var f in findings.OrderByDescending(f => f.Level)) _findings.Children.Add(FindingRow(f));
        int tiles = kit.Tiles.Count(), pieces = kit.PieceCount;
        _summary.Text = kit.Parts.Count == 0 ? "" :
            $"{pieces} piece{(pieces == 1 ? "" : "s")} to cut: {tiles} tile{(tiles == 1 ? "" : "s")}" +
            (spec.HasBase ? $", {tiles} base layer{(tiles == 1 ? "" : "s")}" : "") +
            (kit.Clips > 0 ? $", {kit.Clips} clip{(kit.Clips == 1 ? "" : "s")}" : "") + ".";
        _make.IsEnabled = kit.Ok && kit.Parts.Count > 0 && !findings.Any(f => f.Level == Severity.Bad);
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

    async void Make()
    {
        if (_making) return;
        _making = true;
        _make.IsEnabled = false;
        _status.Text = "Putting the tiles in the design…";
        try
        {
            var spec = Current();
            var built = _kit is not null && _kitFrom?.ToJson() == spec.ToJson() ? _kit : null;
            Func<Layer, Layer>? cut = CutRecipe is { } c ? l => c.R.Settings.ApplyTo(l) with { Material = new MaterialRef(c.M.Id, c.M.Label, c.R.Id, c.R.Title) } : null;
            Func<Layer, Layer>? etch = EtchRecipe is { } e ? l => e.R.Settings.ApplyTo(l) with { Material = new MaterialRef(e.M.Id, e.M.Label, e.R.Id, e.R.Title) } : null;
            Guid? replace = _editing is { } ed && TilePlacement.ShapesOf(_doc.State, ed.Id).Count + _doc.State.OtherPages.Sum(p => p.Shapes.Count(x => x.AddonData.TryGetValue(TilePlacement.IdKey, out var v) && v == ed.Id.ToString())) > 0 ? ed.Id : null;
            var before = _doc.State;
            var (kit, s, sheets, id) = await Task.Run(() =>
            {
                var kit = built ?? TileKitMaker.Make(spec);
                if (!kit.Ok) throw new InvalidOperationException(kit.Findings.First(f => f.Level == Severity.Bad).Text);
                var (s, sheets, id) = TilePlacement.Place(before, kit, spec, _machine, new TilePlaceOptions { CutSettings = cut, EtchSettings = etch, Replace = replace });
                return (kit, s, sheets, id);
            });
            if (!ReferenceEquals(_doc.State, before)) { _status.Text = "The design changed meanwhile: press again to put the tiles in."; return; }
            _doc.Commit(replace is null ? "Floor tiles" : "Update the floor tiles", s, TilePlacement.ShapesOf(s, id));
            _editing = (id, spec);
            _make.Content = "Update in the design";
            int pieces = kit.PieceCount;
            _status.Text = $"{(replace is null ? "Put" : "Updated")} {pieces} piece{(pieces == 1 ? "" : "s")} in the design" +
                           (sheets > 1 ? $", over {sheets} pages (a bed each)" : "") +
                           (spec.Relief ? $": outlines on \"{TilePlacement.CutLayer}\", the texture as a depth map on \"{TilePlacement.ReliefLayer}\" (carved first, in slices)."
                                        : $": outlines on \"{TilePlacement.CutLayer}\", the texture on \"{TilePlacement.EtchLayer}\" and \"{TilePlacement.LineLayer}\" (done first).");
        }
        catch (InvalidOperationException ex) { _status.Text = ex.Message; }
        catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException or ArithmeticException or NullReferenceException)
        {
            AppLog.Error("floortiles", ex);
            _status.Text = $"Couldn't put the tiles in: {ex.Message}";
        }
        finally
        {
            _making = false;
            _make.IsEnabled = _kit?.Ok ?? true;
        }
    }
}
