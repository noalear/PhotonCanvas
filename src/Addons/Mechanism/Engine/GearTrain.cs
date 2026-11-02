using System.Collections.Concurrent;
using System.Collections.Immutable;
using Clipper2Lib;
using PhotonCanvas.Core;

namespace PhotonCanvas.Addon.Mechanism;

/// <summary>How a gear in a train is placed: meshing with the one before it, or fixed to its axle (a compound gear, one layer further out).</summary>
public enum GearLink { Meshes, SameShaft }

/// <summary>One gear of a train.</summary>
public sealed record GearRow
{
    public int Teeth { get; init; } = 24;
    public GearLink Link { get; init; } = GearLink.Meshes;
    /// <summary>Which way from the gear before it this one sits (degrees: 0 = right, 90 = up); for a meshing gear.</summary>
    public double AngleDeg { get; init; }
    /// <summary>Its hole for the axle; null = a round one that turns freely on the axle.</summary>
    public Hole? Hole { get; init; }
    /// <summary>A handle hole in it; null = gear A has one if <see cref="GearTrainSpec.Handle"/> says so, other gears none.</summary>
    public Crank? Handle { get; init; }
}

/// <summary>Generated teeth, kept so a change elsewhere doesn't cut them again.</summary>
static class GearCache
{
    static readonly ConcurrentDictionary<string, Paths64> Teeth = new();

    static Paths64 Get(string key, Func<Paths64> make)
    {
        if (Teeth.Count > 96) Teeth.Clear();
        return Teeth.GetOrAdd(key, _ => make());
    }

    static string Key(ToothForm f) => $"{f.Module:R}|{f.PressureAngleDeg:R}|{f.BacklashMm:R}|{f.Clearance:R}|{f.RootRound:R}";

    public static Paths64 External(ToothForm f, int z, double x, double k) => Get($"e|{Key(f)}|{z}|{x:R}|{k:R}", () => GearMaker.ExternalTeeth(f, z, x, k));
    public static Paths64 Internal(ToothForm f, int z, int cutter, double outer) => Get($"i|{Key(f)}|{z}|{cutter}|{outer:R}", () => GearMaker.InternalTeeth(f, z, cutter, outer));
    public static Paths64 Rack(ToothForm f, int teeth, double body, double end) => Get($"r|{Key(f)}|{teeth}|{body:R}|{end:R}", () => GearMaker.RackTeeth(f, teeth, body, end));
}

/// <summary>
/// A train of spur gears: each one meshes with the one before it, or is fixed on the same axle as it
/// (a compound gear, which lets a short train change speed a lot). With a plate to hold the axles,
/// spacers, and a handle hole in the first gear.
/// </summary>
public sealed record GearTrainSpec : MechSpec
{
    public ToothForm Form { get; init; } = new() { Module = 2.5 };
    /// <summary>A big gear with the handle driving a small one: it spins three times as fast.</summary>
    public ImmutableList<GearRow> Gears { get; init; } = [new() { Teeth = 36 }, new() { Teeth = 12 }];
    /// <summary>Shift the teeth of small gears outwards so they aren't undercut (stronger teeth; the axles move a little apart).</summary>
    public bool AvoidUndercut { get; init; } = true;
    public Axle Axle { get; init; } = new();
    /// <summary>A plate the axles are glued into, with a spacer under each gear.</summary>
    public bool Plate { get; init; } = true;
    /// <summary>Windows in the bigger gears, leaving spokes.</summary>
    public bool Spokes { get; init; } = true;
    /// <summary>A hole near gear A's rim for a peg to turn it by, unless gear A's own row says otherwise (<see cref="GearRow.Handle"/>).</summary>
    public bool Handle { get; init; } = true;

    /// <summary>Gear <paramref name="i"/>'s handle, if it has one.</summary>
    public Crank? HandleOf(int i) => i < 0 || i >= Gears.Count ? null
        : Gears[i].Handle is { } c ? (c.On ? c : null)
        : i == 0 && Handle ? new Crank() : null;

