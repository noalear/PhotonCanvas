using Clipper2Lib;
using PhotonCanvas.Addon.Mechanism;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;

namespace PhotonCanvas.Tests;

/// <summary>The mechanism maker's engine: gear teeth, meshing, cams and the automaton box.</summary>
public class MechanismTests
{
    static readonly ToothForm Form = new() { Module = 3, BacklashMm = 0.15 };

    /// <summary>The angle (radians) a tooth centred on +x covers at radius <paramref name="r"/>, found by walking round the outline.</summary>
    static double ToothAngleAt(Paths64 gear, double r)
    {
        double lo = 0, step = 1e-4;
        while (Poly.Inside(gear, new Vec2(r * Math.Cos(lo + step), r * Math.Sin(lo + step))) && lo < Math.PI) lo += step;
        double hi = 0;
        while (Poly.Inside(gear, new Vec2(r * Math.Cos(hi - step), r * Math.Sin(hi - step))) && hi > -Math.PI) hi -= step;
        return lo - hi;
    }

    [Theory]
    [InlineData(24, 0.0)]
    [InlineData(40, 0.0)]
    [InlineData(12, 0.3)]
    public void ToothThicknessOnThePitchCircleIsTheStandardOne(int z, double shift)
    {
        var gear = GearMaker.ExternalTeeth(Form, z, shift);
        double rp = Form.Module * z / 2;
        double expected = Form.Module * (Math.PI / 2 + 2 * shift * Math.Tan(Form.Alpha)) - Form.BacklashMm / 2;
        double arc = ToothAngleAt(gear, rp) * rp;
        Assert.InRange(arc, expected - 0.01, expected + 0.01);
        // The tip and root circles where they should be.
        var size = GearMaker.External(Form, z, shift);
        var box = Poly.Bounds(gear);
        Assert.InRange(box.MaxX, size.TipR - 0.01, size.TipR + 0.001);
        Assert.True(Poly.Inside(gear, new Vec2((size.RootR - 0.02) * Math.Cos(Math.PI / z), (size.RootR - 0.02) * Math.Sin(Math.PI / z))));
        Assert.False(Poly.Inside(gear, new Vec2((size.RootR + 0.05) * Math.Cos(Math.PI / z), (size.RootR + 0.05) * Math.Sin(Math.PI / z))));
    }

    [Theory]
    [InlineData(20, 30)]
    [InlineData(12, 36)]
    [InlineData(17, 17)]
    public void TwoGearsTurnTogetherWithoutBinding(int z1, int z2)
    {
        double x1 = GearMaker.ShiftAgainstUndercut(Form, z1), x2 = GearMaker.ShiftAgainstUndercut(Form, z2);
        var (_, centre) = GearMaker.Mesh(Form, z1, x1, z2, x2);
        double k = GearMaker.TipShortening(Form, z1, x1, z2, x2);
        var a = GearMaker.ExternalTeeth(Form, z1, x1, k);
        var b = GearMaker.ExternalTeeth(Form, z2, x2, k);
        double b0 = GearMaker.MeshPhase(z1, 0, z2, 0);
        var near = new Rect2(GearMaker.External(Form, z1, x1).PitchR - 3 * Form.Module, -3 * Form.Module, GearMaker.External(Form, z1, x1).PitchR + 3 * Form.Module, 3 * Form.Module);
        for (int i = 0; i < 24; i++)
        {
            double t = 2 * Math.PI / z1 * i / 24;
            var pa = Poly.Move(a, t);
            var pb = Poly.Move(b, b0 - t * z1 / z2, centre, 0);
            Assert.True(Poly.Overlap(pa, pb, near) < 1e-4, $"{z1}/{z2} teeth overlap at step {i}");
        }
        // And the play is what was asked for: half of it either side of the tooth.
        double half = Form.BacklashMm / 2 / (Form.Module * z2 / 2);
        foreach (int side in new[] { 1, -1 })
        {
            Assert.True(Poly.Overlap(a, Poly.Move(b, b0 + side * 0.7 * half, centre, 0), near) < 1e-4, $"{z1}/{z2}: less play than asked for");
            Assert.True(Poly.Overlap(a, Poly.Move(b, b0 + side * 1.3 * half, centre, 0), near) > 1e-4, $"{z1}/{z2}: more play than asked for");
        }
    }

