using Clipper2Lib;
using PhotonCanvas.Addon.FloorTiles;
using PhotonCanvas.Addon.Walls;

namespace PhotonCanvas.Addon.Bases;

/// <summary>
/// A base texture: its name, the group it's listed in, the names of its two sizes (null = not used)
/// and their defaults, whether it has a groove, a random layout and stones that can crack.
/// <paramref name="Floor"/> names the floor tile maker's texture it is, when it is one of those (so
/// bases match the floor they stand on).
/// </summary>
public sealed record GroundPreset(string Name, string Group, string? SizeALabel, double SizeA, string? SizeBLabel, double SizeB, bool UsesGroove, double Groove, bool Random, bool Stone, string About, string? Floor = null);

/// <summary>
/// What a ground texture is made of: raised pieces (what's between them is etched), areas etched as
/// they are, areas outlined (a groove round them, or a scored line), and lines scored as they are.
/// </summary>
public sealed record Ground(PathsD Raised, PathsD Etched, PathsD Outlined, List<PathD> Lines)
{
    public static readonly Ground None = new([], [], [], []);
    public static Ground From(TextureParts t) => new(t.Raised, t.Etched, [], t.Lines);
}

/// <summary>
/// The textures etched on a base's top: ground (cracked earth, rocks, gravel, grass, sand, swamp,
/// forest floor, lava, ice), the floor tile maker's paving (so bases match the floors) and sci-fi
/// metal, laid out from the corner of the box they're made over.
/// </summary>
public static class GroundTextures
{
    public const string Plain = "Plain";
    public const string GroundGroup = "Ground", PavingGroup = "Paving", MetalGroup = "Sci-fi";

    public static readonly IReadOnlyList<GroundPreset> Presets =
    [
        new("Cracked earth", GroundGroup, "Plate size", 4, null, 0, true, 0.5, true, false,
            "Dry, cracked mud: plates of earth of many sides with cracks of different widths between them, and fine cracks running into them."),
        new("Rocky ground", GroundGroup, "Rock size", 6, "Rocks %", 30, true, 0.5, true, false,
            "Rocks and boulders with a groove round each one and a facet or two, and gravel scattered on the ground between."),
        new("Gravel", GroundGroup, "Stone size", 1.4, "Cover %", 35, false, 0, true, false,
            "Small stones of many shapes, etched in: scree, rubble or a gravel path."),
        new("Grass", GroundGroup, "Tuft spacing", 3.5, null, 0, false, 0, true, false,
            "Tufts of grass, each a fan of blades scored from its root, with a little earth showing between."),
        new("Sand ripples", GroundGroup, "Ripple spacing", 1.6, null, 0, false, 0, true, false,
            "Wind ripples in sand, in broken wavy lines, with a pebble here and there: deserts and beaches."),
        new("Swamp", GroundGroup, "Puddle size", 9, "Water %", 30, true, 0.5, true, false,
            "Puddles of standing water with a ripple in each, and reeds growing round them."),
        new("Forest floor", GroundGroup, "Leaf size", 2.6, "Cover %", 35, true, 0.4, true, false,
            "Fallen leaves, roots and twigs."),
        new("Lava cracks", GroundGroup, "Plate size", 6, null, 0, true, 1.2, true, false,
            "Cooled lava: rounded plates with wide channels between them, to paint glowing."),
        new("Cracked ice", GroundGroup, "Crack spacing", 7, null, 0, false, 0, true, false,
            "Smooth ice with long branching cracks scored across it: frozen lakes and snowfields."),
        new("Dungeon flagstones", PavingGroup, "Split %", 50, "Slab size", 8, true, 0.5, true, true,
            "Square slabs, some split into halves or quarters, with chipped corners: the floor tile maker's dungeon floor.", "Dungeon flagstones"),
        new("Flagstones", PavingGroup, "Stone size", 5, null, 0, true, 0.5, true, true,
            "Irregular paving stones of many sides fitted together (crazy paving).", "Flagstones"),
        new("Cobblestones", PavingGroup, "Stone size", 2.8, null, 0, true, 0.45, true, true,
            "Small rounded cobbles: streets, yards and market squares.", "Cobblestones"),
        new("Floorboards", PavingGroup, "Board width", 3, "Board length", 30, true, 0.4, true, false,
            "Boards with staggered joints, grain and nail holes: decks, taverns and bridges.", "Floorboards"),
        new("Stone blocks", PavingGroup, "Block length", 7, "Row width", 5, true, 0.5, true, true,
            "Cut stone in rows of different widths, blocks of different lengths.", "Stone blocks"),
        new("Brick paving", PavingGroup, "Brick length", 5, "Brick width", 2.5, true, 0.45, false, true,
            "Bricks in rows, each row moved along half a brick.", "Brick paving"),
        new("Herringbone brick", PavingGroup, "Brick width", 1.6, null, 0, true, 0.35, false, true,
            "Bricks laid in a zigzag, as in old courtyards and halls.", "Herringbone brick"),
        new("Square tiles", PavingGroup, "Tile size", 5, null, 0, true, 0.45, false, true,
            "Square tiles in straight rows.", "Square tiles"),
        new("Hexagons", MetalGroup, "Hexagon size", 4, null, 0, true, 0.45, false, true,
            "Hexagonal tiles, for temples, labs and sci-fi floors.", "Hexagons"),
        new("Metal deck plates", MetalGroup, "Plate width", 12, "Plate length", 8, true, 0.4, false, false,
            "Riveted plates, for ships, bunkers and sci-fi decks.", "Metal deck plates"),
        new("Tread plate", MetalGroup, "Pattern size", 2.5, null, 0, false, 0, false, false,
            "Non-slip metal plate: little bars etched in, leaning one way and the other.", "Tread plate"),
        new("Grating", MetalGroup, "Slot width", 0.8, "Panel size", 10, true, 0.5, false, false,
            "Panels of metal grating, slots running one way on one panel and the other way on the next: walkways and vents."),
        new(Plain, PavingGroup, null, 0, null, 0, false, 0, false, false, "No texture: smooth tops to sculpt or flock yourself."),
    ];

