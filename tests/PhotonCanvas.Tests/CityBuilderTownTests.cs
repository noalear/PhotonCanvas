using System.Diagnostics;
using PhotonCanvas.Addon.CityBuilder;
using PhotonCanvas.Addon.HouseEditor;
using Severity = PhotonCanvas.Addon.Buildings.Severity;

namespace PhotonCanvas.Tests;

/// <summary>The city builder's town maker: whole towns of each layout, and filling a map's roads with buildings.</summary>
public class CityBuilderTownTests
{
    /// <summary>
    /// The checks every made town passes: none on the same squares, walls open at the roads, and the buildings it
    /// made (from <paramref name="from"/> on) on free ground (not under a road) with doors to the roads.
    /// </summary>
    static void Sound(CityMap m, int from = 0)
    {
        Assert.Empty(CityFootprint.Overlaps(m));
        var paved = m.Paved();
        foreach (var b in m.Buildings.Skip(from))
        {
            var (w, d) = CityFootprint.Size(b);
            Assert.True(b.X >= 0 && b.Y >= 0 && b.X + w <= m.Across && b.Y + d <= m.Down, $"{b.Name} off the map");
            foreach (var (x, y) in CityFootprint.Cells(b))
                Assert.True(paved.GroundAt(x, y) is Ground.Grass or Ground.Dirt, $"{b.Name} stands on {paved.GroundAt(x, y)} at {x},{y}");
            Assert.False(string.IsNullOrWhiteSpace(b.District));
            Assert.False(string.IsNullOrWhiteSpace(b.Type));
        }
        // Most buildings have a door onto a road or the square.
        var made = m.Buildings.Skip(from).ToList();
        int doors = made.Count(b => b.Design.Levels[0].Openings.Any(o =>
        {
            if (o.Kind == OpeningKind.Window) return false;
            var (mx, my) = CityFootprint.ToMap(b, o.X, o.Y);
            return new[] { (0.5, 0.0), (-0.5, 0.0), (0.0, 0.5), (0.0, -0.5) }.Any(dd => paved.GroundAt((int)Math.Floor(mx + dd.Item1), (int)Math.Floor(my + dd.Item2)) is Ground.Road or Ground.Paving or Ground.Dirt);
        }));
        Assert.True(doors >= made.Count * 0.85, $"{doors} of {made.Count} buildings have a door to the road");
        // No town wall across a road: where a road goes through, the wall stops for its gate.
        foreach (var piece in WallLayout.Of(m, f => f.Kind == FenceKind.CityWall).Pieces)
            foreach (var p in Along(piece.Points, piece.Closed))
                Assert.False(m.Roads.Any(r => r.Covers(p.X, p.Y, 0.3)), $"wall across a road at {p}");
    }

    /// <summary>Points every quarter square along a line.</summary>
    static IEnumerable<CityPt> Along(IReadOnlyList<CityPt> pts, bool closed)
    {
        int n = pts.Count;
        for (int i = 0; i < (closed ? n : n - 1); i++)
        {
            var (a, b) = (pts[i], pts[(i + 1) % n]);
            int steps = Math.Max(1, (int)Math.Ceiling(a.DistanceTo(b) * 4));
            for (int k = 0; k <= steps; k++) yield return a + (b - a) * (k / (double)steps);
        }
    }

