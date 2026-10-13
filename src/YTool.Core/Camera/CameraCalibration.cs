namespace YTool.Core.Camera;

/// <summary>Maps machine millimetres to a position in the camera picture.</summary>
public interface IImageMap
{
    /// <summary>Normalised picture position (0–1 across, 0–1 down) of machine point <paramref name="mm"/>.</summary>
    Vec2 ToImage(Vec2 mm);
}

/// <summary>A burned mark: where it is on the machine, and where the camera saw it.</summary>
/// <param name="Mm">Machine position (mm).</param>
/// <param name="Image">Normalised picture position (0–1).</param>
public sealed record CalibrationPoint(Vec2 Mm, Vec2 Image);

public sealed class CalibrationException(string message) : Exception(message);

/// <summary>
/// Camera ↔ machine calibration for one material distance: a perspective map
/// (the camera looks at the bed at an angle) plus a smooth polynomial
/// correction for lens distortion, fitted to burned marks.
/// </summary>
public sealed class CameraCalibration : IImageMap
{
    public double DistanceMm { get; }
    public DateTimeOffset Created { get; }
    public IReadOnlyList<CalibrationPoint> Points { get; }
    /// <summary>Root-mean-square error at the marks, in mm.</summary>
    public double RmsMm { get; }
    /// <summary>Largest error at any mark, in mm.</summary>
    public double MaxMm { get; }
    /// <summary>Per-point error in mm (same order as <see cref="Points"/>).</summary>
    public IReadOnlyList<double> ErrorsMm { get; }

    readonly Homography _h;
    readonly double[]? _cx, _cy;
    readonly int _terms;
    readonly Vec2 _centre;
    readonly double _scale;

    CameraCalibration(double distance, DateTimeOffset created, IReadOnlyList<CalibrationPoint> pts, Homography h,
        double[]? cx, double[]? cy, int terms, Vec2 centre, double scale)
    {
        DistanceMm = distance;
        Created = created;
        Points = pts;
        _h = h;
        _cx = cx;
        _cy = cy;
        _terms = terms;
        _centre = centre;
        _scale = scale;
        ErrorsMm = pts.Select(p => ErrorMm(p)).ToList();
        RmsMm = Math.Sqrt(ErrorsMm.Average(e => e * e));
        MaxMm = ErrorsMm.Max();
    }

    /// <exception cref="CalibrationException">Too few or badly placed points.</exception>
    public static CameraCalibration Fit(IReadOnlyList<CalibrationPoint> points, double distanceMm, DateTimeOffset? created = null)
    {
        if (points.Count < 4) throw new CalibrationException("At least 4 marks are needed.");
        if (points.Any(p => !p.Mm.IsFinite || !p.Image.IsFinite)) throw new CalibrationException("A mark position is not a valid number.");
        var h = Homography.Fit(points.Select(p => (p.Mm, p.Image)).ToList())
                ?? throw new CalibrationException("The marks don't form a usable pattern (are three of them in a line?).");
        var b = Rect2.Bounds(points.Select(p => p.Mm));
        var centre = b.Center;
        double scale = Math.Max(1e-6, Math.Max(b.Width, b.Height) / 2);

        // Enough points for a cubic (10 terms) or quadratic (6) distortion correction?
        int terms = points.Count >= 16 ? 10 : points.Count >= 9 ? 6 : 0;
        double[]? cx = null, cy = null;
        if (terms > 0)
        {
            var rows = points.Select(p => Basis(Norm(p.Mm, centre, scale), terms)).ToArray();
            var rx = points.Select(p => p.Image.X - h.Apply(p.Mm).X).ToArray();
            var ry = points.Select(p => p.Image.Y - h.Apply(p.Mm).Y).ToArray();
            cx = LinearAlgebra.LeastSquares(rows, rx, 1e-9);
            cy = LinearAlgebra.LeastSquares(rows, ry, 1e-9);
            if (cx is null || cy is null) terms = 0;
        }
        return new CameraCalibration(distanceMm, created ?? DateTimeOffset.Now, points, h, terms > 0 ? cx : null, terms > 0 ? cy : null, terms, centre, scale);
    }

