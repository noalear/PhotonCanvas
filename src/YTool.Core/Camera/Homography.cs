namespace YTool.Core.Camera;

/// <summary>A projective (perspective) map of the plane: what a flat camera sees of a flat bed.</summary>
public readonly record struct Homography(double H11, double H12, double H13, double H21, double H22, double H23, double H31, double H32)
{
    public Vec2 Apply(Vec2 p)
    {
        double w = H31 * p.X + H32 * p.Y + 1;
        return new((H11 * p.X + H12 * p.Y + H13) / w, (H21 * p.X + H22 * p.Y + H23) / w);
    }

    /// <summary>
    /// Least-squares fit from ≥ 4 correspondences (direct linear transform with
    /// Hartley normalisation for numerical stability). Null when degenerate,
    /// e.g. three of four points on one line.
    /// </summary>
    public static Homography? Fit(IReadOnlyList<(Vec2 From, Vec2 To)> pairs)
    {
        if (pairs.Count < 4) return null;
        var (tf, nf) = Normaliser(pairs.Select(p => p.From));
        var (tt, nt) = Normaliser(pairs.Select(p => p.To));
        var rows = new List<double[]>();
        var rhs = new List<double>();
        foreach (var (f0, t0) in pairs)
        {
            var f = nf(f0);
            var t = nt(t0);
            rows.Add([f.X, f.Y, 1, 0, 0, 0, -t.X * f.X, -t.X * f.Y]);
            rhs.Add(t.X);
            rows.Add([0, 0, 0, f.X, f.Y, 1, -t.Y * f.X, -t.Y * f.Y]);
            rhs.Add(t.Y);
        }
        var h = LinearAlgebra.LeastSquares([.. rows], [.. rhs]);
        if (h is null || h.Any(v => !double.IsFinite(v))) return null;
        // Undo the normalisations: H = Tt⁻¹ · Hn · Tf.
        var hn = new double[3, 3] { { h[0], h[1], h[2] }, { h[3], h[4], h[5] }, { h[6], h[7], 1 } };
        var full = Mul(Mul(Inverse(tt), hn), tf);
        double s = full[2, 2];
        if (Math.Abs(s) < 1e-15) return null;
        var r = new Homography(full[0, 0] / s, full[0, 1] / s, full[0, 2] / s, full[1, 0] / s, full[1, 1] / s, full[1, 2] / s, full[2, 0] / s, full[2, 1] / s);
        // Reject maps that fold the plane over within the fitted points.
        foreach (var (f, _) in pairs)
            if (r.H31 * f.X + r.H32 * f.Y + 1 <= 1e-9) return null;
        return r;
    }

    static (double[,] T, Func<Vec2, Vec2> Map) Normaliser(IEnumerable<Vec2> pts)
    {
        var list = pts.ToList();
        double cx = list.Average(p => p.X), cy = list.Average(p => p.Y);
        double d = list.Average(p => Math.Sqrt((p.X - cx) * (p.X - cx) + (p.Y - cy) * (p.Y - cy)));
        double s = d > 1e-12 ? Math.Sqrt(2) / d : 1;
        return (new double[3, 3] { { s, 0, -s * cx }, { 0, s, -s * cy }, { 0, 0, 1 } }, p => new((p.X - cx) * s, (p.Y - cy) * s));
    }

    static double[,] Mul(double[,] a, double[,] b)
    {
        var r = new double[3, 3];
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
                for (int k = 0; k < 3; k++) r[i, j] += a[i, k] * b[k, j];
        return r;
    }

    /// <summary>Inverse of a similarity normaliser [s 0 tx; 0 s ty; 0 0 1].</summary>
    static double[,] Inverse(double[,] t)
    {
        double s = t[0, 0];
        return new double[3, 3] { { 1 / s, 0, -t[0, 2] / s }, { 0, 1 / s, -t[1, 2] / s }, { 0, 0, 1 } };
    }
}
