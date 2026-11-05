using PhotonCanvas.Addon.Terrain;

namespace PhotonCanvas.Addon.Furniture;

/// <summary>Where a piece goes in a room.</summary>
public enum FurnitureSpot
{
    /// <summary>Anywhere on the floor (tables, chairs, stands).</summary>
    Free,
    /// <summary>On the floor with its back to a wall (cabinets, fireplaces, beds' heads).</summary>
    Wall,
    /// <summary>Glued flat on a wall, off the floor (hangings, wall shelves).</summary>
    OnWall,
    /// <summary>Flat on the floor; other furniture can stand on it (rugs).</summary>
    Floor,
}

/// <summary>One ready-made piece: a piece of furniture already set up to suit a theme and a room.</summary>
/// <param name="Id">Stays the same between versions, so a saved building can find its pieces again.</param>
public sealed record CatalogEntry(string Id, string Name, string Theme, string Room, FurnitureItem Item, string About)
{
    public FurnitureSpot Spot => FurnitureCatalog.SpotFor(Item);
}

/// <summary>
/// Ready-made furniture for every theme (the wall maker's: Medieval, Dungeon, Cave, Sci-fi, Desert,
/// Gothic) and room: a four-poster and a wardrobe for a medieval bedroom, lockers and bunks for a
/// ship's quarters, a sarcophagus for a gothic crypt. Each entry's item carries its theme, so it keeps
/// its look in a set or a building of another theme. Sizes suit 28 mm figures: use <see cref="Sized"/>.
/// </summary>
public static class FurnitureCatalog
{
    public const string Bedroom = "Bedroom";
    public const string Kitchen = "Kitchen";
    public const string Hall = "Hall & tavern";
    public const string Study = "Study & library";
    public const string Workshop = "Workshop & forge";
    public const string Storage = "Storage";
    public const string Chapel = "Chapel & temple";
    public const string Armoury = "Armoury & guardroom";
    public const string Throne = "Throne room & bridge";
    public const string Prison = "Prison & crypt";
    public const string Lab = "Lab & infirmary";
    /// <summary>Outdoor pieces for the city builder: town walls, gates, towers and the street.</summary>
    public const string Town = "Town";

    /// <summary>The rooms, in the order to show them.</summary>
    public static IReadOnlyList<string> Rooms { get; } = [Bedroom, Kitchen, Hall, Study, Workshop, Storage, Chapel, Armoury, Throne, Prison, Lab, Town];

    public static IReadOnlyList<string> Themes => TerrainThemes.Names;

    static readonly Lazy<IReadOnlyList<CatalogEntry>> all = new(Build);

    /// <summary>Every entry, by theme then room.</summary>
    public static IReadOnlyList<CatalogEntry> All => all.Value;

    /// <summary>The entries for a theme and a room (null for any).</summary>
    public static IEnumerable<CatalogEntry> For(string? theme, string? room) =>
        All.Where(e => (theme is null || string.Equals(e.Theme, theme, StringComparison.OrdinalIgnoreCase)) && (room is null || e.Room == room));

    public static CatalogEntry? Find(string? id) => id is null ? null : All.FirstOrDefault(e => e.Id == id);

    /// <summary>An item (sized for 28 mm figures) resized for other figures.</summary>
    public static FurnitureItem Sized(FurnitureItem it, double figuresMm) => Math.Abs(figuresMm - 28) < 1e-9 ? it : Scale(it, figuresMm / 28);

    /// <summary>Every size in the item multiplied by k (rounded to 0.1 mm).</summary>
    public static FurnitureItem Scale(FurnitureItem it, double k) => it with
    {
        WidthMm = R(it.WidthMm * k), DepthMm = R(it.DepthMm * k), HeightMm = it.Kind == ItemKind.Rug ? it.HeightMm : R(it.HeightMm * k),
        SeatMm = R(it.SeatMm * k), FootMm = R(it.FootMm * k), MattressMm = R(it.MattressMm * k),
        LegMm = R(it.LegMm * k), ArmMm = R(it.ArmMm * k), HangMm = R(it.HangMm * k),
    };

    static double R(double v) => Math.Round(v, 1);

    public static FurnitureSpot SpotFor(FurnitureItem it) => it.Kind switch
    {
        ItemKind.Rug => FurnitureSpot.Floor,
        ItemKind.WallHanging or ItemKind.WallShelf or ItemKind.WallDoor => FurnitureSpot.OnWall,
        ItemKind.Fireplace when it.Hearth == HearthStyle.Spit => FurnitureSpot.Free,
        ItemKind.Cabinet or ItemKind.Bookshelf or ItemKind.Fireplace or ItemKind.Bed or ItemKind.WeaponRack or ItemKind.Desk => FurnitureSpot.Wall,
        ItemKind.Armchair when it.Seat is SeatStyle.Sofa or SeatStyle.Pew or SeatStyle.Settle => FurnitureSpot.Free,
        ItemKind.SlantDesk when it.Slant == SlantStyle.Console => FurnitureSpot.Wall,
        _ => FurnitureSpot.Free,
    };

    // ---- the catalog ----

    sealed class Book(string theme, string key)
    {
        public readonly List<CatalogEntry> Entries = [];
        public string Room = Bedroom;

        public void Add(string name, ItemKind kind, Func<FurnitureItem, FurnitureItem> edit, string about, string? slug = null)
        {
            var it = edit(Defaults.For(kind)) with { Theme = theme, Name = name };
            string id = key + "-" + (slug ?? Slug(name));
            if (Entries.Any(e => e.Id == id)) id += "-" + Slug(Room);
            Entries.Add(new(id, name, theme, Room, it, about));
        }

        static string Slug(string s)
        {
            s = s.Replace("'", "").ToLowerInvariant();
            return string.Join("-", s.Split(s.Where(c => !char.IsLetterOrDigit(c)).Distinct().ToArray(), StringSplitOptions.RemoveEmptyEntries));
        }
    }

    /// <summary>
    /// The town wall pieces and guard tower every theme gets, with ids the city builder looks for:
    /// "{key}-wall-1", "-wall-2", "-wall-corner-out", "-wall-corner-in", "-wall-gate", "-wall-end", "-guard-tower".
    /// </summary>
    static void TownWalls(Book b, string wall, string about)
    {
        b.Room = Town;
        double sq = Furniture.TownWalls.Square, d = 19, H = 56;
        b.Add($"{wall} (1 square)", ItemKind.TownWall, i => i with { Wall = WallPiece.Straight, WidthMm = sq, DepthMm = d, HeightMm = H }, $"One square of {about}.", "wall-1");
        b.Add($"{wall} (2 squares)", ItemKind.TownWall, i => i with { Wall = WallPiece.Straight, WidthMm = 2 * sq, DepthMm = d, HeightMm = H }, $"Two squares of {about}.", "wall-2");
        b.Add($"{wall} corner (outside)", ItemKind.TownWall, i => i with { Wall = WallPiece.OutsideCorner, WidthMm = sq + d / 2, DepthMm = d, HeightMm = H }, "A corner turning one square each way, the parapet on the outside of the turn.", "wall-corner-out");
        b.Add($"{wall} corner (inside)", ItemKind.TownWall, i => i with { Wall = WallPiece.InsideCorner, WidthMm = sq + d / 2, DepthMm = d, HeightMm = H }, "A corner turning one square each way, the parapet on the inside of the turn.", "wall-corner-in");
        b.Add("Gatehouse", ItemKind.TownWall, i => i with { Wall = WallPiece.Gate, WidthMm = 2 * sq, DepthMm = d + 8, HeightMm = H }, "Two squares: a tower over the gate passage, its portcullis raised and the doors open.", "wall-gate");
        b.Add($"{wall} end", ItemKind.TownWall, i => i with { Wall = WallPiece.End, WidthMm = sq, DepthMm = d + 8, HeightMm = H }, "One square of wall ending in a turret, with a door onto the wall walk.", "wall-end");
        b.Add("Guard tower", ItemKind.Tower, i => i with { WidthMm = 2 * sq - 4, DepthMm = 2 * sq - 4, HeightMm = 92 }, "A tall tower on two squares by two: a door, arrow loops on every side, merlons and a flag.", "guard-tower");
    }

