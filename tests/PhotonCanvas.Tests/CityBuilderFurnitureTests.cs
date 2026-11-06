using PhotonCanvas.Addon.CityBuilder;
using PhotonCanvas.Addon.Furniture;
using PhotonCanvas.App.View3D;

namespace PhotonCanvas.Tests;

/// <summary>
/// The furniture maker's pieces put down in the town on the fine grid, and putting things down and moving them
/// part of a square or at an angle: half-step buildings, tilting each thing on its own spot.
/// </summary>
public class CityBuilderFurnitureTests
{
    static HashSet<(int X, int Y)> Rect(int x, int y, int w, int d)
    {
        var s = new HashSet<(int, int)>();
        for (int j = y; j < y + d; j++) for (int i = x; i < x + w; i++) s.Add((i, j));
        return s;
    }

    static CityBuilding Made(int x, int y, int w, int d)
    {
        var (design, mx, my) = BuildingGuess.Make(new SeenBuilding { Cells = Rect(x, y, w, d) }, "Medieval", 25.4)!.Value;
        return new CityBuilding { X = mx, Y = my, Design = design };
    }

    static CatalogEntry Table() => CityItems.Catalog("Medieval", FurnitureCatalog.Hall).First(e => e.Item.Kind == ItemKind.Table);

    [Fact]
    public void The_town_s_pieces_are_those_that_stand_on_the_ground_and_come_first()
    {
        Assert.Equal(FurnitureCatalog.Town, CityItems.Rooms[0]);
        Assert.Equal(FurnitureCatalog.Rooms.Count, CityItems.Rooms.Count);
        foreach (var theme in FurnitureCatalog.Themes)
        {
            var town = CityItems.Catalog(theme, FurnitureCatalog.Town);
            Assert.NotEmpty(town);
            // No walls, guard towers, trees or the Things tool's pieces; nothing hung on a wall.
            Assert.DoesNotContain(town, e => e.Item.Kind == ItemKind.Tree);
            Assert.DoesNotContain(town, e => e.Id.EndsWith("-wall-1", StringComparison.Ordinal) || e.Id.EndsWith("-guard-tower", StringComparison.Ordinal) || e.Id.EndsWith("-market-stall", StringComparison.Ordinal));
            Assert.DoesNotContain(town, e => FurnitureCatalog.SpotFor(e.Item) == FurnitureSpot.OnWall);
        }
        // The town's has the stocks, tables and benches from the rooms, and the town's own signposts.
        var medieval = CityItems.Catalog("Medieval", FurnitureCatalog.Town);
        Assert.Contains(medieval, e => e.Item.Kind == ItemKind.Stocks);
        Assert.Contains(medieval, e => e.Item.Kind == ItemKind.Bench);
        Assert.Contains(medieval, e => e.Id.EndsWith("-signpost", StringComparison.Ordinal));
        Assert.Equal(medieval.Count, medieval.Select(e => e.Id).Distinct().Count());
        // Every room has something, the medieval pieces where a look has none of its own.
        foreach (var room in CityItems.Rooms) Assert.NotEmpty(CityItems.Catalog("Sci-fi", room));
    }

    [Fact]
    public void A_piece_is_true_to_size_and_lies_where_it_is_turned()
    {
        var e = Table();
        var t = CityItems.Make(e, 25.4, 5, 5);
        var look = CityItems.Look(t, 25.4);
        Assert.Equal(look.W, t.WidthMm, 1);
        Assert.Equal(look.D, t.DepthMm, 1);
        Assert.True(t.WidthMm > 10 && t.DepthMm > 5, $"{t.WidthMm} × {t.DepthMm}");
        Assert.True(CityItems.Height(t, 25.4) > 5);
        Assert.Equal(e.Id, t.CatalogId);
        Assert.Equal(1, t.Item.Count);
        // Its corners: front left, front right, back right, back left, with its front towards the bottom of the plan.
        var (w, d) = t.Size(25.4);
        var c = t.Corners(25.4);
        Assert.Equal(5 - w / 2, c[0].X, 9);
        Assert.Equal(5 + d / 2, c[0].Y, 9);
        Assert.Equal(5 + w / 2, c[1].X, 9);
        Assert.True(t.Hit(5, 5, 25.4));
        Assert.True(t.Hit(5 + w / 2 - 0.01, 5, 25.4));
        Assert.False(t.Hit(5 + w / 2 + 0.1, 5, 25.4));
        // A quarter turn clockwise: its front faces left, its width runs down the plan.
        var turned = t with { TurnDeg = 90 };
        Assert.True(turned.Hit(5, 5 + w / 2 - 0.01, 25.4));
        Assert.False(turned.Hit(5 + w / 2 - 0.01, 5, 25.4) && w / 2 > d / 2 + 0.05);
        var front = turned.Corners(25.4);
        Assert.Equal(5 - d / 2, (front[0].X + front[1].X) / 2, 9);
    }

