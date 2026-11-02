using Clipper2Lib;
using PhotonCanvas.Addon.Walls;

namespace PhotonCanvas.Addon.FloorTiles;

/// <summary>
/// A floor texture: its name, the names of its two sizes (null = not used) and their defaults for a
/// 25.4 mm square (scaled with the square when <paramref name="Scales"/>), whether it has a groove,
/// a random layout and stones that can crack. <paramref name="Walls"/> names the wall maker's texture
/// it is made with, when it is one of those.
/// </summary>
public sealed record FloorPreset(string Name, string? SizeALabel, double SizeA, string? SizeBLabel, double SizeB, bool UsesGroove, double Groove, bool Random, bool Stone, bool Scales, string About, string? Walls = null);

/// <summary>
/// The floor textures: some of the wall maker's (laid flat) and some made for floors, laid out from
/// the tile's corner on the grid so that tiles side by side carry the pattern on where it repeats with
/// the square (flagstones that follow the squares, boards, bricks and tiles sized to fit one).
/// </summary>
public static class FloorTextures
{
    public const string Plain = "Plain";
    /// <summary>The square the default sizes are for (mm).</summary>
    public const double BaseSquare = 25.4;

    public static readonly IReadOnlyList<FloorPreset> Presets =
    [
        new("Dungeon flagstones", "Squares split %", 50, null, 0, true, 0.8, true, true, false,
            "Square slabs that follow the grid: some squares are one big slab, others split into halves or quarters, with chipped corners. The grid shows without any lines."),
        new("Flagstones", "Stone size", 10, null, 0, true, 0.8, true, true, true,
            "Irregular paving stones of many sides fitted together (crazy paving)."),
        new("Cobblestones", "Stone size", 4.5, null, 0, true, 0.7, true, true, true,
            "Small rounded cobbles: streets, yards and market squares."),
        new("Floorboards", "Board width", 5.08, "Board length", 60, true, 0.5, true, false, true,
            "Boards running across the tile with staggered joints, grain and nail holes. Five boards to a square.", "Wood planks (across)"),
        new("Stone blocks", "Block length", 12.7, "Row width", 8.47, true, 0.8, true, true, true,
            "Cut stone in rows of different widths, blocks of different lengths.", "Stone blocks"),
        new("Brick paving", "Brick length", 8.47, "Brick width", 4.23, true, 0.6, false, true, true,
            "Bricks in rows, each row moved along half a brick: three to a square.", "Brick"),
        new("Herringbone brick", "Brick width", 3.175, null, 0, true, 0.5, false, true, true,
            "Bricks twice as long as they're wide laid in a zigzag, as in old courtyards and halls."),
        new("Square tiles", "Tile size", 12.7, null, 0, true, 0.6, false, true, true,
            "Square tiles in straight rows: four to a square.", "Tiles"),
        new("Hexagons", "Hexagon size", 8, null, 0, true, 0.7, false, true, true,
            "Hexagonal tiles, for temples, labs and sci-fi floors.", "Hexagons"),
        new("Metal deck plates", "Plate width", 25.4, "Plate length", 12.7, true, 0.5, false, false, true,
            "Riveted plates, for ships, bunkers and sci-fi decks.", "Metal plates"),
        new("Tread plate", "Pattern size", 4, null, 0, false, 0, false, false, true,
            "Non-slip metal plate: little bars etched in, leaning one way and the other."),
        new("Chequered tiles", "Tile size", 12.7, null, 0, true, 0.5, false, false, true,
            "Square tiles, every other one etched dark: halls, chapels and crypts. Four to a square."),
        new("Cave floor", "Rock size", 18, "Layer height", 12, true, 1.0, true, true, true,
            "Rough rock in uneven slabs, split by cracks and pitted, as the wall maker's cave rock.", "Cave rock"),
        new("Sci-fi deck", "Panel width", 25.4, "Panel height", 12.7, true, 0.5, true, false, true,
            "Metal deck panels with cut corners, vents, bolts and hazard stripes, as the wall maker's sci-fi panels.", "Sci-fi panels"),
        new("Grating", "Slot pitch", 2.2, null, 0, false, 0, false, false, true,
            "Open metal grating: rows of slots etched in each square inside a solid frame, turned square by square."),
        new("Sandstone slabs", "Slab length", 16.9, "Row width", 8.47, true, 0.7, true, true, true,
            "Long sandstone slabs with wind-worn lines and pits, as the wall maker's sandstone.", "Sandstone"),
        new("Sand", "Ripple spacing", 3, null, 0, false, 0, true, false, true,
            "Sand blown into ripples, with the odd pebble: deserts, beaches and arenas."),
        new(Plain, null, 0, null, 0, false, 0, false, false, false, "No texture: just the tiles (and the grid, if you mark it)."),
    ];