    public static string Letter(int i) => i < 26 ? ((char)('A' + i)).ToString() : $"G{i + 1}";

    /// <summary>Where each gear goes, which layer, how fast it turns and its starting angle.</summary>
    public sealed record Placed(int Index, int Teeth, double Shift, double TipShorten, Vec2 Centre, int Layer, double Speed, double Phase, GearSize Size);

    public List<Placed> Place()
    {
        var f = Form;
        int n = Gears.Count;
        var x = Gears.Select(g => AvoidUndercut ? GearMaker.ShiftAgainstUndercut(f, g.Teeth) : 0).ToArray();
        var k = new double[n];
        var centre = new Vec2[n];
        var layer = new int[n];
        var speed = new double[n];
        var phase = new double[n];
        speed[0] = 1;
        for (int i = 1; i < n; i++)
        {
            int zp = Gears[i - 1].Teeth, z = Gears[i].Teeth;
            if (Gears[i].Link == GearLink.Meshes)
            {
                var (_, c) = GearMaker.Mesh(f, zp, x[i - 1], z, x[i]);
                double dir = Gears[i].AngleDeg * Math.PI / 180;
                centre[i] = centre[i - 1] + Shapes.Polar(c, dir);
                layer[i] = layer[i - 1];
                speed[i] = -speed[i - 1] * zp / z;
                phase[i] = GearMaker.MeshPhase(zp, phase[i - 1], z, dir);
                double ks = GearMaker.TipShortening(f, zp, x[i - 1], z, x[i]);
                k[i - 1] = Math.Max(k[i - 1], ks);
                k[i] = Math.Max(k[i], ks);
            }
            else
            {
                centre[i] = centre[i - 1];
                layer[i] = layer[i - 1] + 1;
                speed[i] = speed[i - 1];
                phase[i] = phase[i - 1];
            }
        }
        return [.. Enumerable.Range(0, n).Select(i => new Placed(i, Gears[i].Teeth, x[i], k[i], centre[i], layer[i], speed[i], phase[i], GearMaker.External(f, Gears[i].Teeth, x[i], k[i])))];
    }

