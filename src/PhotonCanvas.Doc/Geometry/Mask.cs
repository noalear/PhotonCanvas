using PhotonCanvas.Core;

namespace PhotonCanvas.Doc.Geometry;

/// <summary>
/// Masking shapes with a shape: what lies outside the mask (or inside, turned round) is cut away.
/// Lines are cut where they cross the mask's edge; filled outlines are cut to its shape.
/// </summary>
public static class Mask
{
    /// <summary>
    /// The part of <paramref name="g"/> inside the closed subpaths of <paramref name="mask"/>
    /// (filled even-odd), or outside them with <paramref name="keepOutside"/>.
    /// </summary>
    public static PathGeometry Apply(PathGeometry g, PathGeometry mask, bool keepOutside = false, double tolerance = 0.01)
    {
        var region = Region(mask);
        if (g.IsEmpty) return PathGeometry.Empty;
        if (region.IsEmpty) return keepOutside ? g : PathGeometry.Empty;
        var open = new PathGeometry(g.Subpaths.Where(s => !s.Closed));
        var closed = new PathGeometry(g.Subpaths.Where(s => s.Closed));
        var parts = new List<Subpath>();
        if (!open.IsEmpty)
        {
            // Outside: the lines inside a frame round everything, less the mask.
            var lineRegion = region;
            if (keepOutside)
            {
                var b = open.Bounds().Union(region.Bounds()).Inflate(1);
                lineRegion = ShapeGeometry.Boolean(PathGeometry.Rect(b.MinX, b.MinY, b.Width, b.Height), region, BoolOp.Subtract, tolerance);
            }
            parts.AddRange(ShapeGeometry.ClipLines(open, lineRegion, tolerance).Subpaths);
        }
        if (!closed.IsEmpty)
            parts.AddRange(ShapeGeometry.Boolean(closed, region, keepOutside ? BoolOp.Subtract : BoolOp.Intersect, tolerance).Subpaths);
        return parts.Count == 0 ? PathGeometry.Empty : new PathGeometry(parts);
    }

    /// <summary>The closed subpaths of a shape: the area a mask covers (its open lines don't count).</summary>
    public static PathGeometry Region(PathGeometry mask) => new(mask.Subpaths.Where(s => s.Closed));
}
