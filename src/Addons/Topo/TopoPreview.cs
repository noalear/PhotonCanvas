using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Bitmap = Avalonia.Media.Imaging.Bitmap;
using Avalonia.Platform;
using PhotonCanvas.Core;
using static PhotonCanvas.App.UiKit;

namespace PhotonCanvas.Addon.Topo;

/// <summary>Pictures of the ground: shaded relief of all the heights, and the map's layers in colour.</summary>
static class TopoPictures
{
    /// <summary>Shaded relief (light from the north-west) of the whole grid, at most <paramref name="maxSide"/> pixels a side; BGRA.</summary>
    public static (int W, int H, byte[] Px) Ground(DemGrid g, int maxSide)
    {
        double k = Math.Min(1, maxSide / (double)Math.Max(g.Width, g.Height));
        int w = Math.Max(2, (int)(g.Width * k)), h = Math.Max(2, (int)(g.Height * k));
        double sx = g.WidthM / (w - 1), sy = g.HeightM / (h - 1);
        var z = new double[w * h];
        Parallel.For(0, h, y => { for (int x = 0; x < w; x++) z[y * w + x] = g.Sample(x * sx, y * sy); });
        var px = new byte[w * h * 4];
        double zf = g.Units == HeightUnits.Feet ? 0.3048 : 1;
        if (!g.Georeferenced || g.Units == HeightUnits.Levels)
        {
            // Unknown sizes: make the relief show anyway (its steepest a tenth as tall as it is wide).
            var (lo, hi) = g.Range();
            zf = hi > lo ? 0.1 * Math.Max(g.WidthM, g.HeightM) / (hi - lo) : 1;
        }
        Parallel.For(0, h, y =>
        {
            for (int x = 0; x < w; x++)
            {
                double c = z[y * w + x];
                int o = (y * w + x) * 4;
                if (double.IsNaN(c)) { px[o + 3] = 0; continue; }
                double Z(int xx, int yy) { var v = z[Math.Clamp(yy, 0, h - 1) * w + Math.Clamp(xx, 0, w - 1)]; return double.IsNaN(v) ? c : v; }
                double shade = Shade((Z(x + 1, y) - Z(x - 1, y)) * zf / (2 * sx), (Z(x, y + 1) - Z(x, y - 1)) * zf / (2 * sy));
                byte v8 = (byte)Math.Clamp(60 + 180 * shade, 0, 255);
                px[o] = (byte)(v8 * 0.92); px[o + 1] = (byte)(v8 * 0.96); px[o + 2] = v8; px[o + 3] = 255;
            }
        });
        return (w, h, px);
    }

    /// <summary>0 (in shadow) to 1 (facing the light), light from the north-west, 45° up; slopes as rise per metre east and south.</summary>
    static double Shade(double dzdx, double dzdy)
    {
        // Normal (−dz/dx, dz/dy going north, 1); light from the north-west and up.
        double nx = -dzdx, ny = dzdy, nz = 1, l = Math.Sqrt(nx * nx + ny * ny + 1);
        double lx = -0.5, ly = 0.5, lz = 0.7071;
        return Math.Max(0, (nx * lx + ny * ly + nz * lz) / l);
    }

    static readonly (double At, byte R, byte G, byte B)[] Stops =
    [
        (0.00, 0x5c, 0x8f, 0x5a), (0.30, 0xa9, 0xb8, 0x6a), (0.55, 0xc9, 0xa8, 0x6a),
        (0.80, 0x9c, 0x70, 0x4c), (0.92, 0xc8, 0xbf, 0xb4), (1.00, 0xf6, 0xf4, 0xf0),
    ];

    /// <summary>The colour of a land layer (1 to <paramref name="layers"/>), low green to high white; the base is blue-grey.</summary>
    public static (byte R, byte G, byte B) LayerColour(int layer, int layers)
    {
        if (layer <= 0) return (0x4d, 0x78, 0x96);
        double t = layers <= 1 ? 1 : (layer - 1) / (double)(layers - 1);
        for (int i = 1; i < Stops.Length; i++)
            if (t <= Stops[i].At)
            {
                var (a, b) = (Stops[i - 1], Stops[i]);
                double f = (t - a.At) / (b.At - a.At);
                return ((byte)(a.R + (b.R - a.R) * f), (byte)(a.G + (b.G - a.G) * f), (byte)(a.B + (b.B - a.B) * f));
            }
        return (Stops[^1].R, Stops[^1].G, Stops[^1].B);
    }

