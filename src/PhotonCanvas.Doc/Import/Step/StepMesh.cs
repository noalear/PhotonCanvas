using V2 = PhotonCanvas.Doc.Import.Step.Triangulator.V2;

namespace PhotonCanvas.Doc.Import.Step;

/// <summary>What reading a STEP file gave.</summary>
/// <param name="Triangles">The model, in millimetres, triangles facing outwards.</param>
/// <param name="Faces">Faces in the file's solids.</param>
/// <param name="FailedFaces">Faces that couldn't be turned into triangles (left out).</param>
/// <param name="Solids">Solids (and surface bodies) placed, counting each copy in an assembly.</param>
/// <param name="Problems">What went wrong, one line per kind.</param>
public sealed record StepResult(List<(P3 A, P3 B, P3 C)> Triangles, int Faces, int FailedFaces, int Solids, IReadOnlyList<string> Problems);

/// <summary>
/// Turns the exact shapes in a STEP file (planes, cylinders, cones, spheres, tori, swept curves,
/// NURBS) into triangles within a set distance of the true surface. Each face is laid out flat in
/// its surface's own coordinates, its outline triangulated there, and the triangles refined until
/// they follow the surface. Edges shared by two faces are divided once and used by both, so the
/// faces meet without cracks. Assemblies are put together and units turned into millimetres.
/// </summary>
public static class StepMesh
{
    /// <exception cref="FormatException">Not a STEP file, or no shapes in it.</exception>
    public static StepResult Load(string path, double toleranceMm = 0.05, CancellationToken ct = default) =>
        Mesh(StepFile.Load(path), toleranceMm, ct);

    /// <summary>For looking into a file: a line per face.</summary>
    public static Action<string>? Trace;

    public static StepResult Mesh(StepFile file, double toleranceMm = 0.05, CancellationToken ct = default)
    {
        try { return new Builder(file, Math.Clamp(toleranceMm, 0.005, 5), ct).Run(); }
        catch (Exception e) when (e is not (FormatException or OperationCanceledException or OutOfMemoryException))
        {
            // Something in the file this reader didn't expect: say so rather than fall over.
            throw new FormatException($"This STEP file couldn't be read ({e.GetType().Name}: {e.Message}).", e);
        }
    }

    sealed class Builder(StepFile f, double tolMm, CancellationToken ct)
    {
        readonly Dictionary<int, Curve3?> _curves = [];
        readonly Dictionary<int, Surface3?> _surfaces = [];
        readonly Dictionary<int, List<P3>> _edges = [];
        readonly Dictionary<int, List<(P3, P3, P3)>> _solids = [];
        readonly Dictionary<string, int> _problems = [];
        double _len = 1, _angle = 1, _tol = 0.05;
        int _faces, _failed, _placed;

        void Problem(string what) => _problems[what] = _problems.GetValueOrDefault(what) + 1;

        public StepResult Run()
        {
            (_len, _angle) = FileUnits();
            _tol = tolMm / _len;
            var tris = new List<(P3, P3, P3)>();
            foreach (var (item, xf) in Instances())
            {
                ct.ThrowIfCancellationRequested();
                var solid = Solid(item);
                if (solid.Count == 0) continue;
                _placed++;
                var all = Xform.Identity.Then(xf);
                bool mirror = all.Mirrors;
                foreach (var (a, b, c) in solid)
                {
                    P3 A = all.Apply(a) * _len, B = all.Apply(b) * _len, C = all.Apply(c) * _len;
                    tris.Add(mirror ? (A, C, B) : (A, B, C));
                }
            }
            if (tris.Count == 0 && _faces == 0) throw new FormatException("There are no solid shapes in this STEP file.");
            var problems = _problems.OrderByDescending(p => p.Value).Select(p => p.Value == 1 ? p.Key : $"{p.Key} (×{p.Value})").ToList();
            return new StepResult(tris, _faces, _failed, _placed, problems);
        }

        // ---- reading values ------------------------------------------------------------------

        static double D(object? o) => o switch
        {
            double d => d,
            StepTyped t when t.Args.Count > 0 => D(t.Args[0]),
            _ => 0,
        };
        static IReadOnlyList<object?> L(object? o) => o as IReadOnlyList<object?> ?? [];
        static bool B(object? o) => o is StepEnum { Name: "T" or "TRUE" };
        StepEntity? E(object? o) => f.Get(o);

