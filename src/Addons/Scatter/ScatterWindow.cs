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

namespace PhotonCanvas.Addon.Scatter;

/// <summary>
/// The scatter terrain maker: a list of the scenery to cut (each with how many), the chosen one's size
/// and style, and the sheet it's all cut from; the chosen one's pieces flat and put together in 3D;
/// then every piece goes into the design, nested onto the bed.
/// </summary>
public sealed class ScatterWindow : Window
{
    readonly Document _doc;
    readonly MachineProfile _machine;
    readonly MaterialLibrary? _library;
    readonly string? _dataFolder;
    (Guid Id, ScatterSet Spec)? _editing;
    ScatterSet _set;
    int _sel;

    // The list
    readonly ListBox _list = new() { MinHeight = 120, MaxHeight = 180 };
    readonly ComboBox _addKind = Combo([.. Defaults.Kinds.Select(k => k.Name)]);
    readonly Button _add = UiKit.Button("Add"), _copy = UiKit.Button("Copy"), _remove = UiKit.Button("Remove");

    // The chosen piece
    readonly TextBlock _title = new() { FontWeight = FontWeight.SemiBold }, _about = Hint();
    readonly NumericUpDown _count = Number(1, 500, 1, "0");
    readonly NumericUpDown _w = Number(1, 1000, 0.5m, "0.#"), _d = Number(0, 1000, 0.5m, "0.#"), _h = Number(1, 1000, 0.5m, "0.#");
    readonly TextBlock _wLabel = Label("Width"), _dLabel = Label("Depth"), _hLabel = Label("Height");
    Control _dRow = null!;
    // Barrels
    readonly CheckBox _hoops = new() { Content = "Raised hoops" }, _open = new() { Content = "Open at the top, with water inside" };
    // Fences
    readonly ComboBox _style = Combo("Pickets", "Posts and rails", "Planks", "Wattle");
    readonly CheckBox _feet = new() { Content = "Feet, so it stands up on its own" };
    // Carts
    readonly NumericUpDown _wheel = Number(4, 200, 0.5m, "0.#");
    readonly CheckBox _shafts = new() { Content = "Shafts down to the ground (otherwise a leg)" };
    // Stalls
    readonly NumericUpDown _counter = Number(2, 500, 0.5m, "0.#"), _rise = Number(0, 200, 0.5m, "0.#");
    readonly CheckBox _stripes = new() { Content = "Stripes on the awning" }, _scallops = new() { Content = "A scalloped edge along the front" };
    // Wells
    readonly NumericUpDown _top = Number(5, 1000, 0.5m, "0.#");
    readonly CheckBox _roof = new() { Content = "A roof and a winch" }, _shingles = new() { Content = "Shingles on the roof" };
    readonly ComboBox _texture = Combo([.. StoneFace.Presets.Select(p => p.Name)]);
    // Gravestones
    readonly ComboBox _shape = Combo("Rounded", "Square", "Pointed", "Cross", "Celtic cross");
    readonly NumericUpDown _slab = Number(0, 500, 1, "0.#");
    readonly CheckBox _inscription = new() { Content = "R.I.P. and lettering" }, _weathered = new() { Content = "Weathered: chips and cracks" };
    Control _barrelRows = null!, _fenceRows = null!, _cartRows = null!, _stallRows = null!, _wellRows = null!, _graveRows = null!;

    // The sheet
    readonly ComboBox _scale = Combo("15 mm", "28 mm", "32 mm", "54 mm");
    static readonly double[] Scales = [15, 28, 32, 54];
    readonly NumericUpDown _thick = Number(0.3m, 20, 0.5m, "0.##"), _fit = Number(-0.5m, 2, 0.05m, "0.##"), _seed = Number(1, 9999, 1, "0");
    readonly ComboBox _detail = Combo("None", "Wood grain", "Planks and grain");
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

