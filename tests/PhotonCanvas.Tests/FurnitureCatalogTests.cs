using Clipper2Lib;
using PhotonCanvas.Addon.Furniture;

namespace PhotonCanvas.Tests;

public class FurnitureCatalogTests
{
    static readonly FurnitureSet Sheet = new() { Items = [], ThicknessMm = 2, FitMm = 0.1 };

    internal static string Check(FurnitureItem it, FurnitureSet set, bool joined = true)
    {
        var k = FurnitureKit.Make(it, set);
        var problems = new List<string>();
        if (!k.Ok) problems.Add("not ok: " + string.Join(" / ", k.Findings.Where(f => f.Level == Severity.Bad).Select(f => f.Text)));
        problems.AddRange(k.Findings.Where(f => f.Level == Severity.Warn).Select(f => "warn: " + f.Text));
        if (k.Parts.Count == 0) problems.Add("no parts");
        foreach (var p in k.Parts) if (Geo.Pieces(p.Cut) != 1) problems.Add($"{p.Name} in {Geo.Pieces(p.Cut)} pieces");
        var all = FurnitureTests.Assemble(k, set.ThicknessMm);
        problems.AddRange(FurnitureTests.Clashes(all).Select(c => "clash: " + c));
        problems.AddRange(EmptySlots(all, set.ThicknessMm, set.FitMm).Select(c => "empty slot: " + c));
        if (joined && FurnitureTests.Unjoined(all, set.ThicknessMm) is { Count: > 0 } loose) problems.Add("not all joined: " + string.Join(", ", loose));
        return string.Join("\n", problems);
    }

    /// <summary>Rectangular holes a sheet wide (slots) with no other piece through their middle; windows and lattice holes aren't slots.</summary>
    internal static List<string> EmptySlots(List<FurnitureTests.Placed> all, double T, double fit)
    {
        var empty = new List<string>();
        foreach (var a in all)
            foreach (var hole in Geo.Tidy(a.Cut).Where(p => Clipper.Area(p) < 0))
            {
                var b = Clipper.GetBounds(hole);
                double w = b.right - b.left, h = b.bottom - b.top;
                if (Math.Abs(Math.Abs(Clipper.Area(hole)) - w * h) > 0.02 * w * h) continue;
                bool slot = Math.Abs(Math.Min(w, h) - (T + fit)) < 0.02 || Math.Abs(Math.Max(w, h) - (T + fit)) < 0.02;
                if (!slot) continue;
                var c = a.Pose.At((b.left + b.right) / 2, (b.top + b.bottom) / 2);
                if (!all.Any(o => !ReferenceEquals(o, a) && o.Inside(c, 0.01))) empty.Add($"{a.Name}: slot at {(b.left + b.right) / 2:0.##}, {(b.top + b.bottom) / 2:0.##}");
            }
        return empty;
    }

    public static IEnumerable<object[]> Cabinets()
    {
        foreach (var f in Enum.GetValues<CabinetFront>())
            foreach (var c in Enum.GetValues<Crown>())
                foreach (var theme in new[] { "Medieval", "Gothic", "Sci-fi", "Desert" })
                    yield return [f, c, theme];
    }

    [Theory]
    [MemberData(nameof(Cabinets))]
    public void Every_cabinet_front_and_crown_fits_together(CabinetFront front, Crown crown, string theme)
    {
        var it = Defaults.For(ItemKind.Cabinet) with { Front = front, Crown = crown, Theme = theme, HeightMm = front == CabinetFront.Hutch ? 40 : 34 };
        Assert.Equal("", Check(it, Sheet));
    }

    public static IEnumerable<object[]> Entries() => FurnitureCatalog.All.Select(e => new object[] { e.Id });

    [Theory]
    [MemberData(nameof(Entries))]
    public void Every_catalog_piece_fits_together_in_its_own_theme(string id)
    {
        var e = FurnitureCatalog.Find(id)!;
        var set = FurnitureThemes.Apply(Sheet, e.Theme);
        var r = Check(e.Item, set, joined: e.Item.Kind != ItemKind.WallShelf);
        // A wall shelf's brackets are as thin as the sheet: it warns about that, as it always has.
        if (e.Item.Kind == ItemKind.WallShelf) r = string.Join("\n", r.Split('\n').Where(l => !l.Contains("brackets")));
        Assert.Equal("", r);
    }

