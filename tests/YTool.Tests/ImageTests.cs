using SkiaSharp;
using YTool.Core;
using YTool.Core.Imaging;
using YTool.Doc;
using YTool.Doc.Tracing;

namespace YTool.Tests;

public class ImageTests
{
    /// <summary>A PNG drawn with Skia: white background plus whatever <paramref name="draw"/> paints.</summary>
    static byte[] Png(int w, int h, Action<SKCanvas> draw)
    {
        using var surface = SKSurface.Create(new SKImageInfo(w, h));
        surface.Canvas.Clear(SKColors.White);
        draw(surface.Canvas);
        using var img = surface.Snapshot();
        return img.Encode(SKEncodedImageFormat.Png, 100).ToArray();
    }

    [Fact]
    public void DecodesAndMeasuresBrightness()
    {
        var src = ImageSource.FromEncoded(Png(40, 20, c => c.DrawRect(0, 0, 20, 20, new SKPaint { Color = SKColors.Black })));
        Assert.Equal((40, 20), (src.Width, src.Height));
        Assert.True(src.SampleLuminance(5, 5) < 0.05);
        Assert.True(src.SampleLuminance(35, 5) > 0.95);
        Assert.Equal(1f, src.SampleLuminance(-3, 5)); // outside = white
        Assert.Throws<FormatException>(() => ImageSource.FromEncoded([1, 2, 3, 4]));
    }

    [Fact]
    public void DitheringPreservesAverageTone()
    {
        foreach (var m in Enum.GetValues<DitherMethod>().Where(m => m != DitherMethod.Threshold))
        {
            var grey = Enumerable.Repeat(0.25f, 100 * 100).ToArray(); // 25 % white = 75 % burn
            var dots = Dither.Apply(grey, 100, 100, m);
            double burn = dots.Count(d => d) / 10000.0;
            // Atkinson deliberately drops 1/4 of the error, so dark tones come out darker.
            Assert.InRange(burn, 0.7, m == DitherMethod.Atkinson ? 0.9 : 0.8);
        }
        Assert.All(Dither.Apply([0.49f, 0.51f], 2, 1, DitherMethod.Threshold).Zip([true, false]), t => Assert.Equal(t.Second, t.First));
    }

    [Fact]
    public void ToneAdjustments()
    {
        Assert.Equal(0.3, ToneAdjust.None.Apply(0.3), 9);
        Assert.Equal(0.7, new ToneAdjust(Invert: true).Apply(0.3), 9);
        Assert.True(new ToneAdjust(Brightness: 0.2).Apply(0.3) > 0.45);
        Assert.True(new ToneAdjust(Contrast: 0.5).Apply(0.3) < 0.3);
        Assert.True(new ToneAdjust(Gamma: 2).Apply(0.3) > 0.3);
    }

    [Fact]
    public void TracesACircleSmoothlyAndAccurately()
    {
        // 100 px circle radius 40 centred at (60, 60).
        var src = ImageSource.FromEncoded(Png(120, 120, c => c.DrawCircle(60, 60, 40, new SKPaint { Color = SKColors.Black, IsAntialias = true })));
        var w = ImageTrace.Prepare(src.Pixels);
        var r = ImageTrace.BlackAndWhite(w, new TraceOptions());
        var sub = Assert.Single(r.Parts.Single().Geometry.Subpaths);
        Assert.True(sub.Closed);
        Assert.True(sub.Segs.Length < 40, $"{sub.Segs.Length} segments: should be a handful of curves, not pixel steps");
        Assert.Contains(sub.Segs, s => s.Kind == SegKind.Cubic);
        foreach (var p in sub.Flatten(0.05))
            Assert.InRange(p.Dist(new Vec2(60, 60)), 39.2, 40.8);
    }

