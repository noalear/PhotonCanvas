using System.Globalization;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using Xunit;

namespace PhotonCanvas.Tests;

/// <summary>Spreading fill lines and picture rows apart, so no line is burned straight after its neighbour.</summary>
public class HeatOrderTests
{
    static readonly MachineProfile M = MachineProfile.F2Ultra;
    static readonly IReadOnlyList<Vec2> Square = [new(10, 10), new(30, 10), new(30, 30), new(10, 30)];

    [Fact]
    public void SpreadOrderVisitsEveryLineOnce()
    {
        Assert.Equal([0, 4, 8, 1, 5, 9, 2, 6, 3, 7], Job.SpreadOrder(10, 4));
        Assert.Equal([0, 1, 2], Job.SpreadOrder(3, 1));
        Assert.Equal([0, 2, 1], Job.SpreadOrder(3, 2));
        Assert.Equal([0, 1], Job.SpreadOrder(2, 8));
        // About 0.4 mm between neighbours in a sweep, within sensible limits.
        Assert.Equal(4, Job.SpreadStride(0.1));
        Assert.Equal(32, Job.SpreadStride(0.001));
        Assert.Equal(2, Job.SpreadStride(1));
    }

    [Fact]
    public void SpreadFillBurnsTheSameLinesNeverNeighboursInARow()
    {
        var plain = new FillBlock([Square], LaserSettings.Blue(30, 400), 100);
        var spread = plain with { Spread = 4 };
        var a = Job.FillLines(plain);
        var b = Job.FillLines(spread);
        Assert.Equal(a.Count, b.Count);
        // The same lines, each burned once (direction may differ).
        static string Key(Job.FillSegment s) => $"{s.Y:0.###} {Math.Min(s.X0, s.X1):0.###} {Math.Max(s.X0, s.X1):0.###}";
        Assert.Equal(a.Select(Key).Order(), b.Select(Key).Order());
        // Consecutive lines at least two line gaps apart (0.1 mm here).
        for (int i = 1; i < b.Count; i++) Assert.True(Math.Abs(b[i].Y - b[i - 1].Y) >= 0.2 - 1e-9);
        // Still zigzags: each line starts where the beam has less far to go than a full width back.
        Assert.Contains(b, s => s.X1 < s.X0);
        Assert.Contains(b, s => s.X1 > s.X0);
    }

    [Fact]
    public void LayerSettingSpreadsFillsAndPictures()
    {
        var s = DocState.New();
        var layer = s.Layers[0] with { Mode = LayerMode.Fill, LinesPerCm = 100, SpreadLines = true };
        s = Ops.UpdateLayers(s, new HashSet<Guid> { layer.Id }, _ => layer);
        s = Ops.AddShapes(s, [new PathShape { LayerId = layer.Id, Geometry = PathGeometry.Rect(10, 10, 20, 20) }]);
        var job = JobBuilder.Build(s, new JobOptions());
        var ys = YsOfBurns(job.Motion);
        Assert.True(ys.Count > 100);
        // Not one after the other up the square.
        Assert.Contains(Enumerable.Range(1, ys.Count - 1), i => Math.Abs(ys[i] - ys[i - 1]) > 0.35);
        // Saved and read back with the design and in the material library.
        var back = DocFile.Deserialize(DocFile.Serialize(s));
        Assert.True(back.Layers[0].SpreadLines);
        Assert.True(PhotonCanvas.Doc.Materials.ProcessSettings.From(layer).ApplyTo(s.Layers[0] with { SpreadLines = false }).SpreadLines);
    }

    [Fact]
    public void PictureRowsComeSpread()
    {
        var bmp = new Bitmap(4, 12);
        for (int y = 0; y < 12; y++) bmp[1, y] = true;
        var plain = new DotBlock(bmp, new(20, 20), LaserSettings.Blue(50, 400), 254, 200, 1, "Jarvis");
        var g = new Job(M, [plain with { Spread = 3 }]) { Created = "x" }.Render();
        var ys = g.Split('\n').Where(l => l.StartsWith("G25", StringComparison.Ordinal) && l.Contains('Y'))
            .Select(l => double.Parse(l[(l.IndexOf('Y') + 1)..].Split('S')[0], CultureInfo.InvariantCulture)).ToList();
        Assert.Equal(12, ys.Count);
        // Rows 0, 3, 6, 9, 1, 4 …: 0.3 mm steps, then back.
        Assert.Equal(20.3, ys[1], 6);
        Assert.Equal(12, ys.Distinct().Count());
    }

    /// <summary>The Y of every burning move (S above 0) in order.</summary>
    static List<double> YsOfBurns(string gcode)
    {
        var o = new List<double>();
        double y = 0;
        foreach (var raw in gcode.Split('\n'))
        {
            var l = raw.Trim();
            if (!l.StartsWith('G')) continue;
            int iy = l.IndexOf('Y');
            if (iy >= 0)
            {
                int end = iy + 1;
                while (end < l.Length && (char.IsDigit(l[end]) || l[end] is '.' or '-')) end++;
                y = double.Parse(l[(iy + 1)..end], CultureInfo.InvariantCulture);
            }
            int isv = l.IndexOf('S');
            if (l.StartsWith("G1", StringComparison.Ordinal) && isv >= 0 && double.TryParse(l[(isv + 1)..], NumberStyles.Float, CultureInfo.InvariantCulture, out var sv) && sv > 0)
                o.Add(y);
        }
        return o;
    }
}
