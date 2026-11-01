using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;

namespace PhotonCanvas.Addon.Grain;

/// <summary>What a layer does with the grain.</summary>
public enum GrainMode
{
    /// <summary>The layer's power everywhere.</summary>
    Off,
    /// <summary>Less power where the grain would come out darker, so the engraving is even.</summary>
    Even,
    /// <summary>The opposite: less power where the grain would come out lighter, so the grain shows more (artistic).</summary>
    BringOut,
}

/// <summary>A layer's grain-aware settings.</summary>
/// <param name="Strength">How much less power at most, 0–<see cref="MaxStrength"/> (0.25 = up to a quarter less).</param>
/// <param name="DarkBurnsDarker">On this wood, dark grain comes out darker than light grain at the same power (the usual case; a test burn tells).</param>
/// <param name="TestFactor">This layer is a strip of a grain test burned at this share of the tested layer's power (never compensated). Null for an ordinary layer.</param>
/// <param name="TestOf">For a test strip: the layer it tests (its result goes there).</param>
public sealed record GrainSettings(GrainMode Mode = GrainMode.Off, double Strength = 0.25, bool DarkBurnsDarker = true, double? TestFactor = null, Guid? TestOf = null)
{
    /// <summary>At most this much less power (60 %): past that the engraving's own settings are wrong for the wood.</summary>
    public const double MaxStrength = 0.6;

    public static readonly GrainSettings None = new();

    /// <summary>Does this layer's power change with the grain?</summary>
    public bool Active => Mode != GrainMode.Off && Strength > 0 && TestFactor is null;

    /// <summary>The share of the layer's power to use on grain of darkness <paramref name="t"/> (0 = lightest, 1 = darkest).</summary>
    public double Factor(double t)
    {
        if (!Active || double.IsNaN(t)) return 1;
        double s = Math.Clamp(Strength, 0, MaxStrength);
        // Even: less where the grain burns darker. Bring out: less where it burns lighter.
        bool lessOnDark = (Mode == GrainMode.Even) == DarkBurnsDarker;
        return 1 - s * (lessOnDark ? Math.Clamp(t, 0, 1) : 1 - Math.Clamp(t, 0, 1));
    }

    internal JsonObject ToJson() => new()
    {
        ["mode"] = Mode.ToString(),
        ["strength"] = Math.Round(Strength, 4),
        ["darkBurnsDarker"] = DarkBurnsDarker,
        ["test"] = TestFactor,
        ["testOf"] = TestOf?.ToString(),
    };

    internal static GrainSettings FromJson(JsonNode? n)
    {
        if (n is not JsonObject o) return None;
        try
        {
            return new GrainSettings(
                Enum.TryParse<GrainMode>(o["mode"]?.GetValue<string>(), true, out var m) ? m : GrainMode.Off,
                Math.Clamp(o["strength"]?.GetValue<double>() ?? 0.25, 0, MaxStrength),
                o["darkBurnsDarker"]?.GetValue<bool>() ?? true,
                o["test"]?.GetValue<double>(),
                Guid.TryParse(o["testOf"]?.GetValue<string>(), out var of) ? of : null);
        }
        catch (Exception e) when (e is InvalidOperationException or FormatException)
        {
            return None;
        }
    }
}

/// <summary>
/// What a test burn showed about this wood: how much darker dark grain comes out, as a share of
/// the power that makes the same difference. <see cref="Strength"/> is the even setting it suggests.
/// </summary>
/// <param name="Fit">How well the measurements follow the model (R², 0–1).</param>
/// <param name="Samples">How many places were measured.</param>
public sealed record GrainCalibration(double Strength, bool DarkBurnsDarker, double Fit, int Samples, DateTimeOffset When);

/// <summary>
/// The add-on's notes in the project (<see cref="DocState.AddonData"/>): the board's grain map, the
/// last test's result, and each layer's settings by layer id. Kept in the file while the add-on is off.
/// </summary>
public static class GrainProject
{
    public const string MapKey = "grain.map", CalibrationKey = "grain.calibration", LayersKey = "grain.layers";

    // Parsed once per saved text: a job asks for these every time it's made.
    static readonly ConditionalWeakTable<string, GrainMap> Maps = new();
    static readonly ConditionalWeakTable<string, Dictionary<Guid, GrainSettings>> Layers = new();