    [Fact]
    public void KeepsSquareCornersSharpAndHolesAsHoles()
    {
        var src = ImageSource.FromEncoded(Png(100, 100, c =>
        {
            c.DrawRect(10, 10, 80, 80, new SKPaint { Color = SKColors.Black });
            c.DrawRect(35, 35, 30, 30, new SKPaint { Color = SKColors.White });
        }));
        var g = ImageTrace.BlackAndWhite(ImageTrace.Prepare(src.Pixels), new TraceOptions()).Parts[0].Geometry;
        Assert.Equal(2, g.Subpaths.Length);
        var outer = g.Subpaths.MaxBy(s => s.Flatten(0.1).Count)!;
        var b = new PathGeometry([g.Subpaths.OrderByDescending(s => Rect2.Bounds(s.Flatten(0.1)).Width).First()]).Bounds();
        Assert.InRange(b.MinX, 9.3, 10.7);
        Assert.InRange(b.MaxX, 89.3, 90.7);
        // Corners stay near the true corner (a rounded trace would cut them by several px).
        Assert.Contains(outer.Flatten(0.05), p => p.Dist(new Vec2(10, 10)) < 1.2);
    }

    [Fact]
    public void IgnoresSpecksBelowTheMinimumArea()
    {
        var src = ImageSource.FromEncoded(Png(100, 100, c =>
        {
            c.DrawRect(20, 20, 40, 40, new SKPaint { Color = SKColors.Black });
            c.DrawRect(80, 80, 2, 2, new SKPaint { Color = SKColors.Black }); // 4 px speck
        }));
        var w = ImageTrace.Prepare(src.Pixels);
        Assert.Single(ImageTrace.BlackAndWhite(w, new TraceOptions { MinAreaPx = 10 }).Parts[0].Geometry.Subpaths);
        Assert.Equal(2, ImageTrace.BlackAndWhite(w, new TraceOptions { MinAreaPx = 1 }).Parts[0].Geometry.Subpaths.Length);
    }

    [Fact]
    public void GroupsColoursAndFindsTheBackground()
    {
        var src = ImageSource.FromEncoded(Png(120, 60, c =>
        {
            c.DrawRect(10, 10, 40, 40, new SKPaint { Color = new SKColor(220, 30, 30) });
            c.DrawRect(70, 10, 40, 40, new SKPaint { Color = new SKColor(30, 30, 220) });
            c.DrawRect(15, 15, 5, 5, new SKPaint { Color = new SKColor(230, 40, 35) }); // near-identical red
        }));
        var w = ImageTrace.Prepare(src.Pixels);
        var (palette, labels) = ImageTrace.Palette(w, 20);
        Assert.Equal(3, palette.Count);
        Assert.Single(palette, p => p.IsBackground);
        Assert.True(palette.Single(p => p.IsBackground).Rgb > 0xF0F0F0);
        var include = Enumerable.Range(0, palette.Count).Where(i => !palette[i].IsBackground).ToHashSet();
        var result = ImageTrace.ByColour(w, palette, labels, include, new TraceOptions());
        Assert.Equal(2, result.Parts.Count);
        Assert.All(result.Parts, p => Assert.Single(p.Geometry.Subpaths));
    }

    [Fact]
    public void LargePicturesAreTracedAtWorkingSizeButReturnedInOriginalPixels()
    {
        var src = ImageSource.FromEncoded(Png(4000, 1000, c => c.DrawRect(1000, 200, 2000, 600, new SKPaint { Color = SKColors.Black })));
        var w = ImageTrace.Prepare(src.Pixels);
        Assert.Equal(2000, w.Image.Width);
        var b = ImageTrace.BlackAndWhite(w, new TraceOptions()).Parts[0].Geometry.Bounds();
        Assert.InRange(b.MinX, 995, 1005);
        Assert.InRange(b.MaxX, 2995, 3005);
    }

