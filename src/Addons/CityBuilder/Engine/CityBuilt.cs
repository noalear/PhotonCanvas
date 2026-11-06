using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Clipper2Lib;
using PhotonCanvas.Addon.HouseEditor;
using PhotonCanvas.Addon.Walls;
using FGeo = PhotonCanvas.Addon.Furniture.Geo;

namespace PhotonCanvas.Addon.CityBuilder;

/// <summary>
/// A building as it'll be once it's cut and put together, the building editor's way: every piece where it sits
/// (mm about the plan's middle: x along the plan, y up, z down it), the plan's middle (squares), the square size
/// it's made at, and the ground it covers with everything that sticks out (squares of its own plan): the walls'
/// corner columns, the roofs' overhang, the lamps and sconces on the walls, the doors and shutters swung open.
/// </summary>
public sealed record BuiltModel(List<HouseSolid> Solids, double MiddleX, double MiddleY, double SquareMm, PathsD Extent, bool Ok)
{
    /// <summary>How many pieces it has.</summary>
    public int Count => Solids.Count;

    /// <summary>The box round the ground it covers (squares of its own plan).</summary>
    public RectD Box => Extent.Count == 0 ? new RectD(0, 0, 0, 0) : Clipper.GetBounds(Extent);
}

/// <summary>
/// The buildings on the map as they'll be built, made in the background one at a time (each the building
/// editor's pieces, which takes a second or so) and remembered by what each building is (its design), so a
/// building moved, turned or copied isn't made again. The 3D view shows them true to size, and the plan shows
/// where buildings would run into each other once they're put together.
/// </summary>
public static class CityBuilt
{
    static readonly Dictionary<string, BuiltModel> Made = [];
    static readonly Dictionary<string, Task<BuiltModel>> Making = [];
    static readonly ConditionalWeakTable<HouseDesign, string> Keys = [];
    /// <summary>One building at a time (each makes its walls side by side already).</summary>
    static readonly SemaphoreSlim One = new(1, 1);

    /// <summary>Goes up each time another building is made, so what's drawn from them knows to look again.</summary>
    public static int Version { get; private set; }

