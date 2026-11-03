using Clipper2Lib;
using PhotonCanvas.Addon.GameKit;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;

namespace PhotonCanvas.Tests;

public class GameKitTests
{
    static readonly AccessorySet Sheet = new() { Items = [], ThicknessMm = 3, FitMm = 0.1 };

    static ItemKit Make(AccessoryItem it, AccessorySet? set = null, double max = GameKitMaker.DefaultMaxLength) => GameKitMaker.Make(it, set ?? Sheet, 0, max);
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

    static bool In(PathsD area, double u, double v)
    {
        int inside = 0;
        foreach (var p in area)
            if (Clipper.PointInPolygon(new PointD(u, v), p) == PointInPolygonResult.IsInside) inside++;
        return inside % 2 == 1;
    }

    static List<Placed> Assemble(ItemKit k, double T) =>
        [.. k.Parts.SelectMany(p => p.Poses.Select(pose => new Placed(p.Name, pose, Geo.Offset(p.Cut, -0.03), p.Cut, T)))];

    static bool Overlap((V3 Min, V3 Max) a, (V3 Min, V3 Max) b, double by = 0.05) =>
        Math.Min(a.Max.X, b.Max.X) - Math.Max(a.Min.X, b.Min.X) > by && Math.Min(a.Max.Y, b.Max.Y) - Math.Max(a.Min.Y, b.Min.Y) > by && Math.Min(a.Max.Z, b.Max.Z) - Math.Max(a.Min.Z, b.Min.Z) > by;

    /// <summary>Places where two pieces would be in the same space once put together: points all through each piece, tried in the others.</summary>
    static List<string> Clashes(List<Placed> all, double step)
    {
        var found = new List<string>();
        var boxes = all.Select(a => a.Box()).ToList();
        for (int i = 0; i < all.Count; i++)
        {
            var a = all[i];
            var others = Enumerable.Range(0, all.Count).Where(j => j != i && Overlap(boxes[i], boxes[j])).ToList();
            if (others.Count == 0) continue;
            var b = Geo.Bounds(a.Shrunk);
            var hits = new Dictionary<int, (int N, V3 First)>();
            for (double u = b.left + step / 2; u < b.right; u += step)
                for (double v = b.top + step / 2; v < b.bottom; v += step)
                {
                    if (!In(a.Shrunk, u, v)) continue;
                    foreach (var w in new[] { -a.T / 2 + 0.05, 0, a.T / 2 - 0.05 })
                    {
                        var p = a.Pose.At(u, v, w);
                        foreach (var j in others)
                        {
                            if (j < i) continue;
                            var (lo, hi) = boxes[j];
                            if (p.X < lo.X || p.Y < lo.Y || p.Z < lo.Z || p.X > hi.X || p.Y > hi.Y || p.Z > hi.Z) continue;
                            if (all[j].Inside(p)) hits[j] = hits.TryGetValue(j, out var h) ? (h.N + 1, h.First) : (1, p);
                        }
                    }
                }
            foreach (var (j, (n, f)) in hits) found.Add($"{a.Name} and {all[j].Name}: {n} points, first at {f.X:0.##}, {f.Y:0.##}, {f.Z:0.##}");
        }
        return found;
    }

    /// <summary>Holes with nothing through them (a slot with no tab in it, a pin hole with no pin).</summary>
    static List<string> EmptyHoles(List<Placed> all)
    {
        var empty = new List<string>();
        foreach (var a in all)
            foreach (var hole in Geo.Tidy(a.Cut).Where(p => Clipper.Area(p) < 0))
            {
                // Points all over the hole, in the middle of the sheet: something has to be through some of them.
                var b = Clipper.GetBounds(hole);
                double step = Math.Max(0.1, Math.Min(b.right - b.left, b.bottom - b.top) / 4);
                bool filled = false;
                for (double u = b.left + step / 2; u < b.right && !filled; u += step)
                    for (double v = b.top + step / 2; v < b.bottom && !filled; v += step)
                    {
                        if (Clipper.PointInPolygon(new PointD(u, v), hole) != PointInPolygonResult.IsInside) continue;
                        var c = a.Pose.At(u, v);
                        filled = all.Any(o => !ReferenceEquals(o, a) && o.Inside(c, 0.01));
                    }
                if (!filled) empty.Add($"{a.Name}: hole at {(b.left + b.right) / 2:0.##}, {(b.top + b.bottom) / 2:0.##}");
            }
        return empty;
    }

