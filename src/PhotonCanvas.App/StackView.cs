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
    /// <summary>Cut edges charred as the laser leaves them (darkest on the side it comes in), with a little smoke on the faces beside them.</summary>
    public bool Burnt { get; set { field = value; InvalidateVisual(); } } = true;
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

    /// <summary>Slotted sheets (egg crate, radial) rather than a stack of slices.</summary>
    static bool Sheets(SliceStack st) => st.Method != SliceMethod.Stacked;

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
        Caption(ctx, (Sheets(st) ? From > 1 || last < st.Slices ? $"Sheets {From}–{last} of {st.Slices}" : $"{st.Slices} sheets" : From > 1 || last < st.Slices ? $"Slices {From}–{last} of {st.Slices}" : $"{st.Slices} slices") + " · drag to turn", new Point(8, b.Height - 14));
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
        foreach (var part in Shown(st).OrderBy(p => p.Slice))
        {
            var f = part.Frame;
            Point P(Vec2 uv, double s) { var q = f.At(uv.X, uv.Y, s); var (x, y, _) = v.Project(q.X, q.Y, q.Z); return new Point(x, y); }
            var shade = part.Floating ? Color.Parse("#E5484D") : part.Slice % 2 == 0 ? Color.Parse("#C9A27A") : Color.Parse("#A9825C");
            var pen = new Pen(new SolidColorBrush(shade), 1.2);
            var faint = new Pen(new SolidColorBrush(Color.FromArgb(90, shade.R, shade.G, shade.B)), 1);
            double s0 = -_t / 2, s1 = _t / 2;
            foreach (var l in part.Outline.Flatten(0.2).Where(fp => fp.Closed).Select(fp => fp.Points))
            {
                for (int k = 0; k < l.Count; k++)
                {
                    var a = l[k];
                    var c = l[(k + 1) % l.Count];
                    ctx.DrawLine(pen, P(a, s1), P(c, s1));
                    ctx.DrawLine(faint, P(a, s0), P(c, s0));
                }
                int step = Math.Max(1, l.Count / 12);
                for (int k = 0; k < l.Count; k += step) ctx.DrawLine(faint, P(l[k], s0), P(l[k], s1));
            }
        }
    }

    // ---- shaded solid ----------------------------------------------------------------------

    /// <summary>A material's colours: faces, cut edges, glue lines; and, burnt, the charred edge and how much smoke stains the faces (none for acrylic, which melts clean).</summary>
    sealed record Look3(Color Top, Color Side, Color Glue, double Shine, int Plies, Color? Char = null, double Smoke = 0);

    static Look3 Of(StackMaterial m) => m switch
    {
        StackMaterial.BirchPlywood => new(Color.Parse("#E7D2A6"), Color.Parse("#D8BF8E"), Color.Parse("#7A5E3C"), 0, 3, Color.Parse("#3A2819"), 0.30),
        StackMaterial.Walnut => new(Color.Parse("#6E4B33"), Color.Parse("#5C3E2A"), Color.Parse("#3B2718"), 0.05, 0, Color.Parse("#21150D"), 0.25),
        StackMaterial.Mdf => new(Color.Parse("#B9996F"), Color.Parse("#8F7455"), Color.Parse("#5E4B36"), 0, 0, Color.Parse("#1E1712"), 0.35),
        StackMaterial.Cardboard => new(Color.Parse("#C9A777"), Color.Parse("#B8925F"), Color.Parse("#7D6142"), 0, 2, Color.Parse("#4A321F"), 0.30),
        StackMaterial.WhiteAcrylic => new(Color.Parse("#F4F4F2"), Color.Parse("#E2E3E0"), Color.Parse("#BFC1BD"), 0.6, 0),
        StackMaterial.BlackAcrylic => new(Color.Parse("#26272A"), Color.Parse("#1D1E21"), Color.Parse("#0E0F11"), 0.7, 0),
        _ => new(Color.Parse("#EAD7AF"), Color.Parse("#DCC294"), Color.Parse("#9C7F57"), 0, 0, Color.Parse("#4B321E"), 0.30),
    };

    /// <summary>The look with the cut edges charred: the char colour in place of the bare edge, glue lines and plies darker still.</summary>
    static Look3 Charred(Look3 l) => l.Char is not { } c ? l : l with
    {
        Side = c,
        Glue = Color.FromRgb((byte)(c.R * 0.6), (byte)(c.G * 0.6), (byte)(c.B * 0.6)),
    };

    void RenderSolid(DrawingContext ctx, SliceStack st)
    {
        int w = Math.Max(1, (int)Bounds.Width), h = Math.Max(1, (int)Bounds.Height);
        var v = MakeView(st, w, h);
        var px = new uint[w * h];
        var depth = new float[w * h];
        Array.Fill(depth, float.NegativeInfinity);
        var bare = Of(Material);
        bool burnt = Burnt && bare.Char is not null;
        var look = burnt ? Charred(bare) : bare;
        // Light from the upper left, in front; a little always (so nothing is black).
        var light = Norm(-0.45, -0.75, 0.55);
        double Lit((double X, double Y, double D) n, out double spec)
        {
            double lam = Math.Max(0, n.X * light.X + n.Y * light.Y + n.D * light.D);
            var half = Norm(light.X, light.Y, light.D + 1);
            spec = look.Shine * Math.Pow(Math.Max(0, n.X * half.X + n.Y * half.Y + n.D * half.D), 40);
            return 0.38 + 0.62 * lam;
        }

        foreach (var part in Shown(st))
        {
            var f = part.Frame;
            double s0 = -_t / 2, s1 = _t / 2;
            (double X, double Y, double D) Pr(Vec2 uv, double s) { var q = f.At(uv.X, uv.Y, s); return v.Project(q.X, q.Y, q.Z); }
            var loops = part.Outline.Flatten(0.15).Where(fp => fp.Closed && fp.Points.Count >= 3).Select(fp => fp.Points).ToList();
            var tint = part.Floating ? Color.Parse("#E5484D") : (Color?)null;

            // The face turned towards us (the sheet's two faces are its thickness apart along N).
            var nUp = v.Turn(f.N.X, f.N.Y, f.N.Z);
            bool front = nUp.D >= 0;
            var nf = front ? nUp : (-nUp.X, -nUp.Y, -nUp.D);
            double faceLight = Lit(nf, out double faceSpec);
            uint faceColour = Shade(tint ?? look.Top, faceLight, faceSpec);
            // Smoke beside the cut: about a millimetre wide, heavier on the top (where the laser comes in) than underneath.
            double smoke = burnt && tint is null ? look.Smoke * (front ? 1 : 0.4) : 0;
            FillFace(loops, uv => Pr(uv, front ? s1 : s0), w, h, px, depth, faceColour, smoke, 1.0 * v.Scale);

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
                    // The outward direction in the sheet, turned into the model.
                    double nu = side * ey / len, nv = -side * ex / len;
                    var n = v.Turn(nu * f.U.X + nv * f.V.X, nu * f.U.Y + nv * f.V.Y, nu * f.U.Z + nv * f.V.Z);
                    if (n.D <= 0) continue; // facing away
                    double lit = Lit(n, out double spec);
                    var p0 = Pr(a, s0);
                    var p1 = Pr(c, s0);
                    var p2 = Pr(c, s1);
                    var p3 = Pr(a, s1);
                    Triangle(p0, p1, p2, 0, 0, 1, w, h, px, depth, look, tint, lit, spec, burnt);
                    Triangle(p0, p2, p3, 0, 1, 1, w, h, px, depth, look, tint, lit, spec, burnt);
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

    /// <summary>A flat face (outlines with holes, even-odd) on the screen, depth-tested; its depth follows the face's plane.</summary>
    static void FillFace(List<List<Vec2>> loops, Func<Vec2, (double X, double Y, double D)> project, int w, int h, uint[] px, float[] depth, uint colour, double smoke = 0, double smokePx = 0)
    {
        if (loops.Count == 0) return;
        // The plane on screen: depth = a·x + b·y + c, from three corners well apart.
        var pts = loops[0];
        var p0 = project(pts[0]);
        var p1 = project(pts[pts.Count / 3]);
        var p2 = project(pts[2 * pts.Count / 3]);
        // Corners in a line: use the outline's extremes instead.
        double det = (p1.X - p0.X) * (p2.Y - p0.Y) - (p2.X - p0.X) * (p1.Y - p0.Y);
        if (Math.Abs(det) < 1e-6)
        {
            var all = loops.SelectMany(l => l).Select(project).ToList();
            p0 = all.MinBy(p => p.X); p1 = all.MaxBy(p => p.X); p2 = all.MaxBy(p => Math.Abs((p1.X - p0.X) * (p.Y - p0.Y) - (p.X - p0.X) * (p1.Y - p0.Y)));
            det = (p1.X - p0.X) * (p2.Y - p0.Y) - (p2.X - p0.X) * (p1.Y - p0.Y);
            if (Math.Abs(det) < 1e-6) return; // edge-on: nothing to see
        }
        double da = ((p1.D - p0.D) * (p2.Y - p0.Y) - (p2.D - p0.D) * (p1.Y - p0.Y)) / det;
        double db = ((p1.X - p0.X) * (p2.D - p0.D) - (p2.X - p0.X) * (p1.D - p0.D)) / det;
        double dc = p0.D - da * p0.X - db * p0.Y;
        using var path = new SKPath { FillType = SKPathFillType.EvenOdd };
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var l in loops)
        {
            for (int k = 0; k < l.Count; k++)
            {
                var (x, y, _) = project(l[k]);
                if (k == 0) path.MoveTo((float)x, (float)y); else path.LineTo((float)x, (float)y);
                minX = Math.Min(minX, x); minY = Math.Min(minY, y); maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y);
            }
            path.Close();
        }
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
        // The smoke stain: the outline drawn soft, darkening the face where they overlap.
        byte[]? stain = null;
        if (smoke > 0 && smokePx > 0.5)
        {
            using var soft = new SKBitmap(new SKImageInfo(bw, bh, SKColorType.Alpha8, SKAlphaType.Premul));
            using (var canvas = new SKCanvas(soft))
            {
                canvas.Clear(SKColors.Transparent);
                canvas.Translate(-x0, -y0);
                using var blur = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, (float)(smokePx * 0.6));
                using var paint = new SKPaint { Color = SKColors.White, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = (float)smokePx, MaskFilter = blur };
                canvas.DrawPath(path, paint);
            }
            stain = soft.Bytes;
        }
        for (int y = 0; y < bh; y++)
        {
            for (int x = 0; x < bw; x++)
            {
                if (bytes[y * stride + x] < 128) continue;
                int i = (y + y0) * w + x + x0;
                float d = (float)(da * (x + x0 + 0.5) + db * (y + y0 + 0.5) + dc + 1e-3); // a hair in front of its own edges
                if (d <= depth[i]) continue;
                depth[i] = d;
                if (stain is not null && stain[y * stride + x] is var a && a > 0)
                {
                    double k = 1 - smoke * a / 255.0;
                    px[i] = 0xFF000000u | (uint)((colour >> 16 & 0xFF) * k) << 16 | (uint)((colour >> 8 & 0xFF) * k) << 8 | (uint)((colour & 0xFF) * k);
                }
                else px[i] = colour;
            }
        }
    }

    /// <summary>One triangle of an edge, depth-tested; <paramref name="t0"/>… is how far up the slice each corner is (0 bottom, 1 top), for glue lines and plies.</summary>
    static void Triangle((double X, double Y, double D) a, (double X, double Y, double D) b, (double X, double Y, double D) c, double t0, double t1, double t2,
        int w, int h, uint[] px, float[] depth, Look3 look, Color? tint, double lit, double spec, bool burnt = false)
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
                // Charred darkest at the top, where the beam comes in, a little lighter at the bottom.
                px[i] = Shade(colour, burnt && tint is null ? lit * (0.8 + 0.3 * (1 - t)) : lit, spec);
            }
    }
}
