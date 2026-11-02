using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using PhotonCanvas.Addon.Buildings;
using PhotonCanvas.Addon.FloorTiles;
using PhotonCanvas.Addon.Walls;

namespace PhotonCanvas.Addon.MapTerrain;

/// <summary>How the floor tiles join: the floor tile maker's jigsaw edges, its clips, or plain edges.</summary>
public enum MapJoin { Jigsaw, Clips, None }

/// <summary>Where a battle map was read from, kept so the picture can be shown under the plan again.</summary>
public sealed record MapPicture
{
    /// <summary>The picture file (null when it came from the design).</summary>
    public string? Path { get; init; }
    /// <summary>One grid square in the picture (pixels) and where the first square's top left corner is.</summary>
    public double SquarePx { get; init; } = 70;
    public double OffsetX { get; init; }
    public double OffsetY { get; init; }
}

/// <summary>
/// Everything a map's terrain is made from: the plan, the grid square, the walls (the wall maker's),
/// the doors and the floor tiles. Saved on the pieces it makes, so it can be opened and changed.
/// </summary>
public sealed record MapSpec
{
    public MapPlan Plan { get; init; } = MapPlan.Empty(10, 8);
    /// <summary>One grid square (mm): 25.4 (an inch) for 28–32 mm figures.</summary>
    public double SquareMm { get; init; } = 25.4;
    /// <summary>The look picked last (only a starting point: everything can be changed after).</summary>
    public string Theme { get; init; } = "Dungeon";

    // ---- walls ----
    public bool Walls { get; init; } = true;
    /// <summary>Walls' height (mm), rounded to whole layers of the sheet so the columns come out flush.</summary>
    public double WallHeightMm { get; init; } = 40;
    /// <summary>The sheet the walls, columns and floor tiles are cut from (mm).</summary>
    public double SheetMm { get; init; } = 3;
    /// <summary>Two panels glued back to back, textured on both faces.</summary>
    public bool TwoSided { get; init; }
    /// <summary>Gap added to the columns' slots (mm), on top of the kerf the cut layer allows for.</summary>
    public double FitMm { get; init; } = 0.15;
    public double ColumnMm { get; init; } = 12;
    /// <summary>A wider layer on top of each column.</summary>
    public bool Caps { get; init; } = true;
    /// <summary>The longest wall panel, in squares: longer runs are split with a straight column.</summary>
    public int LongestSquares { get; init; } = 4;
    public string WallTexture { get; init; } = "Stone blocks";
    public EtchStyle Etch { get; init; } = EtchStyle.Grooves;
    public TopStyle Top { get; init; } = TopStyle.Straight;

    // ---- doors ----
    public DoorStyle Door { get; init; } = DoorStyle.Swinging;
    public OpeningShape DoorShape { get; init; } = OpeningShape.Arched;
    public double DoorWidthMm { get; init; } = 16;
    public double DoorHeightMm { get; init; } = 28;
    /// <summary>The sheet a door that swings is cut from (mm), and its fit.</summary>
    public double DoorSheetMm { get; init; } = 2;
    public double DoorFitMm { get; init; } = 0.1;

    // ---- floor ----
    public bool Floor { get; init; } = true;
    public string FloorTexture { get; init; } = "Dungeon flagstones";
    /// <summary>The biggest tile, in squares each way: the floor is cut into tiles no bigger.</summary>
    public int TileSquares { get; init; } = 2;
    public MapJoin Join { get; init; } = MapJoin.Jigsaw;
    public double TabMm { get; init; } = 5;
    /// <summary>The tiles reach out under the walls round the floor, so the walls and columns stand level.</summary>
    public bool UnderWalls { get; init; } = true;

    /// <summary>Stones and boards are laid out at random: each number gives a different layout.</summary>
    public int Seed { get; init; } = 1;

    /// <summary>The battle map the plan was read from, if any.</summary>
    public MapPicture? Picture { get; init; }

