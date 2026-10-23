using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using PhotonCanvas.Core;
using PhotonCanvas.Core.Imaging;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Import;
using static PhotonCanvas.App.UiKit;

namespace PhotonCanvas.App;

static class PictureView
{
    public static Avalonia.Media.Imaging.Bitmap ToBitmap(RasterImage img, int maxSide = 700)
    {
        using var ms = new MemoryStream(img.EncodePng(maxSide));
        return new Avalonia.Media.Imaging.Bitmap(ms);
    }

    public static Control Pane(string title, Image img) => new DockPanel
    {
        Children =
        {
            new TextBlock { Text = title, Classes = { "h" }, [DockPanel.DockProperty] = Dock.Top },
            new Border { Background = Brush("Bg0"), CornerRadius = new(6), Padding = new(8), Child = img },
        },
    };
}

/// <summary>Picture filters (greyscale, saturation, sketch, emboss, comic, black and white, grid) with a before/after preview.</summary>
public sealed class PictureFilterWindow : Window
{
    static readonly (PictureFilter Filter, string Name, string Amount, double Default)[] Filters =
    [
        (PictureFilter.Greyscale, "Greyscale", "", 0),
        (PictureFilter.Saturation, "Saturation", "Less ← colour → more", 0.4),
        (PictureFilter.Sketch, "Pencil sketch", "Line width", 0.4),
        (PictureFilter.Emboss, "Emboss", "Strength", 0.3),
        (PictureFilter.Comic, "Comic", "Tones", 0.2),
        (PictureFilter.BlackAndWhite, "Black and white", "Threshold", 0.5),
        (PictureFilter.Grid, "Mosaic grid", "Tile size", 0.12),
    ];

    readonly RasterImage _src;
    readonly ComboBox _filter = new() { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = Filters.Select(f => f.Name).ToList(), SelectedIndex = 0 };
    readonly Slider _amount = new() { Minimum = -1, Maximum = 1 };
    readonly TextBlock _amountLabel = new() { Classes = { "muted" } };
    readonly Image _before = new() { Stretch = Stretch.Uniform }, _after = new() { Stretch = Stretch.Uniform };
    RasterImage? _result;

    /// <summary>The filtered picture, or null if cancelled.</summary>
    public RasterImage? Result { get; private set; }

    public PictureFilterWindow(RasterImage src)
    {
        // Work on a smaller copy for the preview; the full picture is filtered on Apply.
        _src = src;
        Title = "Picture filters";
        Width = 1000;
        Height = 620;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");
        _before.Source = PictureView.ToBitmap(src);
        _filter.SelectionChanged += (_, _) => { _amount.Value = Filters[Math.Max(0, _filter.SelectedIndex)].Default; Update(); };
        _amount.ValueChanged += (_, _) => Update();
        var apply = new Button { Content = "Apply", Classes = { "primary" }, IsDefault = true, MinWidth = 100 };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 90 };
        apply.Click += async (_, _) =>
        {
            var f = Filters[Math.Max(0, _filter.SelectedIndex)];
            double a = _amount.Value;
            Result = await Task.Run(() => PictureTools.Apply(_src, f.Filter, a));
            Close();
        };
        cancel.Click += (_, _) => Close();
        var left = new StackPanel
        {
            Width = 260, Spacing = 8,
            Children =
            {
                Field("Filter", _filter),
                _amountLabel, _amount,
                Hint("Makes a new picture: Ctrl+Z brings the old one back. Brightness, contrast and sharpening stay in the picture's settings."),
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { cancel, apply } },
            },
        };
        var panes = new Grid { ColumnDefinitions = new("*,12,*") };
        panes.Children.Add(PictureView.Pane("BEFORE", _before));
        panes.Children.Add(PictureView.Pane("AFTER", _after).With(p => Grid.SetColumn(p, 2)));
        var grid = new Grid { ColumnDefinitions = new("Auto,16,*") };
        grid.Children.Add(left);
        grid.Children.Add(panes.With(p => Grid.SetColumn(p, 2)));
        Content = new Border { Padding = new Thickness(16), Child = grid };
        _amount.Value = Filters[0].Default;
        Update();
    }

    async void Update()
    {
        var f = Filters[Math.Max(0, _filter.SelectedIndex)];
        _amountLabel.Text = f.Amount;
        _amountLabel.IsVisible = _amount.IsVisible = f.Amount.Length > 0;
        _amount.Minimum = f.Filter == PictureFilter.Saturation ? -1 : 0;
        double a = _amount.Value;
        var small = _src.Width > 900 || _src.Height > 900 ? RasterImage.Decode(_src.EncodePng(900)) : _src;
        // The mosaic's tiles are in pixels: smaller in the reduced preview, so it looks as it will.
        double shown = f.Filter == PictureFilter.Grid ? a * small.Width / Math.Max(1, _src.Width) : a;
        var r = await Task.Run(() => PictureTools.Apply(small, f.Filter, shown));
        _result = r;
        var old = _after.Source as IDisposable;
        _after.Source = PictureView.ToBitmap(r);
        old?.Dispose();
    }
}

