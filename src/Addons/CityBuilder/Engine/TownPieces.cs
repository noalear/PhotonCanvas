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
public sealed record WallCut(int Two, int One, int CornersOut, int CornersIn, int Gates, int Ends, int Towers, int Half = 0)
{
    public int Count => Two + One + CornersOut + CornersIn + Gates + Ends + Towers + Half;
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
        // The roads laid over the ground, their squares over the water too.
        m = m.Paved();
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
        m = m.Tidied();
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
        foreach (var piece in WallLayout.Of(m, f => f.Kind == FenceKind.Fence).Pieces)
            foreach (var (a, b) in Segs(piece))
            {
                if (!In((a.X + b.X) / 2, (a.Y + b.Y) / 2)) continue;
                int len = (int)Math.Round(a.DistanceTo(b));
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

    static IEnumerable<(CityPt A, CityPt B)> Segs(WallPiece p)
    {
        for (int i = 0; i + 1 < p.Points.Count; i++) yield return (p.Points[i], p.Points[i + 1]);
        if (p.Closed && p.Points.Count > 2) yield return (p.Points[^1], p.Points[0]);
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
    /// goes round the town, an inside one where it goes in), a gatehouse over each road through it, a turret at
    /// each other end, nothing under a guard tower, and the straight runs in sections of two squares and one (and
    /// half a square where a wall laid on half squares needs one).
    /// </summary>
    public static WallCut Walls(CityMap m, (int X0, int Y0, int X1, int Y1)? box = null)
    {
        m = m.Tidied();
        var towers = m.Props.Where(p => p.Kind == PropKind.GuardTower && InBox(box, p.X, p.Y)).ToList();
        var layout = WallLayout.Of(m, f => f.Kind == FenceKind.CityWall);
        if (layout.Pieces.Count == 0) return new(0, 0, 0, 0, 0, 0, towers.Count);
        // The middle of the town walls, for telling which way an open stretch's corners turn.
        var all = m.Fences.Where(f => f.Kind == FenceKind.CityWall).SelectMany(f => f.Points).ToList();
        var centre = new CityPt(all.Average(p => p.X), all.Average(p => p.Y));
        var capped = layout.Ends.Where(e => e.Kind == WallEndKind.Cap).Select(e => e.At).ToList();
        bool IsCap(CityPt p) => capped.Any(c => c.DistanceTo(p) < 1e-6);
        int two = 0, one = 0, half = 0, cornersOut = 0, cornersIn = 0, ends = 0;
        foreach (var piece in layout.Pieces)
        {
            double area = 0;
            if (piece.Fence.Closed)
                for (int i = 0; i < piece.Fence.Points.Count; i++) area += piece.Fence.Points[i].Cross(piece.Fence.Points[(i + 1) % piece.Fence.Points.Count]);
            foreach (var (pts, closed, openStart, openEnd) in ClipToBox(piece, box))
            {
                int n = pts.Count;
                int segCount = closed ? n : n - 1;
                var corner = new bool[n];
                for (int i = 0; i < n; i++)
                {
                    if (!closed && (i == 0 || i == n - 1)) continue;
                    var a = pts[(i - 1 + n) % n];
                    var b = pts[(i + 1) % n];
                    var din = (pts[i] - a).Unit;
                    var dout = (b - pts[i]).Unit;
                    if (din.Dot(dout) > Math.Cos(10 * Math.PI / 180)) continue;
                    corner[i] = true;
                    bool outside = piece.Fence.Closed && Math.Abs(area) > 1e-9
                        ? Math.Sign(din.Cross(dout)) == Math.Sign(area)
                        : (dout - din).Dot(centre - pts[i]) > 0;
                    if (outside) cornersOut++; else cornersIn++;
                }
                bool capStart = !closed && !openStart && IsCap(pts[0]), capEnd = !closed && !openEnd && IsCap(pts[^1]);
                if (capStart) ends++;
                if (capEnd) ends++;
                for (int i = 0; i < segCount; i++)
                {
                    int j = (i + 1) % n;
                    double len = pts[i].DistanceTo(pts[j]);
                    if (corner[i]) len -= 1;
                    if (corner[j]) len -= 1;
                    if (i == 0 && capStart) len -= 1;
                    if (j == n - 1 && !closed && capEnd) len -= 1;
                    if (len < 0.2) continue;
                    int twos = (int)Math.Floor(len / 2 + 1e-6);
                    double rest = len - 2 * twos;
                    two += twos;
                    if (rest >= 1 - 1e-6) { one++; rest -= 1; }
                    if (rest >= 0.25) half++;
                }
            }
        }
        int gates = layout.Gates.Count(g => InBox(box, g.Middle.X, g.Middle.Y));
        return new(two, one, cornersOut, cornersIn, gates, ends, towers.Count, half);
    }

    /// <summary>How wide each gate through the town wall (inside the box) is, in whole squares.</summary>
    public static List<int> GateWidths(CityMap m, (int X0, int Y0, int X1, int Y1)? box = null) =>
        [.. WallLayout.Of(m, f => f.Kind == FenceKind.CityWall).Gates.Where(g => InBox(box, g.Middle.X, g.Middle.Y)).Select(g => Math.Max(1, (int)Math.Round(g.Width)))];

    /// <summary>
    /// A stretch of wall as the parts of it inside the box (all of it when there's none): each part's points, whether
    /// it still goes all the way round, and whether its start and end were cut by the box (going on outside it).
    /// </summary>
    static IEnumerable<(List<CityPt> Points, bool Closed, bool OpenStart, bool OpenEnd)> ClipToBox(WallPiece piece, (int X0, int Y0, int X1, int Y1)? box)
    {
        if (box is not { } b)
        {
            yield return (piece.Points, piece.Closed, false, false);
            yield break;
        }
        var rect = new[] { new CityPt(b.X0, b.Y0), new CityPt(b.X1, b.Y0), new CityPt(b.X1, b.Y1), new CityPt(b.X0, b.Y1) };
        var pts = piece.Closed ? [.. piece.Points, piece.Points[0]] : piece.Points;
        List<CityPt>? cur = null;
        bool curOpenStart = false;
        for (int i = 0; i + 1 < pts.Count; i++)
        {
            var (a, c) = (pts[i], pts[i + 1]);
            if (Geometry.Clip(a, c, rect) is not { } t || t.T1 - t.T0 < 1e-9)
            {
                if (cur is not null) { yield return (cur, false, curOpenStart, true); cur = null; }
                continue;
            }
            var p0 = a + (c - a) * t.T0;
            var p1 = a + (c - a) * t.T1;
            if (cur is null || t.T0 > 1e-9)
            {
                if (cur is not null) yield return (cur, false, curOpenStart, true);
                cur = [p0];
                curOpenStart = t.T0 > 1e-9;
            }
            cur.Add(p1);
            if (t.T1 < 1 - 1e-9)
            {
                yield return (cur, false, curOpenStart, true);
                cur = null;
            }
        }
        if (cur is not null)
        {
            // All of a ring inside the box is still a ring.
            if (piece.Closed && !curOpenStart && cur.Count == pts.Count) yield return (piece.Points, true, false, false);
            else yield return (cur, false, curOpenStart, false);
        }
    }

    /// <summary>The town wall's pieces and guard towers (inside the box) from the furniture maker, in the map's look; null if there are none.</summary>
    public static FurnitureSet? WallSet(CityMap m, (int X0, int Y0, int X1, int Y1)? box = null)
    {
        var cut = Walls(m, box);
        var items = new List<FurnitureItem>();
        var gates = GateWidths(m, box).GroupBy(w => w).OrderBy(g => g.Key).ToList();
        foreach (var (end, count) in new[] { ("wall-2", cut.Two), ("wall-1", cut.One), ("wall-corner-out", cut.CornersOut), ("wall-corner-in", cut.CornersIn), ("wall-gate", cut.Gates), ("wall-end", cut.Ends), ("guard-tower", cut.Towers) })
        {
            if (count <= 0 || Piece(m.Theme, end) is not { } e) continue;
            if (end == "wall-gate" && gates.Count > 0)
            {
                // A gatehouse as wide as each gap: two squares for a lane, four for a high road.
                foreach (var g in gates)
                {
                    var it = Sized(e, m.SquareMm, 1, g.Count());
                    items.Add(g.Key == 2 ? it : it with { WidthMm = Math.Round(it.WidthMm * g.Key / 2.0, 1), Name = $"{e.Name} ({g.Key} squares)" });
                }
                continue;
            }
            items.Add(Sized(e, m.SquareMm, 1, count));
        }
        if (cut.Half > 0 && Piece(m.Theme, "wall-1") is { } h)
        {
            var it = Sized(h, m.SquareMm, 1, cut.Half);
            items.Add(it with { WidthMm = Math.Round(it.WidthMm / 2, 1), Name = $"{h.Name} (half a square)" });
        }
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

    /// <summary>The map's horse troughs, stages, market stalls, outdoor bars, notice boards, fountains, gallows, street lamps and statues (inside the box) from the furniture maker; null if there are none.</summary>
    public static FurnitureSet? TownPropSet(CityMap m, (int X0, int Y0, int X1, int Y1)? box = null)
    {
        var items = new List<FurnitureItem>();
        foreach (var (kind, end) in new[] { (PropKind.Stage, "stage"), (PropKind.Stall, "market-stall"), (PropKind.Bar, "bar"), (PropKind.Trough, "horse-trough"), (PropKind.NoticeBoard, "notice-board"), (PropKind.Fountain, "fountain"), (PropKind.Gallows, "gallows"), (PropKind.Lamp, "street-lamp"), (PropKind.Statue, "statue") })
            foreach (var g in m.Props.Where(p => p.Kind == kind && InBox(box, p.X, p.Y)).GroupBy(p => Math.Round(p.Size * 4) / 4).OrderBy(g => g.Key))
                if (Piece(m.Theme, end) is { } e) items.Add(Sized(e, m.SquareMm, g.Key, g.Count()));
        return Set(m.Theme, items);
    }
}
