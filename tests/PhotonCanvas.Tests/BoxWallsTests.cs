using Clipper2Lib;
using PhotonCanvas.Addon.Walls;
using PhotonCanvas.Addon.Walls.Model;

namespace PhotonCanvas.Tests;

/// <summary>Walls built as boxes, and the boxed and stacked columns they go into.</summary>
public class BoxWallsTests
{
    static WallSpec Box(Func<WallSpec, WallSpec>? change = null)
    {
        var s = WallSpec.NewBoxed with { LengthMm = 100, HeightMm = 50, Texture = Textures.Plain, Connectors = new() { [ConnectorKind.Corner] = 2 } };
        return change is null ? s : change(s);
    }

    static Part Part(WallKit k, string name) => k.Parts.Single(p => p.Name == name);
    static string Why(WallKit k) => string.Join("\n", k.Findings);

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

    static bool In(PathsD area, double u, double v) => OutlineGrid.In(area, u, v);

    static List<Placed> Assemble(WallKit k, double T) =>
        [.. k.Parts.SelectMany(p => p.Poses.Select(pose => new Placed(p.Name, pose, Geo.Offset(p.Cut, -0.03), p.Cut, T)))];

    /// <summary>Places where two pieces would be in the same space once put together (sampled where their boxes meet).</summary>
    static List<string> Clashes(List<Placed> all, double step = 0.25)
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

    /// <summary>Slots (a side as wide as the sheet plus the fit) with nothing through them once it's put together.</summary>
    static List<string> EmptySlots(List<Placed> all, double T, double fit)
    {
        var empty = new List<string>();
        // A column's slots for walls that aren't in the view stay empty.
        foreach (var a in all.Where(p => !p.Name.Contains("column")))
            foreach (var hole in Geo.Tidy(a.Cut).Where(p => Clipper.Area(p) < 0))
            {
                var b = Clipper.GetBounds(hole);
                double w = b.right - b.left, h = b.bottom - b.top;
                if (Math.Abs(Math.Min(w, h) - (T + fit)) > 0.02) continue;
                var c = a.Pose.At((b.left + b.right) / 2, (b.top + b.bottom) / 2);
                if (!all.Any(o => !ReferenceEquals(o, a) && o.Inside(c, 0.01))) empty.Add($"{a.Name}: slot at {(b.left + b.right) / 2:0.##}, {(b.top + b.bottom) / 2:0.##}");
            }
        return empty;
    }

    [Fact]
    public void A_box_wall_has_a_front_back_base_top_ends_and_ribs()
    {
        var k = WallKitMaker.Make(Box());
        Assert.True(k.Ok, Why(k));
        foreach (var name in new[] { "Front", "Back", "Base", "Top", "End", "Rib" }) Assert.Contains(k.Parts, p => p.Name == name);
        Assert.Equal(2, Part(k, "End").Copies);
        Assert.Equal("Front", k.Front!.Name);
        // The faces: as long as the wall, from the base to the top, with tabs below and at the ends.
        var b = Geo.Bounds(Part(k, "Front").Cut);
        Assert.Equal(0, b.top, 2);
        Assert.Equal(50, b.bottom, 2);
        Assert.Equal(-BoxWall.TabLength(Box()), b.left, 2);
        // The top plate fits between the faces.
        var top = Geo.Bounds(Part(k, "Top").Cut);
        Assert.Equal(12 - 2 * 3, top.bottom - top.top, 2);
        Assert.Equal(12, k.WallThicknessMm);
    }

    [Theory]
    [InlineData(TopStyle.Straight, false)]
    [InlineData(TopStyle.Battlements, false)]
    [InlineData(TopStyle.Battlements, true)]
    [InlineData(TopStyle.Ruined, false)]
    public void Nothing_runs_into_anything_else_once_put_together(TopStyle top, bool walkway)
    {
        var s = Box(x => x with
        {
            LengthMm = 110, HeightMm = 64, RuinDepth = 0.3, Top = top, Walkway = walkway, WalkwayDepthMm = 14, Door = DoorKind.DoorwayAndDoor, Windows = 2, SillMm = 22, WindowHeightMm = 10,
            Mounts = 1, MountHeightMm = 14, Accessories = Accessory.Pegs, Connectors = new() { [ConnectorKind.Corner] = 1, [ConnectorKind.Tee] = 1 },
        });
        var k = WallKitMaker.Make(s);
        Assert.True(k.Ok, Why(k));
        var all = Assemble(k, 3);   // the door hangs in its doorway
        Assert.Contains(all, p => p.Name == "Door");
        Assert.Empty(Clashes(all));
        Assert.Empty(EmptySlots(all, 3, s.FitMm));
    }

    [Fact]
    public void Every_tab_on_the_faces_goes_into_a_slot()
    {
        var s = Box();
        var k = WallKitMaker.Make(s);
        var all = Assemble(k, 3);
        var faces = all.Where(p => p.Name is "Front" or "Back").ToList();
        Assert.Equal(2, faces.Count);
        foreach (var f in faces)
        {
            // Each tab's middle (below the base's top, or past the ends) is in another piece's slot: in no other piece.
            var outline = Geo.Tidy(f.Cut).Where(p => Clipper.Area(p) > 0).ToList();
            var tabs = Geo.Subtract(new PathsD(outline), [Geo.Rect(0, s.ThicknessMm, WallKitMaker.PanelLength(s), 100)]);
            Assert.True(tabs.Count >= 4, $"{f.Name}: {tabs.Count} tabs");
            foreach (var t in tabs)
            {
                var b = Clipper.GetBounds(t);
                var c = f.Pose.At((b.left + b.right) / 2, (b.top + b.bottom) / 2);
                var inside = all.Where(o => !ReferenceEquals(o, f) && o.Inside(c, 0.01)).Select(o => o.Name).ToList();
                Assert.True(inside.Count == 0, $"{f.Name} tab at {(b.left + b.right) / 2:0.#}, {(b.top + b.bottom) / 2:0.#} runs into {string.Join(", ", inside)}");
                // And it is in a piece's outline (a slot), not in the open.
                Assert.Contains(all, o => !ReferenceEquals(o, f) && Holds(o, c));
            }
        }
    }

