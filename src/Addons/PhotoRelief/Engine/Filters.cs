namespace PhotonCanvas.Addon.PhotoRelief;

/// <summary>The image filters the relief needs, on plain float grids (row by row, <c>cols</c> across).</summary>
public static class Filters
{
    /// <summary>A Gaussian blur of <paramref name="sigma"/> pixels (three box blurs each way, as good as exact for this).</summary>
    public static float[] Gaussian(float[] src, int cols, int rows, double sigma)
    {
        if (sigma < 0.35) return (float[])src.Clone();
        int r = Math.Max(1, (int)Math.Round((Math.Sqrt(4 * sigma * sigma + 1) - 1) / 2));
        var a = (float[])src.Clone();
        var b = new float[a.Length];
        for (int k = 0; k < 3; k++) { BoxH(a, b, cols, rows, r); (a, b) = (b, a); }
        for (int k = 0; k < 3; k++) { BoxV(a, b, cols, rows, r); (a, b) = (b, a); }
        return a;
    }

    /// <summary>A grid stretched (or shrunk) to another size, smoothly (each new pixel from the four nearest).</summary>
    public static float[] Resample(float[] src, int cols, int rows, int newCols, int newRows)
    {
        var o = new float[newCols * newRows];
        double kx = (double)cols / newCols, ky = (double)rows / newRows;
        Parallel.For(0, newRows, r =>
        {
            double v = Math.Clamp((r + 0.5) * ky - 0.5, 0, rows - 1);
            int y0 = (int)v, y1 = Math.Min(rows - 1, y0 + 1);
            double fy = v - y0;
            for (int c = 0; c < newCols; c++)
            {
                double u = Math.Clamp((c + 0.5) * kx - 0.5, 0, cols - 1);
                int x0 = (int)u, x1 = Math.Min(cols - 1, x0 + 1);
                double fx = u - x0;
                double a = src[y0 * cols + x0] * (1 - fx) + src[y0 * cols + x1] * fx;
                double b = src[y1 * cols + x0] * (1 - fx) + src[y1 * cols + x1] * fx;
                o[r * newCols + c] = (float)(a * (1 - fy) + b * fy);
            }
        });
        return o;
    }

    /// <summary>The mean over a (2r+1)² square round each pixel (the edges repeated outward).</summary>
    public static float[] Box(float[] src, int cols, int rows, int r)
    {
        var a = new float[src.Length];
        var b = new float[src.Length];
        BoxH(src, a, cols, rows, r);
        BoxV(a, b, cols, rows, r);
        return b;
    }

    static void BoxH(float[] src, float[] dst, int cols, int rows, int r)
    {
        float k = 1f / (2 * r + 1);
        Parallel.For(0, rows, y =>
        {
            int o = y * cols;
            double sum = 0;
            for (int i = -r; i <= r; i++) sum += src[o + Math.Clamp(i, 0, cols - 1)];
            for (int x = 0; x < cols; x++)
            {
                dst[o + x] = (float)(sum * k);
                sum += src[o + Math.Min(cols - 1, x + r + 1)] - src[o + Math.Max(0, x - r)];
            }
        });
    }

    static void BoxV(float[] src, float[] dst, int cols, int rows, int r)
    {
        float k = 1f / (2 * r + 1);
        const int Block = 64;
        int blocks = (cols + Block - 1) / Block;
        Parallel.For(0, blocks, bi =>
        {
            int x0 = bi * Block, x1 = Math.Min(cols, x0 + Block), w = x1 - x0;
            var sum = new double[w];
            for (int i = -r; i <= r; i++)
            {
                int row = Math.Clamp(i, 0, rows - 1) * cols;
                for (int x = 0; x < w; x++) sum[x] += src[row + x0 + x];
            }
            for (int y = 0; y < rows; y++)
            {
                int o = y * cols + x0, add = Math.Min(rows - 1, y + r + 1) * cols + x0, take = Math.Max(0, y - r) * cols + x0;
                for (int x = 0; x < w; x++)
                {
                    dst[o + x] = (float)(sum[x] * k);
                    sum[x] += src[add + x] - src[take + x];
                }
            }
        });
    }

