using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using PhotonCanvas.Addon.FloorTiles;
using PhotonCanvas.Addon.Walls;
using PhotonCanvas.App;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Materials;
using static PhotonCanvas.App.UiKit;

namespace PhotonCanvas.Addon.Bases;

/// <summary>
/// The base maker: the sizes and how many of each, the layers, slot and magnet, the texture, and a
/// movement tray, with a preview of one of each base and the tray filled; then the pieces go into the
/// design, nested onto the bed.
/// </summary>
public sealed class BasesWindow : Window
{
    readonly Document _doc;
    readonly MachineProfile _machine;
    readonly MaterialLibrary? _library;
    readonly string? _dataFolder;
    readonly CustomTextures _own;
    (Guid Id, BaseSpec Spec)? _editing;

    // Bases
    readonly StackPanel _sizes = new() { Spacing = 6 };
    readonly List<SizeRow> _sizeRows = [];
    readonly NumericUpDown _thick = Number(0.5m, 20, 0.5m, "0.##"), _layers = Number(1, 4, 1, "0"), _step = Number(0, 10, 0.1m, "0.##"), _corner = Number(0, 20, 0.5m, "0.#");
    readonly ComboBox _fixing = Combo("Nothing (glue the figure on)", "A slot for a slotted figure");
    readonly NumericUpDown _slotL = Number(1, 100, 0.5m, "0.#"), _slotW = Number(0.5m, 10, 0.1m, "0.##"), _magnet = Number(0, 30, 0.5m, "0.#"), _fit = Number(-1, 2, 0.05m, "0.##");
    Control _slotRows = null!;
    readonly ComboBox _cutWith = Combo(), _etchWith = Combo();
    readonly TextBlock _cutNote = Hint(), _floorNote = Hint();
    List<(Material M, Recipe R)?> _cuts = [], _etches = [];

    // Texture
    readonly ComboBox _texture = Combo();
    readonly List<string> _textureNames = [];
    readonly NumericUpDown _sizeA = Number(0, 300, 0.1m, "0.##"), _sizeB = Number(0, 300, 0.5m, "0.##"), _groove = Number(0.1m, 3, 0.05m, "0.##"), _seed = Number(1, 9999, 1, "0"), _cracks = Number(0, 100, 5, "0");
    readonly NumericUpDown _rim = Number(0, 20, 0.5m, "0.#"), _spot = Number(0, 100, 1, "0.#");
    readonly TextBlock _sizeALabel = Label("Size"), _sizeBLabel = Label("Size"), _textureAbout = Hint();
    Control _sizeARow = null!, _sizeBRow = null!, _grooveRow = null!, _seedRow = null!, _cracksRow = null!;
    readonly ComboBox _etch = Combo("Grooves (etched areas, deeper)", "Lines (scored, faster)");

    // Tray
    readonly CheckBox _tray = new() { Content = "Make a movement tray" };
    readonly ComboBox _trayShape = Combo("Round", "Oval", "Square", "Rectangle", "Hexagon");
    readonly NumericUpDown _trayW = Number(5, 400, 0.5m, "0.##"), _trayD = Number(5, 400, 0.5m, "0.##"), _cols = Number(1, 40, 1, "0"), _rows = Number(1, 40, 1, "0");
    readonly CheckBox _stagger = new() { Content = "Stagger the rows (every other row half a base along)" };
    readonly ComboBox _style = Combo("A pocket for each base", "One open pocket for the whole unit");
    readonly NumericUpDown _gap = Number(0, 20, 0.5m, "0.##"), _clear = Number(0, 3, 0.05m, "0.##"), _border = Number(1, 30, 0.5m, "0.#"), _trayCorner = Number(0, 30, 0.5m, "0.#"), _frames = Number(1, 4, 1, "0"), _trays = Number(1, 50, 1, "0");
    readonly CheckBox _bottom = new() { Content = "A bottom plate under the frame", IsChecked = true }, _trayTexture = new() { Content = "Etch the texture on the frame too", IsChecked = true };
    readonly ComboBox _holes = Combo("None", "A hole to push each base out", "A magnet under each base");
    Control _trayRows = null!, _trayDRow = null!;

