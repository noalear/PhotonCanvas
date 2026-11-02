using System.Collections.Immutable;
using Clipper2Lib;
using PhotonCanvas.Core;

namespace PhotonCanvas.Addon.Mechanism;

/// <summary>One cam in an automaton box, with its follower and the platform it lifts.</summary>
public sealed record Station
{
    public ImmutableList<CamStep> Steps { get; init; } = CamPresets.Default;
    /// <summary>The cam's radius where the follower is lowest (mm).</summary>
    public double BaseRadiusMm { get; init; } = 22;
    public FollowerFoot Foot { get; init; } = FollowerFoot.Round;
    /// <summary>A round foot's diameter, or a flat foot's width (mm).</summary>
    public double FootMm { get; init; } = 12;
    /// <summary>How far into its steps this cam is when the handle starts (degrees): puts the figures out of step with each other.</summary>
    public double StartDeg { get; init; }
}

/// <summary>
/// An automaton: a box with a shaft across it turned by a handle, cams on the shaft, and followers that push
/// platforms up through the top for figures to stand on. The box goes together with tabs in slots sized for
/// the sheet; the followers slide in slots in the top and in a shelf under it, which keep them upright.
/// </summary>
public sealed record AutomatonSpec : MechSpec
{
    public ImmutableList<Station> Stations { get; init; } = [new(), new() { Steps = CamPresets.All[2].Steps, StartDeg = 90 }];
    /// <summary>The shaft. Square by default: it grips the cams and sets their timing.</summary>
    public Axle Axle { get; init; } = new() { Square = true };
    /// <summary>How wide each round platform on top is (mm).</summary>
    public double PlatformMm { get; init; } = 30;
    /// <summary>How far the handle is from the shaft (mm).</summary>
    public double CrankMm { get; init; } = 28;
    /// <summary>The handle turns clockwise (looking at the handle side) instead of anticlockwise.</summary>
    public bool Clockwise { get; init; }

    public const int MaxStations = 6;
    /// <summary>Room for the followers to slide in their slots (mm).</summary>
    public const double SlideGap = 0.4;

