using System.Text;
using System.Text.Json.Nodes;
using PhotonCanvas.Core;

namespace PhotonCanvas.Tests;

public class NumFmtTests
{
    [Fact]
    public void Formats()
    {
        Assert.Equal("100", NumFmt.Trim3(100.0));
        Assert.Equal("154.101", NumFmt.Trim3(154.1010));
        Assert.Equal("0", NumFmt.Trim3(-0.0001));
        Assert.Equal("0.5", NumFmt.Trim3(0.5));
        Assert.Equal("100.000", NumFmt.Fixed3(100.0));
        Assert.Equal("0.000", NumFmt.Fixed3(-0.0001));
        Assert.Equal("10", NumFmt.JsNumber(10.0));
        Assert.Equal("0.7", NumFmt.JsNumber(0.7));
        Assert.Equal("7.0866", NumFmt.JsRounded(7.086614173228346, 4));
        Assert.Equal("0.064", NumFmt.JsRounded(0.064, 6));
        Assert.Equal("1e-7", NumFmt.JsNumber(1e-7));
    }
}

public class SettingsTests
{
    static readonly MachineProfile M = MachineProfile.F2Ultra;

    [Fact]
    public void ValidatesRanges()
    {
        LaserSettings.Blue(70, 40).Validate(M);
        LaserSettings.Blue(0, 2).Validate(M);
        Assert.Throws<JobException>(() => LaserSettings.Blue(100.1, 40).Validate(M));
        Assert.Throws<JobException>(() => LaserSettings.Blue(-1, 40).Validate(M));
        Assert.Throws<JobException>(() => LaserSettings.Blue(50, 1).Validate(M));
        Assert.Throws<JobException>(() => LaserSettings.Blue(50, 15001).Validate(M));
        Assert.Throws<JobException>(() => LaserSettings.Blue(double.NaN, 40).Validate(M));
        Assert.Throws<JobException>(() => LaserSettings.Blue(50, 40).WithPasses(0).Validate(M));
    }

    [Fact]
    public void ValidatesMopaTable()
    {
        LaserSettings.Red(70, 40, 200, 65).Validate(M);
        // The built-in (cautious) table: 200 ns up to 500 kHz, 2 ns up to 2000 kHz.
        LaserSettings.Red(70, 40, 200, 500).Validate(M);
        Assert.Throws<JobException>(() => LaserSettings.Red(70, 40, 200, 501).Validate(M));
        LaserSettings.Red(70, 40, 2, 2000).Validate(M);
        Assert.Throws<JobException>(() => LaserSettings.Red(70, 40, 2, 2001).Validate(M));
        Assert.Throws<JobException>(() => LaserSettings.Red(70, 40, 201, 65).Validate(M));
        Assert.Throws<JobException>(() => LaserSettings.Red(70, 40, 200, 0).Validate(M));
        // Blue ignores fiber parameters entirely.
        (LaserSettings.Blue(1, 2) with { PulseWidthNs = 1 }).Validate(M);
    }

    [Fact]
    public void SAndFScaling()
    {
        var s = LaserSettings.Blue(70, 40);
        Assert.Equal((700L, 2400L), (s.SValue, s.FValue));
        Assert.Equal(75000L, LaserSettings.Blue(90, 1250).FValue);
    }
}

public class GeometryTests
{
    [Fact]
    public void ClosedPolylineEmitsClosingPointOnce()
    {
        var sq = Polyline.Loop((0, 0), (1, 0), (1, 1), (0, 0));
        Assert.Equal(3, sq.Points.Count);
        var e = sq.EmittedPoints();
        Assert.Equal(4, e.Count);
        Assert.Equal(e[0], e[3]);
    }

    [Fact]
    public void DuplicatesAndDegenerates()
    {
        Assert.False(Polyline.Open((1, 1), (1, 1), (1, 1)).HasExtent);
        Assert.Single(Polyline.Loop((1, 1)).EmittedPoints());
        Assert.Throws<JobException>(() => Polyline.Open((double.NaN, 0)).Validate());
    }
}

public class GCodeTests
{
    static readonly MachineProfile M = MachineProfile.F2Ultra;

    static Polyline Square(double x, double y, double s) =>
        Polyline.Loop((x, y), (x + s, y), (x + s, y + s), (x, y + s));

