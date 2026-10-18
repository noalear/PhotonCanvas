using PhotonCanvas.Core;

namespace PhotonCanvas.Doc.Geometry;

/// <summary>Ready-made laser projects: finger-jointed boxes, keychains and tags.</summary>
public static class Generators
{
    /// <summary>
    /// A box with finger joints, as flat panels laid out side by side (mm). Sizes are inside the
    /// box; <paramref name="thickness"/> is the material's. Fingers are about
    /// <paramref name="finger"/> wide (an odd number per edge, so the corners work out). Without
    /// a lid the walls' top edges are plain. Put the panels on a Cut layer with the kerf set.
    /// </summary>
    public static List<(string Name, PathGeometry Outline)> Box(double width, double depth, double height, double thickness, double finger, bool lid)
    {
        double t = Math.Max(0.1, thickness);
        finger = Math.Max(t, finger);
        double W = width + 2 * t, D = depth + 2 * t, H = height + t + (lid ? t : 0);
        // Edge kinds: A keeps material at its ends and on even fingers, B is the opposite, F is plain.
        var panels = new List<(string Name, double W, double H, char Bottom, char Right, char Top, char Left)>
        {
            ("Bottom", W, D, 'A', 'A', 'A', 'A'),
            ("Front", W, H, 'B', 'A', lid ? 'B' : 'F', 'A'),
            ("Back", W, H, 'B', 'A', lid ? 'B' : 'F', 'A'),
            ("Left", D, H, 'B', 'B', lid ? 'B' : 'F', 'B'),
            ("Right", D, H, 'B', 'B', lid ? 'B' : 'F', 'B'),
        };
        if (lid) panels.Add(("Lid", W, D, 'A', 'A', 'A', 'A'));
        var o = new List<(string, PathGeometry)>();
        double x = 0;
        foreach (var p in panels)
        {
            var outline = Panel(p.W, p.H, t, finger, p.Bottom, p.Right, p.Top, p.Left, !lid && p.Name is not ("Bottom" or "Lid"));
            o.Add((p.Name, outline.Transform(Affine.Translate(x, 0))));
            x += p.W + 3;
        }
        return o;
    }

    /// <summary>One panel: its outer rectangle with the notches cut out along each jointed edge.</summary>
    static PathGeometry Panel(double w, double h, double t, double finger, char bottom, char right, char top, char left, bool openTop)
    {
        var cuts = new List<PathGeometry>();
        // Edges walked as (start, direction, inward normal, length), in panel coordinates (y down: top edge at y = 0).
        void Edge(char kind, Vec2 a, Vec2 dir, Vec2 inward, double length)
        {
            if (kind == 'F') return;
            double inner = length - 2 * t;
            int n = Math.Max(3, (int)Math.Round(inner / finger));
            if (n % 2 == 0) n++;
            double seg = inner / n;
            void Notch(double s0, double s1)
            {
                var p0 = a + dir * s0;
                var p1 = a + dir * s1;
                var q0 = p0 + inward * t;
                var q1 = p1 + inward * t;
                cuts.Add(PathGeometry.Polyline([p0, p1, q1, q0], true));
            }
            for (int i = 0; i < n; i++)
            {
                bool gap = kind == 'A' ? i % 2 == 1 : i % 2 == 0;
                if (gap) Notch(t + i * seg, t + (i + 1) * seg);
            }
            if (kind == 'B')
            {
                // The corners belong to the neighbouring panel.
                Notch(0, t);
                Notch(length - t, length);
            }
        }
        Edge(top, new(0, 0), new(1, 0), new(0, 1), w);
        Edge(right, new(w, 0), new(0, 1), new(-1, 0), h);
        Edge(bottom, new(w, h), new(-1, 0), new(0, -1), w);
        Edge(left, new(0, h), new(0, -1), new(1, 0), h);
        var rect = PathGeometry.Rect(0, 0, w, h);
        if (openTop)
            // An open box's walls stop at the top: the corner squares there aren't needed either.
            cuts.Add(PathGeometry.Rect(-1, -1, w + 2, 1));
        return cuts.Count == 0 ? rect : ShapeGeometry.Boolean(rect, ShapeGeometry.Union(cuts), BoolOp.Subtract);
    }

    /// <summary>
    /// A living hinge: rows of straight cuts, staggered, that let flat sheet bend (mm, top-left at
    /// 0, 0). Cuts run up and down, so the panel bends around an up-and-down line. Every other
    /// column runs out through the top and bottom edges; the rest stop short. Shorter gaps and
    /// closer columns bend more easily but are weaker.
    /// </summary>
    public static PathGeometry LivingHinge(double width, double height, double cutLength, double gap, double spacing)
    {
        cutLength = Math.Max(1, cutLength);
        gap = Math.Max(0.5, gap);
        spacing = Math.Max(0.3, spacing);
        double period = cutLength + gap;
        var subs = new List<Subpath>();
        int columns = Math.Max(1, (int)Math.Floor(width / spacing));
        double x0 = (width - (columns - 1) * spacing) / 2;
        for (int c = 0; c < columns; c++)
        {
            double x = x0 + c * spacing;
            // Even columns: whole cuts with a gap at each edge. Odd columns: shifted half a period, cut through the edges.
            double y = c % 2 == 0 ? gap / 2 : gap / 2 + period / 2 - period;
            double lo = c % 2 == 0 ? gap / 2 : 0, hi = c % 2 == 0 ? height - gap / 2 : height;
            for (; y < height; y += period)
            {
                double a = Math.Max(lo, y), b = Math.Min(hi, y + cutLength);
                if (b - a >= 0.5) subs.Add(new Subpath(new Vec2(x, a), [Seg.Line(new Vec2(x, b))], false));
            }
        }
        return new PathGeometry(subs);
    }

