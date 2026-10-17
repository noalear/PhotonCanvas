namespace PhotonCanvas.Core.Imaging;

public enum DitherMethod
{
    /// <summary>Jarvis–Judice–Ninke: smooth, fine detail (the stock software's default).</summary>
    Jarvis,
    FloydSteinberg,
    Stucki,
    /// <summary>Atkinson: higher contrast, keeps highlights clean.</summary>
    Atkinson,
    /// <summary>Ordered 8×8 Bayer pattern: regular texture, no "worms".</summary>
    Bayer,
    /// <summary>Plain cut-off at 50 %: for line art and logos.</summary>
    Threshold,
    /// <summary>Round dots on a screen that grow in dark areas (cell size and angle adjustable).</summary>
    Halftone,
    /// <summary>Coarse newspaper dots: a fixed 45° screen, six dots across a cell. Decorative.</summary>
    Newsprint,
    /// <summary>Parallel lines that thicken in dark areas, like an engraved banknote (cell size and angle adjustable).</summary>
    LineScreen,
    /// <summary>Only the hard edges between light and dark: line drawings, handwriting, a pencil-sketch look.</summary>
    Sketch,
}

/// <summary>Screen settings for <see cref="DitherMethod.Halftone"/> and <see cref="DitherMethod.LineScreen"/>.</summary>
/// <param name="CellPx">Screen cell size in dots.</param>
/// <param name="AngleDeg">Screen angle.</param>
public readonly record struct Screen(double CellPx = 8, double AngleDeg = 45);

/// <summary>Brightness and contrast adjustments applied before dithering.</summary>
/// <param name="Brightness">−1 … +1 (0 = unchanged).</param>
/// <param name="Contrast">−1 … +1 (0 = unchanged).</param>
/// <param name="Gamma">0.2 … 5 (1 = unchanged; &gt;1 brightens mid-tones).</param>
/// <param name="Invert">Swap light and dark (e.g. for dark materials).</param>
public sealed record ToneAdjust(double Brightness = 0, double Contrast = 0, double Gamma = 1, bool Invert = false)
{
    public static readonly ToneAdjust None = new();

    /// <summary>Apply to one brightness value (0 black … 1 white).</summary>
    public double Apply(double v)
    {
        if (Invert) v = 1 - v;
        v = Math.Pow(Math.Clamp(v, 0, 1), 1 / Math.Clamp(Gamma, 0.2, 5));
        double c = Math.Clamp(Contrast, -0.99, 0.99);
        double k = c >= 0 ? 1 / (1 - c) : 1 + c;
        v = (v - 0.5) * k + 0.5 + Math.Clamp(Brightness, -1, 1);
        return Math.Clamp(v, 0, 1);
    }
}

public static class Dither
{
    /// <summary>
    /// Turn brightness (0 black … 1 white, row-major) into burn dots
    /// (<c>true</c> = burn). Error diffusion runs serpentine (alternate rows
    /// reverse), which avoids diagonal streaks.
    /// </summary>
    public static bool[] Apply(float[] brightness, int width, int height, DitherMethod method, Screen screen = default)
    {
        if (brightness.Length != width * height) throw new ArgumentException("size mismatch");
        var o = new bool[brightness.Length];
        if (screen.CellPx <= 0) screen = new Screen(8, 45);
        switch (method)
        {
            case DitherMethod.Halftone or DitherMethod.Newsprint or DitherMethod.LineScreen:
            {
                var sc = method == DitherMethod.Newsprint ? new Screen(6, 45) : screen;
                double rad = sc.AngleDeg * Math.PI / 180, cos = Math.Cos(rad), sin = Math.Sin(rad), cell = Math.Max(2, sc.CellPx);
                bool lines = method == DitherMethod.LineScreen;
                for (int y = 0; y < height; y++)
                    for (int x = 0; x < width; x++)
                    {
                        double u = ((x + 0.5) * cos + (y + 0.5) * sin) / cell, v = (-(x + 0.5) * sin + (y + 0.5) * cos) / cell;
                        double fu = u - Math.Floor(u) - 0.5, fv = v - Math.Floor(v) - 0.5;
                        // Spot function: 1 at the middle of a dot (or line), 0 at its edge; the dark
                        // share of a cell grows with how dark the picture is there.
                        double dark = 1 - brightness[y * width + x];
                        // The share of a cell lying further out than this point: the dark share
                        // burns, so the tone comes out right (lines: straight from the distance).
                        double outer = lines ? 2 * Math.Abs(fv) : DotRank(0.5 + (Math.Cos(2 * Math.PI * fu) + Math.Cos(2 * Math.PI * fv)) / 4);
                        o[y * width + x] = dark > 0.004 && outer < dark;
                    }
                return o;
            }
            case DitherMethod.Sketch:
                return Edges(brightness, width, height);
            case DitherMethod.Threshold:
                for (int i = 0; i < o.Length; i++) o[i] = brightness[i] < 0.5f;
                return o;
            case DitherMethod.Bayer:
                for (int y = 0; y < height; y++)
                    for (int x = 0; x < width; x++)
                        o[y * width + x] = brightness[y * width + x] < (Bayer8[(y & 7) * 8 + (x & 7)] + 0.5f) / 64f;
                return o;
        }
        var (kernel, divisor) = Kernel(method);
        var err = (float[])brightness.Clone();
        for (int y = 0; y < height; y++)
        {
            bool rev = (y & 1) == 1;
            for (int i = 0; i < width; i++)
            {
                int x = rev ? width - 1 - i : i;
                int idx = y * width + x;
                float old = err[idx];
                bool burn = old < 0.5f;
                o[idx] = burn;
                float e = old - (burn ? 0f : 1f);
                if (e == 0) continue;
                foreach (var (dx, dy, w) in kernel)
                {
                    int nx = x + (rev ? -dx : dx), ny = y + dy;
                    if (nx < 0 || nx >= width || ny >= height) continue;
                    err[ny * width + nx] += e * w / divisor;
                }
            }
        }
        return o;
    }

