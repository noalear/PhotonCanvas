using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Geometry;
using PhotonCanvas.Addon.Papercraft;
using Xunit;
using Tri = PhotonCanvas.Doc.Import.StlDepth.Triangle;
using V3 = PhotonCanvas.Doc.Import.StlDepth.Vec3;

namespace PhotonCanvas.Tests;

public class PapercraftTests
{
    static IEnumerable<Tri> Quad(V3 a, V3 b, V3 c, V3 d) => [new Tri(a, b, c), new Tri(a, c, d)];

    static List<Tri> Box(double s, bool dent = false)
    {
        V3 P(double x, double y, double z) => new(x, y, z);
        // A deep dent: its faces' shared edges are longer than the rim, so they're folded, not cut.
        var apex = P(s / 2, s / 2, s * 0.05);
        var top = dent
            ? new List<Tri> { new(P(0, 0, s), P(0, s, s), apex), new(P(0, s, s), P(s, s, s), apex), new(P(s, s, s), P(s, 0, s), apex), new(P(s, 0, s), P(0, 0, s), apex) }
            : [.. Quad(P(0, 0, s), P(0, s, s), P(s, s, s), P(s, 0, s))];
        return
        [
            .. Quad(P(0, 0, 0), P(s, 0, 0), P(s, s, 0), P(0, s, 0)), .. top,
            .. Quad(P(0, 0, 0), P(0, 0, s), P(s, 0, s), P(s, 0, 0)), .. Quad(P(0, s, 0), P(s, s, 0), P(s, s, s), P(0, s, s)),
            .. Quad(P(0, 0, 0), P(0, s, 0), P(0, s, s), P(0, 0, s)), .. Quad(P(s, 0, 0), P(s, 0, s), P(s, s, s), P(s, s, 0)),
        ];
    }

    static List<Tri> Sphere(double r, int lat, int lon)
    {
        V3 P(int i, int j)
        {
            double t = Math.PI * i / lat, p = 2 * Math.PI * j / lon;
            return new(r * Math.Sin(t) * Math.Cos(p), r * Math.Sin(t) * Math.Sin(p), r * Math.Cos(t));
        }
        var o = new List<Tri>();
        for (int i = 0; i < lat; i++)
            for (int j = 0; j < lon; j++)
            {
                V3 a = P(i, j), b = P(i + 1, j), c = P(i + 1, j + 1), d = P(i, j + 1);
                if (i > 0) o.Add(new Tri(a, b, d));
                if (i < lat - 1) o.Add(new Tri(b, c, d));
            }
        return o;
    }

