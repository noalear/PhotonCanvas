using PhotonCanvas.Addon.ObjectJig;
using PhotonCanvas.Core;
using PhotonCanvas.Core.Imaging;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Geometry;

namespace PhotonCanvas.Tests;

/// <summary>The object jig maker add-on: pockets, the jig's pages, the design on every object, and finding the jig in a photo.</summary>
public class ObjectJigTests
{
    static readonly Rect2 Area = new(0, 0, 220, 160);

    /// <summary>A pen-like capsule, <paramref name="len"/> × <paramref name="dia"/> mm, its middle at <paramref name="c"/>, turned <paramref name="deg"/>.</summary>
    static PathGeometry Pen(Vec2 c, double deg, double len = 120, double dia = 12) =>
        PathGeometry.RoundRect(-len / 2, -dia / 2, len, dia, dia / 2, dia / 2).Transform(Affine.Rotate(deg).Then(Affine.Translate(c)));

    static Rect2 Measure(string t, TextStyle s) => new(-t.Length * s.SizeMm * 0.3, -s.SizeMm / 2, t.Length * s.SizeMm * 0.3, s.SizeMm / 2);

    static List<Vec2> Points(PathGeometry g) => g.Flatten(0.1).SelectMany(f => f.Points).ToList();

    static bool InsideAny(PathGeometry g, Vec2 p) => g.Flatten(0.05).Count(f => f.Points.Count > 2 && Geom.Inside(f.Points, p)) % 2 == 1;

    static double DistanceTo(PathGeometry g, Vec2 p) =>
        g.Flatten(0.05).Min(f => Enumerable.Range(0, f.Points.Count).Min(i => Geom.SegmentDistance(p, f.Points[i], f.Points[(i + 1) % f.Points.Count])));

    [Fact]
    public void Pocket_holds_the_object_with_the_gap_all_round_and_lies_straight()
    {
        var pen = Pen(new(100, 80), 30);
        var o = new JigOptions { GapMm = 0.8, RoundInsideMm = 0, Notches = JigNotches.None, MarkerMm = 10 };
        var plan = JigMaker.Plan(pen, o, Area, Measure);

        Assert.Single(plan.ObjectMoves);
        Assert.Equal(30, plan.ObjectAngleDeg, 1);
        var placed = pen.Transform(plan.ObjectMoves[0]);
        // Laid straight: the pen now runs left to right.
        var b = placed.Bounds(0.05);
        Assert.Equal(120, b.Width, 1);
        Assert.Equal(12, b.Height, 1);
        // Every point of the object is inside the pocket, the gap in from its edge.
        foreach (var p in Points(placed))
        {
            Assert.True(InsideAny(plan.Pockets, p));
            Assert.InRange(DistanceTo(plan.Pockets, p), 0.7, 0.9);
        }
        // The pocket is inside the jig, and the jig's back-left corner is cut off.
        Assert.True(plan.Board.Contains(plan.Pockets.Bounds(0.1)));
        Assert.False(plan.Corner.IsEmpty);
        Assert.False(InsideAny(plan.Outline, new Vec2(plan.Board.MinX + 2, plan.Board.MinY + 2)));
        Assert.True(InsideAny(plan.Outline, new Vec2(plan.Board.MaxX - 2, plan.Board.MinY + 4)));
        // Where the object lay: the jig's middle is the pen's.
        Assert.True(plan.Board.Center.Dist(new Vec2(100, 80)) < 1);
    }

