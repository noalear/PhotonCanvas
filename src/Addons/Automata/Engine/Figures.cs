using Clipper2Lib;
using PhotonCanvas.Addon.Mechanism;
using PhotonCanvas.Core;

namespace PhotonCanvas.Addon.Automata;

/// <summary>
/// Cut-out figures for the moving toys: simple side-on silhouettes built from circles, ovals and straight
/// pieces, facing right, standing on y = 0 and scaled to the height asked for. Eyes and windows are holes.
/// </summary>
public static class Figures
{
    /// <summary>A figure to pick: its key (saved), its name (shown) and how it's drawn, one unit tall.</summary>
    public sealed record Kind(string Key, string Name, Func<(Paths64 Solid, Paths64 Holes)> Make);

    /// <summary>One unit = this many mm while building (the shapes are scaled to their height after).</summary>
    const double U = 100;

    static Path64 Circle(double x, double y, double r) => Poly.Circle(new Vec2(x * U, y * U), r * U, 0.01);

    static Path64 Oval(double x, double y, double rx, double ry, double angleDeg = 0)
    {
        int n = 64;
        double a = angleDeg * Math.PI / 180, c = Math.Cos(a), s = Math.Sin(a);
        var pts = new List<Vec2>(n);
        for (int i = 0; i < n; i++)
        {
            double t = 2 * Math.PI * i / n, px = rx * Math.Cos(t), py = ry * Math.Sin(t);
            pts.Add(new Vec2((x + px * c - py * s) * U, (y + px * s + py * c) * U));
        }
        return Poly.Path(pts);
    }

    static Path64 Shape(params double[] xy)
    {
        var pts = new List<Vec2>();
        for (int i = 0; i + 1 < xy.Length; i += 2) pts.Add(new Vec2(xy[i] * U, xy[i + 1] * U));
        var p = Poly.Path(pts);
        if (!Clipper.IsPositive(p)) p.Reverse();
        return p;
    }

    /// <summary>A bar with round ends.</summary>
    static Paths64 Stick(double x0, double y0, double x1, double y1, double w)
    {
        Vec2 a = new(x0 * U, y0 * U), b = new(x1 * U, y1 * U);
        return [Poly.Circle(a, w * U / 2, 0.01), Poly.Circle(b, w * U / 2, 0.01), Shapes.Bar(a, b, w * U)];
    }

    static Paths64 All(params object[] items)
    {
        var o = new Paths64();
        foreach (var i in items)
        {
            if (i is Path64 p) o.Add(p);
            else if (i is Paths64 ps) o.AddRange(ps);
        }
        return o;
    }

