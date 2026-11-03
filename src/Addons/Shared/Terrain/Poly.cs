using Clipper2Lib;

namespace PhotonCanvas.Addon.Terrain;

/// <summary>
/// Polygon helpers for the textures (mm, y up), kept here so the shared texture code needs nothing
/// from the add-on it's compiled into. Areas are filled non-zero unless said otherwise.
/// </summary>
internal static class Poly
{
    public const int Precision = 4;

    public static PathD Rect(double x0, double y0, double x1, double y1) =>
        [new(Math.Min(x0, x1), Math.Min(y0, y1)), new(Math.Max(x0, x1), Math.Min(y0, y1)), new(Math.Max(x0, x1), Math.Max(y0, y1)), new(Math.Min(x0, x1), Math.Max(y0, y1))];

    public static PathD Chamfered(double x0, double y0, double x1, double y1, double c)
    {
        c = Math.Clamp(c, 0, Math.Min(x1 - x0, y1 - y0) / 2 - 1e-4);
        if (c <= 1e-4) return Rect(x0, y0, x1, y1);
        return [new(x0 + c, y0), new(x1 - c, y0), new(x1, y0 + c), new(x1, y1 - c), new(x1 - c, y1), new(x0 + c, y1), new(x0, y1 - c), new(x0, y0 + c)];
    }

    /// <summary>A rectangle with only some corners cut (bottom-left, bottom-right, top-right, top-left).</summary>
    public static PathD Cut(double x0, double y0, double x1, double y1, double c, bool bl, bool br, bool tr, bool tl)
    {
        c = Math.Clamp(c, 0, Math.Min(x1 - x0, y1 - y0) / 2 - 1e-4);
        var p = new PathD();
        if (bl && c > 0) { p.Add(new(x0, y0 + c)); p.Add(new(x0 + c, y0)); } else p.Add(new(x0, y0));
        if (br && c > 0) { p.Add(new(x1 - c, y0)); p.Add(new(x1, y0 + c)); } else p.Add(new(x1, y0));
        if (tr && c > 0) { p.Add(new(x1, y1 - c)); p.Add(new(x1 - c, y1)); } else p.Add(new(x1, y1));
        if (tl && c > 0) { p.Add(new(x0 + c, y1)); p.Add(new(x0, y1 - c)); } else p.Add(new(x0, y1));
        return p;
    }

    public static PathD Rounded(double x0, double y0, double x1, double y1, double r)
    {
        r = Math.Clamp(r, 0, Math.Min(x1 - x0, y1 - y0) / 2 - 1e-4);
        if (r < 0.02) return Rect(x0, y0, x1, y1);
        var p = new PathD();
        void Arc(double cx, double cy, double a0)
        {
            for (int i = 0; i <= 6; i++)
            {
                double a = a0 + Math.PI / 2 * i / 6;
                p.Add(new PointD(cx + r * Math.Cos(a), cy + r * Math.Sin(a)));
            }
        }
        Arc(x1 - r, y0 + r, -Math.PI / 2);
        Arc(x1 - r, y1 - r, 0);
        Arc(x0 + r, y1 - r, Math.PI / 2);
        Arc(x0 + r, y0 + r, Math.PI);
        return p;
    }

    public static PathD Circle(double cx, double cy, double r)
    {
        int n = Math.Clamp((int)Math.Ceiling(r * 14), 10, 64);
        var p = new PathD(n);
        for (int i = 0; i < n; i++) p.Add(new PointD(cx + r * Math.Cos(2 * Math.PI * i / n), cy + r * Math.Sin(2 * Math.PI * i / n)));
        return p;
    }

    public static PathD Ellipse(double cx, double cy, double rx, double ry, double turn = 0)
    {
        int n = Math.Clamp((int)Math.Ceiling(Math.Max(rx, ry) * 14), 10, 64);
        double c = Math.Cos(turn), s = Math.Sin(turn);
        var p = new PathD(n);
        for (int i = 0; i < n; i++)
        {
            double a = 2 * Math.PI * i / n, x = rx * Math.Cos(a), y = ry * Math.Sin(a);
            p.Add(new PointD(cx + x * c - y * s, cy + x * s + y * c));
        }
        return p;
    }

