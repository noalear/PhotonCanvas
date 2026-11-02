using PhotonCanvas.Core;
using PhotonCanvas.Core.Imaging;
using PhotonCanvas.Doc;
using PhotonCanvas.Addon.KerfTest;

namespace PhotonCanvas.Tests;

public class KerfGaugeTests
{
    static DocState Empty => DocState.New() with { Layers = [] };

    [Fact]
    public void GaugeHasAWindowTwoSlotsTwoKeysAndScalesMarkedFirst()
    {
        var (s, added) = KerfGauge.Add(Empty, new KerfGaugeOptions { ThicknessMm = 3, Laser = LaserSettings.Blue(100, 8) });
        var cut = KerfGauge.CutLayer(s)!;
        Assert.Equal(LayerMode.Cut, cut.Mode);
        Assert.Equal(0, cut.KerfMm);
        Assert.Equal(8, cut.Laser.Speed);
        // The marks go before the cuts: the keys and the window drop once cut.
        int marks = s.Layers.FindIndex(l => l.Name == KerfGauge.MarksLayerName);
        Assert.True(marks < s.Layers.IndexOf(cut));
        Assert.Equal(LayerMode.Score, s.Layers[marks].Mode);
        Assert.True(s.Layers[marks].Laser.Power <= 15);
        Assert.Equal(2, s.ShapesOn(cut.Id).Count(x => x.Name == KerfGauge.KeyName));
        Assert.Single(s.ShapesOn(cut.Id), x => x.Name == KerfGauge.WindowName);
        Assert.Contains(added, id => s.Shapes.Any(x => x.Id == id && x is TextShape { Text: ".2" }));
        Assert.Contains(added, id => s.Shapes.Any(x => x.Id == id && x is TextShape { Text: "−.4" }));

        var g = KerfGauge.Find(s)!;
        Assert.Equal(4, g.KeyWidthMm, 6);
        Assert.Equal(3, g.ThicknessMm!.Value, 6);
        Assert.Equal(12, g.Flat.Length, 6);
        Assert.Equal(12, g.Edge!.Length, 6);
        // The slots taper: kerf reads from just under 0 at the mouth to 0.5 at the end.
        Assert.Equal(KerfGauge.KerfAtMouth, g.KerfAt(0), 6);
        Assert.Equal(KerfGauge.KerfAtEnd, g.KerfAt(g.Flat.Length), 6);
        Assert.Equal(0.17, g.KerfAt(g.DepthForKerf(0.17)), 6);
        Assert.Equal(-0.3, g.FitAt(g.DepthForFit(-0.3)), 6);
        // Every 0.01 mm of kerf moves the key about a fifth of a mm (12 mm for 0.55 mm).
        Assert.InRange(g.DepthForKerf(0.11) - g.DepthForKerf(0.10), 0.2, 0.25);

        // Keys inside the window, clear of each other, cut before the window around them.
        var window = s.ShapesOn(cut.Id).Single(x => x.Name == KerfGauge.WindowName);
        var keys = s.ShapesOn(cut.Id).Where(x => x.Name == KerfGauge.KeyName).ToList();
        Assert.All(keys, k => Assert.True(window.Bounds.Contains(k.Bounds)));
        Assert.False(keys[0].Bounds.Intersects(keys[1].Bounds));
        var job = JobBuilder.Build(s, new JobOptions());
        Assert.DoesNotContain(job.Warnings, w => w.Contains("runs after the cut layer"));
    }

    [Fact]
    public void KeysFitTheirSlots()
    {
        var (s, _) = KerfGauge.Add(Empty, new KerfGaugeOptions { ThicknessMm = 3 });
        var g = KerfGauge.Find(s)!;
        // A key goes into the mouth of the flat slot with no kerf at all, and reaches its end only at 0.5 mm.
        Assert.True(g.Flat.MouthWidthMm > g.KeyWidthMm);
        Assert.True(g.Flat.EndWidthMm < g.KeyWidthMm - 2 * 0.45);
        // The standing key (3 mm thick) goes in at the fit slot's mouth.
        Assert.True(g.Edge!.MouthWidthMm > 3);
        // Keys are long enough to reach the end of the slot and still be held.
        var key = s.Shapes.First(x => x.Name == KerfGauge.KeyName).Bounds;
        Assert.True(key.Width >= g.Flat.Length + 4);
    }

