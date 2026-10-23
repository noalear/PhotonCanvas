using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Geometry;
using Xunit;

namespace PhotonCanvas.Tests;

public class ObjectSnapTests
{
    static SnapFeatures F(PathGeometry g) => ObjectSnap.Features(g);

    static SnapHit Hit(IEnumerable<SnapFeatures> shapes, Vec2 cursor, SnapKinds kinds = SnapKinds.Default, Vec2? from = null, double tol = 2)
    {
        var hit = ObjectSnap.Find(shapes, cursor, tol, kinds, from);
        Assert.True(hit.HasValue, $"nothing found at {cursor}");
        return hit.Value;
    }

    static void Near(Vec2 want, Vec2 got, double tol = 0.02) => Assert.True(want.Dist(got) <= tol, $"{got} isn't {want}");

    [Fact]
    public void EndsAndMiddlesOfLines()
    {
        var line = F(PathGeometry.Line(new Vec2(10, 10), new Vec2(50, 10)));
        var end = Hit([line], new Vec2(49.2, 10.7));
        Assert.Equal(SnapKind.Endpoint, end.Kind);
        Near(new Vec2(50, 10), end.At);
        var mid = Hit([line], new Vec2(30.6, 9.4));
        Assert.Equal(SnapKind.Midpoint, mid.Kind);
        Near(new Vec2(30, 10), mid.At);
        // Nearest is off unless asked for: elsewhere on the line, nothing.
        Assert.Null(ObjectSnap.Find([line], new Vec2(20, 10.5), 2, SnapKinds.Default));
        var nearest = Hit([line], new Vec2(20, 10.5), SnapKinds.All);
        Assert.Equal(SnapKind.Nearest, nearest.Kind);
        Near(new Vec2(20, 10), nearest.At);
    }

    [Fact]
    public void WhereTwoShapesCross()
    {
        var a = F(PathGeometry.Line(new Vec2(0, 0), new Vec2(40, 40)));
        var b = F(PathGeometry.Line(new Vec2(0, 30), new Vec2(40, 10)));
        // y = x and y = 30 − x/2 cross at x = y = 20.
        var x = Hit([a, b], new Vec2(21, 19.5));
        Assert.Equal(SnapKind.Intersection, x.Kind);
        Near(new Vec2(20, 20), x.At);
        // A rectangle's corner is an end, not a crossing of its own sides.
        var rect = F(PathGeometry.Rect(60, 0, 30, 20));
        var corner = Hit([rect], new Vec2(60.8, 0.6));
        Assert.Equal(SnapKind.Endpoint, corner.Kind);
        Near(new Vec2(60, 0), corner.At);
    }

    [Fact]
    public void CirclesGiveTheirCentreFromTheirEdgeAndTheirQuadrants()
    {
        var circle = F(PathGeometry.Ellipse(100, 100, 25, 25));
        var c = Assert.Single(circle.Circles);
        Assert.True(c.Whole);
        Assert.Equal(25, c.Radius, 2);
        // Pointing at the edge, away from its quadrants: the centre.
        var edge = new Vec2(100 + 25 * Math.Cos(0.7), 100 + 25 * Math.Sin(0.7));
        var centre = Hit([circle], edge + new Vec2(0.3, 0.2));
        Assert.Equal(SnapKind.Center, centre.Kind);
        Near(new Vec2(100, 100), centre.At);
        var quad = Hit([circle], new Vec2(124.4, 100.8));
        Assert.Equal(SnapKind.Quadrant, quad.Kind);
        Near(new Vec2(125, 100), quad.At);
    }

    [Fact]
    public void EllipsesAndRectanglesHaveACentre()
    {
        var ellipse = F(PathGeometry.Ellipse(50, 50, 30, 12));
        var e = Assert.Single(ellipse.Circles);
        Assert.True(e.Whole);
        Near(new Vec2(50, 50), e.Center);
        var top = Hit([ellipse], new Vec2(50.5, 38.6));
        Assert.Equal(SnapKind.Quadrant, top.Kind);
        Near(new Vec2(50, 38), top.At);
        var rect = F(PathGeometry.Rect(0, 0, 40, 20));
        var middle = Hit([rect], new Vec2(20.8, 10.6));
        Assert.Equal(SnapKind.Center, middle.Kind);
        Near(new Vec2(20, 10), middle.At);
    }

    [Fact]
    public void PerpendicularAndTangentFromWhereTheLineStarts()
    {
        var wall = F(PathGeometry.Line(new Vec2(0, 50), new Vec2(100, 50)));
        var from = new Vec2(30, 10);
        // Ending near the wall, near straight below the start: square onto it.
        var perp = Hit([wall], new Vec2(31.2, 50.8), from: from);
        Assert.Equal(SnapKind.Perpendicular, perp.Kind);
        Near(new Vec2(30, 50), perp.At);

        var circle = F(PathGeometry.Ellipse(100, 100, 20, 20));
        var start = new Vec2(100, 40);
        // From 60 mm above the centre, the tangents touch at angle acos(20/60) either side of straight up.
        double a = -Math.PI / 2 + Math.Acos(20.0 / 60);
        var touch = new Vec2(100 + 20 * Math.Cos(a), 100 + 20 * Math.Sin(a));
        var tan = Hit([circle], touch + new Vec2(0.4, -0.3), from: start);
        Assert.Equal(SnapKind.Tangent, tan.Kind);
        Near(touch, tan.At, 0.05);
    }

    [Fact]
    public void PicturesSnapByTheirCornersSidesAndMiddle()
    {
        var pic = ObjectSnap.Features([new Vec2(0, 0), new Vec2(60, 0), new Vec2(60, 40), new Vec2(0, 40)]);
        Near(new Vec2(60, 40), Hit([pic], new Vec2(59.3, 39.5)).At);
        Assert.Equal(SnapKind.Midpoint, Hit([pic], new Vec2(30.4, 0.6)).Kind);
        Assert.Equal(SnapKind.Center, Hit([pic], new Vec2(29.5, 20.5)).Kind);
    }
}
