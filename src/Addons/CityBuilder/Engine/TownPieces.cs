using PhotonCanvas.Addon.Bridges;
using PhotonCanvas.Addon.Furniture;
using PhotonCanvas.Addon.Scatter;
using ScatterDefaults = PhotonCanvas.Addon.Scatter.Defaults;
using FItemKind = PhotonCanvas.Addon.Furniture.ItemKind;
using SItemKind = PhotonCanvas.Addon.Scatter.ItemKind;

namespace PhotonCanvas.Addon.CityBuilder;

/// <summary>Where a road crosses water: its squares, how many squares the water is across (the way the road runs) and how wide the road is.</summary>
public sealed record BridgeSite(HashSet<(int X, int Y)> Cells, bool Across, int Span, int Width, Ground Road)
{
    public (double X, double Y) Middle => (Cells.Average(c => c.X) + 0.5, Cells.Average(c => c.Y) + 0.5);
}

/// <summary>
/// What a town wall is cut as: sections two squares long and one, corners (the parapet round the outside of
/// the turn, or the inside), gatehouses over the roads through it, ends with a turret, and guard towers.
/// </summary>
public sealed record WallCut(int Two, int One, int CornersOut, int CornersIn, int Gates, int Ends, int Towers)
{
    public int Count => Two + One + CornersOut + CornersIn + Gates + Ends + Towers;
}

/// <summary>
/// The rest of a town's pieces besides the buildings and the ground: a bridge from the bridge maker wherever
/// a road crosses water (stone arches for a cobbled road, planks for a track), the scenery maker's wells,
/// carts, barrels, boulders and fences for the map's own, and the furniture maker's detailed town pieces in
/// the map's look: the town wall and its guard towers, trees, horse troughs, stages, stalls, bars and notice boards.
/// </summary>
public static class TownPieces
{
    // ---- bridges ----

    /// <summary>Every place a road (cobbles, paving or a dirt track) crosses water: road squares with water on both sides, side by side.</summary>
    public static List<BridgeSite> Bridges(CityMap m, (int X0, int Y0, int X1, int Y1)? box = null)
    {
        static bool IsRoad(Ground g) => g is Ground.Road or Ground.Paving or Ground.Dirt;
        bool Water(int x, int y) => x >= 0 && y >= 0 && x < m.Across && y < m.Down && m.GroundAt(x, y) == Ground.Water;
        // A road square over water: water to its left and right (a road running down), or above and below (one running across).
        var down = new HashSet<(int X, int Y)>();
        var across = new HashSet<(int X, int Y)>();
        for (int y = 0; y < m.Down; y++)
            for (int x = 0; x < m.Across; x++)
            {
                if (!IsRoad(m.GroundAt(x, y))) continue;
                if (WaterRun(x, y, 1, 0) && WaterRun(x, y, -1, 0)) down.Add((x, y));
                if (WaterRun(x, y, 0, 1) && WaterRun(x, y, 0, -1)) across.Add((x, y));
            }
        // Water on that side, perhaps past other road squares of the same bridge (a road up to four squares wide).
        bool WaterRun(int x, int y, int dx, int dy)
        {
            for (int i = 1; i <= 4; i++)
            {
                int nx = x + dx * i, ny = y + dy * i;
                if (Water(nx, ny)) return true;
                if (nx < 0 || ny < 0 || nx >= m.Across || ny >= m.Down || !IsRoad(m.GroundAt(nx, ny))) return false;
            }
            return false;
        }
        var sites = new List<BridgeSite>();
        foreach (var (set, isAcross) in new[] { (down, false), (across, true) })
        {
            var seen = new HashSet<(int, int)>();
            foreach (var start in set.OrderBy(c => c.Y).ThenBy(c => c.X))
            {
                if (!seen.Add(start)) continue;
                var cells = new HashSet<(int X, int Y)> { start };
                var todo = new Stack<(int X, int Y)>([start]);
                while (todo.Count > 0)
                {
                    var (x, y) = todo.Pop();
                    foreach (var n in new[] { (x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1) })
                        if (set.Contains(n) && seen.Add(n)) { cells.Add(n); todo.Push(n); }
                }
                int spanX = cells.Max(c => c.X) - cells.Min(c => c.X) + 1, spanY = cells.Max(c => c.Y) - cells.Min(c => c.Y) + 1;
                // The way the road runs is the way it crosses the water.
                int span = isAcross ? spanX : spanY, width = isAcross ? spanY : spanX;
                if (span < 1 || width < 1 || width > 6) continue;
                var site = new BridgeSite(cells, isAcross, span, width, cells.GroupBy(c => m.GroundAt(c.X, c.Y)).OrderByDescending(g => g.Count()).First().Key);
                if (box is { } b && !cells.Any(c => c.X >= b.X0 && c.Y >= b.Y0 && c.X < b.X1 && c.Y < b.Y1)) continue;
                sites.Add(site);
            }
        }
        return sites;
    }

