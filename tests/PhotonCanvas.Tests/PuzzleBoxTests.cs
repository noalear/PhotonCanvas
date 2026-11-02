using Clipper2Lib;
using PhotonCanvas.Addon.PuzzleBox;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;

namespace PhotonCanvas.Tests;

public class PuzzleBoxTests
{
    public static IEnumerable<object[]> Kinds() => Presets.Kinds.Select((k, i) => new object[] { i });

    static BoxDesign Make(BoxSpec s) => BoxMaker.Make(s);

    /// <summary>Every body hangs together: each of its pieces is glued to the one above or below (a piece in every layer from its lowest to its highest, overlapping the next).</summary>
    static void Glued(BoxDesign d)
    {
        foreach (var b in d.Bodies)
        {
            var layers = b.Solid.Keys.ToList();
            for (int i = 1; i < layers.Count; i++)
            {
                Assert.True(layers[i] == layers[i - 1] + 1, $"{b.Name}: a gap between layers {layers[i - 1] + 1} and {layers[i] + 1}");
                var both = Geo.Intersect(b.Outline(layers[i]), b.Outline(layers[i - 1]));
                Assert.True(Geo.Area(both) > 4, $"{b.Name}: layer {layers[i] + 1} isn't glued to the one under it");
            }
            foreach (var l in layers)
                Assert.True(Geo.Pieces(b.Outline(l)) == 1, $"{b.Name}: layer {l + 1} comes out in more than one piece");
        }
    }

