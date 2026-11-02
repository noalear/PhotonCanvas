using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Colour;
using Xunit;

namespace PhotonCanvas.Tests;

public class HeatSpreadTests
{
    static readonly SpreadHeat On = new() { On = true, Sheet = "Stainless steel", ThicknessMm = 0.5 };

    /// <summary>A grid of squares, each its own Fill layer, in reading order (row by row).</summary>
    static DocState Grid(int cols, int rows, double cell = 5, double gap = 3, double power = 30, int passes = 1)
    {
        var s = new DocState([], []);
        var shapes = new List<Shape>();
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                var layer = Layer.Default(s.Layers.Count, LayerMode.Fill) with
                {
                    Name = $"R{r + 1} C{c + 1}",
                    Laser = LaserSettings.Blue(power, 500).WithPasses(passes),
                    LinesPerCm = 100,
                    Crosshatch = false,
                };
                s = Ops.AddLayer(s, layer);
                shapes.Add(new PathShape { LayerId = layer.Id, Geometry = PathGeometry.Rect(20 + c * (cell + gap), 20 + r * (cell + gap), cell, cell) });
            }
        return Ops.AddShapes(s, shapes);
    }

    static List<Vec2> RunOrder(BuiltJob job) => [.. job.Steps.Select(st => Rect2.Bounds(st.Points).Center)];

    static double MeanStep(List<Vec2> centres) => Enumerable.Range(1, centres.Count - 1).Average(i => centres[i].Dist(centres[i - 1]));

    [Fact]
    public void DistantSquaresAreInterleaved()
    {
        var s = Grid(4, 4);
        var plain = JobBuilder.Build(s, new JobOptions());
        var spread = JobBuilder.Build(s, new JobOptions { Spread = On });
        Assert.Equal(plain.Job.Blocks.Count, spread.Job.Blocks.Count);
        // Every square still runs once.
        Assert.Equal(RunOrder(plain).OrderBy(p => p.X).ThenBy(p => p.Y), RunOrder(spread).OrderBy(p => p.X).ThenBy(p => p.Y));
        // Next to each other in reading order; far apart once spread.
        Assert.True(MeanStep(RunOrder(spread)) > 1.5 * MeanStep(RunOrder(plain)), $"{MeanStep(RunOrder(spread)):0.#} vs {MeanStep(RunOrder(plain)):0.#}");
        var order = RunOrder(spread);
        Assert.True(order[1].Dist(order[0]) > 15); // the second square is not a neighbour of the first
        Assert.Contains(spread.Warnings, w => w.StartsWith("Spread the heat:") && w.Contains("of 16 parts reordered"));
        // The steps follow the blocks: the timeline's spans point at the steps each block marks, in run order.
        var spans = spread.Timeline.Where(t => !t.IsPause).ToList();
        Assert.Equal(Enumerable.Range(0, 16), spans.Select(t => t.FirstStep));
    }

    [Fact]
    public void SeveralSwatchesOnOneLayerInterleaveToo()
    {
        var s = new DocState([], []);
        var layer = Layer.Default(0, LayerMode.Fill) with { Laser = LaserSettings.Blue(30, 500), LinesPerCm = 100, Crosshatch = false };
        s = Ops.AddLayer(s, layer);
        s = Ops.AddShapes(s, [.. Enumerable.Range(0, 6).Select(i => (Shape)new PathShape { LayerId = layer.Id, Geometry = PathGeometry.Rect(20 + i * 8, 20, 5, 5) })]);
        var plain = JobBuilder.Build(s, new JobOptions());
        // Warming allowed freely: no waits, so the times compare.
        var spread = JobBuilder.Build(s, new JobOptions { Spread = On with { LimitC = 500 } });
        Assert.True(MeanStep(RunOrder(spread)) > MeanStep(RunOrder(plain)));
        // One layer, run in several stretches: the times add up to the same.
        Assert.Equal(plain.LayerTimes.Sum(t => t.TotalSeconds), spread.LayerTimes.Sum(t => t.TotalSeconds), 1e-6);
        Assert.All(spread.LayerTimes, t => Assert.Equal(layer.Id, t.LayerId));
    }

    [Fact]
    public void OverlappingPartsKeepTheirOrder()
    {
        // A fill and, on a later layer, its outline on the same spot, among distant squares.
        var s = Grid(3, 3, gap: 10);
        var first = s.Shapes[0];
        var outline = Layer.Default(s.Layers.Count, LayerMode.Score) with { Name = "Outline", Laser = LaserSettings.Blue(20, 500) };
        s = Ops.AddLayer(s, outline);
        s = Ops.AddShapes(s, [new PathShape { LayerId = outline.Id, Geometry = PathGeometry.Rect(first.Bounds.MinX, first.Bounds.MinY, 5, 5) }]);
        // And one more on top of the middle square, on its own layer before the outline's.
        var job = JobBuilder.Build(s, new JobOptions { Spread = On });
        int fill = job.Steps.ToList().FindIndex(st => st.LayerId == first.LayerId);
        int score = job.Steps.ToList().FindIndex(st => st.LayerId == outline.Id);
        Assert.True(fill < score);
        int fillBlock = job.Job.Blocks.ToList().FindIndex(b => b is FillBlock f && Rect2.Bounds(f.Polygons[0]).Center.Dist(first.Bounds.Center) < 0.1);
        int scoreBlock = job.Job.Blocks.ToList().FindIndex(b => b is VectorBlock);
        Assert.True(fillBlock >= 0 && fillBlock < scoreBlock);
    }

    [Fact]
    public void CutsStayAfterTheEngraving()
    {
        var s = Grid(3, 1, gap: 20);
        var cut = Layer.Default(s.Layers.Count, LayerMode.Cut) with { Name = "Cut out", Laser = LaserSettings.Blue(80, 10) };
        s = Ops.AddLayer(s, cut);
        s = Ops.AddShapes(s, [new PathShape { LayerId = cut.Id, Geometry = PathGeometry.Rect(150, 150, 10, 10) }]);
        var job = JobBuilder.Build(s, new JobOptions { Spread = On });
        Assert.IsType<VectorBlock>(job.Job.Blocks[^1]);
        Assert.Equal(cut.Id, job.LayerTimes[^1].LayerId);
    }

    [Fact]
    public void WaitsAppearWhenTheLimitIsLow()
    {
        var s = Grid(3, 3, gap: 1, power: 60);
        var easy = JobBuilder.Build(s, new JobOptions { Spread = On with { LimitC = 500 } });
        Assert.DoesNotContain(easy.Job.Blocks, b => b.PauseAfterS > 0);
        var tight = JobBuilder.Build(s, new JobOptions { Spread = On with { LimitC = 1 } });
        Assert.Contains(tight.Job.Blocks, b => b.PauseAfterS > 0);
        Assert.Contains("G4P", tight.Job.Render());
        Assert.True(tight.EstimateS > easy.EstimateS);
        Assert.Contains(tight.Warnings, w => w.Contains("of waits added"));
        // Ordering alone can't keep the plate under 1 °C: it says so, and suggests a heat sink.
        Assert.Contains(tight.Warnings, w => w.Contains("heat sink"));
    }

    [Fact]
    public void AHeatSinkNeedsFewerWaits()
    {
        var s = Grid(3, 3, gap: 1, power: 60);
        double Waits(SheetBacking b) => JobBuilder.Build(s, new JobOptions { Spread = On with { LimitC = 3, Backing = b } }).Job.Blocks.Sum(x => x.PauseAfterS);
        Assert.True(Waits(SheetBacking.ClampedToAluminium) < Waits(SheetBacking.InAir));
    }

    [Fact]
    public void TheCooldownIsAMinimumAfterEachPart()
    {
        var s = Grid(2, 2, gap: 20);
        var job = JobBuilder.Build(s, new JobOptions { Spread = On with { LimitC = 500, CooldownS = 4 } });
        Assert.Equal(3, job.Job.Blocks.Count(b => b.PauseAfterS >= 4));
        Assert.Equal(0, job.Job.Blocks[^1].PauseAfterS);
        Assert.Throws<JobException>(() => JobBuilder.Build(s, new JobOptions { Spread = On with { CooldownS = SpreadHeat.MaxCooldownS + 1 } }));
    }

    [Fact]
    public void WorksRoundByRoundAndPathByPath()
    {
        var s = Grid(3, 2, gap: 10, passes: 2);
        foreach (bool rounds in new[] { true, false })
        {
            var job = JobBuilder.Build(s, new JobOptions { WholeJobPasses = rounds, Spread = On with { CooldownS = 1 } });
            Assert.Equal(6, job.Job.Blocks.Count);
            Assert.Equal(job.Job.EstimateSeconds(), job.EstimateS, 1e-6);
            Assert.Contains("G4P1000", job.Job.Render());
        }
    }

    [Fact]
    public void OffIsTheSameJobAsBefore()
    {
        var s = Grid(3, 3);
        // One time for every build: the job's header has its date to the second, which would
        // differ when two builds straddle a second.
        var now = new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);
        string Render(DocState d, JobOptions o) => JobBuilder.Build(d, o, now).Job.Render();
        string plain = Render(s, new JobOptions());
        // Settings changed but off: nothing changes.
        Assert.Equal(plain, Render(s with { Spread = On with { On = false, LimitC = 2, CooldownS = 9 } }, new JobOptions()));
        Assert.Equal(plain, Render(s with { Spread = On }, new JobOptions { Spread = new SpreadHeat() }));
        // On in the design does change it.
        Assert.NotEqual(plain, Render(s with { Spread = On }, new JobOptions()));
    }

    [Fact]
    public void SectionsAndTheMopaOffsetStillApply()
    {
        var s = Grid(4, 1, gap: 30);
        s = s with { Layers = [.. s.Layers.Select(l => l with { Laser = LaserSettings.Red(30, 500, 6, 500) })] };
        var sec = Section.Plan(new Rect2(0, 0, 200, 50), 80, 50, MachineProfile.F2Ultra)[0];
        var job = JobBuilder.Build(s, new JobOptions { Spread = On, Section = sec, MopaOffset = new Vec2(0.5, 0) });
        Assert.Equal(job.Job.Blocks.Count, job.Timeline.Count(t => !t.IsPause));
        Assert.All(job.Timeline.Where(t => !t.IsPause), t => Assert.InRange(t.FirstStep, 0, job.Steps.Count - 1));
    }

    [Fact]
    public void SavedWithTheDesign()
    {
        var s = Grid(2, 1) with { Spread = On with { LimitC = 18, Backing = SheetBacking.TapedToMetal, CooldownS = 2.5, Sheet = "Titanium", ThicknessMm = 0.3 } };
        var back = DocFile.Deserialize(DocFile.Serialize(s));
        Assert.Equal(s.Spread, back.Spread);
        // Off and untouched: not written at all.
        Assert.DoesNotContain("spreadHeat", DocFile.Serialize(Grid(2, 1)));
        Assert.Equal(new SpreadHeat(), DocFile.Deserialize(DocFile.Serialize(Grid(2, 1))).Spread);
    }

    [Fact]
    public void TheColourTestTurnsItOn()
    {
        var (s, _) = ColourTest.Add(DocState.New(), new ColourTestOptions { AcrossSteps = 3, DownSteps = 2, Spread = On });
        Assert.True(s.Spread.On);
        var (off, _) = ColourTest.Add(DocState.New(), new ColourTestOptions { AcrossSteps = 3, DownSteps = 2 });
        Assert.False(off.Spread.On);
    }

    [Fact]
    public void TheSheetWithoutBackingIsTheHeatOrdersSheet()
    {
        // The heat order's own sheet is unchanged: the same plan with the loss given as still air.
        var h = new HeatSettings { Sheet = SheetMetal.Find("Stainless steel"), ThicknessMm = 0.3 };
        var box = new Rect2(0, 0, 30, 30);
        var a = new HeatOrder.Sheet(box, 1, h);
        var b = new HeatOrder.Sheet(box, 1, h, new SpreadHeat().LossWmm2K);
        var d = a.Deposit([new(10, 10), new(20, 10)], 5);
        a.Add(d); b.Add(d);
        a.Advance(3); b.Advance(3);
        Assert.Equal(a.At(new(15, 10)), b.At(new(15, 10)));
        // Clamped to aluminium, it cools much faster.
        var c = new HeatOrder.Sheet(box, 1, h, (new SpreadHeat() with { Backing = SheetBacking.ClampedToAluminium }).LossWmm2K);
        c.Add(d); c.Advance(3);
        Assert.True(c.At(new(15, 10)) < 0.5 * a.At(new(15, 10)));
    }
}
