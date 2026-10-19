using V2 = PhotonCanvas.Doc.Import.Step.Triangulator.V2;

namespace PhotonCanvas.Doc.Import.Step;

/// <summary>A face that can't be turned into triangles; the message says why.</summary>
sealed class FaceException(string message) : Exception(message);

/// <summary>
/// One face: its outline put into the surface's own coordinates (u, v), laid out flat,
/// triangulated, refined until the triangles follow the surface, lifted back to 3D.
/// The outline's points are kept exactly (they're shared with the neighbouring faces).
/// <para>
/// Faces that go right round a surface (a cylinder between its two end circles, a cone up to its
/// tip) are laid out as a ring: the way round becomes the angle about a centre, the other
/// direction the distance from it. Then nothing has to be cut open, holes stay holes, and a tip
/// or pole is the ring's centre.
/// </para>
/// </summary>
sealed class FaceMesher(Surface3 s, bool same, List<List<P3>> loops3, double tol)
{
    readonly List<(double U, double V)> _uv = [];
    readonly List<P3> _pos = [];
    readonly List<List<int>> _loops = [];
    double _su = 1, _sv = 1, _ratio = 1;
    Func<(double U, double V), V2> _map = q => new V2(q.U, q.V);
    Func<V2, (double U, double V), (double U, double V)> _unmap = (p, _) => (p.X, p.Y);
    bool _reversed;

    int Add(double u, double v, P3 p) { _uv.Add((u, v)); _pos.Add(p); return _uv.Count - 1; }

    sealed class Lp
    {
        public List<(double U, double V)> Uv = [];
        public List<P3> P = [];
        /// <summary>Which point of the original outline each one is (a pole's two copies share one).</summary>
        public List<int> Src = [];
        public int Loop, WindU, WindV;
    }

    /// <summary>When planning: the outline steps (loop, step from point n) a ring layout wants divided, and into how many.</summary>
    List<(int Loop, int Seg, int Parts)>? _requests;

    /// <summary>
    /// Lay the face out without meshing it, and say which of its outline's steps need dividing
    /// (so the edge can be divided once, for both faces that share it, before either is meshed).
    /// </summary>
    public List<(int Loop, int Seg, int Parts)> Plan()
    {
        _requests = [];
        try { Layout(); }
        catch (FaceException) { }
        return _requests;
    }

    /// <summary>The outline in the surface's coordinates and laid out flat; false if there's nothing to lay out.</summary>
    bool Layout()
    {
        double pu = s.PeriodU, pv = s.PeriodV;
        var lps = new List<Lp>();
        for (int li = 0; li < loops3.Count; li++)
            if (Flatten(loops3[li], pu, pv) is { } lp) { lp.Loop = li; lps.Add(lp); }
        if (lps.Count == 0 && loops3.Count > 0) return false;
        if (lps.Count > 0) Metric(lps);
        bool wrapU = lps.Any(l => l.WindU != 0), wrapV = lps.Any(l => l.WindV != 0);
        if (lps.Count == 0) Whole(pu, pv);
        else if (wrapU && wrapV) throw new FaceException("Faces wrapping both ways round a surface (a whole torus) aren't supported");
        else if (wrapU) Ring(lps, 0, pu);
        else if (wrapV) Ring(lps, 1, pv);
        else Plain(lps, pu, pv);
        return true;
    }

    public void Mesh(List<(P3, P3, P3)> output)
    {
        if (!Layout()) return;

        var pts = _uv.Select(q => _map(q)).ToList();
        var loops = _loops.Select(l => (IReadOnlyList<int>)l).ToList();
        var tris = Triangulator.Fill(pts, loops);
        if (!s.Flat) Refine(pts, tris, Triangulator.Edges(loops));
        if (StepMesh.Trace is { } trace) Check(trace, pts, loops, tris);
        bool flip = _reversed ^ !same;
        foreach (var (a, b, c) in tris)
        {
            P3 A = _pos[a], B = _pos[b], C = _pos[c];
            if ((B - A).Cross(C - A).Length < 1e-18) continue;
            output.Add(flip ? (A, C, B) : (A, B, C));
        }
    }

