namespace PhotonCanvas.Addon.CityBuilder;

/// <summary>What's picked on the map: buildings, trees and things and labels (by their ids) and stretches of fence.</summary>
public sealed record CityPick
{
    public HashSet<Guid> Buildings { get; init; } = [];
    public HashSet<Guid> Props { get; init; } = [];
    public HashSet<Guid> Labels { get; init; } = [];
    public HashSet<FenceEdge> Fences { get; init; } = [];

    public static CityPick None => new();

    public bool IsEmpty => Buildings.Count == 0 && Props.Count == 0 && Labels.Count == 0 && Fences.Count == 0;

    public static CityPick Of(CityBuilding b) => new() { Buildings = [b.Id] };
    public static CityPick Of(Prop p) => new() { Props = [p.Id] };
    public static CityPick Of(CityLabel l) => new() { Labels = [l.Id] };

    /// <summary>Everything in this or the other.</summary>
    public CityPick With(CityPick o) => new()
    {
        Buildings = [.. Buildings, .. o.Buildings], Props = [.. Props, .. o.Props], Labels = [.. Labels, .. o.Labels], Fences = [.. Fences, .. o.Fences],
    };

    /// <summary>The other's things added, or taken out where they're picked already (Ctrl and click).</summary>
    public CityPick Toggle(CityPick o)
    {
        static HashSet<T> Flip<T>(HashSet<T> a, HashSet<T> b) { var r = new HashSet<T>(a); r.SymmetricExceptWith(b); return r; }
        return new() { Buildings = Flip(Buildings, o.Buildings), Props = Flip(Props, o.Props), Labels = Flip(Labels, o.Labels), Fences = Flip(Fences, o.Fences) };
    }

    /// <summary>Whether all of the other's things are picked here.</summary>
    public bool Holds(CityPick o) => o.Buildings.IsSubsetOf(Buildings) && o.Props.IsSubsetOf(Props) && o.Labels.IsSubsetOf(Labels) && o.Fences.IsSubsetOf(Fences);

    /// <summary>Just the things still on the map.</summary>
    public CityPick On(CityMap m)
    {
        var edges = m.Fences.SelectMany(f => f.Edges).ToHashSet();
        return new()
        {
            Buildings = [.. Buildings.Where(id => m.Building(id) is not null)],
            Props = [.. Props.Where(id => m.Props.Any(p => p.Id == id))],
            Labels = [.. Labels.Where(id => m.Labels.Any(l => l.Id == id))],
            Fences = [.. Fences.Where(edges.Contains)],
        };
    }
}

/// <summary>Things copied off the map, kept to put down again: placed relative to the top left corner of what was copied.</summary>
public sealed record CityClip
{
    public List<CityBuilding> Buildings { get; init; } = [];
    public List<Prop> Props { get; init; } = [];
    public List<CityLabel> Labels { get; init; } = [];
    public List<(FenceKind Kind, FenceEdge Edge)> Fences { get; init; } = [];
    /// <summary>Its size (squares), to keep it on the map.</summary>
    public int Width { get; init; }
    public int Depth { get; init; }

    public bool IsEmpty => Buildings.Count == 0 && Props.Count == 0 && Labels.Count == 0 && Fences.Count == 0;
}

