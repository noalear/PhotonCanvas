using System.Collections.Immutable;
using PhotonCanvas.Addon.Hollow;
using PhotonCanvas.Addon.RotaryFinish;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Geometry;
using Tri = PhotonCanvas.Doc.Import.StlDepth.Triangle;
using V3 = PhotonCanvas.Doc.Import.StlDepth.Vec3;

namespace PhotonCanvas.Tests;

public class RotaryFinishTests
{
    /// <summary>A convex solid from its corners and faces (any winding: each face is turned to face out).</summary>
    static List<Tri> Convex(V3[] c, params int[][] faces)
    {
        var mid = new V3(c.Average(p => p.X), c.Average(p => p.Y), c.Average(p => p.Z));
        var o = new List<Tri>();
        foreach (var f in faces)
            for (int i = 1; i + 1 < f.Length; i++)
            {
                V3 a = c[f[0]], b = c[f[i]], d = c[f[i + 1]];
                double ux = b.X - a.X, uy = b.Y - a.Y, uz = b.Z - a.Z, vx = d.X - a.X, vy = d.Y - a.Y, vz = d.Z - a.Z;
                double nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
                bool outward = nx * (a.X - mid.X) + ny * (a.Y - mid.Y) + nz * (a.Z - mid.Z) > 0;
                o.Add(outward ? new Tri(a, b, d) : new Tri(a, d, b));
            }
        return o;
    }

    static List<Tri> Box(double x0, double y0, double z0, double x1, double y1, double z1) => Convex(
        [new(x0, y0, z0), new(x1, y0, z0), new(x1, y1, z0), new(x0, y1, z0), new(x0, y0, z1), new(x1, y0, z1), new(x1, y1, z1), new(x0, y1, z1)],
        [0, 1, 2, 3], [4, 5, 6, 7], [0, 1, 5, 4], [1, 2, 6, 5], [2, 3, 7, 6], [3, 0, 4, 7]);

    /// <summary>A round post narrowing from r0 at the bottom to r1 at the top (48 sides).</summary>
    static List<Tri> Frustum(double cx, double cy, double r0, double r1, double h, int sides = 48)
    {
        var c = new V3[2 * sides];
        for (int i = 0; i < sides; i++)
        {
            double t = 2 * Math.PI * i / sides;
            c[i] = new(cx + r0 * Math.Cos(t), cy + r0 * Math.Sin(t), 0);
            c[sides + i] = new(cx + r1 * Math.Cos(t), cy + r1 * Math.Sin(t), h);
        }
        var faces = new List<int[]> { Enumerable.Range(0, sides).ToArray(), Enumerable.Range(sides, sides).ToArray() };
        for (int i = 0; i < sides; i++) faces.Add([i, (i + 1) % sides, sides + (i + 1) % sides, sides + i]);
        return Convex(c, [.. faces]);
    }

    /// <summary>A 40 × 40 × 30 block with a ramp on one side (modelled −Y = its front, or +X = its right), 10 mm out at the bottom, flush at the top.</summary>
    static List<Tri> BlockWithRamp(bool right)
    {
        var block = Box(0, 0, 0, 40, 40, 30);
        V3[] ramp = right
            ? [new(40, 10, 0), new(50, 10, 0), new(40, 10, 30), new(40, 30, 0), new(50, 30, 0), new(40, 30, 30)]
            : [new(10, 0, 0), new(10, -10, 0), new(10, 0, 30), new(30, 0, 0), new(30, -10, 0), new(30, 0, 30)];
        return [.. block, .. Convex(ramp, [0, 1, 2], [3, 4, 5], [0, 1, 4, 3], [1, 2, 5, 4], [0, 2, 5, 3])];
    }

    static SliceOptions Opts(RotaryFinishOptions? r = null, double pin = 0, IEnumerable<ISliceModifier>? more = null) => new()
    {
        HeightMm = 30, ThicknessMm = 3, PinMm = pin, Outline = SliceOutline.Outer,
        Modifiers = [.. (more ?? []), new RotaryFinishSlices(r ?? new RotaryFinishOptions())],
    };

