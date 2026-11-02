using Clipper2Lib;
using PhotonCanvas.Core;

namespace PhotonCanvas.Addon.Mechanism;

/// <summary>
/// The tooth shape that gears share so they mesh: the size of the teeth (module = pitch diameter ÷
/// teeth, in mm), the pressure angle, and the allowances a laser-cut gear needs.
/// </summary>
public sealed record ToothForm
{
    /// <summary>Module (mm): pitch diameter ÷ number of teeth. The distance from one tooth to the next is π × this.</summary>
    public double Module { get; init; } = 3;
    /// <summary>Pressure angle (degrees): 20 is the standard; 25 gives stouter teeth and less undercut on small gears.</summary>
    public double PressureAngleDeg { get; init; } = 20;
    /// <summary>Play between meshing teeth, measured along the pitch circle (mm): each gear's teeth are thinned by half of it.</summary>
    public double BacklashMm { get; init; } = 0.15;
    /// <summary>Room left under the tips of the other gear's teeth (× module).</summary>
    public double Clearance { get; init; } = 0.25;
    /// <summary>The round at the bottom of each tooth space (× module), as the corner of a gear-cutting rack leaves it.</summary>
    public double RootRound { get; init; } = 0.38;

    public double Alpha => PressureAngleDeg * Math.PI / 180;
    /// <summary>Distance from one tooth to the next along the pitch circle (mm).</summary>
    public double Pitch => Math.PI * Module;
}

/// <summary>One gear's sizes, all in mm (radii).</summary>
public sealed record GearSize(int Teeth, double PitchR, double BaseR, double TipR, double RootR, double Shift)
{
    /// <summary>How thick a tooth is across its tip (mm): too thin and it burns away or breaks.</summary>
    public double TipThickness { get; init; }
}

/// <summary>
/// Involute gear teeth, made the way a gear-cutting machine makes them: a rack (for an outside gear)
/// or a small cutter gear (for a ring gear) is rolled past the blank and everything it touches is
/// taken away. That gives true involute flanks, the rounded root, and, on gears with few teeth, the
/// same undercut a real gear would have, so the pieces really mesh. Gears are in maths axes (y up),
/// centred on the origin, with tooth 0 centred on the +x axis.
/// </summary>
public static class GearMaker
{
    /// <summary>Smallest number of teeth without undercut and without a profile shift.</summary>
    public static int NoUndercutTeeth(ToothForm f) => (int)Math.Ceiling(2 / (Math.Sin(f.Alpha) * Math.Sin(f.Alpha)) - 1e-9);

    /// <summary>The profile shift (× module) a gear needs so its teeth aren't undercut (0 when it has enough teeth).</summary>
    public static double ShiftAgainstUndercut(ToothForm f, int teeth) => Math.Max(0, 1 - teeth * Math.Sin(f.Alpha) * Math.Sin(f.Alpha) / 2);

    /// <summary>Is a gear with these teeth and shift undercut by the cutting rack.</summary>
    public static bool Undercut(ToothForm f, int teeth, double shift) => shift < 1 - teeth * Math.Sin(f.Alpha) * Math.Sin(f.Alpha) / 2 - 1e-6;

    public static double Inv(double a) => Math.Tan(a) - a;

    /// <summary>The angle whose involute function is <paramref name="v"/>.</summary>
    public static double InvInverse(double v)
    {
        if (v <= 0) return 0;
        double a = Math.Cbrt(3 * v); // inv(a) ≈ a³/3 for small angles
        for (int i = 0; i < 40; i++)
        {
            double t = Math.Tan(a), f = t - a - v, d = t * t;
            if (d < 1e-15) break;
            double next = Math.Clamp(a - f / d, 1e-6, Math.PI / 2 - 1e-6);
            if (Math.Abs(next - a) < 1e-14) { a = next; break; }
            a = next;
        }
        return a;
    }

