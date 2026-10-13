namespace PhotonCanvas.Core;

/// <summary>
/// A point or vector in machine millimetres (+X right, +Y down, origin at the
/// top-left of the galvo field).
/// </summary>
public readonly record struct Vec2(double X, double Y)
{
    public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Y + b.Y);
    public static Vec2 operator -(Vec2 a, Vec2 b) => new(a.X - b.X, a.Y - b.Y);
    public static Vec2 operator *(Vec2 a, double s) => new(a.X * s, a.Y * s);
    public static Vec2 operator *(double s, Vec2 a) => new(a.X * s, a.Y * s);
    public static Vec2 operator /(Vec2 a, double s) => new(a.X / s, a.Y / s);
    public static Vec2 operator -(Vec2 a) => new(-a.X, -a.Y);

    public double Length => Math.Sqrt(X * X + Y * Y);
    public double Dist(Vec2 o) => (this - o).Length;
    public double Dot(Vec2 o) => X * o.X + Y * o.Y;
    public double Cross(Vec2 o) => X * o.Y - Y * o.X;
    public bool IsFinite => double.IsFinite(X) && double.IsFinite(Y);
    public static Vec2 Lerp(Vec2 a, Vec2 b, double t) => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

    public override string ToString() => $"({X:0.###}, {Y:0.###})";
}

/// <summary>Axis-aligned rectangle.</summary>
public readonly record struct Rect2(double MinX, double MinY, double MaxX, double MaxY)
{
    public double Width => MaxX - MinX;
    public double Height => MaxY - MinY;
    public Vec2 Center => new((MinX + MaxX) / 2, (MinY + MaxY) / 2);
    public Vec2 TopLeft => new(MinX, MinY);
    public Vec2 BottomRight => new(MaxX, MaxY);
    public bool IsEmpty => !(MaxX >= MinX && MaxY >= MinY);

    public static readonly Rect2 Empty = new(double.PositiveInfinity, double.PositiveInfinity,
        double.NegativeInfinity, double.NegativeInfinity);

    public static Rect2 FromPoints(Vec2 a, Vec2 b) =>
        new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));

    public Rect2 Union(Rect2 o) => IsEmpty ? o : o.IsEmpty ? this
        : new(Math.Min(MinX, o.MinX), Math.Min(MinY, o.MinY), Math.Max(MaxX, o.MaxX), Math.Max(MaxY, o.MaxY));

    public Rect2 Include(Vec2 p) => IsEmpty ? new(p.X, p.Y, p.X, p.Y)
        : new(Math.Min(MinX, p.X), Math.Min(MinY, p.Y), Math.Max(MaxX, p.X), Math.Max(MaxY, p.Y));

    public Rect2 Inflate(double d) => new(MinX - d, MinY - d, MaxX + d, MaxY + d);

    public bool Contains(Vec2 p) => p.X >= MinX && p.X <= MaxX && p.Y >= MinY && p.Y <= MaxY;
    public bool Contains(Rect2 r) => !r.IsEmpty && r.MinX >= MinX && r.MaxX <= MaxX && r.MinY >= MinY && r.MaxY <= MaxY;
    public bool Intersects(Rect2 r) => !IsEmpty && !r.IsEmpty &&
        r.MinX <= MaxX && r.MaxX >= MinX && r.MinY <= MaxY && r.MaxY >= MinY;

    public static Rect2 Bounds(IEnumerable<Vec2> points)
    {
        var r = Empty;
        foreach (var p in points) r = r.Include(p);
        return r;
    }
}

/// <summary>
/// A sequence of connected points. Closed polylines repeat the first point at
/// the end when emitted, so the cut returns to where it started.
/// </summary>
public sealed class Polyline
{
    /// <summary>Points closer than this are treated as the same point.</summary>
    public const double SamePointMm = 1e-6;

    public IReadOnlyList<Vec2> Points { get; }
    public bool Closed { get; }

    Polyline(IReadOnlyList<Vec2> points, bool closed)
    {
        Points = points;
        Closed = closed;
    }

    public static Polyline Open(IEnumerable<Vec2> points) => new(points.ToArray(), false);

