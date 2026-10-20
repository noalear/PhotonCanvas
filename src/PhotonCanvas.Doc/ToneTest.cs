using PhotonCanvas.Core;
using PhotonCanvas.Core.Imaging;

namespace PhotonCanvas.Doc;

/// <summary>
/// The tone test: a row of grey patches, black to white, burned with a picture layer's own
/// settings (and no correction). The camera reads how light each came out, which gives the
/// material's <see cref="ToneCurve"/>; set on the layer, every picture's greys are corrected
/// through it. The patches are found from the strip wherever it has been moved to.
/// </summary>
public static class ToneTest
{
    public const string LayerPrefix = "Tone test · ";
    const string ShapePrefix = "Tone wedge · ";
    /// <summary>The gap between patches, as a share of a patch (bare material between them, to compare with).</summary>
    const double GapShare = 0.5;
    const int PxPerMm = 10;

    public static bool IsTestLayer(Layer l) => l.Name.StartsWith(LayerPrefix, StringComparison.Ordinal);

    /// <summary>The greys tried, black (0) to white (1).</summary>
    public static IReadOnlyList<double> Levels(int steps)
    {
        steps = Math.Clamp(steps, 3, 41);
        return [.. Enumerable.Range(0, steps).Select(i => (double)i / (steps - 1))];
    }

    /// <summary>Width of the strip for this many patches of this size (mm).</summary>
    public static double Width(int steps, double patchMm) => steps * patchMm * (1 + GapShare) - patchMm * GapShare;

