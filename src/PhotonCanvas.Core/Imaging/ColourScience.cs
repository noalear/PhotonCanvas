namespace PhotonCanvas.Core.Imaging;

/// <summary>A colour in CIELAB (D65): L 0–100 lightness, a green(−)/red(+), b blue(−)/yellow(+).</summary>
public readonly record struct Lab(double L, double A, double B)
{
    /// <summary>Saturation-like distance from grey.</summary>
    public double Chroma => Math.Sqrt(A * A + B * B);

    /// <summary>Hue angle, degrees 0–360 (0 = red-ish, 90 = yellow, 180 = green, 270 = blue).</summary>
    public double HueDeg => (Math.Atan2(B, A) * 180 / Math.PI + 360) % 360;
}

/// <summary>
/// The colour maths for measuring and matching marks: sRGB ↔ CIELAB (D65) and the CIEDE2000
/// colour difference (ΔE00: about 1 is the smallest difference most people notice side by
/// side, 2–3 is noticeable, 5+ is clearly a different colour).
/// </summary>
public static class ColourScience
{
    // D65 white point (2° observer), as used by sRGB.
    const double Xn = 0.95047, Yn = 1.0, Zn = 1.08883;

    /// <summary>sRGB channel 0–1 → linear light 0–1.</summary>
    public static double ToLinear(double c) => c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);

    /// <summary>Linear light 0–1 → sRGB channel 0–1.</summary>
    public static double FromLinear(double c) => c <= 0.0031308 ? 12.92 * c : 1.055 * Math.Pow(c, 1 / 2.4) - 0.055;

    /// <summary>Linear-light RGB (0–1 each) → Lab.</summary>
    public static Lab LinearRgbToLab(double r, double g, double b)
    {
        double x = 0.4124564 * r + 0.3575761 * g + 0.1804375 * b;
        double y = 0.2126729 * r + 0.7151522 * g + 0.0721750 * b;
        double z = 0.0193339 * r + 0.1191920 * g + 0.9503041 * b;
        double fx = F(x / Xn), fy = F(y / Yn), fz = F(z / Zn);
        return new(116 * fy - 16, 500 * (fx - fy), 200 * (fy - fz));
    }

    /// <summary>8-bit sRGB → Lab.</summary>
    public static Lab SrgbToLab(byte r, byte g, byte b) =>
        LinearRgbToLab(ToLinear(r / 255.0), ToLinear(g / 255.0), ToLinear(b / 255.0));

    /// <summary>Lab → linear-light RGB (may fall outside 0–1 for colours a screen can't show).</summary>
    public static (double R, double G, double B) LabToLinearRgb(Lab c)
    {
        double fy = (c.L + 16) / 116, fx = fy + c.A / 500, fz = fy - c.B / 200;
        double x = Xn * FInv(fx), y = Yn * FInv(fy), z = Zn * FInv(fz);
        return (3.2404542 * x - 1.5371385 * y - 0.4985314 * z,
                -0.9692660 * x + 1.8760108 * y + 0.0415560 * z,
                0.0556434 * x - 0.2040259 * y + 1.0572252 * z);
    }

    /// <summary>Lab → 8-bit sRGB, clipped to what a screen can show.</summary>
    public static (byte R, byte G, byte B) LabToSrgb(Lab c)
    {
        var (r, g, b) = LabToLinearRgb(c);
        static byte To8(double v) => (byte)Math.Round(Math.Clamp(FromLinear(Math.Clamp(v, 0, 1)), 0, 1) * 255);
        return (To8(r), To8(g), To8(b));
    }

    static double F(double t) => t > 216.0 / 24389 ? Math.Cbrt(t) : (24389.0 / 27 * t + 16) / 116;
    static double FInv(double t) => t * t * t > 216.0 / 24389 ? t * t * t : (116 * t - 16) / (24389.0 / 27);

    /// <summary>CIEDE2000 colour difference (Sharma, Wu and Dalal's formulation; kL = kC = kH = 1).</summary>
    public static double DeltaE2000(Lab c1, Lab c2)
    {
        const double Deg = Math.PI / 180;
        double c1ab = Math.Sqrt(c1.A * c1.A + c1.B * c1.B), c2ab = Math.Sqrt(c2.A * c2.A + c2.B * c2.B);
        double cMean = (c1ab + c2ab) / 2;
        double c7 = Math.Pow(cMean, 7);
        double g = 0.5 * (1 - Math.Sqrt(c7 / (c7 + Math.Pow(25, 7))));
        double a1 = (1 + g) * c1.A, a2 = (1 + g) * c2.A;
        double cp1 = Math.Sqrt(a1 * a1 + c1.B * c1.B), cp2 = Math.Sqrt(a2 * a2 + c2.B * c2.B);
        double h1 = cp1 == 0 ? 0 : (Math.Atan2(c1.B, a1) / Deg + 360) % 360;
        double h2 = cp2 == 0 ? 0 : (Math.Atan2(c2.B, a2) / Deg + 360) % 360;

        double dL = c2.L - c1.L, dC = cp2 - cp1;
        double dh = cp1 * cp2 == 0 ? 0 : h2 - h1 > 180 ? h2 - h1 - 360 : h2 - h1 < -180 ? h2 - h1 + 360 : h2 - h1;
        double dH = 2 * Math.Sqrt(cp1 * cp2) * Math.Sin(dh / 2 * Deg);

        double lMean = (c1.L + c2.L) / 2, cpMean = (cp1 + cp2) / 2;
        double hMean = cp1 * cp2 == 0 ? h1 + h2
            : Math.Abs(h1 - h2) <= 180 ? (h1 + h2) / 2
            : h1 + h2 < 360 ? (h1 + h2 + 360) / 2 : (h1 + h2 - 360) / 2;
        double t = 1 - 0.17 * Math.Cos((hMean - 30) * Deg) + 0.24 * Math.Cos(2 * hMean * Deg)
                 + 0.32 * Math.Cos((3 * hMean + 6) * Deg) - 0.20 * Math.Cos((4 * hMean - 63) * Deg);
        double dTheta = 30 * Math.Exp(-Math.Pow((hMean - 275) / 25, 2));
        double cp7 = Math.Pow(cpMean, 7);
        double rc = 2 * Math.Sqrt(cp7 / (cp7 + Math.Pow(25, 7)));
        double l50 = (lMean - 50) * (lMean - 50);
        double sl = 1 + 0.015 * l50 / Math.Sqrt(20 + l50);
        double sc = 1 + 0.045 * cpMean;
        double sh = 1 + 0.015 * cpMean * t;
        double rt = -Math.Sin(2 * dTheta * Deg) * rc;
        double tl = dL / sl, tc = dC / sc, th = dH / sh;
        return Math.Sqrt(tl * tl + tc * tc + th * th + rt * tc * th);
    }
}
