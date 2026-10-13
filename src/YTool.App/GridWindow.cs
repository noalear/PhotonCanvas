using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using static YTool.App.UiKit;

namespace YTool.App;

/// <summary>Grid spacing and colours, and snapping. Changes apply as you make them.</summary>
public sealed class GridWindow : Window
{
    readonly AppSettings _settings;
    readonly Action _apply;
    readonly NumericUpDown _major = Number(0.01m, 200, 1, "0.###"), _minor = Number(0.01m, 200, 0.1m, "0.###");
    readonly ColorPicker _majorColor = new(), _minorColor = new();
    readonly CheckBox _show = new() { Content = "Show the grid" };
    readonly CheckBox _snapObjects = new() { Content = "Snap to shapes and the work area (edges, centres, centrelines)" };
    readonly CheckBox _snapGrid = new() { Content = "Snap to grid (minor lines)" };
    readonly NumericUpDown _distance = Number(2, 40, 1, "0");
    readonly TextBlock _note = Hint();
    bool _loading;

    public GridWindow(AppSettings settings, Action apply)
    {
        _settings = settings;
        _apply = apply;
        Title = "Grid & snapping";
        Width = 440;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");

        _loading = true;
        _major.Value = (decimal)settings.GridMajorMm;
        _minor.Value = (decimal)settings.GridMinorMm;
        _majorColor.Color = Parse(settings.GridMajorColor, GridSettings.Default.MajorColor);
        _minorColor.Color = Parse(settings.GridMinorColor, GridSettings.Default.MinorColor);
        _show.IsChecked = settings.ShowGrid;
        _snapObjects.IsChecked = settings.SnapToObjects;
        _snapGrid.IsChecked = settings.SnapToGrid;
        _distance.Value = (decimal)settings.SnapDistancePx;
        _loading = false;

        var presets = new WrapPanel();
        foreach (var (maj, min) in new[] { (10.0, 1.0), (5.0, 1.0), (1.0, 0.1), (0.5, 0.1), (25.4, 2.54) })
        {
            var b = new Button { Content = maj == 25.4 ? "1 in / 0.1 in" : $"{maj:0.#} / {min:0.#} mm", Margin = new(0, 0, 6, 6) };
            b.Click += (_, _) => { _major.Value = (decimal)maj; _minor.Value = (decimal)min; };
            presets.Children.Add(b);
        }
        var close = new Button { Content = "Done", IsDefault = true, IsCancel = true, Classes = { "primary" }, HorizontalAlignment = HorizontalAlignment.Right, MinWidth = 100 };
        close.Click += (_, _) => Close();
        Content = new Border
        {
            Padding = new Thickness(18),
            Child = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    new TextBlock { Text = "GRID", Classes = { "h" } },
                    Field("Major lines (mm)", _major),
                    Field("Minor lines (mm)", _minor),
                    presets,
                    Field("Major colour", _majorColor),
                    Field("Minor colour", _minorColor),
                    _show,
                    _note,
                    new TextBlock { Text = "SNAPPING", Classes = { "h" }, Margin = new(0, 8, 0, 0) },
                    _snapObjects,
                    _snapGrid,
                    Field("Snap distance (px)", _distance, tip: "How close, on screen, something has to be before it snaps."),
                    Hint("Hold Ctrl while dragging to move freely without snapping."),
                    close,
                },
            },
        };
        foreach (var n in new[] { _major, _minor, _distance }) n.ValueChanged += (_, _) => Changed();
        _majorColor.ColorChanged += (_, _) => Changed();
        _minorColor.ColorChanged += (_, _) => Changed();
        foreach (var c in new[] { _show, _snapObjects, _snapGrid }) c.IsCheckedChanged += (_, _) => Changed();
        UpdateNote();
    }

    static Color Parse(string? s, Color fallback) => Color.TryParse(s, out var c) ? c : fallback;
    static string Hex(Color c) => c.A == 255 ? $"#{c.R:X2}{c.G:X2}{c.B:X2}" : $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";

    void Changed()
    {
        if (_loading) return;
        _settings.GridMajorMm = (double)(_major.Value ?? 10);
        _settings.GridMinorMm = (double)(_minor.Value ?? 1);
        _settings.GridMajorColor = Hex(_majorColor.Color);
        _settings.GridMinorColor = Hex(_minorColor.Color);
        _settings.ShowGrid = _show.IsChecked == true;
        _settings.SnapToObjects = _snapObjects.IsChecked == true;
        _settings.SnapToGrid = _snapGrid.IsChecked == true;
        _settings.SnapDistancePx = (double)(_distance.Value ?? 8);
        _settings.Save();
        UpdateNote();
        _apply();
    }

    void UpdateNote()
    {
        double maj = (double)(_major.Value ?? 10), min = (double)(_minor.Value ?? 1);
        double ratio = maj / Math.Max(1e-9, min);
        _note.Text = Math.Abs(ratio - Math.Round(ratio)) > 1e-6
            ? "Tip: major lines line up with minor ones when the major spacing is a whole multiple of the minor."
            : "Lines too close together to see at the current zoom are hidden until you zoom in.";
    }

    /// <summary>The grid described by the settings.</summary>
    public static GridSettings From(AppSettings s) => new(
        s.GridMajorMm, s.GridMinorMm,
        Parse(s.GridMajorColor, GridSettings.Default.MajorColor),
        Parse(s.GridMinorColor, GridSettings.Default.MinorColor));
}
