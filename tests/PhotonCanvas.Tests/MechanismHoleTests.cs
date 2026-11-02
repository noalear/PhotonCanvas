using Clipper2Lib;
using PhotonCanvas.Addon.Mechanism;
using PhotonCanvas.Core;

namespace PhotonCanvas.Tests;

/// <summary>The mechanism maker's holes: round ones with a fit, D and square ones, and handle holes in any gear.</summary>
public class MechanismHoleTests
{
    /// <summary>The holes in a part (paths going the other way round), as their bounds.</summary>
    static List<Rect2> Holes(Paths64 shape) => [.. shape.Where(p => !Clipper.IsPositive(p)).Select(p => Poly.Bounds([p]))];

    /// <summary>The hole whose middle is nearest <paramref name="at"/>.</summary>
    static Rect2 HoleAt(Paths64 shape, Vec2 at) =>
        Holes(shape).OrderBy(h => new Vec2((h.MinX + h.MaxX) / 2, (h.MinY + h.MaxY) / 2).Dist(at)).First();

    static Paths64 PartShape(Mechanism m, string startsWith) => m.Parts.First(p => p.Name.StartsWith(startsWith, StringComparison.Ordinal)).Shape;

    [Theory]
    [InlineData(HoleFit.Press, "", 5.8)]
    [InlineData(HoleFit.Snug, "", 5.94)]
    [InlineData(HoleFit.Slip, "", 6.1)]
    [InlineData(HoleFit.Free, "", 6.3)]
    [InlineData(HoleFit.Press, "Acrylic", 5.92)]
    [InlineData(HoleFit.Free, "Acrylic", 6.25)]
    public void ARoundHoleIsDrawnForItsFitAndMaterial(HoleFit fit, string material, double across)
    {
        var hole = new Hole { SizeMm = 6, Fit = fit };
        var b = Poly.Bounds([hole.Cut(default, material)]);
        Assert.Equal(across, b.Width, 0.01);
        Assert.Equal(across, b.Height, 0.01);
        Assert.Equal(fit is HoleFit.Slip or HoleFit.Free, hole.Turns);
        // A nudge for a dowel that isn't quite its size.
        Assert.Equal(across + 0.1, Poly.Bounds([(hole with { AdjustMm = 0.1 }).Cut(default, material)]).Width, 0.01);
    }

    [Fact]
    public void ADHoleHasItsFlatAndASquareHoleItsSide()
    {
        // A 5 mm motor shaft, 4.5 mm across its flat, snug in wood (0.06 mm tighter all round).
        var d = new Hole { Shape = HoleShape.D, SizeMm = 5, FlatMm = 4.5, Fit = HoleFit.Snug };
        var b = Poly.Bounds([d.Cut(default, "")]);
        Assert.Equal(4.94, b.Height, 0.01);
        Assert.Equal(4.44, b.Width, 0.01);
        Assert.Equal(-2.47, b.MinX, 0.01);   // round on −x, the flat on +x
        Assert.False(d.Turns);
        // Turned (to set a cam's timing): the flat turns with it.
        var up = Poly.Bounds([d.Cut(default, "", Math.PI / 2)]);
        Assert.Equal(4.44, up.Height, 0.01);
        // No flat given: 0.5 mm off the size.
        Assert.Equal(4.5, new Hole { Shape = HoleShape.D, SizeMm = 5 }.Shaft.Flat, 6);

        var sq = new Hole { Shape = HoleShape.Square, SizeMm = 6, Fit = HoleFit.Press };
        var sb = Poly.Bounds([sq.Cut(default, "")]);
        Assert.Equal(5.8, sb.Width, 0.01);
        Assert.Equal(5.8, sb.Height, 0.01);
        Assert.False((sq with { Fit = HoleFit.Free }).Turns);
    }

    [Fact]
    public void AGearThatGripsItsAxleMakesTheAxleTurnInThePlate()
    {
        var spec = new GearTrainSpec
        {
            Gears = [new() { Teeth = 36, Hole = new Hole { Shape = HoleShape.D, SizeMm = 6, FlatMm = 5, Fit = HoleFit.Snug } }, new() { Teeth = 12 }],
        };
        var m = spec.Build();
        Assert.DoesNotContain(m.Findings, f => f.Level >= Severity.Warn);
        // Gear A's hole is a D; B's is the usual free round one.
        var a = HoleAt(PartShape(m, "Gear A"), default);
        Assert.Equal(4.94, a.Width, 0.01);
        Assert.Equal(5.94, a.Height, 0.01);
        var bHole = HoleAt(PartShape(m, "Gear B"), default);
        Assert.Equal(6.3, bHole.Width, 0.01);
        // The plate: A's D axle turns in a round hole; B's round axle is glued in snug.
        var placed = spec.Place();
        var plate = PartShape(m, "Plate");
        Assert.Equal(6.3, HoleAt(plate, placed[0].Centre).Width, 0.01);
        Assert.Equal(5.94, HoleAt(plate, placed[1].Centre).Width, 0.01);
        Assert.Contains(m.Shopping, x => x.Contains("An axle for A: 6 mm D-shaped") && x.Contains("it turns in the plate"));
        Assert.Contains(m.Shopping, x => x.Contains("An axle for B: 6 mm dowel") && x.Contains("glued into the plate"));
        Assert.Contains(m.Findings, f => f.Level == Severity.Note && f.Text.Contains("A's axle hole 6 mm D-shaped"));
        // Spacers for each kind of axle.
        Assert.Equal(2, m.Parts.Count(p => p.Name.StartsWith("Spacer", StringComparison.Ordinal)));
    }

