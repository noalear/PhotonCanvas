using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using Xunit;

namespace PhotonCanvas.Tests;

public class GrapheneTestTests
{
    [Fact]
    public void AGridOfStripsEachWithItsOwnSettings()
    {
        var o = new GrapheneTestOptions { Powers = [30, 60], Speeds = [50, 100, 200], DefocusMm = 1.5 };
        var (s, cells) = GrapheneTest.Add(DocState.New(), o);
        Assert.Equal(6, cells.Count);
        var layers = GrapheneTest.Cells(s);
        Assert.Equal([30.0, 60, 30, 60, 30, 60], layers.Select(l => l.Laser.Power));
        Assert.Equal([50.0, 50, 100, 100, 200, 200], layers.Select(l => l.Laser.Speed));
        Assert.All(layers, l => Assert.Equal(-1.5, l.FocusOffsetMm)); // focus raised: a softer spot
        // Each strip is a dumbbell: two 5 mm pads and the 15 mm strip, 25 mm long.
        var strip = s.Shapes.Single(x => x.LayerId == layers[0].Id);
        Assert.Equal(25, strip.Bounds.Width, 3);
        Assert.Equal(5, strip.Bounds.Height, 3);
        // It builds into a job (the labels light, the strips filled).
        Assert.NotEmpty(JobBuilder.Build(s, new JobOptions()).Job.Blocks);
    }

    [Fact]
    public void TheLowestReadingWins()
    {
        var o = new GrapheneTestOptions { StripMm = 15, StripWidthMm = 3 };
        var best = GrapheneTest.Best([null, 4500, 900, 12000], o)!.Value;
        Assert.Equal(2, best.Index);
        Assert.Equal(900, best.Ohms);
        Assert.Equal(180, best.OhmsPerSquare, 6); // 900 Ω × 3 mm ÷ 15 mm
        Assert.Null(GrapheneTest.Best([null, null], o));
    }
}
