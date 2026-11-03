using Clipper2Lib;
using PhotonCanvas.Addon.Stairs;
using PhotonCanvas.Addon.Walls;
using Roofs = PhotonCanvas.Addon.Roofs;
using Pose = PhotonCanvas.Addon.Furniture.Pose;
using V3 = PhotonCanvas.Addon.Furniture.V3;
using Geo = PhotonCanvas.Addon.Walls.Geo;

namespace PhotonCanvas.Addon.Buildings;

/// <summary>
/// The floors and roofs of buildings that aren't rectangles (an L, T or U, a hexagon, an octagon, a round
/// building, or a shape drawn by hand). Rectangles keep the stair maker's and roof maker's own floors and roofs.
/// </summary>
public static partial class BuildingKitMaker
{
    /// <summary>
    /// The room inside the walls on the plan: inside the walls' inner faces and clear of the corner columns,
    /// with <paramref name="gap"/> mm to spare from both.
    /// </summary>
    public static PathsD RoomInside(BuildingSpec s, Footprint fp, double gap)
    {
        var inside = fp.Outline(-(WallThickness(s) / 2 + gap));
        return Geo.Subtract(inside, ColumnOutlines(s, fp, s.ColumnMm + 2 * gap));
    }

    /// <summary>The corner columns on the plan, each <paramref name="across"/> mm across, turned as they stand.</summary>
    public static PathsD ColumnOutlines(BuildingSpec s, Footprint fp, double across)
    {
        var all = new PathsD();
        foreach (var c in fp.Corners)
        {
            var shape = Connectors.PlateShape(ColumnShapeAt(s, c));
            all.AddRange(Geo.Turn([Connectors.Outline(shape, across)], c.YawDeg, c.At.x, c.At.y));
        }
        return Geo.Union(all);
    }

