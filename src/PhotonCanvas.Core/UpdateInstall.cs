using System.Formats.Tar;
using System.IO.Compression;
using System.Text.Json;

namespace PhotonCanvas.Core;

/// <summary>
/// Putting a downloaded version in place of the running one.
///
/// A published copy carries <see cref="MarkerFile"/> (where it came from) and <see cref="ListFile"/>
/// (the files it is made of) beside the program. A new version is downloaded and unpacked into the
/// data folder while the program runs (<see cref="Pending"/>), and swapped in when the program closes
/// (<see cref="Apply"/>): each old file is renamed to <c>*.pcold</c> (allowed on Windows even while it's
/// in use) and the new one moved in; if anything fails, everything is put back. The <c>*.pcold</c>
/// files are deleted the next time the program runs (<see cref="CleanUp"/>). Files the old version
/// had and the new one doesn't are moved aside the same way, so a dropped add-on doesn't linger.
/// A copy built from the source code has no marker and is never changed in place.
/// </summary>
public static class UpdateInstall
{
    public const string MarkerFile = "update.json";
    public const string ListFile = "update-files.txt";
    public const string OldSuffix = ".pcold";

    /// <summary>What <see cref="MarkerFile"/> says: the repository releases come from, and the build name (win-x64…).</summary>
    public sealed record MarkerInfo(string? Repo, string? Platform);

