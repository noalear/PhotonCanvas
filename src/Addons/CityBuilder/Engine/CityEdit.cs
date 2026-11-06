namespace PhotonCanvas.Addon.CityBuilder;

/// <summary>How points put down or dragged on the map snap: to whole squares (grid points), half squares, or not at all.</summary>
public enum CitySnap { Squares, HalfSquares, Free }

/// <summary>What's picked on the map: buildings, roads, fences, trees and things and labels, by their ids.</summary>
public sealed record CityPick
{
    public HashSet<Guid> Buildings { get; init; } = [];
    public HashSet<Guid> Props { get; init; } = [];
    public HashSet<Guid> Labels { get; init; } = [];
    public HashSet<Guid> Fences { get; init; } = [];
    public HashSet<Guid> Roads { get; init; } = [];

    public static CityPick None => new();

    public bool IsEmpty => Buildings.Count == 0 && Props.Count == 0 && Labels.Count == 0 && Fences.Count == 0 && Roads.Count == 0;

    /// <summary>How many things are picked.</summary>
    public int Count => Buildings.Count + Props.Count + Labels.Count + Fences.Count + Roads.Count;

    public static CityPick Of(CityBuilding b) => new() { Buildings = [b.Id] };
    public static CityPick Of(Prop p) => new() { Props = [p.Id] };
    public static CityPick Of(CityLabel l) => new() { Labels = [l.Id] };
    public static CityPick Of(Fence f) => new() { Fences = [f.Id] };
    public static CityPick Of(CityRoad r) => new() { Roads = [r.Id] };

    /// <summary>Everything in this or the other.</summary>
    public CityPick With(CityPick o) => new()
    {
        Buildings = [.. Buildings, .. o.Buildings], Props = [.. Props, .. o.Props], Labels = [.. Labels, .. o.Labels], Fences = [.. Fences, .. o.Fences], Roads = [.. Roads, .. o.Roads],
    };

    /// <summary>The other's things added, or taken out where they're picked already (Ctrl and click).</summary>
    public CityPick Toggle(CityPick o)
    {
        static HashSet<T> Flip<T>(HashSet<T> a, HashSet<T> b) { var r = new HashSet<T>(a); r.SymmetricExceptWith(b); return r; }
        return new() { Buildings = Flip(Buildings, o.Buildings), Props = Flip(Props, o.Props), Labels = Flip(Labels, o.Labels), Fences = Flip(Fences, o.Fences), Roads = Flip(Roads, o.Roads) };
    }

    /// <summary>Whether all of the other's things are picked here.</summary>
    public bool Holds(CityPick o) => o.Buildings.IsSubsetOf(Buildings) && o.Props.IsSubsetOf(Props) && o.Labels.IsSubsetOf(Labels) && o.Fences.IsSubsetOf(Fences) && o.Roads.IsSubsetOf(Roads);

    /// <summary>Just the things still on the map.</summary>
    public CityPick On(CityMap m) => new()
    {
        Buildings = [.. Buildings.Where(id => m.Building(id) is not null)],
        Props = [.. Props.Where(id => m.Props.Any(p => p.Id == id))],
        Labels = [.. Labels.Where(id => m.Labels.Any(l => l.Id == id))],
        Fences = [.. Fences.Where(id => m.Fences.Any(f => f.Id == id))],
        Roads = [.. Roads.Where(id => m.Roads.Any(r => r.Id == id))],
    };
}

/// <summary>Things copied off the map, kept to put down again: placed relative to the top left corner of what was copied.</summary>
public sealed record CityClip
{
    public List<CityBuilding> Buildings { get; init; } = [];
    public List<Prop> Props { get; init; } = [];
    public List<CityLabel> Labels { get; init; } = [];
    public List<Fence> Fences { get; init; } = [];
    public List<CityRoad> Roads { get; init; } = [];
    /// <summary>Its size (squares), to keep it on the map.</summary>
    public int Width { get; init; }
    public int Depth { get; init; }

    public bool IsEmpty => Buildings.Count == 0 && Props.Count == 0 && Labels.Count == 0 && Fences.Count == 0 && Roads.Count == 0;
}

