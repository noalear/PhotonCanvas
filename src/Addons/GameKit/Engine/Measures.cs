using Clipper2Lib;

namespace PhotonCanvas.Addon.GameKit;

/// <summary>Movement gauges, range rulers and area templates: marked in inches, centimetres or grid squares.</summary>
public static class Measures
{
    /// <summary>The movement gauge's joint: the pin's stem (square-ish, turning in a round hole) and the hole.</summary>
    public sealed record PinPlan(double StemW, double Hole, double HeadW, double HeadH, double CapR);

    public static PinPlan Pin(double T, double fit)
    {
        double pw = Math.Max(T, 2.5), hole = Math.Sqrt(pw * pw + T * T) + Math.Max(fit, 0) + 0.25;
        return new(pw, hole, hole + 3, Math.Max(2, T), hole / 2 + 1.8);
    }

    /// <summary>
    /// A movement gauge: segments on two layers, each joined to the next at a unit mark by a pin (a T,
    /// its stem through both segments, a cap glued on its end underneath) so it bends round scenery.
    /// The ends are square: the gauge is exactly its segments long. Each segment shows the distance to
    /// its far end.
    /// </summary>
    public static void Gauge(Maker m)
    {
        var it = m.Item;
        double T = m.T, wd = it.WidthMm, P = it.StepUnits * Defaults.UnitMm(it.Units);
        int n = it.Segments;
        if (n < 2 || n > 40) { m.Bad("A movement gauge has 2 to 40 segments."); return; }
        var pin = Pin(T, m.Fit);
        double minW = pin.Hole + 2 * (Maker.Bridge + 0.4);
        if (wd < minW) { m.Bad($"The gauge is too narrow for its pins in a {T:0.##} mm sheet: make it at least {minW:0.#} mm wide."); return; }
        if (P < wd + 1) { m.Bad($"The segments are too short for their width: make each at least {(wd + 1) / Defaults.UnitMm(it.Units):0.##} {UnitWord(it.Units)} long, or the gauge narrower."); return; }

        var caps = new List<Pose>();
        var pinPoses = new List<Pose>();
        for (int i = 0; i + 1 < n; i++)
        {
            double px = (i + 1) * P;
            pinPoses.Add(Pose.Front(px - pin.HeadW / 2, -T, 0));
            caps.Add(Pose.Up(px, -T / 2, 0));
        }
        for (int i = 0; i < n; i++)
        {
            var seg = m.Plate($"Segment {i + 1}", Pose.Up(i * P, T / 2 + (i % 2) * T, 0));
            var body = new PathsD { Geo.Rect(0, -wd / 2, P, wd / 2) };
            if (i > 0) body.Add(Geo.Circle(0, 0, wd / 2, 0.01));
            if (i + 1 < n) body.Add(Geo.Circle(P, 0, wd / 2, 0.01));
            seg.Body.AddRange(Geo.Union(body));
            if (i > 0) seg.Holes.Add(Geo.Circle(0, 0, pin.Hole / 2, 0.01));
            if (i + 1 < n) seg.Holes.Add(Geo.Circle(P, 0, pin.Hole / 2, 0.01));
            // The distance to its far end, between the holes; a line across at the far end of the last.
            double room = P - pin.Hole - 3;
            seg.Etched.AddRange(Labels.Text(Defaults.Mark(it.Units, (i + 1) * it.StepUnits), m.Set.Font, P / 2, 0, room, wd * 0.55));
        }
        var cap = m.Plate("Pin cap", [.. caps]);
        cap.Body.Add(Geo.Circle(0, 0, pin.CapR, 0.01));
        var pinPlate = m.Plate("Pin", [.. pinPoses]);
        double s0 = pin.HeadW / 2 - pin.StemW / 2, s1 = pin.HeadW / 2 + pin.StemW / 2;
        pinPlate.Body.Add(Geo.Rect(s0, 0, s1, 3 * T + 0.01));
        pinPlate.Body.Add(Geo.Rect(0, 3 * T, pin.HeadW, 3 * T + pin.HeadH));
        m.Tab(pinPlate, Geo.Rect(s0, 0, s1, T), cap);
        m.Note($"{n} segments of {Defaults.Mark(it.Units, it.StepUnits)} {UnitWord(it.Units)}, {n * P:0.#} mm in all. Stack each joint (odd segments under, even over), push a pin down through it and glue a cap on the stem underneath: glue only the cap, so the joint still turns.");
    }

    static string UnitWord(Units u) => u switch { Units.Centimetres => "cm", Units.GridFeet => "squares", _ => "inch" };

    /// <summary>A straight ruler: ticks along its top edge, numbers under the whole units, and what it counts in.</summary>
    public static void Ruler(Maker m)
    {
        var it = m.Item;
        double U = Defaults.UnitMm(it.Units), L = it.LengthUnits * U, w = it.WidthMm;
        if (it.LengthUnits <= 0 || L < 20) { m.Bad("Make the ruler at least 20 mm long."); return; }
        if (w < 8) { m.Bad("Make the ruler at least 8 mm wide."); return; }
        var r = m.Plate("Ruler", Pose.Up(0, m.T / 2, w));
        r.Body.Add(Geo.Rect(0, 0, L, w));
        var (ticks, labels) = Ticks(it.Units, L, w, m.Set.Font);
        r.Lines.AddRange(ticks);
        r.Etched.AddRange(labels);
        string unit = it.Units switch { Units.Centimetres => "cm", Units.GridFeet => "feet", _ => "inches" };
        r.Etched.AddRange(Labels.Text(unit, m.Set.Font, Math.Min(L * 0.5, 12) , w * 0.2, Math.Min(L * 0.9, 20), w * 0.18, bold: false));
        m.Note($"{Defaults.Mark(it.Units, it.LengthUnits)} {unit}: {L:0.#} mm long.");
    }