    /// <summary>
    /// The town props every theme gets, with ids the city builder looks for: "{key}-tree-{style}" for each tree given,
    /// "-horse-trough", "-stage", "-market-stall", "-bar" and "-notice-board".
    /// </summary>
    static void TownProps(Book b, (string Name, TreeStyle Style, string About)[] trees,
        string trough, string stage, string market, string bar, string board)
    {
        b.Room = Town;
        foreach (var (name, style, about) in trees)
            b.Add(name, ItemKind.Tree, i => i with { Tree = style }, about, "tree-" + style.ToString().ToLowerInvariant());
        b.Add("Horse trough", ItemKind.Trough, i => i, trough, "horse-trough");
        b.Add("Players' stage", ItemKind.Stage, i => i, stage, "stage");
        b.Add("Market stall", ItemKind.Stall, i => i with { Stall = StallStyle.Market }, market, "market-stall");
        b.Add("Outdoor bar", ItemKind.Stall, i => i with { Stall = StallStyle.Bar, WidthMm = 40 }, bar, "bar");
        b.Add("Notice board", ItemKind.Stall, i => i with { Stall = StallStyle.NoticeBoard, WidthMm = 24, DepthMm = 10, HeightMm = 36 }, board, "notice-board");
    }

    static IReadOnlyList<CatalogEntry> Build()
    {
        var list = new List<CatalogEntry>();
        list.AddRange(Medieval());
        list.AddRange(Dungeon());
        list.AddRange(Cave());
        list.AddRange(SciFi());
        list.AddRange(Desert());
        list.AddRange(Gothic());
        // Shown by theme, then by room in the rooms' order.
        var order = TerrainThemes.Names.ToList();
        return [.. list.Select((e, i) => (e, i)).OrderBy(x => order.IndexOf(x.e.Theme)).ThenBy(x => Rooms.ToList().IndexOf(x.e.Room)).ThenBy(x => x.i).Select(x => x.e)];
    }

