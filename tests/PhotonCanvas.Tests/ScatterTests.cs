using Clipper2Lib;
using PhotonCanvas.Addon.Scatter;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;

namespace PhotonCanvas.Tests;

public class ScatterTests
{
    static readonly ScatterSet Sheet = new() { Items = [], ThicknessMm = 2, FitMm = 0.1 };

    static ItemKit Make(ScatterItem it, ScatterSet? set = null) => ScatterKit.Make(it, set ?? Sheet);
    static Part Part(ItemKit k, string name) => k.Parts.Single(p => p.Name == name);

    public static IEnumerable<object[]> Kinds() => Defaults.Kinds.Select(k => new object[] { k.Kind });

    /// <summary>Every piece where it sits once put together, with its outline shrunk a hair so touching faces don't count.</summary>
    sealed record Placed(string Name, Pose Pose, PathsD Shrunk, PathsD Cut, double T)
    {
        public bool Inside(V3 p, double eps = 0.03)
        {
            var (u, v, w) = Pose.Local(p);
            if (Math.Abs(w) > T / 2 - eps) return false;
            return In(Shrunk, u, v);
        }

        public IEnumerable<V3> Corners()
        {
            foreach (var path in Cut)
                foreach (var q in path)
                    foreach (var w in new[] { -T / 2, T / 2 })
                        yield return Pose.At(q.x, q.y, w);
        }

        public (V3 Min, V3 Max) Box()
        {
            var pts = Corners().ToList();
            return (new(pts.Min(q => q.X), pts.Min(q => q.Y), pts.Min(q => q.Z)), new(pts.Max(q => q.X), pts.Max(q => q.Y), pts.Max(q => q.Z)));
        }
    }

    static bool In(PathsD area, double u, double v)
    {
        int inside = 0;
        foreach (var p in area)
            if (Clipper.PointInPolygon(new PointD(u, v), p) == PointInPolygonResult.IsInside) inside++;
        return inside % 2 == 1;
    }

    /// <summary>Every piece where it sits; push-fit ribs are taken off first (they squash flat as the joint goes together).</summary>
    static List<Placed> Assemble(ItemKit k, double T) =>
        [.. k.Parts.SelectMany(p =>
        {
            var cut = p.Ribs.Count > 0 ? Geo.Subtract(p.Cut, p.Ribs) : p.Cut;
            return p.Poses.Select(pose => new Placed(p.Name, pose, Geo.Offset(cut, -0.03), cut, T));
        })];

