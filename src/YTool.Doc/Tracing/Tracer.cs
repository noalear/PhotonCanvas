using System.Collections.Immutable;
using YTool.Core;

namespace YTool.Doc.Tracing;

public sealed record TraceOptions
{
    /// <summary>Black & white: pixels darker than this (0 black … 1 white) are traced.</summary>
    public double Threshold { get; init; } = 0.5;
    /// <summary>Trace the light areas instead of the dark ones.</summary>
    public bool Invert { get; init; }
    /// <summary>0 = follow every pixel step … 10 = very smooth, simplified outlines.</summary>
    public double Smoothness { get; init; } = 2;
    /// <summary>
    /// Bends sharper than this (degrees) stay as corners; gentler ones become
    /// curves. 0 = straight lines only, 180 = everything rounded.
    /// </summary>
    public double CornerAngle { get; init; } = 60;
    /// <summary>Blobs and holes smaller than this many pixels are ignored.</summary>
    public int MinAreaPx { get; init; } = 12;
}

/// <summary>
/// Bitmap → smooth vector outlines. Pipeline: remove specks, find the edges
/// with sub-pixel precision (marching squares on a lightly blurred mask),
/// simplify (Ramer–Douglas–Peucker), then fit cubic curves through the
/// remaining points, keeping sharp corners sharp. Output is in pixel
/// coordinates (pixel centres at +0.5); subpaths use the even-odd rule, so
/// holes come out as holes.
/// </summary>
public static class Tracer
{
    public static PathGeometry TraceBrightness(float[] brightness, int w, int h, TraceOptions o)
    {
        var mask = new bool[w * h];
        float t = (float)o.Threshold;
        for (int i = 0; i < mask.Length; i++) mask[i] = o.Invert ? brightness[i] >= t : brightness[i] < t;
        return TraceMask(mask, w, h, o);
    }

    public static PathGeometry TraceMask(bool[] mask, int w, int h, TraceOptions o)
    {
        if (mask.Length != w * h) throw new ArgumentException("mask size mismatch");
        var clean = (bool[])mask.Clone();
        Despeckle(clean, w, h, o.MinAreaPx);
        var loops = Contours(clean, w, h);
        double eps = 0.3 + Math.Clamp(o.Smoothness, 0, 10) * 0.25;
        var subs = new List<Subpath>();
        foreach (var loop in loops)
        {
            if (Math.Abs(Geom.SignedArea(loop)) < Math.Max(1, o.MinAreaPx) * 0.5) continue;
            var simple = SimplifyClosed(loop, eps);
            if (simple.Count < 3) continue;
            subs.Add(FitCurves(simple, o.CornerAngle));
        }
        return new PathGeometry(subs);
    }

    // ---- 1. specks -------------------------------------------------------------------

    /// <summary>Flip connected regions (ink blobs and enclosed holes) smaller than <paramref name="minArea"/>.</summary>
    internal static void Despeckle(bool[] m, int w, int h, int minArea)
    {
        if (minArea <= 1) return;
        foreach (bool ink in new[] { true, false })
        {
            var seen = new bool[m.Length];
            var stack = new Stack<int>();
            var region = new List<int>();
            for (int start = 0; start < m.Length; start++)
            {
                if (seen[start] || m[start] != ink) continue;
                region.Clear();
                bool touchesBorder = false;
                stack.Push(start);
                seen[start] = true;
                while (stack.Count > 0)
                {
                    int i = stack.Pop();
                    region.Add(i);
                    int x = i % w, y = i / w;
                    if (x == 0 || y == 0 || x == w - 1 || y == h - 1) touchesBorder = true;
                    void Visit(int j) { if (!seen[j] && m[j] == ink) { seen[j] = true; stack.Push(j); } }
                    if (x > 0) Visit(i - 1);
                    if (x < w - 1) Visit(i + 1);
                    if (y > 0) Visit(i - w);
                    if (y < h - 1) Visit(i + w);
                }
                // The background around the picture is never a "hole".
                if (region.Count < minArea && (ink || !touchesBorder))
                    foreach (int i in region) m[i] = !ink;
            }
        }
    }

    // ---- 2. edges ---------------------------------------------------------------------

