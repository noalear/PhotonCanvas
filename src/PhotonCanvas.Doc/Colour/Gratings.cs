using System.Collections.Immutable;
using PhotonCanvas.Core;
using PhotonCanvas.Core.Camera;
using PhotonCanvas.Core.Imaging;
using PhotonCanvas.Doc.Materials;

namespace PhotonCanvas.Doc.Colour;

/// <summary>
/// The five fixed views a grating test is photographed from: always the same, so results can be
/// compared across plates and over time. The lamp stays put; only the camera moves, tilting 45°
/// from straight above towards each side of the plate (as the plate lies on screen).
/// </summary>
public enum GratingView { Straight, Top, Right, Bottom, Left }

public static class GratingViews
{
    public static readonly IReadOnlyList<GratingView> All = [GratingView.Straight, GratingView.Top, GratingView.Right, GratingView.Bottom, GratingView.Left];

    public static string Short(GratingView v) => v switch
    {
        GratingView.Straight => "Straight",
        GratingView.Top => "Top 45°",
        GratingView.Right => "Right 45°",
        GratingView.Bottom => "Bottom 45°",
        _ => "Left 45°",
    };

    public static string Long(GratingView v) => v switch
    {
        GratingView.Straight => "straight down from above",
        GratingView.Top => "tilted 45° from above, from the top edge's side",
        GratingView.Right => "tilted 45° from above, from the right edge's side",
        GratingView.Bottom => "tilted 45° from above, from the bottom edge's side",
        _ => "tilted 45° from above, from the left edge's side",
    };
}

/// <summary>One view of a grating set: the squares as measured from it, and its straightened picture.</summary>
public sealed record GratingViewResult
{
    public GratingView View { get; init; }
    public ImmutableList<ColourSwatch> Swatches { get; init; } = [];
    /// <summary>File name (in the library's photo folder) of the grid as seen from this view, straightened.</summary>
    public string? Frame { get; init; }
}

/// <summary>A grating test photographed from the five fixed views.</summary>
public sealed record GratingSet
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = "";
    public DateTime Measured { get; init; } = DateTime.Now;
    public double? FocusMm { get; init; }
    /// <summary>Where the lamp was and how the photos were taken.</summary>
    public string Notes { get; init; } = "";
    public ImmutableList<GratingViewResult> Views { get; init; } = [];
    /// <summary>File name of the animation cycling through the views.</summary>
    public string? Gif { get; init; }
    public string Machine { get; init; } = MachineProfile.F2Ultra.DeviceCode;
}

/// <summary>How one square did across the views.</summary>
/// <param name="Vividness">Its strongest colour (CIELAB chroma) in any view.</param>
/// <param name="Brightness">Its lightest (CIELAB L) in any view.</param>
/// <param name="LightsUpAt">The views where it shows strong colour (within 60% of its best, and clearly coloured).</param>
public sealed record GratingScore(int Row, int Col, ProcessSettings Settings, double Vividness, double Brightness, IReadOnlyList<GratingView> LightsUpAt);

public static class GratingMeasure
{
    /// <summary>
    /// The part of a photo showing <paramref name="areaMm"/> of the design, straightened (seen
    /// from straight above), <paramref name="pxPerMm"/> pixels per mm. Outside the photo is transparent.
    /// </summary>
    public static RasterImage Straighten(RasterImage photo, Homography mmToPx, Rect2 areaMm, double pxPerMm)
    {
        int w = Math.Max(1, (int)Math.Round(areaMm.Width * pxPerMm)), h = Math.Max(1, (int)Math.Round(areaMm.Height * pxPerMm));
        var o = new byte[w * h * 4];
        var src = photo.Rgba;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var p = mmToPx.Apply(new Vec2(areaMm.MinX + (x + 0.5) / pxPerMm, areaMm.MinY + (y + 0.5) / pxPerMm));
                double fx = p.X - 0.5, fy = p.Y - 0.5;
                int x0 = (int)Math.Floor(fx), y0 = (int)Math.Floor(fy);
                if (x0 < 0 || y0 < 0 || x0 + 1 >= photo.Width || y0 + 1 >= photo.Height) continue;
                double tx = fx - x0, ty = fy - y0;
                int i = (y * w + x) * 4;
                for (int ch = 0; ch < 4; ch++)
                {
                    double a = src[(y0 * photo.Width + x0) * 4 + ch], b = src[(y0 * photo.Width + x0 + 1) * 4 + ch];
                    double c = src[((y0 + 1) * photo.Width + x0) * 4 + ch], d = src[((y0 + 1) * photo.Width + x0 + 1) * 4 + ch];
                    o[i + ch] = (byte)Math.Round((a * (1 - tx) + b * tx) * (1 - ty) + (c * (1 - tx) + d * tx) * ty);
                }
            }
        return new RasterImage(w, h, o);
    }

    /// <summary>The squares across the views, most vivid first (squares left out in any view don't count there).</summary>
    public static IReadOnlyList<GratingScore> Score(IEnumerable<GratingViewResult> views)
    {
        var byCell = new Dictionary<(int, int), List<(GratingView View, ColourSwatch S)>>();
        foreach (var v in views)
            foreach (var s in v.Swatches.Where(s => s.Use))
            {
                if (!byCell.TryGetValue((s.Row, s.Col), out var list)) byCell[(s.Row, s.Col)] = list = [];
                list.Add((v.View, s));
            }
        return byCell.Select(kv =>
        {
            var list = kv.Value;
            double vivid = list.Max(x => x.S.Colour.Chroma), bright = list.Max(x => x.S.L);
            var at = list.Where(x => x.S.Colour.Chroma >= Math.Max(12, 0.6 * vivid)).Select(x => x.View).ToList();
            return new GratingScore(kv.Key.Item1, kv.Key.Item2, list[0].S.Settings, vivid, bright, at);
        }).OrderByDescending(s => s.Vividness).ToList();
    }
}