    static Job VJob(LaserSettings l, params Polyline[] paths) =>
        new(M, paths.Select(p => (Block)new VectorBlock(p, l)).ToList());

    static int Count(string hay, string needle)
    {
        int n = 0;
        for (int i = hay.IndexOf(needle); i >= 0; i = hay.IndexOf(needle, i + needle.Length)) n++;
        return n;
    }

    [Fact]
    public void RejectsEmptyAndOutOfBounds()
    {
        var l = LaserSettings.Blue(70, 40);
        Assert.Throws<EmptyJobException>(() => new Job(M, []).Render());
        Assert.Throws<EmptyJobException>(() => VJob(l, Polyline.Open((5, 5))).Render());
        Assert.Throws<OutsideWorkAreaException>(() => VJob(l, Square(210, 10, 20)).Render());
        Assert.Throws<OutsideWorkAreaException>(() => VJob(l, Square(-1, 10, 5)).Render());
        // Exactly on the field edge is fine.
        VJob(l, Square(200, 200, 20)).Render();
    }

    [Fact]
    public void RoundsModeRunsWholeJobPerPassWithMixedPasses()
    {
        var job = new Job(M,
        [
            new VectorBlock(Square(10, 10, 5), LaserSettings.Blue(20, 400)),
            new VectorBlock(Square(30, 10, 5), LaserSettings.Blue(70, 40).WithPasses(3)),
        ]) { PassMode = new PassMode.Rounds(1.5) };
        var g = job.Render();
        Assert.DoesNotContain("G104", g);
        Assert.Equal(1 + 3, Count(g, "VECTOR HEAD"));
        Assert.Equal(2, Count(g, "G4P1500"));
        var r2 = g[g.IndexOf("# pass 2 of 3")..g.IndexOf("# pass 3 of 3")];
        Assert.Contains("G0X30Y10", r2);
        Assert.DoesNotContain("G0X10Y10", r2);
    }

    [Fact]
    public void LongPausesAreChunkedAndZeroPauseEmitsNothing()
    {
        Assert.Equal(["G4P60000", "G4P60000", "G4P5000"], Job.Dwell(125));
        Assert.Empty(Job.Dwell(0));
        var l = LaserSettings.Blue(70, 40).WithPasses(2);
        var g = new Job(M, [new VectorBlock(Square(10, 10, 5), l)]) { PassMode = new PassMode.Rounds(0) }.Render();
        Assert.DoesNotContain("# pass 2", g);
        Assert.Throws<JobException>(() =>
            new Job(M, [new VectorBlock(Square(10, 10, 5), l)]) { PassMode = new PassMode.Rounds(-1) }.Render());
    }

    [Fact]
    public void FillHandlesHolesAndSkipsDegenerateSpans()
    {
        Vec2[] outer = [new(10, 10), new(30, 10), new(30, 30), new(10, 30)];
        Vec2[] hole = [new(15, 15), new(25, 15), new(25, 25), new(15, 25)];
        var segs = Job.FillLines(new FillBlock([outer, hole], LaserSettings.Blue(50, 500), 10));
        Assert.Equal(2, segs.Count(s => Math.Abs(s.Y - 20) < 1e-9));
        Vec2[] flat = [new(10, 10), new(20, 10)];
        Assert.Empty(Job.FillLines(new FillBlock([flat], LaserSettings.Blue(50, 500), 10)));
    }

    [Fact]
    public void BlankBitmapIsEmptyJob()
    {
        var d = new DotBlock(new Bitmap(10, 10), new(10, 10), LaserSettings.Blue(90, 80), 180, 200, 1, "Jarvis");
        Assert.Throws<EmptyJobException>(() => new Job(M, [d]).Render());
    }

    [Fact]
    public void BorderTracesOutlineAtFramingPower()
    {
        var job = VJob(LaserSettings.Blue(70, 40), Square(10, 10, 5));
        var b = job.Border([new(10, 10), new(15, 10), new(15, 15), new(10, 15), new(10, 10)]);
        Assert.Contains("M114 S1", b);
        Assert.Contains("G1X15Y10S64F960000", b);
        Assert.Throws<OutsideWorkAreaException>(() => job.Border([new(0, 0), new(300, 0)]));
    }
}

