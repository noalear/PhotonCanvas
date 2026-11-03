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

    /// <summary>The rooms, in the order to show them.</summary>
    public static IReadOnlyList<string> Rooms { get; } = [Bedroom, Kitchen, Hall, Study, Workshop, Storage, Chapel, Armoury, Throne, Prison, Lab];

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
        ItemKind.Cabinet or ItemKind.Bookshelf or ItemKind.Fireplace or ItemKind.Bed or ItemKind.WeaponRack or ItemKind.Desk => FurnitureSpot.Wall,
        ItemKind.Armchair when it.Seat is SeatStyle.Sofa or SeatStyle.Pew => FurnitureSpot.Free,
        ItemKind.SlantDesk when it.Slant == SlantStyle.Console => FurnitureSpot.Wall,
        _ => FurnitureSpot.Free,
    };

    // ---- the catalog ----

    sealed class Book(string theme, string key)
    {
        public readonly List<CatalogEntry> Entries = [];
        public string Room = Bedroom;

        public void Add(string name, ItemKind kind, Func<FurnitureItem, FurnitureItem> edit, string about)
        {
            var it = edit(Defaults.For(kind)) with { Theme = theme, Name = name };
            string id = key + "-" + Slug(name);
            if (Entries.Any(e => e.Id == id)) id += "-" + Slug(Room);
            Entries.Add(new(id, name, theme, Room, it, about));
        }

        static string Slug(string s)
        {
            s = s.Replace("'", "").ToLowerInvariant();
            return string.Join("-", s.Split(s.Where(c => !char.IsLetterOrDigit(c)).Distinct().ToArray(), StringSplitOptions.RemoveEmptyEntries));
        }
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
        b.Add("Plain bed", ItemKind.Bed, i => i, "A simple bed with head and foot boards and a blanket.");
        b.Add("Wardrobe", ItemKind.Cabinet, i => i with { Front = CabinetFront.Panelled, Crown = Crown.Cornice, WidthMm = 22, DepthMm = 11, HeightMm = 36 }, "Two panelled doors under a cornice.");
        b.Add("Chest of drawers", ItemKind.Cabinet, i => i with { Front = CabinetFront.Drawers, Crown = Crown.Flat, WidthMm = 22, DepthMm = 10, HeightMm = 18, Rows = 3, Columns = 2 }, "Three rows of drawers, waist high.");
        b.Add("Blanket chest", ItemKind.Chest, i => i, "A banded chest for the foot of the bed.");
        b.Add("Bedside candles", ItemKind.Candlestand, i => i with { WidthMm = 10, HeightMm = 22 }, "A three-branch candelabra.");
        b.Add("Bedroom rug", ItemKind.Rug, i => i with { Rug = RugPattern.Border, WidthMm = 30, DepthMm = 20 }, "A rug with a patterned border.");
        b.Add("Tapestry", ItemKind.WallHanging, i => i with { Hanging = HangingStyle.Tapestry, WidthMm = 18, HeightMm = 24 }, "A tapestry on a rail, with a shield worked into it.");
        b.Add("Dressing screen", ItemKind.Screen, i => i with { ScreenStyle = ScreenStyle.Panels, WidthMm = 28, HeightMm = 28 }, "A three-leaf folding screen.");

        b.Room = Kitchen;
        b.Add("Dresser with plates", ItemKind.Cabinet, i => i with { Front = CabinetFront.Hutch, Contents = ShelfContents.Plates, Crown = Crown.Cornice, WidthMm = 24, DepthMm = 12, HeightMm = 36 }, "A cupboard with open shelves of plates above.");
        b.Add("Bread oven", ItemKind.Fireplace, i => i with { Hearth = HearthStyle.Oven, WidthMm = 26, DepthMm = 16, HeightMm = 24 }, "A domed brick oven with a wood store under it.");
        b.Add("Kitchen table", ItemKind.Table, i => i with { Legs = LegStyle.Trestle, WidthMm = 34, DepthMm = 14 }, "A long scrubbed table on trestles.");
        b.Add("Larder cupboard", ItemKind.Cabinet, i => i with { Front = CabinetFront.Doors, Crown = Crown.Flat, WidthMm = 18, DepthMm = 10, HeightMm = 30, Columns = 2 }, "A tall cupboard for the stores.");
        b.Add("Spice shelf", ItemKind.WallShelf, i => i, "A shelf on two brackets.");
        b.Add("Stool", ItemKind.Stool, i => i, "A round stool.");

        b.Room = Hall;
        b.Add("Great fireplace", ItemKind.Fireplace, i => i with { Hearth = HearthStyle.Fireplace, WidthMm = 30, DepthMm = 11, HeightMm = 42 }, "A brick fireplace with a mantel and a chimney breast.");
        b.Add("Long table", ItemKind.Table, i => i with { Legs = LegStyle.Trestle, WidthMm = 44, DepthMm = 16 }, "A feasting table on trestles.");
        b.Add("Bench", ItemKind.Bench, i => i with { WidthMm = 36 }, "A plank bench.");
        b.Add("Bar counter", ItemKind.Cabinet, i => i with { Front = CabinetFront.Panelled, Crown = Crown.Flat, WidthMm = 40, DepthMm = 12, HeightMm = 16, Columns = 4 }, "A long panelled counter.");
        b.Add("Bottle shelves", ItemKind.Cabinet, i => i with { Front = CabinetFront.Open, Contents = ShelfContents.Bottles, Crown = Crown.Cornice, WidthMm = 24, DepthMm = 8, HeightMm = 32, Shelves = 3 }, "Open shelves of bottles behind the bar.");
        b.Add("Armchair", ItemKind.Armchair, i => i with { BackTop = BackTop.Rounded }, "A high-backed armchair with a tufted seat.");
        b.Add("Settle", ItemKind.Armchair, i => i with { Seat = SeatStyle.Sofa, Columns = 2, WidthMm = 24, BackTop = BackTop.Square }, "A two-seat settle for by the fire.");
        b.Add("Hall rug", ItemKind.Rug, i => i with { Rug = RugPattern.Kilim, WidthMm = 40, DepthMm = 24 }, "A woven rug with stepped diamonds.");
        b.Add("Banner", ItemKind.WallHanging, i => i with { Hanging = HangingStyle.Banner, WidthMm = 10, HeightMm = 26, HangMm = 14 }, "A swallow-tailed banner.");

        b.Room = Study;
        b.Add("Bookcase", ItemKind.Cabinet, i => i with { Front = CabinetFront.Open, Contents = ShelfContents.Books, Crown = Crown.Cornice, WidthMm = 22, DepthMm = 8, HeightMm = 34, Shelves = 3 }, "Open shelves of books under a cornice.");
        b.Add("Writing desk", ItemKind.Desk, i => i, "A desk with a drawer, papers, a book and an inkwell.");
        b.Add("Lectern", ItemKind.SlantDesk, i => i with { Slant = SlantStyle.Lectern, WidthMm = 10 }, "A book stand with an open book.");
        b.Add("Map table", ItemKind.SlantDesk, i => i with { Slant = SlantStyle.MapTable, WidthMm = 24, DepthMm = 16, HeightMm = 14 }, "A low sloped table with a map on it.");
        b.Add("Reading chair", ItemKind.Armchair, i => i with { BackTop = BackTop.Winged, HeightMm = 22 }, "A wing chair.");
        b.Add("Map", ItemKind.WallHanging, i => i with { Hanging = HangingStyle.Map, WidthMm = 20, HeightMm = 14, HangMm = 14 }, "A map for the wall.");

        b.Room = Workshop;
        b.Add("Smithy forge", ItemKind.Fireplace, i => i with { Hearth = HearthStyle.Forge, WidthMm = 26, DepthMm = 14, HeightMm = 34 }, "A forge with a fire bed, a hood and flames.");
        b.Add("Workbench", ItemKind.Table, i => i with { Legs = LegStyle.Slab, Stretcher = true, WidthMm = 32, DepthMm = 12, HeightMm = 14 }, "A heavy bench with solid ends.");
        b.Add("Tool cupboard", ItemKind.Cabinet, i => i with { Front = CabinetFront.Doors, Crown = Crown.Flat, WidthMm = 20, DepthMm = 9, HeightMm = 28 }, "A plain cupboard.");

        b.Room = Storage;
        b.Add("Crate", ItemKind.Crate, i => i, "A braced crate.");
        b.Add("Chest", ItemKind.Chest, i => i, "A chest with iron bands.");
        b.Add("Shelving", ItemKind.Bookshelf, i => i with { Books = false }, "Plain shelves.");

        b.Room = Chapel;
        b.Add("Altar", ItemKind.Cabinet, i => i with { Front = CabinetFront.Altar, Crown = Crown.Flat, WidthMm = 26, DepthMm = 12, HeightMm = 16 }, "An altar with a cloth over it.");
        b.Add("Pew", ItemKind.Armchair, i => i with { Seat = SeatStyle.Pew, Columns = 3, WidthMm = 30, DepthMm = 10, HeightMm = 16, SeatMm = 7.5 }, "A pew with tall ends.");
        b.Add("Altar candles", ItemKind.Candlestand, i => i with { WidthMm = 12, HeightMm = 26 }, "A five-branch candelabra.");

        b.Room = Armoury;
        b.Add("Weapon rack", ItemKind.WeaponRack, i => i, "Spears, a sword, an axe and a halberd in a rack.");
        b.Add("Armour stand", ItemKind.Stand, i => i with { Stand = StandStyle.Armour }, "A suit of armour on a stand.");
        b.Add("Training dummy", ItemKind.Stand, i => i with { Stand = StandStyle.Dummy }, "A dummy with a target on it.");
        b.Add("Shield", ItemKind.WallHanging, i => i with { Hanging = HangingStyle.Shield, WidthMm = 18, HeightMm = 18, HangMm = 14 }, "A shield over two crossed swords.");

        b.Room = Throne;
        b.Add("Throne", ItemKind.Armchair, i => i with { Seat = SeatStyle.Throne, BackTop = BackTop.Arched, WidthMm = 16, DepthMm = 14, HeightMm = 30, SeatMm = 10, ArmMm = 14 }, "A throne on a two-step dais.");
        b.Add("Long carpet", ItemKind.Rug, i => i with { Rug = RugPattern.Border, WidthMm = 60, DepthMm = 18 }, "A runner up to the throne.");
        b.Add("Royal tapestry", ItemKind.WallHanging, i => i with { Hanging = HangingStyle.Tapestry, WidthMm = 24, HeightMm = 30, HangMm = 12 }, "A large tapestry.");
        b.Add("Tall clock", ItemKind.Cabinet, i => i with { Front = CabinetFront.Clock, Crown = Crown.Pediment, Columns = 1, WidthMm = 10, DepthMm = 8, HeightMm = 40 }, "A long-case clock with a pendulum.");

        b.Room = Prison;
        b.Add("Stocks", ItemKind.Stocks, i => i, "Stocks for the village green or the gaol.");
        b.Add("Coffin", ItemKind.Coffin, i => i, "A plank coffin with a cross on the lid.");
        return b.Entries;
    }

    static List<CatalogEntry> Dungeon()
    {
        var b = new Book(TerrainThemes.Dungeon, "dun");
        b.Room = Bedroom;
        b.Add("Straw pallet", ItemKind.Bed, i => i with { BedStyle = BedStyle.Pallet, WidthMm = 30, DepthMm = 14, MattressMm = 5 }, "A mattress on two sleepers, with a fur on it.");
        b.Add("Guard's bunk", ItemKind.Bed, i => i with { BedStyle = BedStyle.Bunk, WidthMm = 32, DepthMm = 15, HeightMm = 30, MattressMm = 6, FootMm = 11 }, "Two bunks, one over the other.");
        b.Add("Footlocker", ItemKind.Chest, i => i with { WidthMm = 14, DepthMm = 9, HeightMm = 8 }, "A small banded chest.");
        b.Add("Fur rug", ItemKind.Rug, i => i with { Rug = RugPattern.Hide, WidthMm = 26, DepthMm = 18 }, "An animal hide.");

        b.Room = Kitchen;
        b.Add("Cook fire", ItemKind.Fireplace, i => i with { Hearth = HearthStyle.Forge, WidthMm = 24, DepthMm = 14, HeightMm = 30 }, "A stone hearth with a hood.");
        b.Add("Rough table", ItemKind.Table, i => i with { Legs = LegStyle.Trestle, WidthMm = 30, DepthMm = 14 }, "A table on trestles.");
        b.Add("Stool", ItemKind.Stool, i => i with { Top = TopShape.Square }, "A square stool.");

        b.Room = Hall;
        b.Add("Long table", ItemKind.Table, i => i with { Legs = LegStyle.Trestle, WidthMm = 40, DepthMm = 14 }, "A long trestle table.");
        b.Add("Bench", ItemKind.Bench, i => i with { WidthMm = 32 }, "A plank bench.");
        b.Add("Torch stand", ItemKind.Candlestand, i => i with { Candle = CandleStyle.Torch, WidthMm = 8, HeightMm = 24 }, "A burning torch on a stand.");
        b.Add("Banner", ItemKind.WallHanging, i => i with { Hanging = HangingStyle.Banner, WidthMm = 10, HeightMm = 24, HangMm = 14 }, "A war banner.");

        b.Room = Study;
        b.Add("Scroll shelves", ItemKind.Cabinet, i => i with { Front = CabinetFront.Open, Contents = ShelfContents.Scrolls, Crown = Crown.Flat, WidthMm = 20, DepthMm = 8, HeightMm = 30, Shelves = 3 }, "Shelves of rolled scrolls.");
        b.Add("Lectern", ItemKind.SlantDesk, i => i with { Slant = SlantStyle.Lectern, WidthMm = 10 }, "A book stand.");

        b.Room = Workshop;
        b.Add("Forge", ItemKind.Fireplace, i => i with { Hearth = HearthStyle.Forge, WidthMm = 26, DepthMm = 14, HeightMm = 34 }, "A forge with a hood and flames.");
        b.Add("Workbench", ItemKind.Table, i => i with { Legs = LegStyle.Slab, Stretcher = true, WidthMm = 30, DepthMm = 12, HeightMm = 14 }, "A heavy bench.");

        b.Room = Storage;
        b.Add("Crate", ItemKind.Crate, i => i, "A braced crate.");
        b.Add("Treasure chest", ItemKind.Chest, i => i with { WidthMm = 18, DepthMm = 11, HeightMm = 10 }, "A big banded chest.");
        b.Add("Rack of shelves", ItemKind.Bookshelf, i => i with { Books = false, Shelves = 3 }, "Plain shelves.");
        b.Add("Barred cupboard", ItemKind.Cabinet, i => i with { Front = CabinetFront.Lattice, Crown = Crown.Flat, WidthMm = 20, DepthMm = 10, HeightMm = 30 }, "A cupboard with a lattice front.");

        b.Room = Chapel;
        b.Add("Dark altar", ItemKind.Cabinet, i => i with { Front = CabinetFront.Altar, Crown = Crown.Flat, WidthMm = 26, DepthMm = 12, HeightMm = 16 }, "An altar with a cloth.");
        b.Add("Torch", ItemKind.Candlestand, i => i with { Candle = CandleStyle.Torch, WidthMm = 8, HeightMm = 26 }, "A torch on a stand.");

        b.Room = Armoury;
        b.Add("Weapon rack", ItemKind.WeaponRack, i => i, "Spears, a sword, an axe and a halberd in a rack.");
        b.Add("Armour stand", ItemKind.Stand, i => i with { Stand = StandStyle.Armour }, "A suit of armour on a stand.");
        b.Add("Training dummy", ItemKind.Stand, i => i with { Stand = StandStyle.Dummy }, "A dummy to hit.");
        b.Add("Shield", ItemKind.WallHanging, i => i with { Hanging = HangingStyle.Shield, WidthMm = 18, HeightMm = 18, HangMm = 14 }, "A shield over crossed swords.");
        b.Add("Weapons cupboard", ItemKind.Cabinet, i => i with { Front = CabinetFront.Doors, Crown = Crown.Flat, WidthMm = 22, DepthMm = 10, HeightMm = 32 }, "A tall plank cupboard.");

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
        b.Add("Hearth fire", ItemKind.Fireplace, i => i with { Hearth = HearthStyle.Forge, WidthMm = 24, DepthMm = 14, HeightMm = 28 }, "A stone fire with a smoke hood.");
        b.Add("Slab table", ItemKind.Table, i => i with { Legs = LegStyle.Slab, WidthMm = 26, DepthMm = 14, HeightMm = 11 }, "A table on stone slabs.");
        b.Add("Stump stool", ItemKind.Stool, i => i, "A round stool.");
        b.Add("Pot shelf", ItemKind.Cabinet, i => i with { Front = CabinetFront.Open, Contents = ShelfContents.Jars, Crown = Crown.Flat, WidthMm = 18, DepthMm = 8, HeightMm = 22, Shelves = 2 }, "Shelves of pots.");

        b.Room = Hall;
        b.Add("Long bench", ItemKind.Bench, i => i with { WidthMm = 30 }, "A plank bench.");
        b.Add("Torch", ItemKind.Candlestand, i => i with { Candle = CandleStyle.Torch, WidthMm = 8, HeightMm = 22 }, "A torch on a stand.");
        b.Add("Chief's seat", ItemKind.Armchair, i => i with { Seat = SeatStyle.Throne, BackTop = BackTop.Rounded, WidthMm = 16, DepthMm = 14, HeightMm = 26, SeatMm = 10, ArmMm = 14 }, "A big seat on a dais.");

        b.Room = Workshop;
        b.Add("Bloomery forge", ItemKind.Fireplace, i => i with { Hearth = HearthStyle.Forge, WidthMm = 24, DepthMm = 14, HeightMm = 32 }, "A forge with flames.");
        b.Add("Work slab", ItemKind.Table, i => i with { Legs = LegStyle.Slab, WidthMm = 28, DepthMm = 12, HeightMm = 12 }, "A low work table.");

        b.Room = Storage;
        b.Add("Crate", ItemKind.Crate, i => i, "A braced crate.");
        b.Add("Chest", ItemKind.Chest, i => i, "A banded chest.");

        b.Room = Armoury;
        b.Add("Spear rack", ItemKind.WeaponRack, i => i, "Spears and axes in a rack.");
        b.Add("Shield", ItemKind.WallHanging, i => i with { Hanging = HangingStyle.Shield, WidthMm = 16, HeightMm = 16, HangMm = 14 }, "A shield over crossed swords.");

        b.Room = Chapel;
        b.Add("Shrine", ItemKind.Cabinet, i => i with { Front = CabinetFront.Altar, Crown = Crown.Flat, WidthMm = 22, DepthMm = 11, HeightMm = 14 }, "A small altar.");

        b.Room = Prison;
        b.Add("Burial slab", ItemKind.Coffin, i => i with { Coffin = CoffinStyle.Sarcophagus, WidthMm = 34, DepthMm = 13, HeightMm = 8, Detail = WoodDetail.Stone }, "A stone tomb.");
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
        b.Add("Cargo crate", ItemKind.Crate, i => i, "A braced cargo crate.");
        b.Add("Storage racks", ItemKind.Bookshelf, i => i with { Books = false }, "Plain racks.");
        b.Add("Tech cabinet", ItemKind.Cabinet, i => i with { Front = CabinetFront.Tech, Crown = Crown.Flat, WidthMm = 20, DepthMm = 10, HeightMm = 30 }, "A cabinet of machines.");

        b.Room = Armoury;
        b.Add("Rifle rack", ItemKind.WeaponRack, i => i with { Rows = 4, HeightMm = 26 }, "Rifles standing in a rack.");
        b.Add("Weapon lockers", ItemKind.Cabinet, i => i with { Front = CabinetFront.Locker, Crown = Crown.Flat, Columns = 3, WidthMm = 24, DepthMm = 10, HeightMm = 34 }, "Three lockers.");
        b.Add("Barrier", ItemKind.Screen, i => i with { ScreenStyle = ScreenStyle.Barrier, Columns = 1, WidthMm = 24, HeightMm = 14 }, "A barricade with chevrons and a grille.");
        b.Add("Armour stand", ItemKind.Stand, i => i with { Stand = StandStyle.Armour }, "Armour on a stand.");

        b.Room = Throne;
        b.Add("Captain's chair", ItemKind.Armchair, i => i with { Seat = SeatStyle.Command, BackTop = BackTop.Square, WidthMm = 16, DepthMm = 14, HeightMm = 24 }, "A command chair with angled arms.");
        b.Add("Helm console", ItemKind.SlantDesk, i => i with { Slant = SlantStyle.Console, WidthMm = 24, DepthMm = 14, HeightMm = 16 }, "A console with screens and buttons.");
        b.Add("Star map table", ItemKind.SlantDesk, i => i with { Slant = SlantStyle.MapTable, WidthMm = 24, DepthMm = 16, HeightMm = 14 }, "A sloped table with a map.");
        b.Add("Main screen", ItemKind.WallHanging, i => i with { Hanging = HangingStyle.Monitor, WidthMm = 26, HeightMm = 16, HangMm = 16 }, "A big screen.");

        b.Room = Prison;
        b.Add("Cell bunk", ItemKind.Bed, i => i with { FootMm = 13, Bedding = false }, "A bare bunk.");
        b.Add("Cell barrier", ItemKind.Screen, i => i with { ScreenStyle = ScreenStyle.Barrier, Columns = 1, WidthMm = 24, HeightMm = 14 }, "A barrier.");

        b.Room = Lab;
        b.Add("Sleeper pod", ItemKind.Coffin, i => i with { Coffin = CoffinStyle.Pod, WidthMm = 36, DepthMm = 14, HeightMm = 8 }, "A sleeper pod with a window.");
        b.Add("Sample cabinet", ItemKind.Cabinet, i => i with { Front = CabinetFront.Glass, Contents = ShelfContents.Jars, Crown = Crown.Flat, WidthMm = 18, DepthMm = 9, HeightMm = 30 }, "Glass doors over jars.");
        b.Add("Lab console", ItemKind.SlantDesk, i => i with { Slant = SlantStyle.Console, WidthMm = 20, DepthMm = 14, HeightMm = 16 }, "A console.");
        b.Add("Medical bed", ItemKind.Bed, i => i with { FootMm = 13 }, "A bed.");
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
        b.Add("Chest", ItemKind.Chest, i => i, "A banded chest.");

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
        b.Add("Crate", ItemKind.Crate, i => i, "A braced crate.");
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
        b.Add("Candelabra", ItemKind.Candlestand, i => i with { WidthMm = 12, HeightMm = 26 }, "A five-branch candelabra.");
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
        b.Add("Grandfather clock", ItemKind.Cabinet, i => i with { Front = CabinetFront.Clock, Crown = Crown.Arch, Columns = 1, WidthMm = 10, DepthMm = 8, HeightMm = 40 }, "A long-case clock.");
        b.Add("Hall portrait", ItemKind.WallHanging, i => i with { Hanging = HangingStyle.Painting, WidthMm = 22, HeightMm = 26, HangMm = 14 }, "A large framed painting.");

        b.Room = Study;
        b.Add("Bookcase", ItemKind.Cabinet, i => i with { Front = CabinetFront.Open, Contents = ShelfContents.Books, Crown = Crown.Pediment, WidthMm = 22, DepthMm = 8, HeightMm = 38, Shelves = 3 }, "Open shelves of books under a pediment.");
        b.Add("Glass bookcase", ItemKind.Cabinet, i => i with { Front = CabinetFront.Glass, Contents = ShelfContents.Books, Crown = Crown.Cornice, WidthMm = 20, DepthMm = 9, HeightMm = 34 }, "Books behind glass doors.");
        b.Add("Scriptorium desk", ItemKind.SlantDesk, i => i with { Slant = SlantStyle.Scriptorium, WidthMm = 14, DepthMm = 12, HeightMm = 20 }, "A sloped writing desk with a shelf.");
        b.Add("Writing desk", ItemKind.Desk, i => i, "A desk with papers and an inkwell.");

        b.Room = Storage;
        b.Add("Chest", ItemKind.Chest, i => i, "A banded chest.");
        b.Add("Crate", ItemKind.Crate, i => i, "A braced crate.");

        b.Room = Chapel;
        b.Add("Altar", ItemKind.Cabinet, i => i with { Front = CabinetFront.Altar, Crown = Crown.Pediment, WidthMm = 26, DepthMm = 12, HeightMm = 24 }, "An altar with a cloth, under a pediment.");
        b.Add("Pew", ItemKind.Armchair, i => i with { Seat = SeatStyle.Pew, Columns = 3, WidthMm = 30, DepthMm = 10, HeightMm = 17, SeatMm = 7.5 }, "A pew with tall ends.");
        b.Add("Lectern", ItemKind.SlantDesk, i => i with { Slant = SlantStyle.Lectern, WidthMm = 10 }, "A book stand.");
        b.Add("Candelabra", ItemKind.Candlestand, i => i with { WidthMm = 12, HeightMm = 26 }, "A five-branch candelabra.");
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
        return b.Entries;
    }
}
