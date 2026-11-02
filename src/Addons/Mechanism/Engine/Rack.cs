using Clipper2Lib;
using PhotonCanvas.Core;

namespace PhotonCanvas.Addon.Mechanism;

/// <summary>
/// A rack and pinion: turning the gear slides the toothed bar. The rack has a slot along it and runs on two
/// pins in a plate, which also holds the pinion's axle.
/// </summary>
public sealed record RackSpec : MechSpec
{
    public ToothForm Form { get; init; } = new();
    public int PinionTeeth { get; init; } = 16;
    /// <summary>How far the rack slides from one end to the other (mm).</summary>
    public double TravelMm { get; init; } = 60;
    public bool AvoidUndercut { get; init; } = true;
    public Axle Axle { get; init; } = new();
    /// <summary>A plate with the pinion's axle and two guide pins for the rack's slot.</summary>
    public bool Plate { get; init; } = true;
    /// <summary>A hole near the pinion's rim for a peg to turn it by.</summary>
    public bool Handle { get; init; } = true;

    public override Mechanism Build(CancellationToken ct = default)
    {
        if (PinionTeeth < 6) throw new InvalidOperationException("The pinion needs at least 6 teeth.");
        if (!(TravelMm > 0)) throw new InvalidOperationException("Give the rack some travel.");
        var f = Form;
        double m = f.Module, p = f.Pitch;
        int z = PinionTeeth;
        double x = AvoidUndercut ? GearMaker.ShiftAgainstUndercut(f, z) : 0;
        var size = GearMaker.External(f, z, x);
        double rp = size.PitchR;
        var mech = new Mechanism { Title = "Rack and pinion", Cycle = 2 * TravelMm / rp };

        // ---- the parts ----
        double holeR = Axle.Square ? Axle.SizeMm * Math.Sqrt(2) / 2 + 0.15 : Axle.SizeMm / 2 + 0.15;
        double pegR = Math.Max(2, Axle.SizeMm / 2 - 0.03);
        var pinionTeeth = GearCache.External(f, z, x, 0);
        var holes = new Paths64 { Axle.TurningHole(default) };
        double rimR = size.RootR - Math.Max(3, 1.25 * m);
        Vec2? handle = null;
        if (Handle && rimR - pegR - 1 > holeR + 3)
        {
            handle = Shapes.Polar(rimR - pegR - 1, 0);
            holes.Add(Poly.Circle(handle.Value, pegR, 0.003));
        }
        var pinion = Poly.Subtract(pinionTeeth, holes);
        mech.Parts.Add(new Part
        {
            Name = $"Pinion ({z} teeth)",
            Shape = pinion,
            Label = size.RootR - holeR > 5 ? new PartLabel("P", new Vec2(0, -(holeR + (size.RootR - holeR) * 0.5)), Math.Clamp((size.RootR - holeR) * 0.35, 2.5, 6)) : null,
        });

        // The rack: enough teeth to stay in mesh over the whole travel, a body under them with the slot.
        int n = (int)Math.Ceiling(TravelMm / p) + 4;
        double ded = m * (1 + f.Clearance);
        double slotW = Axle.SizeMm + 0.3, pinR = Axle.SizeMm / 2;
        double body = Math.Max(2 * m, slotW + 7);
        double spacing = 24;
        double slotLen = TravelMm + spacing + slotW + 0.4;
        double margin = 4;
        double end = Math.Max(5, (slotLen + 2 * margin - n * p) / 2);
        var rackTeeth = GearCache.Rack(f, n, body, end);
        double u0 = -p / 2 - end, u1 = (n - 0.5) * p + end, mid = (u0 + u1) / 2;
        double slotY = -ded - body / 2;
        var rack = Plate ? Poly.Subtract(rackTeeth, [Shapes.RoundRect(mid - slotLen / 2, slotY - slotW / 2, mid + slotLen / 2, slotY + slotW / 2, slotW / 2)]) : rackTeeth;
        // Its name on the body, beside the slot (under it when the slot runs the whole length).
        double under = body / 2 - (Plate ? slotW / 2 : 0);
        mech.Parts.Add(new Part
        {
            Name = $"Rack ({n} teeth)",
            Shape = rack,
            Label = Plate
                ? (under >= 3 ? new PartLabel("Rack", new Vec2(mid, slotY - slotW / 2 - under / 2), Math.Min(3, under * 0.65)) : null)
                : new PartLabel("Rack", new Vec2(mid, slotY), Math.Min(4, body * 0.5)),
        });

        // ---- placing it: the pinion stays put (its axle at the origin), the rack slides under it ----
        double centreY = rp + x * m;                     // pinion centre above the rack's pitch line
        double X0 = -(n - 2.5) * p;                      // rack's offset at the start: the pinion near its right end
        double pinionPhase = -Math.PI / 2;               // a tooth pointing straight down into a space
        Pose PinionAt(double a) => new(new Vec2(0, centreY), pinionPhase + Turn(a));
        Pose RackAt(double a) => new(new Vec2(X0 + rp * Turn(a), 0), 0);

        var bodies = new List<Body>();
        var extent = new Rect2(X0 + u0, slotY - body / 2 - 2, X0 + rp * (TravelMm / rp) + u1, centreY + size.TipR);
        Paths64? plate = null;
        Vec2 pin1 = default, pin2 = default;
        if (Plate)
        {
            // The pins: inside the slot all the way along the travel.
            double first = mid - slotLen / 2 + pinR + 0.2 + X0 + TravelMm;
            pin1 = new Vec2(first, slotY);
            pin2 = new Vec2(first + spacing, slotY);
            var box = extent.Inflate(5);
            box = box.Union(new Rect2(-size.TipR - 5, centreY - size.TipR - 5, size.TipR + 5, centreY + size.TipR + 5));
            plate = Poly.Subtract([Shapes.RoundRect(box.MinX, box.MinY, box.MaxX, box.MaxY, 5)],
                [Axle.HeldHole(new Vec2(0, centreY)), Axle.HeldHole(pin1), Axle.HeldHole(pin2)]);
            mech.Parts.Add(new Part { Name = "Plate", Shape = plate, Label = new PartLabel("Plate", new Vec2(box.MinX + 14, box.MaxY - 5), 3) });
            bodies.Add(new Body("Plate", plate, Tints.Frame, _ => Pose.Zero) { Faint = true });
            extent = box;
            mech.Shopping.Add($"Three pins: {Axle.Words} dowel or rod, about {Math.Ceiling(2 * ThicknessMm + 4):0} mm long (the pinion's axle and two for the rack's slot)");
        }
        else mech.Shopping.Add($"An axle for the pinion: {Axle.Words} dowel or rod");
        if (handle is not null) mech.Shopping.Add($"A handle peg: {pegR * 2 + 0.06:0.#} mm dowel, about 25 mm long");

        bodies.Add(new Body("Rack", rack, Tints.Of(1), RackAt));
        bodies.Add(new Body("Pinion", pinion, Tints.Of(0), PinionAt) { IsInput = true, Lines = [[Shapes.Polar(holeR + 1, 0), Shapes.Polar(size.RootR - 1, 0)]] });
        bodies.Add(new Body("Axle", [Axle.Square ? Axle.HeldHole(default) : Poly.Circle(default, Axle.SizeMm / 2, 0.01)], Tints.Steel, _ => new Pose(new Vec2(0, centreY), 0)));
        if (Plate)
            foreach (var pin in new[] { pin1, pin2 })
                bodies.Add(new Body("Pin", [Poly.Circle(default, pinR, 0.01)], Tints.Steel, _ => new Pose(pin, 0)));
        if (handle is { } h)
            bodies.Add(new Body("Handle", [Poly.Circle(default, pegR, 0.01)], Tints.Steel, a => new Pose(PinionAt(a).Apply(h), 0)));

        // ---- the motion check ----
        double eps = GearMaker.ContactRatioRack(f, size);
        if (eps < 1) mech.Findings.Add(new(Severity.Bad, $"The pinion loses touch with the rack between teeth (contact ratio {eps:0.00}): use more teeth."));
        else if (eps < 1.2) mech.Findings.Add(new(Severity.Warn, $"Only just enough teeth touching (contact ratio {eps:0.00}); it may click."));
        else mech.Findings.Add(new(Severity.Good, $"Pinion and rack mesh: {eps:0.00} pairs of teeth touching on average."));
        // Slide it along, looking where they meet.
        var near = new Rect2(-4 * m, -4 * m, 4 * m, 4 * m);
        const int Steps = 24;
        var bad = new bool[Steps];
        double worst = 0;
        double period = 2 * Math.PI / z;
        for (int s = 0; s < Steps; s++)
        {
            ct.ThrowIfCancellationRequested();
            double a = period * s / Steps;
            var pa = Poly.Clip(Poly.Move(pinion, PinionAt(a)), near);
            var pb = Poly.Clip(Poly.Move(rack, RackAt(a)), near);
            double o = Poly.Overlap(pa, pb);
            worst = Math.Max(worst, o);
            bad[s] = o > 1e-3;
        }
        if (bad.Any(b => b))
        {
            mech.Findings.Add(new(Severity.Bad, $"The pinion's teeth jam in the rack ({worst:0.##} mm² overlap at worst)."));
            mech.MarkWhere(a => bad[(int)Math.Floor((Turn(a) % period + period) % period / period * Steps) % Steps], Severity.Bad);
            mech.Clashing = a => bad[(int)Math.Floor((Turn(a) % period + period) % period / period * Steps) % Steps] ? ["Pinion", "Rack"] : [];
        }
        else
        {
            var pa = Poly.Clip(Poly.Move(pinion, PinionAt(0)), near);
            double Touch(int side)
            {
                double lo = 0, hi = 0.5 / rp + Form.BacklashMm / rp;
                for (int i = 0; i < 14; i++)
                {
                    double mid2 = (lo + hi) / 2;
                    var pb = Poly.Clip(Poly.Move(rack, new Pose(new Vec2(X0 + side * mid2 * rp, 0), 0)), near);
                    if (Poly.Overlap(pa, pb) < 1e-4) lo = mid2; else hi = mid2;
                }
                return lo * rp;
            }
            double play = Touch(1) + Touch(-1);
            mech.Findings.Add(new(Severity.Good, $"The rack slides freely, with about {play:0.##} mm of play between the teeth."));
        }
        if (GearMaker.Undercut(f, z, x))
            mech.Findings.Add(new(Severity.Warn, $"The pinion ({z} teeth) is undercut: its teeth are weaker at the root. Tick “Avoid undercut”, or use at least {GearMaker.NoUndercutTeeth(f)} teeth."));
        else if (x > 0) mech.Findings.Add(new(Severity.Note, $"The pinion's teeth are shifted out by {x * m:0.##} mm so they aren't undercut."));
        if (size.TipThickness < Math.Max(0.3, 0.2 * m))
            mech.Findings.Add(new(Severity.Warn, $"The pinion's tooth tips come to a point ({size.TipThickness:0.##} mm across): use more teeth."));
        if (m < 1.5 && ThicknessMm >= 2)
            mech.Findings.Add(new(Severity.Warn, $"Teeth this small (module {m:0.##}) snap easily in wood; 2 or more is safer."));
        mech.Summary = $"One turn of the pinion slides the rack {2 * Math.PI * rp:0.#} mm; the whole {TravelMm:0.#} mm takes {TravelMm / (2 * Math.PI * rp):0.##} turns.";

        mech.Panels.Add(new PlanarPanel
        {
            Title = "Looking along the pinion's axle",
            Bodies = bodies,
            Extent = extent.Inflate(2),
            InputPivot = new Vec2(0, centreY),
            InputSense = 1,
        });
        return mech;

        // The pinion turns forwards for the first half of the cycle and back again in the second.
        double Turn(double a)
        {
            double full = TravelMm / rp;
            double c = ((a % (2 * full)) + 2 * full) % (2 * full);
            return c <= full ? c : 2 * full - c;
        }
    }
}
