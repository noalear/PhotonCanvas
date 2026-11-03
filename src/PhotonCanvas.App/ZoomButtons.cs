using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using PhotonCanvas.Doc;
using static PhotonCanvas.App.UiKit;

namespace PhotonCanvas.App;

/// <summary>
/// Four small icon buttons in the bottom-right corner of the work area: the whole bed, the whole
/// design, zoom in and zoom out. Each runs the same command as its keyboard shortcut, and its tip
/// shows that shortcut as it's set now (shortcuts can be changed).
/// </summary>
public sealed class ZoomButtons : UserControl
{
    /// <summary>The buttons, in order: icon, what it does, the command it runs.</summary>
    public static readonly (string Icon, string Tip, string Command)[] Buttons =
    [
        ("IcFitBed", "Show the whole bed", "view.fit"),
        ("IcFitDesign", "Show the whole design", "view.fitdesign"),
        ("IcZoomIn", "Zoom in", "view.zoomin"),
        ("IcZoomOut", "Zoom out", "view.zoomout"),
    ];

    public ZoomButtons(Func<string, HotCommand?> command, AppSettings settings)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 1 };
        foreach (var (icon, tip, id) in Buttons)
        {
            var b = new Button
            {
                Classes = { "icon" },
                Width = 26, Height = 26, MinWidth = 0, MinHeight = 0, Padding = new(0),
                HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
                // Clicking leaves the keyboard with the canvas, so shortcuts keep working.
                Focusable = false,
                Content = new PathIcon { Data = Icon(icon), Width = 15, Height = 15 },
            };
            AutomationName(b, tip);
            ToolTip.SetTip(b, tip);
            // Looked up when used: the commands are made after the window, and shortcuts can be changed.
            b.PointerEntered += (_, _) => { if (command(id) is { } c) ToolTip.SetTip(b, Tip(tip, HotkeysWindow.Keys(c, settings))); };
            b.Click += (_, _) => command(id)?.Run();
            row.Children.Add(b);
        }
        Content = new Border
        {
            Background = Brush("HudBg"), BorderBrush = Brush("Line"), BorderThickness = new(1),
            CornerRadius = new(7), Padding = new(2),
            // Small enough to sit in the margin the fitted bed leaves (40 px), so it never covers the bed.
            Margin = new(0, 0, 8, 6),
            Child = row,
        };
    }

    static void AutomationName(Control c, string name) => Avalonia.Automation.AutomationProperties.SetName(c, name);

    /// <summary>"Zoom in (Ctrl+=)": the first of the command's shortcuts, written as on the keyboard.</summary>
    public static string Tip(string what, string keys)
    {
        var first = keys.Split('|', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return first is null ? what : $"{what} ({KeyText(first)})";
    }

    /// <summary>A shortcut as it's written on the keys: "Ctrl+OemPlus" → "Ctrl+=", "Ctrl+D0" → "Ctrl+0".</summary>
    public static string KeyText(string gesture) => string.Join("+", gesture.Split('+').Select(k => k switch
    {
        "OemPlus" => "=",
        "OemMinus" => "-",
        "Add" => "Num +",
        "Subtract" => "Num -",
        "OemQuotes" => "'",
        "OemComma" => ",",
        "OemPeriod" => ".",
        _ when k.Length == 2 && k[0] == 'D' && char.IsDigit(k[1]) => k[1..],
        _ when k.StartsWith("NumPad", StringComparison.Ordinal) => "Num " + k[6..],
        _ => k,
    }));
}
