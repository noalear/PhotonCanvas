using Avalonia.Media;
using Clipper2Lib;
using PhotonCanvas.Addon.Buildings;
using PhotonCanvas.Addon.HouseEditor;
using PhotonCanvas.App.View3D;

namespace PhotonCanvas.Addon.CityBuilder;

/// <summary>What the city's 3D view shows.</summary>
public sealed record CityShow
{
    public bool Roofs { get; init; } = true;
    public bool Props { get; init; } = true;
    /// <summary>Doors and windows cut in the walls (a little slower on a big map).</summary>
    public bool Openings { get; init; } = true;
    /// <summary>The buildings picked, shown orange.</summary>
    public HashSet<Guid> Picked { get; init; } = [];
}

/// <summary>
/// The whole map as a light 3D model, quick to draw however big the map is: the ground as flat coloured
/// patches (squares of the same ground merged into rectangles), each building as plain blocks (a slab for
/// every straight wall with its doors and windows cut through, a plate for every floor, its roofs as plain
/// slopes coloured by their covering), fences and walls along the grid lines, and simple trees and the like.
/// No textures and no engraving depth: those are in each building's own pieces.
/// </summary>
public static class CityScene
{
    /// <summary>The groups: what the view can leave out.</summary>
    public const int GroundGroup = 0, WallGroup = 1, RoofGroup = 2, PropGroup = 3;

    static readonly Vec3 Light = new(0.35, 0.85, 0.45);
    static readonly Color EdgeColour = Color.FromArgb(0xB0, 0x3A, 0x2C, 0x20);

    static SlabStyle Look(Color c, bool edges = true) => new(SlabStyle.Lit(c, Light), edges ? EdgeColour : Colors.Transparent, LineWidth.Px(0.5), c, c, LineWidth.Px(0.5));

    public static Color GroundColour(Ground g) => g switch
    {
        Ground.Grass => Color.FromRgb(0x86, 0xA8, 0x5C),
        Ground.Dirt => Color.FromRgb(0xA4, 0x85, 0x5E),
        Ground.Road => Color.FromRgb(0x9C, 0x97, 0x8E),
        Ground.Paving => Color.FromRgb(0xBF, 0xB6, 0xA6),
        Ground.Sand => Color.FromRgb(0xDC, 0xC9, 0x94),
        Ground.Water => Color.FromRgb(0x4E, 0x86, 0xB6),
        Ground.Rock => Color.FromRgb(0x78, 0x73, 0x6D),
        Ground.Field => Color.FromRgb(0xC2, 0xAE, 0x5A),
        _ => Color.FromRgb(0xC4, 0xA6, 0x7C),
    };

    /// <summary>A wall texture's colour, near enough.</summary>
    public static Color WallColour(string? texture)
    {
        string t = (texture ?? "").ToLowerInvariant();
        if (t.Contains("timber") || t.Contains("wood") || t.Contains("plank")) return Color.FromRgb(0xC9, 0xA4, 0x76);
        if (t.Contains("brick")) return Color.FromRgb(0xB8, 0x6B, 0x4E);
        if (t.Contains("adobe") || t.Contains("sand")) return Color.FromRgb(0xD8, 0xB8, 0x88);
        if (t.Contains("metal") || t.Contains("sci") || t.Contains("hex")) return Color.FromRgb(0x9A, 0xA4, 0xAE);
        if (t.Contains("dungeon") || t.Contains("cave")) return Color.FromRgb(0x8E, 0x88, 0x80);
        return Color.FromRgb(0xCF, 0xC6, 0xB6);
    }

    /// <summary>A roof covering's colour, near enough.</summary>
    public static Color RoofColour(string? covering) => (covering ?? "") switch
    {
        "Clay tiles" => Color.FromRgb(0xB5, 0x5A, 0x3C),
        "Thatch" => Color.FromRgb(0xC9, 0xA8, 0x5C),
        "Wooden shingles" or "Wood planks" => Color.FromRgb(0x8C, 0x64, 0x42),
        "Fish scale" => Color.FromRgb(0x6C, 0x7A, 0x86),
        "Corrugated metal" => Color.FromRgb(0x9A, 0xA2, 0xA8),
        "Plain" => Color.FromRgb(0xA8, 0x9C, 0x8C),
        _ => Color.FromRgb(0x5E, 0x66, 0x70),
    };