    [Fact]
    public void ThinMaterialHasOnlyTheFlatSlot()
    {
        var (s, _) = KerfGauge.Add(Empty, new KerfGaugeOptions { ThicknessMm = 0.6 });
        var g = KerfGauge.Find(s)!;
        Assert.Null(g.Edge);
        Assert.Null(g.ThicknessMm);
        Assert.Single(s.Shapes, x => x.Name == KerfGauge.KeyName);
        Assert.True(double.IsNaN(g.FitAt(3)));
    }

    [Fact]
    public void ThickMaterialGetsATallerKey()
    {
        var (s, _) = KerfGauge.Add(Empty, new KerfGaugeOptions { ThicknessMm = 6 });
        var g = KerfGauge.Find(s)!;
        Assert.Equal(7, g.KeyWidthMm, 6);
        Assert.Equal(6, g.ThicknessMm!.Value, 6);
    }

    [Fact]
    public void FoundWhereverItWasMovedOrTurned()
    {
        var (s, _) = KerfGauge.Add(Empty, new KerfGaugeOptions { ThicknessMm = 3, Origin = new Vec2(20, 30) });
        var before = KerfGauge.Find(s)!;
        var ids = s.Shapes.Select(x => x.Id).ToHashSet();
        s = Ops.Transform(s, ids, Affine.Rotate(30).Then(Affine.Translate(15, -5)));
        var after = KerfGauge.Find(s)!;
        Assert.Equal(before.Flat.Length, after.Flat.Length, 6);
        Assert.Equal(before.KeyWidthMm, after.KeyWidthMm, 6);
        Assert.Equal(before.ThicknessMm!.Value, after.ThicknessMm!.Value, 6);
        Assert.NotEqual(before.Flat.Mouth, after.Flat.Mouth);
    }

    [Fact]
    public void AddingAgainReplacesTheOldGaugeAndRemoveTakesItAway()
    {
        var (s, _) = KerfGauge.Add(Empty, new KerfGaugeOptions());
        (s, _) = KerfGauge.Add(s, new KerfGaugeOptions { Origin = new Vec2(100, 100) });
        Assert.Single(s.Layers, l => l.Name == KerfGauge.CutLayerName);
        Assert.Single(s.Layers, l => l.Name == KerfGauge.MarksLayerName);
        s = KerfGauge.Remove(s);
        Assert.Null(KerfGauge.Find(s));
        Assert.DoesNotContain(s.Layers, KerfGauge.IsGaugeLayer);
        Assert.Empty(s.Shapes);
    }

    [Fact]
    public void ReadingGivesKerfSlotFitAndRealThickness()
    {
        var (s, _) = KerfGauge.Add(Empty, new KerfGaugeOptions { ThicknessMm = 3 });
        var g = KerfGauge.Find(s)!;
        // Kerf 0.18; the 3 mm sheet is really 2.9: the standing key stops where slot + kerf = 2.9.
        var r = new KerfGaugeReading(g.DepthForKerf(0.18), g.DepthForFit(2.9 - 0.18 - 3));
        Assert.Equal(0.18, r.Kerf(g)!.Value, 6);
        Assert.Equal(-0.28, r.RawFit(g)!.Value, 6);
        // With the kerf on the cut layer slots come out as drawn: draw them 2.9 wide, thickness − 0.1.
        Assert.Equal(-0.1, r.SlotFit(g)!.Value, 6);
        Assert.Equal(2.9, r.Thickness(g)!.Value, 6);
        // Agrees with the comb test's rule.
        Assert.Equal(CutTest.SlotFit(r.RawFit(g)!.Value, 0.18, true), r.SlotFit(g)!.Value, 9);
        Assert.Null(new KerfGaugeReading(null, null).Kerf(g));
    }

