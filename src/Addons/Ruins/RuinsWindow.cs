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

namespace PhotonCanvas.Addon.Ruins;

/// <summary>
/// The ruins maker: a broken wall (its size, ends, texture and damage, and a base to stand on), rubble
/// piles, loose stones, fallen chunks and broken pillars, with a preview as built or piece by piece;
/// then the pieces go into the design, nested onto the bed.
/// </summary>
public sealed class RuinsWindow : Window
{
    readonly Document _doc;
    readonly MachineProfile _machine;
    readonly string? _dataFolder;
    readonly MaterialLibrary? _library;
    (Guid Id, RuinSpec Spec)? _editing;
    /// <summary>A texture of your own that came with matched walls or a ruin being changed (the wall maker keeps the list of them).</summary>
    CustomTexture? _custom;

    // Wall
    readonly CheckBox _makeWall = new() { Content = "Make a broken wall", IsChecked = true };
    readonly NumericUpDown _length = Number(10, 2000, 5, "0.#"), _height = Number(6, 1000, 1, "0.#"), _thick = Number(0.5m, 20, 0.5m, "0.##"), _walls = Number(1, 100, 1, "0"), _slotDepth = Number(0.5m, 50, 0.5m, "0.#");
    readonly CheckBox _twoSided = new() { Content = "Two panels back to back (texture on both faces)" };
    readonly ComboBox _leftEnd = Combo("Whole (fits a column)", "Broken"), _rightEnd = Combo("Whole (fits a column)", "Broken");
    readonly CheckBox _plainEnds = new() { Content = "Keep whole ends smooth where they go into the columns", IsChecked = true };
    readonly Button _match = UiKit.Button("Match the walls in the design", tip: "Use the sheet, height, texture and variation of walls made with Walls (the selected ones, or the first in the design), so the ruin matches them and fits their columns.");
    readonly TextBlock _matchNote = Hint();
    readonly ComboBox _cutWith = Combo(), _etchWith = Combo();
    readonly TextBlock _cutNote = Hint();
    List<(Material M, Recipe R)?> _cuts = [], _etches = [];
    Control _wallRows = null!;

    // Texture
    readonly ComboBox _theme = Combo([.. RuinThemes.Names]);
    readonly TextBlock _themeAbout = Hint();
    readonly ComboBox _texture = Combo();
    readonly List<string> _textureNames = [];
    readonly NumericUpDown _sizeA = Number(0.5m, 300, 0.5m, "0.##"), _sizeB = Number(0.5m, 300, 0.5m, "0.##"), _groove = Number(0.1m, 3, 0.1m, "0.##"), _seed = Number(1, 9999, 1, "0");
    readonly TextBlock _sizeALabel = Label("Size"), _sizeBLabel = Label("Size"), _textureAbout = Hint();
    Control _sizeARow = null!, _sizeBRow = null!, _grooveRow = null!;
    readonly NumericUpDown _wear = Number(0, 100, 5, "0"), _detail = Number(0.2m, 5, 0.1m, "0.##");
    Control _wearRow = null!, _detailRow = null!;
    readonly ComboBox _etch = Combo("Grooves (etched areas, deeper)", "Lines (scored, faster)", "Relief (carved in depth, slowest)");

    // Damage
    readonly ComboBox _break = Combo("A dip in the middle", "Falls away to the left", "Falls away to the right", "Two gaps", "A low stump");
    readonly NumericUpDown _breakDepth = Number(5, 90, 5, "0"), _breaches = Number(0, 6, 1, "0"), _breachSize = Number(3, 200, 1, "0.#"), _knocked = Number(0, 40, 1, "0"), _cracks = Number(0, 10, 1, "0");

