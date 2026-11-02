using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Clipper2Lib;
using PhotonCanvas.Addon.Walls;

namespace PhotonCanvas.Addon.Roofs;

public enum RoofStyle { Gable, Hip, LeanTo, Cone }
public enum GableWindow { None, Round, Arched, Square, Pointed, Angled }
/// <summary>How the edge of each ring of a stacked cone is shaped: the steps read as courses of shingles.</summary>
public enum ConeEdge { Scallops, Slates, Smooth }
public enum TowerTop { Battlements, Cone, Open }

/// <summary>
/// A roof that lifts off a building: its outer size is the outside of what it sits on (for the wall
/// maker's walls, the outside of the column caps). Saved on the shapes it makes, so it can be opened
/// and changed.
/// </summary>
public sealed record RoofSpec
{
    public RoofStyle Style { get; init; } = RoofStyle.Gable;
    /// <summary>The look picked to start from (<see cref="RoofThemes"/>): it set the shingles, pitch and gables, which can be changed afterwards.</summary>
    public string Theme { get; init; } = "Medieval";
    /// <summary>Outer size along the ridge (mm).</summary>
    public double LengthMm { get; init; } = 128.5;
    /// <summary>Outer size across the ridge (mm); a cone's diameter.</summary>
    public double WidthMm { get; init; } = 128.5;
    /// <summary>The sheet's thickness (mm).</summary>
    public double ThicknessMm { get; init; } = 3;
    /// <summary>The slope (degrees from flat).</summary>
    public double PitchDeg { get; init; } = 45;
    /// <summary>How far the roof reaches past the walls at the bottom of the slope (mm, along the slope; out from the wall for a cone).</summary>
    public double EaveMm { get; init; } = 4;
    /// <summary>How far a gable roof reaches past each end (mm).</summary>
    public double GableOverhangMm { get; init; } = 3;
    /// <summary>Gap added to slots so tabs slide in (mm), on top of the kerf the cut layer allows for.</summary>
    public double FitMm { get; init; } = 0.15;
    /// <summary>How many roofs like this to cut.</summary>
    public int Roofs { get; init; } = 1;

    /// <summary>A shingle texture (<see cref="Shingles.Presets"/>).</summary>
    public string Shingle { get; init; } = "Slate";
    public double ShingleA { get; init; } = 6;
    public double ShingleB { get; init; } = 4.5;
    public double ShingleGrooveMm { get; init; } = 0.5;
    public int Seed { get; init; } = 1;
    public EtchStyle Etch { get; init; } = EtchStyle.Grooves;
    /// <summary>Strips over the ridge (and the hips of a hipped roof) with ridge tiles etched on.</summary>
    public bool Caps { get; init; } = true;

    /// <summary>The gable ends' texture: one of the wall maker's (<see cref="Textures.Presets"/>), or the walls' own.</summary>
    public string GableTexture { get; init; } = "Brick";
    public CustomTexture? GableCustom { get; init; }
    public double GableA { get; init; } = 8;
    public double GableB { get; init; } = 3.5;
    public double GableGrooveMm { get; init; } = 0.6;
    public int GableSeed { get; init; } = 1;
    public GableWindow Window { get; init; } = GableWindow.Round;
    public double WindowMm { get; init; } = 10;

    /// <summary>Trusses between the gables (or across a hipped roof), about this far apart (mm). 0 = none.</summary>
    public double TrussSpacingMm { get; init; } = 60;
    /// <summary>From the outer edge to the inside face of the walls (mm): the locator under the roof stays inside that.</summary>
    public double InsetMm { get; init; } = 11.5;
    /// <summary>The walls' thickness (mm): the gables stand over their middle.</summary>
    public double WallMm { get; init; } = 3;
    /// <summary>Square corners the locator keeps clear of (mm from the outer corner): the wall maker's columns. 0 = none.</summary>
    public double CornerMm { get; init; } = 20;
    /// <summary>How far what the roof rests on (column caps) is above the top of the walls (mm): the locator reaches below the walls' tops.</summary>
    public double RestAboveWallsMm { get; init; } = 4;

    /// <summary>A cone's rings: how wide each one is (mm).</summary>
    public double ConeRingMm { get; init; } = 6;
    public ConeEdge ConeEdge { get; init; } = ConeEdge.Scallops;
    /// <summary>A hole down the middle of a cone's top for a cocktail stick (its spire point). 0 = none.</summary>
    public double PinMm { get; init; } = 2;

    public string ToJson() => JsonSerializer.Serialize(new RoofsDesign { Roof = this }, RoofJson.Options);
}

/// <summary>A round tower of rings stacked from sheet: stonework on the rings' edges, floors that lift apart, a door and windows, a top, and slots for walls.</summary>
public sealed record TowerSpec
{
    /// <summary>The look picked to start from (<see cref="RoofThemes"/>): it set the stonework, door and windows, which can be changed afterwards.</summary>
    public string Theme { get; init; } = "Medieval";
    public double DiameterMm { get; init; } = 60;
    /// <summary>Height of the tower's body (mm): to the top of the battlements, or to where a cone roof sits.</summary>
    public double HeightMm { get; init; } = 100;
    public double ThicknessMm { get; init; } = 3;
    /// <summary>How thick the tower's wall (each ring) is (mm).</summary>
    public double RingMm { get; init; } = 8;
    /// <summary>Storeys that lift apart, each with a floor (the battlements' deck is one more).</summary>
    public int Storeys { get; init; } = 2;
    public int Towers { get; init; } = 1;

