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

    static bool In(PathsD area, double u, double v)
    {
        int inside = 0;
        foreach (var p in area)
            if (Clipper.PointInPolygon(new PointD(u, v), p) == PointInPolygonResult.IsInside) inside++;
        return inside % 2 == 1;
    }

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
        var all = Assemble(k, 3).Where(p => p.Name != "Door").ToList();   // the loose door stands in its doorway
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
        Assert.Empty(Clashes(Assemble(k, 3).Where(p => p.Name != "Door").ToList()));
    }

    [Fact]
    public void Roofs_fit_box_walls_and_their_column_caps()
    {
        var w = Box(x => x with { ColumnTop = ColumnTop.SteppedCap, ColumnLipMm = 2 });
        var r = PhotonCanvas.Addon.Roofs.WallsFit.Fit(new PhotonCanvas.Addon.Roofs.RoofSpec(), w);
        Assert.Equal(w.WallMm, r.WallMm);
        Assert.Equal(w.ColumnMm + 8, r.CornerMm, 2);
        Assert.Equal(BoxColumns.TopMm(w) - w.HeightMm, r.RestAboveWallsMm, 2);
    }
}
