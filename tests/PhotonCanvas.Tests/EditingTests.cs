using PhotonCanvas.Core;
using PhotonCanvas.Doc;

namespace PhotonCanvas.Tests;

public class NodeEditTests
{
    static PathGeometry Square() => PathGeometry.Rect(0, 0, 10, 10); // closed, 4 anchors

    [Fact]
    public void ClosedSquareHasFourEditablePoints()
    {
        var g = Square();
        Assert.Equal(4, NodeEdit.Nodes(g).Count());
        // Moving the start point also moves the closing end of the last edge.
        var moved = NodeEdit.MoveAnchor(g, new NodeRef(0, 0), new Vec2(-2, -2));
        Assert.Equal(new Vec2(-2, -2), moved.Subpaths[0].Start);
        Assert.Equal(new Vec2(-2, -2), moved.Subpaths[0].Segs[^1].To);
    }

    [Fact]
    public void InsertSplitsWithoutChangingTheShape()
    {
        var circle = PathGeometry.Ellipse(0, 0, 10, 10);
        var g = NodeEdit.Insert(circle, 0, 1, 0.5);
        Assert.Equal(5, g.Subpaths[0].Segs.Length);
        foreach (var p in g.Flatten(0.01)[0].Points) Assert.InRange(p.Length, 9.99, 10.01);
    }

    [Fact]
    public void DeleteMergesNeighbours()
    {
        var g = NodeEdit.Delete(Square(), new NodeRef(0, 1)); // remove (10,0)
        Assert.Equal(3, NodeEdit.Nodes(g).Count());
        Assert.Equal(new Vec2(10, 10), g.Subpaths[0].Segs[0].To);
        var start = NodeEdit.Delete(Square(), new NodeRef(0, 0)); // remove the start of a closed loop
        Assert.Equal(3, NodeEdit.Nodes(start).Count());
        Assert.Equal(start.Subpaths[0].Start, start.Subpaths[0].Segs[^1].To);
        var line = PathGeometry.Line(new(0, 0), new(5, 0));
        Assert.Empty(NodeEdit.Delete(line, new NodeRef(0, 1)).Subpaths); // nothing left to draw
    }

    [Fact]
    public void SmoothAndSharpToggle()
    {
        var g = Square();
        var n = new NodeRef(0, 1);
        Assert.False(NodeEdit.IsSmooth(g, n));
        var smooth = NodeEdit.ToggleSmooth(g, n);
        Assert.True(NodeEdit.IsSmooth(smooth, n));
        var mirrored = NodeEdit.MoveHandle(smooth, n, HandleSide.Out, new Vec2(13, 3), mirror: true);
        Assert.True(NodeEdit.IsSmooth(mirrored, n));
        var sharp = NodeEdit.ToggleSmooth(smooth, n);
        Assert.False(NodeEdit.IsSmooth(sharp, n));
    }

    [Fact]
    public void FindsTheNearestSegment()
    {
        var hit = NodeEdit.NearestOnSegment(Square(), new Vec2(5, 0.2))!.Value;
        Assert.Equal(0, hit.Seg);
        Assert.Equal(0.5, hit.T, 2);
    }
}

public class ArrangeTests
{
    static PathShape Sq(Guid layer, double x, double y, double s = 10) =>
        new() { LayerId = layer, Geometry = PathGeometry.Rect(x, y, s, s) };

    [Fact]
    public void GroupsSelectTogetherAndCopiesGetTheirOwnGroup()
    {
        var s = DocState.New();
        var l = s.Layers[0].Id;
        var a = Sq(l, 0, 0); var b = Sq(l, 20, 0); var c = Sq(l, 40, 0);
        s = Ops.AddShapes(s, [a, b, c]);
        var (grouped, gid) = Ops.Group(s, new HashSet<Guid> { a.Id, b.Id });
        Assert.Equal(new HashSet<Guid> { a.Id, b.Id }, Ops.WithGroups(grouped, [a.Id]));
        var (dup, ids) = Ops.Duplicate(grouped, new HashSet<Guid> { a.Id, b.Id }, 5, 5);
        var copyGroups = dup.Shapes.Where(x => ids.Contains(x.Id)).Select(x => x.GroupId).Distinct().ToList();
        Assert.Single(copyGroups);
        Assert.NotEqual(gid, copyGroups[0]);
        Assert.All(Ops.Ungroup(grouped, new HashSet<Guid> { a.Id, b.Id }).Shapes, x => Assert.Null(x.GroupId));
    }

