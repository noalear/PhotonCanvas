using PhotonCanvas.Addon.HouseEditor;
using PhotonCanvas.Addon.MapTerrain;
using PhotonCanvas.Core.Imaging;

namespace PhotonCanvas.Addon.CityBuilder;

/// <summary>How the buildings show on a battle map: worked out from the picture, seen from above (their roofs), or drawn as floor plans (their walls in lines).</summary>
public enum BuildingsShow { Auto, Roofs, Plans }

public sealed record CityReadOptions
{
    public BuildingsShow Buildings { get; init; } = BuildingsShow.Auto;
    /// <summary>Find buildings at all (otherwise only the ground and trees are read).</summary>
    public bool FindBuildings { get; init; } = true;
    public bool FindTrees { get; init; } = true;
    /// <summary>The biggest building to look for (squares): anything bigger is ground.</summary>
    public int LargestBuilding { get; init; } = 120;
    /// <summary>How much of a roof's outline must show for it to count (0.5 = usual; lower finds more, higher fewer).</summary>
    public double Sensitivity { get; init; } = 0.5;
    public string Theme { get; init; } = "Medieval";
    public double SquareMm { get; init; } = 25.4;
    /// <summary>Saved buildings to use where one fits a footprint (instead of making one up).</summary>
    public IReadOnlyList<PrefabInfo> Prefabs { get; init; } = [];
}

/// <summary>What reading a battle map made, and notes on how it went.</summary>
public sealed record CityReadResult(CityMap Map, List<string> Notes);

/// <summary>
/// Reads a town from a battle map picture, as a best guess at the world seen from above: the grid (see
/// <see cref="GridFinder"/>), then every square sorted by its colour into grass, roads, paving, dirt,
/// sand, water, rock or fields; roofs found by <see cref="RoofFinder"/> as rectangles (at any angle)
/// that a patch of one colour fills, with their outline drawn most of the way round; floor plans found
/// as rooms closed in by walls drawn in dark lines; and woods as darker, busier green. Each building is
/// made by <see cref="BuildingGuess"/> (or a saved building that fits), its door towards the road.
/// </summary>
public static class CityReader
{
    /// <summary>One square of the picture: its mean colour (0–1), how busy it is, and what its colour says.</summary>
    internal readonly record struct Sample(double R, double G, double B, double Busy, double H, double S, double V);

    /// <summary>What a square's colour says it is.</summary>
    internal enum Nature { Water, Grass, Trees, Sand, Dirt, Field, Light, Mid, Dark, Warm }

    /// <summary>The picture's grid: by its drawn lines first, or else by how the picture repeats.</summary>
    public static MapPicture? FindGrid(RasterImage img) => GridFinder.Find(img) ?? MapReader.FindGrid(img);

