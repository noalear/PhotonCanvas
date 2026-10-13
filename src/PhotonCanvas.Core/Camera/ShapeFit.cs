namespace PhotonCanvas.Core.Camera;

/// <summary>How far <see cref="ShapeFit"/> may move, turn and resize the outline.</summary>
public sealed record FitOptions
{
    /// <summary>Largest turn either way, degrees (180 = any angle, 0 = none).</summary>
    public double MaxRotationDeg { get; init; } = 30;
    /// <summary>Largest size change either way, as a fraction (0.15 = ±15 %, 0 = keep the size).</summary>
    public double MaxScaleChange { get; init; } = 0.15;
    /// <summary>How far from where it is now the outline may move, mm.</summary>
    public double SearchMm { get; init; } = 20;
}

/// <summary>
/// Where the outline fits best: <c>p' = Center + Offset + Scale · R(AngleDeg) · (p − Center)</c>
/// (R turns clockwise on screen for positive angles, as +Y is down).
/// </summary>
/// <param name="OnEdge">Fraction of the outline lying on an edge in the photo (0–1).</param>
/// <param name="RmsMm">Typical distance from the outline to those edges, mm.</param>
public sealed record FitResult(Vec2 Center, Vec2 Offset, double AngleDeg, double Scale, double OnEdge, double RmsMm)
{
    public Vec2 Apply(Vec2 p)
    {
        double r = AngleDeg * Math.PI / 180, c = Math.Cos(r), s = Math.Sin(r);
        var d = (p - Center) * Scale;
        return Center + Offset + new Vec2(c * d.X - s * d.Y, s * d.X + c * d.Y);
    }
}

/// <summary>
/// Fits an outline (a vector design) to the edges of a real object in a straightened
/// camera picture: chamfer matching on an edge map, coarse search then refinement.
/// The picture covers <c>area</c> at <c>mmPerPx</c>; pixel (x, y) is centred on
/// <c>area.Min + (x + ½, y + ½) · mmPerPx</c>, as <see cref="CameraWarp.Warp"/> makes it.
/// </summary>
public static class ShapeFit
{
    const double OnEdgeMm = 0.5;