    public override Mechanism Build(CancellationToken ct = default)
    {
        if (Gears.Count < 1) throw new InvalidOperationException("Add a gear.");
        if (Gears.Any(g => g.Teeth < 6)) throw new InvalidOperationException("Each gear needs at least 6 teeth.");
        var f = Form;
        double m = f.Module, t = ThicknessMm;
        var placed = Place();
        var mech = new Mechanism { Title = "Gears" };
        string mat = Material;
        var bores = placed.Select(g => new Bore(Gears[g.Index].Hole, Axle, true, mat)).ToArray();
        var holeR = bores.Select(b => b.R).ToArray();

        // ---- the gears themselves, each with its hole and any handle hole ----
        var shapes = new Paths64[placed.Count];
        var handleAt = new Vec2?[placed.Count];
        var pegs = new Hole?[placed.Count];
        for (int i = 0; i < placed.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var g = placed[i];
            var teeth = GearCache.External(f, g.Teeth, g.Shift, g.TipShorten);
            var holes = new Paths64 { bores[i].Cut(default) };
            double rimR = g.Size.RootR - Math.Max(3, 1.25 * m);
            Vec2? handle = null;
            double pegR = 0;
            if (HandleOf(i) is { } crank)
            {
                var peg = crank.PegHole(Axle);
                pegR = peg.OuterR(mat);
                var (at, why) = crank.Place(holeR[i], rimR - 1, pegR, $"Gear {Letter(i)}");
                if (why is not null) mech.Findings.Add(new Finding(crank.RadiusMm > 0 ? Severity.Warn : Severity.Note, why));
                handle = at;
                if (at is not null) pegs[i] = peg;
            }
            if (Spokes && Shapes.SpokeWindows(holeR[i] + Math.Max(4, 0.4 * bores[i].Shaft.SizeMm), rimR, rimR > 32 ? 5 : 4) is { } windows)
            {
                // The handle's hole sits on a spoke (one points its way), in a round pad.
                if (handle is { } pad) windows = Poly.Grow(Poly.Grow(Poly.Subtract(windows, [Poly.Circle(pad, pegR + 3, 0.003)]), -1), 1);
                holes.AddRange(windows);
            }
            if (handle is { } at2)
            {
                handleAt[i] = at2;
                holes.Add(pegs[i]!.Cut(at2, mat));
            }
            shapes[i] = Poly.Subtract(teeth, holes);
            string name = $"Gear {Letter(i)} ({g.Teeth} teeth)";
            var tick = new List<Vec2> { Shapes.Polar(holeR[i] + 1, 0), Shapes.Polar(Math.Min(holeR[i] + 4, g.Size.RootR - 1), 0) };
            mech.Parts.Add(new Part
            {
                Name = name,
                Shape = shapes[i],
                Label = LabelFor(Letter(i), g.Size.RootR, holeR[i], handleAt[i] is not null),
                Marks = g.Index == 0 ? [tick] : [],
            });
        }

        // ---- the plate, spacers and axles ----
        // An axle turns in the plate when a gear on it grips it; otherwise it's glued in and the gears turn on it.
        var axles = placed.GroupBy(g => (Math.Round(g.Centre.X, 3), Math.Round(g.Centre.Y, 3))).Select(grp => grp.ToList()).ToList();
        Bore BoreOf(List<Placed> a) => a.Select(g => bores[g.Index]).FirstOrDefault(b => b.Chosen) ?? bores[a[0].Index];
        bool AxleTurns(List<Placed> a) => a.Any(g => !bores[g.Index].Turns);
        foreach (var a in axles.Where(a => a.Count > 1))
        {
            var shafts = a.Select(g => bores[g.Index].Shaft).Distinct().ToList();
            if (shafts.Count > 1)
            {
                mech.Findings.Add(new(Severity.Bad, $"{Say.And(a.Select(g => Letter(g.Index)))} share an axle but have different holes for it ({Say.And(shafts.Select(x => x.Words))}). Give them the same shape and size."));
                mech.Mark(0, Mechanism.StripSamples, Severity.Bad);
            }
        }
        var extent = Rect2.Empty;
        foreach (var g in placed) extent = extent.Union(new Rect2(g.Centre.X - g.Size.TipR, g.Centre.Y - g.Size.TipR, g.Centre.X + g.Size.TipR, g.Centre.Y + g.Size.TipR));
        Paths64? plate = null;
        if (Plate)
        {
            var box = extent.Inflate(6);
            var outline = new Paths64 { Shapes.RoundRect(box.MinX, box.MinY, box.MaxX, box.MaxY, 6) };
            var holes = new Paths64();
            foreach (var a in axles) holes.Add(BoreOf(a).Frame(a[0].Centre, AxleTurns(a)));
            foreach (var corner in new[] { new Vec2(box.MinX + 5, box.MinY + 5), new Vec2(box.MaxX - 5, box.MinY + 5), new Vec2(box.MaxX - 5, box.MaxY - 5), new Vec2(box.MinX + 5, box.MaxY - 5) })
                if (placed.All(g => g.Centre.Dist(corner) > g.Size.TipR + 3)) holes.Add(Poly.Circle(corner, 1.75, 0.003));
            plate = Poly.Subtract(outline, holes);
            mech.Parts.Add(new Part { Name = "Plate", Shape = plate, Label = new PartLabel("Plate", new(box.MinX + 14, box.MinY + 4), 3) });
            // Spacers: one under each gear nearest the plate, one kind for each kind of axle.
            double smallest = placed.Min(g => g.Size.RootR) - 1;
            var spacerKinds = axles.GroupBy(a => BoreOf(a).Shaft).ToList();
            foreach (var kind in spacerKinds)
            {
                var b = BoreOf(kind.First());
                double r = Math.Min(b.R + 4, smallest);
                int copies = kind.Sum(a => 1 + a.Min(g => g.Layer));
                string spacerName = spacerKinds.Count == 1 ? "Spacer" : $"Spacer ({b.Shaft.Short})";
                mech.Parts.Add(new Part { Name = spacerName, Shape = Poly.Subtract([Poly.Circle(default, r, 0.003)], [b.Frame(default, true)]), Copies = copies });
            }
            extent = box;
        }
        foreach (var a in axles)
        {
            int layers = a.Max(g => g.Layer) + 1;
            double len = (Plate ? t + (1 + a.Min(g => g.Layer)) * t : 0) + (layers - a.Min(g => g.Layer)) * t + 3;
            string how = !Plate ? "" : AxleTurns(a) ? " (it turns in the plate)" : " (glued into the plate)";
            mech.Shopping.Add($"An axle for {string.Join(" and ", a.Select(g => Letter(g.Index)))}: {BoreOf(a).ShaftWords} dowel or rod, about {Math.Ceiling(len + (Plate ? 0 : 10)):0} mm long{how}");
        }
        // Gears sharing an axle turn as one: glued face to face, unless every one of them grips the axle.
        var shared = axles.Where(a => a.Count > 1 && a.Any(g => bores[g.Index].Turns)).Select(a => Say.And(a.Select(g => Letter(g.Index)))).ToList();
        if (shared.Count > 0) mech.Shopping.Add($"Wood glue: {string.Join("; ", shared)} glued face to face, so they turn as one");
        for (int i = 0; i < placed.Count; i++)
            if (pegs[i] is { } peg) mech.Shopping.Add($"A handle peg for gear {Letter(i)}: {peg.Shaft.Words} dowel, about 25 mm long");
        var drawn = new List<string>();
        for (int i = 0; i < placed.Count; i++)
        {
            if (bores[i].Chosen) drawn.Add($"{Letter(i)}'s axle hole {bores[i].Words}");
            if (pegs[i] is { } peg && (Gears[i].Handle?.Hole is not null)) drawn.Add($"{Letter(i)}'s handle hole {peg.Words(mat)}");
        }
        if (drawn.Count > 0) mech.Findings.Add(new(Severity.Note, $"Holes drawn for {MaterialWords.Of(mat)}: {string.Join("; ", drawn)}."));

        // ---- how it moves ----
        var bodies = new List<Body>();
        if (plate is not null) bodies.Add(new Body("Plate", plate, Tints.Frame, _ => Pose.Zero) { Faint = true });
        foreach (var g in placed.OrderBy(g => g.Layer))
        {
            var mark = new List<Vec2> { Shapes.Polar(holeR[g.Index] + 1, 0), Shapes.Polar(g.Size.RootR - 1, 0) };
            var gg = g;
            bodies.Add(new Body(Letter(g.Index), shapes[g.Index], Tints.Of(g.Index), a => new Pose(gg.Centre, gg.Phase + gg.Speed * a))
            {
                Lines = [mark],
                IsInput = g.Index == 0,
            });
        }
        foreach (var a in axles)
        {
            var g0 = a[0];
            bool turns = AxleTurns(a);
            bodies.Add(new Body("Axle", [BoreOf(a).ShaftOutline()], Tints.Steel, x => new Pose(g0.Centre, turns ? g0.Phase + g0.Speed * x : 0)));
        }
        for (int i = 0; i < placed.Count; i++)
        {
            if (handleAt[i] is not { } h || pegs[i] is not { } peg) continue;
            var g = placed[i];
            bodies.Add(new Body("Handle", [peg.Shaft.Outline(default)], Tints.Steel, a => new Pose(new Pose(g.Centre, g.Phase + g.Speed * a).Apply(h), g.Phase + g.Speed * a)));
        }

        // ---- the motion check ----
        var clashes = CheckMeshes(mech, placed, shapes, ct);
        CheckLayout(mech, placed, handleAt, pegs);
        CheckSizes(mech, placed, holeR);

        var first = placed[0];
        var last = placed[^1];
        mech.Summary = placed.Count == 1 ? $"One gear, {first.Teeth} teeth, {2 * first.Size.TipR:0.#} mm across."
            : $"{Letter(last.Index)} turns {Ratio(Math.Abs(last.Speed))} {(last.Speed > 0 ? "the same way as" : "the other way from")} A.";
        mech.Panels.Add(new PlanarPanel
        {
            Title = "Looking along the axles",
            Bodies = bodies,
            Extent = extent.Inflate(2),
            InputPivot = first.Centre,
            InputSense = 1,
        });
        mech.Clashing = clashes;
        return mech;
    }

