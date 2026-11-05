using Clipper2Lib;
using PhotonCanvas.Addon.Furniture;

namespace PhotonCanvas.Tests;

/// <summary>Crates' boarded faces: every style, every face detail, the words stencilled on them.</summary>
public class CrateTests
{
    static readonly FurnitureSet Sheet = new() { Items = [], ThicknessMm = 2, FitMm = 0.1 };

    static ItemKit Make(FurnitureItem it, WoodDetail detail = WoodDetail.Planks) => FurnitureKit.Make(it, Sheet with { Detail = detail });
    static Part Part(ItemKit k, string name) => k.Parts.Single(p => p.Name == name);
    static FurnitureItem Crate(CrateStyle style, double w = 12, double d = 12, double h = 12) =>
        Defaults.For(ItemKind.Crate) with { CrateStyle = style, WidthMm = w, DepthMm = d, HeightMm = h };

    public static IEnumerable<object[]> Styles() =>
        from s in Enum.GetValues<CrateStyle>()
        from d in new[] { WoodDetail.None, WoodDetail.Grain, WoodDetail.Planks, WoodDetail.Metal }
        from size in new[] { 12.0, 24.0 }
        select new object[] { s, d, size };

    [Theory]
    [MemberData(nameof(Styles))]
    public void Every_style_makes_a_whole_crate_with_its_detail_on_the_pieces(CrateStyle style, WoodDetail detail, double size)
    {
        var k = Make(Crate(style, size, size * 0.7, size * 0.7), detail);
        Assert.True(k.Ok, string.Join("; ", k.Findings.Select(f => f.Text)));
        Assert.Equal(6, k.Parts.Count);
        foreach (var p in k.Parts)
        {
            Assert.Equal(1, Geo.Pieces(p.Cut));
            // The detail stays on the piece.
            Assert.Equal(0, Geo.Area(Geo.Subtract(p.Etched, p.Cut)), 3);
            foreach (var l in p.Lines)
                foreach (var q in l)
                    Assert.NotEqual(PointInPolygonResult.IsOutside, Clipper.PointInPolygon(q, Geo.Offset(p.Cut, 0.01)[0]));
        }
        // Something shows on the front, whatever the detail.
        var front = Part(k, "Front");
        Assert.True(Geo.Area(front.Etched) > 0.5 || front.Lines.Count > 3, $"{style} {detail}: nothing on the front");
    }

    [Fact]
    public void Grain_and_planks_look_different_on_a_crate()
    {
        foreach (var style in Enum.GetValues<CrateStyle>().Where(s => s is not CrateStyle.Slatted))
        {
            var it = Crate(style, 20, 14, 14);
            var none = Part(Make(it, WoodDetail.None), "Front");
            var grain = Part(Make(it, WoodDetail.Grain), "Front");
            var planks = Part(Make(it, WoodDetail.Planks), "Front");
            // No detail: the boards' grooves but no grain.
            Assert.True(grain.Lines.Count > none.Lines.Count + 5, $"{style}: grain adds grain lines");
            // Planks: the infill split into planks, so more grooves than one wide board.
            Assert.True(Geo.Area(planks.Etched) > Geo.Area(grain.Etched) + 0.5, $"{style}: planks add plank grooves ({Geo.Area(planks.Etched):0.00} vs {Geo.Area(grain.Etched):0.00})");
        }
    }

    [Fact]
    public void Each_style_looks_different()
    {
        var fronts = Enum.GetValues<CrateStyle>().Select(s => Part(Make(Crate(s, 20, 14, 14)), "Front")).ToList();
        for (int i = 0; i < fronts.Count; i++)
            for (int j = i + 1; j < fronts.Count; j++)
            {
                double diff = Geo.Area(Geo.Subtract(fronts[i].Etched, fronts[j].Etched)) + Geo.Area(Geo.Subtract(fronts[j].Etched, fronts[i].Etched));
                Assert.True(diff > 2, $"{(CrateStyle)i} and {(CrateStyle)j} etch nearly the same");
            }
    }

    [Fact]
    public void Plain_boards_are_planks_without_a_frame()
    {
        var plain = Part(Make(Defaults.For(ItemKind.Crate) with { Bands = false, WidthMm = 20, DepthMm = 14, HeightMm = 14 }), "Front");
        var braced = Part(Make(Crate(CrateStyle.Braced, 20, 14, 14)), "Front");
        Assert.True(Geo.Area(plain.Etched) > 0.5, "plank grooves");
        Assert.True(Geo.Area(braced.Etched) > Geo.Area(plain.Etched), "the frame and brace add grooves");
    }

    [Fact]
    public void Stencilled_words_are_the_ones_asked_for()
    {
        var few = Part(Make(Crate(CrateStyle.Cargo, 24, 16, 16) with { Label = "I" }, WoodDetail.None), "Front");
        var many = Part(Make(Crate(CrateStyle.Cargo, 24, 16, 16) with { Label = "WWWWWW" }, WoodDetail.None), "Front");
        Assert.True(Geo.Area(many.Etched) > Geo.Area(few.Etched) + 3, $"{Geo.Area(many.Etched):0.0} vs {Geo.Area(few.Etched):0.0}");
        // Left empty, some words are picked: more is burnt than the frame alone.
        var frame = Part(Make(Crate(CrateStyle.Braced, 24, 16, 16), WoodDetail.None), "Front");
        var picked = Part(Make(Crate(CrateStyle.Cargo, 24, 16, 16), WoodDetail.None), "Front");
        Assert.True(Geo.Area(picked.Etched) > Geo.Area(frame.Etched) * 0.8 + 3);
        // An O comes out in two halves, as a stencil paints it.
        var o = Part(Make(Crate(CrateStyle.Cargo, 24, 16, 16) with { Label = "O" }, WoodDetail.None), "Front");
        var plain = Part(Make(Crate(CrateStyle.Cargo, 24, 16, 16) with { Label = "I" }, WoodDetail.None), "Front");
        Assert.True(Geo.Pieces(o.Etched) >= Geo.Pieces(plain.Etched) + 1, $"the O's bridges split it ({Geo.Pieces(o.Etched)} vs {Geo.Pieces(plain.Etched)}, areas {Geo.Area(o.Etched):0.0} {Geo.Area(plain.Etched):0.0})");
    }

    [Fact]
    public void Old_crates_keep_their_braced_look_and_settings_round_trip()
    {
        // A crate saved before the styles came in has no CrateStyle: it's braced, as it was.
        var old = FurnitureSet.FromJson("{\"Items\":[{\"Kind\":\"Crate\",\"WidthMm\":12,\"DepthMm\":12,\"HeightMm\":12,\"Bands\":true}]}")!;
        Assert.Equal(CrateStyle.Braced, old.Items[0].CrateStyle);
        var s = new FurnitureSet { Items = [Crate(CrateStyle.Military) with { Label = "RATIONS" }] };
        var back = FurnitureSet.FromJson(s.ToJson())!;
        Assert.Equal(CrateStyle.Military, back.Items[0].CrateStyle);
        Assert.Equal("RATIONS", back.Items[0].Label);
    }
}