    /// <summary>Closed outlines of the mask, sub-pixel, in pixel coordinates.</summary>
    internal static List<List<Vec2>> Contours(bool[] mask, int w, int h)
    {
        // Pad with empty border so every outline closes, then blur slightly so
        // marching squares interpolates smooth diagonals instead of stairs.
        int W = w + 2, H = h + 2;
        var f = new float[W * H];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (mask[y * w + x]) f[(y + 1) * W + x + 1] = 1;
        f = Blur(f, W, H, 0.6);
        const float level = 0.5f;

        int hEdges = W * H;
        // Edge id → interpolated point and up to two neighbours.
        var point = new Dictionary<int, Vec2>();
        var links = new Dictionary<int, (int A, int B)>();
        void Link(int a, int b)
        {
            Add(a, b);
            Add(b, a);
        }
        void Add(int from, int to) =>
            links[from] = links.TryGetValue(from, out var l) ? (l.A, to) : (to, -1);

        Vec2 Interp(int i0, int j0, int i1, int j1)
        {
            float a = f[j0 * W + i0], b = f[j1 * W + i1];
            double t = Math.Abs(b - a) < 1e-9 ? 0.5 : Math.Clamp((level - a) / (b - a), 0, 1);
            // Padded sample (i, j) is the centre of pixel (i-1, j-1).
            return new(i0 + (i1 - i0) * t - 0.5, j0 + (j1 - j0) * t - 0.5);
        }
        int Edge(int i, int j, char side) => side switch
        {
            'T' => j * W + i,
            'B' => (j + 1) * W + i,
            'L' => hEdges + j * W + i,
            _ => hEdges + j * W + i + 1, // R
        };
        Vec2 EdgePoint(int i, int j, char side) => side switch
        {
            'T' => Interp(i, j, i + 1, j),
            'B' => Interp(i, j + 1, i + 1, j + 1),
            'L' => Interp(i, j, i, j + 1),
            _ => Interp(i + 1, j, i + 1, j + 1),
        };

        for (int j = 0; j < H - 1; j++)
            for (int i = 0; i < W - 1; i++)
            {
                float tl = f[j * W + i], tr = f[j * W + i + 1], br = f[(j + 1) * W + i + 1], bl = f[(j + 1) * W + i];
                int c = (tl >= level ? 8 : 0) | (tr >= level ? 4 : 0) | (br >= level ? 2 : 0) | (bl >= level ? 1 : 0);
                if (c is 0 or 15) continue;
                bool centre = (tl + tr + br + bl) / 4 >= level;
                (char, char)[] segs = c switch
                {
                    1 => [('L', 'B')], 2 => [('B', 'R')], 3 => [('L', 'R')], 4 => [('T', 'R')],
                    5 => centre ? [('L', 'T'), ('B', 'R')] : [('T', 'R'), ('L', 'B')],
                    6 => [('T', 'B')], 7 => [('T', 'L')], 8 => [('T', 'L')], 9 => [('T', 'B')],
                    10 => centre ? [('T', 'R'), ('L', 'B')] : [('L', 'T'), ('B', 'R')],
                    11 => [('T', 'R')], 12 => [('L', 'R')], 13 => [('B', 'R')], _ => [('L', 'B')],
                };
                foreach (var (s0, s1) in segs)
                {
                    int e0 = Edge(i, j, s0), e1 = Edge(i, j, s1);
                    if (!point.ContainsKey(e0)) point[e0] = EdgePoint(i, j, s0);
                    if (!point.ContainsKey(e1)) point[e1] = EdgePoint(i, j, s1);
                    Link(e0, e1);
                }
            }

        var loops = new List<List<Vec2>>();
        var used = new HashSet<int>();
        foreach (var start in links.Keys)
        {
            if (used.Contains(start)) continue;
            var loop = new List<Vec2>();
            int prev = -1, cur = start;
            while (cur >= 0 && used.Add(cur))
            {
                loop.Add(point[cur]);
                var (a, b) = links[cur];
                int next = a != prev ? a : b;
                if (next == prev && a == b) next = -1;
                prev = cur;
                cur = next;
            }
            if (loop.Count >= 3) loops.Add(loop);
        }
        return loops;
    }

