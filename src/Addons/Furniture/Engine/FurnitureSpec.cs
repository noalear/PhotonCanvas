using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace PhotonCanvas.Addon.Furniture;

/// <summary>What a piece of furniture is. New kinds go on the end, so saved lists keep their meaning.</summary>
public enum ItemKind
{
    Table, Bench, Stool, Chair, Bookshelf, Chest, Crate, Bed, WallDoor, WallShelf,
    Cabinet, Desk, Armchair, Fireplace, SlantDesk, WeaponRack, Stand, Coffin, Rug, WallHanging, Candlestand, Screen, Stocks,
    TownWall, Tower, Tree, Trough, Stage, Stall,
}

/// <summary>How a table's or bench's legs are made.</summary>
public enum LegStyle { Legs, Slab, Trestle, Round }
public enum TopShape { Square, Round }
public enum BackStyle { Panel, Slats, Ladder }
/// <summary>A chest's or crate's lid: lifts off (a rim under it), glued on, none, or (chests) a domed lid of planks that lifts off.</summary>
public enum LidStyle { Loose, Flat, None, Domed }
public enum DoorShape { Rectangle, Arched, Pointed }
public enum HingeSide { Left, Right }
/// <summary>The wood detail etched and scored on the faces.</summary>
/// <summary>What's etched on the faces: nothing, wood grain, planks and grain, metal panels with rivets (sci-fi), or stone (cracks and pits).</summary>
public enum WoodDetail { None, Grain, Planks, Metal, Stone }

/// <summary>A cabinet's front: what's on it (etched), or cut through it, and what can be seen behind it.</summary>
public enum CabinetFront
{
    /// <summary>One or more doors (wardrobe, cupboard).</summary>
    Doors,
    /// <summary>Rows of drawers (chest of drawers, apothecary's drawers).</summary>
    Drawers,
    /// <summary>A row of drawers over doors (sideboard, dresser, bedside table).</summary>
    DrawersOverDoors,
    /// <summary>Glazed doors: panes cut through, the shelves and what's on them seen behind.</summary>
    Glass,
    /// <summary>No front: shelves with things on them (pantry, bar shelves).</summary>
    Open,
    /// <summary>A cupboard below and open shelves above, set back (a dresser with plates).</summary>
    Hutch,
    /// <summary>Plain frame-and-panel (a counter, a bar).</summary>
    Panelled,
    /// <summary>Tall metal doors with vents, a name plate and a handle.</summary>
    Locker,
    /// <summary>Racks of machines: lights, vents, screens and tape reels.</summary>
    Tech,
    /// <summary>A tall case clock: a dial up top and the pendulum seen through a window.</summary>
    Clock,
    /// <summary>Doors with a lattice screen cut through them.</summary>
    Lattice,
    /// <summary>An altar's front: panels and a symbol in the middle, a cloth along the top.</summary>
    Altar,
}

/// <summary>What's on the top of a cabinet.</summary>
public enum Crown { Flat, Cornice, Pediment, Arch }

/// <summary>What stands on open shelves, seen from the front.</summary>
public enum ShelfContents { Books, Bottles, Jars, Plates, Scrolls, Tech, Empty, Tools, Stores }

/// <summary>What fills an open crate, heaped up over its rim.</summary>
public enum CrateLoad { None, Apples, Sacks, Rocks }

/// <summary>Seats with arms and a back.</summary>
public enum SeatStyle { Armchair, Throne, Sofa, Pew, Command, Wooden, Settle, Reading }

/// <summary>The top of a seat's back.</summary>
public enum BackTop { Square, Rounded, Arched, Pointed, Winged }

public enum BedStyle { Plain, FourPoster, Bunk, Pallet }

public enum HearthStyle { Fireplace, Oven, Forge }

/// <summary>Things with a sloping top on two shaped sides.</summary>
public enum SlantStyle { Lectern, Scriptorium, Console, MapTable }

public enum StandStyle { Armour, Dummy, Coats }

public enum CoffinStyle { Coffin, Sarcophagus, Pod }

public enum RugPattern { Border, Kilim, Damask, Hide, Mat, Round, Runner }

public enum HangingStyle { Tapestry, Banner, Painting, Shield, Map, Monitor, FramedMap }

public enum CandleStyle { Candelabra, Lamp, Torch }

public enum ScreenStyle { Lattice, Tracery, Panels, Barrier }

/// <summary>
/// One piece of furniture and how many to cut. Sizes are the outside sizes in millimetres; which of the
/// settings count depends on <see cref="Kind"/> (a chair has no shelves). Saved with the pieces it makes.
/// </summary>
public sealed record FurnitureItem
{
    public ItemKind Kind { get; init; } = ItemKind.Table;
    public int Count { get; init; } = 1;

