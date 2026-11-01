using PhotonCanvas.App;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;

namespace PhotonCanvas.Addon.DepthCharge;

/// <summary>What a running DepthCharge reports about itself.</summary>
public sealed record DepthChargeHealth(bool Gpu, bool Quality, bool Fast, bool DepthPro);

/// <summary>A depth job's progress.</summary>
public sealed record DepthJob(string Id, string Status, string? Error, int Width, int Height, int? PatchesDone, double? Seconds)
{
    /// <summary>Tiles done and in all while a large picture is looked at in tiles; once ready, the depth's own size.</summary>
    public int? TilesDone { get; init; }
    public int? TilesTotal { get; init; }
    public int? DepthWidth { get; init; }
    public int? DepthHeight { get; init; }
}

/// <summary>How DepthCharge looks at a picture bigger than its model or far from square.</summary>
public enum DepthTiles { Auto, Fine, Off }

/// <summary>
/// Post-processing applied when fetching a depthmap (no new inference), exactly as
/// DepthCharge's own sliders: midtone lift (gamma), depth-preserving smoothing, black and
/// white points, ignoring extreme tails, emboss or deboss, and the per-model detail controls.
/// </summary>
public sealed record DepthTone
{
    public double Gamma { get; init; } = 1;
    public double Smooth { get; init; }
    public double Black { get; init; }
    public double White { get; init; } = 100;
    public double Clip { get; init; }
    public bool Deboss { get; init; }
    /// <summary>Local reconstruction: brightness-assisted artwork detail (0–1).</summary>
    public double ArtworkDetail { get; init; } = 0.25;
    /// <summary>DA3 + fine detail: model detail strength (0–2), scale (2–64 px) and image texture (0–1).</summary>
    public double Blend { get; init; } = 0.5;
    public double DetailScale { get; init; } = 12;
    public double Texture { get; init; }
    public bool RemoveBackground { get; init; }
    /// <summary>Tiled pictures: strength of the fine detail the tiles found (0–2; 1 as found, 0 the whole picture's shape alone).</summary>
    public double TileDetail { get; init; } = 1;
}

/// <summary>
/// Talks to the user's own DepthCharge (a local Python app that turns pictures into
/// depthmaps with DA3 / Depth Pro on the GPU) over its localhost API, starting it in the
/// background if it isn't already running. DepthCharge stays a separate program; nothing
/// is sent anywhere but 127.0.0.1.
/// </summary>
public sealed class DepthChargeService : IDisposable
{
    public const string Url = "http://127.0.0.1:8765";
    readonly HttpClient _http = new() { BaseAddress = new Uri(Url), Timeout = TimeSpan.FromMinutes(5) };
    Process? _started;
    readonly List<string> _output = [];

