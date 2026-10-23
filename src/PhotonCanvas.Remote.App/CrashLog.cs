namespace PhotonCanvas.Remote.App;

/// <summary>
/// What went wrong, kept in the app's folder (crash.txt) when the app had to close, and shown on the
/// next start so it can be copied and sent. Nothing leaves the phone by itself.
/// </summary>
public static class CrashLog
{
    static string FilePath(string folder) => Path.Combine(folder, "crash.txt");

    public static void Write(string folder, object? error)
    {
        try
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(FilePath(folder), $"{DateTimeOffset.Now:u}\n{error}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>The report from the last time the app closed unexpectedly (then forgotten), or null.</summary>
    public static string? TakeLast(string folder)
    {
        try
        {
            if (!File.Exists(FilePath(folder))) return null;
            var text = File.ReadAllText(FilePath(folder));
            File.Delete(FilePath(folder));
            return text;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }
}
