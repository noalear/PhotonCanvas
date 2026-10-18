using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using PhotonCanvas.Core;
using PhotonCanvas.Core.Imaging;
using PhotonCanvas.Doc;
using Bitmap = Avalonia.Media.Imaging.Bitmap;

namespace PhotonCanvas.App;

/// <summary>A small drawing of one shape (its outline in the layer colour, filled on engrave layers; pictures as themselves).</summary>
public sealed class ShapeThumb : Control
{
    readonly Shape _shape;
    readonly Color _color;
    readonly bool _filled;
    StreamGeometry? _geometry;
    Rect2 _bounds;

    static readonly ConditionalWeakTable<ImageSource, Bitmap> Pictures = new();

    public ShapeThumb(Shape shape, Color color, bool filled, double width = 44, double height = 26)
    {
        _shape = shape;
        _color = color;
        _filled = filled;
        Width = width;
        Height = height;
        ClipToBounds = true;
    }

    public override void Render(DrawingContext ctx)
    {
        var box = new Rect(0, 0, Bounds.Width, Bounds.Height);
        ctx.DrawRectangle(UiKit.Brush("Bg0"), null, box, 3, 3);
        const double Pad = 2;
        if (_shape is ImageShape im)
        {
            var bmp = Pictures.GetValue(im.Source, s => Small(s.Pixels));
            double sc = Math.Min((box.Width - 2 * Pad) / bmp.PixelSize.Width, (box.Height - 2 * Pad) / bmp.PixelSize.Height);
            double w = bmp.PixelSize.Width * sc, h = bmp.PixelSize.Height * sc;
            ctx.DrawImage(bmp, new Rect((box.Width - w) / 2, (box.Height - h) / 2, w, h));
            return;
        }
        if (_shape is not PathShape ps) return;
        if (_geometry is null)
        {
            _bounds = ps.Bounds;
            if (_bounds.IsEmpty) return;
            double size = Math.Max(_bounds.Width, _bounds.Height);
            _geometry = new StreamGeometry();
            using var c = _geometry.Open();
            c.SetFillRule(FillRule.EvenOdd);
            foreach (var f in ps.World.Flatten(Math.Max(0.01, size / 200)))
            {
                if (f.Points.Count < 2) continue;
                c.BeginFigure(new Point(f.Points[0].X, f.Points[0].Y), _filled && f.Closed);
                for (int i = 1; i < f.Points.Count; i++) c.LineTo(new Point(f.Points[i].X, f.Points[i].Y));
                c.EndFigure(f.Closed);
            }
        }
        if (_bounds.IsEmpty) return;
        // Fit and centre; a straight line (no height) still gets a sensible scale.
        double s = Math.Min((box.Width - 2 * Pad) / Math.Max(_bounds.Width, 1e-6), (box.Height - 2 * Pad) / Math.Max(_bounds.Height, 1e-6));
        double ox = (box.Width - _bounds.Width * s) / 2 - _bounds.MinX * s, oy = (box.Height - _bounds.Height * s) / 2 - _bounds.MinY * s;
        using (ctx.PushTransform(new Matrix(s, 0, 0, s, ox, oy)))
        {
            var fill = _filled ? new SolidColorBrush(Color.FromArgb(150, _color.R, _color.G, _color.B)) : null;
            ctx.DrawGeometry(fill, new Pen(new SolidColorBrush(_color), 1.2 / s), _geometry);
        }
    }

    /// <summary>A copy at most 64 px across, for thumbnails.</summary>
    static Bitmap Small(RasterImage src)
    {
        double k = Math.Max(1, Math.Max(src.Width, src.Height) / 64.0);
        int w = Math.Max(1, (int)(src.Width / k)), h = Math.Max(1, (int)(src.Height / k));
        var rgba = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int sx = Math.Min(src.Width - 1, (int)(x * k)), sy = Math.Min(src.Height - 1, (int)(y * k));
                Buffer.BlockCopy(src.Rgba, (sy * src.Width + sx) * 4, rgba, (y * w + x) * 4, 4);
            }
        return CameraImaging.ToAvaloniaRgba(new RasterImage(w, h, rgba));
    }
}
