using PhotonCanvas.Addon.RotaryFinish;
using PhotonCanvas.Addon.StockCarve;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using Tri = PhotonCanvas.Doc.Import.StlDepth.Triangle;
using V3 = PhotonCanvas.Doc.Import.StlDepth.Vec3;

namespace PhotonCanvas.Tests;

public class StockCarveTests
{
    /// <summary>A convex solid from its corners and faces (each face turned to face out).</summary>
    internal static List<Tri> Convex(V3[] c, params int[][] faces)
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

    /// <summary>A prism along X from x0 to x1, its cross-section a convex outline in (Y, Z).</summary>
    internal static List<Tri> Prism(IReadOnlyList<(double Y, double Z)> outline, double x0, double x1)
    {
        int n = outline.Count;
        var c = new V3[2 * n];
        for (int i = 0; i < n; i++)
        {
            c[i] = new(x0, outline[i].Y, outline[i].Z);
            c[n + i] = new(x1, outline[i].Y, outline[i].Z);
        }
        var faces = new List<int[]> { Enumerable.Range(0, n).ToArray(), Enumerable.Range(n, n).ToArray() };
        for (int i = 0; i < n; i++) faces.Add([i, (i + 1) % n, n + (i + 1) % n, n + i]);
        return Convex(c, [.. faces]);
    }

    internal static List<(double, double)> Circle(double r, double cy = 0, double cz = 0, int sides = 96) =>
        [.. Enumerable.Range(0, sides).Select(i => { double t = 2 * Math.PI * i / sides; return (cy + r * Math.Cos(t), cz + r * Math.Sin(t)); })];

    internal static List<Tri> Rod(double r, double x0, double x1, double cy = 0, double cz = 0) => Prism(Circle(r, cy, cz), x0, x1);

    internal static List<Tri> Box(double y0, double z0, double y1, double z1, double x0, double x1) => Prism([(y0, z0), (y1, z0), (y1, z1), (y0, z1)], x0, x1);

    [Fact]
    public void A_stock_model_is_read_by_angle_and_place()
    {
        var stock = StockMap.FromMesh(Rod(10, 20, 80), stepDeg: 3, stepMm: 1);
        Assert.Equal(20, stock.MinX, 1);
        Assert.Equal(80, stock.MaxX, 1);
        foreach (var t in new[] { 0.0, 37, 90, 181.5, 300 })
            Assert.InRange(stock.RadiusAt(t, 50), 9.9, 10.01);
        Assert.Equal(0, stock.RadiusAt(0, 10));
        Assert.Equal(0, stock.RadiusAt(0, 81));
        Assert.True(stock.WholeTurn);
    }

    [Fact]
    public void Angles_go_the_way_the_scan_measures_them()
    {
        // A block reaching 15 mm up (+Z) and 5 mm everywhere else: θ = 0 is up, θ = 90 is toward −Y.
        var stock = StockMap.FromMesh(Box(-5, -5, 5, 15, 0, 40), stepDeg: 2, stepMm: 1);
        Assert.Equal(15, stock.RadiusAt(0, 20), 1);
        Assert.Equal(5, stock.RadiusAt(180, 20), 1);
        Assert.Equal(5, stock.RadiusAt(90, 20), 1);
        Assert.Equal(5, stock.RadiusAt(270, 20), 1);
        // Toward −Y: a block reaching 12 mm that way is widest at 90°.
        var side = StockMap.FromMesh(Box(-12, -5, 5, 5, 0, 40), stepDeg: 2, stepMm: 1);
        Assert.Equal(12, side.RadiusAt(90, 20), 1);
        Assert.Equal(5, side.RadiusAt(270, 20), 1);
    }

    [Fact]
    public void Scan_radii_with_part_of_a_turn_are_unknown_elsewhere()
    {
        double[] angles = [0, 10, 20, 30, 40];
        double[] xs = [0, 10, 20];
        var radii = angles.Select(_ => (IReadOnlyList<double?>)xs.Select(_ => (double?)8).ToList()).ToList();
        var stock = StockMap.FromRadii(angles, xs, radii, wholeTurn: false);
        Assert.Equal(8, stock.RadiusAt(25, 5), 6);
        Assert.True(double.IsNaN(stock.RadiusAt(180, 5)));
        Assert.Equal(8, stock.RadiusAt(40, 5), 6);
        // Negative angles wrap round.
        var neg = StockMap.FromRadii([-20, -10, 0, 10, 20], xs, radii, wholeTurn: false);
        Assert.Equal(8, neg.RadiusAt(355, 5), 6);
        Assert.True(double.IsNaN(neg.RadiusAt(90, 5)));
    }

