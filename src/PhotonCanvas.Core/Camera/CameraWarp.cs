using SkiaSharp;

namespace PhotonCanvas.Core.Camera;

/// <summary>Turns a camera photo into a top-down picture of the work area.</summary>
public static class CameraWarp
{
    /// <summary>
    /// Decode a JPEG as BGRA, optionally downscaled (the cameras are 4000×3000;
    /// <paramref name="maxWidth"/> keeps live view fast).
    /// </summary>
    public static SKBitmap Decode(byte[] jpeg, int maxWidth = 0)
    {
        using var codec = SKCodec.Create(new SKMemoryStream(jpeg)) ?? throw new FormatException("The camera picture could not be read.");
        var size = codec.Info.Size;
        if (maxWidth > 0 && size.Width > maxWidth)
            size = codec.GetScaledDimensions((float)maxWidth / size.Width);
        var info = new SKImageInfo(size.Width, size.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        var bmp = new SKBitmap(info);
        var result = codec.GetPixels(info, bmp.GetPixels());
        if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
        {
            // Scaled decoding unsupported for this image: decode fully and resize.
            bmp.Dispose();
            using var full = SKBitmap.Decode(jpeg) ?? throw new FormatException("The camera picture could not be read.");
            bmp = full.Resize(info, SKFilterQuality.Medium) ?? throw new FormatException("The camera picture could not be resized.");
        }
        return bmp;
    }

    /// <summary>
    /// Whether a photo is washed out: the cameras need a frame or two to settle
    /// their exposure after the lights change, and the first one can be blown out.
    /// </summary>
    public static bool IsOverexposed(byte[] jpeg)
    {
        using var small = Decode(jpeg, 400);
        var px = small.Pixels;
        int clipped = 0;
        double sum = 0;
        foreach (var c in px)
        {
            int y = (c.Red * 77 + c.Green * 150 + c.Blue * 29) >> 8;
            sum += y;
            if (y >= 245) clipped++;
        }
        // Normal overview photos average ~80/255 with ~1 % clipped (measured on the user's F2 Ultra).
        return clipped > px.Length * 0.2 || sum / px.Length > 185;
    }

    public static GrayImage ToGray(SKBitmap bmp)
    {
        var px = bmp.Pixels;
        var g = new byte[bmp.Width * bmp.Height];
        for (int i = 0; i < g.Length; i++)
        {
            var c = px[i];
            g[i] = (byte)((c.Red * 77 + c.Green * 150 + c.Blue * 29) >> 8);
        }
        return new GrayImage(bmp.Width, bmp.Height, g);
    }

    /// <summary>
    /// Resample <paramref name="src"/> into a top-down view of <paramref name="area"/>
    /// at <paramref name="mmPerPx"/>. Areas the camera can't see are transparent.
    /// </summary>
    public static SKBitmap Warp(SKBitmap src, IImageMap map, Rect2 area, double mmPerPx)
    {
        if (src.ColorType != SKColorType.Bgra8888) throw new ArgumentException("source must be BGRA", nameof(src));
        int w = Math.Max(1, (int)Math.Round(area.Width / mmPerPx)), h = Math.Max(1, (int)Math.Round(area.Height / mmPerPx));
        var dst = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul));
        int sw = src.Width, sh = src.Height;
        var srcPx = src.GetPixelSpan().ToArray();
        var dstPx = new byte[w * h * 4];

        // The map is smooth, so evaluate it on a coarse grid and interpolate between.
        const int Step = 8;
        int gw = w / Step + 2, gh = h / Step + 2;
        var grid = new Vec2[gw * gh];
        for (int gy = 0; gy < gh; gy++)
            for (int gx = 0; gx < gw; gx++)
            {
                var mm = new Vec2(area.MinX + (gx * Step + 0.5) * mmPerPx, area.MinY + (gy * Step + 0.5) * mmPerPx);
                var n = map.ToImage(mm);
                grid[gy * gw + gx] = new(n.X * sw - 0.5, n.Y * sh - 0.5);
            }

        Parallel.For(0, h, y =>
        {
            int gy = y / Step;
            double fy = (double)(y % Step) / Step;
            for (int x = 0; x < w; x++)
            {
                int gx = x / Step;
                double fx = (double)(x % Step) / Step;
                var a = grid[gy * gw + gx]; var b = grid[gy * gw + gx + 1];
                var c = grid[(gy + 1) * gw + gx]; var d = grid[(gy + 1) * gw + gx + 1];
                double sx = (a.X * (1 - fx) + b.X * fx) * (1 - fy) + (c.X * (1 - fx) + d.X * fx) * fy;
                double sy = (a.Y * (1 - fx) + b.Y * fx) * (1 - fy) + (c.Y * (1 - fx) + d.Y * fx) * fy;
                if (sx < 0 || sy < 0 || sx > sw - 1 || sy > sh - 1) continue;
                int x0 = (int)sx, y0 = (int)sy;
                int x1 = Math.Min(x0 + 1, sw - 1), y1 = Math.Min(y0 + 1, sh - 1);
                double tx = sx - x0, ty = sy - y0;
                int o = (y * w + x) * 4;
                for (int ch = 0; ch < 4; ch++)
                {
                    double v00 = srcPx[(y0 * sw + x0) * 4 + ch], v10 = srcPx[(y0 * sw + x1) * 4 + ch];
                    double v01 = srcPx[(y1 * sw + x0) * 4 + ch], v11 = srcPx[(y1 * sw + x1) * 4 + ch];
                    dstPx[o + ch] = (byte)Math.Round((v00 * (1 - tx) + v10 * tx) * (1 - ty) + (v01 * (1 - tx) + v11 * tx) * ty);
                }
            }
        });
        System.Runtime.InteropServices.Marshal.Copy(dstPx, 0, dst.GetPixels(), dstPx.Length);
        return dst;
    }
}