    [Fact]
    public void Not_laid_straight_keeps_the_angle_and_notches_open_the_sides()
    {
        var pen = Pen(new(100, 80), 30);
        var plain = JigMaker.Plan(pen, new JigOptions { LayStraight = false, Notches = JigNotches.None }, Area, Measure);
        var notched = JigMaker.Plan(pen, new JigOptions { LayStraight = false, Notches = JigNotches.BothSides, NotchDiameterMm = 18 }, Area, Measure);
        Assert.Equal(30, JigMaker.Axis(pen.Transform(plain.ObjectMoves[0])).AngleDeg, 1);
        // Two notches of 18 mm add about two half-discs beyond the pen's 13 mm-wide pocket.
        double added = ShapeGeometry.Area(notched.Pockets) - ShapeGeometry.Area(plain.Pockets);
        Assert.InRange(added, 150, 320);
        // They sit across the middle: the point 12 mm off the pen's middle, across it, is in a notch.
        double a = 30 * Math.PI / 180;
        var across = new Vec2(-Math.Sin(a), Math.Cos(a));
        var mid = JigMaker.Axis(pen.Transform(notched.ObjectMoves[0])).Centre;
        Assert.True(InsideAny(notched.Pockets, mid + across * 12));
        Assert.True(InsideAny(notched.Pockets, mid - across * 12));
        Assert.False(InsideAny(plain.Pockets, mid + across * 12));
    }

    [Fact]
    public void A_batch_is_a_grid_of_separate_pockets_in_reading_order()
    {
        var pen = Pen(new(100, 80), 0, 60, 10);
        var o = new JigOptions { Columns = 3, Rows = 2, WallMm = 6, Notches = JigNotches.BothSides, NotchDiameterMm = 14 };
        var plan = JigMaker.Plan(pen, o, Area, Measure);
        Assert.Equal(6, plan.ObjectMoves.Count);
        Assert.Equal(6, JigMaker.Outers(plan.Pockets).Count);
        var centres = plan.ObjectMoves.Select(m => JigMaker.Axis(pen.Transform(m)).Centre).ToList();
        for (int i = 1; i < centres.Count; i++)
            Assert.True(centres[i].Y > centres[i - 1].Y + 1 || (Math.Abs(centres[i].Y - centres[i - 1].Y) < 0.01 && centres[i].X > centres[i - 1].X));
        // Pockets 60 + 2·(gap) long, a 6 mm wall between them.
        double pitch = centres[1].X - centres[0].X;
        Assert.InRange(pitch, 60 + 6, 60 + 6 + 3);
    }

    [Fact]
    public void Warns_when_it_runs_off_the_bed_and_when_the_pockets_are_too_deep()
    {
        var pen = Pen(new(100, 80), 0);
        var big = JigMaker.Plan(pen, new JigOptions { Columns = 3 }, Area, Measure);
        Assert.Contains(big.Warnings, w => w.Contains("bigger than the work area"));
        var deep = JigMaker.Plan(pen, new JigOptions { Build = JigBuild.StackedWithBase, SheetMm = 3, DepthMm = 15, ObjectHeightMm = 12 }, Area, Measure);
        Assert.Contains(deep.Warnings, w => w.Contains("deeper than the object"));
        var edge = JigMaker.Plan(Pen(new(10, 10), 0), new JigOptions(), Area, Measure);
        Assert.True(Area.Contains(edge.Board), "Where the object lay is kept on the work area.");
    }

    static (DocState State, Layer Layer, PathShape Logo) Design()
    {
        var s = DocState.New();
        var layer = s.Layers[0] with { Mode = LayerMode.Fill, Name = "Logo" };
        s = s with { Layers = [layer] };
        var logo = new PathShape { Name = "Logo", LayerId = layer.Id, Geometry = PathGeometry.Rect(90, 77, 20, 6) };
        return (Ops.AddShapes(s, [logo]), layer, logo);
    }

