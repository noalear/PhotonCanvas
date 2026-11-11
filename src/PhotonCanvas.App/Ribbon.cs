using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using static PhotonCanvas.App.UiKit;

namespace PhotonCanvas.App;

/// <summary>
/// The command ribbon: tabs of labelled groups. Big buttons (icon over label) for the
/// main command of a group, stacked small ones (icon beside label) for its relatives.
/// Every button can say when it applies; <see cref="Refresh"/> re-checks them all.
/// When a tab doesn't fit, small buttons drop their labels first, then big ones
/// (the tooltips still say what each does).
/// Double-click a tab to fold the ribbon away (and again to bring it back).
/// </summary>
public sealed class Ribbon : UserControl
{
    readonly StackPanel _tabStrip = new() { Orientation = Orientation.Horizontal, Spacing = 0 };
    readonly ContentControl _body = new();
    readonly Border _bodyBorder;
    readonly DockPanel _bodyDock = new();
    readonly ScrollViewer _scroll = new()
    {
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
    };
    bool _fitQueued;

    void FitLater()
    {
        if (_fitQueued) return;
        _fitQueued = true;
        Avalonia.Threading.Dispatcher.UIThread.Post(() => { _fitQueued = false; Fit(); }, Avalonia.Threading.DispatcherPriority.Background);
    }

    /// <summary>
    /// Show as many labels as fit. Labels go a group at a time from the right: first the small
    /// buttons' labels, then (if it still doesn't fit) the big buttons' labels.
    /// </summary>
    void Fit()
    {
        if (_current is not { } tab) return;
        double avail = _scroll.Bounds.Width;
        if (avail <= 1 || !tab.Panel.IsAttachedToVisualTree()) return;
        // Each tab's width at each level is measured once; after that, fitting only picks a level.
        if (tab.Widths is null)
        {
            var widths = new double[tab.Levels + 1];
            for (int level = 0; level <= tab.Levels; level++)
            {
                tab.SetLevel(level);
                // Hidden labels only invalidate their own parent; re-measure the whole tab.
                foreach (var v in tab.Panel.GetVisualDescendants().OfType<Layoutable>()) v.InvalidateMeasure();
                tab.Panel.InvalidateMeasure();
                tab.Panel.Measure(Size.Infinity);
                widths[level] = tab.Panel.DesiredSize.Width;
            }
            if (widths[0] > 1) tab.Widths = widths;
            tab.SetLevel(-1);
            if (tab.Widths is null) return;
        }
        int pick = Array.FindIndex(tab.Widths, w => w <= avail);
        tab.SetLevel(pick < 0 ? tab.Levels : pick);
    }
    readonly DockPanel _header = new();
    readonly List<RibbonTab> _tabs = [];
    readonly List<(Control Control, Func<bool> Enabled)> _conditional = [];
    readonly List<(ToggleButton Button, Func<bool> Checked)> _checked = [];
    readonly List<(Control Control, Func<bool> Visible)> _shown = [];
    RibbonTab? _current;
    bool _folded;

    public Ribbon()
    {
        _bodyBorder = new Border
        {
            Background = Brush("Bg2"),
            BorderBrush = Brush("Line"),
            BorderThickness = new(0, 1, 0, 1),
            Child = _bodyDock,
        };
        _bodyDock.Children.Add(_scroll);
        _scroll.SizeChanged += (_, _) => FitLater();
        // Also when the space changes (the machine group grows on connecting).
        _scroll.ScrollChanged += (_, e) => { if (e.ViewportDelta.X != 0) FitLater(); };
        _scroll.AttachedToVisualTree += (_, _) => FitLater();
        DockPanel.SetDock(_tabStrip, Dock.Left);
        _header.Children.Add(_tabStrip);
        var root = new DockPanel { Background = Brush("Bg1") };
        DockPanel.SetDock(_header, Dock.Top);
        root.Children.Add(_header);
        root.Children.Add(_bodyBorder);
        Content = root;
    }