    /// <exception cref="ArgumentException">The outline is empty.</exception>
    public static FitResult Fit(GrayImage image, Rect2 area, double mmPerPx, IReadOnlyList<IReadOnlyList<Vec2>> outline,
        FitOptions? options = null, CancellationToken cancellationToken = default)
    {
        var opt = options ?? new FitOptions();
        var bounds = Rect2.Bounds(outline.SelectMany(p => p));
        if (bounds.IsEmpty) throw new ArgumentException("The outline is empty.", nameof(outline));
        var center = bounds.Center;

        var edges = EdgeMap.Build(image);
        cancellationToken.ThrowIfCancellationRequested();
        var fine = DistanceField.Build(edges.IsEdge, image.Width, image.Height);

        // Coarse field: edges pooled into ~1 mm cells.
        int k = Math.Max(1, (int)Math.Round(1.0 / mmPerPx));
        int cw = (image.Width + k - 1) / k, ch = (image.Height + k - 1) / k;
        var pooled = new bool[cw * ch];
        for (int y = 0; y < image.Height; y++)
            for (int x = 0; x < image.Width; x++)
                if (edges.IsEdge[y * image.Width + x]) pooled[y / k * cw + x / k] = true;
        var coarse = DistanceField.Build(pooled, cw, ch);
        double coarseMmPerPx = mmPerPx * k;

        double perimeter = outline.Sum(p => Length(p));
        var coarseSamples = Sample(outline, Math.Max(perimeter / 160, 0.5));
        var fineSamples = Sample(outline, Math.Clamp(perimeter / 4000, 0.15, 0.5));

        // ---- coarse search over angle × scale × shift ----
        var angles = new List<double>();
        if (opt.MaxRotationDeg >= 179) for (double a = -180; a < 180; a += 4) angles.Add(a);
        else if (opt.MaxRotationDeg <= 0) angles.Add(0);
        else for (double a = -opt.MaxRotationDeg; a <= opt.MaxRotationDeg + 1e-9; a += 2) angles.Add(a);
        var scales = new List<double>();
        if (opt.MaxScaleChange <= 0) scales.Add(1);
        else for (double s = 1 - opt.MaxScaleChange; s <= 1 + opt.MaxScaleChange + 1e-9; s += 0.03) scales.Add(s);
        double step = coarseMmPerPx;
        int n = Math.Max(1, (int)Math.Ceiling(opt.SearchMm / step));
        const double TauCoarse = 3;

        var results = new List<(double Cost, double Tx, double Ty, double A, double S)>();
        var gate = new object();
        Parallel.ForEach(angles.SelectMany(a => scales.Select(s => (a, s))), new ParallelOptions { CancellationToken = cancellationToken }, pair =>
        {
            var (a, s) = pair;
            var moved = Place(coarseSamples, center, new Vec2(0, 0), a, s);
            var local = new List<(double, double, double, double, double)>();
            for (int iy = -n; iy <= n; iy++)
                for (int ix = -n; ix <= n; ix++)
                {
                    double tx = ix * step, ty = iy * step;
                    if (tx * tx + ty * ty > opt.SearchMm * opt.SearchMm + 1e-9) continue;
                    double sum = 0;
                    foreach (var (p, _) in moved)
                    {
                        double d = coarse.DistanceMm(p.X + tx, p.Y + ty, area, coarseMmPerPx);
                        sum += Math.Min(d * d, TauCoarse * TauCoarse);
                    }
                    local.Add((sum / moved.Count, tx, ty, a, s));
                }
            local.Sort((p, q) => p.Item1.CompareTo(q.Item1));
            lock (gate) results.AddRange(local.Take(4));
        });

        // Keep a few distinct good starts.
        results.Sort((p, q) => p.Cost.CompareTo(q.Cost));
        var starts = new List<(double Tx, double Ty, double A, double S)>();
        foreach (var r in results)
        {
            if (starts.Any(q => Math.Abs(q.Tx - r.Tx) < 2 && Math.Abs(q.Ty - r.Ty) < 2 && Math.Abs(AngleDiff(q.A, r.A)) < 6 && Math.Abs(q.S - r.S) < 0.05)) continue;
            starts.Add((r.Tx, r.Ty, r.A, r.S));
            if (starts.Count == 6) break;
        }

        // ---- refine each start, with a shrinking capture distance ----
        double best = double.MaxValue;
        double[] bestX = [0, 0, 0, 0];
        foreach (var st in starts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            double[] x = [st.Tx, st.Ty, st.A, Math.Log(st.S)];
            double cost = 0;
            foreach (double tau in new[] { 2.0, 1.0, 0.5 })
            {
                double F(double[] v) => FineCost(v, tau);
                x = NelderMead(F, x, [0.4, 0.4, 0.8, 0.008], 160);
                cost = F(x);
            }
            if (cost < best) { best = cost; bestX = x; }
        }

        // ---- score ----
        var final = Place(fineSamples, center, new Vec2(bestX[0], bestX[1]), bestX[2], Math.Exp(bestX[3]));
        int on = 0;
        double sq = 0;
        foreach (var (p, _) in final)
        {
            double d = fine.DistanceMm(p.X, p.Y, area, mmPerPx);
            if (d <= OnEdgeMm) { on++; sq += d * d; }
        }
        return new FitResult(center, new Vec2(bestX[0], bestX[1]), Normalize(bestX[2]), Math.Exp(bestX[3]),
            (double)on / final.Count, on > 0 ? Math.Sqrt(sq / on) : double.NaN);

        double FineCost(double[] v, double tau)
        {
            double a = v[2], s = Math.Exp(v[3]);
            // Stay inside the allowed ranges.
            double pen = 0;
            if (opt.MaxRotationDeg < 179) pen += Over(Math.Abs(AngleDiff(a, 0)), opt.MaxRotationDeg) * 10;
            pen += Over(Math.Abs(s - 1), Math.Max(opt.MaxScaleChange, 1e-6)) * 400;
            pen += Over(Math.Sqrt(v[0] * v[0] + v[1] * v[1]), opt.SearchMm + 2) * 10;
            double sum = 0;
            foreach (var (p, nrm) in Place(fineSamples, center, new Vec2(v[0], v[1]), a, s))
            {
                double d = fine.DistanceMm(p.X, p.Y, area, mmPerPx);
                double c = Math.Min(d * d, tau * tau);
                if (d < tau)
                {
                    // Penalise lying along an edge that runs the wrong way (slats, grain).
                    var g = edges.DirectionAt(fine.NearestEdge(p.X, p.Y, area, mmPerPx));
                    double cos = Math.Abs(g.X * nrm.X + g.Y * nrm.Y);
                    c += 0.4 * tau * tau * (1 - cos);
                }
                sum += c;
            }
            return sum / fineSamples.Count + pen;
        }
    }