    /// <summary>A lumpy, roughly elliptical blob: <paramref name="lumpy"/> 0 = smooth, 1 = very ragged.</summary>
    public static PathD Blob(double cx, double cy, double rx, double ry, double lumpy, Random rnd, double turn = 0)
    {
        int n = Math.Clamp((int)Math.Ceiling(Math.Max(rx, ry) * 10), 7, 40);
        double c = Math.Cos(turn), s = Math.Sin(turn), ph1 = rnd.NextDouble() * 6.28, ph2 = rnd.NextDouble() * 6.28;
        var p = new PathD(n);
        for (int i = 0; i < n; i++)
        {
            double a = 2 * Math.PI * i / n;
            double k = 1 + lumpy * (0.22 * Math.Sin(2 * a + ph1) + 0.14 * Math.Sin(3 * a + ph2) + 0.3 * (rnd.NextDouble() - 0.5));
            double x = rx * k * Math.Cos(a), y = ry * k * Math.Sin(a);
            p.Add(new PointD(cx + x * c - y * s, cy + x * s + y * c));
        }
        return p;
    }

    /// <summary>
    /// An angular flake, as knocked off stone: a few straight sides round (cx, cy), about
    /// <paramref name="rx"/> × <paramref name="ry"/>, turned by <paramref name="turn"/>.
    /// </summary>
    public static PathD Shard(double cx, double cy, double rx, double ry, Random rnd, double turn = 0)
    {
        int n = 4 + rnd.Next(3);
        double c = Math.Cos(turn), s = Math.Sin(turn), a0 = rnd.NextDouble() * Math.PI * 2;
        var p = new PathD(n);
        for (int i = 0; i < n; i++)
        {
            double a = a0 + 2 * Math.PI * (i + (rnd.NextDouble() - 0.5) * 0.6) / n, k = 0.6 + 0.5 * rnd.NextDouble();
            double x = rx * k * Math.Cos(a), y = ry * k * Math.Sin(a);
            p.Add(new PointD(cx + x * c - y * s, cy + x * s + y * c));
        }
        if (Clipper.Area(p) < 0) p.Reverse();
        return p;
    }

    /// <summary>A straight strip of the given width from p to q.</summary>
    public static PathD Strip(PointD p, PointD q, double width)
    {
        double dx = q.x - p.x, dy = q.y - p.y, len = Math.Max(1e-9, Math.Sqrt(dx * dx + dy * dy));
        double nx = -dy / len * width / 2, ny = dx / len * width / 2;
        return [new(p.x + nx, p.y + ny), new(p.x - nx, p.y - ny), new(q.x - nx, q.y - ny), new(q.x + nx, q.y + ny)];
    }

    /// <summary>A strip along a line that narrows to a point at its end (<paramref name="w0"/> wide at the start, <paramref name="w1"/> at the end).</summary>
    public static PathD Taper(IReadOnlyList<PointD> line, double w0, double w1)
    {
        if (line.Count < 2) return [];
        var left = new List<PointD>();
        var right = new List<PointD>();
        double total = 0;
        for (int i = 1; i < line.Count; i++) total += Dist(line[i - 1], line[i]);
        double run = 0;
        for (int i = 0; i < line.Count; i++)
        {
            if (i > 0) run += Dist(line[i - 1], line[i]);
            var a = line[Math.Max(0, i - 1)];
            var b = line[Math.Min(line.Count - 1, i + 1)];
            double dx = b.x - a.x, dy = b.y - a.y, l = Math.Max(1e-9, Math.Sqrt(dx * dx + dy * dy));
            double w = (w0 + (w1 - w0) * (total > 0 ? run / total : 0)) / 2;
            left.Add(new PointD(line[i].x - dy / l * w, line[i].y + dx / l * w));
            right.Add(new PointD(line[i].x + dy / l * w, line[i].y - dx / l * w));
        }
        right.Reverse();
        PathD outp = [.. left, .. right];
        if (Clipper.Area(outp) < 0) outp.Reverse();
        return outp;
    }

    public static double Dist(PointD a, PointD b) => Math.Sqrt((a.x - b.x) * (a.x - b.x) + (a.y - b.y) * (a.y - b.y));

    public static PathsD Union(PathsD a) => a.Count == 0 ? [] : Clipper.BooleanOp(ClipType.Union, a, null, FillRule.NonZero, Precision);
    public static PathsD UnionEvenOdd(PathsD a) => a.Count == 0 ? [] : Clipper.BooleanOp(ClipType.Union, a, null, FillRule.EvenOdd, Precision);
    public static PathsD Subtract(PathsD a, PathsD b) => a.Count == 0 ? [] : b.Count == 0 ? a : Clipper.Difference(a, b, FillRule.NonZero, Precision);
    public static PathsD Intersect(PathsD a, PathsD b) => a.Count == 0 || b.Count == 0 ? [] : Clipper.Intersect(a, b, FillRule.NonZero, Precision);
    public static PathsD Offset(PathsD a, double by, bool round = false) =>
        a.Count == 0 || by == 0 ? a : Clipper.InflatePaths(Union(a), by, round ? JoinType.Round : JoinType.Miter, EndType.Polygon, 4, Precision);