    // ---- the other gear mechanisms ----

    [Fact]
    public void RackAndPinionSlideWithoutJammingAndWithThePlayAskedFor()
    {
        var m = new RackSpec { Form = Form, PinionTeeth = 14, TravelMm = 60 }.Build();
        Assert.DoesNotContain(m.Findings, f => f.Level == Severity.Bad);
        var play = m.Findings.Single(f => f.Text.StartsWith("The rack slides freely"));
        double mm = double.Parse(play.Text.Split("about ")[1].Split(' ')[0], System.Globalization.CultureInfo.InvariantCulture);
        Assert.InRange(mm, 0.1, 0.3);
        // Enough teeth on the rack to stay in mesh all the way along, and a few more.
        int rackTeeth = (int)Math.Ceiling(60 / Form.Pitch) + 4;
        Assert.Equal(["Pinion (14 teeth)", $"Rack ({rackTeeth} teeth)", "Plate"], m.Parts.Select(p => p.Name));
        // The pinion turns forwards then back, so the rack goes there and back over one cycle.
        var rack = m.Panels.OfType<PlanarPanel>().Single().Bodies.Single(b => b.Name == "Rack");
        Assert.Equal(60, rack.Pose(m.Cycle / 2).At.X - rack.Pose(0).At.X, 6);
        Assert.Equal(rack.Pose(0).At.X, rack.Pose(m.Cycle).At.X, 6);
    }

    [Theory]
    [InlineData(PlanetaryHeld.Ring, 12, 12, 3)]
    [InlineData(PlanetaryHeld.Carrier, 15, 12, 3)]
    [InlineData(PlanetaryHeld.Sun, 12, 18, 4)]
    public void PlanetaryGearsTurnTogetherAtTheRightSpeeds(PlanetaryHeld held, int sun, int planet, int planets)
    {
        var spec = new PlanetarySpec { Form = new ToothForm { Module = 2 }, SunTeeth = sun, PlanetTeeth = planet, Planets = planets, Held = held };
        var (ws, wc, wr) = spec.Speeds();
        // Willis: (sun − carrier) / (ring − carrier) = −ring teeth / sun teeth.
        Assert.Equal(-(double)spec.RingTeeth / sun, (ws - wc) / (wr - wc), 9);
        var m = spec.Build();
        Assert.DoesNotContain(m.Findings, f => f.Level == Severity.Bad);
        Assert.Contains(m.Findings, f => f.Text.StartsWith("Sun, planets and ring turn together"));
        Assert.Equal(planets, m.Parts.Single(p => p.Name.StartsWith("Planet")).Copies);
    }

    // ---- cams ----

    [Fact]
    public void CamStepsAreLaidRoundOneTurnAndEndWhereTheyStart()
    {
        var p = new CamProgram([new(CamMove.Rise, 100, 10), new(CamMove.Wait, 50, 0), new(CamMove.Fall, 100, 10), new(CamMove.Wait, 50, 0)]);
        Assert.Equal(300, p.TotalDegrees);
        Assert.Equal(10, p.Lift, 9);
        Assert.Equal(0, p.EndJump, 9);
        double deg = Math.PI / 180;
        Assert.Equal(0, p.At(0).S, 9);
        Assert.Equal(5, p.At(60 * deg).S, 6);    // halfway up (the 300° are stretched to 360)
        Assert.Equal(10, p.At(150 * deg).S, 6);  // waiting at the top
        Assert.Equal(0, p.At(330 * deg).S, 6);
        // Smooth moves start and stop without a jolt: no speed and no push at either end.
        foreach (double t in new[] { 0.0, 1.0 })
        {
            var (_, v, a) = CamProgram.Law(CamLaw.Smooth, t);
            Assert.Equal(0, v, 9);
            Assert.Equal(0, a, 9);
        }
        foreach (var law in Enum.GetValues<CamLaw>())
        {
            Assert.Equal(0, CamProgram.Law(law, 0).Y, 9);
            Assert.Equal(1, CamProgram.Law(law, 1).Y, 9);
        }
        // Steps that end higher than they start drop back at the end; lower can't be done.
        Assert.Equal(4, new CamProgram([new(CamMove.Rise, 180, 10), new(CamMove.Fall, 180, 6)]).EndJump, 9);
        Assert.Equal(-4, new CamProgram([new(CamMove.Rise, 180, 6), new(CamMove.Fall, 180, 10)]).EndJump, 9);
        Assert.Throws<InvalidOperationException>(() => new CamProgram([new(CamMove.Drop, 0, 5)]));
    }