    /// <summary>
    /// What Kyle asked of a made town: no two buildings touching (a square of ground between), none against or
    /// through the town wall, one of each trade's shop, each name once, every road named, streets two squares
    /// wide and high roads four, a square of shore along the river, and roads over water on bridges.
    /// </summary>
    static void Tidy(CityMap m)
    {
        var cells = m.Buildings.Select(b => CityFootprint.Cells(b).ToHashSet()).ToList();
        for (int i = 0; i < cells.Count; i++)
            for (int j = i + 1; j < cells.Count; j++)
                Assert.False(cells[i].Any(c => Enumerable.Range(-1, 3).Any(dx => Enumerable.Range(-1, 3).Any(dy => cells[j].Contains((c.X + dx, c.Y + dy))))),
                    $"{m.Buildings[i].Name} touches {m.Buildings[j].Name}");
        foreach (var wall in m.Fences.Where(f => f.Kind == FenceKind.CityWall))
            foreach (var (b, own) in m.Buildings.Zip(cells))
                Assert.All(own, c => Assert.False(wall.Near(c.X + 0.5, c.Y + 0.5, 1.4), $"{b.Name} against the town wall at {c}"));
        Assert.Equal(m.Buildings.Count, m.Buildings.Select(b => b.Name).Distinct().Count());
        foreach (var type in new[] { "Stable", "Smithy", "Mill" })
            Assert.True(m.Buildings.Count(b => b.Type == type) <= 1 + m.Across * m.Down / 6000, $"{m.Buildings.Count(b => b.Type == type)} {type}");
        Assert.All(m.Roads, r => Assert.False(string.IsNullOrWhiteSpace(r.Name)));
        Assert.Contains(m.Roads, r => r.Width == 4);
        Assert.All(m.Roads, r => Assert.True(r.Width is 2 or 4, $"{r.Name} is {r.Width} wide"));
        // Houses are named for the families in them.
        Assert.All(m.Buildings.Where(b => b.Type is "House" or "Cottage"), b => Assert.Matches(@"^[A-Z][a-z]+(-[A-Z][a-z]+)? (house|cottage)$", b.Name));
        // Shore along the water: no grass right beside it.
        for (int y = 0; y < m.Down; y++)
            for (int x = 0; x < m.Across; x++)
                if (m.GroundAt(x, y) == Ground.Water)
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                            if (x + dx >= 0 && y + dy >= 0 && x + dx < m.Across && y + dy < m.Down)
                                Assert.NotEqual(Ground.Grass, m.GroundAt(x + dx, y + dy));
        // A road over the river is a bridge.
        var paved = m.Paved(overWater: false);
        bool wet = m.Roads.Any(r => r.Cells(m.Across, m.Down).Any(c => m.GroundAt(c.X, c.Y) == Ground.Water));
        Assert.Equal(wet, TownPieces.Bridges(m).Count > 0);
    }

    [Theory]
    [InlineData(TownLayout.Crossroads, false, false)]
    [InlineData(TownLayout.MainStreet, true, false)]
    [InlineData(TownLayout.Grid, true, true)]
    [InlineData(TownLayout.Winding, false, true)]
    [InlineData(TownLayout.Riverside, true, false)]
    public void Every_layout_makes_a_sound_town(TownLayout layout, bool walls, bool river)
    {
        var (m, notes) = TownMaker.Make(new TownOptions { Across = 60, Down = 44, Layout = layout, Walls = walls, River = river, Seed = 3 });
        Assert.Equal((60, 44), (m.Across, m.Down));
        // Buildings the size they are on a battle map (a cottage 20 by 20 feet, a tavern 40 by 40), a square apart.
        Assert.True(m.Buildings.Count >= 12, $"{m.Buildings.Count} buildings; {string.Join(" ", notes)}");
        Assert.True(m.Props.Count > 20);
        Assert.NotEmpty(m.Labels);
        Assert.Contains(m.Buildings, b => b.District == "Market");
        Assert.Contains(m.Buildings, b => b.District == "Residential");
        Sound(m);
        Tidy(m);
        if (walls)
        {
            // All the way round, a guard tower at each corner, a gate where each high road goes through.
            var wall = Assert.Single(m.Fences, f => f.Kind == FenceKind.CityWall);
            Assert.True(wall.Closed && wall.Length > 100, $"{wall.Length} long");
            Assert.All(wall.Points, c => Assert.Contains(m.Props, p => p.Kind == PropKind.GuardTower && p.X == c.X && p.Y == c.Y));
            Assert.True(WallLayout.Of(m).Gates.Count >= 2);
        }
        if (river || layout == TownLayout.Riverside)
        {
            Assert.Contains(Enumerable.Range(0, m.Down).SelectMany(y => Enumerable.Range(0, m.Across).Select(x => m.GroundAt(x, y))), g => g == Ground.Water);
            if (layout == TownLayout.Riverside) Assert.Contains(m.Buildings, b => b.District == "Docks");
        }
        // The square has its temple and hall.
        Assert.Contains(m.Buildings, b => b.Type == "Temple" || b.Type == "Hall");
        // A few of its buildings make up into kits.
        foreach (var b in m.Buildings.Where((_, i) => i % 9 == 0).Take(4))
        {
            var kit = HouseKitMaker.Make(b.Design, 420);
            Assert.True(kit.Ok, $"{b.Name} ({b.Type}): " + string.Join("; ", kit.Findings.Where(f => f.Level == Severity.Bad).Select(f => f.Text)));
        }
    }

