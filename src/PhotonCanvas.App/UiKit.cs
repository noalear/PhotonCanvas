using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace PhotonCanvas.App;

/// <summary>Small helpers so panels built in code stay consistent and readable.</summary>
public static class UiKit
{
    public static IBrush Brush(string key) => (IBrush)Application.Current!.FindResource(key)!;
    public static Geometry Icon(string key) => (Geometry)Application.Current!.FindResource(key)!;

    /// <summary>One of the program's icons by key (Icons.axaml), or else SVG path data (an add-on's own icon).</summary>
    public static Geometry IconOrPath(string keyOrPath)
    {
        if (Application.Current!.TryFindResource(keyOrPath, out var g) && g is Geometry geo) return geo;
        try { return Geometry.Parse(keyOrPath); }
        catch (Exception) { return Geometry.Parse("M4 4h16v16H4z M6 6v12h12V6z"); }
    }

    public static Border Card(string title, params Control[] children)
    {
        var stack = new StackPanel { Spacing = 8 };
        stack.Children.Add(new TextBlock { Text = title.ToUpperInvariant(), Classes = { "h" } });
        foreach (var c in children) stack.Children.Add(c);
        return new Border { Classes = { "card" }, Child = stack };
    }

    /// <summary>A titled group of settings with a line saying what it does.</summary>
    public static Control Explained(string title, string about, params Control[] content)
    {
        var panel = new StackPanel { Spacing = 8, Children = { new TextBlock { Text = title, FontWeight = FontWeight.SemiBold }, Hint(about) } };
        foreach (var c in content) panel.Children.Add(c);
        return new Border { BorderBrush = Brush("Line"), BorderThickness = new(1), CornerRadius = new(8), Padding = new(12), Child = panel };
    }

    /// <summary>A small grey label.</summary>
    public static TextBlock Muted(string text) => new() { Text = text, Classes = { "muted" }, FontSize = 12 };

    /// <summary>A setting with its name above it.</summary>
    public static Control Labelled(string label, Control editor) => Labelled(Muted(label), editor);

    public static Control Labelled(TextBlock label, Control editor) => new StackPanel { Spacing = 3, Children = { label, editor } };

    /// <summary>"4, 8, 12 and 16".</summary>
    public static string ListWords(IEnumerable<string> items)
    {
        var l = items.ToList();
        return l.Count <= 1 ? string.Concat(l) : $"{string.Join(", ", l.Take(l.Count - 1))} and {l[^1]}";
    }

    /// <summary>Label on the left, editor on the right, optional hint underneath.</summary>
    /// <summary>
    /// A diode/MOPA choice, shown only where the machine has both to choose from: on the F2 Ultra UV (and
    /// machines without a known MOPA) it's hidden and the work runs on the machine's own laser.
    /// </summary>
    public static Control LaserChoice(Control row, PhotonCanvas.Core.MachineProfile machine)
    {
        row.IsVisible = machine.Uv is null && machine.HasMopa;
        return row;
    }

    public static Control Field(string label, Control editor, TextBlock? hint = null, string? tip = null)
    {
        var grid = new Grid { ColumnDefinitions = new("110,*") };
        var lbl = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Classes = { "muted" } };
        grid.Children.Add(lbl);
        Grid.SetColumn(editor, 1);
        grid.Children.Add(editor);
        if (tip is not null)
        {
            ToolTip.SetTip(lbl, tip);
            ToolTip.SetTip(editor, tip);
        }
        if (hint is null) return grid;
        hint.Margin = new(110, 2, 0, 0);
        return new StackPanel { Children = { grid, hint } };
    }

    public static NumericUpDown Number(decimal min, decimal max, decimal step, string format = "0.##")
    {
        return new NumericUpDown
        {
            Minimum = min,
            Maximum = max,
            Increment = step,
            FormatString = format,
            ClipValueToMinMax = true,
            AllowSpin = true,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
    }

    public static TextBlock Hint(string text = "") => new() { Classes = { "hint" }, Text = text };

    public static Button Button(string text, string? icon = null, string? tip = null)
    {
        object content = text;
        if (icon is not null)
            content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children = { new PathIcon { Data = Icon(icon), Width = 14, Height = 14 }, new TextBlock { Text = text } },
            };
        var b = new Button { Content = content, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
        if (tip is not null) ToolTip.SetTip(b, tip);
        return b;
    }

    public static Grid Row(params Control[] cells)
    {
        var g = new Grid { ColumnDefinitions = new(string.Join(",", cells.Select(_ => "*"))), ColumnSpacing = 6 };
        for (int i = 0; i < cells.Length; i++)
        {
            Grid.SetColumn(cells[i], i);
            g.Children.Add(cells[i]);
        }
        return g;
    }

    public static string Duration(double seconds) => PhotonCanvas.Core.TimeText.Duration(seconds);

    /// <summary>Short clock-style duration: 45 s, 3:07, 1:02:30.</summary>
    public static string Clock(double seconds) => PhotonCanvas.Core.TimeText.Clock(seconds);

    /// <summary>Configure an object inline and return it (for building menus in one expression).</summary>
    public static T With<T>(this T o, Action<T> setup)
    {
        setup(o);
        return o;
    }
}
