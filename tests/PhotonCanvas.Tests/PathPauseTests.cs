using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using Xunit;

namespace PhotonCanvas.Tests;

public class PathPauseTests
{
    static PathShape Sq(Guid layer, double x, double y, double s = 10) =>
        new() { LayerId = layer, Geometry = PathGeometry.Rect(x, y, s, s) };

    static (DocState State, Layer Cut) ThreeSquares(int passes, double pause, int tabs = 0)
    {
        var s = DocState.New();
        var cut = s.Layers[0] with { Laser = LaserSettings.Blue(70, 10).WithPasses(passes), PathPauseS = pause, TabCount = tabs, TabPowerPct = tabs > 0 ? 20 : 0 };
        s = s with { Layers = [cut] };
        return (Ops.AddShapes(s, [Sq(cut.Id, 10, 10), Sq(cut.Id, 40, 10), Sq(cut.Id, 70, 10)]), cut);
    }

    static int Pauses(string gcode, double s) => gcode.Split('\n').Count(l => l == $"G4P{(long)(s * 1000)}");

    [Fact]
    public void RoundByRoundAPauseFollowsEveryPathEveryRound()
    {
        var (s, _) = ThreeSquares(passes: 2, pause: 1.5);
        var job = JobBuilder.Build(s, new JobOptions { WholeJobPasses = true });
        Assert.Equal(3 * 2, Pauses(job.Job.Render(), 1.5));
        var without = JobBuilder.Build(ThreeSquares(2, 0).State, new JobOptions { WholeJobPasses = true });
        Assert.Equal(without.EstimateS + 6 * 1.5, job.EstimateS, 1e-6);
    }

    [Fact]
    public void PathByPathThePassesAreWrittenOutWithAPauseAfterEach()
    {
        var (s, _) = ThreeSquares(passes: 3, pause: 2);
        var g = JobBuilder.Build(s, new JobOptions { WholeJobPasses = false }).Job.Render();
        Assert.Equal(3 * 3, Pauses(g, 2));
        Assert.DoesNotContain("G104", g); // no device loop: it couldn't hold the pause
        // Without a pause the device's loop repeats each path as before.
        Assert.Contains("G104", JobBuilder.Build(ThreeSquares(3, 0).State, new JobOptions { WholeJobPasses = false }).Job.Render());
    }

    [Fact]
    public void TabsAndPiecesShareTheirPathsOnePause()
    {
        var (s, _) = ThreeSquares(passes: 1, pause: 1, tabs: 2);
        var job = JobBuilder.Build(s, new JobOptions { WholeJobPasses = false });
        Assert.True(job.Job.Blocks.Count > 3); // each square cut in pieces between its tabs
        Assert.Equal(3, job.Job.Blocks.Count(b => b.PauseAfterS > 0));
        Assert.Equal(3, Pauses(job.Job.Render(), 1));
    }

    [Fact]
    public void TheSettingIsSavedAndTooLongIsRefused()
    {
        var (s, cut) = ThreeSquares(1, 0.75);
        Assert.Equal(0.75, DocFile.Deserialize(DocFile.Serialize(s)).Layers[0].PathPauseS);
        var tooLong = s with { Layers = [cut with { PathPauseS = Job.MaxPathPauseS + 1 }] };
        Assert.Contains("pause after each path", Assert.Throws<JobException>(() => JobBuilder.Build(tooLong, new())).Message);
    }

    [Fact]
    public void GrblMachinesWaitToo()
    {
        var square = Polyline.Loop((10, 20), (30, 20), (30, 40), (10, 40));
        var job = new Job(MachineSetup.FullSpectrumHobby.Profile, [new VectorBlock(square, LaserSettings.Blue(60, 15)) { PauseAfterS = 2.5 }]);
        Assert.Contains("G4 P2.5 ; pause after the path", GrblWriter.Write(job, MachineSetup.FullSpectrumHobby));
    }
}
