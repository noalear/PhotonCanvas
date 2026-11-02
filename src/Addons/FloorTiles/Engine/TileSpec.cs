using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using PhotonCanvas.Addon.Walls;

namespace PhotonCanvas.Addon.FloorTiles;

/// <summary>A whole board split into tiles, or a list of loose tiles of the sizes you say.</summary>
public enum TileMode { Board, Tiles }

/// <summary>How tiles hold on to each other.</summary>
public enum TileJoin
{
    /// <summary>Jigsaw tabs and sockets cut in the edges: on every grid square of every edge, a tab then a socket, so any tile fits any other, any way round.</summary>
    Jigsaw,
    /// <summary>Half-bowtie sockets in the edges and separate bowtie clips that bridge two tiles (pulled out again to take the floor apart).</summary>
    Clips,
    /// <summary>Straight edges with holes for round magnets in a base layer, two on every grid square of every edge, north and south up in turn.</summary>
    Magnets,
    /// <summary>Plain straight edges.</summary>
    None,
}

/// <summary>Sides left straight with no joints (the outside of a board): none, the bottom, the bottom and left (a corner), or every side.</summary>
public enum PlainSides { None, One, Corner, Opposite, Three, All }

/// <summary>The grid squares marked on the tile: not at all (the texture shows them), scored, or etched as a groove.</summary>
public enum GridMarks { None, Scored, Grooved }

/// <summary>One size of loose tile (in grid squares) and how many to cut.</summary>
public sealed record TileSize
{
    public double Across { get; init; } = 2;
    public double Down { get; init; } = 2;
    public int Count { get; init; } = 4;
    public PlainSides Plain { get; init; } = PlainSides.None;
}

/// <summary>Everything a set of floor tiles is made from. Saved on the shapes it makes, so it can be opened and changed.</summary>
public sealed record TileSpec
{
    /// <summary>One grid square (mm). 25.4 (an inch) is the usual square for 28–32 mm figures.</summary>
    public double SquareMm { get; init; } = 25.4;
    public TileMode Mode { get; init; } = TileMode.Board;

    /// <summary>The board's size in squares (Board mode). A part square leaves the far column or row narrower.</summary>
    public double BoardAcross { get; init; } = 6;
    public double BoardDown { get; init; } = 6;
    /// <summary>The largest tile the board is split into, in squares.</summary>
    public int TileAcross { get; init; } = 2;
    public int TileDown { get; init; } = 2;
    /// <summary>Straight edges round the outside of the board, so it ends neatly.</summary>
    public bool PlainBorder { get; init; } = true;

    /// <summary>The loose tiles (Tiles mode).</summary>
    public List<TileSize> Tiles { get; init; } = [new()];

    /// <summary>The sheet's thickness (mm).</summary>
    public double ThicknessMm { get; init; } = 3;

    /// <summary>A preset (<see cref="FloorTextures.Presets"/>) or the name of one of your own (shared with the wall maker).</summary>
    public string Texture { get; init; } = "Dungeon flagstones";
    public CustomTexture? Custom { get; init; }
    public double SizeA { get; init; } = 50;
    public double SizeB { get; init; } = 0;
    public double GrooveMm { get; init; } = 0.8;
    public int Seed { get; init; } = 1;
    public EtchStyle Etch { get; init; } = EtchStyle.Grooves;
    /// <summary>Share of the stones (%) with a crack scored across them.</summary>
    public double CracksPct { get; init; } = 15;
    public GridMarks Grid { get; init; } = GridMarks.None;

    public TileJoin Join { get; init; } = TileJoin.Jigsaw;
    /// <summary>Size of a jigsaw tab's round head, or a clip's width (mm).</summary>
    public double TabMm { get; init; } = 5;
    /// <summary>Play between a tab and its socket, or a clip and its socket (mm, half each side), on top of the kerf the cut layer allows for.</summary>
    public double FitMm { get; init; } = 0.15;
    /// <summary>A second, plain layer glued under each tile: thicker, flatter tiles, and somewhere to hide the clips (magnets always have one).</summary>
    public bool BaseLayer { get; init; }
    /// <summary>The magnets' diameter (mm).</summary>
    public double MagnetMm { get; init; } = 5;

    /// <summary>Is there a base layer under each tile (asked for, or needed for the magnets)?</summary>
    [JsonIgnore]
    public bool HasBase => BaseLayer || Join == TileJoin.Magnets;

    public string ToJson() => JsonSerializer.Serialize(this, TileJson.Options);

    public static TileSpec? FromJson(string json)
    {
        try { return JsonSerializer.Deserialize<TileSpec>(json, TileJson.Options); }
        catch (Exception e) when (e is JsonException or NotSupportedException or ArgumentException) { return null; }
    }
}

static class TileJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        Converters = { new JsonStringEnumConverter() },
    };
}
