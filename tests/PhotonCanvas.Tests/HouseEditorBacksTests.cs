using PhotonCanvas.Addon.Buildings;
using PhotonCanvas.Addon.HouseEditor;
using PhotonCanvas.Addon.Walls;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using Geo = PhotonCanvas.Addon.Walls.Geo;
using WallLook = PhotonCanvas.Addon.HouseEditor.WallLook;
using PlaceOptions = PhotonCanvas.Addon.Buildings.PlaceOptions;

namespace PhotonCanvas.Tests;

/// <summary>The building editor's walls with another look on their other side: a single panel's back engraved after turning it over.</summary>
public class HouseEditorBacksTests
{
    static List<WallEdge> Line(int x0, int y0, int x1, int y1)
    {
        var list = new List<WallEdge>();
        int dx = Math.Sign(x1 - x0), dy = Math.Sign(y1 - y0);
        for (var p = new GridPt(x0, y0); p != new GridPt(x1, y1); p += new GridPt(dx, dy)) list.Add(WallEdge.Of(p, p + new GridPt(dx, dy)));
        return list;
    }

    static List<WallEdge> Box(int x0, int y0, int x1, int y1) => [.. Line(x0, y0, x1, y0), .. Line(x1, y0, x1, y1), .. Line(x1, y1, x0, y1), .. Line(x0, y1, x0, y0)];

    /// <summary>One room, its top wall stone outside and wood panelling inside.</summary>
    static HouseDesign Room(bool box)
    {
        var style = HouseDesign.DefaultStyle();
        if (!box) style = style with { BoxWalls = false, TwoSided = false, ColumnMm = 12 };
        var own = Line(0, 0, 4, 0).Select(e => new WallLook { Edge = e, Texture = "Stone blocks", BackTexture = "Wood planks (upright)", BackWear = 0.2 }).ToList();
        return new HouseDesign { Style = style, Levels = [new Level { Walls = Box(0, 0, 4, 3), WallLooks = own }] };
    }

    [Fact]
    public void A_single_panel_wall_gets_its_inside_on_its_back()
    {
        var kit = HouseKitMaker.Make(Room(box: false));
        Assert.True(kit.Ok, string.Join("\n", kit.Findings.Select(f => f.Text)));
        var wall = Assert.Single(kit.Walls, w => w.Spec.Texture == "Stone blocks");
        Assert.True(wall.Spec.FlipsForBack);
        Assert.Equal("Wood planks (upright)", wall.Spec.Back!.Texture);
        Assert.Equal(0.2, wall.Spec.Back.Wear, 6);
        var backed = Assert.Single(kit.Pieces, p => p.BackEtched is { Count: > 0 });
        Assert.Equal(0, Geo.Area(Geo.Subtract(backed.BackEtched!, backed.Cut)), 2);
        // The other walls have nothing on their backs.
        Assert.All(kit.Walls.Where(w => w != wall), w => Assert.False(w.Spec.EngraveBack));
    }

    [Fact]
    public void A_box_wall_gives_its_inside_face_the_look_with_nothing_to_turn_over()
    {
        var kit = HouseKitMaker.Make(Room(box: true));
        Assert.True(kit.Ok, string.Join("\n", kit.Findings.Select(f => f.Text)));
        var wall = Assert.Single(kit.Walls, w => w.Spec.Texture == "Stone blocks");
        Assert.Equal("Wood planks (upright)", wall.Spec.Back!.Texture);
        Assert.DoesNotContain(kit.Pieces, p => p.BackEtched is not null);
    }

    [Fact]
    public void A_different_other_side_splits_a_straight_wall()
    {
        var walls = Box(0, 0, 6, 3);
        var own = Line(0, 0, 3, 0).Select(e => new WallLook { Edge = e, Texture = "Fieldstone", BackTexture = "Brick" })
            .Concat(Line(3, 0, 6, 0).Select(e => new WallLook { Edge = e, Texture = "Fieldstone" })).ToList();
        var level = new Level { Walls = walls, WallLooks = own };
        Assert.Equal(5, level.Shape().Joints.Count);
    }

    [Fact]
    public void The_building_puts_the_backs_on_a_page_after_its_sheet()
    {
        var d = Room(box: false);
        var kit = HouseKitMaker.Make(d);
        var (s, pages, id) = HousePlacement.Place(DocState.New(), kit, d, MachineProfile.F2Ultra, new PlaceOptions());
        var names = Pages.Names(s);
        Assert.Equal(pages, names.Count);
        int at = names.FindIndex(n => n.EndsWith(PhotonCanvas.Addon.Walls.Placement.BackPage));
        Assert.True(at > 0, string.Join(" | ", names));
        var back = Pages.SwitchTo(s, at);
        var guide = Assert.Single(back.Layers, l => l.Name == HousePlacement.Backs.Guide);
        Assert.False(guide.Output);
        Assert.DoesNotContain(back.Layers, l => l.Mode == LayerMode.Cut && l.Output);
        var outline = Assert.Single(back.Shapes, x => x.LayerId == guide.Id);
        // Named like its front, number and all, so the right piece is turned over.
        var backed = kit.Pieces.Single(p => p.BackEtched is { Count: > 0 });
        Assert.StartsWith(backed.Number > 0 ? $"#{backed.Number} " : backed.Name, outline.Name);
        Assert.Contains(back.Shapes, x => x.GroupId == outline.GroupId && x.LayerId == back.Layers.Single(l => l.Name == HousePlacement.Backs.Etch).Id);
        // Replacing the building takes the page away with the rest.
        var plain = d with { Levels = [d.Levels[0] with { WallLooks = [] }] };
        var (s2, pages2, _) = HousePlacement.Place(s, HouseKitMaker.Make(plain), plain, MachineProfile.F2Ultra, new PlaceOptions { Replace = id });
        Assert.DoesNotContain(Pages.Names(s2), n => n.EndsWith(PhotonCanvas.Addon.Walls.Placement.BackPage));
        Assert.Equal(pages2, Pages.Names(s2).Count);
    }

    [Fact]
    public void The_other_side_is_saved_with_the_design()
    {
        var d = HouseDesign.FromJson(Room(box: false).ToJson())!;
        var look = d.Levels[0].LookOf(WallEdge.Of(new(0, 0), new(1, 0)))!;
        Assert.Equal("Wood planks (upright)", look.BackTexture);
        Assert.Equal(0.2, look.BackWear, 6);
    }
}