    public ScatterWindow(Document doc, MachineProfile machine, MaterialLibrary? library, string? dataFolder, (Guid Id, ScatterSet Spec)? editing)
    {
        _doc = doc;
        _machine = machine;
        _library = library;
        _dataFolder = dataFolder;
        _editing = editing;
        _set = editing?.Spec ?? LoadLast() ?? new ScatterSet();
        if (_set.Items.Count == 0) _set = _set with { Items = [Defaults.For(ItemKind.Barrel)] };
        Title = "Scatter terrain";
        Width = 1100;
        Height = 780;
        MinWidth = 860;
        MinHeight = 580;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");

        Library();
        var tabs = new TabControl { Items = { Tab("Scenery", SceneryTab()), Tab("Sheet", SheetTab()) } };
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

    Control SceneryTab()
    {
        var addRow = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 6, Children = { _addKind, _add.With(b => { Grid.SetColumn(b, 1); b.MinWidth = 70; }) } };
        ToolTip.SetTip(_addKind, "Pick a piece of scenery and press Add to put it on the list.");
        _barrelRows = Stack(_hoops, _open);
        _fenceRows = Stack(Field("Fence", _style), _feet);
        _cartRows = Stack(Field("Wheels", _wheel, tip: "The wheels' diameter (mm)."), _shafts);
        _stallRows = Stack(Field("Counter", _counter, tip: "How high the counter top is (mm)."),
            Field("Back higher by", _rise, tip: "How much higher the awning is at the back than at the front (mm), so the rain runs off."), _stripes, _scallops);
        _wellRows = Stack(Field("Stonework", _texture, tip: "The stone etched on the wall, laid out like the wall maker's texture of the same name."),
            _roof, Field("Top", _top, tip: "The height of the top of the roof (mm)."), _shingles);
        _graveRows = Stack(Field("Shape", _shape), Field("Grave slab", _slab, tip: "A grave slab in front of the stone, this long (mm). 0 = just a small base."),
            _inscription, _weathered);
        _dRow = Line(_dLabel, _d);
        return Stack(
            Explained("Scenery to cut", "Everything on the list is cut from the same sheet and nested onto the bed together. Pick one to change it.",
                _list,
                addRow,
                Row(_copy, _remove)),
            Explained("This piece", "",
                _title, _about,
                Field("How many", _count),
                Line(_wLabel, _w), _dRow, Line(_hLabel, _h),
                _barrelRows, _fenceRows, _cartRows, _stallRows, _wellRows, _graveRows));
    }