    public static CityReadResult Read(RasterImage img, CityPicture grid, CityReadOptions o, CancellationToken ct = default)
    {
        var notes = new List<string>();
        int w = img.Width, h = img.Height;
        double p = Math.Max(4, grid.SquarePx), ox = grid.OffsetX, oy = grid.OffsetY;
        double x0 = ox - Math.Floor(ox / p) * p, y0 = oy - Math.Floor(oy / p) * p;
        if (x0 > p * 0.5) x0 -= p;
        if (y0 > p * 0.5) y0 -= p;
        int across = Math.Clamp((int)Math.Floor((w - x0) / p + 0.5), 1, CityMap.MaxSide);
        int down = Math.Clamp((int)Math.Floor((h - y0) / p + 0.5), 1, CityMap.MaxSide);
        if ((w - x0) / p > CityMap.MaxSide + 0.5 || (h - y0) / p > CityMap.MaxSide + 0.5)
            notes.Add($"The map is more than {CityMap.MaxSide} squares across: only its top left {across} × {down} squares are read.");

        var px = img.Rgba;
        var lum = img.Luminance();
        var samples = new Sample[across, down];
        for (int y = 0; y < down; y++)
        {
            ct.ThrowIfCancellationRequested();
            for (int x = 0; x < across; x++)
                samples[x, y] = Measure(px, lum, w, h, x0 + x * p + p * 0.15, y0 + y * p + p * 0.15, x0 + x * p + p * 0.85, y0 + y * p + p * 0.85);
        }
        var nature = Natures(samples, across, down);

        var map = CityMap.Empty(across, down) with
        {
            Theme = o.Theme, SquareMm = o.SquareMm,
            Picture = grid with { },
        };
        var ground = new Ground[across, down];
        for (int y = 0; y < down; y++)
            for (int x = 0; x < across; x++) ground[x, y] = GroundOf(nature[x, y]);

        var used = new bool[across, down];
        var buildings = new List<CityBuilding>();
        var footprints = new List<SeenBuilding>();
        // Where each footprint seen at an angle really sits.
        var placings = new Dictionary<SeenBuilding, Placing>(ReferenceEqualityComparer.Instance);
        int tilted = 0, fromPrefabs = 0;
        if (o.FindBuildings)
        {
            if (o.Buildings != BuildingsShow.Plans)
            {
                // The roofs, as the roof finder sees them: rectangles at any angle.
                var vision = MapVision.Of(img, p, x0, y0, ct);
                var roofs = RoofFinder.Find(vision, new RoofFinder.Options { LargestSquares = Math.Max(1, o.LargestBuilding), Sensitivity = o.Sensitivity }, ct);
                foreach (var r in roofs)
                {
                    ct.ThrowIfCancellationRequested();
                    if (Seen(vision, r, across, down) is not { } seen) continue;
                    if (seen.Placing.Tilt != 0) { tilted++; placings[seen.Footprint] = seen.Placing; }
                    footprints.Add(seen.Footprint);
                    foreach (var c in seen.Footprint.Cells) used[c.X, c.Y] = true;
                }
                if (roofs.Count > 0) notes.Add($"{footprints.Count} building{(footprints.Count == 1 ? "" : "s")} found from their roofs" + (tilted > 0 ? $", {tilted} of them at an angle to the grid." : "."));
            }
            // Floor plans: asked for, or when the roofs gave next to nothing (a map drawn as plans has no roofs to find).
            if (o.Buildings == BuildingsShow.Plans || (o.Buildings == BuildingsShow.Auto && footprints.Count < 2))
            {
                var plans = Plans(samples, lum, w, h, x0, y0, p, across, down, o.LargestBuilding, ct)
                    .Where(f => !f.Cells.Any(c => used[c.X, c.Y])).ToList();
                foreach (var f in plans) foreach (var c in f.Cells) used[c.X, c.Y] = true;
                footprints.AddRange(plans);
                if (plans.Count > 0) notes.Add($"{plans.Count} building{(plans.Count == 1 ? "" : "s")} found drawn as floor plans (rooms closed in by walls in lines).");
            }
            if (footprints.Count == 0) notes.Add("No buildings were found: draw their footprints with the Building tool, lower the sensitivity, or try another setting for how the buildings show.");
        }
        // What's under the buildings, and what looked like a roof but wasn't one.
        for (int y = 0; y < down; y++)
            for (int x = 0; x < across; x++)
            {
                if (used[x, y]) ground[x, y] = Ground.Floor;
                else if (ground[x, y] == Ground.Floor) ground[x, y] = nature[x, y] == Nature.Warm ? Ground.Dirt : Ground.Paving;
            }

        // Doors open onto roads, paving or dirt next to the building.
        foreach (var f in footprints)
        {
            ct.ThrowIfCancellationRequested();
            var towards = new HashSet<(int X, int Y)>(f.DoorTowards);
            if (towards.Count == 0)
                foreach (var (cx, cy) in f.Cells)
                    foreach (var (nx, ny) in new[] { (cx + 1, cy), (cx - 1, cy), (cx, cy + 1), (cx, cy - 1) })
                        if (nx >= 0 && ny >= 0 && nx < across && ny < down && !used[nx, ny] && ground[nx, ny] is Ground.Road or Ground.Paving or Ground.Dirt)
                            towards.Add((nx, ny));
            var fp = f with { DoorTowards = towards };
            placings.TryGetValue(f, out var placing);
            if (placing.Tilt == 0 && PrefabFit.Fit(fp, o.Prefabs) is { } fit)
            {
                buildings.Add(fit);
                fromPrefabs++;
                continue;
            }
            if (BuildingGuess.Make(fp, o.Theme, o.SquareMm) is not { } made) continue;
            var b = new CityBuilding { Name = $"{fp.Type} {buildings.Count + 1}", Type = fp.Type, X = made.X, Y = made.Y, Design = made.Design };
            if (placing.Tilt != 0)
            {
                // Turned about its middle to where the roof really sits, its corner wherever that puts it.
                var (bw, bd) = CityFootprint.Size(b);
                double cx = placing.CentreX - bw / 2.0, cy = placing.CentreY - bd / 2.0;
                int bx = (int)Math.Floor(cx), by = (int)Math.Floor(cy);
                b = b with { X = bx, Y = by, ShiftX = cx - bx, ShiftY = cy - by, TiltDeg = placing.Tilt };
            }
            buildings.Add(b);
        }
        if (fromPrefabs > 0) notes.Add($"{fromPrefabs} of them use saved buildings that fit their footprint.");

        var props = new List<Prop>();
        if (o.FindTrees)
        {
            var rnd = new Random(1234);
            int count = 0;
            // One tree for a small clump of tree squares (one canopy drawn over a few squares); a tree a square in a wood.
            var seen = new bool[across, down];
            for (int y = 0; y < down && props.Count < 6000; y++)
                for (int x = 0; x < across && props.Count < 6000; x++)
                {
                    if (nature[x, y] != Nature.Trees || used[x, y] || seen[x, y]) continue;
                    var clump = new List<(int X, int Y)>();
                    var todo = new Stack<(int X, int Y)>();
                    todo.Push((x, y));
                    seen[x, y] = true;
                    while (todo.Count > 0)
                    {
                        var (cx, cy) = todo.Pop();
                        clump.Add((cx, cy));
                        foreach (var (nx, ny) in new[] { (cx + 1, cy), (cx - 1, cy), (cx, cy + 1), (cx, cy - 1) })
                            if (nx >= 0 && ny >= 0 && nx < across && ny < down && !seen[nx, ny] && nature[nx, ny] == Nature.Trees && !used[nx, ny]) { seen[nx, ny] = true; todo.Push((nx, ny)); }
                    }
                    var s = samples[x, y];
                    // Bluer, darker greens read as pines; the rest as leafy trees.
                    var kind = s.H > 120 && s.V < 0.35 ? PropKind.Pine : PropKind.Tree;
                    if (clump.Count <= 4)
                    {
                        props.Add(new Prop { Kind = kind, X = clump.Average(c => c.X) + 0.5, Y = clump.Average(c => c.Y) + 0.5, Size = Math.Clamp(Math.Sqrt(clump.Count) * 1.1, 1, 2.2), TurnDeg = rnd.Next(360) });
                        count++;
                        continue;
                    }
                    foreach (var (cx, cy) in clump)
                    {
                        props.Add(new Prop { Kind = kind, X = cx + 0.5 + (rnd.NextDouble() - 0.5) * 0.3, Y = cy + 0.5 + (rnd.NextDouble() - 0.5) * 0.3, Size = 1 + rnd.NextDouble() * 0.5, TurnDeg = rnd.Next(360) });
                        count++;
                    }
                }
            if (count > 0) notes.Add($"{count} tree{(count == 1 ? "" : "s")} where the green is darker or busier.");
        }
        int water = 0, roads = 0;
        foreach (var g in ground) { if (g == Ground.Water) water++; else if (g is Ground.Road or Ground.Paving) roads++; }
        if (water > 0) notes.Add($"{water} squares of water.");
        if (roads > 0) notes.Add($"{roads} squares of road and paving.");
        notes.Add("It's a best guess: paint the ground, move, turn and change the buildings to put right what it got wrong.");
        return new CityReadResult(map.WithGround(ground) with { Buildings = buildings, Props = props }, notes);
    }

