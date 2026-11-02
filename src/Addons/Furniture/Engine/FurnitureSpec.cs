using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace PhotonCanvas.Addon.Furniture;

public enum ItemKind { Table, Bench, Stool, Chair, Bookshelf, Chest, Crate, Bed, WallDoor, WallShelf }

/// <summary>How a table's or bench's legs are made.</summary>
public enum LegStyle { Legs, Slab, Trestle, Round }
public enum TopShape { Square, Round }
public enum BackStyle { Panel, Slats, Ladder }
public enum LidStyle { Loose, Flat, None }
public enum DoorShape { Rectangle, Arched, Pointed }
public enum HingeSide { Left, Right }
/// <summary>The wood detail etched and scored on the faces.</summary>
/// <summary>What's etched on the faces: nothing, wood grain, planks and grain, metal panels with rivets (sci-fi), or stone (cracks and pits).</summary>
public enum WoodDetail { None, Grain, Planks, Metal, Stone }

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
    /// <summary>Chests and crates: iron bands and a lock plate etched on.</summary>
    public bool Bands { get; init; } = true;

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