/// <summary>
/// Changes to a map's picked things together: moving them a whole number of squares, turning them a quarter
/// turn about their middle, taking them away, copying and putting copies down. Also the squares a road
/// dragged from one square to another covers.
/// </summary>
public static class CityEdit
{
    /// <summary>Everything wholly inside a box (squares, either way round).</summary>
    public static CityPick Within(CityMap m, double ax, double ay, double bx, double by)
    {
        double x0 = Math.Min(ax, bx), x1 = Math.Max(ax, bx), y0 = Math.Min(ay, by), y1 = Math.Max(ay, by);
        const double e = 1e-9;
        bool In(double x, double y) => x >= x0 - e && x <= x1 + e && y >= y0 - e && y <= y1 + e;
        return new()
        {
            Buildings = [.. m.Buildings.Where(b => { var (w, d) = CityFootprint.Size(b); return In(b.X, b.Y) && In(b.X + w, b.Y + d); }).Select(b => b.Id)],
            Props = [.. m.Props.Where(p => In(p.X, p.Y)).Select(p => p.Id)],
            Labels = [.. m.Labels.Where(l => In(l.X, l.Y)).Select(l => l.Id)],
            Fences = [.. m.Fences.SelectMany(f => f.Edges).Where(ed => In(ed.X, ed.Y) && (ed.Across ? In(ed.X + 1, ed.Y) : In(ed.X, ed.Y + 1)))],
        };
    }

    /// <summary>The fence run an edge belongs to: the edges of the same fence joined to it end to end.</summary>
    public static HashSet<FenceEdge> Run(CityMap m, FenceEdge start)
    {
        if (m.Fences.FirstOrDefault(f => f.Edges.Contains(start)) is not { } fence) return [];
        var ends = new Dictionary<(int, int), List<FenceEdge>>();
        foreach (var ed in fence.Edges)
            foreach (var p in Ends(ed))
            {
                if (!ends.TryGetValue(p, out var l)) ends[p] = l = [];
                l.Add(ed);
            }
        var run = new HashSet<FenceEdge> { start };
        var todo = new Stack<FenceEdge>([start]);
        while (todo.Count > 0)
            foreach (var p in Ends(todo.Pop()))
                foreach (var n in ends[p])
                    if (run.Add(n)) todo.Push(n);
        return run;
    }

    static (int X, int Y)[] Ends(FenceEdge e) => [(e.X, e.Y), e.Across ? (e.X + 1, e.Y) : (e.X, e.Y + 1)];

