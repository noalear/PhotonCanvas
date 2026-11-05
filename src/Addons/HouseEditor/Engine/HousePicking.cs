using System.Text.RegularExpressions;
using Clipper2Lib;
using PhotonCanvas.Addon.Buildings;
using PhotonCanvas.Addon.Furniture;
using Geo = PhotonCanvas.Addon.Walls.Geo;

namespace PhotonCanvas.Addon.HouseEditor;

/// <summary>What kind of thing is picked on the plan.</summary>
public enum PickKind { Opening, Wall, Column, Railing, Roof, Room, Item, Way }

/// <summary>
/// One thing picked on the plan: on which floor, and which: a door or window, a roof, furniture or a way up by
/// its number (<see cref="Index"/>), a column by its grid point, a wall or a railing by one square of it
/// (<see cref="Edge"/>), a room by a point in it (squares).
/// </summary>
public sealed record PlanPick(int Level, PickKind Kind, int Index = -1, GridPt? At = null, WallEdge? Edge = null, (double X, double Y)? Point = null);

/// <summary>
/// Clicking the building in 3D: which thing on the plan a piece under the pointer belongs to (a click on a wall's
/// face picks the wall, or the door or window it was next to; a floor tile picks its room), and which pieces
/// belong to what's picked, to show it picked in 3D too.
/// </summary>
public static partial class HousePicking
{
    /// <summary>A door's leaf hanging in its doorway (it's made as furniture, so it can swing).</summary>
    [GeneratedRegex(@"^Wall [A-Z]+ door$")]
    private static partial Regex DoorLeaf();

    static bool IsLeaf(HouseSolid s) => s.Kind == SolidKind.Furniture && DoorLeaf().IsMatch(s.Name);

    static bool IsShutter(HouseSolid s) => s.Kind == SolidKind.Wall && s.Name.Contains("shutter", StringComparison.OrdinalIgnoreCase);

    /// <summary>Where a point of the building (mm, as in the 3D view) is on the plan (squares).</summary>
    public static (double X, double Y) ToPlan(HouseKit kit, HouseDesign d, V3 p)
    {
        double sq = Math.Max(5, d.SquareMm);
        return (p.X / sq + kit.Origin.X, p.Z / sq + kit.Origin.Y);
    }

    /// <summary>The middle of a piece, where it is in the building (closed, as made).</summary>
    public static V3 Centre(HouseSolid s)
    {
        var b = Geo.Bounds(s.Outline);
        return s.Pose.At((b.left + b.right) / 2, (b.top + b.bottom) / 2);
    }

