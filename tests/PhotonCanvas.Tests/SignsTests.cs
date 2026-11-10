using PhotonCanvas.Addon.Borders;
using PhotonCanvas.Addon.Signs;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using Xunit;

namespace PhotonCanvas.Tests;

/// <summary>The sign maker: its outlines, the words fitted inside the border, the ways of carving them, holes and the depth map.</summary>
public class SignsTests
{
    static SignSpec Preset(string name) => SignPresets.All.First(p => p.Name == name).Spec;

    static SignSpec Plain(LetterStyle style, string words = "HI") => new()
    {
        Shape = SignShape.Rectangle, WidthMm = 120, HeightMm = 60, Border = null, Lines = [new(words)],
        Bold = true, Style = style, LetterDepthMm = 1, VAngleDeg = 60, MarginMm = 6,
    };

    /// <summary>The deepest and shallowest carving across the words' box, below the surface (mm).</summary>
    static (double Min, double Max) DepthRange(SignPlan plan, Func<double, bool>? where = null)
    {
        double lo = double.MaxValue, hi = double.MinValue;
        var box = plan.Words.Box;
        for (double x = box.MinX; x <= box.MaxX; x += 0.2)
            for (double y = box.MinY; y <= box.MaxY; y += 0.2)
            {
                double h = plan.HeightAt(x, y);
                if (double.IsNaN(h)) continue;
                double d = plan.Top - h;
                lo = Math.Min(lo, d);
                hi = Math.Max(hi, d);
            }
        return (lo, hi);
    }

    [Fact]
    public void EveryReadyMadeSignFitsTheBedAndItsWordsFitInside()
    {
        Assert.True(SignPresets.All.Count >= 10);
        Assert.Equal(SignPresets.All.Count, SignPresets.All.Select(p => p.Name).Distinct().Count());
        foreach (var p in SignPresets.All)
        {
            Assert.Null(p.Spec.Problem());
            Assert.False(string.IsNullOrWhiteSpace(p.About), p.Name);
            Assert.Equal(p.Name, p.Spec.Name);
            var plan = new SignPlan(p.Spec);
            Assert.True(plan.SizeW <= 220 && plan.SizeH <= 220, p.Name);
            Assert.True(plan.Words.Fits, p.Name);
            Assert.True(plan.Words.LetterHeightMm >= 8, $"{p.Name}: {plan.Words.LetterHeightMm}");
            Assert.True(plan.Top > 0, p.Name);
            if (p.Spec.Border is not null) Assert.NotNull(plan.Border);
            if (p.Spec.Holes != SignHoles.None) Assert.NotEmpty(plan.Holes);
            // Every point of the words keeps clear of the border.
            double clear = p.Spec.BorderWidth + p.Spec.EdgeMarginMm + p.Spec.MarginMm;
            foreach (var o in plan.Words.Outlines)
                foreach (var q in o)
                    Assert.True(plan.Outline.Inset(q.X, q.Y) >= clear - 1e-3, $"{p.Name}: {q} is {plan.Outline.Inset(q.X, q.Y):0.##} mm in");
        }
    }

    [Fact]
    public void TheInsetIsTheDistanceInFromTheEdgeAndSquareCornersAreMitred()
    {
        var rect = SignOutline.For(new SignSpec { Shape = SignShape.Rectangle, WidthMm = 100, HeightMm = 60 });
        Assert.Equal(4, rect.Runs.Count);
        Assert.Equal(10, rect.Inset(10, 30), 9);
        Assert.Equal(7, rect.Inset(50, 7), 9);
        Assert.Equal(-5, rect.Inset(50, -5), 9);
        Assert.False(rect.Inside(-1, 30));
        // Either side of the corner's diagonal the nearest edge changes: a mitre.
        rect.Inset(5, 4, out int top, out _);
        rect.Inset(4, 5, out int left, out _);
        Assert.Equal("Top", rect.Runs[top].Name);
        Assert.Equal("Left", rect.Runs[left].Name);
        // Along the top it's measured from the left; along the right from the top.
        rect.Inset(30, 2, out _, out double along);
        Assert.Equal(30, along, 9);
        rect.Inset(98, 20, out int right, out along);
        Assert.Equal("Right", rect.Runs[right].Name);
        Assert.Equal(20, along, 9);

        var round = SignOutline.For(new SignSpec { Shape = SignShape.Rounded, WidthMm = 100, HeightMm = 60, CornerMm = 20 });
        Assert.Single(round.Runs);
        Assert.True(round.Runs[0].Loop);
        double perimeter = 2 * (100 - 40) + 2 * (60 - 40) + 2 * Math.PI * 20;
        Assert.Equal(perimeter, round.Runs[0].Length, 6);
        // In the corner: the distance in from the corner's arc.
        double d = Math.Sqrt(2) * 20 - Math.Sqrt(2) * 6;
        Assert.Equal(20 - d, round.Inset(6, 6), 6);
        // Measured from the top in the middle.
        round.Inset(60, 3, out _, out along);
        Assert.Equal(10, along, 6);

        var circle = SignOutline.For(new SignSpec { Shape = SignShape.Circle, WidthMm = 80 });
        Assert.Equal(80, circle.H);
        Assert.Equal(40 - 25, circle.Inset(40 + 15, 40 + 20), 6);
        Assert.Equal(2 * Math.PI * 40, circle.Runs[0].Length, 6);
    }

