using PhotonCanvas.Addon.Stencil;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Geometry;
using Xunit;

namespace PhotonCanvas.Tests;

public class StencilTests
{
    static List<IReadOnlyList<Vec2>> Loops(PathGeometry g) => [.. g.Flatten(0.02).Where(f => f.Closed).Select(f => f.Points)];
    static double Area(PathGeometry g) => ShapeGeometry.Area(g);

    /// <summary>An "O": a ring <paramref name="r"/> out, <paramref name="ri"/> in.</summary>
    static PathGeometry Ring(double cx, double cy, double r, double ri) =>
        new([.. PathGeometry.Ellipse(cx, cy, r, r).Subpaths, .. PathGeometry.Ellipse(cx, cy, ri, ri).Subpaths]);

    /// <summary>Cut as drawn, is the point in a piece that falls out (some outline goes all the way round it)?</summary>
    static bool FallsOut(PathGeometry cut, Vec2 p) => Loops(cut).Any(l => Geom.Inside(l, p));

    [Fact]
    public void AnOsMiddleGetsTwoBridgesOnOppositeSides()
    {
        var o = Ring(0, 0, 10, 6);
        var sheet = new StencilSheet([o]);
        Assert.Single(sheet.Middles);
        var bridges = sheet.AutoBridges(new StencilOptions { WidthMm = 1 });
        Assert.Equal(2, bridges.Count);
        // Straight across the 4 mm ring, a little into the sheet at each end.
        foreach (var b in bridges) Assert.InRange(b.Length, 4, 4.5);
        // Spread apart: on opposite sides of the middle.
        Assert.True(bridges[0].Middle.Dist(bridges[1].Middle) > 14);
        var cut = StencilSheet.Cut(o, bridges);
        Assert.True(FallsOut(o, new Vec2(0, 0)));
        Assert.False(FallsOut(cut, new Vec2(0, 0)));
        Assert.Equal(2, Loops(cut).Count); // the ring in two halves
        double ring = Math.PI * (10 * 10 - 6 * 6);
        Assert.InRange(Area(cut), ring - 2 * 4 * 1 - 0.5, ring - 2 * 4 * 1 + 0.5);
        Assert.Empty(sheet.Check(bridges).Loose);
    }

    [Fact]
    public void OneBridgeEachWhenAsked()
    {
        var sheet = new StencilSheet([Ring(0, 0, 10, 6)]);
        var one = sheet.AutoBridges(new StencilOptions { WidthMm = 1, PerMiddle = 1 });
        Assert.Single(one);
        Assert.Empty(sheet.Check(one).Loose);
        Assert.Single(sheet.Check([]).Loose);
    }

    [Fact]
    public void UpAndDownBridgesAreVertical()
    {
        var sheet = new StencilSheet([Ring(0, 0, 10, 6)]);
        var bridges = sheet.AutoBridges(new StencilOptions { WidthMm = 1.5, Direction = BridgeDirection.UpDown });
        Assert.Equal(2, bridges.Count);
        foreach (var b in bridges)
        {
            Assert.InRange(Math.Abs(b.A.X - b.B.X), 0, 1e-6);
            Assert.InRange(Math.Abs(b.Middle.X), 0, 1.5);
        }
        Assert.Contains(bridges, b => b.Middle.Y < 0);
        Assert.Contains(bridges, b => b.Middle.Y > 0);
    }

    [Fact]
    public void TheShortestWayAcrossIsTaken()
    {
        // A "D"-ish letter: a 20 × 12 box with a hole leaving a 1 mm wall at the right, 4 mm elsewhere.
        var d = new PathGeometry([.. PathGeometry.Rect(0, 0, 20, 12).Subpaths, .. PathGeometry.Rect(4, 4, 15, 4).Subpaths]);
        var sheet = new StencilSheet([d]);
        var b = Assert.Single(sheet.AutoBridges(new StencilOptions { WidthMm = 0.8, PerMiddle = 1 }));
        Assert.InRange(b.Length, 1, 1.3);
        Assert.InRange(b.Middle.X, 19, 20);
    }

    [Fact]
    public void ShapesWithoutAMiddleAreLeftAlone()
    {
        var l = PathGeometry.Rect(0, 0, 10, 2);
        var sheet = new StencilSheet([l]);
        Assert.Empty(sheet.Middles);
        Assert.Empty(sheet.AutoBridges(new StencilOptions()));
    }

