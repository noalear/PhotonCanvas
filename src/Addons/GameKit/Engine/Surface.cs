using Clipper2Lib;
using PhotonCanvas.Addon.Terrain;

namespace PhotonCanvas.Addon.GameKit;

/// <summary>
/// The finish on a face: the furniture maker's wood grain and planks, or the wall maker's stone,
/// brick, tile, plate and hexagon textures (Walls/Engine/Textures.cs), drawn the same way so a dice
/// tower can match the scenery. Sizes are the wall maker's defaults times a scale.
/// </summary>
public static class Surface
{
    /// <summary>The etched areas and scored lines of the finish over <paramref name="region"/> (flat mm, y up).</summary>
    public static (PathsD Etched, List<PathD> Lines) Make(PathsD region, Finish finish, double scale, bool alongU, Random rnd)
    {
        if (finish == Finish.None || region.Count == 0) return ([], []);
        double k = Math.Clamp(scale, 0.25, 10);
        var b = Geo.Bounds(region);
        double len = b.right - b.left, h = b.bottom - b.top;
        if (len < 1 || h < 1) return ([], []);
        if (finish is Finish.WoodGrain or Finish.Planks)
        {
            // The furniture's wood is drawn for 28 mm furniture: drawn small, then scaled up.
            var small = Geo.Map(region, p => new PointD(p.x / k, p.y / k));
            var (e, l) = WoodFace.Make(small, alongU, finish == Finish.Planks, true, 3, rnd);
            return (Geo.Map(e, p => new PointD(p.x * k, p.y * k)), Geo.Map(l, p => new PointD(p.x * k, p.y * k)));
        }
        if (ThemeTexture(finish) is { } theme)
        {
            var info = ThemeTextures.Find(theme)!;
            var (tr, te, tl) = ThemeTextures.Make(theme, len, h, info.SizeA * k, info.SizeB * k, info.Groove * k, rnd);
            var r2 = Geo.Move(tr, b.left, b.top);
            var e2 = Geo.Move(te, b.left, b.top);
            var l2 = Geo.Map(tl, p => new PointD(p.x + b.left, p.y + b.top));
            return (Geo.Union(Geo.Subtract(region, Geo.Union(r2)), Geo.Intersect(e2, region)), Geo.ClipLines(l2, region));
        }
        var t = finish switch
        {
            Finish.Brick => Bricks(len, h, 8 * k, 3.5 * k, 0.6 * k, 0.5),
            Finish.Tiles => Bricks(len, h, 6 * k, 6 * k, 0.6 * k, 0),
            Finish.StoneBlocks => Blocks(len, h, 12 * k, 6 * k, 0.8 * k, rnd),
            Finish.Fieldstone => Fieldstone(len, h, 9 * k, 6 * k, 0.9 * k, rnd),
            Finish.MetalPlates => Plates(len, h, 20 * k, 14 * k, 0.5 * k),
            Finish.Hexagons => Hexagons(len, h, 8 * k, 0.7 * k),
            _ => (Raised: new PathsD(), Lines: new List<PathD>()),
        };
        // Laid from the face's bottom-left corner; what's between the raised pieces is etched away.
        var raised = Geo.Move(t.Raised, b.left, b.top);
        var lines = Geo.Map(t.Lines, p => new PointD(p.x + b.left, p.y + b.top));
        return (Geo.Subtract(region, Geo.Union(raised)), Geo.ClipLines(lines, region));
    }

    /// <summary>The themes' textures (Shared/Terrain/ThemeTextures.cs), drawn as the wall maker draws them.</summary>
    static string? ThemeTexture(Finish f) => f switch
    {
        Finish.DungeonBlocks => ThemeTextures.DungeonBlocks,
        Finish.CaveRock => ThemeTextures.CaveRock,
        Finish.SciFiPanels => ThemeTextures.SciFiPanels,
        Finish.Sandstone => ThemeTextures.Sandstone,
        Finish.Adobe => ThemeTextures.Adobe,
        _ => null,
    };

