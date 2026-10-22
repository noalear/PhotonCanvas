using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using static PhotonCanvas.App.UiKit;

namespace PhotonCanvas.App;

/// <summary>Where a side panel is: which side, its place there, folded to its title, or floating in its own window.</summary>
public sealed record PanelPlace
{
    public string Side { get; init; } = "Left";
    public int Order { get; init; }
    public bool Folded { get; init; }
    public bool Floating { get; init; }
    /// <summary>A floating panel's window (screen position and size).</summary>
    public double X { get; init; } = double.NaN;
    public double Y { get; init; } = double.NaN;
    public double W { get; init; } = 320;
    public double H { get; init; } = 520;
}

/// <summary>
/// Movable side panels: each has a slim title bar with buttons to move it to the other side, float
/// it in a window of its own (closing the window docks it back) or fold it to its title. Panels on
/// a side share its height; a side with none closes up. Where everything is, and how wide the
/// sides are, is kept in the settings.
/// </summary>
public sealed class Docking
{
    sealed class Panel(string id, string title, Control content, PanelPlace home)
    {
        public readonly string Id = id, Title = title;
        public readonly Control Content = content;
        public readonly PanelPlace Home = home;
        public Window? Window;
    }

    readonly Window _owner;
    readonly AppSettings _settings;
    readonly Grid _grid;
    readonly ContentControl _left, _right;
    readonly GridSplitter _leftSplit, _rightSplit;
    readonly List<Panel> _panels = [];

    public Docking(Window owner, AppSettings settings, Grid grid, ContentControl left, ContentControl right, GridSplitter leftSplit, GridSplitter rightSplit)
    {
        _owner = owner;
        _settings = settings;
        _grid = grid;
        _left = left;
        _right = right;
        _leftSplit = leftSplit;
        _rightSplit = rightSplit;
        // The sides' widths, kept when dragged.
        leftSplit.DragCompleted += (_, _) => { _settings.LeftDockWidth = _grid.ColumnDefinitions[0].ActualWidth; _settings.Save(); };
        rightSplit.DragCompleted += (_, _) => { _settings.RightDockWidth = _grid.ColumnDefinitions[4].ActualWidth; _settings.Save(); };
        owner.Closing += (_, _) => { foreach (var p in _panels) p.Window?.Close(); };
    }

    /// <summary>A panel and where it goes by default.</summary>
    public void Add(string id, string title, Control content, PanelPlace home) => _panels.Add(new Panel(id, title, content, home));

    PanelPlace Place(Panel p) => _settings.PanelLayout.TryGetValue(p.Id, out var place) ? place : p.Home;

    void Set(Panel p, PanelPlace place)
    {
        _settings.PanelLayout[p.Id] = place;
        _settings.Save();
        Layout();
    }

    /// <summary>A panel by its title, unfolded if it was folded (for the tutorials to point into); null if there's none.</summary>
    public Control? Show(string title)
    {
        if (_panels.FirstOrDefault(p => string.Equals(p.Title, title, StringComparison.OrdinalIgnoreCase)) is not { } panel) return null;
        if (Place(panel) is { Folded: true } place) Set(panel, place with { Folded = false });
        panel.Window?.Activate();
        return panel.Content;
    }

    /// <summary>Back to where everything started.</summary>
    public void Reset()
    {
        foreach (var p in _panels) p.Window?.Close();
        _settings.PanelLayout.Clear();
        _settings.LeftDockWidth = _settings.RightDockWidth = 0;
        _settings.Save();
        Layout();
    }

    /// <summary>The panel's title bar and contents in their places.</summary>
    public void Layout()
    {
        foreach (var p in _panels)
            if (p.Content.Parent is ContentControl cc) cc.Content = null;
            else if (p.Content.Parent is Decorator d) d.Child = null;
            else if (p.Content.Parent is Avalonia.Controls.Panel pp) pp.Children.Remove(p.Content);
        _left.Content = Side("Left");
        _right.Content = Side("Right");
        bool anyLeft = _panels.Any(p => Place(p) is { Floating: false, Side: "Left" }), anyRight = _panels.Any(p => Place(p) is { Floating: false, Side: "Right" });
        _grid.ColumnDefinitions[0].Width = anyLeft ? new GridLength(_settings.LeftDockWidth > 120 ? _settings.LeftDockWidth : 280) : new GridLength(0);
        _grid.ColumnDefinitions[4].Width = anyRight ? new GridLength(_settings.RightDockWidth > 120 ? _settings.RightDockWidth : 300) : new GridLength(0);
        _leftSplit.IsVisible = anyLeft;
        _rightSplit.IsVisible = anyRight;
        foreach (var p in _panels)
        {
            var place = Place(p);
            if (place.Floating) Float(p, place);
            else p.Window?.Close();
        }
    }