    [Fact]
    public void TaperShrinksToFitASmallView()
    {
        var o = new KerfGaugeOptions { ThicknessMm = 3 };
        var big = KerfGauge.TaperToFit(o, new Vec2(200, 200));
        Assert.Equal(KerfGauge.MaxTaperMm, big);
        var size16 = KerfGauge.Size(o with { TaperMm = 16 });
        Assert.Equal(16, KerfGauge.TaperToFit(o, size16 + new Vec2(0.5, 0.5)));
        Assert.Null(KerfGauge.TaperToFit(o, new Vec2(10, 10)));
        // The added gauge sits inside the size it says it needs.
        var at = new Vec2(5, 7);
        var (s, _) = KerfGauge.Add(Empty, o with { TaperMm = 16, Origin = at });
        var area = KerfGauge.Area(s);
        Assert.True(new Rect2(at.X - 0.01, at.Y - 0.01, at.X + size16.X + 0.01, at.Y + size16.Y + 0.01).Contains(area));
    }

    [Fact]
    public void ShortLabelsFitBetweenTicks()
    {
        Assert.Equal("0", KerfGauge.Short(0, true));
        Assert.Equal(".1", KerfGauge.Short(0.1, false));
        Assert.Equal("+.4", KerfGauge.Short(0.4, true));
        Assert.Equal("−.8", KerfGauge.Short(-0.8, true));
        Assert.Equal("−1", KerfGauge.Short(-1, true));
    }

