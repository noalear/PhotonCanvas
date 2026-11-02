using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace PhotonCanvas.Addon.Scatter;

public enum ItemKind { Barrel, Fence, Cart, MarketStall, Well, Gravestone }

public enum FenceStyle { Picket, PostAndRail, Planks, Wattle }
public enum StoneShape { Rounded, Square, Pointed, Cross, Celtic }
/// <summary>The wood detail etched and scored on the faces.</summary>
public enum WoodDetail { None, Grain, Planks }

/// <summary>
/// One piece of scenery and how many to cut. Sizes are the outside sizes in millimetres; which of the
/// settings count depends on <see cref="Kind"/> (a fence has no wheels). Saved with the pieces it makes.
/// </summary>
public sealed record ScatterItem
{
    public ItemKind Kind { get; init; } = ItemKind.Barrel;
    public int Count { get; init; } = 1;

    /// <summary>Left to right (mm): a barrel's widest, a fence's length, a cart's bed, a stall's front, a well's side, a stone's width.</summary>
    public double WidthMm { get; init; } = 10;
    /// <summary>Front to back (mm): a cart's bed, a stall, a well.</summary>
    public double DepthMm { get; init; } = 10;
    /// <summary>Height (mm): a barrel, a fence, a cart's sides, a stall's front posts, a well's wall, a stone.</summary>
    public double HeightMm { get; init; } = 12;

    /// <summary>Barrels: two raised hoops near the ends and one round the middle.</summary>
    public bool Hoops { get; init; } = true;
    /// <summary>Barrels: open at the top, water inside.</summary>
    public bool Open { get; init; }

    public FenceStyle Style { get; init; } = FenceStyle.Picket;
    /// <summary>Fences: a foot crossing each end post, so the section stands up on its own.</summary>
    public bool Feet { get; init; } = true;

    /// <summary>Carts: the wheels' diameter (mm).</summary>
    public double WheelMm { get; init; } = 14;
    /// <summary>Carts: shafts reaching forward to the ground; without them the cart is tipped onto a prop.</summary>
    public bool Shafts { get; init; } = true;

    /// <summary>Stalls: the counter's height (mm).</summary>
    public double CounterMm { get; init; } = 12;
    /// <summary>Stalls: how much higher the canopy is at the back than at the front (mm).</summary>
    public double RiseMm { get; init; } = 5;
    /// <summary>Stalls: stripes etched on the awning.</summary>
    public bool Stripes { get; init; } = true;
    /// <summary>Stalls: a scalloped edge along the front of the awning.</summary>
    public bool Scallops { get; init; } = true;

    /// <summary>Wells: the top of the roof (mm).</summary>
    public double TopMm { get; init; } = 34;
    /// <summary>Wells: a roof on two posts with a winch bar between them.</summary>
    public bool Roof { get; init; } = true;
    /// <summary>Wells: shingles etched on the roof.</summary>
    public bool Shingles { get; init; } = true;
    /// <summary>Wells: the stonework etched on the wall (one of <see cref="StoneFace.Presets"/>).</summary>
    public string Texture { get; init; } = "Fieldstone";

    public StoneShape Shape { get; init; } = StoneShape.Rounded;
    /// <summary>Gravestones: a grave slab in front of the stone, this long (mm); 0 = just a base.</summary>
    public double SlabMm { get; init; }
    /// <summary>Gravestones: "R.I.P." and lines of lettering scored on.</summary>
    public bool Inscription { get; init; } = true;
    /// <summary>Gravestones: chips out of the edge and cracks scored on.</summary>
    public bool Weathered { get; init; }
}

