using System.Collections.Immutable;
using YTool.Core;

namespace YTool.Doc;

/// <summary>A point on a path: subpath index and anchor index (0 = the subpath's start).</summary>
public readonly record struct NodeRef(int Sub, int Index);

public enum HandleSide { In, Out }

/// <summary>
/// Point-by-point editing of path geometry (in the shape's own coordinates).
/// Anchor k of a subpath is its start (k = 0) or the end of segment k−1.
/// On a closed subpath whose last segment returns to the start, that final
/// anchor is the same point as anchor 0 and is edited together with it.
/// </summary>
public static class NodeEdit
{
    const double Same = 1e-9;

    static bool ClosesOnStart(Subpath sp) => sp.Closed && !sp.Segs.IsEmpty && sp.Segs[^1].To.Dist(sp.Start) <= 1e-6;

    /// <summary>Number of editable anchors in a subpath.</summary>
    public static int AnchorCount(Subpath sp) => sp.Segs.Length + (ClosesOnStart(sp) ? 0 : 1);

    public static IEnumerable<NodeRef> Nodes(PathGeometry g)
    {
        for (int s = 0; s < g.Subpaths.Length; s++)
            for (int i = 0; i < AnchorCount(g.Subpaths[s]); i++) yield return new(s, i);
    }

    public static Vec2 Anchor(PathGeometry g, NodeRef n) =>
        n.Index == 0 ? g.Subpaths[n.Sub].Start : g.Subpaths[n.Sub].Segs[n.Index - 1].To;

    /// <summary>Segment arriving at the anchor (null at the start of an open path).</summary>
    static int? Incoming(Subpath sp, int i) => i > 0 ? i - 1 : ClosesOnStart(sp) ? sp.Segs.Length - 1 : null;

    /// <summary>Segment leaving the anchor (null at the end of an open path).</summary>
    static int? Outgoing(Subpath sp, int i) => i < sp.Segs.Length ? i : null;

    /// <summary>Handle positions; null when that side is a straight line or absent.</summary>
    public static (Vec2? In, Vec2? Out) Handles(PathGeometry g, NodeRef n)
    {
        var sp = g.Subpaths[n.Sub];
        Vec2? hin = Incoming(sp, n.Index) is { } a && sp.Segs[a].Kind == SegKind.Cubic ? sp.Segs[a].C2 : null;
        Vec2? hout = Outgoing(sp, n.Index) is { } b && sp.Segs[b].Kind == SegKind.Cubic ? sp.Segs[b].C1 : null;
        return (hin, hout);
    }

    static PathGeometry Replace(PathGeometry g, int sub, Subpath sp) =>
        new(g.Subpaths.SetItem(sub, sp));

    public static PathGeometry MoveAnchor(PathGeometry g, NodeRef n, Vec2 to)
    {
        var sp = g.Subpaths[n.Sub];
        var delta = to - Anchor(g, n);
        var segs = sp.Segs.ToBuilder();
        var start = sp.Start;
        if (n.Index == 0) start = to;
        if (Incoming(sp, n.Index) is { } a)
        {
            var s = segs[a];
            segs[a] = s with { To = to, C2 = s.Kind == SegKind.Cubic ? s.C2 + delta : to, C1 = s.Kind == SegKind.Cubic ? s.C1 : to };
        }
        if (Outgoing(sp, n.Index) is { } b && segs[b].Kind == SegKind.Cubic)
            segs[b] = segs[b] with { C1 = segs[b].C1 + delta };
        return Replace(g, n.Sub, new Subpath(start, segs.ToImmutable(), sp.Closed));
    }

