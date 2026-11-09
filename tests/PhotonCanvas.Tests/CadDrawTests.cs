using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Geometry;

namespace PhotonCanvas.Tests;

/// <summary>CAD-style drawing: circles by centre, points and tangents; rectangles at any angle; polygons, arcs, centrelines.</summary>
public class CadDrawTests
{
    static CadShape Make(DrawMethod m, CadOptions? o = null, params CadPick[] picks) =>
        CadDraw.Build(m, picks[..^1], picks[^1], o) ?? throw new Xunit.Sdk.XunitException($"{m} made nothing");

    static CadPick At(double x, double y) => new(new Vec2(x, y));

    /// <summary>The corners of a shape's outline (its line ends), on the work area.</summary>
    static List<Vec2> Corners(CadShape s) => [.. s.Geometry.Subpaths[0].Segs.Select(x => x.To)];

    static void Near(Vec2 want, Vec2 got, int digits = 6)
    {
        Assert.Equal(want.X, got.X, digits);
        Assert.Equal(want.Y, got.Y, digits);
    }

    /// <summary>Every point of the outline is <paramref name="r"/> from <paramref name="c"/> (as near as curves draw a circle: 0.03 %).</summary>
    static void IsCircle(CadShape s, Vec2 c, double r)
    {
        foreach (var f in s.Geometry.Flatten(0.001))
            foreach (var p in f.Points) Assert.True(Math.Abs(p.Dist(c) - r) <= 3e-4 * r + 1e-6, $"{p} is {p.Dist(c)} from {c}, not {r}");
    }

    [Fact]
    public void CirclesByCentreRadiusDiameterAndPoints()
    {
        var byRadius = Make(DrawMethod.CircleCentreRadius, null, At(10, 10), At(13, 14));
        Assert.Equal(ParamKind.Ellipse, byRadius.Kind);
        Assert.Equal(10, byRadius.W, 9);
        IsCircle(byRadius, new(10, 10), 5);
        var byDiameter = Make(DrawMethod.CircleCentreDiameter, null, At(10, 10), At(13, 14));
        IsCircle(byDiameter, new(10, 10), 2.5);
        var twoPoints = Make(DrawMethod.Circle2Points, null, At(0, 0), At(6, 8));
        IsCircle(twoPoints, new(3, 4), 5);
        // Through three points: (0, 0), (10, 0) and (0, 10) lie on the circle round (5, 5).
        var three = Make(DrawMethod.Circle3Points, null, At(0, 0), At(10, 0), At(0, 10));
        IsCircle(three, new(5, 5), Math.Sqrt(50));
        // Three in a row make none.
        Assert.Null(CadDraw.Build(DrawMethod.Circle3Points, [At(0, 0), At(5, 0)], At(10, 0)));
        // The shape it makes is an editable circle.
        var shape = Assert.IsType<ParamShape>(three.ToShape(Guid.NewGuid()));
        Assert.Equal(2 * Math.Sqrt(50), shape.SeenSize.W, 6);
    }

    [Fact]
    public void ACircleTouchingTwoLinesWithAGivenRadiusSitsInTheCornerClicked()
    {
        // The x axis and the y axis (Y down): a 5 mm circle in the corner clicked near (8, 0) and (0, 8) sits at (5, 5).
        var x = new LineTarget(new(0, 0), new(20, 0));
        var y = new LineTarget(new(0, 0), new(0, 20));
        var o = new CadOptions { TangentRadiusMm = 5 };
        var c = Make(DrawMethod.CircleTanTanRadius, o, new CadPick(new(8, 0), x), new CadPick(new(0, 8), y));
        IsCircle(c, new(5, 5), 5);
        // Clicked on the other sides of the lines' crossing: the circle goes in that corner.
        var c2 = Make(DrawMethod.CircleTanTanRadius, o, new CadPick(new(-8, 0), x), new CadPick(new(0, 8), y));
        IsCircle(c2, new(-5, 5), 5);
        // All four corners are found.
        Assert.Equal(4, CadDraw.TangentCircles(x, y, 5).Count);
    }