    static Vec2 Norm(Vec2 mm, Vec2 c, double s) => new((mm.X - c.X) / s, (mm.Y - c.Y) / s);

    static double[] Basis(Vec2 u, int terms)
    {
        double x = u.X, y = u.Y;
        return terms == 10
            ? [1, x, y, x * x, x * y, y * y, x * x * x, x * x * y, x * y * y, y * y * y]
            : [1, x, y, x * x, x * y, y * y];
    }

    public Vec2 ToImage(Vec2 mm)
    {
        var p = _h.Apply(mm);
        if (_cx is null || _cy is null) return p;
        var u = Norm(mm, _centre, _scale);
        double x = u.X, y = u.Y;
        double dx = _cx[0] + _cx[1] * x + _cx[2] * y + _cx[3] * x * x + _cx[4] * x * y + _cx[5] * y * y;
        double dy = _cy[0] + _cy[1] * x + _cy[2] * y + _cy[3] * x * x + _cy[4] * x * y + _cy[5] * y * y;
        if (_terms == 10)
        {
            double x3 = x * x * x, x2y = x * x * y, xy2 = x * y * y, y3 = y * y * y;
            dx += _cx[6] * x3 + _cx[7] * x2y + _cx[8] * xy2 + _cx[9] * y3;
            dy += _cy[6] * x3 + _cy[7] * x2y + _cy[8] * xy2 + _cy[9] * y3;
        }
        return new(p.X + dx, p.Y + dy);
    }

    /// <summary>The mark's picture error converted to mm through the local scale of the map.</summary>
    double ErrorMm(CalibrationPoint p)
    {
        const double D = 0.5;
        var c = ToImage(p.Mm);
        var jx = (ToImage(p.Mm + new Vec2(D, 0)) - c) / D;
        var jy = (ToImage(p.Mm + new Vec2(0, D)) - c) / D;
        double det = jx.X * jy.Y - jy.X * jx.Y;
        var r = p.Image - c;
        if (Math.Abs(det) < 1e-18) return double.PositiveInfinity;
        // Solve [jx jy]·d = r for d (mm).
        double dxMm = (r.X * jy.Y - jy.X * r.Y) / det;
        double dyMm = (jx.X * r.Y - r.X * jx.Y) / det;
        return Math.Sqrt(dxMm * dxMm + dyMm * dyMm);
    }
}

/// <summary>
/// The camera picture at any material distance, from calibrations made at a
/// few distances. Every machine point's picture position is followed across
/// the calibrated distances: a straight line through two of them, or a smooth
/// (quadratic) curve through three or more, since the view shifts slightly
/// non-linearly with height. Outside the calibrated range the trend of the
/// nearest two is continued in a straight line.
/// </summary>
sealed class DistanceModel(IReadOnlyList<CameraCalibration> sorted, double distance) : IImageMap
{
    public Vec2 ToImage(Vec2 mm)
    {
        int n = sorted.Count;
        double lo = sorted[0].DistanceMm, hi = sorted[^1].DistanceMm, d = distance;
        if (n == 2 || d <= lo || d >= hi)
        {
            // Two nearest (the end pair when extrapolating).
            int a = d <= lo ? 0 : d >= hi ? n - 2 : Math.Max(0, sorted.Count(c => c.DistanceMm < d) - 1);
            var ca = sorted[a];
            var cb = sorted[a + 1];
            double t = (d - ca.DistanceMm) / (cb.DistanceMm - ca.DistanceMm);
            return Vec2.Lerp(ca.ToImage(mm), cb.ToImage(mm), t);
        }
        // Inside the range with ≥ 3 calibrations: least-squares quadratic in distance.
        double s0 = n, s1 = 0, s2 = 0, s3 = 0, s4 = 0;
        Vec2 t0 = default, t1 = default, t2 = default;
        double mid = (lo + hi) / 2, half = Math.Max(1e-9, (hi - lo) / 2);
        foreach (var c in sorted)
        {
            double x = (c.DistanceMm - mid) / half;
            var y = c.ToImage(mm);
            s1 += x; s2 += x * x; s3 += x * x * x; s4 += x * x * x * x;
            t0 += y; t1 += y * x; t2 += y * (x * x);
        }
        // Solve [[s0 s1 s2][s1 s2 s3][s2 s3 s4]] · [c0 c1 c2] = t for each axis (Cramer's rule).
        double det = s0 * (s2 * s4 - s3 * s3) - s1 * (s1 * s4 - s3 * s2) + s2 * (s1 * s3 - s2 * s2);
        if (Math.Abs(det) < 1e-12) return Vec2.Lerp(sorted[0].ToImage(mm), sorted[^1].ToImage(mm), (d - lo) / (hi - lo));
        double Solve(double r0, double r1, double r2, int k)
        {
            double[,] m = { { s0, s1, s2 }, { s1, s2, s3 }, { s2, s3, s4 } };
            double[] r = [r0, r1, r2];
            for (int row = 0; row < 3; row++) m[row, k] = r[row];
            return (m[0, 0] * (m[1, 1] * m[2, 2] - m[1, 2] * m[2, 1]) - m[0, 1] * (m[1, 0] * m[2, 2] - m[1, 2] * m[2, 0]) + m[0, 2] * (m[1, 0] * m[2, 1] - m[1, 1] * m[2, 0])) / det;
        }
        double q = (d - mid) / half;
        double X(int k) => Solve(t0.X, t1.X, t2.X, k);
        double Y(int k) => Solve(t0.Y, t1.Y, t2.Y, k);
        return new(X(0) + X(1) * q + X(2) * q * q, Y(0) + Y(1) * q + Y(2) * q * q);
    }
}

