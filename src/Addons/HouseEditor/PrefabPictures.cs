using Avalonia.Media;
using Clipper2Lib;
using PhotonCanvas.App.View3D;
using PhotonCanvas.Core;
using SkiaSharp;
using Geo = PhotonCanvas.Addon.Walls.Geo;

namespace PhotonCanvas.Addon.HouseEditor;

/// <summary>A small picture of a building, put together and seen from the front left, for the saved buildings.</summary>
public static class PrefabPictures
{
    static readonly Vec3 Light = new(0.35, 0.85, 0.45);
    static readonly SlabStyle Look = new(SlabStyle.Lit(Color.FromRgb(0xDD, 0xC9, 0xAA), Light), Color.FromRgb(0x5A, 0x46, 0x32), LineWidth.Px(0.8),
        Color.FromRgb(0x7E, 0x5A, 0x38), Color.FromRgb(0x6B, 0x47, 0x28), new LineWidth(0.05, 0.25, 0.8));

    /// <summary>The building's pieces put together, roofs on, as a PNG <paramref name="width"/> × <paramref name="height"/> pixels; null if there's nothing to show.</summary>
    public static byte[]? Png(HouseKit kit, int width = 360, int height = 270)
    {
        if (kit.Solids.Count == 0) return null;
        var scene = new Scene3D();
        foreach (var s in kit.Solids)
        {
            var p = s.Pose;
            var outline = Geo.Tidy(Clipper.SimplifyPaths(s.Outline, 0.05));
            var frame = new Frame3(new Vec3(p.O.X, p.O.Y, p.O.Z), new Vec3(p.U.X, p.U.Y, p.U.Z), new Vec3(p.V.X, p.V.Y, p.V.Z));
            scene.AddSlab(outline, frame, -s.Thickness / 2, s.Thickness / 2, Look, s.Etched, s.Lines);
        }
        if (scene.IsEmpty) return null;
        var pixels = new Model3DPainter().Picture(scene, Camera3D.YUp(35, 32), width, height, Color.FromRgb(0xF6, 0xF3, 0xEC));
        if (pixels is null) return null;
        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var bitmap = new SKBitmap(info);
        var ints = new int[pixels.Length];
        Buffer.BlockCopy(pixels, 0, ints, 0, pixels.Length * 4);
        System.Runtime.InteropServices.Marshal.Copy(ints, 0, bitmap.GetPixels(), ints.Length);
        using var image = SKImage.FromBitmap(bitmap);
        using var png = image.Encode(SKEncodedImageFormat.Png, 90);
        return png.ToArray();
    }
}
