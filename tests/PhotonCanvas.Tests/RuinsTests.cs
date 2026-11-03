using Clipper2Lib;
using PhotonCanvas.Addon.Ruins;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using WallsSpec = PhotonCanvas.Addon.Walls.WallSpec;
using WallsTextures = PhotonCanvas.Addon.Walls.Textures;
using WallsFace = PhotonCanvas.Addon.Walls.Face;

namespace PhotonCanvas.Tests;

public class RuinsTests
{
    static RuinKit Make(RuinSpec s) => RuinKitMaker.Make(s);
    static Part Part(RuinKit k, string name) => k.Parts.Single(p => p.Name == name);
    static RectD Box(PathsD a) => Geo.Bounds(a);
    static string Bad(RuinKit k) => string.Join("\n", k.Findings.Select(f => $"{f.Level}: {f.Text}"));

    /// <summary>Just a wall, nothing else round it.</summary>
    static RuinSpec Wall(RuinSpec? s = null) => (s ?? new RuinSpec()) with { LooseStones = 0, Chunks = 0, Piles = 0, Pillars = 0 };

    [Fact]
    public void A_broken_wall_is_one_piece_inside_its_size_with_whole_ends_full_height()
    {
        var s = Wall(new RuinSpec { LengthMm = 100, HeightMm = 50, Cracks = 0 });
        var k = Make(s);
        Assert.True(k.Ok, Bad(k));
        var wall = Part(k, "Broken wall");
        Assert.Equal(1, Geo.Pieces(wall.Cut));
        var b = Box(wall.Cut);
        Assert.Equal(0, b.left, 3);
        Assert.Equal(100, b.right, 3);
        Assert.Equal(0, b.top, 3);
        Assert.Equal(50, b.bottom, 3);
        // Each whole end is a straight, full-height strip as deep as the slot (4 mm + 0.25).
        Assert.Equal(4.25 * 50, Geo.Area(Geo.Intersect(wall.Cut, [Geo.Rect(0, 0, 4.25, 50)])), 2);
        Assert.Equal(4.25 * 50, Geo.Area(Geo.Intersect(wall.Cut, [Geo.Rect(95.75, 0, 100, 50)])), 2);
        // Smooth there: no texture where it goes into the columns.
        Assert.Equal(0, Geo.Area(Geo.Intersect(wall.Etched, [Geo.Rect(0, 0, 4.25, 50), Geo.Rect(95.75, 0, 100, 50)])), 3);
        // Broken: well short of a whole panel, but it keeps its bottom course all along.
        Assert.True(Geo.Area(wall.Cut) < 100 * 50 * 0.9);
        Assert.Equal(100 * 3.5, Geo.Area(Geo.Intersect(wall.Cut, [Geo.Rect(0, 0, 100, 3.5)])), 1);
    }

    [Fact]
    public void A_brick_wall_breaks_along_the_mortar()
    {
        // Every corner of the outline (and of the holes) is on the brick grid: 8 × 3.5 bricks, half a brick's offset.
        var s = Wall(new RuinSpec { Texture = "Brick", SizeA = 8, SizeB = 3.5, LengthMm = 96, HeightMm = 49, Breaches = 1, KnockedOut = 3, Cracks = 0,
            LeftEnd = EndKind.Broken, RightEnd = EndKind.Broken });
        var wall = Part(Make(s), "Broken wall");
        foreach (var p in wall.Cut)
            foreach (var q in p)
            {
                Assert.True(Math.Abs(Math.IEEERemainder(q.y, 3.5)) < 1e-3 || Math.Abs(q.y - 49) < 1e-3, $"y = {q.y}");
                Assert.True(Math.Abs(Math.IEEERemainder(q.x, 4)) < 1e-3, $"x = {q.x}");
            }
    }