    readonly BasePreview _preview = new();
    readonly TextBlock _summary = new() { TextWrapping = TextWrapping.Wrap };
    readonly StackPanel _findings = new() { Spacing = 4 };
    readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Classes = { "muted" } };
    readonly Button _make = new() { Content = "Put in the design", Classes = { "primary" }, MinWidth = 160 };
    readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(200) };
    bool _loading;
    int _generation;
    BaseKit? _kit;
    BaseSpec? _kitFrom;

    public BasesWindow(Document doc, MachineProfile machine, MaterialLibrary? library, string? dataFolder, (Guid Id, BaseSpec Spec)? editing)
    {
        _doc = doc;
        _machine = machine;
        _library = library;
        _dataFolder = dataFolder;
        // Your own textures are the wall maker's: made there, kept in its data folder, listed here too.
        _own = new CustomTextures(dataFolder is null ? null : Path.Combine(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(dataFolder)) ?? dataFolder, "walls"));
        _editing = editing;
        Title = "Miniature bases";
        Width = 1100;
        Height = 780;
        MinWidth = 840;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");

        Library();
        var tabs = new TabControl
        {
            Items =
            {
                Tab("Bases", BasesTab()),
                Tab("Texture", TextureTab()),
                Tab("Tray", TrayTab()),
            },
        };
        var left = new Border { Width = 470, Padding = new Thickness(14, 10, 8, 0), Child = tabs };

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
        Load(editing?.Spec ?? LoadLast() ?? new BaseSpec());
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

    Control BasesTab()
    {
        var add = UiKit.Button("Add a size");
        add.HorizontalAlignment = HorizontalAlignment.Left;
        add.Click += (_, _) => { AddSizeRow(new BaseSize { Count = 1 }); Changed(); };
        var match = UiKit.Button("Match the floor in the design", tip: "Takes the texture, sheet thickness and etch settings from floor tiles made with the floor tile maker.");
        match.HorizontalAlignment = HorizontalAlignment.Left;
        match.Click += (_, _) => MatchFloor();
        _slotRows = Row(Small("Slot length", _slotL, tip: "How long the slot is (mm). Slotted figures' tabs are usually about 20 mm long for a 25 mm base."),
                        Small("Slot width", _slotW, tip: "How wide the slot is (mm): a little wider than the tab is thick."));
        return Stack(
            Explained("Sizes", "Pick a shape and one of the usual sizes, or type your own. Round, square and hexagonal bases use the width only (a hexagon's is across its flats).",
                _sizes, add),
            Explained("Layers", "A base can be one layer, or several glued together for a thicker base; each layer smaller than the one under it gives a stepped, bevelled edge.",
                Field("Layers", _layers, tip: "Layers glued into each base (1 to 4)."),
                Field("Step in", _step, tip: "How much smaller each layer is than the one under it, all round (mm)."),
                Field("Corners", _corner, tip: "How much the corners of square and rectangular bases are rounded off (mm).")),
            Explained("Figure and magnet", "A slot takes a figure cast with a tab under its feet. A magnet hole goes in the bottom layer (a bottom layer is added if there's only one), for steel-lined cases and magnetic trays.",
                Field("Figure", _fixing),
                _slotRows,
                Field("Magnet size", _magnet, tip: "The round magnets' diameter (mm), 0 for none. Use magnets no thicker than the sheet."),
                Field("Fit", _fit, tip: "Play round a magnet in its hole (mm): more is looser.")),
            Explained("Sheet", "Picking your material's settings brings its thickness and, from your cut test, its kerf.",
                Field("Thickness", _thick, tip: "The sheet's thickness (mm)."),
                Field("Cut with", _cutWith),
                Field("Etch with", _etchWith, tip: "Settings for the texture (an engrave/fill recipe from your library)."),
                _cutNote,
                match, _floorNote));
    }

    Control TextureTab()
    {
        _sizeARow = Line(_sizeALabel, _sizeA);
        _sizeBRow = Line(_sizeBLabel, _sizeB);
        _grooveRow = Field("Groove", _groove, tip: "How wide the etched gaps between plates, stones or boards are (mm).");
        var shuffle = UiKit.Button("Shuffle");
        shuffle.HorizontalAlignment = HorizontalAlignment.Left;
        shuffle.Click += (_, _) => _seed.Value = (_seed.Value ?? 1) % 9999 + 1;
        _seedRow = Field("Variation", new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 6, Children = { _seed, shuffle.With(b => Grid.SetColumn(b, 1)) } },
            tip: "The texture is laid out at random, differently on every base: each number gives a different set.");
        _cracksRow = Field("Cracks %", _cracks, tip: "How many of the stones have a crack scored across them.");
        return Stack(
            Explained("Texture", "Etched on the top of every base: ground, the floor tile maker's paving (so bases match the floor they stand on) or sci-fi metal. Your own textures, made in the wall maker (Walls → Texture), are listed after the presets.",
                Field("Texture", _texture),
                _textureAbout,
                _sizeARow, _sizeBRow, _grooveRow, _seedRow, _cracksRow,
                Field("Etch as", _etch, tip: "Grooves etch the gaps as areas (deeper, the most real). Lines score round each stone or plate (much quicker).")),
            Explained("Rim and feet", "A plain rim round the edge is easy to paint neatly. A plain spot in the middle gives the figure's feet a flat place to be glued.",
                Field("Rim", _rim, tip: "A plain band round the top's edge, with a line scored along its inside (mm; 0 = texture to the edge)."),
                Field("Plain spot", _spot, tip: "A plain round spot in the middle for the figure's feet (mm across; 0 = none).")));
    }

    Control TrayTab()
    {
        _trayDRow = Small("Depth", _trayD, tip: "The bases' depth (mm), for oval and rectangular bases.");
        var same = UiKit.Button("Same as the first size", tip: "Makes the tray for the first size of base in the list.");
        same.HorizontalAlignment = HorizontalAlignment.Left;
        same.Click += (_, _) => TrayFromFirst();
        _trayRows = Stack(
            Explained("Bases it holds", "A movement tray carries a unit of figures on their bases about the table together.",
                Field("Shape", _trayShape),
                Row(Small("Width", _trayW, tip: "The bases' width (mm)."), _trayDRow),
                same,
                Row(Small("Across", _cols, tip: "Bases side by side along the front."), Small("Rows", _rows, tip: "Rows of bases, front to back.")),
                _stagger,
                Field("Pockets", _style, tip: "A pocket for each base holds them in formation; one open pocket takes any mix of bases.")),
            Explained("Frame", "The tray is a frame with the pockets cut out, glued on a bottom plate. More frame layers make deeper pockets.",
                Field("Gap", _gap, tip: "Space between neighbouring bases (mm)."),
                Field("Clearance", _clear, tip: "Play round each base in its pocket (mm, all round). With your material's cut settings picked, the kerf is allowed for too."),
                Field("Border", _border, tip: "The frame's width round the outside (mm)."),
                Field("Corners", _trayCorner, tip: "How much the tray's corners are rounded off (mm)."),
                Field("Frame layers", _frames, tip: "Frame layers stacked on the bottom plate (1 to 4)."),
                _bottom,
                Field("Under each base", _holes, tip: "A hole in the bottom plate to push the base out from underneath, or for a magnet that holds a magnetised base."),
                _trayTexture,
                Field("How many trays", _trays)));
        return Stack(_tray, _trayRows);
    }

    /// <summary>One line of the list of sizes.</summary>
    sealed class SizeRow
    {
        public readonly ComboBox Shape = Combo("Round", "Oval", "Square", "Rectangle", "Hexagon");
        public readonly ComboBox Preset = Combo();
        public readonly NumericUpDown Width = Number(5, 400, 0.5m, "0.##").With(n => n.ShowButtonSpinner = false), Depth = Number(5, 400, 0.5m, "0.##").With(n => n.ShowButtonSpinner = false), Count = Number(0, BaseKitMaker.MaxBases, 1, "0").With(n => n.ShowButtonSpinner = false);
        public readonly Button Remove = new() { Content = "✕", Padding = new Thickness(6, 2), MinWidth = 0 };
        public Grid Row = null!;
        public List<BasePreset> Presets = [];
        public BaseShape ShapeValue => (BaseShape)Math.Max(0, Shape.SelectedIndex);
        public BaseSize Value => new() { Shape = ShapeValue, WidthMm = V(Width, 25), DepthMm = V(Depth, 25), Count = (int)V(Count, 1) };
    }

    void AddSizeRow(BaseSize b)
    {
        var r = new SizeRow();
        bool was = _loading;
        _loading = true;
        r.Shape.SelectedIndex = (int)b.Shape;
        r.Width.Value = D(b.WidthMm); r.Depth.Value = D(b.DepthMm); r.Count.Value = b.Count;
        FillPresets(r);
        _loading = was;
        // Two lines: the shape and its usual sizes; then the size itself and how many.
        r.Row = new Grid { ColumnDefinitions = new("110,*,30"), RowDefinitions = new("Auto,Auto"), ColumnSpacing = 6, RowSpacing = 4 };
        Grid.SetColumn(r.Preset, 1);
        Grid.SetColumn(r.Remove, 2);
        var numbers = new Grid { ColumnDefinitions = new("Auto,*,Auto,*,Auto,*"), ColumnSpacing = 6 };
        Control[] cells = [Label("Width"), r.Width, Label("Depth"), r.Depth, Label("How many"), r.Count];
        for (int i = 0; i < cells.Length; i++) { Grid.SetColumn(cells[i], i); numbers.Children.Add(cells[i]); }
        Grid.SetRow(numbers, 1);
        Grid.SetColumnSpan(numbers, 2);
        r.Row.Children.Add(r.Shape);
        r.Row.Children.Add(r.Preset);
        r.Row.Children.Add(r.Remove);
        r.Row.Children.Add(numbers);
        if (_sizeRows.Count > 0) r.Row.Margin = new Thickness(0, 6, 0, 0);
        ToolTip.SetTip(r.Preset, "The usual sizes for this shape, and what they're used for.");
        ToolTip.SetTip(r.Width, "The base's width (mm): across a round base, across a hexagon's flats.");
        ToolTip.SetTip(r.Depth, "The base's depth (mm), for oval and rectangular bases.");
        ToolTip.SetTip(r.Remove, "Take this size off the list");
        r.Shape.SelectionChanged += (_, _) =>
        {
            if (_loading) return;
            _loading = true;
            if ((BaseShapes.For(r.ShapeValue).Skip(1).FirstOrDefault() ?? BaseShapes.For(r.ShapeValue).FirstOrDefault()) is { } p) { r.Width.Value = D(p.W); r.Depth.Value = D(p.D); }
            FillPresets(r);
            _loading = false;
            Changed();
        };
        r.Preset.SelectionChanged += (_, _) =>
        {
            if (_loading || r.Preset.SelectedIndex < 0 || r.Preset.SelectedIndex >= r.Presets.Count) return;
            _loading = true;
            var p = r.Presets[r.Preset.SelectedIndex];
            r.Width.Value = D(p.W); r.Depth.Value = D(p.D);
            ShowDepth(r);
            _loading = false;
            Changed();
        };
        foreach (var n in new[] { r.Width, r.Depth })
            n.ValueChanged += (_, _) => { if (_loading) return; _loading = true; FillPresets(r); _loading = false; Changed(); };
        r.Count.ValueChanged += (_, _) => Changed();
        r.Remove.Click += (_, _) =>
        {
            _sizeRows.Remove(r);
            _sizes.Children.Remove(r.Row);
            Changed();
        };
        _sizeRows.Add(r);
        _sizes.Children.Add(r.Row);
    }

    /// <summary>The row's list of usual sizes, with the one it has picked (or "Your own").</summary>
    static void FillPresets(SizeRow r)
    {
        r.Presets = [.. BaseShapes.For(r.ShapeValue)];
        double w = V(r.Width, 25), d = V(r.Depth, 25);
        bool usesDepth = r.ShapeValue is BaseShape.Oval or BaseShape.Rectangle;
        int at = r.Presets.FindIndex(p => Math.Abs(p.W - w) < 1e-6 && (!usesDepth || Math.Abs(p.D - d) < 1e-6));
        var items = r.Presets.Select(p => $"{BaseShapes.SizeText(p.Shape, p.W, p.D)}: {p.Use}").ToList();
        if (at < 0) items.Add("Your own size");
        r.Preset.ItemsSource = items;
        r.Preset.SelectedIndex = at < 0 ? items.Count - 1 : at;
        ShowDepth(r);
    }

    static void ShowDepth(SizeRow r) => r.Depth.IsEnabled = r.ShapeValue is BaseShape.Oval or BaseShape.Rectangle;

    // ---- the library and the floor ----

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
            int same = _etches.FindIndex(e => e is { } x && x.M.Id == c.M.Id);
            if (same > 0 && EtchRecipe is null) _etchWith.SelectedIndex = same;
            _loading = false;
        }
        _cutNote.Text = CutRecipe is not { } r
            ? "No kerf allowance: the laser burns each edge back by half its kerf, so bases come out a little small and pockets a little big. Pick your material's cut settings (Materials & Tests → Tests → Cut test measures the kerf) for pieces at their drawn size."
            : r.R.Settings.KerfMm > 0 ? $"{r.M.Label}: kerf {r.R.Settings.KerfMm:0.###} mm, allowed for on the cut layer so bases and pockets come out their drawn size."
            : $"{r.M.Label}: no kerf measured yet, so bases come out a little small (run a cut test).";
        Changed();
    }

    /// <summary>The texture and sheet of floor tiles in the design (the floor tile maker's settings saved on them).</summary>
    void MatchFloor()
    {
        TileSpec? floor = null;
        foreach (var shapes in new[] { (IEnumerable<Shape>)_doc.State.Shapes }.Concat(_doc.State.OtherPages.Select(p => (IEnumerable<Shape>)p.Shapes)))
        {
            var withSpec = shapes.Where(x => x.AddonData.ContainsKey(TilePlacement.SpecKey)).ToList();
            var pick = withSpec.FirstOrDefault(x => _doc.Selection.Contains(x.Id)) ?? withSpec.FirstOrDefault();
            if (pick is not null && TileSpec.FromJson(pick.AddonData[TilePlacement.SpecKey]) is { } t) { floor = t; break; }
        }
        if (floor is null) { _floorNote.Text = "There are no floor tiles from the floor tile maker in the design."; return; }
        _thick.Value = (decimal)Math.Clamp(floor.ThicknessMm, 0.5, 20);
        _etch.SelectedIndex = (int)floor.Etch;
        var preset = GroundTextures.Presets.FirstOrDefault(p => p.Floor == floor.Texture);
        string name = preset?.Name ?? floor.Texture;
        // Paving at the floor's own sizes, so the stones on the bases are as big as the floor's.
        _loading = true;
        FillTextures(name);
        if (preset is not null && preset.Floor == "Dungeon flagstones") { _sizeA.Value = D(floor.SizeA); _sizeB.Value = D(Math.Clamp(floor.SquareMm / 3, 2, 100)); }
        else if (preset is not null) { _sizeA.Value = D(Math.Max(0.3, floor.SizeA)); _sizeB.Value = D(Math.Max(0, floor.SizeB)); }
        _groove.Value = D(Math.Clamp(floor.GrooveMm, 0.1, 3));
        _cracks.Value = D(Math.Clamp(floor.CracksPct, 0, 100));
        _loading = false;
        ShowTextureRows();
        _floorNote.Text = $"From the floor: {floor.Texture.ToLowerInvariant()} on {floor.ThicknessMm:0.##} mm sheet.";
        Changed();
    }

    void TrayFromFirst()
    {
        if (_sizeRows.FirstOrDefault()?.Value is not { } b) return;
        _loading = true;
        _trayShape.SelectedIndex = (int)b.Shape;
        _trayW.Value = D(b.WidthMm);
        _trayD.Value = D(b.DepthMm);
        _loading = false;
        Changed();
    }

    // ---- settings in and out ----

    void Hook()
    {
        foreach (var n in new[] { _thick, _layers, _step, _corner, _slotL, _slotW, _magnet, _fit, _sizeA, _sizeB, _groove, _seed, _cracks, _rim, _spot,
                                  _trayW, _trayD, _cols, _rows, _gap, _clear, _border, _trayCorner, _frames, _trays })
            n.ValueChanged += (_, _) => Changed();
        foreach (var c in new[] { _fixing, _etch, _etchWith, _trayShape, _style, _holes })
            c.SelectionChanged += (_, _) => Changed();
        foreach (var c in new[] { _tray, _stagger, _bottom, _trayTexture })
            c.IsCheckedChanged += (_, _) => Changed();
        _cutWith.SelectionChanged += (_, _) => CutChosen();
        _texture.SelectionChanged += (_, _) => TextureChosen();
    }

    void FillTextures(string select)
    {
        _textureNames.Clear();
        _textureNames.AddRange(GroundTextures.Presets.Select(p => p.Name));
        _textureNames.AddRange(_own.All.Select(t => t.Name).Where(n => !_textureNames.Contains(n)));
        if (!_textureNames.Contains(select) && _editing?.Spec.Custom is { } c && c.Name == select) _textureNames.Add(select);
        bool was = _loading;
        _loading = true;
        _texture.ItemsSource = _textureNames.Select(n => GroundTextures.Preset(n) is { } p ? $"{p.Group}: {n}" : $"{n} (yours)").ToList();
        _texture.SelectedIndex = Math.Max(0, _textureNames.IndexOf(select));
        _loading = was;
        ShowTextureRows();
    }

    string TextureName => _texture.SelectedIndex >= 0 && _texture.SelectedIndex < _textureNames.Count ? _textureNames[_texture.SelectedIndex] : GroundTextures.Presets[0].Name;

    CustomTexture? OwnTexture => GroundTextures.Preset(TextureName) is null ? _own.Get(TextureName) ?? (_editing?.Spec.Custom is { } c && c.Name == TextureName ? c : null) : null;

    void TextureChosen()
    {
        if (_loading) return;
        _loading = true;
        if (GroundTextures.Preset(TextureName) is { } p)
        {
            if (p.SizeALabel is not null) _sizeA.Value = D(p.SizeA);
            if (p.SizeBLabel is not null) _sizeB.Value = D(p.SizeB);
            if (p.UsesGroove) _groove.Value = D(p.Groove);
        }
        else if (OwnTexture is { } own) _sizeA.Value = D(Math.Clamp(own.TileWidthMm, 0.5, 300));
        _loading = false;
        ShowTextureRows();
        Changed();
    }

    void ShowTextureRows()
    {
        if (GroundTextures.Preset(TextureName) is { } p)
        {
            _sizeALabel.Text = p.SizeALabel ?? "";
            _sizeBLabel.Text = p.SizeBLabel ?? "";
            _sizeARow.IsVisible = p.SizeALabel is not null;
            _sizeBRow.IsVisible = p.SizeBLabel is not null;
            _grooveRow.IsVisible = p.UsesGroove;
            _seedRow.IsVisible = p.Random;
            _cracksRow.IsVisible = p.Stone;
            _textureAbout.Text = p.About;
        }
        else
        {
            _sizeALabel.Text = "Tile width";
            _sizeARow.IsVisible = true;
            _sizeBRow.IsVisible = _grooveRow.IsVisible = _seedRow.IsVisible = _cracksRow.IsVisible = false;
            var own = OwnTexture;
            _textureAbout.Text = own is null ? "" : $"Your texture: a {own.TileWidthMm:0.#} × {own.TileHeightMm:0.#} mm tile, scaled to the tile width, repeated in rows.";
        }
    }

    void ShowRows()
    {
        _slotRows.IsVisible = _fixing.SelectedIndex == 1;
        _trayRows.IsVisible = _tray.IsChecked == true;
        _trayDRow.IsEnabled = (BaseShape)Math.Max(0, _trayShape.SelectedIndex) is BaseShape.Oval or BaseShape.Rectangle;
    }

    static decimal D(double v) => (decimal)v;
    static double V(NumericUpDown n, double fallback) => n.Value is { } v ? (double)v : fallback;

    void Load(BaseSpec s)
    {
        _loading = true;
        _sizeRows.Clear();
        _sizes.Children.Clear();
        foreach (var b in s.Bases) AddSizeRow(b);
        _thick.Value = D(s.ThicknessMm); _layers.Value = Math.Clamp(s.Layers, 1, 4); _step.Value = D(s.StepMm); _corner.Value = D(s.CornerMm);
        _fixing.SelectedIndex = (int)s.Fixing; _slotL.Value = D(s.SlotLengthMm); _slotW.Value = D(s.SlotWidthMm); _magnet.Value = D(s.MagnetMm); _fit.Value = D(s.FitMm);
        _sizeA.Value = D(s.SizeA); _sizeB.Value = D(Math.Max(0, s.SizeB)); _groove.Value = D(Math.Clamp(s.GrooveMm, 0.1, 3)); _seed.Value = Math.Clamp(s.Seed, 1, 9999); _cracks.Value = D(s.CracksPct);
        _etch.SelectedIndex = (int)s.Etch; _rim.Value = D(s.RimMm); _spot.Value = D(s.SpotMm);
        var t = s.Tray;
        _tray.IsChecked = s.MakeTray;
        _trayShape.SelectedIndex = (int)t.Shape; _trayW.Value = D(t.WidthMm); _trayD.Value = D(t.DepthMm); _cols.Value = Math.Clamp(t.Columns, 1, 40); _rows.Value = Math.Clamp(t.Rows, 1, 40);
        _stagger.IsChecked = t.Stagger; _style.SelectedIndex = (int)t.Style; _gap.Value = D(t.GapMm); _clear.Value = D(t.ClearMm); _border.Value = D(t.BorderMm); _trayCorner.Value = D(t.CornerMm);
        _frames.Value = Math.Clamp(t.FrameLayers, 1, 4); _bottom.IsChecked = t.Bottom; _holes.SelectedIndex = (int)t.Holes; _trayTexture.IsChecked = t.Textured; _trays.Value = Math.Clamp(t.Count, 1, 50);
        _loading = false;
        FillTextures(s.Texture);
        ShowRows();
        CutChosen();
    }

    BaseSpec Current() => new()
    {
        Bases = [.. _sizeRows.Select(r => r.Value)],
        ThicknessMm = V(_thick, 3), Layers = (int)V(_layers, 1), StepMm = V(_step, 0.8), CornerMm = V(_corner, 1),
        Fixing = (FigureFixing)Math.Max(0, _fixing.SelectedIndex), SlotLengthMm = V(_slotL, 20), SlotWidthMm = V(_slotW, 2.2), MagnetMm = V(_magnet, 0), FitMm = V(_fit, 0.15),
        Texture = TextureName, Custom = OwnTexture, SizeA = V(_sizeA, 4), SizeB = V(_sizeB, 0), GrooveMm = V(_groove, 0.5), Seed = (int)V(_seed, 1), CracksPct = V(_cracks, 10),
        Etch = (EtchStyle)Math.Max(0, _etch.SelectedIndex), RimMm = V(_rim, 1), SpotMm = V(_spot, 0),
        MakeTray = _tray.IsChecked == true,
        Tray = new TraySpec
        {
            Shape = (BaseShape)Math.Max(0, _trayShape.SelectedIndex), WidthMm = V(_trayW, 25), DepthMm = V(_trayD, 25), Columns = (int)V(_cols, 5), Rows = (int)V(_rows, 2),
            Stagger = _stagger.IsChecked == true, Style = (TrayStyle)Math.Max(0, _style.SelectedIndex), GapMm = V(_gap, 1), ClearMm = V(_clear, 0.4), BorderMm = V(_border, 4),
            CornerMm = V(_trayCorner, 3), FrameLayers = (int)V(_frames, 1), Bottom = _bottom.IsChecked == true, Holes = (TrayHoles)Math.Max(0, _holes.SelectedIndex),
            Textured = _trayTexture.IsChecked == true, Count = (int)V(_trays, 1),
        },
    };

    string? LastFile => _dataFolder is null ? null : Path.Combine(_dataFolder, "last.json");

    BaseSpec? LoadLast()
    {
        try { return LastFile is { } f && File.Exists(f) ? BaseSpec.FromJson(File.ReadAllText(f)) : null; }
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
        BaseKit kit;
        try { kit = await Task.Run(() => BaseKitMaker.Make(spec)); }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or ArithmeticException)
        {
            if (gen == _generation) _preview.Show(null, e.Message);
            return;
        }
        if (gen != _generation) return;
        _kit = kit;
        _kitFrom = spec;
        var findings = kit.Findings.Concat(BasePlacement.TooBig(kit, _machine)).ToList();
        _preview.Show(kit.Parts.Count > 0 ? kit : null, findings.FirstOrDefault(f => f.Level == Severity.Bad)?.Text ?? "Nothing to show.");
        _findings.Children.Clear();
        foreach (var f in findings.OrderByDescending(f => f.Level)) _findings.Children.Add(FindingRow(f));
        int bases = kit.BaseCount, pieces = kit.PieceCount, trayPieces = kit.Tray is { } t ? t.Count * (t.Frames.Count + (t.Bottom is null ? 0 : 1)) : 0;
        _summary.Text = kit.Parts.Count == 0 ? "" :
            $"{pieces} piece{(pieces == 1 ? "" : "s")} to cut: " +
            string.Join(", ", new[]
            {
                bases > 0 ? $"{bases} base{(bases == 1 ? "" : "s")}" + (spec.LayerCount > 1 ? $" in {spec.LayerCount} layers" : "") : null,
                trayPieces > 0 ? $"{trayPieces} for {(kit.Tray!.Count == 1 ? "the tray" : $"{kit.Tray.Count} trays")}" : null,
            }.Where(x => x is not null)) + ".";
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
        _status.Text = "Putting the bases in the design…";
        try
        {
            var spec = Current();
            var built = _kit is not null && _kitFrom?.ToJson() == spec.ToJson() ? _kit : null;
            Func<Layer, Layer>? cut = CutRecipe is { } c ? l => c.R.Settings.ApplyTo(l) with { Material = new MaterialRef(c.M.Id, c.M.Label, c.R.Id, c.R.Title) } : null;
            Func<Layer, Layer>? etch = EtchRecipe is { } e ? l => e.R.Settings.ApplyTo(l) with { Material = new MaterialRef(e.M.Id, e.M.Label, e.R.Id, e.R.Title) } : null;
            Guid? replace = _editing is { } ed && BasePlacement.ShapesOf(_doc.State, ed.Id).Count + _doc.State.OtherPages.Sum(p => p.Shapes.Count(x => x.AddonData.TryGetValue(BasePlacement.IdKey, out var v) && v == ed.Id.ToString())) > 0 ? ed.Id : null;
            var before = _doc.State;
            var (kit, s, sheets, id) = await Task.Run(() =>
            {
                var kit = built ?? BaseKitMaker.Make(spec);
                if (!kit.Ok) throw new InvalidOperationException(kit.Findings.First(f => f.Level == Severity.Bad).Text);
                var (s, sheets, id) = BasePlacement.Place(before, kit, spec, _machine, new BasePlaceOptions { CutSettings = cut, EtchSettings = etch, Replace = replace });
                return (kit, s, sheets, id);
            });
            if (!ReferenceEquals(_doc.State, before)) { _status.Text = "The design changed meanwhile: press again to put the bases in."; return; }
            _doc.Commit(replace is null ? "Miniature bases" : "Update the bases", s, BasePlacement.ShapesOf(s, id));
            _editing = (id, spec);
            _make.Content = "Update in the design";
            int pieces = kit.PieceCount;
            _status.Text = $"{(replace is null ? "Put" : "Updated")} {pieces} piece{(pieces == 1 ? "" : "s")} in the design" +
                           (sheets > 1 ? $", over {sheets} pages (a bed each)" : "") +
                           $": outlines on \"{BasePlacement.CutLayer}\", the texture on \"{BasePlacement.EtchLayer}\" and \"{BasePlacement.LineLayer}\" (done first).";
        }
        catch (InvalidOperationException ex) { _status.Text = ex.Message; }
        catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException or ArithmeticException or NullReferenceException)
        {
            AppLog.Error("bases", ex);
            _status.Text = $"Couldn't put the bases in: {ex.Message}";
        }
        finally
        {
            _making = false;
            _make.IsEnabled = _kit?.Ok ?? true;
        }
    }
}
