using System.Text.Json;

namespace PhotonCanvas.Remote;

/// <summary>Where the server is and its access code: from the desktop app's QR code, or typed in.</summary>
/// <param name="Address">The computer's address: an IP or a name, without http:// or the port.</param>
public sealed record ConnectionInfo(string Address, int Port, string Code)
{
    public const int DefaultPort = 8740;

    public Uri BaseUri => new($"http://{(Address.Contains(':') ? $"[{Address}]" : Address)}:{Port}/");

    /// <summary>How it's shown: 192.168.50.247:8740.</summary>
    public string Display => Port == DefaultPort ? Address : $"{Address}:{Port}";

    /// <summary>
    /// Read what the QR code holds (http://192.168.50.247:8740/#code=XXXX-XXXX-XXXX-XXXX), or an address the
    /// user typed ("192.168.50.247", "192.168.50.247:8740", "http://pc.local:8740/") with the code given apart.
    /// Null when it isn't one.
    /// </summary>
    public static ConnectionInfo? TryParse(string? text, string? code = null)
    {
        text = text?.Trim();
        if (string.IsNullOrEmpty(text)) return null;
        if (!text.Contains("://")) text = "http://" + text;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || string.IsNullOrEmpty(uri.Host)) return null;
        string? fromQr = null;
        foreach (var part in uri.Fragment.TrimStart('#').Split('&'))
            if (part.StartsWith("code=", StringComparison.OrdinalIgnoreCase)) fromQr = Uri.UnescapeDataString(part[5..]);
        string c = NormalizeCode(code ?? fromQr);
        if (c.Length == 0) return null;
        int port = uri.IsDefaultPort && !text.Contains($":{uri.Port}") ? DefaultPort : uri.Port;
        return new ConnectionInfo(uri.Host.Trim('[', ']'), port, Format(c));
    }

    /// <summary>Letters and digits only, upper case.</summary>
    public static string NormalizeCode(string? code) => new([.. (code ?? "").Where(char.IsAsciiLetterOrDigit).Select(char.ToUpperInvariant)]);

    /// <summary>In groups of four: XXXX-XXXX-XXXX-XXXX.</summary>
    static string Format(string normalized) => string.Join('-', normalized.Chunk(4).Select(c => new string(c)));
}

/// <summary>The servers this phone knows (the last one first), in the app's own folder.</summary>
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
