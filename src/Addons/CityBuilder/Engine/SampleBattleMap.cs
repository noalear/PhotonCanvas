using PhotonCanvas.Core.Imaging;

namespace PhotonCanvas.Addon.CityBuilder;

/// <summary>
/// A made-up battle map of a riverside village, drawn the way a virtual tabletop's town maps are: grass
/// with a grid over it, cobbled roads meeting at a paved square, a river, houses seen from above (pitched
/// roofs lit on one side, shingles, dark outlines and shadows) and round tree tops. For trying the reader
/// without a picture of your own, and for the tests and the help pictures. Also lists where its houses are.
/// </summary>
public static class SampleBattleMap
{
    public const int SquarePx = 48, Across = 40, Down = 28;

    /// <summary>Each house: its squares (top left, size), roof colour and which way its ridge runs.</summary>
    public static readonly (int X, int Y, int W, int D, (int R, int G, int B) Roof, bool RidgeAcross)[] Houses =
    [
        (3, 9, 5, 3, (176, 82, 56), true),
        (9, 9, 4, 3, (92, 100, 116), true),
        (3, 16, 4, 4, (198, 166, 96), false),
        (16, 8, 6, 4, (176, 82, 56), true),
        (17, 17, 4, 5, (122, 86, 60), false),
        (22, 16, 3, 3, (92, 100, 116), true),
        (8, 16, 3, 5, (176, 82, 56), false),
        (22, 8, 4, 3, (198, 166, 96), true),
        (34, 10, 4, 3, (92, 100, 116), true),
        (34, 16, 5, 4, (176, 82, 56), true),
    ];

    /// <summary>Where the river's middle is (squares across) at a row.</summary>
    static double River(double y) => 29.5 + 1.6 * Math.Sin(y / 4.2);

