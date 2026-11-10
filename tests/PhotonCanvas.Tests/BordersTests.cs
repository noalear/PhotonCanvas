using PhotonCanvas.Addon.Borders;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using Xunit;

namespace PhotonCanvas.Tests;

/// <summary>The border maker: its designs, fitting the repeats round a frame, the pieces and the depth maps it makes.</summary>
public class BordersTests
{
    static BorderDesign Design(string name) => BorderPresets.All.First(d => d.Name == name);

    [Fact]
    public void EveryReadyMadeDesignMakesABorder()
    {
        Assert.True(BorderPresets.FrameDesigns.Count >= 25);
        Assert.True(BorderPresets.EdgeDesigns.Count >= 25);
        // An ornament band for every ornament.
        Assert.Equal(Ornaments.All.Count - 1, BorderPresets.BandDesigns.Count);
        foreach (var cat in BorderPresets.All.GroupBy(d => d.Category))
        {
            Assert.Contains(cat.Key, BorderPresets.Categories);
            Assert.Equal(cat.Count(), cat.Select(d => d.Name).Distinct().Count());
        }
        foreach (var d in BorderPresets.All)
        {
            var spec = new BorderSpec { Design = d, Layout = BorderPresets.UsualLayout(d) };
            Assert.True(spec.Problem() is null, $"{d.Name}: {spec.Problem()}");
            Assert.False(string.IsNullOrWhiteSpace(d.About), d.Name);
            var plan = new BorderPlan(spec);
            Assert.True(plan.Top > plan.Lowest, d.Name);
            // A few points across the middle of the border: all carved, all within its depth.
            double y = plan.Spec.Layout == BorderLayout.Strip ? 0 : plan.SizeH / 2;
            for (int i = 1; i < 10; i++)
            {
                double x = plan.Spec.Layout == BorderLayout.Strip ? plan.SizeW / 2 : plan.Width * i / 10.0;
                double yy = plan.Spec.Layout == BorderLayout.Strip ? plan.Width * i / 10.0 : y;
                double h = plan.HeightAt(x, yy);
                Assert.True(h >= plan.Lowest - 1e-6 && h <= plan.Top + 1e-6, $"{d.Name}: {h} at ({x}, {yy})");
            }
        }
    }

    [Fact]
    public void EveryOrnamentStaysBetweenTheFloorAndTheSurface()
    {
        foreach (var o in Ornaments.All)
        {
            var s = new OrnamentScale(10 * o.Aspect, 10, 3);
            for (double u = -1; u <= 1; u += 0.037)
                for (double v = 0; v <= 1; v += 0.05)
                {
                    double h = Ornaments.Height(o.Kind, u, v, s);
                    Assert.True(double.IsFinite(h) && h >= -1e-9 && h <= 1 + 1e-9, $"{o.Name}: {h} at u {u}, v {v}");
                }
        }
    }

    [Theory]
    [InlineData(Ornament.Hammered)]
    [InlineData(Ornament.FishScale)]
    [InlineData(Ornament.WaveScroll)]
    [InlineData(Ornament.Vitruvian)]
    public void AnOrnamentJoinsSeamlesslyFromOneRepeatToTheNext(Ornament o)
    {
        var s = new OrnamentScale(14 * Ornaments.Info(o).Aspect, 14, 3);
        for (double v = 0.02; v < 1; v += 0.07)
            for (double u = -0.5; u < 0.5; u += 0.031)
                Assert.Equal(Ornaments.Height(o, u, v, s), Ornaments.Height(o, u + 1, v, s), 9);
    }

    [Fact]
    public void TheHammeredTextureRepeatsOnlyOverSeveralBandWidths()
    {
        Assert.True(Ornaments.Info(Ornament.Hammered).Aspect >= 4);
        // Within a repeat it doesn't repeat itself: a quarter of the way along it's different.
        var s = new OrnamentScale(14 * Ornaments.Info(Ornament.Hammered).Aspect, 14, 3);
        double diff = 0;
        for (double v = 0.05; v < 1; v += 0.1)
            for (double u = 0; u < 0.25; u += 0.01)
                diff += Math.Abs(Ornaments.Height(Ornament.Hammered, u, v, s) - Ornaments.Height(Ornament.Hammered, u + 0.25, v, s));
        Assert.True(diff > 1, $"difference {diff}");
    }