    [Theory]
    [InlineData(BreakShape.Dip)]
    [InlineData(BreakShape.FallsLeft)]
    [InlineData(BreakShape.FallsRight)]
    [InlineData(BreakShape.TwoGaps)]
    [InlineData(BreakShape.Stump)]
    public void Broken_down_as_far_as_asked(BreakShape shape)
    {
        var s = Wall(new RuinSpec { Texture = Textures.Plain, Break = shape, BreakDepth = 0.6, LeftEnd = EndKind.Broken, RightEnd = EndKind.Broken, Cracks = 0, KnockedOut = 0 });
        var line = BrokenWall.Profile(s, 100, 50, 0, 0, new Random(1));
        double low = Enumerable.Range(0, 101).Min(i => line.At(i));
        Assert.InRange(low, 50 * 0.4 - 6, 50 * 0.4 + 8);
        var k = Make(s);
        Assert.True(k.Ok, Bad(k));
        Assert.Equal(1, Geo.Pieces(Part(k, "Broken wall").Cut));
        if (shape == BreakShape.FallsLeft) Assert.True(line.At(5) < line.At(95));
        if (shape == BreakShape.FallsRight) Assert.True(line.At(5) > line.At(95));
    }

    public static TheoryData<string> Presets()
    {
        var d = new TheoryData<string>();
        foreach (var p in Textures.Presets) d.Add(p.Name);
        return d;
    }

    [Theory]
    [MemberData(nameof(Presets))]
    public void Every_texture_breaks_into_one_sturdy_piece(string texture)
    {
        var p = Textures.Preset(texture)!;
        foreach (int seed in new[] { 1, 2, 3, 4, 5, 6 })
            foreach (var ends in new[] { EndKind.Whole, EndKind.Broken })
            {
                var s = Wall(new RuinSpec { Texture = texture, SizeA = p.SizeA > 0 ? p.SizeA : 8, SizeB = p.SizeB > 0 ? p.SizeB : 3.5, GrooveMm = p.Groove > 0 ? p.Groove : 0.6,
                    Seed = seed, Breaches = 2, KnockedOut = 3, Cracks = 2, LeftEnd = ends, RightEnd = ends, Break = (BreakShape)(seed % 5) });
                var k = Make(s);
                Assert.True(k.Ok, $"{texture} {seed}: {Bad(k)}");
                var wall = Part(k, "Broken wall");
                Assert.Equal(1, Geo.Pieces(wall.Cut));
                // Nothing narrower than the bridge (bar a sliver of rounding) to snap off.
                Assert.True(Rough.Thin(wall.Cut, RuinKitMaker.Bridge * 0.9) < 1.5, $"{texture} {seed}: {Rough.Thin(wall.Cut, RuinKitMaker.Bridge * 0.9):0.##} mm² thin");
                Assert.Equal(0, Geo.Area(Geo.Subtract(wall.Etched, wall.Cut)), 2);
                Assert.All(wall.Lines.SelectMany(l => l), q => Assert.True(Rough.Inside(Geo.Offset(wall.Cut, 0.01), q)));
            }
    }

    [Fact]
    public void Holes_go_right_through_and_stones_come_out_whole()
    {
        var plain = Part(Make(Wall(new RuinSpec { Breaches = 0, KnockedOut = 0, Cracks = 0 })), "Broken wall");
        var knocked = Part(Make(Wall(new RuinSpec { Breaches = 0, KnockedOut = 3, Cracks = 0 })), "Broken wall");
        Assert.Single(Geo.Tidy(plain.Cut));
        // Three bricks (8 × 3.5 with their mortar) cut out, each its own hole.
        Assert.Equal(3 * 8 * 3.5, Geo.Area(plain.Cut) - Geo.Area(knocked.Cut), 2);
        Assert.Equal(4, Geo.Tidy(knocked.Cut).Count);
        var holed = Make(Wall(new RuinSpec { Breaches = 2, KnockedOut = 0, Cracks = 0, Seed = 3 }));
        var w = Part(holed, "Broken wall");
        Assert.Equal(3, Geo.Tidy(w.Cut).Count);
        // Holes, not notches: the outline round the outside is the same as without them.
        Assert.Equal(Geo.Area(Rough.FillHoles(Part(Make(Wall(new RuinSpec { Breaches = 0, KnockedOut = 0, Cracks = 0, Seed = 3 })), "Broken wall").Cut)), Geo.Area(Rough.FillHoles(w.Cut)), 2);
    }

    [Fact]
    public void A_hole_too_big_for_the_wall_is_reported_not_forced()
    {
        var k = Make(Wall(new RuinSpec { HeightMm = 20, Breaches = 1, BreachMm = 18, BreakDepth = 0.8 }));
        Assert.True(k.Ok);
        Assert.Contains(k.Findings, f => f.Level == Severity.Warn && f.Text.Contains("hole"));
        Assert.Equal(1, Geo.Pieces(Part(k, "Broken wall").Cut));
    }