    // Base
    readonly CheckBox _base = new() { Content = "A base the wall slots into" };
    readonly CheckBox _footRubble = new() { Content = "Rubble heaped against the wall, where it fell", IsChecked = true };
    readonly NumericUpDown _baseMm = Number(6, 80, 1, "0.#"), _fit = Number(-1, 2, 0.05m, "0.##");
    readonly TextBlock _baseNote = Hint();
    Control _baseRows = null!;

    // Rubble
    readonly NumericUpDown _piles = Number(0, Rubble.MaxPiles, 1, "0"), _pileW = Number(4, 1000, 1, "0.#"), _pileD = Number(3, 1000, 1, "0.#"), _pileH = Number(0.5m, 200, 1, "0.#");
    readonly ComboBox _pileShape = Combo("A heap", "Against a wall (straight back)", "In a corner");
    readonly CheckBox _markLayers = new() { Content = "Score where each layer goes on the one under it", IsChecked = true };
    readonly NumericUpDown _loose = Number(0, Rubble.MaxLoose, 1, "0"), _chunks = Number(0, Rubble.MaxChunks, 1, "0");
    Control _pileRows = null!;

    // Pillars
    readonly NumericUpDown _pillars = Number(0, Pillars.MaxPillars, 1, "0"), _pillarW = Number(3, 300, 0.5m, "0.#"), _pillarH = Number(1, 1000, 1, "0.#"), _pin = Number(0, 10, 0.1m, "0.#");
    readonly ComboBox _pillarShape = Combo("Round", "Square", "Eight-sided");
    readonly CheckBox _plinth = new() { Content = "A wider square plinth at the bottom", IsChecked = true };
    Control _pillarRows = null!;

