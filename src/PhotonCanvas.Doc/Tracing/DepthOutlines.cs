using PhotonCanvas.Core;
using PhotonCanvas.Doc.Geometry;

namespace PhotonCanvas.Doc.Tracing;

/// <summary>How wide the outline lines are.</summary>
public enum OutlineWidth
{
    /// <summary>One pass of the beam along each line (open lines on a Score layer).</summary>
    LaserLine,
    /// <summary>Filled lines, all the same width.</summary>
    Fixed,
    /// <summary>Filled lines that swell and thin with <see cref="WidthFollows"/>.</summary>
    Varying,
}

/// <summary>What makes a varying line wide.</summary>
public enum WidthFollows
{
    /// <summary>A steep step in the depthmap (a tall edge) gets a wide line, a gentle one a thin line.</summary>
    EdgeStrength,
    /// <summary>Edges of the raised (light, near) parts get wide lines.</summary>
    Height,
    /// <summary>Edges down in the deep (dark, far) parts get wide lines: shadows in the crevices.</summary>
    Depth,
}

public sealed record DepthOutlineOptions
{
    /// <summary>Detail finer than this (mm) is smoothed away before edges are looked for.</summary>
    public double DetailMm { get; init; } = 0.3;
    /// <summary>How many edges to keep, 1–60: roughly the share (%) of possible edge points that are kept.</summary>
    public double Amount { get; init; } = 18;
    /// <summary>Lines shorter than this (mm) are dropped.</summary>
    public double MinLengthMm { get; init; } = 3;
    /// <summary>How much each line is smoothed along its length (mm).</summary>
    public double SmoothMm { get; init; } = 0.5;
    public OutlineWidth Width { get; init; } = OutlineWidth.Varying;
    /// <summary>Fixed width (mm).</summary>
    public double WidthMm { get; init; } = 0.3;
    /// <summary>Varying: thinnest and widest (mm).</summary>
    public double MinWidthMm { get; init; } = 0.12;
    public double MaxWidthMm { get; init; } = 0.6;
    public WidthFollows Follows { get; init; } = WidthFollows.EdgeStrength;
    /// <summary>Lines come to a point at their ends instead of stopping square.</summary>
    public bool TaperEnds { get; init; } = true;
}

/// <summary>The edge lines found in a depthmap, before they're given a width.</summary>
/// <param name="Lines">Centre lines, in picture mm (0,0 = the picture's top-left corner).</param>
/// <param name="Weights">For each point of each line, 0–1: how strongly it follows <see cref="DepthOutlineOptions.Follows"/>.</param>
public sealed record DepthEdges(IReadOnlyList<IReadOnlyList<Vec2>> Lines, IReadOnlyList<double[]> Weights)
{
    public double LengthMm => Lines.Sum(l => { double s = 0; for (int i = 1; i < l.Count; i++) s += l[i - 1].Dist(l[i]); return s; });
}

