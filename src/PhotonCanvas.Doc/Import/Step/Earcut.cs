using V2 = PhotonCanvas.Doc.Import.Step.Triangulator.V2;

namespace PhotonCanvas.Doc.Import.Step;

// Follows the design of Mapbox's earcut (https://github.com/mapbox/earcut), under this licence:
//
//   ISC License
//
//   Copyright (c) 2016, Mapbox
//
//   Permission to use, copy, modify, and/or distribute this software for any purpose
//   with or without fee is hereby granted, provided that the above copyright notice
//   and this permission notice appear in all copies.
//
//   THE SOFTWARE IS PROVIDED "AS IS" AND ISC DISCLAIMS ALL WARRANTIES WITH REGARD TO
//   THIS SOFTWARE INCLUDING ALL IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS.
//   IN NO EVENT SHALL ISC BE LIABLE FOR ANY SPECIAL, DIRECT, INDIRECT, OR
//   CONSEQUENTIAL DAMAGES OR ANY DAMAGES WHATSOEVER RESULTING FROM LOSS OF USE, DATA
//   OR PROFITS, WHETHER IN AN ACTION OF CONTRACT, NEGLIGENCE OR OTHER TORTIOUS ACTION,
//   ARISING OUT OF OR IN CONNECTION WITH THE USE OR PERFORMANCE OF THIS SOFTWARE.

/// <summary>
/// Ear clipping of an outline with holes, after the design of Mapbox's "earcut" (ISC licence):
/// each hole joined to the outline by a bridge from its leftmost point, ears clipped, and when no
/// clean ear is left, straight-line and repeated points removed, small self-crossings cured, and
/// finally the polygon split along a good diagonal. Copes with what CAD outlines are full of:
/// points in a straight line, holes lined up exactly with the outline, bridge points repeated.
/// Outline anticlockwise, holes clockwise (y up); triangles come out anticlockwise.
/// </summary>
static class Earcut
{

    sealed class Node(int i, double x, double y)
    {
        public readonly int I = i;
        public readonly double X = x, Y = y;
        public Node Prev = null!, Next = null!;
        public bool Steiner;
    }

    public static void Triangulate(IReadOnlyList<V2> pts, IReadOnlyList<int> outer, IReadOnlyList<IReadOnlyList<int>> holes, List<(int, int, int)> tris)
    {
        var outerNode = Ring(pts, outer);
        if (outerNode is null || outerNode.Next == outerNode.Prev) return;
        if (holes.Count > 0)
        {
            var queue = new List<Node>();
            foreach (var h in holes)
            {
                var list = Ring(pts, h);
                if (list is null) continue;
                if (list == list.Next) list.Steiner = true;
                queue.Add(Leftmost(list));
            }
            queue.Sort((a, b) => a.X.CompareTo(b.X));
            foreach (var h in queue) outerNode = EliminateHole(h, outerNode);
        }
        Linked(outerNode, tris, 0, 0);
    }

    /// <summary>A loop as a ring of nodes, in the order given (the caller has set the direction).</summary>
    static Node? Ring(IReadOnlyList<V2> pts, IReadOnlyList<int> loop)
    {
        Node? last = null;
        foreach (var i in loop) last = Insert(i, pts[i].X, pts[i].Y, last);
        if (last is not null && Equals(last, last.Next)) { Remove(last); last = last.Next; }
        return last;
    }

    static Node Insert(int i, double x, double y, Node? last)
    {
        var p = new Node(i, x, y);
        if (last is null) { p.Prev = p; p.Next = p; }
        else
        {
            p.Next = last.Next;
            p.Prev = last;
            last.Next.Prev = p;
            last.Next = p;
        }
        return p;
    }

    static void Remove(Node p)
    {
        p.Next.Prev = p.Prev;
        p.Prev.Next = p.Next;
    }

    static bool Equals(Node a, Node b) => a.X == b.X && a.Y == b.Y;

    /// <summary>Earcut's signed area of a corner: negative for a left turn (a convex corner of an anticlockwise outline).</summary>
    static double Area(Node p, Node q, Node r) => (q.Y - p.Y) * (r.X - q.X) - (q.X - p.X) * (r.Y - q.Y);

    /// <summary>In a straight line to within rounding (points along a cylinder's edge, laid out flat, differ only in the last digits).</summary>
    static bool Flat(Node p, Node q, Node r)
    {
        double ax = q.X - p.X, ay = q.Y - p.Y, bx = r.X - q.X, by = r.Y - q.Y;
        double scale = Math.Sqrt((ax * ax + ay * ay) * (bx * bx + by * by));
        return Math.Abs(Area(p, q, r)) <= 1e-10 * scale;
    }

