using Clipper2Lib;
using PhotonCanvas.Addon.Walls;
using Geo = PhotonCanvas.Addon.Walls.Geo;

namespace PhotonCanvas.Addon.Buildings;

/// <summary>
/// The shape of a building on the ground: a rectangle; an L, a T or a U of wings one wing wide; or a
/// regular hexagon, octagon or many-sided round building; or any shape drawn round its corners (<see cref="Footprint.FromPoints"/>).
/// </summary>
public enum BuildingShape { Rectangle, LShape, TShape, UShape, Hexagon, Octagon, Round, Custom }

/// <summary>
/// One wall of a footprint, from one corner column's centre to the next, on the plan (mm: x to the right,
/// y away from you). Walking from <see cref="From"/> to <see cref="To"/>, the inside of the building is on the left.
/// </summary>
public sealed record FootprintWall(int Index, PointD From, PointD To)
{
    public double Length => Math.Sqrt((To.x - From.x) * (To.x - From.x) + (To.y - From.y) * (To.y - From.y));

    /// <summary>Along the wall, from its start to its end (a unit vector on the plan).</summary>
    public PointD Dir => new((To.x - From.x) / Math.Max(1e-12, Length), (To.y - From.y) / Math.Max(1e-12, Length));

    /// <summary>The way the wall's outside faces (a unit vector on the plan).</summary>
    public PointD Normal => new(Dir.y, -Dir.x);

    public PointD Middle => new((From.x + To.x) / 2, (From.y + To.y) / 2);

    /// <summary>Which side of the building it's on, to the nearest: front (towards you), back, left or right.</summary>
    public WallSide Facing => Math.Abs(Normal.y) >= Math.Abs(Normal.x) - 1e-6 ? (Normal.y < 0 ? WallSide.Front : WallSide.Back) : (Normal.x > 0 ? WallSide.Right : WallSide.Left);
}

/// <summary>
/// One corner of a footprint, where a corner column stands. <see cref="InsideDeg"/> is the angle inside the
/// building (90 at a rectangle's corners, 270 at an L's inner corner, 135 round an octagon). The column's two
/// slots point <see cref="YawDeg"/> (anticlockwise from the plan's x, seen from above) and <see cref="SlotDeg"/>
/// further round, so a column of the wall maker's turned by <see cref="YawDeg"/> takes both walls.
/// </summary>
public sealed record FootprintCorner(int Index, PointD At, double InsideDeg, double YawDeg, double SlotDeg)
{
    /// <summary>The directions its walls go in (degrees, from its first slot).</summary>
    public double[] Slots => [0, SlotDeg];

    /// <summary>The wall maker's kind of column for these slots (<see cref="ConnectorKind.Custom"/> with <see cref="SlotDeg"/> for other angles).</summary>
    public ConnectorKind Kind => Near(SlotDeg, 90) ? ConnectorKind.Corner : Near(SlotDeg, 135) ? ConnectorKind.Angle135
        : Near(SlotDeg, 45) ? ConnectorKind.Sharp45 : Near(SlotDeg, 180) ? ConnectorKind.Straight : ConnectorKind.Custom;

    static bool Near(double a, double b) => Math.Abs(a - b) < 1e-6;
}

/// <summary>
/// A rectangular wing of the building, between its walls' centre lines on the plan. The first wing of an L,
/// T or U runs along the back with its ridge left to right; the others run forward from it, their ridges
/// front to back, joining it at their back end (+y).
/// </summary>
public sealed record Wing(double X0, double Y0, double X1, double Y1, bool AlongX);

/// <summary>
/// A building's footprint: the corner columns' centres, anticlockwise on the plan seen from above (x to the
/// right, y away from you; in 3D, x stays, z = −y), the walls between them, the corners and which wall has
/// the front door. Made from a <see cref="BuildingShape"/> and sizes, or from any list of points (for a
/// building drawn by hand); everything that builds walls, columns, floors and roofs works from it.
/// </summary>
public sealed class Footprint
{
    public BuildingShape Shape { get; }
    public IReadOnlyList<PointD> Points { get; }
    public IReadOnlyList<FootprintWall> Walls { get; }
    public IReadOnlyList<FootprintCorner> Corners { get; }
    /// <summary>The wall with the front door in it.</summary>
    public int DoorWall { get; }
    /// <summary>The wings, for a pitched roof over each (the first along the back); empty when it isn't made of rectangles.</summary>
    public IReadOnlyList<Wing> Wings { get; }
    /// <summary>Sides of a regular footprint (6 for a hexagon, 8 for an octagon), 0 otherwise.</summary>
    public int Sides { get; }
    /// <summary>A regular footprint's middle to each wall's centre line (mm), 0 otherwise.</summary>
    public double Apothem { get; }

