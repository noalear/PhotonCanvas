using System.Collections.Immutable;
using PhotonCanvas.Core;

namespace PhotonCanvas.Doc.Geometry;

/// <summary>
/// Shape tools that work on outlines in millimetres: rounding corners, tidying imported
/// drawings, trimming a line back to where it crosses others, warping, finding circles, and
/// spacing points along a path.
/// </summary>
public static class PathTools
{
    // ---- rounded corners --------------------------------------------------------------

    /// <summary>
    /// Every corner where two straight edges meet becomes an arc of <paramref name="radius"/> mm
    /// (smaller where the edges are too short for it). Curves are left as they are.
    /// Returns the geometry and how many corners were rounded.
    /// </summary>
    public static (PathGeometry Geometry, int Rounded) RoundCorners(PathGeometry g, double radius)
    {
        int n = 0;
        if (!(radius > 0)) return (g, 0);
        var subs = g.Subpaths.Select(sp =>
        {
            var (r, k) = RoundSubpath(sp, radius);
            n += k;
            return r;
        }).ToList();
        return (new PathGeometry(subs), n);
    }

    static (Subpath, int) RoundSubpath(Subpath sp, double radius)
    {
        var segs = sp.Segs.ToList();
        if (segs.Count < 2 && !(sp.Closed && segs.Count >= 2)) return (sp, 0);
        // A closed outline gets its closing edge written out, so every corner sits between two segments.
        if (sp.Closed && segs[^1].To.Dist(sp.Start) > 1e-9) segs.Add(Seg.Line(sp.Start));
        int count = segs.Count;
        var pts = new Vec2[count + 1];
        pts[0] = sp.Start;
        for (int i = 0; i < count; i++) pts[i + 1] = segs[i].To;
        // Vertex i is where segment i−1 ends and segment i starts.
        var corners = new (Vec2 T1, Vec2 C1, Vec2 C2, Vec2 T2)?[count + 1];
        int rounded = 0;
        for (int v = 0; v <= count; v++)
        {
            int inSeg = v - 1, outSeg = v;
            if (sp.Closed)
            {
                if (v == count) continue; // same vertex as 0
                if (inSeg < 0) inSeg = count - 1;
            }
            else if (inSeg < 0 || outSeg >= count) continue;
            if (segs[inSeg].Kind != SegKind.Line || segs[outSeg].Kind != SegKind.Line) continue;
            var corner = pts[v];
            var prev = pts[inSeg];
            var next = segs[outSeg].To;
            double la = prev.Dist(corner), lb = next.Dist(corner);
            if (la < 1e-9 || lb < 1e-9) continue;
            var u = (prev - corner) / la;
            var w = (next - corner) / lb;
            double cos = Math.Clamp(u.Dot(w), -1, 1);
            double theta = Math.Acos(cos); // angle between the edges
            if (theta > Math.PI - 1e-3 || theta < 1e-3) continue; // straight on, or folding back
            double t = radius / Math.Tan(theta / 2);
            // Room on each edge: half of it (the other end may be rounded too), all of it at an open end.
            double roomA = sp.Closed || inSeg > 0 ? la / 2 : la, roomB = sp.Closed || outSeg < count - 1 ? lb / 2 : lb;
            double r = radius;
            if (t > Math.Min(roomA, roomB))
            {
                t = Math.Min(roomA, roomB);
                r = t * Math.Tan(theta / 2);
            }
            double sweep = Math.PI - theta;
            double k = 4.0 / 3 * Math.Tan(sweep / 4) * r;
            var t1 = corner + u * t;
            var t2 = corner + w * t;
            corners[v] = (t1, t1 - u * k, t2 - w * k, t2);
            rounded++;
        }
        if (rounded == 0) return (sp, 0);
        var b = ImmutableArray.CreateBuilder<Seg>();
        var start = sp.Closed && corners[0] is { } c0 ? c0.T2 : sp.Start;
        for (int i = 0; i < count; i++)
        {
            int endV = i + 1;
            var arc = sp.Closed && endV == count ? corners[0] : corners[endV];
            if (arc is { } a)
            {
                b.Add(Seg.Line(a.T1));
                b.Add(Seg.Cubic(a.C1, a.C2, a.T2));
            }
            else b.Add(segs[i]);
        }
        return (new Subpath(start, b.ToImmutable(), sp.Closed), rounded);
    }

