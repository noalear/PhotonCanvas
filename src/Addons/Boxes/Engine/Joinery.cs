using Clipper2Lib;

namespace PhotonCanvas.Addon.Boxes;

/// <summary>
/// A flat piece of sheet being designed: its outline in its own flat coordinates (y up), where it
/// sits in the box, and what's taken out of it. Where two pieces' slabs overlap once put together
/// (a corner, a tab in a wall, two dividers crossing) <see cref="Joinery"/> shares the overlap out
/// between them: each part of it stays in one piece and is cut away from the other.
/// </summary>
public sealed class Slab
{
    public required string Name { get; init; }
    public required Pose Pose { get; init; }
    /// <summary>Which piece keeps a shared corner: front and back 3, ends 2, bottom and lid 1.</summary>
    public int Rank { get; init; }
    public int Group { get; init; }
    /// <summary>On the lid's top: the area a carved border goes round.</summary>
    public RectD? LidArea { get; set; }
    public List<(double Lo, double Hi)> NoSeam { get; } = [];
    /// <summary>The outline before any joint is cut (its bounding box, put together, is what overlaps other pieces).</summary>
    public PathsD Body { get; } = [];
    /// <summary>More of the piece that isn't jointed to anything (a bending lid going on from the back).</summary>
    public PathsD Extra { get; } = [];
    /// <summary>Taken out of it whatever the joints say (pin holes, a finger notch).</summary>
    public PathsD Holes { get; } = [];
    public List<PathD> Slits { get; } = [];
    public List<(PathsD Clip, Pose Pose)>? Facets { get; set; }
    internal PathsD Removed { get; } = [];

    /// <summary>The body's bounding box put together (its thickness centred on its plane).</summary>
    public Box3 Space(double t)
    {
        var b = Geo.Bounds(Body);
        return Box3.Of(new[] { (b.left, b.bottom), (b.right, b.bottom), (b.right, b.top), (b.left, b.top) }
            .SelectMany(q => new[] { Pose.At(q.Item1, q.Item2, -t / 2), Pose.At(q.Item1, q.Item2, t / 2) }));
    }

    /// <summary>Cuts a block of the room out of this piece (as a rectangle in its flat coordinates).</summary>
    internal void Remove(Box3 block)
    {
        double u0 = double.MaxValue, v0 = double.MaxValue, u1 = double.MinValue, v1 = double.MinValue;
        foreach (var c in block.Corners())
        {
            var (u, v, _) = Pose.Local(c);
            u0 = Math.Min(u0, u); u1 = Math.Max(u1, u); v0 = Math.Min(v0, v); v1 = Math.Max(v1, v);
        }
        if (u1 - u0 > 1e-9 && v1 - v0 > 1e-9) Removed.Add(Geo.Rect(u0, v0, u1, v1));
    }
}

/// <summary>How two pieces join where they overlap.</summary>
public enum JointKind
{
    /// <summary>Box joint: the overlap cut in fingers that go alternately to each piece.</summary>
    Finger,
    /// <summary>Tabs of the first piece through slots in the second; the rest of the overlap stays in the second.</summary>
    Tabs,
    /// <summary>Two pieces crossing: each slotted half way, the first from the top, the second from the bottom.</summary>
    Halving,
    /// <summary>The overlap stays in the first piece; the second is cut short (glued).</summary>
    Butt,
}

/// <summary>
/// Joins slabs where they overlap. The overlap of two pieces is a long block (sheet thick by sheet
/// thick along a corner); its length is shared out as fingers, tabs or halves, and a block that
/// goes to one piece is cut away from the other. Where a third piece shares a corner (the bottom
/// under two walls) that bit goes to the highest ranked of the two, so each corner cube ends up in
/// exactly one piece. <see cref="FitMm"/> is the play: positive leaves fingers and slots that much
/// looser (on top of the kerf, which the cut layer takes care of).
/// </summary>
public sealed class Joinery(double thicknessMm, double fitMm)
{
    sealed record Joint(Slab A, Slab B, JointKind Kind, double Size, int Count);

    public double T { get; } = thicknessMm;
    public double FitMm { get; } = fitMm;
    public List<Slab> Slabs { get; } = [];
    public List<Finding> Findings { get; } = [];
    readonly List<Joint> _joints = [];

    public Slab Add(Slab s) { Slabs.Add(s); return s; }

