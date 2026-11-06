using PhotonCanvas.Addon.CityBuilder;

namespace PhotonCanvas.Tests;

/// <summary>The city builder's editing of several things at once: picking in a box, moving, turning, copying and pasting, roads and labels.</summary>
public class CityBuilderEditTests
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

    /// <summary>A 20 × 16 map: a 3 × 2 house at (4, 4), a tree at (9.5, 5.5), a label, and a fence along the top of the house and down its right.</summary>
    static CityMap Little(out CityBuilding house, out Prop tree, out CityLabel label) => Little(out house, out tree, out label, out _);

    static CityMap Little(out CityBuilding house, out Prop tree, out CityLabel label, out Fence fence)
    {
        house = Made(4, 4, 3, 2);
        tree = new Prop { Kind = PropKind.Tree, X = 9.5, Y = 5.5 };
        label = new CityLabel { Text = "Mill Lane", X = 6, Y = 8 };
        fence = new Fence { Kind = FenceKind.Fence, Points = [new(4, 3), new(7, 3), new(7, 5)] };
        return CityMap.Empty(20, 16) with { Buildings = [house], Props = [tree], Labels = [label], Fences = [fence] };
    }

    static List<(double, double)> Pts(Fence f) => [.. f.Points.Select(p => (p.X, p.Y))];

    [Fact]
    public void A_box_picks_what_is_wholly_inside_it()
    {
        var m = Little(out var house, out var tree, out var label, out var fence);
        var all = CityEdit.Within(m, 3, 2, 10, 9);
        Assert.Equal([house.Id], all.Buildings);
        Assert.Equal([tree.Id], all.Props);
        Assert.Equal([label.Id], all.Labels);
        Assert.Equal([fence.Id], all.Fences);
        // Drawn from the other corner, and one that cuts the house and the fence in two.
        Assert.Equal(all.Buildings, CityEdit.Within(m, 10, 9, 3, 2).Buildings);
        var part = CityEdit.Within(m, 3, 2, 5.5, 9);
        Assert.Empty(part.Buildings);
        Assert.Empty(part.Fences);
    }

    [Fact]
    public void Old_fences_of_grid_edges_become_lines_from_point_to_point()
    {
        var old = CityMap.Empty(20, 16) with
        {
            Fences =
            [
                new Fence { Kind = FenceKind.Hedge, Edges = [new(4, 3, true), new(5, 3, true), new(6, 3, true), new(7, 3, false), new(7, 4, false), new(12, 12, true)] },
                // A ring: a field's hedge all the way round.
                new Fence { Kind = FenceKind.Fence, Edges = [new(0, 0, true), new(1, 0, true), new(2, 0, false), new(1, 1, true), new(0, 1, true), new(0, 0, false)] },
            ],
        };
        var m = old.Tidied();
        Assert.Equal(3, m.Fences.Count);
        Assert.All(m.Fences, f => Assert.Empty(f.Edges));
        var run = m.Fences.Single(f => f.Kind == FenceKind.Hedge && f.Points.Count == 3);
        Assert.Equal(5, run.Length, 6);
        Assert.Equal(old.Fences[0].Id, run.Id);
        Assert.Contains((7.0, 3.0), Pts(run));
        Assert.Single(m.Fences, f => f.Kind == FenceKind.Hedge && f.Length == 1);
        var ring = m.Fences.Single(f => f.Kind == FenceKind.Fence);
        Assert.True(ring.Closed);
        Assert.Equal(4, ring.Points.Count);
        Assert.Equal(6, ring.Length, 6);
        // The same edges back.
        Assert.Equal(old.Fences.SelectMany(f => f.Edges).ToHashSet(), m.Fences.SelectMany(f => f.GridEdges()).ToHashSet());
        // Saved and opened again, as lines.
        var back = CityMap.FromJson(old.ToJson())!;
        Assert.Equal(3, back.Fences.Count);
        Assert.Equal(Pts(run), Pts(back.Fences.Single(f => f.Id == run.Id)));
    }

    [Fact]
    public void Moving_what_is_picked_moves_it_all_and_keeps_it_on_the_map()
    {
        var m = Little(out var house, out var tree, out var label);
        var pick = CityEdit.Within(m, 3, 2, 10, 9);
        var (moved, after) = CityEdit.Move(m, pick, 3, -1);
        var h = moved.Building(house.Id)!;
        Assert.Equal((house.X + 3, house.Y - 1), (h.X, h.Y));
        Assert.Equal((12.5, 4.5), (moved.Props[0].X, moved.Props[0].Y));
        Assert.Equal((9.0, 7.0), (moved.Labels[0].X, moved.Labels[0].Y));
        Assert.Equal([(7.0, 2.0), (10.0, 2.0), (10.0, 4.0)], Pts(moved.Fences[0]));
        Assert.Equal([moved.Fences[0].Id], after.Fences);
        // Too far: stopped at the edge (the fence's top at row 3 can go up 3 squares, no more).
        var (far, _) = CityEdit.Move(m, pick, -100, -100);
        Assert.Equal((0, 1), (far.Building(house.Id)!.X, far.Building(house.Id)!.Y));
        Assert.Equal((0.0, 0.0), Pts(far.Fences[0])[0]);
        // Nothing to move: the same map.
        Assert.Same(m, CityEdit.Move(m, CityPick.None, 2, 2).Map);
    }

    [Fact]
    public void Turning_what_is_picked_turns_it_about_its_middle()
    {
        var m = Little(out var house, out var tree, out var label);
        var pick = new CityPick { Buildings = [house.Id] };
        var before = CityFootprint.Cells(house);
        var (turned, _, _) = CityEdit.Turn(m, pick, 1);
        var t = turned.Building(house.Id)!;
        Assert.Equal(1, t.Turn);
        Assert.Equal((2, 3), CityFootprint.Size(t));
        // Its squares are the old ones turned a quarter about a grid point: the same number, in a 2 × 3 block.
        var cells = CityFootprint.Cells(t);
        Assert.Equal(before.Count, cells.Count);
        Assert.Equal(cells.Count, Rect(t.X, t.Y, 2, 3).Intersect(cells).Count());
        // Four turns about the same point bring everything back as it was.
        var all = CityEdit.Within(m, 3, 2, 10, 9);
        var (x, p, pivot) = CityEdit.Turn(m, all, 1);
        for (int i = 0; i < 3; i++) (x, p, pivot) = CityEdit.Turn(x, p, 1, pivot);
        Assert.Equal((house.X, house.Y, 0), (x.Building(house.Id)!.X, x.Building(house.Id)!.Y, x.Building(house.Id)!.Turn));
        Assert.Equal(Pts(m.Fences[0]), Pts(x.Fences[0]));
        Assert.Equal((tree.X, tree.Y), (x.Props[0].X, x.Props[0].Y));
        Assert.Equal((label.X, label.Y), (x.Labels[0].X, x.Labels[0].Y));
        // A turn the other way undoes one.
        var (once, p1, pv) = CityEdit.Turn(m, all, 1);
        var (back, _, _) = CityEdit.Turn(once, p1, -1, pv);
        Assert.Equal(Pts(m.Fences[0]), Pts(back.Fences[0]));
        Assert.Equal((house.X, house.Y), (back.Building(house.Id)!.X, back.Building(house.Id)!.Y));
        // Turned at the map's edge, it's moved back onto the map.
        var edge = CityMap.Empty(10, 10) with { Buildings = [Made(0, 0, 6, 1) with { }] };
        var b0 = edge.Buildings[0];
        var (e2, _, _) = CityEdit.Turn(edge, new CityPick { Buildings = [b0.Id] }, 1);
        var eb = e2.Building(b0.Id)!;
        var (ew, ed) = CityFootprint.Size(eb);
        Assert.True(eb.X >= 0 && eb.Y >= 0 && eb.X + ew <= 10 && eb.Y + ed <= 10, $"{eb.X},{eb.Y} {ew}x{ed}");
    }

    [Fact]
    public void Copied_things_paste_as_new_ones_where_asked()
    {
        var m = Little(out var house, out var tree, out _);
        var all = CityEdit.Within(m, 3, 2, 10, 9);
        var clip = CityEdit.Copy(m, all);
        // From the fence's corner (4, 3) to the tree (9.5) and the label (8).
        Assert.Equal((6, 5), (clip.Width, clip.Depth));
        var (pasted, pick) = CityEdit.Paste(m, clip, 11, 8);
        Assert.Equal(2, pasted.Buildings.Count);
        Assert.Equal(2, pasted.Props.Count);
        Assert.Equal(2, pasted.Labels.Count);
        Assert.Equal(2, pasted.Fences.Count);
        Assert.Equal(10, pasted.Fences.Sum(f => f.Length), 6);
        var copy = pasted.Buildings.Single(b => b.Id != house.Id);
        Assert.Contains(copy.Id, pick.Buildings);
        Assert.Equal((house.X + 7, house.Y + 5), (copy.X, copy.Y));
        Assert.DoesNotContain(tree.Id, pick.Props);
        // Too near the edge: moved back so it fits.
        var (edge, _) = CityEdit.Paste(m, clip, 19, 15);
        Assert.All(edge.Buildings, b => { var (w, d) = CityFootprint.Size(b); Assert.True(b.X + w <= 20 && b.Y + d <= 16); });
        // Taking them away.
        var gone = CityEdit.Delete(pasted, pick);
        Assert.Single(gone.Buildings);
        Assert.Single(gone.Fences);
    }

    [Fact]
    public void A_road_runs_round_one_corner_or_straight()
    {
        var bent = CityEdit.Road(2, 2, 8, 5, 1, straight: false);
        Assert.Contains((8, 2), bent);
        Assert.Contains((8, 5), bent);
        Assert.Contains((2, 2), bent);
        Assert.Equal(7 + 3, bent.Count);
        // Three wide: the brush reaches a square past each end too.
        var wide = CityEdit.Road(2, 2, 8, 2, 3, straight: false);
        Assert.Equal(9 * 3, wide.Count);
        Assert.Contains((5, 1), wide);
        Assert.Contains((5, 3), wide);
        var slant = CityEdit.Road(0, 0, 6, 3, 1, straight: true);
        Assert.Equal(7, slant.Count);
        Assert.Contains((6, 3), slant);
        Assert.Single(CityEdit.Road(4, 4, 4, 4, 1, false));
    }

    [Fact]
    public void Labels_and_ids_come_back_from_a_saved_map_and_old_maps_still_load()
    {
        var m = Little(out _, out var tree, out var label);
        var back = CityMap.FromJson(m.ToJson())!;
        Assert.Equal(label, back.Labels.Single());
        Assert.Equal(tree.Id, back.Props.Single().Id);
        // A map saved before labels and tree ids.
        var old = CityMap.FromJson("{\"Name\":\"Old\",\"Across\":4,\"Down\":2,\"Rows\":[\"gggg\",\"gggg\"],\"Props\":[{\"Kind\":\"Tree\",\"X\":1,\"Y\":1},{\"Kind\":\"Bush\",\"X\":2,\"Y\":1}]}")!;
        Assert.Empty(old.Labels);
        Assert.Equal(2, old.Props.Select(p => p.Id).Distinct().Count());
        Assert.DoesNotContain(Guid.Empty, old.Props.Select(p => p.Id));
    }
}