    static string Key(HouseDesign d) => Keys.GetValue(d, x => Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(x.ToJson()))));

    /// <summary>The building as built, if it's been made already.</summary>
    public static BuiltModel? Known(HouseDesign d)
    {
        string key = Key(d);
        lock (Made) return Made.TryGetValue(key, out var m) ? m : null;
    }

    /// <summary>The building as built: straight away if it's been made, otherwise made in the background (once, however often it's asked for).</summary>
    public static Task<BuiltModel> Get(HouseDesign d)
    {
        string key = Key(d);
        lock (Made)
        {
            if (Made.TryGetValue(key, out var known)) return Task.FromResult(known);
            if (Making.TryGetValue(key, out var going)) return going;
            // Kept as soon as it's made (inside the task, so whoever waits for it finds it known), and dropped from
            // those being made whether it could be or not. The lock's held here until it's down as being made.
            var task = Task.Run(async () =>
            {
                await One.WaitAsync().ConfigureAwait(false);
                try
                {
                    // One that can't be made at all is kept as such (drawn as plain blocks), not tried again and again.
                    BuiltModel m;
                    try { m = Build(d); }
                    catch (Exception) { var (mx, my) = HouseKitMaker.Middle(d); m = new BuiltModel([], mx, my, Math.Max(5, d.SquareMm), [], false); }
                    lock (Made)
                    {
                        if (Made.Count > Most) Made.Clear();
                        Made[key] = m;
                        Version++;
                    }
                    return m;
                }
                finally
                {
                    One.Release();
                    lock (Made) Making.Remove(key);
                }
            });
            Making[key] = task;
            return task;
        }
    }

    /// <summary>The building as built, made now.</summary>
    public static BuiltModel Build(HouseDesign d)
    {
        double sq = Math.Max(5, d.SquareMm);
        var (mx, my) = HouseKitMaker.Middle(d);
        HouseKit kit;
        // Made plain: the pieces are the same shapes either way, and working out the textures is nearly all the time it takes.
        try { kit = HouseKitMaker.Make(Plain(d)); }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or ArithmeticException or IndexOutOfRangeException)
        {
            return new BuiltModel([], mx, my, sq, [], false);
        }
        // Only the shapes are kept, without the teeth of the joints on the walls, floors and roofs (too small to see
        // on a whole town, and most of what there'd be to draw).
        var solids = new List<HouseSolid>(kit.Solids.Count);
        foreach (var s in kit.Solids)
        {
            double by = s.Kind is SolidKind.Furniture or SolidKind.Railing ? 0.05 : Math.Max(0.05, s.Thickness * 1.1);
            var outline = FGeo.Tidy(Clipper.SimplifyPaths(s.Outline, by));
            if (outline.Count == 0) outline = FGeo.Tidy(Clipper.SimplifyPaths(s.Outline, 0.05));
            if (outline.Count == 0) continue;
            solids.Add(s with { Outline = outline, Etched = null, Lines = null, BackEtched = null, BackLines = null });
        }
        // The ground it covers: each piece seen from above (and where the doors and shutters swing to), all together.
        var hulls = new PathsD();
        foreach (var s in solids)
            foreach (var pose in s.Open is { } open ? new[] { s.Pose, open } : [s.Pose])
            {
                var pts = new List<PointD>();
                foreach (var path in s.Outline)
                    foreach (var q in path)
                        foreach (double w in new[] { -s.Thickness / 2, s.Thickness / 2 })
                        {
                            var p = pose.At(q.x, q.y, w);
                            pts.Add(new PointD(p.X / sq + mx, p.Z / sq + my));
                        }
                var hull = Hull(pts);
                if (hull.Count >= 3) hulls.Add(hull);
            }
        var extent = hulls.Count == 0 ? new PathsD() : Clipper.SimplifyPaths(Clipper.Union(hulls, FillRule.NonZero), 0.005);
        return new BuiltModel(solids, mx, my, sq, extent, kit.Ok && solids.Count > 0);
    }

    /// <summary>The design with every wall, floor, column and roof plain: the same pieces, quicker to make.</summary>
    public static HouseDesign Plain(HouseDesign d) => d with
    {
        Style = d.Style with
        {
            Texture = Textures.Plain, UpperTexture = d.Style.UpperTexture is null ? null : Textures.Plain,
            FloorTexture = Textures.Plain, RoofTexture = Textures.Plain,
        },
        UpperFloor = Textures.Plain,
        ColumnTexture = d.ColumnTexture is null ? null : Textures.Plain,
        Roofs = [.. d.Roofs.Select(r => r with { Covering = Textures.Plain })],
        Levels = [.. d.Levels.Select(l => l with
        {
            RoomFloors = [.. l.RoomFloors.Select(f => f with { Texture = Textures.Plain })],
            WallLooks = [.. l.WallLooks.Select(w => w with { Texture = Textures.Plain, BackTexture = w.BackTexture is null ? null : Textures.Plain })],
            Columns = [.. l.Columns.Select(c => c with { Texture = c.Texture is null ? null : Textures.Plain })],
        })],
    };

    /// <summary>The ground a building covers once built, where it is on the map (squares).</summary>
    public static PathsD Extent(CityBuilding b, BuiltModel m)
    {
        var res = new PathsD(m.Extent.Count);
        foreach (var path in m.Extent)
        {
            var q = new PathD(path.Count);
            foreach (var p in path)
            {
                var (x, y) = CityFootprint.ToMap(b, p.x, p.y);
                q.Add(new PointD(x, y));
            }
            res.Add(q);
        }
        return res;
    }

    /// <summary>The ground each building made already covers once built, where it is on the map (squares), with the box round it.</summary>
    public static List<(CityBuilding B, PathsD Extent, RectD Box)> Reaches(CityMap m)
    {
        var res = new List<(CityBuilding, PathsD, RectD)>();
        foreach (var b in m.Buildings)
            if (Known(b.Design) is { Ok: true } model)
            {
                var e = Extent(b, model);
                if (e.Count > 0) res.Add((b, e, Clipper.GetBounds(e)));
            }
        return res;
    }

    /// <summary>
    /// The buildings that would run into each other once they're built (the ground each covers, with its roof's
    /// overhang and everything sticking out of its walls, overlaps the other's), with where; buildings not made
    /// yet are left out.
    /// </summary>
    public static List<(CityBuilding A, CityBuilding B, PathsD Where)> Intrusions(CityMap m) => Intrusions(Reaches(m));

    /// <summary>Where some buildings' ground (<see cref="Reaches"/>) overlaps.</summary>
    public static List<(CityBuilding A, CityBuilding B, PathsD Where)> Intrusions(List<(CityBuilding B, PathsD Extent, RectD Box)> known)
    {
        var res = new List<(CityBuilding, CityBuilding, PathsD)>();
        for (int i = 0; i < known.Count; i++)
            for (int j = i + 1; j < known.Count; j++)
            {
                var (a, ea, ba) = known[i];
                var (b, eb, bb) = known[j];
                if (ba.left >= bb.right || bb.left >= ba.right || ba.top >= bb.bottom || bb.top >= ba.bottom) continue;
                var where = Clipper.Intersect(ea, eb, FillRule.NonZero);
                // More than a sliver (a fortieth of a square's area): pieces really in each other's way.
                if (where.Sum(p => Math.Abs(Clipper.Area(p))) > 0.025) res.Add((a, b, where));
            }
        return res;
    }

    /// <summary>The most buildings kept made (and made for a map).</summary>
    public const int Most = 400;

    /// <summary>Starts making every building on the map not made yet (in the background, the first <see cref="Most"/> of them); how many are still to make.</summary>
    public static int WarmUp(CityMap m)
    {
        int left = 0;
        foreach (var d in m.Buildings.Select(b => b.Design).Distinct().Take(Most))
            if (Known(d) is null)
            {
                _ = Get(d);
                left++;
            }
        return left;
    }

    /// <summary>How many of the map's buildings are made, of how many.</summary>
    public static (int Done, int Of) Progress(CityMap m)
    {
        var designs = m.Buildings.Select(b => b.Design).Distinct().Take(Most).ToList();
        return (designs.Count(d => Known(d) is not null), designs.Count);
    }

    /// <summary>The convex hull of some points (anticlockwise).</summary>
    static PathD Hull(List<PointD> pts)
    {
        var p = pts.Distinct().OrderBy(q => q.x).ThenBy(q => q.y).ToList();
        if (p.Count < 3) return [.. p];
        static double Cross(PointD o, PointD a, PointD b) => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
        var h = new PointD[2 * p.Count];
        int k = 0;
        for (int i = 0; i < p.Count; i++)
        {
            while (k >= 2 && Cross(h[k - 2], h[k - 1], p[i]) <= 1e-12) k--;
            h[k++] = p[i];
        }
        for (int i = p.Count - 2, lower = k + 1; i >= 0; i--)
        {
            while (k >= lower && Cross(h[k - 2], h[k - 1], p[i]) <= 1e-12) k--;
            h[k++] = p[i];
        }
        return [.. h.Take(k - 1)];
    }
}
