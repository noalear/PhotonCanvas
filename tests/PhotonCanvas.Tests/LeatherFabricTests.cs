using PhotonCanvas.Addon.LeatherFabric;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using Xunit;

namespace PhotonCanvas.Tests;

public class LeatherFabricTests
{
    static readonly SewnOptions Leather = SewnOptions.For(SewnMaterial.VegTan) with { AllowanceMm = 4, PitchMm = 4, Holes = HoleStyle.Round, HoleSizeMm = 1 };

    /// <summary>Centres of the round holes (each is one closed subpath).</summary>
    static List<Vec2> Centres(PathGeometry holes) => [.. holes.Subpaths.Select(sp =>
    {
        var b = new PathGeometry([sp]).Bounds();
        return b.Center;
    })];

    static bool Near(Vec2 a, Vec2 b, double tol = 0.05) => a.Dist(b) <= tol;

    [Fact]
    public void ARectangleGetsAHoleInEveryCornerAndEvenStitchesBetween()
    {
        var piece = SewnPattern.Make(PathGeometry.Rect(10, 10, 40, 20), Leather);
        Assert.Null(piece.Problem);
        // Stitch line 4 mm in: 32 × 12 mm, so 8 + 3 + 8 + 3 stitches of exactly 4 mm.
        var b = piece.Stitch.Bounds();
        Assert.Equal(14, b.MinX, 2);
        Assert.Equal(46, b.MaxX, 2);
        Assert.Equal(26, b.MaxY, 2);
        Assert.Equal(22, piece.HoleCount);
        var c = Centres(piece.Holes);
        Assert.Equal(22, c.Count);
        foreach (var corner in new[] { new Vec2(14, 14), new Vec2(46, 14), new Vec2(46, 26), new Vec2(14, 26) })
            Assert.Contains(c, p => Near(p, corner));
        // Along the top: every 4 mm.
        var top = c.Where(p => Math.Abs(p.Y - 14) < 0.05).Select(p => p.X).Order().ToList();
        Assert.Equal(9, top.Count);
        for (int i = 1; i < top.Count; i++) Assert.Equal(4, top[i] - top[i - 1], 2);
        // The cut edge is the outline drawn.
        Assert.Equal(new Rect2(10, 10, 50, 30), piece.Cut.Bounds());
    }

    [Fact]
    public void ACircleIsStitchedEvenlyAllRound()
    {
        var piece = SewnPattern.Make(PathGeometry.Ellipse(50, 50, 20, 20), Leather with { AllowanceMm = 3 });
        var c = Centres(piece.Holes);
        int expected = (int)Math.Round(2 * Math.PI * 17 / 4);
        Assert.Equal(expected, c.Count);
        foreach (var p in c) Assert.Equal(17, p.Dist(new Vec2(50, 50)), 1);
        var gaps = c.Select((p, i) => p.Dist(c[(i + 1) % c.Count])).ToList();
        Assert.True(gaps.Max() - gaps.Min() < 0.1, $"gaps {gaps.Min():0.###}–{gaps.Max():0.###}");
    }

    [Fact]
    public void ASewingLineGetsItsCutEdgeOutsideIt()
    {
        var o = SewnOptions.For(SewnMaterial.Cotton);
        Assert.Equal(DrawnLine.SewingLine, o.Drawn);
        Assert.Equal(HoleStyle.None, o.Holes);
        var piece = SewnPattern.Make(PathGeometry.Rect(20, 20, 40, 20), o);
        var b = piece.Cut.Bounds();
        Assert.Equal(10, b.MinX, 2);
        Assert.Equal(70, b.MaxX, 2);
        Assert.Equal(10, b.MinY, 2);
        Assert.Equal(50, b.MaxY, 2);
        Assert.Equal(new Rect2(20, 20, 60, 40), piece.Stitch.Bounds());
        Assert.Equal(0, piece.HoleCount);
    }

