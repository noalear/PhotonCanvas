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

namespace PhotonCanvas.Addon.Bridges;

/// <summary>
/// The bridge maker: the kind of bridge, its size and style, and the sheet it's cut from; its pieces
/// flat and put together in 3D; then every piece goes into the design, nested onto the bed.
/// </summary>
public sealed class BridgesWindow : Window
{
    readonly Document _doc;
    readonly MachineProfile _machine;
    readonly MaterialLibrary? _library;
    readonly string? _dataFolder;
    (Guid Id, BridgeSpec Spec)? _editing;
    BridgeSpec _spec;

    // The bridge
    readonly ComboBox _kind = Combo([.. Defaults.Kinds.Select(k => k.Name)]);
    readonly TextBlock _about = Hint();
    readonly NumericUpDown _count = Number(1, 100, 1, "0");
    readonly NumericUpDown _len = Number(10, 2000, 1, "0.#"), _wid = Number(1, 500, 0.5m, "0.#"), _h = Number(0, 500, 0.5m, "0.#"), _rise = Number(0, 500, 0.5m, "0.#");
    readonly TextBlock _hLabel = Label("Height"), _riseLabel = Label("Rise");
    readonly ComboBox _ends = Combo("The table", "Banks as high as its ends");
    Control _endsRow = null!, _hRow = null!, _riseRow = null!;

    // Planks and ropes
    readonly NumericUpDown _plank = Number(1, 50, 0.5m, "0.#"), _gap = Number(0, 10, 0.1m, "0.##"), _missing = Number(0, 50, 1, "0");
    readonly CheckBox _rough = new() { Content = "Rough planks (crooked ends, different lengths)" };
    readonly NumericUpDown _beam = Number(1, 100, 0.5m, "0.#"), _legs = Number(0, 20, 1, "0"), _legW = Number(1, 50, 0.5m, "0.#"), _post = Number(1, 50, 0.5m, "0.#");
    readonly CheckBox _railing = new();
    readonly NumericUpDown _railH = Number(2, 200, 0.5m, "0.#"), _every = Number(2, 50, 1, "0");
    readonly TextBlock _everyLabel = Label("Post every");
    Control _plankGroup = null!, _beamRows = null!, _legRows = null!, _missingRow = null!, _postRow = null!, _railGroup = null!;

    // Stone
    readonly NumericUpDown _arches = Number(1, 9, 1, "0"), _span = Number(4, 1000, 1, "0.#"), _spring = Number(0, 200, 0.5m, "0.#"), _ring = Number(0.5m, 30, 0.5m, "0.#"), _pier = Number(0, 200, 0.5m, "0.#");
    readonly ComboBox _archShape = Combo("Round", "Segmental (flatter)", "Pointed");
    readonly ComboBox _road = Combo("Level", "Humped (ramps up from the ends)"), _parapet = Combo("Straight", "Battlements", "None");
    readonly NumericUpDown _endH = Number(1, 500, 0.5m, "0.#"), _parapetH = Number(2, 100, 0.5m, "0.#");
    readonly ComboBox _surface = Combo("Cobbles", "Flagstones", "Plain");
    readonly ComboBox _texture = Combo();
    readonly NumericUpDown _sizeA = Number(0.5m, 200, 0.5m, "0.#"), _sizeB = Number(0.5m, 200, 0.5m, "0.#"), _groove = Number(0.1m, 3, 0.05m, "0.##");
    readonly TextBlock _sizeALabel = Label("Size"), _sizeBLabel = Label("Size"), _textureAbout = Hint();
    Control _sizeARow = null!, _sizeBRow = null!, _grooveRow = null!, _endHRow = null!, _parapetHRow = null!, _pierRow = null!;
    readonly Button _match = UiKit.Button("Match the walls in the design", tip: "Takes the stone texture, its sizes and the sheet from walls made with Walls (the ones selected, or the first in the design).");
    readonly TextBlock _matchNote = Hint();
    Control _stoneGroup = null!;
    List<string> _textures = [];

