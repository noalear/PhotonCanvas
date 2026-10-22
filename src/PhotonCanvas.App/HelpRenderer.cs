using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using PhotonCanvas.Doc.Help;
using Inline = PhotonCanvas.Doc.Help.Inline;
using static PhotonCanvas.App.UiKit;

namespace PhotonCanvas.App;

/// <summary>Where the guide's files are: next to the program, or (running from the source) docs/help.</summary>
public static class HelpFiles
{
    static HelpLibrary? _library;
    static string? _folder;

    public static string Folder => _folder ??= Find();

    static string Find()
    {
        // Writing the guide: PHOTONCANVAS_HELP points at docs/help, so pages show as they're edited.
        if (Environment.GetEnvironmentVariable("PHOTONCANVAS_HELP") is { Length: > 0 } custom && Directory.Exists(custom)) return custom;
        var shipped = Path.Combine(AppContext.BaseDirectory, "help");
        if (File.Exists(Path.Combine(shipped, "contents.md"))) return shipped;
        // From a build in the source tree: look up for docs/help.
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            var docs = Path.Combine(d.FullName, "docs", "help");
            if (File.Exists(Path.Combine(docs, "contents.md"))) return docs;
        }
        return shipped;
    }

    /// <summary>The guide, read once.</summary>
    public static HelpLibrary Library => _library ??= HelpLibrary.Load(Folder);
}