    public enum EarringShape { Teardrop, Leaf, Circle, Hexagon, Arch }

    /// <summary>An earring outline with the hole for its hook at the top (mm, top-left at 0, 0).</summary>
    public static PathGeometry Earring(EarringShape shape, double width, double height, double holeDiameter, double holeInset)
    {
        double w = width, h = height, cx = w / 2;
        PathGeometry body = shape switch
        {
            EarringShape.Circle => PathGeometry.Ellipse(cx, h / 2, w / 2, h / 2),
            EarringShape.Leaf => new PathGeometry([new Subpath(new(cx, 0),
                [Seg.Cubic(new(cx + w * 2 / 3, h * 0.2), new(cx + w * 2 / 3, h * 0.8), new(cx, h)), Seg.Cubic(new(cx - w * 2 / 3, h * 0.8), new(cx - w * 2 / 3, h * 0.2), new(cx, 0))], true)]),
            EarringShape.Hexagon => PathGeometry.Polyline([.. Enumerable.Range(0, 6).Select(i => new Vec2(cx + w / 2 * Math.Sin(i * Math.PI / 3) / Math.Sin(Math.PI / 3), h / 2 - h / 2 * Math.Cos(i * Math.PI / 3)))], true),
            EarringShape.Arch => Arch(w, h),
            _ => Teardrop(w, h),
        };
        if (!(holeDiameter > 0)) return body;
        var hole = PathGeometry.Ellipse(cx, holeInset + holeDiameter / 2, holeDiameter / 2, holeDiameter / 2);
        return new PathGeometry(body.Subpaths.Concat(hole.Subpaths));
    }

    /// <summary>A drop: a point at the top, round at the bottom, the sides tangent to the circle.</summary>
    static PathGeometry Teardrop(double w, double h)
    {
        double r = Math.Min(w / 2, h / 2);
        var c = new Vec2(w / 2, h - r);
        double d = c.Y;
        if (d <= r * 1.01) return PathGeometry.Ellipse(w / 2, h / 2, w / 2, h / 2);
        double phi = Math.Acos(r / d);
        var pts = new List<Vec2> { new(w / 2, 0) };
        double a0 = -Math.PI / 2 + phi, a1 = 3 * Math.PI / 2 - phi;
        for (int i = 0; i <= 64; i++)
        {
            double a = a0 + (a1 - a0) * i / 64;
            pts.Add(c + new Vec2(Math.Cos(a), Math.Sin(a)) * r);
        }
        return PathGeometry.Polyline(pts, true);
    }

    /// <summary>A rainbow arch: a half ring standing on its ends.</summary>
    static PathGeometry Arch(double w, double h)
    {
        double rx = w / 2, ry = h, band = Math.Min(w, h) * 0.3;
        var pts = new List<Vec2>();
        for (int i = 0; i <= 48; i++) { double a = Math.PI + Math.PI * i / 48; pts.Add(new(w / 2 + rx * Math.Cos(a), h + ry * Math.Sin(a))); }
        for (int i = 48; i >= 0; i--) { double a = Math.PI + Math.PI * i / 48; pts.Add(new(w / 2 + (rx - band) * Math.Cos(a), h + (ry - band) * Math.Sin(a))); }
        return PathGeometry.Polyline(pts, true);
    }

    public enum TagShape { RoundedRectangle, Circle, Tag }

    /// <summary>A keychain or tag outline with its hole (mm, top-left at 0, 0).</summary>
    public static PathGeometry Keychain(TagShape shape, double width, double height, double holeDiameter, double holeInset)
    {
        double r = Math.Min(width, height) / 2;
        PathGeometry body = shape switch
        {
            TagShape.Circle => PathGeometry.Ellipse(width / 2, height / 2, width / 2, height / 2),
            TagShape.Tag => Tag(width, height),
            _ => PathGeometry.RoundRect(0, 0, width, height, r * 0.4, r * 0.4),
        };
        if (!(holeDiameter > 0)) return body;
        // The hole near the left end (top for a circle).
        var hole = shape == TagShape.Circle
            ? PathGeometry.Ellipse(width / 2, holeInset + holeDiameter / 2, holeDiameter / 2, holeDiameter / 2)
            : PathGeometry.Ellipse(holeInset + holeDiameter / 2, height / 2, holeDiameter / 2, holeDiameter / 2);
        return new PathGeometry(body.Subpaths.Concat(hole.Subpaths));
    }

    /// <summary>A luggage-tag outline: a rectangle with the left end cut to a blunt point.</summary>
    static PathGeometry Tag(double w, double h)
    {
        double c = Math.Min(h / 2, w / 4);
        return PathGeometry.Polyline([new(c, 0), new(w, 0), new(w, h), new(c, h), new(0, h - c), new(0, c)], true);
    }
}
