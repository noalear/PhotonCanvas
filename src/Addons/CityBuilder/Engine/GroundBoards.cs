using Clipper2Lib;
using PhotonCanvas.Addon.FloorTiles;
using PhotonCanvas.Addon.Walls;
using Geo = PhotonCanvas.Addon.Walls.Geo;

namespace PhotonCanvas.Addon.CityBuilder;

/// <summary>How the map's ground is cut as boards.</summary>
public sealed record GroundOptions
{
    /// <summary>The biggest tile, in squares each way.</summary>
    public int TileSquares { get; init; } = 4;
    public double ThicknessMm { get; init; } = 3;
    public TileJoin Join { get; init; } = TileJoin.Jigsaw;
    /// <summary>The grid scored on.</summary>
    public bool Grid { get; init; } = true;
    /// <summary>Each building's outline scored on, so you know where it stands.</summary>
    public bool Outlines { get; init; } = true;
    /// <summary>Only these squares (the far sides just outside), or the whole map.</summary>
    public (int X0, int Y0, int X1, int Y1)? Box { get; init; }
    /// <summary>How worn the stone is, 0 to 1.</summary>
    public double Wear { get; init; } = 0.5;
    public int Seed { get; init; } = 1;
}

/// <summary>
/// The map's ground as floor tiles, the floor tile maker's way (jigsaw edges, clips or magnets, plain round
/// the outside): each square etched with its own ground, cobbles on the roads, flagstones on paving, sand,
/// rock, tufts of grass, pebbles on dirt tracks, ripples on water and furrows in the fields, a tab carrying
/// the ground of the square it reaches into. Squares under buildings are left plain (the building stands on
/// its own floor), with the building's outline scored round them.
/// </summary>
public static class GroundBoards
{
    /// <summary>The floor tile maker's texture for a ground; null for the ones drawn here.</summary>
    public static string? TextureFor(Ground g) => g switch
    {
        Ground.Road => "Cobblestones",
        Ground.Paving => "Flagstones",
        Ground.Sand => "Sand",
        Ground.Rock => "Cave floor",
        Ground.Floor => "Floorboards",
        _ => null,
    };

