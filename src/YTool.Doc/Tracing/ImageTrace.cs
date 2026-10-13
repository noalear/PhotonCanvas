using YTool.Core;
using YTool.Core.Imaging;

namespace YTool.Doc.Tracing;

/// <summary>One colour found in a picture.</summary>
/// <param name="Rgb">Average colour, 0xRRGGBB.</param>
/// <param name="Pixels">How many pixels belong to it.</param>
/// <param name="IsBackground">Most common colour along the picture's edge.</param>
public sealed record PaletteEntry(uint Rgb, int Pixels, bool IsBackground);

/// <summary>Traced outlines for one colour (or the single black & white result).</summary>
/// <param name="Rgb">Source colour, or null in black & white mode.</param>
/// <param name="Geometry">Outlines in the original image's pixel coordinates.</param>
public sealed record TracePart(uint? Rgb, PathGeometry Geometry);

public sealed record TraceResult(IReadOnlyList<TracePart> Parts)
{
    public int Paths => Parts.Sum(p => p.Geometry.Subpaths.Length);
    public int Nodes => Parts.Sum(p => p.Geometry.Subpaths.Sum(s => s.Segs.Length));
}

/// <summary>Runs a trace on an imported picture, in black & white or by colour.</summary>
public static class ImageTrace
{
    /// <summary>Longest side traced; bigger pictures are averaged down first (fast, and plenty for a laser).</summary>
    public const int WorkingSide = 2000;

    public sealed class Working
    {
        public required RasterImage Image { get; init; }
        /// <summary>Original pixels per working pixel.</summary>
        public required double Scale { get; init; }
        public required float[] Brightness { get; init; }
    }

    public static Working Prepare(RasterImage src)
    {
        var img = src;
        double scale = 1;
        int longSide = Math.Max(src.Width, src.Height);
        if (longSide > WorkingSide)
        {
            scale = (double)longSide / WorkingSide;
            img = Downscale(src, Math.Max(1, (int)Math.Round(src.Width / scale)), Math.Max(1, (int)Math.Round(src.Height / scale)));
            scale = (double)src.Width / img.Width;
        }
        return new Working { Image = img, Scale = scale, Brightness = img.Luminance() };
    }

    public static TraceResult BlackAndWhite(Working w, TraceOptions o)
    {
        var g = Tracer.TraceBrightness(w.Brightness, w.Image.Width, w.Image.Height, o);
        return new([new TracePart(null, ToOriginal(g, w.Scale))]);
    }

    /// <summary>Group the picture's colours. Colours closer than <paramref name="tolerance"/> (0–100) merge.</summary>
    public static (List<PaletteEntry> Palette, int[] Labels) Palette(Working w, double tolerance, int maxColours = 12)
    {
        var img = w.Image;
        int n = img.Width * img.Height;
        // Tolerance as a distance in RGB (0–441); a perceptual weighting keeps greens and blues apart.
        double tol = Math.Clamp(tolerance, 1, 100) / 100 * 160;
        var means = new List<double[]>();   // r, g, b, count
        var labels = new int[n];
        int step = Math.Max(1, n / 200_000);
        for (int i = 0; i < n; i += step)
        {
            int p = i * 4;
            if (img.Rgba[p + 3] < 128) continue;
            double r = img.Rgba[p], g = img.Rgba[p + 1], b = img.Rgba[p + 2];
            int best = Nearest(means, r, g, b, out double d);
            if (best >= 0 && d <= tol)
            {
                var m = means[best];
                m[3]++;
                m[0] += (r - m[0]) / m[3];
                m[1] += (g - m[1]) / m[3];
                m[2] += (b - m[2]) / m[3];
            }
            else if (means.Count < 256) means.Add([r, g, b, 1]);
        }
        // Merge groups that drifted within tolerance of each other.
        for (bool merged = true; merged;)
        {
            merged = false;
            for (int a = 0; a < means.Count && !merged; a++)
                for (int b = a + 1; b < means.Count && !merged; b++)
                    if (Dist(means[a], means[b][0], means[b][1], means[b][2]) <= tol)
                    {
                        double ca = means[a][3], cb = means[b][3];
                        for (int k = 0; k < 3; k++) means[a][k] = (means[a][k] * ca + means[b][k] * cb) / (ca + cb);
                        means[a][3] = ca + cb;
                        means.RemoveAt(b);
                        merged = true;
                    }
        }
        means = means.OrderByDescending(m => m[3]).Take(Math.Max(1, maxColours)).ToList();
        // Colours covering under 0.5 % are mostly soft-edge fringes; their pixels join the nearest real colour.
        double sampled = means.Sum(m => m[3]);
        means = means.Where((m, idx) => idx == 0 || m[3] / sampled >= 0.005).ToList();

        var counts = new int[means.Count];
        var border = new int[means.Count];
        for (int i = 0; i < n; i++)
        {
            int p = i * 4;
            if (img.Rgba[p + 3] < 128) { labels[i] = -1; continue; }
            int k = Nearest(means, img.Rgba[p], img.Rgba[p + 1], img.Rgba[p + 2], out _);
            labels[i] = k;
            counts[k]++;
            int x = i % img.Width, y = i / img.Width;
            if (x == 0 || y == 0 || x == img.Width - 1 || y == img.Height - 1) border[k]++;
        }
        CleanThinFringes(labels, img.Width, img.Height, counts);
        int bg = border.Length == 0 ? -1 : Array.IndexOf(border, border.Max());
        var palette = means.Select((m, k) => new PaletteEntry(
            (uint)Math.Round(m[0]) << 16 | (uint)Math.Round(m[1]) << 8 | (uint)Math.Round(m[2]), counts[k], k == bg)).ToList();
        return (palette, labels);
    }