    [Fact]
    public void ACircleTouchingALineAndACircleOrTwoCircles()
    {
        // A circle of radius 10 at (0, 0) and the line y = 20: a 5 mm circle touching both, between them, sits at y = 15, x = ±√(15² − 15²)… = 0.
        var circle = new CircleTarget(new(0, 0), 10);
        var line = new LineTarget(new(-50, 20), new(50, 20));
        var o = new CadOptions { TangentRadiusMm = 5 };
        var c = Make(DrawMethod.CircleTanTanRadius, o, new CadPick(new(0, 10), circle), new CadPick(new(0, 20), line));
        IsCircle(c, new(0, 15), 5);
        // Two circles 30 apart, radius 10 each: a 5 mm circle touching both outside, above the middle.
        var a = new CircleTarget(new(0, 0), 10);
        var b = new CircleTarget(new(30, 0), 10);
        var both = Make(DrawMethod.CircleTanTanRadius, o, new CadPick(new(8, -6), a), new CadPick(new(22, -6), b));
        // Its centre is 15 from each: (15, −√(15² − 15²)) = (15, 0)… the gap is exactly 10, so it sits in it.
        IsCircle(both, new(15, 0), 5);
        foreach (var (centre, r) in CadDraw.TangentCircles(a, b, 5))
        {
            Assert.Equal(5, r, 9);
            // Touching each: its centre is r ± 10 from theirs.
            Assert.Contains(new[] { 15.0, 5.0 }, d => Math.Abs(centre.Dist(a.Centre) - d) < 1e-6);
            Assert.Contains(new[] { 15.0, 5.0 }, d => Math.Abs(centre.Dist(b.Centre) - d) < 1e-6);
        }
    }

    [Fact]
    public void ACircleTouchingThreeThingsIsTheOneNearTheClicks()
    {
        // A 3-4-5 triangle's sides: the circle touching all three from inside (clicked inside) is its incircle, radius 1, at (1, 1).
        var bottom = new LineTarget(new(0, 0), new(4, 0));
        var left = new LineTarget(new(0, 0), new(0, 3));
        var slope = new LineTarget(new(4, 0), new(0, 3));
        var inc = Make(DrawMethod.CircleTanTanTan, null, new CadPick(new(1, 0), bottom), new CadPick(new(0, 1), left), new CadPick(new(1.6, 1.8), slope));
        IsCircle(inc, new(1, 1), 1);
        // Three equal circles in a row along a line: one touching all three…
        var a = new CircleTarget(new(0, 0), 2);
        var b = new CircleTarget(new(10, 0), 2);
        var c = new CircleTarget(new(5, 8), 2);
        foreach (var (centre, r) in CadDraw.TangentCircles(a, b, c))
            foreach (var t in new[] { a, b, c })
            {
                double d = centre.Dist(t.Centre);
                Assert.True(Math.Abs(d - (r + 2)) < 1e-6 || Math.Abs(d - Math.Abs(r - 2)) < 1e-6, $"{centre} r {r} doesn't touch {t}");
            }
        // …and two circles and a line: each answer touches all three.
        var floor = new LineTarget(new(-20, 10), new(30, 10));
        var found = CadDraw.TangentCircles(a, b, floor);
        Assert.NotEmpty(found);
        foreach (var (centre, r) in found)
        {
            Assert.Equal(r, Math.Abs(centre.Y - 10), 6);
            foreach (var t in new[] { a, b })
            {
                double d = centre.Dist(t.Centre);
                Assert.True(Math.Abs(d - (r + 2)) < 1e-6 || Math.Abs(d - Math.Abs(r - 2)) < 1e-6);
            }
        }
    }

    [Fact]
    public void RectanglesByCentreAndByThreePointsAtAnAngle()
    {
        var centred = Make(DrawMethod.RectCentreCorner, null, At(50, 50), At(60, 45));
        Assert.Equal(ParamKind.Rectangle, centred.Kind);
        Assert.Equal((20.0, 10.0), (centred.W, centred.H));
        var b = centred.Geometry.Bounds();
        Assert.Equal((40.0, 45.0, 60.0, 55.0), (Math.Round(b.MinX, 6), Math.Round(b.MinY, 6), Math.Round(b.MaxX, 6), Math.Round(b.MaxY, 6)));
        // Shift: a square.
        var square = Make(DrawMethod.RectCentreCorner, new CadOptions { Square = true }, At(0, 0), At(10, 4));
        Assert.Equal(square.W, square.H, 9);

        // Three points: a side from (0, 0) to (30, 40) (50 long, at an angle), 10 wide.
        var three = Make(DrawMethod.Rect3Points, null, At(0, 0), At(30, 40), At(-8, 6));
        Assert.Equal(50, three.W, 6);
        Assert.Equal(10, three.H, 6);
        var corners = Corners(three);
        Assert.Contains(corners, p => p.Dist(new(0, 0)) < 1e-6);
        Assert.Contains(corners, p => p.Dist(new(30, 40)) < 1e-6);
        // It stays an editable rectangle, turned: its size reads back as drawn.
        var shape = Assert.IsType<ParamShape>(three.ToShape(Guid.NewGuid()));
        Assert.Equal(50, shape.SeenSize.W, 6);
        Assert.Equal(53.13, shape.Transform.RotationDeg, 2);
        // Drawn right to left it's the same rectangle, not one turned upside down.
        var back = Make(DrawMethod.Rect3Points, null, At(30, 0), At(0, 0), At(10, 8));
        Assert.Equal(0, ((ParamShape)back.ToShape(Guid.NewGuid())).Transform.RotationDeg, 6);
        // A hexagon on a side drawn right to left reads a small turn, not −150°.
        var hex = Make(DrawMethod.PolygonEdge, new CadOptions { Sides = 6 }, At(20, 0), At(0, 0));
        Assert.InRange(((ParamShape)hex.ToShape(Guid.NewGuid())).Transform.RotationDeg, -30, 30);
    }

