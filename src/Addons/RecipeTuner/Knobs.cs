using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Materials;

namespace PhotonCanvas.Addon.RecipeTuner;

/// <summary>A setting the tuner can change.</summary>
public enum Knob { Power, Speed, Passes, LinesPerCm, Dpi, Frequency, PulseWidth, Focus }

/// <summary>
/// What each setting is, how far one "step" of it goes (the size of change that usually makes a
/// visible difference), its limits on the machine, and how it is rounded. The tuner works in steps,
/// so 15% power counts about as much as 1.5 × the speed.
/// </summary>
public static class Knobs
{
    /// <summary>The settings that matter for a purpose (the tuner compares recipes on all of them).</summary>
    public static IReadOnlyList<Knob> For(RecipePurpose p, LightSource light)
    {
        var k = new List<Knob> { Knob.Power, Knob.Speed, Knob.Passes };
        if (p is RecipePurpose.Engrave or RecipePurpose.Mark) k.Add(Knob.LinesPerCm);
        if (p == RecipePurpose.Picture) k.Add(Knob.Dpi);
        if (light == LightSource.Red) { k.Add(Knob.Frequency); k.Add(Knob.PulseWidth); }
        k.Add(Knob.Focus);
        return k;
    }

    /// <summary>The ones changed unless you say otherwise.</summary>
    public static IReadOnlyList<Knob> Default(RecipePurpose p, LightSource light) => p switch
    {
        RecipePurpose.Cut => [Knob.Power, Knob.Speed, Knob.Passes],
        RecipePurpose.Score => [Knob.Power, Knob.Speed],
        RecipePurpose.Picture => [Knob.Power, Knob.Speed],
        _ when light == LightSource.Red => [Knob.Power, Knob.Speed, Knob.Frequency],
        _ => [Knob.Power, Knob.Speed, Knob.LinesPerCm],
    };

    /// <summary>The purposes the tuner works on (relief has its own depth tests).</summary>
    public static readonly IReadOnlyList<RecipePurpose> Purposes =
        [RecipePurpose.Cut, RecipePurpose.Score, RecipePurpose.Engrave, RecipePurpose.Mark, RecipePurpose.Picture];

    public static LayerMode ModeFor(RecipePurpose p) => p switch
    {
        RecipePurpose.Cut => LayerMode.Cut,
        RecipePurpose.Score => LayerMode.Score,
        RecipePurpose.Picture => LayerMode.Image,
        _ => LayerMode.Fill,
    };

    /// <summary>Engraving and marking learn from each other (both are fills).</summary>
    public static int Family(RecipePurpose p) => p switch
    {
        RecipePurpose.Engrave or RecipePurpose.Mark => 2,
        RecipePurpose.Cut => 0,
        RecipePurpose.Score => 1,
        RecipePurpose.Picture => 3,
        _ => 4,
    };

    public static string Name(Knob k) => k switch
    {
        Knob.Power => "Power",
        Knob.Speed => "Speed",
        Knob.Passes => "Passes",
        Knob.LinesPerCm => "Lines per cm",
        Knob.Dpi => "Dots per inch",
        Knob.Frequency => "Frequency",
        Knob.PulseWidth => "Pulse width",
        _ => "Focus",
    };

    public static double Get(ProcessSettings s, Knob k) => k switch
    {
        Knob.Power => s.Laser.Power,
        Knob.Speed => s.Laser.Speed,
        Knob.Passes => s.Laser.Passes,
        Knob.LinesPerCm => s.LinesPerCm,
        Knob.Dpi => s.Dpi,
        Knob.Frequency => s.Laser.FrequencyKhz,
        Knob.PulseWidth => s.Laser.PulseWidthNs,
        _ => s.FocusOffsetMm,
    };

    /// <summary>The settings with one changed: rounded and kept inside the machine's limits.</summary>
    public static ProcessSettings Set(ProcessSettings s, Knob k, double v, MachineProfile m, bool reflective = false)
    {
        v = Round(k, Clamp(k, v, m, s));
        var l = s.Laser;
        var r = k switch
        {
            Knob.Power => s with { Laser = l with { Power = v } },
            Knob.Speed => s with { Laser = l with { Speed = v } },
            Knob.Passes => s with { Laser = l with { Passes = (int)v } },
            Knob.LinesPerCm => s with { LinesPerCm = v },
            Knob.Dpi => s with { Dpi = v },
            Knob.Frequency => s with { Laser = l with { FrequencyKhz = (int)v } },
            Knob.PulseWidth => s with { Laser = l with { PulseWidthNs = (int)v } },
            _ => s with { FocusOffsetMm = v },
        };
        return Fix(r, m, reflective);
    }