    /// <summary>"3×", "1.5×" slower or faster.</summary>
    public static string Ratio(double speed)
    {
        if (Math.Abs(speed - 1) < 1e-9) return "at the same speed";
        double r = speed > 1 ? speed : 1 / speed;
        string n = Math.Abs(r - Math.Round(r)) < 0.005 ? $"{Math.Round(r):0}" : $"{r:0.##}";
        return speed > 1 ? $"{n}× faster" : $"{n}× slower";
    }

    static PartLabel? LabelFor(string letter, double rootR, double holeR, bool handle)
    {
        double room = rootR - holeR;
        if (room < 5) return null;
        double h = Math.Clamp(room * 0.35, 2.5, 6);
        // Under the hub, away from the handle hole (on +x) and the turn mark.
        return new PartLabel(letter, new Vec2(0, -(holeR + room * (handle ? 0.45 : 0.5))), h);
    }

    /// <summary>Teeth that bind, how much play there really is, and how smoothly each pair runs.</summary>
    Func<double, IReadOnlyCollection<string>> CheckMeshes(Mechanism mech, List<Placed> placed, Paths64[] shapes, CancellationToken ct)
    {
        var f = Form;
        double m = f.Module;
        var clashAt = new List<(int A, int B, bool[] Bad, double Period, double Speed)>();
        for (int i = 1; i < placed.Count; i++)
        {
            if (Gears[i].Link != GearLink.Meshes) continue;
            ct.ThrowIfCancellationRequested();
            var a = placed[i - 1];
            var b = placed[i];
            string pair = $"{Letter(a.Index)} and {Letter(b.Index)}";
            var (aw, c) = GearMaker.Mesh(f, a.Teeth, a.Shift, b.Teeth, b.Shift);
            double eps = GearMaker.ContactRatio(f, a.Size, b.Size, c, aw);
            if (eps < 1) mech.Findings.Add(new(Severity.Bad, $"{pair}: the teeth lose touch between one pair and the next (contact ratio {eps:0.00}). Use more teeth or a smaller pressure angle."));
            else if (eps < 1.2) mech.Findings.Add(new(Severity.Warn, $"{pair}: only just enough teeth touching (contact ratio {eps:0.00}); it may click. More teeth run smoother."));
            else mech.Findings.Add(new(Severity.Good, $"{pair} mesh: {eps:0.00} pairs of teeth touching on average, {c:0.##} mm between axles."));
            // Turn them through one tooth of the driver, looking where they meet for any overlap.
            var dir = (b.Centre - a.Centre) / Math.Max(1e-9, (b.Centre - a.Centre).Length);
            var pitchPoint = a.Centre + dir * (c * a.Teeth / (a.Teeth + b.Teeth));
            var near = new Rect2(pitchPoint.X - 4 * m, pitchPoint.Y - 4 * m, pitchPoint.X + 4 * m, pitchPoint.Y + 4 * m);
            const int Steps = 24;
            var bad = new bool[Steps];
            double period = 2 * Math.PI / a.Teeth / Math.Abs(a.Speed);
            double worst = 0;
            for (int s = 0; s < Steps; s++)
            {
                double input = period * s / Steps;
                var pa = Poly.Move(Poly.Clip(Poly.Move(shapes[a.Index], a.Phase + a.Speed * input, a.Centre.X, a.Centre.Y), near), 0);
                var pb = Poly.Clip(Poly.Move(shapes[b.Index], b.Phase + b.Speed * input, b.Centre.X, b.Centre.Y), near);
                double o = Poly.Overlap(pa, pb);
                worst = Math.Max(worst, o);
                bad[s] = o > 1e-3;
            }
            if (bad.Any(x => x))
            {
                mech.Findings.Add(new(Severity.Bad, $"{pair}: the teeth jam ({worst:0.##} mm² overlap at worst)."));
                clashAt.Add((a.Index, b.Index, bad, period, a.Speed));
                mech.MarkWhere(input => bad[(int)Math.Floor((input % period + period) % period / period * Steps) % Steps], Severity.Bad);
            }
            else
            {
                // The play: how far B turns either way before its teeth touch A's.
                double play = PlayMm(shapes[a.Index], shapes[b.Index], a, b, near);
                if (play < 0.02) mech.Findings.Add(new(Severity.Warn, $"{pair}: almost no play between the teeth; they may bind if the axles are a little off. Allow more play."));
                else mech.Findings.Add(new(Severity.Good, $"{pair} turn freely, with about {play:0.##} mm of play between the teeth."));
            }
        }
        return input =>
        {
            var names = new List<string>();
            foreach (var (a, b, bad, period, _) in clashAt)
            {
                int s = (int)Math.Floor((input % period + period) % period / period * bad.Length) % bad.Length;
                if (bad[s]) { names.Add(Letter(a)); names.Add(Letter(b)); }
            }
            return names;
        };
    }