    [Fact]
    public void RectanglesFromASidesMiddle()
    {
        // Centre (0, 0), the middle of a side at (0, −5) (so 10 tall), a corner at (8, −5): 16 × 10.
        var cm = Make(DrawMethod.RectCentreMidCorner, null, At(0, 0), At(0, -5), At(8, -5));
        var b = cm.Geometry.Bounds();
        Assert.Equal(16, b.Width, 6);
        Assert.Equal(10, b.Height, 6);
        Assert.Equal(0, b.Center.X, 6);
        // The middle of a side at (10, 0), one end at (20, 0) (so the side is 20 long), the opposite side 6 away.
        var me = Make(DrawMethod.RectMidEndSide, null, At(10, 0), At(20, 0), At(15, 6));
        var mb = me.Geometry.Bounds();
        Assert.Equal((0.0, 0.0, 20.0, 6.0), (Math.Round(mb.MinX, 6), Math.Round(mb.MinY, 6), Math.Round(mb.MaxX, 6), Math.Round(mb.MaxY, 6)));
    }

    [Fact]
    public void SlotsAndParallelograms()
    {
        // Ends' centres 20 apart, 6 wide: 26 long overall, its ends rounded by 3.
        var slot = Make(DrawMethod.Slot, null, At(0, 0), At(20, 0), At(10, 3));
        Assert.Equal(ParamKind.Rectangle, slot.Kind);
        Assert.Equal((26.0, 6.0, 3.0), (Math.Round(slot.W, 6), Math.Round(slot.H, 6), Math.Round(slot.Corner, 6)));
        var sb = slot.Geometry.Bounds();
        Assert.Equal(-3, sb.MinX, 3);
        Assert.Equal(23, sb.MaxX, 3);
        var par = Make(DrawMethod.Parallelogram, null, At(0, 0), At(10, 0), At(13, 5));
        var c = Corners(par);
        Assert.Contains(c, p => p.Dist(new(3, 5)) < 1e-9);
        Assert.True(par.Closed);
    }

    [Fact]
    public void EllipsesByAxes()
    {
        // Centre (0, 0), one axis 10 long towards (6, 8) (turned 53°), the other 4.
        var e = Make(DrawMethod.EllipseCentreAxes, null, At(0, 0), At(6, 8), At(-3.2, 2.4));
        Assert.Equal(ParamKind.Ellipse, e.Kind);
        Assert.Equal(20, e.W, 6);
        Assert.Equal(8, e.H, 6);
        // The axis end clicked is on it.
        Assert.True(e.Geometry.Flatten(0.001).SelectMany(f => f.Points).Min(p => p.Dist(new(6, 8))) < 0.01);
        var ends = Make(DrawMethod.EllipseAxisEnds, null, At(0, 0), At(20, 0), At(10, 3));
        var b = ends.Geometry.Bounds();
        Assert.Equal((0.0, -3.0, 20.0, 3.0), (Math.Round(b.MinX, 3), Math.Round(b.MinY, 3), Math.Round(b.MaxX, 3), Math.Round(b.MaxY, 3)));
    }