    public static readonly IReadOnlyList<Kind> Kinds =
    [
        new("bird", "Bird", () => (All(
                Oval(0, 0.4, 0.3, 0.19, 12), Circle(0.27, 0.64, 0.14),
                Shape(0.38, 0.69, 0.6, 0.62, 0.38, 0.57),
                Shape(-0.22, 0.47, -0.6, 0.66, -0.56, 0.5, -0.24, 0.3),
                Stick(-0.04, 0.24, -0.08, 0.0, 0.05), Stick(0.08, 0.24, 0.06, 0.0, 0.05)),
            All(Circle(0.31, 0.68, 0.03)))),
        new("fish", "Fish", () => (All(
                Oval(0.02, 0.52, 0.38, 0.24), Shape(-0.28, 0.52, -0.6, 0.8, -0.54, 0.52, -0.6, 0.24),
                Shape(-0.12, 0.7, 0.04, 0.92, 0.16, 0.72), Stick(0, 0.32, 0, 0, 0.08)),
            All(Circle(0.24, 0.58, 0.045)))),
        new("cat", "Cat", () => (All(
                Oval(-0.05, 0.33, 0.25, 0.33), Circle(0.13, 0.74, 0.17),
                Shape(0.02, 0.82, 0.03, 1.0, 0.13, 0.88), Shape(0.17, 0.88, 0.27, 1.0, 0.29, 0.8),
                Stick(-0.2, 0.1, -0.46, 0.3, 0.07), Stick(-0.46, 0.3, -0.42, 0.56, 0.07)),
            All(Circle(0.21, 0.77, 0.025)))),
        new("person", "Person", () => (All(
                Circle(0, 0.86, 0.11), Shape(-0.13, 0.76, 0.13, 0.75, 0.16, 0.4, -0.16, 0.4),
                Stick(-0.12, 0.72, -0.3, 0.46, 0.07), Stick(0.12, 0.72, 0.3, 0.46, 0.07),
                Stick(-0.07, 0.42, -0.1, 0.02, 0.09), Stick(0.07, 0.42, 0.1, 0.02, 0.09)),
            new Paths64())),
        new("boat", "Boat on waves", () => (All(
                Shape(-0.5, 0.3, 0.5, 0.3, 0.36, 0.1, -0.36, 0.1), Stick(0, 0.3, 0, 0.98, 0.04),
                Shape(0, 0.94, 0, 0.36, 0.4, 0.36), Shape(0, 0.84, 0, 0.36, -0.3, 0.36),
                Shape(-0.52, 0, 0.52, 0, 0.52, 0.1, -0.52, 0.1),
                Circle(-0.42, 0.1, 0.07), Circle(-0.21, 0.1, 0.07), Circle(0, 0.1, 0.07), Circle(0.21, 0.1, 0.07), Circle(0.42, 0.1, 0.07)),
            All(Circle(-0.2, 0.2, 0.035), Circle(0.2, 0.2, 0.035)))),
        new("flower", "Flower", () => (All(
                Stick(0, 0, 0, 0.62, 0.05), Oval(0.11, 0.28, 0.12, 0.05, 30), Oval(-0.11, 0.38, 0.12, 0.05, -30),
                Circle(0, 0.88, 0.11), Circle(0.124, 0.808, 0.11), Circle(0.124, 0.663, 0.11),
                Circle(0, 0.6, 0.11), Circle(-0.124, 0.663, 0.11), Circle(-0.124, 0.808, 0.11)),
            All(Circle(0, 0.735, 0.05)))),
        new("star", "Star", () => (All(Star(0, 0.62, 0.38, 0.16), Stick(0, 0.5, 0, 0, 0.07)), new Paths64())),
        new("heart", "Heart", () => (All(
                Circle(-0.13, 0.78, 0.16), Circle(0.13, 0.78, 0.16), Shape(-0.285, 0.73, 0.285, 0.73, 0, 0.34),
                Stick(0, 0.4, 0, 0, 0.07)),
            new Paths64())),
        new("rocket", "Rocket", () => (All(
                Shape(-0.1, 0.22, 0.1, 0.22, 0.1, 0.76, -0.1, 0.76), Shape(-0.1, 0.75, 0.1, 0.75, 0, 1.0),
                Shape(-0.1, 0.42, -0.1, 0.22, -0.25, 0.12), Shape(0.1, 0.22, 0.1, 0.42, 0.25, 0.12),
                Shape(-0.06, 0.23, 0.06, 0.23, 0, 0.08), Stick(0, 0.1, 0, 0, 0.05)),
            All(Circle(0, 0.6, 0.05)))),
        new("tree", "Tree", () => (All(
                Shape(-0.05, 0, 0.05, 0, 0.05, 0.26, -0.05, 0.26), Shape(-0.34, 0.2, 0.34, 0.2, 0, 0.55),
                Shape(-0.27, 0.45, 0.27, 0.45, 0, 0.78), Shape(-0.2, 0.68, 0.2, 0.68, 0, 1.0)),
            new Paths64())),
        new("card", "Blank card (draw your own)", () => (All(Shapes.RoundRect(-0.35 * U, 0.02 * U, 0.35 * U, 1.0 * U, 0.06 * U), Shape(-0.06, 0, 0.06, 0, 0.06, 0.03, -0.06, 0.03)), new Paths64())),
    ];

    static Path64 Star(double x, double y, double outer, double inner)
    {
        var pts = new List<double>();
        for (int i = 0; i < 10; i++)
        {
            double a = Math.PI / 2 + Math.PI * i / 5, r = i % 2 == 0 ? outer : inner;
            pts.Add(x + r * Math.Cos(a));
            pts.Add(y + r * Math.Sin(a));
        }
        return Shape([.. pts]);
    }

    public static Kind? Find(string key) => Kinds.FirstOrDefault(k => k.Key == key);

    public static string NameOf(string key) => Find(key)?.Name ?? "No figure";

    /// <summary>"a bird", "a blank card": in a sentence.</summary>
    public static string Called(string key)
    {
        var name = NameOf(key);
        int cut = name.IndexOf(" (", StringComparison.Ordinal);
        if (cut > 0) name = name[..cut];
        return $"a {name.ToLowerInvariant()}";
    }

    /// <summary>
    /// The figure <paramref name="key"/> (null for none), <paramref name="heightMm"/> tall, standing on y = 0,
    /// its middle at x = 0 where it stands; <paramref name="flip"/> faces it left.
    /// </summary>
    public static Paths64? Make(string key, double heightMm, bool flip)
    {
        if (Find(key) is not { } kind || !(heightMm > 0)) return null;
        var (solid, holes) = kind.Make();
        var shape = Poly.Subtract(Poly.Union(solid), holes);
        var b = Poly.Bounds(shape);
        double k = heightMm / b.Height;
        var o = new Paths64(shape.Count);
        foreach (var path in shape)
        {
            var p = new Path64(path.Count);
            foreach (var pt in path)
            {
                var v = Poly.V(pt);
                double x = v.X * k, y = (v.Y - b.MinY) * k;
                p.Add(Poly.P(flip ? -x : x, y));
            }
            if (flip) p.Reverse();
            o.Add(p);
        }
        return o;
    }
}