        P3 Point(object? o)
        {
            var e = E(o);
            var c = e?.Of("CARTESIAN_POINT") is { } a ? L(a[1]) : L(e?.Of("VERTEX_POINT") is { } v ? E(v[1])?.Of("CARTESIAN_POINT")?[1] : null);
            return new P3(c.Count > 0 ? D(c[0]) : 0, c.Count > 1 ? D(c[1]) : 0, c.Count > 2 ? D(c[2]) : 0);
        }

        P3? Dir(object? o)
        {
            var e = E(o);
            if (e?.Of("DIRECTION") is not { } a) return null;
            var c = L(a[1]);
            return new P3(c.Count > 0 ? D(c[0]) : 0, c.Count > 1 ? D(c[1]) : 0, c.Count > 2 ? D(c[2]) : 0);
        }

        Frame Place(object? o)
        {
            var e = E(o);
            if (e?.Of("AXIS2_PLACEMENT_3D") is { } a) return Frame.Of(Point(a[1]), Dir(a[2]), Dir(a[3]));
            if (e?.Of("AXIS1_PLACEMENT") is { } b) return Frame.Of(Point(b[1]), Dir(b[2]), null);
            return Frame.Identity;
        }

        // ---- units ---------------------------------------------------------------------------

        /// <summary>Millimetres per model unit and radians per angle unit, from the first context that says.</summary>
        (double Len, double Angle) FileUnits()
        {
            double len = 0, ang = 0;
            foreach (var e in f.All)
            {
                if (e.Of("GLOBAL_UNIT_ASSIGNED_CONTEXT") is not { } g) continue;
                foreach (var u in L(g[0]))
                {
                    var unit = E(u);
                    if (unit is null) continue;
                    if (len == 0 && unit.Is("LENGTH_UNIT")) len = Length(unit);
                    if (ang == 0 && unit.Is("PLANE_ANGLE_UNIT")) ang = Angle(unit);
                }
                if (len > 0 && ang > 0) break;
            }
            return (len > 0 ? len : 1, ang > 0 ? ang : 1);
        }

        double Length(StepEntity unit)
        {
            if (unit.Of("SI_UNIT") is { } si)
            {
                double k = Prefix(si.Count > 0 ? si[0] : null);
                return 1000 * k;
            }
            if (unit.Of("CONVERSION_BASED_UNIT") is { } cb && E(cb[1]) is { } m && m.Of("LENGTH_MEASURE_WITH_UNIT") is { } lm)
                return D(lm[0]) * (E(lm[1]) is { } inner ? Length(inner) : 1);
            return 1;
        }

        double Angle(StepEntity unit)
        {
            if (unit.Of("SI_UNIT") is not null) return 1;
            if (unit.Of("CONVERSION_BASED_UNIT") is { } cb && E(cb[1]) is { } m && m.Of("PLANE_ANGLE_MEASURE_WITH_UNIT") is { } am)
                return D(am[0]) * (E(am[1]) is { } inner ? Angle(inner) : 1);
            return 1;
        }

        static double Prefix(object? p) => (p as StepEnum?)?.Name switch
        {
            "MILLI" => 1e-3, "CENTI" => 1e-2, "DECI" => 1e-1, "KILO" => 1e3, "MICRO" => 1e-6, "NANO" => 1e-9,
            _ => 1,
        };

        // ---- curves and surfaces ---------------------------------------------------------------

        Curve3? Curve(object? o)
        {
            var e = E(o);
            if (e is null) return null;
            if (_curves.TryGetValue(e.Id, out var have)) return have;
            Curve3? c = null;
            try { c = MakeCurve(e); }
            catch (Exception ex) when (ex is FormatException or IndexOutOfRangeException or ArgumentException or InvalidCastException) { Problem($"Unreadable curve ({ex.Message})"); }
            _curves[e.Id] = c;
            return c;
        }

