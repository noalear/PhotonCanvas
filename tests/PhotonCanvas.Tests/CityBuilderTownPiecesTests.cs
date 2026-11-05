using PhotonCanvas.Addon.CityBuilder;
using PhotonCanvas.Addon.Furniture;

namespace PhotonCanvas.Tests;

/// <summary>The town's bigger pieces on the map (troughs, stages, stalls, bars, notice boards, guard towers) and the furniture maker's town walls, trees and pieces cut for them.</summary>
public class CityBuilderTownPiecesTests
{
    /// <summary>
    /// A 16 × 12 map with a town wall round squares 2 to 11 across and 2 to 9 down: a road two squares wide
    /// through the top of it (a gate), a stream through the right side (a gap a square wide), and a guard tower
    /// on the bottom side.
    /// </summary>
    static CityMap Walled()
    {
        var m = CityMap.Empty(16, 12, "Walled");
        var g = m.GroundGrid();
        for (int y = 0; y < 12; y++) { g[6, y] = Ground.Road; g[7, y] = Ground.Road; }
        for (int x = 11; x < 16; x++) g[x, 5] = Ground.Water;
        var edges = new List<FenceEdge>();
        for (int x = 2; x < 12; x++)
        {
            if (x is not (6 or 7)) edges.Add(new(x, 2, true));
            if (x is not (6 or 7)) edges.Add(new(x, 10, true));
        }
        for (int y = 2; y < 10; y++)
        {
            edges.Add(new(2, y, false));
            if (y != 5) edges.Add(new(12, y, false));
        }
        return m.WithGround(g) with
        {
            Fences = [new Fence { Kind = FenceKind.CityWall, Edges = edges }],
            Props = [new Prop { Kind = PropKind.GuardTower, X = 7, Y = 10 }],
        };
    }

    [Fact]
    public void A_town_wall_is_cut_as_sections_corners_a_gatehouse_ends_and_towers()
    {
        var cut = TownPieces.Walls(Walled());
        // Four outside corners; a gatehouse over the road; turrets either side of the stream; nothing under the
        // tower; the rest in sections of two squares and one.
        Assert.Equal(new WallCut(Two: 8, One: 5, CornersOut: 4, CornersIn: 0, Gates: 1, Ends: 2, Towers: 1), cut);
        // An L-shaped wall: five corners round the outside of it and one going in.
        var edges = new List<FenceEdge>();
        for (int x = 0; x < 4; x++) edges.Add(new(x, 0, true));
        for (int y = 0; y < 2; y++) edges.Add(new(4, y, false));
        for (int x = 2; x < 4; x++) edges.Add(new(x, 2, true));
        for (int y = 2; y < 4; y++) edges.Add(new(2, y, false));
        for (int x = 0; x < 2; x++) edges.Add(new(x, 4, true));
        for (int y = 0; y < 4; y++) edges.Add(new(0, y, false));
        var l = CityMap.Empty(6, 6) with { Fences = [new Fence { Kind = FenceKind.CityWall, Edges = edges }] };
        Assert.Equal(new WallCut(2, 0, 5, 1, 0, 0, 0), TownPieces.Walls(l));
        // Only what's in a box: the left side, its ends going on outside the box.
        var left = TownPieces.Walls(Walled(), (0, 3, 4, 9));
        Assert.Equal((0, 0, 0), (left.CornersOut, left.Gates, left.Ends));
        Assert.Equal(6, 2 * left.Two + left.One);
        // A plain fence isn't a town wall.
        Assert.Equal(0, TownPieces.Walls(CityWindow.Sample()).Count);
    }

    [Fact]
    public void The_walls_trees_and_town_pieces_come_from_the_furniture_maker_in_the_maps_look()
    {
        var m = Walled() with
        {
            Props =
            [
                new Prop { Kind = PropKind.GuardTower, X = 7, Y = 10 },
                new Prop { Kind = PropKind.Tree, X = 4, Y = 4, Size = 1.2 }, new Prop { Kind = PropKind.Tree, X = 5, Y = 4, Size = 1.3 },
                new Prop { Kind = PropKind.Tree, X = 4, Y = 7, Size = 1.5 }, new Prop { Kind = PropKind.Pine, X = 9, Y = 7 }, new Prop { Kind = PropKind.Bush, X = 9, Y = 8 },
                new Prop { Kind = PropKind.Stage, X = 9.5, Y = 4 }, new Prop { Kind = PropKind.Stall, X = 4, Y = 8.5 }, new Prop { Kind = PropKind.Stall, X = 9, Y = 8.5 },
                new Prop { Kind = PropKind.Bar, X = 3, Y = 5 }, new Prop { Kind = PropKind.Trough, X = 5, Y = 6 }, new Prop { Kind = PropKind.NoticeBoard, X = 3, Y = 3 },
            ],
        };
        var walls = TownPieces.WallSet(m)!;
        Assert.Equal(["med-wall-2", "med-wall-1", "med-wall-corner-out", "med-wall-gate", "med-wall-end", "med-guard-tower"],
            walls.Items.Select(i => FurnitureCatalog.All.First(e => e.Name == i.Name && e.Theme == "Medieval" && e.Room == FurnitureCatalog.Town).Id));
        Assert.Equal([8, 5, 4, 1, 2, 1], walls.Items.Select(i => i.Count));
        Assert.True(FurnitureKit.Make(walls).Ok);
        // The oaks the same size together (to the nearest quarter square), and the pine; the bush isn't cut.
        var trees = TownPieces.TreeSet(m)!;
        Assert.Equal([(TreeStyle.Oak, 2), (TreeStyle.Oak, 1), (TreeStyle.Pine, 1)], trees.Items.Select(i => (i.Tree, i.Count)));
        Assert.True(trees.Items[1].WidthMm > trees.Items[0].WidthMm);
        Assert.True(FurnitureKit.Make(trees).Ok);
        var pieces = TownPieces.TownPropSet(m)!;
        Assert.Equal([(ItemKind.Stage, 1), (ItemKind.Stall, 2), (ItemKind.Stall, 1), (ItemKind.Trough, 1), (ItemKind.Stall, 1)], pieces.Items.Select(i => (i.Kind, i.Count)));
        Assert.True(FurnitureKit.Make(pieces).Ok);
        // In the desert, palms (there's no pine), and on bigger squares, everything bigger.
        var desert = TownPieces.TreeSet(m with { Theme = "Desert" })!;
        Assert.All(desert.Items, i => Assert.Equal(TreeStyle.Palm, i.Tree));
        var big = TownPieces.TownPropSet(m with { SquareMm = 30 })!;
        Assert.True(big.Items[0].WidthMm > pieces.Items[0].WidthMm * 1.15);
        // Nothing in an empty corner.
        Assert.Null(TownPieces.TreeSet(m, (12, 0, 16, 2)));
        Assert.Null(TownPieces.TownPropSet(m, (12, 0, 16, 2)));
    }