    public static double Area(PathsD a) => a.Count == 0 ? 0 : Math.Abs(Clipper.Area(a));

    public static RectD Bounds(PathD p) => Clipper.GetBounds(p);
    public static RectD Bounds(PathsD a) => a.Count == 0 ? new RectD(0, 0, 0, 0) : Clipper.GetBounds(a);

    public static PathD Loop(PathD p) => p.Count == 0 ? p : [.. p, p[0]];

    /// <summary>The parts of open lines inside the areas.</summary>
    public static List<PathD> ClipLines(IEnumerable<PathD> lines, PathsD region)
    {
        var subject = new PathsD(lines.Where(l => l.Count >= 2));
        if (subject.Count == 0 || region.Count == 0) return [];
        var c = new ClipperD(Precision);
        c.AddOpenSubject(subject);
        c.AddClip(region);
        var closed = new PathsD();
        var open = new PathsD();
        c.Execute(ClipType.Intersection, FillRule.NonZero, closed, open);
        return [.. open.Where(p => p.Count >= 2)];
    }

    public static PathD Map(PathD p, Func<PointD, PointD> f) => [.. p.Select(f)];
    public static PathsD Map(PathsD a, Func<PointD, PointD> f) => [.. a.Select(p => Map(p, f))];
    public static List<PathD> Map(IEnumerable<PathD> a, Func<PointD, PointD> f) => [.. a.Select(p => Map(p, f))];

    /// <summary>
    /// The outline with its edges nibbled: points every <paramref name="step"/> mm along it, each moved
    /// inwards by between 0 and <paramref name="depth"/> mm as the noise says, so a straight edge becomes
    /// a worn one and the gap beside it varies. The shape only ever shrinks, so it never runs into its
    /// neighbours.
    /// </summary>
    public static PathD Roughen(PathD p, Noise noise, double depth, double wave, double step, double shift = 0)
    {
        if (p.Count < 3 || depth <= 0.005) return p;
        bool ccw = Clipper.Area(p) > 0;
        step = Math.Max(0.12, step);
        var pts = new List<PointD>();
        for (int i = 0; i < p.Count; i++)
        {
            var a = p[i];
            var b = p[(i + 1) % p.Count];
            double l = Dist(a, b);
            int n = Math.Max(1, (int)Math.Ceiling(l / step));
            for (int k = 0; k < n; k++) pts.Add(new PointD(a.x + (b.x - a.x) * k / n, a.y + (b.y - a.y) * k / n));
        }
        var outp = new PathD(pts.Count);
        for (int i = 0; i < pts.Count; i++)
        {
            var prev = pts[(i - 1 + pts.Count) % pts.Count];
            var next = pts[(i + 1) % pts.Count];
            double dx = next.x - prev.x, dy = next.y - prev.y, l = Math.Max(1e-9, Math.Sqrt(dx * dx + dy * dy));
            // Inwards: left of the direction of travel on an anticlockwise outline.
            double nx = -dy / l, ny = dx / l;
            if (!ccw) { nx = -nx; ny = -ny; }
            var q = pts[i];
            // Mostly a near-straight edge with the odd dip: the noise is squared so deep nibbles are rarer than shallow ones.
            double v = Math.Clamp(0.45 + 0.8 * noise.Fbm(q.x + shift, q.y - shift * 0.7, wave, 2), 0, 1);
            double d = depth * (0.15 + 0.85 * v * v);
            outp.Add(new PointD(q.x + nx * d, q.y + ny * d));
        }
        return outp;
    }

