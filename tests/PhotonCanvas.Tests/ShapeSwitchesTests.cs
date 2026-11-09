using PhotonCanvas.Core;
using PhotonCanvas.Doc;

namespace PhotonCanvas.Tests;

/// <summary>A shape's own switches inside its layer: output (sent or a guide), shown or hidden, locked.</summary>
public class ShapeSwitchesTests
{
    static (DocState State, PathShape A, PathShape B, Layer Cut) TwoSquares()
    {
        var s = DocState.New();
        var cut = s.Layers[0];
        var a = new PathShape { Name = "A", LayerId = cut.Id, Geometry = PathGeometry.Rect(10, 10, 10, 10) };
        var b = new PathShape { Name = "B", LayerId = cut.Id, Geometry = PathGeometry.Rect(40, 10, 10, 10) };
        return (Ops.AddShapes(s, [a, b]), a, b, cut);
    }

    static Rect2 StepBox(BuiltJob job) =>
        job.Steps.SelectMany(st => st.Points).Aggregate(Rect2.Empty, (r, p) => r.Union(new Rect2(p.X, p.Y, p.X, p.Y)));

    [Fact]
    public void ShapesAreShownAndSentUntilSwitchedOff()
    {
        var (_, a, _, _) = TwoSquares();
        Assert.True(a.Visible);
        Assert.True(a.Output);
        Assert.True(a.Sent);
        Assert.False(a.WithVisible(false).Sent);
        Assert.False(a.WithOutput(false).Sent);
        Assert.True(a.WithOutput(false).Visible);
        // The switches are independent.
        var both = a.WithVisible(false).WithOutput(false).WithLocked(true);
        Assert.False(both.Visible);
        Assert.False(both.Output);
        Assert.True(both.Locked);
        Assert.True(both.WithVisible(true).WithOutput(true).Sent);
    }

    [Fact]
    public void AShapeWithItsOutputOffIsLeftOutOfTheJob()
    {
        var (s, a, b, _) = TwoSquares();
        var both = JobBuilder.Build(s, new JobOptions());
        var off = Ops.SetShapeFlags(s, new HashSet<Guid> { b.Id }, output: false);
        var job = JobBuilder.Build(off, new JobOptions());
        Assert.True(job.Steps.Count < both.Steps.Count);
        // Only A is cut: nothing reaches B, 30 mm to the right.
        Assert.True(StepBox(job).MaxX < 30);
        Assert.True(StepBox(both).MaxX > 45);
        // No warning for a guide: it's off on purpose.
        Assert.DoesNotContain(job.Warnings, w => w.Contains("hidden"));
        // The shape stays in the design, on its layer.
        Assert.Equal(a.LayerId, off.FindShape(b.Id)!.LayerId);
    }

    [Fact]
    public void AHiddenShapeIsNotSentAndTheChecksSaySo()
    {
        var (s, _, b, cut) = TwoSquares();
        var hidden = Ops.SetShapeFlags(s, new HashSet<Guid> { b.Id }, visible: false);
        var job = JobBuilder.Build(hidden, new JobOptions());
        Assert.True(StepBox(job).MaxX < 30);
        Assert.Contains(job.Warnings, w => w.Contains("hidden") && w.Contains(cut.Name));
        // Hidden and output off too: still not sent, and nothing to say (it's a guide anyway).
        var guide = Ops.SetShapeFlags(hidden, new HashSet<Guid> { b.Id }, output: false);
        Assert.DoesNotContain(JobBuilder.Build(guide, new JobOptions()).Warnings, w => w.Contains("hidden"));
    }

    [Fact]
    public void ALayerWithEveryShapeOffSendsNothing()
    {
        var (s, a, b, cut) = TwoSquares();
        s = Ops.SetShapeFlags(s, new HashSet<Guid> { a.Id, b.Id }, output: false);
        Assert.Empty(s.SentFor(cut));
        Assert.Equal(2, s.ShapesFor(cut).Count());
        var e = Assert.Throws<JobException>(() => JobBuilder.Build(s, new JobOptions()));
        Assert.Contains("hidden or has its own output off", e.Message);
    }