    /// <summary>
    /// What a click at <paramref name="at"/> (mm, in the building) on piece <paramref name="s"/> picks on the plan;
    /// null when it's nothing that can be picked (a floor outside every room, say).
    /// </summary>
    public static PlanPick? Pick(HouseDesign d, HouseKit kit, HouseSolid s, V3 at)
    {
        if (d.Levels.Count == 0) return null;
        int k = Math.Clamp(s.Level, 0, d.Levels.Count - 1);
        var level = d.Levels[k];
        var plan = level.Shape();
        double sq = Math.Max(5, d.SquareMm);
        var p = ToPlan(kit, d, at);
        switch (s.Kind)
        {
            case SolidKind.Wall:
            {
                // On the wall's middle line, level with where it was clicked; how high up its storey.
                var f = new V3(s.Facing.X, 0, s.Facing.Z);
                var on = at;
                if (f.Length > 1e-6)
                {
                    var along = new V3(f.Z, 0, -f.X) * (1 / f.Length);
                    on = s.Middle + along * (at - s.Middle).Dot(along);
                }
                var c = ToPlan(kit, d, on);
                double up = at.Y - (s.Middle.Y - BuildingKitMaker.StoreyHeight(HouseKitMaker.StyleFor(d)) / 2);
                int o = OpeningAround(level, sq, c, up);
                if (o >= 0) return new PlanPick(k, PickKind.Opening, o);
                if (NearestEdge(level.Walls, c, 0.6) is { } e) return new PlanPick(k, PickKind.Wall, Edge: e);
                return JointNear(plan, p, 1) is { } j ? new PlanPick(k, PickKind.Column, At: j) : null;
            }
            case SolidKind.Column:
                return JointNear(plan, p, Math.Max(0.75, d.Style.ColumnMm / sq)) is { } joint ? new PlanPick(k, PickKind.Column, At: joint) : null;
            case SolidKind.Railing:
                return k > 0 && NearestEdge(OpenRuns(d, k).SelectMany(PlanEditor.EdgesOf), p, 0.75) is { } rail ? new PlanPick(k, PickKind.Railing, Edge: rail) : null;
            case SolidKind.Roof:
            {
                int best = -1;
                double bestD = double.MaxValue;
                for (int i = 0; i < d.Roofs.Count; i++)
                {
                    var r = d.Roofs[i];
                    if (r.Level != s.Level) continue;
                    double grow = r.EaveMm / sq + 0.5;
                    double dx = Math.Max(Math.Max(r.X0 - grow - p.X, p.X - r.X1 - grow), 0), dy = Math.Max(Math.Max(r.Y0 - grow - p.Y, p.Y - r.Y1 - grow), 0);
                    // Inside more than one (they touch): the one whose middle is nearest.
                    double mx = (r.X0 + r.X1) / 2.0 - p.X, my = (r.Y0 + r.Y1) / 2.0 - p.Y;
                    double dist = (dx * dx + dy * dy) * 1e6 + mx * mx + my * my;
                    if (dist < bestD) { bestD = dist; best = i; }
                }
                return best >= 0 ? new PlanPick(Math.Clamp(d.Roofs[best].Level, 0, d.Levels.Count - 1), PickKind.Roof, best) : null;
            }
            case SolidKind.Floor or SolidKind.Ground:
                return plan.RoomAt(p.X, p.Y) >= 0 ? new PlanPick(k, PickKind.Room, Point: p) : null;
            case SolidKind.Furniture:
            {
                if (IsLeaf(s))
                {
                    int door = -1;
                    double best = 1.5;
                    for (int i = 0; i < level.Openings.Count; i++)
                    {
                        var o = level.Openings[i];
                        if (o.Kind != OpeningKind.Door) continue;
                        double dd = Math.Sqrt((o.X - p.X) * (o.X - p.X) + (o.Y - p.Y) * (o.Y - p.Y));
                        if (dd < best) { best = dd; door = i; }
                    }
                    return door >= 0 ? new PlanPick(k, PickKind.Opening, door) : null;
                }
                bool way = IsWayPart(s);
                if (!way)
                {
                    for (int i = level.Furniture.Count - 1; i >= 0; i--)
                        if (Inside(HouseKitMaker.ItemArea(d.Style, level.Furniture[i], sq), p, 0.05)) return new PlanPick(k, PickKind.Item, i);
                }
                var mm = new PointD(p.X * sq, p.Y * sq);
                for (int i = level.WaysUp.Count - 1; i >= 0; i--)
                {
                    var (hatch, foot) = HouseKitMaker.WayArea(d.Style, level.WaysUp[i], sq);
                    if (hatch.Concat(foot).Any(q => Clipper.PointInPolygon(mm, q) != PointInPolygonResult.IsOutside)) return new PlanPick(k, PickKind.Way, i);
                }
                // Not over its footprint (a part that leans out): the nearest of its kind.
                if (!way && Nearest(level.Furniture.Select(f => (f.X, f.Y)), p, 1.5) is >= 0 and var item) return new PlanPick(k, PickKind.Item, item);
                if (way && Nearest(level.WaysUp.Select(w => (w.X, w.Y)), p, 3) is >= 0 and var w) return new PlanPick(k, PickKind.Way, w);
                return null;
            }
            default:
                return null;
        }
    }

    /// <summary>Whether piece <paramref name="s"/> is (part of) what's <paramref name="pick"/>ed, to show it picked.</summary>
    public static bool Shows(HouseDesign d, HouseKit kit, HouseSolid s, PlanPick pick) => Matcher(d, kit, pick)(s);