    /// <summary>
    /// Drag a curve handle. With <paramref name="mirror"/>, the opposite handle
    /// turns to stay in line (keeping its own length), so the point stays smooth.
    /// </summary>
    public static PathGeometry MoveHandle(PathGeometry g, NodeRef n, HandleSide side, Vec2 to, bool mirror)
    {
        var sp = g.Subpaths[n.Sub];
        var anchor = Anchor(g, n);
        var segs = sp.Segs.ToBuilder();
        int? inSeg = Incoming(sp, n.Index), outSeg = Outgoing(sp, n.Index);
        if (side == HandleSide.In && inSeg is { } a && segs[a].Kind == SegKind.Cubic) segs[a] = segs[a] with { C2 = to };
        else if (side == HandleSide.Out && outSeg is { } b && segs[b].Kind == SegKind.Cubic) segs[b] = segs[b] with { C1 = to };
        else return g;
        if (mirror)
        {
            var dir = anchor - to;
            if (dir.Length > Same)
            {
                if (side == HandleSide.In && outSeg is { } o && segs[o].Kind == SegKind.Cubic)
                {
                    double len = segs[o].C1.Dist(anchor);
                    segs[o] = segs[o] with { C1 = anchor + dir / dir.Length * len };
                }
                else if (side == HandleSide.Out && inSeg is { } i && segs[i].Kind == SegKind.Cubic)
                {
                    double len = segs[i].C2.Dist(anchor);
                    segs[i] = segs[i] with { C2 = anchor + dir / dir.Length * len };
                }
            }
        }
        return Replace(g, n.Sub, new Subpath(sp.Start, segs.ToImmutable(), sp.Closed));
    }

    /// <summary>True when both handles exist and line up through the anchor.</summary>
    public static bool IsSmooth(PathGeometry g, NodeRef n)
    {
        var (hin, hout) = Handles(g, n);
        if (hin is not { } a || hout is not { } b) return false;
        var p = Anchor(g, n);
        var u = p - a;
        var v = b - p;
        if (u.Length < Same || v.Length < Same) return false;
        return Math.Abs(u.Cross(v)) / (u.Length * v.Length) < 0.035 && u.Dot(v) > 0; // within ~2°
    }

    /// <summary>Sharp ↔ smooth. Sharp pulls both handles into the point; smooth lines them up (lines become curves).</summary>
    public static PathGeometry ToggleSmooth(PathGeometry g, NodeRef n)
    {
        var sp = g.Subpaths[n.Sub];
        var p = Anchor(g, n);
        var segs = sp.Segs.ToBuilder();
        int? inSeg = Incoming(sp, n.Index), outSeg = Outgoing(sp, n.Index);
        Vec2 Prev(int s) => s == 0 ? sp.Start : segs[s - 1].To;
        if (IsSmooth(g, n))
        {
            if (inSeg is { } a) segs[a] = segs[a] with { C2 = p };
            if (outSeg is { } b) segs[b] = segs[b] with { C1 = p };
        }
        else
        {
            var before = inSeg is { } a0 ? Prev(a0) : p;
            var after = outSeg is { } b0 ? segs[b0].To : p;
            var dir = after - before;
            if (dir.Length < Same) return g;
            dir /= dir.Length;
            if (inSeg is { } a)
            {
                var s = segs[a];
                double len = p.Dist(Prev(a)) / 3;
                var c1 = s.Kind == SegKind.Cubic ? s.C1 : Vec2.Lerp(Prev(a), p, 1.0 / 3);
                segs[a] = Seg.Cubic(c1, p - dir * len, p);
            }
            if (outSeg is { } b)
            {
                var s = segs[b];
                double len = p.Dist(s.To) / 3;
                var c2 = s.Kind == SegKind.Cubic ? s.C2 : Vec2.Lerp(p, s.To, 2.0 / 3);
                segs[b] = Seg.Cubic(p + dir * len, c2, s.To);
            }
        }
        return Replace(g, n.Sub, new Subpath(sp.Start, segs.ToImmutable(), sp.Closed));
    }

