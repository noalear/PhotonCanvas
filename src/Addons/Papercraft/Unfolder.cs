using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using Tri = PhotonCanvas.Doc.Import.StlDepth.Triangle;
using V3 = PhotonCanvas.Doc.Import.StlDepth.Vec3;

namespace PhotonCanvas.Addon.Papercraft;

/// <summary>How a 3D model is unfolded into flat pieces of card.</summary>
public sealed record PapercraftOptions
{
    /// <summary>The finished model's longest side, mm.</summary>
    public double SizeMm { get; init; } = 120;
    /// <summary>Make the model simpler until it has at most this many faces (0 = as it is).</summary>
    public int MaxFaces { get; init; } = 300;
    /// <summary>Glue tabs this deep (mm); 0 = no tabs (the edges are still numbered).</summary>
    public double TabMm { get; init; } = 6;
    /// <summary>Neighbouring faces that bend less than this (degrees) are one flat face: no fold line between them.</summary>
    public double FlatDeg { get; init; } = 2;
    /// <summary>The biggest a piece (tabs included) may be, mm: the bed or the sheet, less a margin. 0 = no limit.</summary>
    public double MaxWidthMm { get; init; }
    public double MaxHeightMm { get; init; }
}

/// <summary>Which way a fold bends, looking at the printed (top) side, which is the model's outside.</summary>
public enum FoldKind
{
    /// <summary>Bends away from you: a ridge on the outside (scored on top, folds easily).</summary>
    Mountain,
    /// <summary>Bends towards you: a valley on the outside.</summary>
    Valley,
}

/// <summary>A fold line on a piece, and how far it bends (0° = flat, 90° = a box corner).</summary>
public sealed record PaperFold(Vec2 A, Vec2 B, FoldKind Kind, double AngleDeg);

/// <summary>
/// An edge that is cut. <see cref="Number"/> is shared by the two edges that are glued together
/// (0 = an open edge of the model: nothing joins it).
/// </summary>
public sealed record PaperEdge(Vec2 A, Vec2 B, int Number, Vec2 Inward)
{
    /// <summary>How far the face reaches in from the edge (to its far corner), mm: room for the edge's number.</summary>
    public double RoomMm { get; init; }
}

/// <summary>A glue tab on a cut edge: its outline from A round to B (A and B are on the edge), folded along A–B.</summary>
public sealed record PaperTab(IReadOnlyList<Vec2> Outline, int Number);

/// <summary>One flat piece: its faces (triangles), folds, cut edges and tabs, in mm (+Y down), printed side up.</summary>
public sealed record PaperPiece(int Index, IReadOnlyList<(Vec2 A, Vec2 B, Vec2 C)> Faces, IReadOnlyList<PaperFold> Folds, IReadOnlyList<PaperEdge> Edges, IReadOnlyList<PaperTab> Tabs)
{
    public Rect2 Bounds
    {
        get
        {
            var r = Rect2.Empty;
            foreach (var (a, b, c) in Faces) r = r.Union(Rect2.FromPoints(a, b)).Union(Rect2.FromPoints(c, c));
            foreach (var t in Tabs) foreach (var p in t.Outline) r = r.Union(Rect2.FromPoints(p, p));
            return r;
        }
    }
}

/// <summary>The whole model unfolded.</summary>
public sealed record PaperNet(IReadOnlyList<PaperPiece> Pieces, int Faces, int SourceTriangles, int Joins, int MissingTabs, int OpenEdges, int OddEdges, double ModelX, double ModelY, double ModelZ)
{
    public int Folds => Pieces.Sum(p => p.Folds.Count);
}

