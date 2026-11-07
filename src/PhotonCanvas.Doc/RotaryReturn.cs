using PhotonCanvas.Core;

namespace PhotonCanvas.Doc;

/// <summary>
/// The rotary has no home: each job starts wherever the object is, and turns it back at the end
/// (<c>G0 U0</c>). A job stopped part way skips that, so the object is left turned. This works out
/// how far (from the job's own time estimate, the same one that moves the "laser is here" marker)
/// and builds the laser-off job that turns it back, since the F2 Ultra ignores U moves sent outside a job.
/// </summary>
public static class RotaryReturn
{
    /// <summary>Turns smaller than this (mm round the object) aren't worth a job.</summary>
    public const double MinTurnMm = 0.05;

    /// <summary>
    /// Where a rotary job has turned the object <paramref name="seconds"/> into its run (by its estimate):
    /// U, in the job's own units (mm round the object, sign included). Null when the job isn't a rotary job;
    /// 0 when it hadn't started marking or had finished (the end turns it back).
    /// </summary>
    public static double? UAt(BuiltJob job, double seconds)
    {
        if (job.Job.Mode is not WorkMode.Rotary r) return null;
        var m = RunTracker.At(job, seconds);
        if (m.Fraction >= 1) return 0;
        double? y = m.Head?.Y ?? (m.Sweep is { } s ? (s.A.Y + s.B.Y) / 2 : null);
        // Between paths (or in a pause): where the last finished path ended.
        if (y is null && m.Done.Count > 0 && job.Steps[m.Done.Max()] is { Points.Count: > 0 } last)
            y = last.Closed ? last.Points[0].Y : last.Points[^1].Y;
        return y is { } v ? (v - r.OriginY) * r.Direction : 0;
    }

    /// <summary>The same, from the machine's own progress (0–1) instead of the time.</summary>
    public static double? UAtFraction(BuiltJob job, double fraction) =>
        UAt(job, Math.Clamp(fraction, 0, 1) * (job.Timeline.Count == 0 ? 0 : job.Timeline[^1].End));

    /// <summary>
    /// A stopped job left the object <paramref name="uMm"/> round (with <paramref name="mode"/>'s scale):
    /// added to any turn still left from before, in the earlier job's scale, so one return undoes both.
    /// </summary>
    public static void NoteLeft(AppSettings s, double uMm, WorkMode.Rotary mode)
    {
        if (s.RotaryLeftU is { } before && s.RotaryLeftScale > 0)
            s.RotaryLeftU = before + uMm * mode.UMultiple / s.RotaryLeftScale;
        else
        {
            s.RotaryLeftU = uMm;
            s.RotaryLeftScale = mode.UMultiple;
            s.RotaryLeftCircumferenceMm = mode.CircumferenceMm;
        }
        if (Math.Abs(s.RotaryLeftU.Value) < MinTurnMm) s.RotaryLeftU = null;
    }

    /// <summary>The rotary mode that turns a left-over turn back: the stopped job's scale, today's laser line.</summary>
    public static WorkMode.Rotary ReturnMode(AppSettings s) =>
        new(s.RotaryLeftScale, 1, Math.Max(1, s.RotaryLeftCircumferenceMm), LineY: Math.Clamp(s.RotaryLineMm, 1, MachineProfile.F2Ultra.FieldH - 1));

    /// <summary>
    /// Turning outside a job (the conveyor's feed request): its units for one motor unit of a job
    /// (U × the job's scale). Until a one-turn check sets it, 1 ÷ the conveyor factor, since the
    /// machine scales the feed by its belt factor (on Kyle's F2 Ultra 5 of them turned a 32-a-turn
    /// chuck about 45°, as that predicts).
    /// </summary>
    public static double NowScale(AppSettings s) => s.RotaryNowScale > 0 ? s.RotaryNowScale : 1 / Math.Max(0.01, s.ConveyorUMultiple);

    /// <summary>The motor units of one whole turn of the object: the U scale × once round it.</summary>
    public static double UnitsPerTurn(WorkMode.Rotary mode, double circumferenceMm) => mode.UMultiple * circumferenceMm;

    /// <summary>Motor units to turn the object <paramref name="degrees"/> (positive: the way a design runs down the page).</summary>
    public static double UnitsFor(double degrees, WorkMode.Rotary mode, double circumferenceMm) =>
        degrees / 360 * UnitsPerTurn(mode, circumferenceMm) * mode.Direction;

    /// <summary>Motor units that turn back what a stopped job left (0 if nothing is left).</summary>
    public static double UnitsBack(AppSettings s) => s.RotaryLeftU is { } u ? -u * s.RotaryLeftScale : 0;

    /// <summary>
    /// After a one-turn check: <paramref name="asked"/> turns were asked for and it made <paramref name="made"/>.
    /// The scale that would have made it right.
    /// </summary>
    public static double CorrectedNowScale(AppSettings s, double asked, double made) =>
        asked > 0 && made > 0 ? NowScale(s) * asked / made : NowScale(s);

    /// <summary>A turn in degrees of the object, for <paramref name="uMm"/> round an object <paramref name="circumferenceMm"/> round.</summary>
    public static double Degrees(double uMm, double circumferenceMm) => circumferenceMm > 0 ? uMm / circumferenceMm * 360 : 0;

    /// <summary>The U for a turn of <paramref name="degrees"/> (positive: the way a design runs down the page).</summary>
    public static double UFor(double degrees, WorkMode.Rotary mode, double circumferenceMm) => degrees / 360 * circumferenceMm * mode.Direction;

    /// <summary>The .xf job that turns the rotary by <paramref name="uMm"/> with the laser off (see <see cref="Job.TurnRotary"/>).</summary>
    public static byte[] TurnJob(WorkMode.Rotary mode, double uMm, DateTimeOffset? now = null)
    {
        var at = now ?? DateTimeOffset.Now;
        var job = new Job(MachineProfile.F2Ultra, []) { Mode = mode, Created = at.ToString("yyyy_MM_dd_HH_mm_ss") };
        string motion = job.TurnRotary(uMm);
        // (The frame is checked against the unrolled area; it's redrawn across the laser line anyway.)
        double mid = MachineProfile.F2Ultra.FieldW / 2, round = Math.Min(mode.CircumferenceMm, MachineProfile.F2Ultra.FieldH) / 2;
        return XfPackage.Build(new()
        {
            Motion = motion,
            // The frame button would only trace a short line across the rotary.
            Border = job.Border([new(mid - 5, round), new(mid + 5, round)]),
            PreviewJpeg = XfPackage.PreviewJpeg([]),
            Light = "blue",
            Mode = "LASER_CYLINDER",
            Axis = "U",
            Created = at,
        });
    }
}