    Control Side(string side)
    {
        var here = _panels.Where(p => Place(p) is { Floating: false } pl && pl.Side == side).OrderBy(p => Place(p).Order).ToList();
        var g = new Grid { Background = Brush("Bg1") };
        int row = 0;
        for (int i = 0; i < here.Count; i++)
        {
            var p = here[i];
            bool folded = Place(p).Folded;
            if (i > 0)
            {
                // Between two open panels: drag to share the height differently.
                bool split = !folded && !Place(here[i - 1]).Folded;
                g.RowDefinitions.Add(new RowDefinition(split ? new GridLength(4) : new GridLength(1)));
                var line = split ? new GridSplitter { Background = Brush("Line"), ResizeDirection = GridResizeDirection.Rows } : (Control)new Border { Background = Brush("Line") };
                Grid.SetRow(line, row++);
                g.Children.Add(line);
            }
            g.RowDefinitions.Add(new RowDefinition(folded ? GridLength.Auto : new GridLength(1, GridUnitType.Star)));
            var frame = Frame(p, folded);
            Grid.SetRow(frame, row++);
            g.Children.Add(frame);
        }
        return g;
    }

    Control Frame(Panel p, bool folded)
    {
        var place = Place(p);
        Button Tool(string glyph, string tip, Action click)
        {
            var b = new Button { Content = glyph, Classes = { "icon" }, Padding = new(5, 0), MinHeight = 20, FontSize = 12 };
            ToolTip.SetTip(b, tip);
            b.Click += (_, _) => click();
            return b;
        }
        string other = place.Side == "Left" ? "Right" : "Left";
        var bar = new DockPanel { Margin = new(8, 3, 4, 3) };
        var tools = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 2,
            Children =
            {
                Tool(place.Side == "Left" ? "→" : "←", $"Move to the {other.ToLowerInvariant()} side", () => Set(p, place with { Side = other, Order = 99, Folded = false })),
                Tool("⧉", "Float in its own window (close the window to dock it back)", () => Set(p, place with { Floating = true })),
                Tool(folded ? "▸" : "▾", folded ? "Open" : "Fold to its title", () => Set(p, place with { Folded = !folded })),
            },
        };
        DockPanel.SetDock(tools, Dock.Right);
        bar.Children.Add(tools);
        bar.Children.Add(new TextBlock { Text = p.Title.ToUpperInvariant(), FontSize = 10.5, LetterSpacing = 0.6, Foreground = Brush("TextMuted"), VerticalAlignment = VerticalAlignment.Center });
        var dock = new DockPanel();
        var head = new Border { Background = Brush("Bg0"), Child = bar };
        DockPanel.SetDock(head, Dock.Top);
        dock.Children.Add(head);
        if (!folded) dock.Children.Add(new Border { Padding = new(10, 6, 6, 0), Child = p.Content });
        return dock;
    }

    void Float(Panel p, PanelPlace place)
    {
        if (p.Window is { } open)
        {
            if (open.Content is Border b && b.Child is null) b.Child = p.Content;
            return;
        }
        var w = new Window
        {
            Title = p.Title, Width = place.W, Height = place.H, Background = Brush("Bg1"),
            Content = new Border { Padding = new(10), Child = p.Content },
            ShowInTaskbar = false,
        };
        if (double.IsFinite(place.X) && double.IsFinite(place.Y)) { w.WindowStartupLocation = WindowStartupLocation.Manual; w.Position = new PixelPoint((int)place.X, (int)place.Y); }
        else w.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        w.Closing += (_, _) =>
        {
            if (w.Content is Border b) b.Child = null; // the content goes back to its side
            p.Window = null;
            if (!_owner.IsVisible) return; // the program is closing: stay floating next time
            _settings.PanelLayout[p.Id] = Place(p) with { Floating = false, X = w.Position.X, Y = w.Position.Y, W = w.Width, H = w.Height };
            _settings.Save();
            Avalonia.Threading.Dispatcher.UIThread.Post(Layout);
        };
        p.Window = w;
        w.Show(_owner);
    }
}