    /// <summary>Sizes of an outside gear with <paramref name="teeth"/>, shift <paramref name="shift"/> (× module) and tips shortened by <paramref name="tipShorten"/> (× module).</summary>
    public static GearSize External(ToothForm f, int teeth, double shift, double tipShorten = 0)
    {
        double m = f.Module, a = f.Alpha;
        double rp = m * teeth / 2, rb = rp * Math.Cos(a), ra = rp + m * (1 + shift - tipShorten), rf = rp - m * (1 + f.Clearance - shift);
        double s = m * (Math.PI / 2 + 2 * shift * Math.Tan(a)) - f.BacklashMm / 2;
        double aa = Math.Acos(Math.Clamp(rb / ra, -1, 1));
        double tip = 2 * ra * (s / (2 * rp) + Inv(a) - Inv(aa));
        return new GearSize(teeth, rp, rb, ra, rf, shift) { TipThickness = tip };
    }

    /// <summary>Sizes of a ring gear (teeth on the inside): its tips are the inner circle, its roots the outer.</summary>
    public static GearSize Internal(ToothForm f, int teeth)
    {
        double m = f.Module, a = f.Alpha;
        double rp = m * teeth / 2, rb = rp * Math.Cos(a), ra = rp - m, rf = rp + m * (1 + f.Clearance);
        // A ring's tooth is the space of an outside gear turned inside out: thickest at its tip.
        double s = m * Math.PI / 2 - f.BacklashMm / 2;
        double aa = Math.Acos(Math.Clamp(rb / ra, -1, 1));
        double tip = 2 * ra * (s / (2 * rp) - Inv(a) + Inv(aa));
        return new GearSize(teeth, rp, rb, ra, rf, 0) { TipThickness = tip };
    }

    /// <summary>
    /// The teeth of an outside gear (no hole): a disc the size of the tip circle with every tooth space
    /// cut by a rolling rack. Tooth 0 is centred on +x.
    /// </summary>
    public static Paths64 ExternalTeeth(ToothForm f, int teeth, double shift, double tipShorten = 0, double backlashMm = double.NaN)
    {
        if (teeth < 3) throw new ArgumentOutOfRangeException(nameof(teeth), "A gear needs at least 3 teeth.");
        double b = double.IsNaN(backlashMm) ? f.BacklashMm : backlashMm;
        double m = f.Module, a = f.Alpha, ta = Math.Tan(a);
        double rp = m * teeth / 2, ra = rp + m * (1 + shift - tipShorten);
        // The tooth's thickness on the pitch circle, and the rack tooth that leaves it (as wide as the space).
        double s = m * (Math.PI / 2 + 2 * shift * ta) - b / 2;
        double er = Math.PI * m - s;
        var cutter = RackTooth(f, er, m * (1 + f.Clearance - shift), -(m * (1 + shift - tipShorten)) - 0.6 * m);
        // Roll it through: the rack moves along its pitch line as the gear turns, never slipping.
        double halfBack = cutter.Max(p => Math.Abs(p.X));
        double reach = halfBack + Math.Sqrt(Math.Max(0, ra * ra - (rp - m * (1 + f.Clearance - shift)) * (rp - m * (1 + f.Clearance - shift)))) + m;
        double phiMax = reach / rp;
        // Each step leaves a ridge about (radius of curvature) × step² ÷ 8 high: 0.006 rad keeps it under a micron.
        int n = Math.Clamp((int)Math.Ceiling(2 * phiMax / 0.006), 40, 1500);
        var space = new List<Paths64>(n + 1);
        for (int i = 0; i <= n; i++)
        {
            double phi = -phiMax + 2 * phiMax * i / n;
            // Only where the rack tooth reaches the blank does it cut anything.
            if (Nearest(cutter, -rp * phi, rp) >= ra) continue;
            double c = Math.Cos(phi), sn = Math.Sin(phi);
            var path = new Path64(cutter.Count);
            foreach (var p in cutter)
            {
                // Rack frame (u along the rack, v towards the gear's centre) → fixed → the gear's own frame.
                double X = p.X - rp * phi, Y = rp - p.Y;
                path.Add(Poly.P(X * c + Y * sn, -X * sn + Y * c));
            }
            space.Add([path]);
        }
        var oneSpace = Clipper.SimplifyPaths(Poly.UnionSweep(space), 15);
        var spaces = new Paths64();
        for (int k = 0; k < teeth; k++) spaces.AddRange(Poly.Move(oneSpace, 2 * Math.PI * k / teeth));
        var gear = Poly.Subtract([Poly.Circle(default, ra, 0.001)], Poly.Union(spaces));
        // The space was cut on +y; a tooth sits half a pitch round from it. Turn tooth 0 onto +x.
        return Poly.Move(gear, -(Math.PI / 2 + Math.PI / teeth));
    }