    [Fact]
    public void Make_adds_the_cut_page_and_the_objects_page_with_the_design_on_every_object()
    {
        var (s, layer, logo) = Design();
        var pen = Pen(new(100, 80), 0, 60, 10);
        var o = new JigOptions { Name = "Pen", Columns = 2, Rows = 1 };
        var plan = JigMaker.Plan(pen, o, Area, Measure);
        var made = JigMaker.Make(s, pen, o, [logo], Area, measure: Measure);

        Assert.Equal(["Page 1", "Pen jig · cut", "Pen jig · objects"], Pages.Names(made));
        Assert.Equal("Pen jig · cut", made.PageName);
        Assert.Equal([JigMaker.LabelLayer, JigMaker.PocketLayer, JigMaker.OutlineLayer], made.Layers.Select(l => l.Name));
        Assert.All(made.Layers, l => Assert.True(l.Output));

        var objects = Pages.SwitchTo(made, 2);
        var guide = Assert.Single(objects.Layers, l => l.Name == JigMaker.GuideLayer);
        Assert.False(guide.Output);
        var copies = objects.Shapes.Where(x => x.Name == "Logo").ToList();
        Assert.Equal(2, copies.Count);
        Assert.Equal([0, 1], copies.Select(c => c.VarOffset));
        // On the page's own copy of the design's layer, not the old page's.
        var pageLogo = Assert.Single(objects.Layers, l => l.Name == "Logo");
        Assert.NotEqual(layer.Id, pageLogo.Id);
        Assert.All(copies, c => Assert.Equal(pageLogo.Id, c.LayerId));
        // Each copy sits on its object as the design sat on the pen.
        for (int k = 0; k < 2; k++)
        {
            var want = logo.World.Bounds(0.1);
            var got = ((PathShape)copies[k]).World.Bounds(0.1);
            var expect = Rect2.Bounds([plan.ObjectMoves[k].Apply(want.TopLeft), plan.ObjectMoves[k].Apply(want.BottomRight)]);
            Assert.True(got.Center.Dist(expect.Center) < 0.05);
        }
        // The jig is drawn as a guide the camera can find again.
        var jig = JigMaker.GuideOn(objects);
        Assert.NotNull(jig);
        Assert.Equal(3, jig.ThicknessMm, 3);
        Assert.Null(JigMaker.GuideOn(made));
    }

    [Fact]
    public void Stacked_jig_has_a_top_the_other_sheets_and_a_base()
    {
        var (s, _, logo) = Design();
        var o = new JigOptions { Build = JigBuild.StackedWithBase, SheetMm = 3, DepthMm = 8, ObjectHeightMm = 14 };
        Assert.Equal(3, o.CutSheets);
        Assert.Equal(12, o.ThicknessMm, 3);
        var made = JigMaker.Make(s, Pen(new(100, 80), 0, 60, 10), o, [logo], Area, measure: Measure);
        Assert.Equal(["Page 1", "Jig · top sheet", "Jig · sheets (cut 2)", "Jig · base", "Jig · objects"], Pages.Names(made));
        var basePage = Pages.SwitchTo(made, 3);
        Assert.Contains(basePage.Layers, l => l.Name == JigMaker.MarksLayer && l.Mode == LayerMode.Score);
        Assert.DoesNotContain(basePage.Layers, l => l.Name == JigMaker.PocketLayer);
        var sheet = Pages.SwitchTo(made, 2);
        // Dowel holes in three corners, beside the outline.
        var edge = sheet.Shapes.Single(x => x.Name == "Jig");
        Assert.Equal(4, ((PathShape)edge).Geometry.Subpaths.Length);
        // Without the cut pages, only the objects page.
        var only = JigMaker.Make(s, Pen(new(100, 80), 0, 60, 10), o, [logo], Area, cutPages: false, measure: Measure);
        Assert.Equal(["Page 1", "Jig · objects"], Pages.Names(only));
    }

    [Fact]
    public void Design_centred_turns_along_the_object_and_fits_inside()
    {
        var pen = Pen(new(100, 80), 30, 100, 20);
        var square = PathGeometry.Rect(10, 10, 40, 40);
        var o = new JigOptions { Placement = DesignPlacement.Centred, FitDesign = true, FitMarginMm = 2 };
        var (move, warning) = JigMaker.DesignOnObject(pen, Points(square), o);
        Assert.Null(warning);
        var placed = square.Transform(move);
        foreach (var p in Points(placed)) Assert.True(InsideAny(pen, p));
        // The pen is 20 wide with a 2 mm margin: the square comes out about 16 mm, turned to 30°.
        double side = Math.Sqrt(ShapeGeometry.Area(placed));
        Assert.InRange(side, 14.5, 16.1);
        Assert.True(JigMaker.Axis(placed).Centre.Dist(new Vec2(100, 80)) < 0.2);

        var line = PathGeometry.Rect(0, 0, 50, 4);
        // Left to right along the pen, whatever the design's own shape.
        var (turn, _) = JigMaker.DesignOnObject(pen, Points(line), o with { FitDesign = false });
        Assert.Equal(30, JigMaker.Axis(line.Transform(turn)).AngleDeg, 1);
        var (asPlaced, _) = JigMaker.DesignOnObject(pen, Points(line), o with { Placement = DesignPlacement.AsPlaced });
        Assert.True(asPlaced.IsIdentity);
    }

