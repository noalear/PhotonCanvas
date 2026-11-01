using Avalonia.Controls;
using Avalonia.Media;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Extensions;
using PhotonCanvas.Doc.Materials;

namespace PhotonCanvas.App.Addons;

// The add-on API (version 1). docs/addons.md explains it with examples.
//
// An add-on is a folder holding addon.json, its assembly and (optionally) a help/ folder. PhotonCanvas
// loads each one into its own unloadable load context, calls Start with a host, and every button,
// command, menu item, overlay, slot panel and job hook it adds through the host is taken away again
// when it is turned off or removed, while the program keeps running. Shared code (Avalonia, Core,
// Doc, the App itself) always comes from the program, never from the add-on's folder.

/// <summary>The class an add-on's assembly provides (named by "entry" in addon.json, or the only one).</summary>
public interface IAddon
{
    /// <summary>Called on the UI thread when the add-on is turned on (at start-up, or later from the Add-ons window).</summary>
    void Start(IAddonHost host);

    /// <summary>
    /// Called on the UI thread when it is turned off or removed. Close your windows, stop timers and
    /// background work, unhook any events you subscribed to yourself. What you added through the host
    /// is removed for you after this returns.
    /// </summary>
    void Stop();
}

/// <summary>What the program gives an add-on. Everything you add through it goes away when the add-on stops.</summary>
public interface IAddonHost
{
    /// <summary>The API version of the running program (bumped only when something is removed or changed incompatibly).</summary>
    int ApiVersion { get; }

    /// <summary>This add-on's addon.json, and where it is installed.</summary>
    AddonManifest Manifest { get; }

    /// <summary>The add-on's own folder (read-only: it may be replaced on update).</summary>
    string Folder { get; }

    /// <summary>A folder for the add-on's own files and settings (kept when the add-on is removed and installed again).</summary>
    string DataFolder { get; }

    /// <summary>The main window: the owner for your dialogs.</summary>
    Window Owner { get; }

    /// <summary>The open design: State, Selection, ActiveLayer, Commit (one undo step), BeginEdit/UpdateEdit/EndEdit.</summary>
    Document Document { get; }

    /// <summary>Raised after the open design changes (an edit, undo, redo, or another file opened).</summary>
    event Action? DocumentChanged;

    /// <summary>The chosen machine's limits and work area.</summary>
    MachineProfile Machine { get; }

    /// <summary>Raised when another machine is chosen.</summary>
    event Action? MachineChanged;

    /// <summary>The program's settings (read; change only with care, then call <see cref="SaveAppSettings"/>).</summary>
    AppSettings AppSettings { get; }
    void SaveAppSettings();

    /// <summary>The material library.</summary>
    MaterialLibrary Library { get; }

    /// <summary>After you change the library (and saved it): the program refreshes its material lists.</summary>
    void RefreshLibrary();

    /// <summary>The picked picture, when exactly one picture is selected.</summary>
    ImageShape? SelectedPicture { get; }

    /// <summary>Is a laser connected.</summary>
    bool IsConnected { get; }

    /// <summary>The camera's latest picture of the work area (null = none yet).</summary>
    CameraPhoto? Photo { get; }

    /// <summary>Raised when a new overview photo arrives.</summary>
    event Action? PhotoChanged;

    /// <summary>
    /// Take a new overview photo (nothing moves; null when not connected). Never move the head or fire
    /// the laser from an add-on unless the person pressed a button that clearly says so.
    /// </summary>
    Task<CameraPhoto?> TakePhotoAsync();

    /// <summary>A close-up photo laid straight on the bed (null when not connected or the close-up camera's place isn't known).</summary>
    Task<CameraPhoto?> CloseUpPhotoAsync();

    /// <summary>The part of the bed the close-up camera sees at the current focus height, if known.</summary>
    Rect2? CloseUpSees();

    /// <summary>The focus distance the machine measured (mm), if known.</summary>
    double? FocusDistanceMm { get; }

    /// <summary>Is the overview camera calibrated for this focus distance.</summary>
    bool CameraCalibratedFor(double distanceMm);

    /// <summary>
    /// A fresh overview photo of part of the bed, straightened at <paramref name="mmPerPx"/> (the red pointers
    /// are switched off for it; nothing moves). Throws if the camera isn't calibrated or no laser is connected.
    /// </summary>
    Task<SkiaSharp.SKBitmap> PhotographAreaAsync(Rect2 area, double mmPerPx, double distanceMm);

    /// <summary>The last job sent to the machine in this session, and when it was sent (null = none yet).</summary>
    (BuiltJob Job, DateTimeOffset SentAt)? LastSentJob { get; }

    /// <summary>
    /// Raised when a job this session sent has finished on the machine (not when it's stopped part-way).
    /// The argument is the design file it was sent from (null = unsaved). Tests use it to open their results.
    /// </summary>
    event Action<string?>? JobFinished;

    /// <summary>
    /// The program's machine connection itself, for add-ons that need more than the members above
    /// (close-up camera placement, the red pointer, measuring). It can move the head and fire the laser:
    /// do that only from a button that plainly says it will. Its members may change between versions;
    /// use the host's own members where they do the job.
    /// </summary>
    MachinePanel MachinePanel { get; }

    /// <summary>The machine's lights (0–100 each), and setting them.</summary>
    (int Front, int Back) Lights { get; }
    void SetLights(int front, int back);

    /// <summary>A line in the status bar.</summary>
    void Status(string text);

    /// <summary>Run work, showing a message (not a crash) if it throws.</summary>
    Task Guard(Func<Task> work);