    /// <summary>
    /// The teeth of a ring gear: an annulus from its tip circle out to <paramref name="outerR"/>, its
    /// spaces cut by a small gear-shaped cutter rolling round inside (with <paramref name="cutterTeeth"/>
    /// teeth: use the planets' count, so they mesh as well as possible). Tooth 0 is centred on +x.
    /// </summary>
    public static Paths64 InternalTeeth(ToothForm f, int teeth, int cutterTeeth, double outerR)
    {
        if (cutterTeeth < 6 || teeth - cutterTeeth < 4) throw new ArgumentOutOfRangeException(nameof(cutterTeeth), "The ring needs a few more teeth than the gears inside it.");
        double m = f.Module;
        var size = Internal(f, teeth);
        double rp = size.PitchR, rpc = m * cutterTeeth / 2, dist = rp - rpc;
        // The cutter: an outside gear as thick as the ring's spaces (teeth thickened by half the play),
        // reaching the ring's root circle, its tip corners rounded.
        var cutter = ExternalTeeth(f, cutterTeeth, 0, -f.Clearance, -f.BacklashMm);
        double round = Math.Min(0.25 * m, 0.6);
        cutter = Clipper.SimplifyPaths(Poly.Grow(Poly.Grow(cutter, -round), round), 10);
        cutter = Poly.Move(cutter, Math.PI / 2); // tooth 0 pointing at +y, into the ring's space there
        // Roll it round inside: the ring turns φ, the cutter (same way) φ × teeth ÷ cutterTeeth. Only the
        // top space is needed (the others are copies of it), so only what the cutter does near it is kept:
        // everything a real cutter takes away is taken away, so a partial view of it is still right.
        double ri = size.TipR, rf = size.RootR;
        double rc = rpc + m * (1 + f.Clearance);
        double cosG = Math.Clamp((dist * dist + ri * ri - rc * rc) / (2 * dist * ri), -1, 1);
        double window = Math.Acos(cosG) + 1.5 * Math.PI / teeth;
        double half = Math.Min(Math.PI / 2, 1.5 * Math.PI / teeth);
        var box = new Rect2(-rf * Math.Sin(half) - 0.5 * m, ri * Math.Cos(half) - 0.5 * m, rf * Math.Sin(half) + 0.5 * m, rf + 0.5 * m);
        // The cutter turns about twice as fast as the ring relative to it: 0.01 rad steps leave ridges well under a micron.
        int n = Math.Clamp((int)Math.Ceiling(2 * window * Math.Max(1, (double)teeth / cutterTeeth - 1) / 0.01), 40, 2000);
        var cuts = new List<Paths64>(n + 1);
        for (int i = 0; i <= n; i++)
        {
            double phi = -window + 2 * window * i / n;
            double psi = phi * teeth / cutterTeeth;
            // In the ring's frame: the cutter's centre goes round by −φ, the cutter turns by ψ − φ.
            double cx = dist * Math.Sin(phi), cy = dist * Math.Cos(phi);
            if (cy + rc < ri * Math.Cos(half) - m) continue;
            cuts.Add(Poly.Clip(Poly.Move(cutter, psi - phi, cx, cy), box));
        }
        var oneSpace = Clipper.SimplifyPaths(Poly.UnionSweep(cuts), 15);
        var spaces = new Paths64();
        for (int k = 0; k < teeth; k++) spaces.AddRange(Poly.Move(oneSpace, 2 * Math.PI * k / teeth));
        var ring = Poly.Subtract([Poly.Circle(default, outerR, 0.002)], [Poly.Circle(default, ri, 0.001)]);
        ring = Poly.Subtract(ring, Poly.Union(spaces));
        return Poly.Move(ring, -(Math.PI / 2 + Math.PI / teeth));
    }

