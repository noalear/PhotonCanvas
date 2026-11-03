using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using PhotonCanvas.Addon.Buildings;
using PhotonCanvas.Addon.Walls;
using GableWindow = PhotonCanvas.Addon.Roofs.GableWindow;
using PhotonCanvas.Addon.Furniture;
using HingeSide = PhotonCanvas.Addon.Walls.HingeSide;

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
    /// <summary>
    /// A door in box walls hangs on pins inside the wall and swings open; an arched or pointed one then has a
    /// fixed panel over it (the leaf is square-topped). Otherwise it's glued in, filling the whole opening.
    /// </summary>
    public bool Opens { get; init; }
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

/// <summary>A room's own floor pattern: the room is the one round the point (<see cref="X"/>, <see cref="Y"/>), in squares.</summary>
public sealed record RoomFloor
{
    public double X { get; init; }
    public double Y { get; init; }
    /// <summary>A texture: one of the floor tile maker's (flagstones, floorboards, chequered tiles…).</summary>
    public string Texture { get; init; } = "Floorboards";
}

/// <summary>One floor of the building: its walls, what's in them and the floor laid outside its rooms (a balcony or a deck).</summary>
public sealed record Level
{
    public List<WallEdge> Walls { get; init; } = [];
    public List<Opening> Openings { get; init; } = [];
    /// <summary>Squares of floor laid by hand, each by its top-left corner (the rooms get a floor anyway).</summary>
    public List<GridPt> Floor { get; init; } = [];
    /// <summary>
    /// How far (squares) this floor's floor reaches past its own rooms and walls: the ground floor's base all
    /// round, an upper floor's over the rooms below (the rest of them are left open, for a roof). None: as
    /// drawn (an upper floor covers everything below it, and floor laid by hand).
    /// </summary>
    public int? Yard { get; init; }
    /// <summary>Railings round the edges of this floor that have no wall on them (a balcony, a terrace).</summary>
    public bool Railings { get; init; } = true;
    /// <summary>Rooms with a floor pattern of their own.</summary>
    public List<RoomFloor> RoomFloors { get; init; } = [];
    /// <summary>The furniture standing on this floor.</summary>
    public List<PlacedItem> Furniture { get; init; } = [];
    /// <summary>Stairs and ladders from this floor up to the one above, each through an opening in its floor.</summary>
    public List<PlacedWay> WaysUp { get; init; } = [];
    /// <summary>Columns with a shape of their own (the rest take the building's).</summary>
    public List<ColumnPick> Columns { get; init; } = [];
    /// <summary>Squares of this floor's open edge whose railing was taken away.</summary>
    public List<WallEdge> NoRailing { get; init; } = [];
    /// <summary>Walls with a look of their own, one square of wall at a time.</summary>
    public List<WallLook> WallLooks { get; init; } = [];

    /// <summary>The shape a column picked for itself, or null when it's the building's.</summary>
    public ColumnPick? PickAt(GridPt at) => Columns.FirstOrDefault(c => c.At == at);

    public ColumnShape? ColumnAt(GridPt at)
    {
        foreach (var c in Columns) if (c.At == at) return c.Shape;
        return null;
    }

    /// <summary>The look a square of wall has of its own, or null when it's like the rest of the building.</summary>
    public WallLook? LookOf(WallEdge e)
    {
        foreach (var l in WallLooks) if (l.Edge == e) return l;
        return null;
    }

    /// <summary>
    /// The plan of this floor's walls, with a column wherever a wall's look changes along a straight line
    /// (each straight wall is cut from one texture).
    /// </summary>
    public PlanShape Shape()
    {
        if (WallLooks.Count == 0) return PlanShape.Of(Walls);
        var looks = new Dictionary<WallEdge, WallLook>();
        foreach (var l in WallLooks) looks[l.Edge] = l;
        var walls = Walls.ToHashSet();
        var splits = new HashSet<GridPt>();
        foreach (var e in walls)
        {
            // The next square on along the same line, if there's wall there.
            var next = WallEdge.Of(e.B, e.B + e.Step);
            if (!walls.Contains(next)) continue;
            looks.TryGetValue(e, out var a);
            looks.TryGetValue(next, out var b);
            if (!WallLook.Same(a, b)) splits.Add(e.B);
        }
        return PlanShape.Of(Walls, splits);
    }
}

/// <summary>
/// A piece of furniture (the furniture maker's) standing on a floor: the middle of it on the plan
/// (squares) and how many quarter turns it's turned (anticlockwise seen from above; 0 with its front
/// towards you, the bottom of the plan).
/// </summary>
public sealed record PlacedItem
{
    public double X { get; init; }
    public double Y { get; init; }
    public int Turn { get; init; }
    public FurnitureItem Item { get; init; } = Defaults.For(ItemKind.Table);
}

