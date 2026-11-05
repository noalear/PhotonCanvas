namespace PhotonCanvas.Addon.CityBuilder;

/// <summary>How a made-up town's streets run.</summary>
public enum TownLayout { Crossroads, MainStreet, Grid, Winding, Riverside }

/// <summary>What to make a town of: its size and look, how its streets run, how full they are, and what else it has.</summary>
public sealed record TownOptions
{
    public string Name { get; init; } = "New town";
    public int Across { get; init; } = 48;
    public int Down { get; init; } = 36;
    public string Theme { get; init; } = "Medieval";
    public double SquareMm { get; init; } = 25.4;
    public TownLayout Layout { get; init; } = TownLayout.Crossroads;
    /// <summary>How full the streets are: 0, a house here and there; 1, packed side by side.</summary>
    public double Density { get; init; } = 0.6;
    /// <summary>A town wall round it, gates where the roads go through, towers at its corners.</summary>
    public bool Walls { get; init; }
    /// <summary>A river through it (a riverside town always has one).</summary>
    public bool River { get; init; }
    /// <summary>A paved market square where the main roads meet, a temple and a hall on it.</summary>
    public bool Square { get; init; } = true;
    public int Seed { get; init; } = 1;
    /// <summary>Saved buildings to put up where one suits a plot (its district and type, then its size).</summary>
    public IReadOnlyList<PrefabInfo> Prefabs { get; init; } = [];
}

/// <summary>
/// Makes a town for you: roads laid out as asked, a market square, a river with bridges, a town wall with gates,
/// and plots along every road filled with buildings of the right kind for their part of town (shops and taverns
/// round the square, houses further out, warehouses by the river, farms outside the walls), saved buildings where
/// one suits a plot; then fields, trees, lamps, a well and street names. Also fills an existing map's roads with
/// buildings wherever there's room.
/// </summary>
public static class TownMaker
{
    /// <summary>A plot's part of town and the kinds of building in it (with how often each comes up).</summary>
    static readonly Dictionary<string, (string Type, double Weight)[]> Kinds = new()
    {
        ["Market"] = [("Shop", 5), ("Tavern", 2), ("Inn", 1), ("Smithy", 1), ("Workshop", 2)],
        ["Residential"] = [("House", 8), ("Cottage", 3), ("Workshop", 1), ("Shop", 1), ("Tavern", 0.3), ("Chapel", 0.12)],
        ["Noble"] = [("Manor", 3), ("House", 2), ("Chapel", 0.4)],
        ["Slums"] = [("Hovel", 5), ("House", 2), ("Tavern", 0.5)],
        ["Docks"] = [("Warehouse", 4), ("Tavern", 1), ("House", 2), ("Workshop", 1)],
        ["Farm"] = [("Barn", 2), ("House", 2), ("Stable", 1), ("Mill", 0.5)],
        ["Military"] = [("Guardhouse", 2), ("Tower", 1)],
        ["Temple"] = [("Chapel", 2), ("Shrine", 2), ("House", 1)],
        ["Industrial"] = [("Workshop", 3), ("Warehouse", 2), ("Smithy", 2), ("Mill", 1)],
        ["Outskirts"] = [("Cottage", 3), ("Barn", 1), ("House", 2), ("Stable", 1)],
        ["Wilderness"] = [("Hovel", 2), ("Cottage", 2)],
    };

    /// <summary>Each kind's frontage and depth (squares, least and most) and floors (least and most).</summary>
    static (int F0, int F1, int D0, int D1, int S0, int S1) Size(string type) => type switch
    {
        "Hovel" => (3, 3, 2, 3, 1, 1),
        "Cottage" => (3, 4, 3, 3, 1, 1),
        "House" or "Hab block" => (3, 5, 3, 4, 1, 2),
        "Canteen" => (5, 6, 4, 5, 1, 2),
        "Hangar" => (5, 6, 4, 5, 1, 1),
        "Bunker" => (3, 4, 3, 4, 1, 1),
        "Shop" => (3, 4, 3, 4, 2, 2),
        "Workshop" or "Smithy" => (4, 5, 3, 4, 1, 1),
        "Tavern" or "Inn" => (5, 6, 4, 5, 2, 2),
        "Manor" => (6, 8, 5, 6, 2, 3),
        "Warehouse" => (5, 7, 4, 5, 1, 2),
        "Barn" => (5, 6, 4, 5, 1, 1),
        "Stable" => (4, 6, 3, 3, 1, 1),
        "Mill" => (3, 3, 3, 3, 2, 2),
        "Temple" => (5, 7, 6, 9, 1, 1),
        "Chapel" => (4, 5, 6, 7, 1, 1),
        "Hall" => (5, 8, 4, 6, 2, 2),
        "Guardhouse" => (3, 4, 3, 4, 2, 2),
        "Shrine" => (3, 3, 2, 3, 1, 1),
        "Tower" => (3, 3, 3, 3, 3, 3),
        _ => (3, 5, 3, 4, 1, 2),
    };

    /// <summary>What a medieval building is roofed with, by its kind (other looks keep their own).</summary>
    static string? Covering(string type, string theme, Random rnd)
    {
        if (!string.Equals(theme, "Medieval", StringComparison.OrdinalIgnoreCase)) return null;
        return type switch
        {
            "Hovel" or "Cottage" or "Barn" or "Stable" => "Thatch",
            "Manor" or "Temple" or "Chapel" or "Hall" or "Tower" or "Guardhouse" => "Slate",
            "Warehouse" or "Mill" => "Wooden shingles",
            _ => rnd.NextDouble() < 0.5 ? "Clay tiles" : rnd.NextDouble() < 0.5 ? "Wooden shingles" : "Thatch",
        };
    }

    static readonly string[] Adjectives = ["Golden", "Silver", "Red", "Green", "Crooked", "Drunken", "Sleeping", "Laughing", "Old", "Black", "Blue", "Merry", "Wandering", "Rusty"];
    static readonly string[] Nouns = ["Goose", "Stag", "Lantern", "Barrel", "Anchor", "Boar", "Crown", "Kettle", "Fox", "Bell", "Plough", "Wheel", "Hound", "Tankard"];
    static readonly string[] Trades = ["Baker", "Butcher", "Chandler", "Cooper", "Tailor", "Apothecary", "Cobbler", "Weaver", "Potter", "Fletcher", "Glassblower", "Cartwright", "Tanner", "Jeweller"];
    static readonly string[] Families = ["Ashford", "Blackwood", "Thorne", "Hale", "Marsh", "Whitlock", "Graves", "Fairfax", "Crane", "Holloway"];
    static readonly string[] Streets = ["High Street", "King's Road", "Market Street", "Mill Lane", "Church Lane", "Bridge Street", "Castle Way", "Water Lane", "Baker's Row", "Tanner's Row", "Gate Street", "Well Lane", "Chapel Row", "Cart Lane"];
    static readonly string[] Rivers = ["River Wend", "River Ashe", "River Moll", "River Tarn", "River Brede"];

