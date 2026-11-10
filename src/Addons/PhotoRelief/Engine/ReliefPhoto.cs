using PhotonCanvas.Core.Imaging;

namespace PhotonCanvas.Addon.PhotoRelief;

/// <summary>
/// The photo a relief is made from: kept at a working size (at most <see cref="MaxSide"/> pixels a
/// side, plenty for a carving), with smaller copies made as needed so a small preview doesn't
/// shimmer.
/// </summary>
public sealed class ReliefPhoto
{
    public const int MaxSide = 2400;

    public RasterImage Image { get; }
    public string Name { get; }
    public int Width => Image.Width;
    public int Height => Image.Height;
    /// <summary>Has see-through parts (its background already removed).</summary>
    public bool HasSeeThrough { get; }

    readonly Dictionary<int, RasterImage> _smaller = [];

    public ReliefPhoto(RasterImage image, string name)
    {
        int f = (int)Math.Ceiling(Math.Max(image.Width, image.Height) / (double)MaxSide);
        Image = f > 1 ? Shrink(image, f) : image;
        Name = string.IsNullOrWhiteSpace(name) ? "Photo" : name.Trim();
        var px = Image.Rgba;
        for (int i = 3; i < px.Length; i += 4)
            if (px[i] < 250) { HasSeeThrough = true; break; }
    }

    /// <summary>The photo made <paramref name="factor"/> times smaller (each pixel the average of a block).</summary>
    public RasterImage At(int factor)
    {
        if (factor <= 1) return Image;
        lock (_smaller)
        {
            if (!_smaller.TryGetValue(factor, out var img)) _smaller[factor] = img = Shrink(Image, factor);
            return img;
        }
    }

    /// <summary>The photo as a file to keep with the relief (JPEG, or PNG when it has see-through parts).</summary>
    public byte[] Encoded() => HasSeeThrough ? Image.EncodePng() : Image.EncodeJpeg(0, 92);

    /// <summary>Block average, weighted by how opaque each pixel is (so see-through pixels don't darken the edges).</summary>
    public static RasterImage Shrink(RasterImage src, int f)
    {
        int w = Math.Max(1, src.Width / f), h = Math.Max(1, src.Height / f);
        var o = new byte[w * h * 4];
        var s = src.Rgba;
        Parallel.For(0, h, y =>
        {
            for (int x = 0; x < w; x++)
            {
                double r = 0, g = 0, b = 0, a = 0;
                int n = 0;
                for (int yy = y * f; yy < Math.Min(src.Height, (y + 1) * f); yy++)
                    for (int xx = x * f; xx < Math.Min(src.Width, (x + 1) * f); xx++)
                    {
                        int p = (yy * src.Width + xx) * 4;
                        double al = s[p + 3];
                        r += s[p] * al; g += s[p + 1] * al; b += s[p + 2] * al; a += al;
                        n++;
                    }
                int q = (y * w + x) * 4;
                if (a > 0) { o[q] = (byte)Math.Round(r / a); o[q + 1] = (byte)Math.Round(g / a); o[q + 2] = (byte)Math.Round(b / a); }
                o[q + 3] = (byte)Math.Round(a / Math.Max(1, n));
            }
        });
        return new RasterImage(w, h, o);
    }
}