    public static GroundPreset? Preset(string name) => Presets.FirstOrDefault(p => p.Name == name);

    /// <summary>The texture over the box <paramref name="w"/> × <paramref name="h"/> mm from 0, 0 (y up), with a little beyond each edge.</summary>
    public static Ground Make(BaseSpec s, double w, double h, int seed)
    {
        double a = Math.Max(0.3, s.SizeA), b = Math.Max(0.3, s.SizeB), g = Math.Clamp(s.GrooveMm, 0.05, 3);
        var p = Preset(s.Texture);
        if (p is null && s.Custom is null) return Ground.None;
        if (p is null || p.Floor is not null)
        {
            // The floor tile maker's (or your own, made in the wall maker), as they'd be on a floor.
            var tile = new TileSpec
            {
                Texture = p?.Floor ?? s.Custom!.Name,
                Custom = p is null ? s.Custom : null,
                // The dungeon slabs follow a grid: the slab size is its square. Other textures only use the square for the margin.
                SquareMm = p?.Floor == "Dungeon flagstones" ? Math.Clamp(s.SizeB, 2, 100) : Math.Max(5, Math.Min(w, h) / 4),
                SizeA = p?.Floor == "Dungeon flagstones" ? Math.Clamp(s.SizeA, 0, 100) : a,
                SizeB = b,
                GrooveMm = g,
                CracksPct = p is { Stone: true } ? s.CracksPct : 0,
            };
            return Ground.From(FloorTextures.Make(tile, w, h, seed));
        }
        var rnd = new Random(seed);
        var r = new Box(-2, -2, w + 2, h + 2);
        return s.Texture switch
        {
            "Cracked earth" => Cracked(r, a, Math.Min(g, a / 3), rnd, rounded: false),
            "Lava cracks" => Cracked(r, a, Math.Min(g, a / 2.5), rnd, rounded: true),
            "Rocky ground" => Rocky(r, a, Math.Clamp(s.SizeB, 0, 90) / 100, rnd),
            "Gravel" => Gravel(r, a, Math.Clamp(s.SizeB, 0, 90) / 100, rnd),
            "Grass" => Grass(r, a, rnd),
            "Sand ripples" => Sand(r, a, rnd),
            "Swamp" => Swamp(r, a, Math.Clamp(s.SizeB, 0, 80) / 100, rnd),
            "Forest floor" => Forest(r, a, Math.Clamp(s.SizeB, 0, 90) / 100, rnd),
            "Cracked ice" => Ice(r, a, rnd),
            "Grating" => Grating(r, a, b, Math.Min(g, b / 4)),
            _ => Ground.None,
        };
    }

