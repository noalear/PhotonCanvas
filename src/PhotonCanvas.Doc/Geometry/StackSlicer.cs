using PhotonCanvas.Core;
using PhotonCanvas.Doc.Import;
using Tri = PhotonCanvas.Doc.Import.StlDepth.Triangle;
using V3 = PhotonCanvas.Doc.Import.StlDepth.Vec3;

namespace PhotonCanvas.Doc.Geometry;

/// <summary>
/// The outline is the same all through a cut slice, but the model's isn't: which one to cut.
/// </summary>
public enum SliceOutline
{
    /// <summary>The model's shape half-way through the slice: steps stick out as much as they cut in.</summary>
    Middle,
    /// <summary>Everything the model covers anywhere in the slice: sand the steps back to the shape.</summary>
    Outer,
    /// <summary>Only what the model covers all through the slice: nothing sticks out, fill the steps.</summary>
    Inner,
}

/// <summary>Which way up the model stands while it's sliced.</summary>
public enum SliceUp { Z, MinusZ, Y, MinusY, X, MinusX }

public sealed record SliceOptions
{
    /// <summary>The material's thickness: one slice each.</summary>
    public double ThicknessMm { get; init; } = 3;
    /// <summary>The model's height (along "up") in mm; the other sizes follow.</summary>
    public double HeightMm { get; init; } = 60;
    public SliceUp Up { get; init; } = SliceUp.Z;
    /// <summary>Turn round the up direction (degrees), to lay the slices out differently.</summary>
    public double TurnDeg { get; init; }
    public SliceOutline Outline { get; init; } = SliceOutline.Middle;
    /// <summary>Cross-sections taken through each slice for Outer and Inner.</summary>
    public int Samples { get; init; } = 5;
    /// <summary>Dowel holes through every slice for lining them up (0 = none).</summary>
    public double DowelMm { get; init; }
    /// <summary>
    /// Pin holes between each pair of neighbouring slices (0 = none): wherever a part sits on the
    /// part below it, both get matching holes, so a short pin through both lines them up.
    /// </summary>
    public double PinMm { get; init; }
    /// <summary>
    /// Where the model is joined but two slices barely touch (steep slopes: a trunk, an ear), add
    /// the shape where they meet to both, so they overlap enough to glue and pin.
    /// </summary>
    public bool KeepJoined { get; init; } = true;
    /// <summary>The least overlap (mm²) two joined parts should have to glue them.</summary>
    public double MinJoinMm2 { get; init; } = 6;
    /// <summary>Parts smaller than this (mm²), or thinner than <see cref="MinPartWidthMm"/>, are left out: slivers too small to cut or handle.</summary>
    public double MinPartMm2 { get; init; } = 1;
    public double MinPartWidthMm { get; init; } = 1;
}

/// <summary>One part to cut: its outline (with holes), where its number goes, and the next slice's outline to mark on it.</summary>
public sealed record SlicePart(int Slice, PathGeometry Outline, Vec2 LabelAt, double LabelRoomMm, IReadOnlyList<IReadOnlyList<Vec2>> NextMarks)
{
    /// <summary>Not joined, through the slices, to anything resting on the bottom slice: it starts in mid-air with nothing under it (glue it on by eye).</summary>
    public bool Floating { get; init; }
}

/// <summary>The result: every part, bottom slice first, in model millimetres (laid flat, top view).</summary>
public sealed record SliceStack(IReadOnlyList<SlicePart> Parts, int Slices, double StackHeightMm, double ModelWidthMm, double ModelDepthMm,
    IReadOnlyList<PathGeometry> Layers, IReadOnlyList<Vec2> Dowels)
{
    /// <summary>Slivers left out for being too small.</summary>
    public int LeftOut { get; init; }
    /// <summary>Joints where the shape where the slices meet was added so they overlap enough to glue.</summary>
    public int Joined { get; init; }
    /// <summary>Pins joining neighbouring slices: where each goes, and the slice it rises from (it sits in that slice and the one above).</summary>
    public IReadOnlyList<(int Slice, Vec2 At)> Pins { get; init; } = [];
}

