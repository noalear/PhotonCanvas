using PhotonCanvas.Core;
using PhotonCanvas.Core.Camera;
using PhotonCanvas.Core.Imaging;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Colour;
using Xunit;

namespace PhotonCanvas.Tests;

public class ColourTests
{
    [Fact]
    public void SrgbToLabMatchesReferenceValues()
    {
        var white = ColourScience.SrgbToLab(255, 255, 255);
        Assert.Equal(100, white.L, 0.01);
        Assert.Equal(0, white.A, 0.01);
        Assert.Equal(0, white.B, 0.01);
        var red = ColourScience.SrgbToLab(255, 0, 0);
        Assert.Equal(53.24, red.L, 0.05);
        Assert.Equal(80.09, red.A, 0.1);
        Assert.Equal(67.20, red.B, 0.1);
        // And back.
        Assert.Equal(((byte)255, (byte)0, (byte)0), ColourScience.LabToSrgb(red));
        var teal = ColourScience.SrgbToLab(40, 140, 150);
        Assert.Equal(((byte)40, (byte)140, (byte)150), ColourScience.LabToSrgb(teal));
    }

    [Fact]
    public void DeltaE2000MatchesSharmasTestData()
    {
        // Sharma, Wu & Dalal (2005), pairs 1, 7 and 17.
        Assert.Equal(2.0425, ColourScience.DeltaE2000(new(50, 2.6772, -79.7751), new(50, 0, -82.7485)), 3);
        Assert.Equal(2.3669, ColourScience.DeltaE2000(new(50, 0, 0), new(50, -1, 2)), 3);
        Assert.Equal(27.1492, ColourScience.DeltaE2000(new(50, 2.5, 0), new(73, 25, -18)), 3);
        Assert.Equal(0, ColourScience.DeltaE2000(new(60, 10, 10), new(60, 10, 10)), 9);
    }

    [Fact]
    public void ColourTestGridRunsFromLeastToMostEnergyWithSettingsTheSourceCanDo()
    {
        var o = new ColourTestOptions
        {
            Across = ColourParam.Speed, AcrossMin = 400, AcrossMax = 1600, AcrossSteps = 4,
            Down = ColourParam.PulseWidthNs, DownMin = 2, DownMax = 500, DownSteps = 4,
            FrequencyKhz = 3500,
        };
        var (s, added) = ColourTest.Add(DocState.New(), o);
        var cells = ColourTest.Cells(s);
        Assert.Equal(16, cells.Count);
        Assert.All(cells, c =>
        {
            Assert.Equal(LightSource.Red, c.Layer.Laser.Light);
            Assert.Equal(LayerMode.Fill, c.Layer.Mode);
            var lim = MachineProfile.F2Ultra.MopaLimitFor(c.Layer.Laser.PulseWidthNs);
            Assert.NotNull(lim); // a pulse width the source really has
            Assert.True(c.Layer.Laser.FrequencyKhz <= lim!.Value.MaxKhz);
        });
        // Rows are the pulse widths, columns the speeds.
        Assert.Equal(1600, cells.Single(c => c.Row == 0 && c.Col == 3).Layer.Laser.Speed);
        Assert.Equal(2, cells.Single(c => c.Row == 0 && c.Col == 0).Layer.Laser.PulseWidthNs);
        Assert.Equal(500, cells.Single(c => c.Row == 3 && c.Col == 0).Layer.Laser.PulseWidthNs);
        // Run order (layer order) never goes down in energy.
        var order = s.Layers.Where(l => l.Name.StartsWith(ColourTest.LayerPrefix)).Select(l => ColourTest.Energy(l.Laser, l.LinesPerCm)).ToList();
        Assert.Equal(order.Order(), order);
        Assert.Contains(s.Layers, l => l.Name == ColourTest.LabelLayerName);
        var b = ColourTest.Bounds(DocState.New(), o);
        Assert.True(b.MaxX < 220 && b.MaxY < 220);
        JobBuilder.Build(s, new JobOptions()); // builds without complaint
    }

    [Fact]
    public void MeasuresEachSquareFromAPictureOfTheBurnedTest()
    {
        var o = new ColourTestOptions { AcrossSteps = 3, DownSteps = 2, CellMm = 6, GapMm = 3 };
        var (s, _) = ColourTest.Add(DocState.New(), o);
        var cells = ColourTest.Cells(s);
        Assert.Equal(6, cells.Count);

        // A fake scan: 8 px per mm, slightly rotated and shifted; each square painted its own colour.
        var mmToPx = Homography.Fit([.. new[] { new Vec2(0, 0), new(100, 0), new(100, 100), new(0, 100) }
            .Select(p => (p, new Vec2(30 + p.X * 8 - p.Y * 0.4, 20 + p.Y * 8 + p.X * 0.4)))])!.Value;
        int w = 900, h = 700;
        var rgba = new byte[w * h * 4];
        for (int i = 0; i < w * h; i++) { rgba[i * 4] = 190; rgba[i * 4 + 1] = 190; rgba[i * 4 + 2] = 195; rgba[i * 4 + 3] = 255; }
        (byte, byte, byte)[] colours = [(200, 160, 60), (120, 70, 40), (110, 50, 130), (40, 70, 170), (80, 150, 200), (20, 20, 25)];
        var pxToMm = Homography.Fit([.. new[] { new Vec2(0, 0), new(100, 0), new(100, 100), new(0, 100) }
            .Select(p => (mmToPx.Apply(p), p))])!.Value;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var mm = pxToMm.Apply(new Vec2(x + 0.5, y + 0.5));
                int k = cells.ToList().FindIndex(c => c.Bounds.Contains(mm));
                if (k < 0) continue;
                var (r, g, b) = colours[k];
                int i = (y * w + x) * 4;
                // The last-but-one square is blotchy: its left half came out much lighter.
                bool blotch = k == 4 && mm.X < cells[k].Bounds.Center.X;
                rgba[i] = (byte)(blotch ? 230 : r); rgba[i + 1] = (byte)(blotch ? 230 : g); rgba[i + 2] = (byte)(blotch ? 230 : b);
            }
        var img = new RasterImage(w, h, rgba);
        var measured = ColourMeasure.Measure(img, mmToPx, cells);
        Assert.Equal(6, measured.Count);
        for (int k = 0; k < 6; k++)
        {
            if (k == 4) continue;
            var (r, g, b) = colours[k];
            Assert.True(ColourScience.DeltaE2000(measured[k].Colour, ColourScience.SrgbToLab(r, g, b)) < 0.5, $"square {k}");
            Assert.True(measured[k].Spread < 0.5);
            Assert.True(measured[k].Use);
        }
        Assert.True(measured[4].Spread > ColourMeasure.BlotchySpread);
        Assert.False(measured[4].Use);
        Assert.Equal(cells[2].Layer.Laser.Speed, measured[2].Settings.Laser.Speed);

