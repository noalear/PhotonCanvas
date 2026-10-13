using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using YTool.Core;

namespace YTool.Doc;

public enum SegKind : byte { Line, Cubic }

/// <summary>One segment of a subpath, from the previous end point to <see cref="To"/>.</summary>
public readonly record struct Seg(SegKind Kind, Vec2 C1, Vec2 C2, Vec2 To)
{
    public static Seg Line(Vec2 to) => new(SegKind.Line, to, to, to);
    public static Seg Cubic(Vec2 c1, Vec2 c2, Vec2 to) => new(SegKind.Cubic, c1, c2, to);

    public Seg Transform(Affine t) => new(Kind, t.Apply(C1), t.Apply(C2), t.Apply(To));
}

/// <summary>A connected run of segments starting at <see cref="Start"/>.</summary>
public sealed record Subpath(Vec2 Start, ImmutableArray<Seg> Segs, bool Closed)
{
    public Subpath Transform(Affine t) => new(t.Apply(Start), [.. Segs.Select(s => s.Transform(t))], Closed);

    public Vec2 End => Segs.IsEmpty ? Start : Segs[^1].To;

    /// <summary>Points along the subpath, curves subdivided until within <paramref name="tol"/> mm.</summary>
    public List<Vec2> Flatten(double tol)
    {
        var o = new List<Vec2>(Segs.Length + 1) { Start };
        var p = Start;
        foreach (var s in Segs)
        {
            if (s.Kind == SegKind.Line) o.Add(s.To);
            else FlattenCubic(p, s.C1, s.C2, s.To, tol, o, 0);
            p = s.To;
        }
        return o;
    }

    static void FlattenCubic(Vec2 p0, Vec2 p1, Vec2 p2, Vec2 p3, double tol, List<Vec2> o, int depth)
    {
        // Flat enough when both control points lie within tol of the chord.
        double d = Math.Max(Geom.SegmentDistance(p1, p0, p3), Geom.SegmentDistance(p2, p0, p3));
        if (d <= tol || depth >= 18)
        {
            o.Add(p3);
            return;
        }
        var p01 = Vec2.Lerp(p0, p1, .5); var p12 = Vec2.Lerp(p1, p2, .5); var p23 = Vec2.Lerp(p2, p3, .5);
        var a = Vec2.Lerp(p01, p12, .5); var b = Vec2.Lerp(p12, p23, .5);
        var m = Vec2.Lerp(a, b, .5);
        FlattenCubic(p0, p01, a, m, tol, o, depth + 1);
        FlattenCubic(m, b, p23, p3, tol, o, depth + 1);
    }
}

/// <summary>A flattened subpath: what the laser actually follows.</summary>
public sealed record FlatPath(List<Vec2> Points, bool Closed);

/// <summary>
/// Immutable vector geometry: one or more subpaths (compound paths and holes
/// are several subpaths in one geometry, filled with the even-odd rule).
/// </summary>
public sealed class PathGeometry
{
    public ImmutableArray<Subpath> Subpaths { get; }

    public PathGeometry(IEnumerable<Subpath> subpaths) => Subpaths = [.. subpaths];

    public static readonly PathGeometry Empty = new([]);

    public bool IsEmpty => Subpaths.All(s => s.Segs.IsEmpty);

    public PathGeometry Transform(Affine t) => t.IsIdentity ? this : new(Subpaths.Select(s => s.Transform(t)));

    public List<FlatPath> Flatten(double tol) =>
        [.. Subpaths.Where(s => !s.Segs.IsEmpty).Select(s => new FlatPath(s.Flatten(tol), s.Closed))];

    /// <summary>Tight bounds (curves flattened finely).</summary>
    public Rect2 Bounds(double tol = 0.01) =>
        Rect2.Bounds(Flatten(tol).SelectMany(f => f.Points));

    // ---- construction -------------------------------------------------------

    public static PathGeometry Rect(double x, double y, double w, double h)
    {
        Vec2 a = new(x, y), b = new(x + w, y), c = new(x + w, y + h), d = new(x, y + h);
        return new([new Subpath(a, [Seg.Line(b), Seg.Line(c), Seg.Line(d), Seg.Line(a)], true)]);
    }

