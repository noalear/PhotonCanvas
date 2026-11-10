using Clipper2Lib;
using PhotonCanvas.Addon.Boxes;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using Xunit;

namespace PhotonCanvas.Tests;

/// <summary>
/// The box and joinery maker: every style and lid makes whole pieces; put together, the pieces fill
/// the walls exactly once (fingers, tabs and halvings agree with their slots); a hinged lid clears the
/// box as it opens; bent walls and pieces cut to fit the bed join back into the shape they came from.
/// </summary>
public class BoxesTests
{
    const double Bed = 220;

    static BoxKit Make(BoxSpec s) => BoxMaker.Make(s, Bed, Bed);

    /// <summary>Is a point of the room inside a piece put together (its outline, its thickness), in any of its places.</summary>
    static bool Inside(Part p, V3 at, double t, Func<Pose, Pose>? move = null)
    {
        IEnumerable<(PathsD Area, Pose Pose)> places = p.Facets is { } f
            ? f.Select(x => (Geo.Intersect(p.Cut, x.Clip), x.Pose))
            : p.Poses.Select(pose => (p.Cut, pose));
        foreach (var (area, pose0) in places)
        {
            var pose = move is null ? pose0 : move(pose0);
            var (u, v, w) = pose.Local(at);
            if (Math.Abs(w) > t / 2) continue;
            int crossings = area.Count(path => In(path, u, v));
            if (crossings % 2 == 1) return true;
        }
        return false;
    }

    /// <summary>Even-odd test in full precision (Clipper's rounds to 0.01 mm, which puts points near an edge on it).</summary>
    static bool In(PathD path, double x, double y)
    {
        bool inside = false;
        for (int i = 0, k = path.Count - 1; i < path.Count; k = i++)
        {
            var a = path[i];
            var b = path[k];
            if ((a.y > y) != (b.y > y) && x < (b.x - a.x) * (y - a.y) / (b.y - a.y) + a.x) inside = !inside;
        }
        return inside;
    }

    static int Covering(BoxKit kit, V3 at, Func<Part, bool>? which = null) =>
        kit.Parts.Where(p => which?.Invoke(p) ?? true).Count(p => Inside(p, at, kit.ThicknessMm));

    public static TheoryData<BoxStyle, LidKind> Styles()
    {
        var d = new TheoryData<BoxStyle, LidKind>();
        foreach (var lid in Enum.GetValues<LidKind>()) d.Add(BoxStyle.Finger, lid);
        d.Add(BoxStyle.Rounded, LidKind.None);
        d.Add(BoxStyle.Rounded, LidKind.LiftOff);
        d.Add(BoxStyle.BendingLid, LidKind.None);
        return d;
    }

    [Theory]
    [MemberData(nameof(Styles))]
    public void EveryStyleAndLidMakesWholePiecesThatFitTheBed(BoxStyle style, LidKind lid)
    {
        var kit = Make(new BoxSpec { Style = style, Lid = lid });
        Assert.True(kit.Ok, string.Join("\n", kit.Findings.Select(f => f.Text)));
        foreach (var p in kit.Parts)
        {
            Assert.Equal(1, Geo.Pieces(p.Cut));
            var b = Geo.Bounds(p.Cut);
            Assert.True(BoxMaker.Fits(b.right - b.left, b.bottom - b.top, Bed, Bed), $"{p.Name} is {b.right - b.left:0} × {b.bottom - b.top:0}");
        }
        Assert.Equal(lid != LidKind.None || style == BoxStyle.BendingLid, kit.Parts.Any(p => p.LidArea is not null));
    }