/// <summary>
/// Stacked slices: a 3D model cut into flat layers of the material's thickness. Each layer is cut
/// out (several parts where the model branches, like an animal's legs), numbered from the bottom
/// up, and marked with where the next layer sits, so they glue up in order into the model.
/// </summary>
public static class StackSlicer
{
    public static SliceStack Slice(IReadOnlyList<Tri> mesh, SliceOptions o, CancellationToken ct = default)
    {
        if (mesh.Count == 0) throw new ArgumentException("The model has no triangles.", nameof(mesh));
        if (!(o.ThicknessMm > 0) || !(o.HeightMm > 0)) throw new ArgumentException("Thickness and height must be above zero.");
        var tris = Orient(mesh, o);
        double h = tris.Max(t => Math.Max(t.A.Z, Math.Max(t.B.Z, t.C.Z)));
        int n = Math.Max(1, (int)Math.Round(h / o.ThicknessMm));
        double slab = h / n;
        var layers = new List<PathGeometry>();
        for (int i = 0; i < n; i++)
        {
            ct.ThrowIfCancellationRequested();
            double z0 = i * slab, z1 = (i + 1) * slab;
            PathGeometry layer;
            if (o.Outline == SliceOutline.Middle) layer = Section(tris, (z0 + z1) / 2);
            else
            {
                int k = Math.Max(2, o.Samples);
                var cuts = Enumerable.Range(0, k).Select(j => Section(tris, z0 + slab * (0.02 + 0.96 * j / (k - 1)))).Where(g => !g.IsEmpty).ToList();
                layer = cuts.Count == 0 ? PathGeometry.Empty
                    : o.Outline == SliceOutline.Outer ? ShapeGeometry.Union(cuts)
                    : cuts.Skip(1).Aggregate(cuts[0], (a, b) => ShapeGeometry.Boolean(a, b, BoolOp.Intersect));
            }
            layers.Add(PathTools.Simplify(layer, 0.01).Geometry);
        }
        // Steep slopes: where the model is joined between two slices but they barely overlap, both
        // get the model's shape at the plane where they meet (always shared, so they overlap).
        int joined = 0;
        if (o.KeepJoined)
            for (int i = 0; i + 1 < n; i++)
            {
                ct.ThrowIfCancellationRequested();
                var meet = Section(tris, (i + 1) * slab);
                if (meet.IsEmpty) continue;
                var shared = ShapeGeometry.Boolean(layers[i], layers[i + 1], BoolOp.Intersect);
                var add = new List<PathGeometry>();
                foreach (var piece in Islands(meet))
                {
                    if (Area(piece) < 0.5) continue; // a point of contact: nothing to glue
                    // How much of this meeting place the two slices already share.
                    var near = ShapeGeometry.Offset(piece, 1, JoinStyle.Round, EndStyle.Round);
                    if (Area(ShapeGeometry.Boolean(shared, near, BoolOp.Intersect)) >= Math.Min(o.MinJoinMm2, Area(piece) * 0.8)) continue;
                    add.Add(piece);
                }
                if (add.Count == 0) continue;
                joined += add.Count;
                layers[i] = ShapeGeometry.Union([layers[i], .. add]);
                layers[i + 1] = ShapeGeometry.Union([layers[i + 1], .. add]);
            }
        // Dowels: two holes in the part every slice has in common (when there is one).
        var dowels = new List<Vec2>();
        if (o.DowelMm > 0 && layers.All(l => !l.IsEmpty))
        {
            var common = layers.Skip(1).Aggregate(layers[0], (a, b) => ShapeGeometry.Boolean(a, b, BoolOp.Intersect));
            var room = ShapeGeometry.Offset(common, -(o.DowelMm / 2 + 1.5), JoinStyle.Round, EndStyle.Round);
            dowels = TwoPointsInside(room);
        }
        // Every slice's parts first (slivers left out), so the pins can join them up.
        int leftOut = 0;
        var islands = new List<List<PathGeometry>>();
        for (int i = 0; i < n; i++)
        {
            var keep = new List<PathGeometry>();
            foreach (var island in Islands(layers[i]))
            {
                var loops = Loops(island);
                if (Math.Abs(loops.Sum(Geom.SignedArea)) < o.MinPartMm2 || Pole(loops).Room * 2 < o.MinPartWidthMm) { leftOut++; continue; }
                keep.Add(island);
            }
            islands.Add(keep);
        }
        // Pins: wherever a part overlaps the part above it, matching holes in both.
        var holes = islands.Select(l => l.Select(_ => new List<Vec2>()).ToList()).ToList();
        var pins = new List<(int, Vec2)>();
        if (o.PinMm > 0)
            for (int i = 0; i + 1 < n; i++)
                for (int a = 0; a < islands[i].Count; a++)
                    for (int b = 0; b < islands[i + 1].Count; b++)
                    {
                        ct.ThrowIfCancellationRequested();
                        foreach (var p in PinSpots(islands[i][a], islands[i + 1][b], i > 0 ? layers[i - 1] : null, i + 2 < n ? layers[i + 2] : null, o.PinMm, holes[i][a]))
                        {
                            holes[i][a].Add(p);
                            holes[i + 1][b].Add(p);
                            pins.Add((i + 1, p));
                        }
                    }
        // Groups of parts joined to each other through the slices.
        var ids = new Dictionary<(int, int), int>();
        foreach (var (i, list) in islands.Select((l, i) => (i, l)))
            for (int k = 0; k < list.Count; k++) ids[(i, k)] = ids.Count;
        var parent = Enumerable.Range(0, ids.Count).ToArray();
        int Find(int x) => parent[x] == x ? x : parent[x] = Find(parent[x]);
        for (int i = 0; i + 1 < n; i++)
            for (int a = 0; a < islands[i].Count; a++)
                for (int b = 0; b < islands[i + 1].Count; b++)
                    if (Area(ShapeGeometry.Boolean(islands[i][a], islands[i + 1][b], BoolOp.Intersect)) > 0.5)
                        parent[Find(ids[(i, a)])] = Find(ids[(i + 1, b)]);
        // A group that reaches the bottom slice stands on the base and can be built up from it (a
        // fish's separate segments, say); only a group starting in mid-air has nothing under it.
        var grounded = ids.Where(kv => kv.Key.Item1 == 0).Select(kv => Find(kv.Value)).ToHashSet();
        bool Touches(int i, int k) => grounded.Contains(Find(ids[(i, k)]));
        var parts = new List<SlicePart>();
        for (int i = 0; i < n; i++)
        {
            var next = i + 1 < n ? layers[i + 1] : null;
            for (int k = 0; k < islands[i].Count; k++)
            {
                var part = islands[i][k];
                var cut = dowels.Select(p => (p, o.DowelMm)).Concat(holes[i][k].Select(p => (p, o.PinMm))).ToList();
                if (cut.Count > 0)
                    part = ShapeGeometry.Boolean(part, new PathGeometry(cut.SelectMany(c => PathGeometry.Ellipse(c.p.X, c.p.Y, c.Item2 / 2, c.Item2 / 2).Subpaths)), BoolOp.Subtract);
                // The number goes clear of the holes and edges.
                var loops = Loops(part);
                var (at, room) = Pole(loops);
                var marks = next is null ? [] : MarksInside(next, loops, 0.6);
                parts.Add(new SlicePart(i + 1, part, at, room, marks) { Floating = n > 1 && !Touches(i, k) });
            }
        }
        double w = tris.Max(t => Math.Max(t.A.X, Math.Max(t.B.X, t.C.X))), d = tris.Max(t => Math.Max(t.A.Y, Math.Max(t.B.Y, t.C.Y)));
        return new SliceStack(parts, n, n * o.ThicknessMm, w, d, layers, dowels) { LeftOut = leftOut, Pins = pins, Joined = joined };
    }