        Curve3? MakeCurve(StepEntity e)
        {
            if (e.Of("LINE") is { } ln && E(ln[2])?.Of("VECTOR") is { } vec)
                return new LineCurve(Point(ln[1]), Dir(vec[1]) ?? new P3(1, 0, 0));
            if (e.Of("CIRCLE") is { } ci) return new CircleCurve(Place(ci[1]), D(ci[2]));
            if (e.Of("ELLIPSE") is { } el) return new EllipseCurve(Place(el[1]), D(el[2]), D(el[3]));
            if (e.Of("SURFACE_CURVE") is { } sc) return Curve(sc[1]);
            if (e.Of("SEAM_CURVE") is { } sm) return Curve(sm[1]);
            if (e.Of("TRIMMED_CURVE") is { } tc) return Curve(tc[1]);
            if (e.Of("POLYLINE") is { } pl) return new PolylineCurve([.. L(pl[1]).Select(Point)]);
            if (e.Type == "B_SPLINE_CURVE_WITH_KNOTS")
            {
                var a = e.Args;
                return new BSplineCurve((int)D(a[1]), [.. L(a[2]).Select(Point)], null, BSpline.Expand([.. L(a[7]).Select(D)], [.. L(a[6]).Select(D)]));
            }
            if (e.Of("B_SPLINE_CURVE") is { } bc && e.Of("B_SPLINE_CURVE_WITH_KNOTS") is { } bk)
            {
                var w = e.Of("RATIONAL_B_SPLINE_CURVE") is { } rw ? L(rw[0]).Select(D).ToList() : null;
                return new BSplineCurve((int)D(bc[0]), [.. L(bc[1]).Select(Point)], w, BSpline.Expand([.. L(bk[1]).Select(D)], [.. L(bk[0]).Select(D)]));
            }
            Problem($"Curve type {(e.Type.Length > 0 ? e.Type : string.Join("+", e.Parts!.Keys))} isn't supported");
            return null;
        }

        Surface3? Surf(object? o)
        {
            var e = E(o);
            if (e is null) return null;
            if (_surfaces.TryGetValue(e.Id, out var have)) return have;
            Surface3? s = null;
            try { s = MakeSurface(e); }
            catch (Exception ex) when (ex is FormatException or IndexOutOfRangeException or ArgumentException or InvalidCastException) { Problem($"Unreadable surface ({ex.Message})"); }
            _surfaces[e.Id] = s;
            return s;
        }

        Surface3? MakeSurface(StepEntity e)
        {
            switch (e.Type)
            {
                case "PLANE": return new PlaneSurface(Place(e.Args[1]));
                case "CYLINDRICAL_SURFACE": return new CylinderSurface(Place(e.Args[1]), D(e.Args[2]));
                case "CONICAL_SURFACE": return new ConeSurface(Place(e.Args[1]), D(e.Args[2]), D(e.Args[3]) * _angle);
                case "SPHERICAL_SURFACE": return new SphereSurface(Place(e.Args[1]), D(e.Args[2]));
                case "TOROIDAL_SURFACE":
                case "DEGENERATE_TOROIDAL_SURFACE": return new TorusSurface(Place(e.Args[1]), D(e.Args[2]), D(e.Args[3]));
                case "SURFACE_OF_LINEAR_EXTRUSION":
                    {
                        var c = Curve(e.Args[1]);
                        var vec = E(e.Args[2])?.Of("VECTOR");
                        if (c is null || vec is null) return null;
                        return new ExtrusionSurface(c, Dir(vec[1]) ?? new P3(0, 0, 1));
                    }
                case "SURFACE_OF_REVOLUTION":
                    {
                        var c = Curve(e.Args[1]);
                        return c is null ? null : new RevolutionSurface(c, Place(e.Args[2]));
                    }
                case "B_SPLINE_SURFACE_WITH_KNOTS":
                    {
                        var a = e.Args;
                        var grid = L(a[3]).Select(row => (IReadOnlyList<P3>)L(row).Select(Point).ToList()).ToList();
                        return new BSplineSurface((int)D(a[1]), (int)D(a[2]), grid, null,
                            BSpline.Expand([.. L(a[10]).Select(D)], [.. L(a[8]).Select(D)]), BSpline.Expand([.. L(a[11]).Select(D)], [.. L(a[9]).Select(D)]));
                    }
            }
            if (e.Of("B_SPLINE_SURFACE") is { } bs && e.Of("B_SPLINE_SURFACE_WITH_KNOTS") is { } bk)
            {
                var grid = L(bs[2]).Select(row => (IReadOnlyList<P3>)L(row).Select(Point).ToList()).ToList();
                var w = e.Of("RATIONAL_B_SPLINE_SURFACE") is { } rw ? L(rw[0]).Select(row => (IReadOnlyList<double>)L(row).Select(D).ToList()).ToList() : null;
                return new BSplineSurface((int)D(bs[0]), (int)D(bs[1]), grid, w,
                    BSpline.Expand([.. L(bk[2]).Select(D)], [.. L(bk[0]).Select(D)]), BSpline.Expand([.. L(bk[3]).Select(D)], [.. L(bk[1]).Select(D)]));
            }
            Problem($"Surface type {(e.Type.Length > 0 ? e.Type : string.Join("+", e.Parts!.Keys))} isn't supported");
            return null;
        }

