using PhotonCanvas.Addon.Bridges;
using PhotonCanvas.Addon.Scatter;
using ScatterDefaults = PhotonCanvas.Addon.Scatter.Defaults;

namespace PhotonCanvas.Addon.CityBuilder;

/// <summary>Where a road crosses water: its squares, how many squares the water is across (the way the road runs) and how wide the road is.</summary>
public sealed record BridgeSite(HashSet<(int X, int Y)> Cells, bool Across, int Span, int Width, Ground Road)
{
    public (double X, double Y) Middle => (Cells.Average(c => c.X) + 0.5, Cells.Average(c => c.Y) + 0.5);
}

/// <summary>
/// The rest of a town's pieces besides the buildings and the ground: a bridge from the bridge maker wherever
/// a road crosses water (stone arches for a cobbled road, planks for a track), and the scenery maker's
/// wells, carts, barrels, boulders and fences for the map's own.
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
        void Add(ItemKind kind, int count, Func<ScatterItem, ScatterItem>? change = null)
        {
            if (count <= 0) return;
            var it = ScatterDefaults.Scaled(ScatterDefaults.For(kind), k) with { Count = count };
            items.Add(change is null ? it : change(it));
        }
        Add(ItemKind.Well, props.Count(p => p.Kind == PropKind.Well));
        Add(ItemKind.Cart, props.Count(p => p.Kind == PropKind.Cart));
        Add(ItemKind.Barrel, props.Count(p => p.Kind == PropKind.Crates) * 3, it => it with { Cluster = 1 });
        Add(ItemKind.Boulder, props.Count(p => p.Kind == PropKind.Boulder));
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
            Add(ItemKind.Fence, count, it => it with { WidthMm = Math.Round(len * m.SquareMm - 1, 1), Style = FenceStyle.PostAndRail });
        if (items.Count == 0) return null;
        return new ScatterSet { Items = items, Theme = theme, ScaleMm = Math.Round(28 * k, 1) };
    }
}
