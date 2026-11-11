namespace PhotonCanvas.Core;

/// <summary>
/// What the MOPA source can do at one pulse width.
/// <para><see cref="DefaultKhz"/> is the full-power frequency. Each pulse can carry only so much
/// energy, and the longer the pulse the more it may carry; below this frequency the module
/// caps the pulse energy, so it delivers less than its rated power (roughly in proportion to
/// the frequency). The stock software marks that range yellow: "the laser module will
/// automatically reduce its output power". <see cref="MaxKhz"/> is the highest frequency at all.</para>
/// <para><see cref="GapFromKhz"/> (long pulses only): from this frequency up to just under the
/// full-power frequency, pulses are at their largest at close to full power. The stock software
/// doesn't allow that range for relief engraving or on highly reflective materials, where the
/// light bouncing back into the fiber can damage it. So do we: on layers marked highly
/// reflective (from the material's tag); everywhere else the whole range is available.</para>
/// </summary>
public readonly record struct MopaLimit(int PulseWidthNs, int DefaultKhz, int MaxKhz, int? GapFromKhz = null)
{
    /// <summary>In the range the module is protected from (see <see cref="GapFromKhz"/>).</summary>
    public bool InGap(int khz) => GapFromKhz is { } g && khz >= g && khz < DefaultKhz;

    /// <summary>A frequency the source accepts: within 1–max, and on reflective work out of the protected range (moved up to full power).</summary>
    public int Safe(int khz, bool reflective = false)
    {
        int f = Math.Clamp(khz, 1, MaxKhz);
        return reflective && InGap(f) ? DefaultKhz : f;
    }

    /// <summary>Roughly what share of its rated power the source can deliver at this frequency (1 = all of it).</summary>
    public double PowerAvailable(int khz) => khz >= DefaultKhz ? 1 : Math.Max(0, khz) / (double)DefaultKhz;
}

/// <summary>
/// The F2 Ultra UV's 355 nm laser, from xTool Studio's own UV model (GS009): marked with <c>G22</c> and a
/// frequency (<c>G0Q</c>) in this range, no pulse width. Untested on a real machine.
/// </summary>
public sealed record UvLaser(int MinKhz = 40, int MaxKhz = 150, int DefaultKhz = 40, double Watts = 5)
{
    public static readonly UvLaser F2UltraUv = new();
}

/// <summary>
/// Per-model machine limits: from xTool's published specifications, measurements on the user's
/// machine, and (the MOPA table) the user's own xTool Studio at run time.
/// </summary>
public sealed record MachineProfile
{
    /// <summary>Device code reported by discovery / machineInfo.</summary>
    public required string DeviceCode { get; init; }
    /// <summary>Comment tag used in G-code section markers, e.g. <c># GS004-4 HEAD</c>.</summary>
    public required string GcodeTag { get; init; }
    /// <summary>Galvo field in mm, as written in the G-code header.</summary>
    public required double FieldW { get; init; }
    public required double FieldH { get; init; }
    public required double MinSpeed { get; init; }
    public required double MaxSpeed { get; init; }
    /// <summary>Head Z (mm) = material distance + this offset.</summary>
    public required double ZOffset { get; init; }
    /// <summary>Highest Z the head may be sent to.</summary>
    public required double ZMax { get; init; }
    public required IReadOnlyList<MopaLimit> Mopa { get; init; }

    /// <summary>Has the 455 nm blue diode (false on the F2 Ultra UV and the fiber-only F2 Ultra).</summary>
    public bool HasDiode { get; init; } = true;
    /// <summary>Has a MOPA fiber laser whose limits are known, so MOPA settings and tools are offered.</summary>
    public bool HasMopa => Mopa.Count > 0;
    /// <summary>The UV laser, on the F2 Ultra UV (its only laser). Null elsewhere.</summary>
    public UvLaser? Uv { get; init; }

    /// <summary>
    /// The settings as this machine runs them: on a UV machine every layer, blue or MOPA, goes to its UV
    /// laser with the layer's power, speed, passes and frequency (the pulse width is dropped). Elsewhere unchanged.
    /// </summary>
    public LaserSettings OnThisMachine(LaserSettings l) => Uv is null || l.Light == LightSource.Uv ? l : l with { Light = LightSource.Uv };

    /// <summary>The wavelengths safety glasses must be rated for, as written in the warnings.</summary>
    public string Wavelengths => Uv is not null ? "355 nm ultraviolet"
        : HasDiode && HasMopa ? "455 nm blue and 1064 nm infrared"
        : HasMopa ? "1064 nm infrared"
        : "455 nm blue";

    public MopaLimit? MopaLimitFor(int pulseWidthNs)
    {
        foreach (var l in Mopa)
            if (l.PulseWidthNs == pulseWidthNs) return l;
        return null;
    }

    public bool Contains(double x, double y)
    {
        // A hair of tolerance so 220.0000001 from float maths is still inside.
        const double Eps = 1e-6;
        return x >= -Eps && y >= -Eps && x <= FieldW + Eps && y <= FieldH + Eps;
    }

    /// <summary>Head Z for a material distance, or null if unreachable.</summary>
    public double? ZForDistance(double distanceMm)
    {
        double z = distanceMm + ZOffset;
        return double.IsFinite(distanceMm) && distanceMm >= 0 && z <= ZMax ? z : null;
    }

    /// <summary>
    /// Pulse width → full-power and top frequency, and where the protected range starts: read from the
    /// user's own xTool Studio when it's installed, else a cautious built-in table (<see cref="MopaTable"/>).
    /// </summary>
    public static IReadOnlyList<MopaLimit> F2UltraMopa => MopaTable.Current;

    /// <summary>xTool F2 Ultra (dual 40 W diode + 60 W MOPA fiber).</summary>
    public static readonly MachineProfile F2Ultra = new()
    {
        DeviceCode = "GS004-CLASS-4",
        GcodeTag = "GS004-4",
        FieldW = 220,
        FieldH = 220,
        MinSpeed = 2,
        MaxSpeed = 15000,
        ZOffset = 6,
        ZMax = 156,
        Mopa = F2UltraMopa,
    };
}
