using PhotonCanvas.Core.Imaging;

namespace PhotonCanvas.Addon.PhotoRelief;

/// <summary>
/// Finds the subject of a photo taken against a plain background. The background's colours are
/// read from the top and the upper sides only (a portrait's shoulders and a pet's body usually run
/// off the bottom, so the bottom edge would count the subject as background); then everything of
/// those colours joined to an edge is background. Specks are dropped and the outline smoothed.
/// </summary>
public static class SubjectMask
{
    /// <summary>The subject 0 (background) … 1, and whether the background looked plain enough to go by.</summary>
    /// <param name="picks">Background colours clicked by the person, and where on <paramref name="img"/> (which may be off it).</param>
    public static (float[] Mask, bool Plain) Find(RasterImage img, double tolerance, double mmPerPx,
        IReadOnlyList<((double R, double G, double B) Colour, int X, int Y)>? picks = null)
    {
        int w = img.Width, h = img.Height, n = w * h;
        var px = img.Rgba;
        var refs = BackgroundColours(img, out double plainShare);
        picks ??= [];
        refs.AddRange(picks.Select(p => p.Colour));
        // Picked colours mean the person has said what the background is.
        if (picks.Count > 0) plainShare = 1;
        double tol = Math.Clamp(tolerance, 0, 100) / 100 * 160;

        // Each colour a little smoothed first, so JPEG noise and grain don't break the background up.
        var ch = new float[3][];
        for (int k = 0; k < 3; k++)
        {
            var c = new float[n];
            for (int i = 0; i < n; i++) c[i] = px[i * 4 + k];
            ch[k] = Filters.Box(c, w, h, 1);
        }
        var near = new bool[n];
        Parallel.For(0, h, y =>
        {
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                if (px[i * 4 + 3] < 8) { near[i] = true; continue; }
                foreach (var c in refs)
                    if (Dist(c, ch[0][i], ch[1][i], ch[2][i]) <= tol) { near[i] = true; break; }
            }
        });

        // Background: those colours, joined to any edge.
        var back = new bool[n];
        var stack = new Stack<int>();
        void Seed(int i) { if (near[i] && !back[i]) { back[i] = true; stack.Push(i); } }
        for (int x = 0; x < w; x++) { Seed(x); Seed((h - 1) * w + x); }
        for (int y = 0; y < h; y++) { Seed(y * w); Seed(y * w + w - 1); }
        foreach (var p in picks)
            if (p.X >= 0 && p.Y >= 0 && p.X < w && p.Y < h) Seed(p.Y * w + p.X);
        while (stack.Count > 0)
        {
            int i = stack.Pop(), x = i % w, y = i / w;
            if (x > 0) Seed(i - 1);
            if (x < w - 1) Seed(i + 1);
            if (y > 0) Seed(i - w);
            if (y < h - 1) Seed(i + w);
        }

        // Specks of subject (under 0.3 % of the picture) are background too.
        DropSpecks(back, w, h, Math.Max(4, (int)(n * 0.003)));

        // A smooth outline: blurred a little and cut at half way, then softened over a pixel.
        var m = new float[n];
        for (int i = 0; i < n; i++) m[i] = back[i] ? 0 : 1;
        m = Filters.Gaussian(m, w, h, Math.Max(1, 0.4 / mmPerPx));
        for (int i = 0; i < n; i++) m[i] = m[i] >= 0.5f ? 1 : 0;
        m = Filters.Gaussian(m, w, h, 0.7);
        return (m, plainShare >= 0.6);
    }

    /// <summary>Kept areas smaller than <paramref name="min"/> pixels become background.</summary>
    static void DropSpecks(bool[] back, int w, int h, int min)
    {
        int n = w * h;
        var seen = new bool[n];
        var stack = new Stack<int>();
        var part = new List<int>();
        for (int s = 0; s < n; s++)
        {
            if (back[s] || seen[s]) continue;
            part.Clear();
            seen[s] = true;
            stack.Push(s);
            while (stack.Count > 0)
            {
                int i = stack.Pop(), x = i % w, y = i / w;
                part.Add(i);
                void Visit(int j) { if (!back[j] && !seen[j]) { seen[j] = true; stack.Push(j); } }
                if (x > 0) Visit(i - 1);
                if (x < w - 1) Visit(i + 1);
                if (y > 0) Visit(i - w);
                if (y < h - 1) Visit(i + w);
            }
            if (part.Count < min) foreach (int i in part) back[i] = true;
        }
    }

    /// <summary>
    /// Up to three main colours along the top and the upper two thirds of the sides, and how much of
    /// that edge they make up (a plain background is nearly all of it).
    /// </summary>
    public static List<(double R, double G, double B)> BackgroundColours(RasterImage img, out double share)
    {
        int w = img.Width, h = img.Height;
        var px = img.Rgba;
        var samples = new List<int>();
        int band = Math.Max(1, h / 100);
        for (int y = 0; y < band; y++) for (int x = 0; x < w; x++) samples.Add(y * w + x);
        for (int y = band; y < h * 2 / 3; y++) { samples.Add(y * w); samples.Add(y * w + w - 1); }
        var bins = new Dictionary<int, (double R, double G, double B, int N)>();
        int opaque = 0;
        foreach (int i in samples)
        {
            int p = i * 4;
            if (px[p + 3] < 8) continue;
            opaque++;
            int key = (px[p] >> 5) << 6 | (px[p + 1] >> 5) << 3 | (px[p + 2] >> 5);
            bins.TryGetValue(key, out var b);
            bins[key] = (b.R + px[p], b.G + px[p + 1], b.B + px[p + 2], b.N + 1);
        }
        var colours = new List<(double, double, double)>();
        int counted = 0;
        foreach (var b in bins.Values.OrderByDescending(b => b.N).Take(3))
        {
            if (b.N < opaque * 0.08) break;
            colours.Add((b.R / b.N, b.G / b.N, b.B / b.N));
            counted += b.N;
        }
        share = opaque == 0 ? 1 : (double)counted / opaque;
        return colours;
    }

    /// <summary>The average colour round a point of the picture (pixels).</summary>
    public static (double R, double G, double B) ColourAt(RasterImage img, double x, double y)
    {
        int cx = Math.Clamp((int)x, 0, img.Width - 1), cy = Math.Clamp((int)y, 0, img.Height - 1);
        double r = 0, g = 0, b = 0;
        int n = 0;
        for (int yy = Math.Max(0, cy - 2); yy <= Math.Min(img.Height - 1, cy + 2); yy++)
            for (int xx = Math.Max(0, cx - 2); xx <= Math.Min(img.Width - 1, cx + 2); xx++)
            {
                int p = (yy * img.Width + xx) * 4;
                r += img.Rgba[p]; g += img.Rgba[p + 1]; b += img.Rgba[p + 2];
                n++;
            }
        return (r / n, g / n, b / n);
    }

    static double Dist((double R, double G, double B) c, double r, double g, double b)
    {
        double dr = c.R - r, dg = c.G - g, db = c.B - b;
        return Math.Sqrt(dr * dr * 0.3 + dg * dg * 0.59 + db * db * 0.11) * 1.7;
    }
}
