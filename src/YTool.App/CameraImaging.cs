using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;

namespace YTool.App;

static class CameraImaging
{
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
