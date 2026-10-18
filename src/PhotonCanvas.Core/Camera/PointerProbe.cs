using PhotonCanvas.Core.Imaging;

namespace PhotonCanvas.Core.Camera;

/// <summary>
/// Measuring heights with the red pointer and the camera in the laser head (laser triangulation).
/// The camera and the pointer's mirrors both ride on the head, so where the dot shows up in the
/// picture depends only on how far the surface is below the head's focus. A calibration on any flat
/// board records that: the head steps through known heights and the dot is photographed at each.
/// </summary>
public static class DotFinder
{
    /// <summary>
    /// The pointer dot's centre (pixels) from a photo with it on and one with it off: only what
    /// changed is left, so shiny, dark or patterned surfaces don't fool it. Null when no clear dot.
    /// </summary>
    public static (Vec2 Centre, double Strength)? Find(RasterImage on, RasterImage off)
    {
        if (on.Width != off.Width || on.Height != off.Height) return null;
        int w = on.Width, h = on.Height;
        var diff = new float[w * h];
        float max = 0;
        long sum = 0;
        for (int i = 0; i < diff.Length; i++)
        {
            int k = i * 4;
            // The red dot: more red than before, beyond any change in brightness overall.
            float dr = on.Rgba[k] - off.Rgba[k], dg = on.Rgba[k + 1] - off.Rgba[k + 1], db = on.Rgba[k + 2] - off.Rgba[k + 2];
            float v = Math.Max(0, dr) + 0.5f * Math.Max(0, dr - (dg + db) / 2);
            diff[i] = v;
            sum += (long)v;
            if (v > max) max = v;
        }
        double mean = (double)sum / diff.Length;
        // A dot stands far above the noise.
        if (max < 25 || max < mean * 8) return null;
        int peak = Array.IndexOf(diff, max);
        // Centroid of the bright patch round the peak (over half the peak, within a window).
        int px = peak % w, py = peak / w, r = Math.Max(6, Math.Min(w, h) / 30);
        double cx = 0, cy = 0, m = 0;
        for (int y = Math.Max(0, py - r); y < Math.Min(h, py + r + 1); y++)
            for (int x = Math.Max(0, px - r); x < Math.Min(w, px + r + 1); x++)
            {
                double v = diff[y * w + x] - max * 0.5;
                if (v <= 0) continue;
                cx += v * (x + 0.5); cy += v * (y + 0.5); m += v;
            }
        if (m <= 0) return null;
        return (new Vec2(cx / m, cy / m), max);
    }
}

/// <summary>
/// The pointer calibration: for a grid of aim points, where the dot appeared at each depth below
/// the head's focus. Depths are mm below the focus plane (negative = above it).
/// </summary>
public sealed record PointerCalibration(IReadOnlyList<double> Depths, IReadOnlyList<Vec2> Aims, IReadOnlyList<IReadOnlyList<Vec2?>> Seen)
{
    /// <summary>Depths the calibration steps through (mm below focus): ±8 mm in 2 mm steps.</summary>
    public static readonly double[] DefaultDepths = [-8, -6, -4, -2, 0, 2, 4, 6, 8];

    /// <summary>
    /// How far below the focus the surface is at <paramref name="aim"/>, from where the dot was seen.
    /// The dot's track for this aim (blended from the nearest calibrated aims) is followed to the
    /// point nearest what was seen. Null outside the calibrated depths or aims.
    /// </summary>
    public double? Depth(Vec2 aim, Vec2 seen)
    {
        var track = Track(aim);
        if (track is null) return null;
        double best = double.PositiveInfinity, depth = double.NaN;
        for (int i = 0; i + 1 < track.Count; i++)
        {
            if (track[i] is not { } a || track[i + 1] is not { } b) continue;
            var ab = b - a;
            double len2 = ab.Dot(ab);
            double t = len2 < 1e-12 ? 0 : Math.Clamp((seen - a).Dot(ab) / len2, 0, 1);
            double d = seen.Dist(a + ab * t);
            if (d < best) { best = d; depth = Depths[i] + (Depths[i + 1] - Depths[i]) * t; }
        }
        // Further from the track than the track's own spacing: not trusted.
        double spacing = TrackSpacing(track);
        return double.IsNaN(depth) || best > Math.Max(3, spacing * 1.5) ? null : depth;
    }

    static double TrackSpacing(List<Vec2?> track)
    {
        var d = new List<double>();
        for (int i = 0; i + 1 < track.Count; i++) if (track[i] is { } a && track[i + 1] is { } b) d.Add(a.Dist(b));
        return d.Count == 0 ? 0 : d.Average();
    }

