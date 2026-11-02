using Clipper2Lib;

namespace PhotonCanvas.Addon.Scatter;

/// <summary>
/// Stonework and shingles etched on a face, laid out like the wall maker's textures of the same name
/// (so a well matches the walls round it): the stones stand out and the grooves between them are
/// etched away. Faces are in flat mm, y up, from (0, 0) to (length, height).
/// </summary>
public static class StoneFace
{
    public const string Plain = "Plain";

    /// <summary>The textures a well can have: name, the stone's size and course height (mm, for 28 mm figures) and the groove.</summary>
    public sealed record Preset(string Name, double A, double B, double Groove);

    public static readonly IReadOnlyList<Preset> Presets =
    [
        new("Fieldstone", 5, 3.5, 0.6),
        new("Stone blocks", 6, 3, 0.5),
        new("Brick", 4, 1.8, 0.4),
        new(Plain, 0, 0, 0),
    ];

    public static Preset? Find(string name) => Presets.FirstOrDefault(p => p.Name == name);

    /// <summary>The grooves (etched areas) of a texture over a face <paramref name="length"/> × <paramref name="height"/>, its sizes scaled by <paramref name="k"/>.</summary>
    public static PathsD Grooves(string texture, double length, double height, double k, Random rnd)
    {
        if (Find(texture) is not { } p || p.Name == Plain || length <= 0 || height <= 0) return [];
        double a = Math.Max(0.8, p.A * k), b = Math.Max(0.8, p.B * k), g = Math.Clamp(p.Groove * Math.Sqrt(k), 0.25, Math.Min(a, b) / 3);
        var raised = p.Name switch
        {
            "Brick" => Bricks(length, height, a, b, g, 0.5),
            "Stone blocks" => Blocks(length, height, a, b, g, rnd),
            _ => Fieldstone(length, height, a, b, g, rnd),
        };
        var face = new PathsD { Geo.Rect(0, 0, length, height) };
        return Geo.Subtract(face, Geo.Union(raised));
    }

    /// <summary>Shingles in rows (each moved along half a shingle), as grooves over a face.</summary>
    public static PathsD Shingles(double length, double height, double w, double h, double g)
    {
        var face = new PathsD { Geo.Rect(0, 0, length, height) };
        return Geo.Subtract(face, Geo.Union(Bricks(length, height, w, h, g, 0.5)));
    }

    static PathD Box(double x0, double y0, double x1, double y1, double g) => Geo.Rect(x0 + g / 2, y0 + g / 2, x1 - g / 2, y1 - g / 2);

    static PathsD Bricks(double length, double height, double a, double b, double g, double shift)
    {
        var raised = new PathsD();
        int rows = (int)Math.Ceiling(height / b) + 1;
        for (int r = 0; r < rows; r++)
        {
            double y = r * b, off = r % 2 == 1 ? shift * a : 0;
            for (double x = -off; x < length + a; x += a) raised.Add(Box(x, y, x + a, y + b, g));
        }
        return raised;
    }

    static PathsD Blocks(double length, double height, double a, double b, double g, Random rnd)
    {
        var raised = new PathsD();
        for (double y = 0; y < height + b;)
        {
            double h = b * (0.75 + 0.5 * rnd.NextDouble());
            for (double x = -a * rnd.NextDouble(); x < length + a;)
            {
                double w = a * (0.6 + 0.9 * rnd.NextDouble());
                raised.Add(Box(x, y, x + w, y + h, g));
                x += w;
            }
            y += h;
        }
        return raised;
    }

    /// <summary>Rows whose edges wander up and down, split into stones with leaning sides, shrunk and rounded off.</summary>
    static PathsD Fieldstone(double length, double height, double a, double b, double g, Random rnd)
    {
        double step = Math.Max(0.8, a / 3), x0 = -a, x1 = length + a;
        int n = (int)Math.Ceiling((x1 - x0) / step) + 1;
        double[] Edge(double y) => [.. Enumerable.Range(0, n).Select(_ => y + (rnd.NextDouble() - 0.5) * 0.45 * b)];
        double At(double[] e, double x)
        {
            double t = Math.Clamp((x - x0) / step, 0, n - 1.0001);
            int i = (int)t;
            return e[i] + (e[i + 1] - e[i]) * (t - i);
        }
        List<PointD> Along(double[] e, double xa, double xb)
        {
            var pts = new List<PointD> { new(xa, At(e, xa)) };
            for (int i = Math.Max(0, (int)Math.Floor((xa - x0) / step) + 1); i < n && x0 + i * step < xb - 1e-6; i++) pts.Add(new(x0 + i * step, e[i]));
            pts.Add(new(xb, At(e, xb)));
            return pts;
        }
        var raised = new PathsD();
        double y0 = -b * 0.5;
        var lower = Edge(y0);
        double round = Math.Min(a, b) * 0.18;
        while (y0 < height + b)
        {
            double y1 = y0 + b * (0.75 + 0.5 * rnd.NextDouble());
            var upper = Edge(y1);
            double side = x0 - a * rnd.NextDouble();
            double lowX = side + (rnd.NextDouble() - 0.5) * a * 0.3, upX = side + (rnd.NextDouble() - 0.5) * a * 0.3;
            while (Math.Min(lowX, upX) < x1)
            {
                double next = Math.Max(lowX, upX) + a * (0.6 + 0.8 * rnd.NextDouble());
                double nLow = next + (rnd.NextDouble() - 0.5) * a * 0.3, nUp = next + (rnd.NextDouble() - 0.5) * a * 0.3;
                var stone = new PathD(Along(lower, lowX, nLow));
                var top = Along(upper, upX, nUp);
                top.Reverse();
                stone.AddRange(top);
                raised.AddRange(Geo.Offset(Geo.Offset([stone], -(g / 2 + round), round: true), round, round: true));
                lowX = nLow;
                upX = nUp;
            }
            y0 = y1;
            lower = upper;
        }
        return raised;
    }
}
