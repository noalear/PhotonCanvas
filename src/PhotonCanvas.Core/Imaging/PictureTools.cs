namespace PhotonCanvas.Core.Imaging;

/// <summary>Picture filters and edits that make a new picture (the original stays in the undo history).</summary>
public enum PictureFilter
{
    Greyscale,
    /// <summary>More or less colour (amount −1 … +1).</summary>
    Saturation,
    /// <summary>Pencil sketch: dark lines where the picture changes, white elsewhere.</summary>
    Sketch,
    /// <summary>Embossed relief: lit from the top left.</summary>
    Emboss,
    /// <summary>Comic: few flat tones with dark outlines.</summary>
    Comic,
    /// <summary>Pure black and white at a threshold (amount 0 … 1).</summary>
    BlackAndWhite,
    /// <summary>Mosaic grid: the picture in square tiles (amount = tile size in pixels / 100).</summary>
    Grid,
}

public static class PictureTools
{
    static double Luma(byte r, byte g, byte b) => (0.2126 * r + 0.7152 * g + 0.0722 * b) / 255;

    /// <summary>The part of the picture from pixel (x, y), w × h.</summary>
    public static RasterImage Crop(RasterImage src, int x, int y, int w, int h)
    {
        x = Math.Clamp(x, 0, src.Width - 1);
        y = Math.Clamp(y, 0, src.Height - 1);
        w = Math.Clamp(w, 1, src.Width - x);
        h = Math.Clamp(h, 1, src.Height - y);
        var o = new byte[w * h * 4];
        for (int r = 0; r < h; r++)
            Array.Copy(src.Rgba, ((y + r) * src.Width + x) * 4, o, r * w * 4, w * 4);
        return new RasterImage(w, h, o);
    }