    /// <summary>Area covered (outlines minus holes), mm².</summary>
    static double Area(PathGeometry g) => g.IsEmpty ? 0 : Math.Abs(Loops(g).Sum(Geom.SignedArea));

    static List<List<Vec2>> Loops(PathGeometry g) => [.. g.Flatten(0.05).Where(f => f.Closed && f.Points.Count >= 3).Select(f => f.Points)];

    /// <summary>
    /// Where pins go between a part and the part above it: inside both, clear of their edges, and
    /// where the slices below and above cover the holes too (so they don't show) if there's room.
    /// Two pins, far apart, when there's room for both (that stops the parts turning); else one.
    /// </summary>
    static List<Vec2> PinSpots(PathGeometry lower, PathGeometry upper, PathGeometry? below, PathGeometry? above, double pinMm, IReadOnlyList<Vec2> taken)
    {
        var overlap = ShapeGeometry.Boolean(lower, upper, BoolOp.Intersect);
        if (overlap.IsEmpty) return [];
        double clear = pinMm / 2 + 1.2;
        var hidden = overlap;
        if (below is not null) hidden = ShapeGeometry.Boolean(hidden, below, BoolOp.Intersect);
        if (above is not null) hidden = ShapeGeometry.Boolean(hidden, above, BoolOp.Intersect);
        foreach (var area in new[] { hidden, overlap })
        {
            if (area.IsEmpty) continue;
            var room = ShapeGeometry.Offset(area, -clear, JoinStyle.Round, EndStyle.Round);
            // Clear of the holes already in the lower part (its pins from the joint below).
            if (taken.Count > 0 && !room.IsEmpty)
                room = ShapeGeometry.Boolean(room, new PathGeometry(taken.SelectMany(t => PathGeometry.Ellipse(t.X, t.Y, pinMm * 1.5 + 1, pinMm * 1.5 + 1).Subpaths)), BoolOp.Subtract);
            if (room.IsEmpty) continue;
            var two = TwoPointsInside(room);
            // Two pins only where they can be well apart (small parts: one, plus the outline mark).
            if (two.Count == 2 && two[0].Dist(two[1]) >= Math.Max(pinMm * 4, 12)) return two;
            var loops = Loops(room);
            if (loops.Count > 0 && Pole(loops) is { Room: > 0 } pole) return [pole.At];
        }
        return [];
    }

