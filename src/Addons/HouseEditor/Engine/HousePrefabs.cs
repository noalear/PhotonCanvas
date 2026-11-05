using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Clipper2Lib;
using PhotonCanvas.App.Addons;
using Geo = PhotonCanvas.Addon.Walls.Geo;

namespace PhotonCanvas.Addon.HouseEditor;

/// <summary>Which side of the plan something faces: the bottom of the plan (towards you), its right, its top, its left. In the order of a quarter turn anticlockwise each.</summary>
public enum PlanSide { Front, Right, Back, Left }

/// <summary>A door or doorway out of a building: where it is on the plan (squares) and which way it faces out.</summary>
public sealed record PrefabDoor
{
    public double X { get; init; }
    public double Y { get; init; }
    public PlanSide Facing { get; init; }
    public OpeningKind Kind { get; init; } = OpeningKind.Door;
}

/// <summary>
/// What a saved building takes up, worked out when it's saved (its design moved so its top-left corner is at
/// grid point 0, 0): its size in squares and mm, its floors and height, the ground squares it stands on
/// (yard included) and its ways in. For laying buildings out on a bigger map.
/// </summary>
public sealed record PrefabFootprint
{
    /// <summary>Squares across (x) and front to back (y), everything on every floor and its yard included.</summary>
    public int Width { get; init; }
    public int Depth { get; init; }
    public double SquareMm { get; init; } = 25.4;
    public double WidthMm => Width * SquareMm;
    public double DepthMm => Depth * SquareMm;
    public int Floors { get; init; } = 1;
    /// <summary>From the ground to the top of its walls or roof, whichever is higher (mm, roughly).</summary>
    public double HeightMm { get; init; }
    public bool Roofed { get; init; }
    /// <summary>The ground squares it stands on, each by its top-left corner: its rooms, floor and yard.</summary>
    public List<GridPt> Cells { get; init; } = [];
    /// <summary>Its doors and doorways out of the building, on the ground floor.</summary>
    public List<PrefabDoor> Doors { get; init; } = [];
}

/// <summary>
/// A building saved to reuse: a design from the building editor with a name, what kind of building it is,
/// its theme, where it goes in a town, tags and notes. Stored one file each in a folder
/// (<see cref="HousePrefabs"/>); the building editor opens them to change or drops them into another building,
/// and the city builder lays them out on a map.
/// </summary>
public sealed record HousePrefab
{
    /// <summary>The file's layout, for later changes.</summary>
    public int Format { get; init; } = HousePrefabs.CurrentFormat;
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = "Building";
    /// <summary>What kind of building it is: one of <see cref="HousePrefabs.Types"/>, or any other typed in.</summary>
    public string Type { get; init; } = "House";
    /// <summary>The look's theme (Medieval, Sci-fi, Dungeon…): the design's own when it's saved.</summary>
    public string Theme { get; init; } = "Medieval";
    /// <summary>Where it goes in a town: one of <see cref="HousePrefabs.Districts"/>, or any other.</summary>
    public string District { get; init; } = "Anywhere";
    public List<string> Tags { get; init; } = [];
    public string Notes { get; init; } = "";
    public DateTime Created { get; init; }
    public DateTime Modified { get; init; }
    /// <summary>Worked out from <see cref="Design"/> when it's saved (and when it's read, if it's missing).</summary>
    public PrefabFootprint? Footprint { get; init; }
    public HouseDesign Design { get; init; } = new();

    /// <summary>The same building turned <paramref name="quarterTurns"/> quarter turns anticlockwise seen from above, its top-left corner at 0, 0 again.</summary>
    public HousePrefab Turned(int quarterTurns)
    {
        var d = Design.Turned(quarterTurns);
        return this with { Design = d, Footprint = HousePrefabs.FootprintOf(d) };
    }

