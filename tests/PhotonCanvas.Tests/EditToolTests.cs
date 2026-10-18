using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Geometry;
using Xunit;

namespace PhotonCanvas.Tests;

public class EditToolTests
{
    static Rect2 B(PathGeometry g) => Rect2.Bounds(g.Flatten(0.005).SelectMany(f => f.Points).ToList());

    [Fact]
    public void ScissorsOpenAndSplit()
    {
        var opened = EditTools.SplitAt(PathGeometry.Rect(0, 0, 10, 10), new Vec2(5, 0))!;
        var sp = Assert.Single(opened.Subpaths);
        Assert.False(sp.Closed);
        Assert.True(sp.Start.Dist(new Vec2(5, 0)) < 1e-6);
        Assert.True(sp.End.Dist(new Vec2(5, 0)) < 1e-6);
        var two = EditTools.SplitAt(PathGeometry.Line(new(0, 0), new(10, 0)), new Vec2(4, 0.1))!;
        Assert.Equal(2, two.Subpaths.Length);
        Assert.Equal(4, two.Subpaths[0].End.X, 6);
    }

    [Fact]
    public void SharedEdgesAreCutOnce()
    {
        var (g, removed) = EditTools.RemoveOverlaps([PathGeometry.Rect(0, 0, 10, 10), PathGeometry.Rect(10, 0, 10, 10)]);
        Assert.Equal(10, removed, 3);
        Assert.True(g[0].Subpaths.Single().Closed);
        var second = g[1].Flatten(0.01).Sum(f => PathTools.CumulativeLengths(f.Points)[^1]);
        Assert.Equal(30, second, 3);
    }

    [Fact]
    public void TangentCirclesAndShadows()
    {
        // Three equal circles in a row of a triangle: the inner and outer touching circles are found.
        var found = EditTools.TangentCircles((new(0, 0), 1), (new(10, 0), 1), (new(5, 8), 1));
        Assert.True(found.Count >= 2);
        foreach (var (c, r) in found)
            foreach (var (cc, rr) in new[] { (new Vec2(0, 0), 1.0), (new Vec2(10, 0), 1.0), (new Vec2(5, 8), 1.0) })
            {
                double d = c.Dist(cc);
                Assert.True(Math.Abs(d - (r + rr)) < 1e-6 || Math.Abs(d - Math.Abs(r - rr)) < 1e-6);
            }
        var shadow = EditTools.Shadow(PathGeometry.Rect(0, 0, 10, 10), 2, 2);
        Assert.Equal(new Rect2(2, 2, 12, 12), B(shadow));
        Assert.False(shadow.IsEmpty);
    }

    [Fact]
    public void EditableShapesKeepTheirSettings()
    {
        var r = ParamShape.Create(ParamKind.Rectangle, 20, 10, 2, 4, 0.5, Guid.NewGuid(), Affine.Translate(5, 5));
        Assert.Contains(r.Geometry.Subpaths[0].Segs, s => s.Kind == SegKind.Cubic);
        // Stretched by the handles, then edited: the size folds in, the corners stay round.
        var stretched = (ParamShape)r.With(transform: Affine.Scale(2, 1).Then(r.Transform));
        Assert.Equal(40, stretched.SeenSize.W, 6);
        var edited = stretched.Edit(40, 10, 3, 4, 0.5);
        Assert.Equal(1, edited.Transform.ScaleX, 6);
        Assert.Equal(40, edited.Bounds.Width, 2);
        var star = ParamShape.Create(ParamKind.Star, 30, 30, 0, 7, 0.4, Guid.NewGuid(), Affine.Identity);
        Assert.Equal(14, star.Geometry.Subpaths[0].Segs.Length);
        var s = Ops.AddShapes(DocState.New(), [star.With(layerId: DocState.New().Layers[0].Id)]);
        var back = DocFile.Deserialize(DocFile.Serialize(s with { Guides = [new Guide(true, 50)] }));
        var p = Assert.IsType<ParamShape>(Assert.Single(back.Shapes));
        Assert.Equal(7, p.Sides);
        Assert.Equal(50, Assert.Single(back.Guides).At);
    }

    [Fact]
    public void TextFitsItsBox()
    {
        var g = TextLayout.Build("WIDE TEXT", new TextStyle("Arial", 10, FitWidthMm: 30));
        Assert.Equal(30, B(g).Width, 2);
        var both = TextLayout.Build("WIDE TEXT", new TextStyle("Arial", 10, FitWidthMm: 30, FitHeightMm: 2));
        Assert.True(B(both).Height <= 2.001 && B(both).Width < 30);
    }

    [Fact]
    public void BoxesAndKeychains()
    {
        var panels = Generators.Box(80, 60, 40, 3, 9, lid: true);
        Assert.Equal(6, panels.Count);
        var bottom = panels.Single(p => p.Name == "Bottom").Outline;
        Assert.Equal(86, B(bottom).Width, 3);
        // Notches make the outline much longer than a plain rectangle.
        double perim = bottom.Flatten(0.01).Sum(f => PathTools.CumulativeLengths([.. f.Points, f.Points[0]])[^1]);
        Assert.True(perim > 2 * (86 + 66) + 20);
        var tag = Generators.Keychain(Generators.TagShape.RoundedRectangle, 50, 20, 4, 3);
        Assert.Equal(2, tag.Subpaths.Length);
    }
}