    /// <summary>The play between two meshing gears at the start, along the pitch circle (mm): B turned each way until it touches.</summary>
    static double PlayMm(Paths64 shapeA, Paths64 shapeB, Placed a, Placed b, Rect2 near)
    {
        var pa = Poly.Clip(Poly.Move(shapeA, a.Phase, a.Centre.X, a.Centre.Y), near);
        double Touch(int side)
        {
            double lo = 0, hi = 4 * Math.PI / b.Teeth / 4;
            if (Poly.Overlap(pa, Poly.Clip(Poly.Move(shapeB, b.Phase + side * hi, b.Centre.X, b.Centre.Y), near)) < 1e-4) return hi;
            for (int i = 0; i < 14; i++)
            {
                double mid = (lo + hi) / 2;
                if (Poly.Overlap(pa, Poly.Clip(Poly.Move(shapeB, b.Phase + side * mid, b.Centre.X, b.Centre.Y), near)) < 1e-4) lo = mid; else hi = mid;
            }
            return lo;
        }
        return (Touch(1) + Touch(-1)) * b.Size.PitchR;
    }

    /// <summary>Gears that don't mesh but touch, and gears that rub on another gear's axle or the handle.</summary>
    void CheckLayout(Mechanism mech, List<Placed> placed, Vec2?[] handleAt, Hole?[] pegs)
    {
        bool any = false;
        for (int i = 0; i < placed.Count; i++)
            for (int j = i + 1; j < placed.Count; j++)
            {
                var a = placed[i];
                var b = placed[j];
                bool meshing = j == i + 1 && Gears[j].Link == GearLink.Meshes;
                bool sameAxle = a.Centre.Dist(b.Centre) < 1e-6;
                double d = a.Centre.Dist(b.Centre);
                if (!meshing && !sameAxle && a.Layer == b.Layer && d < a.Size.TipR + b.Size.TipR + 0.5)
                {
                    mech.Findings.Add(new(Severity.Bad, $"{Letter(a.Index)} and {Letter(b.Index)} are in the same layer and run into each other. Turn {Letter(b.Index)} to another angle."));
                    mech.Mark(0, Mechanism.StripSamples, Severity.Bad);
                    any = true;
                }
            }
        // An axle reaches from the plate out to the front of the last gear on it.
        var axles = placed.GroupBy(o => (Math.Round(o.Centre.X, 3), Math.Round(o.Centre.Y, 3))).Select(x => x.ToList()).ToList();
        foreach (var g in placed)
            foreach (var axle in axles)
            {
                if (axle[0].Centre.Dist(g.Centre) < 1e-6 || g.Layer > axle.Max(o => o.Layer)) continue;
                if (g.Centre.Dist(axle[0].Centre) < g.Size.TipR + Axle.SizeMm / 2 + 0.5)
                {
                    mech.Findings.Add(new(Severity.Bad, $"{Letter(g.Index)}'s teeth rub on the axle of {string.Join(" and ", axle.Select(o => Letter(o.Index)))}."));
                    mech.Mark(0, Mechanism.StripSamples, Severity.Bad);
                    any = true;
                }
            }
        for (int i = 0; i < placed.Count; i++)
        {
            if (handleAt[i] is not { } h || pegs[i] is not { } peg) continue;
            // The peg sticks out of its gear towards you, sweeping a circle round the gear's axle.
            var g0 = placed[i];
            double r = h.Length, pegR = peg.OuterR(Material);
            foreach (var p in placed.Where(p => p.Layer > g0.Layer))
            {
                double d = p.Centre.Dist(g0.Centre);
                if (d - p.Size.TipR < r + pegR + 0.5 && d + p.Size.TipR > r - pegR - 0.5)
                {
                    string me = Letter(g0.Index), them = Letter(p.Index);
                    mech.Findings.Add(new(Severity.Warn, $"The handle peg in {me} would hit {them}, which sits in front of {me}. Leave the handle out, move it, or turn {me} with {them}'s axle."));
                    any = true;
                }
            }
        }
        if (!any && placed.Count > 1) mech.Findings.Add(new(Severity.Good, "No gear runs into another gear or another gear's axle."));
    }

