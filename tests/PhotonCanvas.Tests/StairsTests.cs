using Clipper2Lib;
using PhotonCanvas.Addon.Stairs;
using PhotonCanvas.Addon.Walls;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using Placement = PhotonCanvas.Addon.Stairs.Placement;
using PlaceOptions = PhotonCanvas.Addon.Stairs.PlaceOptions;

namespace PhotonCanvas.Tests;

public class StairsTests
{
    static readonly FloorSpec Default = new();

    /// <summary>Every piece (and the walls and columns it fits) where it sits, its outline shrunk a hair so touching faces don't count.</summary>
    sealed record Placed(string Name, Pose Pose, PathsD Shrunk, PathsD Cut, double T, bool Ghost)
    {
        public bool Inside(V3 p, double eps = 0.03)
        {
            var (u, v, w) = Pose.Local(p);
            if (Math.Abs(w) > T / 2 - eps) return false;
            return In(Shrunk, u, v);
        }

        public (V3 Min, V3 Max) Box()
        {
            var b = Geo.Bounds(Cut);
            var pts = new List<V3>();
            foreach (var u in new[] { b.left, b.right })
                foreach (var v in new[] { b.top, b.bottom })
                    foreach (var w in new[] { -T / 2, T / 2 })
                        pts.Add(Pose.At(u, v, w));
            return (new(pts.Min(q => q.X), pts.Min(q => q.Y), pts.Min(q => q.Z)), new(pts.Max(q => q.X), pts.Max(q => q.Y), pts.Max(q => q.Z)));
        }
    }

    static bool In(PathsD area, double u, double v) => OutlineGrid.In(area, u, v);

    static List<Placed> Assemble(FloorKit k, double T) =>
    [
        .. k.Parts.SelectMany(p => p.Poses.Select(pose => new Placed(p.Name, pose, Geo.Offset(p.Cut, -0.03), p.Cut, T, false))),
        .. k.Ghosts.Select(g => new Placed(g.Name, g.Pose, Geo.Offset(g.Outline, -0.03), g.Outline, g.Thick, true)),
    ];

    /// <summary>Places where two pieces (or a piece and a wall or column) would be in the same space (sampled every 0.1 mm where their boxes meet).</summary>
    static List<string> Clashes(List<Placed> all, double step = 0.1)
    {
        var found = new List<string>();
        for (int i = 0; i < all.Count; i++)
            for (int j = i + 1; j < all.Count; j++)
            {
                if (all[i].Ghost && all[j].Ghost) continue;
                var (a0, a1) = all[i].Box();
                var (b0, b1) = all[j].Box();
                double x0 = Math.Max(a0.X, b0.X), y0 = Math.Max(a0.Y, b0.Y), z0 = Math.Max(a0.Z, b0.Z);
                double x1 = Math.Min(a1.X, b1.X), y1 = Math.Min(a1.Y, b1.Y), z1 = Math.Min(a1.Z, b1.Z);
                if (x1 - x0 < 0.06 || y1 - y0 < 0.06 || z1 - z0 < 0.06) continue;
                int hits = 0;
                V3 first = default;
                for (double x = x0 + step / 2; x < x1; x += step)
                    for (double y = y0 + step / 2; y < y1; y += step)
                        for (double z = z0 + step / 2; z < z1; z += step)
                        {
                            var p = new V3(x, y, z);
                            if (all[i].Inside(p) && all[j].Inside(p)) { if (hits++ == 0) first = p; }
                        }
                if (hits > 0) found.Add($"{all[i].Name} and {all[j].Name}: {hits} points, first at {first.X:0.##}, {first.Y:0.##}, {first.Z:0.##}");
            }
        return found;
    }

    /// <summary>Slot-like holes (one side as wide as the sheet plus the fit) with no other piece through their middle.</summary>
    static List<string> EmptySlots(List<Placed> all, double T, double fit)
    {
        var empty = new List<string>();
        foreach (var a in all.Where(p => !p.Ghost))
            foreach (var hole in Geo.Tidy(a.Cut).Where(p => Clipper.Area(p) < 0))
            {
                var b = Clipper.GetBounds(hole);
                double w = b.right - b.left, h = b.bottom - b.top;
                bool slot = Math.Abs(Math.Min(w, h) - (T + fit)) < 0.02 || Math.Abs(Math.Max(w, h) - (T + fit)) < 0.02;
                if (!slot) continue;
                var c = a.Pose.At((b.left + b.right) / 2, (b.top + b.bottom) / 2);
                if (!all.Any(o => !ReferenceEquals(o, a) && !o.Ghost && o.Inside(c, 0.01))) empty.Add($"{a.Name}: slot at {(b.left + b.right) / 2:0.##}, {(b.top + b.bottom) / 2:0.##}");
            }
        return empty;
    }

