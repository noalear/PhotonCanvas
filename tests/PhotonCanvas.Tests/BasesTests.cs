using Clipper2Lib;
using PhotonCanvas.Addon.Bases;
using PhotonCanvas.Addon.FloorTiles;
using PhotonCanvas.Addon.Walls;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;

namespace PhotonCanvas.Tests;

public class BasesTests
{
    static string Why(BaseKit k) => string.Join("\n", k.Findings);
    static BaseSpec One(BaseShape shape, double w, double d = 0, int count = 1) =>
        new() { Bases = [new BaseSize { Shape = shape, WidthMm = w, DepthMm = d > 0 ? d : w, Count = count }], Texture = GroundTextures.Plain, RimMm = 0 };

    static (double W, double H) Size(PathsD a) { var b = Geo.Bounds(a); return (b.right - b.left, b.bottom - b.top); }

    [Theory]
    [InlineData(BaseShape.Round, 25, 25, 25, 25)]
    [InlineData(BaseShape.Oval, 60, 35, 60, 35)]
    [InlineData(BaseShape.Square, 40, 0, 40, 40)]
    [InlineData(BaseShape.Rectangle, 25, 50, 25, 50)]
    [InlineData(BaseShape.Hexagon, 30, 0, 34.641, 30)]
    public void Each_shape_comes_out_its_size(BaseShape shape, double w, double d, double bw, double bh)
    {
        var k = BaseKitMaker.Make(One(shape, w, d));
        Assert.True(k.Ok, Why(k));
        var top = k.Stacks.Single().Layers.Single();
        var (sw, sh) = Size(top.Cut);
        Assert.Equal(bw, sw, 1);
        Assert.Equal(bh, sh, 1);
        double area = shape switch
        {
            BaseShape.Round => Math.PI * w * w / 4,
            BaseShape.Oval => Math.PI * w * d / 4,
            BaseShape.Hexagon => Math.Sqrt(3) / 2 * w * w,
            _ => bw * bh - (4 - Math.PI) * 1 * 1,   // corners rounded off by 1 mm
        };
        Assert.Equal(area, Geo.Area(top.Cut), area * 0.003);
    }

    [Fact]
    public void Every_base_gets_its_own_texture_inside_the_rim()
    {
        var s = One(BaseShape.Round, 32, count: 3) with { Texture = "Cracked earth", SizeA = 4, GrooveMm = 0.5, RimMm = 1.5 };
        var k = BaseKitMaker.Make(s);
        Assert.True(k.Ok, Why(k));
        var tops = k.Parts.Where(p => p.Name.StartsWith("Round 32 mm base")).ToList();
        Assert.Equal(3, tops.Count);
        Assert.All(tops, t => Assert.True(Geo.Area(t.Etched) > 10));
        Assert.NotEqual(Geo.Area(tops[0].Etched), Geo.Area(tops[1].Etched), 3);
        // Nothing etched in the rim, and the rim's edge is scored.
        Assert.True(Geo.Area(Geo.Subtract(tops[0].Etched, [Geo.Circle(16, 16, 16 - 1.5 + 0.01)])) < 1e-3);
        Assert.Contains(tops[0].Lines, l => Math.Abs(Size([l]).W - 29) < 0.1);
    }

    [Theory]
    [MemberData(nameof(Textures))]
    public void Every_texture_makes_something_on_a_base(string texture)
    {
        var p = GroundTextures.Preset(texture)!;
        var s = One(BaseShape.Oval, 60, 35) with { Texture = texture, SizeA = p.SizeA, SizeB = p.SizeB, GrooveMm = p.UsesGroove ? p.Groove : 0.5 };
        foreach (var etch in new[] { EtchStyle.Grooves, EtchStyle.Lines })
        {
            var k = BaseKitMaker.Make(s with { Etch = etch });
            Assert.True(k.Ok, Why(k));
            var top = k.Stacks.Single().Layers[^1];
            bool any = Geo.Area(top.Etched) > 1 || top.Lines.Count > 3;
            Assert.Equal(texture != GroundTextures.Plain, any);
            // All of it on the base.
            Assert.True(Geo.Area(Geo.Subtract(top.Etched, top.Cut)) < 1e-3, texture);
        }
    }

    public static TheoryData<string> Textures()
    {
        var d = new TheoryData<string>();
        foreach (var p in GroundTextures.Presets) d.Add(p.Name);
        return d;
    }

    [Fact]
    public void Paving_textures_are_the_floor_tile_makers_own()
    {
        foreach (var p in GroundTextures.Presets.Where(p => p.Floor is not null))
            Assert.NotNull(FloorTextures.Preset(p.Floor!));
    }