        // ---- edges -----------------------------------------------------------------------------

        /// <summary>An edge as points from its start to its end, divided once (shared by both its faces).</summary>
        List<P3>? Edge(StepEntity ec)
        {
            if (_edges.TryGetValue(ec.Id, out var have)) return have;
            var a = ec.Of("EDGE_CURVE");
            if (a is null) return null;
            P3 p1 = Point(a[1]), p2 = Point(a[2]);
            bool sense = a.Count <= 4 || B(a[4]);
            var curve = Curve(a[3]);
            List<P3> pts;
            if (curve is null) pts = [p1, p2];
            else if (curve.Straight) pts = [p1, p2];
            else pts = Sample(curve, p1, p2, sense);
            _edges[ec.Id] = pts;
            return pts;
        }

        List<P3> Sample(Curve3 c, P3 p1, P3 p2, bool sense)
        {
            double t0 = c.Project(p1), t1 = c.Project(p2);
            bool closed = (p1 - p2).Length < Math.Max(1e-9, _tol * 1e-3);
            if (c.Period > 0)
            {
                double per = c.Period;
                if (sense)
                {
                    while (t1 <= t0 + 1e-12) t1 += per;
                    while (t1 > t0 + per + 1e-12) t1 -= per;
                    if (closed) t1 = t0 + per;
                }
                else
                {
                    while (t1 >= t0 - 1e-12) t1 -= per;
                    while (t1 < t0 - per - 1e-12) t1 += per;
                    if (closed) t1 = t0 - per;
                }
            }
            else if (closed && c.Range is var (r0, r1) && !double.IsInfinity(r0))
            {
                // A closed curve that isn't periodic: the whole of it.
                (t0, t1) = sense ? (r0, r1) : (r1, r0);
            }
            var pts = new List<P3> { p1 };
            int first = c switch
            {
                CircleCurve => (int)Math.Ceiling(Math.Abs(t1 - t0) / (Math.PI / 4)),
                EllipseCurve => (int)Math.Ceiling(Math.Abs(t1 - t0) / (Math.PI / 4)),
                BSplineCurve b => Math.Max(2, b.Spans * 2),
                PolylineCurve => Math.Max(1, (int)Math.Ceiling(Math.Abs(t1 - t0))),
                _ => 4,
            };
            first = Math.Clamp(first, 1, 4096);
            var prev = (T: t0, P: p1);
            for (int i = 1; i <= first; i++)
            {
                double t = t0 + (t1 - t0) * i / first;
                var p = i == first ? p2 : c.At(t);
                Divide(c, prev.T, prev.P, t, p, pts, 0);
                prev = (t, p);
            }
            return pts;
        }

        /// <summary>Halve the piece until its middle is within tolerance of the straight line (adds the end point, not the start).</summary>
        void Divide(Curve3 c, double ta, P3 pa, double tb, P3 pb, List<P3> pts, int depth)
        {
            double tm = (ta + tb) / 2;
            var pm = c.At(tm);
            if (depth < 16 && (pm - (pa + pb) / 2).Length > _tol)
            {
                Divide(c, ta, pa, tm, pm, pts, depth + 1);
                Divide(c, tm, pm, tb, pb, pts, depth + 1);
                return;
            }
            pts.Add(pb);
        }

        // ---- solids and faces ------------------------------------------------------------------

