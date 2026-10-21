using PhotonCanvas.Core;
using PhotonCanvas.Doc.Geometry;

namespace PhotonCanvas.Doc;

/// <summary>Settings for the graphene test.</summary>
public sealed record GrapheneTestOptions
{
    public IReadOnlyList<double> Powers { get; init; } = [30, 45, 60, 75];
    public IReadOnlyList<double> Speeds { get; init; } = [50, 100, 200];
    /// <summary>Focus raised this far off the surface (mm): a wider, softer spot, as laser graphene usually wants.</summary>
    public double DefocusMm { get; init; } = 1.5;
    public double LinesPerCm { get; init; } = 150;
    /// <summary>The strip between the pads: its length and width (mm). Resistance is read across it.</summary>
    public double StripMm { get; init; } = 15;
    public double StripWidthMm { get; init; } = 3;
    /// <summary>The contact pads at each end, square (mm): room for a meter's probes.</summary>
    public double PadMm { get; init; } = 5;
    public Vec2 Origin { get; init; } = new(20, 20);
    public string Font { get; init; } = "Arial";
}

/// <summary>
/// Laser-induced graphene (experimental): burning cork, wood or polyimide can turn its surface into
/// conductive carbon, for touch pads and simple sensors. Whether the blue diode does it on a given
/// material is what this tests: a grid of dumbbells (two contact pads and the strip between),
/// power across and speed down, the focus raised for a softer spot. Measure each strip's
/// resistance pad to pad with a multimeter: the lowest is the best setting.
/// </summary>
public static class GrapheneTest
{
    public const string LayerPrefix = "Graphene · ";

    public static bool IsTestLayer(Layer l) => l.Name.StartsWith(LayerPrefix, StringComparison.Ordinal);

    /// <summary>One dumbbell, its left pad's top-left at (x, y): pad, strip, pad, as one shape.</summary>
    public static PathGeometry Dumbbell(double x, double y, GrapheneTestOptions o)
    {
        double pad = o.PadMm, mid = y + pad / 2;
        return ShapeGeometry.Union([
            PathGeometry.Rect(x, y, pad, pad),
            PathGeometry.Rect(x + pad - 0.2, mid - o.StripWidthMm / 2, o.StripMm + 0.4, o.StripWidthMm),
            PathGeometry.Rect(x + pad + o.StripMm, y, pad, pad),
        ]);
    }

    /// <summary>The test: a layer per cell (its own power and speed, focus raised), labels on a light score layer. Returns the state and the cells' layers, in order (speed by speed, power by power).</summary>
    public static (DocState State, IReadOnlyList<Guid> Cells) Add(DocState s, GrapheneTestOptions o)
    {
        double cellW = o.PadMm * 2 + o.StripMm, rowH = o.PadMm + 4, colGap = 6;
        var marks = Layer.Default(s.Layers.Count, LayerMode.Score) with { Name = LayerPrefix + "labels", Laser = LaserSettings.Blue(10, 200) };
        s = Ops.AddLayer(s, marks);
        var cells = new List<Guid>();
        var shapes = new List<Shape>();
        for (int r = 0; r < o.Speeds.Count; r++)
            for (int c = 0; c < o.Powers.Count; c++)
            {
                double x = o.Origin.X + 14 + c * (cellW + colGap), y = o.Origin.Y + 6 + r * (rowH + 2);
                var layer = Layer.Default(s.Layers.Count, LayerMode.Fill) with
                {
                    Name = $"{LayerPrefix}{o.Powers[c]:0.#}% {o.Speeds[r]:0.#} mm/s",
                    Laser = LaserSettings.Blue(o.Powers[c], o.Speeds[r]),
                    LinesPerCm = o.LinesPerCm,
                    FocusOffsetMm = -Math.Abs(o.DefocusMm),
                    AngleStepDeg = 0,
                };
                s = Ops.AddLayer(s, layer);
                cells.Add(layer.Id);
                shapes.Add(new PathShape { Name = $"Strip {r * o.Powers.Count + c + 1}", LayerId = layer.Id, Geometry = Dumbbell(x, y, o) });
                // Its number under the strip, to write the reading against.
                shapes.Add(Label($"{r * o.Powers.Count + c + 1}", marks.Id, x + cellW / 2, y + o.PadMm + 2, o.Font));
            }
        // Power along the top, speed down the side.
        for (int c = 0; c < o.Powers.Count; c++) shapes.Add(Label($"{o.Powers[c]:0.#}%", marks.Id, o.Origin.X + 14 + c * (cellW + colGap) + cellW / 2, o.Origin.Y + 2, o.Font));
        for (int r = 0; r < o.Speeds.Count; r++) shapes.Add(Label($"{o.Speeds[r]:0.#}", marks.Id, o.Origin.X + 6, o.Origin.Y + 6 + r * (rowH + 2) + o.PadMm / 2, o.Font));
        return (Ops.AddShapes(s, shapes), cells);
    }

    static Shape Label(string text, Guid layer, double cx, double cy, string font)
    {
        var t = TextShape.Create(text, new TextStyle(font, 2.2), layer, Affine.Identity);
        var b = t.Bounds;
        return b.IsEmpty ? t : t.With(transform: Affine.Translate(cx - b.Center.X, cy - b.Center.Y));
    }

    /// <summary>The cells' layers in the design, in the order they were made.</summary>
    public static IReadOnlyList<Layer> Cells(DocState s) => [.. s.Layers.Where(l => IsTestLayer(l) && l.Mode == LayerMode.Fill)];

    /// <summary>
    /// The best cell from resistances read pad to pad (Ω; null = not read, or open circuit): the
    /// lowest. Its sheet resistance (Ω per square: resistance × width ÷ length) says how good it is.
    /// </summary>
    public static (int Index, double Ohms, double OhmsPerSquare)? Best(IReadOnlyList<double?> ohms, GrapheneTestOptions o)
    {
        int best = -1;
        for (int i = 0; i < ohms.Count; i++)
            if (ohms[i] is > 0 && (best < 0 || ohms[i] < ohms[best])) best = i;
        return best < 0 ? null : (best, ohms[best]!.Value, ohms[best]!.Value * o.StripWidthMm / o.StripMm);
    }
}
