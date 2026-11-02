using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace PhotonCanvas.Addon.Bridges;

public enum BridgeKind { Plank, Rope, Stone }
/// <summary>What the bridge's ends stand on: the table itself, or banks (hills, cliffs) as high as the ends of the deck.</summary>
public enum EndsOn { Table, Banks }
public enum ArchShape { Round, Segmental, Pointed }
/// <summary>A stone bridge's road: level from end to end, or ramps up from lower ends to a level top over the arches.</summary>
public enum RoadShape { Level, Humped }
public enum ParapetTop { Straight, Battlements, None }
public enum RoadSurface { Cobbles, Flagstones, Plain }

/// <summary>
/// Everything one bridge is made from. Sizes in millimetres; which settings count depends on
/// <see cref="Kind"/> (a rope bridge has no arches). Saved with the pieces it makes, so it can be opened and changed.
/// </summary>
public sealed record BridgeSpec
{
    public BridgeKind Kind { get; init; } = BridgeKind.Plank;
    public int Count { get; init; } = 1;

    /// <summary>End to end (mm).</summary>
    public double LengthMm { get; init; } = 100;
    /// <summary>The walkway or road between the sides (mm).</summary>
    public double WidthMm { get; init; } = 24;
    /// <summary>
    /// Plank and rope bridges standing on the table: how high the deck is at the ends (mm). Stone
    /// bridges: how high the road is over the arches.
    /// </summary>
    public double HeightMm { get; init; } = 10;
    /// <summary>Plank bridges: how much higher the deck is in the middle than at the ends (mm; 0 = level). Rope bridges: how far it sags.</summary>
    public double RiseMm { get; init; } = 8;
    public EndsOn Ends { get; init; } = EndsOn.Table;

    // ---- plank and rope bridges ----
    /// <summary>How wide each plank is (mm).</summary>
    public double PlankMm { get; init; } = 4;
    /// <summary>Gap between planks (mm).</summary>
    public double GapMm { get; init; } = 0.6;
    /// <summary>Plank bridges: how deep the side beams are under the deck (mm).</summary>
    public double BeamMm { get; init; } = 4;
    public bool Railing { get; init; } = true;
    /// <summary>The railing's height over the deck (mm).</summary>
    public double RailMm { get; init; } = 14;
    /// <summary>A railing post (or, on a rope bridge, a rope down to the deck) after every so many planks.</summary>
    public int PostEvery { get; init; } = 4;
    /// <summary>Plank bridges standing on the table: legs under the middle, besides the ones at the ends.</summary>
    public int Legs { get; init; }
    /// <summary>How wide the legs are (mm).</summary>
    public double LegMm { get; init; } = 4.5;
    /// <summary>Rope bridges: how thick the posts at the ends are (mm).</summary>
    public double PostMm { get; init; } = 3.5;
    /// <summary>Rope bridges: planks left out here and there.</summary>
    public int MissingPlanks { get; init; }
    /// <summary>Plank ends cut a little crooked and of different lengths.</summary>
    public bool Rough { get; init; }

    // ---- stone bridges ----
    public int Arches { get; init; } = 1;
    /// <summary>Each arch's width at the bottom (mm).</summary>
    public double ArchSpanMm { get; init; } = 40;
    public ArchShape Arch { get; init; } = ArchShape.Round;
    /// <summary>Straight sides under each arch before it starts to curve (mm).</summary>
    public double SpringMm { get; init; } = 3;
    /// <summary>How deep the ring of wedge-shaped stones round each arch is (mm).</summary>
    public double RingMm { get; init; } = 3.5;
    /// <summary>The piers between arches (mm; 0 = to suit the arches).</summary>
    public double PierMm { get; init; }
    public RoadShape Road { get; init; } = RoadShape.Level;
    /// <summary>Humped roads: how high the road is at the ends (mm).</summary>
    public double EndHeightMm { get; init; } = 14;
    public ParapetTop Parapet { get; init; } = ParapetTop.Straight;
    /// <summary>How high the parapet walls stand over the road (mm).</summary>
    public double ParapetMm { get; init; } = 7;
    /// <summary>The texture on the side walls: one of the wall maker's presets, or your own (<see cref="Custom"/>).</summary>
    public string Texture { get; init; } = "Stone blocks";
    public CustomTexture? Custom { get; init; }
    public double SizeA { get; init; } = 8;
    public double SizeB { get; init; } = 4;
    public double GrooveMm { get; init; } = 0.6;
    public RoadSurface Surface { get; init; } = RoadSurface.Cobbles;