    [Fact]
    public void GearsOnOneAxleMustHaveTheSameHole()
    {
        var spec = new GearTrainSpec
        {
            Gears = [new() { Teeth = 36 }, new() { Teeth = 30, Link = GearLink.SameShaft, Hole = new Hole { Shape = HoleShape.Square, SizeMm = 6, Fit = HoleFit.Snug } }],
        };
        var m = spec.Build();
        Assert.Contains(m.Findings, f => f.Level == Severity.Bad && f.Text.Contains("share an axle but have different holes"));

        // Both gripping the same square axle: no glue needed between them.
        var sq = new Hole { Shape = HoleShape.Square, SizeMm = 6, Fit = HoleFit.Snug };
        var ok = (spec with { Handle = false, Gears = [new() { Teeth = 36, Hole = sq }, new() { Teeth = 30, Link = GearLink.SameShaft, Hole = sq }, new() { Teeth = 20 }] }).Build();
        Assert.DoesNotContain(ok.Findings, f => f.Level >= Severity.Warn);
        Assert.DoesNotContain(ok.Shopping, x => x.Contains("glued face to face"));
    }

    [Fact]
    public void AnyGearCanHaveAHandleAtTheRadiusAsked()
    {
        var peg = new Hole { Shape = HoleShape.Square, SizeMm = 5, Fit = HoleFit.Press };
        var spec = new GearTrainSpec
        {
            Handle = false,
            Gears = [new() { Teeth = 36 }, new() { Teeth = 40, AngleDeg = 0, Handle = new Crank { RadiusMm = 25, Hole = peg } }],
        };
        var m = spec.Build();
        Assert.DoesNotContain(m.Findings, f => f.Level >= Severity.Warn);
        // No handle in A any more; one in B, 25 mm out, square and pressed.
        Assert.DoesNotContain(m.Shopping, x => x.Contains("handle peg for gear A"));
        Assert.Contains(m.Shopping, x => x.Contains("handle peg for gear B: 5 mm square dowel"));
        var b = PartShape(m, "Gear B");
        var h = HoleAt(b, new Vec2(25, 0));
        Assert.Equal(25, (h.MinX + h.MaxX) / 2, 0.01);
        Assert.Equal(4.8, h.Width, 0.01);
        Assert.Equal(4.8, h.Height, 0.01);
        // Wood all round it, though B has spoke windows.
        for (int k = 0; k < 12; k++)
            Assert.True(Poly.Inside(b, new Vec2(25, 0) + Shapes.Polar(4.8 * Math.Sqrt(2) / 2 + 1.5, k * Math.PI / 6)), $"{k}");
        // It turns with B in the motion view.
        Assert.Single(m.Panels.OfType<PlanarPanel>().First().Bodies, x => x.Name == "Handle");

        // Too far out: said so, and not cut into the teeth.
        var far = (spec with { Gears = spec.Gears.SetItem(1, spec.Gears[1] with { Handle = new Crank { RadiusMm = 60 } }) }).Build();
        Assert.Contains(far.Findings, f => f.Level == Severity.Warn && f.Text.Contains("cuts into the rim"));
        Assert.DoesNotContain(far.Shopping, x => x.Contains("handle peg"));

        // Turned off in A's own row, though the old setting says A has one.
        var off = new GearTrainSpec { Gears = [new() { Teeth = 36, Handle = new Crank { On = false } }, new() { Teeth = 12 }] };
        Assert.Null(off.HandleOf(0));
        Assert.DoesNotContain(off.Build().Shopping, x => x.Contains("handle peg"));
    }

    [Fact]
    public void AHandlePegInFrontOfAnotherGearIsFlagged()
    {
        // B sits on A's axle in front of it: a peg in A, inside B's teeth, would hit B.
        var spec = new GearTrainSpec { Gears = [new() { Teeth = 40, Handle = new Crank { RadiusMm = 20 } }, new() { Teeth = 30, Link = GearLink.SameShaft }] };
        Assert.Contains(spec.Build().Findings, f => f.Level == Severity.Warn && f.Text.Contains("The handle peg in A would hit B"));
    }