    /// <summary>Fingers about <paramref name="fingerMm"/> wide along where the two meet.</summary>
    public void Finger(Slab a, Slab b, double fingerMm) => _joints.Add(new(a, b, JointKind.Finger, fingerMm, 0));
    /// <summary><paramref name="count"/> tabs of <paramref name="tabbed"/>, each <paramref name="widthMm"/> wide, through slots in <paramref name="into"/> (0: as many as fit).</summary>
    public void Tabs(Slab tabbed, Slab into, int count, double widthMm) => _joints.Add(new(tabbed, into, JointKind.Tabs, widthMm, count));
    public void Halving(Slab fromTop, Slab fromBottom) => _joints.Add(new(fromTop, fromBottom, JointKind.Halving, 0, 0));
    public void Butt(Slab keeps, Slab cutShort) => _joints.Add(new(keeps, cutShort, JointKind.Butt, 0, 0));

    void Bad(string text) => Findings.Add(new(Severity.Bad, text));

    static int LongestAxis(Box3 b) => b.Size(0) >= b.Size(1) && b.Size(0) >= b.Size(2) ? 0 : b.Size(1) >= b.Size(2) ? 1 : 2;

    /// <summary>The stretches of [lo, hi] along an axis that other pieces also fill, merged.</summary>
    static List<(double Lo, double Hi)> Zones(Box3 overlap, int axis, IEnumerable<Box3> others)
    {
        var raw = others.Select(o => overlap.Intersect(o)).Where(b => b.Solid).Select(b => (b.Lo[axis], b.Hi[axis])).OrderBy(z => z.Item1).ToList();
        var merged = new List<(double, double)>();
        foreach (var z in raw)
            if (merged.Count > 0 && z.Item1 <= merged[^1].Item2 + 1e-6) merged[^1] = (merged[^1].Item1, Math.Max(merged[^1].Item2, z.Item2));
            else merged.Add(z);
        return merged;
    }

    /// <summary>[lo, hi] with the zones taken out.</summary>
    static List<(double Lo, double Hi)> Free(double lo, double hi, List<(double Lo, double Hi)> zones)
    {
        var free = new List<(double, double)>();
        double at = lo;
        foreach (var z in zones)
        {
            if (z.Lo - at > 1e-6) free.Add((at, Math.Min(z.Lo, hi)));
            at = Math.Max(at, z.Hi);
        }
        if (hi - at > 1e-6) free.Add((at, hi));
        return free;
    }

    /// <summary>An odd number of fingers about <paramref name="size"/> wide in a stretch (at least three where they'd be wider than the sheet).</summary>
    public static int FingerCount(double length, double size, double t)
    {
        int n = Math.Max(1, (int)Math.Round(length / Math.Max(0.5, size)));
        if (n % 2 == 0) n = n - 1 >= 1 && length / (n - 1) <= size * 1.35 ? n - 1 : n + 1;
        if (n < 3 && length >= 3 * Math.Max(t, 1.5)) n = 3;
        return n;
    }

    /// <summary>Shares every overlap out, then each piece's outline is its body less what it lost.</summary>
    public List<Part> Build()
    {
        var space = Slabs.ToDictionary(s => s, s => s.Space(T));
        var fingered = _joints.Where(j => j.Kind == JointKind.Finger).SelectMany(j => new[] { (j.A, j.B), (j.B, j.A) }).ToLookup(p => p.Item1, p => p.Item2);
        foreach (var j in _joints)
        {
            var ov = space[j.A].Intersect(space[j.B]);
            if (!ov.Solid)
            {
                Bad($"The {j.A.Name.ToLowerInvariant()} doesn't meet the {j.B.Name.ToLowerInvariant()}.");
                continue;
            }
            switch (j.Kind)
            {
                case JointKind.Finger: FingerJoint(j, ov, space, fingered); break;
                case JointKind.Tabs: TabJoint(j, ov, space); break;
                case JointKind.Halving: HalvingJoint(j, ov, space); break;
                case JointKind.Butt: j.B.Remove(ov); break;
            }
        }
        var parts = new List<Part>();
        foreach (var s in Slabs)
        {
            var cut = Geo.Subtract(Geo.Union(Geo.Union(s.Body), Geo.Union(s.Extra)), Geo.Union(Geo.Union(s.Removed), Geo.Union(s.Holes)));
            if (Geo.Pieces(cut) != 1)
            {
                Bad($"The {s.Name.ToLowerInvariant()} comes out in more than one piece: make the box bigger, the fingers wider or the sheet thinner.");
                continue;
            }
            if (Thin(cut) > 0.05) Findings.Add(new(Severity.Warn, $"The {s.Name.ToLowerInvariant()} has bits narrower than {Bridge:0.#} mm that may break: make the fingers wider or the box a little bigger."));
            var slits = Geo.ClipLines(s.Slits, cut);
            parts.Add(new Part(s.Name, cut, slits, [s.Pose]) { Facets = s.Facets, Group = s.Group, LidArea = s.LidArea, NoSeam = s.NoSeam });
        }
        return parts;
    }

