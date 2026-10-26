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
    public void EdgeShapingEngravesTheStepsDownToTheSlope()
    {
        // A pyramid 30 mm across and tall in 3 mm slices: its faces rise 2 mm for every 1 mm in.
        // Engraved 0.25 mm a pass, the bottom slice's top should go down 3 − 2x at x mm in from its edge.
        var o = new SliceOptions { ThicknessMm = 3, HeightMm = 30, Outline = SliceOutline.Outer, KeepJoined = false, ShapeStepMm = 0.25 };
        var stack = StackSlicer.Slice(Pyramid(30, 30), o);
        var bottom = stack.Parts.Single(p => p.Slice == 1);
        // (3 − 0.4 mm skin) ÷ 0.25: ten steps at most, each a smaller ring than the last.
        Assert.Equal(10, bottom.Shaping.Count);
        var areas = bottom.Shaping.Select(Area).ToList();
        Assert.True(areas.Zip(areas.Skip(1)).All(p => p.First > p.Second), string.Join(", ", areas));
        int Steps(double inset) => bottom.Shaping.Count(g => InsideAny(g, new Vec2(15, inset)));
        Assert.InRange(Steps(0.5), 7, 9);   // 2 mm down: 8 steps
        Assert.InRange(Steps(1.25), 1, 3);  // 0.5 mm down: 2 steps
        Assert.Equal(0, Steps(2.5));        // on top of the slice above: untouched
        Assert.Equal(10, Steps(0.1));       // near the edge: as deep as it goes, short of the skin
        // A pyramid only narrows going up: no underside to sand.
        Assert.True(bottom.UndersideMm2 < 5, $"underside {bottom.UndersideMm2}");

        // Laid out: the steps on their own engrave layer, one pass each, before the cut.
        var shaping = Layer.Default(1, LayerMode.Fill) with { Laser = LaserSettings.Blue(60, 200).WithPasses(4) };
        var (s, _) = StackLayout.Place(DocState.New(), stack, MachineProfile.F2Ultra, numbers: true, marks: true, shaping: shaping);
        var layer = s.Layers.Single(l => l.Name == StackLayout.ShapingLayer);
        Assert.Equal(LayerMode.Fill, layer.Mode);
        Assert.Equal(1, layer.Laser.Passes);
        Assert.True(s.Layers.FindIndex(l => l.Id == layer.Id) < s.Layers.FindIndex(l => l.Name == "Slices · cut"));
        Assert.Equal(stack.Parts.Sum(p => p.Shaping.Count), s.Shapes.Count(x => x.LayerId == layer.Id));
        // Steps moved with their part: the bottom slice's first step lies round its cut outline.
        var cut = s.Shapes.Single(x => x.Name == "Slice 1");
        var first = s.Shapes.Single(x => x.Name == "Slice 1 · step 1");
        Assert.True(cut.Bounds.Inflate(0.01).Contains(first.Bounds.TopLeft) && cut.Bounds.Inflate(0.01).Contains(first.Bounds.BottomRight));
        // Upside down, the pyramid's slices have their steps underneath, out of reach from above.
        var flipped = StackSlicer.Slice(Pyramid(30, 30), o with { Up = SliceUp.MinusZ });
        Assert.True(flipped.Parts.Single(p => p.Slice == 8).UndersideMm2 > 50, $"{flipped.Parts.Single(p => p.Slice == 8).UndersideMm2}");
        // Without shaping settings, no shaping layer.
        Assert.DoesNotContain(StackLayout.Place(DocState.New(), stack, MachineProfile.F2Ultra, true, true).State.Layers, l => l.Name == StackLayout.ShapingLayer);
    }

    /// <summary>Inside the shape, holes counted (even-odd over all its loops).</summary>
    static bool InsideAny(PathGeometry g, Vec2 p) => g.Flatten(0.01).Where(f => f.Closed).Count(f => Geom.Inside(f.Points, p)) % 2 == 1;

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
        var o = new SliceOptions { ThicknessMm = 3, HeightMm = 30, KeepJoined = false };
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
    public void PinsKeepClearOfTheDowels()
    {
        // Pins and dowels together: every pin hole leaves a wall of material next to each dowel hole.
        var drum = StackSlicer.Slice(Cylinder(15, 12), new SliceOptions { ThicknessMm = 3, HeightMm = 12, DowelMm = 4, PinMm = 3 });
        Assert.Equal(2, drum.Dowels.Count);
        Assert.NotEmpty(drum.Pins);
        foreach (var (_, at) in drum.Pins)
            foreach (var d in drum.Dowels)
                Assert.True(at.Dist(d) >= 4 / 2.0 + 3 / 2.0 + 1, $"pin at {at} is {at.Dist(d):0.00} mm from the dowel at {d}");
    }

    [Fact]
    public void SlicesAreLaidOutBedByBed()
    {
        var stack = StackSlicer.Slice(Cylinder(30, 120), new SliceOptions { ThicknessMm = 3, HeightMm = 120 });
        Assert.Equal(40, stack.Slices);
        var (s, sheets) = StackLayout.Place(DocState.New(), stack, MachineProfile.F2Ultra, numbers: true, marks: true);
        // Nested, not in rows: at least as tight as 3 × 3 discs a bed (40 slices = 5 beds).
        Assert.InRange(sheets, 3, 5);
        Assert.Equal(["Page 1", .. Enumerable.Range(2, sheets - 1).Select(i => $"Slices {i}")], Pages.Names(s));
        Assert.Equal(0, s.PageIndex);
        // Every slice once, across the pages.
        var texts = Enumerable.Range(0, sheets).SelectMany(i => Pages.SwitchTo(s, i).Shapes.OfType<TextShape>().Select(t => t.Text)).ToList();
        Assert.Equal(Enumerable.Range(1, 40).Select(i => StackSlicer.NotUpsideDown($"{i}")).Order(), texts.Order());
        Assert.Contains("6.", texts);
        for (int i = 0; i < sheets; i++)
        {
            var page = Pages.SwitchTo(s, i);
            // Each number stays inside its own slice (they moved together).
            Assert.All(page.Shapes.Where(x => x.GroupId is not null).GroupBy(x => x.GroupId), g => Assert.Equal(2, g.Count()));
            // Everything inside the bed, nothing overlapping (the discs keep the gap between them).
            var cuts = page.Shapes.OfType<PathShape>().Where(p => p.Name.StartsWith("Slice ") && p is not TextShape).ToList();
            Assert.All(cuts, c => Assert.True(c.Bounds.MinX >= 2.9 && c.Bounds.MinY >= 2.9 && c.Bounds.MaxX <= 217.1 && c.Bounds.MaxY <= 217.1));
            Assert.All(cuts, a => Assert.All(cuts.Where(b => b != a), b => Assert.True((a.Bounds.Center - b.Bounds.Center).Length >= 60 + 2.5)));
        }
        // A page with something on it already: the slices get pages of their own, named after the model.
        var busy = DocState.New();
        busy = Ops.AddShapes(busy, [new PathShape { LayerId = busy.Layers[0].Id, Geometry = PathGeometry.Rect(10, 10, 20, 20) }]);
        var (s2, n2) = StackLayout.Place(busy, stack, MachineProfile.F2Ultra, numbers: true, marks: true, title: "drum");
        Assert.Equal(["Page 1", "drum", .. Enumerable.Range(2, n2 - 1).Select(i => $"drum {i}")], Pages.Names(s2));
        Assert.Equal(1, s2.PageIndex);
        Assert.Single(Pages.SwitchTo(s2, 0).Shapes);
    }

    [Fact]
    public void SlicesFillTheMaterialOutline()
    {
        // A 150 × 100 board drawn on the page, with something already being cut at its corner.
        var s = DocState.New();
        var board = new PathShape { Name = "Board", LayerId = s.Layers[0].Id, Geometry = PathGeometry.Rect(40, 60, 150, 100) };
        var logo = new PathShape { Name = "Logo", LayerId = s.Layers[0].Id, Geometry = PathGeometry.Rect(45, 65, 30, 30) };
        s = Ops.AddShapes(s, [board, logo]);
        s = MaterialOutline.Mark(s, new HashSet<Guid> { board.Id });
        var material = Assert.Single(MaterialOutline.On(s));
        Assert.Equal(board.Id, material.Id);
        Assert.False(s.Layers.Single(l => l.Id == material.LayerId).Output); // never cut

        var stack = StackSlicer.Slice(Cylinder(20, 30), new SliceOptions { ThicknessMm = 3, HeightMm = 30 });
        Assert.Equal(10, stack.Parts.Count);
        var (placed, beds) = StackLayout.Place(s, stack, MachineProfile.F2Ultra, numbers: false, marks: false, material: material.World);
        Assert.True(beds >= 2); // 40 mm discs: only a few fit beside the logo on a 150 × 100 board
        // The first board is this page (around the logo); the others are new pages with the same outline.
        Assert.Equal(0, placed.PageIndex);
        int total = 0;
        for (int i = 0; i < beds; i++)
        {
            var page = Pages.SwitchTo(placed, i);
            var discs = page.Shapes.OfType<PathShape>().Where(x => x.Name.StartsWith("Slice ")).ToList();
            total += discs.Count;
            Assert.All(discs, d => Assert.True(d.Bounds.MinX >= 42.9 && d.Bounds.MaxX <= 187.1 && d.Bounds.MinY >= 62.9 && d.Bounds.MaxY <= 157.1));
            Assert.Single(MaterialOutline.On(page));
            if (i == 0) Assert.All(discs, d => Assert.False(d.Bounds.Intersects(new Rect2(45, 65, 75, 95))));
        }
        Assert.Equal(10, total);
    }

    [Fact]
    public void BigAndSmallPartsShareABed()
    {
        // Three big parts (two to a bed, turned) and twelve small ones in the room left over: two beds.
        var mesh = new List<Tri>();
        for (int i = 0; i < 3; i++) mesh.AddRange(Box(i * 200, 0, 0, i * 200 + 120, 100, 3));
        for (int i = 0; i < 12; i++) mesh.AddRange(Box(i * 30, 300, 0, i * 30 + 20, 330, 3));
        var stack = StackSlicer.Slice(mesh, new SliceOptions { ThicknessMm = 3, HeightMm = 3, KeepJoined = false, WidthMm = 0 });
        var (s, sheets) = StackLayout.Place(DocState.New(), stack, MachineProfile.F2Ultra, numbers: false, marks: false);
        Assert.Equal(15, Enumerable.Range(0, sheets).Sum(i => Pages.SwitchTo(s, i).Shapes.Count));
        Assert.Equal(2, sheets);
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

    /// <summary>A bar leaning over: square, <paramref name="w"/> across, shifted <paramref name="lean"/> along X from bottom to top.</summary>
    static List<Tri> LeaningBar(double x0, double w, double h, double lean)
    {
        V3 P(double x, double y, double z) => new(x + lean * z / h, y, z);
        return
        [
            .. Quad(P(x0, 0, 0), P(x0 + w, 0, 0), P(x0 + w, w, 0), P(x0, w, 0)), .. Quad(P(x0, 0, h), P(x0, w, h), P(x0 + w, w, h), P(x0 + w, 0, h)),
            .. Quad(P(x0, 0, 0), P(x0, 0, h), P(x0 + w, 0, h), P(x0 + w, 0, 0)), .. Quad(P(x0, w, 0), P(x0 + w, w, 0), P(x0 + w, w, h), P(x0, w, h)),
            .. Quad(P(x0, 0, 0), P(x0, w, 0), P(x0, w, h), P(x0, 0, h)), .. Quad(P(x0 + w, 0, 0), P(x0 + w, 0, h), P(x0 + w, w, h), P(x0 + w, w, 0)),
        ];
    }

    [Fact]
    public void SteepSlopesStayJoined()
    {
        // A 4 mm bar leaning 4 mm sideways per 2 mm slice: half-way sections of neighbouring slices miss each other.
        var bar = LeaningBar(0, 4, 20, 40);
        var o = new SliceOptions { ThicknessMm = 2, HeightMm = 20, KeepJoined = false };
        var loose = StackSlicer.Slice(bar, o);
        static double Overlap(SliceStack st, int a, int b) => Area(ShapeGeometry.Boolean(st.Layers[a], st.Layers[b], BoolOp.Intersect));
        Assert.True(Overlap(loose, 4, 5) < 0.5);
        var joined = StackSlicer.Slice(bar, o with { KeepJoined = true });
        Assert.True(joined.Joined >= 9);
        Assert.All(Enumerable.Range(0, 9), i => Assert.True(Overlap(joined, i, i + 1) >= 6, $"slices {i + 1} and {i + 2}"));
        Assert.DoesNotContain(joined.Parts, p => p.Floating);
    }

    [Fact]
    public void SeparatePiecesAreFlagged()
    {
        // A post and, beside it, a block floating in the air (a separate piece of the model).
        var mesh = new List<Tri>([.. Box(0, 0, 0, 20, 20, 30), .. Box(40, 0, 10, 50, 10, 20)]);
        var stack = StackSlicer.Slice(mesh, new SliceOptions { ThicknessMm = 3, HeightMm = 30 });
        var floating = stack.Parts.Where(p => p.Floating).ToList();
        Assert.NotEmpty(floating);
        Assert.All(floating, p => Assert.True(p.Outline.Bounds().MinX > 35));
        Assert.All(stack.Parts.Where(p => p.Outline.Bounds().MaxX < 25), p => Assert.False(p.Floating));
    }

    [Fact]
    public void PiecesStandingOnTheBaseAreFine()
    {
        // A head and separate body segments (a flexible fish), each standing on the base: none flagged.
        var mesh = new List<Tri>([.. Box(0, 0, 0, 20, 20, 30), .. Box(25, 0, 0, 35, 20, 24), .. Box(40, 0, 0, 50, 20, 18)]);
        var stack = StackSlicer.Slice(mesh, new SliceOptions { ThicknessMm = 3, HeightMm = 30 });
        Assert.DoesNotContain(stack.Parts, p => p.Floating);
    }

    [Fact]
    public void EachAxisCanBeSizedOnItsOwn()
    {
        // A 4 mm cylinder 10 mm tall, stretched to 8 mm across X and 2 mm along Y: an ellipse in every slice.
        var stack = StackSlicer.Slice(Cylinder(2, 10), new SliceOptions { ThicknessMm = 2, HeightMm = 10, WidthMm = 8, DepthMm = 2 });
        Assert.Equal(5, stack.Slices);
        var b = stack.Layers[2].Bounds();
        Assert.Equal(8, b.Width, 1);
        Assert.Equal(2, b.Height, 1);
        Assert.Equal((4.0, 4.0, 10.0), StackSlicer.NaturalSize(Cylinder(2, 10), SliceUp.Z, 0) is var n ? (Math.Round(n.X, 1), Math.Round(n.Y, 1), Math.Round(n.Z, 1)) : default);
    }

    static bool In(SlicePart p, Vec2 at) => p.Outline.Flatten(0.02).Where(f => f.Closed).Count(f => Geom.Inside(f.Points, at)) % 2 == 1;

    [Fact]
    public void EggCrateSheetsSlotTogether()
    {
        // A 40 × 30 × 20 block, 3 sheets across X and 2 across Y.
        var stack = StackSlicer.Slice(Box(0, 0, 0, 40, 30, 20), new SliceOptions { Method = SliceMethod.EggCrate, ThicknessMm = 3, HeightMm = 20, SheetsX = 3, SheetsY = 2, SlotFitMm = 0.1 });
        var xs = stack.Parts.Where(p => p.Label!.StartsWith('X')).ToList();
        var ys = stack.Parts.Where(p => p.Label!.StartsWith('Y')).ToList();
        Assert.Equal(3, xs.Count);
        Assert.Equal(2, ys.Count);
        // X sheets: 30 wide (the model's depth), 20 tall; Y sheets 40 wide.
        Assert.Equal(30, xs[0].Outline.Bounds().Width, 1);
        Assert.Equal(40, ys[0].Outline.Bounds().Width, 1);
        // Every crossing: the X sheet keeps only the bottom half there, the Y sheet only the top half (v runs down: top is −20).
        double[] xAt = [40.0 / 6, 20, 40 - 40.0 / 6], yAt = [7.5, 22.5];
        foreach (var x in xs)
            foreach (var y in yAt)
            {
                Assert.False(In(x, new Vec2(y, -18)));
                Assert.True(In(x, new Vec2(y, -2)));
            }
        foreach (var y in ys)
            foreach (var x in xAt)
            {
                Assert.True(In(y, new Vec2(x, -18)));
                Assert.False(In(y, new Vec2(x, -2)));
            }
        // Each slot says what goes in it.
        Assert.All(xs, x => Assert.Equal(["Y1", "Y2"], x.Notes.Select(n => n.Text).Order()));
        Assert.All(ys, y => Assert.Equal(3, y.Notes.Count));
    }

    [Fact]
    public void RadialFinsSlotOntoRings()
    {
        // A 40 mm round column 30 mm tall: 8 fins, 2 rings, a 6 mm hole up the middle.
        var stack = StackSlicer.Slice(Cylinder(20, 30), new SliceOptions { Method = SliceMethod.Radial, ThicknessMm = 3, HeightMm = 30, Fins = 8, Rings = 2, CoreMm = 6 });
        var fins = stack.Parts.Where(p => p.Label!.StartsWith("Fin")).ToList();
        var rings = stack.Parts.Where(p => p.Label!.StartsWith("Ring")).ToList();
        Assert.Equal(8, fins.Count);
        Assert.Equal(2, rings.Count);
        // A fin runs from the core (3 mm out) to the edge (20 mm), and is notched from its inner edge at each ring (heights 10 and 20).
        var b = fins[0].Outline.Bounds();
        Assert.Equal(3, b.MinX, 1);
        Assert.Equal(20, b.MaxX, 0);
        Assert.False(In(fins[0], new Vec2(5, -10)));
        Assert.True(In(fins[0], new Vec2(18, -10)));
        // Rings: the centre hole, and a slot from the outer edge in at each fin (so 1 + 8 holes and notches in all).
        Assert.False(In(rings[0], new Vec2(20, 20)));            // centre hole (axis at the model's middle)
        Assert.False(In(rings[0], new Vec2(20 + 18, 20)));       // fin 1's slot near the edge
        Assert.True(In(rings[0], new Vec2(20 + 5, 20)));         // whole near the middle
        Assert.Equal(8, rings[0].Notes.Count);
    }
    [Fact]
    public void EveryPartHasItsOwnLabel()
    {
        // Two legs under a body: the leg slices are in two pieces, lettered left to right.
        var legs = new List<Tri>([.. Box(0, 0, 0, 10, 10, 10), .. Box(30, 0, 0, 40, 10, 10), .. Box(0, 0, 10, 40, 10, 20)]);
        var stack = StackSlicer.Slice(legs, new SliceOptions { ThicknessMm = 5, HeightMm = 20, PinMm = 2 });
        var labels = stack.Parts.Select(p => p.Label).ToList();
        Assert.Equal(labels.Count, labels.Distinct().Count());
        Assert.Equal(["1a", "1b", "2a", "2b", "3", "4"], labels);
        Assert.True(stack.Parts.Single(p => p.Label == "1a").LabelAt.X < 20);
        // Read upside down, 6 is 9: a dot says which way up it is. 8 and 11 read the same either way.
        Assert.Equal("6.", StackSlicer.PartLabel(6, 0, 1));
        Assert.Equal("9.", StackSlicer.PartLabel(9, 0, 1));
        Assert.Equal("16.", StackSlicer.PartLabel(16, 0, 1));
        Assert.Equal("8", StackSlicer.PartLabel(8, 0, 1));
        Assert.Equal("11", StackSlicer.PartLabel(11, 0, 1));
        Assert.Equal("10", StackSlicer.PartLabel(10, 0, 1));
        Assert.Equal("6b", StackSlicer.PartLabel(6, 1, 2));
        Assert.Equal("3aa", StackSlicer.PartLabel(3, 26, 30));
        // On the bed, each part's name is its own too.
        var (s, _) = StackLayout.Place(DocState.New(), stack, MachineProfile.F2Ultra, numbers: true, marks: true);
        var names = s.Shapes.OfType<PathShape>().Where(sh => s.Layers.First(l => l.Id == sh.LayerId).Name == "Slices · cut").Select(sh => sh.Name).ToList();
        Assert.Equal(names.Count, names.Distinct().Count());
    }

    [Fact]
    public void TheBookletHasAStepForEverySlice()
    {
        var legs = new List<Tri>([.. Box(0, 0, 0, 10, 10, 10), .. Box(30, 0, 0, 40, 10, 10), .. Box(0, 0, 10, 40, 10, 20)]);
        var stack = StackSlicer.Slice(legs, new SliceOptions { ThicknessMm = 5, HeightMm = 20, PinMm = 2 });
        var o = new BookletOptions { Title = "Table", PinMm = 2 };
        // Cover, parts, two pages of two steps, finished.
        Assert.Equal(5, AssemblyBooklet.PageCount(stack, o));
        var path = Path.Combine(Path.GetTempPath(), $"booklet-{Guid.NewGuid():N}.pdf");
        try
        {
            Assert.Equal(5, AssemblyBooklet.SavePdf(stack, o, path));
            var head = File.ReadAllBytes(path);
            Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(head, 0, 4));
        }
        finally { File.Delete(path); }
        if (Environment.GetEnvironmentVariable("BOOKLET_OUT") is { Length: > 0 } dir)
        {
            var pawn = StackSlicer.Slice(Pyramid(40, 36), new SliceOptions { ThicknessMm = 3, HeightMm = 36, PinMm = 2 });
            var po = new BookletOptions { Title = "Pyramid", PinMm = 2 };
            for (int k = 0; k < AssemblyBooklet.PageCount(pawn, po); k++) File.WriteAllBytes(Path.Combine(dir, $"pyramid-{k + 1}.png"), AssemblyBooklet.RenderPng(pawn, po, k));
            AssemblyBooklet.SavePdf(pawn, po, Path.Combine(dir, "pyramid.pdf"));
            for (int k = 0; k < 5; k++) File.WriteAllBytes(Path.Combine(dir, $"table-{k + 1}.png"), AssemblyBooklet.RenderPng(stack, o, k));
            // Four legs, a body, a head, and a chin hanging under it (as in the guide's pictures).
            var animal = StackSlicer.Slice([.. Box(0, 0, 0, 6, 6, 12), .. Box(44, 0, 0, 50, 6, 12), .. Box(0, 14, 0, 6, 20, 12), .. Box(44, 14, 0, 50, 20, 12),
                .. Box(0, 0, 12, 50, 20, 27), .. Box(40, 5, 27, 60, 15, 39), .. Box(52, 5, 21, 60, 15, 27)], new SliceOptions { ThicknessMm = 3, HeightMm = 39, PinMm = 2 });
            var ao = new BookletOptions { Title = "Animal", PinMm = 2 };
            for (int k = 0; k < AssemblyBooklet.PageCount(animal, ao); k++) File.WriteAllBytes(Path.Combine(dir, $"animal-{k + 1}.png"), AssemblyBooklet.RenderPng(animal, ao, k, 1.2f));
        }
        Assert.Throws<ArgumentException>(() => AssemblyBooklet.PageCount(StackSlicer.Slice(Cylinder(20, 30), new SliceOptions { Method = SliceMethod.Radial, ThicknessMm = 3, HeightMm = 30 }), o));
    }

    [Fact]
    public void APartHangingFromTheSliceAboveIsBuiltOffToTheSide()
    {
        // A leg, a body on it, and a chin under the body's far end that doesn't reach the ground:
        // the chin's slice has nothing under it, so it starts a side build that joins under the body.
        var mesh = new List<Tri>([.. Box(0, 0, 0, 10, 10, 10), .. Box(0, 0, 10, 40, 10, 20), .. Box(28, 0, 5, 40, 10, 10)]);
        var stack = StackSlicer.Slice(mesh, new SliceOptions { ThicknessMm = 5, HeightMm = 20, KeepJoined = false });
        Assert.Equal(["1", "2a", "2b", "3", "4"], stack.Parts.Select(p => p.Label));
        var path = Path.Combine(Path.GetTempPath(), $"booklet-{Guid.NewGuid():N}.pdf");
        try
        {
            // Cover, parts, steps 1–2, steps 3–4, finished.
            Assert.Equal(5, AssemblyBooklet.SavePdf(stack, new BookletOptions { Title = "Chin" }, path));
            using var pdf = UglyToad.PdfPig.PdfDocument.Open(path);
            var text = string.Join(" ", pdf.GetPages().Select(pg => pg.Text));
            Assert.Contains("Side build A: b has nothing under it yet", text);
            Assert.DoesNotContain("1×", text);
            Assert.Contains("It joins the model in step 3.", text);
            Assert.Contains("Glue 3 onto side build A, inside the marked line on 2b.", text);
            Assert.Contains("Glue 3 (with side build A under it) onto slice 2", text);
        }
        finally { File.Delete(path); }
        // Two hanging branches on one shared base that hangs too: one side build, joining when the
        // model first reaches it (the body over its left branch), not two.
        var fork = new List<Tri>([.. Box(0, 0, 0, 10, 10, 15), .. Box(0, 0, 15, 60, 10, 20), .. Box(30, 0, 5, 50, 10, 8), .. Box(30, 0, 8, 34, 10, 15), .. Box(46, 0, 8, 50, 10, 15)]);
        var forked = StackSlicer.Slice(fork, new SliceOptions { ThicknessMm = 2.5, HeightMm = 20, KeepJoined = false });
        path = Path.Combine(Path.GetTempPath(), $"booklet-{Guid.NewGuid():N}.pdf");
        try
        {
            AssemblyBooklet.SavePdf(forked, new BookletOptions { Title = "Fork" }, path);
            using var pdf = UglyToad.PdfPig.PdfDocument.Open(path);
            var text = string.Join(" ", pdf.GetPages().Select(pg => pg.Text));
            Assert.Contains("Side build A", text);
            Assert.DoesNotContain("Side build B", text);
            Assert.Contains("onto side build A", text);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void TheStackIsNotAMirrorImageOfTheModel()
    {
        // On the bed Y runs towards the front, so a part near the model's back (high Y) lies near the
        // bed's top edge (low Y): seen from above the parts read as the model does.
        var mesh = new List<Tri>([.. Box(0, 0, 0, 40, 10, 3), .. Box(0, 30, 0, 10, 40, 3)]);
        var stack = StackSlicer.Slice(mesh, new SliceOptions { ThicknessMm = 3, HeightMm = 3, KeepJoined = false });
        var nub = stack.Parts.Single(p => p.Outline.Bounds().Width < 20).Outline.Bounds();
        var bar = stack.Parts.Single(p => p.Outline.Bounds().Width > 20).Outline.Bounds();
        Assert.True(nub.Center.Y < bar.Center.Y, $"{nub} {bar}");
    }
}