    /// <summary>
    /// An edge-keeping smooth (He's guided filter, the picture guiding itself): flat-ish areas
    /// (skin, sky, grain) are smoothed over radius <paramref name="r"/>, while edges with a contrast
    /// well above √<paramref name="eps"/> are kept.
    /// </summary>
    public static float[] Guided(float[] img, int cols, int rows, int r, float eps)
    {
        int n = img.Length;
        var sq = new float[n];
        for (int i = 0; i < n; i++) sq[i] = img[i] * img[i];
        var mean = Box(img, cols, rows, r);
        var meanSq = Box(sq, cols, rows, r);
        var a = new float[n];
        var b = new float[n];
        for (int i = 0; i < n; i++)
        {
            float v = Math.Max(0, meanSq[i] - mean[i] * mean[i]);
            a[i] = v / (v + eps);
            b[i] = mean[i] - a[i] * mean[i];
        }
        var ma = Box(a, cols, rows, r);
        var mb = Box(b, cols, rows, r);
        var o = new float[n];
        for (int i = 0; i < n; i++) o[i] = ma[i] * img[i] + mb[i];
        return o;
    }

    /// <summary>
    /// For each pixel, how far (pixels) it is from the nearest pixel that isn't <paramref name="inside"/>
    /// (0 for those; +∞ everywhere when every pixel is inside). The exact Euclidean distance
    /// (Felzenszwalb and Huttenlocher's two passes).
    /// </summary>
    public static float[] DistanceInside(bool[] inside, int cols, int rows)
    {
        const float Inf = 1e20f;
        int n = cols * rows;
        var f = new float[n];
        bool any = false;
        for (int i = 0; i < n; i++) { f[i] = inside[i] ? Inf : 0; any |= !inside[i]; }
        var o = new float[n];
        if (!any) { Array.Fill(o, float.PositiveInfinity); return o; }
        Parallel.For(0, rows, () => new Line(Math.Max(cols, rows)), (y, _, line) =>
        {
            for (int x = 0; x < cols; x++) line.F[x] = f[y * cols + x];
            line.Run(cols);
            for (int x = 0; x < cols; x++) f[y * cols + x] = line.D[x];
            return line;
        }, _ => { });
        Parallel.For(0, cols, () => new Line(Math.Max(cols, rows)), (x, _, line) =>
        {
            for (int y = 0; y < rows; y++) line.F[y] = f[y * cols + x];
            line.Run(rows);
            for (int y = 0; y < rows; y++) o[y * cols + x] = line.D[y] >= Inf / 2 ? float.PositiveInfinity : MathF.Sqrt(line.D[y]);
            return line;
        }, _ => { });
        return o;
    }

    /// <summary>One row or column of the distance transform, with its working space.</summary>
    sealed class Line(int size)
    {
        public readonly float[] F = new float[size], D = new float[size];
        readonly int[] _v = new int[size];
        readonly double[] _z = new double[size + 1];

        /// <summary>Where the parabolas from q and p cross.</summary>
        double Meet(int q, int p) => ((F[q] + (double)q * q) - (F[p] + (double)p * p)) / (2.0 * q - 2.0 * p);

        public void Run(int n)
        {
            int k = 0;
            _v[0] = 0;
            _z[0] = double.NegativeInfinity;
            _z[1] = double.PositiveInfinity;
            for (int q = 1; q < n; q++)
            {
                double s = Meet(q, _v[k]);
                // z[0] is −∞, so this always stops at k = 0.
                while (s <= _z[k]) { k--; s = Meet(q, _v[k]); }
                k++;
                _v[k] = q;
                _z[k] = s;
                _z[k + 1] = double.PositiveInfinity;
            }
            k = 0;
            for (int q = 0; q < n; q++)
            {
                while (_z[k + 1] < q) k++;
                int p = _v[k];
                D[q] = (float)Math.Min(1e20, (q - p) * (double)(q - p) + F[p]);
            }
        }
    }
}
