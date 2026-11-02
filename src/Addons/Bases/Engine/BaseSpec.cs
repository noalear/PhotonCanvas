using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using PhotonCanvas.Addon.Walls;

namespace PhotonCanvas.Addon.Bases;

/// <summary>A base's outline.</summary>
public enum BaseShape { Round, Oval, Square, Rectangle, Hexagon }

/// <summary>What goes through the base for the figure: nothing, or a slot for a figure cast with a tab under its feet.</summary>
public enum FigureFixing { None, Slot }

/// <summary>A movement tray's pockets: one for each base, or one open pocket the whole unit sits in.</summary>
public enum TrayStyle { Pockets, Open }

/// <summary>Holes in a movement tray's bottom under each base: none, a hole to push the base out from underneath, or a hole for a magnet.</summary>
public enum TrayHoles { None, PushOut, Magnets }

/// <summary>One size of base (mm) and how many to cut. A round base uses only its width; a hexagon's width is across its flats.</summary>
public sealed record BaseSize
{
    public BaseShape Shape { get; init; } = BaseShape.Round;
    public double WidthMm { get; init; } = 25;
    public double DepthMm { get; init; } = 25;
    public int Count { get; init; } = 10;
}

/// <summary>A movement tray: a frame with a pocket for each base of one size, on a bottom plate.</summary>
public sealed record TraySpec
{
    /// <summary>The bases it holds (by default the first size in the list).</summary>
    public BaseShape Shape { get; init; } = BaseShape.Round;
    public double WidthMm { get; init; } = 25;
    public double DepthMm { get; init; } = 25;
    /// <summary>Bases side by side (across the front) and rows of them.</summary>
    public int Columns { get; init; } = 5;
    public int Rows { get; init; } = 2;
    /// <summary>Every other row moved along half a base, as round bases pack closest.</summary>
    public bool Stagger { get; init; }
    public TrayStyle Style { get; init; } = TrayStyle.Pockets;
    /// <summary>Space between neighbouring bases (mm).</summary>
    public double GapMm { get; init; } = 2;
    /// <summary>Play round each base in its pocket (mm, all round), on top of the kerf the cut layer allows for.</summary>
    public double ClearMm { get; init; } = 0.4;
    /// <summary>The frame's width round the outside (mm).</summary>
    public double BorderMm { get; init; } = 4;
    /// <summary>The tray's corners rounded off (mm).</summary>
    public double CornerMm { get; init; } = 3;
    /// <summary>Frame layers stacked on the bottom plate (deeper pockets for thicker bases).</summary>
    public int FrameLayers { get; init; } = 1;
    public bool Bottom { get; init; } = true;
    public TrayHoles Holes { get; init; } = TrayHoles.PushOut;
    /// <summary>The ground texture etched on the frame too.</summary>
    public bool Textured { get; init; } = true;
    public int Count { get; init; } = 1;
}

/// <summary>Everything a set of bases (and movement trays) is made from. Saved on the shapes it makes, so it can be opened and changed.</summary>
public sealed record BaseSpec
{
    public List<BaseSize> Bases { get; init; } = [new()];

    /// <summary>The sheet's thickness (mm).</summary>
    public double ThicknessMm { get; init; } = 3;
    /// <summary>Layers glued into each base: thicker bases, and with a step, a stepped edge.</summary>
    public int Layers { get; init; } = 1;
    /// <summary>How much smaller each layer is than the one under it, all round (mm): a stepped, bevelled look.</summary>
    public double StepMm { get; init; } = 0.8;
    /// <summary>Square and rectangular bases' corners rounded off (mm).</summary>
    public double CornerMm { get; init; } = 1;

    /// <summary>A preset (<see cref="GroundTextures.Presets"/>) or the name of one of your own (shared with the wall maker).</summary>
    public string Texture { get; init; } = "Cracked earth";
    public CustomTexture? Custom { get; init; }
    public double SizeA { get; init; } = 4;
    public double SizeB { get; init; } = 0;
    public double GrooveMm { get; init; } = 0.5;
    public int Seed { get; init; } = 1;
    public EtchStyle Etch { get; init; } = EtchStyle.Grooves;
    /// <summary>Share of the stones (%) with a crack scored across them (paving textures).</summary>
    public double CracksPct { get; init; } = 10;
    /// <summary>A plain band round the top's edge, with a line scored along its inside (mm; 0 = texture to the edge).</summary>
    public double RimMm { get; init; } = 1;
    /// <summary>A plain round spot in the middle for the figure's feet (mm across; 0 = none).</summary>
    public double SpotMm { get; init; }

    public FigureFixing Fixing { get; init; } = FigureFixing.None;
    public double SlotLengthMm { get; init; } = 20;
    public double SlotWidthMm { get; init; } = 2.2;
    /// <summary>A round magnet's diameter (mm; 0 = none): a hole for it in the bottom layer.</summary>
    public double MagnetMm { get; init; }
    /// <summary>Play round a magnet in its hole (mm, total).</summary>
    public double FitMm { get; init; } = 0.15;

    public bool MakeTray { get; init; }
    public TraySpec Tray { get; init; } = new();

    /// <summary>Layers in each base: as asked, and at least two when there are magnets (their holes are in the bottom one).</summary>
    [JsonIgnore]
    public int LayerCount => Math.Clamp(Layers, 1, 4) + (MagnetMm > 0 && Layers <= 1 ? 1 : 0);

    public string ToJson() => JsonSerializer.Serialize(this, BaseJson.Options);

    public static BaseSpec? FromJson(string json)
    {
        try { return JsonSerializer.Deserialize<BaseSpec>(json, BaseJson.Options); }
        catch (Exception e) when (e is JsonException or NotSupportedException or ArgumentException) { return null; }
    }
}

static class BaseJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        Converters = { new JsonStringEnumConverter() },
    };
}