    /// <summary>The rectangle a texture is made over.</summary>
    readonly record struct Box(double X0, double Y0, double X1, double Y1)
    {
        public double W => X1 - X0;
        public double H => Y1 - Y0;
        public PointD Any(Random rnd) => new(X0 + rnd.NextDouble() * W, Y0 + rnd.NextDouble() * H);
    }

    /// <summary>The cells round points scattered on a jiggled grid of <paramref name="size"/> (each holds the ground nearer its point than any other).</summary>
    static List<(PointD Site, PathD Cell)> Cells(Box r, double size, double jitter, Random rnd)
    {
        int nx = (int)Math.Ceiling(r.W / size) + 3, ny = (int)Math.Ceiling(r.H / size) + 3;
        var sites = new PointD[nx, ny];
        for (int i = 0; i < nx; i++)
            for (int j = 0; j < ny; j++)
            {
                double ox = j % 2 == 1 ? size / 2 : 0;
                sites[i, j] = new(r.X0 + (i - 1) * size + ox + (rnd.NextDouble() - 0.5) * 2 * jitter * size,
                                  r.Y0 + (j - 1) * size + (rnd.NextDouble() - 0.5) * 2 * jitter * size);
            }
        var cells = new List<(PointD, PathD)>();
        for (int i = 1; i < nx - 1; i++)
            for (int j = 1; j < ny - 1; j++)
            {
                var c = sites[i, j];
                PathD cell = Geo.Rect(c.x - 3 * size, c.y - 3 * size, c.x + 3 * size, c.y + 3 * size);
                for (int di = -2; di <= 2 && cell.Count >= 3; di++)
                    for (int dj = -2; dj <= 2 && cell.Count >= 3; dj++)
                    {
                        if (di == 0 && dj == 0) continue;
                        int ii = i + di, jj = j + dj;
                        if (ii < 0 || jj < 0 || ii >= nx || jj >= ny) continue;
                        cell = HalfPlane(cell, c, sites[ii, jj]);
                    }
                if (cell.Count >= 3) cells.Add((c, cell));
            }
        return cells;
    }

    /// <summary>The part of a convex polygon nearer <paramref name="keep"/> than <paramref name="other"/>.</summary>
    static PathD HalfPlane(PathD poly, PointD keep, PointD other)
    {
        double mx = (keep.x + other.x) / 2, my = (keep.y + other.y) / 2, nx = other.x - keep.x, ny = other.y - keep.y;
        double Side(PointD p) => (p.x - mx) * nx + (p.y - my) * ny;
        var res = new PathD(poly.Count + 1);
        for (int k = 0; k < poly.Count; k++)
        {
            var a = poly[k];
            var b = poly[(k + 1) % poly.Count];
            double sa = Side(a), sb = Side(b);
            if (sa <= 0) res.Add(a);
            if (sa < 0 && sb > 0 || sa > 0 && sb < 0)
            {
                double t = sa / (sa - sb);
                res.Add(new PointD(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t));
            }
        }
        return res;
    }

