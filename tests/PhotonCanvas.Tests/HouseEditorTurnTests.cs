using Clipper2Lib;
using PhotonCanvas.Addon.Furniture;
using PhotonCanvas.Addon.HouseEditor;
using PhotonCanvas.Addon.Walls;
using Geo = PhotonCanvas.Addon.Walls.Geo;
using Railing = PhotonCanvas.Addon.Stairs.Railing;
using Severity = PhotonCanvas.Addon.Buildings.Severity;

namespace PhotonCanvas.Tests;

/// <summary>The building editor's furniture turned to 45°, drawn from above as made, and ways up with the stairs maker's own settings.</summary>
public class HouseEditorTurnTests
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
    public void Furniture_turned_45_degrees_stands_turned_where_the_plan_shows_it()
    {
        var style = HouseDesign.DefaultStyle();
        var bed = HouseKitMaker.NewItem(style, ItemKind.Bed);
        var placed = new PlacedItem { X = 3, Y = 3, Turn = 0.5, Item = bed };
        var d = new HouseDesign { Levels = [new Level { Walls = Box(0, 0, 6, 6), Furniture = [placed] }] };
        var kit = HouseKitMaker.Make(d);
        Assert.True(kit.Ok, string.Join("\n", kit.Findings.Select(f => f.Text)));
        Assert.DoesNotContain(kit.Findings, f => f.Level == Severity.Warn);
        double sq = d.SquareMm;

        // On the plan: a turned rectangle, its corners off the grid's lines, as big as the bed.
        var outline = HouseKitMaker.ItemOutline(style, placed, sq);
        var (w, dp) = HouseKitMaker.Footprint(style, bed);
        Assert.Equal(w * dp, Math.Abs(Clipper.Area(outline)) * sq * sq, 1);
        var area = HouseKitMaker.ItemArea(style, placed, sq);
        Assert.Equal((w + dp) / Math.Sqrt(2), (area.right - area.left) * sq, 1);
        Assert.Equal((w + dp) / Math.Sqrt(2), (area.bottom - area.top) * sq, 1);
        // Its front faces down and to the right of the plan (between straight ahead and a quarter turn).
        double fx = (outline[0].x + outline[1].x) / 2 - 3, fy = (outline[0].y + outline[1].y) / 2 - 3;
        Assert.True(fx > 0 && fy > 0);
        Assert.Equal(fx, fy, 6);

        // In 3D: its pieces stand round the same middle, and fill the same turned box.
        var (ox, oy) = kit.Origin;
        var pts = kit.Solids.Where(x => x.Name == "Bed").SelectMany(x => x.Outline.SelectMany(path => path.Select(q => x.Pose.At(q.x, q.y)))).ToList();
        Assert.NotEmpty(pts);
        Assert.Equal((area.left + area.right) / 2 - ox, (pts.Min(p => p.X) + pts.Max(p => p.X)) / 2 / sq, 2);
        Assert.Equal((area.top + area.bottom) / 2 - oy, (pts.Min(p => p.Z) + pts.Max(p => p.Z)) / 2 / sq, 2);
        Assert.Equal((area.right - area.left) * sq, pts.Max(p => p.X) - pts.Min(p => p.X), 1.5);

        // Saved with a half turn, and an old file's whole quarter turns still open.
        var back = HouseDesign.FromJson(d.ToJson())!;
        Assert.Equal(0.5, back.Levels[0].Furniture[0].Turn);
        var old = HouseDesign.FromJson(d.ToJson().Replace("\"Turn\":0.5", "\"Turn\":3"))!;
        Assert.Equal(3, old.Levels[0].Furniture[0].Turn);
    }

    [Fact]
    public void A_piece_turned_45_degrees_in_a_corner_is_warned_of_by_its_turned_shape()
    {
        var style = HouseDesign.DefaultStyle();
        var table = HouseKitMaker.NewItem(style, ItemKind.Table);
        var (w, dp) = HouseKitMaker.Footprint(style, table);
        double sq = HouseDesign.DefaultStyle().SquareMm;
        // Square on, pushed up to the wall it just fits; turned 45°, its corners go into the wall.
        double y = (PhotonCanvas.Addon.Buildings.BuildingKitMaker.WallThickness(style) / 2 + dp / 2 + 1.5) / sq;
        var straight = new HouseDesign { Levels = [new Level { Walls = Box(0, 0, 6, 6), Furniture = [new PlacedItem { X = 3, Y = y, Item = table }] }] };
        Assert.DoesNotContain(HouseKitMaker.Make(straight).Findings, f => f.Text.Contains("stands in a wall", StringComparison.Ordinal));
        var turned = straight with { Levels = [straight.Levels[0] with { Furniture = [new PlacedItem { X = 3, Y = y, Turn = 0.5, Item = table }] }] };
        Assert.Contains(HouseKitMaker.Make(turned).Findings, f => f.Text.Contains("stands in a wall", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(ItemKind.Bed)]
    [InlineData(ItemKind.Chair)]
    [InlineData(ItemKind.Bookshelf)]
    [InlineData(ItemKind.Table)]
    public void The_plan_draws_furniture_as_its_pieces_seen_from_above(ItemKind kind)
    {
        var style = HouseDesign.DefaultStyle();
        var item = HouseKitMaker.NewItem(style, kind);
        var look = HouseKitMaker.Look(style, item);
        Assert.NotEmpty(look.Parts);
        // Lowest first, all inside the floor it takes.
        for (int i = 1; i < look.Parts.Count; i++) Assert.True(look.Parts[i].Top >= look.Parts[i - 1].Top);
        var all = Geo.Bounds(Geo.Union([.. look.Parts.SelectMany(p => p.Shape)]));
        Assert.Equal(look.W, all.right - all.left, 0.6);
        Assert.Equal(look.D, all.bottom - all.top, 0.6);
        Assert.Equal(0, (all.left + all.right) / 2, 0.3);
        Assert.Equal(0, (all.top + all.bottom) / 2, 0.3);
        // Remembered.
        Assert.Same(look, HouseKitMaker.Look(style, item));
    }

    [Fact]
    public void A_chair_seen_from_above_has_its_back_away_from_its_front()
    {
        var style = HouseDesign.DefaultStyle();
        var look = HouseKitMaker.Look(style, HouseKitMaker.NewItem(style, ItemKind.Chair));
        // The highest piece is the back: behind the middle (−y; the front is +y).
        var back = look.Parts[^1];
        var b = Geo.Bounds(back.Shape);
        Assert.False(back.Flat);
        Assert.True((b.top + b.bottom) / 2 < 0);
    }

    [Fact]
    public void A_way_up_takes_the_stairs_makers_settings_and_different_ones_are_cut_apart()
    {
        var walls = Box(0, 0, 8, 4);
        var style = HouseDesign.DefaultStyle();
        var usual = HouseKitMaker.WaySpec(style, WayKind.Stairs);
        var own = new PlacedWay { X = 5.5, Y = 2, Kind = WayKind.Stairs, WidthMm = usual.StairWidthMm + 10, Rail = Railing.Both, RiseMm = usual.RiseMm + 1 };
        var spec = HouseKitMaker.WaySpec(style, own);
        Assert.Equal(usual.StairWidthMm + 10, spec.StairWidthMm);
        Assert.Equal(Railing.Both, spec.Rail);
        Assert.Equal(usual.GoingMm, spec.GoingMm);
        var d = new HouseDesign
        {
            Levels = [new Level { Walls = walls, WaysUp = [new PlacedWay { X = 2, Y = 2, Kind = WayKind.Stairs }, own] }, new Level { Walls = walls }],
        };
        var kit = HouseKitMaker.Make(d);
        Assert.True(kit.Ok, string.Join("\n", kit.Findings.Select(f => f.Text)));
        Assert.Contains(kit.Pieces, p => p.Name.StartsWith("Stairs 1: ", StringComparison.Ordinal));
        Assert.Contains(kit.Pieces, p => p.Name.StartsWith("Stairs 2: ", StringComparison.Ordinal));
        // Wider stairs need a wider opening.
        var (h1, _) = HouseKitMaker.WayArea(style, d.Levels[0].WaysUp[0], d.SquareMm);
        var (h2, _) = HouseKitMaker.WayArea(style, own, d.SquareMm);
        Assert.True(Geo.Area(h2) > Geo.Area(h1) + 50);
        // Two made the same are still cut together.
        var same = HouseKitMaker.Make(d with { Levels = [d.Levels[0] with { WaysUp = [d.Levels[0].WaysUp[0], d.Levels[0].WaysUp[0] with { X = 5.5 }] }, d.Levels[1]] });
        Assert.DoesNotContain(same.Pieces, p => p.Name.StartsWith("Stairs 1: ", StringComparison.Ordinal));
        // Saved and opened again.
        var back = HouseDesign.FromJson(d.ToJson())!;
        Assert.Equal(own, back.Levels[0].WaysUp[1]);
    }
}