    /// <summary>Left to right (mm): a table's length, a chair's seat width, a door's width.</summary>
    public double WidthMm { get; init; } = 30;
    /// <summary>Front to back (mm).</summary>
    public double DepthMm { get; init; } = 16;
    /// <summary>Overall height (mm): a table's top, a chest's body, a door's height.</summary>
    public double HeightMm { get; init; } = 13;

    /// <summary>How wide the legs are (mm); 0 = to suit the sheet.</summary>
    public double LegMm { get; init; }
    public LegStyle Legs { get; init; } = LegStyle.Legs;
    /// <summary>A rail low down between the legs (tables, benches, stools).</summary>
    public bool Stretcher { get; init; }
    /// <summary>A workbench's tool rack standing along the back of the top, and a vice on its front corner (tables, benches).</summary>
    public bool ToolRack { get; init; }
    public TopShape Top { get; init; } = TopShape.Round;

    /// <summary>Chairs: the seat's height (mm); the back reaches <see cref="HeightMm"/>.</summary>
    public double SeatMm { get; init; } = 7.5;
    public BackStyle Back { get; init; } = BackStyle.Slats;

    /// <summary>Bookshelves: shelves between the bottom one and the top.</summary>
    public int Shelves { get; init; } = 3;
    /// <summary>Bookshelves: a back panel (it keeps the shelf square).</summary>
    public bool BackPanel { get; init; } = true;
    /// <summary>Bookshelves: book spines etched on the back panel.</summary>
    public bool Books { get; init; } = true;

    public LidStyle Lid { get; init; } = LidStyle.Loose;
    /// <summary>Chests: iron bands and a lock plate etched on. Crates: boards etched on in <see cref="CrateStyle"/> (off: plain planks).</summary>
    public bool Bands { get; init; } = true;
    /// <summary>Crates: how the faces are boarded (a braced frame, slats, iron corners, stencilled cargo…).</summary>
    public CrateStyle CrateStyle { get; init; } = CrateStyle.Braced;
    /// <summary>Crates: the words stencilled on cargo and military crates; null or empty = some to suit.</summary>
    public string? Label { get; init; }
    /// <summary>Crates: rope handles on the ends (military crates always have them).</summary>
    public bool Handles { get; init; }
    /// <summary>Open crates: what's heaped in it, standing up over the rim.</summary>
    public CrateLoad Load { get; init; }

    /// <summary>Beds: the foot board's height (mm); the head board is <see cref="HeightMm"/>.</summary>
    public double FootMm { get; init; } = 8;
    /// <summary>Beds: the top of the mattress (mm).</summary>
    public double MattressMm { get; init; } = 6;
    /// <summary>Beds: a pillow and a turned-down blanket etched on the mattress.</summary>
    public bool Bedding { get; init; } = true;

    /// <summary>Wall pieces: the wall's thickness (mm), both panels if it's two-sided.</summary>
    public double WallMm { get; init; } = 3;
    /// <summary>Wall shelves: the sheet the wall was cut from (mm): its mount slots are that wide, plus the wall's fit.</summary>
    public double WallSheetMm { get; init; } = 3;
    public DoorShape Shape { get; init; } = DoorShape.Arched;
    public HingeSide Hinge { get; init; } = HingeSide.Left;
    /// <summary>Wall doors: how far the door covers the wall round the doorway (mm).</summary>
    public double OverlapMm { get; init; } = 1.5;
    /// <summary>Wall shelves: how tall the wall's mount slots are (mm).</summary>
    public double MountSlotMm { get; init; } = 6;

    /// <summary>A name for lists and part labels ("Wardrobe", "Captain's chair"); null = the kind's name.</summary>
    public string? Name { get; init; }
    /// <summary>
    /// The look this piece is made in (a <see cref="FurnitureThemes"/> name: door panels, handles, ornament);
    /// null = the set's. Lets a sci-fi console stand in a set that's otherwise medieval.
    /// </summary>
    public string? Theme { get; init; }
    /// <summary>What's etched on this piece's faces; null = the set's (a stone altar among wooden pews).</summary>
    public WoodDetail? Detail { get; init; }

