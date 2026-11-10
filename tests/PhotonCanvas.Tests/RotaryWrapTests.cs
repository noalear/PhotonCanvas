using PhotonCanvas.Addon.Borders;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using Xunit;

namespace PhotonCanvas.Tests;

/// <summary>
/// The border maker's "round part on the rotary": rings, flutes and spirals wrapped round a round part, meeting
/// themselves round the back, put in the design once round the rotary start line, and the job's check.
/// </summary>
public class RotaryWrapTests
{
    /// <summary>A design by name: an ornament band (the same at both edges) before a frame or edge of the same name.</summary>
    static BorderDesign Design(string name) => BorderPresets.BandDesigns.FirstOrDefault(d => d.Name == name) ?? BorderPresets.All.First(d => d.Name == name);

    static BorderSpec Wrap(string design, WrapWay way, double dia = 20, int copies = 1, double width = 8) => new()
    {
        Design = Design(design), Layout = BorderLayout.Rotary, PartDiameterMm = dia, Wrap = way, WrapCopies = copies,
        WidthMm = width, WrapLengthMm = 60, WrapGapMm = 4,
    };

    /// <summary>
    /// How far apart the heights are just either side of the join round the back (y = 0 and once round),
    /// against how far apart they are across the same distance anywhere else: a seam shows as a jump.
    /// </summary>
    static (double Seam, double Elsewhere) Join(BorderPlan plan, double y0 = 0)
    {
        const double E = 0.02;
        double seam = 0, elsewhere = 0;
        int n = 0;
        for (double x = 0.37; x < plan.SizeW; x += 0.53)
        {
            double a = plan.HeightAt(x, y0 + E), b = plan.HeightAt(x, plan.SizeH - E);
            if (double.IsNaN(a) || double.IsNaN(b)) continue;
            seam += Math.Abs(a - b);
            // The same 2E step, a third of the way round.
            double m = plan.SizeH / 3;
            elsewhere += Math.Abs(plan.HeightAt(x, m + E) - plan.HeightAt(x, m - E));
            n++;
        }
        Assert.True(n > 5, "the wrap was hardly carved where it was looked at");
        return (seam / n, elsewhere / n);
    }

    static void AssertSeamless(BorderPlan plan, string what)
    {
        var (seam, elsewhere) = Join(plan);
        double depth = plan.Top - plan.Lowest;
        // Less than one of 32 depth slices: nothing a carving would show.
        Assert.True(seam <= 3 * elsewhere + depth / 40, $"{what}: a jump of {seam:0.###} mm at the join (elsewhere {elsewhere:0.###} mm)");
    }

    [Fact]
    public void EveryOrnamentGoesRoundAPartAndMeetsItselfRoundTheBack()
    {
        // Across the join round the back it's just as across the end of any one repeat and the start of the next
        // (some patterns, like basketweave, have edges there of their own).
        const double E = 0.02;
        foreach (var d in BorderPresets.BandDesigns)
            foreach (double dia in new[] { 9.5, 31.7 })
                foreach (bool centred in new[] { false, true })
                {
                    var spec = Wrap(d.Name, WrapWay.Round, dia, width: 9) with { CentreEachSide = centred };
                    Assert.Null(spec.Problem());
                    var plan = new BorderPlan(spec);
                    var (_, _, reps, pitch) = plan.Repeats().Single();
                    Assert.Equal(plan.SizeH, reps * pitch, 6);
                    for (double x = 0.1; x < plan.SizeW && reps > 1; x += 0.37)
                    {
                        double a = plan.HeightAt(x, pitch - E), b = plan.HeightAt(x, plan.SizeH - E), c = plan.HeightAt(x, pitch + E), e = plan.HeightAt(x, E);
                        Assert.True(Math.Abs(a - b) < 1e-6 && Math.Abs(c - e) < 1e-6, $"{d.Name} round Ø {dia}{(centred ? ", centred" : "")} at {x:0.##}: {a} / {b}, {c} / {e} (pitch {pitch})");
                    }
                    if (!centred) AssertSeamlessIfSmooth(plan, d);
                }
    }