    /// <summary>Rounded rectangle; radii are clamped to half the sides.</summary>
    public static PathGeometry RoundRect(double x, double y, double w, double h, double rx, double ry)
    {
        rx = Math.Min(Math.Abs(rx), Math.Abs(w) / 2);
        ry = Math.Min(Math.Abs(ry), Math.Abs(h) / 2);
        if (rx <= 0 || ry <= 0) return Rect(x, y, w, h);
        const double K = 0.5522847498307936; // 4/3·(√2−1): quarter circle as a cubic
        double kx = rx * K, ky = ry * K;
        var segs = ImmutableArray.CreateBuilder<Seg>();
        segs.Add(Seg.Line(new(x + w - rx, y)));
        segs.Add(Seg.Cubic(new(x + w - rx + kx, y), new(x + w, y + ry - ky), new(x + w, y + ry)));
        segs.Add(Seg.Line(new(x + w, y + h - ry)));
        segs.Add(Seg.Cubic(new(x + w, y + h - ry + ky), new(x + w - rx + kx, y + h), new(x + w - rx, y + h)));
        segs.Add(Seg.Line(new(x + rx, y + h)));
        segs.Add(Seg.Cubic(new(x + rx - kx, y + h), new(x, y + h - ry + ky), new(x, y + h - ry)));
        segs.Add(Seg.Line(new(x, y + ry)));
        segs.Add(Seg.Cubic(new(x, y + ry - ky), new(x + rx - kx, y), new(x + rx, y)));
        return new([new Subpath(new(x + rx, y), segs.ToImmutable(), true)]);
    }

    public static PathGeometry Ellipse(double cx, double cy, double rx, double ry)
    {
        const double K = 0.5522847498307936;
        double kx = rx * K, ky = ry * K;
        Vec2 r = new(cx + rx, cy), b = new(cx, cy + ry), l = new(cx - rx, cy), t = new(cx, cy - ry);
        return new([new Subpath(r,
        [
            Seg.Cubic(new(cx + rx, cy + ky), new(cx + kx, cy + ry), b),
            Seg.Cubic(new(cx - kx, cy + ry), new(cx - rx, cy + ky), l),
            Seg.Cubic(new(cx - rx, cy - ky), new(cx - kx, cy - ry), t),
            Seg.Cubic(new(cx + kx, cy - ry), new(cx + rx, cy - ky), r),
        ], true)]);
    }

    public static PathGeometry Polyline(IReadOnlyList<Vec2> pts, bool closed)
    {
        if (pts.Count == 0) return Empty;
        var segs = pts.Skip(1).Select(Seg.Line).ToList();
        if (closed && pts.Count > 1 && pts[^1].Dist(pts[0]) > Core.Polyline.SamePointMm) segs.Add(Seg.Line(pts[0]));
        return new([new Subpath(pts[0], [.. segs], closed)]);
    }

    public static PathGeometry Line(Vec2 a, Vec2 b) => new([new Subpath(a, [Seg.Line(b)], false)]);

    // ---- SVG path data ------------------------------------------------------

    /// <summary>Serialise as SVG path data (absolute M/L/C/Z).</summary>
    public string ToSvgPathData()
    {
        var sb = new StringBuilder();
        static string N(double v) => v.ToString("0.######", CultureInfo.InvariantCulture);
        foreach (var sp in Subpaths)
        {
            sb.Append('M').Append(N(sp.Start.X)).Append(',').Append(N(sp.Start.Y));
            foreach (var s in sp.Segs)
            {
                if (s.Kind == SegKind.Line) sb.Append('L').Append(N(s.To.X)).Append(',').Append(N(s.To.Y));
                else sb.Append('C').Append(N(s.C1.X)).Append(',').Append(N(s.C1.Y)).Append(' ')
                        .Append(N(s.C2.X)).Append(',').Append(N(s.C2.Y)).Append(' ')
                        .Append(N(s.To.X)).Append(',').Append(N(s.To.Y));
            }
            if (sp.Closed) sb.Append('Z');
        }
        return sb.ToString();
    }

    /// <summary>Parse SVG path data (all commands, including arcs and relative forms).</summary>
    public static PathGeometry FromSvgPathData(string d) => SvgPathParser.Parse(d);
}