    [Fact]
    public void DistributeMakesEqualGaps()
    {
        var s = DocState.New();
        var l = s.Layers[0].Id;
        var a = Sq(l, 0, 0, 10); var b = Sq(l, 12, 0, 10); var c = Sq(l, 60, 0, 10);
        s = Ops.AddShapes(s, [a, b, c]);
        var d = Ops.Distribute(s, new HashSet<Guid> { a.Id, b.Id, c.Id }, horizontal: true);
        var xs = d.Shapes.Select(x => x.Bounds.MinX).Order().ToList();
        Assert.Equal(0, xs[0], 9);
        Assert.Equal(30, xs[1], 9);
        Assert.Equal(60, xs[2], 9);
    }

    [Fact]
    public void GridAndCircularArrays()
    {
        var s = DocState.New();
        var a = Sq(s.Layers[0].Id, 10, 10, 10);
        s = Ops.AddShapes(s, [a]);
        var (grid, gids) = Ops.GridArray(s, new HashSet<Guid> { a.Id }, 3, 2, 5, 5);
        Assert.Equal(6, gids.Count);
        Assert.Equal(new Rect2(10, 10, 50, 35), grid.Bounds(gids));
        var (ring, rids) = Ops.CircularArray(s, new HashSet<Guid> { a.Id }, 4, new Vec2(15, 50), 360, rotateCopies: true);
        Assert.Equal(4, rids.Count);
        // Opposite copy is mirrored through the centre.
        var opposite = ring.Shapes.Single(x => x.Id == rids[2]).Bounds.Center;
        Assert.Equal(15, opposite.X, 6);
        Assert.Equal(85, opposite.Y, 6);
    }
}

public class TextTests
{
    [Fact]
    public void TextBecomesOutlinesOfTheRightSize()
    {
        var g = TextLayout.Build("H", new TextStyle(SizeMm: 10));
        var b = g.Bounds();
        Assert.False(g.IsEmpty);
        // A capital H is roughly 0.7 em tall.
        Assert.InRange(b.Height, 6, 8.5);
        Assert.InRange(b.MinY, 1, 4); // top-left of the block at (0, 0)
        Assert.True(TextLayout.Build("", new TextStyle()).IsEmpty);
    }

    [Fact]
    public void SpacingAlignmentAndLines()
    {
        double W(string t, TextStyle s) => TextLayout.Build(t, s).Bounds().Width;
        var plain = new TextStyle(SizeMm: 10);
        Assert.True(W("HHHH", plain with { LetterSpacingMm = 2 }) > W("HHHH", plain) + 5.5);
        var two = TextLayout.Build("H\nH", plain).Bounds();
        Assert.True(two.Height > 18);
        // Right-aligned: the short second line ends where the long first line ends.
        var right = TextLayout.Build("HHHH" + "\n" + "H", plain with { Align = TextAlign.Right }).Subpaths;
        double End(Subpath sp) => Rect2.Bounds(sp.Flatten(0.05)).MaxX;
        Assert.Equal(End(right[3]), End(right[4]), 1);
    }

    [Fact]
    public void BentTextFollowsAnArc()
    {
        var straight = TextLayout.Build("HHHHHHHH", new TextStyle(SizeMm: 10)).Bounds();
        var arch = TextLayout.Build("HHHHHHHH", new TextStyle(SizeMm: 10, BendDeg: 120)).Bounds();
        Assert.True(arch.Height > straight.Height + 5);
        // Arched up: the middle letters sit higher (smaller Y) than the end letters.
        var subs = TextLayout.Build("HHHHHHHH", new TextStyle(SizeMm: 10, BendDeg: 120)).Subpaths;
        double Top(Subpath sp) => Rect2.Bounds(sp.Flatten(0.05)).MinY;
        Assert.True(Top(subs[3]) < Top(subs[0]) - 5);
        var smile = TextLayout.Build("HHHHHHHH", new TextStyle(SizeMm: 10, BendDeg: -120)).Subpaths;
        Assert.True(Top(smile[3]) > Top(smile[0]) + 5);
    }

