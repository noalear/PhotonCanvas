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
            if (_folded) Fold(false);
            Select(tab);
        };
        header.DoubleTapped += (_, _) => Fold(!_folded);
        _tabStrip.Children.Add(header);
        _tabs.Add(tab);
        if (_current is null) Select(tab);
        return tab;
    }

    public void Select(string name)
    {
        if (_tabs.FirstOrDefault(t => t.Name == name) is { } t) Select(t);
    }

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
    }

    internal void Track(Control c, Func<bool>? enabled)
    {
        if (enabled is not null) _conditional.Add((c, enabled));
    }

    internal void TrackChecked(ToggleButton b, Func<bool>? isChecked)
    {
        if (isChecked is not null) _checked.Add((b, isChecked));
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

    /// <summary>
    /// 0 = every label. 1…n = the last 1…n groups lose their small-button labels; n+1…2n = the
    /// last groups lose their big-button labels too. -1 forgets the current level.
    /// </summary>
    internal void SetLevel(int level)
    {
        if (level == _level) return;
        _level = level;
        if (level < 0) return;
        int n = Groups.Count;
        int smallOff = Math.Min(level, n), largeOff = Math.Max(0, level - n);
        for (int i = 0; i < n; i++)
            Groups[i].ShowLabels(small: i < n - smallOff, large: i < n - largeOff);
    }

    internal RibbonTab(Ribbon ribbon, string name, ToggleButton header)
    {
        _ribbon = ribbon;
        Name = name;
        Header = header;
    }

    public RibbonGroup Group(string title)
    {
        var g = new RibbonGroup(_ribbon, this, title);
        Panel.Children.Add(g.Root);
        Groups.Add(g);
        return g;
    }
}

/// <summary>One labelled group of commands.</summary>
public sealed class RibbonGroup
{
    readonly Ribbon _ribbon;
    readonly StackPanel _items = new() { Orientation = Orientation.Horizontal, Spacing = 1, VerticalAlignment = VerticalAlignment.Top };
    StackPanel? _column;
    internal Control Root { get; }

    readonly RibbonTab _tab;
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
            new PathIcon { Data = Icon(icon), Width = 22, Height = 22, HorizontalAlignment = HorizontalAlignment.Center },
            new TextBlock { Text = label, FontSize = 11, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 66, HorizontalAlignment = HorizontalAlignment.Center },
        },
    };

    static Control SmallContent(string label, string icon) => new StackPanel
    {
        Orientation = Orientation.Horizontal,
        Spacing = 6,
        Children =
        {
            new PathIcon { Data = Icon(icon), Width = 14, Height = 14, VerticalAlignment = VerticalAlignment.Center },
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
        _column = null;
        b.MinWidth = 44;
        if (b is TemplatedControl tc) tc.Padding = new(3, 3);
        b.Height = 62;
        b.VerticalAlignment = VerticalAlignment.Top;
        _items.Children.Add(b);
    }

    void AddSmall(Control b)
    {
        if (_column is null || _column.Children.Count >= 3)
        {
            _column = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Top };
            _items.Children.Add(_column);
        }
        b.Height = 21;
        b.HorizontalAlignment = HorizontalAlignment.Stretch;
        if (b is ContentControl cc) cc.HorizontalContentAlignment = HorizontalAlignment.Left;
        _column.Children.Add(b);
    }

    public Button Large(string label, string icon, Action action, string? tip = null, Func<bool>? enabled = null)
    {
        var b = new Button { Classes = { "rb" } };
        b.Content = LargeContent(b, label, icon);
        b.Click += (_, _) => action();
        AddLarge(b);
        return Setup(b, tip, enabled);
    }

    public ToggleButton LargeToggle(string label, string icon, Action<bool> changed, string? tip = null, Func<bool>? enabled = null, Func<bool>? isChecked = null)
    {
        var b = new ToggleButton { Classes = { "rb" } };
        b.Content = LargeContent(b, label, icon);
        b.Click += (_, _) => changed(b.IsChecked == true);
        AddLarge(b);
        _ribbon.TrackChecked(b, isChecked);
        return Setup(b, tip, enabled);
    }

    public Button Small(string label, string icon, Action action, string? tip = null, Func<bool>? enabled = null)
    {
        var b = new Button { Classes = { "rb" }, Content = SmallContent(label, icon, true), Padding = new(4, 1) };
        b.Click += (_, _) => action();
        AddSmall(b);
        return Setup(b, tip, enabled);
    }

    public ToggleButton SmallToggle(string label, string icon, Action<bool> changed, string? tip = null, Func<bool>? enabled = null, Func<bool>? isChecked = null)
    {
        var b = new ToggleButton { Classes = { "rb" }, Content = SmallContent(label, icon, true), Padding = new(4, 1) };
        b.Click += (_, _) => changed(b.IsChecked == true);
        AddSmall(b);
        _ribbon.TrackChecked(b, isChecked);
        return Setup(b, tip, enabled);
    }

    /// <summary>A grid of icon-only buttons, <paramref name="perRow"/> to a row (align, distribute…).</summary>
    public void Icons(int perRow, params (string Icon, string Tip, Action Action, Func<bool>? Enabled)[] buttons)
    {
        _column = null;
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
        }
        _items.Children.Add(grid);
    }

    /// <summary>Any control (a slider, a number box), stacked like a small button.</summary>
    public T Custom<T>(T c, Func<bool>? enabled = null) where T : Control
    {
        _column = null;
        c.VerticalAlignment = VerticalAlignment.Top;
        _items.Children.Add(c);
        _ribbon.Track(c, enabled);
        return c;
    }

    /// <summary>Start a new column of small buttons.</summary>
    public void Break() => _column = null;
}
