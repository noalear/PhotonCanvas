using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Geometry;
using Xunit;

namespace PhotonCanvas.Tests;

public class PieceSplitTests
{
    /// <summary>
    /// A 400 × 150 mm design over four 200 × 75 mm boards laid two by two: an engraved background,
    /// a cut circle across the middle where all four meet, and a small cut square on the first board.
    /// </summary>
    static (DocState State, List<PathShape> Pieces, HashSet<Guid> Art, Guid Fill, Guid Cut) FourBoards()
    {
        var s = DocState.New();
        var cut = s.Layers[0] with { Name = "Cut" };
        var fill = Layer.Default(1, LayerMode.Fill) with { Name = "Engrave" };
        s = s with { Layers = [fill, cut] };
        var boards = new List<PathShape>();
        for (int r = 0; r < 2; r++)
            for (int c = 0; c < 2; c++)
                boards.Add(new PathShape { Name = $"Board {r}{c}", LayerId = cut.Id, Geometry = PathGeometry.Rect(c * 200, r * 75, 200, 75) });
        var background = new PathShape { Name = "Background", LayerId = fill.Id, Geometry = PathGeometry.Rect(0, 0, 400, 150) };
        var circle = new PathShape { Name = "Circle", LayerId = cut.Id, Geometry = PathGeometry.Ellipse(200, 75, 20, 20) };
        var square = new PathShape { Name = "Square", LayerId = cut.Id, Geometry = PathGeometry.Rect(10, 10, 20, 20) };
        s = Ops.AddShapes(s, [.. boards, background, circle, square]);
        s = MaterialOutline.Mark(s, boards.Select(b => b.Id).ToHashSet());
        var pieces = MaterialOutline.On(s).ToList();
        return (s, pieces, [background.Id, circle.Id, square.Id], fill.Id, cut.Id);
    }

    static double Length(PathGeometry g) => g.Flatten(0.005).Sum(f =>
    {
        double l = 0;
        for (int i = 1; i < f.Points.Count; i++) l += f.Points[i].Dist(f.Points[i - 1]);
        return l;
    });

    [Fact]
    public void EachBoardGetsAPageWithItsShareInTheWorkArea()
    {
        var (s, pieces, art, _, _) = FourBoards();
        var before = s.Shapes.Count;
        var r = PieceSplit.Split(s, pieces, art, 220, 220);
        Assert.Equal(4, r.Pieces);
        Assert.Empty(r.Notes);
        Assert.Equal(["Page 1", "Piece 1 of 4", "Piece 2 of 4", "Piece 3 of 4", "Piece 4 of 4"], Pages.Names(r.State));
        Assert.Equal("Piece 1 of 4", r.State.PageName);
        // The layout page is left as it was.
        Assert.Equal(before, Pages.SwitchTo(r.State, 0).Shapes.Count);

        double circleQuarter = 2 * Math.PI * 20 / 4;
        for (int k = 1; k <= 4; k++)
        {
            var page = Pages.SwitchTo(r.State, k);
            var mat = MaterialOutline.On(page);
            var board = Assert.Single(mat);
            Assert.Equal(PieceSplit.PieceName(k), board.Name);
            // Reading order: top left, top right, bottom left, bottom right; each moved onto the bed.
            var b = board.Bounds;
            Assert.InRange(b.MinX, 0, 220);
            Assert.InRange(b.MaxX, 0, 220);
            Assert.InRange(b.MinY, 0, 220);
            Assert.InRange(b.MaxY, 0, 220);
            Assert.Equal(200, b.Width, 2);
            Assert.Equal(75, b.Height, 2);

            // What's sent lies on the board: the background's share, a quarter of the circle.
            var sent = page.Shapes.Where(x => page.FindLayer(x.LayerId)?.Output == true).OfType<PathShape>().ToList();
            Assert.All(sent, x => Assert.True(b.Inflate(0.05).Contains(x.Bounds), $"{x.Name} on piece {k} sticks out: {x.Bounds}"));
            var background = Assert.Single(sent, x => x.Name == "Background");
            Assert.Equal(200 * 75, ShapeGeometry.Area(background.World), 1);
            var arc = Assert.Single(sent, x => x.Name == "Circle");
            Assert.All(arc.World.Subpaths, sp => Assert.False(sp.Closed));
            Assert.Equal(circleQuarter, Length(arc.World), 0.3);
            Assert.Equal(k == 1, sent.Any(x => x.Name == "Square"));

            // The rest shows on a layer that's never sent, the other boards included.
            var others = Assert.Single(page.Layers, l => l.Name == PieceSplit.OthersLayer);
            Assert.False(others.Output);
            var greyed = page.Shapes.Where(x => x.LayerId == others.Id).ToList();
            Assert.Equal(3, greyed.Count(x => x.Name.StartsWith("Piece ")));
            Assert.Contains(greyed, x => x.Name == "Circle");
        }
        // Top right: moved left onto the bed, 5 mm in from the right edge.
        Assert.Equal(215, Assert.Single(MaterialOutline.On(Pages.SwitchTo(r.State, 2))).Bounds.MaxX, 2);
    }