    [Fact]
    public void FishScalesLieOverTheRowBelow()
    {
        // Going down the band: each scale rises toward its rounded edge, then drops to the scale below it.
        var s = new OrnamentScale(14, 14, 3);
        foreach (double u in new[] { 0.0, 0.25, 0.5 })
        {
            var down = Enumerable.Range(0, 400).Select(i => Ornaments.Height(Ornament.FishScale, u, i / 399.0, s)).ToList();
            int rises = 0, drops = 0;
            for (int i = 1; i < down.Count; i++)
            {
                if (down[i] < down[i - 1] - 0.2) drops++;
                if (down[i] > down[i - 1] + 1e-6) rises++;
            }
            Assert.True(drops >= 1, $"u {u}: {drops} edges");
            Assert.True(rises > 200, $"u {u}: rises {rises} of 400 steps");
        }
    }

    [Fact]
    public void TheWaveScrollAndTheVitruvianScrollAreDifferent()
    {
        Assert.Equal(Ornament.Vitruvian, Design("Wave frieze").Bands.Single(b => b.Ornament != Ornament.None).Ornament);
        var a = new OrnamentScale(20, 14, 3);
        double diff = 0;
        for (double v = 0.05; v < 1; v += 0.1)
            for (double u = 0; u < 1; u += 0.05)
                diff += Math.Abs(Ornaments.Height(Ornament.WaveScroll, u, v, a) - Ornaments.Height(Ornament.Vitruvian, u, v, a));
        Assert.True(diff > 20, $"difference {diff}");
    }

    [Fact]
    public void ScalingADesignGivesTheWidthAndDepthAsked()
    {
        var d = Design("Classic gilt").Scaled(50, 4);
        Assert.Equal(50, d.WidthMm, 6);
        Assert.Equal(4, d.DepthMm, 6);
        Assert.Equal(0, d.LowestMm, 6);
    }

    [Fact]
    public void TheDepthMapIsWhiteOnTheSurfaceBlackAtTheBottomAndSeeThroughInTheMiddle()
    {
        var spec = new BorderSpec { Design = Design("Classic gilt"), FrameWidthMm = 120, FrameHeightMm = 140, PixelsPerMm = 4 };
        var plan = new BorderPlan(spec);
        var piece = Assert.Single(plan.Pieces());
        var map = BorderOutput.DepthMap(plan, piece);
        Assert.Equal(480, map.Width);
        Assert.Equal(560, map.Height);
        int lo = 255, hi = 0, clear = 0;
        for (int i = 0; i < map.Width * map.Height; i++)
        {
            if (map.Rgba[i * 4 + 3] == 0) { clear++; continue; }
            lo = Math.Min(lo, map.Rgba[i * 4]);
            hi = Math.Max(hi, map.Rgba[i * 4]);
        }
        Assert.True(lo <= 3, $"darkest {lo}");
        Assert.True(hi >= 252, $"lightest {hi}");
        // The middle (left as it is) is see-through; the border isn't.
        int Alpha(int x, int y) => map.Rgba[(y * map.Width + x) * 4 + 3];
        Assert.Equal(0, Alpha(240, 280));
        Assert.Equal(255, Alpha(10, 280));
        Assert.True(clear > 0);
    }

