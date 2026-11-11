namespace PhotonCanvas.Core;

public enum LightSource
{
    /// <summary>455 nm diode (<c>G21</c>).</summary>
    Blue,
    /// <summary>1064 nm MOPA fiber (<c>G22</c>), with pulse width and frequency.</summary>
    Red,
    /// <summary>
    /// 355 nm UV (the F2 Ultra UV's only laser: <c>G22</c> with a frequency, no pulse width). Never stored
    /// on a layer: a job for a UV machine runs every layer on it (<see cref="MachineProfile.OnThisMachine"/>).
    /// </summary>
    Uv,
}

public static class LightSourceExt
{
    public static string Wire(this LightSource l) => l switch { LightSource.Red => "red", LightSource.Uv => "uv", _ => "blue" };
}

/// <summary>Power, speed and passes for one piece of work.</summary>
public sealed record LaserSettings
{
    public LightSource Light { get; init; } = LightSource.Blue;
    /// <summary>0–100 %. Sent as <c>S</c> = percent × 10.</summary>
    public double Power { get; init; }
    /// <summary>mm/s. Sent as <c>F</c> = mm/s × 60.</summary>
    public double Speed { get; init; }
    /// <summary>How many times this content is processed (≥ 1).</summary>
    public int Passes { get; init; } = 1;
    /// <summary>Fiber only.</summary>
    public int PulseWidthNs { get; init; } = 200;
    /// <summary>Fiber only.</summary>
    public int FrequencyKhz { get; init; } = 65;

    public static LaserSettings Blue(double power, double speed) =>
        new() { Light = LightSource.Blue, Power = power, Speed = speed };

    public static LaserSettings Red(double power, double speed, int pulseWidthNs, int frequencyKhz) =>
        new() { Light = LightSource.Red, Power = power, Speed = speed, PulseWidthNs = pulseWidthNs, FrequencyKhz = frequencyKhz };

    public LaserSettings WithPasses(int passes) => this with { Passes = passes };

    /// <summary><c>S</c> word value (per-mille of full power).</summary>
    public long SValue => (long)Math.Round(Power * 10, MidpointRounding.AwayFromZero);

    /// <summary><c>F</c> word value (mm/min).</summary>
    public long FValue => (long)Math.Round(Speed * 60, MidpointRounding.AwayFromZero);

    public static readonly IReadOnlyList<int> PulseWidths =
        [2, 4, 6, 9, 13, 20, 30, 45, 60, 80, 100, 150, 200, 250, 350, 500];

    /// <param name="reflective">Highly reflective material: keep out of the source's protected frequency range.</param>
    public void Validate(MachineProfile m, bool reflective = false)
    {
        JobException.CheckRange("power", Power, 0, 100, "%");
        JobException.CheckRange("speed", Speed, m.MinSpeed, m.MaxSpeed, "mm/s");
        JobException.CheckRange("passes", Passes, 1, 10_000, "");
        // A UV machine runs every layer on its UV laser: only the frequency matters, in the UV's range.
        if (m.Uv is { } uv)
        {
            JobException.CheckRange("UV frequency", FrequencyKhz, uv.MinKhz, uv.MaxKhz, "kHz");
            return;
        }
        if (Light == LightSource.Uv)
            throw new JobException("This layer is set for a UV laser, but the machine in use has none. Choose the F2 Ultra UV in Settings → Machines, or set the layer to another laser.");
        if (Light == LightSource.Red)
        {
            var lim = m.MopaLimitFor(PulseWidthNs)
                ?? throw new JobException(
                    $"Pulse width {PulseWidthNs} ns is not supported; choose one of {string.Join(", ", PulseWidths)} ns.");
            JobException.CheckRange("frequency", FrequencyKhz, 1, lim.MaxKhz, "kHz");
            if (reflective && lim.InGap(FrequencyKhz))
                throw new JobException(
                    $"On highly reflective material, {lim.GapFromKhz}–{lim.DefaultKhz - 1} kHz at {PulseWidthNs} ns is kept out of to protect the laser source " +
                    $"(the largest pulses at nearly full power, which the material can bounce back into the fiber). Use {lim.DefaultKhz} kHz or more" +
                    (lim.GapFromKhz > 1 ? $", or {lim.GapFromKhz - 1} kHz or less." : "."));
        }
    }
}
