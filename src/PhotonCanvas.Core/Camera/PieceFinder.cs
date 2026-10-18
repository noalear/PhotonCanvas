namespace PhotonCanvas.Core.Camera;

/// <summary>One piece found on the bed: where its middle is, which way its long side runs, and its size (mm).</summary>
public sealed record FoundPiece(Vec2 Centre, double AngleDeg, double LengthMm, double WidthMm, double AreaMm2);

/// <summary>
/// Finds the separate pieces lying on the bed in a top-down photo: everything that isn't the bed
/// (given as a keep mask), split into joined blobs. Each blob's middle and long axis come from its
/// area (moments), so they hold for any shape, not just rectangles.
/// </summary>
public static class PieceFinder
{
    /// <param name="keep">One byte per pixel: over 127 = not bed.</param>
    /// <param name="area">The part of the bed the photo covers (mm); pixel (0, 0) is its top-left.</param>
    /// <param name="minAreaMm2">Smaller blobs (dust, scraps, glare) are left out.</param>
    public static List<FoundPiece> Find(byte[] keep, int width, int height, Rect2 area, double mmPerPx, double minAreaMm2 = 40)
    {
        var seen = new bool[keep.Length];
        var found = new List<FoundPiece>();
        var stack = new Stack<int>();
        var pixels = new List<int>();
        double pxArea = mmPerPx * mmPerPx;
        for (int start = 0; start < keep.Length; start++)
        {
            if (seen[start] || keep[start] <= 127) continue;
            pixels.Clear();
            stack.Push(start);
            seen[start] = true;
            while (stack.Count > 0)
            {
                int i = stack.Pop();
                pixels.Add(i);
                int x = i % width, y = i / width;
                if (x > 0) Visit(i - 1);
                if (x + 1 < width) Visit(i + 1);
                if (y > 0) Visit(i - width);
                if (y + 1 < height) Visit(i + width);
            }
            if (pixels.Count * pxArea < minAreaMm2) continue;
            // Centre and second moments (pixel centres).
            double sx = 0, sy = 0;
            foreach (var i in pixels) { sx += i % width + 0.5; sy += i / width + 0.5; }
            double cx = sx / pixels.Count, cy = sy / pixels.Count;
            double mxx = 0, myy = 0, mxy = 0;
            foreach (var i in pixels)
            {
                double dx = i % width + 0.5 - cx, dy = i / width + 0.5 - cy;
                mxx += dx * dx; myy += dy * dy; mxy += dx * dy;
            }
            double angle = 0.5 * Math.Atan2(2 * mxy, mxx - myy);
            // Extent along the long axis and across it.
            double ux = Math.Cos(angle), uy = Math.Sin(angle);
            double lo = double.MaxValue, hi = double.MinValue, lo2 = double.MaxValue, hi2 = double.MinValue;
            foreach (var i in pixels)
            {
                double dx = i % width + 0.5 - cx, dy = i / width + 0.5 - cy;
                double a = dx * ux + dy * uy, b = -dx * uy + dy * ux;
                lo = Math.Min(lo, a); hi = Math.Max(hi, a); lo2 = Math.Min(lo2, b); hi2 = Math.Max(hi2, b);
            }
            found.Add(new FoundPiece(
                new Vec2(area.MinX + cx * mmPerPx, area.MinY + cy * mmPerPx),
                angle * 180 / Math.PI,
                (hi - lo + 1) * mmPerPx, (hi2 - lo2 + 1) * mmPerPx,
                pixels.Count * pxArea));
        }
        // Reading order: back to front in rows, then left to right (how numbering usually goes).
        double row = found.Count == 0 ? 1 : Math.Max(5, found.Average(p => p.WidthMm) / 2);
        return [.. found.OrderBy(p => Math.Round(p.Centre.Y / row)).ThenBy(p => p.Centre.X)];

        void Visit(int j)
        {
            if (seen[j] || keep[j] <= 127) return;
            seen[j] = true;
            stack.Push(j);
        }
    }

    /// <summary>
    /// The turn (degrees) taking a design whose long side runs at <paramref name="designDeg"/> onto
    /// a piece whose long side runs at <paramref name="pieceDeg"/>: the smaller of the two ways (a long
    /// side has no front or back).
    /// </summary>
    public static double TurnOnto(double designDeg, double pieceDeg)
    {
        double t = pieceDeg - designDeg;
        t = ((t % 180) + 180) % 180;
        return t > 90 ? t - 180 : t;
    }

    /// <summary>Which way the long side of a set of outline points runs (degrees), from their spread.</summary>
    public static double LongAxisDeg(IEnumerable<Vec2> points)
    {
        var pts = points.ToList();
        if (pts.Count < 2) return 0;
        double cx = pts.Average(p => p.X), cy = pts.Average(p => p.Y), mxx = 0, myy = 0, mxy = 0;
        foreach (var p in pts) { double dx = p.X - cx, dy = p.Y - cy; mxx += dx * dx; myy += dy * dy; mxy += dx * dy; }
        return 0.5 * Math.Atan2(2 * mxy, mxx - myy) * 180 / Math.PI;
    }
}
