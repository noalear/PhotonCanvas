using System.Text.Json;
using System.Text.Json.Serialization;
using PhotonCanvas.Core;

namespace PhotonCanvas.Doc;

/// <summary>A job this computer uploaded, so a loaded job can be recognised later.</summary>
public sealed record SentJob(string TaskId, string Name, DateTime At);

/// <summary>A saved set of layer settings, e.g. "3 mm basswood – cut".</summary>
public sealed record Preset(string Name, LayerMode Mode, LaserSettings Laser, double LinesPerCm);

/// <summary>Where a side panel is: which side, its place there, folded to its title, or floating in its own window.</summary>
public sealed record PanelPlace
{
    public string Side { get; init; } = "Left";
    public int Order { get; init; }
    public bool Folded { get; init; }
    public bool Floating { get; init; }
    /// <summary>A floating panel's window (screen position and size).</summary>
    public double X { get; init; } = double.NaN;
    public double Y { get; init; } = double.NaN;
    public double W { get; init; } = 320;
    public double H { get; init; } = 520;
}

/// <summary>A named G-code snippet for the console.</summary>
public sealed class Macro
{
    public string Name { get; set; } = "";
    public string Gcode { get; set; } = "";
}

/// <summary>
/// Per-user settings and presets in the data folder's app.json. Failures to read or write never
/// stop the app; they just fall back to defaults. The desktop app owns this file: other programs
/// (the remote server) only read it, because the app would overwrite their changes on its next save.
/// </summary>
public sealed class AppSettings
{
    public List<Preset> Presets { get; set; } = [];
    public string? LastMachineIp { get; set; }
    public bool AutoConnect { get; set; } = true;
    /// <summary>Add-ons the person turned on (true) or off (false), by id; ones not listed follow their own default.</summary>
    public Dictionary<string, bool> Addons { get; set; } = [];
    public double PassDelayS { get; set; } = 1;
    public bool WholeJobPasses { get; set; } = true;
    /// <summary>Wait this long after each cut or score path (s), set in the job bar. 0 = none.</summary>
    public double PathPauseS { get; set; }
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
    public PhotonCanvas.Core.MachineSetup ActiveSetup() =>
        Machines.FirstOrDefault(m => m.Id == ActiveMachine) ?? PhotonCanvas.Core.MachineSetup.XToolFamily.FirstOrDefault(m => m.Id == ActiveMachine) ?? PhotonCanvas.Core.MachineSetup.F2Ultra;

    /// <summary>Quick processing: closing the lid focuses, photographs, centres the design on the material and loads the job (the button starts it).</summary>
    public bool QuickProcessing { get; set; }
    /// <summary>The auto-focus distance measured on the empty bed (mm): material thickness is the difference from it.</summary>
    public double? BedFocusMm { get; set; }
    /// <summary>How far the MOPA lands from the blue laser (mm), measured with Machine → Align lasers.</summary>
    public double MopaOffsetX { get; set; }
    public double MopaOffsetY { get; set; }
    /// <summary>The galvo's pivot heights (mm), measured with the bevel test: for slopes across (edges running down the field) and down (edges running across).</summary>
    public double? BevelPivotAcross { get; set; }
    public double? BevelPivotDown { get; set; }
    /// <summary>Exhaust fan gear (1–4) and purifier speed (%) sent with each job (3 and 50 are what xTool Studio sends).</summary>
    public int FanGear { get; set; } = 3;
    public int PurifierSpeed { get; set; } = 50;

    /// <summary>At start (and every few hours while open), ask GitHub whether a newer version has been published (nothing about you is sent).</summary>
    public bool CheckForUpdates { get; set; } = true;
    /// <summary>Download a newer version (and add-on updates) without asking, and put it in when the program closes.</summary>
    public bool AutoUpdate { get; set; } = true;
    /// <summary>The version that ran last time (to say "Updated to …" once after an update).</summary>
    public string? LastVersion { get; set; }
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
    /// <summary>The test last opened from the Tests window (Materials &amp; Tests → Tests), picked again next time.</summary>
    public string? LastTest { get; set; }
    public bool SnapToObjects { get; set; } = true;
    public bool SnapToGrid { get; set; }
    public double SnapDistancePx { get; set; } = 8;
    /// <summary>Object snaps (ends, middles, centres, crossings… as in AutoCAD) on; F3 turns them off and on.</summary>
    public bool ObjectSnap { get; set; } = true;
    /// <summary>Which object snaps are on (<see cref="Doc.Geometry.SnapKinds"/>).</summary>
    public int ObjectSnapKinds { get; set; } = (int)Doc.Geometry.SnapKinds.Default;
    /// <summary>Pause on a snap point to line up with it across or down.</summary>
    public bool SnapTracking { get; set; } = true;
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
    /// <summary>Which estimate <see cref="JobTimeScale"/> was learned against (2: the one timed on the machine, Oct 2026).</summary>
    public int JobTimeModel { get; set; }
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
    /// <summary>A rotary of the user's own (homemade, or another make) on the machine's rotary port, with its own scale.</summary>
    public bool OwnsCustomRotary { get; set; }
    /// <summary>Colour tests, measuring colours and colour pictures on metal.</summary>
    public bool ShowColourOnMetal { get; set; } = true;
    /// <summary>Diffraction gratings, shimmer pictures and the depth-map almost-hologram.</summary>
    public bool ShowDiffraction { get; set; } = true;
    public bool ShowConsole { get; set; } = true;

