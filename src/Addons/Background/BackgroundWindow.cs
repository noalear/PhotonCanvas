using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using PhotonCanvas.Core.Imaging;
using static PhotonCanvas.App.UiKit;

using PhotonCanvas.App;

namespace PhotonCanvas.Addon.Background;

/// <summary>
/// Remove a picture's background: the plain backdrop found around its edges, plus any colours
/// clicked on the preview. Previews on a checkerboard at reduced size; applies at full size.
/// </summary>
public sealed class BackgroundWindow : Window
{
    const int PreviewSide = 900;
    readonly RasterImage _full;
    readonly RasterImage _small;
    readonly double _toFull;
    readonly Action<RasterImage> _apply;
    readonly List<(int X, int Y)> _picks = [];

    readonly CheckBox _edges = new() { Content = "Remove the background around the edges", IsChecked = true };
    readonly Slider _tolerance = new() { Minimum = 1, Maximum = 100, Value = 20 };
    readonly Slider _shrink = new() { Minimum = 0, Maximum = 10, Value = 1, SmallChange = 1, IsSnapToTickEnabled = true, TickFrequency = 1 };
    readonly Slider _feather = new() { Minimum = 0, Maximum = 6, Value = 1, SmallChange = 1, IsSnapToTickEnabled = true, TickFrequency = 1 };
    readonly ComboBox _clickMode = new() { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = new[] { "the area clicked", "that colour everywhere" }, SelectedIndex = 0 };
    readonly Image _view = new() { Stretch = Stretch.Uniform, Cursor = new Cursor(StandardCursorType.Cross) };
    readonly TextBlock _status = Hint();
    int _ticket;

    public BackgroundWindow(RasterImage picture, Action<RasterImage> apply)
    {
        _full = picture;
        _apply = apply;
        double k = Math.Max(1, Math.Max(picture.Width, picture.Height) / (double)PreviewSide);
        _small = k > 1 ? Downscale(picture, (int)(picture.Width / k), (int)(picture.Height / k)) : picture;
        _toFull = (double)picture.Width / _small.Width;
        Title = "Remove background";
        Width = 1000;
        Height = 700;
        MinWidth = 700;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");

        foreach (var s in new[] { _tolerance, _shrink, _feather }) s.ValueChanged += (_, _) => Schedule();
        _edges.IsCheckedChanged += (_, _) => Schedule();
        _clickMode.SelectionChanged += (_, _) => Schedule();
        _view.PointerPressed += (_, e) => { Pick(e.GetPosition(_view)); e.Handled = true; };
        var undo = Button("Undo last click");
        undo.Click += (_, _) => { if (_picks.Count > 0) { _picks.RemoveAt(_picks.Count - 1); Schedule(); } };
        var clear = Button("Clear clicks");
        clear.Click += (_, _) => { _picks.Clear(); Schedule(); };
        var ok = new Button { Content = "Apply", Classes = { "primary" }, IsDefault = true, MinWidth = 110 };
        ok.Click += async (_, _) => await Apply(ok);
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 100 };
        cancel.Click += (_, _) => Close();

