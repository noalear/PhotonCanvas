namespace PhotonCanvas.Addon.MapTerrain;

/// <summary>
/// What an edge between two squares has been set to by hand. <see cref="Auto"/>: a wall wherever a
/// floor square meets one that isn't floor, nothing between two floor squares.
/// </summary>
public enum EdgeKind { Auto, Wall, Door, Open }

/// <summary>What actually stands on an edge once the marks and the floor are taken together.</summary>
public enum EdgeState { None, Wall, Door }

/// <summary>
/// An edge of the grid. <see cref="Across"/> edges run left to right along the top of square
/// (<see cref="X"/>, <see cref="Y"/>), between it and the square above; the others run down its left
/// side, between it and the square to its left. Rows are counted from the top, as on the screen.
/// </summary>
public readonly record struct EdgeId(int X, int Y, bool Across)
{
    /// <summary>The squares either side: above/left first, then below/right.</summary>
    public ((int X, int Y) A, (int X, int Y) B) Sides => Across ? ((X, Y - 1), (X, Y)) : ((X - 1, Y), (X, Y));

    /// <summary>Its two ends (grid corners).</summary>
    public ((int X, int Y) From, (int X, int Y) To) Ends => Across ? ((X, Y), (X + 1, Y)) : ((X, Y), (X, Y + 1));
}

/// <summary>An edge set by hand.</summary>
public sealed record EdgeMark
{
    public int X { get; init; }
    public int Y { get; init; }
    public bool Across { get; init; }
    public EdgeKind Kind { get; init; }
}

/// <summary>
/// A floor plan on a square grid: which squares are floor, and the edges set by hand (walls between
/// rooms, doors, gaps). Walls go round the floor by themselves. Saved with the design as text: a row of
/// <c>#</c> (floor) and <c>.</c> (not) for each row of squares.
/// </summary>
public sealed record MapPlan
{
    public const int MaxSide = 80;

    public int Across { get; init; } = 10;
    public int Down { get; init; } = 8;
    public List<string> Rows { get; init; } = [];
    public List<EdgeMark> Marks { get; init; } = [];

    /// <summary>An empty plan of this many squares.</summary>
    public static MapPlan Empty(int across, int down) => new PlanGrid(across, down).ToPlan();

    public PlanGrid Grid() => PlanGrid.From(this);

    /// <summary>A column's letters (A … Z, AA …) and a square's name ("C4").</summary>
    public static string Column(int i) => i < 26 ? ((char)('A' + i)).ToString() : Column(i / 26 - 1) + (char)('A' + i % 26);
    public static string Square(int x, int y) => $"{Column(x)}{y + 1}";

    /// <summary>An edge's name for a note: "between C4 and C5", or "on the top of C1" at the plan's edge.</summary>
    public static string Name(EdgeId e, int across, int down)
    {
        var (a, b) = e.Sides;
        bool aIn = a.X >= 0 && a.Y >= 0 && a.X < across && a.Y < down, bIn = b.X >= 0 && b.Y >= 0 && b.X < across && b.Y < down;
        if (aIn && bIn) return $"between {Square(a.X, a.Y)} and {Square(b.X, b.Y)}";
        if (bIn) return $"on the {(e.Across ? "top" : "left")} of {Square(b.X, b.Y)}";
        if (aIn) return $"on the {(e.Across ? "bottom" : "right")} of {Square(a.X, a.Y)}";
        return "outside the plan";
    }
}

/// <summary>A plan being worked on: the floor as a grid of flags and the marks by edge.</summary>
public sealed class PlanGrid
{
    public int Across { get; }
    public int Down { get; }
    readonly bool[] _floor;
    readonly Dictionary<EdgeId, EdgeKind> _marks = [];

    public PlanGrid(int across, int down)
    {
        Across = Math.Clamp(across, 1, MapPlan.MaxSide);
        Down = Math.Clamp(down, 1, MapPlan.MaxSide);
        _floor = new bool[Across * Down];
    }

    public static PlanGrid From(MapPlan p)
    {
        var g = new PlanGrid(p.Across, p.Down);
        for (int y = 0; y < g.Down && y < p.Rows.Count; y++)
        {
            var row = p.Rows[y] ?? "";
            for (int x = 0; x < g.Across && x < row.Length; x++) g[x, y] = row[x] == '#';
        }
        foreach (var m in p.Marks)
        {
            var e = new EdgeId(m.X, m.Y, m.Across);
            if (g.Valid(e) && m.Kind != EdgeKind.Auto) g._marks[e] = m.Kind;
        }
        return g;
    }