    [Theory]
    [InlineData(FollowerFoot.Round, 12, 1)]
    [InlineData(FollowerFoot.Round, 8, -1)]
    [InlineData(FollowerFoot.Flat, 30, 1)]
    [InlineData(FollowerFoot.Flat, 30, -1)]
    public void TheCutCamGivesTheFollowerThePlannedMotion(FollowerFoot foot, double footMm, int dir)
    {
        foreach (var preset in CamPresets.All.Where(p => !p.Steps.Any(s => s.Move == CamMove.Drop)))
        {
            // A flat foot only follows a cam that bulges outwards everywhere (base radius + lift + its rate of
            // change > 0); a quick move on a small cam doesn't, and the check says so (see below).
            var program = new CamProgram(preset.Steps);
            if (foot == FollowerFoot.Flat && Enumerable.Range(0, 720).Any(k => { var (s, _, a) = program.At(k * Math.PI / 360); return 25 + s + a < 1; }))
            {
                var bad = new CamSpec { Steps = preset.Steps, BaseRadiusMm = 25, Foot = foot, FootMm = 40 }.Build();
                Assert.Contains(bad.Findings, f => f.Level >= Severity.Warn && f.Text.Contains("can't make the motion"));
                continue;
            }
            var d = new CamDesign(preset.Steps, 25, foot, footMm, dir);
            for (int k = 0; k < CamDesign.Samples; k++)
            {
                double phi = 2 * Math.PI * k / CamDesign.Samples;
                Assert.InRange(d.LiftAt(phi) - d.Program.At(phi).S, -CamDesign.Tolerance, CamDesign.Tolerance);
            }
            // The cam's smallest radius is the base radius; its biggest, the base radius plus the lift.
            Assert.InRange(d.MinRadius, 25 - 0.05, 25 + 0.05);
            Assert.InRange(d.MaxRadius, 25 + d.Program.Lift - 0.05, 25 + d.Program.Lift + 0.05);
        }
    }

    [Fact]
    public void TheFollowerReallyRestsOnTheCutShape()
    {
        // Turned to any angle, the cam and its follower touch but don't overlap.
        var spec = new CamSpec { Steps = CamPresets.All[4].Steps };
        var m = spec.Build();
        var bodies = m.Panels.OfType<PlanarPanel>().Single().Bodies;
        var cam = bodies.Single(b => b.Name == "Cam");
        var follower = bodies.Single(b => b.Name == "Follower");
        for (int i = 0; i < 36; i++)
        {
            double a = 2 * Math.PI * i / 36;
            var c = cam.PlacedAt(a);
            var f = follower.PlacedAt(a);
            Assert.True(Poly.Overlap(c, f) < 0.01, $"overlap at {i * 10}°");
            var lowered = Poly.Move(follower.Shape, new Pose(follower.Pose(a).At - new Vec2(0, 0.1), 0));
            Assert.True(Poly.Overlap(c, lowered) > 0, $"not touching at {i * 10}°");
        }
    }

    [Fact]
    public void ASnailCamDropsTheFollowerQuicklyOnlyWithARoundFoot()
    {
        var steps = CamPresets.All.Single(p => p.Name == "Slow rise, sudden drop").Steps;
        var round = new CamSpec { Steps = steps }.Build();
        Assert.Contains(round.Findings, f => f.Level == Severity.Note && f.Text.Contains("drops 12 mm"));
        Assert.DoesNotContain(round.Findings, f => f.Level >= Severity.Warn);
        var flat = new CamSpec { Steps = steps, Foot = FollowerFoot.Flat, FootMm = 30 }.Build();
        Assert.Contains(flat.Findings, f => f.Level == Severity.Warn && f.Text.Contains("can't fall off a step"));
    }

