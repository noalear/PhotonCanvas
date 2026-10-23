using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PhotonCanvas.Core;

namespace PhotonCanvas.Doc;

/// <summary>
/// The remote server's settings, in the data folder's remote\remote.json (never app.json, which the
/// desktop app owns). Made on the first start with a new access code. The desktop app's Remote
/// access window changes them; the server reads them when it starts.
/// </summary>
public sealed class RemoteConfig
{
    public const int DefaultPort = 8740;

    /// <summary>What the phone must send with every request (16 letters and digits, shown in groups of four).</summary>
    public string AccessCode { get; set; } = "";
    public int Port { get; set; } = DefaultPort;
    /// <summary>The machine's address; null = the one the desktop app last connected to.</summary>
    public string? MachineIp { get; set; }
    /// <summary>Folders whose designs the phone lists (besides the desktop app's recent files); empty = Documents.</summary>
    public List<string> ProjectFolders { get; set; } = [];

    public static string Dir => Path.Combine(AppIdentity.DataDir, "remote");
    static string FilePath => Path.Combine(Dir, "remote.json");
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    /// <summary>The saved settings, or new ones (with a new access code, saved at once).</summary>
    public static RemoteConfig LoadOrCreate()
    {
        RemoteConfig? c = null;
        try
        {
            if (File.Exists(FilePath)) c = JsonSerializer.Deserialize<RemoteConfig>(File.ReadAllText(FilePath), Json);
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            // Unreadable: start again with new settings (and a new code).
        }
        c ??= new RemoteConfig();
        if (NormalizeCode(c.AccessCode).Length < 12)
        {
            c.AccessCode = NewAccessCode();
            c.Save();
        }
        if (c.Port is < 1024 or > 65535) c.Port = DefaultPort;
        return c;
    }

    public void Save()
    {
        Directory.CreateDirectory(Dir);
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
        File.Move(tmp, FilePath, overwrite: true);
    }

    // No 0/O or 1/I: the code may be typed in by hand.
    const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    /// <summary>A new random code: 16 characters from 32 (80 bits), as XXXX-XXXX-XXXX-XXXX.</summary>
    public static string NewAccessCode()
    {
        var chars = new char[16];
        for (int i = 0; i < chars.Length; i++) chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        var s = new string(chars);
        return $"{s[..4]}-{s[4..8]}-{s[8..12]}-{s[12..]}";
    }

    /// <summary>The code without dashes or spaces, upper case: how it's compared.</summary>
    public static string NormalizeCode(string? code) =>
        new([.. (code ?? "").Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant)]);

    /// <summary>Does <paramref name="given"/> match the access code? (Time taken doesn't depend on where they differ.)</summary>
    public bool CodeMatches(string? given)
    {
        var a = Encoding.UTF8.GetBytes(NormalizeCode(given));
        var b = Encoding.UTF8.GetBytes(NormalizeCode(AccessCode));
        return a.Length > 0 && CryptographicOperations.FixedTimeEquals(a, b);
    }

    /// <summary>Where the server offers the Android app for download (no access code needed: it's only the app).</summary>
    public const string AndroidAppPath = "/app/PhotonCanvasRemote.apk";

    /// <summary>
    /// The Android app to offer phones: app\PhotonCanvasRemote.apk beside the server (a release puts it
    /// there) or, running from source, the newest build of src/PhotonCanvas.Remote.Android. Null if none.
    /// </summary>
    public static string? FindAndroidApp(string? startDir = null)
    {
        var dir = startDir ?? AppContext.BaseDirectory;
        var beside = Path.Combine(dir, "app", "PhotonCanvasRemote.apk");
        if (File.Exists(beside)) return beside;
        try
        {
            for (var d = new DirectoryInfo(dir); d is not null; d = d.Parent)
            {
                var bin = Path.Combine(d.FullName, "src", "PhotonCanvas.Remote.Android", "bin");
                if (Directory.Exists(bin))
                    return Directory.EnumerateFiles(bin, "*-Signed.apk", SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return null;
    }

    /// <summary>This computer's addresses a phone can reach: home network ones, and Tailscale's (100.64.0.0/10) marked.</summary>
    public static List<(string Address, bool Tailscale)> Addresses()
    {
        var o = new List<(string, bool)>();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                foreach (var a in nic.GetIPProperties().UnicastAddresses)
                {
                    if (a.Address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(a.Address)) continue;
                    var b = a.Address.GetAddressBytes();
                    if (b[0] == 169 && b[1] == 254) continue; // no network (self-assigned)
                    o.Add((a.Address.ToString(), b[0] == 100 && (b[1] & 0xC0) == 64));
                }
            }
        }
        catch (NetworkInformationException) { }
        return [.. o.Distinct().OrderBy(x => x.Item2)];
    }
}