    /// <summary>Undercut, pointed tips, fragile small teeth, thin hubs.</summary>
    void CheckSizes(Mechanism mech, List<Placed> placed, double[] holeR)
    {
        var f = Form;
        double m = f.Module;
        foreach (var g in placed)
        {
            string name = Letter(g.Index);
            if (GearMaker.Undercut(f, g.Teeth, g.Shift))
                mech.Findings.Add(new(Severity.Warn, $"{name} ({g.Teeth} teeth) is undercut: its teeth are thinner at the root and weaker. Tick “Avoid undercut”, or use at least {GearMaker.NoUndercutTeeth(f)} teeth."));
            else if (g.Shift > 0)
                mech.Findings.Add(new(Severity.Note, $"{name}'s teeth are shifted out by {g.Shift * m:0.##} mm so they aren't undercut."));
            if (g.Size.TipThickness < Math.Max(0.3, 0.2 * m))
                mech.Findings.Add(new(Severity.Warn, $"{name}'s tooth tips come to a point ({g.Size.TipThickness:0.##} mm across): the laser may burn them away. Use more teeth or a bigger module."));
            if (g.Size.RootR - holeR[g.Index] < 3)
                mech.Findings.Add(new(Severity.Bad, $"{name} has almost nothing between its axle hole and its teeth. Use a thinner axle or more teeth."));
        }
        if (m < 1.5 && ThicknessMm >= 2)
            mech.Findings.Add(new(Severity.Warn, $"Teeth this small (module {m:0.##}) snap easily in wood; 2 or more is safer for {ThicknessMm:0.#} mm sheet."));
    }
}
