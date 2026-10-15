using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace PhotonCanvas.App;

/// <summary>Small helpers so panels built in code stay consistent and readable.</summary>
static class UiKit
{
    public static IBrush Brush(string key) => (IBrush)Application.Current!.FindResource(key)!;
    public static Geometry Icon(string key) => (Geometry)Application.Current!.FindResource(key)!;

    public static Border Card(string title, params Control[] children)
    {
        var stack = new StackPanel { Spacing = 8 };
        stack.Children.Add(new TextBlock { Text = title.ToUpperInvariant(), Classes = { "h" } });
        foreach (var c in children) stack.Children.Add(c);
        return new Border { Classes = { "card" }, Child = stack };
    }

    /// <summary>Label on the left, editor on the right, optional hint underneath.</summary>
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

    public static string Duration(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0) return "–";
        var t = TimeSpan.FromSeconds(Math.Round(seconds));
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours} h {t.Minutes:00} min" : t.TotalMinutes >= 1 ? $"{t.Minutes} min {t.Seconds:00} s" : $"{t.Seconds} s";
    }

    /// <summary>Short clock-style duration: 45 s, 3:07, 1:02:30.</summary>
    public static string Clock(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0) return "";
        var t = TimeSpan.FromSeconds(Math.Round(seconds));
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : t.TotalMinutes >= 1 ? $"{t.Minutes}:{t.Seconds:00}" : $"{t.Seconds} s";
    }

    /// <summary>Configure an object inline and return it (for building menus in one expression).</summary>
    public static T With<T>(this T o, Action<T> setup)
    {
        setup(o);
        return o;
    }
}
