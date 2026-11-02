using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using PhotonCanvas.Core.Imaging;

namespace PhotonCanvas.Addon.BurnPreview;

static class Bitmaps
{
    /// <summary>Straight-alpha RGBA pixels into an Avalonia bitmap (safe off the UI thread).</summary>
    public static WriteableBitmap From(RasterImage img)
    {
        var wb = new WriteableBitmap(new PixelSize(img.Width, img.Height), new Vector(96, 96), PixelFormat.Rgba8888, AlphaFormat.Unpremul);
        using var fb = wb.Lock();
        int row = img.Width * 4;
        for (int y = 0; y < img.Height; y++)
            System.Runtime.InteropServices.Marshal.Copy(img.Rgba, y * row, fb.Address + y * fb.RowBytes, row);
        return wb;
    }
}
