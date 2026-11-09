using PhotonCanvas.Core.Imaging;

namespace PhotonCanvas.Core.Camera;

/// <summary>
/// Measuring heights with the pointer and the camera in the laser head (laser triangulation).
/// The camera and the pointer's mirrors both ride on the head, so where the spot shows up in the
/// picture depends only on how far the surface is below the head's focus. A calibration on any flat
/// board records that: the head steps through known heights and the spot is photographed at each.
/// <para>
/// The spot needn't be a round dot: the F2 Ultra's is a short line (seen 2026-11-08). The whole bright
/// patch is found, its middle and which way it runs; a line is then read by where it crosses the
/// calibrated track (<see cref="PointerCalibration.Depth(Vec2, PointerSpot)"/>), so how much of the line
/// shows doesn't matter.
/// </para>
/// </summary>
public static class DotFinder
{
    /// <summary>How much brighter (0–255) the spot must make its brightest pixel.</summary>
    public const float MinStrength = 110;

    /// <summary>
    /// The pointer's spot from a photo with it on and one with it off: only what got brighter is left,
    /// so shiny, dark or patterned surfaces don't fool it, whatever colour the pointer is. Null when
    /// there's no clear spot.
    /// </summary>
    public static PointerSpot? Find(RasterImage on, RasterImage off)
    {
        if (on.Width != off.Width || on.Height != off.Height) return null;
        int w = on.Width, h = on.Height;
        var raw = new float[w * h];
        double rawSum = 0;
        for (int i = 0; i < raw.Length; i++)
        {
            int k = i * 4;
            // How much brighter it got, in whichever colour the pointer is.
            float dr = on.Rgba[k] - off.Rgba[k], dg = on.Rgba[k + 1] - off.Rgba[k + 1], db = on.Rgba[k + 2] - off.Rgba[k + 2];
            raw[i] = Math.Max(dr, Math.Max(dg, db));
            rawSum += raw[i];
        }
        // Less any change over the whole picture (the camera's exposure).
        float shift = (float)Math.Max(0, rawSum / raw.Length);
        var diff = new float[raw.Length];
        float max = 0;
        double sum = 0;
        for (int i = 0; i < raw.Length; i++)
        {
            float v = Math.Max(0, raw[i] - shift);
            diff[i] = v;
            sum += v;
            if (v > max) max = v;
        }
        double mean = sum / diff.Length;
        // A spot stands well clear of flickers too: the pointer reads about 200 on a part with the lights on, while
        // where it can't be seen, flickers (the red dot under the head, which stays on) read 25–100 (Kyle's log).
        if (max < MinStrength || max < mean * 8) return null;
        int peak = Array.IndexOf(diff, max);

        // The bright patch joined to the peak (over a third of it), however long it runs.
        float cut = max * 0.35f;
        int limit = Math.Max(64, w * h / 20);
        var seen = new bool[diff.Length];
        var stack = new Stack<int>();
        stack.Push(peak);
        seen[peak] = true;
        double m = 0, cx = 0, cy = 0, sxx = 0, syy = 0, sxy = 0;
        int count = 0;
        while (stack.Count > 0 && count < limit)
        {
            int i = stack.Pop();
            int x = i % w, y = i / w;
            double v = diff[i] - cut;
            double px = x + 0.5, py = y + 0.5;
            m += v; cx += v * px; cy += v * py;
            sxx += v * px * px; syy += v * py * py; sxy += v * px * py;
            count++;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if ((dx | dy) == 0 || nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                    int j = ny * w + nx;
                    if (seen[j] || diff[j] < cut) continue;
                    seen[j] = true;
                    stack.Push(j);
                }
        }
        if (m <= 0) return null;
        cx /= m; cy /= m;
        double vxx = Math.Max(0, sxx / m - cx * cx), vyy = Math.Max(0, syy / m - cy * cy), vxy = sxy / m - cx * cy;
        // Which way it runs: the long axis of the patch.
        double tr = vxx + vyy, det = vxx * vyy - vxy * vxy, disc = Math.Sqrt(Math.Max(0, tr * tr / 4 - det));
        double l1 = tr / 2 + disc, l2 = Math.Max(1e-6, tr / 2 - disc);
        double angle = 0.5 * Math.Atan2(2 * vxy, vxx - vyy);
        var along = new Vec2(Math.Cos(angle), Math.Sin(angle));
        return new PointerSpot(new Vec2(cx, cy), max, along, Math.Sqrt(l1 / l2), 4 * Math.Sqrt(l1));
    }
}

/// <summary>The pointer's spot in a photo.</summary>
/// <param name="Centre">The middle of the bright patch (pixels).</param>
/// <param name="Strength">How much brighter its brightest pixel got.</param>
/// <param name="Along">Which way it runs (a unit vector; either way round).</param>
/// <param name="Elongation">Its length over its width: about 1 for a round dot, more for a line.</param>
/// <param name="LengthPx">About how long it is (pixels).</param>
public readonly record struct PointerSpot(Vec2 Centre, double Strength, Vec2 Along, double Elongation, double LengthPx)
{
    /// <summary>Long enough to be read as a line (by where it crosses the calibrated track).</summary>
    public bool IsLine => Elongation >= 2.5;
}