    public string ToJson() => JsonSerializer.Serialize(this, MapJson.Options);

    public static MapSpec? FromJson(string json)
    {
        try { return JsonSerializer.Deserialize<MapSpec>(json, MapJson.Options); }
        catch (Exception e) when (e is JsonException or NotSupportedException or ArgumentException) { return null; }
    }

    /// <summary>A look: the walls' texture and top, the floor's texture and the doors' shape that go together.</summary>
    public sealed record ThemeInfo(string Name, string Walls, string Floor, TopStyle Top, OpeningShape DoorShape, string About);

    public static readonly IReadOnlyList<ThemeInfo> Themes =
    [
        new("Dungeon", "Stone blocks", "Dungeon flagstones", TopStyle.Straight, OpeningShape.Arched, "Cut stone walls on square flagstones, arched doors."),
        new("Castle", "Brick", "Stone blocks", TopStyle.Battlements, OpeningShape.Pointed, "Brick walls with battlements on stone paving, pointed doors."),
        new("Ruins", "Fieldstone", "Cobblestones", TopStyle.Ruined, OpeningShape.Arched, "Rough stone walls broken away at the top, on cobbles."),
        new("Inn or house", "Wood planks (upright)", "Floorboards", TopStyle.Straight, OpeningShape.Rectangle, "Plank walls on floorboards, square-topped doors."),
        new("Temple", "Tiles", "Hexagons", TopStyle.Straight, OpeningShape.Pointed, "Tiled walls on hexagon tiles, pointed doors."),
        new("Sci-fi", "Sci-fi panels", "Sci-fi deck", TopStyle.Straight, OpeningShape.Rectangle, "Metal wall panels with vents and hazard stripes on deck panels, square doors."),
        new("Cave", "Cave rock", "Cave floor", TopStyle.Straight, OpeningShape.Arched, "Rough cave rock on uneven rock slabs, arched openings."),
        new("Desert", "Sandstone", "Sandstone slabs", TopStyle.Battlements, OpeningShape.Rectangle, "Sandstone walls with battlements on long sandstone slabs, square doors."),
        new("Gothic", "Stone blocks", "Chequered tiles", TopStyle.Straight, OpeningShape.Pointed, "Cut stone walls on a chequered floor, pointed doors."),
    ];

    /// <summary>The same settings with a theme's textures, top and doors.</summary>
    public MapSpec WithTheme(string name)
    {
        var t = Themes.FirstOrDefault(x => x.Name == name);
        if (t is null) return this;
        return this with { Theme = t.Name, WallTexture = t.Walls, FloorTexture = t.Floor, Top = t.Top, DoorShape = t.DoorShape };
    }

    /// <summary>A small dungeon to start from: two rooms joined by a corridor, an alcove, a wall part way across the second room and four doors.</summary>
    public static MapSpec Sample()
    {
        var g = new PlanGrid(11, 9);
        void Room(int x0, int y0, int x1, int y1) { for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++) g[x, y] = true; }
        Room(1, 1, 4, 4);   // the first room
        Room(5, 2, 6, 2);   // a corridor
        Room(7, 1, 9, 6);   // the second room
        Room(2, 5, 3, 6);   // an alcove under the first
        // Doors at both ends of the corridor, into the alcove, and out of the second room.
        g.SetMark(new EdgeId(5, 2, false), EdgeKind.Door);
        g.SetMark(new EdgeId(7, 2, false), EdgeKind.Door);
        g.SetMark(new EdgeId(2, 5, true), EdgeKind.Door);
        g.SetMark(new EdgeId(8, 7, true), EdgeKind.Door);
        // A wall part way across the second room.
        g.SetMark(new EdgeId(7, 4, true), EdgeKind.Wall);
        g.SetMark(new EdgeId(8, 4, true), EdgeKind.Wall);
        return new MapSpec { Plan = g.ToPlan() };
    }
}

static class MapJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        Converters = { new JsonStringEnumConverter() },
    };
}