    static List<CatalogEntry> Medieval()
    {
        var b = new Book(TerrainThemes.Medieval, "med");
        b.Room = Bedroom;
        b.Add("Four-poster bed", ItemKind.Bed, i => i with { BedStyle = BedStyle.FourPoster, WidthMm = 34, DepthMm = 20, HeightMm = 34, FootMm = 10 }, "A lord's bed with a canopy on four posts and a scalloped valance.");
        b.Add("Plain bed", ItemKind.Bed, i => i with { FootMm = 12 }, "A wooden bed: corner posts with turned knobs, an arched head board carved with the theme's symbol, a panelled foot board and a quilted blanket.");
        b.Add("Wardrobe", ItemKind.Cabinet, i => i with { Front = CabinetFront.Panelled, Crown = Crown.Cornice, WidthMm = 22, DepthMm = 11, HeightMm = 36 }, "Two panelled doors under a cornice.");
        b.Add("Chest of drawers", ItemKind.Cabinet, i => i with { Front = CabinetFront.Drawers, Crown = Crown.Flat, WidthMm = 22, DepthMm = 10, HeightMm = 18, Rows = 3, Columns = 2 }, "Three rows of drawers, waist high.");
        b.Add("Blanket chest", ItemKind.Chest, i => i with { WidthMm = 26, DepthMm = 12, HeightMm = 11 }, "A banded chest for the foot of the bed: an arcade of arches across its front with lilies carved behind them, and a handle on each end.");
        b.Add("Bedside candles", ItemKind.Candlestand, i => i with { WidthMm = 10, HeightMm = 22 }, "A three-branch candelabra.");
        b.Add("Bedroom rug", ItemKind.Rug, i => i with { Rug = RugPattern.Round, WidthMm = 32, DepthMm = 20 }, "An oval rug: a fringe, a scrolling vine round its border and the theme's symbol in the middle.");
        b.Add("Tapestry", ItemKind.WallHanging, i => i with { Hanging = HangingStyle.Tapestry, WidthMm = 18, HeightMm = 24 }, "A tapestry on a rail, with a shield worked into it.");
        b.Add("Dressing screen", ItemKind.Screen, i => i with { ScreenStyle = ScreenStyle.Tracery, WidthMm = 28, HeightMm = 28 }, "A three-leaf folding screen: pointed arches pierced through each leaf over a carved panel.");

        b.Room = Kitchen;
        b.Add("Dresser with plates", ItemKind.Cabinet, i => i with { Front = CabinetFront.Hutch, Contents = ShelfContents.Plates, Crown = Crown.Cornice, WidthMm = 24, DepthMm = 12, HeightMm = 36 }, "A cupboard with open shelves of plates above.");
        b.Add("Bread oven", ItemKind.Fireplace, i => i with { Hearth = HearthStyle.Oven, WidthMm = 26, DepthMm = 16, HeightMm = 24 }, "A domed brick oven with a wood store under it.");
        b.Add("Kitchen table", ItemKind.Table, i => i with { Legs = LegStyle.Trestle, Stretcher = true, WidthMm = 34, DepthMm = 14 }, "A long scrubbed table on shaped trestles, a carved stretcher between them.");
        b.Add("Larder cupboard", ItemKind.Cabinet, i => i with { Front = CabinetFront.Doors, Crown = Crown.Flat, WidthMm = 18, DepthMm = 10, HeightMm = 30, Columns = 2 }, "A tall cupboard for the stores.");
        b.Add("Spice shelf", ItemKind.WallShelf, i => i, "A shelf on two brackets.");
        b.Add("Stool", ItemKind.Stool, i => i, "A round stool.");

        b.Room = Hall;
        b.Add("Great fireplace", ItemKind.Fireplace, i => i with { Hearth = HearthStyle.Fireplace, WidthMm = 30, DepthMm = 11, HeightMm = 42 }, "A brick fireplace with a mantel and a chimney breast.");
        b.Add("Long table", ItemKind.Table, i => i with { Legs = LegStyle.Trestle, Stretcher = true, WidthMm = 44, DepthMm = 16 }, "A feasting table on shaped trestles, a carved stretcher between them.");
        b.Add("Bench", ItemKind.Bench, i => i with { WidthMm = 36 }, "A plank bench.");
        b.Add("Bar counter", ItemKind.Cabinet, i => i with { Front = CabinetFront.Panelled, Crown = Crown.Flat, WidthMm = 40, DepthMm = 12, HeightMm = 16, Columns = 4 }, "A long panelled counter.");
        b.Add("Bottle shelves", ItemKind.Cabinet, i => i with { Front = CabinetFront.Open, Contents = ShelfContents.Bottles, Crown = Crown.Cornice, WidthMm = 24, DepthMm = 8, HeightMm = 32, Shelves = 3 }, "Open shelves of bottles behind the bar.");
        b.Add("Armchair", ItemKind.Armchair, i => i with { Seat = SeatStyle.Wooden, BackTop = BackTop.Pointed, HeightMm = 23, ArmMm = 13 }, "A carved wooden armchair: a pointed back between pinnacles, a trefoil pierced through it over the theme's symbol, little arches under the arms.");
        b.Add("Settle", ItemKind.Armchair, i => i with { Seat = SeatStyle.Settle, Columns = 2, WidthMm = 24, BackTop = BackTop.Square }, "A two-seat settle for by the fire: an arcade carved across its back, little arches under the arms and quatrefoils pierced through its front.");
        b.Add("Hall rug", ItemKind.Rug, i => i with { Rug = RugPattern.Kilim, WidthMm = 40, DepthMm = 24 }, "A woven rug with stepped diamonds.");
        b.Add("Banner", ItemKind.WallHanging, i => i with { Hanging = HangingStyle.Banner, WidthMm = 14, HeightMm = 26, HangMm = 14 }, "A swallow-tailed heraldic banner: a quartered shield with the theme's symbol in each quarter.");

        b.Room = Study;
        b.Add("Bookcase", ItemKind.Cabinet, i => i with { Front = CabinetFront.Open, Contents = ShelfContents.Books, Crown = Crown.Cornice, WidthMm = 22, DepthMm = 8, HeightMm = 34, Shelves = 3 }, "Open shelves of books under a cornice.");
        b.Add("Writing desk", ItemKind.Desk, i => i, "A desk with a drawer, papers, a book and an inkwell.");
        b.Add("Lectern", ItemKind.SlantDesk, i => i with { Slant = SlantStyle.Lectern, WidthMm = 10 }, "A book stand with an open book.");
        b.Add("Map table", ItemKind.SlantDesk, i => i with { Slant = SlantStyle.MapTable, WidthMm = 24, DepthMm = 16, HeightMm = 14 }, "A low sloped table with a map on it.");
        b.Add("Reading chair", ItemKind.Armchair, i => i with { Seat = SeatStyle.Reading, BackTop = BackTop.Pointed, HeightMm = 25, ArmMm = 13 }, "A carved wing chair: a buttoned back under a pointed crest with a quatrefoil pierced through it, pinnacles at its corners and little arches under the arms.");
        b.Add("Map", ItemKind.WallHanging, i => i with { Hanging = HangingStyle.FramedMap, WidthMm = 22, HeightMm = 20, HangMm = 12 }, "A map in a heavy frame with carved corner blocks, hung by two chains from a ring.");

        b.Room = Workshop;
        b.Add("Smithy forge", ItemKind.Fireplace, i => i with { Hearth = HearthStyle.Forge, WidthMm = 26, DepthMm = 14, HeightMm = 34 }, "A forge with a fire bed, a hood and flames.");
        b.Add("Workbench", ItemKind.Table, i => i with { Legs = LegStyle.Slab, Stretcher = true, ToolRack = true, WidthMm = 32, DepthMm = 12, HeightMm = 14 }, "A heavy bench with solid ends, a rack of tools along its back and a vice on the corner.");
        b.Add("Anvil", ItemKind.Stand, i => i with { Stand = StandStyle.Anvil, WidthMm = 13, DepthMm = 8, HeightMm = 11 }, "A smith's anvil on a tree stump.");
        b.Add("Tool cupboard", ItemKind.Cabinet, i => i with { Front = CabinetFront.Lattice, Contents = ShelfContents.Tools, Shelves = 1, Crown = Crown.Cornice, WidthMm = 20, DepthMm = 9, HeightMm = 30 }, "A cupboard with pierced lattice doors and tools hung on pegs inside.");

        b.Room = Storage;
        b.Add("Crate", ItemKind.Crate, i => i, "A braced crate.");
        b.Add("Fruit crate", ItemKind.Crate, i => i with { CrateStyle = CrateStyle.Slatted, WidthMm = 16, DepthMm = 13, HeightMm = 9, Lid = LidStyle.None, Load = CrateLoad.Apples }, "An open crate of slats heaped with apples.");
        b.Add("Iron-bound crate", ItemKind.Crate, i => i with { CrateStyle = CrateStyle.Ironbound, WidthMm = 16 }, "Planks with iron brackets on the corners and a strap with a ring.");
        b.Add("Shipping crate", ItemKind.Crate, i => i with { CrateStyle = CrateStyle.Cargo, WidthMm = 16, HeightMm = 12 }, "A framed crate with its contents stencilled on the side.");
        b.Add("Chest", ItemKind.Chest, i => i with { Lid = LidStyle.Domed }, "A chest with iron bands and a domed lid.");
        b.Add("Shelving", ItemKind.Cabinet, i => i with { Front = CabinetFront.Open, Contents = ShelfContents.Stores, Crown = Crown.Flat, WidthMm = 22, DepthMm = 9, HeightMm = 30, Shelves = 2 }, "Storeroom shelves stacked with sacks, little iron-cornered boxes, pots and small barrels.");

        b.Room = Chapel;
        b.Add("Altar", ItemKind.Cabinet, i => i with { Front = CabinetFront.Altar, Crown = Crown.Flat, WidthMm = 26, DepthMm = 12, HeightMm = 16 }, "An altar with a cloth hanging down its front, a budded cross and two candlesticks standing on it.");
        b.Add("Pew", ItemKind.Armchair, i => i with { Seat = SeatStyle.Pew, Columns = 3, WidthMm = 30, DepthMm = 10, HeightMm = 16, SeatMm = 7.5 }, "A pew with tall ends and an arcade carved across its back.");
        b.Add("Altar candles", ItemKind.Candlestand, i => i with { WidthMm = 14, HeightMm = 26 }, "A five-branch candelabra.");

        b.Room = Armoury;
        b.Add("Weapon rack", ItemKind.WeaponRack, i => i, "Spears, a sword, an axe and a halberd in a rack.");
        b.Add("Armour stand", ItemKind.Stand, i => i with { Stand = StandStyle.Armour }, "A suit of armour on a stand.");
        b.Add("Training dummy", ItemKind.Stand, i => i with { Stand = StandStyle.Dummy }, "A dummy with a target on it.");
        b.Add("Shield", ItemKind.WallHanging, i => i with { Hanging = HangingStyle.Shield, WidthMm = 18, HeightMm = 18, HangMm = 14 }, "A shield over two crossed swords.");

        b.Room = Throne;
        b.Add("Throne", ItemKind.Armchair, i => i with { Seat = SeatStyle.Throne, BackTop = BackTop.Pointed, WidthMm = 16, DepthMm = 14, HeightMm = 34, SeatMm = 10, ArmMm = 16 }, "A throne on a two-step dais: a buttoned back under a pointed crest with a quatrefoil pierced through it, pinnacles at its corners and pointed arches pierced under the arms.");
        b.Add("Long carpet", ItemKind.Rug, i => i with { Rug = RugPattern.Runner, WidthMm = 60, DepthMm = 18 }, "A runner up to the throne: lilies down a dark field inside a border with a vine waving along it, a fringe at each end.");
        b.Add("Royal tapestry", ItemKind.WallHanging, i => i with { Hanging = HangingStyle.Tapestry, WidthMm = 24, HeightMm = 30, HangMm = 12 }, "A large tapestry.");
        b.Add("Tall clock", ItemKind.Cabinet, i => i with { Front = CabinetFront.Clock, Crown = Crown.Pediment, Columns = 1, WidthMm = 12, DepthMm = 9, HeightMm = 42 }, "A long-case clock: a dial sunk in its hood, a real pendulum hanging behind the glazed trunk door.");

        b.Room = Prison;
        b.Add("Stocks", ItemKind.Stocks, i => i, "Stocks for the village green or the gaol.");
        b.Add("Coffin", ItemKind.Coffin, i => i, "A plank coffin with a cross on the lid.");
        TownProps(b, [("Oak tree", TreeStyle.Oak, "A spreading oak, its trunk forking into heavy boughs, leaves etched and light showing through the crown."),
                      ("Pine tree", TreeStyle.Pine, "A tall pine in stacked tiers of needles on a straight trunk.")],
            "A stone horse trough on a plinth, water in it, an iron pump at one end.",
            "A players' stage for the market square: planked deck, steps up the front, a painted backdrop and curtains tied back.",
            "A market stall under a striped awning, crates of produce on the counter.",
            "An outdoor tavern bar: barrels along the front, a shelf below the counter and a hanging tankard sign.",
            "The town notice board under its little shingled roof, notices pinned up.");
        TownWalls(b, "Town wall", "stone town wall with a wall walk, merlons with cross loops and a corbelled course");
        return b.Entries;
    }

