using Clipper2Lib;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;

namespace PhotonCanvas.Addon.Scatter;

/// <summary>
/// Polygons for the scatter terrain maker, in millimetres with y up (each piece in its own flat coordinates). Areas are
/// filled even-odd, like the program's own shapes; open lines are kept apart. Turned into the
/// program's paths (flipped so y runs down) only when the parts go into the design.
/// </summary>
public static class Geo
{
    /// <summary>Decimal places Clipper keeps: 0.1 µm.</summary>
    const int Precision = 4;

    public static PathD Rect(double x0, double y0, double x1, double y1) =>
        [new(Math.Min(x0, x1), Math.Min(y0, y1)), new(Math.Max(x0, x1), Math.Min(y0, y1)), new(Math.Max(x0, x1), Math.Max(y0, y1)), new(Math.Min(x0, x1), Math.Max(y0, y1))];

    public static PathD Circle(double cx, double cy, double r, double tol = 0.01)
    {
        int n = Segments(r, tol);
        var p = new PathD(n);
        for (int i = 0; i < n; i++)
        {
            double a = 2 * Math.PI * i / n;
            p.Add(new PointD(cx + r * Math.Cos(a), cy + r * Math.Sin(a)));
        }
        return p;
    }

    /// <summary>Points along an arc (radians, anticlockwise from <paramref name="a0"/> to <paramref name="a1"/>), both ends included.</summary>
    public static IEnumerable<PointD> Arc(double cx, double cy, double r, double a0, double a1, double tol = 0.01)
    {
        int n = Math.Max(2, (int)Math.Ceiling(Segments(r, tol) * Math.Abs(a1 - a0) / (2 * Math.PI)));
        for (int i = 0; i <= n; i++)
        {
            double a = a0 + (a1 - a0) * i / n;
            yield return new PointD(cx + r * Math.Cos(a), cy + r * Math.Sin(a));
        }
    }

    public static int Segments(double r, double tol)
    {
        if (!(r > 0)) return 8;
        double step = 2 * Math.Acos(Math.Clamp(1 - tol / r, -1, 1));
        return Math.Clamp((int)Math.Ceiling(2 * Math.PI / Math.Max(1e-4, step)), 12, 2048);
    }

    public static PathsD Union(PathsD a) => a.Count == 0 ? [] : Clipper.BooleanOp(ClipType.Union, a, null, FillRule.NonZero, Precision);
    public static PathsD Union(PathsD a, PathsD b) => Clipper.Union(a, b, FillRule.NonZero, Precision);
    public static PathsD Subtract(PathsD a, PathsD b) => a.Count == 0 ? [] : b.Count == 0 ? a : Clipper.Difference(a, b, FillRule.EvenOdd, Precision);
    public static PathsD Intersect(PathsD a, PathsD b) => a.Count == 0 || b.Count == 0 ? [] : Clipper.Intersect(a, b, FillRule.EvenOdd, Precision);
    /// <summary>Even-odd areas made tidy (outer outlines anticlockwise, holes clockwise).</summary>
    public static PathsD Tidy(PathsD a) => a.Count == 0 ? [] : Clipper.BooleanOp(ClipType.Union, a, null, FillRule.EvenOdd, Precision);

    /// <summary>The outline turned anticlockwise if it runs clockwise (a piece seen from its back comes out mirrored), so it adds to others rather than cutting them away.</summary>
    public static PathD Positive(PathD p)
    {
        if (Clipper.Area(p) >= 0) return p;
        var r = new PathD(p);
        r.Reverse();
        return r;
    }

    /// <summary>Grows (positive) or shrinks (negative) areas.</summary>
    public static PathsD Offset(PathsD a, double by, bool round = false) =>
        a.Count == 0 || by == 0 ? a : Clipper.InflatePaths(Tidy(a), by, round ? JoinType.Round : JoinType.Miter, EndType.Polygon, 4, Precision);

    public static double Area(PathsD a) => a.Count == 0 ? 0 : Math.Abs(Clipper.Area(Tidy(a)));

