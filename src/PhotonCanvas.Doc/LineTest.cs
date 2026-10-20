using PhotonCanvas.Core;
using PhotonCanvas.Core.Imaging;

namespace PhotonCanvas.Doc;

/// <summary>
/// The line test: which settings hide the rows a picture is burned in. A row of the same patch,
/// each burned with a picture layer's settings with one thing changed: the lines spread out or
/// not, one direction or both, closer lines, and more passes each turned (the power lowered to put in the same energy).
/// The patch is a piece of one of the user's pictures, or grey bands with edges: rows often show
/// only where the power changes along a row (burning back and forth, a late change lands on
/// opposite sides in neighbouring rows), which flat greys never do. Each patch has its own layer.
/// </summary>
public static class LineTest
{
    public const string LayerPrefix = "Line test · ";
    const string ShapePrefix = "Line patch · ";
    /// <summary>The greys in the top of the standard patch, side by side: where rows show differs with the tone.</summary>
    public static readonly double[] Greys = [0.25, 0.5, 0.75];
    const int PxPerMm = 20;

    public static bool IsTestLayer(Layer l) => l.Name.StartsWith(LayerPrefix, StringComparison.Ordinal);

    /// <summary>One patch: what changed from the layer's settings, in plain words, and the settings it burns with.</summary>
    public sealed record Variant(string Label, string Detail, Layer Settings);

    /// <summary>The patches for a picture layer's settings: as they are, then one change each.</summary>
    public static IReadOnlyList<Variant> Variants(Layer l)
    {
        var o = new List<Variant>
        {
            new("As set", $"{l.Dpi:0} dpi · {(l.Bidirectional ? "both ways" : "one way")} · {(l.SpreadLines ? "spread" : "in order")}", l),
            l.SpreadLines
                ? new("Not spread", "Lines burned in order, each beside the last", l with { SpreadLines = false })
                : new("Spread out", "Every few lines first, then the ones between", l with { SpreadLines = true }),
            l.Bidirectional
                ? new("One way", "Every row left to right; slower", l with { Bidirectional = false })
                : new("Both ways", "Rows burned back and forth; faster", l with { Bidirectional = true }),
        };
        foreach (double k in new[] { 4 / 3.0, 5 / 3.0 })
        {
            double dpi = Math.Min(2540, Math.Round(l.Dpi * k / 10) * 10);
            if (dpi <= l.Dpi) continue;
            var finer = Finer(l, dpi);
            o.Add(new($"{dpi:0} dpi", $"Lines {25.4 / dpi:0.###} mm apart; power {finer.Laser.Power:0.#}% (same energy)", finer));
        }
        // Less power, more passes, each turned: no row lies on another, so the row pattern averages out.
        foreach (int n in new[] { 2, 3 })
        {
            var turned = Turned(l, n);
            o.Add(new($"{turned.Laser.Passes} passes turned", $"{turned.Laser.Passes} passes at {turned.Laser.Power:0.#}%, each turned {turned.AngleStepDeg:0}° (same energy)", turned));
        }
        return o;
    }

    /// <summary>
    /// The layer at a finer resolution with the same energy: more lines (or dots) per mm, so the
    /// power goes down by the same share, the lightest grey's power too. On wood it comes out
    /// lighter all the same (seen on basswood, Oct 2026): it marks much less at low power, which
    /// also gives more of the pale greys.
    /// </summary>
    public static Layer Finer(Layer l, double dpi)
    {
        double k = l.Dpi / dpi;
        double power = Math.Max(1, Math.Round(l.Laser.Power * k, 1));
        return l with
        {
            Dpi = dpi,
            Laser = l.Laser with { Power = power },
            GrayMinPower = Math.Min(power, Math.Round(l.GrayMinPower * k, 1)),
        };
    }

    /// <summary>
    /// The layer burned in <paramref name="times"/> as many passes, each turned 180° ÷ passes from the
    /// last (2 → 0° and 90°, 3 → 0°, 60°, 120°), with the power cut by the same share: the same energy.
    /// </summary>
    public static Layer Turned(Layer l, int times)
    {
        int passes = Math.Max(1, l.Laser.Passes) * times;
        double power = Math.Max(1, Math.Round(l.Laser.Power / times, 1));
        return l with
        {
            Laser = l.Laser with { Power = power, Passes = passes },
            GrayMinPower = Math.Min(power, Math.Round(l.GrayMinPower / times, 1)),
            AngleStepDeg = Math.Round(180.0 / passes, 1),
            Crosshatch = false,
        };
    }

    /// <summary>Width of the row of patches (mm).</summary>
    public static double Width(int patches, double size) => patches * size + (patches - 1) * Gap(size);

    static double Gap(double size) => Math.Max(3, size / 3);

    /// <summary>
    /// Add the test for <paramref name="source"/> (a picture layer): one layer and one patch per
    /// variant, left to right from <paramref name="origin"/>. Returns the new state and the patches' ids.
    /// </summary>
    /// <param name="sample">What each patch shows: a piece of a picture (see <see cref="PieceOf"/>), or null for grey bands and edges.</param>
    public static (DocState State, IReadOnlyList<Guid> Patches) Add(DocState s, Layer source, double size, Vec2 origin, Piece? sample = null)
    {
        var variants = Variants(source);
        var png = sample?.Png ?? Patch(size);
        var patchMm = sample?.SizeMm ?? new Vec2(size, size);
        var tone = sample?.Tone ?? ToneAdjust.None;
        var ids = new List<Guid>();
        for (int i = 0; i < variants.Count; i++)
        {
            var layer = variants[i].Settings with
            {
                Id = Guid.NewGuid(),
                Name = LayerPrefix + variants[i].Label,
                Mode = LayerMode.Image,
                PassThrough = false,
                ParentId = null,
            };
            s = Ops.AddLayer(s, layer);
            var patch = new ImageShape
            {
                Name = ShapePrefix + source.Name,
                LayerId = layer.Id,
                Source = ImageSource.FromEncoded(png),
                SizeMm = patchMm,
                Tone = tone,
                Transform = Affine.Translate(origin.X + i * (patchMm.X + Gap(size)), origin.Y),
            };
            s = Ops.AddShapes(s, [patch]);
            ids.Add(patch.Id);
        }
        return (s, ids);
    }

