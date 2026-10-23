using PhotonCanvas.Core;

namespace PhotonCanvas.Server;

/// <summary>The server's log: logs\server-yyyyMMdd.log in the data folder, and the console.</summary>
public static class ServerLog
{
    static readonly Lock Gate = new();

    public static void Write(string category, string message)
    {
        string line = $"{DateTime.Now:HH:mm:ss.fff} [{category}] {message}";
        lock (Gate)
        {
            Console.WriteLine(line);
            try
            {
                var dir = Path.Combine(AppIdentity.DataDir, "logs");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, $"server-{DateTime.Now:yyyyMMdd}.log"), line + Environment.NewLine);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // The log must never stop the server.
            }
        }
    }
}
