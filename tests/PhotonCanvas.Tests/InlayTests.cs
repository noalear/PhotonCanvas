using PhotonCanvas.Addon.Inlay;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Geometry;
using Xunit;

namespace PhotonCanvas.Tests;

/// <summary>The Inlay and marquetry add-on's engine: pieces from shapes, kerf and fit drawn in, the pages it makes.</summary>
public class InlayTests
{
    /// <summary>A design with these shapes (bottom first), each on its own new layer unless one is given; all selected.</summary>
    static (DocState State, IReadOnlySet<Guid> Selection, List<Layer> Layers) Design(params (PathGeometry Geo, int Layer)[] shapes)
    {
        var s = DocState.New();
        var layers = new List<Layer>();
        for (int i = 0; i <= shapes.Max(x => x.Layer); i++)
        {
            var l = Layer.Default(i + 1, LayerMode.Fill) with { Name = $"Wood {i + 1}" };
            layers.Add(l);
            s = Ops.AddLayer(s, l);
        }
        var made = shapes.Select((x, i) => new PathShape { Name = $"Shape {i}", LayerId = layers[x.Layer].Id, Geometry = x.Geo }).ToList();
        s = Ops.AddShapes(s, made);
        return (s, made.Select(m => m.Id).ToHashSet(), layers);
    }

    static DocState Page(DocState s, string name) => Pages.SwitchTo(s, Pages.Names(s).IndexOf(name));

    static List<PathShape> On(DocState s, string page, Func<Layer, bool> layer)
    {
        var p = Page(s, page);
        var ids = p.Layers.Where(layer).Select(l => l.Id).ToHashSet();
        return [.. p.Shapes.OfType<PathShape>().Where(x => ids.Contains(x.LayerId) && x is not TextShape)];
    }

    static Rect2 LoopBounds(PathGeometry g, int i) => new PathGeometry([g.Subpaths.Where(sp => sp.Closed).OrderByDescending(sp => new PathGeometry([sp]).Bounds().Width).ElementAt(i)]).Bounds();

    [Fact]
    public void OnePieceIntoAPocket_TheGapIsSharedAndTheKerfDrawnIn()
    {
        var (s, sel, _) = Design((PathGeometry.Rect(40, 40, 20, 20), 0));
        var d = InlayDesign.From(s, sel);
        Assert.Single(d.Pieces);
        var o = new InlayOptions
        {
            Into = InlayInto.Pocket, FitMm = 0.06, ThicknessMm = 0.6, EtchMmPerPass = 0.15,
            PocketSettings = Layer.Default(0, LayerMode.Fill) with { Laser = LaserSettings.Blue(60, 200) },
            Woods = [new WoodCut { Name = "Walnut", KerfMm = 0.12 }],
        };
        Assert.Equal(4, InlayMaker.Passes(o));
        Assert.Equal(5, InlayMaker.Passes(o with { ThicknessMm = 0.65 })); // never short: rounded up
        var r = InlayMaker.Make(s, d, o);
        Assert.Equal(["Inlay · pocket", "Inlay · Walnut"], r.Pages);
        // The design itself is left alone, on the page before.
        Assert.Equal(s.Shapes.Count, Pages.SwitchTo(r.State, 0).Shapes.Count);
        // The pocket: half the gap bigger all round, the passes from the depth, each turned.
        var pocketLayer = Page(r.State, "Inlay · pocket").Layers.Single(l => l.Mode == LayerMode.Fill);
        Assert.Equal((4, 60.0), (pocketLayer.Laser.Passes, pocketLayer.Laser.Power));
        Assert.NotEqual(0, pocketLayer.AngleStepDeg);
        Assert.Equal(20.06, On(r.State, "Inlay · pocket", l => l.Output).Single().Bounds.Width, 3);
        // The piece: half a kerf out (so it comes out the drawn size) less half the gap; the layer's own kerf 0.
        var cutLayer = Page(r.State, "Inlay · Walnut").Layers.Single(l => l.Mode == LayerMode.Cut);
        Assert.Equal(0, cutLayer.KerfMm);
        var piece = On(r.State, "Inlay · Walnut", l => l.Mode == LayerMode.Cut).Single();
        Assert.Equal(20 + 0.12 - 0.06, piece.Bounds.Width, 3);
        Assert.Equal(new Vec2(50, 50).X, piece.Bounds.Center.X, 3); // in place
        // Pocket wall to piece edge, each side: 0.03 + 0.03 = the gap.
        Assert.Equal(0.06, (20.06 - (piece.Bounds.Width - 0.12)) / 2, 3);
    }

