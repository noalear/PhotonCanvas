using System.Text.Json;
using System.Text.Json.Serialization;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;

namespace PhotonCanvas.App;

/// <summary>A job this computer uploaded, so a loaded job can be recognised later.</summary>
public sealed record SentJob(string TaskId, string Name, DateTime At);

/// <summary>A saved set of layer settings, e.g. "3 mm basswood – cut".</summary>
public sealed record Preset(string Name, LayerMode Mode, LaserSettings Laser, double LinesPerCm);

/// <summary>
/// Per-user settings and presets in %APPDATA%\PhotonCanvas\app.json. Failures to read
/// or write never stop the app; they just fall back to defaults.
/// </summary>
public sealed class AppSettings
{
    public List<Preset> Presets { get; set; } = [];
    public string? LastMachineIp { get; set; }
    public bool AutoConnect { get; set; } = true;
    public double PassDelayS { get; set; } = 1;
    public bool WholeJobPasses { get; set; } = true;
    public double SvgPxPerInch { get; set; } = 96;
    /// <summary>Clockwise display rotation for the close-up camera (measured on the user's F2 Ultra).</summary>
    public double CloseUpRotationDeg { get; set; } = 31.2;
    public double CameraOpacity { get; set; } = 0.85;
    /// <summary>Show the calibrated camera on the work area (restored on startup).</summary>
    public bool CameraOnWorkArea { get; set; }
    public PhotonCanvas.Core.Camera.MarkStyle CalibMarkStyle { get; set; } = PhotonCanvas.Core.Camera.MarkStyle.Dot;
    public double CalibMarkSizeMm { get; set; } = 2;
    public List<string> RecentFiles { get; set; } = [];
    public double OffsetDistanceMm { get; set; } = 2;
    public int OffsetDirection { get; set; }
    public int OffsetCorners { get; set; } = 1;
    public int OffsetEnds { get; set; } = 1;
    public string TextFont { get; set; } = "Arial";
    public double TextSizeMm { get; set; } = 10;
    public int PolygonSides { get; set; } = 6;
    public int StarPoints { get; set; } = 5;
    public double StarInnerPercent { get; set; } = 50;
    public double GridMajorMm { get; set; } = 10;
    public double GridMinorMm { get; set; } = 1;
    public string GridMajorColor { get; set; } = "#2A303B";
    public string GridMinorColor { get; set; } = "#1D2129";
    public bool ShowGrid { get; set; } = true;
    public bool SnapToObjects { get; set; } = true;
    public bool SnapToGrid { get; set; }
    public double SnapDistancePx { get; set; } = 8;
    /// <summary>Version of the safety notice and terms the user accepted (0 = never).</summary>
    public int AcceptedTermsVersion { get; set; }
    /// <summary>Show the less common layer settings (kerf, tabs, overscan, fiber pulse…).</summary>
    public bool ShowAdvanced { get; set; }
    /// <summary>The last jobs uploaded from this computer (newest first).</summary>
    public List<SentJob> SentJobs { get; set; } = [];
    /// <summary>Frame with a tight outline instead of the rectangle.</summary>
    public bool FrameOutline { get; set; }
    // Magic wand and magnetic pen (camera drawing tools).
    public double WandTolerance { get; set; } = 25;
    public double WandSmoothness { get; set; } = 3;
    public double WandCorners { get; set; } = 40;
    public double WandIgnoreMm2 { get; set; } = 2;
    /// <summary>The user's DepthCharge folder, if it isn't in a usual place.</summary>
    public string? DepthChargeFolder { get; set; }
    /// <summary>Name shown on materials you share.</summary>
    public string? LibraryAuthor { get; set; }
    /// <summary>Old presets have been moved into the material library.</summary>
    public bool PresetsAdopted { get; set; }

    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    static string Dir => AppIdentity.DataDir;
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
