using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Geometry;
using Xunit;
using Tri = PhotonCanvas.Doc.Import.StlDepth.Triangle;
using V3 = PhotonCanvas.Doc.Import.StlDepth.Vec3;

namespace PhotonCanvas.Tests;

public class StackSlicerTests
{
    static IEnumerable<Tri> Quad(V3 a, V3 b, V3 c, V3 d) => [new Tri(a, b, c), new Tri(a, c, d)];

    static List<Tri> Box(double x0, double y0, double z0, double x1, double y1, double z1)
    {
        V3 P(double x, double y, double z) => new(x, y, z);
        return
        [
            .. Quad(P(x0, y0, z0), P(x1, y0, z0), P(x1, y1, z0), P(x0, y1, z0)), .. Quad(P(x0, y0, z1), P(x0, y1, z1), P(x1, y1, z1), P(x1, y0, z1)),
            .. Quad(P(x0, y0, z0), P(x0, y0, z1), P(x1, y0, z1), P(x1, y0, z0)), .. Quad(P(x0, y1, z0), P(x1, y1, z0), P(x1, y1, z1), P(x0, y1, z1)),
            .. Quad(P(x0, y0, z0), P(x0, y1, z0), P(x0, y1, z1), P(x0, y0, z1)), .. Quad(P(x1, y0, z0), P(x1, y0, z1), P(x1, y1, z1), P(x1, y1, z0)),
        ];
    }

    static List<Tri> Cylinder(double r, double h, int n = 96)
    {
        var o = new List<Tri>();
        for (int i = 0; i < n; i++)
        {
            double a = 2 * Math.PI * i / n, b = 2 * Math.PI * (i + 1) / n;
            V3 A0 = new(r * Math.Cos(a), r * Math.Sin(a), 0), B0 = new(r * Math.Cos(b), r * Math.Sin(b), 0);
            V3 A1 = A0 with { Z = h }, B1 = B0 with { Z = h };
            o.AddRange(Quad(A0, B0, B1, A1));
            o.Add(new Tri(new V3(0, 0, 0), B0, A0));
            o.Add(new Tri(new V3(0, 0, h), A1, B1));
        }
        return o;
    }

    static List<Tri> Pyramid(double s, double h)
    {
        V3 a = new(0, 0, 0), b = new(s, 0, 0), c = new(s, s, 0), d = new(0, s, 0), top = new(s / 2, s / 2, h);
        return [.. Quad(a, d, c, b), new Tri(a, b, top), new Tri(b, c, top), new Tri(c, d, top), new Tri(d, a, top)];
    }

    static double Area(PathGeometry g) => g.Flatten(0.01).Where(f => f.Closed).Sum(f => Geom.SignedArea(f.Points)) is var a ? Math.Abs(a) : 0;

    [Fact]
    public void ACylinderBecomesCircles()
    {
        // 10 mm tall, 4 mm across, in 2 mm wood: five circles, numbered 1 to 5 from the bottom.
        var stack = StackSlicer.Slice(Cylinder(2, 10), new SliceOptions { ThicknessMm = 2, HeightMm = 10 });
        Assert.Equal(5, stack.Slices);
        Assert.Equal(10, stack.StackHeightMm, 6);
        Assert.Equal([1, 2, 3, 4, 5], stack.Parts.Select(p => p.Slice));
        Assert.All(stack.Parts, p => Assert.InRange(Area(p.Outline), Math.PI * 4 * 0.99, Math.PI * 4));
        // The number goes in the middle.
        Assert.All(stack.Parts, p => Assert.True(p.LabelAt.Dist(new Vec2(2, 2)) < 0.3 && p.LabelRoomMm > 1.7));
    }

    [Fact]
    public void APyramidSteps()
    {
        var o = new SliceOptions { ThicknessMm = 3, HeightMm = 30 };
        var mid = StackSlicer.Slice(Pyramid(30, 30), o);
        Assert.Equal(10, mid.Slices);
        var areas = mid.Layers.Select(Area).ToList();
        Assert.True(areas.Zip(areas.Skip(1)).All(p => p.First > p.Second)); // smaller going up
        // Half-way through the bottom slice the pyramid is 28.5 mm across.
        Assert.Equal(28.5 * 28.5, areas[0], 0);
        var outer = StackSlicer.Slice(Pyramid(30, 30), o with { Outline = SliceOutline.Outer }).Layers.Select(Area).ToList();
        var inner = StackSlicer.Slice(Pyramid(30, 30), o with { Outline = SliceOutline.Inner }).Layers.Select(Area).ToList();
        Assert.All(Enumerable.Range(0, 10), i => Assert.True(outer[i] > areas[i] && areas[i] > inner[i]));
        // Every slice but the top shows where the next one sits.
        Assert.All(mid.Parts.Where(p => p.Slice < 10), p => Assert.NotEmpty(p.NextMarks));
        Assert.Empty(mid.Parts.Single(p => p.Slice == 10).NextMarks);
    }

