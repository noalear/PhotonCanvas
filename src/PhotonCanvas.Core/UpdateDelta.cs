using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace PhotonCanvas.Core;

/// <summary>
/// Updating with only the files that changed. Each release carries, beside its .zip, a list of
/// every file in it with its SHA-256 (<c>PhotonCanvas-win-x64.files.txt</c>, itself listed in
/// SHA256SUMS). The program compares that list with the files it has, then reads just the changed
/// files out of the release's zip with ranged downloads (a zip's table of contents at its end says
/// where each file is), checks each against the list, and stages them like a full download. Files it
/// already has stay where they are. Anything unexpected (no list, a server that won't send parts,
/// a zip it can't read) and the caller falls back to downloading the whole zip.
/// </summary>
public static class UpdateDelta
{
    /// <summary>The file list published beside a release download: "PhotonCanvas-win-x64.zip" → "PhotonCanvas-win-x64.files.txt".</summary>
    public static string ListName(string archiveName)
    {
        foreach (var ext in new[] { ".tar.gz", ".zip" })
            if (archiveName.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) return archiveName[..^ext.Length] + ".files.txt";
        return archiveName + ".files.txt";
    }

    /// <summary>A file of the new version: its path inside the version's folder ('/' between folders) and its SHA-256.</summary>
    public sealed record Entry(string Path, string Sha256);