    [Fact]
    public void The_same_seed_makes_the_same_town_and_another_makes_another()
    {
        var o = new TownOptions { Across = 40, Down = 30, Seed = 12 };
        var a = TownMaker.Make(o).Map;
        var b = TownMaker.Make(o).Map;
        Assert.Equal(a.Rows, b.Rows);
        Assert.Equal(a.Buildings.Select(x => (x.Name, x.X, x.Y, x.Type)), b.Buildings.Select(x => (x.Name, x.X, x.Y, x.Type)));
        var c = TownMaker.Make(o with { Seed = 13 }).Map;
        Assert.NotEqual(a.Buildings.Select(x => (x.X, x.Y)), c.Buildings.Select(x => (x.X, x.Y)));
    }

    [Fact]
    public void A_fuller_town_has_more_buildings()
    {
        var o = new TownOptions { Across = 60, Down = 44, Seed = 5, Layout = TownLayout.Grid };
        int few = TownMaker.Make(o with { Density = 0 }).Map.Buildings.Count, many = TownMaker.Make(o with { Density = 1 }).Map.Buildings.Count;
        // Two squares between buildings however full (so they don't run into each other once built): packing them adds fewer.
        Assert.True(many > few * 1.2, $"{few} sparse, {many} packed");
    }

    [Fact]
    public void Saved_buildings_go_up_where_they_suit_a_plot()
    {
        var (design, _, _) = BuildingGuess.Make(new SeenBuilding { Cells = [.. Enumerable.Range(0, 4).SelectMany(x => Enumerable.Range(0, 3).Select(y => (x, y)))], Type = "House" }, "Medieval", 25.4)!.Value;
        var mine = new PrefabInfo { Id = Guid.NewGuid(), Name = "Kyle's house", Type = "House", District = "Residential", Design = design };
        var (m, notes) = TownMaker.Make(new TownOptions { Across = 60, Down = 44, Seed = 8, Prefabs = [mine] });
        var used = m.Buildings.Where(b => b.PrefabId == mine.Id).ToList();
        Assert.True(used.Count > 1);
        // Its name once; the others named for the families in them.
        Assert.Single(used, b => b.Name == "Kyle's house");
        Assert.Equal(m.Buildings.Count, m.Buildings.Select(b => b.Name).Distinct().Count());
        Assert.Contains("saved buildings", notes[0]);
        Sound(m);
    }

    [Fact]
    public void Filling_a_box_puts_buildings_along_its_roads_only()
    {
        var village = CityWindow.Sample();
        var (filled, added) = TownMaker.Fill(village, "Market", 0.8, 4, [], (19, 0, 36, 12));
        Assert.True(added >= 1, $"{added} added");
        Assert.Equal(village.Buildings.Count + added, filled.Buildings.Count);
        foreach (var b in filled.Buildings.Skip(village.Buildings.Count))
        {
            Assert.Equal("Market", b.District);
            Assert.All(CityFootprint.Cells(b), c => Assert.True(c.X >= 19 && c.Y < 12, $"{b.Name} at {c}"));
        }
        Sound(filled, village.Buildings.Count);
        // A box with no road in it: nothing.
        Assert.Equal(0, TownMaker.Fill(village, "Market", 1, 4, [], (0, 0, 8, 7)).Added);
    }

    [Fact]
    public void A_big_city_is_made_quickly()
    {
        var sw = Stopwatch.StartNew();
        var (m, _) = TownMaker.Make(new TownOptions { Across = 160, Down = 120, Layout = TownLayout.Grid, Walls = true, River = true, Density = 0.8, Seed = 2 });
        sw.Stop();
        Assert.True(m.Buildings.Count > 150, $"{m.Buildings.Count} buildings");
        Assert.True(sw.Elapsed.TotalSeconds < 20, $"{sw.Elapsed.TotalSeconds:0.0} s");
        Sound(m);
        Tidy(m);
    }
}