    [Theory]
    [InlineData(LidKind.None)]
    [InlineData(LidKind.LiftOff)]
    [InlineData(LidKind.Sliding)]
    [InlineData(LidKind.Hinged)]
    public void TheWallsAndBottomFillEveryCornerExactlyOnce(LidKind lid)
    {
        var spec = new BoxSpec { WidthMm = 90, DepthMm = 64, HeightMm = 40, ThicknessMm = 3, Lid = lid, FingerNotch = false };
        var kit = Make(spec);
        Assert.True(kit.Ok);
        double t = 3, Wo = 96, Do = 70, top = t + 40;
        var rnd = new Random(7);
        int checkedPoints = 0;
        for (int i = 0; i < 20000; i++)
        {
            var p = new V3(rnd.NextDouble() * Wo, rnd.NextDouble() * top, rnd.NextDouble() * Do);
            bool inCavity = p.X > t && p.X < Wo - t && p.Y > t && p.Z > t && p.Z < Do - t;
            if (inCavity) continue;
            // The pin holes in the sides are meant to be empty.
            if (lid == LidKind.Hinged && (p.X < t || p.X > Wo - t) && (p.Y - kit.Lid.Through.Y) * (p.Y - kit.Lid.Through.Y) + (p.Z - kit.Lid.Through.Z) * (p.Z - kit.Lid.Through.Z) < 4) continue;
            int n = Covering(kit, p, q => q.Group == 0);
            Assert.True(n == 1, $"{n} pieces at ({p.X:0.00}, {p.Y:0.00}, {p.Z:0.00})");
            checkedPoints++;
        }
        Assert.True(checkedPoints > 3000, $"{checkedPoints} points");
    }

    [Fact]
    public void DividersSlotTogetherWithoutAnyPieceInAnother()
    {
        var kit = Make(new BoxSpec { Lid = LidKind.LiftOff, DividersAcross = 2, DividersAlong = 2 });
        Assert.True(kit.Ok);
        Assert.Equal(2, kit.Parts.Single(p => p.Name == "Divider, front to back").Copies);
        var o = kit.Outside;
        var rnd = new Random(3);
        for (int i = 0; i < 30000; i++)
        {
            var p = new V3(rnd.NextDouble() * o.X, rnd.NextDouble() * o.Y, rnd.NextDouble() * o.Z);
            Assert.True(Covering(kit, p) <= 1, $"two pieces at ({p.X:0.00}, {p.Y:0.00}, {p.Z:0.00})");
        }
        // Every divider's tabs reach through its slots: the wall has holes where a divider passes.
        var front = kit.Parts.Single(p => p.Name == "Front");
        Assert.True(front.Cut.Count >= 3, "the front has the dividers' slots");
    }

    [Fact]
    public void AHingedLidSwingsOpenWithoutHittingTheBox()
    {
        foreach (var (pin, t, depth) in new[] { (3.0, 3.0, 70.0), (1.75, 3.0, 50.0), (4.0, 6.0, 120.0) })
        {
            // With dividers: the lid's back swings down into the box, over them.
            var kit = Make(new BoxSpec { Lid = LidKind.Hinged, PinMm = pin, ThicknessMm = t, DepthMm = depth, FingerNotch = false, DividersAcross = 1, DividersAlong = 1 });
            Assert.True(kit.Ok, string.Join("\n", kit.Findings.Select(f => f.Text)));
            var lid = kit.Parts.Where(p => p.Group == 1).ToList();
            var box = kit.Parts.Where(p => p.Group == 0).ToList();
            Assert.Contains(lid, p => p.Name == "Hinge cheek" && p.Copies == 2);
            Assert.Contains(box, p => p.Name.StartsWith("Divider"));
            // The cheeks' tabs go through slots in the lid, not notches in its edge.
            Assert.True(kit.Parts.Single(p => p.Name == "Lid").Cut.Count >= 5, "the lid has the cheeks' slots");
            // Points all over the lid and its cheeks, turned step by step to fully open.
            var pts = new List<(Part Part, V3 At)>();
            var rnd = new Random(5);
            foreach (var p in lid)
            {
                var b = Geo.Bounds(p.Cut);
                foreach (var pose in p.Poses)
                    for (int i = 0; i < 1500; i++)
                    {
                        double u = b.left + rnd.NextDouble() * (b.right - b.left), v = b.top + rnd.NextDouble() * (b.bottom - b.top), w = (rnd.NextDouble() - 0.5) * t * 0.98;
                        if (Inside(p, pose.At(u, v, w), t)) pts.Add((p, pose.At(u, v, w)));
                    }
            }
            for (double a = 0; a <= 1.0001; a += 0.05)
            {
                var turn = kit.Lid.Apply(new Pose(default, V3.UnitX, V3.UnitY), a);
                foreach (var (_, at) in pts)
                {
                    // The same turn as the lid's, applied to the point.
                    var moved = kit.Lid.Through + Rotate(at - kit.Lid.Through, kit.Lid.Axis, kit.Lid.Degrees * a);
                    var hit = box.FirstOrDefault(q => Inside(q, moved, t));
                    Assert.True(hit is null, $"pin {pin}, sheet {t}: at {a * kit.Lid.Degrees:0}° the lid hits the {hit?.Name}");
                }
            }
        }
    }