    /// <summary>The model stood the chosen way up, turned, sized to the height, and moved to start at 0, 0, 0.</summary>
    static List<Tri> Orient(IReadOnlyList<Tri> mesh, SliceOptions o)
    {
        V3 Up(V3 v) => o.Up switch
        {
            SliceUp.MinusZ => new(v.X, -v.Y, -v.Z),
            SliceUp.Y => new(v.X, -v.Z, v.Y),
            SliceUp.MinusY => new(v.X, v.Z, -v.Y),
            SliceUp.X => new(v.Y, v.Z, v.X),
            SliceUp.MinusX => new(-v.Y, v.Z, -v.X),
            _ => v,
        };
        double t = o.TurnDeg * Math.PI / 180, c = Math.Cos(t), s = Math.Sin(t);
        V3 Map(V3 v) { var u = Up(v); return new(u.X * c - u.Y * s, u.X * s + u.Y * c, u.Z); }
        var tris = mesh.Select(x => new Tri(Map(x.A), Map(x.B), Map(x.C))).ToList();
        var all = tris.SelectMany(x => new[] { x.A, x.B, x.C }).ToList();
        double minX = all.Min(v => v.X), minY = all.Min(v => v.Y), minZ = all.Min(v => v.Z), maxZ = all.Max(v => v.Z);
        double k = o.HeightMm / Math.Max(1e-9, maxZ - minZ);
        V3 Fit(V3 v) => new((v.X - minX) * k, (v.Y - minY) * k, (v.Z - minZ) * k);
        return [.. tris.Select(x => new Tri(Fit(x.A), Fit(x.B), Fit(x.C)))];
    }