/// <summary>Crop a picture: a shape (free, square, 4:3, 3:2, 16:9) and where, in % of the picture.</summary>
public sealed class CropWindow : Window
{
    static readonly (string Name, double Ratio)[] Ratios = [("Free", 0), ("Square 1:1", 1), ("4:3", 4.0 / 3), ("3:4", 3.0 / 4), ("3:2", 1.5), ("2:3", 2.0 / 3), ("16:9", 16.0 / 9)];
    readonly RasterImage _src;
    readonly ComboBox _ratio = new() { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = Ratios.Select(r => r.Name).ToList(), SelectedIndex = 0 };
    readonly NumericUpDown _l = Number(0, 99, 1, "0.#"), _t = Number(0, 99, 1, "0.#"), _w = Number(1, 100, 1, "0.#"), _h = Number(1, 100, 1, "0.#");
    readonly Image _preview = new() { Stretch = Stretch.Uniform };
    readonly Border _box = new() { BorderBrush = Brushes.Orange, BorderThickness = new(2), Background = new SolidColorBrush(Color.FromArgb(40, 255, 165, 0)) };
    readonly Canvas _overlay = new();
    bool _setting;

    /// <summary>The crop in pixels (x, y, width, height), or null if cancelled.</summary>
    public (int X, int Y, int W, int H)? Result { get; private set; }