    Footprint(BuildingShape shape, IReadOnlyList<PointD> points, int doorWall, IReadOnlyList<Wing> wings, int sides, double apothem)
    {
        Shape = shape;
        Points = points;
        Walls = [.. points.Select((p, i) => new FootprintWall(i, p, points[(i + 1) % points.Count]))];
        Corners = [.. points.Select((p, i) => MakeCorner(i, points[(i + points.Count - 1) % points.Count], p, points[(i + 1) % points.Count]))];
        DoorWall = Math.Clamp(doorWall, 0, Math.Max(0, points.Count - 1));
        Wings = wings;
        Sides = sides;
        Apothem = apothem;
    }

    public bool IsRectangle => Shape == BuildingShape.Rectangle;
    public bool IsRegular => Sides > 0;
    /// <summary>An L, T or U: rectangles joined, under a cross-gabled roof.</summary>
    public bool IsWinged => Shape is BuildingShape.LShape or BuildingShape.TShape or BuildingShape.UShape;

    public RectD Bounds => Clipper.GetBounds(new PathsD { new PathD(Points) });

    /// <summary>The centre lines' outline grown outwards by <paramref name="grow"/> (mm; negative shrinks it), with mitred corners.</summary>
    public PathsD Outline(double grow = 0) => Geo.Offset([new PathD(Points)], grow);

    /// <summary>From the plan (x, y) and a height to the building's 3D space (x right, y up, z towards you).</summary>
    public static Furniture.V3 To3D(PointD p, double height) => new(p.x, height, -p.y);

    /// <summary>A direction on the plan in the building's 3D space.</summary>
    public static Furniture.V3 Dir3D(PointD d) => new(d.x, 0, -d.y);

    static double Angle(PointD from, PointD to) => Math.Atan2(to.y - from.y, to.x - from.x) * 180 / Math.PI;

    static double Wrap(double deg) => ((deg % 360) + 360) % 360;

    static FootprintCorner MakeCorner(int i, PointD prev, PointD at, PointD next)
    {
        double toNext = Angle(at, next), toPrev = Angle(at, prev);
        double inside = Wrap(toPrev - toNext);
        if (inside < 1e-9) inside = 360;
        return inside <= 180 + 1e-9
            ? new FootprintCorner(i, at, inside, Wrap(toNext), Math.Round(inside, 9))
            : new FootprintCorner(i, at, inside, Wrap(toPrev), Math.Round(360 - inside, 9));
    }

    /// <summary>The footprint of a building's settings.</summary>
    public static Footprint Of(BuildingSpec s) => Of(s.Shape, s.WidthMm, s.DepthMm, s.WingMm, s.Sides);

