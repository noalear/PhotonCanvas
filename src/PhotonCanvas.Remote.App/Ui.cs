using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace PhotonCanvas.Remote.App;

/// <summary>Small helpers so the screens built in code stay consistent.</summary>
static class Ui
{
    public static IBrush Brush(string key) =>
        Application.Current!.TryGetResource(key, null, out var v) && v is IBrush b ? b : Brushes.Gray;

    public static TextBlock Text(string text = "", string? cls = null)
    {
        var t = new TextBlock { Text = text };
        if (cls is not null) t.Classes.Add(cls);
        return t;
    }

    public static Border Card(string title, params Control[] children)
    {
        var stack = new StackPanel { Spacing = 8 };
        stack.Children.Add(Text(title.ToUpperInvariant(), "h"));
        foreach (var c in children) stack.Children.Add(c);
        return new Border { Classes = { "card" }, Child = stack };
    }

    public static Button Btn(string text, string? cls = null, Action? click = null)
    {
        var b = new Button { Content = text };
        if (cls is not null) b.Classes.Add(cls);
        if (click is not null) b.Click += (_, _) => click();
        return b;
    }

    /// <summary>Cells side by side, equally wide; hidden cells give their room to the others.</summary>
    public static Grid Row(params Control[] cells)
    {
        var g = new Grid { ColumnSpacing = 8 };
        void Lay()
        {
            g.ColumnDefinitions.Clear();
            int col = 0;
            foreach (var c in cells)
            {
                g.ColumnDefinitions.Add(new ColumnDefinition(c.IsVisible ? GridLength.Star : new GridLength(0)));
                Grid.SetColumn(c, col++);
            }
        }
        foreach (var c in cells)
        {
            g.Children.Add(c);
            c.PropertyChanged += (_, e) => { if (e.Property == Visual.IsVisibleProperty) Lay(); };
        }
        Lay();
        return g;
    }

    public static Bitmap? Picture(byte[]? bytes)
    {
        if (bytes is not { Length: > 0 }) return null;
        try { return new Bitmap(new MemoryStream(bytes)); }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or InvalidOperationException) { return null; }
    }

    public static Image PictureBox(double maxHeight = 320) => new()
    {
        Stretch = Stretch.Uniform,
        MaxHeight = maxHeight,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        IsVisible = false,
    };

    /// <summary>45 s, 3:07, 1:02:30.</summary>
    public static string Clock(double? seconds)
    {
        if (seconds is not { } s || !double.IsFinite(s) || s < 0) return "";
        var t = TimeSpan.FromSeconds(Math.Round(s));
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : t.TotalMinutes >= 1 ? $"{t.Minutes}:{t.Seconds:00}" : $"{t.Seconds} s";
    }

    public static string SourceText(string source) => source switch
    {
        "phone" => "Loaded from the phone",
        "computer" => "Sent from the desktop app",
        _ => "From xTool Studio, the machine's screen or another computer",
    };
}
