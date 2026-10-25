using PhotonCanvas.Core;

namespace PhotonCanvas.Doc.Geometry;

/// <summary>What to cut a dowel into: the pins that line up stacked slices.</summary>
public sealed record DowelPinOptions
{
    /// <summary>Pins the slices' holes take (one per pair of matching holes).</summary>
    public int PinsNeeded { get; init; }
    /// <summary>Spares on top, as a share of the pins needed (0.1 = 10% more), rounded up.</summary>
    public double SpareShare { get; init; } = 0.10;
    /// <summary>The slices' material thickness: a pin goes through two of them.</summary>
    public double ThicknessMm { get; init; } = 3;
    /// <summary>How much shorter than two slices a pin is (0.15 = 15%), so it never stops the slices closing up.</summary>
    public double ShorterShare { get; init; } = 0.15;
    public double DowelDiameterMm { get; init; } = 3;
    /// <summary>The dowel the user has.</summary>
    public double DowelLengthMm { get; init; } = 300;
    /// <summary>What the beam takes away at each cut.</summary>
    public double KerfMm { get; init; } = 0.2;
    /// <summary>The first cut squares off the dowel's end (bought dowels rarely end square).</summary>
    public bool TrimEnd { get; init; } = true;
    /// <summary>Each cut line runs this many diameters across the dowel, so it goes right through a dowel lying a little off the line.</summary>
    public double CutAcross { get; init; } = 1.2;
}

/// <summary>The pins to cut and where the cuts go, along the dowel from its end.</summary>
public sealed record DowelPinPlan(double PinLengthMm, int Wanted, int Made, IReadOnlyList<double> CutsAtMm, double UsedMm, double CutLineMm)
{
    /// <summary>The dowel (or the bed) runs out before every pin wanted is made.</summary>
    public bool Short => Made < Wanted;
}

/// <summary>
/// Pins for stacked slices, cut from a long dowel lying on the bed: a straight cut across it
/// every pin length (plus the kerf), the first squaring off its end. Only a design: nothing is
/// sent to the machine here.
/// </summary>
public static class DowelPins
{
    public const string CutLayer = "Pins · cut";

    /// <summary>
    /// How long the pins are, how many to make (the pins needed plus spares, rounded up) and
    /// where each cut goes, limited to the dowel's length and to <paramref name="roomMm"/> (the bed).
    /// </summary>
    public static DowelPinPlan Plan(DowelPinOptions o, double roomMm)
    {
        if (o.ThicknessMm <= 0) throw new ArgumentException("The material needs a thickness.");
        if (o.DowelDiameterMm <= 0) throw new ArgumentException("The dowel needs a diameter.");
        double pin = 2 * o.ThicknessMm * (1 - Math.Clamp(o.ShorterShare, 0, 0.9));
        int wanted = o.PinsNeeded <= 0 ? 0 : (int)Math.Ceiling(o.PinsNeeded * (1 + Math.Max(0, o.SpareShare)) - 1e-9);
        double kerf = Math.Max(0, o.KerfMm);
        double length = Math.Max(0, Math.Min(o.DowelLengthMm, roomMm));
        var cuts = new List<double>();
        double at = 0;
        if (o.TrimEnd)
        {
            // A little in from the end, so the beam cuts all the way round the dowel.
            at = Math.Min(1, length);
            cuts.Add(at);
            at += kerf / 2;
        }
        int made = 0;
        while (made < wanted && at + pin + kerf / 2 <= length + 1e-9)
        {
            // Each cut's middle sits half a kerf past the pin, so the pin comes out its full length.
            cuts.Add(at + pin + kerf / 2);
            at += pin + kerf;
            made++;
        }
        double used = cuts.Count == 0 ? 0 : cuts[^1] + kerf / 2;
        return new DowelPinPlan(pin, wanted, made, cuts, used, o.CutAcross * o.DowelDiameterMm);
    }

    /// <summary>
    /// A page of its own with the cut lines on a cut layer and the dowel drawn as the material
    /// (never sent), lying along the bed's longer side; move the lot onto the dowel in the camera
    /// picture. Returns the design showing the new page.
    /// </summary>
    public static DocState Place(DocState s, DowelPinPlan plan, double dowelMm, double dowelLengthMm, MachineProfile machine, string title = "Pins", double marginMm = 10)
    {
        s = Pages.Add(s, title);
        var cut = s.Layers.FirstOrDefault(l => l.Name == CutLayer);
        if (cut is null)
        {
            cut = Layer.Default(s.Layers.Count, LayerMode.Cut) with { Name = CutLayer };
            s = Ops.AddLayer(s, cut);
        }
        (s, var material) = MaterialOutline.Ensure(s);
        bool alongX = machine.FieldW >= machine.FieldH;
        double room = (alongX ? machine.FieldW : machine.FieldH) - marginMm;
        double across = alongX ? machine.FieldH / 2 : machine.FieldW / 2;
        // Along the dowel (from its end at the margin) and across it, to the bed.
        Vec2 P(double along, double side) => alongX ? new(marginMm + along, across + side) : new(across + side, marginMm + along);
        double shown = Math.Min(dowelLengthMm, room - marginMm), r = dowelMm / 2, half = plan.CutLineMm / 2;
        var a = P(0, -r);
        var b = P(shown, r);
        var group = Guid.NewGuid();
        var shapes = new List<Shape>
        {
            new PathShape { Name = "Dowel", LayerId = material.Id, Geometry = PathGeometry.Rect(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y)) },
        };
        shapes.AddRange(plan.CutsAtMm.Select((at, k) => (Shape)new PathShape { Name = $"Pin cut {k + 1}", LayerId = cut.Id, Geometry = PathGeometry.Line(P(at, -half), P(at, half)) }));
        return Ops.AddShapes(s, shapes.Select(sh => sh.WithGroup(group)));
    }
}