    [Fact]
    public void ShapesOnTopCutAwayWhatsUnder_AndEachLayerIsOneWood()
    {
        // A sky (wood 1) with a sun (wood 2) on top, and a second sky rectangle touching the first: one sky piece.
        var (s, sel, layers) = Design(
            (PathGeometry.Rect(0, 0, 40, 30), 0),
            (PathGeometry.Rect(40, 0, 20, 30), 0),
            (PathGeometry.Rect(10, 10, 10, 10), 1));
        var d = InlayDesign.From(s, sel);
        Assert.Equal(2, d.Woods.Count);
        Assert.Equal(layers[0].Id, d.Woods[0].LayerId);
        Assert.Equal(2, d.Pieces.Count);
        var sky = d.Pieces.Single(p => p.Wood == 0);
        Assert.Equal(60 * 30 - 100, ShapeGeometry.Area(sky.Area), 2);
        Assert.Equal(2, sky.Area.Subpaths.Length); // its outline and the sun's hole
        Assert.Equal(new Rect2(0, 0, 60, 30), d.Bounds);

        // Pieces only: the sky's hole comes in by what the sun goes out, so the joint is exactly the gap.
        var o = new InlayOptions
        {
            Into = InlayInto.Nothing, FitMm = 0.04,
            Woods = [new WoodCut { Name = "Maple", KerfMm = 0.1 }, new WoodCut { Name = "Padauk", KerfMm = 0.14 }],
        };
        var r = InlayMaker.Make(s, d, o);
        Assert.Equal(["Inlay · Maple", "Inlay · Padauk"], r.Pages);
        var skyCut = On(r.State, "Inlay · Maple", l => l.Mode == LayerMode.Cut).Single().Geometry;
        var sunCut = On(r.State, "Inlay · Padauk", l => l.Mode == LayerMode.Cut).Single();
        double skyOff = 0.1 / 2 - 0.02, sunOff = 0.14 / 2 - 0.02;
        Assert.Equal(60 + 2 * skyOff, LoopBounds(skyCut, 0).Width, 3);
        Assert.Equal(10 - 2 * skyOff, LoopBounds(skyCut, 1).Width, 3);
        Assert.Equal(10 + 2 * sunOff, sunCut.Bounds.Width, 3);
        // Cut out, each loses half its kerf: the sun is 10 − 0.04 wide, its hole 10 + 0.04.
        double sunOut = sunCut.Bounds.Width - 0.14, holeOut = LoopBounds(skyCut, 1).Width + 0.1;
        Assert.Equal(2 * 0.04, holeOut - sunOut, 3);
    }

    [Fact]
    public void HolesInASheet_EdgeOptional_RingMiddlesAreLoosePieces()
    {
        // A square ring (one piece with a hole): the sheet shows through its middle.
        var ring = ShapeGeometry.Boolean(PathGeometry.Rect(0, 0, 30, 30), PathGeometry.Rect(10, 10, 10, 10), BoolOp.Subtract);
        var (s, sel, _) = Design((ring, 0));
        var d = InlayDesign.From(s, sel);
        var o = new InlayOptions
        {
            Into = InlayInto.Holes, FitMm = 0.06, MarginMm = 5, CutSheetEdge = true,
            Sheet = new WoodCut { Name = "Sycamore", KerfMm = 0.1 },
            Woods = [new WoodCut { Name = "Wenge", KerfMm = 0.1 }],
        };
        var (main, islands) = InlayMaker.Sheet(d, o);
        Assert.Single(islands);
        Assert.Equal(2, islands[0].Number); // after the picture's own pieces
        var r = InlayMaker.Make(s, d, o);
        Assert.Equal(["Inlay · Sycamore", "Inlay · Wenge"], r.Pages);
        var sheet = On(r.State, "Inlay · Sycamore", l => l.Mode == LayerMode.Cut);
        // The sheet: its edge 5 mm out (cut half a kerf outside), the hole half the gap bigger (cut half a kerf inside).
        var cut = sheet.Single(x => x.Name == "Sheet").Geometry;
        Assert.Equal(40 + 0.1 - 0.06, LoopBounds(cut, 0).Width, 3);
        Assert.Equal(30 - 0.1 + 0.06, LoopBounds(cut, 1).Width, 3);
        // The ring's middle comes out as a piece of the sheet's wood, cut where it is.
        var middle = sheet.Single(x => x.Name == "Piece 2");
        Assert.Equal(10 + 0.1 - 0.06, middle.Bounds.Width, 3);
        Assert.Equal(15, middle.Bounds.Center.X, 3);

        // In a bigger board: only the holes.
        var board = InlayMaker.Make(s, d, o with { CutSheetEdge = false });
        var holes = On(board.State, "Inlay · Sycamore", l => l.Mode == LayerMode.Cut).Single(x => x.Name == "Holes").Geometry;
        Assert.Single(holes.Subpaths);
        Assert.Equal(30 - 0.1 + 0.06, holes.Bounds().Width, 3);
    }

