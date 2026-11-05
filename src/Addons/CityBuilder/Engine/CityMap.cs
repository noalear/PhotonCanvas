using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using PhotonCanvas.Addon.HouseEditor;

namespace PhotonCanvas.Addon.CityBuilder;

/// <summary>What a square of the map's ground is.</summary>
public enum Ground { Grass, Dirt, Road, Paving, Sand, Water, Rock, Field, Floor }

/// <summary>What a fence or wall along the grid lines is.</summary>
public enum FenceKind { Fence, Hedge, StoneWall, CityWall }

/// <summary>
/// Small things dotted about the map, shown in 3D: trees, rocks, a well, carts and so on, and the town's
/// bigger pieces: horse troughs, a players' stage, market stalls, outdoor bars, notice boards and guard towers.
/// </summary>
public enum PropKind { Tree, Pine, Bush, Boulder, Well, Cart, Crates, Lamp, Statue, Trough, Stage, Stall, Bar, NoticeBoard, GuardTower, Fountain, Gallows }

/// <summary>
/// A building on the map: the building editor's design (its plan's top left corner at 0, 0), where its top
/// left corner is on the map (squares) and how many quarter turns it's turned (clockwise seen from above).
/// The turn only places it: the pieces are cut from the design as it is. <see cref="PrefabId"/> is the
/// building editor's saved building it came from, if any.
/// </summary>
public sealed record CityBuilding
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = "House";
    /// <summary>What it is: house, shop, tavern, temple… (the building editor's types, or anything typed).</summary>
    public string Type { get; init; } = "House";
    /// <summary>The part of town it belongs to: residential, market, docks…</summary>
    public string District { get; init; } = "";
    public List<string> Tags { get; init; } = [];
    public Guid? PrefabId { get; init; }
    public int X { get; init; }
    public int Y { get; init; }
    public int Turn { get; init; }
    public HouseDesign Design { get; init; } = new();
}

/// <summary>A fence, hedge or wall along one square's edge of the grid, from grid point (X, Y) one square across (or down).</summary>
public readonly record struct FenceEdge(int X, int Y, bool Across);

/// <summary>A run of fence (or wall) of one kind along grid edges.</summary>
public sealed record Fence
{
    public FenceKind Kind { get; init; } = FenceKind.Fence;
    public List<FenceEdge> Edges { get; init; } = [];
}

/// <summary>A tree, rock, well… at a point on the map (squares), its size (1 = about a square) and turn (degrees).</summary>
public sealed record Prop
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public PropKind Kind { get; init; } = PropKind.Tree;
    public double X { get; init; }
    public double Y { get; init; }
    public double Size { get; init; } = 1;
    public double TurnDeg { get; init; }

    /// <summary>
    /// How big a rectangular thing is (squares, at size 1, before it's turned: across, then front to back),
    /// the furniture maker's town pieces' sizes; null for the round ones (a tree, a well).
    /// </summary>
    public static (double W, double D)? Footprint(PropKind k) => k switch
    {
        PropKind.Trough => (1.26, 0.43),
        PropKind.Stage => (3, 2),
        PropKind.Stall => (1.42, 0.79),
        PropKind.Bar => (1.57, 0.79),
        PropKind.NoticeBoard => (0.94, 0.39),
        PropKind.GuardTower => (1.81, 1.81),
        PropKind.Fountain => (2, 2),
        PropKind.Gallows => (1.81, 1.5),
        _ => null,
    };

    /// <summary>How far from its middle it reaches (squares), for picking it.</summary>
    [JsonIgnore]
    public double Reach => Footprint(Kind) is { } f ? Math.Max(0.3, Math.Max(f.W, f.D) / 2 * Size) : Math.Max(0.3, 0.4 * Size);

    /// <summary>Whether a point (squares) is on it.</summary>
    public bool Hit(double x, double y)
    {
        if (Footprint(Kind) is not { } f) return (X - x) * (X - x) + (Y - y) * (Y - y) <= Reach * Reach;
        double a = -TurnDeg * Math.PI / 180, dx = x - X, dy = y - Y;
        double u = dx * Math.Cos(a) - dy * Math.Sin(a), v = dx * Math.Sin(a) + dy * Math.Cos(a);
        return Math.Abs(u) <= f.W * Size / 2 + 0.05 && Math.Abs(v) <= f.D * Size / 2 + 0.05;
    }
}

/// <summary>Words written on the map (a street's or a district's name, a number for the game master's key): the middle of the text (squares) and how tall it is (squares).</summary>
public sealed record CityLabel
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Text { get; init; } = "";
    public double X { get; init; }
    public double Y { get; init; }
    public double Size { get; init; } = 0.8;

    /// <summary>Roughly how wide the text is (squares).</summary>
    public double Width => Math.Max(0.5, Text.Length * Size * 0.55);
}

/// <summary>The battle map picture a map was read from, shown under the plan: its file and its grid.</summary>
public sealed record CityPicture
{
    public string? Path { get; init; }
    public double SquarePx { get; init; }
    public double OffsetX { get; init; }
    public double OffsetY { get; init; }
}

/// <summary>
/// A town, village or city on a big grid of squares: the ground of every square (grass, roads, water…),
/// the buildings standing on it (each a building editor design, turned and placed), fences and walls along
/// the grid lines, and trees and other small things. Saved as JSON: the ground as a row of letters for each
/// row of squares (<see cref="Code"/>).
/// </summary>
public sealed record CityMap
{
    public const int MaxSide = 300;

