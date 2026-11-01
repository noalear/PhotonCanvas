using System.Runtime.CompilerServices;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Extensions;

namespace PhotonCanvas.Addon.Grain;

/// <summary>
/// Grain-aware engraving's step in making a job: every layer set to follow the grain gets a power
/// field from the board's map, on the copy the job is built from (<see cref="DocState.LayerPower"/>).
/// </summary>
public sealed class GrainHook : IJobHook
{
    public string Name => "Grain-aware engraving";

    public DocState BeforeBuild(DocState state, JobOptions options)
    {
        if (GrainProject.Map(state) is not { } map) return state;
        var power = state.LayerPower;
        foreach (var (layer, settings) in GrainProject.Active(state))
        {
            var f = Field(map, settings);
            power = power.SetItem(layer.Id, PowerField.Combine(power.GetValueOrDefault(layer.Id), f)!);
        }
        return ReferenceEquals(power, state.LayerPower) ? state : state with { LayerPower = power };
    }

    public IEnumerable<string> Check(DocState state, JobOptions options)
    {
        var active = GrainProject.Active(state).Where(x => x.Layer.Output && state.ShapesFor(x.Layer).Any()).ToList();
        if (active.Count == 0) yield break;
        var map = GrainProject.Map(state);
        foreach (var (layer, _) in active)
        {
            if (map is null)
            {
                yield return $"layer \"{layer.Name}\" is set to follow the grain, but the grain hasn't been mapped yet, so it runs at its own power. Map it in Picture → Grain.";
                continue;
            }
            var area = map.Area;
            var b = state.Bounds(state.ShapesFor(layer).Select(sh => sh.Id));
            if (!b.IsEmpty && !area.Contains(b))
                yield return $"part of layer \"{layer.Name}\" is off the grain map, and runs at its own power there. Map the grain over the whole design.";
        }
    }

    static readonly ConditionalWeakTable<GrainMap, Dictionary<GrainSettings, PowerField>> Fields = new();

    /// <summary>The power field a map and settings make (cached: the job and its preview ask often).</summary>
    public static PowerField Field(GrainMap map, GrainSettings settings)
    {
        var byKey = Fields.GetOrCreateValue(map);
        lock (byKey)
        {
            if (byKey.TryGetValue(settings, out var f)) return f;
            var factors = new float[map.Width * map.Height];
            for (int i = 0; i < factors.Length; i++)
            {
                byte v = map.Levels[i];
                factors[i] = v == GrainMap.Unknown ? float.NaN : (float)settings.Factor(v / 254.0);
            }
            f = new PowerField(map.Origin, map.CellMm, map.Width, map.Height, factors);
            byKey[settings] = f;
            return f;
        }
    }
}
