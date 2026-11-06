using PhotonCanvas.Addon.CityBuilder;

namespace PhotonCanvas.Tests;

/// <summary>The city builder's roads and walls as things of their own: laid over the ground, named, stopped by buildings, bridged, and walls opening for them.</summary>
public class CityBuilderRoadTests
{
    static CityBuilding Made(int x, int y, int w, int d)
    {
        var cells = new HashSet<(int X, int Y)>();
        for (int j = y; j < y + d; j++) for (int i = x; i < x + w; i++) cells.Add((i, j));
        var (design, mx, my) = BuildingGuess.Make(new SeenBuilding { Cells = cells }, "Medieval", 25.4)!.Value;
        return new CityBuilding { X = mx, Y = my, Design = design };
    }

    static CityRoad Road(int ax, int ay, int bx, int by, int width, string name = "") =>
        new() { Name = name, Width = width, Points = CityEdit.RoadLine(ax, ay, bx, by, width, straight: false) };

    [Fact]
    public void A_street_is_two_squares_wide_and_a_high_road_four_with_square_ends()
    {
        var lane = Road(2, 5, 10, 5, 2);
        var cells = lane.Cells(30, 20).ToHashSet();
        Assert.Equal([5, 6], cells.Select(c => c.Y).Distinct().Order());
        Assert.Equal((2, 11), (cells.Min(c => c.X), cells.Max(c => c.X)));
        Assert.Equal(20, cells.Count);
        var high = Road(0, 10, 29, 10, 4);
        var hc = high.Cells(30, 20).ToHashSet();
        Assert.Equal([9, 10, 11, 12], hc.Select(c => c.Y).Distinct().Order());
        Assert.Equal(30 * 4, hc.Count);
        // An odd width keeps to the middles of the squares.
        Assert.Equal(0.5, CityEdit.RoadOffset(3));
        Assert.Equal(0, CityEdit.RoadOffset(4));
        Assert.Equal(3, Road(4, 4, 12, 4, 3).Cells(30, 20).Select(c => c.Y).Distinct().Count());
    }

    [Fact]
    public void Roads_lie_on_the_ground_and_the_ground_stays_under_them()
    {
        var m = CityMap.Empty(20, 12);
        var water = m.GroundGrid();
        for (int y = 0; y < 12; y++) { water[9, y] = Ground.Water; water[10, y] = Ground.Water; }
        m = m.WithGround(water) with { Roads = [Road(0, 5, 19, 5, 2, "Mill Lane")] };
        // The ground itself is still grass and water; laid, the road's squares are road (over the water too: its bridge).
        Assert.Equal(Ground.Grass, m.GroundAt(3, 5));
        Assert.Equal(Ground.Road, m.Paved().GroundAt(3, 5));
        Assert.Equal(Ground.Road, m.Paved().GroundAt(9, 5));
        Assert.Equal(Ground.Water, m.Paved(overWater: false).GroundAt(9, 5));
        Assert.True(m.Bridged(9, 5));
        var bridge = Assert.Single(TownPieces.Bridges(m));
        Assert.Equal(2, bridge.Width);
        // Saved and opened again.
        var back = CityMap.FromJson(m.ToJson())!;
        var r = Assert.Single(back.Roads);
        Assert.Equal(("Mill Lane", 2.0), (r.Name, r.Width));
        Assert.Equal(m.Roads[0].Points, r.Points);
    }

    [Fact]
    public void A_road_drawn_on_from_the_end_of_another_joins_it()
    {
        var m = CityMap.Empty(30, 30);
        (m, var a) = CityEdit.AddRoad(m, [new(3, 6), new(11, 6)], 2, Ground.Road, "Mill Lane");
        (m, var b) = CityEdit.AddRoad(m, [new(11, 6), new(11, 14)], 2, Ground.Road, "Other");
        var r = Assert.Single(m.Roads);
        Assert.Equal(a.Id, r.Id);
        Assert.Equal("Mill Lane", r.Name);
        Assert.Equal(3, r.Points.Count);
        // Wider, or another surface: a road of its own.
        (m, _) = CityEdit.AddRoad(m, [new(11, 14), new(20, 14)], 4, Ground.Road, "High Street");
        (m, _) = CityEdit.AddRoad(m, [new(3, 6), new(3, 2)], 2, Ground.Dirt, "Track");
        Assert.Equal(3, m.Roads.Count);
    }

    [Fact]
    public void A_road_stops_short_of_a_building_in_its_way()
    {
        var house = Made(8, 4, 3, 4);
        var m = CityMap.Empty(30, 20) with { Buildings = [house] };
        var road = Road(0, 5, 20, 5, 2);
        Assert.True(CityEdit.Hits(m, road, CityEdit.BuildingCells(m)));
        var stopped = CityEdit.StopAtBuildings(m, road)!;
        var cells = stopped.Cells(m.Across, m.Down).ToList();
        Assert.Equal(7, cells.Max(c => c.X));
        Assert.False(CityEdit.Hits(m, stopped, CityEdit.BuildingCells(m)));
        // Starting inside it: none at all.
        Assert.Null(CityEdit.StopAtBuildings(m, Road(9, 5, 20, 5, 2)));
        // Clear of it: as it was.
        var clear = Road(0, 12, 20, 12, 2);
        Assert.Same(clear, CityEdit.StopAtBuildings(m, clear));
    }