    static string Say(FloorKit k) => string.Join("\n", k.Findings.Select(f => $"{f.Level}: {f.Text}"));

    public static IEnumerable<object[]> Ways() => Enum.GetValues<WayUp>().Select(w => new object[] { w });

    [Theory]
    [MemberData(nameof(Ways))]
    public void Every_way_up_fits_the_room_with_nothing_in_anything_else(WayUp way)
    {
        var s = Default with { Way = way };
        var k = FloorKitMaker.Make(s);
        Assert.True(k.Ok, Say(k));
        var all = Assemble(k, s.SheetMm);
        Assert.Empty(Clashes(all));
        Assert.Empty(EmptySlots(all, s.SheetMm, s.FitMm));
        Assert.Contains(k.Parts, p => p.Name == "Floor");
        if (way != WayUp.None) Assert.True(k.Parts.Count >= 2);
    }

    [Theory]
    [MemberData(nameof(Ways))]
    public void Without_a_floor_the_way_up_still_comes_out_whole(WayUp way)
    {
        var k = FloorKitMaker.Make(Default with { Way = way, Floor = false, ClimbMm = 40 });
        Assert.True(k.Ok, Say(k));
        Assert.DoesNotContain(k.Parts, p => p.Name.StartsWith("Floor"));
        Assert.Empty(Clashes(Assemble(k, 3)));
    }

    [Fact]
    public void The_clash_check_finds_steps_too_big_for_their_slots()
    {
        // A slot fit well under nothing: the steps' tabs are wider than the slots they go through.
        var s = Default with { FitMm = -0.4 };
        var clashes = Clashes(Assemble(FloorKitMaker.Make(s), s.SheetMm));
        Assert.Contains(clashes, c => c.Contains("Step") && c.Contains("Stringer"));
    }

    [Fact]
    public void A_floor_fitted_to_the_wall_maker_clears_its_column_caps_and_rests_on_its_walls()
    {
        var walls = new WallSpec { LengthMm = 120, HeightMm = 45, ThicknessMm = 3, ColumnMm = 18, Cap = true, FitMm = 0.2, Connectors = new() { [ConnectorKind.Corner] = 4 } };
        var s = Default.FitTo(walls);
        Assert.Equal(WallKitMaker.PanelLength(walls) + 2 * WallKitMaker.CentreToEnd(walls), s.WallLengthMm, 2);
        Assert.Equal(45, s.WallHeightMm);
        var k = FloorKitMaker.Make(s);
        Assert.True(k.Ok, Say(k));
        // Each corner of the plate has a hole the wall maker's column cap passes through, with room to spare.
        var cap = Connectors.Column(walls, Connectors.Directions(ConnectorKind.Corner, 0), 3.2, 4, out var problem).Top;
        Assert.Null(problem);
        var floor = k.Parts.Single(p => p.Name == "Floor");
        foreach (var c in FloorKitMaker.ColumnCentres(s))
        {
            var placed = Geo.Move(Geo.Offset(cap, 0.2), c.x, c.y);
            Assert.Equal(0, Geo.Area(Geo.Intersect(placed, floor.Cut)), 3);
        }
        // The plate's underside is the walls' top.
        var plate = Assemble(k, s.SheetMm).Single(p => p.Name == "Floor");
        Assert.Equal(45 + s.SheetMm / 2, plate.Pose.O.Y, 6);
        Assert.Equal(4, FloorKitMaker.ColumnCentres(s).Count);
    }

    [Fact]
    public void Open_stairs_climb_to_the_floor_in_equal_steps()
    {
        var s = Default with { WallHeightMm = 51, RiseMm = 6 };
        var k = FloorKitMaker.Make(s);
        var steps = k.Parts.Single(p => p.Name == "Step");
        double top = 51 + 3, rise = top / k.Layout.Steps;
        Assert.Equal(9, k.Layout.Steps);
        Assert.Equal(8, steps.Copies);
        // Each step's top is a rise above the one under it (less half the fit it sits down in its slot).
        var heights = steps.Poses.Select(p => p.O.Y + s.SheetMm / 2).Order().ToList();
        for (int i = 0; i < heights.Count; i++) Assert.Equal((i + 1) * rise - s.FitMm / 2, heights[i], 6);
        Assert.Equal(top, heights[^1] + rise + s.FitMm / 2, 6);
    }