    /// <summary>Something before the tabs (the File button).</summary>
    public void SetLead(Control c)
    {
        c.Margin = new(6, 3, 8, 3);
        DockPanel.SetDock(c, Dock.Left);
        _header.Children.Insert(0, c);
    }

    /// <summary>Something always shown at the right end of the ribbon, whichever tab is open (the machine).</summary>
    public void SetBodyTrail(Control c)
    {
        var holder = new Border { BorderBrush = Brush("Line"), BorderThickness = new(1, 0, 0, 0), Margin = new(0, 4), Padding = new(8, 0, 6, 0), Child = c };
        DockPanel.SetDock(holder, Dock.Right);
        _bodyDock.Children.Insert(0, holder);
    }

    /// <summary>Controls at the right end of the tab row (undo, redo, settings, help).</summary>
    public void SetTrail(params Control[] controls)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 0, 8, 0) };
        foreach (var c in controls) p.Children.Add(c);
        _header.Children.Add(p);
    }

    public RibbonTab Tab(string name, string? tip = null)
    {
        var header = new ToggleButton { Content = name, Classes = { "rbtab" } };
        if (tip is not null) ToolTip.SetTip(header, tip);
        var tab = new RibbonTab(this, name, header);
        header.Click += (_, _) =>
        {
            _wanted = null;
            if (_folded) Fold(false);
            Select(tab);
        };
        header.DoubleTapped += (_, _) => Fold(!_folded);
        _tabStrip.Children.Add(header);
        _tabs.Add(tab);
        if (_current is null) Select(tab);
        return tab;
    }

    /// <summary>
    /// A tab kept for add-ons' buttons (Designer): its place in the row is fixed here, its header
    /// shows only while an add-on has a button on it, and its groups always come in
    /// <paramref name="groups"/> order, whichever add-on starts first.
    /// </summary>
    public RibbonTab AddonTab(string name, string tip, params string[] groups)
    {
        var tab = Tab(name, tip);
        tab.Extra = true;
        tab.Kept = true;
        tab.GroupOrder = groups;
        tab.Header.IsVisible = false;
        return tab;
    }

    public void Select(string name)
    {
        if (_tabs.FirstOrDefault(t => t.Name == name) is not { } t) return;
        // Designer before its add-ons have started: opened once a button arrives on it.
        if (t.Kept && !t.Header.IsVisible)
        {
            _wanted = t;
            return;
        }
        Select(t);
    }

    /// <summary>A hidden kept tab to open as soon as it shows (the one open when the program last closed).</summary>
    RibbonTab? _wanted;

    /// <summary>The user switched tabs.</summary>
    public event Action<string>? TabSelected;

    void Select(RibbonTab tab)
    {
        if (_current is not null && _current != tab) TabSelected?.Invoke(tab.Name);
        _current = tab;
        _scroll.Content ??= _body;
        foreach (var t in _tabs) t.Header.IsChecked = t == tab;
        _body.Content = tab.Panel;
        FitLater();
    }

    void Fold(bool folded)
    {
        _folded = folded;
        _bodyBorder.IsVisible = !folded;
    }

    /// <summary>Mark a tab as relevant to the selection (a picture is selected: the Picture tab lights up).</summary>
    public void Highlight(string name, bool on)
    {
        if (_tabs.FirstOrDefault(t => t.Name == name) is { } t) t.Header.Classes.Set("context", on);
    }

    /// <summary>Re-check which commands apply and which toggles are on.</summary>
    public void Refresh()
    {
        foreach (var (c, enabled) in _conditional) c.IsEnabled = enabled();
        foreach (var (b, on) in _checked) b.IsChecked = on();
        bool changed = false;
        foreach (var (c, visible) in _shown)
        {
            bool v = visible();
            if (c.IsVisible == v) continue;
            c.IsVisible = v;
            changed = true;
        }
        // Small buttons are stacked again when some were shown or hidden: only the ones showing take a place.
        foreach (var t in _tabs)
            foreach (var g in t.Groups)
                if (g.Repack()) changed = true;
        // An add-on's group goes when none of its buttons are showing (they may hide themselves).
        foreach (var t in _tabs)
            foreach (var g in t.Groups)
            {
                if (!g.Extra) continue;
                bool any = g.AnyShown;
                if (g.Root.IsVisible == any) continue;
                g.Root.IsVisible = any;
                changed = true;
            }
        // A kept tab (Designer) shows while any of its groups do.
        foreach (var t in _tabs)
        {
            if (!t.Kept) continue;
            bool any = t.Groups.Any(g => g.Root.IsVisible);
            if (t.Header.IsVisible != any)
            {
                t.Header.IsVisible = any;
                changed = true;
            }
            if (!any && _current == t) SelectFirstShown();
        }
        if (!changed) return;
        // Widths change with what's shown: measure the tabs again.
        foreach (var t in _tabs)
        {
            t.Widths = null;
            t.SetLevel(-1);
        }
        FitLater();
    }

    /// <summary>Show <paramref name="c"/> only while <paramref name="visible"/> says so (checked on <see cref="Refresh"/>).</summary>
    public T ShowWhen<T>(T c, Func<bool> visible) where T : Control
    {
        _shown.Add((c, visible));
        c.IsVisible = visible();
        return c;
    }

    readonly Dictionary<string, Control> _named = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Every command by "Tab/Label" (icon-only ones by their tooltip), for the help and the tutorials to point at.</summary>
    public IReadOnlyDictionary<string, Control> Commands => _named;

    internal void Register(string tab, string label, Control c) => _named.TryAdd($"{tab}/{label}", c);

    readonly Dictionary<string, Control> _aliases = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// A command that isn't a button of its own but is reached through one ("Tests/Cut test" is picked in the
    /// Tests window, opened by the Tests button): <see cref="Show"/> finds that button for it.
    /// </summary>
    internal void Alias(string tab, string label, Control button) => _aliases[$"{tab}/{label}"] = button;

    internal void Unalias(string tab, string label) => _aliases.Remove($"{tab}/{label}");

    /// <summary>Commands reached through another button (<see cref="Alias"/>), by "Tab/Label".</summary>
    public IReadOnlyDictionary<string, Control> Aliases => _aliases;

    /// <summary>A command's button, after switching to its tab so it can be seen; null if there's no such command.</summary>
    public Control? Show(string tab, string label)
    {
        if (!_named.TryGetValue($"{tab}/{label}", out var c) && !_aliases.TryGetValue($"{tab}/{label}", out c)) return null;
        if (_folded) Fold(false);
        // The tab the button is on (an alias's button is on another tab than the one it's named by).
        if (_tabs.FirstOrDefault(t => Within(c, t.Panel)) is { } on) Select(on.Name);
        else Select(tab);
        return c;
    }

    static bool Within(StyledElement e, Control panel)
    {
        for (var p = e.Parent; p is not null; p = p.Parent) if (p == panel) return true;
        return false;
    }

    /// <summary>A tab's header button.</summary>
    public Control? TabHeader(string name) => _tabs.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase))?.Header;

    /// <summary>The names of the tabs showing, in order.</summary>
    public IEnumerable<string> TabNames => _tabs.Where(t => t.Header.IsVisible).Select(t => t.Name);

    internal void Track(Control c, Func<bool>? enabled)
    {
        if (enabled is not null) _conditional.Add((c, enabled));
    }

    internal void TrackChecked(ToggleButton b, Func<bool>? isChecked)
    {
        if (isChecked is not null) _checked.Add((b, isChecked));
    }

    /// <summary>Forget a control's enabled/checked/visible tests and its name (it is being taken away).</summary>
    internal void Untrack(Control c)
    {
        _conditional.RemoveAll(x => x.Control == c);
        _checked.RemoveAll(x => x.Button == c);
        _shown.RemoveAll(x => x.Control == c);
        foreach (var key in _named.Where(kv => kv.Value == c).Select(kv => kv.Key).ToList()) _named.Remove(key);
    }

    /// <summary>The tab with this name, made (at the end) if there isn't one.</summary>
    internal RibbonTab TabFor(string name) =>
        _tabs.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)) is { } t ? t : Tab(name).With(t => t.Extra = true);

    /// <summary>Widths change with what's in a tab: measure it again.</summary>
    internal void Remeasure(RibbonTab tab)
    {
        tab.Widths = null;
        tab.SetLevel(-1);
        FitLater();
    }

    /// <summary>Take away a tab made for an add-on, once nothing is left on it.</summary>
    internal void DropIfEmpty(RibbonTab tab)
    {
        if (!tab.Extra || tab.Groups.Count > 0) return;
        if (tab.Kept)
        {
            // Stays in its place for the next add-on, just hidden.
            tab.Header.IsVisible = false;
            if (_current == tab) SelectFirstShown();
            return;
        }
        _tabs.Remove(tab);
        _tabStrip.Children.Remove(tab.Header);
        if (_current == tab)
        {
            _current = null;
            if (_tabs.Count > 0) Select(_tabs[0]);
            else _body.Content = null;
        }
    }

    void SelectFirstShown()
    {
        if (_tabs.FirstOrDefault(t => t.Header.IsVisible) is { } first) Select(first);
    }

    /// <summary>
    /// Add a button for an add-on to a tab's group (both made if there aren't any); dispose the result
    /// to take it away again (with the group and tab, if they were made for it and are now empty).
    /// </summary>
    public IDisposable AddExtra(string tabName, string groupTitle, Func<RibbonGroup, Control> add, string? groupAfter = null, int order = 0)
    {
        var tab = TabFor(tabName);
        tab.Header.IsVisible = true;
        if (_wanted == tab)
        {
            _wanted = null;
            Select(tab);
        }
        var group = tab.GroupFor(groupTitle, groupAfter);
        var c = add(group);
        group.PlaceExtra(c, order);
        Remeasure(tab);
        return new Addons.AddonUndo(() =>
        {
            Untrack(c);
            group.Remove(c);
            if (group.IsEmpty && group.Extra) tab.RemoveGroup(group);
            Remeasure(tab);
            DropIfEmpty(tab);
        });
    }
}