        /// <summary>A solid's (or surface body's) triangles, in its own coordinates, made once however often it's placed.</summary>
        List<(P3, P3, P3)> Solid(StepEntity item)
        {
            if (_solids.TryGetValue(item.Id, out var have)) return have;
            var tris = new List<(P3, P3, P3)>();
            var shells = new List<(StepEntity Shell, bool Flip)>();
            bool closed = false;
            void AddShell(object? o, bool flip)
            {
                var s = E(o);
                if (s is null) return;
                if (s.Of("ORIENTED_CLOSED_SHELL") is { } oc) { AddShell(oc[2], flip ^ !B(oc[3])); return; }
                if (s.Of("ORIENTED_OPEN_SHELL") is { } oo) { AddShell(oo[2], flip ^ !B(oo[3])); return; }
                shells.Add((s, flip));
            }
            if (item.Of("MANIFOLD_SOLID_BREP") is { } msb) { AddShell(msb[1], false); closed = true; }
            else if (item.Of("BREP_WITH_VOIDS") is { } bwv) { AddShell(bwv[1], false); foreach (var v in L(bwv[2])) AddShell(v, false); closed = true; }
            else if (item.Of("FACETED_BREP") is { } fb) { AddShell(fb[1], false); closed = true; }
            else if (item.Of("SHELL_BASED_SURFACE_MODEL") is { } sbsm) foreach (var s in L(sbsm[1])) AddShell(s, false);
            else if (item.Of("CLOSED_SHELL") is not null || item.Of("OPEN_SHELL") is not null) AddShell(new StepRef(item.Id), false);
            var faces = new List<(StepEntity Face, bool Flip)>();
            foreach (var (shell, flip) in shells)
                if ((shell.Of("CLOSED_SHELL") ?? shell.Of("OPEN_SHELL")) is { } fl)
                    foreach (var face in L(fl[1]))
                        if (E(face) is { } fe) faces.Add((fe, flip));
            // First, the faces laid out as rings say which edges they need divided finer; the edges are
            // divided once, so both faces sharing each one use the same points (no cracks, no T-joints).
            var finer = new Dictionary<int, Dictionary<int, int>>();
            foreach (var (fe, _) in faces)
            {
                ct.ThrowIfCancellationRequested();
                if (Prepare(fe) is not { Surface: { } srf } job || srf.PeriodU == 0 && srf.PeriodV == 0) continue;
                List<(int Loop, int Seg, int Parts)> asks;
                Trace?.Invoke($"plan #{fe.Id} {srf.GetType().Name}");
                try { asks = new FaceMesher(srf, job.Same, job.Loops, _tol).Plan(); }
                catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException or InvalidOperationException or ArithmeticException) { continue; }
                foreach (var (loop, seg, parts) in asks)
                {
                    var (edge, k) = job.From[loop][seg];
                    if (edge < 0) continue;
                    if (!finer.TryGetValue(edge, out var m)) finer[edge] = m = [];
                    m[k] = Math.Max(m.GetValueOrDefault(k), parts);
                }
            }
            foreach (var (edge, asks) in finer)
            {
                var pts = _edges[edge];
                var divided = new List<P3> { pts[0] };
                for (int k = 0; k + 1 < pts.Count; k++)
                {
                    int parts = asks.GetValueOrDefault(k, 1);
                    for (int j = 1; j < parts; j++) divided.Add(P3.Lerp(pts[k], pts[k + 1], (double)j / parts));
                    divided.Add(pts[k + 1]);
                }
                _edges[edge] = divided;
            }
            foreach (var (fe, flip) in faces)
            {
                ct.ThrowIfCancellationRequested();
                int before = tris.Count;
                Face(fe, tris);
                if (flip)
                    for (int i = before; i < tris.Count; i++) { var (a, b, c) = tris[i]; tris[i] = (a, c, b); }
            }
            // A closed solid must enclose a positive volume; some files have it inside out. A shell of
            // surfaces that happens to close up (a model saved as faces) is put right the same way.
            if ((closed || Sealed(tris)) && Volume(tris) < 0)
                for (int i = 0; i < tris.Count; i++) { var (a, b, c) = tris[i]; tris[i] = (a, c, b); }
            _solids[item.Id] = tris;
            return tris;
        }

        /// <summary>Every edge used by exactly two triangles (points matched to 1e-6 of a unit).</summary>
        static bool Sealed(List<(P3 A, P3 B, P3 C)> tris)
        {
            if (tris.Count < 4) return false;
            var id = new Dictionary<(long, long, long), int>();
            int Id(P3 p) { var k = ((long)Math.Round(p.X * 1e6), (long)Math.Round(p.Y * 1e6), (long)Math.Round(p.Z * 1e6)); if (!id.TryGetValue(k, out var i)) id[k] = i = id.Count; return i; }
            var uses = new Dictionary<long, int>();
            foreach (var (a, b, c) in tris)
            {
                int ia = Id(a), ib = Id(b), ic = Id(c);
                foreach (var (x, y) in new[] { (ia, ib), (ib, ic), (ic, ia) })
                {
                    long k = x < y ? ((long)x << 32) | (uint)y : ((long)y << 32) | (uint)x;
                    uses[k] = uses.GetValueOrDefault(k) + 1;
                }
            }
            return uses.Values.All(v => v == 2);
        }

        static double Volume(List<(P3 A, P3 B, P3 C)> tris) => tris.Sum(t => t.A.Dot(t.B.Cross(t.C))) / 6;

        /// <summary>A face ready to mesh: its surface and outline loops, and for each step of a loop, the edge (and which of its steps) it came from.</summary>
        sealed record FaceJob(Surface3? Surface, bool Same, List<List<P3>> Loops, List<List<(int Edge, int Seg)>> From);

