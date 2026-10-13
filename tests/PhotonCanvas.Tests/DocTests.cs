using PhotonCanvas.Core;
using PhotonCanvas.Doc;

namespace PhotonCanvas.Tests;

public class PathDataTests
{
    static List<FlatPath> Flat(string d) => PathGeometry.FromSvgPathData(d).Flatten(0.001);

    [Fact]
    public void AbsoluteRelativeAndImplicitLineTo()
    {
        var f = Flat("M10 10 20 10 l0 10 h-10 z m5 5 l1 0");
        Assert.Equal(2, f.Count);
        Assert.True(f[0].Closed);
        Assert.Equal([new(10, 10), new(20, 10), new(20, 20), new(10, 20), new(10, 10)], f[0].Points);
        // After z the current point is the subpath start; m is relative to it.
        Assert.Equal([new(15, 15), new(16, 15)], f[1].Points);
        Assert.False(f[1].Closed);
    }

    [Fact]
    public void CompactNumberSyntax()
    {
        // "0.5.5" is two numbers; "1e1" is 10; signs separate numbers.
        var f = Flat("M0.5.5L1e1-2");
        Assert.Equal([new(0.5, 0.5), new(10, -2)], f[0].Points);
    }

    [Fact]
    public void ArcStaysOnCircle()
    {
        // Half circle of radius 10 around (10, 0).
        var f = Flat("M0 0 A10 10 0 0 1 20 0");
        Assert.All(f[0].Points, p => Assert.InRange(p.Dist(new(10, 0)), 9.99, 10.01));
        Assert.Equal(20, f[0].Points[^1].X, 9);
        // Sweep 1 goes through negative y (upwards on screen) here.
        Assert.Contains(f[0].Points, p => p.Y < -9.9);
    }

    [Fact]
    public void ArcRadiiTooSmallAreScaledUp()
    {
        var f = Flat("M0 0 A1 1 0 0 1 20 0");
        Assert.Equal(new Vec2(20, 0), f[0].Points[^1]);
        Assert.All(f[0].Points, p => Assert.InRange(p.Dist(new(10, 0)), 9.99, 10.01));
    }

    [Fact]
    public void SmoothCurvesReflectControlPoints()
    {
        var g = PathGeometry.FromSvgPathData("M0 0 C0 10 10 10 10 0 S20 -10 20 0");
        var s = g.Subpaths[0].Segs[1];
        Assert.Equal(new Vec2(10, -10), s.C1);
    }

    [Fact]
    public void MalformedDataKeepsWhatWasRead()
    {
        var f = Flat("M0 0 L10 0 L10 x 20");
        Assert.Single(f);
        Assert.Equal(2, f[0].Points.Count);
        Assert.Empty(Flat("garbage"));
        Assert.Empty(Flat("M5 5 Z 3 4")); // numbers after Z are an error, no hang
    }

    [Fact]
    public void RoundTripsThroughSvgPathData()
    {
        var g = PathGeometry.Ellipse(50, 50, 20, 10);
        var back = PathGeometry.FromSvgPathData(g.ToSvgPathData());
        Assert.Equal(g.Bounds(), back.Bounds());
        Assert.True(back.Subpaths[0].Closed);
    }

    [Fact]
    public void EllipseBoundsAreTight()
    {
        var b = PathGeometry.Ellipse(50, 50, 20, 10).Bounds(0.001);
        Assert.Equal(30, b.MinX, 2);
        Assert.Equal(70, b.MaxX, 2);
        Assert.Equal(40, b.MinY, 2);
        Assert.Equal(60, b.MaxY, 2);
    }
}

public class SvgImportTests
{
    [Fact]
    public void MillimetreDocumentMapsOneToOne()
    {
        var r = SvgImport.ParseString("""
            <svg xmlns="http://www.w3.org/2000/svg" width="100mm" height="50mm" viewBox="0 0 100 50">
              <rect x="10" y="10" width="20" height="5" stroke="#ff0000" fill="none"/>
            </svg>
            """);
        var b = r.Paths.Single().Geometry.Bounds();
        Assert.Equal(new Rect2(10, 10, 30, 15), b);
        Assert.Equal(0xFF0000u, r.Paths[0].Color);
    }