    /// <summary>Cabinets: the front.</summary>
    public CabinetFront Front { get; init; } = CabinetFront.Doors;
    /// <summary>Cabinets: the top.</summary>
    public Crown Crown { get; init; } = Crown.Cornice;
    /// <summary>Cabinets with shelves that show: what's on them.</summary>
    public ShelfContents Contents { get; init; } = ShelfContents.Books;
    /// <summary>Cabinets: rows of drawers (or of machines). Weapon racks: weapons.</summary>
    public int Rows { get; init; } = 3;
    /// <summary>Cabinets: doors, or drawers in a row. Armchairs: seats (a sofa or pew).</summary>
    public int Columns { get; init; } = 2;

    /// <summary>Armchairs and thrones.</summary>
    public SeatStyle Seat { get; init; } = SeatStyle.Armchair;
    /// <summary>Chairs, armchairs and thrones: the top of the back.</summary>
    public BackTop BackTop { get; init; } = BackTop.Square;
    /// <summary>Armchairs: the arms' height (mm).</summary>
    public double ArmMm { get; init; } = 11;

    public BedStyle BedStyle { get; init; } = BedStyle.Plain;
    public HearthStyle Hearth { get; init; } = HearthStyle.Fireplace;
    public SlantStyle Slant { get; init; } = SlantStyle.Lectern;
    public StandStyle Stand { get; init; } = StandStyle.Armour;
    public CoffinStyle Coffin { get; init; } = CoffinStyle.Coffin;
    public RugPattern Rug { get; init; } = RugPattern.Border;
    public HangingStyle Hanging { get; init; } = HangingStyle.Tapestry;
    public CandleStyle Candle { get; init; } = CandleStyle.Candelabra;
    public ScreenStyle ScreenStyle { get; init; } = ScreenStyle.Lattice;
    /// <summary>Wall hangings: how high the bottom edge is off the floor (mm).</summary>
    public double HangMm { get; init; } = 10;
    /// <summary>Town walls: which piece (a straight run, a corner, a gatehouse, an end).</summary>
    public WallPiece Wall { get; init; } = WallPiece.Straight;
    /// <summary>Trees: an oak, a pine, a dead tree, a palm or a giant mushroom.</summary>
    public TreeStyle Tree { get; init; } = TreeStyle.Oak;
    /// <summary>Street stalls: a market stall, an outdoor bar or a notice board.</summary>
    public StallStyle Stall { get; init; } = StallStyle.Market;
}

/// <summary>Everything cut from one sheet in one go: the pieces of furniture, the sheet and the detail.</summary>
public sealed record FurnitureSet
{
    /// <summary>The look picked to start from (<see cref="FurnitureThemes"/>): it set the detail and the doors' shape, which can be changed afterwards.</summary>
    public string Theme { get; init; } = "Medieval";
    public List<FurnitureItem> Items { get; init; } = [Defaults.For(ItemKind.Table)];
    /// <summary>The sheet's thickness (mm): drives every slot and tab.</summary>
    public double ThicknessMm { get; init; } = 2;
    /// <summary>Gap added to slots so tabs go in (mm), on top of the kerf the cut layer allows for.</summary>
    public double FitMm { get; init; } = 0.1;
    public WoodDetail Detail { get; init; } = WoodDetail.Planks;
    /// <summary>
    /// Push fit: small grip ribs on the walls of every slot and finger joint, which the sheet squeezes
    /// flat as it goes in, so the furniture holds together without glue and comes apart again.
    /// </summary>
    public bool PushFit { get; init; }
    /// <summary>Push fit: how much tighter than the sheet the ribs make each joint (mm, both sides together).</summary>
    public double GripMm { get; init; } = 0.15;
    /// <summary>Grain and plank joints are laid out at random: each number gives a different layout.</summary>
    public int Seed { get; init; } = 1;
    /// <summary>The figures it's for (mm): only used to scale the sizes when it's changed.</summary>
    public double ScaleMm { get; init; } = 28;

    public string ToJson() => JsonSerializer.Serialize(this, FurnitureJson.Options);

    public static FurnitureSet? FromJson(string json)
    {
        try { return JsonSerializer.Deserialize<FurnitureSet>(json, FurnitureJson.Options); }
        catch (Exception e) when (e is JsonException or NotSupportedException or ArgumentException) { return null; }
    }
}

/// <summary>Sizes that suit 28 mm figures (about 1:60), in a 2 mm sheet.</summary>
public static class Defaults
{
    public sealed record KindInfo(ItemKind Kind, string Name, string About);