    static V3 Rotate(V3 v, V3 axis, double deg)
    {
        var k = axis.Norm();
        double r = deg * Math.PI / 180, c = Math.Cos(r), s = Math.Sin(r);
        return v * c + k.Cross(v) * s + k * (k.Dot(v) * (1 - c));
    }

    [Fact]
    public void TheHingeGapIsSmallAndClearsTheBack()
    {
        foreach (var t in new[] { 2.0, 3.0, 4.0, 6.0 })
        {
            var h = BoxMaker.HingeGeometry(t, 3, 80, 45);
            double b = t + h.Drop;
            // The lid's back top corner never comes nearer the pin than the back's inside (less the play).
            Assert.True(Math.Sqrt((h.D - h.Gap) * (h.D - h.Gap) + b * b) <= h.D - BoxMaker.HingePlay + 1e-9, $"sheet {t}");
            // The lid's back doesn't swing far down into the box.
            Assert.True(h.D <= Math.Max(b + 1.5, (45 - h.Drop) * 0.6), $"sheet {t}: pin {h.D:0.0} from the back");
            if (t <= 4) Assert.True(h.Gap <= 2.6, $"sheet {t}: gap {h.Gap:0.00}");
        }
    }

    [Fact]
    public void ARoundedBoxsWallGoesRoundTheBottomAndRejoinsAtItsSeams()
    {
        var spec = new BoxSpec { Style = BoxStyle.Rounded, WidthMm = 100, DepthMm = 70, HeightMm = 40, CornerRadiusMm = 15, ThicknessMm = 3, Lid = LidKind.None };
        var kit = Make(spec);
        Assert.True(kit.Ok);
        var walls = kit.Parts.Where(p => p.Name.StartsWith("Wall")).ToList();
        Assert.True(walls.Count >= 2, "longer than the bed: cut in pieces");
        double t = 3, rn = 15 + t / 2, L = 2 * (100 - 30) + 2 * (70 - 30) + 2 * Math.PI * rn, Hs = t + 40;
        // The pieces don't overlap, and together they're the whole wall (the tails past one end fill the sockets at the other).
        double sum = walls.Sum(w => Geo.Area(w.Cut));
        var union = Geo.Union(new PathsD(walls.SelectMany(w => w.Cut)));
        Assert.Equal(sum, Geo.Area(union), 3);
        var bottom = kit.Parts.Single(p => p.Name == "Bottom");
        double tabs = Geo.Area(bottom.Cut) - Geo.Area([BoxMaker.RoundRect(t, t, 100 + t, 70 + t, 15)]);
        Assert.Equal(L * Hs - tabs, sum, 0);
        Assert.All(walls, w => Assert.NotEmpty(w.Slits));
        // Bent round, the wall's outside is where the box's outside is.
        var rnd = new Random(1);
        for (int i = 0; i < 4000; i++)
        {
            var p = new V3(rnd.NextDouble() * 106, rnd.NextDouble() * Hs, rnd.NextDouble() * 76);
            Assert.True(Covering(kit, p) <= 1, $"two pieces at ({p.X:0.00}, {p.Y:0.00}, {p.Z:0.00})");
        }
        foreach (var at in new[] { new V3(70, Hs / 2, 1.5), new V3(53, Hs / 2, 76 - 1.5), new V3(1.5, Hs / 2, 38), new V3(106 - 1.5, Hs / 2, 38) })
            Assert.Equal(1, Covering(kit, at, p => p.Name.StartsWith("Wall")));
    }