    /// <summary>A photo of the gauge laid on the bed: light stock, keys a slightly different wood, dark open slots with honeycomb glints.</summary>
    static RasterImage Photo(KerfGauge g, Rect2 area, double mmPerPx, double flatTip, double? edgeTip, int seed = 1, Rect2? window = null)
    {
        int w = (int)Math.Ceiling(area.Width / mmPerPx), h = (int)Math.Ceiling(area.Height / mmPerPx);
        var px = new byte[w * h * 4];
        var rnd = new Random(seed);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var mm = new Vec2(area.MinX + (x + 0.5) * mmPerPx, area.MinY + (y + 0.5) * mmPerPx);
                (int r, int gg, int b) c = (205, 170, 120); // stock
                if (window is { } wr && wr.Contains(mm)) c = (x / 6 + y / 6) % 5 == 0 ? (140, 140, 145) : (35, 35, 40); // the window, open
                foreach (var (slot, tip, keyAcross) in new[] { (g.Flat, (double?)flatTip, g.KeyWidthMm), (g.Edge, edgeTip, 3.0) })
                {
                    if (slot is null) continue;
                    double d = slot.DepthOf(mm), u = (mm - slot.Mouth).Dot(slot.Across);
                    // The key sticks out of the mouth into the window (it's 5 mm longer than the slot).
                    if (tip is { } tt && d < 0 && d > tt - slot.Length - 5 && Math.Abs(u) < keyAcross / 2) { c = (190, 150, 100); continue; }
                    if (d < 0 || d > slot.Length || Math.Abs(u) > slot.WidthAt(d) / 2) continue;
                    bool key = tip is { } t && d < t && Math.Abs(u) < Math.Min(keyAcross, slot.WidthAt(d)) / 2;
                    c = key ? (190, 150, 100) : ((x / 6 + y / 6) % 5 == 0 ? (140, 140, 145) : (35, 35, 40));
                }
                int n = rnd.Next(-18, 19);
                int i = (y * w + x) * 4;
                px[i] = (byte)Math.Clamp(c.r + n, 0, 255); px[i + 1] = (byte)Math.Clamp(c.gg + n, 0, 255); px[i + 2] = (byte)Math.Clamp(c.b + n, 0, 255); px[i + 3] = 255;
            }
        return new RasterImage(w, h, px);
    }

    [Fact]
    public void FindsWhereTheKeysStoppedInACloseUpPhoto()
    {
        var (s, _) = KerfGauge.Add(Empty, new KerfGaugeOptions { ThicknessMm = 3, Origin = new Vec2(50, 40) });
        var g = KerfGauge.Find(s)!;
        var area = KerfGauge.Area(s).Inflate(3);
        double mmPerPx = 1 / 30.0;
        double flatTip = g.DepthForKerf(0.16), edgeTip = g.DepthForFit(-0.35);
        var img = Photo(g, area, mmPerPx, flatTip, edgeTip);
        var map = KerfGaugePhoto.OnBed(area, mmPerPx);
        var flat = KerfGaugePhoto.FindTip(img, map, g.Flat)!;
        var edge = KerfGaugePhoto.FindTip(img, map, g.Edge!)!;
        Assert.InRange(flat.DepthMm, flatTip - 0.1, flatTip + 0.1);
        Assert.InRange(edge.DepthMm, edgeTip - 0.1, edgeTip + 0.1);
        Assert.True(flat.Sure);
        Assert.True(edge.Sure);
        var reading = new KerfGaugeReading(flat.DepthMm, edge.DepthMm);
        Assert.InRange(reading.Kerf(g)!.Value, 0.155, 0.165);
    }

    [Fact]
    public void FindsTheTipInATurnedPhotoFromTwoClicks()
    {
        var (s, _) = KerfGauge.Add(Empty, new KerfGaugeOptions { ThicknessMm = 3 });
        var g = KerfGauge.Find(s)!;
        // The photo: the gauge drawn turned 20° and at another scale (a phone held square on).
        var turn = Affine.Rotate(20).Then(Affine.Translate(40, 10));
        var ids = s.Shapes.Select(x => x.Id).ToHashSet();
        var turned = KerfGauge.Find(Ops.Transform(s, ids, turn))!;
        var area = KerfGauge.Area(Ops.Transform(s, ids, turn)).Inflate(3);
        double mmPerPx = 1 / 22.0;
        double tip = g.DepthForKerf(0.22);
        var img = Photo(turned, area, mmPerPx, tip, null);
        // Clicking the mouth and the end in that photo gives the map from the gauge as drawn.
        var onBed = KerfGaugePhoto.OnBed(area, mmPerPx);
        var map = KerfGaugePhoto.FromTwoPoints(g.Flat.Mouth, g.Flat.End, onBed(turned.Flat.Mouth), onBed(turned.Flat.End))!;
        var found = KerfGaugePhoto.FindTip(img, map, g.Flat)!;
        Assert.InRange(found.DepthMm, tip - 0.12, tip + 0.12);
        Assert.Null(KerfGaugePhoto.FromTwoPoints(g.Flat.Mouth, g.Flat.End, new Vec2(5, 5), new Vec2(5, 5)));
    }

    [Fact]
    public void AnEmptySlotIsNotSure()
    {
        var (s, _) = KerfGauge.Add(Empty, new KerfGaugeOptions { ThicknessMm = 3 });
        var g = KerfGauge.Find(s)!;
        var area = KerfGauge.Area(s).Inflate(3);
        var img = Photo(g, area, 1 / 30.0, -10, null);
        var found = KerfGaugePhoto.FindTip(img, KerfGaugePhoto.OnBed(area, 1 / 30.0), g.Flat);
        Assert.True(found is null || !found.Sure);
        // Off the photo: nothing to read.
        Assert.Null(KerfGaugePhoto.FindTip(img, KerfGaugePhoto.OnBed(new Rect2(500, 500, 600, 600), 1 / 30.0), g.Flat));
    }

    [Fact]
    public void LinesTheMouthUpWithTheWindowEdge()
    {
        var (s, _) = KerfGauge.Add(Empty, new KerfGaugeOptions { ThicknessMm = 3, Origin = new Vec2(50, 40) });
        var g = KerfGauge.Find(s)!;
        var window = s.Shapes.Single(x => x.Name == KerfGauge.WindowName).Bounds;
        var windowOnly = new Rect2(window.MinX, window.MinY, g.Flat.Mouth.X, window.MaxY);
        var area = KerfGauge.Area(s).Inflate(3);
        double mmPerPx = 1 / 30.0, tip = g.DepthForKerf(0.2);
        var img = Photo(g, area, mmPerPx, tip, g.DepthForFit(-0.2), window: windowOnly);
        var right = KerfGaugePhoto.OnBed(area, mmPerPx);
        Assert.InRange(KerfGaugePhoto.MouthShift(img, right, g)!.Value, -0.06, 0.06);
        // A camera position 0.4 mm off along the slot: the window's edge shows it.
        var off = KerfGaugePhoto.OnBed(new Rect2(area.MinX + 0.4, area.MinY, area.MaxX + 0.4, area.MaxY), mmPerPx);
        double shift = KerfGaugePhoto.MouthShift(img, off, g)!.Value;
        Assert.InRange(shift, 0.34, 0.46);
        // Mapping through the shift reads the tip where it really is.
        Vec2 Fixed(Vec2 mm) => off(mm + g.Flat.Along * shift);
        Assert.InRange(KerfGaugePhoto.FindTip(img, Fixed, g.Flat)!.DepthMm, tip - 0.1, tip + 0.1);
    }

    [Fact]
    public void ResampleLaysThePhotoStraight()
    {
        var px = new byte[20 * 10 * 4];
        for (int i = 0; i < 200; i++) { px[i * 4] = (byte)(i % 20 < 10 ? 0 : 255); px[i * 4 + 3] = 255; }
        var img = new RasterImage(20, 10, px);
        var straight = KerfGaugePhoto.Resample(img, KerfGaugePhoto.OnBed(new Rect2(0, 0, 20, 10), 1), new Rect2(5, 0, 15, 10), 2);
        Assert.Equal(20, straight.Width);
        Assert.Equal(20, straight.Height);
        Assert.True(straight.Rgba[(10 * 20 + 2) * 4] < 60);
        Assert.True(straight.Rgba[(10 * 20 + 17) * 4] > 200);
    }

    [Fact]
    public void SavingPutsTheKerfOnTheRecipeItWasCutWith()
    {
        var recipe = new PhotonCanvas.Doc.Materials.Recipe { Title = "Cuts", Purpose = PhotonCanvas.Doc.Materials.RecipePurpose.Cut, Settings = new() { Mode = LayerMode.Cut, Laser = LaserSettings.Blue(100, 8), KerfMm = 0.1 }, SlotFitMm = 0.05 };
        var m = new PhotonCanvas.Doc.Materials.Material { Name = "Birch ply", ThicknessMm = 3, Recipes = [recipe] };
        var cut = Layer.Default(0, LayerMode.Cut) with { Laser = LaserSettings.Blue(100, 8), Material = new PhotonCanvas.Doc.Materials.MaterialRef(m.Id, m.Label, recipe.Id, recipe.Title) };
        var (saved, written) = KerfGaugeLibrary.Save(m, cut, 0.162, -0.08, 2.88, 3, new DateTime(2026, 11, 1));
        Assert.Single(saved.Recipes);
        Assert.Equal(recipe.Id, written.Id);
        Assert.Equal(0.162, written.Settings.KerfMm);
        Assert.Equal(-0.08, written.SlotFitMm);
        Assert.True(written.Tested);
        Assert.Contains("really 2.88 mm", written.Notes);
        // No slot fit read: the old one stays.
        var (_, kept) = KerfGaugeLibrary.Save(m, cut, 0.15, null, null, 3, DateTime.Now);
        Assert.Equal(0.05, kept.SlotFitMm);
    }

    [Fact]
    public void SavingWithoutASourceRecipeAddsATestedCutRecipe()
    {
        var m = new PhotonCanvas.Doc.Materials.Material { Name = "Basswood" };
        var cut = Layer.Default(0, LayerMode.Cut) with { Laser = LaserSettings.Blue(90, 12) with { Passes = 2 }, KerfMm = 0 };
        var (saved, written) = KerfGaugeLibrary.Save(m, cut, 0.14, 0.02, 3.1, 3, DateTime.Now);
        Assert.Single(saved.Recipes);
        Assert.Equal(PhotonCanvas.Doc.Materials.RecipePurpose.Cut, written.Purpose);
        Assert.Equal(LayerMode.Cut, written.Settings.Mode);
        Assert.Equal(0.14, written.Settings.KerfMm);
        Assert.Equal(12, written.Settings.Laser.Speed);
        Assert.Equal(2, written.Settings.Laser.Passes);
        Assert.Equal(3, saved.ThicknessMm);
        Assert.StartsWith("Cuts 3 mm: 12 mm/s × 2, kerf 0.14", written.Title);
    }
}