    /// <summary>Each side of a polygon broken into a few pieces whose joints wander off the straight line (a rough, natural edge).</summary>
    static PathD Rough(PathD p, double amount, Random rnd)
    {
        var res = new PathD();
        for (int k = 0; k < p.Count; k++)
        {
            var a = p[k];
            var b = p[(k + 1) % p.Count];
            double dx = b.x - a.x, dy = b.y - a.y, len = Math.Sqrt(dx * dx + dy * dy);
            res.Add(a);
            int pieces = Math.Clamp((int)(len / Math.Max(0.4, amount * 4)), 1, 6);
            for (int i = 1; i < pieces; i++)
            {
                double t = (double)i / pieces, off = (rnd.NextDouble() - 0.5) * 2 * amount;
                res.Add(new PointD(a.x + dx * t - dy / Math.Max(1e-9, len) * off, a.y + dy * t + dx / Math.Max(1e-9, len) * off));
            }
        }
        return res;
    }

    static PointD Centre(PathD p) => new(p.Average(q => q.x), p.Average(q => q.y));

    /// <summary>A rough blob of about <paramref name="r"/> radius: a few lumps round its edge.</summary>
    static PathD Blob(PointD c, double r, Random rnd, int lumps = 3, double lumpiness = 0.22)
    {
        var phase = Enumerable.Range(0, lumps).Select(_ => rnd.NextDouble() * 2 * Math.PI).ToArray();
        int n = Math.Clamp(Geo.Segments(r, 0.03), 12, 64);
        var p = new PathD(n);
        for (int i = 0; i < n; i++)
        {
            double t = 2 * Math.PI * i / n, k = 1;
            for (int l = 0; l < lumps; l++) k += lumpiness / (l + 1) * Math.Sin((l + 2) * t + phase[l]);
            p.Add(new PointD(c.x + r * k * Math.Cos(t), c.y + r * k * Math.Sin(t)));
        }
        return p;
    }

    /// <summary>A stone of a few sides, about <paramref name="r"/> radius, turned at random.</summary>
    static PathD Stone(PointD c, double r, Random rnd)
    {
        int n = 5 + rnd.Next(4);
        double turn = rnd.NextDouble() * 2 * Math.PI;
        var p = new PathD(n);
        for (int i = 0; i < n; i++)
        {
            double t = turn + 2 * Math.PI * (i + (rnd.NextDouble() - 0.5) * 0.5) / n, k = r * (0.7 + 0.3 * rnd.NextDouble());
            p.Add(new PointD(c.x + k * Math.Cos(t), c.y + k * Math.Sin(t)));
        }
        return p;
    }

    /// <summary>
    /// Plates of earth (or lava) with cracks between: the cells, edges roughened and shrunk by a groove
    /// that varies from crack to crack; fine cracks scored from a corner into some of them.
    /// </summary>
    static Ground Cracked(Box r, double size, double g, Random rnd, bool rounded)
    {
        var raised = new PathsD();
        var lines = new List<PathD>();
        foreach (var (_, cell) in Cells(r, size, 0.42, rnd))
        {
            var rough = Rough(cell, rounded ? size * 0.05 : size * 0.07, rnd);
            double shrink = g / 2 * (0.6 + 0.8 * rnd.NextDouble());
            var plate = rounded
                ? Geo.Offset(Geo.Offset([rough], -(shrink + size * 0.12), round: true), size * 0.12, round: true)
                : Geo.Offset([rough], -shrink);
            raised.AddRange(plate);
            if (!rounded && plate.Count > 0 && rnd.NextDouble() < 0.4)
            {
                // A fine crack from a corner part way to the middle, in two kinked steps.
                var corner = cell[rnd.Next(cell.Count)];
                var mid = Centre(cell);
                double reach = 0.4 + 0.3 * rnd.NextDouble();
                var kink = new PointD(corner.x + (mid.x - corner.x) * reach / 2 + (rnd.NextDouble() - 0.5) * size * 0.15, corner.y + (mid.y - corner.y) * reach / 2 + (rnd.NextDouble() - 0.5) * size * 0.15);
                lines.AddRange(Geo.ClipLines([[corner, kink, new PointD(corner.x + (mid.x - corner.x) * reach, corner.y + (mid.y - corner.y) * reach)]], plate));
            }
        }
        return new(raised, [], [], lines);
    }