    /// <summary>The point is in a hole of the piece (a slot or a notch), inside its sheet's thickness.</summary>
    static bool Holds(Placed o, V3 p)
    {
        var (u, v, w) = o.Pose.Local(p);
        if (Math.Abs(w) > o.T / 2) return false;
        var b = Geo.Bounds(o.Cut);
        return u > b.left && u < b.right && v > b.top && v < b.bottom && !In(o.Cut, u, v);
    }

    [Fact]
    public void Columns_too_narrow_for_the_wall_are_refused()
    {
        var k = WallKitMaker.Make(Box(x => x with { ColumnMm = 16 }));
        Assert.False(k.Ok);
        Assert.Contains(k.Findings, f => f.Level == Severity.Bad && f.Text.Contains("at least"));
    }

    [Fact]
    public void A_wall_thinner_than_three_panels_is_refused()
    {
        var k = WallKitMaker.Make(Box(x => x with { WallMm = 8 }));
        Assert.False(k.Ok);
        Assert.Empty(k.Parts);
    }

    [Fact]
    public void Room_between_the_faces_that_the_sheet_doesnt_fill_is_mentioned()
    {
        var k = WallKitMaker.Make(Box(x => x with { WallMm = 13.5 }));
        Assert.True(k.Ok, Why(k));
        Assert.Contains(k.Findings, f => f.Level == Severity.Warn && f.Text.Contains("fits exactly"));
        Assert.DoesNotContain(WallKitMaker.Make(Box()).Findings, f => f.Text.Contains("fits exactly"));
    }

    [Theory]
    [InlineData(ColumnTop.Flush, ColumnBase.Flat)]
    [InlineData(ColumnTop.Cap, ColumnBase.Foot)]
    [InlineData(ColumnTop.SteppedCap, ColumnBase.SteppedPlinth)]
    [InlineData(ColumnTop.SteppedPoint, ColumnBase.Foot)]
    [InlineData(ColumnTop.Battlements, ColumnBase.SteppedPlinth)]
    public void Boxed_columns_take_the_walls_whatever_their_top_and_bottom(ColumnTop top, ColumnBase bottom)
    {
        var s = Box(x => x with { ColumnTop = top, ColumnBase = bottom, Connectors = new() { [ConnectorKind.Cross] = 1, [ConnectorKind.EndCap] = 1 } });
        var k = WallKitMaker.Make(s);
        Assert.True(k.Ok, Why(k));
        Assert.Contains(k.Parts, p => p.Name == "Cross column side");
        // A side no wall comes into has no slots.
        var end = k.Parts.Single(p => p.Name == "End column blank side");
        Assert.DoesNotContain(Geo.Tidy(end.Cut), h => Clipper.Area(h) < 0);
        Assert.Contains(k.Parts, p => p.Name == "End column side");
        var all = Assemble(k, 3);
        Assert.Empty(Clashes(all));
        // The column is as tall as its top makes it.
        double highest = all.Where(p => p.Name.Contains("column")).Max(p => p.Box().Max.Y);
        Assert.Equal(WallKitMaker.ColumnTopMm(s), highest, 1);
    }

    [Theory]
    [InlineData(ConnectorKind.Angle135)]
    [InlineData(ConnectorKind.Sharp45)]
    [InlineData(ConnectorKind.Custom)]
    public void Angled_columns_are_stacks_of_layers_with_slots_for_the_faces(ConnectorKind kind)
    {
        // Walls 12 mm thick meeting at 45° only clear each other 14.5 mm out from the middle.
        var s = Box(x => x with { ColumnMm = kind == ConnectorKind.Sharp45 ? 40 : 30, Connectors = new() { [kind] = 2 } });
        var k = WallKitMaker.Make(s);
        Assert.True(k.Ok, Why(k));
        var layer = k.Parts.Single(p => p.Name.EndsWith("column layer"));
        // Two slots for each wall (a face each): four holes or notches.
        Assert.Equal(1, Geo.Pieces(layer.Cut));
        Assert.Empty(Clashes(Assemble(k, 3)));
    }

    [Fact]
    public void Stacked_columns_can_be_picked_for_square_corners_too()
    {
        var k = WallKitMaker.Make(Box(x => x with { ColumnBuild = ColumnBuild.Stacked, ColumnMm = 20 }));
        Assert.True(k.Ok, Why(k));
        Assert.Contains(k.Parts, p => p.Name == "Corner column layer");
        Assert.DoesNotContain(k.Parts, p => p.Name.Contains("column side"));
    }

    [Fact]
    public void A_walkway_reaches_back_over_the_back_face_on_brackets()
    {
        var k = WallKitMaker.Make(Box(x => x with { HeightMm = 60, Top = TopStyle.Battlements, Walkway = true, WalkwayDepthMm = 20 }));
        Assert.True(k.Ok, Why(k));
        var walk = Geo.Bounds(Part(k, "Walkway").Cut);
        Assert.Equal(20 + 3, walk.bottom - walk.top, 1);   // 20 mm behind the parapet's backing, plus under the backing
        Assert.Contains(k.Parts, p => p.Name == "Walkway bracket");
        Assert.Contains(k.Parts, p => p.Name == "Parapet backing");
        // The back face stops under the walkway.
        Assert.True(Geo.Bounds(Part(k, "Back").Cut).bottom < Geo.Bounds(Part(k, "Front").Cut).bottom - 10);
    }

