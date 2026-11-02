using System.Collections.Immutable;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using PhotonCanvas.Addon.Mechanism;
using PhotonCanvas.App;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Materials;
using static PhotonCanvas.App.UiKit;
using Mech = PhotonCanvas.Addon.Mechanism.Mechanism;

namespace PhotonCanvas.Addon.Automata;

/// <summary>
/// The toys and automata maker: a crank box whose cut-out figures bob, nod, peck and spin, a punched strip
/// for a paper-strip music box, or a spring steel comb to play it. Set it up, watch it move (or play) while the check runs, and put the parts in
/// the design on a cut layer with the material's own kerf.
/// </summary>
public sealed class AutomataWindow : Window
{
    static readonly string[] Kinds = ["Moving figures", "Music box strip", "Music box comb"];
    static readonly string[] KindWords =
    [
        "A box with a handle and cut-out figures on top: each one bobs on a rod, nods or pecks on a lever, or spins on a turntable as you turn it. Checked moving, all the way round, before you cut it.",
        "A punched strip for a paper-strip music box: type the tune or open a MIDI file, and each note becomes a hole in its lane. Checked for notes the box hasn't got and holes too close to play.",
        "A music box comb to cut from thin spring steel: a tine for each lane of the strips, each as long as its note needs (worked out from the metal and its thickness), with stepped clamp bars to hold them.",
    ];
    static readonly string[] MoveNames = ["Bobs up and down", "Nods or pecks on a lever", "Spins on a turntable"];

    readonly Document _doc;
    readonly MachineProfile _machine;
    readonly MaterialLibrary? _library;
    readonly string? _dataFolder;
    (Guid Id, ToySpec Spec)? _editing;
    readonly ToySpec[] _specs = [new FigureBoxSpec(), new StripSpec(), new CombSpec()];