public sealed class RibbonTab
{
    readonly Ribbon _ribbon;
    internal string Name { get; }
    internal ToggleButton Header { get; }
    internal StackPanel Panel { get; } = new() { Orientation = Orientation.Horizontal, Margin = new(4, 0) };
    internal List<RibbonGroup> Groups { get; } = [];

    /// <summary>How many steps of label-dropping there are (two per group).</summary>
    internal int Levels => Groups.Count * 2;

    internal double[]? Widths { get; set; }
    int _level = -1;

    /// <summary>Made for an add-on (taken away again when it's empty).</summary>
    internal bool Extra { get; set; }

    /// <summary>Kept in its place when empty, only hidden (<see cref="Ribbon.AddonTab"/>).</summary>
    internal bool Kept { get; set; }

    /// <summary>The order its groups go in, when they are named here (<see cref="Ribbon.AddonTab"/>, <see cref="Order"/>).</summary>
    internal string[] GroupOrder { get; set; } = [];

    /// <summary>
    /// Where groups go, the program's and add-ons', whichever comes first: a group named here that an
    /// add-on makes goes before the first group that comes after it.
    /// </summary>
    public RibbonTab Order(params string[] groups)
    {
        GroupOrder = groups;
        return this;
    }

    /// <summary>The group with this title, made (at the end) if there isn't one.</summary>
    internal RibbonGroup GroupFor(string title, string? after = null)
    {
        if (Groups.FirstOrDefault(g => string.Equals(g.Title, title, StringComparison.OrdinalIgnoreCase)) is { } had) return had;
        var g = Group(title).With(g => g.Extra = true);
        // A tab with a set group order: before the first group that comes after it.
        int rank = Array.FindIndex(GroupOrder, x => string.Equals(x, title, StringComparison.OrdinalIgnoreCase));
        if (rank >= 0)
        {
            int before = Groups.FindIndex(x => x != g && Array.FindIndex(GroupOrder, o => string.Equals(o, x.Title, StringComparison.OrdinalIgnoreCase)) is var r && (r < 0 || r > rank));
            if (before >= 0)
            {
                Groups.Remove(g);
                Groups.Insert(before, g);
                Panel.Children.Remove(g.Root);
                Panel.Children.Insert(Panel.Children.IndexOf(Groups[before + 1].Root), g.Root);
            }
            return g;
        }
        // Where it belongs, if asked: just after that group.
        // "Tests|Materials": after the first of these there is.
        int at = -1;
        foreach (var name in (after ?? "").Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            if ((at = Groups.FindIndex(x => string.Equals(x.Title, name, StringComparison.OrdinalIgnoreCase))) >= 0) break;
        if (at >= 0 && at < Groups.Count - 2)
        {
            Groups.Remove(g);
            Groups.Insert(at + 1, g);
            Panel.Children.Remove(g.Root);
            Panel.Children.Insert(Panel.Children.IndexOf(Groups[at].Root) + 1, g.Root);
        }
        return g;
    }

    internal void RemoveGroup(RibbonGroup g)
    {
        Groups.Remove(g);
        Panel.Children.Remove(g.Root);
    }

    /// <summary>
    /// 0 = every label. 1…n = n groups lose their small-button labels; n+1…2n = they lose their
    /// big-button labels too. Groups give up their labels in <see cref="LabelsGoFirst"/> order, then
    /// from the right. -1 forgets the current level.
    /// </summary>
    internal void SetLevel(int level)
    {
        if (level == _level) return;
        _level = level;
        if (level < 0) return;
        int n = Groups.Count;
        int smallOff = Math.Min(level, n), largeOff = Math.Max(0, level - n);
        var order = DropOrder();
        for (int i = 0; i < n; i++)
        {
            int rank = order.IndexOf(Groups[i]);
            Groups[i].ShowLabels(small: rank >= smallOff, large: rank >= largeOff);
        }
    }

    string[] _labelsFirst = [];

    /// <summary>
    /// The groups whose labels go first when the tab doesn't fit, because their icons say enough
    /// (the shapes in Draw): the rest keep theirs longer, giving way from the right as before.
    /// </summary>
    public RibbonTab LabelsGoFirst(params string[] groups)
    {
        _labelsFirst = groups;
        Widths = null;
        return this;
    }

    List<RibbonGroup> DropOrder()
    {
        var first = _labelsFirst.Select(t => Groups.FirstOrDefault(g => string.Equals(g.Title, t, StringComparison.OrdinalIgnoreCase)))
            .OfType<RibbonGroup>().ToList();
        return [.. first, .. Enumerable.Reverse(Groups).Where(g => !first.Contains(g))];
    }

    internal RibbonTab(Ribbon ribbon, string name, ToggleButton header)
    {
        _ribbon = ribbon;
        Name = name;
        Header = header;
    }

    public RibbonGroup Group(string title, Func<bool>? visible = null)
    {
        var g = new RibbonGroup(_ribbon, this, title);
        Panel.Children.Add(g.Root);
        Groups.Add(g);
        if (visible is not null) _ribbon.ShowWhen(g.Root, visible);
        return g;
    }
}

/// <summary>One labelled group of commands.</summary>
public sealed class RibbonGroup
{
    readonly Ribbon _ribbon;
    readonly StackPanel _items = new() { Orientation = Orientation.Horizontal, Spacing = 1, VerticalAlignment = VerticalAlignment.Top };
    internal Control Root { get; }

