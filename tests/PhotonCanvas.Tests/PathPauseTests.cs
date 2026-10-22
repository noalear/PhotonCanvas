using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using Xunit;

namespace PhotonCanvas.Tests;

public class PathPauseTests
{
    static PathShape Sq(Guid layer, double x, double y, double s = 10) =>
        new() { LayerId = layer, Geometry = PathGeometry.Rect(x, y, s, s) };

    static (DocState State, Layer Cut) ThreeSquares(int passes, int tabs = 0)
    {
        var s = DocState.New();
        var cut = s.Layers[0] with { Laser = LaserSettings.Blue(70, 10).WithPasses(passes), TabCount = tabs, TabPowerPct = tabs > 0 ? 20 : 0 };
        s = s with { Layers = [cut] };
        return (Ops.AddShapes(s, [Sq(cut.Id, 10, 10), Sq(cut.Id, 40, 10), Sq(cut.Id, 70, 10)]), cut);
    }

    static int Pauses(string gcode, double s) => gcode.Split('\n').Count(l => l == $"G4P{(long)(s * 1000)}");

    [Fact]
    public void RoundByRoundAPauseFollowsEveryPathEveryRound()
    {
        var (s, _) = ThreeSquares(passes: 2);
        var job = JobBuilder.Build(s, new JobOptions { WholeJobPasses = true, PathPauseS = 1.5 });
        Assert.Equal(3 * 2, Pauses(job.Job.Render(), 1.5));
        var without = JobBuilder.Build(s, new JobOptions { WholeJobPasses = true });
        Assert.Equal(without.EstimateS + 6 * 1.5, job.EstimateS, 1e-6);
        // The checks say what it costs.
        Assert.Contains(job.Warnings, w => w.StartsWith("Pausing 1.5 s after each of 3 paths adds about 9 s"));
    }

    [Fact]
    public void PathByPathThePassesAreWrittenOutWithAPauseAfterEach()
    {
        var (s, _) = ThreeSquares(passes: 3);
        var g = JobBuilder.Build(s, new JobOptions { WholeJobPasses = false, PathPauseS = 2 }).Job.Render();
        Assert.Equal(3 * 3, Pauses(g, 2));
        Assert.DoesNotContain("G104", g); // no device loop: it couldn't hold the pause
        // Without a pause the device's loop repeats each path as before.
        Assert.Contains("G104", JobBuilder.Build(s, new JobOptions { WholeJobPasses = false }).Job.Render());
    }

    [Fact]
    public void TabsAndPiecesShareTheirPathsOnePause()
    {
        var (s, _) = ThreeSquares(passes: 1, tabs: 2);
        var job = JobBuilder.Build(s, new JobOptions { WholeJobPasses = false, PathPauseS = 1 });
        Assert.True(job.Job.Blocks.Count > 3); // each square cut in pieces between its tabs
        Assert.Equal(3, job.Job.Blocks.Count(b => b.PauseAfterS > 0));
        Assert.Equal(3, Pauses(job.Job.Render(), 1));
    }

    [Fact]
    public void TooLongIsRefused()
    {
        var (s, _) = ThreeSquares(1);
        Assert.Contains("Pause after each path must be between 0 and 600", Assert.Throws<JobException>(() => JobBuilder.Build(s, new() { PathPauseS = Job.MaxPathPauseS + 1 })).Message);
    }

    [Fact]
    public void GrblMachinesWaitToo()
    {
        var square = Polyline.Loop((10, 20), (30, 20), (30, 40), (10, 40));
        var job = new Job(MachineSetup.FullSpectrumHobby.Profile, [new VectorBlock(square, LaserSettings.Blue(60, 15)) { PauseAfterS = 2.5 }]);
        Assert.Contains("G4 P2.5 ; pause after the path", GrblWriter.Write(job, MachineSetup.FullSpectrumHobby));
    }
}
