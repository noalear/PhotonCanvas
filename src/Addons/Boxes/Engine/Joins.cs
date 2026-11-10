using Clipper2Lib;

namespace PhotonCanvas.Addon.Boxes;

/// <summary>How two pieces cut from one are joined edge to edge, in the same plane.</summary>
public enum JoinKind
{
    /// <summary>Dovetails: tails wider at the tip than at the root, so the pieces lock and can't be pulled apart in the plane.</summary>
    Dovetail,
    /// <summary>Jigsaw knobs: a round head on a neck; locks like the dovetail, with no sharp corners to split.</summary>
    Jigsaw,
    /// <summary>Square fingers: don't lock in the plane, but give glue plenty to hold.</summary>
    Fingers,
    /// <summary>A straight cut (glue a strip behind it).</summary>
    Straight,
}

/// <summary>A join's shape: its kind, how far each tail reaches across the line, and the play between the pieces.</summary>
public sealed record JoinStyle(JoinKind Kind = JoinKind.Dovetail, double SizeMm = 8, double FitMm = 0)
{
    /// <summary>The dovetails' slope each side (degrees): steeper than a woodworker's 1:6, since the sheet is thin.</summary>
    public const double DovetailAngle = 14;
}

/// <summary>
/// Joins that let one shape be cut as several pieces that fit back together edge to edge: a panel
/// bigger than the bed, or the long wall of a bent box. The seams are lines across the shape
/// (x = constant, or y = constant); along each, tails stick out from the piece before the seam
/// into the piece after it, kept clear of the shape's edges.
/// </summary>
public static class Joins
{
    /// <summary>Distance from one tail's middle to the next.</summary>
    public static double Pitch(JoinStyle s) => s.Kind switch
    {
        JoinKind.Dovetail => 2 * s.SizeMm + 2 * s.SizeMm * Math.Tan(JoinStyle.DovetailAngle * Math.PI / 180) + s.SizeMm * 0.3,
        JoinKind.Jigsaw => s.SizeMm * 2.1,
        JoinKind.Fingers => s.SizeMm * 2,
        _ => double.PositiveInfinity,
    };

    /// <summary>How wide the widest part of a tail is, along the seam.</summary>
    static double Span(JoinStyle s) => s.Kind switch
    {
        JoinKind.Dovetail => s.SizeMm + 2 * s.SizeMm * Math.Tan(JoinStyle.DovetailAngle * Math.PI / 180),
        JoinKind.Jigsaw => s.SizeMm,
        JoinKind.Fingers => s.SizeMm,
        _ => 0,
    };

    /// <summary>The middles of the tails along stretches of the seam (each kept <paramref name="margin"/> from its ends).</summary>
    public static List<double> TailCentres(JoinStyle s, IEnumerable<(double Lo, double Hi)> stretches, double margin)
    {
        var o = new List<double>();
        if (s.Kind == JoinKind.Straight) return o;
        double pitch = Pitch(s), span = Span(s);
        foreach (var (lo, hi) in stretches)
        {
            double a = lo + margin, b = hi - margin, len = b - a;
            if (len < span + 0.5) continue;
            int n = Math.Max(1, (int)Math.Floor((len - span) / pitch) + 1);
            double first = (a + b) / 2 - (n - 1) * pitch / 2;
            for (int i = 0; i < n; i++) o.Add(first + i * pitch);
        }
        return o;
    }

    /// <summary>
    /// The seam at x = <paramref name="x"/>: a line from y = <paramref name="y0"/> to <paramref name="y1"/>
    /// with a tail round each of <paramref name="centres"/>, sticking out towards +x.
    /// </summary>
    public static PathD Seam(JoinStyle s, double x, double y0, double y1, IReadOnlyList<double> centres)
    {
        var p = new PathD { new(x, y0) };
        double L = s.SizeMm;
        foreach (var c in centres.Order())
        {
            switch (s.Kind)
            {
                case JoinKind.Dovetail:
                {
                    double root = L / 2, tip = root + L * Math.Tan(JoinStyle.DovetailAngle * Math.PI / 180);
                    p.Add(new(x, c - root)); p.Add(new(x + L, c - tip)); p.Add(new(x + L, c + tip)); p.Add(new(x, c + root));
                    break;
                }
                case JoinKind.Jigsaw:
                {
                    double rk = L / 2, neck = rk * 0.55, xc = x + L - rk;
                    double reach = Math.Sqrt(Math.Max(0, rk * rk - neck * neck)), delta = Math.Atan2(neck, reach);
                    p.Add(new(x, c - neck));
                    p.Add(new(xc - reach, c - neck));
                    // Round the far side of the head, from the neck's one side to the other.
                    double a0 = -(Math.PI - delta), a1 = Math.PI - delta;
                    int n = 24;
                    for (int i = 1; i < n; i++)
                    {
                        double a = a0 + (a1 - a0) * i / n;
                        p.Add(new(xc + rk * Math.Cos(a), c + rk * Math.Sin(a)));
                    }
                    p.Add(new(xc - reach, c + neck));
                    p.Add(new(x, c + neck));
                    break;
                }
                case JoinKind.Fingers:
                    p.Add(new(x, c - L / 2)); p.Add(new(x + L, c - L / 2)); p.Add(new(x + L, c + L / 2)); p.Add(new(x, c + L / 2));
                    break;
            }
        }
        p.Add(new(x, y1));
        return p;
    }

