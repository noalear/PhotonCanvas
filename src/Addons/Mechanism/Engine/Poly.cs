using Clipper2Lib;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Geometry;

namespace PhotonCanvas.Addon.Mechanism;

/// <summary>
/// Polygons for the mechanism engine, on Clipper's integer grid (0.1 µm, like the program's own
/// shape tools): fast unions for generating teeth, and overlap tests for the motion check. Turned
/// into the program's paths (lines and fitted curves) only at the end.
/// </summary>
public static class Poly
{
    public const double Scale = 1e4;

    public static Point64 P(double x, double y) => new((long)Math.Round(x * Scale), (long)Math.Round(y * Scale));
    public static Point64 P(Vec2 v) => P(v.X, v.Y);
    public static Vec2 V(Point64 p) => new(p.X / Scale, p.Y / Scale);

    public static Path64 Path(IEnumerable<Vec2> pts)
    {
        var p = new Path64();
        foreach (var v in pts)
        {
            var q = P(v);
            if (p.Count == 0 || p[^1] != q) p.Add(q);
        }
        while (p.Count > 1 && p[0] == p[^1]) p.RemoveAt(p.Count - 1);
        return p;
    }

    /// <summary>A circle as a polygon whose chords stay within <paramref name="tol"/> mm of it (anticlockwise in maths axes).</summary>
    public static Path64 Circle(Vec2 c, double r, double tol = 0.002)
    {
        int n = Segments(r, tol);
        var p = new Path64(n);
        for (int i = 0; i < n; i++)
        {
            double a = 2 * Math.PI * i / n;
            p.Add(P(c.X + r * Math.Cos(a), c.Y + r * Math.Sin(a)));
        }
        return p;
    }

    /// <summary>How many sides a circle of radius <paramref name="r"/> needs to stay within <paramref name="tol"/>.</summary>
    public static int Segments(double r, double tol)
    {
        if (!(r > 0)) return 8;
        double step = 2 * Math.Acos(Math.Clamp(1 - tol / r, -1, 1));
        return Math.Clamp((int)Math.Ceiling(2 * Math.PI / Math.Max(1e-4, step)), 16, 4096);
    }

    /// <summary>A rectangle (anticlockwise in maths axes).</summary>
    public static Path64 Rect(double x0, double y0, double x1, double y1) =>
        [P(Math.Min(x0, x1), Math.Min(y0, y1)), P(Math.Max(x0, x1), Math.Min(y0, y1)), P(Math.Max(x0, x1), Math.Max(y0, y1)), P(Math.Min(x0, x1), Math.Max(y0, y1))];

    /// <summary>Points turned by <paramref name="angle"/> (radians, anticlockwise in maths axes) about the origin, then moved.</summary>
    public static Paths64 Move(Paths64 paths, double angle, double dx = 0, double dy = 0)
    {
        double c = Math.Cos(angle), s = Math.Sin(angle);
        var o = new Paths64(paths.Count);
        foreach (var path in paths)
        {
            var q = new Path64(path.Count);
            foreach (var p in path)
            {
                double x = p.X / Scale, y = p.Y / Scale;
                q.Add(P(x * c - y * s + dx, x * s + y * c + dy));
            }
            o.Add(q);
        }
        return o;
    }

    public static Paths64 Move(Paths64 paths, Pose pose) => Move(paths, pose.Angle, pose.At.X, pose.At.Y);

    public static Paths64 Union(Paths64 a) => Clipper.Union(a, FillRule.NonZero);

    /// <summary>
    /// The union of many overlapping polygons in a row (a sweep), a few at a time and then those
    /// together: much quicker than all at once, since each step only meets its neighbours' outline.
    /// </summary>
    public static Paths64 UnionSweep(List<Paths64> steps)
    {
        if (steps.Count == 0) return [];
        var level = steps;
        while (level.Count > 1)
        {
            var next = new List<Paths64>((level.Count + 3) / 4);
            for (int i = 0; i < level.Count; i += 4)
            {
                var group = new Paths64();
                for (int k = i; k < Math.Min(level.Count, i + 4); k++) group.AddRange(level[k]);
                next.Add(Clipper.Union(group, FillRule.NonZero));
            }
            level = next;
        }
        return Clipper.Union(level[0], FillRule.NonZero);
    }
    public static Paths64 Union(Paths64 a, Paths64 b) => Clipper.Union(a, b, FillRule.NonZero);
    public static Paths64 Subtract(Paths64 a, Paths64 b) => Clipper.Difference(a, b, FillRule.NonZero);
    public static Paths64 Intersect(Paths64 a, Paths64 b) => Clipper.Intersect(a, b, FillRule.NonZero);

