using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Geometry;
using Xunit;

namespace PhotonCanvas.Tests;

public class MaskTests
{
    static readonly PathGeometry Frame = PathGeometry.Rect(10, 10, 20, 20);

    static double Area(PathGeometry g) => Math.Abs(g.Flatten(0.01).Where(f => f.Closed).Sum(f => Geom.SignedArea(f.Points)));
    static double Length(PathGeometry g) => g.Flatten(0.01).Sum(f => Enumerable.Range(1, f.Points.Count - 1).Sum(i => f.Points[i - 1].Dist(f.Points[i])));

    [Fact]
    public void LinesAreCutAtTheMasksEdge()
    {
        // A line 0–40 mm across through the middle of the 10–30 mm frame.
        var line = PathGeometry.Polyline([new Vec2(0, 20), new Vec2(40, 20)], false);
        var inside = Mask.Apply(line, Frame);
        Assert.InRange(Length(inside), 19.9, 20.1);
        Assert.All(inside.Flatten(0.01).SelectMany(f => f.Points), p => Assert.InRange(p.X, 9.99, 30.01));
        var outside = Mask.Apply(line, Frame, keepOutside: true);
        Assert.InRange(Length(outside), 19.9, 20.1);
        Assert.Equal(2, outside.Subpaths.Length);
    }

    [Fact]
    public void FilledShapesAreCutToTheMasksShape()
    {
        // A 20 × 20 square overlapping the frame by 10 × 10.
        var square = PathGeometry.Rect(20, 20, 20, 20);
        Assert.InRange(Area(Mask.Apply(square, Frame)), 99.5, 100.5);
        Assert.InRange(Area(Mask.Apply(square, Frame, keepOutside: true)), 299.5, 300.5);
    }

    [Fact]
    public void NothingInsideLeavesNothing()
    {
        var far = PathGeometry.Polyline([new Vec2(100, 100), new Vec2(120, 100)], false);
        Assert.True(Mask.Apply(far, Frame).IsEmpty);
        // An open-only mask covers nothing: everything is outside it.
        var openMask = PathGeometry.Polyline([new Vec2(0, 0), new Vec2(50, 50)], false);
        Assert.True(Mask.Apply(Frame, openMask).IsEmpty);
        Assert.Equal(Frame, Mask.Apply(Frame, openMask, keepOutside: true));
    }
}