    /// <summary>The fiber's frequency kept to what its pulse width allows (and out of the protected range on reflective metal).</summary>
    public static ProcessSettings Fix(ProcessSettings s, MachineProfile m, bool reflective = false)
    {
        if (s.Laser.Light != LightSource.Red || m.Mopa.Count == 0) return s;
        var lim = m.Mopa.MinBy(x => Math.Abs(x.PulseWidthNs - s.Laser.PulseWidthNs));
        return s with { Laser = s.Laser with { PulseWidthNs = lim.PulseWidthNs, FrequencyKhz = lim.Safe(s.Laser.FrequencyKhz, reflective) } };
    }

    static double Clamp(Knob k, double v, MachineProfile m, ProcessSettings s)
    {
        if (!double.IsFinite(v)) v = Get(s, k);
        var (lo, hi) = Limits(k, m, s);
        return Math.Clamp(v, lo, hi);
    }

    /// <summary>What the machine (and good sense) allows.</summary>
    public static (double Lo, double Hi) Limits(Knob k, MachineProfile m, ProcessSettings s) => k switch
    {
        Knob.Power => (1, 100),
        Knob.Speed => (m.MinSpeed, m.MaxSpeed),
        Knob.Passes => (1, 100),
        Knob.LinesPerCm => (5, Math.Min(1000, FillBlock.MaxLinesPerCm)),
        Knob.Dpi => (50, 1000),
        Knob.Frequency => (1, m.Mopa.Count == 0 ? 4000 : m.Mopa.MinBy(x => Math.Abs(x.PulseWidthNs - s.Laser.PulseWidthNs)).MaxKhz),
        Knob.PulseWidth => m.Mopa.Count == 0 ? (1, 500) : (m.Mopa.Min(x => x.PulseWidthNs), m.Mopa.Max(x => x.PulseWidthNs)),
        _ => (-Job.MaxFocusOffsetMm, Job.MaxFocusOffsetMm),
    };

    /// <summary>Values as you'd type them: whole percents, round speeds, whole passes.</summary>
    public static double Round(Knob k, double v) => k switch
    {
        Knob.Power => v < 10 ? Math.Round(v * 2) / 2 : Math.Round(v),
        Knob.Speed => v < 10 ? Math.Max(0.5, Math.Round(v * 2) / 2) : v < 50 ? Math.Round(v) : v < 200 ? Math.Round(v / 5) * 5 : v < 1000 ? Math.Round(v / 10) * 10 : v < 5000 ? Math.Round(v / 50) * 50 : Math.Round(v / 100) * 100,
        Knob.Passes => Math.Max(1, Math.Round(v)),
        Knob.LinesPerCm => v < 100 ? Math.Round(v) : Math.Round(v / 5) * 5,
        Knob.Dpi => Math.Round(v / 5) * 5,
        Knob.Frequency => Math.Max(1, Math.Round(v)),
        Knob.PulseWidth => Math.Round(v),
        _ => Math.Round(v * 10) / 10,
    };

    // One step of each setting: power in 15% steps, speed in steps of 1.5 ×, and so on.
    const double PowerStep = 15, FocusStep = 1;
    static readonly double SpeedStep = Math.Log(1.5), PassStep = Math.Log(1.7), LineStep = Math.Log(1.4), DpiStep = Math.Log(1.4), FreqStep = Math.Log(1.8);
    /// <summary>How the heat a recipe puts in is counted: a step is 1.6 ×.</summary>
    static readonly double EnergyStep = Math.Log(1.6);

    /// <summary>A setting in steps (the tuner's units).</summary>
    public static double ToSteps(Knob k, double v, MachineProfile m) => k switch
    {
        Knob.Power => v / PowerStep,
        Knob.Speed => Math.Log(Math.Max(v, 0.01)) / SpeedStep,
        Knob.Passes => Math.Log(Math.Max(v, 1)) / PassStep,
        Knob.LinesPerCm => Math.Log(Math.Max(v, 0.1)) / LineStep,
        Knob.Dpi => Math.Log(Math.Max(v, 1)) / DpiStep,
        Knob.Frequency => Math.Log(Math.Max(v, 1)) / FreqStep,
        Knob.PulseWidth => PulseIndex(v, m) / 2.0,
        _ => v / FocusStep,
    };

