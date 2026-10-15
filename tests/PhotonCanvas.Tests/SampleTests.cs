using System.Globalization;
using PhotonCanvas.Core;

namespace PhotonCanvas.Tests;

/// <summary>
/// Compare our G-code with real xTool Studio exports in captures/gc/.
/// Those files are the user's and are not in git; tests pass vacuously if absent.
/// </summary>
public class SampleTests
{
    static string? Sample(string name)
    {
        var p = TestPaths.Capture(Path.Combine("gc", name));
        return p is null ? null : File.ReadAllText(p);
    }

    static List<string> Norm(string s) => s.Split('\n')
        .Select(l => l.TrimEnd('\r'))
        .Where(l => l.Trim().Length > 0 && !l.StartsWith("# date="))
        .ToList();

    static void AssertSame(string ours, string theirs)
    {
        var (a, b) = (Norm(ours), Norm(theirs));
        for (int i = 0; i < Math.Min(a.Count, b.Count); i++)
            Assert.True(a[i] == b[i], $"first difference at line {i}:\n ours:   {a[i]}\n theirs: {b[i]}");
        Assert.Equal(b.Count, a.Count);
    }

    static Polyline Square() => Polyline.Loop((100, 100), (120, 100), (120, 120), (100, 120));

    [Fact]
    public void BlueCut1Pass()
    {
        if (Sample("blue_cut_70p_40mms_1pass.gc") is not { } theirs) return;
        var job = new Job(MachineProfile.F2Ultra, [new VectorBlock(Square(), LaserSettings.Blue(70, 40))]);
        AssertSame(job.Render(), theirs);
    }

    [Fact]
    public void RedCut8Pass()
    {
        if (Sample("red_cut_70p_40mms_8pass_65khz_200ns.gc") is not { } theirs) return;
        var laser = LaserSettings.Red(70, 40, 200, 65).WithPasses(8);
        var job = new Job(MachineProfile.F2Ultra, [new VectorBlock(Square(), laser)]) { PassMode = new PassMode.PerBlock() };
        AssertSame(job.Render(), theirs);
    }

    [Fact]
    public void BlueCut3PassesStock()
    {
        if (Sample("blue_cut_70p_40mms_3pass.gc") is not { } theirs) return;
        var job = new Job(MachineProfile.F2Ultra, [new VectorBlock(Square(), LaserSettings.Blue(70, 40).WithPasses(3))]) { PassMode = new PassMode.PerBlock() };
        AssertSame(job.Render(), theirs);
    }

    [Fact]
    public void BlueCut3PassesWithStepDown()
    {
        if (Sample("blue_cut_70p_40mms_3pass_stepdown_0.01_0.5.gc") is not { } theirs) return;
        var block = new VectorBlock(Square(), LaserSettings.Blue(70, 40).WithPasses(3)) { Drop = new StepDown(0.01, 0.5) };
        var job = new Job(MachineProfile.F2Ultra, [block]) { PassMode = new PassMode.PerBlock() };
        AssertSame(job.Render(), theirs);
    }