    public static RasterImage Apply(RasterImage src, PictureFilter f, double amount)
    {
        int w = src.Width, h = src.Height;
        var s = src.Rgba;
        var o = (byte[])s.Clone();
        var lum = new double[w * h];
        for (int i = 0; i < w * h; i++) lum[i] = Luma(s[i * 4], s[i * 4 + 1], s[i * 4 + 2]);
        void Grey(int i, double v)
        {
            byte b = (byte)Math.Clamp(Math.Round(v * 255), 0, 255);
            o[i * 4] = o[i * 4 + 1] = o[i * 4 + 2] = b;
        }
        double L(int x, int y) => lum[Math.Clamp(y, 0, h - 1) * w + Math.Clamp(x, 0, w - 1)];
        switch (f)
        {
            case PictureFilter.Greyscale:
                for (int i = 0; i < w * h; i++) Grey(i, lum[i]);
                break;
            case PictureFilter.Saturation:
                double k = 1 + Math.Clamp(amount, -1, 1);
                for (int i = 0; i < w * h; i++)
                    for (int c = 0; c < 3; c++)
                        o[i * 4 + c] = (byte)Math.Clamp(Math.Round(lum[i] * 255 + (s[i * 4 + c] - lum[i] * 255) * k), 0, 255);
                break;
            case PictureFilter.Sketch:
            {
                // Colour dodge of the picture with its blurred negative: a pencil drawing.
                var inv = new float[w * h];
                for (int i = 0; i < inv.Length; i++) inv[i] = (float)(1 - lum[i]);
                var blurred = Blur(inv, w, h, Math.Max(2, (int)Math.Round(Math.Max(w, h) / 150.0 * (0.5 + Math.Clamp(amount, 0, 1)))));
                for (int i = 0; i < w * h; i++)
                    Grey(i, Math.Min(1, lum[i] / Math.Max(1e-3, 1 - blurred[i])));
                break;
            }
            case PictureFilter.Emboss:
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                        Grey(y * w + x, 0.5 + (L(x + 1, y + 1) - L(x - 1, y - 1)) * (1 + 3 * Math.Clamp(amount, 0, 1)));
                break;
            case PictureFilter.Comic:
            {
                int levels = Math.Clamp((int)Math.Round(3 + 5 * Math.Clamp(amount, 0, 1)), 2, 8);
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        int i = y * w + x;
                        double gx = L(x + 1, y) - L(x - 1, y), gy = L(x, y + 1) - L(x, y - 1);
                        bool edge = Math.Sqrt(gx * gx + gy * gy) > 0.18;
                        for (int c = 0; c < 3; c++)
                        {
                            double v = s[i * 4 + c] / 255.0;
                            v = Math.Round(v * (levels - 1)) / (levels - 1);
                            o[i * 4 + c] = edge ? (byte)20 : (byte)Math.Round(v * 255);
                        }
                    }
                break;
            }
            case PictureFilter.BlackAndWhite:
                double t = Math.Clamp(amount, 0.01, 0.99);
                for (int i = 0; i < w * h; i++) Grey(i, lum[i] < t ? 0 : 1);
                break;
            case PictureFilter.Grid:
            {
                int tile = Math.Max(2, (int)Math.Round(Math.Clamp(amount, 0.02, 1) * 100));
                for (int ty = 0; ty < h; ty += tile)
                    for (int tx = 0; tx < w; tx += tile)
                    {
                        double[] sum = new double[4];
                        int n = 0;
                        for (int y = ty; y < Math.Min(h, ty + tile); y++)
                            for (int x = tx; x < Math.Min(w, tx + tile); x++, n++)
                                for (int c = 0; c < 4; c++) sum[c] += s[(y * w + x) * 4 + c];
                        for (int y = ty; y < Math.Min(h, ty + tile); y++)
                            for (int x = tx; x < Math.Min(w, tx + tile); x++)
                            {
                                // A thin gap between tiles makes the grid show.
                                bool gap = x == tx || y == ty;
                                for (int c = 0; c < 3; c++) o[(y * w + x) * 4 + c] = gap ? (byte)255 : (byte)(sum[c] / n);
                                o[(y * w + x) * 4 + 3] = (byte)(sum[3] / n);
                            }
                    }
                break;
            }
        }
        return new RasterImage(w, h, o);
    }

    static float[] Blur(float[] v, int w, int h, int r)
    {
        var copy = (float[])v.Clone();
        // Unsharp mask with amount −1 is a plain blur: reuse its box blur by going the other way.
        var tmp = new float[v.Length];
        for (int pass = 0; pass < 3; pass++)
        {
            Box(copy, tmp, w, h, r, true);
            Box(tmp, copy, w, h, r, false);
        }
        return copy;
    }

    static void Box(float[] src, float[] dst, int w, int h, int r, bool horizontal)
    {
        int lines = horizontal ? h : w, len = horizontal ? w : h;
        for (int l = 0; l < lines; l++)
        {
            double sum = 0;
            int Idx(int k) => horizontal ? l * w + Math.Clamp(k, 0, len - 1) : Math.Clamp(k, 0, len - 1) * w + l;
            for (int k = -r; k <= r; k++) sum += src[Idx(k)];
            for (int k = 0; k < len; k++)
            {
                dst[horizontal ? l * w + k : k * w + l] = (float)(sum / (2 * r + 1));
                sum += src[Idx(k + r + 1)] - src[Idx(k - r)];
            }
        }
    }

    /// <summary>
    /// CMYK separation for screen printing: four pictures (cyan, magenta, yellow, black), each
    /// black where that ink goes (grey for part), white elsewhere. Transparent stays transparent.
    /// </summary>
    public static (string Ink, RasterImage Plate)[] CmykSplit(RasterImage src, ToneAdjust? tone = null)
    {
        int n = src.Width * src.Height;
        var plates = Enumerable.Range(0, 4).Select(_ => new byte[n * 4]).ToArray();
        // The picture as its tone settings show it: brightness, contrast, gamma and invert on each colour.
        Func<double, double> adjust = tone is null || tone == ToneAdjust.None ? v => v : tone.Apply;
        for (int i = 0; i < n; i++)
        {
            double r = adjust(src.Rgba[i * 4] / 255.0), g = adjust(src.Rgba[i * 4 + 1] / 255.0), b = adjust(src.Rgba[i * 4 + 2] / 255.0);
            double k = 1 - Math.Max(r, Math.Max(g, b));
            double c = k >= 1 ? 0 : (1 - r - k) / (1 - k), m = k >= 1 ? 0 : (1 - g - k) / (1 - k), y = k >= 1 ? 0 : (1 - b - k) / (1 - k);
            double[] ink = [c, m, y, k];
            for (int p = 0; p < 4; p++)
            {
                byte v = (byte)Math.Clamp(Math.Round((1 - ink[p]) * 255), 0, 255);
                plates[p][i * 4] = plates[p][i * 4 + 1] = plates[p][i * 4 + 2] = v;
                plates[p][i * 4 + 3] = src.Rgba[i * 4 + 3];
            }
        }
        string[] names = ["Cyan", "Magenta", "Yellow", "Black"];
        return [.. Enumerable.Range(0, 4).Select(p => (names[p], new RasterImage(src.Width, src.Height, plates[p])))];
    }

    /// <summary>
    /// A depth map (light = high) drawn as a lit 3D surface: how the relief will look, lit from
    /// the top left. <paramref name="depthMm"/> sets how steep the light makes it look.
    /// </summary>
    public static RasterImage ShadeRelief(RasterImage depth, double depthMm, double mmPerPixel)
    {
        int w = depth.Width, h = depth.Height;
        var z = new double[w * h];
        for (int i = 0; i < w * h; i++) z[i] = Luma(depth.Rgba[i * 4], depth.Rgba[i * 4 + 1], depth.Rgba[i * 4 + 2]) * depthMm;
        double Z(int x, int y) => z[Math.Clamp(y, 0, h - 1) * w + Math.Clamp(x, 0, w - 1)];
        var o = new byte[w * h * 4];
        double lx = -0.5, ly = -0.5, lz = 0.7, ln = Math.Sqrt(lx * lx + ly * ly + lz * lz);
        lx /= ln; ly /= ln; lz /= ln;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                double dx = (Z(x + 1, y) - Z(x - 1, y)) / (2 * mmPerPixel), dy = (Z(x, y + 1) - Z(x, y - 1)) / (2 * mmPerPixel);
                double nx = -dx, ny = -dy, nz = 1, nn = Math.Sqrt(nx * nx + ny * ny + 1);
                double light = Math.Clamp((nx * lx + ny * ly + nz * lz) / nn, 0, 1);
                byte v = (byte)Math.Round(40 + 205 * light);
                int i = (y * w + x) * 4;
                // A warm metal tone.
                o[i] = (byte)Math.Min(255, v * 1.0);
                o[i + 1] = (byte)Math.Min(255, v * 0.93);
                o[i + 2] = (byte)Math.Min(255, v * 0.82);
                o[i + 3] = 255;
            }
        return new RasterImage(w, h, o);
    }
}