    [Fact]
    public void A_road_cut_by_a_square_stops_square_at_its_edge()
    {
        var road = Road(0, 10, 29, 10, 4, "King's Road");
        var parts = CityEdit.Outside(road, 10, 8, 20, 14);
        Assert.Equal(2, parts.Count);
        Assert.Equal(road.Id, parts[0].Id);
        Assert.All(parts, p => Assert.Equal("King's Road", p.Name));
        Assert.Equal(9, parts[0].Cells(30, 20).Max(c => c.X));
        Assert.Equal(20, parts[1].Cells(30, 20).Min(c => c.X));
        // One that misses the box altogether is left as it is.
        Assert.Same(road, Assert.Single(CityEdit.Outside(road, 0, 0, 5, 3)));
    }

    [Fact]
    public void A_road_name_is_written_along_it_the_right_way_up_and_clear_of_crossings()
    {
        var along = Road(0, 10, 39, 10, 4, "Queen's Way");
        var down = Road(20, 0, 20, 29, 2, "Oak Street");
        // Along a road across, left to right; along one down, reading upwards.
        var (at, deg) = Assert.Single(CityPlan.NameSpots(along, CityPlan.NameSize(along)));
        Assert.Equal(0, deg);
        Assert.Equal(-90, Assert.Single(CityPlan.NameSpots(down, CityPlan.NameSize(down))).Deg);
        // Slid off the crossing.
        var spots = CityPlan.NameSpots(along, CityPlan.NameSize(along), [along, down]);
        double half = (along.Name.Length * CityPlan.NameSize(along) * 0.55 + 1) / 2;
        Assert.All(spots, s => Assert.True(Math.Abs(s.At.X - 21) > half + 1, $"at {s.At}"));
        // Labels turned any way are kept the right way up.
        Assert.Equal(-90, CityLabel.Upright(90));
        Assert.Equal(-45, CityLabel.Upright(135));
        Assert.Equal(80, CityLabel.Upright(-100));
        Assert.Equal(30, CityLabel.Upright(30));
    }

    [Fact]
    public void A_wall_opens_with_a_gate_where_a_road_goes_through_and_is_capped_at_its_ends()
    {
        var wall = new Fence { Kind = FenceKind.CityWall, Points = [new(2, 10), new(30, 10)] };
        var m = CityMap.Empty(34, 20) with { Fences = [wall], Roads = [Road(15, 0, 15, 19, 2, "Gate Street")] };
        var lay = WallLayout.Of(m);
        var gate = Assert.Single(lay.Gates);
        Assert.Equal(2, gate.Width, 1);
        Assert.Equal(16, gate.Middle.X, 1);
        Assert.Equal(2, lay.Pieces.Count);
        Assert.Equal(2, lay.Ends.Count(e => e.Kind == WallEndKind.Cap));
        Assert.Equal(2, lay.Ends.Count(e => e.Kind == WallEndKind.Gate));
        // Moved, the road takes its gate with it.
        var moved = m with { Roads = [Road(22, 0, 22, 19, 2)] };
        Assert.Equal(23, Assert.Single(WallLayout.Of(moved).Gates).Middle.X, 1);
        // Ruined: no caps, no gatehouse, the ends left broken.
        var ruined = WallLayout.Of(m with { Fences = [wall with { Ruined = true }] });
        Assert.Empty(ruined.Gates);
        Assert.All(ruined.Ends, e => Assert.Equal(WallEndKind.Open, e.Kind));
        // Where a river crosses it, it stops either side, capped.
        var g = m.GroundGrid();
        for (int y = 0; y < 20; y++) { g[25, y] = Ground.Water; g[26, y] = Ground.Water; }
        var wet = WallLayout.Of(m.WithGround(g));
        Assert.Equal(3, wet.Pieces.Count);
        Assert.Equal(4, wet.Ends.Count(e => e.Kind == WallEndKind.Cap));
        // A guard tower astride it: the wall stops at the tower either side.
        var towered = WallLayout.Of(m with { Props = [new Prop { Kind = PropKind.GuardTower, X = 8, Y = 10 }] });
        Assert.Equal(2, towered.Ends.Count(e => e.Kind == WallEndKind.Tower));
    }

