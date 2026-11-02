using PhotonCanvas.Addon.Walls;

namespace PhotonCanvas.Addon.MapTerrain;

/// <summary>
/// One wall panel: it runs from grid corner (<see cref="X"/>, <see cref="Y"/>) along the row line
/// (<see cref="Across"/>) or down the column line for <see cref="Squares"/> squares, between two
/// columns. <see cref="Door"/> is which of its squares has the door (−1 for none) and
/// <see cref="DoorOffsetMm"/> where the door's middle is from the panel's middle. <see cref="FacesB"/>:
/// its textured face looks down (or right), into the square below (or to the right); otherwise up (left).
/// A <see cref="Narrow"/> door is one with no room for the width asked (a one-square wall between two
/// columns, where a corridor meets a room): it's as wide as fits between the columns
/// (<see cref="NarrowWidthMm"/>), with no frame round it, and sits loose in its doorway.
/// </summary>
public sealed record WallPanel(int X, int Y, bool Across, int Squares, int Door, double DoorOffsetMm, bool FacesB)
{
    public bool Narrow { get; init; }
    public double NarrowWidthMm { get; init; }

    public EdgeId Edge(int i) => Across ? new EdgeId(X + i, Y, true) : new EdgeId(X, Y + i, false);
    public (int X, int Y) End => Across ? (X + Squares, Y) : (X, Y + Squares);
}

/// <summary>
/// A column at grid corner (<see cref="X"/>, <see cref="Y"/>) with a slot for each wall that meets
/// there: <see cref="Kind"/> is its shape (end, straight, corner, T or cross) and <see cref="Turn"/> how
/// far it's turned (degrees, anticlockwise seen from above) from that kind's slots as the wall maker
/// makes them. Directions: 0 = to the right, 90 = up the plan, 180 = left, 270 = down.
/// </summary>
public sealed record WallColumn(int X, int Y, ConnectorKind Kind, double Turn, double[] Dirs);

/// <summary>Where the walls go on a plan: the panels, the columns, and the doors that had to be left as open doorways.</summary>
public sealed record MapLayout(List<WallPanel> Panels, List<WallColumn> Columns, List<EdgeId> OpenDoors, List<EdgeId> ShiftedDoors);

/// <summary>
/// How doors fit a panel so many squares long: how far the door's middle may be from the panel's middle
/// (mm; less than 0 = it doesn't fit), and, for one that doesn't, the widest door with no frame that
/// fits in the middle (mm; less than <see cref="MinNarrowMm"/> = none).
/// </summary>
public sealed record DoorFit(Func<int, double> Room, Func<int, double> Narrow)
{
    public const double MinNarrowMm = 6;
}

/// <summary>
/// Works out the walls of a plan: every edge with a wall or door on it, joined into straight runs
/// between the corners, T junctions, crossings and ends (each of those gets a column); runs longer
/// than the longest panel are split with straight columns, choosing the splits so that each panel has
/// at most one door and the doors keep away from the columns.
/// </summary>
public static class MapLayoutMaker
{
    /// <summary>
    /// The layout. <paramref name="doors"/> says how a door fits a panel so many squares long;
    /// <paramref name="squareMm"/> is the grid square.
    /// </summary>
    public static MapLayout Make(PlanGrid g, int longest, double squareMm, DoorFit doors)
    {
        longest = Math.Clamp(longest, 1, 40);
        var states = new Dictionary<EdgeId, EdgeState>();
        foreach (var e in g.Edges())
        {
            var st = g.State(e);
            if (st != EdgeState.None) states[e] = st;
        }
        var open = new List<EdgeId>();
        for (int round = 0; round < 50; round++)
        {
            var (panels, shifted, fail) = Panels(g, states, longest, squareMm, doors);
            if (fail.Count == 0) return new MapLayout(panels, Columns(states, panels), open, shifted);
            // A door with no room in its wall is left as an open doorway; the walls either side change, so look again.
            foreach (var e in fail) { states.Remove(e); open.Add(e); }
        }
        return new MapLayout([], [], open, []);
    }

    static IEnumerable<double> DirsAt(Dictionary<EdgeId, EdgeState> states, int x, int y)
    {
        if (states.ContainsKey(new EdgeId(x, y, true))) yield return 0;
        if (states.ContainsKey(new EdgeId(x, y - 1, false))) yield return 90;
        if (states.ContainsKey(new EdgeId(x - 1, y, true))) yield return 180;
        if (states.ContainsKey(new EdgeId(x, y, false))) yield return 270;
    }

    /// <summary>Does a run carry straight on through this corner (two walls in line, nothing else)?</summary>
    static bool Through(Dictionary<EdgeId, EdgeState> states, int x, int y, bool across)
    {
        var d = DirsAt(states, x, y).ToArray();
        return d.Length == 2 && (across ? d[0] == 0 && d[1] == 180 : d[0] == 90 && d[1] == 270);
    }