    enum Kind { Large, Small, Block }

    /// <summary>
    /// One button (or grid, or custom control) of the group. <see cref="Order"/> is set for an add-on's
    /// (they go after the program's own, by order and then arrival); <see cref="NewColumn"/> when the
    /// program asked for a fresh column of small buttons before it (<see cref="Break"/>).
    /// </summary>
    sealed class Entry(Control control, Kind kind, bool newColumn)
    {
        public Control Control { get; } = control;
        public Kind Kind { get; } = kind;
        public bool NewColumn { get; } = newColumn;
        public int? Order { get; set; }
        public int Arrived { get; set; }
        /// <summary>Stays after the add-ons' buttons too (a "More…" menu ends its group).</summary>
        public bool Last { get; set; }
    }

    readonly List<Entry> _entries = [];
    readonly List<StackPanel> _columns = [];
    bool _breakNext;
    string _packed = "";

    readonly RibbonTab _tab;
    internal string Title { get; }
    /// <summary>Made for an add-on (taken away again when it's empty).</summary>
    internal bool Extra { get; set; }
    internal bool IsEmpty => _entries.Count == 0;
    /// <summary>Is any of its buttons showing.</summary>
    internal bool AnyShown => _entries.Any(e => e.Control.IsVisible);
    readonly List<TextBlock> _smallLabels = [];
    readonly List<(Control Button, TextBlock Label)> _largeLabels = [];