    [Fact]
    public void ThePinionAndCamTakeAHoleAndAHandleOfTheirOwn()
    {
        var rack = new RackSpec { PinionHole = new Hole { Shape = HoleShape.D, SizeMm = 5, Fit = HoleFit.Press }, Crank = new Crank { RadiusMm = 12, Hole = new Hole { SizeMm = 4, Fit = HoleFit.Free } } };
        var r = rack.Build();
        Assert.DoesNotContain(r.Findings, f => f.Level >= Severity.Warn);
        var pinion = PartShape(r, "Pinion");
        Assert.Equal(4.3, HoleAt(pinion, new Vec2(12, 0)).Width, 0.01);   // a peg that spins in your fingers
        Assert.Contains(r.Shopping, x => x.Contains("An axle for the pinion: 5 mm D-shaped") && x.Contains("it turns in the plate"));

        var cam = new CamSpec { CamHole = new Hole { Shape = HoleShape.Square, SizeMm = 6, Fit = HoleFit.Snug }, Crank = new Crank { RadiusMm = 14 } };
        var c = cam.Build();
        Assert.DoesNotContain(c.Findings, f => f.Level >= Severity.Warn);
        var camShape = PartShape(c, "Cam");
        Assert.Equal(5.94, HoleAt(camShape, default).Width, 0.01);
        Assert.Equal(14, (HoleAt(camShape, new Vec2(14, 0)).MinX + HoleAt(camShape, new Vec2(14, 0)).MaxX) / 2, 0.01);
        // The square shaft turns in a round hole in the backboard.
        Assert.Equal(6 * Math.Sqrt(2) + 0.3, HoleAt(PartShape(c, "Backboard"), default).Width, 0.01);

        // A cam that turns on its shaft can't be driven by it.
        var loose = (cam with { CamHole = new Hole { SizeMm = 6, Fit = HoleFit.Free } }).Build();
        Assert.Contains(loose.Findings, f => f.Level == Severity.Bad && f.Text.Contains("won't turn it"));
    }

    [Fact]
    public void TheAutomatonsCamsCanTakeADShaft()
    {
        var spec = new AutomatonSpec { CamHole = new Hole { Shape = HoleShape.D, SizeMm = 8, FlatMm = 7, Fit = HoleFit.Snug } };
        var m = spec.Build();
        Assert.DoesNotContain(m.Findings, f => f.Level >= Severity.Warn);
        Assert.Contains(m.Shopping, x => x.Contains("A shaft: 8 mm D-shaped"));
        // The sides' holes are round, for the D to turn in.
        var side = PartShape(m, "Side");
        Assert.Contains(Holes(side), h => Math.Abs(h.Width - 8.3) < 0.02 && Math.Abs(h.Height - 8.3) < 0.02);
        // Round cams need lining up by hand; free ones don't work at all.
        var round = (spec with { CamHole = new Hole { SizeMm = 8, Fit = HoleFit.Press } }).Build();
        Assert.Contains(round.Findings, f => f.Level == Severity.Note && f.Text.Contains("doesn't set the cams' timing"));
        Assert.Contains((spec with { CamHole = new Hole { SizeMm = 8 } }).Build().Findings, f => f.Level == Severity.Bad);
    }

    [Fact]
    public void HolesAndHandlesSaveAndLoad()
    {
        MechSpec[] specs =
        [
            new GearTrainSpec { Gears = [new() { Teeth = 30, Hole = new Hole { Shape = HoleShape.D, SizeMm = 5, FlatMm = 4.2, Fit = HoleFit.Press, AdjustMm = 0.04 }, Handle = new Crank { RadiusMm = 20, Hole = new Hole { Fit = HoleFit.Free, SizeMm = 4 } } }, new() { Teeth = 20 }] },
            new RackSpec { PinionHole = new Hole { Shape = HoleShape.Square }, Crank = new Crank { RadiusMm = 9 } },
            new PlanetarySpec { SunHole = new Hole { Fit = HoleFit.Snug } },
            new CamSpec { CamHole = new Hole { Shape = HoleShape.Square, Fit = HoleFit.Slip } },
            new AutomatonSpec { CamHole = new Hole { Shape = HoleShape.D, SizeMm = 8 }, Material = "Acrylic" },
        ];
        foreach (var spec in specs)
        {
            var back = MechSpec.FromJson(spec.ToJson());
            Assert.NotNull(back);
            Assert.Equal(spec.ToJson(), back.ToJson());
        }
        // Saved before holes could be picked: builds as it always did.
        var old = MechSpec.FromJson("{\"kind\":\"gears\",\"Gears\":[{\"Teeth\":36},{\"Teeth\":12}],\"Handle\":true}") as GearTrainSpec;
        Assert.NotNull(old);
        Assert.Contains(old.Build().Shopping, x => x.Contains("handle peg for gear A: 6 mm dowel"));
    }
}
