using PhotonCanvas.Core;
using PhotonCanvas.Core.Imaging;
using PhotonCanvas.Doc.Geometry;
using PhotonCanvas.Doc.Tracing;

namespace PhotonCanvas.Doc;

/// <summary>Settings for <see cref="PrintCut"/>.</summary>
public sealed record PrintCutOptions
{
    /// <summary>How far outside the printed art the cut goes (mm): a white border round each sticker.</summary>
    public double MarginMm { get; init; } = 1;
    /// <summary>How much darker or more coloured than the paper counts as print (0–1; lower finds fainter print).</summary>
    public double Sensitivity { get; init; } = 0.12;
    /// <summary>Pieces of print smaller than this (mm²) are specks, not stickers.</summary>
    public double MinAreaMm2 { get; init; } = 10;
}

/// <summary>
/// Print-then-cut: the camera finds printed artwork on paper (stickers, labels) and a cut goes round
/// each one, a margin outside it. Print is whatever isn't paper: darker, or coloured, than the paper's
/// own white (measured from the photo). Parts of one sticker that come within the margin of each
/// other get one cut round them all; holes inside a sticker aren't cut.
/// </summary>
public static class PrintCut
{
    /// <summary>The cut outlines (bed mm) for the print in <paramref name="within"/> of a straightened photo.</summary>
    public static PathGeometry Find(RasterImage photo, Rect2 photoArea, double mmPerPx, Rect2 within, PrintCutOptions o)
    {
        int x0 = Math.Clamp((int)Math.Floor((within.MinX - photoArea.MinX) / mmPerPx), 0, photo.Width), x1 = Math.Clamp((int)Math.Ceiling((within.MaxX - photoArea.MinX) / mmPerPx), 0, photo.Width);
        int y0 = Math.Clamp((int)Math.Floor((within.MinY - photoArea.MinY) / mmPerPx), 0, photo.Height), y1 = Math.Clamp((int)Math.Ceiling((within.MaxY - photoArea.MinY) / mmPerPx), 0, photo.Height);
        int w = x1 - x0, h = y1 - y0;
        if (w < 4 || h < 4) return PathGeometry.Empty;
        // The paper: the brightness most of the area has (the 80th percentile, so print doesn't pull it down).
        var lum = new float[w * h];
        var sat = new float[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int k = ((y0 + y) * photo.Width + x0 + x) * 4;
                double r = photo.Rgba[k] / 255.0, g = photo.Rgba[k + 1] / 255.0, b = photo.Rgba[k + 2] / 255.0;
                double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
                lum[y * w + x] = (float)(0.299 * r + 0.587 * g + 0.114 * b);
                sat[y * w + x] = (float)(max <= 0 ? 0 : (max - min) / max);
            }
        var sorted = (float[])lum.Clone();
        Array.Sort(sorted);
        double paper = sorted[(int)(sorted.Length * 0.8)];
        double paperSat = sat.OrderBy(v => v).ElementAt((int)(sat.Length * 0.5));
        var ink = new bool[w * h];
        for (int i = 0; i < ink.Length; i++)
            ink[i] = lum[i] < paper * (1 - o.Sensitivity) || sat[i] > paperSat + o.Sensitivity * 2;
        // Traced in pixels, then onto the bed.
        var traced = Tracer.TraceMask(ink, w, h, new TraceOptions { Smoothness = 1, MinAreaPx = 4 });
        var toBed = Affine.Scale(mmPerPx, mmPerPx).Then(Affine.Translate(photoArea.MinX + x0 * mmPerPx, photoArea.MinY + y0 * mmPerPx));
        var art = traced.Transform(toBed);
        if (art.IsEmpty) return PathGeometry.Empty;
        // Out by the margin (joining near parts), then only the outsides, without specks.
        var grown = ShapeGeometry.Offset(ShapeGeometry.Union([art]), o.MarginMm, JoinStyle.Round, EndStyle.Round);
        var loops = grown.Flatten(0.02).Where(f => f.Closed && f.Points.Count >= 3).Select(f => f.Points).ToList();
        var outer = loops.Where(l => !loops.Any(other => !ReferenceEquals(other, l) && Geom.Inside(other, l[0])))
            .Where(l => Math.Abs(Geom.SignedArea(l)) >= o.MinAreaMm2 + Perimeter(l) * o.MarginMm)
            .ToList();
        return new PathGeometry([.. outer.SelectMany(l => PathGeometry.Polyline(l, true).Subpaths)]);
    }

    static double Perimeter(IReadOnlyList<Vec2> l) => l.Select((p, i) => p.Dist(l[(i + 1) % l.Count])).Sum();
}