    public static (TileSpec Spec, TileKit Kit) Make(CityMap m, GroundOptions o, CancellationToken ct = default)
    {
        // The roads laid on the ground; water stays water under the bridges (cut by the bridge maker).
        m = m.Paved(overWater: false);
        var (bx0, by0, bx1, by1) = o.Box ?? (0, 0, m.Across, m.Down);
        bx0 = Math.Clamp(bx0, 0, m.Across - 1); by0 = Math.Clamp(by0, 0, m.Down - 1);
        bx1 = Math.Clamp(bx1, bx0 + 1, m.Across); by1 = Math.Clamp(by1, by0 + 1, m.Down);
        int across = bx1 - bx0, down = by1 - by0;
        double g = m.SquareMm;
        var spec = new TileSpec
        {
            SquareMm = g,
            Mode = TileMode.Board,
            BoardAcross = across,
            BoardDown = down,
            TileAcross = Math.Clamp(o.TileSquares, 1, 20),
            TileDown = Math.Clamp(o.TileSquares, 1, 20),
            PlainBorder = true,
            ThicknessMm = o.ThicknessMm,
            Theme = "Medieval",
            Texture = FloorTextures.Plain,
            Grid = o.Grid ? GridMarks.Scored : GridMarks.None,
            Join = o.Join,
            Wear = o.Wear,
            Seed = o.Seed,
            CracksPct = 10,
        };
        var kit = TileKitMaker.Make(spec);
        if (!kit.Ok) return (spec, kit);

        // Where each ground is on the board (mm, y up, the board's bottom left corner at 0, 0).
        var under = new HashSet<(int X, int Y)>(m.Buildings.SelectMany(CityFootprint.Cells));
        var cells = new Dictionary<Ground, PathsD>();
        for (int y = Math.Max(0, by0 - 1); y < Math.Min(m.Down, by1 + 1); y++)
            for (int x = Math.Max(0, bx0 - 1); x < Math.Min(m.Across, bx1 + 1); x++)
            {
                if (under.Contains((x, y))) continue;
                var gr = m.GroundAt(x, y);
                if (!cells.TryGetValue(gr, out var list)) cells[gr] = list = [];
                double cx = (x - bx0) * g, cy = (by1 - y - 1) * g;
                list.Add(Geo.Rect(cx - 0.01, cy - 0.01, cx + g + 0.01, cy + g + 0.01));
            }
        var regions = cells.ToDictionary(kv => kv.Key, kv => Geo.Union(kv.Value));
        // The buildings' outlines on the board.
        var outlines = new List<PathD>();
        if (o.Outlines)
            foreach (var b in m.Buildings)
            {
                var own = CityFootprint.Cells(b).Select(c => (c.X, c.Y)).ToHashSet();
                foreach (var e in BuildingGuess.Outline(own))
                    outlines.Add([new((e.A.X - bx0) * g, (by1 - e.A.Y) * g), new((e.B.X - bx0) * g, (by1 - e.B.Y) * g)]);
            }

        // Which grounds each tile has (and the area of each, in the tile's own coordinates).
        var spots = kit.Board;
        var areas = new List<(Ground Ground, PathsD Area)>[spots.Count];
        Parallel.For(0, spots.Count, new ParallelOptions { CancellationToken = ct }, i =>
        {
            var spot = spots[i];
            var near = new PathsD { Geo.Rect(spot.X - g, spot.Y - g, spot.X + spot.W + g, spot.Y + spot.H + g) };
            areas[i] = [];
            foreach (var (gr, region) in regions)
            {
                var area = Geo.Intersect(Geo.Move(Geo.Intersect(region, near), -spot.X, -spot.Y), spot.Tile.Cut);
                if (Geo.Area(area) >= 1) areas[i].Add((gr, area));
            }
        });

        // Each ground's marks are made a few times over a whole tile (and a square round it), and each tile
        // takes one of them: quicker than every tile its own, and no two tiles side by side alike.
        double tw = spots.Max(t => t.W), th = spots.Max(t => t.H);
        const int Variants = 3;
        var wanted = areas.SelectMany(a => a.Select(x => x.Ground)).Distinct().SelectMany(gr => Enumerable.Range(0, Variants).Select(v => (gr, v))).ToList();
        var made = new System.Collections.Concurrent.ConcurrentDictionary<(Ground, int), (PathsD Marks, List<PathD> Lines)>();
        Parallel.ForEach(wanted, new ParallelOptions { CancellationToken = ct }, w =>
        {
            var (gr, v) = w;
            var tex = Texture(gr, spec, tw, th, o.Seed * 7919 + (int)gr * 104729 + v * 31);
            var marks = tex.Raised.Count > 0
                ? Geo.Union(Geo.Subtract([Geo.Rect(-g - 1, -g - 1, tw + g + 1, th + g + 1)], Geo.Union(tex.Raised)), tex.Etched)
                : Geo.Union(tex.Etched);
            made[w] = (marks, tex.Lines);
        });

        var newParts = new Part[spots.Count];
        Parallel.For(0, spots.Count, new ParallelOptions { CancellationToken = ct }, i =>
        {
            var spot = spots[i];
            var p = spot.Tile;
            var etched = new PathsD(p.Etched);
            var lines = new List<PathD>(p.Lines);
            foreach (var (gr, area) in areas[i])
            {
                // Next to each other, tiles take different ones.
                int col = (int)Math.Round(spot.X / Math.Max(1, tw)), row = (int)Math.Round(spot.Y / Math.Max(1, th));
                var (marks, texLines) = made[(gr, ((col % Variants) + 2 * (row % Variants)) % Variants)];
                etched.AddRange(Geo.Intersect(marks, area));
                lines.AddRange(Geo.ClipLines(texLines, area));
            }
            if (outlines.Count > 0)
                lines.AddRange(Geo.ClipLines(outlines.Select(l => new PathD(l.Select(q => new PointD(q.x - spot.X, q.y - spot.Y)))), p.Cut));
            newParts[i] = p with { Etched = etched, Lines = lines };
        });
        var replaced = new Dictionary<Part, Part>(ReferenceEqualityComparer.Instance);
        var board = new List<BoardSpot>();
        for (int i = 0; i < spots.Count; i++)
        {
            replaced[spots[i].Tile] = newParts[i];
            board.Add(spots[i] with { Tile = newParts[i] });
        }
        var findings = new List<Finding>(kit.Findings)
        {
            new(Severity.Note, $"The ground of {across} × {down} squares, in {kit.Tiles.Count()} tiles: each square etched with its own ground (cobbles, flagstones, grass, dirt, water, fields…)."
                + (under.Count > 0 ? " Squares under the buildings are left plain, the buildings' outlines scored round them." : "")),
        };
        return (spec, kit with { Parts = [.. kit.Parts.Select(p => replaced.GetValueOrDefault(p, p))], Board = board, Findings = findings });
    }