    [Fact]
    public void A_made_towns_wall_has_guard_towers_and_its_square_a_stage_and_stalls()
    {
        var (m, notes) = TownMaker.Make(new TownOptions { Across = 64, Down = 48, Layout = TownLayout.Crossroads, Walls = true, Seed = 3 });
        var towers = m.Props.Where(p => p.Kind == PropKind.GuardTower).ToList();
        Assert.NotEmpty(towers);
        // Each astride the wall on a grid point, the wall not running under it.
        var wall = m.Fences.Where(f => f.Kind == FenceKind.CityWall).SelectMany(f => f.Edges).ToList();
        Assert.All(towers, t =>
        {
            Assert.Equal((Math.Round(t.X), Math.Round(t.Y)), (t.X, t.Y));
            Assert.DoesNotContain(wall, e => Math.Abs((e.Across ? e.X + 0.5 : e.X) - t.X) < 0.9 && Math.Abs((e.Across ? e.Y : e.Y + 0.5) - t.Y) < 0.9);
        });
        Assert.Contains(notes, n => n.Contains("guard tower"));
        var cut = TownPieces.Walls(m);
        Assert.Equal((4, 0, towers.Count), (cut.CornersOut, cut.CornersIn, cut.Towers));
        Assert.True(cut.Gates >= 4, $"{cut.Gates} gates");
        // The market square: a stage, stalls, a bar, a notice board and a trough, on its paving and clear of the roads through it.
        foreach (var kind in new[] { PropKind.Stage, PropKind.Stall, PropKind.Bar, PropKind.NoticeBoard, PropKind.Trough })
            Assert.Contains(m.Props, p => p.Kind == kind);
        foreach (var p in m.Props.Where(p => p.Kind is PropKind.Stage or PropKind.Stall or PropKind.Bar or PropKind.NoticeBoard or PropKind.Trough))
        {
            var (w, d) = Prop.Footprint(p.Kind)!.Value;
            bool turned = Math.Abs(p.TurnDeg % 180 - 90) < 1;
            double hw = (turned ? d : w) / 2, hd = (turned ? w : d) / 2;
            for (double x = p.X - hw + 0.05; x < p.X + hw; x += 0.2)
                for (double y = p.Y - hd + 0.05; y < p.Y + hd; y += 0.2)
                    Assert.Equal(Ground.Paving, m.GroundAt((int)Math.Floor(x), (int)Math.Floor(y)));
        }
        Assert.All(m.Props.Where(p => p.Kind is PropKind.Stage or PropKind.Stall), p => Assert.DoesNotContain(m.Props.Where(q => q.Id != p.Id && q.Kind != PropKind.Lamp), q => p.Hit(q.X, q.Y)));
        // The wall and its towers, and the square's pieces, cut whole.
        Assert.True(FurnitureKit.Make(TownPieces.WallSet(m)!).Ok);
        Assert.True(FurnitureKit.Make(TownPieces.TownPropSet(m)!).Ok);
    }

    [Fact]
    public void The_town_pieces_are_picked_by_their_footprint()
    {
        var stage = new Prop { Kind = PropKind.Stage, X = 5, Y = 5, TurnDeg = 90 };
        // Three squares across, turned: three down.
        Assert.True(stage.Hit(5.2, 6.3));
        Assert.False(stage.Hit(6.3, 5.2));
        Assert.True(new Prop { Kind = PropKind.Tree, X = 1, Y = 1 }.Hit(1.2, 1.2));
        Assert.Null(Prop.Footprint(PropKind.Tree));
        Assert.Equal("Guard tower", CityMap.Label(PropKind.GuardTower));
        // Saved and read back by name.
        var m = CityMap.Empty(8, 8) with { Props = [stage, new Prop { Kind = PropKind.NoticeBoard, X = 2, Y = 2 }] };
        var back = CityMap.FromJson(m.ToJson())!;
        Assert.Equal([PropKind.Stage, PropKind.NoticeBoard], back.Props.Select(p => p.Kind));
        Assert.Contains("\"NoticeBoard\"", m.ToJson());
    }
}
