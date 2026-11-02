using PhotonCanvas.Addon.Furniture;
using PhotonCanvas.Addon.Walls;
using Geo = PhotonCanvas.Addon.Walls.Geo;

namespace PhotonCanvas.Addon.Buildings;

/// <summary>
/// Stands the furniture in its rooms for the 3D view: the biggest first, as far back and to the left as
/// it goes, turned a quarter if that fits better, clear of the corner columns, the walls, the doorway and
/// each other. It only places the pieces for the picture; the furniture is cut the same either way.
/// </summary>
public static class Interior
{
    /// <summary>A pose turned <paramref name="turn"/> degrees round the upright (anticlockwise seen from above), then moved.</summary>
    public static Pose Move(Pose p, double dx, double dy, double dz, double turn)
    {
        var o = Turn(p.O, turn);
        return new Pose(new V3(o.X + dx, o.Y + dy, o.Z + dz), Turn(p.U, turn), Turn(p.V, turn));
    }

    static V3 Turn(V3 v, double deg)
    {
        if (deg == 0) return v;
        double a = deg * Math.PI / 180, c = Math.Cos(a), s = Math.Sin(a);
        return new V3(v.X * c + v.Z * s, v.Y, -v.X * s + v.Z * c);
    }

    /// <summary>One piece of furniture's box (turned as given), from where its pieces sit.</summary>
    public static (V3 Min, V3 Max) Box(ItemKit kit, double t, double turn)
    {
        var lo = new V3(double.MaxValue, double.MaxValue, double.MaxValue);
        var hi = new V3(double.MinValue, double.MinValue, double.MinValue);
        foreach (var part in kit.Parts)
        {
            var b = Geo.Bounds(part.Cut);
            foreach (var pose in part.Poses)
                foreach (var u in new[] { b.left, b.right })
                    foreach (var v in new[] { b.top, b.bottom })
                        foreach (var w in new[] { -t / 2, t / 2 })
                        {
                            var q = Turn(pose.At(u, v, w), turn);
                            lo = new V3(Math.Min(lo.X, q.X), Math.Min(lo.Y, q.Y), Math.Min(lo.Z, q.Z));
                            hi = new V3(Math.Max(hi.X, q.X), Math.Max(hi.Y, q.Y), Math.Max(hi.Z, q.Z));
                        }
        }
        return (lo, hi);
    }

    readonly record struct Spot(double X0, double Z0, double X1, double Z1)
    {
        public bool Hits(Spot o, double gap) => X0 < o.X1 + gap && o.X0 < X1 + gap && Z0 < o.Z1 + gap && o.Z0 < Z1 + gap;
    }

    /// <summary>Stands every piece of furniture in its room and adds it to <paramref name="solids"/>.</summary>
    public static void Lay(BuildingSpec s, List<RoomItem> items, List<string> labels, SetKit kit, List<Solid> solids, List<Finding> findings)
    {
        double W = s.WidthMm, D = s.DepthMm, Wt = BuildingKitMaker.WallThickness(s), c = BuildingKitMaker.Clearance, col = s.ColumnMm / 2;
        double T = kit.ThicknessMm;
        // Inside the walls.
        double rx0 = -W / 2 + Wt / 2 + c, rx1 = W / 2 - Wt / 2 - c, rz0 = -D / 2 + Wt / 2 + c, rz1 = D / 2 - Wt / 2 - c;
        var crowded = new List<string>();
        for (int k = 0; k < s.Storeys; k++)
        {
            var taken = new List<Spot>
            {
                new(-W / 2 - col, -D / 2 - col, -W / 2 + col, -D / 2 + col), new(W / 2 - col, -D / 2 - col, W / 2 + col, -D / 2 + col),
                new(-W / 2 - col, D / 2 - col, -W / 2 + col, D / 2 + col), new(W / 2 - col, D / 2 - col, W / 2 + col, D / 2 + col),
            };
            if (k == 0)
            {
                // Room to come in by the door.
                double dw = s.DoorWidthMm / 2 + c;
                taken.Add(new(-dw, D / 2 - Wt / 2 - Math.Max(12, s.DoorWidthMm * 1.2), dw, D / 2));
            }
            double floor = BuildingKitMaker.StoreyBase(s, k);
            var here = items.Select((r, i) => (r, i)).Where(x => Math.Clamp(x.r.Storey, 0, s.Storeys - 1) == k && x.i < kit.Items.Count && kit.Items[x.i].Ok)
                .SelectMany(x => Enumerable.Repeat(x.i, Math.Max(1, x.r.Item.Count)))
                .Select(i => (Index: i, Box: Box(kit.Items[i], T, 0)))
                .OrderByDescending(x => (x.Box.Max.X - x.Box.Min.X) * (x.Box.Max.Z - x.Box.Min.Z))
                .ToList();
            foreach (var (i, _) in here)
            {
                Spot? best = null;
                double bestTurn = 0;
                (V3 Min, V3 Max) bestBox = default;
                foreach (var turn in new double[] { 0, 90 })
                {
                    var b = Box(kit.Items[i], T, turn);
                    double w = b.Max.X - b.Min.X, d = b.Max.Z - b.Min.Z;
                    var spot = Find(rx0, rz0, rx1, rz1, w, d, taken, c);
                    // Upright as made unless a quarter turn sits it further back.
                    if (spot is { } sp && (best is null || sp.Z0 < best.Value.Z0 - 0.5)) { best = sp; bestTurn = turn; bestBox = b; }
                }
                if (best is not { } at)
                {
                    var b = Box(kit.Items[i], T, 0);
                    double w = b.Max.X - b.Min.X, d = b.Max.Z - b.Min.Z;
                    at = new Spot(-w / 2, -d / 2, w / 2, d / 2);
                    bestTurn = 0;
                    bestBox = b;
                    string name = Lower(labels[i]);
                    if (!crowded.Contains(name)) crowded.Add(name);
                }
                taken.Add(at);
                double dx = at.X0 - bestBox.Min.X, dy = floor - bestBox.Min.Y, dz = at.Z0 - bestBox.Min.Z;
                string label = labels[i];
                foreach (var part in kit.Items[i].Parts)
                    foreach (var pose in part.Poses)
                        solids.Add(new Solid(label, part.Cut, Move(pose, dx, dy, dz, bestTurn), T, part.Etched.Count > 0 ? part.Etched : null, part.Lines.Count > 0 ? part.Lines : null, k, WallSide.None, true));
            }
        }
        if (crowded.Count > 0)
            findings.Add(new(Severity.Warn, $"The rooms are too small for all the furniture ({string.Join(", ", crowded)} doesn't fit beside the rest). It's all still cut; make the building bigger or take some out."));
    }

    /// <summary>The first free place for a w × d box, from the back left, in 0.5 mm steps; null if there's none.</summary>
    static Spot? Find(double x0, double z0, double x1, double z1, double w, double d, List<Spot> taken, double gap)
    {
        const double step = 0.5;
        for (double z = z0; z + d <= z1 + 1e-9; z += step)
            for (double x = x0; x + w <= x1 + 1e-9; x += step)
            {
                var s = new Spot(x, z, x + w, z + d);
                Spot? hit = null;
                foreach (var t in taken) if (s.Hits(t, gap)) { hit = t; break; }
                if (hit is null) return s;
                // Jump past what's in the way.
                x = Math.Max(x, hit.Value.X1 + gap - step);
            }
        return null;
    }

    static string Lower(string name) => name.Length > 1 && char.IsLower(name[1]) ? char.ToLowerInvariant(name[0]) + name[1..] : name;
}