    [Fact]
    public void Cracks_are_scored_and_the_same_variation_gives_the_same_ruin()
    {
        var a = Part(Make(Wall(new RuinSpec { Seed = 7, Cracks = 3 })), "Broken wall");
        var b = Part(Make(Wall(new RuinSpec { Seed = 7, Cracks = 3 })), "Broken wall");
        var c = Part(Make(Wall(new RuinSpec { Seed = 8, Cracks = 3 })), "Broken wall");
        Assert.Equal(Geo.Area(a.Cut), Geo.Area(b.Cut), 6);
        Assert.Equal(a.Lines.Count, b.Lines.Count);
        Assert.NotEqual(Geo.Area(a.Cut), Geo.Area(c.Cut), 3);
        Assert.True(a.Lines.Count >= 3);
        // No cracks of the ruin's own (and no wear, which scores the odd hairline crack in the bricks themselves).
        Assert.Empty(Part(Make(Wall(new RuinSpec { Seed = 7, Cracks = 0, Wear = 0 })), "Broken wall").Lines);
    }

    [Fact]
    public void Wood_snaps_board_by_board()
    {
        var s = Wall(new RuinSpec { Texture = "Wood planks (upright)", SizeA = 5, SizeB = 35, GrooveMm = 0.5, KnockedOut = 0, Cracks = 0 });
        var k = Make(s);
        Assert.True(k.Ok, Bad(k));
        // A splinter (a point) on every board across the broken part: many more corners than a stepped top.
        var wall = Part(k, "Broken wall");
        Assert.True(wall.Cut.Sum(p => p.Count) > 40);
        Assert.Equal(1, Geo.Pieces(wall.Cut));
    }

    [Fact]
    public void Two_sided_walls_get_a_mirrored_back()
    {
        var k = Make(Wall(new RuinSpec { TwoSided = true, Walls = 2 }));
        var front = Part(k, "Broken wall");
        var back = Part(k, "Broken wall back");
        Assert.Equal(2, front.Copies);
        Assert.Equal(2, back.Copies);
        Assert.Equal(Geo.Area(front.Cut), Geo.Area(back.Cut), 3);
        // Turned over, the back lies on the front.
        var flipped = Geo.Map(back.Cut, p => new PointD(100 - p.x, p.y));
        Assert.Equal(0, Geo.Area(Geo.Subtract(front.Cut, Geo.Tidy(flipped))), 2);
    }

    [Fact]
    public void A_free_standing_wall_slots_into_its_base_with_rubble_against_it()
    {
        var s = Wall(new RuinSpec { LeftEnd = EndKind.Broken, RightEnd = EndKind.Broken, Base = true, BaseMm = 15, FootRubble = true, PileHeightMm = 9, ThicknessMm = 3, FitMm = 0.2 });
        var k = Make(s);
        Assert.True(k.Ok, Bad(k));
        var wall = Part(k, "Broken wall");
        var baseP = Part(k, "Base");
        // Two tabs, as deep as the base is thick, under the wall.
        Assert.Equal(-3, Box(wall.Cut).top, 3);
        var tabs = Geo.Tidy(Geo.Intersect(wall.Cut, [Geo.Rect(-1, -3, 101, 0)]));
        Assert.Equal(2, tabs.Count);
        // The base has a slot for each tab: the tab's width and the wall's thickness, plus the fit.
        var holes = Geo.Tidy(baseP.Cut).Where(p => Clipper.Area(p) < 0).ToList();
        Assert.Equal(2, holes.Count);
        foreach (var (h, t) in holes.OrderBy(h => Geo.Bounds([h]).left).Zip(tabs.OrderBy(t => Geo.Bounds([t]).left)))
        {
            var hb = Geo.Bounds([h]);
            var tb = Geo.Bounds([t]);
            Assert.Equal(tb.right - tb.left + 0.2, hb.right - hb.left, 3);
            Assert.Equal(3 + 0.2, hb.bottom - hb.top, 3);
            Assert.Equal((tb.left + tb.right) / 2, (hb.left + hb.right) / 2, 3);
        }
        // The heaps: stacked layers that sit on the base, against the wall's faces.
        var view = k.Views.Single(v => v.Name.Contains("base"));
        var heapLayers = view.Layers.Where(l => l.Name.StartsWith("Rubble")).ToList();
        Assert.NotEmpty(heapLayers);
        foreach (var l in heapLayers)
        {
            Assert.Equal(0, Geo.Area(Geo.Subtract(l.Cut, baseP.Cut)), 2);
            Assert.Equal(0, Geo.Area(Geo.Intersect(l.Cut, [Geo.Rect(-50, -1.5, 150, 1.5)])), 3);   // clear of the wall
        }
        Assert.Contains(k.Parts, p => p.Name == "Rubble in front layer 1" || p.Name == "Rubble in front");
    }

