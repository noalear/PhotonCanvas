using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using Xunit;

namespace PhotonCanvas.Tests;

/// <summary>Defects found in the October 2026 review, each pinned by the case that showed it.</summary>
public class ReviewFindingsTests
{
    static PathShape Sq(Guid layer, double x) => new() { LayerId = layer, Geometry = PathGeometry.Rect(x, 10, 10, 10) };

    [Fact]
    public void HighlyReflectiveIsSaved()
    {
        var s = DocState.New();
        s = s with { Layers = [s.Layers[0] with { Laser = LaserSettings.Red(50, 500, 200, 55), Reflective = true }] };
        var back = DocFile.Deserialize(DocFile.Serialize(Ops.AddShapes(s, [Sq(s.Layers[0].Id, 10)])));
        Assert.True(back.Layers[0].Reflective);
        // So the job still refuses the frequencies that could reflect back into the MOPA.
        Assert.Throws<JobException>(() => JobBuilder.Build(back, new JobOptions()));
    }

    /// <summary>Layers A, "A fill" (a sub-layer of A) and B, a square on A and one on B.</summary>
    static (DocState S, Layer A, Layer Fill, Layer B) WithSubLayer()
    {
        var a = Layer.Default(0) with { Name = "A" };
        var fill = Layer.Default(1, LayerMode.Fill) with { Name = "A fill", ParentId = a.Id };
        var b = Layer.Default(2) with { Name = "B" };
        var s = new DocState([a, fill, b], []);
        return (Ops.AddShapes(s, [Sq(a.Id, 10), Sq(b.Id, 50)]), a, fill, b);
    }

    [Fact]
    public void DeletedLayersShapesNeverLandOnASubLayer()
    {
        var (s, a, fill, b) = WithSubLayer();
        // The layer above B is the sub-layer: B's square goes to its parent, and is still marked.
        var r = Ops.DeleteLayers(s, new HashSet<Guid> { b.Id }, fill.Id);
        Assert.All(r.Shapes, x => Assert.Equal(a.Id, x.LayerId));
        Assert.Equal(2, JobBuilder.Build(r, new JobOptions()).Job.Blocks.Count(x => x is VectorBlock));

        // Deleting A takes its sub-layer too; A's square goes to the layer left, not away.
        r = Ops.DeleteLayers(s, new HashSet<Guid> { a.Id }, fill.Id);
        Assert.Equal(2, r.Shapes.Count);
        Assert.All(r.Shapes, x => Assert.Equal(b.Id, x.LayerId));
    }

    [Fact]
    public void MergingKeepsALayerThatHoldsShapes()
    {
        var (s, a, fill, _) = WithSubLayer();
        // The sub-layer dragged above its parent, then both merged.
        s = Ops.MoveLayer(s, fill.Id, 0);
        var r = Ops.MergeLayers(s, [fill.Id, a.Id]);
        Assert.Equal(2, r.Shapes.Count);
        Assert.Contains(r.Layers, l => l.Id == a.Id);
        Assert.Contains(r.Shapes, x => x.LayerId == a.Id);
    }

    static double Length(PathGeometry g) => g.Flatten(0.01).Sum(f =>
    {
        double l = 0;
        for (int i = 1; i < f.Points.Count; i++) l += f.Points[i].Dist(f.Points[i - 1]);
        return l + (f.Closed && f.Points.Count > 1 ? f.Points[^1].Dist(f.Points[0]) : 0);
    });

    [Theory]
    [InlineData(10)]
    [InlineData(30)]
    public void SingleLineTextIsOneWholeStroke(double size)
    {
        // An O: one line round the middle of the ring, between the outline's outer and inner edge.
        var outline = TextLayout.Build("O", new TextStyle("Arial", size));
        var single = TextLayout.Build("O", new TextStyle("Arial", size, SingleLine: true));
        var rings = outline.Subpaths.Select(sp => Length(new PathGeometry([sp]))).OrderBy(x => x).ToList();
        double middle = (rings[0] + rings[^1]) / 2;
        Assert.InRange(single.Subpaths.Count(), 1, 2);
        Assert.InRange(Length(single), middle * 0.9, middle * 1.08);
    }