    [Fact]
    public void RedCutWithWobbleMatchesPointByPoint()
    {
        if (Sample("red_cut_70p_40mms_3pass_wobble_0.05_0.015.gc") is not { } theirs) return;
        // xTool went round this square the other way.
        var square = new List<Vec2> { new(100, 100), new(100, 120), new(120, 120), new(120, 100), new(100, 100) };
        var block = new VectorBlock(Polyline.Open(Wobble.Expand(square, 0.05, 0.015)), LaserSettings.Red(70, 40, 200, 65).WithPasses(3));
        var ours = Norm(new Job(MachineProfile.F2Ultra, [block]) { PassMode = new PassMode.PerBlock() }.Render());
        var them = Norm(theirs);
        static bool IsMove(string l) => l.StartsWith("G0X") || l.StartsWith("G1X");
        // Everything but the moves is identical.
        Assert.Equal(them.Where(l => !IsMove(l)), ours.Where(l => !IsMove(l)));
        static List<Vec2> Pts(List<string> ls) => ls.Where(IsMove).Select(l =>
        {
            int x = l.IndexOf('X'), y = l.IndexOf('Y'), e = l.IndexOfAny(['S', 'F'], y);
            return new Vec2(double.Parse(l[(x + 1)..y], CultureInfo.InvariantCulture), double.Parse(l[(y + 1)..(e < 0 ? l.Length : e)], CultureInfo.InvariantCulture));
        }).ToList();
        var a = Pts(ours); var b = Pts(them);
        Assert.InRange(a.Count, b.Count * 0.999, b.Count * 1.001);
        // Every 50th of our points lies within 0.012 mm of one of theirs nearby (counts drift slightly at corners).
        for (int i = 0; i < a.Count; i += 50)
        {
            int j = (int)((long)i * b.Count / a.Count);
            double best = double.MaxValue;
            for (int k = Math.Max(0, j - 80); k < Math.Min(b.Count, j + 80); k++) best = Math.Min(best, a[i].Dist(b[k]));
            Assert.True(best <= 0.012, $"point {i} {a[i]} is {best:0.000} mm from xTool's path");
        }
    }

    [Fact]
    public void BlueFill()
    {
        if (Sample("blue_fill_90p_1250mms_100lpcm.gc") is not { } theirs) return;
        Vec2[] poly = [new(100, 100), new(120, 100), new(120, 120), new(100, 120)];
        var job = new Job(MachineProfile.F2Ultra, [new FillBlock([poly], LaserSettings.Blue(90, 1250), 100)]);
        Assert.Equal(Preamble.Engrave, job.Preamble);
        AssertSame(job.Render(), theirs);
    }

    [Fact]
    public void DotBitmapEmission()
    {
        if (Sample("blue_image_90p_200us_180dpi_jarvis.gc") is not { } theirs) return;
        double pitch = 25.4 / 180;
        double x = 0, y = 0;
        var dots = new List<(double X, double Y)>();
        foreach (var line in theirs.Split('\n').Where(l => l.StartsWith("G25X") || l.StartsWith("G25Y")))
        {
            var rest = line[3..].TrimEnd('\r');
            var idx = rest.Select((c, i) => (c, i)).Where(t => t.c is 'X' or 'Y' or 'S').ToList();
            for (int k = 0; k < idx.Count; k++)
            {
                int end = k + 1 < idx.Count ? idx[k + 1].i : rest.Length;
                double v = double.Parse(rest[(idx[k].i + 1)..end], CultureInfo.InvariantCulture);
                if (idx[k].c == 'X') x = v;
                else if (idx[k].c == 'Y') y = v;
            }
            dots.Add((x, y));
        }
        double ox = dots.Min(d => d.X), oy = dots.Min(d => d.Y);
        var cells = dots
            .Select(d => (R: (int)Math.Round((d.Y - oy) / pitch), C: (int)Math.Round((d.X - ox) / pitch)))
            .ToHashSet();
        var bmp = new Bitmap(cells.Max(c => c.C) + 1, cells.Max(c => c.R) + 1);
        foreach (var (r, c) in cells) bmp[c, r] = true;
        var job = new Job(MachineProfile.F2Ultra,
            [new DotBlock(bmp, new(ox, oy), LaserSettings.Blue(90, 80), 180, 200, 1, "Jarvis")]);
        AssertSame(job.Render(), theirs);
    }

    [Fact]
    public void BinaryJobDecodesCleanly()
    {
        if (TestPaths.Capture("xf/last_job/motion.bgcode") is not { } path) return;
        var text = BGCode.Tidy(BGCode.Decode(File.ReadAllBytes(path)));
        Assert.DoesNotContain("<", text.Split('\n').Where(l => !l.StartsWith('#')).Aggregate("", (a, b) => a + b));
        Assert.Contains("G104 P8", text);
        Assert.Contains("M523P40", text);
    }
}
