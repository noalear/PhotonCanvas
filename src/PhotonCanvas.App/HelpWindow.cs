using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using PhotonCanvas.Doc.Help;
using static PhotonCanvas.App.UiKit;

namespace PhotonCanvas.App;

/// <summary>
/// The help guide: the contents on the left (pages and their sections) with a search box, the page on
/// the right, back and forward. Links go to other pages, start tutorials or open web pages.
/// </summary>
public sealed class HelpWindow : Window
{
    static HelpWindow? _open;
    readonly HelpLibrary _lib;
    readonly Action<string> _startTutorial;
    readonly Action _showTutorials;
    readonly ScrollViewer _scroll = new() { HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
    readonly TextBox _search = new() { Watermark = "Search the guide", Margin = new(0, 0, 0, 8) };
    readonly TreeView _tree = new();
    readonly StackPanel _results = new() { Spacing = 2, IsVisible = false };
    readonly Button _back = new() { Content = "‹ Back", IsEnabled = false };
    readonly Button _forward = new() { Content = "Forward ›", IsEnabled = false };
    readonly TextBlock _where = new() { VerticalAlignment = VerticalAlignment.Center, Foreground = Brush("TextMuted"), TextTrimming = TextTrimming.CharacterEllipsis };
    readonly List<(string File, string Anchor)> _history = [];
    int _at = -1;
    readonly Dictionary<string, TreeViewItem> _treeItems = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Open the guide (or bring it forward) at a page and section.</summary>
    public static void Show(Window? owner, Action<string> startTutorial, Action showTutorials, string? file = null, string? anchor = null)
    {
        if (_open is null)
        {
            _open = new HelpWindow(HelpFiles.Library, startTutorial, showTutorials);
            _open.Closed += (_, _) => _open = null;
            if (owner is not null) _open.Show(owner);
            else _open.Show();
        }
        else _open.Activate();
        _open.Go(file ?? _open.Home, anchor ?? "");
    }

    string Home => _lib.Pages.ContainsKey("index.md") ? "index.md" : _lib.Contents.FirstOrDefault()?.File ?? _lib.Pages.Keys.FirstOrDefault() ?? "";

    HelpWindow(HelpLibrary lib, Action<string> startTutorial, Action showTutorials)
    {
        _lib = lib;
        _startTutorial = startTutorial;
        _showTutorials = showTutorials;
        Title = "Help guide";
        Width = 1240;
        Height = 860;
        MinWidth = 760;
        MinHeight = 480;
        Background = Brush("Bg1");
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        BuildTree();
        _search.TextChanged += (_, _) => ShowResults();
        _back.Click += (_, _) => Step(-1);
        _forward.Click += (_, _) => Step(1);
        var tutorials = new Button { Content = "▶ Tutorials", Classes = { "primary" } };
        ToolTip.SetTip(tutorials, "Short guided walks through each feature, a step at a time");
        tutorials.Click += (_, _) => _showTutorials();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Back && e.KeyModifiers == KeyModifiers.Alt || e.Key == Key.Left && e.KeyModifiers == KeyModifiers.Alt) { Step(-1); e.Handled = true; }
            else if (e.Key == Key.Right && e.KeyModifiers == KeyModifiers.Alt) { Step(1); e.Handled = true; }
            else if (e.Key == Key.F && e.KeyModifiers == KeyModifiers.Control) { _search.Focus(); e.Handled = true; }
        };

        var left = new DockPanel { Margin = new(12, 12, 6, 12) };
        DockPanel.SetDock(_search, Dock.Top);
        left.Children.Add(_search);
        left.Children.Add(new Panel { Children = { new ScrollViewer { Content = _tree }, new ScrollViewer { Content = _results, IsVisible = false } } });
        // The second scroll viewer shows the search results instead of the contents.
        var lists = (Panel)left.Children[1];
        _results.PropertyChanged += (_, e) =>
        {
            if (e.Property != IsVisibleProperty) return;
            lists.Children[0].IsVisible = !_results.IsVisible;
            lists.Children[1].IsVisible = _results.IsVisible;
        };