        // Distinct colours: the five even squares are all clearly different.
        Assert.Equal(5, ColourMeasure.Distinct(measured).Count);
        // A near-duplicate isn't counted twice.
        var dup = measured[0] with { Row = 9, L = measured[0].L + 0.5 };
        Assert.Equal(5, ColourMeasure.Distinct([.. measured, dup]).Count);
    }

    [Fact]
    public void WhiteBalanceMakesTheReferenceNeutral()
    {
        // A warm-lit grey card: R high, B low.
        int w = 20, h = 20;
        var rgba = new byte[w * h * 4];
        for (int i = 0; i < w * h; i++) { rgba[i * 4] = 200; rgba[i * 4 + 1] = 180; rgba[i * 4 + 2] = 150; rgba[i * 4 + 3] = 255; }
        var gains = ColourMeasure.WhiteBalance(new RasterImage(w, h, rgba), [new(2, 2), new(18, 2), new(18, 18), new(2, 18)]);
        Assert.NotNull(gains);
        var (gr, gg, gb) = gains!.Value;
        double r = ColourScience.ToLinear(200 / 255.0) * gr, g = ColourScience.ToLinear(180 / 255.0) * gg, b = ColourScience.ToLinear(150 / 255.0) * gb;
        var lab = ColourScience.LinearRgbToLab(r, g, b);
        Assert.True(lab.Chroma < 0.5);
    }
}

public class MopaLimitTests
{
    static MopaLimit At(int ns) => MachineProfile.F2Ultra.MopaLimitFor(ns)!.Value;

    [Fact]
    public void LongPulsesSkipTheRangeTheSourceIsProtectedFrom()
    {
        // 200 ns: full power from 65 kHz, 48–64 kHz kept out of (as the stock software does on reflective work).
        var l = At(200);
        Assert.True(l.InGap(48));
        Assert.True(l.InGap(64));
        Assert.False(l.InGap(47));
        Assert.False(l.InGap(65));
        Assert.Equal(65, l.Safe(50));
        Assert.Equal(47, l.Safe(47));
        Assert.Equal(1000, l.Safe(5000)); // and never above its maximum
        Assert.Equal(1, l.Safe(0));
        // Short pulses have no such range.
        Assert.Null(At(6).GapFromKhz);
        Assert.Equal(300, At(6).Safe(300));

        var ex = Assert.Throws<JobException>(() => LaserSettings.Red(50, 500, 200, 55).Validate(MachineProfile.F2Ultra));
        Assert.Contains("48–64 kHz", ex.Message);
        LaserSettings.Red(50, 500, 200, 65).Validate(MachineProfile.F2Ultra);
        LaserSettings.Red(50, 500, 200, 40).Validate(MachineProfile.F2Ultra);
    }

    [Fact]
    public void BelowFullPowerFrequencyTheSourceGivesLess()
    {
        var l = At(6); // full power from 850 kHz
        Assert.Equal(1, l.PowerAvailable(850));
        Assert.Equal(1, l.PowerAvailable(2000));
        Assert.Equal(300 / 850.0, l.PowerAvailable(300), 9);
    }

    [Fact]
    public void ImportedSettingsAreBroughtWithinTheSourcesLimits()
    {
        var s = new PhotonCanvas.Doc.Materials.ProcessSettings
        {
            Laser = LaserSettings.Red(50, 500, 190, 55), // not a real pulse width, and in the protected range
            LinesPerCm = 99999,
        }.Clamped(MachineProfile.F2Ultra);
        Assert.Equal(200, s.Laser.PulseWidthNs);
        Assert.Equal(65, s.Laser.FrequencyKhz);
        Assert.Equal(FillBlock.MaxLinesPerCm, s.LinesPerCm);
    }

    [Fact]
    public void FillsDownToTwoMicronsBuild()
    {
        var s = DocState.New();
        var layer = new Layer { Name = "Colour", Mode = LayerMode.Fill, Laser = LaserSettings.Red(20, 800, 6, 500), LinesPerCm = 5000 };
        s = Ops.AddLayer(s, layer);
        s = Ops.AddShapes(s, [new PathShape { LayerId = layer.Id, Geometry = PathGeometry.Rect(10, 10, 2, 2) }]);
        var job = JobBuilder.Build(s, new JobOptions());
        Assert.Contains("G1", job.Motion);
        var tooFine = Ops.UpdateLayers(s, new HashSet<Guid> { layer.Id }, l => l with { LinesPerCm = 5001 });
        Assert.Throws<JobException>(() => JobBuilder.Build(tooFine, new JobOptions()));
    }
}
