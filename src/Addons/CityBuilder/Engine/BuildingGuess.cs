using PhotonCanvas.Addon.Buildings;
using PhotonCanvas.Addon.HouseEditor;

namespace PhotonCanvas.Addon.CityBuilder;

/// <summary>What's known about a building seen from above: the squares it covers, and a few guesses to make it from.</summary>
public sealed record SeenBuilding
{
    /// <summary>The squares it covers (map squares).</summary>
    public HashSet<(int X, int Y)> Cells { get; init; } = [];
    /// <summary>Walls seen inside it (between two of its squares), map grid edges.</summary>
    public List<(GridPt A, GridPt B)> InnerWalls { get; init; } = [];
    /// <summary>Squares outside it a door should open onto (a road or a square), if any are next to it.</summary>
    public HashSet<(int X, int Y)> DoorTowards { get; init; } = [];
    /// <summary>The roof's covering (one of the roof maker's), or null for the theme's.</summary>
    public string? Covering { get; init; }
    public int Storeys { get; init; } = 1;
    public string Type { get; init; } = "House";
    /// <summary>How the building was seen: "roof" (from above) or "plan" (its rooms drawn).</summary>
    public string From { get; init; } = "roof";
    /// <summary>
    /// A flat roof you can stand on instead of a pitched one: a deck over the top floor with railings round it and
    /// the roof's clutter on it (vents, tanks, antennae, signs). Null: flat in the sci-fi look, pitched otherwise.
    /// </summary>
    public bool? RoofDeck { get; init; }
    /// <summary>
    /// The floor (0 = the ground floor) from which the floors above step in a square all round, leaving a terrace
    /// with railings round them (a tower's setback); null for none. Only where what's left is at least 3 by 3.
    /// </summary>
    public int? SetbackFrom { get; init; }
    /// <summary>Picks the roof's clutter.</summary>
    public int Seed { get; init; } = 1;
}

/// <summary>
/// Makes a building editor design from a footprint: walls round the squares (and any walls seen inside),
/// as many floors as guessed, a door towards the road, windows spaced along the walls clear of the
/// corners, and roofs over it, a rectangle at a time. A best guess to change afterwards.
/// </summary>
public static class BuildingGuess
{
    /// <summary>The building's own look for a theme: the building editor's start, in that theme.</summary>
    public static BuildingSpec StyleFor(string? theme) => BuildingThemes.Apply(HouseDesign.DefaultStyle(), theme) with { Items = [] };