    [Fact]
    public void PixelsDefaultTo96PerInchAnd72IsSelectable()
    {
        const string svg = """<svg xmlns="http://www.w3.org/2000/svg"><rect width="96" height="96"/></svg>""";
        Assert.Equal(25.4, SvgImport.ParseString(svg).Bounds.Width, 6);
        Assert.Equal(25.4 * 96 / 72, SvgImport.ParseString(svg, new() { PxPerInch = 72 }).Bounds.Width, 6);
    }

    [Fact]
    public void InchDocumentWithScaledViewBox()
    {
        var r = SvgImport.ParseString("""
            <svg xmlns="http://www.w3.org/2000/svg" width="2in" height="1in" viewBox="0 0 200 100">
              <line x1="0" y1="0" x2="200" y2="0" stroke="black"/>
            </svg>
            """);
        Assert.Equal(50.8, r.Bounds.Width, 6);
    }

    [Fact]
    public void NestedTransformsCompose()
    {
        var r = SvgImport.ParseString("""
            <svg xmlns="http://www.w3.org/2000/svg" width="100mm" height="100mm" viewBox="0 0 100 100">
              <g transform="translate(10 20)">
                <g transform="scale(2)"><rect x="1" y="1" width="5" height="5"/></g>
              </g>
            </svg>
            """);
        Assert.Equal(new Rect2(12, 22, 22, 32), r.Paths[0].Geometry.Bounds());
    }

    [Fact]
    public void RotateAboutPointAndMatrix()
    {
        var t = SvgImport.ParseTransform("rotate(90 10 10)");
        var p = t.Apply(new(20, 10));
        Assert.Equal(10, p.X, 9);
        Assert.Equal(20, p.Y, 9);
        Assert.Equal(new Vec2(7, 9), SvgImport.ParseTransform("matrix(1,0,0,1,2,3)").Apply(new(5, 6)));
    }

    [Fact]
    public void SkipsHiddenDefsAndReportsText()
    {
        var r = SvgImport.ParseString("""
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10" width="10mm" height="10mm">
              <defs><rect id="r" width="2" height="2"/></defs>
              <rect width="1" height="1" style="display:none"/>
              <g visibility="hidden"><circle r="1"/></g>
              <text>hi</text>
              <use href="#r" x="5" y="5"/>
            </svg>
            """);
        Assert.Single(r.Paths);
        Assert.Equal(new Rect2(5, 5, 7, 7), r.Paths[0].Geometry.Bounds());
        Assert.Contains(r.Warnings, w => w.Contains("Text"));
    }

    [Fact]
    public void ColoursFromStyleAndInheritance()
    {
        var r = SvgImport.ParseString("""
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10">
              <g style="stroke: rgb(0, 128, 255)"><path d="M0 0L1 1" fill="none"/></g>
              <path d="M0 0L1 1Z" fill="#0f0"/>
              <path d="M0 0L1 1Z" style="fill:blue;stroke:none"/>
            </svg>
            """);
        Assert.Equal([0x0080FFu, 0x00FF00u, 0x0000FFu], r.Paths.Select(p => p.Color!.Value));
    }

    [Fact]
    public void SelfReferencingUseDoesNotHang()
    {
        var r = SvgImport.ParseString("""
            <svg xmlns="http://www.w3.org/2000/svg"><g id="a"><use href="#a"/><rect width="1" height="1"/></g></svg>
            """);
        Assert.NotEmpty(r.Paths);
    }

    [Fact]
    public void RejectsNonSvg() =>
        Assert.Throws<FormatException>(() => SvgImport.ParseString("<html/>"));
}

public class DocumentTests
{
    static PathShape Sq(Guid layer, double x, double y, double s = 10) =>
        new() { LayerId = layer, Geometry = PathGeometry.Rect(x, y, s, s) };

