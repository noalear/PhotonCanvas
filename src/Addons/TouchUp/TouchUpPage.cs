using System.Collections.Immutable;
using System.Text.RegularExpressions;
using PhotonCanvas.Core;
using PhotonCanvas.Core.Imaging;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Tracing;

namespace PhotonCanvas.Addon.TouchUp;

/// <summary>
/// The touch-up as a page of its own, after the design: one layer per engraving layer that came
/// out too light, with that layer's settings, holding only what needs going over again. Its own
/// page, so it's its own job (sent with Ship It as usual) and the design is left as it was.
/// </summary>
public static partial class TouchUpPage
{
    public const string PagePrefix = "Touch-up ";

    [GeneratedRegex(@"^Touch-up (\d+)\b")]
    private static partial Regex RoundName();

    /// <summary>Whether a page is a touch-up page (by its name).</summary>
    public static bool IsTouchUpPage(string pageName) => RoundName().IsMatch(pageName);

    /// <summary>The next round's number: one more than the highest touch-up page there is.</summary>
    public static int NextRound(DocState s) =>
        1 + Pages.Names(s).Select(n => RoundName().Match(n)).Where(m => m.Success).Select(m => int.Parse(m.Groups[1].Value)).DefaultIfEmpty(0).Max();

    /// <summary>
    /// The page to compare photos with: the one on screen, or, when that's a touch-up page, the
    /// nearest page before it that isn't (the design it touches up). Null when there's none.
    /// </summary>
    public static DocState? DesignOf(DocState s)
    {
        if (!IsTouchUpPage(s.PageName)) return s;
        var names = Pages.Names(s);
        for (int i = s.PageIndex - 1; i >= 0; i--)
            if (!IsTouchUpPage(names[i])) return Pages.SwitchTo(s, i);
        for (int i = s.PageIndex + 1; i < names.Count; i++)
            if (!IsTouchUpPage(names[i])) return Pages.SwitchTo(s, i);
        return null;
    }

    /// <summary>
    /// Add the touch-up page (shown) after the page on screen. Pictures come back as a picture of
    /// how much more each spot needs (dithered, so a little short gets a few dots); engraved areas
    /// come back as the outlines of the light patches, filled at the layer's settings with the power
    /// scaled by <see cref="TouchUpOptions.Strength"/>. Null when nothing needs touching up.
    /// </summary>
    public static DocState? Add(DocState s, TouchUpResult r, TouchUpOptions o, int round)
    {
        var g = r.Grid;
        var layers = new List<Layer>();
        var shapes = new List<Shape>();
        int colour = 0;
        foreach (var lr in r.Layers.Where(l => l.NeedsTouchUp))
        {
            var src = lr.Layer;
            var layer = src with
            {
                Id = Guid.NewGuid(),
                ParentId = null,
                Name = $"{PagePrefix}{round}: {src.Name}",
                Output = true,
                Visible = true,
                Locked = false,
                Color = Layer.Palette[(colour++ * 5 + 6) % Layer.Palette.Count],
                // The dose is the amount wanted, already: the material's tone correction would bend it again.
                MaterialTone = null,
                PassThrough = false,
                RunCount = 0,
            };
            if (src.Mode == LayerMode.Image)
            {
                // A threshold would drop every spot less than half short: dots spread the dose instead.
                if (layer.Dither == DitherMethod.Threshold) layer = layer with { Dither = DitherMethod.Jarvis, RampMm = 0 };
                shapes.Add(DosePicture(lr.Dose, g, layer.Id, $"Touch-up {round}"));
            }
            else
            {
                var geo = DoseOutline(lr.Dose, g);
                if (geo.Subpaths.IsEmpty) continue;
                double power = Math.Round(Math.Clamp(src.Laser.Power * o.Strength, 1, 100), 1);
                layer = layer with { Laser = layer.Laser with { Power = power }, OutlineAfter = false };
                shapes.Add(new PathShape { Name = $"Touch-up {round}", LayerId = layer.Id, Geometry = geo });
            }
            layers.Add(layer);
        }
        if (layers.Count == 0) return null;
        var page = new DesignPage($"{PagePrefix}{round}", [.. layers], [.. shapes], []);
        return Pages.Insert(s, [page]);
    }