    public static TraceResult ByColour(Working w, IReadOnlyList<PaletteEntry> palette, int[] labels, IReadOnlySet<int> include, TraceOptions o)
    {
        var parts = new List<TracePart>();
        foreach (int k in include.Order())
        {
            if (k < 0 || k >= palette.Count) continue;
            var mask = new bool[labels.Length];
            for (int i = 0; i < mask.Length; i++) mask[i] = labels[i] == k;
            var g = Tracer.TraceMask(mask, w.Image.Width, w.Image.Height, o);
            if (!g.IsEmpty) parts.Add(new TracePart(palette[k].Rgb, ToOriginal(g, w.Scale)));
        }
        return new(parts);
    }

    /// <summary>
    /// Soft edges between two colours produce in-between pixels that can land
    /// on a third colour as one-pixel slivers. A pixel whose colour hardly
    /// appears around it takes the most common colour of its 3 × 3 neighbourhood.
    /// </summary>
    static void CleanThinFringes(int[] labels, int w, int h, int[] counts)
    {
        var src = (int[])labels.Clone();
        Span<int> seen = stackalloc int[9];
        Span<int> tally = stackalloc int[9];
        for (int y = 1; y < h - 1; y++)
            for (int x = 1; x < w - 1; x++)
            {
                int i = y * w + x, own = src[i], distinct = 0, ownCount = 0;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int l = src[i + dy * w + dx];
                        if (l == own) ownCount++;
                        int k = 0;
                        while (k < distinct && seen[k] != l) k++;
                        if (k == distinct) { seen[k] = l; tally[k] = 0; distinct++; }
                        tally[k]++;
                    }
                if (ownCount > 2 || own < 0) continue;
                int best = 0;
                for (int k = 1; k < distinct; k++) if (tally[k] > tally[best]) best = k;
                if (seen[best] < 0 || seen[best] == own) continue;
                labels[i] = seen[best];
                counts[own]--;
                counts[seen[best]]++;
            }
    }

    /// <summary>Average colour around a working-image pixel (3 × 3), 0xRRGGBB.</summary>
    public static uint SampleColour(Working w, int x, int y)
    {
        var img = w.Image;
        double r = 0, g = 0, b = 0;
        int n = 0;
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int xx = Math.Clamp(x + dx, 0, img.Width - 1), yy = Math.Clamp(y + dy, 0, img.Height - 1);
                int p = (yy * img.Width + xx) * 4;
                r += img.Rgba[p]; g += img.Rgba[p + 1]; b += img.Rgba[p + 2];
                n++;
            }
        return (uint)Math.Round(r / n) << 16 | (uint)Math.Round(g / n) << 8 | (uint)Math.Round(b / n);
    }

    /// <summary>
    /// "Magic wand": trace the pixels within <paramref name="tolerance"/> (0–100)
    /// of <paramref name="colour"/>. With <paramref name="connected"/>, only the
    /// region joined to the clicked pixel (<paramref name="seedX"/>, <paramref name="seedY"/>, working pixels).
    /// </summary>
    public static TraceResult PickColour(Working w, uint colour, int seedX, int seedY, double tolerance, bool connected, TraceOptions o)
    {
        var img = w.Image;
        int width = img.Width, height = img.Height, n = width * height;
        double tol = Math.Clamp(tolerance, 0, 100) / 100 * 160;
        double[] c = [colour >> 16 & 0xFF, colour >> 8 & 0xFF, colour & 0xFF, 1];
        var within = new bool[n];
        Parallel.For(0, height, y =>
        {
            for (int x = 0; x < width; x++)
            {
                int i = y * width + x, p = i * 4;
                within[i] = img.Rgba[p + 3] >= 128 && Dist(c, img.Rgba[p], img.Rgba[p + 1], img.Rgba[p + 2]) <= tol;
            }
        });
        bool[] mask = within;
        if (connected)
        {
            mask = new bool[n];
            int seed = Math.Clamp(seedY, 0, height - 1) * width + Math.Clamp(seedX, 0, width - 1);
            if (within[seed])
            {
                var stack = new Stack<int>();
                stack.Push(seed);
                mask[seed] = true;
                while (stack.Count > 0)
                {
                    int i = stack.Pop(), x = i % width, y = i / width;
                    void Visit(int j) { if (within[j] && !mask[j]) { mask[j] = true; stack.Push(j); } }
                    if (x > 0) Visit(i - 1);
                    if (x < width - 1) Visit(i + 1);
                    if (y > 0) Visit(i - width);
                    if (y < height - 1) Visit(i + width);
                }
            }
        }
        var g = Tracer.TraceMask(mask, width, height, o);
        return new(g.IsEmpty ? [] : [new TracePart(colour, ToOriginal(g, w.Scale))]);
    }

    static PathGeometry ToOriginal(PathGeometry g, double scale) =>
        Math.Abs(scale - 1) < 1e-12 ? g : g.Transform(Affine.Scale(scale, scale));

    static int Nearest(List<double[]> means, double r, double g, double b, out double best)
    {
        int idx = -1;
        best = double.PositiveInfinity;
        for (int k = 0; k < means.Count; k++)
        {
            double d = Dist(means[k], r, g, b);
            if (d < best) { best = d; idx = k; }
        }
        return idx;
    }

    /// <summary>"Redmean" weighted RGB distance: cheap and close to how different colours look.</summary>
    static double Dist(double[] m, double r, double g, double b)
    {
        double rm = (m[0] + r) / 2, dr = m[0] - r, dg = m[1] - g, db = m[2] - b;
        return Math.Sqrt((2 + rm / 256) * dr * dr + 4 * dg * dg + (2 + (255 - rm) / 256) * db * db) / 3;
    }

    /// <summary>Area-average downscale (no aliasing on fine detail).</summary>
    static RasterImage Downscale(RasterImage s, int w, int h)
    {
        var o = new byte[w * h * 4];
        double sx = (double)s.Width / w, sy = (double)s.Height / h;
        Parallel.For(0, h, y =>
        {
            int y0 = (int)(y * sy), y1 = Math.Max(y0 + 1, Math.Min(s.Height, (int)((y + 1) * sy)));
            for (int x = 0; x < w; x++)
            {
                int x0 = (int)(x * sx), x1 = Math.Max(x0 + 1, Math.Min(s.Width, (int)((x + 1) * sx)));
                double r = 0, g = 0, b = 0, a = 0;
                int cnt = 0;
                for (int yy = y0; yy < y1; yy++)
                    for (int xx = x0; xx < x1; xx++)
                    {
                        int p = (yy * s.Width + xx) * 4;
                        r += s.Rgba[p]; g += s.Rgba[p + 1]; b += s.Rgba[p + 2]; a += s.Rgba[p + 3];
                        cnt++;
                    }
                int q = (y * w + x) * 4;
                o[q] = (byte)(r / cnt); o[q + 1] = (byte)(g / cnt); o[q + 2] = (byte)(b / cnt); o[q + 3] = (byte)(a / cnt);
            }
        });
        return new RasterImage(w, h, o);
    }
}
