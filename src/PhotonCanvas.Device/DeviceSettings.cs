using System.Text.Json.Nodes;

namespace PhotonCanvas.Device;

/// <summary>A Wi-Fi network the machine can see.</summary>
public sealed record WifiNetwork(string Ssid, int? Signal);

/// <summary>
/// The machine's own settings and services (F2 Ultra, V2 API): stored options, its name, galvo
/// timing per laser, resuming after a power cut, Wi-Fi, the safety interlocks and keeping a job on
/// the machine. None of these move the head or fire the laser.
/// </summary>
public static class DeviceSettings
{
    const string Configs = "/v1/device/configs";

    /// <summary>Stored options the settings page shows (see <see cref="SetConfigAsync"/>).</summary>
    public static readonly string[] Keys =
    [
        "flameAlarm", "workingMode", "gapCheck", "gapCheckWithKey", "beepEnable", "purifierTimeout", "purifierBeepEnable",
        "autoSleepEnable", "laserTempAlarmEnable", "fillLightBrightFront", "fillLightBrightBack",
    ];

    /// <summary>Read stored options by name (missing ones are left out).</summary>
    public static async Task<JsonObject> GetConfigAsync(this DeviceClient c, IEnumerable<string> keys, CancellationToken ct = default)
    {
        var data = await c.GetAsync(Configs, new JsonObject { ["alias"] = "config", ["type"] = "user", ["kv"] = new JsonArray([.. keys.Select(k => (JsonNode)k)]) }, cancellationToken: ct).ConfigureAwait(false);
        return data as JsonObject ?? [];
    }

    /// <summary>Change one stored option.</summary>
    public static Task SetConfigAsync(this DeviceClient c, string key, JsonNode value, CancellationToken ct = default) =>
        c.RequestAsync("PUT", Configs, new JsonObject { ["alias"] = "config", ["type"] = "user", ["kv"] = new JsonObject { [key] = value } }, cancellationToken: ct);

    /// <summary>Rename the machine (shown on its screen and when searching). Up to 15 characters.</summary>
    public static Task SetDeviceNameAsync(this DeviceClient c, string name, CancellationToken ct = default)
    {
        name = name.Trim();
        if (name.Length is 0 or > 15) throw new ArgumentException("The name must be 1 to 15 characters.", nameof(name));
        return c.RequestAsync("PUT", "/v1/device/machineInfo", new JsonObject { ["deviceName"] = name }, cancellationToken: ct);
    }

    /// <summary>How many jobs the machine has run (from the computer and from its own screen).</summary>
    public static async Task<(long Online, long Offline)> GetJobCountsAsync(this DeviceClient c, CancellationToken ct = default)
    {
        var d = await c.GetAsync("/v1/device/statistics", cancellationToken: ct).ConfigureAwait(false) as JsonObject;
        return ((long)Num(d?["numOnlineWorking"]), (long)Num(d?["numOfflineWorking"]));
    }

    // ---- galvo timing ------------------------------------------------------------------------

    /// <summary>The galvo timing values, in the order the settings page shows them.</summary>
    public static readonly (string Key, string Label)[] TimingKeys =
    [
        ("openLaserDelay", "Laser on delay"), ("closeLaserDelay", "Laser off delay"), ("closeLocationDelay", "End-of-line delay"),
        ("cornerDelay", "Corner delay"), ("jumpLocationDelay", "Jump delay"), ("jumpDistanceDelay", "Jump distance delay"), ("dotLocationFinish", "Dot settle delay"),
    ];

    const string Timing = "/v1/motion_control/paramter"; // sic: the firmware's spelling

    /// <summary>One laser's timing ("blue" = diode, "red" = MOPA), as stored (xTool Studio shows them ×10).</summary>
    public static async Task<Dictionary<string, double>> GetTimingAsync(this DeviceClient c, string laser, CancellationToken ct = default)
    {
        var data = await c.RequestAsync("GET", Timing, new JsonObject
        {
            ["action"] = "infoGet",
            ["data"] = new JsonArray(new JsonObject { ["type"] = laser, ["params"] = new JsonArray([.. TimingKeys.Select(k => (JsonNode)k.Key)]) }),
        }, cancellationToken: ct).ConfigureAwait(false);
        var o = new Dictionary<string, double>();
        foreach (var item in (data as JsonArray ?? (data?["data"] as JsonArray) ?? []).OfType<JsonObject>())
            if (item["type"]?.GetValue<string>() == laser && item["para"] is JsonObject para)
                foreach (var (k, v) in para) if (v is not null) o[k] = Num(v);
        return o;
    }

    public static Task SetTimingAsync(this DeviceClient c, string laser, string key, double stored, CancellationToken ct = default) =>
        c.RequestAsync("PUT", Timing, new JsonObject
        {
            ["action"] = "infoSet",
            ["data"] = new JsonArray(new JsonObject { ["type"] = laser, ["params"] = new JsonObject { [key] = stored } }),
        }, cancellationToken: ct);

    /// <summary>Back to the factory timing for one laser.</summary>
    public static Task ResetTimingAsync(this DeviceClient c, string laser, CancellationToken ct = default) =>
        c.RequestAsync("PUT", Timing, new JsonObject { ["action"] = "reset", ["data"] = new JsonArray(new JsonObject { ["type"] = laser }) }, cancellationToken: ct);

    // ---- power cut ---------------------------------------------------------------------------

    const string PowerResume = "/v1/processing/powerResume";