    // ---- tidy up imported drawings ----------------------------------------------------------

    /// <summary>What <see cref="Cleanup"/> did.</summary>
    public sealed record CleanupResult(IReadOnlyList<PathGeometry> Geometries, int Joined, int Closed, int Duplicates, int Tiny)
    {
        public bool Changed => Joined + Closed + Duplicates + Tiny > 0;
    }

    /// <summary>
    /// Tidy several shapes' outlines together (world mm): leave out zero-length pieces, join open
    /// lines whose ends are within <paramref name="tol"/>, close lines that nearly meet themselves,
    /// and delete outlines that lie exactly on top of another. A joined line stays with the shape
    /// its first piece came from.
    /// </summary>
    public static CleanupResult Cleanup(IReadOnlyList<PathGeometry> shapes, double tol)
    {
        int tiny = 0, closed = 0, joined = 0, dupes = 0;
        var owned = new List<(int Shape, Subpath Sub)>();
        for (int s = 0; s < shapes.Count; s++)
            foreach (var sp in shapes[s].Subpaths)
            {
                var (clean, removed) = DropTiny(sp);
                tiny += removed;
                if (clean.Segs.IsEmpty) continue;
                owned.Add((s, clean));
            }

        Subpath CloseIfNear(Subpath sp)
        {
            if (sp.Closed || sp.Segs.Length < 2 || sp.End.Dist(sp.Start) > tol) return sp;
            closed++;
            var segs = sp.Segs.ToBuilder();
            var last = segs[^1];
            segs[^1] = last with { To = sp.Start, C1 = last.Kind == SegKind.Line ? sp.Start : last.C1, C2 = last.Kind == SegKind.Line ? sp.Start : last.C2 };
            return sp with { Segs = segs.ToImmutable(), Closed = true };
        }
        for (int i = 0; i < owned.Count; i++) owned[i] = owned[i] with { Sub = CloseIfNear(owned[i].Sub) };

        // Join open lines end to end, as long as any pair meets.
        bool any = true;
        while (any)
        {
            any = false;
            for (int i = 0; i < owned.Count && !any; i++)
            {
                if (owned[i].Sub.Closed) continue;
                for (int j = 0; j < owned.Count && !any; j++)
                {
                    if (i == j || owned[j].Sub.Closed) continue;
                    var a = owned[i].Sub;
                    var b = owned[j].Sub;
                    Subpath? m = a.End.Dist(b.Start) <= tol ? Join(a, b)
                        : a.End.Dist(b.End) <= tol ? Join(a, Reverse(b))
                        : a.Start.Dist(b.End) <= tol ? Join(b, a)
                        : a.Start.Dist(b.Start) <= tol ? Join(Reverse(a), b)
                        : null;
                    if (m is null) continue;
                    owned[i] = owned[i] with { Sub = CloseIfNear(m) };
                    owned.RemoveAt(j);
                    joined++;
                    any = true;
                }
            }
        }

        // Duplicates: the same points (either direction, any start), within the tolerance.
        var seen = new HashSet<string>();
        var kept = new List<(int Shape, Subpath Sub)>();
        foreach (var o in owned)
        {
            if (seen.Add(Signature(o.Sub, tol))) kept.Add(o);
            else dupes++;
        }
        var result = Enumerable.Range(0, shapes.Count).Select(s => new PathGeometry(kept.Where(k => k.Shape == s).Select(k => k.Sub))).ToList();
        return new CleanupResult(result, joined, closed, dupes, tiny);
    }

