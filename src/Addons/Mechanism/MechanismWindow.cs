using System.Collections.Immutable;
using System.Text.Json;
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

namespace PhotonCanvas.Addon.Mechanism;

/// <summary>
/// The mechanism maker: pick gears, a rack, planetary gears, a cam or an automaton box, set it up, and watch it
/// move while the motion check runs: teeth that jam, followers that can't follow, cranks that hit the base.
/// Then put the parts in the design on a cut layer with the material's own kerf, ready to cut.
/// </summary>
public sealed class MechanismWindow : Window
{
    static readonly string[] Kinds = ["Gears", "Rack and pinion", "Planetary gears", "Cam and follower", "Automaton box"];
    static readonly string[] KindWords =
    [
        "A train of spur gears on a plate: each one meshes with the one before, or shares its axle for a bigger change of speed.",
        "A gear that slides a toothed bar along: turning becomes sliding.",
        "A sun gear, planets round it and a ring with teeth inside: a big slow-down in a small round space.",
        "A shaped wheel that lifts a follower step by step: rise, wait, fall or drop, as you set them.",
        "A box with a handle, cams on a shaft and platforms on top: stand your figures on them and they move.",
    ];

    readonly Document _doc;
    readonly MachineProfile _machine;
    readonly MaterialLibrary? _library;
    readonly string? _dataFolder;
    (Guid Id, MechSpec Spec)? _editing;
    readonly MechSpec[] _specs = [new GearTrainSpec(), new RackSpec(), new PlanetarySpec(), new CamSpec(), new AutomatonSpec()];