/// <summary>
/// The pointer calibration: for a grid of aim points, where the dot appeared at each depth below
/// the head's focus. Depths are mm below the focus plane (negative = above it).
/// </summary>
public sealed record PointerCalibration(IReadOnlyList<double> Depths, IReadOnlyList<Vec2> Aims, IReadOnlyList<IReadOnlyList<Vec2?>> Seen)
{
    /// <summary>Depths the calibration steps through (mm below focus): ±8 mm in 2 mm steps.</summary>
    public static readonly double[] DefaultDepths = [-8, -6, -4, -2, 0, 2, 4, 6, 8];

    /// <summary>How far outside the calibrated aims (mm) a spot is still worked out from them.</summary>
    public const double Margin = 5;

    /// <summary>How far past the end of a track (a fraction of its last step) a spot still reads as the end depth.</summary>
    const double OverEnd = 0.15;

    /// <summary>The area the pointer was calibrated over (the aims' bounds).</summary>
    public Rect2 Covered => Aims.Count == 0 ? Rect2.Empty : Rect2.Bounds(Aims);

    /// <summary>
    /// Whether <paramref name="aim"/> can be read: within <see cref="Margin"/> of the calibrated area. Further out the
    /// fitted map from aim to picture is a guess (Kyle's scan, 2026-11-09, read places 80 mm out of a 30 mm square
    /// and they came back tens of mm wrong).
    /// </summary>
    public bool Covers(Vec2 aim)
    {
        var c = Covered;
        return !c.IsEmpty && aim.X >= c.MinX - Margin && aim.X <= c.MaxX + Margin && aim.Y >= c.MinY - Margin && aim.Y <= c.MaxY + Margin;
    }

    /// <summary>
    /// How far below the focus the surface is at <paramref name="aim"/>, from where the dot was seen.
    /// The dot's track for this aim (blended from the nearest calibrated aims) is followed to the
    /// point nearest what was seen. Null outside the calibrated depths, or more than <see cref="Margin"/> outside the calibrated aims.
    /// </summary>
    public double? Depth(Vec2 aim, Vec2 seen)
    {
        var track = Track(aim);
        if (track is null) return null;
        double best = double.PositiveInfinity, depth = double.NaN, bestRaw = 0;
        int bestSeg = -1, firstSeg = -1, lastSeg = -1;
        for (int i = 0; i + 1 < track.Count; i++)
        {
            if (track[i] is not { } a || track[i + 1] is not { } b) continue;
            if (firstSeg < 0) firstSeg = i;
            lastSeg = i;
            var ab = b - a;
            double len2 = ab.Dot(ab);
            double raw = len2 < 1e-12 ? 0 : (seen - a).Dot(ab) / len2, t = Math.Clamp(raw, 0, 1);
            double d = seen.Dist(a + ab * t);
            if (d < best) { best = d; depth = Depths[i] + (Depths[i + 1] - Depths[i]) * t; bestRaw = raw; bestSeg = i; }
        }
        // Further from the track than the track's own spacing: not trusted.
        double spacing = TrackSpacing(track);
        if (double.IsNaN(depth) || best > Math.Max(3, spacing * 1.5)) return null;
        // Past either end of the track: the surface is further from the focus than was calibrated. Not the end depth
        // (Kyle's log, 2026-11-09: every such spot read as exactly 8 mm above or below the focus).
        if (bestSeg == firstSeg && bestRaw < -OverEnd || bestSeg == lastSeg && bestRaw > 1 + OverEnd) return null;
        return depth;
    }

    /// <summary>
    /// How far below the focus the surface is at <paramref name="aim"/>, from the spot seen. A round dot is read by
    /// its middle (<see cref="Depth(Vec2, Vec2)"/>). A line is read by where it crosses the dot's calibrated track,
    /// so it doesn't matter how much of the line shows or where along it the brightest pixel is; if the line runs
    /// nearly along the track (it can't tell depth that way), its middle is used instead.
    /// </summary>
    public double? Depth(Vec2 aim, PointerSpot spot)
    {
        if (!spot.IsLine || Track(aim) is not { } track) return Depth(aim, spot.Centre);
        var dir = spot.Along;
        double best = double.PositiveInfinity, depth = double.NaN;
        for (int i = 0; i + 1 < track.Count; i++)
        {
            if (track[i] is not { } a || track[i + 1] is not { } b) continue;
            var ab = b - a;
            double len = ab.Length;
            if (len < 1e-9) continue;
            double cr = ab.Cross(dir);
            // Nearly along the track: this stretch can't tell.
            if (Math.Abs(cr) < 0.5 * len) continue;
            double t = (spot.Centre - a).Cross(dir) / cr;
            if (t < -1e-9 || t > 1 + 1e-9) continue;
            var at = a + ab * t;
            double off = at.Dist(spot.Centre);
            if (off < best) { best = off; depth = Depths[i] + (Depths[i + 1] - Depths[i]) * t; }
        }
        // The crossing must be near the middle of the spot: as near as a round dot must be to the track, plus a little
        // for a line only partly seen (never more than two height steps' worth, however long the line). A long smear
        // (the pointer far out of focus, or something else that changed) crossing the track somewhere along it isn't
        // a reading: Kyle's scan (2026-11-09) read about 30 mm out.
        double spacing = TrackSpacing(track);
        if (double.IsNaN(depth) || best > Math.Max(3, spacing * 1.5) + Math.Min(spot.LengthPx * 0.5, spacing * 2)) return Depth(aim, spot.Centre);
        return depth;
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
        if (!Covers(aim)) return null;
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