    [Fact]
    public void A_wall_with_a_whole_end_gets_no_base()
    {
        var k = Make(Wall(new RuinSpec { LeftEnd = EndKind.Whole, RightEnd = EndKind.Broken, Base = true }));
        Assert.DoesNotContain(k.Parts, p => p.Name == "Base");
        Assert.Contains(k.Findings, f => f.Level == Severity.Warn && f.Text.Contains("base"));
        Assert.Equal(0, Box(Part(k, "Broken wall").Cut).top, 3);
    }

    [Theory]
    [InlineData(PileShape.Heap, "Brick")]
    [InlineData(PileShape.AgainstWall, "Stone blocks")]
    [InlineData(PileShape.Corner, "Fieldstone")]
    [InlineData(PileShape.Heap, "Wood planks (across)")]
    [InlineData(PileShape.AgainstWall, "Plain")]
    public void Rubble_piles_stack_each_layer_on_the_one_below(PileShape shape, string texture)
    {
        var p = Textures.Preset(texture)!;
        var s = new RuinSpec { Wall = false, Texture = texture, SizeA = p.SizeA > 0 ? p.SizeA : 8, SizeB = p.SizeB > 0 ? p.SizeB : 3.5, GrooveMm = p.Groove > 0 ? p.Groove : 0.6,
            Piles = 1, PileShape = shape, PileWidthMm = 50, PileDepthMm = 36, PileHeightMm = 12, ThicknessMm = 3, LooseStones = 0, Chunks = 0 };
        var k = Make(s);
        Assert.True(k.Ok, Bad(k));
        var layers = k.Parts.Where(x => x.Name.StartsWith("Rubble pile")).ToList();
        Assert.InRange(layers.Count, 2, 4);
        for (int i = 0; i < layers.Count; i++)
        {
            Assert.Equal($"Rubble pile layer {i + 1}", layers[i].Name);
            Assert.Equal(1, Geo.Pieces(layers[i].Cut));
            Assert.True(Rough.Thin(layers[i].Cut, 1.3) < 1.5, $"layer {i + 1}: {Rough.Thin(layers[i].Cut, 1.3):0.##} mm² thin");
            if (i > 0)
            {
                Assert.Equal(0, Geo.Area(Geo.Subtract(layers[i].Cut, layers[i - 1].Cut)), 2);
                Assert.True(Geo.Area(layers[i].Cut) < Geo.Area(layers[i - 1].Cut));
            }
        }
        var bottom = Box(layers[0].Cut);
        Assert.InRange(bottom.right - bottom.left, shape == PileShape.Corner ? 25 : 35, shape == PileShape.Corner ? 60 : 60);
        if (shape is PileShape.AgainstWall or PileShape.Corner)
        {
            // A straight back: it fills y ≥ 0 right up to y = 0.
            Assert.Equal(0, bottom.top, 3);
            Assert.True(Geo.Area(Geo.Intersect(layers[0].Cut, [Geo.Rect(-100, 0, 100, 1)])) > 15);
        }
        if (shape == PileShape.Corner) Assert.Equal(0, bottom.left, 3);
        // Marked: the next layer's outline scored on each but the top one.
        Assert.All(layers.SkipLast(1), l => Assert.NotEmpty(l.Lines));
        Assert.Single(k.Views);
    }

    [Fact]
    public void Loose_bricks_are_the_texture_s_own_bricks_counted_together()
    {
        var k = Make(new RuinSpec { Wall = false, Texture = "Brick", SizeA = 8, SizeB = 3.5, GrooveMm = 0.6, LooseStones = 10, Chunks = 0 });
        var brick = Part(k, "Loose brick");
        Assert.Equal(10, brick.Copies);
        var b = Box(brick.Cut);
        Assert.Equal(7.4, b.right - b.left, 3);
        Assert.Equal(2.9, b.bottom - b.top, 3);
        var stones = Make(new RuinSpec { Wall = false, Texture = "Fieldstone", SizeA = 9, SizeB = 6, GrooveMm = 0.9, LooseStones = 6, Chunks = 0 });
        Assert.Equal(6, stones.Parts.Where(p => p.Name.StartsWith("Loose stone")).Sum(p => p.Copies));
    }

