using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PhotonCanvas.Core;

/// <summary>
/// New versions: published as GitHub releases (by the Release workflow in .github/workflows, or
/// tools/release.sh). A published copy of the program knows which repository it came from (the
/// <c>update.json</c> beside it); it asks GitHub for the latest release there (nothing about the user
/// is sent, beyond what any web request carries), downloads the file for this computer, checks it
/// against the release's SHA256SUMS and puts it in (see <see cref="UpdateInstall"/>). A private
/// repository needs an access key (<see cref="UpdateInstall.ReadToken"/>).
/// </summary>
public static partial class Updates
{
    /// <summary>Where releases are published when the program doesn't say otherwise (a build from the source code).</summary>
    public const string DefaultRepo = "noalear/PhotonCanvas";

    /// <summary>The release file holding each download's SHA-256 ("hash  name" lines, as sha256sum writes them).</summary>
    public const string SumsFile = "SHA256SUMS";

    /// <summary>This program's version ("0.2.0"), from the build.</summary>
    public static string Current { get; } = Clean(
        Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(Updates).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0");

    /// <summary>The repository this copy gets new versions from.</summary>
    public static string Repo => UpdateInstall.Marker?.Repo is { } r && IsRepo(r) ? r : DefaultRepo;

    /// <summary>"owner/name", nothing else (it goes into a web address).</summary>
    public static bool IsRepo(string? s) => s is not null && RepoPattern().IsMatch(s) && !s.EndsWith("/.") && !s.EndsWith("/..");

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9-]{0,38}/[A-Za-z0-9_.-]{1,100}$")]
    private static partial Regex RepoPattern();

    /// <summary>A file attached to a release: its name, the download page link, the API link (works for private repositories too) and its size.</summary>
    public sealed record Asset(string Name, string Url, string ApiUrl, long Size);

    /// <summary>A release: its version, page, notes, and the files to download.</summary>
    public sealed record Release(string Version, string PageUrl, string Notes, IReadOnlyList<Asset> Files)
    {
        /// <summary>The program download for a build name (win-x64, osx-arm64…), if there's one.</summary>
        public Asset? For(string platform) => Files.FirstOrDefault(f =>
            f.Name.Contains(platform, StringComparison.OrdinalIgnoreCase) &&
            (f.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || f.Name.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase)));

        /// <summary>The download for this computer, if there's one.</summary>
        public Asset? ForThisComputer => For(Platform);