    static RotaryPlan PlanOf(SliceStack s)
    {
        var prep = (RotaryPrep)s.Extras[RotaryFinish.Key];
        var parts = Enumerable.Range(1, s.Slices).Select(i => (IReadOnlyList<PathGeometry>)[.. s.Parts.Where(p => p.Slice == i).Select(p => p.Outline)]).ToList();
        return RotaryFinish.Plan(prep, parts, MachineProfile.F2Ultra.FieldW);
    }

    static Rect2 PassBounds(RotaryPlan p) => p.Bands.SelectMany(b => b.Passes).Aggregate(Rect2.Empty, (r, g) => r.Union(g.Bounds()));

    [Fact]
    public void A_dowel_hole_with_a_notch_goes_up_the_middle_of_every_slice()
    {
        var s = StackSlicer.Slice(Frustum(20, 20, 20, 12, 30), Opts(new RotaryFinishOptions { DowelMm = 6 }));
        var hole = Assert.Single(s.Holes);
        Assert.Equal(6, hole.DiameterMm);
        // The post is round about its middle: that's where it swings least.
        Assert.True(hole.At.Dist(new Vec2(20, 20)) < 1, $"dowel at {hole.At}");
        foreach (var p in s.Parts)
        {
            var loops = StackSlicer.Loops(p.Outline);
            Assert.False(loops.Count(l => Geom.Inside(l, hole.At)) % 2 == 1, $"slice {p.Slice} has no hole at the dowel");
            // The notch: open just past the hole's edge on the start side (down on the slices), solid on the other.
            Assert.False(loops.Count(l => Geom.Inside(l, hole.At + new Vec2(0, 3.6))) % 2 == 1, $"slice {p.Slice} has no notch");
            Assert.True(loops.Count(l => Geom.Inside(l, hole.At - new Vec2(0, 3.6))) % 2 == 1);
        }
        Assert.Contains(s.Notes, n => n.StartsWith("Rotary finish: a 6 mm dowel"));
    }

    [Fact]
    public void Without_room_for_the_dowel_it_says_so_and_cuts_nothing()
    {
        // Two separate posts: no straight line goes through every slice.
        var s = StackSlicer.Slice([.. Box(0, 0, 0, 8, 8, 30), .. Box(20, 0, 0, 28, 8, 30)], Opts());
        Assert.Empty(s.Holes);
        Assert.False(s.Extras.ContainsKey(RotaryFinish.Key));
        Assert.Contains(s.Notes, n => n.Contains("no straight line goes through every slice"));
    }

    [Fact]
    public void Straight_sides_have_nothing_to_take_off()
    {
        var s = StackSlicer.Slice(Box(0, 0, 0, 40, 40, 30), Opts());
        var plan = PlanOf(s);
        Assert.Empty(plan.Bands);
        var mod = new RotaryFinishSlices(new RotaryFinishOptions());
        var doc = DocState.New();
        Assert.Same(doc, mod.AfterMake(doc, s, MachineProfile.F2Ultra, "Block"));
    }