    static (Subpath, int) DropTiny(Subpath sp)
    {
        var b = ImmutableArray.CreateBuilder<Seg>();
        var p = sp.Start;
        int removed = 0;
        foreach (var s in sp.Segs)
        {
            bool zero = s.To.Dist(p) < 1e-6 && (s.Kind == SegKind.Line || s.C1.Dist(p) < 1e-6 && s.C2.Dist(p) < 1e-6);
            if (zero) { removed++; continue; }
            b.Add(s);
            p = s.To;
        }
        return removed == 0 ? (sp, 0) : (sp with { Segs = b.ToImmutable() }, removed);
    }

    static Subpath Join(Subpath a, Subpath b) => new(a.Start, [.. a.Segs, .. b.Segs], false);

    /// <summary>The same subpath run backwards.</summary>
    public static Subpath Reverse(Subpath sp)
    {
        var pts = new List<Vec2> { sp.Start };
        pts.AddRange(sp.Segs.Select(s => s.To));
        var b = ImmutableArray.CreateBuilder<Seg>();
        for (int i = sp.Segs.Length - 1; i >= 0; i--)
        {
            var s = sp.Segs[i];
            var to = pts[i];
            b.Add(s.Kind == SegKind.Line ? Seg.Line(to) : Seg.Cubic(s.C2, s.C1, to));
        }
        return new Subpath(sp.End, b.ToImmutable(), sp.Closed);
    }

    static string Signature(Subpath sp, double tol)
    {
        double q = Math.Max(tol, 1e-4);
        var pts = sp.Flatten(0.01).Select(p => (Math.Round(p.X / q), Math.Round(p.Y / q))).ToList();
        if (sp.Closed && pts.Count > 1 && pts[0] == pts[^1]) pts.RemoveAt(pts.Count - 1);
        pts.Sort();
        return (sp.Closed ? "c" : "o") + string.Join(";", pts.Distinct().Select(p => $"{p.Item1},{p.Item2}"));
    }

    // ---- trim -------------------------------------------------------------------------