    /// <summary>
    /// For looking into a file: says when the triangles don't cover exactly the outline, an edge is
    /// used by more than two of them, or a point strays outside the outline's box.
    /// </summary>
    void Check(Action<string> trace, List<V2> pts, List<IReadOnlyList<int>> loops, List<(int A, int B, int C)> tris)
    {
        var uses = new Dictionary<long, int>();
        foreach (var (x, y, z) in tris)
            foreach (var key in new[] { Triangulator.EdgeKey(x, y), Triangulator.EdgeKey(y, z), Triangulator.EdgeKey(z, x) })
                uses[key] = uses.GetValueOrDefault(key) + 1;
        int over = uses.Count(u => u.Value > 2);
        double want = Math.Abs(Triangulator.EvenOddArea(pts, loops));
        double got = tris.Sum(t => (pts[t.B].X - pts[t.A].X) * (pts[t.C].Y - pts[t.A].Y) - (pts[t.C].X - pts[t.A].X) * (pts[t.B].Y - pts[t.A].Y)) / 2;
        var box = loops3.SelectMany(l => l).ToList();
        double worst = 0;
        if (box.Count > 0)
        {
            double x0 = box.Min(p => p.X), x1 = box.Max(p => p.X), y0 = box.Min(p => p.Y), y1 = box.Max(p => p.Y), z0 = box.Min(p => p.Z), z1 = box.Max(p => p.Z);
            worst = _pos.Max(p => Math.Max(Math.Max(Math.Max(x0 - p.X, p.X - x1), Math.Max(y0 - p.Y, p.Y - y1)), Math.Max(z0 - p.Z, p.Z - z1)));
        }
        if (over > 0 || worst > 0.5 || (s.Flat && Math.Abs(want - got) > 1e-6 * Math.Max(1, want)))
            trace($"  !! {s.GetType().Name}: {over} edges in 3+ triangles, strays {worst:0.##} outside its outline, covers {got:0.###} of {want:0.###} ({_loops.Count} loops, ring {(_reversed ? "reversed" : "kept")})");
    }

    /// <summary>A face with no bounds: all of a closed surface (a whole sphere, say).</summary>
    void Whole(double pu, double pv)
    {
        if (pu > 0 && (pv > 0 || s.Poles.Count >= 2))
        {
            double v0 = pv > 0 ? 0 : s.Poles.Min(), v1 = pv > 0 ? pv : s.Poles.Max();
            var l = new List<int>();
            int n = 32;
            for (int i = 0; i <= n; i++) l.Add(Add(pu * i / n, v0, s.At(pu * i / n, v0)));
            for (int i = 1; i <= n; i++) l.Add(Add(pu, v0 + (v1 - v0) * i / n, s.At(pu, v0 + (v1 - v0) * i / n)));
            for (int i = n - 1; i >= 0; i--) l.Add(Add(pu * i / n, v1, s.At(pu * i / n, v1)));
            for (int i = n - 1; i >= 1; i--) l.Add(Add(0, v0 + (v1 - v0) * i / n, s.At(0, v0 + (v1 - v0) * i / n)));
            _loops.Add(l);
            return;
        }
        throw new FaceException("Faces with no outline on open surfaces aren't supported");
    }