    static List<CatalogEntry> Dungeon()
    {
        var b = new Book(TerrainThemes.Dungeon, "dun");
        b.Room = Bedroom;
        b.Add("Straw pallet", ItemKind.Bed, i => i with { BedStyle = BedStyle.Plain, WidthMm = 30, DepthMm = 15, HeightMm = 12, FootMm = 9, MattressMm = 6 }, "A rough plank bed with a straw mattress, a pillow and a patched blanket.");
        b.Add("Guard's bunk", ItemKind.Bed, i => i with { BedStyle = BedStyle.Bunk, WidthMm = 32, DepthMm = 15, HeightMm = 30, MattressMm = 6, FootMm = 11 }, "Two bunks, one over the other.");
        b.Add("Footlocker", ItemKind.Chest, i => i with { WidthMm = 16, DepthMm = 10, HeightMm = 9, Lid = LidStyle.Domed }, "An iron-bound footlocker with a domed lid.");
        b.Add("Fur rug", ItemKind.Rug, i => i with { Rug = RugPattern.Hide, WidthMm = 26, DepthMm = 18 }, "An animal hide.");

        b.Room = Kitchen;
        b.Add("Cook fire", ItemKind.Fireplace, i => i with { Hearth = HearthStyle.Spit, WidthMm = 26, DepthMm = 14, HeightMm = 18 }, "A ring of stones round a fire, a roast on a spit over it and a cauldron hanging beside.");
        b.Add("Rough table", ItemKind.Table, i => i with { Legs = LegStyle.Trestle, WidthMm = 30, DepthMm = 14 }, "A table on trestles.");
        b.Add("Stool", ItemKind.Stool, i => i with { Top = TopShape.Round }, "A crude round stool on splayed legs.");

        b.Room = Hall;
        b.Add("Long table", ItemKind.Table, i => i with { Legs = LegStyle.Trestle, WidthMm = 40, DepthMm = 14 }, "A long trestle table.");
        b.Add("Bench", ItemKind.Bench, i => i with { WidthMm = 32 }, "A plank bench.");
        b.Add("Torch stand", ItemKind.Candlestand, i => i with { Candle = CandleStyle.Torch, WidthMm = 8, HeightMm = 24 }, "A burning torch on a stand.");
        b.Add("Banner", ItemKind.WallHanging, i => i with { Hanging = HangingStyle.Banner, WidthMm = 12, HeightMm = 26, HangMm = 14 }, "A tattered war banner hung on chains from a spear: burnt dark, a great skull left light, torn through.");

        b.Room = Study;
        b.Add("Scroll shelves", ItemKind.Cabinet, i => i with { Front = CabinetFront.Open, Contents = ShelfContents.Scrolls, Crown = Crown.Flat, WidthMm = 20, DepthMm = 8, HeightMm = 30, Shelves = 3 }, "Shelves of rolled scrolls.");
        b.Add("Lectern", ItemKind.SlantDesk, i => i with { Slant = SlantStyle.Lectern, WidthMm = 10 }, "A book stand.");

        b.Room = Workshop;
        b.Add("Forge", ItemKind.Fireplace, i => i with { Hearth = HearthStyle.Forge, WidthMm = 26, DepthMm = 14, HeightMm = 34 }, "A forge with a hood and flames.");
        b.Add("Workbench", ItemKind.Table, i => i with { Legs = LegStyle.Slab, Stretcher = true, ToolRack = true, WidthMm = 30, DepthMm = 12, HeightMm = 14 }, "A heavy bench with a rack of tools and a vice.");
        b.Add("Anvil", ItemKind.Stand, i => i with { Stand = StandStyle.Anvil, WidthMm = 13, DepthMm = 8, HeightMm = 11 }, "A smith's anvil on a tree stump.");

        b.Room = Storage;
        b.Add("Crate", ItemKind.Crate, i => i, "A braced crate.");
        b.Add("Iron-bound crate", ItemKind.Crate, i => i with { CrateStyle = CrateStyle.Ironbound, WidthMm = 14 }, "Planks with iron brackets on the corners and a skull on the front.");
        b.Add("Supply crate", ItemKind.Crate, i => i with { CrateStyle = CrateStyle.CrossBraced, WidthMm = 14, Handles = true }, "A cross-braced crate with rope handles on the ends.");
        b.Add("Treasure chest", ItemKind.Chest, i => i with { WidthMm = 18, DepthMm = 11, HeightMm = 10, Lid = LidStyle.Open, Load = CrateLoad.Treasure }, "An iron-bound chest thrown open, overflowing with gold coins, a crown and a goblet.");
        b.Add("Storage shelving", ItemKind.Cabinet, i => i with { Front = CabinetFront.Open, Contents = ShelfContents.Stores, Crown = Crown.Flat, Shelves = 1, WidthMm = 24, DepthMm = 9, HeightMm = 22 }, "Rough open shelving stacked with sacks, pots, jugs and a little chest.", slug: "rack-of-shelves");
        b.Add("Barred cupboard", ItemKind.Cabinet, i => i with { Front = CabinetFront.Lattice, Contents = ShelfContents.Jars, Crown = Crown.Flat, WidthMm = 22, DepthMm = 10, HeightMm = 26 }, "A plank cupboard with iron-barred doors, jars and pots seen through the bars.");

        b.Room = Chapel;
        b.Add("Dark altar", ItemKind.Cabinet, i => i with { Front = CabinetFront.Altar, Crown = Crown.Flat, Detail = WoodDetail.Stone, WidthMm = 26, DepthMm = 12, HeightMm = 16 }, "A stone altar: skulls set in pointed niches either side of a cloth worked with a skull, two skulls and two candlesticks on top.");
        b.Add("Torch", ItemKind.Candlestand, i => i with { Candle = CandleStyle.Torch, WidthMm = 8, HeightMm = 26 }, "A torch on a stand.");
        b.Add("Wall torch", ItemKind.WallHanging, i => i with { Hanging = HangingStyle.Torch, WidthMm = 7, DepthMm = 12, HeightMm = 16, HangMm = 14 }, "A burning torch in an iron bracket on a plank post: an iron arm on a strut, the fire basket at its end.");

        b.Room = Armoury;
        b.Add("Weapon rack", ItemKind.WeaponRack, i => i, "Spears, a sword, an axe and a halberd in a rack.");
        b.Add("Armour stand", ItemKind.Stand, i => i with { Stand = StandStyle.Armour }, "A suit of armour on a stand.");
        b.Add("Training dummy", ItemKind.Stand, i => i with { Stand = StandStyle.Dummy }, "A straw man on a post, bound with cord, its chest hacked with sword cuts.");
        b.Add("Shield", ItemKind.WallHanging, i => i with { Hanging = HangingStyle.Shield, WidthMm = 18, HeightMm = 18, HangMm = 14 }, "A shield over crossed swords.");
        b.Add("Weapons cupboard", ItemKind.Cabinet, i => i with { Front = CabinetFront.Lattice, Contents = ShelfContents.Weapons, Shelves = 1, Crown = Crown.Flat, WidthMm = 22, DepthMm = 10, HeightMm = 30 }, "A tall cupboard with iron-barred doors, axes, a sword and a spear hung on pegs inside.");

        b.Room = Throne;
        b.Add("Warlord's throne", ItemKind.Armchair, i => i with { Seat = SeatStyle.Throne, BackTop = BackTop.Pointed, WidthMm = 16, DepthMm = 14, HeightMm = 30, SeatMm = 10, ArmMm = 14 }, "A throne on a dais.");
        b.Add("Hide rug", ItemKind.Rug, i => i with { Rug = RugPattern.Hide, WidthMm = 34, DepthMm = 22 }, "A great hide before the throne.");

        b.Room = Prison;
        b.Add("Stocks", ItemKind.Stocks, i => i, "Stocks for a prisoner.");
        b.Add("Pallet", ItemKind.Bed, i => i with { BedStyle = BedStyle.Pallet, WidthMm = 28, DepthMm = 13, MattressMm = 5, Bedding = false }, "A bare pallet in a cell.");
        b.Add("Coffin", ItemKind.Coffin, i => i, "A plank coffin.");
        b.Add("Stone sarcophagus", ItemKind.Coffin, i => i with { Coffin = CoffinStyle.Sarcophagus, WidthMm = 36, DepthMm = 14, HeightMm = 10, Detail = WoodDetail.Stone }, "A tomb with an effigy on the lid.");

        b.Room = Lab;
        b.Add("Potion shelves", ItemKind.Cabinet, i => i with { Front = CabinetFront.Open, Contents = ShelfContents.Jars, Crown = Crown.Flat, WidthMm = 20, DepthMm = 8, HeightMm = 28, Shelves = 3 }, "Shelves of jars.");
        b.Add("Torturer's table", ItemKind.Table, i => i with { Legs = LegStyle.Slab, WidthMm = 30, DepthMm = 13, HeightMm = 12 }, "A heavy slab table.");
        TownProps(b, [("Dead tree", TreeStyle.Dead, "A gnarled dead tree, bare twisted branches clawing at the sky.")],
            "A worn stone trough on a heavy plinth, a pump at one end.",
            "A grim stage for proclamations and punishments, its backdrop and curtains faded.",
            "A black-market stall with a ragged awning and crates of dubious goods.",
            "A rough drinking bar of barrels and planks with a hanging sign.",
            "A board of bounties and wanted notices under a little roof.");
        TownWalls(b, "Fortress wall", "grim fortress wall of great worn blocks, its merlons knocked about");
        return b.Entries;
    }

