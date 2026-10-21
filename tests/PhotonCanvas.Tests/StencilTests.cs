using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Geometry;
using Xunit;

namespace PhotonCanvas.Tests;

public class StencilTests
{
    static List<IReadOnlyList<Vec2>> Loops(PathGeometry g) => [.. g.Flatten(0.02).Where(f => f.Closed).Select(f => f.Points)];
    static double Area(PathGeometry g) => Math.Abs(Loops(g).Sum(Geom.SignedArea));

    [Fact]
    public void AnOsMiddleStaysJoined()
    {
        // An "O": a ring 10 mm out, 6 mm in.
        var o = new PathGeometry([.. PathGeometry.Ellipse(0, 0, 10, 10).Subpaths, .. PathGeometry.Ellipse(0, 0, 6, 6).Subpaths]);
        var (bridged, islands) = Stencil.Bridge(o, 1);
        Assert.Equal(1, islands);
        // Cut as drawn, the middle is enclosed (it would fall out); bridged, no cut goes all round it.
        Assert.Contains(Loops(o), l => Geom.Inside(l, new Vec2(0, 0)));
        Assert.DoesNotContain(Loops(bridged), l => Geom.Inside(l, new Vec2(0, 0)));
        // Two bridges (above and below the middle), each 1 mm wide across the 4 mm ring.
        double ring = Math.PI * (10 * 10 - 6 * 6);
        Assert.InRange(Area(bridged), ring - 8 - 1.5, ring - 8 + 0.5);
        Assert.Equal(2, Loops(bridged).Count); // the ring in two halves
    }

    [Fact]
    public void ShapesWithoutAMiddleAreLeftAlone()
    {
        var l = PathGeometry.Rect(0, 0, 10, 2);
        var (same, islands) = Stencil.Bridge(l, 1);
        Assert.Equal(0, islands);
        Assert.Same(l, same);
    }
}