public class BGCodeTests
{
    [Fact]
    public void DecodesTokens()
    {
        byte[] raw = [0xc3, .. " GS004-4 HEAD"u8, 0x0a,
            0xe7, 0x00, 0xf8, 0x01, 0x43, 0xce, 0x67, 0x40, 0xf9, 0x01, 0x04, 0xce, 0x87, 0x40, 0x0a,
            0xe7, 0x01, 0xf3, 0x07, 0x00, 0xe6, 0x24, 0x00, 0x0a,
            0xed, 0x05, 0x23, 0xf0, 0x40, 0x0a,
            0xe7, 0x01, 0x98, 0xc0, 0xf0, 0x84, 0xc0, 0xc2, 0xed, 0x90, 0x33, 0xc0, 0xf6, 0x01, 0x00, 0xc2, 0x0a];
        var lines = BGCode.Tidy(BGCode.Decode(raw)).Split('\n').Where(l => l.Length > 0);
        Assert.Equal(["# GS004-4 HEAD", "G0X143.674Y104.874", "G1S700F2400", "M523P40", "G198 P84 \"M9033 V100\""], lines);
    }

    [Fact]
    public void UnknownBytesAreVisible() => Assert.Equal("G<ab>", BGCode.Decode([0xe7, 0xab]));
}

public class XfTests
{
    [Fact]
    public void BuildsUstarWithExpectedMembers()
    {
        var jpg = XfPackage.PreviewJpeg([[new(0, 0), new(10, 10)]]);
        Assert.Equal([0xFF, 0xD8], jpg[..2]);
        var xf = XfPackage.Build(new() { Motion = "G0X0Y0", Border = "G0X0Y0", PreviewJpeg = jpg, Light = "blue" });
        Assert.Equal("ustar\0"u8.ToArray(), xf[257..263]);
        Assert.Equal(0, xf.Length % 512);
        var members = XfPackage.Read(xf);
        Assert.Equal(["preview.jpg", "border.gcode", "motion.gcode", "description.json"], members.Select(m => m.Name));
        Assert.Equal("G0X0Y0"u8.ToArray(), members[2].Data);
    }

    [Fact]
    public void DescriptionCarriesTheEstimatedTimeForTheMachineScreen()
    {
        string Desc(XfPackage.Parts p) => Encoding.UTF8.GetString(XfPackage.Read(XfPackage.Build(p)).Single(m => m.Name == "description.json").Data);
        var flat = new XfPackage.Parts { Motion = "x", Border = "y", PreviewJpeg = [1], Light = "blue", EstimatedSeconds = 83.6 };
        var desc = JsonNode.Parse(Desc(flat))!.AsObject();
        Assert.Equal("84", desc["time"]!.GetValue<string>());
        Assert.Equal(["mode", "light", "create_time", "axis", "time", "processingParams", "material"], desc.Select(kv => kv.Key));
        // Unknown, zero, and the modes xTool Studio gives no time leave it out.
        Assert.DoesNotContain("\"time\"", Desc(flat with { EstimatedSeconds = null }));
        Assert.DoesNotContain("\"time\"", Desc(flat with { EstimatedSeconds = 0 }));
        Assert.DoesNotContain("\"time\"", Desc(flat with { Mode = "CURVE_PROCESS" }));
        Assert.Contains("\"time\":\"84\"", Desc(flat with { Mode = "LASER_CYLINDER", Axis = "U" }));
    }

    [Fact]
    public void ChecksumFieldMatchesPythonTarfile()
    {
        var xf = XfPackage.Build(new() { Motion = "x", Border = "y", PreviewJpeg = [1], Light = "blue" });
        // chksum field: six octal digits, NUL, space.
        Assert.Equal((byte)0, xf[154]);
        Assert.Equal((byte)' ', xf[155]);
        int sum = 0;
        for (int i = 0; i < 512; i++) sum += i is >= 148 and < 156 ? ' ' : xf[i];
        Assert.Equal(Convert.ToString(sum, 8).PadLeft(6, '0'), Encoding.ASCII.GetString(xf, 148, 6));
    }
}

public class StepDownTests
{
    static Polyline Square() => Polyline.Loop((100, 100), (120, 100), (120, 120), (100, 120));

    static List<double> ZMoves(string gcode) => gcode.Split('\n')
        .Where(l => l.StartsWith("G0Z"))
        .Select(l => double.Parse(l[3..l.IndexOf('F')], System.Globalization.CultureInfo.InvariantCulture)).ToList();