    static List<CatalogEntry> Cave()
    {
        var b = new Book(TerrainThemes.Cave, "cav");
        b.Room = Bedroom;
        b.Add("Fur pallet", ItemKind.Bed, i => i with { BedStyle = BedStyle.Pallet, WidthMm = 30, DepthMm = 15, MattressMm = 5 }, "A mattress on log sleepers with a fur.");
        b.Add("Hide rug", ItemKind.Rug, i => i with { Rug = RugPattern.Hide, WidthMm = 28, DepthMm = 20 }, "An animal hide.");
        b.Add("Hide chest", ItemKind.Chest, i => i with { Bands = false }, "A plain chest.");

        b.Room = Kitchen;
        b.Add("Hearth fire", ItemKind.Fireplace, i => i with { Hearth = HearthStyle.Spit, WidthMm = 26, DepthMm = 14, HeightMm = 18 }, "A fire in a ring of stones, a roast turning on a spit over it and a cauldron hanging beside.");
        b.Add("Slab table", ItemKind.Table, i => i with { Legs = LegStyle.Slab, WidthMm = 26, DepthMm = 14, HeightMm = 11 }, "A table on stone slabs.");
        b.Add("Stump stool", ItemKind.Stool, i => i with { Legs = LegStyle.Slab, WidthMm = 9, DepthMm = 9 }, "A sawn tree stump: bark down its sides, roots at its foot, growth rings across the seat.");
        b.Add("Pot shelf", ItemKind.Cabinet, i => i with { Front = CabinetFront.Open, Contents = ShelfContents.Jars, Crown = Crown.Flat, WidthMm = 18, DepthMm = 8, HeightMm = 22, Shelves = 2 }, "Shelves of pots.");

        b.Room = Hall;
        b.Add("Long bench", ItemKind.Bench, i => i with { WidthMm = 30 }, "A plank bench.");
        b.Add("Torch", ItemKind.Candlestand, i => i with { Candle = CandleStyle.Torch, WidthMm = 8, HeightMm = 22 }, "A torch on a stand.");
        b.Add("Chief's seat", ItemKind.Armchair, i => i with { Seat = SeatStyle.Throne, BackTop = BackTop.Rounded, WidthMm = 16, DepthMm = 14, HeightMm = 26, SeatMm = 10, ArmMm = 14 }, "A big seat on a dais.");

        b.Room = Workshop;
        b.Add("Bloomery forge", ItemKind.Fireplace, i => i with { Hearth = HearthStyle.Forge, WidthMm = 24, DepthMm = 14, HeightMm = 32 }, "A forge with flames.");
        b.Add("Work slab", ItemKind.Table, i => i with { Legs = LegStyle.Slab, WidthMm = 28, DepthMm = 12, HeightMm = 12 }, "A low work table.");

        b.Room = Storage;
        b.Add("Crate", ItemKind.Crate, i => i with { CrateStyle = CrateStyle.Lashed, Lid = LidStyle.None, Load = CrateLoad.Rocks, WidthMm = 14, DepthMm = 13, HeightMm = 9 }, "A crude crate of poles lashed together, heaped with rocks.");
        b.Add("Battened crate", ItemKind.Crate, i => i with { CrateStyle = CrateStyle.Battened }, "Upright planks held by battens.");
        b.Add("Chest", ItemKind.Chest, i => i, "A banded chest.");

        b.Room = Armoury;
        b.Add("Spear rack", ItemKind.WeaponRack, i => i, "Spears and axes in a rack.");
        b.Add("Shield", ItemKind.WallHanging, i => i with { Hanging = HangingStyle.Shield, WidthMm = 16, HeightMm = 16, HangMm = 14 }, "A shield over crossed swords.");

        b.Room = Chapel;
        b.Add("Shrine", ItemKind.Cabinet, i => i with { Front = CabinetFront.Altar, Crown = Crown.Flat, WidthMm = 22, DepthMm = 11, HeightMm = 14 }, "A small altar.");

        b.Room = Prison;
        b.Add("Burial slab", ItemKind.Coffin, i => i with { Coffin = CoffinStyle.Sarcophagus, WidthMm = 34, DepthMm = 13, HeightMm = 8, Detail = WoodDetail.Stone }, "A stone tomb.");
        TownProps(b, [("Giant mushroom", TreeStyle.Mushroom, "A giant cave mushroom on a thick stalk, gills under its cap and spots on top."),
                      ("Dead tree", TreeStyle.Dead, "A pale dead tree grown in the dark, bare twisted branches.")],
            "A trough hewn from planks and bound with iron, water inside and a pump at one end.",
            "A rough plank stage for storytellers, with a painted backdrop and curtains.",
            "A trader's stall under a hide awning, crates on the counter.",
            "A plank-and-barrel bar with a hanging tankard sign.",
            "A notice board for the cave settlement under a little plank roof.");
        TownWalls(b, "Palisade", "log palisade, every log sharpened, with a plank walk behind and lashed rails");
        return b.Entries;
    }