    /// <summary>The marks for one ground over a tile <paramref name="w"/> × <paramref name="h"/> mm, reaching a square past its edges.</summary>
    static TextureParts Texture(Ground gr, TileSpec s, double w, double h, int seed)
    {
        double g = s.SquareMm;
        if (TextureFor(gr) is { } name && FloorTextures.Preset(name) is { } preset)
        {
            var (a, b) = FloorTextures.SizesFor(preset, g);
            return FloorTextures.Make(s with { Texture = name, SizeA = a, SizeB = b, GrooveMm = preset.Groove }, w, h, seed);
        }
        var rnd = new Random(seed);
        var etched = new PathsD();
        var lines = new List<PathD>();
        double k = g / 25.4;
        switch (gr)
        {
            case Ground.Grass:
                // Tufts of three blades, scattered.
                for (double y = -g; y < h + g; y += 6 * k)
                    for (double x = -g; x < w + g; x += 6 * k)
                    {
                        double cx = x + rnd.NextDouble() * 6 * k, cy = y + rnd.NextDouble() * 6 * k;
                        for (int i = -1; i <= 1; i++)
                        {
                            double ang = Math.PI / 2 + i * 0.45 + (rnd.NextDouble() - 0.5) * 0.3, len = (1.6 + rnd.NextDouble() * 1.2) * k;
                            lines.Add([new(cx, cy), new(cx + Math.Cos(ang) * len, cy + Math.Sin(ang) * len)]);
                        }
                    }
                break;
            case Ground.Dirt:
                // Pebbles, and the odd rut.
                for (double y = -g; y < h + g; y += 4 * k)
                    for (double x = -g; x < w + g; x += 4 * k)
                        if (rnd.NextDouble() < 0.6) etched.Add(Geo.Circle(x + rnd.NextDouble() * 4 * k, y + rnd.NextDouble() * 4 * k, (0.35 + rnd.NextDouble() * 0.5) * k, 0.05));
                break;
            case Ground.Water:
                // Ripples: short waves in rows.
                for (double y = -g; y < h + g; y += 3.2 * k)
                {
                    double x = -g + rnd.NextDouble() * 6 * k;
                    while (x < w + g)
                    {
                        double len = (5 + rnd.NextDouble() * 7) * k, amp = 0.5 * k;
                        var wave = new PathD();
                        for (int i = 0; i <= 12; i++) wave.Add(new(x + len * i / 12, y + amp * Math.Sin(i / 12.0 * Math.PI * 2)));
                        lines.Add(wave);
                        x += len + (2 + rnd.NextDouble() * 5) * k;
                    }
                }
                break;
            case Ground.Field:
                // Furrows across.
                for (double y = -g + 2 * k; y < h + g; y += 4.2 * k) lines.Add([new(-g, y), new(w + g, y)]);
                break;
        }
        return new TextureParts([], etched, lines);
    }
}
