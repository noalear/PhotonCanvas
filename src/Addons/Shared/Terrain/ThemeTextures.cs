using Clipper2Lib;

namespace PhotonCanvas.Addon.Terrain;

/// <summary>
/// The textures the themes brought (dungeon blocks, cave rock, sci-fi panels, sandstone and adobe),
/// drawn the same way in every terrain maker so a wall, a ruin, a bridge and a dice tower made with the
/// same sizes and variation match stone for stone. Like the wall maker's own textures they're made of
/// raised pieces (what's between them is etched away), extra areas etched as they are and lines scored
/// as they are, over a face from (0, 0) to (length, height) in millimetres with y up, running a little
/// over every edge (the maker clips them to its piece).
///
/// Compiled into each terrain add-on that draws them (see its project file): nothing is shared at run time.
/// </summary>
internal static class ThemeTextures
{
    public const string DungeonBlocks = "Dungeon blocks";
    public const string CaveRock = "Cave rock";
    public const string SciFiPanels = "Sci-fi panels";
    public const string Sandstone = "Sandstone";
    public const string Adobe = "Adobe";

    /// <summary>A texture's name, the names of its two sizes and their defaults (mm, for 28 mm figures), its groove and what it looks like.</summary>
    public sealed record Info(string Name, string SizeALabel, double SizeA, string SizeBLabel, double SizeB, double Groove, string About);

    public static readonly IReadOnlyList<Info> All =
    [
        new(DungeonBlocks, "Block length", 14, "Course height", 8, 0.9, "Big, worn stone blocks with chipped corners and the odd crack: dungeons, crypts and keeps."),
        new(CaveRock, "Rock size", 16, "Layer height", 10, 1.0, "Rough rock in uneven layers, split by cracks and pitted: caves, cliffs and mines."),
        new(SciFiPanels, "Panel width", 16, "Panel height", 12, 0.5, "Metal panels of different widths with cut corners, vents, bolts, inset edges and the odd hazard stripe."),
        new(Sandstone, "Block length", 16, "Course height", 7, 0.7, "Long, soft-cornered sandstone blocks with wind-worn lines and pits: desert temples and towns."),
        new(Adobe, "Patch size", 14, "Brick height", 3, 0.5, "Smooth mud plaster with patches fallen away to show the bricks beneath, and a few cracks."),
    ];

    public static Info? Find(string? name) => All.FirstOrDefault(i => i.Name == name);

    /// <summary>
    /// The texture's raised pieces, etched areas and scored lines over a face <paramref name="length"/> ×
    /// <paramref name="height"/> mm: <paramref name="a"/> and <paramref name="b"/> are its two sizes,
    /// <paramref name="g"/> the groove between pieces. Empty for a name that isn't one of these.
    /// </summary>
    public static (PathsD Raised, PathsD Etched, List<PathD> Lines) Make(string name, double length, double height, double a, double b, double g, Random rnd)
    {
        a = Math.Max(1, a);
        b = Math.Max(1, b);
        g = Math.Clamp(g, 0.05, Math.Min(a, b) / 3);
        // Too many pieces to draw sensibly (a tiny size over a big face): nothing rather than a hang.
        if (length * height / (a * b) > 40000) return ([], [], []);
        return name switch
        {
            DungeonBlocks => Dungeon(length, height, a, b, g, rnd),
            CaveRock => Cave(length, height, a, b, g, rnd),
            SciFiPanels => Panels(length, height, a, b, g, rnd),
            Sandstone => Sand(length, height, a, b, g, rnd),
            Adobe => Plaster(length, height, a, b, g, rnd),
            _ => ([], [], []),
        };
    }

    // ---- the textures ----

