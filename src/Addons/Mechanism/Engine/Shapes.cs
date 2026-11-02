using Clipper2Lib;
using PhotonCanvas.Core;

namespace PhotonCanvas.Addon.Mechanism;

/// <summary>Plain shapes the parts are built from (maths axes, mm).</summary>
public static class Shapes
{
    /// <summary>A rectangle with rounded corners (radius clamped to fit).</summary>
    public static Path64 RoundRect(double x0, double y0, double x1, double y1, double r)
    {
        double w = Math.Abs(x1 - x0), h = Math.Abs(y1 - y0);
        r = Math.Clamp(r, 0, Math.Min(w, h) / 2 - 1e-6);
        if (r <= 1e-6) return Poly.Rect(x0, y0, x1, y1);
        double xa = Math.Min(x0, x1), ya = Math.Min(y0, y1), xb = Math.Max(x0, x1), yb = Math.Max(y0, y1);
        var pts = new List<Vec2>();
        void Corner(double cx, double cy, double a0)
        {
            int n = Math.Max(4, Poly.Segments(r, 0.003) / 4);
            for (int i = 0; i <= n; i++)
            {
                double a = a0 + Math.PI / 2 * i / n;
                pts.Add(new(cx + r * Math.Cos(a), cy + r * Math.Sin(a)));
            }
        }
        Corner(xb - r, ya + r, -Math.PI / 2);
        Corner(xb - r, yb - r, 0);
        Corner(xa + r, yb - r, Math.PI / 2);
        Corner(xa + r, ya + r, Math.PI);
        return Poly.Path(pts);
    }

    /// <summary>A bar with round ends from <paramref name="a"/> to <paramref name="b"/> (a link, a crank arm).</summary>
    public static Paths64 Link(Vec2 a, Vec2 b, double width) =>
        Poly.Union([Poly.Circle(a, width / 2, 0.003), Poly.Circle(b, width / 2, 0.003), Bar(a, b, width)]);

    /// <summary>A straight bar <paramref name="width"/> wide from <paramref name="a"/> to <paramref name="b"/> (square ends), anticlockwise.</summary>
    public static Path64 Bar(Vec2 a, Vec2 b, double width)
    {
        var d = b - a;
        double len = d.Length;
        if (len < 1e-9) return [];
        var n = new Vec2(-d.Y / len, d.X / len) * (width / 2);
        // Anticlockwise (positive), like circles: joined with them (a link's round ends) it must wind the same way.
        return Poly.Path([a - n, b - n, b + n, a + n]);
    }

    /// <summary>A rectangle centred on <paramref name="c"/>, <paramref name="w"/> by <paramref name="h"/>, turned by <paramref name="angle"/>.</summary>
    public static Path64 Box(Vec2 c, double w, double h, double angle = 0)
    {
        var p = new Pose(c, angle);
        return Poly.Path(new[] { new Vec2(-w / 2, -h / 2), new Vec2(w / 2, -h / 2), new Vec2(w / 2, h / 2), new Vec2(-w / 2, h / 2) }.Select(p.Apply));
    }

    /// <summary>
    /// Windows cut out of a wheel between its hub and its rim, leaving <paramref name="spokes"/> spokes: lighter,
    /// quicker to turn, and it looks like a gear. Null when there isn't room.
    /// </summary>
    public static Paths64? SpokeWindows(double hubR, double rimR, int spokes)
    {
        double inner = hubR, outer = rimR;
        if (outer - inner < 6 || spokes < 3) return null;
        double spokeW = Math.Max(3, 0.12 * outer);
        double round = Math.Min(2.5, (outer - inner) / 3);
        var windows = new Paths64();
        for (int k = 0; k < spokes; k++)
        {
            double a0 = 2 * Math.PI * k / spokes, a1 = 2 * Math.PI * (k + 1) / spokes;
            // A sector, narrowed on each side by half a spoke (measured straight across, so spokes keep their width).
            var pts = new List<Vec2>();
            int n = Math.Max(12, Poly.Segments(outer, 0.003) / spokes);
            for (int i = 0; i <= n; i++) pts.Add(Polar(outer, a0 + (a1 - a0) * i / n));
            for (int i = n; i >= 0; i--) pts.Add(Polar(inner, a0 + (a1 - a0) * i / n));
            var sector = new Paths64 { Poly.Path(pts) };
            var cutSides = new Paths64
            {
                Bar(default, Polar(outer * 2, a0), spokeW),
                Bar(default, Polar(outer * 2, a1), spokeW),
            };
            var w = Poly.Subtract(sector, Poly.Union(cutSides));
            w = Poly.Grow(Poly.Grow(w, -round), round);
            if (Poly.Area(w) > 4) windows.AddRange(w);
        }
        return windows.Count == 0 ? null : windows;
    }

    public static Vec2 Polar(double r, double a) => new(r * Math.Cos(a), r * Math.Sin(a));

    /// <summary>A curved arrow round a centre (for marking which way a part turns): from <paramref name="a0"/> to <paramref name="a1"/> radians.</summary>
    public static List<List<Vec2>> TurnArrow(Vec2 c, double r, double a0, double a1)
    {
        var arc = new List<Vec2>();
        int n = 24;
        for (int i = 0; i <= n; i++) arc.Add(c + Polar(r, a0 + (a1 - a0) * i / n));
        // The head: two short lines back from the end, either side of the arc.
        double dir = Math.Sign(a1 - a0);
        var tip = arc[^1];
        var tangent = Polar(1, a1 + dir * Math.PI / 2);
        var radial = Polar(1, a1);
        double head = Math.Max(1.2, r * 0.18);
        var back = tip - tangent * head;
        return [arc, [back + radial * head * 0.55, tip, back - radial * head * 0.55]];
    }

    /// <summary>A short straight line (an alignment tick).</summary>
    public static List<Vec2> Tick(Vec2 a, Vec2 b) => [a, b];
}