    /// <summary>The loop in surface parameters: continuous round the closed directions, poles split in two.</summary>
    Lp? Flatten(List<P3> l3, double pu, double pv)
    {
        int n = l3.Count;
        if (n < 2) return null;
        var raw = new (double U, double V)[n];
        var pole = new bool[n];
        (double, double)? hint = null;
        for (int i = 0; i < n; i++)
        {
            raw[i] = s.Uv(l3[i], hint);
            hint = raw[i];
        }
        // A surface through itself: of each point's places on it, the run that changes least from point to point.
        var cands = s.SelfCrossing ? l3.Select(s.Candidates).ToList() : null;
        if (cands is not null && cands.Any(c => c.Count > 1))
        {
            double Step((double U, double V) a, (double U, double V) b) => Math.Abs(Wrap(b.U - a.U, pu)) + Math.Abs(Wrap(b.V - a.V, pv));
            var cost = new double[n][];
            var from = new int[n][];
            cost[0] = new double[cands[0].Count];
            from[0] = new int[cands[0].Count];
            for (int i = 1; i < n; i++)
            {
                cost[i] = new double[cands[i].Count];
                from[i] = new int[cands[i].Count];
                for (int k = 0; k < cands[i].Count; k++)
                {
                    cost[i][k] = double.MaxValue;
                    for (int j = 0; j < cands[i - 1].Count; j++)
                    {
                        double c = cost[i - 1][j] + Step(cands[i - 1][j], cands[i][k]);
                        if (c < cost[i][k]) { cost[i][k] = c; from[i][k] = j; }
                    }
                }
            }
            int pick = Array.IndexOf(cost[n - 1], cost[n - 1].Min());
            for (int i = n - 1; i >= 0; i--)
            {
                raw[i] = cands[i][pick];
                if (i > 0) pick = from[i][pick];
            }
        }
        for (int i = 0; i < n; i++) pole[i] = AtPole(raw[i]);
        if (pole.All(x => x)) return null;
        int first = Array.IndexOf(pole, false);
        var uv = new (double U, double V)[n];
        uv[first] = raw[first];
        int last = first;
        for (int k = 1; k < n; k++)
        {
            int i = (first + k) % n;
            if (pole[i]) continue;
            uv[i] = (uv[last].U + Wrap(raw[i].U - uv[last].U, pu), uv[last].V + Wrap(raw[i].V - uv[last].V, pv));
            last = i;
        }
        var lp = new Lp();
        // Back round to the start: the total says how many times it went round.
        if (pu > 0) lp.WindU = (int)Math.Round((uv[last].U + Wrap(uv[first].U - uv[last].U, pu) - uv[first].U) / pu);
        if (pv > 0) lp.WindV = (int)Math.Round((uv[last].V + Wrap(uv[first].V - uv[last].V, pv) - uv[first].V) / pv);
        int Pos(int j) => (j - first + n) % n;
        for (int k = 0; k < n; k++)
        {
            int i = (first + k) % n;
            if (!pole[i]) { lp.Uv.Add(uv[i]); lp.P.Add(l3[i]); lp.Src.Add(i); continue; }
            // A pole: u means nothing there, so it's two points, at its neighbours' u (the same place in 3D).
            int pi = (i + n - 1) % n, ni = (i + 1) % n;
            while (pole[pi] && pi != i) pi = (pi + n - 1) % n;
            while (pole[ni] && ni != i) ni = (ni + 1) % n;
            double up = uv[pi].U, un = uv[ni].U;
            if (Pos(ni) < Pos(i)) un += lp.WindU * pu;
            if (Pos(pi) > Pos(i)) up -= lp.WindU * pu;
            lp.Uv.Add((up, raw[i].V));
            lp.P.Add(l3[i]);
            lp.Src.Add(i);
            if (Math.Abs(un - up) > 1e-12) { lp.Uv.Add((un, raw[i].V)); lp.P.Add(l3[i]); lp.Src.Add(i); }
        }
        return lp.Uv.Count >= 2 ? lp : null;
    }

    static double Wrap(double d, double p) => p > 0 ? d - p * Math.Round(d / p) : d;

    bool AtPole((double U, double V) q)
    {
        if (s.Poles.Count == 0)
        {
            if (s is not BSplineSurface) return false;
            // A spline squeezed to a point along u (a "degenerate" edge).
            var a = s.At(q.U - 1e-3, q.V);
            var b = s.At(q.U + 1e-3, q.V);
            var c = s.At(q.U, q.V + 1e-3);
            var d = s.At(q.U, q.V - 1e-3);
            return (a - b).Length < 1e-9 && (c - d).Length > 1e-7;
        }
        return s.Poles.Any(p => Math.Abs(Wrap(q.V - p, s.PeriodV)) < 1e-7);
    }