/// <summary>
/// Changes to a map's picked things together: moving them a whole number of squares, turning them a quarter
/// turn about their middle, taking them away, copying and putting copies down. Also the squares a road
/// dragged from one square to another covers, and the line it runs along.
/// </summary>
public static class CityEdit
{
    /// <summary>Everything wholly inside a box (squares, either way round).</summary>
    public static CityPick Within(CityMap m, double ax, double ay, double bx, double by)
    {
        m = m.Tidied();
        double x0 = Math.Min(ax, bx), x1 = Math.Max(ax, bx), y0 = Math.Min(ay, by), y1 = Math.Max(ay, by);
        const double e = 1e-9;
        bool In(double x, double y) => x >= x0 - e && x <= x1 + e && y >= y0 - e && y <= y1 + e;
        return new()
        {
            Buildings = [.. m.Buildings.Where(b => { var (w, d) = CityFootprint.Size(b); return In(b.X, b.Y) && In(b.X + w, b.Y + d); }).Select(b => b.Id)],
            Props = [.. m.Props.Where(p => In(p.X, p.Y)).Select(p => p.Id)],
            Labels = [.. m.Labels.Where(l => In(l.X, l.Y)).Select(l => l.Id)],
            Fences = [.. m.Fences.Where(f => f.Points.Count > 0 && f.Points.All(p => In(p.X, p.Y))).Select(f => f.Id)],
            Roads = [.. m.Roads.Where(r => r.Points.Count > 0 && r.Points.All(p => In(p.X, p.Y))).Select(r => r.Id)],
        };
    }

    /// <summary>The box round the picked things (squares), or null when nothing is picked.</summary>
    public static (double X0, double Y0, double X1, double Y1)? Bounds(CityMap m, CityPick pick)
    {
        double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
        void Add(double x, double y) { x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); x1 = Math.Max(x1, x); y1 = Math.Max(y1, y); }
        foreach (var b in m.Buildings.Where(b => pick.Buildings.Contains(b.Id))) { var (w, d) = CityFootprint.Size(b); Add(b.X, b.Y); Add(b.X + w, b.Y + d); }
        foreach (var p in m.Props.Where(p => pick.Props.Contains(p.Id))) Add(p.X, p.Y);
        foreach (var l in m.Labels.Where(l => pick.Labels.Contains(l.Id))) Add(l.X, l.Y);
        foreach (var f in m.Fences.Where(f => pick.Fences.Contains(f.Id))) foreach (var p in f.Points) Add(p.X, p.Y);
        foreach (var r in m.Roads.Where(r => pick.Roads.Contains(r.Id))) foreach (var p in r.Points) Add(p.X, p.Y);
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

    /// <summary>The picked things moved (dx, dy) squares, kept on the map; and what's picked afterwards.</summary>
    public static (CityMap Map, CityPick Pick) Move(CityMap m, CityPick pick, int dx, int dy)
    {
        m = m.Tidied();
        (dx, dy) = Clamp(m, pick, dx, dy);
        if (dx == 0 && dy == 0) return (m, pick);
        return Map(m, pick, (x, y) => (x + dx, y + dy), 0, b => b with { X = b.X + dx, Y = b.Y + dy });
    }