    [Fact]
    public void Tread_plate_bars_lean_both_ways()
    {
        // Each bar grown from its line: bars leaning along the line used to vanish, leaving every other one.
        var t = FloorTextures.Make(new TileSpec { Texture = "Tread plate", SizeA = 4 }, 40, 40, 1);
        int n = t.Etched.Count(p => Clipper.Area(p) > 0.1);
        Assert.InRange(n, 500, 620);   // (25.4 + 40 + 25.4) / 4 squared, near enough; half that with the old bars
    }

    [Fact]
    public void Layers_step_in_and_the_magnet_and_slot_go_where_they_should()
    {
        var s = One(BaseShape.Round, 40) with { Layers = 3, StepMm = 1, MagnetMm = 6, Fixing = FigureFixing.Slot, SlotLengthMm = 20, SlotWidthMm = 2 };
        var k = BaseKitMaker.Make(s);
        Assert.True(k.Ok, Why(k));
        var layers = k.Stacks.Single().Layers;
        Assert.Equal(3, layers.Count);
        Assert.Equal(40, Size(layers[0].Cut).W, 1);
        Assert.Equal(38, Size(layers[1].Cut).W, 1);
        Assert.Equal(36, Size(layers[2].Cut).W, 1);
        // The magnet's hole in the bottom only; the slot through the layers above it.
        Assert.Equal(Math.PI * 20 * 20 - Math.PI * 3.0375 * 3.0375, Geo.Area(layers[0].Cut), 3.0);
        Assert.Equal(Math.PI * 19 * 19 - 40, Geo.Area(layers[1].Cut), 3.0);
        Assert.Equal(Math.PI * 18 * 18 - 40, Geo.Area(layers[2].Cut), 3.0);
        Assert.Equal(1, k.Magnets);
    }

    [Fact]
    public void A_magnet_on_a_one_layer_base_gets_a_bottom_layer()
    {
        var k = BaseKitMaker.Make(One(BaseShape.Square, 25, count: 4) with { MagnetMm = 5 });
        Assert.True(k.Ok, Why(k));
        Assert.Equal(2, k.Stacks.Single().Layers.Count);
        Assert.Equal(8, k.PieceCount);
        Assert.Contains(k.Findings, f => f.Text.Contains("bottom layer"));
    }

    [Fact]
    public void Things_that_dont_fit_are_refused()
    {
        Assert.False(BaseKitMaker.Make(One(BaseShape.Round, 20) with { MagnetMm = 19 }).Ok);
        Assert.False(BaseKitMaker.Make(One(BaseShape.Round, 20) with { Fixing = FigureFixing.Slot, SlotLengthMm = 25 }).Ok);
        Assert.False(BaseKitMaker.Make(One(BaseShape.Round, 20) with { Layers = 4, StepMm = 3 }).Ok);
        Assert.False(BaseKitMaker.Make(One(BaseShape.Round, 25, count: BaseKitMaker.MaxBases + 1)).Ok);
        Assert.False(BaseKitMaker.Make(new BaseSpec { Bases = [] }).Ok);
    }

    [Fact]
    public void A_tray_has_a_pocket_for_each_base_that_holds_it_with_play()
    {
        var s = One(BaseShape.Round, 25, count: 10) with { Texture = "Cracked earth", MakeTray = true, Tray = new TraySpec { Columns = 5, Rows = 2, GapMm = 2, ClearMm = 0.4, BorderMm = 4, Holes = TrayHoles.PushOut } };
        var k = BaseKitMaker.Make(s);
        Assert.True(k.Ok, Why(k));
        var tray = k.Tray!;
        Assert.Equal(10, tray.Spots.Count);
        var frame = tray.Frames.Single();
        var (fw, fh) = Size(frame.Cut);
        Assert.Equal(5 * 25 + 4 * 2 + 2 * 0.4 + 2 * 4, fw, 1);
        Assert.Equal(2 * 25 + 2 + 2 * 0.4 + 2 * 4, fh, 1);
        // Each base fits in its pocket without touching the frame, and the pockets don't run into each other.
        var base0 = BaseShapes.Outline(BaseShape.Round, 25, 25, 1);
        foreach (var c in tray.Spots)
        {
            var b = Geo.Move(base0, c.x, c.y);
            Assert.True(Geo.Area(Geo.Intersect(b, frame.Cut)) < 1e-3);
            Assert.True(Geo.Area(Geo.Intersect(Geo.Offset(b, 0.35), frame.Cut)) < 1e-3);
        }
        Assert.Equal(10, Geo.Tidy(frame.Cut).Count(p => Clipper.Area(p) < 0));
        // The bottom has a push-out hole under each base.
        Assert.Equal(10, Geo.Tidy(tray.Bottom!.Cut).Count(p => Clipper.Area(p) < 0));
        Assert.True(Geo.Area(frame.Etched) > 10, $"etched {Geo.Area(frame.Etched)}");
    }

