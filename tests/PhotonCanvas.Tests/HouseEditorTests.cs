using Clipper2Lib;
using PhotonCanvas.Addon.Buildings;
using PhotonCanvas.Addon.Furniture;
using PhotonCanvas.Addon.HouseEditor;
using PhotonCanvas.Addon.Walls;
using Geo = PhotonCanvas.Addon.Walls.Geo;
using Severity = PhotonCanvas.Addon.Buildings.Severity;

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