    [Fact]
    public void A_tapered_post_comes_down_to_its_slope()
    {
        // 20 mm → 12 mm over 30 mm: each 3 mm slice's steps stick out up to 0.8 mm.
        var s = StackSlicer.Slice(Frustum(20, 20, 20, 12, 30), Opts(new RotaryFinishOptions { PassDepthMm = 0.1, FocusBandMm = 1 }));
        var p = PlanOf(s);
        Assert.InRange(p.DiameterMm, 40, 41);
        Assert.InRange(p.DeepestMm, 0.55, 0.85);
        Assert.InRange(p.MostPasses, 4, 8);
        Assert.Equal(0, p.TooDeepMm2);
        // The surface goes from 20 mm out down to 12 mm: several focus heights, outermost first, each lowering the head more.
        Assert.InRange(p.Bands.Count, 7, 9);
        for (int i = 1; i < p.Bands.Count; i++)
        {
            Assert.True(p.Bands[i].RadiusMm < p.Bands[i - 1].RadiusMm);
            Assert.True(p.Bands[i].FocusOffsetMm > p.Bands[i - 1].FocusOffsetMm);
        }
        // All round, along the stack's length, centred in the field.
        var b = PassBounds(p);
        Assert.Equal(30, p.LengthMm, 6);
        Assert.Equal((220 - 30) / 2.0, p.StartX, 6);
        Assert.True(b.MinX >= p.StartX - 0.5 && b.MaxX <= p.StartX + 30.5, $"{b}");
        Assert.True(b.MinY >= -1e-6 && b.MaxY <= p.CircumferenceMm + 1e-6 && b.Height > p.CircumferenceMm * 0.95, $"{b}");
    }

    [Fact]
    public void Each_band_holds_only_the_surface_that_far_out()
    {
        // A post 10 mm off the dowel on one side: the surface is far out at some angles, near at others.
        var s = StackSlicer.Slice(Frustum(20, 20, 20, 12, 30), Opts(new RotaryFinishOptions { FocusBandMm = 2 }));
        var p = PlanOf(s);
        // Near the bottom the post is wide (outer bands), near the top narrow (inner bands).
        var outer = p.Bands[0].Passes.Aggregate(Rect2.Empty, (r, g) => r.Union(g.Bounds()));
        var inner = p.Bands[^1].Passes.Aggregate(Rect2.Empty, (r, g) => r.Union(g.Bounds()));
        Assert.True(outer.Center.X < inner.Center.X, $"outer band at {outer}, inner at {inner}");
    }

    [Theory]
    [InlineData(false, false, 0.5)]   // a ramp on the front faces the laser at the start: the middle of the page
    [InlineData(true, false, 0.75)]   // on the right: a quarter turn on
    [InlineData(true, true, 0.25)]    // on the right with the stack the other way round: a quarter turn back
    public void The_engraving_lands_where_the_model_slopes(bool right, bool flipped, double at)
    {
        var s = StackSlicer.Slice(BlockWithRamp(right), Opts(new RotaryFinishOptions { BottomOnRight = flipped }));
        var p = PlanOf(s);
        Assert.NotEmpty(p.Bands);
        var b = PassBounds(p);
        double c = p.CircumferenceMm;
        Assert.True(Math.Abs(b.Center.Y - at * c) < c / 10, $"engraving centred at {b.Center.Y:0.#} of {c:0.#}, wanted {at * c:0.#}");
        Assert.True(b.Height < c / 3, $"{b}");
        // The ramp is wide at the bottom: its steps are along the whole stack, each slice's from its bottom.
        Assert.True(b.Width > 25, $"{b}");
        // (Across a flat face, slanting rays meet more of the step than straight on.)
        Assert.InRange(p.DeepestMm, 0.6, 1.3);
    }

    [Fact]
    public void Slice_one_on_the_right_mirrors_the_page()
    {
        var left = PlanOf(StackSlicer.Slice(Frustum(20, 20, 20, 12, 30), Opts()));
        var right = PlanOf(StackSlicer.Slice(Frustum(20, 20, 20, 12, 30), Opts(new RotaryFinishOptions { BottomOnRight = true })));
        Assert.Equal(left.Bands.Count, right.Bands.Count);
        var lb = left.Bands[0].Passes.Aggregate(Rect2.Empty, (r, g) => r.Union(g.Bounds()));
        var rb = right.Bands[0].Passes.Aggregate(Rect2.Empty, (r, g) => r.Union(g.Bounds()));
        Assert.Equal(220 - lb.Center.X, rb.Center.X, 0);
    }

