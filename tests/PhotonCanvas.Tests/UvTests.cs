using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using Xunit;

namespace PhotonCanvas.Tests;

/// <summary>The F2 Ultra UV (GS009): one 355 nm laser, marked with G22 and a frequency, no pulse width.</summary>
public class UvTests
{
    static MachineProfile Uv => MachineSetup.ForDeviceCode("GS009-CLASS-4")!.Profile;

    static List<string> Lines(string g) => [.. g.Split('\n').Select(l => l.Trim())];

    [Fact]
    public void TheUvHasOneLaserAndItsOwnRange()
    {
        var uv = Uv;
        Assert.NotNull(uv.Uv);
        Assert.False(uv.HasDiode);
        Assert.False(uv.HasMopa);
        Assert.Equal((40, 150), (uv.Uv!.MinKhz, uv.Uv.MaxKhz));
        Assert.Equal(("GS009-4", 200.0, 200.0), (uv.GcodeTag, uv.FieldW, uv.FieldH));
        Assert.Contains("355 nm", uv.Wavelengths);
        // The Class 1 version is the same laser and G-code.
        var c1 = MachineSetup.ForDeviceCode("GS009-CLASS-1")!.Profile;
        Assert.Equal(("GS009-4", 40), (c1.GcodeTag, c1.Uv!.MinKhz));

        var f2 = MachineProfile.F2Ultra;
        Assert.Null(f2.Uv);
        Assert.True(f2.HasDiode && f2.HasMopa);
        Assert.Equal("455 nm blue and 1064 nm infrared", f2.Wavelengths);
        // The fiber-only F2 Ultra has no diode; the F1 Lite no MOPA.
        Assert.False(MachineSetup.ForDeviceCode("GS007-CLASS-4")!.Profile.HasDiode);
        Assert.False(MachineSetup.ForDeviceCode("GS005")!.Profile.HasMopa);
    }

    [Fact]
    public void EveryLayerRunsOnTheUvLaser()
    {
        // A blue cut and a MOPA fill: both go to the UV laser, G22 with the layer's frequency and no pulse width.
        var cut = new VectorBlock(Polyline.Open((10, 10), (20, 10)), LaserSettings.Blue(50, 200) with { FrequencyKhz = 60 });
        var fill = new FillBlock([[new(30, 30), new(40, 30), new(40, 40), new(30, 40)]], LaserSettings.Red(30, 1000, 200, 120), 100);
        var job = new Job(Uv, [cut, fill]) { Created = "x" };
        Assert.All(job.Blocks, b => Assert.Equal(LightSource.Uv, b.Laser.Light));
        job.Validate();
        var lines = Lines(job.Render());
        Assert.Contains("# GS009-4 HEAD", lines);
        Assert.DoesNotContain("G21", lines);
        Assert.DoesNotContain(lines, l => l.StartsWith("G0H", StringComparison.Ordinal));
        int head = lines.IndexOf("# GS009-4 VECTOR HEAD");
        // As xTool Studio writes it for the UV: G4P1, G22, G90, G0Q<kHz>.
        Assert.Equal(["G4P1", "G22", "G90", "G0Q60"], lines.Skip(head + 2).Take(4));
        Assert.Contains("G0Q120", lines);
    }

    [Fact]
    public void FrequencyOutsideTheUvRangeIsRefused()
    {
        var tooHigh = LaserSettings.Red(30, 1000, 200, 300);
        var e = Assert.Throws<JobException>(() => tooHigh.Validate(Uv));
        Assert.Contains("UV frequency", e.Message);
        // A MOPA layer that suits the UV is fine there (its pulse width is dropped), though not on a machine without a known MOPA.
        LaserSettings.Red(30, 1000, 200, 80).Validate(Uv);
        // A UV setting is refused on a machine without one.
        Assert.Throws<JobException>(() => (LaserSettings.Blue(30, 100) with { Light = LightSource.Uv }).Validate(MachineProfile.F2Ultra));
    }

    [Fact]
    public void TheF2UltraIsUnchanged()
    {
        var cut = new VectorBlock(Polyline.Open((10, 10), (20, 10)), LaserSettings.Blue(50, 200));
        var job = new Job(MachineProfile.F2Ultra, [cut]) { Created = "x" };
        Assert.Same(cut, job.Blocks[0]);
        Assert.Contains("G21", Lines(job.Render()));
    }

    [Fact]
    public void ADesignBuildsForTheUv()
    {
        var s = DocState.New();
        var layer = new Layer { Name = "Mark", Mode = LayerMode.Score, Laser = LaserSettings.Blue(40, 500) with { FrequencyKhz = 40 } };
        s = Ops.AddLayer(s, layer);
        s = Ops.AddShapes(s, [new PathShape { LayerId = layer.Id, Geometry = PathGeometry.Rect(10, 10, 20, 20) }]);
        var built = JobBuilder.Build(s, new JobOptions { Machine = Uv, MopaOffset = new Vec2(1, 1) });
        Assert.Contains("G0Q40", built.Motion);
        Assert.DoesNotContain("\nG21", built.Motion);
        Assert.All(built.Job.Blocks, b => Assert.Equal(LightSource.Uv, b.Laser.Light));
        // A UV design can't go past its 200 mm field.
        var wide = Ops.AddShapes(s, [new PathShape { LayerId = layer.Id, Geometry = PathGeometry.Rect(150, 10, 60, 20) }]);
        Assert.ThrowsAny<JobException>(() => JobBuilder.Build(wide, new JobOptions { Machine = Uv }));
    }
}