    /// <summary>
    /// Rocks of a few sides (the bigger ones first, none overlapping), each outlined with a groove and
    /// given a facet line or two, with gravel scattered between them.
    /// </summary>
    static Ground Rocky(Box r, double size, double cover, Random rnd)
    {
        var rocks = new List<(PointD C, double R)>();
        var outlined = new PathsD();
        var lines = new List<PathD>();
        double target = cover * r.W * r.H, covered = 0;
        for (int tries = 0; tries < 4000 && covered < target; tries++)
        {
            double rr = size / 2 * (tries < 200 ? 0.7 + 0.6 * rnd.NextDouble() : 0.35 + 0.4 * rnd.NextDouble());
            var c = r.Any(rnd);
            if (rocks.Any(o => Math.Sqrt((o.C.x - c.x) * (o.C.x - c.x) + (o.C.y - c.y) * (o.C.y - c.y)) < o.R + rr + 0.6)) continue;
            rocks.Add((c, rr));
            var stone = Stone(c, rr, rnd);
            outlined.Add(stone);
            covered += Math.PI * rr * rr * 0.8;
            // A facet: from one corner to one about opposite, bent near the middle.
            if (rr > 1)
            {
                int i = rnd.Next(stone.Count), j = (i + stone.Count / 2) % stone.Count;
                var bend = new PointD(c.x + (rnd.NextDouble() - 0.5) * rr * 0.5, c.y + (rnd.NextDouble() - 0.5) * rr * 0.5);
                lines.AddRange(Geo.ClipLines([[stone[i], bend, stone[j]]], Geo.Offset([stone], -0.2)));
            }
        }
        var gravel = new PathsD();
        int dots = (int)(r.W * r.H / 6);
        var rocksArea = Geo.Offset(outlined, 0.8);
        for (int k = 0; k < dots; k++)
        {
            var c = r.Any(rnd);
            double rr = 0.15 + 0.25 * rnd.NextDouble();
            gravel.Add(Stone(c, rr, rnd));
        }
        return new([], Geo.Subtract(Geo.Union(gravel), rocksArea), outlined, lines);
    }

    /// <summary>Small stones scattered until they cover about <paramref name="cover"/> of the ground, etched in.</summary>
    static Ground Gravel(Box r, double size, double cover, Random rnd)
    {
        var stones = new PathsD();
        int n = (int)(cover * r.W * r.H / (Math.PI * size * size / 4 * 0.75));
        for (int k = 0; k < Math.Min(n, 20000); k++)
            stones.Add(Stone(r.Any(rnd), size / 2 * (0.5 + 0.7 * rnd.NextDouble()), rnd));
        return new([], Geo.Union(stones), [], []);
    }

    /// <summary>A tuft: blades fanned out from about one point, each bending a little.</summary>
    static IEnumerable<PathD> Tuft(PointD c, double len, Random rnd)
    {
        int blades = 5 + rnd.Next(5);
        double lean = rnd.NextDouble() * 2 * Math.PI;
        for (int i = 0; i < blades; i++)
        {
            double t = lean + 2 * Math.PI * (i + (rnd.NextDouble() - 0.5) * 0.6) / blades, l = len * (0.55 + 0.6 * rnd.NextDouble());
            double bend = (rnd.NextDouble() - 0.5) * 0.8;
            var start = new PointD(c.x + Math.Cos(t) * len * 0.12, c.y + Math.Sin(t) * len * 0.12);
            var mid = new PointD(c.x + Math.Cos(t + bend * 0.5) * l * 0.55, c.y + Math.Sin(t + bend * 0.5) * l * 0.55);
            var tip = new PointD(c.x + Math.Cos(t + bend) * l, c.y + Math.Sin(t + bend) * l);
            yield return [start, mid, tip];
        }
    }

