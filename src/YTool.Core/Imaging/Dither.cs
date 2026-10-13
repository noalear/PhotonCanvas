namespace YTool.Core.Imaging;

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
}

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
    public static bool[] Apply(float[] brightness, int width, int height, DitherMethod method)
    {
        if (brightness.Length != width * height) throw new ArgumentException("size mismatch");
        var o = new bool[brightness.Length];
        switch (method)
        {
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
        _ => "Threshold",
    };
}