    /// <summary>
    /// A test for the pieces that are (part of) what's <paramref name="pick"/>ed, to show it picked: worked out once
    /// (the floor's plan, the wall's run), then asked of every piece.
    /// </summary>
    public static Func<HouseSolid, bool> Matcher(HouseDesign d, HouseKit kit, PlanPick pick)
    {
        static bool None(HouseSolid _) => false;
        if (pick.Level < 0 || pick.Level >= d.Levels.Count) return None;
        var level = d.Levels[pick.Level];
        double sq = Math.Max(5, d.SquareMm);
        int k = pick.Level;
        (double X, double Y) C(HouseSolid s) => ToPlan(kit, d, Centre(s));
        switch (pick.Kind)
        {
            case PickKind.Roof:
            {
                if (pick.Index < 0 || pick.Index >= d.Roofs.Count) return None;
                var roof = d.Roofs[pick.Index];
                var rect = new RectD(roof.X0, roof.Y0, roof.X1, roof.Y1);
                double grow = roof.EaveMm / sq + 0.3;
                return s => s.Kind == SolidKind.Roof && s.Level == roof.Level && Inside(rect, C(s), grow);
            }
            case PickKind.Opening:
            {
                if (pick.Index < 0 || pick.Index >= level.Openings.Count) return None;
                var o = level.Openings[pick.Index];
                double reach = o.WidthMm / sq / 2 + 0.35;
                return s =>
                {
                    if (s.Level != k || !(IsLeaf(s) || IsShutter(s))) return false;
                    var c = C(s);
                    return Math.Sqrt((o.X - c.X) * (o.X - c.X) + (o.Y - c.Y) * (o.Y - c.Y)) <= reach;
                };
            }
            case PickKind.Wall:
            {
                if (pick.Edge is not { } e || PlanEditor.RunOf(level.Shape(), e) is not { } run) return None;
                double ax = run.A.X, ay = run.A.Y, dx = run.B.X - ax, dy = run.B.Y - ay, len = Math.Sqrt(dx * dx + dy * dy);
                if (len < 1e-9) return None;
                return s =>
                {
                    if (s.Kind != SolidKind.Wall || s.Level != k) return false;
                    // Its middle on the run, and facing across it.
                    var m = ToPlan(kit, d, s.Middle);
                    double t = ((m.X - ax) * dx + (m.Y - ay) * dy) / (len * len);
                    double off = Math.Abs((m.X - ax) * dy - (m.Y - ay) * dx) / len;
                    double fl = Math.Sqrt(s.Facing.X * s.Facing.X + s.Facing.Z * s.Facing.Z);
                    bool across = fl < 1e-9 || Math.Abs((s.Facing.X * dx + s.Facing.Z * dy) / (fl * len)) < 0.3;
                    return t > -0.01 && t < 1.01 && off < 0.25 && across;
                };
            }
            case PickKind.Column:
            {
                if (pick.At is not { } g) return None;
                double reach = d.Style.ColumnMm / sq / 2 + 0.3;
                return s =>
                {
                    if (s.Kind != SolidKind.Column || s.Level != k) return false;
                    var c = C(s);
                    return Math.Max(Math.Abs(c.X - g.X), Math.Abs(c.Y - g.Y)) <= reach;
                };
            }
            case PickKind.Railing:
            {
                if (pick.Edge is not { } e) return None;
                var runs = OpenRuns(d, k).Where(r => PlanEditor.EdgesOf(r).Contains(e)).ToList();
                if (runs.Count == 0) return None;
                return s => s.Kind == SolidKind.Railing && s.Level == k && runs.Any(r => SegmentDistance(C(s), r.A, r.B) < 0.4);
            }
            case PickKind.Room:
            {
                if (pick.Point is not { } pt) return None;
                var plan = level.Shape();
                int room = plan.RoomAt(pt.X, pt.Y);
                if (room < 0) return None;
                return s => s.Kind is SolidKind.Floor or SolidKind.Ground && s.Level == k && C(s) is var c && plan.RoomAt(c.X, c.Y) == room;
            }
            case PickKind.Item:
            {
                if (pick.Index < 0 || pick.Index >= level.Furniture.Count) return None;
                var area = HouseKitMaker.ItemArea(d.Style, level.Furniture[pick.Index], sq);
                return s => s.Kind == SolidKind.Furniture && s.Level == k && !IsLeaf(s) && !IsWayPart(s) && Inside(area, C(s), 0.15);
            }
            case PickKind.Way:
            {
                if (pick.Index < 0 || pick.Index >= level.WaysUp.Count) return None;
                var w = level.WaysUp[pick.Index];
                string name = HouseKitMaker.WayName(w.Kind);
                var (hatch, foot) = HouseKitMaker.WayArea(d.Style, w, sq);
                var area = Geo.Offset(Geo.Union(new PathsD(hatch.Concat(foot))), sq * 0.3);
                return s =>
                {
                    if (s.Kind != SolidKind.Furniture || s.Level != k || s.Name != name) return false;
                    var c = C(s);
                    return area.Any(q => Clipper.PointInPolygon(new PointD(c.X * sq, c.Y * sq), q) != PointInPolygonResult.IsOutside);
                };
            }
            default:
                return None;
        }
    }