    public CropWindow(RasterImage src)
    {
        _src = src;
        Title = "Crop picture";
        Width = 860;
        Height = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");
        _preview.Source = PictureView.ToBitmap(src);
        _l.Value = 0; _t.Value = 0; _w.Value = 100; _h.Value = 100;
        foreach (var n in new[] { _l, _t, _w, _h }) n.ValueChanged += (_, _) => Changed(n);
        _ratio.SelectionChanged += (_, _) => Changed(_w);
        var ok = new Button { Content = "Crop", Classes = { "primary" }, IsDefault = true, MinWidth = 100 };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 90 };
        ok.Click += (_, _) =>
        {
            var (x, y, w, h) = Pixels();
            Result = (x, y, w, h);
            Close();
        };
        cancel.Click += (_, _) => Close();
        _overlay.Children.Add(_box);
        var stage = new Grid { Children = { _preview, _overlay } };
        stage.SizeChanged += (_, _) => Draw();
        var left = new StackPanel
        {
            Width = 240, Spacing = 8,
            Children =
            {
                Field("Shape", _ratio),
                // Two to a row: names above their boxes (beside them they don't fit).
                Row(Labelled("Left %", _l), Labelled("Top %", _t)),
                Row(Labelled("Width %", _w), Labelled("Height %", _h)),
                Hint("The cropped picture keeps its scale: it gets smaller on the work area by the part cut away."),
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { cancel, ok } },
            },
        };
        var grid = new Grid { ColumnDefinitions = new("Auto,16,*") };
        grid.Children.Add(left);
        grid.Children.Add(new Border { Background = Brush("Bg0"), CornerRadius = new(6), Padding = new(8), Child = stage }.With(b => Grid.SetColumn(b, 2)));
        Content = new Border { Padding = new Thickness(16), Child = grid };
    }

    (int X, int Y, int W, int H) Pixels()
    {
        double l = (double)(_l.Value ?? 0) / 100, t = (double)(_t.Value ?? 0) / 100, w = (double)(_w.Value ?? 100) / 100, h = (double)(_h.Value ?? 100) / 100;
        int x = (int)Math.Round(l * _src.Width), y = (int)Math.Round(t * _src.Height);
        int pw = Math.Max(1, Math.Min(_src.Width - x, (int)Math.Round(w * _src.Width))), ph = Math.Max(1, Math.Min(_src.Height - y, (int)Math.Round(h * _src.Height)));
        return (x, y, pw, ph);
    }

    void Changed(NumericUpDown which)
    {
        if (_setting) return;
        _setting = true;
        try
        {
            double ratio = Ratios[Math.Max(0, _ratio.SelectedIndex)].Ratio;
            if (ratio > 0)
            {
                // Keep the chosen shape: the other side follows (in pixels, so it's true).
                double w = (double)(_w.Value ?? 100) / 100 * _src.Width, h = (double)(_h.Value ?? 100) / 100 * _src.Height;
                if (which == _h) w = h * ratio; else h = w / ratio;
                if (w > _src.Width) { w = _src.Width; h = w / ratio; }
                if (h > _src.Height) { h = _src.Height; w = h * ratio; }
                _w.Value = (decimal)Math.Round(w / _src.Width * 100, 1);
                _h.Value = (decimal)Math.Round(h / _src.Height * 100, 1);
            }
            _l.Value = Math.Min(_l.Value ?? 0, 100 - (_w.Value ?? 100));
            _t.Value = Math.Min(_t.Value ?? 0, 100 - (_h.Value ?? 100));
        }
        finally { _setting = false; }
        Draw();
    }

    void Draw()
    {
        var size = _overlay.Bounds.Size;
        if (size.Width < 2) return;
        double k = Math.Min(size.Width / _src.Width, size.Height / _src.Height);
        double ox = (size.Width - _src.Width * k) / 2, oy = (size.Height - _src.Height * k) / 2;
        var (x, y, w, h) = Pixels();
        Canvas.SetLeft(_box, ox + x * k);
        Canvas.SetTop(_box, oy + y * k);
        _box.Width = w * k;
        _box.Height = h * k;
    }
}

/// <summary>A 3D model (STL) as a depth map for relief engraving.</summary>
public sealed class StlWindow : Window
{
    readonly ComboBox _view = new() { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = new[] { "From the top", "From the front", "From the side" }, SelectedIndex = 0 };
    readonly NumericUpDown _res = Number(100, 4000, 100, "0"), _size = Number(2, 220, 1, "0.#");
    readonly CheckBox _around = new() { Content = "Carve around the model (it stands proud)" };
    readonly Image _preview = new() { Stretch = Stretch.Uniform };
    readonly TextBlock _info = Hint();
    List<StlDepth.Triangle>? _tris;
    RasterImage? _depth;

    /// <summary>The depth map and its size on the work area (mm), or null.</summary>
    public (RasterImage Depth, Vec2 SizeMm, string Name)? Result { get; private set; }
    string _name = "Model";