    [Fact]
    public void A_broken_top_has_layers_between_the_faces_no_higher_than_either_face()
    {
        var s = Box(x => x with { Top = TopStyle.Ruined, RuinDepth = 0.5, Texture = "Brick", SizeA = 8, SizeB = 3.5 });
        var k = WallKitMaker.Make(s);
        Assert.True(k.Ok, Why(k));
        var layers = k.Parts.Where(p => p.Name == "Broken top layer").ToList();
        Assert.NotEmpty(layers);
        Assert.Equal(BoxWall.CoreLayers(s), layers.Sum(p => p.Copies));
        double frontTop = Geo.Bounds(Part(k, "Front").Cut).bottom;
        Assert.All(layers, l => Assert.True(Geo.Bounds(l.Cut).bottom <= frontTop + 1e-6));
    }

    [Fact]
    public void Doors_and_windows_are_lined_between_the_faces()
    {
        var k = WallKitMaker.Make(Box(x => x with { Door = DoorKind.Doorway, Windows = 2, SillMm = 22, WindowHeightMm = 10 }));
        Assert.True(k.Ok, Why(k));
        var linings = k.Parts.Where(p => p.Name.StartsWith("Lining")).ToList();
        Assert.Equal(3, linings.Count);
        Assert.All(linings, l => Assert.Equal(BoxWall.CoreLayers(Box()), l.Copies));
    }

    [Fact]
    public void A_window_through_the_top_plate_splits_it()
    {
        var k = WallKitMaker.Make(Box(x => x with { Windows = 1, SillMm = 40, WindowHeightMm = 8, WindowShape = OpeningShape.Rectangle, FrameMm = 0 }));
        Assert.True(k.Ok, Why(k));
        Assert.Equal(2, k.Parts.Where(p => p.Name == "Top").Sum(p => p.Copies));
    }

    [Fact]
    public void Both_faces_are_textured_and_the_back_can_be_left_plain()
    {
        var s = Box(x => x with { Texture = "Brick", SizeA = 8, SizeB = 3.5, GrooveMm = 0.6 });
        var k = WallKitMaker.Make(s);
        Assert.True(Geo.Area(Part(k, "Front").Etched) > 300);
        Assert.True(Geo.Area(Part(k, "Back").Etched) > 300);
        Assert.Empty(Part(k, "Top").Etched);
        var plainBack = WallKitMaker.Make(s with { TextureBack = false });
        Assert.Empty(Part(plainBack, "Back").Etched);
        var sides = WallKitMaker.Make(s with { TextureSides = true, EndsIntoColumns = false });
        Assert.True(Geo.Area(Part(sides, "Top").Etched) > 20);
        Assert.True(Geo.Area(Part(sides, "End").Etched) > 20);
        // Nothing etched outside a piece.
        foreach (var p in sides.Parts) Assert.Equal(0, Geo.Area(Geo.Subtract(p.Etched, p.Cut)), 1);
    }

    [Fact]
    public void Closed_ends_have_no_tabs_and_the_plinth_goes_round_them()
    {
        var k = WallKitMaker.Make(Box(x => x with { EndsIntoColumns = false, PlinthMm = 2, Connectors = [] }));
        Assert.True(k.Ok, Why(k));
        var front = Geo.Bounds(Part(k, "Front").Cut);
        Assert.Equal(0, front.left, 2);
        Assert.Equal(100, front.right, 2);
        var bp = Geo.Bounds(Part(k, "Base").Cut);
        Assert.Equal(104, bp.right - bp.left, 2);
        Assert.Equal(16, bp.bottom - bp.top, 2);
        Assert.Empty(Clashes(Assemble(k, 3)));
    }

    [Fact]
    public void Centre_to_centre_lengths_allow_for_half_a_column_each_end()
    {
        var s = Box(x => x with { LengthMm = 120, Measure = LengthMeasure.Centres });
        Assert.Equal(120 - 22, WallKitMaker.PanelLength(s), 6);
        Assert.Equal(11, WallKitMaker.CentreToEnd(s), 6);
    }

    [Fact]
    public void The_new_settings_survive_saving_and_old_walls_stay_flat()
    {
        var s = Box(x => x with { WallMm = 15, PlinthMm = 0, ColumnTop = ColumnTop.SteppedPoint, ColumnBase = ColumnBase.SteppedPlinth, TextureSides = true });
        Assert.Equal(s.ToJson(), WallSpec.FromJson(s.ToJson())!.ToJson());
        var old = WallSpec.FromJson("""{"LengthMm":80,"HeightMm":40,"ThicknessMm":3}""")!;
        Assert.Equal(WallBuild.Flat, old.Build);
        Assert.Equal("Wall", WallKitMaker.Make(old).Parts[0].Name);
    }

    [Fact]
    public void Flat_walls_are_shown_put_together_too()
    {
        var k = WallKitMaker.Make(new WallSpec { Texture = Textures.Plain, Door = DoorKind.DoorwayAndDoor, Mounts = 2, Accessories = Accessory.Sconces, Connectors = new() { [ConnectorKind.Corner] = 2 } });
        Assert.True(k.Ok, Why(k));
        Assert.Single(Part(k, "Wall").Poses);
        Assert.Equal(2, Part(k, "Torch sconce").Poses.Count);
        Assert.Contains(k.Parts, p => p.Name.StartsWith("Corner column") && p.Poses.Count > 0);
        Assert.Empty(Clashes(Assemble(k, 3)));
    }

    [Fact]
    public void Roofs_fit_box_walls_and_their_column_caps()
    {
        var w = Box(x => x with { ColumnTop = ColumnTop.SteppedCap, ColumnLipMm = 2 });
        var r = PhotonCanvas.Addon.Roofs.WallsFit.Fit(new PhotonCanvas.Addon.Roofs.RoofSpec(), w);
        Assert.Equal(w.WallMm, r.WallMm);
        Assert.Equal(w.ColumnMm + 8, r.CornerMm, 2);
        Assert.Equal(BoxColumns.TopMm(w) - w.HeightMm, r.RestAboveWallsMm, 2);
        // A tower's slots take the whole box wall, deep enough for the tabs on its faces' ends.
        var t = PhotonCanvas.Addon.Roofs.WallsFit.Fit(new PhotonCanvas.Addon.Roofs.TowerSpec(), w);
        Assert.Equal(w.WallMm, t.WallMm);
        Assert.Equal(BoxWall.TabLength(w) + WallKitMaker.EndClearMm, t.SlotDepthMm, 2);
        Assert.True(PhotonCanvas.Addon.Roofs.TowerMaker.Make(t).Ok);
    }