    [Fact]
    public void TheCheckFindsCamsThatCantWork()
    {
        Severity Worst(CamSpec s, string words) => s.Build().Findings.Where(f => f.Text.Contains(words)).Max(f => f.Level);
        // Rising 20 mm in 40° pushes the follower sideways much too hard.
        Assert.Equal(Severity.Bad, Worst(new CamSpec { Steps = [new(CamMove.Rise, 40, 20), new(CamMove.Fall, 320, 20)], BaseRadiusMm = 15 }, "off straight up"));
        // A flat foot can't follow three bumps on a small cam, and it isn't wide enough either.
        var flat = new CamSpec { Steps = CamPresets.All[3].Steps, BaseRadiusMm = 12, Foot = FollowerFoot.Flat, FootMm = 20 }.Build();
        Assert.Contains(flat.Findings, f => f.Level == Severity.Bad && f.Text.Contains("can't make the motion"));
        Assert.Contains(flat.Findings, f => f.Level == Severity.Bad && f.Text.Contains("past its edge"));
        Assert.Contains(flat.Strip, s => s == Severity.Bad);
        // Going down further than up means a jump up at the end.
        Assert.Equal(Severity.Bad, Worst(new CamSpec { Steps = [new(CamMove.Rise, 180, 5), new(CamMove.Fall, 180, 10)] }, "jump up"));
        // A follower lifted hard and fast flies off when turned briskly.
        var fast = new CamSpec { Steps = [new(CamMove.Rise, 30, 15), new(CamMove.Fall, 30, 15), new(CamMove.Wait, 300, 0)], BaseRadiusMm = 40 }.Build();
        Assert.Contains(fast.Findings, f => f.Level == Severity.Warn && f.Text.Contains("turns a minute"));
    }

    // ---- the automaton box ----

    [Fact]
    public void TheAutomatonBoxFitsTogether()
    {
        var spec = new AutomatonSpec { ThicknessMm = 3, SlotFitMm = 0.1 };
        var m = spec.Build();
        Assert.DoesNotContain(m.Findings, f => f.Level >= Severity.Warn);
        Assert.Equal(2, m.Parts.Single(p => p.Name == "Side").Copies);
        foreach (var name in new[] { "Base", "Top", "Shelf", "Cam 1", "Cam 2", "Follower 1", "Follower 2", "Crank", "Collar" })
            Assert.Contains(m.Parts, p => p.Name == name);
        Assert.Equal(2, m.Parts.Single(p => p.Name == "Platform").Copies);

        // Every tab on the sides has a slot in the base 0.1 mm wider than the sheet.
        var side = m.Parts.Single(p => p.Name == "Side").Shape;
        var sideBox = Poly.Bounds(side);
        Assert.Equal(3, -sideBox.MinY, 3);  // the bottom tabs reach down one thickness
        var holes = m.Parts.Single(p => p.Name == "Base").Shape.Where(p => !Clipper.IsPositive(p)).Select(p => Poly.Bounds([p])).ToList();
        Assert.Equal(4, holes.Count);
        Assert.All(holes, h => Assert.Equal(3.1, h.Width, 2));

        // The followers stand on their cams, and their platforms clear the top at their lowest.
        var front = m.Panels.OfType<PlanarPanel>().First();
        double topZ = front.Bodies.Single(b => b.Name == "Top").Shape.SelectMany(p => p).Max(q => Poly.V(q).Y);
        for (int i = 0; i < 2; i++)
        {
            var platform = front.Bodies.Single(b => b.Name == $"Platform {i + 1}");
            double lowest = Enumerable.Range(0, 72).Min(k => Poly.Bounds(platform.PlacedAt(2 * Math.PI * k / 72)).MinY);
            Assert.InRange(lowest - topZ, 2.5, 3.5);
        }
    }