    static double Over(double v, double limit) => v > limit ? (v - limit) * (v - limit) : 0;
    static double AngleDiff(double a, double b) => Normalize(a - b);
    static double Normalize(double a) { a %= 360; if (a > 180) a -= 360; if (a <= -180) a += 360; return a; }
    static double Length(IReadOnlyList<Vec2> p) { double l = 0; for (int i = 1; i < p.Count; i++) l += p[i].Dist(p[i - 1]); return l; }

    /// <summary>Points every <paramref name="spacing"/> mm along the outline, with unit normals.</summary>
    static List<(Vec2 P, Vec2 N)> Sample(IReadOnlyList<IReadOnlyList<Vec2>> outline, double spacing)
    {
        var o = new List<(Vec2, Vec2)>();
        foreach (var path in outline)
        {
            double carry = 0;
            for (int i = 1; i < path.Count; i++)
            {
                var a = path[i - 1]; var b = path[i];
                double len = a.Dist(b);
                if (len < 1e-9) continue;
                var t = (b - a) / len;
                var nrm = new Vec2(-t.Y, t.X);
                double d = carry;
                for (; d < len; d += spacing) o.Add((a + t * d, nrm));
                carry = d - len; // where the next point falls on the following segment
            }
        }
        return o;
    }

    static List<(Vec2 P, Vec2 N)> Place(List<(Vec2 P, Vec2 N)> samples, Vec2 center, Vec2 offset, double angleDeg, double scale)
    {
        double r = angleDeg * Math.PI / 180, c = Math.Cos(r), s = Math.Sin(r);
        var o = new List<(Vec2, Vec2)>(samples.Count);
        foreach (var (p, nrm) in samples)
        {
            var d = (p - center) * scale;
            o.Add((center + offset + new Vec2(c * d.X - s * d.Y, s * d.X + c * d.Y), new Vec2(c * nrm.X - s * nrm.Y, s * nrm.X + c * nrm.Y)));
        }
        return o;
    }

    static double[] NelderMead(Func<double[], double> f, double[] x0, double[] step, int iterations)
    {
        int n = x0.Length;
        var pts = new double[n + 1][];
        var val = new double[n + 1];
        for (int i = 0; i <= n; i++)
        {
            pts[i] = (double[])x0.Clone();
            if (i > 0) pts[i][i - 1] += step[i - 1];
            val[i] = f(pts[i]);
        }
        for (int it = 0; it < iterations; it++)
        {
            Array.Sort(val, pts);
            var centroid = new double[n];
            for (int i = 0; i < n; i++) for (int j = 0; j < n; j++) centroid[j] += pts[i][j] / n;
            double[] At(double t) { var p = new double[n]; for (int j = 0; j < n; j++) p[j] = centroid[j] + t * (pts[n][j] - centroid[j]); return p; }
            var xr = At(-1); double fr = f(xr);
            if (fr < val[0])
            {
                var xe = At(-2); double fe = f(xe);
                if (fe < fr) { pts[n] = xe; val[n] = fe; } else { pts[n] = xr; val[n] = fr; }
            }
            else if (fr < val[n - 1]) { pts[n] = xr; val[n] = fr; }
            else
            {
                var xc = fr < val[n] ? At(-0.5) : At(0.5); double fc = f(xc);
                if (fc < Math.Min(fr, val[n])) { pts[n] = xc; val[n] = fc; }
                else
                    for (int i = 1; i <= n; i++)
                    {
                        for (int j = 0; j < n; j++) pts[i][j] = pts[0][j] + 0.5 * (pts[i][j] - pts[0][j]);
                        val[i] = f(pts[i]);
                    }
            }
        }
        Array.Sort(val, pts);
        return pts[0];
    }
}