    // ---- broken tops and merlon heights ----

    public static TheoryData<RuinStyle, bool, bool> Breaks()
    {
        var d = new TheoryData<RuinStyle, bool, bool>();
        foreach (var style in Enum.GetValues<RuinStyle>().Where(r => r != RuinStyle.None))
        {
            d.Add(style, false, false);
            d.Add(style, true, false);
        }
        d.Add(RuinStyle.Crumbled, true, true);
        d.Add(RuinStyle.Breach, true, true);
        return d;
    }

    [Theory]
    [MemberData(nameof(Breaks))]
    public void Every_broken_top_goes_together_with_or_without_battlements(RuinStyle style, bool battlements, bool walkway)
    {
        var s = Box(x => x with
        {
            LengthMm = 110, HeightMm = 64, RuinDepth = 0.4, Texture = "Brick", SizeA = 8, SizeB = 3.5, Broken = style,
            Top = battlements ? TopStyle.Battlements : TopStyle.Straight, Merlons = MerlonPattern.Alternating, Walkway = walkway, WalkwayDepthMm = 14,
            Windows = 2, SillMm = 18, WindowHeightMm = 8, Connectors = new() { [ConnectorKind.Corner] = 1, [ConnectorKind.Tee] = 1 },
        });
        var k = WallKitMaker.Make(s);
        Assert.True(k.Ok, Why(k));
        var front = Geo.Bounds(Part(k, "Front").Cut);
        var all = Assemble(k, 3);
        Assert.Empty(Clashes(all));
        Assert.Empty(EmptySlots(all, 3, s.FitMm));
        if (walkway) Assert.Contains(k.Parts, p => p.Name == "Walkway");
        // Broken: somewhere along the front is well below the top.
        var under = Geo.Intersect(Part(k, "Front").Cut, [Geo.Rect(5, 0, 105, 64)]);
        Assert.True(Geo.Area(under) < 100 * 64 - 150, $"{style}: hardly broken");
        Assert.True(front.bottom <= 64 + 1e-6);
    }

    [Theory]
    [InlineData(RuinStyle.Crumbled)]
    [InlineData(RuinStyle.Breach)]
    [InlineData(RuinStyle.Weathered)]
    public void A_break_in_brick_follows_the_joints(RuinStyle style)
    {
        var s = Box(x => x with { LengthMm = 120, HeightMm = 60, Texture = "Brick", SizeA = 8, SizeB = 3.5, GrooveMm = 0.6, Wear = 0, Broken = style, RuinDepth = 0.4 });
        var stones = Textures.Make(s, new Face(120, 60, 0, 120, 60)).Raised;
        var p = Tops.Break(s, Profile.Level(0, 120, 60), 120, 3, 60, 10, 0, 1, stones, out _);
        // Where the top is level and broken, it runs along a joint between rows of bricks (half a joint above a brick's top).
        var tops = stones.Select(st => Clipper2Lib.Clipper.GetBounds(st).bottom).Distinct().ToList();
        double level = 0, onJoint = 0;
        foreach (var seg in p.Segs.Where(g => Math.Abs(g.Y0 - g.Y1) < 1e-6 && g.Y0 < 59.5 && g.X1 - g.X0 > 0.2))
        {
            level += seg.X1 - seg.X0;
            if (tops.Any(t => Math.Abs(seg.Y0 - (t + 0.35)) < 0.3)) onJoint += seg.X1 - seg.X0;
        }
        Assert.True(level > 20, $"broken along only {level:0.#} mm");
        Assert.True(onJoint > 0.9 * level, $"{onJoint:0.#} of {level:0.#} mm on the joints");
        // And its ends are upright, not sloping across a brick.
        Assert.DoesNotContain(p.Segs, g => Math.Abs(g.Y1 - g.Y0) > 0.5 && g.X1 - g.X0 > 0.1);
    }

    [Fact]
    public void Planks_and_shattered_tops_break_in_a_jagged_line()
    {
        foreach (var (tex, style) in new[] { ("Wood planks (across)", RuinStyle.Crumbled), ("Brick", RuinStyle.Shattered) })
        {
            var preset = Textures.Preset(tex)!;
            var s = Box(x => x with { LengthMm = 120, HeightMm = 60, Texture = tex, SizeA = preset.SizeA, SizeB = preset.SizeB, Broken = style });
            var k = WallKitMaker.Make(s);
            Assert.True(k.Ok, Why(k));
            var front = Part(k, "Front").Cut;
            // Sloping edges along the break.
            Assert.Contains(front.SelectMany(path => path.Zip(path.Skip(1))), e => Math.Abs(e.First.x - e.Second.x) > 0.5 && Math.Abs(e.First.y - e.Second.y) > 0.5);
        }
    }

    [Fact]
    public void A_fallen_end_stands_at_one_end_and_falls_away_at_the_other()
    {
        var s = Box(x => x with { LengthMm = 120, HeightMm = 60, Texture = "Brick", SizeA = 8, SizeB = 3.5, Broken = RuinStyle.FallenEnd, RuinAt = RuinPlace.Left, RuinDepth = 0.6 });
        var k = WallKitMaker.Make(s);
        Assert.True(k.Ok, Why(k));
        var front = Part(k, "Front").Cut;
        double Height(double x0, double x1) => Geo.Bounds(Geo.Intersect(front, [Geo.Rect(x0, 0, x1, 70)])).bottom;
        Assert.True(Height(8, 20) < 45, $"left end {Height(8, 20):0.#} mm high");
        Assert.True(Height(100, 112) > 58);
        // Its tabs into the columns are kept low, below the fallen end.
        Assert.True(BoxWall.EndTabs(s).Max(t => t.Y1) < Height(0, 3));
        Assert.Empty(Clashes(Assemble(k, 3)));
    }

