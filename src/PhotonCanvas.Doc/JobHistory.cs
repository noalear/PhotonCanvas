using System.Text.Json;
using PhotonCanvas.Core;

namespace PhotonCanvas.Doc;

/// <summary>One finished (or stopped) job, for the history.</summary>
public sealed record JobRecord(DateTime When, string Design, double EstimateS, double TookS, bool Finished, int Layers, double WidthMm, double HeightMm);

/// <summary>
/// The job history, one line of JSON per job in the data folder. The desktop app and the remote
/// server both append to it (a line at a time, so neither loses the other's).
/// </summary>
public static class JobHistory
{
    static string FilePath => Path.Combine(AppIdentity.DataDir, "job-history.jsonl");

    public static void Add(JobRecord r)
    {
        try
        {
            Directory.CreateDirectory(AppIdentity.DataDir);
            File.AppendAllText(FilePath, JsonSerializer.Serialize(r) + "\n");
        }
        catch (IOException) { }
    }

    public static List<JobRecord> All()
    {
        try
        {
            if (!File.Exists(FilePath)) return [];
            return [.. File.ReadAllLines(FilePath).Select(l => { try { return JsonSerializer.Deserialize<JobRecord>(l); } catch (JsonException) { return null; } }).OfType<JobRecord>()];
        }
        catch (IOException) { return []; }
    }
}
