using PhotonCanvas.Core;

namespace PhotonCanvas.Doc.Colour;

/// <summary>A MOPA setting a colour test can sweep.</summary>
public enum ColourParam { Power, Speed, LinesPerCm, FrequencyKhz, PulseWidthNs }

/// <summary>
/// What a colour test grid tries on metal with the MOPA laser: one setting across, another
/// down, the rest fixed. Each square is filled with its own settings.
/// </summary>
public sealed record ColourTestOptions
{
    // Defaults follow what has worked on stainless with MOPA sources (Applied Science's published
    // settings): short 6 ns pulses at 800 mm/s, lines 0.002 mm apart, with frequency doing most of
    // the work and power fine-tuning it. Power is a wider range here: our source is 60 W, not 100.
    public ColourParam Across { get; init; } = ColourParam.FrequencyKhz;
    public double AcrossMin { get; init; } = 200;
    public double AcrossMax { get; init; } = 1500;
    public int AcrossSteps { get; init; } = 8;
    public ColourParam Down { get; init; } = ColourParam.Power;
    public double DownMin { get; init; } = 20;
    public double DownMax { get; init; } = 60;
    public int DownSteps { get; init; } = 6;

    // The settings that aren't swept.
    public double Power { get; init; } = 30;
    public double Speed { get; init; } = 800;
    public double LinesPerCm { get; init; } = 5000;
    public int FrequencyKhz { get; init; } = 500;
    public int PulseWidthNs { get; init; } = 6;

    public double CellMm { get; init; } = 5;
    public double GapMm { get; init; } = 2;
    public double LabelMm { get; init; } = 2.5;
    public string Font { get; init; } = "Arial";
    /// <summary>Top-left corner of the whole test, mm.</summary>
    public Vec2 Origin { get; init; } = new(10, 10);
    /// <summary>Settings for the axis labels (a light MOPA mark).</summary>
    public double LabelPower { get; init; } = 30;
    public double LabelSpeed { get; init; } = 1000;
}

/// <summary>One square of a colour test: where it is in the grid and what it was marked with.</summary>
public sealed record ColourTestCell(int Row, int Col, Layer Layer, Rect2 Bounds);

/// <summary>
/// Builds a colour test grid for the MOPA laser. Every square is its own Fill layer, named
/// "Colour test R3 C5", and the squares run from the least energy per area to the most, so
/// the heat builds up gradually. Values that the source can't do (pulse widths it doesn't
/// have, frequencies above a pulse width's limit) are brought to the nearest it can.
/// </summary>
public static class ColourTest
{
    public const string LayerPrefix = "Colour test R";
    public const string LabelLayerName = "Colour test labels";

    public static string Name(ColourParam p) => p switch
    {
        ColourParam.Power => "Power %",
        ColourParam.Speed => "Speed mm/s",
        ColourParam.LinesPerCm => "Lines/cm",
        ColourParam.FrequencyKhz => "Frequency kHz",
        _ => "Pulse ns",
    };

    /// <summary>The values tried along one axis (pulse widths snap to the ones the source has; no duplicates).</summary>
    public static IReadOnlyList<double> Values(ColourParam p, double min, double max, int steps, MachineProfile m)
    {
        var raw = MaterialTest.Steps(min, max, Math.Max(1, steps));
        if (p == ColourParam.PulseWidthNs)
            return raw.Select(v => (double)m.Mopa.MinBy(x => Math.Abs(x.PulseWidthNs - v))!.PulseWidthNs).Distinct().ToList();
        return raw.Select(v => p switch
        {
            ColourParam.Power => Math.Clamp(v, 0, 100),
            ColourParam.Speed => Math.Clamp(v, m.MinSpeed, m.MaxSpeed),
            ColourParam.LinesPerCm => Math.Clamp(v, 1, FillBlock.MaxLinesPerCm),
            ColourParam.FrequencyKhz => Math.Clamp(Math.Round(v), 1, 4000),
            _ => v,
        }).Distinct().ToList();
    }

    /// <summary>The settings for one square.</summary>
    public static (LaserSettings Laser, double LinesPerCm) Settings(ColourTestOptions o, double across, double down, MachineProfile m)
    {
        double power = o.Power, speed = o.Speed, lpcm = o.LinesPerCm;
        double freq = o.FrequencyKhz, pulse = o.PulseWidthNs;
        void Set(ColourParam p, double v)
        {
            switch (p)
            {
                case ColourParam.Power: power = v; break;
                case ColourParam.Speed: speed = v; break;
                case ColourParam.LinesPerCm: lpcm = v; break;
                case ColourParam.FrequencyKhz: freq = v; break;
                default: pulse = v; break;
            }
        }
        Set(o.Across, across);
        Set(o.Down, down);
        var lim = m.Mopa.MinBy(x => Math.Abs(x.PulseWidthNs - pulse))!;
        var laser = new LaserSettings
        {
            Light = LightSource.Red,
            Power = Math.Clamp(power, 0, 100),
            Speed = Math.Clamp(speed, m.MinSpeed, m.MaxSpeed),
            PulseWidthNs = lim.PulseWidthNs,
            FrequencyKhz = lim.Safe((int)Math.Round(freq)),
        };
        return (laser, Math.Clamp(lpcm, 1, FillBlock.MaxLinesPerCm));
    }

