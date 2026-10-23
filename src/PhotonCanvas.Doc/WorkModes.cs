using PhotonCanvas.Core;

namespace PhotonCanvas.Doc;

/// <summary>The work mode chosen in the settings, as the generator needs it.</summary>
public static class WorkModes
{
    public static readonly string[] Attachments = ["RA2 Pro · rollers", "RA2 Pro · chuck", "RA3 · rollers", "RA3 · chuck"];

    /// <summary>Longest unrolled length the rotary takes (xTool Studio's limit).</summary>
    public const double MaxCircumferenceMm = 375;

    /// <summary>Is this attachment (index into <see cref="Attachments"/>) one the user has?</summary>
    public static bool Owned(AppSettings s, int attachment) => attachment >= 2 ? s.OwnsRa3 : s.OwnsRa2;

    /// <summary>The work mode in the settings is one the user's equipment can do (else the flat bed is used).</summary>
    public static bool Usable(AppSettings s) => s.ActiveSetup().Kind == MachineKind.XTool && s.WorkMode switch
    {
        "Rotary" => Owned(s, s.RotaryAttachment),
        "Conveyor" => s.OwnsConveyor,
        _ => true,
    };

    public static WorkMode From(AppSettings s)
    {
        if (!Usable(s)) return new WorkMode.Flat();
        switch (s.WorkMode)
        {
            case "Rotary":
                double circ = Math.PI * Math.Max(1, s.RotaryDiameterMm);
                bool chuck = s.RotaryAttachment is 1 or 3, ra3 = s.RotaryAttachment >= 2;
                // Rollers: a fixed scale (four times as much on the RA3); chuck: 128 steps per turn of the object.
                return new WorkMode.Rotary(chuck ? 128 / circ : 0.59917 * (ra3 ? 4 : 1), chuck ? 1 : -1, Math.Min(circ, MaxCircumferenceMm));
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
        WorkMode.Rotary r => $"Rotary ({Attachments[Math.Clamp(s.RotaryAttachment, 0, 3)]}), Ø {s.RotaryDiameterMm:0.#} mm: {MachineProfile.F2Ultra.FieldW:0} × {r.CircumferenceMm:0.#} mm unrolled",
        WorkMode.Conveyor c => $"Conveyor: {c.LengthMm:0} × {MachineProfile.F2Ultra.FieldH:0} mm",
        _ => "Flat bed",
    };
}
