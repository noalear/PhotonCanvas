using PhotonCanvas.Core;

namespace PhotonCanvas.Doc;

/// <summary>A setting a material test grid can sweep.</summary>
public enum TestParam
{
    Power,
    Speed,
    /// <summary>Distance between fill lines, mm.</summary>
    LineInterval,
    /// <summary>Focus offset, mm (+ = focus deeper into the material, head lower).</summary>
    Focus,
    /// <summary>MOPA pulse frequency, kHz.</summary>
    Frequency,
    /// <summary>MOPA pulse width, ns (snapped to the ones the source has).</summary>
    PulseWidth,
    Passes,
}

/// <summary>What a material test grid tries: one setting across, another down, the rest fixed.</summary>
public sealed record MaterialTestOptions
{
    public LayerMode Mode { get; init; } = LayerMode.Fill;
    public LightSource Light { get; init; } = LightSource.Blue;
    public TestParam Across { get; init; } = TestParam.Power;
    public double AcrossMin { get; init; } = 10;
    public double AcrossMax { get; init; } = 90;
    public int AcrossSteps { get; init; } = 5;
    public TestParam Down { get; init; } = TestParam.Speed;
    public double DownMin { get; init; } = 50;
    public double DownMax { get; init; } = 400;
    public int DownSteps { get; init; } = 5;

    // The settings that aren't swept.
    public double Power { get; init; } = 50;
    public double Speed { get; init; } = 200;
    public double LinesPerCm { get; init; } = 100;
    public double FocusMm { get; init; }
    public int FrequencyKhz { get; init; } = 65;
    public int PulseWidthNs { get; init; } = 200;
    public int Passes { get; init; } = 1;

    public double CellMm { get; init; } = 10;
    public double GapMm { get; init; } = 3;
    public double LabelMm { get; init; } = 3;
    public string Font { get; init; } = "Arial";
    /// <summary>Top-left corner of the whole test, mm.</summary>
    public Vec2 Origin { get; init; } = new(10, 10);

    /// <summary>Focus test: focus −3 to +3 mm across (13 steps), two powers down, sparse lines so each line's width shows.</summary>
    public static MaterialTestOptions FocusTest(LightSource light) => new()
    {
        Light = light, Mode = LayerMode.Fill,
        Across = TestParam.Focus, AcrossMin = -3, AcrossMax = 3, AcrossSteps = 13,
        Down = TestParam.Power, DownMin = light == LightSource.Red ? 30 : 20, DownMax = light == LightSource.Red ? 60 : 50, DownSteps = 2,
        Speed = light == LightSource.Red ? 1000 : 200, LinesPerCm = 10, CellMm = 6, GapMm = 2, LabelMm = 2.5,
    };

    /// <summary>Line interval test: lines 0.01–0.1 mm apart across, power down.</summary>
    public static MaterialTestOptions IntervalTest(LightSource light) => new()
    {
        Light = light, Mode = LayerMode.Fill,
        Across = TestParam.LineInterval, AcrossMin = 0.01, AcrossMax = 0.1, AcrossSteps = 10,
        Down = TestParam.Power, DownMin = light == LightSource.Red ? 20 : 20, DownMax = light == LightSource.Red ? 80 : 60, DownSteps = 4,
        Speed = light == LightSource.Red ? 1000 : 200, CellMm = 6, GapMm = 2, LabelMm = 2.5,
    };
}

/// <summary>
/// Builds a test grid: one square per combination of the two swept settings, each on its own
/// layer with those settings, plus labelled axes on a light score layer.
/// </summary>
public static class MaterialTest
{
    /// <summary>n evenly spaced values from min to max, rounded to as few decimals as the step size needs.</summary>
    public static IReadOnlyList<double> Steps(double min, double max, int n)
    {
        if (n <= 1) return [Math.Round(min, 3)];
        double step = Math.Abs(max - min) / (n - 1);
        int digits = step >= 2 ? 0 : step >= 0.2 ? 1 : step >= 0.02 ? 2 : 3;
        return Enumerable.Range(0, n).Select(i => Math.Round(min + (max - min) * i / (n - 1), digits)).ToList();
    }