    [Fact]
    public void AMiddleInsideAMiddleIsHeldThroughIt()
    {
        // A ring inside a ring (a target): the inner middle can only reach the sheet through the outer one.
        var target = new PathGeometry([.. Ring(0, 0, 20, 16).Subpaths, .. Ring(0, 0, 10, 6).Subpaths]);
        var sheet = new StencilSheet([target]);
        Assert.Equal(2, sheet.Middles.Count);
        var bridges = sheet.AutoBridges(new StencilOptions { WidthMm = 1 });
        Assert.Equal(4, bridges.Count);
        Assert.Empty(sheet.Check(bridges).Loose);
        var cut = StencilSheet.Cut(target, bridges);
        Assert.False(FallsOut(cut, new Vec2(0, 0)));
        Assert.False(FallsOut(cut, new Vec2(0, 13)));
        Assert.False(FallsOut(cut, new Vec2(13, 0)));
    }

    [Fact]
    public void WithAnEdgeTheStencilIsInsideIt()
    {
        var edge = PathGeometry.Rect(-30, -20, 60, 40);
        var sheet = new StencilSheet([Ring(0, 0, 10, 6)], edge);
        Assert.Single(sheet.Middles);
        var bridges = sheet.AutoBridges(new StencilOptions());
        Assert.Empty(sheet.Check(bridges).Loose);
    }

    [Fact]
    public void ABridgeCanBeClickedInAndCheckedOut()
    {
        var sheet = new StencilSheet([Ring(0, 0, 10, 6)]);
        // In the ring, on the right: straight across it.
        var b = sheet.BridgeAt(new Vec2(8, 0), new StencilOptions { WidthMm = 1 });
        Assert.NotNull(b);
        Assert.InRange(b!.Length, 4, 4.5);
        Assert.InRange(Math.Abs(b.A.Y - b.B.Y), 0, 0.3);
        Assert.Empty(sheet.Check([b]).Loose);
        // In the middle or out on the sheet: nothing to bridge.
        Assert.Null(sheet.BridgeAt(new Vec2(0, 0), new StencilOptions()));
        Assert.Null(sheet.BridgeAt(new Vec2(15, 0), new StencilOptions()));
    }

    [Fact]
    public void LongBridgesAreFlagged()
    {
        var sheet = new StencilSheet([Ring(0, 0, 30, 10)]);
        var bridges = sheet.AutoBridges(new StencilOptions { WidthMm = 1 });
        Assert.NotEmpty(sheet.Check(bridges).Long);
    }

    [Fact]
    public void LettersOfARealTextAreBridged()
    {
        var s = DocState.New();
        var t = TextShape.Create("OAR8", new TextStyle(SizeMm: 20, Bold: true), s.Layers[0].Id, Affine.Translate(20, 30));
        var sheet = new StencilSheet([t.World]);
        Assert.True(sheet.Middles.Count >= 4, $"middles: {sheet.Middles.Count}"); // O, A, R, and two in 8
        var bridges = sheet.AutoBridges(new StencilOptions { WidthMm = 1 });
        Assert.Empty(sheet.Check(bridges).Loose);
        foreach (var b in bridges) Assert.True(b.Length < 8, $"bridge {b.Length:0.00} mm long");
    }

    [Fact]
    public void BridgingATextKeepsItSoItCanComeBack()
    {
        var s = DocState.New();
        var t = TextShape.Create("O", new TextStyle(SizeMm: 20), s.Layers[0].Id, Affine.Translate(20, 30));
        var other = new PathShape { LayerId = s.Layers[0].Id, Geometry = PathGeometry.Rect(60, 30, 10, 2) };
        s = Ops.AddShapes(s, [t, other]);
        var input = StencilEdit.Gather(s, new HashSet<Guid> { t.Id, other.Id });
        Assert.Null(input.Edge);
        var sheet = new StencilSheet(input.Letters.Select(l => l.World));
        var bridges = sheet.AutoBridges(new StencilOptions());
        var done = StencilEdit.Apply(s, input, bridges, new StencilOptions());
        var bridged = Assert.IsType<PathShape>(done.FindShape(t.Id));
        Assert.IsNotType<TextShape>(bridged);
        Assert.True(StencilEdit.IsBridged(bridged));
        Assert.Same(other, done.FindShape(other.Id)); // nothing crossed it
        Assert.Equal(s.Shapes.Count, done.Shapes.Count);

        // Moved afterwards, the bridges and the text move with it.
        var moved = Ops.Move(done, new HashSet<Guid> { t.Id }, 5, 0);
        var again = StencilEdit.Gather(moved, new HashSet<Guid> { t.Id });
        Assert.Contains(t.Id, again.Bridged);
        var back = Assert.IsType<TextShape>(Assert.Single(again.Letters));
        Assert.InRange(back.Bounds.MinX - t.Bounds.MinX, 4.99, 5.01);
        Assert.Equal(bridges.Count, again.Existing.Count);
        Assert.InRange(again.Existing[0].A.X - bridges[0].A.X, 4.99, 5.01);
        Assert.Equal(new StencilOptions(), again.Options);

        // Taken out, it is a text again, where it now is.
        var undone = StencilEdit.TakeOut(moved, [t.Id]);
        var text = Assert.IsType<TextShape>(undone.FindShape(t.Id));
        Assert.Equal("O", text.Text);
        Assert.InRange(text.Bounds.MinX - t.Bounds.MinX, 4.99, 5.01);
    }