    [Fact]
    public void Turning_round_the_axis_moves_a_point_the_scans_way()
    {
        // A small block on top of the model (+Z): turned 90° round the axis it ends up toward −Y (θ = 90).
        var model = new TargetModel(Box(-1, 9, 1, 11, 0, 2));
        var placed = model.Placed(new Placement { SpinDeg = 90, UpMm = 10 });
        var ys = placed.SelectMany(t => new[] { t.A.Y, t.B.Y, t.C.Y }).ToList();
        var zs = placed.SelectMany(t => new[] { t.A.Z, t.B.Z, t.C.Z }).ToList();
        double y = (ys.Min() + ys.Max()) / 2, z = (zs.Min() + zs.Max()) / 2;
        Assert.Equal(0, y, 6);
        Assert.Equal(10, z, 6);
        // A model off its middle: the top corner turned 90° goes to −Y.
        var tall = new TargetModel(Box(-1, -1, 1, 9, 0, 2)); // middle at z = 4: top at +5
        var turned = tall.Placed(new Placement { SpinDeg = 90 });
        Assert.Equal(-5, turned.Min(t => Math.Min(t.A.Y, Math.Min(t.B.Y, t.C.Y))), 6);
    }

    [Fact]
    public void A_model_is_laid_with_its_longest_side_along_the_axis()
    {
        var upright = new TargetModel(Box(-5, 0, 5, 60, 0, 10)); // 10 along X, 10 Y, 60 Z
        var p = upright.StartIn(null);
        var placed = upright.Placed(p);
        double len = placed.Max(t => t.A.X) - placed.Min(t => t.A.X);
        Assert.Equal(60, len, 3);
    }

    [Fact]
    public void A_smaller_model_fits_and_a_bigger_one_sticks_out()
    {
        var stock = StockMap.FromMesh(Rod(10, 0, 60), stepDeg: 2, stepMm: 0.5);
        var small = new TargetModel(Rod(8, 0, 40));
        var r = StockCarve.Compare(stock, small.Placed(new Placement { AlongMm = 30 }));
        Assert.True(r.Fits);
        Assert.InRange(r.WorstMm, -2.1, -1.8);
        Assert.False(r.PastEnds);

        var big = new TargetModel(Rod(12, 0, 40));
        var rb = StockCarve.Compare(stock, big.Placed(new Placement { AlongMm = 30 }));
        Assert.False(rb.Fits);
        Assert.InRange(rb.WorstMm, 1.8, 2.2);
        Assert.True(rb.OutsideMm2 > 0.9 * 2 * Math.PI * 12 * 40);
        Assert.True(rb.StickOut.Count(v => v > 0) > rb.StickOut.Length / 2);

        // Hanging off the end.
        var rEnd = StockCarve.Compare(stock, small.Placed(new Placement { AlongMm = 50 }));
        Assert.True(rEnd.PastEnds);
        Assert.False(rEnd.Fits);
    }

    [Fact]
    public void Best_fit_moves_a_model_into_the_stock_keeping_its_size()
    {
        var stock = StockMap.FromMesh(Rod(10, 0, 60), stepDeg: 2, stepMm: 0.5);
        var model = new TargetModel(Rod(8, 0, 40));
        var start = new Placement { AlongMm = 12, UpMm = 3, SideMm = -2 };
        Assert.False(StockCarve.Compare(stock, model.Placed(start)).Fits);
        var fit = StockCarve.BestFit(stock, model, start, keepSize: true);
        Assert.Equal(1, fit.Scale);
        var r = StockCarve.Compare(stock, model.Placed(fit));
        Assert.True(r.Fits, $"worst {r.WorstMm}");
        Assert.InRange(r.WorstMm, -2.1, -1.5);
        Assert.Equal(30, fit.AlongMm, 0);
    }

