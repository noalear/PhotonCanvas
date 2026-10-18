namespace PhotonCanvas.Core.Imaging;

/// <summary>How one burned test square came out, measured from a photo.</summary>
/// <param name="Contrast">How much darker than the bare material around it, 0 … 255 (negative = lighter, as on some metals).</param>
/// <param name="Spread">How uneven the inside is (standard deviation of brightness): blotches, stripes, scorching.</param>
public sealed record SquareReading(double Contrast, double Spread, double Inside, double Around)
{
    /// <summary>Visible at all: clearly darker (or lighter) than the material.</summary>
    public bool Visible => Math.Abs(Contrast) >= TestGridReader.VisibleContrast;
}

/// <summary>What "best" means when picking a test square.</summary>
public enum TestGoal
{
    /// <summary>The strongest mark that's still even.</summary>
    DarkestEven,
    /// <summary>The faintest mark that's still clearly visible and even (least heat: least warping, fastest).</summary>
    LightestVisible,
}

/// <summary>
/// Reads a burned material test from a camera photo: each square's brightness against the bare
/// material just around it, and how even it is. No guessing at what looks nice: it measures, then
/// ranks by the chosen goal.
/// </summary>
public static class TestGridReader
{
    public const double VisibleContrast = 18;

    /// <summary>Measure one square (mm on the bed) in a photo covering <paramref name="area"/>.</summary>
    public static SquareReading? Read(RasterImage photo, Rect2 area, double mmPerPx, Rect2 square)
    {
        // Inside: the middle of the square (clear of the edges and of any outline pass).
        double ix = square.Width * 0.2, iy = square.Height * 0.2;
        var inner = new Rect2(square.MinX + ix, square.MinY + iy, square.MaxX - ix, square.MaxY - iy);
        // Around: a ring 1 to 2.5 mm outside the square (the bare material).
        var ring = square.Inflate(2.5);
        var gap = square.Inflate(1);
        var (inMean, inSd, inN) = Stats(photo, area, mmPerPx, inner, null);
        var (outMean, _, outN) = Stats(photo, area, mmPerPx, ring, gap);
        if (inN < 4 || outN < 4) return null;
        return new SquareReading(outMean - inMean, inSd, inMean, outMean);
    }

    /// <summary>Brightness mean and spread of the pixels in <paramref name="r"/> (and not in <paramref name="hole"/>).</summary>
    static (double Mean, double Sd, int N) Stats(RasterImage img, Rect2 area, double mmPerPx, Rect2 r, Rect2? hole)
    {
        int x0 = Math.Max(0, (int)Math.Floor((r.MinX - area.MinX) / mmPerPx)), x1 = Math.Min(img.Width - 1, (int)Math.Ceiling((r.MaxX - area.MinX) / mmPerPx));
        int y0 = Math.Max(0, (int)Math.Floor((r.MinY - area.MinY) / mmPerPx)), y1 = Math.Min(img.Height - 1, (int)Math.Ceiling((r.MaxY - area.MinY) / mmPerPx));
        double sum = 0, sum2 = 0;
        int n = 0;
        var px = img.Rgba;
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                double mx = area.MinX + (x + 0.5) * mmPerPx, my = area.MinY + (y + 0.5) * mmPerPx;
                if (mx < r.MinX || mx > r.MaxX || my < r.MinY || my > r.MaxY) continue;
                if (hole is { } h && mx >= h.MinX && mx <= h.MaxX && my >= h.MinY && my <= h.MaxY) continue;
                int k = (y * img.Width + x) * 4;
                double l = 0.299 * px[k] + 0.587 * px[k + 1] + 0.114 * px[k + 2];
                sum += l; sum2 += l * l; n++;
            }
        if (n == 0) return (0, 0, 0);
        double mean = sum / n;
        return (mean, Math.Sqrt(Math.Max(0, sum2 / n - mean * mean)), n);
    }

    /// <summary>
    /// The best square for the goal (index into <paramref name="readings"/>), or -1 when none is
    /// visible. Uneven squares lose: each unit of spread costs two of contrast.
    /// </summary>
    public static int Best(IReadOnlyList<SquareReading?> readings, TestGoal goal)
    {
        int best = -1;
        double bestScore = double.NegativeInfinity;
        for (int i = 0; i < readings.Count; i++)
        {
            if (readings[i] is not { Visible: true } r) continue;
            double c = Math.Abs(r.Contrast);
            // Too blotchy to use at all: more spread than a third of the contrast.
            if (r.Spread > c / 3 + 4) continue;
            double score = goal == TestGoal.DarkestEven ? c - 2 * r.Spread : -c - 2 * r.Spread;
            if (score > bestScore) { bestScore = score; best = i; }
        }
        return best;
    }
}
