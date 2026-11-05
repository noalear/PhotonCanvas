using Clipper2Lib;
using PhotonCanvas.Addon.Furniture;

namespace PhotonCanvas.Tests;

/// <summary>Crates' boarded faces: every style, every face detail, the words stencilled on them.</summary>
public class CrateTests
{
    static readonly FurnitureSet Sheet = new() { Items = [], ThicknessMm = 2, FitMm = 0.1 };

    static ItemKit Make(FurnitureItem it, WoodDetail detail = WoodDetail.Planks) => FurnitureKit.Make(it, Sheet with { Detail = detail });
    static Part Part(ItemKit k, string name) => k.Parts.Single(p => p.Name == name);

    /// <summary>A face as it looks put together: the panel and what's glued on over it (boards, iron, trim), flat.</summary>
    static Part Face(ItemKit k, string name)
    {
        var panel = Part(k, name);
        var on = k.Parts.Where(p => p.Glued && p.Name.StartsWith(name + " ")).ToList();
        var etched = Geo.Union(new PathsD(on.SelectMany(p => p.Etched).Concat(Geo.Subtract(panel.Etched, Geo.Union(new PathsD(on.SelectMany(p => p.Cut)))))));
        return new Part(name, panel.Cut, etched, [.. panel.Lines, .. on.SelectMany(p => p.Lines)], panel.Poses);
    }
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
        // Six panels, and the boards and trim glued on over them.
        Assert.Equal(6, k.Parts.Count(p => !p.Glued));
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
        var front = Face(k, "Front");
        Assert.True(Geo.Area(front.Etched) > 0.5 || front.Lines.Count > 3, $"{style} {detail}: nothing on the front");
    }

    [Fact]
    public void Grain_and_planks_look_different_on_a_crate()
    {
        // (Slatted crates are all slats; carved ones have no planks inside the frame.)
        foreach (var style in Enum.GetValues<CrateStyle>().Where(s => s is not (CrateStyle.Slatted or CrateStyle.Carved or CrateStyle.Arched)))
        {
            // (The same words on each, so only the wood differs.)
            var it = Crate(style, 20, 14, 14) with { Label = "SALT" };
            var none = Face(Make(it, WoodDetail.None), "Front");
            var grain = Face(Make(it, WoodDetail.Grain), "Front");
            var planks = Face(Make(it, WoodDetail.Planks), "Front");
            // No detail: the boards' grooves but no grain.
            Assert.True(grain.Lines.Count > none.Lines.Count + 5, $"{style}: grain adds grain lines");
            // Planks: the infill split into planks, so more grooves than one wide board.
            Assert.True(Geo.Area(planks.Etched) > Geo.Area(grain.Etched) + 0.5, $"{style}: planks add plank grooves ({Geo.Area(planks.Etched):0.00} vs {Geo.Area(grain.Etched):0.00})");
        }
    }

    [Fact]
    public void Each_style_looks_different()
    {
        var fronts = Enum.GetValues<CrateStyle>().Select(s => Face(Make(Crate(s, 20, 14, 14)), "Front")).ToList();
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
        var plain = Face(Make(Defaults.For(ItemKind.Crate) with { Bands = false, WidthMm = 20, DepthMm = 14, HeightMm = 14 }), "Front");
        // (Push fit: the frame etched on, on a box the same size.)
        var braced = Part(FurnitureKit.Make(Crate(CrateStyle.Braced, 20, 14, 14), Sheet with { Detail = WoodDetail.Planks, PushFit = true }), "Front");
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

    public static IEnumerable<object[]> Themed() =>
        from s in Enum.GetValues<CrateStyle>()
        from t in new[] { "Medieval", "Dungeon", "Cave", "Sci-fi", "Desert", "Gothic" }
        from h in new[] { false, true }
        select new object[] { s, t, h };

    [Theory]
    [MemberData(nameof(Themed))]
    public void Every_style_in_every_theme_goes_together_with_its_boards_glued_on(CrateStyle style, string theme, bool handles)
    {
        var it = Crate(style, 18, 13, 13) with { Theme = theme, Handles = handles };
        Assert.Equal("", FurnitureCatalogTests.Check(it, Sheet));
        // Under push fit nothing's glued: it's all etched on the panels.
        var push = FurnitureKit.Make(it, Sheet with { PushFit = true });
        Assert.True(push.Ok);
        Assert.DoesNotContain(push.Parts, p => p.Glued);
    }

    [Fact]
    public void A_braced_crate_has_its_frame_and_brace_glued_on_proud_of_each_face()
    {
        var k = Make(Crate(CrateStyle.Braced, 16, 12, 12));
        foreach (var face in new[] { "Front", "Back", "Left end", "Right end", "Lid" })
        {
            var boards = k.Parts.Where(p => p.Glued && p.Name.StartsWith(face + " ")).ToList();
            Assert.NotEmpty(boards);
            // They cover the frame, not the whole face.
            var panel = Part(k, face);
            double area = Geo.Area(new PathsD(boards.SelectMany(b => b.Cut)));
            Assert.InRange(area, Geo.Area(panel.Cut) * 0.3, Geo.Area(panel.Cut) * 2.2);
        }
        // The long sides' boards reach over the ends' to close the corners, and the crate keeps its outside size.
        var front = k.Parts.Single(p => p.Name == "Front boards");
        var b = Geo.Bounds(front.Cut);
        Assert.Equal(16, b.right - b.left, 2);
        Assert.Equal(16 - 4, Geo.Bounds(Part(k, "Front").Cut) is var f ? f.right - f.left : 0, 2);
        var all = FurnitureTests.Assemble(k, 2);
        Assert.Equal(16, all.Max(p => p.Box().Max.X) - all.Min(p => p.Box().Min.X), 2);
        Assert.Equal(12, all.Max(p => p.Box().Max.Y) - all.Min(p => p.Box().Min.Y), 2);
        Assert.Equal(12, all.Max(p => p.Box().Max.Z) - all.Min(p => p.Box().Min.Z), 2);
    }

    [Theory]
    [InlineData(CrateLoad.Apples, CrateStyle.Slatted)]
    [InlineData(CrateLoad.Sacks, CrateStyle.Carved)]
    [InlineData(CrateLoad.Rocks, CrateStyle.Lashed)]
    [InlineData(CrateLoad.Apples, CrateStyle.Braced)]
    public void An_open_crate_holds_its_load_heaped_over_the_rim(CrateLoad load, CrateStyle style)
    {
        var it = Crate(style, 16, 13, 9) with { Lid = LidStyle.None, Load = load };
        Assert.Equal("", FurnitureCatalogTests.Check(it, Sheet));
        var k = Make(it);
        var heap = k.Parts.Single(p => p.Name == load.ToString());
        var b = Geo.Bounds(heap.Cut);
        Assert.True(b.bottom > 9 + 1, $"{load} tops out at {b.bottom:0.0}");
        Assert.True(heap.Lines.Count > 4, "outlined");
        // Its tabs go down through the bottom.
        Assert.InRange(Geo.Tidy(Part(k, "Bottom").Cut).Count(p => Clipper.Area(p) < 0), 1, 2);
        // A closed crate has nothing in it.
        Assert.DoesNotContain(Make(it with { Lid = LidStyle.Flat }).Parts, p => p.Name == load.ToString());
    }

    [Fact]
    public void Crate_handles_and_loads_round_trip()
    {
        var s = new FurnitureSet { Items = [Crate(CrateStyle.Lashed) with { Handles = true, Load = CrateLoad.Rocks, Lid = LidStyle.None }] };
        var back = FurnitureSet.FromJson(s.ToJson())!.Items[0];
        Assert.Equal(CrateStyle.Lashed, back.CrateStyle);
        Assert.True(back.Handles);
        Assert.Equal(CrateLoad.Rocks, back.Load);
    }
}
