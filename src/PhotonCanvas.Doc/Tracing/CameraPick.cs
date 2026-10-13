using PhotonCanvas.Core;
using PhotonCanvas.Core.Imaging;

namespace PhotonCanvas.Doc.Tracing;

/// <summary>How the magic wand turns a clicked region of the camera picture into an outline.</summary>
public sealed record PickOptions
{
    /// <summary>0–100: how different a colour may be from the clicked one and still count as the piece.</summary>
    public double Tolerance { get; init; } = 25;
    /// <summary>0 = follow every pixel step … 10 = very smooth, simplified outline.</summary>
    public double Smoothness { get; init; } = 3;
    /// <summary>Bends sharper than this (degrees) stay corners; gentler ones become curves.</summary>
    public double CornerAngle { get; init; } = 40;
    /// <summary>Specks and holes smaller than this (mm²) are ignored (knots, burn marks, dust).</summary>
    public double IgnoreBelowMm2 { get; init; } = 2;
}

/// <summary>
/// Vector outlines from the straightened camera picture of the work area
/// (<c>area</c> in mm at <c>mmPerPx</c>; pixel (x, y) covers
/// <c>area.Min + [x, x+1) · mmPerPx</c>): the magic wand and the magnetic pen's smoothing.
/// </summary>
public static class CameraPick
{
    /// <summary>Lightly smooth the photo (camera noise, wood grain) and get it ready for picking. Do this once per photo.</summary>
    public static ImageTrace.Working Prepare(RasterImage photo) => ImageTrace.Prepare(Blur(photo));

    /// <summary>
    /// The outline (with holes) of the region around <paramref name="clickMm"/> whose colour
    /// is within the tolerance of the clicked colour, in mm. Empty when nothing is there.
    /// </summary>
    public static PathGeometry Outline(ImageTrace.Working w, Rect2 area, double mmPerPx, Vec2 clickMm, PickOptions o)
    {
        int x = (int)Math.Floor((clickMm.X - area.MinX) / mmPerPx / w.Scale), y = (int)Math.Floor((clickMm.Y - area.MinY) / mmPerPx / w.Scale);
        if (x < 0 || y < 0 || x >= w.Image.Width || y >= w.Image.Height) return new PathGeometry([]);
        // A click outside what the camera sees picks nothing.
        if (w.Image.Rgba[(y * w.Image.Width + x) * 4 + 3] < 128) return new PathGeometry([]);
        var trace = new TraceOptions
        {
            Smoothness = o.Smoothness,
            CornerAngle = o.CornerAngle,
            MinAreaPx = Math.Max(1, (int)Math.Round(o.IgnoreBelowMm2 / (mmPerPx * mmPerPx))),
        };
        var colour = ImageTrace.SampleColour(w, x, y);
        var r = ImageTrace.PickColour(w, colour, x, y, o.Tolerance, connected: true, trace);
        if (r.Parts.Count == 0) return new PathGeometry([]);
        return ToMm(r.Parts[0].Geometry, area, mmPerPx);
    }

    /// <summary>A closed loop of points (mm, e.g. from the magnetic pen) as smooth lines and curves.</summary>
    public static PathGeometry FitLoop(IReadOnlyList<Vec2> loopMm, Rect2 area, double mmPerPx, double smoothness = 3, double cornerAngle = 40)
    {
        var px = loopMm.Select(p => (p - new Vec2(area.MinX, area.MinY)) / mmPerPx).ToList();
        if (px.Count > 1 && px[0].Dist(px[^1]) < 1e-9) px.RemoveAt(px.Count - 1);
        double eps = 0.3 + Math.Clamp(smoothness, 0, 10) * 0.25;
        var sub = Tracer.FitOutline(px, eps, cornerAngle);
        return sub is null ? PathGeometry.Polyline(loopMm, true) : ToMm(new PathGeometry([sub]), area, mmPerPx);
    }

    static PathGeometry ToMm(PathGeometry px, Rect2 area, double mmPerPx) =>
        px.Transform(Affine.Scale(mmPerPx, mmPerPx).Then(Affine.Translate(area.MinX, area.MinY)));

    /// <summary>3 × 3 box blur, twice (≈ Gaussian); see-through pixels stay see-through.</summary>
    static RasterImage Blur(RasterImage src)
    {
        int w = src.Width, h = src.Height;
        var a = (byte[])src.Rgba.Clone();
        var b = new byte[a.Length];
        for (int pass = 0; pass < 2; pass++)
        {
            Parallel.For(0, h, y =>
            {
                for (int x = 0; x < w; x++)
                {
                    int o = (y * w + x) * 4;
                    if (a[o + 3] < 128) { b[o] = a[o]; b[o + 1] = a[o + 1]; b[o + 2] = a[o + 2]; b[o + 3] = a[o + 3]; continue; }
                    int r = 0, g = 0, bl = 0, n = 0;
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int xx = x + dx, yy = y + dy;
                            if (xx < 0 || yy < 0 || xx >= w || yy >= h) continue;
                            int q = (yy * w + xx) * 4;
                            if (a[q + 3] < 128) continue;
                            r += a[q]; g += a[q + 1]; bl += a[q + 2]; n++;
                        }
                    b[o] = (byte)(r / n); b[o + 1] = (byte)(g / n); b[o + 2] = (byte)(bl / n); b[o + 3] = a[o + 3];
                }
            });
            (a, b) = (b, a);
        }
        return new RasterImage(w, h, a);
    }
}
