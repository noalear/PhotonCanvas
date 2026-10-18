using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using Xunit;

namespace PhotonCanvas.Tests;

public class EtchTestTests
{
    [Fact]
    public void TheTestIsALadderOfCircles()
    {
        var o = new EtchTestOptions { FirstPasses = 2, StepPasses = 2, Circles = 10 };
        Assert.Equal([2, 4, 6, 8, 10, 12, 14, 16, 18, 20], o.Ladder);
        var (s, added) = EtchTest.Add(DocState.New(), o);
        var circles = s.Layers.Where(l => EtchTest.PassesOf(l) is not null).ToList();
        Assert.Equal(10, circles.Count);
        Assert.All(circles, l => Assert.Equal(EtchTest.PassesOf(l), l.Laser.Passes));
        Assert.Equal(20, added.Count); // ten circles and their numbers
        // Run round by round, the 20-pass circle gets a pass in every round, the 2-pass one in two.
        var job = JobBuilder.Build(s, new JobOptions { WholeJobPasses = true });
        Assert.Equal(20, job.Job.Rounds);
    }

    [Fact]
    public void TheRateComesFromTheBurnThrough()
    {
        // 3 mm board: through at 24 passes, not at 23.
        var r = EtchTest.FromBreakthrough(3, 24, 23);
        Assert.Equal(3 / 23.5, r.MmPerPass, 6);
        Assert.Equal(0.125, r.LowMm, 6);
        Assert.Equal(3 / 23.0, r.HighMm, 6);
        Assert.Equal(0.1, EtchTest.NoneThrough(3, 30).HighMm, 6);
    }

    [Fact]
    public void DepthReadingsShowTheSlowdown()
    {
        // 0.2 mm on the first pass, slowing as it goes: d = 0.21 n − 0.01 n².
        var readings = new[] { 1, 2, 4, 6, 8 }.Select(n => (n, 0.21 * n - 0.01 * n * n)).ToList();
        var c = EtchTest.FromDepths(readings);
        Assert.Equal(0.2, c.FirstPassMm, 6);
        Assert.Equal((0.21 * 8 - 0.64) / 8, c.AverageMm, 6);
        Assert.True(c.SlowdownPercent > 60);
        Assert.True(c.RmsMm < 1e-9);
        // Two readings: a straight line.
        Assert.Equal(0.15, EtchTest.FromDepths([(2, 0.3), (4, 0.6)]).FirstPassMm, 6);
    }
}
