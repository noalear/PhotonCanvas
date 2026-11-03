using Clipper2Lib;
using Geo = PhotonCanvas.Addon.Walls.Geo;

namespace PhotonCanvas.Addon.HouseEditor;

/// <summary>A straight stretch of wall between two columns: the grid points it runs through, first to last.</summary>
public sealed record Run(List<GridPt> Points)
{
    public GridPt A => Points[0];
    public GridPt B => Points[^1];
    public GridPt Step => Points[1] - Points[0];
    public bool Diagonal => Step.X != 0 && Step.Y != 0;
    /// <summary>Squares along it (each diagonal one is √2 squares long).</summary>
    public int Steps => Points.Count - 1;
    public Run Reversed => new([.. Enumerable.Reverse(Points)]);
}

/// <summary>A room: the floor walls go all the way round, as triangles of squares (quarters cut corner to corner).</summary>
public sealed record Room(int Index, PathsD Area, double AreaSquares);

/// <summary>
/// What a floor's walls make: the straight runs between columns, the columns (every corner, end and
/// junction) with the directions walls leave them in, and the rooms they close in. Plan coordinates are
/// squares (x right, y towards you).
/// </summary>
public sealed class PlanShape
{
    public List<Run> Runs { get; } = [];
    /// <summary>Every column and the directions (unit steps) walls leave it in.</summary>
    public Dictionary<GridPt, List<GridPt>> Joints { get; } = [];
    public List<Room> Rooms { get; } = [];
    /// <summary>Which room each quarter square is in (−1: outside), by <see cref="Quarter"/>.</summary>
    readonly Dictionary<(int X, int Y, int Q), int> _roomOf = [];
    int _x0, _y0, _x1, _y1;

    /// <summary>The quarters of a square, cut corner to corner: its top, right, bottom and left triangles.</summary>
    public const int Top = 0, Right = 1, Bottom = 2, Left = 3;

    public static readonly GridPt[] Directions = [new(1, 0), new(1, 1), new(0, 1), new(-1, 1), new(-1, 0), new(-1, -1), new(0, -1), new(1, -1)];

    /// <summary>The room a quarter square is in, or −1 when it's outside every room.</summary>
    public int RoomAt(int x, int y, int q) => _roomOf.TryGetValue((x, y, q), out var r) ? r : -1;

    /// <summary>The room at a point (squares), or −1.</summary>
    public int RoomAt(double x, double y)
    {
        int cx = (int)Math.Floor(x), cy = (int)Math.Floor(y);
        double u = x - cx - 0.5, v = y - cy - 0.5;
        int q = Math.Abs(u) > Math.Abs(v) ? (u > 0 ? Right : Left) : (v > 0 ? Bottom : Top);
        return RoomAt(cx, cy, q);
    }

    /// <summary>
    /// The plan of a floor's walls. <paramref name="splits"/> are grid points where a run must be cut
    /// even though the wall goes straight on (a straight column), so two doors can each have a wall.
    /// </summary>
    public static PlanShape Of(IEnumerable<WallEdge> walls, ISet<GridPt>? splits = null)
    {
        var p = new PlanShape();
        var edges = walls.Distinct().ToHashSet();
        var at = new Dictionary<GridPt, List<GridPt>>();
        foreach (var e in edges)
        {
            (at.TryGetValue(e.A, out var a) ? a : at[e.A] = []).Add(e.B - e.A);
            (at.TryGetValue(e.B, out var b) ? b : at[e.B] = []).Add(e.A - e.B);
        }
        bool IsJoint(GridPt g) => !at.TryGetValue(g, out var d) || d.Count != 2 || d[0] + d[1] != default || (splits?.Contains(g) ?? false);
        foreach (var (g, d) in at)
            if (IsJoint(g)) p.Joints[g] = [.. d.OrderBy(Angle)];

        // Each run from a joint, along each way out of it, to the next joint.
        var used = new HashSet<WallEdge>();
        foreach (var (g, dirs) in p.Joints)
            foreach (var d in dirs)
            {
                if (used.Contains(WallEdge.Of(g, g + d))) continue;
                var pts = new List<GridPt> { g };
                var at0 = g;
                while (true)
                {
                    var next = at0 + d;
                    used.Add(WallEdge.Of(at0, next));
                    pts.Add(next);
                    at0 = next;
                    if (IsJoint(next)) break;
                }
                p.Runs.Add(new Run(pts));
            }
        // Loops with no joint at all can't happen on a grid (a closed loop has to turn somewhere).
        p.FindRooms(edges);
        return p;
    }

    /// <summary>A direction's angle, anticlockwise seen from above (degrees, 0 = right): the plan's y runs towards you.</summary>
    public static double Angle(GridPt d) => (Math.Atan2(-d.Y, d.X) * 180 / Math.PI + 360) % 360;

