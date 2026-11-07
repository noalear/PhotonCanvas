using PhotonCanvas.Addon.StockCarve;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Geometry;
using static PhotonCanvas.Tests.StockCarveTests;

namespace PhotonCanvas.Tests;

public class ProfileCutTests
{
    const double LineY = 110;

    static StockMap RodStock(double r = 10, double length = 60) => StockMap.FromMesh(Rod(r, 0, length), stepDeg: 2, stepMm: 0.5);

    [Fact]
    public void Views_go_top_and_side_then_the_45s_then_the_22s()
    {
        Assert.Equal([0, 90], ProfileCut.Views(new ProfileOptions { Directions = 2 }));
        Assert.Equal([0, 90, 45, 135], ProfileCut.Views(new ProfileOptions { Directions = 4 }));
        Assert.Equal([0, 90, 45, 135, 22.5, 67.5, 112.5, 157.5], ProfileCut.Views(new ProfileOptions()));
        Assert.Equal([30, 120], ProfileCut.Views(new ProfileOptions { Directions = 2, StartDeg = 30 }));
    }

    [Fact]
    public void Both_sides_cuts_each_view_twice_half_a_turn_apart_and_checks_after_the_second()
    {
        var steps = ProfileCut.Steps(new ProfileOptions());
        Assert.Equal(16, steps.Count);
        for (int v = 0; v < 8; v++)
        {
            var a = steps[2 * v];
            var b = steps[2 * v + 1];
            Assert.Equal(v, a.View);
            Assert.Equal(v, b.View);
            Assert.False(a.EndsView);
            Assert.True(b.EndsView);
            Assert.Equal(180, Math.Abs(ProfileCut.TurnBetween(a.ViewDeg, b.ViewDeg)), 6);
        }
        Assert.Equal(0, steps[0].ViewDeg);
        // Never more than half a turn between cuts.
        for (int i = 1; i < steps.Count; i++) Assert.True(Math.Abs(ProfileCut.TurnBetween(steps[i - 1].ViewDeg, steps[i].ViewDeg)) <= 180);

        var one = ProfileCut.Steps(new ProfileOptions { BothSides = false, Directions = 4 });
        Assert.Equal([0, 90, 45, 135], one.Select(s => s.ViewDeg));
        Assert.All(one, s => Assert.True(s.EndsView));
    }

    [Fact]
    public void The_silhouette_is_seen_the_way_the_part_is_turned()
    {
        // A block toward −Y (θ = 90°) at the start: seen at 0° it's at negative h (toward the front of the machine).
        var side = Box(-12, -2, -6, 2, 10, 30);
        var b0 = Rect2.Bounds(StackSlicer.Loops(ProfileCut.Silhouette(side, 0)).SelectMany(l => l));
        Assert.Equal(-12, b0.MinY, 3);
        Assert.Equal(-6, b0.MaxY, 3);
        Assert.Equal(10, b0.MinX, 3);
        Assert.Equal(30, b0.MaxX, 3);
        // A block on top (+Z), seen after a quarter turn (90°): the top has gone toward the back, +h.
        var top = Box(-2, 6, 2, 12, 10, 30);
        var b90 = Rect2.Bounds(StackSlicer.Loops(ProfileCut.Silhouette(top, 90)).SelectMany(l => l));
        Assert.Equal(6, b90.MinY, 3);
        Assert.Equal(12, b90.MaxY, 3);
        // Half a turn on, the outline is the mirror image.
        var b270 = Rect2.Bounds(StackSlicer.Loops(ProfileCut.Silhouette(top, 270)).SelectMany(l => l));
        Assert.Equal(-12, b270.MinY, 3);
        Assert.Equal(-6, b270.MaxY, 3);
    }

    [Fact]
    public void A_round_rod_is_as_deep_as_its_chord()
    {
        var rem = Remaining.From(RodStock());
        var mid = rem.TopBottom(30, 0, 0)!.Value;
        Assert.Equal(10, mid.Top, 1);
        Assert.Equal(-10, mid.Bottom, 1);
        var off = rem.TopBottom(30, 6, 37)!.Value;
        Assert.Equal(8, off.Top, 1);
        Assert.Equal(-8, off.Bottom, 1);
        Assert.Null(rem.TopBottom(30, 11, 0));
        Assert.Null(rem.TopBottom(70, 0, 0));
    }

