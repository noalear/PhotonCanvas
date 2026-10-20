using PhotonCanvas.Core;
using PhotonCanvas.Core.Imaging;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Geometry;
using Xunit;

namespace PhotonCanvas.Tests;

/// <summary>Features from the comparison with other laser software.</summary>
public class ParityTests
{
    static (DocState, Layer) WithShape(LayerMode mode, PathGeometry g, Func<Layer, Layer>? edit = null)
    {
        var s = DocState.New();
        var layer = (edit ?? (l => l))(Layer.Default(1, mode));
        s = Ops.AddLayer(s, layer);
        return (Ops.AddShapes(s, [new PathShape { LayerId = layer.Id, Geometry = g }]), layer);
    }

    [Fact]
    public void SubLayersRunTheParentsShapes()
    {
        var (s, fill) = WithShape(LayerMode.Fill, PathGeometry.Rect(10, 10, 20, 20));
        var outline = Layer.Default(2, LayerMode.Score) with { ParentId = fill.Id };
        s = Ops.AddLayer(s, outline);
        var job = JobBuilder.Build(s, new JobOptions());
        Assert.Contains(job.Job.Blocks, b => b is FillBlock);
        Assert.Contains(job.Job.Blocks, b => b is VectorBlock);
        // Shapes put on a sub-layer go to its parent; deleting the parent takes its sub-layers.
        s = Ops.AddShapes(s, [new PathShape { LayerId = outline.Id, Geometry = PathGeometry.Rect(40, 10, 5, 5) }]);
        Assert.Equal(2, s.ShapesOn(fill.Id).Count());
        Assert.Null(Ops.DeleteLayers(s, new HashSet<Guid> { fill.Id }, null).FindLayer(outline.Id));
        Assert.Equal(outline.ParentId, DocFile.Deserialize(DocFile.Serialize(s)).FindLayer(outline.Id)!.ParentId);
    }

    [Fact]
    public void OneWayAndTogetherFills()
    {
        var two = new PathGeometry(PathGeometry.Rect(10, 10, 10, 10).Subpaths.Concat(PathGeometry.Rect(30, 10, 10, 10).Subpaths));
        var (s, _) = WithShape(LayerMode.Fill, two, l => l with { Bidirectional = false, FillTogether = true, LinesPerCm = 10 });
        var f = Assert.IsType<FillBlock>(Assert.Single(JobBuilder.Build(s, new JobOptions()).Job.Blocks));
        Assert.True(f.Unidirectional);
        Assert.All(Job.FillLines(f), l => Assert.True(l.End.X > l.Start.X));
    }

    [Fact]
    public void PerforationAndDotMode()
    {
        var dashes = JobBuilder.Dashes([new(0, 0), new(10, 0)], 2, 1);
        Assert.Equal(4, dashes.Count); // 0–2, 3–5, 6–8, 9–10
        Assert.All(dashes.Take(3), d => Assert.Equal(2, d[0].Dist(d[^1]), 6));
        Assert.Equal(6, JobBuilder.DotsAlong([new(0, 0), new(5, 0)], 1).Count);

        var (s, _) = WithShape(LayerMode.Cut, PathGeometry.Line(new(10, 10), new(30, 10)), l => l with { DotModeMm = 2 });
        var job = JobBuilder.Build(s, new JobOptions());
        var pts = Assert.IsType<PointsBlock>(Assert.Single(job.Job.Blocks));
        Assert.Equal(11, pts.Points.Count);
        Assert.Contains("G25X30.000Y10.000", job.Motion);
        var (p, _) = WithShape(LayerMode.Cut, PathGeometry.Line(new(10, 10), new(30, 10)), l => l with { PerforateMm = 3, PerforateGapMm = 2 });
        Assert.Equal(4, JobBuilder.Build(p, new JobOptions()).Job.Blocks.Count);
    }

