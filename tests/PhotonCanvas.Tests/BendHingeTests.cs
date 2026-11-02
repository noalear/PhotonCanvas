using PhotonCanvas.Addon.BendHinge;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Help;

namespace PhotonCanvas.Tests;

/// <summary>Bend to shape: a living hinge slit only where, and only as closely as, the curve needs.</summary>
public class BendHingeTests
{
    static readonly BendHingeSettings Defaults = new();

    static BendHingeResult Make(PathGeometry curve, BendHingeSettings? s = null)
    {
        var p = BendHinge.Profile(curve);
        Assert.NotNull(p);
        return BendHinge.Make(p, s ?? Defaults);
    }

    static double Spacing(BendHingeResult r) =>
        r.Runs.Where(x => x.Count > 1).SelectMany(x => x.Zip(x.Skip(1), (a, b) => b - a)).DefaultIfEmpty(0).Average();

    [Fact]
    public void TheDefaultsMakeAHinge() => Assert.Null(Defaults.Problem());

    [Fact]
    public void AStraightLineStaysSolid()
    {
        var r = Make(PathGeometry.Line(new(0, 0), new(100, 0)));
        Assert.Equal(100, r.Length, 1);
        Assert.Empty(r.Lines);
        Assert.Single(r.Bands);
        Assert.Equal(BandKind.Solid, r.Bands[0].Kind);
        Assert.True(r.Slits.IsEmpty);
    }

    [Fact]
    public void AGentleArcIsLeftForTheSheetToTake()
    {
        // 90° of a 1 m radius: far gentler than 3 mm plywood takes by itself.
        double R = 1000;
        var pts = Enumerable.Range(0, 200).Select(i => new Vec2(R * Math.Sin(i / 199.0 * 0.15), R - R * Math.Cos(i / 199.0 * 0.15))).ToList();
        var r = Make(PathGeometry.Polyline(pts, false));
        Assert.True(R > BendHinge.SolidRadius(Defaults));
        Assert.Empty(r.Lines);
    }

    [Fact]
    public void ACircleIsSlitAllRoundAtTheSpacingItsRadiusNeeds()
    {
        var r = Make(PathGeometry.Ellipse(0, 0, 50, 50));
        Assert.True(r.Profile.Closed);
        Assert.Equal(2 * Math.PI * 50, r.Length, 0);
        Assert.True(r.AllReachable);
        Assert.True(r.Lines.Count() > 100);
        double w = Spacing(r);
        // As far apart as a 50 mm radius allows (each strip turning as much as it may), but not further.
        Assert.InRange(BendHinge.RadiusAt(Defaults, w), 45, 50.5);
        Assert.InRange(r.TightestRadius, 49, 51);
    }

    [Fact]
    public void ARoundedRectangleIsSlitOnlyAtItsCorners()
    {
        double radius = 15;
        var r = Make(PathGeometry.RoundRect(0, 0, 120, 80, radius, radius));
        var hinges = r.Bands.Where(b => b.Kind != BandKind.Solid).ToList();
        Assert.Equal(4, hinges.Count);
        Assert.True(r.AllReachable);
        double arc = Math.PI / 2 * radius;
        foreach (var b in hinges)
        {
            // Each corner's quarter circle, give or take half the widest spacing at each end.
            Assert.InRange(b.To - b.From, arc - 1, arc + 2 * Defaults.MaxSpacing + 1);
            Assert.InRange(b.Radius, radius - 1.5, radius + 1);
        }
        // The panel's ends meet in a straight side: both ends are solid.
        Assert.Equal(BandKind.Solid, r.Bands[0].Kind);
        Assert.Equal(BandKind.Solid, r.Bands[^1].Kind);
        // The solid stretches are about the straight sides' lengths.
        double solid = r.Bands.Where(b => b.Kind == BandKind.Solid).Sum(b => b.To - b.From);
        Assert.InRange(solid, 2 * (90 + 50) - 8 * Defaults.MaxSpacing - 2, 2 * (90 + 50) + 2);
    }

