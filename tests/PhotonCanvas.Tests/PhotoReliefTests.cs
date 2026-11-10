using PhotonCanvas.Addon.PhotoRelief;
using PhotonCanvas.Core;
using PhotonCanvas.Core.Imaging;
using PhotonCanvas.Doc;
using Xunit;

namespace PhotonCanvas.Tests;

/// <summary>Photo to relief: its filters, finding the subject, the relief's shape, the plaque, and putting it in the design.</summary>
public class PhotoReliefTests
{
    /// <summary>A picture drawn by a function of (x, y) in 0–1 giving (r, g, b).</summary>
    static RasterImage Draw(int w, int h, Func<double, double, (int R, int G, int B)> f)
    {
        var px = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var (r, g, b) = f((x + 0.5) / w, (y + 0.5) / h);
                int p = (y * w + x) * 4;
                px[p] = (byte)Math.Clamp(r, 0, 255); px[p + 1] = (byte)Math.Clamp(g, 0, 255); px[p + 2] = (byte)Math.Clamp(b, 0, 255); px[p + 3] = 255;
            }
        return new RasterImage(w, h, px);
    }

    /// <summary>
    /// A made-up portrait: a pale face (an oval, with dark eyes and darker hair on top) and dark
    /// shoulders running off the bottom, on a plain blue-grey wall.
    /// </summary>
    static ReliefPhoto Portrait() => new(Draw(300, 360, (x, y) =>
    {
        double fx = (x - 0.5) / 0.22, fy = (y - 0.42) / 0.28;
        bool face = fx * fx + fy * fy < 1;
        bool shoulders = y > 0.72 && Math.Abs(x - 0.5) < 0.42 - (0.78 - Math.Min(y, 0.78)) * 2;
        if (face)
        {
            if (y < 0.24) return (60, 45, 35); // hair
            bool eye = Math.Abs(y - 0.40) < 0.02 && (Math.Abs(x - 0.42) < 0.03 || Math.Abs(x - 0.58) < 0.03);
            return eye ? (40, 30, 30) : (228, 190, 165);
        }
        if (shoulders) return (40, 45, 70);
        return (150, 160, 175);
    }), "Test portrait");

    static int At(ReliefResult r, double x, double y) => (int)(y * r.Rows) * r.Cols + (int)(x * r.Cols);

    [Fact]
    public void BlurKeepsAnEvenPictureAndItsAverage()
    {
        int w = 50, h = 40;
        var flat = Enumerable.Repeat(0.3f, w * h).ToArray();
        Assert.All(Filters.Gaussian(flat, w, h, 6), v => Assert.Equal(0.3f, v, 4));
        var rnd = new Random(3);
        var noise = Enumerable.Range(0, w * h).Select(_ => (float)rnd.NextDouble()).ToArray();
        var b = Filters.Gaussian(noise, w, h, 2);
        Assert.Equal(noise.Average(), b.Average(), 1);
        // Smoother: much less spread.
        double Spread(float[] a) { double m = a.Average(); return Math.Sqrt(a.Average(v => (v - m) * (v - m))); }
        Assert.True(Spread(b) < Spread(noise) / 3);
    }

    [Fact]
    public void DistanceIsExact()
    {
        int w = 21, h = 15;
        var inside = Enumerable.Repeat(true, w * h).ToArray();
        Assert.All(Filters.DistanceInside(inside, w, h), v => Assert.True(float.IsPositiveInfinity(v)));
        inside[7 * w + 10] = false;
        var d = Filters.DistanceInside(inside, w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                Assert.Equal(Math.Sqrt((x - 10) * (x - 10) + (y - 7) * (y - 7)), d[y * w + x], 3);
    }

    [Fact]
    public void GuidedSmoothKeepsEdgesAndSmoothsNoise()
    {
        int w = 60, h = 30;
        var rnd = new Random(5);
        var img = new float[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                img[y * w + x] = (x < 30 ? 0.2f : 0.8f) + (float)(rnd.NextDouble() - 0.5) * 0.04f;
        var o = Filters.Guided(img, w, h, 3, 0.01f);
        // The step stays a step…
        Assert.True(o[15 * w + 27] < 0.3 && o[15 * w + 32] > 0.7);
        // …and the noise either side is smoother.
        double Rough(float[] a) { double s = 0; for (int x = 5; x < 24; x++) s += Math.Abs(a[15 * w + x + 1] - a[15 * w + x]); return s; }
        Assert.True(Rough(o) < Rough(img) / 2);
    }

    [Fact]
    public void StraightBrightnessFollowsTheBrightness()
    {
        var photo = new ReliefPhoto(Draw(200, 100, (x, _) => ((int)(255 * x), (int)(255 * x), (int)(255 * x))), "ramp");
        var spec = ReliefStyles.Find("Straight brightness")!.Apply(new ReliefSpec { WidthMm = 100 });
        var r = PhotoDepth.Make(spec, photo, 200, 100);
        int row = 50 * 200;
        for (int x = 5; x < 195; x += 10) Assert.True(r.Heights[row + x + 10] >= r.Heights[row + x] - 1e-4, $"at {x}");
        Assert.True(r.Heights[row + 2] < 0.05 && r.Heights[row + 197] > 0.95);
        // Darker parts higher turns it round.
        var dark = PhotoDepth.Make(spec with { DarkIsHigh = true }, photo, 200, 100);
        Assert.True(dark.Heights[row + 2] > 0.95 && dark.Heights[row + 197] < 0.05);
    }

    [Fact]
    public void APortraitStandsUpOffItsPlainBackground()
    {
        var photo = Portrait();
        // No plaque, so the corners are background (the field) too.
        var spec = ReliefStyles.Find("Portrait")!.Apply(new ReliefSpec { WidthMm = 100 }) with { Shape = PlaqueShape.None, BackgroundDepth = 80 };
        var r = PhotoDepth.Make(spec, photo, 250, 300);
        Assert.False(r.BackgroundNotFound);
        Assert.False(r.BusyBackground);
        // The face and the shoulders (which run off the bottom) are the subject; the wall isn't.
        Assert.InRange(r.SubjectShare, 0.25, 0.5);
        double field = 1 - 0.8;
        Assert.Equal(field, r.Heights[At(r, 0.05, 0.05)], 2);
        Assert.Equal(field, r.Heights[At(r, 0.95, 0.5)], 2);
        Assert.True(r.Heights[At(r, 0.5, 0.45)] > field + 0.3, "the face stands up");
        Assert.True(r.Heights[At(r, 0.5, 0.95)] > field + 0.05, "the shoulders do too");
        // Rounded: the middle of the face is higher than near its edge.
        Assert.True(r.Heights[At(r, 0.5, 0.5)] > r.Heights[At(r, 0.705, 0.5)]);
        Assert.All(r.Heights, v => Assert.InRange(v, 0f, 1f));
    }

    [Fact]
    public void ThePreviewFindsTheSameSubjectAsTheDepthMap()
    {
        var photo = Portrait();
        var spec = ReliefStyles.Find("Portrait")!.Apply(new ReliefSpec { WidthMm = 100 });
        var small = PhotoDepth.Make(spec, photo, 120, 144);
        var big = PhotoDepth.Make(spec, photo, 700, 840);
        Assert.Equal(big.SubjectShare, small.SubjectShare, 1);
    }

    [Fact]
    public void EvenOutFlattensTheCliffs()
    {
        // A pale face with dark hair across its top: plain brightness makes a cliff at the hairline.
        var photo = new ReliefPhoto(Draw(200, 200, (_, y) => y < 0.4 ? (40, 35, 30) : (225, 190, 170)), "hairline");
        var plain = new ReliefSpec { Subject = SubjectFrom.WholePicture, Shape = PlaqueShape.None, Raise = 0, Smooth = 0, Detail = 50, Form = 50 };
        double Cliff(ReliefResult r)
        {
            double most = 0;
            for (int y = 60; y < 100; y++) most = Math.Max(most, Math.Abs(r.Heights[(y + 1) * 200 + 100] - r.Heights[y * 200 + 100]));
            return most;
        }
        var raw = PhotoDepth.Make(plain with { EvenOut = 0 }, photo, 200, 200);
        var even = PhotoDepth.Make(plain with { EvenOut = 90 }, photo, 200, 200);
        Assert.True(Cliff(even) < Cliff(raw) * 0.6, $"{Cliff(even)} vs {Cliff(raw)}");
    }

    [Fact]
    public void PlaquesRimsAndSunkReliefs()
    {
        var photo = Portrait();
        var oval = ReliefStyles.Find("Portrait")!.Apply(new ReliefSpec { WidthMm = 100 }) with { RimMm = 5 };
        var r = PhotoDepth.Make(oval, photo, 200, 240);
        // Nothing carved outside the oval; its rim's outer half is the surface.
        Assert.True(float.IsNaN(r.Heights[At(r, 0.01, 0.01)]));
        Assert.True(float.IsNaN(r.Heights[At(r, 0.99, 0.99)]));
        Assert.Equal(1f, r.Heights[At(r, 0.5, 0.008)], 2);
        Assert.Equal(1f, r.Heights[At(r, 0.006, 0.5)], 2);
        var map = PhotoDepth.DepthMap(r);
        Assert.Equal(0, map.Rgba[3]);
        Assert.Equal(255, map.Rgba[(At(r, 0.5, 0.5)) * 4 + 3]);

        // A circle is as tall as it is wide.
        var coin = ReliefStyles.Find("Coin or medal")!.Apply(new ReliefSpec { WidthMm = 60 });
        Assert.Equal((60.0, 60.0), coin.Size(photo.Width, photo.Height));
        Assert.Equal((100.0, 120.0), oval.Size(photo.Width, photo.Height));
        Assert.Equal((100.0, 70.0), (oval with { KeepPhotoShape = false, HeightMm = 70 }).Size(photo.Width, photo.Height));

        // Sunk: the background is the surface, the subject goes down into it.
        var sunk = PhotoDepth.Make(ReliefStyles.Find("Sunk relief")!.Apply(new ReliefSpec { WidthMm = 100 }), photo, 200, 240);
        Assert.Equal(1f, sunk.Heights[At(sunk, 0.05, 0.05)], 2);
        Assert.True(sunk.Heights[At(sunk, 0.5, 0.45)] < 0.99);
        Assert.True(sunk.Heights[At(sunk, 0.29, 0.45)] < sunk.Heights[At(sunk, 0.5, 0.5)] - 0.2, "cut in deepest at the outline");
    }

    [Fact]
    public void ClickingTheBackgroundTakesMoreAway()
    {
        // A grey wall with a green bush at the bottom left, joined to the bottom edge only: the
        // background is read from the top and sides, so the bush counts as subject until it's clicked.
        var photo = new ReliefPhoto(Draw(200, 200, (x, y) =>
        {
            if ((x - 0.5) * (x - 0.5) + (y - 0.45) * (y - 0.45) < 0.04) return (230, 200, 180);
            if (x > 0.05 && x < 0.3 && y > 0.75) return (40, 140, 50);
            return (160, 160, 165);
        }), "bush");
        var spec = new ReliefSpec { Shape = PlaqueShape.None, WidthMm = 100 };
        var before = PhotoDepth.Make(spec, photo, 200, 200);
        var after = PhotoDepth.Make(spec with { Picks = [new PhotoPoint(0.15, 0.9)] }, photo, 200, 200);
        Assert.True(after.SubjectShare < before.SubjectShare - 0.05, $"{after.SubjectShare} vs {before.SubjectShare}");
        Assert.True(after.Heights[At(after, 0.15, 0.9)] < before.Heights[At(before, 0.15, 0.9)] - 0.05);
    }

    [Fact]
    public void ABusyOrMissingBackgroundIsSaid()
    {
        // Nothing plain round the edges: no background found, so the whole photo is the subject.
        var rnd = new Random(9);
        var busy = new ReliefPhoto(Draw(150, 150, (_, _) => (rnd.Next(256), rnd.Next(256), rnd.Next(256))), "noise");
        var r = PhotoDepth.Make(new ReliefSpec { Shape = PlaqueShape.None }, busy, 150, 150);
        Assert.True(r.BackgroundNotFound || r.BusyBackground);
        // A photo that's all one colour has nothing but background: the whole of it is used.
        var flat = new ReliefPhoto(Draw(100, 100, (_, _) => (120, 120, 120)), "flat");
        Assert.True(PhotoDepth.Make(new ReliefSpec(), flat, 100, 100).BackgroundNotFound);
    }

    [Fact]
    public void EveryStyleMakesAReliefWithinTheDepth()
    {
        var photo = Portrait();
        Assert.Equal(ReliefStyles.All.Count, ReliefStyles.All.Select(s => s.Name).Distinct().Count());
        foreach (var style in ReliefStyles.All)
        {
            var spec = style.Apply(new ReliefSpec { WidthMm = 80 });
            Assert.Null(spec.Problem(photo.Width, photo.Height));
            Assert.False(string.IsNullOrWhiteSpace(style.About));
            var r = PhotoDepth.Make(spec, photo, 120, 140);
            var carved = r.Heights.Where(v => !float.IsNaN(v)).ToList();
            Assert.True(carved.Count > 100, style.Name);
            Assert.All(carved, v => Assert.InRange(v, 0f, 1f));
            Assert.True(carved.Max() - carved.Min() > 0.3, $"{style.Name} has depth");
        }
    }

    [Fact]
    public void ZoomingAndMovingCropsThePhoto()
    {
        var photo = Portrait();
        var spec = new ReliefSpec { Shape = PlaqueShape.Circle, WidthMm = 50 };
        var (x, y, w, h) = PhotoDepth.Window(spec, photo);
        // A circle on a tall photo: the whole width, the middle of its height.
        Assert.Equal(300, w, 6);
        Assert.Equal(300, h, 6);
        Assert.Equal(30, y, 6);
        var zoomed = PhotoDepth.Window(spec with { Zoom = 2, PanX = -1, PanY = 1 }, photo);
        Assert.Equal((0.0, 210.0, 150.0, 150.0), zoomed);
        // The crop is made from that part.
        var crop = PhotoDepth.Crop(spec with { Zoom = 2, PanX = -1, PanY = 1 }, photo, 40, 40);
        Assert.Equal(40, crop.Width);
    }

    [Fact]
    public void ItGoesInTheDesignAndCanBeOpenedAgain()
    {
        var photo = Portrait();
        var spec = ReliefStyles.Find("Portrait")!.Apply(new ReliefSpec { WidthMm = 60, PixelsPerMm = 4 }) with { CutLine = true, Picks = [new PhotoPoint(0.1, 0.1)] };
        var r = ReliefOutput.Full(spec, photo);
        Assert.Equal((240, 288), (r.Cols, r.Rows));
        var machine = MachineProfile.F2Ultra;
        var (state, ids, layerId) = ReliefOutput.Add(DocState.New(), spec, photo, PhotoDepth.DepthMap(r), r.WidthMm, r.HeightMm, machine, new Vec2(80, 90));
        var layer = state.Layers.First(l => l.Id == layerId);
        Assert.Equal(ImageEngraveMode.Slices, layer.ImageMode);
        Assert.StartsWith("Relief · Test portrait", layer.Name);
        Assert.Equal(2, ids.Count);
        var pic = state.Shapes.OfType<ImageShape>().Single();
        Assert.Equal(60, pic.SizeMm.X, 6);
        Assert.Equal(72, pic.SizeMm.Y, 6);
        Assert.Equal(80, (pic.Bounds.MinX + pic.Bounds.MaxX) / 2, 6);
        Assert.Equal(90, (pic.Bounds.MinY + pic.Bounds.MaxY) / 2, 6);
        var cut = state.Shapes.OfType<PathShape>().Single();
        Assert.Equal(LayerMode.Cut, state.Layers.First(l => l.Id == cut.LayerId).Mode);

        // Picking either finds the relief, its photo and its settings again.
        var found = ReliefOutput.Find(state, new HashSet<Guid> { cut.Id, pic.Id });
        Assert.NotNull(found);
        Assert.Equal(spec.ToJson(), found.Value.Spec.ToJson());
        Assert.Equal(photo.Width, found.Value.Photo.Width);
        Assert.Equal("Test portrait", found.Value.Photo.Name);
        Assert.Equal(new HashSet<Guid>(ids), found.Value.Ids);
        Assert.Null(ReliefOutput.Find(state, new HashSet<Guid>()));
    }

    [Fact]
    public void SettingsSurviveSavingAndSizesAreCapped()
    {
        var spec = new ReliefSpec { Style = "Pet", Subject = SubjectFrom.SeeThrough, Shape = PlaqueShape.Rounded, Zoom = 1.5, PanX = -0.3, Picks = [new PhotoPoint(0.2, 0.7)] };
        var back = ReliefSpec.FromJson(spec.ToJson());
        Assert.NotNull(back);
        Assert.Equal(spec.ToJson(), back.ToJson());
        Assert.Null(ReliefSpec.FromJson("not json"));
        // 40 per mm on a 400 mm plaque would be 16000 pixels a side: lowered to stay within 6000.
        Assert.Equal(15, ReliefOutput.PixelsPerMm(new ReliefSpec { PixelsPerMm = 40 }, 400, 300), 6);
        Assert.Equal(10, ReliefOutput.PixelsPerMm(new ReliefSpec(), 100, 120), 6);
        Assert.NotNull(new ReliefSpec { WidthMm = 20, RimMm = 10 }.Problem(100, 100));
    }

    [Fact]
    public void BigPhotosAreWorkedOnSmaller()
    {
        var big = new ReliefPhoto(Draw(5000, 1000, (x, _) => ((int)(x * 255), 0, 0)), "wide");
        Assert.True(big.Width <= ReliefPhoto.MaxSide);
        Assert.Equal(5.0, (double)big.Width / big.Height, 1);
        Assert.False(big.HasSeeThrough);
        Assert.Equal(big.Width / 3, big.At(3).Width);
    }
}
