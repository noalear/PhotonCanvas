using System.Collections.Immutable;
using YTool.Core;

namespace YTool.Doc.Geometry;

/// <summary>
/// Turns a dense closed polygon (in mm) back into lines and cubic curves:
/// corners stay sharp, straight edges stay single lines, and smooth runs
/// become as few curves as the tolerance allows.
/// </summary>
internal static class CurveFit
{
    /// <summary>A turn sharper than this is a corner, never smoothed over.</summary>
    const double CornerRad = 35 * Math.PI / 180;

    /// <summary>A straight edge meeting a curve within this angle is treated as tangent to it.</summary>
    const double TangentRad = 10 * Math.PI / 180;

    /// <summary>Lines and curves within <paramref name="tol"/> of the closed polygon <paramref name="pts"/>.</summary>
    public static Subpath Closed(List<Vec2> pts, double tol)
    {
        var p = SimplifyClosed(pts, tol / 8);
        int n = p.Count;
        var b = ImmutableArray.CreateBuilder<Seg>();
        if (n < 3)
        {
            for (int k = 1; k <= n; k++) b.Add(Seg.Line(p[k % n]));
            return new Subpath(p[0], b.ToImmutable(), true);
        }

        var turn = new double[n];
        for (int i = 0; i < n; i++)
        {
            var a = p[i] - p[(i - 1 + n) % n];
            var c = p[(i + 1) % n] - p[i];
            turn[i] = Math.Abs(Math.Atan2(a.Cross(c), a.Dot(c)));
        }
        var corner = turn.Select(t => t > CornerRad).ToArray();
        // A segment is a real straight edge when it is too long to be a chord of
        // a curve that turns as much as its ends do (its sagitta would exceed tol/2).
        var straight = new bool[n];
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            double len = p[i].Dist(p[j]);
            double ta = corner[i] ? 0 : turn[i], tb = corner[j] ? 0 : turn[j];
            straight[i] = corner[i] && corner[j] || len * Math.Max(ta, tb) / 8 > tol / 2;
        }
        bool Break(int i) => corner[i] || straight[i] || straight[(i - 1 + n) % n];

        int start = Enumerable.Range(0, n).FirstOrDefault(Break, -1);
        if (start < 0)
        {
            // Smooth all round: two halves that meet with matching tangents.
            var d0 = new List<Vec2>(p) { p[0] };
            int h = n / 2;
            var t0 = Unit(d0[1] - d0[n - 1]);
            var th = Unit(d0[h + 1] - d0[h - 1]);
            FitRun(d0, 0, h, t0, -th, tol, b);
            FitRun(d0, h, n, th, -t0, tol, b);
            return new Subpath(d0[0], b.ToImmutable(), true);
        }

        // Rotate so the loop starts at a break; d[n] repeats d[0].
        var d = new List<Vec2>(n + 1);
        for (int k = 0; k <= n; k++) d.Add(p[(start + k) % n]);
        bool Corner(int k) => corner[(start + k) % n];
        bool Straight(int k) => straight[(start + k) % n];
        double Turn(int k) => turn[(start + k) % n];
        bool IsBreak(int k) => Break((start + k) % n);