/// <summary>The calibration to use for a material distance.</summary>
/// <param name="Map">Mapping to use.</param>
/// <param name="Description">Plain-language note, e.g. "calibrated at 21 mm".</param>
/// <param name="Exact">False when the distance is well outside what was calibrated.</param>
public sealed record ImageMapChoice(IImageMap Map, string Description, bool Exact);

public static class CalibrationSet
{
    /// <summary>How far beyond the calibrated distances the trend is trusted (mm).</summary>
    public const double TrustedExtrapolationMm = 5;

    /// <summary>
    /// The picture mapping for <paramref name="distanceMm"/> (null = unknown →
    /// most recent calibration). With calibrations at two or three distances,
    /// any distance in between (or a little beyond) is calculated.
    /// </summary>
    public static ImageMapChoice? For(IReadOnlyList<CameraCalibration> cals, double? distanceMm)
    {
        if (cals.Count == 0) return null;
        if (distanceMm is not { } d || !double.IsFinite(d))
        {
            var latest = cals.MaxBy(c => c.Created)!;
            return new(latest, $"calibrated at {latest.DistanceMm:0.#} mm (machine distance unknown)", false);
        }
        var sorted = cals.OrderBy(c => c.DistanceMm).ToList();
        var nearest = sorted.MinBy(c => Math.Abs(c.DistanceMm - d))!;
        if (Math.Abs(nearest.DistanceMm - d) <= 0.25) return new(nearest, $"calibrated at {nearest.DistanceMm:0.#} mm", true);
        if (sorted.Count == 1)
        {
            bool close = Math.Abs(nearest.DistanceMm - d) <= 1;
            return new(nearest, $"calibrated at {nearest.DistanceMm:0.#} mm" +
                (close ? "" : $"; material is at {d:0.#} mm. Calibrate a second height so other heights can be calculated"), close);
        }
        double lo = sorted[0].DistanceMm, hi = sorted[^1].DistanceMm;
        string list = string.Join(", ", sorted.Select(c => $"{c.DistanceMm:0.#}"));
        bool inside = d >= lo && d <= hi;
        bool trusted = d >= lo - TrustedExtrapolationMm && d <= hi + TrustedExtrapolationMm;
        string how = inside ? "calculated" : "extended";
        return new(new DistanceModel(sorted, d), $"{how} for {d:0.#} mm from calibrations at {list} mm" +
            (trusted ? "" : ". That's well outside them: calibrate nearer this height for accuracy"), trusted);
    }
}
