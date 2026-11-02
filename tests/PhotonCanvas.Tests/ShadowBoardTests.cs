using PhotonCanvas.Addons.ShadowBoard;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Geometry;
using Xunit;

namespace PhotonCanvas.Tests;

public class ShadowBoardTests
{
    // A long "spanner" lying along X and a round "socket", well apart.
    static readonly ShadowTool Spanner = new("Spanner", PathGeometry.RoundRect(40, 40, 120, 20, 4, 4));
    static readonly ShadowTool Socket = new("Socket", PathGeometry.Ellipse(220, 50, 12, 12));

    static Rect2 FixedMeasure(string text, TextStyle st) { double w = text.Length * st.SizeMm * 0.6; return new(-w / 2, -st.SizeMm / 2, w / 2, st.SizeMm / 2); }

    [Fact]
    public void TheAxisOfALongShapeRunsAlongItsLongSide()
    {
        var (c, a) = ShadowBoardMaker.Axis(PathGeometry.Rect(0, 0, 100, 10));
        Assert.Equal(50, c.X, 3); Assert.Equal(5, c.Y, 3);
        Assert.Equal(0, Math.Abs(a), 1);
        var turned = PathGeometry.Rect(0, 0, 100, 10).Transform(Affine.RotateAbout(new(50, 5), 30));
        Assert.Equal(30, ShadowBoardMaker.Axis(turned).AngleDeg, 1);
    }

    [Fact]
    public void PocketsAreTheGapBiggerWithANotchEachSideAcrossTheMiddle()
    {
        var o = new ShadowBoardOptions { GapMm = 1.5, NotchDiameterMm = 20, Notches = NotchPlacement.BothSides, Labels = false, MarginMm = 10 };
        var plan = ShadowBoardMaker.Plan([Spanner], o, FixedMeasure);
        var b = plan.Pockets.Bounds(0.05);
        // Along the spanner: just the gap. Across it: the notches stick out half their size past the pocket's edge.
        Assert.Equal(123, b.Width, 1);
        Assert.Equal(20 + 3 + 20, b.Height, 1);
        Assert.Equal(100, b.Center.X, 1);
        // Without notches it's just the gap bigger.
        var plain = ShadowBoardMaker.Plan([Spanner], o with { Notches = NotchPlacement.None }, FixedMeasure).Pockets.Bounds(0.05);
        Assert.Equal((123.0, 23.0), (Math.Round(plain.Width, 1), Math.Round(plain.Height, 1)));
        // One notch goes on the front (larger Y).
        var front = ShadowBoardMaker.Plan([Spanner], o with { Notches = NotchPlacement.Front }, FixedMeasure).Pockets.Bounds(0.05);
        Assert.Equal(38.5, front.MinY, 1);
        Assert.Equal(61.5 + 10, front.MaxY, 1);
        // The board is the margin round it all.
        Assert.Equal(b.Inflate(10).Width, plan.Board.Width, 1);
        Assert.Empty(plan.Warnings);
    }

    [Fact]
    public void LabelsGoUnderTheirPocketsWhenThereIsRoom()
    {
        var o = new ShadowBoardOptions { Notches = NotchPlacement.None, LabelSizeMm = 5 };
        var plan = ShadowBoardMaker.Plan([Spanner, Socket], o, FixedMeasure);
        Assert.Equal(2, plan.Labels.Count);
        Assert.All(plan.Labels, l => Assert.True(l.Fits));
        var spannerPocket = Spanner.Outline.Bounds().Inflate(o.GapMm);
        Assert.True(plan.Labels[0].Box.MinY > spannerPocket.MaxY);
        Assert.Equal(100, plan.Labels[0].Centre.X, 1);
        // Labels are inside the board.
        Assert.All(plan.Labels, l => Assert.True(plan.Board.Contains(l.Box)));
    }

    [Fact]
    public void ABoardTooSmallOrPocketsThatMeetAreWarnedAbout()
    {
        var o = new ShadowBoardOptions { BoardWidthMm = 100, BoardHeightMm = 50, Labels = false };
        var plan = ShadowBoardMaker.Plan([Spanner], o, FixedMeasure);
        Assert.Equal(100, plan.Board.Width, 3);
        Assert.Contains(plan.Warnings, w => w.Contains("the board is 100 × 50"));
        var touching = new ShadowTool("B", PathGeometry.Rect(161, 40, 20, 20));
        Assert.Contains(ShadowBoardMaker.Plan([Spanner, touching], o with { BoardWidthMm = 0 }, FixedMeasure).Warnings, w => w.Contains("run into each other"));
    }

