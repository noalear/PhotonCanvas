using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace PhotonCanvas.Addon.Ruins;

public enum EtchStyle { Grooves, Lines }
/// <summary>Where the wall is broken down most.</summary>
public enum BreakShape { Dip, FallsLeft, FallsRight, TwoGaps, Stump }
/// <summary>A whole end is straight and full height, to slide into the wall maker's columns; a broken one crumbles away.</summary>
public enum EndKind { Whole, Broken }
public enum PileShape { Heap, AgainstWall, Corner }
public enum PillarShape { Round, Square, Octagon }

/// <summary>What a texture is made from: the same names and numbers as the wall maker's.</summary>
public sealed record TextureSpec
{
    /// <summary>A preset (<see cref="Textures.Presets"/>) or the name of a texture of your own made in the wall maker.</summary>
    public string Texture { get; init; } = "Brick";
    public CustomTexture? Custom { get; init; }
    public double SizeA { get; init; } = 8;
    public double SizeB { get; init; } = 3.5;
    public double GrooveMm { get; init; } = 0.6;
    public int Seed { get; init; } = 1;
}

/// <summary>Everything a set of ruins is made from. Saved on the shapes it makes, so it can be opened and changed.</summary>
public sealed record RuinSpec
{
    // ---- the broken wall ----

    /// <summary>Make a broken wall section (otherwise only the rubble and pillars).</summary>
    public bool Wall { get; init; } = true;
    public double LengthMm { get; init; } = 100;
    /// <summary>Height of the wall where it's whole (mm).</summary>
    public double HeightMm { get; init; } = 50;
    /// <summary>The sheet's thickness (mm): every piece is cut from it.</summary>
    public double ThicknessMm { get; init; } = 3;
    /// <summary>Two panels glued back to back (mirror images), so both faces are textured.</summary>
    public bool TwoSided { get; init; }
    public int Walls { get; init; } = 1;
    public EndKind LeftEnd { get; init; } = EndKind.Whole;
    public EndKind RightEnd { get; init; } = EndKind.Whole;
    /// <summary>How deep the wall maker's columns' slots are (mm): a whole end is kept straight (and smooth) that far.</summary>
    public double SlotDepthMm { get; init; } = 4;
    /// <summary>Keep whole ends smooth where they go into the columns.</summary>
    public bool PlainEnds { get; init; } = true;

    // ---- the texture ----
    /// <summary>The look picked to start from (<see cref="RuinThemes"/>): it set the texture and pillars, which can be changed afterwards.</summary>
    public string Theme { get; init; } = "Medieval";

    public string Texture { get; init; } = "Brick";
    public CustomTexture? Custom { get; init; }
    public double SizeA { get; init; } = 8;
    public double SizeB { get; init; } = 3.5;
    public double GrooveMm { get; init; } = 0.6;
    /// <summary>The variation: where the wall breaks, and how stones and planks are laid.</summary>
    public int Seed { get; init; } = 1;
    public EtchStyle Etch { get; init; } = EtchStyle.Grooves;

    // ---- the damage ----

    public BreakShape Break { get; init; } = BreakShape.Dip;
    /// <summary>How far down the wall is broken at its lowest, as a share of the height.</summary>
    public double BreakDepth { get; init; } = 0.55;
    /// <summary>Holes knocked right through the wall.</summary>
    public int Breaches { get; init; }
    public double BreachMm { get; init; } = 14;
    /// <summary>Single bricks or stones knocked out (cut right through).</summary>
    public int KnockedOut { get; init; } = 3;
    /// <summary>Cracks scored down from the broken top.</summary>
    public int Cracks { get; init; } = 2;

    // ---- a base for a free-standing wall ----

    /// <summary>A base the wall's tabs slot into (only for a wall with both ends broken).</summary>
    public bool Base { get; init; }
    /// <summary>How far the base reaches out in front of and behind the wall (mm).</summary>
    public double BaseMm { get; init; } = 14;
    /// <summary>Rubble heaped against the wall on its base, where the wall fell.</summary>
    public bool FootRubble { get; init; } = true;

    // ---- rubble ----

    public int Piles { get; init; }
    public PileShape PileShape { get; init; } = PileShape.Heap;
    public double PileWidthMm { get; init; } = 40;
    public double PileDepthMm { get; init; } = 30;
    /// <summary>How high a pile is (mm); it's stacked from layers of the sheet.</summary>
    public double PileHeightMm { get; init; } = 12;
    /// <summary>Score where each layer of a pile goes on the one under it.</summary>
    public bool MarkLayers { get; init; } = true;
    /// <summary>Single bricks or stones to scatter.</summary>
    public int LooseStones { get; init; } = 12;
    /// <summary>Broken-off pieces of wall to lay on the ground.</summary>
    public int Chunks { get; init; } = 2;

    // ---- pillars ----

    public int Pillars { get; init; }
    public PillarShape PillarShape { get; init; } = PillarShape.Round;
    /// <summary>Across the pillar (mm).</summary>
    public double PillarMm { get; init; } = 14;
    /// <summary>How high the tallest part of the broken pillar is (mm).</summary>
    public double PillarHeightMm { get; init; } = 30;
    /// <summary>A hole up the middle for a pin that lines the layers up. 0 = none.</summary>
    public double PinMm { get; init; } = 2;
    /// <summary>A wider square layer at the bottom of each pillar.</summary>
    public bool Plinth { get; init; } = true;

    /// <summary>Gap added to slots so parts slide together (mm), on top of the kerf the cut layer allows for.</summary>
    public double FitMm { get; init; } = 0.15;

    public TextureSpec TextureOf(int seed) => new() { Texture = Texture, Custom = Custom, SizeA = SizeA, SizeB = SizeB, GrooveMm = GrooveMm, Seed = seed };
    public TextureSpec TextureSpec => TextureOf(Seed);

    public string ToJson() => JsonSerializer.Serialize(this, RuinJson.Options);

    public static RuinSpec? FromJson(string json)
    {
        try { return JsonSerializer.Deserialize<RuinSpec>(json, RuinJson.Options); }
        catch (Exception e) when (e is JsonException or NotSupportedException or ArgumentException) { return null; }
    }
}

/// <summary>A texture of your own (made in the wall maker): shapes repeated across the face. Closed shapes are etched as areas, open ones as lines.</summary>
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

static class RuinJson
{
    // Own resolver (docs/addons.md): options equal to the defaults would share a cache the program keeps.
    public static readonly JsonSerializerOptions Options = new()
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        Converters = { new JsonStringEnumConverter() },
    };
}

public enum Severity { Note, Warn, Bad }
public sealed record Finding(Severity Level, string Text);
