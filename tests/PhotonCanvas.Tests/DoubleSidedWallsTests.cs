using Clipper2Lib;
using PhotonCanvas.Addon.Walls;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;

namespace PhotonCanvas.Tests;

/// <summary>Walls with another look on their back: a single panel's back engraved after turning the pieces over.</summary>
public class DoubleSidedWallsTests
{
    static readonly MachineProfile Machine = MachineProfile.F2Ultra;

    /// <summary>A single panel, its door off to the left so a back drawn the wrong way round would show.</summary>
    static WallSpec Single(Func<WallSpec, WallSpec>? change = null)
    {
        var s = new WallSpec
        {
            LengthMm = 120, HeightMm = 50, Texture = "Brick", Door = DoorKind.Doorway, DoorOffsetMm = -30, Windows = 1, WindowAtMm = [35],
            EngraveBack = true, Back = FaceLook.Of("Wood planks (upright)"), Connectors = [],
        };
        return change is null ? s : change(s);
    }

    static Part Wall(WallKit k) => k.Parts.Single(p => p.Name == "Wall");

    [Fact]
    public void A_single_panel_gets_its_back_engraving_with_its_own_look()
    {
        var k = WallKitMaker.Make(Single());
        Assert.True(k.Ok, string.Join("\n", k.Findings));
        var wall = Wall(k);
        Assert.True(wall.HasBack);
        // Another texture: not the front's etching.
        Assert.NotEqual(Geo.Area(wall.Etched), Geo.Area(wall.BackEtched!), 1);
        // Kept on the panel (seen through it from the front, so in the front's x), clear of the door and window.
        Assert.Equal(0, Geo.Area(Geo.Subtract(wall.BackEtched!, wall.Cut)), 2);
        Assert.Contains(k.Findings, f => f.Text.Contains("turn them over"));
    }

    [Fact]
    public void The_front_is_the_same_with_or_without_a_back()
    {
        var with = Wall(WallKitMaker.Make(Single()));
        var without = Wall(WallKitMaker.Make(Single(s => s with { EngraveBack = false, Back = null })));
        Assert.False(without.HasBack);
        Assert.Equal(Geo.Area(without.Etched), Geo.Area(with.Etched), 6);
        Assert.Equal(without.Lines.Count, with.Lines.Count);
    }

    [Fact]
    public void Only_a_single_panel_is_turned_over()
    {
        // Two panels back to back: the back panel is a piece of its own, so it simply gets the back's look.
        var two = WallKitMaker.Make(Single(s => s with { TwoSided = true }));
        Assert.False(Wall(two).HasBack);
        var back = two.Parts.Single(p => p.Name == "Wall back");
        Assert.NotEqual(Geo.Area(Wall(two).Etched), Geo.Area(back.Etched), 1);
        // A box wall's back face too.
        var box = WallKitMaker.Make(WallSpec.NewBoxed with { Texture = "Brick", Back = FaceLook.Of(Textures.Plain), EngraveBack = true, Connectors = [] });
        Assert.DoesNotContain(box.Parts, p => p.HasBack);
        Assert.Empty(box.Parts.Single(p => p.Name == "Back").Etched);
        Assert.NotEmpty(box.Parts.Single(p => p.Name == "Front").Etched);
    }

