using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using Xunit;

namespace PhotonCanvas.Tests;

public class HeatPlanTests
{
    static FlatPath Square(double x, double y, double s) => new([new(x, y), new(x + s, y), new(x + s, y + s), new(x, y + s)], true);

    static List<(FlatPath, IReadOnlyList<FlatPath>)> Whole(IEnumerable<FlatPath> paths) => [.. paths.Select(p => (p, (IReadOnlyList<FlatPath>)[p]))];

    static HeatSettings Thin(int passes = 1, double limit = 30, double piece = 15) => new()
    {
        Sheet = SheetMetal.Find("Aluminium"), ThicknessMm = 0.1, LimitC = limit, PieceMm = piece,
        Watts = 18, SpeedMmS = 400, Passes = passes,
    };

    static double Length(List<Vec2> p) => Enumerable.Range(1, p.Count - 1).Sum(i => p[i - 1].Dist(p[i]));

    // A 5 × 5 grid of small squares, 12 mm apart.
    static List<FlatPath> Grid() => [.. Enumerable.Range(0, 25).Select(i => Square(20 + i % 5 * 12, 20 + i / 5 * 12, 3))];

    [Fact]
    public void EveryPassOfEveryPathOnceAndInTurn()
    {
        var plan = HeatOrder.Plan(Whole(Grid()), Thin(passes: 3));
        Assert.Equal(25 * 3, plan.Cuts.Count);
        foreach (var g in plan.Cuts.GroupBy(c => c.Path))
            Assert.Equal([1, 2, 3], g.Select(c => c.Pass));
    }

    [Fact]
    public void NextCutsGoFarFromTheLast()
    {
        var plan = HeatOrder.Plan(Whole(Grid()), Thin());
        static Vec2 Centre(HeatCut c) => Rect2.Bounds(c.Points).Center;
        double mean = Enumerable.Range(1, plan.Cuts.Count - 1).Average(i => Centre(plan.Cuts[i - 1]).Dist(Centre(plan.Cuts[i])));
        // Least travel would hop to a neighbour 12 mm away each time; spreading the heat jumps across the sheet.
        Assert.True(mean > 25, $"mean jump {mean:0.#} mm");
    }

    [Fact]
    public void InsidesAreFinishedBeforeTheLastPassOfTheirOutline()
    {
        var outline = Square(10, 10, 80);
        var plan = HeatOrder.Plan(Whole([outline, .. Grid()]), Thin(passes: 2, piece: 0));
        int lastInside = plan.Cuts.ToList().FindLastIndex(c => c.Path > 0);
        int outlineLast = plan.Cuts.ToList().FindIndex(c => c.Path == 0 && c.Pass == 2);
        Assert.True(outlineLast > lastInside);
    }

    [Fact]
    public void LongPathsAreCutInPiecesThatCoverThemWhole()
    {
        var bar = new FlatPath([new(10, 50), new(110, 50)], false);
        var plan = HeatOrder.Plan(Whole([bar]), Thin(piece: 15));
        Assert.Equal(7, plan.Cuts.Count); // 100 mm in pieces of about 15
        Assert.All(plan.Cuts, c => Assert.InRange(Length(c.Points), 14, 14.4));
        double covered = plan.Cuts.Sum(c => Length(c.Points));
        Assert.InRange(covered, 100, 100.4); // a little overlap where pieces meet
        // Not one after another along the line: the next piece starts away from where the last one ended.
        Assert.Contains(Enumerable.Range(1, plan.Cuts.Count - 1), i => plan.Cuts[i].Points[0].Dist(plan.Cuts[i - 1].Points[^1]) > 20);
    }

    [Fact]
    public void ATighterLimitWaitsLonger()
    {
        // Many passes on few paths close together: the heat builds up.
        var paths = Enumerable.Range(0, 4).Select(i => Square(40 + i % 2 * 6, 40 + i / 2 * 6, 3)).ToList();
        var loose = HeatOrder.Plan(Whole(paths), Thin(passes: 20, limit: 200));
        var tight = HeatOrder.Plan(Whole(paths), Thin(passes: 20, limit: 20));
        Assert.Equal(0, loose.WaitS);
        Assert.True(tight.WaitS > 1, $"waited {tight.WaitS:0.##} s");
        Assert.True(tight.PeakC < loose.PeakC);
        Assert.Contains(tight.Cuts, c => c.WaitBeforeS > 0);
    }

    [Fact]
    public void HeatSpreadsSlowerInStainlessSoItWarmsMoreLocally()
    {
        var paths = Enumerable.Range(0, 4).Select(i => Square(40 + i % 2 * 6, 40 + i / 2 * 6, 3)).ToList();
        var al = HeatOrder.Plan(Whole(paths), Thin(passes: 10, limit: 1000));
        var ss = HeatOrder.Plan(Whole(paths), Thin(passes: 10, limit: 1000) with { Sheet = SheetMetal.Find("Stainless steel") });
        Assert.True(ss.PeakC > al.PeakC, $"stainless {ss.PeakC:0.#}, aluminium {al.PeakC:0.#}");
    }

    [Fact]
    public void TheJobRunsTheCutsInThatOrderOnePassEach()
    {
        var s = DocState.New();
        var cut = s.Layers[0] with
        {
            Laser = LaserSettings.Red(31, 400, 45, 132).WithPasses(3), Order = PathOrder.Heat,
            HeatSheet = "Aluminium", HeatThicknessMm = 0.1, HeatLimitC = 30, HeatPieceMm = 15,
        };
        s = s with { Layers = [cut] };
        s = Ops.AddShapes(s, [.. Grid().Select(f => new PathShape { LayerId = cut.Id, Geometry = PathGeometry.Polyline(f.Points, true) })]);
        var job = JobBuilder.Build(s, new JobOptions { WholeJobPasses = true });
        Assert.Equal(25 * 3, job.Job.Blocks.Count);
        Assert.All(job.Job.Blocks, b => Assert.Equal(1, b.Laser.Passes));
        Assert.Equal(1, job.LayerTimes.Single().Passes); // passes already written out
        Assert.Contains(job.Warnings, w => w.Contains("spreads the heat") && w.Contains("°C (estimated)"));
        // Saved with the layer.
        var back = DocFile.Deserialize(DocFile.Serialize(s)).Layers[0];
        Assert.Equal((PathOrder.Heat, "Aluminium", 0.1, 30.0, 15.0), (back.Order, back.HeatSheet, back.HeatThicknessMm, back.HeatLimitC, back.HeatPieceMm));
    }
}