    static List<CatalogEntry> SciFi()
    {
        var b = new Book(TerrainThemes.SciFi, "sci");
        b.Room = Bedroom;
        b.Add("Crew bunks", ItemKind.Bed, i => i with { BedStyle = BedStyle.Bunk, WidthMm = 32, DepthMm = 15, HeightMm = 30, MattressMm = 6, FootMm = 11 }, "Two bunks, one over the other.");
        b.Add("Officer's bed", ItemKind.Bed, i => i with { FootMm = 13 }, "A single bed.");
        b.Add("Lockers", ItemKind.Cabinet, i => i with { Front = CabinetFront.Locker, Crown = Crown.Flat, Columns = 3, WidthMm = 24, DepthMm = 10, HeightMm = 34 }, "A row of three lockers.");
        b.Add("Deck mat", ItemKind.Rug, i => i with { Rug = RugPattern.Mat, WidthMm = 30, DepthMm = 18 }, "A ribbed mat with hazard ends.");
        b.Add("Wall screen", ItemKind.WallHanging, i => i with { Hanging = HangingStyle.Monitor, WidthMm = 18, HeightMm = 12, HangMm = 14 }, "A screen on the wall.");
        b.Add("Floor lamp", ItemKind.Candlestand, i => i with { Candle = CandleStyle.Lamp, WidthMm = 8, HeightMm = 26 }, "A lamp on a stand.");

        b.Room = Kitchen;
        b.Add("Galley units", ItemKind.Cabinet, i => i with { Front = CabinetFront.DrawersOverDoors, Crown = Crown.Flat, WidthMm = 26, DepthMm = 11, HeightMm = 16, Columns = 2 }, "Counter units with drawers over doors.");
        b.Add("Mess table", ItemKind.Table, i => i with { Legs = LegStyle.Slab, Top = TopShape.Square, WidthMm = 30, DepthMm = 14 }, "A table on solid ends.");
        b.Add("Mess chair", ItemKind.Chair, i => i with { Back = BackStyle.Panel }, "A chair with a panel back.");
        b.Add("Heater unit", ItemKind.Fireplace, i => i with { WidthMm = 22, DepthMm = 10, HeightMm = 30 }, "A heater with glowing fins.");

        b.Room = Hall;
        b.Add("Lounge sofa", ItemKind.Armchair, i => i with { Seat = SeatStyle.Sofa, Columns = 2, WidthMm = 24, BackTop = BackTop.Square }, "A two-seat sofa.");
        b.Add("Lounge chair", ItemKind.Armchair, i => i with { BackTop = BackTop.Square }, "An armchair.");
        b.Add("Bar counter", ItemKind.Cabinet, i => i with { Front = CabinetFront.Tech, Crown = Crown.Flat, WidthMm = 36, DepthMm = 12, HeightMm = 16, Columns = 3 }, "A counter with machine panels.");

        b.Room = Study;
        b.Add("Data shelves", ItemKind.Cabinet, i => i with { Front = CabinetFront.Open, Contents = ShelfContents.Tech, Crown = Crown.Flat, WidthMm = 22, DepthMm = 9, HeightMm = 32, Shelves = 3 }, "Racks of machines and drives.");
        b.Add("Terminal desk", ItemKind.Desk, i => i with { Contents = ShelfContents.Tech }, "A desk with a screen and a keyboard.");

        b.Room = Workshop;
        b.Add("Tool lockers", ItemKind.Cabinet, i => i with { Front = CabinetFront.Locker, Crown = Crown.Flat, Columns = 2, WidthMm = 18, DepthMm = 10, HeightMm = 32 }, "Two lockers.");
        b.Add("Work console", ItemKind.SlantDesk, i => i with { Slant = SlantStyle.Console, WidthMm = 24, DepthMm = 14, HeightMm = 16 }, "A console with screens and buttons.");
        b.Add("Work table", ItemKind.Table, i => i with { Legs = LegStyle.Slab, Top = TopShape.Square, WidthMm = 30, DepthMm = 12, HeightMm = 14 }, "A metal work table.");

        b.Room = Storage;
        b.Add("Cargo crate", ItemKind.Crate, i => i with { CrateStyle = CrateStyle.Cargo, Label = "C-742", WidthMm = 16, HeightMm = 12 }, "A metal cargo crate with its contents stencilled on the side.");
        b.Add("Ammo crate", ItemKind.Crate, i => i with { CrateStyle = CrateStyle.Military, WidthMm = 18, DepthMm = 12, HeightMm = 10 }, "A long, low crate with a stencilled label, latches and handles.");
        b.Add("Storage racks", ItemKind.Bookshelf, i => i with { Books = false }, "Plain racks.");
        b.Add("Tech cabinet", ItemKind.Cabinet, i => i with { Front = CabinetFront.Tech, Crown = Crown.Flat, WidthMm = 20, DepthMm = 10, HeightMm = 30 }, "A cabinet of machines.");
        b.Add("Reactor core", ItemKind.Candlestand, i => i with { Candle = CandleStyle.Reactor, WidthMm = 11, DepthMm = 11, HeightMm = 24 }, "A glowing reactor column held in rings between four struts, on a stepped base under a stepped cap.");

        b.Room = Armoury;
        b.Add("Rifle rack", ItemKind.WeaponRack, i => i with { Rows = 4, HeightMm = 26 }, "Rifles standing in a rack.");
        b.Add("Weapon lockers", ItemKind.Cabinet, i => i with { Front = CabinetFront.Locker, Crown = Crown.Flat, Columns = 3, WidthMm = 24, DepthMm = 10, HeightMm = 34 }, "Three lockers.");
        b.Add("Barrier", ItemKind.Screen, i => i with { ScreenStyle = ScreenStyle.Barrier, Columns = 1, WidthMm = 24, DepthMm = 7, HeightMm = 14 }, "A barricade with chevrons and a grille.");
        b.Add("Armour stand", ItemKind.Stand, i => i with { Stand = StandStyle.Armour }, "Armour on a stand.");

        b.Room = Throne;
        b.Add("Captain's chair", ItemKind.Armchair, i => i with { Seat = SeatStyle.Command, BackTop = BackTop.Square, WidthMm = 16, DepthMm = 14, HeightMm = 24 }, "A command chair with angled arms.");
        b.Add("Helm console", ItemKind.SlantDesk, i => i with { Slant = SlantStyle.Console, WidthMm = 24, DepthMm = 14, HeightMm = 16 }, "A console with screens and buttons.");
        b.Add("Star map table", ItemKind.SlantDesk, i => i with { Slant = SlantStyle.MapTable, WidthMm = 24, DepthMm = 16, HeightMm = 14 }, "A sloped table with a map.");
        b.Add("Main screen", ItemKind.WallHanging, i => i with { Hanging = HangingStyle.Monitor, WidthMm = 26, HeightMm = 16, HangMm = 16 }, "A big screen.");

        b.Room = Prison;
        b.Add("Cell bunk", ItemKind.Bed, i => i with { FootMm = 13, Bedding = false }, "A bare bunk.");
        b.Add("Cell barrier", ItemKind.Screen, i => i with { ScreenStyle = ScreenStyle.Barrier, Columns = 1, WidthMm = 24, DepthMm = 7, HeightMm = 14 }, "A barrier.");

        b.Room = Lab;
        b.Add("Sleeper pod", ItemKind.Coffin, i => i with { Coffin = CoffinStyle.Pod, WidthMm = 36, DepthMm = 14, HeightMm = 8 }, "A sleeper pod with a window.");
        b.Add("Sample cabinet", ItemKind.Cabinet, i => i with { Front = CabinetFront.Glass, Contents = ShelfContents.Jars, Crown = Crown.Flat, WidthMm = 18, DepthMm = 9, HeightMm = 30 }, "Glass doors over jars.");
        b.Add("Lab console", ItemKind.SlantDesk, i => i with { Slant = SlantStyle.Console, WidthMm = 20, DepthMm = 14, HeightMm = 16 }, "A console.");
        b.Add("Medical bed", ItemKind.Bed, i => i with { FootMm = 13 }, "A bed.");
        TownProps(b, [("Planter tree", TreeStyle.Oak, "A colony tree grown in a chamfered planter, its crown etched and cut through.")],
            "A water trough with a feed pipe along one end and a pump head.",
            "A broadcast stage: plated deck, steps up the front and a star-map backdrop.",
            "A street vendor's stall under a panelled canopy, crates of goods on the counter.",
            "A street bar under a plated canopy with a hanging sign.",
            "A bulletin board on legs under a little canopy.");
        TownWalls(b, "Defence wall", "armoured defence wall with gun ports, a hazard-striped band and a skirt of plate");
        return b.Entries;
    }

