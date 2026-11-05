using PhotonCanvas.Addon.HouseEditor;
using Severity = PhotonCanvas.Addon.Buildings.Severity;

namespace PhotonCanvas.Tests;

/// <summary>Saved buildings (prefabs): saved and read back, their footprint, turned, dropped into another building, and the best fit for a space.</summary>
public class HousePrefabTests
{
    static string TempFolder()
    {
        var f = Path.Combine(Path.GetTempPath(), "pc-prefabs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(f);
        return f;
    }

    [Fact]
    public void A_saved_building_comes_back_as_it_was_saved_at_the_top_left()
    {
        var folder = TempFolder();
        try
        {
            var design = HouseDesign.Starter();
            var saved = HousePrefabs.Save(folder, new HousePrefab { Name = " Corner shop ", Type = "Shop", District = "Market", Tags = ["corner", " Corner", "two rooms"], Notes = "By the well", Design = design }, [1, 2, 3]);
            Assert.True(File.Exists(Path.Combine(folder, saved.Id.ToString("N") + ".json")));
            Assert.NotNull(HousePrefabs.PicturePath(folder, saved.Id));

            var all = HousePrefabs.Load(folder);
            var p = Assert.Single(all);
            Assert.Equal(saved.Id, p.Id);
            Assert.Equal("Corner shop", p.Name);
            Assert.Equal("Shop", p.Type);
            Assert.Equal("Market", p.District);
            Assert.Equal(["corner", "two rooms"], p.Tags);
            Assert.Equal("By the well", p.Notes);
            Assert.NotEqual(default, p.Created);
            // Moved to the top-left of the grid, otherwise the same building.
            Assert.Equal((0, 0, 5, 4), p.Design.GridBounds());
            Assert.Equal(design.Moved(-12, -12).ToJson(), p.Design.ToJson());

            var f = p.Footprint!;
            Assert.Equal(5, f.Width);
            Assert.Equal(4, f.Depth);
            Assert.Equal(1, f.Floors);
            Assert.Equal(20, f.Cells.Count);
            // The front door faces out to the front; the doorway between the rooms isn't a way in.
            var door = Assert.Single(f.Doors);
            Assert.Equal(PlanSide.Front, door.Facing);
            Assert.Equal(1.5, door.X, 6);
            Assert.Equal(4, door.Y, 6);

            // Saved again: the same file, the first saved time kept.
            var again = HousePrefabs.Save(folder, p with { Name = "Corner shop 2" });
            Assert.Single(HousePrefabs.Load(folder));
            Assert.Equal(p.Created, again.Created);

            HousePrefabs.Delete(folder, p.Id);
            Assert.Empty(HousePrefabs.Load(folder));
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void A_bare_design_reads_as_a_new_saved_building_and_rubbish_is_passed_over()
    {
        var folder = TempFolder();
        try
        {
            File.WriteAllText(Path.Combine(folder, "last.json"), HouseDesign.Sample().ToJson());
            File.WriteAllText(Path.Combine(folder, "nonsense.json"), "{ \"Hello\": 1 }");
            File.WriteAllText(Path.Combine(folder, "broken.json"), "{ not json");
            var p = Assert.Single(HousePrefabs.Load(folder));
            Assert.Equal(2, p.Design.Levels.Count);
            Assert.Equal(2, p.Footprint!.Floors);
            Assert.True(p.Footprint.Roofed);
            Assert.True(p.Footprint.HeightMm > 2 * p.Design.Style.StoreyHeightMm);
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void Turning_a_building_four_times_brings_it_back_and_it_still_goes_together()
    {
        var d = HouseDesign.Sample().AtOrigin();
        Assert.Equal(d.ToJson(), d.Turned(4).ToJson());
        Assert.Equal(d.Turned(1).Turned(1).ToJson(), d.Turned(2).ToJson());

        var (_, _, w, h) = d.GridBounds();
        var t = d.Turned(1);
        Assert.Equal((0, 0, h, w), t.GridBounds());
        var p = new HousePrefab { Design = d, Footprint = HousePrefabs.FootprintOf(d) };
        var tp = p.Turned(1);
        Assert.Equal(p.Footprint!.Cells.Count, tp.Footprint!.Cells.Count);
        // The front door now faces the right.
        Assert.Equal(p.Footprint.Doors.Select(x => HousePrefabs.Turn(x.Facing, 1)).Order(), tp.Footprint.Doors.Select(x => x.Facing).Order());

        var kit = HouseKitMaker.Make(d);
        var turned = HouseKitMaker.Make(t);
        Assert.True(turned.Ok);
        Assert.DoesNotContain(turned.Findings, f => f.Level == Severity.Bad);
        Assert.Equal(kit.Walls.Count, turned.Walls.Count);
        Assert.Equal(kit.Solids.Count(s => s.Kind == SolidKind.Furniture), turned.Solids.Count(s => s.Kind == SolidKind.Furniture));
    }

    [Fact]
    public void A_saved_building_dropped_in_shares_walls_brings_its_floors_and_keeps_its_look()
    {
        var shop = HouseDesign.Starter().AtOrigin();
        var host = HouseDesign.Starter();
        int walls = host.Levels[0].Walls.Count;

        // Side by side, sharing the end wall: that wall is kept once.
        var (_, _, w, _) = shop.GridBounds();
        var row = host.With(shop, 12 + w, 12);
        Assert.Equal(2 * walls - 4, row.Levels[0].Walls.Count);
        Assert.Equal(2 * host.Levels[0].Openings.Count, row.Levels[0].Openings.Count);
        Assert.Empty(row.Levels[0].WallLooks);
        Assert.True(HouseKitMaker.Make(row).Ok);

        // A two-storey building with another look onto a one-storey one: its floor above comes too, its walls keep their texture.
        var tall = HouseDesign.Sample().AtOrigin() with { Style = HouseDesign.Sample().Style with { Texture = "Brick" } };
        var both = host.With(tall, 20, 20);
        Assert.Equal(2, both.Levels.Count);
        Assert.Equal(tall.Roofs.Count, both.Roofs.Count);
        Assert.All(both.Levels[0].WallLooks, l => Assert.Equal("Brick", l.Texture));
        Assert.Equal(tall.Levels[0].Walls.Count, both.Levels[0].WallLooks.Count);
        Assert.Equal(tall.Levels[0].Furniture.Count, both.Levels[0].Furniture.Count);

        // Onto the floor above: its ground floor goes up there.
        var up = both.With(shop, 2, 30, level: 1);
        Assert.Equal(2, up.Levels.Count);
        Assert.Equal(both.Levels[1].Walls.Count + walls, up.Levels[1].Walls.Count);
    }

    [Fact]
    public void The_best_fit_turns_a_building_to_suit_the_space_and_the_type_asked_for()
    {
        var house = HouseDesign.Starter().AtOrigin();
        var a = new HousePrefab { Name = "House", Type = "House", Design = house, Footprint = HousePrefabs.FootprintOf(house) };
        var tower = HouseDesign.Sample().AtOrigin();
        var b = new HousePrefab { Name = "Tall", Type = "Tavern", Design = tower, Footprint = HousePrefabs.FootprintOf(tower) };

        // 5 × 4 into a 4 × 5 space: turned a quarter.
        var fit = HousePrefabs.BestFit([a], 4, 5)!.Value;
        Assert.Equal(1, fit.Turns % 2);
        Assert.Equal(0, fit.Score, 1);
        // Its door towards the street at the left: turned three quarters (front → left).
        Assert.Equal(3, HousePrefabs.BestFit([a], 4, 5, doorsFacing: PlanSide.Left)!.Value.Turns);
        Assert.Equal(0, HousePrefabs.BestFit([a], 5, 4, doorsFacing: PlanSide.Front)!.Value.Turns);
        // A tavern asked for: the tavern, even though the house fits better.
        Assert.Equal("Tall", HousePrefabs.BestFit([a, b], 5, 4, type: "Tavern")!.Value.Prefab.Name);
        Assert.Null(HousePrefabs.BestFit([], 5, 4));
    }

    [Fact]
    public void Saved_buildings_are_found_by_type_tag_and_words()
    {
        var d = HouseDesign.Starter();
        HousePrefab P(string name, string type, params string[] tags) => new() { Name = name, Type = type, Tags = [.. tags], Design = d };
        var all = new[] { P("Prancing pony", "Tavern", "inn", "corner"), P("Smithy", "Smithy", "forge"), P("Corner shop", "Shop", "Corner") };
        Assert.Equal(["Prancing pony", "Corner shop"], HousePrefabs.Matching(all, tags: ["corner"]).Select(p => p.Name));
        Assert.Equal(["Smithy"], HousePrefabs.Matching(all, type: "smithy").Select(p => p.Name));
        Assert.Equal(["Prancing pony"], HousePrefabs.Matching(all, text: "pony").Select(p => p.Name));
        Assert.Equal(["Smithy"], HousePrefabs.Matching(all, text: "FORGE").Select(p => p.Name));
        Assert.Equal(3, HousePrefabs.Matching(all).Count());
    }
}
