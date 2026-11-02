using Clipper2Lib;

namespace PhotonCanvas.Addon.Ruins;

/// <summary>One brick, stone, tile or board of a texture: its outline with half the groove round it (so neighbours meet), and its middle.</summary>
public readonly record struct Cell(PathsD Shape, PointD Middle);

/// <summary>
/// Broken things: lumpy outlines, outlines that follow whole bricks or stones (a ruin breaks along its
/// mortar), and the clean-up that keeps a piece in one piece.
/// </summary>
public static class Rough
{
    /// <summary>The middle of an outline (its centroid; the average of its points if it has no area).</summary>
    public static PointD Middle(PathD p)
    {
        double a = 0, cx = 0, cy = 0;
        for (int i = 0; i < p.Count; i++)
        {
            var q = p[i];
            var r = p[(i + 1) % p.Count];
            double c = q.x * r.y - r.x * q.y;
            a += c;
            cx += (q.x + r.x) * c;
            cy += (q.y + r.y) * c;
        }
        if (Math.Abs(a) < 1e-9) return p.Count == 0 ? new PointD(0, 0) : new PointD(p.Average(q => q.x), p.Average(q => q.y));
        return new PointD(cx / (3 * a), cy / (3 * a));
    }

    /// <summary>The areas with their holes filled.</summary>
    public static PathsD FillHoles(PathsD a) => [.. Geo.Tidy(a).Where(p => Clipper.Area(p) > 0)];

    /// <summary>The biggest separate piece of the areas (with its holes); crumbs that came loose are dropped.</summary>
    public static PathsD Largest(PathsD a)
    {
        var outers = FillHoles(a);
        if (outers.Count <= 1) return Geo.Tidy(a);
        var big = outers.OrderByDescending(p => Clipper.Area(p)).First();
        return Geo.Intersect(a, [big]);
    }

    /// <summary>How much of a piece (mm²) is in strips narrower than <paramref name="width"/> (they'd snap off).</summary>
    public static double Thin(PathsD cut, double width)
    {
        double w = width * 0.9;
        var opened = Geo.Offset(Geo.Offset(cut, -w / 2), w / 2);
        return Geo.Area(Geo.Subtract(cut, opened));
    }

    /// <summary>The areas without any strip narrower than <paramref name="width"/> (shrunk and grown back: thin bits vanish, the rest keeps its corners).</summary>
    public static PathsD Open(PathsD a, double width) => a.Count == 0 || width <= 0 ? a : Geo.Offset(Geo.Offset(a, -width / 2), width / 2);

    /// <summary>The areas with gaps narrower than <paramref name="width"/> closed (grown and shrunk back).</summary>
    public static PathsD Close(PathsD a, double width) => a.Count == 0 || width <= 0 ? a : Geo.Offset(Geo.Offset(a, width / 2), -width / 2);

    /// <summary>A smooth wobble: a few sine waves of random phase, between about −1 and 1.</summary>
    public sealed class Wobble
    {
        readonly (double F, double P, double A)[] _waves;

        public Wobble(Random rnd, double wavelength, int waves = 3)
        {
            _waves = new (double, double, double)[waves];
            double total = 0;
            for (int i = 0; i < waves; i++)
            {
                double a = 1.0 / (i + 1);
                _waves[i] = (2 * Math.PI / Math.Max(0.1, wavelength) * (i + 1) * (0.8 + 0.4 * rnd.NextDouble()), rnd.NextDouble() * 2 * Math.PI, a);
                total += a;
            }
            for (int i = 0; i < waves; i++) _waves[i].A /= total;
        }

        public double At(double t) => _waves.Sum(w => w.A * Math.Sin(w.F * t + w.P));
    }

