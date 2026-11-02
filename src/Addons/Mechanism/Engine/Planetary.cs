using Clipper2Lib;
using PhotonCanvas.Core;

namespace PhotonCanvas.Addon.Mechanism;

/// <summary>Which part of a planetary set is held still (the other two are the input and the output).</summary>
public enum PlanetaryHeld
{
    /// <summary>Ring held: turn the sun, the carrier turns slowly the same way (the biggest reduction).</summary>
    Ring,
    /// <summary>Carrier held: turn the sun, the ring turns slowly the other way.</summary>
    Carrier,
    /// <summary>Sun held: turn the ring, the carrier turns a little slower the same way.</summary>
    Sun,
}

/// <summary>
/// A planetary (epicyclic) gear set: a sun gear in the middle, planets round it on a carrier, and a ring
/// gear with its teeth on the inside round them all. A big reduction in a small round space.
/// </summary>
public sealed record PlanetarySpec : MechSpec
{
    public ToothForm Form { get; init; } = new() { Module = 2 };
    public int SunTeeth { get; init; } = 12;
    public int PlanetTeeth { get; init; } = 12;
    public int Planets { get; init; } = 3;
    public PlanetaryHeld Held { get; init; } = PlanetaryHeld.Ring;
    /// <summary>The sun's (and carrier's) axle.</summary>
    public Axle Axle { get; init; } = new();
    /// <summary>The pins the planets turn on (glued into the carrier).</summary>
    public double PlanetPinMm { get; init; } = 4;
    /// <summary>How wide the ring's rim is outside its tooth roots (mm).</summary>
    public double RimMm { get; init; } = 10;
    /// <summary>The sun's hole for its axle; null = one that grips it.</summary>
    public Hole? SunHole { get; init; }

    public int RingTeeth => SunTeeth + 2 * PlanetTeeth;

    /// <summary>How fast the sun, carrier and ring turn for one turn of the input.</summary>
    public (double Sun, double Carrier, double Ring) Speeds()
    {
        double zs = SunTeeth, zr = RingTeeth;
        return Held switch
        {
            PlanetaryHeld.Ring => (1, zs / (zs + zr), 0),
            PlanetaryHeld.Carrier => (1, 0, -zs / zr),
            _ => (0, zr / (zs + zr), 1),
        };
    }

    /// <summary>The planets' angles round the sun: evenly spaced when the teeth allow, else as near as they do.</summary>
    public double[] PlanetAngles()
    {
        int total = SunTeeth + RingTeeth, k = Planets;
        return [.. Enumerable.Range(0, k).Select(j => Math.Round((double)j * total / k) * 2 * Math.PI / total)];
    }

