using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using PhotonCanvas.Core;
using PhotonCanvas.Core.Imaging;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Colour;
using PhotonCanvas.Doc.Materials;
using static PhotonCanvas.App.UiKit;

using PhotonCanvas.App;

namespace PhotonCanvas.Addon.ColourOnMetal;

/// <summary>
/// A picture in the colours a plate really made (a measured palette from Measure colours): the
/// original and the result side by side, simulated with the measured colours, before any layer
/// is added. Each colour becomes one fill layer with the settings that made it.
/// </summary>
public sealed class ColourPictureWindow : Window
{
    readonly ImageShape _picture;
    readonly List<(Material Material, ColourPalette Palette)> _palettes;
    readonly ComboBox _palette = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly NumericUpDown _pixel = Number(0.05m, 3, 0.05m, "0.##");
    readonly CheckBox _dither = new() { Content = "Mix colours with dots for in-between shades", IsChecked = true };
    readonly CheckBox _fit = new() { Content = "Fit the picture's light and dark to the palette", IsChecked = true };
    readonly CheckBox _bare = new() { Content = "Use bare metal as a colour", IsChecked = true };
    readonly Slider _merge = new() { Minimum = 2, Maximum = 20, Value = 5 };
    readonly TextBlock _mergeText = new() { MinWidth = 30, VerticalAlignment = VerticalAlignment.Center };
    readonly NumericUpDown _overscan = Number(0, 2, 0.05m, "0.##");
    readonly Image _before = new() { Stretch = Stretch.Uniform }, _after = new() { Stretch = Stretch.Uniform };
    readonly WrapPanel _chips = new();
    readonly TextBlock _summary = new() { TextWrapping = TextWrapping.Wrap };
    readonly Avalonia.Threading.DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(250) };
    int _version;

    /// <summary>The palette and options chosen, or null if cancelled.</summary>
    public (ColourPalette Palette, ColourPictureOptions Options)? Result { get; private set; }

    public ColourPictureWindow(ImageShape picture, MaterialLibrary library)
    {
        _picture = picture;
        _palettes = library.Materials.SelectMany(m => m.Palettes.OrderByDescending(p => p.Measured).Select(p => (m, p))).ToList();
        Title = "Colour picture on metal (MOPA)";
        Width = 1100;
        Height = 720;
        MinWidth = 860;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");

        _palette.ItemsSource = _palettes.Select(x => $"{x.Material.Label} · {x.Palette.Name} ({x.Palette.Measured:d MMM})").ToList();
        _palette.SelectedIndex = _palettes.Count > 0 ? 0 : -1;
        _pixel.Value = 0.3m;
        _overscan.Value = 0;
        ToolTip.SetTip(_pixel, "Size of one colour dot. Smaller = finer detail, but heat makes neighbouring colours bleed: 0.2–0.5 mm is a good start.");
        ToolTip.SetTip(_merge, "Palette colours closer than this count as one colour (ΔE). Higher = fewer layers.");
        ToolTip.SetTip(_overscan, "Run-up past each line with the laser off: steadier colour right to the edges, a little slower.");
        foreach (var n in new[] { _pixel, _overscan }) n.ValueChanged += (_, _) => Later();
        foreach (var c in new[] { _dither, _fit, _bare }) c.IsCheckedChanged += (_, _) => Later();
        _merge.ValueChanged += (_, _) => Later();
        _palette.SelectionChanged += (_, _) => Later();
        _debounce.Tick += (_, _) => { _debounce.Stop(); Update(); };

        var add = new Button { Content = "Add colour layers", Classes = { "primary" }, IsDefault = true, MinWidth = 150, IsEnabled = _palettes.Count > 0 };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 100 };
        add.Click += (_, _) => { if (Current() is { } p) Result = (p, Options()); Close(); };
        cancel.Click += (_, _) => Close();

        var form = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap,
                    Text = "Every pixel becomes the nearest colour your plate really made, with dots of neighbouring colours mixed in for the shades between. " +
                           "Each colour is one layer, with the settings that made it.",
                },
                Field("Palette", _palette),
                Field("Dot size (mm)", _pixel),
                _dither,
                _fit,
                _bare,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { new TextBlock { Text = "Merge colours closer than ΔE", Classes = { "muted" }, VerticalAlignment = VerticalAlignment.Center }, new Border { Width = 130, Child = _merge }, _mergeText } },
                Field("Overscan (mm)", _overscan),
                new TextBlock { Text = "COLOURS USED", Classes = { "h" }, Margin = new(0, 6, 0, 0) },
                _chips,
                _summary,
                Hint("Use the same plate, finish and focus as the colour test, and the same lights for judging. Colours shift with the viewing angle, like the test."),
            },
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = new(0, 10, 0, 0), Children = { cancel, add } };
        var left = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Bottom);
        left.Children.Add(buttons);
        left.Children.Add(new ScrollViewer { Content = form });

        Control Pane(string title, Image img) => new DockPanel
        {
            Children =
            {
                new TextBlock { Text = title, Classes = { "h" }, [DockPanel.DockProperty] = Dock.Top },
                new Border { Background = Brush("Bg0"), CornerRadius = new(6), Padding = new(8), Child = img },
            },
        };
        var panes = new Grid { ColumnDefinitions = new("*,12,*") };
        panes.Children.Add(Pane("THE PICTURE", _before));
        var afterPane = Pane("ON THE METAL (SIMULATED)", _after);
        Grid.SetColumn(afterPane, 2);
        panes.Children.Add(afterPane);
        var grid = new Grid { ColumnDefinitions = new("340,16,*") };
        grid.Children.Add(left);
        Grid.SetColumn(panes, 2);
        grid.Children.Add(panes);
        Content = new Border { Padding = new(16), Child = grid };

        using (var ms = new MemoryStream(picture.Source.Pixels.EncodePng(900))) _before.Source = new Avalonia.Media.Imaging.Bitmap(ms);
        if (_palettes.Count == 0)
            _summary.Text = "There are no measured colour palettes yet. Burn a colour test (Materials & Tests → Tests → Colour test), measure it (Materials & Tests → Tests → Measure colours) and save the palette first.";
        else Update();
    }

    void Later()
    {
        _debounce.Stop();
        _debounce.Start();
    }

    ColourPalette? Current() => _palette.SelectedIndex >= 0 && _palette.SelectedIndex < _palettes.Count ? _palettes[_palette.SelectedIndex].Palette : null;

    ColourPictureOptions Options() => new()
    {
        PixelMm = (double)(_pixel.Value ?? 0.3m),
        Dither = _dither.IsChecked == true,
        FitLightness = _fit.IsChecked == true,
        UseBare = _bare.IsChecked == true,
        MinDeltaE = Math.Round(_merge.Value),
        OverscanMm = (double)(_overscan.Value ?? 0),
    };

    void Update()
    {
        _mergeText.Text = $"{_merge.Value:0}";
        if (Current() is not { } palette) return;
        var o = Options();
        var map = ColourPicture.Map(_picture.Source.Pixels, _picture.SizeMm, palette, o);
        // The result, drawn in the measured colours.
        var rgba = new byte[map.Cols * map.Rows * 4];
        var srgb = map.Inks.Select(k => ColourScience.LabToSrgb(k.Colour)).ToArray();
        for (int i = 0; i < map.Cells.Length; i++)
        {
            int k = map.Cells[i];
            if (k < 0) continue;
            (rgba[i * 4], rgba[i * 4 + 1], rgba[i * 4 + 2]) = srgb[k];
            rgba[i * 4 + 3] = 255;
        }
        var old = _after.Source as IDisposable;
        using (var ms = new MemoryStream(new RasterImage(map.Cols, map.Rows, rgba).EncodePng()))
            _after.Source = new Avalonia.Media.Imaging.Bitmap(ms);
        RenderOptions.SetBitmapInterpolationMode(_after, Avalonia.Media.Imaging.BitmapInterpolationMode.None);
        old?.Dispose();

        _chips.Children.Clear();
        int total = Math.Max(1, map.Counts.Sum());
        foreach (var ink in map.Inks.Where(k => map.Counts[k.Index] > 0).OrderByDescending(k => map.Counts[k.Index]))
        {
            var (r, g, b) = srgb[ink.Index];
            var chip = new Border { Width = 26, Height = 26, Margin = new(0, 0, 4, 4), CornerRadius = new(4), Background = new SolidColorBrush(Color.FromRgb(r, g, b)), BorderBrush = Brush("Line"), BorderThickness = new(1) };
            ToolTip.SetTip(chip, (ink.IsBare ? "Bare metal (not marked)" : ink.Swatch!.Settings.Summary()) + $"\n{100.0 * map.Counts[ink.Index] / total:0}% of the picture");
            _chips.Children.Add(chip);
        }
        int layers = map.Inks.Count(k => !k.IsBare && map.Counts[k.Index] > 0);
        string head = $"{map.Cols} × {map.Rows} dots, {map.Inks.Count(k => map.Counts[k.Index] > 0)} colours, {layers} layer{(layers == 1 ? "" : "s")}.";
        if (palette.BareL is null && o.UseBare) head += " (Bare metal wasn't measured with this palette: a light grey stands in for it.)";
        _summary.Text = head + " Working out the time…";
        _ = EstimateAsync(palette, o, head);
    }

    async Task EstimateAsync(ColourPalette palette, ColourPictureOptions o, string head)
    {
        int v = ++_version;
        var picture = _picture;
        string time;
        try
        {
            time = await Task.Run(() =>
            {
                var s = DocState.New();
                var layer = Layer.Default(0, LayerMode.Image);
                s = Ops.AddLayer(s, layer);
                var copy = new ImageShape { Name = "p", LayerId = layer.Id, Source = picture.Source, SizeMm = picture.SizeMm, Transform = picture.Transform };
                s = Ops.AddShapes(s, [copy]);
                var (after, ids) = ColourPicture.Add(s, copy, palette, o);
                if (ids.Count == 0) return " Nothing to mark.";
                after = Ops.UpdateLayers(after, new HashSet<Guid> { layer.Id }, l => l with { Output = false });
                return $" About {Duration(JobBuilder.Build(after, new JobOptions { WholeJobPasses = false }).EstimateS)} to mark.";
            });
        }
        catch (JobException e) { time = $" ⚠ {e.Message}"; }
        if (v == _version) _summary.Text = head + time;
    }
}
