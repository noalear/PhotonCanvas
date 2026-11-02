using PhotonCanvas.Addon.Mechanism;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using Mech = PhotonCanvas.Addon.Mechanism.Mechanism;

namespace PhotonCanvas.Addon.Automata;

/// <summary>Where a toy's parts go (the mechanism maker's placement, on layers and keys of this add-on's own).</summary>
public static class ToyPlacement
{
    public static readonly PlaceTarget Target = new("Toy · cut", "Toy · marks", "automata.id", "automata.spec");

    /// <summary>The parts too big for the machine's bed (with 3 mm round them, turned a quarter if need be), as findings.</summary>
    public static IEnumerable<Finding> TooBig(Mech m, MachineProfile machine, double gapMm = 3)
    {
        double lo = Math.Min(machine.FieldW, machine.FieldH), hi = Math.Max(machine.FieldW, machine.FieldH);
        foreach (var part in m.Parts)
        {
            var box = Poly.Bounds(part.Shape);
            if (box.IsEmpty || (Math.Max(box.Width, box.Height) + 2 * gapMm <= hi + 1e-6 && Math.Min(box.Width, box.Height) + 2 * gapMm <= lo + 1e-6)) continue;
            string fix = m.Title == "Music box comb" ? "Use thinner stock, fewer notes or the comb an octave up" : "Use fewer or smaller figures, or bring the levers' pivots nearer their rods";
            yield return new Finding(Severity.Bad, $"The {Say.Lower(part.Name)} is {box.Width:0} × {box.Height:0} mm: too big for the {machine.FieldW:0} × {machine.FieldH:0} mm bed. {fix}.");
        }
    }

    /// <summary>The toy a shape belongs to, and its settings, if it's one of ours.</summary>
    public static (Guid Id, ToySpec Spec)? Find(DocState s, IEnumerable<Guid> selection)
    {
        foreach (var id in selection)
        {
            var shape = s.Shapes.FirstOrDefault(x => x.Id == id);
            if (shape is null || !shape.AddonData.TryGetValue(Target.IdKey, out var tid) || !Guid.TryParse(tid, out var g)) continue;
            var withSpec = s.Shapes.FirstOrDefault(x => x.AddonData.TryGetValue(Target.IdKey, out var v) && v == tid && x.AddonData.ContainsKey(Target.SpecKey));
            if (withSpec is not null && ToySpec.FromJson(withSpec.AddonData[Target.SpecKey]) is { } spec) return (g, spec);
        }
        return null;
    }
}