    /// <summary>Where DepthCharge usually lives: installed by its Setup, or a copied folder in Documents.</summary>
    public static IEnumerable<string> LikelyFolders(string? remembered)
    {
        if (!string.IsNullOrWhiteSpace(remembered)) yield return remembered;
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "DepthCharge");
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "DepthCharge");
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        yield return Path.Combine(home, "Documents", "DepthCharge");
        yield return Path.Combine(home, "OneDrive", "Documents", "DepthCharge");
    }

    /// <summary>A DepthCharge folder that has been set up (its private Python and the app).</summary>
    public static bool IsUsable(string folder) =>
        File.Exists(Path.Combine(folder, "runtime", "python", "python.exe")) && File.Exists(Path.Combine(folder, "app.py"));

    public static string? Find(string? remembered) => LikelyFolders(remembered).FirstOrDefault(IsUsable);

    /// <summary>Health of a running DepthCharge, or null if none is listening.</summary>
    public async Task<DepthChargeHealth?> HealthAsync(CancellationToken ct = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(3));
            var j = JsonNode.Parse(await _http.GetStringAsync("/api/health", cts.Token))!.AsObject();
            if (j["app"]?.GetValue<string>() != "DepthCharge") return null;
            var models = j["models"] as JsonObject;
            bool Has(string m) => models?[m]?.GetValue<bool>() == true;
            bool gpu = j["providers"] is JsonArray p && p.Any(x => x?.GetValue<string>() == "DmlExecutionProvider");
            return new DepthChargeHealth(gpu, Has("quality"), Has("fast"), Has("depthpro"));
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>Use a running DepthCharge, or start the one in <paramref name="folder"/> (hidden) and wait for it.</summary>
    public async Task<DepthChargeHealth> EnsureRunningAsync(string folder, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        if (await HealthAsync(ct) is { } running) return running;
        if (!IsUsable(folder)) throw new InvalidOperationException($"DepthCharge isn't set up in {folder} (run its Setup first).");
        if (_started is null || _started.HasExited)
        {
            progress?.Report("Starting DepthCharge…");
            var psi = new ProcessStartInfo(Path.Combine(folder, "runtime", "python", "python.exe"))
            {
                WorkingDirectory = folder,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            // Same isolation as its own launcher; serve only, no browser.
            psi.Environment.Remove("PYTHONHOME");
            psi.Environment.Remove("PYTHONPATH");
            psi.Environment["PYTHONNOUSERSITE"] = "1";
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add("import sys; sys.path.insert(0, r'" + folder.Replace("'", "\\'") + "'); import uvicorn; uvicorn.run('app:app', host='127.0.0.1', port=8765, log_level='warning')");
            _output.Clear();
            var p = Process.Start(psi) ?? throw new InvalidOperationException("DepthCharge didn't start.");
            void Keep(string? line) { if (line is null) return; lock (_output) { _output.Add(line); if (_output.Count > 40) _output.RemoveAt(0); } }
            p.OutputDataReceived += (_, e) => Keep(e.Data);
            p.ErrorDataReceived += (_, e) => Keep(e.Data);
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            _started = p;
        }
        for (int i = 0; i < 180; i++)
        {
            await Task.Delay(500, ct);
            if (await HealthAsync(ct) is { } h) return h;
            if (_started.HasExited)
                throw new InvalidOperationException("DepthCharge stopped while starting:\n" + string.Join("\n", LastOutput()));
        }
        throw new TimeoutException("DepthCharge didn't answer within 90 seconds.");
    }

    IEnumerable<string> LastOutput() { lock (_output) return _output.TakeLast(8).ToList(); }

    /// <summary>
    /// Start a depth estimate. Models: quality (DA3 Large), fast (V2 Small), relief, hybrid, depthpro.
    /// Tiles: a big or wide picture is looked at in overlapping tiles fitted to the whole picture's shape.
    /// </summary>
    public async Task<DepthJob> SubmitAsync(byte[] image, string name, string model, int size, DepthTiles tiles = DepthTiles.Auto, CancellationToken ct = default)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(image);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", string.IsNullOrWhiteSpace(name) ? "image.png" : name + ".png");
        form.Add(new StringContent(model), "model");
        form.Add(new StringContent(size.ToString(CultureInfo.InvariantCulture)), "size");
        form.Add(new StringContent(tiles.ToString().ToLowerInvariant()), "tiles");
        using var r = await _http.PostAsync("/api/jobs", form, ct);
        return Job(await Read(r, ct));
    }

    public async Task<DepthJob> StatusAsync(string id, CancellationToken ct = default)
    {
        using var r = await _http.GetAsync($"/api/jobs/{id}", ct);
        return Job(await Read(r, ct));
    }

    /// <summary>The depthmap as PNG (white = shallow, black = deep), with any crop/mask saved in DepthCharge applied.</summary>
    public async Task<byte[]> DepthPngAsync(string id, DepthTone t, int longest, int bits = 8, CancellationToken ct = default)
    {
        static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
        var q = $"longest={longest}&bits={bits}&gamma={F(t.Gamma)}&smooth={F(t.Smooth)}&black={F(t.Black)}&white={F(t.White)}&clip={F(t.Clip)}" +
                $"&polarity={(t.Deboss ? "deboss" : "emboss")}&detail={F(t.ArtworkDetail)}&blend={F(t.Blend)}&detail_scale={F(t.DetailScale)}" +
                $"&texture={F(t.Texture)}&remove_background={(t.RemoveBackground ? "true" : "false")}&tile_detail={F(t.TileDetail)}";
        using var r = await _http.GetAsync($"/api/jobs/{id}/depth?{q}", ct);
        if (!r.IsSuccessStatusCode) throw new InvalidOperationException(Detail(await r.Content.ReadAsStringAsync(ct), r));
        return await r.Content.ReadAsByteArrayAsync(ct);
    }

    static async Task<JsonObject> Read(HttpResponseMessage r, CancellationToken ct)
    {
        var text = await r.Content.ReadAsStringAsync(ct);
        if (!r.IsSuccessStatusCode) throw new InvalidOperationException(Detail(text, r));
        return JsonNode.Parse(text)!.AsObject();
    }

    static string Detail(string body, HttpResponseMessage r)
    {
        try { if (JsonNode.Parse(body)?["detail"]?.ToString() is { Length: > 0 } d) return d; }
        catch (System.Text.Json.JsonException) { }
        return $"DepthCharge answered {(int)r.StatusCode} {r.ReasonPhrase}.";
    }

    static DepthJob Job(JsonObject j) => new(
        j["id"]!.GetValue<string>(), j["status"]?.GetValue<string>() ?? "?", j["error"]?.ToString(),
        j["width"]?.GetValue<int>() ?? 0, j["height"]?.GetValue<int>() ?? 0,
        j["patches_done"] is JsonValue pd && pd.TryGetValue<int>(out var n) ? n : null,
        j["seconds"] is JsonValue s && s.TryGetValue<double>(out var sec) ? sec : null)
    {
        TilesDone = Int(j, "tiles_done"),
        TilesTotal = Int(j, "tiles_total"),
        DepthWidth = Int(j, "depth_width"),
        DepthHeight = Int(j, "depth_height"),
    };

    static int? Int(JsonObject j, string key) => j[key] is JsonValue v && v.TryGetValue<int>(out var n) ? n : null;

    /// <summary>Stop DepthCharge if this program started it (leave one the user started alone).</summary>
    public void Dispose()
    {
        try { if (_started is { HasExited: false } p) p.Kill(entireProcessTree: true); }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        _started?.Dispose();
        _http.Dispose();
    }
}