        FaceJob? Prepare(StepEntity face)
        {
            var a = face.Of("ADVANCED_FACE") ?? face.Of("FACE_SURFACE");
            if (a is null) return null;
            var loops = new List<List<P3>>();
            var from = new List<List<(int, int)>>();
            foreach (var bo in L(a[1]))
            {
                var bound = E(bo);
                var ba = bound?.Of("FACE_OUTER_BOUND") ?? bound?.Of("FACE_BOUND");
                if (ba is null) continue;
                if (Loop(E(ba[1])) is not { } lf || lf.Points.Count < 2) continue;
                var (loop, src) = lf;
                if (!B(ba[2]))
                {
                    // Backwards: the step from point i is the old step into it.
                    int n = loop.Count;
                    loop.Reverse();
                    var old = src;
                    src = [.. Enumerable.Range(0, n).Select(i => old[(2 * n - 2 - i) % n])];
                }
                loops.Add(loop);
                from.Add(src);
            }
            return new FaceJob(Surf(a[2]), B(a[3]), loops, from);
        }

        void Face(StepEntity face, List<(P3, P3, P3)> tris)
        {
            if (Prepare(face) is not { } job) return;
            _faces++;
            var (surface, same, loops) = (job.Surface, job.Same, job.Loops);
            if (surface is null) { _failed++; return; }
            try
            {
                int before = tris.Count;
                Trace?.Invoke($"start #{face.Id} {surface.GetType().Name} ({loops.Sum(l => l.Count)} points)");
                new FaceMesher(surface, same, loops, _tol).Mesh(tris);
                Trace?.Invoke($"face #{face.Id} {surface.GetType().Name}: {loops.Sum(l => l.Count)} outline points, {tris.Count - before} triangles");
                if (tris.Count == before && loops.Count > 0) { _failed++; Problem("Faces with no area"); }
            }
            catch (FaceException ex) { _failed++; Problem(ex.Message); }
            catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException or InvalidOperationException or ArithmeticException)
            {
                _failed++;
                Problem($"A face couldn't be read ({ex.GetType().Name})");
            }
        }

        /// <summary>
        /// A bound's points, edge after edge, round the loop (not repeating the first point at the end),
        /// and for each, the edge and step the line from it to the next point lies along (−1 if none).
        /// </summary>
        (List<P3> Points, List<(int Edge, int Seg)> From)? Loop(StepEntity? loop)
        {
            if (loop is null) return null;
            if (loop.Of("POLY_LOOP") is { } pl)
            {
                var poly = L(pl[1]).Select(Point).ToList();
                return (poly, [.. poly.Select(_ => (-1, -1))]);
            }
            if (loop.Of("VERTEX_LOOP") is not null) return null; // a single point (a cone's tip): nothing to draw
            if (loop.Of("EDGE_LOOP") is not { } el) return null;
            var pts = new List<P3>();
            var from = new List<(int, int)>();
            foreach (var oe in L(el[1]))
            {
                var o = E(oe)?.Of("ORIENTED_EDGE");
                if (o is null) continue;
                var ec = E(o[3]);
                if (ec is null) continue;
                var edge = Edge(ec);
                if (edge is null) continue;
                bool forward = B(o[4]);
                int n = edge.Count;
                // Every point but the last (the next edge starts there); the step from point k is the edge's step k (or, backwards, n - 2 - k).
                for (int k = 0; k + 1 < n; k++)
                {
                    var p = edge[forward ? k : n - 1 - k];
                    if (pts.Count > 0 && (pts[^1] - p).Length < 1e-12) { from[^1] = (ec.Id, forward ? k : n - 2 - k); continue; }
                    pts.Add(p);
                    from.Add((ec.Id, forward ? k : n - 2 - k));
                }
            }
            if (pts.Count > 1 && (pts[0] - pts[^1]).Length < 1e-9) { pts.RemoveAt(pts.Count - 1); from.RemoveAt(from.Count - 1); }
            return (pts, from);
        }

        // ---- assemblies --------------------------------------------------------------------------

        static bool IsShapeRep(StepEntity e) =>
            e.Type.EndsWith("SHAPE_REPRESENTATION", StringComparison.Ordinal) ||
            (e.Parts?.Keys.Any(k => k.EndsWith("SHAPE_REPRESENTATION", StringComparison.Ordinal)) ?? false);

        IReadOnlyList<object?> Items(StepEntity rep)
        {
            var a = rep.Type.Length > 0 ? rep.Args : rep.Parts!.FirstOrDefault(p => p.Key.EndsWith("REPRESENTATION", StringComparison.Ordinal)).Value;
            return a is { Count: > 1 } ? L(a[1]) : [];
        }

        static bool IsBody(StepEntity e) =>
            e.Is("MANIFOLD_SOLID_BREP") || e.Is("BREP_WITH_VOIDS") || e.Is("FACETED_BREP") || e.Is("SHELL_BASED_SURFACE_MODEL");

        /// <summary>Every body to make, with where it goes (an assembly's parts placed; a plain part as it is).</summary>
        IEnumerable<(StepEntity Item, Xform Where)> Instances()
        {
            var reps = f.All.Where(IsShapeRep).ToList();
            // Representations that share coordinates (a part's shape and its geometry), and placements of one in another.
            var same = new Dictionary<int, List<int>>();
            var placed = new List<(int Child, int Parent, Xform ChildToParent)>();
            // Which product each representation belongs to, to tell assembly from part.
            var repOfPd = new Dictionary<int, int>();
            foreach (var e in f.All)
            {
                if (e.Of("SHAPE_DEFINITION_REPRESENTATION") is { } sdr && E(sdr[0])?.Of("PRODUCT_DEFINITION_SHAPE") is { } pds && pds.Count > 2 && pds[2] is StepRef pd && sdr[1] is StepRef rep)
                    repOfPd.TryAdd(pd.Id, rep.Id);
            }
            var nauoOf = new Dictionary<int, (int Parent, int Child)>(); // relationship entity → products
            foreach (var e in f.All)
            {
                if (e.Of("CONTEXT_DEPENDENT_SHAPE_REPRESENTATION") is not { } cd) continue;
                var pds = E(cd[1])?.Of("PRODUCT_DEFINITION_SHAPE");
                var nauo = pds is { Count: > 2 } ? E(pds[2])?.Of("NEXT_ASSEMBLY_USAGE_OCCURRENCE") : null;
                if (nauo is { Count: > 4 } && cd[0] is StepRef rel && nauo[3] is StepRef parent && nauo[4] is StepRef child)
                    nauoOf[rel.Id] = (parent.Id, child.Id);
            }
            foreach (var e in f.All)
            {
                var rr = e.Of("REPRESENTATION_RELATIONSHIP") ?? e.Of("SHAPE_REPRESENTATION_RELATIONSHIP");
                if (rr is null || rr.Count < 4 || rr[2] is not StepRef r1 || rr[3] is not StepRef r2)
                {
                    if (e.Type == "SHAPE_REPRESENTATION_RELATIONSHIP" && e.Args.Count >= 4 && e.Args[2] is StepRef s1 && e.Args[3] is StepRef s2)
                        Link(same, s1.Id, s2.Id);
                    continue;
                }
                var withT = e.Of("REPRESENTATION_RELATIONSHIP_WITH_TRANSFORMATION");
                if (withT is null) { Link(same, r1.Id, r2.Id); continue; }
                var idt = E(withT[0])?.Of("ITEM_DEFINED_TRANSFORMATION");
                if (idt is null || idt.Count < 4) continue;
                var f1 = Xform.From(Place(idt[2]));
                var f2 = Xform.From(Place(idt[3]));
                // Usually rep 1 is the part and rep 2 the assembly; the product structure says for sure.
                bool oneIsChild = true;
                if (nauoOf.TryGetValue(e.Id, out var pc) && repOfPd.TryGetValue(pc.Child, out var childRep) && Group(same, childRep) is var cg)
                    oneIsChild = cg.Contains(r1.Id) || !cg.Contains(r2.Id);
                if (oneIsChild) placed.Add((r1.Id, r2.Id, f1.InverseRigid().Then(f2)));
                else placed.Add((r2.Id, r1.Id, f2.InverseRigid().Then(f1)));
            }
            // Groups of same-coordinate representations; a group placed in another is a child.
            var groupOf = new Dictionary<int, int>();
            foreach (var r in reps) if (!groupOf.ContainsKey(r.Id)) foreach (var m in Group(same, r.Id)) groupOf[m] = r.Id;
            int G(int rep) => groupOf.TryGetValue(rep, out var g) ? g : rep;
            var children = placed.GroupBy(p => G(p.Parent)).ToDictionary(g => g.Key, g => g.Select(p => (Child: G(p.Child), p.ChildToParent)).ToList());
            var isChild = placed.Select(p => G(p.Child)).ToHashSet();
            var members = groupOf.GroupBy(kv => kv.Value).ToDictionary(g => g.Key, g => g.Select(kv => kv.Key).ToList());
            var results = new List<(StepEntity, Xform)>();
            var seen = new HashSet<int>();
            void Walk(int group, Xform xf, int depth)
            {
                if (depth > 32) return;
                foreach (var rep in members.GetValueOrDefault(group) ?? [group])
                    if (f.Get(rep) is { } re)
                        foreach (var it in Items(re))
                            AddItem(E(it), xf, depth, results, seen);
                foreach (var (child, t) in children.GetValueOrDefault(group) ?? [])
                    Walk(child, t.Then(xf), depth + 1);
            }
            foreach (var root in members.Keys.Where(g => !isChild.Contains(g))) Walk(root, Xform.Identity, 0);
            // Bodies nothing placed (or files without the usual structure): as they are.
            foreach (var e in f.All.Where(IsBody))
                if (!seen.Contains(e.Id)) results.Add((e, Xform.Identity));
            return results;
        }

        void AddItem(StepEntity? it, Xform xf, int depth, List<(StepEntity, Xform)> results, HashSet<int> seen)
        {
            if (it is null || depth > 32) return;
            if (IsBody(it)) { results.Add((it, xf)); seen.Add(it.Id); return; }
            if (it.Of("MAPPED_ITEM") is { } mi && E(mi[1])?.Of("REPRESENTATION_MAP") is { } map && E(map[1]) is { } mapped)
            {
                var origin = Xform.From(Place(map[0]));
                var target = Xform.From(Place(mi[2]));
                var t = origin.InverseRigid().Then(target).Then(xf);
                foreach (var sub in Items(mapped)) AddItem(E(sub), t, depth + 1, results, seen);
            }
        }

        static void Link(Dictionary<int, List<int>> same, int a, int b)
        {
            if (!same.TryGetValue(a, out var la)) same[a] = la = [];
            if (!same.TryGetValue(b, out var lb)) same[b] = lb = [];
            la.Add(b);
            lb.Add(a);
        }

        static HashSet<int> Group(Dictionary<int, List<int>> same, int start)
        {
            var g = new HashSet<int> { start };
            var todo = new Stack<int>([start]);
            while (todo.Count > 0)
                foreach (var n in same.GetValueOrDefault(todo.Pop()) ?? [])
                    if (g.Add(n)) todo.Push(n);
            return g;
        }
    }

}

