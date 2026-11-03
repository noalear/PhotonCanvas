using Clipper2Lib;
using PhotonCanvas.Addon.Furniture;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;

namespace PhotonCanvas.Tests;

public class FurnitureTests
{
    static readonly FurnitureSet Sheet = new() { Items = [], ThicknessMm = 2, FitMm = 0.1 };

    static ItemKit Make(FurnitureItem it, FurnitureSet? set = null) => FurnitureKit.Make(it, set ?? Sheet);
    static Part Part(ItemKit k, string name) => k.Parts.Single(p => p.Name == name);

    public static IEnumerable<object[]> Kinds() => Defaults.Kinds.Select(k => new object[] { k.Kind });

    /// <summary>Every piece where it sits once put together, with its outline shrunk a hair so touching faces don't count.</summary>
    sealed record Placed(string Name, Pose Pose, PathsD Shrunk, PathsD Cut, double T)
    {
        public bool Inside(V3 p, double eps = 0.03)
        {
            var (u, v, w) = Pose.Local(p);
            if (Math.Abs(w) > T / 2 - eps) return false;
            return In(Shrunk, u, v);
        }

        public (V3 Min, V3 Max) Box()
        {
            var b = Geo.Bounds(Cut);
            var pts = new List<V3>();
            foreach (var u in new[] { b.left, b.right })
                foreach (var v in new[] { b.top, b.bottom })
                    foreach (var w in new[] { -T / 2, T / 2 })
                        pts.Add(Pose.At(u, v, w));
            return (new(pts.Min(q => q.X), pts.Min(q => q.Y), pts.Min(q => q.Z)), new(pts.Max(q => q.X), pts.Max(q => q.Y), pts.Max(q => q.Z)));
        }
    }

    static bool In(PathsD area, double u, double v) => OutlineGrid.In(area, u, v);

    static List<Placed> Assemble(ItemKit k, double T) =>
        [.. k.Parts.SelectMany(p => p.Poses.Select(pose => new Placed(p.Name, pose, Geo.Offset(p.Cut, -0.03), p.Cut, T)))];

    /// <summary>Places where two pieces would be in the same space once put together (sampled every 0.1 mm where their boxes meet).</summary>
    static List<string> Clashes(List<Placed> all)
    {
        var found = new List<string>();
        for (int i = 0; i < all.Count; i++)
            for (int j = i + 1; j < all.Count; j++)
            {
                var (a0, a1) = all[i].Box();
                var (b0, b1) = all[j].Box();
                double x0 = Math.Max(a0.X, b0.X), y0 = Math.Max(a0.Y, b0.Y), z0 = Math.Max(a0.Z, b0.Z);
                double x1 = Math.Min(a1.X, b1.X), y1 = Math.Min(a1.Y, b1.Y), z1 = Math.Min(a1.Z, b1.Z);
                if (x1 - x0 < 0.06 || y1 - y0 < 0.06 || z1 - z0 < 0.06) continue;
                const double step = 0.1;
                int hits = 0;
                V3 first = default;
                for (double x = x0 + step / 2; x < x1; x += step)
                    for (double y = y0 + step / 2; y < y1; y += step)
                        for (double z = z0 + step / 2; z < z1; z += step)
                        {
                            var p = new V3(x, y, z);
                            if (all[i].Inside(p) && all[j].Inside(p)) { if (hits++ == 0) first = p; }
                        }
                if (hits > 0) found.Add($"{all[i].Name} and {all[j].Name}: {hits} points, first at {first.X:0.##}, {first.Y:0.##}, {first.Z:0.##}");
            }
        return found;
    }

    /// <summary>Slot-like holes (one side as wide as the sheet plus the fit) with no other piece through their middle.</summary>
    static List<string> EmptySlots(List<Placed> all, double T, double fit)
    {
        var empty = new List<string>();
        foreach (var a in all)
            foreach (var hole in Geo.Tidy(a.Cut).Where(p => Clipper.Area(p) < 0))
            {
                var b = Clipper.GetBounds(hole);
                double w = b.right - b.left, h = b.bottom - b.top;
                bool slot = Math.Abs(Math.Min(w, h) - (T + fit)) < 0.02 || Math.Abs(Math.Max(w, h) - (T + fit)) < 0.02;
                if (!slot) continue;
                var c = a.Pose.At((b.left + b.right) / 2, (b.top + b.bottom) / 2);
                if (!all.Any(o => !ReferenceEquals(o, a) && o.Inside(c, 0.01))) empty.Add($"{a.Name}: slot at {(b.left + b.right) / 2:0.##}, {(b.top + b.bottom) / 2:0.##}");
            }
        return empty;
    }

