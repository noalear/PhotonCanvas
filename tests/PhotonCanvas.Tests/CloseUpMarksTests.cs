using PhotonCanvas.Core;
using PhotonCanvas.Core.Camera;
using PhotonCanvas.Core.Imaging;
using PhotonCanvas.Doc;
using Xunit;

namespace PhotonCanvas.Tests;

public class CloseUpMarksTests
{
    const int W = 1200, H = 900;

    /// <summary>The camera: bed mm → photo pixels, turned like the user's close-up (148.8°), about 35 px/mm, a little perspective, maybe mirrored.</summary>
    static Homography Camera(Vec2 centreMm, bool mirror = false)
    {
        double a = 148.8 * Math.PI / 180, k = 35;
        Vec2 Map(Vec2 mm)
        {
            double x = mm.X - centreMm.X, y = (mm.Y - centreMm.Y) * (mirror ? -1 : 1);
            double px = k * (Math.Cos(a) * x - Math.Sin(a) * y), py = k * (Math.Sin(a) * x + Math.Cos(a) * y);
            double persp = 1 + 0.004 * x; // one side a touch further away
            return new Vec2(W / 2.0 + 40 + px / persp, H / 2.0 - 25 + py / persp);
        }
        var probe = new[] { new Vec2(-10, -10), new Vec2(10, -10), new Vec2(10, 10), new Vec2(-10, 10) }.Select(p => p + centreMm).ToList();
        return Homography.Fit([.. probe.Select(p => (p, Map(p)))])!.Value;
    }

    /// <summary>A photo of burned dots on wood: uneven light, grain lines, a knot, a little noise.</summary>
    static RasterImage Photo(Homography mmToPx, IReadOnlyList<Vec2> dots, double dotMm, IEnumerable<int>? leaveOut = null)
    {
        var skip = (leaveOut ?? []).ToHashSet();
        var toBed = Homography.Fit([.. dots.Take(4).Select(d => (mmToPx.Apply(d), d)).Append((mmToPx.Apply(dots[8]), dots[8]))])!.Value;
        var px = new byte[W * H * 4];
        var rnd = new Random(7);
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                double light = 0.65 + 0.25 * x / W; // light falling off across the picture
                light *= 1 + 0.05 * Math.Sin(y * 0.09 + Math.Sin(x * 0.01) * 3); // grain
                if (Math.Abs(y - 120 - x * 0.05) < 3) light *= 0.6; // a dark grain line
                if (new Vec2(x, y).Dist(new Vec2(1050, 780)) < 30) light *= 0.5; // a knot
                var mm = toBed.Apply(new Vec2(x + 0.5, y + 0.5));
                for (int i = 0; i < dots.Count; i++)
                    if (!skip.Contains(i) && mm.Dist(dots[i]) < dotMm / 2) light = 0.22;
                light += (rnd.NextDouble() - 0.5) * 0.04;
                int k = (y * W + x) * 4;
                byte v = (byte)Math.Clamp(light * 255, 0, 255);
                px[k] = v; px[k + 1] = (byte)(v * 0.85); px[k + 2] = (byte)(v * 0.65); px[k + 3] = 255;
            }
        return new RasterImage(W, H, px);
    }

    static void AssertFits(CloseUpMarks.Located? got, Homography truth, IReadOnlyList<Vec2> dots)
    {
        Assert.NotNull(got);
        foreach (var d in dots) Assert.True(got!.MmToCamera.Apply(d).Dist(truth.Apply(d)) < 2, $"{got.MmToCamera.Apply(d)} vs {truth.Apply(d)}");
        // Away from the dots too: the corners of the photo's view.
        var mid = dots[4];
        foreach (var off in new[] { new Vec2(8, 6), new Vec2(-8, -6), new Vec2(8, -6) })
            Assert.True(got!.MmToCamera.Apply(mid + off).Dist(truth.Apply(mid + off)) < 6);
        Assert.True(got!.ErrorMm < 0.05, $"error {got.ErrorMm}");
    }

    [Fact]
    public void FindsTheDotsAndWhereTheCameraLooks()
    {
        var centre = new Vec2(110, 95);
        var dots = CloseUpMarks.Places(centre, 4);
        var cam = Camera(centre);
        var got = CloseUpMarks.Locate(Photo(cam, dots, 1.2), dots);
        AssertFits(got, cam, dots);
        Assert.All(got!.Found, f => Assert.NotNull(f));
        // It sees 1200 × 900 px at 35 px/mm (34 × 26 mm) turned about 31° from square: the straight
        // part that's surely in the picture is smaller, round the dots, and all inside the picture.
        Assert.True(got.Sees.Contains(centre));
        Assert.InRange(got.Sees.Width * got.Sees.Height, 34 * 26 * 0.3, 34 * 26 * 0.8);
        foreach (var corner in new[] { got.Sees.TopLeft, new Vec2(got.Sees.MaxX, got.Sees.MinY), got.Sees.BottomRight, new Vec2(got.Sees.MinX, got.Sees.MaxY) })
        {
            var p = cam.Apply(corner);
            Assert.InRange(p.X, 0, W);
            Assert.InRange(p.Y, 0, H);
        }
    }

    [Fact]
    public void AMirroredCameraIsReadTheRightWayRound()
    {
        var centre = new Vec2(60, 140);
        var dots = CloseUpMarks.Places(centre, 4);
        var cam = Camera(centre, mirror: true);
        AssertFits(CloseUpMarks.Locate(Photo(cam, dots, 1.2), dots), cam, dots);
    }

    [Fact]
    public void ADotThatDidntBurnIsLeftOut()
    {
        var centre = new Vec2(110, 95);
        var dots = CloseUpMarks.Places(centre, 4);
        var cam = Camera(centre);
        var got = CloseUpMarks.Locate(Photo(cam, dots, 1.2, leaveOut: [7]), dots);
        AssertFits(got, cam, dots);
        Assert.Null(got!.Found[7]);
    }

    [Fact]
    public void WithoutTheDotThatTellsWhichWayRoundItGivesUp()
    {
        var centre = new Vec2(110, 95);
        var dots = CloseUpMarks.Places(centre, 4);
        Assert.Null(CloseUpMarks.Locate(Photo(Camera(centre), dots, 1.2, leaveOut: [9]), dots));
    }

    [Fact]
    public void TheDotsGoOnTheirOwnLayerAndAreFoundWhereTheyWereMoved()
    {
        var (s, ids) = CloseUpMarks.Add(DocState.New(), new Vec2(100, 100), 4, 1.2);
        Assert.Equal(10, ids.Count);
        s = Ops.Transform(s, ids.ToHashSet(), Affine.Translate(5, -3));
        var marks = CloseUpMarks.Marks(s)!;
        // The grid's first dot one step up and left of the middle (96, 96), moved by (5, −3).
        Assert.Equal(101, marks[0].X, 6);
        Assert.Equal(93, marks[0].Y, 6);
        // The odd one: half a step right of the first, 0.6 steps above it.
        Assert.Equal(103, marks[9].X, 6);
        Assert.Equal(90.6, marks[9].Y, 6);
        Assert.DoesNotContain(CloseUpMarks.Remove(s).Layers, CloseUpMarks.IsMarksLayer);
    }
}