    /// <summary>For the ornaments without edges of their own: no jump at the join at all.</summary>
    static void AssertSeamlessIfSmooth(BorderPlan plan, BorderDesign d)
    {
        if (d.Bands.Any(b => b.Ornament is Ornament.Basketweave or Ornament.Herringbone or Ornament.Chevron or Ornament.Billet or Ornament.Dentil or Ornament.GreekKey or Ornament.Fret)) return;
        AssertSeamless(plan, d.Name);
    }

    [Theory]
    [InlineData(WrapWay.Along, 1)]
    [InlineData(WrapWay.Along, 5)]
    [InlineData(WrapWay.Spiral, 1)]
    [InlineData(WrapWay.Spiral, 3)]
    public void FlutesAndSpiralsMeetThemselvesRoundTheBackToo(WrapWay way, int copies)
    {
        foreach (var name in new[] { "Rope", "Greek key", "Egg and dart", "Acanthus", "Plait", "Hammered" })
        {
            var spec = Wrap(name, way, 30, copies, width: 6);
            Assert.Null(spec.Problem());
            AssertSeamless(new BorderPlan(spec), $"{name} {way} ×{copies}");
            AssertSeamless(new BorderPlan(spec with { WrapMirrored = true, WrapLeftHand = true, WrapTurned = true }), $"{name} {way} ×{copies} mirrored");
        }
    }

    [Fact]
    public void RingsGoRoundWithGapsBetween()
    {
        var spec = Wrap("Rope", WrapWay.Round, 25, copies: 3, width: 8);
        var plan = new BorderPlan(spec);
        Assert.Equal(3 * 8 + 2 * 4, plan.SizeW, 6);
        Assert.Equal(Math.PI * 25, plan.SizeH, 6);
        Assert.Single(plan.Pieces());
        Assert.Empty(plan.CutLines(plan.Pieces()[0]));
        // Carved on the rings, left alone in the gaps.
        Assert.False(double.IsNaN(plan.HeightAt(4, 10)));
        Assert.True(double.IsNaN(plan.HeightAt(10, 10)));
        Assert.False(double.IsNaN(plan.HeightAt(16, 10)));
        // Every ring the same.
        for (double y = 0.5; y < plan.SizeH; y += 1.3)
            Assert.Equal(plan.HeightAt(3, y), plan.HeightAt(3 + 12, y), 9);
        // A whole number of repeats once round.
        var (_, _, reps, pitch) = plan.Repeats().First();
        Assert.Equal(plan.SizeH, reps * pitch, 6);
    }

    [Fact]
    public void MirroredRingsAreTheSameBothWaysAndTurnedRoundSwapsSides()
    {
        var spec = Wrap("Classic gilt", WrapWay.Round, 40, width: 10) with { WrapMirrored = true };
        var plan = new BorderPlan(spec);
        Assert.Equal(20, plan.SizeW, 6);
        for (double x = 0.33; x < 10; x += 0.71)
            Assert.Equal(plan.HeightAt(x, 40), plan.HeightAt(20 - x, 40), 6);
        var plain = new BorderPlan(spec with { WrapMirrored = false });
        var turned = new BorderPlan(spec with { WrapMirrored = false, WrapTurned = true });
        for (double x = 0.33; x < 10; x += 0.71)
            Assert.Equal(plain.HeightAt(x, 40), turned.HeightAt(10 - x, 40), 6);
    }