    /// <summary>The board's grain map, if one was made.</summary>
    public static GrainMap? Map(DocState s)
    {
        if (!s.AddonData.TryGetValue(MapKey, out var json)) return null;
        if (Maps.TryGetValue(json, out var m)) return m;
        GrainMap? parsed;
        try { parsed = GrainMap.FromJson(JsonNode.Parse(json)); }
        catch (JsonException) { parsed = null; }
        if (parsed is not null) Maps.AddOrUpdate(json, parsed);
        return parsed;
    }

    /// <summary>The project with this map (null takes it away).</summary>
    public static DocState WithMap(DocState s, GrainMap? map)
    {
        if (map is null) return s with { AddonData = s.AddonData.Remove(MapKey) };
        var json = map.ToJson().ToJsonString();
        // Already known: the job doesn't decode the picture again.
        Maps.AddOrUpdate(json, map);
        return s with { AddonData = s.AddonData.SetItem(MapKey, json) };
    }

    /// <summary>The last grain test's result, if any.</summary>
    public static GrainCalibration? Calibration(DocState s)
    {
        if (!s.AddonData.TryGetValue(CalibrationKey, out var json)) return null;
        try
        {
            if (JsonNode.Parse(json) is not JsonObject c) return null;
            return new GrainCalibration(c["strength"]?.GetValue<double>() ?? 0, c["darkBurnsDarker"]?.GetValue<bool>() ?? true,
                c["fit"]?.GetValue<double>() ?? 0, c["samples"]?.GetValue<int>() ?? 0,
                DateTimeOffset.TryParse(c["when"]?.GetValue<string>(), out var w) ? w : DateTimeOffset.MinValue);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    public static DocState WithCalibration(DocState s, GrainCalibration? cal) => cal is null
        ? s with { AddonData = s.AddonData.Remove(CalibrationKey) }
        : s with
        {
            AddonData = s.AddonData.SetItem(CalibrationKey, new JsonObject
            {
                ["strength"] = Math.Round(cal.Strength, 4),
                ["darkBurnsDarker"] = cal.DarkBurnsDarker,
                ["fit"] = Math.Round(cal.Fit, 4),
                ["samples"] = cal.Samples,
                ["when"] = cal.When.ToString("o"),
            }.ToJsonString()),
        };

    static Dictionary<Guid, GrainSettings> AllLayers(DocState s)
    {
        if (!s.AddonData.TryGetValue(LayersKey, out var json)) return [];
        return Layers.GetValue(json, j =>
        {
            var d = new Dictionary<Guid, GrainSettings>();
            try
            {
                if (JsonNode.Parse(j) is JsonObject o)
                    foreach (var (k, v) in o)
                        if (Guid.TryParse(k, out var id)) d[id] = GrainSettings.FromJson(v);
            }
            catch (JsonException) { }
            return d;
        });
    }

    /// <summary>A layer's settings (off when it has none).</summary>
    public static GrainSettings For(DocState s, Guid layerId) => AllLayers(s).GetValueOrDefault(layerId) ?? GrainSettings.None;

    /// <summary>The project with a layer's settings changed (<see cref="GrainSettings.None"/> takes them away). Settings of layers no longer in the project are dropped.</summary>
    public static DocState WithLayer(DocState s, Guid layerId, GrainSettings settings)
    {
        var all = new Dictionary<Guid, GrainSettings>(AllLayers(s));
        if (settings == GrainSettings.None) all.Remove(layerId);
        else all[layerId] = settings;
        // Layers on other pages count too (each page has its own layers).
        var ids = s.Layers.Select(l => l.Id).Concat(s.OtherPages.SelectMany(p => p.Layers.Select(l => l.Id))).ToHashSet();
        var o = new JsonObject();
        foreach (var (id, set) in all.Where(kv => ids.Contains(kv.Key)).OrderBy(kv => kv.Key))
            o[id.ToString()] = set.ToJson();
        return o.Count == 0
            ? s with { AddonData = s.AddonData.Remove(LayersKey) }
            : s with { AddonData = s.AddonData.SetItem(LayersKey, o.ToJsonString()) };
    }

    /// <summary>Every layer's settings, on every page.</summary>
    public static IReadOnlyDictionary<Guid, GrainSettings> All(DocState s) => AllLayers(s);

    /// <summary>The layers on the page that follow the grain.</summary>
    public static IEnumerable<(Layer Layer, GrainSettings Settings)> Active(DocState s) =>
        s.Layers.Select(l => (l, For(s, l.Id))).Where(x => x.Item2.Active);
}
