using Clipper2Lib;
using PhotonCanvas.Core;

namespace PhotonCanvas.Doc.Geometry;

/// <summary>How corners are formed when an outline is offset.</summary>
public enum JoinStyle
{
    /// <summary>Corners stay pointed (mitred). Very sharp points are cut off once they reach the miter limit.</summary>
    Sharp,
    /// <summary>Corners become arcs around the original corner.</summary>
    Round,
    /// <summary>Corners are cut straight across (bevelled).</summary>
    Flat,
}

/// <summary>How the ends of an open line are capped when the line is turned into an outline.</summary>
public enum EndStyle
{
    /// <summary>Square end exactly at the end of the line.</summary>
    Flat,
    /// <summary>Half-circle end.</summary>
    Round,
    /// <summary>Square end that reaches past the end of the line by the offset distance.</summary>
    Square,
}

/// <summary>How two shapes are combined.</summary>
public enum BoolOp
{
    /// <summary>Everything covered by either shape.</summary>
    Union,
    /// <summary>The first shape with the second one cut away.</summary>
    Subtract,
    /// <summary>Only where the shapes overlap.</summary>
    Intersect,
    /// <summary>Where exactly one of the shapes is: the overlap is removed.</summary>
    Exclude,
}

/// <summary>
/// Offsetting and combining filled shapes, in millimetres. Curves are followed
/// to within the given tolerance. Results are closed outlines (even-odd fill):
/// straight edges stay straight, and smooth runs such as rounded corners and
/// circles come back as curves rather than thousands of tiny lines.
/// </summary>
public static class ShapeGeometry
{
    /// <summary>Internal grid: 0.1 µm.</summary>
    const double Scale = 1e4;

    /// <summary>Points farther out than this (mm) are ignored as nonsense.</summary>
    const double MaxCoordMm = 1e8;

    /// <summary>
    /// Grows (positive <paramref name="distanceMm"/>) or shrinks (negative) the
    /// filled area of the closed subpaths; holes do the opposite. Closed
    /// subpaths are filled with the even-odd rule. Open subpaths become a
    /// closed outline |distance| either side of the line, capped with
    /// <paramref name="end"/> (the sign is ignored for them).
    /// </summary>
    /// <param name="miterLimit">For <see cref="JoinStyle.Sharp"/>: how far a point may reach, in multiples of the distance, before it is cut off.</param>
    /// <param name="tolerance">Largest allowed deviation from the exact result (mm).</param>
    public static PathGeometry Offset(PathGeometry world, double distanceMm, JoinStyle join, EndStyle end, double miterLimit = 4, double tolerance = 0.01)
    {
        try
        {
            if (world is null || !double.IsFinite(distanceMm)) return PathGeometry.Empty;
            double tol = Tol(tolerance);
            double miter = double.IsFinite(miterLimit) ? Math.Clamp(miterLimit, 1, 100) : 4;
            var (closed, open) = ToPaths(world, tol);
            var parts = new Paths64();
            if (closed.Count > 0)
            {
                // Normalise first: the offsetter relies on outer/hole orientation, the document on even-odd.
                var areas = Clipper.Union(closed, FillRule.EvenOdd);
                parts.AddRange(distanceMm == 0 ? areas : Inflate(areas, distanceMm, join, EndType.Polygon, miter, tol));
            }
            if (open.Count > 0 && distanceMm != 0)
                parts.AddRange(Inflate(open, Math.Abs(distanceMm), join, Ends(end), miter, tol));
            if (closed.Count > 0 && open.Count > 0) parts = Clipper.Union(parts, FillRule.NonZero);
            return ToGeometry(parts, tol);
        }
        catch (Exception)
        {
            return PathGeometry.Empty;
        }
    }

    /// <summary>
    /// Combines the closed subpaths of <paramref name="a"/> and <paramref name="b"/>
    /// (each filled even-odd). <see cref="BoolOp.Subtract"/> is a minus b.
    /// Open subpaths are ignored.
    /// </summary>
    public static PathGeometry Boolean(PathGeometry a, PathGeometry b, BoolOp op, double tolerance = 0.01)
    {
        try
        {
            double tol = Tol(tolerance);
            var pa = a is null ? new Paths64() : ToPaths(a, tol).Closed;
            var pb = b is null ? new Paths64() : ToPaths(b, tol).Closed;
            var clip = op switch
            {
                BoolOp.Union => ClipType.Union,
                BoolOp.Subtract => ClipType.Difference,
                BoolOp.Intersect => ClipType.Intersection,
                _ => ClipType.Xor,
            };
            return ToGeometry(Clipper.BooleanOp(clip, pa, pb, FillRule.EvenOdd), tol);
        }
        catch (Exception)
        {
            return PathGeometry.Empty;
        }
    }