    [Fact]
    public void ABendingLidLiesOnTopAndItsBackIsJointed()
    {
        var kit = Make(new BoxSpec { Style = BoxStyle.BendingLid, WidthMm = 100, DepthMm = 70, HeightMm = 45, BendRadiusMm = 10 });
        Assert.True(kit.Ok);
        var strip = kit.Parts.Single(p => p.Name == "Back and lid");
        Assert.NotEmpty(strip.Slits);
        Assert.NotNull(strip.LidArea);
        double t = 3, Ho = kit.Outside.Y;
        // The middle of the lid is on top, and the back's middle at the back.
        Assert.True(Inside(strip, new V3(53, Ho - t / 2, 60), t));
        Assert.True(Inside(strip, new V3(53, 20, t / 2), t));
        // And nothing else is there.
        Assert.Equal(1, Covering(kit, new V3(53, Ho - t / 2, 60)));
    }

    [Theory]
    [InlineData(JoinKind.Dovetail)]
    [InlineData(JoinKind.Jigsaw)]
    [InlineData(JoinKind.Fingers)]
    [InlineData(JoinKind.Straight)]
    public void SplitPiecesFitBackTogether(JoinKind kind)
    {
        var shape = new PathsD { BoxMaker.RoundRect(0, 0, 500, 120, 10) };
        var pieces = SplitWindow.Cut(shape, true, 0, new JoinStyle(kind, 8, 0), 3, 216);
        Assert.Equal(3, pieces.Count);
        Assert.All(pieces, p => Assert.Equal(1, Geo.Pieces(p)));
        Assert.All(pieces, p => Assert.True(Geo.Bounds(p).Width <= 216 + 1e-6));
        Assert.Equal(Geo.Area(shape), pieces.Sum(Geo.Area), 2);
        Assert.Equal(Geo.Area(shape), Geo.Area(Geo.Union(new PathsD(pieces.SelectMany(p => p)))), 2);
        if (kind != JoinKind.Straight)
        {
            // Tails: the first piece reaches past the seam.
            Assert.True(Geo.Bounds(pieces[0]).right > 500.0 / 3 + 7);
        }
        // With play, there's a gap between them.
        var loose = SplitWindow.Cut(shape, true, 0, new JoinStyle(kind, 8, 0.2), 3, 216);
        Assert.True(loose.Sum(Geo.Area) < Geo.Area(shape) - 10);
        Assert.Equal(loose.Sum(Geo.Area), Geo.Area(Geo.Union(new PathsD(loose.SelectMany(p => p)))), 2);
    }

    [Fact]
    public void DovetailsLockAndStayClearOfTheEdges()
    {
        var style = new JoinStyle(JoinKind.Dovetail, 8, 0);
        var centres = Joins.TailCentres(style, [(0, 100)], 5);
        Assert.NotEmpty(centres);
        var seam = Joins.Seam(style, 0, 0, 100, centres);
        // Wider at the tip than at the root.
        double root = seam.Where(p => p.x == 0).Select(p => p.y).Where(y => y > 0 && y < 100).Order().Take(2).Aggregate((a, b) => b - a);
        double tip = seam.Where(p => Math.Abs(p.x - 8) < 1e-9).Select(p => p.y).Order().Take(2).Aggregate((a, b) => b - a);
        Assert.True(tip > root + 2);
        Assert.True(seam.Where(p => p.x > 0).All(p => p.y >= 5 - 1e-9 && p.y <= 95 + 1e-9));
    }

    [Fact]
    public void ABoxBiggerThanTheBedIsCutInPiecesThatFit()
    {
        var kit = Make(new BoxSpec { WidthMm = 300, DepthMm = 120, HeightMm = 60 });
        Assert.True(kit.Ok, string.Join("\n", kit.Findings.Select(f => f.Text)));
        Assert.Contains(kit.Parts, p => p.Name.StartsWith("Front (1 of"));
        Assert.All(kit.Parts, p => { var b = Geo.Bounds(p.Cut); Assert.True(BoxMaker.Fits(b.right - b.left, b.bottom - b.top, Bed, Bed), p.Name); });
        var tooWide = Make(new BoxSpec { WidthMm = 300, DepthMm = 300, HeightMm = 60 });
        Assert.False(tooWide.Ok);
    }

