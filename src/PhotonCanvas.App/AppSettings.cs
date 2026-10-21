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
/// <summary>A named G-code snippet for the console.</summary>
public sealed class Macro
{
    public string Name { get; set; } = "";
    public string Gcode { get; set; } = "";
}

public sealed class AppSettings
{
    public List<Preset> Presets { get; set; } = [];
    public string? LastMachineIp { get; set; }
    public bool AutoConnect { get; set; } = true;
    public double PassDelayS { get; set; } = 1;
    public bool WholeJobPasses { get; set; } = true;
    public double SvgPxPerInch { get; set; } = 96;
    /// <summary>Clockwise display rotation for the close-up camera (measured on the user's F2 Ultra).</summary>
    public double CloseUpRotationDeg { get; set; } = -148.8;
    /// <summary>Where the close-up camera looks on the bed, per focus distance (from Measure colours).</summary>
    public List<CloseUpPlacement> CloseUpPlacements { get; set; } = [];
    public double CameraOpacity { get; set; } = 0.85;
    /// <summary>Show the calibrated camera on the work area (restored on startup).</summary>
    public bool CameraOnWorkArea { get; set; }
    public PhotonCanvas.Core.Camera.MarkStyle CalibMarkStyle { get; set; } = PhotonCanvas.Core.Camera.MarkStyle.Dot;
    public double CalibMarkSizeMm { get; set; } = 2;
    public List<string> RecentFiles { get; set; } = [];
    /// <summary>Lasers the user added (GRBL machines); the F2 Ultra is built in.</summary>
    public List<PhotonCanvas.Core.MachineSetup> Machines { get; set; } = [];
    /// <summary>Where each side panel is (Docking): side, order, folded, floating.</summary>
    public Dictionary<string, PanelPlace> PanelLayout { get; set; } = [];
    /// <summary>The sides' widths (px); 0 = as they start.</summary>
    public double LeftDockWidth { get; set; }
    public double RightDockWidth { get; set; }
    /// <summary>The machine in use; null = the F2 Ultra.</summary>
    public Guid? ActiveMachine { get; set; }

    /// <summary>The machine in use (the F2 Ultra unless another was chosen).</summary>
    public PhotonCanvas.Core.MachineSetup ActiveSetup() => Machines.FirstOrDefault(m => m.Id == ActiveMachine) ?? PhotonCanvas.Core.MachineSetup.F2Ultra;