    [Fact]
    public void FlutesAreFittedToGoRoundWithoutAGap()
    {
        var spec = Wrap("Reeded", WrapWay.Along, 30, copies: 6, width: 99);
        Assert.Equal(Math.PI * 30 / 6, spec.BorderWidth, 9);
        Assert.Equal(Math.PI * 30 / 12, (spec with { WrapMirrored = true }).BorderWidth, 9);
        var plan = new BorderPlan(spec);
        Assert.Equal(spec.BorderWidth, plan.Width, 6);
        Assert.Equal(60, plan.SizeW, 6);
        // Each copy the same, all the way round.
        double p = plan.SizeH / 6;
        for (double x = 1; x < 60; x += 7.3)
            for (double y = 0.2; y < p; y += 0.9)
                Assert.Equal(plan.HeightAt(x, y), plan.HeightAt(x, y + 4 * p), 9);
    }

    [Fact]
    public void ASpiralRunsAtTheSlantItsStartsNeed()
    {
        // A plain profile (no ornament): the height doesn't change along the band, only across it.
        var design = new BorderDesign { Name = "Bead", Bands = [new BorderBand { WidthMm = 6, TopMm = 2, Profile = BandProfile.Bead, DropMm = 2 }] };
        foreach (int starts in new[] { 1, 2, 4 })
            foreach (bool left in new[] { false, true })
            {
                var spec = new BorderSpec { Design = design, Layout = BorderLayout.Rotary, Wrap = WrapWay.Spiral, PartDiameterMm = 25, WrapCopies = starts, WrapLengthMm = 120, WrapLeftHand = left };
                Assert.Null(spec.Problem());
                var plan = new BorderPlan(spec);
                double c = Math.PI * 25, across = starts * 6;
                double lead = across * c / Math.Sqrt(c * c - across * across);
                Assert.Equal(lead, plan.SpiralLeadMm, 6);
                // Along the band: a lead along the part for once round (the other way round for a left-hand twist).
                for (double a = 0.05; a < 0.6; a += 0.11)
                {
                    double y0 = 20, y1 = left ? y0 - a * c : y0 + a * c;
                    y1 -= Math.Floor(y1 / c) * c;
                    Assert.Equal(plan.HeightAt(10, y0), plan.HeightAt(10 + a * lead, y1), 6);
                }
                // Across it, it changes.
                Assert.NotEqual(plan.HeightAt(10, 20), plan.HeightAt(11.5, 20), 3);
            }
    }

    [Fact]
    public void WrapSettingsComeBackFromTheirJson()
    {
        var spec = Wrap("Barley twist", WrapWay.Spiral, 33.3, copies: 2) with { WrapMirrored = true, WrapLeftHand = true, WrapTurned = true, WrapLengthMm = 140 };
        var back = BorderSpec.FromJson(spec.ToJson());
        Assert.NotNull(back);
        Assert.Equal(spec.ToJson(), back!.ToJson());
        Assert.Equal(BorderLayout.Rotary, back.Layout);
        Assert.Equal(WrapWay.Spiral, back.Wrap);
        Assert.Equal(33.3, back.PartDiameterMm, 9);
        Assert.True(back.WrapLeftHand && back.WrapMirrored && back.WrapTurned);
        // An older border, saved before rotary parts, still reads.
        var old = BorderSpec.FromJson(new BorderSpec { Design = Design("Rope") }.ToJson().Replace("\"PartDiameterMm\"", "\"Gone\""));
        Assert.NotNull(old);
        Assert.Equal(25, old!.PartDiameterMm, 9);
    }

    [Fact]
    public void ProblemsWithARotaryPartAreSaidInWords()
    {
        Assert.Contains("diameter", Wrap("Rope", WrapWay.Round, dia: 1).Problem());
        Assert.Contains("375", Wrap("Rope", WrapWay.Round, dia: 130).Problem());
        Assert.Contains("Too wide to spiral", Wrap("Rope", WrapWay.Spiral, dia: 10, copies: 3, width: 10).Problem());
        Assert.Contains("rings", Wrap("Rope", WrapWay.Round, copies: 0).Problem());
        Assert.Contains("1 mm", Wrap("Rope", WrapWay.Along, dia: 10, copies: 60).Problem());
        Assert.Null(Wrap("Rope", WrapWay.Spiral, dia: 40, copies: 2, width: 8).Problem());
    }