        var bar = new DockPanel { Margin = new(18, 12, 18, 8) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { _back, _forward } };
        DockPanel.SetDock(buttons, Dock.Left);
        DockPanel.SetDock(tutorials, Dock.Right);
        bar.Children.Add(buttons);
        bar.Children.Add(tutorials);
        _where.Margin = new(12, 0);
        bar.Children.Add(_where);
        var right = new DockPanel();
        DockPanel.SetDock(bar, Dock.Top);
        right.Children.Add(bar);
        right.Children.Add(new Border { BorderBrush = Brush("Line"), BorderThickness = new(0, 1, 0, 0), Child = _scroll });

        var grid = new Grid { ColumnDefinitions = new("300,Auto,*") };
        grid.Children.Add(new Border { Background = Brush("Bg2"), Child = left });
        var splitter = new GridSplitter { Width = 4, Background = Brush("Line") };
        Grid.SetColumn(splitter, 1);
        grid.Children.Add(splitter);
        Grid.SetColumn(right, 2);
        grid.Children.Add(right);
        Content = grid;
    }

    void BuildTree()
    {
        var listed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        TreeViewItem Item(ContentsEntry e)
        {
            listed.Add(e.File);
            var item = new TreeViewItem { Header = new TextBlock { Text = e.Title, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis } };
            item.PointerReleased += (_, ev) => { ev.Handled = true; Go(e.File, ""); };
            _treeItems.TryAdd(e.File, item);
            // The page's own sections, then the pages under it.
            if (_lib.Pages.TryGetValue(e.File, out var page))
                foreach (var h in page.Blocks.OfType<Heading>().Where(h => h.Level == 2))
                {
                    var sub = new TreeViewItem { Header = new TextBlock { Text = h.Text, FontSize = 12.5, Foreground = Brush("TextMuted"), TextTrimming = TextTrimming.CharacterEllipsis } };
                    sub.PointerReleased += (_, ev) => { ev.Handled = true; Go(e.File, h.Anchor); };
                    item.Items.Add(sub);
                }
            foreach (var c in e.Children) item.Items.Add(Item(c));
            return item;
        }
        foreach (var e in _lib.Contents) _tree.Items.Add(Item(e));
        // Pages not in the contents (shouldn't be any): at the end, so nothing is unreachable.
        foreach (var p in _lib.Pages.Values.Where(p => !listed.Contains(p.File)).OrderBy(p => p.Title))
            _tree.Items.Add(Item(new ContentsEntry(p.Title, p.File, [])));
    }

    void ShowResults()
    {
        var q = _search.Text?.Trim() ?? "";
        _results.Children.Clear();
        _results.IsVisible = q.Length > 0;
        if (q.Length == 0) return;
        var hits = _lib.Search(q);
        // Tutorials whose title or summary match, first: they're often what's wanted.
        foreach (var t in _lib.Tutorials.Where(t => q.Split(' ', StringSplitOptions.RemoveEmptyEntries).All(w => (t.Title + " " + t.Summary).Contains(w, StringComparison.OrdinalIgnoreCase))).Take(5))
        {
            var b = ResultButton("▶ Tutorial: " + t.Title, t.Summary);
            var id = t.Id;
            b.Click += (_, _) => _startTutorial(id);
            _results.Children.Add(b);
        }
        if (hits.Count == 0 && _results.Children.Count == 0)
            _results.Children.Add(Hint("Nothing found. Try fewer or shorter words."));
        foreach (var (page, anchor, heading, snippet) in hits)
        {
            var b = ResultButton(heading == page.Title ? page.Title : $"{page.Title} › {heading}", snippet);
            var (f, a) = (page.File, anchor);
            b.Click += (_, _) => Go(f, a);
            _results.Children.Add(b);
        }
    }

    static Button ResultButton(string title, string snippet) => new()
    {
        HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Background = Brushes.Transparent, Padding = new(6, 5),
        Content = new StackPanel
        {
            Spacing = 2,
            Children =
            {
                new TextBlock { Text = title, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = snippet, FontSize = 12, Foreground = Brush("TextMuted"), TextWrapping = TextWrapping.Wrap, MaxLines = 3 },
            },
        },
    };

    /// <summary>Follow a link: a page (page.md#section), a section of this page (#section), a tutorial or a web page.</summary>
    void Follow(string target)
    {
        if (target.StartsWith("tutorial:", StringComparison.OrdinalIgnoreCase)) { _startTutorial(target[9..]); return; }
        if (target.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || target.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            _ = Launcher.LaunchUriAsync(new Uri(target));
            return;
        }
        int hash = target.IndexOf('#');
        string file = hash < 0 ? target : target[..hash], anchor = hash < 0 ? "" : target[(hash + 1)..];
        if (file.Length == 0 && _at >= 0) file = _history[_at].File;
        Go(Path.GetFileName(file), anchor);
    }

    /// <summary>Show a page, scrolled to a section, and remember it for Back.</summary>
    public void Go(string file, string anchor)
    {
        if (_at >= 0 && _history[_at] == (file, anchor) && _scroll.Content is not null) { ScrollTo(anchor); return; }
        if (_at < _history.Count - 1) _history.RemoveRange(_at + 1, _history.Count - _at - 1);
        _history.Add((file, anchor));
        _at = _history.Count - 1;
        Display();
    }

    void Step(int by)
    {
        int to = _at + by;
        if (to < 0 || to >= _history.Count) return;
        _at = to;
        Display();
    }

    Dictionary<string, Control> _anchors = [];

    void Display()
    {
        var (file, anchor) = _history[_at];
        _back.IsEnabled = _at > 0;
        _forward.IsEnabled = _at < _history.Count - 1;
        if (!_lib.Pages.TryGetValue(file, out var page))
        {
            _scroll.Content = new TextBlock { Text = _lib.Pages.Count == 0 ? $"The guide's files weren't found (looked in {_lib.Folder})." : $"There's no page called {file}.", Margin = new(24), TextWrapping = TextWrapping.Wrap };
            return;
        }
        var r = new HelpRenderer(_lib.Folder, Follow);
        var body = r.Render(page.Blocks);
        // The tutorials for this page, at its end.
        // (Those the page doesn't already link to.)
        var linked = HelpText.Links(page.Blocks).Where(l => l.StartsWith("tutorial:", StringComparison.OrdinalIgnoreCase)).Select(l => l[9..]).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var tutorials = _lib.Tutorials.Where(t => string.Equals(t.Page?.Split('#')[0], file, StringComparison.OrdinalIgnoreCase) && !linked.Contains(t.Id)).ToList();
        var holder = new StackPanel { Spacing = 14, Children = { body } };
        if (tutorials.Count > 0)
        {
            var list = new StackPanel { Spacing = 6, Children = { new TextBlock { Text = linked.Count > 0 ? "More tutorials" : "Try it: tutorials", FontSize = 16, FontWeight = FontWeight.SemiBold, Margin = new(0, 18, 0, 0) } } };
            foreach (var t in tutorials)
            {
                var b = ResultButton("▶ " + t.Title, t.Summary);
                var id = t.Id;
                b.Click += (_, _) => _startTutorial(id);
                list.Children.Add(b);
            }
            holder.Children.Add(list);
        }
        _anchors = r.Anchors;
        _scroll.Content = new Border { Padding = new(36, 22, 36, 60), MaxWidth = 980, HorizontalAlignment = HorizontalAlignment.Left, Child = holder };
        _where.Text = page.Title + (anchor.Length > 0 && _anchors.ContainsKey(anchor) ? " › " + page.Blocks.OfType<Heading>().FirstOrDefault(h => h.Anchor == anchor)?.Text : "");
        Title = $"Help guide · {page.Title}";
        if (_treeItems.TryGetValue(file, out var item)) { _tree.SelectedItem = item; item.IsExpanded = true; }
        _scroll.Offset = default;
        ScrollTo(anchor);
    }

    void ScrollTo(string anchor)
    {
        if (anchor.Length == 0 || !_anchors.TryGetValue(anchor, out var target)) return;
        // After layout: the heading's place on the page is known then.
        Dispatcher.UIThread.Post(() =>
        {
            if (_scroll.Content is not Visual page || target.TranslatePoint(new Point(0, 0), page) is not { } p) return;
            _scroll.Offset = new Vector(0, Math.Max(0, p.Y - 8));
        }, DispatcherPriority.Background);
    }
}