    [Fact]
    public void EachCamsStartIsBuiltIntoItsShaftHole()
    {
        // A square shaft: a cam started a quarter turn on has the same square hole, so its line says which way round it goes.
        var spec = new AutomatonSpec { Stations = [new(), new() { StartDeg = 45 }] };
        var m = spec.Build();
        var c1 = m.Parts.Single(p => p.Name == "Cam 1");
        var c2 = m.Parts.Single(p => p.Name == "Cam 2");
        double HoleTurn(Part p)
        {
            var hole = p.Shape.Where(q => !Clipper.IsPositive(q)).OrderBy(q => Math.Abs(Clipper.Area(q))).First();
            var corner = hole.Select(Poly.V).OrderByDescending(v => v.Length).First();
            double a = Math.Atan2(corner.Y, corner.X) * 180 / Math.PI;
            return ((a % 90) + 90) % 90;
        }
        Assert.Equal((HoleTurn(c1) - 45 + 90) % 90, HoleTurn(c2), 0);
        // And the cam's line points where the crank points at the start.
        Assert.Equal(-45, Math.Atan2(c2.Marks[0][1].Y, c2.Marks[0][1].X) * 180 / Math.PI, 1);
    }

    [Fact]
    public void GearAsHandleSitsInAPadOnASpoke()
    {
        var spec = new GearTrainSpec();
        var m = spec.Build();
        Assert.Contains(m.Shopping, x => x.Contains("handle peg for gear A"));
        var a = m.Parts.First(p => p.Name.StartsWith("Gear A")).Shape;
        double rimR = GearMaker.External(spec.Form, spec.Gears[0].Teeth, 0).RootR - Math.Max(3, 1.25 * spec.Form.Module);
        double pegR = spec.Axle.SizeMm / 2 - 0.03;
        var at = new Vec2(rimR - pegR - 1, 0);
        Assert.False(Poly.Inside(a, at));
        // Wood all round the hole, though the gear has spoke windows.
        for (int k = 0; k < 12; k++)
            Assert.True(Poly.Inside(a, at + new Vec2(Math.Cos(k * Math.PI / 6), Math.Sin(k * Math.PI / 6)) * (pegR + 1.5)), $"{k}");
        Assert.True(a.Count > 3);
        // Too small a gear A for one: said so, not left out quietly.
        var small = new GearTrainSpec { Gears = [new() { Teeth = 10 }, new() { Teeth = 20 }] }.Build();
        Assert.Contains(small.Findings, f => f.Level == Severity.Note && f.Text.Contains("too small for a handle hole"));
        Assert.DoesNotContain(small.Shopping, x => x.Contains("handle peg"));
    }

    [Fact]
    public void EveryMechanismSavesAndLoadsAgain()
    {
        MechSpec[] specs =
        [
            new GearTrainSpec { Gears = [new() { Teeth = 10 }, new() { Teeth = 30, AngleDeg = 30 }, new() { Teeth = 12, Link = GearLink.SameShaft }] },
            new RackSpec { PinionTeeth = 18, Plate = false },
            new PlanetarySpec { Held = PlanetaryHeld.Carrier },
            new CamSpec { Steps = CamPresets.All[6].Steps, Foot = FollowerFoot.Flat, Clockwise = true },
            new AutomatonSpec { Stations = [new() { StartDeg = 30, Foot = FollowerFoot.Flat }], ThicknessMm = 4 },
        ];
        foreach (var spec in specs)
        {
            string json = spec.ToJson();
            var back = MechSpec.FromJson(json);
            Assert.NotNull(back);
            Assert.Equal(spec.GetType(), back.GetType());
            Assert.Equal(json, back.ToJson());
        }
        Assert.Contains("\"Flat\"", specs[3].ToJson());
        Assert.Null(MechSpec.FromJson("{\"kind\":\"nothing\"}"));
        Assert.Null(MechSpec.FromJson("not json"));
    }

    [Fact]
    public void EveryMechanismBuildsCleanAsItStarts()
    {
        foreach (var spec in new MechSpec[] { new GearTrainSpec(), new RackSpec(), new PlanetarySpec(), new CamSpec(), new AutomatonSpec() })
        {
            var m = spec.Build();
            Assert.DoesNotContain(m.Findings, f => f.Level >= Severity.Warn);
            Assert.NotEmpty(m.Parts);
            Assert.All(m.Parts, p => Assert.True(Poly.Area(p.Shape) > 1, p.Name));
            Assert.NotEmpty(m.Summary);
            Assert.All(m.Strip, s => Assert.True(s < Severity.Warn));
        }
    }

    // ---- putting the parts in the design ----