    [Fact]
    public void A_deep_break_on_a_low_wall_is_made_shallower()
    {
        var s = Box(x => x with { HeightMm = 30, Broken = RuinStyle.Breach, RuinDepth = 0.8, Texture = "Brick", SizeA = 8, SizeB = 3.5 });
        var k = WallKitMaker.Make(s);
        Assert.True(k.Ok, Why(k));
        Assert.Contains(k.Findings, f => f.Text.Contains("as far as a box wall this high allows"));
        Assert.True(Geo.Bounds(Part(k, "Top").Cut).bottom - Geo.Bounds(Part(k, "Top").Cut).top > 0);
    }

    [Theory]
    [InlineData(MerlonPattern.Alternating)]
    [InlineData(MerlonPattern.TallEnds)]
    public void Merlons_can_vary_in_height(MerlonPattern pattern)
    {
        var s = Box(x => x with { LengthMm = 120, HeightMm = 60, Top = TopStyle.Battlements, MerlonHeightMm = 8, Merlons = pattern, MerlonStepMm = 3 });
        var crenels = BoxWall.Crenels(120, s.MerlonWidthMm, s.CrenelWidthMm);
        var p = Tops.Battlements(s, 120, 60, 52, crenels);
        var merlons = p.Segs.Where(g => g.Y0 > 52.5).Select(g => g.Y0).ToList();
        Assert.Equal(crenels.Count + 1, merlons.Count);
        Assert.Equal(60, merlons[0]);
        Assert.Equal(60, merlons[^1] + (pattern == MerlonPattern.Alternating && merlons.Count % 2 == 0 ? 3 : 0));
        Assert.Equal(pattern == MerlonPattern.Alternating ? 57 : merlons.Count > 2 ? 57 : 60, merlons[1]);
        foreach (var build in new[] { s, s with { Build = WallBuild.Flat } })
        {
            var k = WallKitMaker.Make(build);
            Assert.True(k.Ok, Why(k));
            var cut = (build.Boxed ? Part(k, "Front") : Part(k, "Wall")).Cut;
            Assert.Equal(60, Geo.Bounds(cut).bottom, 3);
            Assert.True(Geo.Area(Geo.Intersect(cut, [Geo.Rect(-5, 57.5, 125, 61)])) < Geo.Area(Geo.Intersect(cut, [Geo.Rect(-5, 54, 125, 57.5)])) * 0.9);
        }
        Assert.Empty(Clashes(Assemble(WallKitMaker.Make(s), 3)));
    }

    [Theory]
    [MemberData(nameof(Breaks))]
    public void Flat_walls_break_every_way_too(RuinStyle style, bool battlements, bool walkway)
    {
        var s = new WallSpec { LengthMm = 110, HeightMm = 60, Texture = "Stone blocks", SizeA = 12, SizeB = 6, Broken = style, Top = battlements ? TopStyle.Battlements : TopStyle.Straight, Walkway = walkway, Connectors = new() { [ConnectorKind.Corner] = 2 } };
        var k = WallKitMaker.Make(s);
        Assert.True(k.Ok, Why(k));
        Assert.Equal(1, Geo.Pieces(Part(k, "Wall").Cut));
    }

    [Fact]
    public void An_old_ruined_top_is_broken_in_steps_and_new_settings_are_saved()
    {
        var old = WallSpec.FromJson("""{"LengthMm":80,"HeightMm":40,"ThicknessMm":3,"Top":"Ruined"}""")!;
        Assert.Equal(RuinStyle.Stepped, old.Breaks);
        var s = Box(x => x with { Top = TopStyle.Battlements, Broken = RuinStyle.Breach, RuinAt = RuinPlace.Right, Merlons = MerlonPattern.TallEnds, MerlonStepMm = 2.5 });
        var back = WallSpec.FromJson(s.ToJson())!;
        Assert.Equal((RuinStyle.Breach, RuinPlace.Right, MerlonPattern.TallEnds, 2.5), (back.Breaks, back.RuinAt, back.Merlons, back.MerlonStepMm));
    }

    // ---- the wall's own list of mount points ----

    static List<MountPoint> SomeMounts() =>
    [
        new() { XMm = 20, YMm = 22, Item = MountItem.Sconce },
        new() { XMm = 50, YMm = 30, Item = MountItem.Shelf, Holes = 2, SpacingMm = 24 },
        new() { XMm = 85, YMm = 26, Item = MountItem.Peg, Holes = 2, SpacingMm = 10 },
        new() { XMm = 70, YMm = 18, Item = MountItem.Lamp, OnBack = true },
        new() { XMm = 95, YMm = 36, SlotWidthMm = 2, SlotHeightMm = 4 },
    ];

