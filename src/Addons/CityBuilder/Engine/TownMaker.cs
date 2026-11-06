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

    /// <summary>
    /// Each kind's frontage and depth (squares, least and most) and floors (least and most): the sizes of such
    /// buildings on a battle map of five-foot squares (a cottage 20 by 20 feet, a town house 20 by 30, a tavern
    /// 35 by 40, a temple 45 by 65).
    /// </summary>
    static (int F0, int F1, int D0, int D1, int S0, int S1) Size(string type) => type switch
    {
        "Hovel" => (3, 3, 3, 3, 1, 1),
        "Cottage" => (4, 5, 4, 4, 1, 1),
        "House" or "Hab block" => (4, 5, 5, 6, 1, 2),
        "Canteen" => (7, 8, 6, 8, 1, 2),
        "Hangar" => (7, 9, 6, 8, 1, 1),
        "Bunker" => (4, 5, 4, 5, 1, 1),
        "Shop" => (4, 5, 5, 6, 2, 2),
        "Workshop" => (5, 6, 5, 6, 1, 2),
        "Smithy" => (5, 6, 5, 6, 1, 1),
        "Tavern" => (7, 8, 7, 8, 2, 2),
        "Inn" => (8, 9, 8, 9, 2, 3),
        "Manor" => (9, 11, 8, 9, 2, 3),
        "Warehouse" => (7, 8, 8, 10, 1, 2),
        "Barn" => (6, 7, 8, 10, 1, 1),
        "Stable" => (8, 10, 4, 5, 1, 1),
        "Mill" => (4, 4, 4, 4, 2, 3),
        "Temple" => (8, 10, 12, 14, 1, 1),
        "Chapel" => (5, 6, 9, 10, 1, 1),
        "Hall" => (8, 10, 8, 10, 2, 2),
        "Guardhouse" => (4, 5, 5, 6, 2, 2),
        "Shrine" => (3, 3, 3, 3, 1, 1),
        "Tower" => (4, 4, 4, 4, 3, 3),
        _ => (4, 5, 5, 6, 1, 2),
    };

    /// <summary>The smaller kind to put up when a plot's too small for the one picked.</summary>
    static string Smaller(string district) => district switch
    {
        "Slums" or "Wilderness" => "Hovel",
        "Outskirts" or "Farm" => "Cottage",
        _ => "House",
    };

    /// <summary>
    /// How many of a kind a town of <paramref name="big"/> (0 for a town, more for a city) has: one smithy, mill,
    /// stable and so on in a town, a few more in a city; houses as many as there's room for.
    /// </summary>
    static int Limit(string type, int big) => type switch
    {
        "Smithy" or "Mill" or "Stable" or "Chapel" or "Shrine" or "Inn" or "Guardhouse" or "Manor" or "Bunker" or "Hangar" => 1 + big,
        "Temple" or "Hall" => 1 + big / 3,
        "Tavern" or "Canteen" => 2 + big,
        "Warehouse" or "Barn" or "Tower" => 2 + 2 * big,
        _ => int.MaxValue,
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

    static readonly string[] Adjectives = ["Golden", "Silver", "Red", "Green", "Crooked", "Drunken", "Sleeping", "Laughing", "Old", "Black", "Blue", "Merry", "Wandering", "Rusty", "Prancing", "Jolly", "Grey", "White", "Lucky", "Brave", "Hungry", "Painted"];
    static readonly string[] Nouns = ["Goose", "Stag", "Lantern", "Barrel", "Anchor", "Boar", "Crown", "Kettle", "Fox", "Bell", "Plough", "Wheel", "Hound", "Tankard", "Pony", "Dragon", "Griffin", "Swan", "Owl", "Hart", "Mermaid", "Raven"];
    /// <summary>Trades with a shop on the street, each in one town once.</summary>
    static readonly string[] ShopTrades =
    [
        "Baker", "Butcher", "Chandler", "Tailor", "Apothecary", "Cobbler", "Jeweller", "Grocer", "Herbalist", "Bookbinder", "Cheesemonger", "Fishmonger",
        "Hatter", "Locksmith", "Scribe", "Barber", "Candlemaker", "Vintner", "Clothier", "Spice merchant", "Alchemist", "Cartographer", "Tinker", "Moneylender",
    ];
    /// <summary>Trades with a workshop, each in one town once.</summary>
    static readonly string[] WorkshopTrades =
    [
        "Cooper", "Cartwright", "Tanner", "Weaver", "Potter", "Fletcher", "Glassblower", "Carpenter", "Wheelwright", "Bowyer", "Ropemaker", "Dyer",
        "Mason", "Brewer", "Saddler", "Armourer", "Shipwright", "Furrier", "Thatcher", "Woodcarver",
    ];
    /// <summary>Family names for the houses and cottages, and the manors.</summary>
    static readonly string[] Families =
    [
        "Ashford", "Blackwood", "Thorne", "Hale", "Marsh", "Whitlock", "Graves", "Fairfax", "Crane", "Holloway", "Abbott", "Barlow", "Brightwater", "Carver",
        "Colborne", "Cotton", "Dunmore", "Eastwood", "Fenwick", "Fletcher", "Garrow", "Goodwin", "Greaves", "Hartley", "Hawthorne", "Hollis", "Kettle",
        "Langley", "Larkin", "Lowe", "Mallory", "Merrow", "Millward", "Morrow", "Nettle", "Oakes", "Penrose", "Pike", "Quill", "Radley", "Redfern", "Rook",
        "Rowe", "Selby", "Shaw", "Stone", "Stroud", "Talbot", "Tanner", "Thatcher", "Underhill", "Vance", "Wainwright", "Warren", "Webb", "Weston",
        "Wilde", "Winslow", "Wren", "Yardley", "Ainsley", "Birch", "Bramble", "Brook", "Calloway", "Cobb", "Croft", "Dale", "Draper", "Elwood", "Finch",
        "Foley", "Gale", "Gilbert", "Hayward", "Hobb", "Ingram", "Kemp", "Lister", "Meadows", "Norris", "Orchard", "Parr", "Pickering", "Reeve",
        "Ridley", "Saltmarsh", "Sawyer", "Sparrow", "Steele", "Tilly", "Toller", "Turner", "Vale", "Ward", "Whitby", "Wolfe", "Wyatt", "Yates",
    ];
    /// <summary>Names for the high roads through a town, then for its lanes.</summary>
    static readonly string[] HighRoads = ["King's Road", "High Street", "Queen's Way", "Great North Road", "Old Road", "Broad Street", "Long Street", "Market Road", "London Road", "West Road"];
    static readonly string[] Streets =
    [
        "Market Street", "Mill Lane", "Church Lane", "Bridge Street", "Castle Way", "Water Lane", "Baker's Row", "Tanner's Row", "Gate Street", "Well Lane",
        "Chapel Row", "Cart Lane", "Fish Street", "Wool Lane", "Silver Street", "Friar's Walk", "Crown Lane", "Sheep Street", "Butcher's Row", "Cooper's Lane",
        "Pudding Lane", "Abbey Road", "Orchard Lane", "Rope Walk", "Hay Lane", "Barley Row", "Lantern Lane", "Thimble Street", "Candle Lane", "Bell Street",
        "Ash Lane", "Oak Street", "Elm Row", "Willow Walk", "Hollow Lane", "Stone Street", "Cobble Lane", "Pike Street", "Raven Lane", "Smith's Row",
        "Dyer's Lane", "Potter's Row", "Weaver's Walk", "Saddler's Lane", "Fletcher's Row", "Glover's Lane", "Mint Street", "Tower Lane", "Postern Lane", "Dock Street",
        "Ferry Lane", "Quay Street", "Garden Row", "Meadow Lane", "Hill Street", "Shambles", "Cheapside", "Poultry", "Threadneedle Lane", "Goose Lane",
    ];
    static readonly string[] Rivers = ["River Wend", "River Ashe", "River Moll", "River Tarn", "River Brede"];

    /// <summary>Names given out once each in a town: shop and workshop trades, taverns, families, roads.</summary>
    sealed class Names(Random rnd, IEnumerable<CityBuilding>? existing = null, IEnumerable<CityRoad>? roads = null)
    {
        // Names already in the town: its buildings' and roads', and the families and trades in them.
        readonly HashSet<string> _used = new([.. (existing ?? []).Select(b => b.Name), .. (existing ?? []).Select(b => b.Name.Split(' ')[0]), .. (roads ?? []).Select(r => r.Name)], StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> _trades = new((existing ?? []).Select(b => b.Name.Split("'s ")[0]), StringComparer.OrdinalIgnoreCase);
        int _lane;

        public bool Has(string name) => _used.Contains(name);

        /// <summary>Whether there's a name left for one more of a kind (a shop's trade not yet in the town).</summary>
        public bool Left(string type) => type switch
        {
            "Shop" => ShopTrades.Any(t => !_trades.Contains(t)),
            "Workshop" => WorkshopTrades.Any(t => !_trades.Contains(t)),
            _ => true,
        };

        string? Fresh(IEnumerable<string> choices)
        {
            foreach (var c in choices.OrderBy(_ => rnd.Next()))
                if (_used.Add(c)) return c;
            return null;
        }

        /// <summary>Marks a name as taken (a saved building's own).</summary>
        public void Use(string name) => _used.Add(name);

        /// <summary>A trade not yet in the town, or null when every one is.</summary>
        public string? Trade(string[] trades)
        {
            foreach (var t in trades.OrderBy(_ => rnd.Next()))
                if (_trades.Add(t)) return t;
            return null;
        }

        /// <summary>A family not yet in the town; when every one is, two joined (Ashford-Blackwood).</summary>
        public string Family() => Fresh(Families) ?? Fresh(Families.SelectMany(a => Families.Where(b => b != a).Select(b => $"{a}-{b}"))) ?? Numbered("Family");

        public string Tavern() => Fresh(Adjectives.SelectMany(a => Nouns.Select(n => $"The {a} {n}"))) ?? Numbered("Tavern");

        /// <summary>A name with the next number not yet used (Tavern 3).</summary>
        string Numbered(string name)
        {
            for (int i = 2; ; i++)
                if (_used.Add($"{name} {i}")) return $"{name} {i}";
        }

        public string HighRoad() => Fresh(HighRoads) ?? Lane();

        public string Lane() => Fresh(Streets) ?? $"Lane {++_lane}";

        /// <summary>A name for a building of a kind (a trade's shop, a tavern's sign, a family's house), or null when the town has its one of that kind of shop.</summary>
        public string? For(string type, int n)
        {
            switch (type)
            {
                case "Tavern" or "Inn" or "Canteen": return Tavern();
                case "Shop": return Trade(ShopTrades) is { } t ? $"{t}'s shop" : null;
                case "Workshop": return Trade(WorkshopTrades) is { } w ? $"{w}'s workshop" : null;
                case "Manor": return $"{Family()} Manor";
                case "House": return $"{Family()} house";
                case "Cottage": return $"{Family()} cottage";
                case "Hovel": return $"{Family()} hovel";
                case "Hab block": return $"{Family()} hab";
                case "Temple": return Fresh(["Temple", "Temple of the Dawn", "Temple of the Harvest"]) ?? $"Temple {n}";
                case "Hall": return Fresh(["Town hall", "Guild hall", "Moot hall"]) ?? $"Hall {n}";
                case "Smithy": return Fresh(["Smithy"]) ?? $"{Family()} smithy";
                case "Mill": return Fresh(["Mill", "Water mill", "Windmill"]) ?? $"Mill {n}";
                case "Stable": return Fresh(["Stables", "Livery stables", "Post stables"]) ?? $"Stables {n}";
                case "Chapel": return Fresh(["Chapel", "Wayside chapel", "Chapel of rest"]) ?? $"Chapel {n}";
                case "Barn": return $"{Family()} barn";
                case "Tower": return Fresh(["Watchtower", "Bell tower", "Old tower", "Signal tower"]) ?? $"Tower {n}";
                case "Guardhouse": return Fresh(["Guardhouse", "Watch house", "Gate guardhouse"]) ?? $"Guardhouse {n}";
                case "Warehouse": return Fresh(["Wool warehouse", "Grain store", "Salt warehouse", "Timber store", "Cloth hall", "Spice warehouse", "Wine cellars", "Ironmonger's store"]) ?? $"Warehouse {n}";
                default: return Fresh([type]) ?? $"{type} {n}";
            }
        }
    }

    /// <summary>A street name the map hasn't got yet, for a road laid by hand.</summary>
    public static string RoadName(CityMap m, int seed) => new Names(new Random(seed), m.Buildings, m.Roads).Lane();

    /// <summary>
    /// A name for a new building of a kind the map hasn't used yet: a family's for a house or cottage (the Ashford
    /// house), a trade's for a shop, a sign for a tavern.
    /// </summary>
    public static string BuildingName(CityMap m, string type, int seed) =>
        new Names(new Random(seed), m.Buildings, m.Roads).For(type, m.Buildings.Count + 1) ?? $"{type} {m.Buildings.Count + 1}";

    // ---- a whole town ----

    /// <summary>A whole town on a new map, and notes on what was made.</summary>
    public static (CityMap Map, List<string> Notes) Make(TownOptions o)
    {
        var rnd = new Random(o.Seed);
        int w = Math.Clamp(o.Across, 16, CityMap.MaxSide), d = Math.Clamp(o.Down, 16, CityMap.MaxSide);
        var g = new Ground[w, d];
        int cx = w / 2 + rnd.Next(-w / 12, w / 12 + 1), cy = d / 2 + rnd.Next(-d / 12, d / 12 + 1);
        bool riverside = o.Layout == TownLayout.Riverside;
        int big = w * d / 6000;
        var names = new Names(rnd);

        // The town wall's line (grid lines): plots inside it, farms outside.
        int inset = o.Walls ? Math.Clamp(Math.Min(w, d) / 8, 3, 12) : 0;
        var wall = (X0: inset, Y0: inset, X1: w - inset, Y1: d - inset);
        bool Inside(int x, int y) => !o.Walls || (x > wall.X0 && x < wall.X1 - 1 && y > wall.Y0 && y < wall.Y1 - 1);

        // The river: down one side, or along the bottom for a riverside town; a square of shore along each bank.
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
            for (int y = 0; y < d; y++)
                for (int x = 0; x < w; x++)
                    if (g[x, y] == Ground.Grass && Neighbours8(x, y).Any(water.Contains)) g[x, y] = Ground.Sand;
        }
        // The ground before the roads go down (put back under them at the end: the roads lie on top).
        var under = (Ground[,])g.Clone();

        // The roads: four squares wide for the high roads through the town, two for the lanes.
        const int High = 4, Lane = 2;
        var roads = new List<(CityRoad Road, HashSet<(int X, int Y)> Cells, bool Main, bool Across)>();
        void Lay(CityRoad road, bool main)
        {
            var cells = road.Cells(w, d).ToHashSet();
            if (cells.Count == 0) return;
            foreach (var (x, y) in cells) g[x, y] = Ground.Road;
            var a = road.Points[0];
            var b = road.Points[^1];
            roads.Add((road, cells, main, Math.Abs(b.X - a.X) >= Math.Abs(b.Y - a.Y)));
        }
        void Road(int ax, int ay, int bx, int by, int width, bool main)
        {
            var road = new CityRoad { Width = width, Points = CityEdit.RoadLine(ax, ay, bx, by, width, straight: false) };
            // A lane running the way the river does stops short of its bank instead of running down it (and isn't
            // laid at all if it starts in it); lanes across it cross it, on a bridge.
            bool across = Math.Abs(bx - ax) >= Math.Abs(by - ay);
            if (!main && water.Count > 0 && across == riverside)
            {
                if (CityEdit.StopAt(w, d, road, c => water.Contains(c) || g[c.X, c.Y] == Ground.Sand) is not { } dry || dry.Length < 3) return;
                road = dry;
            }
            // A lane that would only be a stub isn't laid.
            if (!main && road.Length < 5) return;
            Lay(road with { Name = main ? names.HighRoad() : names.Lane() }, main);
        }
        int t0x = Math.Max(1, wall.X0 + 2), t1x = Math.Min(w - 2, wall.X1 - 3), t0y = Math.Max(1, wall.Y0 + 2), t1y = Math.Min(d - 2, wall.Y1 - 3);
        int Between(int a, int b) => a >= b ? a : rnd.Next(a, b + 1);
        switch (o.Layout)
        {
            case TownLayout.Crossroads:
            {
                Road(0, cy, w - 1, cy, High, true);
                Road(cx, 0, cx, d - 1, High, true);
                // Lanes off the high roads in each quarter, one every 14 to 17 squares out to the edge of town,
                // the further ones not always running all the way.
                foreach (var (sx, sy) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
                {
                    for (int k = 0, ly = cy + sy * Between(10, 13); ly > t0y && ly < t1y; k++, ly += sy * Between(14, 17))
                    {
                        int end = sx < 0 ? t0x + rnd.Next(0, 4) : t1x - rnd.Next(0, 4);
                        if (k > 0) end = cx + (int)((end - cx) * (0.7 + rnd.NextDouble() * 0.3));
                        if (Math.Abs(end - cx) > 5) Road(cx + sx * 2, ly, end, ly, Lane, false);
                    }
                    for (int k = 0, lx = cx + sx * Between(10, 13); lx > t0x && lx < t1x; k++, lx += sx * Between(14, 17))
                    {
                        int end = sy < 0 ? t0y + rnd.Next(0, 4) : t1y - rnd.Next(0, 4);
                        if (k > 0) end = cy + (int)((end - cy) * (0.7 + rnd.NextDouble() * 0.3));
                        if (Math.Abs(end - cy) > 5) Road(lx, cy + sy * 2, lx, end, Lane, false);
                    }
                }
                break;
            }
            case TownLayout.MainStreet:
            {
                Road(0, cy, w - 1, cy, High, true);
                for (int x = t0x + Between(4, 8); x < t1x - 2; x += Between(14, 18))
                {
                    if (rnd.NextDouble() < 0.85) Road(x, cy - 2, x, cy - (int)((cy - t0y) * (0.7 + rnd.NextDouble() * 0.3)), Lane, false);
                    if (rnd.NextDouble() < 0.85) Road(x + rnd.Next(-2, 3), cy + 2, x, cy + (int)((t1y - cy) * (0.7 + rnd.NextDouble() * 0.3)), Lane, false);
                }
                // A big town has back streets along it too, sixteen or so squares apart.
                foreach (int sy in new[] { -1, 1 })
                {
                    int room = sy < 0 ? cy - t0y : t1y - cy;
                    if (room < 30) continue;
                    for (int y = cy + sy * Between(15, 18); sy < 0 ? y > t0y + 6 : y < t1y - 6; y += sy * Between(15, 18))
                        Road(t0x + rnd.Next(2, 8), y, t1x - rnd.Next(2, 8), y, Lane, false);
                }
                break;
            }
            case TownLayout.Grid:
            {
                int step = Math.Clamp(Math.Min(w, d) / 4, 14, 18);
                Road(0, cy, w - 1, cy, High, true);
                Road(cx, 0, cx, d - 1, High, true);
                for (int y = cy - step; y > t0y; y -= step) Road(t0x, y, t1x, y, Lane, false);
                for (int y = cy + step; y < t1y; y += step) Road(t0x, y, t1x, y, Lane, false);
                for (int x = cx - step; x > t0x; x -= step) Road(x, t0y, x, t1y, Lane, false);
                for (int x = cx + step; x < t1x; x += step) Road(x, t0y, x, t1y, Lane, false);
                break;
            }
            case TownLayout.Winding:
            {
                // A high road wandering across, from point to point, and lanes off it.
                double amp = Math.Max(2, d / 9.0), period = Math.Max(6, w / 4.5), phase = rnd.NextDouble() * 6;
                int Yat(int x) => Math.Clamp((int)Math.Round(cy + amp * Math.Sin(x / period + phase)), 3, d - 4);
                var pts = new List<CityPt>();
                for (int x = 0; ; x += 4)
                {
                    int xx = Math.Min(x, w);
                    pts.Add(new CityPt(xx, Yat(Math.Min(xx, w - 1)) + 1));
                    if (xx >= w) break;
                }
                Lay(new CityRoad { Name = names.HighRoad(), Width = High, Points = CityEdit.Distinct(pts) }, true);
                cy = Yat(cx);
                for (int x = t0x + Between(5, 8); x < t1x - 2; x += Between(14, 17))
                {
                    int y = Yat(x);
                    if (rnd.NextDouble() < 0.8) Road(x, y - 2, x + rnd.Next(-3, 4), Math.Max(t0y, y - (int)((y - t0y) * (0.5 + rnd.NextDouble() * 0.5))), Lane, false);
                    if (rnd.NextDouble() < 0.8) Road(x, y + 3, x + rnd.Next(-3, 4), Math.Min(t1y, y + 3 + (int)((t1y - y) * (0.5 + rnd.NextDouble() * 0.5))), Lane, false);
                }
                // A big town has more lanes wandering along it, crossing those.
                for (int k = 1; k < 30; k++)
                {
                    int off = (k + 1) / 2 * Between(16, 19) * (k % 2 == 1 ? -1 : 1);
                    double ph = rnd.NextDouble() * 6, am = amp * (0.4 + rnd.NextDouble() * 0.4);
                    int LaneY(int x) => (int)Math.Round(cy + off + am * Math.Sin(x / period + ph));
                    if (LaneY(w / 2) <= t0y + 3 || LaneY(w / 2) >= t1y - 3) { if (Math.Abs(off) > d) break; continue; }
                    var lane = new List<CityPt>();
                    int x0 = t0x + rnd.Next(1, 6), x1 = t1x - rnd.Next(1, 6);
                    for (int x = x0; ; x += 4)
                    {
                        int xx = Math.Min(x, x1);
                        lane.Add(new CityPt(xx, Math.Clamp(LaneY(xx), t0y + 1, t1y - 1)));
                        if (xx >= x1) break;
                    }
                    var road = new CityRoad { Name = names.Lane(), Width = Lane, Points = CityEdit.Distinct(lane) };
                    if (road.Length < 8) continue;
                    Lay(road, false);
                }
                break;
            }
            case TownLayout.Riverside:
            {
                // A quay along the river (the town's docks: no shore there), a high road along the town, and lanes down to the water.
                int quayTop = water.Count > 0 ? water.Min(c => c.Y) - 2 : d - 6;
                for (int x = 0; x < w; x++)
                {
                    int top = water.Where(c => c.X == x).Select(c => c.Y).DefaultIfEmpty(quayTop + 2).Min();
                    for (int y = Math.Max(0, top - 2); y < top; y++) g[x, y] = under[x, y] = Ground.Paving;
                }
                int main = Math.Clamp(quayTop - Between(9, 11), t0y + 4, d - 5);
                cy = main;
                Road(0, main, w - 1, main, High, true);
                // Back streets along the town above it, as many as there's room for.
                var backs = new List<int>();
                for (int y = main - Between(15, 17); y > t0y + 5; y -= Between(15, 17))
                {
                    int x0 = t0x + rnd.Next(0, 5), x1 = t1x - rnd.Next(0, 5);
                    Road(x0, y, x1, y, Lane, false);
                    backs.Add(y);
                }
                for (int x = t0x + Between(4, 7); x < t1x - 2; x += Between(14, 17))
                {
                    Road(x, main + 3, x, quayTop - 1, Lane, false);
                    int end = backs.Count > 0 ? backs[^1] : main - (int)((main - t0y) * (0.5 + rnd.NextDouble() * 0.5));
                    if (rnd.NextDouble() < 0.75) Road(x + rnd.Next(-2, 3), main - 2, x, Math.Max(t0y, end), Lane, false);
                }
                break;
            }
        }

        // The market square where the high roads meet: paved, the roads stopping at its edge.
        var square = (X0: 0, Y0: 0, X1: -1, Y1: -1);
        if (o.Square)
        {
            int sw = Math.Clamp(w / 5, 10, 18), sd = Math.Clamp(d / 5, 9, 14);
            square = (Math.Clamp(cx - sw / 2 + 1, 1, w - sw - 1), Math.Clamp(cy - sd / 2 + 1, 1, d - sd - 1), 0, 0);
            square = (square.X0, square.Y0, square.X0 + sw - 1, square.Y0 + sd - 1);
            for (int y = square.Y0; y <= square.Y1; y++)
                for (int x = square.X0; x <= square.X1; x++)
                    if (g[x, y] != Ground.Water) g[x, y] = under[x, y] = Ground.Paving;
            for (int i = roads.Count - 1; i >= 0; i--)
            {
                var r = roads[i];
                var parts = CityEdit.Outside(r.Road, square.X0, square.Y0, square.X1 + 1, square.Y1 + 1);
                roads.RemoveAt(i);
                // What's left of a lane on the far side of the square only if it's more than a stub.
                foreach (var part in parts.Where(q => q.Length >= (r.Main ? 1 : 5)))
                    roads.Insert(i, (part with { Id = Guid.NewGuid() }, part.Cells(w, d).ToHashSet(), r.Main, r.Across));
            }
        }
        bool OnSquare(int x, int y) => x >= square.X0 && x <= square.X1 && y >= square.Y0 && y <= square.Y1;
        // How far out from the middle a square is: 0 there, 1 at the town's edge (its wall, or the map's) that way.
        double Far(int x, int y)
        {
            double ex = x >= cx ? (o.Walls ? wall.X1 : w) - cx : cx - (o.Walls ? wall.X0 : 0);
            double ey = y >= cy ? (o.Walls ? wall.Y1 : d) - cy : cy - (o.Walls ? wall.Y0 : 0);
            return Math.Max(Math.Abs(x - cx) / Math.Max(1, ex), Math.Abs(y - cy) / Math.Max(1, ey));
        }

        // The town wall all the way round (opening wherever a road goes through it, stopping either side of the
        // river), a guard tower astride each corner and others along it; nothing built against it, either side.
        var fences = new List<Fence>();
        var buildings = new List<CityBuilding>();
        var taken = new HashSet<(int X, int Y)>();
        var clear = new HashSet<(int X, int Y)>();
        var notes = new List<string>();
        var towers = new List<Prop>();
        if (o.Walls)
        {
            fences.Add(new Fence { Kind = FenceKind.CityWall, Closed = true, Points = [new(wall.X0, wall.Y0), new(wall.X1, wall.Y0), new(wall.X1, wall.Y1), new(wall.X0, wall.Y1)] });
            Ground At(int x, int y) => x >= 0 && y >= 0 && x < w && y < d ? g[x, y] : Ground.Grass;
            for (int y = wall.Y0 - 1; y <= wall.Y1; y++)
                foreach (int x in new[] { wall.X0 - 1, wall.X0, wall.X1 - 1, wall.X1 }) clear.Add((x, y));
            for (int x = wall.X0 - 1; x <= wall.X1; x++)
                foreach (int y in new[] { wall.Y0 - 1, wall.Y0, wall.Y1 - 1, wall.Y1 }) clear.Add((x, y));
            void Tower(int px, int py)
            {
                towers.Add(new Prop { Kind = PropKind.GuardTower, X = px, Y = py });
                foreach (var c in Cells(px - 2, py - 2, 4, 4)) clear.Add(c);
            }
            foreach (var (tx, ty) in new[] { (wall.X0, wall.Y0), (wall.X1, wall.Y0), (wall.X0, wall.Y1), (wall.X1, wall.Y1) }) Tower(tx, ty);
            // Along each side every dozen squares or so, clear of the corners, gates and river.
            bool Clear(int px, int py) => Enumerable.Range(px - 3, 6).All(x => Enumerable.Range(py - 3, 6).All(y => At(x, y) is not (Ground.Road or Ground.Water or Ground.Sand)));
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
                        Tower(px, py);
                    }
                }
        }

        int noble = rnd.Next(4);
        Place(g, w, d, taken, clear, buildings, rnd, o.Density, o.Theme, o.SquareMm, o.Prefabs, names, big, (x, y, onSquare) =>
        {
            if (onSquare) return "Market";
            if (!Inside(x, y)) return o.Walls ? "Farm" : null;
            if (water.Count > 0 && water.Any(c => Math.Abs(c.X - x) + Math.Abs(c.Y - y) <= 5)) return "Docks";
            double far = Far(x, y);
            if (o.Walls && (x <= wall.X0 + 4 || x >= wall.X1 - 5 || y <= wall.Y0 + 4 || y >= wall.Y1 - 5) && GateNear(g, x, y)) return "Military";
            if (far < 0.3) return "Market";
            if (far < 0.6) return (x >= cx ? 1 : 0) + (y >= cy ? 2 : 0) == noble ? "Noble" : "Residential";
            if (far < 0.85) return "Residential";
            return o.Walls ? "Slums" : rnd.NextDouble() < 0.5 ? "Farm" : "Residential";
        }, OnSquare, Inside, firstOnSquare: o.Square ? ["Temple", "Hall", "Tavern"] : []);

        // Farms' fields outside the walls (or round the edge of an open town), with hedges, clear of the buildings.
        var props = new List<Prop>(towers);
        int fields = 0;
        for (int tries = 0; tries < (w * d) / 60 && fields < (w * d) / 300 + 2; tries++)
        {
            int fw = rnd.Next(5, 9), fd = rnd.Next(4, 7), fx = rnd.Next(1, Math.Max(2, w - fw - 1)), fy = rnd.Next(1, Math.Max(2, d - fd - 1));
            var cells = Cells(fx, fy, fw, fd);
            if (cells.Any(c => Inside(c.X, c.Y) && (o.Walls || Far(c.X, c.Y) < 0.75))) continue;
            if (Cells(fx - 1, fy - 1, fw + 2, fd + 2).Any(c => c.X < 0 || c.Y < 0 || c.X >= w || c.Y >= d || g[c.X, c.Y] != Ground.Grass || taken.Contains(c) || clear.Contains(c))) continue;
            foreach (var (x, y) in cells) { g[x, y] = under[x, y] = Ground.Field; taken.Add((x, y)); }
            foreach (var c in Cells(fx - 1, fy - 1, fw + 2, fd + 2)) clear.Add(c);
            var kind = rnd.NextDouble() < 0.6 ? FenceKind.Hedge : FenceKind.Fence;
            fences.Add(new Fence { Kind = kind, Closed = true, Points = [new(fx, fy), new(fx + fw, fy), new(fx + fw, fy + fd), new(fx, fy + fd)] });
            fields++;
        }

        // The square: a fountain (or on a small one a well) in the middle, carts, crates and a statue, lamps at its corners.
        if (o.Square)
        {
            double mx = (square.X0 + square.X1 + 1) / 2.0, my = (square.Y0 + square.Y1 + 1) / 2.0;
            bool fountain = square.X1 - square.X0 >= 3 && square.Y1 - square.Y0 >= 3;
            if (fountain)
            {
                // Two squares by two, on the grid.
                mx = Math.Floor(mx + 0.5);
                my = Math.Floor(my + 0.5);
            }
            props.Add(new Prop { Kind = fountain ? PropKind.Fountain : PropKind.Well, X = mx, Y = my });
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
            bool IsRoad(int x, int y) => x >= 0 && y >= 0 && x < w && y < d && g[x, y] == Ground.Road;
            bool Way(int x, int y) => (x >= square.X0 && x <= square.X1 && (IsRoad(x, square.Y0 - 1) || IsRoad(x, square.Y1 + 1)))
                || (y >= square.Y0 && y <= square.Y1 && (IsRoad(square.X0 - 1, y) || IsRoad(square.X1 + 1, y)));
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
            var inside = Enumerable.Range(square.X0 + 1, Math.Max(0, square.X1 - square.X0 - 1)).SelectMany(x => Enumerable.Range(square.Y0 + 1, Math.Max(0, square.Y1 - square.Y0 - 1)).Select(y => (x, y))).OrderBy(_ => rnd.Next()).ToList();
            // A grim town has its gallows.
            if (o.Theme is "Dungeon" or "Gothic")
                _ = Stand(PropKind.Gallows, 2, 2, 0, xs.Select(x => (x - 1, square.Y0))) || Stand(PropKind.Gallows, 2, 2, 0, xs.Select(x => (x - 1, square.Y1 - 1))) || Stand(PropKind.Gallows, 2, 2, 0, inside);
            // Stalls along the bottom and the top, facing in.
            int stalls = Math.Clamp((square.X1 - square.X0) / 3, 2, 5);
            for (int i = 0; i < stalls; i++)
                if (!Stand(PropKind.Stall, 2, 1, 180, xs.Select(x => (x - 1, square.Y1))))
                    Stand(PropKind.Stall, 2, 1, 0, xs.Select(x => (x - 1, square.Y0)));
            // A bar, a notice board and a trough along its left or right side, or else its top or bottom, or else inside it.
            var sides = Enumerable.Range(square.Y0 + 1, Math.Max(0, square.Y1 - square.Y0 - 1)).SelectMany(y => new[] { (square.X0, y), (square.X1, y) }).OrderBy(_ => rnd.Next()).ToList();
            var ends = Enumerable.Range(square.X0 + 1, Math.Max(0, square.X1 - square.X0 - 1)).SelectMany(x => new[] { (x, square.Y0), (x, square.Y1) }).OrderBy(_ => rnd.Next()).ToList();
            foreach (var (kind, len) in new[] { (PropKind.Bar, 2), (PropKind.NoticeBoard, 1), (PropKind.Trough, 2) })
                _ = Stand(kind, 1, len, 90, sides) || Stand(kind, len, 1, 0, ends) || Stand(kind, 1, len, 90, inside) || Stand(kind, len, 1, 0, inside);
            // The well moves off to the side when the fountain has the middle.
            if (fountain) _ = Stand(PropKind.Well, 1, 1, 0, sides) || Stand(PropKind.Well, 1, 1, 0, inside);
            for (int i = 0; i < Math.Max(2, (square.X1 - square.X0) / 3); i++)
                Stand(i % 2 == 0 ? PropKind.Cart : PropKind.Crates, 1, 1, rnd.Next(4) * 90, inside);
        }
        // Lamps along the high roads, on the grass at their edge (the top or left side), every ninth square.
        foreach (var r in roads.Where(r => r.Main))
        {
            int i = 0;
            foreach (var c in r.Cells.OrderBy(c => r.Across ? c.X : c.Y).ThenBy(c => r.Across ? c.Y : c.X))
            {
                var beside = r.Across ? (X: c.X, Y: c.Y - 1) : (X: c.X - 1, Y: c.Y);
                if (beside.X < 0 || beside.Y < 0 || g[beside.X, beside.Y] != Ground.Grass || taken.Contains(beside) || clear.Contains(beside) || !Inside(c.X, c.Y)) continue;
                if (i++ % 9 == 4) props.Add(new Prop { Kind = PropKind.Lamp, X = r.Across ? c.X + 0.5 : c.X - 0.2, Y = r.Across ? c.Y - 0.2 : c.Y + 0.5 });
            }
        }
        // Trees: gardens in town, woods outside, a few rocks; none against a building, the wall or a road.
        double phaseT = rnd.NextDouble() * 10;
        for (int y = 0; y < d; y++)
            for (int x = 0; x < w; x++)
            {
                if (g[x, y] != Ground.Grass || taken.Contains((x, y)) || clear.Contains((x, y)) || NextToRoad(g, x, y, w, d)) continue;
                double wood = 0.5 + 0.5 * Math.Sin(x / 5.3 + phaseT) * Math.Cos(y / 4.1 - phaseT);
                bool town = Inside(x, y) && Far(x, y) < (o.Walls ? 2 : 0.8);
                double p = town ? 0.04 : 0.05 + 0.4 * wood * wood;
                if (rnd.NextDouble() < p)
                    props.Add(new Prop { Kind = rnd.NextDouble() < (town ? 0.2 : 0.35) ? PropKind.Pine : rnd.NextDouble() < 0.15 ? PropKind.Bush : PropKind.Tree, X = x + 0.3 + rnd.NextDouble() * 0.4, Y = y + 0.3 + rnd.NextDouble() * 0.4, Size = 0.9 + rnd.NextDouble() * 0.6, TurnDeg = rnd.Next(360) });
                else if (!town && rnd.NextDouble() < 0.006)
                    props.Add(new Prop { Kind = PropKind.Boulder, X = x + 0.5, Y = y + 0.5, Size = 0.8 + rnd.NextDouble() * 0.6, TurnDeg = rnd.Next(360) });
            }

        // The square's name, and the river's along it where no bridge crosses (the roads carry their own names).
        var labels = new List<CityLabel>();
        if (o.Square) labels.Add(new CityLabel { Text = "Market Square", X = (square.X0 + square.X1 + 1) / 2.0, Y = square.Y0 + 0.7, Size = 0.8 });
        if (water.Count > 0)
        {
            var bridged = roads.SelectMany(r => r.Cells).Where(water.Contains).ToHashSet();
            // A row (or column) of the river with no bridge over it, a fifth of the way along.
            var lines = water.GroupBy(c => riverside ? c.X : c.Y).Where(l => !l.Any(bridged.Contains)).ToList();
            if (lines.Count > 0)
            {
                double target = (riverside ? w : d) * 0.2;
                var line = lines.OrderBy(l => Math.Abs(l.Key - target)).First();
                double lx = riverside ? line.Key + 0.5 : line.Average(c => c.X) + 0.5, ly = riverside ? line.Average(c => c.Y) + 0.5 : line.Key + 0.5;
                labels.Add(new CityLabel { Text = Rivers[rnd.Next(Rivers.Length)], X = lx, Y = ly, Size = 0.8, TurnDeg = riverside ? 0 : -90 });
            }
        }

        // The ground as it is under the roads (grass, shore, the river under its bridges): the roads lie on top of it.
        var final = (Ground[,])g.Clone();
        for (int y = 0; y < d; y++)
            for (int x = 0; x < w; x++)
                if (g[x, y] == Ground.Road) final[x, y] = under[x, y];
        var map = CityMap.Empty(w, d, string.IsNullOrWhiteSpace(o.Name) ? "New town" : o.Name) with { Theme = o.Theme, SquareMm = o.SquareMm };
        map = map.WithGround(final) with { Buildings = buildings, Roads = [.. roads.Select(r => r.Road)], Fences = fences, Props = props, Labels = labels };

        int fromSaved = buildings.Count(b => b.PrefabId is not null);
        notes.Add($"{buildings.Count} buildings" + (fromSaved > 0 ? $" ({fromSaved} of them your saved buildings)" : "") + $", {roads.Count} roads, {props.Count(p => p.Kind is PropKind.Tree or PropKind.Pine or PropKind.Bush)} trees.");
        if (o.Walls)
        {
            int gates = WallLayout.Of(map, f => f.Kind == FenceKind.CityWall).Gates.Count;
            notes.Add($"A town wall with {gates} gate{(gates == 1 ? "" : "s")} and {towers.Count} guard tower{(towers.Count == 1 ? "" : "s")} on it, one at each corner.");
        }
        if (fields > 0) notes.Add($"{fields} field{(fields == 1 ? "" : "s")} with hedges and fences.");
        notes.Add("By district: " + string.Join(", ", buildings.GroupBy(b => b.District).OrderByDescending(x => x.Count()).Select(x => $"{x.Count()} {x.Key.ToLowerInvariant()}")) + ".");
        return (map, notes);
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

    /// <summary>The eight squares round a square (some may be off the map).</summary>
    static IEnumerable<(int X, int Y)> Neighbours8(int x, int y)
    {
        for (int j = y - 1; j <= y + 1; j++)
            for (int i = x - 1; i <= x + 1; i++)
                if (i != x || j != y) yield return (i, j);
    }

    /// <summary>The squares either side of every fence and wall line (kept clear of buildings), and round its guard towers.</summary>
    static void BesideFences(CityMap m, HashSet<(int X, int Y)> clear)
    {
        foreach (var f in m.Fences)
            foreach (var (a, b) in f.Segments())
            {
                double len = a.DistanceTo(b);
                int n = Math.Max(1, (int)Math.Ceiling(len * 4));
                for (int i = 0; i <= n; i++)
                {
                    var p = a + (b - a) * (i / (double)n);
                    foreach (double ox in new[] { -0.45, 0.45 })
                        foreach (double oy in new[] { -0.45, 0.45 })
                            clear.Add(((int)Math.Floor(p.X + ox), (int)Math.Floor(p.Y + oy)));
                }
            }
        foreach (var t in m.Props.Where(p => p.Kind == PropKind.GuardTower))
        {
            double r = WallLayout.TowerReach(t);
            for (int y = (int)Math.Floor(t.Y - r); y < Math.Ceiling(t.Y + r); y++)
                for (int x = (int)Math.Floor(t.X - r); x < Math.Ceiling(t.X + r); x++) clear.Add((x, y));
        }
    }

    // ---- plots along the roads ----

    /// <summary>
    /// Buildings along the roads (and round paved squares) on grass that's free and not kept <paramref name="clear"/>:
    /// for every stretch of road, plots side by side facing it (as close as <paramref name="density"/> says, but
    /// never touching: a square of ground round each), each one's kind from its part of town
    /// (<paramref name="districtAt"/>; null leaves the plot empty) and no more of a kind than a town that size has
    /// (one smithy, one stable, each trade's shop once), a saved building where one of the right district and kind
    /// suits it, otherwise one made up of the size such a building is on a battle map. Returns how many were put up.
    /// </summary>
    static int Place(Ground[,] g, int w, int d, HashSet<(int X, int Y)> taken, HashSet<(int X, int Y)> clear, List<CityBuilding> buildings, Random rnd,
        double density, string theme, double squareMm, IReadOnlyList<PrefabInfo> prefabs, Names names, int big,
        Func<int, int, bool, string?> districtAt, Func<int, int, bool> onSquare, Func<int, int, bool> allowed, string[] firstOnSquare)
    {
        density = Math.Clamp(density, 0, 1);
        int count = 0, number = buildings.Count;
        var made = buildings.GroupBy(b => b.Type, StringComparer.OrdinalIgnoreCase).ToDictionary(x => x.Key, x => x.Count(), StringComparer.OrdinalIgnoreCase);
        int Made(string type) => made.GetValueOrDefault(type);
        bool Room(string type) => Made(type) < Limit(type, big) && names.Left(type);
        bool Road(int x, int y) => x >= 0 && y >= 0 && x < w && y < d && g[x, y] is Ground.Road or Ground.Paving;
        bool Free(int x, int y) => x >= 0 && y >= 0 && x < w && y < d && g[x, y] is Ground.Grass or Ground.Dirt && !taken.Contains((x, y)) && !clear.Contains((x, y));

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

        // A kind for a plot in a district: one the town hasn't its fill of yet, or else the district's everyday one.
        string Choose(string district)
        {
            var kinds = Kinds.GetValueOrDefault(district) ?? Kinds["Residential"];
            for (int i = 0; i < 4; i++)
            {
                string t = ForTheme(Pick(kinds, rnd), theme);
                if (Room(t)) return t;
            }
            return ForTheme(Smaller(district), theme);
        }

        // A made-up building of a kind on the plot at a front, as big as fits (down to its least; a little less when
        // squeezed onto the square), its long side to the road if it won't fit end on; null if none does.
        CityBuilding? MadeUp(string type, string district, int x, int y, int face, bool squeeze = false)
        {
            var (fx, fy, ax, ay) = faces[face];
            var (f0, f1, d0, d1, s0, s1) = Size(type);
            if (squeeze) (f0, d0) = (Math.Max(3, f0 * 3 / 4), Math.Max(3, d0 * 3 / 4));
            int front = rnd.Next(f0, f1 + 1), deep = rnd.Next(d0, d1 + 1);
            HashSet<(int X, int Y)>? plot = null;
            foreach (bool turned in new[] { false, true })
                for (int fr = turned ? deep : front; fr >= (turned ? d0 : f0) && plot is null; fr--)
                    for (int dp = turned ? front : deep; dp >= (turned ? f0 : d0) && plot is null; dp--)
                    {
                        var p = Plot(x, y, fx, fy, ax, ay, fr, dp);
                        if (p.All(c => Free(c.X, c.Y) && (allowed(c.X, c.Y) || district == "Farm"))) plot = p;
                    }
            if (plot is null) return null;
            var towards = Around(plot).Where(c => Road(c.X, c.Y)).ToHashSet();
            var seen = new SeenBuilding { Cells = plot, Storeys = rnd.Next(s0, s1 + 1), Type = type, Covering = Covering(type, theme, rnd), DoorTowards = towards };
            if (BuildingGuess.Make(seen, theme, squareMm) is not { } m) return null;
            return new CityBuilding { Type = type, District = district, X = m.X, Y = m.Y, Design = m.Design };
        }
        var skipTo = new Dictionary<int, (int X, int Y)>();
        // Puts a building up: its name (a family's, a trade's, a tavern's sign) given now, and a square of ground
        // kept clear all round it so the next one doesn't touch it.
        List<(int X, int Y)> Add(CityBuilding b, string? name = null)
        {
            number++;
            if (name is not null) names.Use(name);
            b = b with { Name = name ?? names.For(b.Type, number) ?? $"{b.Type} {number}" };
            var own = CityFootprint.Cells(b).ToList();
            foreach (var c in own) taken.Add(c);
            foreach (var (x, y) in own)
                foreach (var n in Neighbours8(x, y))
                    if (!taken.Contains(n)) clear.Add(n);
            buildings.Add(b);
            made[b.Type] = Made(b.Type) + 1;
            count++;
            return own;
        }

        // The square's own buildings first (its temple, hall and tavern), each at the first plot facing it where it fits.
        foreach (var kind in firstOnSquare)
        {
            string type = ForTheme(kind, theme);
            if (!Room(type)) continue;
            foreach (var (x, y, face) in fronts)
            {
                if (!Free(x, y) || !onSquare(x + faces[face].Fx, y + faces[face].Fy)) continue;
                if ((kind == "Temple" ? "Temple" : districtAt(x, y, true)) is not { } district || MadeUp(type, district, x, y, face, squeeze: true) is not { } b) continue;
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
            string type = Choose(district);

            // A saved building of this district and kind first (any kind the town has room for, if none is), as its size.
            CityBuilding? b = null;
            string? name = null;
            var fits = prefabs.Where(p => (string.Equals(p.District, district, StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(p.District) || p.District == "Anywhere")
                && (string.IsNullOrWhiteSpace(p.Type) || Made(p.Type) < Limit(p.Type, big))).ToList();
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
                        b = fit with { Id = Guid.NewGuid(), District = string.IsNullOrWhiteSpace(p.District) || p.District == "Anywhere" ? district : p.District };
                        if (!string.IsNullOrWhiteSpace(p.Name) && !names.Has(p.Name)) name = p.Name;
                        break;
                    }
                }
            }
            // Made up, as big as such a building is; a smaller everyday one where it won't fit.
            b ??= MadeUp(type, district, x, y, face);
            if (b is null && ForTheme(Smaller(district), theme) is var small && small != type) b = MadeUp(small, district, x, y, face);
            if (b is null) continue;
            var own = Add(b, name);
            // The next plot along: just past the square of ground round it in a packed street, otherwise a square or two on.
            int gap = rnd.NextDouble() < density * 0.8 ? 0 : rnd.Next(1, 2 + (int)Math.Round((1 - density) * 4));
            int far = ax == 1 ? own.Max(c => c.X) : own.Max(c => c.Y);
            skipTo[face] = ax == 1 ? (far + 2 + gap, y) : (x, far + 2 + gap);
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
        m = m.Tidied();
        var g = m.Paved().GroundGrid();
        var taken = m.Buildings.SelectMany(CityFootprint.Cells).ToHashSet();
        // A square of ground round every building kept clear, and either side of the fences and walls.
        var clear = new HashSet<(int X, int Y)>();
        foreach (var (x, y) in taken)
            foreach (var n in Neighbours8(x, y))
                if (!taken.Contains(n)) clear.Add(n);
        BesideFences(m, clear);
        var area = box ?? (0, 0, m.Across, m.Down);
        bool Inside(int x, int y) => x >= area.X0 && y >= area.Y0 && x < area.X1 && y < area.Y1;
        var buildings = new List<CityBuilding>(m.Buildings);
        var d = string.IsNullOrWhiteSpace(district) ? "Residential" : district.Trim();
        var names = new Names(rnd, m.Buildings, m.Roads);
        int added = Place(g, m.Across, m.Down, taken, clear, buildings, rnd, density, m.Theme, m.SquareMm, prefabs, names, m.Across * m.Down / 6000,
            (x, y, _) => Inside(x, y) ? d : null, (_, _) => false, Inside, []);
        if (added == 0) return (m, 0);
        var fresh = buildings.Skip(m.Buildings.Count).ToList();
        var under = fresh.SelectMany(CityFootprint.Cells).ToHashSet();
        return (m with
        {
            Buildings = buildings,
            Props = [.. m.Props.Where(p => !under.Contains(((int)Math.Floor(p.X), (int)Math.Floor(p.Y))))],
        }, added);
    }
}