    [Fact]
    public void On_bigger_squares_a_piece_is_bigger_in_proportion()
    {
        var e = Table();
        var small = CityItems.Make(e, 25.4);
        var big = CityItems.Make(e, 50.8);
        Assert.InRange(big.WidthMm / small.WidthMm, 1.85, 2.15);
        // The same size in squares either way.
        Assert.Equal(small.Size(25.4).W, big.Size(50.8).W, 1);
    }

    [Fact]
    public void Furniture_is_picked_moved_copied_saved_and_taken_away_with_the_rest()
    {
        var item = CityItems.Make(Table(), 25.4, 5.3, 5.7, 30);
        var m = CityMap.Empty(20, 16) with { Items = [item] };
        var pick = CityEdit.Within(m, 4, 4, 7, 7);
        Assert.Equal([item.Id], pick.Items);
        Assert.Equal(1, pick.Count);
        var (moved, after) = CityEdit.Move(m, pick, 0.1, -0.2);
        Assert.Equal(5.4, moved.Items[0].X, 9);
        Assert.Equal(5.5, moved.Items[0].Y, 9);
        Assert.Equal(after.Items, pick.Items);
        // Copied and pasted: a new piece, the same as the old.
        var clip = CityEdit.Copy(m, pick);
        var (pasted, fresh) = CityEdit.Paste(m, clip, 10, 2);
        Assert.Equal(2, pasted.Items.Count);
        var copy = pasted.Items.Single(i => fresh.Items.Contains(i.Id));
        Assert.NotEqual(item.Id, copy.Id);
        Assert.Equal(item.CatalogId, copy.CatalogId);
        Assert.Equal(30, copy.TurnDeg);
        // Saved and loaded.
        var back = CityMap.FromJson(pasted.ToJson())!;
        Assert.Equal(2, back.Items.Count);
        Assert.Equal(item.Item.Kind, back.Items[0].Item.Kind);
        Assert.Equal(item.WidthMm, back.Items[0].WidthMm, 6);
        Assert.Equal((item.X, item.Y, item.TurnDeg), (back.Items[0].X, back.Items[0].Y, back.Items[0].TurnDeg));
        // An older map with no furniture loads with none.
        var json = System.Text.Json.Nodes.JsonNode.Parse(CityMap.Empty(4, 4).ToJson())!.AsObject();
        Assert.True(json.Remove("Items"));
        var older = CityMap.FromJson(json.ToJsonString())!;
        Assert.Empty(older.Items);
        // Taken away; and a map made smaller keeps only what's still on it.
        Assert.Empty(CityEdit.Delete(m, pick).Items);
        Assert.Single(pasted.Resized(8, 8).Items);
    }

    [Fact]
    public void The_same_pieces_are_counted_together_to_cut()
    {
        var e = Table();
        var bench = CityItems.Catalog("Medieval", FurnitureCatalog.Hall).First(x => x.Item.Kind != ItemKind.Table);
        var m = CityMap.Empty(20, 16) with { Items = [CityItems.Make(e, 25.4, 2, 2), CityItems.Make(e, 25.4, 6, 2, 45), CityItems.Make(bench, 25.4, 12, 12)] };
        var set = CityItems.Set(m)!;
        Assert.Equal(2, set.Items.Count);
        Assert.Equal(2, set.Items.Single(i => i.Kind == ItemKind.Table).Count);
        Assert.Equal(3, set.Items.Sum(i => i.Count));
        // Only those in the box, and nothing when there's none.
        Assert.Equal(2, CityItems.Set(m, (0, 0, 10, 10))!.Items.Sum(i => i.Count));
        Assert.Null(CityItems.Set(m, (15, 0, 20, 5)));
        Assert.Null(CityItems.Set(CityMap.Empty(4, 4)));
        // It can be made.
        Assert.True(FurnitureKit.Make(set).Ok);
    }