    /// <summary>
    /// Add the test: a copy of <paramref name="settings"/> (a picture layer; its tone correction off)
    /// holding the strip, top-left at <paramref name="origin"/>. Returns the new state and the strip's id.
    /// </summary>
    public static (DocState State, Guid Strip) Add(DocState s, Layer settings, int steps, double patchMm, Vec2 origin)
    {
        var levels = Levels(steps);
        double gap = patchMm * GapShare, width = Width(levels.Count, patchMm);
        int w = Math.Max(1, (int)Math.Round(width * PxPerMm)), h = Math.Max(1, (int)Math.Round(patchMm * PxPerMm));
        var px = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                double mm = (x + 0.5) / PxPerMm;
                int patch = (int)Math.Floor(mm / (patchMm + gap));
                bool inPatch = patch < levels.Count && mm - patch * (patchMm + gap) < patchMm;
                byte v = inPatch ? (byte)Math.Round(levels[patch] * 255) : (byte)255;
                int k = (y * w + x) * 4;
                px[k] = px[k + 1] = px[k + 2] = v;
                px[k + 3] = 255;
            }
        var png = new RasterImage(w, h, px).EncodePng();
        var layer = settings with
        {
            Id = Guid.NewGuid(),
            Name = LayerPrefix + settings.Name,
            Mode = LayerMode.Image,
            MaterialTone = null, // the test measures the material as it is
            ParentId = null,
        };
        s = Ops.AddLayer(s, layer);
        var strip = new ImageShape
        {
            Name = $"{ShapePrefix}{levels.Count} steps",
            LayerId = layer.Id,
            Source = ImageSource.FromEncoded(png),
            SizeMm = new Vec2(width, patchMm),
            Transform = Affine.Translate(origin.X, origin.Y),
        };
        return (Ops.AddShapes(s, [strip]), strip.Id);
    }

    /// <summary>The test strip in the design, if there is one (the newest).</summary>
    public static ImageShape? Strip(DocState s)
    {
        var layers = s.Layers.Where(IsTestLayer).Select(l => l.Id).ToHashSet();
        return s.Shapes.OfType<ImageShape>().LastOrDefault(x => layers.Contains(x.LayerId) && x.Name.StartsWith(ShapePrefix, StringComparison.Ordinal));
    }

    /// <summary>How many patches a strip has (from its name).</summary>
    public static int Steps(ImageShape strip) =>
        int.TryParse(strip.Name[ShapePrefix.Length..].Split(' ')[0], out int n) ? n : 11;

    /// <summary>Each patch's grey and where it is on the bed now (the strip may have been moved or scaled).</summary>
    public static IReadOnlyList<(double Level, Rect2 Bed)> Patches(ImageShape strip)
    {
        var levels = Levels(Steps(strip));
        double patch = strip.SizeMm.X / (levels.Count * (1 + GapShare) - GapShare), gap = patch * GapShare;
        var o = new List<(double, Rect2)>();
        for (int i = 0; i < levels.Count; i++)
        {
            double x0 = i * (patch + gap);
            Vec2[] corners = [new(x0, 0), new(x0 + patch, 0), new(x0 + patch, strip.SizeMm.Y), new(x0, strip.SizeMm.Y)];
            o.Add((levels[i], Rect2.Bounds(corners.Select(strip.Transform.Apply))));
        }
        return o;
    }

    /// <summary>
    /// Read the patches from a photo (covering <paramref name="area"/> at <paramref name="mmPerPx"/>):
    /// each one's brightness, the bare material's, and the colours of bare and fully burned (for previews).
    /// Null when the photo doesn't show the strip.
    /// </summary>
    public static ToneReading? Read(ImageShape strip, RasterImage photo, Rect2 area, double mmPerPx)
    {
        var patches = Patches(strip);
        var readings = patches.Select(p => TestGridReader.Read(photo, area, mmPerPx, p.Bed)).ToList();
        if (readings.Any(r => r is null)) return null;
        double bare = readings.Average(r => r!.Around);
        var curve = ToneCurve.FromReadings([.. patches.Select(p => p.Level)], [.. readings.Select(r => r!.Inside)], bare);
        var black = patches.Select((p, i) => (p.Level, i)).MinBy(x => x.Level).i;
        uint burnColour = MeanColour(photo, area, mmPerPx, Inner(patches[black].Bed)), bareColour = MeanColour(photo, area, mmPerPx, Ring(patches));
        // The colours go with the curve, so pictures can be shown as they'll burn anywhere it's used.
        curve = curve with { BareColour = bareColour, BurnColour = burnColour };
        return new ToneReading(curve, [.. readings.Select(r => r!.Inside)], bare, burnColour, bareColour);
    }

    /// <summary>
    /// Why a reading can't be right, or null when it looks sound. The white patch is never burned
    /// (white isn't visited), so it must read close to bare material; and a lighter grey can't burn
    /// clearly darker than a darker one. When either fails, the camera was reading the wrong spots
    /// (the view doesn't line up with the strip, a shadow, or an older burn under it), and a curve
    /// made from it would squeeze every picture into a few dark tones.
    /// </summary>
    public static string? Problem(ToneReading r)
    {
        var pts = r.Curve.Points.OrderBy(p => p.Input).ToList();
        if (pts.Count > 0 && pts[^1].Input >= 0.999 && pts[^1].Lightness < 0.8)
            return $"The white patch isn't burned at all, but it reads only {pts[^1].Lightness * 100:0}% as light as bare wood, so the camera is reading the wrong spots. " +
                   "Check that the photo lines up with the strip (calibrate the camera if it doesn't), that nothing shades it and that it's on clean wood, then update the photo.";
        for (int i = 1; i < pts.Count; i++)
        {
            double darkest = pts.Take(i).Max(p => p.Lightness);
            if (pts[i].Lightness < darkest - 0.12)
                return $"Grey {pts[i].Input * 100:0}% reads darker ({pts[i].Lightness * 100:0}% light) than a darker grey ({darkest * 100:0}%), which a burn can't do: the camera is reading the wrong spots. " +
                       "Check that the photo lines up with the strip, that nothing shades it and that it's on clean wood, then update the photo.";
        }
        return null;
    }

    static Rect2 Inner(Rect2 r) => new(r.MinX + r.Width * 0.2, r.MinY + r.Height * 0.2, r.MaxX - r.Width * 0.2, r.MaxY - r.Height * 0.2);

    /// <summary>A band of bare material just above the strip.</summary>
    static Rect2 Ring(IReadOnlyList<(double Level, Rect2 Bed)> patches)
    {
        var all = patches.Select(p => p.Bed).Aggregate(Rect2.Empty, (a, b) => a.Union(b));
        return new Rect2(all.MinX, all.MinY - 2.5, all.MaxX, all.MinY - 1);
    }

    /// <summary>Average colour (0xRRGGBB) of the photo inside a bed rectangle.</summary>
    public static uint MeanColour(RasterImage photo, Rect2 area, double mmPerPx, Rect2 r)
    {
        int x0 = Math.Max(0, (int)Math.Floor((r.MinX - area.MinX) / mmPerPx)), x1 = Math.Min(photo.Width - 1, (int)Math.Ceiling((r.MaxX - area.MinX) / mmPerPx));
        int y0 = Math.Max(0, (int)Math.Floor((r.MinY - area.MinY) / mmPerPx)), y1 = Math.Min(photo.Height - 1, (int)Math.Ceiling((r.MaxY - area.MinY) / mmPerPx));
        double sr = 0, sg = 0, sb = 0;
        int n = 0;
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                int k = (y * photo.Width + x) * 4;
                sr += photo.Rgba[k]; sg += photo.Rgba[k + 1]; sb += photo.Rgba[k + 2];
                n++;
            }
        if (n == 0) return 0x808080;
        return (uint)Math.Round(sr / n) << 16 | (uint)Math.Round(sg / n) << 8 | (uint)Math.Round(sb / n);
    }

    /// <summary>
    /// A picture as it would come out on the material: each tone through the material's response
    /// (corrected first, or not), coloured between the burned and bare colours read off the photo.
    /// </summary>
    public static RasterImage Simulate(RasterImage picture, ToneCurve curve, uint bareColour, uint burnColour, bool corrected)
    {
        var px = new byte[picture.Width * picture.Height * 4];
        static byte R(uint c) => (byte)(c >> 16);
        static byte G(uint c) => (byte)(c >> 8);
        static byte B(uint c) => (byte)c;
        for (int i = 0; i < picture.Width * picture.Height; i++)
        {
            int k = i * 4;
            double lum = (0.299 * picture.Rgba[k] + 0.587 * picture.Rgba[k + 1] + 0.114 * picture.Rgba[k + 2]) / 255;
            // Transparent shows as bare material.
            lum = lum * picture.Rgba[k + 3] / 255 + (255 - picture.Rgba[k + 3]) / 255.0;
            double sent = corrected ? curve.Correct(lum) : lum;
            double light = curve.Response(sent);
            px[k] = (byte)Math.Round(R(burnColour) + (R(bareColour) - R(burnColour)) * light);
            px[k + 1] = (byte)Math.Round(G(burnColour) + (G(bareColour) - G(burnColour)) * light);
            px[k + 2] = (byte)Math.Round(B(burnColour) + (B(bareColour) - B(burnColour)) * light);
            px[k + 3] = 255;
        }
        return new RasterImage(picture.Width, picture.Height, px);
    }
}

/// <summary>What a tone test came out as: the curve, each patch's brightness, the bare material's, and the colours of fully burned and bare.</summary>
public sealed record ToneReading(ToneCurve Curve, IReadOnlyList<double> Patches, double Bare, uint BurnColour, uint BareColour);
