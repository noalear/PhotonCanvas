using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using Xunit;

namespace PhotonCanvas.Tests;

public class ExactDepthTests
{
    /// <summary>A material that slows as the hole deepens: 0.12 mm on the first pass, less after.</summary>
    static double DepthAfter(int passes) => 0.12 * passes - 0.0009 * passes * passes;

    static DepthRun NewRun(double target, double guess) => new()
    {
        LayerId = Guid.NewGuid(), TargetMm = target, GuessMmPerPass = guess,
        Inside = [new(10, 10), new(15, 10), new(20, 10)], Outside = [new(0, 10), new(30, 10), new(15, 0)],
    };

    /// <summary>Burn and measure until done (or 12 rounds): heights read with a little noise; the board can shift after a round.</summary>
    static DepthRun Run(DepthRun run, Func<int, double> shiftAfterRound, int seed = 1)
    {
        var rnd = new Random(seed);
        double surface = 3.0;
        double?[] Read(int passes, double shift) => [.. run.Points.Select((p, i) =>
            (double?)(surface + shift - (i < run.Inside.Count ? DepthAfter(passes) : 0) + (rnd.NextDouble() - 0.5) * 0.02))];
        Assert.Equal(DepthStep.MeasureSurface, run.Next);
        run = run.Measured(Read(0, 0));
        for (int round = 1; round <= 12 && run.Next == DepthStep.Burn; round++)
        {
            int n = run.NextPasses();
            Assert.True(n >= 1);
            run = run.Burning(n);
            Assert.Equal(DepthStep.MeasureDepth, run.Next);
            run = run.Measured(Read(run.PassesSoFar + n, shiftAfterRound(round)));
        }
        return run;
    }

    [Fact]
    public void ClosesInOnTheTargetWithoutGoingPast()
    {
        // The guess (from an etch test on the surface) is a bit optimistic: it slows down deeper.
        var run = Run(NewRun(2.0, 0.12), _ => 0);
        Assert.Equal(DepthStep.Finished, run.Next);
        Assert.InRange(run.DepthMm, 2.0 - 0.05, 2.0 + 0.12);
        Assert.InRange(run.Rounds.Count, 2, 7);
        // Each round measured, and the passes add up.
        Assert.Equal(run.Rounds.Sum(r => r.Passes), run.PassesSoFar);
        // The first round stops well short, in case the guess is wrong.
        Assert.InRange(run.History[0].DepthMm, 0.4, 1.0);
    }

    [Fact]
    public void ABadGuessIsCorrectedByTheFirstMeasurement()
    {
        // Guessed four times too deep a pass: the first round is short, then it catches up.
        var run = Run(NewRun(1.5, 0.4), _ => 0);
        Assert.Equal(DepthStep.Finished, run.Next);
        Assert.InRange(run.DepthMm, 1.45, 1.62);
        // Guessed far too shallow: the first round goes most of the way, never past.
        var slow = Run(NewRun(1.5, 0.05), _ => 0);
        Assert.Equal(DepthStep.Finished, slow.Next);
        Assert.InRange(slow.DepthMm, 1.45, 1.62);
    }

    [Fact]
    public void BoardShiftIsTakenOffByThePointsOutside()
    {
        // After round 1 everything reads 0.3 mm higher (the board was nudged up, or the head drifted).
        var run = Run(NewRun(2.0, 0.12), r => r >= 1 ? 0.3 : 0);
        Assert.InRange(run.DepthMm, 1.95, 2.12);
        // Every round's depth tracks the real one.
        foreach (var (passes, depth, _) in run.History) Assert.InRange(depth, DepthAfter(passes) - 0.03, DepthAfter(passes) + 0.03);
    }

    [Fact]
    public void PicksPointsWellInsideAndJustOutside()
    {
        var (inside, outside) = DepthRun.PickPoints(PathGeometry.Rect(10, 20, 30, 12), 3, 1.5);
        Assert.Equal(3, inside.Count);
        Assert.All(inside, p => Assert.True(p.X >= 11.5 && p.X <= 38.5 && p.Y >= 21.5 && p.Y <= 30.5));
        // Spread out: not all bunched in the middle.
        Assert.True(inside.Max(p => p.X) - inside.Min(p => p.X) > 15);
        Assert.Equal(3, outside.Count);
        Assert.All(outside, p => Assert.False(p.X > 10 && p.X < 40 && p.Y > 20 && p.Y < 32));
        // A ring: points in the band, not in the hole.
        var ring = new PathGeometry(PathGeometry.Rect(0, 0, 30, 30).Subpaths.Concat(PathGeometry.Rect(8, 8, 14, 14).Subpaths));
        var (onRing, _) = DepthRun.PickPoints(ring, 3, 1.5);
        Assert.All(onRing, p => Assert.False(p.X > 6.5 && p.X < 23.5 && p.Y > 6.5 && p.Y < 23.5));
    }

    [Fact]
    public void KeptBetweenRounds()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"pc-depth-{Guid.NewGuid():N}");
        try
        {
            var run = NewRun(1.0, 0.1).Measured([3, 3, 3, 3, 3, null]).Burning(5);
            run.Save(folder);
            var back = DepthRun.Load(folder, run.LayerId)!;
            Assert.Equal(DepthStep.MeasureDepth, back.Next);
            Assert.Equal(5, back.Rounds[0].Passes);
            Assert.Null(back.Before![5]);
            Assert.Equal(run.Inside, back.Inside);
            Assert.Null(DepthRun.Load(folder, Guid.NewGuid()));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
}