    /// <summary>Whether it has the tag (case doesn't matter).</summary>
    public bool HasTag(string tag) => Tags.Any(t => string.Equals(t.Trim(), tag.Trim(), StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// The saved buildings: one JSON file each, <c>&lt;id&gt;.json</c> (the id without dashes), in a folder, with a
/// picture <c>&lt;id&gt;.png</c> beside it. The building editor's own folder is <see cref="DefaultFolder"/>;
/// another add-on (the city builder) reads the same one.
/// </summary>
public static class HousePrefabs
{
    public const int CurrentFormat = 1;

    /// <summary>Kinds of building to pick from (any other can be typed in).</summary>
    public static readonly IReadOnlyList<string> Types =
    [
        "House", "Cottage", "Hovel", "Manor", "Shop", "Market stall", "Tavern", "Inn", "Smithy", "Workshop", "Mill", "Warehouse",
        "Barn", "Stable", "Temple", "Chapel", "Shrine", "Hall", "Tower", "Watchtower", "Gatehouse", "Guardhouse", "Keep", "Wall",
        "Ruin", "Bunker", "Hab block", "Outpost", "Other",
    ];

    /// <summary>Where in a town a building goes, for laying out a map.</summary>
    public static readonly IReadOnlyList<string> Districts =
    [
        "Anywhere", "Residential", "Market", "Docks", "Temple", "Noble", "Military", "Farm", "Slums", "Industrial", "Outskirts", "Wilderness",
    ];

    /// <summary>The building editor's saved buildings: addon-data/houseeditor/prefabs.</summary>
    public static string DefaultFolder => Path.Combine(AddonManager.DataRoot, "houseeditor", "prefabs");

    static readonly JsonSerializerOptions Options = new()
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        Converters = { new JsonStringEnumConverter() },
        WriteIndented = true,
    };

    public static string FileName(Guid id) => id.ToString("N") + ".json";

    public static string PathOf(string folder, Guid id) => Path.Combine(folder, FileName(id));

    /// <summary>The picture saved with a building, or null if it has none.</summary>
    public static string? PicturePath(string folder, Guid id)
    {
        var p = Path.Combine(folder, id.ToString("N") + ".png");
        return File.Exists(p) ? p : null;
    }

    public static string ToJson(HousePrefab p) => JsonSerializer.Serialize(p, Options);

    /// <summary>A saved building from its file's text, or null when it isn't one. A bare design (the editor's last.json) comes in as a new building.</summary>
    public static HousePrefab? FromJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            HousePrefab? p;
            if (doc.RootElement.TryGetProperty("Design", out _)) p = JsonSerializer.Deserialize<HousePrefab>(json, Options);
            else if (doc.RootElement.TryGetProperty("Levels", out _) && HouseDesign.FromJson(json) is { } d) p = new HousePrefab { Design = d, Theme = d.Style.Theme };
            else return null;
            if (p is null || p.Design.Levels.Count == 0) return null;
            return p with { Tags = p.Tags ?? [], Notes = p.Notes ?? "", Footprint = p.Footprint ?? FootprintOf(p.Design) };
        }
        catch (Exception e) when (e is JsonException or NotSupportedException or ArgumentException or InvalidOperationException) { return null; }
    }

    /// <summary>Every saved building in <paramref name="folder"/>, by name (files that can't be read are passed over).</summary>
    public static List<HousePrefab> Load(string folder)
    {
        var list = new List<HousePrefab>();
        if (!Directory.Exists(folder)) return list;
        foreach (var f in Directory.EnumerateFiles(folder, "*.json"))
        {
            if (Read(f) is { } p) list.Add(p);
        }
        return [.. list.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(p => p.Id)];
    }

    /// <summary>One saved building, by its id, or null.</summary>
    public static HousePrefab? Find(string folder, Guid id) => Read(PathOf(folder, id));

