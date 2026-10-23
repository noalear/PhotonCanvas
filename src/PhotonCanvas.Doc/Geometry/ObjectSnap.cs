using PhotonCanvas.Core;

namespace PhotonCanvas.Doc.Geometry;

/// <summary>Which object snaps are on (AutoCAD's running object snaps).</summary>
[Flags]
public enum SnapKinds
{
    None = 0,
    Endpoint = 1,
    Midpoint = 2,
    Center = 4,
    Quadrant = 8,
    Intersection = 16,
    Perpendicular = 32,
    Tangent = 64,
    Nearest = 128,
    /// <summary>All but Nearest (which makes everything sticky).</summary>
    Default = Endpoint | Midpoint | Center | Quadrant | Intersection | Perpendicular | Tangent,
    All = Default | Nearest,
}

/// <summary>One kind of snap point.</summary>
public enum SnapKind { Endpoint, Midpoint, Center, Quadrant, Intersection, Perpendicular, Tangent, Nearest }

/// <summary>Where the pointer snaps to, and what kind of point that is.</summary>
public readonly record struct SnapHit(Vec2 At, SnapKind Kind);

/// <summary>
/// A circle, ellipse or arc found in a shape: its centre, and for a whole circle or ellipse its four
/// quadrant points. <see cref="Radius"/> is 0 for an ellipse (no tangents to it).
/// </summary>
public readonly record struct SnapCircle(Vec2 Center, double Radius, bool Whole, Vec2[] Quadrants);

/// <summary>What can be snapped to on one shape (world mm), worked out once per shape.</summary>
public sealed class SnapFeatures
{
    public required Rect2 Bounds { get; init; }
    /// <summary>Corners and the ends of lines and curves.</summary>
    public required List<Vec2> Ends { get; init; }
    /// <summary>The middle of each line or curve, along its length.</summary>
    public required List<Vec2> Mids { get; init; }
    public required List<SnapCircle> Circles { get; init; }
    /// <summary>The middle of closed outlines that aren't round (a rectangle's centre).</summary>
    public required List<Vec2> Centres { get; init; }
    /// <summary>The outlines as fine polylines (for intersections, nearest and perpendicular).</summary>
    public required List<List<Vec2>> Lines { get; init; }
}

/// <summary>
/// Object snaps, as in AutoCAD: the ends, middles, centres, quadrants and crossings of other shapes,
/// and the perpendicular and tangent points from where a line starts. The point nearest the pointer
/// wins, ends and crossings first; Nearest (anywhere on a line) only when nothing else is close.
/// </summary>
public static class ObjectSnap
{
    const double Flat = 0.01;

    /// <summary>The snap points of a vector shape's outline (world mm).</summary>
    public static SnapFeatures Features(PathGeometry world)
    {
        var ends = new List<Vec2>();
        var mids = new List<Vec2>();
        var circles = new List<SnapCircle>();
        var centres = new List<Vec2>();
        var lines = new List<List<Vec2>>();
        foreach (var sp in world.Subpaths)
        {
            if (sp.Segs.IsEmpty) continue;
            var flat = sp.Flatten(Flat);
            if (sp.Closed && flat[0] != flat[^1]) flat.Add(flat[0]);
            lines.Add(flat);
            var p = sp.Start;
            var spEnds = new List<Vec2> { p };
            var spMids = new List<Vec2>();
            var arcs = new List<(Vec2 C, double R)>();
            foreach (var s in sp.Segs)
            {
                spEnds.Add(s.To);
                if (s.Kind == SegKind.Line) spMids.Add((p + s.To) * 0.5);
                else
                {
                    spMids.Add(CubicMidByLength(p, s.C1, s.C2, s.To));
                    if (Arc(p, s.C1, s.C2, s.To) is { } arc) arcs.Add(arc);
                }
                p = s.To;
            }
            bool round = sp.Closed && arcs.Count == sp.Segs.Length && arcs.Count > 0 &&
                         arcs.All(a => a.C.Dist(arcs[0].C) <= 1e-3 * arcs[0].R + 0.01 && Math.Abs(a.R - arcs[0].R) <= 1e-3 * arcs[0].R + 0.01);
            if (round)
            {
                var c = arcs[0].C;
                double r = arcs.Average(a => a.R);
                // A circle has no ends or middles, only its quadrants (where its curves join).
                circles.Add(new(c, r, true, [new(c.X + r, c.Y), new(c.X, c.Y - r), new(c.X - r, c.Y), new(c.X, c.Y + r)]));
            }
            else if (Ellipse(sp) is { } e) circles.Add(e);
            else
            {
                ends.AddRange(spEnds);
                mids.AddRange(spMids);
                // Arcs inside an outline (a rounded corner): their centres, one each.
                foreach (var a in arcs)
                    if (!circles.Any(o => o.Center.Dist(a.C) <= 0.01 && Math.Abs(o.Radius - a.R) <= 0.01)) circles.Add(new(a.C, a.R, false, []));
                if (sp.Closed && Centroid(flat) is { } g) centres.Add(g);
            }
        }
        // A closed outline's start is also its end: once is enough.
        var distinct = new List<Vec2>();
        foreach (var e in ends) if (!distinct.Any(d => d.Dist(e) < 1e-6)) distinct.Add(e);
        return new SnapFeatures
        {
            Bounds = Rect2.Bounds(lines.SelectMany(l => l)),
            Ends = distinct, Mids = mids, Circles = circles, Centres = centres, Lines = lines,
        };
    }

