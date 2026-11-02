using PhotonCanvas.Addon.Hollow;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Geometry;
using Tri = PhotonCanvas.Doc.Import.StlDepth.Triangle;
using V3 = PhotonCanvas.Doc.Import.StlDepth.Vec3;

namespace PhotonCanvas.Tests;

public class HollowSlicesTests
{
    static IEnumerable<Tri> Quad(V3 a, V3 b, V3 c, V3 d) => [new Tri(a, b, c), new Tri(a, c, d)];

    static List<Tri> Box(double x0, double y0, double z0, double x1, double y1, double z1)
    {
        V3 P(double x, double y, double z) => new(x, y, z);
        return
        [
            .. Quad(P(x0, y0, z0), P(x1, y0, z0), P(x1, y1, z0), P(x0, y1, z0)), .. Quad(P(x0, y0, z1), P(x0, y1, z1), P(x1, y1, z1), P(x1, y0, z1)),
            .. Quad(P(x0, y0, z0), P(x0, y0, z1), P(x1, y0, z1), P(x1, y0, z0)), .. Quad(P(x0, y1, z0), P(x1, y1, z0), P(x1, y1, z1), P(x0, y1, z1)),
            .. Quad(P(x0, y0, z0), P(x0, y1, z0), P(x0, y1, z1), P(x0, y0, z1)), .. Quad(P(x1, y0, z0), P(x1, y0, z1), P(x1, y1, z1), P(x1, y1, z0)),
        ];
    }

    // A 40 × 40 × 30 mm block in 3 mm slices: 10 slices.
    static readonly List<Tri> Block = Box(0, 0, 0, 40, 40, 30);
    static SliceOptions Opts(HollowOptions h, double pin = 0, double dowel = 0) => new()
    {
        HeightMm = 30, ThicknessMm = 3, PinMm = pin, DowelMm = dowel, Modifiers = [new HollowSlices(h)],
    };

    static double Area(PathGeometry g) => StackSlicer.Area(g);
    static HollowReport Report(SliceStack s) => (HollowReport)s.Extras[HollowSlices.Key];

    [Fact]
    public void Without_the_add_on_nothing_changes()
    {
        var plain = StackSlicer.Slice(Block, new SliceOptions { HeightMm = 30, ThicknessMm = 3 });
        Assert.Empty(plain.Notes);
        Assert.Empty(plain.Extras);
        Assert.All(plain.Parts, p => Assert.Equal(1600, Area(p.Outline), 0));
    }

    [Fact]
    public void Middle_slices_lose_their_middle_and_the_top_and_bottom_stay_closed()
    {
        var s = StackSlicer.Slice(Block, Opts(new HollowOptions { WallMm = 5 }));
        Assert.Equal(10, s.Parts.Count);
        // A 5 mm wall is two 3 mm slices: 0, 1 and 8, 9 stay solid; the rest are 40 mm squares with a 30 mm hole.
        foreach (var p in s.Parts)
        {
            bool cap = p.Slice <= 2 || p.Slice >= 9;
            Assert.Equal(cap ? 1600 : 1600 - 900, Area(p.Outline), 0);
        }
        var r = Report(s);
        Assert.Equal(6, r.HollowSliceCount);
        Assert.Equal(6 * 900 / 16000.0, r.Saved, 2);
        Assert.Contains("Hollowed with a 5 mm wall", Assert.Single(s.Notes));
    }

    [Fact]
    public void Open_bottom_and_top_carry_the_hollow_out()
    {
        var s = StackSlicer.Slice(Block, Opts(new HollowOptions { WallMm = 5, OpenBottom = true, OpenTop = true }));
        Assert.All(s.Parts, p => Assert.Equal(700, Area(p.Outline), 0));
    }

