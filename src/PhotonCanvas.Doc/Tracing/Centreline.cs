using System.Collections.Immutable;
using PhotonCanvas.Core;
using PhotonCanvas.Doc.Geometry;

namespace PhotonCanvas.Doc.Tracing;

/// <summary>
/// Centre-line tracing: one line down the middle of each stroke (handwriting, line art, text),
/// instead of an outline round both sides. The strokes are thinned to one pixel (Zhang–Suen),
/// then followed from end to end and smoothed.
/// </summary>
public static class Centreline
{
    /// <summary>
    /// Lines through the middle of the <paramref name="ink"/> pixels (row-major, <c>true</c> = ink),
    /// in pixel coordinates (pixel centres at +0.5). Lines shorter than <paramref name="minLengthPx"/> are dropped.
    /// </summary>
    public static List<List<Vec2>> Trace(bool[] ink, int w, int h, double minLengthPx = 3, double smoothPx = 0.7)
    {
        var skel = Thin(ink, w, h);
        bool On(int x, int y) => x >= 0 && y >= 0 && x < w && y < h && skel[y * w + x];
        int Neighbours(int x, int y)
        {
            int n = 0;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                    if ((dx != 0 || dy != 0) && On(x + dx, y + dy)) n++;
            return n;
        }
        var used = new HashSet<(int, int, int, int)>(); // walked steps, both ways
        var lines = new List<List<Vec2>>();
        bool Walked(int x0, int y0, int x1, int y1) => used.Contains((x0, y0, x1, y1));
        void Mark(int x0, int y0, int x1, int y1) { used.Add((x0, y0, x1, y1)); used.Add((x1, y1, x0, y0)); }

        List<Vec2> Follow(int sx, int sy, int nx, int ny)
        {
            var pts = new List<Vec2> { new(sx + 0.5, sy + 0.5) };
            int px = sx, py = sy, cx = nx, cy = ny;
            Mark(px, py, cx, cy);
            while (true)
            {
                pts.Add(new(cx + 0.5, cy + 0.5));
                if (Neighbours(cx, cy) != 2) break; // an end or a junction
                (int X, int Y)? next = null;
                // Prefer straight-on neighbours, then diagonals.
                foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (-1, -1), (1, -1), (-1, 1) })
                {
                    int qx = cx + dx, qy = cy + dy;
                    if (!On(qx, qy) || (qx == px && qy == py) || Walked(cx, cy, qx, qy)) continue;
                    next = (qx, qy);
                    break;
                }
                if (next is not { } nn) break;
                Mark(cx, cy, nn.X, nn.Y);
                (px, py, cx, cy) = (cx, cy, nn.X, nn.Y);
                if (cx == sx && cy == sy) { pts.Add(new(sx + 0.5, sy + 0.5)); break; } // round a loop
            }
            return pts;
        }

        // Lines start at ends and junctions; what's left after that is closed loops.
        for (int pass = 0; pass < 2; pass++)
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    if (!skel[y * w + x]) continue;
                    int n = Neighbours(x, y);
                    if (pass == 0 && n == 2) continue;
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            if ((dx == 0 && dy == 0) || !On(x + dx, y + dy) || Walked(x, y, x + dx, y + dy)) continue;
                            var line = Follow(x, y, x + dx, y + dy);
                            if (Length(line) >= minLengthPx) lines.Add(PathTools.Simplify(line, smoothPx));
                        }
                    if (pass == 0 && n == 0) continue; // a lone dot
                }
        return lines;
    }

    static double Length(List<Vec2> p)
    {
        double l = 0;
        for (int i = 1; i < p.Count; i++) l += p[i - 1].Dist(p[i]);
        return l;
    }

    /// <summary>The lines as open paths in mm, through <paramref name="pixelToMm"/>.</summary>
    public static PathGeometry ToGeometry(List<List<Vec2>> lines, Affine pixelToMm) =>
        new(lines.Where(l => l.Count >= 2).Select(l =>
        {
            var pts = l.Select(pixelToMm.Apply).ToList();
            bool closed = pts.Count > 3 && pts[0].Dist(pts[^1]) < 1e-6;
            if (closed) pts.RemoveAt(pts.Count - 1);
            return new Subpath(pts[0], [.. pts.Skip(1).Select(Seg.Line)], closed);
        }));

    /// <summary>Zhang–Suen thinning: the strokes worn down to one pixel wide, keeping their shape and connections.</summary>
    public static bool[] Thin(bool[] ink, int w, int h)
    {
        var img = (bool[])ink.Clone();
        bool P(int x, int y) => x >= 0 && y >= 0 && x < w && y < h && img[y * w + x];
        var remove = new List<int>();
        bool changed = true;
        while (changed)
        {
            changed = false;
            for (int step = 0; step < 2; step++)
            {
                remove.Clear();
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        if (!img[y * w + x]) continue;
                        bool p2 = P(x, y - 1), p3 = P(x + 1, y - 1), p4 = P(x + 1, y), p5 = P(x + 1, y + 1);
                        bool p6 = P(x, y + 1), p7 = P(x - 1, y + 1), p8 = P(x - 1, y), p9 = P(x - 1, y - 1);
                        bool[] ring = [p2, p3, p4, p5, p6, p7, p8, p9];
                        int b = ring.Count(v => v);
                        if (b < 2 || b > 6) continue;
                        int a = 0;
                        for (int i = 0; i < 8; i++) if (!ring[i] && ring[(i + 1) % 8]) a++;
                        if (a != 1) continue;
                        if (step == 0 ? (p2 && p4 && p6) || (p4 && p6 && p8) : (p2 && p4 && p8) || (p2 && p6 && p8)) continue;
                        remove.Add(y * w + x);
                    }
                foreach (int i in remove) img[i] = false;
                changed |= remove.Count > 0;
            }
        }
        return img;
    }
}