    /// <summary>
    /// A bridge for a crossing, in the map's look: stone arches for cobbles or paving, planks for a track,
    /// reaching half a square onto each bank, as wide as the road. Its arches are found by trying a few
    /// (fewer and wider first) until the bridge maker is happy with them. Null if none suits.
    /// </summary>
    public static (BridgeSpec Spec, BridgeKit Kit)? Bridge(BridgeSite site, string theme, double squareMm)
    {
        double g = squareMm, L = (site.Span + 1) * g, W = Math.Max(8, site.Width * g - 6);
        double scale = 28 * g / 25.4;
        var start = BridgeThemes.Apply(new BridgeSpec { ScaleMm = scale, LengthMm = L, WidthMm = W }, theme);
        if (site.Road == Ground.Dirt)
        {
            var plank = start with { Kind = BridgeKind.Plank, HeightMm = Math.Round(g * 0.4, 1), RiseMm = Math.Round(Math.Min(L * 0.12, g * 0.3), 1), Legs = site.Span >= 4 ? site.Span / 3 : 0, PlankMm = Math.Round(4 * g / 25.4, 1) };
            var kit = BridgeKits.Make(plank);
            return kit.Ok ? (plank, kit) : null;
        }
        double water = site.Span * g;
        for (int n = Math.Max(1, (int)Math.Round(water / (2.6 * g))); n <= 6; n++)
            foreach (var share in new[] { 0.8, 0.65, 0.5 })
                foreach (var arch in new[] { start.Arch, ArchShape.Segmental })
                {
                    double A = Math.Round(water * share / n, 1);
                    double rise = arch switch { ArchShape.Segmental => A / 4, ArchShape.Pointed => Math.Sqrt(0.5) * A, _ => A / 2 };
                    var spec = start with
                    {
                        Kind = BridgeKind.Stone,
                        Arches = n,
                        ArchSpanMm = A,
                        Arch = arch,
                        HeightMm = Math.Round(rise + start.SpringMm + start.RingMm + Math.Max(6, g * 0.25), 1),
                        Surface = site.Road == Ground.Paving ? RoadSurface.Flagstones : RoadSurface.Cobbles,
                    };
                    var kit = BridgeKits.Make(spec);
                    if (kit.Ok) return (spec, kit);
                }
        return null;
    }

    // ---- scenery ----