    [Fact]
    public void Staggered_round_bases_nest_closer_and_an_open_tray_has_one_pocket()
    {
        var straight = BaseKitMaker.Make(new BaseSpec { Bases = [], MakeTray = true, Tray = new TraySpec { Columns = 5, Rows = 3 } });
        var stagger = BaseKitMaker.Make(new BaseSpec { Bases = [], MakeTray = true, Tray = new TraySpec { Columns = 5, Rows = 3, Stagger = true } });
        Assert.True(stagger.Ok, Why(stagger));
        Assert.Equal(14, stagger.Tray!.Spots.Count);
        Assert.True(Size(stagger.Tray.Frames[0].Cut).H < Size(straight.Tray!.Frames[0].Cut).H - 5);
        var open = BaseKitMaker.Make(new BaseSpec { Bases = [], MakeTray = true, Tray = new TraySpec { Style = TrayStyle.Open, Columns = 4, Rows = 2, FrameLayers = 2 } });
        Assert.True(open.Ok, Why(open));
        Assert.Equal(2, open.Tray!.Frames.Count);
        Assert.Single(Geo.Tidy(open.Tray.Frames[^1].Cut), p => Clipper.Area(p) < 0);
    }

    [Fact]
    public void The_spec_survives_saving()
    {
        var s = One(BaseShape.Oval, 75, 42, 3) with { Texture = "Swamp", MagnetMm = 5, MakeTray = true, Tray = new TraySpec { Shape = BaseShape.Oval, WidthMm = 75, DepthMm = 42, Holes = TrayHoles.Magnets } };
        var back = BaseSpec.FromJson(s.ToJson())!;
        Assert.Equal(s.ToJson(), back.ToJson());
        Assert.Null(BaseSpec.FromJson("{ not json"));
    }

    [Fact]
    public void Placing_puts_every_piece_on_the_bed_and_can_be_replaced()
    {
        var machine = MachineProfile.F2Ultra;
        var s = One(BaseShape.Round, 25, count: 6) with { Texture = "Rocky ground", SizeA = 6, SizeB = 30, MagnetMm = 5, MakeTray = true, Tray = new TraySpec { Columns = 3, Rows = 2 } };
        var k = BaseKitMaker.Make(s);
        Assert.True(k.Ok, Why(k));
        var (state, sheets, id) = BasePlacement.Place(DocState.New(), k, s, machine, new BasePlaceOptions());
        int OnCut(DocState d, int n) => Enumerable.Range(0, n).SelectMany(i => Pages.SwitchTo(d, i).Shapes).Count(x => x.AddonData.ContainsKey(BasePlacement.SpecKey));
        Assert.Equal(k.PieceCount, OnCut(state, sheets));
        Assert.All(state.Shapes, x => Assert.True(x.Bounds.MinX >= 0 && x.Bounds.MaxX <= machine.FieldW && x.Bounds.MinY >= 0 && x.Bounds.MaxY <= machine.FieldH));
        Assert.True(state.Layers.FindIndex(l => l.Name == BasePlacement.EtchLayer) < state.Layers.FindIndex(l => l.Name == BasePlacement.CutLayer));
        Assert.True(state.Layers.FindIndex(l => l.Name == BasePlacement.LineLayer) < state.Layers.FindIndex(l => l.Name == BasePlacement.CutLayer));
        Assert.Equal(s.ToJson(), BasePlacement.Find(state, [state.Shapes[0].Id])!.Value.Spec.ToJson());

        var changed = s with { Texture = "Grass", SizeA = 3.5, MakeTray = false };
        var (s2, sheets2, id2) = BasePlacement.Place(state, BaseKitMaker.Make(changed), changed, machine, new BasePlaceOptions { Replace = id });
        Assert.Equal(id, id2);
        Assert.Equal("Grass", BasePlacement.Find(s2, [s2.Shapes[0].Id])!.Value.Spec.Texture);
        Assert.Equal(BaseKitMaker.Make(changed).PieceCount, OnCut(s2, s2.OtherPages.Count + 1));
    }
}