    public static RasterImage Make()
    {
        int p = SquarePx, w = Across * p, h = Down * p;
        var px = new byte[w * h * 4];
        var rnd = new Random(11);
        // Soft patches of lighter and darker grass.
        double Noise(double x, double y) => 0.5 * Math.Sin(x * 0.021 + 1.3) * Math.Cos(y * 0.017) + 0.3 * Math.Sin((x + y) * 0.043) + 0.2 * Math.Cos(x * 0.09 - y * 0.07);
        var trees = new List<(double X, double Y, double R)>();
        // A wood at the top left, and trees here and there away from the houses and roads.
        for (int i = 0; i < 26; i++) trees.Add((rnd.NextDouble() * 12 * p + p, rnd.NextDouble() * 6 * p + p * 0.6, (0.6 + rnd.NextDouble() * 0.5) * p));
        for (int i = 0; i < 14; i++) trees.Add(((2 + rnd.NextDouble() * 22) * p, (23.5 + rnd.NextDouble() * 3.5) * p, (0.5 + rnd.NextDouble() * 0.4) * p));

        bool Road(int cx, int cy) => cy is 13 or 14 || (cx is 13 or 14 && cy < 26);
        bool Square(int cx, int cy) => cx >= 11 && cx <= 16 && cy >= 12 && cy <= 15;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int cx = x / p, cy = y / p;
                double fx = x / (double)p, fy = y / (double)p;
                double r, g, b;
                double n = Noise(x, y);
                double river = Math.Abs(fx - River(fy));
                if (river < 1.6)
                {
                    double ripple = 0.04 * Math.Sin(x * 0.3 + y * 0.11) + (rnd.NextDouble() - 0.5) * 0.04;
                    r = 0.22 + ripple; g = 0.44 + ripple; b = 0.68 + ripple;
                    if (river > 1.35) { r += 0.15; g += 0.12; b += 0.05; }
                }
                else if (Square(cx, cy))
                {
                    // Flagstones: their own shade each, dark joints.
                    int sx = x / (p / 3), sy = y / (p / 3);
                    double s = 0.72 + ((sx * 73 + sy * 151) % 17) / 17.0 * 0.08 + (rnd.NextDouble() - 0.5) * 0.03;
                    if (x % (p / 3) < 2 || y % (p / 3) < 2) s -= 0.2;
                    r = s; g = s * 0.96; b = s * 0.88;
                }
                else if (Road(cx, cy) && river >= 1.6)
                {
                    // Cobbles: small round stones in staggered rows, dark joints round each.
                    int sx = x / 9, sy = (y + (x / 9 % 2) * 4) / 9;
                    double s = 0.5 + ((sx * 31 + sy * 57) % 11) / 11.0 * 0.1 + (rnd.NextDouble() - 0.5) * 0.04;
                    double ux = x - sx * 9 - 4, uy = y + (x / 9 % 2) * 4 - sy * 9 - 4;
                    if (ux * ux + uy * uy > 3.6 * 3.6) s -= 0.14;
                    r = s * 1.02; g = s; b = s * 0.94;
                }
                else
                {
                    r = 0.42 + 0.05 * n + (rnd.NextDouble() - 0.5) * 0.04;
                    g = 0.6 + 0.06 * n + (rnd.NextDouble() - 0.5) * 0.04;
                    b = 0.3 + 0.03 * n + (rnd.NextDouble() - 0.5) * 0.03;
                }
                Put(px, w, x, y, r, g, b);
            }
        }
        // Shadows down and to the right of the houses and trees, then the houses and trees.
        foreach (var hs in Houses) Shade(px, w, h, (hs.X + 0.15) * p, (hs.Y + 0.15) * p, (hs.X + hs.W + 0.15) * p, (hs.Y + hs.D + 0.15) * p, 0.6);
        foreach (var t in trees) ShadeCircle(px, w, h, t.X + p * 0.15, t.Y + p * 0.15, t.R, 0.65);
        foreach (var hs in Houses) Roof(px, w, hs.X * p, hs.Y * p, hs.W * p, hs.D * p, hs.Roof, hs.RidgeAcross, rnd);
        foreach (var t in trees) Tree(px, w, h, t.X, t.Y, t.R, rnd);
        // The grid.
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (x % p == 0 || y % p == 0)
                {
                    int k = (y * w + x) * 4;
                    px[k] = (byte)(px[k] * 0.72); px[k + 1] = (byte)(px[k + 1] * 0.72); px[k + 2] = (byte)(px[k + 2] * 0.72);
                }
        return new RasterImage(w, h, px);
    }

    static void Put(byte[] px, int w, int x, int y, double r, double g, double b)
    {
        int k = (y * w + x) * 4;
        px[k] = (byte)Math.Clamp(r * 255, 0, 255);
        px[k + 1] = (byte)Math.Clamp(g * 255, 0, 255);
        px[k + 2] = (byte)Math.Clamp(b * 255, 0, 255);
        px[k + 3] = 255;
    }

    static void Shade(byte[] px, int w, int h, double x0, double y0, double x1, double y1, double f)
    {
        for (int y = Math.Max(0, (int)y0); y < Math.Min(h, (int)y1); y++)
            for (int x = Math.Max(0, (int)x0); x < Math.Min(w, (int)x1); x++)
            {
                int k = (y * w + x) * 4;
                px[k] = (byte)(px[k] * f); px[k + 1] = (byte)(px[k + 1] * f); px[k + 2] = (byte)(px[k + 2] * f);
            }
    }

    static void ShadeCircle(byte[] px, int w, int h, double cx, double cy, double r, double f)
    {
        for (int y = Math.Max(0, (int)(cy - r)); y < Math.Min(h, (int)(cy + r)); y++)
            for (int x = Math.Max(0, (int)(cx - r)); x < Math.Min(w, (int)(cx + r)); x++)
                if ((x - cx) * (x - cx) + (y - cy) * (y - cy) <= r * r)
                {
                    int k = (y * w + x) * 4;
                    px[k] = (byte)(px[k] * f); px[k + 1] = (byte)(px[k + 1] * f); px[k + 2] = (byte)(px[k + 2] * f);
                }
    }

    /// <summary>A pitched roof from above: the two slopes lit differently, rows of shingles across them, a ridge line and a dark outline.</summary>
    static void Roof(byte[] px, int w, int x0, int y0, int rw, int rd, (int R, int G, int B) c, bool across, Random rnd)
    {
        for (int y = y0; y < y0 + rd; y++)
            for (int x = x0; x < x0 + rw; x++)
            {
                double u = across ? (y - y0) / (double)rd : (x - x0) / (double)rw;
                // The slope towards the light (top or left) is brighter.
                double light = u < 0.5 ? 1.08 : 0.86;
                double along = across ? y - y0 : x - x0;
                double from = across ? Math.Abs(u - 0.5) * rd : Math.Abs(u - 0.5) * rw;
                double f = light * (((int)(from / 6)) % 2 == 0 ? 1 : 0.92) * (1 + (rnd.NextDouble() - 0.5) * 0.06);
                if (Math.Abs(u - 0.5) * (across ? rd : rw) < 2) f = 0.7;
                bool edge = x - x0 < 3 || y - y0 < 3 || x0 + rw - 1 - x < 3 || y0 + rd - 1 - y < 3;
                if (edge) f = 0.25;
                _ = along;
                Put(px, w, x, y, c.R / 255.0 * f, c.G / 255.0 * f, c.B / 255.0 * f);
            }
    }

    static void Tree(byte[] px, int w, int h, double cx, double cy, double r, Random rnd)
    {
        for (int y = Math.Max(0, (int)(cy - r)); y < Math.Min(h, (int)(cy + r)); y++)
            for (int x = Math.Max(0, (int)(cx - r)); x < Math.Min(w, (int)(cx + r)); x++)
            {
                double d = Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) / r;
                if (d > 1) continue;
                // Lit from the top left, clumps of leaves.
                double lit = 1 - 0.35 * ((x - cx) + (y - cy)) / (2 * r) - 0.25 * d;
                double clump = 0.08 * Math.Sin(x * 0.4) * Math.Cos(y * 0.37) + (rnd.NextDouble() - 0.5) * 0.08;
                double g = (0.36 + clump) * lit;
                Put(px, w, x, y, g * 0.5, g, g * 0.42);
            }
    }
}
