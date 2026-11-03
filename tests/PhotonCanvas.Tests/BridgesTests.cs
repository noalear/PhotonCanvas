using Clipper2Lib;
using PhotonCanvas.Addon.Bridges;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;

namespace PhotonCanvas.Tests;

public class BridgesTests
{
    static BridgeKit Make(BridgeSpec s) => BridgeKits.Make(s);
    static Part Part(BridgeKit k, string name) => k.Parts.Single(p => p.Name == name);

    public static IEnumerable<object[]> Kinds() => Defaults.Kinds.Select(k => new object[] { k.Kind });

    /// <summary>Every piece where it sits once put together, with its outline shrunk a hair so touching faces don't count.</summary>
    sealed class Placed
    {
        public string Name { get; }
        public Pose Pose { get; }
        public PathsD Shrunk { get; }
        public PathsD Cut { get; }
        public double T { get; }
        public V3 Min { get; }
        public V3 Max { get; }

        public Placed(string name, Pose pose, PathsD cut, double t)
        {
            Name = name;
            Pose = pose;
            Cut = cut;
            T = t;
            Shrunk = Geo.Offset(cut, -0.03);
            var b = Geo.Bounds(cut);
            var pts = new List<V3>();
            foreach (var u in new[] { b.left, b.right })
                foreach (var v in new[] { b.top, b.bottom })
                    foreach (var w in new[] { -t / 2, t / 2 })
                        pts.Add(pose.At(u, v, w));
            Min = new(pts.Min(q => q.X), pts.Min(q => q.Y), pts.Min(q => q.Z));
            Max = new(pts.Max(q => q.X), pts.Max(q => q.Y), pts.Max(q => q.Z));
        }

        public bool Inside(V3 p, double eps = 0.03)
        {
            var (u, v, w) = Pose.Local(p);
            if (Math.Abs(w) > T / 2 - eps) return false;
            return In(Shrunk, u, v);
        }
    }

    static bool In(PathsD area, double u, double v) => OutlineGrid.In(area, u, v);

    static List<Placed> Assemble(BridgeKit k) =>
        [.. k.Parts.SelectMany(p => p.Poses.Select(pose => new Placed(p.Name, pose, p.Cut, k.Spec.ThicknessMm)))];

    /// <summary>Places where two pieces would be in the same space once put together (sampled where their boxes meet).</summary>
    static List<string> Clashes(List<Placed> all)
    {
        var found = new List<string>();
        for (int i = 0; i < all.Count; i++)
            for (int j = i + 1; j < all.Count; j++)
            {
                var (a, b) = (all[i], all[j]);
                double x0 = Math.Max(a.Min.X, b.Min.X), y0 = Math.Max(a.Min.Y, b.Min.Y), z0 = Math.Max(a.Min.Z, b.Min.Z);
                double x1 = Math.Min(a.Max.X, b.Max.X), y1 = Math.Min(a.Max.Y, b.Max.Y), z1 = Math.Min(a.Max.Z, b.Max.Z);
                if (x1 - x0 < 0.06 || y1 - y0 < 0.06 || z1 - z0 < 0.06) continue;
                // About 0.1 mm apart, coarser in big overlaps so the test stays quick.
                double step = Math.Max(0.1, Math.Cbrt((x1 - x0) * (y1 - y0) * (z1 - z0) / 150000));
                int hits = 0;
                V3 first = default;
                for (double x = x0 + step / 2; x < x1; x += step)
                    for (double y = y0 + step / 2; y < y1; y += step)
                        for (double z = z0 + step / 2; z < z1; z += step)
                        {
                            var p = new V3(x, y, z);
                            if (a.Inside(p) && b.Inside(p)) { if (hits++ == 0) first = p; }
                        }
                if (hits > 0) found.Add($"{a.Name} and {b.Name}: {hits} points, first at {first.X:0.##}, {first.Y:0.##}, {first.Z:0.##}");
            }
        return found;
    }