    /// <summary>
    /// Trim: the piece of the outline nearest <paramref name="click"/>, between the places it
    /// crosses the <paramref name="cutters"/> (other outlines, world mm) or itself, is removed.
    /// Returns the new geometry (the trimmed outline becomes open lines; its other subpaths are
    /// unchanged) and the removed piece, or null when the outline crosses nothing there.
    /// </summary>
    public static (PathGeometry Result, List<Vec2> Removed)? Trim(PathGeometry world, Vec2 click, IReadOnlyList<IReadOnlyList<Vec2>> cutters, double tol = 0.01)
    {
        var subs = world.Subpaths.Where(s => !s.Segs.IsEmpty).ToList();
        if (subs.Count == 0) return null;
        // The subpath nearest the click.
        int k = -1;
        double best = double.PositiveInfinity;
        var flats = subs.Select(s => Loop(s.Flatten(tol), s.Closed)).ToList();
        for (int i = 0; i < flats.Count; i++)
        {
            var (d, _) = Nearest(flats[i], click);
            if (d < best) { best = d; k = i; }
        }
        var pts = flats[k];
        bool closedPath = subs[k].Closed;
        var cum = Cumulative(pts);
        double length = cum[^1];
        if (length < 1e-9) return null;
        var (_, sClick) = Nearest(pts, click);

        var all = new List<IReadOnlyList<Vec2>>(cutters);
        for (int i = 0; i < flats.Count; i++) if (i != k) all.Add(flats[i]);
        var cuts = new List<double>();
        for (int i = 0; i + 1 < pts.Count; i++)
        {
            var a = pts[i];
            var b = pts[i + 1];
            double segLen = cum[i + 1] - cum[i];
            if (segLen < 1e-12) continue;
            var box = Rect2.FromPoints(a, b).Inflate(1e-9);
            foreach (var c in all)
                for (int j = 0; j + 1 < c.Count; j++)
                {
                    if (!box.Intersects(Rect2.FromPoints(c[j], c[j + 1]).Inflate(1e-9))) continue;
                    if (Cross(a, b, c[j], c[j + 1]) is { } t) cuts.Add(cum[i] + t * segLen);
                }
            // Where the outline crosses itself.
            for (int j = i + 2; j + 1 < pts.Count; j++)
            {
                if (closedPath && i == 0 && j + 2 == pts.Count) continue; // neighbours across the start
                if (Cross(a, b, pts[j], pts[j + 1]) is { } t)
                {
                    cuts.Add(cum[i] + t * segLen);
                    if (CrossParam(pts[j], pts[j + 1], a, b) is { } u) cuts.Add(cum[j] + u * (cum[j + 1] - cum[j]));
                }
            }
        }
        cuts.Sort();
        var c2 = new List<double>();
        foreach (var c in cuts)
            if (c2.Count == 0 || c - c2[^1] > 1e-6) c2.Add(c);
        if (!closedPath) c2.RemoveAll(c => c < 1e-6 || c > length - 1e-6);
        if (c2.Count == 0) return null;

        List<List<Vec2>> keep = [];
        List<Vec2> removed;
        if (!closedPath)
        {
            var bounds = new List<double> { 0 };
            bounds.AddRange(c2);
            bounds.Add(length);
            int at = Math.Max(0, bounds.FindLastIndex(b => b <= sClick));
            at = Math.Min(at, bounds.Count - 2);
            removed = Extract(pts, cum, bounds[at], bounds[at + 1]);
            if (bounds[at] > 1e-9) keep.Add(Extract(pts, cum, 0, bounds[at]));
            if (bounds[at + 1] < length - 1e-9) keep.Add(Extract(pts, cum, bounds[at + 1], length));
        }
        else if (c2.Count == 1)
        {
            removed = pts;
        }
        else
        {
            int i = c2.FindLastIndex(c => c <= sClick);
            double from, to;
            if (i < 0 || i == c2.Count - 1) { from = c2[^1]; to = c2[0] + length; } // across the start
            else { from = c2[i]; to = c2[i + 1]; }
            removed = Extract(pts, cum, from, to);
            // The rest: from the far end of the removed piece round to its near end.
            double kf = to, kt = from + length;
            if (kf >= length - 1e-9) { kf -= length; kt -= length; }
            keep.Add(Extract(pts, cum, kf, kt));
        }
        var result = new List<Subpath>();
        for (int i = 0; i < subs.Count; i++)
        {
            if (i != k) { result.Add(subs[i]); continue; }
            foreach (var piece in keep.Where(p => p.Count >= 2))
            {
                var simple = Simplify(piece, tol / 2);
                result.Add(new Subpath(simple[0], [.. simple.Skip(1).Select(Seg.Line)], false));
            }
        }
        return (new PathGeometry(result), removed);
    }

    /// <summary>A flattened subpath, closed loops with the start repeated at the end.</summary>
    static List<Vec2> Loop(List<Vec2> pts, bool closed)
    {
        if (closed && pts.Count > 1 && pts[0].Dist(pts[^1]) > 1e-9) pts.Add(pts[0]);
        return pts;
    }

    /// <summary>The flattened outline as lines, closed loops closed: for other tools to cut against.</summary>
    public static List<IReadOnlyList<Vec2>> Polylines(PathGeometry world, double tol = 0.01) =>
        [.. world.Subpaths.Where(s => !s.Segs.IsEmpty).Select(s => (IReadOnlyList<Vec2>)Loop(s.Flatten(tol), s.Closed))];

    static double[] Cumulative(IReadOnlyList<Vec2> pts)
    {
        var cum = new double[pts.Count];
        for (int i = 1; i < pts.Count; i++) cum[i] = cum[i - 1] + pts[i - 1].Dist(pts[i]);
        return cum;
    }