    internal void ShowLabels(bool small, bool large)
    {
        foreach (var t in _smallLabels) t.IsVisible = small;
        foreach (var (b, t) in _largeLabels)
        {
            t.IsVisible = large;
            b.MinWidth = large ? 44 : 34;
        }
    }

    internal RibbonGroup(Ribbon ribbon, RibbonTab tab, string title)
    {
        _ribbon = ribbon;
        _tab = tab;
        Title = title;
        var grid = new Grid { RowDefinitions = new("*,Auto"), Height = 84, Margin = new(4, 4, 4, 2) };
        grid.Children.Add(_items);
        var label = new TextBlock
        {
            Text = title, FontSize = 10.5, Foreground = Brush("TextMuted"),
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new(0, 2, 0, 0),
        };
        Grid.SetRow(label, 1);
        grid.Children.Add(label);
        Root = new Border
        {
            BorderBrush = Brush("Line"),
            BorderThickness = new(0, 0, 1, 0),
            Padding = new(4, 0),
            Margin = new(0, 4),
            Child = grid,
        };
    }

    Control LargeContent(Control button, string label, string icon)
    {
        var c = (StackPanel)LargeContent(label, icon);
        _largeLabels.Add((button, (TextBlock)c.Children[1]));
        return c;
    }

    Control SmallContent(string label, string icon, bool track)
    {
        var c = (StackPanel)SmallContent(label, icon);
        if (track) _smallLabels.Add((TextBlock)c.Children[1]);
        return c;
    }

