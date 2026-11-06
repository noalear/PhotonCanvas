using System.Diagnostics;
using PhotonCanvas.Addon.Buildings;
using PhotonCanvas.Addon.CityBuilder;
using PhotonCanvas.Addon.HouseEditor;
using PhotonCanvas.App.View3D;
using PhotonCanvas.Core.Imaging;
using Severity = PhotonCanvas.Addon.Buildings.Severity;

namespace PhotonCanvas.Tests;

public class CityBuilderTests
{
    static HashSet<(int X, int Y)> Rect(int x, int y, int w, int d)
    {
        var s = new HashSet<(int, int)>();
        for (int j = y; j < y + d; j++) for (int i = x; i < x + w; i++) s.Add((i, j));
        return s;
    }

    static CityBuilding Made(int x, int y, int w, int d, int storeys = 1, HashSet<(int X, int Y)>? towards = null)
    {
        var f = new SeenBuilding { Cells = Rect(x, y, w, d), Storeys = storeys, DoorTowards = towards ?? [] };
        var (design, mx, my) = BuildingGuess.Make(f, "Medieval", 25.4)!.Value;
        return new CityBuilding { X = mx, Y = my, Design = design };
    }

    // ---- the map ----

    [Fact]
    public void A_map_with_buildings_fences_and_trees_saves_and_loads()
    {
        var m = CityWindow.Sample();
        Assert.NotEmpty(m.Buildings);
        var back = CityMap.FromJson(m.ToJson());
        Assert.NotNull(back);
        Assert.Equal(m.Across, back!.Across);
        Assert.Equal(m.Rows, back.Rows);
        Assert.Equal(m.Buildings.Count, back.Buildings.Count);
        Assert.Equal(m.Buildings[0].Design.ToJson(), back.Buildings[0].Design.ToJson());
        Assert.Equal(m.Fences.Sum(f => f.Edges.Count), back.Fences.Sum(f => f.Edges.Count));
        Assert.Equal(m.Props.Count, back.Props.Count);
        Assert.Equal(Ground.Road, back.GroundAt(17, 3));
        Assert.Null(CityMap.FromJson("{not json"));
    }

