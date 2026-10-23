using System.Text.Json;
using PhotonCanvas.Doc;

namespace PhotonCanvas.Server;

/// <summary>
/// A job the server saw run, whoever started it.
/// </summary>
/// <param name="Source">"phone" (loaded from the phone), "computer" (sent from the desktop app), or "elsewhere" (xTool Studio, the machine's screen).</param>
/// <param name="StartedFromPhone">Started with the phone's Start rather than the machine's button.</param>
public sealed record RemoteJobRecord(
    string Id, string? TaskId, string Design, string Source,
    DateTime? LoadedAt, DateTime StartedAt, DateTime? EndedAt, bool Finished, bool StartedFromPhone,
    double? EstimateS, double? TookS, bool HasBefore, bool HasAfter);

/// <summary>A job this server uploaded, so it's recognised (and named) when the machine runs it.</summary>
public sealed record SentJobInfo(string TaskId, string Design, string Path, double EstimateS, int Layers, double WidthMm, double HeightMm, DateTime At);

/// <summary>
/// The server's history (remote\jobs.jsonl, newest last) with each job's pictures in
/// remote\jobs\&lt;id&gt;\before.jpg and after.jpg, and the jobs it uploaded (remote\sent.json).
/// </summary>
public sealed class JobLog
{
    const int KeepJobs = 300, KeepSent = 50;
    readonly Lock _gate = new();
    readonly List<RemoteJobRecord> _jobs;
    readonly List<SentJobInfo> _sent;
    readonly string _dir;

    public JobLog(string? dir = null)
    {
        _dir = dir ?? RemoteConfig.Dir;
        _jobs = ReadLines<RemoteJobRecord>(Path.Combine(_dir, "jobs.jsonl"));
        _sent = Read<List<SentJobInfo>>(Path.Combine(_dir, "sent.json")) ?? [];
    }

    public string PictureDir(string id) => Path.Combine(_dir, "jobs", id);

    public IReadOnlyList<RemoteJobRecord> Newest(int max)
    {
        lock (_gate) return [.. _jobs.AsEnumerable().Reverse().Take(max)];
    }

    public RemoteJobRecord? Find(string id)
    {
        lock (_gate) return _jobs.FirstOrDefault(j => j.Id == id);
    }

    /// <summary>Add a job, or replace it (same id) as it goes on.</summary>
    public void Save(RemoteJobRecord r)
    {
        lock (_gate)
        {
            int i = _jobs.FindIndex(j => j.Id == r.Id);
            if (i >= 0) _jobs[i] = r;
            else _jobs.Add(r);
            if (_jobs.Count > KeepJobs)
            {
                foreach (var old in _jobs.Take(_jobs.Count - KeepJobs)) TryDelete(PictureDir(old.Id));
                _jobs.RemoveRange(0, _jobs.Count - KeepJobs);
            }
            Write(Path.Combine(_dir, "jobs.jsonl"), string.Concat(_jobs.Select(j => JsonSerializer.Serialize(j) + "\n")));
        }
    }

    public void AddSent(SentJobInfo s)
    {
        lock (_gate)
        {
            _sent.Insert(0, s);
            if (_sent.Count > KeepSent) _sent.RemoveRange(KeepSent, _sent.Count - KeepSent);
            Write(Path.Combine(_dir, "sent.json"), JsonSerializer.Serialize(_sent));
        }
    }

    public SentJobInfo? Sent(string? taskId)
    {
        if (string.IsNullOrEmpty(taskId)) return null;
        lock (_gate) return _sent.FirstOrDefault(s => s.TaskId == taskId);
    }

    public void SavePicture(string id, string name, byte[] jpeg)
    {
        try
        {
            Directory.CreateDirectory(PictureDir(id));
            File.WriteAllBytes(Path.Combine(PictureDir(id), name), jpeg);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ServerLog.Write("jobs", $"Couldn't save {name} for job {id}: {e.Message}");
        }
    }

    public byte[]? ReadPicture(string id, string name)
    {
        // Ids are letters, digits and dashes (20261023-020000-abc123): nothing that could leave the folder.
        if (name is not ("before.jpg" or "after.jpg") || id.Length == 0 || id.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-')) return null;
        var p = Path.Combine(PictureDir(id), name);
        try { return File.Exists(p) ? File.ReadAllBytes(p) : null; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    static void Write(string path, string text)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, text);
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ServerLog.Write("jobs", $"Couldn't save {Path.GetFileName(path)}: {e.Message}");
        }
    }

    static T? Read<T>(string path)
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path)) : default; }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return default; }
    }

    static List<T> ReadLines<T>(string path)
    {
        try
        {
            if (!File.Exists(path)) return [];
            return [.. File.ReadAllLines(path).Select(l => { try { return JsonSerializer.Deserialize<T>(l); } catch (JsonException) { return default; } }).OfType<T>()];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return []; }
    }

    static void TryDelete(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