    /// <summary>
    /// The floors between storeys (and under the battlements) for a building that isn't a rectangle: a plate
    /// as big as the outside of its columns, lying on the walls and the columns, with the floors' texture
    /// etched on. The way up and its opening are the stair maker's, made for the biggest rectangle that fits
    /// in the room, in its back right corner.
    /// </summary>
    static void ShapedFloors(BuildingSpec s, Footprint fp, int storeys, int wallStoreys, List<Piece> pieces, List<Solid> solids, List<Finding> findings,
        List<(int Storey, double X0, double Z0, double X1, double Z1)> blocked)
    {
        int count = wallStoreys - 1;
        if (count <= 0) return;
        double T = s.WallSheetMm, Wt = WallThickness(s), H = StoreyHeight(s);
        var b = fp.Bounds;
        var outline = fp.Outline(s.ColumnMm / 2);

        // The texture: the stair maker's floor over the building's whole bounds, cut to the plate. Its plan runs
        // from the back right corner, turned half round, as the rectangle's floors are.
        var texture = FloorSpecFor(s, 1) with
        {
            WallLengthMm = b.right - b.left, WallWidthMm = b.bottom - b.top, Columns = false, Hatch = false, Way = WayUp.None, WaysUp = 1,
            OverhangMm = s.ColumnMm,
        };
        PathsD etched = [];
        List<PathD> lines = [];
        if (FloorKitMaker.Make(texture).Parts.FirstOrDefault(p => p.Name == "Floor") is { } tex)
        {
            PointD Turned(PointD p) => new(b.right - p.x, b.bottom - p.y);
            etched = Geo.Map(tex.Etched, Turned);
            lines = Geo.Map(tex.Lines, Turned);
        }

        // The way up, in the biggest rectangle that fits in the room.
        PathsD hatch = [];
        FloorKit? way = null;
        double cx1 = 0, cy1 = 0;
        if (s.WayUp != WayUpStyle.None)
        {
            var room = Footprint.LargestRectangle(RoomInside(s, fp, 0.6));
            if (room is not { } r || r.right - r.left < 20 || r.bottom - r.top < 20)
                findings.Add(new(Severity.Warn, "The rooms are too small for a way up: the floors have no opening. Make the building bigger."));
            else
            {
                // Walls of the stair maker's room stand round the rectangle, their middles half a wall outside it,
                // so their inside faces are its edges.
                var spec = FloorSpecFor(s, count) with { WallLengthMm = r.right - r.left + Wt, WallWidthMm = r.bottom - r.top + Wt, Columns = false };
                var kit = FloorKitMaker.Make(spec);
                if (kit.Ok)
                {
                    way = kit;
                    cx1 = r.right + Wt / 2;
                    cy1 = r.bottom + Wt / 2;
                    hatch = Geo.Map(kit.Layout.Hatch, p => new PointD(cx1 - p.x, cy1 - p.y));
                    foreach (var f in kit.Findings.Where(f => f.Level == Walls.Severity.Warn))
                        findings.Add(new(Severity.Warn, $"Floors: {f.Text}"));
                }
                else findings.Add(new(Severity.Warn, "The way up doesn't fit in the rooms, so the floors have no opening: make the building bigger or pick another way up."));
            }
        }

        // The plate: flat walls' columns carry a pin up through it.
        var pins = s.BoxWalls ? [] : new PathsD(fp.Corners.Select(c => Geo.Circle(c.At.x, c.At.y, PinMm / 2)));
        var cut = Geo.Subtract(outline, Geo.Union(hatch, pins));
        var keep = Geo.Subtract(cut, Geo.Offset(pins, 0.8));
        etched = Geo.Intersect(Geo.Union(etched), keep);
        lines = Geo.ClipLines(lines, keep);
        pieces.Add(new Piece("Floor", Sheet.Walls, cut, etched, lines, count));
        for (int k = 1; k <= count; k++)
            solids.Add(new Solid(k == storeys ? "Top floor" : "Floor", cut, Flat(0, StoreyBase(s, k - 1) + H + T / 2, 0, 0), T, Nil(etched), Nil(lines), k, WallSide.Floor, false));

        if (way is null) return;
        V3 Point(Stairs.V3 p, double up) => new(cx1 - p.X, p.Y + up, -cy1 - p.Z);
        V3 Dir(Stairs.V3 p) => new(-p.X, p.Y, -p.Z);
        foreach (var part in way.Parts.Where(p => p.Name != "Floor"))
        {
            pieces.Add(new Piece(part.Name, Sheet.Walls, part.Cut, part.Etched, part.Lines, part.Copies));
            for (int k = 1; k <= count; k++)
            {
                double up = StoreyBase(s, k - 1);
                foreach (var pose in part.Poses)
                {
                    var solid = new Solid(part.Name, part.Cut, new Pose(Point(pose.O, up), Dir(pose.U), Dir(pose.V)), T, Nil(part.Etched), Nil(part.Lines), k - 1, WallSide.None, false);
                    solids.Add(solid);
                    var pb = Geo.Bounds(part.Cut);
                    var pts = new[] { solid.Pose.At(pb.left, pb.top), solid.Pose.At(pb.right, pb.top), solid.Pose.At(pb.left, pb.bottom), solid.Pose.At(pb.right, pb.bottom) };
                    blocked.Add((k - 1, pts.Min(q => q.X), pts.Min(q => q.Z), pts.Max(q => q.X), pts.Max(q => q.Z)));
                }
            }
        }
        if (hatch.Count > 0)
        {
            var hb = Geo.Bounds(hatch);
            for (int k = 1; k <= count; k++) blocked.Add((k, hb.left, -hb.bottom, hb.right, -hb.top));
        }
        string where = "in the room";
        if (hatch.Count > 0)
        {
            var hb = Geo.Bounds(hatch);
            double hx = (hb.left + hb.right) / 2, hy = (hb.top + hb.bottom) / 2, span = Math.Max(fp.Bounds.right - fp.Bounds.left, fp.Bounds.bottom - fp.Bounds.top) * 0.1;
            string fb = hy > span ? "back" : hy < -span ? "front" : "", lr = hx > span ? "right" : hx < -span ? "left" : "";
            where = fb.Length + lr.Length == 0 ? "in the middle of the room" : $"at the {(fb.Length > 0 && lr.Length > 0 ? fb + " " + lr : fb + lr)} of the room";
        }
        findings.Add(new(Severity.Note, $"Each floor has an opening {where} with {(s.WayUp == WayUpStyle.Ladder ? "a ladder" : s.WayUp == WayUpStyle.Spiral ? "spiral stairs" : "stairs")} up to it from the storey below."));
    }