    [Fact]
    public void TurnedOverPiecesAreCutMirrored()
    {
        // An L, off to one side of the picture: mirrored about the picture's middle.
        var l = PathGeometry.Polyline([new(0, 0), new(10, 0), new(10, 4), new(4, 4), new(4, 20), new(0, 20)], true);
        var (s, sel, _) = Design((PathGeometry.Rect(0, 0, 40, 20), 0), (l, 1));
        var d = InlayDesign.From(s, sel);
        var o = new InlayOptions { Into = InlayInto.Nothing, FitMm = 0, Flip = true, Woods = [new WoodCut { KerfMm = 0 }, new WoodCut { Name = "Ebony", KerfMm = 0 }] };
        var r = InlayMaker.Make(s, d, o);
        var piece = On(r.State, "Inlay · Ebony", x => x.Mode == LayerMode.Cut).Single();
        Assert.Equal(new Rect2(30, 0, 40, 20), piece.Bounds);
        // Its long leg is now on the right.
        Assert.Contains(piece.Geometry.Flatten(0.01)[0].Points, p => p.X == 40 && p.Y == 20);
    }

    [Fact]
    public void PackedPiecesSitInRowsOnTheBed_AndAKeyShowsWhereTheyGo()
    {
        // Twelve 30 mm squares of one wood, spread out over 600 mm: packed onto a 100 mm wide bed.
        var shapes = Enumerable.Range(0, 12).Select(i => (PathGeometry.Rect(i * 50, 0, 30, 30), 0)).ToArray();
        var (s, sel, _) = Design(shapes);
        var d = InlayDesign.From(s, sel);
        Assert.Equal(12, d.Pieces.Count);
        Assert.Equal(Enumerable.Range(1, 12), d.Pieces.OrderBy(p => p.Bounds.MinX).Select(p => p.Number)); // left to right
        var o = new InlayOptions { Into = InlayInto.Nothing, Pack = true, SpacingMm = 2, Woods = [new WoodCut { Name = "Oak", KerfMm = 0.1 }] };
        var r = InlayMaker.Make(s, d, o, bedWidthMm: 100);
        Assert.Equal(["Inlay · Oak", "Inlay · key"], r.Pages);
        var pieces = On(r.State, "Inlay · Oak", l => l.Mode == LayerMode.Cut);
        Assert.Equal(12, pieces.Count);
        Assert.All(pieces, p => Assert.True(p.Bounds.MinX >= 2 - 1e-6 && p.Bounds.MaxX <= 98 + 1e-6));
        for (int i = 0; i < pieces.Count; i++)
            for (int j = i + 1; j < pieces.Count; j++)
                Assert.False(pieces[i].Bounds.Intersects(pieces[j].Bounds), $"{pieces[i].Name} and {pieces[j].Name} overlap");
        // The key: every piece where it goes, with its number, nothing burned.
        var key = Page(r.State, "Inlay · key");
        Assert.All(key.Layers, l => Assert.False(l.Output));
        Assert.Equal(12, key.Shapes.OfType<TextShape>().Count());
        Assert.Equal(12, key.Shapes.Count(x => x is not TextShape));
    }

    [Fact]
    public void NumbersAreMarkedOnlyWhenAsked_AndWarningsSayWhatsWrong()
    {
        // Two pieces of one wood 0.05 mm apart: too close to cut in place with a 0.2 mm kerf.
        var (s, sel, _) = Design((PathGeometry.Rect(0, 0, 10, 10), 0), (PathGeometry.Rect(10.05, 0, 10, 10), 0));
        var d = InlayDesign.From(s, sel);
        Assert.Equal(2, d.Pieces.Count);
        var o = new InlayOptions { Into = InlayInto.Nothing, FitMm = 0, Woods = [new WoodCut { Name = "Ash", KerfMm = 0.2 }] };
        var r = InlayMaker.Make(s, d, o);
        Assert.Contains(r.Warnings, w => w.Contains("too close"));
        var numbers = Page(r.State, "Inlay · Ash").Layers.Single(l => l.Mode == LayerMode.Score);
        Assert.False(numbers.Output);
        Assert.DoesNotContain(InlayMaker.Make(s, d, o with { Pack = true }).Warnings, w => w.Contains("too close"));
        var marked = InlayMaker.Make(s, d, o with { MarkNumbers = true });
        var layer = Page(marked.State, "Inlay · Ash").Layers;
        Assert.True(layer.Single(l => l.Mode == LayerMode.Score).Output);
        // Marked before the cuts (a cut piece can shift).
        Assert.True(layer.FindIndex(l => l.Mode == LayerMode.Score) < layer.FindIndex(l => l.Mode == LayerMode.Cut));
        Assert.Throws<InvalidOperationException>(() => InlayMaker.Make(s, InlayDesign.Empty, o));
    }

