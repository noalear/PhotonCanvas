using System.Collections.Immutable;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Geometry;
using PhotonCanvas.Doc.Import;
using Xunit;

namespace PhotonCanvas.Tests;

/// <summary>Cut order, start points, lead-outs, hand-placed tabs, point editing and more file imports.</summary>
public class OrderAndImportTests
{
    static DocState Doc(Func<Layer, Layer> edit, params PathShape[] shapes)
    {
        var s = DocState.New();
        var layer = edit(Layer.Default(1, LayerMode.Cut));
        s = Ops.AddLayer(s, layer);
        return Ops.AddShapes(s, [.. shapes.Select(p => p.With(layerId: layer.Id))]);
    }

    static PathShape Shape(PathGeometry g) => new() { Geometry = g };

    static List<VectorBlock> Vectors(DocState s) => [.. JobBuilder.Build(s, new JobOptions()).Job.Blocks.OfType<VectorBlock>()];

    static double Length(IEnumerable<VectorBlock> blocks) =>
        blocks.Sum(b => PathTools.CumulativeLengths([.. b.Path.EmittedPoints()])[^1]);

    [Fact]
    public void AsDrawnKeepsTheDrawingOrder()
    {
        var far = Shape(PathGeometry.Rect(100, 100, 10, 10));
        var near = Shape(PathGeometry.Rect(0, 0, 10, 10));
        var optimised = Vectors(Doc(l => l, far, near));
        Assert.True(optimised[0].Path.Points[0].X < 50);
        var drawn = Vectors(Doc(l => l with { Order = PathOrder.AsDrawn }, far, near));
        Assert.True(drawn[0].Path.Points[0].X > 50);
    }

    [Fact]
    public void LoopsCanStartAtTheirNearestPoint()
    {
        var loop = PathGeometry.Polyline([new(30, 30), new(10, 30), new(10, 10), new(30, 10)], true);
        var plain = Vectors(Doc(l => l, Shape(loop)));
        Assert.Equal(new Vec2(30, 30), plain[0].Path.Points[0]);
        var nearest = Vectors(Doc(l => l with { StartNearest = true }, Shape(loop)));
        Assert.Equal(new Vec2(10, 10), nearest[0].Path.Points[0]);
    }

    [Fact]
    public void SharedEdgesOnALayerAreCutOnce()
    {
        var a = Shape(PathGeometry.Rect(10, 10, 10, 10));
        var b = Shape(PathGeometry.Rect(20, 10, 10, 10));
        Assert.Equal(80, Length(Vectors(Doc(l => l, a, b))), 3);
        Assert.Equal(70, Length(Vectors(Doc(l => l with { RemoveOverlaps = true }, a, b))), 3);
    }

    [Fact]
    public void LeadOutAddsItsLength()
    {
        var r = Shape(PathGeometry.Rect(10, 10, 10, 10));
        double plain = Length(Vectors(Doc(l => l, r)));
        double led = Length(Vectors(Doc(l => l with { LeadOutMm = 2 }, r)));
        Assert.Equal(plain + 2, led, 3);
    }

    [Fact]
    public void HandPlacedTabsLeaveGaps()
    {
        var r = Shape(PathGeometry.Rect(10, 10, 20, 20)).WithTabs([new Vec2(20, 10), new Vec2(30, 20)]);
        var cut = Vectors(Doc(l => l with { TabWidthMm = 2 }, r));
        Assert.Equal(2, cut.Count);
        Assert.Equal(80 - 4, Length(cut), 3);
        // Kept with the design.
        var back = DocFile.Deserialize(DocFile.Serialize(Doc(l => l, r)));
        Assert.Equal(2, Assert.IsType<PathShape>(Assert.Single(back.Shapes)).ManualTabs.Count);
    }