    [Fact]
    public void TextStaysEditableThroughSaveAndLoad()
    {
        var s = DocState.New();
        var t = TextShape.Create("Hello", new TextStyle(SizeMm: 8, Bold: true, BendDeg: 30), s.Layers[0].Id, Affine.Translate(20, 30));
        var back = DocFile.Deserialize(DocFile.Serialize(Ops.AddShapes(s, [t])));
        var bt = Assert.IsType<TextShape>(Assert.Single(back.Shapes));
        Assert.Equal("Hello", bt.Text);
        Assert.Equal(t.Style, bt.Style);
        Assert.Equal(t.Bounds.MinX, bt.Bounds.MinX, 4);
        var edited = bt.Edit("Hi", bt.Style);
        Assert.Equal(bt.Id, edited.Id);
        Assert.True(edited.Bounds.Width < bt.Bounds.Width);
        Assert.IsNotType<TextShape>(edited.Baked()); // convert to paths
    }
}

public class MaterialTestTests
{
    [Fact]
    public void OneLayerPerSquareWithItsSettings()
    {
        var s0 = DocState.New();
        var o = new MaterialTestOptions { AcrossMin = 10, AcrossMax = 90, AcrossSteps = 3, DownMin = 100, DownMax = 300, DownSteps = 2 };
        var (s, added) = MaterialTest.Add(s0, o);
        var squares = s.Layers.Skip(s0.Layers.Count).Where(l => l.Name.StartsWith("Test ") && l.Name != "Test labels").ToList();
        Assert.Equal(6, squares.Count);
        Assert.All(squares, l => Assert.Equal(LayerMode.Fill, l.Mode));
        Assert.Equal([10.0, 50.0, 90.0], squares.Take(3).Select(l => l.Laser.Power));
        Assert.Equal([100.0, 300.0], squares.Where(l => l.Laser.Power == 10).Select(l => l.Laser.Speed));
        // Each square layer holds exactly one square, power increasing left to right.
        var firstRow = squares.Take(3).Select(l => s.Shapes.Single(x => x.LayerId == l.Id).Bounds.MinX).ToList();
        Assert.True(firstRow[0] < firstRow[1] && firstRow[1] < firstRow[2]);
        // Labels: 3 power values + 2 speed values + 2 axis names.
        var labels = s.Layers.Single(l => l.Name == "Test labels");
        Assert.Equal(7, s.Shapes.Count(x => x.LayerId == labels.Id));
        Assert.Equal(6 + 7, added.Count);
        var b = MaterialTest.Bounds(s0, o);
        Assert.True(b.MinX >= 10 && b.MinY >= 10 && b.MaxX < 100 && b.MaxY < 100);
    }

    [Fact]
    public void StepsAreEvenAndInclusive()
    {
        Assert.Equal([10.0, 30.0, 50.0], MaterialTest.Steps(10, 50, 3));
        Assert.Equal([25.0], MaterialTest.Steps(25, 80, 1));
    }
}

public class FrameTests
{
    static DocState Design(params Shape[] shapes)
    {
        var s = DocState.New();
        return Ops.AddShapes(s, shapes.Select(x => (Shape)((PathShape)x).With(layerId: s.Layers[0].Id)));
    }

    [Fact]
    public void RectangleFrameIsTheJobBorder()
    {
        var job = JobBuilder.Build(Design(new PathShape { Geometry = PathGeometry.Ellipse(100, 100, 20, 10) }), new JobOptions());
        Assert.Equal(job.Border, JobBuilder.FrameGcode(job, FrameShape.Rectangle));
    }

    [Fact]
    public void OutlineFrameHugsTheDesign()
    {
        // A diamond: its outline frame should touch the four tips, not the box corners.
        var diamond = PathGeometry.FromSvgPathData("M100 80 L120 100 L100 120 L80 100 Z");
        var job = JobBuilder.Build(Design(new PathShape { Geometry = diamond }), new JobOptions());
        var g = JobBuilder.FrameGcode(job, FrameShape.Outline);
        Assert.Contains("X100Y80", g);
        Assert.Contains("X120Y100", g);
        Assert.DoesNotContain("X120Y80", g); // a box corner
        Assert.StartsWith("# GS004-4 HEAD", g);
        Assert.Contains("S64F960000", g); // same faint framing power as the job border
    }

