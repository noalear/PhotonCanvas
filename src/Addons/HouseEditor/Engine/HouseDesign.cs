using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using PhotonCanvas.Addon.Buildings;
using PhotonCanvas.Addon.Walls;

namespace PhotonCanvas.Addon.HouseEditor;

/// <summary>A point where grid lines cross, counted in squares from the plan's top-left corner (x right, y towards you).</summary>
public readonly record struct GridPt(int X, int Y)
{
    public static GridPt operator +(GridPt a, GridPt b) => new(a.X + b.X, a.Y + b.Y);
    public static GridPt operator -(GridPt a, GridPt b) => new(a.X - b.X, a.Y - b.Y);
}

/// <summary>
/// One square's worth of wall: from a grid point to the next one along, across or corner to corner
/// (a diagonal). Kept with <see cref="A"/> the first of the two (left, then top) so each is stored once.
/// </summary>
public readonly record struct WallEdge(GridPt A, GridPt B)
{
    public static WallEdge Of(GridPt a, GridPt b) => a.X < b.X || (a.X == b.X && a.Y < b.Y) ? new(a, b) : new(b, a);
    [JsonIgnore] public bool Diagonal => A.X != B.X && A.Y != B.Y;
    [JsonIgnore] public GridPt Step => B - A;
}

/// <summary>What's dropped into a wall: a door that opens, a doorway with nothing in it, or a window.</summary>
public enum OpeningKind { Door, Doorway, Window }

/// <summary>
/// A door, doorway or window in a wall. It sits where it was dropped, <see cref="X"/>, <see cref="Y"/>
/// (squares, on a wall's line); the wall it's on is found from there, so it moves with the plan and drops
/// out when its wall is taken away.
/// </summary>
public sealed record Opening
{
    public OpeningKind Kind { get; init; } = OpeningKind.Window;
    public double X { get; init; }
    public double Y { get; init; }
    public OpeningShape Shape { get; init; } = OpeningShape.Arched;
    public double WidthMm { get; init; } = 8;
    public double HeightMm { get; init; } = 12;
    /// <summary>A window's bottom above its floor (mm).</summary>
    public double SillMm { get; init; } = 22;
    /// <summary>A door's look (planks, studded, metal, bars, stone).</summary>
    public DoorLook Look { get; init; } = DoorLook.Planks;
    /// <summary>Which side a door swings from, seen from the outside.</summary>
    public HingeSide Hinge { get; init; } = HingeSide.Left;
    public WindowBars Bars { get; init; } = WindowBars.Cross;
    public WindowFill Fill { get; init; } = WindowFill.Frame;

    /// <summary>What the window is like, apart from where it is: windows on one wall that are all alike can share it.</summary>
    [JsonIgnore] public (OpeningShape, double, double, double, WindowBars, WindowFill) WindowStyle => (Shape, WidthMm, HeightMm, SillMm, Bars, Fill);

    public static Opening NewDoor(BuildingSpec s, double x, double y) => new()
    {
        Kind = OpeningKind.Door, X = x, Y = y, Shape = s.DoorShape, WidthMm = s.DoorWidthMm, HeightMm = s.DoorHeightMm, Look = s.DoorLook,
    };

    public static Opening NewDoorway(BuildingSpec s, double x, double y) => NewDoor(s, x, y) with { Kind = OpeningKind.Doorway };

    public static Opening NewWindow(BuildingSpec s, double x, double y) => new()
    {
        Kind = OpeningKind.Window, X = x, Y = y, Shape = s.WindowShape, WidthMm = s.WindowWidthMm, HeightMm = s.WindowHeightMm,
        SillMm = s.SillMm, Bars = s.Bars, Fill = s.WindowFill,
    };
}

/// <summary>One floor of the building: its walls, what's in them and the floor laid outside its rooms (a balcony or a deck).</summary>
public sealed record Level
{
    public List<WallEdge> Walls { get; init; } = [];
    public List<Opening> Openings { get; init; } = [];
    /// <summary>Squares of floor laid by hand, each by its top-left corner (the rooms get a floor anyway).</summary>
    public List<GridPt> Floor { get; init; } = [];
}

/// <summary>
/// A building drawn by hand, floor by floor, on a grid of squares: walls along the grid lines and
/// corner to corner, the doors and windows dropped in them, and the floor. <see cref="Style"/> holds
/// the look and the sizes (the whole-building kits' settings: textures, sheet, box or flat walls, storey
/// height, figures); its own width, depth, storeys, windows and furniture aren't used. Saved on the
/// shapes it makes, so it can be opened and changed.
/// </summary>
public sealed record HouseDesign
{
    public BuildingSpec Style { get; init; } = DefaultStyle();
    /// <summary>The grid's square (mm): walls run along its lines.</summary>
    public double SquareMm { get; init; } = 25.4;
    public List<Level> Levels { get; init; } = [new()];

