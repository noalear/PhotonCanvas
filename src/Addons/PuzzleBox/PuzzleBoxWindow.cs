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

namespace PhotonCanvas.Addon.PuzzleBox;

/// <summary>
/// The puzzle box maker: the kind of box and its size, top, hidden drawer and message on the left,
/// the sheet on a second tab; the box put together in 3D with every step of opening it, and its
/// pieces flat, on the right; then every piece goes into the design, nested onto the bed. Built like
/// the game accessories maker's window.
/// </summary>
public sealed class PuzzleBoxWindow : Window
{
    readonly Document _doc;
    readonly MachineProfile _machine;
    readonly MaterialLibrary? _library;
    readonly string? _dataFolder;
    (Guid Id, BoxSpec Spec)? _editing;
    BoxSpec _spec;

    // The box
    readonly ComboBox _kind = Combo([.. Presets.Kinds.Select(k => k.Name), "Your own"]);
    readonly TextBlock _about = Hint();
    readonly NumericUpDown _w = Number(30, 1000, 1, "0.#"), _d = Number(30, 1000, 1, "0.#"), _h = Number(5, 500, 1, "0.#"), _wall = Number(3, 60, 0.5m, "0.#");
    readonly NumericUpDown _count = Number(1, 50, 1, "0");
    readonly ComboBox _top = Combo([.. Presets.TopNames]);
    readonly NumericUpDown _panels = Number(1, 8, 1, "0"), _seed = Number(1, 9999, 1, "0");
    readonly ComboBox _difficulty = Combo([.. Presets.DifficultyNames]);
    readonly CheckBox _drawer = new() { Content = "A drawer hidden in the bottom layers" };
    readonly NumericUpDown _drawerH = Number(3, 300, 1, "0.#");
    readonly CheckBox _bolt = new() { Content = "Locked until the button inside is slid" };
    readonly CheckBox _notch = new() { Content = "Finger notch underneath to pull it" };
    readonly TextBox _message = new() { Watermark = "Etched inside (leave empty for none)", AcceptsReturn = false };
    readonly ComboBox _messageAt = Combo([.. Presets.MessagePlaceNames]);
    Control _panelRows = null!, _drawerRows = null!, _messageAtRow = null!;

    // The sheet
    readonly NumericUpDown _thick = Number(0.5m, 12, 0.5m, "0.##"), _fit = Number(0.05m, 1.5m, 0.05m, "0.##");
    readonly CheckBox _marks = new() { Content = "Letter the pieces (on the faces that get glued over)" };
    readonly ComboBox _font = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    List<string> _fonts = [];
    readonly ComboBox _cutWith = Combo(), _etchWith = Combo();
    readonly TextBlock _cutNote = Hint();
    List<(Material M, Recipe R)?> _cuts = [], _etches = [];

    // The views
    readonly TabControl _views = new();
    readonly BoxView _box = new();
    readonly PiecesView _pieces = new();
    readonly Slider _step = new() { Minimum = 0, Maximum = 0, SmallChange = 1, LargeChange = 1, IsSnapToTickEnabled = true, TickFrequency = 1, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock _stepText = new() { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    readonly Button _back = UiKit.Button("◀"), _next = UiKit.Button("▶");
    readonly CheckBox _inside = new() { Content = "See inside" };
    readonly TextBlock _summary = new() { TextWrapping = TextWrapping.Wrap };
    readonly StackPanel _opening = new() { Spacing = 2 }, _assembly = new() { Spacing = 2 }, _findings = new() { Spacing = 4 };
    readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Classes = { "muted" } };
    readonly Button _make = new() { Content = "Put in the design", Classes = { "primary" }, MinWidth = 160 };
    readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(250) };
    bool _loading;
    int _generation;
    BoxKit? _kit;
    string? _kitFrom;