    public override Mechanism Build(CancellationToken ct = default)
    {
        if (SunTeeth < 6 || PlanetTeeth < 6) throw new InvalidOperationException("The sun and planets need at least 6 teeth each.");
        if (Planets < 1 || Planets > 8) throw new InvalidOperationException("Use 1 to 8 planets.");
        var f = Form;
        double m = f.Module, t = ThicknessMm;
        int zs = SunTeeth, zp = PlanetTeeth, zr = RingTeeth;
        var sun = GearMaker.External(f, zs, 0);
        var planet = GearMaker.External(f, zp, 0);
        var ringSize = GearMaker.Internal(f, zr);
        double a = m * (zs + zp) / 2;
        double outerR = ringSize.RootR + Math.Max(4, RimMm);
        var (ws, wc, wr) = Speeds();
        double wp = wc - (ws - wc) * zs / zp; // each planet's own turning (absolute), from Willis's formula
        var angles = PlanetAngles();
        var mech = new Mechanism { Title = "Planetary gears" };

        // ---- the parts ----
        ct.ThrowIfCancellationRequested();
        var bore = new Bore(SunHole, Axle, false, Material);
        double holeR = bore.R;
        double pinHoleR = PlanetPinMm / 2 + 0.15;
        var sunShape = Poly.Subtract(GearCache.External(f, zs, 0, 0), [bore.Cut(default)]);
        if (bore.Turns)
            mech.Findings.Add(new(Severity.Bad, $"The sun's hole lets it turn on its axle, but the sun is {(Held == PlanetaryHeld.Sun ? "held still" : "turned")} by its axle. Use a press or snug fit, or a D or square hole."));
        else if (bore.Chosen) mech.Findings.Add(new(Severity.Note, $"Holes drawn for {MaterialWords.Of(Material)}: the sun's axle hole {bore.Words}."));
        var planetShape = Poly.Subtract(GearCache.External(f, zp, 0, 0), [Poly.Circle(default, pinHoleR, 0.003)]);
        ct.ThrowIfCancellationRequested();
        Paths64 ringTeeth;
        try { ringTeeth = GearCache.Internal(f, zr, zp, outerR); }
        catch (ArgumentOutOfRangeException) { throw new InvalidOperationException("The ring needs more teeth than that: use bigger planets or a bigger sun."); }
        // Four bolt holes in the rim to hold the ring down (or to turn it by).
        double boltR = Math.Min(1.75, (outerR - ringSize.RootR) / 2 - 1.2);
        var bolts = new Paths64();
        if (boltR >= 1.2)
            for (int i = 0; i < 4; i++) bolts.Add(Poly.Circle(Shapes.Polar((ringSize.RootR + outerR) / 2, Math.PI / 4 + i * Math.PI / 2), boltR, 0.003));
        var ringShape = bolts.Count > 0 ? Poly.Subtract(ringTeeth, bolts) : ringTeeth;
        double carrierR = a + pinHoleR + Math.Max(4, PlanetPinMm);
        var carrierHoles = new Paths64 { bore.Frame(default, true) };
        foreach (var g in angles) carrierHoles.Add(Poly.Circle(Shapes.Polar(a, g), PlanetPin(), 0.003));
        var carrierShape = Poly.Subtract([Poly.Circle(default, carrierR, 0.003)], carrierHoles);
        double PlanetPin() => PlanetPinMm / 2 - 0.03;

        mech.Parts.Add(new Part { Name = $"Sun ({zs} teeth)", Shape = sunShape, Label = Lbl("S", sun.RootR, holeR) });
        mech.Parts.Add(new Part { Name = $"Planet ({zp} teeth)", Shape = planetShape, Copies = Planets, Label = Lbl("P", planet.RootR, pinHoleR) });
        mech.Parts.Add(new Part
        {
            Name = $"Ring ({zr} teeth)",
            Shape = ringShape,
            Label = outerR - ringSize.RootR >= 6 ? new PartLabel("Ring", new Vec2(0, -(ringSize.RootR + outerR) / 2), Math.Min(4, (outerR - ringSize.RootR) * 0.45)) : null,
        });
        var carrierMarks = Shapes.TurnArrow(default, (holeR + a - pinHoleR) / 2, Math.PI * 0.62, Math.PI * 0.38);
        mech.Parts.Add(new Part { Name = "Carrier", Shape = carrierShape, Label = null, Marks = Held == PlanetaryHeld.Carrier ? [] : carrierMarks });
        Paths64? baseShape = null;
        if (Held == PlanetaryHeld.Ring)
        {
            // A base the ring is screwed to, with the sun's axle turning in its middle.
            var holes = new Paths64(bolts) { bore.Frame(default, true) };
            baseShape = Poly.Subtract([Poly.Circle(default, outerR, 0.003)], holes);
            mech.Parts.Add(new Part { Name = "Base", Shape = baseShape, Label = new PartLabel("Base", new Vec2(0, -(holeR + ringSize.RootR) / 2), 4) });
        }
        mech.Shopping.Add($"An axle for the sun: {bore.ShaftWords} dowel or rod, about {Math.Ceiling(4 * t + 15):0} mm long");
        mech.Shopping.Add($"{Planets} pins for the planets: {PlanetPinMm:0.#} mm dowel, {Math.Ceiling(2 * t + 1):0} mm long");
        if (bolts.Count > 0) mech.Shopping.Add($"4 small screws or bolts ({2 * boltR + 0.3:0.#} mm) for the ring");

        // ---- how it moves ----
        double ringPhase = RingPhase(f, zp, GearMaker.MeshPhase(zs, 0, zp, angles[0]), zr, angles[0]);
        var bodies = new List<Body>();
        if (baseShape is not null) bodies.Add(new Body("Base", baseShape, Tints.Frame, _ => Pose.Zero) { Faint = true });
        bodies.Add(new Body("Ring", ringShape, Tints.Of(3), x => new Pose(default, ringPhase + wr * x)) { IsInput = Held == PlanetaryHeld.Sun, Lines = [[Shapes.Polar(ringSize.RootR + 1, 0), Shapes.Polar(outerR - 1, 0)]] });
        bodies.Add(new Body("Sun", sunShape, Tints.Of(0), x => new Pose(default, ws * x)) { IsInput = Held != PlanetaryHeld.Sun, Lines = [[Shapes.Polar(holeR + 1, 0), Shapes.Polar(sun.RootR - 1, 0)]] });
        for (int j = 0; j < angles.Length; j++)
        {
            double g = angles[j];
            double phase0 = GearMaker.MeshPhase(zs, 0, zp, g);
            bodies.Add(new Body($"Planet {j + 1}", planetShape, Tints.Of(1), x => new Pose(Shapes.Polar(a, g + wc * x), phase0 + wp * x)) { Lines = [[Shapes.Polar(pinHoleR + 1, 0), Shapes.Polar(planet.RootR - 1, 0)]] });
        }
        // The carrier drawn see-through over the planets, turning with them.
        bodies.Add(new Body("Carrier", carrierShape, Tints.Of(2), x => new Pose(default, wc * x)) { Faint = true, Lines = Held == PlanetaryHeld.Carrier ? [] : carrierMarks });
        bodies.Add(new Body("Axle", [bore.ShaftOutline()], Tints.Steel, x => new Pose(default, ws * x)));

        // ---- the motion check ----
        int total = zs + zr;
        if (total % Planets != 0)
            mech.Findings.Add(new(Severity.Note, $"{Planets} planets can't be spaced evenly with these teeth (sun + ring = {total}, not a multiple of {Planets}): they're placed as evenly as the teeth allow."));
        else mech.Findings.Add(new(Severity.Good, $"The {Planets} planets are evenly spaced."));
        // Planets that touch each other.
        bool touching = false;
        for (int j = 0; j < angles.Length && Planets > 1; j++)
        {
            double gap = angles[(j + 1) % angles.Length] - angles[j];
            if (gap <= 0) gap += 2 * Math.PI;
            if (2 * a * Math.Sin(gap / 2) < 2 * planet.TipR + 1) touching = true;
        }
        if (touching) mech.Findings.Add(new(Severity.Bad, "The planets run into each other: use fewer or smaller planets, or a bigger sun."));
        double eps1 = GearMaker.ContactRatio(f, sun, planet, a, f.Alpha);
        double eps2 = GearMaker.ContactRatioInternal(f, planet, ringSize, ringSize.PitchR - planet.PitchR);
        foreach (var (eps, what) in new[] { (eps1, "Sun and planets"), (eps2, "Planets and ring") })
        {
            if (eps < 1) mech.Findings.Add(new(Severity.Bad, $"{what}: the teeth lose touch (contact ratio {eps:0.00})."));
            else if (eps < 1.2) mech.Findings.Add(new(Severity.Warn, $"{what}: only just enough teeth touching (contact ratio {eps:0.00})."));
            else mech.Findings.Add(new(Severity.Good, $"{what} mesh: {eps:0.00} pairs of teeth touching on average."));
        }
        // Turn it through one tooth of the sun (relative to the carrier), looking where planet 1 meets the sun and the ring.
        var badSun = new bool[24];
        var badRing = new bool[24];
        double rel = Math.Abs(ws - wc) > 1e-9 ? Math.Abs(ws - wc) : Math.Abs(wr - wc);
        double period = 2 * Math.PI / (Math.Abs(ws - wc) > 1e-9 ? zs : zr) / Math.Max(1e-9, rel);
        double worst = 0;
        for (int s = 0; s < 24; s++)
        {
            ct.ThrowIfCancellationRequested();
            double x = period * s / 24;
            var pc = Shapes.Polar(a, angles[0] + wc * x);
            var dir = pc / a;
            var pPlanet = Poly.Move(planetShape, new Pose(pc, GearMaker.MeshPhase(zs, 0, zp, angles[0]) + wp * x));
            var nearSun = Around(dir * sun.PitchR, 4 * m);
            var nearRing = Around(dir * ringSize.PitchR, 4 * m);
            double o1 = Poly.Overlap(Poly.Move(sunShape, new Pose(default, ws * x)), pPlanet, nearSun);
            double o2 = Poly.Overlap(Poly.Move(ringShape, new Pose(default, ringPhase + wr * x)), pPlanet, nearRing);
            badSun[s] = o1 > 1e-3;
            badRing[s] = o2 > 1e-3;
            worst = Math.Max(worst, Math.Max(o1, o2));
        }
        if (badSun.Any(b => b)) mech.Findings.Add(new(Severity.Bad, "The planets jam against the sun."));
        if (badRing.Any(b => b)) mech.Findings.Add(new(Severity.Bad, $"The planets jam in the ring (the ring needs more teeth than the planets: try bigger planets or a smaller module). {worst:0.##} mm² overlap at worst."));
        if (!badSun.Any(b => b) && !badRing.Any(b => b)) mech.Findings.Add(new(Severity.Good, "Sun, planets and ring turn together without jamming."));
        else
        {
            int Step(double x) => (int)Math.Floor((x % period + period) % period / period * 24) % 24;
            mech.MarkWhere(x => badSun[Step(x)] || badRing[Step(x)], Severity.Bad);
            mech.Clashing = x =>
            {
                var names = new List<string>();
                if (badSun[Step(x)]) names.AddRange(["Sun", "Planet 1"]);
                if (badRing[Step(x)]) names.AddRange(["Ring", "Planet 1"]);
                return names;
            };
        }
        foreach (var (z, name) in new[] { (zs, "The sun's teeth"), (zp, "The planets' teeth") })
            if (GearMaker.Undercut(f, z, 0))
                mech.Findings.Add(new(Severity.Note, $"{name} ({z}) are a little undercut (thinner at the root): fine for turning by hand; {GearMaker.NoUndercutTeeth(f)}+ teeth or a 25° pressure angle avoid it."));
        if (sun.RootR - holeR < 3) mech.Findings.Add(new(Severity.Bad, "The sun has almost nothing between its axle hole and its teeth: use a thinner axle or more sun teeth."));
        if (planet.RootR - pinHoleR < 2.5) mech.Findings.Add(new(Severity.Bad, "The planets have almost nothing between their pin hole and their teeth: use thinner pins."));
        if (m < 1.5 && t >= 2) mech.Findings.Add(new(Severity.Warn, $"Teeth this small (module {m:0.##}) snap easily in wood; 2 or more is safer."));

        mech.Summary = Held switch
        {
            PlanetaryHeld.Ring => $"Ring held: turn the sun {Fmt((double)(zs + zr) / zs)} times and the carrier goes round once, the same way.",
            PlanetaryHeld.Carrier => $"Carrier held: turn the sun {Fmt((double)zr / zs)} times and the ring goes round once, the other way.",
            _ => $"Sun held: turn the ring {Fmt((double)(zs + zr) / zr)} times and the carrier goes round once, the same way.",
        };
        mech.Panels.Add(new PlanarPanel
        {
            Title = "Looking along the axle",
            Bodies = bodies,
            Extent = new Rect2(-outerR, -outerR, outerR, outerR).Inflate(2),
            InputPivot = default,
            InputSense = 1,
        });
        return mech;

        static PartLabel? Lbl(string text, double rootR, double holeR) =>
            rootR - holeR > 5 ? new PartLabel(text, new Vec2(0, -(holeR + (rootR - holeR) * 0.5)), Math.Clamp((rootR - holeR) * 0.35, 2.5, 5)) : null;
        static Rect2 Around(Vec2 c, double r) => new(c.X - r, c.Y - r, c.X + r, c.Y + r);
        static string Fmt(double v) => Math.Abs(v - Math.Round(v)) < 0.005 ? $"{Math.Round(v):0}" : $"{v:0.##}";
    }

    /// <summary>The ring's starting angle so it meshes with a planet (turned to <paramref name="planetPhase"/>) whose centre is in direction <paramref name="dir"/>.</summary>
    public static double RingPhase(ToothForm f, int planetTeeth, double planetPhase, int ringTeeth, double dir)
    {
        // From MeshPhaseInRing: planetPhase = dir − (pr + ½)·2π/zp, with pr the ring's tooth phase at dir.
        double pp = (dir - planetPhase) / (2 * Math.PI / planetTeeth);
        double pr = pp - 0.5;
        pr -= Math.Floor(pr);
        return dir - pr * (2 * Math.PI / ringTeeth);
    }
}
