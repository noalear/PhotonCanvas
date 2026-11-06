using Clipper2Lib;
using PhotonCanvas.Addon.CityBuilder;
using PhotonCanvas.Addon.HouseEditor;
using PhotonCanvas.App.View3D;

namespace PhotonCanvas.Tests;

/// <summary>
/// The town's buildings as they'll be once they're cut and put together: made plain (the same pieces, quicker),
/// the ground each covers with its columns, roofs' overhang and open doors, where two would run into each other,
/// and the 3D view drawing them true to size.
/// </summary>
public class CityBuiltTests
{
    static HashSet<(int X, int Y)> Rect(int x, int y, int w, int d)
    {
        var s = new HashSet<(int, int)>();
        for (int j = y; j < y + d; j++) for (int i = x; i < x + w; i++) s.Add((i, j));
        return s;
    }

    static CityBuilding Made(int x, int y, int w, int d, int turn = 0)
    {
        var (design, mx, my) = BuildingGuess.Make(new SeenBuilding { Cells = Rect(x, y, w, d) }, "Medieval", 25.4)!.Value;
        return new CityBuilding { X = mx, Y = my, Design = design, Turn = turn };
    }

    [Fact]
    public void The_middle_is_where_the_kit_puts_the_building()
    {
        foreach (var d in new[] { Made(0, 0, 4, 4).Design, Made(0, 0, 5, 7).Design, HouseDesign.Sample() })
            Assert.Equal(HouseKitMaker.Make(d).Origin, HouseKitMaker.Middle(d));
        Assert.Equal((0.0, 0.0), HouseKitMaker.Middle(new HouseDesign { Levels = [new()] }));
    }

    [Fact]
    public void Made_plain_it_has_the_same_pieces_in_the_same_places()
    {
        var d = Made(0, 0, 4, 5).Design;
        var plain = CityBuilt.Plain(d);
        Assert.Equal(Addon.Walls.Textures.Plain, plain.Style.Texture);
        Assert.All(plain.Roofs, r => Assert.Equal(Addon.Walls.Textures.Plain, r.Covering));
        var real = HouseKitMaker.Make(d).Solids;
        var quick = HouseKitMaker.Make(plain).Solids;
        Assert.Equal(real.Count, quick.Count);
        for (int i = 0; i < real.Count; i++)
        {
            Assert.Equal(real[i].Kind, quick[i].Kind);
            Assert.Equal(real[i].Pose, quick[i].Pose);
            Assert.Equal(Math.Abs(Clipper.Area(real[i].Outline)), Math.Abs(Clipper.Area(quick[i].Outline)), 6);
        }
        // Only the shapes are kept, without what's etched.
        var m = CityBuilt.Build(d);
        Assert.True(m.Ok);
        Assert.All(m.Solids, s => Assert.Null(s.Etched));
        Assert.True(m.Solids.Sum(s => s.Outline.Sum(p => p.Count)) < real.Sum(s => s.Outline.Sum(p => p.Count)));
    }

    [Fact]
    public void As_built_a_building_covers_more_than_its_plan()
    {
        var b = Made(0, 0, 4, 4);
        var m = CityBuilt.Build(b.Design);
        Assert.True(m.Ok);
        Assert.True(m.Count > 20);
        var box = m.Box;
        // Its corner columns and the roof's overhang reach past the plan's 4 × 4 squares all round, though not far.
        Assert.InRange(-box.left, 0.3, 1.5);
        Assert.InRange(-box.top, 0.3, 1.5);
        Assert.InRange(box.right - 4, 0.3, 1.5);
        Assert.InRange(box.bottom - 4, 0.3, 1.5);
        // Its ground covers all its plan.
        var plan = new PathsD { new PathD { new(0, 0), new(4, 0), new(4, 4), new(0, 4) } };
        var outside = Clipper.Difference(plan, m.Extent, FillRule.NonZero);
        Assert.True(outside.Sum(p => Math.Abs(Clipper.Area(p))) < 0.01);

        // On the map, where the building is, turned with it.
        var moved = b with { X = 10, Y = 6 };
        var e = Clipper.GetBounds(CityBuilt.Extent(moved, m));
        Assert.Equal(10 + box.left, e.left, 6);
        Assert.Equal(6 + box.top, e.top, 6);
        var long_ = Made(0, 0, 3, 6);
        var lm = CityBuilt.Build(long_.Design);
        var turned = Clipper.GetBounds(CityBuilt.Extent(long_ with { Turn = 1 }, lm));
        Assert.True(turned.right - turned.left > turned.bottom - turned.top);
    }

