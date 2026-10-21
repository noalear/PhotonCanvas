using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using Xunit;

namespace PhotonCanvas.Tests;

public class InlayTests
{
    [Fact]
    public void APocketAsDeepAsTheVeneerAndThePieceOnItsOwnPage()
    {
        var s = DocState.New();
        var star = new PathShape { Name = "Star", LayerId = s.Layers[0].Id, Geometry = PathGeometry.Rect(40, 40, 20, 20) };
        s = Ops.AddShapes(s, [star]);
        var o = new InlayOptions
        {
            VeneerMm = 0.6, GapMm = 0.05, EtchMmPerPass = 0.15,
            PocketSettings = Layer.Default(0, LayerMode.Fill) with { Laser = LaserSettings.Blue(60, 200) },
            PieceSettings = Layer.Default(0, LayerMode.Cut) with { KerfMm = 0.12 },
        };
        Assert.Equal(4, Inlay.Passes(o)); // 0.6 mm in 0.15 mm passes
        Assert.Equal(5, Inlay.Passes(o with { VeneerMm = 0.65 })); // never short: rounded up
        var r = Inlay.Make(s, [star], o);
        // The board's page: the pocket, the gap bigger all round, 4 passes turned for a flat floor.
        var pocketLayer = r.Layers.Single(l => l.Name == Inlay.PocketLayer);
        Assert.Equal((LayerMode.Fill, 4, 60.0), (pocketLayer.Mode, pocketLayer.Laser.Passes, pocketLayer.Laser.Power));
        Assert.NotEqual(0, pocketLayer.AngleStepDeg);
        var pocket = r.Shapes.Single(x => x.LayerId == pocketLayer.Id);
        Assert.Equal(20.1, pocket.Bounds.Width, 3);
        // The piece: the veneer's page, the drawn size, the cut test's kerf on its layer.
        Assert.Contains("Inlay piece", Pages.Names(r));
        var onPiecePage = Pages.SwitchTo(r, Pages.Names(r).IndexOf("Inlay piece"));
        var pieceLayer = onPiecePage.Layers.Single(l => l.Name == Inlay.PieceLayer);
        Assert.Equal((LayerMode.Cut, 0.12), (pieceLayer.Mode, pieceLayer.KerfMm));
        var piece = onPiecePage.Shapes.Single(x => x.LayerId == pieceLayer.Id);
        Assert.Equal(new Rect2(40, 40, 60, 60), piece.Bounds);
    }
}