    /// <summary>The map as it will be stacked: each point the colour of its top layer, shaded, a dark line where the layers change; transparent outside the map's shape. BGRA, Nx × Ny.</summary>
    public static byte[] Layers(TopoSurface s)
    {
        int w = s.Nx, h = s.Ny;
        var band = new int[w * h];
        for (int i = 0; i < band.Length; i++) band[i] = s.LayerAt(s.Heights[i]);
        double mmx = s.Options.WidthMm / (w - 1), mmy = s.Options.ModelDepthMm / (h - 1);
        var px = new byte[w * h * 4];
        Parallel.For(0, h, y =>
        {
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x, o = i * 4;
                if (!s.Inside[i]) continue;
                double Z(int xx, int yy) => s.ZOf(s.Heights[Math.Clamp(yy, 0, h - 1) * w + Math.Clamp(xx, 0, w - 1)]);
                // Shaded as the model will be (its own height scale), softly.
                double shade = Shade((Z(x + 1, y) - Z(x - 1, y)) / (2 * mmx), (Z(x, y + 1) - Z(x, y - 1)) / (2 * mmy));
                var (r, g, b) = LayerColour(band[i], s.Options.Layers);
                bool edge = x + 1 < w && band[i + 1] != band[i] || y + 1 < h && band[i + w] != band[i];
                double k = edge ? 0.45 : 0.75 + 0.35 * shade;
                px[o] = (byte)Math.Min(255, b * k); px[o + 1] = (byte)Math.Min(255, g * k); px[o + 2] = (byte)Math.Min(255, r * k); px[o + 3] = 255;
            }
        });
        return px;
    }

    public static WriteableBitmap Bitmap(int w, int h, byte[] bgra)
    {
        var wb = new WriteableBitmap(new PixelSize(w, h), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        using var fb = wb.Lock();
        for (int y = 0; y < h; y++)
            System.Runtime.InteropServices.Marshal.Copy(bgra, y * w * 4, fb.Address + y * fb.RowBytes, w * 4);
        return wb;
    }
}

/// <summary>
/// The ground with the map's area on it: drag the area to move it, the wheel to make it bigger or
/// smaller. Heights from a file keep the area inside the file; downloaded ones can go anywhere
/// (the window fetches the ground around it when you let go).
/// </summary>
sealed class TopoPreview : Control
{
    Bitmap? _ground;
    double _gridW = 1, _gridH = 1;
    Bitmap? _layers;
    TopoArea _area;
    IReadOnlyList<Vec2>? _shape; // 0..1 across the area, Y down
    Point? _dragFrom;
    TopoArea _dragArea;

    public string? Message { get => _message; set { _message = value; InvalidateVisual(); } }
    string? _message;
    /// <summary>The area may go off the ground shown (downloaded heights: more are fetched).</summary>
    public bool Roams { get; set; }
    public TopoArea Area => _area;
    /// <summary>The area moved (true: the drag or wheel is over).</summary>
    public event Action<bool>? AreaChanged;

    public TopoPreview()
    {
        ClipToBounds = true;
        Cursor = new Cursor(StandardCursorType.SizeAll);
        ToolTip.SetTip(this, "Drag the map's outline to move it; the mouse wheel makes the area bigger or smaller.");
    }

    public void ShowGround(Bitmap? ground, double widthM, double heightM)
    {
        var old = _ground;
        _ground = ground;
        _gridW = Math.Max(1e-6, widthM);
        _gridH = Math.Max(1e-6, heightM);
        if (!ReferenceEquals(old, ground)) old?.Dispose();
        InvalidateVisual();
    }

