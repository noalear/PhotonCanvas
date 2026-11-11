using PhotonCanvas.Core;
using PhotonCanvas.Doc;

namespace PhotonCanvas.Addon.Orders;

/// <summary>
/// Sample orders (an Etsy "Order Items" CSV and a Shopify orders export, made up) and sample
/// products with their designs, to try everything without a shop.
/// </summary>
public static class Samples
{
    public static string Csv(string name)
    {
        using var s = typeof(Samples).Assembly.GetManifestResourceStream("samples." + name)
            ?? throw new FileNotFoundException("Sample not found: " + name);
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }

    public static string EtsyCsv => Csv("etsy-order-items-sample.csv");
    public static string ShopifyCsv => Csv("shopify-orders-sample.csv");

    /// <summary>
    /// A name keyring (60 × 25 mm, a hole at one end, {Personalization} across the rest) and a round
    /// coaster (90 mm, the customer's two lines inside an engraved ring).
    /// </summary>
    public static IReadOnlyList<(Product Product, DocState Design)> Products()
    {
        var cut = Layer.Default(0, LayerMode.Cut) with { Name = "Cut" };
        var engrave = Layer.Default(1, LayerMode.Fill) with { Name = "Engrave", LinesPerCm = 150 };

        var keyOutline = new PathShape { Name = "Keyring", LayerId = cut.Id, Geometry = PathGeometry.RoundRect(0, 0, 60, 25, 5, 5) };
        var hole = new PathShape { Name = "Hole", LayerId = cut.Id, Geometry = PathGeometry.Ellipse(6.5, 12.5, 2.5, 2.5) };
        var name = Centred(TextShape.Create("{Personalization}", new TextStyle("Arial", 9, Bold: true, Align: TextAlign.Center, FitWidthMm: 42, FitHeightMm: 10), engrave.Id, Affine.Identity), new Vec2(36, 12.5));
        var keyring = new DocState([engrave, cut], [keyOutline, hole, name]);

        var cut2 = cut with { Id = Guid.NewGuid() };
        var engrave2 = engrave with { Id = Guid.NewGuid() };
        var coaster = new PathShape { Name = "Coaster", LayerId = cut2.Id, Geometry = PathGeometry.Ellipse(45, 45, 45, 45) };
        var line1 = Centred(TextShape.Create("{Personalization 1}", new TextStyle("Georgia", 10, Align: TextAlign.Center, FitWidthMm: 62, FitHeightMm: 14), engrave2.Id, Affine.Identity), new Vec2(45, 40));
        var line2 = Centred(TextShape.Create("{Personalization 2}", new TextStyle("Georgia", 6, Italic: true, Align: TextAlign.Center), engrave2.Id, Affine.Identity), new Vec2(45, 56));
        var ring = new PathShape { Name = "Ring", LayerId = engrave2.Id, Geometry = new PathGeometry([.. PathGeometry.Ellipse(45, 45, 39, 39).Subpaths, .. PathGeometry.Ellipse(45, 45, 38, 38).Subpaths]) };
        var coasterDesign = new DocState([engrave2, cut2], [coaster, ring, line1, line2]);

        return
        [
            (new Product { Name = "Name keyring", Matches = "KEY-WAL\nKEY-MAP\nKeychain", Material = "Wood 3 mm", Design = "Name keyring.pcanvas" }, keyring),
            (new Product { Name = "Slate coaster", Matches = "COASTER-RND\nCoaster", Material = "Slate", Design = "Slate coaster.pcanvas" }, coasterDesign),
        ];
    }

    /// <summary>The text moved so the middle of its box is at <paramref name="centre"/>.</summary>
    static TextShape Centred(TextShape t, Vec2 centre)
    {
        var b = t.Bounds;
        return (TextShape)t.With(transform: Affine.Translate(centre.X - b.Center.X, centre.Y - b.Center.Y));
    }
}