    [Fact]
    public void Resizing_keeps_the_ground_and_drops_buildings_off_the_edge()
    {
        var m = CityWindow.Sample();
        var small = m.Resized(20, 14);
        Assert.Equal(20, small.Across);
        Assert.Equal(14, small.Rows.Count);
        Assert.Equal(m.GroundAt(17, 12), small.GroundAt(17, 12));
        Assert.All(small.Buildings, b => { var (w, d) = CityFootprint.Size(b); Assert.True(b.X + w <= 20 && b.Y + d <= 14); });
        Assert.True(small.Buildings.Count < m.Buildings.Count);
        var big = m.Resized(50, 40);
        Assert.Equal(Ground.Grass, big.GroundAt(45, 35));
        Assert.Equal(m.Buildings.Count, big.Buildings.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Turning_a_building_places_its_plan_and_back_again(int turn)
    {
        var b = Made(0, 0, 5, 3) with { X = 10, Y = 4, Turn = turn };
        var (w, d) = CityFootprint.Size(b);
        Assert.Equal(turn % 2 == 0 ? (5, 3) : (3, 5), (w, d));
        foreach (var (x, y) in new[] { (0.0, 0.0), (5.0, 0.0), (1.5, 2.0), (5.0, 3.0) })
        {
            var (mx, my) = CityFootprint.ToMap(b, x, y);
            Assert.InRange(mx, 10, 10 + w);
            Assert.InRange(my, 4, 4 + d);
            var (bx, by) = CityFootprint.FromMap(b, mx, my);
            Assert.Equal(x, bx, 9);
            Assert.Equal(y, by, 9);
        }
        var cells = CityFootprint.Cells(b);
        Assert.Equal(15, cells.Count);
        Assert.All(cells, c => Assert.True(c.X >= 10 && c.X < 10 + w && c.Y >= 4 && c.Y < 4 + d));
    }

    [Fact]
    public void Buildings_on_the_same_squares_are_found()
    {
        var a = Made(2, 2, 4, 3);
        var b = Made(5, 4, 3, 3);
        var c = Made(12, 2, 3, 3);
        var m = CityMap.Empty(20, 20) with { Buildings = [a, b, c] };
        var o = CityFootprint.Overlaps(m);
        Assert.Single(o);
        Assert.Equal(a.Id, o[0].A.Id);
    }

    [Fact]
    public void A_tilted_building_keeps_its_angle_and_stands_on_the_squares_under_it()
    {
        var b = Made(0, 0, 4, 2) with { X = 10, Y = 4, ShiftX = 0.5, ShiftY = 0.25, TiltDeg = 30 };
        Assert.False(CityFootprint.OnGrid(b));
        // It turns about its own middle, which stays put.
        var (cx, cy) = CityFootprint.Centre(b);
        Assert.Equal(12.5, cx, 9);
        Assert.Equal(5.25, cy, 9);
        var (mx, my) = CityFootprint.ToMap(b, 2, 1);
        Assert.Equal(cx, mx, 9);
        Assert.Equal(cy, my, 9);
        // Its corners are 4 and 2 apart still, and every point comes back to where it was in the plan.
        var corners = CityFootprint.Corners(b);
        Assert.Equal(4, Math.Sqrt(Math.Pow(corners[1].X - corners[0].X, 2) + Math.Pow(corners[1].Y - corners[0].Y, 2)), 9);
        Assert.Equal(2, Math.Sqrt(Math.Pow(corners[2].X - corners[1].X, 2) + Math.Pow(corners[2].Y - corners[1].Y, 2)), 9);
        // The top edge slopes down to the right by 30°.
        Assert.Equal(Math.Tan(30 * Math.PI / 180), (corners[1].Y - corners[0].Y) / (corners[1].X - corners[0].X), 9);
        foreach (var (x, y) in new[] { (0.0, 0.0), (4.0, 0.0), (1.5, 2.0), (3.2, 0.7) })
        {
            var (px, py) = CityFootprint.ToMap(b, x, y);
            var (bx, by) = CityFootprint.FromMap(b, px, py);
            Assert.Equal(x, bx, 9);
            Assert.Equal(y, by, 9);
        }
        // Its middle is on it, a point well outside its corners isn't; the squares it stands on take in every square of its plan.
        Assert.True(CityFootprint.Hit(b, cx, cy));
        Assert.False(CityFootprint.Hit(b, cx + 3, cy));
        var cells = CityFootprint.Cells(b);
        Assert.Contains(((int)Math.Floor(cx), (int)Math.Floor(cy)), cells);
        for (int y = 0; y < 2; y++)
            for (int x = 0; x < 4; x++)
            {
                var (px, py) = CityFootprint.ToMap(b, x + 0.5, y + 0.5);
                Assert.Contains(((int)Math.Floor(px), (int)Math.Floor(py)), cells);
            }
        Assert.InRange(cells.Count, 8, 16);
        // Squared to the grid, it's back on whole squares.
        var squared = b with { TiltDeg = 0, ShiftX = 0, ShiftY = 0, X = (int)Math.Round(b.X + b.ShiftX), Y = (int)Math.Round(b.Y + b.ShiftY) };
        Assert.True(CityFootprint.OnGrid(squared));
        Assert.Equal(8, CityFootprint.Cells(squared).Count);
    }

    [Fact]
    public void Turning_a_tilted_building_keeps_its_tilt_and_turns_it_about_the_pivot()
    {
        var b = Made(0, 0, 4, 2) with { X = 10, Y = 4, ShiftX = 0.5, ShiftY = 0.25, TiltDeg = 20 };
        var m = CityMap.Empty(30, 30) with { Buildings = [b] };
        var pick = new CityPick { Buildings = [b.Id] };
        var (before, _) = CityFootprint.Centre(b);
        var (turned, _, pivot) = CityEdit.Turn(m, pick, 1);
        var t = Assert.Single(turned.Buildings);
        Assert.Equal(1, t.Turn);
        Assert.Equal(20, t.TiltDeg, 9);
        Assert.False(CityFootprint.OnGrid(t));
        // A quarter turn clockwise about the pivot takes the middle with it.
        var (cx, cy) = CityFootprint.Centre(t);
        var (bx, by) = CityFootprint.Centre(b);
        Assert.Equal(pivot.X - (by - pivot.Y), cx, 6);
        Assert.Equal(pivot.Y + (bx - pivot.X), cy, 6);
        Assert.Equal(before, bx, 9);
        // Four turns bring it back where it was.
        var (back, _, _) = CityEdit.Turn(turned, pick, 3, pivot);
        var r = Assert.Single(back.Buildings);
        Assert.Equal(b.X + b.ShiftX, r.X + r.ShiftX, 6);
        Assert.Equal(b.Y + b.ShiftY, r.Y + r.ShiftY, 6);
        Assert.Equal(0, r.Turn);
    }

    // ---- finding the grid ----

    /// <summary>A picture with a grid of thin lines every <paramref name="spacing"/> pixels from <paramref name="start"/>, a few heavy outlines over it, and grain.</summary>
    static RasterImage Gridded(int w, int h, double spacing, double start, bool lightLines = false, bool grid = true)
    {
        var px = new byte[w * h * 4];
        var rnd = new Random(9);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int n = rnd.Next(-8, 9);
                int r = 110 + n, g = 150 + n, b = 80 + n;
                // Three roofs, outlined heavily (lines that aren't the grid's).
                if ((x is >= 100 and <= 260 && y is >= 90 and <= 210) || (x is >= 400 and <= 560 && y is >= 300 and <= 420) || (x is >= 620 and <= 700 && y is >= 60 and <= 300))
                {
                    r = 170 + n; g = 80 + n; b = 60 + n;
                    bool edge = x is 100 or 101 or 259 or 260 or 400 or 401 or 559 or 560 or 620 or 621 or 699 or 700 || y is 90 or 91 or 209 or 210 or 300 or 301 or 419 or 420 or 60 or 61 or 299;
                    if (edge) { r = 30; g = 25; b = 20; }
                }
                if (grid)
                {
                    double gx = (x - start) / spacing, gy = (y - start) / spacing;
                    bool line = Math.Abs(gx - Math.Round(gx)) * spacing < 0.5 || Math.Abs(gy - Math.Round(gy)) * spacing < 0.5;
                    if (line) { double f = lightLines ? 1.35 : 0.72; r = (int)(r * f); g = (int)(g * f); b = (int)(b * f); }
                }
                int k = (y * w + x) * 4;
                px[k] = (byte)Math.Clamp(r, 0, 255);
                px[k + 1] = (byte)Math.Clamp(g, 0, 255);
                px[k + 2] = (byte)Math.Clamp(b, 0, 255);
                px[k + 3] = 255;
            }
        return new RasterImage(w, h, px);
    }

    [Theory]
    [InlineData(37.5, 12.3, false)]
    [InlineData(70.25, 3.0, false)]
    [InlineData(48, 20.0, true)]
    public void The_grid_is_found_to_a_fraction_of_a_pixel_whatever_its_spacing(double spacing, double start, bool light)
    {
        var grid = GridFinder.Find(Gridded(800, 600, spacing, start, light));
        Assert.NotNull(grid);
        Assert.Equal(spacing, grid!.SquarePx, 1);
        Assert.Equal(start, grid.OffsetX, 0);
        Assert.Equal(start, grid.OffsetY, 0);
    }

    [Fact]
    public void Heavy_outlines_dont_pass_for_a_coarser_grid_and_no_grid_is_none()
    {
        // The outlines repeat at 160 pixels or so: not the grid, which is every 40.
        var grid = GridFinder.Find(Gridded(800, 600, 40, 0));
        Assert.NotNull(grid);
        Assert.Equal(40, grid!.SquarePx, 1);
        Assert.Null(GridFinder.Find(Gridded(800, 600, 40, 0, grid: false)));
    }

    // ---- making buildings from footprints ----

    [Fact]
    public void A_footprint_gets_walls_round_it_a_door_towards_the_road_windows_and_a_roof()
    {
        // The road is along the top of the footprint.
        var road = Enumerable.Range(3, 5).Select(x => (x, 3)).ToHashSet();
        var b = Made(3, 4, 5, 3, 1, road);
        Assert.Equal((3, 4), (b.X, b.Y));
        var g = b.Design.Levels.Single();
        Assert.Equal(2 * (5 + 3), g.Walls.Count);
        var door = Assert.Single(g.Openings, o => o.Kind == OpeningKind.Door);
        // On the top wall (y = 0 in the design), away from its ends.
        Assert.Equal(0, door.Y, 9);
        Assert.InRange(door.X, 1, 4);
        Assert.Contains(g.Openings, o => o.Kind == OpeningKind.Window);
        var roof = Assert.Single(b.Design.Roofs);
        Assert.Equal((0, 0, 5, 3), (roof.X0, roof.Y0, roof.X1, roof.Y1));
        Assert.True(roof.RidgeAcross);
        // And the building editor makes it, every piece fitting the bed.
        var kit = HouseKitMaker.Make(b.Design);
        Assert.True(kit.Ok, string.Join("; ", kit.Findings.Where(f => f.Level == Severity.Bad).Select(f => f.Text)));
        Assert.True(kit.PieceCount > 10);
    }

    [Fact]
    public void An_L_shaped_footprint_gets_a_roof_for_each_wing_and_two_floors_make()
    {
        var cells = Rect(0, 0, 6, 3);
        cells.UnionWith(Rect(0, 3, 3, 3));
        var f = new SeenBuilding { Cells = cells, Storeys = 2 };
        var (d, _, _) = BuildingGuess.Make(f, "Medieval", 25.4)!.Value;
        Assert.Equal(2, d.Levels.Count);
        Assert.Equal(2, d.Roofs.Count);
        Assert.All(d.Roofs, r => Assert.Equal(1, r.Level));
        // Every square under exactly one roof.
        int covered = d.Roofs.Sum(r => (r.X1 - r.X0) * (r.Y1 - r.Y0));
        Assert.Equal(cells.Count, covered);
        Assert.DoesNotContain(d.Levels[1].Openings, o => o.Kind == OpeningKind.Door);
        var kit = HouseKitMaker.Make(d);
        Assert.True(kit.Ok, string.Join("; ", kit.Findings.Where(x => x.Level == Severity.Bad).Select(x => x.Text)));
    }

    [Fact]
    public void Windows_are_spaced_clear_of_the_ends_of_a_wall()
    {
        Assert.Empty(BuildingGuess.Spots(1));
        Assert.Equal([1.0], BuildingGuess.Spots(2));
        Assert.Equal([1.0, 3.0, 5.0], BuildingGuess.Spots(6));
        foreach (int n in Enumerable.Range(2, 12))
            Assert.All(BuildingGuess.Spots(n), s => Assert.InRange(s, 0.99, n - 0.99));
    }

    [Fact]
    public void Floors_can_be_added_and_taken_off_and_the_roof_moves_with_them()
    {
        var b = Made(0, 0, 4, 4);
        var up = CityWindow.AddFloor(b.Design);
        Assert.Equal(2, up.Levels.Count);
        Assert.All(up.Roofs, r => Assert.Equal(1, r.Level));
        Assert.DoesNotContain(up.Levels[1].Openings, o => o.Kind != OpeningKind.Window);
        var down = CityWindow.TakeFloor(up);
        Assert.Single(down.Levels);
        Assert.All(down.Roofs, r => Assert.Equal(0, r.Level));
        var bare = b.Design with { Roofs = [] };
        Assert.NotEmpty(CityWindow.RoofAgain(bare).Roofs);
    }

    [Fact]
    public void Every_building_in_the_sample_village_makes_its_pieces()
    {
        foreach (var b in CityWindow.Sample().Buildings)
        {
            var kit = HouseKitMaker.Make(b.Design, 300);
            Assert.True(kit.Ok, $"{b.Name} ({CityFootprint.Size(b)}, {b.Design.Levels.Count} floors): " + string.Join("; ", kit.Findings.Where(f => f.Level == Severity.Bad).Select(f => f.Text)));
        }
    }

    // ---- saved buildings ----

    [Fact]
    public void Saved_buildings_load_from_the_building_editors_folder()
    {
        var root = Path.Combine(Path.GetTempPath(), "pc-city-" + Guid.NewGuid().ToString("N"));
        try
        {
            var mine = Path.Combine(root, "citybuilder");
            Directory.CreateDirectory(mine);
            var folder = PrefabShelf.FolderNextTo(mine)!;
            Assert.Equal(Path.Combine(root, "houseeditor", "prefabs"), folder);
            var b = Made(0, 0, 4, 3);
            var p = new PrefabInfo { Id = Guid.NewGuid(), Name = "Bakery", Type = "Shop", District = "Market", Tags = ["bread"], Theme = "Medieval", Design = b.Design.Moved(3, 2) };
            PrefabShelf.Save(folder, p);
            // A file as the building editor writes it, with fields the city builder doesn't use.
            File.WriteAllText(Path.Combine(folder, "other.json"), "{\"Format\":1,\"Name\":\"Shed\",\"Type\":\"Shed\",\"Footprint\":{\"Width\":2},\"Extra\":[1,2],\"Design\":" + Made(0, 0, 2, 2).Design.ToJson() + "}");
            File.WriteAllText(Path.Combine(folder, "broken.json"), "{\"Name\":\"no design\"}");
            var list = PrefabShelf.Load(folder);
            Assert.Equal(2, list.Count);
            var bakery = Assert.Single(list, x => x.Name == "Bakery");
            Assert.Equal(("Shop", "Market"), (bakery.Type, bakery.District));
            Assert.Equal(["bread"], bakery.Tags);
            // Moved back to its top left corner.
            Assert.Equal((4, 3), CityFootprint.Extent(bakery.Design));
            Assert.Contains(list, x => x.Name == "Shed");
            // The building editor reads the one saved here as one of its own, footprint and all.
            var own = Assert.Single(PhotonCanvas.Addon.HouseEditor.HousePrefabs.Load(folder), x => x.Name == "Bakery");
            Assert.Equal((p.Id, "Market", 4, 3), (own.Id, own.District, own.Footprint!.Width, own.Footprint.Depth));
        }
        finally { try { Directory.Delete(root, true); } catch (IOException) { } }
    }

    [Fact]
    public void A_saved_building_that_fits_a_footprint_is_used_turned_to_fit()
    {
        var shop = new PrefabInfo { Id = Guid.NewGuid(), Name = "Shop", Type = "Shop", Design = Made(0, 0, 3, 5).Design };
        var other = new PrefabInfo { Id = Guid.NewGuid(), Name = "Big", Type = "Hall", Design = Made(0, 0, 6, 6).Design };
        // A 5 × 3 footprint: the 3 × 5 shop, turned a quarter.
        var fit = PrefabFit.Fit(new SeenBuilding { Cells = Rect(10, 10, 5, 3), Type = "House" }, [other, shop]);
        Assert.NotNull(fit);
        Assert.Equal(shop.Id, fit!.PrefabId);
        Assert.Equal(1, fit.Turn % 2);
        Assert.Equal((10, 10), (fit.X, fit.Y));
        Assert.Null(PrefabFit.Fit(new SeenBuilding { Cells = Rect(0, 0, 4, 4) }, [other, shop]));
    }

    // ---- reading a battle map ----

    /// <summary>A made-up battle map: grass with a grid, a road across, a red-roofed house, a grey-roofed one, a pond and a wood.</summary>
    static RasterImage Town(int sq = 40, int across = 30, int down = 20)
    {
        int w = sq * across, h = sq * down;
        var px = new byte[w * h * 4];
        var rnd = new Random(5);
        (byte, byte, byte) At(int cx, int cy)
        {
            if (cy is 10 or 11) return (150, 147, 140);                              // road
            if (cx >= 3 && cx <= 6 && cy >= 4 && cy <= 7) return (172, 72, 52);        // red tiles
            if (cx >= 14 && cx <= 18 && cy >= 3 && cy <= 6) return (78, 84, 96);       // slate
            if (cx >= 20 && cx <= 25 && cy >= 14 && cy <= 17) return (60, 112, 172);   // pond
            if (cx >= 2 && cx <= 6 && cy >= 14 && cy <= 17) return (36, 74, 32);       // wood
            return (104, 152, 72);                                                     // grass
        }
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int cx = x / sq, cy = y / sq;
                var (r, g, b) = At(cx, cy);
                int n = rnd.Next(-10, 11);
                // Dark outlines round the roofs, as a battle map draws them.
                bool roof = (r, g, b) is (172, 72, 52) or (78, 84, 96);
                bool edge = roof && (At((x - 3) / sq, cy) != (r, g, b) || At((x + 3) / sq, cy) != (r, g, b) || At(cx, (y - 3) / sq) != (r, g, b) || At(cx, (y + 3) / sq) != (r, g, b));
                if (edge) { r = 30; g = 26; b = 22; n = 0; }
                if (x % sq == 0 || y % sq == 0) { r = (byte)(r * 0.7); g = (byte)(g * 0.7); b = (byte)(b * 0.7); }
                int k = (y * w + x) * 4;
                px[k] = (byte)Math.Clamp(r + n, 0, 255);
                px[k + 1] = (byte)Math.Clamp(g + n, 0, 255);
                px[k + 2] = (byte)Math.Clamp(b + n, 0, 255);
                px[k + 3] = 255;
            }
        return new RasterImage(w, h, px);
    }