    [Fact]
    public void AnOvalIsFourArcsMeetingSmoothly()
    {
        foreach (var (w, h) in new[] { (210.0, 130.0), (120.0, 200.0) })
        {
            var o = SignOutline.For(new SignSpec { Shape = SignShape.Oval, WidthMm = w, HeightMm = h });
            Assert.Single(o.Runs);
            // Close to a true ellipse's perimeter (Ramanujan).
            double a = w / 2, b = h / 2;
            double ellipse = Math.PI * (3 * (a + b) - Math.Sqrt((3 * a + b) * (a + 3 * b)));
            Assert.InRange(o.Runs[0].Length, ellipse * 0.98, ellipse * 1.02);
            // It touches all four sides of its box, and the inset changes smoothly all the way round.
            Assert.Equal(1, o.Inset(w / 2, 1), 6);
            Assert.Equal(1, o.Inset(w / 2, h - 1), 6);
            Assert.Equal(1, o.Inset(1, h / 2), 6);
            Assert.Equal(1, o.Inset(w - 1, h / 2), 6);
            double last = double.NaN;
            for (double t = 0; t <= 2 * Math.PI; t += 0.002)
            {
                double inset = o.Inset(w / 2 + 0.8 * a * Math.Cos(t), h / 2 + 0.8 * b * Math.Sin(t));
                Assert.True(inset > 0);
                if (!double.IsNaN(last)) Assert.True(Math.Abs(inset - last) < 0.2, $"jump at {t}");
                last = inset;
            }
        }
    }

    [Fact]
    public void AnArchedSignRisesToTheTopInTheMiddle()
    {
        var spec = new SignSpec { Shape = SignShape.Arched, WidthMm = 200, HeightMm = 120, ArchRiseMm = 40, CornerMm = 12 };
        var o = SignOutline.For(spec);
        Assert.Equal(2, o.Runs.Count);
        Assert.Equal(3, o.Inset(100, 3), 6);
        Assert.Equal(3, o.Inset(3, 80), 6);
        Assert.Equal(3, o.Inset(100, 117), 6);
        // The top corners are cut away by the arch.
        Assert.False(o.Inside(2, 2));
        Assert.True(o.Inside(100, 1));
    }

    [Fact]
    public void TheWordsDistanceIsSignedAndCapped()
    {
        var square = new InkField([[new(0, 0), new(10, 0), new(10, 10), new(0, 10)]], 3);
        Assert.Equal(3, square.At(5, 5), 9);
        Assert.Equal(1, square.At(1, 5), 9);
        Assert.Equal(-2, square.At(12, 5), 9);
        Assert.Equal(-3, square.At(50, 50), 9);
        // A hole the other way round is outside.
        var ring = new InkField([[new(0, 0), new(10, 0), new(10, 10), new(0, 10)], [new(3, 3), new(3, 7), new(7, 7), new(7, 3)]], 3);
        Assert.True(ring.At(5, 5) < 0);
        Assert.True(ring.At(1.5, 5) > 0);
        // A whole window agrees with point by point.
        var w = ring.Fill(-1, -1, 0.5, 26, 26);
        for (int r = 0; r < 26; r++)
            for (int c = 0; c < 26; c++)
                Assert.Equal(ring.At(-1 + (c + 0.5) * 0.5, -1 + (r + 0.5) * 0.5), w[r * 26 + c], 4);
    }

