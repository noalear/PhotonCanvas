namespace PhotonCanvas.Addon.Terrain;

/// <summary>
/// Smooth random noise for textures (gradient noise, as Perlin's): the same seed always gives the same
/// field, so a texture made twice with the same variation comes out the same. Values are about −1 to 1.
/// </summary>
internal sealed class Noise
{
    readonly int[] _perm = new int[512];
    readonly double[] _gx = new double[256], _gy = new double[256];

    public Noise(int seed)
    {
        var rnd = new Random(seed * 7919 + 17);
        var p = Enumerable.Range(0, 256).ToArray();
        for (int i = 255; i > 0; i--)
        {
            int j = rnd.Next(i + 1);
            (p[i], p[j]) = (p[j], p[i]);
        }
        for (int i = 0; i < 512; i++) _perm[i] = p[i & 255];
        for (int i = 0; i < 256; i++)
        {
            double a = rnd.NextDouble() * Math.PI * 2;
            _gx[i] = Math.Cos(a);
            _gy[i] = Math.Sin(a);
        }
    }

    static double Fade(double t) => t * t * t * (t * (t * 6 - 15) + 10);

    double Corner(int ix, int iy, double dx, double dy)
    {
        int h = _perm[(_perm[ix & 255] + iy) & 511] & 255;
        return _gx[h] * dx + _gy[h] * dy;
    }

    /// <summary>Gradient noise at (x, y): features about one unit across.</summary>
    public double At(double x, double y)
    {
        double fx = Math.Floor(x), fy = Math.Floor(y);
        int ix = (int)fx, iy = (int)fy;
        double dx = x - fx, dy = y - fy;
        double u = Fade(dx), v = Fade(dy);
        double a = Corner(ix, iy, dx, dy), b = Corner(ix + 1, iy, dx - 1, dy);
        double c = Corner(ix, iy + 1, dx, dy - 1), d = Corner(ix + 1, iy + 1, dx - 1, dy - 1);
        double ab = a + (b - a) * u, cd = c + (d - c) * u;
        // The raw range is about ±0.7: stretched to about ±1.
        return (ab + (cd - ab) * v) * 1.41;
    }

    /// <summary>Layers of noise, each twice as fine and half as strong (fractal noise): features <paramref name="size"/> across with rougher detail on them.</summary>
    public double Fbm(double x, double y, double size, int octaves = 4)
    {
        double sum = 0, amp = 1, norm = 0, f = 1 / Math.Max(1e-6, size);
        for (int o = 0; o < octaves; o++)
        {
            sum += amp * At(x * f + o * 31.7, y * f - o * 17.3);
            norm += amp;
            amp *= 0.5;
            f *= 2.03;
        }
        return sum / norm;
    }
}