    /// <summary>Where a roof seen at an angle goes: its tilt past the quarter turns, and its middle (squares).</summary>
    readonly record struct Placing(double Tilt, double CentreX, double CentreY);

    /// <summary>
    /// A roof the finder saw as a footprint on the grid: square to it (within five degrees) it's snapped to
    /// whole squares, each of its rectangles rounded to squares (so an L stays an L); at an angle it's its
    /// biggest rectangle, rounded to whole squares, kept at its angle about its middle.
    /// </summary>
    static (SeenBuilding Footprint, Placing Placing)? Seen(MapVision v, SeenRoof r, int across, int down)
    {
        double sq = v.Square;
        var sample = new Sample(r.R, r.G, r.B, 0, Hsv(r.R, r.G, r.B).H, Hsv(r.R, r.G, r.B).S, Hsv(r.R, r.G, r.B).V);
        string covering = CoveringFor(sample);
        // Squares from working cells: the grid's first lines are at the offsets.
        double Sx(double x) => (x - v.OffsetX) / sq;
        double Sy(double y) => (y - v.OffsetY) / sq;
        double a = r.AngleDeg;
        bool square = a <= 5 || a >= 85;
        if (square)
        {
            var cells = new HashSet<(int X, int Y)>();
            foreach (var box in r.Rects)
            {
                // Along the axes (at 85° and more the box's width runs down the map).
                double bw = a >= 85 ? box.H : box.W, bd = a >= 85 ? box.W : box.H;
                int wSq = Math.Max(1, (int)Math.Round(bw / sq)), dSq = Math.Max(1, (int)Math.Round(bd / sq));
                int x0 = (int)Math.Round(Sx(box.Cx) - wSq / 2.0), y0 = (int)Math.Round(Sy(box.Cy) - dSq / 2.0);
                for (int y = y0; y < y0 + dSq; y++)
                    for (int x = x0; x < x0 + wSq; x++)
                        if (x >= 0 && y >= 0 && x < across && y < down) cells.Add((x, y));
            }
            if (cells.Count == 0) return null;
            return (new SeenBuilding { Cells = cells, Covering = covering, Type = TypeFor(cells.Count) }, default);
        }
        var main = r.Rects[0];
        // The tilt is the smaller turn from either axis, so the footprint's longer side stays its longer side.
        double tilt = a <= 45 ? a : a - 90;
        double mw = a <= 45 ? main.W : main.H, md = a <= 45 ? main.H : main.W;
        int tw = Math.Max(1, (int)Math.Round(mw / sq)), td = Math.Max(1, (int)Math.Round(md / sq));
        double cxSq = Sx(main.Cx), cySq = Sy(main.Cy);
        // Its cells for the ground under it and the door: the squares its middle part lies over, square to the grid.
        var own = new HashSet<(int X, int Y)>();
        int ax0 = (int)Math.Round(cxSq - tw / 2.0), ay0 = (int)Math.Round(cySq - td / 2.0);
        for (int y = ay0; y < ay0 + td; y++)
            for (int x = ax0; x < ax0 + tw; x++)
                if (x >= 0 && y >= 0 && x < across && y < down) own.Add((x, y));
        if (own.Count == 0) return null;
        return (new SeenBuilding { Cells = own, Covering = covering, Type = TypeFor(own.Count) }, new Placing(tilt, cxSq, cySq));
    }