    [Fact]
    public void A_battle_map_is_read_as_ground_buildings_and_trees()
    {
        var img = Town();
        var grid = CityReader.FindGrid(img);
        Assert.NotNull(grid);
        Assert.Equal(40, grid!.SquarePx, 0);
        var res = CityReader.Read(img, new CityPicture { SquarePx = grid.SquarePx, OffsetX = grid.OffsetX, OffsetY = grid.OffsetY }, new CityReadOptions());
        var m = res.Map;
        Assert.Equal((30, 20), (m.Across, m.Down));
        Assert.Equal(Ground.Grass, m.GroundAt(10, 2));
        Assert.Equal(Ground.Road, m.GroundAt(15, 10));
        Assert.Equal(Ground.Water, m.GroundAt(22, 15));
        // Both houses, on their own squares, with doors.
        Assert.True(m.Buildings.Count == 2, string.Join("; ", m.Buildings.Select(b => $"{b.X},{b.Y} {CityFootprint.Size(b)}")) + " | " + string.Join(" ", res.Notes));
        var red = Assert.Single(m.Buildings, b => b.X == 3);
        Assert.Equal((3, 4), (red.X, red.Y));
        Assert.Equal((4, 4), CityFootprint.Size(red));
        Assert.Equal("Clay tiles", red.Design.Roofs[0].Covering);
        var grey = Assert.Single(m.Buildings, b => b.X == 14);
        Assert.Equal((5, 4), CityFootprint.Size(grey));
        Assert.Equal("Slate", grey.Design.Roofs[0].Covering);
        Assert.All(m.Buildings, b => Assert.Contains(b.Design.Levels[0].Openings, o => o.Kind == OpeningKind.Door));
        // The slate roof is nowhere near the road: its door is on its front (the bottom).
        var door = grey.Design.Levels[0].Openings.First(o => o.Kind == OpeningKind.Door);
        Assert.Equal(4, door.Y, 9);
        // Squares under the buildings are floor; the wood is trees on grass.
        Assert.Equal(Ground.Floor, m.GroundAt(4, 5));
        Assert.Equal(Ground.Grass, m.GroundAt(4, 15));
        Assert.InRange(m.Props.Count(p => p.Kind is PropKind.Tree or PropKind.Pine && p.X > 2 && p.X < 7 && p.Y > 14 && p.Y < 18), 12, 20);
        Assert.DoesNotContain(m.Props, p => p.Y < 13);
        Assert.NotEmpty(res.Notes);
    }

