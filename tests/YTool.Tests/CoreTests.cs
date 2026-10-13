using System.Text;
using YTool.Core;

namespace YTool.Tests;

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
        LaserSettings.Red(70, 40, 200, 1000).Validate(M);
        Assert.Throws<JobException>(() => LaserSettings.Red(70, 40, 200, 1001).Validate(M));
        LaserSettings.Red(70, 40, 2, 4000).Validate(M);
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