    /// <summary>
    /// The areas where <paramref name="value"/> is above <paramref name="level"/>, traced over a grid
    /// <paramref name="cell"/> mm square across the box (marching squares). The edge of the box counts as
    /// below, so every outline closes. Holes come out as holes (even-odd).
    /// </summary>
    public static PathsD Contours(double x0, double y0, double x1, double y1, double cell, Func<double, double, double> value, double level)
    {
        cell = Math.Max(0.08, cell);
        int nx = Math.Max(2, (int)Math.Ceiling((x1 - x0) / cell)) + 1, ny = Math.Max(2, (int)Math.Ceiling((y1 - y0) / cell)) + 1;
        if ((long)nx * ny > 2_000_000) return [];
        // One cell of "below" round the outside.
        var v = new double[nx + 2, ny + 2];
        for (int i = 0; i < nx + 2; i++)
            for (int j = 0; j < ny + 2; j++)
                v[i, j] = i == 0 || j == 0 || i == nx + 1 || j == ny + 1 ? -1 : value(x0 + (i - 1) * cell, y0 + (j - 1) * cell) - level;
        PointD X(int i, int j) => new(x0 + (i - 1) * cell, y0 + (j - 1) * cell);
        // A point on an edge, keyed: horizontal edge (i,j)-(i+1,j) = 2·(i·M+j), vertical (i,j)-(i,j+1) = 2·(i·M+j)+1.
        long M = ny + 3;
        long KeyH(int i, int j) => 2 * (i * M + j);
        long KeyV(int i, int j) => 2 * (i * M + j) + 1;
        var at = new Dictionary<long, PointD>();
        PointD Lerp(int ia, int ja, int ib, int jb)
        {
            double va = v[ia, ja], vb = v[ib, jb], t = Math.Clamp(va / (va - vb), 0, 1);
            var a = X(ia, ja);
            var b = X(ib, jb);
            return new PointD(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t);
        }
        long E(int i, int j, int edge)
        {
            // 0 bottom, 1 right, 2 top, 3 left of cell (i, j).
            long k = edge switch { 0 => KeyH(i, j), 1 => KeyV(i + 1, j), 2 => KeyH(i, j + 1), _ => KeyV(i, j) };
            if (!at.ContainsKey(k))
                at[k] = edge switch
                {
                    0 => Lerp(i, j, i + 1, j),
                    1 => Lerp(i + 1, j, i + 1, j + 1),
                    2 => Lerp(i, j + 1, i + 1, j + 1),
                    _ => Lerp(i, j, i, j + 1),
                };
            return k;
        }
        var link = new Dictionary<long, List<long>>();
        void Seg(long a, long b)
        {
            if (!link.TryGetValue(a, out var la)) link[a] = la = [];
            if (!link.TryGetValue(b, out var lb)) link[b] = lb = [];
            la.Add(b);
            lb.Add(a);
        }
        for (int i = 0; i < nx + 1; i++)
            for (int j = 0; j < ny + 1; j++)
            {
                int c = (v[i, j] > 0 ? 1 : 0) | (v[i + 1, j] > 0 ? 2 : 0) | (v[i + 1, j + 1] > 0 ? 4 : 0) | (v[i, j + 1] > 0 ? 8 : 0);
                switch (c)
                {
                    case 0: case 15: break;
                    case 1: case 14: Seg(E(i, j, 3), E(i, j, 0)); break;
                    case 2: case 13: Seg(E(i, j, 0), E(i, j, 1)); break;
                    case 3: case 12: Seg(E(i, j, 3), E(i, j, 1)); break;
                    case 4: case 11: Seg(E(i, j, 1), E(i, j, 2)); break;
                    case 6: case 9: Seg(E(i, j, 0), E(i, j, 2)); break;
                    case 7: case 8: Seg(E(i, j, 3), E(i, j, 2)); break;
                    case 5:
                    case 10:
                        {
                            double mid = (v[i, j] + v[i + 1, j] + v[i + 1, j + 1] + v[i, j + 1]) / 4;
                            bool joined = (mid > 0) == (c == 5);
                            if (joined) { Seg(E(i, j, 3), E(i, j, 2)); Seg(E(i, j, 0), E(i, j, 1)); }
                            else { Seg(E(i, j, 3), E(i, j, 0)); Seg(E(i, j, 1), E(i, j, 2)); }
                            break;
                        }
                }
            }
        var loops = new PathsD();
        var used = new HashSet<long>();
        foreach (var start in link.Keys)
        {
            if (used.Contains(start)) continue;
            var loop = new PathD();
            long prev = -1, cur = start;
            while (cur >= 0 && used.Add(cur))
            {
                loop.Add(at[cur]);
                long nextKey = -1;
                foreach (var n in link[cur]) if (n != prev && !used.Contains(n)) { nextKey = n; break; }
                prev = cur;
                cur = nextKey;
            }
            if (loop.Count >= 3) loops.Add(loop);
        }
        return UnionEvenOdd(loops);
    }
}