    /// <summary>The wall maker's preset for the stonework (only the ones that work on a ring's edge: <see cref="Rings.Textures"/>).</summary>
    public string Texture { get; init; } = "Stone blocks";
    /// <summary>Block length round the tower (mm).</summary>
    public double SizeA { get; init; } = 12;
    /// <summary>Course height (mm): rounded to whole layers.</summary>
    public double SizeB { get; init; } = 6;
    public double GrooveMm { get; init; } = 0.8;
    public int Seed { get; init; } = 1;
    /// <summary>Planks etched on the floors.</summary>
    public bool FloorPlanks { get; init; } = true;

    public TowerTop Top { get; init; } = TowerTop.Battlements;
    public double MerlonWidthMm { get; init; } = 8;
    public double CrenelWidthMm { get; init; } = 6;
    public double MerlonHeightMm { get; init; } = 6;
    /// <summary>How high the wall round the top deck is (mm), merlons included.</summary>
    public double ParapetMm { get; init; } = 15;
    public double ConePitchDeg { get; init; } = 60;
    public double ConeEaveMm { get; init; } = 4;
    public ConeEdge ConeEdge { get; init; } = ConeEdge.Scallops;

    public bool Door { get; init; } = true;
    public OpeningShape DoorShape { get; init; } = OpeningShape.Arched;
    public double DoorWidthMm { get; init; } = 14;
    public double DoorHeightMm { get; init; } = 24;
    /// <summary>Which way the door faces (degrees, anticlockwise seen from above; 270 = towards you in the preview).</summary>
    public double DoorAngleDeg { get; init; } = 270;
    public int WindowsPerStorey { get; init; } = 2;
    public OpeningShape WindowShape { get; init; } = OpeningShape.ArrowSlit;
    public double WindowWidthMm { get; init; } = 3;
    public double WindowHeightMm { get; init; } = 12;
    /// <summary>Bottom of the windows above each storey's floor (mm).</summary>
    public double SillMm { get; init; } = 8;

    /// <summary>Slots for the wall maker's walls, like its columns' (null = none).</summary>
    public ConnectorKind? Join { get; init; }
    /// <summary>Angle between the two walls for <see cref="ConnectorKind.Custom"/> (degrees).</summary>
    public double JoinAngleDeg { get; init; } = 120;
    /// <summary>Which way the first wall leaves the tower (degrees, anticlockwise seen from above).</summary>
    public double JoinFirstDeg { get; init; }
    /// <summary>The walls' thickness (both panels, if they're two-sided) (mm).</summary>
    public double WallMm { get; init; } = 3;
    /// <summary>How high the walls are (mm): the slots go that high.</summary>
    public double WallHeightMm { get; init; } = 50;
    public double SlotDepthMm { get; init; } = 4;
    public double FitMm { get; init; } = 0.15;

    public double PinMm { get; init; } = 2;

    public string ToJson() => JsonSerializer.Serialize(new RoofsDesign { Tower = this }, RoofJson.Options);
}

/// <summary>What's saved on the pieces: a roof or a tower.</summary>
public sealed record RoofsDesign
{
    public RoofSpec? Roof { get; init; }
    public TowerSpec? Tower { get; init; }

    public string ToJson() => JsonSerializer.Serialize(this, RoofJson.Options);

    public static RoofsDesign? FromJson(string json)
    {
        try { return JsonSerializer.Deserialize<RoofsDesign>(json, RoofJson.Options) is { } d && (d.Roof is not null || d.Tower is not null) ? d : null; }
        catch (Exception e) when (e is JsonException or NotSupportedException or ArgumentException) { return null; }
    }
}

static class RoofJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        Converters = { new JsonStringEnumConverter() },
    };
}

/// <summary>
/// One piece to cut (y up, mm): its outline and holes, what's etched on it as areas, and lines scored
/// on it, with where each copy sits in the model put together (<see cref="Poses"/>, one per copy in one model).
/// </summary>
public sealed record RoofPart(string Name, PathsD Cut, PathsD Etched, List<PathD> Lines, int Copies, List<Pose> Poses);

/// <summary>Everything a roof or tower is cut from, with what was found while making it.</summary>
public sealed record RoofKit(List<RoofPart> Parts, List<Finding> Findings, double ThicknessMm)
{
    public bool Ok => !Findings.Any(f => f.Level == Severity.Bad);
    public int PieceCount => Parts.Sum(p => p.Copies);

    /// <summary>The kit as the wall maker's, for its flat preview of the pieces.</summary>
    public WallKit AsWallKit() => new([.. Parts.Select(p => new Part(p.Name, p.Cut, p.Etched, p.Lines, p.Copies))], Findings, 0, ThicknessMm, 0);
}