    [Fact]
    public void Mount_points_go_where_they_are_put_each_with_its_own_slots_and_accessory()
    {
        var s = Box(x => x with { LengthMm = 110, HeightMm = 55, MountPoints = SomeMounts() });
        var k = WallKitMaker.Make(s);
        Assert.True(k.Ok, Why(k));
        Assert.DoesNotContain(k.Findings, f => f.Level == Severity.Warn && f.Text.StartsWith("Mount point"));
        Assert.Equal(5, k.MountsUsed.Count);
        var front = Part(k, "Front").Cut;
        bool Hole(PathsD cut, double x, double y) => !In(cut, x, y) && In(Geo.Offset(cut, 3), x, y);
        // Each slot is a hole in the front at its place: the sconce, the shelf's two, the peg's two one above the other, the bare slot.
        foreach (var (x, y) in new[] { (20.0, 22.0), (38.0, 30.0), (62.0, 30.0), (85.0, 21.0), (85.0, 31.0), (95.0, 36.0) }) Assert.True(Hole(front, x, y), $"no slot at {x}, {y}");
        // The lamp's is in the back (drawn as seen from behind).
        Assert.False(Hole(front, 70, 18));
        Assert.True(Hole(Part(k, "Back").Cut, 110 - 70, 18));
        // The bare slot is the size asked for.
        var bare = Geo.Tidy(front).Where(p => Clipper.Area(p) < 0).Select(p => Clipper.GetBounds(p)).Single(b => Math.Abs((b.left + b.right) / 2 - 95) < 0.01);
        Assert.Equal((2.0, 4.0), (Math.Round(bare.right - bare.left, 3), Math.Round(bare.bottom - bare.top, 3)));
        // The accessories: a sconce, a shelf on two brackets, one peg with two tabs, a lamp.
        Assert.Equal(1, Part(k, "Torch sconce").Copies);
        Assert.Equal(2, Part(k, "Shelf bracket").Copies);
        Assert.Equal(1, Part(k, "Shelf").Copies);
        Assert.Equal(1, Part(k, "Wall peg").Copies);
        Assert.Equal(1, Part(k, "Wall lamp").Copies);
        var shelf = Geo.Bounds(Part(k, "Shelf").Cut);
        Assert.Equal(s.ShelfDepthMm, shelf.bottom - shelf.top, 3);
        Assert.True(shelf.right - shelf.left > 24);
        var all = Assemble(k, 3);
        Assert.Empty(Clashes(all));
        Assert.Empty(EmptySlots(all, 3, s.FitMm));
    }

    [Fact]
    public void Flat_walls_take_the_mount_list_too()
    {
        var s = new WallSpec { LengthMm = 110, HeightMm = 55, Texture = "Brick", MountPoints = SomeMounts(), Connectors = new() { [ConnectorKind.Corner] = 2 } };
        var k = WallKitMaker.Make(s);
        Assert.True(k.Ok, Why(k));
        Assert.Equal(5, k.MountsUsed.Count);
        foreach (var name in new[] { "Torch sconce", "Shelf bracket", "Shelf", "Wall peg", "Wall lamp" }) Assert.Contains(k.Parts, p => p.Name == name);
        Assert.Empty(Clashes(Assemble(k, 3)));
    }

    [Fact]
    public void Mount_points_that_dont_fit_are_said_and_left_out()
    {
        var s = Box(x => x with
        {
            LengthMm = 100, HeightMm = 50, Door = DoorKind.Doorway, DoorOffsetMm = 0,
            MountPoints = [new() { XMm = 3, YMm = 20 }, new() { XMm = 30, YMm = 48 }, new() { XMm = 50, YMm = 15 }, new() { XMm = 25, YMm = 20 }, new() { XMm = 26, YMm = 21 }, new() { XMm = 80, YMm = 20, Holes = 2, SpacingMm = 2 }],
        });
        var k = WallKitMaker.Make(s);
        Assert.True(k.Ok, Why(k));
        string W(int n) => string.Join("\n", k.Findings.Where(f => f.Text.StartsWith($"Mount point {n} ")).Select(f => f.Text));
        Assert.Contains("past the end", W(1));
        Assert.Contains("too high", W(2));
        Assert.Contains("door", W(3));
        Assert.Empty(W(4));
        Assert.Contains("on top of mount point 4", W(5));
        Assert.Contains("further apart", k.Findings.Single(f => f.Text.StartsWith("Mount point 6")).Text);
        Assert.Single(k.MountsUsed);
    }

    [Fact]
    public void A_wall_that_only_said_how_many_mount_points_lists_where_they_went()
    {
        var s = Box(x => x with { Mounts = 3, MountHeightMm = 25, Accessories = Accessory.OneOfEach });
        var k = WallKitMaker.Make(s);
        Assert.True(k.Ok, Why(k));
        Assert.Equal(3, k.MountsUsed.Count);
        Assert.Equal([MountItem.Peg, MountItem.Sconce, MountItem.Shelf], k.MountsUsed.Select(m => m.Item));
        Assert.All(k.MountsUsed, m => Assert.Equal(25, m.YMm));
        // Made again from the list, the slots are in the same places.
        var again = WallKitMaker.Make(s with { Mounts = 0, Accessories = Accessory.None, MountPoints = k.MountsUsed });
        Assert.True(again.Ok, Why(again));
        Assert.Equal(k.MountsUsed.Select(m => m.XMm), again.MountsUsed.Select(m => m.XMm));
        var holes = (WallKit w) => Geo.Tidy(Part(w, "Front").Cut).Where(p => Clipper.Area(p) < 0).Select(p => Math.Round(Clipper.GetBounds(p).left, 2)).Order().ToList();
        Assert.All(holes(k).Zip(holes(again)), h => Assert.Equal(h.First, h.Second, 0.011));
    }

    [Fact]
    public void Accessories_and_shelves_can_be_etched_and_the_list_is_saved()
    {
        var s = Box(x => x with { LengthMm = 110, HeightMm = 55, MountPoints = SomeMounts(), AccessoryTexture = "Wood planks (across)", ShelfDepthMm = 14 });
        var k = WallKitMaker.Make(s);
        Assert.True(k.Ok, Why(k));
        foreach (var name in new[] { "Shelf", "Shelf bracket", "Torch sconce" })
            Assert.True(Part(k, name).Etched.Count > 0 || Part(k, name).Lines.Count > 0, $"{name} not etched");
        // Kept off the tabs, inside the wall.
        Assert.True(Geo.Bounds(Part(k, "Shelf bracket").Etched.Count > 0 ? Part(k, "Shelf bracket").Etched : [.. Part(k, "Shelf bracket").Lines]).left >= 0);
        Assert.Equal(14, Geo.Bounds(Part(k, "Shelf").Cut).bottom - Geo.Bounds(Part(k, "Shelf").Cut).top, 3);
        var back = WallSpec.FromJson(s.ToJson())!;
        Assert.Equal(s.MountPoints, back.MountPoints);
        Assert.Equal(("Wood planks (across)", 14.0), (back.AccessoryTexture, back.ShelfDepthMm));
        Assert.Equal(WallSpec.FromJson("""{"Mounts":2}""")!.MountPoints, []);
    }

