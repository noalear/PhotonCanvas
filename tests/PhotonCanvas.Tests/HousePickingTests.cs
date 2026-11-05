using PhotonCanvas.Addon.Furniture;
using PhotonCanvas.Addon.HouseEditor;

namespace PhotonCanvas.Tests;

/// <summary>
/// Clicking the building in the editor's 3D view: a click on a piece picks what it's part of on the plan (the
/// wall, the column, the door, the roof, the furniture, the stairs, the room), and what's picked shows in 3D.
/// </summary>
public class HousePickingTests
{
    static readonly HouseDesign D = HouseDesign.Sample();
    static readonly HouseKit Kit = HouseKitMaker.Make(D);

    static PlanPick? PickAtMiddle(HouseSolid s) => HousePicking.Pick(D, Kit, s, HousePicking.Centre(s));

    [Fact]
    public void A_click_on_a_wall_picks_that_wall_and_it_shows_picked()
    {
        var walls = Kit.Solids.Where(s => s.Kind == SolidKind.Wall && !s.Name.Contains("shutter", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.NotEmpty(walls);
        int picked = 0;
        foreach (var w in walls)
        {
            var p = PickAtMiddle(w);
            Assert.NotNull(p);
            Assert.Equal(w.Level, p.Level);
            if (p.Kind != PickKind.Wall) continue;
            picked++;
            Assert.True(HousePicking.Shows(D, Kit, w, p), $"{w.Name} isn't shown picked");
            // Only the walls along that run, not the rest.
            var lit = Kit.Solids.Where(s => HousePicking.Shows(D, Kit, s, p)).ToList();
            Assert.All(lit, s => Assert.Equal(SolidKind.Wall, s.Kind));
            Assert.True(lit.Count < walls.Count / 2, $"{lit.Count} of {walls.Count} wall pieces lit for one wall");
        }
        Assert.True(picked > walls.Count / 3, $"only {picked} of {walls.Count} wall pieces picked their wall");
    }

    [Fact]
    public void A_click_on_a_window_in_a_wall_picks_the_window()
    {
        var ground = D.Levels[0];
        int window = ground.Openings.FindIndex(o => o.Kind == OpeningKind.Window);
        var o = ground.Openings[window];
        // The wall it's in, at the window's middle height.
        var wall = Kit.Solids.Where(s => s.Kind == SolidKind.Wall && s.Level == 0)
            .OrderBy(s => Distance(HousePicking.ToPlan(Kit, D, s.Middle), (o.X, o.Y))).First();
        double sq = D.SquareMm;
        var at = new V3((o.X - Kit.Origin.X) * sq, 0, (o.Y - Kit.Origin.Y) * sq);
        double floor = wall.Middle.Y - Addon.Buildings.BuildingKitMaker.StoreyHeight(HouseKitMaker.StyleFor(D)) / 2;
        var pick = HousePicking.Pick(D, Kit, wall, at with { Y = floor + o.SillMm + o.HeightMm / 2 });
        Assert.Equal(new PlanPick(0, PickKind.Opening, window), pick);
        // Low down under it, it's the wall.
        Assert.Equal(PickKind.Wall, HousePicking.Pick(D, Kit, wall, at with { Y = floor + 2 })?.Kind);
    }

    static double Distance((double X, double Y) a, (double X, double Y) b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    [Fact]
    public void Columns_roofs_doors_stairs_and_furniture_pick_themselves()
    {
        foreach (var c in Kit.Solids.Where(s => s.Kind == SolidKind.Column))
        {
            var p = PickAtMiddle(c);
            Assert.Equal(PickKind.Column, p?.Kind);
            Assert.True(HousePicking.Shows(D, Kit, c, p!));
        }

        var roof = Kit.Solids.First(s => s.Kind == SolidKind.Roof);
        var rp = PickAtMiddle(roof);
        Assert.Equal(new PlanPick(1, PickKind.Roof, 0), rp);
        Assert.All(Kit.Solids.Where(s => s.Kind == SolidKind.Roof), s => Assert.True(HousePicking.Shows(D, Kit, s, rp!)));

        // The ground floor's door that opens hangs as a leaf: it picks the door.
        var leaf = Kit.Solids.First(s => s.Kind == SolidKind.Furniture && s.Level == 0 && s.Name.EndsWith(" door", StringComparison.Ordinal));
        var dp = PickAtMiddle(leaf);
        Assert.Equal(PickKind.Opening, dp?.Kind);
        Assert.Equal(OpeningKind.Door, D.Levels[0].Openings[dp!.Index].Kind);
        Assert.True(HousePicking.Shows(D, Kit, leaf, dp));

        var stairs = Kit.Solids.Where(s => s.Kind == SolidKind.Furniture && s.Name == HouseKitMaker.WayName(WayKind.Stairs)).ToList();
        Assert.NotEmpty(stairs);
        Assert.All(stairs, s =>
        {
            var p = PickAtMiddle(s);
            Assert.Equal(new PlanPick(0, PickKind.Way, 0), p);
            Assert.True(HousePicking.Shows(D, Kit, s, p!));
        });

        // The bed upstairs: its pieces pick it, and only its pieces show picked.
        var bed = new PlanPick(1, PickKind.Item, 0);
        var bedParts = Kit.Solids.Where(s => HousePicking.Shows(D, Kit, s, bed)).ToList();
        Assert.NotEmpty(bedParts);
        Assert.All(bedParts, s => Assert.Equal(bed, PickAtMiddle(s)));
    }

    [Fact]
    public void A_click_on_a_floor_picks_the_room_and_its_floor_shows_picked()
    {
        var tile = Kit.Solids.First(s => s.Kind is SolidKind.Ground or SolidKind.Floor && s.Level == 0
            && D.Levels[0].Shape().RoomAt(HousePicking.ToPlan(Kit, D, HousePicking.Centre(s)).X, HousePicking.ToPlan(Kit, D, HousePicking.Centre(s)).Y) >= 0);
        var p = PickAtMiddle(tile);
        Assert.Equal(PickKind.Room, p?.Kind);
        Assert.Equal(0, p!.Level);
        Assert.True(HousePicking.Shows(D, Kit, tile, p));
    }
}