    static Ground Grass(Box r, double spacing, Random rnd)
    {
        var lines = new List<PathD>();
        var earth = new PathsD();
        for (double x = r.X0; x < r.X1; x += spacing)
            for (double y = r.Y0; y < r.Y1; y += spacing)
            {
                var c = new PointD(x + rnd.NextDouble() * spacing, y + rnd.NextDouble() * spacing);
                if (rnd.NextDouble() < 0.15) { earth.Add(Stone(c, spacing * (0.15 + 0.15 * rnd.NextDouble()), rnd)); continue; }
                lines.AddRange(Tuft(c, spacing * 0.75, rnd));
            }
        return new([], Geo.Union(earth), [], lines);
    }

    /// <summary>Wind ripples: wavy lines a spacing apart, broken into lengths, each wandering a little; a pebble now and then.</summary>
    static Ground Sand(Box r, double spacing, Random rnd)
    {
        var lines = new List<PathD>();
        double wave = spacing * (5 + 3 * rnd.NextDouble()), amp = spacing * 0.35, phase = rnd.NextDouble() * 6.28, drift = (rnd.NextDouble() - 0.5) * 0.3;
        for (double y = r.Y0; y < r.Y1 + spacing; y += spacing * (0.8 + 0.4 * rnd.NextDouble()))
        {
            for (double x = r.X0 - rnd.NextDouble() * 8; x < r.X1;)
            {
                double len = spacing * (4 + 8 * rnd.NextDouble()), shift = (rnd.NextDouble() - 0.5) * spacing * 0.3;
                var line = new PathD();
                for (double t = x; t <= x + len; t += Math.Max(0.2, spacing / 4))
                    line.Add(new PointD(t, y + shift + drift * (t - r.X0) + amp * Math.Sin(t / wave * 6.28 + phase + y * 0.7)));
                if (line.Count >= 2) lines.Add(line);
                x += len + spacing * (0.3 + 0.8 * rnd.NextDouble());
            }
        }
        var pebbles = new PathsD();
        int n = (int)(r.W * r.H / 120);
        for (int k = 0; k < n; k++) pebbles.Add(Stone(r.Any(rnd), 0.3 + 0.4 * rnd.NextDouble(), rnd));
        var pebbleArea = Geo.Union(pebbles);
        return new([], pebbleArea, [], Geo.ClipLines(lines, Geo.Subtract([Geo.Rect(r.X0 - 20, r.Y0 - 20, r.X1 + 20, r.Y1 + 20)], Geo.Offset(pebbleArea, 0.3))));
    }

    /// <summary>Puddles (outlined, a ripple scored in each), with tufts of reeds round them.</summary>
    static Ground Swamp(Box r, double size, double cover, Random rnd)
    {
        var pools = new PathsD();
        var lines = new List<PathD>();
        double target = cover * r.W * r.H, covered = 0;
        var placed = new List<(PointD C, double R)>();
        for (int tries = 0; tries < 600 && covered < target; tries++)
        {
            double rr = size / 2 * (0.5 + 0.7 * rnd.NextDouble());
            var c = r.Any(rnd);
            if (placed.Any(o => Math.Sqrt((o.C.x - c.x) * (o.C.x - c.x) + (o.C.y - c.y) * (o.C.y - c.y)) < (o.R + rr) * 1.25 + 0.8)) continue;
            placed.Add((c, rr));
            var pool = Blob(c, rr, rnd, lumps: 3, lumpiness: 0.3);
            pools.Add(pool);
            covered += Math.PI * rr * rr;
            if (rr > 1.5) lines.Add(Geo.Loop(Blob(new PointD(c.x + (rnd.NextDouble() - 0.5) * rr * 0.3, c.y + (rnd.NextDouble() - 0.5) * rr * 0.3), rr * 0.45, rnd, lumps: 2, lumpiness: 0.15)));
        }
        // Reeds on the ground, thickest near the water.
        var reeds = new List<PathD>();
        var water = Geo.Offset(pools, 0.6);
        double step = Math.Max(2.5, size * 0.45);
        for (double x = r.X0; x < r.X1; x += step)
            for (double y = r.Y0; y < r.Y1; y += step)
            {
                var c = new PointD(x + rnd.NextDouble() * step, y + rnd.NextDouble() * step);
                double near = placed.Count == 0 ? 99 : placed.Min(o => Math.Sqrt((o.C.x - c.x) * (o.C.x - c.x) + (o.C.y - c.y) * (o.C.y - c.y)) - o.R);
                if (rnd.NextDouble() < (near < size * 0.4 ? 0.8 : 0.25)) reeds.AddRange(Tuft(c, step * 0.7, rnd));
            }
        lines.AddRange(Geo.ClipLines(reeds, Geo.Subtract([Geo.Rect(r.X0 - 20, r.Y0 - 20, r.X1 + 20, r.Y1 + 20)], water)));
        return new([], [], pools, lines);
    }