    static Control LargeContent(string label, string icon) => new StackPanel
    {
        Spacing = 3,
        HorizontalAlignment = HorizontalAlignment.Center,
        Children =
        {
            new PathIcon { Data = IconOrPath(icon), Width = 22, Height = 22, HorizontalAlignment = HorizontalAlignment.Center },
            new TextBlock { Text = label, FontSize = 11, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 66, HorizontalAlignment = HorizontalAlignment.Center },
        },
    };

    static Control SmallContent(string label, string icon) => new StackPanel
    {
        Orientation = Orientation.Horizontal,
        Spacing = 6,
        Children =
        {
            new PathIcon { Data = IconOrPath(icon), Width = 14, Height = 14, VerticalAlignment = VerticalAlignment.Center },
            new TextBlock { Text = label, FontSize = 12, VerticalAlignment = VerticalAlignment.Center },
        },
    };

    T Setup<T>(T b, string? tip, Func<bool>? enabled) where T : Control
    {
        if (tip is not null) ToolTip.SetTip(b, tip);
        _ribbon.Track(b, enabled);
        return b;
    }

    void AddLarge(Control b)
    {
        b.MinWidth = 44;
        if (b is TemplatedControl tc) tc.Padding = new(3, 3);
        b.Height = 62;
        b.VerticalAlignment = VerticalAlignment.Top;
        Add(b, Kind.Large);
    }

