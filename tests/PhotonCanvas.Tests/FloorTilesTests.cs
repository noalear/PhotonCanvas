using Clipper2Lib;
using PhotonCanvas.Addon.FloorTiles;
using PhotonCanvas.Addon.Walls;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;

namespace PhotonCanvas.Tests;

public class FloorTilesTests
{
    const double G = 25.4;

    static TileKit Make(TileSpec s) => TileKitMaker.Make(s);
    static string Why(TileKit k) => string.Join("\n", k.Findings);
    static TileSpec Loose(double across, double down, TileJoin join = TileJoin.Jigsaw, PlainSides plain = PlainSides.None) =>
        new() { Mode = TileMode.Tiles, Tiles = [new TileSize { Across = across, Down = down, Count = 1, Plain = plain }], Join = join, Texture = FloorTextures.Plain };

    static PathsD Turned(PathsD a, int quarter, double w, double h)
    {
        // Turned about the tile's middle by quarter × 90°, then put back with its corner at 0, 0.
        var t = Geo.Turn(Geo.Move(a, -w / 2, -h / 2), 90 * quarter);
        double nw = quarter % 2 == 0 ? w : h, nh = quarter % 2 == 0 ? h : w;
        return Geo.Move(t, nw / 2, nh / 2);
    }

    [Fact]
    public void A_board_is_split_into_tiles_with_straight_edges_round_the_outside()
    {
        var k = Make(new TileSpec { BoardAcross = 6, BoardDown = 6, TileAcross = 2, TileDown = 2, Texture = FloorTextures.Plain });
        Assert.True(k.Ok, Why(k));
        Assert.Equal(9, k.Board.Count);
        Assert.Equal(9, k.Tiles.Count());
        Assert.Contains(k.Findings, f => f.Text.Contains("4 corners, 4 edges, 1 middle"));
        // The middle tile has tabs on all four sides; a corner tile is square on two.
        var middle = k.Board.Single(b => b.Plain == Sides.None).Tile;
        var mb = Geo.Bounds(middle.Cut);
        Assert.True(mb.left < -5 && mb.bottom > 2 * G + 5 && mb.top < -5 && mb.right > 2 * G + 5);
        var corner = k.Board.Single(b => b.X == 0 && b.Y == 0);
        Assert.Equal(Sides.Left | Sides.Bottom, corner.Plain);
        var cb = Geo.Bounds(corner.Tile.Cut);
        Assert.Equal(0, cb.left, 6);
        Assert.Equal(0, cb.top, 6);
        Assert.Equal("Tile A3", corner.Tile.Name);
    }

