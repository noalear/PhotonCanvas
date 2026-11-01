using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;

namespace PhotonCanvas.App;

public static class CameraImaging
{
    /// <summary>Copy straight-alpha RGBA pixels into an Avalonia bitmap (safe off the UI thread).</summary>
    public static WriteableBitmap ToAvaloniaRgba(PhotonCanvas.Core.Imaging.RasterImage img)
    {
        var wb = new WriteableBitmap(new PixelSize(img.Width, img.Height), new Vector(96, 96), PixelFormat.Rgba8888, AlphaFormat.Unpremul);
        using var fb = wb.Lock();
        int row = img.Width * 4;
        for (int y = 0; y < img.Height; y++)
            System.Runtime.InteropServices.Marshal.Copy(img.Rgba, y * row, fb.Address + y * fb.RowBytes, row);
        return wb;
    }

    /// <summary>Copy a BGRA Skia bitmap into an Avalonia bitmap (safe to call off the UI thread).</summary>
    /// <summary>RGBA copy of a camera bitmap, for the drawing tools.</summary>
    public static PhotonCanvas.Core.Imaging.RasterImage ToRaster(SKBitmap b)
    {
        var px = b.Pixels;
        var rgba = new byte[px.Length * 4];
        for (int i = 0; i < px.Length; i++)
        {
            rgba[i * 4] = px[i].Red; rgba[i * 4 + 1] = px[i].Green; rgba[i * 4 + 2] = px[i].Blue; rgba[i * 4 + 3] = px[i].Alpha;
        }
        return new PhotonCanvas.Core.Imaging.RasterImage(b.Width, b.Height, rgba);
    }

    public static WriteableBitmap ToAvalonia(SKBitmap b)
    {
        var wb = new WriteableBitmap(new PixelSize(b.Width, b.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using var fb = wb.Lock();
        var bytes = b.Bytes;
        int row = b.Width * 4;
        for (int y = 0; y < b.Height; y++)
            System.Runtime.InteropServices.Marshal.Copy(bytes, y * b.RowBytes, fb.Address + y * fb.RowBytes, row);
        return wb;
    }
}