    /// <summary>
    /// The design for a footprint, with its top left corner at 0, 0, and where that corner is on the map.
    /// Null when there's nothing to make (no squares).
    /// </summary>
    public static (HouseDesign Design, int X, int Y)? Make(SeenBuilding f, string? theme, double squareMm)
    {
        if (f.Cells.Count == 0) return null;
        int x0 = f.Cells.Min(c => c.X), y0 = f.Cells.Min(c => c.Y);
        var cells = f.Cells.Select(c => (X: c.X - x0, Y: c.Y - y0)).ToHashSet();
        var style = StyleFor(theme);
        bool sci = IsSciFi(theme);
        bool deck = f.RoofDeck ?? sci;
        // A city's buildings each laid out afresh, so no two show the same pattern of panels and fittings.
        if (sci) style = style with { Seed = 1000 + Math.Abs(f.Seed % 100000), Texture = Outside(f.Type).Ground, UpperTexture = Outside(f.Type).Upper };
        // A roof deck is a floor of its own on top.
        int storeys = Math.Clamp(f.Storeys, 1, HouseKitMaker.MaxLevels - (deck ? 1 : 0));
        var outer = Outline(cells);
        var inner = f.InnerWalls
            .Select(w => WallEdge.Of(new GridPt(w.A.X - x0, w.A.Y - y0), new GridPt(w.B.X - x0, w.B.Y - y0)))
            .Where(e => !outer.Contains(e)).ToHashSet();
        var towards = f.DoorTowards.Select(c => (c.X - x0, c.Y - y0)).ToHashSet();
        // The squares the upper floors keep when they step in (a square in all round, if that leaves 3 by 3).
        var upperCells = cells;
        if (f.SetbackFrom is int from && from > 0 && from < storeys)
        {
            var inset = cells.Where(c => Neighbours(c).All(cells.Contains)).ToHashSet();
            int iw = inset.Count == 0 ? 0 : inset.Max(c => c.X) - inset.Min(c => c.X) + 1, id = inset.Count == 0 ? 0 : inset.Max(c => c.Y) - inset.Min(c => c.Y) + 1;
            if (iw >= 3 && id >= 3 && inset.Count >= 9 && Rectangles(inset).Count == 1) upperCells = inset;
        }
        var levels = new List<Level>();
        var looks = new Random(f.Seed * 31 + 7);
        for (int k = 0; k < storeys; k++)
        {
            var own = k >= (f.SetbackFrom ?? int.MaxValue) ? upperCells : cells;
            var ownOuter = own == cells ? outer : Outline(own);
            var walls = k == 0 ? ownOuter.Concat(inner).ToList() : [.. ownOuter];
            var openings = Openings(walls, own, k == 0 ? towards : null, style, k == 0, squareMm > 0 ? squareMm : style.SquareMm);
            levels.Add(new Level { Walls = walls, Openings = openings, WallLooks = sci ? CityLooks(f.Type, k, ownOuter, own, k == 0 ? towards : [], style, looks) : [] });
        }
        if (deck)
        {
            var top = levels.Count > 1 && (f.SetbackFrom ?? int.MaxValue) < storeys ? upperCells : cells;
            levels.Add(new Level { Furniture = Rooftop(top, style, f.Seed) });
            var d0 = new HouseDesign { Style = style, SquareMm = squareMm > 0 ? squareMm : style.SquareMm, Levels = levels, UpperFloor = FloorThemesFor(theme), VaryWalls = sci };
            return (d0, x0, y0);
        }
        var cover = f.Covering ?? style.RoofTexture;
        var roofs = Rectangles(cells).Select(r =>
        {
            var area = RoofArea.Over(storeys - 1, new GridPt(r.X0, r.Y0), new GridPt(r.X1 + 1, r.Y1 + 1), style);
            int w = r.X1 - r.X0 + 1, d = r.Y1 - r.Y0 + 1;
            return area with
            {
                Kind = w == d && w >= 3 ? RoofKind.Hip : RoofKind.Gable,
                Covering = cover,
                PitchDeg = cover == "Thatch" ? 50 : style.RoofPitchDeg,
            };
        }).ToList();
        var design = new HouseDesign { Style = style, SquareMm = squareMm > 0 ? squareMm : style.SquareMm, Levels = levels, Roofs = roofs };
        return (design, x0, y0);
    }

    /// <summary>A cyberpunk city building's outside walls by what it is: the ground floor's, then the floors above.</summary>
    public static (string Ground, string? Upper) Outside(string type) => type switch
    {
        "Corp tower" or "Spire" => (BuildingThemes.GlassFacade, BuildingThemes.GlassFacade),
        "Data centre" or "Capsule hotel" => (BuildingThemes.HabConcrete, BuildingThemes.GlassFacade),
        "Cargo depot" or "Factory" or "Hangar" or "Power plant" or "Hydroponics" or "Warehouse" or "Workshop" => (BuildingThemes.SteelCladding, BuildingThemes.SteelCladding),
        "Stack" => (BuildingThemes.SteelCladding, BuildingThemes.HabConcrete),
        _ => (BuildingThemes.HabConcrete, null),
    };

    /// <summary>Kinds with a shop at street level (a shutter, a sign box and posters on the walls to the road).</summary>
    static bool StreetShop(string type) => type is "Neon bar" or "Noodle bar" or "Tech shop" or "Arcade" or "Clinic" or "Shop" or "Canteen" or "Hab block";

