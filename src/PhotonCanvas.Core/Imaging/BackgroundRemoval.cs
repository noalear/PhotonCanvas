namespace PhotonCanvas.Core.Imaging;

/// <summary>How to decide what's background.</summary>
public sealed record BackgroundOptions
{
    /// <summary>0–100: how different a colour may be and still count as background.</summary>
    public double Tolerance { get; init; } = 20;
    /// <summary>Remove the background found around the picture's edges.</summary>
    public bool FromEdges { get; init; } = true;
    /// <summary>Extra colours to remove, picked on the picture (pixel coordinates).</summary>
    public IReadOnlyList<(int X, int Y)> Picks { get; init; } = [];
    /// <summary>Picked colours are removed everywhere, not just the area joined to the click.</summary>
    public bool PicksEverywhere { get; init; }
    /// <summary>Pull the kept edge in by this many pixels (removes coloured halos).</summary>
    public int Shrink { get; init; }
    /// <summary>Soften the cut edge over this many pixels.</summary>
    public double Feather { get; init; } = 1;
}

/// <summary>
/// Removes a picture's background without any AI or download: colours found along the edges
/// (and any picked by the user) are flood-filled away within a tolerance, then the edge is
/// shrunk and softened. Best for logos, scans and objects on a plain backdrop.
/// </summary>
public static class BackgroundRemoval
{
    /// <summary>Keep mask, 0 = background … 255 = keep, one byte per pixel.</summary>
    public static byte[] Mask(RasterImage img, BackgroundOptions o)
    {
        int w = img.Width, h = img.Height, n = w * h;
        var px = img.Rgba;
        double tol = Math.Clamp(o.Tolerance, 0, 100) / 100 * 160;
        var removed = new bool[n];
        for (int i = 0; i < n; i++) removed[i] = px[i * 4 + 3] < 8; // already see-through

        if (o.FromEdges)
        {
            var colours = BorderColours(img);
            var near = new bool[n];
            Parallel.For(0, h, y =>
            {
                for (int x = 0; x < w; x++)
                {
                    int p = (y * w + x) * 4;
                    foreach (var c in colours)
                        if (Dist(c, px[p], px[p + 1], px[p + 2]) <= tol) { near[y * w + x] = true; break; }
                }
            });
            var seeds = new List<int>();
            for (int x = 0; x < w; x++) { seeds.Add(x); seeds.Add((h - 1) * w + x); }
            for (int y = 0; y < h; y++) { seeds.Add(y * w); seeds.Add(y * w + w - 1); }
            Flood(seeds, near, removed, w, h);
        }

        foreach (var (sx, sy) in o.Picks)
        {
            if (sx < 0 || sy < 0 || sx >= w || sy >= h) continue;
            var c = Sample(img, sx, sy);
            var near = new bool[n];
            Parallel.For(0, h, y =>
            {
                for (int x = 0; x < w; x++)
                {
                    int p = (y * w + x) * 4;
                    near[y * w + x] = Dist(c, px[p], px[p + 1], px[p + 2]) <= tol;
                }
            });
            if (o.PicksEverywhere)
            {
                for (int i = 0; i < n; i++) removed[i] |= near[i];
            }
            else Flood([sy * w + sx], near, removed, w, h);
        }

        var keep = new byte[n];
        for (int i = 0; i < n; i++) keep[i] = removed[i] ? (byte)0 : (byte)255;
        for (int k = 0; k < o.Shrink; k++) keep = Erode(keep, w, h);
        if (o.Feather > 0.25) keep = Blur(keep, w, h, Math.Max(1, (int)Math.Round(o.Feather)));
        return keep;
    }

    /// <summary>The picture with the mask applied as transparency.</summary>
    public static RasterImage Apply(RasterImage img, byte[] keep)
    {
        var rgba = (byte[])img.Rgba.Clone();
        for (int i = 0; i < keep.Length; i++) rgba[i * 4 + 3] = (byte)Math.Min(rgba[i * 4 + 3], keep[i]);
        return new RasterImage(img.Width, img.Height, rgba);
    }