/// <summary>What climbs to the floor above: open stairs, solid stairs (stacked layers), spiral stairs or a leaning ladder.</summary>
public enum WayKind { Stairs, SolidStairs, Spiral, Ladder }

/// <summary>
/// A way up from a floor to the one above: the middle of it and its opening on the plan (squares), and
/// which way it climbs (0 towards the right of the plan, each step a quarter turn anticlockwise).
/// </summary>
public sealed record PlacedWay
{
    public double X { get; init; }
    public double Y { get; init; }
    public int Turn { get; init; }
    public WayKind Kind { get; init; } = WayKind.Stairs;
}

/// <summary>How a column is built: as usual (a box when it's square in box walls, otherwise stacked layers), a box whatever its shape, or stacked layers on a pin.</summary>
public enum ColumnMade { Usual, Boxed, Stacked }

/// <summary>A column's own shape, build and texture (null: the building's): the column at grid point <see cref="At"/> (squares).</summary>
public sealed record ColumnPick
{
    public GridPt At { get; init; }
    public ColumnShape? Shape { get; init; }
    public ColumnMade? Build { get; init; }
    /// <summary>The texture etched on a boxed column's sides (one of the wall maker's).</summary>
    public string? Texture { get; init; }
    [JsonIgnore] public bool Empty => Shape is null && Build is null && Texture is null;
}

/// <summary>
/// One square of wall's own look: its texture, how worn it is (0 crisp to 1 battered) and the size of
/// its smallest marks (mm, 0 = the texture's own). Every square of a straight wall gets the same look.
/// </summary>
public sealed record WallLook
{
    public WallEdge Edge { get; init; }
    public string Texture { get; init; } = "Fieldstone";
    public double Wear { get; init; } = 0.5;
    public double DetailMm { get; init; }
    /// <summary>
    /// The texture on the wall's other side (the inside of an outside wall), or null for the same as the front. A
    /// single-panel wall's back is engraved after the cut pieces are turned over; a box wall's back face just takes it.
    /// </summary>
    public string? BackTexture { get; init; }
    /// <summary>How worn the other side is (0 to 1).</summary>
    public double BackWear { get; init; } = 0.5;

    /// <summary>Whether two squares of wall look the same (null: like the rest of the building).</summary>
    public static bool Same(WallLook? a, WallLook? b) =>
        a is null ? b is null : b is not null && a.Texture == b.Texture && Math.Abs(a.Wear - b.Wear) < 1e-9 && Math.Abs(a.DetailMm - b.DetailMm) < 1e-9
            && a.BackTexture == b.BackTexture && (a.BackTexture is null || Math.Abs(a.BackWear - b.BackWear) < 1e-9);
}

/// <summary>A roof's shape: pitched with a gable at each end, hipped (sloping on all four sides), a lean-to sloping one way, or flat.</summary>
public enum RoofKind { Gable, Hip, LeanTo, Flat }

/// <summary>
/// A roof put over part of a floor: a rectangle of the plan between grid points, resting on the column
/// caps of the walls under it. <see cref="Turn"/> is which way it runs: 0 with the ridge left to right
/// (a lean-to sloping down towards you), each step a quarter turn anticlockwise seen from above.
/// </summary>
public sealed record RoofArea
{
    /// <summary>The floor it sits on top of (0 = the ground floor's walls).</summary>
    public int Level { get; init; }
    public int X0 { get; init; }
    public int Y0 { get; init; }
    public int X1 { get; init; }
    public int Y1 { get; init; }
    public RoofKind Kind { get; init; } = RoofKind.Gable;
    public int Turn { get; init; }
    public double PitchDeg { get; init; } = 45;
    /// <summary>What's on its slopes: one of the roof maker's coverings (slate, thatch, clay tiles…).</summary>
    public string Covering { get; init; } = "Slate";
    /// <summary>How far it reaches past the walls at the bottom of the slope (mm).</summary>
    public double EaveMm { get; init; } = 4;
    /// <summary>A window in each gable end (or a lean-to's sides).</summary>
    public GableWindow Window { get; init; } = GableWindow.None;

    /// <summary>The ridge runs left to right on the plan (otherwise up and down it).</summary>
    public bool RidgeAcross => Turn % 2 == 0;

    public static RoofArea Over(int level, GridPt a, GridPt b, BuildingSpec s)
    {
        int x0 = Math.Min(a.X, b.X), x1 = Math.Max(a.X, b.X), y0 = Math.Min(a.Y, b.Y), y1 = Math.Max(a.Y, b.Y);
        return new RoofArea
        {
            Level = level, X0 = x0, Y0 = y0, X1 = x1, Y1 = y1,
            Kind = s.Roof switch { RoofShape.Hip => RoofKind.Hip, RoofShape.LeanTo => RoofKind.LeanTo, _ => RoofKind.Gable },
            // The ridge along the longer side.
            Turn = x1 - x0 >= y1 - y0 ? 0 : 1,
            PitchDeg = s.RoofPitchDeg, Covering = s.RoofTexture, EaveMm = s.RoofEaveMm,
        };
    }
}