    [Fact]
    public void VCarvedLettersGoNoDeeperThanAskedAndLeaveTheBoardAlone()
    {
        var plan = new SignPlan(Plain(LetterStyle.VCarved));
        var (lo, hi) = DepthRange(plan);
        Assert.Equal(0, lo, 6);
        Assert.InRange(hi, 0.9, 1 + 1e-6);
        Assert.Equal(1, plan.Top, 6);
        // Far from the words the board is the surface.
        Assert.Equal(plan.Top, plan.HeightAt(3, 3), 6);
        // The depth grows at the V's slope in from a letter's edge: tan 60° ≈ 1.73 mm per mm.
        var o = plan.Words.Outlines[0];
        var box = Rect2.Bounds(o);
        double y = box.Center.Y, x = box.MinX;
        while (!(plan.HeightAt(x, y) < plan.Top - 1e-6) && x < box.MaxX) x += 0.01;
        double d1 = plan.Top - plan.HeightAt(x + 0.2, y), d2 = plan.Top - plan.HeightAt(x + 0.3, y);
        Assert.Equal(1 / Math.Tan(Math.PI / 6), (d2 - d1) / 0.1, 1);
    }

    [Fact]
    public void RaisedLettersStandUpFromACarvedPanel()
    {
        foreach (var style in new[] { LetterStyle.Raised, LetterStyle.Domed, LetterStyle.Chiselled })
        {
            var plan = new SignPlan(Plain(style) with { Texture = Background.Smooth });
            // Between the letters the panel is carved to the depth; the letters reach the surface.
            Assert.Equal(1, plan.Top - plan.HeightAt(plan.Words.Box.MinX - 1, plan.Words.Box.Center.Y), 6);
            var (lo, hi) = DepthRange(plan);
            Assert.Equal(1, hi, 6);
            if (style != LetterStyle.Chiselled) Assert.Equal(0, lo, 6);
            else Assert.True(lo < 0.5);
        }
        // Textures stand up from the floor, never above the surface.
        foreach (var texture in new[] { Background.Hammered, Background.Stippled, Background.Lines })
        {
            var plan = new SignPlan(Plain(LetterStyle.Raised) with { Texture = texture, TextureMm = 0.3 });
            for (double x = 3; x < 20; x += 0.13)
            {
                double d = plan.Top - plan.HeightAt(x, 30);
                Assert.InRange(d, 0.7 - 1e-6, 1 + 1e-6);
            }
        }
    }

    [Fact]
    public void SunkLettersHaveAFlatFloor()
    {
        foreach (var edge in new[] { LetterEdge.Square, LetterEdge.Chamfer, LetterEdge.Rounded })
        {
            var plan = new SignPlan(Plain(LetterStyle.Sunk) with { Edge = edge, EdgeMm = 0.5 });
            var (lo, hi) = DepthRange(plan);
            Assert.Equal(0, lo, 6);
            Assert.Equal(1, hi, 6);
        }
    }

    [Fact]
    public void TheWordsGrowToFillTheSpaceOrStayTheSizeAsked()
    {
        var spec = Plain(LetterStyle.VCarved, "Hello");
        var fitted = new SignPlan(spec);
        Assert.True(fitted.Words.Fits);
        // Fitted: something touches the space kept round the words.
        double nearest = fitted.Words.Outlines.SelectMany(o => o).Min(q => fitted.Outline.Inset(q.X, q.Y));
        Assert.Equal(spec.MarginMm, nearest, 1);

        var asked = new SignPlan(spec with { FitWords = false, LetterHeightMm = 12 });
        Assert.Equal(12, asked.Words.LetterHeightMm, 6);
        Assert.True(asked.Words.Fits);
        var huge = new SignPlan(spec with { FitWords = false, LetterHeightMm = 80 });
        Assert.False(huge.Words.Fits);

        // A second line at half size is half as tall; moving the words down moves them.
        var two = new SignPlan(spec with { Lines = [new("Hello"), new("HELLO", 50)] });
        Assert.Equal(two.Words.LineHeights[0] / 2, two.Words.LineHeights[1], 6);
        var down = new SignPlan(spec with { FitWords = false, LetterHeightMm = 10, OffsetYMm = 5 });
        var mid = new SignPlan(spec with { FitWords = false, LetterHeightMm = 10 });
        Assert.Equal(mid.Words.Box.Center.Y + 5, down.Words.Box.Center.Y, 6);
    }