    [Fact]
    public void It_never_goes_deeper_than_asked()
    {
        var s = StackSlicer.Slice(Frustum(20, 20, 20, 12, 30), Opts(new RotaryFinishOptions { MaxDepthMm = 0.3 }));
        var p = PlanOf(s);
        Assert.True(p.DeepestMm <= 0.3 + 0.06, $"{p.DeepestMm}");
        Assert.True(p.TooDeepMm2 > 0);
        Assert.Contains("sand those spots", RotaryFinish.Describe(p, new RotaryFinishOptions { MaxDepthMm = 0.3 }));
    }

    [Fact]
    public void Hollowing_keeps_material_round_the_dowel_and_the_pins_keep_clear()
    {
        var s = StackSlicer.Slice(Box(0, 0, 0, 40, 40, 30), Opts(new RotaryFinishOptions { DowelMm = 6 }, pin: 2, more: [new HollowSlices(new HollowOptions { WallMm = 5 })]));
        var hole = Assert.Single(s.Holes);
        // The rotary step runs first (it was set second), so the hollow sees the dowel.
        Assert.True(((HollowReport)s.Extras[HollowSlices.Key]).HollowSliceCount > 0);
        foreach (var p in s.Parts)
        {
            var loops = StackSlicer.Loops(p.Outline);
            Assert.True(loops.Count(l => Geom.Inside(l, hole.At + new Vec2(4.5, 0))) % 2 == 1, $"slice {p.Slice}: no material beside the dowel");
        }
        Assert.All(s.Pins, pin => Assert.True(pin.At.Dist(hole.At) > 3 + 1 + 1.2, $"pin at {pin.At} too near the dowel"));
    }

    [Fact]
    public void Make_adds_a_rotary_page_with_a_layer_a_focus_height()
    {
        var mod = new RotaryFinishSlices(new RotaryFinishOptions(), Layer.Default(0, LayerMode.Fill) with { LinesPerCm = 120 });
        var s = StackSlicer.Slice(Frustum(20, 20, 20, 12, 30), new SliceOptions { HeightMm = 30, ThicknessMm = 3, Outline = SliceOutline.Outer, Modifiers = [mod] });
        var doc = DocState.New();
        var after = mod.AfterMake(doc, s, MachineProfile.F2Ultra, "Post");
        Assert.Equal("Post on the rotary", after.PageName);
        Assert.Single(after.OtherPages);
        var plan = PlanOf(s);
        Assert.Equal(plan.Bands.Count, after.Layers.Count);
        Assert.All(after.Layers, l =>
        {
            Assert.Equal(LayerMode.Fill, l.Mode);
            Assert.Equal(120, l.LinesPerCm);
            Assert.Equal(1, l.Laser.Passes);
            Assert.StartsWith("Rotary · focus", l.Name);
        });
        Assert.Equal(plan.Bands.Select(b => b.FocusOffsetMm), after.Layers.Select(l => l.FocusOffsetMm));
        Assert.Equal(plan.PassCount, after.Shapes.Count);
        var start = Assert.Single(after.Guides, g => g.RotaryStart);
        Assert.Equal(plan.CircumferenceMm / 2, start.At, 6);
        Assert.Equal(plan.DiameterMm, RotaryPage.MadeFor(after));
        Assert.Null(RotaryPage.MadeFor(doc));
    }

