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

public class BuildingsTests
{
    public static IEnumerable<object[]> Kinds() => BuildingSpec.Kinds.Select(k => new object[] { k.Kind });

    static BuildingKit Make(BuildingKind k, double scale = 28)
    {
        var s = BuildingSpec.Preset(k);
        // Furniture for small figures needs a thinner sheet.
        if (Math.Abs(scale - 28) > 1e-9) s = s.Scaled(scale / 28) with { FurnitureSheetMm = scale < 20 ? 1 : 2 };
        return BuildingKitMaker.Make(s);
    }

    static bool In(PathsD area, double u, double v)
    {
        int inside = 0;
        foreach (var p in area)
            if (Clipper.PointInPolygon(new PointD(u, v), p) == PointInPolygonResult.IsInside) inside++;
        return inside % 2 == 1;
    }

    sealed record Placed(Solid S, PathsD Shrunk)
    {
        public bool Inside(V3 p, double eps = 0.03)
        {
            var (u, v, w) = S.Pose.Local(p);
            return Math.Abs(w) <= S.Thickness / 2 - eps && In(Shrunk, u, v);
        }
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

    /// <summary>Places where two solids would be in the same space (sampled where their boxes meet).</summary>
    static List<string> Clashes(IReadOnlyList<Solid> a, IReadOnlyList<Solid> b, double step = 0.25)
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
        var walls = kit.Solids.Where(s => !s.IsFurniture && s.Side is not (WallSide.None or WallSide.Floor) && s.Name != "Front door").ToList();
        var columns = kit.Solids.Where(s => s.Name is "Corner column" or "Column cap").ToList();
        var floors = kit.Solids.Where(s => s.Side == WallSide.Floor).ToList();
        Assert.Equal(4, walls.Count(w => w.Storey == 0));
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
        var spec = BuildingSpec.Preset(k);
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
            Assert.Equal(H, c.Thickness, 6);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void Furniture_stands_in_its_room(BuildingKind k)
    {
        var spec = BuildingSpec.Preset(k);
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

    [Fact]
    public void A_swinging_door_fits_the_doorway()
    {
        var spec = BuildingSpec.Preset(BuildingKind.Cottage);
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
        var two = BuildingKitMaker.Make(BuildingSpec.Preset(BuildingKind.Cottage) with { TwoSided = true });
        Assert.Equal(4, two.Pieces.Where(p => p.Name.EndsWith("back panel", StringComparison.Ordinal)).Sum(p => p.Copies));
        var tower = Make(BuildingKind.Watchtower);
        Assert.Contains(tower.Pieces, w => w.Name == "Walls, storey 2" && w.Copies == 4);
        Assert.Contains(tower.Pieces, w => w.Name == "Walls, battlements" && w.Copies == 4);
        Assert.Contains(tower.Pieces, w => w.Name == "Back and side walls, storey 1" && w.Copies == 3);
    }

    [Fact]
    public void Spec_survives_saving()
    {
        var spec = BuildingSpec.Preset(BuildingKind.Tavern);
        var back = BuildingSpec.FromJson(spec.ToJson())!;
        Assert.Equal(spec.ToJson(), back.ToJson());
        Assert.Equal("Bar counter", back.Label(4));
        Assert.Equal("Table", back.Label(0));
        Assert.Null(BuildingSpec.FromJson("{ not json"));
    }

    [Fact]
    public void Scaling_resizes_everything_but_the_sheets()
    {
        var s = BuildingSpec.Preset(BuildingKind.Cottage);
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
        var s = BuildingSpec.Preset(BuildingKind.Cottage);
        Assert.False(BuildingKitMaker.Make(s with { WidthMm = 20 }).Ok);
        Assert.False(BuildingKitMaker.Make(s with { ColumnMm = 4 }).Ok);
        var big = BuildingKitMaker.Make(s with { WidthMm = 300 });
        Assert.NotEmpty(Placement.TooBig(big, MachineProfile.F2Ultra));
    }

    [Fact]
    public void Goes_into_the_design_a_sheet_at_a_time()
    {
        var spec = BuildingSpec.Preset(BuildingKind.Cottage);
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
        var spec = BuildingSpec.Preset(BuildingKind.Cottage) with { FurnitureSheetMm = 3 };
        var sheets = Placement.Sheets(BuildingKitMaker.Make(spec), spec, new PlaceOptions());
        Assert.Single(sheets);
    }
}