    /// <summary>
    /// The scenery maker's pieces for the map's (inside the box): a well, a cart, barrels for each pile of
    /// crates, a boulder, and fence sections for its fences (each run in sections of up to four squares).
    /// Trees, lamps, statues, hedges and walls aren't among them. Null if there's nothing it makes.
    /// </summary>
    public static ScatterSet? Scenery(CityMap m, string theme, (int X0, int Y0, int X1, int Y1)? box = null)
    {
        bool In(double x, double y) => box is not { } b || x >= b.X0 && y >= b.Y0 && x < b.X1 && y < b.Y1;
        var props = m.Props.Where(p => In(p.X, p.Y)).ToList();
        double k = m.SquareMm / 25.4;
        var items = new List<ScatterItem>();
        void Add(SItemKind kind, int count, Func<ScatterItem, ScatterItem>? change = null)
        {
            if (count <= 0) return;
            var it = ScatterDefaults.Scaled(ScatterDefaults.For(kind), k) with { Count = count };
            items.Add(change is null ? it : change(it));
        }
        Add(SItemKind.Well, props.Count(p => p.Kind == PropKind.Well));
        Add(SItemKind.Cart, props.Count(p => p.Kind == PropKind.Cart));
        Add(SItemKind.Barrel, props.Count(p => p.Kind == PropKind.Crates) * 3, it => it with { Cluster = 1 });
        Add(SItemKind.Boulder, props.Count(p => p.Kind == PropKind.Boulder));
        // Fences: each straight run in sections of up to four squares, the same lengths together.
        var sections = new Dictionary<int, int>();
        foreach (var f in m.Fences.Where(f => f.Kind == FenceKind.Fence))
            foreach (var (ax, ay, bx, by) in CityScene.Runs(f.Edges))
            {
                if (!In((ax + bx) / 2.0, (ay + by) / 2.0)) continue;
                int len = Math.Abs(bx - ax) + Math.Abs(by - ay);
                while (len > 0)
                {
                    int part = len > 4 ? (len == 5 ? 3 : 4) : len;
                    sections[part] = sections.GetValueOrDefault(part) + 1;
                    len -= part;
                }
            }
        foreach (var (len, count) in sections.OrderByDescending(s => s.Key))
            Add(SItemKind.Fence, count, it => it with { WidthMm = Math.Round(len * m.SquareMm - 1, 1), Style = FenceStyle.PostAndRail });
        if (items.Count == 0) return null;
        return new ScatterSet { Items = items, Theme = theme, ScaleMm = Math.Round(28 * k, 1) };
    }

    // ---- the furniture maker's town pieces ----

    /// <summary>The furniture maker's town piece for a look whose id ends "-<paramref name="end"/>" (the medieval one if the look has none).</summary>
    public static CatalogEntry? Piece(string theme, string end) =>
        FurnitureCatalog.For(theme, FurnitureCatalog.Town).FirstOrDefault(e => e.Id.EndsWith("-" + end, StringComparison.Ordinal))
        ?? FurnitureCatalog.For("Medieval", FurnitureCatalog.Town).FirstOrDefault(e => e.Id.EndsWith("-" + end, StringComparison.Ordinal));

    /// <summary>A set of the furniture maker's pieces in the map's look, from a 2 mm sheet; null if it's empty.</summary>
    static FurnitureSet? Set(string theme, List<FurnitureItem> items) =>
        items.Count == 0 ? null : FurnitureThemes.Apply(new FurnitureSet { Items = items, ThicknessMm = 2, FitMm = 0.1, ScaleMm = 28 }, theme);

    /// <summary>A catalog piece sized for the map's squares (its town pieces are made for 25.4 mm squares) times <paramref name="size"/>.</summary>
    static FurnitureItem Sized(CatalogEntry e, double squareMm, double size = 1, int count = 1)
    {
        double k = squareMm / 25.4 * size;
        var it = Math.Abs(k - 1) < 1e-9 ? e.Item : FurnitureCatalog.Scale(e.Item, k);
        return it with { Count = count };
    }

    static bool InBox((int X0, int Y0, int X1, int Y1)? box, double x, double y) => box is not { } b || x >= b.X0 && y >= b.Y0 && x < b.X1 && y < b.Y1;