    public override Mechanism Build(CancellationToken ct = default)
    {
        if (Stations.Count < 1) throw new InvalidOperationException("Add a cam.");
        if (Stations.Count > MaxStations) throw new InvalidOperationException($"Use at most {MaxStations} cams.");
        int dir = Clockwise ? -1 : 1;
        double t = ThicknessMm, fit = SlotFitMm, fitLen = 0.2, P = PlatformMm;
        int n = Stations.Count;
        var designs = new CamDesign[n];
        for (int i = 0; i < n; i++)
        {
            ct.ThrowIfCancellationRequested();
            var s = Stations[i];
            try { designs[i] = new CamDesign(s.Steps, s.BaseRadiusMm, s.Foot, s.FootMm, dir, ct); }
            catch (InvalidOperationException e) { throw new InvalidOperationException($"Cam {i + 1}: {char.ToLowerInvariant(e.Message[0])}{e.Message[1..]}", e); }
        }
        var starts = Stations.Select(s => s.StartDeg * Math.PI / 180).ToArray();
        var mech = new Mechanism { Title = "Automaton" };

        // ---- sizes: everything follows from the cams ----
        double heldR = Axle.Square ? Axle.SizeMm * Math.Sqrt(2) / 2 : Axle.SizeMm / 2;
        double rMax = designs.Max(d => d.MaxRadius);
        double sp = Math.Max(P + 6, 24);                       // from one cam to the next along the shaft
        double W = n * sp;                                     // inside, between the sides
        var barW = designs.Select(d => d.Foot == FollowerFoot.Round ? Math.Clamp(1.6 * d.FootR, 6, 10) : 10).ToArray();
        double D = Math.Ceiling(Math.Max(Math.Max(2 * rMax + 14, P + 10), Math.Max(50, designs.Max(d => 2 * d.FootR + 10))));
        double zs = Math.Ceiling(rMax + 5);                    // the shaft, above the base
        double hiFoot = Enumerable.Range(0, n).Max(i => zs + designs[i].Height.Max() + designs[i].FootTop);
        double zShelf = Math.Ceiling(hiFoot + 5);              // the shelf's underside
        double guide = Math.Max(16, 5 * t);                    // from the shelf up to the top: keeps the followers upright
        double H = zShelf + t + guide;                         // inside height
        double ds = Math.Max(barW.Max() + 12, 20);             // the shelf's depth
        double stw = Math.Round(ds * 0.6, 1);                  // its tabs
        double tw = Math.Round(D / 4, 1);                      // the sides' tabs
        double ob = 8, ot = 3;                                 // how far the base and top reach past the sides
        double bx = W / 2 + t + ob, by = D / 2 + ob, tx = W / 2 + t + ot;
        double X(int i) => -W / 2 + sp * (i + 0.5);
        var shoulder = Enumerable.Range(0, n).Select(i => H + t + 3 - (zs + designs[i].Height.Min())).ToArray();
        var tabW = barW.Select(w => Math.Max(3, w - 4)).ToArray();

        // ---- the box ----
        var sideSlots = new Paths64();
        foreach (var sx in new[] { -1, 1 })
            foreach (var sy in new[] { -1, 1 })
                sideSlots.Add(Shapes.Box(new Vec2(sx * (W / 2 + t / 2), sy * D / 4), t + fit, tw + fitLen));
        var followerSlots = new Paths64(Enumerable.Range(0, n).Select(i => Shapes.Box(new Vec2(X(i), 0), t + SlideGap, barW[i] + SlideGap)));

        var basePart = Poly.Subtract([Shapes.RoundRect(-bx, -by, bx, by, 4)], sideSlots);
        mech.Parts.Add(new Part { Name = "Base", Shape = basePart, Label = new PartLabel("Base", new Vec2(0, -by + ob / 2), 3.5) });

        var topPart = Poly.Subtract([Shapes.RoundRect(-tx, -D / 2, tx, D / 2, 3)], Poly.Union(sideSlots, followerSlots));
        mech.Parts.Add(new Part { Name = "Top", Shape = topPart, Label = D / 2 - P / 2 > 6 ? new PartLabel("Top", new Vec2(0, -D / 2 + 4.5), 3.5) : null });

        var shelfPart = Poly.Subtract(
            Poly.Union([Poly.Rect(-W / 2, -ds / 2, W / 2, ds / 2), Poly.Rect(-W / 2 - t, -stw / 2, -W / 2 + 0.01, stw / 2), Poly.Rect(W / 2 - 0.01, -stw / 2, W / 2 + t, stw / 2)]),
            followerSlots);
        double shelfRoom = sp / 2 - (t + SlideGap) / 2;
        mech.Parts.Add(new Part { Name = "Shelf", Shape = shelfPart, Label = shelfRoom > 14 ? new PartLabel("Shelf", new Vec2(-W / 2 + shelfRoom / 2, 0), 3) : null });

        var sideOutline = new Paths64 { Poly.Rect(-D / 2, 0, D / 2, H) };
        foreach (var sy in new[] { -1, 1 })
        {
            sideOutline.Add(Poly.Rect(sy * D / 4 - tw / 2, -t, sy * D / 4 + tw / 2, 0.01));
            sideOutline.Add(Poly.Rect(sy * D / 4 - tw / 2, H - 0.01, sy * D / 4 + tw / 2, H + t));
        }
        var side = Poly.Subtract(Poly.Union(sideOutline), [Axle.TurningHole(new Vec2(0, zs)), Shapes.Box(new Vec2(0, zShelf + t / 2), stw + fitLen, t + fit)]);
        mech.Parts.Add(new Part { Name = "Side", Shape = side, Copies = 2, Label = zs - heldR > 14 ? new PartLabel("Side", new Vec2(0, 6), 3.5) : null });

        // ---- the cams, followers and platforms ----
        var cams = new Paths64[n];
        var followers = new Paths64[n];
        var solids = new Paths64[n];
        var camLines = new List<List<Vec2>>[n];
        for (int i = 0; i < n; i++)
        {
            var d = designs[i];
            // The shaft's square, seen from the cam: a square shaft sets each cam's timing by itself.
            double turn0 = -d.TurnAt(starts[i]);
            cams[i] = Poly.Subtract(d.Outline, [Axle.HeldHole(default, turn0)]);
            double room = d.MinRadius - heldR;
            var lines = new List<List<Vec2>>();
            if (room > 4) lines.Add([Shapes.Polar(heldR + 1.5, turn0), Shapes.Polar(heldR + Math.Min(7, room - 1.5), turn0)]);
            if (room > 7) lines.AddRange(Shapes.TurnArrow(default, heldR + room * 0.55, turn0 + Math.PI - dir * 0.55, turn0 + Math.PI + dir * 0.55));
            camLines[i] = lines;
            var labelAt = Shapes.Polar(heldR + room * 0.5, turn0 + Math.PI / 2);
            mech.Parts.Add(new Part
            {
                Name = $"Cam {i + 1}",
                Shape = cams[i],
                Marks = lines,
                Label = room > 8 ? new PartLabel($"Cam {i + 1}", labelAt, Math.Clamp(room * 0.22, 2.5, 4)) : null,
            });

            solids[i] = Poly.Union(d.FollowerShape(barW[i], shoulder[i]), [Poly.Rect(-tabW[i] / 2, shoulder[i] - 0.01, tabW[i] / 2, shoulder[i] + t)]);
            followers[i] = solids[i];
            mech.Parts.Add(new Part
            {
                Name = $"Follower {i + 1}",
                Shape = followers[i],
                Label = new PartLabel($"{i + 1}", new Vec2(0, (d.FootTop + 3 + shoulder[i]) / 2), Math.Clamp(barW[i] - 3, 2.5, 4), 90),
            });
        }
        foreach (var g in Enumerable.Range(0, n).GroupBy(i => Math.Round(tabW[i], 3)))
        {
            var plat = Poly.Subtract([Poly.Circle(default, P / 2, 0.003)], [Shapes.Box(default, t + fit, g.Key + fitLen)]);
            string name = n == 1 || g.Count() == n ? "Platform" : $"Platform {string.Join(", ", g.Select(i => i + 1))}";
            mech.Parts.Add(new Part { Name = name, Shape = plat, Copies = g.Count() });
        }

        // ---- the handle ----
        double cw = Math.Max(12, 2 * heldR + 8);
        double pegR = Axle.SizeMm / 2;
        var crank = Poly.Subtract(Shapes.Link(default, new Vec2(CrankMm, 0), cw), [Axle.HeldHole(default), Poly.Circle(new Vec2(CrankMm, 0), pegR - 0.03, 0.003)]);
        mech.Parts.Add(new Part { Name = "Crank", Shape = crank });
        var collar = Poly.Subtract([Poly.Circle(default, heldR + 5, 0.003)], [Axle.HeldHole(default)]);
        mech.Parts.Add(new Part { Name = "Collar", Shape = collar });
        double shaftLen = W + 2 * t + 2 * (t + 1) + 4;
        mech.Shopping.Add($"A shaft: {Axle.Words} dowel, {Math.Ceiling(shaftLen):0} mm long");
        mech.Shopping.Add($"A handle: {Axle.SizeMm:0.#} mm round dowel, about 30 mm long");
        mech.Shopping.Add("Wood glue, and figures to stand on the platforms");

        // ---- from the front: the whole box, the platforms going up and down ----
        double xL = -W / 2 - t - 1 - t, xC = W / 2 + t + 1;
        var front = new List<Body>
        {
            new("Base", [Poly.Rect(-bx, -t, bx, 0)], Tints.Frame, _ => Pose.Zero) { Faint = true },
            new("Side", [Poly.Rect(-W / 2 - t, 0, -W / 2, H)], Tints.Frame, _ => Pose.Zero) { Faint = true },
            new("Side", [Poly.Rect(W / 2, 0, W / 2 + t, H)], Tints.Frame, _ => Pose.Zero) { Faint = true },
            new("Shelf", [Poly.Rect(-W / 2, zShelf, W / 2, zShelf + t)], Tints.Frame, _ => Pose.Zero) { Faint = true },
            new("Top", [Poly.Rect(-tx, H, tx, H + t)], Tints.Frame, _ => Pose.Zero) { Faint = true },
            new("Shaft", [Poly.Rect(xL - 1, zs - Axle.SizeMm / 2, xC + t + 2, zs + Axle.SizeMm / 2)], Tints.Steel, _ => Pose.Zero),
            new("Collar", [Poly.Rect(xL, zs - heldR - 5, xL + t, zs + heldR + 5)], Tints.Of(7), _ => Pose.Zero),
        };
        for (int i = 0; i < n; i++)
        {
            var d = designs[i];
            double x = X(i), phi0 = starts[i], s = shoulder[i];
            front.Add(new Body($"Cam {i + 1}", [Poly.Rect(x - t / 2, zs - d.MaxRadius, x + t / 2, zs + d.MaxRadius)], Tints.Of(i), _ => Pose.Zero)
            {
                ShapeAt = a => [Poly.Rect(x - t / 2, zs + d.BottomAt(a + phi0), x + t / 2, zs + d.TopAt(a + phi0))],
            });
            Pose Up(double a) => new(new Vec2(0, zs + d.HeightAt(a + phi0)), 0);
            front.Add(new Body($"Follower {i + 1}", [Poly.Rect(x - t / 2, -d.FootBottom, x + t / 2, s + t)], Tints.Of(i + 4), Up));
            front.Add(new Body($"Platform {i + 1}", [Poly.Rect(x - P / 2, s, x + P / 2, s + t)], Tints.Of(i + 4), Up));
        }
        front.Add(new Body("Crank", [Poly.Rect(xC, zs - CrankMm - cw / 2, xC + t, zs + CrankMm + cw / 2)], Tints.Of(7), _ => Pose.Zero)
        {
            ShapeAt = a =>
            {
                double zh = zs + CrankMm * Math.Sin(dir * a);
                return [Poly.Rect(xC, Math.Min(zs, zh) - cw / 2, xC + t, Math.Max(zs, zh) + cw / 2)];
            },
        });
        front.Add(new Body("Handle", [Poly.Rect(xC + t, zs - pegR, xC + t + 25, zs + pegR)], Tints.Steel, a => new Pose(new Vec2(0, CrankMm * Math.Sin(dir * a)), 0)));
        double topMost = Enumerable.Range(0, n).Max(i => zs + designs[i].Height.Max() + shoulder[i] + t);
        mech.Panels.Add(new PlanarPanel
        {
            Title = "From the front",
            Bodies = front,
            Extent = new Rect2(-bx, -t, xC + t + 25, Math.Max(topMost, H + t)).Inflate(2),
            Weight = 1.6,
        });

        // ---- from the handle's side: each cam pushing its follower ----
        for (int i = 0; i < n; i++)
        {
            var d = designs[i];
            double phi0 = starts[i], s = shoulder[i], gap = (barW[i] + SlideGap) / 2;
            Pose Up(double a) => new(new Vec2(0, zs + d.HeightAt(a + phi0)), 0);
            var bodies = new List<Body>
            {
                new("Side", side, Tints.Frame, _ => Pose.Zero) { Faint = true },
                new("Base", [Poly.Rect(-by, -t, by, 0)], Tints.Frame, _ => Pose.Zero) { Faint = true },
                new("Shelf", [Poly.Rect(-ds / 2, zShelf, -gap, zShelf + t), Poly.Rect(gap, zShelf, ds / 2, zShelf + t)], Tints.Frame, _ => Pose.Zero) { Faint = true },
                new("Top", [Poly.Rect(-D / 2, H, -gap, H + t), Poly.Rect(gap, H, D / 2, H + t)], Tints.Frame, _ => Pose.Zero) { Faint = true },
                new($"Cam {i + 1}", cams[i], Tints.Of(i), a => new Pose(new Vec2(0, zs), d.TurnAt(a + phi0))) { IsInput = true, Lines = camLines[i] },
                new($"Follower {i + 1}", followers[i], Tints.Of(i + 4), Up),
                new($"Platform {i + 1}", [Poly.Rect(-P / 2, s, P / 2, s + t)], Tints.Of(i + 4), Up),
                new("Shaft", [Axle.Square ? Axle.HeldHole(default) : Poly.Circle(default, Axle.SizeMm / 2, 0.01)], Tints.Steel, a => new Pose(new Vec2(0, zs), dir * a)),
            };
            mech.Panels.Add(new PlanarPanel
            {
                Title = n == 1 ? "The cam, from the handle's side" : $"Cam {i + 1}, handle side",
                Bodies = bodies,
                Extent = new Rect2(-by, -t, by, Math.Max(zs + d.Height.Max() + s + t, H + t)).Inflate(2),
                InputPivot = new Vec2(0, zs),
                InputSense = dir,
            });
        }
        mech.Panels.Add(CamSpec.LiftChart(
            Enumerable.Range(0, n).Select(i => ($"Platform {i + 1}", Tints.Of(i + 4), (Func<double, double>)(a => designs[i].LiftAt(a + starts[i])), false)),
            n == 1 ? "How high the platform is" : "How high each platform is"));

        // ---- the motion check ----
        double rpm = double.PositiveInfinity;
        for (int i = 0; i < n; i++)
        {
            ct.ThrowIfCancellationRequested();
            rpm = Math.Min(rpm, designs[i].Check(mech, n == 1 ? "The cam" : $"Cam {i + 1}", starts[i], heldR, cams[i], solids[i]));
        }
        mech.Findings.Add(CamDesign.SpeedFinding(rpm, n > 1));
        bool crankLow = zs - CrankMm - cw / 2 < 1;
        if (CrankMm < heldR + pegR + 4)
            mech.Findings.Add(new(Severity.Bad, $"The handle is too close to the shaft: make the crank at least {Math.Ceiling(heldR + pegR + 4):0} mm."));
        if (crankLow)
        {
            mech.Findings.Add(new(Severity.Bad, $"The crank hits the base at the bottom of its turn: make it {Math.Floor(zs - cw / 2 - 1):0} mm or shorter."));
            mech.MarkWhere(a => zs + CrankMm * Math.Sin(dir * a) - cw / 2 < 1, Severity.Bad);
        }
        else mech.Findings.Add(new(Severity.Good, "The handle clears the base all the way round."));
        if (n > 1) mech.Findings.Add(new(Severity.Good, $"The platforms are {sp - P:0} mm apart, so they never touch."));
        mech.Clashing = a =>
        {
            var names = new List<string>();
            for (int i = 0; i < n; i++)
                if (designs[i].Trouble[CamDesign.Index(a + starts[i])]) names.AddRange([$"Cam {i + 1}", $"Follower {i + 1}"]);
            if (crankLow && zs + CrankMm * Math.Sin(dir * a) - cw / 2 < 1) names.AddRange(["Crank", "Base"]);
            return names;
        };

        double outerW = 2 * bx, outerD = 2 * by, outerH = H + 2 * t;
        string lifts = Say.And(designs.Select(d => $"{d.Program.Lift:0.#}"));
        mech.Summary = $"A box {outerW:0} × {outerD:0} mm and {outerH:0} mm tall with {n} cam{(n > 1 ? "s" : "")} on one shaft: turning the handle lifts the platform{(n > 1 ? "s" : "")} {lifts} mm.";
        return mech;
    }
}