    static IEnumerable<DocState> PagesOf(DocState s) => Enumerable.Range(0, s.OtherPages.Count + 1).Select(i => Pages.SwitchTo(s, i));

    public static TheoryData<int> Kinds() => [0, 1, 2, 3, 4];

    static MechSpec Starting(int kind) => kind switch
    {
        0 => new GearTrainSpec(),
        1 => new RackSpec(),
        2 => new PlanetarySpec(),
        3 => new CamSpec(),
        _ => new AutomatonSpec(),
    };

    [Theory]
    [MemberData(nameof(Kinds))]
    public void ThePartsGoOnACutLayerNestedOnTheBed(int kind)
    {
        var spec = Starting(kind);
        var m = spec.Build();
        var machine = MachineProfile.F2Ultra;
        var (s, sheets, id) = Placement.Place(DocState.New(), m, spec, machine, new PlaceOptions());
        Assert.Equal(0, s.PageIndex);
        Assert.Equal(sheets, s.OtherPages.Count + 1);
        var outlines = new List<PathShape>();
        foreach (var page in PagesOf(s))
        {
            var cut = Assert.Single(page.Layers, l => l.Name == Placement.CutLayer);
            var marks = Assert.Single(page.Layers, l => l.Name == Placement.MarkLayer);
            Assert.Equal(LayerMode.Cut, cut.Mode);
            Assert.Equal(LayerMode.Score, marks.Mode);
            Assert.True(marks.Laser.Power <= 15);
            // Marked first, while the sheet is whole.
            Assert.True(page.Layers.IndexOf(marks) < page.Layers.IndexOf(cut));
            var mine = page.Shapes.Where(x => x.AddonData.ContainsKey(Placement.IdKey)).ToList();
            Assert.NotEmpty(mine);
            Assert.All(mine, x => Assert.Equal(id.ToString(), x.AddonData[Placement.IdKey]));
            var onPage = mine.Where(x => x.LayerId == cut.Id).Cast<PathShape>().ToList();
            Assert.All(mine.Where(x => x.LayerId != cut.Id), x => Assert.Equal(marks.Id, x.LayerId));
            // Inside the bed, the gap round the edge kept.
            Assert.All(mine, x => Assert.True(x.Bounds.MinX >= 2.4 && x.Bounds.MinY >= 2.4 && x.Bounds.MaxX <= machine.FieldW - 2.4 && x.Bounds.MaxY <= machine.FieldH - 2.4, $"{x.Name} at {x.Bounds}"));
            // Parts never overlap (one may sit in another's hole).
            var shapes = onPage.Select(x => Poly.FromGeometry(x.World)).ToList();
            for (int i = 0; i < shapes.Count; i++)
                for (int j = i + 1; j < shapes.Count; j++)
                    Assert.True(Poly.Area(Poly.Intersect(shapes[i], shapes[j])) < 0.5, $"{onPage[i].Name} overlaps {onPage[j].Name}");
            // Each part's name and marks move with it.
            Assert.All(mine.Where(x => x.LayerId == marks.Id), x => Assert.Contains(onPage, o => o.GroupId == x.GroupId && o.Bounds.Inflate(0.5).Contains(x.Bounds)));
            outlines.AddRange(onPage);
        }
        // Every part, every copy, once, at its true size (turned at most).
        Assert.Equal(m.Parts.Sum(p => p.Copies), outlines.Count);
        foreach (var part in m.Parts)
        {
            var cut = outlines.Where(o => o.Name == part.Name || o.Name.StartsWith(part.Name + " (")).ToList();
            Assert.Equal(part.Copies, cut.Count);
            Assert.All(cut, c => Assert.Equal(1, Poly.Area(Poly.FromGeometry(c.World)) / Poly.Area(part.Shape), 3));
        }
        // The settings go with it, so it can be opened again.
        var found = Placement.Find(s, [outlines[0].Id]);
        Assert.NotNull(found);
        Assert.Equal(id, found.Value.Id);
        Assert.Equal(spec.ToJson(), found.Value.Spec.ToJson());
    }