    [Fact]
    public void PolygonsByCentreAndCornerOrSideAndByOneSide()
    {
        var o = new CadOptions { Sides = 6 };
        // Inscribed: every corner 10 from the centre, one of them where clicked.
        var ins = Make(DrawMethod.PolygonCentreCorner, o, At(50, 50), At(60, 50));
        Assert.Equal(ParamKind.Polygon, ins.Kind);
        var corners = Corners(ins);
        Assert.Equal(6, corners.Count);
        foreach (var p in corners) Assert.Equal(10, p.Dist(new(50, 50)), 6);
        Assert.Contains(corners, p => p.Dist(new(60, 50)) < 1e-6);
        // Circumscribed: the middle of each side 10 from the centre.
        var cir = Make(DrawMethod.PolygonCentreSide, o, At(0, 0), At(0, -10));
        var cc = Corners(cir);
        for (int i = 0; i < cc.Count; i++) Assert.Equal(10, ((cc[i] + cc[(i + 1) % cc.Count]) * 0.5).Length, 6);
        Assert.Contains(Enumerable.Range(0, cc.Count), i => ((cc[i] + cc[(i + 1) % cc.Count]) * 0.5).Dist(new(0, -10)) < 1e-6);
        // By one side: a square on (0, 0)–(10, 0), built above it (Y down: at negative Y).
        var sq = Make(DrawMethod.PolygonEdge, new CadOptions { Sides = 4 }, At(0, 0), At(10, 0));
        var sb = sq.Geometry.Bounds();
        Assert.Equal((0.0, -10.0, 10.0, 0.0), (Math.Round(sb.MinX, 6), Math.Round(sb.MinY, 6), Math.Round(sb.MaxX, 6), Math.Round(sb.MaxY, 6)));
        // Odd sides too: a triangle's corners are all where they should be.
        var tri = Make(DrawMethod.PolygonCentreCorner, new CadOptions { Sides = 3 }, At(0, 0), At(0, 10));
        foreach (var p in Corners(tri)) Assert.Equal(10, p.Length, 6);
        Assert.Contains(Corners(tri), p => p.Dist(new(0, 10)) < 1e-6);
        // A star by its centre and a point's tip.
        var star = Make(DrawMethod.StarCentrePoint, new CadOptions { StarPoints = 5, StarInner = 0.5 }, At(0, 0), At(12, 0));
        Assert.Equal(ParamKind.Star, star.Kind);
        var sp = Corners(star);
        Assert.Equal(10, sp.Count);
        Assert.Equal(5, sp.Count(p => Math.Abs(p.Length - 12) < 1e-6));
        Assert.Equal(5, sp.Count(p => Math.Abs(p.Length - 6) < 1e-6));
    }

    [Fact]
    public void ArcsByThreePointsAndByCentre()
    {
        // Start (10, 0), through (0, −10), end (−10, 0): the top half of the circle round (0, 0).
        var arc = Make(DrawMethod.Arc3Points, null, At(10, 0), At(0, -10), At(-10, 0));
        Assert.False(arc.Closed);
        IsCircle(arc, new(0, 0), 10);
        var pts = arc.Geometry.Flatten(0.01).SelectMany(f => f.Points).ToList();
        Assert.True(pts.All(p => p.Y <= 1e-6), "the arc should be the half through (0, −10)");
        Near(new(10, 0), arc.Geometry.Subpaths[0].Start);
        Near(new(-10, 0), arc.Geometry.Subpaths[0].Segs[^1].To);
        // Start, end, then the point it bulges through: the bottom half this time.
        var other = Make(DrawMethod.ArcStartEndPoint, null, At(10, 0), At(-10, 0), At(0, 10));
        Assert.True(other.Geometry.Flatten(0.01).SelectMany(f => f.Points).All(p => p.Y >= -1e-6));
        // Centre, start, end: counter-clockwise as on paper (up first, on a Y-down work area)…
        var ccw = Make(DrawMethod.ArcCentreStartEnd, null, At(0, 0), At(10, 0), At(0, 10));
        var cp = ccw.Geometry.Flatten(0.01).SelectMany(f => f.Points).ToList();
        IsCircle(ccw, new(0, 0), 10);
        Assert.Contains(cp, p => p.Dist(new(0, -10)) < 0.05);
        Assert.Contains(cp, p => p.Dist(new(-10, 0)) < 0.05);
        // …or clockwise with Alt: the short way round.
        var cw = Make(DrawMethod.ArcCentreStartEnd, new CadOptions { Clockwise = true }, At(0, 0), At(10, 0), At(0, 10));
        Assert.True(cw.Geometry.Flatten(0.01).SelectMany(f => f.Points).All(p => p.X >= -1e-6 && p.Y >= -1e-6));
    }

