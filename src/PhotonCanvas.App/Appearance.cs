using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using static PhotonCanvas.App.UiKit;

namespace PhotonCanvas.App;

/// <summary>
/// Lengths shown in millimetres or inches. Everything is kept in millimetres; only what's
/// shown and typed changes (rulers, the cursor read-out, sizes on the work area, measuring,
/// and the selection's position and size).
/// </summary>
public static class Units
{
    public static bool Inches { get; set; }

    public static double PerUnit => Inches ? 25.4 : 1;
    public static string Name => Inches ? "in" : "mm";

    public static double FromMm(double mm) => mm / PerUnit;
    public static double ToMm(double v) => v * PerUnit;

    /// <summary>A length with its unit, e.g. "12.5 mm" or "0.492 in".</summary>
    public static string Len(double mm) => Num(mm) + " " + Name;

    /// <summary>A length in the current unit, without the unit (3 decimals for inches, 2 for mm).</summary>
    public static string Num(double mm) => FromMm(mm).ToString(Inches ? "0.###" : "0.##", CultureInfo.InvariantCulture);
}

/// <summary>
/// Dark (the default) or light colours. Set once at start-up, before any window is made: the
/// palette brushes are replaced and the controls' own theme follows.
/// </summary>
public static class AppTheme
{
    public static bool Light { get; private set; }

    static readonly Dictionary<string, string> LightPalette = new()
    {
        ["Bg0"] = "#E4E8EE", ["Bg1"] = "#F3F5F8", ["Bg2"] = "#FFFFFF", ["Bg3"] = "#E6EAF0",
        ["Line"] = "#CBD2DC", ["TextMain"] = "#1A1F28", ["TextMuted"] = "#5B6472",
        ["Accent"] = "#2F64D0", ["AccentDeep"] = "#2453B3", ["AccentSoft"] = "#DCE6FA", ["AccentLine"] = "#8FAAE3",
        ["AccentHover"] = "#CCDAF7", ["OnAccentSoft"] = "#163A86", ["ContextText"] = "#2453B3", ["HudBg"] = "#F2FFFFFF", ["SelBg"] = "#DCE6FA",
        ["Warn"] = "#A86E0A", ["Danger"] = "#B83A33", ["Ok"] = "#2B8459",
    };

    public static void Apply(Application app, bool light)
    {
        Light = light;
        if (!light) return;
        app.RequestedThemeVariant = ThemeVariant.Light;
        foreach (var (key, hex) in LightPalette) app.Resources[key] = new SolidColorBrush(Color.Parse(hex));
        CanvasView.UseLightColours();
    }
}

/// <summary>Settings page: colours and units.</summary>
public sealed class AppearancePanel : UserControl
{
    public AppearancePanel(AppSettings settings, Action unitsChanged)
    {
        var theme = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = new[] { "Dark", "Light" }, SelectedIndex = settings.Theme == "Light" ? 1 : 0 };
        var themeNote = Hint(theme.SelectedIndex == (AppTheme.Light ? 1 : 0) ? "" : "Takes effect the next time the program starts.");
        theme.SelectionChanged += (_, _) =>
        {
            settings.Theme = theme.SelectedIndex == 1 ? "Light" : "Dark";
            settings.Save();
            themeNote.Text = (theme.SelectedIndex == 1) == AppTheme.Light ? "" : "Takes effect the next time the program starts.";
        };
        var units = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = new[] { "Millimetres", "Inches" }, SelectedIndex = settings.Inches ? 1 : 0 };
        units.SelectionChanged += (_, _) =>
        {
            settings.Inches = units.SelectedIndex == 1;
            Units.Inches = settings.Inches;
            settings.Save();
            unitsChanged();
        };
        Content = new StackPanel
        {
            Spacing = 10,
            Margin = new(0, 4, 0, 0),
            Children =
            {
                new TextBlock { Text = "APPEARANCE", Classes = { "h" } },
                Field("Colours", theme),
                themeNote,
                Field("Lengths in", units),
                Hint("Rulers, the cursor position, sizes on the work area, measuring, and a selection's position and size. Layer settings (speed, line spacing, kerf…) stay in millimetres."),
            },
        };
    }
}
