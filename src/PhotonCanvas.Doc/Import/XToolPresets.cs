using System.Globalization;
using PhotonCanvas.Core;
using PhotonCanvas.Core.Imaging;
using PhotonCanvas.Doc.Materials;

namespace PhotonCanvas.Doc.Import;

/// <summary>
/// xTool Studio's material settings, read from its own cache on this computer (read-only): xTool's
/// tested settings for a machine (its online library, as last downloaded) and the user's own
/// settings made in xTool Studio. Only materials and their settings are read: the account,
/// devices, projects and messages in the same folder are left alone, and nothing is sent anywhere.
/// </summary>
public static class XToolPresets
{
    /// <summary>One material and its settings for the machine, as xTool Studio has them.</summary>
    public sealed record Found(string Name, double? ThicknessMm, string Category, bool Reflective, bool Yours, IReadOnlyList<(string Title, ProcessSettings Settings)> Settings);

    /// <summary>What was read: the materials, and how many settings were left out (other work modes: batch, conveyor, rotary, relief; knife and intaglio).</summary>
    public sealed record Result(IReadOnlyList<Found> Materials, int LeftOut);

    /// <summary>xTool Studio's IndexedDB folder on this computer, if it's installed.</summary>
    public static string? Folder()
    {
        var p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "xTool Studio", "IndexedDB", "atomm_renderer_0.indexeddb.leveldb");
        return Directory.Exists(p) ? p : null;
    }

    /// <summary>Read the settings for <paramref name="deviceCode"/> (e.g. GS004-CLASS-4) from xTool Studio's folder.</summary>
    public static Result Read(string folder, string deviceCode)
    {
        var names = ChromiumIndexedDb.StoreNames(folder);
        var records = ChromiumIndexedDb.Read(folder);
        List<Dictionary<string, object?>> Store(string name) =>
            [.. names.Where(n => n.Value == name).SelectMany(n => records.GetValueOrDefault(n.Key) ?? []).OfType<Dictionary<string, object?>>()];
        return From(Store("materials"), Store("materialParams"), Store("userParams"), Store("localMaterials"), Store("localMaterialParams"), deviceCode);
    }

    /// <summary>The settings from the stores' records (split out so it can be tested without xTool's folder).</summary>
    public static Result From(IReadOnlyList<Dictionary<string, object?>> materials, IReadOnlyList<Dictionary<string, object?>> materialParams,
        IReadOnlyList<Dictionary<string, object?>> userParams, IReadOnlyList<Dictionary<string, object?>> localMaterials,
        IReadOnlyList<Dictionary<string, object?>> localParams, string deviceCode)
    {
        int leftOut = 0;
        var byId = new Dictionary<string, (string Name, double? Thickness, string Category, bool Reflective, bool Yours)>();
        foreach (var m in materials)
            byId[Str(m.GetValueOrDefault("id"))] = (Name(m.GetValueOrDefault("name")), Num(m.GetValueOrDefault("thickness")),
                Category(Str(m.GetValueOrDefault("categoryCode")), Str(m.GetValueOrDefault("secondCategoryCode"))),
                // Shiny by name only for metals ("Brown to Gold … Leather" isn't).
                Str(m.GetValueOrDefault("categoryCode")).Equals("Metal", StringComparison.OrdinalIgnoreCase) && Material.LooksReflective(Name(m.GetValueOrDefault("name"))), false);
        foreach (var m in localMaterials)
        {
            var name = Name(m.GetValueOrDefault("name"));
            bool shiny = Codes(m.GetValueOrDefault("precautionCodes")).Contains("HIGH_REFLECT") || Material.LooksReflective(name);
            byId[Str(m.GetValueOrDefault("id"))] = (name, Num(m.GetValueOrDefault("thickness")), Category(Str(m.GetValueOrDefault("categoryCode")), ""), shiny, true);
        }
        var found = new Dictionary<string, (bool Yours, List<(string, ProcessSettings)> List)>();
        void Add(string materialId, bool yours, string title, ProcessSettings? s)
        {
            if (s is null) { leftOut++; return; }
            string key = (yours ? "y:" : "x:") + materialId;
            if (!found.TryGetValue(key, out var list)) found[key] = list = (yours, []);
            // The same title twice (blue and MOPA settings, say): tell them apart.
            string t = title;
            for (int n = 2; list.List.Any(x => x.Item1 == t); n++) t = $"{title} ({n})";
            list.List.Add((t, s));
        }
        foreach (var rec in materialParams.Concat(userParams))
        {
            if (Str(rec.GetValueOrDefault("deviceCode")) != deviceCode) continue;
            bool yours = userParams.Contains(rec);
            string mid = Str(rec.GetValueOrDefault("materialId"));
            foreach (var p in Items(rec.GetValueOrDefault("machineParas")).OfType<Dictionary<string, object?>>())
            {
                var raw = p.GetValueOrDefault("rawData") as Dictionary<string, object?>;
                Add(mid, yours, Str(p.GetValueOrDefault("name")), Settings(
                    Code(p.GetValueOrDefault("processingType")), Code(p.GetValueOrDefault("processingMode")), Code(p.GetValueOrDefault("laserSource")),
                    Field(raw, p, "power"), Field(raw, p, "speed"), Field(raw, p, "repeat"),
                    Num(raw?.GetValueOrDefault("density")) ?? Num(Code(p.GetValueOrDefault("density"))),
                    Field(raw, p, "dpi"), Num(raw?.GetValueOrDefault("dotDuration")) ?? Num(p.GetValueOrDefault("dotTime")),
                    Code(p.GetValueOrDefault("bitmapMode")) is { Length: > 0 } bm ? bm : Str(raw?.GetValueOrDefault("bitmapMode")),
                    Field(raw, p, "pulseWidth"), Num(raw?.GetValueOrDefault("mopaFrequency")) ?? Num(p.GetValueOrDefault("mopaFrequency")) ?? Num(p.GetValueOrDefault("frequency")), raw));
            }
        }
        foreach (var p in localParams)
        {
            if (Str(p.GetValueOrDefault("deviceCode")) != deviceCode) continue;
            var raw = p.GetValueOrDefault("rawData") as Dictionary<string, object?>;
            Add(Str(p.GetValueOrDefault("materialId")), true, Str(p.GetValueOrDefault("name")), Settings(
                Str(p.GetValueOrDefault("processingTypeCode")), Str(p.GetValueOrDefault("processingModeCode")), Str(p.GetValueOrDefault("laserSourceCode")),
                Num(raw?.GetValueOrDefault("power")), Num(raw?.GetValueOrDefault("speed")), Num(raw?.GetValueOrDefault("repeat")), Num(raw?.GetValueOrDefault("density")),
                Num(raw?.GetValueOrDefault("dpi")), Num(raw?.GetValueOrDefault("dotDuration")), Str(raw?.GetValueOrDefault("bitmapMode")),
                Num(raw?.GetValueOrDefault("pulseWidth")), Num(raw?.GetValueOrDefault("mopaFrequency")), raw));
        }
        var o = new List<Found>();
        foreach (var (key, (yours, list)) in found)
        {
            if (!byId.TryGetValue(key[2..], out var m)) m = (yours ? "My xTool settings" : "xTool settings", null, "Other", false, yours);
            o.Add(new Found(m.Name, m.Thickness, m.Category, m.Reflective, yours, list));
        }
        return new Result([.. o.OrderBy(f => f.Yours ? 0 : 1).ThenBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase)], leftOut);
    }

    /// <summary>One xTool setting as ours; null for kinds we don't do (rotary, relief, knife, intaglio…).</summary>
    static ProcessSettings? Settings(string type, string mode, string source, double? power, double? speed, double? repeat, double? density,
        double? dpi, double? dotUs, string bitmapMode, double? pulseNs, double? freqKhz, Dictionary<string, object?>? raw)
    {
        if (mode is not ("LASER_PLANE" or "")) return null;
        LayerMode? m = type switch
        {
            "VECTOR_CUTTING" => LayerMode.Cut,
            "VECTOR_ENGRAVING" => LayerMode.Score,
            "FILL_VECTOR_ENGRAVING" or "COLOR_FILL_ENGRAVE" => LayerMode.Fill,
            "BITMAP_ENGRAVING" or "COLOR_ENGRAVE" => LayerMode.Image,
            _ => null,
        };
        if (m is not { } kind || power is not > 0) return null;
        double sp = speed is > 0 ? speed.Value : kind == LayerMode.Image ? 100 : 20;
        var laser = source == "red"
            ? LaserSettings.Red(power.Value, sp, (int)Math.Round(pulseNs is > 0 ? pulseNs.Value : 200), (int)Math.Round(freqKhz is > 0 ? freqKhz.Value : 60))
            : LaserSettings.Blue(power.Value, sp);
        laser = laser.WithPasses(Math.Max(1, (int)Math.Round(repeat ?? 1)));
        var s = new ProcessSettings { Mode = kind, Laser = laser };
        if (kind == LayerMode.Fill && density is > 0) s = s with { LinesPerCm = density.Value };
        if (kind == LayerMode.Image)
        {
            s = s with { Dpi = dpi is > 0 ? dpi.Value : 254, DotUs = (int)Math.Round((dotUs is > 0 ? dotUs.Value : 200) / 10) * 10 };
            s = bitmapMode.ToLowerInvariant() switch
            {
                "grayscale" or "greyscale" => s with { ImageMode = ImageEngraveMode.Grayscale },
                "jarvis" => s with { Dither = DitherMethod.Jarvis },
                "stucki" => s with { Dither = DitherMethod.Stucki },
                "atkinson" => s with { Dither = DitherMethod.Atkinson },
                "floydsteinberg" or "floyd" => s with { Dither = DitherMethod.FloydSteinberg },
                "bayer" or "ordered" => s with { Dither = DitherMethod.Bayer },
                "threshold" or "binary" => s with { Dither = DitherMethod.Threshold },
                "halftone" => s with { Dither = DitherMethod.Halftone },
                "sketch" => s with { Dither = DitherMethod.Sketch },
                _ => s,
            };
        }
        if (raw is not null)
        {
            if (Num(raw.GetValueOrDefault("scanAngle")) is { } a && kind == LayerMode.Fill) s = s with { FillAngleDeg = a };
            if (raw.GetValueOrDefault("crossAngle") is true && kind == LayerMode.Fill) s = s with { Crosshatch = true };
            if (raw.GetValueOrDefault("enableKerf") is true && Num(raw.GetValueOrDefault("kerfDistance")) is { } k) s = s with { KerfMm = k };
            // xTool's defocus lifts the focus off the surface (a softer, wider beam): ours is negative.
            if (raw.GetValueOrDefault("defocus") is true && Num(raw.GetValueOrDefault("defocus_distance")) is { } d) s = s with { FocusOffsetMm = -Math.Abs(d) };
        }
        return s;
    }

    /// <summary>A recipe's title when xTool's has none: "Cut · 60% · 10 mm/s × 2".</summary>
    public static string Describe(ProcessSettings s) =>
        $"{s.Mode switch { LayerMode.Cut => "Cut", LayerMode.Score => "Score", LayerMode.Fill => "Engrave", _ => "Picture" }} · " +
        (s.Laser.Light == LightSource.Red ? "MOPA " : "") + $"{s.Laser.Power:0.#}%" +
        (s.Mode == LayerMode.Image ? $" · {s.Dpi:0} dpi" : $" · {s.Laser.Speed:0.#} mm/s") + (s.Laser.Passes > 1 ? $" × {s.Laser.Passes}" : "");

    /// <summary>
    /// Put the settings into the library: each material found by name and thickness (or added), each
    /// setting by title (updated, or added). xTool's are marked as tested by xTool; the user's own
    /// from xTool Studio as theirs. Returns (materials added, settings added, settings updated).
    /// </summary>
    public static (int Materials, int Added, int Updated) Into(MaterialLibrary lib, Result r, string deviceCode)
    {
        int mats = 0, added = 0, updated = 0;
        foreach (var f in r.Materials)
        {
            var m = lib.Materials.FirstOrDefault(x => string.Equals(x.Name, f.Name, StringComparison.CurrentCultureIgnoreCase) && x.ThicknessMm == f.ThicknessMm);
            if (m is null)
            {
                m = new Material { Name = f.Name, ThicknessMm = f.ThicknessMm, Category = f.Category, HighlyReflective = f.Reflective, Notes = f.Yours ? "From your xTool Studio settings." : "From xTool Studio's material library." };
                mats++;
            }
            var recipes = m.Recipes;
            foreach (var (title, s) in f.Settings)
            {
                string t = string.IsNullOrWhiteSpace(title) ? Describe(s) : title.Trim();
                t = f.Yours ? t : $"{t} (xTool)";
                var recipe = new Recipe
                {
                    Purpose = s.Mode switch { LayerMode.Cut => RecipePurpose.Cut, LayerMode.Score => RecipePurpose.Score, LayerMode.Fill => RecipePurpose.Engrave, _ => RecipePurpose.Picture },
                    Title = t, Settings = s, Tested = !f.Yours, TestedBy = f.Yours ? "" : MaterialLibrary.XToolTester, Machine = deviceCode,
                    Notes = f.Yours ? "Made in xTool Studio." : "xTool's own tested setting, from xTool Studio's library.",
                };
                int at = recipes.FindIndex(x => x.Title == t);
                if (at < 0) { recipes = recipes.Add(recipe); added++; }
                else if (recipes[at].Settings != s) { recipes = recipes.SetItem(at, recipes[at] with { Settings = s, Updated = DateTime.Now }); updated++; }
            }
            lib.Upsert(m with { Recipes = recipes });
        }
        return (mats, added, updated);
    }

    // ---- reading the records

    static IEnumerable<object?> Items(object? v) => v switch { List<object?> l => l, Dictionary<string, object?> d => d.Values, _ => [] };

    static string Code(object? v) => v is Dictionary<string, object?> d ? Str(d.GetValueOrDefault("code")) : "";

    static string Str(object? v) => v switch { string s => s, double d => d.ToString(CultureInfo.InvariantCulture), bool b => b ? "true" : "false", _ => "" };

    static double? Num(object? v) => v switch
    {
        double d when double.IsFinite(d) => d,
        string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => d,
        _ => null,
    };

    /// <summary>A setting's value: the full record's (rawData) when it has one, else the summary's.</summary>
    static double? Field(Dictionary<string, object?>? raw, Dictionary<string, object?> p, string key) =>
        Num(raw?.GetValueOrDefault(key)) is { } r and > 0 ? r : Num(p.GetValueOrDefault(key));

    static IEnumerable<string> Codes(object? v) => Items(v).Select(Str);

    /// <summary>The English name (xTool keeps one per language), or the only one.</summary>
    static string Name(object? v) => v switch
    {
        string s => s.Trim(),
        Dictionary<string, object?> d => (Str(d.GetValueOrDefault("en")) is { Length: > 0 } en ? en : d.Values.Select(Str).FirstOrDefault(x => x.Length > 0) ?? "").Trim(),
        _ => "",
    };

    static string Category(string code, string second) => (code.ToUpperInvariant(), second.ToUpperInvariant()) switch
    {
        (_, "PLYWOOD" or "MDF") => "Plywood & MDF",
        ("WOOD", _) => "Wood",
        ("ACRYLIC", _) => "Acrylic",
        ("LEATHER", _) => "Leather",
        ("PAPER", _) => "Paper & card",
        ("FABRIC", _) => "Fabric",
        ("METAL", _) => "Metal",
        ("STONE", _) => "Stone & tile",
        ("GLASS", _) => "Glass",
        ("PLASTIC", _) => "Plastic",
        _ => "Other",
    };
}