    void AddSmall(Control b)
    {
        b.Height = 21;
        b.HorizontalAlignment = HorizontalAlignment.Stretch;
        if (b is ContentControl cc) cc.HorizontalContentAlignment = HorizontalAlignment.Left;
        Add(b, Kind.Small);
    }

    void Add(Control c, Kind kind)
    {
        _entries.Add(new Entry(c, kind, kind == Kind.Small && _breakNext));
        _breakNext = false;
        Pack();
    }

    /// <summary>Which buttons are showing (one 1 or 0 each), to tell when they must be stacked again.</summary>
    string Shape() => string.Create(_entries.Count, _entries, (span, es) => { for (int i = 0; i < span.Length; i++) span[i] = es[i].Control.IsVisible ? '1' : '0'; });

    /// <summary>Pack again if buttons have been shown or hidden since the last time (true if it did).</summary>
    internal bool Repack()
    {
        if (Shape() == _packed) return false;
        Pack();
        return true;
    }

    /// <summary>
    /// Lay the buttons out: big ones side by side, small ones stacked in columns of up to three, the
    /// program's and the add-ons' alike. A run of small buttons (between big ones, or where the program
    /// asked for a <see cref="Break"/>) is spread evenly over as few columns as hold it: 4 go 2 + 2 and
    /// 7 go 3 + 2 + 2, so no button is left alone in a column of its own. Hidden buttons don't take a
    /// place, so nothing leaves a gap (Scan the part only shows with the rotary, for one).
    /// </summary>
    void Pack()
    {
        foreach (var col in _columns) col.Children.Clear();
        _columns.Clear();
        _items.Children.Clear();
        var own = _entries.Where(e => e.Order is null && !e.Last);
        var extras = _entries.Where(e => e.Order is not null).OrderBy(e => e.Order).ThenBy(e => e.Arrived);
        var last = _entries.Where(e => e.Order is null && e.Last);
        var run = new List<Entry>();
        foreach (var e in own.Concat(extras).Concat(last))
        {
            if (e.Kind == Kind.Small && !(e.NewColumn && run.Count > 0))
            {
                run.Add(e);
                continue;
            }
            PackRun(run);
            if (e.Kind == Kind.Small) run.Add(e);
            else _items.Children.Add(e.Control);
        }
        PackRun(run);
        foreach (var col in _columns) col.IsVisible = col.Children.Any(c => c.IsVisible);
        _packed = Shape();
    }

    /// <summary>Stack a run of small buttons into evenly filled columns (the first ones take any extra), then empty it.</summary>
    void PackRun(List<Entry> run)
    {
        if (run.Count == 0) return;
        int shown = run.Count(e => e.Control.IsVisible);
        int columns = Math.Max(1, (shown + 2) / 3);
        StackPanel? column = null;
        int index = -1, inColumn = 0, size = 0;
        foreach (var e in run)
        {
            bool visible = e.Control.IsVisible;
            if (column is null || (visible && inColumn >= size))
            {
                index++;
                size = shown / columns + (index < shown % columns ? 1 : 0);
                column = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Top };
                _columns.Add(column);
                _items.Children.Add(column);
                inColumn = 0;
            }
            column.Children.Add(e.Control);
            if (visible) inColumn++;
        }
        run.Clear();
    }

    public Button Large(string label, string icon, Action action, string? tip = null, Func<bool>? enabled = null)
    {
        var b = new Button { Classes = { "rb" } };
        b.Content = LargeContent(b, label, icon);
        b.Click += (_, _) => action();
        AddLarge(b);
        _ribbon.Register(_tab.Name, label, b);
        return Setup(b, tip, enabled);
    }

    public ToggleButton LargeToggle(string label, string icon, Action<bool> changed, string? tip = null, Func<bool>? enabled = null, Func<bool>? isChecked = null)
    {
        var b = new ToggleButton { Classes = { "rb" } };
        b.Content = LargeContent(b, label, icon);
        b.Click += (_, _) => changed(b.IsChecked == true);
        AddLarge(b);
        _ribbon.Register(_tab.Name, label, b);
        _ribbon.TrackChecked(b, isChecked);
        return Setup(b, tip, enabled);
    }

