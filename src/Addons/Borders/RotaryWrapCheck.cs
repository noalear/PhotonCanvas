using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Extensions;

namespace PhotonCanvas.Addon.Borders;

/// <summary>
/// A border wrapped round a rotary part goes once round the part at the size it was made for. The job's
/// check says when the work mode isn't the rotary, when Object Ø is set to another size (the pattern would
/// stop short of meeting itself round the back, or run over), or when the picture was stretched.
/// </summary>
public sealed class RotaryWrapCheck : IJobHook
{
    public string Name => "Rotary wrap";

    /// <summary>How far once round may be off (mm) before the join shows.</summary>
    public const double ToleranceMm = 0.3;

    public IEnumerable<string> Check(DocState state, JobOptions options)
    {
        var said = new HashSet<string>();
        foreach (var layer in JobBuilder.OutputLayers(state))
            foreach (var shape in state.SentFor(layer))
            {
                if (shape is not ImageShape || !shape.AddonData.TryGetValue(BorderOutput.SpecKey, out var json)) continue;
                if (BorderSpec.FromJson(json) is not { Layout: BorderLayout.Rotary } spec) continue;
                string name = string.IsNullOrWhiteSpace(shape.Name) ? "The rotary wrap" : $"\"{shape.Name}\"";
                double d = spec.PartDiameterMm, round = spec.PartCircumferenceMm;
                string? problem = null;
                if (options.Mode is not WorkMode.Rotary r)
                    problem = $"{name} wraps round a part in the rotary: choose Machine → Work mode → Rotary, Object Ø {d:0.##} mm.";
                else if (Math.Abs(r.CircumferenceMm - round) > ToleranceMm)
                    problem = $"{name} was made to go once round Ø {d:0.##} mm, but Work mode's Object Ø is {r.CircumferenceMm / Math.PI:0.##} mm: set it to {d:0.##} mm, or right-click it → Edit border… and make it for that size, or the pattern won't meet itself round the back.";
                else if (Math.Abs(shape.Bounds.Height - round) > ToleranceMm)
                    problem = $"{name} has been stretched round the part ({shape.Bounds.Height:0.#} mm, once round is {round:0.#} mm): right-click it → Edit border… to make it again, or the join round the back won't match.";
                if (problem is not null && said.Add(problem)) yield return problem;
            }
    }
}