    /// <summary>The list as sha256sum writes it ("hash  ./path"), paths relative to the version's folder.</summary>
    public static List<Entry> ParseList(string text)
    {
        var list = new List<Entry>();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length < 67 || line[64] != ' ') continue;
            var hash = line[..64];
            if (!hash.All(char.IsAsciiHexDigit)) continue;
            var path = line[65..].TrimStart(' ', '*').Replace('\\', '/');
            if (path.StartsWith("./")) path = path[2..];
            if (path.Length == 0 || path.StartsWith('/') || path.Split('/').Any(p => p is "" or "." or "..")) continue;
            list.Add(new Entry(path, hash.ToLowerInvariant()));
        }
        return list;
    }

    /// <summary>
    /// The files that differ from what's in <paramref name="installRoot"/> (missing, or a different
    /// SHA-256), plus update.json and update-files.txt, which a staged version always carries.
    /// </summary>
    public static List<Entry> Changed(IEnumerable<Entry> list, string installRoot)
    {
        var changed = new List<Entry>();
        foreach (var e in list)
        {
            var name = System.IO.Path.GetFileName(e.Path);
            bool always = name is UpdateInstall.MarkerFile or UpdateInstall.ListFile;
            var local = System.IO.Path.Combine(installRoot, e.Path.Replace('/', System.IO.Path.DirectorySeparatorChar));
            if (always || !File.Exists(local) || Updates.Sha256(local) != e.Sha256) changed.Add(e);
        }
        return changed;
    }

    /// <summary>A file in a zip, from its table of contents.</summary>
    public sealed record ZipEntry(string Name, int Method, long CompressedSize, long LocalHeader);

    /// <summary>
    /// Read the zip's table of contents (central directory) with ranged reads: its end record is in
    /// the last 64 KB. Zip64 archives (over 4 GB, or 65535 files) aren't handled: callers fall back.
    /// </summary>
    public static async Task<Dictionary<string, ZipEntry>> ReadDirectory(Updates.RangeReader read, long length, CancellationToken ct = default)
    {
        long tailStart = Math.Max(0, length - 66000);
        var tail = await read(tailStart, length - 1, ct).ConfigureAwait(false);
        int eocd = -1;
        for (int i = tail.Length - 22; i >= 0; i--)
            if (BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(i)) == 0x06054b50) { eocd = i; break; }
        if (eocd < 0) throw new InvalidDataException("The download isn't a zip (no end record).");
        uint count = BinaryPrimitives.ReadUInt16LittleEndian(tail.AsSpan(eocd + 10));
        long dirSize = BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(eocd + 12));
        long dirStart = BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(eocd + 16));
        if (count == 0xFFFF || dirSize == 0xFFFFFFFF || dirStart == 0xFFFFFFFF) throw new InvalidDataException("A zip64 download: read whole.");
        if (dirStart + dirSize > length) throw new InvalidDataException("The zip's table of contents points outside it.");
        var dir = dirStart >= tailStart ? tail.AsSpan((int)(dirStart - tailStart), (int)dirSize).ToArray() : await read(dirStart, dirStart + dirSize - 1, ct).ConfigureAwait(false);

        var entries = new Dictionary<string, ZipEntry>(StringComparer.Ordinal);
        int p = 0;
        while (p + 46 <= dir.Length && BinaryPrimitives.ReadUInt32LittleEndian(dir.AsSpan(p)) == 0x02014b50)
        {
            var s = dir.AsSpan(p);
            int method = BinaryPrimitives.ReadUInt16LittleEndian(s[10..]);
            long comp = BinaryPrimitives.ReadUInt32LittleEndian(s[20..]);
            int nameLen = BinaryPrimitives.ReadUInt16LittleEndian(s[28..]);
            int extraLen = BinaryPrimitives.ReadUInt16LittleEndian(s[30..]);
            int commentLen = BinaryPrimitives.ReadUInt16LittleEndian(s[32..]);
            long local = BinaryPrimitives.ReadUInt32LittleEndian(s[42..]);
            if (p + 46 + nameLen > dir.Length) break;
            var name = Encoding.UTF8.GetString(s.Slice(46, nameLen)).Replace('\\', '/');
            if (comp == 0xFFFFFFFF || local == 0xFFFFFFFF) throw new InvalidDataException("A zip64 download: read whole.");
            if (!name.EndsWith('/')) entries[name] = new ZipEntry(name, method, comp, local);
            p += 46 + nameLen + extraLen + commentLen;
        }
        if (entries.Count == 0) throw new InvalidDataException("The zip's table of contents is empty.");
        return entries;
    }

    /// <summary>The folder at the top of the zip that the version's files are in ("PhotonCanvas/"), or "" when they're at the top.</summary>
    public static string TopFolder(IReadOnlyDictionary<string, ZipEntry> entries)
    {
        var firsts = entries.Keys.Select(n => n.Contains('/') ? n[..(n.IndexOf('/') + 1)] : "").Distinct().ToList();
        return firsts.Count == 1 ? firsts[0] : "";
    }

    /// <summary>A planned download: which files, in which ranges of the zip, and how many bytes in all.</summary>
    public sealed record Plan(List<(Entry File, ZipEntry Zip)> Files, List<(long First, long Last)> Ranges, long Bytes);

    /// <summary>Group the changed files' places in the zip into few ranges (neighbours closer than <paramref name="joinGap"/> are read together).</summary>
    public static Plan MakePlan(IEnumerable<Entry> changed, IReadOnlyDictionary<string, ZipEntry> entries, long length, long joinGap = 256 * 1024)
    {
        var top = TopFolder(entries);
        var files = new List<(Entry, ZipEntry)>();
        foreach (var e in changed)
        {
            if (!entries.TryGetValue(top + e.Path, out var z)) throw new InvalidDataException($"{e.Path} is in the list but not in the zip.");
            files.Add((e, z));
        }
        files.Sort((a, b) => a.Item2.LocalHeader.CompareTo(b.Item2.LocalHeader));
        var ranges = new List<(long First, long Last)>();
        foreach (var (_, z) in files)
        {
            // The local header repeats the name and may carry its own extra field: allow for it.
            long first = z.LocalHeader, last = Math.Min(length - 1, z.LocalHeader + 30 + Encoding.UTF8.GetByteCount(z.Name) + 1024 + z.CompressedSize - 1);
            if (ranges.Count > 0 && first <= ranges[^1].Last + joinGap) ranges[^1] = (ranges[^1].First, Math.Max(ranges[^1].Last, last));
            else ranges.Add((first, last));
        }
        return new Plan(files, ranges, ranges.Sum(r => r.Last - r.First + 1));
    }

    /// <summary>
    /// Download the planned files into <paramref name="stagingRoot"/> (at their paths in the version),
    /// each checked against its SHA-256. Reports progress 0–1 by bytes.
    /// </summary>
    public static async Task Fetch(Plan plan, Updates.RangeReader read, string stagingRoot, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        long done = 0;
        foreach (var (first, last) in plan.Ranges)
        {
            var bytes = await read(first, last, ct).ConfigureAwait(false);
            foreach (var (file, z) in plan.Files.Where(f => f.Zip.LocalHeader >= first && f.Zip.LocalHeader <= last))
            {
                int at = (int)(z.LocalHeader - first);
                if (at + 30 > bytes.Length || BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at)) != 0x04034b50)
                    throw new InvalidDataException($"{file.Path}: no file header where the zip says.");
                int nameLen = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(at + 26));
                int extraLen = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(at + 28));
                long dataAt = at + 30 + nameLen + extraLen;
                byte[] data;
                if (dataAt + z.CompressedSize <= bytes.Length) data = bytes.AsSpan((int)dataAt, (int)z.CompressedSize).ToArray();
                else
                {
                    long start = first + dataAt;
                    data = z.CompressedSize == 0 ? [] : await read(start, start + z.CompressedSize - 1, ct).ConfigureAwait(false);
                }
                var target = System.IO.Path.Combine(stagingRoot, file.Path.Replace('/', System.IO.Path.DirectorySeparatorChar));
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!);
                await using (var output = File.Create(target))
                {
                    if (z.Method == 0) await output.WriteAsync(data, ct).ConfigureAwait(false);
                    else if (z.Method == 8)
                    {
                        await using var inflate = new DeflateStream(new MemoryStream(data), CompressionMode.Decompress);
                        await inflate.CopyToAsync(output, ct).ConfigureAwait(false);
                    }
                    else throw new InvalidDataException($"{file.Path}: compressed in a way that isn't handled ({z.Method}).");
                }
                using (var check = File.OpenRead(target))
                    if (Convert.ToHexStringLower(SHA256.HashData(check)) != file.Sha256)
                        throw new InvalidDataException($"{file.Path} doesn't match its checksum.");
            }
            done += last - first + 1;
            progress?.Report(plan.Bytes == 0 ? 1 : (double)done / plan.Bytes);
        }
    }

    /// <summary>A reader over a local file (tests, and anything already on disk).</summary>
    public static Updates.RangeReader FileReader(string path) => async (first, last, ct) =>
    {
        await using var f = File.OpenRead(path);
        f.Seek(first, SeekOrigin.Begin);
        var buf = new byte[last - first + 1];
        await f.ReadExactlyAsync(buf, ct).ConfigureAwait(false);
        return buf;
    };
}
