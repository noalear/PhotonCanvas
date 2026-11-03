using System.Runtime.CompilerServices;
using Clipper2Lib;

namespace PhotonCanvas.Tests;

/// <summary>
/// "Is this point inside the outline?" asked hundreds of thousands of times of the same outline: the
/// put-together tests sample every place two pieces' boxes meet, about every 0.1 mm. Asking Clipper
/// each time walks every edge of the outline, and a worn, textured piece has thousands, which made a
/// few tests take over a minute each.
///
/// Same answer, quicker: an outline asked often gets a grid laid over it. A square no edge comes near
/// (none within 0.02 mm of it) is all inside or all outside, so it's asked once, at its middle,
/// and remembered. Points in squares an edge crosses, and outlines asked only a few times, are asked
/// of Clipper as before. The answer is the same as <see cref="Slow"/> for every point.
/// </summary>
static class OutlineGrid
{
    /// <summary>Inside an odd number of the outline's paths (so inside a piece and not in one of its holes), and not on an edge.</summary>
    public static bool In(PathsD area, double u, double v)
    {
        var g = Grids.GetValue(area, a => new Grid(a));
        return g.In(u, v);
    }

    /// <summary>The plain answer, asking Clipper of every path.</summary>
    public static bool Slow(PathsD area, double u, double v)
    {
        int inside = 0;
        foreach (var p in area)
            if (Clipper.PointInPolygon(new PointD(u, v), p) == PointInPolygonResult.IsInside) inside++;
        return inside % 2 == 1;
    }

    static readonly ConditionalWeakTable<PathsD, Grid> Grids = new();

    sealed class Grid(PathsD area)
    {
        const int AskedBeforeGrid = 200, MaxCells = 1024;
        const sbyte Unknown = 0, Inside = 1, Outside = 2, Edge = 3;

        int asked;
        volatile bool built;
        readonly object gate = new();
        double x0, y0, x1, y1, h;
        int nx, ny;
        sbyte[] cells = [];

        public bool In(double u, double v)
        {
            if (!built)
            {
                if (Interlocked.Increment(ref asked) < AskedBeforeGrid) return Slow(area, u, v);
                lock (gate) if (!built) { Build(); built = true; }
            }
            // Beyond every point of the outline: outside every path.
            if (!(u >= x0 && u <= x1 && v >= y0 && v <= y1)) return false;
            int i = Math.Clamp((int)Math.Floor((u - x0) / h), 0, nx - 1), j = Math.Clamp((int)Math.Floor((v - y0) / h), 0, ny - 1);
            ref sbyte c = ref cells[j * nx + i];
            if (c == Edge) return Slow(area, u, v);
            // Racing threads work out the same answer, so either may write it.
            if (c == Unknown) c = Slow(area, x0 + (i + 0.5) * h, y0 + (j + 0.5) * h) ? Inside : Outside;
            return c == Inside;
        }

        void Build()
        {
            x0 = y0 = double.MaxValue; x1 = y1 = double.MinValue;
            foreach (var p in area)
                foreach (var q in p) { x0 = Math.Min(x0, q.x); y0 = Math.Min(y0, q.y); x1 = Math.Max(x1, q.x); y1 = Math.Max(y1, q.y); }
            if (x0 > x1) { x0 = y0 = 1; x1 = y1 = 0; nx = ny = 1; h = 1; cells = [Outside]; return; }
            h = Math.Max(Math.Max(x1 - x0, y1 - y0) / MaxCells, 1e-6);
            nx = Math.Max(1, (int)Math.Ceiling((x1 - x0) / h) + 1);
            ny = Math.Max(1, (int)Math.Ceiling((y1 - y0) / h) + 1);
            cells = new sbyte[nx * ny];
            foreach (var p in area)
                for (int k = 0; k < p.Count; k++)
                {
                    var a = p[k];
                    var b = p[(k + 1) % p.Count];
                    // In pieces no longer than a square, so a long slanting edge marks only the squares along it.
                    int n = Math.Max(1, (int)Math.Ceiling(Math.Max(Math.Abs(b.x - a.x), Math.Abs(b.y - a.y)) / h));
                    for (int s = 0; s < n; s++)
                    {
                        double ax = a.x + (b.x - a.x) * s / n, ay = a.y + (b.y - a.y) * s / n;
                        double bx = a.x + (b.x - a.x) * (s + 1) / n, by = a.y + (b.y - a.y) * (s + 1) / n;
                        Mark(Math.Min(ax, bx), Math.Min(ay, by), Math.Max(ax, bx), Math.Max(ay, by));
                    }
                }
        }

        /// <summary>
        /// Every square this box touches, or comes within 0.02 mm of, may have an edge in it. (Clipper
        /// rounds the point and the outline to 0.01 mm before it looks, which can move either by up to
        /// 0.007 mm: a point that close to an edge may come out on the other side.)
        /// </summary>
        void Mark(double ax, double ay, double bx, double by)
        {
            const double e = 0.02;
            int i0 = Math.Max(0, (int)Math.Floor((ax - e - x0) / h)), i1 = Math.Min(nx - 1, (int)Math.Floor((bx + e - x0) / h));
            int j0 = Math.Max(0, (int)Math.Floor((ay - e - y0) / h)), j1 = Math.Min(ny - 1, (int)Math.Floor((by + e - y0) / h));
            for (int j = j0; j <= j1; j++)
                for (int i = i0; i <= i1; i++)
                    cells[j * nx + i] = Edge;
        }
    }
}