    /// <summary>What a building of so many squares most likely is.</summary>
    static string TypeFor(int squares) => squares >= 40 ? "Hall" : squares >= 20 ? "Big house" : squares <= 2 ? "Shed" : "House";

    static Sample Measure(byte[] px, float[] lum, int w, int h, double ax, double ay, double bx, double by)
    {
        int i0 = Math.Clamp((int)Math.Floor(ax), 0, w - 1), i1 = Math.Clamp((int)Math.Ceiling(bx), i0 + 1, w);
        int j0 = Math.Clamp((int)Math.Floor(ay), 0, h - 1), j1 = Math.Clamp((int)Math.Ceiling(by), j0 + 1, h);
        int sx = Math.Max(1, (i1 - i0) / 14), sy = Math.Max(1, (j1 - j0) / 14);
        double r = 0, g = 0, b = 0, l = 0, l2 = 0;
        int n = 0;
        for (int j = j0; j < j1; j += sy)
            for (int i = i0; i < i1; i += sx)
            {
                int k = (j * w + i) * 4;
                r += px[k];
                g += px[k + 1];
                b += px[k + 2];
                double v = lum[j * w + i];
                l += v;
                l2 += v * v;
                n++;
            }
        if (n == 0) return new Sample(0.5, 0.5, 0.5, 0, 0, 0, 0.5);
        r /= 255.0 * n;
        g /= 255.0 * n;
        b /= 255.0 * n;
        double mean = l / n, busy = Math.Sqrt(Math.Max(0, l2 / n - mean * mean));
        var (hh, ss, vv) = Hsv(r, g, b);
        return new Sample(r, g, b, busy, hh, ss, vv);
    }

    internal static (double H, double S, double V) Hsv(double r, double g, double b)
    {
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
        double h = 0;
        if (d > 1e-9)
        {
            if (max == r) h = 60 * (((g - b) / d) % 6);
            else if (max == g) h = 60 * ((b - r) / d + 2);
            else h = 60 * ((r - g) / d + 4);
        }
        if (h < 0) h += 360;
        return (h, max <= 1e-9 ? 0 : d / max, max);
    }