    [Fact]
    public void Cutting_top_and_side_leaves_the_model_square_and_the_bar_to_the_chuck()
    {
        var stock = RodStock();
        var rem = Remaining.From(stock);
        double whole = rem.VolumeMm3();
        var placed = Box(-3, -3, 3, 3, 20, 40);
        var o = new ProfileOptions { LeaveMm = 0.5, CoreMm = 2 };
        foreach (var v in new[] { 0.0, 90 }) rem = rem.After(ProfileCut.Keep(placed, v, o, stock.MinX, stock.MaxX), v);
        int k = rem.Column(30);
        double step = rem.StepDeg;
        // Straight up: the leave over the square; at 45° its corner.
        Assert.Equal(3.5, rem.R[0, k], 1);
        Assert.Equal(3.5 * Math.Sqrt(2), rem.R[(int)Math.Round(45 / step), k], 1);
        // Past the model's free end: gone; toward the chuck (left): the bar.
        Assert.Equal(0, rem.R[0, rem.Column(50)], 6);
        Assert.Equal(2, rem.R[0, rem.Column(10)], 1);
        Assert.True(rem.VolumeMm3() < whole * 0.2);

        // The chuck on the right keeps the bar that end instead.
        var right = Remaining.From(stock).After(ProfileCut.Keep(placed, 0, o with { ChuckOnLeft = false }, stock.MinX, stock.MaxX), 0);
        int side = (int)Math.Round(90 / right.StepDeg);
        Assert.Equal(0, right.R[side, right.Column(10)], 6);
        Assert.Equal(2, right.R[side, right.Column(50)], 1);
    }

    [Fact]
    public void A_cut_goes_round_the_outline_out_in_the_waste_and_steps_its_focus_down()
    {
        var stock = RodStock();
        var rem = Remaining.From(stock);
        var placed = Box(-3, -3, 3, 3, 20, 40);
        var o = new ProfileOptions { LeaveMm = 0.5, CoreMm = 2, FocusStepMm = 2, PassDepthMm = 0.5, TrenchMm = 1, LineGapMm = 0.25, OverlapMm = 1 };
        var step = ProfileCut.Steps(o)[0];
        var plan = ProfileCut.Plan(rem, placed, step, o, LineY, 30);
        Assert.Equal(30, plan.FocusX, 6);
        Assert.Equal(10, plan.TopMm, 1);
        Assert.Equal(4, plan.PassesPerStep);
        Assert.NotEmpty(plan.Bands);
        // From both sides: down from the top (10 above the axis) to just past the middle (1 below it).
        Assert.InRange(plan.DeepestMm, 11, 13);
        for (int i = 1; i < plan.Bands.Count; i++) Assert.Equal(plan.Bands[i - 1].FocusOffsetMm + 2, plan.Bands[i].FocusOffsetMm, 6);
        Assert.Equal(1, plan.Bands[0].FocusOffsetMm, 6);

        var keepLoops = StackSlicer.Loops(plan.Keep);
        foreach (var b in plan.Bands)
            foreach (var f in b.Lines.Flatten(0.05))
                foreach (var p in f.Points)
                {
                    // On the bed, over the rod, never inside what's kept.
                    double h = LineY - p.Y;
                    Assert.InRange(h, -11, 11);
                    Assert.InRange(p.X, -6, 66);
                    Assert.False(keepLoops.Count(l => Geom.Inside(l, new Vec2(p.X, h))) % 2 == 1, $"A cut line at ({p.X:0.##}, {h:0.##}) is inside what's kept.");
                }
        // The cut goes across the rod past the model's free end (x 40.5 and a bit), so the end drops off.
        Assert.Contains(plan.Bands.SelectMany(b => b.Lines.Flatten(0.05)).SelectMany(f => f.Points), p => p.X > 40.5 && p.X < 42 && Math.Abs(LineY - p.Y) < 1);

        // Without both sides it goes right through.
        var through = ProfileCut.Plan(rem, placed, step, o with { BothSides = false }, LineY, 30);
        Assert.InRange(through.DeepestMm, 20.4, 23);
    }

    [Fact]
    public void A_cut_is_a_flat_job_with_deep_focus_allowed_only_when_asked()
    {
        var stock = RodStock();
        var rem = Remaining.From(stock);
        var placed = Box(-3, -3, 3, 3, 20, 40);
        var o = new ProfileOptions { CoreMm = 2, BothSides = false };
        var plan = ProfileCut.Plan(rem, placed, ProfileCut.Steps(o)[0], o, LineY, 30);
        var state = ProfileCut.Job(plan, Layer.Default(0, LayerMode.Cut));
        Assert.Equal(plan.Bands.Count, state.Layers.Count);
        Assert.All(state.Layers, l => Assert.Equal(plan.PassesPerStep, l.Laser.Passes));
        var opt = new JobOptions { Mode = new WorkMode.Flat(), WholeJobPasses = false };
        Assert.Throws<JobException>(() => JobBuilder.Build(state, opt));
        var job = JobBuilder.Build(state, opt with { FocusLimitMm = Job.MaxRotaryFocusOffsetMm });
        Assert.True(job.EstimateS > 0);
        Assert.Contains(job.Job.Blocks, b => b.FocusOffsetMm > 10);

        // More passes: the deepest heights only.
        var deep = ProfileCut.Job(plan, Layer.Default(0, LayerMode.Cut), ProfileCut.Deepest(plan, 2));
        Assert.Equal(2, deep.Layers.Count);
        Assert.Equal(plan.Bands[^1].FocusOffsetMm, deep.Layers[^1].FocusOffsetMm, 6);
    }