    /// <summary>Round-dot spot values over one cell, sorted: how much of a cell is nearer the middle than a given value.</summary>
    static readonly double[] DotSpots = MakeDotSpots();

    static double[] MakeDotSpots()
    {
        const int N = 128;
        var v = new double[N * N];
        for (int i = 0; i < N; i++)
            for (int j = 0; j < N; j++)
                v[i * N + j] = 0.5 + (Math.Cos(2 * Math.PI * ((i + 0.5) / N - 0.5)) + Math.Cos(2 * Math.PI * ((j + 0.5) / N - 0.5))) / 4;
        Array.Sort(v);
        return v;
    }

    /// <summary>Share of a cell whose spot value is above <paramref name="spot"/> (0 at a dot's middle, 1 at its corners).</summary>
    static double DotRank(double spot)
    {
        int i = Array.BinarySearch(DotSpots, spot);
        if (i < 0) i = ~i;
        return 1 - (double)i / DotSpots.Length;
    }

    /// <summary>Edges: Sobel gradient, thinned to the strongest line across each edge, above a fixed strength.</summary>
    static bool[] Edges(float[] b, int w, int h)
    {
        var mag = new float[b.Length];
        var dir = new byte[b.Length]; // 0 = horizontal gradient, 1 = 45°, 2 = vertical, 3 = 135°
        float At(int x, int y) => b[Math.Clamp(y, 0, h - 1) * w + Math.Clamp(x, 0, w - 1)];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float gx = At(x + 1, y - 1) + 2 * At(x + 1, y) + At(x + 1, y + 1) - At(x - 1, y - 1) - 2 * At(x - 1, y) - At(x - 1, y + 1);
                float gy = At(x - 1, y + 1) + 2 * At(x, y + 1) + At(x + 1, y + 1) - At(x - 1, y - 1) - 2 * At(x, y - 1) - At(x + 1, y - 1);
                int i = y * w + x;
                mag[i] = MathF.Sqrt(gx * gx + gy * gy) / 4; // 0–1 for a full black/white step
                double a = Math.Atan2(gy, gx) * 180 / Math.PI;
                if (a < 0) a += 180;
                dir[i] = (byte)(a < 22.5 || a >= 157.5 ? 0 : a < 67.5 ? 1 : a < 112.5 ? 2 : 3);
            }
        var o = new bool[b.Length];
        const float Strength = 0.12f;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                float m = mag[i];
                if (m < Strength) continue;
                var (dx, dy) = dir[i] switch { 0 => (1, 0), 1 => (1, 1), 2 => (0, 1), _ => (-1, 1) };
                float Mag(int xx, int yy) => xx < 0 || yy < 0 || xx >= w || yy >= h ? 0 : mag[yy * w + xx];
                o[i] = m >= Mag(x + dx, y + dy) && m > Mag(x - dx, y - dy);
            }
        return o;
    }

    static readonly float[] Bayer8 =
    [
        0, 32, 8, 40, 2, 34, 10, 42, 48, 16, 56, 24, 50, 18, 58, 26,
        12, 44, 4, 36, 14, 46, 6, 38, 60, 28, 52, 20, 62, 30, 54, 22,
        3, 35, 11, 43, 1, 33, 9, 41, 51, 19, 59, 27, 49, 17, 57, 25,
        15, 47, 7, 39, 13, 45, 5, 37, 63, 31, 55, 23, 61, 29, 53, 21,
    ];

    static ((int Dx, int Dy, float W)[] Kernel, float Divisor) Kernel(DitherMethod m) => m switch
    {
        DitherMethod.FloydSteinberg => ([(1, 0, 7), (-1, 1, 3), (0, 1, 5), (1, 1, 1)], 16),
        DitherMethod.Stucki => ([(1, 0, 8), (2, 0, 4), (-2, 1, 2), (-1, 1, 4), (0, 1, 8), (1, 1, 4), (2, 1, 2),
            (-2, 2, 1), (-1, 2, 2), (0, 2, 4), (1, 2, 2), (2, 2, 1)], 42),
        // Atkinson spreads only 6/8 of the error, deliberately.
        DitherMethod.Atkinson => ([(1, 0, 1), (2, 0, 1), (-1, 1, 1), (0, 1, 1), (1, 1, 1), (0, 2, 1)], 8),
        _ => ([(1, 0, 7), (2, 0, 5), (-2, 1, 3), (-1, 1, 5), (0, 1, 7), (1, 1, 5), (2, 1, 3),
            (-2, 2, 1), (-1, 2, 3), (0, 2, 5), (1, 2, 3), (2, 2, 1)], 48),
    };

    public static string WireName(DitherMethod m) => m switch
    {
        DitherMethod.Jarvis => "Jarvis",
        DitherMethod.FloydSteinberg => "FloydSteinberg",
        DitherMethod.Stucki => "Stucki",
        DitherMethod.Atkinson => "Atkinson",
        DitherMethod.Bayer => "Bayer",
        // Our own screens: binary dots like a threshold image, which is what the machine is told.
        _ => "Threshold",
    };
}
