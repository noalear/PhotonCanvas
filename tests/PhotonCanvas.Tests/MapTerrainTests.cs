using Clipper2Lib;
using PhotonCanvas.Addon.Buildings;
using PhotonCanvas.Addon.Furniture;
using PhotonCanvas.Addon.MapTerrain;
using PhotonCanvas.Addon.Walls;
using PhotonCanvas.Core;
using PhotonCanvas.Core.Imaging;
using PhotonCanvas.Doc;
using Geo = PhotonCanvas.Addon.Walls.Geo;
using Severity = PhotonCanvas.Addon.Buildings.Severity;

namespace PhotonCanvas.Tests;

public class MapTerrainTests
{
    // ---- plans to try ----

    static PlanGrid Rooms(int across, int down, params (int X0, int Y0, int X1, int Y1)[] rooms)
    {
        var g = new PlanGrid(across, down);
        foreach (var (x0, y0, x1, y1) in rooms)
            for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++) g[x, y] = true;
        return g;
    }

    static MapSpec With(PlanGrid g) => new MapSpec { Plan = g.ToPlan() };

    public static IEnumerable<object[]> Themes() => MapSpec.Themes.Select(t => new object[] { t.Name });

    // ---- the plan ----

    [Fact]
    public void Walls_go_round_the_floor_by_themselves_and_marks_change_them()
    {
        var g = Rooms(4, 3, (1, 1, 2, 1));
        Assert.Equal(EdgeState.Wall, g.State(new EdgeId(1, 1, true)));    // top of B2
        Assert.Equal(EdgeState.None, g.State(new EdgeId(2, 1, false)));   // between B2 and C2
        Assert.Equal(EdgeState.None, g.State(new EdgeId(0, 0, true)));    // no floor either side
        g.SetMark(new EdgeId(2, 1, false), EdgeKind.Wall);
        g.SetMark(new EdgeId(1, 1, true), EdgeKind.Open);
        g.SetMark(new EdgeId(2, 2, true), EdgeKind.Door);
        g.SetMark(new EdgeId(0, 0, true), EdgeKind.Wall);
        Assert.Equal(EdgeState.Wall, g.State(new EdgeId(2, 1, false)));
        Assert.Equal(EdgeState.None, g.State(new EdgeId(1, 1, true)));
        Assert.Equal(EdgeState.Door, g.State(new EdgeId(2, 2, true)));
        Assert.Equal(EdgeState.None, g.State(new EdgeId(0, 0, true)));
        // Saved and read back the same; the mark with no floor beside it isn't kept.
        var back = MapSpec.FromJson(With(g).ToJson())!.Plan;
        Assert.Equal(3, back.Marks.Count);
        var g2 = back.Grid();
        foreach (var e in g.Edges()) Assert.Equal(g.State(e), g2.State(e));
        Assert.Equal("between B2 and C2", MapPlan.Name(new EdgeId(2, 1, false), 4, 3));
        Assert.Equal("on the top of A1", MapPlan.Name(new EdgeId(0, 0, true), 4, 3));
    }

    [Fact]
    public void Resizing_and_trimming_keep_the_plan()
    {
        var g = Rooms(10, 8, (3, 2, 5, 4));
        g.SetMark(new EdgeId(4, 2, true), EdgeKind.Door);
        var t = g.Trimmed(1);
        Assert.Equal(5, t.Across);
        Assert.Equal(5, t.Down);
        Assert.Equal(9, t.FloorCount);
        Assert.Equal(EdgeState.Door, t.State(new EdgeId(2, 1, true)));
        var r = g.Resized(5, 4);
        Assert.Equal(4, r.FloorCount);
    }

    // ---- the walls ----

    [Fact]
    public void A_room_gets_four_corner_columns_and_long_walls_are_split()
    {
        var spec = With(Rooms(8, 5, (1, 1, 6, 3))) with { LongestSquares = 4 };
        var lay = MapKitMaker.Layout(spec);
        Assert.Equal(4, lay.Columns.Count(c => c.Kind == ConnectorKind.Corner));
        // The 6-square walls are split in two (3 + 3); the 3-square ones aren't.
        Assert.Equal(2, lay.Columns.Count(c => c.Kind == ConnectorKind.Straight));
        Assert.All(lay.Panels, p => Assert.Equal(3, p.Squares));
        Assert.Equal(6, lay.Panels.Count);
        // Every panel's texture looks into the room.
        var g = spec.Plan.Grid();
        foreach (var p in lay.Panels)
        {
            var (a, b) = p.Edge(0).Sides;
            Assert.Equal(g[b.X, b.Y], p.FacesB);
            Assert.NotEqual(g[a.X, a.Y], p.FacesB);
        }
    }

    [Fact]
    public void A_wall_across_a_room_makes_T_junctions_and_ends_make_end_columns()
    {
        var g = Rooms(6, 6, (0, 0, 4, 4));
        // Across the room from wall to wall: two T's.
        for (int y = 0; y < 5; y++) g.SetMark(new EdgeId(2, y, false), EdgeKind.Wall);
        var lay = MapKitMaker.Layout(With(g));
        Assert.Equal(2, lay.Columns.Count(c => c.Kind == ConnectorKind.Tee));
        Assert.Equal(4, lay.Columns.Count(c => c.Kind == ConnectorKind.Corner));
        // A short wall sticking out from the side: a T where it meets the wall, an end where it stops.
        var g2 = Rooms(6, 6, (0, 0, 4, 4));
        g2.SetMark(new EdgeId(0, 2, true), EdgeKind.Wall);
        g2.SetMark(new EdgeId(1, 2, true), EdgeKind.Wall);
        var lay2 = MapKitMaker.Layout(With(g2));
        Assert.Single(lay2.Columns, c => c.Kind == ConnectorKind.Tee);
        var end = Assert.Single(lay2.Columns, c => c.Kind == ConnectorKind.EndCap);
        Assert.Equal((2, 2), (end.X, end.Y));
        Assert.Equal(180, end.Turn);   // its one slot points left, back along the wall
        // A cross where two walls cross in the middle.
        var g3 = Rooms(6, 6, (0, 0, 3, 3));
        for (int i = 0; i < 4; i++) { g3.SetMark(new EdgeId(2, i, false), EdgeKind.Wall); g3.SetMark(new EdgeId(i, 2, true), EdgeKind.Wall); }
        Assert.Single(MapKitMaker.Layout(With(g3)).Columns, c => c.Kind == ConnectorKind.Cross);
    }

    [Fact]
    public void Every_column_has_a_slot_for_each_wall_that_meets_it()
    {
        var spec = MapSpec.Sample();
        var lay = MapKitMaker.Layout(spec);
        var at = lay.Columns.ToDictionary(c => (c.X, c.Y));
        foreach (var p in lay.Panels)
        {
            double out0 = p.Across ? 0 : 270, back = p.Across ? 180 : 90;
            Assert.Contains(out0, at[(p.X, p.Y)].Dirs);
            Assert.Contains(back, at[p.End].Dirs);
        }
        // The slots as made point the same ways.
        foreach (var c in lay.Columns)
        {
            var made = Connectors.Directions(c.Kind, 0).Select(d => (d + c.Turn) % 360).OrderBy(d => d);
            Assert.Equal(c.Dirs.OrderBy(d => d), made);
        }
    }

    [Fact]
    public void Each_door_gets_a_panel_of_its_own_and_keeps_clear_of_the_columns()
    {
        var spec = MapSpec.Sample();
        var g = spec.Plan.Grid();
        var lay = MapKitMaker.Layout(spec);
        int doors = g.Edges().Count(e => g.State(e) == EdgeState.Door);
        Assert.Equal(doors, lay.Panels.Count(p => p.Door >= 0) + lay.OpenDoors.Count);
        foreach (var p in lay.Panels.Where(p => p.Door >= 0))
        {
            Assert.Equal(1, Enumerable.Range(0, p.Squares).Count(i => g.State(p.Edge(i)) == EdgeState.Door));
            if (!p.Narrow) Assert.True(Math.Abs(p.DoorOffsetMm) <= MapKitMaker.DoorRoom(spec, p.Squares) + 1e-9);
        }
        // Two doors side by side on a long wall go in two panels.
        var two = Rooms(8, 4, (0, 0, 7, 2));
        two.SetMark(new EdgeId(3, 0, true), EdgeKind.Door);
        two.SetMark(new EdgeId(4, 0, true), EdgeKind.Door);
        var lay2 = MapKitMaker.Layout(With(two) with { LongestSquares = 4 });
        Assert.Equal(2, lay2.Panels.Count(p => p.Door >= 0));
        Assert.Empty(lay2.OpenDoors);
    }

    [Fact]
    public void A_door_where_a_corridor_meets_a_room_is_made_narrow_to_fit()
    {
        var g = Rooms(5, 5, (2, 0, 2, 3));
        g.SetMark(new EdgeId(2, 0, true), EdgeKind.Door);
        var spec = With(g);
        var lay = MapKitMaker.Layout(spec);
        var p = Assert.Single(lay.Panels, p => p.Door >= 0);
        Assert.True(p.Narrow);
        Assert.InRange(p.NarrowWidthMm, DoorFit.MinNarrowMm, spec.DoorWidthMm);
        var kit = MapKitMaker.Make(spec);
        Assert.True(kit.Ok, string.Join("\n", kit.Findings.Select(f => f.Text)));
        Assert.Contains(kit.Pieces, x => x.Name == "Narrow door");
    }

    [Fact]
    public void A_door_with_no_room_is_left_as_an_open_doorway()
    {
        // A corridor (open at the far end) into a room, with columns too wide to leave room for any door between them.
        var g = Rooms(5, 5, (2, 0, 2, 1), (0, 2, 4, 4));
        g.SetMark(new EdgeId(2, 2, true), EdgeKind.Door);
        g.SetMark(new EdgeId(2, 0, true), EdgeKind.Open);
        var spec = With(g) with { ColumnMm = 20 };
        var lay = MapKitMaker.Layout(spec);
        Assert.Single(lay.OpenDoors);
        Assert.DoesNotContain(lay.Panels, p => p.Door >= 0);
        // No wall there now, so the columns either side are corners, not T's.
        Assert.Equal(6, lay.Columns.Count(c => c.Kind == ConnectorKind.Corner));
        Assert.DoesNotContain(lay.Columns, c => c.Kind == ConnectorKind.Tee);
        var kit = MapKitMaker.Make(spec);
        Assert.True(kit.Ok, string.Join("\n", kit.Findings.Select(f => f.Text)));
        Assert.Contains(kit.Findings, f => f.Level == Severity.Warn && f.Text.Contains("open doorway", StringComparison.Ordinal));
    }

    // ---- the whole kit ----

    [Theory]
    [MemberData(nameof(Themes))]
    public void Every_look_makes_a_whole_kit_that_fits_the_bed(string theme)
    {
        var spec = MapSpec.Sample().WithTheme(theme);
        var kit = MapKitMaker.Make(spec);
        Assert.True(kit.Ok, string.Join("\n", kit.Findings.Select(f => f.Text)));
        Assert.DoesNotContain(kit.Findings, f => f.Level == Severity.Warn);
        Assert.Empty(MapPlacement.TooBig(kit, MachineProfile.F2Ultra));
        foreach (var p in kit.Pieces) Assert.True(Geo.Pieces(p.Cut) == 1, $"{p.Name} comes out in {Geo.Pieces(p.Cut)} pieces");
        Assert.Contains(kit.Pieces, p => p.Name.StartsWith("Floor tile", StringComparison.Ordinal));
        Assert.Contains(kit.Pieces, p => p.Name.StartsWith("Wall", StringComparison.Ordinal) && p.Etched.Count + p.Lines.Count > 0);
        Assert.Contains(kit.Pieces, p => p.Sheet == Sheet.Furniture);
    }

    [Theory]
    [InlineData(DoorStyle.Swinging)]
    [InlineData(DoorStyle.Loose)]
    [InlineData(DoorStyle.Open)]
    public void Door_styles(DoorStyle style)
    {
        var spec = MapSpec.Sample() with { Door = style };
        var kit = MapKitMaker.Make(spec);
        Assert.True(kit.Ok, string.Join("\n", kit.Findings.Select(f => f.Text)));
        int doors = MapKitMaker.Layout(spec).Panels.Count(p => p.Door >= 0);
        Assert.Equal(style == DoorStyle.Swinging, kit.Pieces.Any(p => p.Sheet == Sheet.Furniture));
        var lay = MapKitMaker.Layout(spec);
        int narrow = lay.Panels.Count(p => p.Narrow);
        Assert.Equal(3, narrow);   // the corridor's two ends and the alcove
        int loose = kit.Pieces.Where(p => p.Name is "Door" or "Narrow door").Sum(p => p.Copies);
        if (style == DoorStyle.Open) { Assert.Equal(0, loose); Assert.DoesNotContain(kit.Solids, s => s.Name == "Door"); }
        else Assert.Equal(style == DoorStyle.Loose ? doors : narrow, loose);
        if (style == DoorStyle.Swinging) Assert.True(kit.Solids.Count(s => s.IsFurniture) >= doors - narrow);
    }

    [Fact]
    public void Walls_only_and_floor_only()
    {
        var walls = MapKitMaker.Make(MapSpec.Sample() with { Floor = false });
        Assert.True(walls.Ok);
        Assert.DoesNotContain(walls.Pieces, p => p.Name.StartsWith("Floor", StringComparison.Ordinal));
        // On the table, with no tiles under them.
        Assert.All(walls.Solids.Where(s => s.Name.StartsWith("Wall", StringComparison.Ordinal)), s => Assert.Equal(0, s.Pose.O.Y, 6));
        var floor = MapKitMaker.Make(MapSpec.Sample() with { Walls = false });
        Assert.True(floor.Ok);
        Assert.All(floor.Pieces, p => Assert.StartsWith("Floor tile", p.Name));
        Assert.False(MapKitMaker.Make(MapSpec.Sample() with { Walls = false, Floor = false }).Ok);
        Assert.False(MapKitMaker.Make(new MapSpec()).Ok);   // no floor painted
    }

    [Fact]
    public void Bad_settings_are_reported()
    {
        var s = MapSpec.Sample();
        Assert.False(MapKitMaker.Make(s with { ColumnMm = 4 }).Ok);
        Assert.False(MapKitMaker.Make(s with { SquareMm = 5 }).Ok);
        Assert.False(MapKitMaker.Make(s with { TabMm = 12 }).Ok);
        Assert.False(MapKitMaker.Make(s with { DoorHeightMm = 60 }).Ok);
    }

    // ---- in 3D ----

    static bool In(PathsD area, double u, double v)
    {
        int inside = 0;
        foreach (var p in area)
            if (Clipper.PointInPolygon(new PointD(u, v), p) == PointInPolygonResult.IsInside) inside++;
        return inside % 2 == 1;
    }

    static (V3 Min, V3 Max) Box(Solid s)
    {
        var b = Geo.Bounds(s.Outline);
        var pts = new List<V3>();
        foreach (var u in new[] { b.left, b.right })
            foreach (var v in new[] { b.top, b.bottom })
                foreach (var w in new[] { -s.Thickness / 2, s.Thickness / 2 })
                    pts.Add(s.Pose.At(u, v, w));
        return (new(pts.Min(q => q.X), pts.Min(q => q.Y), pts.Min(q => q.Z)), new(pts.Max(q => q.X), pts.Max(q => q.Y), pts.Max(q => q.Z)));
    }

    static bool Inside(Solid s, PathsD shrunk, V3 p, double eps = 0.03)
    {
        var (u, v, w) = s.Pose.Local(p);
        return Math.Abs(w) <= s.Thickness / 2 - eps && In(shrunk, u, v);
    }

    /// <summary>Places where two solids would be in the same space (sampled where their boxes meet).</summary>
    static List<string> Clashes(IReadOnlyList<Solid> a, IReadOnlyList<Solid> b, double step = 0.3)
    {
        var found = new List<string>();
        var sa = a.Select(s => (S: s, P: Geo.Offset(s.Outline, -0.03), B: Box(s))).ToList();
        var sb = b.Select(s => (S: s, P: Geo.Offset(s.Outline, -0.03), B: Box(s))).ToList();
        foreach (var x in sa)
            foreach (var y in sb)
            {
                if (ReferenceEquals(x.S, y.S)) continue;
                double x0 = Math.Max(x.B.Min.X, y.B.Min.X), y0 = Math.Max(x.B.Min.Y, y.B.Min.Y), z0 = Math.Max(x.B.Min.Z, y.B.Min.Z);
                double x1 = Math.Min(x.B.Max.X, y.B.Max.X), y1 = Math.Min(x.B.Max.Y, y.B.Max.Y), z1 = Math.Min(x.B.Max.Z, y.B.Max.Z);
                if (x1 - x0 < 0.06 || y1 - y0 < 0.06 || z1 - z0 < 0.06) continue;
                for (double px = x0 + step / 2; px < x1; px += step)
                    for (double py = y0 + step / 2; py < y1; py += step)
                        for (double pz = z0 + step / 2; pz < z1; pz += step)
                        {
                            var p = new V3(px, py, pz);
                            if (Inside(x.S, x.P, p) && Inside(y.S, y.P, p)) { found.Add($"{x.S.Name} and {y.S.Name} at {px:0.##}, {py:0.##}, {pz:0.##}"); goto next; }
                        }
                next:;
            }
        return found;
    }

    [Fact]
    public void Walls_slide_into_the_columns_and_stand_on_the_tiles_without_clashing()
    {
        var spec = MapSpec.Sample() with { Door = DoorStyle.Loose };
        var kit = MapKitMaker.Make(spec);
        Assert.True(kit.Ok, string.Join("\n", kit.Findings.Select(f => f.Text)));
        var walls = kit.Solids.Where(s => s.Name.StartsWith("Wall", StringComparison.Ordinal)).ToList();
        var columns = kit.Solids.Where(s => s.Name.Contains("column", StringComparison.Ordinal)).ToList();
        var tiles = kit.Solids.Where(s => s.Side == WallSide.Ground).ToList();
        Assert.NotEmpty(walls);
        Assert.Empty(Clashes(walls, columns));
        Assert.Empty(Clashes(walls, tiles));
        Assert.Empty(Clashes(columns, tiles));
        // Each end of each wall reaches into a column.
        bool InColumn(V3 p) => columns.Any(c =>
        {
            var (u, v, w) = c.Pose.Local(p);
            var cb = Geo.Bounds(c.Outline);
            return Math.Abs(w) <= c.Thickness / 2 && u > cb.left && u < cb.right && v > cb.top && v < cb.bottom;
        });
        foreach (var w in walls)
        {
            var b = Geo.Bounds(w.Outline);
            foreach (var u in new[] { b.left + 0.3, b.right - 0.3 })
            {
                var p = w.Pose.At(u, (b.top + b.bottom) / 4);
                Assert.True(InColumn(p), $"{w.Name}'s end at {p.X:0.#}, {p.Y:0.#}, {p.Z:0.#} isn't in a column");
            }
        }
        // The walls stand on the tiles.
        Assert.All(walls, w => Assert.Equal(spec.SheetMm, w.Pose.O.Y, 6));
    }

    /// <summary>The tiles' outlines laid flat in the plan's 3D ground (X, Z), as Clipper paths in X, −Z.</summary>
    static List<PathsD> TilesOnTheGround(BuildingKit kit) =>
        [.. kit.Solids.Where(s => s.Side == WallSide.Ground).Select(s => Geo.Map(s.Outline, q => { var p = s.Pose.At(q.x, q.y); return new PointD(p.X, -p.Z); }))];

    [Theory]
    [InlineData(MapJoin.Jigsaw)]
    [InlineData(MapJoin.Clips)]
    [InlineData(MapJoin.None)]
    public void Tiles_cover_the_floor_without_overlapping_and_reach_under_the_walls(MapJoin join)
    {
        var spec = MapSpec.Sample() with { Join = join };
        var kit = MapKitMaker.Make(spec);
        Assert.True(kit.Ok, string.Join("\n", kit.Findings.Select(f => f.Text)));
        var tiles = TilesOnTheGround(kit);
        var g = spec.Plan.Grid();
        double sq = spec.SquareMm;
        // The clips' sockets are filled by the clips, not the tiles.
        double play = join == MapJoin.Clips ? 70 : 6;
        // No two tiles in the same place (the joints' fit leaves them a little apart).
        for (int i = 0; i < tiles.Count; i++)
            for (int j = i + 1; j < tiles.Count; j++)
                Assert.True(Geo.Area(Geo.Intersect(tiles[i], tiles[j])) < 0.01, $"tiles {i} and {j} overlap");
        // Every floor square is covered (bar the joints' play).
        // All of them together, the joints' play closed up.
        var all = Geo.Offset(Geo.Offset(Geo.Union(new PathsD(tiles.SelectMany(t => t))), 0.2), -0.2);
        for (int y = 0; y < g.Down; y++)
            for (int x = 0; x < g.Across; x++)
            {
                var cell = new PathsD { Geo.Rect(x * sq, -(y + 1) * sq, (x + 1) * sq, -y * sq) };
                double covered = Geo.Area(Geo.Intersect(cell, all));
                if (g[x, y]) Assert.True(covered > sq * sq - play, $"{MapPlan.Square(x, y)} is only {covered:0} of {sq * sq:0} mm² covered");
            }
        // Every column stands on the tiles all over.
        foreach (var c in MapKitMaker.Layout(spec).Columns)
        {
            double r = spec.ColumnMm / 2;
            var foot = new PathsD { Geo.Rect(c.X * sq - r, -c.Y * sq - r, c.X * sq + r, -c.Y * sq + r) };
            Assert.True(Geo.Area(Geo.Subtract(foot, all)) < (join == MapJoin.Clips ? 30 : 0.5), $"the column at {c.X}, {c.Y} hangs off the tiles");
        }
        int clips = kit.Pieces.Where(p => p.Name == "Floor tile clip").Sum(p => p.Copies);
        Assert.Equal(join == MapJoin.Clips, clips > 0);
    }

    [Fact]
    public void Neighbouring_tiles_lock_together_with_jigsaw_joints()
    {
        var spec = With(Rooms(4, 2, (0, 0, 3, 1))) with { Walls = false, TileSquares = 2 };
        var kit = MapKitMaker.Make(spec);
        var tiles = TilesOnTheGround(kit);
        Assert.Equal(2, tiles.Count);
        // The left tile's tabs reach into the right one's squares, into its sockets.
        double sq = spec.SquareMm;
        var rightSquares = new PathsD { Geo.Rect(2 * sq, -2 * sq, 4 * sq, 0) };
        var left = tiles.OrderBy(t => Geo.Bounds(t).left).First();
        Assert.True(Geo.Area(Geo.Intersect(left, rightSquares)) > 5);
        // Plain round the outside: within the plan's outline.
        var outline = new PathsD { Geo.Rect(0, -2 * sq, 4 * sq, 0) };
        foreach (var t in tiles) Assert.True(Geo.Area(Geo.Subtract(t, outline)) < 1e-3);
    }

    // ---- reading a battle map ----

    /// <summary>A battle map: grid lines every <paramref name="p"/> pixels, light floor on dark rock, or a line drawing.</summary>
    static RasterImage Map(PlanGrid plan, int p, bool lines, int offset = 0, bool walls = false)
    {
        int w = plan.Across * p + offset, h = plan.Down * p + offset;
        var px = new byte[w * h * 4];
        var rnd = new Random(3);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int cx = (x - offset) / p, cy = (y - offset) / p;
                bool floor = x >= offset && y >= offset && plan[cx, cy];
                double v = lines ? 0.95 : floor ? 0.8 : 0.15;
                v += (rnd.NextDouble() - 0.5) * 0.08;
                if ((x - offset) % p == 0 || (y - offset) % p == 0) v -= lines ? 0.25 : floor ? 0.3 : 0.05;
                byte b = (byte)Math.Clamp(v * 255, 0, 255);
                int i = (y * w + x) * 4;
                px[i] = b; px[i + 1] = b; px[i + 2] = (byte)Math.Clamp(b * 0.9, 0, 255); px[i + 3] = 255;
            }
        if (walls)
        {
            // Thick dark lines on every wall of the plan.
            foreach (var e in plan.Edges().Where(e => plan.State(e) != EdgeState.None))
            {
                var (a, b) = e.Ends;
                for (int y = a.Y * p + offset - 3; y <= b.Y * p + offset + 3; y++)
                    for (int x = a.X * p + offset - 3; x <= b.X * p + offset + 3; x++)
                    {
                        if (x < 0 || y < 0 || x >= w || y >= h) continue;
                        int i = (y * w + x) * 4;
                        px[i] = px[i + 1] = px[i + 2] = 20;
                    }
            }
        }
        return new RasterImage(w, h, px);
    }

    static PlanGrid Dungeon()
    {
        var g = Rooms(12, 9, (1, 1, 4, 4), (5, 2, 7, 2), (8, 1, 10, 7), (2, 5, 3, 7));
        return g;
    }

    [Theory]
    [InlineData(40, 0)]
    [InlineData(70, 12)]
    [InlineData(33, 5)]
    public void The_grid_is_found(int p, int offset)
    {
        var img = Map(Dungeon(), p, false, offset);
        var grid = MapReader.FindGrid(img);
        Assert.NotNull(grid);
        Assert.Equal(p, grid!.SquarePx, 1.0);
        double Off(double o) { double r = o % p; return r > p / 2.0 ? r - p : r; }
        Assert.True(Math.Abs(Off(grid.OffsetX - offset)) <= 1.5 && Math.Abs(Off(grid.OffsetY - offset)) <= 1.5, $"found {grid.SquarePx} px from {grid.OffsetX}, {grid.OffsetY}");
    }

    [Fact]
    public void A_picture_with_no_grid_has_none_found()
    {
        var px = new byte[200 * 150 * 4];
        Array.Fill(px, (byte)200);
        Assert.Null(MapReader.FindGrid(new RasterImage(200, 150, px)));
        var g = MapReader.GridFor(new RasterImage(200, 150, px), 10);
        Assert.Equal(20, g.SquarePx, 3);
    }

    [Fact]
    public void A_filled_in_map_is_read_square_by_square()
    {
        var plan = Dungeon();
        var img = Map(plan, 50, false, 0);
        var r = MapReader.Read(img, MapReader.FindGrid(img)!, new ReadOptions());
        Assert.Equal(plan.Across, r.Plan.Across);
        Assert.Equal(plan.Down, r.Plan.Down);
        for (int y = 0; y < plan.Down; y++)
            for (int x = 0; x < plan.Across; x++) Assert.True(plan[x, y] == r.Plan[x, y], $"{MapPlan.Square(x, y)} read wrong");
        // Read the other way round when asked.
        var dark = MapReader.Read(img, MapReader.FindGrid(img)!, new ReadOptions { Floor = FloorTone.Darker });
        Assert.Equal(plan.Across * plan.Down - plan.FloorCount, dark.Plan.FloorCount);
    }

    [Fact]
    public void A_line_drawing_is_read_from_its_walls()
    {
        var plan = Rooms(10, 8, (1, 1, 4, 3), (5, 1, 8, 6));
        for (int y = 1; y <= 3; y++) plan.SetMark(new EdgeId(5, y, false), EdgeKind.Wall);
        var img = Map(plan, 40, true, 0, walls: true);
        var r = MapReader.Read(img, new MapPicture { SquarePx = 40 }, new ReadOptions());
        for (int y = 0; y < plan.Down; y++)
            for (int x = 0; x < plan.Across; x++) Assert.True(plan[x, y] == r.Plan[x, y], $"{MapPlan.Square(x, y)} read wrong");
        // The wall drawn between the two rooms is kept as a wall.
        Assert.Equal(EdgeState.Wall, r.Plan.State(new EdgeId(5, 2, false)));
        Assert.Equal(EdgeState.None, r.Plan.State(new EdgeId(3, 2, false)));
    }

    [Fact]
    public void Shapes_in_the_design_become_the_plan()
    {
        double g = 25.4;
        var room = new List<(double, double)> { (100, 50), (100 + 4 * g, 50), (100 + 4 * g, 50 + 3 * g), (100, 50 + 3 * g) };
        var hall = new List<(double, double)> { (100 + 4 * g, 50 + g), (100 + 6 * g, 50 + g), (100 + 6 * g, 50 + 2 * g), (100 + 4 * g, 50 + 2 * g) };
        var plan = MapReader.FromOutlines([room, hall], g);
        Assert.Equal(6, plan.Across);
        Assert.Equal(3, plan.Down);
        Assert.Equal(14, plan.FloorCount);
        Assert.True(plan[5, 1]);
        Assert.False(plan[5, 0]);
    }

    // ---- into the design ----

    [Fact]
    public void Goes_into_the_design_and_can_be_found_again()
    {
        var spec = MapSpec.Sample();
        var kit = MapKitMaker.Make(spec);
        var (s, pages, id) = MapPlacement.Place(DocState.New(), kit, spec, MachineProfile.F2Ultra, new MapPlaceOptions());
        Assert.True(pages >= 2);
        var names = Pages.Names(s);
        Assert.Contains(names, n => n.Contains("Map terrain (3 mm sheet)", StringComparison.Ordinal));
        Assert.Contains(names, n => n.Contains("doors (2 mm sheet)", StringComparison.Ordinal));
        int cuts = 0;
        for (int i = 0; i <= s.OtherPages.Count; i++)
        {
            var page = Pages.SwitchTo(s, i);
            var cutLayer = page.Layers.First(l => l.Name == MapPlacement.CutLayer).Id;
            var mine = page.Shapes.Where(x => x.LayerId == cutLayer).ToList();
            cuts += mine.Count;
            Assert.Equal(spec.ToJson(), MapPlacement.Find(page, [mine[^1].Id])!.Value.Spec.ToJson());
        }
        Assert.Equal(kit.PieceCount, cuts);
        // Replacing takes the old pieces off every page.
        var smaller = spec with { Floor = false };
        var (s2, _, id2) = MapPlacement.Place(s, MapKitMaker.Make(smaller), smaller, MachineProfile.F2Ultra, new MapPlaceOptions { Replace = id });
        Assert.Equal(id, id2);
        Assert.Equal(MapKitMaker.Make(smaller).PieceCount, s2.Shapes.Concat(s2.OtherPages.SelectMany(p => p.Shapes)).Count(x => !x.Name.Contains('·') && x.AddonData.ContainsKey(MapPlacement.IdKey)));
    }

    [Fact]
    public void A_big_map_is_made_in_reasonable_time()
    {
        var g = Rooms(30, 24, (1, 1, 28, 22));
        for (int x = 2; x < 28; x += 6) for (int y = 1; y < 23; y++) if (y % 7 != 3) g.SetMark(new EdgeId(x, y, false), EdgeKind.Wall);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var kit = MapKitMaker.Make(With(g));
        Assert.True(kit.Ok, string.Join("\n", kit.Findings.Select(f => f.Text)));
        Assert.True(sw.Elapsed.TotalSeconds < 60, $"took {sw.Elapsed.TotalSeconds:0.#} s");
    }
}