    static PathD Box(double x0, double y0, double x1, double y1, double g) => Geo.Rect(x0 + g / 2, y0 + g / 2, x1 - g / 2, y1 - g / 2);

    static (PathsD Raised, List<PathD> Lines) Bricks(double len, double h, double a, double b, double g, double shift)
    {
        var raised = new PathsD();
        int rows = (int)Math.Ceiling(h / b) + 1;
        for (int r = 0; r < rows; r++)
        {
            double y = r * b, off = r % 2 == 1 ? shift * a : 0;
            for (double x = -off; x < len + a; x += a) raised.Add(Box(x, y, x + a, y + b, g));
        }
        return (raised, []);
    }

    static (PathsD Raised, List<PathD> Lines) Blocks(double len, double h, double a, double b, double g, Random rnd)
    {
        var raised = new PathsD();
        for (double y = 0; y < h + b;)
        {
            double bh = b * (0.75 + 0.5 * rnd.NextDouble());
            for (double x = -a * rnd.NextDouble(); x < len + a;)
            {
                double w = a * (0.6 + 0.9 * rnd.NextDouble());
                raised.Add(Box(x, y, x + w, y + bh, g));
                x += w;
            }
            y += bh;
        }
        return (raised, []);
    }

    /// <summary>Rows whose edges wander up and down, split into stones with leaning sides, shrunk and rounded off.</summary>
    static (PathsD Raised, List<PathD> Lines) Fieldstone(double len, double h, double a, double b, double g, Random rnd)
    {
        double step = Math.Max(0.8, a / 3), x0 = -a, x1 = len + a;
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
        while (y0 < h + b)
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
        return (raised, []);
    }

    static (PathsD Raised, List<PathD> Lines) Plates(double len, double h, double a, double b, double g)
    {
        var raised = new PathsD();
        var lines = new List<PathD>();
        double rivet = Math.Clamp(Math.Min(a, b) * 0.03, 0.2, 0.9), inset = Math.Max(1, rivet * 3), pitch = Math.Max(3, rivet * 8);
        int rows = (int)Math.Ceiling(h / b) + 1;
        for (int r = 0; r < rows; r++)
        {
            double y = r * b, off = r % 2 == 1 ? a / 2 : 0;
            for (double x = -off; x < len + a; x += a)
            {
                raised.Add(Box(x, y, x + a, y + b, g));
                double x0 = x + inset, x1 = x + a - inset, y0 = y + inset, y1 = y + b - inset;
                if (x1 <= x0 || y1 <= y0) continue;
                int nx = Math.Max(1, (int)Math.Round((x1 - x0) / pitch)), ny = Math.Max(1, (int)Math.Round((y1 - y0) / pitch));
                for (int i = 0; i <= nx; i++)
                {
                    lines.Add(Geo.Loop(Geo.Circle(x0 + (x1 - x0) * i / nx, y0, rivet)));
                    lines.Add(Geo.Loop(Geo.Circle(x0 + (x1 - x0) * i / nx, y1, rivet)));
                }
                for (int j = 1; j < ny; j++)
                {
                    lines.Add(Geo.Loop(Geo.Circle(x0, y0 + (y1 - y0) * j / ny, rivet)));
                    lines.Add(Geo.Loop(Geo.Circle(x1, y0 + (y1 - y0) * j / ny, rivet)));
                }
            }
        }
        return (raised, lines);
    }

    static (PathsD Raised, List<PathD> Lines) Hexagons(double len, double h, double size, double g)
    {
        double r = size / Math.Sqrt(3), dx = 1.5 * r, inner = r - g / Math.Sqrt(3);
        var raised = new PathsD();
        int col = 0;
        for (double x = 0; x < len + size; x += dx, col++)
            for (double y = col % 2 == 0 ? 0 : size / 2; y < h + size; y += size)
            {
                var hex = new PathD(6);
                for (int k = 0; k < 6; k++) hex.Add(new PointD(x + inner * Math.Cos(k * Math.PI / 3), y + inner * Math.Sin(k * Math.PI / 3)));
                raised.Add(hex);
            }
        return (raised, []);
    }
}