    static bool IsWayPart(HouseSolid s) => Enum.GetValues<WayKind>().Any(k => HouseKitMaker.WayName(k) == s.Name);

    static List<RailingRun> OpenRuns(HouseDesign d, int k) => k <= 0 || k >= d.Levels.Count ? [] :
        HouseKitMaker.RailingRuns(d.Levels[k] with { Railings = true, NoRailing = [] }, d.Levels[k - 1].Shape(), d.Levels[k].Shape(), d.Style.WallSheetMm, d.Style.ColumnMm);

    /// <summary>The door or window in a wall around a point on its middle line (squares), <paramref name="upMm"/> above its floor; −1 if none.</summary>
    static int OpeningAround(Level level, double sq, (double X, double Y) c, double upMm)
    {
        int best = -1;
        double bestD = double.MaxValue;
        for (int i = 0; i < level.Openings.Count; i++)
        {
            var o = level.Openings[i];
            double dd = Math.Sqrt((o.X - c.X) * (o.X - c.X) + (o.Y - c.Y) * (o.Y - c.Y));
            double bottom = o.Kind == OpeningKind.Window ? o.SillMm : 0;
            // A little round it too: its frame.
            if (dd > o.WidthMm / sq / 2 + 2.5 / sq || upMm < bottom - 3 || upMm > bottom + o.HeightMm + 3) continue;
            if (dd < bestD) { bestD = dd; best = i; }
        }
        return best;
    }

    static WallEdge? NearestEdge(IEnumerable<WallEdge> edges, (double X, double Y) p, double reach)
    {
        WallEdge? best = null;
        double bestD = reach;
        foreach (var w in edges)
        {
            double d = SegmentDistance(p, w.A, w.B);
            if (d < bestD) { bestD = d; best = w; }
        }
        return best;
    }

    static double SegmentDistance((double X, double Y) p, GridPt a, GridPt b)
    {
        double ax = a.X, ay = a.Y, dx = b.X - ax, dy = b.Y - ay, len2 = dx * dx + dy * dy;
        double t = len2 < 1e-12 ? 0 : Math.Clamp(((p.X - ax) * dx + (p.Y - ay) * dy) / len2, 0, 1);
        double px = ax + t * dx - p.X, py = ay + t * dy - p.Y;
        return Math.Sqrt(px * px + py * py);
    }

    static GridPt? JointNear(PlanShape plan, (double X, double Y) p, double reach)
    {
        GridPt? best = null;
        double bestD = reach;
        foreach (var g in plan.Joints.Keys)
        {
            double d = Math.Max(Math.Abs(g.X - p.X), Math.Abs(g.Y - p.Y));
            if (d < bestD) { bestD = d; best = g; }
        }
        return best;
    }

    static int Nearest(IEnumerable<(double X, double Y)> points, (double X, double Y) p, double reach)
    {
        int best = -1, i = 0;
        double bestD = reach;
        foreach (var q in points)
        {
            double d = Math.Sqrt((q.X - p.X) * (q.X - p.X) + (q.Y - p.Y) * (q.Y - p.Y));
            if (d < bestD) { bestD = d; best = i; }
            i++;
        }
        return best;
    }

    static bool Inside(RectD r, (double X, double Y) p, double grow) =>
        p.X >= Math.Min(r.left, r.right) - grow && p.X <= Math.Max(r.left, r.right) + grow && p.Y >= Math.Min(r.top, r.bottom) - grow && p.Y <= Math.Max(r.top, r.bottom) + grow;
}
