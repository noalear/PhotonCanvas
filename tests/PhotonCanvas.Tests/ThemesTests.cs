using Bridges = PhotonCanvas.Addon.Bridges;
using Buildings = PhotonCanvas.Addon.Buildings;
using FloorTiles = PhotonCanvas.Addon.FloorTiles;
using Furniture = PhotonCanvas.Addon.Furniture;
using GameKit = PhotonCanvas.Addon.GameKit;
using Roofs = PhotonCanvas.Addon.Roofs;
using Ruins = PhotonCanvas.Addon.Ruins;
using Scatter = PhotonCanvas.Addon.Scatter;
using Stairs = PhotonCanvas.Addon.Stairs;
using Walls = PhotonCanvas.Addon.Walls;

namespace PhotonCanvas.Tests;

/// <summary>
/// The terrain themes (Medieval, Dungeon, Cave, Sci-fi, Desert, Gothic) in every terrain maker: the same
/// list everywhere, and each maker's pieces in each theme come out whole.
/// </summary>
public class ThemesTests
{
    public static TheoryData<string> Themes()
    {
        var d = new TheoryData<string>();
        foreach (var t in Walls.WallThemes.Names) d.Add(t);
        return d;
    }

    [Fact]
    public void Every_terrain_maker_offers_the_same_themes()
    {
        string[] expected = ["Medieval", "Dungeon", "Cave", "Sci-fi", "Desert", "Gothic"];
        Assert.Equal(expected, Walls.WallThemes.Names);
        Assert.Equal(expected, FloorTiles.FloorThemes.Names);
        Assert.Equal(expected, Roofs.RoofThemes.Names);
        Assert.Equal(expected, Stairs.StairThemes.Names);
        Assert.Equal(expected, Ruins.RuinThemes.Names);
        Assert.Equal(expected, Bridges.BridgeThemes.Names);
        Assert.Equal(expected, GameKit.GameKitThemes.Names);
        Assert.Equal(expected, Furniture.FurnitureThemes.Names);
        Assert.Equal(expected, Scatter.ScatterThemes.Names);
        Assert.Equal(expected, Buildings.BuildingThemes.Names);
        Assert.All(expected, t => Assert.False(string.IsNullOrWhiteSpace(Walls.WallThemes.About(t))));
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void Themed_walls_come_out_whole_with_their_texture_and_door(string theme)
    {
        var spec = Walls.WallThemes.Apply(new Walls.WallSpec(), theme) with { Door = Walls.DoorKind.DoorwayAndDoor, Windows = 2 };
        Assert.Equal(theme, spec.Theme);
        var kit = Walls.WallKitMaker.Make(spec);
        Assert.True(kit.Ok, string.Join("\n", kit.Findings));
        Assert.NotNull(kit.Front);
        Assert.True(kit.Front!.Etched.Count + kit.Front.Lines.Count > 0, "no texture on the wall");
        var door = Assert.Single(kit.Parts, p => p.Name is "Door" or "Portcullis");
        Assert.True(door.Etched.Count + door.Lines.Count > 0, "nothing on the door");
    }

    [Theory]
    [InlineData(Walls.DoorLook.Planks)]
    [InlineData(Walls.DoorLook.Studded)]
    [InlineData(Walls.DoorLook.Metal)]
    [InlineData(Walls.DoorLook.Bars)]
    [InlineData(Walls.DoorLook.Stone)]
    public void Every_door_look_makes_a_whole_door_in_every_doorway(Walls.DoorLook look)
    {
        foreach (var shape in Enum.GetValues<Walls.OpeningShape>().Where(s => s is not (Walls.OpeningShape.Round or Walls.OpeningShape.ArrowSlit)))
        {
            var kit = Walls.WallKitMaker.Make(new Walls.WallSpec { Door = Walls.DoorKind.DoorwayAndDoor, DoorShape = shape, DoorLook = look });
            Assert.True(kit.Ok, $"{shape}: " + string.Join("\n", kit.Findings));
            var door = Assert.Single(kit.Parts, p => p.Name is "Door" or "Portcullis");
            Assert.Equal(1, Walls.Geo.Pieces(door.Cut));
        }
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void Themed_floor_tiles_roofs_towers_and_floors_come_out_whole(string theme)
    {
        var tiles = FloorTiles.TileKitMaker.Make(FloorTiles.FloorThemes.Apply(new FloorTiles.TileSpec(), theme));
        Assert.True(tiles.Ok, string.Join("\n", tiles.Findings));
        var roof = Roofs.RoofMaker.Make(Roofs.RoofThemes.Apply(new Roofs.RoofSpec(), theme));
        Assert.True(roof.Ok, string.Join("\n", roof.Findings));
        var tower = Roofs.TowerMaker.Make(Roofs.RoofThemes.Apply(new Roofs.TowerSpec(), theme));
        Assert.True(tower.Ok, string.Join("\n", tower.Findings));
        var floor = Stairs.FloorKitMaker.Make(Stairs.StairThemes.Apply(new Stairs.FloorSpec(), theme));
        Assert.True(floor.Ok, string.Join("\n", floor.Findings));
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void Themed_ruins_bridges_and_game_kit_come_out_whole(string theme)
    {
        var ruin = Ruins.RuinKitMaker.Make(Ruins.RuinThemes.Apply(new Ruins.RuinSpec(), theme));
        Assert.True(ruin.Ok, string.Join("\n", ruin.Findings));
        foreach (var kind in Enum.GetValues<Bridges.BridgeKind>())
        {
            var bridge = Bridges.BridgeKits.Make(Bridges.BridgeThemes.Apply(Bridges.Defaults.For(kind), theme));
            Assert.True(bridge.Ok, $"{kind}: " + string.Join("\n", bridge.Findings));
        }
        var set = GameKit.GameKitThemes.Apply(new GameKit.AccessorySet { Items = [.. GameKit.Defaults.Kinds.Select(k => GameKit.Defaults.For(k.Kind))] }, theme);
        var kit = GameKit.GameKitMaker.Make(set);
        Assert.True(kit.Ok, string.Join("\n", kit.Items.SelectMany(i => i.Findings)));
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void Each_themes_starter_furniture_and_scenery_come_out_whole(string theme)
    {
        var furniture = Furniture.FurnitureThemes.Apply(new Furniture.FurnitureSet { Items = Furniture.FurnitureThemes.Starter(theme) }, theme);
        Assert.Equal(Furniture.FurnitureThemes.Detail(theme), furniture.Detail);
        var fk = Furniture.FurnitureKit.Make(furniture);
        Assert.True(fk.Ok, string.Join("\n", fk.Items.SelectMany(i => i.Findings)));

        var scatter = Scatter.ScatterThemes.Apply(new Scatter.ScatterSet { Items = Scatter.ScatterThemes.Starter(theme) }, theme);
        var sk = Scatter.ScatterKit.Make(scatter);
        Assert.True(sk.Ok, string.Join("\n", sk.Items.SelectMany(i => i.Findings)));
        // Every kind of scenery turned to the theme still comes out.
        var all = Scatter.ScatterThemes.Apply(new Scatter.ScatterSet { Items = [.. Scatter.Defaults.Kinds.Select(k => Scatter.Defaults.For(k.Kind))] }, theme);
        Assert.True(Scatter.ScatterKit.Make(all).Ok);
    }

    [Fact]
    public void The_themes_bring_their_own_scenery()
    {
        Assert.Contains(Scatter.ScatterThemes.Starter("Cave"), i => i.Kind == Scatter.ItemKind.Stalagmite);
        Assert.Contains(Scatter.ScatterThemes.Starter("Sci-fi"), i => i.Kind == Scatter.ItemKind.Console);
        Assert.Contains(Scatter.ScatterThemes.Starter("Desert"), i => i.Kind == Scatter.ItemKind.Palm);
        Assert.Contains(Scatter.ScatterThemes.Starter("Dungeon"), i => i.Kind == Scatter.ItemKind.Brazier);
        Assert.Contains(Scatter.ScatterThemes.Starter("Gothic"), i => i.Kind == Scatter.ItemKind.Fence && i.Style == Scatter.FenceStyle.Railings);
        // Sci-fi turns the barrels already on the list into drums.
        var s = Scatter.ScatterThemes.Apply(new Scatter.ScatterSet(), "Sci-fi");
        Assert.True(s.Items[0].Metal);
        Assert.Equal(Scatter.WoodDetail.None, s.Detail);
    }

    public static TheoryData<Buildings.BuildingKind, string> BuildingsInThemes()
    {
        var d = new TheoryData<Buildings.BuildingKind, string>();
        foreach (var k in Enum.GetValues<Buildings.BuildingKind>())
            foreach (var t in Walls.WallThemes.Names) d.Add(k, t);
        return d;
    }

    [Theory]
    [MemberData(nameof(BuildingsInThemes))]
    public void Every_building_in_every_theme_comes_out_whole(Buildings.BuildingKind kind, string theme)
    {
        var spec = Buildings.BuildingThemes.Apply(Buildings.BuildingSpec.Preset(kind), theme);
        Assert.Equal(theme, spec.Theme);
        var kit = Buildings.BuildingKitMaker.Make(spec);
        Assert.True(kit.Ok, string.Join("\n", kit.Findings.Select(f => f.Text)));
        foreach (var p in kit.Pieces) Assert.True(Walls.Geo.Pieces(p.Cut) == 1, $"{p.Name} comes out in {Walls.Geo.Pieces(p.Cut)} pieces");
    }
}