    static void Works(BoxDesign d)
    {
        Assert.True(d.Ok, string.Join("\n", d.Findings));
        Assert.Empty(Solver.Overlaps(d, Pose.Shut(d.Bodies.Count)));
        Glued(d);
        Assert.NotNull(d.Solution);
        Assert.True(d.Solution!.Solved);
        // Every pose on the way is a real one: nothing inside anything.
        foreach (var p in d.Solution.Poses) Assert.Empty(Solver.Overlaps(d, p));
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void Every_kind_goes_together_and_opens(int kind)
    {
        var d = Make(Presets.Kinds[kind].Spec);
        Works(d);
        Assert.DoesNotContain(d.Findings, f => f.Level == Severity.Warn);
        var kit = Kit.Make(d);
        Assert.True(kit.Ok);
        Assert.All(kit.Parts, p => Assert.Equal(1, Geo.Pieces(p.Cut)));
        // The layers add up to the height (whole layers).
        Assert.Equal(Math.Round(Presets.Kinds[kind].Spec.HeightMm / 3), d.Layers);
    }

    public static IEnumerable<object[]> Variants()
    {
        var puzzle = Presets.Kinds[0].Spec;
        var drawer = Presets.Kinds[1].Spec;
        var both = Presets.Kinds[2].Spec;
        var block = Presets.Kinds[3].Spec;
        yield return [puzzle with { Panels = 1 }];
        yield return [puzzle with { Panels = 2, Difficulty = Difficulty.Easy }];
        yield return [puzzle with { Panels = 5, DepthMm = 110, Difficulty = Difficulty.Hard }];
        yield return [puzzle with { Panels = 4, ThicknessMm = 4, WallMm = 14, HeightMm = 60, WidthMm = 140, DepthMm = 110 }];
        yield return [puzzle with { ThicknessMm = 2, WallMm = 10, HeightMm = 30, Seed = 7 }];
        yield return [puzzle with { FitMm = 0.4, Seed = 3 }];
        yield return [drawer with { Bolt = false }];
        yield return [drawer with { WidthMm = 90, DepthMm = 80, WallMm = 10, HeightMm = 54 }];
        yield return [drawer with { ThicknessMm = 6, WallMm = 16, HeightMm = 72, DrawerHeightMm = 24, WidthMm = 160, DepthMm = 120 }];
        yield return [drawer with { FingerNotch = false }];
        yield return [both with { Panels = 4, DepthMm = 100, Difficulty = Difficulty.Hard, Seed = 11 }];
        yield return [block with { DrawerHeightMm = 12 }];
    }

    [Theory]
    [MemberData(nameof(Variants))]
    public void Other_sizes_and_sheets_go_together_and_open(BoxSpec s) => Works(Make(s));

    [Fact]
    public void Panels_only_come_out_after_the_right_shifts()
    {
        var d = Make(Presets.Kinds[0].Spec with { Panels = 3 });
        Works(d);
        var sol = d.Solution!;
        // Shut, no panel slides out: the front one is hooked to the next.
        var shut = Pose.Shut(d.Bodies.Count);
        Assert.DoesNotContain(Solver.Moves(d, shut), m => m.Move.Out);
        // The way out shifts panels sideways before each comes out, and they come out front first.
        Assert.Contains(sol.Moves, m => m.Dir is Dir.Left or Dir.Right);
        var outs = sol.Moves.Where(m => m.Out).Select(m => d.Bodies[m.Body].Number).ToList();
        Assert.Equal([1, 2, 3], outs);
        Assert.True(sol.Moves.Count >= 5);
    }

    [Fact]
    public void Without_its_hooks_the_front_panel_just_slides_out()
    {
        var d = Make(Presets.Kinds[0].Spec);
        // Take every hook's foot off (the runners back to plain rectangles under their tops, wider so they still hold in the groove).
        foreach (var b in d.Bodies.Where(b => b.Role == BodyRole.Panel))
        {
            int runner = b.Solid.Keys.Min();
            var r = b.Solid[runner];
            double x0 = r.Min(q => q.X0), x1 = r.Max(q => q.X1), y0 = b.Solid[runner + 1][0].Y0, y1 = r.Max(q => q.Y1);
            b.Set(runner, [new Box2(x0, y0, x1, Math.Min(y1, b.Number < 3 ? b.Solid[runner + 1][0].Y1 : y1))]);
        }
        var sol = Solver.Solve(d);
        Assert.True(sol.Solved);
        Assert.Equal(3, sol.Moves.Count);
        Assert.True(sol.Moves[0].Out);
    }

    [Fact]
    public void The_drawer_only_opens_after_the_button_and_the_button_only_once_the_box_is_open()
    {
        var d = Make(Presets.Kinds[2].Spec);
        Works(d);
        var sol = d.Solution!;
        int button = d.Bodies.FindIndex(b => b.Role == BodyRole.Button), drawer = d.DrawerBody!.Value;
        int firstButton = sol.Moves.FindIndex(m => m.Body == button);
        int drawerOut = sol.Moves.FindIndex(m => m.Body == drawer);
        Assert.True(firstButton >= 0 && drawerOut > firstButton);
        Assert.True(firstButton >= sol.BoxOpenAt, "the button moved before the box was open");
        // Shut, the drawer doesn't move more than the play in it.
        var shut = Pose.Shut(d.Bodies.Count);
        Assert.True(Solver.Travel(d, shut, drawer, Dir.Forward) < Solver.MinMove(d.Fit));
        // With everything over the top out, the button slides across and then the drawer comes right out.
        var open = sol.Poses[sol.BoxOpenAt];
        Assert.True(Solver.Travel(d, open, drawer, Dir.Forward) < Solver.MinMove(d.Fit));
        double slide = Solver.Travel(d, open, button, Dir.Right);
        Assert.True(slide > 4);
        var slid = open.With(button, slide, 0, false);
        Assert.True(Solver.Travel(d, slid, drawer, Dir.Forward) >= Solver.Away);
        // The button can't be lifted out (the tooth under the floor is longer than the slot is wide), shut or slid.
        Assert.False(Solver.Lifts(d, open, button));
        Assert.False(Solver.Lifts(d, slid, button));
    }

    [Fact]
    public void The_button_covers_its_slot_whichever_way_it_is_slid()
    {
        var d = Make(Presets.Kinds[1].Spec);
        Works(d);
        int button = d.Bodies.FindIndex(b => b.Role == BodyRole.Button);
        var b = d.Bodies[button];
        int floor = b.Solid.Keys.ElementAt(1), top = b.Solid.Keys.ElementAt(2);
        // The slot is the hole in the floor; the button is the piece above it.
        var floorHoles = Geo.Tidy(d.Bodies[0].Outline(floor)).Where(p => Clipper.Area(p) < 0).ToList();
        var slot = Assert.Single(floorHoles);
        var nub = b.Outline(top);
        Assert.True(Geo.Area(Geo.Subtract([slot], nub)) < 1e-3, "the slot shows beside the button");
        double slide = Solver.Travel(d, Pose.Shut(d.Bodies.Count).With(d.TopBodies[0], 0, 0, true), button, Dir.Right);
        Assert.True(Geo.Area(Geo.Subtract([slot], Geo.Move(nub, slide, 0))) < 1e-3, "the slot shows beside the button once it's slid");
    }

    [Fact]
    public void The_lid_lifts_off_and_the_panels_do_not()
    {
        var lid = Make(Presets.Kinds[1].Spec);
        Assert.True(Solver.Lifts(lid, Pose.Shut(lid.Bodies.Count), lid.TopBodies[0]));
        var panels = Make(Presets.Kinds[0].Spec);
        foreach (var p in panels.TopBodies) Assert.False(Solver.Lifts(panels, Pose.Shut(panels.Bodies.Count), p));
        // Shifted as far as they go either way, they're still held down by the top layer.
        var right = Pose.Shut(panels.Bodies.Count);
        foreach (var p in panels.TopBodies) right = right.With(p, BoxMaker.Size(panels.Spec).Stop, 0, false);
        Assert.Empty(Solver.Overlaps(panels, right));
        foreach (var p in panels.TopBodies) Assert.False(Solver.Lifts(panels, right, p));
    }

    [Fact]
    public void Harder_puzzles_take_at_least_as_many_moves()
    {
        int Moves(Difficulty diff) => Make(Presets.Kinds[0].Spec with { Panels = 4, DepthMm = 100, Difficulty = diff, Seed = 5 }).Solution!.Moves.Count;
        int easy = Moves(Difficulty.Easy), hard = Moves(Difficulty.Hard);
        Assert.True(hard >= easy, $"easy {easy}, hard {hard}");
        Assert.True(hard >= 7);
    }

    [Fact]
    public void The_drawer_front_is_flush_and_its_seams_are_the_fit()
    {
        var s = Presets.Kinds[3].Spec;
        var d = Make(s);
        Works(d);
        var dr = d.Bodies[d.DrawerBody!.Value];
        var front = dr.Solid[1][0];
        Assert.Equal(0, front.Y0, 6);
        Assert.Equal(s.WallMm + s.FitMm, front.X0, 6);
        Assert.Equal(s.WidthMm - s.WallMm - s.FitMm, front.X1, 6);
        // The drawer's layers line up with the box's, so its seams are the layers' seams.
        Assert.Equal(Enumerable.Range(1, dr.Solid.Count), dr.Solid.Keys);
    }

    [Fact]
    public void Too_small_or_impossible_boxes_are_refused_not_squeezed()
    {
        Assert.False(Make(Presets.Kinds[0].Spec with { WidthMm = 40 }).Ok);
        Assert.False(Make(Presets.Kinds[0].Spec with { Panels = 8, DepthMm = 60 }).Ok);
        Assert.False(Make(Presets.Kinds[1].Spec with { HeightMm = 15 }).Ok);
        Assert.False(Make(Presets.Kinds[0].Spec with { WallMm = 7 }).Ok);
        Assert.False(Make(new BoxSpec { Top = TopKind.Closed, Drawer = false }).Ok);
        Assert.False(Make(Presets.Kinds[0].Spec with { HeightMm = 0 }).Ok);
    }

    [Fact]
    public void Letters_go_on_the_glued_faces_when_the_font_has_them()
    {
        var d = Make(Presets.Kinds[2].Spec);
        if (Labels.Text("A", "Arial", 0, 0, 5, 5).Count == 0) return;
        var frame = d.Bodies[0];
        int top = frame.Solid.Keys.Max();
        Assert.False(frame.Etched.ContainsKey(top));
        Assert.True(frame.Etched.ContainsKey(0));
        // Each letter sits where the layer above covers it.
        foreach (var (layer, marks) in frame.Etched)
        {
            if (layer == top) continue;
            Assert.True(Geo.Area(Geo.Subtract(Geo.Union(marks), frame.Outline(layer + 1))) < 0.01, $"layer {layer + 1}'s letter shows");
        }
        Assert.StartsWith("A, B", Kit.Order(d));
        var none = Make(Presets.Kinds[2].Spec with { MarkPieces = false });
        Assert.DoesNotContain(none.Bodies, b => b.Etched.Count > 0 && b.Role != BodyRole.Panel);
        var msg = Make(Presets.Kinds[2].Spec with { Message = "Happy birthday" });
        Assert.True(msg.Bodies[msg.DrawerBody!.Value].Etched.ContainsKey(1));
    }

    [Fact]
    public void Settings_round_trip_through_json()
    {
        var s = Presets.Kinds[2].Spec with { Panels = 4, Difficulty = Difficulty.Hard, Message = "x", MessageAt = MessagePlace.Floor, Seed = 42, FitMm = 0.3 };
        Assert.Equal(s, BoxSpec.FromJson(s.ToJson()));
        Assert.Null(BoxSpec.FromJson("{not json"));
    }

    [Fact]
    public void The_same_settings_make_the_same_box()
    {
        var a = Make(Presets.Kinds[2].Spec with { Seed = 9 });
        var b = Make(Presets.Kinds[2].Spec with { Seed = 9 });
        Assert.Equal(a.Solution!.Moves.Select(m => (m.Body, m.Dir)), b.Solution!.Moves.Select(m => (m.Body, m.Dir)));
        Assert.Equal(a.Hooks.Select(h => (h.Dir, h.Half)), b.Hooks.Select(h => (h.Dir, h.Half)));
    }

    [Fact]
    public void Placing_puts_every_piece_on_its_layers_and_can_replace_it()
    {
        var spec = Presets.Kinds[2].Spec with { Message = "Hello" };
        var kit = Kit.Make(Make(spec));
        var machine = MachineProfile.F2Ultra;
        var (s, sheets, id) = Placement.Place(DocState.New(), kit, machine, new PlaceOptions());
        int cutShapes = Enumerable.Range(0, sheets).Select(i => Pages.SwitchTo(s, i)).Sum(p => p.Shapes.Count(x => p.Layers.First(l => l.Id == x.LayerId).Name == Placement.CutLayer));
        Assert.Equal(kit.PieceCount, cutShapes);
        Assert.True(s.Layers.FindIndex(l => l.Name == Placement.EtchLayer) < s.Layers.FindIndex(l => l.Name == Placement.CutLayer));
        Assert.All(s.Shapes, x => Assert.True(x.Bounds.MinX >= 0 && x.Bounds.MaxX <= machine.FieldW && x.Bounds.MinY >= 0 && x.Bounds.MaxY <= machine.FieldH));
        Assert.Equal(spec, Placement.Find(s, Placement.ShapesOf(s, id))!.Value.Spec);
        var (s2, _, id2) = Placement.Place(s, Kit.Make(Make(spec with { Panels = 2 })), machine, new PlaceOptions { Replace = id });
        Assert.Equal(id, id2);
        Assert.Equal(spec with { Panels = 2 }, Placement.Find(s2, Placement.ShapesOf(s2, id))!.Value.Spec);
    }
}