    static List<CatalogEntry> Desert()
    {
        var b = new Book(TerrainThemes.Desert, "des");
        b.Room = Bedroom;
        b.Add("Low bed", ItemKind.Bed, i => i with { HeightMm = 10, FootMm = 7 }, "A low bed.");
        b.Add("Lattice cupboard", ItemKind.Cabinet, i => i with { Front = CabinetFront.Lattice, Crown = Crown.Arch, WidthMm = 20, DepthMm = 10, HeightMm = 32 }, "A cupboard with lattice doors under an arch.");
        b.Add("Kilim", ItemKind.Rug, i => i with { Rug = RugPattern.Kilim, WidthMm = 32, DepthMm = 20 }, "A woven kilim.");
        b.Add("Round rug", ItemKind.Rug, i => i with { Rug = RugPattern.Round, WidthMm = 26, DepthMm = 26 }, "A round rug with a rosette.");
        b.Add("Lattice screen", ItemKind.Screen, i => i with { ScreenStyle = ScreenStyle.Lattice, WidthMm = 30, HeightMm = 30 }, "A three-leaf lattice screen.");
        b.Add("Chest", ItemKind.Chest, i => i with { Lid = LidStyle.Domed }, "A banded chest with a domed lid.");

        b.Room = Kitchen;
        b.Add("Clay oven", ItemKind.Fireplace, i => i with { Hearth = HearthStyle.Oven, WidthMm = 24, DepthMm = 16, HeightMm = 22 }, "A domed oven.");
        b.Add("Low table", ItemKind.Table, i => i with { Legs = LegStyle.Round, HeightMm = 9 }, "A round low table.");
        b.Add("Jar shelves", ItemKind.Cabinet, i => i with { Front = CabinetFront.Open, Contents = ShelfContents.Jars, Crown = Crown.Arch, WidthMm = 20, DepthMm = 8, HeightMm = 28, Shelves = 2 }, "Shelves of jars under an arch.");
        b.Add("Stool", ItemKind.Stool, i => i, "A round stool.");

        b.Room = Hall;
        b.Add("Divan", ItemKind.Armchair, i => i with { Seat = SeatStyle.Sofa, Columns = 2, WidthMm = 26, HeightMm = 17, SeatMm = 6.5, ArmMm = 10, BackTop = BackTop.Rounded }, "A low two-seat divan.");
        b.Add("Hanging lamp stand", ItemKind.Candlestand, i => i with { WidthMm = 10, HeightMm = 22 }, "A candle stand.");
        b.Add("Tapestry", ItemKind.WallHanging, i => i with { Hanging = HangingStyle.Tapestry, WidthMm = 18, HeightMm = 24 }, "A woven hanging.");

        b.Room = Study;
        b.Add("Scroll cabinet", ItemKind.Cabinet, i => i with { Front = CabinetFront.Open, Contents = ShelfContents.Scrolls, Crown = Crown.Arch, WidthMm = 22, DepthMm = 8, HeightMm = 32, Shelves = 2 }, "Arched shelves of scrolls.");
        b.Add("Map table", ItemKind.SlantDesk, i => i with { Slant = SlantStyle.MapTable, WidthMm = 24, DepthMm = 16, HeightMm = 14 }, "A map table.");
        b.Add("Writing desk", ItemKind.Desk, i => i, "A desk with papers.");

        b.Room = Storage;
        b.Add("Crate", ItemKind.Crate, i => i with { CrateStyle = CrateStyle.Carved, WidthMm = 14 }, "A framed crate carved with a lattice of stars.");
        b.Add("Spice crate", ItemKind.Crate, i => i with { CrateStyle = CrateStyle.Carved, Lid = LidStyle.None, Load = CrateLoad.Sacks, WidthMm = 14, DepthMm = 13, HeightMm = 8 }, "An open carved crate heaped with tied sacks of spice.");
        b.Add("Slatted crate", ItemKind.Crate, i => i with { CrateStyle = CrateStyle.Slatted }, "A crate of slats with gaps between them.");
        b.Add("Wall shelf", ItemKind.WallShelf, i => i, "A shelf on two brackets.");

        b.Room = Chapel;
        b.Add("Shrine", ItemKind.Cabinet, i => i with { Front = CabinetFront.Altar, Crown = Crown.Arch, WidthMm = 24, DepthMm = 12, HeightMm = 22 }, "An altar under an arch.");

        b.Room = Armoury;
        b.Add("Weapon rack", ItemKind.WeaponRack, i => i, "Spears and swords in a rack.");

        b.Room = Throne;
        b.Add("Sultan's seat", ItemKind.Armchair, i => i with { Seat = SeatStyle.Throne, BackTop = BackTop.Pointed, WidthMm = 16, DepthMm = 14, HeightMm = 28, SeatMm = 10, ArmMm = 14 }, "A throne on a dais.");
        b.Add("Great rug", ItemKind.Rug, i => i with { Rug = RugPattern.Kilim, WidthMm = 44, DepthMm = 28 }, "A large kilim.");

        b.Room = Prison;
        b.Add("Sarcophagus", ItemKind.Coffin, i => i with { Coffin = CoffinStyle.Sarcophagus, WidthMm = 36, DepthMm = 14, HeightMm = 10, Detail = WoodDetail.Stone }, "A tomb with an effigy.");
        TownProps(b, [("Palm tree", TreeStyle.Palm, "A date palm, its ringed trunk leaning a little, fronds spreading from the crown.")],
            "A sandstone trough for camels and horses, water inside and a pump at one end.",
            "A stage for dancers and storytellers in the bazaar, curtains tied back.",
            "A bazaar stall under a striped awning, crates of goods on the counter.",
            "A street drinks bar under an awning with a hanging sign.",
            "A notice board for the caravan town under a little roof.");
        TownWalls(b, "Sandstone wall", "sandstone wall with stepped merlons, keyhole loops and beam ends under the walk");
        return b.Entries;
    }

