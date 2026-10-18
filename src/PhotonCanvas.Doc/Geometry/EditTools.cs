using System.Collections.Immutable;
using PhotonCanvas.Core;

namespace PhotonCanvas.Doc.Geometry;

/// <summary>Editing tools: scissors, removing overlapping lines, tangent circles, shadows, fitting.</summary>
public static class EditTools
{
    static Vec2 Bezier(Vec2 p0, Vec2 c1, Vec2 c2, Vec2 p3, double t)
    {
        double u = 1 - t;
        return p0 * (u * u * u) + c1 * (3 * u * u * t) + c2 * (3 * u * t * t) + p3 * (t * t * t);
    }

    /// <summary>The point of a path nearest <paramref name="p"/>: subpath, segment and parameter, and how far it is.</summary>
    public static (int Sub, int Seg, double T, double Distance) Nearest(PathGeometry g, Vec2 p)
    {
        (int, int, double, double) best = (-1, -1, 0, double.PositiveInfinity);
        for (int s = 0; s < g.Subpaths.Length; s++)
        {
            var sp = g.Subpaths[s];
            var prev = sp.Start;
            for (int k = 0; k < sp.Segs.Length; k++)
            {
                var seg = sp.Segs[k];
                Vec2 At(double t) => seg.Kind == SegKind.Line ? Vec2.Lerp(prev, seg.To, t) : Bezier(prev, seg.C1, seg.C2, seg.To, t);
                // Coarse samples, then narrow down around the best.
                double bt = 0, bd = double.PositiveInfinity;
                for (int i = 0; i <= 32; i++)
                {
                    double t = i / 32.0, d = At(t).Dist(p);
                    if (d < bd) { bd = d; bt = t; }
                }
                double lo = Math.Max(0, bt - 1 / 32.0), hi = Math.Min(1, bt + 1 / 32.0);
                for (int i = 0; i < 40; i++)
                {
                    double a = lo + (hi - lo) / 3, b = hi - (hi - lo) / 3;
                    if (At(a).Dist(p) < At(b).Dist(p)) hi = b; else lo = a;
                }
                bt = (lo + hi) / 2;
                bd = At(bt).Dist(p);
                if (bd < best.Item4) best = (s, k, bt, bd);
                prev = seg.To;
            }
        }
        return best;
    }

    /// <summary>
    /// Scissors: cut the path at the point nearest <paramref name="p"/>. A closed outline opens
    /// there; an open line becomes two. Curves stay curves.
    /// </summary>
    public static PathGeometry? SplitAt(PathGeometry g, Vec2 p)
    {
        var (sub, seg, t, _) = Nearest(g, p);
        if (sub < 0) return null;
        int node;
        if (t < 1e-6) node = seg;
        else if (t > 1 - 1e-6) node = seg + 1;
        else
        {
            g = NodeEdit.Insert(g, sub, seg, t);
            node = seg + 1;
        }
        var sp = g.Subpaths[sub];
        var segs = sp.Segs.ToList();
        Vec2 Point(int i) => i == 0 ? sp.Start : segs[i - 1].To;
        var parts = new List<Subpath>();
        if (sp.Closed)
        {
            if (segs[^1].To.Dist(sp.Start) > 1e-9) segs.Add(Seg.Line(sp.Start));
            int n = segs.Count;
            node %= n;
            var rotated = segs.Skip(node).Concat(segs.Take(node)).ToImmutableArray();
            parts.Add(new Subpath(Point(node), rotated, false));
        }
        else
        {
            if (node <= 0 || node >= segs.Count) return g; // at an end: nothing to cut
            parts.Add(new Subpath(sp.Start, [.. segs.Take(node)], false));
            parts.Add(new Subpath(Point(node), [.. segs.Skip(node)], false));
        }
        var all = g.Subpaths.ToList();
        all.RemoveAt(sub);
        all.InsertRange(sub, parts);
        return new PathGeometry(all);
    }