    public void ShowLayers(Bitmap? layers)
    {
        var old = _layers;
        _layers = layers;
        if (!ReferenceEquals(old, layers)) old?.Dispose();
        InvalidateVisual();
    }

    public void SetArea(TopoArea a, IReadOnlyList<Vec2>? shape)
    {
        _area = a;
        _shape = shape;
        InvalidateVisual();
    }

    (double S, double Ox, double Oy) View()
    {
        double pad = 12;
        double s = Math.Min((Bounds.Width - 2 * pad) / _gridW, (Bounds.Height - 2 * pad) / _gridH);
        if (!(s > 0)) s = 1e-6;
        return (s, (Bounds.Width - _gridW * s) / 2, (Bounds.Height - _gridH * s) / 2);
    }

    Rect AreaRect()
    {
        var (s, ox, oy) = View();
        return new Rect(ox + _area.Left * s, oy + _area.Top * s, _area.WidthM * s, _area.HeightM * s);
    }

    public override void Render(DrawingContext dc)
    {
        dc.FillRectangle(Brush("Bg2"), new Rect(Bounds.Size));
        var (s, ox, oy) = View();
        var groundRect = new Rect(ox, oy, _gridW * s, _gridH * s);
        if (_ground is not null)
            using (dc.PushOpacity(0.55))
                dc.DrawImage(_ground, new Rect(_ground.Size), groundRect);
        if (_shape is { Count: >= 3 } shape && _area.WidthM > 0)
        {
            var r = AreaRect();
            if (_layers is not null) dc.DrawImage(_layers, new Rect(_layers.Size), r);
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                g.BeginFigure(new Point(r.X + shape[0].X * r.Width, r.Y + shape[0].Y * r.Height), false);
                foreach (var p in shape.Skip(1)) g.LineTo(new Point(r.X + p.X * r.Width, r.Y + p.Y * r.Height));
                g.EndFigure(true);
            }
            dc.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)), 3.5), geo);
            dc.DrawGeometry(null, new Pen(Brushes.White, 1.5), geo);
        }
        if (_message is { Length: > 0 } m)
        {
            var ft = new FormattedText(m, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 14, Brush("TextMain"))
            {
                MaxTextWidth = Math.Max(50, Bounds.Width - 60),
                TextAlignment = TextAlignment.Center,
            };
            var at = new Point((Bounds.Width - ft.Width) / 2, (Bounds.Height - ft.Height) / 2);
            dc.DrawRectangle(Brush("Bg1"), null, new Rect(at.X - 12, at.Y - 8, ft.Width + 24, ft.Height + 16), 8, 8);
            dc.DrawText(ft, at);
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_shape is null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _dragFrom = e.GetPosition(this);
        _dragArea = _area;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragFrom is not { } from) return;
        var (s, _, _) = View();
        var d = e.GetPosition(this) - from;
        _area = Keep(_dragArea with { X = _dragArea.X + d.X / s, Y = _dragArea.Y + d.Y / s });
        InvalidateVisual();
        AreaChanged?.Invoke(false);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragFrom is null) return;
        _dragFrom = null;
        e.Pointer.Capture(null);
        AreaChanged?.Invoke(true);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (_shape is null) return;
        double k = Math.Pow(1.15, -e.Delta.Y);
        _area = Keep(_area with { WidthM = Math.Max(100, _area.WidthM * k), HeightM = Math.Max(100 * _area.HeightM / Math.Max(1e-9, _area.WidthM), _area.HeightM * k) });
        InvalidateVisual();
        AreaChanged?.Invoke(true);
        e.Handled = true;
    }

    /// <summary>A file's heights: the area stays on them (shrunk to fit if it must).</summary>
    TopoArea Keep(TopoArea a)
    {
        if (Roams) return a;
        double k = Math.Min(1, Math.Min(_gridW / a.WidthM, _gridH / a.HeightM));
        double w = a.WidthM * k, h = a.HeightM * k;
        return new TopoArea(Math.Clamp(a.X, w / 2, _gridW - w / 2), Math.Clamp(a.Y, h / 2, _gridH - h / 2), w, h);
    }
}