/// <summary>A curve turned round an axis: u the angle, v along the curve.</summary>
public sealed class RevolutionSurface(Curve3 curve, Frame axis) : Surface3
{
    public override P3 At(double u, double v)
    {
        var l = axis.Local(curve.At(v));
        double c = Math.Cos(u), sn = Math.Sin(u);
        return axis.At(l.X * c - l.Y * sn, l.X * sn + l.Y * c, l.Z);
    }
    public override (double U, double V) Uv(P3 p, (double U, double V)? hint)
    {
        // The profile: distance from the axis and height along it.
        var lp = axis.Local(p);
        double rho = Math.Sqrt(lp.X * lp.X + lp.Y * lp.Y), h = lp.Z;
        var (t0, t1) = curve.Range;
        if (double.IsInfinity(t0)) (t0, t1) = (-1e4, 1e4);
        double best = t0, bd = double.MaxValue;
        for (int i = 0; i <= 200; i++)
        {
            double t = t0 + (t1 - t0) * i / 200;
            var lc = axis.Local(curve.At(t));
            double d = Math.Pow(Math.Sqrt(lc.X * lc.X + lc.Y * lc.Y) - rho, 2) + Math.Pow(lc.Z - h, 2);
            if (d < bd) { bd = d; best = t; }
        }
        double step = (t1 - t0) / 200;
        for (int k = 0; k < 30 && step > 1e-12; k++)
        {
            foreach (var t in new[] { best - step, best + step })
            {
                if (t < t0 || t > t1) continue;
                var lc = axis.Local(curve.At(t));
                double d = Math.Pow(Math.Sqrt(lc.X * lc.X + lc.Y * lc.Y) - rho, 2) + Math.Pow(lc.Z - h, 2);
                if (d < bd) { bd = d; best = t; }
            }
            step /= 2;
        }
        var lb = axis.Local(curve.At(best));
        double u = Math.Atan2(lp.Y, lp.X) - Math.Atan2(lb.Y, lb.X);
        return (u, best);
    }
    public override double PeriodU => 2 * Math.PI;
}