    public static FloorPreset? Preset(string name) => Presets.FirstOrDefault(p => p.Name == name);

    /// <summary>A preset's sizes for a square of <paramref name="square"/> mm.</summary>
    public static (double A, double B) SizesFor(FloorPreset p, double square)
    {
        double k = p.Scales ? square / BaseSquare : 1;
        return (p.SizeA * k, p.SizeB * k);
    }

    /// <summary>
    /// The texture over a tile <paramref name="w"/> × <paramref name="h"/> mm (y up, the tile's corner at
    /// 0, 0), reaching a square beyond each edge so tabs that stick out are covered too.
    /// </summary>
    public static TextureParts Make(TileSpec s, double w, double h, int seed)
    {
        double g0 = Math.Max(1, s.SquareMm), a = Math.Max(0.5, s.SizeA), b = Math.Max(0.5, s.SizeB);
        double groove = Math.Clamp(s.GrooveMm, 0.05, 3);
        var rnd = new Random(seed);
        var region = new Area(-g0, -g0, w + g0, h + g0);
        var p = Preset(s.Texture);
        TextureParts parts;
        if (p is null || p.Walls is not null)
        {
            // One of the wall maker's: made over a face a square bigger all round, then moved back a square.
            var wallSpec = p is null
                ? new WallSpec { Texture = s.Custom?.Name ?? s.Texture, Custom = s.Custom, SizeA = a, SizeB = b, GrooveMm = groove, Seed = seed }
                : new WallSpec { Texture = p.Walls!, SizeA = a, SizeB = p.SizeBLabel is null ? a : b, GrooveMm = groove, Seed = seed };
            if (p is null && s.Custom is null) return TextureParts.None;
            double fw = w + 2 * g0, fh = h + 2 * g0;
            var t = Textures.Make(wallSpec, new Face(fw, fh, 0, fw, fh));
            parts = new(Geo.Move(t.Raised, -g0, -g0), Geo.Move(t.Etched, -g0, -g0), Geo.Map(t.Lines, q => new PointD(q.x - g0, q.y - g0)));
        }
        else parts = s.Texture switch
        {
            "Dungeon flagstones" => Dungeon(region, g0, Math.Clamp(s.SizeA, 0, 100) / 100, Math.Min(groove, g0 / 6), rnd),
            "Flagstones" => Crazy(region, a, Math.Min(groove, a / 3), rnd, jitter: 0.45, round: Math.Min(a * 0.06, 0.6)),
            "Cobblestones" => Crazy(region, a, Math.Min(groove, a / 3), rnd, jitter: 0.3, round: a * 0.22),
            "Herringbone brick" => Herringbone(region, a, Math.Min(groove, a / 2)),
            "Tread plate" => Tread(region, a),
            "Chequered tiles" => Chequer(region, a, Math.Min(groove, a / 3)),
            "Grating" => Grating(region, g0, a),
            "Sand" => Sand(region, a, rnd),
            _ => TextureParts.None,
        };
        if (p is { Stone: true } && s.CracksPct > 0 && parts.Raised.Count > 0)
            parts = parts with { Lines = [.. parts.Lines, .. Cracks(parts.Raised, s.CracksPct / 100, rnd)] };
        return parts;
    }

    /// <summary>The rectangle a texture is made over.</summary>
    readonly record struct Area(double X0, double Y0, double X1, double Y1);

    static PathD Box(double x0, double y0, double x1, double y1, double g) => Geo.Rect(x0 + g / 2, y0 + g / 2, x1 - g / 2, y1 - g / 2);