    /// <summary>Ticks down from the top edge (v = <paramref name="w"/>) every unit, half and quarter (inches) or millimetre (cm), and the numbers.</summary>
    static (List<PathD> Ticks, PathsD Labels) Ticks(Units units, double L, double w, string font)
    {
        double U = Defaults.UnitMm(units);
        int sub = units switch { Units.Centimetres => 10, Units.Inches => 4, _ => 2 };
        var ticks = new List<PathD>();
        var labels = new PathsD();
        double textH = Math.Min(w * 0.28, U * 0.45), big = w * 0.4;
        int steps = (int)Math.Floor(L / U * sub + 1e-6);
        for (int k = 1; k <= steps; k++)
        {
            double x = k * U / sub;
            if (x > L - 0.3) break;
            bool whole = k % sub == 0, half = sub % 2 == 0 && k % (sub / 2) == 0;
            double len = whole ? big : half ? w * 0.28 : w * 0.16;
            ticks.Add([new(x, w + 0.5), new(x, w - len)]);
            if (whole) labels.AddRange(Labels.Text(Defaults.Mark(units, k / sub), font, x, w - big - 1 - textH / 2, U * 0.8, textH));
        }
        return (ticks, labels);
    }

    /// <summary>An area template: a circle with a ring at every unit, a cone (as wide as it is far, at every distance) or a square with a grid.</summary>
    public static void Template(Maker m)
    {
        var it = m.Item;
        double U = Defaults.UnitMm(it.Units), S = it.LengthUnits * U;
        if (it.LengthUnits <= 0 || S < 15) { m.Bad("Make the template at least 15 mm."); return; }
        int whole = (int)Math.Floor(it.LengthUnits + 1e-6);
        double textH = Math.Clamp(U * 0.3, 3, 7);
        var lines = new List<PathD>();
        var etched = new PathsD();
        Plate t;
        switch (it.Shape)
        {
            case TemplateShape.Circle:
            {
                t = m.Plate("Circle template", Pose.Up(S, m.T / 2, S));
                t.Body.Add(Geo.Circle(S, S, S, 0.02));
                // A hole in the middle to see the model it's centred on.
                double hole = Math.Min(4, S * 0.08);
                t.Holes.Add(Geo.Circle(S, S, hole, 0.01));
                for (int k = 1; k <= whole; k++)
                {
                    double rk = k * U;
                    if (rk < S - 0.3) lines.Add(Geo.Loop(Geo.Circle(S, S, rk, 0.02)));
                    if (rk - textH > hole + 1) etched.AddRange(Labels.Text(Defaults.Mark(it.Units, k), m.Set.Font, S + rk - textH * 0.9, S, U * 0.6, textH));
                }
                break;
            }
            case TemplateShape.Cone:
            {
                // From its point (where the caster stands) out: as wide at each distance as it is far.
                t = m.Plate("Cone template", Pose.Up(0, m.T / 2, 0));
                t.Body.Add(new PathD { new(0, 0), new(S, -S / 2), new(S, S / 2) });
                for (int k = 1; k <= whole; k++)
                {
                    double x = k * U;
                    if (x < S - 0.3) lines.Add([new(x, -x / 2 - 1), new(x, x / 2 + 1)]);
                    if (x * 0.5 > textH) etched.AddRange(Labels.Text(Defaults.Mark(it.Units, k), m.Set.Font, x - textH * 0.8, 0, x * 0.45, textH));
                }
                lines.Add([new(-1, 0), new(S + 1, 0)]);
                break;
            }
            default:
            {
                t = m.Plate("Square template", Pose.Up(0, m.T / 2, S));
                t.Body.Add(Geo.Rect(0, 0, S, S));
                for (int k = 1; k <= whole; k++)
                {
                    double x = k * U;
                    if (x >= S - 0.3) break;
                    lines.Add([new(x, -1), new(x, S + 1)]);
                    lines.Add([new(-1, x), new(S + 1, x)]);
                }
                etched.AddRange(Labels.Text($"{Defaults.Mark(it.Units, it.LengthUnits)}", m.Set.Font, S / 2, S / 2, Math.Min(U * 0.8, S * 0.5), textH * 1.4));
                break;
            }
        }
        t.Lines.AddRange(lines);
        t.Etched.AddRange(etched);
        m.Note($"A {Defaults.Mark(it.Units, it.LengthUnits)} {(it.Units == Units.Centimetres ? "cm" : it.Units == Units.GridFeet ? "ft" : "inch")} {it.Shape.ToString().ToLowerInvariant()}{(it.Shape == TemplateShape.Circle ? " (radius)" : "")}: {(it.Shape == TemplateShape.Circle ? 2 * S : S):0.#} mm across.");
    }
}
