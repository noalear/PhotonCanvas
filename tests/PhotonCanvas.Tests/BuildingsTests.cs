using Clipper2Lib;
using PhotonCanvas.Addon.Buildings;
using PhotonCanvas.Addon.Furniture;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using Geo = PhotonCanvas.Addon.Walls.Geo;
using Severity = PhotonCanvas.Addon.Buildings.Severity;
using Placement = PhotonCanvas.Addon.Buildings.Placement;
using PlaceOptions = PhotonCanvas.Addon.Buildings.PlaceOptions;

namespace PhotonCanvas.Tests;

/// <summary>Buildings with flat walls (as they were first made) and, at the end, with box walls (as they come now).</summary>
public class BuildingsTests
{
    public static IEnumerable<object[]> Kinds() => BuildingSpec.Kinds.Select(k => new object[] { k.Kind });

    static BuildingKit Make(BuildingKind k, double scale = 28)
    {
        var s = BuildingSpec.FlatPreset(k);
        // Furniture for small figures needs a thinner sheet.
        if (Math.Abs(scale - 28) > 1e-9) s = s.Scaled(scale / 28) with { FurnitureSheetMm = scale < 20 ? 1 : 2 };
        return BuildingKitMaker.Make(s);
    }

    static bool In(PathsD area, double u, double v) => OutlineGrid.In(area, u, v);

    sealed record Placed(Solid S, PathsD Shrunk)
    {
        public bool Inside(V3 p, double eps = 0.03)
        {
            var (u, v, w) = S.Pose.Local(p);
            return Math.Abs(w) <= S.Thickness / 2 - eps && In(Shrunk, u, v);
        }
    }