    /// <summary>
    /// A closed loop. If the caller already repeated the first point at the
    /// end, it is dropped so it is not emitted twice.
    /// </summary>
    public static Polyline Loop(IEnumerable<Vec2> points)
    {
        var pts = points.ToList();
        if (pts.Count > 1 && pts[0].Dist(pts[^1]) <= SamePointMm) pts.RemoveAt(pts.Count - 1);
        return new(pts, true);
    }

    public static Polyline Open(params (double X, double Y)[] pts) => Open(pts.Select(p => new Vec2(p.X, p.Y)));
    public static Polyline Loop(params (double X, double Y)[] pts) => Loop(pts.Select(p => new Vec2(p.X, p.Y)));

    /// <summary>
    /// The points the laser visits, with the closing point appended for closed
    /// loops and consecutive duplicates removed.
    /// </summary>
    public List<Vec2> EmittedPoints()
    {
        var o = new List<Vec2>(Points.Count + 1);
        foreach (var p in Points)
            if (o.Count == 0 || o[^1].Dist(p) > SamePointMm) o.Add(p);
        if (Closed && o.Count > 1 && o[0].Dist(o[^1]) > SamePointMm) o.Add(o[0]);
        return o;
    }

    /// <summary>True if the laser would actually move while firing.</summary>
    public bool HasExtent => EmittedPoints().Count >= 2;

    public double Length
    {
        get
        {
            var e = EmittedPoints();
            double sum = 0;
            for (int i = 1; i < e.Count; i++) sum += e[i - 1].Dist(e[i]);
            return sum;
        }
    }

    public Rect2 Bounds => Rect2.Bounds(Points);

    public Polyline Reversed() => new(Points.Reverse().ToArray(), Closed);

    /// <summary>A closed loop that starts at point index <paramref name="i"/>.</summary>
    public Polyline RotatedTo(int i)
    {
        if (!Closed || i == 0 || Points.Count == 0) return this;
        var pts = new Vec2[Points.Count];
        for (int k = 0; k < pts.Length; k++) pts[k] = Points[(i + k) % pts.Length];
        return new(pts, true);
    }

    public void Validate()
    {
        foreach (var p in Points)
            if (!p.IsFinite) throw new JobException("A path coordinate is not a valid number.");
    }
}

public static class Geom
{
    /// <summary>Points along an elliptical arc (angles in degrees, +Y down), about <paramref name="step"/> mm apart.</summary>
    public static List<Vec2> Arc(double cx, double cy, double rx, double ry, double a0, double a1, double step)
    {
        double span = (a1 - a0) * Math.PI / 180;
        int n = (int)Math.Max(8, Math.Ceiling(Math.Abs(span) * Math.Max(rx, ry) / step));
        var o = new List<Vec2>(n + 1);
        for (int i = 0; i <= n; i++)
        {
            double a = a0 * Math.PI / 180 + span * i / n;
            o.Add(new(cx + rx * Math.Cos(a), cy + ry * Math.Sin(a)));
        }
        return o;
    }

    /// <summary>Signed area (positive = clockwise on screen, since +Y is down).</summary>
    public static double SignedArea(IReadOnlyList<Vec2> poly)
    {
        double a = 0;
        for (int i = 0, n = poly.Count; i < n; i++) a += poly[i].Cross(poly[(i + 1) % n]);
        return a / 2;
    }

    /// <summary>Even-odd point-in-polygon test.</summary>
    public static bool Inside(IReadOnlyList<Vec2> poly, Vec2 p)
    {
        bool inside = false;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
        {
            var (a, b) = (poly[i], poly[j]);
            if ((a.Y > p.Y) != (b.Y > p.Y) && p.X < (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X)
                inside = !inside;
        }
        return inside;
    }

    /// <summary>Distance from <paramref name="p"/> to segment ab.</summary>
    public static double SegmentDistance(Vec2 p, Vec2 a, Vec2 b)
    {
        var ab = b - a;
        double l2 = ab.Dot(ab);
        if (l2 <= 0) return p.Dist(a);
        double t = Math.Clamp((p - a).Dot(ab) / l2, 0, 1);
        return p.Dist(a + ab * t);
    }
}