    /// <summary>The parts of open lines inside the areas.</summary>
    public static List<PathD> ClipLines(IEnumerable<PathD> lines, PathsD region)
    {
        var subject = new PathsD(lines.Where(l => l.Count >= 2));
        if (subject.Count == 0 || region.Count == 0) return [];
        var c = new ClipperD(Precision);
        c.AddOpenSubject(subject);
        c.AddClip(region);
        var closed = new PathsD();
        var open = new PathsD();
        c.Execute(ClipType.Intersection, FillRule.EvenOdd, closed, open);
        return [.. open.Where(p => p.Count >= 2)];
    }

    /// <summary>A straight strip of the given width from p to q.</summary>
    public static PathD Strip(PointD p, PointD q, double width)
    {
        double dx = q.x - p.x, dy = q.y - p.y, len = Math.Max(1e-9, Math.Sqrt(dx * dx + dy * dy));
        double nx = -dy / len * width / 2, ny = dx / len * width / 2;
        return [new(p.x + nx, p.y + ny), new(p.x - nx, p.y - ny), new(q.x - nx, q.y - ny), new(q.x + nx, q.y + ny)];
    }

    /// <summary>A closed outline as an open line that comes back to its start (to be scored round).</summary>
    public static PathD Loop(PathD p) => p.Count == 0 ? p : [.. p, p[0]];

    public static RectD Bounds(PathsD a) => a.Count == 0 ? new RectD(0, 0, 0, 0) : Clipper.GetBounds(a);

    /// <summary>Points moved by an affine map.</summary>
    public static PathD Map(PathD p, Func<PointD, PointD> f) => [.. p.Select(f)];
    public static PathsD Map(PathsD a, Func<PointD, PointD> f) => [.. a.Select(p => Map(p, f))];
    public static List<PathD> Map(IEnumerable<PathD> a, Func<PointD, PointD> f) => [.. a.Select(p => Map(p, f))];

    public static PathsD Move(PathsD a, double dx, double dy) => Map(a, q => new PointD(q.x + dx, q.y + dy));

    /// <summary>Turned about the origin (degrees, anticlockwise with y up), then moved.</summary>
    public static PathsD Turn(PathsD a, double deg, double dx = 0, double dy = 0)
    {
        double r = deg * Math.PI / 180, c = Math.Cos(r), s = Math.Sin(r);
        return Map(a, q => new PointD(q.x * c - q.y * s + dx, q.x * s + q.y * c + dy));
    }

    /// <summary>How many separate pieces (outer outlines) the areas make.</summary>
    public static int Pieces(PathsD a) => Tidy(a).Count(p => Clipper.Area(p) > 1e-6);

    /// <summary>The program's path for the areas (closed) and lines (open), flipped so y runs down.</summary>
    public static PathGeometry ToGeometry(PathsD areas, IEnumerable<PathD>? lines = null)
    {
        var subs = new List<Subpath>();
        foreach (var p in areas)
        {
            if (p.Count < 3) continue;
            subs.Add(new Subpath(new Vec2(p[0].x, -p[0].y), [.. p.Skip(1).Select(q => Seg.Line(new Vec2(q.x, -q.y))), Seg.Line(new Vec2(p[0].x, -p[0].y))], true));
        }
        if (lines is not null)
            foreach (var p in lines)
            {
                if (p.Count < 2) continue;
                subs.Add(new Subpath(new Vec2(p[0].x, -p[0].y), [.. p.Skip(1).Select(q => Seg.Line(new Vec2(q.x, -q.y)))], false));
            }
        return new PathGeometry(subs);
    }

    /// <summary>Areas and lines from the program's path (y down → y up): closed subpaths are areas, open ones lines.</summary>
    public static (PathsD Areas, List<PathD> Lines) FromGeometry(PathGeometry g, double tol = 0.02)
    {
        var areas = new PathsD();
        var lines = new List<PathD>();
        foreach (var f in g.Flatten(tol))
        {
            var p = new PathD(f.Points.Select(v => new PointD(v.X, -v.Y)));
            if (f.Closed && p.Count >= 3) areas.Add(p);
            else if (p.Count >= 2) lines.Add(p);
        }
        return (areas, lines);
    }
}
