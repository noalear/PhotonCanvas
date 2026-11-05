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
    /// <summary>How different a roof has to look from the ground round it to count (0.5 = usual; lower finds more, higher fewer).</summary>
    public double Sensitivity { get; init; } = 0.5;
    public string Theme { get; init; } = "Medieval";
    public double SquareMm { get; init; } = 25.4;
    /// <summary>Saved buildings to use where one fits a footprint (instead of making one up).</summary>
    public IReadOnlyList<PrefabInfo> Prefabs { get; init; } = [];
}

/// <summary>What reading a battle map made, and notes on how it went.</summary>
public sealed record CityReadResult(CityMap Map, List<string> Notes);

/// <summary>
/// Reads a town from a battle map picture, as a best guess at the world seen from above: the grid (as
/// the map to terrain reader finds it), then every square sorted by its colour into grass, roads, paving,
/// dirt, sand, water, rock or fields; roofs found as patches of one colour that stand out from the ground
/// round them and are building-shaped (compact, not too big, not part of a long road); floor plans found
/// as rooms closed in by walls drawn in dark lines; and woods as darker, busier green. Each building is
/// made by <see cref="BuildingGuess"/> (or a saved building that fits), its door towards the road.
/// </summary>
public static class CityReader
{
    /// <summary>One square of the picture: its mean colour (0–1), how busy it is, and what its colour says.</summary>
    internal readonly record struct Sample(double R, double G, double B, double Busy, double H, double S, double V);

    /// <summary>What a square's colour says it is.</summary>
    internal enum Nature { Water, Grass, Trees, Sand, Dirt, Field, Light, Mid, Dark, Warm }

    public static MapPicture? FindGrid(RasterImage img) => MapReader.FindGrid(img);

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
        var footprints = new List<SeenBuilding>();
        if (o.FindBuildings)
        {
            if (o.Buildings != BuildingsShow.Roofs)
            {
                var plans = Plans(samples, lum, w, h, x0, y0, p, across, down, o.LargestBuilding, ct);
                foreach (var f in plans) foreach (var c in f.Cells) used[c.X, c.Y] = true;
                footprints.AddRange(plans);
                if (plans.Count > 0) notes.Add($"{plans.Count} building{(plans.Count == 1 ? "" : "s")} found drawn as floor plans (rooms closed in by walls in lines).");
            }
            if (o.Buildings != BuildingsShow.Plans)
            {
                var roofs = Roofs(samples, nature, used, across, down, o.LargestBuilding, o.Sensitivity, new Edging(lum, w, h, x0, y0, p), ct);
                foreach (var f in roofs) foreach (var c in f.Cells) used[c.X, c.Y] = true;
                footprints.AddRange(roofs);
                if (roofs.Count > 0) notes.Add($"{roofs.Count} building{(roofs.Count == 1 ? "" : "s")} found from their roofs.");
            }
            if (footprints.Count == 0) notes.Add("No buildings were found: draw their footprints with the Building tool, or try another setting for how the buildings show.");
        }
        // What's under the buildings, and what looked like a roof but wasn't one.
        for (int y = 0; y < down; y++)
            for (int x = 0; x < across; x++)
            {
                if (used[x, y]) ground[x, y] = Ground.Floor;
                else if (ground[x, y] == Ground.Floor) ground[x, y] = nature[x, y] == Nature.Warm ? Ground.Dirt : Ground.Paving;
            }