        var controls = new StackPanel
        {
            Spacing = 10,
            Width = 300,
            Margin = new(0, 0, 16, 0),
            Children =
            {
                new TextBlock { TextWrapping = TextWrapping.Wrap, Text = "Removed areas become see-through and are never engraved. Click the preview to remove a colour too." },
                _edges,
                Labeled("Tolerance", _tolerance, "How different a colour can be and still count as background. Raise it if bits of backdrop remain; lower it if the subject's edges get eaten."),
                Field("Clicks remove", _clickMode),
                Row(undo, clear),
                Labeled("Shrink edge", _shrink, "Pull the kept edge in by this many pixels, to drop a halo of backdrop colour."),
                Labeled("Soften edge", _feather, "Feather the cut over this many pixels."),
                _status,
            },
        };
        var checker = new Border { Background = Checkerboard(), CornerRadius = new(6), ClipToBounds = true, Child = _view };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = new(0, 12, 0, 0), Children = { cancel, ok } };
        DockPanel.SetDock(buttons, Dock.Bottom);
        DockPanel.SetDock(controls, Dock.Left);
        Content = new Border { Padding = new Thickness(16), Child = new DockPanel { Children = { buttons, controls, checker } } };
        Schedule();
    }

    static Control Labeled(string label, Slider s, string tip)
    {
        var value = new TextBlock { MinWidth = 30, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        void Show() => value.Text = $"{s.Value:0}";
        s.ValueChanged += (_, _) => Show();
        Show();
        var g = new Grid { ColumnDefinitions = new("100,*,Auto") };
        g.Children.Add(new TextBlock { Text = label, Classes = { "muted" }, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(s, 1); g.Children.Add(s);
        Grid.SetColumn(value, 2); g.Children.Add(value);
        ToolTip.SetTip(g, tip);
        return g;
    }

    BackgroundOptions Options(double scale) => new()
    {
        FromEdges = _edges.IsChecked == true,
        Tolerance = _tolerance.Value,
        Picks = _picks.Select(p => ((int)(p.X * scale), (int)(p.Y * scale))).ToList(),
        PicksEverywhere = _clickMode.SelectedIndex == 1,
        Shrink = (int)Math.Round(_shrink.Value * scale),
        Feather = _feather.Value * scale,
    };

    void Pick(Point at)
    {
        if (_view.Bounds.Width <= 0) return;
        double s = Math.Min(_view.Bounds.Width / _small.Width, _view.Bounds.Height / _small.Height);
        double ox = (_view.Bounds.Width - _small.Width * s) / 2, oy = (_view.Bounds.Height - _small.Height * s) / 2;
        int x = (int)((at.X - ox) / s), y = (int)((at.Y - oy) / s);
        if (x < 0 || y < 0 || x >= _small.Width || y >= _small.Height) return;
        _picks.Add((x, y));
        Schedule();
    }

    void Schedule()
    {
        int ticket = ++_ticket;
        var o = Options(1);
        _ = Run();

        async Task Run()
        {
            await Task.Delay(120);
            if (ticket != _ticket) return;
            var result = await Task.Run(() => BackgroundRemoval.Apply(_small, BackgroundRemoval.Mask(_small, o)));
            if (ticket != _ticket) return;
            var old = _view.Source as IDisposable;
            _view.Source = CameraImaging.ToAvaloniaRgba(result);
            old?.Dispose();
            int kept = 0;
            for (int i = 3; i < result.Rgba.Length; i += 4) if (result.Rgba[i] > 127) kept++;
            _status.Text = $"Keeping {100.0 * kept / (result.Width * result.Height):0}% of the picture." + (_picks.Count > 0 ? $" {_picks.Count} colour click(s)." : "");
        }
    }

    async Task Apply(Button ok)
    {
        ok.IsEnabled = false;
        _status.Text = "Working at full size…";
        var o = Options(_toFull);
        var result = await Task.Run(() => BackgroundRemoval.Apply(_full, BackgroundRemoval.Mask(_full, o)));
        _apply(result);
        Close();
    }

    static IBrush Checkerboard()
    {
        var bmp = new WriteableBitmap(new PixelSize(16, 16), new Vector(96, 96), Avalonia.Platform.PixelFormat.Bgra8888, Avalonia.Platform.AlphaFormat.Opaque);
        using (var fb = bmp.Lock())
        {
            var px = new byte[16 * 16 * 4];
            for (int y = 0; y < 16; y++)
                for (int x = 0; x < 16; x++)
                {
                    byte v = (x < 8) ^ (y < 8) ? (byte)58 : (byte)44;
                    int o = (y * 16 + x) * 4;
                    px[o] = px[o + 1] = px[o + 2] = v; px[o + 3] = 255;
                }
            System.Runtime.InteropServices.Marshal.Copy(px, 0, fb.Address, px.Length);
        }
        return new ImageBrush(bmp) { TileMode = TileMode.Tile, DestinationRect = new RelativeRect(0, 0, 16, 16, RelativeUnit.Absolute) };
    }

    static RasterImage Downscale(RasterImage s, int w, int h)
    {
        var o = new byte[w * h * 4];
        double sx = (double)s.Width / w, sy = (double)s.Height / h;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int x0 = (int)(x * sx), y0 = (int)(y * sy);
                Buffer.BlockCopy(s.Rgba, (y0 * s.Width + x0) * 4, o, (y * w + x) * 4, 4);
            }
        return new RasterImage(w, h, o);
    }
}
