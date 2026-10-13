using System.Text.Json;
using System.Text.Json.Serialization;
using YTool.Core;
using YTool.Doc;

namespace YTool.App;

/// <summary>A saved set of layer settings, e.g. "3 mm basswood – cut".</summary>
public sealed record Preset(string Name, LayerMode Mode, LaserSettings Laser, double LinesPerCm);

/// <summary>
/// Per-user settings and presets in %APPDATA%\YTool\app.json. Failures to read
/// or write never stop the app; they just fall back to defaults.
/// </summary>
public sealed class AppSettings
{
    public List<Preset> Presets { get; set; } = [];
    public string? LastMachineIp { get; set; }
    public double PassDelayS { get; set; } = 1;
    public bool WholeJobPasses { get; set; } = true;
    public double SvgPxPerInch { get; set; } = 96;
    /// <summary>Clockwise display rotation for the close-up camera (measured on the user's F2 Ultra).</summary>
    public double CloseUpRotationDeg { get; set; } = 31.2;
    public List<string> RecentFiles { get; set; } = [];

    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "YTool");
    static string FilePath => Path.Combine(Dir, "app.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Json) ?? new();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException or NotSupportedException)
        {
            // Unreadable settings: start fresh rather than refusing to open.
        }
        return new();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
            File.Move(tmp, FilePath, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Non-fatal.
        }
    }

    public void AddRecent(string path)
    {
        RecentFiles.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        RecentFiles.Insert(0, path);
        if (RecentFiles.Count > 10) RecentFiles.RemoveRange(10, RecentFiles.Count - 10);
    }
}
