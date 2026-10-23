using System.Text;
using PhotonCanvas.Core;

namespace PhotonCanvas.Doc;

/// <summary>The job options the saved settings give: what the job bar sends with, and what the remote server uses.</summary>
public static class JobSettings
{
    public static JobOptions From(AppSettings s) => new()
    {
        WholeJobPasses = s.WholeJobPasses,
        PassDelayS = Math.Clamp(s.PassDelayS, 0, 3600),
        PathPauseS = Math.Clamp(s.PathPauseS, 0, Job.MaxPathPauseS),
        // GRBL machines: their one laser's power (the F2 Ultra's 40 W blue and 60 W MOPA are the defaults).
        BlueWatts = s.ActiveSetup() is { Kind: MachineKind.Grbl } g1 ? g1.PowerW : 40,
        RedWatts = s.ActiveSetup() is { Kind: MachineKind.Grbl } g2 ? g2.PowerW : 60,
        Mode = WorkModes.From(s),
        Machine = WorkModes.Area(s),
        FanGear = s.FanGear,
        MopaOffset = new Vec2(s.MopaOffsetX, s.MopaOffsetY),
        PurifierSpeed = s.PurifierSpeed,
    };
}

/// <summary>
/// What to tell the user before a job goes to the machine.
/// </summary>
/// <param name="Refusal">Set when the job must not be sent (the head would come too close to the material).</param>
/// <param name="NotInJob">Things that look like part of the design but won't be done: shown first.</param>
/// <param name="Lines">The layers, passes, size, time and anything unusual about this run.</param>
/// <param name="Warnings">The builder's warnings (without the left-out ones).</param>
public sealed record JobReview(string? Refusal, IReadOnlyList<string> NotInJob, IReadOnlyList<string> Lines, IReadOnlyList<string> Warnings);

/// <summary>The checks before sending a job, shared by the desktop app's Ship It and the remote server.</summary>
public static class JobChecks
{
    /// <param name="focusDistanceMm">The machine's focus distance to the material, if known.</param>
    public static JobReview Review(DocState state, BuiltJob job, JobOptions opt, AppSettings settings, double? focusDistanceMm)
    {
        var lines = new List<string>();
        var sent = JobBuilder.OutputLayers(state).Where(l => state.ShapesFor(l).Any()).ToList();
        static string Line(Layer l)
        {
            var src = l.Laser.Light == LightSource.Red ? $"MOPA {l.Laser.PulseWidthNs} ns {l.Laser.FrequencyKhz} kHz" : "diode";
            return $"• {l.Name}: {l.Mode}, {l.Laser.Power:0.#}% at {l.Laser.Speed:0.#} mm/s × {l.Laser.Passes} ({src})";
        }
        if (sent.Count <= 8)
            lines.AddRange(sent.Select(Line));
        else
        {
            // Many layers (a test grid): the ranges say more than 50 lines would.
            static string Range(IEnumerable<double> v, string unit) { var a = v.Min(); var b = v.Max(); return a == b ? $"{a:0.#}{unit}" : $"{a:0.#}–{b:0.#}{unit}"; }
            lines.Add($"{sent.Count} layers: power {Range(sent.Select(l => l.Laser.Power), "%")}, speed {Range(sent.Select(l => l.Laser.Speed), " mm/s")}" +
                      (sent.Any(l => l.Laser.Light == LightSource.Red)
                          ? $", MOPA {Range(sent.Where(l => l.Laser.Light == LightSource.Red).Select(l => (double)l.Laser.PulseWidthNs), " ns")} at {Range(sent.Where(l => l.Laser.Light == LightSource.Red).Select(l => (double)l.Laser.FrequencyKhz), " kHz")}"
                          : "") + ".");
            lines.Add($"First: {Line(sent[0])[2..]}");
            lines.Add($"Last: {Line(sent[^1])[2..]}");
        }
        lines.Add("");
        lines.Add(opt.WholeJobPasses
            ? $"Passes run over the whole job{(opt.PassDelayS > 0 ? $", {opt.PassDelayS:0.#} s pause between" : "")}."
            : "Each path runs all its passes before the next.");
        if (opt.PathPauseS > 0) lines.Add($"A {opt.PathPauseS:0.##} s pause after each path.");
        lines.Add($"Size {job.Bounds.Width:0.#} × {job.Bounds.Height:0.#} mm · about {TimeText.Duration(job.EstimateS)}.");
        if (focusDistanceMm is { } d) lines.Add($"Machine focus distance: {d:0.0} mm. Make sure this matches your material.");
        if (VariableText.Describe(state) is { } marks) lines.Add($"Fields: {marks}.");
        if (WorkModes.Usable(settings) && settings.WorkMode != "Flat") lines.Add($"Work mode: {WorkModes.Describe(settings)} (experimental).");
        if (opt.Section is { } sec) lines.Add($"Marking {sec.Label} only. Is the material where it should be for it?");
        if (opt.OnlyLayer is { } only && state.FindLayer(only) is { } ol) lines.Add($"Marking the layer \"{ol.Name}\" only.");

        // Step-down and focus offsets lower the head: never let it come near the material.
        string? refusal = null;
        double offset = job.Job.Blocks.Select(b => b.FocusOffsetMm).DefaultIfEmpty(0).Max();
        double deepest = JobBuilder.OutputLayers(state).Where(l => l.Mode is LayerMode.Cut or LayerMode.Score && (l.StepDownMm > 0 || l.StepDownFirstMm > 0))
            .Select(l => l.FocusOffsetMm + l.StepDownFirstMm + (Math.Max(1, l.Laser.Passes) - 1) * l.StepDownMm).Append(offset).Max();
        if (deepest > 0 && focusDistanceMm is { } focus && deepest > focus - 3)
            refusal = $"Step-down and focus offsets would lower the head {deepest:0.##} mm, but it's only {focus:0.0} mm from the material. Reduce them.";
        if (job.Job.Blocks.Any(b => b.FocusOffsetMm != 0))
            lines.Add($"The head moves for focus offsets (down at most {Math.Max(0, offset):0.##} mm) and comes back at the end.");
        return new JobReview(refusal, [.. job.LeftOut], lines, [.. job.Warnings.Except(job.LeftOut)]);
    }

    /// <summary>The review as one message: left-out things first, then the lines and the warnings.</summary>
    public static string Text(JobReview r)
    {
        var sb = new StringBuilder();
        if (r.NotInJob.Count > 0)
        {
            sb.AppendLine("NOT IN THIS JOB:");
            foreach (var w in r.NotInJob) sb.AppendLine($"✖ {w}");
            sb.AppendLine();
        }
        foreach (var l in r.Lines) sb.AppendLine(l);
        foreach (var w in r.Warnings) sb.AppendLine($"⚠ {w}");
        return sb.ToString();
    }
}