    readonly TabControl _kind = new() { Margin = new Thickness(14, 8, 14, 0), Padding = new Thickness(0) };
    readonly TextBlock _kindWords = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 0, 2, 0) };
    readonly Border _settings = new();
    readonly ComboBox _cutWith = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    List<(Material M, Recipe R)?> _cuts = [];
    readonly NumericUpDown _thick = Number(0.5m, 25, 0.1m, "0.##");
    readonly NumericUpDown _fit = Number(-0.5m, 1, 0.05m, "+0.##;-0.##;0");
    readonly TextBlock _cutNote = Hint();
    Control _thickRow = null!, _fitRow = null!;
    readonly CheckBox _marks = new() { Content = "Engrave the parts' names and marks", IsChecked = true };

    readonly MotionView _view = new() { MinHeight = 320 };
    readonly Button _play = new() { Content = "Pause", MinWidth = 80 };
    readonly Slider _speed = new() { Minimum = 1, Maximum = 60, Value = 8, Width = 160, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock _speedText = new() { VerticalAlignment = VerticalAlignment.Center, MinWidth = 110 };
    readonly TextBlock _summary = new() { TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.SemiBold };
    readonly StackPanel _findings = new() { Spacing = 4 };
    readonly TextBlock _shopping = new() { TextWrapping = TextWrapping.Wrap, Classes = { "hint" } };
    readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    readonly Button _make = new() { Content = "Put in the design", Classes = { "primary" }, MinWidth = 170, IsEnabled = false };

    readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(250) };
    CancellationTokenSource? _cts;
    Mech? _built;
    ToySpec? _builtFrom;
    int _shownKind = -1;

    public AutomataWindow(Document doc, MachineProfile machine, MaterialLibrary? library = null, string? dataFolder = null, (Guid Id, ToySpec Spec)? editing = null)
    {
        _doc = doc;
        _machine = machine;
        _library = library;
        _dataFolder = dataFolder;
        Title = "Toys and automata";
        Width = 1240;
        Height = 820;
        MinWidth = 980;
        MinHeight = 620;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");

        foreach (var k in Kinds) _kind.Items.Add(new TabItem { Header = k });
        _kind.SelectedIndex = 0;
        _thick.Value = 3;
        _fit.Value = 0.1m;
        Load();
        Library();
        if (editing is { } e)
        {
            _editing = e;
            int k = KindOf(e.Spec);
            _specs[k] = e.Spec;
            _kind.SelectedIndex = k;
            if (k == 0)
            {
                _thick.Value = (decimal)e.Spec.ThicknessMm;
                _fit.Value = (decimal)e.Spec.SlotFitMm;
            }
            _make.Content = "Update in the design";
        }

        _debounce.Tick += (_, _) => { _debounce.Stop(); _ = Rebuild(); };
        _kind.SelectionChanged += (_, _) => { ShowKind(); Changed(); };
        _thick.ValueChanged += (_, _) => Changed();
        _fit.ValueChanged += (_, _) => Changed();
        _cutWith.SelectionChanged += (_, _) => CutChosen();
        _play.Click += (_, _) => _view.Playing = !_view.Playing;
        _view.PlayingChanged += () => _play.Content = _view.Playing ? "Pause" : "Play";
        _speed.PropertyChanged += (_, ev) => { if (ev.Property == RangeBase.ValueProperty) Speed(); };
        _make.Click += (_, _) => Make();
        Speed();

        _thickRow = Field("Thickness", _thick, tip: "The sheet's thickness (mm): slots, spacers and dowel lengths follow it.");
        _fitRow = Field("Slot fit", _fit, Hint("Slots for the box's tabs are the sheet's thickness plus this."), "From the cut test: + is looser, − is tighter.");
        var material = Explained("Material", "What it's cut from. Picking your material's cut settings brings its thickness and, from your cut test, its kerf and slot fit.",
            Field("Cut with", _cutWith), _thickRow, _fitRow, _cutNote, _marks);
        ToolTip.SetTip(_marks, "Names, numbers and arrows, engraved lightly on a Score layer before the parts are cut out.");
        var left = new StackPanel { Spacing = 12, Margin = new Thickness(14), Children = { _kindWords, _settings, material } };
        var leftScroll = new ScrollViewer { Content = left, Width = 430, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };

        var controls = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Children = { _play, Muted("Speed").With(m => m.VerticalAlignment = VerticalAlignment.Center), _speed, _speedText, Hint("Drag a cam round to turn it by hand, drag sideways elsewhere, or click the strip.").With(h => h.VerticalAlignment = VerticalAlignment.Center) },
        };
        var results = new StackPanel { Spacing = 8, Children = { _summary, _findings, _shopping } };
        var right = new Grid { RowDefinitions = new("*,Auto,Auto"), Margin = new Thickness(0, 14, 14, 0), RowSpacing = 8 };
        right.Children.Add(new Border { CornerRadius = new(8), ClipToBounds = true, BorderBrush = Brush("Line"), BorderThickness = new(1), Child = _view });
        Grid.SetRow(controls, 1);
        right.Children.Add(controls);
        var resultScroll = new ScrollViewer { Content = results, MaxHeight = 230, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
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

        var body = new Grid { ColumnDefinitions = new("Auto,*"), RowDefinitions = new("Auto,*,Auto") };
        Grid.SetColumnSpan(_kind, 2);
        body.Children.Add(_kind);
        Grid.SetRow(leftScroll, 1);
        body.Children.Add(leftScroll);
        Grid.SetColumn(right, 1);
        Grid.SetRow(right, 1);
        body.Children.Add(right);
        Grid.SetRow(bottom, 2);
        Grid.SetColumnSpan(bottom, 2);
        body.Children.Add(bottom);
        Content = body;

        ShowKind();
        Changed();
        Closed += (_, _) =>
        {
            _cts?.Cancel();
            _debounce.Stop();
            _view.Playing = false;
            Save();
        };
    }

    static int KindOf(ToySpec s) => s switch { StripSpec => 1, CombSpec => 2, _ => 0 };

    int Kind => Math.Clamp(_kind.SelectedIndex, 0, Kinds.Length - 1);

    FigureBoxSpec Box { get => (FigureBoxSpec)_specs[0]; set => _specs[0] = value; }
    StripSpec Strip { get => (StripSpec)_specs[1]; set => _specs[1] = value; }
    CombSpec Comb { get => (CombSpec)_specs[2]; set => _specs[2] = value; }

    /// <summary>The settings as they are now, made of the sheet chosen (a strip's pieces as long as the bed allows).</summary>
    ToySpec Current()
    {
        var mat = Cut()?.M.Category ?? "";
        if (_specs[Kind] is StripSpec st) return st with { Material = mat, MaxPieceMm = Math.Max(_machine.FieldW, _machine.FieldH) - 10 };
        if (_specs[Kind] is CombSpec cs) return cs with { Material = mat };
        return _specs[Kind] with { ThicknessMm = (double)(_thick.Value ?? 3), SlotFitMm = (double)(_fit.Value ?? 0.1m), Material = mat };
    }

    void Speed()
    {
        _view.TurnsPerMinute = Math.Round(_speed.Value);
        _speedText.Text = $"{_view.TurnsPerMinute:0} turns a minute";
    }

    void Changed()
    {
        _debounce.Stop();
        _debounce.Start();
    }

    async Task Rebuild()
    {
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        var spec = Current();
        _make.IsEnabled = false;
        try
        {
            var machine = _machine;
            var m = await Task.Run(() =>
            {
                var m = spec.Build(cts.Token);
                m.Findings.AddRange(ToyPlacement.TooBig(m, machine));
                return m;
            }, cts.Token);
            if (cts.IsCancellationRequested) return;
            _built = m;
            _builtFrom = spec;
            _view.Show(m);
            ShowResults(m, null);
            _make.IsEnabled = true;
        }
        catch (OperationCanceledException) { }
        catch (InvalidOperationException e)
        {
            if (cts.IsCancellationRequested) return;
            _built = null;
            _view.Show(null);
            ShowResults(null, e.Message);
        }
        catch (Exception e) when (e is ArgumentException or IndexOutOfRangeException or ArithmeticException or NullReferenceException)
        {
            if (cts.IsCancellationRequested) return;
            AppLog.Error("automata", e);
            _built = null;
            _view.Show(null);
            ShowResults(null, $"Couldn't make that: {e.Message}");
        }
    }

    void ShowResults(Mech? m, string? error)
    {
        _findings.Children.Clear();
        if (m is null)
        {
            _summary.Text = "";
            _findings.Children.Add(FindingRow(new Finding(Severity.Bad, error ?? "Nothing to show.")));
            _shopping.Text = "";
            return;
        }
        int bad = m.Findings.Count(f => f.Level == Severity.Bad), warn = m.Findings.Count(f => f.Level == Severity.Warn);
        _summary.Text = m.Summary + (bad > 0 ? $" {bad} problem{(bad == 1 ? "" : "s")} to fix (red)." : warn > 0 ? $" {warn} thing{(warn == 1 ? "" : "s")} to watch (orange)." : " The check found nothing wrong.");
        foreach (var f in m.Findings.OrderByDescending(f => f.Level)) _findings.Children.Add(FindingRow(f));
        int parts = m.Parts.Sum(p => p.Copies);
        _shopping.Text = $"{parts} part{(parts == 1 ? "" : "s")} to cut: {Say.And(m.Parts.Select(p => p.Copies > 1 ? $"{p.Copies} × {Say.Lower(p.Name)}" : Say.Lower(p.Name)))}." +
                         (m.Shopping.Count > 0 ? "\nAlso needed: " + string.Join("; ", m.Shopping.Select(Say.Lower)) + "." : "");
    }

    static Control FindingRow(Finding f)
    {
        var (mark, brush) = f.Level switch
        {
            Severity.Good => ("✓", Brush("Ok")),
            Severity.Note => ("i", Brush("TextMuted")),
            Severity.Warn => ("!", Brush("Warn")),
            _ => ("✕", Brush("Danger")),
        };
        var g = new Grid { ColumnDefinitions = new("20,*") };
        g.Children.Add(new TextBlock { Text = mark, Foreground = brush, FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center });
        var t = new TextBlock { Text = f.Text, TextWrapping = TextWrapping.Wrap };
        Grid.SetColumn(t, 1);
        g.Children.Add(t);
        return g;
    }

    // ---- the material ----

    void Library()
    {
        var mats = _library?.Materials ?? [];
        _cuts = [null, .. mats.SelectMany(m => m.Recipes.Where(r => r.Settings.Mode == LayerMode.Cut).Select(r => ((Material, Recipe)?)(m, r)))];
        _cutWith.ItemsSource = _cuts.Select(c => c is { } x
            ? $"{x.M.Label} · {x.R.Title}" + (x.R.Settings.KerfMm > 0 ? $" (kerf {x.R.Settings.KerfMm:0.###} mm)" : "")
            : "As set on the cut layer").ToList();
        int pick = _savedRecipe is { } id ? _cuts.FindIndex(c => c is { } x && x.R.Id == id) : -1;
        _cutWith.SelectedIndex = Math.Max(0, pick);
        Note();
    }

    (Material M, Recipe R)? Cut() => _cutWith.SelectedIndex > 0 && _cutWith.SelectedIndex < _cuts.Count ? _cuts[_cutWith.SelectedIndex] : null;

    void CutChosen()
    {
        if (Cut() is { } c && Kind == 0)
        {
            if (c.M.ThicknessMm is { } t) _thick.Value = (decimal)t;
            if (c.R.SlotFitMm is { } fit) _fit.Value = (decimal)fit;
        }
        // Only a thin sheet's thickness: the clamp plate's (or a wooden sheet's) isn't the comb's.
        if (Cut() is { M.ThicknessMm: { } ct } && Kind == 2 && _combThick is not null && ct is >= 0.1 and <= 1.2)
            _combThick.Value = (decimal)ct;
        Note();
        Changed();
    }

    void Note()
    {
        if (Cut() is not { } c)
        {
            _cutNote.Text = Kind == 1
                ? "Pick your card's cut settings so the holes come out their drawn size (Laser → Tests → Cut test measures the kerf)."
                : Kind == 2
                ? "Pick your spring steel's cut settings so the tines come out their drawn width (Laser → Tests → Cut test measures the kerf). The clamp bars go on a page of their own, on a layer for the thicker plate."
                : "No kerf allowance: the laser burns each edge back by half its kerf, so slots come out looser and holes bigger. Pick your material's cut settings (Laser → Tests → Cut test measures the kerf) for parts at their drawn size.";
            return;
        }
        var notes = new List<string>();
        if (c.M.ThicknessMm is { } t) notes.Add($"{t:0.##} mm thick");
        notes.Add(c.R.Settings.KerfMm > 0 ? $"kerf {c.R.Settings.KerfMm:0.###} mm, allowed for on the cut layer so the parts come out their drawn size" : "no kerf measured yet (run a cut test)");
        if (c.R.SlotFitMm is { } fit && Kind == 0) notes.Add($"slot fit {fit:+0.##;-0.##;0} mm from your cut test");
        _cutNote.Text = $"{c.M.Label}: {string.Join(", ", notes)}.";
    }

    // ---- putting it in ----

    bool _making;

    async void Make()
    {
        if (_making) return;
        _making = true;
        _make.IsEnabled = false;
        _status.Text = "Putting the parts in the design…";
        try
        {
            var spec = Current();
            var built = _built is not null && _builtFrom?.ToJson() == spec.ToJson() ? _built : null;
            var cut = Cut();
            Func<Layer, Layer>? settings = cut is { } c ? l => c.R.Settings.ApplyTo(l) with { Material = new MaterialRef(c.M.Id, c.M.Label, c.R.Id, c.R.Title) } : null;
            var to = ToyPlacement.Target;
            Guid? replace = _editing is { } e && Placement.Exists(_doc.State, e.Id, to) ? e.Id : null;
            bool marks = _marks.IsChecked == true;
            var before = _doc.State;
            var (m, s, sheets, id) = await Task.Run(() =>
            {
                var m = built ?? spec.Build();
                var (s, sheets, id) = Placement.Place(before, m, spec.ToJson(), _machine, new PlaceOptions { CutSettings = settings, Marks = marks, Replace = replace }, to);
                return (m, s, sheets, id);
            });
            if (!ReferenceEquals(_doc.State, before)) { _status.Text = "The design changed meanwhile: press again to put the parts in."; return; }
            _doc.Commit(replace is null ? $"Toy: {m.Title}" : $"Update the {m.Title.ToLowerInvariant()}", s, Placement.ShapesOf(s, id, to));
            _editing = (id, spec);
            _make.Content = "Update in the design";
            int parts = m.Parts.Sum(p => p.Copies);
            _status.Text = $"{(replace is null ? "Put" : "Updated")} {parts} part{(parts == 1 ? "" : "s")} in the design on \"{to.CutLayer}\"" +
                           (m.Parts.FirstOrDefault(x => x.Stock is not null) is { } other
                               ? $", and the {Say.Lower(other.Name)}s on the next page, on \"{to.CutLayer} ({other.Stock})\" (cut them from that plate with its own settings)"
                               : sheets > 1 ? $", over {sheets} pages (a bed each)" : "") +
                           (cut is { } cc ? $", cut with {cc.M.Label} · {cc.R.Title}" : "") +
                           (marks ? $"; names and marks on \"{to.MarkLayer}\"" : "") + "." +
                           (m.Worst == Severity.Bad ? " ⚠ The check found problems (in red): it may not work as it is." : "");
        }
        catch (InvalidOperationException ex) { _status.Text = ex.Message; }
        catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException or ArithmeticException or NullReferenceException)
        {
            AppLog.Error("automata", ex);
            _status.Text = $"Couldn't put it in: {ex.Message}";
        }
        finally
        {
            _making = false;
            _make.IsEnabled = _built is not null;
        }
    }

    // ---- each kind's settings ----

    void ShowKind()
    {
        int k = Kind;
        _kindWords.Text = KindWords[k];
        _thickRow.IsVisible = _fitRow.IsVisible = k == 0;
        Note();
        if (_shownKind == k) return;
        _shownKind = k;
        _settings.Child = k switch { 0 => FiguresPanel(), 1 => StripPanel(), _ => CombPanel() };
    }

    NumericUpDown Num(decimal min, decimal max, decimal step, string format, double value, Action<double> set, bool narrow = false)
    {
        var n = Number(min, max, step, format);
        if (narrow) n.ShowButtonSpinner = false;
        n.Value = Math.Clamp((decimal)value, min, max);
        n.ValueChanged += (_, _) =>
        {
            if (n.Value is not { } v) return;
            set((double)v);
            Changed();
        };
        return n;
    }

    CheckBox Check(string text, bool value, Action<bool> set, string? tip = null)
    {
        var c = new CheckBox { Content = text, IsChecked = value };
        if (tip is not null) ToolTip.SetTip(c, tip);
        c.IsCheckedChanged += (_, _) => { set(c.IsChecked == true); Changed(); };
        return c;
    }

    ComboBox Pick(string[] items, int index, Action<int> set)
    {
        var c = new ComboBox { ItemsSource = items, SelectedIndex = Math.Clamp(index, 0, items.Length - 1), HorizontalAlignment = HorizontalAlignment.Stretch };
        c.SelectionChanged += (_, _) =>
        {
            if (c.SelectedIndex < 0) return;
            set(c.SelectedIndex);
            Changed();
        };
        return c;
    }

    static StackPanel Stack(params Control[] items)
    {
        var s = new StackPanel { Spacing = 8 };
        foreach (var c in items) s.Children.Add(c);
        return s;
    }

    // ---- moving figures ----

    readonly HashSet<int> _open = [];

    Control FiguresPanel()
    {
        var host = new StackPanel { Spacing = 8 };
        var preset = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = FigureBoxSpec.All.Select(p => p.Name).Append("Your own").ToList() };
        bool filling = false;
        var add = Button("Add a figure", tip: "Another figure along the box, with its own cam or turntable.");
        void Preset()
        {
            filling = true;
            var found = FigureBoxSpec.Find(Box.Stations);
            preset.SelectedIndex = found is null ? FigureBoxSpec.All.Length : FigureBoxSpec.All.IndexOf(found);
            filling = false;
        }
        void Fill()
        {
            host.Children.Clear();
            var stations = Box.Stations;
            for (int i = 0; i < stations.Count; i++)
            {
                int idx = i;
                var st = stations[i];
                FigureStation Get() => Box.Stations[idx];
                void Set(FigureStation v) { Box = Box with { Stations = Box.Stations.SetItem(idx, v) }; Preset(); }
                var ex = new Expander
                {
                    Header = $"Figure {i + 1}: {(Figures.Find(st.Figure) is null ? "a rod" : Figures.Called(st.Figure))} that {st.MoveWords}",
                    IsExpanded = _open.Contains(i) || (i == 0 && _open.Count == 0),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Content = StationFields(idx, Get, Set, () => { Fill(); Changed(); }),
                };
                ex.PropertyChanged += (_, e) =>
                {
                    if (e.Property != Expander.IsExpandedProperty) return;
                    if (ex.IsExpanded) _open.Add(idx); else _open.Remove(idx);
                };
                host.Children.Add(ex);
            }
            add.IsEnabled = stations.Count < FigureBoxSpec.MaxStations;
            Preset();
        }
        preset.SelectionChanged += (_, _) =>
        {
            if (filling || preset.SelectedIndex < 0 || preset.SelectedIndex >= FigureBoxSpec.All.Length) return;
            Box = Box with { Stations = FigureBoxSpec.All[preset.SelectedIndex].Stations };
            _open.Clear();
            Fill();
            Changed();
        };
        add.Click += (_, _) =>
        {
            int n = Box.Stations.Count;
            Box = Box with { Stations = Box.Stations.Add(new FigureStation { Figure = "star", StartDeg = n * 90 % 360 }) };
            _open.Clear();
            _open.Add(n);
            Fill();
            Changed();
        };
        Fill();
        var s = Box;
        return Stack(
            Explained("Figures", "Each figure has its own space along the box, so they never meet.", Field("Start from", preset), host, add),
            Explained("The box", "Tabs in slots for the sheet's thickness; rods slide in slots in the top and a shelf under it.",
                Field("Crank", Num(10, 120, 1, "0", s.CrankMm, v => Box = Box with { CrankMm = v }), Hint("From the shaft to the handle (mm).")),
                Field("Handle turns", Pick(["Anticlockwise", "Clockwise"], s.Clockwise ? 1 : 0, v => Box = Box with { Clockwise = v == 1 }), tip: "Looking at the handle's side of the box."),
                Field("Shaft", Num(2, 25, 0.5m, "0.#", s.Axle.SizeMm, v => Box = Box with { Axle = Box.Axle with { SizeMm = v } }), Hint("The dowel's width (mm).")),
                Check("Square shaft: sets every cam's timing by itself", s.Axle.Square, b => Box = Box with { Axle = Box.Axle with { Square = b } }),
                Field("Pivot pins", Num(1, 10, 0.5m, "0.#", s.PinMm, v => Box = Box with { PinMm = v }), Hint("The dowel the levers tip on (mm): a bamboo skewer is about 3.")),
                Field("Spindles", Num(2, 12, 0.5m, "0.#", s.SpindleMm, v => Box = Box with { SpindleMm = v }), Hint("The round dowel each turntable turns on (mm).")),
                Field("Band", Num(1, 6, 0.5m, "0.#", s.BandMm, v => Box = Box with { BandMm = v }), Hint("How thick the rubber band or O-ring round a drive wheel is (mm)."))));
    }

    /// <summary>One figure's settings: which figure, how it moves, and its cam, lever or turntable.</summary>
    Control StationFields(int idx, Func<FigureStation> get, Action<FigureStation> set, Action refill)
    {
        var st = get();
        var keys = Figures.Kinds.Select(k => k.Key).Append("").ToArray();
        var names = Figures.Kinds.Select(k => k.Name).Append("None (put your own on)").ToArray();
        int fi = Array.IndexOf(keys, st.Figure);
        var items = new List<Control>
        {
            Field("Moves", Pick(MoveNames, (int)st.Move, v => { set(get() with { Move = (FigureMove)v }); Dispatcher.UIThread.Post(refill); })),
            Field("Figure", Pick(names, fi < 0 ? keys.Length - 1 : fi, v => { set(get() with { Figure = keys[v] }); Dispatcher.UIThread.Post(refill); })),
            Field("Height", Num(10, 150, 1, "0", st.FigureMm, v => set(get() with { FigureMm = v })), Hint("How tall the figure is (mm).")),
            Check("Face the other way", st.Flip, b => set(get() with { Flip = b })),
        };
        if (st.Move == FigureMove.Nod)
        {
            items.Add(Field("Pivot", Num(14, 80, 1, "0", st.PivotMm, v => set(get() with { PivotMm = v })), Hint("From the rod that pushes the lever to its pivot (mm): further tips it more gently.")));
            items.Add(Field("Arm", Num(0, 150, 1, "0", st.ArmMm, v => set(get() with { ArmMm = v })), Hint("From the pivot to where the figure stands (mm).")));
            items.Add(Check("Figure behind the pivot: it dips as the rod rises (a peck, a hammer)", st.Dips, b => set(get() with { Dips = b })));
        }
        if (st.Move == FigureMove.Spin)
        {
            items.Add(Field("Wheel", Num(8, 60, 1, "0", st.WheelMm, v => set(get() with { WheelMm = v })), Hint("The drive wheel's radius with its band on (mm).")));
            items.Add(Field("Offset", Num(4, 60, 1, "0", st.OffsetMm, v => set(get() with { OffsetMm = v })), Hint("From the turntable's middle to where the wheel touches it (mm): the figure turns wheel ÷ offset times a turn.")));
        }
        else
        {
            items.Add(Explained("Its cam", "What the rod does over one turn of the handle.", StepsEditor(() => get().Steps, v => set(get() with { Steps = v }))));
            items.Add(Field("Base radius", Num(10, 80, 1, "0", st.BaseRadiusMm, v => set(get() with { BaseRadiusMm = v })), Hint("The cam's radius where the rod is lowest (mm). Bigger pushes more gently.")));
            items.Add(Field("Foot", Pick(["Round: follows every dip", "Flat: can't get into dips"], (int)st.Foot, v => set(get() with { Foot = (FollowerFoot)v }))));
            items.Add(Field("Foot size", Num(4, 40, 1, "0", st.FootMm, v => set(get() with { FootMm = v })), Hint("A round foot's diameter, or a flat foot's width (mm).")));
            items.Add(Field("Starts", Num(0, 359, 15, "0", st.StartDeg, v => set(get() with { StartDeg = v })), Hint("How far into its steps this cam is when the handle starts (degrees): puts the figures out of step.")));
        }
        var remove = Button("Take this figure away");
        remove.IsEnabled = Box.Stations.Count > 1;
        remove.Click += (_, _) =>
        {
            Box = Box with { Stations = Box.Stations.RemoveAt(idx) };
            _open.Clear();
            refill();
        };
        items.Add(remove);
        return Stack([.. items]);
    }

    /// <summary>A cam's steps, one row each, with a choice of ready-made motions.</summary>
    Control StepsEditor(Func<ImmutableList<CamStep>> get, Action<ImmutableList<CamStep>> set)
    {
        var preset = new ComboBox { ItemsSource = CamPresets.All.Select(p => p.Name).Append("Your own steps").ToList(), HorizontalAlignment = HorizontalAlignment.Stretch };
        var rows = new StackPanel { Spacing = 4 };
        var add = Button("Add a step");
        bool filling = false;
        void Which()
        {
            filling = true;
            var found = CamPresets.Find(get());
            preset.SelectedIndex = found is null ? CamPresets.All.Length : CamPresets.All.IndexOf(found);
            filling = false;
        }
        void Update(int idx, Func<CamStep, CamStep> f)
        {
            set(get().SetItem(idx, f(get()[idx])));
            Which();
        }
        void Fill()
        {
            rows.Children.Clear();
            var head = new Grid { ColumnDefinitions = new("76,52,48,*,28"), ColumnSpacing = 4 };
            foreach (var (text, col) in new[] { ("Move", 0), ("Degrees", 1), ("mm", 2), ("How", 3) })
            {
                var t = Muted(text);
                Grid.SetColumn(t, col);
                head.Children.Add(t);
            }
            rows.Children.Add(head);
            var steps = get();
            for (int i = 0; i < steps.Count; i++)
            {
                int idx = i;
                var st = steps[i];
                var row = new Grid { ColumnDefinitions = new("76,52,48,*,28"), ColumnSpacing = 4 };
                var move = Pick(["Rise", "Wait", "Fall", "Drop"], (int)st.Move, v => { Update(idx, x => x with { Move = (CamMove)v }); Dispatcher.UIThread.Post(Fill); });
                var deg = Num(0, 360, 5, "0.#", st.Degrees, v => Update(idx, x => x with { Degrees = v }), narrow: true);
                deg.IsEnabled = st.Move != CamMove.Drop;
                var mm = Num(0, 100, 0.5m, "0.#", st.Mm, v => Update(idx, x => x with { Mm = v }), narrow: true);
                mm.IsEnabled = st.Move != CamMove.Wait;
                var law = Pick(["Smooth", "Harmonic", "3-4-5", "Steady"], (int)st.Law, v => Update(idx, x => x with { Law = (CamLaw)v }));
                law.IsEnabled = st.Move is CamMove.Rise or CamMove.Fall;
                var remove = new Button { Content = "✕", Padding = new(6, 2), IsEnabled = steps.Count > 1 };
                ToolTip.SetTip(remove, "Take this step away");
                remove.Click += (_, _) => { set(get().RemoveAt(idx)); Fill(); Changed(); };
                foreach (var (c, col) in new (Control, int)[] { (move, 0), (deg, 1), (mm, 2), (law, 3), (remove, 4) })
                {
                    Grid.SetColumn(c, col);
                    row.Children.Add(c);
                }
                rows.Children.Add(row);
            }
            add.IsEnabled = steps.Count < 16;
            Which();
        }
        preset.SelectionChanged += (_, _) =>
        {
            if (filling || preset.SelectedIndex < 0 || preset.SelectedIndex >= CamPresets.All.Length) return;
            set(CamPresets.All[preset.SelectedIndex].Steps);
            Fill();
            Changed();
        };
        add.Click += (_, _) => { set(get().Add(new CamStep(CamMove.Wait, 30, 0))); Fill(); Changed(); };
        Fill();
        return Stack(Field("Motion", preset), rows, add);
    }

    // ---- music box strip ----

    TextBox _tune = null!;
    NumericUpDown _transpose = null!;
    readonly TextBlock _tuneNote = Hint();

    Control StripPanel()
    {
        var s = Strip;
        var custom = new StackPanel { Spacing = 8 };
        var boxes = Movement.All.Select(m => m.Name).Append("Another box (set its sizes)").ToArray();
        int mi = Movement.All.ToList().FindIndex(m => m.Key == s.Movement);
        void Custom()
        {
            custom.Children.Clear();
            var st = Strip;
            custom.Children.Add(Field("Strip width", Num(10, 200, 0.5m, "0.##", st.WidthMm, v => Strip = Strip with { WidthMm = v }), Hint("Measure a strip that came with the box (mm).")));
            custom.Children.Add(Field("Lane spacing", Num(0.5m, 10, 0.1m, "0.##", st.PitchMm, v => Strip = Strip with { PitchMm = v }), Hint("From one note's holes to the next across the strip (mm).")));
            custom.Children.Add(Field("First lane", Num(1, 50, 0.1m, "0.##", st.FirstMm, v => Strip = Strip with { FirstMm = v }), Hint("From the edge to the lowest note's holes (mm).")));
            var tracks = new TextBox { Text = st.Tracks, AcceptsReturn = false, TextWrapping = TextWrapping.Wrap };
            tracks.TextChanged += (_, _) => { Strip = Strip with { Tracks = tracks.Text ?? "" }; Changed(); };
            custom.Children.Add(Field("Its notes", tracks, Hint("Lowest first, as names: C3 D3 G3 A3…")));
        }
        var box = Pick(boxes, mi < 0 ? boxes.Length - 1 : mi, v =>
        {
            Strip = v < Movement.All.Length ? Strip.WithMovement(Movement.All[v]) : Strip with { Movement = "custom" };
            custom.IsVisible = v >= Movement.All.Length;
            Custom();
        });
        custom.IsVisible = mi < 0;
        Custom();

        var tunes = Melody.Tunes.Select(t => t.Name).Append("Every note, lowest first (a test strip)").Append("Your own").ToArray();
        _tune = new TextBox { Text = s.Tune, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 120, MaxHeight = 220, FontFamily = new FontFamily("Consolas,Menlo,monospace") };
        bool filling = false;
        int Which()
        {
            int i = Melody.Tunes.ToList().FindIndex(t => t.Text == Strip.Tune);
            if (i >= 0) return i;
            try { if (Strip.Tune == Melody.EveryNote(Strip.Lanes())) return Melody.Tunes.Count; }
            catch (InvalidOperationException) { }
            return Melody.Tunes.Count + 1;
        }
        var tune = Pick(tunes, Which(), v =>
        {
            if (filling || v > Melody.Tunes.Count) return;
            string text;
            if (v < Melody.Tunes.Count) text = Melody.Tunes[v].Text;
            else
            {
                try { text = Melody.EveryNote(Strip.Lanes()); }
                catch (InvalidOperationException) { return; }
            }
            Strip = Strip with { Tune = text, Transpose = 0 };
            filling = true;
            _tune.Text = text;
            _transpose.Value = 0;
            filling = false;
        });
        _tune.TextChanged += (_, _) =>
        {
            if (filling) return;
            Strip = Strip with { Tune = _tune.Text ?? "" };
            filling = true;
            tune.SelectedIndex = Which();
            filling = false;
            Changed();
        };
        var midi = Button("Open a MIDI file…", tip: "Read the notes of a .mid file into the tune (every track but the drums), put on the nearest quarter of a beat.");
        midi.Click += async (_, _) => await OpenMidi();
        _transpose = Num(-36, 36, 1, "+0;-0;0", s.Transpose, v => Strip = Strip with { Transpose = (int)v });
        var fitIt = Button("Fit it to the box", tip: "Move the tune up or down to the key that leaves the fewest notes out.");
        fitIt.Click += (_, _) =>
        {
            try
            {
                var notes = Melody.Parse(Strip.Tune).Notes;
                int best = StripSpec.BestTranspose(notes, Strip.Lanes(), Strip.Octaves);
                _transpose.Value = best;
                _tuneNote.Text = best == 0 ? "It already fits as well as it can." : $"Moved {(best > 0 ? "up" : "down")} {Math.Abs(best)} semitone{(Math.Abs(best) == 1 ? "" : "s")}.";
            }
            catch (InvalidOperationException ex) { _tuneNote.Text = ex.Message; }
        };
        var keyRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _transpose, fitIt } };

        return Stack(
            Explained("The music box", "Paper-strip movements differ a little between makers: cut the test strip first to check the holes line up with yours.",
                Field("Box", box), custom,
                Field("Hole size", Num(0.5m, 6, 0.1m, "0.##", s.HoleMm, v => Strip = Strip with { HoleMm = v }), Hint("Across each hole (mm), as the box's own punch makes them."))),
            Explained("The tune", "Notes with a space between: C5 D5 E5. Add :2 for two beats or :0.5 for half; join notes with + to play them together (C4+E4); - is a rest.",
                Field("Start from", tune), _tune, midi,
                Field("Move it", keyRow, Hint("Up (+) or down (−) in semitones."), "Every note moves by this much."),
                _tuneNote,
                Check("Play notes the box hasn't got an octave up or down", s.Octaves, b => Strip = Strip with { Octaves = b })),
            Explained("The strip", "Too long for the bed, it's cut in pieces that lock together with a dovetail.",
                Field("Beat", Num(2, 40, 0.5m, "0.#", s.BeatMm, v => Strip = Strip with { BeatMm = v }), Hint("Strip for each beat (mm): longer gives repeated notes more room.")),
                Field("Lead-in", Num(5, 100, 1, "0", s.LeadMm, v => Strip = Strip with { LeadMm = v }), Hint("Blank strip before the first note, to feed it in by (mm).")),
                Check("Lanes from the other edge", s.Mirror, b => Strip = Strip with { Mirror = b }, "As if the strip were turned over: for a box that reads its lowest note from the other edge.")));
    }

    // ---- music box comb ----

    NumericUpDown? _combThick;
    readonly TextBlock _combNote = Hint();

    Control CombPanel()
    {
        var s = Comb;
        var custom = new StackPanel { Spacing = 8 };
        var boxes = Movement.All.Select(m => m.Name).Append("Another box (set its sizes)").ToArray();
        int mi = Movement.All.ToList().FindIndex(m => m.Key == s.Movement);
        void Custom()
        {
            custom.Children.Clear();
            var st = Comb;
            custom.Children.Add(Field("Strip width", Num(10, 200, 0.5m, "0.##", st.WidthMm, v => Comb = Comb with { WidthMm = v }), Hint("Measure a strip that came with the box (mm).")));
            custom.Children.Add(Field("Lane spacing", Num(0.8m, 10, 0.1m, "0.##", st.PitchMm, v => Comb = Comb with { PitchMm = v }), Hint("From one note's holes to the next across the strip (mm): the tines are this far apart.")));
            custom.Children.Add(Field("First lane", Num(1, 50, 0.1m, "0.##", st.FirstMm, v => Comb = Comb with { FirstMm = v }), Hint("From the edge to the lowest note's holes (mm).")));
            var tracks = new TextBox { Text = st.Tracks, AcceptsReturn = false, TextWrapping = TextWrapping.Wrap };
            tracks.TextChanged += (_, _) => { Comb = Comb with { Tracks = tracks.Text ?? "" }; Changed(); };
            custom.Children.Add(Field("Its notes", tracks, Hint("Lowest first, as names: C4 D4 E4 F4…")));
        }
        var box = Pick(boxes, mi < 0 ? boxes.Length - 1 : mi, v =>
        {
            Comb = v < Movement.All.Length ? Comb.WithMovement(Movement.All[v]) : Comb with { Movement = "custom" };
            custom.IsVisible = v >= Movement.All.Length;
            Custom();
        });
        custom.IsVisible = mi < 0;
        Custom();
        string[] octaves = ["Two octaves down", "An octave down", "As the lanes are named", "An octave up", "Two octaves up"];

        var metals = CombMetal.All.Select(m => m.Name).ToArray();
        int metal = Math.Max(0, CombMetal.All.ToList().FindIndex(m => m.Key == s.Metal));
        _combThick = Num(0.1m, 3, 0.01m, "0.###", s.ThicknessMm, v => Comb = Comb with { ThicknessMm = v });
        _combThick.Width = 130;
        var suggest = Button("Suggest", tip: $"The thickest shim sold (0.010\" to 0.040\") that keeps the bass tine within {CombSpec.LongestWanted:0} mm and the treble one at least {CombSpec.ShortestWanted:0} mm.");
        suggest.Click += (_, _) =>
        {
            try
            {
                double t = Comb.Suggest();
                _combThick.Value = (decimal)t;
                _combNote.Text = $"{t:0.###} mm ({t / 25.4:0.000}\") shim.";
            }
            catch (InvalidOperationException ex) { _combNote.Text = ex.Message; }
        };
        var thickRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _combThick, suggest } };

        return Stack(
            Explained("The strips it plays", "Its tines are as far apart as the strip's lanes, so the strips this maker punches line up with it: pick the same box.",
                Field("Box", box), custom,
                Check("Tines from the other edge", s.Mirror, b => Comb = Comb with { Mirror = b }, "Lowest note at the other end: as \"Lanes from the other edge\" on the strip."),
                Field("Sounds", Pick(octaves, s.Octave + 2, v => Comb = Comb with { Octave = v - 2 }), Hint("An octave up makes every tine about 71% as long (1/√2); down, about 1.4 times."), "The strips play the same tune whatever octave the comb is in.")),
            Explained("The metal", "Each tine's length is worked out from how stiff and heavy the metal is and how thick it is (a cantilever's first note). Width doesn't change the note.",
                Field("Metal", Pick(metals, metal, v => Comb = Comb with { Metal = CombMetal.All[v].Key })),
                Field("Thickness", thickRow, Hint("The stock's thickness (mm). Thicker is louder but makes every tine longer."), "0.5 mm (0.020\") is a good start for a 15-note comb."),
                _combNote,
                Field("Gap", Num(0.1m, 2, 0.05m, "0.##", s.GapMm, v => Comb = Comb with { GapMm = v }), Hint("The slot between tines (mm): each tine is the lane spacing less this wide.")),
                Field("Tuning allowance", Num(0, 5, 0.1m, "0.#", s.ExtraMm, v => Comb = Comb with { ExtraMm = v }), Hint("Cut each tine this much long (mm) so it comes out flat, and only ever file the tips to tune up."))),
            Explained("Holding it", "Two thick clamp bars, cut from steel plate with the same stepped edge, pinch the comb at each tine's root. The stiffer they are, the truer and louder the notes.",
                Check("Line the tips up, as on a real comb", s.AlignTips, b => Comb = Comb with { AlignTips = b }, "Where the strip plucks them. Untick to line the roots up instead, so a plain straight bar clamps it."),
                Field("Base", Num(6, 40, 1, "0", s.BaseMm, v => Comb = Comb with { BaseMm = v }), Hint("Solid metal behind the longest tine, for the bolts (mm).")),
                Field("Clamp bars", Num(1, 10, 0.5m, "0.#", s.ClampMm, v => Comb = Comb with { ClampMm = v }), Hint("Their thickness (mm): 3 mm steel or more.")),
                Field("Bolts", Num(1.5m, 8, 0.5m, "0.#", s.BoltMm, v => Comb = Comb with { BoltMm = v }), Hint("Metric size: 3 is M3."))),
            Explained("Making it", "The help has all of this in full.",
                Hint("Stock: blue-tempered 1095 or full-hard 301 shim is already spring-hard, so cut it and use it. Annealed spring steel must be hardened (heated to non-magnetic, about 800 °C, and quenched in warm oil between two steel plates), then tempered at about 370 to 400 °C for an hour, which a kitchen oven can't reach."),
                Hint("Cutting it on a fiber laser: many quick passes, not a few slow ones, so the thin tines don't heat up and warp. Start at 100% power, about 100 mm/s, 30 to 40 kHz and the longest pulse your laser has (MOPA: 350 to 500 ns), and add passes until it drops through (about 20 to 40 for 0.5 mm). Raise the sheet on spacers so it doesn't weld to the bed, and run a cut test on an offcut first."),
                Hint("Then smooth each tine's edges along its length with a fine stone, clamp it up, and tune it.")));
    }

    async Task OpenMidi()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "MIDI file",
            FileTypeFilter =
            [
                new FilePickerFileType("MIDI files") { Patterns = ["*.mid", "*.midi", "*.kar"] },
                new FilePickerFileType("All files") { Patterns = ["*"] },
            ],
        });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path) return;
        try
        {
            var bytes = await File.ReadAllBytesAsync(path);
            UseMidi(bytes, Path.GetFileName(path));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            _tuneNote.Text = $"Couldn't read it: {e.Message}";
        }
    }

    /// <summary>Use the notes of a MIDI file as the tune, moved to fit the box (also how the tests load one).</summary>
    internal void UseMidi(byte[] bytes, string name)
    {
        var notes = MidiFile.Read(bytes);
        var text = Melody.Write(notes);
        int best = 0;
        try { best = StripSpec.BestTranspose(notes, Strip.Lanes(), Strip.Octaves); }
        catch (InvalidOperationException) { }
        Strip = Strip with { Tune = text, Transpose = best };
        _tune.Text = text;
        _transpose.Value = best;
        _tuneNote.Text = $"Read {notes.Count} notes from {name}" + (best != 0 ? $", moved {(best > 0 ? "up" : "down")} {Math.Abs(best)} semitones to fit the box." : ".");
        Changed();
    }

    // ---- remembered between visits ----

    sealed record Saved(int Kind, string[] Specs, Guid? Recipe, bool Marks, double Thickness, double Fit, double Speed);

    Guid? _savedRecipe;

    string? SavePath => _dataFolder is null ? null : Path.Combine(_dataFolder, "last.json");

    void Load()
    {
        if (SavePath is not { } path || !File.Exists(path)) return;
        try
        {
            var s = JsonSerializer.Deserialize<Saved>(File.ReadAllText(path), ToyJson.Options);
            if (s is null) return;
            for (int i = 0; i < Math.Min(s.Specs.Length, _specs.Length); i++)
                if (ToySpec.FromJson(s.Specs[i]) is { } spec && KindOf(spec) == i) _specs[i] = spec;
            _kind.SelectedIndex = Math.Clamp(s.Kind, 0, Kinds.Length - 1);
            _savedRecipe = s.Recipe;
            _marks.IsChecked = s.Marks;
            _thick.Value = (decimal)Math.Clamp(s.Thickness, 0.5, 25);
            _fit.Value = (decimal)Math.Clamp(s.Fit, -0.5, 1);
            _speed.Value = Math.Clamp(s.Speed, 1, 60);
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException or NotSupportedException) { }
    }

    void Save()
    {
        if (SavePath is not { } path) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var s = new Saved(Kind, [.. _specs.Select(x => x.ToJson())], Cut()?.R.Id, _marks.IsChecked == true, (double)(_thick.Value ?? 3), (double)(_fit.Value ?? 0.1m), _speed.Value);
            File.WriteAllText(path, JsonSerializer.Serialize(s, ToyJson.Options));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>For the screenshots and tests: build now and wait.</summary>
    internal Task BuildNow()
    {
        _debounce.Stop();
        return Rebuild();
    }

    /// <summary>For the screenshots: show a kind with these settings.</summary>
    internal void ShowToy(ToySpec spec)
    {
        int k = KindOf(spec);
        _specs[k] = spec;
        _shownKind = -1;
        _kind.SelectedIndex = k;
        ShowKind();
    }
}