    static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
    };

    static readonly Lazy<MarkerInfo?> MarkerLazy = new(() => ReadMarker(AppContext.BaseDirectory));

    /// <summary>This copy's marker, or null when it was built from the source code.</summary>
    public static MarkerInfo? Marker => MarkerLazy.Value;

    public static MarkerInfo? ReadMarker(string programDir)
    {
        try
        {
            var f = Path.Combine(programDir, MarkerFile);
            return File.Exists(f) ? JsonSerializer.Deserialize<MarkerInfo>(File.ReadAllText(f), Json) : null;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>The folder a version is unpacked as: the program's folder, or on a Mac the PhotonCanvas.app bundle around it.</summary>
    public static string RootFor(string programDir)
    {
        var dir = Path.TrimEndingDirectorySeparator(Path.GetFullPath(programDir));
        var macOs = Path.GetFileName(dir);
        var contents = Path.GetDirectoryName(dir);
        if (macOs == "MacOS" && contents is not null && Path.GetFileName(contents) == "Contents" &&
            Path.GetDirectoryName(contents) is { } bundle && bundle.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
            return bundle;
        return dir;
    }

    /// <summary>This copy's install folder (see <see cref="RootFor"/>).</summary>
    public static string InstallRoot => RootFor(AppContext.BaseDirectory);

    /// <summary>A published copy whose folder can be written to: it can update itself in place.</summary>
    public static bool CanUpdateInPlace => Marker is not null && Writable(InstallRoot);

    static bool Writable(string dir)
    {
        try
        {
            var probe = Path.Combine(dir, $".write-test-{Environment.ProcessId}");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }

    // ---- downloaded, waiting to go in ------------------------------------------------------------

    /// <summary>Where new versions are downloaded and unpacked.</summary>
    public static string UpdatesDir => Path.Combine(AppIdentity.DataDir, "updates");

    /// <summary>A version unpacked in <paramref name="Folder"/>, waiting to go into <paramref name="InstallRoot"/> when the program closes.</summary>
    public sealed record PendingUpdate(string Version, string Folder, string InstallRoot);

    static string PendingFile => Path.Combine(UpdatesDir, "pending.json");

    public static PendingUpdate? Pending
    {
        get
        {
            try
            {
                if (!File.Exists(PendingFile)) return null;
                var p = JsonSerializer.Deserialize<PendingUpdate>(File.ReadAllText(PendingFile), Json);
                return p is not null && Directory.Exists(p.Folder) ? p : null;
            }
            catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return null; }
        }
    }

    /// <summary>The pending version, if it is for this copy and newer than it.</summary>
    public static PendingUpdate? PendingHere =>
        Pending is { } p && SamePath(p.InstallRoot, InstallRoot) && Updates.Newer(p.Version, Updates.Current) ? p : null;

    /// <summary>The pending version is for this copy but isn't newer than it (it went in some other way): it can go.</summary>
    public static bool PendingIsStale =>
        Pending is { } p && SamePath(p.InstallRoot, InstallRoot) && !Updates.Newer(p.Version, Updates.Current);

    public static void SetPending(PendingUpdate p)
    {
        Directory.CreateDirectory(UpdatesDir);
        File.WriteAllText(PendingFile, JsonSerializer.Serialize(p, Json));
    }

    /// <summary>Forget the pending version and delete everything downloaded.</summary>
    public static void ClearDownloads(string? keep = null)
    {
        try { if (keep is null) File.Delete(PendingFile); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        if (!Directory.Exists(UpdatesDir)) return;
        foreach (var d in Directory.GetDirectories(UpdatesDir))
        {
            if (keep is not null && SamePath(d, keep)) continue;
            try { Directory.Delete(d, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>
    /// Unpack a downloaded .zip or .tar.gz into <paramref name="dest"/> (emptied first) and return the
    /// version's own folder in it (the single folder at the top: PhotonCanvas, or PhotonCanvas.app).
    /// </summary>
    public static string Unpack(string archive, string dest)
    {
        if (Directory.Exists(dest)) Directory.Delete(dest, recursive: true);
        Directory.CreateDirectory(dest);
        if (archive.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) ZipFile.ExtractToDirectory(archive, dest);
        else
        {
            using var file = File.OpenRead(archive);
            using var gz = new GZipStream(file, CompressionMode.Decompress);
            TarFile.ExtractToDirectory(gz, dest, overwriteFiles: true);
        }
        var dirs = Directory.GetDirectories(dest);
        var files = Directory.GetFiles(dest);
        if (dirs.Length == 1 && files.Length == 0) return dirs[0];
        if (files.Length > 0 && File.Exists(Path.Combine(dest, MarkerFile))) return dest;
        throw new InvalidDataException("The download isn't laid out like a PhotonCanvas version (one folder at the top).");
    }

    /// <summary>The marker and file list inside a version's folder (a Mac bundle keeps them in Contents/MacOS).</summary>
    public static string ProgramDirIn(string root)
    {
        var mac = Path.Combine(root, "Contents", "MacOS");
        return Directory.Exists(mac) ? mac : root;
    }

    /// <summary>Every file in a version's folder, relative to it.</summary>
    public static List<string> FilesIn(string root) =>
        [.. Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(root, f))
            .Where(f => !f.EndsWith(OldSuffix, StringComparison.OrdinalIgnoreCase))];

    /// <summary>The files <see cref="ListFile"/> says a version installed in <paramref name="root"/> is made of (empty when it doesn't say).</summary>
    public static List<string> ListedIn(string root)
    {
        var f = Path.Combine(ProgramDirIn(root), ListFile);
        if (!File.Exists(f)) return [];
        var rootFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        var list = new List<string>();
        foreach (var raw in File.ReadAllLines(f))
        {
            var line = raw.Trim().Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            if (line.Length == 0 || line.StartsWith('#')) continue;
            if (line.StartsWith("." + Path.DirectorySeparatorChar)) line = line[2..];
            // Only ever inside the folder.
            var full = Path.GetFullPath(Path.Combine(root, line));
            if (!full.StartsWith(rootFull, PathComparison)) continue;
            list.Add(Path.GetRelativePath(root, full));
        }
        return list;
    }

    static StringComparison PathComparison => OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
    static StringComparer PathComparer => OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

    static bool SamePath(string a, string b) =>
        string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)), PathComparison);

    sealed class Step
    {
        public required string Target { get; init; }
        public string? Backup { get; set; }
        public bool MovedIn { get; set; }
    }

    /// <summary>
    /// Swap the version unpacked in <paramref name="newRoot"/> into <paramref name="installRoot"/>
    /// (its files are moved, so <paramref name="newRoot"/> is used up). All or nothing: on any failure
    /// what was done is undone and the exception is passed on. Returns how many files were put in.
    /// <paramref name="newRoot"/> may hold only the files that changed (see <see cref="UpdateDelta"/>)
    /// as long as it has the new version's update-files.txt: files that list names stay.
    /// </summary>
    public static int Apply(string newRoot, string installRoot)
    {
        var incoming = FilesIn(newRoot);
        if (incoming.Count == 0) throw new InvalidDataException("The new version has no files.");
        if (!incoming.Contains(Path.GetRelativePath(newRoot, Path.Combine(ProgramDirIn(newRoot), MarkerFile)), PathComparer))
            throw new InvalidDataException("The new version has no update.json, so it isn't a published PhotonCanvas.");
        // What the new version is made of: the files brought, plus (when only the changed files were
        // downloaded) the ones its list names that are already here and stay as they are.
        var incomingSet = incoming.Concat(ListedIn(newRoot)).ToHashSet(PathComparer);
        var dropped = ListedIn(installRoot).Where(f => !incomingSet.Contains(f)).ToList();

        var done = new List<Step>();
        try
        {
            foreach (var rel in incoming)
            {
                var target = Path.Combine(installRoot, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                var step = new Step { Target = target };
                done.Add(step);
                if (File.Exists(target)) step.Backup = MoveAside(target);
                File.Move(Path.Combine(newRoot, rel), target);
                step.MovedIn = true;
            }
            foreach (var rel in dropped)
            {
                var target = Path.Combine(installRoot, rel);
                if (!File.Exists(target)) continue;
                var step = new Step { Target = target };
                done.Add(step);
                step.Backup = MoveAside(target);
            }
            return incoming.Count;
        }
        catch
        {
            Undo(done);
            throw;
        }
    }

    static void Undo(List<Step> done)
    {
        for (int i = done.Count - 1; i >= 0; i--)
        {
            var s = done[i];
            try
            {
                if (s.MovedIn && File.Exists(s.Target)) File.Delete(s.Target);
                if (s.Backup is { } b && File.Exists(b)) File.Move(b, s.Target);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* best effort */ }
        }
    }

    /// <summary>Rename a file to name.pcold (or name.1.pcold… when an older one can't be deleted yet).</summary>
    static string MoveAside(string file)
    {
        for (int n = 0; ; n++)
        {
            var aside = n == 0 ? file + OldSuffix : $"{file}.{n}{OldSuffix}";
            if (File.Exists(aside))
            {
                try { File.Delete(aside); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { if (n < 20) continue; throw; }
            }
            File.Move(file, aside);
            return aside;
        }
    }

    /// <summary>Delete the files an earlier update moved aside (the ones still in use stay until next time). Returns how many went.</summary>
    public static int CleanUp(string installRoot)
    {
        if (!Directory.Exists(installRoot)) return 0;
        int n = 0;
        foreach (var f in Directory.EnumerateFiles(installRoot, "*" + OldSuffix, SearchOption.AllDirectories).ToList())
        {
            try { File.Delete(f); n++; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        return n;
    }

    /// <summary>
    /// Put a version into <paramref name="target"/>, a folder of its own (for a copy built from the
    /// source code, which never changes itself): a new or empty folder gets the files; one that
    /// already holds a published copy is updated like <see cref="Apply"/>. Anything else is refused.
    /// </summary>
    public static void InstallInto(string newRoot, string target)
    {
        if (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any())
        {
            if (ReadMarker(ProgramDirIn(target)) is null)
                throw new IOException($"{target} already has other files in it, so nothing was put there.");
            Apply(newRoot, target);
            return;
        }
        Directory.CreateDirectory(target);
        foreach (var rel in FilesIn(newRoot))
        {
            var to = Path.Combine(target, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            File.Move(Path.Combine(newRoot, rel), to);
        }
    }

    // ---- the GitHub access key -------------------------------------------------------------------

    static string TokenFile => Path.Combine(AppIdentity.DataDir, "github-key.txt");

    /// <summary>The GitHub access key for downloading from a private repository (kept in its own file in the data folder, not in the settings).</summary>
    public static string? ReadToken()
    {
        try { return File.Exists(TokenFile) && File.ReadAllText(TokenFile).Trim() is { Length: > 0 } t ? t : null; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    public static void SaveToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) { if (File.Exists(TokenFile)) File.Delete(TokenFile); return; }
        Directory.CreateDirectory(AppIdentity.DataDir);
        File.WriteAllText(TokenFile, token.Trim());
    }
}