    /// <summary>A leaf: two arcs meeting at its ends, <paramref name="len"/> long, with its midrib.</summary>
    static (PathD Leaf, PathD Rib) Leaf(PointD c, double len, double turn)
    {
        double half = len / 2, wid = len * 0.22;
        var pts = new PathD();
        const int n = 10;
        for (int i = 0; i <= n; i++) { double t = -1 + 2.0 * i / n; pts.Add(new PointD(t * half, wid * (1 - t * t))); }
        for (int i = n - 1; i > 0; i--) { double t = -1 + 2.0 * i / n; pts.Add(new PointD(t * half, -wid * (1 - t * t))); }
        double cs = Math.Cos(turn), sn = Math.Sin(turn);
        PointD Put(PointD p) => new(c.x + p.x * cs - p.y * sn, c.y + p.x * sn + p.y * cs);
        return (Geo.Map(pts, Put), [Put(new PointD(-half * 0.85, 0)), Put(new PointD(half * 1.15, 0))]);
    }

    static Ground Forest(Box r, double size, double cover, Random rnd)
    {
        var outlined = new PathsD();
        var lines = new List<PathD>();
        // A root or two snaking across, tapering.
        int roots = 1 + rnd.Next(2);
        for (int k = 0; k < roots; k++)
        {
            var p = new PointD(r.X0, r.Y0 + r.H * (0.2 + 0.6 * rnd.NextDouble()));
            double dir = (rnd.NextDouble() - 0.5) * 0.8, width = Math.Max(0.8, size * 0.6);
            if (rnd.NextDouble() < 0.5) { p = new PointD(r.X0 + r.W * (0.2 + 0.6 * rnd.NextDouble()), r.Y0); dir += Math.PI / 2; }
            var spine = new List<PointD> { p };
            for (double l = 0; l < Math.Max(r.W, r.H) * 1.2; l += 1)
            {
                dir += (rnd.NextDouble() - 0.5) * 0.25;
                p = new PointD(p.x + Math.Cos(dir), p.y + Math.Sin(dir));
                spine.Add(p);
            }
            for (int i = 0; i + 1 < spine.Count; i++)
            {
                double w = width * (1 - 0.6 * i / spine.Count);
                outlined.AddRange(Clipper.InflatePaths([[spine[i], spine[i + 1]]], w / 2, JoinType.Round, EndType.Round, 2, 4));
            }
        }
        outlined = Geo.Union(outlined);
        var rootArea = Geo.Offset(outlined, 0.5);
        // Leaves, not on the roots and not on each other.
        var leaves = new PathsD();
        int n = (int)(cover * r.W * r.H / (size * size * 0.3));
        for (int k = 0; k < Math.Min(n, 4000); k++)
        {
            var (leaf, rib) = Leaf(r.Any(rnd), size * (0.7 + 0.6 * rnd.NextDouble()), rnd.NextDouble() * 2 * Math.PI);
            var grown = Geo.Offset([leaf], 0.25);
            if (Geo.Area(Geo.Intersect(grown, Geo.Union(rootArea, leaves))) > 1e-4) continue;
            leaves.AddRange(grown);
            outlined.Add(leaf);
            lines.Add(rib);
        }
        // Twigs.
        int twigs = (int)(r.W * r.H / 150);
        for (int k = 0; k < twigs; k++)
        {
            var a = r.Any(rnd);
            double t = rnd.NextDouble() * 2 * Math.PI, l = size * (1 + rnd.NextDouble());
            var b = new PointD(a.x + Math.Cos(t) * l, a.y + Math.Sin(t) * l);
            var fork = new PointD(a.x + Math.Cos(t) * l * 0.5, a.y + Math.Sin(t) * l * 0.5);
            lines.AddRange(Geo.ClipLines([[a, b], [fork, new PointD(fork.x + Math.Cos(t + 0.6) * l * 0.35, fork.y + Math.Sin(t + 0.6) * l * 0.35)]],
                Geo.Subtract([Geo.Rect(r.X0 - 20, r.Y0 - 20, r.X1 + 20, r.Y1 + 20)], Geo.Union(rootArea, leaves))));
        }
        return new([], [], outlined, lines);
    }