    /// <summary>Courses of big blocks of different lengths; some corners chipped off, some blocks cracked.</summary>
    static (PathsD, PathsD, List<PathD>) Dungeon(double len, double h, double a, double b, double g, Random rnd)
    {
        var raised = new PathsD();
        var lines = new List<PathD>();
        for (double y = 0; y < h + b;)
        {
            double hh = b * (0.85 + 0.3 * rnd.NextDouble());
            for (double x = -a * rnd.NextDouble(); x < len + a;)
            {
                double w = a * (0.7 + 0.6 * rnd.NextDouble());
                double x0 = x + g / 2, y0 = y + g / 2, x1 = x + w - g / 2, y1 = y + hh - g / 2;
                var chips = new PathsD();
                foreach (var (cx, cy, sx, sy) in new[] { (x0, y0, 1, 1), (x1, y0, -1, 1), (x1, y1, -1, -1), (x0, y1, 1, -1) })
                {
                    if (rnd.NextDouble() > 0.3) continue;
                    double c = Math.Min(x1 - x0, y1 - y0) * (0.12 + 0.18 * rnd.NextDouble());
                    chips.Add([new(cx - sx * 0.1, cy - sy * 0.1), new(cx + sx * c * (0.6 + 0.8 * rnd.NextDouble()), cy - sy * 0.1), new(cx - sx * 0.1, cy + sy * c * (0.6 + 0.8 * rnd.NextDouble()))]);
                }
                raised.AddRange(Subtract([Rect(x0, y0, x1, y1)], chips));
                if (rnd.NextDouble() < 0.25) lines.Add(Crack(x0, y0, x1, y1, rnd));
                x += w;
            }
            y += hh;
        }
        return (raised, [], lines);
    }

    /// <summary>
    /// Layers of rock whose edges wander a long way up and down, split into lumps by leaning cracks;
    /// sharp-cornered, some with a crack across them and pits etched in.
    /// </summary>
    static (PathsD, PathsD, List<PathD>) Cave(double len, double h, double a, double b, double g, Random rnd)
    {
        double step = Math.Max(1, a / 4), x0 = -a, x1 = len + a;
        int n = (int)Math.Ceiling((x1 - x0) / step) + 1;
        // A layer's edge: a slow wave plus a little jitter.
        double[] Edge(double y)
        {
            double phase = rnd.NextDouble() * Math.PI * 2, wave = a * (1.5 + rnd.NextDouble() * 2), amp = b * 0.3;
            return [.. Enumerable.Range(0, n).Select(i => y + amp * Math.Sin((x0 + i * step) / wave * Math.PI * 2 + phase) + (rnd.NextDouble() - 0.5) * 0.3 * b)];
        }
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
        var etched = new PathsD();
        var lines = new List<PathD>();
        double ya = -b * 0.6;
        var lower = Edge(ya);
        while (ya < h + b)
        {
            double yb = ya + b * (0.7 + 0.6 * rnd.NextDouble());
            var upper = Edge(yb);
            double side = x0 - a * rnd.NextDouble();
            double lowX = side + (rnd.NextDouble() - 0.5) * a * 0.6, upX = side + (rnd.NextDouble() - 0.5) * a * 0.6;
            while (Math.Min(lowX, upX) < x1)
            {
                double next = Math.Max(lowX, upX) + a * (0.6 + 1.2 * rnd.NextDouble());
                double nLow = next + (rnd.NextDouble() - 0.5) * a * 0.6, nUp = next + (rnd.NextDouble() - 0.5) * a * 0.6;
                var rock = new PathD(Along(lower, lowX, nLow));
                // The crack up the right side bends once on its way.
                double mx = (nLow + nUp) / 2 + (rnd.NextDouble() - 0.5) * a * 0.25;
                rock.Add(new PointD(mx, (At(lower, nLow) + At(upper, nUp)) / 2));
                var top = Along(upper, upX, nUp);
                top.Reverse();
                rock.AddRange(top);
                var shrunk = Offset([rock], -g / 2);
                raised.AddRange(shrunk);
                var rb = Clipper.GetBounds(shrunk);
                if (rb.right > rb.left + 2 && rb.bottom > rb.top + 2)
                {
                    if (rnd.NextDouble() < 0.35) lines.Add(Crack(rb.left, rb.top, rb.right, rb.bottom, rnd));
                    int pits = rnd.NextDouble() < 0.4 ? 1 + rnd.Next(3) : 0;
                    for (int k = 0; k < pits; k++)
                    {
                        double r = Math.Min(g * 0.6, 0.25 + 0.3 * rnd.NextDouble());
                        var c = new PointD(rb.left + (rb.right - rb.left) * (0.2 + 0.6 * rnd.NextDouble()), rb.top + (rb.bottom - rb.top) * (0.2 + 0.6 * rnd.NextDouble()));
                        etched.AddRange(Intersect([Circle(c.x, c.y, r)], shrunk));
                    }
                }
                lowX = nLow;
                upX = nUp;
            }
            ya = yb;
            lower = upper;
        }
        return (raised, etched, lines);
    }

