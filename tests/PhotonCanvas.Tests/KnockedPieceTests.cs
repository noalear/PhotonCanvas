using PhotonCanvas.Addon.Recovery;
using PhotonCanvas.Core;
using PhotonCanvas.Core.Camera;
using PhotonCanvas.Doc;
using SkiaSharp;

namespace PhotonCanvas.Tests;

/// <summary>
/// Knocked-piece recovery against made-up photos: a board on slatted bed, part of the design
/// already burnt on it, then the board knocked (moved and turned). The photo shows the board where
/// it is now with the finished marks on it; the rest of the design isn't there yet.
/// </summary>
public class KnockedPieceTests
{
    static readonly Rect2 Area = new(20, 10, 200, 160);
    const double MmPerPx = 0.2;
    static readonly Vec2 Turn = new(100, 80);

    static readonly Vec2[] F =
    [
        new(62, 50), new(84, 50), new(84, 56), new(69, 56), new(69, 62), new(80, 62),
        new(80, 68), new(69, 68), new(69, 80), new(62, 80),
    ];

    static readonly Vec2[] Triangle = [new(95, 52), new(118, 52), new(100, 74)];

    /// <summary>The design: three engraved shapes, then a hole and an outline cut out of the board.</summary>
    static (DocState State, Shape[] Engraved, Shape[] Cuts, Shape Board) Design()
    {
        var s = DocState.New();
        var cutLayer = s.Layers[0];
        var engrave = Layer.Default(1, LayerMode.Fill) with { Name = "Engrave" };
        s = Ops.AddLayer(s, engrave, 0);
        (s, var material) = MaterialOutline.Ensure(s);
        Shape[] engraved =
        [
            new PathShape { Name = "F", LayerId = engrave.Id, Geometry = PathGeometry.Polyline(F, true) },
            new PathShape { Name = "Triangle", LayerId = engrave.Id, Geometry = PathGeometry.Polyline(Triangle, true) },
            new PathShape { Name = "Bar", LayerId = engrave.Id, Geometry = PathGeometry.Rect(62, 90, 60, 6) },
        ];
        Shape[] cuts =
        [
            new PathShape { Name = "Hole", LayerId = cutLayer.Id, Geometry = PathGeometry.Ellipse(130, 70, 6, 6) },
            new PathShape { Name = "Outline", LayerId = cutLayer.Id, Geometry = PathGeometry.RoundRect(55, 44, 95, 60, 4, 4) },
        ];
        var board = new PathShape { Name = "Board", LayerId = material.Id, Geometry = PathGeometry.Rect(48, 38, 110, 74) };
        s = Ops.AddShapes(s, [.. engraved, .. cuts, board]);
        return (s, engraved, cuts, board);
    }