    /// <summary>
    /// A city building's walls of a floor that look different from the rest: the ground floor's walls to the road a
    /// shopfront (on a hab block, now and then), and on hab blocks and stacks the odd wall clad in steel.
    /// </summary>
    static List<WallLook> CityLooks(string type, int level, HashSet<WallEdge> outer, HashSet<(int X, int Y)> cells, HashSet<(int, int)> towards, BuildingSpec style, Random rnd)
    {
        var list = new List<WallLook>();
        var plan = PlanShape.Of([.. outer]);
        bool shop = level == 0 && StreetShop(type) && (type != "Hab block" || rnd.NextDouble() < 0.45);
        foreach (var run in plan.Runs)
        {
            var edges = run.Points.Zip(run.Points.Skip(1)).Select(p => WallEdge.Of(p.First, p.Second)).ToList();
            bool toRoad = run.Points.Zip(run.Points.Skip(1)).Any(p => towards.Contains(Outside(p.First, p.Second, cells)));
            string? tex = null;
            if (shop && toRoad) tex = BuildingThemes.Shopfront;
            else if (type is "Hab block" or "Stack" && level > 0 && rnd.NextDouble() < 0.18) tex = BuildingThemes.SteelCladding;
            else if (type == "Stack" && rnd.NextDouble() < 0.3) tex = BuildingThemes.HabConcrete;
            if (tex is null || tex == (level > 0 && style.UpperTexture is { } u ? u : style.Texture)) continue;
            foreach (var e in edges) list.Add(new WallLook { Edge = e, Texture = tex, Wear = 0.5 });
        }
        return list;
    }

    static bool IsSciFi(string? theme) => theme is not null && theme.Contains("sci", StringComparison.OrdinalIgnoreCase);

    static string FloorThemesFor(string? theme) => PhotonCanvas.Addon.FloorTiles.FloorThemes.Texture(PhotonCanvas.Addon.Buildings.BuildingThemes.Names.FirstOrDefault(n => string.Equals(n, theme, StringComparison.OrdinalIgnoreCase)) ?? theme);

    static IEnumerable<(int X, int Y)> Neighbours((int X, int Y) c)
    {
        for (int j = -1; j <= 1; j++)
            for (int i = -1; i <= 1; i++)
                if (i != 0 || j != 0) yield return (c.X + i, c.Y + j);
    }

    /// <summary>
    /// What stands on a flat roof: the roof's clutter from the sci-fi catalog (fuel tanks, a server rack for the air
    /// plant, a cargo crate, a beacon mast, a holo sign, a light pylon), one to a square, about one to every four
    /// squares of roof, kept off its edge squares where the roof is big enough to have a middle.
    /// </summary>
    public static List<PlacedItem> Rooftop(HashSet<(int X, int Y)> cells, BuildingSpec style, int seed)
    {
        string[] ids = ["sci-fuel-drums", "sci-server-rack", "sci-beacon", "sci-signpost", "sci-cargo-crate", "sci-street-lamp", "sci-tech-cabinet", "sci-reactor-core"];
        var entries = ids.Select(PhotonCanvas.Addon.Furniture.FurnitureCatalog.Find).OfType<PhotonCanvas.Addon.Furniture.CatalogEntry>().ToList();
        if (entries.Count == 0 || cells.Count == 0) return [];
        var rnd = new Random(seed);
        var middle = cells.Where(c => Neighbours(c).All(cells.Contains)).ToList();
        var spots = (middle.Count >= 2 ? middle : [.. cells]).OrderBy(_ => rnd.Next()).ToList();
        int want = Math.Clamp(cells.Count / 4, 1, 5);
        var used = new HashSet<(int X, int Y)>();
        var list = new List<PlacedItem>();
        // A sign or a mast on most roofs, then the rest.
        var order = new List<PhotonCanvas.Addon.Furniture.CatalogEntry>();
        var tall = entries.Where(e => e.Id is "sci-beacon" or "sci-signpost" or "sci-street-lamp").ToList();
        if (tall.Count > 0) order.Add(tall[rnd.Next(tall.Count)]);
        while (order.Count < want) order.Add(entries[rnd.Next(entries.Count)]);
        foreach (var e in order)
        {
            // A square of its own, none of its neighbours taken (so nothing stands against anything else).
            int i = spots.FindIndex(c => !used.Contains(c) && !Neighbours(c).Any(used.Contains));
            if (i < 0) break;
            var at = spots[i];
            used.Add(at);
            var item = HouseKitMaker.FromCatalog(style, e);
            list.Add(new PlacedItem { X = at.X + 0.5, Y = at.Y + 0.5, Turn = rnd.Next(4), Item = item });
        }
        return list;
    }