    [Fact]
    public void Solid_stairs_are_whole_layers_and_hide_their_pins()
    {
        var s = Default with { Way = WayUp.SolidStairs, WallHeightMm = 51, RiseMm = 6 };
        var k = FloorKitMaker.Make(s);
        Assert.True(k.Ok, Say(k));
        var layers = k.Parts.Where(p => p.Name.StartsWith("Step") || p.Name == "Top step").ToList();
        // 54 mm in 6 mm steps of two 3 mm layers: 8 steps (the ninth rise is up onto the floor), 16 layers.
        Assert.Equal(16, layers.Sum(p => p.Copies));
        var top = layers.Single(p => p.Name == "Top step");
        Assert.DoesNotContain(Geo.Tidy(top.Cut), p => Clipper.Area(p) < 0);
        Assert.All(layers.Where(p => p != top), p => Assert.Equal(2, Geo.Tidy(p.Cut).Count(q => Clipper.Area(q) < 0)));
        // Every layer's top is a whole number of sheets up.
        Assert.All(layers.SelectMany(p => p.Poses), p => Assert.Equal(0, Math.IEEERemainder(p.O.Y + 1.5, 3), 6));
    }

    [Fact]
    public void Spiral_steps_turn_one_step_each_and_thread_on_the_pin()
    {
        var s = Default with { Way = WayUp.Spiral, WallHeightMm = 51, SpiralStepDeg = 30, Clockwise = false };
        var k = FloorKitMaker.Make(s);
        Assert.True(k.Ok, Say(k));
        var steps = k.Parts.Single(p => p.Name == "Spiral step");
        var angles = steps.Poses.OrderBy(p => p.O.Y).Select(p => Math.Atan2(-p.U.Z, p.U.X) * 180 / Math.PI).ToList();
        for (int i = 1; i < angles.Count; i++) Assert.Equal(30, Math.IEEERemainder(angles[i] - angles[i - 1], 360), 6);
        // The pin goes up through every step, spacer and the base.
        Assert.Contains(k.Ghosts, g => g.Name == "Pin");
        Assert.Empty(Clashes(Assemble(k, s.SheetMm)));
    }

    [Fact]
    public void Stairs_too_long_for_the_room_are_refused()
    {
        var k = FloorKitMaker.Make(Default with { GoingMm = 20 });
        Assert.False(k.Ok);
        Assert.Contains(k.Findings, f => f.Level == Severity.Bad && f.Text.Contains("wall or a column"));
    }

    [Fact]
    public void An_opening_into_a_column_is_refused()
    {
        var k = FloorKitMaker.Make(Default with { HatchAuto = false, HatchXMm = 2, HatchYMm = 2, HatchLengthMm = 30, HatchWidthMm = 30 });
        Assert.Contains(k.Findings, f => f.Level == Severity.Bad && f.Text.Contains("column"));
    }

    [Theory]
    [InlineData(2, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 2)]
    public void A_floor_in_pieces_dovetails_back_into_the_whole_floor(int along, int across)
    {
        var whole = FloorKitMaker.Make(Default with { Way = WayUp.None, WallsAlong = 2, WallsAcross = 2 }).Parts.Single(p => p.Name == "Floor");
        var k = FloorKitMaker.Make(Default with { Way = WayUp.None, WallsAlong = 2, WallsAcross = 2, PiecesAlong = along, PiecesAcross = across });
        Assert.True(k.Ok, Say(k));
        var pieces = k.Parts.Where(p => p.Name.StartsWith("Floor piece")).ToList();
        Assert.Equal(along * across, pieces.Count);
        // No two pieces overlap, and together they're the whole floor less the dovetails' clearance.
        for (int i = 0; i < pieces.Count; i++)
            for (int j = i + 1; j < pieces.Count; j++)
                Assert.Equal(0, Geo.Area(Geo.Intersect(pieces[i].Cut, pieces[j].Cut)), 3);
        double sum = pieces.Sum(p => Geo.Area(p.Cut)), full = Geo.Area(whole.Cut);
        Assert.InRange(sum, full - 60, full + 1e-3);
        // Each piece has a dovetail into a neighbour or a socket from one.
        Assert.All(pieces, p => Assert.True(Geo.Tidy(p.Cut)[0].Count > 4));
        // A 2 × 2 room of 108.5 mm walls is too big for a 220 mm bed in one piece, but not in pieces.
        Assert.True(Geo.Bounds(whole.Cut).right - Geo.Bounds(whole.Cut).left > 216);
        Assert.All(pieces, p => Assert.True(Geo.Bounds(p.Cut).right - Geo.Bounds(p.Cut).left < 216));
    }