    [Fact]
    public void Tiles_on_the_board_fit_together_without_overlapping_and_fill_it()
    {
        var k = Make(new TileSpec { BoardAcross = 4, BoardDown = 4, Texture = FloorTextures.Plain, FitMm = 0.2 });
        Assert.True(k.Ok, Why(k));
        var placed = k.Board.Select(b => Geo.Move(b.Tile.Cut, b.X, b.Y)).ToList();
        for (int i = 0; i < placed.Count; i++)
            for (int j = i + 1; j < placed.Count; j++)
                Assert.True(Geo.Area(Geo.Intersect(placed[i], placed[j])) < 1e-3, $"{k.Board[i].Tile.Name} overlaps {k.Board[j].Tile.Name}");
        // Together they cover the board but for the play round the tabs.
        var all = Geo.Union(new PathsD(placed.SelectMany(p => p)));
        double board = 4 * G * 4 * G;
        Assert.InRange(Geo.Area(all), board - 60, board + 1e-3);
        Assert.Equal(0, Geo.Area(Geo.Subtract(all, [Geo.Rect(0, 0, 4 * G, 4 * G)])), 3);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Any_tile_fits_any_other_turned_any_way(int quarter)
    {
        var a = Make(Loose(2, 1)).Tiles.Single().Cut;
        var b = Turned(Make(Loose(2, 1)).Tiles.Single().Cut, quarter, 2 * G, G);
        // b beside a's right edge (b is 2 × 1 or, turned, 1 × 2: line their bottoms up on the grid).
        var beside = Geo.Move(b, 2 * G, 0);
        Assert.True(Geo.Area(Geo.Intersect(a, beside)) < 1e-3, "they overlap");
        // a's tab reaches into b's ground, and b's into a's: they lock.
        Assert.True(Geo.Area(Geo.Intersect(a, [Geo.Rect(2 * G, 0, 2 * G + 10, G)])) > 5);
        Assert.True(Geo.Area(Geo.Intersect(beside, [Geo.Rect(2 * G - 10, 0, 2 * G, G)])) > 5);
        // And on top.
        var above = Geo.Move(b, quarter % 2 == 0 ? 0 : 0, G);
        Assert.True(Geo.Area(Geo.Intersect(a, above)) < 1e-3, "they overlap above");
    }

    [Fact]
    public void A_part_square_board_has_narrower_far_tiles()
    {
        var k = Make(new TileSpec { BoardAcross = 5, BoardDown = 4.5, Texture = FloorTextures.Plain });
        Assert.True(k.Ok, Why(k));
        Assert.Equal(9, k.Board.Count);
        Assert.Equal(new[] { 2.0, 2, 1 }.Select(v => v * G), k.Board.Where(b => b.Y == 0).OrderBy(b => b.X).Select(b => b.W), new Tol(1e-6));
        Assert.Equal(0.5 * G, k.Board.Max(b => b.Y + b.H) - k.Board.Max(b => b.Y), 6);
        Assert.Contains(k.Findings, f => f.Text.Contains("isn't a whole number"));
        // Still no overlaps where the part squares meet.
        var placed = k.Board.Select(b => Geo.Move(b.Tile.Cut, b.X, b.Y)).ToList();
        for (int i = 0; i < placed.Count; i++)
            for (int j = i + 1; j < placed.Count; j++)
                Assert.True(Geo.Area(Geo.Intersect(placed[i], placed[j])) < 1e-3);
    }

    sealed class Tol(double t) : IEqualityComparer<double>
    {
        public bool Equals(double a, double b) => Math.Abs(a - b) <= t;
        public int GetHashCode(double v) => 0;
    }

    [Fact]
    public void Tabs_too_big_for_the_squares_are_refused()
    {
        var k = Make(Loose(1, 1) with { TabMm = 12 });
        Assert.False(k.Ok);
        Assert.Contains(k.Findings, f => f.Level == Severity.Bad && f.Text.Contains("too big"));
        Assert.True(Make(Loose(1, 1) with { SquareMm = 20, TabMm = 4 }).Ok);
    }

    [Fact]
    public void Clips_join_tiles_through_sockets_in_the_base_layer()
    {
        var k = Make(new TileSpec { BoardAcross = 4, BoardDown = 2, Join = TileJoin.Clips, BaseLayer = true, Texture = FloorTextures.Plain });
        Assert.True(k.Ok, Why(k));
        // Two tiles side by side: two squares of edge between them, a clip each.
        Assert.Equal(2, k.Clips);
        var tile = k.Board[0].Tile;
        Assert.Equal(2 * G * 2 * G, Geo.Area(tile.Cut), 3);   // the top is a plain square
        var baseLayer = k.Parts.Single(p => p.Name == tile.Name + TileKitMaker.BaseSuffix);
        Assert.True(Geo.Area(baseLayer.Cut) < 2 * G * 2 * G - 30);
        // A clip across the joint fits in the two sockets that meet there.
        var clip = k.Parts.Single(p => p.Name == TileKitMaker.ClipName);
        Assert.Equal(2, clip.Copies);
        var right = k.Board.Single(b => b.X > 0);
        var bases = Geo.Union(baseLayer.Cut, Geo.Move(k.Parts.Single(p => p.Name == right.Tile.Name + TileKitMaker.BaseSuffix).Cut, right.X, right.Y));
        var placed = Geo.Move(clip.Cut, 2 * G, G / 2);
        Assert.True(Geo.Area(Geo.Intersect(placed, bases)) < 1e-3, "the clip doesn't fit");
    }

    [Fact]
    public void With_a_base_layer_the_jigsaw_is_only_in_the_base()
    {
        var k = Make(new TileSpec { BoardAcross = 6, BoardDown = 6, Join = TileJoin.Jigsaw, BaseLayer = true, Texture = FloorTextures.Plain });
        Assert.True(k.Ok, Why(k));
        Assert.Contains(k.Findings, f => f.Text.Contains("base layer only"));
        foreach (var spot in k.Board)
        {
            // The top is a plain rectangle, exactly its squares.
            var tb = Geo.Bounds(spot.Tile.Cut);
            Assert.Equal(0, tb.left, 6); Assert.Equal(0, tb.top, 6);
            Assert.Equal(spot.W, tb.right, 6); Assert.Equal(spot.H, tb.bottom, 6);
            Assert.Equal(spot.W * spot.H, Geo.Area(spot.Tile.Cut), 3);
        }
        // The middle tile's base has the tabs and sockets the top no longer has.
        var middle = k.Board.Single(b => b.Plain == Sides.None);
        var mbase = k.Parts.Single(p => p.Name == middle.Tile.Name + TileKitMaker.BaseSuffix);
        var bb = Geo.Bounds(mbase.Cut);
        Assert.True(bb.left < -5 && bb.right > middle.W + 5 && bb.top < -5 && bb.bottom > middle.H + 5);
        Assert.True(Geo.Area(Geo.Subtract([Geo.Rect(0, 0, middle.W, middle.H)], mbase.Cut)) > 30, "no sockets in the base");
        // And the bases lock together on the board without overlapping.
        var bases = k.Board.Select(b => Geo.Move(k.Parts.Single(p => p.Name == b.Tile.Name + TileKitMaker.BaseSuffix).Cut, b.X, b.Y)).ToList();
        for (int i = 0; i < bases.Count; i++)
            for (int j = i + 1; j < bases.Count; j++)
                Assert.True(Geo.Area(Geo.Intersect(bases[i], bases[j])) < 1e-3, $"bases {i} and {j} overlap");
        // Without a base layer the top has the jigsaw itself.
        var plain = Make(new TileSpec { BoardAcross = 6, BoardDown = 6, Join = TileJoin.Jigsaw, Texture = FloorTextures.Plain });
        Assert.DoesNotContain(plain.Parts, p => p.Name.EndsWith(TileKitMaker.BaseSuffix));
        var pm = Geo.Bounds(plain.Board.Single(b => b.Plain == Sides.None).Tile.Cut);
        Assert.True(pm.left < -5);
    }

    [Theory]
    [InlineData(TileJoin.Jigsaw)]
    [InlineData(TileJoin.Clips)]
    [InlineData(TileJoin.Magnets)]
    public void Base_layer_tops_are_plain_and_hold_the_whole_texture(TileJoin join)
    {
        var k = Make(new TileSpec { BoardAcross = 2, BoardDown = 2, Join = join, BaseLayer = true, Texture = "Dungeon flagstones" });
        Assert.True(k.Ok, Why(k));
        var tile = k.Board.Single().Tile;
        Assert.Equal(2 * G * 2 * G, Geo.Area(tile.Cut), 3);
        Assert.True(Geo.Area(Geo.Subtract(tile.Etched, tile.Cut)) < 1e-3);
    }

    [Fact]
    public void Boards_with_joints_round_the_outside_lock_onto_each_other()
    {
        var spec = new TileSpec { BoardAcross = 4, BoardDown = 4, TileAcross = 2, TileDown = 2, PlainBorder = false, Texture = FloorTextures.Plain };
        var k = Make(spec);
        Assert.True(k.Ok, Why(k));
        Assert.All(k.Board, b => Assert.Equal(Sides.None, b.Plain));
        Assert.Contains(k.Findings, f => f.Text.Contains("outside edges have joints"));
        // A corner tile now has tabs out past the board's edge.
        var corner = Geo.Bounds(k.Board.Single(b => b.X == 0 && b.Y == 0).Tile.Cut);
        Assert.True(corner.left < -5 && corner.top < -5);

        PathsD Board(double dx, double dy) => Geo.Union(new PathsD(k.Board.SelectMany(b => Geo.Move(b.Tile.Cut, b.X + dx, b.Y + dy))));
        var a = Board(0, 0);
        foreach (var (dx, dy) in new[] { (4 * G, 0.0), (0.0, 4 * G) })
        {
            // A second board beside or above it: they don't overlap and its tabs reach into the first one's sockets.
            var b = Board(dx, dy);
            Assert.True(Geo.Area(Geo.Intersect(a, b)) < 1e-3, "the boards overlap");
            var past = dx > 0 ? Geo.Rect(-G, -G, 4 * G, 5 * G) : Geo.Rect(-G, -G, 5 * G, 4 * G);
            Assert.True(Geo.Area(Geo.Intersect(b, [past])) > 10, "no tabs reach across");
        }
        // Turned round, a board still fits against another.
        var turned = Geo.Move(Turned(a, 2, 4 * G, 4 * G), 4 * G, 0);
        Assert.True(Geo.Area(Geo.Intersect(a, turned)) < 1e-3, "a turned board overlaps");

        // Straight round the outside (the default), the corner is square there.
        var straight = Make(spec with { PlainBorder = true });
        var sc = Geo.Bounds(straight.Board.Single(b => b.X == 0 && b.Y == 0).Tile.Cut);
        Assert.Equal(0, sc.left, 6);
        Assert.DoesNotContain(straight.Findings, f => f.Text.Contains("outside edges have joints"));
    }

    [Fact]
    public void Clips_without_a_base_are_cut_through_the_tile()
    {
        var k = Make(Loose(1, 1, TileJoin.Clips));
        Assert.True(k.Ok, Why(k));
        Assert.True(Geo.Area(k.Tiles.Single().Cut) < G * G - 30);
        Assert.Equal(2, k.Clips);   // four sockets
        Assert.Contains(k.Findings, f => f.Text.Contains("show on top"));
    }

    [Fact]
    public void Magnets_go_in_a_base_layer_two_to_a_square_of_edge_clear_of_each_other()
    {
        var k = Make(Loose(2, 2, TileJoin.Magnets));
        Assert.True(k.Ok, Why(k));
        Assert.Equal(16, k.Magnets);
        var b = k.Parts.Single(p => p.Name.EndsWith(TileKitMaker.BaseSuffix));
        // The base's outline and 16 round holes, none touching another or the edge.
        var holes = Geo.Tidy(b.Cut).Where(p => Clipper.Area(p) < 0).ToList();
        Assert.Equal(16, holes.Count);
        Assert.Equal(8, b.Lines.Count);   // the north-up rings
        var grown = holes.Select(h => Geo.Offset([h], 0.5)).ToList();
        for (int i = 0; i < grown.Count; i++)
            for (int j = i + 1; j < grown.Count; j++)
                Assert.True(Geo.Area(Geo.Intersect(grown[i], grown[j])) < 1e-6);
        Assert.False(Make(Loose(1, 1, TileJoin.Magnets) with { MagnetMm = 10 }).Ok);
    }

    [Fact]
    public void Magnets_meet_north_to_south()
    {
        // The ringed (north up) hole on one tile's edge is beside an unringed (south up) one on the next.
        var k = Make(new TileSpec { BoardAcross = 2, BoardDown = 1, TileAcross = 1, TileDown = 1, Join = TileJoin.Magnets, Texture = FloorTextures.Plain });
        Assert.True(k.Ok, Why(k));
        var left = k.Parts.Single(p => p.Name == "Tile A1" + TileKitMaker.BaseSuffix);
        var right = k.Parts.Single(p => p.Name == "Tile B1" + TileKitMaker.BaseSuffix);
        double RingY(Part p, double x) => p.Lines.Select(l => Geo.Bounds([l])).Where(b => Math.Abs((b.left + b.right) / 2 - x) < 5).Select(b => (b.top + b.bottom) / 2).Single();
        Assert.Single(left.Lines, l => Geo.Bounds([l]).right > G - 6);
        double yl = RingY(left, G - 4), yr = RingY(right, 4);
        Assert.True(Math.Abs(yl - yr) > 5, "both north up at the same place");
    }

    public static TheoryData<string> Presets() => [.. FloorTextures.Presets.Where(p => p.Name != FloorTextures.Plain).Select(p => p.Name)];

    [Theory]
    [MemberData(nameof(Presets))]
    public void Every_texture_is_etched_inside_the_tile(string texture)
    {
        var p = FloorTextures.Preset(texture)!;
        var (a, b) = FloorTextures.SizesFor(p, G);
        var s = Loose(2, 2) with { Texture = texture, SizeA = a, SizeB = b, GrooveMm = p.Groove };
        var k = Make(s);
        Assert.True(k.Ok, Why(k));
        var tile = k.Tiles.Single();
        double etched = Geo.Area(tile.Etched), area = Geo.Area(tile.Cut);
        Assert.True(etched > 30 || tile.Lines.Count > 20, $"{texture}: only {etched:0.#} mm² etched");
        Assert.True(etched < area * 0.85, $"{texture}: nearly all of it etched");
        Assert.Equal(0, Geo.Area(Geo.Subtract(tile.Etched, tile.Cut)), 2);
        // Tabs are textured too: something is etched or scored out past the edges.
        bool onTabs = Geo.Area(Geo.Subtract(tile.Etched, [Geo.Rect(0, 0, 2 * G, 2 * G)])) > 0.5
            || tile.Lines.SelectMany(l => l).Any(q => q.x < -0.5 || q.y < -0.5 || q.x > 2 * G + 0.5 || q.y > 2 * G + 0.5);
        Assert.True(onTabs, $"{texture}: nothing on the tabs");
    }

    [Fact]
    public void Dungeon_flagstones_keep_a_groove_on_every_grid_line()
    {
        var k = Make(Loose(2, 2) with { Texture = "Dungeon flagstones", SizeA = 100, CracksPct = 0 });
        var tile = k.Tiles.Single();
        // Along the line between the squares, the groove runs right across (a thin strip there is all etched).
        var strip = new PathsD { Geo.Rect(G - 0.1, 1, G + 0.1, 2 * G - 1) };
        Assert.Equal(Geo.Area(strip), Geo.Area(Geo.Intersect(tile.Etched, strip)), 2);
        var across = new PathsD { Geo.Rect(1, G - 0.1, 2 * G - 1, G + 0.1) };
        Assert.Equal(Geo.Area(across), Geo.Area(Geo.Intersect(tile.Etched, across)), 2);
    }

    [Fact]
    public void Herringbone_bricks_tile_the_floor_without_gaps_or_overlaps()
    {
        var bricks = FloorTextures.HerringboneBricks(0, 0, 40, 40, 2.5);
        double sum = bricks.Sum(b => Math.Abs(Clipper.Area(b)));
        var union = Geo.Union(bricks);
        Assert.Equal(sum, Geo.Area(union), 3);   // no overlaps
        var box = new PathsD { Geo.Rect(0, 0, 40, 40) };
        Assert.Equal(1600, Geo.Area(Geo.Intersect(union, box)), 3);   // no gaps
    }

    [Fact]
    public void Lines_style_and_grid_marks()
    {
        var lines = Make(Loose(2, 2) with { Texture = "Square tiles", SizeA = 12.7, Etch = EtchStyle.Lines }).Tiles.Single();
        Assert.Empty(lines.Etched);
        Assert.True(lines.Lines.Count > 10);
        var scored = Make(Loose(3, 2) with { Grid = GridMarks.Scored }).Tiles.Single();
        Assert.Equal(3, scored.Lines.Count);   // two across, one down
        var grooved = Make(Loose(3, 2) with { Grid = GridMarks.Grooved, GrooveMm = 0.8 }).Tiles.Single();
        Assert.InRange(Geo.Area(grooved.Etched), 0.8 * (2 * 2 * G + 3 * G) - 5, 0.8 * (2 * 2 * G + 3 * G) + 10);
    }

    [Fact]
    public void Each_tile_gets_its_own_layout_and_the_same_seed_gives_the_same()
    {
        var s = new TileSpec { BoardAcross = 4, BoardDown = 2, Texture = "Flagstones", SizeA = 10 };
        var k = Make(s);
        Assert.NotEqual(Geo.Area(k.Board[0].Tile.Etched), Geo.Area(k.Board[1].Tile.Etched), 3);
        Assert.Equal(Geo.Area(k.Board[0].Tile.Etched), Geo.Area(Make(s).Board[0].Tile.Etched), 6);
        Assert.NotEqual(Geo.Area(k.Board[0].Tile.Etched), Geo.Area(Make(s with { Seed = 2 }).Board[0].Tile.Etched), 3);
    }

    [Fact]
    public void Loose_tiles_come_in_the_numbers_and_kinds_listed()
    {
        var k = Make(new TileSpec
        {
            Mode = TileMode.Tiles,
            Texture = FloorTextures.Plain,
            Tiles = [new() { Across = 2, Down = 2, Count = 3 }, new() { Across = 1, Down = 2, Count = 2, Plain = PlainSides.One }, new() { Across = 1, Down = 1, Count = 1, Plain = PlainSides.Corner }],
        });
        Assert.True(k.Ok, Why(k));
        Assert.Equal(6, k.Tiles.Count());
        Assert.Contains(k.Tiles, t => t.Name == "2 × 2 tile (1 of 3)");
        Assert.Contains(k.Tiles, t => t.Name == "1 × 2 edge tile (2 of 2)");
        var corner = Geo.Bounds(k.Tiles.Single(t => t.Name == "1 × 1 corner tile").Cut);
        Assert.Equal(0, corner.left, 6);
        Assert.Equal(0, corner.top, 6);
        Assert.False(Make(new TileSpec { BoardAcross = 40, BoardDown = 40, TileAcross = 1, TileDown = 1 }).Ok);
    }

    [Fact]
    public void Settings_survive_being_saved()
    {
        var s = new TileSpec { Mode = TileMode.Tiles, Tiles = [new() { Across = 3, Down = 1, Count = 5, Plain = PlainSides.Opposite }], Join = TileJoin.Magnets, Grid = GridMarks.Scored, Texture = "Cobblestones" };
        var back = TileSpec.FromJson(s.ToJson())!;
        Assert.Equal(s.ToJson(), back.ToJson());
        Assert.Equal(PlainSides.Opposite, back.Tiles[0].Plain);
        Assert.True(back.HasBase);
        Assert.Null(TileSpec.FromJson("{nope"));
    }

    [Fact]
    public void The_tiles_go_into_the_design_on_their_layers_and_can_be_replaced()
    {
        var machine = MachineProfile.F2Ultra;
        var spec = new TileSpec { BoardAcross = 4, BoardDown = 4, Texture = "Dungeon flagstones", Grid = GridMarks.Scored, Join = TileJoin.Clips, BaseLayer = true };
        var kit = Make(spec);
        Assert.True(kit.Ok, Why(kit));
        var (s, sheets, id) = TilePlacement.Place(DocState.New(), kit, spec, machine, new TilePlaceOptions());
        int OnCut(DocState d, int n) => Enumerable.Range(0, n).SelectMany(i => Pages.SwitchTo(d, i).Shapes).Count(x => x.AddonData.ContainsKey(TilePlacement.SpecKey));
        Assert.Equal(kit.PieceCount, OnCut(s, sheets));
        Assert.All(s.Shapes, x => Assert.True(x.Bounds.MinX >= 0 && x.Bounds.MaxX <= machine.FieldW && x.Bounds.MinY >= 0 && x.Bounds.MaxY <= machine.FieldH));
        Assert.True(s.Layers.FindIndex(l => l.Name == TilePlacement.EtchLayer) < s.Layers.FindIndex(l => l.Name == TilePlacement.CutLayer));
        Assert.True(s.Layers.FindIndex(l => l.Name == TilePlacement.LineLayer) < s.Layers.FindIndex(l => l.Name == TilePlacement.CutLayer));
        Assert.Equal(spec.ToJson(), TilePlacement.Find(s, [s.Shapes[0].Id])!.Value.Spec.ToJson());

        var changed = spec with { Texture = "Cobblestones", SizeA = 4.5, BoardAcross = 2 };
        var (s2, sheets2, id2) = TilePlacement.Place(s, Make(changed), changed, machine, new TilePlaceOptions { Replace = id });
        Assert.Equal(id, id2);
        Assert.Equal("Cobblestones", TilePlacement.Find(s2, [s2.Shapes[0].Id])!.Value.Spec.Texture);
        Assert.Equal(Make(changed).PieceCount, OnCut(s2, s2.OtherPages.Count + 1));
    }

    [Fact]
    public void A_tile_too_big_for_the_bed_is_reported()
    {
        var spec = Loose(12, 2);
        var kit = Make(spec);
        Assert.Single(TilePlacement.TooBig(kit, MachineProfile.F2Ultra with { FieldW = 200, FieldH = 200 }));
    }

    [Fact]
    public void Relief_carves_each_tile_as_a_depth_map()
    {
        var spec = new TileSpec { BoardAcross = 2, BoardDown = 2, Texture = "Dungeon flagstones", Relief = true };
        var kit = Make(spec);
        Assert.True(kit.Ok, Why(kit));
        var (s, _, _) = TilePlacement.Place(DocState.New(), kit, spec, MachineProfile.F2Ultra, new TilePlaceOptions());
        Assert.Contains(s.Layers, l => l.Name == TilePlacement.ReliefLayer && l.Mode == LayerMode.Image && l.ImageMode == ImageEngraveMode.Slices);
        Assert.DoesNotContain(s.Layers, l => l.Name == TilePlacement.EtchLayer);
        var pics = s.Shapes.OfType<ImageShape>().ToList();
        Assert.NotEmpty(pics);
        Assert.All(pics, p => Assert.NotNull(p.GroupId));
        var (dark, light, greys, _) = WallsTests.Tones(pics[0]);
        Assert.True(dark > 0.03 && light > 0.3 && greys > 10, $"{dark:P0} deep, {light:P0} near the surface, {greys} heights");
    }
}