    [Fact]
    public void TypedNumbersGiveExactSizes()
    {
        // A radius of 7.5 typed with the pointer off to the right.
        var p = CadDraw.TypedPoint(DrawMethod.CircleCentreRadius, [At(10, 10)], new(50, 10), 7.5);
        Near(new(17.5, 10), p!.Value);
        // A side picked at its middle: the typed number is the whole side.
        var half = CadDraw.TypedPoint(DrawMethod.RectMidEndSide, [At(0, 0)], new(0, 30), 20);
        Near(new(0, 10), half!.Value);
        // A width across the first side, on the pointer's side.
        var off = CadDraw.TypedPoint(DrawMethod.Rect3Points, [At(0, 0), At(10, 0)], new(4, -3), 6);
        Near(new(4, -6), off!.Value);
        var slot = CadDraw.TypedPoint(DrawMethod.Slot, [At(0, 0), At(10, 0)], new(4, 3), 6);
        Near(new(4, 3), slot!.Value);
        // An arc's angle in degrees: 90 counter-clockwise from the start (up, on the work area).
        var ang = CadDraw.TypedPoint(DrawMethod.ArcCentreStartEnd, [At(0, 0), At(10, 0)], new(0, 0), 90);
        Near(new(0, -10), ang!.Value);
        // Typing means nothing for a point on an arc.
        Assert.Null(CadDraw.TypedPoint(DrawMethod.Arc3Points, [At(0, 0)], new(5, 5), 3));
    }

    [Fact]
    public void LinesFromTheMiddleAndTouchingTwoCircles()
    {
        var mid = Make(DrawMethod.LineFromMiddle, null, At(10, 10), At(15, 10));
        Near(new(5, 10), mid.Geometry.Subpaths[0].Start);
        Near(new(15, 10), mid.Geometry.Subpaths[0].Segs[0].To);
        // Two circles of radius 5, 20 apart: the line along their tops (clicked there) runs at y = −5.
        var a = new CircleTarget(new(0, 0), 5);
        var b = new CircleTarget(new(20, 0), 5);
        var top = Make(DrawMethod.LineTangentTwoCircles, null, new CadPick(new(0, -5), a), new CadPick(new(20, -5), b));
        Near(new(0, -5), top.Geometry.Subpaths[0].Start);
        Near(new(20, -5), top.Geometry.Subpaths[0].Segs[0].To);
        Assert.Equal(4, CadDraw.TangentLines(a, b).Count());
        // Crossing between them (the inner tangents) when clicked that way.
        var cross = Make(DrawMethod.LineTangentTwoCircles, null, new CadPick(new(2, -4.5), a), new CadPick(new(18, 4.5), b));
        var s = cross.Geometry.Subpaths[0];
        Assert.Equal(5, s.Start.Dist(a.Centre), 6);
        Assert.Equal(5, s.Segs[0].To.Dist(b.Centre), 6);
        Assert.True(s.Start.Y < 0 && s.Segs[0].To.Y > 0);
    }

    [Fact]
    public void CentrelinesBetweenLinesAndCentreMarks()
    {
        // Two parallel lines 10 apart: the centreline runs midway, 3 mm past both ends.
        var a = new LineTarget(new(0, 0), new(40, 0));
        var b = new LineTarget(new(45, 10), new(5, 10));
        var cl = Make(DrawMethod.CentrelineBetweenLines, new CadOptions { OvershootMm = 3 }, new CadPick(new(10, 0), a), new CadPick(new(10, 10), b));
        Assert.True(cl.Centreline);
        Assert.False(cl.Closed);
        var s = cl.Geometry.Subpaths[0];
        Near(new(-3, 5), s.Start);
        Near(new(48, 5), s.Segs[0].To);
        // Two lines meeting at a right angle at the origin: the centreline halves it, at 45°.
        var x = new LineTarget(new(0, 0), new(10, 0));
        var y = new LineTarget(new(0, 0), new(0, 10));
        var bis = Make(DrawMethod.CentrelineBetweenLines, new CadOptions { OvershootMm = 0 }, new CadPick(new(5, 0), x), new CadPick(new(0, 5), y));
        var bs = bis.Geometry.Subpaths[0];
        var d = bs.Segs[0].To - bs.Start;
        Assert.Equal(1, d.Y / d.X, 6);
        // A centre mark on a circle of radius 10: a cross 2 × (10 + 3) each way.
        var mark = Make(DrawMethod.CentreMark, new CadOptions { OvershootMm = 3 }, new CadPick(new(10, 0), new CircleTarget(new(0, 0), 10)));
        Assert.Equal(2, mark.Geometry.Subpaths.Length);
        var mb = mark.Geometry.Bounds();
        Assert.Equal((-13.0, -13.0, 13.0, 13.0), (mb.MinX, mb.MinY, mb.MaxX, mb.MaxY));
        // On any other shape: through the middle of its box.
        var box = Make(DrawMethod.CentreMark, new CadOptions { OvershootMm = 2 }, new CadPick(new(0, 0), new BoxTarget(new Rect2(0, 0, 20, 10))));
        Assert.Equal((-2.0, -2.0, 22.0, 12.0), (box.Geometry.Bounds().MinX, box.Geometry.Bounds().MinY, box.Geometry.Bounds().MaxX, box.Geometry.Bounds().MaxY));
        var two = Make(DrawMethod.CentrelineTwoPoints, null, At(0, 0), At(10, 0));
        Assert.True(two.Centreline);
    }

