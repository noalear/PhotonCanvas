using System.Text.Json;

namespace PhotonCanvas.Remote;

/// <summary>Where the server is, its access code, and (when known) its certificate for the encrypted connection.</summary>
/// <param name="Address">The computer's address: an IP or a name, without http:// or the port.</param>
/// <param name="Port">The plain HTTP port (8740): the phone page, the app download, and servers without HTTPS.</param>
/// <param name="TlsPort">The HTTPS port, when the server has one (8741).</param>
/// <param name="Fingerprint">SHA-256 of the server's certificate (base64url): the app talks to that certificate only.</param>
/// <param name="Others">The same computer's other addresses (home network, Tailscale): tried when <paramref name="Address"/> doesn't answer.</param>
public sealed record ConnectionInfo(string Address, int Port, string Code, int? TlsPort = null, string? Fingerprint = null, string[]? Others = null)
{
    /// <summary>Every address to try, the last one that worked first.</summary>
    public IEnumerable<string> AllAddresses => new[] { Address }.Concat(Others ?? []).Distinct();

    /// <summary>This address from now on, the rest kept as others.</summary>
    public ConnectionInfo Using(string address, IEnumerable<string>? known = null) =>
        this with { Address = address, Others = [.. (known ?? AllAddresses).Where(a => a != address).Distinct()] };

    /// <summary>Same computer and settings (whatever address it was reached at).</summary>
    public bool SameAs(ConnectionInfo? other) => other is not null && this with { Others = null } == other with { Others = null } && (Others ?? []).SequenceEqual(other.Others ?? []);
    public const int DefaultPort = 8740;

    /// <summary>Encrypted, to that one certificate.</summary>
    public bool Secure => TlsPort is not null && !string.IsNullOrEmpty(Fingerprint);

    string Host => Address.Contains(':') ? $"[{Address}]" : Address;

    /// <summary>Where the API is: HTTPS when the certificate is known, else plain HTTP.</summary>
    public Uri BaseUri => Secure ? new($"https://{Host}:{TlsPort}/") : PlainUri;

    public Uri PlainUri => new($"http://{Host}:{Port}/");

    /// <summary>How it's shown: 192.168.1.50 (or with the port when it isn't 8740).</summary>
    public string Display => Port == DefaultPort ? Address : $"{Address}:{Port}";

    /// <summary>
    /// Read what the QR code holds (http://192.168.1.50:8740/#code=XXXX-XXXX-XXXX-XXXX&amp;tls=8741&amp;fp=…), or an
    /// address the user typed ("192.168.1.50", "192.168.1.50:8740", "http://pc.local:8740/") with the code
    /// given apart. Null when it isn't one.
    /// </summary>
    public static ConnectionInfo? TryParse(string? text, string? code = null)
    {
        text = text?.Trim();
        if (string.IsNullOrEmpty(text)) return null;
        if (!text.Contains("://")) text = "http://" + text;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || string.IsNullOrEmpty(uri.Host)) return null;
        string? fromQr = null, fp = null;
        int? tls = null;
        string[]? others = null;
        foreach (var part in uri.Fragment.TrimStart('#').Split('&'))
        {
            var (key, value) = part.IndexOf('=') is > 0 and var i ? (part[..i].ToLowerInvariant(), Uri.UnescapeDataString(part[(i + 1)..])) : ("", "");
            if (key == "code") fromQr = value;
            else if (key == "fp" && value.Length >= 40) fp = value;
            else if (key == "tls" && int.TryParse(value, out var t) && t is > 0 and < 65536) tls = t;
            else if (key == "alt") others = [.. value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(a => Uri.CheckHostName(a) != UriHostNameType.Unknown)];
        }
        string c = NormalizeCode(code ?? fromQr);
        if (c.Length == 0) return null;
        int port = uri.IsDefaultPort && !text.Contains($":{uri.Port}") ? DefaultPort : uri.Port;
        string host = uri.Host.Trim('[', ']');
        return new ConnectionInfo(host, port, Format(c), fp is null ? null : tls, tls is null ? null : fp, others is { Length: > 0 } ? [.. others.Where(a => a != host)] : null);
    }

    /// <summary>Tailscale's addresses: 100.64.0.0/10.</summary>
    public static bool IsTailscale(string address) =>
        System.Net.IPAddress.TryParse(address, out var ip) && ip.GetAddressBytes() is [100, var b, _, _] && (b & 0xC0) == 64;

    /// <summary>Letters and digits only, upper case.</summary>
    public static string NormalizeCode(string? code) => new([.. (code ?? "").Where(char.IsAsciiLetterOrDigit).Select(char.ToUpperInvariant)]);

    /// <summary>In groups of four: XXXX-XXXX-XXXX-XXXX.</summary>
    static string Format(string normalized) => string.Join('-', normalized.Chunk(4).Select(c => new string(c)));
}

/// <summary>The computer this phone uses, in the app's own folder.</summary>
public sealed class ConnectionStore(string folder)
{
    string FilePath => Path.Combine(folder, "connection.json");

    public ConnectionInfo? Load()
    {
        try { return File.Exists(FilePath) ? JsonSerializer.Deserialize(File.ReadAllText(FilePath), RemoteJson.Default.ConnectionInfo) : null; }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return null; }
    }

    public void Save(ConnectionInfo c)
    {
        Directory.CreateDirectory(folder);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(c, RemoteJson.Default.ConnectionInfo));
    }

    public void Forget()
    {
        try { File.Delete(FilePath); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}

/// <summary>When the phone keeps an eye on the machine with the app closed.</summary>
public enum WatchMode
{
    /// <summary>Only while the app is open.</summary>
    Off,
    /// <summary>While a job is loaded, running or paused (the default): progress, the end, alarms.</summary>
    DuringJobs,
    /// <summary>Always while the computer can be reached: also catches jobs started at the machine or the desktop app.</summary>
    Always,
}

/// <summary>The phone app's own settings (settings.json in its folder), read by the screens and the background watcher.</summary>
public sealed record PhoneSettings(WatchMode Watch = WatchMode.DuringJobs, bool AskedForNotifications = false)
{
    static string FilePath(string folder) => Path.Combine(folder, "settings.json");

    public static PhoneSettings Load(string folder)
    {
        try { return File.Exists(FilePath(folder)) ? JsonSerializer.Deserialize(File.ReadAllText(FilePath(folder)), RemoteJson.Default.PhoneSettings) ?? new() : new(); }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }

    public void Save(string folder)
    {
        Directory.CreateDirectory(folder);
        File.WriteAllText(FilePath(folder), JsonSerializer.Serialize(this, RemoteJson.Default.PhoneSettings));
    }
}

/// <summary>Times shown for a job, in words.</summary>
public static class JobTimes
{
    /// <summary>"done about 4:15 AM" (or tomorrow's day name when it ends after midnight).</summary>
    public static string? FinishText(JobStatus? job, DateTimeOffset now)
    {
        if (job is not { Stage: "running" or "paused", RemainingS: { } left } || !double.IsFinite(left)) return null;
        var end = now.AddSeconds(left).ToLocalTime();
        var day = end.Date == now.ToLocalTime().Date ? "" : $"{end:ddd} ";
        return $"done about {day}{end:t}";
    }
}
