namespace PhotonCanvas.Addon.CityBuilder;

/// <summary>What's at a free end of a stretch of fence or wall.</summary>
public enum WallEndKind
{
    /// <summary>It just ends: a town wall's turret, a stone wall's pillar, a fence's end post.</summary>
    Cap,
    /// <summary>Beside a road through it: a town wall's gatehouse, a fence's gate post.</summary>
    Gate,
    /// <summary>Under a guard tower standing astride it.</summary>
    Tower,
    /// <summary>Nothing: it runs off the map, or it's a ruin.</summary>
    Open,
}

/// <summary>A stretch of a fence or wall left standing between its gaps: its points (squares), all the way round when <paramref name="Closed"/>.</summary>
public sealed record WallPiece(Fence Fence, List<CityPt> Points, bool Closed)
{
    public double Length
    {
        get
        {
            double l = 0;
            for (int i = 0; i + 1 < Points.Count; i++) l += Points[i].DistanceTo(Points[i + 1]);
            if (Closed && Points.Count > 2) l += Points[^1].DistanceTo(Points[0]);
            return l;
        }
    }
}

/// <summary>A free end of a stretch: where it is, the way out of it (away from the stretch, a unit) and what's there.</summary>
public sealed record WallEnd(Fence Fence, CityPt At, CityPt Out, WallEndKind Kind);

/// <summary>Where a road goes through a fence or wall: the two sides of the gap (a gatehouse spans it on a town wall).</summary>
public sealed record WallGate(Fence Fence, CityPt A, CityPt B)
{
    public double Width => A.DistanceTo(B);
    public CityPt Middle => (A + B) * 0.5;
}

/// <summary>
/// How the map's fences and walls stand: each one's line less the gaps in it (where a road crosses it, where a
/// river runs through it, under a guard tower), the gates where roads go through, and the free ends with what's
/// at each. Roads open the walls wherever they cross them (the road's own squares, or road painted on the ground
/// on both sides of the line), so moving a road moves its gate.
/// </summary>
public sealed record WallLayout(List<WallPiece> Pieces, List<WallEnd> Ends, List<WallGate> Gates)
{
    enum Gap { Water, Road, Tower }

    /// <summary>How far a guard tower reaches either way along a wall it stands on (squares): its footprint to the next half square.</summary>
    public static double TowerReach(Prop t) => Math.Ceiling(0.905 * t.Size * 2 - 1e-6) / 2;

    /// <summary>The layout of the fences <paramref name="which"/> picks (all of them when null).</summary>
    public static WallLayout Of(CityMap m, Func<Fence, bool>? which = null)
    {
        m = m.Tidied();
        var towers = m.Props.Where(p => p.Kind == PropKind.GuardTower).ToList();
        var pieces = new List<WallPiece>();
        var ends = new List<WallEnd>();
        var gates = new List<WallGate>();
        foreach (var f in m.Fences)
        {
            if (which is not null && !which(f)) continue;
            if (f.Points.Count < 2) continue;
            Lay(m, f, towers, pieces, ends, gates);
        }
        // Older maps left a gap in the wall for a road: two ends facing each other across one become a gate.
        var caps = ends.Select((e, i) => (e, i)).Where(x => x.e.Kind == WallEndKind.Cap && !x.e.Fence.Ruined).ToList();
        var pairs = new List<(int A, int B, double D)>();
        for (int i = 0; i < caps.Count; i++)
            for (int j = i + 1; j < caps.Count; j++)
            {
                var (a, b) = (caps[i].e, caps[j].e);
                if (a.Fence.Kind != b.Fence.Kind) continue;
                var d = b.At - a.At;
                double dist = d.Length;
                if (dist < 0.5 || dist > 4.6) continue;
                var dir = d * (1 / dist);
                if (a.Out.Dot(dir) < 0.95 || b.Out.Dot(dir) > -0.95) continue;
                var mid = (a.At + b.At) * 0.5;
                if (!RoadAt(m, mid, new CityPt(-dir.Y, dir.X))) continue;
                pairs.Add((caps[i].i, caps[j].i, dist));
            }
        var paired = new HashSet<int>();
        foreach (var (a, b, _) in pairs.OrderBy(p => p.D))
        {
            if (paired.Contains(a) || paired.Contains(b)) continue;
            paired.Add(a);
            paired.Add(b);
            gates.Add(new WallGate(ends[a].Fence, ends[a].At, ends[b].At));
            ends[a] = ends[a] with { Kind = WallEndKind.Gate };
            ends[b] = ends[b] with { Kind = WallEndKind.Gate };
        }
        return new WallLayout(pieces, ends, gates);
    }