    [Fact]
    public void UndoRedoAndNoOpEditsAreNotRecorded()
    {
        var d = new Document();
        var a = Sq(d.ActiveLayer, 0, 0);
        d.Commit("Add", Ops.AddShapes(d.State, [a]));
        d.Commit("Nothing", Ops.Move(d.State, new HashSet<Guid> { a.Id }, 0, 0));
        Assert.Equal("Add", d.UndoLabel);
        d.Commit("Move", Ops.Move(d.State, new HashSet<Guid> { a.Id }, 5, 0));
        Assert.Equal(5, d.State.Shapes[0].Bounds.MinX, 9);
        d.Undo();
        Assert.Equal(0, d.State.Shapes[0].Bounds.MinX, 9);
        d.Redo();
        Assert.Equal(5, d.State.Shapes[0].Bounds.MinX, 9);
        d.Undo();
        d.Undo();
        Assert.Empty(d.State.Shapes);
        Assert.False(d.CanUndo);
    }

    [Fact]
    public void LiveEditIsOneUndoStepAndCancelRestores()
    {
        var d = new Document();
        var a = Sq(d.ActiveLayer, 0, 0);
        d.Commit("Add", Ops.AddShapes(d.State, [a]), [a.Id]);
        var ids = new HashSet<Guid> { a.Id };
        var start = d.State;
        d.BeginEdit();
        for (int i = 1; i <= 10; i++) d.UpdateEdit(Ops.Move(start, ids, i, 0));
        d.EndEdit("Move");
        Assert.Equal(10, d.State.Shapes[0].Bounds.MinX, 9);
        d.Undo();
        Assert.Equal(0, d.State.Shapes[0].Bounds.MinX, 9);

        d.BeginEdit();
        d.UpdateEdit(Ops.Move(d.State, ids, 50, 0));
        d.CancelEdit();
        Assert.Equal(0, d.State.Shapes[0].Bounds.MinX, 9);
    }

    [Fact]
    public void SelectionDropsDeletedShapes()
    {
        var d = new Document();
        var a = Sq(d.ActiveLayer, 0, 0);
        d.Commit("Add", Ops.AddShapes(d.State, [a]), [a.Id]);
        d.Commit("Delete", Ops.Delete(d.State, new HashSet<Guid> { a.Id }));
        Assert.Empty(d.Selection);
    }

    [Fact]
    public void DeletingLayersMovesOrRemovesShapesAndKeepsOneLayer()
    {
        var s = DocState.New();
        var l1 = s.Layers[0].Id;
        var l2 = new Layer { Name = "Two" };
        s = Ops.AddLayer(s, l2);
        s = Ops.AddShapes(s, [Sq(l1, 0, 0), Sq(l2.Id, 5, 5)]);
        var moved = Ops.DeleteLayers(s, new HashSet<Guid> { l2.Id }, l1);
        Assert.Equal(2, moved.ShapesOn(l1).Count());
        var removed = Ops.DeleteLayers(s, new HashSet<Guid> { l2.Id }, null);
        Assert.Single(removed.Shapes);
        var all = Ops.DeleteLayers(s, new HashSet<Guid> { l1, l2.Id }, null);
        Assert.Single(all.Layers);
        Assert.Empty(all.Shapes);
    }

    [Fact]
    public void BulkLayerEditAndMerge()
    {
        var s = DocState.New();
        var a = new Layer { Name = "A" };
        var b = new Layer { Name = "B" };
        s = Ops.AddLayer(Ops.AddLayer(s, a), b);
        s = Ops.AddShapes(s, [Sq(a.Id, 0, 0), Sq(b.Id, 5, 5)]);
        var edited = Ops.UpdateLayers(s, new HashSet<Guid> { a.Id, b.Id }, l => l with { Laser = l.Laser with { Power = 33 } });
        Assert.All(edited.Layers.Where(l => l.Name is "A" or "B"), l => Assert.Equal(33, l.Laser.Power));
        var merged = Ops.MergeLayers(s, [b.Id, a.Id]);
        Assert.Equal(2, merged.Layers.Count);
        Assert.Equal(2, merged.ShapesOn(a.Id).Count()); // a comes first in layer order
    }

    [Fact]
    public void CombineAndBreakApart()
    {
        var s = DocState.New();
        var l = s.Layers[0].Id;
        var x = Sq(l, 0, 0);
        var y = Sq(l, 20, 0);
        s = Ops.AddShapes(s, [x, y]);
        var (c, id) = Ops.Combine(s, new HashSet<Guid> { x.Id, y.Id });
        Assert.Single(c.Shapes);
        var (broken, ids) = Ops.BreakApart(c, new HashSet<Guid> { id!.Value });
        Assert.Equal(2, ids.Count);
        Assert.Equal(2, broken.Shapes.Count);
    }