    // ---- photos -----------------------------------------------------------------------------------

    const double MmPerPx = 0.4;

    /// <summary>A straightened bed photo: a dark honeycomb-ish bed, and light wood where <paramref name="wood"/> is (even-odd: pockets show the bed).</summary>
    static RasterImage Photo(PathGeometry wood, PathGeometry? objects = null)
    {
        int w = (int)(Area.Width / MmPerPx), h = (int)(Area.Height / MmPerPx);
        var rgba = new byte[w * h * 4];
        var polys = wood.Flatten(0.05).Where(f => f.Points.Count > 2).Select(f => f.Points).ToList();
        var objPolys = objects?.Flatten(0.05).Where(f => f.Points.Count > 2).Select(f => f.Points).ToList() ?? [];
        var rnd = new Random(7);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var p = new Vec2(Area.MinX + (x + 0.5) * MmPerPx, Area.MinY + (y + 0.5) * MmPerPx);
                int inside = polys.Count(poly => Geom.Inside(poly, p));
                byte r, g, b;
                if (objPolys.Any(poly => Geom.Inside(poly, p))) (r, g, b) = (30, 60, 160);
                else if (inside % 2 == 1) (r, g, b) = (214, 182, 132);
                else
                {
                    bool cell = (x % 15 == 0) || (y % 15 == 0);
                    (r, g, b) = cell ? ((byte)70, (byte)70, (byte)72) : ((byte)45, (byte)45, (byte)48);
                }
                int n = rnd.Next(-6, 7), i = (y * w + x) * 4;
                rgba[i] = (byte)Math.Clamp(r + n, 0, 255); rgba[i + 1] = (byte)Math.Clamp(g + n, 0, 255); rgba[i + 2] = (byte)Math.Clamp(b + n, 0, 255); rgba[i + 3] = 255;
            }
        return new RasterImage(w, h, rgba);
    }

    static JigShapes Shapes(JigPlan p) => new(p.Outline, p.Pockets, p.Corner, 3);

    [Theory]
    [InlineData(23, 25, -12)]
    [InlineData(170, -20, 10)]   // nearly upside down: the pen pocket alone can't tell, the cut-off corner must
    [InlineData(-95, 0, 0)]
    public void Finds_the_jig_anywhere_on_the_bed_any_way_round(double deg, double dx, double dy)
    {
        var pen = Pen(new(110, 80), 0, 70, 12);
        var plan = JigMaker.Plan(pen, Margin(12), Area, Measure);
        var truth = Affine.RotateAbout(plan.Board.Center, deg).Then(Affine.Translate(dx, dy));
        var wood = new PathGeometry([.. plan.Outline.Subpaths, .. plan.Pockets.Subpaths]).Transform(truth);
        var photo = Photo(wood);

        var found = JigFinder.Find(photo, Area, MmPerPx, Shapes(plan));
        Assert.True(found.Refined, $"edges matched only {found.OnEdge:P0}");
        Assert.True(found.CornerClear > 0.8);
        // The pocket and the design spot land where they really are, to well under a millimetre.
        foreach (var p in new[] { plan.Board.Center, plan.Board.TopLeft, plan.Board.BottomRight, JigMaker.Axis(pen.Transform(plan.ObjectMoves[0])).Centre + new Vec2(20, 0) })
            Assert.True(found.Move.Apply(p).Dist(truth.Apply(p)) < 0.6, $"{p}: {found.Move.Apply(p)} vs {truth.Apply(p)}");
    }

    static JigOptions Margin(double m) => new() { MarginMm = m };

    [Theory]
    [InlineData(92)]
    [InlineData(181)]
    [InlineData(-88)]
    public void Finds_a_squarish_batch_jig_turned_by_quarter_turns(double deg)
    {
        var pen = Pen(new(110, 80), 0, 40, 30);
        var plan = JigMaker.Plan(pen, new JigOptions { Columns = 2, Rows = 2, Notches = JigNotches.None }, Area, Measure);
        var truth = Affine.RotateAbout(plan.Board.Center, deg).Then(Affine.Translate(4, -3));
        var photo = Photo(new PathGeometry([.. plan.Outline.Subpaths, .. plan.Pockets.Subpaths]).Transform(truth));
        var found = JigFinder.Find(photo, Area, MmPerPx, Shapes(plan));
        foreach (var p in new[] { plan.Board.TopLeft, plan.Board.BottomRight, new Vec2(plan.Board.MaxX, plan.Board.MinY) })
            Assert.True(found.Move.Apply(p).Dist(truth.Apply(p)) < 0.6, $"{p}: {found.Move.Apply(p)} vs {truth.Apply(p)}");
    }

    [Fact]
    public void Finds_the_jig_with_the_objects_in_it()
    {
        var pen = Pen(new(110, 80), 0, 70, 12);
        var plan = JigMaker.Plan(pen, new JigOptions(), Area, Measure);
        var truth = Affine.RotateAbout(plan.Board.Center, 8).Then(Affine.Translate(-15, 6));
        var wood = new PathGeometry([.. plan.Outline.Subpaths, .. plan.Pockets.Subpaths]).Transform(truth);
        var photo = Photo(wood, plan.ObjectOutlines.Transform(truth));
        var found = JigFinder.Find(photo, Area, MmPerPx, Shapes(plan));
        foreach (var p in new[] { plan.Board.TopLeft, plan.Board.BottomRight })
            Assert.True(found.Move.Apply(p).Dist(truth.Apply(p)) < 0.8);
    }

    [Fact]
    public void No_jig_in_the_photo_says_so()
    {
        var plan = JigMaker.Plan(Pen(new(110, 80), 0, 70, 12), new JigOptions(), Area, Measure);
        var empty = Photo(PathGeometry.Empty);
        var e = Assert.Throws<InvalidOperationException>(() => JigFinder.Find(empty, Area, MmPerPx, Shapes(plan)));
        Assert.Contains("No jig", e.Message);
    }

    [Fact]
    public void Outlines_the_biggest_thing_on_the_bed_as_the_object()
    {
        var pen = Pen(new(120, 70), 35, 110, 14);
        var crumb = PathGeometry.Ellipse(30, 30, 4, 4);
        var photo = Photo(new PathGeometry([.. pen.Subpaths, .. crumb.Subpaths]));
        var things = JigMaker.FromMask(JigFinder.NotBed(photo, 18), photo.Width, photo.Height, Area, MmPerPx, 20);
        Assert.Equal(2, things.Count);
        var obj = things[0];
        Assert.InRange(ShapeGeometry.Area(obj) / ShapeGeometry.Area(pen), 0.93, 1.07);
        var (c, a, _) = JigMaker.Axis(obj);
        Assert.True(c.Dist(new Vec2(120, 70)) < 0.5);
        Assert.Equal(35, a, 1.5);
    }

    [Fact]
    public void Saved_jigs_come_back_as_they_were()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pc-jig-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new JigStore(dir);
            var pen = Pen(new(100, 80), 12);
            var o = new JigOptions { Build = JigBuild.StackedNoBase, Columns = 4, Rows = 2, GapMm = 0.9, Centre = new Vec2(110, 85), Name = "Pen" };
            store.Save(SavedJig.From("Pen / blue", pen, o));
            var back = Assert.Single(store.All());
            Assert.Equal("Pen / blue", back.Name);
            var bo = back.ApplyTo(new JigOptions());
            Assert.Equal(JigBuild.StackedNoBase, bo.Build);
            Assert.Equal(4, bo.Columns);
            Assert.Equal(0.9, bo.GapMm, 6);
            Assert.Equal(new Vec2(110, 85), bo.Centre);
            Assert.InRange(ShapeGeometry.Area(back.ObjectOutline()) / ShapeGeometry.Area(pen), 0.999, 1.001);
            store.Delete("Pen / blue");
            Assert.Empty(store.All());
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