    internal static (V3 Min, V3 Max) Box(Solid s)
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
    internal static List<string> Clashes(IReadOnlyList<Solid> a, IReadOnlyList<Solid> b, double step = 0.25)
    {
        var found = new List<string>();
        var pa = a.Select(s => new Placed(s, Geo.Offset(s.Outline, -0.03))).ToList();
        var pb = b.Select(s => new Placed(s, Geo.Offset(s.Outline, -0.03))).ToList();
        foreach (var x in pa)
            foreach (var y in pb)
            {
                if (ReferenceEquals(x.S, y.S)) continue;
                var (a0, a1) = Box(x.S);
                var (b0, b1) = Box(y.S);
                double x0 = Math.Max(a0.X, b0.X), y0 = Math.Max(a0.Y, b0.Y), z0 = Math.Max(a0.Z, b0.Z);
                double x1 = Math.Min(a1.X, b1.X), y1 = Math.Min(a1.Y, b1.Y), z1 = Math.Min(a1.Z, b1.Z);
                if (x1 - x0 < 0.06 || y1 - y0 < 0.06 || z1 - z0 < 0.06) continue;
                for (double px = x0 + step / 2; px < x1; px += step)
                    for (double py = y0 + step / 2; py < y1; py += step)
                        for (double pz = z0 + step / 2; pz < z1; pz += step)
                        {
                            var p = new V3(px, py, pz);
                            if (x.Inside(p) && y.Inside(p)) { found.Add($"{x.S.Name} and {y.S.Name} at {px:0.##}, {py:0.##}, {pz:0.##}"); goto next; }
                        }
                next:;
            }
        return found;
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void Every_building_comes_out_whole_and_fits_the_bed(BuildingKind k)
    {
        var kit = Make(k);
        Assert.True(kit.Ok, string.Join("\n", kit.Findings.Select(f => f.Text)));
        Assert.DoesNotContain(kit.Findings, f => f.Level == Severity.Warn);
        Assert.Empty(Placement.TooBig(kit, MachineProfile.F2Ultra));
        Assert.Contains(kit.Pieces, p => p.Sheet == Sheet.Furniture);
        // Every piece comes out in one piece.
        foreach (var p in kit.Pieces) Assert.True(Geo.Pieces(p.Cut) == 1, $"{p.Name} comes out in {Geo.Pieces(p.Cut)} pieces");
    }

    [Theory]
    [InlineData(BuildingKind.Cottage, 15)]
    [InlineData(BuildingKind.Tavern, 32)]
    [InlineData(BuildingKind.Watchtower, 54)]
    [InlineData(BuildingKind.Cottage, 54)]
    public void Other_figure_sizes_make_whole_buildings(BuildingKind k, double scale)
    {
        var kit = Make(k, scale);
        Assert.True(kit.Ok, string.Join("\n", kit.Findings.Select(f => f.Text)));
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void Walls_go_into_the_columns_without_clashing(BuildingKind k)
    {
        var kit = Make(k);
        var walls = kit.Solids.Where(s => !s.IsFurniture && s.Side is not (WallSide.None or WallSide.Floor or WallSide.Ground or WallSide.Roof) && s.Name != "Front door").ToList();
        var columns = kit.Solids.Where(s => s.Name is "Corner column" or "Column cap").ToList();
        var floors = kit.Solids.Where(s => s.Side is WallSide.Floor or WallSide.Ground).ToList();
        Assert.Equal(Footprint.Of(BuildingSpec.FlatPreset(k)).Walls.Count, walls.Count(w => w.Storey == 0));
        Assert.Empty(Clashes(walls, columns));
        Assert.Empty(Clashes(walls, floors));
        Assert.Empty(Clashes(columns, floors));
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
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void Storeys_stack_flush(BuildingKind k)
    {
        foreach (var spec in new[] { BuildingSpec.Preset(k), BuildingSpec.FlatPreset(k) })
            StackFlush(spec);
    }

    static void StackFlush(BuildingSpec spec)
    {
        var kit = BuildingKitMaker.Make(spec);
        double T = spec.WallSheetMm, H = BuildingKitMaker.StoreyHeight(spec);
        Assert.Equal(0, H / T % 1, 6);
        foreach (var f in kit.Solids.Where(s => s.Side == WallSide.Floor))
        {
            var (lo, hi) = Box(f);
            // Lies on the storey below's walls and columns, and under the next storey's.
            Assert.Equal(BuildingKitMaker.StoreyBase(spec, f.Storey - 1) + H, lo.Y, 6);
            Assert.Equal(BuildingKitMaker.StoreyBase(spec, f.Storey), hi.Y, 6);
        }
        foreach (var c in kit.Solids.Where(s => s.Name == "Corner column" && s.Storey < spec.Storeys))
        {
            // A stacked column is one solid as tall as the walls; a boxed one's sides reach from its storey's floor to the top of its walls.
            var (lo, hi) = Box(c);
            if (spec.BoxWalls) Assert.True(lo.Y >= BuildingKitMaker.StoreyBase(spec, c.Storey) - 1e-6 && hi.Y <= BuildingKitMaker.StoreyBase(spec, c.Storey) + H + 1e-6, $"{c.Name} {lo.Y}..{hi.Y}");
            else Assert.Equal(H, c.Thickness, 6);
        }
        if (spec.BoxWalls)
            Assert.Contains(kit.Solids, c => c.Name == "Corner column" && c.Storey == 0 && Math.Abs(Box(c).Max.Y - BuildingKitMaker.StoreyBase(spec, 0) - H) < 1e-6);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void Furniture_stands_in_its_room(BuildingKind k)
    {
        var spec = BuildingSpec.FlatPreset(k);
        var kit = BuildingKitMaker.Make(spec);
        double W = spec.WidthMm, D = spec.DepthMm, Wt = BuildingKitMaker.WallThickness(spec), H = BuildingKitMaker.StoreyHeight(spec);
        var furniture = kit.Solids.Where(s => s.IsFurniture && s.Name != "Front door").ToList();
        Assert.NotEmpty(furniture);
        foreach (var f in furniture)
        {
            var (lo, hi) = Box(f);
            double floor = BuildingKitMaker.StoreyBase(spec, f.Storey);
            Assert.True(lo.X >= -W / 2 + Wt / 2 - 1e-6 && hi.X <= W / 2 - Wt / 2 + 1e-6 && lo.Z >= -D / 2 + Wt / 2 - 1e-6 && hi.Z <= D / 2 - Wt / 2 + 1e-6, $"{f.Name} is outside the walls");
            Assert.True(lo.Y >= floor - 1e-6 && hi.Y <= floor + H + 1e-6, $"{f.Name} isn't on its floor");
        }
        var structure = kit.Solids.Where(s => !s.IsFurniture).ToList();
        Assert.Empty(Clashes(furniture, structure, 0.4));
    }

    [Theory]
    [InlineData(BuildingKind.Cottage, RoofShape.Gable)]
    [InlineData(BuildingKind.Tavern, RoofShape.Gable)]
    [InlineData(BuildingKind.Tavern, RoofShape.Hip)]
    [InlineData(BuildingKind.Cottage, RoofShape.LeanTo)]
    public void The_roof_sits_on_the_top_storey_without_clashing(BuildingKind k, RoofShape shape)
    {
        var spec = BuildingSpec.FlatPreset(k) with { Roof = shape };
        var kit = BuildingKitMaker.Make(spec);
        Assert.DoesNotContain(kit.Findings, f => f.Level != Severity.Note);
        var roof = kit.Solids.Where(s => s.Side == WallSide.Roof).ToList();
        Assert.NotEmpty(roof);
        Assert.Contains(kit.Pieces, p => p.Name == "Roof base" && p.Sheet == Sheet.Walls);
        // As wide as the column caps' outside: half a column and the caps' lip past the columns' centres.
        double W = spec.WidthMm, D = spec.DepthMm, half = spec.ColumnMm / 2 + PhotonCanvas.Addon.Walls.WallKitMaker.LipMm;
        int top = spec.Storeys - 1;
        double wallsTop = BuildingKitMaker.StoreyBase(spec, top) + BuildingKitMaker.StoreyHeight(spec);
        var base_ = Box(roof.First(s => s.Name == "Roof base"));
        // The base lies on the column caps.
        Assert.Equal(wallsTop + spec.WallSheetMm, base_.Min.Y, 3);
        Assert.Equal(-W / 2 - half, base_.Min.X, 1);
        Assert.Equal(W / 2 + half, base_.Max.X, 1);
        Assert.Equal(-D / 2 - half, base_.Min.Z, 1);
        Assert.Equal(D / 2 + half, base_.Max.Z, 1);
        // The locator reaches down inside the walls.
        var locator = roof.Where(s => s.Name.StartsWith("Roof locator", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(locator);
        Assert.True(locator.Min(s => Box(s).Min.Y) < wallsTop, "the locator doesn't reach inside the walls");
        var structure = kit.Solids.Where(s => !s.IsFurniture && s.Side != WallSide.Roof).ToList();
        Assert.Empty(Clashes(roof, structure, 0.4));
        Assert.Contains(kit.Findings, f => f.Text.Contains("with its roof", StringComparison.Ordinal));

        var bare = BuildingKitMaker.Make(spec with { Roof = RoofShape.None });
        Assert.DoesNotContain(bare.Solids, s => s.Side == WallSide.Roof);
        Assert.DoesNotContain(bare.Pieces, p => p.Name.StartsWith("Roof", StringComparison.Ordinal));
    }

    [Fact]
    public void A_tower_with_battlements_has_no_roof()
    {
        var spec = BuildingSpec.FlatPreset(BuildingKind.Watchtower) with { Roof = RoofShape.Gable };
        var kit = BuildingKitMaker.Make(spec);
        Assert.DoesNotContain(kit.Solids, s => s.Side == WallSide.Roof);
    }

    [Theory]
    [InlineData(WayUpStyle.Stairs)]
    [InlineData(WayUpStyle.Spiral)]
    [InlineData(WayUpStyle.Ladder)]
    [InlineData(WayUpStyle.None)]
    public void Each_way_up_stands_inside_without_clashing(WayUpStyle way)
    {
        var spec = BuildingSpec.FlatPreset(BuildingKind.Tavern) with { WayUp = way };
        var kit = BuildingKitMaker.Make(spec);
        Assert.DoesNotContain(kit.Findings, f => f.Level != Severity.Note);
        var up = kit.Solids.Where(s => !s.IsFurniture && s.Side == WallSide.None && s.Name is not ("Corner column" or "Column cap")).ToList();
        var plate = kit.Solids.First(s => s.Side == WallSide.Floor);
        if (way == WayUpStyle.None)
        {
            Assert.Empty(up);
            // A plain floor has no opening: its only holes are the four pin holes.
            Assert.Equal(4, plate.Outline.Count(p => !Clipper.IsPositive(p)));
            return;
        }
        Assert.NotEmpty(up);
        Assert.Equal(5, plate.Outline.Count(p => !Clipper.IsPositive(p)));
        double W = spec.WidthMm, D = spec.DepthMm, Wt = BuildingKitMaker.WallThickness(spec);
        foreach (var u in up)
        {
            var (lo, hi) = Box(u);
            Assert.True(lo.X >= -W / 2 + Wt / 2 - 0.05 && hi.X <= W / 2 - Wt / 2 + 0.05 && lo.Z >= -D / 2 + Wt / 2 - 0.05 && hi.Z <= D / 2 - Wt / 2 + 0.05, $"{u.Name} is outside the walls");
            Assert.True(lo.Y >= BuildingKitMaker.StoreyBase(spec, 0) - 1e-6, $"{u.Name} is under the floor");
            // At the back, away from the door.
            Assert.True(hi.Z < D / 6, $"{u.Name} isn't at the back");
        }
        var structure = kit.Solids.Where(s => !s.IsFurniture && !up.Contains(s)).ToList();
        Assert.Empty(Clashes(up, structure, 0.4));
        Assert.Contains(kit.Findings, f => f.Text.Contains("back right", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void The_building_stands_on_floor_tiles_that_cover_it(BuildingKind k)
    {
        var spec = BuildingSpec.FlatPreset(k);
        var kit = BuildingKitMaker.Make(spec);
        var tiles = kit.Solids.Where(s => s.Side == WallSide.Ground).ToList();
        Assert.NotEmpty(tiles);
        Assert.Contains(kit.Pieces, p => p.Name.StartsWith("Floor tile", StringComparison.Ordinal) && p.Sheet == Sheet.Walls);
        // The tiles' top is where the walls stand, and together they cover the building's outside.
        Assert.All(tiles, t => Assert.Equal(BuildingKitMaker.StoreyBase(spec, 0), Box(t).Max.Y, 6));
        // Centred under it, reaching a whole square past a thin strip at the edge.
        var board = BuildingKitMaker.TileSpecFor(spec);
        double across = board.BoardAcross * board.SquareMm, down = board.BoardDown * board.SquareMm;
        Assert.InRange(across, spec.WidthMm + spec.ColumnMm - 0.01, spec.WidthMm + spec.ColumnMm + board.SquareMm);
        Assert.InRange(down, spec.DepthMm + spec.ColumnMm - 0.01, spec.DepthMm + spec.ColumnMm + board.SquareMm);
        Assert.Equal(-across / 2, tiles.Min(t => Box(t).Min.X), 1);
        Assert.Equal(down / 2, tiles.Max(t => Box(t).Max.Z), 1);
        double area = tiles.Sum(t => Geo.Area(t.Outline));
        Assert.InRange(area, across * down * 0.98, across * down * 1.02);

        var bare = BuildingKitMaker.Make(spec with { FloorTiles = false });
        Assert.DoesNotContain(bare.Pieces, p => p.Name.StartsWith("Floor tile", StringComparison.Ordinal));
        Assert.Equal(0, BuildingKitMaker.StoreyBase(spec with { FloorTiles = false }, 0));
    }

    [Fact]
    public void A_swinging_door_fits_the_doorway()
    {
        var spec = BuildingSpec.FlatPreset(BuildingKind.Cottage);
        var kit = BuildingKitMaker.Make(spec);
        Assert.Contains(kit.Pieces, p => p.Name == "Front door" && p.Sheet == Sheet.Furniture);
        Assert.Contains(kit.Pieces, p => p.Name == "Front door clip" && p.Copies == 2);
        var door = kit.Furniture!.Items[^1].Item;
        Assert.Equal(ItemKind.WallDoor, door.Kind);
        Assert.Equal(spec.DoorWidthMm, door.WidthMm);
        Assert.Equal(BuildingKitMaker.WallThickness(spec), door.WallMm);

        var loose = BuildingKitMaker.Make(spec with { Door = DoorStyle.Loose });
        Assert.Contains(loose.Pieces, p => p.Name == "Front door" && p.Sheet == Sheet.Walls);
        var open = BuildingKitMaker.Make(spec with { Door = DoorStyle.Open });
        Assert.DoesNotContain(open.Pieces, p => p.Name.StartsWith("Front door", StringComparison.Ordinal));
    }

    [Fact]
    public void Walls_that_come_out_the_same_are_cut_together()
    {
        var kit = Make(BuildingKind.Cottage);
        var walls = kit.Pieces.Where(p => p.WallSpecJson is not null).ToList();
        Assert.Equal(4, walls.Sum(w => w.Copies));
        Assert.Contains(walls, w => w.Name == "Side walls" && w.Copies == 2);
        Assert.Contains(walls, w => w.Name == "Front wall" && w.Copies == 1);
        var two = BuildingKitMaker.Make(BuildingSpec.FlatPreset(BuildingKind.Cottage) with { TwoSided = true });
        Assert.Equal(4, two.Pieces.Where(p => p.Name.EndsWith("back panel", StringComparison.Ordinal)).Sum(p => p.Copies));
        var tower = Make(BuildingKind.Watchtower);
        Assert.Contains(tower.Pieces, w => w.Name == "Walls, storey 2" && w.Copies == 4);
        Assert.Contains(tower.Pieces, w => w.Name == "Walls, battlements" && w.Copies == 4);
        Assert.Contains(tower.Pieces, w => w.Name == "Back and side walls, storey 1" && w.Copies == 3);
    }

    [Fact]
    public void Push_fit_furniture_gets_the_furniture_makers_grip_ribs()
    {
        var spec = BuildingSpec.FlatPreset(BuildingKind.Cottage);
        var glued = BuildingKitMaker.Make(spec);
        var pushed = BuildingKitMaker.Make(spec with { FurniturePushFit = true });
        Assert.True(pushed.Ok);
        Assert.True(pushed.Furniture!.Items.All(i => i.Item.Kind == ItemKind.WallDoor || i.Ok));
        static double Points(BuildingKit k) => k.Pieces.Where(p => p.Sheet == Sheet.Furniture).Sum(p => p.Cut.Sum(c => c.Count));
        Assert.True(Points(pushed) > Points(glued));
    }

    [Fact]
    public void Spec_survives_saving()
    {
        var spec = BuildingSpec.FlatPreset(BuildingKind.Tavern);
        var back = BuildingSpec.FromJson(spec.ToJson())!;
        Assert.Equal(spec.ToJson(), back.ToJson());
        Assert.Equal("Bar counter", back.Label(4));
        Assert.Equal("Table", back.Label(0));
        Assert.Null(BuildingSpec.FromJson("{ not json"));
    }

    [Fact]
    public void Scaling_resizes_everything_but_the_sheets()
    {
        var s = BuildingSpec.FlatPreset(BuildingKind.Cottage);
        var big = s.Scaled(2);
        Assert.Equal(56, big.ScaleMm);
        Assert.Equal(s.WidthMm * 2, big.WidthMm);
        Assert.Equal(s.DoorHeightMm * 2, big.DoorHeightMm);
        Assert.Equal(s.Items[0].Item.WidthMm * 2, big.Items[0].Item.WidthMm);
        Assert.Equal(s.WallSheetMm, big.WallSheetMm);
        Assert.Equal(s.FurnitureSheetMm, big.FurnitureSheetMm);
    }

    [Fact]
    public void Too_small_or_too_thin_is_said_plainly()
    {
        var s = BuildingSpec.FlatPreset(BuildingKind.Cottage);
        Assert.False(BuildingKitMaker.Make(s with { WidthMm = 20 }).Ok);
        Assert.False(BuildingKitMaker.Make(s with { ColumnMm = 4 }).Ok);
        var big = BuildingKitMaker.Make(s with { WidthMm = 300 });
        Assert.NotEmpty(Placement.TooBig(big, MachineProfile.F2Ultra));
    }

    [Fact]
    public void Goes_into_the_design_a_sheet_at_a_time()
    {
        var spec = BuildingSpec.FlatPreset(BuildingKind.Cottage);
        var kit = BuildingKitMaker.Make(spec);
        var (s, pages, id) = Placement.Place(DocState.New(), kit, spec, MachineProfile.F2Ultra, new PlaceOptions());
        Assert.True(pages >= 2);
        // Every piece is on some page, each page has the settings, and walls carry the wall maker's settings.
        int cuts = 0;
        var names = Pages.Names(s);
        Assert.Contains(names, n => n.Contains("walls (3 mm sheet)", StringComparison.Ordinal));
        Assert.Contains(names, n => n.Contains("furniture (2 mm sheet)", StringComparison.Ordinal));
        for (int i = 0; i <= s.OtherPages.Count; i++)
        {
            var page = Pages.SwitchTo(s, i);
            var cutLayer = page.Layers.First(l => l.Name == Placement.CutLayer).Id;
            var mine = page.Shapes.Where(x => x.LayerId == cutLayer).ToList();
            cuts += mine.Count;
            Assert.Contains(mine, x => x.AddonData.ContainsKey(Placement.SpecKey));
            Assert.Equal(spec.ToJson(), Placement.Find(page, [mine[^1].Id])!.Value.Spec.ToJson());
            Assert.True(page.Layers.FindIndex(l => l.Name == Placement.EtchLayer) < page.Layers.FindIndex(l => l.Name == Placement.CutLayer));
        }
        Assert.Equal(kit.PieceCount, cuts);
        Assert.True(s.Shapes.Concat(s.OtherPages.SelectMany(p => p.Shapes)).Count(x => x.AddonData.ContainsKey(Placement.WallSpecKey)) >= 2);

        // Replacing takes the old pieces off every page.
        var smaller = spec with { Items = [spec.Items[0]] };
        var (s2, _, id2) = Placement.Place(s, BuildingKitMaker.Make(smaller), smaller, MachineProfile.F2Ultra, new PlaceOptions { Replace = id });
        Assert.Equal(id, id2);
        Assert.Equal(BuildingKitMaker.Make(smaller).PieceCount, s2.Shapes.Concat(s2.OtherPages.SelectMany(p => p.Shapes)).Count(x => x.Name.Contains("·", StringComparison.Ordinal) is false && x.AddonData.ContainsKey(Placement.IdKey)));
    }

    [Fact]
    public void One_sheet_when_the_furniture_is_cut_from_the_walls_sheet()
    {
        var spec = BuildingSpec.FlatPreset(BuildingKind.Cottage) with { FurnitureSheetMm = 3 };
        var sheets = Placement.Sheets(BuildingKitMaker.Make(spec), spec, new PlaceOptions());
        Assert.Single(sheets);
    }

    // ---- box walls ----

    static BuildingKit MakeBox(BuildingKind k) => BuildingKitMaker.Make(BuildingSpec.Preset(k));

    [Theory]
    [MemberData(nameof(Kinds))]
    public void Every_box_walled_building_comes_out_whole_and_fits_the_bed(BuildingKind k)
    {
        var spec = BuildingSpec.Preset(k);
        Assert.True(spec.BoxWalls);
        var kit = BuildingKitMaker.Make(spec);
        Assert.True(kit.Ok, string.Join("\n", kit.Findings.Select(f => f.Text)));
        Assert.DoesNotContain(kit.Findings, f => f.Level == Severity.Warn);
        Assert.Empty(Placement.TooBig(kit, MachineProfile.F2Ultra));
        foreach (var p in kit.Pieces) Assert.True(Geo.Pieces(p.Cut) == 1, $"{p.Name} comes out in {Geo.Pieces(p.Cut)} pieces");
        // Each wall is a box: faces, a base, a top, ends; the corner columns are boxes too.
        foreach (var part in new[] { ": back", ": base", ": top", ": end" }) Assert.Contains(kit.Pieces, p => p.Name.EndsWith(part, StringComparison.Ordinal));
        // Square corners' columns are boxes; an octagon's are stacks of layers.
        if (Footprint.Of(spec).Corners.Any(c => Math.Abs(c.SlotDeg - 90) < 1e-6))
            Assert.Contains(kit.Pieces, p => p.Name.StartsWith("Corner column side", StringComparison.Ordinal));
        else Assert.Contains(kit.Pieces, p => p.Name.Contains("column layer", StringComparison.Ordinal));
        Assert.Equal(12, kit.WallMm);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void Nothing_in_a_box_walled_building_runs_into_anything_else(BuildingKind k)
    {
        var kit = MakeBox(k);
        Assert.True(kit.Ok, string.Join("\n", kit.Findings.Select(f => f.Text)));
        var structure = kit.Solids.Where(s => !s.IsFurniture).ToList();
        Assert.Empty(Clashes(structure, structure, 0.4));
        Assert.Empty(Clashes(kit.Solids.Where(s => s.IsFurniture).ToList(), structure, 0.5));
        // Every slot in a column's sides has a wall's tab in it.
        var placed = structure.Select(s => new Placed(s, Geo.Offset(s.Outline, -0.03))).ToList();
        foreach (var c in placed.Where(p => p.S.Name == "Corner column"))
            foreach (var hole in Geo.Tidy(c.S.Outline).Where(h => Clipper.Area(h) < 0))
            {
                var b = Clipper.GetBounds(hole);
                // A stacked column's pin hole is round and empty.
                if (Math.Abs(b.right - b.left - BuildingKitMaker.PinMm) < 0.1 && Math.Abs(b.bottom - b.top - BuildingKitMaker.PinMm) < 0.1) continue;
                var centre = c.S.Pose.At((b.left + b.right) / 2, (b.top + b.bottom) / 2);
                Assert.True(placed.Any(o => !ReferenceEquals(o, c) && o.Inside(centre, 0.01)), $"An empty slot in a corner column at {centre.X:0.#}, {centre.Y:0.#}, {centre.Z:0.#}");
            }
    }

    [Fact]
    public void A_box_walled_building_hangs_its_own_door()
    {
        var kit = MakeBox(BuildingKind.Cottage);
        Assert.Contains(kit.Pieces, p => p.Name == "Front door" && p.Sheet == Sheet.Walls);
        Assert.Contains(kit.Pieces, p => p.Name == "Front wall: door head");
        Assert.DoesNotContain(kit.Pieces, p => p.Name.StartsWith("Front door clip", StringComparison.Ordinal));
        Assert.Contains(kit.Solids, s => s.Name == "Front door" && s.Side == WallSide.Front && !s.IsFurniture);
        // Loose: glued into its doorway; open: no door at all.
        var loose = BuildingKitMaker.Make(BuildingSpec.Preset(BuildingKind.Cottage) with { Door = DoorStyle.Loose });
        Assert.Contains(loose.Pieces, p => p.Name == "Front door");
        Assert.DoesNotContain(loose.Pieces, p => p.Name == "Front wall: door head");
        var open = BuildingKitMaker.Make(BuildingSpec.Preset(BuildingKind.Cottage) with { Door = DoorStyle.Open });
        // (Furniture has doors of its own: a cupboard's are cut to glue on.)
        Assert.DoesNotContain(open.Pieces, p => p.Sheet != Sheet.Furniture && p.Name.Contains("door", StringComparison.OrdinalIgnoreCase) && !p.Name.Contains("Floor", StringComparison.Ordinal));
    }

    [Fact]
    public void Box_walled_buildings_say_when_their_columns_are_too_thin()
    {
        var kit = BuildingKitMaker.Make(BuildingSpec.Preset(BuildingKind.Cottage) with { ColumnMm = 16 });
        Assert.False(kit.Ok);
        Assert.Contains(kit.Findings, f => f.Level == Severity.Bad && f.Text.Contains("box walls"));
        var scaled = BuildingSpec.Preset(BuildingKind.Cottage).Scaled(54.0 / 28);
        Assert.Equal(0, scaled.WallMm % scaled.WallSheetMm, 6);
        Assert.True(scaled.ColumnMm >= scaled.NarrowestBoxColumn);
        Assert.True(BuildingKitMaker.Make(scaled with { FurnitureSheetMm = 2 }).Ok);
    }

    [Fact]
    public void Old_buildings_keep_their_flat_walls()
    {
        var old = BuildingSpec.FromJson("""{"Kind":"Cottage","WidthMm":110}""")!;
        Assert.False(old.BoxWalls);
        var back = BuildingSpec.FromJson((BuildingSpec.Preset(BuildingKind.Tavern) with { WindowFill = Addon.Walls.WindowFill.Shutters }).ToJson())!;
        Assert.True(back.BoxWalls);
        Assert.Equal(Addon.Walls.WindowFill.Shutters, back.WindowFill);
    }
}