    [Fact]
    public void Best_fit_finds_the_off_centre_room_in_lopsided_stock()
    {
        // The stock reaches far up and only a little the other ways; a round model fits only up there.
        var stock = StockMap.FromMesh(Box(-6, -3, 6, 17, 0, 50), stepDeg: 2, stepMm: 0.5);
        var model = new TargetModel(Rod(5, 0, 30));
        var start = new Placement { AlongMm = 25 };
        Assert.False(StockCarve.Compare(stock, model.Placed(start)).Fits);
        var fit = StockCarve.BestFit(stock, model, start);
        var r = StockCarve.Compare(stock, model.Placed(fit));
        Assert.True(r.Fits, $"worst {r.WorstMm}");
        Assert.InRange(fit.UpMm, 2.5, 11.5);
    }

    [Fact]
    public void Best_fit_turns_a_flat_model_to_lie_in_flat_stock()
    {
        // Flat stock lying sideways (wide toward ±Y), a flat model standing up (tall in Z): it must turn 90° round the axis.
        var stock = StockMap.FromMesh(Box(-15, -4, 15, 4, 0, 40), stepDeg: 2, stepMm: 0.5);
        var model = new TargetModel(Box(-3, -12, 3, 12, 0, 30));
        var start = new Placement { AlongMm = 20 };
        Assert.False(StockCarve.Compare(stock, model.Placed(start)).Fits);
        var fit = StockCarve.BestFit(stock, model, start);
        Assert.True(StockCarve.Compare(stock, model.Placed(fit)).Fits);
        double spin = ((fit.SpinDeg % 180) + 180) % 180;
        Assert.InRange(spin, 80, 100);
    }

    [Fact]
    public void Best_fit_can_make_the_model_as_big_as_fits()
    {
        var stock = StockMap.FromMesh(Rod(10, 0, 80), stepDeg: 2, stepMm: 0.5);
        var model = new TargetModel(Rod(4, 0, 20));
        var fit = StockCarve.BestFit(stock, model, new Placement { AlongMm = 40 }, keepSize: false, spareMm: 0.5);
        // 9.5 mm of radius to fill: about 2.4 times (a little less, as the stock's sides are flat between scan angles).
        Assert.InRange(fit.Scale, 2.2, 2.4);
        var r = StockCarve.Compare(stock, model.Placed(fit));
        Assert.True(r.Fits);
        Assert.InRange(r.WorstMm, -0.8, 0);
        // And shrinks one that's too big.
        var huge = new TargetModel(Rod(15, 0, 30));
        var shrunk = StockCarve.BestFit(stock, huge, new Placement { AlongMm = 40 }, keepSize: false);
        Assert.InRange(shrunk.Scale, 0.6, 0.67);
        Assert.True(StockCarve.Compare(stock, huge.Placed(shrunk)).Fits);
    }

    [Fact]
    public void The_carving_takes_round_stock_down_to_a_thinner_rod()
    {
        var stock = StockMap.FromMesh(Rod(10, 0, 60), stepDeg: 2, stepMm: 0.5);
        var model = new TargetModel(Rod(8.5, 0, 40));
        var placed = model.Placed(new Placement { AlongMm = 30 });
        var o = new CarveOptions { PassDepthMm = 0.25, FocusBandMm = 1, DetailMm = 0.5 };
        var plan = StockCarve.Plan(stock, placed, o);
        Assert.Equal(20, plan.DiameterMm);
        Assert.Equal(10, plan.StartX, 1);
        Assert.Equal(40, plan.LengthMm, 1);
        Assert.InRange(plan.DeepestMm, 1.3, 1.8);
        Assert.InRange(plan.Bands.Count, 1, 3);
        Assert.Equal(0, plan.TooDeepMm2, 3);
        Assert.InRange(plan.FocusRadiusMm, 9.8, 10.01);
        // Covers the whole way round, between the model's ends only.
        var all = plan.Bands.SelectMany(b => b.Passes).Select(g => g.Bounds()).ToList();
        Assert.InRange(all.Min(b => b.MinX), 9.5, 11);
        Assert.InRange(all.Max(b => b.MaxX), 49, 50.5);
        Assert.InRange(all.Max(b => b.MaxY) - all.Min(b => b.MinY), 0.95 * Math.PI * 20, Math.PI * 20 + 0.01);

        var page = StockCarve.Page(plan, Layer.Default(0, LayerMode.Fill), "Rod");
        Assert.Equal(plan.Bands.Count, page.Layers.Count);
        Assert.All(page.Shapes, s => Assert.True(s.AddonData.ContainsKey(StockCarve.NoteKey)));
        var state = Pages.Insert(DocState.New(), [page]);
        Assert.Equal(20, RotaryPage.MadeFor(state));
        Assert.Contains(page.Guides, g => g.RotaryStart);
    }