/// <summary>What the building stands on: the floor tile maker's jigsaw tiles, or one plate shaped to the building (cut in pieces only if it's bigger than the bed).</summary>
public enum BaseKind { Tiles, Plate }

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
    /// <summary>The roofs, each over a rectangle of one floor.</summary>
    public List<RoofArea> Roofs { get; init; } = [];
    /// <summary>What the building stands on (with <see cref="BuildingSpec.FloorTiles"/> on): jigsaw floor tiles, or one plate shaped to it.</summary>
    public BaseKind Base { get; init; } = BaseKind.Tiles;
    /// <summary>The pattern on the floors between storeys: one of the floor tile maker's.</summary>
    public string UpperFloor { get; init; } = "Floorboards";
    /// <summary>How the columns are built (a column can have its own).</summary>
    public ColumnMade ColumnBuild { get; init; } = ColumnMade.Usual;
    /// <summary>The texture on boxed columns' sides (null: the walls' own).</summary>
    public string? ColumnTexture { get; init; }
    /// <summary>How worn the walls' and floors' textures are, 0 (new and crisp) to 1 (ancient and battered); 0.5 is each texture's usual look.</summary>
    public double Wear { get; init; } = 0.5;
    /// <summary>The size of the textures' smallest marks (pits, nicks, grain, bolts) in mm; 0 = each texture's own.</summary>
    public double DetailMm { get; init; }
    /// <summary>Whether every piece has its number (the one in the building booklet) scored small on it.</summary>
    public bool NumberPieces { get; init; } = true;

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
    /// corner to corner, and a smaller room upstairs under a gable roof, with a door out onto a terrace with railings.
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
            RoomFloors = [new RoomFloor { X = 3.5, Y = 4.5, Texture = "Flagstones" }],
            Furniture =
            [
                new PlacedItem { X = 3.5, Y = 4, Item = HouseKitMaker.FromCatalog(style, FurnitureCatalog.Find("med-hall-rug")!) },
                new PlacedItem { X = 3.5, Y = 4, Item = HouseKitMaker.NewItem(style, ItemKind.Table) },
                new PlacedItem { X = 3.5, Y = 2.75, Item = HouseKitMaker.NewItem(style, ItemKind.Bench) },
                new PlacedItem { X = 5.9, Y = 2.6, Item = HouseKitMaker.NewItem(style, ItemKind.Chest) },
                new PlacedItem { X = 6.25, Y = 6.5, Item = HouseKitMaker.NewItem(style, ItemKind.Crate) },
            ],
            WaysUp = [new PlacedWay { X = 7.5, Y = 3.7, Turn = 1, Kind = WayKind.Stairs }],
            Openings =
            [
                Opening.NewDoor(style, 3.5, 7) with { Opens = true },
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
            Furniture =
            [
                new PlacedItem { X = 3.25, Y = 4.5, Turn = 1, Item = HouseKitMaker.NewItem(style, ItemKind.Bed) },
                new PlacedItem { X = 5.2, Y = 2.75, Turn = 2, Item = HouseKitMaker.NewItem(style, ItemKind.Bookshelf) },
            ],
            Openings =
            [
                Opening.NewDoor(style, 6, 4.5),
                Opening.NewWindow(style, 4, 2),
                Opening.NewWindow(style, 2, 4.5),
                Opening.NewWindow(style, 4, 7),
            ],
        };
        var roof = RoofArea.Over(1, new GridPt(2, 2), new GridPt(6, 7), style) with { Kind = RoofKind.Gable, Covering = "Slate", PitchDeg = 45, Window = GableWindow.Round };
        return new HouseDesign { Style = style, SquareMm = style.SquareMm, Levels = [ground, upstairs], Roofs = [roof] }.Moved(10, 10);
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
                RoomFloors = [.. l.RoomFloors.Select(r => r with { X = r.X + dx, Y = r.Y + dy })],
                WallLooks = [.. l.WallLooks.Select(w => w with { Edge = new WallEdge(w.Edge.A + d, w.Edge.B + d) })],
                NoRailing = [.. l.NoRailing.Select(w => new WallEdge(w.A + d, w.B + d))],
                Columns = [.. l.Columns.Select(c => c with { At = c.At + d })],
                Furniture = [.. l.Furniture.Select(f => f with { X = f.X + dx, Y = f.Y + dy })],
                WaysUp = [.. l.WaysUp.Select(w => w with { X = w.X + dx, Y = w.Y + dy })],
            })],
            Roofs = [.. Roofs.Select(r => r with { X0 = r.X0 + dx, X1 = r.X1 + dx, Y0 = r.Y0 + dy, Y1 = r.Y1 + dy })],
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