    /// <summary>Long cracks wandering across, splitting off a branch now and then.</summary>
    static Ground Ice(Box r, double spacing, Random rnd)
    {
        var lines = new List<PathD>();
        int n = Math.Max(1, (int)(r.W * r.H / (spacing * spacing * 3)));
        void Crack(PointD p, double dir, double len, int depth)
        {
            var line = new PathD { p };
            for (double l = 0; l < len; l += 0.8)
            {
                dir += (rnd.NextDouble() - 0.5) * 0.5;
                p = new PointD(p.x + Math.Cos(dir) * 0.8, p.y + Math.Sin(dir) * 0.8);
                line.Add(p);
                if (depth < 2 && rnd.NextDouble() < 0.06) Crack(p, dir + (rnd.NextDouble() < 0.5 ? 1 : -1) * (0.5 + 0.6 * rnd.NextDouble()), len * 0.4, depth + 1);
            }
            lines.Add(line);
        }
        for (int k = 0; k < n; k++) Crack(r.Any(rnd), rnd.NextDouble() * 2 * Math.PI, spacing * (1.5 + 2 * rnd.NextDouble()), 0);
        return new([], [], [], lines);
    }

    /// <summary>Square panels of grating, the slots across on one panel and along on the next, like a chequer.</summary>
    static Ground Grating(Box r, double slot, double panel, double g)
    {
        var raised = new PathsD();
        var etched = new PathsD();
        double margin = Math.Max(slot, 0.8);
        for (double x = Math.Floor(r.X0 / panel) * panel; x < r.X1; x += panel)
            for (double y = Math.Floor(r.Y0 / panel) * panel; y < r.Y1; y += panel)
            {
                raised.Add(Geo.Rect(x + g / 2, y + g / 2, x + panel - g / 2, y + panel - g / 2));
                bool across = ((int)Math.Round(x / panel) + (int)Math.Round(y / panel)) % 2 == 0;
                double x0 = x + g / 2 + margin, x1 = x + panel - g / 2 - margin, y0 = y + g / 2 + margin, y1 = y + panel - g / 2 - margin;
                if (x1 - x0 < slot || y1 - y0 < slot) continue;
                int count = Math.Max(1, (int)Math.Floor(((across ? y1 - y0 : x1 - x0) + slot) / (2 * slot)));
                double used = count * 2 * slot - slot, start = (across ? y0 + y1 : x0 + x1) / 2 - used / 2;
                for (int k = 0; k < count; k++)
                {
                    double a = start + k * 2 * slot;
                    etched.Add(across ? BaseShapes.RoundedRect(x0, a, x1, a + slot, slot / 2) : BaseShapes.RoundedRect(a, y0, a + slot, y1, slot / 2));
                }
            }
        return new(raised, etched, [], []);
    }
}
