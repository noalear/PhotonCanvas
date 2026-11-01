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

using PhotonCanvas.App;

namespace PhotonCanvas.Addon.Relief;

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
