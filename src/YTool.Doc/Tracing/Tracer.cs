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
    public double CornerAngle { get; init; } = 40;
    /// <summary>Blobs and holes smaller than this many pixels are ignored.</summary>
    public int MinAreaPx { get; init; } = 12;
}

/// <summary>
/// Bitmap → smooth vector outlines. Pipeline: remove specks, find the edges
/// with sub-pixel precision (marching squares on a lightly blurred mask),
/// find corners on the dense outline, then fit each stretch between corners
/// with straight lines where it is straight and least-squares cubic curves
/// elsewhere (Schneider's method), splitting until every edge point is
/// within the tolerance. Output is in pixel
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
            // Slivers can collapse to nothing once straight runs become lines; a laser can't use those.
            if (FitOutline(loop, eps, o.CornerAngle) is { } sub && Math.Abs(Geom.SignedArea(sub.Flatten(0.25))) >= Math.Max(1, o.MinAreaPx) * 0.5)
                subs.Add(sub);
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

    /// <summary>
    /// Vector outline for one dense edge loop, within <paramref name="eps"/> px of it.
    /// <paramref name="cornerDeg"/> &lt; 1 gives straight lines only.
    /// </summary>
    internal static Subpath? FitOutline(List<Vec2> loop, double eps, double cornerDeg)
    {
        var pts = Dedupe(loop);
        if (pts.Count < 3) return null;
        var b = ImmutableArray.CreateBuilder<Seg>();
        if (cornerDeg < 1)
        {
            var poly = SimplifyClosed(pts, eps);
            if (poly.Count < 3) return null;
            for (int k = 1; k <= poly.Count; k++) b.Add(Seg.Line(poly[k % poly.Count]));
            return new Subpath(poly[0], b.ToImmutable(), true);
        }

        var corners = FindCorners(pts, cornerDeg, 2 + eps);
        int n = pts.Count;
        // Start the loop at a corner (or anywhere, if it is smooth all round).
        int start = corners.Count > 0 ? corners[0] : 0;
        var p = new List<Vec2>(n + 1);
        for (int k = 0; k < n; k++) p.Add(pts[(start + k) % n]);
        var isCorner = new bool[n + 1];
        foreach (int c in corners) isCorner[(c - start + n) % n] = true;
        p.Add(p[0]);
        isCorner[n] = isCorner[0];
        SmoothBetweenCorners(p, isCorner);

        if (corners.Count == 0)
        {
            // Smooth all round: fit two halves that meet with matching tangents.
            int half = n / 2;
            var t0 = CentralTangent(p, 0, closed: true);
            var th = CentralTangent(p, half, closed: true);
            FitRun(p, 0, half, t0, -th, eps, b);
            FitRun(p, half, n, th, -t0, eps, b);
        }
        else
        {
            int from = 0;
            for (int k = 1; k <= n; k++)
            {
                if (!isCorner[k] && k != n) continue;
                FitRun(p, from, k, null, null, eps, b);
                from = k;
            }
        }
        return new Subpath(p[0], b.ToImmutable(), true);
    }

    static List<Vec2> Dedupe(List<Vec2> loop)
    {
        var o = new List<Vec2>(loop.Count);
        foreach (var q in loop)
            if (o.Count == 0 || o[^1].Dist(q) > 1e-6) o.Add(q);
        while (o.Count > 1 && o[0].Dist(o[^1]) <= 1e-6) o.RemoveAt(o.Count - 1);
        return o;
    }

    /// <summary>
    /// Indices where the outline turns more than <paramref name="cornerDeg"/>,
    /// measured between points <paramref name="radius"/> px behind and ahead
    /// (so a corner softened over a pixel or two still counts as one corner).
    /// </summary>
    internal static List<int> FindCorners(List<Vec2> p, double cornerDeg, double radius)
    {
        int n = p.Count;
        var turn = new double[n];
        for (int i = 0; i < n; i++)
        {
            Vec2 back = p[i], ahead = p[i];
            double d = 0;
            for (int k = 1; k < n && d < radius; k++) { d += p[(i - k + 1 + n) % n].Dist(p[(i - k + n) % n]); back = p[(i - k + n) % n]; }
            d = 0;
            for (int k = 1; k < n && d < radius; k++) { d += p[(i + k - 1) % n].Dist(p[(i + k) % n]); ahead = p[(i + k) % n]; }
            var a = p[i] - back;
            var c = ahead - p[i];
            double la = a.Length, lc = c.Length;
            turn[i] = la < 1e-9 || lc < 1e-9 ? 0 : Math.Acos(Math.Clamp(a.Dot(c) / (la * lc), -1, 1)) * 180 / Math.PI;
        }
        // One corner per run of over-threshold points: the sharpest.
        var o = new List<int>();
        int first = Enumerable.Range(0, n).FirstOrDefault(i => turn[i] <= cornerDeg, -1);
        if (first < 0) return o; // every point is "sharp": treat as smooth (tiny blob)
        for (int k = 1; k <= n; k++)
        {
            int i = (first + k) % n;
            if (turn[i] <= cornerDeg) continue;
            int best = i;
            while (turn[(i + 1) % n] > cornerDeg && (i + 1) % n != first)
            {
                i = (i + 1) % n;
                k++;
                if (turn[i] > turn[best]) best = i;
            }
            o.Add(best);
        }
        o.Sort();
        return o;
    }

    /// <summary>One light averaging pass that leaves corners exactly where they are.</summary>
    static void SmoothBetweenCorners(List<Vec2> p, bool[] isCorner)
    {
        int n = p.Count - 1; // last point repeats the first
        var copy = p.ToArray();
        for (int i = 0; i < n; i++)
        {
            if (isCorner[i]) continue;
            var prev = copy[(i - 1 + n) % n];
            var next = copy[(i + 1) % n];
            p[i] = (prev + copy[i] * 2 + next) / 4;
        }
        p[n] = p[0];
    }

    static Vec2 Unit(Vec2 v) => v.Length < 1e-12 ? new Vec2(1, 0) : v / v.Length;

    /// <summary>Direction of travel at point i, from neighbours about 2 px either side.</summary>
    static Vec2 CentralTangent(List<Vec2> p, int i, bool closed)
    {
        int n = closed ? p.Count - 1 : p.Count;
        Vec2 ahead = p[i], back = p[i];
        for (int k = 1; k < n; k++) { ahead = p[closed ? (i + k) % n : Math.Min(i + k, n - 1)]; if (ahead.Dist(p[i]) >= 2) break; }
        for (int k = 1; k < n; k++) { back = p[closed ? (i - k + n) % n : Math.Max(i - k, 0)]; if (back.Dist(p[i]) >= 2) break; }
        return Unit(ahead - back);
    }

    /// <summary>
    /// Points p[from..to] → segments. Straight if every point is within eps of
    /// the chord; otherwise cubic curves. Tangents: null = one-sided (corner).
    /// </summary>
    static void FitRun(List<Vec2> p, int from, int to, Vec2? startDir, Vec2? endDir, double eps, ImmutableArray<Seg>.Builder o)
    {
        if (to <= from) return;
        double dev = 0;
        for (int i = from + 1; i < to; i++) dev = Math.Max(dev, Geom.SegmentDistance(p[i], p[from], p[to]));
        if (dev <= eps * 0.75 || to - from < 3)
        {
            o.Add(Seg.Line(p[to]));
            return;
        }
        var t1 = startDir ?? OneSided(p, from, to, +1);
        var t2 = endDir ?? OneSided(p, to, from, -1);
        FitCubic(p, from, to, t1, t2, eps, o, 0);
    }

    /// <summary>Tangent at an end of a run, looking into the run over about 3 px.</summary>
    static Vec2 OneSided(List<Vec2> p, int at, int other, int step)
    {
        int i = at;
        while (i != other && p[i].Dist(p[at]) < 3) i += step;
        return Unit(p[i] - p[at]);
    }

    // Schneider, "An Algorithm for Automatically Fitting Digitized Curves" (Graphics Gems, 1990).
    // tHat1 points from d[first] into the curve; tHat2 points from d[last] back into it.
    static void FitCubic(List<Vec2> d, int first, int last, Vec2 tHat1, Vec2 tHat2, double error, ImmutableArray<Seg>.Builder o, int depth)
    {
        double err2 = error * error;
        if (last - first == 1)
        {
            double dist = d[first].Dist(d[last]) / 3;
            o.Add(Seg.Cubic(d[first] + tHat1 * dist, d[last] + tHat2 * dist, d[last]));
            return;
        }
        var u = ChordParams(d, first, last);
        var bez = Generate(d, first, last, u, tHat1, tHat2);
        var (maxErr, split) = MaxError(d, first, last, bez, u);
        if (maxErr <= err2)
        {
            o.Add(Seg.Cubic(bez[1], bez[2], bez[3]));
            return;
        }
        if (maxErr <= err2 * 9)
            for (int it = 0; it < 6; it++)
            {
                u = Reparameterize(d, first, last, u, bez);
                bez = Generate(d, first, last, u, tHat1, tHat2);
                (maxErr, split) = MaxError(d, first, last, bez, u);
                if (maxErr <= err2)
                {
                    o.Add(Seg.Cubic(bez[1], bez[2], bez[3]));
                    return;
                }
            }
        if (depth > 40)
        {
            o.Add(Seg.Cubic(bez[1], bez[2], bez[3]));
            return;
        }
        var centre = Unit(d[split - 1] - d[split + 1]);
        FitCubic(d, first, split, tHat1, centre, error, o, depth + 1);
        FitCubic(d, split, last, -centre, tHat2, error, o, depth + 1);
    }

    static double[] ChordParams(List<Vec2> d, int first, int last)
    {
        var u = new double[last - first + 1];
        for (int i = first + 1; i <= last; i++) u[i - first] = u[i - first - 1] + d[i].Dist(d[i - 1]);
        double total = u[^1];
        for (int i = 1; i < u.Length; i++) u[i] = total > 0 ? u[i] / total : (double)i / (u.Length - 1);
        return u;
    }

    static Vec2[] Generate(List<Vec2> d, int first, int last, double[] u, Vec2 tHat1, Vec2 tHat2)
    {
        Vec2 p0 = d[first], p3 = d[last];
        double c00 = 0, c01 = 0, c11 = 0, x0 = 0, x1 = 0;
        for (int i = 0; i < u.Length; i++)
        {
            double t = u[i], mt = 1 - t;
            double b0 = mt * mt * mt, b1 = 3 * t * mt * mt, b2 = 3 * t * t * mt, b3 = t * t * t;
            var a0 = tHat1 * b1;
            var a1 = tHat2 * b2;
            c00 += a0.Dot(a0);
            c01 += a0.Dot(a1);
            c11 += a1.Dot(a1);
            var tmp = d[first + i] - (p0 * (b0 + b1) + p3 * (b2 + b3));
            x0 += a0.Dot(tmp);
            x1 += a1.Dot(tmp);
        }
        double det = c00 * c11 - c01 * c01;
        double segLen = p0.Dist(p3);
        double al = Math.Abs(det) < 1e-12 ? 0 : (x0 * c11 - x1 * c01) / det;
        double ar = Math.Abs(det) < 1e-12 ? 0 : (c00 * x1 - c01 * x0) / det;
        // Degenerate or wild handles: fall back to the classic one-third rule.
        double small = 1e-6 * segLen;
        if (al < small || ar < small || al > segLen * 2 || ar > segLen * 2) al = ar = segLen / 3;
        return [p0, p0 + tHat1 * al, p3 + tHat2 * ar, p3];
    }

    static Vec2 Bez(Vec2[] b, double t)
    {
        double mt = 1 - t;
        return b[0] * (mt * mt * mt) + b[1] * (3 * t * mt * mt) + b[2] * (3 * t * t * mt) + b[3] * (t * t * t);
    }

    static (double MaxErr, int Split) MaxError(List<Vec2> d, int first, int last, Vec2[] bez, double[] u)
    {
        double max = 0;
        int split = (first + last) / 2;
        for (int i = first + 1; i < last; i++)
        {
            var diff = Bez(bez, u[i - first]) - d[i];
            double e = diff.Dot(diff);
            if (e >= max) { max = e; split = i; }
        }
        return (max, Math.Clamp(split, first + 1, last - 1));
    }

    static double[] Reparameterize(List<Vec2> d, int first, int last, double[] u, Vec2[] bez)
    {
        Vec2[] q1 = [(bez[1] - bez[0]) * 3, (bez[2] - bez[1]) * 3, (bez[3] - bez[2]) * 3];
        Vec2[] q2 = [(q1[1] - q1[0]) * 2, (q1[2] - q1[1]) * 2];
        var o = new double[u.Length];
        for (int i = 0; i < u.Length; i++)
        {
            double t = u[i], mt = 1 - t;
            var q = Bez(bez, t);
            var d1 = q1[0] * (mt * mt) + q1[1] * (2 * t * mt) + q1[2] * (t * t);
            var d2 = q2[0] * mt + q2[1] * t;
            var diff = q - d[first + i];
            double num = diff.Dot(d1), den = d1.Dot(d1) + diff.Dot(d2);
            o[i] = Math.Abs(den) < 1e-12 ? t : Math.Clamp(t - num / den, 0, 1);
        }
        return o;
    }
}