    [Fact]
    public void AngleIsAbsoluteAndZeroRestoresTheOriginal()
    {
        var s = DocState.New();
        var sq = Sq(s.Layers[0].Id, 10, 10, 20);
        s = Ops.AddShapes(s, [sq]);
        var ids = new HashSet<Guid> { sq.Id };
        var at45 = Ops.SetRotation(s, ids, 45);
        Assert.Equal(45, at45.Shapes[0].Transform.RotationDeg, 9);
        var at30 = Ops.SetRotation(at45, ids, 30); // absolute, not +30
        Assert.Equal(30, at30.Shapes[0].Transform.RotationDeg, 9);
        var back = Ops.SetRotation(at30, ids, 0);
        Assert.Equal(0, back.Shapes[0].Transform.RotationDeg, 9);
        Assert.Equal(sq.Bounds.MinX, back.Shapes[0].Bounds.MinX, 6);
        Assert.Equal(sq.Bounds.MinY, back.Shapes[0].Bounds.MinY, 6);
        Assert.Same(back, Ops.SetRotation(back, ids, 0)); // no-op isn't an undo step
    }

    [Fact]
    public void ScaleIsRelativeToTheOriginalAndKeepsTheCentre()
    {
        var s = DocState.New();
        var sq = Sq(s.Layers[0].Id, 10, 10, 20);
        s = Ops.AddShapes(s, [sq]);
        var ids = new HashSet<Guid> { sq.Id };
        var half = Ops.SetScale(s, ids, 0.5, 0.5);
        Assert.Equal(10, half.Shapes[0].Bounds.Width, 6);
        Assert.Equal(sq.Bounds.Center, half.Shapes[0].Bounds.Center);
        // Rotated shapes scale along their own axes: no skew, angle unchanged.
        var turned = Ops.SetRotation(s, ids, 30);
        var wide = Ops.SetScale(turned, ids, 2, 1);
        var t = wide.Shapes[0].Transform;
        Assert.Equal(30, t.RotationDeg, 9);
        Assert.Equal(2, t.ScaleX, 9);
        Assert.Equal(1, t.ScaleY, 9);
        Assert.Equal(0, t.A * t.C + t.B * t.D, 9); // axes stay perpendicular
    }

    [Fact]
    public void HitTestStrokeAndInside()
    {
        var sq = Sq(Guid.Empty, 10, 10);
        Assert.True(sq.HitTest(new(10.01, 15), 0.1, insideCounts: false));
        Assert.False(sq.HitTest(new(15, 15), 0.1, insideCounts: false));
        Assert.True(sq.HitTest(new(15, 15), 0.1, insideCounts: true));
        Assert.True(sq.Touches(new Rect2(9, 9, 11, 11)));
        Assert.False(sq.Touches(new Rect2(12, 12, 18, 18))); // entirely inside, touching no edge
    }

    [Fact]
    public void FileRoundTrip()
    {
        var s = DocState.New();
        var l = s.Layers[0] with { Mode = LayerMode.Fill, Laser = LaserSettings.Red(12.5, 300, 60, 115).WithPasses(3), LinesPerCm = 80 };
        s = s with { Layers = [l] };
        var sh = new PathShape { LayerId = l.Id, Name = "e", Geometry = PathGeometry.Ellipse(0, 0, 5, 3), Transform = Affine.Translate(50, 60) };
        s = Ops.AddShapes(s, [sh]);
        var back = DocFile.Deserialize(DocFile.Serialize(s));
        Assert.Equal(l, back.Layers[0]);
        var b2 = (PathShape)back.Shapes[0];
        Assert.Equal(sh.Id, b2.Id);
        Assert.Equal(sh.Transform, b2.Transform);
        Assert.Equal(sh.Bounds.MinX, b2.Bounds.MinX, 6);
        Assert.Throws<FormatException>(() => DocFile.Deserialize("{\"format\":\"photoncanvas\",\"version\":99}"));
        Assert.Throws<FormatException>(() => DocFile.Deserialize("not json"));
        Assert.Throws<FormatException>(() => DocFile.Deserialize("{\"format\":\"something else\",\"version\":1}"));
        // Projects saved before the rename (format "ytool") still open.
        var legacy = DocFile.Serialize(s).Replace("\"photoncanvas\"", "\"ytool\"");
        Assert.Contains("\"ytool\"", legacy);
        Assert.Equal(l, DocFile.Deserialize(legacy).Layers[0]);
    }
}