    /// <summary>
    /// Slabs on the grid: each square is one slab or (for <paramref name="split"/> of them) split into
    /// halves, quarters, a half and two quarters, or three strips; corners are chipped now and then and
    /// the edges wander a little, so no two slabs are quite alike.
    /// </summary>
    static TextureParts Dungeon(Area r, double sq, double split, double g, Random rnd)
    {
        var raised = new PathsD();
        int i0 = (int)Math.Floor(r.X0 / sq), i1 = (int)Math.Ceiling(r.X1 / sq), j0 = (int)Math.Floor(r.Y0 / sq), j1 = (int)Math.Ceiling(r.Y1 / sq);
        for (int i = i0; i < i1; i++)
            for (int j = j0; j < j1; j++)
            {
                double x = i * sq, y = j * sq;
                var cells = new List<(double X0, double Y0, double X1, double Y1)>();
                if (rnd.NextDouble() >= split) cells.Add((0, 0, 1, 1));
                else
                {
                    double m = 0.5 + (rnd.NextDouble() - 0.5) * 0.2;
                    switch (rnd.Next(7))
                    {
                        case 0: cells.AddRange([(0, 0, m, 1), (m, 0, 1, 1)]); break;
                        case 1: cells.AddRange([(0, 0, 1, m), (0, m, 1, 1)]); break;
                        case 2: cells.AddRange([(0, 0, 0.5, 0.5), (0.5, 0, 1, 0.5), (0, 0.5, 0.5, 1), (0.5, 0.5, 1, 1)]); break;
                        case 3: cells.AddRange([(0, 0, 1, 0.5), (0, 0.5, 0.5, 1), (0.5, 0.5, 1, 1)]); break;
                        case 4: cells.AddRange([(0, 0.5, 1, 1), (0, 0, 0.5, 0.5), (0.5, 0, 1, 0.5)]); break;
                        case 5: cells.AddRange([(0, 0, 0.33, 1), (0.33, 0, 0.67, 1), (0.67, 0, 1, 1)]); break;
                        default: cells.AddRange([(0, 0, 0.5, 1), (0.5, 0, 1, 0.5), (0.5, 0.5, 1, 1)]); break;
                    }
                }
                foreach (var c in cells)
                {
                    double ax = x + c.X0 * sq + g / 2, ay = y + c.Y0 * sq + g / 2, bx = x + c.X1 * sq - g / 2, by = y + c.Y1 * sq - g / 2;
                    double size = Math.Min(bx - ax, by - ay), wob = Math.Min(g * 0.4, size * 0.03);
                    // Each corner pulled in a little at random (never out, so the grooves on the grid stay whole).
                    double J() => rnd.NextDouble() * wob;
                    var slab = new PathD { new(ax + J(), ay + J()), new(bx - J(), ay + J()), new(bx - J(), by - J()), new(ax + J(), by - J()) };
                    // A chipped corner now and then.
                    if (rnd.NextDouble() < 0.25)
                    {
                        int k = rnd.Next(4);
                        double cut = size * (0.08 + 0.1 * rnd.NextDouble());
                        var corner = slab[k];
                        var prev = slab[(k + 3) % 4];
                        var next = slab[(k + 1) % 4];
                        PointD Toward(PointD from, PointD to, double d)
                        {
                            double dx = to.x - from.x, dy = to.y - from.y, l = Math.Max(1e-9, Math.Sqrt(dx * dx + dy * dy));
                            return new(from.x + dx / l * d, from.y + dy / l * d);
                        }
                        var chipped = new PathD();
                        for (int q = 0; q < 4; q++)
                        {
                            if (q == k) { chipped.Add(Toward(corner, prev, cut)); chipped.Add(Toward(corner, next, cut * (0.6 + 0.8 * rnd.NextDouble()))); }
                            else chipped.Add(slab[q]);
                        }
                        slab = chipped;
                    }
                    raised.Add(slab);
                }
            }
        return new(raised, [], []);
    }