/// <summary>
/// Outlines from a depthmap: the places where the surface steps or bends sharply (the outline of a
/// face against the background, a leaf over a leaf) found with a Canny edge finder, followed into
/// lines, then drawn as laser lines or filled ribbons a set width or swelling with the edge.
/// Marked over a relief, they put back the drawing that a soft depthmap loses.
/// </summary>
public static class DepthOutlines
{
    /// <summary>
    /// Finds the edges of a depthmap given as <paramref name="height"/> (0 deep/black … 1 near/white,
    /// row-major) of <paramref name="w"/> × <paramref name="h"/> pixels, each <paramref name="mmPerPx"/> in size.
    /// </summary>
    public static DepthEdges Find(float[] height, int w, int h, Vec2 mmPerPx, DepthOutlineOptions o)
    {
        if (height.Length != w * h || w < 3 || h < 3) return new([], []);
        double pxMm = (mmPerPx.X + mmPerPx.Y) / 2;
        var smooth = Blur(height, w, h, Math.Max(0.6, o.DetailMm / pxMm));

        // Sobel: how steep the surface is, and which way it climbs.
        var mag = new float[w * h];
        var gx = new float[w * h];
        var gy = new float[w * h];
        Parallel.For(1, h - 1, y =>
        {
            for (int x = 1; x < w - 1; x++)
            {
                int i = y * w + x;
                float a = smooth[i - w - 1], b = smooth[i - w], c = smooth[i - w + 1];
                float d = smooth[i - 1], f = smooth[i + 1];
                float g = smooth[i + w - 1], k = smooth[i + w], l = smooth[i + w + 1];
                float sx = (c + 2 * f + l) - (a + 2 * d + g);
                float sy = (g + 2 * k + l) - (a + 2 * b + c);
                gx[i] = sx;
                gy[i] = sy;
                mag[i] = MathF.Sqrt(sx * sx + sy * sy);
            }
        });

        // Thin to the ridge: keep a point only where it's steeper than its neighbours across the edge.
        var peak = new bool[w * h];
        Parallel.For(1, h - 1, y =>
        {
            for (int x = 1; x < w - 1; x++)
            {
                int i = y * w + x;
                float m = mag[i];
                if (m <= 1e-6f) continue;
                double ang = Math.Atan2(gy[i], gx[i]) * 180 / Math.PI;
                if (ang < 0) ang += 180;
                int j = ang < 22.5 || ang >= 157.5 ? 1 : ang < 67.5 ? w + 1 : ang < 112.5 ? w : w - 1;
                peak[i] = m >= mag[i - j] && m > mag[i + j];
            }
        });

        // Thresholds from the picture itself: the strongest Amount % of ridge points start lines,
        // which then carry on through anything at least half as strong (hysteresis).
        var ridge = new List<float>();
        for (int i = 0; i < peak.Length; i++) if (peak[i]) ridge.Add(mag[i]);
        if (ridge.Count == 0) return new([], []);
        ridge.Sort();
        double amount = Math.Clamp(o.Amount, 1, 60) / 100;
        float high = ridge[Math.Clamp((int)((1 - amount) * ridge.Count), 0, ridge.Count - 1)];
        float low = high * 0.5f;
        float top = ridge[Math.Clamp((int)(0.995 * ridge.Count), 0, ridge.Count - 1)];
        var edge = new bool[w * h];
        var stack = new Stack<int>();
        for (int i = 0; i < edge.Length; i++)
        {
            if (!peak[i] || edge[i] || mag[i] < high) continue;
            edge[i] = true;
            stack.Push(i);
            while (stack.Count > 0)
            {
                int c = stack.Pop(), cx = c % w, cy = c / w;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = cx + dx, ny = cy + dy;
                        if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                        int n = ny * w + nx;
                        if (edge[n] || !peak[n] || mag[n] < low) continue;
                        edge[n] = true;
                        stack.Push(n);
                    }
            }
        }

