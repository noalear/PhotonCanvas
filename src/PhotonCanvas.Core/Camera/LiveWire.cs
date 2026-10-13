namespace PhotonCanvas.Core.Camera;

/// <summary>
/// "Magnetic pen" (intelligent scissors): the cheapest path between two points
/// over a straightened camera picture, where strong thin edges are cheap, so a
/// line drawn between clicks hugs the edges of real objects.
/// The picture covers <c>area</c> at <c>mmPerPx</c>; pixel (x, y) is centred on
/// <c>area.Min + (x + ½, y + ½) · mmPerPx</c>.
/// </summary>
public sealed class LiveWire
{
    readonly float[] _cost;   // cost of stepping onto each pixel
    readonly float[] _mag;    // edge strength 0..1, for snapping
    readonly int _w, _h;

    public Rect2 Area { get; }
    public double MmPerPx { get; }

    LiveWire(float[] cost, float[] mag, int w, int h, Rect2 area, double mmPerPx)
    {
        _cost = cost; _mag = mag; _w = w; _h = h; Area = area; MmPerPx = mmPerPx;
    }

    public static LiveWire Build(GrayImage img, Rect2 area, double mmPerPx)
    {
        int w = img.Width, h = img.Height;
        var edges = EdgeMap.Build(img);
        var mag = new float[w * h];
        // Gradient strength again (EdgeMap keeps only the thinned edges).
        var px = img.Pixels;
        for (int y = 1; y < h - 1; y++)
            for (int x = 1; x < w - 1; x++)
            {
                int i = y * w + x;
                float gx = px[i - w + 1] + 2 * px[i + 1] + px[i + w + 1] - px[i - w - 1] - 2 * px[i - 1] - px[i + w - 1];
                float gy = px[i + w - 1] + 2 * px[i + w] + px[i + w + 1] - px[i - w - 1] - 2 * px[i - w] - px[i - w + 1];
                mag[i] = MathF.Sqrt(gx * gx + gy * gy);
            }
        var sorted = mag.Where(m => m > 0).OrderBy(m => m).ToArray();
        float top = sorted.Length == 0 ? 1 : Math.Max(1, sorted[(int)(sorted.Length * 0.99)]);
        var cost = new float[w * h];
        for (int i = 0; i < mag.Length; i++)
        {
            float g = Math.Min(1, mag[i] / top);
            mag[i] = g;
            // Cheap on strong, thin edges; never free, so paths don't wander.
            cost[i] = 0.12f + 0.45f * (1 - g) + (edges.IsEdge[i] ? 0 : 0.43f);
        }
        return new LiveWire(cost, mag, w, h, area, mmPerPx);
    }

    int ToIndex(Vec2 mm)
    {
        int x = (int)Math.Floor((mm.X - Area.MinX) / MmPerPx), y = (int)Math.Floor((mm.Y - Area.MinY) / MmPerPx);
        return x < 0 || y < 0 || x >= _w || y >= _h ? -1 : y * _w + x;
    }

    Vec2 ToMm(int i) => new(Area.MinX + (i % _w + 0.5) * MmPerPx, Area.MinY + (i / _w + 0.5) * MmPerPx);

    /// <summary>The strongest edge within <paramref name="radiusMm"/> of a point, or the point itself if there is none.</summary>
    public Vec2 Snap(Vec2 mm, double radiusMm)
    {
        int c = ToIndex(mm);
        if (c < 0) return mm;
        int r = Math.Max(1, (int)Math.Round(radiusMm / MmPerPx)), cx = c % _w, cy = c / _w;
        int best = -1;
        float bestScore = 0.25f; // weaker than this isn't an edge worth snapping to
        for (int y = Math.Max(0, cy - r); y <= Math.Min(_h - 1, cy + r); y++)
            for (int x = Math.Max(0, cx - r); x <= Math.Min(_w - 1, cx + r); x++)
            {
                int dx = x - cx, dy = y - cy;
                if (dx * dx + dy * dy > r * r) continue;
                // Prefer strong edges, then near ones.
                float score = _mag[y * _w + x] - 0.15f * MathF.Sqrt(dx * dx + dy * dy) / r;
                if (score > bestScore) { bestScore = score; best = y * _w + x; }
            }
        return best < 0 ? mm : ToMm(best);
    }

