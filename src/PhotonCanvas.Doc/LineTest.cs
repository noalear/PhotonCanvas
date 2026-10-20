using PhotonCanvas.Core;
using PhotonCanvas.Core.Imaging;

namespace PhotonCanvas.Doc;

/// <summary>
/// The line test: which settings hide the rows a picture is burned in. A row of the same grey
/// patch (dark, mid and light bands), each burned with a picture layer's settings with one thing
/// changed: the lines spread out or not, one direction or both, and closer lines (with the power
/// lowered to put in the same heat). Each patch has its own layer, so it can be used as it is.
/// </summary>
public static class LineTest
{
    public const string LayerPrefix = "Line test · ";
    const string ShapePrefix = "Line patch · ";
    /// <summary>The greys in each patch, side by side: where rows show differs with the tone.</summary>
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
            o.Add(new($"{dpi:0} dpi", $"Lines {25.4 / dpi:0.###} mm apart; power {finer.Laser.Power:0.#}% (same heat)", finer));
        }
        return o;
    }

    /// <summary>
    /// The layer at a finer resolution with the same heat: more lines (or dots) per mm, so the
    /// power goes down by the same share, the lightest grey's power too.
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

    /// <summary>Width of the row of patches (mm).</summary>
    public static double Width(int patches, double size) => patches * size + (patches - 1) * Gap(size);

    static double Gap(double size) => Math.Max(3, size / 3);

    /// <summary>
    /// Add the test for <paramref name="source"/> (a picture layer): one layer and one patch per
    /// variant, left to right from <paramref name="origin"/>. Returns the new state and the patches' ids.
    /// </summary>
    public static (DocState State, IReadOnlyList<Guid> Patches) Add(DocState s, Layer source, double size, Vec2 origin)
    {
        var variants = Variants(source);
        var png = Patch(size);
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
                SizeMm = new Vec2(size, size),
                Transform = Affine.Translate(origin.X + i * (size + Gap(size)), origin.Y),
            };
            s = Ops.AddShapes(s, [patch]);
            ids.Add(patch.Id);
        }
        return (s, ids);
    }

    /// <summary>The patch picture: the greys side by side, full height (rows run across all of them).</summary>
    static byte[] Patch(double size)
    {
        int n = Math.Max(1, (int)Math.Round(size * PxPerMm));
        var px = new byte[n * n * 4];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                byte v = (byte)Math.Round(Greys[Math.Min(Greys.Length - 1, x * Greys.Length / n)] * 255);
                int k = (y * n + x) * 4;
                px[k] = px[k + 1] = px[k + 2] = v;
                px[k + 3] = 255;
            }
        return new RasterImage(n, n, px).EncodePng();
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
    /// (resolution, power, lightest power, spreading, direction); everything else stays.
    /// </summary>
    public static Layer UseOn(Layer target, Layer tested) => target with
    {
        Dpi = tested.Dpi,
        Laser = target.Laser with { Power = tested.Laser.Power },
        GrayMinPower = tested.GrayMinPower,
        SpreadLines = tested.SpreadLines,
        Bidirectional = tested.Bidirectional,
    };

    /// <summary>Take the test out of the design: its patches and their layers.</summary>
    public static DocState Remove(DocState s)
    {
        var layers = s.Layers.Where(IsTestLayer).Select(l => l.Id).ToHashSet();
        return layers.Count == 0 ? s : Ops.DeleteLayers(s, layers, null);
    }
}