    [Fact]
    public void ShapeOutputCannotSendAShapeOnTheMaskLayer()
    {
        var (s, a, _, _) = TwoSquares();
        s = MaskLayer.Mark(s, new HashSet<Guid> { a.Id });
        var onMask = s.FindShape(a.Id)!;
        Assert.True(onMask.Output);
        var job = JobBuilder.Build(s, new JobOptions());
        // Only B (the one left on the cut layer) is cut.
        Assert.True(StepBox(job).MinX > 30);
    }

    [Fact]
    public void SubLayersSkipTheParentsShapesThatAreOff()
    {
        var (s, _, b, cut) = TwoSquares();
        var outline = Layer.Default(1, LayerMode.Score) with { Name = "Outline", ParentId = cut.Id };
        s = Ops.AddLayer(s, outline);
        s = Ops.SetShapeFlags(s, new HashSet<Guid> { b.Id }, output: false);
        Assert.Single(s.SentFor(outline));
        var job = JobBuilder.Build(s, new JobOptions());
        Assert.True(StepBox(job).MaxX < 30);
    }

    [Fact]
    public void HiddenShapesCantBePicked()
    {
        var (s, a, b, _) = TwoSquares();
        s = Ops.SetShapeFlags(s, new HashSet<Guid> { a.Id }, visible: false);
        s = Ops.SetShapeFlags(s, new HashSet<Guid> { b.Id }, output: false);
        var pickable = s.Editable().Select(x => x.Id).ToList();
        Assert.DoesNotContain(a.Id, pickable);
        // A guide can still be picked and moved.
        Assert.Contains(b.Id, pickable);
        Assert.DoesNotContain(a.Id, s.Shown().Select(x => x.Id));
    }

    [Fact]
    public void SetShapeFlagsChangesOnlyWhatItsAskedAndNothingWhenSetAlready()
    {
        var (s, a, b, _) = TwoSquares();
        var ids = new HashSet<Guid> { a.Id };
        var next = Ops.SetShapeFlags(s, ids, visible: false, locked: true);
        var na = next.FindShape(a.Id)!;
        Assert.False(na.Visible);
        Assert.True(na.Locked);
        Assert.True(na.Output);
        Assert.Same(b, next.FindShape(b.Id));
        Assert.Same(next, Ops.SetShapeFlags(next, ids, visible: false, locked: true));
        Assert.Same(s, Ops.SetShapeFlags(s, ids, output: true));
    }

    [Fact]
    public void TheSwitchesAreSavedAndKeptWhenAShapeIsRebuilt()
    {
        var (s, a, b, _) = TwoSquares();
        s = Ops.SetShapeFlags(s, new HashSet<Guid> { a.Id }, visible: false);
        s = Ops.SetShapeFlags(s, new HashSet<Guid> { b.Id }, output: false, locked: true);
        var back = DocFile.Deserialize(DocFile.Serialize(s));
        var ba = back.FindShape(a.Id)!;
        var bb = back.FindShape(b.Id)!;
        Assert.False(ba.Visible);
        Assert.True(ba.Output);
        Assert.False(ba.Locked);
        Assert.True(bb.Visible);
        Assert.False(bb.Output);
        Assert.True(bb.Locked);
        // Old files (no switches) load with everything on.
        var plain = DocFile.Deserialize(DocFile.Serialize(TwoSquares().State));
        Assert.All(plain.Shapes, x => Assert.True(x.Sent));
        // Text rebuilt from new words keeps them.
        var text = TextShape.Create("Hi", new TextStyle("Arial", 5), s.Layers[0].Id, Affine.Identity);
        var off = (TextShape)text.WithOutput(false).WithVisible(false);
        var rebuilt = off.Edit("Hello", off.Style);
        Assert.False(rebuilt.Output);
        Assert.False(rebuilt.Visible);
    }

    [Fact]
    public void ExportsLeaveHiddenShapesOut()
    {
        var (s, a, _, _) = TwoSquares();
        var svg = Doc.Export.DesignExport.Svg(s);
        var hidden = Doc.Export.DesignExport.Svg(Ops.SetShapeFlags(s, new HashSet<Guid> { a.Id }, visible: false));
        Assert.True(hidden.Length < svg.Length);
    }
}