    public static double FromSteps(Knob k, double u, MachineProfile m) => k switch
    {
        Knob.Power => u * PowerStep,
        Knob.Speed => Math.Exp(u * SpeedStep),
        Knob.Passes => Math.Exp(u * PassStep),
        Knob.LinesPerCm => Math.Exp(u * LineStep),
        Knob.Dpi => Math.Exp(u * DpiStep),
        Knob.Frequency => Math.Exp(u * FreqStep),
        Knob.PulseWidth => PulseAt(u * 2, m),
        _ => u * FocusStep,
    };

    static IReadOnlyList<int> Pulses(MachineProfile m) => m.Mopa.Count > 0 ? [.. m.Mopa.Select(x => x.PulseWidthNs).Order()] : LaserSettings.PulseWidths;

    static double PulseIndex(double ns, MachineProfile m)
    {
        var p = Pulses(m);
        int best = 0;
        for (int i = 1; i < p.Count; i++) if (Math.Abs(p[i] - ns) < Math.Abs(p[best] - ns)) best = i;
        return best;
    }

    static double PulseAt(double index, MachineProfile m)
    {
        var p = Pulses(m);
        return p[Math.Clamp((int)Math.Round(index), 0, p.Count - 1)];
    }

    /// <summary>
    /// Roughly how much heat the settings put into the material (power × passes ÷ speed, times line
    /// or dot density for fills and pictures, times what the fiber delivers at its frequency), in steps.
    /// Recipes with the same heat tend to look alike, so the tuner compares this as well.
    /// </summary>
    public static double Energy(ProcessSettings s, RecipePurpose p, MachineProfile m)
    {
        double e = Math.Max(s.Laser.Power, 0.1) / 100 * Math.Max(1, s.Laser.Passes) / Math.Max(s.Laser.Speed, 0.01);
        if (p is RecipePurpose.Engrave or RecipePurpose.Mark) e *= Math.Max(s.LinesPerCm, 1);
        if (p == RecipePurpose.Picture) e *= Math.Max(s.Dpi, 1) / 2.54;
        if (s.Laser.Light == LightSource.Red && m.MopaLimitFor(s.Laser.PulseWidthNs) is { } lim) e *= Math.Max(0.05, lim.PowerAvailable(s.Laser.FrequencyKhz));
        return Math.Log(e) / EnergyStep;
    }

    /// <summary>A value as written: "72%", "35 mm/s", "×7", "120 lines/cm", "+0.5 mm".</summary>
    public static string Text(Knob k, double v) => k switch
    {
        Knob.Power => $"{v:0.#}%",
        Knob.Speed => $"{v:0.#} mm/s",
        Knob.Passes => $"×{v:0}",
        Knob.LinesPerCm => $"{v:0} lines/cm",
        Knob.Dpi => $"{v:0} dpi",
        Knob.Frequency => $"{v:0} kHz",
        Knob.PulseWidth => $"{v:0} ns",
        _ => v > 0 ? $"focus +{v:0.0} mm" : v < 0 ? $"focus −{-v:0.0} mm" : "focus 0",
    };

    /// <summary>How <paramref name="to"/> differs from <paramref name="from"/>, in words: "5 mm/s slower, 1 more pass".</summary>
    public static string Difference(Knob k, double from, double to)
    {
        double d = to - from;
        if (Math.Abs(d) < 1e-9) return "";
        string n(double x, string f) => Math.Abs(x).ToString(f);
        return k switch
        {
            Knob.Power => $"{n(d, "0.#")}% {(d > 0 ? "more" : "less")} power",
            Knob.Speed => $"{n(d, "0.#")} mm/s {(d > 0 ? "faster" : "slower")}",
            Knob.Passes => $"{n(d, "0")} {(d > 0 ? "more" : "fewer")} pass{(Math.Abs(d) == 1 ? "" : "es")}",
            Knob.LinesPerCm => $"{n(d, "0")} {(d > 0 ? "more" : "fewer")} lines/cm",
            Knob.Dpi => $"{n(d, "0")} dpi {(d > 0 ? "more" : "less")}",
            Knob.Frequency => $"{n(d, "0")} kHz {(d > 0 ? "higher" : "lower")}",
            Knob.PulseWidth => $"{(d > 0 ? "longer" : "shorter")} pulses ({to:0} ns)",
            _ => $"focus {n(d, "0.0")} mm {(d > 0 ? "deeper" : "higher")}",
        };
    }

    /// <summary>The settings in one line, only the parts the purpose uses.</summary>
    public static string Summary(ProcessSettings s, RecipePurpose p) =>
        string.Join(" · ", For(p, s.Laser.Light).Where(k => k != Knob.Focus || Math.Abs(s.FocusOffsetMm) > 1e-9)
            .Where(k => k != Knob.Passes || s.Laser.Passes > 1)
            .Select(k => Text(k, Get(s, k))));
}
