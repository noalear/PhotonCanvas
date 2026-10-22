using System.Text;
using PhotonCanvas.Core;
using PhotonCanvas.Core.Imaging;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Export;
using PhotonCanvas.Doc.Import;
using PhotonCanvas.Doc.Tracing;
using Xunit;

namespace PhotonCanvas.Tests;

public class PictureToolTests
{
    static RasterImage Solid(int w, int h, byte r, byte g, byte b)
    {
        var px = new byte[w * h * 4];
        for (int i = 0; i < w * h; i++) { px[i * 4] = r; px[i * 4 + 1] = g; px[i * 4 + 2] = b; px[i * 4 + 3] = 255; }
        return new RasterImage(w, h, px);
    }

    [Fact]
    public void FiltersCropAndCmyk()
    {
        var red = Solid(20, 10, 200, 40, 40);
        var grey = PictureTools.Apply(red, PictureFilter.Greyscale, 0);
        Assert.Equal(grey.Rgba[0], grey.Rgba[1]);
        var bw = PictureTools.Apply(red, PictureFilter.BlackAndWhite, 0.5);
        Assert.Equal(0, bw.Rgba[0]);
        var crop = PictureTools.Crop(red, 5, 2, 10, 4);
        Assert.Equal((10, 4), (crop.Width, crop.Height));
        var plates = PictureTools.CmykSplit(Solid(4, 4, 0, 255, 255)); // cyan
        Assert.Equal("Cyan", plates[0].Ink);
        Assert.Equal(0, plates[0].Plate.Rgba[0]);     // full cyan ink
        Assert.Equal(255, plates[1].Plate.Rgba[0]);   // no magenta
        // Split as the tone settings show it: white inverted is black, all black ink.
        var inverted = PictureTools.CmykSplit(Solid(4, 4, 255, 255, 255), new ToneAdjust(Invert: true));
        Assert.Equal(0, inverted[3].Plate.Rgba[0]);
        Assert.Equal(255, inverted[0].Plate.Rgba[0]);
        foreach (var f in Enum.GetValues<PictureFilter>()) Assert.Equal(800, PictureTools.Apply(red, f, 0.5).Rgba.Length);
        Assert.Equal(red.Width, PictureTools.ShadeRelief(red, 2, 0.1).Width);
    }

    [Fact]
    public void CentreLineThroughAThickStroke()
    {
        int w = 60, h = 20;
        var ink = new bool[w * h];
        for (int y = 7; y < 13; y++)
            for (int x = 5; x < 55; x++) ink[y * w + x] = true;
        var lines = Centreline.Trace(ink, w, h);
        var line = Assert.Single(lines);
        Assert.All(line, p => Assert.InRange(p.Y, 8, 12));
        Assert.True(line.Max(p => p.X) - line.Min(p => p.X) > 40);
    }

    [Fact]
    public void SingleLineText()
    {
        var g = TextLayout.Build("L", new TextStyle("Arial", 20, SingleLine: true));
        Assert.NotEmpty(g.Subpaths);
        Assert.Contains(g.Subpaths, s => !s.Closed);
        // An L is two strokes meeting at the corner: a few lines, not an outline.
        Assert.InRange(g.Subpaths.Length, 1, 4);
    }

    [Fact]
    public void StlToDepth()
    {
        // A square pyramid, 10 × 10 base, 5 high, as text STL.
        string V(double x, double y, double z) => FormattableString.Invariant($"vertex {x} {y} {z}\n");
        var sb = new StringBuilder("solid p\n");
        (double, double)[] c = [(0, 0), (10, 0), (10, 10), (0, 10)];
        for (int i = 0; i < 4; i++)
        {
            var (ax, ay) = c[i];
            var (bx, by) = c[(i + 1) % 4];
            sb.Append("facet normal 0 0 1\nouter loop\n").Append(V(ax, ay, 0)).Append(V(bx, by, 0)).Append(V(5, 5, 5)).Append("endloop\nendfacet\n");
        }
        sb.Append("endsolid p\n");
        var tris = StlDepth.Parse(Encoding.ASCII.GetBytes(sb.ToString()));
        Assert.Equal(4, tris.Count);
        var (depth, w, h, range) = StlDepth.Render(tris, StlView.Top, 100);
        Assert.Equal(10, w, 6);
        Assert.Equal(5, range, 6);
        Assert.True(depth.Rgba[(50 * depth.Width + 50) * 4] > 240); // the peak is white
        Assert.True(depth.Rgba[(2 * depth.Width + 50) * 4] < 40);   // the base edge is dark
    }

    [Fact]
    public void DesignExports()
    {
        var s = DocState.New();
        s = Ops.AddShapes(s, [new PathShape { LayerId = s.Layers[0].Id, Geometry = PathGeometry.Rect(10, 10, 20, 20) }]);
        var svg = DesignExport.Svg(s);
        Assert.Contains("<path d=\"M", svg);
        Assert.Contains("width=\"20mm\"", svg);
        var dxf = DesignExport.Dxf(s);
        Assert.Contains("POLYLINE", dxf);
        Assert.Contains("\nEOF\n", dxf);
        var png = DesignExport.Png(s, 100);
        Assert.Equal(0x89, png[0]);
        var round = SvgImport.ParseString(svg);
        Assert.NotEmpty(round.Paths);
    }

    [Fact]
    public void OnlyOneLayer()
    {
        var s = DocState.New();
        var a = s.Layers[0];
        var b = Layer.Default(1, LayerMode.Score);
        s = Ops.AddLayer(s, b);
        s = Ops.AddShapes(s, [new PathShape { LayerId = a.Id, Geometry = PathGeometry.Rect(10, 10, 5, 5) }, new PathShape { LayerId = b.Id, Geometry = PathGeometry.Rect(30, 10, 5, 5) }]);
        Assert.Equal(2, JobBuilder.Build(s, new JobOptions()).LayerTimes.Count);
        var only = JobBuilder.Build(s, new JobOptions { OnlyLayer = b.Id });
        Assert.Equal(b.Id, Assert.Single(only.LayerTimes).LayerId);
    }
}