    static bool PointInTriangle(double ax, double ay, double bx, double by, double cx, double cy, double px, double py) =>
        (cx - px) * (ay - py) >= (ax - px) * (cy - py) &&
        (ax - px) * (by - py) >= (bx - px) * (ay - py) &&
        (bx - px) * (cy - py) >= (cx - px) * (by - py);

    static void Linked(Node? ear, List<(int, int, int)> tris, int pass, int depth)
    {
        if (ear is null || depth > 64) return;
        var stop = ear;
        while (ear.Prev != ear.Next)
        {
            var prev = ear.Prev;
            var next = ear.Next;
            if (IsEar(ear))
            {
                tris.Add((prev.I, ear.I, next.I));
                Remove(ear);
                // Skipping the next corner gives fewer slivers.
                ear = next.Next;
                stop = next.Next;
                continue;
            }
            ear = next;
            if (ear == stop)
            {
                if (pass == 0) Linked(Filter(ear, null), tris, 1, depth + 1);
                else if (pass == 1)
                {
                    ear = Cure(Filter(ear, null), tris);
                    Linked(ear, tris, 2, depth + 1);
                }
                else if (pass == 2) Split(ear, tris, depth + 1);
                break;
            }
        }
    }

    static bool IsEar(Node ear)
    {
        var a = ear.Prev;
        var b = ear;
        var c = ear.Next;
        if (Area(a, b, c) >= 0 || Flat(a, b, c)) return false; // reflex or flat: can't be an ear
        // No other point inside the ear (only reflex ones can be).
        for (var p = ear.Next.Next; p != ear.Prev; p = p.Next)
            if (PointInTriangle(a.X, a.Y, b.X, b.Y, c.X, c.Y, p.X, p.Y) && Area(p.Prev, p, p.Next) >= 0) return false;
        return true;
    }

    /// <summary>Remove repeated points and points in a straight line.</summary>
    static Node? Filter(Node? start, Node? end)
    {
        if (start is null) return null;
        end ??= start;
        var p = start;
        bool again;
        do
        {
            again = false;
            if (!p.Steiner && (Equals(p, p.Next) || Flat(p.Prev, p, p.Next)))
            {
                Remove(p);
                p = end = p.Prev;
                if (p == p.Next) break;
                again = true;
            }
            else p = p.Next;
        } while (again || p != end);
        return end;
    }

    /// <summary>Two neighbouring edges that cross: clip the small triangle they make.</summary>
    static Node? Cure(Node? start, List<(int, int, int)> tris)
    {
        if (start is null) return null;
        var p = start;
        do
        {
            var a = p.Prev;
            var b = p.Next.Next;
            if (!Equals(a, b) && Intersects(a, p, p.Next, b) && LocallyInside(a, b) && LocallyInside(b, a))
            {
                tris.Add((a.I, p.I, b.I));
                Remove(p);
                Remove(p.Next);
                p = start = b;
            }
            p = p.Next;
        } while (p != start);
        return Filter(p, null);
    }

    /// <summary>Split the polygon in two along a good diagonal and do each half.</summary>
    static void Split(Node start, List<(int, int, int)> tris, int depth)
    {
        var a = start;
        do
        {
            var b = a.Next.Next;
            while (b != a.Prev)
            {
                if (a.I != b.I && ValidDiagonal(a, b))
                {
                    var c = SplitPolygon(a, b);
                    a = Filter(a, a.Next)!;
                    c = Filter(c, c.Next)!;
                    Linked(a, tris, 0, depth);
                    Linked(c, tris, 0, depth);
                    return;
                }
                b = b.Next;
            }
            a = a.Next;
        } while (a != start);
    }

    static Node EliminateHole(Node hole, Node outerNode)
    {
        var bridge = Bridge(hole, outerNode);
        if (bridge is null) return outerNode;
        var bridgeReverse = SplitPolygon(bridge, hole);
        // Tidy round the cut. The tidying can take out the node the outline was known by, so go on from
        // one it returns (always still in the ring).
        Filter(bridgeReverse, bridgeReverse.Next);
        return Filter(bridge, bridge.Next) ?? outerNode;
    }