    /// <summary>Axis heading.</summary>
    public static string Name(TestParam p) => p switch
    {
        TestParam.Power => "Power %",
        TestParam.Speed => "mm/s",
        TestParam.LineInterval => "Line gap mm",
        TestParam.Focus => "Focus mm",
        TestParam.Frequency => "kHz",
        TestParam.PulseWidth => "ns",
        _ => "Passes",
    };

    /// <summary>A value as written on the grid (focus gets its sign).</summary>
    public static string Label(TestParam p, double v) => p switch
    {
        TestParam.Focus => v > 0 ? $"+{v:0.##}" : v < 0 ? $"−{-v:0.##}" : "0",
        TestParam.LineInterval => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
        _ => $"{v:0.#}",
    };

    /// <summary>The values tried along one axis, brought inside what the machine does (no duplicates).</summary>
    public static IReadOnlyList<double> Values(TestParam p, double min, double max, int steps, MachineProfile m)
    {
        var raw = Steps(min, max, Math.Max(1, steps));
        IEnumerable<double> v = p switch
        {
            TestParam.Power => raw.Select(x => Math.Clamp(x, 0, 100)),
            TestParam.Speed => raw.Select(x => Math.Clamp(x, m.MinSpeed, m.MaxSpeed)),
            TestParam.LineInterval => raw.Select(x => Math.Clamp(x, 10 / FillBlock.MaxLinesPerCm, 10)),
            TestParam.Focus => raw.Select(x => Math.Clamp(x, -Job.MaxFocusOffsetMm, Job.MaxFocusOffsetMm)),
            TestParam.Frequency => raw.Select(x => Math.Clamp(Math.Round(x), 1, 4000)),
            TestParam.PulseWidth => raw.Select(x => m.Mopa.Count == 0 ? x : (double)m.Mopa.MinBy(l => Math.Abs(l.PulseWidthNs - x))!.PulseWidthNs),
            _ => raw.Select(x => Math.Clamp(Math.Round(x), 1, 1000)),
        };
        return v.Distinct().ToList();
    }

    /// <summary>The layer settings for one square.</summary>
    public static Layer Square(Layer layer, MaterialTestOptions o, double across, double down, MachineProfile m)
    {
        double power = o.Power, speed = o.Speed, lpcm = o.LinesPerCm, focus = o.FocusMm, freq = o.FrequencyKhz, pulse = o.PulseWidthNs, passes = o.Passes;
        void Set(TestParam p, double v)
        {
            switch (p)
            {
                case TestParam.Power: power = v; break;
                case TestParam.Speed: speed = v; break;
                case TestParam.LineInterval: lpcm = 10 / v; break;
                case TestParam.Focus: focus = v; break;
                case TestParam.Frequency: freq = v; break;
                case TestParam.PulseWidth: pulse = v; break;
                default: passes = v; break;
            }
        }
        Set(o.Across, across);
        Set(o.Down, down);
        var laser = LaserSettings.Blue(Math.Clamp(power, 0, 100), Math.Clamp(speed, m.MinSpeed, m.MaxSpeed)) with { Passes = (int)Math.Clamp(passes, 1, 1000) };
        if (o.Light == LightSource.Red && m.Mopa.Count > 0)
        {
            var lim = m.Mopa.MinBy(x => Math.Abs(x.PulseWidthNs - pulse))!;
            laser = laser with { Light = LightSource.Red, PulseWidthNs = lim.PulseWidthNs, FrequencyKhz = lim.Safe((int)Math.Round(freq)) };
        }
        return layer with
        {
            Laser = laser,
            LinesPerCm = Math.Clamp(lpcm, 1, FillBlock.MaxLinesPerCm),
            FocusOffsetMm = Math.Clamp(focus, -Job.MaxFocusOffsetMm, Job.MaxFocusOffsetMm),
        };
    }