    // ---- the sheet ----
    /// <summary>The sheet's thickness (mm): drives every slot, notch and tab.</summary>
    public double ThicknessMm { get; init; } = 2;
    /// <summary>Gap added to slots and notches so pieces go in (mm), on top of the kerf the cut layer allows for.</summary>
    public double FitMm { get; init; } = 0.1;
    /// <summary>Wood grain scored on planks, beams and posts.</summary>
    public bool Grain { get; init; } = true;
    /// <summary>Stones, grain and rough planks are laid out at random: each number gives a different layout.</summary>
    public int Seed { get; init; } = 1;
    /// <summary>The figures it's for (mm): only used to scale the sizes when it's changed.</summary>
    public double ScaleMm { get; init; } = 28;

    public string ToJson() => JsonSerializer.Serialize(this, BridgeJson.Options);

    public static BridgeSpec? FromJson(string json)
    {
        try { return JsonSerializer.Deserialize<BridgeSpec>(json, BridgeJson.Options); }
        catch (Exception e) when (e is JsonException or NotSupportedException or ArgumentException) { return null; }
    }
}

/// <summary>Your own texture, as the wall maker saves it: shapes repeated across the face. Closed shapes are etched as areas, open ones as lines.</summary>
public sealed record CustomTexture
{
    public string Name { get; init; } = "";
    /// <summary>The shapes, as SVG path data in the program's millimetres (y down), with the tile's top-left corner at 0, 0.</summary>
    public string Path { get; init; } = "";
    public double TileWidthMm { get; init; } = 10;
    public double TileHeightMm { get; init; } = 10;
    /// <summary>Every other row shifted along by this share of a tile (0 to 1).</summary>
    public double RowShift { get; init; }
}

/// <summary>Sizes that suit 28 mm figures (about 1:60).</summary>
public static class Defaults
{
    public sealed record KindInfo(BridgeKind Kind, string Name, string About);

    public static readonly IReadOnlyList<KindInfo> Kinds =
    [
        new(BridgeKind.Plank, "Plank bridge", "Loose planks across two side beams, level or arched, with a railing and legs if you like. Each plank drops into notches in the beams."),
        new(BridgeKind.Rope, "Rope bridge", "Planks hanging between two posts at each end on sagging ropes, with a rope handrail: to string between two hills or cliffs."),
        new(BridgeKind.Stone, "Stone arch bridge", "Two side walls with one or more arches and parapets, stone etched on, joined by the road and walls inside."),
    ];

    public static KindInfo Info(BridgeKind k) => Kinds.First(x => x.Kind == k);

    public static BridgeSpec For(BridgeKind k) => k switch
    {
        BridgeKind.Rope => new()
        {
            Kind = k, LengthMm = 120, WidthMm = 18, HeightMm = 30, RiseMm = 10, Ends = EndsOn.Banks, PlankMm = 4, GapMm = 0.8,
            Railing = true, RailMm = 14, PostEvery = 3, PostMm = 3.5, MissingPlanks = 1, Rough = true, ThicknessMm = 2, FitMm = 0.1,
        },
        BridgeKind.Stone => new()
        {
            Kind = k, LengthMm = 100, WidthMm = 36, HeightMm = 32, Arches = 1, ArchSpanMm = 40, Arch = ArchShape.Round, SpringMm = 3,
            RingMm = 3.5, Road = RoadShape.Level, EndHeightMm = 14, Parapet = ParapetTop.Straight, ParapetMm = 7,
            Texture = "Stone blocks", SizeA = 8, SizeB = 4, GrooveMm = 0.6, Surface = RoadSurface.Cobbles, ThicknessMm = 3, FitMm = 0.15,
        },
        _ => new()
        {
            Kind = k, LengthMm = 100, WidthMm = 24, HeightMm = 10, RiseMm = 8, Ends = EndsOn.Table, PlankMm = 4, GapMm = 0.6, BeamMm = 4,
            Railing = true, RailMm = 14, PostEvery = 4, Legs = 0, LegMm = 4.5, ThicknessMm = 2, FitMm = 0.1,
        },
    };

    /// <summary>Every size scaled (for other figures); the sheet, the fit and counts stay.</summary>
    public static BridgeSpec Scaled(BridgeSpec s, double k)
    {
        double R(double v) => Math.Round(v * k, 1);
        return s with
        {
            LengthMm = R(s.LengthMm), WidthMm = R(s.WidthMm), HeightMm = R(s.HeightMm), RiseMm = R(s.RiseMm), PlankMm = R(s.PlankMm),
            BeamMm = R(s.BeamMm), RailMm = R(s.RailMm), LegMm = R(s.LegMm), PostMm = R(s.PostMm), ArchSpanMm = R(s.ArchSpanMm),
            SpringMm = R(s.SpringMm), RingMm = R(s.RingMm), PierMm = R(s.PierMm), EndHeightMm = R(s.EndHeightMm), ParapetMm = R(s.ParapetMm),
            SizeA = R(s.SizeA), SizeB = R(s.SizeB),
        };
    }
}

static class BridgeJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        Converters = { new JsonStringEnumConverter() },
    };
}

public enum Severity { Note, Warn, Bad }
public sealed record Finding(Severity Level, string Text);