    public StlWindow()
    {
        Title = "3D model to relief";
        Width = 820;
        Height = 540;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");
        _res.Value = 800;
        _size.Value = 60;
        ToolTip.SetTip(_res, "Pixels along the depth map's longer side. 10 per mm is plenty for most reliefs.");
        ToolTip.SetTip(_size, "How big the relief is on the work area, along its longer side.");
        var open = Button("Open STL…", tip: "A 3D model saved as STL (binary or text), e.g. from a 3D design program or a model site.");
        open.Click += async (_, _) => await Open();
        foreach (var n in new[] { _res, _size }) n.ValueChanged += (_, _) => Render();
        _view.SelectionChanged += (_, _) => Render();
        _around.IsCheckedChanged += (_, _) => Render();
        var add = new Button { Content = "Add to design", Classes = { "primary" }, IsDefault = true, MinWidth = 120 };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 90 };
        add.Click += (_, _) =>
        {
            if (_depth is null) return;
            double mm = (double)(_size.Value ?? 60), k = mm / Math.Max(_depth.Width, _depth.Height);
            Result = (_depth, new Vec2(_depth.Width * k, _depth.Height * k), _name);
            Close();
        };
        cancel.Click += (_, _) => Close();
        var left = new StackPanel
        {
            Width = 260, Spacing = 8,
            Children =
            {
                open, Field("View", _view), Field("Pixels", _res), Field("Size (mm)", _size), _around, _info,
                Hint("The highest point of the model is the surface; the lowest is the deepest. It goes on a depth-slices layer: set the slices, power and passes there."),
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { cancel, add } },
            },
        };
        var grid = new Grid { ColumnDefinitions = new("Auto,16,*") };
        grid.Children.Add(left);
        grid.Children.Add(PictureView.Pane("DEPTH MAP", _preview).With(p => Grid.SetColumn(p, 2)));
        Content = new Border { Padding = new Thickness(16), Child = grid };
    }

    async Task Open()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "3D model",
            FileTypeFilter = [new FilePickerFileType("3D models (STL, OBJ, STEP)") { Patterns = ["*.stl", "*.obj", "*.stp", "*.step"] }],
        });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path) return;
        try
        {
            _tris = await Task.Run(() => StlDepth.Load(path));
            _name = Path.GetFileNameWithoutExtension(path);
            Render();
        }
        catch (Exception e) when (e is IOException or FormatException) { _info.Text = $"Couldn't read it: {e.Message}"; }
    }

    async void Render()
    {
        if (_tris is not { } tris) return;
        var view = (StlView)Math.Max(0, _view.SelectedIndex);
        int px = (int)(_res.Value ?? 800);
        bool around = _around.IsChecked == true;
        var (depth, w, h, range) = await Task.Run(() => StlDepth.Render(tris, view, px, around));
        _depth = depth;
        var old = _preview.Source as IDisposable;
        _preview.Source = PictureView.ToBitmap(depth);
        old?.Dispose();
        _info.Text = $"{tris.Count:N0} triangles; the model is {w:0.#} × {h:0.#} × {range:0.#} (its own units).";
    }
}

/// <summary>A depth map seen as the relief it will make, lit from the top left.</summary>
public sealed class ReliefPreviewWindow : Window
{
    public ReliefPreviewWindow(ImageShape picture)
    {
        Title = "Relief preview";
        Width = 760;
        Height = 640;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");
        var img = new Image { Stretch = Stretch.Uniform };
        var depth = new Slider { Minimum = 0.2, Maximum = 5, Value = 1 };
        var src = picture.Source.Pixels.Width > 1200 || picture.Source.Pixels.Height > 1200 ? RasterImage.Decode(picture.Source.Pixels.EncodePng(1200)) : picture.Source.Pixels;
        double mmPerPx = picture.SizeMm.X / src.Width;
        async void Draw()
        {
            double d = depth.Value;
            var shaded = await Task.Run(() => PictureTools.ShadeRelief(src, d, mmPerPx));
            var old = img.Source as IDisposable;
            img.Source = PictureView.ToBitmap(shaded, 1200);
            old?.Dispose();
        }
        depth.ValueChanged += (_, _) => Draw();
        var top = new StackPanel { Spacing = 4, Children = { new TextBlock { Text = "Depth (mm): how deep the deepest part is carved", Classes = { "muted" } }, depth } };
        DockPanel.SetDock(top, Dock.Top);
        Content = new DockPanel { Margin = new Thickness(14), Children = { top, new Border { Background = Brush("Bg0"), CornerRadius = new(6), Padding = new(8), Margin = new(0, 8, 0, 0), Child = img } } };
        Draw();
    }
}