    /// <summary>
    /// Rows of metal panels, half, one, one and a half or two widths long, their corners cut; each
    /// plain, or with vents, an inset edge, bolts or a band of hazard stripes along its foot.
    /// </summary>
    static (PathsD, PathsD, List<PathD>) Panels(double len, double h, double a, double b, double g, Random rnd)
    {
        var raised = new PathsD();
        var etched = new PathsD();
        var lines = new List<PathD>();
        double[] widths = [0.5, 1, 1, 1, 1.5, 2];
        for (double y = 0; y < h + b; y += b)
            for (double x = -a * rnd.NextDouble(); x < len + a;)
            {
                double w = a * widths[rnd.Next(widths.Length)];
                double x0 = x + g / 2, y0 = y + g / 2, x1 = x + w - g / 2, y1 = y + b - g / 2, pw = x1 - x0, ph = y1 - y0;
                x += w;
                if (pw < 1 || ph < 1) continue;
                double c = Math.Min(pw, ph) * 0.12;
                var panel = Chamfered(x0, y0, x1, y1, c);
                raised.Add(panel);
                double pick = rnd.NextDouble();
                double inset = Math.Max(0.8, Math.Min(pw, ph) * 0.15);
                if (pick < 0.25 && ph > 4 && pw > 4)
                {
                    // Vents: short slots across the middle of the panel.
                    double slot = Math.Max(0.4, g), pitch = slot * 2.4;
                    int count = Math.Clamp((int)((ph * 0.5) / pitch), 2, 8);
                    double sx0 = x0 + pw * 0.25, sx1 = x1 - pw * 0.25, sy = (y0 + y1) / 2 - pitch * (count - 1) / 2;
                    for (int k = 0; k < count; k++) etched.Add(Rect(sx0, sy + k * pitch - slot / 2, sx1, sy + k * pitch + slot / 2));
                }
                else if (pick < 0.45 && Math.Min(pw, ph) > 3 * inset)
                    lines.Add(Loop(Chamfered(x0 + inset, y0 + inset, x1 - inset, y1 - inset, c * 0.6)));
                else if (pick < 0.65 && Math.Min(pw, ph) > 3)
                {
                    double r = Math.Clamp(Math.Min(pw, ph) * 0.03, 0.2, 0.4), o = Math.Max(0.8, c + r * 2);
                    foreach (var (bx, by) in new[] { (x0 + o, y0 + o), (x1 - o, y0 + o), (x1 - o, y1 - o), (x0 + o, y1 - o) })
                        lines.Add(Loop(Circle(bx, by, r)));
                }
                else if (pick < 0.72 && ph > 4)
                {
                    // Hazard stripes in a band along the foot of the panel.
                    double band = Math.Min(ph * 0.25, 3), sw = Math.Max(0.6, band * 0.4);
                    var stripes = new PathsD();
                    for (double sx = x0 - band; sx < x1 + band; sx += sw * 2)
                        stripes.Add([new(sx, y0), new(sx + sw, y0), new(sx + sw + band, y0 + band), new(sx + band, y0 + band)]);
                    etched.AddRange(Intersect(stripes, [Rect(x0 + c, y0, x1 - c, y0 + band)]));
                    lines.Add([new(x0, y0 + band), new(x1, y0 + band)]);
                }
            }
        return (raised, etched, lines);
    }