    /// <summary>Split segment <paramref name="seg"/> at parameter t (0–1), adding a point there.</summary>
    public static PathGeometry Insert(PathGeometry g, int sub, int seg, double t)
    {
        var sp = g.Subpaths[sub];
        var s = sp.Segs[seg];
        var p0 = seg == 0 ? sp.Start : sp.Segs[seg - 1].To;
        Seg first, second;
        if (s.Kind == SegKind.Line)
        {
            var m = Vec2.Lerp(p0, s.To, t);
            first = Seg.Line(m);
            second = Seg.Line(s.To);
        }
        else
        {
            var a = Vec2.Lerp(p0, s.C1, t); var b = Vec2.Lerp(s.C1, s.C2, t); var c = Vec2.Lerp(s.C2, s.To, t);
            var d = Vec2.Lerp(a, b, t); var e = Vec2.Lerp(b, c, t);
            var m = Vec2.Lerp(d, e, t);
            first = Seg.Cubic(a, d, m);
            second = Seg.Cubic(e, c, s.To);
        }
        var segs = sp.Segs.RemoveAt(seg).InsertRange(seg, ImmutableArray.Create(first, second));
        return Replace(g, sub, new Subpath(sp.Start, segs, sp.Closed));
    }

    /// <summary>Remove a point; its two segments merge into one. Subpaths left with no segments disappear.</summary>
    public static PathGeometry Delete(PathGeometry g, NodeRef n)
    {
        var sp = g.Subpaths[n.Sub];
        if (AnchorCount(sp) <= 2) return new(g.Subpaths.RemoveAt(n.Sub));
        int? inSeg = Incoming(sp, n.Index), outSeg = Outgoing(sp, n.Index);
        var segs = sp.Segs.ToBuilder();
        var start = sp.Start;
        if (inSeg is { } a && outSeg is { } b)
        {
            var sa = segs[a];
            var sb = segs[b];
            var merged = sa.Kind == SegKind.Line && sb.Kind == SegKind.Line
                ? Seg.Line(sb.To)
                : Seg.Cubic(sa.Kind == SegKind.Cubic ? sa.C1 : (a == 0 ? sp.Start : segs[a - 1].To), sb.Kind == SegKind.Cubic ? sb.C2 : sb.To, sb.To);
            if (n.Index == 0)
            {
                // Removing the start of a closed loop: the loop now starts at the next point.
                start = sb.To;
                segs[a] = merged;
                segs.RemoveAt(b);
            }
            else
            {
                segs[a] = merged;
                segs.RemoveAt(b);
            }
        }
        else if (outSeg is { } only)
        {
            start = segs[only].To; // removing the first point of an open path
            segs.RemoveAt(only);
        }
        else if (inSeg is { } last) segs.RemoveAt(last); // removing the last point of an open path
        return Replace(g, n.Sub, new Subpath(start, segs.ToImmutable(), sp.Closed));
    }

    /// <summary>Nearest point on any segment to <paramref name="p"/>: (subpath, segment, t, distance).</summary>
    public static (int Sub, int Seg, double T, double Dist)? NearestOnSegment(PathGeometry g, Vec2 p)
    {
        (int, int, double, double)? best = null;
        for (int s = 0; s < g.Subpaths.Length; s++)
        {
            var sp = g.Subpaths[s];
            var prev = sp.Start;
            for (int i = 0; i < sp.Segs.Length; i++)
            {
                var seg = sp.Segs[i];
                const int N = 48;
                for (int k = 0; k <= N; k++)
                {
                    double t = (double)k / N;
                    var q = seg.Kind == SegKind.Line ? Vec2.Lerp(prev, seg.To, t) : CubicAt(prev, seg, t);
                    double d = q.Dist(p);
                    if (best is null || d < best.Value.Item4) best = (s, i, t, d);
                }
                prev = seg.To;
            }
        }
        return best;
    }

    static Vec2 CubicAt(Vec2 p0, Seg s, double t)
    {
        double mt = 1 - t;
        return p0 * (mt * mt * mt) + s.C1 * (3 * t * mt * mt) + s.C2 * (3 * t * t * mt) + s.To * (t * t * t);
    }
}
