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

/// <summary>Linear blend of two calibrations, for a distance between them.</summary>
sealed class BlendedMap(IImageMap a, IImageMap b, double t) : IImageMap
{
    public Vec2 ToImage(Vec2 mm) => Vec2.Lerp(a.ToImage(mm), b.ToImage(mm), t);
}

/// <summary>The calibration to use for a material distance.</summary>
/// <param name="Map">Mapping to use.</param>
/// <param name="Description">Plain-language note, e.g. "calibrated at 21 mm".</param>
/// <param name="Exact">False when the nearest calibration is more than 1 mm away.</param>
public sealed record ImageMapChoice(IImageMap Map, string Description, bool Exact);

public static class CalibrationSet
{
    /// <summary>
    /// Pick or blend calibrations for <paramref name="distanceMm"/> (null =
    /// unknown → most recent). Between two saved distances the maps are
    /// interpolated; outside them the nearest is used.
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
        var below = sorted.LastOrDefault(c => c.DistanceMm < d);
        var above = sorted.FirstOrDefault(c => c.DistanceMm > d);
        if (below is not null && above is not null)
        {
            double t = (d - below.DistanceMm) / (above.DistanceMm - below.DistanceMm);
            return new(new BlendedMap(below, above, t), $"blended from {below.DistanceMm:0.#} and {above.DistanceMm:0.#} mm", true);
        }
        bool close = Math.Abs(nearest.DistanceMm - d) <= 1;
        return new(nearest, $"calibrated at {nearest.DistanceMm:0.#} mm" + (close ? "" : $"; material is at {d:0.#} mm, so the picture may be off"), close);
    }
}