    /// <summary>Whether there's road both sides of a line through a point (across the line, <paramref name="n"/>), or a road over it.</summary>
    static bool RoadAt(CityMap m, CityPt p, CityPt n)
    {
        if (m.Roads.Any(r => r.Covers(p.X, p.Y, 0.01))) return true;
        var a = p + n * 0.3;
        var b = p - n * 0.3;
        return m.GroundAt((int)Math.Floor(a.X), (int)Math.Floor(a.Y)) == Ground.Road && m.GroundAt((int)Math.Floor(b.X), (int)Math.Floor(b.Y)) == Ground.Road;
    }

    static void Lay(CityMap m, Fence f, List<Prop> towers, List<WallPiece> pieces, List<WallEnd> ends, List<WallGate> gates)
    {
        var segs = f.Segments().ToList();
        var starts = new double[segs.Count + 1];
        for (int i = 0; i < segs.Count; i++) starts[i + 1] = starts[i] + segs[i].A.DistanceTo(segs[i].B);
        double total = starts[^1];
        if (total < 1e-6) return;
        // The gaps along the whole line (squares from its start).
        var gaps = new List<(double S0, double S1, Gap Kind)>();
        for (int i = 0; i < segs.Count; i++)
            foreach (var (t0, t1, kind) in Cuts(m, segs[i].A, segs[i].B, towers))
                gaps.Add((starts[i] + t0, starts[i] + t1, kind));
        gaps = Merge(gaps);
        if (f.Closed && gaps.Count > 1 && gaps[0].S0 < 1e-6 && gaps[^1].S1 > total - 1e-6 && gaps[0].Kind == gaps[^1].Kind)
        {
            // A gap across the start of a closed line is one gap.
            var first = gaps[0];
            gaps.RemoveAt(0);
            gaps[^1] = (gaps[^1].S0, total + first.S1, first.Kind);
        }

        CityPt At(double s)
        {
            s = f.Closed ? ((s % total) + total) % total : Math.Clamp(s, 0, total);
            int i = Array.FindLastIndex(starts, v => v <= s + 1e-9);
            i = Math.Clamp(i, 0, segs.Count - 1);
            var (a, b) = segs[i];
            double len = starts[i + 1] - starts[i];
            return len < 1e-9 ? a : a + (b - a) * ((s - starts[i]) / len);
        }
        List<CityPt> Between(double s0, double s1)
        {
            var pts = new List<CityPt> { At(s0) };
            // The corners in between, going round past the start of a closed line.
            for (int lap = 0; lap < 2; lap++)
                for (int i = 1; i <= segs.Count; i++)
                {
                    double s = starts[i] + lap * total;
                    if (i == segs.Count && !f.Closed) continue;
                    if (s > s0 + 1e-6 && s < s1 - 1e-6) pts.Add(At(s));
                }
            pts.Add(At(s1));
            return pts;
        }
        bool OnEdge(CityPt p) => p.X <= 1e-6 || p.Y <= 1e-6 || p.X >= m.Across - 1e-6 || p.Y >= m.Down - 1e-6;
        WallEndKind OwnEnd(CityPt p)
        {
            if (towers.Any(t => UnderTower(t, p, 0.15))) return WallEndKind.Tower;
            if (f.Ruined || OnEdge(p)) return WallEndKind.Open;
            return WallEndKind.Cap;
        }
        WallEndKind GapEnd(Gap g, CityPt p) => g switch
        {
            Gap.Tower => WallEndKind.Tower,
            Gap.Road => f.Ruined ? WallEndKind.Open : WallEndKind.Gate,
            _ => f.Ruined ? WallEndKind.Open : WallEndKind.Cap,
        };
        void Piece(double s0, double s1, WallEndKind k0, WallEndKind k1)
        {
            if (s1 - s0 < 0.05) return;
            var pts = Between(s0, s1);
            pieces.Add(new WallPiece(f, pts, false));
            ends.Add(new WallEnd(f, pts[0], (pts[0] - pts[1]).Unit, k0));
            ends.Add(new WallEnd(f, pts[^1], (pts[^1] - pts[^2]).Unit, k1));
        }

        // A gatehouse over each road through it, unless it's ruined (just a gap then).
        if (!f.Ruined)
            foreach (var (s0, s1, kind) in gaps.Where(g => g.Kind == Gap.Road))
                gates.Add(new WallGate(f, At(s0), At(s1)));
        if (gaps.Count == 0)
        {
            if (f.Closed)
            {
                pieces.Add(new WallPiece(f, [.. f.Points], true));
                return;
            }
            Piece(0, total, OwnEnd(f.Points[0]), OwnEnd(f.Points[^1]));
            return;
        }
        if (f.Closed)
        {
            for (int i = 0; i < gaps.Count; i++)
            {
                var a = gaps[i];
                var b = i + 1 < gaps.Count ? gaps[i + 1] : (S0: gaps[0].S0 + total, S1: gaps[0].S1 + total, Kind: gaps[0].Kind);
                Piece(a.S1, b.S0, GapEnd(a.Kind, At(a.S1)), GapEnd(b.Kind, At(b.S0)));
            }
            return;
        }
        if (gaps[0].S0 > 1e-6) Piece(0, gaps[0].S0, OwnEnd(f.Points[0]), GapEnd(gaps[0].Kind, At(gaps[0].S0)));
        for (int i = 0; i + 1 < gaps.Count; i++)
            Piece(gaps[i].S1, gaps[i + 1].S0, GapEnd(gaps[i].Kind, At(gaps[i].S1)), GapEnd(gaps[i + 1].Kind, At(gaps[i + 1].S0)));
        if (gaps[^1].S1 < total - 1e-6) Piece(gaps[^1].S1, total, GapEnd(gaps[^1].Kind, At(gaps[^1].S1)), OwnEnd(f.Points[^1]));
    }