    public static readonly IReadOnlyList<KindInfo> Kinds =
    [
        new(ItemKind.Table, "Table", "A top on two leg frames that slot into it, with four legs, solid ends, trestles or (round) crossed legs."),
        new(ItemKind.Bench, "Bench", "A plank seat on two solid ends joined by a rail."),
        new(ItemKind.Stool, "Stool", "A round or square seat on two crossed legs."),
        new(ItemKind.Chair, "Chair", "A seat slotted between a back (with its back legs) and a front leg frame."),
        new(ItemKind.Bookshelf, "Bookshelf", "Two sides, shelves and a top, with a back panel and books etched on it."),
        new(ItemKind.Chest, "Chest", "A box with finger joints, iron bands and a lock plate, and a lid that lifts off."),
        new(ItemKind.Crate, "Crate", "A closed box with finger joints and braced planks: scatter for any table."),
        new(ItemKind.Bed, "Bed", "Head and foot boards, a mattress that slots between them and side rails, with bedding etched on."),
        new(ItemKind.WallDoor, "Door for a wall", "A door that hangs on two clips slid onto the side of a doorway from the wall maker, and swings open."),
        new(ItemKind.WallShelf, "Wall shelf", "A shelf on two brackets that plug into the wall maker's mount points."),
        new(ItemKind.Cabinet, "Cabinet", "Sides, shelves and a top slotted together, with doors, drawers, glass, a lattice or open shelves on the front: wardrobes, dressers, lockers, clocks and more."),
        new(ItemKind.Desk, "Desk", "A top on two solid ends joined by a back panel, with a drawer front under the top."),
        new(ItemKind.Armchair, "Armchair or throne", "A seat between two shaped arms and a back: armchairs, thrones, sofas, pews and command chairs."),
        new(ItemKind.Fireplace, "Fireplace or oven", "A surround with a mantel shelf for against a wall, a hearth on the floor and logs inside: fireplaces, bread ovens and forges."),
        new(ItemKind.SlantDesk, "Lectern or console", "A sloping top on two shaped sides: lecterns, writing desks, map tables and control consoles."),
        new(ItemKind.WeaponRack, "Weapon rack", "Spears, swords and axes standing in a notched rail on two feet."),
        new(ItemKind.Stand, "Armour stand", "Two profiles crossed on a base: armour on a stand, a training dummy or a coat stand."),
        new(ItemKind.Coffin, "Coffin or sarcophagus", "Layers glued into a solid shape, the lid carved: a coffin, a sarcophagus with its effigy, or a sleeper pod."),
        new(ItemKind.Rug, "Rug", "One flat piece with a pattern etched on it: a bordered rug, a kilim, damask, a hide, a deck mat or a round rug."),
        new(ItemKind.WallHanging, "Wall hanging", "A tapestry, banner, painting, shield, map or screen to glue on a wall."),
        new(ItemKind.Candlestand, "Candle stand or lamp", "Two profiles crossed on a round foot: a candelabra, a floor lamp or a torch stand."),
        new(ItemKind.Screen, "Screen", "A folding screen of hinged leaves standing in a zig-zag (or one panel on two feet): a lattice, tracery, plain panels or a barrier."),
        new(ItemKind.Stocks, "Stocks", "Two posts on feet and a board with holes for the head and hands."),
        new(ItemKind.TownWall, "Town wall", "A town wall a square or two long, a corner, a gatehouse or an end, with a wall walk, a crenellated parapet, arrow loops and moulded courses, lining up on the map's grid."),
        new(ItemKind.Tree, "Tree", "Two tree profiles halved together in a round base, bark and leaves etched and gaps cut through the canopy: an oak, a pine, a dead tree, a palm or a giant mushroom."),
        new(ItemKind.Trough, "Trough", "A horse trough on a plinth with water inside, a rim round the top and a pump at one end."),
        new(ItemKind.Stage, "Stage", "A players' stage: a planked deck with steps up the front, a painted backdrop behind a proscenium, curtains tied back."),
        new(ItemKind.Stall, "Stall or notice board", "A market stall under a striped awning, an outdoor bar with barrels and a hanging sign, or a notice board under a little roof."),
        new(ItemKind.Tower, "Guard tower", "A tall tower with a fighting roof behind merlons, a door, arrow loops on every side, quoins and a flag."),
    ];

    public static KindInfo Info(ItemKind k) => Kinds.First(x => x.Kind == k);