    /// <summary>A floor plan drawn in lines: a light floor, rooms closed in by dark walls, with a gap for a door.</summary>
    static RasterImage Plans(int sq = 40)
    {
        int across = 16, down = 12, w = sq * across, h = sq * down;
        var px = new byte[w * h * 4];
        bool Wall(int x, int y)
        {
            // A building of two rooms from squares (3, 3) to (10, 7): its outside, a wall at x = 7 between the rooms.
            int gx = (int)Math.Round(x / (double)sq), gy = (int)Math.Round(y / (double)sq);
            bool nearX = Math.Abs(x - gx * sq) <= 3, nearY = Math.Abs(y - gy * sq) <= 3;
            double fx = x / (double)sq, fy = y / (double)sq;
            bool alongTop = nearY && (gy == 3 || gy == 8) && fx >= 3 && fx <= 11;
            bool alongSide = nearX && (gx == 3 || gx == 11 || gx == 7) && fy >= 3 && fy <= 8;
            // The door: a gap in the bottom wall under square (5, 7).
            if (alongTop && gy == 8 && fx > 5 && fx < 6) return false;
            return alongTop || alongSide;
        }
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int k = (y * w + x) * 4;
                byte v = Wall(x, y) ? (byte)20 : (x % sq == 0 || y % sq == 0) ? (byte)170 : (byte)225;
                px[k] = v; px[k + 1] = (byte)(v * 0.97); px[k + 2] = (byte)(v * 0.92); px[k + 3] = 255;
            }
        return new RasterImage(w, h, px);
    }

    [Fact]
    public void A_floor_plan_is_read_as_one_building_with_a_wall_inside_and_its_door_in_the_gap()
    {
        var img = Plans();
        var res = CityReader.Read(img, new CityPicture { SquarePx = 40 }, new CityReadOptions { Buildings = BuildingsShow.Plans, FindTrees = false });
        var b = Assert.Single(res.Map.Buildings);
        Assert.Equal((3, 3), (b.X, b.Y));
        Assert.Equal((8, 5), CityFootprint.Size(b));
        var g = b.Design.Levels[0];
        // The outside (2 × (8 + 5) squares) and the wall between the rooms (5 squares).
        Assert.Equal(26 + 5, g.Walls.Count);
        var door = Assert.Single(g.Openings, o => o.Kind == OpeningKind.Door);
        Assert.Equal(5, door.Y, 9);
        Assert.InRange(door.X, 1.5, 3.5);
        Assert.True(HouseKitMaker.Make(b.Design).Ok);
    }

    // ---- the light 3D view ----

    [Fact]
    public void Ground_of_one_kind_is_merged_into_rectangles()
    {
        var m = CityWindow.Sample();
        var patches = CityScene.Patches(m);
        Assert.Equal(m.Across * m.Down, patches.Sum(p => (p.X1 - p.X0 + 1) * (p.Y1 - p.Y0 + 1)));
        Assert.True(patches.Count < m.Across * m.Down / 8);
        foreach (var (x0, y0, x1, y1, g) in patches)
            for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++) Assert.Equal(g, m.GroundAt(x, y));
    }

    [Fact]
    public void Fence_edges_join_into_straight_runs()
    {
        var runs = CityScene.Runs([new(0, 0, true), new(1, 0, true), new(2, 0, true), new(5, 0, true), new(3, 1, false), new(3, 2, false)]);
        Assert.Equal(3, runs.Count);
        Assert.Contains((0, 0, 3, 0), runs);
        Assert.Contains((5, 0, 6, 0), runs);
        Assert.Contains((3, 1, 3, 3), runs);
    }

    [Fact]
    public void A_big_town_builds_its_3D_view_quickly_and_draws()
    {
        // 120 × 120 squares, 80 buildings, a thousand trees.
        var m = CityMap.Empty(120, 120);
        var list = new List<CityBuilding>();
        for (int i = 0; i < 80; i++)
        {
            int x = i % 10 * 12, y = i / 10 * 14;
            list.Add(Made(x, y, 4 + i % 4, 3 + i % 3, 1 + i % 3) with { Turn = i % 4 });
        }
        var rnd = new Random(1);
        var props = Enumerable.Range(0, 1000).Select(_ => new Prop { X = rnd.NextDouble() * 120, Y = rnd.NextDouble() * 120 }).ToList();
        m = m with { Buildings = list, Props = props };
        var scene = new Scene3D();
        var sw = Stopwatch.StartNew();
        CityScene.Build(scene, m, new CityShow());
        sw.Stop();
        Assert.True(sw.ElapsedMilliseconds < 4000, $"Building the scene took {sw.ElapsedMilliseconds} ms");
        Assert.False(scene.IsEmpty);
        var painter = new Model3DPainter();
        var pic = painter.Picture(scene, Camera3D.YUp(30, 42), 320, 240, Avalonia.Media.Colors.White, draft: true);
        Assert.NotNull(pic);
        // Something other than the background was drawn.
        Assert.Contains(pic!, p => p != 0xFFFFFFFFu);
    }
}