    /// <summary>
    /// A straight rack: <paramref name="teeth"/> teeth along x on top of a bar <paramref name="bodyMm"/> deep below
    /// the roots. The pitch line is y = 0 and tooth 0 is centred on x = 0; teeth point up (+y).
    /// </summary>
    public static Paths64 RackTeeth(ToothForm f, int teeth, double bodyMm, double endMm)
    {
        double m = f.Module, p = Math.PI * m;
        // A rack's tooth is the cutting rack's space: thinned by half the play, addendum 1 module, room under it.
        double s = p / 2 - f.BacklashMm / 2;
        double add = m, ded = m * (1 + f.Clearance);
        // One tooth space as a cutter tooth, pointing down (−y) from above the tips into the body.
        var gap = RackTooth(f, p - s, ded, -add - 0.6 * m);
        var gapPath = new Path64(gap.Count);
        foreach (var q in gap) gapPath.Add(Poly.P(q.X, -q.Y));
        // The toothed strip, and the bar under it (reaching on past the teeth at root height, for holding it).
        var bar = new Paths64
        {
            Poly.Rect(-p / 2 - endMm, -ded - bodyMm, (teeth - 0.5) * p + endMm, -ded),
            Poly.Rect(-p / 2, -ded - 0.01, (teeth - 0.5) * p, add),
        };
        var cuts = new Paths64();
        for (int k = -1; k < teeth; k++) cuts.AddRange(Poly.Move([gapPath], 0, (k + 0.5) * p, 0));
        return Poly.Subtract(Poly.Union(bar), Poly.Union(cuts));
    }

    /// <summary>How near the gear's centre the rack tooth comes when moved <paramref name="shift"/> along (fixed frame: the rolling line at y = <paramref name="rp"/>).</summary>
    static double Nearest(List<Vec2> tooth, double shift, double rp)
    {
        double best = double.MaxValue;
        for (int i = 0; i < tooth.Count; i++)
        {
            var a = new Vec2(tooth[i].X + shift, rp - tooth[i].Y);
            var b = new Vec2(tooth[(i + 1) % tooth.Count].X + shift, rp - tooth[(i + 1) % tooth.Count].Y);
            best = Math.Min(best, Geom.SegmentDistance(default, a, b));
        }
        return best;
    }

    /// <summary>
    /// One tooth of the cutting rack, in its own frame: u along the rack, v towards the work, the rolling
    /// line at v = 0. It is <paramref name="width"/> wide on that line, reaches <paramref name="depth"/> in,
    /// with its tip corners rounded, and goes back to <paramref name="back"/> (outside the work).
    /// </summary>
    static List<Vec2> RackTooth(ToothForm f, double width, double depth, double back)
    {
        double a = f.Alpha, ta = Math.Tan(a), ca = Math.Cos(a), sa = Math.Sin(a);
        double half(double v) => width / 2 - v * ta;
        // A cutter too narrow to reach its full depth comes to a point earlier.
        double tipHalf = half(depth);
        if (tipHalf < 1e-4)
        {
            depth = (width / 2 - 1e-4) / ta;
            tipHalf = 1e-4;
        }
        double rho = Math.Min(f.RootRound * f.Module, 0.95 * tipHalf * ca / (1 - sa));
        var o = new List<Vec2> { new(half(back), back) };
        if (rho > 1e-6)
        {
            double vc = depth - rho, uc = width / 2 - vc * ta - rho / ca;
            int steps = 12;
            for (int i = 0; i <= steps; i++)
            {
                double t = a + (Math.PI / 2 - a) * i / steps;
                o.Add(new(uc + rho * Math.Cos(t), vc + rho * Math.Sin(t)));
            }
            for (int i = 0; i <= steps; i++)
            {
                double t = Math.PI / 2 + (Math.PI / 2 - a) * i / steps;
                o.Add(new(-uc + rho * Math.Cos(t), vc + rho * Math.Sin(t)));
            }
        }
        else
        {
            o.Add(new(tipHalf, depth));
            o.Add(new(-tipHalf, depth));
        }
        o.Add(new(-half(back), back));
        return o;
    }

