using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using PhotonCanvas.Core;
using PhotonCanvas.Doc.Geometry;
using SkiaSharp;

namespace PhotonCanvas.App;

public enum StackLook { Solid, Lines }

/// <summary>What the slices are cut from, for the solid preview: top colour, edge colour, and how the edge looks.</summary>
public enum StackMaterial { Basswood, BirchPlywood, Walnut, Mdf, Cardboard, WhiteAcrylic, BlackAcrylic }

/// <summary>
/// The slices stacked up in 3D, as they'll be glued: shaded solid in a chosen material (drawn with
/// a depth buffer, so nearer parts hide farther ones from any angle), or as see-through outlines.
/// A range of slices can be shown on its own, to look inside. Parts joined to nothing are red.
/// Drag to turn.
/// </summary>
public sealed class StackView : Control
{
    SliceStack? _stack;
    double _t = 3, _yaw = -30, _pitch = 30;
    Point? _drag;
    WriteableBitmap? _frame;

    public StackLook Look { get; set { field = value; InvalidateVisual(); } } = StackLook.Solid;
    public StackMaterial Material { get; set { field = value; InvalidateVisual(); } } = StackMaterial.Basswood;
    /// <summary>The first and last slice shown (1 = bottom).</summary>
    public int From { get; set { field = value; InvalidateVisual(); } } = 1;
    public int To { get; set { field = value; InvalidateVisual(); } } = int.MaxValue;