    /// <summary>A picture's snap points: its corners, the middles of its sides and its centre.</summary>
    public static SnapFeatures Features(IReadOnlyList<Vec2> corners)
    {
        var mids = Enumerable.Range(0, corners.Count).Select(i => (corners[i] + corners[(i + 1) % corners.Count]) * 0.5).ToList();
        var loop = corners.Append(corners[0]).ToList();
        return new SnapFeatures
        {
            Bounds = Rect2.Bounds(corners),
            Ends = [.. corners], Mids = mids, Circles = [],
            Centres = [corners.Aggregate(new Vec2(0, 0), (a, b) => a + b) * (1.0 / corners.Count)],
            Lines = [loop],
        };
    }

    /// <summary>
    /// The snap point nearest <paramref name="cursor"/> (within <paramref name="tol"/> mm) on the shapes,
    /// or null. <paramref name="from"/> is where the line being drawn starts (for perpendicular and tangent).
    /// </summary>
    public static SnapHit? Find(IEnumerable<SnapFeatures> shapes, Vec2 cursor, double tol, SnapKinds kinds, Vec2? from = null)
    {
        if (kinds == SnapKinds.None || !(tol > 0)) return null;
        var near = shapes.Where(f => f.Bounds.Inflate(tol).Contains(cursor)).ToList();
        if (near.Count == 0) return null;
        SnapHit? best = null;
        double bestScore = double.PositiveInfinity;
        // The score is the distance from the pointer, plus a little for the less precise kinds.
        void Offer(Vec2 at, SnapKind kind, double extra, double? distance = null)
        {
            double d = distance ?? at.Dist(cursor);
            if (d > tol || !at.IsFinite) return;
            double score = d + extra * tol;
            if (score < bestScore) { bestScore = score; best = new SnapHit(at, kind); }
        }
        bool On(SnapKinds k) => (kinds & k) != 0;

        // The bits of outline near the pointer.
        var box = new Rect2(cursor.X - tol, cursor.Y - tol, cursor.X + tol, cursor.Y + tol);
        var segs = new List<(Vec2 A, Vec2 B, int Line, int Index, int Count)>();
        int lineId = 0;
        foreach (var f in near)
        {
            if (On(SnapKinds.Endpoint)) foreach (var e in f.Ends) Offer(e, SnapKind.Endpoint, 0);
            if (On(SnapKinds.Midpoint)) foreach (var m in f.Mids) Offer(m, SnapKind.Midpoint, 0.15);
            foreach (var c in f.Circles)
            {
                if (On(SnapKinds.Quadrant) && c.Whole) foreach (var q in c.Quadrants) Offer(q, SnapKind.Quadrant, 0.1);
                if (On(SnapKinds.Center))
                {
                    Offer(c.Center, SnapKind.Center, 0);
                    // Pointing at a circle's edge finds its centre, however far away (as in AutoCAD).
                    if (c.Radius > 0 && Math.Abs(cursor.Dist(c.Center) - c.Radius) <= tol && OnLines(f, cursor, tol))
                        Offer(c.Center, SnapKind.Center, 0.5, Math.Abs(cursor.Dist(c.Center) - c.Radius));
                    else if (c.Radius == 0 && OnLines(f, cursor, tol))
                        Offer(c.Center, SnapKind.Center, 0.5, DistanceToLines(f, cursor));
                }
                if (from is { } o && c.Radius > 0 && Math.Abs(cursor.Dist(c.Center) - c.Radius) <= tol)
                {
                    if (On(SnapKinds.Tangent))
                        foreach (var t in Tangents(o, c.Center, c.Radius))
                            if (c.Whole || OnLines(f, t, 0.05)) Offer(t, SnapKind.Tangent, 0.3);
                    if (On(SnapKinds.Perpendicular) && o.Dist(c.Center) > 1e-9)
                    {
                        var u = (o - c.Center) * (1 / o.Dist(c.Center));
                        foreach (var foot in new[] { c.Center + u * c.Radius, c.Center - u * c.Radius })
                            if (c.Whole || OnLines(f, foot, 0.05)) Offer(foot, SnapKind.Perpendicular, 0.3);
                    }
                }
            }
            if (On(SnapKinds.Center)) foreach (var g in f.Centres) Offer(g, SnapKind.Center, 0.05);
            foreach (var line in f.Lines)
            {
                for (int i = 0; i + 1 < line.Count; i++)
                {
                    var a = line[i];
                    var b = line[i + 1];
                    if (!Rect2.FromPoints(a, b).Intersects(box)) continue;
                    segs.Add((a, b, lineId, i, line.Count - 1));
                }
                lineId++;
            }
        }

        if (On(SnapKinds.Intersection))
            for (int i = 0; i < segs.Count; i++)
                for (int j = i + 1; j < segs.Count; j++)
                {
                    var (a, b) = (segs[i], segs[j]);
                    // Neighbouring pieces of one outline meet at their shared point, which isn't a crossing.
                    if (a.Line == b.Line && (Math.Abs(a.Index - b.Index) <= 1 || Math.Abs(a.Index - b.Index) == a.Count - 1)) continue;
                    if (Cross(a.A, a.B, b.A, b.B) is { } x) Offer(x, SnapKind.Intersection, 0);
                }
        foreach (var s in segs)
        {
            if (from is { } o && On(SnapKinds.Perpendicular) && Foot(o, s.A, s.B) is { } foot && foot.Dist(o) > 1e-6)
                Offer(foot, SnapKind.Perpendicular, 0.3);
            if (On(SnapKinds.Nearest)) Offer(Closest(cursor, s.A, s.B), SnapKind.Nearest, 1);
        }
        return best;
    }