    /// <summary>
    /// Lines lying on top of each other are cut only once: where a line runs along a line already
    /// kept (within <paramref name="tol"/>), that stretch is left out (shared edges of shapes
    /// side by side). Comes back as open lines, one geometry per input; closed loops that lose
    /// nothing stay closed.
    /// </summary>
    public static (List<PathGeometry> Geometries, double RemovedMm) RemoveOverlaps(IReadOnlyList<PathGeometry> shapes, double tol = 0.02)
    {
        var kept = new List<(Vec2 A, Vec2 B)>();
        var result = new List<PathGeometry>();
        double removed = 0;
        foreach (var g in shapes)
        {
            var subs = new List<Subpath>();
            foreach (var sp in g.Subpaths.Where(s => !s.Segs.IsEmpty))
            {
                var pts = sp.Flatten(0.01);
                if (sp.Closed && pts[0].Dist(pts[^1]) > 1e-9) pts.Add(pts[0]);
                var runs = new List<List<Vec2>>();
                List<Vec2>? run = null;
                bool lostAny = false;
                for (int i = 0; i + 1 < pts.Count; i++)
                {
                    var a = pts[i];
                    var b = pts[i + 1];
                    double len = a.Dist(b);
                    if (len < 1e-9) continue;
                    var free = new List<(double, double)> { (0, 1) };
                    var u = (b - a) / len;
                    foreach (var (c, d) in kept)
                    {
                        if (Geom.SegmentDistance(c, a, b) > tol && Geom.SegmentDistance(d, a, b) > tol && Geom.SegmentDistance(a, c, d) > tol && Geom.SegmentDistance(b, c, d) > tol) continue;
                        var v = d - c;
                        double lv = v.Length;
                        if (lv < 1e-9 || Math.Abs(u.Cross(v / lv)) > 0.02) continue; // not parallel
                        // Both lines on one line?
                        if (Math.Abs((c - a).Cross(u)) > tol) continue;
                        double sc = (c - a).Dot(u) / len, sd = (d - a).Dot(u) / len;
                        double lo = Math.Max(0, Math.Min(sc, sd)), hi = Math.Min(1, Math.Max(sc, sd));
                        if (hi - lo < 1e-9) continue;
                        free = Subtract(free, lo, hi);
                    }
                    double freeLen = free.Sum(f => f.Item2 - f.Item1);
                    if (freeLen < 1 - 1e-9) { lostAny = true; removed += (1 - freeLen) * len; }
                    foreach (var (f0, f1) in free)
                    {
                        var p0 = Vec2.Lerp(a, b, f0);
                        var p1 = Vec2.Lerp(a, b, f1);
                        if (run is not null && run[^1].Dist(p0) < 1e-9) run.Add(p1);
                        else { run = [p0, p1]; runs.Add(run); }
                        kept.Add((p0, p1));
                    }
                    if (free.Count == 0 || free[^1].Item2 < 1 - 1e-9) run = null;
                }
                if (!lostAny && sp.Closed) { subs.Add(sp); continue; }
                if (!lostAny) { subs.Add(sp); continue; }
                foreach (var r in runs.Where(r => r.Count >= 2))
                {
                    var simple = PathTools.Simplify(r, 0.005);
                    subs.Add(new Subpath(simple[0], [.. simple.Skip(1).Select(Seg.Line)], false));
                }
            }
            result.Add(new PathGeometry(subs));
        }
        return (result, removed);
    }

    static List<(double, double)> Subtract(List<(double, double)> free, double lo, double hi)
    {
        var o = new List<(double, double)>();
        foreach (var (a, b) in free)
        {
            if (hi <= a || lo >= b) { o.Add((a, b)); continue; }
            if (lo > a + 1e-9) o.Add((a, lo));
            if (hi < b - 1e-9) o.Add((hi, b));
        }
        return o;
    }