    [Fact]
    public void EveryStripTakesNoMoreTurningThanItCan()
    {
        // An S-bend: tight one way, gentle the other.
        var g = new PathGeometry([new Subpath(new(0, 0),
            [Seg.Line(new(30, 0)), Seg.Cubic(new(50, 0), new(50, 25), new(70, 25)), Seg.Cubic(new(120, 25), new(120, 80), new(160, 80)), Seg.Line(new(200, 80))], false)]);
        var r = Make(g);
        Assert.True(r.AllReachable);
        foreach (var run in r.Runs)
            for (int i = 1; i < run.Count; i++)
            {
                double w = run[i] - run[i - 1];
                Assert.InRange(w, Defaults.MinSpacing - 1e-9, Defaults.MaxSpacing + 1e-9);
                Assert.True(r.Profile.TurnBetween(run[i - 1], run[i]) <= BendHinge.StripTurn(Defaults, w) + 1e-6);
            }
        // The tight bend (about 40 mm along) gets closer lines than the gentle one (about 120 mm along).
        double Closest(double from, double to) => r.Runs.SelectMany(x => x.Zip(x.Skip(1), (a, b) => (a, w: b - a))).Where(p => p.a >= from && p.a <= to).Min(p => p.w);
        Assert.True(Closest(35, 50) < Closest(110, 140));
    }

    [Fact]
    public void ASharpCornerIsReportedWithACutLengthThatReachesIt()
    {
        var r = Make(PathGeometry.Rect(0, 0, 80, 50));
        Assert.False(r.AllReachable);
        Assert.Equal(4, r.Bands.Count(b => b.Kind == BandKind.TooTight));
        Assert.True(r.TightestRadius < 2);
        // A sharp corner needs far more than any slit fits in 60 mm.
        Assert.Null(r.SuggestedCutLength);
    }

    [Fact]
    public void ATightCornerSuggestsLongerSlitsAndThoseReachIt()
    {
        // 6 mm corners: tighter than 15 mm slits reach at 1 mm spacing (about 9.7 mm).
        var curve = PathGeometry.RoundRect(0, 0, 80, 50, 6, 6);
        var r = Make(curve);
        Assert.True(r.ReachableRadius > 6);
        Assert.False(r.AllReachable);
        Assert.NotNull(r.SuggestedCutLength);
        var again = Make(curve, Defaults with { CutLength = r.SuggestedCutLength!.Value });
        Assert.True(again.AllReachable, $"{again.ReachableRadius} vs {again.TightestRadius}");
    }

    [Fact]
    public void CalibratingFromABendGivesThatBendBack()
    {
        var m = BendHinge.Calibrate(HingeMaterial.Presets[0], thickness: 3, kerf: 0.15, cutLength: 20, gap: 3, spacing: 1.5, radius: 18);
        var s = new BendHingeSettings { Thickness = 3, Kerf = 0.15, CutLength = 20, Gap = 3, Safety = 1, Material = m };
        Assert.Equal(18, BendHinge.RadiusAt(s, 1.5), 6);
        Assert.Equal(HingeMaterial.Presets[0].BendStrain, m.BendStrain);
    }

    [Fact]
    public void SlitsAlternateAndStayInsideThePanel()
    {
        var r = Make(PathGeometry.Ellipse(0, 0, 40, 30));
        var box = r.Slits.Bounds();
        Assert.True(box.MinY >= -1e-9 && box.MaxY <= Defaults.Height + 1e-9);
        Assert.True(box.MinX >= 0 && box.MaxX <= r.Length);
        var run = r.Runs.First(x => x.Count >= 2);
        double y0(double x) => r.Slits.Subpaths.Where(p => Math.Abs(p.Start.X - x) < 1e-9).Min(p => p.Start.Y);
        // The first line of a run stays inside the edges; the next runs out through the top.
        Assert.Equal(Defaults.Gap / 2, y0(run[0]), 6);
        Assert.Equal(0, y0(run[1]), 6);
    }