        // Doors open onto roads, paving or dirt next to the building.
        var buildings = new List<CityBuilding>();
        int fromPrefabs = 0;
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
            if (PrefabFit.Fit(fp, o.Prefabs) is { } fit)
            {
                buildings.Add(fit);
                fromPrefabs++;
                continue;
            }
            if (BuildingGuess.Make(fp, o.Theme, o.SquareMm) is not { } made) continue;
            buildings.Add(new CityBuilding { Name = $"{fp.Type} {buildings.Count + 1}", Type = fp.Type, X = made.X, Y = made.Y, Design = made.Design });
        }
        if (fromPrefabs > 0) notes.Add($"{fromPrefabs} of them use saved buildings that fit their footprint.");

        var props = new List<Prop>();
        if (o.FindTrees)
        {
            var rnd = new Random(1234);
            int count = 0;
            for (int y = 0; y < down && props.Count < 6000; y++)
                for (int x = 0; x < across && props.Count < 6000; x++)
                {
                    if (nature[x, y] != Nature.Trees || used[x, y]) continue;
                    var s = samples[x, y];
                    // Bluer, darker greens read as pines; the rest as leafy trees.
                    var kind = s.H > 120 && s.V < 0.35 ? PropKind.Pine : PropKind.Tree;
                    props.Add(new Prop { Kind = kind, X = x + 0.5 + (rnd.NextDouble() - 0.5) * 0.3, Y = y + 0.5 + (rnd.NextDouble() - 0.5) * 0.3, Size = 1 + rnd.NextDouble() * 0.5, TurnDeg = rnd.Next(360) });
                    count++;
                }
            if (count > 0) notes.Add($"{count} tree{(count == 1 ? "" : "s")} where the green is darker and busier.");
        }
        int water = 0, roads = 0;
        foreach (var g in ground) { if (g == Ground.Water) water++; else if (g is Ground.Road or Ground.Paving) roads++; }
        if (water > 0) notes.Add($"{water} squares of water.");
        if (roads > 0) notes.Add($"{roads} squares of road and paving.");
        notes.Add("It's a best guess: paint the ground, move, turn and change the buildings to put right what it got wrong.");
        return new CityReadResult(map.WithGround(ground) with { Buildings = buildings, Props = props }, notes);
    }

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

    static double Distance(Sample a, Sample b)
    {
        double dr = a.R - b.R, dg = a.G - b.G, db = a.B - b.B;
        return Math.Sqrt(dr * dr + dg * dg + db * db);
    }

    // ---- roofs ----

    /// <summary>
    /// Patches of one colour that stand out from the ground round them and are building-shaped. The squares are
    /// grouped by colour (k-means); a colour that runs on and on (a road, a lawn) is ground, and so is anything
    /// green or blue; a patch is a roof when it's compact (fills most of its bounding box, two squares or more
    /// both ways), not too big, and its edge differs clearly from what's round it.
    /// </summary>
    static List<SeenBuilding> Roofs(Sample[,] s, Nature[,] nature, bool[,] used, int across, int down, int largest, double sensitivity, Edging edging, CancellationToken ct)
    {
        int n = across * down;
        var label = Cluster(s, across, down, 8, ct);
        var res = new List<SeenBuilding>();
        var seen = new bool[across, down];
        double needed = 0.08 + 0.16 * Math.Clamp(sensitivity, 0, 1);
        for (int y = 0; y < down; y++)
            for (int x = 0; x < across; x++)
            {
                if (seen[x, y]) continue;
                // One patch: squares of one colour group, joined side by side.
                int c = label[x, y];
                var cells = Joined((x, y), q => label[q.X, q.Y] == c, across, down, seen);
                if (cells.Count < 3 || cells.Count > n * 0.3) continue;
                Try(cells, 0);
            }
        return res;

        // A patch is a roof, or else (a roof the same colour group as the road beside it) maybe its two colours apart are.
        void Try(List<(int X, int Y)> cells, int depth)
        {
            ct.ThrowIfCancellationRequested();
            if (cells.Count < 3) return;
            if (Roof(cells) is { } found) { res.Add(found); return; }
            if (depth >= 3 || cells.Count < 6) return;
            var (a, b) = Split(s, cells);
            if (a.Count == 0 || b.Count == 0 || Distance(Mean(s, a), Mean(s, b)) < 0.06) return;
            foreach (var part in new[] { a, b })
            {
                var inPart = part.ToHashSet();
                var done = new bool[across, down];
                foreach (var q in part)
                    if (!done[q.X, q.Y]) Try(Joined(q, r => inPart.Contains(r), across, down, done), depth + 1);
            }
        }

        SeenBuilding? Roof(List<(int X, int Y)> cells)
        {
            if (cells.Count > largest) return null;
            if (cells.Any(q => used[q.X, q.Y])) return null;
            int greenish = cells.Count(q => nature[q.X, q.Y] is Nature.Water or Nature.Grass or Nature.Trees);
            if (greenish * 2 > cells.Count) return null;
            int bx0 = cells.Min(q => q.X), bx1 = cells.Max(q => q.X), by0 = cells.Min(q => q.Y), by1 = cells.Max(q => q.Y);
            int bw = bx1 - bx0 + 1, bd = by1 - by0 + 1;
            if (bw < 2 || bd < 2) return null;
            var set = Filled(cells.ToHashSet(), bx0, by0, bx1, by1);
            double fill = set.Count / (double)(bw * bd);
            if (fill < 0.6) return null;
            // A long thin patch is a road or a wall, not a house.
            int lo = Math.Min(bw, bd), hi = Math.Max(bw, bd);
            if (hi > 4 * lo || (lo <= 2 && hi > 2.5 * lo)) return null;
            // How much it stands out from what's just round it.
            double inside = 0, outside = 0;
            int ni = 0, no = 0;
            var mean = Mean(s, set);
            foreach (var (cx, cy) in set)
                foreach (var (nx, ny) in new[] { (cx + 1, cy), (cx - 1, cy), (cx, cy + 1), (cx, cy - 1) })
                {
                    if (set.Contains((nx, ny))) continue;
                    if (nx < 0 || ny < 0 || nx >= across || ny >= down) continue;
                    outside += Distance(s[nx, ny], mean);
                    no++;
                }
            foreach (var q in set) { inside += Distance(s[q.X, q.Y], mean); ni++; }
            if (no == 0) return null;
            double contrast = outside / no - inside / Math.Max(1, ni);
            // Red-tiled roofs stand out by colour anyway; greys need the clearer edge.
            int warm = set.Count(q => nature[q.X, q.Y] == Nature.Warm);
            bool red = warm * 2 > set.Count;
            double need = red ? needed * 0.6 : needed;
            if (contrast < need) return null;
            // Roofs on a battle map are outlined (or cast a shadow); a paved square or a road isn't. Only the reddest
            // roofs, or any patch when looking hard (Roofs turned well down), go without.
            if (!red && sensitivity >= 0.3 && edging.Outlined(set) < 0.35) return null;
            var (type, storeys) = BuildingGuess.Kind(set);
            return new SeenBuilding { Cells = set, Covering = CoveringFor(mean), Storeys = storeys, Type = type, From = "roof" };
        }
    }

    /// <summary>How much of a patch's edge is drawn darker than either side of it (an outline or a shadow), from the picture.</summary>
    sealed class Edging(float[] lum, int w, int h, double x0, double y0, double p)
    {
        double Mean(double ax, double ay, double bx, double by)
        {
            int i0 = Math.Clamp((int)Math.Floor(ax), 0, w - 1), i1 = Math.Clamp((int)Math.Ceiling(bx), i0 + 1, w);
            int j0 = Math.Clamp((int)Math.Floor(ay), 0, h - 1), j1 = Math.Clamp((int)Math.Ceiling(by), j0 + 1, h);
            int sx = Math.Max(1, (i1 - i0) / 20), sy = Math.Max(1, (j1 - j0) / 20);
            double sum = 0;
            int n = 0;
            for (int j = j0; j < j1; j += sy)
                for (int i = i0; i < i1; i += sx) { sum += lum[j * w + i]; n++; }
            return n == 0 ? 1 : sum / n;
        }

        /// <summary>The share (0 to 1) of the patch's outside edges that are darker than both sides by a clear step.</summary>
        public double Outlined(HashSet<(int X, int Y)> cells)
        {
            int edges = 0, dark = 0;
            double band = Math.Max(2, p * 0.12), side = p * 0.3;
            foreach (var (x, y) in cells)
                foreach (var (dx, dy) in new[] { (0, -1), (0, 1), (-1, 0), (1, 0) })
                {
                    if (cells.Contains((x + dx, y + dy))) continue;
                    edges++;
                    // The grid line between the square and its neighbour, and the strips either side of it.
                    double lx = x0 + (dx > 0 ? x + 1 : x) * p, ly = y0 + (dy > 0 ? y + 1 : y) * p;
                    // The darkest thin line near the grid line (an outline is a few pixels wide, on either side of it).
                    double line = double.MaxValue, a, b;
                    if (dy != 0)
                    {
                        double ax = x0 + x * p + p * 0.2, bx = x0 + x * p + p * 0.8;
                        for (double d = -band; d <= band; d += 1) line = Math.Min(line, Mean(ax, ly + d - 1, bx, ly + d + 1));
                        a = Mean(ax, ly - band - side, bx, ly - band);
                        b = Mean(ax, ly + band, bx, ly + band + side);
                    }
                    else
                    {
                        double ay = y0 + y * p + p * 0.2, by = y0 + y * p + p * 0.8;
                        for (double d = -band; d <= band; d += 1) line = Math.Min(line, Mean(lx + d - 1, ay, lx + d + 1, by));
                        a = Mean(lx - band - side, ay, lx - band, by);
                        b = Mean(lx + band, ay, lx + band + side, by);
                    }
                    if (Math.Min(a, b) - line > 0.1) dark++;
                }
            return edges == 0 ? 0 : (double)dark / edges;
        }
    }

    /// <summary>The squares joined side by side to <paramref name="start"/> that pass <paramref name="same"/> (marked in <paramref name="seen"/>).</summary>
    static List<(int X, int Y)> Joined((int X, int Y) start, Func<(int X, int Y), bool> same, int across, int down, bool[,] seen)
    {
        var cells = new List<(int X, int Y)>();
        var todo = new Stack<(int X, int Y)>();
        todo.Push(start);
        seen[start.X, start.Y] = true;
        while (todo.Count > 0)
        {
            var (cx, cy) = todo.Pop();
            cells.Add((cx, cy));
            foreach (var q in new[] { (cx + 1, cy), (cx - 1, cy), (cx, cy + 1), (cx, cy - 1) })
                if (q.Item1 >= 0 && q.Item2 >= 0 && q.Item1 < across && q.Item2 < down && !seen[q.Item1, q.Item2] && same(q))
                {
                    seen[q.Item1, q.Item2] = true;
                    todo.Push(q);
                }
        }
        return cells;
    }

    /// <summary>A patch's squares split in two by colour (two-means, started from its darkest and lightest squares).</summary>
    static (List<(int X, int Y)> A, List<(int X, int Y)> B) Split(Sample[,] s, List<(int X, int Y)> cells)
    {
        var f = cells.Select(q => Feature(s[q.X, q.Y])).ToList();
        var ca = f.MinBy(v => v.Item1);
        var cb = f.MaxBy(v => v.Item1);
        var side = new bool[cells.Count];
        for (int it = 0; it < 10; it++)
        {
            for (int i = 0; i < f.Count; i++) side[i] = D2(f[i], cb) < D2(f[i], ca);
            (double, double, double) M(bool which)
            {
                double a = 0, b = 0, c = 0;
                int n = 0;
                for (int i = 0; i < f.Count; i++) if (side[i] == which) { a += f[i].Item1; b += f[i].Item2; c += f[i].Item3; n++; }
                return n == 0 ? (which ? cb : ca) : (a / n, b / n, c / n);
            }
            ca = M(false);
            cb = M(true);
        }
        var A = new List<(int, int)>();
        var B = new List<(int, int)>();
        for (int i = 0; i < cells.Count; i++) (side[i] ? B : A).Add(cells[i]);
        return (A, B);
        static double D2((double, double, double) p, (double, double, double) q) => (p.Item1 - q.Item1) * (p.Item1 - q.Item1) + (p.Item2 - q.Item2) * (p.Item2 - q.Item2) + (p.Item3 - q.Item3) * (p.Item3 - q.Item3);
    }

    /// <summary>The squares with the holes in them filled (a chimney or a skylight in a roof).</summary>
    static HashSet<(int X, int Y)> Filled(HashSet<(int X, int Y)> cells, int x0, int y0, int x1, int y1)
    {
        var outside = new HashSet<(int, int)>();
        var todo = new Stack<(int, int)>();
        for (int x = x0 - 1; x <= x1 + 1; x++) { todo.Push((x, y0 - 1)); todo.Push((x, y1 + 1)); }
        for (int y = y0; y <= y1; y++) { todo.Push((x0 - 1, y)); todo.Push((x1 + 1, y)); }
        while (todo.Count > 0)
        {
            var (x, y) = todo.Pop();
            if (x < x0 - 1 || x > x1 + 1 || y < y0 - 1 || y > y1 + 1 || cells.Contains((x, y)) || !outside.Add((x, y))) continue;
            todo.Push((x + 1, y)); todo.Push((x - 1, y)); todo.Push((x, y + 1)); todo.Push((x, y - 1));
        }
        var res = new HashSet<(int X, int Y)>(cells);
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                if (!outside.Contains((x, y))) res.Add((x, y));
        return res;
    }

    static Sample Mean(Sample[,] s, IEnumerable<(int X, int Y)> cells)
    {
        double r = 0, g = 0, b = 0, busy = 0;
        int n = 0;
        foreach (var (x, y) in cells) { var q = s[x, y]; r += q.R; g += q.G; b += q.B; busy += q.Busy; n++; }
        if (n == 0) return default;
        r /= n; g /= n; b /= n;
        var (h, sat, v) = Hsv(r, g, b);
        return new Sample(r, g, b, busy / n, h, sat, v);
    }

    /// <summary>The roof maker's covering a roof's colour suggests.</summary>
    internal static string CoveringFor(Sample q)
    {
        if (q.S < 0.16) return "Slate";
        if ((q.H < 22 || q.H > 330) && q.S > 0.3) return "Clay tiles";
        if (q.H >= 35 && q.H < 65 && q.V > 0.5) return "Thatch";
        if (q.H < 45) return q.V < 0.5 ? "Wooden shingles" : "Clay tiles";
        return "Slate";
    }

    /// <summary>The squares grouped into <paramref name="k"/> colours (k-means, started from evenly spread brightnesses, so the same map always groups the same way).</summary>
    static int[,] Cluster(Sample[,] s, int across, int down, int k, CancellationToken ct)
    {
        var pts = new List<(double A, double B, double C)>(across * down);
        for (int y = 0; y < down; y++)
            for (int x = 0; x < across; x++) pts.Add(Feature(s[x, y]));
        var order = Enumerable.Range(0, pts.Count).OrderBy(i => pts[i].A).ToArray();
        k = Math.Min(k, pts.Count);
        var centres = new (double A, double B, double C)[k];
        for (int i = 0; i < k; i++) centres[i] = pts[order[(int)((i + 0.5) / k * (pts.Count - 1))]];
        var label = new int[pts.Count];
        for (int it = 0; it < 14; it++)
        {
            ct.ThrowIfCancellationRequested();
            bool moved = false;
            for (int i = 0; i < pts.Count; i++)
            {
                int best = 0;
                double bd = double.MaxValue;
                for (int c = 0; c < k; c++)
                {
                    double da = pts[i].A - centres[c].A, db = pts[i].B - centres[c].B, dc = pts[i].C - centres[c].C, d = da * da + db * db + dc * dc;
                    if (d < bd) { bd = d; best = c; }
                }
                if (label[i] != best) { label[i] = best; moved = true; }
            }
            var sum = new (double A, double B, double C, int N)[k];
            for (int i = 0; i < pts.Count; i++)
            {
                ref var t = ref sum[label[i]];
                t.A += pts[i].A; t.B += pts[i].B; t.C += pts[i].C; t.N++;
            }
            for (int c = 0; c < k; c++) if (sum[c].N > 0) centres[c] = (sum[c].A / sum[c].N, sum[c].B / sum[c].N, sum[c].C / sum[c].N);
            if (!moved && it > 0) break;
        }
        var res = new int[across, down];
        for (int y = 0; y < down; y++)
            for (int x = 0; x < across; x++) res[x, y] = label[y * across + x];
        return res;
    }

    /// <summary>A colour as brightness and two colour differences (roughly how the eye sorts them).</summary>
    static (double, double, double) Feature(Sample q) => ((q.R + q.G + q.B) / 3, (q.R - q.G) * 1.4, (q.R + q.G - 2 * q.B) * 0.7);

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