        var raw = Centreline.Trace(edge, w, h, Math.Max(2, o.MinLengthMm / pxMm), 0.5);
        var lines = new List<IReadOnlyList<Vec2>>();
        var weights = new List<double[]>();
        double smoothPx = Math.Max(0, o.SmoothMm / pxMm);
        foreach (var line in raw)
        {
            var pts = SmoothLine(line, smoothPx);
            if (pts.Count < 2 || Length(pts) * pxMm < o.MinLengthMm) continue;
            pts = PathTools.Simplify(pts, 0.15);
            var wt = new double[pts.Count];
            for (int k = 0; k < pts.Count; k++)
                wt[k] = Sample(pts[k], o.Follows, smooth, mag, gx, gy, w, h, low, top);
            lines.Add(pts.Select(p => new Vec2(p.X * mmPerPx.X, p.Y * mmPerPx.Y)).ToList());
            weights.Add(SmoothWeights(wt));
        }
        if (o.Follows != WidthFollows.EdgeStrength) Stretch(weights);
        return new(lines, weights);
    }

    /// <summary>
    /// The edges as the marks to make, in picture mm: open lines (<see cref="OutlineWidth.LaserLine"/>)
    /// or filled outlines merged into one shape.
    /// </summary>
    public static PathGeometry Draw(DepthEdges e, DepthOutlineOptions o)
    {
        if (e.Lines.Count == 0) return PathGeometry.Empty;
        if (o.Width == OutlineWidth.LaserLine)
            return new PathGeometry(e.Lines.Where(l => l.Count >= 2).Select(l => PathGeometry.Polyline(l, false).Subpaths[0]));
        double lo = Math.Max(0.02, o.Width == OutlineWidth.Fixed ? o.WidthMm : Math.Min(o.MinWidthMm, o.MaxWidthMm));
        double hi = Math.Max(lo, o.Width == OutlineWidth.Fixed ? o.WidthMm : Math.Max(o.MinWidthMm, o.MaxWidthMm));
        const double tol = 0.005;
        var pieces = new List<Subpath>();
        for (int n = 0; n < e.Lines.Count; n++)
        {
            var l = e.Lines[n];
            var wt = e.Weights[n];
            var r = new double[l.Count];
            double total = Length(l), along = 0;
            for (int k = 0; k < l.Count; k++)
            {
                if (k > 0) along += l[k - 1].Dist(l[k]);
                double width = lo + (hi - lo) * (o.Width == OutlineWidth.Fixed ? 1 : wt[k]);
                if (o.TaperEnds)
                {
                    // Come to a point over a few widths at each end (less on a short line).
                    double reach = Math.Min(total / 2, hi * 4);
                    double t = reach <= 1e-9 ? 1 : Math.Clamp(Math.Min(along, total - along) / reach, 0, 1);
                    width *= 0.15 + 0.85 * t * t * (3 - 2 * t);
                }
                r[k] = width / 2;
            }
            // Each step a tapered band, each point a disc; merged, they make a ribbon with round ends and joins.
            for (int k = 0; k < l.Count; k++)
            {
                pieces.Add(Disc(l[k], r[k], tol));
                if (k == 0) continue;
                var a = l[k - 1];
                var b = l[k];
                var d = b - a;
                double len = d.Length;
                if (len < 1e-9) continue;
                var nrm = new Vec2(-d.Y / len, d.X / len);
                pieces.Add(Ccw([a + nrm * r[k - 1], b + nrm * r[k], b - nrm * r[k], a - nrm * r[k - 1]]));
            }
        }
        return ShapeGeometry.FillNonZero(new PathGeometry(pieces), tol);
    }

    /// <summary>0–1 for one point on an edge: how steep, how high or how deep it is there.</summary>
    static double Sample(Vec2 p, WidthFollows follows, float[] height, float[] mag, float[] gx, float[] gy, int w, int h, float low, float top)
    {
        int x = Math.Clamp((int)p.X, 0, w - 1), y = Math.Clamp((int)p.Y, 0, h - 1);
        if (follows == WidthFollows.EdgeStrength)
        {
            float m = 0;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int xx = Math.Clamp(x + dx, 0, w - 1), yy = Math.Clamp(y + dy, 0, h - 1);
                    m = Math.Max(m, mag[yy * w + xx]);
                }
            return Math.Clamp((m - low) / Math.Max(1e-6, top - low), 0, 1);
        }
        // The surface a couple of pixels to the high side of the step (height) or the low side (depth).
        int i = y * w + x;
        double len = Math.Sqrt(gx[i] * gx[i] + gy[i] * gy[i]);
        double ux = len > 1e-9 ? gx[i] / len : 0, uy = len > 1e-9 ? gy[i] / len : 0;
        double side = follows == WidthFollows.Height ? 2.5 : -2.5;
        int sx = Math.Clamp((int)(p.X + ux * side), 0, w - 1), sy = Math.Clamp((int)(p.Y + uy * side), 0, h - 1);
        double v = height[sy * w + sx];
        return follows == WidthFollows.Height ? v : 1 - v;
    }

    /// <summary>Spreads the weights over 0–1 (2nd to 98th percentile), so the whole width range is used.</summary>
    static void Stretch(List<double[]> weights)
    {
        var all = weights.SelectMany(x => x).OrderBy(x => x).ToList();
        if (all.Count < 2) return;
        double a = all[(int)(0.02 * (all.Count - 1))], b = all[(int)(0.98 * (all.Count - 1))];
        if (b - a < 1e-6) { foreach (var wt in weights) Array.Fill(wt, 0.5); return; }
        foreach (var wt in weights)
            for (int k = 0; k < wt.Length; k++) wt[k] = Math.Clamp((wt[k] - a) / (b - a), 0, 1);
    }

    /// <summary>Evens out the weights along a line, so its width swells gently instead of flickering.</summary>
    static double[] SmoothWeights(double[] wt)
    {
        var o = new double[wt.Length];
        for (int k = 0; k < wt.Length; k++)
        {
            double s = 0;
            int n = 0;
            for (int j = Math.Max(0, k - 3); j <= Math.Min(wt.Length - 1, k + 3); j++) { s += wt[j]; n++; }
            o[k] = s / n;
        }
        return o;
    }

    /// <summary>
    /// The line resampled every pixel and averaged over about <paramref name="radiusPx"/> either side,
    /// ends kept in place: the pixel stairs of the edge become a smooth stroke.
    /// </summary>
    static List<Vec2> SmoothLine(List<Vec2> line, double radiusPx)
    {
        if (line.Count < 3 || radiusPx < 0.5) return line;
        var even = new List<Vec2> { line[0] };
        double carry = 0;
        for (int i = 1; i < line.Count; i++)
        {
            var a = line[i - 1];
            var d = line[i] - a;
            double len = d.Length;
            double t = 1 - carry;
            while (t <= len)
            {
                even.Add(a + d * (t / len));
                t += 1;
            }
            carry = len - (t - 1);
        }
        if (even[^1].Dist(line[^1]) > 1e-6) even.Add(line[^1]);
        int r = Math.Max(1, (int)Math.Round(radiusPx));
        var o = new List<Vec2>(even.Count);
        for (int k = 0; k < even.Count; k++)
        {
            int reach = Math.Min(r, Math.Min(k, even.Count - 1 - k)); // shrinks at the ends, so they stay put
            double sx = 0, sy = 0;
            for (int j = k - reach; j <= k + reach; j++) { sx += even[j].X; sy += even[j].Y; }
            o.Add(new Vec2(sx / (2 * reach + 1), sy / (2 * reach + 1)));
        }
        return o;
    }

    static Subpath Disc(Vec2 c, double r, double tol)
    {
        int n = r <= tol ? 6 : Math.Clamp((int)Math.Ceiling(Math.PI / Math.Acos(1 - tol / r)), 6, 48);
        var pts = new List<Vec2>(n);
        for (int i = 0; i < n; i++)
        {
            double a = 2 * Math.PI * i / n;
            pts.Add(new Vec2(c.X + r * Math.Cos(a), c.Y + r * Math.Sin(a)));
        }
        return PathGeometry.Polyline(pts, true).Subpaths[0];
    }

    /// <summary>All pieces wound the same way, so the non-zero fill merges them instead of cutting holes.</summary>
    static Subpath Ccw(List<Vec2> pts)
    {
        if (Geom.SignedArea(pts) < 0) pts.Reverse();
        return PathGeometry.Polyline(pts, true).Subpaths[0];
    }

    static double Length(IReadOnlyList<Vec2> p)
    {
        double l = 0;
        for (int i = 1; i < p.Count; i++) l += p[i - 1].Dist(p[i]);
        return l;
    }

    /// <summary>Gaussian blur (separable).</summary>
    static float[] Blur(float[] src, int w, int h, double sigma)
    {
        int r = Math.Max(1, (int)Math.Ceiling(sigma * 3));
        var k = new float[2 * r + 1];
        double sum = 0;
        for (int i = -r; i <= r; i++) sum += k[i + r] = (float)Math.Exp(-i * i / (2 * sigma * sigma));
        for (int i = 0; i < k.Length; i++) k[i] /= (float)sum;
        var tmp = new float[w * h];
        var o = new float[w * h];
        Parallel.For(0, h, y =>
        {
            for (int x = 0; x < w; x++)
            {
                float s = 0;
                for (int j = -r; j <= r; j++) s += k[j + r] * src[y * w + Math.Clamp(x + j, 0, w - 1)];
                tmp[y * w + x] = s;
            }
        });
        Parallel.For(0, h, y =>
        {
            for (int x = 0; x < w; x++)
            {
                float s = 0;
                for (int j = -r; j <= r; j++) s += k[j + r] * tmp[Math.Clamp(y + j, 0, h - 1) * w + x];
                o[y * w + x] = s;
            }
        });
        return o;
    }
}
