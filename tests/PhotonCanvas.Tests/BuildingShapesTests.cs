using Clipper2Lib;
using PhotonCanvas.Addon.Buildings;
using PhotonCanvas.Addon.Furniture;
using PhotonCanvas.Addon.Walls;
using PhotonCanvas.Core;
using Geo = PhotonCanvas.Addon.Walls.Geo;
using Severity = PhotonCanvas.Addon.Buildings.Severity;
using Placement = PhotonCanvas.Addon.Buildings.Placement;
using V3 = PhotonCanvas.Addon.Furniture.V3;

namespace PhotonCanvas.Tests;

/// <summary>Buildings that aren't rectangles (an L, T or U, a hexagon, an octagon, a round building) and corner columns of other shapes.</summary>
public class BuildingShapesTests
{
    static readonly BuildingShape[] Others = [BuildingShape.LShape, BuildingShape.TShape, BuildingShape.UShape, BuildingShape.Hexagon, BuildingShape.Octagon, BuildingShape.Round];

    public static IEnumerable<object[]> ShapesAndWalls() => Others.SelectMany(s => new[] { new object[] { s, true }, new object[] { s, false } });
    public static IEnumerable<object[]> Shapes() => Others.Select(s => new object[] { s });

    static bool Regular(BuildingShape s) => s is BuildingShape.Hexagon or BuildingShape.Octagon or BuildingShape.Round;

    /// <summary>A two-storey building of the shape, with a few pieces of furniture and its roof.</summary>
    static BuildingSpec Spec(BuildingShape shape, bool box)
    {
        var s = box ? BuildingSpec.Preset(BuildingKind.Tavern) : BuildingSpec.FlatPreset(BuildingKind.Tavern);
        s = s with
        {
            Shape = shape, Roof = RoofShape.Gable,
            Items = [new RoomItem { Item = Defaults.For(ItemKind.Table) }, new RoomItem { Item = Defaults.For(ItemKind.Chair) with { Count = 2 } }, new RoomItem { Item = Defaults.For(ItemKind.Bed), Storey = 1 }, new RoomItem { Item = Defaults.For(ItemKind.Chest), Storey = 1 }],
        };
        if (Regular(shape)) return s with { WidthMm = shape == BuildingShape.Hexagon ? (box ? 130 : 110) : shape == BuildingShape.Round ? (box ? 170 : 150) : box ? 150 : 130, ColumnMm = box ? 20 : 16, Sides = 10, WayUp = WayUpStyle.Spiral, WindowsFront = 1, WindowsSide = 1 };
        return s with { WidthMm = shape == BuildingShape.UShape ? 178 : 170, DepthMm = 150, WingMm = shape == BuildingShape.UShape ? 56 : box ? 76 : 70 };
    }

    static List<Solid> Structure(BuildingKit kit) => [.. kit.Solids.Where(s => !s.IsFurniture)];