    /// <summary>Whether a point is under a guard tower (within its footprint and <paramref name="margin"/> more).</summary>
    public static bool UnderTower(Prop t, CityPt p, double margin)
    {
        var q = Geometry.Turn(p, new CityPt(t.X, t.Y), -t.TurnDeg);
        double r = 0.905 * t.Size + margin;
        return Math.Abs(q.X - t.X) < r && Math.Abs(q.Y - t.Y) < r;
    }

    /// <summary>Overlapping gaps joined (the stronger kind kept: a tower over a road, a road over water).</summary>
    static List<(double S0, double S1, Gap Kind)> Merge(List<(double S0, double S1, Gap Kind)> gaps)
    {
        var res = new List<(double S0, double S1, Gap Kind)>();
        foreach (var g in gaps.Where(g => g.S1 - g.S0 > 1e-6).OrderBy(g => g.S0))
        {
            if (res.Count > 0)
            {
                var last = res[^1];
                bool overlap = g.S0 < last.S1 - 1e-6 || (g.S0 <= last.S1 + 1e-6 && g.Kind == last.Kind);
                if (overlap)
                {
                    res[^1] = (last.S0, Math.Max(last.S1, g.S1), (Gap)Math.Max((int)last.Kind, (int)g.Kind));
                    continue;
                }
            }
            res.Add(g);
        }
        return res;
    }

    /// <summary>The gaps in one straight stretch, from a to b (squares along it).</summary>
    static IEnumerable<(double T0, double T1, Gap Kind)> Cuts(CityMap m, CityPt a, CityPt b, List<Prop> towers)
    {
        double len = a.DistanceTo(b);
        if (len < 1e-9) yield break;
        var u = (b - a) * (1 / len);
        var n = new CityPt(-u.Y, u.X);
        // Roads crossing it (not running along it).
        foreach (var r in m.Roads)
            foreach (var (ra, rb) in r.Segments())
            {
                var ru = (rb - ra).Unit;
                if (Math.Abs(u.Dot(ru)) > 0.87) continue;
                if (Geometry.Clip(a, b, Geometry.Strip(ra, rb, r.Width / 2 - 0.01, r.Width / 2 - 0.01)) is { } c && c.T1 - c.T0 > 1e-6)
                    yield return (c.T0 * len, c.T1 * len, Gap.Road);
            }
        // Road painted on the ground both sides of it, or water both sides of it with no bridge.
        int steps = Math.Max(1, (int)Math.Ceiling(len / 0.1));
        double h = len / steps;
        Gap? run = null;
        double from = 0;
        for (int i = 0; i <= steps; i++)
        {
            Gap? here = null;
            if (i < steps)
            {
                double t = (i + 0.5) * h;
                var p = a + u * t;
                var p1 = p + n * 0.3;
                var p2 = p - n * 0.3;
                var g1 = m.GroundAt((int)Math.Floor(p1.X), (int)Math.Floor(p1.Y));
                var g2 = m.GroundAt((int)Math.Floor(p2.X), (int)Math.Floor(p2.Y));
                if (g1 == Ground.Road && g2 == Ground.Road) here = Gap.Road;
                else if (g1 == Ground.Water && g2 == Ground.Water && !m.Roads.Any(r => r.Covers(p.X, p.Y))) here = Gap.Water;
            }
            if (here != run)
            {
                if (run is { } k) yield return (from, i * h, k);
                run = here;
                from = i * h;
            }
        }
        foreach (var t in towers)
        {
            double r = TowerReach(t);
            var c = new CityPt(t.X, t.Y);
            var square = new[] { new CityPt(-r, -r), new CityPt(r, -r), new CityPt(r, r), new CityPt(-r, r) }.Select(q => Geometry.Turn(c + q, c, t.TurnDeg)).ToArray();
            if (Geometry.Clip(a, b, square) is { } cl && cl.T1 - cl.T0 > 1e-6)
                yield return (cl.T0 * len, cl.T1 * len, Gap.Tower);
        }
    }
}
