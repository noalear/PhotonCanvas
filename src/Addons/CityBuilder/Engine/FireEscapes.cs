using PhotonCanvas.Addon.Buildings;
using PhotonCanvas.Addon.Furniture;
using PhotonCanvas.Addon.HouseEditor;

namespace PhotonCanvas.Addon.CityBuilder;

/// <summary>
/// Fire escapes up the outsides of a cyberpunk city's taller buildings: on a side or back wall (never one
/// facing the street, never one with a door in it), against the wall's outside face, with a landing at each
/// floor that wall goes up to, as the furniture maker's fire escape so it's cut with the town's furniture.
/// </summary>
public static class FireEscapes
{
    /// <summary>The fewest floors (walls) a building has before it gets one.</summary>
    public const int FromFloors = 3;

    /// <summary>
    /// A fire escape for each of the buildings (with <see cref="FromFloors"/> floors or more, about two in
    /// three of them) that has a side or back wall long enough with room in front of it: on the map's ground
    /// <paramref name="g"/> (roads painted in), clear of <paramref name="taken"/> squares (which it adds to).
    /// </summary>
    public static List<CityItem> For(IEnumerable<CityBuilding> buildings, Ground[,] g, HashSet<(int X, int Y)> taken, string theme, double squareMm, Random rnd, double share = 0.65, Func<int, int, bool>? avoid = null)
    {
        var list = new List<CityItem>();
        if (TownPieces.Piece(theme, "fire-escape") is not { } entry) return list;
        int w = g.GetLength(0), d = g.GetLength(1);
        foreach (var b in buildings)
        {
            var design = b.Design;
            int floors = design.Levels.Count(l => l.Walls.Count > 0);
            if (floors < FromFloors || design.Levels.Count == 0 || rnd.NextDouble() > share) continue;
            var s = HouseKitMaker.StyleFor(design);
            double dsq = Math.Max(5, design.SquareMm), k = squareMm / dsq;
            double pitch = (BuildingKitMaker.StoreyBase(s, 1) - BuildingKitMaker.StoreyBase(s, 0)) * k;
            double half = BuildingKitMaker.WallThickness(s) / 2 / dsq;
            var proto = CityItems.Make(entry, squareMm);
            double ew = proto.WidthMm / squareMm, ed = proto.DepthMm / squareMm;
            var ground = design.Levels[0];
            var plan = ground.Shape();
            var doors = ground.Openings.Where(o => o.Kind != OpeningKind.Window).ToList();
            (CityItem Item, HashSet<(int, int)> Cells)? best = null;
            double bestLen = 0;
            foreach (var run in plan.Runs)
            {
                double ax = run.A.X, ay = run.A.Y, bx = run.B.X, by = run.B.Y, len = Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay));
                if (run.Diagonal || len < ew + 1 || len <= bestLen) continue;
                double dx = (bx - ax) / len, dy = (by - ay) / len, nx = dy, ny = -dx, mx = (ax + bx) / 2, my = (ay + by) / 2;
                bool left = plan.RoomAt(mx + nx * 0.3, my + ny * 0.3) >= 0, right = plan.RoomAt(mx - nx * 0.3, my - ny * 0.3) >= 0;
                if (left == right) continue;
                if (left) (nx, ny) = (-nx, -ny);
                if (doors.Any(o => HouseKitMaker.Along(run, o.X, o.Y) is not null)) continue;
                // How many floors up this wall goes (a tower's top floors step in).
                int up = 0;
                for (int lv = 1; lv < design.Levels.Count; lv++)
                {
                    var walls = design.Levels[lv].Walls;
                    if (walls.Count == 0 || !walls.Any(e => Covers(e, ax + dx * 0.5, ay + dy * 0.5)) || !walls.Any(e => Covers(e, bx - dx * 0.5, by - dy * 0.5))) break;
                    up = lv;
                }
                if (up < FromFloors - 1) continue;
                // Its middle, the depth of the wall's half and its own half out from the wall's line.
                double out_ = half + ed / 2 + 0.02;
                var (cx, cy) = CityFootprint.ToMap(b, mx + nx * out_, my + ny * out_);
                var (ox, oy) = CityFootprint.ToMap(b, mx, my);
                double mnx = cx - ox, mny = cy - oy, ml = Math.Sqrt(mnx * mnx + mny * mny);
                mnx /= ml; mny /= ml;
                // The squares it stands on must be open ground, not a street and not another building's.
                var cells = new HashSet<(int, int)>();
                double ux = -mny, uy = mnx;
                bool ok = true;
                for (double a = -ew / 2 + 0.05; a <= ew / 2 - 0.05 + 1e-9 && ok; a += Math.Max(0.1, (ew - 0.1) / 6))
                    for (double q = -ed / 2 + 0.05; q <= ed / 2 - 0.05 + 1e-9 && ok; q += Math.Max(0.1, (ed - 0.1) / 2))
                    {
                        int x = (int)Math.Floor(cx + ux * a + mnx * q), y = (int)Math.Floor(cy + uy * a + mny * q);
                        if (x < 0 || y < 0 || x >= w || y >= d || g[x, y] is Ground.Road or Ground.Water || taken.Contains((x, y)) || avoid?.Invoke(x, y) == true) ok = false;
                        else cells.Add((x, y));
                    }
                // Not right on a street either: a square past it stays off the road.
                int fx = (int)Math.Floor(cx + mnx * (ed / 2 + 0.6)), fy = (int)Math.Floor(cy + mny * (ed / 2 + 0.6));
                if (!ok || fx < 0 || fy < 0 || fx >= w || fy >= d || g[fx, fy] is Ground.Road) continue;
                // Facing out from the wall: its front (its own +v) along the outward normal.
                double turn = Math.Round(Math.Atan2(-mnx, mny) * 180 / Math.PI, 3);
                var item = proto with
                {
                    Id = Guid.NewGuid(), X = Math.Round(cx, 4), Y = Math.Round(cy, 4), TurnDeg = (turn % 360 + 360) % 360,
                    Item = TownWalls.FireEscapeFor(proto.Item, up, pitch),
                };
                best = (item, cells);
                bestLen = len;
            }
            if (best is { } found)
            {
                list.Add(found.Item);
                foreach (var c in found.Cells) taken.Add(c);
            }
        }
        return list;
    }

    /// <summary>Whether a wall's line passes through the plan point (x, y).</summary>
    static bool Covers(WallEdge e, double x, double y)
    {
        double ax = e.A.X, ay = e.A.Y, bx = e.B.X, by = e.B.Y, dx = bx - ax, dy = by - ay, len2 = dx * dx + dy * dy;
        if (len2 < 1e-9) return false;
        double t = ((x - ax) * dx + (y - ay) * dy) / len2, px = ax + dx * t - x, py = ay + dy * t - y;
        return t >= -1e-6 && t <= 1 + 1e-6 && px * px + py * py < 1e-6;
    }
}