    /// <summary>The model's cross-section at height <paramref name="z"/>: triangle edges crossing it, joined into closed outlines.</summary>
    public static PathGeometry Section(IReadOnlyList<Tri> tris, double z)
    {
        z += 1e-7; // never exactly on a vertex
        var segs = new List<(Vec2 A, Vec2 B)>();
        foreach (var t in tris)
        {
            var pts = new List<Vec2>(2);
            void Edge(V3 p, V3 q)
            {
                if ((p.Z - z) * (q.Z - z) >= 0) return;
                double f = (z - p.Z) / (q.Z - p.Z);
                pts.Add(new Vec2(p.X + (q.X - p.X) * f, p.Y + (q.Y - p.Y) * f));
            }
            Edge(t.A, t.B); Edge(t.B, t.C); Edge(t.C, t.A);
            if (pts.Count != 2 || pts[0].Dist(pts[1]) <= 1e-9) continue;
            // Run each piece with the solid on its left (outward normal on the right), so outlines
            // come out anticlockwise and holes clockwise, whatever order the triangle's edges cut.
            double nx = (t.B.Y - t.A.Y) * (t.C.Z - t.A.Z) - (t.B.Z - t.A.Z) * (t.C.Y - t.A.Y);
            double ny = (t.B.Z - t.A.Z) * (t.C.X - t.A.X) - (t.B.X - t.A.X) * (t.C.Z - t.A.Z);
            var d = pts[1] - pts[0];
            segs.Add(d.Y * nx - d.X * ny >= 0 ? (pts[0], pts[1]) : (pts[1], pts[0]));
        }
        // Join the pieces end to end (ends matched to a micron).
        static (long, long) Key(Vec2 p) => ((long)Math.Round(p.X * 1000), (long)Math.Round(p.Y * 1000));
        // Pieces by where they start: each one carries on from the end of the last.
        var at = new Dictionary<(long, long), List<int>>();
        for (int i = 0; i < segs.Count; i++)
        {
            if (!at.TryGetValue(Key(segs[i].A), out var l)) at[Key(segs[i].A)] = l = [];
            l.Add(i);
        }
        var used = new bool[segs.Count];
        var loops = new List<Subpath>();
        for (int s0 = 0; s0 < segs.Count; s0++)
        {
            if (used[s0]) continue;
            used[s0] = true;
            var loop = new List<Vec2> { segs[s0].A, segs[s0].B };
            while (true)
            {
                var end = loop[^1];
                int nxt = at.TryGetValue(Key(end), out var cand) ? cand.FirstOrDefault(j => !used[j], -1) : -1;
                if (nxt < 0) break;
                used[nxt] = true;
                loop.Add(segs[nxt].B);
                if (Key(loop[^1]) == Key(loop[0])) break;
            }
            if (Key(loop[^1]) == Key(loop[0])) loop.RemoveAt(loop.Count - 1);
            if (loop.Count >= 3 && Math.Abs(Geom.SignedArea(loop)) > 1e-4)
                loops.Add(PathGeometry.Polyline(loop, true).Subpaths[0]);
        }
        // Non-zero: overlapping solids in the model merge, holes (running the other way) stay holes.
        return loops.Count == 0 ? PathGeometry.Empty : ShapeGeometry.FillNonZero(new PathGeometry(loops));
    }

    /// <summary>Each separate piece of a layer (an outline with the holes inside it).</summary>
    static List<PathGeometry> Islands(PathGeometry layer)
    {
        var loops = layer.Subpaths.Where(sp => sp.Closed).Select(sp => (Sp: sp, Pts: sp.Flatten(0.05))).Where(x => x.Pts.Count >= 3).ToList();
        int Depth(int i) => loops.Where((_, j) => j != i).Count(o => Geom.Inside(o.Pts, loops[i].Pts[0]));
        var depth = loops.Select((_, i) => Depth(i)).ToList();
        var outers = Enumerable.Range(0, loops.Count).Where(i => depth[i] % 2 == 0).ToList();
        var o = outers.ToDictionary(i => i, i => new List<Subpath> { loops[i].Sp });
        for (int i = 0; i < loops.Count; i++)
        {
            if (depth[i] % 2 == 0) continue;
            // A hole belongs to the smallest outline round it.
            var owner = outers.Where(j => Geom.Inside(loops[j].Pts, loops[i].Pts[0])).OrderBy(j => Math.Abs(Geom.SignedArea(loops[j].Pts))).FirstOrDefault(-1);
            if (owner >= 0) o[owner].Add(loops[i].Sp);
        }
        return [.. o.Values.Select(l => new PathGeometry(l))];
    }

