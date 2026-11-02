using PhotonCanvas.Core;
using PhotonCanvas.Core.Imaging;
using PhotonCanvas.Doc;
using PhotonCanvas.Addon.TouchUp;
using Xunit;

namespace PhotonCanvas.Tests;

/// <summary>Burn, look, touch up: photos of burned engravings compared with the design, and the touch-up page made from them.</summary>
public class TouchUpTests
{
    static readonly Rect2 PhotoArea = new(40, 40, 80, 70);
    const double MmPerPx = 0.05;

    /// <summary>A picture layer with a 20 × 10 mm grey ramp at (50, 50): white on the left, black on the right.</summary>
    static (DocState State, Layer Layer) RampDesign()
    {
        int w = 200, h = 100;
        var px = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int k = (y * w + x) * 4;
                px[k] = px[k + 1] = px[k + 2] = (byte)Math.Round(255 * (1 - (x + 0.5) / w));
                px[k + 3] = 255;
            }
        var layer = Layer.Default(0, LayerMode.Image) with { Name = "Photo" };
        var img = new ImageShape
        {
            Name = "Ramp", LayerId = layer.Id,
            Source = ImageSource.FromEncoded(new RasterImage(w, h, px).EncodePng()),
            SizeMm = new Vec2(20, 10),
            Transform = Affine.Translate(50, 50),
        };
        return (DocState.New() with { Layers = [layer], Shapes = [img] }, layer);
    }

    /// <summary>A fill layer with a 12 × 12 mm square at (50, 50).</summary>
    static (DocState State, Layer Layer) SquareDesign()
    {
        var layer = Layer.Default(0, LayerMode.Fill) with { Name = "Engrave", LinesPerCm = 100 };
        var sq = new PathShape { Name = "Square", LayerId = layer.Id, Geometry = PathGeometry.Rect(50, 50, 12, 12) };
        return (DocState.New() with { Layers = [layer], Shapes = [sq] }, layer);
    }

    /// <summary>
    /// A photo of the burn: light falling off to one side, the material's brightness times how
    /// much the burn darkened it there, a little noise, and the whole photo placed <paramref name="offX"/>
    /// mm off where it really is (as the camera's placement can be).
    /// </summary>
    static RasterImage Photo(Func<Vec2, double> darkness, double offX = 0, double offY = 0, int seed = 1)
    {
        int w = (int)Math.Round(PhotoArea.Width / MmPerPx), h = (int)Math.Round(PhotoArea.Height / MmPerPx);
        var rnd = new Random(seed);
        var px = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var p = new Vec2(PhotoArea.MinX + (x + 0.5) * MmPerPx - offX, PhotoArea.MinY + (y + 0.5) * MmPerPx - offY);
                double light = 0.85 - 0.25 * (p.X - PhotoArea.MinX) / PhotoArea.Width;
                double v = light * (1 - darkness(p)) + (rnd.NextDouble() - 0.5) * 0.03;
                int k = (y * w + x) * 4;
                px[k] = px[k + 1] = px[k + 2] = (byte)Math.Clamp((int)Math.Round(255 * v), 0, 255);
                px[k + 3] = 255;
            }
        return new RasterImage(w, h, px);
    }

    /// <summary>How dark the ramp should burn at a point (0 outside it), as the design says.</summary>
    static double Ramp(Vec2 p) => p.X >= 50 && p.X <= 70 && p.Y >= 50 && p.Y <= 60 ? (p.X - 50) / 20 : 0;

    static readonly Rect2 Weak = new(60, 53, 64, 57);

    [Fact]
    public void ALightPatchInAPictureIsFoundAndNothingElse()
    {
        var (s, layer) = RampDesign();
        // Burned 60% dark at full tone, except a 4 mm patch that only got half of it; the photo sits 0.3 mm right.
        var photo = Photo(p => 0.6 * Ramp(p) * (Weak.Contains(p) ? 0.5 : 1), offX: 0.3);
        var r = TouchUpAnalysis.Compare(s, [layer], photo, PhotoArea, MmPerPx, new TouchUpOptions());

        Assert.Equal(0.3, r.ShiftMm.X, 1);
        Assert.Equal(0, r.ShiftMm.Y, 1);
        Assert.False(r.MarksLighter);
        var lr = Assert.Single(r.Layers);
        Assert.Null(lr.Note);
        Assert.True(lr.NeedsTouchUp);
        Assert.True(r.NeedsTouchUp);

        // Nearly all of the touch-up is in the weak patch, and most of the patch is touched up.
        var g = r.Grid;
        int dosed = 0, inPatch = 0, patchCells = 0, patchDosed = 0;
        for (int y = 0; y < g.H; y++)
            for (int x = 0; x < g.W; x++)
            {
                var c = g.Centre(x, y);
                bool near = Weak.Inflate(0.6).Contains(c), core = Weak.Inflate(-0.6).Contains(c);
                float d = lr.Dose[y * g.W + x];
                if (d > 0) { dosed++; if (near) inPatch++; }
                if (core) { patchCells++; if (d > 0) patchDosed++; }
            }
        Assert.True(dosed > 0);
        Assert.True(inPatch >= 0.95 * dosed, $"{inPatch} of {dosed} touched-up cells are in the weak patch");
        Assert.True(patchDosed >= 0.8 * patchCells, $"{patchDosed} of {patchCells} cells of the patch are touched up");
        // The patch is about half as dark as it should be: around 0.5 of the darkness missing at its tones, times the strength.
        double mean = Enumerable.Range(0, g.Count).Where(i => lr.Dose[i] > 0).Average(i => lr.Dose[i]);
        Assert.InRange(mean / new TouchUpOptions().Strength, 0.2, 0.5);
        Assert.Contains("too light", r.Summary);
    }

    /// <summary>
    /// The ramp burned as it really is: dithered dots on the layer's 180 dpi grid, each a round
    /// burn a little wider than the dot pitch's half. What a picture's photo really looks like close up.
    /// </summary>
    static Func<Vec2, double> DitheredRamp(Rect2 weak, double weakShare)
    {
        double pitch = 25.4 / 180;
        int cols = (int)(20 / pitch), rows = (int)(10 / pitch);
        var grey = new float[cols * rows];
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++) grey[r * cols + c] = (float)(1 - ((c + 0.5) * pitch) / 20);
        var dots = Dither.Apply(grey, cols, rows, DitherMethod.Jarvis);
        return p =>
        {
            double u = (p.X - 50) / pitch - 0.5, v = (p.Y - 50) / pitch - 0.5;
            int c0 = (int)Math.Round(u), r0 = (int)Math.Round(v);
            double dark = 0;
            for (int r = r0 - 1; r <= r0 + 1; r++)
                for (int c = c0 - 1; c <= c0 + 1; c++)
                {
                    if (c < 0 || r < 0 || c >= cols || r >= rows || !dots[r * cols + c]) continue;
                    double dx = (u - c) * pitch, dy = (v - r) * pitch;
                    if (dx * dx + dy * dy < 0.085 * 0.085) dark = 0.75;
                }
            return weak.Contains(p) ? dark * weakShare : dark;
        };
    }

    [Fact]
    public void DitheredDotsAreJudgedByTheirDensity()
    {
        var (s, layer) = RampDesign();
        var even = TouchUpAnalysis.Compare(s, [layer], Photo(DitheredRamp(Weak, 1), offX: -0.2, offY: 0.15, seed: 5), PhotoArea, MmPerPx, new TouchUpOptions());
        // Whole dots fit in 19.9 mm of the 20 (as in a real job), so the burn's right edge is a dot short of the design's.
        Assert.Equal(-0.2, even.ShiftMm.X, 0.15);
        Assert.Equal(0.15, even.ShiftMm.Y, 0.15);
        Assert.True(even.Layers[0].TooLightShare < 0.01, $"{even.Layers[0].TooLightShare:P1} of an even dithered burn flagged");

        var r = TouchUpAnalysis.Compare(s, [layer], Photo(DitheredRamp(Weak, 0.4), offX: -0.2, offY: 0.15, seed: 6), PhotoArea, MmPerPx, new TouchUpOptions());
        // The weak patch's own edges pull a little; within about a grid cell and a dot.
        Assert.Equal(-0.2, r.ShiftMm.X, 0.15);
        Assert.Equal(0.15, r.ShiftMm.Y, 0.15);
        var lr = r.Layers[0];
        Assert.True(lr.NeedsTouchUp);
        var g = r.Grid;
        int dosed = 0, inPatch = 0;
        for (int i = 0; i < g.Count; i++)
            if (lr.Dose[i] > 0) { dosed++; if (Weak.Inflate(0.8).Contains(g.Centre(i % g.W, i / g.W))) inPatch++; }
        Assert.True(inPatch >= 0.9 * dosed, $"{inPatch} of {dosed} touched-up cells are in the weak patch");
    }

    [Fact]
    public void AnEvenBurnUnderUnevenLightNeedsNothing()
    {
        var (s, layer) = RampDesign();
        var photo = Photo(p => 0.6 * Ramp(p), seed: 7);
        var r = TouchUpAnalysis.Compare(s, [layer], photo, PhotoArea, MmPerPx, new TouchUpOptions());
        Assert.False(r.NeedsTouchUp);
        Assert.Equal(0, r.Layers[0].TooLight);
        Assert.Contains("nothing needs touching up", r.Summary);
    }

    [Fact]
    public void AimingDarkerTouchesUpAnEvenBurnAllOver()
    {
        var (s, layer) = RampDesign();
        var photo = Photo(p => 0.6 * Ramp(p));
        var r = TouchUpAnalysis.Compare(s, [layer], photo, PhotoArea, MmPerPx, new TouchUpOptions { AimDarker = 0.3 });
        var lr = r.Layers[0];
        Assert.True(lr.NeedsTouchUp);
        // The darker half of the ramp is short by more than the sensitivity everywhere.
        Assert.True(lr.TooLightShare > 0.4, $"{lr.TooLightShare:P0} touched up");
    }

    [Fact]
    public void FillLinesDontCountAsGapsButAWeakCornerDoes()
    {
        var (s, layer) = SquareDesign();
        var weak = new Rect2(50, 50, 56, 55);
        // Lines 0.1 mm apart, each 0.06 mm wide, 70% dark on the line; the corner got half.
        double Burn(Vec2 p)
        {
            if (p.X < 50 || p.X > 62 || p.Y < 50 || p.Y > 62) return 0;
            bool onLine = (p.Y - 50) % 0.1 < 0.06;
            return onLine ? (weak.Contains(p) ? 0.35 : 0.7) : 0.05;
        }
        double Even(Vec2 p) => p.X < 50 || p.X > 62 || p.Y < 50 || p.Y > 62 ? 0 : (p.Y - 50) % 0.1 < 0.06 ? 0.7 : 0.05;
        var even = TouchUpAnalysis.Compare(s, [layer], Photo(Even, seed: 3), PhotoArea, MmPerPx, new TouchUpOptions());
        Assert.False(even.NeedsTouchUp);

        var r = TouchUpAnalysis.Compare(s, [layer], Photo(Burn, seed: 4), PhotoArea, MmPerPx, new TouchUpOptions());
        var lr = r.Layers[0];
        Assert.True(lr.NeedsTouchUp);
        var g = r.Grid;
        for (int i = 0; i < g.Count; i++)
            if (lr.Dose[i] > 0) Assert.True(weak.Inflate(0.8).Contains(g.Centre(i % g.W, i / g.W)), $"touch-up at {g.Centre(i % g.W, i / g.W)}");

        // The page: one fill layer at the strength's share of the power, holding the corner's outline.
        var opt = new TouchUpOptions();
        var next = TouchUpPage.Add(s, r, opt, TouchUpPage.NextRound(s));
        Assert.NotNull(next);
        Assert.Equal("Touch-up 1", next!.PageName);
        var tl = Assert.Single(next.Layers);
        Assert.Equal("Touch-up 1: Engrave", tl.Name);
        Assert.Equal(LayerMode.Fill, tl.Mode);
        Assert.Equal(Math.Round(layer.Laser.Power * opt.Strength, 1), tl.Laser.Power);
        var shape = Assert.IsType<PathShape>(Assert.Single(next.Shapes));
        var b = shape.World.Flatten(0.05).SelectMany(f => f.Points).ToList();
        var box = Rect2.Bounds(b);
        Assert.True(weak.Inflate(1).Contains(box), $"outline {box} is round the weak corner");
        Assert.True(box.Width > 3 && box.Height > 2.5);
    }

    [Fact]
    public void ThePageIsItsOwnJobAndRoundsCountUp()
    {
        var (s, layer) = RampDesign();
        var photo = Photo(p => 0.6 * Ramp(p) * (Weak.Contains(p) ? 0.5 : 1));
        var r = TouchUpAnalysis.Compare(s, [layer], photo, PhotoArea, MmPerPx, new TouchUpOptions());
        var next = TouchUpPage.Add(s, r, new TouchUpOptions(), TouchUpPage.NextRound(s))!;

        Assert.Equal(["Page 1", "Touch-up 1"], Pages.Names(next));
        Assert.True(TouchUpPage.IsTouchUpPage(next.PageName));
        var tl = Assert.Single(next.Layers);
        Assert.Equal(LayerMode.Image, tl.Mode);
        Assert.Null(tl.MaterialTone);
        var pic = Assert.IsType<ImageShape>(Assert.Single(next.Shapes));
        // Over the compared area, see-through where nothing is added (never burned, even inverted).
        Assert.True(pic.ShownBounds.Inflate(0.01).Contains(r.Grid.Area) || r.Grid.Area.Inflate(0.01).Contains(pic.ShownBounds));
        var c = Weak.Center;
        var local = pic.LocalToPixel(pic.Transform.Invert().Apply(c));
        Assert.False(pic.Source.SeeThrough(local.X, local.Y));
        Assert.True(pic.Source.SampleLuminance(local.X, local.Y) < 1);
        var far = pic.LocalToPixel(pic.Transform.Invert().Apply(new Vec2(52, 58)));
        Assert.True(pic.Source.SeeThrough(far.X, far.Y));

        // It builds as a job of its own, with only the touch-up in it.
        var job = JobBuilder.Build(next, new JobOptions());
        Assert.NotNull(job);

        // Comparing again from the touch-up page compares with the design.
        var design = TouchUpPage.DesignOf(next);
        Assert.NotNull(design);
        Assert.Equal("Page 1", design!.PageName);
        Assert.Equal(layer.Id, Assert.Single(TouchUpAnalysis.Candidates(design)).Id);
        Assert.Equal(2, TouchUpPage.NextRound(next));
        // Nothing to touch up: no page.
        var fine = TouchUpAnalysis.Compare(s, [layer], Photo(p => 0.6 * Ramp(p)), PhotoArea, MmPerPx, new TouchUpOptions());
        Assert.Null(TouchUpPage.Add(s, fine, new TouchUpOptions(), 1));
    }

    [Fact]
    public void MarksLighterThanTheMaterialAreFollowed()
    {
        var (s, layer) = RampDesign();
        // Dark material, marks that come out lighter (negative darkness), with a patch half as light as it should be.
        var photo = Photo(p => 0.55 - 0.8 * Ramp(p) * (Weak.Contains(p) ? 0.5 : 1));
        var r = TouchUpAnalysis.Compare(s, [layer], photo, PhotoArea, MmPerPx, new TouchUpOptions());
        Assert.True(r.MarksLighter);
        Assert.True(r.Layers[0].NeedsTouchUp);
        var g = r.Grid;
        int i = (int)((Weak.Center.Y - g.Area.MinY) / g.Pitch) * g.W + (int)((Weak.Center.X - g.Area.MinX) / g.Pitch);
        Assert.True(r.Layers[0].Dose[i] > 0);
    }

    [Fact]
    public void PhotosThatCantBeJudgedSaySo()
    {
        var (s, layer) = RampDesign();
        // Nothing burned at all.
        var blank = Photo(_ => 0);
        var e = Assert.Throws<TouchUpException>(() => TouchUpAnalysis.Compare(s, [layer], blank, PhotoArea, MmPerPx, new TouchUpOptions()));
        Assert.False(string.IsNullOrWhiteSpace(e.Message));
        // A photo of somewhere else on the bed.
        Assert.Throws<TouchUpException>(() => TouchUpAnalysis.Compare(s, [layer], blank, new Rect2(200, 200, 240, 230), MmPerPx, new TouchUpOptions()));
        // No layers.
        Assert.Throws<TouchUpException>(() => TouchUpAnalysis.Compare(s, [], blank, PhotoArea, MmPerPx, new TouchUpOptions()));
    }

    [Fact]
    public void ReliefAndCutLayersArentCandidates()
    {
        var (s, layer) = RampDesign();
        var relief = layer with { Id = Guid.NewGuid(), Name = "Relief", ImageMode = ImageEngraveMode.Slices };
        var cut = Layer.Default(2, LayerMode.Cut);
        var sh = (ImageShape)s.Shapes[0];
        s = s with
        {
            Layers = [layer, relief, cut],
            Shapes = [sh, new ImageShape { LayerId = relief.Id, Source = sh.Source, SizeMm = sh.SizeMm, Transform = sh.Transform },
                new PathShape { LayerId = cut.Id, Geometry = PathGeometry.Rect(0, 0, 5, 5) }],
        };
        Assert.Equal([layer.Id], TouchUpAnalysis.Candidates(s).Select(l => l.Id));
    }
}