    /// <summary>
    /// Weld: one outline around everything the parts cover. Each part's closed
    /// subpaths are filled even-odd, so a part's own holes stay open unless
    /// another part covers them. Open subpaths are ignored.
    /// </summary>
    public static PathGeometry Union(IEnumerable<PathGeometry> parts, double tolerance = 0.01)
    {
        try
        {
            if (parts is null) return PathGeometry.Empty;
            double tol = Tol(tolerance);
            var all = new Paths64();
            foreach (var g in parts)
            {
                if (g is null) continue;
                var closed = ToPaths(g, tol).Closed;
                if (closed.Count > 0) all.AddRange(Clipper.Union(closed, FillRule.EvenOdd));
            }
            return all.Count == 0 ? PathGeometry.Empty : ToGeometry(Clipper.Union(all, FillRule.NonZero), tol);
        }
        catch (Exception)
        {
            return PathGeometry.Empty;
        }
    }

    /// <summary>
    /// The area the closed subpaths cover by the non-zero rule: outlines that run the same way
    /// add up (overlapping solids merge), ones that run the other way cut holes.
    /// </summary>
    public static PathGeometry FillNonZero(PathGeometry g, double tolerance = 0.01)
    {
        try
        {
            double tol = Tol(tolerance);
            var closed = ToPaths(g, tol).Closed;
            return closed.Count == 0 ? PathGeometry.Empty : ToGeometry(Clipper.Union(closed, FillRule.NonZero), tol);
        }
        catch (Exception)
        {
            return PathGeometry.Empty;
        }
    }

    static double Tol(double t) => double.IsFinite(t) && t > 0 ? Math.Clamp(t, 1e-3, 10) : 0.01;

    static JoinType Joins(JoinStyle j) => j switch
    {
        JoinStyle.Round => JoinType.Round,
        JoinStyle.Flat => JoinType.Bevel,
        _ => JoinType.Miter,
    };

    static EndType Ends(EndStyle e) => e switch
    {
        EndStyle.Round => EndType.Round,
        EndStyle.Square => EndType.Square,
        _ => EndType.Butt,
    };

    static Paths64 Inflate(Paths64 paths, double deltaMm, JoinStyle join, EndType end, double miter, double tol)
    {
        // Arcs within a quarter of the tolerance leave room for flattening and refitting.
        var co = new ClipperOffset(miter, tol / 4 * Scale);
        co.AddPaths(paths, Joins(join), end);
        var o = new Paths64();
        co.Execute(deltaMm * Scale, o);
        return o;
    }

    static (Paths64 Closed, Paths64 Open) ToPaths(PathGeometry g, double tol)
    {
        var closed = new Paths64();
        var open = new Paths64();
        foreach (var f in g.Flatten(tol / 4))
        {
            var path = new Path64(f.Points.Count);
            foreach (var p in f.Points)
            {
                if (!p.IsFinite || Math.Abs(p.X) > MaxCoordMm || Math.Abs(p.Y) > MaxCoordMm) continue;
                var q = new Point64((long)Math.Round(p.X * Scale), (long)Math.Round(p.Y * Scale));
                if (path.Count == 0 || path[^1] != q) path.Add(q);
            }
            if (f.Closed)
            {
                while (path.Count > 1 && path[0] == path[^1]) path.RemoveAt(path.Count - 1);
                if (path.Count >= 3) closed.Add(path);
            }
            else if (path.Count >= 1) open.Add(path);
        }
        return (closed, open);
    }

    static PathGeometry ToGeometry(Paths64 paths, double tol)
    {
        var subs = new List<Subpath>();
        double minArea = 1e-3 * tol * tol;
        foreach (var path in paths)
        {
            var pts = new List<Vec2>(path.Count);
            foreach (var q in path)
            {
                var v = new Vec2(q.X / Scale, q.Y / Scale);
                if (pts.Count == 0 || pts[^1] != v) pts.Add(v);
            }
            while (pts.Count > 1 && pts[0] == pts[^1]) pts.RemoveAt(pts.Count - 1);
            if (pts.Count < 3 || Math.Abs(Geom.SignedArea(pts)) < minArea) continue;
            subs.Add(CurveFit.Closed(pts, tol));
        }
        return subs.Count == 0 ? PathGeometry.Empty : new PathGeometry(subs);
    }
}