    void FingerJoint(Joint j, Box3 ov, Dictionary<Slab, Box3> space, ILookup<Slab, Slab> fingered)
    {
        int axis = LongestAxis(ov);
        var (hiRank, loRank) = j.A.Rank >= j.B.Rank ? (j.A, j.B) : (j.B, j.A);
        // A corner another piece also fills (one fingered to both) goes to the higher ranked of the two.
        var thirds = Slabs.Where(c => c != j.A && c != j.B && fingered[j.A].Contains(c) && fingered[j.B].Contains(c)).Select(c => space[c]);
        var zones = Zones(ov, axis, thirds);
        foreach (var z in zones) loRank.Remove(ov.Slice(axis, z.Lo, z.Hi));
        double grow = FitMm / 4;
        foreach (var (lo, hi) in Free(ov.Lo[axis], ov.Hi[axis], zones))
        {
            int n = FingerCount(hi - lo, j.Size, T);
            double seg = (hi - lo) / n;
            for (int i = 0; i < n; i++)
            {
                // Next to a corner the lower ranked piece's finger, so the two alternate there too.
                var loses = i % 2 == 0 ? hiRank : loRank;
                var block = ov.Slice(axis, lo + i * seg, lo + (i + 1) * seg).Grown(V3.Axis(axis, grow));
                loses.Remove(block);
            }
        }
    }

    void TabJoint(Joint j, Box3 ov, Dictionary<Slab, Box3> space)
    {
        int axis = LongestAxis(ov);
        var zones = Zones(ov, axis, Slabs.Where(c => c != j.A && c != j.B).Select(c => space[c]));
        foreach (var z in zones) j.A.Remove(ov.Slice(axis, z.Lo, z.Hi));
        foreach (var (lo, hi) in Free(ov.Lo[axis], ov.Hi[axis], zones))
        {
            double len = hi - lo;
            int n = j.Count > 0 ? j.Count : Math.Max(1, (int)Math.Floor(len / (3 * j.Size)));
            double w = Math.Min(j.Size, len / (2 * n));
            double at = lo;
            for (int i = 0; i < n; i++)
            {
                double c = lo + len * (i + 0.5) / n;
                if (c - w / 2 > at) j.A.Remove(ov.Slice(axis, at, c - w / 2));
                // The slot: the tab's size, the play wider all round.
                var tab = ov.Slice(axis, c - w / 2, c + w / 2);
                j.B.Remove(tab.Grown(new V3(FitMm / 2, FitMm / 2, FitMm / 2)));
                at = c + w / 2;
            }
            if (hi > at) j.A.Remove(ov.Slice(axis, at, hi));
        }
    }

    void HalvingJoint(Joint j, Box3 ov, Dictionary<Slab, Box3> space)
    {
        // Along the axis both pieces run along (the one that's neither's thickness): up and down for dividers.
        int axis = LongestAxis(ov);
        var zones = Zones(ov, axis, Slabs.Where(c => c != j.A && c != j.B).Select(c => space[c]));
        var free = Free(ov.Lo[axis], ov.Hi[axis], zones);
        if (free.Count == 0) return;
        double lo = free.Min(f => f.Lo), hi = free.Max(f => f.Hi), mid = (lo + hi) / 2;
        var g = new V3(axis == 0 ? 0 : FitMm / 2, axis == 1 ? 0 : FitMm / 2, axis == 2 ? 0 : FitMm / 2);
        // The first is slotted from the top (loses the upper half), the second from the bottom; each slot the play wider.
        j.A.Remove(ov.Slice(axis, mid, ov.Hi[axis] + T).Grown(g));
        j.B.Remove(ov.Slice(axis, ov.Lo[axis] - T, mid).Grown(g));
    }

    /// <summary>The narrowest strip of sheet that's safe to leave (mm).</summary>
    public const double Bridge = 1.2;

    /// <summary>How much of the piece (mm²) is in strips narrower than about <see cref="Bridge"/>.</summary>
    public static double Thin(PathsD cut)
    {
        double w = Bridge * 0.8;
        var opened = Geo.Offset(Geo.Offset(cut, -w / 2), w / 2);
        return Geo.Area(Geo.Subtract(cut, opened));
    }
}