    /// <summary>A saved building from a file (one shared by someone else, say), or null.</summary>
    public static HousePrefab? Read(string file)
    {
        try { return File.Exists(file) ? FromJson(File.ReadAllText(file)) : null; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>
    /// Saves a building (in place of the one with the same id): its design moved to the top-left of the grid,
    /// its footprint worked out, the time stamped. <paramref name="png"/> is its picture, if there is one.
    /// Returns it as saved.
    /// </summary>
    public static HousePrefab Save(string folder, HousePrefab prefab, byte[]? png = null)
    {
        Directory.CreateDirectory(folder);
        var now = DateTime.UtcNow;
        var design = prefab.Design.AtOrigin();
        var saved = prefab with
        {
            Format = CurrentFormat,
            Name = string.IsNullOrWhiteSpace(prefab.Name) ? "Building" : prefab.Name.Trim(),
            Type = string.IsNullOrWhiteSpace(prefab.Type) ? "Other" : prefab.Type.Trim(),
            Tags = [.. prefab.Tags.Select(t => t.Trim()).Where(t => t.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)],
            Created = prefab.Created == default ? now : prefab.Created,
            Modified = now,
            Design = design,
            Footprint = FootprintOf(design),
        };
        var path = PathOf(folder, saved.Id);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, ToJson(saved));
        File.Move(tmp, path, overwrite: true);
        if (png is { Length: > 0 }) File.WriteAllBytes(Path.Combine(folder, saved.Id.ToString("N") + ".png"), png);
        return saved;
    }

    /// <summary>Takes a saved building (and its picture) away.</summary>
    public static void Delete(string folder, Guid id)
    {
        foreach (var f in new[] { PathOf(folder, id), Path.Combine(folder, id.ToString("N") + ".png") })
            if (File.Exists(f)) File.Delete(f);
    }

    /// <summary>A copy of a building with a new id, to keep as another.</summary>
    public static HousePrefab Copy(HousePrefab p, string? name = null) => p with { Id = Guid.NewGuid(), Name = name ?? p.Name + " (copy)", Created = default };

    /// <summary>
    /// The buildings matching what's asked: any type, theme or district left null matches all, every tag must
    /// be there, and <paramref name="text"/> is looked for in the name, type, tags and notes.
    /// </summary>
    public static IEnumerable<HousePrefab> Matching(IEnumerable<HousePrefab> all, string? type = null, string? theme = null, string? district = null, string? text = null, IEnumerable<string>? tags = null)
    {
        var need = tags?.Where(t => !string.IsNullOrWhiteSpace(t)).ToList() ?? [];
        bool Same(string a, string? b) => b is null || string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        bool Has(string s) => s.Contains(text!.Trim(), StringComparison.CurrentCultureIgnoreCase);
        return all.Where(p => Same(p.Type, type) && Same(p.Theme, theme) && Same(p.District, district) && need.All(p.HasTag)
            && (string.IsNullOrWhiteSpace(text) || Has(p.Name) || Has(p.Type) || Has(p.Notes) || p.Tags.Any(Has)));
    }

    /// <summary>
    /// The building and the turn (quarter turns anticlockwise) that best fill a <paramref name="width"/> ×
    /// <paramref name="depth"/> squares space: as near its size as can be without going over (over is allowed,
    /// but costs a lot), of the type asked for if there is one, with a door facing <paramref name="doorsFacing"/>
    /// (a street, say). Null when there are none to pick from. A lower score is a better fit.
    /// </summary>
    public static (HousePrefab Prefab, int Turns, double Score)? BestFit(IEnumerable<HousePrefab> all, int width, int depth, string? type = null, PlanSide? doorsFacing = null, string? theme = null)
    {
        (HousePrefab, int, double)? best = null;
        foreach (var p in all)
        {
            var f = p.Footprint ?? FootprintOf(p.Design);
            for (int q = 0; q < 4; q++)
            {
                int w = q % 2 == 0 ? f.Width : f.Depth, d = q % 2 == 0 ? f.Depth : f.Width;
                double score = Math.Max(0, width - w) + Math.Max(0, depth - d) + 10 * (Math.Max(0, w - width) + Math.Max(0, d - depth));
                if (type is not null && !string.Equals(p.Type, type, StringComparison.OrdinalIgnoreCase)) score += 100;
                if (theme is not null && !string.Equals(p.Theme, theme, StringComparison.OrdinalIgnoreCase)) score += 50;
                if (doorsFacing is { } side && f.Doors.Count > 0 && !f.Doors.Any(dr => Turn(dr.Facing, q) == side)) score += 5;
                // A square building fits either way: the turn that needs no turning first.
                score += q * 0.01;
                if (best is null || score < best.Value.Item3) best = (p, q, score);
            }
        }
        return best;
    }

    /// <summary>A side turned <paramref name="quarterTurns"/> quarter turns anticlockwise seen from above.</summary>
    public static PlanSide Turn(PlanSide side, int quarterTurns) => (PlanSide)((((int)side + quarterTurns) % 4 + 4) % 4);

    /// <summary>What a design takes up (see <see cref="PrefabFootprint"/>); its own position on the grid is kept.</summary>
    public static PrefabFootprint FootprintOf(HouseDesign d)
    {
        var (x0, y0, x1, y1) = d.GridBounds();
        var ground = d.Levels[0];
        var plan = ground.Shape();
        var area = HouseKitMaker.FloorArea(ground, null, plan);
        var cells = new List<GridPt>();
        for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
                if (Inside(area, x + 0.5, y + 0.5)) cells.Add(new GridPt(x - x0, y - y0));

        var doors = new List<PrefabDoor>();
        foreach (var o in ground.Openings.Where(o => o.Kind != OpeningKind.Window))
        {
            if (FacingOut(ground, plan, o) is { } side) doors.Add(new PrefabDoor { X = o.X - x0, Y = o.Y - y0, Facing = side, Kind = o.Kind });
        }

        double storey = d.Style.StoreyHeightMm, height = d.Levels.Count * storey;
        foreach (var r in d.Roofs)
        {
            double span = (r.RidgeAcross ? r.Y1 - r.Y0 : r.X1 - r.X0) * d.SquareMm;
            double rise = r.Kind switch
            {
                RoofKind.Flat => 0,
                RoofKind.LeanTo => span * Math.Tan(r.PitchDeg * Math.PI / 180),
                _ => span / 2 * Math.Tan(r.PitchDeg * Math.PI / 180),
            };
            height = Math.Max(height, (r.Level + 1) * storey + rise);
        }
        return new PrefabFootprint
        {
            Width = x1 - x0, Depth = y1 - y0, SquareMm = d.SquareMm, Floors = d.Levels.Count, HeightMm = Math.Round(height, 1),
            Roofed = d.Roofs.Count > 0, Cells = cells, Doors = doors,
        };
    }

    static bool Inside(PathsD area, double x, double y)
    {
        int inside = 0;
        foreach (var p in area)
            if (p.Count >= 3 && Clipper.PointInPolygon(new PointD(x, y), p) != PointInPolygonResult.IsOutside) inside++;
        return inside % 2 == 1;
    }

    /// <summary>Which way a door faces out of the building, or null when it's between two rooms (or not on a wall).</summary>
    static PlanSide? FacingOut(Level level, PlanShape plan, Opening o)
    {
        foreach (var w in level.Walls)
        {
            double dx = w.B.X - w.A.X, dy = w.B.Y - w.A.Y, len = Math.Sqrt(dx * dx + dy * dy);
            double t = ((o.X - w.A.X) * dx + (o.Y - w.A.Y) * dy) / (len * len);
            double off = Math.Abs((o.X - w.A.X) * dy - (o.Y - w.A.Y) * dx) / len;
            if (t < -1e-6 || t > 1 + 1e-6 || off > 0.05) continue;
            double nx = dy / len, ny = -dx / len;
            bool a = plan.RoomAt(o.X + nx * 0.3, o.Y + ny * 0.3) >= 0, b = plan.RoomAt(o.X - nx * 0.3, o.Y - ny * 0.3) >= 0;
            if (a == b) return null;
            // The way out.
            double ox = a ? -nx : nx, oy = a ? -ny : ny;
            return Math.Abs(ox) >= Math.Abs(oy) ? ox > 0 ? PlanSide.Right : PlanSide.Left : oy > 0 ? PlanSide.Front : PlanSide.Back;
        }
        return null;
    }
}

public sealed partial record HouseDesign
{
    /// <summary>
    /// The grid squares everything takes up on every floor, from (<c>X0</c>, <c>Y0</c>) to (<c>X1</c>, <c>Y1</c>):
    /// walls, floor laid, roofs, furniture and ways up, and the ground floor's yard. (0, 0, 0, 0) when there's nothing.
    /// </summary>
    public (int X0, int Y0, int X1, int Y1) GridBounds()
    {
        int x0 = int.MaxValue, y0 = int.MaxValue, x1 = int.MinValue, y1 = int.MinValue;
        void Add(double x, double y)
        {
            x0 = Math.Min(x0, (int)Math.Floor(x + 1e-9)); y0 = Math.Min(y0, (int)Math.Floor(y + 1e-9));
            x1 = Math.Max(x1, (int)Math.Ceiling(x - 1e-9)); y1 = Math.Max(y1, (int)Math.Ceiling(y - 1e-9));
        }
        foreach (var l in Levels)
        {
            foreach (var w in l.Walls) { Add(w.A.X, w.A.Y); Add(w.B.X, w.B.Y); }
            foreach (var f in l.Floor) { Add(f.X, f.Y); Add(f.X + 1, f.Y + 1); }
            foreach (var f in l.Furniture) Add(f.X, f.Y);
            foreach (var w in l.WaysUp) Add(w.X, w.Y);
        }
        foreach (var r in Roofs) { Add(r.X0, r.Y0); Add(r.X1, r.Y1); }
        if (x0 == int.MaxValue) return (0, 0, 0, 0);
        // The ground floor's yard reaches past its walls.
        if (Levels.Count > 0 && Levels[0].Yard is int yard && yard > 0 && Levels[0].Walls.Count > 0)
        {
            var ws = Levels[0].Walls.SelectMany(w => new[] { w.A, w.B }).ToList();
            x0 = Math.Min(x0, ws.Min(p => p.X) - yard); y0 = Math.Min(y0, ws.Min(p => p.Y) - yard);
            x1 = Math.Max(x1, ws.Max(p => p.X) + yard); y1 = Math.Max(y1, ws.Max(p => p.Y) + yard);
        }
        return (x0, y0, x1, y1);
    }