    /// <summary>Up to three main colours along the border (ignoring ones that make up under 8 % of it).</summary>
    static List<double[]> BorderColours(RasterImage img)
    {
        int w = img.Width, h = img.Height;
        var samples = new List<double[]>();
        void Add(int x, int y)
        {
            int p = (y * w + x) * 4;
            if (img.Rgba[p + 3] >= 8) samples.Add([img.Rgba[p], img.Rgba[p + 1], img.Rgba[p + 2]]);
        }
        int step = Math.Max(1, (w + h) / 800);
        for (int x = 0; x < w; x += step) { Add(x, 0); Add(x, h - 1); }
        for (int y = 0; y < h; y += step) { Add(0, y); Add(w - 1, y); }
        if (samples.Count == 0) return [];
        // k-means, k = 3, seeded from spread-out samples.
        var means = new List<double[]> { samples[0], samples[samples.Count / 3], samples[2 * samples.Count / 3] }.Select(m => (double[])m.Clone()).ToList();
        var label = new int[samples.Count];
        for (int it = 0; it < 12; it++)
        {
            for (int i = 0; i < samples.Count; i++)
                label[i] = Enumerable.Range(0, means.Count).MinBy(k => Dist(means[k], samples[i][0], samples[i][1], samples[i][2]));
            for (int k = 0; k < means.Count; k++)
            {
                var mine = samples.Where((_, i) => label[i] == k).ToList();
                if (mine.Count > 0) means[k] = [mine.Average(s => s[0]), mine.Average(s => s[1]), mine.Average(s => s[2])];
            }
        }
        return means.Where((_, k) => label.Count(l => l == k) >= samples.Count * 0.08).ToList();
    }

    static double[] Sample(RasterImage img, int x, int y)
    {
        double r = 0, g = 0, b = 0;
        int n = 0;
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int xx = Math.Clamp(x + dx, 0, img.Width - 1), yy = Math.Clamp(y + dy, 0, img.Height - 1);
                int p = (yy * img.Width + xx) * 4;
                r += img.Rgba[p]; g += img.Rgba[p + 1]; b += img.Rgba[p + 2]; n++;
            }
        return [r / n, g / n, b / n];
    }

    static void Flood(IEnumerable<int> seeds, bool[] near, bool[] removed, int w, int h)
    {
        var stack = new Stack<int>();
        var seen = new bool[near.Length];
        foreach (var s in seeds)
            if (near[s] && !seen[s]) { seen[s] = true; stack.Push(s); }
        while (stack.Count > 0)
        {
            int i = stack.Pop();
            removed[i] = true;
            int x = i % w, y = i / w;
            void Visit(int j) { if (!seen[j] && near[j]) { seen[j] = true; stack.Push(j); } }
            if (x > 0) Visit(i - 1);
            if (x < w - 1) Visit(i + 1);
            if (y > 0) Visit(i - w);
            if (y < h - 1) Visit(i + w);
        }
    }

    static byte[] Erode(byte[] m, int w, int h)
    {
        var o = new byte[m.Length];
        Parallel.For(0, h, y =>
        {
            for (int x = 0; x < w; x++)
            {
                byte v = m[y * w + x];
                if (x > 0) v = Math.Min(v, m[y * w + x - 1]);
                if (x < w - 1) v = Math.Min(v, m[y * w + x + 1]);
                if (y > 0) v = Math.Min(v, m[(y - 1) * w + x]);
                if (y < h - 1) v = Math.Min(v, m[(y + 1) * w + x]);
                o[y * w + x] = v;
            }
        });
        return o;
    }

    static byte[] Blur(byte[] m, int w, int h, int r)
    {
        var tmp = new float[m.Length];
        var o = new byte[m.Length];
        Parallel.For(0, h, y =>
        {
            for (int x = 0; x < w; x++)
            {
                float s = 0; int c = 0;
                for (int k = -r; k <= r; k++) { int xx = x + k; if (xx < 0 || xx >= w) continue; s += m[y * w + xx]; c++; }
                tmp[y * w + x] = s / c;
            }
        });
        Parallel.For(0, h, y =>
        {
            for (int x = 0; x < w; x++)
            {
                float s = 0; int c = 0;
                for (int k = -r; k <= r; k++) { int yy = y + k; if (yy < 0 || yy >= h) continue; s += tmp[yy * w + x]; c++; }
                o[y * w + x] = (byte)Math.Round(s / c);
            }
        });
        return o;
    }

    /// <summary>"Redmean" colour distance (as the tracer uses), 0 … ~255.</summary>
    static double Dist(double[] m, double r, double g, double b)
    {
        double rm = (m[0] + r) / 2, dr = m[0] - r, dg = m[1] - g, db = m[2] - b;
        return Math.Sqrt((2 + rm / 256) * dr * dr + 4 * dg * dg + (2 + (255 - rm) / 256) * db * db) / 3;
    }
}