    /// <summary>
    /// Two outside gears meshing: the working pressure angle and the distance between their centres.
    /// With no profile shift that's simply the two pitch radii added up.
    /// </summary>
    public static (double AlphaW, double Centre) Mesh(ToothForm f, int z1, double x1, int z2, double x2)
    {
        double a = f.Alpha;
        double invW = Inv(a) + 2 * Math.Tan(a) * (x1 + x2) / (z1 + z2);
        double aw = Math.Abs(x1 + x2) < 1e-12 ? a : InvInverse(invW);
        return (aw, f.Module * (z1 + z2) / 2 * Math.Cos(a) / Math.Cos(aw));
    }

    /// <summary>How much the tips of a shifted pair must be shortened (× module) to keep the room under them.</summary>
    public static double TipShortening(ToothForm f, int z1, double x1, int z2, double x2)
    {
        var (_, centre) = Mesh(f, z1, x1, z2, x2);
        return Math.Max(0, x1 + x2 - (centre - f.Module * (z1 + z2) / 2) / f.Module);
    }

    /// <summary>Contact ratio of two outside gears: how many pairs of teeth are touching on average (above 1.2 runs smoothly).</summary>
    public static double ContactRatio(ToothForm f, GearSize g1, GearSize g2, double centre, double alphaW) =>
        (Math.Sqrt(Math.Max(0, g1.TipR * g1.TipR - g1.BaseR * g1.BaseR)) + Math.Sqrt(Math.Max(0, g2.TipR * g2.TipR - g2.BaseR * g2.BaseR)) - centre * Math.Sin(alphaW))
        / (Math.PI * f.Module * Math.Cos(f.Alpha));

    /// <summary>Contact ratio of a gear inside a ring.</summary>
    public static double ContactRatioInternal(ToothForm f, GearSize pinion, GearSize ring, double centre) =>
        (Math.Sqrt(Math.Max(0, pinion.TipR * pinion.TipR - pinion.BaseR * pinion.BaseR)) - Math.Sqrt(Math.Max(0, ring.TipR * ring.TipR - ring.BaseR * ring.BaseR)) + centre * Math.Sin(f.Alpha))
        / (Math.PI * f.Module * Math.Cos(f.Alpha));

    /// <summary>Contact ratio of a gear on a rack.</summary>
    public static double ContactRatioRack(ToothForm f, GearSize pinion) =>
        (Math.Sqrt(Math.Max(0, pinion.TipR * pinion.TipR - pinion.BaseR * pinion.BaseR)) - pinion.BaseR * Math.Tan(f.Alpha) + f.Module * (1 - pinion.Shift) / Math.Sin(f.Alpha))
        / (Math.PI * f.Module * Math.Cos(f.Alpha));

    /// <summary>
    /// How far gear B must be turned from where it starts (radians) so it meshes with gear A: A's tooth
    /// grid is at <paramref name="angleA"/>, B sits in direction <paramref name="dir"/> from A.
    /// Outside gears turn opposite ways; a tooth of one faces a space of the other on the line between them.
    /// </summary>
    public static double MeshPhase(int teethA, double angleA, int teethB, double dir)
    {
        double phaseA = Frac((dir - angleA) / (2 * Math.PI / teethA));
        return dir + Math.PI - (0.5 - phaseA) * (2 * Math.PI / teethB);
    }

    /// <summary>The same for a gear (B) inside a ring (A), whose centre is in direction <paramref name="dir"/> from the ring's: they turn the same way.</summary>
    public static double MeshPhaseInRing(int ringTeeth, double ringAngle, int teethB, double dir)
    {
        double phaseRing = Frac((dir - ringAngle) / (2 * Math.PI / ringTeeth));
        return dir - (phaseRing + 0.5) * (2 * Math.PI / teethB);
    }

    static double Frac(double v) => v - Math.Floor(v);
}