    [Fact]
    public void The_page_builds_a_rotary_job_from_its_start_line()
    {
        var mod = new RotaryFinishSlices(new RotaryFinishOptions { FocusBandMm = 2 });
        var s = StackSlicer.Slice(Frustum(20, 20, 20, 12, 30), new SliceOptions { HeightMm = 30, ThicknessMm = 3, Outline = SliceOutline.Outer, Modifiers = [mod] });
        var page = mod.AfterMake(DocState.New(), s, MachineProfile.F2Ultra, "Post");
        double c = PlanOf(s).CircumferenceMm;
        var job = JobBuilder.Build(page, new JobOptions { Mode = new WorkMode.Rotary(WorkModes.ChuckTurnUnits / c, 1, c) });
        var g = job.Motion;
        Assert.Contains($"M535U{WorkModes.ChuckTurnUnits / c:0.#####}", g);
        // The start line, not the middle of what's drawn, faces the laser at the start.
        Assert.Contains($"\"start\":{{\"x\":0,\"y\":{c / 2:0.0000}}}", g);
        // The focus moves between bands: out of U mode, a Z move, back in.
        Assert.Contains("G101\n", g);

        var check = new RotaryPageCheck();
        Assert.Empty(check.Check(page, new JobOptions { Mode = new WorkMode.Rotary(1, 1, c) }));
        Assert.Contains(check.Check(page, new JobOptions()), w => w.Contains("this page is for the rotary"));
        Assert.Contains(check.Check(page, new JobOptions { Mode = new WorkMode.Rotary(1, 1, Math.PI * 60) }), w => w.Contains($"Object Ø {PlanOf(s).DiameterMm:0.#} mm"));
        Assert.Contains(check.Check(page, new JobOptions { Mode = new WorkMode.Rotary(1, -1, c) }), w => w.Contains("chuck"));
    }

    [Fact]
    public void The_rotary_start_line_is_saved()
    {
        var s = DocState.New() with { Guides = [new Guide(false, 62.8) { RotaryStart = true }, new Guide(true, 10)] };
        var back = DocFile.Deserialize(DocFile.Serialize(s));
        Assert.True(back.Guides[0].RotaryStart);
        Assert.Equal(62.8, back.Guides[0].At);
        Assert.False(back.Guides[1].RotaryStart);
    }

    [Fact]
    public void Your_own_rotary_has_its_own_scale()
    {
        var s = new AppSettings { OwnsCustomRotary = true, WorkMode = "Rotary", RotaryAttachment = WorkModes.CustomRotary, RotaryDiameterMm = 40, CustomRotaryTurnUnits = 200 };
        Assert.True(WorkModes.Owned(s, WorkModes.CustomRotary));
        Assert.False(WorkModes.Owned(s, 1));
        Assert.True(WorkModes.OwnsRotary(s));
        Assert.True(s.OwnsAttachment);
        var r = Assert.IsType<WorkMode.Rotary>(WorkModes.From(s));
        Assert.Equal(200 / (Math.PI * 40), r.UMultiple, 9);
        Assert.Equal(1, r.Direction);
        s.CustomRotaryReverse = true;
        Assert.Equal(-1, Assert.IsType<WorkMode.Rotary>(WorkModes.From(s)).Direction);
        s.CustomRotaryChuck = false;
        s.CustomRotaryRollerScale = 0.7;
        var rollers = Assert.IsType<WorkMode.Rotary>(WorkModes.From(s));
        Assert.Equal(0.7, rollers.UMultiple, 9);
        Assert.Equal(1, rollers.Direction); // rollers turn the object backwards, reversed again
        Assert.Contains("Your own rotary", WorkModes.Describe(s));
        // Without it, the flat bed.
        s.OwnsCustomRotary = false;
        Assert.IsType<WorkMode.Flat>(WorkModes.From(s));
        // xTool's own are as before.
        var ra2 = new AppSettings { OwnsRa2 = true, WorkMode = "Rotary", RotaryAttachment = 1, RotaryDiameterMm = 40 };
        Assert.Equal(128 / (Math.PI * 40), Assert.IsType<WorkMode.Rotary>(WorkModes.From(ra2)).UMultiple, 9);
    }

    [Fact]
    public void A_test_turn_corrects_the_scale()
    {
        // Asked for one turn, it made half: twice the units.
        Assert.Equal(256, WorkModes.Calibrated(128, 1, 0.5), 9);
        Assert.Equal(64, WorkModes.Calibrated(128, 2, 4), 9);
        Assert.Equal(128, WorkModes.Calibrated(128, 1, 0), 9);
    }
}
