using Clipper2Lib;
using PhotonCanvas.Addon.Walls;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;

namespace PhotonCanvas.Tests;

public class WallsTests
{
    static WallKit Make(WallSpec s) => WallKitMaker.Make(s);
    static Part Part(WallKit k, string name) => k.Parts.Single(p => p.Name == name);
    static double Width(PathsD a) { var b = Geo.Bounds(a); return b.right - b.left; }
    static double Height(PathsD a) { var b = Geo.Bounds(a); return b.bottom - b.top; }
    static bool Inside(PathsD area, double x, double y) => Clipper.PointInPolygon(new PointD(x, y), Geo.Tidy(area).OrderByDescending(p => Math.Abs(Clipper.Area(p))).First()) != PointInPolygonResult.IsOutside
        && Geo.Area(Geo.Intersect(area, [Geo.Rect(x - 0.01, y - 0.01, x + 0.01, y + 0.01)])) > 1e-5;

    [Fact]
    public void A_plain_wall_is_a_rectangle_of_the_size_asked()
    {
        var k = Make(new WallSpec { LengthMm = 120, HeightMm = 40, Texture = Textures.Plain, Connectors = [] });
        Assert.True(k.Ok, string.Join("\n", k.Findings));
        var wall = Part(k, "Wall");
        Assert.Equal(120 * 40, Geo.Area(wall.Cut), 3);
        Assert.Empty(wall.Etched);
        Assert.Single(k.Parts);
    }

    [Theory]
    [InlineData("Brick")]
    [InlineData("Stone blocks")]
    [InlineData("Fieldstone")]
    [InlineData("Tiles")]
    [InlineData("Wood planks (upright)")]
    [InlineData("Wood planks (across)")]
    [InlineData("Timber frame")]
    [InlineData("Corrugated metal")]
    [InlineData("Metal plates")]
    [InlineData("Hexagons")]
    public void Every_preset_etches_inside_the_wall_and_not_on_its_ends(string texture)
    {
        var p = Textures.Preset(texture)!;
        var s = new WallSpec { LengthMm = 100, HeightMm = 50, Texture = texture, SizeA = p.SizeA, SizeB = p.SizeB, GrooveMm = p.Groove, Connectors = [] };
        var wall = Part(Make(s), "Wall");
        double etched = Geo.Area(wall.Etched);
        Assert.True(etched > 50, $"{texture}: only {etched:0.#} mm² etched");
        Assert.True(etched < 100 * 50 * 0.9, $"{texture}: {etched:0} mm² etched, nearly all of it");
        // Nothing outside the panel, nothing on the plain ends (4 mm slot + 0.25 mm).
        Assert.Equal(0, Geo.Area(Geo.Subtract(wall.Etched, wall.Cut)), 2);
        Assert.Equal(0, Geo.Area(Geo.Intersect(wall.Etched, [Geo.Rect(0, 0, 4.2, 50), Geo.Rect(95.8, 0, 100, 50)])), 2);
        Assert.All(wall.Lines.SelectMany(l => l), q => Assert.InRange(q.x, 4.2, 95.8));
    }

    [Fact]
    public void Lines_style_scores_instead_of_etching_areas()
    {
        var wall = Part(Make(new WallSpec { Texture = "Brick", Etch = EtchStyle.Lines, Connectors = [] }), "Wall");
        Assert.Empty(wall.Etched);
        Assert.True(wall.Lines.Count > 50);
    }

    [Fact]
    public void Bricks_follow_the_brick_size()
    {
        // 8 × 3.5 bricks with 0.6 mm mortar: the mortar is about (8·3.5 − 7.4·2.9)/(8·3.5) ≈ 23 % of the textured face.
        // No wear: worn bricks lose a little more to nicks, chips and pits.
        var wall = Part(Make(new WallSpec { LengthMm = 100, HeightMm = 49, Texture = "Brick", SizeA = 8, SizeB = 3.5, GrooveMm = 0.6, PlainEnds = false, Wear = 0, Connectors = [] }), "Wall");
        Assert.Equal(0.233, Geo.Area(wall.Etched) / (100 * 49), 2);
    }