    [Fact]
    public void ConvexHullDropsInnerPoints()
    {
        var hull = JobBuilder.ConvexHull([new(0, 0), new(10, 0), new(10, 10), new(0, 10), new(5, 5), new(5, 0)]);
        Assert.Equal(4, hull.Count);
        Assert.DoesNotContain(new Vec2(5, 5), hull);
    }

    [Fact]
    public void WarnsAboutShapesStackedOnTopOfEachOther()
    {
        var a = new PathShape { Name = "Combined", Geometry = PathGeometry.Rect(10, 10, 50, 20) };
        var b = new PathShape { Name = "Traced", Geometry = PathGeometry.Rect(10, 10, 50, 20) };
        var c = new PathShape { Name = "Other", Geometry = PathGeometry.Rect(100, 10, 50, 20) };
        var job = JobBuilder.Build(Design(a, b, c), new JobOptions());
        var w = Assert.Single(job.Warnings, x => x.Contains("same place"));
        Assert.Contains("Combined", w);
        Assert.Contains("Traced", w);
        Assert.DoesNotContain(job.Warnings, x => x.Contains("Other"));
    }

    [Fact]
    public void PreviewIsCentred()
    {
        // A wide, short design: equal white space above and below.
        var jpg = XfPackage.PreviewJpeg([[new Vec2(0, 100), new Vec2(200, 100), new Vec2(200, 120), new Vec2(0, 120), new Vec2(0, 100)]]);
        using var bmp = SkiaSharp.SKBitmap.Decode(jpg);
        int Dark(int y) => Enumerable.Range(0, bmp.Width).Count(x => bmp.GetPixel(x, y).Red < 128);
        int top = Enumerable.Range(0, bmp.Height).First(y => Dark(y) > 0);
        int bottom = Enumerable.Range(0, bmp.Height).Last(y => Dark(y) > 0);
        Assert.InRange(top + bottom, bmp.Height - 6, bmp.Height + 4);
    }
}

public class ShapeLibraryTests
{
    [Fact]
    public void EveryBuiltInShapeDrawsAndFitsItsBox()
    {
        var all = ShapeLibrary.BuiltIn;
        Assert.True(all.Count >= 50, $"{all.Count} shapes");
        Assert.Equal(all.Count, all.Select(s => s.Name).Distinct().Count());
        Assert.All(all, s =>
        {
            Assert.False(s.Geometry.IsEmpty, s.Name);
            var b = s.Geometry.Bounds(0.05);
            if (!s.RealSize) Assert.InRange(Math.Max(b.Width, b.Height), 99.5, 100.5);
            Assert.True(b.MinX > -0.5 && b.MinY > -0.5, $"{s.Name} at {b}");
        });
        Assert.Contains(ShapeLibrary.UserCategory, ShapeLibrary.Categories);
    }

    [Fact]
    public void SavesAndLoadsTheUsersShapes()
    {
        var dir = Path.Combine(Path.GetTempPath(), "photoncanvas-shapes-" + Guid.NewGuid().ToString("N"));
        try
        {
            var s = DocState.New();
            var shapes = new[] { new PathShape { LayerId = s.Layers[0].Id, Geometry = PathGeometry.Rect(10, 10, 40, 20) }, new PathShape { LayerId = s.Layers[0].Id, Geometry = PathGeometry.Ellipse(80, 20, 10, 10) } };
            var f1 = ShapeLibrary.SaveUser(dir, "My/badge?", shapes);
            var f2 = ShapeLibrary.SaveUser(dir, "My/badge?", shapes);
            Assert.NotEqual(f1, f2); // never overwrites
            var mine = ShapeLibrary.LoadUser(dir);
            Assert.Equal(2, mine.Count);
            Assert.All(mine, m => { Assert.True(m.IsUser); Assert.Equal(2, m.Geometry.Subpaths.Length); Assert.Equal(100, m.Geometry.Bounds(0.05).Width, 0); });
            File.WriteAllText(Path.Combine(dir, "broken" + DocFile.Extension), "nope");
            Assert.Equal(2, ShapeLibrary.LoadUser(dir).Count);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