    public PuzzleBoxWindow(Document doc, MachineProfile machine, MaterialLibrary? library, string? dataFolder, (Guid Id, BoxSpec Spec)? editing)
    {
        _doc = doc;
        _machine = machine;
        _library = library;
        _dataFolder = dataFolder;
        _editing = editing;
        _spec = editing?.Spec ?? LoadLast() ?? Presets.Kinds[2].Spec;
        Title = "Puzzle box";
        Width = 1120;
        Height = 800;
        MinWidth = 880;
        MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");

        Library();
        Fonts();
        var tabs = new TabControl { Items = { Tab("Box", BoxTab()), Tab("Sheet", SheetTab()) } };
        var left = new Border { Width = 440, Padding = new Thickness(14, 10, 8, 0), Child = tabs };

        _views.Items.Add(new TabItem { Header = "Put together", Content = Framed(Together()) });
        _views.Items.Add(new TabItem { Header = "Pieces", Content = Framed(_pieces) });
        var results = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                _summary,
                Heading("How it opens"), _opening,
                Heading("How it goes together"), _assembly,
                _findings,
            },
        };
        var right = new Grid { RowDefinitions = new("*,Auto"), Margin = new Thickness(0, 6, 14, 0), RowSpacing = 8 };
        right.Children.Add(_views);
        var resultScroll = new ScrollViewer { Content = results, MaxHeight = 230, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
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

    static TextBlock Heading(string text) => new() { Text = text, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 4, 0, 0) };

    /// <summary>The 3D view with the steps of opening it under it.</summary>
    Control Together()
    {
        var bar = new Grid { ColumnDefinitions = new("Auto,Auto,180,*,Auto"), ColumnSpacing = 8, Margin = new Thickness(10, 6, 10, 8) };
        bar.Children.Add(_back);
        Grid.SetColumn(_next, 1);
        bar.Children.Add(_next);
        Grid.SetColumn(_step, 2);
        bar.Children.Add(_step);
        Grid.SetColumn(_stepText, 3);
        bar.Children.Add(_stepText);
        Grid.SetColumn(_inside, 4);
        bar.Children.Add(_inside);
        ToolTip.SetTip(_back, "The step before");
        ToolTip.SetTip(_next, "The next step of opening it");
        ToolTip.SetTip(_step, "Step through opening the box");
        ToolTip.SetTip(_inside, "Make the box's own layers faint, to see the drawer, the bolt and the panels' hooks inside");
        var g = new Grid { RowDefinitions = new("*,Auto"), Background = Brush("Bg2") };
        g.Children.Add(_box);
        Grid.SetRow(bar, 1);
        g.Children.Add(bar);
        return g;
    }

    // ---- building the tabs ----

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

    Control BoxTab()
    {
        var shuffle = UiKit.Button("Shuffle");
        shuffle.Click += (_, _) => _seed.Value = (_seed.Value ?? 1) % 9999 + 1;
        _panelRows = Stack(
            Field("Panels", _panels, tip: "Panels across the top, front to back. One is a plain sliding lid; with more, each is hooked to the next underneath."),
            Field("Difficulty", _difficulty, tip: "The maker tries different hooks and keeps a layout that takes fewer (Easy) or more (Hard) moves to open."),
            Field("Variation", new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 6, Children = { _seed, shuffle.With(b => Grid.SetColumn(b, 1)) } },
                tip: "Which way the hooks point and where they sit: each number gives a different puzzle."));
        _drawerRows = Stack(
            Field("Drawer height", _drawerH, tip: "The drawer's outside height (mm), in whole layers of the sheet."),
            _bolt, _notch);
        ToolTip.SetTip(_bolt, "A bolt under a button on the floor inside the box keeps the drawer shut: only someone who has opened the box and found the button can open it.");
        ToolTip.SetTip(_notch, "A notch in the bottom under the drawer's front, for a fingertip. Without it, tip the box forward to slide the drawer out.");
        _messageAtRow = Field("Where", _messageAt);
        return Stack(
            Explained("Kind of box", "",
                _kind, _about),
            Explained("Size", "Outside sizes in mm. The height is made of whole layers of the sheet.",
                Field("Width", _w, tip: "Left to right, outside (mm)."),
                Field("Depth", _d, tip: "Front to back, outside (mm)."),
                Field("Height", _h, tip: "Bottom to top, outside (mm): rounded to whole layers."),
                Field("Walls", _wall, tip: "How thick the walls are (mm): the width of each layer's ring. Sliding panels need about 10 mm or more."),
                Field("How many", _count)),
            Explained("Top", "",
                Field("Top", _top, tip: "Sliding panels that only come out in the right order, a lid that lifts off, or a closed top (a block with a hidden drawer)."),
                _panelRows),
            Explained("Hidden drawer", "Cut from the same layers as the walls, so its seams look like the layers'.",
                _drawer, _drawerRows),
            Explained("Message", "Etched inside, for whoever opens it.",
                _message, _messageAtRow));
    }

    Control SheetTab() => Stack(
        Explained("Sheet", "Every layer is one sheet: 3 mm plywood suits most boxes.",
            Field("Thickness", _thick, tip: "The sheet's thickness (mm)."),
            Field("Sliding fit", _fit, tip: "The gap round everything that slides (mm each side): more is looser. 0.25 slides freely in plywood once the kerf is allowed for."),
            _marks,
            Field("Font", _font, tip: "For the letters and the message.")),
        Explained("Material", "Picking your material's settings brings its thickness and, from your cut test, its kerf, so the sliding parts come out their drawn size.",
            Field("Cut with", _cutWith),
            Field("Etch with", _etchWith, tip: "Settings for the letters, the thumb grips and the message (an engrave/fill recipe from your library)."),
            _cutNote));

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
        if (!_fonts.Contains(_spec.Font)) _fonts.Insert(0, _spec.Font);
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
            int same = _etches.FindIndex(e => e is { } x && x.M.Id == c.M.Id);
            if (same > 0 && EtchRecipe is null) _etchWith.SelectedIndex = same;
            _loading = false;
        }
        _cutNote.Text = CutRecipe is not { } r
            ? "No kerf allowance: the laser burns each edge back by half its kerf, so sliding parts come out looser than drawn. Pick your material's cut settings (Materials & Tests → Tests → Cut test measures the kerf)."
            : r.R.Settings.KerfMm > 0 ? $"{r.M.Label}: kerf {r.R.Settings.KerfMm:0.###} mm, allowed for on the cut layer so the pieces come out their drawn size."
            : $"{r.M.Label}: no kerf measured yet, so sliding parts come out a little loose (run a cut test).";
        Changed(sheet: true);
    }

    // ---- settings in and out ----

    void Hook()
    {
        foreach (var n in new[] { _w, _d, _h, _wall, _count, _panels, _seed, _drawerH, _thick, _fit })
            n.ValueChanged += (_, _) => Changed();
        foreach (var c in new[] { _top, _difficulty, _messageAt, _font, _etchWith })
            c.SelectionChanged += (_, _) => Changed();
        foreach (var c in new[] { _drawer, _bolt, _notch, _marks })
            c.IsCheckedChanged += (_, _) => Changed();
        _message.TextChanged += (_, _) => Changed();
        _kind.SelectionChanged += (_, _) => KindChosen();
        _cutWith.SelectionChanged += (_, _) => CutChosen();
        _step.ValueChanged += (_, _) => ShowStep();
        _back.Click += (_, _) => _step.Value = Math.Max(0, _step.Value - 1);
        _next.Click += (_, _) => _step.Value = Math.Min(_step.Maximum, _step.Value + 1);
        _inside.IsCheckedChanged += (_, _) => _box.SeeInside = _inside.IsChecked == true;
    }

    static decimal D(double v) => (decimal)v;
    static double V(NumericUpDown n, double fallback) => n.Value is { } v ? (double)v : fallback;

    /// <summary>A kind picked: its top, drawer and height; the size and the sheet stay.</summary>
    void KindChosen()
    {
        if (_loading || _kind.SelectedIndex < 0 || _kind.SelectedIndex >= Presets.Kinds.Count) { Describe(); return; }
        var p = Presets.Kinds[_kind.SelectedIndex].Spec;
        _spec = _spec with
        {
            Top = p.Top, Panels = p.Panels, Drawer = p.Drawer, Bolt = p.Bolt, HeightMm = p.HeightMm, DrawerHeightMm = p.DrawerHeightMm,
            MessageAt = p.Drawer ? MessagePlace.Drawer : MessagePlace.Floor,
        };
        Load();
        Changed();
    }

    void Describe()
    {
        int k = Presets.Match(_spec);
        _about.Text = k >= 0 ? Presets.Kinds[k].About : "Your own mix of top and drawer.";
    }

    void Load()
    {
        _loading = true;
        var s = _spec;
        _kind.SelectedIndex = Presets.Match(s) is var k and >= 0 ? k : Presets.Kinds.Count;
        _w.Value = D(s.WidthMm); _d.Value = D(s.DepthMm); _h.Value = D(s.HeightMm); _wall.Value = D(Math.Clamp(s.WallMm, 3, 60)); _count.Value = Math.Clamp(s.Count, 1, 50);
        _top.SelectedIndex = (int)s.Top;
        _panels.Value = Math.Clamp(s.Panels, 1, 8); _difficulty.SelectedIndex = (int)s.Difficulty; _seed.Value = Math.Clamp(s.Seed, 1, 9999);
        _drawer.IsChecked = s.Drawer; _drawerH.Value = D(Math.Clamp(s.DrawerHeightMm, 3, 300)); _bolt.IsChecked = s.Bolt; _notch.IsChecked = s.FingerNotch;
        if (_message.Text != s.Message) _message.Text = s.Message;
        _messageAt.SelectedIndex = (int)s.MessageAt;
        _thick.Value = D(Math.Clamp(s.ThicknessMm, 0.5, 12)); _fit.Value = D(Math.Clamp(s.FitMm, 0.05, 1.5));
        _marks.IsChecked = s.MarkPieces;
        _font.SelectedIndex = Math.Max(0, _fonts.IndexOf(s.Font));
        _loading = false;
        Shown();
        Describe();
        CutChosen();
    }

    /// <summary>Only the settings that count for this box.</summary>
    void Shown()
    {
        _panelRows.IsVisible = _spec.Top == TopKind.SlidingPanels;
        _difficulty.IsEnabled = _seed.IsEnabled = _spec.Panels > 1;
        _drawerRows.IsVisible = _spec.Drawer;
        _bolt.IsEnabled = _spec.Top != TopKind.Closed;
        _messageAtRow.IsVisible = _spec.Drawer && _spec.Top != TopKind.Closed;
    }

    BoxSpec FromControls() => _spec with
    {
        WidthMm = V(_w, _spec.WidthMm), DepthMm = V(_d, _spec.DepthMm), HeightMm = V(_h, _spec.HeightMm), WallMm = V(_wall, _spec.WallMm),
        Count = (int)V(_count, 1),
        Top = (TopKind)Math.Max(0, _top.SelectedIndex), Panels = (int)V(_panels, _spec.Panels), Difficulty = (Difficulty)Math.Max(0, _difficulty.SelectedIndex), Seed = (int)V(_seed, 1),
        Drawer = _drawer.IsChecked == true, DrawerHeightMm = V(_drawerH, _spec.DrawerHeightMm), Bolt = _bolt.IsChecked == true, FingerNotch = _notch.IsChecked == true,
        Message = _message.Text ?? "", MessageAt = _spec.Drawer && _spec.Top == TopKind.Closed ? MessagePlace.Drawer : (MessagePlace)Math.Max(0, _messageAt.SelectedIndex),
        ThicknessMm = V(_thick, 3), FitMm = V(_fit, 0.25), MarkPieces = _marks.IsChecked == true, Font = _font.SelectedItem as string ?? _spec.Font,
    };

    void Changed(bool sheet = false)
    {
        if (_loading) return;
        _spec = FromControls();
        if (!_spec.Drawer && _spec.MessageAt == MessagePlace.Drawer) _spec = _spec with { MessageAt = MessagePlace.Floor };
        if (_spec.Top == TopKind.Closed && _spec.Drawer) _spec = _spec with { MessageAt = MessagePlace.Drawer };
        _loading = true;
        _kind.SelectedIndex = Presets.Match(_spec) is var k and >= 0 ? k : Presets.Kinds.Count;
        _loading = false;
        Shown();
        Describe();
        _debounce.Stop();
        _debounce.Start();
    }

    string? LastFile => _dataFolder is null ? null : Path.Combine(_dataFolder, "last.json");

    BoxSpec? LoadLast()
    {
        try { return LastFile is { } f && File.Exists(f) ? BoxSpec.FromJson(File.ReadAllText(f)) : null; }
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

    async Task Rebuild()
    {
        int gen = ++_generation;
        var spec = _spec;
        BoxKit kit;
        try { kit = await Task.Run(() => Kit.Make(BoxMaker.Make(spec))); }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or ArithmeticException)
        {
            if (gen == _generation) { _pieces.Show(null, e.Message); _box.Show(null, e.Message); }
            return;
        }
        if (gen != _generation) return;
        _kit = kit;
        _kitFrom = spec.ToJson();
        var d = kit.Design;
        var all = d.Findings.Concat(d.Ok ? Placement.TooBig(kit, _machine) : []).ToList();
        string? bad = all.FirstOrDefault(f => f.Level == Severity.Bad)?.Text;
        _pieces.Show(bad is null ? kit : null, bad ?? "Nothing to show.");
        _box.Show(bad is null ? d : null, bad ?? "Nothing to show.");

        // The steps of opening it.
        _opening.Children.Clear();
        var moves = d.Solution?.Moves ?? [];
        for (int i = 0; i < moves.Count; i++)
            _opening.Children.Add(new TextBlock { Text = $"{i + 1}. {Solver.Words(d, moves[i])}", TextWrapping = TextWrapping.Wrap });
        if (moves.Count == 0) _opening.Children.Add(new TextBlock { Text = "—", Classes = { "muted" } });
        _assembly.Children.Clear();
        if (d.Ok)
            foreach (var a in Kit.Assembly(d)) _assembly.Children.Add(new TextBlock { Text = "• " + a, TextWrapping = TextWrapping.Wrap });
        _loading = true;
        _step.Maximum = moves.Count;
        _step.Value = 0;
        _loading = false;
        ShowStep();

        _findings.Children.Clear();
        foreach (var f in all.OrderByDescending(f => f.Level)) _findings.Children.Add(FindingRow(f));
        int pieces = kit.PieceCount;
        _summary.Text = pieces == 0 ? "" : $"{pieces} piece{(pieces == 1 ? "" : "s")} to cut" + (spec.Count > 1 ? $" for {spec.Count} boxes" : "") + $", {d.Layers} layers high ({d.HeightMm:0.#} mm).";
        _make.IsEnabled = kit.Ok && pieces > 0 && bad is null;
    }

    void ShowStep()
    {
        var d = _kit?.Design;
        var sol = d?.Solution;
        if (d is null || sol is null || !d.Ok)
        {
            _stepText.Text = "";
            _back.IsEnabled = _next.IsEnabled = false;
            _box.Step(null, -1);
            return;
        }
        int i = (int)Math.Round(_step.Value);
        i = Math.Clamp(i, 0, sol.Moves.Count);
        _box.Step(sol.Poses[i], i > 0 ? sol.Moves[i - 1].Body : -1);
        _stepText.Text = i == 0 ? $"Shut. {sol.Moves.Count} move{(sol.Moves.Count == 1 ? "" : "s")} to open it: press ▶." : $"{i} of {sol.Moves.Count}: {Solver.Words(d, sol.Moves[i - 1])}";
        _back.IsEnabled = i > 0;
        _next.IsEnabled = i < sol.Moves.Count;
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
            Guid? replace = _editing is { } ed && Placement.CountEverywhere(_doc.State, ed.Id) > 0 ? ed.Id : null;
            var before = _doc.State;
            var (kit, s, sheets, id) = await Task.Run(() =>
            {
                var kit = built ?? Kit.Make(BoxMaker.Make(spec));
                if (!kit.Ok) throw new InvalidOperationException(kit.Design.Findings.FirstOrDefault(f => f.Level == Severity.Bad)?.Text ?? "Nothing to cut.");
                var (s, sheets, id) = Placement.Place(before, kit, _machine, new PlaceOptions { CutSettings = cut, EtchSettings = etch, Replace = replace });
                return (kit, s, sheets, id);
            });
            if (!ReferenceEquals(_doc.State, before)) { _status.Text = "The design changed meanwhile: press again to put the pieces in."; return; }
            _doc.Commit(replace is null ? "Puzzle box" : "Update the puzzle box", s, Placement.ShapesOf(s, id));
            _editing = (id, spec);
            _make.Content = "Update in the design";
            int pieces = kit.PieceCount;
            _status.Text = $"{(replace is null ? "Put" : "Updated")} {pieces} piece{(pieces == 1 ? "" : "s")} in the design" +
                           (sheets > 1 ? $", over {sheets} pages (a bed each)" : "") +
                           $": outlines on \"{Placement.CutLayer}\", the letters and etching on \"{Placement.EtchLayer}\" (done first).";
        }
        catch (InvalidOperationException ex) { _status.Text = ex.Message; }
        catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException or ArithmeticException or NullReferenceException)
        {
            AppLog.Error("puzzlebox", ex);
            _status.Text = $"Couldn't put the box in: {ex.Message}";
        }
        finally
        {
            _making = false;
            _make.IsEnabled = _kit?.Ok ?? true;
        }
    }
}