    public MapPlan ToPlan() => new()
    {
        Across = Across,
        Down = Down,
        Rows = [.. Enumerable.Range(0, Down).Select(y => new string([.. Enumerable.Range(0, Across).Select(x => this[x, y] ? '#' : '.')]))],
        // Only marks that change something are kept.
        Marks = [.. _marks.Where(kv => kv.Value != EdgeKind.Auto && Touches(kv.Key)).OrderBy(kv => kv.Key.Across).ThenBy(kv => kv.Key.Y).ThenBy(kv => kv.Key.X)
                         .Select(kv => new EdgeMark { X = kv.Key.X, Y = kv.Key.Y, Across = kv.Key.Across, Kind = kv.Value })],
    };

    public bool In(int x, int y) => x >= 0 && y >= 0 && x < Across && y < Down;

    /// <summary>Is the square floor? Outside the plan, never.</summary>
    public bool this[int x, int y]
    {
        get => In(x, y) && _floor[y * Across + x];
        set { if (In(x, y)) _floor[y * Across + x] = value; }
    }

    public bool Valid(EdgeId e) => e.Across ? e.X >= 0 && e.X < Across && e.Y >= 0 && e.Y <= Down : e.X >= 0 && e.X <= Across && e.Y >= 0 && e.Y < Down;

    /// <summary>Is there floor on at least one side of the edge?</summary>
    public bool Touches(EdgeId e)
    {
        var (a, b) = e.Sides;
        return this[a.X, a.Y] || this[b.X, b.Y];
    }

    public EdgeKind Mark(EdgeId e) => _marks.TryGetValue(e, out var k) ? k : EdgeKind.Auto;

    public void SetMark(EdgeId e, EdgeKind k)
    {
        if (!Valid(e)) return;
        if (k == EdgeKind.Auto) _marks.Remove(e);
        else _marks[e] = k;
    }

    /// <summary>A wall would stand here without any mark: floor on one side only.</summary>
    public bool AutoWall(EdgeId e)
    {
        var (a, b) = e.Sides;
        return this[a.X, a.Y] != this[b.X, b.Y];
    }

    public EdgeState State(EdgeId e)
    {
        if (!Touches(e)) return EdgeState.None;
        return Mark(e) switch
        {
            EdgeKind.Wall => EdgeState.Wall,
            EdgeKind.Door => EdgeState.Door,
            EdgeKind.Open => EdgeState.None,
            _ => AutoWall(e) ? EdgeState.Wall : EdgeState.None,
        };
    }

    /// <summary>Every edge of the grid.</summary>
    public IEnumerable<EdgeId> Edges()
    {
        for (int y = 0; y <= Down; y++)
            for (int x = 0; x < Across; x++) yield return new EdgeId(x, y, true);
        for (int y = 0; y < Down; y++)
            for (int x = 0; x <= Across; x++) yield return new EdgeId(x, y, false);
    }

    public int FloorCount => _floor.Count(f => f);

    /// <summary>The same plan made <paramref name="across"/> × <paramref name="down"/>, kept at the top left.</summary>
    public PlanGrid Resized(int across, int down)
    {
        var g = new PlanGrid(across, down);
        for (int y = 0; y < g.Down; y++)
            for (int x = 0; x < g.Across; x++) g[x, y] = this[x, y];
        foreach (var kv in _marks) if (g.Valid(kv.Key)) g._marks[kv.Key] = kv.Value;
        return g;
    }

    /// <summary>The plan moved so the floor starts at its top left corner, with <paramref name="border"/> empty squares round it.</summary>
    public PlanGrid Trimmed(int border = 0)
    {
        int x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1;
        for (int y = 0; y < Down; y++)
            for (int x = 0; x < Across; x++)
                if (this[x, y]) { x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); x1 = Math.Max(x1, x); y1 = Math.Max(y1, y); }
        if (x1 < 0) return this;
        int dx = border - x0, dy = border - y0;
        var g = new PlanGrid(x1 - x0 + 1 + 2 * border, y1 - y0 + 1 + 2 * border);
        for (int y = 0; y < Down; y++)
            for (int x = 0; x < Across; x++) if (this[x, y]) g[x + dx, y + dy] = true;
        foreach (var kv in _marks)
        {
            var e = kv.Key with { X = kv.Key.X + dx, Y = kv.Key.Y + dy };
            if (g.Valid(e)) g._marks[e] = kv.Value;
        }
        return g;
    }

    public PlanGrid Copy() => From(ToPlan());
}