    /// <summary>
    /// How the town wall (inside the box) is cut: a corner piece where it turns (an outside one where the turn
    /// goes round the town, an inside one where it goes in), a gatehouse over each road through a gap two
    /// squares wide, a turret at each other end, nothing under a guard tower, and the straight runs in
    /// sections of two squares and one.
    /// </summary>
    public static WallCut Walls(CityMap m, (int X0, int Y0, int X1, int Y1)? box = null)
    {
        static (int X, int Y) End(FenceEdge e) => e.Across ? (e.X + 1, e.Y) : (e.X, e.Y + 1);
        var towers = m.Props.Where(p => p.Kind == PropKind.GuardTower && InBox(box, p.X, p.Y)).ToList();
        bool UnderTower(double x, double y, double margin) => towers.Any(t => Math.Abs(t.X - x) < 0.9 * t.Size + margin && Math.Abs(t.Y - y) < 0.9 * t.Size + margin);
        var edges = m.Fences.Where(f => f.Kind == FenceKind.CityWall).SelectMany(f => f.Edges)
            .Where(e => { var (mx, my) = e.Across ? (e.X + 0.5, (double)e.Y) : (e.X, e.Y + 0.5); return InBox(box, mx, my) && !UnderTower(mx, my, 0); })
            .ToHashSet();
        if (edges.Count == 0) return new(0, 0, 0, 0, 0, 0, towers.Count);
        // The edge leaving a grid point each way.
        (int X, int Y)[] dirs = [(1, 0), (-1, 0), (0, 1), (0, -1)];
        static FenceEdge Toward((int X, int Y) p, (int X, int Y) d) => d switch
        {
            (1, 0) => new(p.X, p.Y, true), (-1, 0) => new(p.X - 1, p.Y, true),
            (0, 1) => new(p.X, p.Y, false), _ => new(p.X, p.Y - 1, false),
        };
        var points = edges.SelectMany(e => new[] { (e.X, e.Y), End(e) }).Distinct().ToList();
        List<(int X, int Y)> Arms((int X, int Y) p) => [.. dirs.Where(d => edges.Contains(Toward(p, d)))];
        double cx = edges.Average(e => e.Across ? e.X + 0.5 : e.X), cy = edges.Average(e => e.Across ? e.Y : e.Y + 0.5);
        var used = new HashSet<FenceEdge>();
        int cornersOut = 0, cornersIn = 0, gates = 0, ends = 0;
        foreach (var p in points.OrderBy(p => p.Y).ThenBy(p => p.X))
        {
            var arms = Arms(p);
            if (arms.Count != 2 || arms[0].X * arms[1].X + arms[0].Y * arms[1].Y != 0) continue;
            var (a, b) = (Toward(p, arms[0]), Toward(p, arms[1]));
            if (used.Contains(a) || used.Contains(b)) continue;
            used.Add(a);
            used.Add(b);
            // The turn goes round the town if the corner's inside faces the wall's middle.
            double dot = (arms[0].X + arms[1].X) * (cx - p.X) + (arms[0].Y + arms[1].Y) * (cy - p.Y);
            if (dot > 0) cornersOut++; else cornersIn++;
        }
        var gated = new HashSet<(int X, int Y)>();
        bool Road(int x, int y) => m.In(x, y) && m.GroundAt(x, y) is Ground.Road or Ground.Paving or Ground.Dirt;
        foreach (var p in points.OrderBy(p => p.Y).ThenBy(p => p.X))
        {
            var arms = Arms(p);
            if (arms.Count != 1 || gated.Contains(p)) continue;
            // A gap two squares wide the other way, the wall going on beyond it and a road through it: a gatehouse.
            var away = (X: -arms[0].X, Y: -arms[0].Y);
            var q = (X: p.X + 2 * away.X, Y: p.Y + 2 * away.Y);
            if (gated.Contains(q) || !Arms(q).Contains(away)) continue;
            if (edges.Contains(Toward(p, away)) || edges.Contains(Toward((p.X + away.X, p.Y + away.Y), away)) || UnderTower(p.X + away.X, p.Y + away.Y, 0)) continue;
            // The squares either side of the gap.
            bool road = Enumerable.Range(0, 2).Any(i =>
            {
                int gx = p.X + away.X * i + (away.X < 0 ? -1 : 0), gy = p.Y + away.Y * i + (away.Y < 0 ? -1 : 0);
                return away.Y == 0 ? Road(gx, gy) || Road(gx, gy - 1) : Road(gx, gy) || Road(gx - 1, gy);
            });
            if (!road) continue;
            gated.Add(p);
            gated.Add(q);
            gates++;
        }
        foreach (var p in points)
        {
            var arms = Arms(p);
            if (arms.Count != 1 || gated.Contains(p) || UnderTower(p.X, p.Y, 0.15)) continue;
            // An end at the edge of the box goes on outside it: no turret.
            if (box is { } bx && (p.X <= bx.X0 || p.Y <= bx.Y0 || p.X >= bx.X1 || p.Y >= bx.Y1)) continue;
            var e = Toward(p, arms[0]);
            if (used.Add(e)) ends++;
        }
        int two = 0, one = 0;
        foreach (var line in edges.Where(e => !used.Contains(e)).GroupBy(e => (e.Across, e.Across ? e.Y : e.X)))
        {
            var at = line.Select(e => e.Across ? e.X : e.Y).OrderBy(v => v).ToList();
            int run = 1;
            for (int i = 1; i <= at.Count; i++)
            {
                if (i < at.Count && at[i] == at[i - 1] + 1) { run++; continue; }
                two += run / 2;
                one += run % 2;
                run = 1;
            }
        }
        return new(two, one, cornersOut, cornersIn, gates, ends, towers.Count);
    }