    [Fact]
    public void MotifsAndARuleJoinTheWords()
    {
        var spec = Plain(LetterStyle.VCarved, "Hello") with { Lines = [new("Hello"), new("there", 60)] };
        int words = new SignPlan(spec).Words.Outlines.Count;
        foreach (Decoration d in Enum.GetValues<Decoration>().Where(d => d != Decoration.None))
        {
            var plan = new SignPlan(spec with { Decoration = d });
            Assert.Equal(words + 2, plan.Words.Outlines.Count);
            Assert.True(plan.Words.Fits, d.ToString());
        }
        Assert.Equal(words + 1, new SignPlan(spec with { Rule = true }).Words.Outlines.Count);
    }

    [Fact]
    public void ScrewHolesAreSeeThroughCountersunkAndCut()
    {
        foreach (SignHoles holes in Enum.GetValues<SignHoles>())
        {
            var spec = Preset("House name") with { Holes = holes, HoleMm = 4, Countersink = true };
            var plan = new SignPlan(spec);
            int want = holes switch { SignHoles.None => 0, SignHoles.TopMiddle => 1, SignHoles.FourCorners => 4, _ => 2 };
            Assert.Equal(want, plan.Holes.Count);
            Assert.Equal(want + 1, plan.CutLines().Count);
            foreach (var h in plan.Holes)
            {
                Assert.True(double.IsNaN(plan.HeightAt(h.X, h.Y)));
                // The countersink: 1 mm out from the hole's edge it's 1 mm deeper than the panel round it.
                double panel = plan.HeightAt(h.X + spec.CountersinkMm / 2 + 0.5, h.Y);
                Assert.Equal(panel - 1, plan.HeightAt(h.X + spec.HoleMm / 2 + 1, h.Y), 6);
                // Inside the border, clear of the words.
                Assert.True(plan.Outline.Inset(h.X, h.Y) >= spec.BorderWidth + spec.CountersinkMm / 2);
                foreach (var q in plan.Words.Outlines.SelectMany(o => o))
                    Assert.True(q.Dist(h) >= spec.CountersinkMm / 2);
            }
        }
        // Round raised letters the countersink starts from the carved panel.
        var raised = new SignPlan(Preset("House number"));
        Assert.Equal(raised.Spec.LetterDepthMm + raised.CountersinkDepth, raised.Top, 6);
    }

    [Fact]
    public void TheBorderFollowsTheEdgeWithWholeRepeats()
    {
        foreach (var name in new[] { "House name", "Memorial plaque", "Workshop", "Arched house sign", "Pet name" })
        {
            var plan = new SignPlan(Preset(name));
            Assert.NotNull(plan.Border);
            var runs = plan.Outline.Runs.ToDictionary(r => r.Name);
            foreach (var (run, _, repeats, pitch) in plan.Repeats())
                Assert.Equal(runs[run].Length, repeats * pitch, 6);
            // Across the border the heights stay between the surface and its depth.
            double bw = plan.Border!.WidthMm;
            for (double t = 0.05; t < bw; t += bw / 23)
            {
                double h = plan.HeightAt(plan.SizeW / 2, t);
                Assert.InRange(plan.Top - h, -1e-6, plan.Border.DepthMm + 1e-6);
            }
        }
        // The panel can sit level with the border's inner edge.
        var sunk = new SignPlan(Preset("Memorial plaque") with { Panel = PanelLevel.BorderInnerEdge });
        Assert.True(sunk.PanelDepth > 0);
        Assert.Equal(sunk.PanelDepth, sunk.Top - sunk.HeightAt(30, sunk.SizeH / 2), 6);
    }

