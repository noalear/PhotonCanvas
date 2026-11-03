using Clipper2Lib;
using PhotonCanvas.Addon.Buildings;
using PhotonCanvas.Addon.Furniture;
using PhotonCanvas.Addon.HouseEditor;
using PhotonCanvas.Addon.Walls;
using Geo = PhotonCanvas.Addon.Walls.Geo;
using Severity = PhotonCanvas.Addon.Buildings.Severity;
using HingeSide = PhotonCanvas.Addon.Walls.HingeSide;

namespace PhotonCanvas.Tests;

/// <summary>The building editor: rooms found from the walls drawn, walls cut between columns, doors and windows where they were dropped, floors, and the whole thing put together without clashes.</summary>
public class HouseEditorTests
{
    static List<WallEdge> Line(int x0, int y0, int x1, int y1)
    {
        var list = new List<WallEdge>();
        int dx = Math.Sign(x1 - x0), dy = Math.Sign(y1 - y0);
        for (var p = new GridPt(x0, y0); p != new GridPt(x1, y1); p += new GridPt(dx, dy)) list.Add(WallEdge.Of(p, p + new GridPt(dx, dy)));
        return list;
    }

    static List<WallEdge> Box(int x0, int y0, int x1, int y1) => [.. Line(x0, y0, x1, y0), .. Line(x1, y0, x1, y1), .. Line(x1, y1, x0, y1), .. Line(x0, y1, x0, y0)];

    [Fact]
    public void Rooms_are_found_from_the_walls_round_them()
    {
        var walls = Box(0, 0, 4, 3).Concat(Line(2, 0, 2, 3)).ToList();
        var plan = PlanShape.Of(walls);
        Assert.Equal(2, plan.Rooms.Count);
        Assert.All(plan.Rooms, r => Assert.Equal(6, r.AreaSquares, 3));
        // Corners, two T junctions: six columns, seven runs.
        Assert.Equal(6, plan.Joints.Count);
        Assert.Equal(7, plan.Runs.Count);
        Assert.NotEqual(plan.RoomAt(1, 1), plan.RoomAt(3, 1));
        Assert.Equal(-1, plan.RoomAt(5, 1));

        // A gap in the walls lets the room out.
        var open = PlanShape.Of(walls.Where(e => e != WallEdge.Of(new(0, 1), new(0, 2))));
        Assert.Single(open.Rooms);
    }

    [Fact]
    public void Diagonal_walls_close_rooms_too()
    {
        // A square with one corner cut off corner to corner: 4 × 4 less half a square.
        var walls = Line(0, 1, 0, 4).Concat(Line(0, 4, 4, 4)).Concat(Line(4, 4, 4, 0)).Concat(Line(4, 0, 1, 0)).Concat([WallEdge.Of(new(0, 1), new(1, 0))]).ToList();
        var plan = PlanShape.Of(walls);
        Assert.Single(plan.Rooms);
        Assert.Equal(15.5, plan.Rooms[0].AreaSquares, 3);
        // The outside is on the right walking along the top wall from right to left... (left of it as drawn, going right).
        var (left, right) = plan.Sides(new(1, 0), new(2, 0));
        Assert.Equal(-1, left);
        Assert.Equal(0, right);
    }

    [Fact]
    public void The_starter_house_comes_out_whole_and_fits_together()
    {
        var kit = HouseKitMaker.Make(HouseDesign.Starter());
        Assert.True(kit.Ok, string.Join("\n", kit.Findings.Select(f => f.Text)));
        Assert.DoesNotContain(kit.Findings, f => f.Level == Severity.Warn);
        // Seven walls (the inner one between two T junctions), six columns.
        Assert.Equal(7, kit.Walls.Count);
        Assert.Equal(6, kit.Solids.Where(s => s.Kind == SolidKind.Column && s.Pose.V.Y == 0).Select(s => (Math.Round(s.Pose.O.X), Math.Round(s.Pose.O.Z))).Distinct().Count());
        // The door and windows went where they were dropped.
        Assert.Equal(1, kit.Walls.Count(w => w.Spec.Door == DoorKind.DoorwayAndDoor));
        Assert.Equal(1, kit.Walls.Count(w => w.Spec.Door == DoorKind.Doorway));
        Assert.Equal(3, kit.Walls.Sum(w => w.Spec.WindowAtMm.Count));
        Assert.Empty(Clashes(kit.Solids.Where(s => s.Kind is SolidKind.Wall or SolidKind.Column).ToList(), step: 0.6));
        Assert.True(kit.Pieces.All(p => Fits(p)), "A piece is too big for the bed.");
    }

    [Fact]
    public void Each_outside_wall_faces_out()
    {
        var kit = HouseKitMaker.Make(HouseDesign.Starter());
        var fronts = kit.Solids.Where(s => s.Kind == SolidKind.Wall && s.Name.Length == "Wall A".Length).ToList();
        Assert.Equal(7, fronts.Count);
        // Every outside wall's front faces away from the middle of the building (the inner wall, near the middle, either way).
        foreach (var w in fronts.Where(w => Math.Abs(w.Middle.X) > 20 || Math.Abs(w.Middle.Z) > 20))
            Assert.True(w.Facing.X * w.Middle.X + w.Facing.Z * w.Middle.Z > 0, $"{w.Name} faces in");
    }

    [Fact]
    public void Two_doors_on_one_wall_get_a_column_between()
    {
        var style = HouseDesign.DefaultStyle();
        var design = new HouseDesign
        {
            Style = style,
            Levels = [new Level { Walls = Box(0, 0, 6, 3), Openings = [Opening.NewDoor(style, 1.5, 3), Opening.NewDoor(style, 4.5, 3)] }],
        };
        var kit = HouseKitMaker.Make(design);
        Assert.True(kit.Ok, string.Join("\n", kit.Findings.Select(f => f.Text)));
        Assert.Equal(2, kit.Walls.Count(w => w.Spec.Door != DoorKind.None));
        Assert.Contains(kit.Pieces, p => p.Name.StartsWith("Straight column", StringComparison.Ordinal));
    }

    [Fact]
    public void A_wall_longer_than_the_bed_is_cut()
    {
        var design = new HouseDesign { Levels = [new Level { Walls = Box(0, 0, 12, 4) }] };
        var kit = HouseKitMaker.Make(design);
        Assert.True(kit.Ok, string.Join("\n", kit.Findings.Select(f => f.Text)));
        Assert.All(kit.Walls, w => Assert.True(WallKitMaker.PanelLength(w.Spec) <= 216, $"{w.Letter} is {WallKitMaker.PanelLength(w.Spec):0} mm"));
        Assert.Contains(kit.Findings, f => f.Text.Contains("longer than the bed"));
        Assert.True(kit.Pieces.All(p => Fits(p)), "A piece is too big for the bed.");
    }

