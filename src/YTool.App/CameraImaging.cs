using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;

namespace YTool.App;

static class CameraImaging
{
    /// <summary>Copy straight-alpha RGBA pixels into an Avalonia bitmap (safe off the UI thread).</summary>
    public static WriteableBitmap ToAvaloniaRgba(YTool.Core.Imaging.RasterImage img)
    {
        var wb = new WriteableBitmap(new PixelSize(img.Width, img.Height), new Vector(96, 96), PixelFormat.Rgba8888, AlphaFormat.Unpremul);
        using var fb = wb.Lock();
        int row = img.Width * 4;
        for (int y = 0; y < img.Height; y++)
            System.Runtime.InteropServices.Marshal.Copy(img.Rgba, y * row, fb.Address + y * fb.RowBytes, row);
        return wb;
    }

    /// <summary>Copy a BGRA Skia bitmap into an Avalonia bitmap (safe to call off the UI thread).</summary>
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
