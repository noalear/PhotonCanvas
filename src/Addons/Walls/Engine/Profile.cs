using Clipper2Lib;

namespace PhotonCanvas.Addon.Walls;

/// <summary>
/// A wall's top seen from the front: level stretches from left to right (mm, y up). A straight top is one
/// stretch; battlements and broken tops are many.
/// </summary>
public sealed class Profile
{
    public IReadOnlyList<(double X0, double X1, double Y)> Steps { get; }

    public Profile(IEnumerable<(double X0, double X1, double Y)> steps) => Steps = [.. steps.Where(s => s.X1 - s.X0 > 1e-9).OrderBy(s => s.X0)];

    public static Profile Level(double x0, double x1, double y) => new([(x0, x1, y)]);

    public double Min => Steps.Count == 0 ? 0 : Steps.Min(s => s.Y);
    public double Max => Steps.Count == 0 ? 0 : Steps.Max(s => s.Y);

    /// <summary>The height at x (the lower of two stretches where they meet).</summary>
    public double At(double x)
    {
        double best = double.NaN;
        foreach (var s in Steps)
            if (x >= s.X0 - 1e-9 && x <= s.X1 + 1e-9) best = double.IsNaN(best) ? s.Y : Math.Min(best, s.Y);
        return double.IsNaN(best) ? 0 : best;
    }

    /// <summary>The lowest point over a stretch of the wall.</summary>
    public double Lowest(double x0, double x1)
    {
        double best = double.MaxValue;
        foreach (var s in Steps)
            if (s.X1 > x0 - 1e-9 && s.X0 < x1 + 1e-9) best = Math.Min(best, s.Y);
        return best == double.MaxValue ? 0 : best;
    }

    /// <summary>The area under the profile, down to <paramref name="y0"/>.</summary>
    public PathsD Under(double y0)
    {
        var rects = new PathsD();
        foreach (var s in Steps)
            if (s.Y > y0 + 1e-6) rects.Add(Geo.Rect(s.X0, y0, s.X1, s.Y));
        return Geo.Union(rects);
    }

    /// <summary>Every place a stretch starts or ends.</summary>
    public IEnumerable<double> Breaks => Steps.SelectMany(s => new[] { s.X0, s.X1 });

    /// <summary>Two profiles combined stretch by stretch (over the places either changes).</summary>
    public static Profile Combine(Profile a, Profile b, Func<double, double, double, double> f)
    {
        var xs = a.Breaks.Concat(b.Breaks).Select(x => Math.Round(x, 6)).Distinct().Order().ToList();
        var steps = new List<(double, double, double)>();
        for (int i = 0; i + 1 < xs.Count; i++)
        {
            double m = (xs[i] + xs[i + 1]) / 2;
            steps.Add((xs[i], xs[i + 1], f(m, a.At(m), b.At(m))));
        }
        return new Profile(steps);
    }

    /// <summary>Neighbouring stretches at the same height joined.</summary>
    public Profile Merged()
    {
        var o = new List<(double X0, double X1, double Y)>();
        foreach (var s in Steps)
        {
            if (o.Count > 0 && Math.Abs(o[^1].Y - s.Y) < 1e-6 && Math.Abs(o[^1].X1 - s.X0) < 1e-6) o[^1] = (o[^1].X0, s.X1, s.Y);
            else o.Add(s);
        }
        return new Profile(o);
    }
}