    readonly Border _tabsHost = new() { Width = 440, Padding = new Thickness(14, 10, 8, 0) };
    readonly RuinPreview _preview = new();
    readonly ComboBox _show = Combo("As built", "Pieces to cut");
    readonly TextBlock _summary = new() { TextWrapping = TextWrapping.Wrap };
    readonly StackPanel _findings = new() { Spacing = 4 };
    readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Classes = { "muted" } };
    readonly Button _make = new() { Content = "Put in the design", Classes = { "primary" }, MinWidth = 160 };
    readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(200) };
    bool _loading;
    int _generation;
    RuinKit? _kit;
    RuinSpec? _kitFrom;

    public RuinsWindow(Document doc, MachineProfile machine, MaterialLibrary? library, string? dataFolder, (Guid Id, RuinSpec Spec)? editing)
    {
        _doc = doc;
        _machine = machine;
        _library = library;
        _dataFolder = dataFolder;
        _editing = editing;
        Title = "Ruins and rubble";
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
                Tab("Wall", WallTab()),
                Tab("Texture", TextureTab()),
                Tab("Damage", DamageTab()),
                Tab("Base", BaseTab()),
                Tab("Rubble", RubbleTab()),
                Tab("Pillars", PillarsTab()),
            },
        };
        _tabsHost.Child = tabs;

        var results = new StackPanel { Spacing = 8, Children = { _summary, _findings } };
        var right = new Grid { RowDefinitions = new("Auto,*,Auto"), Margin = new Thickness(0, 14, 14, 0), RowSpacing = 8 };
        _show.Width = 170;
        _show.HorizontalAlignment = HorizontalAlignment.Left;
        ToolTip.SetTip(_show, "As built: the wall's face, and each pile, base and pillar from above with its layers stacked. Pieces to cut: one of each piece, flat.");
        right.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { Label("Show"), _show } });
        var frame = new Border { CornerRadius = new(8), ClipToBounds = true, BorderBrush = Brush("Line"), BorderThickness = new(1), Child = _preview };
        Grid.SetRow(frame, 1);
        right.Children.Add(frame);
        var resultScroll = new ScrollViewer { Content = results, MaxHeight = 200, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(resultScroll, 2);
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
        _show.SelectionChanged += (_, _) => { _preview.Built = _show.SelectedIndex == 0; _preview.Show(_kit is { Parts.Count: > 0 } ? _kit : null, "Nothing to show."); };
        Hook();
        var last = editing is null ? LoadLast() : null;
        Load(editing?.Spec ?? last ?? new RuinSpec());
        // A first ruin matches any walls already in the design.
        if (editing is null && last is null) MatchWalls(quiet: true);
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

    Control WallTab()
    {
        _match.HorizontalAlignment = HorizontalAlignment.Left;
        _wallRows = Stack(
            Row(Small("Length", _length, tip: "How long the wall section is (mm)."), Small("Height", _height, tip: "How high it is where it's whole (mm).")),
            _twoSided,
            Field("How many walls", _walls, tip: "How many broken walls like this to cut (they're all the same; change Variation for a different one)."),
            Row(Small("Left end", _leftEnd, tip: "Whole: straight and full height, to slide into a column from the wall maker. Broken: it crumbles away."),
                Small("Right end", _rightEnd, tip: "Whole: straight and full height, to slide into a column from the wall maker. Broken: it crumbles away.")),
            Field("Slot depth", _slotDepth, tip: "How deep the columns' slots are (mm), as set in the wall maker: whole ends are kept straight that far."),
            _plainEnds);
        return Stack(
            Explained("Wall", "A section of wall broken down from the top, in the wall maker's textures. Untick it to make only rubble and pillars.",
                _makeWall, _wallRows),
            Explained("Sheet", "Every piece is cut from this sheet. Matching walls made with Walls brings their sheet, height, texture and variation, so the ruin's courses line up with theirs and its whole ends fit their columns.",
                Field("Thickness", _thick, tip: "The sheet's thickness (mm)."),
                _match, _matchNote),
            Explained("Material", "Picking your material's settings brings its thickness and, from your cut test, its kerf and slot fit.",
                Field("Cut with", _cutWith),
                Field("Etch with", _etchWith, tip: "Settings for the texture (an engrave/fill recipe from your library)."),
                _cutNote));
    }

    Control TextureTab()
    {
        _sizeARow = Line(_sizeALabel, _sizeA);
        _sizeBRow = Line(_sizeBLabel, _sizeB);
        _grooveRow = Field("Groove", _groove, tip: "How wide the etched gaps between bricks, stones or planks are (mm).");
        var shuffle = UiKit.Button("Shuffle");
        shuffle.HorizontalAlignment = HorizontalAlignment.Left;
        shuffle.Click += (_, _) => _seed.Value = (_seed.Value ?? 1) % 9999 + 1;
        var seedRow = Field("Variation", new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 6, Children = { _seed, shuffle.With(b => Grid.SetColumn(b, 1)) } },
            tip: "Where the wall breaks, how the stones lie and how the rubble falls: each number gives a different ruin, the same number the same one. Use the walls' own number to keep their stones.");
        _wearRow = Field("Wear %", _wear, tip: "How weathered the texture looks: 0 is new and crisp, 50 the usual look, 100 ancient and battered (more nicks, chipped corners, pits, cracks and scratches). Use the walls' own value to match them.");
        _detailRow = Field("Detail size", _detail, tip: "Size of the smallest marks in mm: pits, nicks, wood grain, bolts, vent slats and greebles.");
        return Stack(
            Explained("Theme", "A look to start from, the same as the wall maker's: it sets the texture and the pillars' shape to match the walls of that theme. Change them afterwards if you like.",
                Field("Theme", _theme), _themeAbout),
            Explained("Texture", "The wall maker's textures. A wall breaks along the mortar, brick by brick, where the texture has bricks or stones; wood splinters, metal tears and plain sheet breaks in steps.",
            Field("Texture", _texture),
            _textureAbout,
            _sizeARow, _sizeBRow, _grooveRow, seedRow,
            _wearRow, _detailRow,
            Field("Etch as", _etch, tip: "Grooves etch the gaps as areas (deeper, the most real). Lines score round each brick or stone (much quicker). Relief carves a greyscale depth map in slices: each piece at its own height, edges rounded into the grooves (the most real, and much slower).")));
    }

    Control DamageTab() => Stack(
        Explained("Break", "How the top of the wall has fallen. Whole ends rise back to full height to meet their columns.",
            Field("Break", _break),
            Field("Broken down %", _breakDepth, tip: "How far down the wall is broken where it's lowest, as a share of its height.")),
        Explained("Holes and cracks", "Holes knocked right through and single stones knocked out (cut away along the mortar), and cracks scored down from the break. They keep clear of the edges so nothing thin is left to snap.",
            Row(Small("Holes through", _breaches, tip: "Holes knocked right through the wall, below the break."), Small("Hole size", _breachSize, tip: "About how wide each hole is (mm).")),
            Row(Small("Knocked-out stones", _knocked, tip: "Single bricks or stones missing from the wall (for textures of bricks, stones, tiles or plates)."),
                Small("Cracks", _cracks, tip: "Cracks scored down the face from low points of the break."))));

    Control BaseTab()
    {
        _baseRows = Stack(
            Field("Base reach", _baseMm, tip: "How far the base reaches out in front of and behind the wall (mm)."),
            _footRubble);
        return Stack(Explained("Base", "A wall with both ends broken can stand on a base of its own: tabs on the wall's bottom edge slot into it, and rubble can be heaped against it where it fell, with stones scored on the ground round about.",
            _base, _baseRows, _baseNote,
            Field("Slot fit", _fit, tip: "Added to the tabs' size for the base's slots (mm): more is looser. With your material's cut settings picked, the kerf is allowed for too.")));
    }

    Control RubbleTab()
    {
        _pileRows = Stack(
            Field("Shape", _pileShape, tip: "A heap stands on its own; against a wall has a straight back to push up to a wall; in a corner has two straight sides at a right angle."),
            Row(Small("Width", _pileW, tip: "How wide the bottom layer is (mm)."), Small("Depth", _pileD, tip: "How deep the bottom layer is, front to back (mm).")),
            Field("Height", _pileH, tip: "How high the pile is (mm): it's stacked from layers of the sheet."),
            _markLayers);
        return Stack(
            Explained("Rubble piles", "Stacked from layers of the sheet, each a little smaller than the one under it, with edges of whole bricks or stones turned every which way, and the texture etched where each layer shows.",
                Field("Piles", _piles, tip: "How many piles (each a little different)."), _pileRows),
            Explained("Loose stones", "Single bricks or stones to scatter round the ruins or glue on the piles, and broken-off pieces of wall with the courses still in them.",
                Field("Loose stones", _loose, tip: "How many single bricks or stones to cut."),
                Field("Fallen chunks", _chunks, tip: "How many broken-off pieces of wall to cut, to lay on the ground.")));
    }

    Control PillarsTab()
    {
        _pillarRows = Stack(
            Field("Shape", _pillarShape),
            Row(Small("Width", _pillarW, tip: "Across the pillar (mm)."), Small("Height", _pillarH, tip: "How high the broken pillar stands at its highest (mm), without the plinth.")),
            Field("Pin hole Ø", _pin, tip: "A hole up the middle of each layer for a cocktail stick or wire that lines the stack up (mm). 0 = none."),
            _plinth);
        return Stack(Explained("Broken pillars", "Stacks of layers on a pin: whole at the bottom, the top few broken away more and more.",
            Field("Pillars", _pillars, tip: "How many broken pillars (each broken differently)."), _pillarRows));
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
            int same = _etches.FindIndex(e => e is { } x && x.M.Id == c.M.Id);
            if (same > 0 && EtchRecipe is null) _etchWith.SelectedIndex = same;
            _loading = false;
        }
        _cutNote.Text = CutRecipe is not { } r
            ? "No kerf allowance: the laser burns each edge back by half its kerf, so the base's slots come out a little loose. Pick your material's cut settings (Materials & Tests → Tests → Cut test measures the kerf) for slots at their drawn size."
            : r.R.Settings.KerfMm > 0 ? $"{r.M.Label}: kerf {r.R.Settings.KerfMm:0.###} mm, allowed for on the cut layer."
            : $"{r.M.Label}: no kerf measured yet (run a cut test).";
        Changed();
    }

    // ---- settings in and out ----

    void Hook()
    {
        foreach (var n in new[] { _length, _height, _thick, _walls, _slotDepth, _sizeA, _sizeB, _groove, _seed, _breakDepth, _breaches, _breachSize, _knocked, _cracks,
                                  _baseMm, _fit, _piles, _pileW, _pileD, _pileH, _loose, _chunks, _pillars, _pillarW, _pillarH, _pin })
            n.ValueChanged += (_, _) => Changed();
        foreach (var n in new[] { _wear, _detail }) n.ValueChanged += (_, _) => Changed();
        foreach (var c in new[] { _leftEnd, _rightEnd, _etch, _break, _pileShape, _pillarShape, _etchWith })
            c.SelectionChanged += (_, _) => Changed();
        foreach (var c in new[] { _makeWall, _twoSided, _plainEnds, _base, _footRubble, _markLayers, _plinth })
            c.IsCheckedChanged += (_, _) => Changed();
        _cutWith.SelectionChanged += (_, _) => CutChosen();
        _texture.SelectionChanged += (_, _) => TextureChosen();
        _theme.SelectionChanged += (_, _) => ThemeChosen();
    }

    void FillTextures(string select)
    {
        _textureNames.Clear();
        _textureNames.AddRange(Textures.Presets.Select(p => p.Name));
        if (_custom is { } c && !_textureNames.Contains(c.Name)) _textureNames.Add(c.Name);
        bool was = _loading;
        _loading = true;
        _texture.ItemsSource = _textureNames.Select(n => Textures.Preset(n) is null ? $"{n} (yours)" : n).ToList();
        _texture.SelectedIndex = Math.Max(0, _textureNames.IndexOf(select));
        _loading = was;
        ShowTextureRows();
    }

    string TextureName => _texture.SelectedIndex >= 0 && _texture.SelectedIndex < _textureNames.Count ? _textureNames[_texture.SelectedIndex] : "Brick";

    CustomTexture? OwnTexture => Textures.Preset(TextureName) is null && _custom is { } c && c.Name == TextureName ? c : null;

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
            _wearRow.IsVisible = _detailRow.IsVisible = p.Name != Textures.Plain;
            _textureAbout.Text = p.About + (Rough.Cellular(p.Name) ? " Breaks along the mortar." : Rough.Wooden(p.Name) ? " Splinters where it breaks." : "");
        }
        else
        {
            _sizeALabel.Text = "Tile width";
            _sizeARow.IsVisible = true;
            _sizeBRow.IsVisible = _grooveRow.IsVisible = false;
            _wearRow.IsVisible = _detailRow.IsVisible = false;
            _textureAbout.Text = OwnTexture is { } own ? $"Your texture from the wall maker: a {own.TileWidthMm:0.#} × {own.TileHeightMm:0.#} mm tile, scaled to the tile width. It breaks in steps." : "";
        }
    }

    void ShowRows()
    {
        _wallRows.IsEnabled = _makeWall.IsChecked == true;
        bool broken = _leftEnd.SelectedIndex == 1 && _rightEnd.SelectedIndex == 1;
        _base.IsEnabled = _makeWall.IsChecked == true && broken;
        _baseRows.IsVisible = _base.IsChecked == true;
        _baseRows.IsEnabled = _base.IsEnabled;
        _baseNote.Text = _makeWall.IsChecked != true ? "Tick Make a broken wall (Wall tab) for a base." : broken ? "" : "A base is for a wall with both ends broken (Wall tab): a wall with a whole end stands in the wall maker's columns.";
        _baseNote.IsVisible = _baseNote.Text.Length > 0;
        _pileRows.IsVisible = (_piles.Value ?? 0) > 0;
        _pillarRows.IsVisible = (_pillars.Value ?? 0) > 0;
    }

    static decimal D(double v) => (decimal)v;
    static double V(NumericUpDown n, double fallback) => n.Value is { } v ? (double)v : fallback;

    string ThemeName => RuinThemes.Names[Math.Clamp(_theme.SelectedIndex, 0, RuinThemes.Names.Count - 1)];

    /// <summary>A theme picked: its texture and pillars replace the ones set.</summary>
    void ThemeChosen()
    {
        _themeAbout.Text = RuinThemes.About(ThemeName);
        if (_loading) return;
        Load(RuinThemes.Apply(Current(), ThemeName));
        Changed();
    }

    void Load(RuinSpec s)
    {
        _loading = true;
        if (s.Custom is not null) _custom = s.Custom;
        _makeWall.IsChecked = s.Wall;
        _length.Value = D(s.LengthMm); _height.Value = D(s.HeightMm); _thick.Value = D(s.ThicknessMm); _walls.Value = s.Walls;
        _twoSided.IsChecked = s.TwoSided; _leftEnd.SelectedIndex = (int)s.LeftEnd; _rightEnd.SelectedIndex = (int)s.RightEnd;
        _slotDepth.Value = D(s.SlotDepthMm); _plainEnds.IsChecked = s.PlainEnds;
        _sizeA.Value = D(s.SizeA); _sizeB.Value = D(s.SizeB); _groove.Value = D(Math.Clamp(s.GrooveMm, 0.1, 3)); _seed.Value = Math.Clamp(s.Seed, 1, 9999);
        _wear.Value = D(Math.Clamp(s.Wear, 0, 1) * 100); _detail.Value = D(Math.Clamp(s.DetailMm > 0 ? s.DetailMm : Textures.Preset(s.Texture)?.Detail ?? 0.5, 0.2, 5));
        _etch.SelectedIndex = s.Relief ? 2 : (int)s.Etch;
        _theme.SelectedIndex = Math.Max(0, RuinThemes.Names.ToList().IndexOf(s.Theme)); _themeAbout.Text = RuinThemes.About(s.Theme);
        _break.SelectedIndex = (int)s.Break; _breakDepth.Value = D(Math.Round(Math.Clamp(s.BreakDepth, 0.05, 0.9) * 100));
        _breaches.Value = Math.Clamp(s.Breaches, 0, 6); _breachSize.Value = D(s.BreachMm); _knocked.Value = Math.Clamp(s.KnockedOut, 0, 40); _cracks.Value = Math.Clamp(s.Cracks, 0, 10);
        _base.IsChecked = s.Base; _baseMm.Value = D(s.BaseMm); _footRubble.IsChecked = s.FootRubble; _fit.Value = D(s.FitMm);
        _piles.Value = Math.Clamp(s.Piles, 0, Rubble.MaxPiles); _pileShape.SelectedIndex = (int)s.PileShape;
        _pileW.Value = D(s.PileWidthMm); _pileD.Value = D(s.PileDepthMm); _pileH.Value = D(s.PileHeightMm); _markLayers.IsChecked = s.MarkLayers;
        _loose.Value = Math.Clamp(s.LooseStones, 0, Rubble.MaxLoose); _chunks.Value = Math.Clamp(s.Chunks, 0, Rubble.MaxChunks);
        _pillars.Value = Math.Clamp(s.Pillars, 0, Pillars.MaxPillars); _pillarShape.SelectedIndex = (int)s.PillarShape;
        _pillarW.Value = D(s.PillarMm); _pillarH.Value = D(s.PillarHeightMm); _pin.Value = D(s.PinMm); _plinth.IsChecked = s.Plinth;
        _loading = false;
        FillTextures(s.Texture);
        ShowRows();
        CutChosen();
    }

    RuinSpec Current() => new()
    {
        Wall = _makeWall.IsChecked == true, Theme = ThemeName,
        LengthMm = V(_length, 100), HeightMm = V(_height, 50), ThicknessMm = V(_thick, 3), TwoSided = _twoSided.IsChecked == true, Walls = (int)V(_walls, 1),
        LeftEnd = (EndKind)Math.Max(0, _leftEnd.SelectedIndex), RightEnd = (EndKind)Math.Max(0, _rightEnd.SelectedIndex),
        SlotDepthMm = V(_slotDepth, 4), PlainEnds = _plainEnds.IsChecked == true,
        Texture = TextureName, Custom = OwnTexture, SizeA = V(_sizeA, 8), SizeB = V(_sizeB, 3.5), GrooveMm = V(_groove, 0.6), Seed = (int)V(_seed, 1),
        Wear = V(_wear, 50) / 100, DetailMm = V(_detail, 0.5),
        Etch = _etch.SelectedIndex == 1 ? EtchStyle.Lines : EtchStyle.Grooves, Relief = _etch.SelectedIndex == 2,
        Break = (BreakShape)Math.Max(0, _break.SelectedIndex), BreakDepth = V(_breakDepth, 55) / 100,
        Breaches = (int)V(_breaches, 0), BreachMm = V(_breachSize, 14), KnockedOut = (int)V(_knocked, 3), Cracks = (int)V(_cracks, 2),
        Base = _base.IsChecked == true, BaseMm = V(_baseMm, 14), FootRubble = _footRubble.IsChecked == true, FitMm = V(_fit, 0.15),
        Piles = (int)V(_piles, 0), PileShape = (PileShape)Math.Max(0, _pileShape.SelectedIndex), PileWidthMm = V(_pileW, 40), PileDepthMm = V(_pileD, 30), PileHeightMm = V(_pileH, 12),
        MarkLayers = _markLayers.IsChecked == true, LooseStones = (int)V(_loose, 12), Chunks = (int)V(_chunks, 2),
        Pillars = (int)V(_pillars, 0), PillarShape = (PillarShape)Math.Max(0, _pillarShape.SelectedIndex), PillarMm = V(_pillarW, 14), PillarHeightMm = V(_pillarH, 30),
        PinMm = V(_pin, 2), Plinth = _plinth.IsChecked == true,
    };

    /// <summary>Takes the sheet, height and texture of walls made with Walls (quietly: only if there are any).</summary>
    void MatchWalls(bool quiet)
    {
        var json = WallsLink.Find(_doc.State, _doc.Selection);
        if (json is null)
        {
            if (!quiet) _matchNote.Text = "There are no walls from Walls in the design to match.";
            return;
        }
        if (WallsLink.Match(Current(), json) is not { } matched)
        {
            if (!quiet) _matchNote.Text = "Couldn't read those walls' settings.";
            return;
        }
        Load(matched);
        _matchNote.Text = $"Matched: {matched.ThicknessMm:0.##} mm sheet{(matched.TwoSided ? " (two panels back to back)" : "")}, {matched.HeightMm:0.#} mm high, {matched.Texture}, variation {matched.Seed}.";
        Changed();
    }

    string? LastFile => _dataFolder is null ? null : Path.Combine(_dataFolder, "last.json");

    RuinSpec? LoadLast()
    {
        try { return LastFile is { } f && File.Exists(f) ? RuinSpec.FromJson(File.ReadAllText(f)) : null; }
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
        RuinKit kit;
        try { kit = await Task.Run(() => RuinKitMaker.Make(spec)); }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or ArithmeticException)
        {
            if (gen == _generation) _preview.Show(null, e.Message);
            return;
        }
        if (gen != _generation) return;
        _kit = kit;
        _kitFrom = spec;
        var findings = kit.Findings.Concat(Placement.TooBig(kit, _machine)).ToList();
        _preview.Show(kit.Parts.Count > 0 ? kit : null, findings.FirstOrDefault(f => f.Level == Severity.Bad)?.Text ?? "Nothing to show.");
        _findings.Children.Clear();
        foreach (var f in findings.OrderByDescending(f => f.Level)) _findings.Children.Add(FindingRow(f));
        int pieces = kit.PieceCount;
        _summary.Text = kit.Parts.Count == 0 ? "" : $"{pieces} piece{(pieces == 1 ? "" : "s")} to cut: " + Summary(kit) + ".";
        _make.IsEnabled = kit.Ok && kit.Parts.Count > 0 && !findings.Any(f => f.Level == Severity.Bad);
    }

    /// <summary>What's in the kit, in a few words: "a broken wall, 2 rubble piles (9 layers), 12 loose bricks".</summary>
    static string Summary(RuinKit kit)
    {
        var words = new List<string>();
        var groups = kit.Parts.GroupBy(p => Kind(p.Name)).ToList();
        foreach (var g in groups)
        {
            int n = g.Sum(p => p.Copies);
            words.Add(g.Key switch
            {
                "layers" => $"{n} layer{(n == 1 ? "" : "s")} of rubble",
                "pillar" => $"{n} pillar layer{(n == 1 ? "" : "s")}",
                _ => n > 1 ? $"{n} × {g.Key}" : g.Key,
            });
        }
        return ListWords(words);
    }

    static string Kind(string name)
    {
        if (name.StartsWith("Rubble", StringComparison.Ordinal)) return "layers";
        if (name.StartsWith("Pillar", StringComparison.Ordinal)) return "pillar";
        // "Loose brick 3" → "loose brick", "Fallen chunk 2" → "fallen chunk".
        var words = name.Split(' ');
        if (words.Length > 1 && int.TryParse(words[^1], out _)) name = string.Join(' ', words[..^1]);
        return char.ToLowerInvariant(name[0]) + name[1..];
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
                var kit = built ?? RuinKitMaker.Make(spec);
                if (!kit.Ok) throw new InvalidOperationException(kit.Findings.First(f => f.Level == Severity.Bad).Text);
                var (s, sheets, id) = Placement.Place(before, kit, spec, _machine, new PlaceOptions { CutSettings = cut, EtchSettings = etch, Replace = replace });
                return (kit, s, sheets, id);
            });
            if (!ReferenceEquals(_doc.State, before)) { _status.Text = "The design changed meanwhile: press again to put the pieces in."; return; }
            _doc.Commit(replace is null ? "Ruins" : "Update the ruins", s, Placement.ShapesOf(s, id));
            _editing = (id, spec);
            _make.Content = "Update in the design";
            int pieces = kit.PieceCount;
            _status.Text = $"{(replace is null ? "Put" : "Updated")} {pieces} piece{(pieces == 1 ? "" : "s")} in the design" +
                           (sheets > 1 ? $", over {sheets} pages (a bed each)" : "") +
                           (spec.Relief ? $": outlines on \"{Placement.CutLayer}\", the texture as a depth map on \"{Placement.ReliefLayer}\" (carved first, in slices)."
                                        : $": outlines on \"{Placement.CutLayer}\", the texture on \"{Placement.EtchLayer}\" and \"{Placement.LineLayer}\" (done first).");
        }
        catch (InvalidOperationException ex) { _status.Text = ex.Message; }
        catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException or ArithmeticException or NullReferenceException)
        {
            AppLog.Error("ruins", ex);
            _status.Text = $"Couldn't put the ruins in: {ex.Message}";
        }
        finally
        {
            _making = false;
            _make.IsEnabled = _kit?.Ok ?? true;
        }
    }
}