    [Fact]
    public void Fallen_chunks_carry_the_texture()
    {
        var k = Make(new RuinSpec { Wall = false, Texture = "Stone blocks", SizeA = 12, SizeB = 6, GrooveMm = 0.8, LooseStones = 0, Chunks = 3 });
        var chunks = k.Parts.Where(p => p.Name.StartsWith("Fallen chunk")).ToList();
        Assert.Equal(3, chunks.Count);
        Assert.All(chunks, c =>
        {
            Assert.Equal(1, Geo.Pieces(c.Cut));
            Assert.True(Geo.Area(c.Etched) > 5);
            Assert.Equal(0, Geo.Area(Geo.Subtract(c.Etched, c.Cut)), 2);
        });
    }

    [Theory]
    [InlineData(PillarShape.Round)]
    [InlineData(PillarShape.Square)]
    [InlineData(PillarShape.Octagon)]
    public void Broken_pillars_are_whole_below_and_broken_above(PillarShape shape)
    {
        var s = new RuinSpec { Wall = false, LooseStones = 0, Chunks = 0, Pillars = 2, PillarShape = shape, PillarMm = 14, PillarHeightMm = 30, ThicknessMm = 3, PinMm = 2, Plinth = true };
        var k = Make(s);
        Assert.True(k.Ok, Bad(k));
        // 10 layers: 3 broken, 7 whole, for each pillar.
        Assert.Equal(14, Part(k, "Pillar layer").Copies);
        Assert.Equal(2, Part(k, "Pillar plinth").Copies);
        var layer = Part(k, "Pillar layer");
        Assert.Equal(2, Geo.Tidy(layer.Cut).Count);                         // its pin hole
        Assert.Equal(14, Box(layer.Cut).right - Box(layer.Cut).left, 1);
        foreach (int p in new[] { 1, 2 })
        {
            var broken = k.Parts.Where(x => x.Name.StartsWith($"Pillar {p} broken layer")).ToList();
            Assert.InRange(broken.Count, 1, 3);
            var below = Rough.FillHoles(layer.Cut);
            foreach (var b in broken)
            {
                Assert.Equal(1, Geo.Pieces(b.Cut));
                Assert.Equal(0, Geo.Area(Geo.Subtract(Rough.FillHoles(b.Cut), below)), 2);
                Assert.True(Geo.Area(b.Cut) < Geo.Area(Rough.FillHoles(layer.Cut)));
                below = Rough.FillHoles(b.Cut);
            }
        }
        Assert.Equal(2, k.Views.Count);
    }

    [Fact]
    public void Nothing_asked_for_is_reported()
    {
        var k = Make(new RuinSpec { Wall = false, LooseStones = 0, Chunks = 0 });
        Assert.False(k.Ok);
        Assert.Empty(k.Parts);
    }

    [Fact]
    public void Settings_round_trip()
    {
        var s = new RuinSpec { Texture = "Fieldstone", Break = BreakShape.TwoGaps, LeftEnd = EndKind.Broken, Piles = 3, PileShape = PileShape.Corner, PillarShape = PillarShape.Octagon };
        var back = RuinSpec.FromJson(s.ToJson())!;
        Assert.Equal(s.ToJson(), back.ToJson());
        Assert.Contains("\"TwoGaps\"", s.ToJson());
        Assert.Null(RuinSpec.FromJson("{not json"));
    }