    void FindRooms(HashSet<WallEdge> edges)
    {
        if (edges.Count == 0) return;
        _x0 = edges.Min(e => Math.Min(e.A.X, e.B.X)) - 1;
        _y0 = edges.Min(e => Math.Min(e.A.Y, e.B.Y)) - 1;
        _x1 = edges.Max(e => Math.Max(e.A.X, e.B.X)) + 1;
        _y1 = edges.Max(e => Math.Max(e.A.Y, e.B.Y)) + 1;
        bool Wall(int ax, int ay, int bx, int by) => edges.Contains(WallEdge.Of(new(ax, ay), new(bx, by)));
        var seen = new Dictionary<(int, int, int), int>();
        var rooms = new List<List<(int X, int Y, int Q)>>();
        var outside = new List<bool>();
        for (int y = _y0; y < _y1; y++)
            for (int x = _x0; x < _x1; x++)
                for (int q = 0; q < 4; q++)
                {
                    if (seen.ContainsKey((x, y, q))) continue;
                    int id = rooms.Count;
                    var members = new List<(int, int, int)>();
                    bool open = false;
                    var stack = new Stack<(int X, int Y, int Q)>();
                    stack.Push((x, y, q));
                    seen[(x, y, q)] = id;
                    while (stack.Count > 0)
                    {
                        var (cx, cy, cq) = stack.Pop();
                        members.Add((cx, cy, cq));
                        if (cx == _x0 || cy == _y0 || cx == _x1 - 1 || cy == _y1 - 1) open = true;
                        foreach (var n in Neighbours(cx, cy, cq, Wall))
                        {
                            if (n.X < _x0 || n.Y < _y0 || n.X >= _x1 || n.Y >= _y1) { open = true; continue; }
                            if (seen.ContainsKey(n)) continue;
                            seen[n] = id;
                            stack.Push(n);
                        }
                    }
                    rooms.Add(members);
                    outside.Add(open);
                }
        int index = 0;
        for (int i = 0; i < rooms.Count; i++)
        {
            if (outside[i]) continue;
            var tris = new PathsD(rooms[i].Select(t => Triangle(t.X, t.Y, t.Q)));
            foreach (var t in rooms[i]) _roomOf[t] = index;
            Rooms.Add(new Room(index, Geo.Union(tris), rooms[i].Count / 4.0));
            index++;
        }
    }

    /// <summary>The quarter squares next to one, past any wall in the way.</summary>
    static IEnumerable<(int X, int Y, int Q)> Neighbours(int x, int y, int q, Func<int, int, int, int, bool> wall)
    {
        // Corner to corner: the main diagonal (top-left to bottom-right) and the other one.
        bool main = wall(x, y, x + 1, y + 1), anti = wall(x + 1, y, x, y + 1);
        switch (q)
        {
            case Top:
                if (!wall(x, y, x + 1, y)) yield return (x, y - 1, Bottom);
                if (!anti) yield return (x, y, Right);
                if (!main) yield return (x, y, Left);
                break;
            case Right:
                if (!wall(x + 1, y, x + 1, y + 1)) yield return (x + 1, y, Left);
                if (!anti) yield return (x, y, Top);
                if (!main) yield return (x, y, Bottom);
                break;
            case Bottom:
                if (!wall(x, y + 1, x + 1, y + 1)) yield return (x, y + 1, Top);
                if (!anti) yield return (x, y, Left);
                if (!main) yield return (x, y, Right);
                break;
            default:
                if (!wall(x, y, x, y + 1)) yield return (x - 1, y, Right);
                if (!anti) yield return (x, y, Bottom);
                if (!main) yield return (x, y, Top);
                break;
        }
    }

    /// <summary>A quarter square, in squares.</summary>
    public static PathD Triangle(int x, int y, int q)
    {
        var c = new PointD(x + 0.5, y + 0.5);
        return q switch
        {
            Top => [new(x, y), new(x + 1, y), c],
            Right => [new(x + 1, y), new(x + 1, y + 1), c],
            Bottom => [new(x + 1, y + 1), new(x, y + 1), c],
            _ => [new(x, y + 1), new(x, y), c],
        };
    }

    /// <summary>
    /// The room on each side of one square's wall, walking from a to b on the plan as it's drawn (y down
    /// the screen): (left, right). −1 where it's outside.
    /// </summary>
    public (int Left, int Right) Sides(GridPt a, GridPt b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y, len = Math.Sqrt(dx * dx + dy * dy);
        double mx = (a.X + b.X) / 2.0 + 0.2 * dx, my = (a.Y + b.Y) / 2.0 + 0.2 * dy;
        // Left on the screen is the direction turned a quarter anticlockwise: (dy, −dx).
        double nx = dy / len * 0.1, ny = -dx / len * 0.1;
        return (RoomAt(mx + nx, my + ny), RoomAt(mx - nx, my - ny));
    }
}
