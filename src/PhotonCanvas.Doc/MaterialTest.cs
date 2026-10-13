using PhotonCanvas.Core;

namespace PhotonCanvas.Doc;

/// <summary>What a material test grid tries: power across, speed down.</summary>
public sealed record MaterialTestOptions
{
    public LayerMode Mode { get; init; } = LayerMode.Fill;
    public LightSource Light { get; init; } = LightSource.Blue;
    public double PowerMin { get; init; } = 10;
    public double PowerMax { get; init; } = 90;
    public int PowerSteps { get; init; } = 5;
    public double SpeedMin { get; init; } = 50;
    public double SpeedMax { get; init; } = 400;
    public int SpeedSteps { get; init; } = 5;
    public double CellMm { get; init; } = 10;
    public double GapMm { get; init; } = 3;
    public double LinesPerCm { get; init; } = 100;
    public double LabelMm { get; init; } = 3;
    public string Font { get; init; } = "Arial";
    /// <summary>Top-left corner of the whole test, mm.</summary>
    public Vec2 Origin { get; init; } = new(10, 10);
}

/// <summary>
/// Builds a power × speed test grid: one square per combination, each on its
/// own layer with those settings, plus labelled axes on a light score layer.
/// </summary>
public static class MaterialTest
{
    /// <summary>n evenly spaced values from min to max, rounded to whole numbers when the steps are big enough to read easily.</summary>
    public static IReadOnlyList<double> Steps(double min, double max, int n)
    {
        if (n <= 1) return [Math.Round(min, 1)];
        int digits = Math.Abs(max - min) / (n - 1) >= 2 ? 0 : 1;
        return Enumerable.Range(0, n).Select(i => Math.Round(min + (max - min) * i / (n - 1), digits)).ToList();
    }

    /// <summary>Add the test to <paramref name="s"/>. Returns the new state and the ids of everything added.</summary>
    public static (DocState State, IReadOnlyList<Guid> Added) Add(DocState s, MaterialTestOptions o)
    {
        var powers = Steps(Math.Clamp(o.PowerMin, 0, 100), Math.Clamp(o.PowerMax, 0, 100), Math.Max(1, o.PowerSteps));
        var speeds = Steps(o.SpeedMin, o.SpeedMax, Math.Max(1, o.SpeedSteps));
        double pitch = o.CellMm + o.GapMm;
        double labelCol = o.LabelMm * 5 + o.GapMm; // room for "400" plus the axis name
        double headerRow = o.LabelMm * 2 + o.GapMm * 1.5;
        var x0 = o.Origin.X + labelCol;
        var y0 = o.Origin.Y + headerRow;

        var shapes = new List<Shape>();
        int layerIndex = s.Layers.Count;
        LaserSettings Laser(double p, double v) => o.Light == LightSource.Red
            ? new LaserSettings { Light = LightSource.Red, Power = p, Speed = v }
            : LaserSettings.Blue(p, v);

        for (int r = 0; r < speeds.Count; r++)
            for (int c = 0; c < powers.Count; c++)
            {
                var layer = Layer.Default(layerIndex++, o.Mode) with
                {
                    Name = $"Test {powers[c]:0.#}% · {speeds[r]:0.#} mm/s",
                    Laser = Laser(powers[c], speeds[r]),
                    LinesPerCm = o.LinesPerCm,
                };
                s = Ops.AddLayer(s, layer);
                shapes.Add(new PathShape
                {
                    Name = $"{powers[c]:0.#}% {speeds[r]:0.#} mm/s",
                    LayerId = layer.Id,
                    Geometry = PathGeometry.Rect(x0 + c * pitch, y0 + r * pitch, o.CellMm, o.CellMm),
                });
            }

        // Labels: lightly scored outlines, middle power, fastest speed.
        var labels = Layer.Default(layerIndex, LayerMode.Score) with
        {
            Name = "Test labels",
            Laser = Laser(Math.Clamp((o.PowerMin + o.PowerMax) / 2, 5, 100), o.SpeedMax),
        };
        s = Ops.AddLayer(s, labels);
        var style = new TextStyle(o.Font, o.LabelMm);
        TextShape Label(string text, double cx, double cy, bool rightAlign = false)
        {
            var t = TextShape.Create(text, style, labels.Id, Affine.Identity);
            var b = t.Bounds;
            if (b.IsEmpty) return t;
            double dx = rightAlign ? cx - b.MaxX : cx - (b.MinX + b.MaxX) / 2;
            return (TextShape)t.With(transform: Affine.Translate(dx, cy - (b.MinY + b.MaxY) / 2));
        }
        double gridW = powers.Count * pitch - o.GapMm, gridH = speeds.Count * pitch - o.GapMm;
        shapes.Add(Label("Power %", x0 + gridW / 2, o.Origin.Y + o.LabelMm / 2));
        for (int c = 0; c < powers.Count; c++)
            shapes.Add(Label($"{powers[c]:0.#}", x0 + c * pitch + o.CellMm / 2, y0 - o.GapMm / 2 - o.LabelMm / 2));
        for (int r = 0; r < speeds.Count; r++)
            shapes.Add(Label($"{speeds[r]:0.#}", x0 - o.GapMm, y0 + r * pitch + o.CellMm / 2, rightAlign: true));
        shapes.Add(Label("mm/s", x0 - o.GapMm, y0 + gridH + o.GapMm + o.LabelMm / 2, rightAlign: true));

        s = Ops.AddShapes(s, shapes);
        return (s, shapes.Select(x => x.Id).ToList());
    }

    /// <summary>Overall size of the test, mm (for checking it fits the work area).</summary>
    public static Rect2 Bounds(DocState before, MaterialTestOptions o)
    {
        var (after, ids) = Add(before, o);
        var set = ids.ToHashSet();
        return after.Shapes.Where(x => set.Contains(x.Id)).Select(x => x.Bounds).Aggregate(Rect2.Empty, (a, b) => a.Union(b));
    }
}