/// <summary>Everything cut from one sheet in one go: the pieces of scenery, the sheet and the detail.</summary>
public sealed record ScatterSet
{
    public List<ScatterItem> Items { get; init; } = [Defaults.For(ItemKind.Barrel)];
    /// <summary>The sheet's thickness (mm): drives every slot and tab, and a barrel's layers.</summary>
    public double ThicknessMm { get; init; } = 2;
    /// <summary>Gap added to slots so tabs go in (mm), on top of the kerf the cut layer allows for.</summary>
    public double FitMm { get; init; } = 0.1;
    public WoodDetail Detail { get; init; } = WoodDetail.Planks;
    /// <summary>
    /// Push fit (as the furniture maker's): small grip ribs on the walls of every slot, which the sheet
    /// squeezes flat as it goes in, so the scenery holds together without glue and comes apart again.
    /// </summary>
    public bool PushFit { get; init; }
    /// <summary>Push fit: how much tighter than the sheet the ribs make each joint (mm, both sides together).</summary>
    public double GripMm { get; init; } = 0.15;
    /// <summary>Grain, stones and chips are laid out at random: each number gives a different layout.</summary>
    public int Seed { get; init; } = 1;
    /// <summary>The figures it's for (mm): only used to scale the sizes when it's changed.</summary>
    public double ScaleMm { get; init; } = 28;

    public string ToJson() => JsonSerializer.Serialize(this, ScatterJson.Options);

    public static ScatterSet? FromJson(string json)
    {
        try { return JsonSerializer.Deserialize<ScatterSet>(json, ScatterJson.Options); }
        catch (Exception e) when (e is JsonException or NotSupportedException or ArgumentException) { return null; }
    }
}

/// <summary>Sizes that suit 28 mm figures (about 1:60), in a 2 mm sheet.</summary>
public static class Defaults
{
    public sealed record KindInfo(ItemKind Kind, string Name, string About);

    public static readonly IReadOnlyList<KindInfo> Kinds =
    [
        new(ItemKind.Barrel, "Barrel", "Discs stacked on a hidden core that lines them up, bulging in the middle, with raised hoops and staves etched on the lid."),
        new(ItemKind.Fence, "Fence", "A section of picket, post-and-rail, plank or wattle fence, standing on feet slotted across its end posts."),
        new(ItemKind.Cart, "Cart", "A finger-jointed cart bed on two beams, with an axle through two spoked wheels and shafts down to the ground."),
        new(ItemKind.MarketStall, "Market stall", "Two side frames, a counter and a sloping awning with stripes and a scalloped edge, all slotted together."),
        new(ItemKind.Well, "Well", "A square stone well with two posts, a winch bar and a shingled roof, its stonework etched like the wall maker's."),
        new(ItemKind.Gravestone, "Gravestone", "A headstone slotted into a base or a grave slab: rounded, square, pointed, a cross or a Celtic cross, with lettering."),
    ];

    public static KindInfo Info(ItemKind k) => Kinds.First(x => x.Kind == k);

    public static ScatterItem For(ItemKind k) => k switch
    {
        ItemKind.Barrel => new() { Kind = k, WidthMm = 10, HeightMm = 12 },
        ItemKind.Fence => new() { Kind = k, WidthMm = 40, HeightMm = 14, Style = FenceStyle.Picket },
        ItemKind.Cart => new() { Kind = k, WidthMm = 16, DepthMm = 22, HeightMm = 6, WheelMm = 14 },
        ItemKind.MarketStall => new() { Kind = k, WidthMm = 30, DepthMm = 16, HeightMm = 28, CounterMm = 12, RiseMm = 5 },
        ItemKind.Well => new() { Kind = k, WidthMm = 20, DepthMm = 20, HeightMm = 10, TopMm = 34 },
        ItemKind.Gravestone => new() { Kind = k, WidthMm = 9, HeightMm = 13, Shape = StoneShape.Rounded },
        _ => new() { Kind = k },
    };

    /// <summary>The same piece <paramref name="k"/> times the size (for figures of another size).</summary>
    public static ScatterItem Scaled(ScatterItem it, double k) => it with
    {
        WidthMm = Math.Round(it.WidthMm * k, 1), DepthMm = Math.Round(it.DepthMm * k, 1), HeightMm = Math.Round(it.HeightMm * k, 1),
        WheelMm = Math.Round(it.WheelMm * k, 1), CounterMm = Math.Round(it.CounterMm * k, 1), RiseMm = Math.Round(it.RiseMm * k, 1),
        TopMm = Math.Round(it.TopMm * k, 1), SlabMm = Math.Round(it.SlabMm * k, 1),
    };
}

static class ScatterJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        Converters = { new JsonStringEnumConverter() },
    };
}

public enum Severity { Note, Warn, Bad }
public sealed record Finding(Severity Level, string Text);