    /// <summary>Any attachment that gives a work mode besides the flat bed.</summary>
    public bool OwnsAttachment => OwnsRa2 || OwnsRa3 || OwnsConveyor || OwnsCustomRotary;

    /// <summary>Framing: power (%) and speed (mm/s) of the faint trace.</summary>
    public double FramePowerPct { get; set; } = 6.4;
    public double FrameSpeedMmS { get; set; } = 16000;

    /// <summary>"Flat", "Rotary" or "Conveyor".</summary>
    public string WorkMode { get; set; } = "Flat";
    /// <summary>Rotary: 0 RA2 rollers, 1 RA2 chuck, 2 RA3 rollers, 3 RA3 chuck, 4 the user's own rotary.</summary>
    public int RotaryAttachment { get; set; }
    public double RotaryDiameterMm { get; set; } = 60;
    /// <summary>The user's own rotary: holds the object in a chuck (it turns with the motor) or on rollers.</summary>
    public bool CustomRotaryChuck { get; set; } = true;
    /// <summary>
    /// The user's own rotary with a chuck: the firmware's units for one turn of the object (the scale
    /// sent is this ÷ the circumference). 128 on xTool's chucks; the calibration in Work mode corrects it.
    /// </summary>
    public double CustomRotaryTurnUnits { get; set; } = 128;
    /// <summary>The user's own rotary on rollers: the firmware scale (0.59917 on the RA2 Pro).</summary>
    public double CustomRotaryRollerScale { get; set; } = 0.59917;
    /// <summary>The user's own rotary turns the other way from xTool's (a motor wired the other way round).</summary>
    public bool CustomRotaryReverse { get; set; }
    /// <summary>
    /// Rotary: where the laser draws across the object, in mm from the back (top) edge of the galvo
    /// field. The middle (110) suits xTool's rotaries on their marked lines; a rotary that sits
    /// elsewhere needs its own (frame the job to see the line).
    /// </summary>
    public double RotaryLineMm { get; set; } = 110;
    /// <summary>
    /// A rotary job was stopped part way and left the object turned this far from where it started
    /// (U, mm round the object, as the job wrote it); null once it's back. Kept with the scale and
    /// circumference of that job, so turning it back uses the same ones.
    /// </summary>
    public double? RotaryLeftU { get; set; }
    public double RotaryLeftScale { get; set; }
    public double RotaryLeftCircumferenceMm { get; set; }
    /// <summary>
    /// Turning the rotary outside a job (the conveyor's feed request): its units for one of the
    /// motor's units in a job (the U scale × mm). 0 = not checked yet, so 1 ÷ the conveyor factor
    /// is assumed (the machine scales the feed by its belt factor). Corrected from a one-turn check.
    /// </summary>
    public double RotaryNowScale { get; set; }
    /// <summary>
    /// Rotary: how high the axis of the rotary is above the bed, in mm the way a focus height is given (so a part's
    /// top height minus this is its distance from the axis). Null until typed or set from a scan of a round rod.
    /// </summary>
    public double? RotaryAxisHeightMm { get; set; }
    /// <summary>Scan the part: measure with the pointer and close-up camera (true) or the machine's auto-focus.</summary>
    public bool RotaryScanPointer { get; set; } = true;
    /// <summary>Scan the part: angles to measure at, the span of the turn, and the places along the axis.</summary>
    public int RotaryScanStops { get; set; } = 24;
    public double RotaryScanFromDeg { get; set; }
    public double RotaryScanToDeg { get; set; } = 360;
    public double RotaryScanX0 { get; set; } = 95;
    public double RotaryScanX1 { get; set; } = 125;
    public int RotaryScanPoints { get; set; } = 7;
    /// <summary>
    /// Scan the part: the stock is a standard shape (a square bar, a round rod…), so the scan only finds how it sits
    /// and the model is that exact shape; <see cref="StockKind.Scanned"/> uses what was measured. Sizes in mm.
    /// </summary>
    public StockKind RotaryStockKind { get; set; } = StockKind.Scanned;
    public double RotaryStockWidthMm { get; set; } = 50.8;
    public double RotaryStockHeightMm { get; set; } = 50.8;
    public bool RotaryStockInches { get; set; } = true;
    /// <summary>Scan the part: extra wait after each turn for the part to settle (seconds).</summary>
    public double RotaryScanSettleS { get; set; } = 0.5;
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

    /// <summary>Set after restoring a backup: nothing is saved until the program starts again (it would overwrite what was restored).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public static bool Frozen { get; set; }

    public void Save()
    {
        if (Frozen) return;
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