    [Fact]
    public void EngravesAnImageAsDotsOnTheMachineGrid()
    {
        var s = DocState.New();
        var layer = Layer.Default(0, LayerMode.Image) with { Dpi = 254 }; // 0.1 mm pitch
        s = s with { Layers = [layer] };
        var src = ImageSource.FromEncoded(Png(100, 100, c => c.DrawRect(0, 0, 50, 100, new SKPaint { Color = SKColors.Black })));
        var img = new ImageShape { LayerId = layer.Id, Source = src, SizeMm = new(10, 10), Transform = Affine.Translate(50, 60), Name = "half" };
        s = Ops.AddShapes(s, [img]);
        var job = JobBuilder.Build(s, new JobOptions());
        Assert.Contains("M523P10", job.Motion);
        Assert.Contains("G25T20", job.Motion);
        var dot = Assert.IsType<DotBlock>(Assert.Single(job.Job.Blocks));
        Assert.Equal(100, dot.Bitmap.Width);
        // Left half burns, right half doesn't.
        Assert.True(dot.Bitmap[10, 50]);
        Assert.False(dot.Bitmap[90, 50]);
        Assert.InRange(job.Bounds.MinX, 50, 50.1);

        // Rotated 90°, the black half moves to the top.
        var rotated = Ops.Transform(s, new HashSet<Guid> { img.Id }, Affine.RotateAbout(new Vec2(55, 65), 90));
        var r = Assert.IsType<DotBlock>(Assert.Single(JobBuilder.Build(rotated, new JobOptions()).Job.Blocks));
        Assert.True(r.Bitmap[50, 10]);
        Assert.False(r.Bitmap[50, 90]);
    }

    [Fact]
    public void WarnsWhenImagesAndVectorsAreOnTheWrongKindOfLayer()
    {
        var s = DocState.New();
        var cut = s.Layers[0];
        var imgLayer = Layer.Default(1, LayerMode.Image);
        s = Ops.AddLayer(s, imgLayer);
        var src = ImageSource.FromEncoded(Png(10, 10, c => c.Clear(SKColors.Black)));
        s = Ops.AddShapes(s,
        [
            new ImageShape { LayerId = cut.Id, Source = src, SizeMm = new(5, 5), Transform = Affine.Translate(10, 10) },
            new PathShape { LayerId = imgLayer.Id, Geometry = PathGeometry.Rect(30, 30, 5, 5) },
            new PathShape { LayerId = cut.Id, Geometry = PathGeometry.Rect(50, 50, 5, 5) },
        ]);
        var job = JobBuilder.Build(s, new JobOptions());
        Assert.Contains(job.Warnings, w => w.Contains("image(s) were skipped"));
        Assert.Contains(job.Warnings, w => w.Contains("vector shape(s) were skipped"));
    }

    [Fact]
    public void ImagesSurviveSaveAndLoad()
    {
        var s = DocState.New();
        var l = s.Layers[0] with { Mode = LayerMode.Image, Dpi = 300, DotUs = 150, Dither = DitherMethod.Atkinson };
        s = s with { Layers = [l] };
        var bytes = Png(30, 20, c => c.DrawCircle(15, 10, 5, new SKPaint { Color = SKColors.Black }));
        var img = new ImageShape
        {
            LayerId = l.Id, Name = "logo", Source = ImageSource.FromEncoded(bytes), SizeMm = new(30, 20),
            Tone = new ToneAdjust(0.1, -0.2, 1.4, true), Transform = Affine.Translate(5, 6),
        };
        var back = DocFile.Deserialize(DocFile.Serialize(Ops.AddShapes(s, [img])));
        Assert.Equal(l, back.Layers[0]);
        var b = Assert.IsType<ImageShape>(Assert.Single(back.Shapes));
        Assert.Equal(bytes, b.Source.Encoded);
        Assert.Equal(img.Tone, b.Tone);
        Assert.Equal(img.SizeMm, b.SizeMm);
        Assert.Equal(img.Bounds, b.Bounds);
    }
}