    [Fact]
    public void AShapeWhollyOnOneBoardIsCopiedAsItIs()
    {
        var (s, pieces, art, _, cut) = FourBoards();
        var r = PieceSplit.Split(s, pieces, art, 220, 220);
        var page = Pages.SwitchTo(r.State, 1);
        var square = Assert.Single(page.Shapes.OfType<PathShape>(), x => x.Name == "Square" && page.FindLayer(x.LayerId)?.Output == true);
        Assert.True(square.Geometry.Subpaths.Single().Closed);
        Assert.Equal(20 * 20, ShapeGeometry.Area(square.World), 3);
        // Not greyed on its own page (it's all there); greyed on the others.
        var othersLayer = page.Layers.Single(l => l.Name == PieceSplit.OthersLayer).Id;
        Assert.DoesNotContain(page.Shapes, x => x.LayerId == othersLayer && x.Name == "Square");
        var page2 = Pages.SwitchTo(r.State, 2);
        var others2 = page2.Layers.Single(l => l.Name == PieceSplit.OthersLayer).Id;
        Assert.Contains(page2.Shapes, x => x.LayerId == others2 && x.Name == "Square");
    }

    [Fact]
    public void APictureIsClippedToEachBoardAndBurnsOnlyThere()
    {
        var (s, pieces, art, _, _) = FourBoards();
        var picLayer = Layer.Default(2, LayerMode.Image) with { Name = "Photo", Dpi = 100 };
        s = Ops.AddLayer(s, picLayer);
        var px = new byte[8 * 4 * 4];
        for (int i = 0; i < px.Length; i += 4) { px[i] = px[i + 1] = px[i + 2] = 40; px[i + 3] = 255; }
        var photo = new ImageShape { Name = "Photo", LayerId = picLayer.Id, Source = ImageSource.FromEncoded(new Core.Imaging.RasterImage(8, 4, px).ToPng()), SizeMm = new Vec2(400, 150) };
        s = Ops.AddShapes(s, [photo]);
        var r = PieceSplit.Split(s, pieces, new HashSet<Guid>(art) { photo.Id }, 220, 220);
        for (int k = 1; k <= 4; k++)
        {
            var page = Pages.SwitchTo(r.State, k);
            var board = MaterialOutline.On(page).Single().Bounds;
            var live = Assert.Single(page.Shapes.OfType<ImageShape>(), x => x.LayerId == page.Layers.First(l => l.Name == "Photo").Id);
            Assert.NotNull(live.Clip);
            Assert.True(board.Inflate(0.05).Contains(live.ShownBounds), $"piece {k}: shows over {live.ShownBounds}, board {board}");
            // Burned only on the board, so the job fits the work area.
            var (block, area) = JobBuilder.Rasterize(live, page.FindLayer(live.LayerId)!);
            Assert.NotNull(block);
            Assert.True(board.Inflate(0.3).Contains(area), $"piece {k}: dots over {area}");
        }
    }

    [Fact]
    public void PiecesAreReadRowByRow()
    {
        var s = DocState.New();
        var l = s.Layers[0].Id;
        // A slightly ragged layout: the second row's boards aren't level.
        PathShape Board(string n, double x, double y) => new() { Name = n, LayerId = l, Geometry = PathGeometry.Rect(x, y, 100, 50) };
        var order = PieceSplit.InReadingOrder([Board("d", 103, 55), Board("b", 102, 1), Board("c", 0, 52), Board("a", 0, 0)]);
        Assert.Equal(["a", "b", "c", "d"], order.Select(p => p.Name));
    }

    [Fact]
    public void LinesAreClippedToARegion()
    {
        var region = PathGeometry.Rect(0, 0, 100, 100);
        // A square half over the edge: three of its sides, half of two of them, stay.
        var across = ShapeGeometry.ClipLines(PathGeometry.Rect(80, 20, 40, 40), region);
        Assert.All(across.Subpaths, sp => Assert.False(sp.Closed));
        Assert.Equal(20 + 40 + 20, Length(across), 0.05);
        // One wholly inside comes back closed, all of it.
        var inside = ShapeGeometry.ClipLines(PathGeometry.Rect(10, 10, 30, 30), region);
        Assert.True(Assert.Single(inside.Subpaths).Closed);
        Assert.Equal(120, Length(inside), 0.05);
        Assert.True(ShapeGeometry.ClipLines(PathGeometry.Rect(200, 200, 10, 10), region).IsEmpty);
    }
}