    /// <summary>Pieces joined to the rest (their boxes overlap another's by nearly a sheet's thickness every way: a tab in a slot).</summary>
    static bool AllJoined(List<Placed> all, double T)
    {
        if (all.Count < 2) return true;
        bool Joined(Placed a, Placed b)
        {
            var (a0, a1) = a.Box();
            var (b0, b1) = b.Box();
            double dx = Math.Min(a1.X, b1.X) - Math.Max(a0.X, b0.X), dy = Math.Min(a1.Y, b1.Y) - Math.Max(a0.Y, b0.Y), dz = Math.Min(a1.Z, b1.Z) - Math.Max(a0.Z, b0.Z);
            if (Math.Min(dx, Math.Min(dy, dz)) > T * 0.4) return true;
            // Layers glued flat on each other (the tracker's base): touching all over, not just at an edge.
            if (!a.Name.StartsWith("Base") || !b.Name.StartsWith("Base")) return false;
            double[] d = [dx, dy, dz];
            Array.Sort(d);
            return Math.Abs(d[0]) < 0.01 && d[1] > 2 * T;
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

    static void FitsTogether(ItemKit k, double t, bool joined = true)
    {
        Assert.True(k.Ok, string.Join("\n", k.Findings));
        Assert.NotEmpty(k.Parts);
        Assert.All(k.Parts, p => Assert.NotEmpty(p.Poses));
        Assert.All(k.Parts, p => Assert.Equal(1, Geo.Pieces(p.Cut)));
        var all = Assemble(k, t);
        double size = all.Max(p => { var (lo, hi) = p.Box(); return Math.Max(hi.X - lo.X, Math.Max(hi.Y - lo.Y, hi.Z - lo.Z)); });
        Assert.Empty(Clashes(all, size > 100 ? 0.35 : 0.15));
        if (k.Item.Kind != AccessoryKind.Template) Assert.Empty(EmptyHoles(all));
        if (joined) Assert.True(AllJoined(all, t), "a piece isn't joined to the others");
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void Every_kind_makes_whole_pieces_that_fit_together_without_clashing(AccessoryKind kind)
    {
        var k = Make(Defaults.For(kind));
        FitsTogether(k, 3);
        Assert.DoesNotContain(k.Findings, f => f.Level == Severity.Warn);
        Assert.Contains(k.Findings, f => f.Level == Severity.Note);
    }

    public static IEnumerable<object[]> Variants()
    {
        var tower = Defaults.For(AccessoryKind.DiceTower);
        yield return [tower with { Battlements = false }, 3.0];
        yield return [tower with { Baffles = 4, HeightMm = 190, DieMm = 18 }, 3.0];
        yield return [tower with { Baffles = 1, HeightMm = 90, TrayMm = 40 }, 2.0];
        yield return [tower with { WidthMm = 90, DepthMm = 80, HeightMm = 170, DieMm = 24, RimMm = 22 }, 4.0];
        var stand = Defaults.For(AccessoryKind.CardStand);
        yield return [stand with { Tiers = 1, HeightMm = 22 }, 3.0];
        yield return [stand with { Tiers = 3, DepthMm = 75, HeightMm = 45, Ribs = 4 }, 3.0];
        yield return [stand with { Ribs = 2, WidthMm = 120, SlotMm = 4 }, 2.0];
        var tray = Defaults.For(AccessoryKind.DeckTray);
        yield return [tray with { Compartments = 1 }, 3.0];
        yield return [tray with { Compartments = 3, CardWMm = 41, CardHMm = 63, HeightMm = 18 }, 2.0];
        var init = Defaults.For(AccessoryKind.Initiative);
        yield return [init with { Tiles = 5, Top = TileTop.Arched }, 2.0];
        yield return [init with { Tiles = 4, Top = TileTop.Square, TileWMm = 30, TileHMm = 25 }, 3.0];
        yield return [init with { Tiles = 12 }, 3.0];
        var gauge = Defaults.For(AccessoryKind.MoveGauge);
        yield return [gauge with { Units = Units.Centimetres, StepUnits = 2.5, Segments = 8 }, 2.0];
        yield return [gauge with { Units = Units.GridFeet, Segments = 3, WidthMm = 14 }, 3.0];
    }

    [Theory]
    [MemberData(nameof(Variants))]
    public void Other_sizes_and_sheets_fit_together_too(AccessoryItem it, double t) => FitsTogether(Make(it, Sheet with { ThicknessMm = t }), t);

    [Fact]
    public void The_fit_checks_see_a_tab_with_no_slot_and_a_slot_with_no_tab()
    {
        var k = Make(Defaults.For(AccessoryKind.DiceTower));
        // The left side with its slots filled in: every tab into it clashes.
        var solid = k with { Parts = [.. k.Parts.Select(p => p.Name == "Left side" ? p with { Cut = [.. Geo.Tidy(p.Cut).Where(q => Clipper.Area(q) > 0)] } : p)] };
        Assert.NotEmpty(Clashes(Assemble(solid, 3), 0.35));
        // Without its ramps, their slots are empty.
        var noRamps = k with { Parts = [.. k.Parts.Where(p => !p.Name.Contains("amp"))] };
        Assert.NotEmpty(EmptyHoles(Assemble(noRamps, 3)));
    }

    [Fact]
    public void The_dice_tower_ramps_zigzag_with_room_for_the_dice_and_the_bottom_one_leads_out_of_the_gate()
    {
        var it = Defaults.For(AccessoryKind.DiceTower) with { Baffles = 3, DieMm = 20 };
        var p = DiceTower.Plan(it, 3);
        Assert.Equal(3, p.Ramps.Count);
        double a = DiceTower.RampDeg * Math.PI / 180, hv = 3 / (2 * Math.Cos(a));
        for (int j = 0; j < p.Ramps.Count; j++)
        {
            var (zh, yh, zl, yl) = p.Ramps[j];
            Assert.True(yh > yl);
            Assert.Equal(Math.Tan(a), (yh - yl) / Math.Abs(zh - zl), 6);
            // They slope back and forth: the bottom one down to the front, where the gate is.
            Assert.Equal(j % 2 == 0, zh < zl);
            if (j > 0)
            {
                // A die rolling down the ramp below passes under this one's end with room to spare...
                var (bzh, byh, bzl, byl) = p.Ramps[j - 1];
                double below = byl + (byh - byl) * (zl - bzl) / (bzh - bzl);
                Assert.True(yl - hv - (below + hv) >= 1.2 * 20 - 1e-6);
                // ...and falls past its free end, which leaves a gap at the wall.
                double wall = zh < zl ? p.Z1 : p.Z0;
                Assert.True(Math.Abs(wall - zl) >= 1.15 * 20 - 1e-6);
            }
        }
        // The bottom ramp ends just over the floor, at the gate, and the gate is higher than a die.
        Assert.InRange(p.Ramps[0].YLow - p.FloorTop, 0, 3);
        Assert.True(p.GateTop - p.FloorTop > 20);
        var k = Make(it);
        Assert.Equal(2, Part(k, "Ramp").Copies);
        var all = Assemble(k, 3);
        // The tower is the size asked, the tray in front of it.
        Assert.Equal(150, all.Max(q => q.Box().Max.Y), 3);
        Assert.Equal(70, all.Max(q => q.Box().Max.X) - all.Min(q => q.Box().Min.X), 3);
        Assert.Equal(70 + 60, all.Max(q => q.Box().Max.Z) - all.Min(q => q.Box().Min.Z), 3);
        Assert.Equal(0, all.Min(q => q.Box().Min.Y), 3);
    }

    [Fact]
    public void A_tower_too_short_for_its_ramps_or_too_narrow_for_its_dice_is_refused()
    {
        var tower = Defaults.For(AccessoryKind.DiceTower);
        var tall = Make(tower with { Baffles = 6 });
        Assert.False(tall.Ok);
        Assert.Contains(tall.Findings, f => f.Text.Contains("taller"));
        Assert.False(Make(tower with { WidthMm = 30 }).Ok);
        Assert.False(Make(tower with { DieMm = 40 }).Ok);
        Assert.False(Make(tower with { RimMm = 4 }).Ok);
    }

    [Fact]
    public void Too_small_accessories_are_refused_not_squeezed()
    {
        foreach (var kind in Defaults.Kinds.Select(x => x.Kind))
        {
            var tiny = Defaults.For(kind) with { WidthMm = 4, DepthMm = 4, HeightMm = 3, CardWMm = 5, CardHMm = 5, TileWMm = 4, TileHMm = 5, StepUnits = 0.1, LengthUnits = 0.2 };
            Assert.False(Make(tiny).Ok, $"{kind} made tiny");
        }
    }

    [Fact]
    public void The_card_stands_slots_are_the_width_asked_and_lean_back()
    {
        var it = Defaults.For(AccessoryKind.CardStand) with { SlotMm = 3, Tiers = 2, Ribs = 3 };
        var k = Make(it);
        var rib = Part(k, "Rib");
        Assert.Equal(3, rib.Copies);
        var p = Cards.PlanStand(it, 3);
        // Across each slot, just under its top, the rib is open for the slot's width.
        double lean = Cards.LeanDeg * Math.PI / 180;
        for (int i = 0; i < 2; i++)
        {
            double top = p.RowHeights[i], zt = it.DepthMm - i * p.RowDepth - p.RowDepth * 0.55;
            double y = top - 2, zc = zt + 2 / Math.Cos(lean) * Math.Sin(lean);
            double open = 0;
            for (double z = zc - 4; z < zc + 4; z += 0.01) if (!In(rib.Cut, z, y)) open += 0.01;
            Assert.Equal(3 / Math.Cos(lean), open, 1);
        }
        // The back row stands higher than the front.
        Assert.True(p.RowHeights[1] > p.RowHeights[0] + 5);
    }

    [Fact]
    public void A_deck_tray_fits_the_cards_with_a_notch_in_each_pile()
    {
        var it = Defaults.For(AccessoryKind.DeckTray) with { Compartments = 2, CardWMm = 63, CardHMm = 88, HeightMm = 25 };
        var k = Make(it);
        var (W, H, D, pw, pd) = Cards.TraySize(it, 3);
        Assert.True(pw > 63 && pw < 66);
        Assert.True(pd > 88 && pd < 91);
        var all = Assemble(k, 3);
        Assert.Equal(W, all.Max(q => q.Box().Max.X) - all.Min(q => q.Box().Min.X), 3);
        Assert.Equal(D, all.Max(q => q.Box().Max.Z) - all.Min(q => q.Box().Min.Z), 3);
        Assert.Equal(H, all.Max(q => q.Box().Max.Y), 3);
        Assert.Equal(1, Part(k, "Divider").Copies);
        // Two finger notches dip into the front's top edge.
        var front = Part(k, "Front");
        Assert.True(Geo.Area(front.Cut) < W * H - 2 * 100);
        Assert.Equal(6, k.Parts.Count);
        Assert.True(pd < D);
    }

    [Fact]
    public void A_long_initiative_tracker_base_is_cut_in_pieces_with_the_joins_staggered()
    {
        var it = Defaults.For(AccessoryKind.Initiative) with { Tiles = 12, Names = "Aria\nBorin, Cass\n\nGoblin boss" };
        var k = Make(it, max: 150);
        Assert.True(k.Ok, string.Join("\n", k.Findings));
        var p = Initiative.Plan(it, 3, 0.1);
        Assert.True(p.Length > 150);
        var bases = k.Parts.Where(x => x.Name.StartsWith("Base")).ToList();
        Assert.All(bases, b => { var bb = Geo.Bounds(b.Cut); Assert.True(bb.right - bb.left <= 150 + 1e-6, b.Name); });
        // Joins (piece ends) in one layer are never at the same place as in the layer on it.
        var ends = bases.GroupBy(b => b.Poses[0].O.Y).OrderBy(g => g.Key)
            .Select(g => g.SelectMany(b => { var bb = Geo.Bounds(b.Cut); return new[] { bb.left, bb.right }; }).Where(x => x > 0.5 && x < p.Length - 0.5).Select(x => Math.Round(x, 1)).ToHashSet()).ToList();
        Assert.Equal(p.GroovedLayers + 1, ends.Count);
        for (int i = 1; i < ends.Count; i++) Assert.Empty(ends[i].Intersect(ends[i - 1]));
        Assert.Equal(["Aria", "Borin", "Cass", "4", "Goblin boss", "6"], Initiative.TileLabels(it).Take(6));
        // The groove is wider than the tiles, so they lift out easily.
        Assert.True(p.GrooveWidth > 3.3);
    }

    [Fact]
    public void A_movement_gauge_is_its_segments_long_and_its_pins_turn_in_their_holes()
    {
        var it = Defaults.For(AccessoryKind.MoveGauge) with { Segments = 6, StepUnits = 1, Units = Units.Inches };
        var k = Make(it);
        var all = Assemble(k, 3);
        var segs = all.Where(q => q.Name.StartsWith("Segment")).ToList();
        Assert.Equal(6, segs.Count);
        Assert.Equal(6 * 25.4, segs.Max(q => q.Box().Max.X) - segs.Min(q => q.Box().Min.X), 3);
        // Neighbours on different layers.
        for (int i = 1; i < segs.Count; i++) Assert.NotEqual(segs[i].Pose.O.Y, segs[i - 1].Pose.O.Y);
        var pin = Measures.Pin(3, 0.1);
        Assert.True(Math.Sqrt(pin.StemW * pin.StemW + 9) < pin.Hole);
        Assert.Equal(5, Part(k, "Pin").Copies);
        Assert.Equal(5, Part(k, "Pin cap").Copies);
        Assert.True(pin.HeadW > pin.Hole && pin.CapR * 2 > pin.Hole);
    }

    [Fact]
    public void Rulers_and_templates_are_the_size_marked()
    {
        var ruler = Part(Make(Defaults.For(AccessoryKind.Ruler) with { LengthUnits = 6, Units = Units.Inches }), "Ruler");
        var b = Geo.Bounds(ruler.Cut);
        Assert.Equal(6 * 25.4, b.right - b.left, 3);
        // Quarter-inch ticks: 6 × 4, less the one at the far end.
        Assert.Equal(23, ruler.Lines.Count);
        var cm = Part(Make(Defaults.For(AccessoryKind.Ruler) with { LengthUnits = 15, Units = Units.Centimetres }), "Ruler");
        Assert.Equal(149, cm.Lines.Count);

        var circle = Part(Make(Defaults.For(AccessoryKind.Template) with { LengthUnits = 3, Shape = TemplateShape.Circle }), "Circle template");
        var cb = Geo.Bounds(circle.Cut);
        Assert.Equal(6 * 25.4, cb.right - cb.left, 1);
        Assert.Equal(2, circle.Lines.Count);
        var cone = Part(Make(Defaults.For(AccessoryKind.Template) with { LengthUnits = 15, Units = Units.Centimetres, Shape = TemplateShape.Cone }), "Cone template");
        var nb = Geo.Bounds(cone.Cut);
        Assert.Equal(150, nb.right - nb.left, 3);
        Assert.Equal(150, nb.bottom - nb.top, 3);
        var square = Part(Make(Defaults.For(AccessoryKind.Template) with { LengthUnits = 2, Units = Units.GridFeet, Shape = TemplateShape.Square }), "Square template");
        // One line each way, stopped short of the size written where they cross.
        Assert.True(square.Lines.Count >= 2);
        Assert.InRange(square.Lines.Sum(Length), 2 * 50.8 * 0.6, 2 * 52.8 - 2);
        Assert.Equal("10", Defaults.Mark(Units.GridFeet, 2));
        Assert.Equal("2.5", Defaults.Mark(Units.Centimetres, 2.5));
        // A 5 inch radius circle is too big for the F2 Ultra's bed.
        var big = Make(Defaults.For(AccessoryKind.Template) with { LengthUnits = 5 });
        Assert.NotEmpty(Placement.TooBig(big, MachineProfile.F2Ultra));
    }

    static double Length(PathD l)
    {
        double d = 0;
        for (int i = 1; i < l.Count; i++) d += Math.Sqrt((l[i].x - l[i - 1].x) * (l[i].x - l[i - 1].x) + (l[i].y - l[i - 1].y) * (l[i].y - l[i - 1].y));
        return d;
    }

    [Fact]
    public void Numbers_and_names_are_etched_when_the_font_has_them()
    {
        var probe = Labels.Text("12", "Arial", 0, 0, 10, 6);
        if (probe.Count == 0) return; // no fonts on this machine
        var b = Geo.Bounds(probe);
        Assert.True(b.right - b.left <= 10 + 1e-6 && b.bottom - b.top <= 6 + 1e-6);
        Assert.Equal(0, (b.left + b.right) / 2, 3);
        var k = Make(Defaults.For(AccessoryKind.Initiative) with { Tiles = 2, Names = "Thorn" });
        Assert.All(k.Parts.Where(p => p.Name.StartsWith("Tile")), p => Assert.True(Geo.Area(p.Etched) > 5, p.Name));
        Assert.True(Geo.Area(Part(Make(Defaults.For(AccessoryKind.Ruler)), "Ruler").Etched) > 5);
        // Labels stay on the pieces.
        foreach (var p in k.Parts) Assert.Equal(0, Geo.Area(Geo.Subtract(p.Etched, p.Cut)), 3);
    }

    [Fact]
    public void The_finish_follows_the_setting_and_stays_on_the_faces()
    {
        var it = Defaults.For(AccessoryKind.DiceTower);
        var none = Make(it, Sheet with { Finish = Finish.None });
        Assert.All(none.Parts, p => { Assert.Empty(p.Etched); Assert.Empty(p.Lines); });
        foreach (var f in Enum.GetValues<Finish>().Where(f => f != Finish.None))
        {
            var k = Make(it, Sheet with { Finish = f });
            var side = Part(k, "Left side");
            Assert.True(Geo.Area(side.Etched) > 20 || side.Lines.Count > 10, f.ToString());
            foreach (var p in k.Parts) Assert.Equal(0, Geo.Area(Geo.Subtract(p.Etched, p.Cut)), 3);
            // Ramps and the floor are left plain.
            Assert.Empty(Part(k, "Floor").Etched);
        }
        // The same variation gives the same stones.
        Assert.Equal(Geo.Area(Part(Make(it, Sheet with { Seed = 4 }), "Back").Etched), Geo.Area(Part(Make(it, Sheet with { Seed = 4 }), "Back").Etched), 6);
    }

    [Fact]
    public void Settings_round_trip_through_json()
    {
        var set = new AccessorySet
        {
            Items = [Defaults.For(AccessoryKind.DiceTower) with { Count = 2, Baffles = 2 }, Defaults.For(AccessoryKind.Template) with { Shape = TemplateShape.Cone, Units = Units.GridFeet }, Defaults.For(AccessoryKind.Initiative) with { Names = "A\nB" }],
            ThicknessMm = 2, Finish = Finish.Fieldstone, Seed = 9, TextureScale = 1.5,
        };
        var back = AccessorySet.FromJson(set.ToJson())!;
        Assert.Equal(set.ToJson(), back.ToJson());
        Assert.Equal(2, back.Items[0].Count);
        Assert.Contains("\"Cone\"", set.ToJson());
        Assert.Null(AccessorySet.FromJson("{nope"));
    }

    [Fact]
    public void Placing_puts_every_piece_on_its_layers_and_can_replace_it()
    {
        var machine = MachineProfile.F2Ultra;
        var set = Sheet with { Items = [Defaults.For(AccessoryKind.CardStand), Defaults.For(AccessoryKind.MoveGauge) with { Count = 2 }, Defaults.For(AccessoryKind.Ruler)] };
        var kit = GameKitMaker.Make(set);
        Assert.True(kit.Ok);
        var (s, sheets, id) = Placement.Place(DocState.New(), kit, set, machine, new PlaceOptions());
        var all = Enumerable.Range(0, sheets).SelectMany(i => Pages.SwitchTo(s, i).Shapes).ToList();
        var cut = s.Layers.First(l => l.Name == Placement.CutLayer).Id;
        Assert.Equal(kit.PieceCount, all.Count(x => x.LayerId == cut));
        Assert.Equal(3 + 2 + (6 + 5 + 5) * 2 + 1, kit.PieceCount);
        Assert.Contains(all, x => x.Name.StartsWith("Movement gauge: pin (2 of 10)"));
        Assert.True(s.Layers.FindIndex(l => l.Name == Placement.LineLayer) < s.Layers.FindIndex(l => l.Name == Placement.CutLayer));
        Assert.All(s.Shapes, x => Assert.True(x.Bounds.MinX >= 0 && x.Bounds.MaxX <= machine.FieldW && x.Bounds.MinY >= 0 && x.Bounds.MaxY <= machine.FieldH));
        Assert.Equal(set.ToJson(), Placement.Find(s, [s.Shapes[0].Id])!.Value.Spec.ToJson());

        var changed = set with { Items = [Defaults.For(AccessoryKind.Template) with { Count = 2 }] };
        var (s2, _, id2) = Placement.Place(s, GameKitMaker.Make(changed), changed, machine, new PlaceOptions { Replace = id });
        Assert.Equal(id, id2);
        Assert.DoesNotContain(s2.Shapes, x => x.Name.Contains("gauge") || x.Name.Contains("Ruler") || x.Name.Contains("Rib"));
        Assert.Equal(AccessoryKind.Template, Placement.Find(s2, [s2.Shapes[0].Id])!.Value.Spec.Items[0].Kind);
    }
}
