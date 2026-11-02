using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace PhotonCanvas.Addon.GameKit;

public enum AccessoryKind { DiceTower, CardStand, DeckTray, Initiative, MoveGauge, Ruler, Template }

/// <summary>What the marks on gauges, rulers and templates count in.</summary>
public enum Units { Inches, Centimetres, GridFeet }

public enum TemplateShape { Circle, Cone, Square }

public enum TileTop { Rounded, Arched, Square }

/// <summary>
/// What's etched on the faces that show (dice tower walls, the deck tray's sides, the card stand's
/// front rail, the top of the initiative tracker's base): wood like the furniture maker's, or one of
/// the wall maker's textures.
/// </summary>
public enum Finish { None, WoodGrain, Planks, Brick, StoneBlocks, Fieldstone, Tiles, MetalPlates, Hexagons, DungeonBlocks, CaveRock, SciFiPanels, Sandstone, Adobe }

/// <summary>
/// One accessory and how many to cut. Sizes are in millimetres; which settings count depends on
/// <see cref="Kind"/>. Saved with the pieces it makes.
/// </summary>
public sealed record AccessoryItem
{
    public AccessoryKind Kind { get; init; } = AccessoryKind.DiceTower;
    public int Count { get; init; } = 1;

    /// <summary>Left to right (mm): a tower's width, a card stand's length.</summary>
    public double WidthMm { get; init; } = 70;
    /// <summary>Front to back (mm): a tower's depth (without its tray), a card stand's depth.</summary>
    public double DepthMm { get; init; } = 70;
    /// <summary>Height (mm): a tower's, a card stand's back row, a deck tray's inside depth (the stack of cards).</summary>
    public double HeightMm { get; init; } = 150;

    // ---- dice tower ----
    /// <summary>Ramps inside the tower, the bottom one sending the dice out of the gate.</summary>
    public int Baffles { get; init; } = 3;
    /// <summary>How far the tray reaches out in front of the tower (mm).</summary>
    public double TrayMm { get; init; } = 60;
    /// <summary>How high the tray's sides are (mm).</summary>
    public double RimMm { get; init; } = 18;
    /// <summary>The biggest die it has to pass (mm): sets the gaps between ramps and the gate.</summary>
    public double DieMm { get; init; } = 20;
    public bool Battlements { get; init; } = true;

    // ---- card stand ----
    /// <summary>Rows of slots, one behind and above the other.</summary>
    public int Tiers { get; init; } = 2;
    /// <summary>How wide the card slots are (mm): a thick hand wants more.</summary>
    public double SlotMm { get; init; } = 2.5;
    /// <summary>Upright ribs the slots are cut in (2 or more).</summary>
    public int Ribs { get; init; } = 3;

    // ---- deck tray ----
    public double CardWMm { get; init; } = 63;
    public double CardHMm { get; init; } = 88;
    /// <summary>Piles side by side (draw, discard…).</summary>
    public int Compartments { get; init; } = 2;

    // ---- initiative tracker ----
    public int Tiles { get; init; } = 8;
    public double TileWMm { get; init; } = 22;
    public double TileHMm { get; init; } = 32;
    public TileTop Top { get; init; } = TileTop.Rounded;
    /// <summary>One name per line (or comma); tiles without a name get a number.</summary>
    public string Names { get; init; } = "";

    // ---- gauges, rulers, templates ----
    public Units Units { get; init; } = Units.Inches;
    /// <summary>Movement gauge: how many segments.</summary>
    public int Segments { get; init; } = 6;
    /// <summary>Movement gauge: each segment's length, in <see cref="Units"/>.</summary>
    public double StepUnits { get; init; } = 1;
    /// <summary>Ruler: its length; template: its radius (circle), length (cone) or side (square), in <see cref="Units"/>.</summary>
    public double LengthUnits { get; init; } = 6;
    public TemplateShape Shape { get; init; } = TemplateShape.Circle;
}

/// <summary>Everything cut from one sheet in one go: the accessories, the sheet and the finish.</summary>
public sealed record AccessorySet
{
    /// <summary>The look picked to start from (<see cref="GameKitThemes"/>): it set the finish and the towers' tops, which can be changed afterwards.</summary>
    public string Theme { get; init; } = "Medieval";
    public List<AccessoryItem> Items { get; init; } = [Defaults.For(AccessoryKind.DiceTower)];
    /// <summary>The sheet's thickness (mm): drives every slot and tab.</summary>
    public double ThicknessMm { get; init; } = 3;
    /// <summary>Gap added to slots so tabs go in (mm), on top of the kerf the cut layer allows for.</summary>
    public double FitMm { get; init; } = 0.1;
    public Finish Finish { get; init; } = Finish.StoneBlocks;
    /// <summary>The finish's sizes are the wall maker's (for 28 mm walls) times this.</summary>
    public double TextureScale { get; init; } = 2;
    /// <summary>Stones, planks and grain are laid out at random: each number gives a different layout.</summary>
    public int Seed { get; init; } = 1;
    /// <summary>The font for numbers and names.</summary>
    public string Font { get; init; } = "Arial";