    [Fact]
    public void TheDepthMapIsExactlyOnceRoundAndItsTopRowFollowsOnFromItsBottomRow()
    {
        var spec = Wrap("Greek key", WrapWay.Round, 22, copies: 2, width: 10) with { PixelsPerMm = 8, SoftenMm = 0.15 };
        var plan = new BorderPlan(spec);
        var piece = plan.Pieces()[0];
        var map = BorderOutput.DepthMap(plan, piece);
        Assert.Equal(Math.Round(Math.PI * 22 * 8), map.Height);
        Assert.Equal(Math.Round(plan.SizeW * 8), map.Width);
        // The first row against the last, as against two rows next to each other anywhere else.
        double Row(int a, int b)
        {
            double sum = 0;
            int n = 0;
            for (int x = 0; x < map.Width; x++)
            {
                int ia = (a * map.Width + x) * 4, ib = (b * map.Width + x) * 4;
                if (map.Rgba[ia + 3] == 0 || map.Rgba[ib + 3] == 0) continue;
                sum += Math.Abs(map.Rgba[ia] - map.Rgba[ib]);
                n++;
            }
            return sum / Math.Max(1, n);
        }
        double seam = Row(0, map.Height - 1);
        double typical = Enumerable.Range(1, map.Height - 2).Average(r => Row(r, r + 1));
        Assert.True(seam <= 3 * typical + 2, $"seam {seam:0.##} grey levels, typical {typical:0.##}");
    }

    [Fact]
    public void PutInTheDesignItGoesOnceRoundTheStartLineOnARotaryLayer()
    {
        var spec = Wrap("Rope", WrapWay.Round, 24, copies: 2, width: 8) with { PixelsPerMm = 3, CutLines = true };
        var plan = new BorderPlan(spec);
        var maps = plan.Pieces().Select(p => (p, BorderOutput.DepthMap(plan, p))).ToList();
        double c = Math.PI * 24;

        // No start line yet: one goes half way round, and the picture fills the page once round.
        var (state, ids, layerId) = BorderOutput.Add(DocState.New(), plan, maps, MachineProfile.F2Ultra);
        var start = Assert.Single(state.Guides, g => g.RotaryStart && !g.Vertical);
        Assert.Equal(c / 2, start.At, 9);
        var pic = Assert.Single(state.Shapes.OfType<ImageShape>());
        Assert.Single(ids);
        Assert.Equal(0, pic.Bounds.MinY, 6);
        Assert.Equal(c, pic.Bounds.MaxY, 6);
        Assert.Equal((MachineProfile.F2Ultra.FieldW - plan.SizeW) / 2, pic.Bounds.MinX, 6);
        // Lines along the part, no slant or turn between slices; no cut lines round a part.
        var layer = state.Layers.Single(l => l.Id == layerId);
        Assert.Equal(ImageEngraveMode.Slices, layer.ImageMode);
        Assert.Equal(0, layer.FillAngleDeg);
        Assert.Equal(0, layer.AngleStepDeg);
        Assert.StartsWith("Rotary wrap", layer.Name);
        Assert.DoesNotContain(state.Shapes, s => s is PathShape);

        // A start line somewhere else: once round it, and nothing new added.
        var moved = DocState.New() with { Guides = [new Guide(false, 70) { RotaryStart = true }] };
        var (state2, _, _) = BorderOutput.Add(moved, plan, maps, MachineProfile.F2Ultra);
        Assert.Single(state2.Guides);
        var pic2 = Assert.Single(state2.Shapes.OfType<ImageShape>());
        Assert.Equal(70 - c / 2, pic2.Bounds.MinY, 6);
        Assert.Equal(70 + c / 2, pic2.Bounds.MaxY, 6);

        // Edit border… finds it, and making it again keeps it along the part where it was.
        var found = BorderOutput.Find(state, new HashSet<Guid> { pic.Id })!.Value;
        Assert.Equal(BorderLayout.Rotary, found.Spec.Layout);
        var again = Ops.Delete(state, found.Ids);
        var (state3, _, _) = BorderOutput.Add(again, plan, maps, MachineProfile.F2Ultra, found.TopLeft with { X = 30 }, again.Layers.Single(l => l.Id == layerId));
        var pic3 = Assert.Single(state3.Shapes.OfType<ImageShape>());
        Assert.Equal(30, pic3.Bounds.MinX, 6);
        Assert.Equal(0, pic3.Bounds.MinY, 6);
    }