    [Fact]
    public void TheCutLayerTakesTheMaterialsKerf()
    {
        var spec = new AutomatonSpec();
        var o = new PlaceOptions { CutSettings = l => l with { KerfMm = 0.16, Mode = LayerMode.Fill, Name = "Something else", Laser = l.Laser with { Speed = 12 } }, Marks = false };
        var (s, sheets, _) = Placement.Place(DocState.New(), spec.Build(), spec, MachineProfile.F2Ultra, o);
        Assert.True(sheets >= 1);
        foreach (var page in PagesOf(s))
        {
            // Every page's cut layer: the settings, but still the mechanism's cut layer.
            var cut = Assert.Single(page.Layers, l => l.Name == Placement.CutLayer);
            Assert.Equal(0.16, cut.KerfMm);
            Assert.Equal(12, cut.Laser.Speed);
            Assert.Equal(LayerMode.Cut, cut.Mode);
            Assert.DoesNotContain(page.Layers, l => l.Name == Placement.MarkLayer);
            Assert.All(page.Shapes, x => Assert.Equal(cut.Id, x.LayerId));
        }
    }

    [Fact]
    public void MakingItAgainReplacesItWhereItIs()
    {
        // Something of the person's on the page: the gears get a page of their own.
        var s = DocState.New();
        s = Ops.AddShapes(s, [new PathShape { Name = "Mine", LayerId = s.Layers[0].Id, Geometry = PathGeometry.Rect(10, 10, 20, 20) }]);
        var spec = new GearTrainSpec();
        var (placed, _, id) = Placement.Place(s, spec.Build(), spec, MachineProfile.F2Ultra, new PlaceOptions());
        Assert.Equal(["Page 1", "Gears"], Pages.Names(placed));
        Assert.Equal(1, placed.PageIndex);
        static int Outlines(DocState d) => d.Shapes.Count(x => x.AddonData.ContainsKey(Placement.SpecKey));
        int before = Outlines(placed);

        // Made again with a smaller gear B: the old parts go, the new ones go where they were, under the same name.
        var other = spec with { Gears = [new GearRow { Teeth = 12 }, new GearRow { Teeth = 30 }] };
        var (again, _, id2) = Placement.Place(placed, other.Build(), other, MachineProfile.F2Ultra, new PlaceOptions { Replace = id });
        Assert.Equal(id, id2);
        Assert.Equal(["Page 1", "Gears"], Pages.Names(again));
        Assert.Equal(1, again.PageIndex);
        Assert.Equal(before, Outlines(again));
        Assert.Equal(again.Shapes.Count, Placement.ShapesOf(again, id).Count);
        Assert.Contains(again.Shapes, x => x.Name == "Gear B (30 teeth)");
        Assert.DoesNotContain(again.Shapes, x => x.Name.Contains("36 teeth"));
        Assert.Equal(other.ToJson(), Placement.Find(again, [again.Shapes[0].Id])!.Value.Spec.ToJson());
        Assert.Single(Pages.SwitchTo(again, 0).Shapes);
        // One layer each, not one more each time.
        Assert.Single(again.Layers, l => l.Name == Placement.CutLayer);

        // A mechanism over several pages, made again smaller: the pages it no longer needs go; the person's stay.
        var big = new AutomatonSpec { Stations = [new(), new(), new()] };
        var (three, sheets, bid) = Placement.Place(DocState.New(), big.Build(), big, MachineProfile.F2Ultra, new PlaceOptions());
        Assert.True(sheets >= 2, $"{sheets}");
        var small = new CamSpec();
        var (one, n, _) = Placement.Place(three, small.Build(), small, MachineProfile.F2Ultra, new PlaceOptions { Replace = bid });
        Assert.Equal(1, n);
        Assert.Empty(one.OtherPages);
        Assert.False(Placement.Exists(Placement.Remove(one, bid), bid));
    }

    [Fact]
    public void APartBiggerThanTheBedIsRefused()
    {
        var spec = new GearTrainSpec { Gears = [new() { Teeth = 12 }, new() { Teeth = 160 }] };
        var e = Assert.Throws<InvalidOperationException>(() => Placement.Place(DocState.New(), spec.Build(), spec, MachineProfile.F2Ultra, new PlaceOptions()));
        Assert.Contains("too big for the 220 × 220 mm bed", e.Message);
    }
}
