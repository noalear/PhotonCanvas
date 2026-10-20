using System.Text.Json;
using PhotonCanvas.Core;
using PhotonCanvas.Doc.Materials;

namespace PhotonCanvas.Doc;

/// <summary>
/// Brings data saved in earlier places into the data folder, losing nothing: the material libraries
/// are combined recipe by recipe (a recipe in both keeps its newer copy), the job history line by
/// line, and any other file is copied when it's missing here or newer there. Each earlier file is
/// taken once (a note of what was taken is kept), so a recipe deleted here doesn't come back. The
/// earlier folders are left as they are. Never throws; returns how many files were taken.
/// </summary>
public static class DataMerge
{
    // (The first ledger, merged-from.json, came before materials were matched by name; it's dropped so everything is merged again.)
    const string LedgerName = "data-merged.json", FirstLedger = "merged-from.json";

    public static int Run() => Run(AppIdentity.DataDir, AppIdentity.EarlierDataDirs());

    public static int Run(string dataDir, IEnumerable<string> earlier)
    {
        try
        {
            Directory.CreateDirectory(dataDir);
            var ledgerPath = Path.Combine(dataDir, LedgerName);
            if (File.Exists(Path.Combine(dataDir, FirstLedger))) File.Delete(Path.Combine(dataDir, FirstLedger));
            var ledger = Load(ledgerPath);
            int taken = 0;
            foreach (var source in earlier.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!Directory.Exists(source) || Path.GetFullPath(source).Equals(Path.GetFullPath(dataDir), StringComparison.OrdinalIgnoreCase)) continue;
                foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
                {
                    var info = new FileInfo(file);
                    string stamp = $"{info.Length}:{info.LastWriteTimeUtc.Ticks}";
                    if (ledger.TryGetValue(file, out var had) && had == stamp) continue;
                    var rel = Path.GetRelativePath(source, file);
                    var target = Path.Combine(dataDir, rel);
                    try
                    {
                        Take(file, rel, target);
                        ledger[file] = stamp;
                        taken++;
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or FormatException) { }
                }
            }
            if (taken > 0) File.WriteAllText(ledgerPath, JsonSerializer.Serialize(ledger, new JsonSerializerOptions { WriteIndented = true }));
            return taken;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return 0; // not fatal: the program starts with what's here
        }
    }

    static Dictionary<string, string> Load(string path)
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? [] : []; }
        catch (Exception e) when (e is IOException or JsonException) { return []; }
    }

    static void Take(string file, string rel, string target)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var name = rel.Replace('\\', '/');
        if (name.Equals("materials/library.json", StringComparison.OrdinalIgnoreCase)) { MergeLibrary(file, target); return; }
        if (name.Equals("job-history.jsonl", StringComparison.OrdinalIgnoreCase)) { MergeLines(file, target); return; }
        if (name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) return;
        if (!File.Exists(target) || File.GetLastWriteTimeUtc(file) > File.GetLastWriteTimeUtc(target))
        {
            File.Copy(file, target, overwrite: true);
            File.SetLastWriteTimeUtc(target, File.GetLastWriteTimeUtc(file));
        }
    }

    /// <summary>
    /// Every material and recipe from both. A material is the same one when it has the same id, or
    /// the same name and thickness (copies made apart from each other); a recipe when it has the
    /// same id (the one changed last is kept), or the same title and settings (a duplicate).
    /// </summary>
    static void MergeLibrary(string file, string target)
    {
        var from = MaterialLibrary.Load(Path.GetDirectoryName(file)!);
        var into = MaterialLibrary.Load(Path.GetDirectoryName(target)!);
        // Copies of one material already here (from an earlier merge) become one.
        foreach (var twin in into.Materials.GroupBy(m => m.Label, StringComparer.CurrentCultureIgnoreCase).Where(g => g.Count() > 1).ToList())
        {
            var keep = twin.First();
            foreach (var other in twin.Skip(1)) { keep = Fold(keep, other); into.Materials.Remove(other); }
            into.Upsert(keep);
        }
        foreach (var m in from.Materials)
        {
            var mine = into.Find(m.Id) ?? into.Materials.FirstOrDefault(x => string.Equals(x.Label, m.Label, StringComparison.CurrentCultureIgnoreCase));
            into.Upsert(mine is null ? m : Fold(mine, m));
        }
        into.Save();
    }

    /// <summary><paramref name="m"/> with <paramref name="other"/>'s recipes added.</summary>
    static Material Fold(Material m, Material other)
    {
        var recipes = m.Recipes;
        foreach (var r in other.Recipes)
        {
            int at = recipes.FindIndex(x => x.Id == r.Id);
            if (at >= 0) { if (r.Updated > recipes[at].Updated) recipes = recipes.SetItem(at, r); continue; }
            int same = recipes.FindIndex(x => x.Title == r.Title && Json(x.Settings) == Json(r.Settings));
            if (same < 0) recipes = recipes.Add(r);
            else if (r.Updated > recipes[same].Updated) recipes = recipes.SetItem(same, r);
        }
        return m with { Recipes = recipes, ThicknessMm = m.ThicknessMm ?? other.ThicknessMm };
    }

    static string Json(ProcessSettings s) => JsonSerializer.Serialize(s);

    /// <summary>Lines from both, each once, in time order (each line starts with its time).</summary>
    static void MergeLines(string file, string target)
    {
        var lines = (File.Exists(target) ? File.ReadAllLines(target) : []).Concat(File.ReadAllLines(file))
            .Where(l => l.Length > 0).Distinct().Order(StringComparer.Ordinal).ToList();
        File.WriteAllLines(target, lines);
    }
}