    /// <summary>What each square's colour says it is; woods are the darker, busier part of the green.</summary>
    internal static Nature[,] Natures(Sample[,] s, int across, int down)
    {
        var n = new Nature[across, down];
        var greens = new List<(double V, double Busy)>();
        for (int y = 0; y < down; y++)
            for (int x = 0; x < across; x++)
            {
                n[x, y] = NatureOf(s[x, y]);
                if (n[x, y] == Nature.Grass) greens.Add((s[x, y].V, s[x, y].Busy));
            }
        Tinted(s, n, across, down);
        if (greens.Count >= 6)
        {
            var vs = greens.Select(g => g.V).OrderBy(v => v).ToArray();
            var bs = greens.Select(g => g.Busy).OrderBy(v => v).ToArray();
            double vMid = vs[vs.Length / 2], bMid = bs[bs.Length / 2];
            for (int y = 0; y < down; y++)
                for (int x = 0; x < across; x++)
                {
                    if (n[x, y] != Nature.Grass) continue;
                    var q = s[x, y];
                    // Clearly darker than the usual green, or much busier (leaves and shadows).
                    if (q.V < vMid - 0.12 || (q.Busy > Math.Max(0.06, bMid * 1.9) && q.V < vMid)) n[x, y] = Nature.Trees;
                }
        }
        return n;
    }

    /// <summary>
    /// A map painted all one hue (a night scene, blue over everything) reads as water wherever the paint
    /// is blue enough, cobbles and all. When a good share of the map is water and nearly every square is
    /// the same blue, only the squares that are plainly water stay so: smoother than wet cobbles, or
    /// clearly bluer than the run of them; the rest are paving.
    /// </summary>
    static void Tinted(Sample[,] s, Nature[,] n, int across, int down)
    {
        var water = new List<Sample>();
        var hues = new List<double>();
        for (int y = 0; y < down; y++)
            for (int x = 0; x < across; x++)
            {
                if (n[x, y] == Nature.Water) water.Add(s[x, y]);
                if (s[x, y].S > 0.1) hues.Add(s[x, y].H);
            }
        if (water.Count < 0.35 * across * down || hues.Count < 6) return;
        hues.Sort();
        double hMid = hues[hues.Count / 2];
        if (hMid < 190 || hMid > 250) return;
        if (hues.Count(h => Math.Abs(h - hMid) <= 30) < 0.75 * hues.Count) return;
        var sats = water.Select(q => q.S).OrderBy(q => q).ToArray();
        double sMid = sats[sats.Length / 2];
        for (int y = 0; y < down; y++)
            for (int x = 0; x < across; x++)
            {
                if (n[x, y] != Nature.Water) continue;
                var q = s[x, y];
                if (q.Busy < 0.045 || q.S > sMid + 0.08) continue;
                n[x, y] = Nature.Mid;
            }
    }

    internal static Nature NatureOf(Sample q)
    {
        double h = q.H, s = q.S, v = q.V;
        if (v < 0.16) return Nature.Dark;
        // Blue-grey (slate) is less saturated than water.
        if (s > 0.28 && h >= 170 && h <= 260 && q.B > q.R + 0.08) return Nature.Water;
        if (s > 0.14 && h >= 65 && h < 170) return Nature.Grass;
        if (s < 0.14)
        {
            // Grey: paving and roads when light, slate or rock when dark.
            if (v > 0.66) return Nature.Light;
            if (v > 0.34) return Nature.Mid;
            return Nature.Dark;
        }
        if ((h < 18 || h > 330) && s > 0.32) return Nature.Warm;
        // A yellow-green that's busy with strokes: a tree's leaves (a field is drawn in even rows).
        if (h >= 35 && h < 65 && s > 0.45 && q.Busy > 0.16) return Nature.Trees;
        if (h >= 40 && h < 65 && s > 0.35 && v > 0.45) return Nature.Field;
        if (h >= 30 && h < 65 && v > 0.62) return Nature.Sand;
        if (h < 65) return v > 0.3 ? Nature.Dirt : Nature.Dark;
        // Purples and pinks (rare on a map): read by brightness.
        return v > 0.55 ? Nature.Light : Nature.Mid;
    }