    /// <summary>
    /// The dot's expected place at each depth for this aim: at each depth, a smooth map from aim to
    /// picture (quadratic with six or more calibrated aims, else straight) fitted to the calibration.
    /// </summary>
    List<Vec2?>? Track(Vec2 aim)
    {
        if (Aims.Count == 0) return null;
        var o = new List<Vec2?>();
        for (int d = 0; d < Depths.Count; d++)
        {
            var have = Enumerable.Range(0, Aims.Count).Where(i => Seen[i][d] is not null).ToList();
            if (have.Count < 3) { o.Add(null); continue; }
            bool quad = have.Count >= 6;
            double[] Terms(Vec2 a) => quad ? [1, a.X, a.Y, a.X * a.X, a.X * a.Y, a.Y * a.Y] : [1, a.X, a.Y];
            int n = quad ? 6 : 3;
            // Centred and scaled so the squares stay well behaved.
            var c = new Vec2(have.Average(i => Aims[i].X), have.Average(i => Aims[i].Y));
            double sc = Math.Max(1, have.Max(i => Aims[i].Dist(c)));
            Vec2 Norm(Vec2 a) => (a - c) / sc;
            var m = new double[n, n];
            var vx = new double[n];
            var vy = new double[n];
            foreach (var i in have)
            {
                var t = Terms(Norm(Aims[i]));
                var p = Seen[i][d]!.Value;
                for (int r = 0; r < n; r++)
                {
                    vx[r] += t[r] * p.X; vy[r] += t[r] * p.Y;
                    for (int k = 0; k < n; k++) m[r, k] += t[r] * t[k];
                }
            }
            if (LeastSquares.Solve(m, vx) is not { } cx || LeastSquares.Solve(m, vy) is not { } cy) { o.Add(null); continue; }
            var ta = Terms(Norm(aim));
            o.Add(new Vec2(ta.Select((v, k) => v * cx[k]).Sum(), ta.Select((v, k) => v * cy[k]).Sum()));
        }
        return o.Count(p => p is not null) >= 2 ? o : null;
    }
}

/// <summary>Small dense linear systems (normal equations).</summary>
static class LeastSquares
{
    /// <summary>Gaussian elimination with partial pivoting; null if singular.</summary>
    public static double[]? Solve(double[,] a, double[] b)
    {
        int n = b.Length;
        var m = (double[,])a.Clone();
        var x = (double[])b.Clone();
        for (int col = 0; col < n; col++)
        {
            int piv = col;
            for (int r = col + 1; r < n; r++) if (Math.Abs(m[r, col]) > Math.Abs(m[piv, col])) piv = r;
            if (Math.Abs(m[piv, col]) < 1e-12) return null;
            if (piv != col)
            {
                for (int k = 0; k < n; k++) (m[col, k], m[piv, k]) = (m[piv, k], m[col, k]);
                (x[col], x[piv]) = (x[piv], x[col]);
            }
            for (int r = col + 1; r < n; r++)
            {
                double f = m[r, col] / m[col, col];
                for (int k = col; k < n; k++) m[r, k] -= f * m[col, k];
                x[r] -= f * x[col];
            }
        }
        for (int r = n - 1; r >= 0; r--)
        {
            double sum = x[r];
            for (int k = r + 1; k < n; k++) sum -= m[r, k] * x[k];
            x[r] = sum / m[r, r];
        }
        return x;
    }
}

/// <summary>Checking measured points against each other: a point far from what its neighbours suggest is suspect.</summary>
public static class SurfaceCheck
{
    /// <summary>Indices of points more than <paramref name="tolMm"/> from the smooth surface through the others.</summary>
    public static List<int> Outliers(IReadOnlyList<SurfacePoint> pts, double tolMm = 0.6)
    {
        var bad = new List<int>();
        if (pts.Count < 8) return bad;
        // Worst first, then look again without it: one bad point mustn't make its neighbours look bad.
        while (true)
        {
            int worst = -1;
            double worstOff = tolMm;
            for (int i = 0; i < pts.Count; i++)
            {
                if (bad.Contains(i)) continue;
                // The surface through its nearest good neighbours (not itself).
                var others = Enumerable.Range(0, pts.Count).Where(j => j != i && !bad.Contains(j))
                    .OrderBy(j => pts[j].At.Dist(pts[i].At)).Take(12).Select(j => pts[j]).ToList();
                if (others.Count < 6) continue;
                double off = Math.Abs(Surface.Fit(others, SurfaceShape.Freeform, 0.2).HeightAt(pts[i].At) - pts[i].Height);
                if (off > worstOff) { worstOff = off; worst = i; }
            }
            if (worst < 0 || bad.Count > pts.Count / 4) return bad;
            bad.Add(worst);
        }
    }
}
