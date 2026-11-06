using PhotonCanvas.Addon.CityBuilder;
using PhotonCanvas.Addon.HouseEditor;
using Severity = PhotonCanvas.Addon.Buildings.Severity;

namespace PhotonCanvas.Tests;

/// <summary>The city builder's sci-fi look: cyberpunk cities of tall flat-roofed blocks, not medieval towns in grey.</summary>
public class CityBuilderSciFiTests
{
    static HashSet<(int X, int Y)> Box(int w, int d) => [.. Enumerable.Range(0, w).SelectMany(x => Enumerable.Range(0, d).Select(y => (x, y)))];

    [Theory]
    [InlineData(TownLayout.Crossroads, false, false, 1)]
    [InlineData(TownLayout.Grid, false, true, 2)]
    [InlineData(TownLayout.Riverside, true, false, 3)]
    public void A_sci_fi_town_is_a_dense_cyberpunk_city(TownLayout layout, bool walls, bool river, int seed)
    {
        var (m, notes) = TownMaker.Make(new TownOptions { Theme = "Sci-fi", Across = 64, Down = 48, Layout = layout, Walls = walls, River = river, Seed = seed });
        string info = string.Join(" ", notes);
        Assert.Empty(CityFootprint.Overlaps(m));
        Assert.True(m.Buildings.Count >= 18, $"{m.Buildings.Count} buildings; {info}");
        // Tall: most are three floors or more, flat-topped with a roof deck and things on it, no pitched roofs.
        Assert.All(m.Buildings, b => Assert.Empty(b.Design.Roofs));
        Assert.True(m.Buildings.Count(b => b.Design.Levels.Count(l => l.Walls.Count > 0) >= 3) >= m.Buildings.Count / 2, info);
        Assert.True(m.Buildings.Count(b => b.Design.Levels[^1].Furniture.Count > 0) >= m.Buildings.Count * 3 / 4);
        // City kinds: no cottages, barns or taverns.
        Assert.DoesNotContain(m.Buildings, b => b.Type is "House" or "Cottage" or "Hovel" or "Barn" or "Tavern" or "Temple");
        Assert.Contains(m.Buildings, b => b.Type is "Hab block" or "Stack");
        Assert.Contains(m.Buildings, b => b.Type is "Corp tower" or "Neon bar" or "Arcade" or "Tech shop" or "Noodle bar");
        Assert.Equal(m.Buildings.Count, m.Buildings.Select(b => b.Name).Distinct().Count());
        // Paved all over: no grass, fields or woods, light pylons along its streets and signposts at its crossings.
        var paved = m.Paved();
        for (int y = 0; y < m.Down; y++)
            for (int x = 0; x < m.Across; x++)
                Assert.True(m.GroundAt(x, y) is Ground.Paving or Ground.Water, $"{m.GroundAt(x, y)} at {x},{y}");
        foreach (var b in m.Buildings)
            Assert.All(CityFootprint.Cells(b), c => Assert.Equal(Ground.Paving, paved.GroundAt(c.X, c.Y)));
        Assert.True(m.Props.Count(p => p.Kind == PropKind.Lamp) >= 12, $"{m.Props.Count(p => p.Kind == PropKind.Lamp)} lamps");
        Assert.True(m.Props.Count(p => p.Kind is PropKind.Tree or PropKind.Pine) < 15);
        Assert.Contains(m.Items, i => i.CatalogId == "sci-signpost");
        Assert.All(m.Roads, r => Assert.True(r.Width is 3 or 4, $"{r.Name} is {r.Width} wide"));
        Assert.All(m.Roads, r => Assert.False(string.IsNullOrWhiteSpace(r.Name)));
        Assert.Contains(m.Labels, l => l.Text == "Central Plaza");
        // Fire escapes up the sides of the taller blocks: against a building, off the streets, a landing a floor.
        var escapes = m.Items.Where(i => i.CatalogId == "sci-fire-escape").ToList();
        Assert.True(escapes.Count >= 3, $"{escapes.Count} fire escapes");
        var cellsOf = m.Buildings.SelectMany(CityFootprint.Cells).ToHashSet();
        var streets = m.Roads.SelectMany(r => r.Cells(m.Across, m.Down)).ToHashSet();
        foreach (var e in escapes)
        {
            var (ex, ey) = ((int)Math.Floor(e.X), (int)Math.Floor(e.Y));
            Assert.DoesNotContain((ex, ey), cellsOf);
            Assert.DoesNotContain((ex, ey), streets);
            Assert.Contains(m.Buildings, b => CityFootprint.Cells(b).Any(c => Math.Abs(c.X - ex) <= 1 && Math.Abs(c.Y - ey) <= 1));
            Assert.InRange(e.Item.Rows, 2, 5);
            var kit = PhotonCanvas.Addon.Furniture.FurnitureKit.Make(e.Item, PhotonCanvas.Addon.Furniture.FurnitureThemes.Apply(new PhotonCanvas.Addon.Furniture.FurnitureSet { Items = [], ThicknessMm = 2, FitMm = 0.1 }, "Sci-fi"));
            Assert.True(kit.Ok, string.Join("; ", kit.Findings));
        }
        // A few of them make up into kits.
        foreach (var b in m.Buildings.Where((_, i) => i % 7 == 0).Take(4))
        {
            var kit = HouseKitMaker.Make(b.Design, 420);
            Assert.True(kit.Ok, $"{b.Name} ({b.Type}): " + string.Join("; ", kit.Findings.Where(f => f.Level == Severity.Bad).Select(f => f.Text)));
        }
        // Filling it again finds room on its pavements, not on its plaza or streets.
        var (filled, _) = TownMaker.Fill(m, "Residential", 1, 9, []);
        Assert.Empty(CityFootprint.Overlaps(filled));
        var roads = filled.Roads.SelectMany(r => r.Cells(m.Across, m.Down)).ToHashSet();
        foreach (var b in filled.Buildings.Skip(m.Buildings.Count))
            Assert.DoesNotContain(CityFootprint.Cells(b), roads.Contains);
        if (Environment.GetEnvironmentVariable("PC_PREVIEW") is { } dir)
        {
            var sb = new System.Text.StringBuilder();
            var own = m.Buildings.SelectMany(b => CityFootprint.Cells(b).Select(c => (c, b))).ToDictionary(x => x.c, x => x.b);
            for (int y = 0; y < m.Down; y++)
            {
                for (int x = 0; x < m.Across; x++)
                    sb.Append(own.TryGetValue((x, y), out var b) ? (char)('0' + Math.Min(9, b.Design.Levels.Count(l => l.Walls.Count > 0))) : paved.GroundAt(x, y) switch { Ground.Road => '.', Ground.Water => '~', _ => m.Props.Any(p => (int)p.X == x && (int)p.Y == y) ? '*' : ' ' });
                sb.AppendLine();
            }
            sb.AppendLine(info);
            sb.AppendLine(string.Join(", ", m.Buildings.GroupBy(b => b.Type).Select(g => $"{g.Count()} {g.Key}")));
            File.WriteAllText(Path.Combine(dir, $"town-{layout}.txt"), sb.ToString());
        }
    }

