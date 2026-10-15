using System.Collections.Immutable;
using PhotonCanvas.Core;
using PhotonCanvas.Core.Camera;
using PhotonCanvas.Core.Imaging;
using PhotonCanvas.Doc.Materials;

namespace PhotonCanvas.Doc.Colour;

/// <summary>One measured square of a colour test: the settings and the colour they made.</summary>
public sealed record ColourSwatch
{
    public int Row { get; init; }
    public int Col { get; init; }
    public ProcessSettings Settings { get; init; } = new();
    /// <summary>Measured colour, CIELAB (D65).</summary>
    public double L { get; init; }
    public double A { get; init; }
    public double B { get; init; }
    /// <summary>How uneven the square is: the average ΔE00 of its samples from its mean (under ~2 is even).</summary>
    public double Spread { get; init; }
    /// <summary>Part of the palette (the user can leave out squares that came out badly).</summary>
    public bool Use { get; init; } = true;

    public Lab Colour => new(L, A, B);
}

/// <summary>
/// The colours one material really produced with the MOPA laser, measured from a burned colour
/// test: the starting point for colour pictures. Only valid for that material, its surface
/// finish and the focus distance it was marked at.
/// </summary>
public sealed record ColourPalette
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = "";
    public DateTime Measured { get; init; } = DateTime.Now;
    public double? FocusMm { get; init; }
    /// <summary>How it was measured, e.g. "scan" or "photo with grey card".</summary>
    public string Method { get; init; } = "";
    public string Notes { get; init; } = "";
    public ImmutableList<ColourSwatch> Swatches { get; init; } = [];
    public string Machine { get; init; } = MachineProfile.F2Ultra.DeviceCode;
}

/// <summary>Measuring a burned colour test from a picture, and choosing the colours worth using.</summary>
public static class ColourMeasure
{
    /// <summary>Squares more uneven than this (average ΔE00 inside the square) are flagged as blotchy.</summary>
    public const double BlotchySpread = 3.0;

    /// <summary>
    /// Per-channel gains that make a grey or white reference come out neutral: measured in the
    /// reference's area of the picture. Null if the area is empty or black.
    /// </summary>
    public static (double R, double G, double B)? WhiteBalance(RasterImage img, IReadOnlyList<Vec2> areaPx)
    {
        var (r, g, b, n) = MeanLinear(img, areaPx);
        if (n == 0 || r <= 1e-6 || g <= 1e-6 || b <= 1e-6) return null;
        double y = (r + g + b) / 3;
        return (y / r, y / g, y / b);
    }

    /// <summary>
    /// The colour of each square: <paramref name="mmToPx"/> maps the design (mm) onto the picture.
    /// Only the middle of each square is read (<paramref name="inset"/> = fraction kept), away from its edges.
    /// </summary>
    public static IReadOnlyList<ColourSwatch> Measure(RasterImage img, Homography mmToPx, IReadOnlyList<ColourTestCell> cells,
        (double R, double G, double B)? balance = null, double inset = 0.6)
    {
        var gains = balance ?? (1, 1, 1);
        var result = new List<ColourSwatch>();
        const int N = 10; // N × N sample points per square
        foreach (var cell in cells)
        {
            var c = cell.Bounds.Center;
            double hw = cell.Bounds.Width * inset / 2, hh = cell.Bounds.Height * inset / 2;
            var samples = new List<Lab>();
            double sr = 0, sg = 0, sb = 0;
            int count = 0;
            for (int i = 0; i < N; i++)
                for (int j = 0; j < N; j++)
                {
                    var mm = new Vec2(c.X - hw + 2 * hw * (i + 0.5) / N, c.Y - hh + 2 * hh * (j + 0.5) / N);
                    var px = mmToPx.Apply(mm);
                    var (r, g, b, n) = MeanLinear(img, [px], radius: 2);
                    if (n == 0) continue;
                    r *= gains.R; g *= gains.G; b *= gains.B;
                    samples.Add(ColourScience.LinearRgbToLab(r, g, b));
                    sr += r; sg += g; sb += b;
                    count++;
                }
            if (count == 0) continue;
            var mean = ColourScience.LinearRgbToLab(sr / count, sg / count, sb / count);
            double spread = samples.Average(x => ColourScience.DeltaE2000(x, mean));
            result.Add(new ColourSwatch
            {
                Row = cell.Row, Col = cell.Col, Settings = ProcessSettings.From(cell.Layer),
                L = mean.L, A = mean.A, B = mean.B, Spread = spread, Use = spread <= BlotchySpread,
            });
        }
        return result;
    }

    /// <summary>
    /// The colours worth keeping: the most even squares first, each added only if it differs
    /// from every colour already kept by at least <paramref name="minDeltaE"/> (ΔE00).
    /// Returns the chosen swatches, most even first.
    /// </summary>
    public static IReadOnlyList<ColourSwatch> Distinct(IEnumerable<ColourSwatch> swatches, double minDeltaE = 5)
    {
        var kept = new List<ColourSwatch>();
        foreach (var s in swatches.Where(s => s.Use).OrderBy(s => s.Spread))
            if (kept.All(k => ColourScience.DeltaE2000(k.Colour, s.Colour) >= minDeltaE))
                kept.Add(s);
        return kept;
    }

    /// <summary>Mean linear-light colour of the pixels within <paramref name="radius"/> of the points (or inside the polygon when there are 3 or more).</summary>
    static (double R, double G, double B, int N) MeanLinear(RasterImage img, IReadOnlyList<Vec2> pts, int radius = 0)
    {
        double r = 0, g = 0, b = 0;
        int n = 0;
        void Add(int x, int y)
        {
            if (x < 0 || y < 0 || x >= img.Width || y >= img.Height) return;
            int i = (y * img.Width + x) * 4;
            if (img.Rgba[i + 3] < 128) return;
            r += Lin[img.Rgba[i]];
            g += Lin[img.Rgba[i + 1]];
            b += Lin[img.Rgba[i + 2]];
            n++;
        }
        if (pts.Count >= 3)
        {
            var box = Rect2.Bounds(pts);
            for (int y = (int)Math.Floor(box.MinY); y <= (int)Math.Ceiling(box.MaxY); y++)
                for (int x = (int)Math.Floor(box.MinX); x <= (int)Math.Ceiling(box.MaxX); x++)
                    if (Inside(pts, new Vec2(x + 0.5, y + 0.5))) Add(x, y);
        }
        else
            foreach (var p in pts)
                for (int dy = -radius; dy <= radius; dy++)
                    for (int dx = -radius; dx <= radius; dx++)
                        Add((int)Math.Floor(p.X) + dx, (int)Math.Floor(p.Y) + dy);
        return n == 0 ? (0, 0, 0, 0) : (r / n, g / n, b / n, n);
    }

    static readonly double[] Lin = Enumerable.Range(0, 256).Select(v => ColourScience.ToLinear(v / 255.0)).ToArray();

    static bool Inside(IReadOnlyList<Vec2> poly, Vec2 p)
    {
        bool inside = false;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            if ((poly[i].Y > p.Y) != (poly[j].Y > p.Y) &&
                p.X < (poly[j].X - poly[i].X) * (p.Y - poly[i].Y) / (poly[j].Y - poly[i].Y) + poly[i].X)
                inside = !inside;
        return inside;
    }
}