    [Fact]
    public void NotchesSitInTheMiddleOfEachSidePointingIn()
    {
        var piece = SewnPattern.Make(PathGeometry.Rect(20, 20, 40, 20), SewnOptions.For(SewnMaterial.Cotton));
        var cut = piece.Cut.Bounds();
        Assert.Equal(4, piece.Notches.Subpaths.Length);
        foreach (var n in piece.Notches.Subpaths)
        {
            Assert.False(n.Closed);
            Assert.Equal(3, n.Start.Dist(n.End), 2);
            // Starts on the cut edge, ends inside it, in the middle of a side.
            Assert.True(Math.Abs(n.Start.X - cut.MinX) < 0.01 || Math.Abs(n.Start.X - cut.MaxX) < 0.01 || Math.Abs(n.Start.Y - cut.MinY) < 0.01 || Math.Abs(n.Start.Y - cut.MaxY) < 0.01);
            Assert.True(cut.Inflate(-0.5).Contains(n.End), $"notch end {n.End} is not inside");
            Assert.True(Math.Abs(n.Start.X - cut.Center.X) < 0.01 || Math.Abs(n.Start.Y - cut.Center.Y) < 0.01);
        }
    }

    [Fact]
    public void APieceTooNarrowForTheAllowanceSaysSo()
    {
        var piece = SewnPattern.Make(PathGeometry.Rect(0, 0, 40, 5), Leather);
        Assert.NotNull(piece.Problem);
        Assert.Equal(0, piece.HoleCount);
        Assert.False(piece.Cut.IsEmpty);
    }

    [Fact]
    public void HolesThatReachTheEdgeAreReported()
    {
        var piece = SewnPattern.Make(PathGeometry.Rect(0, 0, 60, 40), Leather with { AllowanceMm = 1, HoleSizeMm = 3 });
        Assert.Contains("cut edge", piece.Problem);
    }

    [Fact]
    public void AnOpenLineIsStitchedEndToEndAndNothingIsCut()
    {
        var piece = SewnPattern.Make(PathGeometry.Line(new Vec2(0, 0), new Vec2(20, 0)), Leather);
        Assert.True(piece.Cut.IsEmpty);
        var c = Centres(piece.Holes).Select(p => p.X).Order().ToList();
        Assert.Equal([0, 4, 8, 12, 16, 20], c.Select(x => Math.Round(x, 3)));
    }

    [Fact]
    public void SlotsAndDiamondsLean45DegreesFromTheStitchLine()
    {
        // Slot: its first side runs along its length; diamond: from its first point to its third.
        var slot = SewnPattern.Hole(new Vec2(0, 0), new Vec2(1, 0), HoleStyle.Slots, 2).Subpaths[0];
        var diamond = SewnPattern.Hole(new Vec2(0, 0), new Vec2(1, 0), HoleStyle.Diamonds, 2).Subpaths[0];
        foreach (var d in new[] { slot.Segs[0].To - slot.Start, diamond.Segs[1].To - diamond.Start })
        {
            Assert.Equal(2, d.Length, 3);
            Assert.Equal(45, Math.Atan2(d.Y, d.X) * 180 / Math.PI, 3);
        }
        Assert.True(slot.Closed && diamond.Closed);
    }

    [Fact]
    public void CutOutsAreStitchedOnlyWhenAsked()
    {
        var piece = new PathGeometry([.. PathGeometry.Rect(0, 0, 80, 50).Subpaths, .. PathGeometry.Rect(30, 20, 20, 10).Subpaths]);
        int outer = SewnPattern.Make(piece, Leather).HoleCount;
        int both = SewnPattern.Make(piece, Leather with { StitchInside = true }).HoleCount;
        Assert.True(both > outer, $"{both} vs {outer}");
        // Round the window, 4 mm out from it: 28 × 18 mm = 7 + 5 + 7 + 5 stitches (about).
        Assert.InRange(both - outer, 22, 26);
    }

    static (DocState S, PathShape Rect) Design()
    {
        var s = DocState.New();
        var rect = new PathShape { Name = "Pocket", LayerId = s.Layers[0].Id, Geometry = PathGeometry.Rect(10, 10, 60, 40) };
        return (Ops.AddShapes(s, [rect]), rect);
    }

    static readonly Rect2 Bed = new(0, 0, 220, 220);