    [Theory]
    [MemberData(nameof(ShapesAndWalls))]
    public void Every_shape_comes_out_whole_and_fits_the_bed(BuildingShape shape, bool box)
    {
        var spec = Spec(shape, box);
        var kit = BuildingKitMaker.Make(spec);
        Assert.True(kit.Ok, string.Join("\n", kit.Findings.Select(f => f.Text)));
        Assert.DoesNotContain(kit.Findings, f => f.Level == Severity.Warn);
        Assert.Empty(Placement.TooBig(kit, MachineProfile.F2Ultra));
        foreach (var p in kit.Pieces) Assert.True(Geo.Pieces(p.Cut) == 1, $"{p.Name} comes out in {Geo.Pieces(p.Cut)} pieces");
        var fp = Footprint.Of(spec);
        int walls = kit.Solids.Count(s => s.Storey == 0 && s.Name.Contains("wall", StringComparison.OrdinalIgnoreCase) && !s.Name.Contains(':') && s.Side is not (WallSide.None or WallSide.Floor or WallSide.Ground or WallSide.Roof)
            && (box ? kit.Pieces.Any(p => p.Name == s.Name && p.WallSpecJson is not null) : s.Name != "Front door"));
        Assert.Equal(fp.Walls.Count, box ? kit.Pieces.Where(p => p.WallSpecJson is not null && !p.Name.Contains("storey 2") && !p.Name.Contains("battlements")).Sum(p => p.Copies) : walls);
        // A roof on top: pointed on a many-sided building, gables on an L, T or U.
        Assert.Contains(kit.Pieces, p => p.Name == "Roof base");
        if (Regular(shape)) Assert.Contains(kit.Pieces, p => p.Name == "Roof slope" && p.Copies == fp.Walls.Count);
        else Assert.Contains(kit.Pieces, p => p.Name.StartsWith("Roof", StringComparison.Ordinal) && p.Name.Contains("wing slope", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(ShapesAndWalls))]
    public void Nothing_in_a_shaped_building_runs_into_anything_else(BuildingShape shape, bool box)
    {
        var kit = BuildingKitMaker.Make(Spec(shape, box));
        Assert.True(kit.Ok, string.Join("\n", kit.Findings.Select(f => f.Text)));
        var structure = Structure(kit);
        Assert.Empty(BuildingsTests.Clashes(structure, structure, 0.4));
        Assert.Empty(BuildingsTests.Clashes([.. kit.Solids.Where(s => s.IsFurniture)], structure, 0.5));
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public void Shaped_floors_and_roofs_sit_where_they_should(BuildingShape shape)
    {
        var spec = Spec(shape, true);
        var kit = BuildingKitMaker.Make(spec);
        var fp = Footprint.Of(spec);
        double H = BuildingKitMaker.StoreyHeight(spec), T = spec.WallSheetMm;
        // The floor lies on the walls and columns of the storey below, and covers the outside of the columns.
        var floor = kit.Solids.Single(s => s.Side == WallSide.Floor);
        var (lo, hi) = BuildingsTests.Box(floor);
        Assert.Equal(BuildingKitMaker.StoreyBase(spec, 0) + H, lo.Y, 6);
        Assert.Equal(BuildingKitMaker.StoreyBase(spec, 1), hi.Y, 6);
        Assert.InRange(Geo.Area(floor.Outline), Geo.Area(fp.Outline(spec.ColumnMm / 2)) * 0.6, Geo.Area(fp.Outline(spec.ColumnMm / 2)) + 1);
        // The way up stands inside the room, under the opening.
        var up = kit.Solids.Where(s => s.Side == WallSide.None && !s.IsFurniture && s.Name is not ("Corner column" or "Column cap")).ToList();
        Assert.NotEmpty(up);
        var room = BuildingKitMaker.RoomInside(spec, fp, 0);
        foreach (var u in up)
        {
            var (a, b) = BuildingsTests.Box(u);
            foreach (var (x, z) in new[] { (a.X, a.Z), (b.X, a.Z), (a.X, b.Z), (b.X, b.Z) })
                Assert.True(Clipper.PointInPolygon(new PointD(x, -z), room[0]) != PointInPolygonResult.IsOutside || room.Count > 1, $"{u.Name} is outside the room at {x:0.#}, {z:0.#}");
        }
        Assert.True(floor.Outline.Count(p => !Clipper.IsPositive(p)) >= 1, "the floor has no opening");
        // The roof's base rests on the top storey's column caps; its locator reaches down inside the walls.
        double wallsTop = BuildingKitMaker.StoreyBase(spec, 1) + H;
        var roofBase = BuildingsTests.Box(kit.Solids.First(s => s.Name == "Roof base"));
        Assert.Equal(wallsTop + T, roofBase.Min.Y, 3);
        var locator = kit.Solids.Where(s => s.Name.StartsWith("Roof locator", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(locator);
        Assert.True(locator.Min(s => BuildingsTests.Box(s).Min.Y) < wallsTop, "the locator doesn't reach inside the walls");
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public void Battlements_go_round_any_shape(BuildingShape shape)
    {
        var spec = Spec(shape, true) with { Top = BuildingTop.Battlements, Storeys = 1, Items = [] };
        var kit = BuildingKitMaker.Make(spec);
        Assert.True(kit.Ok, string.Join("\n", kit.Findings.Select(f => f.Text)));
        Assert.DoesNotContain(kit.Solids, s => s.Side == WallSide.Roof);
        Assert.Contains(kit.Solids, s => s.Name == "Top floor");
        Assert.Contains(kit.Pieces, p => p.Name.Contains("battlements", StringComparison.Ordinal));
        var structure = Structure(kit);
        Assert.Empty(BuildingsTests.Clashes(structure, structure, 0.5));
    }

    public static IEnumerable<object[]> ColumnShapes() =>
        new[] { ColumnShape.Square, ColumnShape.Chamfered, ColumnShape.Octagon, ColumnShape.Hexagon, ColumnShape.Round, ColumnShape.Fluted }
            .SelectMany(c => new[] { new object[] { c, BuildingShape.Rectangle }, new object[] { c, BuildingShape.Octagon }, new object[] { c, BuildingShape.Hexagon } });

    [Theory]
    [MemberData(nameof(ColumnShapes))]
    public void Every_column_shape_takes_its_walls(ColumnShape column, BuildingShape shape)
    {
        var spec = BuildingSpec.Preset(BuildingKind.Cottage) with
        {
            Shape = shape, Column = column, ColumnMm = 24, Items = [], Roof = RoofShape.None, FloorTiles = false, Storeys = 1,
            WidthMm = shape == BuildingShape.Rectangle ? 118 : 150,
        };
        var kit = BuildingKitMaker.Make(spec);
        Assert.True(kit.Ok, string.Join("\n", kit.Findings.Select(f => f.Text)));
        Assert.DoesNotContain(kit.Findings, f => f.Level == Severity.Warn);
        var fp = Footprint.Of(spec);
        foreach (var c in fp.Corners)
        {
            var at = BuildingKitMaker.ColumnShapeAt(spec, c);
            Assert.True(Connectors.Suits(at, c.Slots), $"{at} at {c.SlotDeg}°");
            if (Connectors.Suits(column, c.Slots)) Assert.Equal(column, at);
        }
        if (fp.Corners.Any(c => BuildingKitMaker.ColumnShapeAt(spec, c) != column))
            Assert.Contains(kit.Findings, f => f.Text.Contains("don't suit", StringComparison.Ordinal));
        var structure = Structure(kit);
        Assert.Empty(BuildingsTests.Clashes(structure, structure, 0.4));
        // Square columns of box walls are boxes; the rest are stacks of layers.
        bool boxes = fp.Corners.Any(c => BuildingKitMaker.ColumnShapeAt(spec, c) == ColumnShape.Square);
        Assert.Equal(boxes, kit.Pieces.Any(p => p.Name.Contains("column side", StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(!boxes || fp.Corners.Any(c => BuildingKitMaker.ColumnShapeAt(spec, c) != ColumnShape.Square), kit.Pieces.Any(p => p.Name.Contains("column layer", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void Flat_walls_take_columns_of_any_shape()
    {
        foreach (var column in new[] { ColumnShape.Octagon, ColumnShape.Round, ColumnShape.Fluted, ColumnShape.Chamfered })
        {
            var spec = BuildingSpec.FlatPreset(BuildingKind.Cottage) with { Column = column, Items = [], Roof = RoofShape.None, FloorTiles = false };
            var kit = BuildingKitMaker.Make(spec);
            Assert.True(kit.Ok, $"{column}: " + string.Join("\n", kit.Findings.Select(f => f.Text)));
            var walls = kit.Solids.Where(s => s.Side is WallSide.Front or WallSide.Back or WallSide.Left or WallSide.Right && s.Name != "Front door").ToList();
            var columns = kit.Solids.Where(s => s.Name is "Corner column" or "Column cap").ToList();
            Assert.Empty(BuildingsTests.Clashes(walls, columns));
        }
    }

    [Fact]
    public void Footprints_have_the_corners_they_should()
    {
        var rect = Footprint.Of(BuildingShape.Rectangle, 100, 80, 0);
        Assert.Equal(4, rect.Walls.Count);
        Assert.All(rect.Corners, c => { Assert.Equal(90, c.InsideDeg, 6); Assert.Equal(ConnectorKind.Corner, c.Kind); });
        // The first wall runs left to right across the front; the corners turn the way the rectangle's columns always have.
        Assert.Equal(WallSide.Front, rect.Walls[0].Facing);
        Assert.Equal(new[] { 0.0, 90, 180, 270 }, rect.Corners.Select(c => Math.Round(c.YawDeg, 6)));
        Assert.Equal(new[] { WallSide.Front, WallSide.Right, WallSide.Back, WallSide.Left }, rect.Walls.Select(w => w.Facing));

        var l = Footprint.Of(BuildingShape.LShape, 160, 140, 70);
        Assert.Equal(6, l.Walls.Count);
        Assert.Single(l.Corners, c => Math.Abs(c.InsideDeg - 270) < 1e-6);
        Assert.All(l.Corners, c => Assert.Equal(90, c.SlotDeg, 6));
        Assert.Equal(WallSide.Front, l.Walls[l.DoorWall].Facing);
        Assert.Equal(2, l.Wings.Count);
        Assert.Equal(8, Footprint.Of(BuildingShape.TShape, 160, 140, 60).Walls.Count);
        Assert.Equal(3, Footprint.Of(BuildingShape.UShape, 220, 140, 60).Wings.Count);

        var oct = Footprint.Of(BuildingShape.Octagon, 140, 0, 0);
        Assert.All(oct.Corners, c => { Assert.Equal(135, c.InsideDeg, 6); Assert.Equal(ConnectorKind.Angle135, c.Kind); });
        Assert.Equal(140, oct.Bounds.right - oct.Bounds.left, 6);
        Assert.Equal(140, oct.Bounds.bottom - oct.Bounds.top, 6);
        Assert.Equal(WallSide.Front, oct.Walls[0].Facing);
        Assert.Equal(0, oct.Walls[0].Middle.x, 6);
        var hex = Footprint.Of(BuildingShape.Hexagon, 120, 0, 0);
        Assert.All(hex.Corners, c => Assert.Equal(120, c.SlotDeg, 6));
        Assert.Equal(ConnectorKind.Custom, hex.Corners[0].Kind);
        Assert.Equal(12, Footprint.Of(BuildingShape.Round, 160, 0, 0, 11).Walls.Count);

        // Drawn round the other way, a footprint still runs anticlockwise, the door in the same wall.
        var drawn = Footprint.FromPoints([new(0, 100), new(120, 100), new(120, 0), new(0, 0)], doorWall: 2);
        Assert.True(Clipper.Area(new PathD(drawn.Points)) > 0);
        Assert.True(drawn.IsRectangle);
        Assert.Equal(WallSide.Front, drawn.Walls[drawn.DoorWall].Facing);
        Assert.Equal(-60, drawn.Bounds.left, 6);
        var room = Footprint.LargestRectangle(l.Outline(-10))!.Value;
        Assert.True(room.right - room.left >= 130 && room.bottom - room.top >= 45, $"{room.left},{room.top} {room.right},{room.bottom}");
    }

    [Fact]
    public void A_drawn_footprint_makes_a_building()
    {
        // A pentagon-ish house with a pointed front.
        var fp = Footprint.FromPoints([new(-60, -40), new(60, -40), new(60, 40), new(0, 80), new(-60, 40)]);
        var spec = BuildingSpec.Preset(BuildingKind.Cottage) with { Items = [new RoomItem { Item = Defaults.For(ItemKind.Table) }] };
        var kit = BuildingKitMaker.Make(spec, fp);
        Assert.True(kit.Ok, string.Join("\n", kit.Findings.Select(f => f.Text)));
        Assert.DoesNotContain(kit.Solids, s => s.Side == WallSide.Roof);
        Assert.Equal(5, kit.Pieces.Where(p => p.WallSpecJson is not null).Sum(p => p.Copies));
        var structure = Structure(kit);
        Assert.Empty(BuildingsTests.Clashes(structure, structure, 0.5));
    }

    [Fact]
    public void Each_shape_gets_the_roofs_that_suit_it()
    {
        var s = BuildingSpec.Preset(BuildingKind.Cottage);
        Assert.Equal(RoofShape.Hip, (s with { Roof = RoofShape.Pointed }).RoofFor());
        Assert.Equal(RoofShape.LeanTo, (s with { Roof = RoofShape.LeanTo }).RoofFor());
        Assert.Equal(RoofShape.Pointed, (s with { Shape = BuildingShape.Octagon, Roof = RoofShape.Gable }).RoofFor());
        Assert.Equal(RoofShape.Gable, (s with { Shape = BuildingShape.LShape, Roof = RoofShape.Hip }).RoofFor());
        Assert.Equal(RoofShape.None, (s with { Shape = BuildingShape.Round, Roof = RoofShape.None }).RoofFor());
        Assert.Equal(RoofShape.None, (s with { Shape = BuildingShape.Custom, Roof = RoofShape.Gable }).RoofFor());
    }

    [Fact]
    public void Sizes_that_dont_fit_the_shape_are_said_plainly()
    {
        var s = BuildingSpec.Preset(BuildingKind.Manor);
        var wide = BuildingKitMaker.Make(s with { WingMm = 150 });
        Assert.False(wide.Ok);
        Assert.Contains(wide.Findings, f => f.Level == Severity.Bad && f.Text.Contains("wings", StringComparison.Ordinal));
        var small = BuildingKitMaker.Make(BuildingSpec.Preset(BuildingKind.OctagonTower) with { WidthMm = 50 });
        Assert.False(small.Ok);
        Assert.Contains(small.Findings, f => f.Level == Severity.Bad && f.Text.Contains("between its columns", StringComparison.Ordinal));
    }

    [Fact]
    public void Shapes_survive_saving_and_old_buildings_stay_rectangles()
    {
        var spec = BuildingSpec.Preset(BuildingKind.Manor) with { Column = ColumnShape.Fluted, Sides = 14 };
        var back = BuildingSpec.FromJson(spec.ToJson())!;
        Assert.Equal(BuildingShape.LShape, back.Shape);
        Assert.Equal(ColumnShape.Fluted, back.Column);
        Assert.Equal(spec.WingMm, back.WingMm);
        var old = BuildingSpec.FromJson("""{"Kind":"Cottage","WidthMm":110}""")!;
        Assert.Equal(BuildingShape.Rectangle, old.Shape);
        Assert.Equal(ColumnShape.Auto, old.Column);
        Assert.Equal(spec.WingMm * 2, spec.Scaled(2).WingMm);
    }

    [Fact]
    public void Fluted_columns_keep_their_grooves_clear_of_the_walls()
    {
        double[] corner = [0, 90];
        var o = Connectors.Outline(ColumnShape.Fluted, 24, corner, 12);
        // Full round where each wall comes in, and grooves round the outside.
        Assert.Equal(12, Connectors.Reach(o, 0), 1);
        Assert.Equal(12, Connectors.Reach(o, 90), 1);
        Assert.True(Connectors.Reach(o, 202.5) < 11.5);
        Assert.True(Geo.Area([o]) < Math.PI * 144 - 5);
        Assert.Equal(ColumnShape.Octagon, Connectors.Matching(ColumnShape.Square, [0, 135]));
        Assert.Equal(ColumnShape.Hexagon, Connectors.Matching(ColumnShape.Auto, [0, 120]));
        Assert.Equal(ColumnShape.Round, Connectors.Matching(ColumnShape.Octagon, [0, 150]));
        Assert.Equal(ColumnShape.Fluted, Connectors.Matching(ColumnShape.Fluted, [0, 150]));
        Assert.Equal(ColumnShape.Square, Connectors.Matching(ColumnShape.Hexagon, [0, 90]));
        // The wall maker's own choice for a corner is as it was.
        Assert.Equal(ColumnShape.Square, Connectors.ShapeFor(new WallSpec(), [0, 90]));
        Assert.Equal(ColumnShape.Round, Connectors.ShapeFor(new WallSpec(), [0, 120]));
    }
}