    [Fact]
    public void TheDepthMapIsSeeThroughOutsideTheSign()
    {
        var plan = new SignPlan(Preset("House name") with { PixelsPerMm = 4 });
        var map = SignOutput.DepthMap(plan);
        Assert.Equal(840, map.Width);
        Assert.Equal(520, map.Height);
        Assert.Equal(0, map.Rgba[3]);                                   // the corner, outside the oval
        int mid = (260 * 840 + 20) * 4;                                 // just inside the rope, on the panel
        Assert.Equal(255, map.Rgba[mid + 3]);
        int dark = 255;
        for (int i = 0; i < map.Width * map.Height; i++)
            if (map.Rgba[i * 4 + 3] != 0) dark = Math.Min(dark, map.Rgba[i * 4]);
        Assert.True(dark < 10);
        // Too many pixels for one picture: fewer per mm.
        Assert.True(SignOutput.PixelsPerMm(new SignPlan(Preset("House name") with { WidthMm = 1500, HeightMm = 1000, PixelsPerMm = 20 })) < 20);
    }

    [Fact]
    public void TheSettingsSayWhatsWrong()
    {
        Assert.NotNull((Preset("House name") with { WidthMm = 10 }).Problem());
        Assert.NotNull((Preset("House name") with { HeightMm = 30, BorderWidthMm = 14 }).Problem());
        Assert.NotNull((Preset("House name") with { LetterDepthMm = 0 }).Problem());
        Assert.NotNull((Preset("House name") with { VAngleDeg = 5 }).Problem());
        Assert.Null(Preset("House name").Problem());
        // A circle is as tall as it is wide, whatever height is set.
        Assert.Equal((90.0, 90.0), (new SignSpec { Shape = SignShape.Circle, WidthMm = 90, HeightMm = 10 }).Size());
    }

    [Fact]
    public void ASignSavesAndReadsBack()
    {
        var spec = Preset("Pet name") with { Holes = SignHoles.Sides, Panel = PanelLevel.BorderInnerEdge };
        var back = SignSpec.FromJson(spec.ToJson());
        Assert.NotNull(back);
        Assert.Equal(spec.ToJson(), back!.ToJson());
        Assert.Equal("Ivy", back.Border!.Name);
        Assert.Equal(spec.Lines.Count, back.Lines.Count);
        Assert.Equal(-40, back.Lines[1].BendDeg);
        Assert.Null(SignSpec.FromJson("not json"));
        Assert.Null(SignSpec.FromJson(""));
    }

    [Fact]
    public void PutInTheDesignMakesADepthSlicesLayerAndCanBeFoundAgain()
    {
        var spec = Preset("Workshop") with { PixelsPerMm = 2 };
        var plan = new SignPlan(spec);
        var map = SignOutput.DepthMap(plan);
        var (state, ids, layerId) = SignOutput.Add(DocState.New(), plan, map, MachineProfile.F2Ultra);
        var layer = state.Layers.Single(l => l.Id == layerId);
        Assert.Equal(LayerMode.Image, layer.Mode);
        Assert.Equal(ImageEngraveMode.Slices, layer.ImageMode);
        Assert.StartsWith("Sign · WORKSHOP", layer.Name);
        var picture = Assert.Single(state.Shapes.OfType<ImageShape>());
        Assert.Equal(plan.SizeW, picture.SizeMm.X, 6);
        // The outline and four screw holes on the Cut layer, in one shape.
        var cut = Assert.Single(state.Shapes.OfType<PathShape>());
        Assert.Equal(LayerMode.Cut, state.Layers.Single(l => l.Id == cut.LayerId).Mode);
        Assert.Equal(5, cut.Geometry.Subpaths.Length);
        Assert.Equal(2, ids.Count);

        var found = SignOutput.Find(state, new HashSet<Guid> { picture.Id });
        Assert.NotNull(found);
        Assert.Equal(spec.ToJson(), found!.Value.Spec.ToJson());
        Assert.Equal(2, found.Value.Ids.Count);
        Assert.Null(SignOutput.Find(DocState.New(), new HashSet<Guid>()));

        // Made again where it was, on the same layer.
        var again = Ops.Delete(state, found.Value.Ids);
        var (next, ids2, layer2) = SignOutput.Add(again, plan, map, MachineProfile.F2Ultra, found.Value.TopLeft, again.Layers.Single(l => l.Id == layerId));
        Assert.Equal(layerId, layer2);
        var b = next.Shapes.Single(s => ids2.Contains(s.Id) && s is ImageShape).Bounds;
        Assert.Equal(found.Value.TopLeft.X, b.MinX, 6);
        Assert.Equal(found.Value.TopLeft.Y, b.MinY, 6);
    }
}