    /// <summary>True when a job was cut short by a power cut and can carry on from where it stopped.</summary>
    public static async Task<bool> HasInterruptedJobAsync(this DeviceClient c, CancellationToken ct = default)
    {
        var d = await c.RequestAsync("GET", PowerResume, null, new JsonObject { ["action"] = "query" }, cancellationToken: ct).ConfigureAwait(false);
        return d?["status"] is JsonValue v && v.TryGetValue<bool>(out var b) && b;
    }

    /// <summary>Get the interrupted job ready to carry on: it starts when the machine's button is pressed.</summary>
    public static Task ArmInterruptedJobAsync(this DeviceClient c, CancellationToken ct = default) =>
        c.RequestAsync("PUT", PowerResume, null, new JsonObject { ["action"] = "start" }, cancellationToken: ct);

    /// <summary>Forget the interrupted job.</summary>
    public static Task DropInterruptedJobAsync(this DeviceClient c, CancellationToken ct = default) =>
        c.RequestAsync("PUT", PowerResume, null, new JsonObject { ["action"] = "cancel" }, cancellationToken: ct);

    // ---- Wi-Fi -------------------------------------------------------------------------------

    /// <summary>Networks the machine can see, strongest first, each name once.</summary>
    public static async Task<List<WifiNetwork>> ScanWifiAsync(this DeviceClient c, CancellationToken ct = default)
    {
        var d = await c.GetAsync("/v1/wifi/ap-list", timeout: TimeSpan.FromSeconds(30), cancellationToken: ct).ConfigureAwait(false);
        var list = d as JsonArray ?? d?["list"] as JsonArray ?? d?["aps"] as JsonArray ?? (d as JsonObject)?.Select(kv => kv.Value).OfType<JsonArray>().FirstOrDefault() ?? [];
        var nets = new List<WifiNetwork>();
        foreach (var n in list.OfType<JsonObject>())
        {
            var ssid = n.FirstOrDefault(kv => kv.Key.Contains("ssid", StringComparison.OrdinalIgnoreCase)).Value?.ToString();
            if (string.IsNullOrWhiteSpace(ssid)) continue;
            var sig = n.FirstOrDefault(kv => kv.Key.Contains("signal", StringComparison.OrdinalIgnoreCase) || kv.Key.Contains("rssi", StringComparison.OrdinalIgnoreCase) || kv.Key.Contains("level", StringComparison.OrdinalIgnoreCase)).Value;
            nets.Add(new WifiNetwork(ssid, sig is null ? null : (int)Num(sig)));
        }
        return [.. nets.GroupBy(x => x.Ssid).Select(g => g.MaxBy(x => x.Signal ?? int.MinValue)!).OrderByDescending(x => x.Signal ?? int.MinValue)];
    }

    /// <summary>Join a network. The machine drops its current link while it changes over.</summary>
    public static Task JoinWifiAsync(this DeviceClient c, string ssid, string password, CancellationToken ct = default) =>
        c.RequestAsync("PUT", "/v1/wifi/credentials", new JsonObject { ["username"] = ssid, ["passwd"] = password }, timeout: TimeSpan.FromSeconds(60), cancellationToken: ct);

    /// <summary>The network the machine is on now, if any.</summary>
    public static async Task<string?> ConnectedWifiAsync(this DeviceClient c, CancellationToken ct = default)
    {
        var d = await c.GetAsync("/v1/wifi/connected-ssid", cancellationToken: ct).ConfigureAwait(false);
        return d is JsonValue v ? v.ToString() : d?["ssid"]?.ToString() ?? d?.AsObject().FirstOrDefault().Value?.ToString();
    }

    // ---- interlocks --------------------------------------------------------------------------

    /// <summary>One accessory's or sensor's state: "gap" (lid), "machine_lock" (safety key), "smoking_fan" (exhaust), "emergency_stop".</summary>
    public static async Task<JsonObject> GetPeripheralAsync(this DeviceClient c, string type, CancellationToken ct = default) =>
        await c.GetAsync("/v1/peripheral/param", null, new JsonObject { ["type"] = type }, cancellationToken: ct).ConfigureAwait(false) as JsonObject ?? [];

    // ---- jobs kept on the machine --------------------------------------------------------------

    /// <summary>
    /// Keep the last job on the machine under <paramref name="name"/> (up to 15 letters and digits),
    /// to run again from its own screen. Needs the machine's touchscreen controller.
    /// </summary>
    public static async Task KeepLastJobAsync(this DeviceClient c, string name, CancellationToken ct = default)
    {
        if (name.Length is 0 or > 15 || !name.All(char.IsAsciiLetterOrDigit))
            throw new ArgumentException("Use 1 to 15 letters and digits (no spaces).", nameof(name));
        var resp = await c.SendRequestAsync("POST", "/v1/file-backups", new JsonObject { ["filename"] = name, ["filetype"] = "xf" }, cancellationToken: ct).ConfigureAwait(false);
        if (resp.IsSuccess) return;
        throw new DeviceException(resp.Code, resp.Code switch
        {
            1 => $"There's already a job called {name} on the machine.",
            2 => "The machine has no finished job to keep.",
            3 => "The machine has no room left.",
            5 => "The machine couldn't read the job.",
            6 => "The name is too long.",
            _ => $"The machine couldn't keep the job ({resp.Message}).",
        });
    }

    static double Num(JsonNode? n) => n is JsonValue v
        ? v.TryGetValue<double>(out var d) ? d : v.TryGetValue<string>(out var s) && double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var p) ? p : 0
        : 0;
}