    [Fact]
    public void BranchesBecomeSeparatePartsAndDowelsLineThemUp()
    {
        // Two legs (0–10 high) under a body (10–20 high): 2 parts in each lower slice, one above.
        var legs = new List<Tri>([.. Box(0, 0, 0, 10, 10, 10), .. Box(30, 0, 0, 40, 10, 10), .. Box(0, 0, 10, 40, 10, 20)]);
        var stack = StackSlicer.Slice(legs, new SliceOptions { ThicknessMm = 5, HeightMm = 20 });
        Assert.Equal(4, stack.Slices);
        Assert.Equal(2, stack.Parts.Count(p => p.Slice == 1));
        Assert.Single(stack.Parts, p => p.Slice == 4);
        var drum = StackSlicer.Slice(Cylinder(15, 12), new SliceOptions { ThicknessMm = 3, HeightMm = 12, DowelMm = 4 });
        Assert.Equal(2, drum.Dowels.Count);
        // Each slice has its two dowel holes: three outlines.
        Assert.All(drum.Parts, p => Assert.Equal(3, p.Outline.Subpaths.Length));
        // Standing it another way up changes the slicing.
        var side = StackSlicer.Slice(Cylinder(2, 10), new SliceOptions { ThicknessMm = 2, HeightMm = 4, Up = SliceUp.X });
        Assert.Equal(2, side.Slices);
    }

    [Fact]
    public void SlicesAreLaidOutBedByBed()
    {
        var stack = StackSlicer.Slice(Cylinder(30, 120), new SliceOptions { ThicknessMm = 3, HeightMm = 120 });
        Assert.Equal(40, stack.Slices);
        var (s, sheets) = StackLayout.Place(DocState.New(), stack, MachineProfile.F2Ultra, numbers: true, marks: true);
        Assert.Equal(5, sheets);
        Assert.Equal(["Page 1", "Slices 2", "Slices 3", "Slices 4", "Slices 5"], Pages.Names(s));
        Assert.Equal(0, s.PageIndex);
        var texts = s.Shapes.OfType<TextShape>().Select(t => t.Text).ToList();
        Assert.Equal(Enumerable.Range(1, 9).Select(i => $"{i}"), texts);
        // Everything inside the bed, nothing overlapping.
        var cuts = s.Shapes.OfType<PathShape>().Where(p => p.Name.StartsWith("Slice ") && p is not TextShape).ToList();
        Assert.All(cuts, c => Assert.True(c.Bounds.MinX >= 0 && c.Bounds.MaxX <= 220 && c.Bounds.MaxY <= 220));
        Assert.All(cuts, a => Assert.All(cuts.Where(b => b != a), b => Assert.False(a.Bounds.Intersects(b.Bounds))));
    }

    [Fact]
    public void OverlappingSolidsMerge()
    {
        // Two boxes modelled separately that overlap (a body and a leg, say): one outline, no hole where they overlap.
        var mesh = new List<Tri>([.. Box(0, 0, 0, 20, 20, 10), .. Box(10, 10, 0, 30, 30, 10)]);
        var layer = StackSlicer.Section(mesh, 5);
        Assert.Single(layer.Subpaths);
        Assert.Equal(400 + 400 - 100, Area(layer), 1);
    }

    [Fact]
    public void PinsJoinEachSliceToTheNext()
    {
        // Two legs under a body: each leg slice pins to the next, the leg tops to the body.
        var legs = new List<Tri>([.. Box(0, 0, 0, 12, 12, 10), .. Box(30, 0, 0, 42, 12, 10), .. Box(0, 0, 10, 42, 12, 20)]);
        var stack = StackSlicer.Slice(legs, new SliceOptions { ThicknessMm = 5, HeightMm = 20, PinMm = 2 });
        Assert.Equal(4, stack.Slices);
        // Joints: slice 1→2 (two legs), 2→3 (each leg to the body), 3→4 (body): pins in all of them.
        Assert.All(new[] { 1, 2, 3 }, j => Assert.Contains(stack.Pins, p => p.Slice == j));
        Assert.Contains(stack.Pins, p => p.Slice == 2 && p.At.X < 20);
        Assert.Contains(stack.Pins, p => p.Slice == 2 && p.At.X > 20);
        foreach (var (slice, at) in stack.Pins)
        {
            // The same hole in the part below and the part above.
            foreach (var s in new[] { slice, slice + 1 })
                Assert.Contains(stack.Parts.Where(p => p.Slice == s), p => p.Outline.Subpaths.Any(sp => sp.Flatten(0.05).Average(q => q.X) is var cx && Math.Abs(cx - at.X) < 0.05
                    && Math.Abs(sp.Flatten(0.05).Average(q => q.Y) - at.Y) < 0.05));
        }
        // No two holes in one part run into each other.
        foreach (var part in stack.Parts)
        {
            var centres = part.Outline.Subpaths.Skip(1).Select(sp => new Vec2(sp.Flatten(0.05).Average(q => q.X), sp.Flatten(0.05).Average(q => q.Y))).ToList();
            Assert.All(centres, a => Assert.All(centres.Where(b => b != a), b => Assert.True(a.Dist(b) > 3)));
        }
    }
}