    /// <summary>The look the editor starts with: the cottage's box walls, without its roof (roofs come separately).</summary>
    public static BuildingSpec DefaultStyle() => BuildingSpec.Preset(BuildingKind.Cottage) with { Items = [] };

    /// <summary>A small house to start from: two rooms, a door and three windows.</summary>
    public static HouseDesign Starter()
    {
        var style = DefaultStyle();
        var walls = new List<WallEdge>();
        void Line(int x0, int y0, int x1, int y1)
        {
            int dx = Math.Sign(x1 - x0), dy = Math.Sign(y1 - y0);
            for (var p = new GridPt(x0, y0); p != new GridPt(x1, y1); p += new GridPt(dx, dy)) walls.Add(WallEdge.Of(p, p + new GridPt(dx, dy)));
        }
        Line(2, 2, 7, 2); Line(7, 2, 7, 6); Line(7, 6, 2, 6); Line(2, 6, 2, 2); Line(5, 2, 5, 6);
        var level = new Level
        {
            Walls = walls,
            Openings =
            [
                Opening.NewDoor(style, 3.5, 6),
                Opening.NewDoorway(style, 5, 4.5),
                Opening.NewWindow(style, 6, 6),
                Opening.NewWindow(style, 3.5, 2),
                Opening.NewWindow(style, 7, 4),
            ],
        };
        return new HouseDesign { Style = style, SquareMm = style.SquareMm, Levels = [level] }.Moved(10, 10);
    }

    /// <summary>
    /// A two-storey house to show what the editor does: a ground floor of two rooms with a corner cut off
    /// corner to corner, and a smaller room upstairs with a door out onto the roof of the room below.
    /// </summary>
    public static HouseDesign Sample()
    {
        var style = DefaultStyle() with { UpperTexture = "Timber frame" };
        List<WallEdge> Lines(params (int X, int Y)[] pts)
        {
            var list = new List<WallEdge>();
            for (int i = 0; i + 1 < pts.Length; i++)
            {
                var a = new GridPt(pts[i].X, pts[i].Y);
                var b = new GridPt(pts[i + 1].X, pts[i + 1].Y);
                int dx = Math.Sign(b.X - a.X), dy = Math.Sign(b.Y - a.Y);
                for (var p = a; p != b; p += new GridPt(dx, dy)) list.Add(WallEdge.Of(p, p + new GridPt(dx, dy)));
            }
            return list;
        }
        var ground = new Level
        {
            Walls = [.. Lines((2, 2), (9, 2), (9, 5), (7, 7), (2, 7), (2, 2)), .. Lines((5, 2), (5, 7))],
            Openings =
            [
                Opening.NewDoor(style, 3.5, 7),
                Opening.NewDoorway(style, 5, 5.5),
                Opening.NewWindow(style, 2, 4.5),
                Opening.NewWindow(style, 3.5, 2),
                Opening.NewWindow(style, 7, 2),
                Opening.NewWindow(style, 8, 6),
                Opening.NewWindow(style, 9, 3.5),
                Opening.NewWindow(style, 6, 7),
            ],
        };
        var upstairs = new Level
        {
            Walls = Lines((2, 2), (6, 2), (6, 7), (2, 7), (2, 2)),
            Openings =
            [
                Opening.NewDoor(style, 6, 4.5),
                Opening.NewWindow(style, 4, 2),
                Opening.NewWindow(style, 2, 4.5),
                Opening.NewWindow(style, 4, 7),
            ],
        };
        return new HouseDesign { Style = style, SquareMm = style.SquareMm, Levels = [ground, upstairs] }.Moved(10, 10);
    }

    /// <summary>The whole building moved across the grid by whole squares.</summary>
    public HouseDesign Moved(int dx, int dy)
    {
        var d = new GridPt(dx, dy);
        return this with
        {
            Levels = [.. Levels.Select(l => l with
            {
                Walls = [.. l.Walls.Select(w => new WallEdge(w.A + d, w.B + d))],
                Openings = [.. l.Openings.Select(o => o with { X = o.X + dx, Y = o.Y + dy })],
                Floor = [.. l.Floor.Select(f => f + d)],
            })],
        };
    }

    public string ToJson() => JsonSerializer.Serialize(this, HouseJson.Options);

    public static HouseDesign? FromJson(string json)
    {
        try { return JsonSerializer.Deserialize<HouseDesign>(json, HouseJson.Options); }
        catch (Exception e) when (e is JsonException or NotSupportedException or ArgumentException) { return null; }
    }
}

static class HouseJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        Converters = { new JsonStringEnumConverter() },
    };
}