    /// <summary>Open a help page (a file in your help/ folder, or one of the program's pages).</summary>
    void ShowHelp(string file, string? anchor = null);

    /// <summary>Redraw the canvas (after your overlay's data changes).</summary>
    void InvalidateCanvas();

    /// <summary>Zoom the canvas out to the whole work area.</summary>
    void FitWorkArea();

    /// <summary>Make a layer the active one and pick it in the Layers panel (e.g. the layer you just added).</summary>
    void ActivateLayer(Guid layerId);

    /// <summary>Ask every button's enabled/checked/visible test again (after your state changes).</summary>
    void RefreshCommands();

    /// <summary>A button on the ribbon. The group is made if it isn't there (and taken away again when empty).</summary>
    IDisposable AddRibbonButton(RibbonButton button);

    /// <summary>A command for the shortcut list (Settings → Shortcuts) and the command search; with a key, a shortcut too.</summary>
    IDisposable AddCommand(AddonCommand command);

    /// <summary>An item on the canvas's right-click menu.</summary>
    IDisposable AddContextMenuItem(ContextMenuEntry item);

    /// <summary>Something drawn on the canvas.</summary>
    IDisposable AddCanvasOverlay(ICanvasOverlay overlay);

    /// <summary>
    /// Outline where the close-up camera looks on the canvas while <paramref name="when"/> says so
    /// (asked again after each change to the design or the machine), e.g. while your test is in the design.
    /// </summary>
    IDisposable ShowCloseUpAreaWhile(Func<bool> when);

    /// <summary>A panel in a named slot of a window (see <see cref="AddonSlots"/> for the slots there are).</summary>
    IDisposable AddToSlot(string slot, Func<SlotContext, Control?> make);

    /// <summary>A step in turning the design into a job (see <see cref="IJobHook"/>).</summary>
    IDisposable AddJobHook(IJobHook hook);

    /// <summary>
    /// Offer something to other add-ons (looked up by type with <see cref="Find{T}"/>). Add-ons that
    /// use it list this one under "requires" in addon.json, so it starts first and stops last.
    /// </summary>
    IDisposable Publish<T>(T service) where T : class;

    /// <summary>A service another add-on published (null if that add-on isn't on).</summary>
    T? Find<T>() where T : class;
}

/// <summary>
/// A ribbon button. <paramref name="Tab"/> and <paramref name="Group"/> are names as shown ("Home",
/// "Draw"); a new tab or group is made at the end if there isn't one. <paramref name="Icon"/> is
/// one of the program's icon keys (Icons.axaml, e.g. "IcDepth") or SVG path data for a 24×24 box.
/// </summary>
public sealed record RibbonButton(string Tab, string Group, string Label, string Icon, Action Run)
{
    public string? Tip { get; init; }
    /// <summary>Big (icon over label) or small (icon beside label).</summary>
    public bool Large { get; init; }
    public Func<bool>? Enabled { get; init; }
    /// <summary>Set for a toggle: is it on.</summary>
    public Func<bool>? Checked { get; init; }
    /// <summary>Hidden while this says false.</summary>
    public Func<bool>? Visible { get; init; }
    /// <summary>
    /// When the group has to be made: put it after this group of the tab (default: at the end).
    /// "Tests|Materials" means after Tests, or after Materials when there's no Tests group.
    /// </summary>
    public string? GroupAfter { get; init; }
    /// <summary>Where it goes among add-ons' buttons in the same group: lower first (then in the order they came). The program's own buttons come first.</summary>
    public int Order { get; init; }
}

/// <summary>
/// A command. <paramref name="Id"/> is unique ("papercraft.open"); <paramref name="Category"/> groups it in
/// the shortcut list; <paramref name="Keys"/> is a gesture like "Ctrl+Alt+P" (several with |; empty for none).
/// The person can change the keys in Settings like any other command.
/// </summary>
public sealed record AddonCommand(string Id, string Category, string Title, string Keys, Action Run);

/// <summary>A right-click menu item. Shown when <see cref="Visible"/> says so; greyed out when <see cref="Enabled"/> says no.</summary>
public sealed record ContextMenuEntry(string Header, Action Run)
{
    public Func<bool>? Visible { get; init; }
    public Func<bool>? Enabled { get; init; }
    public string? Gesture { get; init; }
}

public enum CanvasLayer
{
    /// <summary>Over the camera picture, under the shapes (world mm coordinates).</summary>
    UnderShapes,
    /// <summary>Over the shapes (world mm coordinates).</summary>
    OverShapes,
    /// <summary>On top of everything, in screen pixels (labels, legends).</summary>
    Screen,
}

/// <summary>Drawing on the canvas. Keep it quick: it runs on every repaint.</summary>
public interface ICanvasOverlay
{
    CanvasLayer Layer { get; }
    void Draw(DrawingContext ctx, CanvasDrawInfo info);
}

/// <summary>For an overlay: how big a screen pixel is, and turning world mm into screen pixels.</summary>
public sealed record CanvasDrawInfo(double PixelsPerMm, Func<Vec2, Avalonia.Point> ToScreen, Rect2 Visible);

/// <summary>What a window passes to the panels in one of its slots.</summary>
public sealed class SlotContext
{
    public required string Slot { get; init; }
    /// <summary>The window holding the slot.</summary>
    public required Window Window { get; init; }
    /// <summary>The window's own object (e.g. its options), when the slot says what it passes.</summary>
    public object? Data { get; init; }
    /// <summary>Call when your panel's settings change, so the window can recompute.</summary>
    public Action Changed { get; init; } = () => { };
}