    /// <summary>Courses of long sandstone blocks with rounded corners, wind lines along them and the odd pit.</summary>
    static (PathsD, PathsD, List<PathD>) Sand(double len, double h, double a, double b, double g, Random rnd)
    {
        var raised = new PathsD();
        var etched = new PathsD();
        var lines = new List<PathD>();
        for (double y = 0; y < h + b;)
        {
            double hh = b * (0.85 + 0.3 * rnd.NextDouble());
            for (double x = -a * rnd.NextDouble(); x < len + a;)
            {
                double w = a * (0.75 + 0.8 * rnd.NextDouble());
                double x0 = x + g / 2, y0 = y + g / 2, x1 = x + w - g / 2, y1 = y + hh - g / 2;
                x += w;
                if (x1 - x0 < 1 || y1 - y0 < 1) continue;
                double r = Math.Min(x1 - x0, y1 - y0) * 0.18;
                var block = Offset(Offset([Rect(x0, y0, x1, y1)], -r), r, round: true);
                raised.AddRange(block);
                // One to three wind-worn lines along the block.
                int count = 1 + rnd.Next(3);
                for (int k = 0; k < count; k++)
                {
                    double ly = y0 + (y1 - y0) * (0.2 + 0.6 * rnd.NextDouble());
                    double la = x0 + (x1 - x0) * 0.3 * rnd.NextDouble(), lb = x1 - (x1 - x0) * 0.3 * rnd.NextDouble();
                    double amp = Math.Min(0.3, (y1 - y0) * 0.05), wave = 4 + 6 * rnd.NextDouble(), phase = rnd.NextDouble() * 6.28;
                    var line = new PathD();
                    for (double t = la; t <= lb; t += 0.6) line.Add(new PointD(t, ly + amp * Math.Sin(t / wave * 6.28 + phase)));
                    if (line.Count >= 2) lines.Add(line);
                }
                if (rnd.NextDouble() < 0.3)
                    for (int k = 0, pits = 1 + rnd.Next(3); k < pits; k++)
                    {
                        double pr = 0.2 + 0.25 * rnd.NextDouble();
                        etched.Add(Circle(x0 + (x1 - x0) * (0.15 + 0.7 * rnd.NextDouble()), y0 + (y1 - y0) * (0.2 + 0.6 * rnd.NextDouble()), pr));
                    }
            }
            y += hh;
        }
        return (raised, etched, lines);
    }

    /// <summary>
    /// Plaster over the whole face, with ragged patches fallen away where bricks show (brick height
    /// <paramref name="b"/>, two and a half times as long), and a few hairline cracks in the plaster.
    /// </summary>
    static (PathsD, PathsD, List<PathD>) Plaster(double len, double h, double a, double b, double g, Random rnd)
    {
        var face = Rect(-1, -1, len + 1, h + 1);
        int count = Math.Max(1, (int)Math.Round(len * h / (a * a * 5)));
        var patches = new PathsD();
        for (int i = 0; i < count; i++)
        {
            double cx = len * rnd.NextDouble(), cy = h * (0.1 + 0.8 * rnd.NextDouble()), r = a / 2 * (0.6 + 0.6 * rnd.NextDouble());
            var blob = new PathD();
            for (int k = 0; k < 10; k++)
            {
                double t = k / 10.0 * Math.PI * 2, rr = r * (0.65 + 0.5 * rnd.NextDouble());
                blob.Add(new PointD(cx + rr * Math.Cos(t) * 1.3, cy + rr * Math.Sin(t) * 0.8));
            }
            patches.Add(blob);
        }
        var holes = Union(patches);
        var bricks = new PathsD();
        double bl = b * 2.5;
        int rows = (int)Math.Ceiling(h / b) + 2;
        for (int row = -1; row < rows; row++)
        {
            double y = row * b, off = row % 2 != 0 ? bl / 2 : 0;
            for (double x = -off - bl; x < len + bl; x += bl) bricks.Add(Rect(x + g / 2, y + g / 2, x + bl - g / 2, y + b - g / 2));
        }
        var raised = Subtract([face], holes);
        raised.AddRange(Intersect(bricks, Offset(holes, -g)));
        var lines = new List<PathD>();
        int cracks = Math.Max(1, (int)Math.Round(len * h / (a * a * 4)));
        for (int i = 0; i < cracks; i++)
        {
            double cx = len * rnd.NextDouble(), cy = h * rnd.NextDouble(), s = a * (0.3 + 0.4 * rnd.NextDouble());
            lines.Add(Crack(cx - s / 2, cy - s / 2, cx + s / 2, cy + s / 2, rnd));
        }
        // Cracks stop at the patches' edges.
        return (raised, [], Clip(lines, Subtract([face], holes)));
    }

