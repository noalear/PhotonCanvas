using Clipper2Lib;
using PhotonCanvas.Addon.Buildings;
using PhotonCanvas.Addon.HouseEditor;
using PhotonCanvas.Addon.Roofs;
using Xunit;
using Geo = PhotonCanvas.Addon.Walls.Geo;

namespace PhotonCanvas.Tests;

/// <summary>
/// Buildings put together look as they should from outside: the floors between storeys stop flush with the
/// walls, a gable roof's base doesn't stick out past the gables, and the ridge caps lie on their slopes.
/// </summary>
public class BuildingFitTests
{
    /// <summary>How far a shape reaches either way along a line across it at <paramref name="at"/> (x when <paramref name="alongX"/>, else y).</summary>
    static (double Lo, double Hi) Span(PathsD shape, double at, bool alongX)
    {
        var band = alongX ? Geo.Rect(-1e4, at - 0.05, 1e4, at + 0.05) : Geo.Rect(at - 0.05, -1e4, at + 0.05, 1e4);
        var b = Geo.Bounds(Geo.Intersect(shape, [band]));
        return alongX ? (b.left, b.right) : (b.top, b.bottom);
    }

    static List<WallEdge> Box(int x1, int y1)
    {
        var list = new List<WallEdge>();
        for (int x = 0; x < x1; x++) { list.Add(WallEdge.Of(new(x, 0), new(x + 1, 0))); list.Add(WallEdge.Of(new(x, y1), new(x + 1, y1))); }
        for (int y = 0; y < y1; y++) { list.Add(WallEdge.Of(new(0, y), new(0, y + 1))); list.Add(WallEdge.Of(new(x1, y), new(x1, y + 1))); }
        return list;
    }

    [Fact]
    public void The_building_editors_upper_floor_is_flush_with_the_walls_not_sticking_out()
    {
        var d = new HouseDesign { Levels = [new Level { Walls = Box(4, 3) }, new Level { Walls = Box(4, 3) }] };
        var kit = HouseKitMaker.Make(d);
        Assert.True(kit.Ok, string.Join("\n", kit.Findings.Select(f => f.Text)));
        var s = HouseKitMaker.StyleFor(d);
        double sq = d.SquareMm, C = s.ColumnMm, Wt = BuildingKitMaker.WallThickness(s);
        Assert.True(C > Wt + 1, "The columns should be wider than the walls for this test to mean anything.");
        var plate = kit.Pieces.Single(p => p.Name == "Floor 2").Cut;
        var b = Geo.Bounds(plate);
        // Round the columns at the corners it reaches their outside...
        Assert.Equal(4 * sq + C, b.right - b.left, 1);
        // ...but along the walls between them it stops at the walls' outside faces.
        double midY = (b.top + b.bottom) / 2, midX = (b.left + b.right) / 2;
        var (lo, hi) = Span(plate, midY, alongX: true);
        Assert.Equal(4 * sq + Wt, hi - lo, 1);
        (lo, hi) = Span(plate, midX + sq / 2, alongX: false);
        Assert.Equal(3 * sq + Wt, hi - lo, 1);
        // The 3D view shows the same plate.
        var solid = kit.Solids.Single(x => x.Kind == SolidKind.Floor);
        Assert.Equal(Geo.Area(plate), Geo.Area(solid.Outline), 1);
    }

    [Theory]
    [InlineData(BuildingKind.Cottage)]
    [InlineData(BuildingKind.Tavern)]
    [InlineData(BuildingKind.OctagonTower)]
    [InlineData(BuildingKind.Manor)]
    public void A_building_kits_floors_dont_stick_out_past_its_walls(BuildingKind kind)
    {
        var spec = BuildingSpec.Preset(kind) with { Storeys = 2 };
        var kit = BuildingKitMaker.Make(spec);
        Assert.True(kit.Ok, string.Join("\n", kit.Findings.Select(f => f.Text)));
        double Wt = BuildingKitMaker.WallThickness(spec), C = spec.ColumnMm;
        var floors = kit.Pieces.Where(p => p.Name == "Floor").ToList();
        Assert.NotEmpty(floors);
        var fp = Footprint.Of(spec);
        // Nothing of the plate lies further out than the walls' outside faces, except round the columns.
        var allowed = Geo.Union(fp.Outline(Wt / 2 + 0.05), BuildingKitMaker.ColumnOutlines(spec, fp, C + 0.1));
        foreach (var f in floors)
        {
            // The rectangle's plate is laid out from its first corner column; shaped plates are on the footprint's plan.
            var cut = fp.IsRectangle ? Geo.Move(f.Cut, fp.Corners.Min(c => c.At.x), fp.Corners.Min(c => c.At.y)) : f.Cut;
            Assert.True(Geo.Area(Geo.Subtract(cut, allowed)) < 1, $"{kind}: the floor sticks out past the walls by {Geo.Area(Geo.Subtract(cut, allowed)):0.#} mm².");
            // And it still covers the walls' tops it stands on (bar the pin holes of flat walls' columns).
            var tops = Geo.Subtract(fp.Outline(Wt / 2 - 0.3), fp.Outline(-Wt / 2));
            double bare = Geo.Area(Geo.Subtract(tops, cut));
            Assert.True(bare < 30, $"{kind}: {bare:0.#} mm² of the walls' tops has no floor on it.");
        }
    }