    /// <summary>Slot-like holes (rectangles, one side as wide as the sheet plus the fit, at any angle) with no other piece through their middle.</summary>
    static List<string> EmptySlots(List<Placed> all, double T, double fit)
    {
        var empty = new List<string>();
        foreach (var a in all)
            foreach (var hole in Geo.Tidy(a.Cut).Where(p => Clipper.Area(p) < 0))
            {
                double area = Math.Abs(Clipper.Area(hole)), per = 0;
                for (int i = 0; i < hole.Count; i++) { var p = hole[i]; var q = hole[(i + 1) % hole.Count]; per += Math.Sqrt((p.x - q.x) * (p.x - q.x) + (p.y - q.y) * (p.y - q.y)); }
                double d = per * per / 16 - area;
                if (d < 0 || hole.Count > 8) continue;
                double w = per / 4 - Math.Sqrt(d), h = per / 4 + Math.Sqrt(d);
                if (Math.Abs(w - (T + fit)) > 0.03 && Math.Abs(h - (T + fit)) > 0.03) continue;
                var c = new PointD(hole.Average(p => p.x), hole.Average(p => p.y));
                var at = a.Pose.At(c.x, c.y);
                if (!all.Any(o => !ReferenceEquals(o, a) && o.Inside(at, 0.01))) empty.Add($"{a.Name}: slot at {c.x:0.##}, {c.y:0.##}");
            }
        return empty;
    }

    /// <summary>Pieces joined to the rest (their boxes overlap another's by nearly a sheet's thickness every way: a tab in a slot, a plank in a notch).</summary>
    static bool AllJoined(List<Placed> all, double T)
    {
        if (all.Count < 2) return true;
        bool Joined(Placed a, Placed b)
        {
            double dx = Math.Min(a.Max.X, b.Max.X) - Math.Max(a.Min.X, b.Min.X), dy = Math.Min(a.Max.Y, b.Max.Y) - Math.Max(a.Min.Y, b.Min.Y), dz = Math.Min(a.Max.Z, b.Max.Z) - Math.Max(a.Min.Z, b.Min.Z);
            return Math.Min(dx, Math.Min(dy, dz)) > T * 0.4;
        }
        var seen = new HashSet<int> { 0 };
        var queue = new Queue<int>([0]);
        while (queue.Count > 0)
        {
            int i = queue.Dequeue();
            for (int j = 0; j < all.Count; j++)
                if (!seen.Contains(j) && Joined(all[i], all[j])) { seen.Add(j); queue.Enqueue(j); }
        }
        return seen.Count == all.Count;
    }