    [Fact]
    public void AMiddleDrawnAsItsOwnShapeIsJoinedToItsLetter()
    {
        var s = DocState.New();
        var outer = new PathShape { LayerId = s.Layers[0].Id, Geometry = PathGeometry.Ellipse(0, 0, 10, 10), Name = "outer" };
        var inner = new PathShape { LayerId = s.Layers[0].Id, Geometry = PathGeometry.Ellipse(0, 0, 6, 6), Name = "inner" };
        s = Ops.AddShapes(s, [outer, inner]);
        var input = StencilEdit.Gather(s, new HashSet<Guid> { outer.Id, inner.Id });
        Assert.Null(input.Edge); // the outer circle is a letter here, with the inner inside it
        var sheet = new StencilSheet(input.Letters.Select(l => l.World), input.Edge?.World);
        var bridges = sheet.AutoBridges(new StencilOptions());
        Assert.Equal(2, bridges.Count);
        var done = StencilEdit.Apply(s, input, bridges, new StencilOptions());
        var one = Assert.Single(done.Shapes);
        Assert.Equal(outer.Id, one.Id);
        Assert.False(FallsOut(((PathShape)one).World, new Vec2(0, 0)));
        // And back to the two circles.
        var undone = StencilEdit.TakeOut(done, [outer.Id]);
        Assert.Equal(2, undone.Shapes.Count);
        Assert.Equal(["outer", "inner"], undone.Shapes.Select(x => x.Name));
    }

    [Fact]
    public void AnOutlineRoundEverythingIsTheEdge()
    {
        var s = DocState.New();
        var plate = new PathShape { LayerId = s.Layers[0].Id, Geometry = PathGeometry.Rect(-30, -20, 60, 40) };
        var o = new PathShape { LayerId = s.Layers[0].Id, Geometry = Ring(0, 0, 10, 6) };
        s = Ops.AddShapes(s, [plate, o]);
        var input = StencilEdit.Gather(s, new HashSet<Guid> { plate.Id, o.Id });
        Assert.Same(plate, input.Edge);
        Assert.Same(o, Assert.Single(input.Letters));
        // A lone O is never its own edge.
        Assert.Null(StencilEdit.Gather(s, new HashSet<Guid> { o.Id }).Edge);
    }

    [Fact]
    public void ACopyOfABridgedShapeComesBackOnItsOwn()
    {
        var s = DocState.New();
        var o = new PathShape { LayerId = s.Layers[0].Id, Geometry = Ring(0, 0, 10, 6) };
        s = Ops.AddShapes(s, [o]);
        var input = StencilEdit.Gather(s, new HashSet<Guid> { o.Id });
        var sheet = new StencilSheet(input.Letters.Select(l => l.World));
        s = StencilEdit.Apply(s, input, sheet.AutoBridges(new StencilOptions()), new StencilOptions());
        var copy = s.Shapes[0].With(id: Guid.NewGuid(), transform: Affine.Translate(40, 0));
        s = Ops.AddShapes(s, [copy]);
        var both = StencilEdit.TakeOut(s, [o.Id, copy.Id]);
        Assert.Equal(2, both.Shapes.Select(x => x.Id).Distinct().Count());
        Assert.All(both.Shapes, x => Assert.False(StencilEdit.IsBridged(x)));
        Assert.InRange(both.Shapes[1].Bounds.MinX - both.Shapes[0].Bounds.MinX, 39.99, 40.01);
    }
}