/// <summary>Thin edges (Sobel + non-maximum suppression) and their directions.</summary>
public sealed class EdgeMap
{
    public required bool[] IsEdge { get; init; }
    /// <summary>Unit gradient direction per pixel (across the edge).</summary>
    public required Vec2[] Direction { get; init; }

    public Vec2 DirectionAt(int index) => index >= 0 ? Direction[index] : new Vec2(0, 0);

    public static EdgeMap Build(GrayImage img)
    {
        int w = img.Width, h = img.Height;
        var blurred = Blur(img.Pixels, w, h);
        var mag = new float[w * h];
        var dir = new Vec2[w * h];
        for (int y = 1; y < h - 1; y++)
            for (int x = 1; x < w - 1; x++)
            {
                float P(int dx, int dy) => blurred[(y + dy) * w + x + dx];
                float gx = P(1, -1) + 2 * P(1, 0) + P(1, 1) - P(-1, -1) - 2 * P(-1, 0) - P(-1, 1);
                float gy = P(-1, 1) + 2 * P(0, 1) + P(1, 1) - P(-1, -1) - 2 * P(0, -1) - P(1, -1);
                float m = MathF.Sqrt(gx * gx + gy * gy);
                mag[y * w + x] = m;
                if (m > 0) dir[y * w + x] = new Vec2(gx / m, gy / m);
            }
        // Threshold relative to the strong edges in view, with a floor against noise.
        var sorted = mag.Where(m => m > 0).OrderBy(m => m).ToArray();
        float strong = sorted.Length == 0 ? 0 : sorted[(int)(sorted.Length * 0.98)];
        float t = Math.Max(24, strong * 0.22f);
        var edge = new bool[w * h];
        for (int y = 2; y < h - 2; y++)
            for (int x = 2; x < w - 2; x++)
            {
                int i = y * w + x;
                float m = mag[i];
                if (m < t) continue;
                // Keep only the ridge across the edge.
                int dx = (int)Math.Round(dir[i].X), dy = (int)Math.Round(dir[i].Y);
                if (m >= mag[i + dy * w + dx] && m > mag[i - dy * w - dx]) edge[i] = true;
            }
        return new EdgeMap { IsEdge = edge, Direction = dir };
    }

    static float[] Blur(byte[] src, int w, int h)
    {
        float[] k = [0.106f, 0.244f, 0.300f, 0.244f, 0.106f]; // Gaussian, σ ≈ 1.2 px
        var tmp = new float[w * h];
        var o = new float[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float s = 0;
                for (int j = -2; j <= 2; j++) s += k[j + 2] * src[y * w + Math.Clamp(x + j, 0, w - 1)];
                tmp[y * w + x] = s;
            }
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float s = 0;
                for (int j = -2; j <= 2; j++) s += k[j + 2] * tmp[Math.Clamp(y + j, 0, h - 1) * w + x];
                o[y * w + x] = s;
            }
        return o;
    }
}

/// <summary>Exact Euclidean distance to the nearest edge pixel (Felzenszwalb–Huttenlocher), with which pixel that is.</summary>
public sealed class DistanceField
{
    readonly float[] _dist;   // pixels
    readonly int[] _nearest;  // index of the nearest edge pixel, −1 if none
    readonly int _w, _h;