    [Fact]
    public void MakingPiecesKeepsTheDrawingAsAPatternAndCutsHolesFirst()
    {
        var (s, rect) = Design();
        var r = SewnBuild.Apply(s, [rect.Id], Leather, Bed);
        Assert.Empty(r.Problems);
        var layers = SewnBuild.LayersOf(r.State);
        var master = r.State.Shapes.Single(x => x.Id == rect.Id);
        Assert.Equal(layers.Pattern, master.LayerId);
        Assert.False(r.State.FindLayer(layers.Pattern!.Value)!.Output);
        Assert.NotNull(SewnBuild.OptionsOf(master));
        Assert.True(r.State.LayerIndex(layers.Holes!.Value) < r.State.LayerIndex(layers.Cut!.Value));
        var cut = r.State.Shapes.Where(x => x.LayerId == layers.Cut).OfType<PathShape>().Single();
        Assert.Equal(rect.World.Bounds(), cut.World.Bounds());
        Assert.Single(r.State.Shapes, x => x.LayerId == layers.Holes);
        // All of one piece moves together.
        Assert.Single(r.Made.Select(id => r.State.Shapes.Single(x => x.Id == id).GroupId).Distinct());
        Assert.Empty(SewnBuild.Check(r.State));
    }

    [Fact]
    public void MakingAgainReplacesWhatWasMadeBefore()
    {
        var (s, rect) = Design();
        var first = SewnBuild.Apply(s, [rect.Id], Leather, Bed);
        // Start from one of the made shapes: it leads back to the drawing.
        var made = first.State.Shapes.First(x => x.Id != rect.Id && first.Made.Contains(x.Id));
        var second = SewnBuild.Apply(first.State, [made.Id], Leather with { PitchMm = 8 }, Bed);
        Assert.Equal(first.State.Shapes.Count, second.State.Shapes.Count);
        Assert.Equal(first.State.Layers.Count, second.State.Layers.Count);
        Assert.True(second.Holes < first.Holes);
        Assert.Equal(8, SewnBuild.OptionsOf(second.State.Shapes.Single(x => x.Id == rect.Id))!.PitchMm);
    }

    [Fact]
    public void TheMirrorImageGoesBesideThePiece()
    {
        var (s, rect) = Design();
        var r = SewnBuild.Apply(s, [rect.Id], Leather with { Mirror = true }, Bed);
        Assert.Equal(2, r.Pieces);
        var mirrored = r.State.Shapes.Where(x => x.AddonData.TryGetValue(SewnBuild.RoleKey, out var role) && role.StartsWith("mirror-")).ToList();
        Assert.Equal(4, mirrored.Count); // pattern, cut, stitch line, holes
        foreach (var m in mirrored) Assert.True(m.Bounds.MinX >= 70 + SewnBuild.MirrorGapMm - 0.01, $"{m.Name} at {m.Bounds}");
        Assert.False(r.OffBed);
        // Too wide to go beside it: underneath instead.
        var below = SewnBuild.Apply(s, [rect.Id], Leather with { Mirror = true }, new Rect2(0, 0, 120, 220));
        foreach (var m in below.State.Shapes.Where(x => x.AddonData.TryGetValue(SewnBuild.RoleKey, out var role) && role.StartsWith("mirror-")))
            Assert.True(m.Bounds.MinY >= 50 + SewnBuild.MirrorGapMm - 0.01);
    }

    [Fact]
    public void VinylIsNeverMade()
    {
        var (s, rect) = Design();
        var r = SewnBuild.Apply(s, [rect.Id], SewnOptions.For(SewnMaterial.Vinyl), Bed);
        Assert.Same(s, r.State);
        Assert.Empty(r.Made);
        Assert.True(SewnMaterials.Info(SewnMaterial.Vinyl).Never);
    }

    [Fact]
    public void TheJobChecksWarnAboutChromeLeatherAndHolesAfterTheCut()
    {
        var (s, rect) = Design();
        var r = SewnBuild.Apply(s, [rect.Id], Leather with { Material = SewnMaterial.ChromeTan }, Bed);
        Assert.Contains(SewnBuild.Check(r.State), w => w.Contains("Chrome-tanned"));
        var l = SewnBuild.LayersOf(r.State);
        var swapped = Ops.MoveLayer(r.State, l.Holes!.Value, r.State.Layers.Count - 1);
        Assert.Contains(SewnBuild.Check(swapped), w => w.Contains("Stitch holes run after"));
    }

    [Fact]
    public void OptionsSurviveBeingSaved()
    {
        var o = Leather with { Material = SewnMaterial.WoolFelt, Mirror = true, StitchLine = StitchLineShow.Groove, RecipeId = Guid.NewGuid() };
        Assert.Equal(o, SewnBuild.Load(SewnBuild.Save(o)));
        Assert.Null(SewnBuild.Load("not json"));
        Assert.Equal(50, (SewnBuild.Load("{\"AllowanceMm\": 900}"))!.AllowanceMm);
    }
}