    [Fact]
    public void The_texture_stays_on_the_floor()
    {
        foreach (var tex in new[] { "Wood planks (across)", "Stone blocks", "Tiles", Textures.Plain })
        {
            var p = Textures.Preset(tex)!;
            var k = FloorKitMaker.Make(Default with { Texture = tex, SizeA = p.SizeA, SizeB = p.SizeB, GrooveMm = p.Groove });
            var floor = k.Parts.Single(x => x.Name == "Floor");
            Assert.Equal(0, Geo.Area(Geo.Subtract(floor.Etched, floor.Cut)), 2);
            if (tex == Textures.Plain) Assert.Empty(floor.Etched);
            else Assert.True(Geo.Area(floor.Etched) > 200, tex);
        }
    }

    [Theory]
    [InlineData(FloorEdge.OverColumns)]
    [InlineData(FloorEdge.FlushWithWalls)]
    [InlineData(FloorEdge.Overhang)]
    public void Every_edge_comes_out_in_one_piece(FloorEdge edge)
    {
        var s = Default with { Edge = edge, OverhangMm = 10 };
        var k = FloorKitMaker.Make(s);
        Assert.True(k.Ok, Say(k));
        var b = Geo.Bounds(k.Parts.Single(p => p.Name == "Floor").Cut);
        double expect = Plate.EdgeBeyond(s, 3);
        Assert.Equal(-expect, b.left, 3);
        Assert.Equal(108.5 + expect, b.right, 3);
        Assert.Empty(Clashes(Assemble(k, 3)));
    }

    [Fact]
    public void Without_an_opening_the_way_up_stands_outside_and_reaches_the_edge()
    {
        foreach (var way in new[] { WayUp.Stairs, WayUp.Ladder, WayUp.HookedLadder })
        {
            var s = Default with { Hatch = false, Way = way };
            var k = FloorKitMaker.Make(s);
            Assert.True(k.Ok, Say(k));
            Assert.Empty(Clashes(Assemble(k, 3)));
            Assert.True(Geo.Tidy(k.Parts.Single(p => p.Name == "Floor").Cut).Count(p => Clipper.Area(p) < 0) == 4, "only the columns' holes");
        }
    }

    [Fact]
    public void Settings_survive_being_saved()
    {
        var s = Default with { Way = WayUp.Spiral, Edge = FloorEdge.Overhang, Rail = Railing.Both, PiecesAlong = 2, Texture = "Tiles" };
        var back = FloorSpec.FromJson(s.ToJson())!;
        Assert.Equal(s.ToJson(), back.ToJson());
        Assert.Contains("\"Spiral\"", s.ToJson());
        Assert.Null(FloorSpec.FromJson("{not json"));
    }

    [Fact]
    public void Placing_puts_every_piece_on_its_layers_and_can_replace_it()
    {
        var machine = MachineProfile.F2Ultra;
        var spec = Default with { Rail = Railing.Right, Trapdoor = true };
        var kit = FloorKitMaker.Make(spec);
        var (s, sheets, id) = Placement.Place(DocState.New(), kit, spec, machine, new PlaceOptions());
        var all = Enumerable.Range(0, sheets).SelectMany(i => Pages.SwitchTo(s, i).Shapes).ToList();
        Assert.Equal(kit.PieceCount, all.Count(x => x.AddonData.ContainsKey(Placement.SpecKey)));
        Assert.True(s.Layers.FindIndex(l => l.Name == Placement.EtchLayer) < s.Layers.FindIndex(l => l.Name == Placement.CutLayer));
        Assert.All(s.Shapes, x => Assert.True(x.Bounds.MinX >= 0 && x.Bounds.MaxX <= machine.FieldW && x.Bounds.MinY >= 0 && x.Bounds.MaxY <= machine.FieldH));
        Assert.Equal(spec.ToJson(), Placement.Find(s, [s.Shapes[0].Id])!.Value.Spec.ToJson());

        var changed = spec with { Way = WayUp.Ladder };
        var (s2, _, id2) = Placement.Place(s, FloorKitMaker.Make(changed), changed, machine, new PlaceOptions { Replace = id });
        Assert.Equal(id, id2);
        Assert.DoesNotContain(s2.Shapes, x => x.Name.StartsWith("Stringer"));
        Assert.Equal(WayUp.Ladder, Placement.Find(s2, [s2.Shapes[0].Id])!.Value.Spec.Way);

        // Walls in the design are found for fitting the floor to.
        var walls = new WallSpec { HeightMm = 40 };
        var withWalls = Ops.AddShapes(DocState.New(), [new PathShape { Name = "Wall", Geometry = Geo.ToGeometry([Geo.Rect(0, 0, 10, 10)]) }.WithAddonData(PhotonCanvas.Addon.Walls.Placement.SpecKey, walls.ToJson())]);
        Assert.Equal(40, Placement.WallsIn(withWalls, [])!.HeightMm);
    }
}