        public Asset? Named(string name) => Files.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));

        /// <summary>An add-on's .pcaddon in its release: the one whose name starts with its id, or the only one.</summary>
        public Asset? AddonFile(string id)
        {
            var files = Files.Where(f => f.Name.EndsWith(".pcaddon", StringComparison.OrdinalIgnoreCase)).ToList();
            return files.FirstOrDefault(f => f.Name.StartsWith(id, StringComparison.OrdinalIgnoreCase)) ?? (files.Count == 1 ? files[0] : null);
        }
    }

    /// <summary>This computer's build name: win-x64, osx-arm64, linux-x64… (what the program was published as, when it says).</summary>
    public static string Platform => UpdateInstall.Marker?.Platform is { Length: > 0 } p ? p : ComputerPlatform;

    static string ComputerPlatform =>
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
            if (r.ValueKind != JsonValueKind.Object) return null;
            if (!r.TryGetProperty("tag_name", out var tag) || tag.GetString() is not { Length: > 0 } t) return null;
            if (r.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True) return null;
            var files = new List<Asset>();
            if (r.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
                foreach (var a in assets.EnumerateArray())
                    if (a.TryGetProperty("name", out var n) && n.GetString() is { Length: > 0 } name)
                        files.Add(new Asset(name,
                            a.TryGetProperty("browser_download_url", out var u) ? u.GetString() ?? "" : "",
                            a.TryGetProperty("url", out var api) ? api.GetString() ?? "" : "",
                            a.TryGetProperty("size", out var size) && size.TryGetInt64(out long s) ? s : 0));
            return new Release(Clean(t), r.TryGetProperty("html_url", out var page) ? page.GetString() ?? "" : "",
                r.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "", files);
        }
        catch (JsonException) { return null; }
        catch (InvalidOperationException) { return null; }
    }

    /// <summary>How asking for the latest release went.</summary>
    public enum Outcome { Found, NoneOrPrivate, KeyRefused, Offline }

    /// <summary>The answer to <see cref="LatestAsync"/>: the release (when found) and how it went.</summary>
    public sealed record Answer(Release? Release, Outcome Outcome);

    // Redirects are followed by hand so the access key only ever goes to api.github.com, never to
    // the storage server GitHub sends downloads on to.
    static readonly HttpClient Http = new(new SocketsHttpHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };

    /// <summary>Ask GitHub for the latest release of <paramref name="repo"/> (default: this program's). Quiet about being offline: that's normal.</summary>
    public static async Task<Answer> LatestAsync(string? repo = null, string? token = null, CancellationToken ct = default)
    {
        repo ??= Repo;
        if (!IsRepo(repo)) return new(null, Outcome.NoneOrPrivate);
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(15));
            using var res = await SendAsync($"https://api.github.com/repos/{repo}/releases/latest", "application/vnd.github+json", token, cts.Token).ConfigureAwait(false);
            if (res.StatusCode is HttpStatusCode.Unauthorized) return new(null, Outcome.KeyRefused);
            if (!res.IsSuccessStatusCode) return new(null, Outcome.NoneOrPrivate);
            var r = Parse(await res.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false));
            return new(r, r is null ? Outcome.NoneOrPrivate : Outcome.Found);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or OperationCanceledException or IOException)
        {
            ct.ThrowIfCancellationRequested();
            return new(null, Outcome.Offline);
        }
    }

    /// <summary>Download a release file to <paramref name="dest"/>, saying how far it's got (0–1). Throws when it can't.</summary>
    public static async Task DownloadAsync(Asset asset, string dest, string? token, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        string url = asset.ApiUrl.Length > 0 ? asset.ApiUrl : asset.Url;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromMinutes(30));
        using var res = await SendAsync(url, "application/octet-stream", token, cts.Token).ConfigureAwait(false);
        if (!res.IsSuccessStatusCode) throw new IOException($"GitHub answered {(int)res.StatusCode} {res.ReasonPhrase} for {asset.Name}.");
        long total = res.Content.Headers.ContentLength ?? asset.Size;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(dest))!);
        var part = dest + ".part";
        await using (var from = await res.Content.ReadAsStreamAsync(cts.Token).ConfigureAwait(false))
        await using (var to = File.Create(part))
        {
            var buf = new byte[81920];
            long got = 0;
            int n;
            while ((n = await from.ReadAsync(buf, cts.Token).ConfigureAwait(false)) > 0)
            {
                await to.WriteAsync(buf.AsMemory(0, n), cts.Token).ConfigureAwait(false);
                got += n;
                if (total > 0) progress?.Report(Math.Min(1, (double)got / total));
            }
        }
        File.Move(part, dest, overwrite: true);
    }

    /// <summary>Download a small text file from a release (SHA256SUMS).</summary>
    public static async Task<string> DownloadTextAsync(Asset asset, string? token, CancellationToken ct = default)
    {
        string url = asset.ApiUrl.Length > 0 ? asset.ApiUrl : asset.Url;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(30));
        using var res = await SendAsync(url, "application/octet-stream", token, cts.Token).ConfigureAwait(false);
        if (!res.IsSuccessStatusCode) throw new IOException($"GitHub answered {(int)res.StatusCode} {res.ReasonPhrase} for {asset.Name}.");
        return await res.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
    }

    static async Task<HttpResponseMessage> SendAsync(string url, string accept, string? token, CancellationToken ct)
    {
        var uri = new Uri(url);
        for (int hop = 0; ; hop++)
        {
            if (uri.Scheme != Uri.UriSchemeHttps) throw new IOException("Only https downloads are allowed.");
            using var req = new HttpRequestMessage(HttpMethod.Get, uri);
            req.Headers.UserAgent.ParseAdd($"{AppIdentity.ProductName}/{Current}");
            req.Headers.Accept.ParseAdd(accept);
            if (!string.IsNullOrWhiteSpace(token) && SendsKeyTo(uri)) req.Headers.Authorization = new("Bearer", token.Trim());
            var res = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if ((int)res.StatusCode is >= 300 and < 400 && res.Headers.Location is { } next && hop < 5)
            {
                res.Dispose();
                uri = next.IsAbsoluteUri ? next : new Uri(uri, next);
                continue;
            }
            return res;
        }
    }

    /// <summary>Reads byte ranges (first to last, inclusive) of a file.</summary>
    public delegate Task<byte[]> RangeReader(long first, long last, CancellationToken ct);

    /// <summary>
    /// A way to read parts of a release file without downloading all of it, and its length; null
    /// when the server won't send parts. GitHub sends downloads on to a storage server with a
    /// short-lived link: that link is found once and used for every part (the key never goes there).
    /// </summary>
    public static async Task<(RangeReader Read, long Length)?> RangesAsync(Asset asset, string? token, CancellationToken ct = default)
    {
        var uri = new Uri(asset.ApiUrl.Length > 0 ? asset.ApiUrl : asset.Url);
        long length = -1;
        using (var cts = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            cts.CancelAfter(TimeSpan.FromSeconds(30));
            for (int hop = 0; ; hop++)
            {
                using var res = await RangeRequest(uri, 0, 0, token, cts.Token).ConfigureAwait(false);
                if ((int)res.StatusCode is >= 300 and < 400 && res.Headers.Location is { } next && hop < 5)
                {
                    uri = next.IsAbsoluteUri ? next : new Uri(uri, next);
                    continue;
                }
                if (res.StatusCode != HttpStatusCode.PartialContent || res.Content.Headers.ContentRange?.Length is not { } total) return null;
                length = total;
                break;
            }
        }
        var final = uri;
        async Task<byte[]> Read(long first, long last, CancellationToken c)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(c);
            cts.CancelAfter(TimeSpan.FromMinutes(5));
            using var res = await RangeRequest(final, first, last, token, cts.Token).ConfigureAwait(false);
            if (res.StatusCode != HttpStatusCode.PartialContent || res.Content.Headers.ContentRange?.From != first)
                throw new IOException($"The download server didn't send the part asked for ({(int)res.StatusCode}).");
            var bytes = await res.Content.ReadAsByteArrayAsync(cts.Token).ConfigureAwait(false);
            if (bytes.Length != last - first + 1) throw new IOException("The download server sent a part of the wrong size.");
            return bytes;
        }
        return (Read, length);
    }

    static Task<HttpResponseMessage> RangeRequest(Uri uri, long first, long last, string? token, CancellationToken ct)
    {
        if (uri.Scheme != Uri.UriSchemeHttps) throw new IOException("Only https downloads are allowed.");
        var req = new HttpRequestMessage(HttpMethod.Get, uri);
        req.Headers.UserAgent.ParseAdd($"{AppIdentity.ProductName}/{Current}");
        req.Headers.Accept.ParseAdd("application/octet-stream");
        req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(first, last);
        if (!string.IsNullOrWhiteSpace(token) && SendsKeyTo(uri)) req.Headers.Authorization = new("Bearer", token.Trim());
        return Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
    }

    /// <summary>The access key only ever goes to GitHub's API.</summary>
    public static bool SendsKeyTo(Uri uri) => uri.Scheme == Uri.UriSchemeHttps && string.Equals(uri.Host, "api.github.com", StringComparison.OrdinalIgnoreCase);

    /// <summary>SHA256SUMS ("hash  name" or "hash *name" lines) → name → lower-case hash.</summary>
    public static Dictionary<string, string> ParseSums(string text)
    {
        var sums = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            int space = line.IndexOf(' ');
            if (space != 64) continue;
            var hash = line[..64];
            if (!hash.All(char.IsAsciiHexDigit)) continue;
            var name = line[(space + 1)..].TrimStart(' ', '*');
            name = name.Replace('\\', '/');
            if (name.Contains('/')) name = name[(name.LastIndexOf('/') + 1)..];
            if (name.Length > 0) sums[name] = hash.ToLowerInvariant();
        }
        return sums;
    }

    /// <summary>A file's SHA-256, lower-case hex.</summary>
    public static string Sha256(string file)
    {
        using var s = File.OpenRead(file);
        return Convert.ToHexStringLower(SHA256.HashData(s));
    }
}