    /// <summary>The dose as a picture over the grid: darker = more to add, see-through where nothing is added.</summary>
    public static ImageShape DosePicture(float[] dose, CellGrid g, Guid layerId, string name)
    {
        var rgba = new byte[g.Count * 4];
        for (int i = 0; i < g.Count; i++)
        {
            byte v = (byte)Math.Clamp((int)Math.Round(255 * (1 - dose[i])), 0, 255);
            rgba[i * 4] = rgba[i * 4 + 1] = rgba[i * 4 + 2] = v;
            rgba[i * 4 + 3] = dose[i] > 0 ? (byte)255 : (byte)0;
        }
        var png = new RasterImage(g.W, g.H, rgba).EncodePng();
        return new ImageShape
        {
            Name = name,
            LayerId = layerId,
            Source = ImageSource.FromEncoded(png),
            SizeMm = new Vec2(g.W * g.Pitch, g.H * g.Pitch),
            Transform = Affine.Translate(g.Area.MinX, g.Area.MinY),
        };
    }

    /// <summary>The outlines of the patches that need more (bed mm), smooth, holes kept.</summary>
    public static PathGeometry DoseOutline(float[] dose, CellGrid g)
    {
        var mask = new bool[g.Count];
        for (int i = 0; i < g.Count; i++) mask[i] = dose[i] > 0;
        var traced = Tracer.TraceMask(mask, g.W, g.H, new TraceOptions { Smoothness = 1, CornerAngle = 60, MinAreaPx = 2 });
        return traced.Transform(Affine.Scale(g.Pitch, g.Pitch).Then(Affine.Translate(g.Area.MinX, g.Area.MinY)));
    }

    /// <summary>
    /// Pictures of the comparison, at the grid's size: what the design wants (grey), what the photo
    /// found (grey, lined up), and the photo with the too-light parts in red, stronger where more is missing.
    /// </summary>
    public static (RasterImage Wanted, RasterImage Found, RasterImage TooLight) Pictures(TouchUpResult r)
    {
        var g = r.Grid;
        int n = g.Count;
        var want = new byte[n * 4];
        var found = new byte[n * 4];
        var light = new byte[n * 4];
        // The photo stretched to use the whole range, so faint burns show.
        var vals = r.Found.Where(f => !float.IsNaN(f)).Select(f => (double)f).ToList();
        double lo = vals.Count > 0 ? TouchUpAnalysis.Percentile(vals, 0.01) : 0, hi = vals.Count > 0 ? TouchUpAnalysis.Percentile(vals, 0.99) : 1;
        if (hi - lo < 1e-3) hi = lo + 1e-3;
        for (int i = 0; i < n; i++)
        {
            byte t = (byte)Math.Clamp((int)Math.Round(255 * (1 - r.Target[i])), 0, 255);
            want[i * 4] = want[i * 4 + 1] = want[i * 4 + 2] = t;
            want[i * 4 + 3] = 255;
            float f = r.Found[i];
            byte p = float.IsNaN(f) ? (byte)128 : (byte)Math.Clamp((int)Math.Round(255 * (f - lo) / (hi - lo)), 0, 255);
            found[i * 4] = found[i * 4 + 1] = found[i * 4 + 2] = p;
            found[i * 4 + 3] = 255;
            double d = Math.Clamp(r.Deficit[i] * 2.5, 0, 1);
            light[i * 4] = (byte)Math.Round(p * (1 - d) + 230 * d);
            light[i * 4 + 1] = (byte)Math.Round(p * (1 - d) + 40 * d);
            light[i * 4 + 2] = (byte)Math.Round(p * (1 - d) + 40 * d);
            light[i * 4 + 3] = 255;
        }
        return (new RasterImage(g.W, g.H, want), new RasterImage(g.W, g.H, found), new RasterImage(g.W, g.H, light));
    }
}