    [Fact]
    public void Matching_walls_takes_their_sheet_texture_and_variation_so_the_courses_line_up()
    {
        var walls = new WallsSpec { HeightMm = 60, ThicknessMm = 4, TwoSided = true, Texture = "Stone blocks", SizeA = 11, SizeB = 5.5, GrooveMm = 0.7, Seed = 42, SlotDepthMm = 5, FitMm = 0.25, Etch = PhotonCanvas.Addon.Walls.EtchStyle.Lines };
        var m = WallsLink.Match(new RuinSpec(), walls.ToJson())!;
        Assert.Equal(60, m.HeightMm);
        Assert.Equal(4, m.ThicknessMm);
        Assert.True(m.TwoSided);
        Assert.Equal("Stone blocks", m.Texture);
        Assert.Equal(11, m.SizeA);
        Assert.Equal(5.5, m.SizeB);
        Assert.Equal(0.7, m.GrooveMm);
        Assert.Equal(42, m.Seed);
        Assert.Equal(5, m.SlotDepthMm);
        Assert.Equal(0.25, m.FitMm);
        Assert.Equal(EtchStyle.Lines, m.Etch);
        Assert.Null(WallsLink.Match(new RuinSpec(), "[1, 2]"));

        // The same stones as the wall maker lays on a wall of the same length.
        var ours = Textures.Make(m.TextureSpec, new Face(100, 60, 5.25, 94.75, 60));
        var theirs = WallsTextures.Make(walls with { LengthMm = 100 }, new WallsFace(100, 60, 5.25, 94.75, 60));
        Assert.Equal(theirs.Raised.Count, ours.Raised.Count);
        for (int i = 0; i < ours.Raised.Count; i++)
            Assert.Equal(theirs.Raised[i].Select(q => (q.x, q.y)), ours.Raised[i].Select(q => (q.x, q.y)));
    }

    [Fact]
    public void A_custom_texture_comes_with_matched_walls()
    {
        var custom = new PhotonCanvas.Addon.Walls.CustomTexture { Name = "Skulls", Path = "M0 0 L4 0 L4 4 Z", TileWidthMm = 5, TileHeightMm = 5 };
        var walls = new WallsSpec { Texture = "Skulls", Custom = custom, SizeA = 5 };
        var m = WallsLink.Match(new RuinSpec(), walls.ToJson())!;
        Assert.Equal("Skulls", m.Texture);
        Assert.Equal("M0 0 L4 0 L4 4 Z", m.Custom!.Path);
        var k = Make(Wall(m));
        Assert.True(k.Ok, Bad(k));
        Assert.True(Geo.Area(Part(k, "Broken wall").Etched) > 10);
    }

    [Fact]
    public void Placing_puts_every_piece_on_its_layers_and_can_replace_it()
    {
        var machine = MachineProfile.F2Ultra;
        var spec = new RuinSpec { Breaches = 1, Piles = 1, LooseStones = 5, Chunks = 1, Pillars = 1 };
        var kit = Make(spec);
        Assert.True(kit.Ok, Bad(kit));
        var (s, sheets, id) = Placement.Place(DocState.New(), kit, spec, machine, new PlaceOptions());
        var all = Enumerable.Range(0, sheets).SelectMany(i => Pages.SwitchTo(s, i).Shapes).ToList();
        Assert.Equal(kit.PieceCount, all.Count(x => x.AddonData.ContainsKey(Placement.SpecKey)));
        Assert.Contains(s.Layers, l => l.Name == Placement.EtchLayer && l.Mode == LayerMode.Fill);
        Assert.Contains(s.Layers, l => l.Name == Placement.LineLayer && l.Mode == LayerMode.Score);
        Assert.True(s.Layers.FindIndex(l => l.Name == Placement.EtchLayer) < s.Layers.FindIndex(l => l.Name == Placement.CutLayer));
        Assert.True(s.Layers.FindIndex(l => l.Name == Placement.LineLayer) < s.Layers.FindIndex(l => l.Name == Placement.CutLayer));
        Assert.All(s.Shapes, x => Assert.True(x.Bounds.MinX >= 0 && x.Bounds.MaxX <= machine.FieldW && x.Bounds.MinY >= 0 && x.Bounds.MaxY <= machine.FieldH));
        Assert.Equal(spec.ToJson(), Placement.Find(s, [s.Shapes[0].Id])!.Value.Spec.ToJson());

        var changed = spec with { Piles = 0, Pillars = 0, Texture = "Tiles" };
        var (s2, _, id2) = Placement.Place(s, Make(changed), changed, machine, new PlaceOptions { Replace = id });
        Assert.Equal(id, id2);
        Assert.DoesNotContain(s2.Shapes, x => x.Name.StartsWith("Rubble") || x.Name.StartsWith("Pillar"));
        Assert.Equal("Tiles", Placement.Find(s2, [s2.Shapes[0].Id])!.Value.Spec.Texture);
    }
}
