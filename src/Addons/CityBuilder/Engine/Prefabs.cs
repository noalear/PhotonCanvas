using System.Text.Json;
using PhotonCanvas.Addon.HouseEditor;

namespace PhotonCanvas.Addon.CityBuilder;

/// <summary>A building saved in the building editor to use again (a prefab): its name, type, district, tags and design.</summary>
public sealed record PrefabInfo
{
    public Guid Id { get; init; }
    public string Name { get; init; } = "";
    public string Type { get; init; } = "";
    public string District { get; init; } = "";
    public string Theme { get; init; } = "";
    public List<string> Tags { get; init; } = [];
    /// <summary>The design, moved so its top left corner is at 0, 0.</summary>
    public HouseDesign Design { get; init; } = new();
    /// <summary>Its picture file (PNG), if one was saved with it.</summary>
    public string? Thumbnail { get; init; }

    /// <summary>A copy of it standing on the map with its top left corner at (x, y), turned.</summary>
    public CityBuilding Place(int x, int y, int turn = 0) => new()
    {
        Name = Name, Type = string.IsNullOrWhiteSpace(Type) ? "House" : Type, District = District, Tags = [.. Tags],
        PrefabId = Id, X = x, Y = y, Turn = ((turn % 4) + 4) % 4, Design = Design,
    };
}

/// <summary>
/// The buildings saved in the building editor (its data folder's <c>prefabs</c> folder: a JSON file each,
/// holding the name, type, district, tags and the design). Read leniently, so a file from a newer version
/// still loads what it can.
/// </summary>
public static class PrefabShelf
{
    /// <summary>The building editor's prefab folder, next to this add-on's own data folder.</summary>
    public static string? FolderNextTo(string? dataFolder)
    {
        if (string.IsNullOrEmpty(dataFolder)) return null;
        var root = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(dataFolder));
        return root is null ? null : Path.Combine(root, "houseeditor", "prefabs");
    }

    public static List<PrefabInfo> Load(string? folder)
    {
        var list = new List<PrefabInfo>();
        if (folder is null || !Directory.Exists(folder)) return list;
        IEnumerable<string> files;
        try { files = Directory.EnumerateFiles(folder, "*.json").ToList(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return list; }
        foreach (var f in files)
        {
            try
            {
                if (Parse(File.ReadAllText(f)) is { } p)
                {
                    var png = Path.ChangeExtension(f, ".png");
                    list.Add(p with { Thumbnail = File.Exists(png) ? png : null });
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        return [.. list.OrderBy(p => p.Type, StringComparer.CurrentCultureIgnoreCase).ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    /// <summary>One prefab from its JSON, or null when it isn't one.</summary>
    public static PrefabInfo? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !Get(root, "Design", out var designEl) || designEl.ValueKind != JsonValueKind.Object) return null;
            if (HouseDesign.FromJson(designEl.GetRawText()) is not { } design || design.Levels.Count == 0) return null;
            string Str(string name) => Get(root, name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
            var tags = Get(root, "Tags", out var t) && t.ValueKind == JsonValueKind.Array
                ? t.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).Where(x => x.Length > 0).ToList()
                : [];
            Guid id = Get(root, "Id", out var idEl) && idEl.ValueKind == JsonValueKind.String && Guid.TryParse(idEl.GetString(), out var g) ? g : Guid.Empty;
            return new PrefabInfo
            {
                Id = id, Name = Str("Name"), Type = Str("Type"), District = Str("District"), Theme = Str("Theme"), Tags = tags,
                Design = CityFootprint.Normalised(design),
            };
        }
        catch (JsonException) { return null; }
    }

    /// <summary>
    /// Saves a building with the building editor's saved buildings, in their format (a JSON file named by its
    /// id; the building editor works out its footprint and picture again when it next saves it).
    /// </summary>
    public static string Save(string folder, PrefabInfo p, string notes = "")
    {
        Directory.CreateDirectory(folder);
        var id = p.Id == Guid.Empty ? Guid.NewGuid() : p.Id;
        var path = Path.Combine(folder, id.ToString("N") + ".json");
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteNumber("Format", 1);
            w.WriteString("Id", id);
            w.WriteString("Name", p.Name);
            w.WriteString("Type", p.Type);
            w.WriteString("District", p.District);
            w.WriteStartArray("Tags");
            foreach (var t in p.Tags) w.WriteStringValue(t);
            w.WriteEndArray();
            w.WriteString("Theme", p.Theme);
            w.WriteString("Notes", notes);
            var now = DateTime.UtcNow;
            w.WriteString("Created", now);
            w.WriteString("Modified", now);
            w.WritePropertyName("Design");
            w.WriteRawValue(p.Design.ToJson());
            w.WriteEndObject();
        }
        File.WriteAllBytes(path, stream.ToArray());
        return path;
    }

    static bool Get(JsonElement o, string name, out JsonElement value)
    {
        foreach (var p in o.EnumerateObject())
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) { value = p.Value; return true; }
        value = default;
        return false;
    }
}

/// <summary>Picks a saved building for a footprint read from a battle map, when one fits it well.</summary>
public static class PrefabFit
{
    /// <summary>
    /// A saved building standing on the footprint, turned to fit, when the footprint is a rectangle and one is
    /// the same size (either way round): the same type first, then the one whose door ends up towards the road.
    /// </summary>
    public static CityBuilding? Fit(SeenBuilding f, IReadOnlyList<PrefabInfo> prefabs)
    {
        if (prefabs.Count == 0 || f.Cells.Count == 0) return null;
        int x0 = f.Cells.Min(c => c.X), y0 = f.Cells.Min(c => c.Y), x1 = f.Cells.Max(c => c.X), y1 = f.Cells.Max(c => c.Y);
        int w = x1 - x0 + 1, d = y1 - y0 + 1;
        if (f.Cells.Count != w * d) return null;
        CityBuilding? best = null;
        double bestScore = double.MinValue;
        foreach (var p in prefabs)
            for (int turn = 0; turn < 4; turn++)
            {
                var b = p.Place(x0, y0, turn);
                var (bw, bd) = CityFootprint.Size(b);
                if (bw != w || bd != d) continue;
                var cells = CityFootprint.Cells(b);
                // Its ground floor must cover most of the footprint.
                int covered = cells.Count(c => f.Cells.Contains(c));
                if (covered < f.Cells.Count * 0.8) continue;
                double score = covered + (string.Equals(p.Type, f.Type, StringComparison.OrdinalIgnoreCase) ? 50 : 0) + 20 * DoorsTowards(b, f.DoorTowards);
                if (score > bestScore) { bestScore = score; best = b; }
            }
        return best;
    }

    /// <summary>How many of the building's ground-floor doors open onto one of the squares given.</summary>
    static int DoorsTowards(CityBuilding b, HashSet<(int X, int Y)> towards)
    {
        if (towards.Count == 0 || b.Design.Levels.Count == 0) return 0;
        var own = CityFootprint.Cells(b);
        int n = 0;
        foreach (var o in b.Design.Levels[0].Openings)
        {
            if (o.Kind == OpeningKind.Window) continue;
            var (mx, my) = CityFootprint.ToMap(b, o.X, o.Y);
            // The squares either side of the door: the one that isn't the building's.
            foreach (var (dx, dy) in new[] { (0.5, 0.0), (-0.5, 0.0), (0.0, 0.5), (0.0, -0.5) })
            {
                var c = ((int)Math.Floor(mx + dx), (int)Math.Floor(my + dy));
                if (!own.Contains(c) && towards.Contains(c)) { n++; break; }
            }
        }
        return n;
    }
}