    static RoofSpec Gable(RoofStyle style, double pitch) => new()
    {
        Style = style, LengthMm = 120, WidthMm = 90, PitchDeg = pitch, ThicknessMm = 3, EaveMm = 6, GableOverhangMm = 3,
        InsetMm = 12 + 1 + 6, WallMm = 12, CornerMm = 26, Caps = true,
    };

    [Theory]
    [InlineData(RoofStyle.Gable)]
    [InlineData(RoofStyle.LeanTo)]
    public void A_gable_roofs_base_stops_at_the_gable_walls_face(RoofStyle style)
    {
        var spec = Gable(style, 35);
        var kit = RoofMaker.Make(spec);
        Assert.True(kit.Ok, string.Join("\n", kit.Findings.Select(f => f.Text)));
        var basePlate = kit.Parts.Single(p => p.Name == "Base").Cut;
        double face = spec.InsetMm - spec.WallMm;
        // Between the corners it's flush with the gable walls' outside faces at both ends...
        var (lo, hi) = Span(basePlate, spec.WidthMm / 2 + 7, alongX: true);
        Assert.Equal(face, lo, 1);
        Assert.Equal(spec.LengthMm - face, hi, 1);
        // ...and at the corners it still covers the columns' caps.
        (lo, hi) = Span(basePlate, 1, alongX: true);
        Assert.Equal(0, lo, 1);
        Assert.Equal(spec.LengthMm, hi, 1);
    }

    [Theory]
    [InlineData(RoofStyle.Gable, 20)]
    [InlineData(RoofStyle.Gable, 35)]
    [InlineData(RoofStyle.Gable, 45)]
    [InlineData(RoofStyle.Gable, 60)]
    [InlineData(RoofStyle.Hip, 30)]
    [InlineData(RoofStyle.Hip, 55)]
    public void The_ridge_caps_lie_on_their_own_slopes(RoofStyle style, double pitch)
    {
        var kit = RoofMaker.Make(Gable(style, pitch));
        Assert.True(kit.Ok, string.Join("\n", kit.Findings.Select(f => f.Text)));
        double T = kit.ThicknessMm;
        var slopes = kit.Parts.Where(p => p.Name.Contains("slope", StringComparison.OrdinalIgnoreCase) && !p.Name.Contains("cap", StringComparison.OrdinalIgnoreCase)).ToList();
        var caps = kit.Parts.Where(p => p.Name.Contains("cap", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.NotEmpty(caps);
        foreach (var cap in caps)
            foreach (var pose in cap.Poses)
            {
                // The slope under it: the cap's pose moved back down by a sheet.
                var under = pose.Moved(pose.N * -T);
                var slope = slopes.FirstOrDefault(sl => sl.Poses.Any(p => (p.O - under.O).Length < 1e-6 && (p.U - under.U).Length < 1e-6 && (p.V - under.V).Length < 1e-6));
                Assert.True(slope is not null, $"{cap.Name}: no slope under it.");
                // Every strip of the cap lies on the slope for at least a couple of mm (it doesn't float off it).
                var on = Geo.Intersect(cap.Cut, slope!.Cut);
                foreach (var strip in Geo.Tidy(cap.Cut).Where(p => Clipper.Area(p) > 0))
                {
                    double touching = Geo.Area(Geo.Intersect([strip], on));
                    var bb = Geo.Bounds([strip]);
                    double longest = Math.Max(bb.right - bb.left, bb.bottom - bb.top);
                    Assert.True(touching > longest * 2, $"{style} {pitch}°: {cap.Name} lies on its slope over only {touching:0.#} mm² ({longest:0} mm long).");
                }
            }
    }
}
