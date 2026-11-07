using PhotonCanvas.Core;
using PhotonCanvas.Core.Imaging;
using PhotonCanvas.Doc;

namespace PhotonCanvas.Tests;

/// <summary>The mask layer: masks are kept on the work area but never cut or marked.</summary>
public class MaskLayerTests
{
    static ImageSource Picture()
    {
        int w = 20, h = 20;
        var px = new byte[w * h * 4];
        for (int i = 0; i < w * h; i++) { px[i * 4 + 3] = 255; }
        return ImageSource.FromEncoded(new RasterImage(w, h, px).ToPng());
    }

    /// <summary>A picture on its own layer, masked by a circle left on the "Cut" layer (Kyle's job).</summary>
    static (DocState State, PathShape Circle, ImageShape Picture, Layer Cut) MaskedPicture()
    {
        var s = DocState.New();
        var cut = s.Layers[0];
        var picLayer = Layer.Default(1, LayerMode.Image) with { Name = "Picture" };
        s = Ops.AddLayer(s, picLayer);
        var circle = new PathShape { Name = "Circle", LayerId = cut.Id, Geometry = PathGeometry.Ellipse(30, 30, 8, 8) };
        var pic = new ImageShape { Name = "Photo", LayerId = picLayer.Id, Source = Picture(), SizeMm = new Vec2(20, 20), Transform = Affine.Translate(20, 20) };
        pic = pic.WithClip(circle.World);
        s = Ops.AddShapes(s, [pic, circle]);
        return (s, circle, pic, cut);
    }

    [Fact]
    public void TheMaskLayerIsNeverSent()
    {
        var l = new Layer { Mask = true };
        Assert.False(l.Output);
        Assert.False((l with { Output = true }).Output);
        Assert.True((l with { Mask = false }).Output);
        Assert.False((new Layer { Output = false } with { Mask = false }).Output);
    }

    [Fact]
    public void MarkMovesShapesOntoOneMaskLayer()
    {
        var (s, circle, pic, cut) = MaskedPicture();
        s = MaskLayer.Mark(s, new HashSet<Guid> { circle.Id, pic.Id });
        var ml = MaskLayer.Layer(s);
        Assert.NotNull(ml);
        Assert.Equal(MaskLayer.LayerName, ml!.Name);
        Assert.False(ml.Output);
        Assert.Equal(ml.Id, s.FindShape(circle.Id)!.LayerId);
        // Pictures aren't masks: the picture stays on its own layer.
        Assert.Equal(pic.LayerId, s.FindShape(pic.Id)!.LayerId);
        // Marking again reuses the same layer.
        var other = new PathShape { LayerId = cut.Id, Geometry = PathGeometry.Rect(0, 0, 5, 5) };
        s = MaskLayer.Mark(Ops.AddShapes(s, [other]), new HashSet<Guid> { other.Id });
        Assert.Single(s.Layers, l => l.Mask);
        Assert.Equal(ml.Id, s.FindShape(other.Id)!.LayerId);
    }

    [Fact]
    public void MasksOnTheMaskLayerAreLeftOutOfTheJob()
    {
        var (s, circle, _, _) = MaskedPicture();
        var withCut = JobBuilder.Build(s, new JobOptions());
        // Left on the cut layer, the circle is cut, and the job's checks say so.
        Assert.Contains(withCut.Warnings, w => w.Contains("\"Circle\"") && w.Contains("will be cut") && w.Contains("mask layer"));
        Assert.Contains(JobBuilder.OutputLayers(s), l => l.Name == "Cut");

        var masked = MaskLayer.Mark(s, new HashSet<Guid> { circle.Id });
        var job = JobBuilder.Build(masked, new JobOptions());
        Assert.DoesNotContain(job.Warnings, w => w.Contains("mask layer"));
        Assert.DoesNotContain(JobBuilder.OutputLayers(masked), l => l.Mask);
        // Only the picture is in the job now: fewer steps, and nothing from the circle's layer.
        Assert.True(job.Steps.Count < withCut.Steps.Count);
    }

    [Fact]
    public void ASubLayerOfTheMaskLayerIsNeverSentEither()
    {
        var (s, circle, _, _) = MaskedPicture();
        s = MaskLayer.Mark(s, new HashSet<Guid> { circle.Id });
        var ml = MaskLayer.Layer(s)!;
        var sub = Layer.Default(s.Layers.Count, LayerMode.Fill) with { ParentId = ml.Id, Name = "Sneaky fill" };
        s = Ops.AddLayer(s, sub);
        Assert.True(MaskLayer.IsMask(s, s.FindLayer(sub.Id)!));
        Assert.DoesNotContain(JobBuilder.OutputLayers(s), l => l.Id == sub.Id);
    }

    [Fact]
    public void TheMaskLayerIsSavedAndLoaded()
    {
        var (s, circle, _, _) = MaskedPicture();
        s = MaskLayer.Mark(s, new HashSet<Guid> { circle.Id });
        var back = DocFile.Deserialize(DocFile.Serialize(s));
        var ml = MaskLayer.Layer(back);
        Assert.NotNull(ml);
        Assert.False(ml!.Output);
        Assert.Equal(ml.Id, back.FindShape(circle.Id)!.LayerId);
        // Ordinary layers don't become masks.
        Assert.Single(back.Layers, l => l.Mask);
    }

    [Fact]
    public void AShapeThatOnlyLooksLikeTheMaskElsewhereIsntFlagged()
    {
        var (s, _, _, cut) = MaskedPicture();
        var elsewhere = new PathShape { Name = "Other circle", LayerId = cut.Id, Geometry = PathGeometry.Ellipse(60, 60, 8, 8) };
        s = Ops.AddShapes(s, [elsewhere]);
        var flagged = MaskLayer.LeftOnOutput(s, JobBuilder.OutputLayers(s)).Select(x => x.Shape.Name).ToList();
        Assert.Equal(["Circle"], flagged);
    }
}
