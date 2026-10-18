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