    static bool InsideEvenOdd(IReadOnlyList<List<Vec2>> loops, Vec2 p) => loops.Count(l => Geom.Inside(l, p)) % 2 == 1;

    static double EdgeDistance(IReadOnlyList<List<Vec2>> loops, Vec2 p)
    {
        double best = double.PositiveInfinity;
        foreach (var l in loops)
            for (int i = 0; i < l.Count; i++)
            {
                var a = l[i];
                var b = l[(i + 1) % l.Count];
                var ab = b - a;
                double len2 = ab.Dot(ab), t = len2 < 1e-18 ? 0 : Math.Clamp((p - a).Dot(ab) / len2, 0, 1);
                best = Math.Min(best, p.Dist(a + ab * t));
            }
        return best;
    }

    /// <summary>The point inside furthest from any edge (where the number fits best), and how far that is.</summary>
    static (Vec2 At, double Room) Pole(List<List<Vec2>> loops)
    {
        var box = Rect2.Bounds(loops.SelectMany(l => l));
        (Vec2, double) best = (box.Center, 0);
        double cell = Math.Max(box.Width, box.Height) / 24;
        var centre = box.Center;
        for (int pass = 0; pass < 3 && cell > 1e-3; pass++)
        {
            var start = best.Item2 > 0 ? best.Item1 : centre;
            double span = pass == 0 ? Math.Max(box.Width, box.Height) : cell * 4;
            for (double x = start.X - span / 2; x <= start.X + span / 2; x += cell)
                for (double y = start.Y - span / 2; y <= start.Y + span / 2; y += cell)
                {
                    var p = new Vec2(x, y);
                    if (!InsideEvenOdd(loops, p)) continue;
                    double d = EdgeDistance(loops, p);
                    if (d > best.Item2) best = (p, d);
                }
            cell /= 4;
        }
        return best;
    }

    /// <summary>The next layer's outline, only where it lies inside this part (clear of its edge by <paramref name="margin"/>).</summary>
    static List<IReadOnlyList<Vec2>> MarksInside(PathGeometry next, List<List<Vec2>> part, double margin)
    {
        var o = new List<IReadOnlyList<Vec2>>();
        foreach (var f in next.Flatten(0.05).Where(f => f.Closed))
        {
            var pts = f.Points;
            var run = new List<Vec2>();
            int count = pts.Count;
            for (int i = 0; i <= count; i++)
            {
                var a = pts[i % count];
                var b = pts[(i + 1) % count];
                int steps = Math.Max(1, (int)Math.Ceiling(a.Dist(b) / 0.25));
                for (int k = 0; k < steps && !(i == count && k > 0); k++)
                {
                    var p = Vec2.Lerp(a, b, (double)k / steps);
                    if (InsideEvenOdd(part, p) && EdgeDistance(part, p) > margin) run.Add(p);
                    else { if (run.Count >= 3) o.Add(run); run = []; }
                }
            }
            if (run.Count >= 3) o.Add(run);
        }
        return o;
    }

    /// <summary>Two points inside an area, far apart (for dowels), or none.</summary>
    static List<Vec2> TwoPointsInside(PathGeometry area)
    {
        var loops = area.Flatten(0.1).Where(f => f.Closed && f.Points.Count >= 3).Select(f => f.Points).ToList();
        if (loops.Count == 0) return [];
        var box = Rect2.Bounds(loops.SelectMany(l => l));
        var inside = new List<Vec2>();
        double cell = Math.Max(0.5, Math.Max(box.Width, box.Height) / 40);
        for (double x = box.MinX; x <= box.MaxX; x += cell)
            for (double y = box.MinY; y <= box.MaxY; y += cell)
                if (InsideEvenOdd(loops, new Vec2(x, y))) inside.Add(new Vec2(x, y));
        if (inside.Count < 2) return [];
        var a = inside.MaxBy(p => p.Dist(box.Center));
        var b = inside.MaxBy(p => p.Dist(a));
        return a.Dist(b) < 3 ? [] : [a, b];
    }
}

