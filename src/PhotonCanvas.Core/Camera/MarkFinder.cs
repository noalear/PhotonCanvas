namespace PhotonCanvas.Core.Camera;

/// <summary>An 8-bit greyscale image, row-major.</summary>
public sealed class GrayImage(int width, int height, byte[] pixels)
{
    public int Width { get; } = width;
    public int Height { get; } = height;
    public byte[] Pixels { get; } = pixels;
    public byte this[int x, int y] => Pixels[y * Width + x];
}

/// <summary>Locates a dark burned dot near a predicted position.</summary>
public static class MarkFinder
{
    /// <summary>
    /// Refine <paramref name="guess"/> (pixels) to the centre of the dark mark
    /// within <paramref name="radiusPx"/>. Uses the darkness-weighted centroid
    /// of pixels below an Otsu threshold, re-centring a few times. Returns null
    /// if there is no clear dark mark there.
    /// </summary>
    public static Vec2? Find(GrayImage img, Vec2 guess, double radiusPx)
    {
        var c = guess;
        for (int iter = 0; iter < 6; iter++)
        {
            var next = Centroid(img, c, radiusPx);
            if (next is not { } n) return iter == 0 ? null : c;
            bool settled = n.Dist(c) < 0.25;
            c = n;
            if (settled) break;
        }
        return c;
    }

    static Vec2? Centroid(GrayImage img, Vec2 c, double r)
    {
        int x0 = Math.Max(0, (int)Math.Floor(c.X - r)), x1 = Math.Min(img.Width - 1, (int)Math.Ceiling(c.X + r));
        int y0 = Math.Max(0, (int)Math.Floor(c.Y - r)), y1 = Math.Min(img.Height - 1, (int)Math.Ceiling(c.Y + r));
        if (x1 <= x0 || y1 <= y0) return null;
        var hist = new int[256];
        int count = 0;
        double r2 = r * r;
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                double dx = x - c.X, dy = y - c.Y;
                if (dx * dx + dy * dy > r2) continue;
                hist[img[x, y]]++;
                count++;
            }
        if (count < 16) return null;
        int t = Otsu(hist, count);
        // Need real contrast between mark and surroundings.
        double darkSum = 0, darkN = 0, lightSum = 0, lightN = 0;
        for (int v = 0; v < 256; v++)
        {
            if (v <= t) { darkSum += v * hist[v]; darkN += hist[v]; }
            else { lightSum += v * hist[v]; lightN += hist[v]; }
        }
        if (darkN == 0 || lightN == 0) return null;
        double frac = darkN / count;
        if (lightSum / lightN - darkSum / darkN < 25 || frac < 0.003 || frac > 0.5) return null;

        double sx = 0, sy = 0, sw = 0;
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                double dx = x - c.X, dy = y - c.Y;
                if (dx * dx + dy * dy > r2) continue;
                int v = img[x, y];
                if (v > t) continue;
                double w = t - v + 1;
                sx += w * x;
                sy += w * y;
                sw += w;
            }
        return sw > 0 ? new Vec2(sx / sw, sy / sw) : null;
    }

    static int Otsu(int[] hist, int total)
    {
        double sum = 0;
        for (int i = 0; i < 256; i++) sum += i * hist[i];
        double sumB = 0, best = -1;
        int wB = 0, t = 127;
        for (int i = 0; i < 256; i++)
        {
            wB += hist[i];
            if (wB == 0) continue;
            int wF = total - wB;
            if (wF == 0) break;
            sumB += i * hist[i];
            double mB = sumB / wB, mF = (sum - sumB) / wF;
            double between = (double)wB * wF * (mB - mF) * (mB - mF);
            if (between > best) { best = between; t = i; }
        }
        return t;
    }
}