    // ---- what fills the doors and windows ----

    /// <summary>A hinged leaf turned about its pins by <paramref name="deg"/> (its latch side towards the front when more than 0).</summary>
    static Placed Swung(Placed leaf, double deg)
    {
        // The pins are the lowest part of the leaf: the hinge is up their middle.
        var b = Geo.Bounds(leaf.Cut);
        var pin = Geo.Bounds(Geo.Intersect(leaf.Cut, [Geo.Rect(b.left - 1, b.top - 1, b.right + 1, b.top + 0.3)]));
        double pu = (pin.left + pin.right) / 2, mid = (b.left + b.right) / 2;
        var p = leaf.Pose.At(pu, 0);
        double a = deg * Math.PI / 180 * (pu < mid ? 1 : -1), c = Math.Cos(a), sn = Math.Sin(a);
        V3 R(V3 v) => new(v.X * c - v.Z * sn, v.Y, v.X * sn + v.Z * c);
        var o = p + R(leaf.Pose.O - p);
        return leaf with { Pose = new Pose(o, R(leaf.Pose.U), leaf.Pose.V) };
    }

    static List<string> Clashes(Placed one, List<Placed> all, double step = 0.25) =>
        Clashes([one, .. all.Where(o => !ReferenceEquals(o, one) && o.Name != one.Name)], step).Where(t => t.StartsWith(one.Name + " and")).ToList();

    [Theory]
    [InlineData(OpeningShape.Rectangle, HingeSide.Left, 12)]
    [InlineData(OpeningShape.Arched, HingeSide.Right, 12)]
    [InlineData(OpeningShape.Pointed, HingeSide.Left, 15)]
    [InlineData(OpeningShape.Rough, HingeSide.Left, 12)]
    public void A_door_hangs_on_pins_and_swings_open_without_hitting_anything(OpeningShape shape, HingeSide hinge, double wall)
    {
        var s = Box(x => x with { WallMm = wall, ColumnMm = wall + 12, LengthMm = 110, HeightMm = 64, Door = DoorKind.DoorwayAndDoor, DoorShape = shape, DoorHinge = hinge, DoorWidthMm = 18, DoorHeightMm = 32 });
        var k = WallKitMaker.Make(s);
        Assert.True(k.Ok, Why(k));
        Assert.Contains(k.Parts, p => p.Name == "Door head");
        if (shape != OpeningShape.Rectangle) Assert.Contains(k.Parts, p => p.Name == "Arch panel");
        var all = Assemble(k, 3);
        Assert.Empty(Clashes(all));
        Assert.Empty(EmptySlots(all, 3, s.FitMm));
        var door = all.Single(p => p.Name == "Door");
        // Both pins are in round holes: the bottom one in the base, the top one in the head plate.
        var b = Geo.Bounds(door.Cut);
        foreach (var v in new[] { b.top + 1, b.bottom - 1 })
        {
            var pin = Geo.Bounds(Geo.Intersect(door.Cut, [Geo.Rect(b.left - 1, v - 0.1, b.right + 1, v + 0.1)]));
            var c = door.Pose.At((pin.left + pin.right) / 2, v);
            Assert.Contains(all, o => o.Name is "Base" or "Door head" && Holds(o, c));
        }
        // It opens to the front, all the way round to square, and shuts against a stop behind it.
        foreach (var deg in new[] { 10, 30, 60, 90 }) Assert.Empty(Clashes(Swung(door, deg), all, 0.3));
        Assert.NotEmpty(Clashes(Swung(door, -12), all, 0.3));
        // The 3D view's open door is the same.
        var part = Part(k, "Door");
        Assert.NotNull(part.HingeU);
        var shown = BoxWall.Opened(part, door.Pose, 60);
        var swung = Swung(door, 60).Pose;
        Assert.True((shown.O - swung.O).Length < 1e-6 && (shown.U - swung.U).Length < 1e-6, $"{shown} vs {swung}");
    }

    [Fact]
    public void A_door_can_be_glued_into_a_groove_instead()
    {
        var s = Box(x => x with { LengthMm = 110, HeightMm = 64, Door = DoorKind.DoorwayAndDoor, DoorShape = OpeningShape.Arched, DoorOpens = false, DoorWidthMm = 18, DoorHeightMm = 32 });
        var k = WallKitMaker.Make(s);
        Assert.True(k.Ok, Why(k));
        Assert.DoesNotContain(k.Parts, p => p.Name == "Door head");
        var door = Part(k, "Door");
        // Wider than the doorway by the groove each side, less the fit, and in the middle layer between the faces.
        var b = Geo.Bounds(door.Cut);
        Assert.Equal(18 + 2 * (s.InsertGrooveMm - s.FitMm / 2), b.right - b.left, 1);
        Assert.Equal(BoxWall.CoreZ(s)[BoxWall.CoreLayers(s) / 2], door.Poses[0].O.Z, 3);
        Assert.NotEmpty(door.Etched.Count > 0 ? door.Etched : [.. door.Lines]);
        var all = Assemble(k, 3);
        Assert.Empty(Clashes(all));
        // The groove: next to the doorway's edge in the door's layer there's door, not lining.
        var at = new V3(55 - 9 - s.InsertGrooveMm / 2, 15, door.Poses[0].O.Z);
        Assert.Equal(["Door"], all.Where(o => o.Inside(at)).Select(o => o.Name));
    }