    /// <summary>The box round the picked things (squares), or null when nothing is picked.</summary>
    public static (double X0, double Y0, double X1, double Y1)? Bounds(CityMap m, CityPick pick)
    {
        double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
        void Add(double x, double y) { x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); x1 = Math.Max(x1, x); y1 = Math.Max(y1, y); }
        foreach (var b in m.Buildings.Where(b => pick.Buildings.Contains(b.Id))) { var (w, d) = CityFootprint.Size(b); Add(b.X, b.Y); Add(b.X + w, b.Y + d); }
        foreach (var p in m.Props.Where(p => pick.Props.Contains(p.Id))) Add(p.X, p.Y);
        foreach (var l in m.Labels.Where(l => pick.Labels.Contains(l.Id))) Add(l.X, l.Y);
        foreach (var e in pick.Fences) foreach (var (x, y) in Ends(e)) Add(x, y);
        return x0 > x1 ? null : (x0, y0, x1, y1);
    }

    /// <summary>The move (squares) made as near to the one asked for as keeps everything picked on the map.</summary>
    public static (int Dx, int Dy) Clamp(CityMap m, CityPick pick, int dx, int dy)
    {
        if (Bounds(m, pick) is not { } bb) return (0, 0);
        // Buildings and fences on whole squares; trees and labels just inside the edge.
        int lo = (int)Math.Ceiling(-bb.X0 - 1e-9), hi = (int)Math.Floor(m.Across - bb.X1 + 1e-9);
        int top = (int)Math.Ceiling(-bb.Y0 - 1e-9), bottom = (int)Math.Floor(m.Down - bb.Y1 + 1e-9);
        return (lo > hi ? 0 : Math.Clamp(dx, lo, hi), top > bottom ? 0 : Math.Clamp(dy, top, bottom));
    }

    /// <summary>The picked things moved (dx, dy) squares, kept on the map; and what's picked afterwards (the fences' edges move).</summary>
    public static (CityMap Map, CityPick Pick) Move(CityMap m, CityPick pick, int dx, int dy)
    {
        (dx, dy) = Clamp(m, pick, dx, dy);
        if (dx == 0 && dy == 0) return (m, pick);
        return Map(m, pick, (x, y) => (x + dx, y + dy), 0, b => b with { X = b.X + dx, Y = b.Y + dy });
    }

    /// <summary>
    /// The picked things turned <paramref name="by"/> quarter turns (clockwise seen from above) about a point in
    /// their middle (a grid point or the middle of a square, so everything stays on the grid), then moved back onto
    /// the map if they went over its edge. Turning again about the <see cref="Pivot"/> it gives back keeps turns
    /// exact: four bring everything back where it was, and a turn the other way undoes one.
    /// </summary>
    public static (CityMap Map, CityPick Pick, (double X, double Y) Pivot) Turn(CityMap m, CityPick pick, int by, (double X, double Y)? pivot = null)
    {
        if (Bounds(m, pick) is not { } bb) return (m, pick, default);
        int q = ((by % 4) + 4) % 4;
        var (cx, cy) = pivot ?? Pivot(bb);
        if (q == 0) return (m, pick, (cx, cy));
        (double, double) Rot(double x, double y)
        {
            for (int i = 0; i < q; i++) (x, y) = (cx - (y - cy), cy + (x - cx));
            return (x, y);
        }
        var (turned, after) = Map(m, pick, Rot, q * 90, b =>
        {
            var (w, d) = CityFootprint.Size(b);
            var (ax, ay) = Rot(b.X + b.ShiftX, b.Y + b.ShiftY);
            var (bx, by2) = Rot(b.X + b.ShiftX + w, b.Y + b.ShiftY + d);
            // On the grid the corner stays a whole number; off it the shift goes round with the building (its tilt is about its own middle, so it keeps).
            double nx = Math.Min(ax, bx), ny = Math.Min(ay, by2);
            int x = CityFootprint.OnGrid(b) ? (int)Math.Round(nx) : (int)Math.Floor(nx + 1e-9), y = CityFootprint.OnGrid(b) ? (int)Math.Round(ny) : (int)Math.Floor(ny + 1e-9);
            return b with { Turn = (b.Turn + q) % 4, X = x, Y = y, ShiftX = CityFootprint.OnGrid(b) ? 0 : nx - x, ShiftY = CityFootprint.OnGrid(b) ? 0 : ny - y };
        });
        // Back onto the map: moved by what it hangs over the edge (and the pivot with it).
        if (Bounds(turned, after) is not { } nb) return (turned, after, (cx, cy));
        int dx = nb.X0 < 0 ? (int)Math.Ceiling(-nb.X0) : nb.X1 > m.Across ? -(int)Math.Ceiling(nb.X1 - m.Across) : 0;
        int dy = nb.Y0 < 0 ? (int)Math.Ceiling(-nb.Y0) : nb.Y1 > m.Down ? -(int)Math.Ceiling(nb.Y1 - m.Down) : 0;
        if (dx == 0 && dy == 0) return (turned, after, (cx, cy));
        var (back, pb) = Map(turned, after, (x, y) => (x + dx, y + dy), 0, b => b with { X = b.X + dx, Y = b.Y + dy });
        return (back, pb, (cx + dx, cy + dy));
    }

    /// <summary>
    /// The point to turn a box about: its middle, moved to the nearest grid point or middle of a square (turning
    /// about either keeps the grid on the grid).
    /// </summary>
    static (double X, double Y) Pivot((double X0, double Y0, double X1, double Y1) bb)
    {
        double cx = Math.Round((bb.X0 + bb.X1)) / 2, cy = Math.Round((bb.Y0 + bb.Y1)) / 2;
        bool hx = Math.Abs(cx - Math.Floor(cx) - 0.5) < 1e-9, hy = Math.Abs(cy - Math.Floor(cy) - 0.5) < 1e-9;
        if (hx != hy)
        {
            // Half a square out one way only: back half a square that way.
            if (hx) cx -= 0.5;
            else cy -= 0.5;
        }
        return (cx, cy);
    }

    /// <summary>Each picked thing moved by a mapping of points (buildings by their own), the trees turned <paramref name="turnDeg"/> more.</summary>
    static (CityMap, CityPick) Map(CityMap m, CityPick pick, Func<double, double, (double X, double Y)> at, double turnDeg, Func<CityBuilding, CityBuilding> building)
    {
        // Each picked edge moved, staying with its own fence; where it lands on another fence's edge, it takes its place.
        var movedOf = m.Fences.Select(f => f.Edges.Where(pick.Fences.Contains).Select(e =>
        {
            var ends = Ends(e).Select(p => at(p.X, p.Y)).Select(p => ((int)Math.Round(p.X), (int)Math.Round(p.Y))).ToArray();
            return ends[0].Item2 == ends[1].Item2
                ? new FenceEdge(Math.Min(ends[0].Item1, ends[1].Item1), ends[0].Item2, true)
                : new FenceEdge(ends[0].Item1, Math.Min(ends[0].Item2, ends[1].Item2), false);
        }).ToList()).ToList();
        var moved = movedOf.SelectMany(l => l).ToHashSet();
        var fences = m.Fences.Select((f, i) => f with
        {
            Edges = [.. f.Edges.Where(e => !pick.Fences.Contains(e) && !moved.Contains(e)), .. movedOf[i].Distinct()],
        }).ToList();
        var map = m with
        {
            Buildings = [.. m.Buildings.Select(b => pick.Buildings.Contains(b.Id) ? building(b) : b)],
            Props = [.. m.Props.Select(p => { if (!pick.Props.Contains(p.Id)) return p; var (x, y) = at(p.X, p.Y); return p with { X = x, Y = y, TurnDeg = (p.TurnDeg + turnDeg) % 360 }; })],
            Labels = [.. m.Labels.Select(l => { if (!pick.Labels.Contains(l.Id)) return l; var (x, y) = at(l.X, l.Y); return l with { X = x, Y = y }; })],
            Fences = [.. fences.Where(f => f.Edges.Count > 0)],
        };
        return (map, pick with { Fences = moved });
    }

    /// <summary>The map without the picked things.</summary>
    public static CityMap Delete(CityMap m, CityPick pick) => m with
    {
        Buildings = [.. m.Buildings.Where(b => !pick.Buildings.Contains(b.Id))],
        Props = [.. m.Props.Where(p => !pick.Props.Contains(p.Id))],
        Labels = [.. m.Labels.Where(l => !pick.Labels.Contains(l.Id))],
        Fences = [.. m.Fences.Select(f => f with { Edges = [.. f.Edges.Where(e => !pick.Fences.Contains(e))] }).Where(f => f.Edges.Count > 0)],
    };

    /// <summary>The picked things, kept relative to the top left corner of the squares they're on.</summary>
    public static CityClip Copy(CityMap m, CityPick pick)
    {
        if (Bounds(m, pick) is not { } bb) return new CityClip();
        int ox = (int)Math.Floor(bb.X0), oy = (int)Math.Floor(bb.Y0);
        return new CityClip
        {
            Buildings = [.. m.Buildings.Where(b => pick.Buildings.Contains(b.Id)).Select(b => b with { X = b.X - ox, Y = b.Y - oy })],
            Props = [.. m.Props.Where(p => pick.Props.Contains(p.Id)).Select(p => p with { X = p.X - ox, Y = p.Y - oy })],
            Labels = [.. m.Labels.Where(l => pick.Labels.Contains(l.Id)).Select(l => l with { X = l.X - ox, Y = l.Y - oy })],
            Fences = [.. m.Fences.SelectMany(f => f.Edges.Where(pick.Fences.Contains).Select(e => (f.Kind, e with { X = e.X - ox, Y = e.Y - oy })))],
            Width = Math.Max(1, (int)Math.Ceiling(bb.X1) - ox),
            Depth = Math.Max(1, (int)Math.Ceiling(bb.Y1) - oy),
        };
    }

    /// <summary>A copy of the clip put down with its top left corner at (x, y) (moved to fit on the map), and what's picked: the copy.</summary>
    public static (CityMap Map, CityPick Pick) Paste(CityMap m, CityClip clip, int x, int y)
    {
        if (clip.IsEmpty) return (m, CityPick.None);
        x = Math.Clamp(x, 0, Math.Max(0, m.Across - clip.Width));
        y = Math.Clamp(y, 0, Math.Max(0, m.Down - clip.Depth));
        var buildings = clip.Buildings.Select(b => b with { Id = Guid.NewGuid(), X = b.X + x, Y = b.Y + y }).ToList();
        var props = clip.Props.Select(p => p with { Id = Guid.NewGuid(), X = p.X + x, Y = p.Y + y }).ToList();
        var labels = clip.Labels.Select(l => l with { Id = Guid.NewGuid(), X = l.X + x, Y = l.Y + y }).ToList();
        var fences = m.Fences.ToList();
        var edges = new HashSet<FenceEdge>();
        foreach (var (kind, e0) in clip.Fences)
        {
            var e = e0 with { X = e0.X + x, Y = e0.Y + y };
            if (e.X < 0 || e.Y < 0 || e.X > m.Across || e.Y > m.Down) continue;
            // Wherever there's a fence already, the copy's takes its place.
            fences = [.. fences.Select(f => f with { Edges = [.. f.Edges.Where(o => o != e)] }).Where(f => f.Edges.Count > 0)];
            int i = fences.FindIndex(f => f.Kind == kind);
            if (i < 0) fences.Add(new Fence { Kind = kind, Edges = [e] });
            else fences[i] = fences[i] with { Edges = [.. fences[i].Edges, e] };
            edges.Add(e);
        }
        var map = m with { Buildings = [.. m.Buildings, .. buildings], Props = [.. m.Props, .. props], Labels = [.. m.Labels, .. labels], Fences = fences };
        return (map, new CityPick { Buildings = [.. buildings.Select(b => b.Id)], Props = [.. props.Select(p => p.Id)], Labels = [.. labels.Select(l => l.Id)], Fences = edges });
    }

    /// <summary>
    /// The squares a road dragged from square A to square B covers, <paramref name="width"/> squares wide: along
    /// the longer way first and then round the corner, or (<paramref name="straight"/>) in a straight line however
    /// it slants.
    /// </summary>
    public static HashSet<(int X, int Y)> Road(int ax, int ay, int bx, int by, int width, bool straight)
    {
        var line = new List<(int X, int Y)>();
        if (straight)
        {
            int n = Math.Max(Math.Abs(bx - ax), Math.Abs(by - ay));
            for (int i = 0; i <= n; i++)
            {
                double t = n == 0 ? 0 : i / (double)n;
                line.Add(((int)Math.Round(ax + (bx - ax) * t), (int)Math.Round(ay + (by - ay) * t)));
            }
        }
        else
        {
            bool acrossFirst = Math.Abs(bx - ax) >= Math.Abs(by - ay);
            int cx = acrossFirst ? bx : ax, cy = acrossFirst ? ay : by;
            void Leg(int x0, int y0, int x1, int y1)
            {
                int n = Math.Max(Math.Abs(x1 - x0), Math.Abs(y1 - y0));
                for (int i = 0; i <= n; i++) line.Add((x0 + Math.Sign(x1 - x0) * i, y0 + Math.Sign(y1 - y0) * i));
            }
            Leg(ax, ay, cx, cy);
            Leg(cx, cy, bx, by);
        }
        width = Math.Max(1, width);
        int h0 = (width - 1) / 2;
        var cells = new HashSet<(int X, int Y)>();
        foreach (var (x, y) in line)
            for (int j = 0; j < width; j++)
                for (int i = 0; i < width; i++) cells.Add((x - h0 + i, y - h0 + j));
        return cells;
    }
}