    /// <summary>An outline point the hole's leftmost point can be joined to: along a ray to the left, then the best-angled point that can see it.</summary>
    static Node? Bridge(Node hole, Node outerNode)
    {
        var p = outerNode;
        double hx = hole.X, hy = hole.Y, qx = double.NegativeInfinity;
        Node? m = null;
        do
        {
            if (hy <= p.Y && hy >= p.Next.Y && p.Next.Y != p.Y)
            {
                double x = p.X + (hy - p.Y) * (p.Next.X - p.X) / (p.Next.Y - p.Y);
                if (x <= hx && x > qx)
                {
                    qx = x;
                    if (x == hx)
                    {
                        if (hy == p.Y) return p;
                        if (hy == p.Next.Y) return p.Next;
                    }
                    m = p.X < p.Next.X ? p : p.Next;
                }
            }
            p = p.Next;
        } while (p != outerNode);
        if (m is null) return null;
        if (hx == qx) return m; // the hole touches the outline: its nearer end
        var stop = m;
        double mx = m.X, my = m.Y, tanMin = double.PositiveInfinity;
        p = m;
        do
        {
            if (hx >= p.X && p.X >= mx && hx != p.X &&
                PointInTriangle(hy < my ? hx : qx, hy, mx, my, hy < my ? qx : hx, hy, p.X, p.Y))
            {
                double tan = Math.Abs(hy - p.Y) / (hx - p.X);
                if (LocallyInside(p, hole) && (tan < tanMin || (tan == tanMin && (p.X > m.X || (p.X == m.X && SectorContainsSector(m, p))))))
                {
                    m = p;
                    tanMin = tan;
                }
            }
            p = p.Next;
        } while (p != stop);
        return m;
    }

    static bool SectorContainsSector(Node m, Node p) => Area(m.Prev, m, p.Prev) < 0 && Area(p.Next, m, m.Next) < 0;

    static Node Leftmost(Node start)
    {
        var p = start;
        var left = start;
        do
        {
            if (p.X < left.X || (p.X == left.X && p.Y < left.Y)) left = p;
            p = p.Next;
        } while (p != start);
        return left;
    }

    static bool ValidDiagonal(Node a, Node b) =>
        a.Next.I != b.I && a.Prev.I != b.I && !IntersectsPolygon(a, b) &&
        (LocallyInside(a, b) && LocallyInside(b, a) && MiddleInside(a, b) && (Area(a.Prev, a, b.Prev) != 0 || Area(a, b.Prev, b) != 0) ||
         Equals(a, b) && Area(a.Prev, a, a.Next) > 0 && Area(b.Prev, b, b.Next) > 0);

    static int Sign(double v) => v > 0 ? 1 : v < 0 ? -1 : 0;

    static bool OnSegment(Node p, Node q, Node r) =>
        q.X <= Math.Max(p.X, r.X) && q.X >= Math.Min(p.X, r.X) && q.Y <= Math.Max(p.Y, r.Y) && q.Y >= Math.Min(p.Y, r.Y);

    static bool Intersects(Node p1, Node q1, Node p2, Node q2)
    {
        int o1 = Sign(Area(p1, q1, p2)), o2 = Sign(Area(p1, q1, q2)), o3 = Sign(Area(p2, q2, p1)), o4 = Sign(Area(p2, q2, q1));
        if (o1 != o2 && o3 != o4) return true;
        if (o1 == 0 && OnSegment(p1, p2, q1)) return true;
        if (o2 == 0 && OnSegment(p1, q2, q1)) return true;
        if (o3 == 0 && OnSegment(p2, p1, q2)) return true;
        if (o4 == 0 && OnSegment(p2, q1, q2)) return true;
        return false;
    }

    static bool IntersectsPolygon(Node a, Node b)
    {
        var p = a;
        do
        {
            if (p.I != a.I && p.Next.I != a.I && p.I != b.I && p.Next.I != b.I && Intersects(p, p.Next, a, b)) return true;
            p = p.Next;
        } while (p != a);
        return false;
    }

    static bool LocallyInside(Node a, Node b) =>
        Area(a.Prev, a, a.Next) < 0
            ? Area(a, b, a.Next) >= 0 && Area(a, a.Prev, b) >= 0
            : Area(a, b, a.Prev) < 0 || Area(a, a.Next, b) < 0;

    static bool MiddleInside(Node a, Node b)
    {
        var p = a;
        bool inside = false;
        double px = (a.X + b.X) / 2, py = (a.Y + b.Y) / 2;
        do
        {
            if ((p.Y > py) != (p.Next.Y > py) && p.Next.Y != p.Y && px < (p.Next.X - p.X) * (py - p.Y) / (p.Next.Y - p.Y) + p.X) inside = !inside;
            p = p.Next;
        } while (p != a);
        return inside;
    }

    /// <summary>Join a and b with a two-way edge, making two rings; returns the new copy of b.</summary>
    static Node SplitPolygon(Node a, Node b)
    {
        var a2 = new Node(a.I, a.X, a.Y);
        var b2 = new Node(b.I, b.X, b.Y);
        var an = a.Next;
        var bp = b.Prev;
        a.Next = b;
        b.Prev = a;
        a2.Next = an;
        an.Prev = a2;
        b2.Next = a2;
        a2.Prev = b2;
        bp.Next = b2;
        b2.Prev = bp;
        return b2;
    }
}