    /// <summary>Separable Gaussian blur.</summary>
    static float[] Blur(float[] src, int w, int h, double sigma)
    {
        int r = Math.Max(1, (int)Math.Ceiling(sigma * 3));
        var k = new float[2 * r + 1];
        double sum = 0;
        for (int i = -r; i <= r; i++) sum += k[i + r] = (float)Math.Exp(-i * i / (2 * sigma * sigma));
        for (int i = 0; i < k.Length; i++) k[i] /= (float)sum;
        var tmp = new float[src.Length];
        var dst = new float[src.Length];
        Parallel.For(0, h, y =>
        {
            for (int x = 0; x < w; x++)
            {
                float acc = 0;
                for (int i = -r; i <= r; i++) acc += k[i + r] * src[y * w + Math.Clamp(x + i, 0, w - 1)];
                tmp[y * w + x] = acc;
            }
        });
        Parallel.For(0, h, y =>
        {
            for (int x = 0; x < w; x++)
            {
                float acc = 0;
                for (int i = -r; i <= r; i++) acc += k[i + r] * tmp[Math.Clamp(y + i, 0, h - 1) * w + x];
                dst[y * w + x] = acc;
            }
        });
        return dst;
    }

    // ---- 3. simplify -------------------------------------------------------------------

    internal static List<Vec2> SimplifyClosed(List<Vec2> pts, double eps)
    {
        int n = pts.Count;
        if (n < 4) return pts;
        // Split the loop at the point farthest from the first, simplify both halves.
        int far = 0;
        double best = -1;
        for (int i = 1; i < n; i++)
        {
            double d = pts[i].Dist(pts[0]);
            if (d > best) { best = d; far = i; }
        }
        var a = Rdp(pts, 0, far, eps);
        var bList = new List<Vec2>(pts.Skip(far)) { pts[0] };
        var b = Rdp(bList, 0, bList.Count - 1, eps);
        var o = new List<Vec2>(a);
        o.RemoveAt(o.Count - 1);
        o.AddRange(b);
        o.RemoveAt(o.Count - 1); // closing point is implicit
        return o;
    }

    static List<Vec2> Rdp(List<Vec2> p, int from, int to, double eps)
    {
        var keep = new bool[p.Count];
        keep[from] = keep[to] = true;
        var stack = new Stack<(int, int)>();
        stack.Push((from, to));
        while (stack.Count > 0)
        {
            var (a, b) = stack.Pop();
            double maxD = -1;
            int idx = -1;
            for (int i = a + 1; i < b; i++)
            {
                double d = Geom.SegmentDistance(p[i], p[a], p[b]);
                if (d > maxD) { maxD = d; idx = i; }
            }
            if (idx >= 0 && maxD > eps)
            {
                keep[idx] = true;
                stack.Push((a, idx));
                stack.Push((idx, b));
            }
        }
        var o = new List<Vec2>();
        for (int i = from; i <= to; i++) if (keep[i]) o.Add(p[i]);
        return o;
    }

    // ---- 4. curves ---------------------------------------------------------------------

    /// <summary>Smooth closed curve through the points; vertices that turn more than <paramref name="cornerDeg"/> stay sharp.</summary>
    internal static Subpath FitCurves(List<Vec2> p, double cornerDeg)
    {
        int n = p.Count;
        var corner = new bool[n];
        for (int i = 0; i < n; i++)
        {
            var a = p[i] - p[(i - 1 + n) % n];
            var b = p[(i + 1) % n] - p[i];
            double la = a.Length, lb = b.Length;
            double turn = la < 1e-12 || lb < 1e-12 ? 180 : Math.Acos(Math.Clamp(a.Dot(b) / (la * lb), -1, 1)) * 180 / Math.PI;
            corner[i] = turn > cornerDeg;
        }
        Vec2 Tangent(int i, bool outgoing)
        {
            if (corner[i])
                return outgoing ? p[(i + 1) % n] - p[i] : p[i] - p[(i - 1 + n) % n];
            return (p[(i + 1) % n] - p[(i - 1 + n) % n]) / 2;
        }
        var segs = ImmutableArray.CreateBuilder<Seg>(n);
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            if (corner[i] && corner[j]) { segs.Add(Seg.Line(p[j])); continue; }
            var t0 = Tangent(i, true);
            var t1 = Tangent(j, false);
            // Keep handles inside the segment so curves never overshoot or loop.
            double len = p[i].Dist(p[j]);
            static Vec2 Clip(Vec2 v, double max) => v.Length > max && v.Length > 0 ? v * (max / v.Length) : v;
            segs.Add(Seg.Cubic(p[i] + Clip(t0 / 3, len * 0.45), p[j] - Clip(t1 / 3, len * 0.45), p[j]));
        }
        return new Subpath(p[0], segs.ToImmutable(), true);
    }
}