    [Fact]
    public void The_backs_go_on_a_page_of_their_own_mirrored_across_the_bed()
    {
        var spec = Single(s => s with { Walls = 2 });
        var kit = WallKitMaker.Make(spec);
        var (s, sheets, _) = Placement.Place(DocState.New(), kit, spec, Machine, new PlaceOptions());
        Assert.Equal(1, sheets);
        var names = Pages.Names(s);
        Assert.Equal(2, names.Count);
        Assert.EndsWith(Placement.BackPage, names[1]);
        Assert.Equal(0, s.PageIndex);

        var front = s;
        var back = Pages.SwitchTo(s, 1);
        // An engrave only: no cut, and the outlines are a guide that isn't burned.
        Assert.DoesNotContain(back.Layers, l => l.Mode == LayerMode.Cut && l.Output);
        var guide = Assert.Single(back.Layers, l => l.Name == Placement.BackGuideLayer);
        Assert.False(guide.Output);
        var etch = Assert.Single(back.Layers, l => l.Name == Placement.BackEtchLayer);
        Assert.Equal(LayerMode.Fill, etch.Mode);
        Assert.True(etch.Output);

        var cut = front.Layers.Single(l => l.Name == Placement.CutLayer).Id;
        var panels = front.Shapes.Where(x => x.LayerId == cut && x.Name.StartsWith("Wall")).ToList();
        var outlines = back.Shapes.Where(x => x.LayerId == guide.Id).ToList();
        Assert.Equal(2, panels.Count);
        Assert.Equal(2, outlines.Count);
        foreach (var o in outlines)
        {
            // Where a panel is once the sheet is turned over left to right.
            Assert.Contains(panels, p => Math.Abs(p.Bounds.MinX - (Machine.FieldW - o.Bounds.MaxX)) < 0.05 && Math.Abs(p.Bounds.MinY - o.Bounds.MinY) < 0.05
                && Math.Abs(p.Bounds.Width - o.Bounds.Width) < 0.05);
            // Grouped with the engraving, which lies on the turned-over piece (clear of its door and window holes).
            var texture = back.Shapes.Single(x => x.GroupId == o.GroupId && x.LayerId == etch.Id);
            var piece = Geo.FromGeometry(((PathShape)o).World).Areas;
            var engraved = Geo.FromGeometry(((PathShape)texture).World).Areas;
            Assert.True(Geo.Area(engraved) > 100);
            Assert.True(Geo.Area(Geo.Subtract(engraved, piece)) < 0.5, $"{Geo.Area(Geo.Subtract(engraved, piece)):0.##} mm² engraved off the piece");
            Assert.All(back.Shapes.Where(x => x.GroupId == o.GroupId), x => Assert.True(x.Transform.IsIdentity));
        }
    }

    [Fact]
    public void Replacing_the_walls_takes_their_backs_away_too()
    {
        var spec = Single();
        var (s, _, id) = Placement.Place(DocState.New(), WallKitMaker.Make(spec), spec, Machine, new PlaceOptions());
        Assert.Equal(2, Pages.Names(s).Count);
        var plain = spec with { EngraveBack = false };
        var (s2, _, _) = Placement.Place(s, WallKitMaker.Make(plain), plain, Machine, new PlaceOptions { Replace = id });
        Assert.Single(Pages.Names(s2));
        Assert.Equal(plain.ToJson(), Placement.Find(s2, [s2.Shapes[0].Id])!.Value.Spec.ToJson());
    }

    [Fact]
    public void A_relief_back_is_a_picture_never_drawn_mirrored()
    {
        var spec = Single(s => s with { Relief = true });
        var (s, _, _) = Placement.Place(DocState.New(), WallKitMaker.Make(spec), spec, Machine, new PlaceOptions());
        var back = Pages.SwitchTo(s, 1);
        var relief = Assert.Single(back.Layers, l => l.Name == Placement.BackReliefLayer);
        var pic = Assert.Single(back.Shapes.OfType<ImageShape>());
        Assert.Equal(relief.Id, pic.LayerId);
        var t = pic.Transform;
        Assert.True(t.A * t.D - t.B * t.C > 0, "the picture is mirrored");
        var o = back.Shapes.Single(x => x.GroupId == pic.GroupId && x is PathShape);
        Assert.InRange(Math.Abs(pic.Bounds.MinX - o.Bounds.MinX), 0, 0.3);
        Assert.InRange(Math.Abs(pic.Bounds.Width - o.Bounds.Width), 0, 0.3);
    }

    [Fact]
    public void The_back_look_is_saved_with_the_walls()
    {
        var spec = Single();
        var again = WallSpec.FromJson(spec.ToJson())!;
        Assert.True(again.EngraveBack);
        Assert.Equal("Wood planks (upright)", again.Back!.Texture);
        Assert.Null(WallSpec.FromJson(new WallSpec().ToJson())!.Back);
    }
}