    [Fact]
    public void ANumberGoesInThePiecesRoomiestPart()
    {
        // A C: its middle is outside it, so the number goes in the thick back.
        var c = ShapeGeometry.Boolean(PathGeometry.Rect(0, 0, 30, 30), PathGeometry.Rect(8, 8, 30, 14), BoolOp.Subtract);
        var (at, room) = Piece.InsidePoint(c);
        Assert.True(at.X < 8, $"{at}");
        Assert.InRange(room, 4, 4.8); // the back is 8 mm thick (a little more in its corners)
    }

    [Fact]
    public void ComponentsKeepHolesWithTheirOutline()
    {
        // A frame with a separate island in its middle: two parts, the frame keeping its hole.
        var frame = ShapeGeometry.Boolean(PathGeometry.Rect(0, 0, 30, 30), PathGeometry.Rect(5, 5, 20, 20), BoolOp.Subtract);
        var g = ShapeGeometry.Union([frame, PathGeometry.Rect(10, 10, 10, 10)]);
        var parts = InlayDesign.Components(g).OrderByDescending(p => p.Bounds().Width).ToList();
        Assert.Equal(2, parts.Count);
        Assert.Equal(2, parts[0].Subpaths.Length);
        Assert.Single(parts[1].Subpaths);
    }

    [Fact]
    public void TheFitTestHasAHoleForEachGapAndAStarForEachHole()
    {
        var s = DocState.New();
        var fits = new[] { -0.04, 0.0, 0.04, 0.08 };
        var o = new FitTestOptions
        {
            Into = InlayInto.Holes, Fits = fits, SizeMm = 12,
            Piece = new WoodCut { Name = "Walnut", KerfMm = 0.1 },
            Inlay = new InlayOptions { Sheet = new WoodCut { Name = "Maple", KerfMm = 0.12 }, CutSheetEdge = true },
        };
        var (state, pages) = InlayFitTest.Make(s, o);
        Assert.Equal(["Inlay fit test · holes", "Inlay fit test · stars"], pages);
        var holes = On(state, pages[0], l => l.Mode == LayerMode.Cut).Where(x => x.Name.StartsWith("Hole")).ToList();
        Assert.Equal(4, holes.Count);
        Assert.Contains(On(state, pages[0], l => l.Mode == LayerMode.Cut), x => x.Name == "Strip");
        Assert.Equal(["−0.04", "0", "+0.04", "+0.08"], Page(state, pages[0]).Shapes.OfType<TextShape>().Select(t => t.Text));
        // Each hole bigger than the last by the step; the stars all the drawn size plus half a kerf.
        var areas = holes.Select(h => ShapeGeometry.Area(h.Geometry)).ToList();
        Assert.True(areas.Zip(areas.Skip(1)).All(p => p.Second > p.First));
        var star = InlayFitTest.Star(new Vec2(0, 0), 12);
        var hole0 = holes[1].Geometry; // gap 0: the star less half the sheet's kerf
        Assert.Equal(ShapeGeometry.Area(ShapeGeometry.Offset(star, -0.06, JoinStyle.Sharp, EndStyle.Flat, 8)), ShapeGeometry.Area(hole0), 2);
        var stars = On(state, pages[1], l => l.Mode == LayerMode.Cut);
        Assert.Equal(4, stars.Count);
        Assert.All(stars, x => Assert.Equal(ShapeGeometry.Area(ShapeGeometry.Offset(star, 0.05, JoinStyle.Sharp, EndStyle.Flat, 8)), ShapeGeometry.Area(x.Geometry), 2));

        // Into a pocket: engraved, the depth's passes, no strip.
        var pocket = InlayFitTest.Make(s, o with { Into = InlayInto.Pocket, Inlay = new InlayOptions { ThicknessMm = 0.6, EtchMmPerPass = 0.2 } });
        var fill = Page(pocket.State, pocket.Pages[0]).Layers.Single(l => l.Mode == LayerMode.Fill);
        Assert.Equal(3, fill.Laser.Passes);
        Assert.Equal(4, Page(pocket.State, pocket.Pages[0]).Shapes.Count(x => x.LayerId == fill.Id));
    }
}