    /// <summary>The building moved so the top-left of everything it takes up is grid point 0, 0.</summary>
    public HouseDesign AtOrigin()
    {
        var (x0, y0, _, _) = GridBounds();
        return x0 == 0 && y0 == 0 ? this : Moved(-x0, -y0);
    }

    /// <summary>
    /// The building turned <paramref name="quarterTurns"/> quarter turns anticlockwise seen from above (the
    /// way furniture turns), moved so its top-left is at grid point 0, 0: its walls, doors and windows, floors,
    /// furniture, ways up, columns and roofs all go round with it.
    /// </summary>
    public HouseDesign Turned(int quarterTurns)
    {
        var d = AtOrigin();
        int q = ((quarterTurns % 4) + 4) % 4;
        for (int i = 0; i < q; i++) d = d.TurnedOnce();
        return d;
    }

    /// <summary>One quarter turn anticlockwise: (x, y) → (y, W − x), W the width, at the top-left already.</summary>
    HouseDesign TurnedOnce()
    {
        var (_, _, W, _) = GridBounds();
        GridPt P(GridPt p) => new(p.Y, W - p.X);
        WallEdge E(WallEdge e) => WallEdge.Of(P(e.A), P(e.B));
        static int T(int t) => ((t + 1) % 4 + 4) % 4;
        return this with
        {
            Levels = [.. Levels.Select(l => l with
            {
                Walls = [.. l.Walls.Select(E)],
                Openings = [.. l.Openings.Select(o => o with { X = o.Y, Y = W - o.X })],
                Floor = [.. l.Floor.Select(f => new GridPt(f.Y, W - f.X - 1))],
                RoomFloors = [.. l.RoomFloors.Select(r => r with { X = r.Y, Y = W - r.X })],
                WallLooks = [.. l.WallLooks.Select(w => w with { Edge = E(w.Edge) })],
                NoRailing = [.. l.NoRailing.Select(E)],
                Columns = [.. l.Columns.Select(c => c with { At = P(c.At) })],
                Furniture = [.. l.Furniture.Select(f => f with { X = f.Y, Y = W - f.X, Turn = T(f.Turn) })],
                WaysUp = [.. l.WaysUp.Select(w => w with { X = w.Y, Y = W - w.X, Turn = T(w.Turn) })],
            })],
            Roofs = [.. Roofs.Select(r => r with { X0 = r.Y0, X1 = r.Y1, Y0 = W - r.X1, Y1 = W - r.X0, Turn = T(r.Turn) })],
        };
    }