    /// <summary>Add the test to <paramref name="s"/>. Returns the new state and the ids of everything added.</summary>
    public static (DocState State, IReadOnlyList<Guid> Added) Add(DocState s, MaterialTestOptions o, MachineProfile? machine = null)
    {
        var m = machine ?? MachineProfile.F2Ultra;
        var across = Values(o.Across, o.AcrossMin, o.AcrossMax, o.AcrossSteps, m);
        var down = Values(o.Down, o.DownMin, o.DownMax, o.DownSteps, m);
        double pitch = o.CellMm + o.GapMm;
        double labelCol = o.LabelMm * 5 + o.GapMm; // room for "400" or "0.025" plus the axis name
        double headerRow = o.LabelMm * 2 + o.GapMm * 1.5;
        var x0 = o.Origin.X + labelCol;
        var y0 = o.Origin.Y + headerRow;

        var shapes = new List<Shape>();
        int layerIndex = s.Layers.Count, firstTest = s.Layers.Count;
        for (int r = 0; r < down.Count; r++)
            for (int c = 0; c < across.Count; c++)
            {
                string desc = $"{Label(o.Across, across[c])} {Name(o.Across)} · {Label(o.Down, down[r])} {Name(o.Down)}";
                if (o.Across == TestParam.Power && o.Down == TestParam.Speed) desc = $"{across[c]:0.#}% · {down[r]:0.#} mm/s";
                var layer = Square(Layer.Default(layerIndex++, o.Mode), o, across[c], down[r], m) with { Name = $"Test {desc}" };
                s = Ops.AddLayer(s, layer);
                shapes.Add(new PathShape
                {
                    Name = desc,
                    LayerId = layer.Id,
                    Geometry = PathGeometry.Rect(x0 + c * pitch, y0 + r * pitch, o.CellMm, o.CellMm),
                });
            }

        // Labels: lightly scored outlines, gentle settings, at the surface focus.
        var labelLaser = o.Light == LightSource.Red
            ? new LaserSettings { Light = LightSource.Red, Power = 30, Speed = 1000 }
            : LaserSettings.Blue(Math.Clamp(o.Power * 0.6, 5, 100), Math.Clamp(Math.Max(o.Speed, 200), m.MinSpeed, m.MaxSpeed));
        var labels = Layer.Default(layerIndex, LayerMode.Score) with { Name = "Test labels", Laser = labelLaser };
        // Before the squares: cut squares drop out, so nothing may come after them.
        s = Ops.AddLayer(s, labels, firstTest);
        var style = new TextStyle(o.Font, o.LabelMm);
        TextShape Text(string text, double cx, double cy, bool rightAlign = false)
        {
            var t = TextShape.Create(text, style, labels.Id, Affine.Identity);
            var b = t.Bounds;
            if (b.IsEmpty) return t;
            double dx = rightAlign ? cx - b.MaxX : cx - (b.MinX + b.MaxX) / 2;
            return (TextShape)t.With(transform: Affine.Translate(dx, cy - (b.MinY + b.MaxY) / 2));
        }
        double gridW = across.Count * pitch - o.GapMm, gridH = down.Count * pitch - o.GapMm;
        shapes.Add(Text(Name(o.Across), x0 + gridW / 2, o.Origin.Y + o.LabelMm / 2));
        for (int c = 0; c < across.Count; c++)
            shapes.Add(Text(Label(o.Across, across[c]), x0 + c * pitch + o.CellMm / 2, y0 - o.GapMm / 2 - o.LabelMm / 2));
        for (int r = 0; r < down.Count; r++)
            shapes.Add(Text(Label(o.Down, down[r]), x0 - o.GapMm, y0 + r * pitch + o.CellMm / 2, rightAlign: true));
        shapes.Add(Text(Name(o.Down), x0 - o.GapMm, y0 + gridH + o.GapMm + o.LabelMm / 2, rightAlign: true));

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