    [Fact]
    public void PointsCanBeRoundedAndMadeSymmetric()
    {
        var rect = PathGeometry.Rect(0, 0, 10, 10);
        var round = NodeEdit.RoundCorner(rect, new NodeRef(0, 1), 2);
        var bounds = Rect2.Bounds(round.Flatten(0.005).SelectMany(f => f.Points).ToList());
        Assert.Equal(new Rect2(0, 0, 10, 10), bounds);
        Assert.Single(round.Subpaths[0].Segs, s => s.Kind == SegKind.Cubic);
        // A rounded corner no longer reaches the corner point.
        Assert.True(round.Flatten(0.005).SelectMany(f => f.Points).Min(p => p.Dist(new Vec2(10, 0))) > 0.5);

        var curve = new PathGeometry([new Subpath(new(0, 0), [Seg.Cubic(new(2, 0), new(4, 5), new(5, 5)), Seg.Cubic(new(9, 5), new(10, 2), new(10, 0))], false)]);
        var sym = NodeEdit.Symmetric(curve, new NodeRef(0, 1));
        var (hin, hout) = NodeEdit.Handles(sym, new NodeRef(0, 1));
        Assert.Equal(hin!.Value.Dist(new(5, 5)), hout!.Value.Dist(new(5, 5)), 6);
        Assert.Equal(5, hout.Value.X - hin.Value.X, 6); // in line through the point, each 2.5 long (the average)
        Assert.Equal(5, hin.Value.Y, 6);
    }

    [Fact]
    public void PlotterFilesImport()
    {
        var r = HpglImport.Parse("IN;SP1;PU0,0;PD400,0,400,400,0,400,0,0;PU;SP2;PU800,0;PD1200,0;");
        Assert.Equal(2, r.Paths.Count);
        var square = r.Paths[0].Geometry;
        Assert.True(square.Subpaths[0].Closed);
        var b = Rect2.Bounds(square.Flatten(0.01).SelectMany(f => f.Points).ToList());
        Assert.Equal(10, b.Width, 6);
        Assert.Equal(-10, b.MinY, 6); // plotter Y points up
        Assert.NotEqual(r.Paths[0].Color, r.Paths[1].Color);
    }

    [Fact]
    public void LightBurnProjectsImport()
    {
        const string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <LightBurnProject AppVersion="1.4.00" FormatVersion="1">
                <CutSetting type="Cut"><index Value="0"/><name Value="Outline"/></CutSetting>
                <CutSetting type="Scan"><index Value="1"/><name Value="Engrave"/></CutSetting>
                <Shape Type="Rect" CutIndex="0" W="20" H="10" Cr="0"><XForm>1 0 0 1 50 50</XForm></Shape>
                <Shape Type="Group" CutIndex="1">
                    <XForm>1 0 0 1 10 0</XForm>
                    <Children>
                        <Shape Type="Ellipse" CutIndex="1" Rx="5" Ry="5"><XForm>1 0 0 1 0 0</XForm></Shape>
                        <Shape Type="Path" CutIndex="1"><XForm>1 0 0 1 0 0</XForm>
                            <VertList>V0 0c0x0c0y5c1x0c1y0V10 0c0x10c0y0c1x10c1y5</VertList>
                            <PrimList>B0 1</PrimList>
                        </Shape>
                    </Children>
                </Shape>
            </LightBurnProject>
            """;
        var r = LbrnImport.Parse(xml);
        Assert.Equal(3, r.Paths.Count);
        Assert.Equal("Outline", r.LayerNames[r.Paths[0].Color!.Value]);
        Assert.Equal("Engrave", r.LayerNames[r.Paths[1].Color!.Value]);
        var rect = Rect2.Bounds(r.Paths[0].Geometry.Flatten(0.01).SelectMany(f => f.Points).ToList());
        Assert.Equal(new Rect2(40, -55, 60, -45), rect);
        var circle = Rect2.Bounds(r.Paths[1].Geometry.Flatten(0.01).SelectMany(f => f.Points).ToList());
        Assert.Equal(10, circle.Center.X, 3);
        var curve = r.Paths[2].Geometry.Subpaths.Single();
        Assert.Equal(SegKind.Cubic, curve.Segs.Single().Kind);
        Assert.Equal(new Vec2(20, 0), curve.End);
        Assert.Throws<FormatException>(() => LbrnImport.Parse("<svg/>"));
    }
}