        int at = 0;
        while (at < n)
        {
            if (Straight(at))
            {
                b.Add(Seg.Line(d[at + 1]));
                at++;
                continue;
            }
            int to = at + 1;
            while (to < n && !IsBreak(to)) to++;
            var t1 = !Corner(at) && Straight((at - 1 + n) % n) && Turn(at) < TangentRad
                ? Unit(d[at] - d[at == 0 ? n - 1 : at - 1])
                : OneSided(d, at, to);
            var t2 = !Corner(to % n) && Straight(to % n) && Turn(to % n) < TangentRad
                ? -Unit(d[to % n + 1] - d[to % n])
                : OneSided(d, to, at);
            FitRun(d, at, to, t1, t2, tol, b);
            at = to;
        }
        return new Subpath(d[0], b.ToImmutable(), true);
    }

    static Vec2 Unit(Vec2 v) => v.Length < 1e-12 ? new Vec2(1, 0) : v / v.Length;

    /// <summary>Tangent at an end of a run, pointing into it (second order, from three points when there are three).</summary>
    static Vec2 OneSided(List<Vec2> d, int at, int other)
    {
        int s = other > at ? 1 : -1;
        var u1 = Unit(d[at + s] - d[at]);
        if (Math.Abs(other - at) < 2) return u1;
        var u2 = Unit(d[at + 2 * s] - d[at + s]);
        var t = u1 * 1.5 - u2 * 0.5;
        return t.Length < 1e-9 ? u1 : Unit(t);
    }

    /// <summary>Points d[from..to] as a line if they are straight, otherwise as cubic curves.</summary>
    static void FitRun(List<Vec2> d, int from, int to, Vec2 t1, Vec2 t2, double tol, ImmutableArray<Seg>.Builder o)
    {
        if (to <= from) return;
        double dev = 0;
        for (int i = from + 1; i < to; i++) dev = Math.Max(dev, Geom.SegmentDistance(d[i], d[from], d[to]));
        if (to - from == 1 || dev <= tol / 4)
        {
            o.Add(Seg.Line(d[to]));
            return;
        }
        FitCubic(d, from, to, t1, t2, tol / 2, o, 0);
    }

    // Schneider, "An Algorithm for Automatically Fitting Digitized Curves" (Graphics Gems, 1990),
    // with the error also checked halfway between samples so long chords cannot bulge.
    static void FitCubic(List<Vec2> d, int first, int last, Vec2 tHat1, Vec2 tHat2, double error, ImmutableArray<Seg>.Builder o, int depth)
    {
        if (last - first == 1)
        {
            o.Add(Seg.Line(d[last]));
            return;
        }
        double err2 = error * error;
        var u = ChordParams(d, first, last);
        var bez = Generate(d, first, last, u, tHat1, tHat2);
        var (maxErr, split) = MaxError(d, first, last, bez, u);
        if (maxErr <= err2)
        {
            o.Add(Seg.Cubic(bez[1], bez[2], bez[3]));
            return;
        }
        if (maxErr <= err2 * 16)
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
        if (depth >= 32)
        {
            for (int i = first + 1; i <= last; i++) o.Add(Seg.Line(d[i]));
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
        double al = Math.Abs(det) < 1e-18 ? 0 : (x0 * c11 - x1 * c01) / det;
        double ar = Math.Abs(det) < 1e-18 ? 0 : (c00 * x1 - c01 * x0) / det;
        // Degenerate or wild handles: fall back to the one-third rule.
        double small = 1e-6 * segLen;
        if (al < small || ar < small || al > segLen * 2 || ar > segLen * 2) al = ar = segLen / 3;
        return [p0, p0 + tHat1 * al, p3 + tHat2 * ar, p3];
    }

    static Vec2 Bez(Vec2[] b, double t)
    {
        double mt = 1 - t;
        return b[0] * (mt * mt * mt) + b[1] * (3 * t * mt * mt) + b[2] * (3 * t * t * mt) + b[3] * (t * t * t);
    }

    /// <summary>Largest squared error at the samples and halfway between them, and where to split.</summary>
    static (double MaxErr, int Split) MaxError(List<Vec2> d, int first, int last, Vec2[] bez, double[] u)
    {
        double max = 0;
        int split = (first + last) / 2;
        for (int i = first; i < last; i++)
        {
            if (i > first)
            {
                var diff = Bez(bez, u[i - first]) - d[i];
                double e = diff.Dot(diff);
                if (e >= max) { max = e; split = i; }
            }
            double m = Geom.SegmentDistance(Bez(bez, (u[i - first] + u[i + 1 - first]) / 2), d[i], d[i + 1]);
            if (m * m > max) { max = m * m; split = i == first ? i + 1 : i; }
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
        o[0] = 0;
        o[^1] = 1;
        return o;
    }

    /// <summary>Douglas–Peucker on a closed loop (the closing point is implicit).</summary>
    internal static List<Vec2> SimplifyClosed(List<Vec2> pts, double eps)
    {
        int n = pts.Count;
        if (n < 4) return pts;
        int far = 0;
        double best = -1;
        for (int i = 1; i < n; i++)
        {
            double dd = pts[i].Dist(pts[0]);
            if (dd > best) { best = dd; far = i; }
        }
        var keep = new bool[n + 1];
        keep[0] = keep[far] = keep[n] = true;
        Vec2 At(int i) => pts[i % n];
        var stack = new Stack<(int, int)>();
        stack.Push((0, far));
        stack.Push((far, n));
        while (stack.Count > 0)
        {
            var (a, c) = stack.Pop();
            double maxD = -1;
            int idx = -1;
            for (int i = a + 1; i < c; i++)
            {
                double dd = Geom.SegmentDistance(At(i), At(a), At(c));
                if (dd > maxD) { maxD = dd; idx = i; }
            }
            if (idx >= 0 && maxD > eps)
            {
                keep[idx] = true;
                stack.Push((a, idx));
                stack.Push((idx, c));
            }
        }
        var o = new List<Vec2>();
        for (int i = 0; i < n; i++) if (keep[i]) o.Add(pts[i]);
        return o;
    }
}