    /// <summary>Energy per area, relative (power ÷ (speed × line spacing)): the order the squares run in.</summary>
    public static double Energy(LaserSettings l, double linesPerCm) => l.Power / Math.Max(1e-9, l.Speed * 10 / linesPerCm);

    /// <summary>Add the test to <paramref name="s"/>. Returns the new state and the ids of everything added.</summary>
    public static (DocState State, IReadOnlyList<Guid> Added) Add(DocState s, ColourTestOptions o, MachineProfile? machine = null)
    {
        var m = machine ?? MachineProfile.F2Ultra;
        var across = Values(o.Across, o.AcrossMin, o.AcrossMax, o.AcrossSteps, m);
        var down = Values(o.Down, o.DownMin, o.DownMax, o.DownSteps, m);
        double pitch = o.CellMm + o.GapMm;
        double labelCol = o.LabelMm * 4 + o.GapMm;
        double headerRow = o.LabelMm * 2 + o.GapMm * 1.5;
        var x0 = o.Origin.X + labelCol;
        var y0 = o.Origin.Y + headerRow;

        var cells = new List<(int R, int C, LaserSettings Laser, double Lpcm)>();
        for (int r = 0; r < down.Count; r++)
            for (int c = 0; c < across.Count; c++)
            {
                var (laser, lpcm) = Settings(o, across[c], down[r], m);
                cells.Add((r, c, laser, lpcm));
            }

        var shapes = new List<Shape>();
        int layerIndex = s.Layers.Count;
        // Least energy first: the plate warms gradually instead of a hot square cooking its neighbours' starting point.
        foreach (var (r, c, laser, lpcm) in cells.OrderBy(x => Energy(x.Laser, x.Lpcm)))
        {
            var layer = Layer.Default(layerIndex++, LayerMode.Fill) with
            {
                Name = $"{LayerPrefix}{r + 1} C{c + 1}",
                Laser = laser,
                LinesPerCm = lpcm,
                FillAngleDeg = 0,
                Crosshatch = false,
                OverscanMm = 0,
            };
            s = Ops.AddLayer(s, layer);
            shapes.Add(new PathShape
            {
                Name = $"R{r + 1}C{c + 1}",
                LayerId = layer.Id,
                Geometry = PathGeometry.Rect(x0 + c * pitch, y0 + r * pitch, o.CellMm, o.CellMm),
            });
        }

        // Axis labels, lightly marked.
        var lim = m.MopaLimitFor(30) ?? m.Mopa[0];
        var labels = Layer.Default(layerIndex, LayerMode.Score) with
        {
            Name = LabelLayerName,
            Laser = new LaserSettings { Light = LightSource.Red, Power = o.LabelPower, Speed = o.LabelSpeed, PulseWidthNs = lim.PulseWidthNs, FrequencyKhz = lim.DefaultKhz },
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
        static string Fmt(double v) => v >= 100 ? $"{v:0}" : $"{v:0.#}";
        double gridW = across.Count * pitch - o.GapMm, gridH = down.Count * pitch - o.GapMm;
        shapes.Add(Label(Name(o.Across), x0 + gridW / 2, o.Origin.Y + o.LabelMm / 2));
        for (int c = 0; c < across.Count; c++)
            shapes.Add(Label(Fmt(across[c]), x0 + c * pitch + o.CellMm / 2, y0 - o.GapMm / 2 - o.LabelMm / 2));
        for (int r = 0; r < down.Count; r++)
            shapes.Add(Label(Fmt(down[r]), x0 - o.GapMm, y0 + r * pitch + o.CellMm / 2, rightAlign: true));
        shapes.Add(Label(Name(o.Down).Split(' ')[0], x0 - o.GapMm, y0 + gridH + o.GapMm + o.LabelMm / 2, rightAlign: true));

        s = Ops.AddShapes(s, shapes);
        return (s, shapes.Select(x => x.Id).ToList());
    }

    /// <summary>Overall size of the test, mm (for checking it fits the work area).</summary>
    public static Rect2 Bounds(DocState before, ColourTestOptions o)
    {
        var (after, ids) = Add(before, o);
        var set = ids.ToHashSet();
        return after.Shapes.Where(x => set.Contains(x.Id)).Select(x => x.Bounds).Aggregate(Rect2.Empty, (a, b) => a.Union(b));
    }

    /// <summary>The colour test squares in a design, by row and column (empty if there's no colour test).</summary>
    public static IReadOnlyList<ColourTestCell> Cells(DocState s)
    {
        var list = new List<ColourTestCell>();
        foreach (var l in s.Layers.Where(l => l.Name.StartsWith(LayerPrefix, StringComparison.Ordinal)))
        {
            var rest = l.Name[LayerPrefix.Length..].Split(" C");
            if (rest.Length != 2 || !int.TryParse(rest[0], out int r) || !int.TryParse(rest[1], out int c)) continue;
            var shapes = s.ShapesOn(l.Id).ToList();
            if (shapes.Count == 0) continue;
            var b = shapes.Select(x => x.Bounds).Aggregate(Rect2.Empty, (a, x) => a.Union(x));
            list.Add(new(r - 1, c - 1, l, b));
        }
        return list.OrderBy(x => x.Row).ThenBy(x => x.Col).ToList();
    }
}