    [Fact]
    public void StackedSheetsMakeTopMiddleAndBasePagesInTheSamePlace()
    {
        var s = DocState.New();
        var o = new ShadowBoardOptions { DepthMm = 8, SheetMm = 3, PinDiameterMm = 3 };
        Assert.Equal(3, ShadowBoardMaker.SheetsFor(o)); // 8 mm in 3 mm sheets: rounded up
        var r = ShadowBoardMaker.Make(s, [Spanner, Socket], o, FixedMeasure);
        var names = Pages.Names(r);
        Assert.Equal(["Page 1", ShadowBoardMaker.TopPage, ShadowBoardMaker.MiddlePage + " (cut 2)", ShadowBoardMaker.BasePage], names);
        Assert.Equal(ShadowBoardMaker.TopPage, r.PageName); // the top sheet is shown
        // Top: pockets + board (with 4 dowel holes) cut, labels engraved.
        var cut = r.Layers.Single(l => l.Name == ShadowBoardMaker.CutLayer);
        Assert.Equal(LayerMode.Cut, cut.Mode);
        var board = r.Shapes.OfType<PathShape>().Single(x => x.Name == "Board");
        Assert.Equal(5, board.Geometry.Subpaths.Length);
        Assert.Equal(2, r.Shapes.OfType<TextShape>().Count());
        Assert.Equal(LayerMode.Fill, r.Layers.Single(l => l.Name == ShadowBoardMaker.LabelLayer).Mode);
        // Base: solid (no pockets), with the tools' shadows engraved; the board in exactly the same place.
        var basePage = Pages.SwitchTo(r, 3);
        Assert.DoesNotContain(basePage.Shapes, x => x.Name == "Tool pockets");
        var shadows = basePage.Shapes.OfType<PathShape>().Single(x => x.Name == "Tool shadows");
        Assert.Equal(2, shadows.Geometry.Subpaths.Length);
        Assert.Equal(board.Bounds, basePage.Shapes.Single(x => x.Name == "Board").Bounds);
        // Only one cut sheet: no middle page.
        var one = ShadowBoardMaker.Make(s, [Spanner], o with { DepthMm = 3 }, FixedMeasure);
        Assert.Equal(3, Pages.Names(one).Count);
    }

    [Fact]
    public void EngravedPocketsAreOnePageWithPassesForTheDepth()
    {
        var o = new ShadowBoardOptions { Style = ShadowBoardStyle.EngravedPockets, DepthMm = 3, EtchMmPerPass = 0.2, PinDiameterMm = 3 };
        Assert.Equal(15, ShadowBoardMaker.PassesFor(o));
        var r = ShadowBoardMaker.Make(DocState.New(), [Spanner], o, FixedMeasure);
        Assert.Equal(2, Pages.Names(r).Count);
        var pockets = r.Layers.Single(l => l.Name == ShadowBoardMaker.PocketLayer);
        Assert.Equal((LayerMode.Fill, 15), (pockets.Mode, pockets.Laser.Passes));
        Assert.NotEqual(0, pockets.AngleStepDeg);
        // No dowel holes on a single board: just its outline.
        Assert.Single(r.Shapes.OfType<PathShape>().Single(x => x.Name == "Board").Geometry.Subpaths);
        // Not cutting the outline: no board shape at all.
        var noCut = ShadowBoardMaker.Make(DocState.New(), [Spanner], o with { CutOutline = false }, FixedMeasure);
        Assert.DoesNotContain(noCut.Shapes, x => x.Name == "Board");
    }

    [Fact]
    public void ToolsFromAMaskAreTheBlobsOutlinesWithHolesFilledInReadingOrder()
    {
        // 200 × 100 px at 0.5 mm/px: a ring (a hole in it) at the right, a bar at the left.
        int w = 200, h = 100;
        var keep = new byte[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                double dx = x - 150, dy = y - 50, d = Math.Sqrt(dx * dx + dy * dy);
                bool ring = d < 30 && d > 15, isBar = x is >= 20 and < 80 && y is >= 40 and < 60;
                keep[y * w + x] = ring || isBar ? (byte)255 : (byte)0;
            }
        var tools = ShadowBoardMaker.FromMask(keep, w, h, new Rect2(10, 10, 110, 60), 0.5, minAreaMm2: 20);
        Assert.Equal(2, tools.Count);
        Assert.Equal(["Tool 1", "Tool 2"], tools.Select(t => t.Name));
        var bar = tools[0].Outline.Bounds(0.1);
        Assert.Equal(10 + 20 * 0.5, bar.MinX, 0); // mm on the bed
        Assert.Single(tools[1].Outline.Subpaths); // the ring's hole is filled in
        Assert.InRange(ShapeGeometry.Area(tools[1].Outline), Math.PI * 15 * 15 * 0.95, Math.PI * 15 * 15 * 1.05);
    }

    [Fact]
    public void ToolsFromShapesKeepTheShapesNames()
    {
        var s = DocState.New();
        var a = new PathShape { Name = "Pliers", LayerId = s.Layers[0].Id, Geometry = PathGeometry.Rect(100, 10, 30, 80) };
        var b = new PathShape { Name = "Screwdriver", LayerId = s.Layers[0].Id, Geometry = PathGeometry.Rect(10, 10, 10, 80) };
        var tools = ShadowBoardMaker.FromShapes([a, b]);
        Assert.Equal(["Screwdriver", "Pliers"], tools.Select(t => t.Name));
    }
}
