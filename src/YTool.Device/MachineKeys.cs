using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using YTool.Core;

namespace YTool.Device;

/// <summary>
/// The three fixed strings xTool machines use to be found on the network and
/// to pair with a computer. They are not part of this program: the user
/// supplies them from their own copy of xTool Studio (see <see cref="KeyFinder"/>).
/// Only one-way fingerprints are kept here, to confirm that what the user
/// supplied is correct.
/// </summary>
/// <param name="DiscoveryRequest">Encrypts the "find machines" broadcast (32 characters).</param>
/// <param name="DiscoveryReply">Decrypts the machines' replies (32 characters).</param>
/// <param name="Pairing">Sent when pairing (20 characters).</param>
public sealed record MachineKeys(string DiscoveryRequest, string DiscoveryReply, string Pairing)
{
    public static readonly MachineKeys Empty = new("", "", "");

    /// <summary>SHA-256 of the correct values, hex.</summary>
    public static class Fingerprints
    {
        public const string DiscoveryRequest = "5aa7eff61978c77abc4c6d9b39c6d5837a1c7d0ec777fba695fc81572f3b6ba3";
        public const string DiscoveryReply = "359d1cdd6dca256d1a84b146edb1acbcec7c3de6b7a787480e9c441b0b233b44";
        public const string Pairing = "e974517b5fb09711d261f5c2d9a33c2ad0560827cf5f16acd437421d7222b6f1";
    }

    public static string Fingerprint(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    public bool RequestValid => Fingerprint(DiscoveryRequest) == Fingerprints.DiscoveryRequest;
    public bool ReplyValid => Fingerprint(DiscoveryReply) == Fingerprints.DiscoveryReply;
    public bool PairingValid => Fingerprint(Pairing) == Fingerprints.Pairing;

    /// <summary>All three present and correct.</summary>
    public bool IsComplete => RequestValid && ReplyValid && PairingValid;

    public static string DefaultPath =>
        Path.Combine(AppIdentity.DataDir, "keys.json");

    /// <summary>Keys saved on this computer, or <see cref="Empty"/>. Never throws.</summary>
    public static MachineKeys Load(string? path = null)
    {
        try
        {
            var p = path ?? DefaultPath;
            if (!File.Exists(p)) return Empty;
            var k = JsonSerializer.Deserialize<MachineKeys>(File.ReadAllText(p));
            return k is null ? Empty : new(k.DiscoveryRequest ?? "", k.DiscoveryReply ?? "", k.Pairing ?? "");
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return Empty;
        }
    }

    public void Save(string? path = null)
    {
        var p = path ?? DefaultPath;
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}

/// <summary>
/// Finds the machine keys in the user's own xTool Studio installation by
/// looking for strings whose fingerprint matches. Nothing is copied except
/// those three strings, and only onto this computer.
/// </summary>
public static class KeyFinder
{
    /// <summary>Usual places xTool Studio's main program file lives.</summary>
    public static IEnumerable<string> LikelyLocations()
    {
        string[] roots =
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        ];
        foreach (var root in roots.Where(r => !string.IsNullOrEmpty(r)))
            foreach (var name in new[] { "xTool Studio", "xToolStudio", "xTool Creative Space" })
            {
                var asar = Path.Combine(root, name, "resources", "app.asar");
                if (File.Exists(asar)) yield return asar;
            }
    }

    /// <summary>
    /// Scan a file (normally xTool Studio's <c>resources\app.asar</c>) for the
    /// keys. Returns what was found; missing ones are empty.
    /// </summary>
    public static MachineKeys Scan(string file, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        using var s = File.OpenRead(file);
        return Scan(s, Wanted, progress, ct);
    }

    static readonly (int Length, string Fingerprint)[] Wanted =
    [
        (32, MachineKeys.Fingerprints.DiscoveryRequest),
        (32, MachineKeys.Fingerprints.DiscoveryReply),
        (20, MachineKeys.Fingerprints.Pairing),
    ];

    /// <summary>
    /// Look for quoted strings of the wanted lengths whose fingerprint matches.
    /// Returns them in the order of <paramref name="wanted"/> (empty when not found).
    /// </summary>
    internal static MachineKeys Scan(Stream s, IReadOnlyList<(int Length, string Fingerprint)> wanted, IProgress<double>? progress, CancellationToken ct)
    {
        var found = new string[wanted.Count];
        Array.Fill(found, "");
        var lengths = wanted.Select(w => w.Length).ToHashSet();
        int maxLen = lengths.Max();
        const int Chunk = 4 << 20;
        var buf = new byte[Chunk + maxLen + 2];
        int carry = 0;
        long total = s.CanSeek ? s.Length : 0, done = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            int n = s.Read(buf, carry, Chunk);
            if (n <= 0) break;
            int len = carry + n;
            int i = 0;
            for (; i < len; i++)
            {
                byte q = buf[i];
                if (q is not ((byte)'"' or (byte)'\'' or (byte)'`')) continue;
                int j = i + 1;
                while (j < len && j - i - 1 <= maxLen && IsKeyChar(buf[j])) j++;
                if (j >= len) break; // token may continue in the next chunk
                int tokenLen = j - i - 1;
                if (buf[j] == q && lengths.Contains(tokenLen))
                {
                    var token = Encoding.ASCII.GetString(buf, i + 1, tokenLen);
                    var fp = MachineKeys.Fingerprint(token);
                    for (int k = 0; k < wanted.Count; k++)
                        if (found[k].Length == 0 && wanted[k].Length == tokenLen && wanted[k].Fingerprint == fp) found[k] = token;
                }
            }
            // Keep the unfinished tail for the next chunk.
            carry = Math.Min(len - i, maxLen + 2);
            Buffer.BlockCopy(buf, len - carry, buf, 0, carry);
            done += n;
            if (total > 0) progress?.Report((double)done / total);
            if (found.All(f => f.Length > 0)) break;
        }
        return new MachineKeys(found[0], found[1], found.Length > 2 ? found[2] : "");
    }

    static bool IsKeyChar(byte b) =>
        b is >= (byte)'a' and <= (byte)'z' or >= (byte)'A' and <= (byte)'Z' or >= (byte)'0' and <= (byte)'9' or (byte)'-' or (byte)'+' or (byte)'/' or (byte)'=';
}