    [Fact]
    public void Pins_go_in_the_walls_not_the_hollow()
    {
        var s = StackSlicer.Slice(Block, Opts(new HollowOptions { WallMm = 6 }, pin: 2));
        Assert.Equal(9, s.Pins.Count == 0 ? 0 : s.Pins.Select(p => p.Slice).Distinct().Count());
        foreach (var (slice, at) in s.Pins)
        {
            // Every pin hole is inside both parts' material (a hole in it, not in the hollow).
            foreach (var p in s.Parts.Where(p => p.Slice == slice || p.Slice == slice + 1))
            {
                var probe = PathGeometry.Ellipse(at.X, at.Y, 2, 2);
                var hollow = ShapeGeometry.Boolean(PathGeometry.Rect(6, 6, 28, 28), probe, BoolOp.Intersect);
                if (p.Slice is > 2 and < 9) Assert.True(hollow.IsEmpty || Area(hollow) < 0.5, $"pin at {at} sits in the hollow of slice {p.Slice}");
            }
        }
    }

    [Fact]
    public void Dowels_keep_solid_round_them_and_parts_stay_in_one_piece()
    {
        var s = StackSlicer.Slice(Block, Opts(new HollowOptions { WallMm = 5 }, dowel: 4));
        Assert.Equal(2, s.Dowels.Count);
        Assert.Equal(10, s.Parts.Count); // nothing fell into pieces
        var r = Report(s);
        Assert.True(r.HollowSliceCount > 0);
        foreach (var p in s.Parts)
            Assert.Single(StackSlicer.Islands(p.Outline));
    }

    [Fact]
    public void Channel_goes_up_through_the_base_into_the_hollow()
    {
        var s = StackSlicer.Slice(Block, Opts(new HollowOptions { WallMm = 5, ChannelMm = 6 }));
        var r = Report(s);
        Assert.NotNull(r.ChannelAt);
        Assert.Equal(2, r.ChannelSlices);
        var hole = Math.PI * 9;
        Assert.Equal(1600 - hole, Area(s.Parts[0].Outline), 0);
        Assert.Equal(1600 - hole, Area(s.Parts[1].Outline), 0);
        Assert.Equal(1600, Area(s.Parts[9].Outline), 0);
    }

    [Fact]
    public void Magnet_pockets_go_in_the_base_under_solid()
    {
        var s = StackSlicer.Slice(Block, Opts(new HollowOptions { WallMm = 5, MagnetMm = 8, MagnetThickMm = 3, Magnets = 4 }));
        var r = Report(s);
        Assert.Equal(4, r.MagnetsAt.Count);
        Assert.Equal(1, r.MagnetSlices);
        Assert.InRange(Area(s.Parts[0].Outline), 1600 - 4 * Math.PI * 16 - 2, 1600 - 4 * Math.PI * 16 + 2);
        Assert.Equal(1600, Area(s.Parts[1].Outline), 0);
        // Slice 2 above is solid (the base is two slices), so they can go anywhere clear of the edge, spread out.
        Assert.All(r.MagnetsAt, m => Assert.True(m.X >= 5.4 && m.X <= 34.6 && m.Y >= 5.4 && m.Y <= 34.6, $"magnet at {m}"));
        Assert.True(r.MagnetsAt.SelectMany(a => r.MagnetsAt.Where(b => b != a).Select(b => a.Dist(b))).Min() > 20);
    }

    [Fact]
    public void Magnets_keep_out_from_under_an_open_hollow()
    {
        var s = StackSlicer.Slice(Block, Opts(new HollowOptions { WallMm = 9, OpenBottom = true, MagnetMm = 3, MagnetThickMm = 2, Magnets = 2 }));
        var r = Report(s);
        // Open bottom: only the 9 mm wall is solid; the pockets go in it, clear of the 22 mm hollow.
        Assert.Equal(2, r.MagnetsAt.Count);
        Assert.All(r.MagnetsAt, m => Assert.True(Math.Max(Math.Max(9 - m.X, m.X - 31), Math.Max(9 - m.Y, m.Y - 31)) >= 1.5 + 1.2 - 0.1, $"magnet at {m}"));
        Assert.Contains("magnet pockets", s.Notes[0]);
    }

    [Fact]
    public void Too_small_to_hollow_says_so()
    {
        var s = StackSlicer.Slice(Box(0, 0, 0, 8, 8, 30), new SliceOptions { HeightMm = 30, ThicknessMm = 3, Modifiers = [new HollowSlices(new HollowOptions { WallMm = 5 })] });
        Assert.Equal(0, Report(s).HollowSliceCount);
        Assert.Contains("Too small to hollow", s.Notes[0]);
    }
}