    /// <summary>Distance from <paramref name="p"/> to the polyline, and how far along it the nearest point is.</summary>
    public static (double Distance, double Along) Nearest(IReadOnlyList<Vec2> pts, Vec2 p)
    {
        double best = double.PositiveInfinity, along = 0, run = 0;
        if (pts.Count == 1) return (pts[0].Dist(p), 0);
        for (int i = 0; i + 1 < pts.Count; i++)
        {
            var a = pts[i];
            var d = pts[i + 1] - a;
            double len2 = d.Dot(d), len = Math.Sqrt(len2);
            double t = len2 < 1e-18 ? 0 : Math.Clamp((p - a).Dot(d) / len2, 0, 1);
            double dist = (a + d * t).Dist(p);
            if (dist < best) { best = dist; along = run + t * len; }
            run += len;
        }
        return (best, along);
    }

    /// <summary>Where segment ab crosses segment cd, as a fraction along ab (null if they don't).</summary>
    static double? Cross(Vec2 a, Vec2 b, Vec2 c, Vec2 d)
    {
        var r = b - a;
        var s = d - c;
        double den = r.Cross(s);
        if (Math.Abs(den) < 1e-15) return null;
        double t = (c - a).Cross(s) / den, u = (c - a).Cross(r) / den;
        return t >= -1e-9 && t <= 1 + 1e-9 && u >= -1e-9 && u <= 1 + 1e-9 ? Math.Clamp(t, 0, 1) : null;
    }

    static double? CrossParam(Vec2 a, Vec2 b, Vec2 c, Vec2 d) => Cross(a, b, c, d);

    /// <summary>The polyline from <paramref name="from"/> to <paramref name="to"/> mm along it (for a closed loop, <paramref name="to"/> may go round past the end once).</summary>
    static List<Vec2> Extract(IReadOnlyList<Vec2> pts, double[] cum, double from, double to)
    {
        double length = cum[^1];
        var o = new List<Vec2>();
        Vec2 At(double s)
        {
            s = Math.Clamp(s, 0, length);
            int i = Array.BinarySearch(cum, s);
            if (i < 0) i = ~i - 1;
            i = Math.Clamp(i, 0, pts.Count - 2);
            double seg = cum[i + 1] - cum[i];
            return seg < 1e-12 ? pts[i] : Vec2.Lerp(pts[i], pts[i + 1], (s - cum[i]) / seg);
        }
        void Run(double f, double t)
        {
            o.Add(At(f));
            for (int i = 0; i < pts.Count; i++)
                if (cum[i] > f + 1e-9 && cum[i] < t - 1e-9) o.Add(pts[i]);
            o.Add(At(t));
        }
        if (to <= length + 1e-9) Run(from, Math.Min(to, length));
        else
        {
            Run(from, length);
            var tail = new List<Vec2>(o);
            o.Clear();
            Run(0, to - length);
            tail.AddRange(o.Skip(1));
            return tail;
        }
        return o;
    }

    /// <summary>Douglas–Peucker: fewer points, within <paramref name="eps"/> of the original line.</summary>
    public static List<Vec2> Simplify(List<Vec2> pts, double eps)
    {
        if (pts.Count < 3) return [.. pts];
        var keep = new bool[pts.Count];
        keep[0] = keep[^1] = true;
        var stack = new Stack<(int, int)>();
        stack.Push((0, pts.Count - 1));
        while (stack.Count > 0)
        {
            var (a, b) = stack.Pop();
            double max = 0;
            int at = -1;
            for (int i = a + 1; i < b; i++)
            {
                double d = Geom.SegmentDistance(pts[i], pts[a], pts[b]);
                if (d > max) { max = d; at = i; }
            }
            if (at < 0 || max <= eps) continue;
            keep[at] = true;
            stack.Push((a, at));
            stack.Push((at, b));
        }
        return [.. pts.Where((_, i) => keep[i])];
    }

    // ---- along a path -------------------------------------------------------------------