    /// <summary>
    /// The picked things turned <paramref name="by"/> quarter turns (clockwise seen from above) about a point in
    /// their middle (a grid point or the middle of a square, so everything stays on the grid), then moved back onto
    /// the map if they went over its edge. Turning again about the <see cref="Pivot"/> it gives back keeps turns
    /// exact: four bring everything back where it was, and a turn the other way undoes one. Labels stay the right
    /// way up.
    /// </summary>
    public static (CityMap Map, CityPick Pick, (double X, double Y) Pivot) Turn(CityMap m, CityPick pick, int by, (double X, double Y)? pivot = null)
    {
        m = m.Tidied();
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
            var (ax, ay) = Rot(b.X, b.Y);
            var (bx, by2) = Rot(b.X + w, b.Y + d);
            return b with { Turn = (b.Turn + q) % 4, X = (int)Math.Round(Math.Min(ax, bx)), Y = (int)Math.Round(Math.Min(ay, by2)) };
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

    /// <summary>Each picked thing moved by a mapping of points (buildings by their own), the trees and labels turned <paramref name="turnDeg"/> more.</summary>
    static (CityMap, CityPick) Map(CityMap m, CityPick pick, Func<double, double, (double X, double Y)> at, double turnDeg, Func<CityBuilding, CityBuilding> building)
    {
        CityPt P(CityPt p) { var (x, y) = at(p.X, p.Y); return new CityPt(Math.Round(x, 6), Math.Round(y, 6)); }
        var map = m with
        {
            Buildings = [.. m.Buildings.Select(b => pick.Buildings.Contains(b.Id) ? building(b) : b)],
            Props = [.. m.Props.Select(p => { if (!pick.Props.Contains(p.Id)) return p; var (x, y) = at(p.X, p.Y); return p with { X = x, Y = y, TurnDeg = (p.TurnDeg + turnDeg) % 360 }; })],
            Labels = [.. m.Labels.Select(l => { if (!pick.Labels.Contains(l.Id)) return l; var (x, y) = at(l.X, l.Y); return l with { X = x, Y = y, TurnDeg = CityLabel.Upright(l.TurnDeg + turnDeg) }; })],
            Fences = [.. m.Fences.Select(f => pick.Fences.Contains(f.Id) ? f with { Points = [.. f.Points.Select(P)] } : f)],
            Roads = [.. m.Roads.Select(r => pick.Roads.Contains(r.Id) ? r with { Points = [.. r.Points.Select(P)] } : r)],
        };
        return (map, pick);
    }

    /// <summary>The map without the picked things.</summary>
    public static CityMap Delete(CityMap m, CityPick pick)
    {
        m = m.Tidied();
        return m with
        {
            Buildings = [.. m.Buildings.Where(b => !pick.Buildings.Contains(b.Id))],
            Props = [.. m.Props.Where(p => !pick.Props.Contains(p.Id))],
            Labels = [.. m.Labels.Where(l => !pick.Labels.Contains(l.Id))],
            Fences = [.. m.Fences.Where(f => !pick.Fences.Contains(f.Id))],
            Roads = [.. m.Roads.Where(r => !pick.Roads.Contains(r.Id))],
        };
    }

    /// <summary>The picked things, kept relative to the top left corner of the squares they're on.</summary>
    public static CityClip Copy(CityMap m, CityPick pick)
    {
        m = m.Tidied();
        if (Bounds(m, pick) is not { } bb) return new CityClip();
        int ox = (int)Math.Floor(bb.X0), oy = (int)Math.Floor(bb.Y0);
        CityPt O(CityPt p) => new(p.X - ox, p.Y - oy);
        return new CityClip
        {
            Buildings = [.. m.Buildings.Where(b => pick.Buildings.Contains(b.Id)).Select(b => b with { X = b.X - ox, Y = b.Y - oy })],
            Props = [.. m.Props.Where(p => pick.Props.Contains(p.Id)).Select(p => p with { X = p.X - ox, Y = p.Y - oy })],
            Labels = [.. m.Labels.Where(l => pick.Labels.Contains(l.Id)).Select(l => l with { X = l.X - ox, Y = l.Y - oy })],
            Fences = [.. m.Fences.Where(f => pick.Fences.Contains(f.Id)).Select(f => f with { Points = [.. f.Points.Select(O)] })],
            Roads = [.. m.Roads.Where(r => pick.Roads.Contains(r.Id)).Select(r => r with { Points = [.. r.Points.Select(O)] })],
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
        CityPt O(CityPt p) => new(p.X + x, p.Y + y);
        var buildings = clip.Buildings.Select(b => b with { Id = Guid.NewGuid(), X = b.X + x, Y = b.Y + y }).ToList();
        var props = clip.Props.Select(p => p with { Id = Guid.NewGuid(), X = p.X + x, Y = p.Y + y }).ToList();
        var labels = clip.Labels.Select(l => l with { Id = Guid.NewGuid(), X = l.X + x, Y = l.Y + y }).ToList();
        var fences = clip.Fences.Select(f => f with { Id = Guid.NewGuid(), Points = [.. f.Points.Select(O)] }).ToList();
        var roads = clip.Roads.Select(r => r with { Id = Guid.NewGuid(), Points = [.. r.Points.Select(O)] }).ToList();
        var map = m with { Buildings = [.. m.Buildings, .. buildings], Props = [.. m.Props, .. props], Labels = [.. m.Labels, .. labels], Fences = [.. m.Fences, .. fences], Roads = [.. m.Roads, .. roads] };
        return (map, new CityPick { Buildings = [.. buildings.Select(b => b.Id)], Props = [.. props.Select(p => p.Id)], Labels = [.. labels.Select(l => l.Id)], Fences = [.. fences.Select(f => f.Id)], Roads = [.. roads.Select(r => r.Id)] });
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

    // ---- roads and fences, point by point ----

    /// <summary>A point snapped: to the grid (plus <paramref name="offset"/>, half a square for the middles of squares), to half squares, or as it is.</summary>
    public static CityPt Snapped(CityPt p, CitySnap snap, double offset = 0) => snap switch
    {
        CitySnap.Squares => new(Math.Round(p.X - offset) + offset, Math.Round(p.Y - offset) + offset),
        CitySnap.HalfSquares => new(Math.Round(p.X * 2) / 2, Math.Round(p.Y * 2) / 2),
        _ => new(Math.Round(p.X, 3), Math.Round(p.Y, 3)),
    };

    /// <summary>Where a road's middle line snaps to: grid lines for an even width (so it covers whole squares), the middles of squares for an odd one.</summary>
    public static double RoadOffset(double width) => Math.Abs(Math.IEEERemainder(width, 2)) > 0.5 ? 0.5 : 0;

    /// <summary>
    /// The middle line of the road <see cref="Road"/> lays from square A to square B: along the longer way first
    /// and round the corner, or straight however it slants.
    /// </summary>
    public static List<CityPt> RoadLine(int ax, int ay, int bx, int by, int width, bool straight)
    {
        width = Math.Max(1, width);
        int h0 = (width - 1) / 2;
        double off = width / 2.0 - h0;
        CityPt P(int x, int y) => new(x + off, y + off);
        if (straight) return Distinct([P(ax, ay), P(bx, by)]);
        bool acrossFirst = Math.Abs(bx - ax) >= Math.Abs(by - ay);
        return Distinct([P(ax, ay), acrossFirst ? P(bx, ay) : P(ax, by), P(bx, by)]);
    }

    /// <summary>The line with points one on top of the next taken out.</summary>
    public static List<CityPt> Distinct(List<CityPt> pts)
    {
        var res = new List<CityPt>();
        foreach (var p in pts)
            if (res.Count == 0 || res[^1].DistanceTo(p) > 1e-6) res.Add(p);
        return res;
    }

    /// <summary>A line from a to b, round one corner (the longer way first), or straight.</summary>
    public static List<CityPt> Bent(CityPt a, CityPt b, bool straight)
    {
        if (straight) return Distinct([a, b]);
        bool acrossFirst = Math.Abs(b.X - a.X) >= Math.Abs(b.Y - a.Y);
        return Distinct([a, acrossFirst ? new CityPt(b.X, a.Y) : new CityPt(a.X, b.Y), b]);
    }

    /// <summary>
    /// A new road along the points, joined onto the end of a road of the same width and surface that it starts
    /// (or ends) at; named <paramref name="name"/> when it's a new one. Returns the map and the road.
    /// </summary>
    public static (CityMap Map, CityRoad Road) AddRoad(CityMap m, List<CityPt> points, double width, Ground surface, string name)
    {
        points = Distinct(points);
        bool Same(CityRoad r) => Math.Abs(r.Width - width) < 1e-6 && r.Surface == surface && r.Points.Count > 0;
        foreach (var r in m.Roads.Where(Same))
        {
            List<CityPt>? joined = null;
            if (r.Points[^1].DistanceTo(points[0]) < 1e-6) joined = [.. r.Points, .. points.Skip(1)];
            else if (r.Points[0].DistanceTo(points[0]) < 1e-6) joined = [.. Enumerable.Reverse(points).SkipLast(1), .. r.Points];
            if (joined is null) continue;
            var longer = r with { Points = Distinct(joined) };
            return (m with { Roads = [.. m.Roads.Select(x => x.Id == r.Id ? longer : x)] }, longer);
        }
        var road = new CityRoad { Name = name, Width = width, Surface = surface, Points = points };
        return (m with { Roads = [.. m.Roads, road] }, road);
    }

    /// <summary>
    /// A fence from a to b: joined onto the end of one of the same kind it starts or ends at (going all the way
    /// round when it meets its own other end). Returns the map and the fence.
    /// </summary>
    public static (CityMap Map, Fence Fence) AddFence(CityMap m, FenceKind kind, CityPt a, CityPt b, bool ruined = false)
    {
        m = m.Tidied();
        var line = new List<CityPt> { a, b };
        var fences = m.Fences.ToList();
        Fence? AtEnd(CityPt p, Guid? not) => fences.FirstOrDefault(f => f.Kind == kind && !f.Closed && f.Points.Count > 1 && f.Id != not && (f.Points[0].DistanceTo(p) < 1e-6 || f.Points[^1].DistanceTo(p) < 1e-6));
        var id = Guid.NewGuid();
        bool closed = false;
        if (AtEnd(a, null) is { } fa)
        {
            var pts = fa.Points[^1].DistanceTo(a) < 1e-6 ? fa.Points : [.. Enumerable.Reverse(fa.Points)];
            line = [.. pts, b];
            fences.Remove(fa);
            id = fa.Id;
            ruined |= fa.Ruined;
        }
        if (line.Count > 2 && line[0].DistanceTo(b) < 1e-6)
        {
            line.RemoveAt(line.Count - 1);
            closed = true;
        }
        else if (AtEnd(b, id) is { } fb)
        {
            var pts = fb.Points[0].DistanceTo(b) < 1e-6 ? fb.Points : [.. Enumerable.Reverse(fb.Points)];
            line = [.. line, .. pts.Skip(1)];
            fences.Remove(fb);
            ruined |= fb.Ruined;
        }
        var fence = new Fence { Id = id, Kind = kind, Points = Distinct(line), Closed = closed && line.Count > 2, Ruined = ruined };
        fences.Add(fence);
        return (m with { Fences = fences }, fence);
    }

    /// <summary>A fence (or wall) all the way round a rectangle between two corners.</summary>
    public static (CityMap Map, Fence Fence) AddFenceRectangle(CityMap m, FenceKind kind, CityPt a, CityPt b, bool ruined = false)
    {
        double x0 = Math.Min(a.X, b.X), x1 = Math.Max(a.X, b.X), y0 = Math.Min(a.Y, b.Y), y1 = Math.Max(a.Y, b.Y);
        var fence = new Fence { Kind = kind, Closed = true, Ruined = ruined, Points = [new(x0, y0), new(x1, y0), new(x1, y1), new(x0, y1)] };
        return (m.Tidied() with { Fences = [.. m.Tidied().Fences, fence] }, fence);
    }

    /// <summary>Where along a line (squares from its start) the point nearest to p is, and how far away p is.</summary>
    public static (double At, double Distance, int Segment) Nearest(IReadOnlyList<CityPt> pts, bool closed, CityPt p)
    {
        double best = double.MaxValue, at = 0, s = 0;
        int seg = 0;
        int n = closed && pts.Count > 2 ? pts.Count : pts.Count - 1;
        for (int i = 0; i < n; i++)
        {
            var a = pts[i];
            var b = pts[(i + 1) % pts.Count];
            var ab = b - a;
            double l2 = ab.Dot(ab), len = Math.Sqrt(l2);
            double t = l2 < 1e-12 ? 0 : Math.Clamp((p - a).Dot(ab) / l2, 0, 1);
            double d = p.DistanceTo(a + ab * t);
            if (d < best) { best = d; at = s + t * len; seg = i; }
            s += len;
        }
        return (at, best, seg);
    }

    /// <summary>A line's points from <paramref name="s0"/> to <paramref name="s1"/> squares along it (going round past the start of a closed one).</summary>
    public static List<CityPt> Part(IReadOnlyList<CityPt> pts, bool closed, double s0, double s1)
    {
        var all = closed ? [.. pts, pts[0]] : pts.ToList();
        var starts = new List<double> { 0 };
        for (int i = 0; i + 1 < all.Count; i++) starts.Add(starts[^1] + all[i].DistanceTo(all[i + 1]));
        double total = starts[^1];
        CityPt At(double s)
        {
            s = closed && total > 0 ? ((s % total) + total) % total : Math.Clamp(s, 0, total);
            for (int i = 0; i + 1 < all.Count; i++)
                if (s <= starts[i + 1] + 1e-9)
                {
                    double len = starts[i + 1] - starts[i];
                    return len < 1e-9 ? all[i] : all[i] + (all[i + 1] - all[i]) * ((s - starts[i]) / len);
                }
            return all[^1];
        }
        var res = new List<CityPt> { At(s0) };
        for (int lap = 0; lap < (closed ? 2 : 1); lap++)
            for (int i = 1; i < all.Count - (closed ? 0 : 1); i++)
            {
                double s = starts[i] + lap * total;
                if (s > s0 + 1e-6 && s < s1 - 1e-6) res.Add(At(s));
            }
        res.Add(At(s1));
        return Distinct(res);
    }

    /// <summary>
    /// The map with a piece of a fence taken out round a point on it, <paramref name="length"/> squares long
    /// (to the nearest half square): the fence left either side of the gap, or nothing if it was that short.
    /// </summary>
    public static CityMap CutOut(CityMap m, Guid fenceId, CityPt p, double length = 1)
    {
        m = m.Tidied();
        if (m.FenceById(fenceId) is not { } f || f.Points.Count < 2) return m;
        var (at, _, _) = Nearest(f.Points, f.Closed, p);
        double total = f.Length;
        double s0 = Math.Round((at - length / 2) * 2) / 2, s1 = s0 + length;
        var parts = new List<List<CityPt>>();
        if (f.Closed) parts.Add(Part(f.Points, true, s1, s0 + total));
        else
        {
            if (s0 > 0.05) parts.Add(Part(f.Points, false, 0, Math.Min(s0, total)));
            if (s1 < total - 0.05) parts.Add(Part(f.Points, false, Math.Max(0, s1), total));
        }
        var left = parts.Where(q => q.Count > 1).Select((q, i) => f with { Id = i == 0 ? f.Id : Guid.NewGuid(), Points = q, Closed = false }).ToList();
        int idx = m.Fences.FindIndex(x => x.Id == fenceId);
        var fences = m.Fences.ToList();
        fences.RemoveAt(idx);
        fences.InsertRange(idx, left);
        return m with { Fences = fences };
    }

    /// <summary>The squares buildings stand on.</summary>
    public static HashSet<(int X, int Y)> BuildingCells(CityMap m) => m.Buildings.SelectMany(CityFootprint.Cells).ToHashSet();

    /// <summary>Whether a road runs over any of these squares (buildings').</summary>
    public static bool Hits(CityMap m, CityRoad r, HashSet<(int X, int Y)> cells) => r.Cells(m.Across, m.Down).Any(cells.Contains);

    /// <summary>
    /// The road stopped short of the first building it would run into, going along it from its start (to the
    /// nearest half square); null if even its start is in one.
    /// </summary>
    public static CityRoad? StopAtBuildings(CityMap m, CityRoad r, HashSet<(int X, int Y)>? cells = null)
    {
        cells ??= BuildingCells(m);
        return StopAt(m.Across, m.Down, r, cells.Contains);
    }

    /// <summary>
    /// The road stopped short of the first square it would cover that's <paramref name="blocked"/>, going along
    /// it from its start (to the nearest half square), on a map this big; null if even its start is.
    /// </summary>
    public static CityRoad? StopAt(int across, int down, CityRoad r, Func<(int X, int Y), bool> blocked)
    {
        bool Hit(CityRoad q) => q.Cells(across, down).Any(blocked);
        if (!Hit(r)) return r;
        CityRoad Upto(double s) => r with { Points = Part(r.Points, false, 0, s) };
        if (Hit(Upto(0))) return null;
        // The longest stretch from its start, in half squares, that misses them all.
        int lo = 0, hi = (int)Math.Floor(r.Length * 2);
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (Hit(Upto(mid / 2.0))) hi = mid - 1;
            else lo = mid;
        }
        return lo == 0 ? null : Upto(lo / 2.0);
    }

    /// <summary>
    /// What's left of a road outside a box (squares, from its corner x0, y0 to x1, y1): the road cut where it
    /// meets the box, its ends stopping square at the box's edge. Each part keeps the road's name and look;
    /// the first keeps its id.
    /// </summary>
    public static List<CityRoad> Outside(CityRoad r, double x0, double y0, double x1, double y1)
    {
        // The middle line stops half the road's width short of the box, so the road's square end meets its edge.
        double h = Math.Max(0, r.Width / 2 - 0.01);
        var box = new List<CityPt> { new(x0 - h, y0 - h), new(x1 + h, y0 - h), new(x1 + h, y1 + h), new(x0 - h, y1 + h) };
        var inside = new List<(double S0, double S1)>();
        double s = 0;
        foreach (var (a, b) in r.Segments())
        {
            double len = a.DistanceTo(b);
            if (Geometry.Clip(a, b, box) is { } t && t.T1 > t.T0) inside.Add((s + t.T0 * len, s + t.T1 * len));
            s += len;
        }
        double total = s;
        if (inside.Count == 0) return [r];
        var keep = new List<(double S0, double S1)>();
        double from = 0;
        foreach (var (i0, i1) in inside.OrderBy(i => i.S0))
        {
            if (i0 > from + 1e-6) keep.Add((from, i0));
            from = Math.Max(from, i1);
        }
        if (total > from + 1e-6) keep.Add((from, total));
        return [.. keep.Select((k, i) => r with { Id = i == 0 ? r.Id : Guid.NewGuid(), Points = Part(r.Points, false, k.S0, k.S1) }).Where(q => q.Points.Count > 1)];
    }
}