    [Fact]
    public void Diagonal_corners_get_angled_columns()
    {
        var walls = Line(0, 2, 0, 5).Concat(Line(0, 5, 5, 5)).Concat(Line(5, 5, 5, 0)).Concat(Line(5, 0, 2, 0)).Concat(Line(0, 2, 2, 0)).ToList();
        var kit = HouseKitMaker.Make(new HouseDesign { Levels = [new Level { Walls = walls }] });
        Assert.True(kit.Ok, string.Join("\n", kit.Findings.Select(f => f.Text)));
        Assert.Contains(kit.Pieces, p => p.Name.Contains("135° column", StringComparison.Ordinal));
        Assert.Empty(Clashes(kit.Solids.Where(s => s.Kind is SolidKind.Wall or SolidKind.Column).ToList(), step: 0.6));
    }

    [Fact]
    public void A_second_floor_has_a_floor_under_it_and_flush_columns_below()
    {
        var style = HouseDesign.DefaultStyle();
        var design = new HouseDesign
        {
            Style = style,
            Levels =
            [
                new Level { Walls = Box(0, 0, 5, 4), Openings = [Opening.NewDoor(style, 2.5, 4)] },
                // Smaller upstairs, with a balcony floor out front.
                new Level { Walls = Box(0, 0, 3, 4), Openings = [Opening.NewDoor(style, 3, 2)], Floor = [new(3, 0), new(4, 0), new(3, 1), new(4, 1), new(3, 2), new(4, 2), new(3, 3), new(4, 3)] },
            ],
        };
        var kit = HouseKitMaker.Make(design);
        Assert.True(kit.Ok, string.Join("\n", kit.Findings.Select(f => f.Text)));
        var floor = Assert.Single(kit.Solids, s => s.Kind == SolidKind.Floor);
        // As big as the room below, columns and all.
        var b = Geo.Bounds(floor.Outline);
        Assert.Equal(5 * 25.4 + style.ColumnMm, b.right - b.left, 1);
        Assert.Equal(4 * 25.4 + style.ColumnMm, b.bottom - b.top, 1);
        // The walls above stand on it, the columns below are flush under it.
        double top = BuildingKitMaker.StoreyBase(HouseKitMaker.StyleFor(design), 1);
        Assert.Equal(top - style.WallSheetMm / 2, floor.Pose.O.Y, 3);
        Assert.Contains(kit.Pieces, p => p.Name.StartsWith("Floor 1 corner column", StringComparison.Ordinal) && !p.Name.Contains("top"));
        Assert.Empty(Clashes(kit.Solids.Where(s => s.Kind is SolidKind.Wall or SolidKind.Column or SolidKind.Floor).ToList(), step: 0.7));
    }