    /// <summary>The grid edges round a set of squares: wherever a square meets one that isn't in it.</summary>
    public static HashSet<WallEdge> Outline(HashSet<(int X, int Y)> cells)
    {
        var res = new HashSet<WallEdge>();
        foreach (var (x, y) in cells)
        {
            if (!cells.Contains((x, y - 1))) res.Add(WallEdge.Of(new(x, y), new(x + 1, y)));
            if (!cells.Contains((x, y + 1))) res.Add(WallEdge.Of(new(x, y + 1), new(x + 1, y + 1)));
            if (!cells.Contains((x - 1, y))) res.Add(WallEdge.Of(new(x, y), new(x, y + 1)));
            if (!cells.Contains((x + 1, y))) res.Add(WallEdge.Of(new(x + 1, y), new(x + 1, y + 1)));
        }
        return res;
    }

    /// <summary>Where openings go along a straight wall of <paramref name="length"/> squares: about one every two squares, evenly, clear of the ends.</summary>
    public static List<double> Spots(int length)
    {
        if (length < 2) return [];
        int n = Math.Max(1, length / 2);
        return [.. Enumerable.Range(0, n).Select(i => length * (i + 0.5) / n)];
    }

    /// <summary>Where a city block's windows go along a wall <paramref name="length"/> squares long: as many as fit a square apart, clear of its ends.</summary>
    static IEnumerable<double> Rows(int length, double clear)
    {
        double span = length - 2 * clear;
        if (span < 0) yield break;
        int n = (int)Math.Floor(span) + 1;
        for (int i = 0; i < n; i++) yield return n == 1 ? length / 2.0 : clear + span * i / (n - 1);
    }

    static List<Opening> Openings(List<WallEdge> walls, HashSet<(int X, int Y)> cells, HashSet<(int, int)>? towards, BuildingSpec style, bool door, double sq)
    {
        var outer = Outline(cells);
        var plan = PlanShape.Of(walls);
        var list = new List<Opening>();
        // How far (squares) a door's and a window's middle must stay from a wall's ends, clear of the columns there.
        // (A box wall's panel stops at the column's side, its frame 3 mm and its end a further 5 mm in: 9 mm with a little over.)
        double doorClear = (style.ColumnMm / 2 + style.DoorWidthMm / 2 + 10) / sq, windowClear = (style.ColumnMm / 2 + style.WindowWidthMm / 2 + 9) / sq;
        // How far apart two windows' middles must be (their frames and a gap between).
        double windowApart = (style.WindowWidthMm + 8) / sq;
        double apart = (style.DoorWidthMm / 2 + style.WindowWidthMm / 2 + 6) / sq;
        // Only the walls round the outside get doors and windows.
        var runs = plan.Runs.Where(r => r.Points.Zip(r.Points.Skip(1)).All(p => outer.Contains(WallEdge.Of(p.First, p.Second)))).ToList();
        (Run Run, double At)? front = null;
        if (door)
        {
            double best = double.MinValue;
            foreach (var r in runs)
            {
                if (r.Steps < 2 * doorClear) continue;
                // The squares just outside this wall the door would like to open onto (a gap drawn in the wall, a road).
                var wanted = new List<double>();
                for (int i = 0; i < r.Steps; i++)
                {
                    var o = Outside(r.Points[i], r.Points[i + 1], cells);
                    if (towards is not null && towards.Contains(o)) wanted.Add(i + 0.5);
                }
                double mid = r.Steps / 2.0;
                // In the middle of the square nearest the wall's middle that it should open onto, kept clear of the ends; else the middle.
                double at = wanted.Count > 0 ? Math.Clamp(wanted.OrderBy(t => Math.Abs(t - mid)).First(), doorClear, r.Steps - doorClear) : mid;
                // Towards the road most, else the front (the bottom of the map), else the longest wall.
                bool facesFront = r.Step.Y == 0 && Outside(r.Points[0], r.Points[1], cells).Y == r.Points[0].Y;
                double score = wanted.Count * 100 + (facesFront ? 10 : 0) + r.Steps;
                if (score > best) { best = score; front = (r, at); }
            }
        }
        foreach (var r in runs)
        {
            double? doorAt = front is { } f && f.Run == r ? f.At : null;
            if (doorAt is { } d)
            {
                var (dx, dy) = PointAlong(r, d);
                list.Add(Opening.NewDoor(style, dx, dy));
            }
            double? last = null;
            // A city block's windows come in rows, evenly along the wall a square or so apart.
            foreach (var spot in IsSciFi(style.Theme) ? Rows(r.Steps, windowClear) : Spots(r.Steps))
            {
                // One a little too near an end is moved in, clear of its column, if that leaves it clear of the last.
                if (r.Steps < 2 * windowClear) break;
                double t = Math.Clamp(spot, windowClear, r.Steps - windowClear);
                if (last is { } lt && t - lt < windowApart) continue;
                if (doorAt is { } dd && Math.Abs(dd - t) < apart) continue;
                last = t;
                var (x, y) = PointAlong(r, t);
                list.Add(Opening.NewWindow(style, x, y));
            }
        }
        return list;
    }