    [Fact]
    public void OutsideSizesComeOutThatSize()
    {
        foreach (var lid in Enum.GetValues<LidKind>())
        {
            var kit = Make(new BoxSpec { Outside = true, WidthMm = 120, DepthMm = 80, HeightMm = 60, Lid = lid });
            Assert.Equal(120, kit.Outside.X, 6);
            Assert.Equal(80, kit.Outside.Z, 6);
            Assert.Equal(60, kit.Outside.Y, 6);
        }
    }

    [Fact]
    public void TheSettingsRoundTrip()
    {
        var s = new BoxSpec { Style = BoxStyle.Rounded, Lid = LidKind.Sliding, Border = true, BorderDesign = "Classic gilt", SeamJoin = JoinKind.Jigsaw, DividersAcross = 3 };
        Assert.Equal(s, BoxSpec.FromJson(s.ToJson()));
        Assert.Null(BoxSpec.FromJson("not json"));
    }

    [Fact]
    public void PuttingABoxInNestsItsPiecesAndCanReplaceThem()
    {
        var machine = MachineProfile.F2Ultra;
        var spec = new BoxSpec { Lid = LidKind.Hinged };
        var kit = BoxMaker.Make(spec, machine.FieldW, machine.FieldH);
        var (s, sheets, id, _) = Placement.Place(DocState.New(), kit, spec, machine, new Placement.Options());
        Assert.True(sheets >= 1);
        Assert.Equal(kit.PieceCount, Placement.CountEverywhere(s, id));
        var cut = s.Layers.Single(l => l.Name == Placement.CutLayer);
        Assert.All(s.Shapes.Where(x => Placement.ShapesOf(s, id).Contains(x.Id)), x => Assert.Equal(cut.Id, x.LayerId));
        var found = Placement.Find(s, [s.Shapes.First().Id]);
        Assert.NotNull(found);
        Assert.Equal(spec, found!.Value.Spec);
        var changed = spec with { Lid = LidKind.LiftOff };
        var (s2, _, id2, _) = Placement.Place(s, BoxMaker.Make(changed, machine.FieldW, machine.FieldH), changed, machine, new Placement.Options { Replace = id });
        Assert.Equal(id, id2);
        Assert.Equal(BoxMaker.Make(changed, machine.FieldW, machine.FieldH).PieceCount, Placement.CountEverywhere(s2, id));
    }

    [Fact]
    public void ACarvedBorderLiesOverTheLid()
    {
        var machine = MachineProfile.F2Ultra;
        var spec = new BoxSpec { Lid = LidKind.LiftOff, Border = true, BorderDesign = "", BorderWidthMm = 8, BorderInsetMm = 4, WidthMm = 90, DepthMm = 60 };
        var kit = BoxMaker.Make(spec, machine.FieldW, machine.FieldH);
        var (s, _, id, note) = Placement.Place(DocState.New(), kit, spec, machine, new Placement.Options());
        Assert.NotNull(note);
        var picture = Assert.Single(s.Shapes.OfType<ImageShape>());
        var lid = s.Shapes.OfType<PathShape>().Single(x => x.Name == "Box · lid");
        var pb = picture.Bounds;
        var lb = lid.Bounds;
        Assert.Equal(lb.MinX + 4, pb.MinX, 1);
        Assert.Equal(lb.MaxX - 4, pb.MaxX, 1);
        Assert.Equal(lb.MinY + 4, pb.MinY, 1);
        Assert.Equal(lb.MaxY - 4, pb.MaxY, 1);
        // Carved before the pieces are cut.
        var layers = s.Layers.Select(l => l.Id).ToList();
        Assert.True(layers.IndexOf(picture.LayerId) < layers.IndexOf(s.Layers.Single(l => l.Name == Placement.CutLayer).Id));
        Assert.Contains(id, Placement.ShapesOf(s, id).Select(_ => id));
    }
}
