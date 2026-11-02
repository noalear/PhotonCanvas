using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using PhotonCanvas.App.Addons;
using PhotonCanvas.Core;

namespace PhotonCanvas.Addon.Grain;

/// <summary>The grain map, or where a layer's power is lowered, laid over the camera picture on the canvas.</summary>
sealed class GrainOverlay : ICanvasOverlay
{
    WriteableBitmap? _bitmap;
    Rect2 _area;
    double _opacity = 0.8;

    public CanvasLayer Layer => CanvasLayer.UnderShapes;

    public void Clear()
    {
        _bitmap?.Dispose();
        _bitmap = null;
    }

    /// <summary>Show the map itself: dark grain dark, light grain light.</summary>
    public void ShowMap(GrainMap map)
    {
        var rgba = new byte[map.Width * map.Height * 4];
        for (int i = 0; i < map.Levels.Length; i++)
        {
            byte v = map.Levels[i];
            byte g = (byte)(230 - v * 200 / 254);
            rgba[i * 4] = g;
            rgba[i * 4 + 1] = (byte)(g * 0.92);
            rgba[i * 4 + 2] = (byte)(g * 0.8);
            rgba[i * 4 + 3] = v == GrainMap.Unknown ? (byte)0 : (byte)255;
        }
        Set(rgba, map.Width, map.Height, map.Area, 0.85);
    }

    /// <summary>Show how much less power the field uses: clear at full power, deeper orange the less it uses.</summary>
    public void ShowPower(PowerField field)
    {
        int w = field.Width, h = field.Height;
        var rgba = new byte[w * h * 4];
        for (int r = 0; r < h; r++)
            for (int c = 0; c < w; c++)
            {
                int i = r * w + c;
                float f = field.Cell(c, r);
                double less = float.IsNaN(f) ? 0 : Math.Clamp((1 - f) / GrainSettings.MaxStrength, 0, 1);
                rgba[i * 4] = 249;
                rgba[i * 4 + 1] = 115;
                rgba[i * 4 + 2] = 22;
                rgba[i * 4 + 3] = (byte)Math.Round(less * 230);
            }
        Set(rgba, w, h, field.Bounds, 1);
    }

    void Set(byte[] rgba, int w, int h, Rect2 area, double opacity)
    {
        var bmp = new WriteableBitmap(new PixelSize(w, h), new Vector(96, 96), PixelFormat.Rgba8888, AlphaFormat.Unpremul);
        using (var fb = bmp.Lock())
            for (int r = 0; r < h; r++)
                Marshal.Copy(rgba, r * w * 4, fb.Address + r * fb.RowBytes, w * 4);
        _bitmap?.Dispose();
        _bitmap = bmp;
        _area = area;
        _opacity = opacity;
    }

    public void Draw(DrawingContext ctx, CanvasDrawInfo info)
    {
        if (_bitmap is not { } bmp) return;
        var size = bmp.PixelSize;
        using (ctx.PushOpacity(_opacity))
            ctx.DrawImage(bmp, new Rect(0, 0, size.Width, size.Height), new Rect(_area.MinX, _area.MinY, _area.Width, _area.Height));
    }
}
