using Clipper2Lib;
using PhotonCanvas.Addon.Walls;

namespace PhotonCanvas.Addon.Bases;

/// <summary>A size bases are often sold in, for the list of sizes.</summary>
public sealed record BasePreset(BaseShape Shape, double W, double D, string Use);

/// <summary>Base outlines (mm, y up, the outline's box from 0, 0 to its width and depth) and the usual sizes.</summary>
public static class BaseShapes
{
    /// <summary>Common sizes for tabletop figures, by shape.</summary>
    public static readonly IReadOnlyList<BasePreset> Presets =
    [
        new(BaseShape.Round, 20, 20, "small figures"),
        new(BaseShape.Round, 25, 25, "a 28 mm figure"),
        new(BaseShape.Round, 28.5, 28.5, "a 28 mm figure, larger"),
        new(BaseShape.Round, 32, 32, "a 28–32 mm figure"),
        new(BaseShape.Round, 40, 40, "a large figure or a leader"),
        new(BaseShape.Round, 50, 50, "a big creature"),
        new(BaseShape.Round, 60, 60, "a big creature or a small vehicle"),
        new(BaseShape.Round, 80, 80, "a monster"),
        new(BaseShape.Round, 100, 100, "a huge monster"),
        new(BaseShape.Round, 130, 130, "a giant"),
        new(BaseShape.Oval, 60, 35, "a rider or a beast"),
        new(BaseShape.Oval, 75, 42, "a rider or a beast, larger"),
        new(BaseShape.Oval, 90, 52, "a big rider or a chariot"),
        new(BaseShape.Oval, 105, 70, "a large beast"),
        new(BaseShape.Oval, 120, 92, "a vehicle or a monster"),
        new(BaseShape.Oval, 170, 105, "a big vehicle"),
        new(BaseShape.Square, 20, 20, "rank-and-file infantry"),
        new(BaseShape.Square, 25, 25, "rank-and-file infantry, larger"),
        new(BaseShape.Square, 30, 30, "a hero or heavy infantry"),
        new(BaseShape.Square, 40, 40, "a war band or a big figure"),
        new(BaseShape.Square, 50, 50, "a monster"),
        new(BaseShape.Rectangle, 25, 50, "cavalry"),
        new(BaseShape.Rectangle, 30, 60, "heavy cavalry"),
        new(BaseShape.Rectangle, 40, 20, "historical infantry in pairs"),
        new(BaseShape.Rectangle, 50, 100, "a chariot or a monster"),
        new(BaseShape.Hexagon, 25, 25, "a figure on a hex grid"),
        new(BaseShape.Hexagon, 30, 30, "a figure on a hex grid, larger"),
        new(BaseShape.Hexagon, 50, 50, "a big figure on a hex grid"),
    ];

    public static IEnumerable<BasePreset> For(BaseShape shape) => Presets.Where(p => p.Shape == shape);

    public static string Word(BaseShape s) => s switch
    {
        BaseShape.Round => "Round",
        BaseShape.Oval => "Oval",
        BaseShape.Square => "Square",
        BaseShape.Rectangle => "Rectangular",
        _ => "Hexagonal",
    };

    static string N(double v) => v.ToString("0.##", System.Globalization.CultureInfo.CurrentCulture);

    /// <summary>"25 mm", "60 × 35 mm".</summary>
    public static string SizeText(BaseShape s, double w, double d) => s is BaseShape.Round or BaseShape.Square or BaseShape.Hexagon ? $"{N(w)} mm" : $"{N(w)} × {N(d)} mm";

    /// <summary>"Round 25 mm".</summary>
    public static string Label(BaseShape s, double w, double d) => $"{Word(s)} {SizeText(s, w, d)}";

    /// <summary>The outline's box: a round, square or hexagonal base uses only the width (a hexagon is wider across its points).</summary>
    public static (double W, double D) Box(BaseShape s, double w, double d) => s switch
    {
        BaseShape.Round or BaseShape.Square => (w, w),
        BaseShape.Hexagon => (w * 2 / Math.Sqrt(3), w),
        _ => (w, d),
    };

    /// <summary>The base's outline, in its box from 0, 0.</summary>
    public static PathsD Outline(BaseShape s, double w, double d, double corner)
    {
        var (bw, bd) = Box(s, w, d);
        switch (s)
        {
            case BaseShape.Round:
                return [Geo.Circle(bw / 2, bd / 2, bw / 2)];
            case BaseShape.Oval:
                return [Ellipse(bw / 2, bd / 2, bw / 2, bd / 2)];
            case BaseShape.Hexagon:
            {
                // Flat top and bottom: the width across the flats is the depth.
                double r = bw / 2;
                var hex = new PathD(6);
                for (int k = 0; k < 6; k++) hex.Add(new PointD(bw / 2 + r * Math.Cos(k * Math.PI / 3), bd / 2 + r * Math.Sin(k * Math.PI / 3)));
                return [hex];
            }
            default:
                return [RoundedRect(0, 0, bw, bd, corner)];
        }
    }

    /// <summary>An ellipse, with points close enough for the biggest bases.</summary>
    public static PathD Ellipse(double cx, double cy, double rx, double ry)
    {
        int n = Geo.Segments(Math.Max(rx, ry), 0.01);
        var p = new PathD(n);
        for (int i = 0; i < n; i++)
        {
            double a = 2 * Math.PI * i / n;
            p.Add(new PointD(cx + rx * Math.Cos(a), cy + ry * Math.Sin(a)));
        }
        return p;
    }

    /// <summary>A rectangle with its corners rounded off (a radius of 0 leaves them square).</summary>
    public static PathD RoundedRect(double x0, double y0, double x1, double y1, double r)
    {
        r = Math.Clamp(r, 0, Math.Min(x1 - x0, y1 - y0) / 2);
        if (r < 0.01) return Geo.Rect(x0, y0, x1, y1);
        var p = new PathD();
        p.AddRange(Geo.Arc(x1 - r, y0 + r, r, -Math.PI / 2, 0));
        p.AddRange(Geo.Arc(x1 - r, y1 - r, r, 0, Math.PI / 2));
        p.AddRange(Geo.Arc(x0 + r, y1 - r, r, Math.PI / 2, Math.PI));
        p.AddRange(Geo.Arc(x0 + r, y0 + r, r, Math.PI, 1.5 * Math.PI));
        return p;
    }

    /// <summary>The outline shrunk all round by <paramref name="by"/> (a layer higher up a stepped base).</summary>
    public static PathsD Inset(PathsD outline, double by) => by <= 0 ? outline : Geo.Offset(outline, -by, round: true);
}