    DistanceField(float[] dist, int[] nearest, int w, int h) { _dist = dist; _nearest = nearest; _w = w; _h = h; }

    public static DistanceField Build(bool[] edge, int w, int h)
    {
        const double Inf = 1e20;
        var f = new double[w * h];
        var srcY = new int[w * h];
        var col = new double[h];
        var colOut = new double[h];
        var colArg = new int[h];
        for (int x = 0; x < w; x++)
        {
            for (int y = 0; y < h; y++) col[y] = edge[y * w + x] ? 0 : Inf;
            Transform1D(col, h, colOut, colArg);
            for (int y = 0; y < h; y++) { f[y * w + x] = colOut[y]; srcY[y * w + x] = colArg[y]; }
        }
        var dist = new float[w * h];
        var nearest = new int[w * h];
        var row = new double[w];
        var rowOut = new double[w];
        var rowArg = new int[w];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++) row[x] = f[y * w + x];
            Transform1D(row, w, rowOut, rowArg);
            for (int x = 0; x < w; x++)
            {
                int sx = rowArg[x];
                bool found = rowOut[x] < Inf / 2;
                dist[y * w + x] = found ? (float)Math.Sqrt(rowOut[x]) : 1e6f;
                nearest[y * w + x] = found ? srcY[y * w + sx] * w + sx : -1;
            }
        }
        return new DistanceField(dist, nearest, w, h);
    }

    static void Transform1D(double[] f, int n, double[] d, int[] arg)
    {
        var v = new int[n];
        var z = new double[n + 1];
        int k = 0;
        v[0] = 0; z[0] = double.NegativeInfinity; z[1] = double.PositiveInfinity;
        for (int q = 1; q < n; q++)
        {
            double s = ((f[q] + (double)q * q) - (f[v[k]] + (double)v[k] * v[k])) / (2.0 * q - 2.0 * v[k]);
            while (s <= z[k])
            {
                k--;
                s = ((f[q] + (double)q * q) - (f[v[k]] + (double)v[k] * v[k])) / (2.0 * q - 2.0 * v[k]);
            }
            k++;
            v[k] = q; z[k] = s; z[k + 1] = double.PositiveInfinity;
        }
        k = 0;
        for (int q = 0; q < n; q++)
        {
            while (z[k + 1] < q) k++;
            d[q] = (double)(q - v[k]) * (q - v[k]) + f[v[k]];
            arg[q] = v[k];
        }
    }

    /// <summary>Distance (mm) from a point (mm) to the nearest edge, interpolated; far outside counts as far.</summary>
    public double DistanceMm(double xMm, double yMm, Rect2 area, double mmPerPx)
    {
        double px = (xMm - area.MinX) / mmPerPx - 0.5, py = (yMm - area.MinY) / mmPerPx - 0.5;
        if (px < 0 || py < 0 || px > _w - 1 || py > _h - 1) return 1e3;
        int x0 = (int)px, y0 = (int)py;
        int x1 = Math.Min(x0 + 1, _w - 1), y1 = Math.Min(y0 + 1, _h - 1);
        double tx = px - x0, ty = py - y0;
        double d = (_dist[y0 * _w + x0] * (1 - tx) + _dist[y0 * _w + x1] * tx) * (1 - ty)
                 + (_dist[y1 * _w + x0] * (1 - tx) + _dist[y1 * _w + x1] * tx) * ty;
        return d * mmPerPx;
    }

    /// <summary>Index of the edge pixel nearest a point (mm), or −1.</summary>
    public int NearestEdge(double xMm, double yMm, Rect2 area, double mmPerPx)
    {
        int x = (int)Math.Round((xMm - area.MinX) / mmPerPx - 0.5), y = (int)Math.Round((yMm - area.MinY) / mmPerPx - 0.5);
        if (x < 0 || y < 0 || x >= _w || y >= _h) return -1;
        return _nearest[y * _w + x];
    }
}