    /// <summary>
    /// Stones of many sides fitted together: the cells round points scattered on a jiggled grid
    /// (each cell holds the ground nearer its point than any other), shrunk by half the groove and
    /// rounded off.
    /// </summary>
    static TextureParts Crazy(Area r, double size, double g, Random rnd, double jitter, double round)
    {
        int nx = (int)Math.Ceiling((r.X1 - r.X0) / size) + 3, ny = (int)Math.Ceiling((r.Y1 - r.Y0) / size) + 3;
        var sites = new PointD[nx, ny];
        for (int i = 0; i < nx; i++)
            for (int j = 0; j < ny; j++)
            {
                // Every other row moved along half a stone, so the stones don't line up in columns.
                double ox = j % 2 == 1 ? size / 2 : 0;
                sites[i, j] = new(r.X0 + (i - 1) * size + ox + (rnd.NextDouble() - 0.5) * 2 * jitter * size,
                                  r.Y0 + (j - 1) * size + (rnd.NextDouble() - 0.5) * 2 * jitter * size);
            }
        var raised = new PathsD();
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
                if (cell.Count < 3) continue;
                raised.AddRange(round > 0
                    ? Geo.Offset(Geo.Offset([cell], -(g / 2 + round), round: true), round, round: true)
                    : Geo.Offset([cell], -g / 2));
            }
        return new(raised, [], []);
    }

    /// <summary>The part of a convex polygon nearer <paramref name="keep"/> than <paramref name="other"/>.</summary>
    static PathD HalfPlane(PathD poly, PointD keep, PointD other)
    {
        double mx = (keep.x + other.x) / 2, my = (keep.y + other.y) / 2, nx = other.x - keep.x, ny = other.y - keep.y;
        double Side(PointD p) => (p.x - mx) * nx + (p.y - my) * ny;  // < 0: on keep's side
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

    /// <summary>
    /// Bricks <paramref name="w"/> wide and twice as long in a herringbone: steps of a lying brick and a
    /// standing one going up to the right, each step a brick's width along and up from the last, the
    /// steps repeated every two widths along and two down.
    /// </summary>
    internal static PathsD HerringboneBricks(double x0, double y0, double x1, double y1, double w)
    {
        var bricks = new PathsD();
        int n = (int)Math.Ceiling(Math.Max(Math.Abs(x0), Math.Max(Math.Abs(x1), Math.Max(Math.Abs(y0), Math.Abs(y1)))) / w) + 4;
        for (int i = -2 * n; i <= 2 * n; i++)
            for (int m = -n; m <= n; m++)
            {
                double u = (i + 2 * m) * w, v = (i - 2 * m) * w;
                // Lying: [u, u + 2w] × [v, v + w]. Standing: [u + 2w, u + 3w] × [v − w, v + w].
                if (u + 2 * w > x0 && u < x1 && v + w > y0 && v < y1) bricks.Add(Geo.Rect(u, v, u + 2 * w, v + w));
                if (u + 3 * w > x0 && u + 2 * w < x1 && v + w > y0 && v - w < y1) bricks.Add(Geo.Rect(u + 2 * w, v - w, u + 3 * w, v + w));
            }
        return bricks;
    }

    static TextureParts Herringbone(Area r, double w, double g) =>
        new([.. HerringboneBricks(r.X0, r.Y0, r.X1, r.Y1, w).Select(p => Box(p[0].x, p[0].y, p[2].x, p[2].y, g))], [], []);

    /// <summary>Tread plate: little round-ended bars, etched in, leaning one way and the other in a chequer.</summary>
    static TextureParts Tread(Area r, double size)
    {
        var etched = new PathsD();
        double len = size * 0.7, wid = Math.Max(0.3, size * 0.17);
        for (double x = Math.Floor(r.X0 / size) * size; x < r.X1; x += size)
            for (double y = Math.Floor(r.Y0 / size) * size; y < r.Y1; y += size)
            {
                int k = (int)Math.Round(x / size) + (int)Math.Round(y / size);
                double ang = (k % 2 == 0 ? 45 : -45) * Math.PI / 180, cx = x + size / 2, cy = y + size / 2;
                double dx = Math.Cos(ang) * (len - wid) / 2, dy = Math.Sin(ang) * (len - wid) / 2;
                // A round-ended bar: the line between its ends, grown by half its width. (Grown as an open line: a thin
                // triangle along it would vanish when it lies along the line, and the bars leaning one way went missing.)
                etched.AddRange(Clipper.InflatePaths([[new(cx - dx, cy - dy), new(cx + dx, cy + dy)]], wid / 2, JoinType.Round, EndType.Round, 2, 4));
            }
        return new([], etched, []);
    }

    /// <summary>Square tiles in rows and columns from the tile's corner, every other one etched all over (dark).</summary>
    static TextureParts Chequer(Area r, double size, double g)
    {
        var raised = new PathsD();
        var etched = new PathsD();
        for (int i = (int)Math.Floor(r.X0 / size); i * size < r.X1; i++)
            for (int j = (int)Math.Floor(r.Y0 / size); j * size < r.Y1; j++)
            {
                var tile = Box(i * size, j * size, (i + 1) * size, (j + 1) * size, g);
                raised.Add(tile);
                if (((i + j) % 2 + 2) % 2 == 1) etched.Add(tile);
            }
        return new(raised, etched, []);
    }

    /// <summary>
    /// Grating: in each grid square a solid frame round rows of slots <paramref name="pitch"/> apart,
    /// the rows running across in one square and up and down in the next, all etched in.
    /// </summary>
    static TextureParts Grating(Area r, double sq, double pitch)
    {
        var etched = new PathsD();
        double frame = Math.Max(1.2, sq * 0.06), slot = Math.Max(0.4, pitch * 0.5);
        for (int i = (int)Math.Floor(r.X0 / sq); i * sq < r.X1; i++)
            for (int j = (int)Math.Floor(r.Y0 / sq); j * sq < r.Y1; j++)
            {
                double x0 = i * sq + frame, y0 = j * sq + frame, x1 = (i + 1) * sq - frame, y1 = (j + 1) * sq - frame;
                if (x1 - x0 < pitch * 2 || y1 - y0 < pitch * 2) continue;
                bool across = ((i + j) % 2 + 2) % 2 == 0;
                double from = across ? y0 : x0, to = across ? y1 : x1;
                int n = Math.Max(1, (int)Math.Floor((to - from - slot) / pitch) + 1);
                double start = from + (to - from - (n - 1) * pitch - slot) / 2;
                for (int k = 0; k < n; k++)
                {
                    double t = start + k * pitch;
                    etched.Add(across ? Geo.Rect(x0, t, x1, t + slot) : Geo.Rect(t, y0, t + slot, y1));
                }
            }
        return new([], etched, []);
    }

    /// <summary>Sand: gently wavy ripples scored across, broken now and then, and a few small pebbles etched in.</summary>
    static TextureParts Sand(Area r, double spacing, Random rnd)
    {
        var lines = new List<PathD>();
        var etched = new PathsD();
        double s = Math.Max(1, spacing), wave = s * (5 + 3 * rnd.NextDouble()), slant = (rnd.NextDouble() - 0.5) * 0.3;
        for (double y = r.Y0; y < r.Y1 + s; y += s * (0.8 + 0.4 * rnd.NextDouble()))
        {
            double phase = rnd.NextDouble() * Math.PI * 2, amp = s * (0.25 + 0.2 * rnd.NextDouble());
            var line = new PathD();
            for (double x = r.X0; x <= r.X1; x += 0.6)
            {
                // A break in the ripple now and then.
                if (rnd.NextDouble() < 0.012 && line.Count > 4) { lines.Add(line); line = []; x += s * (1 + 2 * rnd.NextDouble()); continue; }
                line.Add(new PointD(x, y + slant * (x - r.X0) + amp * Math.Sin(x / wave * Math.PI * 2 + phase)));
            }
            if (line.Count >= 2) lines.Add(line);
        }
        int pebbles = (int)((r.X1 - r.X0) * (r.Y1 - r.Y0) / (s * s * 40));
        for (int k = 0; k < pebbles; k++)
            etched.Add(Geo.Circle(r.X0 + (r.X1 - r.X0) * rnd.NextDouble(), r.Y0 + (r.Y1 - r.Y0) * rnd.NextDouble(), s * (0.08 + 0.1 * rnd.NextDouble())));
        return new([], etched, lines);
    }

    /// <summary>A wandering crack scored across some of the stones (<paramref name="share"/> of them, the bigger ones).</summary>
    static IEnumerable<PathD> Cracks(PathsD stones, double share, Random rnd)
    {
        foreach (var stone in stones)
        {
            if (stone.Count < 3 || rnd.NextDouble() >= share) continue;
            var b = Clipper.GetBounds(stone);
            double w = b.right - b.left, h = b.bottom - b.top;
            if (Math.Min(w, h) < 2) continue;
            // From somewhere along one side to somewhere along the opposite one, in a few jagged steps.
            bool across = rnd.NextDouble() < 0.5;
            PointD a = across ? new(b.left - 0.5, b.top + h * (0.2 + 0.6 * rnd.NextDouble())) : new(b.left + w * (0.2 + 0.6 * rnd.NextDouble()), b.top - 0.5);
            PointD z = across ? new(b.right + 0.5, b.top + h * (0.2 + 0.6 * rnd.NextDouble())) : new(b.left + w * (0.2 + 0.6 * rnd.NextDouble()), b.bottom + 0.5);
            // Most cracks stop part way.
            double reach = rnd.NextDouble() < 0.6 ? 0.45 + 0.4 * rnd.NextDouble() : 1;
            int steps = 4 + rnd.Next(3);
            double jag = Math.Min(w, h) * 0.12;
            var line = new PathD();
            for (int k = 0; k <= steps; k++)
            {
                double t = reach * k / steps;
                double off = k == 0 ? 0 : (rnd.NextDouble() - 0.5) * 2 * jag;
                line.Add(across ? new(a.x + (z.x - a.x) * t, a.y + (z.y - a.y) * t + off) : new(a.x + (z.x - a.x) * t + off, a.y + (z.y - a.y) * t));
            }
            // Kept off the stone's edge a little, so it reads as a crack in the stone and not a chip.
            foreach (var l in Geo.ClipLines([line], Geo.Offset([stone], -0.25))) yield return l;
        }
    }
}