    [Fact]
    public void ASunkPanelIsCarvedRightAcross()
    {
        var spec = new BorderSpec { Design = Design("Shaker"), FrameWidthMm = 150, FrameHeightMm = 150, Middle = MiddleFill.Floor };
        var plan = new BorderPlan(spec);
        Assert.Equal(plan.Lowest, plan.HeightAt(75, 75), 6);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AMitredFrameHasAWholeMotifInTheMiddleOfEachSide(bool onCorners)
    {
        var spec = new BorderSpec { Design = Design("Classic gilt"), FrameWidthMm = 210, FrameHeightMm = 260, MotifOnCorners = onCorners };
        var plan = new BorderPlan(spec);
        var reps = plan.Repeats().ToList();
        Assert.NotEmpty(reps);
        // With a gap on each corner an odd number puts a motif in the middle; with a motif on each corner, an even number.
        foreach (var r in reps) Assert.True(r.Repeats % 2 == (onCorners ? 0 : 1), $"{r.Run} band {r.Band}: {r.Repeats}");
        // Its symmetric ornaments mirror left to right and top to bottom.
        for (double y = 0.5; y < plan.Width; y += 1.7)
            for (double x = 0.5; x < plan.SizeW / 2; x += 3.1)
            {
                Assert.Equal(plan.HeightAt(x, y), plan.HeightAt(plan.SizeW - x, y), 3);
                Assert.Equal(plan.HeightAt(x, y), plan.HeightAt(x, plan.SizeH - y), 3);
            }
    }

    [Fact]
    public void ASquareMitredFrameTurnsItsCornersOnTheDiagonal()
    {
        var plan = new BorderPlan(new BorderSpec { Design = Design("Pearled"), FrameWidthMm = 160, FrameHeightMm = 160 });
        for (double y = 0.3; y < plan.Width; y += 1.1)
            for (double x = 0.3; x < plan.SizeW; x += 2.3)
                Assert.Equal(plan.HeightAt(x, y), plan.HeightAt(y, x), 3);
    }

    [Fact]
    public void FourSeparateSticksAreTheFramesSides()
    {
        var spec = new BorderSpec { Design = Design("Classic gilt"), FrameWidthMm = 300, FrameHeightMm = 400, FourPieces = true };
        var plan = new BorderPlan(spec);
        var pieces = plan.Pieces();
        Assert.Equal(new[] { "Top side", "Right side", "Bottom side", "Left side" }, pieces.Select(p => p.Name));
        Assert.Equal(300, pieces[0].WidthMm, 6);
        Assert.Equal(400, pieces[1].WidthMm, 6);
        Assert.All(pieces, p => Assert.Equal(plan.Width, p.HeightMm, 6));
        // The top stick is the frame's top side; past its mitres there's nothing.
        for (double x = 1; x < 300; x += 7.3)
            for (double y = 0.2; y < plan.Width && y < x && y < 300 - x; y += 2.9)
                Assert.Equal(plan.HeightAt(x, y), plan.HeightAt(pieces[0], x, y), 6);
        Assert.True(double.IsNaN(plan.HeightAt(pieces[0], 2, plan.Width - 1)));
        // The right stick, its outer edge along the top of its own picture.
        Assert.Equal(plan.HeightAt(300 - 3, 200), plan.HeightAt(pieces[1], 200, 3), 6);
        // Each stick fits a 220 mm bed only lengthways if it's short enough; the cut lines are its four corners.
        Assert.Equal(4, Assert.Single(plan.CutLines(pieces[0])).Count);
    }

    [Fact]
    public void ALongStripIsMadeInPiecesThatCarryOn()
    {
        var spec = new BorderSpec { Design = Design("Rope edge"), Layout = BorderLayout.Strip, LengthMm = 500, MaxPieceMm = 200 };
        var plan = new BorderPlan(spec);
        var pieces = plan.Pieces();
        Assert.Equal(3, pieces.Count);
        Assert.Equal("Part 2 of 3", pieces[1].Name);
        Assert.All(pieces, p => Assert.True(p.WidthMm <= 200));
        Assert.Equal(500, pieces.Sum(p => p.WidthMm), 6);
        for (double x = 0.5; x < pieces[1].WidthMm; x += 4.1)
            Assert.Equal(plan.HeightAt(x + pieces[1].From, 3), plan.HeightAt(pieces[1], x, 3), 6);
    }

    [Fact]
    public void MitredEndsAreCutAt45Degrees()
    {
        var plan = new BorderPlan(new BorderSpec { Design = Design("Chamfer"), Layout = BorderLayout.Strip, LengthMm = 100, MitredEnds = true });
        double w = plan.Width;
        Assert.False(double.IsNaN(plan.HeightAt(w * 0.5, w * 0.4)));
        Assert.True(double.IsNaN(plan.HeightAt(w * 0.4, w * 0.5)));
        Assert.True(double.IsNaN(plan.HeightAt(100 - w * 0.4, w * 0.5)));
    }

    [Fact]
    public void AnOvalIsCarvedRoundItsRimOnly()
    {
        var plan = new BorderPlan(new BorderSpec { Design = Design("Rope twist"), Layout = BorderLayout.Oval, FrameWidthMm = 160, FrameHeightMm = 200 });
        Assert.True(double.IsNaN(plan.HeightAt(80, 100)));
        Assert.True(double.IsNaN(plan.HeightAt(2, 2)));
        Assert.False(double.IsNaN(plan.HeightAt(80, plan.Width / 2)));
        Assert.False(double.IsNaN(plan.HeightAt(plan.Width / 2, 100)));
        var h = plan.Render(plan.Pieces()[0], 0, 0, 2, 80, 100);
        Assert.Contains(h, v => float.IsNaN(v));
        Assert.All(h.Where(v => !float.IsNaN(v)), v => Assert.InRange(v, plan.Lowest - 1e-4, plan.Top + 1e-4));
    }

    [Fact]
    public void CornerBlocksAndRoundedCornersMakeAWholeFrame()
    {
        foreach (var corners in new[] { CornerStyle.Blocks, CornerStyle.Rounded })
        {
            var plan = new BorderPlan(new BorderSpec { Design = Design("Louis XVI"), FrameWidthMm = 180, FrameHeightMm = 220, Corners = corners, CornerRadiusMm = 25 });
            Assert.False(double.IsNaN(plan.HeightAt(plan.Width / 2, plan.Width / 2)), $"{corners}: the corner");
            Assert.False(double.IsNaN(plan.HeightAt(90, plan.Width / 2)), $"{corners}: the top");
            Assert.True(double.IsNaN(plan.HeightAt(90, 110)), $"{corners}: the middle");
        }
        // Rounded: the very corner is outside it.
        var round = new BorderPlan(new BorderSpec { Design = Design("Louis XVI"), Corners = CornerStyle.Rounded, CornerRadiusMm = 30 });
        Assert.True(double.IsNaN(round.HeightAt(0.5, 0.5)));
    }

    [Fact]
    public void TheSizeCanBeTheOpening()
    {
        var spec = new BorderSpec { Design = Design("Shaker"), FrameWidthMm = 127, FrameHeightMm = 178, SizeIsOpening = true };
        var plan = new BorderPlan(spec);
        Assert.Equal(127 + 2 * plan.Width, plan.SizeW, 6);
        Assert.Equal(178 + 2 * plan.Width, plan.SizeH, 6);
    }

    [Fact]
    public void ProblemsAreSaidInWords()
    {
        Assert.NotNull(new BorderSpec { Design = BorderPresets.Default with { Bands = [] } }.Problem());
        Assert.NotNull(new BorderSpec { Design = Design("Classic gilt"), FrameWidthMm = 60, FrameHeightMm = 60 }.Problem());
        Assert.NotNull(new BorderSpec { Design = Design("Chamfer"), Layout = BorderLayout.Strip, LengthMm = 10, MitredEnds = true }.Problem());
        Assert.Null(new BorderSpec().Problem());
    }

    [Fact]
    public void ASpecComesBackFromItsJson()
    {
        var spec = new BorderSpec
        {
            Design = Design("Celtic plait") with { Name = "Mine", Category = BorderPresets.MyDesigns },
            Layout = BorderLayout.Oval, FrameWidthMm = 123, FrameHeightMm = 145, Corners = CornerStyle.Rounded, WidthMm = 30, DepthMm = 2.5,
            Middle = MiddleFill.InnerEdge, PixelsPerMm = 12, SoftenMm = 0.1, CutLines = true, MotifOnCorners = true,
        };
        var back = BorderSpec.FromJson(spec.ToJson());
        Assert.NotNull(back);
        Assert.Equal(spec.ToJson(), back!.ToJson());
        Assert.Equal(spec.Design.Bands.Count, back.Design.Bands.Count);
        Assert.Null(BorderSpec.FromJson("not json"));
    }

    [Fact]
    public void ABigPieceIsMadeAtFewerPixelsPerMm()
    {
        var spec = new BorderSpec { Design = Design("Rope edge"), Layout = BorderLayout.Strip, LengthMm = 1500, PixelsPerMm = 10 };
        var plan = new BorderPlan(spec);
        var p = plan.Pieces()[0];
        double ppm = BorderOutput.PixelsPerMm(spec, p);
        Assert.True(ppm * 1500 <= BorderOutput.MaxSide + 1e-6);
        Assert.Equal(10, BorderOutput.PixelsPerMm(spec with { LengthMm = 200 }, new BorderPlan(spec with { LengthMm = 200 }).Pieces()[0]), 6);
    }

    [Fact]
    public void PutInTheDesignMakesADepthSlicesLayerAndCanBeFoundAgain()
    {
        var spec = new BorderSpec { Design = Design("Classic gilt"), FrameWidthMm = 300, FrameHeightMm = 300, FourPieces = true, PixelsPerMm = 2, CutLines = true };
        var plan = new BorderPlan(spec);
        var maps = plan.Pieces().Select(p => (p, BorderOutput.DepthMap(plan, p))).ToList();
        var (state, ids, layerId) = BorderOutput.Add(DocState.New(), plan, maps, MachineProfile.F2Ultra);
        var layer = state.Layers.Single(l => l.Id == layerId);
        Assert.Equal(LayerMode.Image, layer.Mode);
        Assert.Equal(ImageEngraveMode.Slices, layer.ImageMode);
        var pictures = state.Shapes.OfType<ImageShape>().Where(s => s.LayerId == layerId).ToList();
        Assert.Equal(4, pictures.Count);
        Assert.Contains(pictures, s => s.Name.EndsWith("top side"));
        // Cut lines on a Cut layer, one per stick.
        Assert.Equal(4, state.Shapes.OfType<PathShape>().Count(s => state.Layers.Single(l => l.Id == s.LayerId).Mode == LayerMode.Cut));
        Assert.Equal(8, ids.Count);

        // Picking any one of them finds the whole border, its spec and where it was.
        var found = BorderOutput.Find(state, new HashSet<Guid> { pictures[2].Id });
        Assert.NotNull(found);
        Assert.Equal(spec.ToJson(), found!.Value.Spec.ToJson());
        Assert.Equal(8, found.Value.Ids.Count);
        Assert.Equal(layerId, found.Value.LayerId);
        Assert.Null(BorderOutput.Find(DocState.New(), new HashSet<Guid>()));

        // Made again where it was, on the same layer.
        var again = Ops.Delete(state, found.Value.Ids);
        var (next, ids2, layer2) = BorderOutput.Add(again, plan, maps, MachineProfile.F2Ultra, found.Value.TopLeft, again.Layers.Single(l => l.Id == layerId));
        Assert.Equal(layerId, layer2);
        Assert.Equal(8, ids2.Count);
        var b = next.Shapes.Where(s => ids2.Contains(s.Id) && s is ImageShape).Select(s => s.Bounds).ToList();
        Assert.Equal(found.Value.TopLeft.X, b.Min(r => r.MinX), 6);
        Assert.Equal(found.Value.TopLeft.Y, b.Min(r => r.MinY), 6);
    }
}