    [Fact]
    public void Furniture_shows_in_the_picture_and_in_3D()
    {
        var empty = CityMap.Empty(4, 4);
        var with = empty with { Items = [CityItems.Make(Table(), 25.4, 2, 2)] };
        var o = new TownPictureOptions { PxPerSquare = 40, Grid = false, Labels = false };
        using var a = TownPicture.Render(empty, o);
        using var b = TownPicture.Render(with, o);
        var pa = a.GetPixel(80, 80);
        var pb = b.GetPixel(80, 80);
        Assert.True(Math.Abs(pa.Red - pb.Red) + Math.Abs(pa.Green - pb.Green) + Math.Abs(pa.Blue - pb.Blue) > 30, $"{pa} vs {pb}");
        var s0 = new Scene3D();
        CityScene.Build(s0, empty, new CityShow());
        var s1 = new Scene3D();
        CityScene.Build(s1, with, new CityShow());
        Assert.True(s1.Version > s0.Version);
        // Its pieces stand on the ground, no higher than it's made.
        var solid = CityItems.SolidOf(with.Items[0], 25.4);
        Assert.NotEmpty(solid.Parts);
        // Not shown when the trees and things are left out.
        var s2 = new Scene3D();
        CityScene.Build(s2, with, new CityShow { Props = false });
        var s3 = new Scene3D();
        CityScene.Build(s3, empty, new CityShow { Props = false });
        Assert.Equal(s3.Version, s2.Version);
    }

    [Fact]
    public void The_fine_grid_is_tenths_of_a_square()
    {
        var p = CityEdit.Snapped(new CityPt(1.23, 4.56), CitySnap.Fine);
        Assert.Equal(1.2, p.X, 9);
        Assert.Equal(4.6, p.Y, 9);
        Assert.Equal(1.0, CityEdit.Snapped(new CityPt(1.23, 4.56), CitySnap.HalfSquares).X, 9);
    }

    [Fact]
    public void A_building_moved_part_of_a_square_keeps_whole_squares_and_the_rest_in_its_shift()
    {
        var house = Made(4, 4, 3, 2);
        var s = CityEdit.Shifted(house, 1.5, -0.25);
        Assert.Equal((house.X + 1, house.Y - 1), (s.X, s.Y));
        Assert.Equal(0.5, s.ShiftX, 9);
        Assert.Equal(0.75, s.ShiftY, 9);
        Assert.False(CityFootprint.OnGrid(s));
        // Back by the same: on whole squares again, no shift left.
        var back = CityEdit.Shifted(s, -1.5, 0.25);
        Assert.Equal((house.X, house.Y, 0.0, 0.0), (back.X, back.Y, back.ShiftX, back.ShiftY));
        Assert.True(CityFootprint.OnGrid(back));
        // Moving what's picked half a square takes the building with it.
        var m = CityMap.Empty(20, 16) with { Buildings = [house] };
        var (moved, _) = CityEdit.Move(m, CityPick.Of(house), 0.5, 0);
        var h = moved.Building(house.Id)!;
        Assert.Equal(house.X + 0.5, h.X + h.ShiftX, 9);
        Assert.Equal(house.Y, h.Y + h.ShiftY, 9);
    }

    [Fact]
    public void Each_thing_turned_on_its_own_spot_a_building_tilting_about_its_middle()
    {
        var house = Made(4, 4, 4, 2);
        var tree = new Prop { Kind = PropKind.Well, X = 12, Y = 3, TurnDeg = 350 };
        var label = new CityLabel { Text = "Mill Lane", X = 10, Y = 10, TurnDeg = 80 };
        var item = CityItems.Make(Table(), 25.4, 15, 12, 0);
        var m = CityMap.Empty(24, 20) with { Buildings = [house], Props = [tree], Labels = [label], Items = [item] };
        var all = CityEdit.Within(m, 0, 0, 24, 20);
        var (cx, cy) = CityFootprint.Centre(house);
        var t = CityEdit.TurnEach(m, all, 15);
        var h = t.Building(house.Id)!;
        Assert.Equal(15, h.TiltDeg, 9);
        Assert.Equal(house.Turn, h.Turn);
        var (nx, ny) = CityFootprint.Centre(h);
        Assert.Equal(cx, nx, 6);
        Assert.Equal(cy, ny, 6);
        Assert.Equal(5, t.Props[0].TurnDeg, 9);
        Assert.Equal(15, t.Items[0].TurnDeg, 9);
        Assert.Equal((15.0, 12.0), (t.Items[0].X, t.Items[0].Y));
        // A label stays the right way up.
        Assert.InRange(t.Labels[0].TurnDeg, -90, 90);
        // Past 45° a building goes round a quarter turn and tilts back the other way, its middle still where it was.
        var far = CityEdit.TurnEach(m, all, 60).Building(house.Id)!;
        Assert.Equal((house.Turn + 1) % 4, far.Turn);
        Assert.Equal(-30, far.TiltDeg, 9);
        var (fx, fy) = CityFootprint.Centre(far);
        Assert.Equal(cx, fx, 6);
        Assert.Equal(cy, fy, 6);
        // Turned all the way back: square to the grid again.
        var square = CityEdit.TurnEach(CityEdit.TurnEach(m, all, 30), all, -30).Building(house.Id)!;
        Assert.True(CityFootprint.OnGrid(square));
        Assert.Equal((house.X, house.Y), (square.X, square.Y));
    }
}
