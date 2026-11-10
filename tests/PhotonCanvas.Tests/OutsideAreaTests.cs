using PhotonCanvas.Addon.RotaryFinish;
using PhotonCanvas.Addon.StockCarve;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using static PhotonCanvas.Tests.StockCarveTests;

namespace PhotonCanvas.Tests;

/// <summary>
/// Kyle, 2026-11-09: a Carve to model page made for Object Ø 36 mm, with Work mode set to 32.7 mm, stopped with
/// "Point (169.944, 113.105) mm is outside the work area" and nothing to say which of 141 shapes on 12 layers it was.
/// </summary>
public class OutsideAreaTests
{
    static readonly CarveOptions Carve = new() { PassDepthMm = 0.5, DetailMm = 1 };

    static (StockMap Stock, IReadOnlyList<PhotonCanvas.Doc.Import.StlDepth.Triangle> Placed) Part()
    {
        var stock = StockMap.FromMesh(Rod(18, 0, 60), stepDeg: 3, stepMm: 1);
        var placed = new TargetModel(Rod(15, 0, 40)).Placed(new Placement { AlongMm = 30 });
        return (stock, placed);
    }

    static JobOptions Rotary(double dia) => new() { Mode = new WorkMode.Rotary(128 / (Math.PI * dia), 1, Math.PI * dia) };

    [Fact]
    public void A_carve_page_made_for_another_object_size_names_the_shapes_that_run_past_once_round()
    {
        var (stock, placed) = Part();
        var old = StockCarve.Plan(stock, placed, Carve);
        Assert.Equal(36, old.DiameterMm);
        var state = Pages.Insert(DocState.New(), [StockCarve.Page(old, Layer.Default(0, LayerMode.Fill), "Rod")]);
        var e = Assert.Throws<OutsideWorkAreaException>(() => JobBuilder.Build(state, Rotary(32.7)));
        Assert.StartsWith("\"Pass 1 at ", e.Message);
        Assert.Contains("on layer \"Rotary · focus", e.Message);
        Assert.Contains("past once round the object", e.Message);
        Assert.Contains("Object Ø 32.7 mm", e.Message);
        Assert.NotEmpty(e.ShapeIds);
        Assert.All(e.ShapeIds, id => Assert.NotNull(state.FindShape(id)));
        // Passes the whole way round are the same size and place on purpose: not called old copies.
        Assert.DoesNotContain(e.Warnings, w => w.Contains("same size and in the same place"));
    }

    [Fact]
    public void Carve_to_model_makes_the_page_for_the_object_size_work_mode_is_set_to()
    {
        var (stock, placed) = Part();
        var plan = StockCarve.Plan(stock, placed, Carve, objectDiameterMm: 32.7);
        Assert.Equal(32.7, plan.DiameterMm, 6);
        var all = plan.Bands.SelectMany(b => b.Passes).Select(g => g.Bounds()).ToList();
        Assert.True(all.Max(b => b.MaxY) <= Math.PI * 32.7 + 1e-6);
        var state = Pages.Insert(DocState.New(), [StockCarve.Page(plan, Layer.Default(0, LayerMode.Fill), "Rod")]);
        Assert.Equal(32.7, RotaryPage.MadeFor(state));
        var job = JobBuilder.Build(state, Rotary(32.7));
        Assert.True(job.Paths > 0);
        Assert.Contains("as Machine → Work mode is set now", StockCarve.Describe(plan, Carve, 32.7));
        Assert.Contains("Set Machine → Work mode → Rotary to Object Ø 36 mm", StockCarve.Describe(StockCarve.Plan(stock, placed, Carve), Carve, 32.7));
        // Far off the stock's size (the lines would land far apart), or not on the rotary: its own size.
        Assert.Equal(36, StockCarve.Plan(stock, placed, Carve, objectDiameterMm: 10).DiameterMm);
        Assert.Equal(36, StockCarve.Plan(stock, placed, Carve, objectDiameterMm: 100).DiameterMm);
        Assert.Equal(36, StockCarve.Plan(stock, placed, Carve, objectDiameterMm: null).DiameterMm);
    }