    [Fact]
    public void A_sci_fi_tower_has_a_roof_deck_with_clutter_and_steps_in()
    {
        var seen = new SeenBuilding { Cells = Box(6, 6), Storeys = 5, Type = "Corp tower", SetbackFrom = 3, Seed = 4, DoorTowards = [(2, 6), (3, 6)] };
        var (d, _, _) = BuildingGuess.Make(seen, "Sci-fi", 25.4)!.Value;
        Assert.Equal(6, d.Levels.Count);
        Assert.Empty(d.Roofs);
        Assert.Empty(d.Levels[^1].Walls);
        Assert.NotEmpty(d.Levels[^1].Furniture);
        // The setback: the top floors are smaller than the ground floor.
        Assert.True(d.Levels[4].Walls.Count < d.Levels[0].Walls.Count);
        var kit = HouseKitMaker.Make(d, 420);
        Assert.True(kit.Ok, string.Join("; ", kit.Findings.Where(f => f.Level != Severity.Note).Select(f => f.Text)));
        foreach (var f in kit.Findings.Where(f => f.Level != Severity.Note)) Console.WriteLine(f.Text);
        foreach (var w in kit.Walls.Take(3)) Console.WriteLine($"{w.Level}{w.Letter} L={w.Spec.LengthMm} H={w.Spec.HeightMm} win={w.Spec.WindowWidthMm}x{w.Spec.WindowHeightMm} sill={w.Spec.SillMm} at=[{string.Join(",", w.Spec.WindowAtMm)}] frame={w.Spec.FrameMm} box={w.Spec.Boxed}");
    }
}