    static JobOptions RotaryAt(double dia) => new() { Mode = new WorkMode.Rotary(128 / (Math.PI * dia), 1, Math.PI * dia) };

    [Fact]
    public void TheJobCheckWantsTheRotaryAtTheSizeItWasMadeFor()
    {
        var spec = Wrap("Rope", WrapWay.Round, 24, width: 8) with { PixelsPerMm = 2 };
        var plan = new BorderPlan(spec);
        var maps = plan.Pieces().Select(p => (p, BorderOutput.DepthMap(plan, p))).ToList();
        var (state, _, _) = BorderOutput.Add(DocState.New(), plan, maps, MachineProfile.F2Ultra);
        var check = new RotaryWrapCheck();

        Assert.Contains("Work mode → Rotary", Assert.Single(check.Check(state, new JobOptions())));
        Assert.Contains("Object Ø is 30", Assert.Single(check.Check(state, RotaryAt(30))));
        Assert.Empty(check.Check(state, RotaryAt(24)));
        // Stretched round the part.
        var pic = state.Shapes.OfType<ImageShape>().Single();
        var stretched = Ops.Transform(state, new HashSet<Guid> { pic.Id }, Affine.Scale(1, 1.1));
        Assert.Contains("stretched", Assert.Single(check.Check(stretched, RotaryAt(24))));
        // An ordinary border, or a wrap that isn't sent, isn't its business.
        var flat = new BorderPlan(new BorderSpec { Design = Design("Rope"), Layout = BorderLayout.Strip, LengthMm = 50, PixelsPerMm = 2 });
        var (other, _, _) = BorderOutput.Add(DocState.New(), flat, flat.Pieces().Select(p => (p, BorderOutput.DepthMap(flat, p))).ToList(), MachineProfile.F2Ultra);
        Assert.Empty(check.Check(other, RotaryAt(24)));
        var off = state with { Layers = [.. state.Layers.Select(l => l with { Output = false })] };
        Assert.Empty(check.Check(off, new JobOptions()));
    }

    [Fact]
    public void ARingRoundAPartBuildsARotaryJob()
    {
        var spec = Wrap("Pearls", WrapWay.Round, 20, width: 6) with { PixelsPerMm = 4 };
        var plan = new BorderPlan(spec);
        var maps = plan.Pieces().Select(p => (p, BorderOutput.DepthMap(plan, p))).ToList();
        var (state, _, layerId) = BorderOutput.Add(DocState.New(), plan, maps, MachineProfile.F2Ultra);
        state = state with { Layers = [.. state.Layers.Select(l => l.Id == layerId ? l with { Slices = l.Slices with { Count = 4 } } : l)] };
        double c = Math.PI * 20;
        var job = JobBuilder.Build(state, RotaryAt(20) with { Machine = MachineProfile.F2Ultra with { FieldH = Math.Round(c, 2) } });
        Assert.NotEmpty(job.Job.Blocks);
        Assert.All(job.Job.Blocks.OfType<FillBlock>(), f => Assert.Equal(0, f.AngleDeg, 6));
    }
}