    static double Len(V3 a, V3 b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y) + (a.Z - b.Z) * (a.Z - b.Z));
    static double Area(Vec2 a, Vec2 b, Vec2 c) => Math.Abs((b - a).Cross(c - a)) / 2;
    static double Area3(Tri t)
    {
        double a = Len(t.A, t.B), b = Len(t.B, t.C), c = Len(t.C, t.A), s = (a + b + c) / 2;
        return Math.Sqrt(Math.Max(0, s * (s - a) * (s - b) * (s - c)));
    }

    [Fact]
    public void ACubeUnfoldsIntoOneCrossWithSevenTabs()
    {
        var net = Unfolder.Unfold(Box(10), new PapercraftOptions { SizeMm = 40, MaxFaces = 0 });
        var piece = Assert.Single(net.Pieces);
        Assert.Equal(12, net.Faces);
        // Six squares need five folds to hold together; the other seven of the cube's twelve edges are cut and glued.
        Assert.Equal(5, net.Folds);
        Assert.Equal(7, net.Joins);
        Assert.All(piece.Folds, f => { Assert.Equal(FoldKind.Mountain, f.Kind); Assert.Equal(90, f.AngleDeg, 3); Assert.Equal(40, f.A.Dist(f.B), 6); });
        // The squares' diagonals are flat: no line drawn.
        Assert.Equal(14, piece.Edges.Count);
        Assert.Equal(7, piece.Tabs.Count);
        Assert.Equal(0, net.MissingTabs);
        // Each edge number appears on exactly two edges.
        Assert.All(piece.Edges.GroupBy(e => e.Number), g => Assert.Equal(2, g.Count()));
        // Flat area is the cube's surface: nothing stretched, nothing overlapping.
        Assert.Equal(6 * 40 * 40, piece.Faces.Sum(f => Area(f.A, f.B, f.C)), 3);
        var faces = ShapeGeometry.Union(piece.Faces.Select(f => PathGeometry.Polyline([f.A, f.B, f.C], true)), 0.001);
        Assert.Equal(6 * 40 * 40, ShapeGeometry.Area(faces), 0);
        // The outline (faces and tabs) is one closed shape.
        Assert.Single(PapercraftLayout.Outline(piece).Subpaths);
    }

    [Fact]
    public void InsideOutModelsFoldTheSameWay()
    {
        var inside = Box(10).Select(t => new Tri(t.A, t.C, t.B)).ToList();
        var net = Unfolder.Unfold(inside, new PapercraftOptions { SizeMm = 40, MaxFaces = 0 });
        Assert.All(net.Pieces.SelectMany(p => p.Folds), f => Assert.Equal(FoldKind.Mountain, f.Kind));
    }

    [Fact]
    public void DentsFoldAsValleys()
    {
        var net = Unfolder.Unfold(Box(10, dent: true), new PapercraftOptions { SizeMm = 40, MaxFaces = 0, FlatDeg = 1 });
        var folds = net.Pieces.SelectMany(p => p.Folds).ToList();
        Assert.True(folds.Any(f => f.Kind == FoldKind.Valley), string.Join("; ", folds.Select(f => $"{f.Kind} {f.AngleDeg:0}")) + $" pieces {net.Pieces.Count} joins {net.Joins}");
        // Inside the dent the faces meet in valleys; round the rim and the box's corners they're ridges.
        Assert.Contains(folds, f => f.Kind == FoldKind.Valley);
        Assert.Contains(folds, f => f.Kind == FoldKind.Mountain);
    }

    [Fact]
    public void ASphereUnfoldsWithoutOverlapsAndKeepsEveryTriangleItsShape()
    {
        var mesh = Sphere(1, 10, 16);
        var net = Unfolder.Unfold(mesh, new PapercraftOptions { SizeMm = 100, MaxFaces = 0, MaxWidthMm = 300, MaxHeightMm = 300 });
        Assert.Equal(mesh.Count, net.Faces);
        // Surface area kept (scaled from radius 1 to 100 mm across).
        double want = mesh.Sum(Area3) * 50 * 50;
        double got = net.Pieces.Sum(p => p.Faces.Sum(f => Area(f.A, f.B, f.C)));
        Assert.Equal(want, got, want * 1e-6);
        foreach (var p in net.Pieces)
        {
            // No overlaps: the faces' union covers as much as their sum.
            var union = ShapeGeometry.Union(p.Faces.Select(f => PathGeometry.Polyline([f.A, f.B, f.C], true)), 0.001);
            double sum = p.Faces.Sum(f => Area(f.A, f.B, f.C));
            Assert.True(Math.Abs(ShapeGeometry.Area(union) - sum) < sum * 1e-3 + 0.05, $"piece {p.Index}: {ShapeGeometry.Area(union)} vs {sum}");
            var b = p.Bounds;
            Assert.True(Math.Max(b.Width, b.Height) <= 300, $"{b.Width} × {b.Height}");
        }
        // Every join is numbered on two edges.
        var numbers = net.Pieces.SelectMany(p => p.Edges).Where(e => e.Number > 0).GroupBy(e => e.Number);
        Assert.All(numbers, g => Assert.Equal(2, g.Count()));
        Assert.Equal(net.Joins, numbers.Count());
    }

    [Fact]
    public void PiecesStayInsideTheBed()
    {
        var net = Unfolder.Unfold(Sphere(1, 12, 20), new PapercraftOptions { SizeMm = 200, MaxFaces = 0, MaxWidthMm = 150, MaxHeightMm = 100, TabMm = 5 });
        Assert.True(net.Pieces.Count > 1);
        Assert.All(net.Pieces, p =>
        {
            var b = p.Bounds;
            Assert.True(Math.Min(b.Width, b.Height) <= 100 && Math.Max(b.Width, b.Height) <= 150, $"{b.Width} × {b.Height}");
        });
    }

    [Fact]
    public void SimplifyingKeepsUnderTheFaceCount()
    {
        var mesh = Sphere(1, 40, 60);
        var net = Unfolder.Unfold(mesh, new PapercraftOptions { SizeMm = 100, MaxFaces = 200 });
        Assert.True(net.Faces <= 200 && net.Faces > 40, $"{net.Faces}");
        Assert.Equal(mesh.Count, net.SourceTriangles);
    }

    [Fact]
    public void LaidOutWithFoldsAndNumbersBeforeTheCut()
    {
        var net = Unfolder.Unfold(Box(10, dent: true), new PapercraftOptions { SizeMm = 50, MaxFaces = 0, MaxWidthMm = 214, MaxHeightMm = 214 });
        var (s, sheets) = PapercraftLayout.Place(DocState.New(), net, MachineProfile.F2Ultra, numbers: true);
        Assert.Equal(1, sheets);
        int Index(string name) => s.Layers.FindIndex(l => l.Name == name);
        Assert.True(Index(PapercraftLayout.NumbersLayer) < Index(PapercraftLayout.CutLayer));
        Assert.True(Index(PapercraftLayout.MountainLayer) < Index(PapercraftLayout.CutLayer));
        Assert.True(Index(PapercraftLayout.ValleyLayer) < Index(PapercraftLayout.CutLayer));
        var cut = s.Layers.Single(l => l.Name == PapercraftLayout.CutLayer);
        Assert.Equal(net.Pieces.Count, s.Shapes.Count(x => x.LayerId == cut.Id));
        var valley = s.Layers.Single(l => l.Name == PapercraftLayout.ValleyLayer);
        Assert.Equal(LayerMode.Score, valley.Mode);
        Assert.True(valley.PerforateMm > 0);
        Assert.Contains(s.Shapes, x => x.LayerId == valley.Id);
        Assert.Contains(s.Shapes, x => x is TextShape);
        // Everything on the bed.
        Assert.All(s.Shapes, x => Assert.True(x.Bounds.MinX >= 0 && x.Bounds.MinY >= 0 && x.Bounds.MaxX <= MachineProfile.F2Ultra.FieldW && x.Bounds.MaxY <= MachineProfile.F2Ultra.FieldH, $"{x.Name} {x.Bounds}"));
        // Every piece's shapes are grouped together.
        Assert.All(s.Shapes, x => Assert.NotNull(x.GroupId));
    }
}