    // ---- helpers ----

    /// <summary>A crack wandering down across a box from its top edge, three or four bends long.</summary>
    static PathD Crack(double x0, double y0, double x1, double y1, Random rnd)
    {
        double w = x1 - x0, hh = y1 - y0;
        var p = new PathD { new(x0 + w * (0.2 + 0.6 * rnd.NextDouble()), y1) };
        int steps = 3 + rnd.Next(2);
        double depth = hh * (0.4 + 0.5 * rnd.NextDouble());
        for (int i = 1; i <= steps; i++)
        {
            var last = p[^1];
            p.Add(new PointD(Math.Clamp(last.x + (rnd.NextDouble() - 0.5) * w * 0.35, x0, x1), y1 - depth * i / steps));
        }
        return p;
    }

    static PathD Chamfered(double x0, double y0, double x1, double y1, double c) =>
        [new(x0 + c, y0), new(x1 - c, y0), new(x1, y0 + c), new(x1, y1 - c), new(x1 - c, y1), new(x0 + c, y1), new(x0, y1 - c), new(x0, y0 + c)];

    static PathD Rect(double x0, double y0, double x1, double y1) => [new(x0, y0), new(x1, y0), new(x1, y1), new(x0, y1)];

    static PathD Circle(double cx, double cy, double r)
    {
        int n = Math.Clamp((int)Math.Ceiling(r * 12), 10, 48);
        var p = new PathD(n);
        for (int i = 0; i < n; i++) p.Add(new PointD(cx + r * Math.Cos(2 * Math.PI * i / n), cy + r * Math.Sin(2 * Math.PI * i / n)));
        return p;
    }

    static PathD Loop(PathD p) => p.Count == 0 ? p : [.. p, p[0]];

    const int Precision = 4;
    static PathsD Union(PathsD a) => a.Count == 0 ? [] : Clipper.BooleanOp(ClipType.Union, a, null, FillRule.NonZero, Precision);
    static PathsD Subtract(PathsD a, PathsD b) => a.Count == 0 ? [] : b.Count == 0 ? a : Clipper.Difference(a, b, FillRule.NonZero, Precision);
    static PathsD Intersect(PathsD a, PathsD b) => a.Count == 0 || b.Count == 0 ? [] : Clipper.Intersect(a, b, FillRule.NonZero, Precision);
    static PathsD Offset(PathsD a, double by, bool round = false) =>
        a.Count == 0 ? a : Clipper.InflatePaths(Union(a), by, round ? JoinType.Round : JoinType.Miter, EndType.Polygon, 4, Precision);

    static List<PathD> Clip(List<PathD> lines, PathsD region)
    {
        var subject = new PathsD(lines.Where(l => l.Count >= 2));
        if (subject.Count == 0 || region.Count == 0) return [];
        var c = new ClipperD(Precision);
        c.AddOpenSubject(subject);
        c.AddClip(region);
        var closed = new PathsD();
        var open = new PathsD();
        c.Execute(ClipType.Intersection, FillRule.NonZero, closed, open);
        return [.. open.Where(p => p.Count >= 2)];
    }
}