    static List<CatalogEntry> Gothic()
    {
        var b = new Book(TerrainThemes.Gothic, "got");
        b.Room = Bedroom;
        b.Add("Four-poster bed", ItemKind.Bed, i => i with { BedStyle = BedStyle.FourPoster, WidthMm = 34, DepthMm = 20, HeightMm = 36, FootMm = 10 }, "A canopied bed.");
        b.Add("Wardrobe", ItemKind.Cabinet, i => i with { Front = CabinetFront.Panelled, Crown = Crown.Pediment, WidthMm = 22, DepthMm = 11, HeightMm = 38 }, "Panelled doors under a pediment.");
        b.Add("Dresser", ItemKind.Cabinet, i => i with { Front = CabinetFront.DrawersOverDoors, Crown = Crown.Cornice, WidthMm = 22, DepthMm = 10, HeightMm = 20 }, "Drawers over doors.");
        b.Add("Damask rug", ItemKind.Rug, i => i with { Rug = RugPattern.Damask, WidthMm = 32, DepthMm = 22 }, "A rug with a damask pattern.");
        b.Add("Portrait", ItemKind.WallHanging, i => i with { Hanging = HangingStyle.Painting, WidthMm = 16, HeightMm = 20, HangMm = 14 }, "A framed painting.");
        b.Add("Candelabra", ItemKind.Candlestand, i => i with { WidthMm = 14, HeightMm = 26 }, "A five-branch candelabra.");
        b.Add("Tracery screen", ItemKind.Screen, i => i with { ScreenStyle = ScreenStyle.Tracery, WidthMm = 30, HeightMm = 30 }, "A screen of pointed arches.");

        b.Room = Kitchen;
        b.Add("Kitchen dresser", ItemKind.Cabinet, i => i with { Front = CabinetFront.Hutch, Contents = ShelfContents.Plates, Crown = Crown.Cornice, WidthMm = 24, DepthMm = 12, HeightMm = 38 }, "A cupboard with plates above.");
        b.Add("Range fireplace", ItemKind.Fireplace, i => i with { Hearth = HearthStyle.Fireplace, WidthMm = 26, DepthMm = 11, HeightMm = 40 }, "A fireplace with a mantel.");

        b.Room = Hall;
        b.Add("Grand fireplace", ItemKind.Fireplace, i => i with { Hearth = HearthStyle.Fireplace, WidthMm = 30, DepthMm = 11, HeightMm = 44 }, "A tall fireplace with a crest.");
        b.Add("Dining table", ItemKind.Table, i => i with { Legs = LegStyle.Slab, WidthMm = 40 }, "A long table on solid ends.");
        b.Add("High-backed chair", ItemKind.Chair, i => i with { Back = BackStyle.Ladder, HeightMm = 22 }, "A tall ladder-back chair.");
        b.Add("Wing chair", ItemKind.Armchair, i => i with { BackTop = BackTop.Winged, HeightMm = 22 }, "A wing chair.");
        b.Add("Sofa", ItemKind.Armchair, i => i with { Seat = SeatStyle.Sofa, Columns = 2, WidthMm = 24, BackTop = BackTop.Arched }, "A two-seat sofa.");
        b.Add("Grandfather clock", ItemKind.Cabinet, i => i with { Front = CabinetFront.Clock, Crown = Crown.Arch, Columns = 1, WidthMm = 12, DepthMm = 9, HeightMm = 42 }, "A long-case clock with a pointed hood and pinnacles, a real pendulum hanging behind the glazed trunk door.");
        b.Add("Hall portrait", ItemKind.WallHanging, i => i with { Hanging = HangingStyle.Painting, WidthMm = 22, HeightMm = 26, HangMm = 14 }, "A large framed painting.");

        b.Room = Study;
        b.Add("Bookcase", ItemKind.Cabinet, i => i with { Front = CabinetFront.Open, Contents = ShelfContents.Books, Crown = Crown.Pediment, WidthMm = 22, DepthMm = 8, HeightMm = 38, Shelves = 3 }, "Open shelves of books under a pediment.");
        b.Add("Glass bookcase", ItemKind.Cabinet, i => i with { Front = CabinetFront.Glass, Contents = ShelfContents.Books, Crown = Crown.Cornice, WidthMm = 20, DepthMm = 9, HeightMm = 34 }, "Books behind glass doors.");
        b.Add("Scriptorium desk", ItemKind.SlantDesk, i => i with { Slant = SlantStyle.Scriptorium, WidthMm = 14, DepthMm = 12, HeightMm = 20 }, "A sloped writing desk with a shelf.");
        b.Add("Writing desk", ItemKind.Desk, i => i, "A desk with papers and an inkwell.");

        b.Room = Storage;
        b.Add("Chest", ItemKind.Chest, i => i with { Lid = LidStyle.Domed }, "A banded chest with a domed lid.");
        b.Add("Crate", ItemKind.Crate, i => i with { CrateStyle = CrateStyle.Arched, WidthMm = 14 }, "A framed crate with an arcade of pointed arches, a lily in the middle and iron corners.");
        b.Add("Cross-braced crate", ItemKind.Crate, i => i with { CrateStyle = CrateStyle.CrossBraced }, "A crate with an X brace on every side.");

        b.Room = Chapel;
        b.Add("Altar", ItemKind.Cabinet, i => i with { Front = CabinetFront.Altar, Crown = Crown.Flat, WidthMm = 26, DepthMm = 12, HeightMm = 18 }, "A tall altar with a cloth hanging down its front, a budded cross and two candlesticks standing on it.");
        b.Add("Pew", ItemKind.Armchair, i => i with { Seat = SeatStyle.Pew, Columns = 3, WidthMm = 30, DepthMm = 10, HeightMm = 17, SeatMm = 7.5 }, "A pew with tall ends.");
        b.Add("Lectern", ItemKind.SlantDesk, i => i with { Slant = SlantStyle.Lectern, WidthMm = 10 }, "A book stand.");
        b.Add("Candelabra", ItemKind.Candlestand, i => i with { WidthMm = 14, HeightMm = 26 }, "A five-branch candelabra.");
        b.Add("Rood screen", ItemKind.Screen, i => i with { ScreenStyle = ScreenStyle.Tracery, WidthMm = 36, HeightMm = 32, Columns = 4 }, "A screen of pointed arches.");

        b.Room = Throne;
        b.Add("Throne", ItemKind.Armchair, i => i with { Seat = SeatStyle.Throne, BackTop = BackTop.Pointed, WidthMm = 16, DepthMm = 14, HeightMm = 32, SeatMm = 10, ArmMm = 14 }, "A pointed throne on a dais.");
        b.Add("Long carpet", ItemKind.Rug, i => i with { Rug = RugPattern.Damask, WidthMm = 60, DepthMm = 18 }, "A damask runner.");

        b.Room = Prison;
        b.Add("Coffin", ItemKind.Coffin, i => i, "A coffin.");
        b.Add("Sarcophagus", ItemKind.Coffin, i => i with { Coffin = CoffinStyle.Sarcophagus, WidthMm = 36, DepthMm = 14, HeightMm = 10, Detail = WoodDetail.Stone }, "A tomb with an effigy.");
        b.Add("Crypt candles", ItemKind.Candlestand, i => i with { WidthMm = 10, HeightMm = 22 }, "A candelabra.");

        b.Room = Lab;
        b.Add("Apothecary drawers", ItemKind.Cabinet, i => i with { Front = CabinetFront.Drawers, Crown = Crown.Cornice, Rows = 5, Columns = 4, WidthMm = 22, DepthMm = 9, HeightMm = 30 }, "A wall of small drawers.");
        b.Add("Potion cabinet", ItemKind.Cabinet, i => i with { Front = CabinetFront.Glass, Contents = ShelfContents.Bottles, Crown = Crown.Cornice, WidthMm = 18, DepthMm = 9, HeightMm = 30 }, "Bottles behind glass.");
        b.Add("Laboratory table", ItemKind.Table, i => i with { Legs = LegStyle.Legs, Stretcher = true, WidthMm = 30, DepthMm = 14 }, "A table with stretchers.");
        TownProps(b, [("Dead tree", TreeStyle.Dead, "A blasted churchyard tree, bare twisted branches against the moon."),
                      ("Pine tree", TreeStyle.Pine, "A dark pine in stacked tiers on a straight trunk.")],
            "A carved stone trough on a plinth, a pump at one end.",
            "A theatre stage with a painted backdrop, a crest over the arch and heavy curtains tied back.",
            "A market stall under a striped awning, crates on the counter.",
            "An outdoor bar with barrels along the front and a hanging sign.",
            "A parish notice board under its little roof.");
        TownWalls(b, "City wall", "cut-stone city wall with gabled merlons, pointed loops and a corbel table of little arches");
        return b.Entries;
    }
}