    [Fact]
    public async Task Buildings_a_square_apart_run_into_each_other_but_not_two_apart()
    {
        var a = Made(0, 0, 4, 4);
        foreach (var gap in new[] { 1, 2 })
        {
            var b = Made(4 + gap, 0, 4, 4);
            var map = new CityMap { Across = 20, Down = 10, Buildings = [a, b] };
            await CityBuilt.Get(a.Design);
            await CityBuilt.Get(b.Design);
            var hits = CityBuilt.Intrusions(map);
            if (gap == 1)
            {
                var hit = Assert.Single(hits);
                Assert.True(hit.Where.Sum(p => Math.Abs(Clipper.Area(p))) > 0.5);
                var where = Clipper.GetBounds(hit.Where);
                Assert.True(where.left > 3.5 && where.right < 5.5);
            }
            else Assert.Empty(hits);
            Assert.Equal((map.Buildings.Select(x => x.Design).Distinct().Count(), map.Buildings.Count), CityBuilt.Progress(map));
        }
        Assert.Equal(2, TownMaker.Apart);
    }

    [Fact]
    public async Task Each_building_is_made_once_and_kept()
    {
        var d = Made(0, 0, 5, 5).Design;
        var first = CityBuilt.Get(d);
        var again = CityBuilt.Get(d);
        var m = await first;
        Assert.Same(m, await again);
        // Kept by what it is: the same design read back from its file is the same building.
        var copy = HouseDesign.FromJson(d.ToJson())!;
        Assert.Same(m, CityBuilt.Known(copy));
        Assert.Same(m, await CityBuilt.Get(copy));
        // Nothing left to make on a map of it.
        var map = new CityMap { Across = 10, Down = 10, Buildings = [new CityBuilding { Design = d }, new CityBuilding { X = 6, Design = copy }] };
        Assert.Equal(0, CityBuilt.WarmUp(map));
    }

    [Fact]
    public async Task A_made_town_s_buildings_fit_together_once_built()
    {
        var (town, _) = TownMaker.Make(new TownOptions { Name = "Fit", Across = 48, Down = 36, Layout = TownLayout.Crossroads, Seed = 3, Theme = "Medieval" });
        Assert.True(town.Buildings.Count >= 6);
        await Task.WhenAll(town.Buildings.Select(b => CityBuilt.Get(b.Design)));
        Assert.All(town.Buildings, b => Assert.True(CityBuilt.Known(b.Design) is { Ok: true }, b.Name));
        Assert.Empty(CityBuilt.Intrusions(town));
        // And the sample village's.
        var village = CityWindow.Sample();
        await Task.WhenAll(village.Buildings.Select(b => CityBuilt.Get(b.Design)));
        Assert.Empty(CityBuilt.Intrusions(village));
    }

    [Fact]
    public async Task The_3D_view_shows_buildings_as_built_once_they_re_made()
    {
        var b = Made(2, 2, 4, 4);
        var map = new CityMap { Across = 10, Down = 10, Buildings = [b] };
        var m = await CityBuilt.Get(b.Design);
        // On the whole map: every building as built once it's made, as plain blocks if not asked for.
        var whole = new Scene3D();
        CityScene.Build(whole, map, new CityShow());
        Assert.False(whole.IsEmpty);
        var light = new Scene3D();
        CityScene.AddBuilding(light, b, 25.4, new CityShow(), false);
        var built = new Scene3D();
        CityScene.AddBuilt(built, b, m, 25.4, new CityShow(), false);
        var open = new Scene3D();
        CityScene.AddBuilt(open, b, m, 25.4, new CityShow { DoorsOpen = true }, false);
        // Seen from above, as built it's wider than the plain blocks: its corner columns and its roof's overhang.
        var cam = Camera3D.YUp(0, 89.9);
        var painter = new Model3DPainter();
        Assert.NotNull(painter.Picture(light, cam, 200, 200, Avalonia.Media.Colors.White));
        double lightWidth = 1 / painter.PxPerMm;
        Assert.NotNull(painter.Picture(built, cam, 200, 200, Avalonia.Media.Colors.White));
        double builtWidth = 1 / painter.PxPerMm;
        Assert.True(builtWidth > lightWidth * 1.15, $"{builtWidth} vs {lightWidth}");
        // The doors swung open make another picture.
        Assert.NotEqual(Picture(built), Picture(open));

        static string Picture(Scene3D s) => Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(
            System.Runtime.InteropServices.MemoryMarshal.AsBytes(new Model3DPainter().Picture(s, Camera3D.YUp(30, 30), 160, 120, Avalonia.Media.Colors.White)!.AsSpan())));
    }
}