    [Fact]
    public void WholeJobPassesGoDownAndBackUpEachRound()
    {
        // A step-down cut and a plain score in the same job: the score must stay in focus.
        var cut = new VectorBlock(Square(), LaserSettings.Blue(70, 40).WithPasses(3)) { Drop = new StepDown(0.2, 0.5) };
        var score = new VectorBlock(Polyline.Open((10, 10), (50, 10)), LaserSettings.Blue(20, 200).WithPasses(2));
        var g = new Job(MachineProfile.F2Ultra, [cut, score]) { PassMode = new PassMode.Rounds(0) }.Render();
        Assert.Equal([-0.2, 0.2, -0.7, 0.7, -1.2, 1.2], ZMoves(g));
        Assert.Equal(0, ZMoves(g).Sum(), 9);
    }

    [Fact]
    public void WholeJobPassesKeepTheHeadDownBetweenPaths()
    {
        // Three paths of one cut layer: each round steps down once, not once per path.
        var laser = LaserSettings.Blue(70, 40).WithPasses(3);
        var drop = new StepDown(0.2, 0.5);
        VectorBlock At(double x) => new(Polyline.Loop((x, 100), (x + 10, 100), (x + 10, 110), (x, 110)), laser) { Drop = drop };
        var g = new Job(MachineProfile.F2Ultra, [At(100), At(130), At(160)]) { PassMode = new PassMode.Rounds(0) }.Render();
        Assert.Equal([-0.2, -0.5, -0.5, 1.2], ZMoves(g));
    }

    [Fact]
    public void PauseAfterEachPassKeepsTheHeadDown()
    {
        // Passes written out for the pause: the head steps down between them and only goes up at the end.
        var cut = new VectorBlock(Square(), LaserSettings.Blue(70, 40).WithPasses(3)) { Drop = new StepDown(0, 0.4), PauseAfterS = 1 };
        var g = new Job(MachineProfile.F2Ultra, [cut]) { PassMode = new PassMode.PerBlock() }.Render();
        Assert.Equal([-0.4, -0.4, 0.8], ZMoves(g));
    }

    [Fact]
    public void PerPathPassesEndWhereTheyStarted()
    {
        var cut = new VectorBlock(Square(), LaserSettings.Blue(70, 40).WithPasses(4)) { Drop = new StepDown(0, 0.3) };
        var g = new Job(MachineProfile.F2Ultra, [cut]) { PassMode = new PassMode.PerBlock() }.Render();
        Assert.Equal([-0.3, -0.3, -0.3, 0.9], ZMoves(g));
        Assert.DoesNotContain("G104", g); // passes written out, not looped
    }

    [Fact]
    public void RefusesToDriveTheHeadTooFar()
    {
        var cut = new VectorBlock(Square(), LaserSettings.Blue(70, 40).WithPasses(30)) { Drop = new StepDown(0, 0.5) };
        var e = Assert.Throws<JobException>(() => new Job(MachineProfile.F2Ultra, [cut]) { PassMode = new PassMode.PerBlock() }.Render());
        Assert.Contains("14.5 mm", e.Message);
        Assert.Throws<JobException>(() => new Job(MachineProfile.F2Ultra, [new VectorBlock(Square(), LaserSettings.Blue(70, 40)) { Drop = new StepDown(0, 3) }]).Render());
    }

    [Fact]
    public void WobbleTracesLoopsAlongThePath()
    {
        var pts = Wobble.Expand([new Vec2(0, 0), new Vec2(10, 0)], 0.4, 0.1);
        Assert.Equal(3601, pts.Count); // 10 mm / (0.1 / 36) + 1
        // Each point is one radius from where the loop centre is at that moment.
        for (int i = 0; i < pts.Count; i += 37)
        {
            var c = new Vec2(Math.Min(i * 0.1 / 36, 10), 0);
            Assert.Equal(0.2, pts[i].Dist(c), 6);
        }
        Assert.Equal(new Vec2(0.2 * Math.Cos(-100 * Math.PI / 180), 0.2 * Math.Sin(-100 * Math.PI / 180)), pts[0]);
    }
}

