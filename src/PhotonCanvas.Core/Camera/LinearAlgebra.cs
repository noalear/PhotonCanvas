namespace PhotonCanvas.Core.Camera;

/// <summary>Small dense least-squares solver (normal equations with Gaussian elimination).</summary>
static class LinearAlgebra
{
    /// <summary>
    /// Least-squares solution of A·x ≈ b, with optional ridge regularisation
    /// <paramref name="ridge"/> on every coefficient. Returns null if singular.
    /// </summary>
    public static double[]? LeastSquares(double[][] a, double[] b, double ridge = 0)
    {
        int n = a[0].Length;
        var m = new double[n, n + 1];
        for (int r = 0; r < a.Length; r++)
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++) m[i, j] += a[r][i] * a[r][j];
                m[i, n] += a[r][i] * b[r];
            }
        for (int i = 0; i < n; i++) m[i, i] += ridge;
        return Solve(m, n);
    }

    /// <summary>Gaussian elimination with partial pivoting on an n×(n+1) augmented matrix.</summary>
    static double[]? Solve(double[,] m, int n)
    {
        for (int c = 0; c < n; c++)
        {
            int p = c;
            for (int r = c + 1; r < n; r++)
                if (Math.Abs(m[r, c]) > Math.Abs(m[p, c])) p = r;
            if (Math.Abs(m[p, c]) < 1e-14) return null;
            if (p != c)
                for (int k = c; k <= n; k++) (m[c, k], m[p, k]) = (m[p, k], m[c, k]);
            for (int r = 0; r < n; r++)
            {
                if (r == c) continue;
                double f = m[r, c] / m[c, c];
                if (f == 0) continue;
                for (int k = c; k <= n; k++) m[r, k] -= f * m[c, k];
            }
        }
        var x = new double[n];
        for (int i = 0; i < n; i++) x[i] = m[i, n] / m[i, i];
        return x;
    }
}