    /// <summary>
    /// Cheapest paths from <paramref name="seedMm"/> to everywhere within
    /// <paramref name="radiusMm"/> (a square around it). Takes a moment; build it once per click.
    /// </summary>
    public Tree Grow(Vec2 seedMm, double radiusMm, CancellationToken cancellationToken = default)
    {
        int seed = ToIndex(seedMm);
        var prev = new int[_w * _h];
        Array.Fill(prev, -2); // −2 = not reached
        if (seed < 0) return new Tree(this, prev, seed);
        int r = (int)Math.Ceiling(radiusMm / MmPerPx), sx = seed % _w, sy = seed / _w;
        int x0 = Math.Max(0, sx - r), x1 = Math.Min(_w - 1, sx + r), y0 = Math.Max(0, sy - r), y1 = Math.Min(_h - 1, sy + r);
        var dist = new float[_w * _h];
        Array.Fill(dist, float.PositiveInfinity);
        var done = new bool[_w * _h];
        var queue = new PriorityQueue<int, float>();
        dist[seed] = 0;
        prev[seed] = -1;
        queue.Enqueue(seed, 0);
        const float Diag = 1.41421356f;
        int steps = 0;
        while (queue.TryDequeue(out int i, out float d))
        {
            if (done[i]) continue;
            done[i] = true;
            if ((++steps & 0xFFFF) == 0) cancellationToken.ThrowIfCancellationRequested();
            int x = i % _w, y = i / _w;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    int nx = x + dx, ny = y + dy;
                    if (nx < x0 || nx > x1 || ny < y0 || ny > y1) continue;
                    int j = ny * _w + nx;
                    if (done[j]) continue;
                    float nd = d + _cost[j] * (dx != 0 && dy != 0 ? Diag : 1);
                    if (nd < dist[j])
                    {
                        dist[j] = nd;
                        prev[j] = i;
                        queue.Enqueue(j, nd);
                    }
                }
        }
        return new Tree(this, prev, seed);
    }

    /// <summary>The paths found by <see cref="Grow"/>.</summary>
    public sealed class Tree
    {
        readonly LiveWire _wire;
        readonly int[] _prev;
        readonly int _seed;

        internal Tree(LiveWire wire, int[] prev, int seed) { _wire = wire; _prev = prev; _seed = seed; }

        /// <summary>Points (mm, pixel centres) from the seed to <paramref name="targetMm"/>; null when it's out of reach.</summary>
        public List<Vec2>? PathTo(Vec2 targetMm)
        {
            int t = _wire.ToIndex(targetMm);
            if (t < 0 || _seed < 0 || _prev[t] == -2) return null;
            var idx = new List<int>();
            for (int i = t; i >= 0; i = _prev[i]) idx.Add(i);
            idx.Reverse();
            return idx.Select(_wire.ToMm).ToList();
        }
    }

    /// <summary>Douglas–Peucker: drop points within <paramref name="tolerance"/> of the line through their neighbours.</summary>
    public static List<Vec2> Simplify(IReadOnlyList<Vec2> pts, double tolerance)
    {
        if (pts.Count < 3) return [.. pts];
        var keep = new bool[pts.Count];
        keep[0] = keep[^1] = true;
        var stack = new Stack<(int, int)>();
        stack.Push((0, pts.Count - 1));
        while (stack.Count > 0)
        {
            var (a, b) = stack.Pop();
            double best = 0;
            int at = -1;
            var ab = pts[b] - pts[a];
            double len = ab.Length;
            for (int i = a + 1; i < b; i++)
            {
                double d = len < 1e-12 ? pts[i].Dist(pts[a]) : Math.Abs(ab.Cross(pts[i] - pts[a])) / len;
                if (d > best) { best = d; at = i; }
            }
            if (at >= 0 && best > tolerance)
            {
                keep[at] = true;
                stack.Push((a, at));
                stack.Push((at, b));
            }
        }
        return pts.Where((_, i) => keep[i]).ToList();
    }
}
