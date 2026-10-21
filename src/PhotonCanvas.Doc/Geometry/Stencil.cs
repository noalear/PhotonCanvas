using PhotonCanvas.Core;

namespace PhotonCanvas.Doc.Geometry;

/// <summary>
/// Stencil bridges: cut out, a letter's inside (the middle of an O, A, R, B…) would fall out with it.
/// A strip of the sheet is kept across the letter through each such island, up and down (or across),
/// so the island stays joined to the rest of the stencil.
/// </summary>
public static class Stencil
{
    /// <summary>
    /// <paramref name="shape"/> (closed outlines, holes as inner loops) with a bridge
    /// <paramref name="widthMm"/> wide through every island. Returns the shape and how many islands were bridged.
    /// </summary>
    public static (PathGeometry Shape, int Islands) Bridge(PathGeometry shape, double widthMm, bool across = false)
    {
        var loops = shape.Flatten(0.02).Where(f => f.Closed && f.Points.Count >= 3).Select(f => f.Points).ToList();
        if (loops.Count < 2 || !(widthMm > 0)) return (shape, 0);
        // A loop inside an odd number of others is a hole in the letter: the island to keep.
        // (Counted from a point on the loop itself: its middle may be another loop's inside.)
        int Depth(IReadOnlyList<Vec2> loop) => loops.Count(o => !ReferenceEquals(o, loop) && Geom.Inside(o, loop[0]));
        var strips = new List<PathGeometry>();
        int islands = 0;
        foreach (var hole in loops.Where(l => Depth(l) % 2 == 1))
        {
            // The outline round it: the smallest loop holding it.
            var outer = loops.Where(o => !ReferenceEquals(o, hole) && Geom.Inside(o, hole[0])).MinBy(o => Math.Abs(Geom.SignedArea(o)));
            if (outer is null) continue;
            var ob = Rect2.Bounds(outer);
            var c = Inner(hole);
            strips.Add(across
                ? PathGeometry.Rect(ob.MinX - 1, c.Y - widthMm / 2, ob.Width + 2, widthMm)
                : PathGeometry.Rect(c.X - widthMm / 2, ob.MinY - 1, widthMm, ob.Height + 2));
            islands++;
        }
        if (islands == 0) return (shape, 0);
        return (ShapeGeometry.Boolean(shape, ShapeGeometry.Union(strips), BoolOp.Subtract), islands);
    }

    /// <summary>A point well inside a loop (its pole would be ideal; the middle of the widest span through its centre does).</summary>
    static Vec2 Inner(IReadOnlyList<Vec2> loop)
    {
        var b = Rect2.Bounds(loop);
        // Just off the middle, so the line doesn't run through a corner (counted twice).
        double y = (b.MinY + b.MaxY) / 2 + b.Height * 0.0173;
        var xs = new List<double>();
        for (int i = 0; i < loop.Count; i++)
        {
            Vec2 a = loop[i], d = loop[(i + 1) % loop.Count];
            if ((a.Y <= y) == (d.Y <= y)) continue;
            xs.Add(a.X + (y - a.Y) / (d.Y - a.Y) * (d.X - a.X));
        }
        xs.Sort();
        double best = -1, mid = b.Center.X;
        for (int i = 0; i + 1 < xs.Count; i += 2)
            if (xs[i + 1] - xs[i] > best) { best = xs[i + 1] - xs[i]; mid = (xs[i] + xs[i + 1]) / 2; }
        return new Vec2(mid, y);
    }
}