    /// <summary>
    /// An outline made lumpy: points every <paramref name="step"/> mm along it moved in or out by up to
    /// <paramref name="amount"/> mm, smoothly, with waves about <paramref name="wavelength"/> mm long.
    /// </summary>
    public static PathD Lumpy(PathD p, double amount, double wavelength, Random rnd, double step = 0.8)
    {
        if (p.Count < 3 || amount <= 0) return p;
        var wob = new Wobble(rnd, wavelength);
        double sign = Clipper.Area(p) >= 0 ? 1 : -1;   // outward normal side
        var outp = new PathD();
        double along = 0;
        for (int i = 0; i < p.Count; i++)
        {
            var a = p[i];
            var b = p[(i + 1) % p.Count];
            double dx = b.x - a.x, dy = b.y - a.y, len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 1e-9) continue;
            double nx = dy / len * sign, ny = -dx / len * sign;
            int n = Math.Max(1, (int)Math.Ceiling(len / step));
            for (int k = 0; k < n; k++)
            {
                double t = (double)k / n, d = amount * wob.At(along + t * len);
                outp.Add(new PointD(a.x + dx * t + nx * d, a.y + dy * t + ny * d));
            }
            along += len;
        }
        return outp;
    }

    public static PathD Ellipse(double cx, double cy, double rx, double ry, int n = 96)
    {
        var p = new PathD(n);
        for (int i = 0; i < n; i++)
        {
            double a = 2 * Math.PI * i / n;
            p.Add(new PointD(cx + rx * Math.Cos(a), cy + ry * Math.Sin(a)));
        }
        return p;
    }

    /// <summary>Is the texture made of separate bricks, stones, tiles or plates that a break can follow?</summary>
    public static bool Cellular(string texture) => texture is "Brick" or "Tiles" or "Stone blocks" or "Fieldstone" or "Metal plates" or "Hexagons";

    /// <summary>Wood breaks in splinters.</summary>
    public static bool Wooden(string texture) => texture.StartsWith("Wood planks", StringComparison.Ordinal);

    /// <summary>The texture's bricks or stones, each grown by half the groove so neighbours meet in the middle of the mortar.</summary>
    public static List<Cell> Cells(TextureParts tex, double groove)
    {
        var cells = new List<Cell>(tex.Raised.Count);
        foreach (var r in tex.Raised)
        {
            if (r.Count < 3 || Math.Abs(Clipper.Area(r)) < 1e-4) continue;
            var grown = Geo.Offset([r], groove / 2);
            if (grown.Count == 0) continue;
            cells.Add(new Cell(grown, Middle(r)));
        }
        return cells;
    }

    /// <summary>Is a point inside the areas (even-odd)?</summary>
    public static bool Inside(PathsD area, PointD p)
    {
        int crossings = 0;
        foreach (var poly in area)
        {
            var r = Clipper.PointInPolygon(p, poly);
            if (r == PointInPolygonResult.IsOn) return true;
            if (r == PointInPolygonResult.IsInside) crossings++;
        }
        return crossings % 2 == 1;
    }

    /// <summary>The cells whose middles are inside the area, joined: an outline that follows whole bricks or stones.</summary>
    public static PathsD Snap(IEnumerable<Cell> cells, PathsD area)
    {
        var b = Geo.Bounds(area);
        var keep = new PathsD();
        foreach (var c in cells)
            if (c.Middle.x >= b.left && c.Middle.x <= b.right && c.Middle.y >= b.top && c.Middle.y <= b.bottom && Inside(area, c.Middle))
                keep.AddRange(c.Shape);
        return Geo.Union(keep);
    }

    /// <summary>Cells moved by a turn (degrees) about the origin, then along.</summary>
    public static List<Cell> Turn(List<Cell> cells, double deg, double dx, double dy)
    {
        double r = deg * Math.PI / 180, c = Math.Cos(r), s = Math.Sin(r);
        PointD M(PointD q) => new(q.x * c - q.y * s + dx, q.x * s + q.y * c + dy);
        return [.. cells.Select(x => new Cell(Geo.Map(x.Shape, M), M(x.Middle)))];
    }
}