    /// <summary>Everything on the near side of a seam (x below it), as one area reaching far past the shape.</summary>
    static PathD NearSide(PathD seam, double reach)
    {
        var p = new PathD(seam);
        p.Add(new(seam[^1].x - reach, seam[^1].y));
        p.Add(new(seam[0].x - reach, seam[0].y));
        return p;
    }

    /// <summary>Where the line x = <paramref name="x"/> crosses the shape: the stretches of y inside it.</summary>
    public static List<(double Lo, double Hi)> Crossing(PathsD shape, double x)
    {
        var b = Geo.Bounds(shape);
        var band = Geo.Intersect(shape, [Geo.Rect(x - 0.01, b.top - 1, x + 0.01, b.bottom + 1)]);
        var spans = band.Where(q => Math.Abs(Clipper.Area(q)) > 1e-8).Select(q => { var r = Clipper.GetBounds(q); return (r.top, r.bottom); }).OrderBy(q => q.top).ToList();
        var merged = new List<(double, double)>();
        foreach (var sp in spans)
            if (merged.Count > 0 && sp.top <= merged[^1].Item2 + 1e-6) merged[^1] = (merged[^1].Item1, Math.Max(merged[^1].Item2, sp.bottom));
            else merged.Add(sp);
        return merged;
    }

    /// <summary>
    /// The shape cut at each of <paramref name="seams"/> (x positions, along x when <paramref name="alongX"/>,
    /// else y positions), the tails of each seam kept <paramref name="margin"/> from the shape's edges.
    /// Pieces in order; the play is taken half from each side of every seam. With
    /// <paramref name="keepEnds"/> false the bits before the first seam and after the last are left out
    /// (a closed loop cut at both ends of its flat length).
    /// </summary>
    public static List<PathsD> Split(PathsD shape, bool alongX, IReadOnlyList<double> seams, JoinStyle style, double margin, bool keepEnds = true, IReadOnlyList<(double Lo, double Hi)>? stretches = null)
    {
        // Seams along y: swap x and y, cut, swap back.
        if (!alongX)
        {
            var swapped = Geo.Map(shape, q => new PointD(q.y, q.x));
            return [.. Split(swapped, true, seams, style, margin, keepEnds, stretches).Select(p => Geo.Tidy(Geo.Map(p, q => new PointD(q.y, q.x))))];
        }
        var b = Geo.Bounds(shape);
        double reach = (b.right - b.left) + 4 * style.SizeMm + 100;
        var nears = new List<PathD>();
        foreach (var x in seams.Order())
        {
            var where = stretches ?? Crossing(shape, x);
            var seam = Seam(style, x, b.top - 5, b.bottom + 5, TailCentres(style, where, margin));
            nears.Add(NearSide(seam, reach));
        }
        double half = style.FitMm / 2;
        var pieces = new List<PathsD>();
        for (int i = 0; i <= nears.Count; i++)
        {
            if (!keepEnds && (i == 0 || i == nears.Count)) continue;
            var piece = shape;
            if (i < nears.Count) piece = Geo.Intersect(piece, half > 0 ? Geo.Offset([nears[i]], -half) : [nears[i]]);
            if (i > 0) piece = Geo.Subtract(piece, half > 0 ? Geo.Offset([nears[i - 1]], half) : [nears[i - 1]]);
            if (Geo.Area(piece) > 1e-6) pieces.Add(Geo.Tidy(piece));
        }
        return pieces;
    }

    /// <summary>
    /// Seams (positions along one axis) that cut [<paramref name="lo"/>, <paramref name="hi"/>] into
    /// pieces each no longer than <paramref name="maxLength"/> (tails included), kept out of
    /// <paramref name="keepOut"/> (a living hinge, say). Empty when it fits in one.
    /// </summary>
    public static List<double> Seams(double lo, double hi, double maxLength, JoinStyle style, IReadOnlyList<(double Lo, double Hi)>? keepOut = null)
    {
        double reach = style.Kind == JoinKind.Straight ? 0 : style.SizeMm;
        double room = maxLength - reach - 1;
        if (hi - lo <= maxLength || room <= 1) return [];
        int k = (int)Math.Ceiling((hi - lo) / room);
        var seams = new List<double>();
        for (int i = 1; i < k; i++)
        {
            double at = lo + (hi - lo) * i / k;
            // Out of the way of anything that mustn't be cut through: to whichever side of it is nearer.
            foreach (var (a, b) in keepOut ?? [])
            {
                double ka = a - reach - 2, kb = b + 2;
                if (at > ka && at < kb) at = at - ka < kb - at ? ka : kb;
            }
            seams.Add(at);
        }
        return seams;
    }
}