    /// <summary>A name for a building of a kind (numbering houses and the like by <paramref name="n"/>).</summary>
    static string NameFor(string type, int n, Random rnd) => type switch
    {
        "Tavern" or "Inn" => $"The {Adjectives[rnd.Next(Adjectives.Length)]} {Nouns[rnd.Next(Nouns.Length)]}",
        "Shop" => $"{Trades[rnd.Next(Trades.Length)]}'s shop",
        "Workshop" => $"{Trades[rnd.Next(Trades.Length)]}'s workshop",
        "Manor" => $"{Families[rnd.Next(Families.Length)]} Manor",
        "Temple" => "Temple",
        "Hall" => "Town hall",
        "Smithy" => "Smithy",
        "Mill" => "Mill",
        _ => $"{type} {n}",
    };

    // ---- a whole town ----

    /// <summary>A whole town on a new map, and notes on what was made.</summary>
    public static (CityMap Map, List<string> Notes) Make(TownOptions o)
    {
        var rnd = new Random(o.Seed);
        int w = Math.Clamp(o.Across, 16, CityMap.MaxSide), d = Math.Clamp(o.Down, 16, CityMap.MaxSide);
        var g = new Ground[w, d];
        int cx = w / 2 + rnd.Next(-w / 12, w / 12 + 1), cy = d / 2 + rnd.Next(-d / 12, d / 12 + 1);
        bool riverside = o.Layout == TownLayout.Riverside;

        // The town wall's line (grid lines): plots inside it, farms outside.
        int inset = o.Walls ? Math.Clamp(Math.Min(w, d) / 8, 3, 12) : 0;
        var wall = (X0: inset, Y0: inset, X1: w - inset, Y1: d - inset);
        bool Inside(int x, int y) => !o.Walls || (x > wall.X0 && x < wall.X1 - 1 && y > wall.Y0 && y < wall.Y1 - 1);

        // The river: down one side, or along the bottom for a riverside town.
        var water = new HashSet<(int X, int Y)>();
        if (o.River || riverside)
        {
            double width = Math.Clamp(Math.Min(w, d) / 14.0, 2, 5);
            double phase = rnd.NextDouble() * 6;
            if (riverside)
            {
                double baseY = d - inset - Math.Max(4, d / 7.0);
                for (int x = 0; x < w; x++)
                {
                    double mid = baseY + 1.2 * Math.Sin(x / 7.0 + phase);
                    for (int y = 0; y < d; y++) if (Math.Abs(y + 0.5 - mid) < width / 2) water.Add((x, y));
                }
            }
            else
            {
                double baseX = cx + (w - cx) * 0.55;
                for (int y = 0; y < d; y++)
                {
                    double mid = baseX + 1.6 * Math.Sin(y / 6.0 + phase);
                    for (int x = 0; x < w; x++) if (Math.Abs(x + 0.5 - mid) < width / 2) water.Add((x, y));
                }
            }
            foreach (var (x, y) in water) g[x, y] = Ground.Water;
        }

        // The roads.
        var roads = new List<(HashSet<(int X, int Y)> Cells, bool Main, bool Across)>();
        void Road(int ax, int ay, int bx, int by, int width, bool main)
        {
            var cells = CityEdit.Road(ax, ay, bx, by, width, straight: false).Where(c => c.X >= 0 && c.Y >= 0 && c.X < w && c.Y < d).ToHashSet();
            // A lane running the way the river does stops at its bank instead of running down it (and isn't
            // laid at all if it starts in it); lanes across it cross it, and get a bridge.
            bool across = Math.Abs(bx - ax) >= Math.Abs(by - ay);
            if (!main && water.Count > 0 && across == riverside)
            {
                var wet = cells.Where(water.Contains).Select(c => Math.Abs(c.X - ax) + Math.Abs(c.Y - ay)).DefaultIfEmpty(int.MaxValue).Min();
                cells = cells.Where(c => Math.Abs(c.X - ax) + Math.Abs(c.Y - ay) < wet).ToHashSet();
                if (cells.Count < 3) return;
            }
            if (cells.Count == 0) return;
            foreach (var (x, y) in cells) g[x, y] = Ground.Road;
            roads.Add((cells, main, across));
        }
        int t0x = Math.Max(1, wall.X0 + 2), t1x = Math.Min(w - 2, wall.X1 - 3), t0y = Math.Max(1, wall.Y0 + 2), t1y = Math.Min(d - 2, wall.Y1 - 3);
        int Between(int a, int b) => a >= b ? a : rnd.Next(a, b + 1);
        switch (o.Layout)
        {
            case TownLayout.Crossroads:
            {
                Road(0, cy, w - 1, cy, 2, true);
                Road(cx, 0, cx, d - 1, 2, true);
                // Lanes off the main roads in each quarter, one every 8 to 11 squares out to the edge of town,
                // the further ones not always running all the way.
                foreach (var (sx, sy) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
                {
                    for (int k = 0, ly = cy + sy * Between(6, 9); ly > t0y && ly < t1y; k++, ly += sy * Between(8, 11))
                    {
                        int end = sx < 0 ? t0x + rnd.Next(0, 4) : t1x - rnd.Next(0, 4);
                        if (k > 0) end = cx + (int)((end - cx) * (0.55 + rnd.NextDouble() * 0.45));
                        if (Math.Abs(end - cx) > 3) Road(cx, ly, end, ly, 1, false);
                    }
                    for (int k = 0, lx = cx + sx * Between(6, 9); lx > t0x && lx < t1x; k++, lx += sx * Between(8, 11))
                    {
                        int end = sy < 0 ? t0y + rnd.Next(0, 4) : t1y - rnd.Next(0, 4);
                        if (k > 0) end = cy + (int)((end - cy) * (0.55 + rnd.NextDouble() * 0.45));
                        if (Math.Abs(end - cy) > 3) Road(lx, cy, lx, end, 1, false);
                    }
                }
                break;
            }
            case TownLayout.MainStreet:
            {
                Road(0, cy, w - 1, cy, 3, true);
                for (int x = t0x + Between(3, 6); x < t1x - 2; x += Between(8, 11))
                {
                    if (rnd.NextDouble() < 0.85) Road(x, cy, x, cy - (int)((cy - t0y) * (0.45 + rnd.NextDouble() * 0.55)), 1, false);
                    if (rnd.NextDouble() < 0.85) Road(x + rnd.Next(-2, 3), cy, x, cy + (int)((t1y - cy) * (0.45 + rnd.NextDouble() * 0.55)), 1, false);
                }
                // A big town has back streets along it too, a dozen or so squares apart.
                foreach (int sy in new[] { -1, 1 })
                {
                    int room = sy < 0 ? cy - t0y : t1y - cy;
                    if (room < 24) continue;
                    for (int y = cy + sy * Between(11, 14); sy < 0 ? y > t0y + 4 : y < t1y - 4; y += sy * Between(11, 14))
                        Road(t0x + rnd.Next(2, 8), y, t1x - rnd.Next(2, 8), y, 1, false);
                }
                break;
            }
            case TownLayout.Grid:
            {
                int step = Math.Clamp(Math.Min(w, d) / 5, 8, 12);
                Road(0, cy, w - 1, cy, 2, true);
                Road(cx, 0, cx, d - 1, 2, true);
                for (int y = cy - step; y > t0y; y -= step) Road(t0x, y, t1x, y, 1, false);
                for (int y = cy + step; y < t1y; y += step) Road(t0x, y, t1x, y, 1, false);
                for (int x = cx - step; x > t0x; x -= step) Road(x, t0y, x, t1y, 1, false);
                for (int x = cx + step; x < t1x; x += step) Road(x, t0y, x, t1y, 1, false);
                break;
            }
            case TownLayout.Winding:
            {
                // A main road wandering across, laid a column at a time, and lanes off it.
                double amp = Math.Max(2, d / 9.0), period = Math.Max(6, w / 4.5), phase = rnd.NextDouble() * 6;
                int Yat(int x) => Math.Clamp((int)Math.Round(cy + amp * Math.Sin(x / period + phase)), 1, d - 3);
                var cells = new HashSet<(int X, int Y)>();
                for (int x = 0; x < w; x++)
                {
                    int y0 = Yat(x), y1 = x + 1 < w ? Yat(x + 1) : y0;
                    for (int y = Math.Min(y0, y1); y <= Math.Max(y0, y1); y++) { cells.Add((x, y)); cells.Add((x, y + 1)); }
                }
                cells = cells.Where(c => c.Y >= 0 && c.Y < d).ToHashSet();
                foreach (var (x, y) in cells) g[x, y] = Ground.Road;
                roads.Add((cells, true, true));
                cy = Yat(cx);
                for (int x = t0x + Between(4, 7); x < t1x - 2; x += Between(9, 13))
                {
                    int y = Yat(x);
                    if (rnd.NextDouble() < 0.8) Road(x, y, x + rnd.Next(-3, 4), Math.Max(t0y, y - (int)((y - t0y) * (0.5 + rnd.NextDouble() * 0.5))), 1, false);
                    if (rnd.NextDouble() < 0.8) Road(x, y + 1, x + rnd.Next(-3, 4), Math.Min(t1y, y + 1 + (int)((t1y - y) * (0.5 + rnd.NextDouble() * 0.5))), 1, false);
                }
                // A big town has more lanes wandering along it, crossing those.
                for (int k = 1; k < 30; k++)
                {
                    int off = (k + 1) / 2 * Between(13, 16) * (k % 2 == 1 ? -1 : 1);
                    double ph = rnd.NextDouble() * 6, am = amp * (0.4 + rnd.NextDouble() * 0.4);
                    int Lane(int x) => (int)Math.Round(cy + off + am * Math.Sin(x / period + ph));
                    if (Lane(w / 2) <= t0y + 3 || Lane(w / 2) >= t1y - 3) { if (Math.Abs(off) > d) break; continue; }
                    var lane = new HashSet<(int X, int Y)>();
                    for (int x = t0x + rnd.Next(1, 6); x <= t1x - rnd.Next(1, 6); x++)
                    {
                        int y0 = Lane(x), y1 = Lane(x + 1);
                        for (int y = Math.Min(y0, y1); y <= Math.Max(y0, y1); y++)
                            if (y > t0y && y < t1y) lane.Add((x, y));
                    }
                    if (lane.Count < 8) continue;
                    foreach (var (x, y) in lane) g[x, y] = Ground.Road;
                    roads.Add((lane, false, true));
                }
                break;
            }
            case TownLayout.Riverside:
            {
                // A quay along the river, a main road along the town, and lanes down to the water.
                int quayTop = water.Count > 0 ? water.Min(c => c.Y) - 2 : d - 6;
                for (int x = 0; x < w; x++)
                {
                    int top = water.Where(c => c.X == x).Select(c => c.Y).DefaultIfEmpty(quayTop + 2).Min();
                    for (int y = Math.Max(0, top - 2); y < top; y++) g[x, y] = Ground.Paving;
                }
                int main = Math.Clamp(quayTop - Between(6, 8), t0y + 3, d - 4);
                cy = main;
                Road(0, main, w - 1, main, 2, true);
                // Back streets along the town above it, as many as there's room for.
                var backs = new List<int>();
                for (int y = main - Between(9, 11); y > t0y + 3; y -= Between(9, 11))
                {
                    int x0 = t0x + rnd.Next(0, 5), x1 = t1x - rnd.Next(0, 5);
                    Road(x0, y, x1, y, 1, false);
                    backs.Add(y);
                }
                for (int x = t0x + Between(3, 6); x < t1x - 2; x += Between(8, 11))
                {
                    Road(x, main + 2, x, quayTop, 1, false);
                    int end = backs.Count > 0 ? backs[^1] : main - (int)((main - t0y) * (0.5 + rnd.NextDouble() * 0.5));
                    if (rnd.NextDouble() < 0.75) Road(x + rnd.Next(-2, 3), main, x, Math.Max(t0y, end), 1, false);
                }
                break;
            }
        }

        // Bridges: wherever a road crosses the river it stays road (paved over the water).
        foreach (var r in roads)
            foreach (var c in r.Cells)
                if (water.Contains(c)) g[c.X, c.Y] = Ground.Road;

        // The market square where the main roads meet.
        var square = (X0: 0, Y0: 0, X1: -1, Y1: -1);
        if (o.Square)
        {
            int sw = Math.Clamp(w / 6, 6, 14), sd = Math.Clamp(d / 6, 5, 10);
            square = (Math.Clamp(cx - sw / 2, 1, w - sw - 1), Math.Clamp(cy - sd / 2 + 1, 1, d - sd - 1), 0, 0);
            square = (square.X0, square.Y0, square.X0 + sw - 1, square.Y0 + sd - 1);
            for (int y = square.Y0; y <= square.Y1; y++)
                for (int x = square.X0; x <= square.X1; x++)
                    if (g[x, y] != Ground.Water) g[x, y] = Ground.Paving;
        }
        bool OnSquare(int x, int y) => x >= square.X0 && x <= square.X1 && y >= square.Y0 && y <= square.Y1;
        // How far out from the middle a square is: 0 there, 1 at the town's edge (its wall, or the map's) that way.
        double Far(int x, int y)
        {
            double ex = x >= cx ? (o.Walls ? wall.X1 : w) - cx : cx - (o.Walls ? wall.X0 : 0);
            double ey = y >= cy ? (o.Walls ? wall.Y1 : d) - cy : cy - (o.Walls ? wall.Y0 : 0);
            return Math.Max(Math.Abs(x - cx) / Math.Max(1, ex), Math.Abs(y - cy) / Math.Max(1, ey));
        }

        // The town wall, with gates where the roads go through.
        var fences = new List<Fence>();
        var buildings = new List<CityBuilding>();
        var taken = new HashSet<(int X, int Y)>();
        var notes = new List<string>();
        var towers = new List<Prop>();
        int gates = 0;
        if (o.Walls)
        {
            var edges = new List<FenceEdge>();
            Ground At(int x, int y) => x >= 0 && y >= 0 && x < w && y < d ? g[x, y] : Ground.Grass;
            // Along each side: an edge of wall unless a road (a gate) or the river goes through.
            void Side(IEnumerable<FenceEdge> side)
            {
                bool inGate = false;
                foreach (var e in side)
                {
                    var (a, b) = e.Across ? (At(e.X, e.Y), At(e.X, e.Y - 1)) : (At(e.X, e.Y), At(e.X - 1, e.Y));
                    bool road = a == Ground.Road || b == Ground.Road, river = a == Ground.Water || b == Ground.Water;
                    if (road && !inGate) gates++;
                    inGate = road;
                    if (!road && !river) edges.Add(e);
                }
            }
            foreach (int y in new[] { wall.Y0, wall.Y1 }) Side(Enumerable.Range(wall.X0, wall.X1 - wall.X0).Select(x => new FenceEdge(x, y, true)));
            foreach (int x in new[] { wall.X0, wall.X1 }) Side(Enumerable.Range(wall.Y0, wall.Y1 - wall.Y0).Select(y => new FenceEdge(x, y, false)));
            // Guard towers astride the wall every dozen squares or so, clear of the corners, gates and river:
            // each on the 2 × 2 squares round a grid point on the wall, the wall either side meeting it.
            bool Clear(int px, int py) => Enumerable.Range(px - 3, 6).All(x => Enumerable.Range(py - 3, 6).All(y => At(x, y) is not (Ground.Road or Ground.Water)));
            foreach (bool across in new[] { true, false })
                foreach (int line in across ? new[] { wall.Y0, wall.Y1 } : new[] { wall.X0, wall.X1 })
                {
                    int from = across ? wall.X0 : wall.Y0, to = across ? wall.X1 : wall.Y1, length = to - from;
                    int n = length / 13;
                    for (int i = 1; i <= n; i++)
                    {
                        int at = from + (int)Math.Round(length * i / (n + 1.0));
                        var (px, py) = across ? (at, line) : (line, at);
                        if (at - from < 5 || to - at < 5 || !Clear(px, py)) continue;
                        var under = across ? new[] { new FenceEdge(px - 1, py, true), new FenceEdge(px, py, true) } : [new FenceEdge(px, py - 1, false), new FenceEdge(px, py, false)];
                        if (!under.All(edges.Contains)) continue;
                        edges.RemoveAll(under.Contains);
                        towers.Add(new Prop { Kind = PropKind.GuardTower, X = px, Y = py });
                        foreach (var c in Cells(px - 1, py - 1, 2, 2)) taken.Add(c);
                    }
                }
            fences.Add(new Fence { Kind = FenceKind.CityWall, Edges = edges });
            // Towers in the corners, inside the wall.
            foreach (var (tx, ty) in new[] { (wall.X0, wall.Y0), (wall.X1 - 3, wall.Y0), (wall.X0, wall.Y1 - 3), (wall.X1 - 3, wall.Y1 - 3) })
                Put(Cells(tx, ty, 3, 3), "Tower", "Military", "Corner tower", storeys: 3);
        }

        int noble = rnd.Next(4);
        Place(g, w, d, taken, buildings, rnd, o.Density, o.Theme, o.SquareMm, o.Prefabs, (x, y, onSquare) =>
        {
            if (onSquare) return "Market";
            if (!Inside(x, y)) return o.Walls ? "Farm" : null;
            if (water.Count > 0 && water.Any(c => Math.Abs(c.X - x) + Math.Abs(c.Y - y) <= 4)) return "Docks";
            double far = Far(x, y);
            if (o.Walls && (x <= wall.X0 + 3 || x >= wall.X1 - 4 || y <= wall.Y0 + 3 || y >= wall.Y1 - 4) && GateNear(g, x, y)) return "Military";
            if (far < 0.3) return "Market";
            if (far < 0.6) return (x >= cx ? 1 : 0) + (y >= cy ? 2 : 0) == noble ? "Noble" : "Residential";
            if (far < 0.85) return "Residential";
            return o.Walls ? "Slums" : rnd.NextDouble() < 0.5 ? "Farm" : "Residential";
        }, OnSquare, Inside, firstOnSquare: o.Square ? ["Temple", "Hall", "Tavern"] : []);

        // Farms' fields outside the walls (or round the edge of an open town), with hedges.
        var props = new List<Prop>(towers);
        int fields = 0;
        for (int tries = 0; tries < (w * d) / 60 && fields < (w * d) / 300 + 2; tries++)
        {
            int fw = rnd.Next(5, 9), fd = rnd.Next(4, 7), fx = rnd.Next(0, Math.Max(1, w - fw)), fy = rnd.Next(0, Math.Max(1, d - fd));
            var cells = Cells(fx, fy, fw, fd);
            if (cells.Any(c => Inside(c.X, c.Y) && (o.Walls || Far(c.X, c.Y) < 0.75))) continue;
            if (cells.Any(c => g[c.X, c.Y] != Ground.Grass || taken.Contains(c))) continue;
            foreach (var (x, y) in cells) { g[x, y] = Ground.Field; taken.Add((x, y)); }
            var edges = new List<FenceEdge>();
            for (int x = fx; x < fx + fw; x++) { edges.Add(new FenceEdge(x, fy, true)); edges.Add(new FenceEdge(x, fy + fd, true)); }
            for (int y = fy; y < fy + fd; y++) { edges.Add(new FenceEdge(fx, y, false)); edges.Add(new FenceEdge(fx + fw, y, false)); }
            var kind = rnd.NextDouble() < 0.6 ? FenceKind.Hedge : FenceKind.Fence;
            int fi = fences.FindIndex(f => f.Kind == kind);
            if (fi < 0) fences.Add(new Fence { Kind = kind, Edges = edges });
            else fences[fi] = fences[fi] with { Edges = [.. fences[fi].Edges, .. edges.Where(e => !fences[fi].Edges.Contains(e))] };
            fields++;
        }

        // The square: a well in the middle, carts, crates and a statue, lamps at its corners.
        if (o.Square)
        {
            double mx = (square.X0 + square.X1 + 1) / 2.0, my = (square.Y0 + square.Y1 + 1) / 2.0;
            props.Add(new Prop { Kind = PropKind.Well, X = mx, Y = my });
            props.Add(new Prop { Kind = PropKind.Statue, X = square.X0 + 1.5, Y = square.Y0 + 1.5 });
            foreach (var (lx, ly) in new[] { (square.X0 + 0.5, square.Y0 + 0.5), (square.X1 + 0.5, square.Y0 + 0.5), (square.X0 + 0.5, square.Y1 + 0.5), (square.X1 + 0.5, square.Y1 + 0.5) })
                props.Add(new Prop { Kind = PropKind.Lamp, X = lx, Y = ly });
            // What's on it stands clear of the well, the statue, the lamps and the ways through it (the rows and
            // columns where a road comes in): a players' stage, market stalls, an outdoor bar, a notice board
            // and a horse trough along its sides, carts and crates about it.
            var used = new HashSet<(int X, int Y)>
            {
                ((int)Math.Floor(mx - 0.5), (int)Math.Floor(my - 0.5)), ((int)Math.Floor(mx), (int)Math.Floor(my - 0.5)),
                ((int)Math.Floor(mx - 0.5), (int)Math.Floor(my)), ((int)Math.Floor(mx), (int)Math.Floor(my)),
                (square.X0 + 1, square.Y0 + 1), (square.X0, square.Y0), (square.X1, square.Y0), (square.X0, square.Y1), (square.X1, square.Y1),
            };
            bool Way(int x, int y) => Enumerable.Range(square.X0, square.X1 - square.X0 + 1).Any(xx => xx == x && (IsRoad(xx, square.Y0 - 1) || IsRoad(xx, square.Y1 + 1)))
                || Enumerable.Range(square.Y0, square.Y1 - square.Y0 + 1).Any(yy => yy == y && (IsRoad(square.X0 - 1, yy) || IsRoad(square.X1 + 1, yy)));
            bool IsRoad(int x, int y) => x >= 0 && y >= 0 && x < w && y < d && g[x, y] == Ground.Road;
            bool Fits(int x0, int y0, int cw, int cd) => Enumerable.Range(x0, cw).All(x => Enumerable.Range(y0, cd).All(y =>
                OnSquare(x, y) && g[x, y] == Ground.Paving && !used.Contains((x, y)) && !Way(x, y)));
            bool Stand(PropKind kind, int cw, int cd, double turn, IEnumerable<(int X, int Y)> spots)
            {
                foreach (var (x0, y0) in spots)
                {
                    if (!Fits(x0, y0, cw, cd)) continue;
                    foreach (var c in Cells(x0, y0, cw, cd)) used.Add(c);
                    props.Add(new Prop { Kind = kind, X = x0 + cw / 2.0, Y = y0 + cd / 2.0, TurnDeg = turn });
                    return true;
                }
                return false;
            }
            var xs = Enumerable.Range(square.X0, square.X1 - square.X0 + 1).OrderBy(x => Math.Abs(x + 0.5 - mx)).ToList();
            // The stage along the top (its backdrop behind it), or else the bottom.
            if (!Stand(PropKind.Stage, 3, 2, 0, xs.Select(x => (x, square.Y0))))
                Stand(PropKind.Stage, 3, 2, 180, xs.Select(x => (x, square.Y1 - 1)));
            // Stalls along the bottom and the top, facing in.
            int stalls = Math.Clamp((square.X1 - square.X0) / 3, 2, 5);
            for (int i = 0; i < stalls; i++)
                if (!Stand(PropKind.Stall, 2, 1, 180, xs.Select(x => (x - 1, square.Y1))))
                    Stand(PropKind.Stall, 2, 1, 0, xs.Select(x => (x - 1, square.Y0)));
            // A bar, a notice board and a trough along its left or right side, or else its top or bottom, or else inside it.
            var sides = Enumerable.Range(square.Y0 + 1, Math.Max(0, square.Y1 - square.Y0 - 1)).SelectMany(y => new[] { (square.X0, y), (square.X1, y) }).OrderBy(_ => rnd.Next()).ToList();
            var ends = Enumerable.Range(square.X0 + 1, Math.Max(0, square.X1 - square.X0 - 1)).SelectMany(x => new[] { (x, square.Y0), (x, square.Y1) }).OrderBy(_ => rnd.Next()).ToList();
            var inside = Enumerable.Range(square.X0 + 1, Math.Max(0, square.X1 - square.X0 - 1)).SelectMany(x => Enumerable.Range(square.Y0 + 1, Math.Max(0, square.Y1 - square.Y0 - 1)).Select(y => (x, y))).OrderBy(_ => rnd.Next()).ToList();
            foreach (var (kind, len) in new[] { (PropKind.Bar, 2), (PropKind.NoticeBoard, 1), (PropKind.Trough, 2) })
                _ = Stand(kind, 1, len, 90, sides) || Stand(kind, len, 1, 0, ends) || Stand(kind, 1, len, 90, inside) || Stand(kind, len, 1, 0, inside);
            for (int i = 0; i < Math.Max(2, (square.X1 - square.X0) / 3); i++)
                Stand(i % 2 == 0 ? PropKind.Cart : PropKind.Crates, 1, 1, rnd.Next(4) * 90, inside);
        }
        // Lamps along the main roads.
        foreach (var r in roads.Where(r => r.Main))
        {
            int i = 0;
            foreach (var c in r.Cells.OrderBy(c => r.Across ? c.X : c.Y).ThenBy(c => r.Across ? c.Y : c.X))
            {
                // On the grass at the road's edge (its top or left side), every ninth square.
                var beside = r.Across ? (X: c.X, Y: c.Y - 1) : (X: c.X - 1, Y: c.Y);
                if (beside.X < 0 || beside.Y < 0 || g[beside.X, beside.Y] != Ground.Grass || taken.Contains(beside) || !Inside(c.X, c.Y)) continue;
                if (i++ % 9 == 4) props.Add(new Prop { Kind = PropKind.Lamp, X = r.Across ? c.X + 0.5 : c.X - 0.2, Y = r.Across ? c.Y - 0.2 : c.Y + 0.5 });
            }
        }
        // Trees: gardens in town, woods outside, a few rocks.
        double phaseT = rnd.NextDouble() * 10;
        for (int y = 0; y < d; y++)
            for (int x = 0; x < w; x++)
            {
                if (g[x, y] != Ground.Grass || taken.Contains((x, y)) || NextToRoad(g, x, y, w, d)) continue;
                double wood = 0.5 + 0.5 * Math.Sin(x / 5.3 + phaseT) * Math.Cos(y / 4.1 - phaseT);
                bool town = Inside(x, y) && Far(x, y) < (o.Walls ? 2 : 0.8);
                double p = town ? 0.04 : 0.05 + 0.4 * wood * wood;
                if (rnd.NextDouble() < p)
                    props.Add(new Prop { Kind = rnd.NextDouble() < (town ? 0.2 : 0.35) ? PropKind.Pine : rnd.NextDouble() < 0.15 ? PropKind.Bush : PropKind.Tree, X = x + 0.3 + rnd.NextDouble() * 0.4, Y = y + 0.3 + rnd.NextDouble() * 0.4, Size = 0.9 + rnd.NextDouble() * 0.6, TurnDeg = rnd.Next(360) });
                else if (!town && rnd.NextDouble() < 0.006)
                    props.Add(new Prop { Kind = PropKind.Boulder, X = x + 0.5, Y = y + 0.5, Size = 0.8 + rnd.NextDouble() * 0.6, TurnDeg = rnd.Next(360) });
            }

        // Street names on the roads, the square's and the river's.
        var labels = new List<CityLabel>();
        var names = Streets.OrderBy(_ => rnd.Next()).ToList();
        int ni = 0;
        foreach (var r in roads.OrderByDescending(r => r.Main).ThenByDescending(r => r.Cells.Count))
        {
            if (ni >= names.Count || r.Cells.Count < 6) continue;
            var along = r.Cells.OrderBy(c => r.Across ? c.X : c.Y).ToList();
            var at = along[Math.Clamp(along.Count / 4 + rnd.Next(along.Count / 4 + 1), 0, along.Count - 1)];
            if (OnSquare(at.X, at.Y) || water.Contains(at)) continue;
            var line = r.Cells.Where(c => r.Across ? c.X == at.X : c.Y == at.Y).ToList();
            double lx = r.Across ? at.X + 0.5 : line.Average(c => c.X) + 0.5, ly = r.Across ? line.Average(c => c.Y) + 0.5 : at.Y + 0.5;
            labels.Add(new CityLabel { Text = names[ni++], X = lx, Y = ly, Size = r.Main ? 0.9 : 0.6 });
            if (labels.Count >= 6) break;
        }
        if (o.Square) labels.Add(new CityLabel { Text = "Market Square", X = (square.X0 + square.X1 + 1) / 2.0, Y = square.Y0 + 0.7, Size = 0.8 });
        if (water.Count > 0)
        {
            var spot = water.OrderBy(c => Math.Abs(c.X - w * 0.2) + Math.Abs(c.Y - d * 0.15)).First();
            if (riverside) spot = water.Where(c => !roads.Any(r => r.Cells.Contains(c))).OrderBy(c => Math.Abs(c.X - w * 0.15)).ThenBy(c => c.Y).Skip(1).FirstOrDefault(spot);
            labels.Add(new CityLabel { Text = Rivers[rnd.Next(Rivers.Length)], X = spot.X + 0.5, Y = spot.Y + 0.5, Size = 0.8 });
        }

        var map = CityMap.Empty(w, d, string.IsNullOrWhiteSpace(o.Name) ? "New town" : o.Name) with { Theme = o.Theme, SquareMm = o.SquareMm };
        map = map.WithGround(g) with { Buildings = buildings, Fences = [.. fences.Where(f => f.Edges.Count > 0)], Props = props, Labels = labels };

        int fromSaved = buildings.Count(b => b.PrefabId is not null);
        notes.Add($"{buildings.Count} buildings" + (fromSaved > 0 ? $" ({fromSaved} of them your saved buildings)" : "") + $", {roads.Count} roads, {props.Count(p => p.Kind is PropKind.Tree or PropKind.Pine or PropKind.Bush)} trees.");
        if (o.Walls) notes.Add($"A town wall with {gates} gate{(gates == 1 ? "" : "s")}, a tower in each corner" + (towers.Count > 0 ? $" and {towers.Count} guard tower{(towers.Count == 1 ? "" : "s")} along it." : "."));
        if (fields > 0) notes.Add($"{fields} field{(fields == 1 ? "" : "s")} with hedges and fences.");
        notes.Add("By district: " + string.Join(", ", buildings.GroupBy(b => b.District).OrderByDescending(x => x.Count()).Select(x => $"{x.Count()} {x.Key.ToLowerInvariant()}")) + ".");
        return (map, notes);

        void Put(HashSet<(int X, int Y)> cells, string type, string district, string name, int storeys)
        {
            if (cells.Any(c => c.X < 0 || c.Y < 0 || c.X >= w || c.Y >= d || g[c.X, c.Y] is Ground.Road or Ground.Water || taken.Contains(c))) return;
            var f = new SeenBuilding { Cells = cells, Storeys = storeys, Type = type, Covering = Covering(type, o.Theme, rnd), DoorTowards = Around(cells).Where(c => c.X >= 0 && c.Y >= 0 && c.X < w && c.Y < d && g[c.X, c.Y] is Ground.Road or Ground.Paving).ToHashSet() };
            if (BuildingGuess.Make(f, o.Theme, o.SquareMm) is not { } m) return;
            buildings.Add(new CityBuilding { Name = name, Type = type, District = district, X = m.X, Y = m.Y, Design = m.Design });
            foreach (var c in cells) taken.Add(c);
        }
    }

    /// <summary>Whether a road that runs off the map (through a gate) is within three squares.</summary>
    static bool GateNear(Ground[,] g, int x, int y)
    {
        int w = g.GetLength(0), d = g.GetLength(1);
        for (int j = y - 3; j <= y + 3; j++)
            for (int i = x - 3; i <= x + 3; i++)
                if (i >= 0 && j >= 0 && i < w && j < d && Math.Abs(i - x) + Math.Abs(j - y) <= 3 && g[i, j] == Ground.Road && RoadLeaves(g, i, j)) return true;
        return false;
    }

    /// <summary>Whether a road square is on a road that runs off the map (through a gate).</summary>
    static bool RoadLeaves(Ground[,] g, int x, int y)
    {
        int w = g.GetLength(0), d = g.GetLength(1);
        bool Run(int dx, int dy)
        {
            for (int i = x, j = y; i >= 0 && j >= 0 && i < w && j < d; i += dx, j += dy)
                if (g[i, j] != Ground.Road) return false;
            return true;
        }
        return Run(1, 0) || Run(-1, 0) || Run(0, 1) || Run(0, -1);
    }

    static bool NextToRoad(Ground[,] g, int x, int y, int w, int d)
    {
        foreach (var (i, j) in new[] { (x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1) })
            if (i >= 0 && j >= 0 && i < w && j < d && g[i, j] is Ground.Road or Ground.Paving) return true;
        return false;
    }

    static HashSet<(int X, int Y)> Cells(int x, int y, int w, int d)
    {
        var s = new HashSet<(int X, int Y)>();
        for (int j = y; j < y + d; j++) for (int i = x; i < x + w; i++) s.Add((i, j));
        return s;
    }

    static IEnumerable<(int X, int Y)> Around(HashSet<(int X, int Y)> cells)
    {
        foreach (var (x, y) in cells)
            foreach (var n in new[] { (x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1) })
                if (!cells.Contains(n)) yield return n;
    }

    // ---- plots along the roads ----

    /// <summary>
    /// Buildings along the roads (and round paved squares) on grass that's free: for every stretch of road, plots
    /// side by side facing it (as close as <paramref name="density"/> says), each one's kind from its part of
    /// town (<paramref name="districtAt"/>; null leaves the plot empty), a saved building where one of the right
    /// district and kind suits it, otherwise one made up. Returns how many were put up.
    /// </summary>
    static int Place(Ground[,] g, int w, int d, HashSet<(int X, int Y)> taken, List<CityBuilding> buildings, Random rnd, double density, string theme, double squareMm,
        IReadOnlyList<PrefabInfo> prefabs, Func<int, int, bool, string?> districtAt, Func<int, int, bool> onSquare, Func<int, int, bool> allowed, string[] firstOnSquare)
    {
        density = Math.Clamp(density, 0, 1);
        int count = 0, number = buildings.Count;
        bool Road(int x, int y) => x >= 0 && y >= 0 && x < w && y < d && g[x, y] is Ground.Road or Ground.Paving;
        bool Free(int x, int y) => x >= 0 && y >= 0 && x < w && y < d && g[x, y] is Ground.Grass or Ground.Dirt && !taken.Contains((x, y));

        // Each way a plot can face its road: (towards the road), and the way along it.
        (int Fx, int Fy, int Ax, int Ay)[] faces = [(0, 1, 1, 0), (0, -1, 1, 0), (1, 0, 0, 1), (-1, 0, 0, 1)];
        // The fronts: free squares with road beside them, in order along the road (a row or column at a time).
        var fronts = new List<(int X, int Y, int Face)>();
        for (int f = 0; f < faces.Length; f++)
        {
            var (fx, fy, ax, ay) = faces[f];
            var list = new List<(int X, int Y, int Face)>();
            for (int y = 0; y < d; y++)
                for (int x = 0; x < w; x++)
                    if (Free(x, y) && Road(x + fx, y + fy)) list.Add((x, y, f));
            fronts.AddRange(ax == 1 ? list.OrderBy(c => c.Y).ThenBy(c => c.X) : list.OrderBy(c => c.X).ThenBy(c => c.Y));
        }
        // Plots facing the square first, so its temple and hall go there.
        fronts = [.. fronts.OrderByDescending(c => onSquare(c.X + faces[c.Face].Fx, c.Y + faces[c.Face].Fy))];

        // A made-up building of a kind on the plot at a front, as big as fits (down to its least); null if none does.
        CityBuilding? MadeUp(string type, string district, int x, int y, int face)
        {
            var (fx, fy, ax, ay) = faces[face];
            var (f0, f1, d0, d1, s0, s1) = Size(type);
            int front = rnd.Next(f0, f1 + 1), deep = rnd.Next(d0, d1 + 1);
            HashSet<(int X, int Y)>? plot = null;
            for (int fr = front; fr >= f0 && plot is null; fr--)
                for (int dp = deep; dp >= d0 && plot is null; dp--)
                {
                    var p = Plot(x, y, fx, fy, ax, ay, fr, dp);
                    if (p.All(c => Free(c.X, c.Y) && (allowed(c.X, c.Y) || district == "Farm"))) plot = p;
                }
            if (plot is null) return null;
            var towards = Around(plot).Where(c => Road(c.X, c.Y)).ToHashSet();
            var seen = new SeenBuilding { Cells = plot, Storeys = rnd.Next(s0, s1 + 1), Type = type, Covering = Covering(type, theme, rnd), DoorTowards = towards };
            if (BuildingGuess.Make(seen, theme, squareMm) is not { } m) return null;
            return new CityBuilding { Name = NameFor(type, ++number, rnd), Type = type, District = district, X = m.X, Y = m.Y, Design = m.Design };
        }
        var skipTo = new Dictionary<int, (int X, int Y)>();
        List<(int X, int Y)> Add(CityBuilding b)
        {
            var own = CityFootprint.Cells(b).ToList();
            foreach (var c in own) taken.Add(c);
            buildings.Add(b);
            count++;
            return own;
        }

        // The square's own buildings first (its temple, hall and tavern), each at the first plot facing it where it fits.
        foreach (var kind in firstOnSquare)
        {
            string type = ForTheme(kind, theme);
            foreach (var (x, y, face) in fronts)
            {
                if (!Free(x, y) || !onSquare(x + faces[face].Fx, y + faces[face].Fy)) continue;
                if ((kind == "Temple" ? "Temple" : districtAt(x, y, true)) is not { } district || MadeUp(type, district, x, y, face) is not { } b) continue;
                Add(b);
                break;
            }
        }

        foreach (var (x, y, face) in fronts)
        {
            if (!Free(x, y)) continue;
            var (fx, fy, ax, ay) = faces[face];
            // Gaps between plots: skip squares just after the last plot along this road.
            if (skipTo.TryGetValue(face, out var until) && (ax == 1 ? y == until.Y && x < until.X : x == until.X && y < until.Y)) continue;
            bool facesSquare = onSquare(x + fx, y + fy);
            var district = districtAt(x, y, facesSquare);
            if (district is null || !allowed(x, y) && district != "Farm") continue;
            if (!facesSquare && rnd.NextDouble() > 0.35 + 0.65 * density) continue;
            string type = ForTheme(Pick(Kinds.GetValueOrDefault(district) ?? Kinds["Residential"], rnd), theme);

            // A saved building of this district and kind first (any kind, if none is), as its size.
            CityBuilding? b = null;
            var fits = prefabs.Where(p => string.Equals(p.District, district, StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(p.District) || p.District == "Anywhere").ToList();
            var same = fits.Where(p => string.Equals(p.Type, type, StringComparison.OrdinalIgnoreCase)).ToList();
            if (same.Count > 0) fits = same;
            if (fits.Count > 0 && rnd.NextDouble() < 0.6)
            {
                var p = fits[rnd.Next(fits.Count)];
                var (pw, pd) = CityFootprint.Extent(p.Design);
                foreach (var (front, deep) in new[] { (pw, pd), (pd, pw) })
                {
                    var plot = Plot(x, y, fx, fy, ax, ay, front, deep);
                    if (!plot.All(c => Free(c.X, c.Y) && (allowed(c.X, c.Y) || district == "Farm"))) continue;
                    var towards = Around(plot).Where(c => Road(c.X, c.Y)).ToHashSet();
                    if (PrefabFit.Fit(new SeenBuilding { Cells = plot, Type = p.Type, DoorTowards = towards }, [p]) is { } fit)
                    {
                        b = fit with { Id = Guid.NewGuid(), District = string.IsNullOrWhiteSpace(p.District) || p.District == "Anywhere" ? district : p.District, Name = string.IsNullOrWhiteSpace(p.Name) ? NameFor(p.Type, ++number, rnd) : p.Name };
                        break;
                    }
                }
            }
            b ??= MadeUp(type, district, x, y, face);
            if (b is null) continue;
            var own = Add(b);
            // The next plot along: right beside it in a packed street, otherwise a square or two on.
            int gap = rnd.NextDouble() < density * 0.8 ? 0 : rnd.Next(1, 3);
            int far = ax == 1 ? own.Max(c => c.X) : own.Max(c => c.Y);
            skipTo[face] = ax == 1 ? (far + 1 + gap, y) : (x, far + 1 + gap);
        }
        return count;
    }

    /// <summary>A plot <paramref name="front"/> squares along the road and <paramref name="deep"/> back from it, its front corner at (x, y).</summary>
    static HashSet<(int X, int Y)> Plot(int x, int y, int fx, int fy, int ax, int ay, int front, int deep)
    {
        var s = new HashSet<(int X, int Y)>();
        for (int i = 0; i < front; i++)
            for (int j = 0; j < deep; j++) s.Add((x + ax * i - fx * j, y + ay * i - fy * j));
        return s;
    }

    /// <summary>A kind of building as it's called in a look (a sci-fi town's houses are hab blocks).</summary>
    static string ForTheme(string type, string theme) => !theme.Contains("sci", StringComparison.OrdinalIgnoreCase) ? type : type switch
    {
        "House" or "Cottage" or "Hovel" => "Hab block",
        "Tavern" or "Inn" => "Canteen",
        "Barn" or "Stable" => "Hangar",
        "Guardhouse" => "Bunker",
        _ => type,
    };

    static string Pick((string Type, double Weight)[] kinds, Random rnd)
    {
        double total = kinds.Sum(k => k.Weight), r = rnd.NextDouble() * total;
        foreach (var (type, weight) in kinds)
            if ((r -= weight) <= 0) return type;
        return kinds[^1].Type;
    }

    // ---- filling an existing map ----

    /// <summary>
    /// Buildings along the map's roads (and paved squares) wherever there's free grass, inside a box (squares,
    /// or the whole map), all of one district; saved buildings of that district where they suit a plot. Trees
    /// where a building now stands go. Returns the map and how many buildings were put up.
    /// </summary>
    public static (CityMap Map, int Added) Fill(CityMap m, string district, double density, int seed, IReadOnlyList<PrefabInfo> prefabs, (int X0, int Y0, int X1, int Y1)? box = null)
    {
        var rnd = new Random(seed);
        var g = m.GroundGrid();
        var taken = m.Buildings.SelectMany(CityFootprint.Cells).ToHashSet();
        var area = box ?? (0, 0, m.Across, m.Down);
        bool Inside(int x, int y) => x >= area.X0 && y >= area.Y0 && x < area.X1 && y < area.Y1;
        var buildings = new List<CityBuilding>();
        var d = string.IsNullOrWhiteSpace(district) ? "Residential" : district.Trim();
        int added = Place(g, m.Across, m.Down, taken, buildings, rnd, density, m.Theme, m.SquareMm, prefabs, (x, y, _) => Inside(x, y) ? d : null, (_, _) => false, Inside, []);
        if (added == 0) return (m, 0);
        var under = buildings.SelectMany(CityFootprint.Cells).ToHashSet();
        return (m with
        {
            Buildings = [.. m.Buildings, .. buildings],
            Props = [.. m.Props.Where(p => !under.Contains(((int)Math.Floor(p.X), (int)Math.Floor(p.Y))))],
        }, added);
    }
}