    [Fact]
    public void CentrelinesHaveTheirOwnLayerThatIsntMarked()
    {
        var s = new DocState([Layer.Default(0)], []);
        var (s1, layer) = CentreLines.Ensure(s);
        Assert.True(layer.Centrelines);
        Assert.False(layer.Output);
        Assert.Equal(2, s1.Layers.Count);
        // Asked again, the same one.
        var (s2, again) = CentreLines.Ensure(s1);
        Assert.Same(s1, s2);
        Assert.Equal(layer.Id, again.Id);
        // It survives saving and opening (its output can be turned on, unlike the mask layer's).
        var on = Ops.UpdateLayers(s1, new HashSet<Guid> { layer.Id }, l => l with { Output = true });
        var back = DocFile.Deserialize(DocFile.Serialize(on));
        var read = Assert.Single(back.Layers, l => l.Centrelines);
        Assert.True(read.Output);
        Assert.Equal(CentreLines.LayerName, read.Name);
    }

    [Fact]
    public void EveryMethodHasALabelAndStepsAndBuildsFromSensiblePoints()
    {
        var circle = new CircleTarget(new(0, 0), 10);
        var circle2 = new CircleTarget(new(40, 0), 10);
        var line = new LineTarget(new(-50, 20), new(50, 20));
        foreach (var m in Enum.GetValues<DrawMethod>())
        {
            Assert.False(string.IsNullOrEmpty(CadDraw.Label(m)));
            if (CadDraw.IsBox(m)) continue;
            var steps = CadDraw.Steps(m);
            Assert.NotEmpty(steps);
            Assert.False(string.IsNullOrEmpty(CadDraw.Hint(m)), $"{m} has no hint");
            CadPick[] picks = m switch
            {
                DrawMethod.CircleTanTanRadius => [new(new(0, 10), circle), new(new(0, 20), line)],
                DrawMethod.CircleTanTanTan => [new(new(7, 7), circle), new(new(33, 7), circle2), new(new(20, 20), line)],
                DrawMethod.LineTangentTwoCircles => [new(new(0, -10), circle), new(new(40, -10), circle2)],
                DrawMethod.CentrelineBetweenLines => [new(new(0, 20), line), new(new(0, 30), new LineTarget(new(-50, 30), new(50, 30)))],
                DrawMethod.CentreMark => [new(new(10, 0), circle)],
                _ => [.. new[] { At(0, 0), At(20, 5), At(7, 15) }.Take(steps.Length)],
            };
            Assert.Equal(steps.Length, picks.Length);
            var shape = CadDraw.Build(m, picks[..^1], picks[^1], new CadOptions { TangentRadiusMm = 5 });
            Assert.True(shape is not null, $"{m} made nothing");
            Assert.False(shape.Geometry.IsEmpty);
            Assert.NotNull(shape.ToShape(Guid.NewGuid()));
        }
        // Every tool's list holds every method once.
        var all = CadDraw.RectMethods.Concat(CadDraw.EllipseMethods).Concat(CadDraw.PolygonMethods).Concat(CadDraw.StarMethods)
            .Concat(CadDraw.LineMethods).Concat(CadDraw.ArcMethods).Concat(CadDraw.CentrelineMethods).ToList();
        Assert.Equal(Enum.GetValues<DrawMethod>().Length, all.Distinct().Count());
        Assert.Equal(all.Count, all.Distinct().Count());
    }
}