    [Fact]
    public void The_same_variation_gives_the_same_stones()
    {
        var a = Part(Make(new WallSpec { Texture = "Fieldstone", Seed = 5, Connectors = [] }), "Wall");
        var b = Part(Make(new WallSpec { Texture = "Fieldstone", Seed = 5, Connectors = [] }), "Wall");
        var c = Part(Make(new WallSpec { Texture = "Fieldstone", Seed = 6, Connectors = [] }), "Wall");
        Assert.Equal(Geo.Area(a.Etched), Geo.Area(b.Etched), 6);
        Assert.NotEqual(Geo.Area(a.Etched), Geo.Area(c.Etched), 3);
    }

    [Fact]
    public void Battlements_cut_gaps_in_the_top_but_keep_the_ends_whole()
    {
        var s = new WallSpec { LengthMm = 100, HeightMm = 50, Texture = Textures.Plain, Top = TopStyle.Battlements, MerlonWidthMm = 7, CrenelWidthMm = 5, MerlonHeightMm = 6, Connectors = [] };
        var wall = Part(Make(s), "Wall");
        // 91.5 mm between the ends: floor((91.5 − 3.5) / 12) = 7 gaps, 5 × 6 mm each.
        Assert.Equal(100 * 50 - 7 * 30, Geo.Area(wall.Cut), 1);
        Assert.Equal(50, Height(wall.Cut), 3);
        Assert.True(Inside(wall.Cut, 2, 49), "the end is cut down");
        Assert.True(Inside(wall.Cut, 98, 49), "the end is cut down");
    }

    [Fact]
    public void A_walkway_comes_with_brackets_and_slots_for_them()
    {
        var s = new WallSpec { LengthMm = 120, HeightMm = 50, Texture = Textures.Plain, Top = TopStyle.Battlements, Walkway = true, Connectors = [] };
        var k = Make(s);
        Assert.True(k.Ok, string.Join("\n", k.Findings));
        var walkway = Part(k, "Walkway");
        var brackets = Part(k, "Walkway bracket");
        Assert.True(brackets.Copies >= 2);
        // One slot per bracket, through the wall: the cut has that many holes.
        Assert.Equal(1 + brackets.Copies, Geo.Tidy(Part(k, "Wall").Cut).Count);
        Assert.Equal(10, Height(walkway.Cut), 3);
    }

    [Fact]
    public void A_ruined_top_is_lower_in_the_middle_and_whole_at_the_ends()
    {
        var s = new WallSpec { LengthMm = 120, HeightMm = 60, Texture = "Brick", Top = TopStyle.Ruined, RuinDepth = 0.5, Connectors = [] };
        var wall = Part(Make(s), "Wall");
        Assert.True(Geo.Area(wall.Cut) < 120 * 60 * 0.9);
        Assert.True(Inside(wall.Cut, 2, 59) && Inside(wall.Cut, 118, 59));
        Assert.Equal(1, Geo.Pieces(wall.Cut));
    }

    [Fact]
    public void A_doorway_is_cut_through_the_bottom_and_a_loose_door_fits_it()
    {
        var s = new WallSpec { LengthMm = 100, HeightMm = 50, Texture = "Brick", Door = DoorKind.DoorwayAndDoor, DoorShape = OpeningShape.Rectangle, DoorWidthMm = 16, DoorHeightMm = 28, Connectors = [] };
        var k = Make(s);
        var wall = Part(k, "Wall");
        Assert.Equal(100 * 50 - 16 * 28, Geo.Area(wall.Cut), 1);
        Assert.False(Inside(wall.Cut, 50, 1));
        var door = Part(k, "Door");
        Assert.Equal(16 - 2 * WallKitMaker.DoorClearMm, Width(door.Cut), 2);
        Assert.Equal(28 - 2 * WallKitMaker.DoorClearMm, Height(door.Cut), 2);
        Assert.NotEmpty(door.Etched);
        // The frame keeps the texture back from the doorway.
        Assert.Equal(0, Geo.Area(Geo.Intersect(wall.Etched, [Geo.Rect(42 - 0.6, 0, 58 + 0.6, 28 + 0.6)])), 2);
    }