    /// <summary>
    /// The roof on a building that isn't a rectangle: a pointed roof over a hexagon, an octagon or a round
    /// building; crossing gables over an L, T or U. It rests on the top storey's column caps, with a locator
    /// frame under its base that drops inside the walls.
    /// </summary>
    static void ShapedRoof(BuildingSpec s, Footprint fp, List<Piece> pieces, List<Solid> solids, List<Finding> findings)
    {
        var shape = s.RoofFor();
        if (shape == RoofShape.None || s.Top != BuildingTop.Straight) return;
        int top = Math.Clamp(s.Storeys, 1, 6) - 1;
        var wall = Topped(StoreyWall(s, top, s.WidthMm), true);
        double T = s.WallSheetMm, lip = WallKitMaker.ColumnLip(wall), grow = s.ColumnMm / 2 + lip;
        // The roof's Z = 0 is the top of the column caps it rests on.
        double baseY = StoreyBase(s, top) + WallKitMaker.ColumnTopMm(wall);
        double rest = Math.Max(0, WallKitMaker.ColumnTopMm(wall) - wall.HeightMm);
        int layers = Math.Max(1, (int)Math.Ceiling((rest + Roofs.RoofMaker.GripMm) / T - 1e-6));
        var locator = Locator(s, fp, lip, findings);
        var look = RoofLook(s) with { ThicknessMm = T, FitMm = s.FitMm };

        if (fp.IsRegular)
        {
            var kit = Roofs.PolyRoof.Pointed(look, fp.Sides, fp.Apothem + grow, -90, locator, layers);
            Report(kit, findings);
            if (!kit.Ok) return;
            V3 Point(Roofs.V3 p) => new(p.X, baseY + p.Z, -p.Y);
            V3 Dir(Roofs.V3 d) => new(d.X, d.Z, -d.Y);
            foreach (var part in kit.Parts) AddRoofPart(part, part.Cut, part.Etched, part.Lines, part.Poses, Point, Dir, kit.ThicknessMm, top, pieces, solids);
            findings.Add(new(Severity.Note, "The roof is glued together on its base and lifts off: the frame under the base drops inside the top walls so it can't slide."));
            return;
        }
        if (fp.IsWinged && fp.Wings.Count >= 2) CrossGables(s, fp, look, wall, grow, baseY, layers, locator, top, pieces, solids, findings);
    }

    static void Report(Roofs.RoofKit kit, List<Finding> findings)
    {
        foreach (var f in kit.Findings.Where(f => f.Level != Walls.Severity.Note))
            findings.Add(new(f.Level == Walls.Severity.Bad ? Severity.Bad : Severity.Warn, $"Roof: {f.Text}"));
    }

    /// <summary>One of the roof's pieces, cut and placed (the roof's model mapped into the building).</summary>
    static void AddRoofPart(Roofs.RoofPart part, PathsD cut, PathsD etched, List<PathD> lines, IReadOnlyList<Roofs.Pose> poses, Func<Roofs.V3, V3> point, Func<Roofs.V3, V3> dir,
        double T, int top, List<Piece> pieces, List<Solid> solids, string? called = null)
    {
        if (cut.Count == 0 || poses.Count == 0) return;
        string name = called ?? (part.Name.StartsWith("Roof", StringComparison.Ordinal) ? part.Name : "Roof " + char.ToLowerInvariant(part.Name[0]) + part.Name[1..]);
        pieces.Add(new Piece(name, Sheet.Walls, cut, etched, lines, poses.Count));
        foreach (var pose in poses)
        {
            // The roof maker's pieces run from w = 0 to T; a solid here is centred on its pose.
            var mid = pose.O + pose.N * (T / 2);
            solids.Add(new Solid(name, cut, new Pose(point(mid), dir(pose.U), dir(pose.V)), T, Nil(etched), Nil(lines), top + 1, WallSide.Roof, false));
        }
    }

    /// <summary>
    /// The frame under a roof's base, on the plan: inside the top walls (with the roof maker's gap) and clear of
    /// the column caps, a band round the inside of the walls. Empty, with a warning, if there's no room.
    /// </summary>
    static PathsD Locator(BuildingSpec s, Footprint fp, double lip, List<Finding> findings)
    {
        double clear = Roofs.RoofMaker.ClearMm;
        var region = Geo.Subtract(fp.Outline(-(WallThickness(s) / 2 + clear)), Geo.Offset(ColumnOutlines(s, fp, s.ColumnMm + 2 * lip), clear));
        // Bits too thin to cut are dropped.
        region = Geo.Offset(Geo.Offset(region, -1.5), 1.5);
        if (Geo.Area(region) < 100)
        {
            findings.Add(new(Severity.Warn, "The walls leave no room inside for the locator under the roof: it has none, so it can slide."));
            return [];
        }
        var frame = Geo.Subtract(region, Geo.Offset(region, -8));
        if (Geo.Pieces(frame) == 1) return Geo.Tidy(frame);
        return Geo.Pieces(region) == 1 ? Geo.Tidy(region) : [Geo.Tidy(region).OrderByDescending(p => Math.Abs(Clipper.Area(p))).First()];
    }