    [Fact]
    public void Walls_join_end_to_end_close_into_rings_and_have_gaps_cut_in_them()
    {
        var m = CityMap.Empty(30, 30);
        (m, var a) = CityEdit.AddFence(m, FenceKind.StoneWall, new(2, 2), new(12, 2));
        (m, _) = CityEdit.AddFence(m, FenceKind.StoneWall, new(12, 2), new(12, 8));
        (m, _) = CityEdit.AddFence(m, FenceKind.StoneWall, new(12, 8), new(2, 8));
        var f = Assert.Single(m.Fences);
        Assert.Equal(a.Id, f.Id);
        Assert.False(f.Closed);
        (m, f) = CityEdit.AddFence(m, FenceKind.StoneWall, new(2, 8), new(2, 2));
        f = Assert.Single(m.Fences);
        Assert.True(f.Closed);
        Assert.Equal(32, f.Length, 6);
        // A rectangle in one go is the same ring.
        var (r, ring) = CityEdit.AddFenceRectangle(CityMap.Empty(30, 30), FenceKind.Hedge, new(12, 8), new(2, 2), ruined: true);
        Assert.True(ring.Closed && ring.Ruined);
        Assert.Equal(32, ring.Length, 6);
        // A gap cut in a straight wall leaves two; in a ring, one that's open.
        var (s, line) = CityEdit.AddFence(CityMap.Empty(30, 30), FenceKind.Fence, new(2, 2), new(12, 2));
        var cut = CityEdit.CutOut(s, line.Id, new(6.2, 2));
        Assert.Equal(2, cut.Fences.Count);
        Assert.Equal(9, cut.Fences.Sum(x => x.Length), 6);
        var open = CityEdit.CutOut(m, f.Id, new(7, 2));
        var left = Assert.Single(open.Fences);
        Assert.False(left.Closed);
        Assert.Equal(31, left.Length, 6);
    }

    [Fact]
    public void Picking_moving_and_turning_take_roads_and_walls_along()
    {
        var road = Road(2, 2, 10, 2, 2, "Mill Lane");
        var wall = new Fence { Kind = FenceKind.StoneWall, Points = [new(2, 6), new(10, 6)] };
        var m = CityMap.Empty(20, 20) with { Roads = [road], Fences = [wall] };
        var all = CityEdit.Within(m, 0, 0, 12, 8);
        Assert.Equal([road.Id], all.Roads);
        Assert.Equal([wall.Id], all.Fences);
        var (moved, _) = CityEdit.Move(m, all, 3, 4);
        Assert.Equal(road.Points.Select(p => p + new CityPt(3, 4)), moved.Roads[0].Points);
        Assert.Equal(wall.Points.Select(p => p + new CityPt(3, 4)), moved.Fences[0].Points);
        var (turned, _, _) = CityEdit.Turn(m, all, 1);
        // Across before, down after.
        Assert.Equal(turned.Roads[0].Points[0].X, turned.Roads[0].Points[1].X, 6);
        Assert.Equal(turned.Fences[0].Points[0].X, turned.Fences[0].Points[1].X, 6);
        var clip = CityEdit.Copy(m, all);
        var (pasted, pick) = CityEdit.Paste(m, clip, 8, 10);
        Assert.Equal(2, pasted.Roads.Count);
        Assert.Equal("Mill Lane", pasted.Roads.Single(r => pick.Roads.Contains(r.Id)).Name);
        Assert.DoesNotContain(CityEdit.Delete(pasted, all).Roads, r => r.Id == road.Id);
    }

    [Fact]
    public void A_new_building_or_road_gets_a_name_the_town_has_not_got()
    {
        var m = CityMap.Empty(30, 30) with { Buildings = [Made(2, 2, 4, 4) with { Name = "Ashford house", Type = "House" }], Roads = [Road(0, 10, 29, 10, 2, "Mill Lane")] };
        for (int seed = 0; seed < 20; seed++)
        {
            var house = TownMaker.BuildingName(m, "House", seed);
            Assert.Matches(@"^[A-Z][a-z]+(-[A-Z][a-z]+)? house$", house);
            Assert.NotEqual("Ashford house", house);
            Assert.NotEqual("Mill Lane", TownMaker.RoadName(m, seed));
        }
        Assert.EndsWith("'s shop", TownMaker.BuildingName(m, "Shop", 1));
        Assert.StartsWith("The ", TownMaker.BuildingName(m, "Tavern", 1));
    }

    [Fact]
    public void A_made_towns_roads_are_named_and_its_buildings_kept_clear_of_them()
    {
        var (m, _) = TownMaker.Make(new TownOptions { Across = 70, Down = 50, Layout = TownLayout.Crossroads, River = true, Seed = 9 });
        Assert.NotEmpty(m.Roads);
        // The ground under the roads is the ground (grass, the river): the roads lie on it.
        Assert.DoesNotContain(m.Rows, row => row.Contains(CityMap.Code(Ground.Road)));
        var cells = CityEdit.BuildingCells(m);
        Assert.All(m.Roads, r => Assert.False(CityEdit.Hits(m, r, cells), $"{r.Name} runs through a building"));
        // The high roads cross the river on bridges.
        Assert.NotEmpty(TownPieces.Bridges(m));
    }
}