    [Fact]
    public void A_closed_door_is_etched_not_cut()
    {
        var wall = Part(Make(new WallSpec { Texture = "Brick", Door = DoorKind.ClosedDoor, Connectors = [] }), "Wall");
        Assert.Equal(100 * 50, Geo.Area(wall.Cut), 1);
        Assert.True(Geo.Area(Geo.Intersect(wall.Etched, [Geo.Rect(43, 0, 57, 26)])) > 5);
    }

    [Fact]
    public void Windows_spread_clear_of_the_door_and_bars_split_them_into_panes()
    {
        var s = new WallSpec { LengthMm = 150, HeightMm = 50, Texture = Textures.Plain, Door = DoorKind.Doorway, Windows = 2, WindowShape = OpeningShape.Rectangle, WindowWidthMm = 10, WindowHeightMm = 12, SillMm = 25, Bars = WindowBars.Cross, Connectors = [] };
        var wall = Part(Make(s), "Wall");
        var holes = Geo.Tidy(wall.Cut).Where(p => Clipper.Area(p) < 0).ToList();
        Assert.Equal(8, holes.Count);    // two windows, four panes each
        // One window each side of the door.
        Assert.Contains(holes, h => h.Max(q => q.x) < 75 - 8);
        Assert.Contains(holes, h => h.Min(q => q.x) > 75 + 8);
    }

    [Fact]
    public void Too_many_windows_are_reported_not_squeezed_in()
    {
        var k = Make(new WallSpec { LengthMm = 60, Texture = Textures.Plain, Windows = 10, WindowWidthMm = 10, Connectors = [] });
        Assert.Contains(k.Findings, f => f.Level == Severity.Warn && f.Text.Contains("of the 10 windows"));
    }

    [Fact]
    public void A_door_too_tall_for_the_wall_is_refused()
    {
        var k = Make(new WallSpec { HeightMm = 30, Door = DoorKind.Doorway, DoorHeightMm = 29, Connectors = [] });
        Assert.False(k.Ok);
    }

    [Fact]
    public void Mount_slots_are_as_wide_as_the_sheet_plus_the_fit_and_accessories_fit_them()
    {
        var s = new WallSpec { LengthMm = 100, HeightMm = 50, ThicknessMm = 3, FitMm = 0.15, Texture = Textures.Plain, Mounts = 3, MountHeightMm = 30, MountSlotMm = 6, Accessories = Accessory.OneOfEach, Walls = 2, Connectors = [] };
        var k = Make(s);
        var holes = Geo.Tidy(Part(k, "Wall").Cut).Where(p => Clipper.Area(p) < 0).ToList();
        Assert.Equal(3, holes.Count);
        Assert.All(holes, h => { Assert.Equal(3.15, h.Max(q => q.x) - h.Min(q => q.x), 3); Assert.Equal(6, h.Max(q => q.y) - h.Min(q => q.y), 3); });
        foreach (var name in new[] { "Wall peg", "Torch sconce", "Shelf bracket" })
        {
            var a = Part(k, name);
            Assert.Equal(6, a.Copies);   // 3 mounts × 2 walls
            // The tab (x < 0) goes in as deep as the wall, no taller than the slot.
            var tab = Geo.Intersect(a.Cut, [Geo.Rect(-10, -50, 0, 50)]);
            Assert.Equal(2.8, Width(tab), 2);
            Assert.True(Height(tab) < 6);
            Assert.Equal(1, Geo.Pieces(a.Cut));
        }
    }

    [Fact]
    public void Two_sided_walls_get_a_mirrored_back_and_double_width_slots()
    {
        var s = new WallSpec { LengthMm = 100, ThicknessMm = 3, TwoSided = true, Texture = "Brick", Door = DoorKind.Doorway, DoorOffsetMm = 20, Connectors = new() { [ConnectorKind.Corner] = 1 } };
        var k = Make(s);
        var front = Part(k, "Wall");
        var back = Part(k, "Wall back");
        Assert.Equal(Geo.Area(front.Cut), Geo.Area(back.Cut), 3);
        // The door is right of the middle on the front, so left of it on the mirror image.
        Assert.False(Inside(front.Cut, 70, 1));
        Assert.False(Inside(back.Cut, 30, 1));
        Assert.Equal(6, k.WallThicknessMm);
        var layer = Part(k, "Corner column layer");
        // A 16 mm square with two 6.15 × 4 mm slots and a 2 mm pin hole.
        Assert.Equal(256 - 2 * 6.15 * 4 - Math.PI, Geo.Area(layer.Cut), 1);
    }