    [Fact]
    public void TheFormerSitsHalfTheThicknessInside()
    {
        var former = BendHinge.Former(PathGeometry.Ellipse(100, 100, 50, 50), 3);
        var box = former.Bounds();
        Assert.Equal(97, box.Width, 0.1);
        Assert.True(BendHinge.Former(PathGeometry.Line(new(0, 0), new(10, 0)), 3).IsEmpty);
    }

    [Fact]
    public void LineUpMarksMeetOnTheFormer()
    {
        var curve = PathGeometry.RoundRect(0, 0, 120, 80, 15, 15);
        var r = Make(curve);
        var (panel, former) = BendHinge.LineUpMarks(r);
        Assert.Equal(4, panel.Subpaths.Length);
        // One for each corner, and one where the panel's ends meet.
        Assert.Equal(5, former.Subpaths.Length);
        // Each former mark starts on the former's edge (1.5 mm inside the curve) and points further in.
        var box = new Rect2(0, 0, 120, 80);
        foreach (var m in former.Subpaths)
        {
            var a = m.Start; var b = m.End;
            Assert.True(a.X > 0 && a.X < 120 && a.Y > 0 && a.Y < 80);
            Assert.True(b.Dist(box.Center) < a.Dist(box.Center));
        }
    }

    [Fact]
    public void ALongPanelIsSplitInItsSolidStretches()
    {
        var r = Make(PathGeometry.RoundRect(0, 0, 120, 80, 15, 15));
        Assert.True(r.Length > 300);
        var cuts = BendHinge.SplitPoints(r, 220, out bool through);
        Assert.False(through);
        Assert.Single(cuts);
        var ends = cuts.Prepend(0).Append(r.Length).ToList();
        for (int i = 1; i < ends.Count; i++) Assert.InRange(ends[i] - ends[i - 1], 1, 220);
        // Each cut is in a solid stretch, clear of the slits.
        foreach (var c in cuts)
            Assert.Contains(r.Bands, b => b.Kind == BandKind.Solid && c >= b.From + 3 - 1e-9 && c <= b.To - 3 + 1e-9);
        // The pieces keep every slit between them.
        var (panelMarks, _) = BendHinge.LineUpMarks(r);
        int slits = 0;
        for (int i = 1; i < ends.Count; i++)
        {
            var (outline, s, m) = BendHinge.Piece(r, panelMarks, ends[i - 1], ends[i]);
            Assert.Equal(ends[i] - ends[i - 1], outline.Bounds().Width, 6);
            slits += s.Subpaths.Length;
            if (!s.IsEmpty) Assert.True(s.Bounds().MinX >= 0 && s.Bounds().MaxX <= ends[i] - ends[i - 1]);
        }
        Assert.Equal(r.Slits.Subpaths.Length, slits);
        // A panel that fits isn't cut.
        Assert.Empty(BendHinge.SplitPoints(r, 400, out _));
    }

    [Fact]
    public void ItsHelpPageAndTutorialAreThere()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "PhotonCanvas.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var lib = HelpLibrary.Load(Path.Combine(dir.FullName, "src", "Addons", "BendHinge", "help"));
        var page = Assert.Single(lib.Pages.Values);
        Assert.Contains("BendHingeWindow", page.Windows.Keys);
        var t = Assert.Single(lib.Tutorials);
        Assert.Equal("Drawing", t.Category);
        Assert.True(t.Steps.Count >= 3);
        Assert.Contains(t.Steps, s => s.Target == "ribbon:Home/Bend to shape");
    }

    [Fact]
    public void BadSettingsSayWhatsWrong()
    {
        Assert.NotNull((Defaults with { CutLength = 5, Gap = 3 }).Problem());
        Assert.NotNull((Defaults with { Height = 10 }).Problem());
        Assert.NotNull((Defaults with { MinSpacing = 0.2 }).Problem());
        Assert.NotNull((Defaults with { MaxSpacing = 0.5 }).Problem());
        Assert.NotNull((Defaults with { Safety = 0.5 }).Problem());
    }
}