    [Fact]
    public void DeepestSliceFirstStartsOnTheSurface()
    {
        // A grey ramp, so every one of the 16 slices has something in it.
        var px = new byte[16 * 4];
        for (int i = 0; i < 16; i++) { px[i * 4] = px[i * 4 + 1] = px[i * 4 + 2] = (byte)(255 - i * 16); px[i * 4 + 3] = 255; }
        var layer = Layer.Default(0, LayerMode.Image) with
        {
            ImageMode = ImageEngraveMode.Slices, Dpi = 50,
            Slices = new DepthSlices { Count = 16, DropMm = 0.2, DropEvery = 4, CleanupEvery = 4, DeepestFirst = true },
        };
        var pic = new ImageShape { LayerId = layer.Id, Source = ImageSource.FromEncoded(new Core.Imaging.RasterImage(16, 1, px).ToPng()), SizeMm = new Vec2(64, 8), Transform = Affine.Translate(20, 20) };
        var job = JobBuilder.Build(Ops.AddShapes(new DocState([layer], []), [pic]), new JobOptions()).Job;
        var slices = job.Blocks.OfType<FillBlock>().Where(b => b.Laser == layer.Laser).ToList();
        // Nothing is cut yet when the first slice runs: the surface focus. Then down 0.2 mm every 4 slices.
        Assert.Equal(0, slices[0].FocusOffsetMm, 9);
        Assert.All(slices.Zip(slices.Skip(1)), p => Assert.True(p.Second.FocusOffsetMm >= p.First.FocusOffsetMm));
        Assert.Equal(0.2 * 3, slices[^1].FocusOffsetMm, 9);
        // A cleanup after every 4 slices run, not after the slice numbered 4.
        int i4 = job.Blocks.ToList().FindIndex(b => b is FillBlock f && f.Laser != layer.Laser);
        Assert.Equal(4, job.Blocks.Take(i4).Count(b => b is FillBlock f && f.Laser == layer.Laser));
    }

    [Fact]
    public void ACurvedSurfaceFillBurnsAtItsSpeed()
    {
        // The head's Z move between bands is at F600 (10 mm/s); F is modal, so the burn has to set it back.
        var square = new Vec2[] { new(95, 95), new(125, 95), new(125, 125), new(95, 125) };
        var g = new Job(MachineProfile.F2Ultra, [new FillBlock([square], LaserSettings.Blue(10, 400), 50)]) { SurfaceHeight = p => 20 + (p.Y - 95) * 0.1, Created = "x" }.Render();
        var lines = g.Split('\n').Select(l => l.Trim()).ToList();
        string feed = "F24000";
        string? now = null;
        int burns = 0;
        foreach (var l in lines)
        {
            if (System.Text.RegularExpressions.Regex.Match(l, @"F(\d+)") is { Success: true } m && l.StartsWith('G')) now = "F" + m.Groups[1].Value;
            if (l.StartsWith("G1X") && !l.EndsWith("S0")) { burns++; Assert.Equal(feed, now); }
        }
        Assert.True(burns > 100);
    }

    [Fact]
    public void OverscanIsCheckedOnEveryTurnedPass()
    {
        // Lines across fit with their run-ups; turned 90° on pass 2, the run-ups go above the field.
        var area = new Vec2[] { new(5, 0.5), new(50, 0.5), new(50, 30), new(5, 30) };
        var fill = new FillBlock([area], LaserSettings.Blue(10, 400).WithPasses(2), 20) { OverscanMm = 2, AngleStepDeg = 90 };
        Assert.Throws<OutsideWorkAreaException>(() => new Job(MachineProfile.F2Ultra, [fill]).Validate());
    }

    [Fact]
    public void AShapeSavedOnASubLayerLoadsOnItsParent()
    {
        var (s, a, fill, _) = WithSubLayer();
        s = s with { Shapes = [.. s.Shapes.Select(x => x.LayerId == a.Id ? x.With(layerId: fill.Id) : x)] };
        var back = DocFile.Deserialize(DocFile.Serialize(s));
        Assert.DoesNotContain(back.Shapes, x => x.LayerId == fill.Id);
        Assert.Contains(back.Shapes, x => x.LayerId == a.Id);
    }
}