    static ImageShape Picture(Guid layer, int w, int h, Func<int, int, byte> grey, double mm)
    {
        var rgba = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = (y * w + x) * 4;
                rgba[i] = rgba[i + 1] = rgba[i + 2] = grey(x, y);
                rgba[i + 3] = 255;
            }
        return new ImageShape { LayerId = layer, Source = ImageSource.FromEncoded(new RasterImage(w, h, rgba).EncodePng()), SizeMm = new Vec2(mm, mm * h / w), Transform = Affine.Translate(20, 20) };
    }

    [Fact]
    public void RampPassThroughAndClip()
    {
        var layer = Layer.Default(1, LayerMode.Image) with { Dither = DitherMethod.Threshold, RampMm = 1, Dpi = 254, GrayMinPower = 10 };
        var pic = Picture(layer.Id, 100, 100, (x, y) => 0, 10);
        var (block, _) = JobBuilder.Rasterize(pic, layer);
        Assert.NotNull(block!.Levels);
        // Edges are weaker than the middle.
        int w = block.Bitmap.Width;
        Assert.True(block.Levels![0] < block.Levels[(w / 2) * w + w / 2]);

        // 40 pixels across 10 mm is about 102 dpi: kept as they are under a finer layer resolution…
        var through = layer with { RampMm = 0, PassThrough = true, Dpi = 300 };
        var (pt, _) = JobBuilder.Rasterize(Picture(layer.Id, 40, 40, (x, y) => 0, 10), through);
        Assert.Equal(40, pt!.Bitmap.Width);
        // …but never packed closer than the layer's resolution (that would pile on heat).
        var (coarse, _) = JobBuilder.Rasterize(Picture(layer.Id, 40, 40, (x, y) => 0, 10), through with { Dpi = 50 });
        Assert.Equal(50.0, coarse!.Dpi, 6);
        Assert.InRange(coarse.Bitmap.Width, 19, 20);

        var clipped = Picture(layer.Id, 100, 100, (x, y) => 0, 10).WithClip(PathGeometry.Ellipse(25, 25, 5, 5));
        var (cb, _) = JobBuilder.Rasterize(clipped, layer with { RampMm = 0 });
        double share = cb!.Bitmap.Bits.Count(b => b) / (double)cb.Bitmap.Bits.Length;
        Assert.InRange(share, Math.PI / 4 - 0.05, Math.PI / 4 + 0.05);
    }

    [Fact]
    public void SharpenBringsOutEdges()
    {
        var v = Enumerable.Range(0, 40).Select(i => i < 20 ? 0.3f : 0.7f).ToArray();
        Dither.Sharpen(v, 40, 1, 1.5, 2);
        Assert.True(v[19] < 0.3f && v[20] > 0.7f);
        Assert.Equal(0.3f, v[5], 3);
    }

    [Fact]
    public void MoreCodes()
    {
        foreach (var (kind, text) in new[] { (CodeKind.Ean8, "1234567"), (CodeKind.UpcE, "0123456"), (CodeKind.Itf, "123456"), (CodeKind.Code93, "ABC-1"), (CodeKind.Codabar, "A1234B"), (CodeKind.Pdf417, "PhotonCanvas"), (CodeKind.Aztec, "PhotonCanvas") })
        {
            var (g, err) = CodeShape.Build(new CodeSpec(kind), text);
            Assert.True(err is null, $"{kind}: {err}");
            Assert.False(g.IsEmpty);
        }
    }

    [Fact]
    public void SimplifyCutAndWeld()
    {
        var dense = PathGeometry.Polyline([.. Enumerable.Range(0, 400).Select(i => new Vec2(10 * Math.Cos(i * Math.PI / 200), 10 * Math.Sin(i * Math.PI / 200)))], true);
        var (simple, before, after) = PathTools.Simplify(dense, 0.02);
        Assert.True(after < before / 4);
        Assert.Equal(20, Rect2.Bounds(simple.Flatten(0.01).SelectMany(f => f.Points).ToList()).Width, 1);

        var pieces = PathTools.CutShapes([PathGeometry.Rect(0, 0, 10, 10), PathGeometry.Rect(5, 0, 10, 10)]);
        Assert.Equal(3, pieces.Count);

        var loose = TextLayout.Build("AV", new TextStyle("Arial", 10, LetterSpacingMm: -4));
        var welded = TextLayout.Build("AV", new TextStyle("Arial", 10, LetterSpacingMm: -4, Weld: true));
        Assert.True(welded.Subpaths.Length < loose.Subpaths.Length, $"{loose.Subpaths.Length} -> {welded.Subpaths.Length}");
    }
}