    /// <summary>
    /// The standard patch. Top: the greys side by side (flat: rows from spacing or heat show here).
    /// Bottom: things that change along a row, where rows from burning back and forth show as
    /// jagged edges: upright bars, a ring on mid grey, and a ramp from black to white.
    /// </summary>
    static byte[] Patch(double size)
    {
        int n = Math.Max(1, (int)Math.Round(size * PxPerMm));
        var px = new byte[n * n * 4];
        double c = n * 0.5, ringR = n / 6.0, ringW = Math.Max(2, n / 40.0);
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                int third = Math.Min(2, x * 3 / n);
                double v;
                if (y < n * 0.4) v = Greys[third];
                else if (third == 0) v = (x / (PxPerMm * 0.5)) % 2 < 1 ? 0.1 : 0.9; // 0.5 mm bars
                else if (third == 1)
                {
                    double d = Math.Sqrt((x - c) * (x - c) + (y - n * 0.7) * (y - n * 0.7));
                    v = Math.Abs(d - ringR) < ringW ? 0.1 : 0.55;
                }
                else v = (x - 2 * n / 3.0) / (n / 3.0); // ramp
                byte b = (byte)Math.Round(Math.Clamp(v, 0, 1) * 255);
                int k = (y * n + x) * 4;
                px[k] = px[k + 1] = px[k + 2] = b;
                px[k + 3] = 255;
            }
        return new RasterImage(n, n, px).EncodePng();
    }

    /// <summary>A piece of a picture for the patches: its pixels, its size on the bed, and its tone settings.</summary>
    public sealed record Piece(byte[] Png, Vec2 SizeMm, ToneAdjust Tone);

    /// <summary>
    /// The middle of <paramref name="picture"/>, <paramref name="size"/> mm square at the size it's
    /// placed at (or all of it, when it's smaller), keeping its own pixels: burned just as the picture would be.
    /// </summary>
    public static Piece PieceOf(ImageShape picture, double size)
    {
        var src = picture.Source.Pixels;
        double scale = Math.Max(1e-9, picture.Transform.ScaleX);
        double pxPerMm = src.Width / (picture.SizeMm.X * scale);
        int w = Math.Clamp((int)Math.Round(size * pxPerMm), 1, src.Width), h = Math.Clamp((int)Math.Round(size * pxPerMm), 1, src.Height);
        int x0 = (src.Width - w) / 2, y0 = (src.Height - h) / 2;
        var px = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            Array.Copy(src.Rgba, ((y0 + y) * src.Width + x0) * 4, px, y * w * 4, w * 4);
        return new Piece(new RasterImage(w, h, px).EncodePng(), new Vec2(w / pxPerMm, h / pxPerMm), picture.Tone);
    }

    /// <summary>The test's patches in the design, left to right, each with its layer; empty when there's none.</summary>
    public static IReadOnlyList<(Layer Layer, ImageShape Patch)> Patches(DocState s)
    {
        var o = new List<(Layer, ImageShape)>();
        foreach (var l in s.Layers.Where(IsTestLayer))
            foreach (var p in s.ShapesFor(l).OfType<ImageShape>().Where(p => p.Name.StartsWith(ShapePrefix, StringComparison.Ordinal)))
                o.Add((l, p));
        return [.. o.OrderBy(x => x.Item2.Bounds.MinX)];
    }

    /// <summary>The name of the picture layer the test was made from (kept in the patches' names).</summary>
    public static string? SourceName(DocState s) => Patches(s).Select(p => p.Patch.Name[ShapePrefix.Length..]).FirstOrDefault();

    /// <summary>What a patch's layer says, after the prefix ("One way", "400 dpi"…).</summary>
    public static string Label(Layer l) => IsTestLayer(l) ? l.Name[LayerPrefix.Length..] : l.Name;

    /// <summary>
    /// <paramref name="target"/> with the settings the test varies taken from a patch's layer
    /// (resolution, power, lightest power, passes and their angles, spreading, direction); everything else stays.
    /// </summary>
    public static Layer UseOn(Layer target, Layer tested) => target with
    {
        Dpi = tested.Dpi,
        Laser = target.Laser with { Power = tested.Laser.Power, Passes = tested.Laser.Passes },
        GrayMinPower = tested.GrayMinPower,
        SpreadLines = tested.SpreadLines,
        Bidirectional = tested.Bidirectional,
        FillAngleDeg = tested.FillAngleDeg,
        AngleStepDeg = tested.AngleStepDeg,
        Crosshatch = tested.Crosshatch,
        CrossAngleDeg = tested.CrossAngleDeg,
    };

    /// <summary>Take the test out of the design: its patches and their layers.</summary>
    public static DocState Remove(DocState s)
    {
        var layers = s.Layers.Where(IsTestLayer).Select(l => l.Id).ToHashSet();
        return layers.Count == 0 ? s : Ops.DeleteLayers(s, layers, null);
    }
}