    /// <summary>
    /// How long a step in u and v is on the surface, so the flat layout keeps shapes; then
    /// stretched so the direction that bends less (along a cylinder) gets longer triangles.
    /// </summary>
    void Metric(List<Lp> lps)
    {
        double su = 0, sv = 0;
        int n = 0;
        foreach (var lp in lps)
            for (int i = 0; i < lp.Uv.Count; i += Math.Max(1, lp.Uv.Count / 16))
            {
                var (u, v) = lp.Uv[i];
                const double h = 1e-5;
                double a = (s.At(u + h, v) - s.At(u - h, v)).Length / (2 * h);
                double b = (s.At(u, v + h) - s.At(u, v - h)).Length / (2 * h);
                if (a < 1e-9 || b < 1e-9) continue;
                su += a; sv += b; n++;
            }
        if (n > 0) { _su = su / n; _sv = sv / n; }
        if (s.Flat || n == 0) return;
        double ku = 0, kv = 0;
        var pts = lps.SelectMany(l => l.Uv).ToList();
        double eu = Math.Max(1e-9, pts.Max(q => q.U) - pts.Min(q => q.U)), ev = Math.Max(1e-9, pts.Max(q => q.V) - pts.Min(q => q.V));
        double hu = eu * 1e-3, hv = ev * 1e-3;
        for (int i = 0; i < pts.Count; i += Math.Max(1, pts.Count / 24))
        {
            var (u, v) = pts[i];
            var c = s.At(u, v);
            var du = (s.At(u + hu, v) - s.At(u - hu, v)) / (2 * hu);
            var dv = (s.At(u, v + hv) - s.At(u, v - hv)) / (2 * hv);
            if (du.Length < 1e-9 || dv.Length < 1e-9) continue;
            var nrm = du.Cross(dv).Unit;
            var duu = (s.At(u + hu, v) - c * 2 + s.At(u - hu, v)) / (hu * hu);
            var dvv = (s.At(u, v + hv) - c * 2 + s.At(u, v - hv)) / (hv * hv);
            ku = Math.Max(ku, Math.Abs(duu.Dot(nrm)) / du.Dot(du));
            kv = Math.Max(kv, Math.Abs(dvv.Dot(nrm)) / dv.Dot(dv));
        }
        // The step that keeps a chord within tolerance on that bend (as long as the face on a straight).
        double size = Math.Max(eu * _su, ev * _sv);
        double Step(double k) => k < 1e-9 ? size : Math.Min(size, Math.Sqrt(8 * tol / k));
        double ratio = Math.Clamp(Step(kv) / Step(ku), 1.0 / 60, 60);
        _sv /= ratio;
        _ratio = ratio;
    }

    /// <summary>Loops that don't go round the surface: each brought into one period, laid out as they are.</summary>
    void Plain(List<Lp> lps, double pu, double pv)
    {
        var reference = lps.MaxBy(l => (l.Uv.Max(q => q.U) - l.Uv.Min(q => q.U)) * (l.Uv.Max(q => q.V) - l.Uv.Min(q => q.V)))!;
        double u0 = reference.Uv.Min(q => q.U), v0 = reference.Uv.Min(q => q.V);
        foreach (var lp in lps)
        {
            double du = 0, dv = 0;
            if (pu > 0 && lp != reference) du = -pu * Math.Floor((lp.Uv.Average(q => q.U) - u0) / pu);
            if (pv > 0 && lp != reference) dv = -pv * Math.Floor((lp.Uv.Average(q => q.V) - v0) / pv);
            _loops.Add([.. lp.Uv.Select((q, i) => Add(q.U + du, q.V + dv, lp.P[i]))]);
        }
        double su = _su, sv = _sv;
        _map = q => new V2(q.U * su, q.V * sv);
        _unmap = (p, _) => (p.X / su, p.Y / sv);
    }

