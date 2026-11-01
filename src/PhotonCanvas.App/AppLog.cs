using System.Text;

namespace PhotonCanvas.App;

/// <summary>
/// Plain-text diary in %APPDATA%\PhotonCanvas\logs (one file per day, last 14 kept):
/// machine events as received, warnings and any unexpected errors. Writing
/// never throws.
/// </summary>
public static class AppLog
{
    static readonly object Gate = new();
    public static string Dir { get; } = Path.Combine(PhotonCanvas.Core.AppIdentity.DataDir, "logs");
    static bool _pruned;

    public static string CurrentFile => Path.Combine(Dir, $"photoncanvas-{DateTime.Now:yyyyMMdd}.log");

    public static void Write(string category, string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Dir);
                File.AppendAllText(CurrentFile, $"{DateTime.Now:HH:mm:ss.fff} [{category}] {message}{Environment.NewLine}", Encoding.UTF8);
                if (!_pruned)
                {
                    _pruned = true;
                    foreach (var old in new DirectoryInfo(Dir).GetFiles("photoncanvas-*.log").OrderByDescending(f => f.Name).Skip(14))
                        old.Delete();
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Logging must never take the app down.
        }
    }

    public static void Error(string where, Exception e) => Write("error", $"{where}: {e}");
}