    [Theory]
    [InlineData(ConnectorKind.Straight, 2)]
    [InlineData(ConnectorKind.Corner, 2)]
    [InlineData(ConnectorKind.Angle135, 2)]
    [InlineData(ConnectorKind.Sharp45, 2)]
    [InlineData(ConnectorKind.Tee, 3)]
    [InlineData(ConnectorKind.Cross, 4)]
    [InlineData(ConnectorKind.EndCap, 1)]
    [InlineData(ConnectorKind.Custom, 2)]
    public void Every_connector_has_a_slot_per_wall_and_stays_in_one_piece(ConnectorKind kind, int slots)
    {
        var s = new WallSpec { HeightMm = 50, ThicknessMm = 3, ColumnMm = 18, SlotDepthMm = 4, PinMm = 2, Connectors = new() { [kind] = 2 } };
        var k = Make(s);
        Assert.True(k.Ok, string.Join("\n", k.Findings));
        var layer = k.Parts.Single(p => p.Name.EndsWith("column layer"));
        Assert.Equal(17 * 2, layer.Copies);   // 50 / 3 → 17 layers, two columns
        Assert.Equal(1, Geo.Pieces(layer.Cut));
        // Area: the column less its slots (3.15 × 4 each, measured where they enter) and the pin.
        var shape = Connectors.ShapeFor(s, Connectors.Directions(kind, s.CustomAngleDeg));
        double full = Geo.Area([Connectors.Outline(shape, 18)]);
        Assert.InRange(full - Geo.Area(layer.Cut) - Math.PI, slots * 3.15 * 4 - 1.5, slots * 3.15 * 4 + 1.5);
        var cap = k.Parts.Single(p => p.Name.EndsWith("column cap"));
        Assert.Equal(2, cap.Copies);
        Assert.True(Width(cap.Cut) > Width(layer.Cut) + 3);
    }

    [Fact]
    public void Columns_pick_a_shape_that_suits_the_angles()
    {
        var s = new WallSpec();
        Assert.Equal(ColumnShape.Square, Connectors.ShapeFor(s, Connectors.Directions(ConnectorKind.Tee, 0)));
        Assert.Equal(ColumnShape.Octagon, Connectors.ShapeFor(s, Connectors.Directions(ConnectorKind.Angle135, 0)));
        Assert.Equal(ColumnShape.Round, Connectors.ShapeFor(s, Connectors.Directions(ConnectorKind.Custom, 120)));
    }

    [Fact]
    public void Slots_that_meet_in_the_middle_are_refused()
    {
        var k = Make(new WallSpec { ColumnMm = 8, SlotDepthMm = 3.8, PinMm = 0, Connectors = new() { [ConnectorKind.Sharp45] = 1 } });
        Assert.False(k.Ok);
        Assert.Contains(k.Findings, f => f.Level == Severity.Bad && f.Text.StartsWith("Sharp (45°)"));
    }

    [Fact]
    public void Measured_between_centres_the_panel_is_shorter_by_the_columns()
    {
        var s = new WallSpec { LengthMm = 50.8, Measure = LengthMeasure.Centres, ColumnMm = 16, SlotDepthMm = 4, Texture = Textures.Plain, Connectors = new() { [ConnectorKind.Straight] = 1 } };
        var k = Make(s);
        // Each end sits 8 − 4 + 0.25 mm from its column's centre.
        Assert.Equal(50.8 - 2 * 4.25, k.PanelLengthMm, 6);
        Assert.Equal(50.8 - 2 * 4.25, Width(Part(k, "Wall").Cut), 3);
    }

    [Fact]
    public void A_foot_replaces_the_bottom_layer()
    {
        var k = Make(new WallSpec { HeightMm = 30, ThicknessMm = 3, Foot = true, Cap = false, Connectors = new() { [ConnectorKind.EndCap] = 1 } });
        Assert.Equal(9, Part(k, "End column layer").Copies);
        Assert.Equal(1, Part(k, "End column foot").Copies);
        Assert.DoesNotContain(k.Parts, p => p.Name.EndsWith(" cap"));
    }