    [Fact]
    public void After_a_view_the_next_cut_is_shallower_and_skips_the_air()
    {
        var stock = RodStock();
        var placed = Box(-3, -3, 3, 3, 20, 40);
        var o = new ProfileOptions { CoreMm = 2 };
        var rem = Remaining.From(stock);
        var first = ProfileCut.Plan(rem, placed, new ProfileStep(0, 0, 90, 0, false), o, LineY, 30);
        rem = rem.After(ProfileCut.Keep(placed, 0, o, stock.MinX, stock.MaxX), 0);
        var after = ProfileCut.Plan(rem, placed, new ProfileStep(0, 0, 90, 0, false), o, LineY, 30);
        // Seen side on, the slab that's left is only 3.5 mm each side of the axis: the cut starts that much lower, and is shallower.
        Assert.InRange(first.TopMm, 9.5, 10.5);
        Assert.InRange(after.TopMm, 3, 4);
        Assert.True(after.DeepestMm < first.DeepestMm - 5);
    }

    [Fact]
    public void What_is_left_carves_from_there()
    {
        var stock = RodStock();
        var placed = Box(-3, -3, 3, 3, 20, 40);
        var o = new ProfileOptions { CoreMm = 2 };
        var rem = Remaining.From(stock).After(ProfileCut.Keep(placed, 0, o, stock.MinX, stock.MaxX), 0);
        var map = rem.ToStock();
        Assert.True(map.WholeTurn);
        Assert.Equal(10, map.RadiusAt(0, 30), 1);
        Assert.Equal(3.5, map.RadiusAt(90, 30), 1);
    }

    [Fact]
    public void Profile_cuts_need_the_stock_all_the_way_round()
    {
        double[] xs = [0, 10, 20];
        var radii = new[] { 0.0, 10, 20 }.Select(_ => (IReadOnlyList<double?>)xs.Select(_ => (double?)8).ToList()).ToList();
        var part = StockMap.FromRadii([0, 10, 20], xs, radii, wholeTurn: false);
        Assert.Throws<ArgumentException>(() => Remaining.From(part));
    }

    [Fact]
    public void The_camera_check_sees_the_waste_gone_and_ignores_a_brighter_light()
    {
        int w = 40, h = 20;
        var zones = new byte[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                zones[y * w + x] = x < 15 ? ThroughCheck.Kept : x > 20 ? ThroughCheck.Waste : ThroughCheck.Ignore;
        var before = Enumerable.Repeat((byte)120, w * h).ToArray();
        // The waste dropped away: the bed far below shows instead.
        var gone = before.Select((v, i) => zones[i] == ThroughCheck.Waste ? (byte)30 : v).ToArray();
        Assert.Equal(ThroughVerdict.Through, ThroughCheck.Judge(before, gone, zones, w, h).Verdict);
        Assert.Equal(ThroughVerdict.NotThrough, ThroughCheck.Judge(before, before, zones, w, h).Verdict);
        // The same part, the light a fifth brighter: not a change.
        var brighter = before.Select(v => (byte)(v * 1.2)).ToArray();
        Assert.Equal(ThroughVerdict.NotThrough, ThroughCheck.Judge(before, brighter, zones, w, h).Verdict);
        // Some of it gone.
        var some = before.Select((v, i) => zones[i] == ThroughCheck.Waste && i % w > 33 ? (byte)30 : v).ToArray();
        Assert.Equal(ThroughVerdict.Partly, ThroughCheck.Judge(before, some, zones, w, h).Verdict);
    }

    [Fact]
    public void The_camera_zones_line_up_with_the_bed()
    {
        var stock = RodStock();
        var placed = Box(-3, -3, 3, 3, 20, 40);
        var o = new ProfileOptions { CoreMm = 2 };
        var plan = ProfileCut.Plan(Remaining.From(stock), placed, ProfileCut.Steps(o)[1], o, LineY, 30);
        var area = new Rect2(0, LineY - 15, 60, LineY + 15);
        var zones = ThroughCheck.Zones(plan, o, LineY, area, 0.5, out int w, out int h);
        Assert.Equal(120, w);
        Assert.Equal(60, h);
        byte At(double x, double y) => zones[(int)((y - area.MinY) / 0.5) * w + (int)((x - area.MinX) / 0.5)];
        Assert.Equal(ThroughCheck.Kept, At(30, LineY));
        Assert.Equal(ThroughCheck.Waste, At(30, LineY + 8));
        Assert.Equal(ThroughCheck.Waste, At(50, LineY));
        Assert.Equal(ThroughCheck.Ignore, At(30, LineY + 13));
    }
}