/// <summary>
/// Unfolds a 3D model (triangles) into flat pieces that fold back up into it: each piece grows
/// from its biggest face across the flattest, longest edges first, as long as it doesn't overlap
/// itself or outgrow the bed; then a new piece starts. Every cut edge is numbered, with its partner
/// on the other piece; one of each pair gets a glue tab.
/// </summary>
public static class Unfolder
{
    public static PaperNet Unfold(IReadOnlyList<Tri> mesh, PapercraftOptions o, CancellationToken ct = default)
    {
        if (mesh.Count == 0) throw new InvalidOperationException("The model has no faces.");
        // Scale so the longest side is the size asked for.
        double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
        foreach (var t in mesh)
            foreach (var v in new[] { t.A, t.B, t.C })
            {
                minX = Math.Min(minX, v.X); minY = Math.Min(minY, v.Y); minZ = Math.Min(minZ, v.Z);
                maxX = Math.Max(maxX, v.X); maxY = Math.Max(maxY, v.Y); maxZ = Math.Max(maxZ, v.Z);
            }
        double extent = Math.Max(maxX - minX, Math.Max(maxY - minY, maxZ - minZ));
        if (!(extent > 0)) throw new InvalidOperationException("The model is flat or empty.");
        double k = o.SizeMm / extent;
        var (verts, faces) = Weld(mesh, minX, minY, minZ, k);
        if (o.MaxFaces > 0 && faces.Count > o.MaxFaces) (verts, faces) = Simplify(verts, faces, o.MaxFaces);
        if (faces.Count == 0) throw new InvalidOperationException("Nothing is left of the model: it's too thin to fold.");
        ct.ThrowIfCancellationRequested();

        // Outside out: if the model's volume comes out negative, it was made inside out.
        double vol = 0;
        foreach (var f in faces) vol += Dot(verts[f.A], Cross(verts[f.B], verts[f.C]));
        if (vol < 0) faces = [.. faces.Select(f => new Face(f.A, f.C, f.B))];

        var u = new Unfolding(verts, faces, o, ct);
        var net = u.Run();
        return net with { SourceTriangles = mesh.Count, ModelX = (maxX - minX) * k, ModelY = (maxY - minY) * k, ModelZ = (maxZ - minZ) * k };
    }

    internal readonly record struct Face(int A, int B, int C)
    {
        public int this[int i] => i == 0 ? A : i == 1 ? B : C;
    }

    /// <summary>Merge corners that are in the same place, and drop faces with no area.</summary>
    static (List<V3> Verts, List<Face> Faces) Weld(IReadOnlyList<Tri> mesh, double ox, double oy, double oz, double k)
    {
        const double q = 1e-4; // mm
        var index = new Dictionary<(long, long, long), int>();
        var verts = new List<V3>();
        int Id(V3 v)
        {
            var p = new V3((v.X - ox) * k, (v.Y - oy) * k, (v.Z - oz) * k);
            var key = ((long)Math.Round(p.X / q), (long)Math.Round(p.Y / q), (long)Math.Round(p.Z / q));
            if (!index.TryGetValue(key, out int i)) { i = verts.Count; verts.Add(p); index[key] = i; }
            return i;
        }
        var faces = new List<Face>();
        foreach (var t in mesh)
        {
            var f = new Face(Id(t.A), Id(t.B), Id(t.C));
            if (Area(verts, f) > 1e-6) faces.Add(f);
        }
        return Clean(verts, faces);
    }