    [Fact]
    public void Your_own_texture_repeats_the_shapes_and_survives_the_design_file()
    {
        // A 4 mm circle on a 6 mm tile.
        var t = CustomTextures.FromGeometry("Dots", [PathGeometry.Ellipse(10, 10, 2, 2)], 2, 0.5)!;
        Assert.Equal(6, t.TileWidthMm, 3);
        var s = new WallSpec { LengthMm = 60, HeightMm = 30, Texture = "Dots", Custom = t, SizeA = 6, PlainEnds = false, Connectors = [] };
        var wall = Part(Make(s), "Wall");
        // About one 12.6 mm² dot per 36 mm² tile.
        Assert.InRange(Geo.Area(wall.Etched) / (60 * 30), 0.28, 0.42);
        var back = WallSpec.FromJson(s.ToJson())!;
        Assert.Equal("Dots", back.Custom!.Name);
        Assert.Equal(Geo.Area(wall.Etched), Geo.Area(Part(Make(back), "Wall").Etched), 3);
    }

    [Fact]
    public void Your_own_textures_are_kept_for_next_time()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pc-walls-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new CustomTextures(dir);
            store.Put(CustomTextures.FromGeometry("Lines", [PathGeometry.Line(new Vec2(0, 0), new Vec2(5, 0))], 1, 0)!);
            var again = new CustomTextures(dir);
            Assert.Equal("Lines", Assert.Single(again.All).Name);
            again.Remove("Lines");
            Assert.Empty(new CustomTextures(dir).All);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void Settings_round_trip_through_json()
    {
        var s = new WallSpec { LengthMm = 77, Texture = "Fieldstone", Top = TopStyle.Ruined, Door = DoorKind.ClosedDoor, Connectors = new() { [ConnectorKind.Tee] = 3, [ConnectorKind.Corner] = 1 } };
        var back = WallSpec.FromJson(s.ToJson())!;
        Assert.Equal(s.ToJson(), back.ToJson());
        Assert.Equal(3, back.ConnectorCount(ConnectorKind.Tee));
        Assert.Contains("\"Fieldstone\"", s.ToJson());
        Assert.Null(WallSpec.FromJson("{not json"));
    }

    [Fact]
    public void Placing_puts_every_piece_on_its_layers_and_can_replace_it()
    {
        var machine = MachineProfile.F2Ultra;
        var spec = new WallSpec { Texture = "Brick", Door = DoorKind.DoorwayAndDoor, Mounts = 2, Accessories = Accessory.Pegs, Connectors = new() { [ConnectorKind.Corner] = 2 } };
        var kit = Make(spec);
        var (s, sheets, id) = Placement.Place(DocState.New(), kit, spec, machine, new PlaceOptions());
        int expected = kit.PieceCount;
        var all = Enumerable.Range(0, sheets).SelectMany(i => Pages.SwitchTo(s, i).Shapes).ToList();
        Assert.Equal(expected, all.Count(x => x.LayerId == s.Layers.First(l => l.Name == Placement.CutLayer).Id || x.AddonData.ContainsKey(Placement.SpecKey)));
        Assert.Contains(s.Layers, l => l.Name == Placement.EtchLayer && l.Mode == LayerMode.Fill);
        // The etch runs before the cut.
        Assert.True(s.Layers.FindIndex(l => l.Name == Placement.EtchLayer) < s.Layers.FindIndex(l => l.Name == Placement.CutLayer));
        Assert.All(s.Shapes, x => Assert.True(x.Bounds.MinX >= 0 && x.Bounds.MaxX <= machine.FieldW && x.Bounds.MinY >= 0 && x.Bounds.MaxY <= machine.FieldH));
        Assert.Equal(spec.ToJson(), Placement.Find(s, [s.Shapes[0].Id])!.Value.Spec.ToJson());

        var changed = spec with { Texture = "Tiles", Connectors = [] };
        var (s2, _, id2) = Placement.Place(s, Make(changed), changed, machine, new PlaceOptions { Replace = id });
        Assert.Equal(id, id2);
        Assert.DoesNotContain(s2.Shapes, x => x.Name.Contains("column"));
        Assert.Equal("Tiles", Placement.Find(s2, [s2.Shapes[0].Id])!.Value.Spec.Texture);
    }
}