    /// <summary>
    /// Loops that go round the surface, laid out as a ring: the closed direction (a) becomes the
    /// angle, the other (b) the distance from the centre. Two or more loops make an annulus; a
    /// single one is closed at a tip or pole, which goes at the centre.
    /// </summary>
    void Ring(List<Lp> lps, int axis, double period)
    {
        double A((double U, double V) q) => axis == 0 ? q.U : q.V;
        double B((double U, double V) q) => axis == 0 ? q.V : q.U;
        // Squeezed less than a plain layout: the more a ring is squeezed, the more points its outlines need (see below).
        double sa = axis == 0 ? _su : _sv * _ratio, sb = axis == 0 ? _sv * _ratio / Math.Min(_ratio, 8) : _su;
        // How long a unit of b is on the surface (mm), before any squeezing.
        double mb = axis == 0 ? _sv * _ratio : _su;
        var wraps = lps.Where(l => (axis == 0 ? l.WindU : l.WindV) != 0).ToList();
        double bMin = lps.Min(l => l.Uv.Min(B)), bMax = lps.Max(l => l.Uv.Max(B));
        // ρ as a function of b, and back.
        Func<double, double> rho, back;
        // The range a point may have across the ring (a cap reaches its pole).
        double lo = bMin, hi = bMax;
        bool outward;
        if (wraps.Count == 1)
        {
            // One loop round: the face goes from it to a pole, on its inside (the left of its direction, seen with the face's normal towards you).
            var w = wraps[0];
            int dir = axis == 0 ? w.WindU : w.WindV;
            int inside = (axis == 0 ? dir : -dir) * (same ? 1 : -1);
            // Holes in the face are on its inside, whatever the flags say (some programs get them backwards).
            var holes = lps.Where(l => l != w).ToList();
            if (holes.Count > 0) inside = holes.Average(l => l.Uv.Average(B)) > w.Uv.Average(B) ? 1 : -1;
            var poles = axis == 0 ? s.Poles : [];
            double? pole = inside > 0 ? poles.Where(p => p >= bMax - 1e-9).Cast<double?>().Min() : poles.Where(p => p <= bMin + 1e-9).Cast<double?>().Max();
            // Flags that don't add up: the only pole there is.
            pole ??= poles.Where(p => p >= bMax - 1e-9).Cast<double?>().Min() ?? poles.Where(p => p <= bMin + 1e-9).Cast<double?>().Max();
            if (pole is not { } pb) throw new FaceException("A face going round its surface has nowhere to close");
            (lo, hi) = (Math.Min(bMin, pb), Math.Max(bMax, pb));
            if (pb > bMin)
            {
                rho = b => (pb - b) * sb;
                back = r => pb - r / sb;
                outward = false;
            }
            else
            {
                rho = b => (b - pb) * sb;
                back = r => pb + r / sb;
                outward = true;
            }
        }
        else
        {
            // An annulus: start at the ring's own radius, so its shape is kept.
            double r0 = period * sa / (2 * Math.PI);
            if (bMax - bMin < 1e-12) throw new FaceException("A face going round its surface has no width");
            rho = b => r0 + (b - bMin) * sb;
            back = r => bMin + (r - r0) / sb;
            outward = true;
        }
        StepMesh.Trace?.Invoke($"  ring: axis {axis}, {wraps.Count} of {lps.Count} loops go round ({string.Join(" ", lps.Select(l => $"[{(axis == 0 ? l.WindU : l.WindV)} b {l.Uv.Min(B):0.###}..{l.Uv.Max(B):0.###}]"))}), ρ {rho(bMin):0.###}..{rho(bMax):0.###}, sb {sb:0.####}");
        double k = 2 * Math.PI / period;
        _map = q =>
        {
            double t = A(q) * k, r = rho(B(q));
            return new V2(r * Math.Cos(t), r * Math.Sin(t));
        };
        _unmap = (p, near) =>
        {
            double t = Math.Atan2(p.Y, p.X) / k;
            // The same place on the surface whichever way round; keep it near its neighbours anyway.
            double a0 = A(near);
            t += period * Math.Round((a0 - t) / period);
            // Never off the face's own range (a point in a chord's dip would be a hair past its edge).
            double b = Math.Clamp(back(Math.Sqrt(p.X * p.X + p.Y * p.Y)), lo, hi);
            return axis == 0 ? (t, b) : (b, t);
        };
        // Laid round a centre, (angle, distance) turns the other way from (x, y): the layout keeps (a, b)'s turn only if ρ shrinks as b grows; (u, v) is (a, b), or (b, a) when the closed direction is v.
        _reversed = outward ^ (axis == 1);
        // The outlines, with points added along their straight pieces where the ring bends them: laid
        // round a circle, a straight step becomes a chord that dips toward the centre, and the dip
        // means a step off the end of the face. The added points lie exactly on the original
        // straight edge in 3D, so the neighbouring face still meets this one.
        foreach (var lp in lps)
        {
            var loop = new List<int>();
            int n = lp.Uv.Count;
            for (int i = 0; i < n; i++)
            {
                var q0 = lp.Uv[i];
                var q1 = i + 1 < n ? lp.Uv[i + 1] : lp.Uv[0];
                loop.Add(Add(q0.U, q0.V, lp.P[i]));
                // The closing step of a loop that goes round ends a period on from its start.
                double da = A(q1) - A(q0);
                da -= period * Math.Round(da / period);
                double r = Math.Max(rho(B(q0)), rho(B(q1)));
                if (r < 1e-12) continue;
                // A chord across angle φ dips r(1 − cos φ/2) ≈ rφ²/8 on the layout: that's dip / sb across the ring
                // in b, and that times b's own length per unit on the surface; kept to half the tolerance.
                double maxAngle = Math.Sqrt(8 * (tol / 2) * sb / (r * mb));
                int parts = (int)Math.Min(512, Math.Ceiling(Math.Abs(da * k) / maxAngle));
                // Planning: ask for the step to be divided at the edge (a little finer, so this face needs nothing more when meshed).
                if (_requests is not null && parts > 1 && lp.Src[(i + 1) % n] == (lp.Src[i] + 1) % loops3[lp.Loop].Count)
                    _requests.Add((lp.Loop, lp.Src[i], (int)Math.Min(512, Math.Ceiling(parts * 1.25))));
                for (int j = 1; j < parts; j++)
                {
                    double t = (double)j / parts;
                    double a = A(q0) + da * t, b = B(q0) + (B(q1) - B(q0)) * t;
                    var q = axis == 0 ? (a, b) : (b, a);
                    loop.Add(Add(q.Item1, q.Item2, P3.Lerp(lp.P[i], lp.P[(i + 1) % n], t)));
                }
            }
            _loops.Add(loop);
        }
    }

    /// <summary>Delaunay flips, then split edges (and triangles) that stray from the surface, until they all follow it.</summary>
    void Refine(List<V2> pts, List<(int A, int B, int C)> tris, HashSet<long> fixedEdges)
    {
        Triangulator.Flip(pts, tris, fixedEdges);
        int budget = Math.Max(20000, _uv.Count * 40);
        for (int pass = 0; pass < 16 && _uv.Count < budget; pass++)
        {
            var edgeTris = new Dictionary<long, List<int>>();
            for (int t = 0; t < tris.Count; t++) Remember(edgeTris, tris[t], t);
            var splits = new List<(int A, int B, double Sag)>();
            foreach (var (key, _) in edgeTris)
            {
                if (fixedEdges.Contains(key)) continue;
                int a = (int)(key >> 32), b = (int)(key & 0xffffffff);
                double sag = Sag(pts, a, b);
                if (sag > tol) splits.Add((a, b, sag));
            }
            var touched = new HashSet<int>(splits.SelectMany(e => edgeTris[Triangulator.EdgeKey(e.A, e.B)]));
            var centres = new List<int>();
            for (int t = 0; t < tris.Count; t++)
            {
                if (touched.Contains(t)) continue;
                var (a, b, c) = tris[t];
                var (q, _) = Centre(pts, a, b, c);
                if ((s.At(q.U, q.V) - (_pos[a] + _pos[b] + _pos[c]) / 3).Length > tol) centres.Add(t);
            }
            StepMesh.Trace?.Invoke($"  pass {pass}: {_uv.Count} points, {tris.Count} triangles, {splits.Count} edges to split (worst {(splits.Count > 0 ? splits.Max(e => e.Sag) : 0):0.###}), {centres.Count} centres");
            if (splits.Count == 0 && centres.Count == 0) break;
            // One at a time (each split keeps the map of edges to triangles right), worst first.
            foreach (var (a, b, _) in splits.OrderByDescending(e => e.Sag))
            {
                if (_uv.Count >= budget) break;
                long key = Triangulator.EdgeKey(a, b);
                if (!edgeTris.TryGetValue(key, out var ts) || ts.Count == 0) continue;
                var mid = new V2((pts[a].X + pts[b].X) / 2, (pts[a].Y + pts[b].Y) / 2);
                var q = _unmap(mid, _uv[a]);
                int m = Add(q.U, q.V, s.At(q.U, q.V));
                pts.Add(mid);
                foreach (var t in ts.ToList())
                {
                    var (x, y, z) = Rotate(tris[t], a, b);
                    Forget(edgeTris, tris[t], t);
                    tris[t] = (x, m, z);
                    tris.Add((m, y, z));
                    Remember(edgeTris, tris[t], t);
                    Remember(edgeTris, tris[^1], tris.Count - 1);
                }
            }
            foreach (var t in centres)
            {
                if (_uv.Count >= budget) break;
                var (a, b, c) = tris[t];
                var (q, mid) = Centre(pts, a, b, c);
                int m = Add(q.U, q.V, s.At(q.U, q.V));
                pts.Add(mid);
                tris[t] = (a, b, m);
                tris.Add((b, c, m));
                tris.Add((c, a, m));
            }
            Triangulator.Flip(pts, tris, fixedEdges);
        }
    }

    /// <summary>How far the middle of an edge is from the surface.</summary>
    double Sag(List<V2> pts, int a, int b)
    {
        var q = _unmap(new V2((pts[a].X + pts[b].X) / 2, (pts[a].Y + pts[b].Y) / 2), _uv[a]);
        return (s.At(q.U, q.V) - (_pos[a] + _pos[b]) / 2).Length;
    }

    ((double U, double V) Uv, V2 Flat) Centre(List<V2> pts, int a, int b, int c)
    {
        var mid = new V2((pts[a].X + pts[b].X + pts[c].X) / 3, (pts[a].Y + pts[b].Y + pts[c].Y) / 3);
        return (_unmap(mid, _uv[a]), mid);
    }

    /// <summary>The triangle turned so it starts with the edge a–b (in whichever direction it has it).</summary>
    static (int, int, int) Rotate((int A, int B, int C) t, int a, int b)
    {
        if (t.A == a && t.B == b || t.A == b && t.B == a) return (t.A, t.B, t.C);
        if (t.B == a && t.C == b || t.B == b && t.C == a) return (t.B, t.C, t.A);
        return (t.C, t.A, t.B);
    }

    static void Forget(Dictionary<long, List<int>> map, (int A, int B, int C) t, int id)
    {
        foreach (var k in new[] { Triangulator.EdgeKey(t.A, t.B), Triangulator.EdgeKey(t.B, t.C), Triangulator.EdgeKey(t.C, t.A) })
            if (map.TryGetValue(k, out var l)) l.Remove(id);
    }

    static void Remember(Dictionary<long, List<int>> map, (int A, int B, int C) t, int id)
    {
        foreach (var k in new[] { Triangulator.EdgeKey(t.A, t.B), Triangulator.EdgeKey(t.B, t.C), Triangulator.EdgeKey(t.C, t.A) })
        {
            if (!map.TryGetValue(k, out var l)) map[k] = l = [];
            l.Add(id);
        }
    }
}