/// <summary>Stacked slices laid out on the bed, ready to cut: a page per bed-full.</summary>
public static class StackLayout
{
    /// <summary>
    /// Every part on a Cut layer, its number (and the next slice's outline, when asked) lightly
    /// marked on a Score layer, packed in number order. Parts that don't fit go on new pages
    /// ("Slices 2", …). Returns the design (showing the first page used) and how many beds it takes.
    /// </summary>
    public static (DocState State, int Sheets) Place(DocState s, SliceStack stack, MachineProfile machine, bool numbers, bool marks, double gapMm = 3)
    {
        int firstPage = s.PageIndex;
        int sheets = 1;
        (Layer Cut, Layer Marks) Layers(ref DocState st)
        {
            var cut = st.Layers.FirstOrDefault(l => l.Name == "Slices · cut");
            var mk = st.Layers.FirstOrDefault(l => l.Name == "Slices · marks");
            if (cut is null)
            {
                cut = Layer.Default(st.Layers.Count, LayerMode.Cut) with { Name = "Slices · cut" };
                st = Ops.AddLayer(st, cut);
            }
            if (mk is null)
            {
                var score = Layer.Default(st.Layers.Count, LayerMode.Score);
                mk = score with { Name = "Slices · marks", Laser = score.Laser with { Power = Math.Min(score.Laser.Power, 15) } };
                st = Ops.AddLayer(st, mk);
            }
            return (cut, mk);
        }
        var (cutLayer, markLayer) = Layers(ref s);
        double x = gapMm, y = gapMm, rowH = 0;
        foreach (var part in stack.Parts)
        {
            var box = part.Outline.Bounds();
            if (box.IsEmpty) continue;
            if (box.Width + 2 * gapMm > machine.FieldW || box.Height + 2 * gapMm > machine.FieldH)
                throw new InvalidOperationException($"Slice {part.Slice} is {box.Width:0} × {box.Height:0} mm: bigger than the bed. Make the model smaller.");
            if (x + box.Width + gapMm > machine.FieldW) { x = gapMm; y += rowH + gapMm; rowH = 0; }
            if (y + box.Height + gapMm > machine.FieldH)
            {
                // A new bed-full on its own page.
                s = Pages.Add(s, $"Slices {++sheets}");
                (cutLayer, markLayer) = Layers(ref s);
                x = gapMm; y = gapMm; rowH = 0;
            }
            var move = Affine.Translate(x - box.MinX, y - box.MinY);
            var shapes = new List<Shape> { new PathShape { Name = $"Slice {part.Slice}", LayerId = cutLayer.Id, Geometry = part.Outline.Transform(move) } };
            if (marks && part.NextMarks.Count > 0)
                shapes.Add(new PathShape { Name = $"Where slice {part.Slice + 1} goes", LayerId = markLayer.Id, Geometry = new PathGeometry(part.NextMarks.SelectMany(m => PathGeometry.Polyline(m, false).Subpaths)).Transform(move) });
            if (numbers && part.LabelRoomMm > 0.6)
            {
                double size = Math.Clamp(part.LabelRoomMm * 0.9, 1.2, 6);
                var text = TextShape.Create($"{part.Slice}", new TextStyle("Arial", size), markLayer.Id, Affine.Identity);
                var tb = text.Bounds;
                var at = move.Apply(part.LabelAt);
                shapes.Add(text.With(transform: Affine.Translate(at.X - tb.Center.X, at.Y - tb.Center.Y)));
            }
            s = Ops.AddShapes(s, shapes);
            if (shapes.Count > 1) s = Ops.Group(s, shapes.Select(sh => sh.Id).ToHashSet()).Item1;
            x += box.Width + gapMm;
            rowH = Math.Max(rowH, box.Height);
        }
        return (Pages.SwitchTo(s, firstPage), sheets);
    }
}
