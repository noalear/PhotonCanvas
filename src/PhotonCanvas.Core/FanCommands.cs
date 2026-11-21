using System.Text.RegularExpressions;

namespace PhotonCanvas.Core;

/// <summary>How a fan gear is asked for: <c>B</c> = automatic at that gear (what every job sends), <c>A</c> = manual, held at that gear.</summary>
public enum FanControl { Auto, Manual }

/// <summary>
/// Where a fan command goes: the machine itself (a bare <c>M9064</c>), or forwarded with
/// <c>G198 P…</c> to an xTool duct fan on accessory port 70 (the older duct fan) or 78 (the SafetyPro IF2 inline fan).
/// </summary>
public enum FanTarget { Machine, DuctFanPort, InlineFanPort }

/// <summary>
/// The exhaust fan's speed commands (<c>M9064</c>), as xTool writes them for its duct fans: gear 0
/// (off) to 4 (fastest), automatic (<c>B</c>) or manual (<c>A</c>). On the IF2 (port 78) a manual
/// gear carries <c>C3</c> (<c>C2</c> for off) and an automatic one <c>C4</c>. These only touch fans:
/// <see cref="IsFanOnly"/> is the check the fan test uses before sending anything.
/// Which of them the F2 Ultra's own exhaust fan obeys is not known yet (2026-11-21).
/// </summary>
public static partial class FanCommands
{
    public const int MaxGear = 4;

    /// <summary>One fan command for <paramref name="target"/>.</summary>
    public static string Gear(FanTarget target, FanControl control, int gear)
    {
        if (gear is < 0 or > MaxGear) throw new ArgumentOutOfRangeException(nameof(gear), "The fan's gears are 0 (off) to 4.");
        string letter = control == FanControl.Auto ? "B" : "A";
        string m = $"M9064 {letter}{gear}";
        return target switch
        {
            FanTarget.Machine => m,
            FanTarget.DuctFanPort => $"G198 P70 \"{m}\"",
            _ => $"G198 P78 \"{m} {(control == FanControl.Auto ? "C4" : gear == 0 ? "C2" : "C3")}\"",
        };
    }

    /// <summary>The same gear for the machine and both duct-fan ports, as a job's start sends it.</summary>
    public static IReadOnlyList<string> Everywhere(FanControl control, int gear) =>
        [Gear(FanTarget.Machine, control, gear), Gear(FanTarget.DuctFanPort, control, gear), Gear(FanTarget.InlineFanPort, control, gear)];

    /// <summary>True only for a fan speed command in one of the forms above, nothing else.</summary>
    public static bool IsFanOnly(string line) => FanLine().IsMatch(line);

    [GeneratedRegex("""^(M9064 [AB][0-4]( C[234])?|G198 P(70|78) "M9064 [AB][0-4]( C[234])?")$""")]
    private static partial Regex FanLine();

    /// <summary>The machine's stored fan speed setting (%), as xTool's P2S keeps it. Whether the F2 Ultra uses it is what the fan test finds out.</summary>
    public const string SpeedConfigKey = "smokeFanSpeed";
    public const int MinSpeedPct = 10, MaxSpeedPct = 100;

    /// <summary>Stored fan settings worth reading (other xTool machines' names for them included); missing ones just don't come back.</summary>
    public static readonly string[] ConfigKeys =
        [SpeedConfigKey, "purifierTimeout", "purifierEnable", "smokeFanTimeout", "smokeFanEngraveSpeed", "smokeFanCuttingSpeed", "smokeFanBitmapSpeed"];
}

/// <summary>One step of a laser-off fan test job: these fan commands, then a wait.</summary>
public sealed record FanStep(IReadOnlyList<string> Commands, double Seconds, string Note = "");