    static Color Tint(Color c) => Color.FromRgb((byte)Math.Min(255, c.R * 0.6 + 0xF0 * 0.4), (byte)Math.Min(255, c.G * 0.6 + 0x9A * 0.4), (byte)Math.Min(255, c.B * 0.6 + 0x2A * 0.4));

    public static void Build(Scene3D scene, CityMap map, CityShow show)
    {
        scene.Clear();
        double sq = Math.Max(5, map.SquareMm);
        AddGround(scene, map, sq);
        foreach (var b in map.Buildings) AddBuilding(scene, b, sq, show, show.Picked.Contains(b.Id));
        foreach (var f in map.Fences) AddFence(scene, f, sq);
        if (show.Props)
            foreach (var p in map.Props) AddProp(scene, p, sq);
    }

    // ---- the ground ----

    /// <summary>The squares of one ground merged into rectangles (each grown along its row, then down while the rows match).</summary>
    public static List<(int X0, int Y0, int X1, int Y1, Ground G)> Patches(CityMap map)
    {
        var g = map.GroundGrid();
        int w = map.Across, h = map.Down;
        var done = new bool[w, h];
        var res = new List<(int, int, int, int, Ground)>();
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                if (done[x, y]) continue;
                var k = g[x, y];
                int x1 = x;
                while (x1 + 1 < w && !done[x1 + 1, y] && g[x1 + 1, y] == k) x1++;
                int y1 = y;
                while (y1 + 1 < h)
                {
                    bool same = true;
                    for (int i = x; i <= x1 && same; i++) same = !done[i, y1 + 1] && g[i, y1 + 1] == k;
                    if (!same) break;
                    y1++;
                }
                for (int j = y; j <= y1; j++) for (int i = x; i <= x1; i++) done[i, j] = true;
                res.Add((x, y, x1, y1, k));
            }
        return res;
    }

    static void AddGround(Scene3D scene, CityMap map, double sq)
    {
        var up = new Vec3(0, 1, 0);
        foreach (var (x0, y0, x1, y1, g) in Patches(map))
        {
            double y = g == Ground.Water ? -1.2 : 0;
            double ax = x0 * sq, az = y0 * sq, bx = (x1 + 1) * sq, bz = (y1 + 1) * sq;
            scene.AddFace([[new(ax, y, az), new(bx, y, az), new(bx, y, bz), new(ax, y, bz)]], up, Scene3D.Shade(GroundColour(g), 0.62 + 0.38 * up.Dot(Light.Unit)), GroundGroup);
        }
        // The map's edge: a board under it all.
        double W = map.Across * sq, D = map.Down * sq;
        var outline = new PathsD { new PathD { new(0, 0), new(W, 0), new(W, D), new(0, D) } };
        scene.AddSlab(outline, new Frame3(new Vec3(0, 0, 0), new Vec3(1, 0, 0), new Vec3(0, 0, 1)), -4, -0.01, Look(Color.FromRgb(0x6B, 0x55, 0x3C), false), group: GroundGroup, faces: false);
    }

    // ---- buildings ----

    /// <summary>Where a point of a building's plan (squares) is in the 3D view (mm, Y up), at height <paramref name="up"/>.</summary>
    static Vec3 World(CityBuilding b, double x, double y, double up, double sq)
    {
        var (mx, my) = CityFootprint.ToMap(b, x, y);
        return new Vec3(mx * sq, up, my * sq);
    }

    /// <summary>A frame lying flat at height <paramref name="up"/>: u along the plan's x (mm), v along its −y, so its normal points up.</summary>
    static Frame3 Flat(CityBuilding b, double up, double sq)
    {
        var o = World(b, 0, 0, up, sq);
        var u = (World(b, 1, 0, up, sq) - o).Unit;
        var v = (World(b, 0, 1, up, sq) - o).Unit * -1;
        return new Frame3(o, u, v);
    }

    static PathsD PlanToFlat(PathsD squares, double sq)
    {
        var res = new PathsD();
        foreach (var p in squares)
        {
            // Turning y over turns every outline round, so they all go back the other way (outlines and holes stay apart).
            var q = new PathD(p.Select(pt => new PointD(pt.x * sq, -pt.y * sq)));
            q.Reverse();
            res.Add(q);
        }
        if (res.Count == 1 && !Clipper.IsPositive(res[0])) res[0].Reverse();
        return res;
    }

    public static void AddBuilding(Scene3D scene, CityBuilding b, double sq, CityShow show, bool selected)
    {
        var d = b.Design;
        if (d.Levels.Count == 0) return;
        var s = HouseKitMaker.StyleFor(d);
        double dsq = Math.Max(5, d.SquareMm);
        // The building is drawn at the map's square size: a design drawn on other squares is scaled to fit its footprint.
        double k = sq / dsq;
        double H = BuildingKitMaker.StoreyHeight(s) * k, T = s.WallSheetMm * k, Wt = Math.Max(1, BuildingKitMaker.WallThickness(s) * k);
        int levels = Math.Min(d.Levels.Count, HouseKitMaker.MaxLevels);
        Color Pick(Color c) => selected ? Tint(c) : c;
        var floorLook = Look(Pick(Color.FromRgb(0xC9, 0xAC, 0x82)));
        double Base(int level) => BuildingKitMaker.StoreyBase(s, level) * k;

        for (int lv = 0; lv < levels; lv++)
        {
            var level = d.Levels[lv];
            var plan = level.Shape();
            double y0 = Base(lv);
            // The floor: the ground floor's on the ground, the others on the walls below.
            var rooms = new PathsD();
            foreach (var r in plan.Rooms) rooms.AddRange(r.Area);
            foreach (var f in level.Floor) rooms.Add(new PathD { new(f.X, f.Y), new(f.X + 1, f.Y), new(f.X + 1, f.Y + 1), new(f.X, f.Y + 1) });
            if (rooms.Count > 0)
            {
                var area = Clipper.Union(rooms, Clipper2Lib.FillRule.NonZero);
                double bottom = lv == 0 ? 0 : y0 - T;
                if (y0 - bottom > 0.05) scene.AddSlab(PlanToFlat(area, sq), Flat(b, 0, sq), bottom, y0, floorLook, group: WallGroup);
            }
            var wallLook = Look(Pick(WallColour(lv > 0 && s.UpperTexture is { } up ? up : s.Texture)));
            foreach (var run in plan.Runs)
            {
                var a = World(b, run.A.X, run.A.Y, y0, sq);
                var e = World(b, run.B.X, run.B.Y, y0, sq);
                double L = (e - a).Length;
                if (L < 1e-6) continue;
                var u = (e - a) * (1 / L);
                var frame = new Frame3(a, u, new Vec3(0, 1, 0));
                // A little past each end, so the corners close (there are no columns in this view).
                var outline = new PathsD { new PathD { new(-Wt / 2, 0), new(L + Wt / 2, 0), new(L + Wt / 2, H), new(-Wt / 2, H) } };
                if (show.Openings)
                    foreach (var o in level.Openings)
                    {
                        if (HouseKitMaker.Along(run, o.X, o.Y) is not { } steps) continue;
                        double at = steps / run.Steps * L, hw = o.WidthMm * k / 2;
                        double v0 = o.Kind == OpeningKind.Window ? o.SillMm * k : 0.01, v1 = Math.Min(H - 0.5, v0 + o.HeightMm * k);
                        if (v1 <= v0) continue;
                        // A hole runs the other way round.
                        outline.Add(new PathD { new(at - hw, v0), new(at - hw, v1), new(at + hw, v1), new(at + hw, v0) });
                    }
                scene.AddSlab(outline, frame, -Wt / 2, Wt / 2, wallLook, group: WallGroup);
            }
        }
        if (show.Roofs)
            foreach (var r in d.Roofs)
                if (r.Level < levels) AddRoof(scene, b, r, Base(r.Level) + H, sq, k, Pick(RoofColour(r.Covering)));
    }

    /// <summary>A roof as plain slopes: gable, hip, lean-to or flat, from the top of the walls of its floor.</summary>
    static void AddRoof(Scene3D scene, CityBuilding b, RoofArea r, double top, double sq, double k, Color colour)
    {
        // Along the ridge (a) and across it (c), in plan squares.
        bool across = r.RidgeAcross;
        double a0 = across ? r.X0 : r.Y0, a1 = across ? r.X1 : r.Y1, c0 = across ? r.Y0 : r.X0, c1 = across ? r.Y1 : r.X1;
        if (a1 <= a0 || c1 <= c0) return;
        double e = r.EaveMm * k / sq, tan = Math.Tan(Math.Clamp(r.PitchDeg, 5, 75) * Math.PI / 180);
        Vec3 P(double a, double c, double up) => across ? World(b, a, c, up, sq) : World(b, c, a, up, sq);
        var look = Look(colour);
        var centre = P((a0 + a1) / 2, (c0 + c1) / 2, top);
        void Face(params Vec3[] pts)
        {
            if (pts.Length < 3) return;
            var n = (pts[1] - pts[0]).Cross(pts[2] - pts[0]).Unit;
            var mid = pts.Aggregate(new Vec3(0, 0, 0), (acc, p) => acc + p) * (1.0 / pts.Length);
            if (n.Dot(mid - centre) < 0) n = n * -1;
            scene.AddFace([pts], n, look.Paint(n, false), RoofGroup);
            for (int i = 0; i < pts.Length; i++) scene.AddEdge(pts[i], pts[(i + 1) % pts.Length], EdgeColour, LineWidth.Px(0.5), group: RoofGroup);
        }
        if (r.Kind == RoofKind.Flat)
        {
            var outline = new PathsD { new PathD { new(a0 - e, c0 - e), new(a1 + e, c0 - e), new(a1 + e, c1 + e), new(a0 - e, c1 + e) } };
            var plan = across ? outline : new PathsD(outline.Select(p => new PathD(p.Select(q => new PointD(q.y, q.x)))));
            scene.AddSlab(PlanToFlat(plan, sq), Flat(b, 0, sq), top, top + 2 * k, look, group: RoofGroup);
            return;
        }
        double span = c1 - c0, half = span / 2, cm = (c0 + c1) / 2;
        // Heights in mm over the plan in squares: the slope rises tan × run.
        double rise = half * sq * tan, drop = e * sq * tan;
        if (r.Kind == RoofKind.LeanTo)
        {
            // High at the back (c0, turn 0), low at the front.
            double hi = top + span * sq * tan, lo = top - drop, g = e / 2;
            bool flip = r.Turn >= 2;
            double back = flip ? c1 : c0, front = flip ? c0 - e : c1 + e;
            Face(P(a0 - g, back, hi), P(a1 + g, back, hi), P(a1 + g, front, lo), P(a0 - g, front, lo));
            Face(P(a0, back, top), P(a0, back, hi), P(a0, flip ? c0 : c1, top));
            Face(P(a1, back, top), P(a1, back, hi), P(a1, flip ? c0 : c1, top));
            Face(P(a0, back, top), P(a1, back, top), P(a1, back, hi), P(a0, back, hi));
            return;
        }
        double ridge = top + rise, eave = top - drop;
        if (r.Kind == RoofKind.Hip)
        {
            // The hips come in as far as the slopes do across, so the ends slope at the same pitch.
            double inset = Math.Min(half, (a1 - a0) / 2);
            double ra0 = a0 + inset, ra1 = a1 - inset;
            Face(P(a0 - e, c0 - e, eave), P(a1 + e, c0 - e, eave), P(ra1, cm, ridge), P(ra0, cm, ridge));
            Face(P(a0 - e, c1 + e, eave), P(a1 + e, c1 + e, eave), P(ra1, cm, ridge), P(ra0, cm, ridge));
            Face(P(a0 - e, c0 - e, eave), P(a0 - e, c1 + e, eave), P(ra0, cm, ridge));
            Face(P(a1 + e, c0 - e, eave), P(a1 + e, c1 + e, eave), P(ra1, cm, ridge));
            return;
        }
        // Gable: two slopes, a little over each gable end, and the gable ends themselves.
        double ge = e / 2;
        Face(P(a0 - ge, c0 - e, eave), P(a1 + ge, c0 - e, eave), P(a1 + ge, cm, ridge), P(a0 - ge, cm, ridge));
        Face(P(a0 - ge, c1 + e, eave), P(a1 + ge, c1 + e, eave), P(a1 + ge, cm, ridge), P(a0 - ge, cm, ridge));
        var wall = Look(WallColour(null));
        foreach (var a in new[] { a0, a1 })
        {
            var pts = new[] { P(a, c0, top), P(a, c1, top), P(a, cm, ridge) };
            var n = (pts[1] - pts[0]).Cross(pts[2] - pts[0]).Unit;
            if (n.Dot(pts[0] - centre) < 0) n = n * -1;
            scene.AddFace([pts], n, wall.Paint(n, true), RoofGroup);
        }
    }

    // ---- fences ----

    static void AddFence(Scene3D scene, Fence f, double sq)
    {
        var (h, t, colour) = f.Kind switch
        {
            FenceKind.Hedge => (0.5 * sq, 0.3 * sq, Color.FromRgb(0x4F, 0x7A, 0x3A)),
            FenceKind.StoneWall => (0.45 * sq, 0.16 * sq, Color.FromRgb(0xA8, 0xA1, 0x96)),
            FenceKind.CityWall => (1.8 * sq, 0.5 * sq, Color.FromRgb(0xB6, 0xAE, 0xA0)),
            _ => (0.4 * sq, 0.06 * sq, Color.FromRgb(0x8E, 0x68, 0x44)),
        };
        var look = Look(colour);
        // Straight runs along each line, so a long wall is one block.
        foreach (var (x0, y0, x1, y1) in Runs(f.Edges))
        {
            var a = new Vec3(x0 * sq, 0, y0 * sq);
            var b = new Vec3(x1 * sq, 0, y1 * sq);
            double L = (b - a).Length;
            if (L < 1e-6) continue;
            var u = (b - a) * (1 / L);
            var outline = new PathsD { new PathD { new(-t / 2, 0), new(L + t / 2, 0), new(L + t / 2, h), new(-t / 2, h) } };
            scene.AddSlab(outline, new Frame3(a, u, new Vec3(0, 1, 0)), -t / 2, t / 2, look, group: WallGroup);
        }
    }

    /// <summary>One-square edges joined into straight runs (grid points from, to).</summary>
    public static List<(int X0, int Y0, int X1, int Y1)> Runs(IEnumerable<FenceEdge> edges)
    {
        var set = edges.ToHashSet();
        var res = new List<(int, int, int, int)>();
        foreach (var e in set.OrderBy(e => e.Across).ThenBy(e => e.Y).ThenBy(e => e.X))
        {
            // Start only where the run starts.
            var before = e.Across ? e with { X = e.X - 1 } : e with { Y = e.Y - 1 };
            if (set.Contains(before)) continue;
            int n = 1;
            while (set.Contains(e.Across ? e with { X = e.X + n } : e with { Y = e.Y + n })) n++;
            res.Add(e.Across ? (e.X, e.Y, e.X + n, e.Y) : (e.X, e.Y, e.X, e.Y + n));
        }
        return res;
    }

    // ---- trees and the like ----

    static PathsD Ring(double r, int sides, double turn = 0)
    {
        var p = new PathD();
        for (int i = 0; i < sides; i++)
        {
            double a = turn + i * 2 * Math.PI / sides;
            p.Add(new PointD(r * Math.Cos(a), r * Math.Sin(a)));
        }
        return [p];
    }

    static PathsD Box(double w, double d) => [new PathD { new(-w / 2, -d / 2), new(w / 2, -d / 2), new(w / 2, d / 2), new(-w / 2, d / 2) }];

    /// <summary>A box <paramref name="w"/> × <paramref name="d"/> with its middle at (<paramref name="a"/>, <paramref name="b"/>).</summary>
    static PathsD BoxAt(double a, double b, double w, double d) => [new PathD { new(a - w / 2, b - d / 2), new(a + w / 2, b - d / 2), new(a + w / 2, b + d / 2), new(a - w / 2, b + d / 2) }];

    /// <summary>A square ring: the outside <paramref name="w"/> across, its walls <paramref name="t"/> thick.</summary>
    static PathsD Frame(double w, double d, double t)
    {
        var inner = new PathD(Box(w - 2 * t, d - 2 * t)[0]);
        inner.Reverse();
        return [Box(w, d)[0], inner];
    }

    static void AddProp(Scene3D scene, Prop p, double sq)
    {
        double k = sq * Math.Clamp(p.Size, 0.2, 6), turn = p.TurnDeg * Math.PI / 180;
        var o = new Vec3(p.X * sq, 0, p.Y * sq);
        var u = new Vec3(Math.Cos(turn), 0, Math.Sin(turn));
        var v = new Vec3(Math.Sin(turn), 0, -Math.Cos(turn));
        var flat = new Frame3(o, u, v);
        void Slab(PathsD outline, double w0, double w1, Color c) => scene.AddSlab(outline, flat, w0, w1, Look(c, false), group: PropGroup);
        var trunk = Color.FromRgb(0x6E, 0x4D, 0x30);
        switch (p.Kind)
        {
            case PropKind.Tree:
                Slab(Ring(0.06 * k, 6), 0, 0.3 * k, trunk);
                Slab(Ring(0.42 * k, 8), 0.25 * k, 0.5 * k, Color.FromRgb(0x4E, 0x80, 0x3C));
                Slab(Ring(0.34 * k, 8, 0.2), 0.5 * k, 0.68 * k, Color.FromRgb(0x58, 0x8C, 0x42));
                Slab(Ring(0.2 * k, 8), 0.68 * k, 0.8 * k, Color.FromRgb(0x62, 0x96, 0x48));
                break;
            case PropKind.Pine:
                Slab(Ring(0.05 * k, 6), 0, 0.2 * k, trunk);
                for (int i = 0; i < 4; i++)
                    Slab(Ring((0.34 - 0.075 * i) * k, 8, i * 0.2), (0.15 + 0.2 * i) * k, (0.33 + 0.2 * i) * k, Color.FromRgb(0x3A, (byte)(0x66 + 8 * i), 0x40));
                break;
            case PropKind.Bush:
                Slab(Ring(0.25 * k, 7), 0, 0.22 * k, Color.FromRgb(0x5A, 0x86, 0x44));
                break;
            case PropKind.Boulder:
                Slab(Ring(0.25 * k, 6, 0.3), 0, 0.12 * k, Color.FromRgb(0x8C, 0x88, 0x82));
                Slab(Ring(0.17 * k, 6, 0.6), 0.12 * k, 0.22 * k, Color.FromRgb(0x96, 0x92, 0x8C));
                break;
            case PropKind.Well:
            {
                var ring = Ring(0.3 * k, 10);
                ring.AddRange(Ring(0.2 * k, 10).Select(r => { var q = new PathD(r); q.Reverse(); return q; }));
                Slab(ring, 0, 0.2 * k, Color.FromRgb(0xA4, 0x9C, 0x90));
                Slab(Ring(0.2 * k, 10), 0, 0.04 * k, Color.FromRgb(0x3E, 0x5E, 0x80));
                break;
            }
            case PropKind.Cart:
                Slab(Box(0.7 * k, 0.4 * k), 0.12 * k, 0.28 * k, Color.FromRgb(0x9A, 0x70, 0x48));
                foreach (double side in new[] { -1.0, 1.0 })
                {
                    var wheel = new Frame3(o + v * (side * 0.22 * k) + new Vec3(0, 0.12 * k, 0), u, new Vec3(0, 1, 0));
                    scene.AddSlab(Ring(0.12 * k, 10), wheel, -0.015 * k, 0.015 * k, Look(trunk, false), group: PropGroup);
                }
                break;
            case PropKind.Crates:
                Slab(Box(0.3 * k, 0.3 * k), 0, 0.3 * k, Color.FromRgb(0xB0, 0x86, 0x58));
                scene.AddSlab(Ring(0.12 * k, 10), new Frame3(o + u * (0.3 * k), u, v), 0, 0.3 * k, Look(Color.FromRgb(0x8A, 0x62, 0x3E), false), group: PropGroup);
                break;
            case PropKind.Lamp:
                Slab(Box(0.05 * k, 0.05 * k), 0, 0.7 * k, Color.FromRgb(0x3A, 0x3A, 0x3A));
                Slab(Box(0.14 * k, 0.14 * k), 0.7 * k, 0.82 * k, Color.FromRgb(0xE8, 0xC8, 0x6A));
                break;
            case PropKind.Statue:
                Slab(Box(0.4 * k, 0.4 * k), 0, 0.2 * k, Color.FromRgb(0xB0, 0xA8, 0x9C));
                Slab(Box(0.14 * k, 0.12 * k), 0.2 * k, 0.75 * k, Color.FromRgb(0xC8, 0xC2, 0xB8));
                break;
            // The town's bigger pieces, as blocks the furniture maker's sizes (the back is +v, the plan's top).
            case PropKind.Trough:
                Slab(Frame(1.26 * k, 0.43 * k, 0.07 * k), 0, 0.32 * k, Color.FromRgb(0x9C, 0x96, 0x8C));
                Slab(Box(1.12 * k, 0.29 * k), 0, 0.26 * k, Color.FromRgb(0x4E, 0x86, 0xB6));
                break;
            case PropKind.Stage:
                Slab(Box(3 * k, 2 * k), 0, 0.4 * k, Color.FromRgb(0x9A, 0x70, 0x48));
                Slab(BoxAt(0, -1.1 * k, 1 * k, 0.2 * k), 0, 0.2 * k, Color.FromRgb(0x8A, 0x62, 0x3E));
                Slab(BoxAt(0, 0.92 * k, 3 * k, 0.16 * k), 0.4 * k, 1.7 * k, Color.FromRgb(0x9E, 0x2E, 0x2E));
                foreach (double side in new[] { -1.0, 1.0 })
                    Slab(BoxAt(side * 1.4 * k, 0.75 * k, 0.2 * k, 0.2 * k), 0.4 * k, 1.8 * k, Color.FromRgb(0x7A, 0x52, 0x32));
                break;
            case PropKind.Stall:
                Slab(BoxAt(0, -0.1 * k, 1.3 * k, 0.45 * k), 0, 0.36 * k, Color.FromRgb(0x9A, 0x70, 0x48));
                foreach (double side in new[] { -1.0, 1.0 })
                    Slab(BoxAt(side * 0.66 * k, 0.32 * k, 0.06 * k, 0.06 * k), 0, 0.8 * k, Color.FromRgb(0x6E, 0x4D, 0x30));
                Slab(Box(1.42 * k, 0.79 * k), 0.8 * k, 0.86 * k, Color.FromRgb(0xC8, 0x4C, 0x3C));
                break;
            case PropKind.Bar:
                Slab(BoxAt(0, -0.2 * k, 1.57 * k, 0.32 * k), 0, 0.42 * k, Color.FromRgb(0x7A, 0x52, 0x32));
                foreach (double a in new[] { -0.5, 0.0, 0.5 })
                    scene.AddSlab(Ring(0.12 * k, 10), new Frame3(o + u * (a * k) + v * (0.22 * k), u, v), 0, 0.32 * k, Look(Color.FromRgb(0x8A, 0x62, 0x3E), false), group: PropGroup);
                Slab(BoxAt(0.7 * k, 0.3 * k, 0.06 * k, 0.06 * k), 0, 0.9 * k, Color.FromRgb(0x6E, 0x4D, 0x30));
                break;
            case PropKind.NoticeBoard:
                foreach (double side in new[] { -1.0, 1.0 })
                    Slab(BoxAt(side * 0.4 * k, 0, 0.06 * k, 0.06 * k), 0, 0.95 * k, Color.FromRgb(0x6E, 0x4D, 0x30));
                Slab(Box(0.8 * k, 0.05 * k), 0.4 * k, 0.85 * k, Color.FromRgb(0xE8, 0xDC, 0xC4));
                Slab(Box(0.94 * k, 0.39 * k), 0.95 * k, 1.0 * k, Color.FromRgb(0x7A, 0x52, 0x32));
                break;
            case PropKind.Fountain:
            {
                var stone = Color.FromRgb(0xA4, 0x9C, 0x90);
                Slab(Ring(1.0 * k, 8), 0, 0.08 * k, stone);
                var basin = Ring(0.92 * k, 8);
                basin.AddRange(Ring(0.8 * k, 8).Select(r => { var q = new PathD(r); q.Reverse(); return q; }));
                Slab(basin, 0.08 * k, 0.36 * k, stone);
                Slab(Ring(0.8 * k, 8), 0.08 * k, 0.26 * k, Color.FromRgb(0x4E, 0x86, 0xB6));
                Slab(Ring(0.1 * k, 8), 0.26 * k, 0.9 * k, stone);
                Slab(Ring(0.48 * k, 16), 0.82 * k, 0.9 * k, stone);
                Slab(Ring(0.42 * k, 16), 0.9 * k, 0.92 * k, Color.FromRgb(0x4E, 0x86, 0xB6));
                Slab(Ring(0.08 * k, 8), 0.92 * k, 1.3 * k, stone);
                Slab(Ring(0.27 * k, 16), 1.22 * k, 1.3 * k, stone);
                Slab(Ring(0.21 * k, 16), 1.3 * k, 1.32 * k, Color.FromRgb(0x5E, 0x96, 0xC6));
                Slab(Ring(0.06 * k, 8), 1.32 * k, 1.8 * k, Color.FromRgb(0xC4, 0xBC, 0xB0));
                break;
            }
            case PropKind.Gallows:
            {
                var wood = Color.FromRgb(0x9A, 0x70, 0x48);
                Slab(BoxAt(0, 0.18 * k, 1.81 * k, 1.14 * k), 0, 0.52 * k, wood);
                Slab(BoxAt(-0.43 * k, -0.57 * k, 0.47 * k, 0.36 * k), 0, 0.26 * k, Color.FromRgb(0x8A, 0x62, 0x3E));
                foreach (double side in new[] { -1.0, 1.0 })
                {
                    Slab(BoxAt(side * 0.55 * k, 0.2 * k, 0.11 * k, 0.08 * k), 0.52 * k, 2.36 * k, trunk);
                    Slab(BoxAt(side * 0.8 * k, 0.18 * k, 0.05 * k, 1.0 * k), 0.52 * k, 0.84 * k, trunk);
                }
                Slab(BoxAt(0, 0.2 * k, 1.3 * k, 0.11 * k), 2.25 * k, 2.36 * k, trunk);
                Slab(BoxAt(-0.08 * k, 0.2 * k, 0.03 * k, 0.03 * k), 1.45 * k, 2.25 * k, Color.FromRgb(0xD8, 0xC0, 0x8A));
                break;
            }
            case PropKind.GuardTower:
                Slab(Box(1.81 * k, 1.81 * k), 0, 3.4 * k, Color.FromRgb(0xA4, 0x9C, 0x90));
                Slab(Frame(1.81 * k, 1.81 * k, 0.18 * k), 3.4 * k, 3.65 * k, Color.FromRgb(0xB4, 0xAC, 0xA0));
                for (int i = 0; i < 4; i++)
                    foreach (double side in new[] { -1.0, 1.0 })
                    {
                        double c = (-0.72 + 0.48 * i) * k;
                        Slab(BoxAt(c, side * 0.815 * k, 0.24 * k, 0.18 * k), 3.65 * k, 3.85 * k, Color.FromRgb(0xB4, 0xAC, 0xA0));
                        Slab(BoxAt(side * 0.815 * k, c, 0.18 * k, 0.24 * k), 3.65 * k, 3.85 * k, Color.FromRgb(0xB4, 0xAC, 0xA0));
                    }
                break;
        }
    }
}
