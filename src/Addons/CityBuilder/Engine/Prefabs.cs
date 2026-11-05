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

    /// <summary>One read by the building editor's saved buildings.</summary>
    public static PrefabInfo From(HousePrefab p, string? thumbnail = null) => new()
    {
        Id = p.Id, Name = p.Name, Type = p.Type, District = p.District, Theme = p.Theme, Tags = [.. p.Tags],
        Design = CityFootprint.Normalised(p.Design), Thumbnail = thumbnail,
    };
}

/// <summary>
/// The buildings saved in the building editor (its data folder's <c>prefabs</c> folder), read and written with
/// the building editor's own <see cref="HousePrefabs"/>, so a building saved here shows in its Saved buildings
/// and the other way round.
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

    /// <summary>Every saved building in the folder, by type then name (files that can't be read are passed over).</summary>
    public static List<PrefabInfo> Load(string? folder)
    {
        if (folder is null) return [];
        List<HousePrefab> all;
        try { all = HousePrefabs.Load(folder); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return []; }
        return [.. all.Select(p => PrefabInfo.From(p, HousePrefabs.PicturePath(folder, p.Id)))
            .OrderBy(p => p.Type, StringComparer.CurrentCultureIgnoreCase).ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    /// <summary>One prefab from its JSON, or null when it isn't one.</summary>
    public static PrefabInfo? Parse(string json) => HousePrefabs.FromJson(json) is { } p ? PrefabInfo.From(p) : null;

    /// <summary>
    /// Saves a building with the building editor's saved buildings (its footprint worked out, the time stamped),
    /// with <paramref name="png"/> as its picture if there is one. Returns the file.
    /// </summary>
    public static string Save(string folder, PrefabInfo p, string notes = "", byte[]? png = null)
    {
        var saved = HousePrefabs.Save(folder, new HousePrefab
        {
            Id = p.Id == Guid.Empty ? Guid.NewGuid() : p.Id,
            Name = p.Name,
            Type = p.Type,
            District = string.IsNullOrWhiteSpace(p.District) ? "Anywhere" : p.District,
            Theme = string.IsNullOrWhiteSpace(p.Theme) ? p.Design.Style.Theme : p.Theme,
            Tags = [.. p.Tags],
            Notes = notes,
            Design = p.Design,
        }, png);
        return HousePrefabs.PathOf(folder, saved.Id);
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