    static Ground GroundOf(Nature n) => n switch
    {
        Nature.Water => Ground.Water,
        Nature.Grass or Nature.Trees => Ground.Grass,
        Nature.Sand => Ground.Sand,
        Nature.Dirt => Ground.Dirt,
        Nature.Field => Ground.Field,
        Nature.Light => Ground.Paving,
        Nature.Mid => Ground.Road,
        Nature.Dark => Ground.Rock,
        // A red patch on the ground that isn't a roof: most likely earth.
        _ => Ground.Floor,
    };

    internal static string CoveringFor(Sample q)
    {
        if (q.S < 0.16) return "Slate";
        if ((q.H < 22 || q.H > 330) && q.S > 0.3) return "Clay tiles";
        if (q.H >= 35 && q.H < 65 && q.V > 0.5) return "Thatch";
        if (q.H < 45) return q.V < 0.5 ? "Wooden shingles" : "Clay tiles";
        return "Slate";
    }

    // ---- floor plans ----

    /// <summary>
    /// Rooms closed in by walls drawn as dark lines along the grid: the squares reached from each other
    /// without crossing a line, away from the map's edge and not too big, with most of their outside edge
    /// drawn. Rooms that share a line are one building (the line is a wall inside it); gaps in the outside
    /// line are its doors.
    /// </summary>
    static List<SeenBuilding> Plans(Sample[,] s, float[] lum, int w, int h, double x0, double y0, double p, int across, int down, int largest, CancellationToken ct)
    {
        var wall = new HashSet<EdgeId>();
        double half = Math.Max(1.5, p * 0.08);
        double Strip(double ax, double ay, double bx, double by, double below, out double share)
        {
            int i0 = Math.Clamp((int)Math.Floor(ax), 0, w - 1), i1 = Math.Clamp((int)Math.Ceiling(bx), i0 + 1, w);
            int j0 = Math.Clamp((int)Math.Floor(ay), 0, h - 1), j1 = Math.Clamp((int)Math.Ceiling(by), j0 + 1, h);
            int sx = Math.Max(1, (i1 - i0) / 24), sy = Math.Max(1, (j1 - j0) / 24);
            double sum = 0;
            int n = 0, dark = 0;
            for (int j = j0; j < j1; j += sy)
                for (int i = i0; i < i1; i += sx) { float v = lum[j * w + i]; sum += v; n++; if (v < below) dark++; }
            share = n == 0 ? 0 : (double)dark / n;
            return n == 0 ? 1 : sum / n;
        }
        double L(int x, int y) => (s[x, y].R * 0.299 + s[x, y].G * 0.587 + s[x, y].B * 0.114);
        for (int y = 0; y <= down; y++)
            for (int x = 0; x <= across; x++)
            {
                // Along the top of square (x, y), and down its left side.
                if (x < across && y > 0 && y < down)
                {
                    double side = Math.Min(L(x, y - 1), L(x, y)), lx = x0 + x * p, ly = y0 + y * p;
                    double m = Strip(lx + p * 0.15, ly - half, lx + p * 0.85, ly + half, side - 0.2, out double share);
                    if (side - m >= 0.2 && share >= 0.5) wall.Add(new EdgeId(x, y, true));
                }
                if (y < down && x > 0 && x < across)
                {
                    double side = Math.Min(L(x - 1, y), L(x, y)), lx = x0 + x * p, ly = y0 + y * p;
                    double m = Strip(lx - half, ly + p * 0.15, lx + half, ly + p * 0.85, side - 0.2, out double share);
                    if (side - m >= 0.2 && share >= 0.5) wall.Add(new EdgeId(x, y, false));
                }
            }
        ct.ThrowIfCancellationRequested();
        if (wall.Count < 4) return [];
        // A one-square gap in a straight line of wall is a doorway: closed for finding the rooms, a door afterwards.
        var gaps = new HashSet<EdgeId>();
        foreach (var e in wall.ToList())
        {
            var next = e.Across ? e with { X = e.X + 1 } : e with { Y = e.Y + 1 };
            var after = e.Across ? e with { X = e.X + 2 } : e with { Y = e.Y + 2 };
            if (!wall.Contains(next) && wall.Contains(after)) gaps.Add(next);
        }
        var closed = new HashSet<EdgeId>(wall);
        closed.UnionWith(gaps);

        // Rooms: reached without crossing a drawn line.
        var room = new int[across, down];
        for (int y = 0; y < down; y++) for (int x = 0; x < across; x++) room[x, y] = -1;
        var rooms = new List<List<(int X, int Y)>>();
        for (int y = 0; y < down; y++)
            for (int x = 0; x < across; x++)
            {
                if (room[x, y] >= 0) continue;
                int id = rooms.Count;
                var list = new List<(int, int)>();
                var todo = new Stack<(int, int)>();
                todo.Push((x, y));
                room[x, y] = id;
                while (todo.Count > 0)
                {
                    var (cx, cy) = todo.Pop();
                    list.Add((cx, cy));
                    foreach (var (nx, ny, e) in new[] { (cx + 1, cy, new EdgeId(cx + 1, cy, false)), (cx - 1, cy, new EdgeId(cx, cy, false)), (cx, cy + 1, new EdgeId(cx, cy + 1, true)), (cx, cy - 1, new EdgeId(cx, cy, true)) })
                        if (nx >= 0 && ny >= 0 && nx < across && ny < down && room[nx, ny] < 0 && !closed.Contains(e))
                        {
                            room[nx, ny] = id;
                            todo.Push((nx, ny));
                        }
                }
                rooms.Add(list);
            }
        // A room: not the outside (touching the map's edge or huge), and walled in most of the way round.
        var isRoom = new bool[rooms.Count];
        for (int r = 0; r < rooms.Count; r++)
        {
            var cells = rooms[r];
            if (cells.Count > largest || cells.Any(c => c.X == 0 || c.Y == 0 || c.X == across - 1 || c.Y == down - 1)) continue;
            var set = cells.ToHashSet();
            int edges = 0, drawn = 0;
            foreach (var e in Edges(set)) { edges++; if (closed.Contains(e)) drawn++; }
            isRoom[r] = edges > 0 && drawn >= edges * 0.75;
        }
        // Rooms that touch across a drawn line make one building.
        var parent = Enumerable.Range(0, rooms.Count).ToArray();
        int Root(int i) { while (parent[i] != i) i = parent[i] = parent[parent[i]]; return i; }
        foreach (var e in closed)
        {
            var (a, b) = e.Sides;
            if (a.X < 0 || a.Y < 0 || b.X >= across || b.Y >= down) continue;
            int ra = room[a.X, a.Y], rb = room[b.X, b.Y];
            if (ra != rb && isRoom[ra] && isRoom[rb]) parent[Root(ra)] = Root(rb);
        }
        var groups = new Dictionary<int, HashSet<(int X, int Y)>>();
        for (int r = 0; r < rooms.Count; r++)
        {
            if (!isRoom[r]) continue;
            int g = Root(r);
            if (!groups.TryGetValue(g, out var set)) groups[g] = set = [];
            set.UnionWith(rooms[r]);
        }
        var res = new List<SeenBuilding>();
        foreach (var cells in groups.Values)
        {
            if (cells.Count < 2 || cells.Count > largest) continue;
            var inner = new List<(GridPt, GridPt)>();
            var doors = new HashSet<(int X, int Y)>();
            foreach (var e in wall)
            {
                var (a, b) = e.Sides;
                if (cells.Contains(a) && cells.Contains(b))
                {
                    var (f, t) = e.Ends;
                    inner.Add((new GridPt(f.X, f.Y), new GridPt(t.X, t.Y)));
                }
            }
            // Gaps in the outside line are where the doors are.
            foreach (var e in Edges(cells))
            {
                if (!gaps.Contains(e)) continue;
                var (a, b) = e.Sides;
                doors.Add(cells.Contains(a) ? b : a);
            }
            var (type, storeys) = BuildingGuess.Kind(cells);
            res.Add(new SeenBuilding { Cells = cells, InnerWalls = inner, DoorTowards = doors, Storeys = Math.Min(storeys, 2), Type = type, From = "plan" });
        }
        return res;
    }

    /// <summary>The grid edges round a set of squares.</summary>
    static IEnumerable<EdgeId> Edges(HashSet<(int X, int Y)> cells)
    {
        foreach (var (x, y) in cells)
        {
            if (!cells.Contains((x, y - 1))) yield return new EdgeId(x, y, true);
            if (!cells.Contains((x, y + 1))) yield return new EdgeId(x, y + 1, true);
            if (!cells.Contains((x - 1, y))) yield return new EdgeId(x, y, false);
            if (!cells.Contains((x + 1, y))) yield return new EdgeId(x + 1, y, false);
        }
    }
}