    static bool OnLines(SnapFeatures f, Vec2 p, double tol) => DistanceToLines(f, p) <= tol;

    static double DistanceToLines(SnapFeatures f, Vec2 p)
    {
        double best = double.PositiveInfinity;
        foreach (var l in f.Lines)
            for (int i = 0; i + 1 < l.Count; i++) best = Math.Min(best, Closest(p, l[i], l[i + 1]).Dist(p));
        return best;
    }

    static Vec2 Closest(Vec2 p, Vec2 a, Vec2 b)
    {
        var d = b - a;
        double len2 = d.X * d.X + d.Y * d.Y;
        if (len2 < 1e-18) return a;
        double t = Math.Clamp(((p.X - a.X) * d.X + (p.Y - a.Y) * d.Y) / len2, 0, 1);
        return a + d * t;
    }

    /// <summary>The foot of the perpendicular from <paramref name="p"/> onto segment a–b, if it lands on it.</summary>
    static Vec2? Foot(Vec2 p, Vec2 a, Vec2 b)
    {
        var d = b - a;
        double len2 = d.X * d.X + d.Y * d.Y;
        if (len2 < 1e-18) return null;
        double t = ((p.X - a.X) * d.X + (p.Y - a.Y) * d.Y) / len2;
        return t is >= 0 and <= 1 ? a + d * t : null;
    }

    /// <summary>Where segments a–b and c–d cross, or null.</summary>
    static Vec2? Cross(Vec2 a, Vec2 b, Vec2 c, Vec2 d)
    {
        var r = b - a;
        var s = d - c;
        double den = r.X * s.Y - r.Y * s.X;
        if (Math.Abs(den) < 1e-14) return null;
        var q = c - a;
        double t = (q.X * s.Y - q.Y * s.X) / den, u = (q.X * r.Y - q.Y * r.X) / den;
        const double E = 1e-9;
        return t is >= -E and <= 1 + E && u is >= -E and <= 1 + E ? a + r * t : null;
    }

    /// <summary>The points where lines from <paramref name="p"/> touch the circle.</summary>
    static IEnumerable<Vec2> Tangents(Vec2 p, Vec2 c, double r)
    {
        double d = p.Dist(c);
        if (d <= r + 1e-9) yield break;
        double a = Math.Atan2(p.Y - c.Y, p.X - c.X), b = Math.Acos(r / d);
        yield return c + new Vec2(Math.Cos(a + b), Math.Sin(a + b)) * r;
        yield return c + new Vec2(Math.Cos(a - b), Math.Sin(a - b)) * r;
    }