    static void FitsTogether(BridgeKit k)
    {
        Assert.True(k.Ok, string.Join("\n", k.Findings));
        Assert.NotEmpty(k.Parts);
        Assert.All(k.Parts, p => Assert.Equal(1, Geo.Pieces(p.Cut)));
        Assert.All(k.Parts, p => Assert.NotEmpty(p.Poses));
        var all = Assemble(k);
        Assert.Empty(Clashes(all));
        Assert.Empty(EmptySlots(all, k.Spec.ThicknessMm, k.Spec.FitMm));
        Assert.True(AllJoined(all, k.Spec.ThicknessMm), "a piece isn't joined to the others");
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void Every_kind_makes_whole_pieces_that_fit_together_without_clashing(BridgeKind kind)
    {
        var k = Make(Defaults.For(kind));
        FitsTogether(k);
        Assert.DoesNotContain(k.Findings, f => f.Level == Severity.Warn);
    }

    public static IEnumerable<object[]> Variants()
    {
        var plank = Defaults.For(BridgeKind.Plank);
        yield return [plank with { RiseMm = 0 }];
        yield return [plank with { RiseMm = 0, Railing = false, HeightMm = 4 }];
        yield return [plank with { HeightMm = 25, Legs = 2, LengthMm = 140 }];
        yield return [plank with { HeightMm = 18, Legs = 1, RiseMm = 14, PostEvery = 3, Rough = true }];
        yield return [plank with { Ends = EndsOn.Banks, RiseMm = 12 }];
        yield return [plank with { ThicknessMm = 3, BeamMm = 5, PlankMm = 5, GapMm = 0.8, WidthMm = 30, HeightMm = 12, FitMm = 0.15 }];
        yield return [plank with { ThicknessMm = 1.5, PlankMm = 3, GapMm = 0.5, BeamMm = 3, RailMm = 10, LengthMm = 70 }];
        var rope = Defaults.For(BridgeKind.Rope);
        yield return [rope with { Rough = false, MissingPlanks = 0 }];
        yield return [rope with { Railing = false }];
        yield return [rope with { Ends = EndsOn.Table, HeightMm = 30, RiseMm = 12 }];
        yield return [rope with { RiseMm = 0, PostEvery = 5, MissingPlanks = 3 }];
        yield return [rope with { ThicknessMm = 3, PlankMm = 5, GapMm = 1, LengthMm = 160, RiseMm = 16, FitMm = 0.15 }];
        var stone = Defaults.For(BridgeKind.Stone);
        yield return [stone with { Arch = ArchShape.Segmental, HeightMm = 25 }];
        yield return [stone with { Arch = ArchShape.Pointed, ArchSpanMm = 30, HeightMm = 34 }];
        yield return [stone with { Arches = 3, ArchSpanMm = 28, LengthMm = 140, HeightMm = 29, Arch = ArchShape.Round, SpringMm = 6 }];
        yield return [stone with { Road = RoadShape.Humped, EndHeightMm = 16 }];
        yield return [stone with { Road = RoadShape.Humped, EndHeightMm = 10, Arches = 2, ArchSpanMm = 26, PierMm = 7, HeightMm = 24, Parapet = ParapetTop.None }];
        yield return [stone with { Parapet = ParapetTop.Battlements, ParapetMm = 9, Texture = "Brick", SizeA = 8, SizeB = 3.5 }];
        yield return [stone with { Parapet = ParapetTop.None, Texture = Textures.Plain, Surface = RoadSurface.Flagstones }];
        yield return [stone with { ThicknessMm = 2, FitMm = 0.1, WidthMm = 24, Texture = "Fieldstone", SizeA = 9, SizeB = 6, Surface = RoadSurface.Plain }];
        yield return [stone with { Texture = "Wood planks (across)", SizeA = 5, SizeB = 35, Road = RoadShape.Humped, Parapet = ParapetTop.Battlements }];
    }

    [Theory]
    [MemberData(nameof(Variants))]
    public void Other_styles_and_sheets_fit_together_too(BridgeSpec spec) => FitsTogether(Make(spec));

    [Fact]
    public void A_plank_bridge_is_the_size_asked_with_its_deck_at_the_height_asked()
    {
        var spec = Defaults.For(BridgeKind.Plank) with { LengthMm = 120, WidthMm = 30, HeightMm = 12, RiseMm = 10 };
        var k = Make(spec);
        var all = Assemble(k);
        Assert.Equal(0, all.Min(p => p.Min.X), 2);
        Assert.Equal(120, all.Where(p => !p.Name.StartsWith("Plank")).Max(p => p.Max.X), 2);
        Assert.Equal(0, all.Min(p => p.Min.Y), 3);
        var planks = all.Where(p => p.Name.StartsWith("Plank")).ToList();
        // The planks' tops are flush with the deck: 12 mm at the ends, 10 more in the middle.
        Assert.Equal(22, planks.Max(p => p.Max.Y), 0);
        Assert.True(planks.Min(p => p.Max.Y) > 12 - 0.01);
        Assert.True(planks.Min(p => p.Max.Y) < 14.5);
        // The planks reach past both sides, and the walkway between the sides is as wide as asked.
        double zs = 30 / 2.0 + 1;
        Assert.All(planks, p => Assert.True(p.Max.Z > zs + 1 && p.Min.Z < -zs - 1));
        var sides = all.Where(p => p.Name.StartsWith("Side")).ToList();
        Assert.Equal(2, sides.Count);
        Assert.Equal(30, sides.Max(p => p.Min.Z) - sides.Min(p => p.Max.Z), 3);
    }

    [Fact]
    public void A_plank_cannot_slide_across_its_necks_are_narrower_than_the_notches_it_sits_in()
    {
        var spec = Defaults.For(BridgeKind.Plank) with { Rough = false };
        var k = Make(spec);
        var plank = Part(k, "Plank");
        var b = Geo.Bounds(plank.Cut);
        double width = b.right - b.left, keeper = Planks.Keeper(spec.PlankMm);
        Assert.Equal(spec.PlankMm, width, 3);
        double notch = spec.PlankMm - 2 * keeper + spec.FitMm;
        Assert.True(notch < width - 0.5, "the plank's full width must be wider than the notch, so it can't slide through it");
        // Two necks, one where each side crosses it.
        Assert.Equal(4, Geo.Area(Geo.Subtract([Geo.Rect(b.left, b.top, b.right, b.bottom)], plank.Cut)) / (keeper * (spec.ThicknessMm + spec.FitMm)), 1);
    }

    [Fact]
    public void A_rope_bridge_sags_between_its_posts_and_leaves_planks_out()
    {
        var spec = Defaults.For(BridgeKind.Rope) with { RiseMm = 12, MissingPlanks = 2 };
        var k = Make(spec);
        FitsTogether(k);
        var all = Assemble(k);
        var planks = all.Where(p => p.Name.StartsWith("Plank")).ToList();
        double ropeD = spec.ThicknessMm + 1.6;
        // Standing on the banks: the posts start at 0, the deck's ends are a rope's depth up, the middle hangs 12 lower.
        var side = Part(k, "Side (ropes and posts)");
        Assert.True(In(side.Cut, spec.PostMm / 2, 0.05) && !In(side.Cut, spec.PostMm / 2, -0.05), "the posts stand at 0");
        Assert.Equal(-12, all.Where(p => p.Name.StartsWith("Side")).Min(p => p.Min.Y), 2);
        Assert.Equal(ropeD - 12, planks.Min(p => p.Max.Y), 0);
        var line = DeckLine.Sag(ropeD, 12, spec.PostMm, spec.LengthMm - spec.PostMm);
        var laid = Planks.Lay(line, 0.6, line.Length - 0.6, spec.PlankMm, spec.GapMm);
        Assert.Equal(laid.Count - 2, planks.Count);
        Assert.Contains(k.Findings, f => f.Text.Contains("below them"));
    }

    [Fact]
    public void Posts_come_every_few_planks_the_same_from_both_ends_with_room_to_pass_a_plank()
    {
        foreach (int planks in new[] { 7, 12, 23, 24, 25, 40 })
            foreach (int every in new[] { 2, 3, 4, 5 })
            {
                var at = Planks.PostTeeth(planks, every);
                Assert.Equal(at.Select(t => planks - 2 - t).OrderBy(t => t), at);
                var bounds = new List<int> { -1 };
                bounds.AddRange(at);
                bounds.Add(planks - 1);
                Assert.All(bounds.Zip(bounds.Skip(1), (a, b) => b - a), bay => Assert.True(bay >= 2, $"{planks} planks every {every}: a bay of {bay}"));
            }
    }

    [Fact]
    public void A_stone_bridge_has_its_arches_open_underneath_and_the_road_at_the_height_asked()
    {
        var spec = Defaults.For(BridgeKind.Stone) with { Arches = 2, ArchSpanMm = 30, LengthMm = 120, HeightMm = 30 };
        var k = Make(spec);
        FitsTogether(k);
        var side = Part(k, "Side wall");
        Assert.Equal(2, side.Copies);
        double a = (120 - 2 * 30 - Math.Max(spec.ThicknessMm + spec.FitMm + 3, 30 * 0.25)) / 2;
        Assert.False(In(side.Cut, a + 15, 1), "the first arch is open");
        Assert.False(In(side.Cut, 120 - a - 15, 1), "the second arch is open");
        Assert.True(In(side.Cut, a / 2, 1), "the end stands on the table");
        var b = Geo.Bounds(side.Cut);
        Assert.Equal(30 + spec.ParapetMm, b.bottom - b.top, 3);
        var all = Assemble(k);
        Assert.Equal(30, all.Single(p => p.Name == "Road").Max.Y, 3);
        Assert.Single(k.Parts, p => p.Name == "Pier wall");
        Assert.Equal(2, Part(k, "End wall").Copies);
        // Stone is etched on the walls, with grooves round the arch stones.
        Assert.True(Geo.Area(side.Etched) > 50);
    }

    [Fact]
    public void A_humped_road_is_three_pieces_ramps_and_a_level_top()
    {
        var spec = Defaults.For(BridgeKind.Stone) with { Road = RoadShape.Humped, EndHeightMm = 16 };
        var k = Make(spec);
        Assert.Equal(2, Part(k, "Road (ramp)").Copies);
        Assert.Equal(1, Part(k, "Road (top)").Copies);
        var all = Assemble(k);
        var ramps = all.Where(p => p.Name == "Road (ramp)").ToList();
        Assert.True(ramps.Min(p => p.Min.X) < 0.01 && ramps.Max(p => p.Max.X) > spec.LengthMm - 0.01);
        Assert.Equal(spec.HeightMm, all.Single(p => p.Name == "Road (top)").Max.Y, 3);
    }

    [Fact]
    public void Too_tall_an_arch_or_too_many_arches_says_what_to_change()
    {
        var tall = Make(Defaults.For(BridgeKind.Stone) with { ArchSpanMm = 60, LengthMm = 120 });
        Assert.False(tall.Ok);
        Assert.Contains(tall.Findings, f => f.Level == Severity.Bad && f.Text.Contains("raise the road"));
        var many = Make(Defaults.For(BridgeKind.Stone) with { Arches = 4 });
        Assert.Contains(many.Findings, f => f.Level == Severity.Bad && f.Text.Contains("take up too much"));
        var low = Make(Defaults.For(BridgeKind.Plank) with { RailMm = 3 });
        Assert.Contains(low.Findings, f => f.Level == Severity.Bad && f.Text.Contains("railing is too low"));
        var sags = Make(Defaults.For(BridgeKind.Rope) with { Ends = EndsOn.Table, HeightMm = 10, RiseMm = 9 });
        Assert.Contains(sags.Findings, f => f.Level == Severity.Bad && f.Text.Contains("sags onto the table"));
    }

    [Fact]
    public void Settings_survive_being_saved_with_the_design()
    {
        var spec = Defaults.For(BridgeKind.Stone) with { Arches = 3, Road = RoadShape.Humped, Custom = new CustomTexture { Name = "Mine", Path = "M0 0 L5 0 L5 5 Z" }, Texture = "Mine" };
        var back = BridgeSpec.FromJson(spec.ToJson());
        Assert.Equal(spec with { Custom = null }, back! with { Custom = null });
        Assert.Equal(spec.Custom, back.Custom);
        Assert.Null(BridgeSpec.FromJson("not json"));
    }

    [Fact]
    public void Matching_the_walls_takes_their_stone_and_sheet()
    {
        const string walls = """{"LengthMm":100,"ThicknessMm":3,"Texture":"Fieldstone","SizeA":9,"SizeB":6,"GrooveMm":0.9,"Seed":4,"FitMm":0.15}""";
        var look = WallsLink.Read(walls)!;
        var s = WallsLink.Fit(Defaults.For(BridgeKind.Stone) with { ThicknessMm = 2 }, look);
        Assert.Equal("Fieldstone", s.Texture);
        Assert.Equal(9, s.SizeA);
        Assert.Equal(6, s.SizeB);
        Assert.Equal(3, s.ThicknessMm);
        FitsTogether(Make(s));
    }

    [Fact]
    public void Scaling_for_other_figures_scales_the_sizes_but_not_the_sheet()
    {
        var s = Defaults.For(BridgeKind.Plank);
        var big = Defaults.Scaled(s, 54 / 28.0);
        Assert.Equal(Math.Round(s.LengthMm * 54 / 28, 1), big.LengthMm);
        Assert.Equal(s.ThicknessMm, big.ThicknessMm);
        Assert.True(Make(big with { ThicknessMm = 3, BeamMm = Math.Max(big.BeamMm, 4.5) }).Ok);
    }

    [Fact]
    public void Putting_it_in_the_design_lays_every_piece_out_on_the_bed_and_updating_replaces_them()
    {
        var spec = Defaults.For(BridgeKind.Plank) with { Count = 2 };
        var kit = Make(spec);
        var machine = MachineProfile.F2Ultra;
        var (s, sheets, id) = Placement.Place(DocState.New(), kit, spec, machine, new PlaceOptions());
        var all = Enumerable.Range(0, sheets).SelectMany(i => Pages.SwitchTo(s, i).Shapes).ToList();
        var cut = s.Layers.First(l => l.Name == Placement.CutLayer).Id;
        Assert.Equal(kit.PieceCount, all.Count(x => x.LayerId == cut));
        Assert.All(all, x => Assert.Equal(id.ToString(), x.AddonData[Placement.IdKey]));
        Assert.True(s.Layers.FindIndex(l => l.Name == Placement.LineLayer) < s.Layers.FindIndex(l => l.Name == Placement.CutLayer));
        Assert.All(s.Shapes, x => Assert.True(x.Bounds.MinX >= 0 && x.Bounds.MaxX <= machine.FieldW && x.Bounds.MinY >= 0 && x.Bounds.MaxY <= machine.FieldH));
        Assert.Equal(spec.ToJson(), Placement.Find(s, [s.Shapes[0].Id])!.Value.Spec.ToJson());

        var stone = Defaults.For(BridgeKind.Stone);
        var (s2, _, id2) = Placement.Place(s, Make(stone), stone, machine, new PlaceOptions { Replace = id });
        Assert.Equal(id, id2);
        Assert.DoesNotContain(s2.Shapes, x => x.Name.Contains("Plank"));
        Assert.Contains(s2.Shapes, x => x.Name.StartsWith("Side wall"));
        Assert.Contains(s2.Layers, l => l.Name == Placement.EtchLayer);
        Assert.Equal(BridgeKind.Stone, Placement.Find(s2, [s2.Shapes[0].Id])!.Value.Spec.Kind);
    }
}