/// <summary>Turns the guide's blocks into controls in the program's look.</summary>
sealed class HelpRenderer(string folder, Action<string> follow)
{
    /// <summary>Headings by anchor, filled in as a page is drawn (to jump to a section).</summary>
    public Dictionary<string, Control> Anchors { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Smaller type and tighter spacing (the tutorial card).</summary>
    public bool Compact { get; init; }

    double BodySize => Compact ? 13 : 14.5;

    public Control Render(IEnumerable<Block> blocks)
    {
        var panel = new StackPanel { Spacing = Compact ? 6 : 10 };
        foreach (var b in blocks) panel.Children.Add(Block(b));
        return panel;
    }

    Control Block(Block b)
    {
        switch (b)
        {
            case Heading h:
            {
                var t = new SelectableTextBlock
                {
                    Text = h.Text, TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.SemiBold,
                    FontSize = h.Level switch { 1 => Compact ? 17 : 27, 2 => Compact ? 15 : 20, _ => Compact ? 14 : 16.5 },
                    Margin = h.Level switch { 1 => new(0, 0, 0, 4), 2 => new(0, Compact ? 4 : 18, 0, 0), _ => new(0, Compact ? 2 : 8, 0, 0) },
                };
                Control c = t;
                if (h.Level == 2 && !Compact)
                    c = new StackPanel { Spacing = 6, Children = { t, new Border { Height = 1, Background = Brush("Line") } } };
                Anchors.TryAdd(h.Anchor, c);
                return c;
            }
            case Paragraph p:
                return Text(p.Inlines);
            case ListBlock l:
                return List(l.Numbered, l.Items);
            case Figure f:
                return Figure(f);
            case Callout c:
                return Callout(c);
            case Table t:
                return Table(t);
            case CodeBlock code:
                return new Border
                {
                    Background = Brush("Bg0"), CornerRadius = new(6), Padding = new(10, 8),
                    Child = new SelectableTextBlock { Text = code.Text, FontFamily = new FontFamily("Consolas, Menlo, monospace"), FontSize = 12.5, TextWrapping = TextWrapping.Wrap },
                };
            case Rule:
                return new Border { Height = 1, Background = Brush("Line"), Margin = new(0, 6) };
            default:
                return new TextBlock();
        }
    }

    /// <summary>A paragraph's text with its bold, italic, control names and links.</summary>
    public SelectableTextBlock Text(IEnumerable<Inline> inlines, double? size = null)
    {
        var t = new SelectableTextBlock { TextWrapping = TextWrapping.Wrap, FontSize = size ?? BodySize, LineHeight = (size ?? BodySize) * 1.5 };
        var list = new InlineCollection();
        foreach (var i in inlines)
            switch (i)
            {
                case TextRun r:
                    var run = new Run(r.Text);
                    if (r.Bold) run.FontWeight = FontWeight.SemiBold;
                    if (r.Italic) run.FontStyle = FontStyle.Italic;
                    list.Add(run);
                    break;
                case CodeRun c:
                    // A key or a control's name: set off like a key cap.
                    list.Add(new Run($" {c.Text} ") { Background = Brush("Bg3"), FontWeight = FontWeight.SemiBold, Foreground = Brush("TextMain") });
                    break;
                case LinkRun l:
                    var link = new TextBlock
                    {
                        Text = l.Text, Foreground = Brush("Accent"), Cursor = new Cursor(StandardCursorType.Hand),
                        FontSize = size ?? BodySize, TextDecorations = TextDecorations.Underline,
                    };
                    if (l.Target.StartsWith("tutorial:", StringComparison.OrdinalIgnoreCase)) link.Text = "▶ " + l.Text;
                    ToolTip.SetTip(link, l.Target.StartsWith("tutorial:", StringComparison.OrdinalIgnoreCase) ? "Start this tutorial" : l.Target.StartsWith("http") ? l.Target : null);
                    var target = l.Target;
                    link.PointerPressed += (_, e) => { e.Handled = true; follow(target); };
                    list.Add(new InlineUIContainer(link) { BaselineAlignment = BaselineAlignment.TextBottom });
                    break;
            }
        t.Inlines = list;
        return t;
    }

    Control List(bool numbered, IReadOnlyList<ListItem> items, int depth = 0)
    {
        var panel = new StackPanel { Spacing = Compact ? 3 : 5, Margin = new(depth == 0 ? 2 : 18, 0, 0, 0) };
        for (int n = 0; n < items.Count; n++)
        {
            var item = items[n];
            var grid = new Grid { ColumnDefinitions = new("Auto,*") };
            var mark = new TextBlock
            {
                Text = numbered ? $"{n + 1}." : depth == 0 ? "•" : "◦", FontSize = BodySize, MinWidth = numbered ? 22 : 16,
                Foreground = Brush(numbered ? "TextMain" : "Accent"), FontWeight = numbered ? FontWeight.SemiBold : FontWeight.Normal,
                Margin = new(0, 0, 4, 0),
            };
            grid.Children.Add(mark);
            var body = new StackPanel { Spacing = 4, Children = { Text(item.Inlines) } };
            if (item.Children.Count > 0) body.Children.Add(List(false, item.Children, depth + 1));
            Grid.SetColumn(body, 1);
            grid.Children.Add(body);
            panel.Children.Add(grid);
        }
        return panel;
    }

    Control Figure(Figure f)
    {
        var path = Path.Combine(folder, f.Source.Replace('/', Path.DirectorySeparatorChar));
        Control picture;
        if (File.Exists(path))
        {
            Bitmap bmp;
            try { bmp = new Bitmap(path); }
            catch (Exception) { return Missing(f); }
            var img = new Image
            {
                Source = bmp, Stretch = Stretch.Uniform, MaxWidth = bmp.Size.Width, HorizontalAlignment = HorizontalAlignment.Left,
                Cursor = new Cursor(StandardCursorType.Hand),
            };
            ToolTip.SetTip(img, "Click to see it full size");
            img.PointerPressed += (_, e) =>
            {
                e.Handled = true;
                var w = new Window
                {
                    Title = f.Caption.Length > 0 ? f.Caption : Path.GetFileName(path), Background = Brush("Bg1"),
                    Width = Math.Min(1400, bmp.Size.Width + 40), Height = Math.Min(950, bmp.Size.Height + 60),
                    Content = new ScrollViewer { HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, Content = new Image { Source = bmp, Stretch = Stretch.None } },
                };
                w.Show();
            };
            picture = new Border { BorderBrush = Brush("Line"), BorderThickness = new(1), CornerRadius = new(6), ClipToBounds = true, HorizontalAlignment = HorizontalAlignment.Left, Child = img };
        }
        else return Missing(f);
        var panel = new StackPanel { Spacing = 4, Margin = new(0, 4, 0, 6), Children = { picture } };
        if (f.Caption.Length > 0)
            panel.Children.Add(new TextBlock { Text = f.Caption, FontSize = 12.5, FontStyle = FontStyle.Italic, Foreground = Brush("TextMuted"), TextWrapping = TextWrapping.Wrap });
        return panel;
    }

    static Control Missing(Figure f) => new Border
    {
        BorderBrush = Brush("Line"), BorderThickness = new(1), CornerRadius = new(6), Padding = new(10),
        Child = new TextBlock { Text = $"(picture: {f.Caption})", Foreground = Brush("TextMuted"), TextWrapping = TextWrapping.Wrap },
    };

    Control Callout(Callout c)
    {
        var (label, colour) = c.Kind switch
        {
            "tip" => ("Tip", "Ok"),
            "warning" => ("Warning", "Warn"),
            "safety" => ("Safety", "Danger"),
            _ => ("Note", "Accent"),
        };
        var text = Text([new TextRun(label + ": ", Bold: true), .. c.Inlines]);
        return new Border
        {
            Background = Brush("Bg2"), BorderBrush = Brush(colour), BorderThickness = new(3, 0, 0, 0),
            CornerRadius = new(4), Padding = new(12, 8), Child = text,
        };
    }

    Control Table(Table t)
    {
        int cols = t.Rows.Count == 0 ? 0 : t.Rows.Max(r => r.Count);
        var grid = new Grid { ColumnDefinitions = new(string.Join(",", Enumerable.Range(0, cols).Select(i => i == cols - 1 ? "*" : "Auto"))) };
        for (int r = 0; r < t.Rows.Count; r++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            for (int c = 0; c < t.Rows[r].Count; c++)
            {
                var text = Text(t.Rows[r][c], BodySize - 1);
                if (r == 0) text.FontWeight = FontWeight.SemiBold;
                var cell = new Border
                {
                    Padding = new(8, 5), BorderBrush = Brush("Line"), BorderThickness = new(0, 0, 0, 1),
                    Background = r == 0 ? Brush("Bg2") : null, Child = text, MaxWidth = c == cols - 1 ? double.PositiveInfinity : 320,
                };
                Grid.SetRow(cell, r);
                Grid.SetColumn(cell, c);
                grid.Children.Add(cell);
            }
        }
        return new Border { BorderBrush = Brush("Line"), BorderThickness = new(1), CornerRadius = new(6), ClipToBounds = true, Child = grid };
    }
}