public class CityBuilderSampleMapTests
{
    [Fact]
    public void The_sample_battle_map_is_read_with_most_of_its_houses_where_they_are()
    {
        var img = SampleBattleMap.Make();
        var grid = CityReader.FindGrid(img);
        Assert.NotNull(grid);
        Assert.Equal(SampleBattleMap.SquarePx, grid!.SquarePx, 0);
        var res = CityReader.Read(img, new CityPicture { SquarePx = grid.SquarePx, OffsetX = grid.OffsetX, OffsetY = grid.OffsetY }, new CityReadOptions());
        var m = res.Map;
        Assert.Equal((SampleBattleMap.Across, SampleBattleMap.Down), (m.Across, m.Down));
        // A house is found when a building covers most of its squares.
        int found = 0;
        foreach (var h in SampleBattleMap.Houses)
        {
            var own = new HashSet<(int, int)>();
            for (int y = h.Y; y < h.Y + h.D; y++) for (int x = h.X; x < h.X + h.W; x++) own.Add((x, y));
            if (m.Buildings.Any(b => CityFootprint.Cells(b).Count(c => own.Contains(c)) >= own.Count * 0.7)) found++;
        }
        Assert.True(found >= SampleBattleMap.Houses.Length - 1, $"found {found} of {SampleBattleMap.Houses.Length}: " + string.Join("; ", m.Buildings.Select(b => $"{b.X},{b.Y} {CityFootprint.Size(b)}")));
        // Not many made up out of nothing.
        Assert.True(m.Buildings.Count <= SampleBattleMap.Houses.Length + 2, $"{m.Buildings.Count} buildings: " + string.Join("; ", m.Buildings.Select(b => $"{b.X},{b.Y} {CityFootprint.Size(b)}")));
        // The river, the roads and the square.
        Assert.Equal(Ground.Water, m.GroundAt(29, 5));
        Assert.Contains(m.GroundAt(5, 13), new[] { Ground.Road, Ground.Paving });
        Assert.Contains(m.GroundAt(12, 12), new[] { Ground.Road, Ground.Paving });
        Assert.Equal(Ground.Grass, m.GroundAt(26, 3));
        // The wood at the top left.
        Assert.True(m.Props.Count(p => p.X < 14 && p.Y < 8) >= 15, $"{m.Props.Count(p => p.X < 14 && p.Y < 8)} trees in the wood");
    }
}