    /// <summary>
    /// <paramref name="count"/> places evenly along a path (world mm) and the path's direction there
    /// (degrees): end to end on an open path, evenly round a closed one.
    /// </summary>
    public static List<(Vec2 Point, double AngleDeg)> PointsAlong(IReadOnlyList<Vec2> pts, bool closed, int count)
    {
        var line = closed ? Loop([.. pts], true) : [.. pts];
        var o = new List<(Vec2, double)>();
        if (line.Count < 2 || count < 1) return o;
        var cum = Cumulative(line);
        double length = cum[^1];
        for (int i = 0; i < count; i++)
        {
            double s = closed ? length * i / count : count == 1 ? length / 2 : length * i / (count - 1);
            o.Add(At(line, cum, s));
        }
        return o;
    }

    /// <summary>The point <paramref name="s"/> mm along a polyline and its direction (degrees). Past either end it carries straight on.</summary>
    public static (Vec2 Point, double AngleDeg) At(IReadOnlyList<Vec2> pts, double[] cum, double s)
    {
        int n = pts.Count;
        int i = Array.BinarySearch(cum, s);
        if (i < 0) i = ~i - 1;
        i = Math.Clamp(i, 0, n - 2);
        // Skip zero-length pieces for the direction.
        int j = i;
        while (j < n - 2 && cum[j + 1] - cum[j] < 1e-12) j++;
        var d = pts[j + 1] - pts[j];
        double len = d.Length;
        var u = len < 1e-12 ? new Vec2(1, 0) : d / len;
        var p = pts[i] + u * (s - cum[i]);
        return (p, Math.Atan2(u.Y, u.X) * 180 / Math.PI);
    }

    public static double[] CumulativeLengths(IReadOnlyList<Vec2> pts) => Cumulative(pts);

    /// <summary>
    /// Copies of the shapes <paramref name="ids"/> (as one unit, centred) spaced evenly along a
    /// path in world mm, turned to follow it or not. The originals stay unless <paramref name="removeOriginals"/>.
    /// </summary>
    public static (DocState State, List<Guid> Ids) CopyAlong(DocState s, IReadOnlySet<Guid> ids, IReadOnlyList<Vec2> path, bool closed, int count, bool follow, bool removeOriginals)
    {
        var originals = s.Shapes.Where(x => ids.Contains(x.Id)).ToList();
        var box = s.Bounds(ids);
        if (originals.Count == 0 || box.IsEmpty) return (s, []);
        var copies = new List<Shape>();
        foreach (var (p, angle) in PointsAlong(path, closed, Math.Clamp(count, 1, 5000)))
        {
            var t = Affine.Translate(-box.Center.X, -box.Center.Y).Then(Affine.Rotate(follow ? angle : 0)).Then(Affine.Translate(p));
            copies.AddRange(Ops.CopyAsUnit(originals, t));
        }
        if (removeOriginals) s = Ops.Delete(s, ids);
        return (Ops.AddShapes(s, copies), copies.Select(c => c.Id).ToList());
    }

    // ---- warp -----------------------------------------------------------------------------

    /// <summary>
    /// Every point of the outline moved by <paramref name="map"/>: the outline is followed closely,
    /// moved, then turned back into as few lines and curves as <paramref name="tol"/> allows.
    /// </summary>
    public static PathGeometry Warp(PathGeometry world, Func<Vec2, Vec2> map, double tol = 0.02)
    {
        var subs = new List<Subpath>();
        foreach (var sp in world.Subpaths.Where(s => !s.Segs.IsEmpty))
        {
            var dense = Densify(sp.Flatten(tol / 2), 0.5);
            if (sp.Closed && dense.Count > 1 && dense[0].Dist(dense[^1]) < 1e-9) dense.RemoveAt(dense.Count - 1);
            var moved = dense.Select(map).ToList();
            if (sp.Closed && moved.Count >= 3) subs.Add(CurveFit.Closed(moved, tol));
            else if (moved.Count >= 2)
            {
                var simple = Simplify(moved, tol / 2);
                subs.Add(new Subpath(simple[0], [.. simple.Skip(1).Select(Seg.Line)], sp.Closed));
            }
        }
        return new PathGeometry(subs);
    }