    [Theory]
    [MemberData(nameof(Entries))]
    public void Every_catalog_piece_fits_together_for_bigger_figures_on_3_mm_sheet(string id)
    {
        // 32 mm figures on 3 mm sheet: everything gets bigger, the joints thicker.
        var e = FurnitureCatalog.Find(id)!;
        var set = FurnitureThemes.Apply(Sheet with { ThicknessMm = 3 }, e.Theme);
        var k = FurnitureKit.Make(FurnitureCatalog.Sized(e.Item, 54), set);
        Assert.True(k.Ok, string.Join("\n", k.Findings));
    }

    [Fact]
    public void Odd_sizes_and_sheets_are_refused_or_made_never_crash()
    {
        // Every catalog piece, tiny on thick sheet and huge on thin: refused with a reason, or made whole.
        foreach (var e in FurnitureCatalog.All)
            foreach (var (k, t) in new[] { (0.3, 3.0), (0.6, 6.0), (2.2, 1.0) })
            {
                var set = FurnitureThemes.Apply(Sheet with { ThicknessMm = t }, e.Theme);
                var kit = FurnitureKit.Make(FurnitureCatalog.Scale(e.Item, k), set);
                if (kit.Ok) Assert.True(kit.Parts.Count > 0 && kit.Parts.All(p => Geo.Pieces(p.Cut) >= 1), $"{e.Id} × {k} at {t} mm");
                else Assert.Contains(kit.Findings, f => f.Level == Severity.Bad && f.Text.Length > 10);
            }
    }

    [Fact]
    public void The_catalog_has_furniture_for_every_theme_and_its_ids_are_unique()
    {
        var all = FurnitureCatalog.All;
        Assert.Equal(all.Count, all.Select(e => e.Id).Distinct().Count());
        foreach (var theme in FurnitureThemes.Names)
        {
            var mine = FurnitureCatalog.For(theme, null).ToList();
            Assert.True(mine.Count >= 15, $"{theme} has only {mine.Count} pieces");
            Assert.All(mine, e => Assert.Equal(theme, e.Item.Theme));
            Assert.True(mine.Select(e => e.Room).Distinct().Count() >= 7, $"{theme} has too few rooms");
        }
        Assert.All(all, e => Assert.Contains(e.Room, FurnitureCatalog.Rooms));
        // Shown by theme, then by room.
        var order = all.Select(e => (FurnitureThemes.Names.ToList().IndexOf(e.Theme), FurnitureCatalog.Rooms.ToList().IndexOf(e.Room))).ToList();
        Assert.Equal(order.OrderBy(x => x.Item1).ThenBy(x => x.Item2), order);
    }

    [Fact]
    public void Catalog_pieces_round_trip_through_json_and_scale_with_the_figures()
    {
        var set = Sheet with { Items = [.. FurnitureCatalog.All.Select(e => e.Item)] };
        var back = FurnitureSet.FromJson(set.ToJson())!;
        Assert.Equal(set.Items, back.Items);
        var throne = FurnitureCatalog.Find("med-throne")!;
        var big = FurnitureCatalog.Sized(throne.Item, 56);
        Assert.Equal(throne.Item.HeightMm * 2, big.HeightMm, 1);
        Assert.Equal(throne.Item.ArmMm * 2, big.ArmMm, 1);
        Assert.Equal(FurnitureSpot.Floor, FurnitureCatalog.Find("med-hall-rug")!.Spot);
        Assert.Equal(FurnitureSpot.OnWall, FurnitureCatalog.Find("med-tapestry")!.Spot);
        Assert.Equal(FurnitureSpot.Wall, FurnitureCatalog.Find("med-wardrobe")!.Spot);
    }

    [Fact]
    public void A_piece_keeps_its_own_themes_look_in_a_set_of_another_theme()
    {
        var locker = FurnitureCatalog.Find("sci-lockers")!.Item;
        var medieval = FurnitureThemes.Apply(Sheet with { Items = [locker] }, "Medieval");
        var m = new Maker(locker, medieval, 0);
        Assert.Equal("Sci-fi", m.Theme);
        Assert.Equal(WoodDetail.Metal, m.Detail);
    }
}