    /// <summary>A straightened photo: dark slatted bed, the light board with grain, and the finished marks, all moved by <paramref name="knock"/>.</summary>
    static GrayImage Photo(Affine knock, IEnumerable<Shape> burnt, Shape board, int seed = 3)
    {
        int w = (int)Math.Round(Area.Width / MmPerPx), h = (int)Math.Round(Area.Height / MmPerPx);
        using var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul));
        var rng = new Random(seed);
        using (var c = new SKCanvas(bmp))
        {
            c.Clear(new SKColor(60, 60, 60));
            using var slat = new SKPaint { Color = new SKColor(30, 30, 30), IsAntialias = true };
            for (double x = Area.MinX; x < Area.MaxX; x += 7)
                c.DrawRect((float)((x - Area.MinX) / MmPerPx), 0, (float)(1 / MmPerPx), h, slat);
            using var wood = new SKPaint { Color = new SKColor(205, 180, 140), IsAntialias = true };
            using (var p = PathOf(board, knock)) c.DrawPath(p, wood);
            // Grain: faint streaks along the board.
            using var grain = new SKPaint { Color = new SKColor(185, 158, 120), IsAntialias = true, StrokeWidth = 2, Style = SKPaintStyle.Stroke };
            c.Save();
            using (var clip = PathOf(board, knock)) c.ClipPath(clip, antialias: true);
            for (int i = 0; i < 40; i++)
            {
                double y = 30 + rng.NextDouble() * 100, x0 = 40, x1 = 170, wob = rng.NextDouble() * 3;
                using var streak = new SKPath();
                for (double x = x0; x <= x1; x += 2)
                {
                    var q = knock.Apply(new Vec2(x, y + wob * Math.Sin(x / 9 + i)));
                    var px = Px(q);
                    if (x == x0) streak.MoveTo(px); else streak.LineTo(px);
                }
                c.DrawPath(streak, grain);
            }
            c.Restore();
            using var burn = new SKPaint { Color = new SKColor(70, 45, 25), IsAntialias = true };
            foreach (var shape in burnt)
            {
                using var p = PathOf(shape, knock);
                c.DrawPath(p, burn);
            }
        }
        var g = CameraWarp.ToGray(bmp);
        for (int i = 0; i < g.Pixels.Length; i++) g.Pixels[i] = (byte)Math.Clamp(g.Pixels[i] + rng.Next(-10, 11), 0, 255);
        return g;
    }

    static SKPoint Px(Vec2 p) => new((float)((p.X - Area.MinX) / MmPerPx), (float)((p.Y - Area.MinY) / MmPerPx));

    static SKPath PathOf(Shape shape, Affine knock)
    {
        var path = new SKPath { FillType = SKPathFillType.EvenOdd };
        foreach (var f in ((PathShape)shape).World.Flatten(0.02))
        {
            for (int i = 0; i < f.Points.Count; i++)
            {
                var px = Px(knock.Apply(f.Points[i]));
                if (i == 0) path.MoveTo(px); else path.LineTo(px);
            }
            if (f.Closed) path.Close();
        }
        return path;
    }

    static Affine Knock(double deg, double dx, double dy) => Affine.RotateAbout(Turn, deg).Then(Affine.Translate(dx, dy));

    static IEnumerable<Vec2> Points(Shape s) => ((PathShape)s).World.Flatten(0.1).SelectMany(f => f.Points);

    [Fact]
    public void FindsTheKnockedPieceByItsMarksAndTellsWhatsDone()
    {
        var (s, engraved, cuts, board) = Design();
        var knock = Knock(6, 3.7, -2.4);
        var photo = Photo(knock, engraved, board);
        var r = KnockRecovery.Find(photo, Area, MmPerPx, KnockRecovery.Marked(s));

        Assert.True(r.Trusted, $"matched {r.Matched:P0}, other turn {r.OtherTurnDeg}");
        Assert.InRange(r.AngleDeg, 5.6, 6.4);
        // The parts still to do land where they belong on the board: within 0.3 mm.
        foreach (var cut in cuts)
            foreach (var p in Points(cut))
                Assert.True(r.Move.Apply(p).Dist(knock.Apply(p)) < 0.3, $"{cut.Name} {p}: {r.Move.Apply(p)} vs {knock.Apply(p)}");
        foreach (var e in engraved) Assert.Equal(MarkState.Done, r.Shapes.Single(x => x.Id == e.Id).State);
        foreach (var c in cuts) Assert.Equal(MarkState.NotYet, r.Shapes.Single(x => x.Id == c.Id).State);
        Assert.InRange(r.FarErrorMm, 0.01, 1.5);
    }

    [Fact]
    public void FindsItWhenOnlyOneShapeIsFinished()
    {
        var (s, engraved, cuts, board) = Design();
        var knock = Knock(-11, -6, 4.5);
        var photo = Photo(knock, [engraved[0]], board, seed: 9);
        var r = KnockRecovery.Find(photo, Area, MmPerPx, KnockRecovery.Marked(s));

        Assert.True(r.Trusted, $"matched {r.Matched:P0}");
        Assert.Equal(MarkState.Done, r.Shapes.Single(x => x.Id == engraved[0].Id).State);
        Assert.Equal(MarkState.NotYet, r.Shapes.Single(x => x.Id == engraved[2].Id).State);
        foreach (var p in Points(cuts[1]))
            Assert.True(r.Move.Apply(p).Dist(knock.Apply(p)) < 0.5, $"{p}: {r.Move.Apply(p)} vs {knock.Apply(p)}");
    }

    [Fact]
    public void AShapeKnockedHalfwayThroughShowsAsPartlyDone()
    {
        var (s, engraved, cuts, board) = Design();
        var knock = Knock(4, -2, 3);
        // The bar's fill had got half way across.
        var half = new PathShape { Geometry = PathGeometry.Rect(62, 90, 30, 6) };
        var photo = Photo(knock, [engraved[0], engraved[1], half], board, seed: 11);
        var r = KnockRecovery.Find(photo, Area, MmPerPx, KnockRecovery.Marked(s));
        Assert.True(r.Trusted);
        Assert.Equal(MarkState.Partly, r.Shapes.Single(x => x.Id == engraved[2].Id).State);
        Assert.Equal(MarkState.Done, r.Shapes.Single(x => x.Id == engraved[1].Id).State);
    }

    [Fact]
    public void ShapesTheUserSaysAreFinishedAreTheOnesFitted()
    {
        var (s, engraved, cuts, board) = Design();
        var knock = Knock(3, 2, 1);
        var photo = Photo(knock, engraved, board, seed: 5);
        var done = engraved.Select(e => e.Id).ToHashSet();
        var r = KnockRecovery.Find(photo, Area, MmPerPx, KnockRecovery.Marked(s), knownDone: done);
        Assert.All(r.Shapes, x => Assert.Equal(done.Contains(x.Id) ? MarkState.Done : MarkState.NotYet, x.State));
        foreach (var p in Points(cuts[0]))
            Assert.True(r.Move.Apply(p).Dist(knock.Apply(p)) < 0.3);
    }

    [Fact]
    public void RoundMarksCantTellTheTurn()
    {
        var s = DocState.New();
        var ring = new PathShape { Name = "Ring", LayerId = s.Layers[0].Id, Geometry = PathGeometry.Ellipse(100, 80, 15, 15) };
        var later = new PathShape { Name = "Later", LayerId = s.Layers[0].Id, Geometry = PathGeometry.Rect(130, 60, 10, 30) };
        s = Ops.AddShapes(s, [ring, later]);
        var board = new PathShape { Geometry = PathGeometry.Rect(60, 40, 100, 80) };
        var photo = Photo(Knock(8, 2, -3), [ring], board);
        var r = KnockRecovery.Find(photo, Area, MmPerPx, KnockRecovery.Marked(s), knownDone: new HashSet<Guid> { ring.Id });
        Assert.NotNull(r.OtherTurnDeg);
        Assert.False(r.Trusted);
    }

    [Fact]
    public void OnlyShapesThatAreSentCount()
    {
        var (s, engraved, cuts, board) = Design();
        // A sub-layer on the cut layer marks its shapes too; the material layer marks nothing.
        var outline = Layer.Default(5, LayerMode.Score) with { ParentId = s.Layers.First(l => l.Name == "Cut").Id };
        s = Ops.AddLayer(s, outline);
        s = Ops.UpdateLayers(s, new HashSet<Guid> { s.Layers.First(l => l.Name == "Cut").Id }, l => l with { Output = false });
        var marked = KnockRecovery.Marked(s).Select(m => m.Id).ToHashSet();
        Assert.All(engraved, e => Assert.Contains(e.Id, marked));
        Assert.All(cuts, c => Assert.Contains(c.Id, marked));
        Assert.DoesNotContain(board.Id, marked);
    }

    [Fact]
    public void TheRestGoesOnANewPageAndThisPageIsKept()
    {
        var (s, engraved, cuts, board) = Design();
        var move = Knock(5, 1, 2);
        var all = s.Shapes.Select(x => x.Id).ToHashSet();
        var done = engraved.Select(e => e.Id).ToHashSet();
        var t = KnockRecovery.RestPage(s, all, done, move);

        Assert.Equal(1, t.PageIndex);
        Assert.StartsWith("Page 1", t.PageName);
        Assert.Contains("rest", t.PageName);
        // This page is as it was.
        var kept = Assert.Single(t.OtherPages);
        Assert.Equal(s.Shapes.Select(x => x.Transform), kept.Shapes.Select(x => x.Transform));
        Assert.Equal(s.Shapes.Select(x => x.LayerId), kept.Shapes.Select(x => x.LayerId));
        // On the new page everything is moved, and the finished shapes aren't sent again.
        Assert.Equal(s.Shapes.Count, t.Shapes.Count);
        for (int i = 0; i < s.Shapes.Count; i++)
            Assert.True(Points(t.Shapes[i]).First().Dist(move.Apply(Points(s.Shapes[i]).First())) < 1e-6);
        var doneLayer = t.Layers.Single(l => l.Name == KnockRecovery.DoneLayerName);
        Assert.False(doneLayer.Output);
        Assert.Equal(3, t.Shapes.Count(x => x.LayerId == doneLayer.Id));
        var sent = JobBuilder.OutputLayers(t).Select(l => t.Owner(l.Id)).ToHashSet();
        Assert.Equal(2, t.Shapes.Count(x => sent.Contains(x.LayerId)));
        var job = JobBuilder.Build(t, new JobOptions());
        Assert.NotEmpty(job.Steps);
    }
}
