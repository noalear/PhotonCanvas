using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using Xunit;

namespace PhotonCanvas.Tests;

public class PagesUpdateTests
{
    [Fact]
    public void ALayerChangesOnTheChosenPagesOnly()
    {
        var s = DocState.New();
        s = s with { Layers = [s.Layers[0] with { Name = "Slices · cut" }] };
        s = Ops.AddShapes(s, [new PathShape { LayerId = s.Layers[0].Id, Geometry = PathGeometry.Rect(10, 10, 10, 10) }]);
        s = Pages.Add(s);   // page 2, its own copy of the layer
        s = Pages.Add(s);   // page 3, shown
        var updated = Pages.UpdateLayers(s, 1, 2, l => l.Name == "Slices · cut", l => l with { KerfMm = 0.15 });
        Assert.Equal(2, updated.PageIndex); // still showing page 3
        Assert.Equal(0.15, updated.Layers.Single().KerfMm);
        Assert.Equal([0.0, 0.15], updated.OtherPages.Select(p => p.Layers.Single().KerfMm)); // page 1 untouched, page 2 changed
    }
}