    /// <summary>What to make: a tab each (the tabs hold nothing; the window below changes with them).</summary>
    readonly TabControl _kind = new() { Margin = new Thickness(14, 8, 14, 0), Padding = new Thickness(0) };
    readonly TextBlock _kindWords = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 0, 2, 0) };
    readonly Border _settings = new();
    readonly ComboBox _cutWith = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    List<(Material M, Recipe R)?> _cuts = [];
    readonly NumericUpDown _thick = Number(0.5m, 25, 0.1m, "0.##");
    readonly NumericUpDown _fit = Number(-0.5m, 1, 0.05m, "+0.##;-0.##;0");
    readonly TextBlock _cutNote = Hint();
    Control _fitRow = null!;
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
    Mechanism? _built;
    MechSpec? _builtFrom;
    int _shownKind = -1;

    public MechanismWindow(Document doc, MachineProfile machine, MaterialLibrary? library = null, string? dataFolder = null, (Guid Id, MechSpec Spec)? editing = null)
    {
        _doc = doc;
        _machine = machine;
        _library = library;
        _dataFolder = dataFolder;
        Title = "Mechanism maker";
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
            _thick.Value = (decimal)e.Spec.ThicknessMm;
            _fit.Value = (decimal)e.Spec.SlotFitMm;
            _make.Content = "Update in the design";
        }

        _debounce.Tick += (_, _) => { _debounce.Stop(); _ = Rebuild(); };
        _kind.SelectionChanged += (_, _) => { ShowKind(); Changed(); };
        _thick.ValueChanged += (_, _) => Changed();
        _fit.ValueChanged += (_, _) => Changed();
        _cutWith.SelectionChanged += (_, _) => CutChosen();
        _play.Click += (_, _) => _view.Playing = !_view.Playing;
        _view.PlayingChanged += () => _play.Content = _view.Playing ? "Pause" : "Play";
        _speed.PropertyChanged += (_, e) => { if (e.Property == RangeBase.ValueProperty) Speed(); };
        _make.Click += (_, _) => Make();
        Speed();

        // ---- left: what to make, its settings, the material ----
        _fitRow = Field("Slot fit", _fit, Hint("Slots for the box's tabs are the sheet's thickness plus this."), "From the cut test: + is looser, − is tighter.");
        var material = Explained("Material", "What it's cut from. Picking your material's cut settings brings its thickness and, from your cut test, its kerf and slot fit.",
            Field("Cut with", _cutWith),
            Field("Thickness", _thick, tip: "The sheet's thickness (mm): spacers, slots and axle lengths follow it."),
            _fitRow,
            _cutNote,
            _marks);
        ToolTip.SetTip(_marks, "Names, turning arrows and lines to line parts up, engraved lightly on a Score layer before the parts are cut out.");
        var left = new StackPanel
        {
            Spacing = 12,
            Margin = new Thickness(14),
            Children =
            {
                _kindWords,
                _settings,
                material,
            },
        };
        var leftScroll = new ScrollViewer { Content = left, Width = 420, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };

        // ---- right: the motion check ----
        var controls = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Children = { _play, Muted("Speed").With(m => m.VerticalAlignment = VerticalAlignment.Center), _speed, _speedText, Hint("Drag a part round to turn it by hand, or click the strip.").With(h => h.VerticalAlignment = VerticalAlignment.Center) },
        };
        var results = new StackPanel { Spacing = 8, Children = { _summary, _findings, _shopping } };
        var right = new Grid { RowDefinitions = new("*,Auto,Auto"), Margin = new Thickness(0, 14, 14, 0), RowSpacing = 8 };
        right.Children.Add(new Border { CornerRadius = new(8), ClipToBounds = true, BorderBrush = Brush("Line"), BorderThickness = new(1), Child = _view });
        Grid.SetRow(controls, 1);
        right.Children.Add(controls);
        var resultScroll = new ScrollViewer { Content = results, MaxHeight = 230, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(resultScroll, 2);
        right.Children.Add(resultScroll);

        // ---- bottom: put it in ----
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

    static int KindOf(MechSpec s) => s switch { GearTrainSpec => 0, RackSpec => 1, PlanetarySpec => 2, CamSpec => 3, _ => 4 };

    int Kind => Math.Clamp(_kind.SelectedIndex, 0, Kinds.Length - 1);

    /// <summary>The settings as they are now, made of the sheet chosen.</summary>
    MechSpec Current() => _specs[Kind] with { ThicknessMm = (double)(_thick.Value ?? 3), SlotFitMm = (double)(_fit.Value ?? 0.1m), Material = Cut()?.M.Category ?? "" };

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
                // Part of the check: will each part go on this machine's bed.
                m.Findings.AddRange(Placement.TooBig(m, machine));
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
            AppLog.Error("mechanism", e);
            _built = null;
            _view.Show(null);
            ShowResults(null, $"Couldn't make that: {e.Message}");
        }
    }

    void ShowResults(Mechanism? m, string? error)
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
        _summary.Text = m.Summary + (bad > 0 ? $" {bad} problem{(bad == 1 ? "" : "s")} to fix (red)." : warn > 0 ? $" {warn} thing{(warn == 1 ? "" : "s")} to watch (orange)." : " The motion check found nothing wrong.");
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
        if (Cut() is { } c)
        {
            if (c.M.ThicknessMm is { } t) _thick.Value = (decimal)t;
            if (c.R.SlotFitMm is { } fit) _fit.Value = (decimal)fit;
        }
        Note();
        Changed();
    }

    void Note()
    {
        if (Cut() is not { } c)
        {
            _cutNote.Text = "No kerf allowance: the laser burns each edge back by half its kerf, so gears get a little more play and slots come out looser. Pick your material's cut settings (Laser → Tests → Cut test measures the kerf) for parts at their drawn size.";
            return;
        }
        var notes = new List<string>();
        if (c.M.ThicknessMm is { } t) notes.Add($"{t:0.##} mm thick");
        notes.Add(c.R.Settings.KerfMm > 0 ? $"kerf {c.R.Settings.KerfMm:0.###} mm, allowed for on the cut layer so the parts come out their drawn size" : "no kerf measured yet (run a cut test)");
        if (c.R.SlotFitMm is { } fit) notes.Add($"slot fit {fit:+0.##;-0.##;0} mm from your cut test");
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
            // Made again: the old parts are replaced, wherever they are.
            Guid? replace = _editing is { } e && Placement.Exists(_doc.State, e.Id) ? e.Id : null;
            bool marks = _marks.IsChecked == true;
            var before = _doc.State;
            // Nesting takes a moment: done away from the window, then put in as one step (one undo).
            var (m, s, sheets, id) = await Task.Run(() =>
            {
                var m = built ?? spec.Build();
                var (s, sheets, id) = Placement.Place(before, m, spec, _machine, new PlaceOptions { CutSettings = settings, Marks = marks, Replace = replace });
                return (m, s, sheets, id);
            });
            if (!ReferenceEquals(_doc.State, before)) { _status.Text = "The design changed meanwhile: press again to put the parts in."; return; }
            _doc.Commit(replace is null ? $"Mechanism: {m.Title}" : $"Update the {m.Title.ToLowerInvariant()}", s, Placement.ShapesOf(s, id));
            _editing = (id, spec);
            _make.Content = "Update in the design";
            int parts = m.Parts.Sum(p => p.Copies);
            _status.Text = $"{(replace is null ? "Put" : "Updated")} {parts} part{(parts == 1 ? "" : "s")} in the design on \"{Placement.CutLayer}\"" +
                           (sheets > 1 ? $", over {sheets} pages (a bed each)" : "") +
                           (cut is { } cc ? $", cut with {cc.M.Label} · {cc.R.Title}" : "") +
                           (marks ? $"; names and marks on \"{Placement.MarkLayer}\"" : "") + "." +
                           (m.Worst == Severity.Bad ? " ⚠ The motion check found problems (in red): it may not work as it is." : "");
        }
        catch (InvalidOperationException ex) { _status.Text = ex.Message; }
        catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException or ArithmeticException or NullReferenceException)
        {
            AppLog.Error("mechanism", ex);
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
        _kindWords.Text = KindWords[k] + " Each one is checked moving, all the way round, before you cut it.";
        _fitRow.IsVisible = k == 4;
        if (_shownKind == k) return;
        _shownKind = k;
        _settings.Child = k switch
        {
            0 => GearsPanel(),
            1 => RackPanel(),
            2 => PlanetaryPanel(),
            3 => CamPanel(),
            _ => AutomatonPanel(),
        };
    }

    GearTrainSpec Gears { get => (GearTrainSpec)_specs[0]; set => _specs[0] = value; }
    RackSpec Rack { get => (RackSpec)_specs[1]; set => _specs[1] = value; }
    PlanetarySpec Planetary { get => (PlanetarySpec)_specs[2]; set => _specs[2] = value; }
    CamSpec Cam { get => (CamSpec)_specs[3]; set => _specs[3] = value; }
    AutomatonSpec Automaton { get => (AutomatonSpec)_specs[4]; set => _specs[4] = value; }

    /// <summary>A number box that changes the settings; <paramref name="narrow"/> for a table's cells (no buttons: the wheel and arrow keys still step it).</summary>
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

    static readonly double[] Angles = [14.5, 20, 25];

    Control TeethFields(Func<ToothForm> get, Action<ToothForm> set)
    {
        var f = get();
        var module = Num(0.5m, 10, 0.25m, "0.##", f.Module, v => set(get() with { Module = v }));
        int ai = Array.FindIndex(Angles, a => Math.Abs(a - f.PressureAngleDeg) < 0.01);
        var angle = Pick(["14.5° (old style)", "20° (standard)", "25° (stronger, fewer teeth)"], ai < 0 ? 1 : ai, i => set(get() with { PressureAngleDeg = Angles[i] }));
        var play = Num(0, 1, 0.05m, "0.##", f.BacklashMm, v => set(get() with { BacklashMm = v }));
        return Explained("Teeth", "The same for every gear that meshes.",
            Field("Tooth size", module, Hint("Module: mm across the gear for each tooth. 2 to 3 is sturdy in 3 mm wood."), "A gear's size is its number of teeth × this (mm), at the circle where it meets the next."),
            Field("Tooth angle", angle, tip: "The pressure angle: 20° is standard; 25° makes stronger teeth and lets small gears have fewer teeth without being undercut."),
            Field("Play", play, Hint("Room between the teeth (mm), so they never bind."), "Backlash, shared between the two gears of each pair."));
    }

    Control AxleFields(Func<Axle> get, Action<Axle> set, string what)
    {
        var a = get();
        var size = Num(2, 25, 0.5m, "0.#", a.SizeMm, v => set(get() with { SizeMm = v }));
        var square = Check("Square: grips the parts on it, turns in round holes", a.Square, b => set(get() with { Square = b }));
        return Stack(Field(what, size, Hint("The dowel or rod's width (mm). Holes it turns in get 0.3 mm of room; holes that hold it are snug, to glue."), "Round dowel, or square dowel for parts that must turn with it."), square);
    }

    /// <summary>
    /// A part's hole for its shaft: the mechanism's usual one (<paramref name="usual"/> says what that is), or a
    /// round, D or square one of its own with a fit. <paramref name="fitWhenChosen"/> is the fit a new one starts with.
    /// </summary>
    Control HoleFields(string label, Func<Hole?> get, Action<Hole?> set, string usual, Func<double> size, HoleFit fitWhenChosen, string? tip = null)
    {
        var host = new StackPanel { Spacing = 8 };
        void Fill()
        {
            host.Children.Clear();
            var h = get();
            var shape = Pick([usual, "Round", "D (round with a flat)", "Square"], h is null ? 0 : 1 + (int)h.Shape, v =>
            {
                if (v == 0) set(null);
                else
                {
                    var was = get() ?? new Hole { SizeMm = size(), Fit = fitWhenChosen };
                    var to = (HoleShape)(v - 1);
                    // A D or square hole always turns with its shaft: no free fit for it.
                    set(was with { Shape = to, Fit = to != HoleShape.Round && was.Fit == HoleFit.Free ? HoleFit.Snug : was.Fit });
                }
                Dispatcher.UIThread.Post(Fill);
            });
            host.Children.Add(Field(label, shape, tip: tip ?? "Round with a fit (press, snug, slip or free), D-shaped for a motor shaft or a dowel with a flat, or square."));
            if (h is null) return;
            host.Children.Add(Field("Shaft size", Num(1, 50, 0.1m, "0.##", h.SizeMm, v => { if (get() is { } c) set(c with { SizeMm = v }); }),
                Hint(h.Shape == HoleShape.Square ? "The square shaft's side (mm)." : "The shaft's diameter (mm).")));
            if (h.Shape == HoleShape.D)
                host.Children.Add(Field("Across the flat", Num(0.5m, 50, 0.1m, "0.##", h.Shaft.Flat, v => { if (get() is { } c) set(c with { FlatMm = v }); }),
                    Hint("From the flat to the far side of the shaft (mm).")));
            HoleFit[] fits = h.Shape == HoleShape.Round ? Fits.All : [HoleFit.Press, HoleFit.Snug, HoleFit.Slip];
            int fi = Array.IndexOf(fits, h.Fit);
            host.Children.Add(Field("Fit", Pick([.. fits.Select(Fits.Words)], fi < 0 ? 1 : fi, v => { if (get() is { } c) set(c with { Fit = fits[v] }); }),
                Hint("Drawn for the material picked below (wood squashes, acrylic cracks); the list under the picture gives each hole's size."),
                "Press and snug grip the shaft, so the part turns with it; slip and free let it turn on the shaft."));
            host.Children.Add(Field("Adjust", Num(-1, 1, 0.02m, "+0.##;-0.##;0", h.AdjustMm, v => { if (get() is { } c) set(c with { AdjustMm = v }); }),
                Hint("Added across the hole (mm): + is looser. For dowels that aren't quite their size.")));
        }
        Fill();
        return host;
    }

    /// <summary>A handle hole: on or off, how far out it sits, and the peg's hole.</summary>
    Control HandleFields(string text, Func<Crank> get, Action<Crank> set, Func<double> axleSize)
    {
        var host = new StackPanel { Spacing = 8 };
        void Fill()
        {
            host.Children.Clear();
            var c = get();
            var on = new CheckBox { Content = text, IsChecked = c.On };
            ToolTip.SetTip(on, "A hole off the middle for a peg (a crank pin) to turn it by.");
            on.IsCheckedChanged += (_, _) =>
            {
                set(get() with { On = on.IsChecked == true });
                Changed();
                Dispatcher.UIThread.Post(Fill);
            };
            host.Children.Add(on);
            if (!c.On) return;
            host.Children.Add(Field("Handle radius", Num(0, 500, 0.5m, "0.#", c.RadiusMm, v => set(get() with { RadiusMm = v })),
                Hint("From the middle to the handle hole's centre (mm). 0 puts it as far out as there's room for.")));
            host.Children.Add(HoleFields("Peg hole", () => get().Hole, h => set(get() with { Hole = h }), $"Snug, {Math.Max(4, axleSize()):0.#} mm (usual)", axleSize, HoleFit.Snug,
                "The peg's hole: snug or press to glue a peg in, free for a peg that spins in your fingers, or D or square."));
        }
        Fill();
        return host;
    }

    readonly HashSet<int> _openGears = [];

    Control GearsPanel()
    {
        var rows = new StackPanel { Spacing = 6 };
        var extras = new StackPanel { Spacing = 6 };
        var add = Button("Add a gear", tip: "Another gear meshing with the last one (or set it to share the last one's axle).");
        void Fill()
        {
            rows.Children.Clear();
            var gears = Gears.Gears;
            var head = new Grid { ColumnDefinitions = new("22,58,*,58,30"), ColumnSpacing = 6 };
            foreach (var (text, col) in new[] { ("", 0), ("Teeth", 1), ("Placed", 2), ("Angle", 3) })
            {
                var t = Muted(text);
                Grid.SetColumn(t, col);
                head.Children.Add(t);
            }
            rows.Children.Add(head);
            for (int i = 0; i < gears.Count; i++)
            {
                int idx = i;
                var g = gears[i];
                var row = new Grid { ColumnDefinitions = new("22,58,*,58,30"), ColumnSpacing = 6 };
                var letter = new TextBlock { Text = GearTrainSpec.Letter(i), FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
                row.Children.Add(letter);
                var teeth = Num(6, 200, 1, "0", g.Teeth, v => Gears = Gears with { Gears = Gears.Gears.SetItem(idx, Gears.Gears[idx] with { Teeth = (int)v }) }, narrow: true);
                Grid.SetColumn(teeth, 1);
                row.Children.Add(teeth);
                if (i > 0)
                {
                    string prev = GearTrainSpec.Letter(i - 1);
                    var link = Pick([$"meshes with {prev}", $"on {prev}'s axle"], (int)g.Link, v =>
                    {
                        Gears = Gears with { Gears = Gears.Gears.SetItem(idx, Gears.Gears[idx] with { Link = (GearLink)v }) };
                        Dispatcher.UIThread.Post(Fill);
                    });
                    Grid.SetColumn(link, 2);
                    row.Children.Add(link);
                    var angle = Num(-360, 360, 15, "0", g.AngleDeg, v => Gears = Gears with { Gears = Gears.Gears.SetItem(idx, Gears.Gears[idx] with { AngleDeg = v }) }, narrow: true);
                    angle.IsEnabled = g.Link == GearLink.Meshes;
                    ToolTip.SetTip(angle, $"Which way from {prev} it sits (degrees: 0 = right, 90 = up).");
                    Grid.SetColumn(angle, 3);
                    row.Children.Add(angle);
                    var remove = new Button { Content = "✕", Padding = new(6, 2) };
                    ToolTip.SetTip(remove, "Take this gear away");
                    remove.Click += (_, _) =>
                    {
                        Gears = Gears with { Gears = Gears.Gears.RemoveAt(idx) };
                        Fill();
                        Changed();
                    };
                    Grid.SetColumn(remove, 4);
                    row.Children.Add(remove);
                }
                else
                {
                    var turned = Muted("turned by hand");
                    turned.VerticalAlignment = VerticalAlignment.Center;
                    Grid.SetColumn(turned, 2);
                    row.Children.Add(turned);
                }
                rows.Children.Add(row);
            }
            add.IsEnabled = gears.Count < 8;
            extras.Children.Clear();
            for (int i = 0; i < gears.Count; i++)
            {
                int idx = i;
                GearRow Row() => Gears.Gears[idx];
                void SetRow(GearRow r, bool legacyOff = false) => Gears = Gears with { Gears = Gears.Gears.SetItem(idx, r), Handle = legacyOff ? false : Gears.Handle };
                var ex = new Expander
                {
                    Header = $"Gear {GearTrainSpec.Letter(i)}: hole and handle",
                    IsExpanded = _openGears.Contains(i),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Content = Stack(
                        HoleFields("Axle hole", () => Row().Hole, h => SetRow(Row() with { Hole = h }), "Turns freely (usual)", () => Gears.Axle.SizeMm, HoleFit.Press),
                        HandleFields("A handle hole", () => Gears.HandleOf(idx) ?? Row().Handle ?? new Crank { On = false }, c => SetRow(Row() with { Handle = c }, legacyOff: idx == 0), () => Gears.Axle.SizeMm)),
                };
                ex.PropertyChanged += (_, e) =>
                {
                    if (e.Property != Expander.IsExpandedProperty) return;
                    if (ex.IsExpanded) _openGears.Add(idx); else _openGears.Remove(idx);
                };
                extras.Children.Add(ex);
            }
        }
        add.Click += (_, _) =>
        {
            var last = Gears.Gears[^1];
            Gears = Gears with { Gears = Gears.Gears.Add(new GearRow { Teeth = 20, AngleDeg = last.Link == GearLink.Meshes ? last.AngleDeg : 0 }) };
            Fill();
            Changed();
        };
        Fill();
        var s = Gears;
        return Stack(
            TeethFields(() => Gears.Form, f => Gears = Gears with { Form = f }),
            Explained("Gears", "A drives B, B drives C… A gear on the axle of the one before turns with it: big then small makes a big change of speed in a small space.", rows, add,
                Check("Avoid undercut (stronger small gears)", s.AvoidUndercut, b => Gears = Gears with { AvoidUndercut = b }, "Small gears' teeth are shifted outwards so they aren't cut thin at the root; their axles move a little further apart."),
                Check("A plate to hold the axles, with spacers", s.Plate, b => Gears = Gears with { Plate = b }),
                Check("Spokes in the bigger gears", s.Spokes, b => Gears = Gears with { Spokes = b }),
                AxleFields(() => Gears.Axle, a => Gears = Gears with { Axle = a }, "Axles")),
            Explained("Holes and handles", "Each gear's hole for its axle (round with a fit, D or square) and a handle hole in any gear, as far out as you set.", extras));
    }

    Control RackPanel()
    {
        var s = Rack;
        return Stack(
            TeethFields(() => Rack.Form, f => Rack = Rack with { Form = f }),
            Explained("Rack and pinion", "The pinion turns on its axle; the rack slides on two pins through its slot.",
                Field("Pinion teeth", Num(6, 80, 1, "0", s.PinionTeeth, v => Rack = Rack with { PinionTeeth = (int)v })),
                Field("Travel", Num(10, 500, 5, "0", s.TravelMm, v => Rack = Rack with { TravelMm = v }), Hint("How far the rack slides, end to end (mm).")),
                Check("Avoid undercut (stronger pinion)", s.AvoidUndercut, b => Rack = Rack with { AvoidUndercut = b }),
                Check("A plate with the axle and the rack's pins", s.Plate, b => Rack = Rack with { Plate = b }),
                AxleFields(() => Rack.Axle, a => Rack = Rack with { Axle = a }, "Axle and pins"),
                HoleFields("Pinion's hole", () => Rack.PinionHole, h => Rack = Rack with { PinionHole = h }, "Turns freely (usual)", () => Rack.Axle.SizeMm, HoleFit.Press),
                HandleFields("A handle hole in the pinion", () => Rack.Crank with { On = Rack.Handle }, c => Rack = Rack with { Handle = c.On, Crank = c }, () => Rack.Axle.SizeMm)));
    }

    Control PlanetaryPanel()
    {
        var s = Planetary;
        var ring = Hint();
        void Ring() => ring.Text = $"The ring gets {Planetary.RingTeeth} teeth (sun + 2 × planet).";
        Ring();
        return Stack(
            TeethFields(() => Planetary.Form, f => Planetary = Planetary with { Form = f }),
            Explained("Planetary gears", "Hold one part still, turn another, and the third turns slower or faster.",
                Field("Sun teeth", Num(6, 80, 1, "0", s.SunTeeth, v => { Planetary = Planetary with { SunTeeth = (int)v }; Ring(); })),
                Field("Planet teeth", Num(6, 80, 1, "0", s.PlanetTeeth, v => { Planetary = Planetary with { PlanetTeeth = (int)v }; Ring(); })),
                ring,
                Field("Planets", Num(1, 8, 1, "0", s.Planets, v => Planetary = Planetary with { Planets = (int)v })),
                Field("Held still", Pick(["The ring (turn the sun)", "The carrier (turn the sun)", "The sun (turn the ring)"], (int)s.Held, v => Planetary = Planetary with { Held = (PlanetaryHeld)v }),
                    Hint("Fixed to the base. In brackets: the part you turn by hand.")),
                Field("Planet pins", Num(2, 12, 0.5m, "0.#", s.PlanetPinMm, v => Planetary = Planetary with { PlanetPinMm = v }), Hint("The dowels the planets turn on (mm).")),
                Field("Ring's rim", Num(4, 40, 1, "0", s.RimMm, v => Planetary = Planetary with { RimMm = v }), Hint("Wood round the ring's teeth (mm).")),
                AxleFields(() => Planetary.Axle, a => Planetary = Planetary with { Axle = a }, "Sun's axle"),
                HoleFields("Sun's hole", () => Planetary.SunHole, h => Planetary = Planetary with { SunHole = h }, "Grips the axle (usual)", () => Planetary.Axle.SizeMm, HoleFit.Press)));
    }

    Control CamPanel()
    {
        var s = Cam;
        return Stack(
            Explained("The motion", "What the follower does over one turn of the cam, step by step.",
                StepsEditor(() => Cam.Steps, v => Cam = Cam with { Steps = v })),
            Explained("Cam and follower", "The follower slides on two pins in a backboard; its foot rides on the cam.",
                CamFields(() => (Cam.BaseRadiusMm, Cam.Foot, Cam.FootMm), (r, f, m) => Cam = Cam with { BaseRadiusMm = r, Foot = f, FootMm = m }),
                Field("Turns", Pick(["Anticlockwise", "Clockwise"], s.Clockwise ? 1 : 0, v => Cam = Cam with { Clockwise = v == 1 }), tip: "Looking at the front of the cam. It matters for a sudden drop, which only works one way."),
                AxleFields(() => Cam.Axle, a => Cam = Cam with { Axle = a }, "Shaft and pins"),
                HoleFields("Cam's hole", () => Cam.CamHole, h => Cam = Cam with { CamHole = h }, "Grips the shaft (usual)", () => Cam.Axle.SizeMm, HoleFit.Press, "The cam's hole for its shaft, which sets the shaft's shape: round with a press or snug fit, D-shaped or square."),
                HandleFields("A handle hole in the cam", () => Cam.Crank with { On = Cam.Handle }, c => Cam = Cam with { Handle = c.On, Crank = c }, () => Cam.Axle.SizeMm)));
    }

    Control CamFields(Func<(double R, FollowerFoot Foot, double Mm)> get, Action<double, FollowerFoot, double> set)
    {
        var (r, foot, mm) = get();
        return Stack(
            Field("Base radius", Num(5, 150, 1, "0.#", r, v => { var g = get(); set(v, g.Foot, g.Mm); }), Hint("The cam's radius where the follower is lowest (mm). Bigger pushes more gently.")),
            Field("Foot", Pick(["Round: follows every dip", "Flat: can't get into dips"], (int)foot, v => { var g = get(); set(g.R, (FollowerFoot)v, g.Mm); })),
            Field("Foot size", Num(4, 80, 1, "0.#", mm, v => { var g = get(); set(g.R, g.Foot, v); }), Hint("A round foot's diameter, or a flat foot's width (mm).")));
    }

    /// <summary>A cam's steps, one row each, with a choice of ready-made motions.</summary>
    Control StepsEditor(Func<ImmutableList<CamStep>> get, Action<ImmutableList<CamStep>> set)
    {
        var preset = new ComboBox { ItemsSource = CamPresets.All.Select(p => p.Name).Append("Your own steps").ToList(), HorizontalAlignment = HorizontalAlignment.Stretch };
        var rows = new StackPanel { Spacing = 4 };
        var total = Hint();
        var add = Button("Add a step");
        bool filling = false;
        void Total()
        {
            var steps = get();
            double sum = steps.Where(x => x.Move != CamMove.Drop).Sum(x => x.Degrees);
            total.Text = Math.Abs(sum - 360) < 0.5 ? "The steps fill the turn (360°)." : $"The steps take {sum:0.#}°: they're {(sum > 360 ? "squeezed" : "stretched")} to fill the turn.";
            filling = true;
            var found = CamPresets.Find(steps);
            preset.SelectedIndex = found is null ? CamPresets.All.Length : CamPresets.All.IndexOf(found);
            filling = false;
        }
        void Update(int idx, Func<CamStep, CamStep> f)
        {
            set(get().SetItem(idx, f(get()[idx])));
            Total();
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
                ToolTip.SetTip(law, "Smooth starts and stops without a jolt; Harmonic is gentlest in the middle; 3-4-5 is like Smooth; Steady knocks at each end.");
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
            Total();
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
        return Stack(Field("Start from", preset), rows, add, total);
    }

    Control AutomatonPanel()
    {
        var cams = new StackPanel { Spacing = 8 };
        var add = Button("Add a cam", tip: "Another cam on the shaft, with its own follower and platform.");
        void Fill()
        {
            cams.Children.Clear();
            var stations = Automaton.Stations;
            for (int i = 0; i < stations.Count; i++)
            {
                int idx = i;
                var st = stations[i];
                Station Get() => Automaton.Stations[idx];
                void Set(Station v) => Automaton = Automaton with { Stations = Automaton.Stations.SetItem(idx, v) };
                var remove = Button("Take this cam away");
                remove.IsEnabled = stations.Count > 1;
                remove.Click += (_, _) => { Automaton = Automaton with { Stations = Automaton.Stations.RemoveAt(idx) }; Fill(); Changed(); };
                var preset = CamPresets.Find(st.Steps);
                var ex = new Expander
                {
                    Header = $"Cam {i + 1}: {preset?.Name.ToLowerInvariant() ?? "your own steps"}",
                    IsExpanded = i == stations.Count - 1,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Content = Stack(
                        StepsEditor(() => Get().Steps, v => Set(Get() with { Steps = v })),
                        CamFields(() => (Get().BaseRadiusMm, Get().Foot, Get().FootMm), (r, f, m) => Set(Get() with { BaseRadiusMm = r, Foot = f, FootMm = m })),
                        Field("Starts", Num(0, 359, 15, "0", st.StartDeg, v => Set(Get() with { StartDeg = v })), Hint("How far into its steps this cam is when the handle starts (degrees): puts the figures out of step.")),
                        remove),
                };
                cams.Children.Add(ex);
            }
            add.IsEnabled = stations.Count < AutomatonSpec.MaxStations;
        }
        add.Click += (_, _) =>
        {
            var n = Automaton.Stations.Count;
            Automaton = Automaton with { Stations = Automaton.Stations.Add(new Station { StartDeg = n * 90 % 360 }) };
            Fill();
            Changed();
        };
        Fill();
        var s = Automaton;
        return Stack(
            Explained("Cams", "One cam, follower and platform each, side by side along the shaft.", cams, add),
            Explained("The box", "Tabs in slots for the sheet's thickness; the followers slide in slots in the top and in a shelf under it.",
                Field("Platforms", Num(10, 120, 1, "0", s.PlatformMm, v => Automaton = Automaton with { PlatformMm = v }), Hint("How wide each round platform is (mm): your figures stand on these.")),
                Field("Crank", Num(10, 120, 1, "0", s.CrankMm, v => Automaton = Automaton with { CrankMm = v }), Hint("From the shaft to the handle (mm).")),
                Field("Handle turns", Pick(["Anticlockwise", "Clockwise"], s.Clockwise ? 1 : 0, v => Automaton = Automaton with { Clockwise = v == 1 }), tip: "Looking at the handle's side of the box."),
                AxleFields(() => Automaton.Axle, a => Automaton = Automaton with { Axle = a }, "Shaft"),
                HoleFields("Cams' holes", () => Automaton.CamHole, h => Automaton = Automaton with { CamHole = h }, "Grips the shaft (usual)", () => Automaton.Axle.SizeMm, HoleFit.Press,
                    "The cams' holes for the shaft, which set its shape and size: D-shaped or square set each cam's timing by themselves; round ones are lined up by hand and glued.")));
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
            var s = JsonSerializer.Deserialize<Saved>(File.ReadAllText(path), AddonJson.Options);
            if (s is null) return;
            for (int i = 0; i < Math.Min(s.Specs.Length, _specs.Length); i++)
                if (MechSpec.FromJson(s.Specs[i]) is { } spec && KindOf(spec) == i) _specs[i] = spec;
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
            File.WriteAllText(path, JsonSerializer.Serialize(s, AddonJson.Options));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>For the screenshots: build now and wait.</summary>
    internal Task BuildNow()
    {
        _debounce.Stop();
        return Rebuild();
    }
}