    /// <summary>At start, ask GitHub whether a newer version has been published (nothing about you is sent).</summary>
    public bool CheckForUpdates { get; set; } = true;
    /// <summary>A version the user said not to be told about again.</summary>
    public string? SkippedVersion { get; set; }
    /// <summary>The design open when the program was last closed (reopened on start), if it had been saved.</summary>
    public string? LastOpenFile { get; set; }
    /// <summary>The part of the bed on screen then (mm: left, top, right, bottom), to come back to the same view.</summary>
    public double[]? LastView { get; set; }
    public double OffsetDistanceMm { get; set; } = 2;
    public int OffsetDirection { get; set; }
    public int OffsetCorners { get; set; } = 1;
    public int OffsetEnds { get; set; } = 1;
    public string TextFont { get; set; } = "Arial";
    public double TextSizeMm { get; set; } = 10;
    public int PolygonSides { get; set; } = 6;
    public int StarPoints { get; set; } = 5;
    public double StarInnerPercent { get; set; } = 50;
    /// <summary>Take a camera photo every few seconds while a job runs, and make a video of it.</summary>
    public bool TimeLapse { get; set; }
    public double TimeLapseSeconds { get; set; } = 5;
    /// <summary>"Dark" or "Light" (used from the next start).</summary>
    public string Theme { get; set; } = "Dark";
    /// <summary>Show lengths in inches instead of millimetres.</summary>
    public bool Inches { get; set; }
    public double GridMajorMm { get; set; } = 10;
    public double GridMinorMm { get; set; } = 1;
    public string GridMajorColor { get; set; } = "#2A303B";
    public string GridMinorColor { get; set; } = "#1D2129";
    public bool ShowGrid { get; set; } = true;
    /// <summary>Draw pictures as they'll come out on their layer's material (where it has a tone test).</summary>
    public bool PicturesAsBurned { get; set; }
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
    /// <summary>
    /// How much longer real jobs take than our estimate (it leaves out acceleration), learned
    /// from finished jobs; keeps the "laser is here" marker in step with the machine.
    /// </summary>
    public double JobTimeScale { get; set; } = 1.0;
    /// <summary>The machine controls on the work area are folded down to the status.</summary>
    public bool MachineHudFolded { get; set; }
    /// <summary>Ribbon tab last used.</summary>
    public string RibbonTab { get; set; } = "Home";
    // Magic wand and magnetic pen (camera drawing tools).
    public double WandTolerance { get; set; } = 25;
    public double WandSmoothness { get; set; } = 3;
    public double WandCorners { get; set; } = 40;
    public double WandIgnoreMm2 { get; set; } = 2;
    /// <summary>The user's DepthCharge folder, if it isn't in a usual place.</summary>
    public string? DepthChargeFolder { get; set; }
    /// <summary>Where the 3D model browser last looked.</summary>
    public string? ModelFolder { get; set; }
    /// <summary>Name shown on materials you share.</summary>
    public string? LibraryAuthor { get; set; }
    /// <summary>Old presets have been moved into the material library.</summary>
    public bool PresetsAdopted { get; set; }
    /// <summary>Fonts starred in the font manager (shown first).</summary>
    public List<string> FontFavourites { get; set; } = [];
    /// <summary>Fonts hidden in the font manager (left out of the font list).</summary>
    public List<string> FontHidden { get; set; } = [];
    /// <summary>Keyboard shortcuts changed from their defaults: command id → keys ("" = none).</summary>
    public Dictionary<string, string> Hotkeys { get; set; } = [];
    // What the user has, so features that need something they don't have stay out of the way.
    /// <summary>The equipment question has been answered (asked once, at the first start).</summary>
    public bool EquipmentAsked { get; set; }
    public bool OwnsRa2 { get; set; }
    public bool OwnsRa3 { get; set; }
    public bool OwnsConveyor { get; set; }
    /// <summary>Colour tests, measuring colours and colour pictures on metal.</summary>
    public bool ShowColourOnMetal { get; set; } = true;
    /// <summary>Diffraction gratings, shimmer pictures and the depth-map almost-hologram.</summary>
    public bool ShowDiffraction { get; set; } = true;
    public bool ShowConsole { get; set; } = true;

    /// <summary>Any attachment that gives a work mode besides the flat bed.</summary>
    public bool OwnsAttachment => OwnsRa2 || OwnsRa3 || OwnsConveyor;

    /// <summary>Framing: power (%) and speed (mm/s) of the faint trace.</summary>
    public double FramePowerPct { get; set; } = 6.4;
    public double FrameSpeedMmS { get; set; } = 16000;

    /// <summary>"Flat", "Rotary" or "Conveyor".</summary>
    public string WorkMode { get; set; } = "Flat";
    /// <summary>Rotary: 0 RA2 rollers, 1 RA2 chuck, 2 RA3 rollers, 3 RA3 chuck.</summary>
    public int RotaryAttachment { get; set; }
    public double RotaryDiameterMm { get; set; } = 60;
    /// <summary>Conveyor: the firmware scale (0.74592 at 100 % calibration) and angle compensation.</summary>
    public double ConveyorUMultiple { get; set; } = 0.74592;
    public double ConveyorAngle { get; set; }
    /// <summary>G-code snippets saved in the console: name → G-code.</summary>
    public List<Macro> Macros { get; set; } = [];

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

/// <summary>
/// Where the close-up camera looks, at one focus distance: the map from the bed (mm) to the
/// camera's own picture (pixels, as it comes from the camera). The head doesn't travel, so
/// this stays put until the focus height changes.
/// </summary>
public sealed class CloseUpPlacement
{
    public double DistanceMm { get; set; }
    /// <summary>The homography's eight numbers (H11, H12, H13, H21, H22, H23, H31, H32).</summary>
    public double[] MmToCamera { get; set; } = [];
    public DateTime Made { get; set; } = DateTime.Now;

    public PhotonCanvas.Core.Camera.Homography? Map => MmToCamera.Length == 8
        ? new PhotonCanvas.Core.Camera.Homography(MmToCamera[0], MmToCamera[1], MmToCamera[2], MmToCamera[3], MmToCamera[4], MmToCamera[5], MmToCamera[6], MmToCamera[7])
        : null;
}