    // The sheet
    readonly ComboBox _scale = Combo("15 mm", "28 mm", "32 mm", "54 mm");
    static readonly double[] Scales = [15, 28, 32, 54];
    readonly NumericUpDown _thick = Number(0.3m, 20, 0.5m, "0.##"), _fit = Number(-0.5m, 2, 0.05m, "0.##"), _seed = Number(1, 9999, 1, "0");
    readonly CheckBox _grain = new() { Content = "Wood grain and rope twist scored on" };
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
    BridgeKit? _kit;
    string? _kitFrom;

    public BridgesWindow(Document doc, MachineProfile machine, MaterialLibrary? library, string? dataFolder, (Guid Id, BridgeSpec Spec)? editing)
    {
        _doc = doc;
        _machine = machine;
        _library = library;
        _dataFolder = dataFolder;
        _editing = editing;
        _spec = editing?.Spec ?? LoadLast() ?? Defaults.For(BridgeKind.Plank);
        Title = "Miniature bridges";
        Width = 1100;
        Height = 780;
        MinWidth = 860;
        MinHeight = 580;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");

        Library();
        var tabs = new TabControl { Items = { Tab("Bridge", BridgeTab()), Tab("Sheet", SheetTab()) } };
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
        LoadBridge();
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

    Control BridgeTab()
    {
        _endsRow = Field("Ends stand on", _ends, tip: "The table, or two hills, banks or cliffs as high as the ends of the deck, with the bridge between them.");
        _hRow = Line(_hLabel, _h);
        _riseRow = Line(_riseLabel, _rise);
        _missingRow = Field("Missing planks", _missing, tip: "Planks left out here and there, for a rickety look.");
        _beamRows = Stack(Field("Beam depth", _beam, tip: "How deep the side beams are under the deck (mm)."));
        _legRows = Stack(Field("Middle legs", _legs, tip: "Legs under the middle of a long bridge, besides the ones at the ends."), Field("Leg width", _legW, tip: "How wide the legs are (mm)."));
        _postRow = Field("Post thickness", _post, tip: "How thick the posts at the ends are (mm).");
        _plankGroup = Explained("Planks", "Each plank drops into a notch in both sides; a narrow neck where it crosses a side stops it sliding across.",
            Field("Plank width", _plank, tip: "How wide each plank is (mm)."),
            Field("Gap", _gap, tip: "The gap between planks (mm)."),
            _rough, _missingRow, _beamRows, _legRows);
        _railGroup = Explained("Railing", "",
            _railing,
            Field("Height", _railH, tip: "How high the railing or hand rope is over the deck (mm)."),
            Line(_everyLabel, _every, "A post (or a rope down to the deck) after every so many planks."),
            _postRow);
        _sizeARow = Line(_sizeALabel, _sizeA);
        _sizeBRow = Line(_sizeBLabel, _sizeB);
        _grooveRow = Field("Groove", _groove, tip: "How wide the etched gaps between the stones are (mm).");
        _endHRow = Field("End height", _endH, tip: "How high the road is at the ends (mm).");
        _parapetHRow = Field("Parapet height", _parapetH, tip: "How high the parapet walls stand over the road (mm).");
        _pierRow = Field("Pier width", _pier, tip: "How wide the piers between the arches are (mm). 0 = to suit the arches.");
        _stoneGroup = Stack(
            Explained("Arches", "Cut out of both side walls, with a ring of wedge-shaped stones round each.",
                Field("Arches", _arches),
                Field("Arch width", _span, tip: "Each arch's width at the bottom (mm)."),
                Field("Arch shape", _archShape),
                Field("Straight sides", _spring, tip: "How high the arch's sides go straight up before it curves (mm)."),
                Field("Ring depth", _ring, tip: "How deep the ring of stones round each arch is (mm)."),
                _pierRow),
            Explained("Road and parapet", "",
                Field("Road", _road), _endHRow,
                Field("Parapet", _parapet), _parapetHRow,
                Field("Road surface", _surface)),
            Explained("Stone", "The wall maker's textures, etched on the outer faces.",
                Field("Texture", _texture), _textureAbout, _sizeARow, _sizeBRow, _grooveRow, _match, _matchNote));
        return Stack(
            Explained("Bridge", "",
                Field("Kind", _kind), _about,
                Field("How many", _count)),
            Explained("Size", "All in mm: the length end to end, the width of the walkway or road between the sides.",
                Field("Length", _len), Field("Width", _wid, tip: "The walkway or road between the sides (mm)."),
                _endsRow, _hRow, _riseRow),
            _plankGroup, _railGroup, _stoneGroup);
    }

    Control SheetTab()
    {
        var shuffle = UiKit.Button("Shuffle");
        shuffle.HorizontalAlignment = HorizontalAlignment.Left;
        shuffle.Click += (_, _) => _seed.Value = (_seed.Value ?? 1) % 9999 + 1;
        return Stack(
            Explained("Sheet", "Every piece is cut from one sheet: its thickness sets every slot and notch. 2 mm suits plank and rope bridges at 28 mm, 3 mm stone ones.",
                Field("Thickness", _thick, tip: "The sheet's thickness (mm)."),
                Field("Slot fit", _fit, tip: "Added to the sheet's thickness for the slots and notches (mm): more is looser. 0.1 is a snug fit to glue."),
                Field("Figures", _scale, tip: "Changing it scales every size to suit figures that size.")),
            Explained("Detail", "Scored and etched on the faces that show.",
                _grain,
                Field("Variation", new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 6, Children = { _seed, shuffle.With(b => Grid.SetColumn(b, 1)) } },
                    tip: "Stones, grain and rough planks are laid out at random: each number gives a different layout.")),
            Explained("Material", "Picking your material's settings brings its thickness and, from your cut test, its kerf and slot fit.",
                Field("Cut with", _cutWith),
                Field("Etch with", _etchWith, tip: "Settings for the stone and the road (an engrave/fill recipe from your library)."),
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
        foreach (var n in new[] { _count, _len, _wid, _h, _rise, _plank, _gap, _missing, _beam, _legs, _legW, _post, _railH, _every,
                                  _arches, _span, _spring, _ring, _pier, _endH, _parapetH, _sizeA, _sizeB, _groove })
            n.ValueChanged += (_, _) => BridgeChanged();
        foreach (var c in new[] { _ends, _archShape, _road, _parapet, _surface })
            c.SelectionChanged += (_, _) => BridgeChanged();
        foreach (var c in new[] { _rough, _railing })
            c.IsCheckedChanged += (_, _) => BridgeChanged();
        _texture.SelectionChanged += (_, _) => TextureChosen();
        _kind.SelectionChanged += (_, _) => KindChosen();
        foreach (var n in new[] { _thick, _fit, _seed }) n.ValueChanged += (_, _) => SheetChanged();
        _grain.IsCheckedChanged += (_, _) => SheetChanged();
        _etchWith.SelectionChanged += (_, _) => SheetChanged();
        _cutWith.SelectionChanged += (_, _) => CutChosen();
        _scale.SelectionChanged += (_, _) => ScaleChosen();
        _match.Click += (_, _) => MatchWalls();
    }

    static decimal D(double v) => (decimal)v;
    static double V(NumericUpDown n, double fallback) => n.Value is { } v ? (double)v : fallback;

    void KindChosen()
    {
        if (_loading) return;
        var kind = Defaults.Kinds[Math.Clamp(_kind.SelectedIndex, 0, Defaults.Kinds.Count - 1)].Kind;
        if (kind == _spec.Kind) return;
        // A new kind starts from its own sizes (for the figures chosen); the sheet stays if it came from the library.
        var next = Defaults.For(kind);
        if (Math.Abs(_spec.ScaleMm - 28) > 1e-9) next = Defaults.Scaled(next, _spec.ScaleMm / 28);
        next = next with { Count = _spec.Count, Seed = _spec.Seed, Grain = _spec.Grain, ScaleMm = _spec.ScaleMm };
        if (CutRecipe is not null) next = next with { ThicknessMm = _spec.ThicknessMm, FitMm = _spec.FitMm };
        _spec = next;
        LoadSheet();
        LoadBridge();
        Changed();
    }

    void ScaleChosen()
    {
        if (_loading) return;
        double to = Scales[Math.Clamp(_scale.SelectedIndex, 0, Scales.Length - 1)], k = to / Math.Max(1, _spec.ScaleMm);
        if (Math.Abs(k - 1) < 1e-9) return;
        _spec = Defaults.Scaled(_spec, k) with { ScaleMm = to };
        LoadBridge();
        Changed();
    }

    void LoadSheet()
    {
        bool was = _loading;
        _loading = true;
        _thick.Value = D(_spec.ThicknessMm); _fit.Value = D(_spec.FitMm); _seed.Value = Math.Clamp(_spec.Seed, 1, 9999);
        _grain.IsChecked = _spec.Grain;
        int sc = Array.FindIndex(Scales, x => Math.Abs(x - _spec.ScaleMm) < 1e-6);
        _scale.SelectedIndex = sc < 0 ? 1 : sc;
        _loading = was;
        if (!was) CutChosen();
    }

    void LoadBridge()
    {
        var s = _spec;
        var kind = s.Kind;
        bool was = _loading;
        _loading = true;
        _kind.SelectedIndex = (int)kind;
        _about.Text = Defaults.Info(kind).About;
        _count.Value = Math.Max(1, s.Count);
        _len.Value = D(s.LengthMm); _wid.Value = D(s.WidthMm); _h.Value = D(s.HeightMm); _rise.Value = D(s.RiseMm);
        _ends.SelectedIndex = (int)s.Ends;
        _plank.Value = D(s.PlankMm); _gap.Value = D(s.GapMm); _missing.Value = Math.Max(0, s.MissingPlanks); _rough.IsChecked = s.Rough;
        _beam.Value = D(s.BeamMm); _legs.Value = Math.Max(0, s.Legs); _legW.Value = D(s.LegMm); _post.Value = D(s.PostMm);
        _railing.IsChecked = s.Railing; _railH.Value = D(s.RailMm); _every.Value = Math.Max(2, s.PostEvery);
        _arches.Value = Math.Clamp(s.Arches, 1, 9); _span.Value = D(s.ArchSpanMm); _archShape.SelectedIndex = (int)s.Arch;
        _spring.Value = D(s.SpringMm); _ring.Value = D(s.RingMm); _pier.Value = D(s.PierMm);
        _road.SelectedIndex = (int)s.Road; _endH.Value = D(s.EndHeightMm); _parapet.SelectedIndex = (int)s.Parapet; _parapetH.Value = D(s.ParapetMm);
        _surface.SelectedIndex = (int)s.Surface;
        _textures = [.. Textures.Presets.Select(p => p.Name)];
        if (s.Custom is { } c && !_textures.Contains(c.Name)) _textures.Add(c.Name);
        _texture.ItemsSource = _textures;
        _texture.SelectedIndex = Math.Max(0, _textures.IndexOf(s.Texture));
        _sizeA.Value = D(s.SizeA); _sizeB.Value = D(s.SizeB); _groove.Value = D(Math.Clamp(s.GrooveMm, 0.1, 3));
        _matchNote.Text = "";

        bool stone = kind == BridgeKind.Stone, rope = kind == BridgeKind.Rope, table = s.Ends == EndsOn.Table;
        _hLabel.Text = stone ? "Road height" : "Deck height";
        ToolTip.SetTip(_h, stone ? "How high the road is over the arches (mm)." : "How high the deck is at the ends, above the table (mm).");
        _riseLabel.Text = rope ? "Sag" : "Arch rise";
        ToolTip.SetTip(_rise, rope ? "How far the middle hangs below the ends (mm)." : "How much higher the middle is than the ends (mm). 0 = level.");
        _endsRow.IsVisible = !stone;
        _hRow.IsVisible = stone || table;
        _riseRow.IsVisible = !stone;
        _plankGroup.IsVisible = _railGroup.IsVisible = !stone;
        _stoneGroup.IsVisible = stone;
        _missingRow.IsVisible = rope;
        _beamRows.IsVisible = kind == BridgeKind.Plank;
        _legRows.IsVisible = kind == BridgeKind.Plank && table;
        _postRow.IsVisible = rope;
        _railing.Content = rope ? "A hand rope" : "A railing";
        _everyLabel.Text = rope ? "Rope every" : "Post every";
        _endHRow.IsVisible = s.Road == RoadShape.Humped;
        _parapetHRow.IsVisible = s.Parapet != ParapetTop.None;
        _pierRow.IsVisible = s.Arches > 1;
        ShowTextureSizes();
        _loading = was;
    }

    void ShowTextureSizes()
    {
        var p = Textures.Preset(_spec.Texture);
        bool custom = p is null && _spec.Custom is { } c && c.Name == _spec.Texture;
        _sizeALabel.Text = custom ? "Tile width" : p?.SizeALabel ?? "";
        _sizeBLabel.Text = p?.SizeBLabel ?? "";
        _sizeARow.IsVisible = custom || p?.SizeALabel is not null;
        _sizeBRow.IsVisible = p?.SizeBLabel is not null;
        _grooveRow.IsVisible = p?.UsesGroove ?? false;
        _textureAbout.Text = custom ? "Your own texture, from the wall maker." : p?.About ?? "";
    }

    void TextureChosen()
    {
        if (_loading || _texture.SelectedIndex < 0 || _texture.SelectedIndex >= _textures.Count) return;
        string name = _textures[_texture.SelectedIndex];
        if (name == _spec.Texture) return;
        // A preset brings its own sizes.
        _spec = Textures.Preset(name) is { } p
            ? _spec with { Texture = name, SizeA = p.SizeA > 0 ? p.SizeA : _spec.SizeA, SizeB = p.SizeB > 0 ? p.SizeB : _spec.SizeB, GrooveMm = p.UsesGroove ? p.Groove : _spec.GrooveMm }
            : _spec with { Texture = name };
        LoadBridge();
        Changed();
    }

    /// <summary>The bridge as the settings say.</summary>
    BridgeSpec FromControls()
    {
        var s = _spec;
        return s with
        {
            Count = (int)V(_count, 1),
            LengthMm = V(_len, s.LengthMm), WidthMm = V(_wid, s.WidthMm), HeightMm = V(_h, s.HeightMm), RiseMm = V(_rise, s.RiseMm),
            Ends = (EndsOn)Math.Max(0, _ends.SelectedIndex),
            PlankMm = V(_plank, s.PlankMm), GapMm = V(_gap, s.GapMm), MissingPlanks = (int)V(_missing, 0), Rough = _rough.IsChecked == true,
            BeamMm = V(_beam, s.BeamMm), Legs = (int)V(_legs, 0), LegMm = V(_legW, s.LegMm), PostMm = V(_post, s.PostMm),
            Railing = _railing.IsChecked == true, RailMm = V(_railH, s.RailMm), PostEvery = (int)V(_every, s.PostEvery),
            Arches = (int)V(_arches, 1), ArchSpanMm = V(_span, s.ArchSpanMm), Arch = (ArchShape)Math.Max(0, _archShape.SelectedIndex),
            SpringMm = V(_spring, s.SpringMm), RingMm = V(_ring, s.RingMm), PierMm = V(_pier, 0),
            Road = (RoadShape)Math.Max(0, _road.SelectedIndex), EndHeightMm = V(_endH, s.EndHeightMm),
            Parapet = (ParapetTop)Math.Max(0, _parapet.SelectedIndex), ParapetMm = V(_parapetH, s.ParapetMm),
            Surface = (RoadSurface)Math.Max(0, _surface.SelectedIndex),
            SizeA = V(_sizeA, s.SizeA), SizeB = V(_sizeB, s.SizeB), GrooveMm = V(_groove, s.GrooveMm),
        };
    }

    void BridgeChanged()
    {
        if (_loading) return;
        var before = _spec;
        _spec = FromControls();
        // Some settings show or hide others.
        if (before.Ends != _spec.Ends || before.Road != _spec.Road || before.Parapet != _spec.Parapet || (before.Arches > 1) != (_spec.Arches > 1)) LoadBridge();
        Changed();
    }

    void SheetChanged()
    {
        if (_loading) return;
        _spec = _spec with { ThicknessMm = V(_thick, 2), FitMm = V(_fit, 0.1), Seed = (int)V(_seed, 1), Grain = _grain.IsChecked == true };
        Changed();
    }

    void MatchWalls()
    {
        if (WallsLink.Find(_doc.State, _doc.Selection) is not { } w)
        {
            _matchNote.Text = "There are no walls from Walls in the design. Pick a texture here instead.";
            return;
        }
        _spec = WallsLink.Fit(_spec, w);
        LoadSheet();
        LoadBridge();
        _matchNote.Text = $"Matched: {w.Texture}, cut from {w.SheetMm:0.##} mm sheet like the walls.";
        Changed();
    }

    string? LastFile => _dataFolder is null ? null : Path.Combine(_dataFolder, "last.json");

    BridgeSpec? LoadLast()
    {
        try { return LastFile is { } f && File.Exists(f) ? BridgeSpec.FromJson(File.ReadAllText(f)) : null; }
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
        BridgeKit kit;
        try { kit = await Task.Run(() => BridgeKits.Make(spec)); }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or ArithmeticException)
        {
            if (gen == _generation) { _pieces.Show(null, e.Message); _assembled.Show(null, spec.ThicknessMm, e.Message); }
            return;
        }
        if (gen != _generation) return;
        _kit = kit;
        _kitFrom = spec.ToJson();
        var all = kit.Findings.Concat(Placement.TooBig(kit, _machine)).ToList();
        string? bad = all.FirstOrDefault(f => f.Level == Severity.Bad)?.Text;
        _pieces.Show(kit.Parts.Count > 0 ? kit : null, bad ?? "Nothing to show.");
        _assembled.Show(kit.Parts.Count > 0 ? kit : null, spec.ThicknessMm, bad ?? "Nothing to show.");
        _findings.Children.Clear();
        foreach (var f in all.OrderByDescending(f => f.Level)) _findings.Children.Add(FindingRow(f));
        int pieces = kit.PieceCount, each = kit.Parts.Sum(p => p.Copies);
        string name = Defaults.Info(spec.Kind).Name.ToLowerInvariant();
        _summary.Text = pieces == 0 ? "" :
            spec.Count > 1 ? $"{pieces} pieces to cut for {spec.Count} {name}s ({each} for each)." : $"{pieces} piece{(pieces == 1 ? "" : "s")} to cut for the {name}.";
        _make.IsEnabled = kit.Ok && pieces > 0 && bad is null;
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
            var spec = _spec;
            var built = _kit is not null && _kitFrom == spec.ToJson() ? _kit : null;
            Func<Layer, Layer>? cut = CutRecipe is { } c ? l => c.R.Settings.ApplyTo(l) with { Material = new MaterialRef(c.M.Id, c.M.Label, c.R.Id, c.R.Title) } : null;
            Func<Layer, Layer>? etch = EtchRecipe is { } e ? l => e.R.Settings.ApplyTo(l) with { Material = new MaterialRef(e.M.Id, e.M.Label, e.R.Id, e.R.Title) } : null;
            Guid? replace = _editing is { } ed && Placement.ShapesOf(_doc.State, ed.Id).Count + _doc.State.OtherPages.Sum(p => p.Shapes.Count(x => x.AddonData.TryGetValue(Placement.IdKey, out var v) && v == ed.Id.ToString())) > 0 ? ed.Id : null;
            var before = _doc.State;
            var (kit, s, sheets, id) = await Task.Run(() =>
            {
                var kit = built ?? BridgeKits.Make(spec);
                if (!kit.Ok) throw new InvalidOperationException(kit.Findings.FirstOrDefault(f => f.Level == Severity.Bad)?.Text ?? "Nothing to cut.");
                var (s, sheets, id) = Placement.Place(before, kit, spec, _machine, new PlaceOptions { CutSettings = cut, EtchSettings = etch, Replace = replace });
                return (kit, s, sheets, id);
            });
            if (!ReferenceEquals(_doc.State, before)) { _status.Text = "The design changed meanwhile: press again to put the pieces in."; return; }
            _doc.Commit(replace is null ? "Bridge" : "Update the bridge", s, Placement.ShapesOf(s, id));
            _editing = (id, spec);
            _make.Content = "Update in the design";
            int pieces = kit.PieceCount;
            _status.Text = $"{(replace is null ? "Put" : "Updated")} {pieces} piece{(pieces == 1 ? "" : "s")} in the design" +
                           (sheets > 1 ? $", over {sheets} pages (a bed each)" : "") +
                           $": outlines on \"{Placement.CutLayer}\", the detail on \"{Placement.EtchLayer}\" and \"{Placement.LineLayer}\" (done first).";
        }
        catch (InvalidOperationException ex) { _status.Text = ex.Message; }
        catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException or ArithmeticException or NullReferenceException)
        {
            AppLog.Error("bridges", ex);
            _status.Text = $"Couldn't put the bridge in: {ex.Message}";
        }
        finally
        {
            _making = false;
            _make.IsEnabled = _kit?.Ok ?? true;
        }
    }
}