    /// <summary>
    /// A footprint of a shape: <paramref name="width"/> × <paramref name="depth"/> overall for the rectangle, L, T and U
    /// (their wings <paramref name="wing"/> wide), <paramref name="width"/> across the flats for the regular ones.
    /// All between the corner columns' centres, centred on the plan's origin.
    /// </summary>
    public static Footprint Of(BuildingShape shape, double width, double depth, double wing, int sides = 12)
    {
        double W = width, D = depth, a = wing, x0 = -W / 2, x1 = W / 2, y0 = -D / 2, y1 = D / 2;
        switch (shape)
        {
            case BuildingShape.LShape:
                return new(shape, P((x0, y0), (x0 + a, y0), (x0 + a, y1 - a), (x1, y1 - a), (x1, y1), (x0, y1)), 2,
                    [new(x0, y1 - a, x1, y1, true), new(x0, y0, x0 + a, y1 - a, false)], 0, 0);
            case BuildingShape.TShape:
                return new(shape, P((-a / 2, y0), (a / 2, y0), (a / 2, y1 - a), (x1, y1 - a), (x1, y1), (x0, y1), (x0, y1 - a), (-a / 2, y1 - a)), 0,
                    [new(x0, y1 - a, x1, y1, true), new(-a / 2, y0, a / 2, y1 - a, false)], 0, 0);
            case BuildingShape.UShape:
                return new(shape, P((x0, y0), (x0 + a, y0), (x0 + a, y1 - a), (x1 - a, y1 - a), (x1 - a, y0), (x1, y0), (x1, y1), (x0, y1)), 2,
                    [new(x0, y1 - a, x1, y1, true), new(x0, y0, x0 + a, y1 - a, false), new(x1 - a, y0, x1, y1 - a, false)], 0, 0);
            case BuildingShape.Hexagon:
            case BuildingShape.Octagon:
            case BuildingShape.Round:
            {
                int n = shape == BuildingShape.Hexagon ? 6 : shape == BuildingShape.Octagon ? 8 : RoundSides(sides);
                double ap = W / 2, r = ap / Math.Cos(Math.PI / n);
                // The first wall faces you: it runs left to right across the front.
                var pts = Enumerable.Range(0, n).Select(i =>
                {
                    double t = (-90 - 180.0 / n + i * 360.0 / n) * Math.PI / 180;
                    return new PointD(Clean(r * Math.Cos(t)), Clean(r * Math.Sin(t)));
                }).ToList();
                return new(shape, pts, 0, [], n, ap);
            }
            default:
                return new(BuildingShape.Rectangle, P((x0, y0), (x1, y0), (x1, y1), (x0, y1)), 0, [new(x0, y0, x1, y1, W >= D)], 0, 0);
        }
    }

    /// <summary>A round building's sides: an even number from 6 to 24.</summary>
    public static int RoundSides(int sides) => Math.Clamp(sides + (sides % 2), 6, 24);

    static double Clean(double v) => Math.Abs(v) < 1e-9 ? 0 : Math.Round(v, 9);

    static List<PointD> P(params (double X, double Y)[] pts) => [.. pts.Select(p => new PointD(p.X, p.Y))];

    /// <summary>
    /// A footprint through any points (the corner columns' centres, in order round the building, either way
    /// round), moved so its bounds are centred on the plan's origin. The front door goes in <paramref name="doorWall"/> (counted from the wall that starts at the first
    /// point), or else in the longest wall facing you.
    /// </summary>
    public static Footprint FromPoints(IEnumerable<PointD> points, int? doorWall = null)
    {
        var pts = points.ToList();
        bool flipped = Clipper.Area(new PathD(pts)) < 0;
        if (flipped) pts.Reverse();
        // Centred on the plan's origin, as the shapes are.
        if (pts.Count > 0)
        {
            var b = Clipper.GetBounds(new PathD(pts));
            double cx = (b.left + b.right) / 2, cy = (b.top + b.bottom) / 2;
            pts = [.. pts.Select(p => new PointD(Clean(p.x - cx), Clean(p.y - cy)))];
        }
        var made = new Footprint(BuildingShape.Rectangle, pts, 0, [], 0, 0);
        int door;
        if (doorWall is { } d && pts.Count > 0)
        {
            int n = pts.Count;
            // Reversing the points turns wall i (from point i to i + 1) into the wall from point n − 1 − i.
            door = flipped ? ((n - 2 - d) % n + n) % n : ((d % n) + n) % n;
        }
        else door = made.Walls.OrderByDescending(w => w.Facing == WallSide.Front).ThenByDescending(w => w.Length).FirstOrDefault()?.Index ?? 0;
        bool rect = pts.Count == 4 && made.Corners.All(c => Math.Abs(c.InsideDeg - 90) < 1e-6) && made.Walls.All(w => Math.Abs(w.Dir.x) < 1e-9 || Math.Abs(w.Dir.y) < 1e-9);
        return new(rect ? BuildingShape.Rectangle : BuildingShape.Custom, pts, door, rect ? [RectWing(pts)] : [], 0, 0);
    }