    public Button Small(string label, string icon, Action action, string? tip = null, Func<bool>? enabled = null)
    {
        var b = new Button { Classes = { "rb" }, Content = SmallContent(label, icon, true), Padding = new(4, 1) };
        b.Click += (_, _) => action();
        AddSmall(b);
        _ribbon.Register(_tab.Name, label, b);
        return Setup(b, tip, enabled);
    }

    public ToggleButton SmallToggle(string label, string icon, Action<bool> changed, string? tip = null, Func<bool>? enabled = null, Func<bool>? isChecked = null)
    {
        var b = new ToggleButton { Classes = { "rb" }, Content = SmallContent(label, icon, true), Padding = new(4, 1) };
        b.Click += (_, _) => changed(b.IsChecked == true);
        AddSmall(b);
        _ribbon.Register(_tab.Name, label, b);
        _ribbon.TrackChecked(b, isChecked);
        return Setup(b, tip, enabled);
    }

    /// <summary>A grid of icon-only buttons, <paramref name="perRow"/> to a row (align, distribute…).</summary>
    public void Icons(int perRow, params (string Icon, string Tip, Action Action, Func<bool>? Enabled)[] buttons)
    {
        var grid = new UniformGrid { Columns = perRow, VerticalAlignment = VerticalAlignment.Top };
        foreach (var (icon, tip, action, enabled) in buttons)
        {
            var b = new Button
            {
                Classes = { "rb" }, Width = 28, Height = 26, Padding = new(0),
                HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
                Content = new PathIcon { Data = Icon(icon), Width = 16, Height = 16 },
            };
            b.Click += (_, _) => action();
            grid.Children.Add(Setup(b, tip, enabled));
            _ribbon.Register(_tab.Name, tip, b);
        }
        Add(grid, Kind.Block);
    }

    /// <summary>Any control (a slider, a number box), stacked like a small button.</summary>
    public T Custom<T>(T c, Func<bool>? enabled = null) where T : Control
    {
        c.VerticalAlignment = VerticalAlignment.Top;
        Add(c, Kind.Block);
        _ribbon.Track(c, enabled);
        return c;
    }

    /// <summary>Start a new column of small buttons.</summary>
    public void Break() => _breakNext = true;

    /// <summary>Keep this button at the end of the group, after any add-on's (the group's "More…" menu).</summary>
    public T KeepLast<T>(T c) where T : Control
    {
        if (_entries.FirstOrDefault(e => e.Control == c) is { } entry)
        {
            entry.Last = true;
            Pack();
        }
        return c;
    }

    int _arrivals;

    /// <summary>
    /// An add-on's button just added goes among the other add-ons' by <paramref name="order"/>, then by
    /// arrival, after the program's own: so the order doesn't depend on which add-on started first.
    /// Small ones fill up the group's last column before starting a new one.
    /// </summary>
    internal void PlaceExtra(Control c, int order)
    {
        if (_entries.FirstOrDefault(e => e.Control == c) is not { } entry) return;
        entry.Order = order;
        entry.Arrived = _arrivals++;
        Pack();
    }

    static bool Within(StyledElement e, Control c)
    {
        for (var p = e.Parent; p is not null; p = p.Parent) if (p == c) return true;
        return false;
    }

    /// <summary>Take a button (or custom control) away again, and its column if that's left empty.</summary>
    internal void Remove(Control c)
    {
        // By the logical parents: the visual ones only exist once the tab has been shown, and a label
        // left here would keep the add-on (its button's click and Checked) in memory.
        _smallLabels.RemoveAll(l => Within(l, c));
        _largeLabels.RemoveAll(x => x.Button == c);
        // Its click handler belongs to the add-on: nothing here may keep hold of it.
        _entries.RemoveAll(e => e.Control == c);
        Pack();
    }
}