    static (List<WallPanel> Panels, List<EdgeId> Shifted, List<EdgeId> Fail) Panels(PlanGrid g, Dictionary<EdgeId, EdgeState> states, int longest, double sq, DoorFit doors)
    {
        var panels = new List<WallPanel>();
        var shifted = new List<EdgeId>();
        var fail = new List<EdgeId>();
        foreach (bool across in new[] { true, false })
        {
            int lines = across ? g.Down + 1 : g.Across + 1, along = across ? g.Across : g.Down;
            for (int line = 0; line < lines; line++)
            {
                int i = 0;
                while (i < along)
                {
                    EdgeId E(int k) => across ? new EdgeId(k, line, true) : new EdgeId(line, k, false);
                    if (!states.ContainsKey(E(i))) { i++; continue; }
                    // A run: on until the next corner that isn't straight through, or a gap.
                    int start = i;
                    i++;
                    while (i < along && states.ContainsKey(E(i)) && Through(states, across ? i : line, across ? line : i, across)) i++;
                    var run = Enumerable.Range(start, i - start).Select(E).ToList();
                    foreach (var p in Split(run, states, longest))
                    {
                        var first = run[p.From];
                        int n = p.To - p.From, door = -1;
                        for (int k = p.From; k < p.To; k++) if (states[run[k]] == EdgeState.Door) door = k - p.From;
                        double offset = 0, narrow = 0;
                        if (door >= 0)
                        {
                            double room = doors.Room(n);
                            double want = (door + 0.5) * sq - n * sq / 2;
                            if (room < 0)
                            {
                                // No room for the door asked: as wide as fits in the middle, or none at all.
                                narrow = doors.Narrow(n);
                                if (narrow < DoorFit.MinNarrowMm) { fail.Add(run[p.From + door]); continue; }
                                if (Math.Abs(want) > 0.05) shifted.Add(run[p.From + door]);
                            }
                            else
                            {
                                offset = Math.Clamp(want, -room, room);
                                if (Math.Abs(offset - want) > 0.05) shifted.Add(run[p.From + door]);
                            }
                        }
                        panels.Add(new WallPanel(first.X, first.Y, across, n, door, Math.Round(offset, 3), FacesB(g, run.Skip(p.From).Take(n))) { Narrow = narrow > 0, NarrowWidthMm = Math.Round(narrow, 2) });
                    }
                }
            }
        }
        return (panels, shifted, fail);
    }

    /// <summary>The textured face looks into the floor: down (or right) unless the floor is only above (or left).</summary>
    static bool FacesB(PlanGrid g, IEnumerable<EdgeId> edges)
    {
        int a = 0, b = 0;
        foreach (var e in edges)
        {
            var (sa, sb) = e.Sides;
            if (g[sa.X, sa.Y]) a++;
            if (g[sb.X, sb.Y]) b++;
        }
        return b >= a;
    }

    /// <summary>
    /// A run split into panels no longer than <paramref name="longest"/> squares, each with at most one
    /// door: as few panels as can be, about the same length, and with the doors kept off the panels'
    /// ends (next to a column) where possible.
    /// </summary>
    static List<(int From, int To)> Split(List<EdgeId> run, Dictionary<EdgeId, EdgeState> states, int longest)
    {
        int n = run.Count;
        double avg = (double)n / Math.Max(1, (int)Math.Ceiling((double)n / longest));
        var best = new double[n + 1];
        var from = new int[n + 1];
        Array.Fill(best, double.MaxValue);
        best[0] = 0;
        for (int j = 1; j <= n; j++)
            for (int i = Math.Max(0, j - longest); i < j; i++)
            {
                if (best[i] == double.MaxValue) continue;
                int doors = 0, at = -1;
                for (int k = i; k < j; k++) if (states[run[k]] == EdgeState.Door) { doors++; at = k; }
                if (doors > 1) continue;
                double cost = 10 + 0.5 * Math.Abs(j - i - avg);
                if (doors == 1) cost += j - i == 1 ? 1000 : at == i || at == j - 1 ? 2 : 0;
                if (best[i] + cost < best[j]) { best[j] = best[i] + cost; from[j] = i; }
            }
        var res = new List<(int, int)>();
        for (int j = n; j > 0; j = from[j]) res.Add((from[j], j));
        res.Reverse();
        return res;
    }

    /// <summary>A column at every end of every panel, with a slot for each wall that meets there.</summary>
    static List<WallColumn> Columns(Dictionary<EdgeId, EdgeState> states, List<WallPanel> panels)
    {
        var at = new HashSet<(int, int)>();
        foreach (var p in panels) { at.Add((p.X, p.Y)); at.Add(p.End); }
        var res = new List<WallColumn>();
        foreach (var (x, y) in at.OrderBy(v => v.Item2).ThenBy(v => v.Item1))
        {
            var dirs = DirsAt(states, x, y).ToArray();
            if (dirs.Length == 0) continue;
            var (kind, turn) = Shape(dirs);
            res.Add(new WallColumn(x, y, kind, turn, dirs));
        }
        return res;
    }

    /// <summary>The kind of column for slots in these directions, and how far the wall maker's one is turned to match.</summary>
    public static (ConnectorKind Kind, double Turn) Shape(double[] dirs)
    {
        var set = dirs.Select(d => ((int)Math.Round(d) % 360 + 360) % 360).ToHashSet();
        var kind = set.Count switch
        {
            1 => ConnectorKind.EndCap,
            2 => set.Contains((set.First() + 180) % 360) ? ConnectorKind.Straight : ConnectorKind.Corner,
            3 => ConnectorKind.Tee,
            _ => ConnectorKind.Cross,
        };
        var canon = Connectors.Directions(kind, 0).Select(d => (int)d).ToArray();
        foreach (int turn in new[] { 0, 90, 180, 270 })
            if (canon.Select(d => (d + turn) % 360).ToHashSet().SetEquals(set)) return (kind, turn);
        return (kind, 0);
    }
}