    [Fact]
    public void Past_the_model_s_ends_the_stock_is_left_unless_asked()
    {
        var stock = StockMap.FromMesh(Rod(10, 0, 60), stepDeg: 3, stepMm: 1);
        var placed = new TargetModel(Rod(8, 0, 20)).Placed(new Placement { AlongMm = 30 });
        var left = StockCarve.Plan(stock, placed, new CarveOptions { PassDepthMm = 0.5, DetailMm = 1 });
        Assert.Equal(20, left.LengthMm, 1);
        var all = StockCarve.Plan(stock, placed, new CarveOptions { PassDepthMm = 0.5, DetailMm = 1, PastEnds = true, CoreMm = 6, MaxDepthMm = 10 });
        Assert.Equal(60, all.LengthMm, 1);
        Assert.InRange(all.DeepestMm, 3.5, 4.5);
    }

    [Fact]
    public void Only_the_top_is_carved_when_only_the_top_stands_proud()
    {
        // Round stock; the model is the same rod with its top flattened at 8 mm up: only the top, round θ = 0, comes off,
        // and θ = 0 is the start line, half way down the page.
        var flatTop = Circle(10).Select(p => (p.Item1, Math.Min(p.Item2, 8.0))).Distinct().ToList();
        var stock = StockMap.FromMesh(Rod(10, 0, 40), stepDeg: 2, stepMm: 0.5);
        // (Its box runs from 10 down to 8 up, so its middle is 1 mm down: put that back.)
        var placed = new TargetModel(Prism(flatTop, 0, 40)).Placed(new Placement { AlongMm = 20, UpMm = -1 });
        Assert.InRange(StockCarve.Compare(stock, placed).WorstMm, -0.05, 0.01);
        var plan = StockCarve.Plan(stock, placed, new CarveOptions { PassDepthMm = 0.2, DetailMm = 0.5 });
        Assert.NotEmpty(plan.Bands);
        double circ = plan.CircumferenceMm;
        var b = plan.Bands.SelectMany(x => x.Passes).Select(g => g.Bounds()).ToList();
        double half = Math.Acos(0.8) / (2 * Math.PI) * circ;
        Assert.InRange(b.Min(r => r.MinY), circ / 2 - half - 1.5, circ / 2);
        Assert.InRange(b.Max(r => r.MaxY), circ / 2, circ / 2 + half + 1.5);
        Assert.InRange(plan.DeepestMm, 1.6, 2.2);
    }

    [Fact]
    public void A_square_bar_from_the_scan_window_is_read_as_its_exact_shape()
    {
        // A 2" square bar, flat face up, its middle on the axis (60 mm above the bed), as Scan the part saves it.
        var fit = new StockFit(new StockShape(StockKind.Square, 50.8), 60, new Vec2(0, 0), 0, 0, 100);
        var scan = fit.ToScan([20, 60, 100, 140], 110) with { AxisHeightMm = 60 };
        var stock = ScanLink.FromScan(scan, scan.AxisHeightMm!.Value);
        Assert.True(stock.WholeTurn);
        Assert.Equal(25.4, stock.RadiusAt(0, 80), 1);
        Assert.Equal(25.4, stock.RadiusAt(90, 80), 1);
        // The corners, between the scan's 2° steps (a hair under the sharp corner itself).
        Assert.InRange(stock.RadiusAt(45, 80), 35.2, 35.95);
        Assert.InRange(stock.RadiusAt(315, 30), 35.2, 35.95);
        // A 40 mm round rod fits; best fit leaves it in the middle with ~5 mm all round.
        var model = new TargetModel(Rod(20, 0, 60));
        var placed = StockCarve.BestFit(stock, model, new Placement { AlongMm = 80, UpMm = 2 });
        var r = StockCarve.Compare(stock, model.Placed(placed));
        Assert.True(r.Fits);
        Assert.InRange(r.WorstMm, -5.5, -5.0);
        Assert.Equal(80, placed.AlongMm, 0);
    }

    [Fact]
    public void The_simplex_search_finds_a_bowl_s_bottom()
    {
        var x = StockCarve.NelderMead(v => (v[0] - 3) * (v[0] - 3) + 2 * (v[1] + 1) * (v[1] + 1), [0, 0], [1, 1], 300);
        Assert.Equal(3, x[0], 2);
        Assert.Equal(-1, x[1], 2);
    }
}