    static Wing RectWing(List<PointD> pts)
    {
        var b = Clipper.GetBounds(new PathD(pts));
        return new(b.left, b.top, b.right, b.bottom, b.right - b.left >= b.bottom - b.top);
    }

    /// <summary>What's wrong with the shape's sizes for its corner columns (null if nothing).</summary>
    public static string? Problem(BuildingSpec s)
    {
        double W = s.WidthMm, D = s.DepthMm, a = s.WingMm, need = s.ColumnMm + 20;
        switch (s.Shape)
        {
            case BuildingShape.LShape:
            case BuildingShape.TShape:
            case BuildingShape.UShape:
            {
                if (a < Math.Max(30, need)) return $"The wings are too narrow: make them at least {Math.Ceiling(Math.Max(30, need)):0} mm wide.";
                double room = s.Shape switch { BuildingShape.LShape => W - a, BuildingShape.TShape => (W - a) / 2, _ => W - 2 * a };
                if (room < need) return $"The wings are too wide for the building: make them narrower or the building wider (the {(s.Shape == BuildingShape.UShape ? "courtyard" : "wing along the back")} needs at least {Math.Ceiling(need):0} mm).";
                if (D - a < need) return $"The wings are too wide for the building: make them narrower or the building deeper (the wing{(s.Shape == BuildingShape.UShape ? "s" : "")} at the front need{(s.Shape == BuildingShape.UShape ? "" : "s")} at least {Math.Ceiling(need):0} mm).";
                return null;
            }
            case BuildingShape.Hexagon:
            case BuildingShape.Octagon:
            case BuildingShape.Round:
            {
                var f = Of(s);
                double side = f.Walls[0].Length;
                if (side < s.ColumnMm + 10)
                    return $"Each wall is only {side:0.#} mm between its columns' centres: make the building wider than {Math.Ceiling((s.ColumnMm + 10) / Math.Tan(Math.PI / f.Sides)):0} mm{(s.Shape == BuildingShape.Round ? ", give it fewer sides" : "")} or make the columns thinner.";
                return null;
            }
            default: return null;
        }
    }

    /// <summary>
    /// The biggest rectangle (square to the plan) inside <paramref name="region"/>, found on a grid of
    /// <paramref name="cell"/> mm. Null if there's none.
    /// </summary>
    public static RectD? LargestRectangle(PathsD region, double cell = 1)
    {
        if (region.Count == 0) return null;
        var b = Clipper.GetBounds(region);
        int nx = (int)Math.Floor((b.right - b.left) / cell), ny = (int)Math.Floor((b.bottom - b.top) / cell);
        if (nx < 1 || ny < 1) return null;
        // A cell counts if the whole of it is inside: all four corners in, and no edge of the region crossing it.
        var shrunk = Geo.Offset(region, -cell * 0.75);
        var heights = new int[nx];
        double best = 0;
        RectD? found = null;
        for (int j = 0; j < ny; j++)
        {
            double y = b.top + (j + 0.5) * cell;
            for (int i = 0; i < nx; i++)
            {
                double x = b.left + (i + 0.5) * cell;
                heights[i] = Inside(shrunk, x, y) ? heights[i] + 1 : 0;
            }
            // The biggest rectangle under this row's histogram.
            var stack = new Stack<int>();
            for (int i = 0; i <= nx; i++)
            {
                int h = i == nx ? 0 : heights[i];
                while (stack.Count > 0 && heights[stack.Peek()] >= h)
                {
                    int top = stack.Pop(), height = heights[top], left = stack.Count == 0 ? 0 : stack.Peek() + 1;
                    double area = (double)height * (i - left);
                    if (height > 0 && area > best + 1e-9)
                    {
                        best = area;
                        found = new RectD(b.left + left * cell, b.top + (j + 1 - height) * cell, b.left + i * cell, b.top + (j + 1) * cell);
                    }
                }
                stack.Push(i);
            }
        }
        return found;
    }

    static bool Inside(PathsD region, double x, double y)
    {
        int count = 0;
        foreach (var path in region)
            if (Clipper.PointInPolygon(new PointD(x, y), path) != PointInPolygonResult.IsOutside) count += Clipper.IsPositive(path) ? 1 : -1;
        return count > 0;
    }
}