    [Fact]
    public void On_the_bed_the_check_names_the_shape_and_its_layer_and_keeps_the_other_checks()
    {
        var s = DocState.New();
        var layer = Layer.Default(1, LayerMode.Cut) with { Name = "Outlines" };
        var hidden = Layer.Default(2, LayerMode.Cut) with { Name = "Old", Visible = false };
        s = Ops.AddLayer(Ops.AddLayer(s, layer), hidden);
        var inside = new PathShape { Name = "Inside", LayerId = layer.Id, Geometry = PathGeometry.Rect(20, 20, 30, 30) };
        var box = new PathShape { Name = "Box", LayerId = layer.Id, Geometry = PathGeometry.Rect(200, 20, 40, 30) };
        s = Ops.AddShapes(s, [inside, box, new PathShape { LayerId = hidden.Id, Geometry = PathGeometry.Rect(20, 20, 5, 5) }]);
        var e = Assert.Throws<OutsideWorkAreaException>(() => JobBuilder.Build(s, new JobOptions()));
        Assert.StartsWith("\"Box\" on layer \"Outlines\" goes outside the work area", e.Message);
        Assert.Contains("X 200 to 240", e.Message);
        Assert.Equal([box.Id], e.ShapeIds);
        Assert.Contains(e.Warnings, w => w.Contains("\"Old\" is hidden"));
    }

    [Fact]
    public void A_fill_whose_run_up_goes_past_the_edge_names_the_shape_nearest()
    {
        var s = DocState.New();
        var layer = Layer.Default(1, LayerMode.Fill) with { Name = "Fills", OverscanMm = 5 };
        s = Ops.AddLayer(s, layer);
        var edge = new PathShape { Name = "Edge", LayerId = layer.Id, Geometry = PathGeometry.Rect(1, 20, 30, 30) };
        s = Ops.AddShapes(s, [edge, new PathShape { Name = "Middle", LayerId = layer.Id, Geometry = PathGeometry.Rect(100, 100, 10, 10) }]);
        var e = Assert.Throws<OutsideWorkAreaException>(() => JobBuilder.Build(s, new JobOptions()));
        Assert.StartsWith("\"Edge\" on layer \"Fills\"", e.Message);
        Assert.Contains("run-up", e.Message);
        Assert.Equal([edge.Id], e.ShapeIds);
    }
}

/// <summary>Kyle, 2026-11-10: a rotary carve estimated at 38.6 min ran 54.5: the part turning between lines wasn't counted.</summary>
public class RotaryEstimateTests
{
    static DocState Square(LayerMode mode)
    {
        var s = DocState.New();
        var layer = Layer.Default(1, mode) with { LinesPerCm = 100, FillAngleDeg = 0 };
        s = Ops.AddLayer(s, layer);
        return Ops.AddShapes(s, [new PathShape { LayerId = layer.Id, Geometry = PathGeometry.Rect(100, 40, 40, 20) }]);
    }

    [Fact]
    public void On_the_rotary_each_line_adds_the_turn_to_the_next()
    {
        var s = Square(LayerMode.Fill);
        double circ = Math.PI * 32.6;
        var rotary = new JobOptions { Mode = new WorkMode.Rotary(32 / circ, 1, circ), Machine = MachineProfile.F2Ultra with { FieldH = Math.Round(circ, 2) } };
        var flat = JobBuilder.Build(s, new JobOptions());
        var round = JobBuilder.Build(s, rotary);
        var fill = Assert.IsType<FillBlock>(Assert.Single(round.Job.Blocks));
        int lines = Job.FillLines(fill).Count;
        Assert.InRange(lines, 190, 210);
        // About 32 ms a line more (and the 20 mm round at the layer's speed, not the galvo's jump).
        Assert.InRange(round.EstimateS - flat.EstimateS, (lines - 1) * Job.RotaryStepS, (lines - 1) * Job.RotaryStepS + 1);
        // The layer times and the run's timeline add up to it, so progress follows.
        Assert.Equal(round.EstimateS, Job.StartS + round.LayerTimes.Sum(t => t.TotalSeconds), 6);
        Assert.Equal(round.EstimateS, round.Timeline[^1].End, 6);
    }
}