    public void Show(SliceStack stack, double thickness)
    {
        _stack = stack;
        _t = thickness;
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e) { _drag = e.GetPosition(this); e.Pointer.Capture(this); }
    protected override void OnPointerReleased(PointerReleasedEventArgs e) { _drag = null; e.Pointer.Capture(null); }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (_drag is not { } from) return;
        var p = e.GetPosition(this);
        _yaw += (p.X - from.X) * 0.5;
        _pitch = Math.Clamp(_pitch + (p.Y - from.Y) * 0.4, -60, 89);
        _drag = p;
        InvalidateVisual();
    }

    /// <summary>The view: turned by yaw, tipped by pitch; screen x, screen y (down), and depth (towards you).</summary>
    readonly record struct View(double Cx, double Cy, double Cz, double Scale, double W, double H, double Yaw, double Pitch)
    {
        public (double X, double Y, double D) Project(double x, double y, double z)
        {
            double dx = x - Cx, dy = y - Cy, dz = z - Cz;
            double rx = dx * Math.Cos(Yaw) - dy * Math.Sin(Yaw), ry = dx * Math.Sin(Yaw) + dy * Math.Cos(Yaw);
            return (W / 2 + rx * Scale, H / 2 + (ry * Math.Sin(Pitch) - dz * Math.Cos(Pitch)) * Scale, ry * Math.Cos(Pitch) + dz * Math.Sin(Pitch));
        }

        /// <summary>A direction in the model turned into the view (x right, y down, d towards you).</summary>
        public (double X, double Y, double D) Turn(double x, double y, double z)
        {
            double rx = x * Math.Cos(Yaw) - y * Math.Sin(Yaw), ry = x * Math.Sin(Yaw) + y * Math.Cos(Yaw);
            return (rx, ry * Math.Sin(Pitch) - z * Math.Cos(Pitch), ry * Math.Cos(Pitch) + z * Math.Sin(Pitch));
        }
    }

    View MakeView(SliceStack st, double w, double h)
    {
        double span = Math.Max(Math.Max(st.ModelWidthMm, st.ModelDepthMm), st.StackHeightMm);
        return new View(st.ModelWidthMm / 2, st.ModelDepthMm / 2, st.StackHeightMm / 2, Math.Min(w, h) * 0.78 / Math.Max(1, span), w, h, _yaw * Math.PI / 180, _pitch * Math.PI / 180);
    }

    IEnumerable<SlicePart> Shown(SliceStack st) => st.Parts.Where(p => p.Slice >= From && p.Slice <= Math.Min(To, st.Slices));

    public override void Render(DrawingContext ctx)
    {
        var b = Bounds;
        ctx.FillRectangle(UiKit.Brush("Bg0"), new Rect(b.Size));
        if (_stack is not { } st)
        {
            Caption(ctx, "Open a 3D model to see it in slices.", new Point(b.Width / 2, b.Height / 2), centre: true);
            return;
        }
        if (Look == StackLook.Solid) RenderSolid(ctx, st);
        else RenderLines(ctx, st);
        int last = Math.Min(To, st.Slices);
        Caption(ctx, (From > 1 || last < st.Slices ? $"Slices {From}–{last} of {st.Slices}" : $"{st.Slices} slices") + " · drag to turn", new Point(8, b.Height - 14));
    }

    static void Caption(DrawingContext ctx, string text, Point at, bool centre = false)
    {
        var ft = new FormattedText(text, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default), 11.5, UiKit.Brush("TextMuted"));
        ctx.DrawText(ft, new Point(centre ? at.X - ft.Width / 2 : at.X, at.Y - ft.Height / 2));
    }

    // ---- outlines --------------------------------------------------------------------------

    void RenderLines(DrawingContext ctx, SliceStack st)
    {
        var v = MakeView(st, Bounds.Width, Bounds.Height);
        Point P(Vec2 xy, double z) { var (x, y, _) = v.Project(xy.X, xy.Y, z); return new Point(x, y); }
        foreach (var part in Shown(st).OrderBy(p => p.Slice))
        {
            var shade = part.Floating ? Color.Parse("#E5484D") : part.Slice % 2 == 0 ? Color.Parse("#C9A27A") : Color.Parse("#A9825C");
            var pen = new Pen(new SolidColorBrush(shade), 1.2);
            var faint = new Pen(new SolidColorBrush(Color.FromArgb(90, shade.R, shade.G, shade.B)), 1);
            double z0 = (part.Slice - 1) * _t, z1 = part.Slice * _t;
            foreach (var l in part.Outline.Flatten(0.2).Where(f => f.Closed).Select(f => f.Points))
            {
                for (int k = 0; k < l.Count; k++)
                {
                    var a = l[k];
                    var c = l[(k + 1) % l.Count];
                    ctx.DrawLine(pen, P(a, z1), P(c, z1));
                    ctx.DrawLine(faint, P(a, z0), P(c, z0));
                }
                int step = Math.Max(1, l.Count / 12);
                for (int k = 0; k < l.Count; k += step) ctx.DrawLine(faint, P(l[k], z0), P(l[k], z1));
            }
        }
    }

    // ---- shaded solid ----------------------------------------------------------------------

    sealed record Look3(Color Top, Color Side, Color Glue, double Shine, int Plies);

    static Look3 Of(StackMaterial m) => m switch
    {
        StackMaterial.BirchPlywood => new(Color.Parse("#E7D2A6"), Color.Parse("#D8BF8E"), Color.Parse("#7A5E3C"), 0, 3),
        StackMaterial.Walnut => new(Color.Parse("#6E4B33"), Color.Parse("#5C3E2A"), Color.Parse("#3B2718"), 0.05, 0),
        StackMaterial.Mdf => new(Color.Parse("#B9996F"), Color.Parse("#8F7455"), Color.Parse("#5E4B36"), 0, 0),
        StackMaterial.Cardboard => new(Color.Parse("#C9A777"), Color.Parse("#B8925F"), Color.Parse("#7D6142"), 0, 2),
        StackMaterial.WhiteAcrylic => new(Color.Parse("#F4F4F2"), Color.Parse("#E2E3E0"), Color.Parse("#BFC1BD"), 0.6, 0),
        StackMaterial.BlackAcrylic => new(Color.Parse("#26272A"), Color.Parse("#1D1E21"), Color.Parse("#0E0F11"), 0.7, 0),
        _ => new(Color.Parse("#EAD7AF"), Color.Parse("#DCC294"), Color.Parse("#9C7F57"), 0, 0),
    };

    void RenderSolid(DrawingContext ctx, SliceStack st)
    {
        int w = Math.Max(1, (int)Bounds.Width), h = Math.Max(1, (int)Bounds.Height);
        var v = MakeView(st, w, h);
        var px = new uint[w * h];
        var depth = new float[w * h];
        Array.Fill(depth, float.NegativeInfinity);
        var look = Of(Material);
        // Light from the upper left, in front; a little always (so nothing is black).
        var light = Norm(-0.45, -0.75, 0.55);
        double Lit((double X, double Y, double D) n, out double spec)
        {
            double lam = Math.Max(0, n.X * light.X + n.Y * light.Y + n.D * light.D);
            var half = Norm(light.X, light.Y, light.D + 1);
            spec = look.Shine * Math.Pow(Math.Max(0, n.X * half.X + n.Y * half.Y + n.D * half.D), 40);
            return 0.38 + 0.62 * lam;
        }

        bool fromAbove = _pitch > 0;
        foreach (var part in Shown(st))
        {
            double z0 = (part.Slice - 1) * _t, z1 = part.Slice * _t;
            var loops = part.Outline.Flatten(0.15).Where(f => f.Closed && f.Points.Count >= 3).Select(f => f.Points).ToList();
            var tint = part.Floating ? Color.Parse("#E5484D") : (Color?)null;

            // The top (or, seen from below, the bottom) face: flat, so its depth follows a plane.
            double faceZ = fromAbove ? z1 : z0;
            var nf = v.Turn(0, 0, fromAbove ? 1 : -1);
            double faceLight = Lit(nf, out double faceSpec);
            uint faceColour = Shade(tint ?? look.Top, faceLight, faceSpec);
            FillFace(loops, faceZ, v, w, h, px, depth, faceColour);

            // The edges all round: a strip of quads per outline, facing outwards.
            foreach (var l in loops)
            {
                // Outwards: right of travel round an anticlockwise outline, left round a hole.
                bool hole = loops.Count(o => !ReferenceEquals(o, l) && Geom.Inside(o, l[0])) % 2 == 1;
                bool ccw = Geom.SignedArea(l) > 0;
                double side = ccw ^ hole ? 1 : -1;
                for (int k = 0; k < l.Count; k++)
                {
                    var a = l[k];
                    var c = l[(k + 1) % l.Count];
                    double ex = c.X - a.X, ey = c.Y - a.Y, len = Math.Sqrt(ex * ex + ey * ey);
                    if (len < 1e-9) continue;
                    var n = v.Turn(side * ey / len, -side * ex / len, 0);
                    if (n.D <= 0) continue; // facing away
                    double lit = Lit(n, out double spec);
                    var p0 = v.Project(a.X, a.Y, z0);
                    var p1 = v.Project(c.X, c.Y, z0);
                    var p2 = v.Project(c.X, c.Y, z1);
                    var p3 = v.Project(a.X, a.Y, z1);
                    Triangle(p0, p1, p2, 0, 0, 1, w, h, px, depth, look, tint, lit, spec);
                    Triangle(p0, p2, p3, 0, 1, 1, w, h, px, depth, look, tint, lit, spec);
                }
            }
        }

        // Onto the screen.
        if (_frame is null || _frame.PixelSize.Width != w || _frame.PixelSize.Height != h)
        {
            _frame?.Dispose();
            _frame = new WriteableBitmap(new PixelSize(w, h), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        }
        var bg = ((ISolidColorBrush)UiKit.Brush("Bg0")).Color;
        uint bgPx = 0xFF000000u | (uint)bg.R << 16 | (uint)bg.G << 8 | bg.B;
        using (var fb = _frame.Lock())
        {
            var row = new int[w];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++) row[x] = (int)(px[y * w + x] == 0 ? bgPx : px[y * w + x]);
                System.Runtime.InteropServices.Marshal.Copy(row, 0, fb.Address + y * fb.RowBytes, w);
            }
        }
        ctx.DrawImage(_frame, new Rect(0, 0, w, h));
    }

    static (double X, double Y, double D) Norm(double x, double y, double d)
    {
        double l = Math.Sqrt(x * x + y * y + d * d);
        return (x / l, y / l, d / l);
    }

    static uint Shade(Color c, double lit, double spec)
    {
        byte Ch(byte v) => (byte)Math.Clamp(v * lit + 255 * spec, 0, 255);
        return 0xFF000000u | (uint)Ch(c.R) << 16 | (uint)Ch(c.G) << 8 | Ch(c.B);
    }

    /// <summary>A flat horizontal face (outlines with holes, even-odd) at height <paramref name="z"/>, depth-tested.</summary>
    static void FillFace(List<List<Vec2>> loops, double z, View v, int w, int h, uint[] px, float[] depth, uint colour)
    {
        if (Math.Abs(Math.Sin(v.Pitch)) < 0.03) return; // edge-on: nothing to see
        using var path = new SKPath { FillType = SKPathFillType.EvenOdd };
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var l in loops)
            for (int k = 0; k < l.Count; k++)
            {
                var (x, y, _) = v.Project(l[k].X, l[k].Y, z);
                if (k == 0) path.MoveTo((float)x, (float)y); else path.LineTo((float)x, (float)y);
                minX = Math.Min(minX, x); minY = Math.Min(minY, y); maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y);
            }
        path.Close();
        int x0 = Math.Max(0, (int)Math.Floor(minX)), y0 = Math.Max(0, (int)Math.Floor(minY));
        int x1 = Math.Min(w - 1, (int)Math.Ceiling(maxX)), y1 = Math.Min(h - 1, (int)Math.Ceiling(maxY));
        if (x1 < x0 || y1 < y0) return;
        int bw = x1 - x0 + 1, bh = y1 - y0 + 1;
        using var mask = new SKBitmap(new SKImageInfo(bw, bh, SKColorType.Alpha8, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(mask))
        {
            canvas.Clear(SKColors.Transparent);
            canvas.Translate(-x0, -y0);
            using var paint = new SKPaint { Color = SKColors.White, IsAntialias = false, Style = SKPaintStyle.Fill };
            canvas.DrawPath(path, paint);
        }
        var bytes = mask.Bytes;
        int stride = mask.RowBytes;
        // Depth on a horizontal plane: from the screen row back to the model's y, then to depth.
        double sinP = Math.Sin(v.Pitch), cosP = Math.Cos(v.Pitch), dz = z - v.Cz;
        for (int y = 0; y < bh; y++)
        {
            double ry = ((y + y0 + 0.5 - v.H / 2) / v.Scale + dz * cosP) / sinP;
            float d = (float)(ry * cosP + dz * sinP + 1e-3); // a hair in front of its own edges
            for (int x = 0; x < bw; x++)
            {
                if (bytes[y * stride + x] < 128) continue;
                int i = (y + y0) * w + x + x0;
                if (d <= depth[i]) continue;
                depth[i] = d;
                px[i] = colour;
            }
        }
    }

    /// <summary>One triangle of an edge, depth-tested; <paramref name="t0"/>… is how far up the slice each corner is (0 bottom, 1 top), for glue lines and plies.</summary>
    static void Triangle((double X, double Y, double D) a, (double X, double Y, double D) b, (double X, double Y, double D) c, double t0, double t1, double t2,
        int w, int h, uint[] px, float[] depth, Look3 look, Color? tint, double lit, double spec)
    {
        int minX = Math.Max(0, (int)Math.Floor(Math.Min(a.X, Math.Min(b.X, c.X)))), maxX = Math.Min(w - 1, (int)Math.Ceiling(Math.Max(a.X, Math.Max(b.X, c.X))));
        int minY = Math.Max(0, (int)Math.Floor(Math.Min(a.Y, Math.Min(b.Y, c.Y)))), maxY = Math.Min(h - 1, (int)Math.Ceiling(Math.Max(a.Y, Math.Max(b.Y, c.Y))));
        double area = (b.X - a.X) * (c.Y - a.Y) - (c.X - a.X) * (b.Y - a.Y);
        if (Math.Abs(area) < 1e-9) return;
        var side = tint ?? look.Side;
        var glue = tint is { } tc ? Color.FromRgb((byte)(tc.R * 0.6), (byte)(tc.G * 0.6), (byte)(tc.B * 0.6)) : look.Glue;
        for (int y = minY; y <= maxY; y++)
            for (int x = minX; x <= maxX; x++)
            {
                double sx = x + 0.5, sy = y + 0.5;
                double w0 = ((b.X - sx) * (c.Y - sy) - (c.X - sx) * (b.Y - sy)) / area;
                double w1 = ((c.X - sx) * (a.Y - sy) - (a.X - sx) * (c.Y - sy)) / area;
                double w2 = 1 - w0 - w1;
                if (w0 < -1e-6 || w1 < -1e-6 || w2 < -1e-6) continue;
                float d = (float)(w0 * a.D + w1 * b.D + w2 * c.D);
                int i = y * w + x;
                if (d <= depth[i]) continue;
                depth[i] = d;
                double t = w0 * t0 + w1 * t1 + w2 * t2;
                // A thin glue line at each slice's top and bottom; plies inside plywood and card.
                Color colour = side;
                if (t < 0.06 || t > 0.94) colour = glue;
                else if (look.Plies > 1)
                {
                    double ply = t * look.Plies;
                    double within = ply - Math.Floor(ply);
                    colour = within < 0.07 ? glue : ((int)ply % 2 == 0 ? side : Color.FromRgb((byte)(side.R * 0.93), (byte)(side.G * 0.93), (byte)(side.B * 0.93)));
                }
                px[i] = Shade(colour, lit, spec);
            }
    }
}