    public string ToJson() => JsonSerializer.Serialize(this, GameKitJson.Options);

    public static AccessorySet? FromJson(string json)
    {
        try { return JsonSerializer.Deserialize<AccessorySet>(json, GameKitJson.Options); }
        catch (Exception e) when (e is JsonException or NotSupportedException or ArgumentException) { return null; }
    }
}

public static class Defaults
{
    public sealed record KindInfo(AccessoryKind Kind, string Name, string About);

    public static readonly IReadOnlyList<KindInfo> Kinds =
    [
        new(AccessoryKind.DiceTower, "Dice tower", "A tower with ramps inside that tumble the dice, a gate at the bottom and a tray in front for them to land in."),
        new(AccessoryKind.CardStand, "Card stand", "Ribs with slanted slots on two rails: a hand of cards stands up in it, in one or more rows."),
        new(AccessoryKind.DeckTray, "Deck tray", "An open box for decks lying flat, with a finger notch in each pile to take cards."),
        new(AccessoryKind.Initiative, "Initiative tracker", "Named or numbered tiles that stand in a groove along a base, in turn order."),
        new(AccessoryKind.MoveGauge, "Movement gauge", "Segments joined by pins that bend round models and scenery, numbered by distance."),
        new(AccessoryKind.Ruler, "Range ruler", "A straight ruler marked in inches, centimetres or grid squares."),
        new(AccessoryKind.Template, "Area template", "A circle, cone or square for blasts and spells, with rings or lines at each step."),
    ];

    public static KindInfo Info(AccessoryKind k) => Kinds.First(x => x.Kind == k);

    public static AccessoryItem For(AccessoryKind k) => k switch
    {
        AccessoryKind.DiceTower => new() { Kind = k, WidthMm = 70, DepthMm = 70, HeightMm = 150, Baffles = 3, TrayMm = 60, RimMm = 18, DieMm = 20 },
        AccessoryKind.CardStand => new() { Kind = k, WidthMm = 180, DepthMm = 50, HeightMm = 34, Tiers = 2, SlotMm = 2.5, Ribs = 3 },
        AccessoryKind.DeckTray => new() { Kind = k, CardWMm = 63, CardHMm = 88, Compartments = 2, HeightMm = 25, WidthMm = 0, DepthMm = 0 },
        AccessoryKind.Initiative => new() { Kind = k, Tiles = 8, TileWMm = 22, TileHMm = 32, WidthMm = 0, DepthMm = 0, HeightMm = 0 },
        AccessoryKind.MoveGauge => new() { Kind = k, Units = Units.Inches, Segments = 6, StepUnits = 1, WidthMm = 12, DepthMm = 0, HeightMm = 0 },
        AccessoryKind.Ruler => new() { Kind = k, Units = Units.Inches, LengthUnits = 6, WidthMm = 20, DepthMm = 0, HeightMm = 0 },
        AccessoryKind.Template => new() { Kind = k, Units = Units.Inches, LengthUnits = 3, Shape = TemplateShape.Circle, WidthMm = 0, DepthMm = 0, HeightMm = 0 },
        _ => new() { Kind = k },
    };

    public static readonly IReadOnlyList<string> FinishNames = ["None", "Wood grain", "Planks", "Brick", "Stone blocks", "Fieldstone", "Tiles", "Metal plates", "Hexagons",
        "Dungeon blocks", "Cave rock", "Sci-fi panels", "Sandstone", "Adobe"];
    public static readonly IReadOnlyList<string> UnitNames = ["Inches", "Centimetres", "Grid squares (1 inch = 5 ft)"];

    /// <summary>One unit in mm.</summary>
    public static double UnitMm(Units u) => u == Units.Centimetres ? 10 : 25.4;

    /// <summary>The number written at <paramref name="units"/> along: inches and centimetres as they are, grid squares as feet (5 a square).</summary>
    public static string Mark(Units u, double units)
    {
        double v = u == Units.GridFeet ? units * 5 : units;
        return Math.Abs(v - Math.Round(v)) < 1e-6 ? ((int)Math.Round(v)).ToString(System.Globalization.CultureInfo.InvariantCulture) : v.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
    }
}

static class GameKitJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        Converters = { new JsonStringEnumConverter() },
    };
}

public enum Severity { Note, Warn, Bad }
public sealed record Finding(Severity Level, string Text);