    public static FurnitureItem For(ItemKind k) => k switch
    {
        ItemKind.Table => new() { Kind = k, WidthMm = 30, DepthMm = 16, HeightMm = 13, Legs = LegStyle.Legs },
        ItemKind.Bench => new() { Kind = k, WidthMm = 26, DepthMm = 6, HeightMm = 7.5, Legs = LegStyle.Slab, Stretcher = true },
        ItemKind.Stool => new() { Kind = k, WidthMm = 8, DepthMm = 8, HeightMm = 7.5, Top = TopShape.Round },
        ItemKind.Chair => new() { Kind = k, WidthMm = 9, DepthMm = 9, HeightMm = 17, SeatMm = 7.5, Back = BackStyle.Slats },
        ItemKind.Bookshelf => new() { Kind = k, WidthMm = 20, DepthMm = 8, HeightMm = 30, Shelves = 3 },
        ItemKind.Chest => new() { Kind = k, WidthMm = 16, DepthMm = 10, HeightMm = 9, Lid = LidStyle.Loose },
        ItemKind.Crate => new() { Kind = k, WidthMm = 12, DepthMm = 12, HeightMm = 12, Lid = LidStyle.Flat },
        ItemKind.Bed => new() { Kind = k, WidthMm = 32, DepthMm = 16, HeightMm = 13, FootMm = 8, MattressMm = 6 },
        ItemKind.WallDoor => new() { Kind = k, WidthMm = 16, HeightMm = 28, DepthMm = 0, Shape = DoorShape.Arched, WallMm = 3 },
        ItemKind.WallShelf => new() { Kind = k, WidthMm = 20, DepthMm = 5, HeightMm = 6, WallMm = 3, MountSlotMm = 6 },
        ItemKind.Desk => new() { Kind = k, WidthMm = 24, DepthMm = 12, HeightMm = 13, Contents = ShelfContents.Books },
        ItemKind.Fireplace => new() { Kind = k, WidthMm = 26, DepthMm = 9, HeightMm = 40, Hearth = HearthStyle.Fireplace },
        ItemKind.SlantDesk => new() { Kind = k, WidthMm = 9, DepthMm = 8, HeightMm = 22, Slant = SlantStyle.Lectern },
        ItemKind.WeaponRack => new() { Kind = k, WidthMm = 18, DepthMm = 8, HeightMm = 30, Rows = 5 },
        ItemKind.Stand => new() { Kind = k, WidthMm = 14, DepthMm = 10, HeightMm = 32, Stand = StandStyle.Armour },
        ItemKind.Coffin => new() { Kind = k, WidthMm = 34, DepthMm = 11, HeightMm = 6, Coffin = CoffinStyle.Coffin },
        ItemKind.Rug => new() { Kind = k, WidthMm = 36, DepthMm = 24, HeightMm = 1, Rug = RugPattern.Border },
        ItemKind.WallHanging => new() { Kind = k, WidthMm = 16, DepthMm = 0, HeightMm = 22, HangMm = 10, Hanging = HangingStyle.Tapestry },
        ItemKind.Candlestand => new() { Kind = k, WidthMm = 10, DepthMm = 7, HeightMm = 24, Candle = CandleStyle.Candelabra },
        ItemKind.Screen => new() { Kind = k, WidthMm = 30, DepthMm = 12, HeightMm = 30, Columns = 3, ScreenStyle = ScreenStyle.Lattice },
        ItemKind.Stocks => new() { Kind = k, WidthMm = 22, DepthMm = 9, HeightMm = 28 },
        ItemKind.TownWall => new() { Kind = k, WidthMm = TownWalls.Square, DepthMm = 19, HeightMm = 56, Wall = WallPiece.Straight },
        ItemKind.Tower => new() { Kind = k, WidthMm = 46, DepthMm = 46, HeightMm = 92 },
        ItemKind.Tree => new() { Kind = k, WidthMm = 50, DepthMm = 50, HeightMm = 90, Tree = TreeStyle.Oak },
        ItemKind.Trough => new() { Kind = k, WidthMm = 32, DepthMm = 11, HeightMm = 9 },
        ItemKind.Stage => new() { Kind = k, WidthMm = 76.2, DepthMm = 50.8, HeightMm = 60 },
        ItemKind.Stall => new() { Kind = k, WidthMm = 36, DepthMm = 20, HeightMm = 40, Stall = StallStyle.Market },
        ItemKind.Armchair => new() { Kind = k, WidthMm = 14, DepthMm = 12, HeightMm = 20, SeatMm = 7.5, ArmMm = 11, Seat = SeatStyle.Armchair, BackTop = BackTop.Rounded, Columns = 1 },
        ItemKind.Cabinet => new() { Kind = k, WidthMm = 22, DepthMm = 11, HeightMm = 34, Front = CabinetFront.Doors, Crown = Crown.Cornice, Columns = 2, Shelves = 2 },
        _ => new() { Kind = k },
    };
}

static class FurnitureJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        Converters = { new JsonStringEnumConverter() },
    };
}

public enum Severity { Note, Warn, Bad }
public sealed record Finding(Severity Level, string Text);
