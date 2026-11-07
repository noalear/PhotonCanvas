using PhotonCanvas.Core;

namespace PhotonCanvas.Doc;

/// <summary>The work mode chosen in the settings, as the generator needs it.</summary>
public static class WorkModes
{
    public static readonly string[] Attachments = ["RA2 Pro · rollers", "RA2 Pro · chuck", "RA3 · rollers", "RA3 · chuck", "Your own rotary"];

    /// <summary>The attachment (index into <see cref="Attachments"/>) that is the user's own rotary.</summary>
    public const int CustomRotary = 4;

    /// <summary>xTool's chucks: the firmware's units for one turn of the object.</summary>
    public const double ChuckTurnUnits = 128;
    /// <summary>The RA2 Pro's roller scale (four times as much on the RA3).</summary>
    public const double RollerScale = 0.59917;

    /// <summary>Longest unrolled length the rotary takes (xTool Studio's limit).</summary>
    public const double MaxCircumferenceMm = 375;

    /// <summary>Is this attachment (index into <see cref="Attachments"/>) one the user has?</summary>
    public static bool Owned(AppSettings s, int attachment) => attachment switch
    {
        CustomRotary => s.OwnsCustomRotary,
        >= 2 and <= 3 => s.OwnsRa3,
        >= 0 and <= 1 => s.OwnsRa2,
        _ => false,
    };

    /// <summary>Does the user have any rotary.</summary>
    public static bool OwnsRotary(AppSettings s) => s.OwnsRa2 || s.OwnsRa3 || s.OwnsCustomRotary;

    /// <summary>Does this attachment hold the object in a chuck (rather than on rollers)?</summary>
    public static bool IsChuck(AppSettings s, int attachment) => attachment == CustomRotary ? s.CustomRotaryChuck : attachment is 1 or 3;

    /// <summary>The work mode in the settings is one the user's equipment can do (else the flat bed is used).</summary>
    public static bool Usable(AppSettings s) => s.ActiveSetup().Kind == MachineKind.XTool && s.WorkMode switch
    {
        "Rotary" => Owned(s, s.RotaryAttachment),
        "Conveyor" => s.OwnsConveyor,
        _ => true,
    };

    /// <summary>
    /// The firmware scale (M535U) and turning direction for a rotary attachment and object diameter.
    /// Rollers: a fixed scale (four times as much on the RA3); a chuck: its units a turn ÷ the circumference.
    /// </summary>
    public static (double Scale, int Direction) RotaryScale(AppSettings s, int attachment, double diameterMm)
    {
        double circ = Math.PI * Math.Max(1, diameterMm);
        if (attachment == CustomRotary)
        {
            bool chuck = s.CustomRotaryChuck;
            int dir = (chuck ? 1 : -1) * (s.CustomRotaryReverse ? -1 : 1);
            return (chuck ? Math.Max(1e-6, s.CustomRotaryTurnUnits) / circ : Math.Max(1e-6, s.CustomRotaryRollerScale), dir);
        }
        bool isChuck = attachment is 1 or 3, ra3 = attachment >= 2;
        return (isChuck ? ChuckTurnUnits / circ : RollerScale * (ra3 ? 4 : 1), isChuck ? 1 : -1);
    }

    /// <summary>
    /// Calibrating the user's own rotary: a job asked for <paramref name="asked"/> turns of the object and it
    /// turned <paramref name="seen"/>. The units a turn (chuck) or the scale (rollers) that would have turned it right.
    /// </summary>
    public static double Calibrated(double current, double asked, double seen) =>
        asked > 0 && seen > 0 && current > 0 ? current * asked / seen : current;

    public static WorkMode From(AppSettings s)
    {
        if (!Usable(s)) return new WorkMode.Flat();
        switch (s.WorkMode)
        {
            case "Rotary":
                double circ = Math.PI * Math.Max(1, s.RotaryDiameterMm);
                var (scale, dir) = RotaryScale(s, s.RotaryAttachment, s.RotaryDiameterMm);
                return new WorkMode.Rotary(scale, dir, Math.Min(circ, MaxCircumferenceMm),
                    LineY: Math.Clamp(s.RotaryLineMm, 1, MachineProfile.F2Ultra.FieldH - 1));
            case "Conveyor":
                return new WorkMode.Conveyor(s.ConveyorUMultiple, s.ConveyorAngle);
            default:
                return new WorkMode.Flat();
        }
    }

    /// <summary>The work area the canvas shows for the mode: the bed, the object unrolled, or the belt's length.</summary>
    public static MachineProfile Area(AppSettings s) => s.ActiveSetup() is { } m && m.Id != MachineSetup.F2Ultra.Id ? m.Profile : From(s) switch
    {
        WorkMode.Rotary r => MachineProfile.F2Ultra with { FieldH = Math.Round(r.CircumferenceMm, 2) },
        WorkMode.Conveyor c => MachineProfile.F2Ultra with { FieldW = c.LengthMm },
        _ => MachineProfile.F2Ultra,
    };

    public static string Describe(AppSettings s) => !Usable(s) ? "Flat bed" : From(s) switch
    {
        WorkMode.Rotary r => $"Rotary ({Attachments[Math.Clamp(s.RotaryAttachment, 0, Attachments.Length - 1)]}), Ø {s.RotaryDiameterMm:0.#} mm: {MachineProfile.F2Ultra.FieldW:0} × {r.CircumferenceMm:0.#} mm unrolled",
        WorkMode.Conveyor c => $"Conveyor: {c.LengthMm:0} × {MachineProfile.F2Ultra.FieldH:0} mm",
        _ => "Flat bed",
    };
}