    /// <summary>Pieces joined to the rest (their boxes overlap another's by nearly a sheet's thickness every way: a tab in a slot).</summary>
    static bool AllJoined(List<Placed> all, double T)
    {
        // A loose lid and its rim just sit on top.
        all = [.. all.Where(p => p.Name is not ("Lid rim" or "Lid rail" or "Lid") || !all.Any(q => q.Name is "Lid rim" or "Lid rail"))];
        if (all.Count < 2) return true;
        bool Joined(Placed a, Placed b)
        {
            var (a0, a1) = a.Box();
            var (b0, b1) = b.Box();
            double dx = Math.Min(a1.X, b1.X) - Math.Max(a0.X, b0.X), dy = Math.Min(a1.Y, b1.Y) - Math.Max(a0.Y, b0.Y), dz = Math.Min(a1.Z, b1.Z) - Math.Max(a0.Z, b0.Z);
            return Math.Min(dx, Math.Min(dy, dz)) > T * 0.4;
        }
        var seen = new HashSet<int> { 0 };
        var queue = new Queue<int>([0]);
        while (queue.Count > 0)
        {
            int i = queue.Dequeue();
            for (int j = 0; j < all.Count; j++)
                if (!seen.Contains(j) && Joined(all[i], all[j])) { seen.Add(j); queue.Enqueue(j); }
        }
        return seen.Count == all.Count;
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void Every_kind_makes_whole_pieces_that_fit_together_without_clashing(ItemKind kind)
    {
        var k = Make(Defaults.For(kind));
        Assert.True(k.Ok, string.Join("\n", k.Findings));
        Assert.NotEmpty(k.Parts);
        Assert.All(k.Parts, p => Assert.Equal(1, Geo.Pieces(p.Cut)));
        Assert.All(k.Parts, p => Assert.NotEmpty(p.Poses));
        var all = Assemble(k, 2);
        Assert.Empty(Clashes(all));
        Assert.Empty(EmptySlots(all, 2, 0.1));
        if (kind != ItemKind.WallShelf) Assert.True(AllJoined(all, 2), "a piece isn't joined to the others");
        if (kind != ItemKind.WallShelf) Assert.DoesNotContain(k.Findings, f => f.Level == Severity.Warn);
    }

    public static IEnumerable<object[]> Variants()
    {
        foreach (var s in new[] { LegStyle.Legs, LegStyle.Slab, LegStyle.Trestle, LegStyle.Round })
            foreach (var rails in new[] { false, true })
                yield return [Defaults.For(ItemKind.Table) with { Legs = s, Stretcher = rails, WidthMm = s == LegStyle.Round ? 20 : 30 }, 2.0];
        yield return [Defaults.For(ItemKind.Bench) with { Stretcher = false }, 2.0];
        yield return [Defaults.For(ItemKind.Bench) with { Legs = LegStyle.Trestle }, 3.0];
        yield return [Defaults.For(ItemKind.Stool) with { Top = TopShape.Square }, 1.5];
        yield return [Defaults.For(ItemKind.Table) with { WidthMm = 45, DepthMm = 22, HeightMm = 20 }, 3.0];
        foreach (var b in new[] { BackStyle.Panel, BackStyle.Slats, BackStyle.Ladder })
            yield return [Defaults.For(ItemKind.Chair) with { Back = b, WidthMm = 12, HeightMm = 20 }, 1.5];
        yield return [Defaults.For(ItemKind.Chair) with { WidthMm = 11, DepthMm = 11, SeatMm = 9, HeightMm = 20 }, 3.0];
        yield return [Defaults.For(ItemKind.Bookshelf) with { BackPanel = false, Shelves = 1 }, 2.0];
        yield return [Defaults.For(ItemKind.Bookshelf) with { WidthMm = 30, DepthMm = 12, HeightMm = 40, Shelves = 4 }, 3.0];
        yield return [Defaults.For(ItemKind.Chest) with { Lid = LidStyle.Flat }, 2.0];
        yield return [Defaults.For(ItemKind.Chest) with { Lid = LidStyle.None, Bands = false }, 1.5];
        yield return [Defaults.For(ItemKind.Crate) with { Lid = LidStyle.None }, 3.0];
        yield return [Defaults.For(ItemKind.Crate) with { WidthMm = 30, DepthMm = 20, HeightMm = 15 }, 2.0];
        yield return [Defaults.For(ItemKind.Bed) with { Bedding = false, WidthMm = 40, DepthMm = 22, MattressMm = 8 }, 3.0];
        foreach (var shape in new[] { DoorShape.Rectangle, DoorShape.Arched, DoorShape.Pointed })
            foreach (var hinge in new[] { HingeSide.Left, HingeSide.Right })
                yield return [Defaults.For(ItemKind.WallDoor) with { Shape = shape, Hinge = hinge, WidthMm = 18, HeightMm = 32 }, 2.0];
        yield return [Defaults.For(ItemKind.WallDoor) with { WallMm = 6 }, 3.0];
    }

    [Theory]
    [MemberData(nameof(Variants))]
    public void Other_styles_and_sheets_fit_together_too(FurnitureItem it, double t)
    {
        var set = Sheet with { ThicknessMm = t };
        var k = Make(it, set);
        Assert.True(k.Ok, string.Join("\n", k.Findings));
        Assert.All(k.Parts, p => Assert.Equal(1, Geo.Pieces(p.Cut)));
        var all = Assemble(k, t);
        Assert.Empty(Clashes(all));
        Assert.Empty(EmptySlots(all, t, 0.1));
        Assert.True(AllJoined(all, t), "a piece isn't joined to the others");
    }

    static readonly FurnitureSet Push = Sheet with { PushFit = true, GripMm = 0.15 };

    /// <summary>The pieces without their grip ribs, to check the rest still fits.</summary>
    static ItemKit WithoutRibs(ItemKit k) => k with
    {
        Parts = [.. k.Parts.Select(p => p with { Cut = Geo.Subtract(p.Cut, Geo.Offset(p.Ribs, 0.005)) })],
    };

    public static IEnumerable<object[]> PushVariants()
    {
        foreach (var k in Defaults.Kinds) yield return [Defaults.For(k.Kind), 2.0];
        yield return [Defaults.For(ItemKind.Table) with { Legs = LegStyle.Round, WidthMm = 20 }, 2.0];
        yield return [Defaults.For(ItemKind.Chest) with { Lid = LidStyle.Flat }, 2.0];
        yield return [Defaults.For(ItemKind.Chest) with { WidthMm = 24, DepthMm = 14, HeightMm = 12 }, 3.0];
        yield return [Defaults.For(ItemKind.Chair) with { Back = BackStyle.Ladder, WidthMm = 12, HeightMm = 20 }, 1.5];
        yield return [Defaults.For(ItemKind.WallShelf) with { DepthMm = 8, HeightMm = 8 }, 3.0];
    }

    [Theory]
    [MemberData(nameof(PushVariants))]
    public void Push_fit_furniture_has_ribs_and_still_fits_together(FurnitureItem it, double t)
    {
        var push = Make(it, Push with { ThicknessMm = t });
        Assert.True(push.Ok, string.Join("\n", push.Findings));
        Assert.All(push.Parts, p => Assert.Equal(1, Geo.Pieces(p.Cut)));
        // Ribs on the pieces with slots, notches or finger joints, and only on the outline's edge.
        Assert.True(push.Parts.Sum(p => Geo.Area(p.Ribs)) > 0.01, "no ribs");
        Assert.All(push.Parts, p => Assert.True(Geo.Area(Geo.Subtract(p.Ribs, p.Cut)) < 1e-6));
        Assert.All(push.Parts.Where(p => Geo.Area(p.Ribs) > 0), p => Assert.True(Geo.Area(Geo.Offset(p.Ribs, -0.15, round: true)) < 1e-6, $"{p.Name}: a rib taller than 0.3 mm"));
        // Without the ribs, everything still goes together as before.
        var all = Assemble(WithoutRibs(push), t);
        Assert.Empty(Clashes(all));
        Assert.Empty(EmptySlots(all, t, 0.1));
        if (it.Kind != ItemKind.WallShelf) Assert.True(AllJoined(all, t), "a piece isn't joined to the others");
        if (it.Kind != ItemKind.WallShelf) Assert.DoesNotContain(push.Findings, f => f.Level == Severity.Warn);
        Assert.DoesNotContain(push.Findings, f => f.Text.Contains("glue", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Push_fit_ribs_narrow_each_slot_to_the_sheet_less_the_grip()
    {
        const double t = 2, grip = 0.15;
        var top = Part(Make(Defaults.For(ItemKind.Table), Push), "Top");
        var holes = Geo.Tidy(top.Cut).Where(p => Clipper.Area(p) < 0).ToList();
        Assert.NotEmpty(holes);
        foreach (var hole in holes)
        {
            // Across the slot (the way it's as wide as the sheet plus the fit), the rib tips are the corners nearest the middle.
            var b = Clipper.GetBounds(hole);
            bool acrossX = Math.Abs(b.right - b.left - 2.1) < 0.01;
            Assert.True(acrossX || Math.Abs(b.bottom - b.top - 2.1) < 0.01);
            var at = hole.Select(q => acrossX ? q.x : q.y).ToList();
            double mid = acrossX ? (b.left + b.right) / 2 : (b.top + b.bottom) / 2;
            Assert.Equal(t - grip, at.Where(x => x > mid).Min() - at.Where(x => x < mid).Max(), 3);
        }
    }

    [Fact]
    public void Push_fit_chests_and_shelves_need_no_glue()
    {
        var chest = Make(Defaults.For(ItemKind.Chest), Push);
        Assert.Contains(chest.Parts, p => p.Name == "Lid rail" && p.Copies == 2);
        Assert.DoesNotContain(chest.Parts, p => p.Name == "Lid rim");
        Assert.Equal(4, Geo.Tidy(Part(chest, "Lid").Cut).Count(p => Clipper.Area(p) < 0));
        var shelf = Make(Defaults.For(ItemKind.WallShelf) with { WallSheetMm = 2 }, Push);
        Assert.Equal(2, Geo.Tidy(Part(shelf, "Shelf").Cut).Count(p => Clipper.Area(p) < 0));
        var door = Make(Defaults.For(ItemKind.WallDoor), Push);
        Assert.True(Geo.Area(Part(door, "Door clip").Cut) > Geo.Area(Part(Make(Defaults.For(ItemKind.WallDoor)), "Door clip").Cut) + 0.01, "the clip's fork has no ribs");
    }

    [Fact]
    public void A_table_is_the_size_asked_and_its_top_has_a_slot_for_every_tab()
    {
        var k = Make(new FurnitureItem { Kind = ItemKind.Table, WidthMm = 40, DepthMm = 20, HeightMm = 15, Legs = LegStyle.Legs });
        var top = Part(k, "Top");
        var b = Geo.Bounds(top.Cut);
        Assert.Equal(40, b.right - b.left, 3);
        Assert.Equal(20, b.bottom - b.top, 3);
        Assert.Equal(4, Geo.Tidy(top.Cut).Count(p => Clipper.Area(p) < 0));
        var frame = Part(k, "Leg frame");
        Assert.Equal(2, frame.Copies);
        // The frames reach from the floor to the top of the table (through the top).
        var fb = Geo.Bounds(frame.Cut);
        Assert.Equal(15, fb.bottom - fb.top, 3);
        var all = Assemble(k, 2);
        Assert.Equal(0, all.Min(p => p.Box().Min.Y), 3);
        Assert.Equal(15, all.Max(p => p.Box().Max.Y), 3);
    }

    [Fact]
    public void A_chair_seat_is_at_the_seat_height_and_its_back_at_the_full_height()
    {
        var it = Defaults.For(ItemKind.Chair) with { SeatMm = 8, HeightMm = 18 };
        var all = Assemble(Make(it), 2);
        var seat = all.Single(p => p.Name == "Seat");
        Assert.Equal(8, seat.Box().Max.Y, 3);
        Assert.Equal(18, all.Max(p => p.Box().Max.Y), 3);
        Assert.Equal(it.DepthMm, all.Max(p => p.Box().Max.Z) - all.Min(p => p.Box().Min.Z), 3);
    }

    [Fact]
    public void A_bookshelf_has_the_shelves_asked_and_books_on_its_back()
    {
        var k = Make(Defaults.For(ItemKind.Bookshelf) with { Shelves = 4, HeightMm = 40 });
        Assert.Equal(5, Part(k, "Shelf").Copies);
        var back = Part(k, "Back");
        Assert.True(back.Lines.Count > 20, "books");
        Assert.True(Geo.Area(back.Etched) > 10);
        Assert.False(Make(Defaults.For(ItemKind.Bookshelf) with { Shelves = 12 }).Ok);
    }

    [Fact]
    public void Box_corners_interlock_with_fingers_on_every_edge()
    {
        var k = Make(Defaults.For(ItemKind.Crate) with { WidthMm = 20, DepthMm = 14, HeightMm = 12, Lid = LidStyle.Flat });
        Assert.Equal(6, k.Parts.Count);
        // Six panels as big as their faces, less the fingers of the others: together they're the box's volume of sheet.
        double T = 2, volume = k.Parts.Sum(p => Geo.Area(p.Cut) * T);
        double shell = 20 * 12 * 14 - (20 - 2 * T) * (12 - 2 * T) * (14 - 2 * T);
        Assert.Equal(shell, volume, 1);
        var front = Part(k, "Front");
        // The front has fingers (notches) along its sides.
        Assert.True(Geo.Area(front.Cut) < 20 * 12 - 1);
    }

    [Fact]
    public void A_chest_lid_sits_on_top_with_its_rim_inside()
    {
        var it = Defaults.For(ItemKind.Chest);
        var all = Assemble(Make(it), 2);
        var lid = all.Single(p => p.Name == "Lid");
        Assert.Equal(it.HeightMm + 2, lid.Box().Max.Y, 3);
        var rim = all.Single(p => p.Name == "Lid rim");
        Assert.True(rim.Box().Min.X > 2 && rim.Box().Max.X < it.WidthMm - 2);
        Assert.DoesNotContain(all, p => p.Name == "Lid" && p.Box().Max.Y < it.HeightMm);
    }

    [Fact]
    public void The_door_covers_the_wall_makers_doorway_of_every_shape()
    {
        foreach (var (shape, wallShape) in new[] { (DoorShape.Rectangle, PhotonCanvas.Addon.Walls.OpeningShape.Rectangle), (DoorShape.Arched, PhotonCanvas.Addon.Walls.OpeningShape.Arched), (DoorShape.Pointed, PhotonCanvas.Addon.Walls.OpeningShape.Pointed) })
        {
            // The same outline as the wall maker cuts.
            var ours = WallPieces.Doorway(shape, 10, 0, 16, 28);
            var theirs = PhotonCanvas.Addon.Walls.WallKitMaker.Opening(wallShape, 10, 0, 16, 28);
            Assert.Equal(PhotonCanvas.Addon.Walls.Geo.Area(theirs), Geo.Area(ours), 3);
            var it = Defaults.For(ItemKind.WallDoor) with { Shape = shape };
            var k = Make(it);
            Assert.True(k.Ok, string.Join("\n", k.Findings));
            var door = Part(k, "Door");
            // Apart from the floor gap and the notches for the clips at the hinge side, the door covers the doorway.
            var plan = WallPieces.Plan(it, 2, 0.1);
            var doorway = Geo.Subtract(WallPieces.Doorway(shape, 0, 0, 16, 28), Geo.Union([Geo.Rect(-1, -1, plan.NotchTo + 0.01, 30), Geo.Rect(-1, -1, 20, WallPieces.Floor + 0.01)]));
            Assert.Equal(0, Geo.Area(Geo.Subtract(doorway, door.Cut)), 2);
        }
    }

    [Theory]
    [InlineData(DoorShape.Rectangle, 3.0, 2.0)]
    [InlineData(DoorShape.Arched, 3.0, 2.0)]
    [InlineData(DoorShape.Pointed, 6.0, 3.0)]
    [InlineData(DoorShape.Arched, 1.5, 1.5)]
    public void The_door_swings_open_clear_of_the_wall_and_the_clips(DoorShape shape, double wall, double t)
    {
        var it = Defaults.For(ItemKind.WallDoor) with { Shape = shape, WallMm = wall };
        var set = Sheet with { ThicknessMm = t };
        var k = Make(it, set);
        Assert.True(k.Ok, string.Join("\n", k.Findings));
        var plan = WallPieces.Plan(it, t, 0.1);
        var door = Part(k, "Door");
        var clips = Assemble(k, t).Where(p => p.Name == "Door clip").ToList();
        // The wall: everything outside the doorway, between its faces.
        var doorway = WallPieces.Doorway(shape, 0, 0, it.WidthMm, it.HeightMm);
        bool InWall(V3 p) => Math.Abs(p.Z) < wall / 2 - 0.01 && p.Y > 0.01 && !In(doorway, p.X, p.Y);
        // Turn the door on its pins, a few degrees at a time, and look at points all over it (both faces).
        var b = Geo.Bounds(door.Cut);
        var inside = Geo.Offset(door.Cut, -0.02);
        for (int deg = 0; deg <= 95; deg += 5)
        {
            double a = deg * Math.PI / 180, c = Math.Cos(a), s = Math.Sin(a);
            for (double x = b.left; x <= b.right; x += 0.25)
                for (double y = b.top; y <= b.bottom; y += 0.25)
                {
                    if (!In(inside, x, y)) continue;
                    foreach (var w in new[] { -t / 2 + 0.02, 0, t / 2 - 0.02 })
                    {
                        // Turned outwards about the pins' axis (x = 0, z = the axis), hinge on the left.
                        var p = new V3(x * c - w * s, y, plan.AxisZ + x * s + w * c);
                        Assert.False(InWall(p), $"At {deg}° the door hits the wall at {x:0.##}, {y:0.##}");
                        Assert.False(clips.Any(cl => cl.Inside(p)), $"At {deg}° the door hits a clip at {x:0.##}, {y:0.##}");
                    }
                }
        }
        // The clips grip the wall on both faces, and don't reach into it.
        foreach (var cl in clips)
            for (double x = -plan.Grip + 0.1; x < 0; x += 0.2)
                for (double z = -wall / 2 + 0.05; z < wall / 2; z += 0.2)
                    Assert.False(cl.Inside(new V3(x, cl.Pose.O.Y, z)));
    }

    [Fact]
    public void A_door_too_low_for_its_clips_is_refused()
    {
        var k = Make(Defaults.For(ItemKind.WallDoor) with { Shape = DoorShape.Arched, WidthMm = 16, HeightMm = 14 });
        Assert.False(k.Ok);
        Assert.Contains(k.Findings, f => f.Text.Contains("clips"));
    }

    [Fact]
    public void Shelf_brackets_fit_the_wall_makers_mount_slots()
    {
        var wall = new PhotonCanvas.Addon.Walls.WallSpec { ThicknessMm = 3, Mounts = 2, MountSlotMm = 6, Connectors = [] };
        var it = Defaults.For(ItemKind.WallShelf) with { WallMm = 3, WallSheetMm = 3, MountSlotMm = 6 };
        var k = Make(it, Sheet with { ThicknessMm = 3 });
        Assert.True(k.Ok, string.Join("\n", k.Findings));
        var bracket = Part(k, "Shelf bracket");
        // The tab: as long as the wall is thick (less a little) and a little shorter than the slot.
        var tab = Geo.Intersect(bracket.Cut, [Geo.Rect(-10, -10, 0, 20)]);
        var tb = Geo.Bounds(tab);
        Assert.InRange(tb.right - tb.left, 2.7, 3.0);
        Assert.InRange(tb.bottom - tb.top, 5.6, 6.0);
        // A sheet thicker than the wall's slots can't go in.
        Assert.False(Make(it with { WallSheetMm = 2 }, Sheet with { ThicknessMm = 3 }).Ok);
        _ = wall;
    }

    [Fact]
    public void Too_small_furniture_is_refused_not_squeezed()
    {
        foreach (var kind in Defaults.Kinds.Select(x => x.Kind))
        {
            var tiny = Defaults.For(kind) with { WidthMm = 3, DepthMm = 3, HeightMm = 3, SeatMm = 2 };
            var k = Make(tiny);
            Assert.False(k.Ok, $"{kind} made at 3 mm");
        }
    }

    [Fact]
    public void Wood_detail_follows_the_setting()
    {
        var it = Defaults.For(ItemKind.Table);
        var none = Make(it, Sheet with { Detail = WoodDetail.None });
        Assert.All(none.Parts, p => { Assert.Empty(p.Etched); Assert.Empty(p.Lines); });
        var grain = Make(it, Sheet with { Detail = WoodDetail.Grain });
        Assert.Empty(Part(grain, "Top").Etched);
        Assert.True(Part(grain, "Top").Lines.Count > 5);
        var planks = Make(it, Sheet with { Detail = WoodDetail.Planks });
        Assert.True(Geo.Area(Part(planks, "Top").Etched) > 3, "plank grooves");
        // Detail stays on the piece and off its tabs.
        foreach (var p in planks.Parts)
        {
            Assert.Equal(0, Geo.Area(Geo.Subtract(p.Etched, p.Cut)), 3);
        }
        // The same seed gives the same grain.
        Assert.Equal(Part(Make(it, Sheet with { Seed = 4 }), "Top").Lines.Sum(l => l.Count), Part(Make(it, Sheet with { Seed = 4 }), "Top").Lines.Sum(l => l.Count));
    }

    [Fact]
    public void Settings_round_trip_through_json()
    {
        var set = new FurnitureSet
        {
            Items = [Defaults.For(ItemKind.Chair) with { Count = 6, Back = BackStyle.Ladder }, Defaults.For(ItemKind.WallDoor) with { Hinge = HingeSide.Right, Shape = DoorShape.Pointed }],
            ThicknessMm = 1.5, Detail = WoodDetail.Grain, Seed = 9,
        };
        var back = FurnitureSet.FromJson(set.ToJson())!;
        Assert.Equal(set.ToJson(), back.ToJson());
        Assert.Equal(6, back.Items[0].Count);
        Assert.Contains("\"Pointed\"", set.ToJson());
        Assert.Null(FurnitureSet.FromJson("{nope"));
    }

    [Fact]
    public void Placing_puts_every_piece_on_its_layers_and_can_replace_it()
    {
        var machine = MachineProfile.F2Ultra;
        var set = Sheet with { Items = [Defaults.For(ItemKind.Table), Defaults.For(ItemKind.Chair) with { Count = 4 }, Defaults.For(ItemKind.WallDoor)] };
        var kit = FurnitureKit.Make(set);
        Assert.True(kit.Ok);
        var (s, sheets, id) = Placement.Place(DocState.New(), kit, set, machine, new PlaceOptions());
        var all = Enumerable.Range(0, sheets).SelectMany(i => Pages.SwitchTo(s, i).Shapes).ToList();
        var cut = s.Layers.First(l => l.Name == Placement.CutLayer).Id;
        Assert.Equal(kit.PieceCount, all.Count(x => x.LayerId == cut));
        Assert.Equal(3 + 4 * 3 + 3, kit.PieceCount);
        Assert.Contains(all, x => x.Name.StartsWith("Chair: seat (2 of 4)"));
        Assert.True(s.Layers.FindIndex(l => l.Name == Placement.EtchLayer) < s.Layers.FindIndex(l => l.Name == Placement.CutLayer));
        Assert.True(s.Layers.FindIndex(l => l.Name == Placement.LineLayer) < s.Layers.FindIndex(l => l.Name == Placement.CutLayer));
        Assert.All(s.Shapes, x => Assert.True(x.Bounds.MinX >= 0 && x.Bounds.MaxX <= machine.FieldW && x.Bounds.MinY >= 0 && x.Bounds.MaxY <= machine.FieldH));
        Assert.Equal(set.ToJson(), Placement.Find(s, [s.Shapes[0].Id])!.Value.Spec.ToJson());

        var changed = set with { Items = [Defaults.For(ItemKind.Stool) with { Count = 2 }] };
        var (s2, _, id2) = Placement.Place(s, FurnitureKit.Make(changed), changed, machine, new PlaceOptions { Replace = id });
        Assert.Equal(id, id2);
        Assert.DoesNotContain(s2.Shapes, x => x.Name.Contains("Chair") || x.Name.Contains("Door"));
        Assert.Equal(ItemKind.Stool, Placement.Find(s2, [s2.Shapes[0].Id])!.Value.Spec.Items[0].Kind);
    }
}
