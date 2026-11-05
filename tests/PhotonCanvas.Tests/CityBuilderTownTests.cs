using System.Diagnostics;
using PhotonCanvas.Addon.CityBuilder;
using PhotonCanvas.Addon.HouseEditor;
using Severity = PhotonCanvas.Addon.Buildings.Severity;

namespace PhotonCanvas.Tests;

/// <summary>The city builder's town maker: whole towns of each layout, and filling a map's roads with buildings.</summary>
public class CityBuilderTownTests
{
    /// <summary>The checks every made town passes: none on the same squares, walls open at the roads, and the buildings it made (from <paramref name="from"/> on) on free ground with doors to the roads.</summary>
    static void Sound(CityMap m, int from = 0)
    {
        Assert.Empty(CityFootprint.Overlaps(m));
        foreach (var b in m.Buildings.Skip(from))
        {
            var (w, d) = CityFootprint.Size(b);
            Assert.True(b.X >= 0 && b.Y >= 0 && b.X + w <= m.Across && b.Y + d <= m.Down, $"{b.Name} off the map");
            foreach (var (x, y) in CityFootprint.Cells(b))
                Assert.True(m.GroundAt(x, y) is Ground.Grass or Ground.Dirt, $"{b.Name} stands on {m.GroundAt(x, y)} at {x},{y}");
            Assert.False(string.IsNullOrWhiteSpace(b.District));
            Assert.False(string.IsNullOrWhiteSpace(b.Type));
        }
        // Most buildings have a door onto a road or the square.
        var made = m.Buildings.Skip(from).ToList();
        int doors = made.Count(b => b.Design.Levels[0].Openings.Any(o =>
        {
            if (o.Kind == OpeningKind.Window) return false;
            var (mx, my) = CityFootprint.ToMap(b, o.X, o.Y);
            return new[] { (0.5, 0.0), (-0.5, 0.0), (0.0, 0.5), (0.0, -0.5) }.Any(dd => m.GroundAt((int)Math.Floor(mx + dd.Item1), (int)Math.Floor(my + dd.Item2)) is Ground.Road or Ground.Paving or Ground.Dirt);
        }));
        Assert.True(doors >= made.Count * 0.85, $"{doors} of {made.Count} buildings have a door to the road");
        // No town wall across a road.
        foreach (var e in m.Fences.Where(f => f.Kind == FenceKind.CityWall).SelectMany(f => f.Edges))
        {
            var (a, b) = e.Across ? (m.GroundAt(e.X, e.Y), m.GroundAt(e.X, e.Y - 1)) : (m.GroundAt(e.X, e.Y), m.GroundAt(e.X - 1, e.Y));
            Assert.False(a == Ground.Road || b == Ground.Road, $"wall across the road at {e}");
        }
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
        Assert.True(m.Buildings.Count >= 25, $"{m.Buildings.Count} buildings; {string.Join(" ", notes)}");
        Assert.True(m.Props.Count > 20);
        Assert.NotEmpty(m.Labels);
        Assert.Contains(m.Buildings, b => b.District == "Market");
        Assert.Contains(m.Buildings, b => b.District == "Residential");
        Sound(m);
        if (walls)
        {
            Assert.Contains(m.Fences, f => f.Kind == FenceKind.CityWall && f.Edges.Count > 40);
            Assert.Equal(4, m.Buildings.Count(b => b.Name == "Corner tower"));
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
        Assert.True(many > few * 1.3, $"{few} sparse, {many} packed");
    }

    [Fact]
    public void Saved_buildings_go_up_where_they_suit_a_plot()
    {
        var (design, _, _) = BuildingGuess.Make(new SeenBuilding { Cells = [.. Enumerable.Range(0, 4).SelectMany(x => Enumerable.Range(0, 3).Select(y => (x, y)))], Type = "House" }, "Medieval", 25.4)!.Value;
        var mine = new PrefabInfo { Id = Guid.NewGuid(), Name = "Kyle's house", Type = "House", District = "Residential", Design = design };
        var (m, notes) = TownMaker.Make(new TownOptions { Across = 60, Down = 44, Seed = 8, Prefabs = [mine] });
        var used = m.Buildings.Where(b => b.PrefabId == mine.Id).ToList();
        Assert.NotEmpty(used);
        Assert.All(used, b => Assert.Equal("Kyle's house", b.Name));
        Assert.Contains("saved buildings", notes[0]);
        Sound(m);
    }

    [Fact]
    public void Filling_a_box_puts_buildings_along_its_roads_only()
    {
        var village = CityWindow.Sample();
        var (filled, added) = TownMaker.Fill(village, "Market", 0.8, 4, [], (19, 0, 36, 12));
        Assert.True(added >= 2, $"{added} added");
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
        Assert.True(m.Buildings.Count > 200, $"{m.Buildings.Count} buildings");
        Assert.True(sw.Elapsed.TotalSeconds < 20, $"{sw.Elapsed.TotalSeconds:0.0} s");
        Sound(m);
    }
}
