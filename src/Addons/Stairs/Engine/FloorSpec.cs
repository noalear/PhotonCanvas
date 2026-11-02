using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using PhotonCanvas.Addon.Walls;

namespace PhotonCanvas.Addon.Stairs;

/// <summary>How far the floor plate reaches past the walls.</summary>
public enum FloorEdge { OverColumns, FlushWithWalls, Overhang }

/// <summary>What goes up to the floor.</summary>
public enum WayUp { None, Stairs, SolidStairs, Spiral, Ladder, HookedLadder }

/// <summary>Which side of open stairs has a handrail (left and right as you go up).</summary>
public enum Railing { None, Left, Right, Both }

/// <summary>
/// An upper floor for a room of the wall maker's walls, and the way up to it. Everything is in mm.
/// The room is laid out on a plan from the centre of its first corner column: x along it, y across
/// it. Saved on the shapes it makes, so it can be opened and changed.
/// </summary>
public sealed record FloorSpec
{
    // ---- the walls it sits on (the wall maker's settings) ----

    /// <summary>The walls' height (mm): the plate sits on their tops.</summary>
    public double WallHeightMm { get; init; } = 50;
    /// <summary>The sheet the walls (and their columns) are cut from (mm).</summary>
    public double WallSheetMm { get; init; } = 3;
    /// <summary>The walls are two panels glued back to back.</summary>
    public bool TwoSidedWalls { get; init; }
    /// <summary>The room's length along x, column centre to column centre, for one wall (mm).</summary>
    public double WallLengthMm { get; init; } = 108.5;
    /// <summary>Walls end to end along the room's length.</summary>
    public int WallsAlong { get; init; } = 1;
    /// <summary>The room's width along y, column centre to column centre, for one wall (mm).</summary>
    public double WallWidthMm { get; init; } = 108.5;
    /// <summary>Walls end to end across the room.</summary>
    public int WallsAcross { get; init; } = 1;
    /// <summary>The walls are joined by the wall maker's columns: the plate has a hole for each.</summary>
    public bool Columns { get; init; } = true;
    public ColumnShape Column { get; init; } = ColumnShape.Auto;
    /// <summary>Column size across its flats (mm).</summary>
    public double ColumnMm { get; init; } = 16;
    /// <summary>The columns have a wider cap on top.</summary>
    public bool ColumnCaps { get; init; } = true;

    // ---- the sheet ----

    /// <summary>The sheet the floor and the way up are cut from (mm).</summary>
    public double SheetMm { get; init; } = 3;
    /// <summary>Gap added to slots so parts slide together (mm), on top of the kerf the cut layer allows for.</summary>
    public double FitMm { get; init; } = 0.15;

    // ---- the floor ----

    public bool Floor { get; init; } = true;
    public FloorEdge Edge { get; init; } = FloorEdge.OverColumns;
    /// <summary>How far the plate reaches past the walls' outer faces with <see cref="FloorEdge.Overhang"/> (mm): a jettied upper storey or a ledge.</summary>
    public double OverhangMm { get; init; } = 6;
    /// <summary>How many plates like this to cut (one per storey).</summary>
    public int Floors { get; init; } = 1;
    /// <summary>Pieces the plate is cut in along its length and across it (joined with dovetails), for floors bigger than the bed.</summary>
    public int PiecesAlong { get; init; } = 1;
    public int PiecesAcross { get; init; } = 1;

    /// <summary>A preset of the wall maker's (<see cref="Textures.Presets"/>), the name of your own, or <see cref="Textures.Plain"/>.</summary>
    public string Texture { get; init; } = "Wood planks (across)";
    public CustomTexture? Custom { get; init; }
    public double SizeA { get; init; } = 5;
    public double SizeB { get; init; } = 35;
    public double GrooveMm { get; init; } = 0.5;
    public int Seed { get; init; } = 1;
    public EtchStyle Etch { get; init; } = EtchStyle.Grooves;
    /// <summary>Turn the texture a quarter turn, so planks run across the room instead of along it.</summary>
    public bool TextureAcross { get; init; }

    // ---- the opening ----

    /// <summary>An opening in the plate where the way up comes through.</summary>
    public bool Hatch { get; init; } = true;
    /// <summary>Size the opening to the way up and put it in the first corner, along the first wall.</summary>
    public bool HatchAuto { get; init; } = true;
    /// <summary>The opening's near corner on the plan (mm from the first corner column's centre), when not automatic.</summary>
    public double HatchXMm { get; init; } = 15;
    public double HatchYMm { get; init; } = 3;
    /// <summary>The opening's size along the room and across it (mm), when not automatic.</summary>
    public double HatchLengthMm { get; init; } = 60;
    public double HatchWidthMm { get; init; } = 34;
    /// <summary>A loose trapdoor that lies over the opening.</summary>
    public bool Trapdoor { get; init; }

    // ---- the way up ----

    public WayUp Way { get; init; } = WayUp.Stairs;
    /// <summary>How high the way up climbs (mm) when there's no floor; with a floor it climbs to the floor's top.</summary>
    public double ClimbMm { get; init; } = 53;
    /// <summary>Stairs: width between the sides (mm), the height of each step and how deep each step is.</summary>
    public double StairWidthMm { get; init; } = 25;
    public double RiseMm { get; init; } = 6;
    public double GoingMm { get; init; } = 7;
    public Railing Rail { get; init; } = Railing.None;
    /// <summary>Etch the floor's texture on the steps.</summary>
    public bool TreadTexture { get; init; } = true;
    /// <summary>Spiral stairs: radius, how far round each step turns (degrees), the centre's size and the pin it turns on.</summary>
    public double SpiralRadiusMm { get; init; } = 26;
    public double SpiralStepDeg { get; init; } = 30;
    public double SpiralHubMm { get; init; } = 9;
    public double SpiralPinMm { get; init; } = 3;
    /// <summary>Spiral stairs turn clockwise as they go up (seen from above).</summary>
    public bool Clockwise { get; init; } = true;
    /// <summary>Ladders: width between the rails (mm), the distance between rungs, and how steeply a leaning ladder stands (degrees).</summary>
    public double LadderWidthMm { get; init; } = 12;
    public double RungStepMm { get; init; } = 6;
    public double LadderAngleDeg { get; init; } = 75;
    /// <summary>How many ways up like this to cut.</summary>
    public int WaysUp { get; init; } = 1;

    public string ToJson() => JsonSerializer.Serialize(this, FloorJson.Options);

    public static FloorSpec? FromJson(string json)
    {
        try { return JsonSerializer.Deserialize<FloorSpec>(json, FloorJson.Options); }
        catch (Exception e) when (e is JsonException or NotSupportedException or ArgumentException) { return null; }
    }

    /// <summary>The floor made to sit on walls made with these wall maker settings.</summary>
    public FloorSpec FitTo(WallSpec w)
    {
        double centres = WallKitMaker.PanelLength(w) + 2 * WallKitMaker.CentreToEnd(w);
        return this with
        {
            WallHeightMm = w.HeightMm, WallSheetMm = w.ThicknessMm, TwoSidedWalls = w.TwoSided,
            WallLengthMm = Math.Round(centres, 2), WallWidthMm = Math.Round(centres, 2),
            Columns = true, Column = w.Column, ColumnMm = w.ColumnMm, ColumnCaps = w.Cap,
            SheetMm = w.ThicknessMm, FitMm = w.FitMm,
        };
    }
}

static class FloorJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        Converters = { new JsonStringEnumConverter() },
    };
}
