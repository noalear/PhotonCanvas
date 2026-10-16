namespace PhotonCanvas.Core;

public enum LightSource
{
    /// <summary>455 nm diode (<c>G21</c>).</summary>
    Blue,
    /// <summary>1064 nm MOPA fiber (<c>G22</c>), with pulse width and frequency.</summary>
    Red,
}

public static class LightSourceExt
{
    public static string Wire(this LightSource l) => l == LightSource.Red ? "red" : "blue";
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

    public void Validate(MachineProfile m)
    {
        JobException.CheckRange("power", Power, 0, 100, "%");
        JobException.CheckRange("speed", Speed, m.MinSpeed, m.MaxSpeed, "mm/s");
        JobException.CheckRange("passes", Passes, 1, 10_000, "");
        if (Light == LightSource.Red)
        {
            var lim = m.MopaLimitFor(PulseWidthNs)
                ?? throw new JobException(
                    $"Pulse width {PulseWidthNs} ns is not supported; choose one of {string.Join(", ", PulseWidths)} ns.");
            JobException.CheckRange("frequency", FrequencyKhz, 1, lim.MaxKhz, "kHz");
            if (lim.InGap(FrequencyKhz))
                throw new JobException(
                    $"At {PulseWidthNs} ns, {lim.GapFromKhz}–{lim.DefaultKhz - 1} kHz is kept out of to protect the laser source " +
                    $"(the largest pulses at nearly full power, which reflective metal can bounce back into the fiber). Use {lim.DefaultKhz} kHz or more, or {lim.GapFromKhz - 1} kHz or less.");
        }
    }
}