    static Vec2 Bezier(Vec2 p0, Vec2 p1, Vec2 p2, Vec2 p3, double t)
    {
        double u = 1 - t;
        return p0 * (u * u * u) + p1 * (3 * u * u * t) + p2 * (3 * u * t * t) + p3 * (t * t * t);
    }

    /// <summary>The point halfway along a curve's length.</summary>
    static Vec2 CubicMidByLength(Vec2 p0, Vec2 p1, Vec2 p2, Vec2 p3)
    {
        const int N = 64;
        var pts = new Vec2[N + 1];
        var len = new double[N + 1];
        for (int i = 0; i <= N; i++)
        {
            pts[i] = Bezier(p0, p1, p2, p3, (double)i / N);
            if (i > 0) len[i] = len[i - 1] + pts[i].Dist(pts[i - 1]);
        }
        double half = len[N] / 2;
        for (int i = 1; i <= N; i++)
            if (len[i] >= half)
            {
                double k = len[i] - len[i - 1] < 1e-12 ? 0 : (half - len[i - 1]) / (len[i] - len[i - 1]);
                return pts[i - 1] + (pts[i] - pts[i - 1]) * k;
            }
        return pts[N / 2];
    }

    /// <summary>A curve that's a circular arc: its centre and radius (the curve stays within 0.2 % of it).</summary>
    static (Vec2 C, double R)? Arc(Vec2 p0, Vec2 p1, Vec2 p2, Vec2 p3)
    {
        var a = p0;
        var m = Bezier(p0, p1, p2, p3, 0.5);
        var b = p3;
        double d = 2 * (a.X * (m.Y - b.Y) + m.X * (b.Y - a.Y) + b.X * (a.Y - m.Y));
        if (Math.Abs(d) < 1e-12) return null;
        double a2 = a.X * a.X + a.Y * a.Y, m2 = m.X * m.X + m.Y * m.Y, b2 = b.X * b.X + b.Y * b.Y;
        var c = new Vec2((a2 * (m.Y - b.Y) + m2 * (b.Y - a.Y) + b2 * (a.Y - m.Y)) / d, (a2 * (b.X - m.X) + m2 * (a.X - b.X) + b2 * (m.X - a.X)) / d);
        double r = c.Dist(a);
        if (!double.IsFinite(r) || r < 1e-6) return null;
        double tol = Math.Max(0.002 * r, 0.005);
        foreach (var t in new[] { 0.2, 0.35, 0.65, 0.8 })
            if (Math.Abs(Bezier(p0, p1, p2, p3, t).Dist(c) - r) > tol) return null;
        return (c, r);
    }

    /// <summary>An ellipse drawn as four curves (as the Ellipse tool makes it): its centre and the ends of its axes.</summary>
    static SnapCircle? Ellipse(Subpath sp)
    {
        if (!sp.Closed || sp.Segs.Length != 4 || sp.Segs.Any(s => s.Kind != SegKind.Cubic)) return null;
        var v = new[] { sp.Start, sp.Segs[0].To, sp.Segs[1].To, sp.Segs[2].To };
        var c1 = (v[0] + v[2]) * 0.5;
        var c2 = (v[1] + v[3]) * 0.5;
        double size = Math.Max(v[0].Dist(v[2]), v[1].Dist(v[3]));
        if (c1.Dist(c2) > 1e-3 * size + 0.005) return null;
        var c = (c1 + c2) * 0.5;
        // The four points are the ends of the axes: they lie at right angles round the middle.
        var a = v[0] - c;
        var b = v[1] - c;
        if (Math.Abs(a.X * b.X + a.Y * b.Y) > 0.02 * a.Length * b.Length) return null;
        return new SnapCircle(c, 0, true, v);
    }

    /// <summary>The middle (centroid) of a closed polyline's area, or null for one with no area.</summary>
    static Vec2? Centroid(List<Vec2> p)
    {
        double a = 0, cx = 0, cy = 0;
        for (int i = 0; i + 1 < p.Count; i++)
        {
            double k = p[i].X * p[i + 1].Y - p[i + 1].X * p[i].Y;
            a += k;
            cx += (p[i].X + p[i + 1].X) * k;
            cy += (p[i].Y + p[i + 1].Y) * k;
        }
        if (Math.Abs(a) < 1e-9) return null;
        return new Vec2(cx / (3 * a), cy / (3 * a));
    }
}
