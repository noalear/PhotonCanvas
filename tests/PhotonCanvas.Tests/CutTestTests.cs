using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Geometry;
using Xunit;
using Tri = PhotonCanvas.Doc.Import.StlDepth.Triangle;
using V3 = PhotonCanvas.Doc.Import.StlDepth.Vec3;

namespace PhotonCanvas.Tests;

public class CutTestTests
{
    [Fact]
    public void GridHasASquarePerSettingWithLabelsFirst()
    {
        var o = new CutTestOptions { SpeedMin = 4, SpeedMax = 24, SpeedSteps = 6, DownMin = 1, DownMax = 3, DownSteps = 3, Power = 100 };
        var (s, added) = CutTest.Add(DocState.New() with { Layers = [] }, o);
        var cells = s.Layers.Where(CutTest.IsCell).ToList();
        Assert.Equal(18, cells.Count);
        Assert.All(cells, l => Assert.Equal(LayerMode.Cut, l.Mode));
        Assert.All(cells, l => Assert.Equal(0, l.KerfMm));
        Assert.All(cells, l => Assert.Equal(100, l.Laser.Power));
        Assert.Equal([4, 8, 12, 16, 20, 24], cells.Select(l => l.Laser.Speed).Distinct().Order());
        Assert.Equal([1, 2, 3], cells.Select(l => l.Laser.Passes).Distinct().Order());
        // Labels are marked before any square is cut (squares drop out), and lightly.
        int labels = s.Layers.FindIndex(l => l.Name == "Cut test labels");
        Assert.Equal(0, labels);
        Assert.True(s.Layers[labels].Laser.Power <= 15);
        var job = JobBuilder.Build(s, new JobOptions());
        Assert.DoesNotContain(job.Warnings, w => w.Contains("runs after the cut layer"));
        // One square per cell, none overlapping.
        var squares = s.Shapes.Where(x => cells.Any(c => c.Id == x.LayerId)).ToList();
        Assert.Equal(18, squares.Count);
        Assert.All(squares, a => Assert.All(squares.Where(b => b != a), b => Assert.False(a.Bounds.Intersects(b.Bounds))));
        Assert.Contains(added, id => s.Shapes.Any(x => x.Id == id && x is TextShape { Text: "mm/s" }));
    }

    [Fact]
    public void PowerCanGoDownTheSide()
    {
        var o = new CutTestOptions { Down = CutTestDown.Power, DownMin = 60, DownMax = 100, DownSteps = 5, Passes = 2, SpeedSteps = 2 };
        var (s, _) = CutTest.Add(DocState.New() with { Layers = [] }, o);
        var cells = s.Layers.Where(CutTest.IsCell).ToList();
        Assert.Equal([60, 70, 80, 90, 100], cells.Select(l => l.Laser.Power).Distinct().Order());
        Assert.All(cells, l => Assert.Equal(2, l.Laser.Passes));
    }

    [Fact]
    public void QuickestIsFewestPassesForTheSpeed()
    {
        // 20 mm/s twice round is slower than 12 mm/s once.
        Assert.True(CutTest.SecondsPerMm(LaserSettings.Blue(100, 12)) < CutTest.SecondsPerMm(LaserSettings.Blue(100, 20) with { Passes = 2 }));
    }

