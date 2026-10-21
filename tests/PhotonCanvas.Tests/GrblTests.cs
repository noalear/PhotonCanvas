using PhotonCanvas.Core;
using Xunit;

namespace PhotonCanvas.Tests;

public class GrblTests
{
    static readonly MachineSetup M = MachineSetup.FullSpectrumHobby; // 508 × 305 mm, S1000, front-left origin

    static List<string> Lines(string g) => [.. g.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith(';'))];

    [Fact]
    public void ACutIsMovesInMillimetresFromTheFrontLeft()
    {
        var square = Polyline.Loop((10, 20), (30, 20), (30, 40), (10, 40));
        var job = new Job(M.Profile, [new VectorBlock(square, LaserSettings.Blue(60, 15).WithPasses(2))]);
        var g = Lines(GrblWriter.Write(job, M));
        Assert.Equal("G21 G90 G17", g[0]);
        Assert.Contains("M8", g); // air assist on
        // Design (10, 20) is 20 mm from the back of a 305 mm bed: Y 285 from the front.
        Assert.Equal(2, g.Count(l => l == "G0 X10 Y285"));
        // 15 mm/s = 900 mm/min, 60% of S1000 = S600, on the first cut move only.
        Assert.Contains("G1 X30 Y285 F900 S600", g);
        Assert.Contains("G1 X30 Y265", g);
        Assert.Contains("G1 X10 Y285", g); // closed back to the start
        Assert.Equal(["M5 S0", "M9", "G0 X0 Y0"], g[^3..]);
    }

    [Fact]
    public void TheOriginChoiceDecidesWhichWayYGoes()
    {
        var job = new Job(M.Profile, [new VectorBlock(Polyline.Open((5, 50), (6, 50)), LaserSettings.Blue(50, 10))]);
        Assert.Contains("G0 X5 Y-50", GrblWriter.Write(job, M with { Origin = GrblOrigin.BackLeftNegative }));
        Assert.Contains("G0 X5 Y50", GrblWriter.Write(job, M with { Origin = GrblOrigin.BackLeft }));
    }

    [Fact]
    public void PictureRowsAreRunsOfEqualPower()
    {
        // One row of 6 dots at 254 dpi (0.1 mm apart): on on off on on on, greyscale levels 255 255 - 0 0 255.
        var bmp = new Bitmap(6, 1);
        foreach (int c in new[] { 0, 1, 3, 4, 5 }) bmp[c, 0] = true;
        byte[] levels = [255, 255, 0, 0, 0, 255];
        var dots = new DotBlock(bmp, new Vec2(100, 100), LaserSettings.Blue(80, 200), 254, 200, 20, "grayscale") { Levels = levels };
        var g = Lines(GrblWriter.Write(new Job(M.Profile, [dots]), M with { RasterOverscanMm = 2, AirAssist = false }));
        // In from 2 mm before the row's first dot edge (99.95), at speed, laser off.
        Assert.Contains("G0 X97.95 Y205", g);
        Assert.Contains("G1 X99.95 Y205 F12000 S0", g);
        // Dots 0–1 at full (S800), dot 2 off, dots 3–4 at the lightest (20% = S200), dot 5 full; then out 2 mm.
        int i = g.IndexOf("G1 X99.95 Y205 F12000 S0");
        Assert.Equal(["G1 X100.15 Y205 S800", "G1 X100.25 Y205 S0", "G1 X100.45 Y205 S200", "G1 X100.55 Y205 S800", "G1 X102.55 Y205 S0"], g[(i + 1)..(i + 6)]);
    }

    [Fact]
    public void RoundsRunEveryBlockAPassAtATime()
    {
        var a = new VectorBlock(Polyline.Open((0, 0), (1, 0)), LaserSettings.Blue(50, 10).WithPasses(2));
        var b = new VectorBlock(Polyline.Open((0, 5), (1, 5)), LaserSettings.Blue(50, 10));
        var job = new Job(M.Profile, [a, b]) { PassMode = new PassMode.Rounds(3) };
        var starts = Lines(GrblWriter.Write(job, M)).Where(l => l.StartsWith("G0 X0")).ToList();
        Assert.Equal(["G0 X0 Y305", "G0 X0 Y300", "G0 X0 Y305"], starts.Take(3)); // a, b, then a again
        Assert.Contains("G4 P3 ; pause between passes", GrblWriter.Write(job, M));
    }

    [Fact]
    public void OnlyFlatBedJobs()
    {
        var job = new Job(M.Profile, [new VectorBlock(Polyline.Open((0, 0), (1, 0)), LaserSettings.Blue(50, 10))]) { Mode = new WorkMode.Rotary(1, 1, 100) };
        Assert.Throws<JobException>(() => GrblWriter.Write(job, M));
    }

    [Fact]
    public void FramingTracesTheBoxWithTheLaserOff()
    {
        var g = Lines(GrblWriter.Frame(new Rect2(10, 10, 50, 30), M));
        Assert.Contains("M5 S0", g);
        Assert.Contains("G0 X10 Y295", g);
        Assert.All(g.Where(l => l.StartsWith("G1")), l => Assert.DoesNotMatch(@"S[1-9]", l));
    }
}