    /// <summary>Extra points so no step is longer than <paramref name="step"/> mm (straight lines bend when warped).</summary>
    static List<Vec2> Densify(List<Vec2> pts, double step)
    {
        var o = new List<Vec2>(pts.Count * 2);
        for (int i = 0; i < pts.Count; i++)
        {
            if (i > 0)
            {
                var a = pts[i - 1];
                int n = (int)Math.Ceiling(a.Dist(pts[i]) / step);
                for (int k = 1; k < n; k++) o.Add(Vec2.Lerp(a, pts[i], (double)k / n));
            }
            o.Add(pts[i]);
        }
        return o;
    }

    // ---- simplify and cut shapes -------------------------------------------------------

    /// <summary>
    /// Fewer points: the outline followed to within <paramref name="tol"/> mm and turned back into
    /// as few lines and curves as that allows (imports and traces often have thousands).
    /// Returns the geometry and the point counts before and after.
    /// </summary>
    public static (PathGeometry Geometry, int Before, int After) Simplify(PathGeometry world, double tol)
    {
        int before = world.Subpaths.Sum(s => s.Segs.Length), after = 0;
        var subs = new List<Subpath>();
        foreach (var sp in world.Subpaths.Where(s => !s.Segs.IsEmpty))
        {
            var pts = sp.Flatten(tol / 4);
            Subpath next;
            if (sp.Closed && pts.Count >= 4)
            {
                if (pts[0].Dist(pts[^1]) < 1e-9) pts.RemoveAt(pts.Count - 1);
                next = CurveFit.Closed(pts, tol);
            }
            else
            {
                var simple = Simplify(pts, tol);
                next = new Subpath(simple[0], [.. simple.Skip(1).Select(Seg.Line)], sp.Closed);
            }
            // Never make it worse.
            if (next.Segs.Length >= sp.Segs.Length) next = sp;
            after += next.Segs.Length;
            subs.Add(next);
        }
        return (new PathGeometry(subs), before, after);
    }

    /// <summary>
    /// Cut overlapping shapes into separate pieces where they cross (each overlap becomes its own
    /// piece, and what's left of each shape another). Pieces come back in world mm.
    /// </summary>
    public static List<PathGeometry> CutShapes(IReadOnlyList<PathGeometry> shapes, double tol = 0.01)
    {
        var pieces = new List<PathGeometry>();
        foreach (var raw in shapes)
        {
            var s = new PathGeometry(raw.Subpaths.Where(sp => sp.Closed));
            if (s.IsEmpty) continue;
            var next = new List<PathGeometry>();
            var rest = s;
            foreach (var p in pieces)
            {
                var both = ShapeGeometry.Boolean(p, s, BoolOp.Intersect, tol);
                var only = ShapeGeometry.Boolean(p, s, BoolOp.Subtract, tol);
                if (!both.IsEmpty) next.Add(both);
                if (!only.IsEmpty) next.Add(only);
                rest = ShapeGeometry.Boolean(rest, p, BoolOp.Subtract, tol);
            }
            if (!rest.IsEmpty) next.Add(rest);
            pieces = next;
        }
        return pieces;
    }

    // ---- circles ---------------------------------------------------------------------------

    /// <summary>The circle this outline is (centre and diameter, mm), or null if it isn't one closed circle.</summary>
    public static (Vec2 Centre, double Diameter)? Circle(PathGeometry world)
    {
        var subs = world.Subpaths.Where(s => !s.Segs.IsEmpty).ToList();
        if (subs.Count != 1 || !subs[0].Closed) return null;
        var pts = subs[0].Flatten(0.005);
        if (pts.Count < 8) return null;
        var box = Rect2.Bounds(pts);
        double r = (box.Width + box.Height) / 4;
        if (r < 0.01) return null;
        double tol = Math.Max(0.02, r * 0.015);
        if (Math.Abs(box.Width - box.Height) > 2 * tol) return null;
        var c = box.Center;
        return pts.All(p => Math.Abs(p.Dist(c) - r) <= tol) ? (c, 2 * r) : null;
    }
}