    Control SheetTab()
    {
        var shuffle = UiKit.Button("Shuffle");
        shuffle.HorizontalAlignment = HorizontalAlignment.Left;
        shuffle.Click += (_, _) => _seed.Value = (_seed.Value ?? 1) % 9999 + 1;
        return Stack(
            Explained("Sheet", "Everything is cut from one sheet: its thickness sets every slot and tab, and how many layers a barrel has. Thin sheet (1.5 to 2 mm) looks best at 28 mm.",
                Field("Thickness", _thick, tip: "The sheet's thickness (mm)."),
                Field("Slot fit", _fit, tip: "Added to the sheet's thickness for the slots (mm): more is looser. 0.1 is a snug fit to glue."),
                Field("Figures", _scale, tip: "Changing it scales every size on the list to suit figures that size.")),
            Explained("Detail", "Etched and scored on the faces that show.",
                Field("Wood", _detail, tip: "Grain is scored lines; planks add etched grooves between boards."),
                Field("Variation", new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 6, Children = { _seed, shuffle.With(b => Grid.SetColumn(b, 1)) } },
                    tip: "Grain, stones, chips and cracks are laid out at random: each number gives a different layout.")),
            Explained("Material", "Picking your material's settings brings its thickness and, from your cut test, its kerf and slot fit.",
                Field("Cut with", _cutWith),
                Field("Etch with", _etchWith, tip: "Settings for the stonework, stripes, shingles and grooves (an engrave/fill recipe from your library)."),
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
        foreach (var n in new[] { _count, _w, _d, _h, _wheel, _counter, _rise, _top, _slab })
            n.ValueChanged += (_, _) => ItemChanged();
        foreach (var c in new[] { _style, _texture, _shape })
            c.SelectionChanged += (_, _) => ItemChanged();
        foreach (var c in new[] { _hoops, _open, _feet, _shafts, _stripes, _scallops, _roof, _shingles, _inscription, _weathered })
            c.IsCheckedChanged += (_, _) => ItemChanged();
        foreach (var n in new[] { _thick, _fit, _seed }) n.ValueChanged += (_, _) => SheetChanged();
        _detail.SelectionChanged += (_, _) => SheetChanged();
        _etchWith.SelectionChanged += (_, _) => SheetChanged();
        _cutWith.SelectionChanged += (_, _) => CutChosen();
        _scale.SelectionChanged += (_, _) => ScaleChosen();
        _list.SelectionChanged += (_, _) => { if (!_loading && _list.SelectedIndex >= 0 && _list.SelectedIndex != _sel) { _sel = _list.SelectedIndex; LoadItem(); Changed(); } };
        _add.Click += (_, _) => AddItem(Defaults.For(Defaults.Kinds[Math.Max(0, _addKind.SelectedIndex)].Kind));
        _copy.Click += (_, _) => AddItem(_set.Items[_sel]);
        _remove.Click += (_, _) => RemoveItem();
    }

    static decimal D(double v) => (decimal)v;
    static double V(NumericUpDown n, double fallback) => n.Value is { } v ? (double)v : fallback;
    ScatterItem Item => _set.Items[Math.Clamp(_sel, 0, _set.Items.Count - 1)];

    void FillList(int select)
    {
        bool was = _loading;
        _loading = true;
        _sel = Math.Clamp(select, 0, _set.Items.Count - 1);
        _list.ItemsSource = _set.Items.Select((it, i) => ScatterKit.Label(_set, i) + (it.Count > 1 ? $" × {it.Count}" : "")).ToList();
        _list.SelectedIndex = _sel;
        _remove.IsEnabled = _set.Items.Count > 1;
        _loading = was;
        LoadItem();
    }

    void AddItem(ScatterItem it)
    {
        // A new piece is sized for the figures chosen.
        if (!ReferenceEquals(it, _set.Items.ElementAtOrDefault(_sel)) && Math.Abs(_set.ScaleMm - 28) > 1e-9)
            it = Defaults.Scaled(it, _set.ScaleMm / 28);
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

    void ScaleChosen()
    {
        if (_loading) return;
        double to = Scales[Math.Clamp(_scale.SelectedIndex, 0, Scales.Length - 1)], k = to / Math.Max(1, _set.ScaleMm);
        if (Math.Abs(k - 1) < 1e-9) return;
        _set = _set with { ScaleMm = to, Items = [.. _set.Items.Select(it => Defaults.Scaled(it, k))] };
        FillList(_sel);
        Changed();
    }

    void LoadSheet()
    {
        _loading = true;
        _thick.Value = D(_set.ThicknessMm); _fit.Value = D(_set.FitMm); _seed.Value = Math.Clamp(_set.Seed, 1, 9999);
        _detail.SelectedIndex = (int)_set.Detail;
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
        _title.Text = info.Name;
        _about.Text = info.About;
        _count.Value = Math.Max(1, it.Count);
        _w.Value = D(it.WidthMm); _d.Value = D(it.DepthMm); _h.Value = D(it.HeightMm);
        _hoops.IsChecked = it.Hoops; _open.IsChecked = it.Open;
        _style.SelectedIndex = (int)it.Style; _feet.IsChecked = it.Feet;
        _wheel.Value = D(it.WheelMm); _shafts.IsChecked = it.Shafts;
        _counter.Value = D(it.CounterMm); _rise.Value = D(it.RiseMm); _stripes.IsChecked = it.Stripes; _scallops.IsChecked = it.Scallops;
        _top.Value = D(it.TopMm); _roof.IsChecked = it.Roof; _shingles.IsChecked = it.Shingles;
        _texture.SelectedIndex = Math.Max(0, StoneFace.Presets.ToList().FindIndex(p => p.Name == it.Texture));
        _shape.SelectedIndex = (int)it.Shape; _slab.Value = D(it.SlabMm); _inscription.IsChecked = it.Inscription; _weathered.IsChecked = it.Weathered;

        (string W, string? D, string H) labels = kind switch
        {
            ItemKind.Barrel => ("Across", null, "Height"),
            ItemKind.Fence => ("Length", null, "Height"),
            ItemKind.Cart => ("Bed width", "Bed length", "Sides"),
            ItemKind.MarketStall => ("Width", "Depth", "Front height"),
            ItemKind.Well => ("Width", "Depth", "Wall height"),
            _ => ("Width", null, "Height"),
        };
        _wLabel.Text = labels.W;
        _hLabel.Text = labels.H;
        _dLabel.Text = labels.D ?? "";
        _dRow.IsVisible = labels.D is not null;
        _barrelRows.IsVisible = kind == ItemKind.Barrel;
        _fenceRows.IsVisible = kind == ItemKind.Fence;
        _cartRows.IsVisible = kind == ItemKind.Cart;
        _stallRows.IsVisible = kind == ItemKind.MarketStall;
        _wellRows.IsVisible = kind == ItemKind.Well;
        _graveRows.IsVisible = kind == ItemKind.Gravestone;
        _loading = was;
    }

    /// <summary>The chosen piece as the settings say.</summary>
    ScatterItem FromControls()
    {
        var it = Item;
        return it with
        {
            Count = (int)V(_count, 1),
            WidthMm = V(_w, it.WidthMm), DepthMm = V(_d, it.DepthMm), HeightMm = V(_h, it.HeightMm),
            Hoops = _hoops.IsChecked == true, Open = _open.IsChecked == true,
            Style = (FenceStyle)Math.Max(0, _style.SelectedIndex), Feet = _feet.IsChecked == true,
            WheelMm = V(_wheel, it.WheelMm), Shafts = _shafts.IsChecked == true,
            CounterMm = V(_counter, it.CounterMm), RiseMm = V(_rise, it.RiseMm), Stripes = _stripes.IsChecked == true, Scallops = _scallops.IsChecked == true,
            TopMm = V(_top, it.TopMm), Roof = _roof.IsChecked == true, Shingles = _shingles.IsChecked == true,
            Texture = StoneFace.Presets[Math.Clamp(_texture.SelectedIndex, 0, StoneFace.Presets.Count - 1)].Name,
            Shape = (StoneShape)Math.Max(0, _shape.SelectedIndex), SlabMm = V(_slab, it.SlabMm), Inscription = _inscription.IsChecked == true, Weathered = _weathered.IsChecked == true,
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
        Changed();
    }

    void SheetChanged()
    {
        if (_loading) return;
        _set = _set with { ThicknessMm = V(_thick, 2), FitMm = V(_fit, 0.1), Seed = (int)V(_seed, 1), Detail = (WoodDetail)Math.Max(0, _detail.SelectedIndex) };
        Changed();
    }

    string? LastFile => _dataFolder is null ? null : Path.Combine(_dataFolder, "last.json");

    ScatterSet? LoadLast()
    {
        try { return LastFile is { } f && File.Exists(f) ? ScatterSet.FromJson(File.ReadAllText(f)) : null; }
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
        try { kit = await Task.Run(() => ScatterKit.Make(set)); }
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
            string prefix = kit.Items.Count > 1 ? ScatterKit.Label(set, i) + ": " : "";
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
            $"{pieces} piece{(pieces == 1 ? "" : "s")} to cut for " + ListWords(set.Items.Select((it, i) => (it.Count > 1 ? $"{it.Count} × " : "") + Lower(ScatterKit.Label(set, i)))) +
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
                var kit = built ?? ScatterKit.Make(set);
                if (!kit.Ok) throw new InvalidOperationException(kit.Items.SelectMany(i => i.Findings).First(f => f.Level == Severity.Bad).Text);
                var (s, sheets, id) = Placement.Place(before, kit, set, _machine, new PlaceOptions { CutSettings = cut, EtchSettings = etch, Replace = replace });
                return (kit, s, sheets, id);
            });
            if (!ReferenceEquals(_doc.State, before)) { _status.Text = "The design changed meanwhile: press again to put the pieces in."; return; }
            _doc.Commit(replace is null ? "Scatter terrain" : "Update the scatter terrain", s, Placement.ShapesOf(s, id));
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
            AppLog.Error("scatter", ex);
            _status.Text = $"Couldn't put the scenery in: {ex.Message}";
        }
        finally
        {
            _making = false;
            _make.IsEnabled = _kit?.Ok ?? true;
        }
    }
}