    /// <summary>Places where two pieces would be in the same space once put together (sampled every 0.1 mm where their boxes meet).</summary>
    static List<string> Clashes(List<Placed> all)
    {
        var found = new List<string>();
        for (int i = 0; i < all.Count; i++)
            for (int j = i + 1; j < all.Count; j++)
            {
                var (a0, a1) = all[i].Box();
                var (b0, b1) = all[j].Box();
                double x0 = Math.Max(a0.X, b0.X), y0 = Math.Max(a0.Y, b0.Y), z0 = Math.Max(a0.Z, b0.Z);
                double x1 = Math.Min(a1.X, b1.X), y1 = Math.Min(a1.Y, b1.Y), z1 = Math.Min(a1.Z, b1.Z);
                if (x1 - x0 < 0.06 || y1 - y0 < 0.06 || z1 - z0 < 0.06) continue;
                const double step = 0.1;
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
        foreach (var a in all)
            foreach (var hole in Geo.Tidy(a.Cut).Where(p => Clipper.Area(p) < 0))
            {
                var b = Clipper.GetBounds(hole);
                double w = b.right - b.left, h = b.bottom - b.top;
                bool slot = Math.Abs(Math.Min(w, h) - (T + fit)) < 0.02 || Math.Abs(Math.Max(w, h) - (T + fit)) < 0.02;
                if (!slot) continue;
                var c = a.Pose.At((b.left + b.right) / 2, (b.top + b.bottom) / 2);
                if (!all.Any(o => !ReferenceEquals(o, a) && o.Inside(c, 0.01))) empty.Add($"{a.Name}: slot at {(b.left + b.right) / 2:0.##}, {(b.top + b.bottom) / 2:0.##}");
            }
        return empty;
    }

    /// <summary>Pieces joined to the rest: some point of each is inside a slot, notch or hole of another (sampled), or they share a joint's space.</summary>
    static bool AllJoined(List<Placed> all, double T, bool glued = false)
    {
        if (all.Count < 2) return true;
        // Two pieces are joined when one goes through the other's plane inside the other's bounds, within a sheet's thickness: a tab in a slot.
        bool Joined(Placed a, Placed b)
        {
            var (a0, a1) = a.Box();
            var (b0, b1) = b.Box();
            double dx = Math.Min(a1.X, b1.X) - Math.Max(a0.X, b0.X), dy = Math.Min(a1.Y, b1.Y) - Math.Max(a0.Y, b0.Y), dz = Math.Min(a1.Z, b1.Z) - Math.Max(a0.Z, b0.Z);
            double[] d = [dx, dy, dz];
            Array.Sort(d);
            // A barrel's layers are glued face to face: they only touch.
            return d[0] > T * 0.4 || (glued && d[0] > -0.02 && d[1] > T * 0.4);
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

    static void FitsTogether(ItemKit k, double T, double fit, bool warnings = false)
    {
        Assert.True(k.Ok, string.Join("\n", k.Findings));
        Assert.NotEmpty(k.Parts);
        Assert.All(k.Parts, p => Assert.Equal(1, Geo.Pieces(p.Cut)));
        Assert.All(k.Parts, p => Assert.NotEmpty(p.Poses));
        var all = Assemble(k, T);
        Assert.Empty(Clashes(all));
        Assert.Empty(EmptySlots(all, T, fit));
        Assert.True(AllJoined(all, T, k.Item.Kind is ItemKind.Barrel or ItemKind.Stalagmite or ItemKind.Mushroom or ItemKind.Boulder or ItemKind.Urn), "a piece isn't joined to the others");
        if (!warnings) Assert.DoesNotContain(k.Findings, f => f.Level == Severity.Warn);
        // It stands on the table: nothing below it, something on it.
        double low = all.SelectMany(p => p.Corners()).Min(q => q.Y);
        Assert.InRange(low, -0.02, 0.05);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void Every_kind_makes_whole_pieces_that_fit_together_without_clashing(ItemKind kind)
    {
        FitsTogether(Make(Defaults.For(kind)), 2, 0.1);
    }

    public static IEnumerable<object[]> Variants()
    {
        foreach (var t in new[] { 1.5, 3.0 })
            foreach (var kind in Defaults.Kinds.Select(k => k.Kind))
                // A 28 mm cart's sides and well's walls are too low for a 3 mm sheet's finger joints: those are refused (below).
                yield return [t > 2 && kind is ItemKind.Cart or ItemKind.Well ? Defaults.Scaled(Defaults.For(kind), 1.5) : Defaults.For(kind), t];
        foreach (var s in Enum.GetValues<FenceStyle>())
            foreach (var feet in new[] { true, false })
                yield return [Defaults.For(ItemKind.Fence) with { Style = s, Feet = feet }, 2.0];
        yield return [Defaults.For(ItemKind.Fence) with { WidthMm = 100, Style = FenceStyle.Wattle }, 2.0];
        yield return [Defaults.For(ItemKind.Barrel) with { Open = true }, 2.0];
        yield return [Defaults.For(ItemKind.Barrel) with { Open = true, HeightMm = 20, WidthMm = 14 }, 1.5];
        yield return [Defaults.For(ItemKind.Barrel) with { Hoops = false, HeightMm = 30, WidthMm = 20 }, 2.0];
        yield return [Defaults.For(ItemKind.Cart) with { Shafts = false }, 2.0];
        yield return [Defaults.For(ItemKind.Cart) with { WheelMm = 24, WidthMm = 24, DepthMm = 34, HeightMm = 9 }, 2.0];
        yield return [Defaults.For(ItemKind.MarketStall) with { Scallops = false, Stripes = false, RiseMm = 0 }, 2.0];
        yield return [Defaults.For(ItemKind.MarketStall) with { WidthMm = 50, DepthMm = 24, HeightMm = 34, RiseMm = 10 }, 3.0];
        yield return [Defaults.For(ItemKind.Well) with { Roof = false }, 2.0];
        foreach (var tex in StoneFace.Presets.Select(p => p.Name))
            yield return [Defaults.For(ItemKind.Well) with { Texture = tex, Shingles = tex != "Plain" }, 2.0];
        yield return [Defaults.For(ItemKind.Well) with { WidthMm = 28, DepthMm = 22, TopMm = 45 }, 2.0];
        foreach (var shape in Enum.GetValues<StoneShape>())
            foreach (var slab in new[] { 0.0, 18.0 })
                yield return [Defaults.For(ItemKind.Gravestone) with { Shape = shape, SlabMm = slab, Weathered = slab > 0 }, 2.0];
        // The theme pieces' own settings.
        yield return [Defaults.For(ItemKind.Barrel) with { Metal = true }, 2.0];
        yield return [Defaults.For(ItemKind.Barrel) with { Metal = true, Open = true }, 2.0];
        foreach (var n in new[] { 1, 3, 5 })
            yield return [Defaults.For(ItemKind.Stalagmite) with { Cluster = n }, 2.0];
        foreach (var n in new[] { 1, 3 })
            yield return [Defaults.For(ItemKind.Mushroom) with { Cluster = n }, 2.0];
        yield return [Defaults.For(ItemKind.Brazier) with { Flames = false }, 2.0];
        yield return [Defaults.For(ItemKind.Brazier) with { WidthMm = 24, HeightMm = 30 }, 3.0];
        yield return [Defaults.For(ItemKind.Urn) with { Open = true }, 2.0];
        yield return [Defaults.For(ItemKind.Urn) with { Open = true, WidthMm = 20, HeightMm = 30 }, 1.5];
        yield return [Defaults.For(ItemKind.Boulder) with { WidthMm = 40, DepthMm = 30, HeightMm = 20 }, 3.0];
        yield return [Defaults.For(ItemKind.Palm) with { WidthMm = 20, DepthMm = 10, HeightMm = 30 }, 2.0];
        yield return [Defaults.For(ItemKind.Console) with { WidthMm = 40, DepthMm = 16, HeightMm = 30 }, 3.0];
        // Sized for other figures.
        foreach (var (k, t) in new[] { (15 / 28.0, 0.8), (54 / 28.0, 2.0), (54 / 28.0, 3.0) })
            foreach (var kind in Defaults.Kinds.Select(x => x.Kind))
                yield return [Defaults.Scaled(Defaults.For(kind), k), t];
    }

    [Theory]
    [MemberData(nameof(Variants))]
    public void Other_styles_sizes_and_sheets_fit_together_too(ScatterItem it, double t)
    {
        var set = Sheet with { ThicknessMm = t };
        // Weathering may leave a chip that narrows an edge: that's a warning, not a fault.
        FitsTogether(ScatterKit.Make(it, set), t, 0.1, warnings: it.Weathered || it.WidthMm < 6);
    }

    [Theory]
    [InlineData(ItemKind.Stalagmite)]
    [InlineData(ItemKind.Mushroom)]
    [InlineData(ItemKind.Boulder)]
    [InlineData(ItemKind.Palm)]
    [InlineData(ItemKind.Brazier)]
    public void Rock_and_plant_shapes_differ_with_the_variation_and_still_fit_together(ItemKind kind)
    {
        var shapes = new HashSet<double>();
        foreach (var seed in new[] { 2, 3, 4, 5, 6 })
        {
            var k = ScatterKit.Make(Defaults.For(kind) with { Cluster = 3 }, Sheet with { Seed = seed });
            FitsTogether(k, 2, 0.1);
            shapes.Add(Math.Round(k.Parts.Sum(p => Geo.Area(p.Cut) * p.Copies), 2));
        }
        if (kind != ItemKind.Brazier) Assert.True(shapes.Count > 1, "every variation came out the same");
    }

    [Fact]
    public void A_drum_is_a_metal_barrel_with_a_filler_cap_and_named_so()
    {
        var set = Sheet with { Items = [Defaults.For(ItemKind.Barrel) with { Metal = true }, Defaults.For(ItemKind.Barrel)] };
        Assert.Equal("Drum", ScatterKit.Label(set, 0));
        Assert.Equal("Barrel", ScatterKit.Label(set, 1));
        var drum = Part(Make(set.Items[0]), "Lid");
        var barrel = Part(Make(set.Items[1]), "Lid");
        Assert.Empty(drum.Etched);
        Assert.NotEmpty(barrel.Etched);
    }

    [Fact]
    public void A_brazier_is_two_halves_slotted_from_opposite_ends()
    {
        var it = Defaults.For(ItemKind.Brazier);
        var k = Make(it);
        Assert.Equal(2, k.Parts.Count);
        // The bowl is 0.35 of the width deep (14 mm across: 4.9 mm), under flames 0.3 of the height (6.6 mm): its middle is 12.95 mm up.
        double cx = it.WidthMm / 2, yj = 12.95;
        var fromTop = k.Parts.Single(p => p.Name.Contains("top")).Cut;
        var fromBottom = k.Parts.Single(p => p.Name.Contains("bottom")).Cut;
        Assert.True(In(fromTop, cx, yj - 0.6) && !In(fromTop, cx, yj + 0.6));
        Assert.True(!In(fromBottom, cx, yj - 0.6) && In(fromBottom, cx, yj + 0.6));
    }

    [Fact]
    public void A_console_screen_stands_in_slots_in_its_top()
    {
        var k = Make(Defaults.For(ItemKind.Console));
        var top = Part(k, "Top");
        Assert.Equal(2, Geo.Tidy(top.Cut).Count(p => Clipper.Area(p) < 0));
        Assert.NotEmpty(top.Etched);
        Assert.NotEmpty(Part(k, "Screen").Lines);
        Assert.DoesNotContain(k.Parts, p => p.Name == "Bottom");
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void A_push_fit_puts_grip_ribs_in_the_slots_and_still_fits_together(ItemKind kind)
    {
        var set = Sheet with { PushFit = true, GripMm = 0.15 };
        var k = ScatterKit.Make(Defaults.For(kind), set);
        FitsTogether(k, 2, 0.1);
        Assert.Contains(k.Parts, p => p.Ribs.Count > 0);
        // Each rib stands (fit + grip) / 2 into its slot, so the slot is the sheet less the grip.
        var ribbed = k.Parts.First(p => p.Ribs.Count > 0);
        var tall = ribbed.Ribs.Max(r => { var b = Clipper.GetBounds(r); return Math.Min(b.right - b.left, b.bottom - b.top); });
        Assert.Equal((0.1 + 0.15) / 2, tall, 1);
        Assert.Empty(ScatterKit.Make(Defaults.For(kind), Sheet).Parts.SelectMany(p => p.Ribs));
    }

    [Fact]
    public void A_barrel_is_stacked_from_layers_widest_in_the_middle_on_a_core()
    {
        var it = Defaults.For(ItemKind.Barrel) with { WidthMm = 12, HeightMm = 16 };
        var k = Make(it);
        var layers = k.Parts.Where(p => p.Name != "Core").SelectMany(p => p.Poses.Select(pose => (pose.O.Y, R: (Geo.Bounds(p.Cut).right - Geo.Bounds(p.Cut).left) / 2))).OrderBy(x => x.Y).ToList();
        Assert.Equal(8, layers.Count);
        Assert.Equal(6, layers.Max(l => l.R), 2);
        Assert.True(layers[0].R < layers[3].R && layers[^1].R < layers[4].R, "it bulges");
        var core = Part(k, "Core");
        Assert.Equal(6 * 2, Geo.Bounds(core.Cut).bottom - Geo.Bounds(core.Cut).top, 3);
        // An open barrel has a rim on top and water under it.
        var open = Make(it with { Open = true });
        Assert.Contains(open.Parts, p => p.Name == "Rim");
        Assert.NotEmpty(Part(open, "Water").Lines);
        Assert.NotEmpty(Part(k, "Lid").Lines);
    }

    [Fact]
    public void A_fence_has_a_foot_under_every_post_and_its_style_shows()
    {
        var it = Defaults.For(ItemKind.Fence) with { WidthMm = 70 };
        var k = Make(it);
        Assert.Equal(4, Part(k, "Foot").Copies); // 70 mm: three bays, four posts
        var fence = Part(k, "Fence");
        var b = Geo.Bounds(fence.Cut);
        Assert.Equal(70, b.right - b.left, 2);
        Assert.Equal(14, b.bottom - b.top, 2);
        // Pickets leave gaps; planks don't.
        double picket = Geo.Area(fence.Cut), planks = Geo.Area(Part(Make(it with { Style = FenceStyle.Planks }), "Fence").Cut);
        Assert.True(planks > picket * 1.4, $"{planks} vs {picket}");
        Assert.NotEmpty(Part(Make(it with { Style = FenceStyle.Wattle }), "Fence").Etched);
        Assert.DoesNotContain(Make(it with { Feet = false }).Parts, p => p.Name == "Foot");
    }

    [Fact]
    public void A_cart_stands_on_its_wheels_and_shafts_with_its_axle_through_the_hubs()
    {
        var k = Make(Defaults.For(ItemKind.Cart));
        var all = Assemble(k, 2);
        double Low(string name) => all.Where(p => p.Name == name).SelectMany(p => p.Corners()).Min(q => q.Y);
        Assert.Equal(0, Low("Wheel"), 1);
        Assert.InRange(Low("Beam and shaft"), 0, 0.3);
        Assert.Equal(2, Part(k, "Wheel").Copies);
        // Big wheels have their spokes cut out; small ones have them etched.
        Assert.True(Geo.Tidy(Part(Make(Defaults.For(ItemKind.Cart) with { WheelMm = 20 }), "Wheel").Cut).Count > 3);
        Assert.NotEmpty(Part(Make(Defaults.For(ItemKind.Cart) with { WheelMm = 8, HeightMm = 6 }, Sheet with { ThicknessMm = 1.5 }), "Wheel").Etched);
    }

    [Fact]
    public void A_stall_has_its_counter_at_the_height_asked_under_an_awning_that_slopes_back()
    {
        var it = Defaults.For(ItemKind.MarketStall);
        var k = Make(it);
        var all = Assemble(k, 2);
        var top = all.Single(p => p.Name == "Counter top");
        Assert.Equal(it.CounterMm + 1, top.Pose.O.Y, 3);
        var awning = all.Single(p => p.Name == "Awning");
        var pts = awning.Corners().ToList();
        // Higher at the back (small z) than at the front.
        Assert.True(pts.Where(q => q.Z < 2).Max(q => q.Y) > pts.Where(q => q.Z > it.DepthMm - 2).Max(q => q.Y) + 3);
        Assert.NotEmpty(Part(k, "Awning").Etched);
        Assert.Empty(Part(Make(it with { Stripes = false }), "Awning").Etched);
    }

    [Fact]
    public void A_well_reaches_the_top_asked_and_its_stonework_follows_the_texture()
    {
        var it = Defaults.For(ItemKind.Well);
        var k = Make(it);
        double high = Assemble(k, 2).SelectMany(p => p.Corners()).Max(q => q.Y);
        Assert.InRange(high, it.TopMm - 1, it.TopMm + 1);
        Assert.NotEmpty(Part(k, "Front wall").Etched);
        Assert.Empty(Part(Make(it with { Texture = StoneFace.Plain, Roof = false }), "Front wall").Etched);
        Assert.Equal(4, Make(it with { Roof = false }).Parts.Count);
        Assert.NotEqual(Geo.Area(Part(k, "Front wall").Etched), Geo.Area(Part(Make(it with { Texture = "Brick" }), "Front wall").Etched), 3);
    }

    [Fact]
    public void A_gravestone_stands_in_its_base_with_lettering_on_it()
    {
        var it = Defaults.For(ItemKind.Gravestone);
        var k = Make(it);
        var stone = Part(k, "Headstone");
        Assert.NotEmpty(stone.Lines);
        Assert.Empty(Part(Make(it with { Inscription = false }), "Headstone").Lines);
        var slab = Make(it with { SlabMm = 20 });
        var sb = Geo.Bounds(Part(slab, "Grave slab").Cut);
        Assert.True(sb.bottom - sb.top > 20);
        // The stone stands on top of the base.
        var all = Assemble(k, 2);
        Assert.Equal(2, all.Single(p => p.Name == "Headstone").Pose.O.Y, 3);
    }

    [Fact]
    public void Too_small_scenery_is_refused_not_squeezed()
    {
        Assert.False(Make(Defaults.For(ItemKind.Barrel) with { HeightMm = 4 }).Ok);
        Assert.False(Make(Defaults.For(ItemKind.Fence) with { HeightMm = 5 }).Ok);
        Assert.False(Make(Defaults.For(ItemKind.Cart) with { WheelMm = 4 }).Ok);
        Assert.False(Make(Defaults.For(ItemKind.MarketStall) with { CounterMm = 30 }).Ok);
        Assert.False(Make(Defaults.For(ItemKind.Well) with { TopMm = 14 }).Ok);
        Assert.False(Make(Defaults.For(ItemKind.Gravestone) with { HeightMm = 5 }).Ok);
        Assert.False(Make(Defaults.For(ItemKind.Cart) with { DepthMm = 0 }).Ok);
    }

    [Fact]
    public void Wood_detail_follows_the_setting()
    {
        var it = Defaults.For(ItemKind.Cart);
        Assert.Empty(Part(Make(it, Sheet with { Detail = WoodDetail.None }), "Front").Lines);
        Assert.NotEmpty(Part(Make(it, Sheet with { Detail = WoodDetail.Grain }), "Front").Lines);
        Assert.NotEmpty(Part(Make(it, Sheet with { Detail = WoodDetail.Planks }), "Front").Etched);
        // The same seed gives the same layout.
        Assert.Equal(Part(Make(it, Sheet with { Seed = 4 }), "Front").Lines.Sum(l => l.Count), Part(Make(it, Sheet with { Seed = 4 }), "Front").Lines.Sum(l => l.Count));
    }

    [Fact]
    public void Settings_round_trip_through_json()
    {
        var set = new ScatterSet
        {
            Items = [Defaults.For(ItemKind.Barrel) with { Count = 6, Open = true }, Defaults.For(ItemKind.Gravestone) with { Shape = StoneShape.Celtic, SlabMm = 15 }],
            ThicknessMm = 1.5, Detail = WoodDetail.Grain, Seed = 9,
        };
        var back = ScatterSet.FromJson(set.ToJson())!;
        Assert.Equal(set.ToJson(), back.ToJson());
        Assert.Equal(6, back.Items[0].Count);
        Assert.Contains("\"Celtic\"", set.ToJson());
        Assert.Null(ScatterSet.FromJson("{nope"));
    }

    [Fact]
    public void Placing_puts_every_piece_on_its_layers_and_can_replace_it()
    {
        var machine = MachineProfile.F2Ultra;
        var set = Sheet with { Items = [Defaults.For(ItemKind.Barrel) with { Count = 3 }, Defaults.For(ItemKind.Fence) with { Count = 2 }, Defaults.For(ItemKind.Gravestone)] };
        var kit = ScatterKit.Make(set);
        Assert.True(kit.Ok);
        var (s, sheets, id) = Placement.Place(DocState.New(), kit, set, machine, new PlaceOptions());
        var all = Enumerable.Range(0, sheets).SelectMany(i => Pages.SwitchTo(s, i).Shapes).ToList();
        var cut = s.Layers.First(l => l.Name == Placement.CutLayer).Id;
        Assert.Equal(kit.PieceCount, all.Count(x => x.LayerId == cut));
        Assert.Contains(all, x => x.Name.StartsWith("Fence: foot (2 of 6)"));
        Assert.True(s.Layers.FindIndex(l => l.Name == Placement.EtchLayer) < s.Layers.FindIndex(l => l.Name == Placement.CutLayer));
        Assert.True(s.Layers.FindIndex(l => l.Name == Placement.LineLayer) < s.Layers.FindIndex(l => l.Name == Placement.CutLayer));
        Assert.All(s.Shapes, x => Assert.True(x.Bounds.MinX >= 0 && x.Bounds.MaxX <= machine.FieldW && x.Bounds.MinY >= 0 && x.Bounds.MaxY <= machine.FieldH));
        Assert.Equal(set.ToJson(), Placement.Find(s, [s.Shapes[0].Id])!.Value.Spec.ToJson());

        var changed = set with { Items = [Defaults.For(ItemKind.Well)] };
        var (s2, _, id2) = Placement.Place(s, ScatterKit.Make(changed), changed, machine, new PlaceOptions { Replace = id });
        Assert.Equal(id, id2);
        Assert.DoesNotContain(s2.Shapes, x => x.Name.Contains("Barrel") || x.Name.Contains("Fence"));
        Assert.Equal(ItemKind.Well, Placement.Find(s2, [s2.Shapes[0].Id])!.Value.Spec.Items[0].Kind);
    }
}