    /// <summary>
    /// Fewer, bigger faces: corners close together are merged into one (on a grid made as fine as
    /// the face count allows), and faces that collapse are dropped.
    /// </summary>
    static (List<V3>, List<Face>) Simplify(List<V3> verts, List<Face> faces, int maxFaces)
    {
        double ext = 0;
        foreach (var v in verts) ext = Math.Max(ext, Math.Max(v.X, Math.Max(v.Y, v.Z)));
        (List<V3>, List<Face>) At(int cells)
        {
            double cell = ext / cells;
            var groups = new Dictionary<(long, long, long), int>();
            var sum = new List<(double X, double Y, double Z, int N)>();
            var map = new int[verts.Count];
            for (int i = 0; i < verts.Count; i++)
            {
                var v = verts[i];
                var key = ((long)Math.Floor(v.X / cell), (long)Math.Floor(v.Y / cell), (long)Math.Floor(v.Z / cell));
                if (!groups.TryGetValue(key, out int g)) { g = sum.Count; groups[key] = g; sum.Add((0, 0, 0, 0)); }
                var s = sum[g];
                sum[g] = (s.X + v.X, s.Y + v.Y, s.Z + v.Z, s.N + 1);
                map[i] = g;
            }
            var nv = sum.Select(s => new V3(s.X / s.N, s.Y / s.N, s.Z / s.N)).ToList();
            var nf = new List<Face>();
            var seen = new HashSet<(int, int, int)>();
            foreach (var f in faces)
            {
                var m = new Face(map[f.A], map[f.B], map[f.C]);
                if (m.A == m.B || m.B == m.C || m.A == m.C || Area(nv, m) < 1e-4) continue;
                int[] s = [m.A, m.B, m.C];
                Array.Sort(s);
                if (seen.Add((s[0], s[1], s[2]))) nf.Add(m);
            }
            return Clean(nv, nf);
        }
        // The finest grid that gets under the face count.
        int lo = 2, hi = 1024;
        var best = At(lo);
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            var r = At(mid);
            if (r.Item2.Count <= maxFaces) { lo = mid; best = r; }
            else hi = mid - 1;
        }
        return best;
    }

    /// <summary>Only the corners still used.</summary>
    static (List<V3>, List<Face>) Clean(List<V3> verts, List<Face> faces)
    {
        var map = new Dictionary<int, int>();
        var nv = new List<V3>();
        int M(int i)
        {
            if (!map.TryGetValue(i, out int j)) { j = nv.Count; nv.Add(verts[i]); map[i] = j; }
            return j;
        }
        var nf = faces.Select(f => new Face(M(f.A), M(f.B), M(f.C))).ToList();
        return (nv, nf);
    }

    internal static V3 Sub(V3 a, V3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    internal static double Dot(V3 a, V3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    internal static V3 Cross(V3 a, V3 b) => new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
    internal static double Len(V3 a) => Math.Sqrt(Dot(a, a));
    static double Area(List<V3> v, Face f) => Len(Cross(Sub(v[f.B], v[f.A]), Sub(v[f.C], v[f.A]))) / 2;

    sealed class Unfolding(List<V3> verts, List<Face> faces, PapercraftOptions o, CancellationToken ct)
    {
        readonly Vec2[][] _at = new Vec2[faces.Count][];
        readonly int[] _piece = Enumerable.Repeat(-1, faces.Count).ToArray();
        // Edge (low vertex, high vertex) → the faces on it, with the edge's place in each (0: A–B, 1: B–C, 2: C–A).
        readonly Dictionary<(int, int), List<(int Face, int Side)>> _edges = [];
        readonly V3[] _normal = new V3[faces.Count];

        static (int, int) Key(int a, int b) => a < b ? (a, b) : (b, a);
        (int, int) EdgeKey(int f, int side) => Key(faces[f][side], faces[f][(side + 1) % 3]);

        public PaperNet Run()
        {
            for (int f = 0; f < faces.Count; f++)
            {
                var n = Cross(Sub(verts[faces[f].B], verts[faces[f].A]), Sub(verts[faces[f].C], verts[faces[f].A]));
                double l = Len(n);
                _normal[f] = l > 0 ? new V3(n.X / l, n.Y / l, n.Z / l) : new V3(0, 0, 1);
                for (int s = 0; s < 3; s++)
                {
                    var key = EdgeKey(f, s);
                    if (!_edges.TryGetValue(key, out var list)) _edges[key] = list = [];
                    list.Add((f, s));
                }
            }

            // Grow pieces, biggest faces first as seeds.
            var order = Enumerable.Range(0, faces.Count).OrderByDescending(f => Area(verts, faces[f])).ToList();
            var pieces = new List<List<int>>();
            foreach (int seed in order)
            {
                if (_piece[seed] >= 0) continue;
                ct.ThrowIfCancellationRequested();
                pieces.Add(Grow(seed, pieces.Count));
            }

            // Every edge: a fold (inside a piece), a numbered cut (between two faces), or an open edge.
            var folds = pieces.Select(_ => new List<PaperFold>()).ToList();
            var cuts = pieces.Select(_ => new List<PaperEdge>()).ToList();
            var tabs = pieces.Select(_ => new List<PaperTab>()).ToList();
            var pairs = new List<((int Face, int Side) X, (int Face, int Side) Y, int Number)>();
            int number = 0, open = 0, odd = 0;
            foreach (var (key, on) in _edges.OrderBy(e => e.Key))
            {
                if (on.Count == 2)
                {
                    var (f, sf) = on[0];
                    var (g, sg) = on[1];
                    var (fa, fb) = Ends(f, sf, key);
                    var (ga, gb) = Ends(g, sg, key);
                    double angle = Dihedral(f, g, key, out bool mountain);
                    if (_piece[f] == _piece[g] && fa.Dist(ga) < 1e-6 && fb.Dist(gb) < 1e-6)
                    {
                        if (angle >= o.FlatDeg) folds[_piece[f]].Add(new PaperFold(fa, fb, mountain ? FoldKind.Mountain : FoldKind.Valley, angle));
                        continue;
                    }
                    number++;
                    cuts[_piece[f]].Add(Edge(f, sf, fa, fb, number));
                    cuts[_piece[g]].Add(Edge(g, sg, ga, gb, number));
                    pairs.Add((on[0], on[1], number));
                }
                else
                {
                    if (on.Count == 1) open++; else odd++;
                    foreach (var (f, s) in on)
                    {
                        var (a, b) = Ends(f, s, key);
                        cuts[_piece[f]].Add(Edge(f, s, a, b, 0));
                    }
                }
            }

            // A glue tab on one edge of each pair: whichever side it fits on, alternating to share them out.
            int missing = 0;
            if (o.TabMm > 0)
            {
                var grids = pieces.Select(p => new Grid(p.Select(f => _at[f]))).ToList();
                foreach (var (x, y, n) in pairs)
                {
                    var first = n % 2 == 0 ? x : y;
                    var second = n % 2 == 0 ? y : x;
                    if (!TryTab(first, n, grids, tabs) && !TryTab(second, n, grids, tabs)) missing++;
                }
            }

            // Out: +Y down, each piece from its own top-left corner.
            var result = new List<PaperPiece>();
            for (int i = 0; i < pieces.Count; i++)
            {
                var piece = new PaperPiece(i,
                    [.. pieces[i].Select(f => (_at[f][0], _at[f][1], _at[f][2]))],
                    folds[i], cuts[i], tabs[i]);
                result.Add(Flip(piece));
            }
            return new PaperNet(result, faces.Count, 0, number, missing, open, odd, 0, 0, 0);
        }

        /// <summary>Mirror Y (laid out with +Y up, the program's +Y is down) and move to the origin.</summary>
        static PaperPiece Flip(PaperPiece p)
        {
            Vec2 F(Vec2 v) => new(v.X, -v.Y);
            var mirrored = new PaperPiece(p.Index,
                [.. p.Faces.Select(t => (F(t.A), F(t.B), F(t.C)))],
                [.. p.Folds.Select(x => x with { A = F(x.A), B = F(x.B) })],
                [.. p.Edges.Select(e => e with { A = F(e.A), B = F(e.B), Inward = F(e.Inward) })],
                [.. p.Tabs.Select(t => t with { Outline = [.. t.Outline.Select(F)] })]);
            var b = mirrored.Bounds;
            var d = new Vec2(-b.MinX, -b.MinY);
            Vec2 M(Vec2 v) => v + d;
            return new PaperPiece(p.Index,
                [.. mirrored.Faces.Select(t => (M(t.A), M(t.B), M(t.C)))],
                [.. mirrored.Folds.Select(x => x with { A = M(x.A), B = M(x.B) })],
                [.. mirrored.Edges.Select(e => e with { A = M(e.A), B = M(e.B) })],
                [.. mirrored.Tabs.Select(t => t with { Outline = [.. t.Outline.Select(M)] })]);
        }

        /// <summary>Where an edge's two corners are on the flat piece, low vertex first.</summary>
        (Vec2, Vec2) Ends(int f, int side, (int Lo, int Hi) key)
        {
            int a = faces[f][side];
            var p = _at[f][side];
            var q = _at[f][(side + 1) % 3];
            return a == key.Lo ? (p, q) : (q, p);
        }

        PaperEdge Edge(int f, int side, Vec2 a, Vec2 b, int number)
        {
            var c = _at[f][(side + 2) % 3];
            var inward = Inward(f, side);
            return new PaperEdge(a, b, number, inward) { RoomMm = Math.Max(0, (c - a).Dot(inward)) };
        }

        /// <summary>The unit direction from an edge into its face, on the flat piece.</summary>
        Vec2 Inward(int f, int side)
        {
            var p = _at[f][side];
            var q = _at[f][(side + 1) % 3];
            var c = _at[f][(side + 2) % 3];
            var d = q - p;
            var n = new Vec2(-d.Y, d.X) / Math.Max(d.Length, 1e-12);
            return n.Dot(c - p) >= 0 ? n : -n;
        }

        /// <summary>How far the two faces bend from flat (degrees), and whether it's a ridge seen from outside.</summary>
        double Dihedral(int f, int g, (int Lo, int Hi) key, out bool mountain)
        {
            double cos = Math.Clamp(Dot(_normal[f], _normal[g]), -1, 1);
            double angle = Math.Acos(cos) * 180 / Math.PI;
            int other = faces[g].A != key.Item1 && faces[g].A != key.Item2 ? faces[g].A : faces[g].B != key.Item1 && faces[g].B != key.Item2 ? faces[g].B : faces[g].C;
            // g's far corner below f's plane: the edge is a ridge (convex).
            mountain = Dot(Sub(verts[other], verts[key.Item1]), _normal[f]) <= 0;
            return angle;
        }

        double EdgeLength((int Lo, int Hi) key) => Len(Sub(verts[key.Item2], verts[key.Item1]));

        List<int> Grow(int seed, int index)
        {
            var members = new List<int> { seed };
            var grid = new Grid([]);
            // The seed, flat as it is, outside up (counter-clockwise with +Y up).
            var (a, b, c) = (verts[faces[seed].A], verts[faces[seed].B], verts[faces[seed].C]);
            var pa = new Vec2(0, 0);
            var pb = new Vec2(Len(Sub(b, a)), 0);
            var pc = Third(pa, pb, Len(Sub(c, a)), Len(Sub(c, b)), 1);
            _at[seed] = [pa, pb, pc];
            _piece[seed] = index;
            grid.Add(_at[seed]);
            var box = Box(_at[seed]);
            var queue = new PriorityQueue<(int From, int Side), (int, double)>();
            void Push(int f)
            {
                for (int s = 0; s < 3; s++)
                {
                    var key = EdgeKey(f, s);
                    var on = _edges[key];
                    if (on.Count != 2) continue;
                    int g = on[0].Face == f ? on[1].Face : on[0].Face;
                    if (_piece[g] >= 0) continue;
                    double angle = Dihedral(f, g, key, out _);
                    // Flat joins first (they don't need a fold), then the longest edges (less to cut and glue).
                    queue.Enqueue((f, s), (angle < o.FlatDeg ? 0 : 1, -EdgeLength(key)));
                }
            }
            Push(seed);
            double margin = 2 * Math.Max(o.TabMm, 0);
            while (queue.TryDequeue(out var e, out _))
            {
                var key = EdgeKey(e.From, e.Side);
                var on = _edges[key];
                int g = on[0].Face == e.From ? on[1].Face : on[0].Face;
                if (_piece[g] >= 0) continue;
                // Unfold g across the edge, onto the side away from the face it hangs from.
                int i = faces[e.From][e.Side], j = faces[e.From][(e.Side + 1) % 3];
                var pi = _at[e.From][e.Side];
                var pj = _at[e.From][(e.Side + 1) % 3];
                var far = _at[e.From][(e.Side + 2) % 3];
                var place = new Vec2[3];
                int third = -1;
                for (int s = 0; s < 3; s++)
                {
                    int v = faces[g][s];
                    if (v == i) place[s] = pi;
                    else if (v == j) place[s] = pj;
                    else third = s;
                }
                if (third < 0) continue;
                var vt = verts[faces[g][third]];
                double side = Math.Sign((pj - pi).Cross(far - pi));
                place[third] = Third(pi, pj, Len(Sub(vt, verts[i])), Len(Sub(vt, verts[j])), -side);
                if (!place[third].IsFinite) continue;
                // Not over the piece, and still fits the bed.
                if (grid.Overlaps(place)) continue;
                var grown = box.Union(Box(place));
                if (o.MaxWidthMm > 0 && o.MaxHeightMm > 0)
                {
                    double w = grown.Width + margin, h = grown.Height + margin;
                    if (Math.Min(w, h) > Math.Min(o.MaxWidthMm, o.MaxHeightMm) || Math.Max(w, h) > Math.Max(o.MaxWidthMm, o.MaxHeightMm)) continue;
                }
                box = grown;
                _at[g] = place;
                _piece[g] = index;
                grid.Add(place);
                members.Add(g);
                Push(g);
            }
            return members;
        }

        bool TryTab((int Face, int Side) at, int number, List<Grid> grids, List<List<PaperTab>> tabs)
        {
            var (f, s) = at;
            int p = _piece[f];
            var a = _at[f][s];
            var b = _at[f][(s + 1) % 3];
            var len = a.Dist(b);
            if (len < 1) return false;
            var dir = (b - a) / len;
            var outward = -Inward(f, s);
            for (double h = Math.Min(o.TabMm, len * 0.6); h >= Math.Min(2, o.TabMm); h /= 2)
            {
                // Sides at 45° or steeper, so neighbouring tabs don't meet at the corners.
                double inset = Math.Min(h, len * 0.3);
                Vec2[] tab = [a, a + outward * h + dir * inset, b + outward * h - dir * inset, b];
                if (grids[p].Overlaps(tab)) continue;
                // Not over another tab on the piece either.
                if (tabs[p].Any(t => Sat.Overlap([.. t.Outline], tab))) continue;
                tabs[p].Add(new PaperTab(tab, number));
                return true;
            }
            return false;
        }

        static Rect2 Box(IReadOnlyList<Vec2> pts)
        {
            var r = Rect2.Empty;
            foreach (var p in pts) r = r.Union(Rect2.FromPoints(p, p));
            return r;
        }

        /// <summary>The corner at distances da from a and db from b, on the left of a→b (side 1) or the right (−1).</summary>
        static Vec2 Third(Vec2 a, Vec2 b, double da, double db, double side)
        {
            double l = a.Dist(b);
            var u = (b - a) / l;
            double along = (da * da - db * db + l * l) / (2 * l);
            double h = Math.Sqrt(Math.Max(0, da * da - along * along));
            return a + u * along + new Vec2(-u.Y, u.X) * (h * side);
        }

        /// <summary>The faces placed so far, bucketed for quick overlap tests.</summary>
        sealed class Grid
        {
            readonly List<Vec2[]> _tris = [];
            readonly Dictionary<(int, int), List<int>> _cells = [];
            double _cell = 10;

            public Grid(IEnumerable<Vec2[]> tris)
            {
                foreach (var t in tris) Add(t);
            }

            public void Add(Vec2[] t)
            {
                int k = _tris.Count;
                _tris.Add(t);
                foreach (var c in Cells(Box(t))) { if (!_cells.TryGetValue(c, out var l)) _cells[c] = l = []; l.Add(k); }
            }

            IEnumerable<(int, int)> Cells(Rect2 r)
            {
                int x0 = (int)Math.Floor(r.MinX / _cell), x1 = (int)Math.Floor(r.MaxX / _cell);
                int y0 = (int)Math.Floor(r.MinY / _cell), y1 = (int)Math.Floor(r.MaxY / _cell);
                for (int x = x0; x <= x1; x++)
                    for (int y = y0; y <= y1; y++) yield return (x, y);
            }

            public bool Overlaps(Vec2[] poly)
            {
                var seen = new HashSet<int>();
                foreach (var c in Cells(Box(poly)))
                    if (_cells.TryGetValue(c, out var l))
                        foreach (int k in l)
                            if (seen.Add(k) && Sat.Overlap(_tris[k], poly)) return true;
                return false;
            }
        }
    }
}

/// <summary>Do two convex polygons overlap (more than touching)?</summary>
static class Sat
{
    const double Eps = 1e-5;

    public static bool Overlap(Vec2[] a, Vec2[] b) => !Separated(a, b) && !Separated(b, a);

    static bool Separated(Vec2[] a, Vec2[] b)
    {
        for (int i = 0; i < a.Length; i++)
        {
            var p = a[i];
            var q = a[(i + 1) % a.Length];
            var axis = new Vec2(-(q.Y - p.Y), q.X - p.X);
            double l = axis.Length;
            if (l < 1e-12) continue;
            axis /= l;
            double amin = double.MaxValue, amax = double.MinValue, bmin = double.MaxValue, bmax = double.MinValue;
            foreach (var v in a) { double d = axis.Dot(v); amin = Math.Min(amin, d); amax = Math.Max(amax, d); }
            foreach (var v in b) { double d = axis.Dot(v); bmin = Math.Min(bmin, d); bmax = Math.Max(bmax, d); }
            if (amax <= bmin + Eps || bmax <= amin + Eps) return true;
        }
        return false;
    }
}
