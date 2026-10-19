namespace PhotonCanvas.Doc.Import.Step;

/// <summary>
/// Triangles for a flat region with holes (even-odd: a loop inside another is a hole, one inside
/// that is solid again). Loops are lists of vertex numbers into a shared list of 2D points, so
/// triangles keep pointing at the caller's vertices. Each outline and its holes go to
/// <see cref="Earcut"/>; Delaunay flips then give well-shaped triangles. Triangles come out anticlockwise.
/// </summary>
public static class Triangulator
{
    public readonly record struct V2(double X, double Y);

    static double Cross(V2 o, V2 a, V2 b) => (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);

    public static double Area(IReadOnlyList<V2> pts, IReadOnlyList<int> loop)
    {
        double s = 0;
        for (int i = 0, j = loop.Count - 1; i < loop.Count; j = i++)
            s += pts[loop[j]].X * pts[loop[i]].Y - pts[loop[i]].X * pts[loop[j]].Y;
        return s / 2;
    }

    static bool Inside(IReadOnlyList<V2> pts, IReadOnlyList<int> loop, V2 p)
    {
        bool inside = false;
        for (int i = 0, j = loop.Count - 1; i < loop.Count; j = i++)
        {
            var a = pts[loop[i]];
            var b = pts[loop[j]];
            if ((a.Y > p.Y) != (b.Y > p.Y) && p.X < (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X) inside = !inside;
        }
        return inside;
    }

    /// <summary>A point just inside the loop near its first edge, to tell which loops it's within.</summary>
    static V2 Probe(IReadOnlyList<V2> pts, IReadOnlyList<int> loop)
    {
        // The middle of the longest edge, nudged a hair to its left (inside, for an anticlockwise loop) — then
        // whichever side, the test is about other loops, which don't come that close.
        int best = 0;
        double bl = -1;
        for (int i = 0; i < loop.Count; i++)
        {
            var a = pts[loop[i]];
            var b = pts[loop[(i + 1) % loop.Count]];
            double l = (b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y);
            if (l > bl) { bl = l; best = i; }
        }
        var p = pts[loop[best]];
        var q = pts[loop[(best + 1) % loop.Count]];
        return new V2((p.X + q.X) / 2, (p.Y + q.Y) / 2);
    }

    /// <summary>Triangles (anticlockwise) filling the loops even-odd.</summary>
    public static List<(int A, int B, int C)> Fill(IReadOnlyList<V2> pts, IReadOnlyList<IReadOnlyList<int>> loopsIn)
    {
        // Tidy: no repeated neighbours, nothing too small to matter.
        var loops = new List<List<int>>();
        foreach (var l in loopsIn)
        {
            var clean = new List<int>();
            foreach (var i in l)
                if (clean.Count == 0 || !Same(pts[clean[^1]], pts[i])) clean.Add(i);
            while (clean.Count > 1 && Same(pts[clean[0]], pts[clean[^1]])) clean.RemoveAt(clean.Count - 1);
            if (clean.Count >= 3 && Math.Abs(Area(pts, clean)) > 1e-14) loops.Add(clean);
        }
        var tris = new List<(int, int, int)>();
        if (loops.Count == 0) return tris;
        // How deep each loop sits: even = an outline, odd = a hole.
        var probes = loops.Select(l => Probe(pts, l)).ToList();
        var depth = new int[loops.Count];
        var parent = new int[loops.Count];
        for (int i = 0; i < loops.Count; i++)
        {
            parent[i] = -1;
            double parentArea = double.MaxValue;
            for (int j = 0; j < loops.Count; j++)
            {
                if (i == j || !Inside(pts, loops[j], probes[i])) continue;
                depth[i]++;
                double a = Math.Abs(Area(pts, loops[j]));
                if (a < parentArea && a > Math.Abs(Area(pts, loops[i])) * 0.999999) { parentArea = a; parent[i] = j; }
            }
        }
        for (int o = 0; o < loops.Count; o++)
        {
            if (depth[o] % 2 != 0) continue;
            var outer = new List<int>(loops[o]);
            if (Area(pts, outer) < 0) outer.Reverse();
            var holes = Enumerable.Range(0, loops.Count).Where(h => depth[h] == depth[o] + 1 && parent[h] == o)
                .Select(h => { var l = new List<int>(loops[h]); if (Area(pts, l) > 0) l.Reverse(); return l; })
                .OrderByDescending(l => l.Max(i => pts[i].X)).ToList();
            int first = tris.Count;
            Earcut.Triangulate(pts, outer, holes.Cast<IReadOnlyList<int>>().ToList(), tris);
            Restore(pts, tris, first, [outer, .. holes]);
        }
        return tris;
    }

    /// <summary>
    /// Earcut drops outline points that lie in a straight line, but they're shared with the
    /// neighbouring face (and, laid out flat, a cylinder's end circle is a straight line): put
    /// each one back, splitting the triangle whose edge skipped it into a fan.
    /// </summary>
    static void Restore(IReadOnlyList<V2> pts, List<(int A, int B, int C)> tris, int first, List<List<int>> loops)
    {
        var at = new Dictionary<int, (int Loop, int Pos)>();
        for (int l = 0; l < loops.Count; l++)
            for (int i = 0; i < loops[l].Count; i++) at.TryAdd(loops[l][i], (l, i));
        // The points an edge p→q skipped along its loop (in order), if they all lie on it.
        List<int>? Skipped(int p, int q)
        {
            if (!at.TryGetValue(p, out var pp) || !at.TryGetValue(q, out var qq) || pp.Loop != qq.Loop) return null;
            var loop = loops[pp.Loop];
            int n = loop.Count, gap = (qq.Pos - pp.Pos + n) % n;
            if (gap < 2) return null;
            var a = pts[p];
            var b = pts[q];
            double len = Math.Sqrt(Dist2(a, b));
            if (len == 0) return null;
            var list = new List<int>();
            for (int k = 1; k < gap; k++)
            {
                int v = loop[(pp.Pos + k) % n];
                if (Math.Abs(Cross(a, b, pts[v])) / len > 1e-9 * Math.Max(1, len)) return null;
                list.Add(v);
            }
            return list;
        }
        int cap = tris.Count + 2 * loops.Sum(l => l.Count) + 16;
        for (int t = first; t < tris.Count && tris.Count < cap; t++)
        {
            int guard = 0;
            for (bool changed = true; changed && guard++ < 64;)
            {
                changed = false;
                var (a, b, c) = tris[t];
                // Each edge in the triangle's direction, with the corner opposite.
                foreach (var (p, q, far) in new[] { (a, b, c), (b, c, a), (c, a, b) })
                {
                    // Only a real triangle (a sliver along the outline would just make more slivers).
                    if (Math.Abs(Cross(pts[p], pts[q], pts[far])) <= 1e-12 * Dist2(pts[p], pts[q])) continue;
                    if (Skipped(p, q) is not { } mid || mid.Contains(far)) continue;
                    // A fan from the far corner over the points put back.
                    var chain = new List<int> { p };
                    chain.AddRange(mid);
                    chain.Add(q);
                    tris[t] = (chain[0], chain[1], far);
                    for (int k = 1; k + 1 < chain.Count; k++) tris.Add((chain[k], chain[k + 1], far));
                    changed = true;
                    break;
                }
            }
        }
    }

    static bool Same(V2 a, V2 b) => Math.Abs(a.X - b.X) < 1e-12 && Math.Abs(a.Y - b.Y) < 1e-12;

    static double Dist2(V2 a, V2 b) => (a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y);

    static long Key(int a, int b) => a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;

    /// <summary>
    /// Better-shaped triangles: flip each shared edge whose opposite corner sits inside the other
    /// triangle's circumcircle (Delaunay), never an edge of the outline.
    /// </summary>
    public static void Flip(IReadOnlyList<V2> pts, List<(int A, int B, int C)> tris, HashSet<long> fixedEdges)
    {
        // Each directed edge → its triangle; a queue of edges to look at (Lawson's algorithm).
        static long Dir(int a, int b) => ((long)a << 32) | (uint)b;
        var owner = new Dictionary<long, int>(tris.Count * 3);
        void Own(int t) { var (a, b, c) = tris[t]; owner[Dir(a, b)] = t; owner[Dir(b, c)] = t; owner[Dir(c, a)] = t; }
        void Disown(int t) { var (a, b, c) = tris[t]; owner.Remove(Dir(a, b)); owner.Remove(Dir(b, c)); owner.Remove(Dir(c, a)); }
        for (int t = 0; t < tris.Count; t++) Own(t);
        var queue = new Queue<(int, int)>();
        var queued = new HashSet<long>();
        void Push(int a, int b)
        {
            long k = Key(a, b);
            if (!fixedEdges.Contains(k) && queued.Add(k)) queue.Enqueue((a, b));
        }
        foreach (var (a, b, c) in tris) { Push(a, b); Push(b, c); Push(c, a); }
        long guard = 60L * tris.Count + 1000;
        while (queue.Count > 0 && guard-- > 0)
        {
            var (a, b) = queue.Dequeue();
            queued.Remove(Key(a, b));
            // t1 holds a→b, t2 holds b→a.
            if (!owner.TryGetValue(Dir(a, b), out int t1) || !owner.TryGetValue(Dir(b, a), out int t2)) continue;
            int c = Opposite(tris[t1], a, b), d = Opposite(tris[t2], b, a);
            var pa = pts[a];
            var pb = pts[b];
            var pc = pts[c];
            var pd = pts[d];
            if (!InCircle(pa, pb, pc, pd)) continue;
            if (Cross(pa, pd, pc) <= 1e-18 || Cross(pd, pb, pc) <= 1e-18) continue; // the flip would fold over
            Disown(t1);
            Disown(t2);
            tris[t1] = (a, d, c);
            tris[t2] = (d, b, c);
            Own(t1);
            Own(t2);
            Push(a, d); Push(d, b); Push(b, c); Push(c, a);
        }
    }

    /// <summary>The corner of a triangle that isn't on its edge a→b.</summary>
    static int Opposite((int A, int B, int C) t, int a, int b) =>
        t.A != a && t.A != b ? t.A : t.B != a && t.B != b ? t.B : t.C;

    static bool InCircle(V2 a, V2 b, V2 c, V2 d)
    {
        double ax = a.X - d.X, ay = a.Y - d.Y, bx = b.X - d.X, by = b.Y - d.Y, cx = c.X - d.X, cy = c.Y - d.Y;
        double det = (ax * ax + ay * ay) * (bx * cy - cx * by) - (bx * bx + by * by) * (ax * cy - cx * ay) + (cx * cx + cy * cy) * (ax * by - bx * ay);
        return det > 1e-18 * (Math.Abs(ax) + Math.Abs(bx) + Math.Abs(cx) + 1e-300);
    }

    /// <summary>The area the loops enclose even-odd (outlines minus holes, plus islands in holes…).</summary>
    public static double EvenOddArea(IReadOnlyList<V2> pts, IReadOnlyList<IReadOnlyList<int>> loops)
    {
        var good = loops.Where(l => l.Count >= 3).ToList();
        double total = 0;
        foreach (var l in good)
        {
            var probe = Probe(pts, l);
            int depth = good.Count(o => !ReferenceEquals(o, l) && Inside(pts, o, probe));
            total += (depth % 2 == 0 ? 1 : -1) * Math.Abs(Area(pts, l));
        }
        return total;
    }

    /// <summary>The edges of the loops, which flips and splits must keep.</summary>
    public static HashSet<long> Edges(IReadOnlyList<IReadOnlyList<int>> loops)
    {
        var set = new HashSet<long>();
        foreach (var l in loops)
            for (int i = 0; i < l.Count; i++) set.Add(Key(l[i], l[(i + 1) % l.Count]));
        return set;
    }

    public static long EdgeKey(int a, int b) => Key(a, b);
}