    /// <summary>Grows (+) or shrinks (−) the area by <paramref name="d"/> mm, rounding corners.</summary>
    public static Paths64 Grow(Paths64 a, double d, double tol = 0.002)
    {
        if (d == 0) return a;
        var co = new ClipperOffset(2, tol * Scale);
        co.AddPaths(a, JoinType.Round, EndType.Polygon);
        var o = new Paths64();
        co.Execute(d * Scale, o);
        return o;
    }

    /// <summary>Area in mm² (outer outlines minus holes).</summary>
    public static double Area(Paths64 p) => Math.Abs(Clipper.Area(p)) / (Scale * Scale);

    /// <summary>The part of <paramref name="a"/> inside a box, quickly (for overlap tests near one place).</summary>
    public static Paths64 Clip(Paths64 a, Rect2 box) =>
        Clipper.RectClip(new Rect64(P(box.MinX, box.MinY).X, P(box.MinX, box.MinY).Y, P(box.MaxX, box.MaxY).X, P(box.MaxX, box.MaxY).Y), a);

    /// <summary>How much two shapes overlap (mm²), looking only inside <paramref name="near"/> when given.</summary>
    public static double Overlap(Paths64 a, Paths64 b, Rect2? near = null)
    {
        if (near is { } box)
        {
            a = Clip(a, box);
            b = Clip(b, box);
        }
        if (a.Count == 0 || b.Count == 0) return 0;
        return Area(Intersect(a, b));
    }

    public static Rect2 Bounds(Paths64 p)
    {
        var r = Rect2.Empty;
        foreach (var path in p)
            foreach (var q in path) r = r.Include(V(q));
        return r;
    }

    /// <summary>Is the point inside (non-zero rule)?</summary>
    public static bool Inside(Paths64 p, Vec2 v)
    {
        var q = P(v);
        int winding = 0;
        foreach (var path in p)
        {
            var r = Clipper.PointInPolygon(q, path);
            if (r == PointInPolygonResult.IsOn) return true;
            if (r == PointInPolygonResult.IsInside) winding += Clipper.IsPositive(path) ? 1 : -1;
        }
        return winding != 0;
    }

    /// <summary>The outlines as the program's paths: straight runs as lines, smooth runs as curves within <paramref name="tol"/>.</summary>
    public static PathGeometry ToGeometry(Paths64 p, double tol = 0.004)
    {
        if (p.Count == 0) return PathGeometry.Empty;
        var raw = new PathGeometry(p.Where(x => x.Count >= 3).Select(x => new Subpath(V(x[0]), [.. x.Skip(1).Select(q => Seg.Line(V(q))), Seg.Line(V(x[0]))], true)));
        // Through the program's own shape tools, which fit curves to the dense outline.
        var fitted = ShapeGeometry.Union([raw], tol);
        return fitted.IsEmpty ? raw : fitted;
    }

    /// <summary>The program's paths as polygons (curves followed within <paramref name="tol"/>).</summary>
    public static Paths64 FromGeometry(PathGeometry g, double tol = 0.01)
    {
        var o = new Paths64();
        foreach (var f in g.Flatten(tol).Where(f => f.Closed))
        {
            var p = Path(f.Points);
            if (p.Count >= 3) o.Add(p);
        }
        // Even-odd in the program; non-zero here.
        return Clipper.Union(o, FillRule.EvenOdd);
    }

    /// <summary>The outlines as point lists (for drawing).</summary>
    public static List<List<Vec2>> Points(Paths64 p) => [.. p.Select(path => path.Select(V).ToList())];
}

/// <summary>Where a part is and how far it's turned (radians, anticlockwise in maths axes).</summary>
public readonly record struct Pose(Vec2 At, double Angle)
{
    public static readonly Pose Zero = new(default, 0);
    public Vec2 Apply(Vec2 p)
    {
        double c = Math.Cos(Angle), s = Math.Sin(Angle);
        return new(At.X + p.X * c - p.Y * s, At.Y + p.X * s + p.Y * c);
    }
}
