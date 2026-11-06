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
    /// <summary>
    /// Each building as it'll be once it's cut and put together (<see cref="CityBuilt"/>), true to size: its corner
    /// columns, roofs' overhang, doors, shutters and lamps. Buildings not made that way yet are drawn as plain blocks.
    /// </summary>
    public bool AsBuilt { get; init; } = true;
    /// <summary>The doors and shutters swung open (as built).</summary>
    public bool DoorsOpen { get; init; }
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

    /// <summary>A ground's colour in a look: a cyberpunk city's streets dark asphalt, its pavements grey slabs.</summary>
    public static Color GroundColour(Ground g, string? theme) => !TownMaker.SciFi(theme) ? GroundColour(g) : g switch
    {
        Ground.Road => Color.FromRgb(0x33, 0x36, 0x3D),
        Ground.Paving => Color.FromRgb(0x6A, 0x6E, 0x76),
        Ground.Dirt => Color.FromRgb(0x5A, 0x52, 0x4A),
        Ground.Grass => Color.FromRgb(0x4E, 0x6A, 0x4A),
        Ground.Water => Color.FromRgb(0x24, 0x4A, 0x66),
        Ground.Sand => Color.FromRgb(0x7A, 0x74, 0x6A),
        Ground.Floor => Color.FromRgb(0x5C, 0x60, 0x68),
        _ => GroundColour(g),
    };

    /// <summary>A wall texture's colour, near enough.</summary>
    public static Color WallColour(string? texture)
    {
        string t = (texture ?? "").ToLowerInvariant();
        // A cyberpunk city's outsides: stained concrete, dark steel, shopfronts, blue-black glass.
        if (t.Contains("hab concrete")) return Color.FromRgb(0x8E, 0x8F, 0x8F);
        if (t.Contains("steel cladding")) return Color.FromRgb(0x5E, 0x68, 0x72);
        if (t.Contains("shopfront")) return Color.FromRgb(0x6E, 0x66, 0x6C);
        if (t.Contains("glass facade")) return Color.FromRgb(0x3C, 0x4E, 0x62);
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
        map = map.Tidied();
        double sq = Math.Max(5, map.SquareMm);
        // The roads laid on the ground; the water left under their bridges, and the bridges over it.
        bool sci = TownMaker.SciFi(map.Theme);
        AddGround(scene, map.Paved(overWater: false), sq, map.Theme);
        foreach (var site in TownPieces.Bridges(map)) AddBridge(scene, site, sq);
        int built = 0;
        foreach (var b in map.Buildings)
        {
            bool picked = show.Picked.Contains(b.Id);
            if (show.AsBuilt && built < MostBuilt && CityBuilt.Known(b.Design) is { Ok: true } model)
            {
                AddBuilt(scene, b, model, sq, show, picked);
                built++;
            }
            else AddBuilding(scene, b, sq, show, picked);
            if (sci) AddNeon(scene, b, sq, show.Openings);
        }
        AddWalls(scene, map, sq);
        if (show.Props)
        {
            foreach (var p in map.Props) AddProp(scene, p, sq, sci);
            foreach (var i in map.Items) AddItem(scene, i, sq, show.Picked.Contains(i.Id), sci ? ItemMetal : null);
        }
    }

    /// <summary>The most buildings drawn as built (the rest plain blocks), so a big town still turns round.</summary>
    public const int MostBuilt = 200;

    static readonly Color ItemWood = Color.FromRgb(0xB0, 0x86, 0x58), ItemPicked = Color.FromRgb(0xF0, 0x9A, 0x2A), ItemMetal = Color.FromRgb(0x7C, 0x84, 0x8E);
    static readonly Color FloorWood = Color.FromRgb(0xC9, 0xAC, 0x82), DoorWood = Color.FromRgb(0x8A, 0x62, 0x3E);

    /// <summary>
    /// A building as it'll be once it's cut and put together: every one of its pieces where it sits, true to size
    /// (scaled only if it's drawn on other squares than the map's), coloured by what it is.
    /// </summary>
    public static void AddBuilt(Scene3D scene, CityBuilding b, BuiltModel m, double sq, CityShow show, bool selected)
    {
        var d = b.Design;
        double k = sq / m.SquareMm;
        // A point of the model (mm about its middle: x along the plan, y up, z down it) on the map: through its
        // plan (squares), turned, shifted and tilted with the building; an affine map, so three points give it.
        Vec3 OnMap(double px, double py, double up)
        {
            var (x, y) = CityFootprint.ToMap(b, px, py);
            return new Vec3(x * sq, up, y * sq);
        }
        var o = OnMap(m.MiddleX, m.MiddleY, 0);
        var ex = (OnMap(m.MiddleX + 1 / m.SquareMm, m.MiddleY, 0) - o) * (1 / k);
        var ez = (OnMap(m.MiddleX, m.MiddleY + 1 / m.SquareMm, 0) - o) * (1 / k);
        Vec3 P(Furniture.V3 p) => o + ex * (p.X * k) + new Vec3(0, p.Y * k, 0) + ez * (p.Z * k);
        Vec3 D(Furniture.V3 v) => ex * v.X + new Vec3(0, v.Y, 0) + ez * v.Z;
        Color Pick(Color c) => selected ? Tint(c) : c;
        var s = HouseKitMaker.StyleFor(d);
        var walls = Look(Pick(WallColour(s.Texture)));
        var upper = s.UpperTexture is { } up ? Look(Pick(WallColour(up))) : walls;
        var columns = Look(Pick(Scene3DShade(WallColour(d.ColumnTexture ?? s.Texture), 0.9)));
        var roof = Look(Pick(RoofColour(d.Roofs.Count > 0 ? d.Roofs[0].Covering : s.RoofTexture)));
        bool sci = d.VaryWalls || (s.Texture ?? "") is "Hab concrete" or "Steel cladding" or "Shopfront" or "Glass facade" or "Sci-fi panels";
        // A city block's floors and roof deck are dark poured slabs, its fittings steel.
        var floor = Look(Pick(sci ? Color.FromRgb(0x50, 0x54, 0x5C) : FloorWood));
        var wood = Look(Pick(sci ? ItemMetal : ItemWood));
        var door = Look(Pick(sci ? Color.FromRgb(0x4A, 0x50, 0x58) : DoorWood));
        foreach (var piece in m.Solids)
        {
            var pose = show.DoorsOpen && piece.Open is { } open ? open : piece.Pose;
            var look = piece.Kind switch
            {
                SolidKind.Wall => piece.Level > 0 ? upper : walls,
                SolidKind.Column => columns,
                SolidKind.Roof => roof,
                SolidKind.Floor or SolidKind.Ground => floor,
                _ => piece.Open is not null ? door : wood,
            };
            var outline = k == 1 ? piece.Outline : Clipper.ScalePaths(piece.Outline, k);
            var frame = new Frame3(P(pose.O), D(pose.U), D(pose.V));
            double t = piece.Thickness * k / 2;
            scene.AddSlab(outline, frame, -t, t, look, group: piece.Kind == SolidKind.Roof ? RoofGroup : WallGroup);
        }
    }

    static Color Scene3DShade(Color c, double f) => Color.FromRgb((byte)(c.R * f), (byte)(c.G * f), (byte)(c.B * f));

    /// <summary>
    /// A piece of furniture put together as it will be once it's cut: every one of its flat pieces where it
    /// sits, its true size, turned as it's put down (just a box the size of the floor it takes if it can't be made).
    /// </summary>
    public static void AddItem(Scene3D scene, CityItem i, double sq, bool picked = false, Color? colour = null)
    {
        var solid = CityItems.SolidOf(i, sq);
        double a = i.TurnDeg * Math.PI / 180, c = Math.Cos(a), s = Math.Sin(a), ox = i.X * sq, oz = i.Y * sq;
        // The piece's own x (along its front) and z (towards its front) turned as on the map, clockwise from above.
        Vec3 P(Furniture.V3 p) => new(ox + p.X * c - p.Z * s, p.Y, oz + p.X * s + p.Z * c);
        Vec3 D(Furniture.V3 d) => new(d.X * c - d.Z * s, d.Y, d.X * s + d.Z * c);
        var style = Look(picked ? ItemPicked : colour ?? ItemWood);
        if (solid.Parts.Count == 0)
        {
            var flat = new Frame3(new Vec3(ox, 0, oz), new Vec3(c, 0, s), new Vec3(s, 0, -c));
            scene.AddSlab(Box(Math.Max(1, i.WidthMm), Math.Max(1, i.DepthMm)), flat, 0, Math.Max(1, i.Item.HeightMm), style, group: PropGroup);
            return;
        }
        double t = solid.ThicknessMm;
        foreach (var (outline, poses) in solid.Parts)
            foreach (var q in poses)
                scene.AddSlab(outline, new Frame3(P(q.O), D(q.U), D(q.V)), -t / 2, t / 2, style, group: PropGroup);
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

    static void AddGround(Scene3D scene, CityMap map, double sq, string? theme = null)
    {
        var up = new Vec3(0, 1, 0);
        foreach (var (x0, y0, x1, y1, g) in Patches(map))
        {
            double y = g == Ground.Water ? -1.2 : 0;
            double ax = x0 * sq, az = y0 * sq, bx = (x1 + 1) * sq, bz = (y1 + 1) * sq;
            scene.AddFace([[new(ax, y, az), new(bx, y, az), new(bx, y, bz), new(ax, y, bz)]], up, Scene3D.Shade(GroundColour(g, theme), 0.62 + 0.38 * up.Dot(Light.Unit)), GroundGroup);
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
        var floorLook = Look(Pick(d.VaryWalls ? Color.FromRgb(0x50, 0x54, 0x5C) : Color.FromRgb(0xC9, 0xAC, 0x82)));
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

    // ---- a cyberpunk city's neon ----

    /// <summary>The city's neon: magenta, cyan, amber, violet and acid green.</summary>
    static readonly Color[] Neon =
    [
        Color.FromRgb(0xFF, 0x2E, 0xA6), Color.FromRgb(0x22, 0xE5, 0xFF), Color.FromRgb(0xFF, 0xB8, 0x2E), Color.FromRgb(0xA0, 0x5C, 0xFF), Color.FromRgb(0x5D, 0xFF, 0x8B),
    ];
    static readonly Color[] Lit = [Color.FromRgb(0xFF, 0xD0, 0x80), Color.FromRgb(0xA8, 0xEC, 0xFF), Color.FromRgb(0xFF, 0xE8, 0xB8)];
    static readonly Color Unlit = Color.FromRgb(0x1E, 0x24, 0x2C);

    /// <summary>Kinds with a shop or bar at street level: a neon sign band over the ground floor and a sign up the front.</summary>
    static bool Street(string type) => type is "Neon bar" or "Noodle bar" or "Tech shop" or "Arcade" or "Clinic" or "Capsule hotel" or "Corp tower" or "Canteen";

    /// <summary>
    /// A cyberpunk building's lights as they'd look at night (drawn only, never cut): its windows lit or dark, a
    /// neon strip round its roof line, a sign band over a shop's ground floor and a tall sign up the front of a
    /// bar or arcade, each building in its own two colours.
    /// </summary>
    static void AddNeon(Scene3D scene, CityBuilding b, double sq, bool windows)
    {
        var d = b.Design;
        if (d.Levels.Count == 0) return;
        var s = HouseKitMaker.StyleFor(d);
        double k = sq / Math.Max(5, d.SquareMm);
        double H = BuildingKitMaker.StoreyHeight(s) * k, Wt = Math.Max(1, BuildingKitMaker.WallThickness(s) * k);
        int hash = 17;
        foreach (char c in b.Name + "|" + b.Type) hash = unchecked(hash * 31 + c);
        var rnd = new Random(hash);
        Color a = Neon[rnd.Next(Neon.Length)], c2 = Neon[rnd.Next(Neon.Length)];
        if (c2 == a) c2 = Neon[(Array.IndexOf(Neon, a) + 2) % Neon.Length];
        int levels = Math.Min(d.Levels.Count, HouseKitMaker.MaxLevels);
        int top = Enumerable.Range(0, levels).LastOrDefault(i => d.Levels[i].Walls.Count > 0, -1);
        if (top < 0) return;
        bool street = Street(b.Type);
        // Just off the wall's outside face (in squares), past its texture.
        double off = (Wt / 2 + 1.6 * k) / sq;
        var up = new Vec3(0, 1, 0);
        void Quad(double x0, double y0, double x1, double y1, double nx, double ny, double h0, double h1, double o, Color colour)
        {
            var p = new[] { World(b, x0 + nx * o, y0 + ny * o, h0, sq), World(b, x1 + nx * o, y1 + ny * o, h0, sq), World(b, x1 + nx * o, y1 + ny * o, h1, sq), World(b, x0 + nx * o, y0 + ny * o, h1, sq) };
            var n = (p[1] - p[0]).Cross(p[3] - p[0]).Unit;
            var outward = World(b, x0 + nx, y0 + ny, h0, sq) - World(b, x0, y0, h0, sq);
            if (n.Dot(outward) < 0) n = n * -1;
            scene.AddFace([p], n, colour, WallGroup);
        }
        (Run? Run, double Nx, double Ny, double Len) longest = default;
        for (int lv = 0; lv <= top; lv++)
        {
            var level = d.Levels[lv];
            if (level.Walls.Count == 0) continue;
            var plan = level.Shape();
            double y0 = BuildingKitMaker.StoreyBase(s, lv) * k;
            foreach (var run in plan.Runs)
            {
                double ax = run.A.X, ay = run.A.Y, bx = run.B.X, by = run.B.Y, len = Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay));
                if (len < 1e-6) continue;
                double dx = (bx - ax) / len, dy = (by - ay) / len, nx = dy, ny = -dx, mx = (ax + bx) / 2, my = (ay + by) / 2;
                bool left = plan.RoomAt(mx + nx * 0.3, my + ny * 0.3) >= 0, right = plan.RoomAt(mx - nx * 0.3, my - ny * 0.3) >= 0;
                if (left == right) continue;
                // The normal pointing out of the building.
                if (left) (nx, ny) = (-nx, -ny);
                if (lv == 0 && len > longest.Len) longest = (run, nx, ny, len);
                if (lv == top) Quad(ax, ay, bx, by, nx, ny, y0 + H * 0.88, y0 + H * 0.96, off, c2);
                if (lv == 0 && street) Quad(ax + dx * 0.15, ay + dy * 0.15, bx - dx * 0.15, by - dy * 0.15, nx, ny, y0 + H * 0.84, y0 + H * 0.89, off, a);
                if (!windows) continue;
                // The windows lit (or dark), set back in the wall.
                foreach (var o in level.Openings.Where(o => o.Kind == OpeningKind.Window))
                {
                    if (HouseKitMaker.Along(run, o.X, o.Y) is not { } steps) continue;
                    double t = steps / run.Steps, px = ax + (bx - ax) * t, py = ay + (by - ay) * t, hw = o.WidthMm * k / 2 / sq;
                    double v0 = y0 + o.SillMm * k, v1 = Math.Min(y0 + H - 0.5, v0 + o.HeightMm * k);
                    if (v1 <= v0) continue;
                    var colour = rnd.NextDouble() < 0.3 ? Unlit : Lit[rnd.Next(Lit.Length)];
                    Quad(px - dx * hw, py - dy * hw, px + dx * hw, py + dy * hw, nx, ny, v0, v1, -0.1 * Wt / sq, colour);
                }
            }
        }
        // A tall sign up the front of a bar, arcade or shop of three floors or more: a strip a sixth of a square wide.
        if (street && top >= 2 && longest.Run is not null)
        {
            var r = longest.Run!;
            double dx = (r.B.X - r.A.X) / longest.Len, dy = (r.B.Y - r.A.Y) / longest.Len, at = Math.Min(0.6, longest.Len * 0.2), wdt = 0.18;
            double sx = r.A.X + dx * at, sy = r.A.Y + dy * at;
            Quad(sx, sy, sx + dx * wdt, sy + dy * wdt, longest.Nx, longest.Ny, BuildingKitMaker.StoreyBase(s, 1) * k, BuildingKitMaker.StoreyBase(s, top) * k + H * 0.8, off + 2 * k / sq, a);
        }
    }

    // ---- bridges ----

    /// <summary>A bridge as a plain deck over the water with a parapet (or a rail for a plank bridge) along each side.</summary>
    static void AddBridge(Scene3D scene, BridgeSite site, double sq)
    {
        int x0 = site.Cells.Min(c => c.X), x1 = site.Cells.Max(c => c.X) + 1, y0 = site.Cells.Min(c => c.Y), y1 = site.Cells.Max(c => c.Y) + 1;
        bool planks = site.Road == Ground.Dirt;
        var deck = Look(planks ? Color.FromRgb(0x9A, 0x70, 0x48) : Color.FromRgb(0xB8, 0xB0, 0xA2));
        var side = Look(planks ? Color.FromRgb(0x6E, 0x4E, 0x30) : Color.FromRgb(0xA4, 0x9C, 0x90));
        double top = 0.35 * sq, rise = planks ? 0.35 * sq : 0.4 * sq, t = planks ? 0.06 * sq : 0.18 * sq;
        // A little onto each bank, so it meets the road.
        double ax = x0 - (site.Across ? 0.2 : 0), bx = x1 + (site.Across ? 0.2 : 0), ay = y0 - (site.Across ? 0 : 0.2), by = y1 + (site.Across ? 0 : 0.2);
        var outline = new PathsD { new PathD { new(ax * sq, -ay * sq), new(bx * sq, -ay * sq), new(bx * sq, -by * sq), new(ax * sq, -by * sq) } };
        if (!Clipper.IsPositive(outline[0])) outline[0].Reverse();
        var flat = new Frame3(new Vec3(0, 0, 0), new Vec3(1, 0, 0), new Vec3(0, 0, -1));
        scene.AddSlab(outline, flat, -1.2, top, deck, group: WallGroup);
        foreach (bool far in new[] { false, true })
        {
            var a = site.Across ? new Vec3(ax * sq, top, (far ? by : ay) * sq) : new Vec3((far ? bx : ax) * sq, top, ay * sq);
            var e = site.Across ? new Vec3(bx * sq, top, (far ? by : ay) * sq) : new Vec3((far ? bx : ax) * sq, top, by * sq);
            double L = (e - a).Length;
            var u = (e - a) * (1 / L);
            var wall = new PathsD { new PathD { new(0, 0), new(L, 0), new(L, rise), new(0, rise) } };
            double inward = far ? -t : t;
            scene.AddSlab(wall, new Frame3(a, u, new Vec3(0, 1, 0)), Math.Min(0, inward), Math.Max(0, inward), side, group: WallGroup);
        }
    }

    // ---- fences and walls ----

    static (double H, double T, Color Colour) FenceLook(FenceKind k, double sq) => k switch
    {
        FenceKind.Hedge => (0.5 * sq, 0.3 * sq, Color.FromRgb(0x4F, 0x7A, 0x3A)),
        FenceKind.StoneWall => (0.45 * sq, 0.16 * sq, Color.FromRgb(0xA8, 0xA1, 0x96)),
        FenceKind.CityWall => (1.8 * sq, 0.5 * sq, Color.FromRgb(0xB6, 0xAE, 0xA0)),
        _ => (0.4 * sq, 0.06 * sq, Color.FromRgb(0x8E, 0x68, 0x44)),
    };

    /// <summary>The fences and walls where they stand (open where roads go through), with a town wall's turrets and gatehouses.</summary>
    static void AddWalls(Scene3D scene, CityMap map, double sq)
    {
        var layout = WallLayout.Of(map);
        foreach (var piece in layout.Pieces)
        {
            var (h, t, colour) = FenceLook(piece.Fence.Kind, sq);
            if (piece.Fence.Ruined) h *= 0.6;
            var look = Look(colour);
            int n = piece.Points.Count, segs = piece.Closed && n > 2 ? n : n - 1;
            for (int i = 0; i < segs; i++)
                Block(scene, piece.Points[i], piece.Points[(i + 1) % n], 0, h, t, t / 2, look, sq);
        }
        foreach (var e in layout.Ends.Where(e => e.Kind == WallEndKind.Cap))
        {
            var (h, t, colour) = FenceLook(e.Fence.Kind, sq);
            // A turret a little taller and wider than the wall; a pillar or a post.
            var (len, wide, high) = e.Fence.Kind switch
            {
                FenceKind.CityWall => (0.8, 0.85, h + 0.4 * sq),
                FenceKind.StoneWall => (0.28, 0.28, h + 0.08 * sq),
                FenceKind.Fence => (0.12, 0.12, h + 0.06 * sq),
                _ => (0.0, 0.0, 0.0),
            };
            if (len <= 0) continue;
            Block(scene, e.At - e.Out * (len / 2), e.At + e.Out * (len / 2), 0, high, wide * sq, 0, Look(Scene3D.Shade(colour, 0.92)), sq);
        }
        foreach (var g in layout.Gates.Where(g => g.Fence.Kind == FenceKind.CityWall))
        {
            var (h, t, colour) = FenceLook(FenceKind.CityWall, sq);
            var look = Look(Scene3D.Shade(colour, 0.92));
            var u = (g.B - g.A).Unit;
            // A tower either side of the road and the gatehouse over it, high enough to pass under.
            Block(scene, g.A - u * 0.75, g.A, 0, h + 0.5 * sq, 0.95 * sq, 0, look, sq);
            Block(scene, g.B, g.B + u * 0.75, 0, h + 0.5 * sq, 0.95 * sq, 0, look, sq);
            Block(scene, g.A, g.B, h * 0.62, h + 0.25 * sq, 0.8 * sq, 0, look, sq);
        }
    }

    /// <summary>A block along the line from a to b (squares) from height h0 to h1 (mm), t thick (mm), reaching <paramref name="past"/> (mm) beyond each end.</summary>
    static void Block(Scene3D scene, CityPt a, CityPt b, double h0, double h1, double t, double past, SlabStyle look, double sq)
    {
        var pa = new Vec3(a.X * sq, 0, a.Y * sq);
        var pb = new Vec3(b.X * sq, 0, b.Y * sq);
        double L = (pb - pa).Length;
        if (L < 1e-6) return;
        var u = (pb - pa) * (1 / L);
        var outline = new PathsD { new PathD { new(-past, h0), new(L + past, h0), new(L + past, h1), new(-past, h1) } };
        scene.AddSlab(outline, new Frame3(pa, u, new Vec3(0, 1, 0)), -t / 2, t / 2, look, group: WallGroup);
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

    static void AddProp(Scene3D scene, Prop p, double sq, bool sci = false)
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
            case PropKind.Lamp when sci:
                // A light pylon: a dark tapered post, a glowing bar on its neck and a beacon on top.
                Slab(Box(0.09 * k, 0.09 * k), 0, 0.12 * k, Color.FromRgb(0xC8, 0xA8, 0x2A));
                Slab(Box(0.05 * k, 0.05 * k), 0.12 * k, 0.9 * k, Color.FromRgb(0x30, 0x34, 0x3A));
                scene.AddSlab(Box(0.05 * k, 0.03 * k), new Frame3(o + v * (0.04 * k), u, v), 0.55 * k, 0.85 * k, new SlabStyle((_, _) => Neon[1], Colors.Transparent, LineWidth.Px(0.5), Neon[1], Neon[1], LineWidth.Px(0.5)), group: PropGroup);
                Slab(Box(0.04 * k, 0.04 * k), 0.9 * k, 0.96 * k, Neon[0]);
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