    /// <summary>
    /// Crossing gables over an L, T or U: the roof maker's gable roof over the wing along the back, and one
    /// over each wing coming forward, running back into the first one's front slope (cut where they meet, so
    /// each rests on it along the valley). They share one base plate in the footprint's shape, with one locator.
    /// </summary>
    static void CrossGables(BuildingSpec s, Footprint fp, Roofs.RoofSpec look, WallSpec wall, double grow, double baseY, int layers, PathsD locator, int top,
        List<Piece> pieces, List<Solid> solids, List<Finding> findings)
    {
        double T = s.WallSheetMm, fit = Math.Max(0, s.FitMm);
        double pitch = Math.Clamp(look.PitchDeg, 10, 75) * Math.PI / 180, c = Math.Cos(pitch), sn = Math.Sin(pitch), e = Math.Max(0, look.EaveMm);
        var main = fp.Wings[0];
        var legs = fp.Wings.Skip(1).ToList();
        // A wing's roof, outer size given, fitted to walls like the building's (its gables over the walls); its own locator isn't used.
        Roofs.RoofKit Wing(double length, double width) => Roofs.RoofMaker.Make(Roofs.RoofMaker.FitToWalls(look with { Style = Roofs.RoofStyle.Gable }, wall, wall) with
        {
            LengthMm = Math.Round(length, 2), WidthMm = Math.Round(width, 2),
        });

        double Lm = main.X1 - main.X0 + 2 * grow, Wm = main.Y1 - main.Y0 + 2 * grow, x0m = main.X0 - grow, cym = (main.Y0 + main.Y1) / 2;
        double front = main.Y0 - grow;   // the back wing's base's front edge, on the plan
        var mainKit = Wing(Lm, Wm);
        Report(mainKit, findings);
        if (!mainKit.Ok) return;
        V3 MainPoint(Roofs.V3 p) => new(x0m + p.X, baseY + p.Z, -(cym + p.Y));
        V3 MainDir(Roofs.V3 d) => new(d.X, d.Z, -d.Y);
        PointD MainPlan(PointD q) => new(x0m + q.x, cym + q.y - Wm / 2);   // the base's flat (u, v) → the plan

        var baseOutline = new PathsD { Geo.Rect(main.X0 - grow, main.Y0 - grow, main.X1 + grow, main.Y1 + grow) };
        var slots = new PathsD();
        if (mainKit.Parts.FirstOrDefault(p => p.Name == "Base") is { } mb)
            slots.AddRange(Geo.Map(Geo.Subtract([Geo.Rect(0, 0, Lm, Wm)], mb.Cut), MainPlan));

        // The back wing: its front slope's eave is cut away where the other wings' bases run under it.
        foreach (var part in mainKit.Parts.Where(p => p.Name is not ("Base" or "Locator")))
        {
            var cut = part.Cut;
            if (part.Name == "Slope (left)")
            {
                double go = look.GableOverhangMm;
                var notches = new PathsD(legs.Select(l => Geo.Rect(l.X0 - grow - x0m + go, -1, l.X1 + grow - x0m + go, e + 0.01)));
                cut = Geo.Subtract(cut, notches);
            }
            AddRoofPart(part, cut, Geo.Intersect(part.Etched, cut), Geo.ClipLines(part.Lines, cut), part.Poses, MainPoint, MainDir, mainKit.ThicknessMm, top, pieces, solids);
        }

        // The back wing's front slope's outside face, on the plan with height: n · (p − q) = T on it.
        var n = (X: 0.0, Y: -sn, Z: c);
        double Above(double x, double y, double z) => n.X * x + n.Y * (y - front) + n.Z * (z - T) - T;
        double margin = e * c + 2 * T + 2;
        for (int i = 0; i < legs.Count; i++)
        {
            var leg = legs[i];
            double cx = (leg.X0 + leg.X1) / 2, y0 = leg.Y0 - grow, Ll = cym - y0, Wl = leg.X1 - leg.X0 + 2 * grow;
            // The wing's roof runs from its front to the back wing's ridge.
            var kit = Wing(Ll, Wl);
            Report(kit, findings);
            if (!kit.Ok) return;
            // The model's X runs from the wing's front back towards the back wing; its Y to the left on the plan.
            (double X, double Y, double Z) Plan3(Roofs.V3 p) => (cx - p.Y, y0 + p.X, p.Z);
            V3 Point(Roofs.V3 p) => new(cx - p.Y, baseY + p.Z, -(y0 + p.X));
            V3 Dir(Roofs.V3 d) => new(-d.Y, d.Z, -d.X);
            PointD Plan(PointD q) => new(cx - (q.y - Wl / 2), y0 + q.x);
            string called = legs.Count == 1 ? "Roof wing" : cx < 0 ? "Roof left wing" : cx > 0 ? "Roof right wing" : "Roof front wing";
            double cutoff = front - margin;

            baseOutline.Add(Geo.Rect(leg.X0 - grow, y0, leg.X1 + grow, front + 0.01));
            if (kit.Parts.FirstOrDefault(p => p.Name == "Base") is { } lb)
                slots.AddRange(Geo.Map(Geo.Subtract([Geo.Rect(0, 0, Ll, Wl)], lb.Cut), Plan).Where(p => Clipper.GetBounds(p).bottom < cutoff));

            foreach (var part in kit.Parts.Where(p => p.Name is not ("Base" or "Locator")))
            {
                string name = $"{called} {char.ToLowerInvariant(part.Name[0])}{part.Name[1..]}";
                if (part.Name is "Gable end" or "Truss" || part.Name.StartsWith("Truss", StringComparison.Ordinal))
                {
                    // The gable at the back and the trusses under the back wing's slope go: only those in front of it stay.
                    var kept = part.Poses.Where(p => y0 + Math.Max(p.O.X, p.At(0, 0, T).X) < cutoff).ToList();
                    AddRoofPart(part, part.Cut, part.Etched, part.Lines, kept, Point, Dir, kit.ThicknessMm, top, pieces, solids, name);
                    continue;
                }
                // Slopes and the ridge cap: cut along the valley, where they meet the back wing's front slope.
                var pose = part.Poses[0];
                double G(double u, double v) { var q = Plan3(pose.At(u, v)); return Above(q.X, q.Y, q.Z); }
                var keep = HalfPlane(part.Cut, G);
                if (part.Name.Contains("cap", StringComparison.Ordinal))
                {
                    // The ridge cap stops short of the back wing's ridge cap.
                    double cw = 2 * T + 2;
                    keep = Geo.Intersect(keep, HalfPlane(part.Cut, (u, v) => cym - cw / 2 - 0.5 - Plan3(pose.At(u, v)).Y));
                }
                var cut = Geo.Intersect(part.Cut, keep);
                AddRoofPart(part, cut, Geo.Intersect(part.Etched, cut), Geo.ClipLines(part.Lines, cut), part.Poses, Point, Dir, kit.ThicknessMm, top, pieces, solids, name);
            }
        }

        // One base for all the wings, and one locator under it.
        var basePlate = Geo.Subtract(Geo.Union(baseOutline), Geo.Union(slots));
        pieces.Insert(pieces.FindIndex(p => p.Name.StartsWith("Roof", StringComparison.Ordinal)) is >= 0 and var at ? at : pieces.Count,
            new Piece("Roof base", Sheet.Walls, basePlate, [], [], 1));
        solids.Add(new Solid("Roof base", basePlate, Flat(0, baseY + T / 2, 0, 0), T, null, null, top + 1, WallSide.Roof, false));
        if (locator.Count > 0)
        {
            pieces.Add(new Piece("Roof locator", Sheet.Walls, locator, [], [], layers));
            for (int i = 0; i < layers; i++)
                solids.Add(new Solid("Roof locator", locator, Flat(0, baseY - (i + 0.5) * T, 0, 0), T, null, null, top + 1, WallSide.Roof, false));
        }
        findings.Add(new(Severity.Note, $"The wing{(legs.Count == 1 ? "'s roof runs" : "s' roofs run")} back into the back wing's front slope: glue {(legs.Count == 1 ? "its slopes" : "their slopes")} to it along the valley{(legs.Count == 1 ? "" : "s")}."));
        findings.Add(new(Severity.Note, "The roof is glued together on its base and lifts off: the frame under the base drops inside the top walls so it can't slide."));
    }

    /// <summary>The part of a piece's flat plane (round its outline) where <paramref name="g"/> (straight in u and v) is 0 or more.</summary>
    static PathsD HalfPlane(PathsD outline, Func<double, double, double> g)
    {
        var b = Geo.Bounds(outline);
        var poly = new List<PointD> { new(b.left - 5, b.top - 5), new(b.right + 5, b.top - 5), new(b.right + 5, b.bottom + 5), new(b.left - 5, b.bottom + 5) };
        var kept = new PathD();
        for (int i = 0; i < poly.Count; i++)
        {
            var p = poly[i];
            var q = poly[(i + 1) % poly.Count];
            double gp = g(p.x, p.y), gq = g(q.x, q.y);
            if (gp >= 0) kept.Add(p);
            if ((gp >= 0) != (gq >= 0))
            {
                double t = gp / (gp - gq);
                kept.Add(new PointD(p.x + (q.x - p.x) * t, p.y + (q.y - p.y) * t));
            }
        }
        return kept.Count >= 3 ? [kept] : [];
    }
}