public class LayerDescentWobbleTests
{
    [Fact]
    public void LayerSettingsReachTheJobAndTheFile()
    {
        var s = PhotonCanvas.Doc.DocState.New();
        var layer = s.Layers[0] with
        {
            Laser = LaserSettings.Red(70, 40, 200, 65).WithPasses(3),
            StepDownMm = 0.5, StepDownFirstMm = 0.01, Wobble = true, WobbleDiameterMm = 0.05, WobbleSpacingMm = 0.015,
        };
        s = s with { Layers = [layer] };
        s = PhotonCanvas.Doc.Ops.AddShapes(s, [new PhotonCanvas.Doc.PathShape { LayerId = layer.Id, Geometry = PhotonCanvas.Doc.PathGeometry.Rect(100, 100, 20, 20) }]);
        var job = PhotonCanvas.Doc.JobBuilder.Build(s, new PhotonCanvas.Doc.JobOptions { WholeJobPasses = false });
        var block = Assert.IsType<VectorBlock>(Assert.Single(job.Job.Blocks));
        Assert.Equal(new StepDown(0.01, 0.5), block.Drop);
        Assert.InRange(block.Path.Points.Count, 191_000, 193_000); // wobbled: ~2400 points per mm of an 80 mm square
        Assert.Contains(job.Warnings, w => w.Contains("1.01 mm"));
        Assert.Contains("G0Z1.01F600", job.Motion);

        var back = PhotonCanvas.Doc.DocFile.Deserialize(PhotonCanvas.Doc.DocFile.Serialize(s)).Layers[0];
        Assert.Equal((0.5, 0.01, true, 0.05, 0.015), (back.StepDownMm, back.StepDownFirstMm, back.Wobble, back.WobbleDiameterMm, back.WobbleSpacingMm));

        // The blue diode wobbles too: it's how the galvo moves, not a property of the laser.
        var blue = s with { Layers = [layer with { Laser = LaserSettings.Blue(70, 40).WithPasses(3) }] };
        var bj = PhotonCanvas.Doc.JobBuilder.Build(blue, new PhotonCanvas.Doc.JobOptions());
        Assert.DoesNotContain(bj.Warnings, w => w.Contains("wobble"));
        Assert.InRange(((VectorBlock)bj.Job.Blocks[0]).Path.Points.Count, 191_000, 193_000);
    }

    [Fact]
    public void TooMuchWobbleIsRefusedBeforeItRunsOutOfMemory()
    {
        // "catstack": 4 m of cuts × 7 passes at 0.015 mm made 69 million moves and crashed the app.
        // 800 mm × 7 passes is 13 million: refused at once, naming a spacing that fits.
        var s = PhotonCanvas.Doc.DocState.New();
        var layer = s.Layers[0] with { Laser = LaserSettings.Blue(100, 50).WithPasses(7), Wobble = true, WobbleDiameterMm = 0.05, WobbleSpacingMm = 0.015 };
        s = s with { Layers = [layer] };
        s = PhotonCanvas.Doc.Ops.AddShapes(s, [new PhotonCanvas.Doc.PathShape { LayerId = layer.Id, Geometry = PhotonCanvas.Doc.PathGeometry.Rect(10, 10, 200, 200) }]);
        var before = GC.GetTotalAllocatedBytes();
        var e = Assert.Throws<JobException>(() => PhotonCanvas.Doc.JobBuilder.Build(s, new PhotonCanvas.Doc.JobOptions()));
        Assert.True(GC.GetTotalAllocatedBytes() - before < 50_000_000, "checked before any wobble is made");
        Assert.Contains("13.4 million moves", e.Message);
        Assert.Contains("at least 0.025 mm", e.Message);

        // The spacing it names fits.
        var ok = PhotonCanvas.Doc.Ops.UpdateLayers(s, new HashSet<Guid> { layer.Id }, l => l with { WobbleSpacingMm = 0.025 });
        var job = PhotonCanvas.Doc.JobBuilder.Build(ok, new PhotonCanvas.Doc.JobOptions());
        Assert.InRange(((VectorBlock)job.Job.Blocks[0]).Path.Points.Count, 1_150_000, 1_160_000); // 800 mm × 1440 per mm

        // And the expansion itself refuses a path too long to hold.
        Assert.Throws<JobException>(() => Wobble.Expand([new Vec2(0, 0), new Vec2(5000, 0)], 0.05, 0.015));
    }
}