    public string Name { get; init; } = "New town";
    public string Theme { get; init; } = "Medieval";
    public double SquareMm { get; init; } = 25.4;
    public int Across { get; init; } = 40;
    public int Down { get; init; } = 30;
    public List<string> Rows { get; init; } = [];
    public List<CityBuilding> Buildings { get; init; } = [];
    public List<Fence> Fences { get; init; } = [];
    public List<Prop> Props { get; init; } = [];
    public List<CityLabel> Labels { get; init; } = [];
    public CityPicture? Picture { get; init; }

    /// <summary>The letter each kind of ground is saved as.</summary>
    public static char Code(Ground g) => g switch
    {
        Ground.Grass => 'g', Ground.Dirt => 'd', Ground.Road => 'r', Ground.Paving => 'p', Ground.Sand => 's',
        Ground.Water => 'w', Ground.Rock => 'k', Ground.Field => 'f', _ => 'o',
    };

    public static Ground FromCode(char c) => c switch
    {
        'd' => Ground.Dirt, 'r' => Ground.Road, 'p' => Ground.Paving, 's' => Ground.Sand, 'w' => Ground.Water,
        'k' => Ground.Rock, 'f' => Ground.Field, 'o' => Ground.Floor, _ => Ground.Grass,
    };

    public static string Label(Ground g) => g switch
    {
        Ground.Road => "Road (cobbles)", Ground.Paving => "Paving (flagstones)", Ground.Field => "Field (crops)", Ground.Floor => "Floor (indoors)",
        _ => g.ToString(),
    };

    public static string Label(PropKind k) => k switch
    {
        PropKind.Pine => "Pine tree", PropKind.Crates => "Crates and barrels", PropKind.Trough => "Horse trough", PropKind.Stage => "Players' stage",
        PropKind.Stall => "Market stall", PropKind.Bar => "Outdoor bar", PropKind.NoticeBoard => "Notice board", PropKind.GuardTower => "Guard tower",
        _ => k.ToString(),
    };

    public static string Label(FenceKind k) => k switch { FenceKind.StoneWall => "Stone wall", FenceKind.CityWall => "Town wall", _ => k.ToString() };

    /// <summary>An all-grass map of this many squares.</summary>
    public static CityMap Empty(int across, int down, string name = "New town")
    {
        across = Math.Clamp(across, 1, MaxSide);
        down = Math.Clamp(down, 1, MaxSide);
        return new CityMap { Name = name, Across = across, Down = down, Rows = [.. Enumerable.Repeat(new string('g', across), down)] };
    }

    public bool In(int x, int y) => x >= 0 && y >= 0 && x < Across && y < Down;

    public Ground GroundAt(int x, int y) => !In(x, y) || y >= Rows.Count || x >= Rows[y].Length ? Ground.Grass : FromCode(Rows[y][x]);

    /// <summary>The ground as a grid to change (x across, y down).</summary>
    public Ground[,] GroundGrid()
    {
        var g = new Ground[Across, Down];
        for (int y = 0; y < Down; y++)
            for (int x = 0; x < Across; x++) g[x, y] = GroundAt(x, y);
        return g;
    }

    public CityMap WithGround(Ground[,] g) => this with
    {
        Rows = [.. Enumerable.Range(0, Down).Select(y => new string([.. Enumerable.Range(0, Across).Select(x => Code(x < g.GetLength(0) && y < g.GetLength(1) ? g[x, y] : Ground.Grass))]))],
    };

    /// <summary>The same map made a new size, kept at the top left (new squares are grass; buildings off the edge go).</summary>
    public CityMap Resized(int across, int down)
    {
        across = Math.Clamp(across, 1, MaxSide);
        down = Math.Clamp(down, 1, MaxSide);
        var old = GroundGrid();
        var g = new Ground[across, down];
        for (int y = 0; y < down; y++)
            for (int x = 0; x < across; x++) g[x, y] = x < Across && y < Down ? old[x, y] : Ground.Grass;
        var m = this with { Across = across, Down = down };
        return m.WithGround(g) with
        {
            Buildings = [.. Buildings.Where(b => { var (w, d) = CityFootprint.Size(b); return b.X + w <= across && b.Y + d <= down; })],
            Props = [.. Props.Where(p => p.X < across && p.Y < down)],
            Labels = [.. Labels.Where(l => l.X < across && l.Y < down)],
            Fences = [.. Fences.Select(f => f with { Edges = [.. f.Edges.Where(e => e.X <= across && e.Y <= down)] })],
        };
    }

    public CityBuilding? Building(Guid id) => Buildings.FirstOrDefault(b => b.Id == id);

    public CityMap WithBuilding(CityBuilding b) => this with
    {
        Buildings = Buildings.Any(x => x.Id == b.Id) ? [.. Buildings.Select(x => x.Id == b.Id ? b : x)] : [.. Buildings, b],
    };

    public CityMap Without(Guid id) => this with { Buildings = [.. Buildings.Where(b => b.Id != id)] };

    public string ToJson() => JsonSerializer.Serialize(this, CityJson.Options);

    public static CityMap? FromJson(string json)
    {
        try
        {
            var m = JsonSerializer.Deserialize<CityMap>(json, CityJson.Options);
            if (m is null) return null;
            // Older maps had no labels; their trees had no ids.
            return m with { Across = Math.Clamp(m.Across, 1, MaxSide), Down = Math.Clamp(m.Down, 1, MaxSide), Labels = m.Labels ?? [], Props = [.. (m.Props ?? []).Select(p => p.Id == Guid.Empty ? p with { Id = Guid.NewGuid() } : p)] };
        }
        catch (Exception e) when (e is JsonException or NotSupportedException or ArgumentException) { return null; }
    }
}

static class CityJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        Converters = { new JsonStringEnumConverter() },
        WriteIndented = false,
    };
}