public class JobBuilderTests
{
    static PathShape Sq(Guid layer, double x, double y, double s) =>
        new() { LayerId = layer, Geometry = PathGeometry.Rect(x, y, s, s) };

    [Fact]
    public void InnerPathsAreCutBeforeTheirOutline()
    {
        var s = DocState.New();
        var l = s.Layers[0].Id;
        var outer = Sq(l, 10, 10, 50);
        var eye1 = Sq(l, 20, 20, 5);
        var eye2 = Sq(l, 40, 20, 5);
        s = Ops.AddShapes(s, [outer, eye1, eye2]); // outline drawn first
        var job = JobBuilder.Build(s, new JobOptions());
        Assert.Equal(3, job.Steps.Count);
        Assert.Equal(new Vec2(10, 10), job.Steps[^1].Points[0]);
        Assert.Equal(new Rect2(10, 10, 60, 60), job.Bounds);
    }

    [Fact]
    public void WarnsAboutHiddenLayersOpenFillsAndCutOrder()
    {
        var s = DocState.New();
        var cut = s.Layers[0];
        var fill = new Layer { Name = "Engrave", Mode = LayerMode.Fill, Laser = LaserSettings.Blue(30, 400) };
        var hidden = new Layer { Name = "Ghost", Visible = false };
        s = Ops.AddLayer(Ops.AddLayer(s, fill), hidden);
        s = Ops.AddShapes(s,
        [
            Sq(cut.Id, 10, 10, 10),
            Sq(fill.Id, 30, 30, 10),
            new PathShape { LayerId = fill.Id, Geometry = PathGeometry.Line(new(50, 50), new(60, 60)) },
            Sq(hidden.Id, 100, 100, 5),
        ]);
        var job = JobBuilder.Build(s, new JobOptions());
        Assert.Contains(job.Warnings, w => w.Contains("Ghost"));
        Assert.Contains(job.Warnings, w => w.Contains("open path"));
        Assert.Contains(job.Warnings, w => w.Contains("runs after the cut layer"));
        Assert.DoesNotContain(job.Steps, st => st.Points.Any(p => p.X >= 100));
    }

    [Fact]
    public void ErrorsAreReadable()
    {
        var s = DocState.New();
        Assert.Contains("nothing on the canvas", Assert.Throws<JobException>(() => JobBuilder.Build(s, new())).Message);
        var l = s.Layers[0];
        s = Ops.AddShapes(s, [Sq(l.Id, 210, 10, 20)]);
        Assert.Throws<OutsideWorkAreaException>(() => JobBuilder.Build(s, new()));
        s = s with { Layers = [l with { Laser = l.Laser with { Speed = 0.5 } }] };
        var e = Assert.Throws<JobException>(() => JobBuilder.Build(s, new()));
        Assert.StartsWith("Layer \"Cut\"", e.Message);
    }

    [Fact]
    public void WholeJobPassesProduceRoundsWithDelay()
    {
        var s = DocState.New();
        var l = s.Layers[0];
        s = s with { Layers = [l with { Laser = l.Laser.WithPasses(3) }] };
        s = Ops.AddShapes(s, [Sq(l.Id, 10, 10, 10)]);
        var job = JobBuilder.Build(s, new JobOptions { PassDelayS = 2 });
        Assert.DoesNotContain("G104", job.Motion);
        Assert.Contains("G4P2000", job.Motion);
        var names = XfPackage.Read(job.Xf).Select(m => m.Name);
        Assert.Equal(["preview.jpg", "border.gcode", "motion.gcode", "description.json"], names);
        var stock = JobBuilder.Build(s, new JobOptions { WholeJobPasses = false });
        Assert.Contains("G104 P3", stock.Motion);
    }
}