    /// <summary>
    /// Circles touching all three given circles (Apollonius' problem): up to eight, each touching
    /// every circle from inside or outside. Centres and radii in mm.
    /// </summary>
    public static List<(Vec2 Centre, double Radius)> TangentCircles((Vec2 C, double R) a, (Vec2 C, double R) b, (Vec2 C, double R) c)
    {
        var o = new List<(Vec2, double)>();
        foreach (int s1 in new[] { 1, -1 })
            foreach (int s2 in new[] { 1, -1 })
                foreach (int s3 in new[] { 1, -1 })
                {
                    // |P − Ci| = r + si·ri for each circle: subtracting the first from the others
                    // leaves two linear equations in x, y and r.
                    (double X, double Y, double R, double K) Row((Vec2 C, double R) p, int sp, (Vec2 C, double R) q, int sq) =>
                        (2 * (q.C.X - p.C.X), 2 * (q.C.Y - p.C.Y), 2 * (sq * q.R - sp * p.R),
                         q.C.X * q.C.X + q.C.Y * q.C.Y - q.R * q.R - (p.C.X * p.C.X + p.C.Y * p.C.Y - p.R * p.R));
                    var e1 = Row(a, s1, b, s2);
                    var e2 = Row(a, s1, c, s3);
                    double det = e1.X * e2.Y - e1.Y * e2.X;
                    if (Math.Abs(det) < 1e-12) continue;
                    // x = x0 + xr·r, y = y0 + yr·r  from  e.X·x + e.Y·y + e.R·r = e.K
                    double x0 = (e1.K * e2.Y - e2.K * e1.Y) / det, xr = (-e1.R * e2.Y + e2.R * e1.Y) / det;
                    double y0 = (e1.X * e2.K - e2.X * e1.K) / det, yr = (-e1.X * e2.R + e2.X * e1.R) / det;
                    // Into the first circle's equation: a quadratic in r.
                    double dx = x0 - a.C.X, dy = y0 - a.C.Y;
                    double qa = xr * xr + yr * yr - 1, qb = 2 * (dx * xr + dy * yr) - 2 * s1 * a.R, qc = dx * dx + dy * dy - a.R * a.R;
                    var roots = new List<double>();
                    if (Math.Abs(qa) < 1e-12) { if (Math.Abs(qb) > 1e-12) roots.Add(-qc / qb); }
                    else
                    {
                        double disc = qb * qb - 4 * qa * qc;
                        if (disc < 0) continue;
                        roots.Add((-qb + Math.Sqrt(disc)) / (2 * qa));
                        roots.Add((-qb - Math.Sqrt(disc)) / (2 * qa));
                    }
                    foreach (var r in roots.Where(r => r > 1e-6))
                    {
                        var centre = new Vec2(x0 + xr * r, y0 + yr * r);
                        if (!o.Any(x => x.Item1.Dist(centre) < 1e-6 && Math.Abs(x.Item2 - r) < 1e-6)) o.Add((centre, r));
                    }
                }
        return o;
    }

    /// <summary>
    /// A shadow: the shape moved by (dx, dy) minus the shape itself; with <paramref name="steps"/>
    /// above 1, every step along the way joined (a solid 3D extrusion).
    /// </summary>
    public static PathGeometry Shadow(PathGeometry world, double dx, double dy, int steps = 1)
    {
        var area = new PathGeometry(world.Subpaths.Where(s => s.Closed));
        if (area.IsEmpty) return PathGeometry.Empty;
        steps = Math.Clamp(steps, 1, 200);
        var copies = Enumerable.Range(1, steps).Select(k => area.Transform(Affine.Translate(dx * k / steps, dy * k / steps))).ToList();
        var joined = steps == 1 ? copies[0] : ShapeGeometry.Union(copies);
        return ShapeGeometry.Boolean(joined, area, BoolOp.Subtract);
    }
}
