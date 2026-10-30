using System.Reflection;
using System.Text.Json;

namespace PhotonCanvas.Core;

/// <summary>
/// New versions: published as releases on a public, releases-only GitHub repository (the source
/// repository is private; the program is free to use, all rights reserved). At start, if the user allows it, the
/// program asks GitHub for the latest release (nothing about the user is sent, beyond what any web
/// request carries) and offers it.
/// </summary>
public static class Updates
{
    /// <summary>Where releases are published.</summary>
    public const string Repo = "noalear/PhotonCanvas-releases";

    /// <summary>This program's version ("0.2.0"), from the build.</summary>
    public static string Current { get; } = Clean(
        Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(Updates).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0");

    /// <summary>A release: its version, page, notes, and the files to download (name → link).</summary>
    public sealed record Release(string Version, string PageUrl, string Notes, IReadOnlyList<(string Name, string Url)> Files)
    {
        /// <summary>The download for this computer, if there's one (win-x64, osx-arm64, linux-x64…).</summary>
        public (string Name, string Url)? ForThisComputer
        {
            get
            {
                foreach (var f in Files)
                    if (f.Name.Contains(Platform, StringComparison.OrdinalIgnoreCase)) return f;
                return null;
            }
        }
    }

    /// <summary>This computer's build name: win-x64, osx-arm64, linux-x64…</summary>
    public static string Platform =>
        (OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : "linux") + "-" +
        (System.Runtime.InteropServices.RuntimeInformation.OSArchitecture == System.Runtime.InteropServices.Architecture.Arm64 ? "arm64" : "x64");

    /// <summary>"v1.2.0+abc123" → "1.2.0".</summary>
    public static string Clean(string v)
    {
        v = v.Trim();
        if (v.StartsWith('v') || v.StartsWith('V')) v = v[1..];
        int plus = v.IndexOf('+');
        return plus >= 0 ? v[..plus] : v;
    }

    /// <summary>Is <paramref name="a"/> a later version than <paramref name="b"/>? Numbers part by part ("1.10" after "1.9"); a "-beta" comes before the plain version.</summary>
    public static bool Newer(string a, string b) => Compare(Clean(a), Clean(b)) > 0;

    static int Compare(string a, string b)
    {
        static (int[] Nums, string Pre) Split(string v)
        {
            int dash = v.IndexOf('-');
            string main = dash >= 0 ? v[..dash] : v, pre = dash >= 0 ? v[(dash + 1)..] : "";
            return ([.. main.Split('.').Select(p => int.TryParse(p, out int n) ? n : 0)], pre);
        }
        var (x, xp) = Split(a);
        var (y, yp) = Split(b);
        for (int i = 0; i < Math.Max(x.Length, y.Length); i++)
        {
            int c = (i < x.Length ? x[i] : 0).CompareTo(i < y.Length ? y[i] : 0);
            if (c != 0) return c;
        }
        if (xp.Length == 0 && yp.Length > 0) return 1;
        if (xp.Length > 0 && yp.Length == 0) return -1;
        return string.CompareOrdinal(xp, yp);
    }

    /// <summary>GitHub's "latest release" answer; null when it isn't one.</summary>
    public static Release? Parse(string json)
    {
        try
        {
            using var d = JsonDocument.Parse(json);
            var r = d.RootElement;
            if (!r.TryGetProperty("tag_name", out var tag) || tag.GetString() is not { Length: > 0 } t) return null;
            if (r.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True) return null;
            var files = new List<(string, string)>();
            if (r.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
                foreach (var a in assets.EnumerateArray())
                    if (a.TryGetProperty("name", out var n) && a.TryGetProperty("browser_download_url", out var u) && n.GetString() is { } name && u.GetString() is { } url)
                        files.Add((name, url));
            return new Release(Clean(t), r.TryGetProperty("html_url", out var page) ? page.GetString() ?? "" : "",
                r.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "", files);
        }
        catch (JsonException) { return null; }
    }

    /// <summary>Ask GitHub for the latest release. Null when there's none, or it can't be reached (offline is normal).</summary>
    public static async Task<Release?> LatestAsync(HttpClient http, CancellationToken ct = default)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Repo}/releases/latest");
            req.Headers.UserAgent.ParseAdd($"{AppIdentity.ProductName}/{Current}");
            req.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var res = await http.SendAsync(req, ct).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode) return null;
            return Parse(await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or OperationCanceledException) { return null; }
    }
}