    static (double X, double Y) PointAlong(Run r, double t) => (r.A.X + r.Step.X * t, r.A.Y + r.Step.Y * t);

    /// <summary>The square on the outside of a one-square wall (the side that isn't in the building).</summary>
    static (int X, int Y) Outside(GridPt a, GridPt b, HashSet<(int X, int Y)> cells)
    {
        if (a.Y == b.Y)
        {
            int x = Math.Min(a.X, b.X);
            return cells.Contains((x, a.Y)) ? (x, a.Y - 1) : (x, a.Y);
        }
        int y = Math.Min(a.Y, b.Y);
        return cells.Contains((a.X, y)) ? (a.X - 1, y) : (a.X, y);
    }

    /// <summary>The squares split into rectangles, biggest first (each square in one): the roofs' shapes.</summary>
    public static List<(int X0, int Y0, int X1, int Y1)> Rectangles(HashSet<(int X, int Y)> cells)
    {
        var left = new HashSet<(int X, int Y)>(cells);
        var res = new List<(int, int, int, int)>();
        while (left.Count > 0)
        {
            int bx0 = 0, by0 = 0, bx1 = -1, by1 = -1, bestArea = 0;
            foreach (var (sx, sy) in left)
            {
                // The widest rectangles with this square as their top left corner.
                int maxW = 0;
                while (left.Contains((sx + maxW, sy))) maxW++;
                int w = maxW;
                for (int h = 1; w > 0; h++)
                {
                    int rowW = 0;
                    while (rowW < w && left.Contains((sx + rowW, sy + h - 1))) rowW++;
                    w = Math.Min(w, rowW);
                    if (w == 0) break;
                    // Prefer squarer pieces a little, so an L doesn't become one long sliver and a stub.
                    int area = w * h;
                    if (area > bestArea || (area == bestArea && Math.Abs(w - h) < Math.Abs((bx1 - bx0 + 1) - (by1 - by0 + 1))))
                    {
                        bestArea = area;
                        (bx0, by0, bx1, by1) = (sx, sy, sx + w - 1, sy + h - 1);
                    }
                }
            }
            if (bestArea == 0) break;
            for (int y = by0; y <= by1; y++)
                for (int x = bx0; x <= bx1; x++) left.Remove((x, y));
            res.Add((bx0, by0, bx1, by1));
        }
        return res;
    }

    /// <summary>A building's likely use and height from its size and shape.</summary>
    public static (string Type, int Storeys) Kind(HashSet<(int X, int Y)> cells)
    {
        int n = cells.Count;
        if (n == 0) return ("House", 1);
        int w = cells.Max(c => c.X) - cells.Min(c => c.X) + 1, d = cells.Max(c => c.Y) - cells.Min(c => c.Y) + 1;
        bool full = n == w * d;
        if (full && w == d && w is >= 2 and <= 3) return ("Tower", 3);
        if (n <= 4) return ("Shed", 1);
        if (n <= 10) return ("House", 1);
        if (n <= 24) return ("House", 2);
        if (n <= 48) return ("Hall", 2);
        return ("Keep", 2);
    }
}