    [Fact]
    public void KerfTestHasASquareAndAComb()
    {
        var o = new KerfTestOptions { ThicknessMm = 3, Laser = LaserSettings.Blue(100, 12) };
        var (s, _) = CutTest.AddKerf(DocState.New() with { Layers = [] }, o);
        var cut = CutTest.KerfLayer(s)!;
        Assert.Equal(0, cut.KerfMm);
        Assert.Equal(12, cut.Laser.Speed);
        Assert.True(s.Layers.IndexOf(cut) > s.Layers.FindIndex(l => l.Name == "Kerf test labels"));
        var pieces = s.Shapes.Where(x => x.LayerId == cut.Id).OfType<PathShape>().ToList();
        Assert.Equal(2, pieces.Count);
        var square = pieces.Single(p => p.Name.StartsWith("Kerf square"));
        Assert.Equal(20, square.Bounds.Width, 6);
        // The comb's slots: the thickness plus each fit, in order.
        Assert.Equal([2.8, 2.9, 3.0, 3.1, 3.2, 3.3], CutTest.SlotWidths(o).Select(w => Math.Round(w, 2)));
        var comb = pieces.Single(p => p.Name == "Fit comb").World.Flatten(0.05).Single().Points;
        var gaps = new List<double>();
        for (int i = 1; i + 2 < comb.Count; i++)
            if (Math.Abs(comb[i].X - comb[i + 1].X) < 1e-9 && comb[i + 1].Y > comb[i].Y && Math.Abs(comb[i + 2].Y - comb[i + 1].Y) < 1e-9 && comb[i + 2].X > comb[i + 1].X)
                gaps.Add(Math.Round(comb[i + 2].X - comb[i + 1].X, 2));
        Assert.Equal([2.8, 2.9, 3.0, 3.1, 3.2, 3.3], gaps);
        Assert.Contains(s.Shapes.OfType<TextShape>(), t => t.Text == "+0.1");
        Assert.Contains(s.Shapes.OfType<TextShape>(), t => t.Text == "−0.2");
    }

    [Fact]
    public void KerfTestLabelsGoBeforeTheGridsCuts()
    {
        // The kerf test is added later, beside a grid that's still in the design: its labels must still come before every cut.
        var (s, _) = CutTest.Add(DocState.New() with { Layers = [] }, new CutTestOptions());
        (s, _) = CutTest.AddKerf(s, new KerfTestOptions { Origin = new Vec2(10, 120) });
        Assert.DoesNotContain(JobBuilder.Build(s, new JobOptions()).Warnings, w => w.Contains("runs after the cut layer"));
    }

    [Fact]
    public void KerfAndFitFromMeasurements()
    {
        Assert.Equal(0.18, CutTest.Kerf(20, 19.82), 6);
        Assert.Equal(0, CutTest.Kerf(20, 20.05));
        // Snug in the +0.1 slot: it came out 0.18 wider than drawn. With the kerf set on the layer slots come out as drawn, so the fit takes the kerf in.
        Assert.Equal(0.28, CutTest.SlotFit(0.1, 0.18, layerKerfSet: true), 6);
        Assert.Equal(0.1, CutTest.SlotFit(0.1, 0.18, layerKerfSet: false), 6);
    }

    [Fact]
    public void SliceMarksComeBeforeTheCuts()
    {
        var stack = StackSlicer.Slice(Box(), new SliceOptions { ThicknessMm = 3, HeightMm = 6, KeepJoined = false });
        var (s, _) = StackLayout.Place(DocState.New(), stack, MachineProfile.F2Ultra, numbers: true, marks: true);
        int marks = s.Layers.FindIndex(l => l.Name == "Slices · marks"), cut = s.Layers.FindIndex(l => l.Name == "Slices · cut");
        Assert.True(marks < cut);
        Assert.DoesNotContain(JobBuilder.Build(s, new JobOptions()).Warnings, w => w.Contains("runs after the cut layer"));
    }

    static IEnumerable<Tri> Quad(V3 a, V3 b, V3 c, V3 d) => [new Tri(a, b, c), new Tri(a, c, d)];

    static List<Tri> Box()
    {
        double x0 = 0, y0 = 0, z0 = 0, x1 = 30, y1 = 20, z1 = 6;
        V3 P(double x, double y, double z) => new(x, y, z);
        return
        [
            .. Quad(P(x0, y0, z0), P(x1, y0, z0), P(x1, y1, z0), P(x0, y1, z0)), .. Quad(P(x0, y0, z1), P(x0, y1, z1), P(x1, y1, z1), P(x1, y0, z1)),
            .. Quad(P(x0, y0, z0), P(x0, y0, z1), P(x1, y0, z1), P(x1, y0, z0)), .. Quad(P(x0, y1, z0), P(x1, y1, z0), P(x1, y1, z1), P(x0, y1, z1)),
            .. Quad(P(x0, y0, z0), P(x0, y1, z0), P(x0, y1, z1), P(x0, y0, z1)), .. Quad(P(x1, y0, z0), P(x1, y0, z1), P(x1, y1, z1), P(x1, y1, z0)),
        ];
    }
}