    [Fact]
    public void A_wall_too_thin_for_hinge_pins_glues_its_door_in()
    {
        var k = WallKitMaker.Make(Box(x => x with { WallMm = 9, LengthMm = 110, HeightMm = 64, Door = DoorKind.DoorwayAndDoor, DoorWidthMm = 18, DoorHeightMm = 32 }));
        Assert.True(k.Ok, Why(k));
        Assert.Contains(k.Findings, f => f.Text.Contains("glued in place") && f.Text.Contains("thick"));
        Assert.DoesNotContain(k.Parts, p => p.Name == "Door head");
        Assert.Empty(Clashes(Assemble(k, 3)));
    }

    [Fact]
    public void A_portcullis_slides_up_inside_the_wall()
    {
        var s = Box(x => x with { LengthMm = 110, HeightMm = 64, Door = DoorKind.DoorwayAndDoor, DoorLook = DoorLook.Bars, DoorShape = OpeningShape.Rectangle, DoorWidthMm = 18, DoorHeightMm = 28 });
        var k = WallKitMaker.Make(s);
        Assert.True(k.Ok, Why(k));
        Assert.Contains(k.Findings, f => f.Text.Contains("slides up"));
        var all = Assemble(k, 3);
        Assert.Empty(Clashes(all));
        // Lifted as far as the note says, it still hits nothing.
        var p = all.Single(o => o.Name == "Portcullis");
        double up = double.Parse(System.Text.RegularExpressions.Regex.Match(k.Findings.First(f => f.Text.Contains("slides up")).Text, @"([\d.]+) mm").Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(up > 10, $"{up}");
        Assert.Equal(up, Part(k, "Portcullis").SlideMm, 3);
        var lifted = p with { Pose = BoxWall.Opened(Part(k, "Portcullis"), p.Pose) };
        Assert.Empty(Clashes(lifted, all));
    }

    [Theory]
    [InlineData(WindowFill.Frame, WindowBars.Cross, 8)]
    [InlineData(WindowFill.Frame, WindowBars.None, 8)]
    [InlineData(WindowFill.Shutter, WindowBars.Bars, 8)]
    [InlineData(WindowFill.Shutters, WindowBars.None, 8)]
    [InlineData(WindowFill.Shutters, WindowBars.None, 16)]
    [InlineData(WindowFill.Open, WindowBars.Cross, 8)]
    public void Windows_are_filled_and_nothing_clashes(WindowFill fill, WindowBars bars, double width)
    {
        var s = Box(x => x with { LengthMm = 110, HeightMm = 64, Windows = 2, WindowShape = OpeningShape.Arched, WindowWidthMm = width, WindowHeightMm = 16, SillMm = 24, Bars = bars, WindowFill = fill });
        var k = WallKitMaker.Make(s);
        Assert.True(k.Ok, Why(k));
        var all = Assemble(k, 3);
        Assert.Empty(Clashes(all));
        Assert.Empty(EmptySlots(all, 3, s.FitMm));
        string[] expect = fill switch
        {
            WindowFill.Frame => ["Window frame"],
            WindowFill.Shutter => ["Shutter"],
            WindowFill.Shutters when width >= 16 => ["Left shutter", "Right shutter", "Window sill", "Window head", "Arch panel"],
            WindowFill.Shutters => ["Shutter", "Window sill", "Window head"],   // the arch over it is hardly taller than the head plate
            _ => [],
        };
        foreach (var name in expect) Assert.Equal(2, Part(k, name).Copies);
        if (fill == WindowFill.Open) Assert.DoesNotContain(k.Parts, p => p.Name is "Window frame" or "Shutter");
        // Filled, the front face's window is clear (the bars are in the frame); left open, they're in the face.
        var front = Part(k, "Front");
        int holesInFront = Geo.Tidy(front.Cut).Count(p => Clipper.Area(p) < 0);
        Assert.Equal(fill == WindowFill.Open ? 8 : 2, holesInFront);
        if (fill == WindowFill.Shutters)
            foreach (var leaf in all.Where(p => p.Name.EndsWith("hutter")))
                foreach (var deg in new[] { 20, 60, 90 }) Assert.Empty(Clashes(Swung(leaf, deg), all, 0.3));
    }

    [Fact]
    public void Round_windows_get_a_glued_shutter_when_asked_for_ones_that_open()
    {
        var k = WallKitMaker.Make(Box(x => x with { LengthMm = 110, HeightMm = 64, Windows = 1, WindowShape = OpeningShape.Round, WindowWidthMm = 10, SillMm = 24, WindowFill = WindowFill.Shutters }));
        Assert.True(k.Ok, Why(k));
        Assert.Contains(k.Findings, f => f.Text.Contains("Round windows"));
        Assert.Equal(1, Part(k, "Shutter").Copies);
        Assert.Empty(Clashes(Assemble(k, 3)));
    }

    [Fact]
    public void Old_designs_get_a_door_that_opens_and_framed_windows()
    {
        var s = WallSpec.FromJson("""{"Build":"Box","WallMm":12,"Door":"DoorwayAndDoor","Windows":2}""")!;
        Assert.True(s.DoorOpens);
        Assert.Equal(WindowFill.Frame, s.WindowFill);
        var back = WallSpec.FromJson((s with { DoorOpens = false, DoorHinge = HingeSide.Right, WindowFill = WindowFill.Shutters, InsertGrooveMm = 1.6 }).ToJson())!;
        Assert.Equal((false, HingeSide.Right, WindowFill.Shutters, 1.6), (back.DoorOpens, back.DoorHinge, back.WindowFill, back.InsertGrooveMm));
    }

    [Fact]
    public void Arrow_slits_are_left_open_rather_than_filled_with_a_frame()
    {
        var k = WallKitMaker.Make(Box(x => x with { Windows = 2, WindowShape = OpeningShape.ArrowSlit, WindowWidthMm = 2.5, WindowHeightMm = 12, SillMm = 20 }));
        Assert.True(k.Ok, Why(k));
        Assert.DoesNotContain(k.Parts, p => p.Name == "Window frame");
    }
}