    /// <summary>
    /// This building with another (a saved one) dropped into it, moved <paramref name="dx"/>, <paramref name="dy"/>
    /// squares, its ground floor on this one's floor <paramref name="level"/> (and its floors above on the ones above
    /// that, added as needed, up to <see cref="HouseKitMaker.MaxLevels"/>). Walls they share are kept once; its
    /// walls keep its texture and wear where they differ from this building's. Its own sizes (wall thickness, sheet,
    /// storey height) give way to this one's.
    /// </summary>
    public HouseDesign With(HouseDesign part, int dx, int dy, int level = 0)
    {
        var p = part.Moved(dx, dy);
        level = Math.Clamp(level, 0, Math.Max(0, Levels.Count - 1));
        var levels = Levels.ToList();
        for (int i = 0; i < p.Levels.Count && level + i < HouseKitMaker.MaxLevels; i++)
        {
            int k = level + i;
            var add = p.Levels[i];
            // Its walls' look where it isn't this building's: kept as each wall's own.
            string theirs = i == 0 ? part.Style.Texture : part.Style.UpperTexture ?? part.Style.Texture;
            var looks = add.WallLooks.ToList();
            if (k >= levels.Count)
            {
                levels.Add(add with { WallLooks = LooksFor(add, looks, theirs, part, Style.UpperTexture ?? Style.Texture) });
                continue;
            }
            var l = levels[k];
            string ours = k == 0 ? Style.Texture : Style.UpperTexture ?? Style.Texture;
            looks = LooksFor(add, looks, theirs, part, ours);
            var walls = l.Walls.ToHashSet();
            foreach (var w in add.Walls) walls.Add(w);
            // No two diagonals crossing in one square: theirs win.
            foreach (var w in add.Walls.Where(w => w.Diagonal)) walls.Remove(WallEdge.Of(new(w.A.X, w.B.Y), new(w.B.X, w.A.Y)));
            var lookAt = l.WallLooks.Where(x => walls.Contains(x.Edge)).ToDictionary(x => x.Edge);
            foreach (var x in looks) lookAt[x.Edge] = x;
            var columns = l.Columns.ToDictionary(c => c.At);
            foreach (var c in add.Columns) columns[c.At] = c;
            levels[k] = l with
            {
                Walls = [.. walls],
                Openings = [.. l.Openings, .. add.Openings],
                Floor = [.. l.Floor.Union(add.Floor)],
                RoomFloors = [.. l.RoomFloors, .. add.RoomFloors],
                Furniture = [.. l.Furniture, .. add.Furniture],
                WaysUp = [.. l.WaysUp, .. add.WaysUp],
                Columns = [.. columns.Values],
                NoRailing = [.. l.NoRailing.Union(add.NoRailing)],
                WallLooks = [.. lookAt.Values],
            };
        }
        int kept = Math.Min(p.Levels.Count, HouseKitMaker.MaxLevels - level);
        return this with
        {
            Levels = levels,
            Roofs = [.. Roofs, .. p.Roofs.Where(r => r.Level < kept).Select(r => r with { Level = r.Level + level })],
        };
    }

    /// <summary>A part's wall looks, with its walls that have none given its own building's look when that isn't the one they're going into.</summary>
    List<WallLook> LooksFor(Level add, List<WallLook> looks, string theirs, HouseDesign part, string ours)
    {
        bool differs = theirs != ours || Math.Abs(part.Wear - Wear) > 1e-9 || Math.Abs(part.DetailMm - DetailMm) > 1e-9;
        if (!differs) return looks;
        var own = looks.Select(x => x.Edge).ToHashSet();
        foreach (var w in add.Walls)
            if (!own.Contains(w)) looks.Add(new WallLook { Edge = w, Texture = theirs, Wear = part.Wear, DetailMm = part.DetailMm, BackWear = part.Wear });
        return looks;
    }
}
