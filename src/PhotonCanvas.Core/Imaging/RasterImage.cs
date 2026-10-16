using SkiaSharp;

namespace PhotonCanvas.Core.Imaging;

/// <summary>
/// A decoded image: straight (non-premultiplied) RGBA, row-major. Immutable
/// after construction.
/// </summary>
public sealed class RasterImage
{
    public int Width { get; }
    public int Height { get; }
    /// <summary>R, G, B, A bytes per pixel.</summary>
    public byte[] Rgba { get; }

    public RasterImage(int width, int height, byte[] rgba)
    {
        if (width <= 0 || height <= 0) throw new ArgumentException("image must have pixels");
        if (rgba.Length != width * height * 4) throw new ArgumentException("pixel data does not match the size");
        Width = width;
        Height = height;
        Rgba = rgba;
    }

    /// <summary>Largest side we keep in memory; bigger photos are scaled down on import.</summary>
    public const int MaxSide = 6000;

    /// <exception cref="FormatException">Not a readable image.</exception>
    public static RasterImage Decode(byte[] encoded)
    {
        using var codec = SKCodec.Create(new SKMemoryStream(encoded)) ?? throw new FormatException("This file isn't an image this program can read.");
        var size = codec.Info.Size;
        if (size.Width <= 0 || size.Height <= 0) throw new FormatException("The image is empty.");
        var info = new SKImageInfo(size.Width, size.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var bmp = new SKBitmap(info);
        var r = codec.GetPixels(info, bmp.GetPixels());
        if (r is not (SKCodecResult.Success or SKCodecResult.IncompleteInput)) throw new FormatException($"The image could not be decoded ({r}).");
        // Phone photos are often stored sideways with an orientation tag.
        using var oriented = ApplyOrientation(bmp, codec.EncodedOrigin);
        var src = oriented ?? bmp;
        if (Math.Max(src.Width, src.Height) > MaxSide)
        {
            double s = (double)MaxSide / Math.Max(src.Width, src.Height);
            var small = new SKImageInfo(Math.Max(1, (int)(src.Width * s)), Math.Max(1, (int)(src.Height * s)), SKColorType.Rgba8888, SKAlphaType.Unpremul);
            using var resized = src.Resize(small, SKFilterQuality.High) ?? throw new FormatException("The image could not be resized.");
            return new RasterImage(resized.Width, resized.Height, resized.Bytes);
        }
        return new RasterImage(src.Width, src.Height, src.Bytes);
    }

    /// <summary>JPEG of this image, scaled down so its longest side is at most <paramref name="maxSide"/> (0 = full size).</summary>
    public byte[] EncodeJpeg(int maxSide = 0, int quality = 85) => Encode(SKEncodedImageFormat.Jpeg, maxSide, quality);

    /// <summary>PNG of this image (keeps transparency), optionally scaled down.</summary>
    public byte[] EncodePng(int maxSide = 0) => Encode(SKEncodedImageFormat.Png, maxSide, 100);

    byte[] Encode(SKEncodedImageFormat format, int maxSide, int quality)
    {
        var info = new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var bmp = new SKBitmap(info);
        System.Runtime.InteropServices.Marshal.Copy(Rgba, 0, bmp.GetPixels(), Rgba.Length);
        SKBitmap? resized = null;
        try
        {
            var src = bmp;
            if (maxSide > 0 && Math.Max(Width, Height) > maxSide)
            {
                double s = (double)maxSide / Math.Max(Width, Height);
                resized = bmp.Resize(new SKImageInfo(Math.Max(1, (int)(Width * s)), Math.Max(1, (int)(Height * s)), SKColorType.Rgba8888, SKAlphaType.Unpremul), SKFilterQuality.High);
                if (resized is not null) src = resized;
            }
            using var img = SKImage.FromBitmap(src);
            using var data = img.Encode(format, quality);
            return data.ToArray();
        }
        finally { resized?.Dispose(); }
    }

    /// <summary>
    /// This image turned <paramref name="degreesClockwise"/> about its centre, on a canvas big
    /// enough to hold all of it (the corners outside the picture are transparent).
    /// </summary>
    public RasterImage Rotated(double degreesClockwise)
    {
        double d = (degreesClockwise % 360 + 360) % 360;
        if (d < 1e-9) return this;
        double r = d * Math.PI / 180, cos = Math.Abs(Math.Cos(r)), sin = Math.Abs(Math.Sin(r));
        int w = (int)Math.Ceiling(Width * cos + Height * sin - 1e-6), h = (int)Math.Ceiling(Width * sin + Height * cos - 1e-6);
        var info = new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var src = new SKBitmap(info);
        System.Runtime.InteropServices.Marshal.Copy(Rgba, 0, src.GetPixels(), Rgba.Length);
        using var dst = new SKBitmap(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        using (var c = new SKCanvas(dst))
        {
            c.Clear(SKColors.Transparent);
            c.Translate(w / 2f, h / 2f);
            c.RotateDegrees((float)d);
            c.Translate(-Width / 2f, -Height / 2f);
            using var paint = new SKPaint { IsAntialias = true, FilterQuality = SKFilterQuality.High };
            c.DrawBitmap(src, 0, 0, paint);
        }
        return new RasterImage(w, h, dst.Bytes);
    }

    static SKBitmap? ApplyOrientation(SKBitmap bmp, SKEncodedOrigin origin)
    {
        if (origin is SKEncodedOrigin.TopLeft or SKEncodedOrigin.Default) return null;
        bool swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var info = new SKImageInfo(swap ? bmp.Height : bmp.Width, swap ? bmp.Width : bmp.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var o = new SKBitmap(info);
        using var c = new SKCanvas(o);
        float w = bmp.Width, h = bmp.Height;
        switch (origin)
        {
            case SKEncodedOrigin.TopRight: c.Scale(-1, 1, w / 2, 0); break;
            case SKEncodedOrigin.BottomRight: c.RotateDegrees(180, w / 2, h / 2); break;
            case SKEncodedOrigin.BottomLeft: c.Scale(1, -1, 0, h / 2); break;
            case SKEncodedOrigin.LeftTop: c.Translate(0, 0); c.RotateDegrees(90); c.Scale(1, -1); break;
            case SKEncodedOrigin.RightTop: c.Translate(h, 0); c.RotateDegrees(90); break;
            case SKEncodedOrigin.RightBottom: c.Translate(h, w); c.RotateDegrees(90); c.Scale(-1, 1); c.Translate(-w, 0); break;
            case SKEncodedOrigin.LeftBottom: c.Translate(0, w); c.RotateDegrees(-90); break;
        }
        c.DrawBitmap(bmp, 0, 0);
        return o;
    }

    /// <summary>
    /// Brightness 0 (black) – 1 (white) per pixel, transparent areas count as
    /// white (nothing to burn). Rec. 709 luma.
    /// </summary>
    public float[] Luminance()
    {
        var o = new float[Width * Height];
        for (int i = 0; i < o.Length; i++)
        {
            int p = i * 4;
            float y = (0.2126f * Rgba[p] + 0.7152f * Rgba[p + 1] + 0.0722f * Rgba[p + 2]) / 255f;
            float a = Rgba[p + 3] / 255f;
            o[i] = y * a + (1 - a);
        }
        return o;
    }

    /// <summary>Encode as PNG (for saving a modified image).</summary>
    public byte[] ToPng()
    {
        var info = new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var bmp = new SKBitmap(info);
        System.Runtime.InteropServices.Marshal.Copy(Rgba, 0, bmp.GetPixels(), Rgba.Length);
        using var data = bmp.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