    [Fact]
    public void A_floor_bigger_than_the_bed_is_cut_along_a_wall()
    {
        var walls = Box(0, 0, 10, 4).Concat(Line(5, 0, 5, 4)).ToList();
        var design = new HouseDesign { Levels = [new Level { Walls = walls }, new Level { Walls = walls }] };
        var kit = HouseKitMaker.Make(design);
        Assert.True(kit.Ok, string.Join("\n", kit.Findings.Select(f => f.Text)));
        var floors = kit.Pieces.Where(p => p.Name == "Floor 2" || p.Name.StartsWith("Floor 2, piece", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, floors.Count);
        Assert.All(floors, p => Assert.True(Fits(p)));
    }

    [Fact]
    public void A_design_saves_and_opens_again()
    {
        var d = HouseDesign.Starter();
        var back = HouseDesign.FromJson(d.ToJson());
        Assert.NotNull(back);
        Assert.Equal(d.Levels[0].Walls.Count, back!.Levels[0].Walls.Count);
        Assert.Equal(d.Levels[0].Openings.Count, back.Levels[0].Openings.Count);
        Assert.Equal(d.ToJson(), back.ToJson());
    }

    [Fact]
    public void Windows_go_where_they_are_asked_for()
    {
        foreach (var boxed in new[] { false, true })
        {
            var spec = (boxed ? WallSpec.NewBoxed : new WallSpec()) with { LengthMm = 150, HeightMm = 50, Windows = 2, WindowAtMm = [-40, 30], Connectors = [] };
            var kit = WallKitMaker.Make(spec);
            Assert.True(kit.Ok, string.Join("\n", kit.Findings.Select(f => f.Text)));
            var front = kit.Front!;
            double L = WallKitMaker.PanelLength(spec), y = spec.SillMm + spec.WindowHeightMm / 3;
            foreach (var at in new[] { -40.0, 30 })
                Assert.False(OutlineGrid.In(front.Cut, L / 2 + at, y), $"No window at {at} mm ({(boxed ? "box" : "flat")} wall)");
            Assert.True(OutlineGrid.In(front.Cut, L / 2 - 5, y), $"A window where none was asked for ({(boxed ? "box" : "flat")} wall)");
        }
    }

    [Fact]
    public void The_pieces_go_into_the_design_and_can_be_found_and_replaced()
    {
        var design = HouseDesign.Sample();
        var kit = HouseKitMaker.Make(design);
        Assert.True(kit.Ok, string.Join("\n", kit.Findings.Select(f => f.Text)));
        Assert.Empty(HousePlacement.TooBig(kit, PhotonCanvas.Core.MachineProfile.F2Ultra));
        var (s, pages, id) = HousePlacement.Place(PhotonCanvas.Doc.DocState.New(), kit, design, PhotonCanvas.Core.MachineProfile.F2Ultra, new PhotonCanvas.Addon.Buildings.PlaceOptions());
        Assert.True(pages >= 1);
        // Every piece's outline (its etching and lines are named "… · etching", "… · lines").
        Assert.Equal(kit.PieceCount, s.Shapes.Concat(s.OtherPages.SelectMany(p => p.Shapes)).Count(x => x.AddonData.ContainsKey(HousePlacement.IdKey) && !x.Name.Contains(" · ")));
        var found = HousePlacement.Find(s, s.Shapes.Select(x => x.Id));
        Assert.NotNull(found);
        Assert.Equal(design.ToJson(), found!.Value.Design.ToJson());
        // Changed and put in again: the old pieces go.
        var smaller = HouseDesign.Starter();
        var (s2, _, id2) = HousePlacement.Place(s, HouseKitMaker.Make(smaller), smaller, PhotonCanvas.Core.MachineProfile.F2Ultra, new PhotonCanvas.Addon.Buildings.PlaceOptions { Replace = id });
        Assert.Equal(id, id2);
        Assert.Equal(HouseDesign.Starter().ToJson(), HousePlacement.Find(s2, s2.Shapes.Select(x => x.Id))!.Value.Design.ToJson());
    }

    // ---- roofs, railings and rooms' floors ----

    /// <summary>A house of one room (4 × 3 squares) on the ground floor, and nothing else.</summary>
    static HouseDesign OneRoom(params RoofArea[] roofs) => new() { Levels = [new Level { Walls = Box(0, 0, 4, 3) }], Roofs = [.. roofs] };

    static RoofArea RoofOver(int x0, int y0, int x1, int y1, RoofKind kind, int turn = 0, int level = 0) =>
        new() { Level = level, X0 = x0, Y0 = y0, X1 = x1, Y1 = y1, Kind = kind, Turn = turn, PitchDeg = 40, Covering = kind == RoofKind.Flat ? "Square tiles" : "Slate" };

    [Fact]
    public void The_sample_house_comes_out_whole_with_its_roof_railings_and_room_floor()
    {
        var kit = HouseKitMaker.Make(HouseDesign.Sample());
        Assert.True(kit.Ok, string.Join("\n", kit.Findings.Select(f => f.Text)));
        Assert.DoesNotContain(kit.Findings, f => f.Level == Severity.Warn);
        Assert.Contains(kit.Pieces, p => p.Name == "Roof: base");
        Assert.Contains(kit.Pieces, p => p.Name.StartsWith("Roof: gable", StringComparison.Ordinal));
        Assert.Contains(kit.Pieces, p => p.Name.StartsWith("Floor 2 railing", StringComparison.Ordinal));
        Assert.Contains(kit.Pieces, p => p.Name == "Floor 1, room 1 (flagstones)");
        Assert.True(kit.Pieces.All(p => Fits(p)), "A piece is too big for the bed.");
        // The railings stand on the floor without running into the walls or columns.
        Assert.Empty(Clashes(kit.Solids.Where(s => s.Kind is SolidKind.Wall or SolidKind.Column or SolidKind.Railing).ToList(), step: 0.7));
    }

    [Theory]
    [InlineData(RoofKind.Gable, 0)]
    [InlineData(RoofKind.Gable, 1)]
    [InlineData(RoofKind.Hip, 0)]
    [InlineData(RoofKind.LeanTo, 0)]
    [InlineData(RoofKind.LeanTo, 1)]
    [InlineData(RoofKind.LeanTo, 2)]
    [InlineData(RoofKind.LeanTo, 3)]
    [InlineData(RoofKind.Flat, 0)]
    public void A_roof_rests_on_the_column_caps_over_its_rectangle(RoofKind kind, int turn)
    {
        var d = OneRoom(RoofOver(0, 0, 4, 3, kind, turn));
        var kit = HouseKitMaker.Make(d);
        Assert.True(kit.Ok, string.Join("\n", kit.Findings.Select(f => f.Text)));
        var roof = kit.Solids.Where(s => s.Kind == SolidKind.Roof).ToList();
        Assert.NotEmpty(roof);
        var s = HouseKitMaker.StyleFor(d);
        var wall = BuildingKitMaker.StoreyWall(s, 0, 100) with { Cap = true, ColumnTop = ColumnTop.Cap };
        double rest = BuildingKitMaker.StoreyBase(s, 0) + WallKitMaker.ColumnTopMm(wall);
        // Its base lies on the caps (the frame under it drops inside the walls).
        var bottoms = roof.Select(r => Corners(r).Min(c => c.Y)).ToList();
        Assert.Contains(bottoms, y => Math.Abs(y - rest) < 0.05);
        Assert.All(roof.Where(r => r.Name == "Roof" && Corners(r).Min(c => c.Y) >= rest - 0.05), r => Assert.True(Corners(r).Min(c => c.Y) >= rest - 0.05));
        // Centred over the room, as wide as the caps' outside (and its eaves beyond).
        double lip = WallKitMaker.ColumnLip(wall);
        double outX = 4 * 25.4 + s.ColumnMm + 2 * lip, outZ = 3 * 25.4 + s.ColumnMm + 2 * lip;
        var basePlate = roof.Where(r => Math.Abs(Corners(r).Min(c => c.Y) - rest) < 0.05).OrderByDescending(r => Geo.Area(r.Outline)).First();
        var cs = Corners(basePlate);
        Assert.Equal(outX, cs.Max(c => c.X) - cs.Min(c => c.X), 1);
        Assert.Equal(outZ, cs.Max(c => c.Z) - cs.Min(c => c.Z), 1);
        Assert.Equal(0, (cs.Max(c => c.X) + cs.Min(c => c.X)) / 2, 1);
        Assert.Equal(0, (cs.Max(c => c.Z) + cs.Min(c => c.Z)) / 2, 1);
        if (kind == RoofKind.LeanTo)
        {
            // Its high side is away from where it slopes down to.
            var (_, front) = HouseKitMaker.Directions(turn);
            var all = roof.SelectMany(Corners).ToList();
            var high = all.OrderByDescending(c => c.Y).Take(4).ToList();
            double along = high.Average(c => c.X * front.X + c.Z * front.Y);
            Assert.True(along < 0, $"The lean-to's high side should be at the back (got {along:0.#}).");
        }
        if (kind == RoofKind.Gable)
        {
            // The ridge runs the way it was asked to: across the narrower way it's lower (by half the difference up the slope).
            static double Ridge(HouseKit k) => k.Solids.Where(s => s.Kind == SolidKind.Roof).SelectMany(Corners).Max(c => c.Y);
            double other = Ridge(HouseKitMaker.Make(OneRoom(RoofOver(0, 0, 4, 3, kind, 1 - turn))));
            double diff = (Ridge(kit) - other) * (turn == 0 ? 1 : -1);
            Assert.Equal(-(outX - outZ) / 2 * Math.Tan(40 * Math.PI / 180), diff, 0.5);
        }
    }

    [Fact]
    public void A_roof_over_two_rooms_has_no_frame_under_it_and_one_under_a_floor_is_warned_of()
    {
        var walls = Box(0, 0, 6, 3).Concat(Line(3, 0, 3, 3)).ToList();
        var d = new HouseDesign { Levels = [new Level { Walls = walls }], Roofs = [RoofOver(0, 0, 6, 3, RoofKind.Gable)] };
        var kit = HouseKitMaker.Make(d);
        Assert.True(kit.Ok, string.Join("\n", kit.Findings.Select(f => f.Text)));
        Assert.DoesNotContain(kit.Pieces, p => p.Name.Contains("locator", StringComparison.Ordinal));
        Assert.Contains(kit.Findings, f => f.Text.Contains("more than one room", StringComparison.Ordinal));
        var one = HouseKitMaker.Make(OneRoom(RoofOver(0, 0, 4, 3, RoofKind.Gable)));
        Assert.Contains(one.Pieces, p => p.Name.Contains("locator", StringComparison.Ordinal));

        // Under the floor above: warned.
        var two = new HouseDesign { Levels = [new Level { Walls = Box(0, 0, 4, 3) }, new Level { Walls = Box(0, 0, 4, 3) }], Roofs = [RoofOver(0, 0, 4, 3, RoofKind.Gable)] };
        Assert.Contains(HouseKitMaker.Make(two).Findings, f => f.Level == Severity.Warn && f.Text.Contains("under floor 2", StringComparison.Ordinal));
    }

    [Fact]
    public void Railings_go_round_open_floor_and_into_slots_in_it()
    {
        var style = HouseDesign.DefaultStyle();
        // Upstairs is half the house: the rest of the floor is a terrace.
        var d = new HouseDesign { Style = style, Levels = [new Level { Walls = Box(0, 0, 6, 3) }, new Level { Walls = Box(0, 0, 3, 3) }] };
        var below = PlanShape.Of(d.Levels[0].Walls);
        var plan = PlanShape.Of(d.Levels[1].Walls);
        var runs = HouseKitMaker.RailingRuns(d.Levels[1], below, plan, style.WallSheetMm, style.ColumnMm);
        // Along the top, the right-hand end and the bottom of the terrace.
        Assert.Equal(3, runs.Count);
        Assert.All(runs, r => Assert.DoesNotContain(d.Levels[1].Walls, w => w == WallEdge.Of(r.A, r.A + r.Step)));
        // Stopped at the columns at (3, 0) and (3, 3); at the far corners, the ones across carry on past.
        Assert.All(runs.Where(r => r.A.Y == r.B.Y), r => Assert.Contains(new[] { r.TrimA, r.TrimB }, t => t < 0));
        Assert.All(runs.Where(r => r.A.X == r.B.X), r => Assert.True(r.TrimA > 0 && r.TrimB > 0));

        var kit = HouseKitMaker.Make(d);
        Assert.True(kit.Ok, string.Join("\n", kit.Findings.Select(f => f.Text)));
        Assert.Equal(3, kit.Solids.Count(s => s.Kind == SolidKind.Railing));
        var floor = Assert.Single(kit.Solids, s => s.Kind == SolidKind.Floor);
        // Two tabs a railing, a slot each.
        Assert.Equal(1 + 6, floor.Outline.Count);
        Assert.Empty(Clashes(kit.Solids.Where(s => s.Kind is SolidKind.Wall or SolidKind.Column or SolidKind.Railing or SolidKind.Floor).ToList(), step: 0.7));

        // Railings turned off: none, and no slots.
        var off = HouseKitMaker.Make(d with { Levels = [d.Levels[0], d.Levels[1] with { Railings = false }] });
        Assert.DoesNotContain(off.Solids, s => s.Kind == SolidKind.Railing);
        Assert.Single(Assert.Single(off.Solids, s => s.Kind == SolidKind.Floor).Outline);

        // One stretch taken away (the right-hand end): the other two stay, and stop on the grid where it was.
        var gap = d with { Levels = [d.Levels[0], d.Levels[1] with { NoRailing = Line(6, 0, 6, 3) }] };
        var left = HouseKitMaker.RailingRuns(gap.Levels[1], below, plan, style.WallSheetMm, style.ColumnMm);
        Assert.Equal(2, left.Count);
        Assert.All(left, r => Assert.Equal(0, r.A.X == 6 ? r.TrimA : r.TrimB, 6));
        var gapKit = HouseKitMaker.Make(gap);
        Assert.True(gapKit.Ok, string.Join("\n", gapKit.Findings.Select(f => f.Text)));
        Assert.Equal(2, gapKit.Solids.Count(s => s.Kind == SolidKind.Railing));
        Assert.Equal(1 + 4, Assert.Single(gapKit.Solids, s => s.Kind == SolidKind.Floor).Outline.Count);
        Assert.Equal(3, HouseDesign.FromJson(gap.ToJson())!.Levels[1].NoRailing.Count);
    }

    [Fact]
    public void A_room_gets_its_own_floor_pattern()
    {
        var walls = Box(0, 0, 6, 3).Concat(Line(3, 0, 3, 3)).ToList();
        var plain = new HouseDesign { Levels = [new Level { Walls = walls }, new Level { Walls = walls }] };
        var mixed = plain with { Levels = [plain.Levels[0] with { RoomFloors = [new RoomFloor { X = 1.5, Y = 1.5, Texture = "Chequered tiles" }] }, plain.Levels[1] with { RoomFloors = [new RoomFloor { X = 4.5, Y = 1.5, Texture = "Herringbone brick" }] }] };
        var a = HouseKitMaker.Make(plain);
        var b = HouseKitMaker.Make(mixed);
        Assert.True(b.Ok, string.Join("\n", b.Findings.Select(f => f.Text)));
        // Downstairs, a loose floor for the left room, inside its walls.
        var loose = Assert.Single(b.Pieces, p => p.Name.StartsWith("Floor 1, room", StringComparison.Ordinal));
        var lb = Geo.Bounds(loose.Cut);
        Assert.True(lb.right - lb.left < 3 * 25.4 && lb.bottom - lb.top < 3 * 25.4);
        Assert.NotEmpty(loose.Etched);
        Assert.Empty(Clashes(b.Solids.Where(s => s.Kind is SolidKind.Wall or SolidKind.Column || s.Name.StartsWith("Floor 1, room", StringComparison.Ordinal)).ToList(), step: 0.7));
        // Upstairs, the right room's part of the floor is etched differently.
        var fa = a.Pieces.Single(p => p.Name == "Floor 2");
        var fb = b.Pieces.Single(p => p.Name == "Floor 2");
        Assert.NotEqual(Geo.Area(fa.Etched), Geo.Area(fb.Etched), 0);
    }

    [Fact]
    public void Roofs_and_room_floors_save_and_move_with_the_design()
    {
        var d = HouseDesign.Sample();
        var back = HouseDesign.FromJson(d.ToJson())!;
        Assert.Equal(d.ToJson(), back.ToJson());
        Assert.Single(back.Roofs);
        var moved = d.Moved(2, -1);
        Assert.Equal(d.Roofs[0].X0 + 2, moved.Roofs[0].X0);
        Assert.Equal(d.Roofs[0].Y1 - 1, moved.Roofs[0].Y1);
        Assert.Equal(d.Levels[0].RoomFloors[0].X + 2, moved.Levels[0].RoomFloors[0].X);
    }

    [Fact]
    public void A_door_fills_its_arch_unless_it_opens_on_hinges()
    {
        var d = HouseDesign.Starter();
        var door = d.Levels[0].Openings.First(o => o.Kind == OpeningKind.Door);
        Assert.False(door.Opens);
        var kit = HouseKitMaker.Make(d);
        var wall = kit.Walls.Single(w => w.Spec.Door == DoorKind.DoorwayAndDoor);
        Assert.False(wall.Spec.DoorOpens);
        // Glued in, it's the opening's own shape: no square leaf with a panel over it.
        Assert.DoesNotContain(kit.Pieces, p => p.Name.Contains("arch panel", StringComparison.Ordinal) || p.Name.Contains("door head", StringComparison.Ordinal));

        var opens = d with { Levels = [d.Levels[0] with { Openings = [.. d.Levels[0].Openings.Select(o => o.Kind == OpeningKind.Door ? o with { Opens = true } : o)] }] };
        var hinged = HouseKitMaker.Make(opens);
        Assert.True(hinged.Walls.Single(w => w.Spec.Door == DoorKind.DoorwayAndDoor).Spec.DoorOpens);
        Assert.Contains(hinged.Pieces, p => p.Name.Contains("door head", StringComparison.Ordinal));
    }

    [Fact]
    public void The_building_can_stand_on_one_plate_with_the_floors_patterns_picked()
    {
        var walls = Box(0, 0, 6, 3).Concat(Line(3, 0, 3, 3)).ToList();
        var d = new HouseDesign
        {
            Base = BaseKind.Plate,
            Levels = [new Level { Walls = walls, RoomFloors = [new RoomFloor { X = 1.5, Y = 1.5, Texture = "Chequered tiles" }] }, new Level { Walls = walls }],
        };
        var kit = HouseKitMaker.Make(d);
        Assert.True(kit.Ok, string.Join("\n", kit.Findings.Select(f => f.Text)));
        Assert.DoesNotContain(kit.Pieces, p => p.Name.StartsWith("Floor tile", StringComparison.Ordinal));
        // The room's pattern is on the plate, not a loose floor.
        Assert.DoesNotContain(kit.Pieces, p => p.Name.StartsWith("Floor 1, room", StringComparison.Ordinal));
        var plate = Assert.Single(kit.Pieces, p => p.Name == "Base");
        var b = Geo.Bounds(plate.Cut);
        var style = HouseDesign.DefaultStyle();
        Assert.InRange(b.right - b.left, 6 * 25.4 + style.ColumnMm + 4 - 0.3, 6 * 25.4 + style.ColumnMm + 4 + 0.3);
        Assert.NotEmpty(plate.Etched);
        Assert.NotEmpty(plate.Lines);
        Assert.Empty(Clashes(kit.Solids.Where(s => s.Kind is SolidKind.Wall or SolidKind.Column or SolidKind.Ground).ToList(), step: 0.7));

        // The upper floors' pattern is the one picked.
        var a = HouseKitMaker.Make(d with { UpperFloor = "Floorboards" }).Pieces.Single(p => p.Name == "Floor 2");
        var c = HouseKitMaker.Make(d with { UpperFloor = "Square tiles" }).Pieces.Single(p => p.Name == "Floor 2");
        Assert.NotEqual(Geo.Area(a.Etched), Geo.Area(c.Etched), 0);
    }

    [Fact]
    public void A_wall_can_have_a_look_of_its_own_and_gets_a_column_where_it_changes()
    {
        var walls = Box(0, 0, 6, 3);
        var own = Line(0, 0, 3, 0).Select(e => new WallLook { Edge = e, Texture = "Brick", Wear = 0.9, DetailMm = 0.6 }).ToList();
        var d = new HouseDesign { Wear = 0.2, Levels = [new Level { Walls = walls, WallLooks = own }] };
        var kit = HouseKitMaker.Make(d);
        Assert.True(kit.Ok, string.Join("\n", kit.Findings.Select(f => f.Text)));
        // The top wall is cut where its look changes.
        Assert.Equal(5, kit.Walls.Count);
        Assert.Equal(5, d.Levels[0].Shape().Joints.Count);
        var brick = Assert.Single(kit.Walls, w => w.Spec.Texture == "Brick");
        Assert.Equal(0.9, brick.Spec.Wear, 6);
        Assert.Equal(0.6, brick.Spec.DetailMm, 6);
        Assert.Contains(new GridPt(3, 0), new[] { brick.A, brick.B });
        Assert.All(kit.Walls.Where(w => w != brick), w =>
        {
            Assert.Equal(HouseDesign.DefaultStyle().Texture, w.Spec.Texture);
            Assert.Equal(0.2, w.Spec.Wear, 6);
        });
        Assert.Empty(Clashes(kit.Solids.Where(s => s.Kind is SolidKind.Wall or SolidKind.Column).ToList(), step: 0.7));

        // Saved, opened and moved with the design.
        var back = HouseDesign.FromJson(d.Moved(2, 1).ToJson())!;
        Assert.Equal(0.2, back.Wear, 6);
        Assert.Equal(3, back.Levels[0].WallLooks.Count);
        Assert.Equal("Brick", back.Levels[0].LookOf(WallEdge.Of(new(2, 1), new(3, 1)))?.Texture);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Columns_take_the_shape_picked_for_them_where_it_suits(bool box)
    {
        // A square room with one corner cut off corner to corner: walls meet there at 135°.
        var walls = Line(0, 0, 4, 0).Concat(Line(4, 0, 4, 2)).Concat(Line(4, 2, 2, 4)).Concat(Line(2, 4, 0, 4)).Concat(Line(0, 4, 0, 0)).ToList();
        var style = HouseDesign.DefaultStyle();
        if (!box) style = style with { BoxWalls = false, ColumnMm = 12 };
        var d = new HouseDesign
        {
            Style = style with { Column = ColumnShape.Round },
            Levels = [new Level { Walls = walls, Columns = [new ColumnPick { At = new(0, 0), Shape = ColumnShape.Octagon }, new ColumnPick { At = new(4, 0), Shape = ColumnShape.Chamfered }] }],
        };
        var kit = HouseKitMaker.Make(d);
        Assert.True(kit.Ok, string.Join("\n", kit.Findings.Select(f => f.Text)));
        var level = d.Levels[0];
        var plan = level.Shape();
        ColumnShape At(int x, int y) => HouseKitMaker.ColumnShapeAt(level, d.Style, new(x, y), plan.Joints[new(x, y)]);
        Assert.Equal(ColumnShape.Octagon, At(0, 0));
        Assert.Equal(ColumnShape.Chamfered, At(4, 0));
        Assert.Equal(ColumnShape.Round, At(0, 4));
        Assert.Equal(ColumnShape.Round, At(4, 2));
        Assert.Contains(kit.Pieces, p => p.Name.Contains("octagonal", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(kit.Pieces, p => p.Name.Contains("round", StringComparison.OrdinalIgnoreCase));
        // A shape that doesn't suit: chamfered at 135° is made to suit (an octagon), and a note says so.
        var odd = d with { Levels = [level with { Columns = [new ColumnPick { At = new(4, 2), Shape = ColumnShape.Chamfered }] }] };
        Assert.Equal(ColumnShape.Octagon, HouseKitMaker.ColumnShapeAt(odd.Levels[0], d.Style, new(4, 2), plan.Joints[new(4, 2)]));
        Assert.Contains(HouseKitMaker.Make(odd).Findings, f => f.Text.StartsWith("Chamfered columns don't suit", StringComparison.Ordinal));
        Assert.Empty(Clashes(kit.Solids.Where(s => s.Kind is SolidKind.Wall or SolidKind.Column).ToList(), step: 0.7));
        Assert.Equal(2, HouseDesign.FromJson(d.Moved(1, 1).ToJson())!.Levels[0].Columns.Count(c => c.At.X >= 1));
    }

    static List<V3> Corners(HouseSolid s)
    {
        var b = Geo.Bounds(s.Outline);
        var list = new List<V3>();
        foreach (var u in new[] { b.left, b.right })
            foreach (var v in new[] { b.top, b.bottom })
                foreach (var w in new[] { -s.Thickness / 2, s.Thickness / 2 })
                    list.Add(s.Pose.At(u, v, w));
        return list;
    }

    static bool Fits(Piece p, double bed = 220, double gap = 2)
    {
        var b = Geo.Bounds(p.Cut);
        return Math.Max(b.right - b.left, b.bottom - b.top) + 2 * gap <= bed + 1e-6;
    }

    static (V3 Min, V3 Max) BoxOf(HouseSolid s)
    {
        var b = Geo.Bounds(s.Outline);
        var pts = new List<V3>();
        foreach (var u in new[] { b.left, b.right })
            foreach (var v in new[] { b.top, b.bottom })
                foreach (var w in new[] { -s.Thickness / 2, s.Thickness / 2 })
                    pts.Add(s.Pose.At(u, v, w));
        return (new(pts.Min(q => q.X), pts.Min(q => q.Y), pts.Min(q => q.Z)), new(pts.Max(q => q.X), pts.Max(q => q.Y), pts.Max(q => q.Z)));
    }

    /// <summary>Places where two solids would be in the same space (sampled where their boxes meet).</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_door_that_opens_swings_from_the_side_it_is_hinged(bool box)
    {
        var style = HouseDesign.DefaultStyle();
        if (!box) style = style with { BoxWalls = false, ColumnMm = 12 };
        var start = HouseDesign.Starter() with { Style = style };
        HouseDesign With(HingeSide side) => start with
        {
            Levels = [start.Levels[0] with { Openings = [.. start.Levels[0].Openings.Select(o => o.Kind == OpeningKind.Door ? o with { Opens = true, Shape = OpeningShape.Arched, Hinge = side } : o)] }],
        };
        var left = HouseKitMaker.Make(With(HingeSide.Left));
        var right = HouseKitMaker.Make(With(HingeSide.Right));
        Assert.True(left.Ok, string.Join("\n", left.Findings.Select(f => f.Text)));
        Assert.DoesNotContain(left.Findings, f => f.Level == Severity.Warn && f.Text.Contains("door", StringComparison.OrdinalIgnoreCase));
        if (!box)
        {
            // A flat wall has just the doorway: the door hangs on clips beside it, cut from the furniture's sheet.
            Assert.Contains(left.Walls, w => w.Spec.Door == DoorKind.Doorway);
            Assert.DoesNotContain(left.Walls, w => w.Spec.Door == DoorKind.DoorwayAndDoor);
            Assert.Contains(left.Pieces, p => p.Sheet == Sheet.Furniture && p.Name.EndsWith(": door clip", StringComparison.Ordinal) && p.Copies == 2);
        }
        else Assert.True(left.Walls.Single(w => w.Spec.Door == DoorKind.DoorwayAndDoor).Spec.DoorOpens);

        // Shown open, the leaf turns about its hinge: the hinge stays put, and it swings out the other way when hinged on the other side.
        HouseSolid Leaf(HouseKit k) => k.Solids.Single(x => x.Open is not null && x.Name.EndsWith("door", StringComparison.Ordinal));
        var l = Leaf(left);
        var r = Leaf(right);
        var (lo, ro) = (l.Open!, r.Open!);
        Assert.True(Math.Abs(lo.U.Dot(l.Pose.U)) < 0.5, "the leaf turns well away from the wall");
        Assert.True(lo.U.Dot(l.Facing) > 0.5 || lo.U.Dot(l.Facing) < -0.5);
        // Its far edge goes out of the building (the way the wall faces), whichever side it's hinged.
        V3 Far(HouseSolid x, Pose at, bool hingedRight)
        {
            var b = Geo.Bounds(x.Outline);
            return at.At(hingedRight ? b.left : b.right, (b.top + b.bottom) / 2);
        }
        Assert.True((Far(l, lo, false) - Far(l, l.Pose, false)).Dot(l.Facing) > 1);
        Assert.True((Far(r, ro, true) - Far(r, r.Pose, true)).Dot(r.Facing) > 1);
        Assert.True(lo.U.Dot(ro.U) < 0.5, "hinged on the other side it turns the other way");
    }

    [Fact]
    public void Each_floor_can_be_kept_to_a_yard_round_its_walls()
    {
        var style = HouseDesign.DefaultStyle();
        double sq = 25.4, C = style.ColumnMm;
        // A long ground floor, and a smaller room on the left of it upstairs.
        var d = new HouseDesign
        {
            Base = BaseKind.Plate,
            Levels = [new Level { Walls = Box(0, 0, 6, 3) }, new Level { Walls = Box(0, 0, 3, 3) }],
        };
        double Width(HouseKit k, string name) { var b = Geo.Bounds(k.Pieces.Single(p => p.Name == name).Cut); return b.right - b.left; }
        var drawn = HouseKitMaker.Make(d);
        Assert.Equal(6 * sq + C, Width(drawn, "Floor 2"), 0.5);
        Assert.Equal(6 * sq + C + 4, Width(drawn, "Base"), 0.5);

        // No yard upstairs: its floor stops at its walls, and the rooms below past it want a roof.
        HouseDesign Yards(int? ground, int? upper) => d with { Levels = [d.Levels[0] with { Yard = ground }, d.Levels[1] with { Yard = upper }] };
        var none = HouseKitMaker.Make(Yards(null, 0));
        Assert.Equal(3 * sq + C, Width(none, "Floor 2"), 0.5);
        Assert.Contains(none.Findings, f => f.Text.Contains("yard stops short", StringComparison.Ordinal));
        var plans = new[] { d.Levels[0].Shape(), d.Levels[1].Shape() };
        Assert.Empty(HouseKitMaker.RailingRuns(d.Levels[1] with { Yard = 0 }, plans[0], plans[1], style.WallSheetMm, C));
        // A roof over them and the note goes.
        var roofed = HouseKitMaker.Make(Yards(null, 0) with { Roofs = [new RoofArea { Level = 0, X0 = 3, Y0 = 0, X1 = 6, Y1 = 3, Kind = RoofKind.Flat }] });
        Assert.DoesNotContain(roofed.Findings, f => f.Text.Contains("yard stops short", StringComparison.Ordinal));
        Assert.DoesNotContain(roofed.Findings, f => f.Text.Contains("under floor 2", StringComparison.Ordinal));

        // A yard of one square: the floor reaches a square past the walls, with a railing round its edge.
        var one = HouseKitMaker.Make(Yards(null, 1));
        Assert.Equal(4 * sq + C, Width(one, "Floor 2"), 0.5);
        var runs = HouseKitMaker.RailingRuns(d.Levels[1] with { Yard = 1 }, plans[0], plans[1], style.WallSheetMm, C);
        Assert.Equal(5, runs.Sum(r => r.Steps));

        // On the ground floor the base reaches the yard all round.
        var ground = HouseKitMaker.Make(Yards(2, null), longestMm: 400);
        Assert.Equal(10 * sq + C + 4, Width(ground, "Base"), 0.5);
        var b = Geo.Bounds(ground.Pieces.Single(p => p.Name == "Base").Cut);
        Assert.Equal(7 * sq + C + 4, b.bottom - b.top, 0.5);

        // Saved and opened again.
        Assert.Equal(1, HouseDesign.FromJson(Yards(null, 1).ToJson())!.Levels[1].Yard);
        Assert.Null(HouseDesign.FromJson(d.ToJson())!.Levels[1].Yard);
    }

    [Fact]
    public void Furniture_stands_where_it_is_put_turned_and_crowding_is_warned_of()
    {
        var style = HouseDesign.DefaultStyle();
        var table = HouseKitMaker.NewItem(style, ItemKind.Table);
        var bed = HouseKitMaker.NewItem(style, ItemKind.Bed);
        var d = new HouseDesign
        {
            Levels = [new Level { Walls = Box(0, 0, 6, 4), Furniture = [new PlacedItem { X = 1.5, Y = 2, Item = table }, new PlacedItem { X = 3.8, Y = 2, Turn = 1, Item = bed }] }],
        };
        var kit = HouseKitMaker.Make(d);
        Assert.True(kit.Ok, string.Join("\n", kit.Findings.Select(f => f.Text)));
        Assert.DoesNotContain(kit.Findings, f => f.Level == Severity.Warn);
        Assert.Contains(kit.Pieces, p => p.Sheet == Sheet.Furniture && p.Name.StartsWith("Table: ", StringComparison.Ordinal));
        Assert.Contains(kit.Pieces, p => p.Sheet == Sheet.Furniture && p.Name.StartsWith("Bed: ", StringComparison.Ordinal));
        // Each stands over where it was put on the plan, the bed turned a quarter so it's long front to back.
        var (ox, oy) = kit.Origin;
        (double X0, double Z0, double X1, double Z1) Span(string name)
        {
            var pts = kit.Solids.Where(x => x.Name == name).SelectMany(x => x.Outline.SelectMany(path => path.Select(q => x.Pose.At(q.x, q.y)))).ToList();
            return (pts.Min(p => p.X), pts.Min(p => p.Z), pts.Max(p => p.X), pts.Max(p => p.Z));
        }
        double sq = d.SquareMm;
        var t = Span("Table");
        Assert.Equal((1.5 - ox) * sq, (t.X0 + t.X1) / 2, 1.0);
        Assert.Equal((2 - oy) * sq, (t.Z0 + t.Z1) / 2, 1.0);
        var b = Span("Bed");
        Assert.Equal((3.8 - ox) * sq, (b.X0 + b.X1) / 2, 1.0);
        // Turned a quarter, it takes the floor the other way round, as the plan shows it.
        var area = HouseKitMaker.ItemArea(style, d.Levels[0].Furniture[1], sq);
        var straight = HouseKitMaker.ItemArea(style, d.Levels[0].Furniture[1] with { Turn = 0 }, sq);
        Assert.Equal(straight.right - straight.left, area.bottom - area.top, 6);
        Assert.Equal((area.right - area.left) * sq, b.X1 - b.X0, 1.5);
        Assert.Equal((area.bottom - area.top) * sq, b.Z1 - b.Z0, 1.5);
        // The same piece twice is cut twice from one set of parts.
        var two = HouseKitMaker.Make(d with { Levels = [d.Levels[0] with { Furniture = [.. d.Levels[0].Furniture, new PlacedItem { X = 1.8, Y = 2.3, Item = table }] }] });
        Assert.Equal(2 * kit.Pieces.Where(p => p.Name.StartsWith("Table: ", StringComparison.Ordinal)).Sum(p => p.Copies),
            two.Pieces.Where(p => p.Name.StartsWith("Table: ", StringComparison.Ordinal)).Sum(p => p.Copies));
        Assert.Contains(two.Findings, f => f.Level == Severity.Warn && f.Text.Contains("runs into the table", StringComparison.Ordinal));
        // In a wall, or off the floor, it's warned of.
        var bad = HouseKitMaker.Make(d with { Levels = [d.Levels[0] with { Furniture = [new PlacedItem { X = 0, Y = 2, Item = table }, new PlacedItem { X = 8, Y = 2, Item = table }] }] });
        Assert.Contains(bad.Findings, f => f.Text.Contains("stands in a wall", StringComparison.Ordinal));
        Assert.Contains(bad.Findings, f => f.Text.Contains("off the edge of the floor", StringComparison.Ordinal));
        // Saved, opened and moved with the design.
        var back = HouseDesign.FromJson(d.Moved(1, 1).ToJson())!;
        Assert.Equal(2.5, back.Levels[0].Furniture[0].X, 6);
        Assert.Equal(1, back.Levels[0].Furniture[1].Turn);
        Assert.Equal(ItemKind.Bed, back.Levels[0].Furniture[1].Item.Kind);
    }

    [Theory]
    [InlineData(WayKind.Stairs)]
    [InlineData(WayKind.Ladder)]
    [InlineData(WayKind.Spiral)]
    public void A_way_up_goes_through_an_opening_cut_in_the_floor_above(WayKind kind)
    {
        var walls = Box(0, 0, 6, 4);
        var d = new HouseDesign
        {
            Levels = [new Level { Walls = walls, WaysUp = [new PlacedWay { X = 3, Y = 2, Kind = kind }] }, new Level { Walls = walls }],
        };
        var kit = HouseKitMaker.Make(d);
        Assert.True(kit.Ok, string.Join("\n", kit.Findings.Select(f => f.Text)));
        Assert.DoesNotContain(kit.Findings, f => f.Level == Severity.Warn);
        string name = HouseKitMaker.WayName(kind);
        Assert.Contains(kit.Pieces, p => p.Name.StartsWith(name + ": ", StringComparison.Ordinal));
        Assert.Contains(kit.Solids, x => x.Name == name);
        // The floor above has a hole where it comes up.
        var floor = kit.Pieces.Single(p => p.Name == "Floor 2");
        var plain = HouseKitMaker.Make(d with { Levels = [d.Levels[0] with { WaysUp = [] }, d.Levels[1]] }).Pieces.Single(p => p.Name == "Floor 2");
        Assert.True(Geo.Area(floor.Cut) < Geo.Area(plain.Cut) - 100);
        var (hatch, foot) = HouseKitMaker.WayArea(d.Style, d.Levels[0].WaysUp[0], d.SquareMm);
        Assert.True(Geo.Area(hatch) > 100);
        Assert.True(Geo.Area(foot) > 0);

        // On the top floor it leads nowhere; into a wall it's warned of.
        var top = HouseKitMaker.Make(d with { Levels = [d.Levels[0]] });
        Assert.Contains(top.Findings, f => f.Text.Contains("lead nowhere", StringComparison.Ordinal));
        var walled = HouseKitMaker.Make(d with { Levels = [d.Levels[0] with { WaysUp = [new PlacedWay { X = 0.4, Y = 2, Kind = kind }] }, d.Levels[1]] });
        Assert.Contains(walled.Findings, f => f.Level == Severity.Warn && f.Text.Contains("wall", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Columns_can_be_boxed_whatever_their_shape_with_a_texture_of_their_own(bool box)
    {
        // A room with a corner cut off corner to corner: octagonal columns suit every joint.
        var walls = Line(0, 0, 8, 0).Concat(Line(8, 0, 8, 4)).Concat(Line(8, 4, 4, 8)).Concat(Line(4, 8, 0, 8)).Concat(Line(0, 8, 0, 0)).ToList();
        var style = HouseDesign.DefaultStyle();
        style = box ? style with { ColumnMm = 50 } : style with { BoxWalls = false, ColumnMm = 28 };
        var d = new HouseDesign
        {
            Style = style with { Column = ColumnShape.Octagon },
            ColumnBuild = ColumnMade.Boxed, ColumnTexture = "Brick",
            Levels = [new Level { Walls = walls, Columns = [new ColumnPick { At = new(0, 0), Build = ColumnMade.Stacked }] }],
        };
        var kit = HouseKitMaker.Make(d);
        Assert.True(kit.Ok, string.Join("\n", kit.Findings.Select(f => f.Text)));
        // Four boxed columns: a slotted base and top plate each, and eight sides; the one picked stacked is layers.
        var boxedPieces = kit.Pieces.Where(p => p.Name.Contains("(brick)", StringComparison.Ordinal)).ToList();
        Assert.Equal(4, boxedPieces.Where(p => p.Name.EndsWith(" top", StringComparison.Ordinal)).Sum(p => p.Copies));
        Assert.Equal(4, boxedPieces.Where(p => p.Name.EndsWith(" base", StringComparison.Ordinal)).Sum(p => p.Copies));
        int sides = boxedPieces.Where(p => p.Name.Contains("side", StringComparison.Ordinal)).Sum(p => p.Copies);
        Assert.Equal(4 * 8 + (box ? 0 : 4 * 2), sides);
        Assert.Contains(boxedPieces, p => p.Name.EndsWith("blank side", StringComparison.Ordinal) && p.Etched.Count > 0);
        Assert.Contains(kit.Pieces, p => p.Name.Contains("layer", StringComparison.Ordinal));
        // The plates hold the sides' tabs in slots.
        var top = boxedPieces.First(p => p.Name.EndsWith(" top", StringComparison.Ordinal));
        Assert.True(top.Cut.Count >= 7, "the top plate has a slot for the tab of each side no wall comes into");
        Assert.Empty(Clashes(kit.Solids.Where(x => x.Kind is SolidKind.Wall or SolidKind.Column).ToList(), step: 0.7));
        // Columns too thin for octagonal boxes say how wide they must be.
        var thin = HouseKitMaker.Make(d with { Style = d.Style with { ColumnMm = box ? 30 : 16 } });
        Assert.Contains(thin.Findings, f => f.Level == Severity.Bad && f.Text.Contains("must be at least", StringComparison.Ordinal));
        // Saved and opened again.
        var back = HouseDesign.FromJson(d.ToJson())!;
        Assert.Equal(ColumnMade.Boxed, back.ColumnBuild);
        Assert.Equal("Brick", back.ColumnTexture);
        Assert.Equal(ColumnMade.Stacked, back.Levels[0].PickAt(new(0, 0))?.Build);
        Assert.Null(back.Levels[0].PickAt(new(0, 0))?.Shape);
    }

    static List<string> Clashes(IReadOnlyList<HouseSolid> all, double step = 0.5)
    {
        var found = new List<string>();
        var shrunk = all.Select(s => Geo.Offset(s.Outline, -0.05)).ToList();
        var boxes = all.Select(BoxOf).ToList();
        bool Inside(int i, V3 p)
        {
            var (u, v, w) = all[i].Pose.Local(p);
            return Math.Abs(w) <= all[i].Thickness / 2 - 0.05 && OutlineGrid.In(shrunk[i], u, v);
        }
        for (int i = 0; i < all.Count; i++)
            for (int j = i + 1; j < all.Count; j++)
            {
                var (a0, a1) = boxes[i];
                var (b0, b1) = boxes[j];
                double x0 = Math.Max(a0.X, b0.X), y0 = Math.Max(a0.Y, b0.Y), z0 = Math.Max(a0.Z, b0.Z);
                double x1 = Math.Min(a1.X, b1.X), y1 = Math.Min(a1.Y, b1.Y), z1 = Math.Min(a1.Z, b1.Z);
                if (x1 - x0 < 0.1 || y1 - y0 < 0.1 || z1 - z0 < 0.1) continue;
                for (double px = x0 + step / 2; px < x1; px += step)
                    for (double py = y0 + step / 2; py < y1; py += step)
                        for (double pz = z0 + step / 2; pz < z1; pz += step)
                        {
                            var p = new V3(px, py, pz);
                            if (Inside(i, p) && Inside(j, p)) { found.Add($"{all[i].Name} and {all[j].Name} at {px:0.##}, {py:0.##}, {pz:0.##}"); goto next; }
                        }
                next:;
            }
        return found;
    }
}