    /// <summary>The town wall's pieces and guard towers (inside the box) from the furniture maker, in the map's look; null if there are none.</summary>
    public static FurnitureSet? WallSet(CityMap m, (int X0, int Y0, int X1, int Y1)? box = null)
    {
        var cut = Walls(m, box);
        var items = new List<FurnitureItem>();
        foreach (var (end, count) in new[] { ("wall-2", cut.Two), ("wall-1", cut.One), ("wall-corner-out", cut.CornersOut), ("wall-corner-in", cut.CornersIn), ("wall-gate", cut.Gates), ("wall-end", cut.Ends), ("guard-tower", cut.Towers) })
            if (count > 0 && Piece(m.Theme, end) is { } e) items.Add(Sized(e, m.SquareMm, 1, count));
        return Set(m.Theme, items);
    }

    /// <summary>
    /// The map's trees (inside the box) as the furniture maker's, in the map's look (an oak, a palm, a giant
    /// mushroom or a dead tree; a pine where there's one), as big as on the map to the nearest quarter: the
    /// same sizes together. Bushes aren't among them. Null if there are none.
    /// </summary>
    public static FurnitureSet? TreeSet(CityMap m, (int X0, int Y0, int X1, int Y1)? box = null)
    {
        var trees = FurnitureCatalog.For(m.Theme, FurnitureCatalog.Town).Where(e => e.Item.Kind == FItemKind.Tree).ToList();
        if (trees.Count == 0) trees = [.. FurnitureCatalog.For("Medieval", FurnitureCatalog.Town).Where(e => e.Item.Kind == FItemKind.Tree)];
        if (trees.Count == 0) return null;
        var broad = trees.FirstOrDefault(e => e.Item.Tree != TreeStyle.Pine) ?? trees[0];
        var pine = trees.FirstOrDefault(e => e.Item.Tree == TreeStyle.Pine) ?? broad;
        var items = m.Props.Where(p => p.Kind is PropKind.Tree or PropKind.Pine && InBox(box, p.X, p.Y))
            .GroupBy(p => (p.Kind, Size: Math.Max(0.75, Math.Round(p.Size * 4) / 4)))
            .OrderBy(g => g.Key.Kind).ThenBy(g => g.Key.Size)
            .Select(g =>
            {
                var e = g.Key.Kind == PropKind.Pine ? pine : broad;
                // As wide as its crown on the map: an oak's about 1.1 squares across at size 1, a pine's 0.85.
                double across = (g.Key.Kind == PropKind.Pine ? 0.85 : 1.1) * g.Key.Size * 25.4;
                return Sized(e, m.SquareMm, Math.Max(0.6, across / e.Item.WidthMm), g.Count()) with { Name = $"{e.Name} ({g.Key.Size:0.##} squares)" };
            }).ToList();
        return Set(m.Theme, items);
    }

    /// <summary>The map's horse troughs, stages, market stalls, outdoor bars, notice boards, fountains and gallows (inside the box) from the furniture maker; null if there are none.</summary>
    public static FurnitureSet? TownPropSet(CityMap m, (int X0, int Y0, int X1, int Y1)? box = null)
    {
        var items = new List<FurnitureItem>();
        foreach (var (kind, end) in new[] { (PropKind.Stage, "stage"), (PropKind.Stall, "market-stall"), (PropKind.Bar, "bar"), (PropKind.Trough, "horse-trough"), (PropKind.NoticeBoard, "notice-board"), (PropKind.Fountain, "fountain"), (PropKind.Gallows, "gallows") })
            foreach (var g in m.Props.Where(p => p.Kind == kind && InBox(box, p.X, p.Y)).GroupBy(p => Math.Round(p.Size * 4) / 4).OrderBy(g => g.Key))
                if (Piece(m.Theme, end) is { } e) items.Add(Sized(e, m.SquareMm, g.Key, g.Count()));
        return Set(m.Theme, items);
    }
}
