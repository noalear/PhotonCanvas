using Avalonia.Controls;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Extensions;
using PhotonCanvas.Doc.Materials;

namespace PhotonCanvas.App.Addons;

/// <summary>What the main window offers add-ons (it implements this; add-ons see it through <see cref="IAddonHost"/>).</summary>
public interface IAddonServices
{
    Window Owner { get; }
    Document Document { get; }
    MachineProfile Machine { get; }
    AppSettings AppSettings { get; }
    void SaveAppSettings();
    MaterialLibrary Library { get; }
    ImageShape? SelectedPicture { get; }
    bool IsConnected { get; }
    CameraPhoto? Photo { get; }
    Task<CameraPhoto?> TakePhotoAsync();
    Task<CameraPhoto?> CloseUpPhotoAsync();
    Rect2? CloseUpSees();
    (int Front, int Back) Lights { get; }
    void SetLights(int front, int back);
    double? FocusDistanceMm { get; }
    bool CameraCalibratedFor(double distanceMm);
    Task<SkiaSharp.SKBitmap> PhotographAreaAsync(Rect2 area, double mmPerPx, double distanceMm);
    (BuiltJob Job, DateTimeOffset SentAt)? LastSentJob { get; }
    void Status(string text);
    Task Guard(Func<Task> work);
    void ShowHelp(string file, string? anchor);
    void InvalidateCanvas();
    void RefreshCommands();

    event Action? DocumentChanged;
    event Action? MachineChanged;
    event Action? PhotoChanged;

    IDisposable AddRibbonButton(RibbonButton button);
    IDisposable AddCommand(AddonCommand command);
    IDisposable AddContextMenuItem(ContextMenuEntry item);
    IDisposable AddCanvasOverlay(ICanvasOverlay overlay);
    /// <summary>The add-on's help/ folder appears in the guide; dispose to take it out.</summary>
    IDisposable AddHelp(AddonManifest manifest, string folder);
}

/// <summary>
/// One add-on's view of the program. Remembers everything the add-on adds, so
/// <see cref="RemoveAll"/> takes it all away again (and unhooks its event handlers).
/// </summary>
public sealed class AddonHost : IAddonHost
{
    /// <summary>The add-on API version this program has.</summary>
    public const int CurrentApiVersion = 1;

    readonly AddonManager _manager;
    readonly IAddonServices _s;
    readonly List<IDisposable> _added = [];
    readonly List<(Action Handler, Action Unhook)> _unhook = [];

    internal AddonHost(AddonManager manager, IAddonServices services, AddonManifest manifest)
    {
        _manager = manager;
        _s = services;
        Manifest = manifest;
        _dataFolder = Path.Combine(AddonManager.DataRoot, manifest.Id);
        var help = Path.Combine(manifest.Folder, "help");
        if (Directory.Exists(help)) Keep(_s.AddHelp(manifest, help));
    }

    public int ApiVersion => CurrentApiVersion;
    public AddonManifest Manifest { get; }
    public string Folder => Manifest.Folder;
    readonly string _dataFolder;
    public string DataFolder
    {
        get
        {
            Directory.CreateDirectory(_dataFolder);
            return _dataFolder;
        }
    }

    public Window Owner => _s.Owner;
    public Document Document => _s.Document;
    public MachineProfile Machine => _s.Machine;
    public AppSettings AppSettings => _s.AppSettings;
    public void SaveAppSettings() => _s.SaveAppSettings();
    public MaterialLibrary Library => _s.Library;
    public ImageShape? SelectedPicture => _s.SelectedPicture;
    public bool IsConnected => _s.IsConnected;
    public CameraPhoto? Photo => _s.Photo;
    public Task<CameraPhoto?> TakePhotoAsync() => _s.TakePhotoAsync();
    public Task<CameraPhoto?> CloseUpPhotoAsync() => _s.CloseUpPhotoAsync();
    public Rect2? CloseUpSees() => _s.CloseUpSees();
    public (int Front, int Back) Lights => _s.Lights;
    public void SetLights(int front, int back) => _s.SetLights(front, back);
    public double? FocusDistanceMm => _s.FocusDistanceMm;
    public bool CameraCalibratedFor(double distanceMm) => _s.CameraCalibratedFor(distanceMm);
    public Task<SkiaSharp.SKBitmap> PhotographAreaAsync(Rect2 area, double mmPerPx, double distanceMm) => _s.PhotographAreaAsync(area, mmPerPx, distanceMm);
    public (BuiltJob Job, DateTimeOffset SentAt)? LastSentJob => _s.LastSentJob;
    public void Status(string text) => _s.Status(text);
    public Task Guard(Func<Task> work) => _s.Guard(work);
    public void ShowHelp(string file, string? anchor = null) => _s.ShowHelp(file, anchor);
    public void InvalidateCanvas() => _s.InvalidateCanvas();
    public void RefreshCommands() => _s.RefreshCommands();

    // The add-on's handlers are kept here and unhooked from the program when it stops.
    public event Action? DocumentChanged
    {
        add => Hook(value, h => _s.DocumentChanged += h, h => _s.DocumentChanged -= h);
        remove => Unhook(value);
    }
    public event Action? MachineChanged
    {
        add => Hook(value, h => _s.MachineChanged += h, h => _s.MachineChanged -= h);
        remove => Unhook(value);
    }
    public event Action? PhotoChanged
    {
        add => Hook(value, h => _s.PhotoChanged += h, h => _s.PhotoChanged -= h);
        remove => Unhook(value);
    }

    void Hook(Action? h, Action<Action> add, Action<Action> remove)
    {
        if (h is null) return;
        // An add-on's mistake mustn't stop the program's other handlers.
        Action safe = () =>
        {
            try { h(); }
            catch (Exception e) { AppLog.Error($"Add-on {Manifest.Id}", e); }
        };
        add(safe);
        _unhook.Add((h, () => remove(safe)));
    }

    void Unhook(Action? h)
    {
        int at = _unhook.FindLastIndex(x => x.Handler == h);
        if (at < 0) return;
        _unhook[at].Unhook();
        _unhook.RemoveAt(at);
    }

    IDisposable Keep(IDisposable d)
    {
        _added.Add(d);
        return new AddonUndo(() =>
        {
            if (_added.Remove(d)) d.Dispose();
        });
    }

    Action Safe(Action a) => () =>
    {
        try { a(); }
        catch (Exception e)
        {
            AppLog.Error($"Add-on {Manifest.Id}", e);
            _s.Status($"{Manifest.Name}: {e.Message}");
        }
    };

    Func<bool>? Safe(Func<bool>? f) => f is null ? null : () =>
    {
        try { return f(); }
        catch (Exception) { return false; }
    };

    public IDisposable AddRibbonButton(RibbonButton b) =>
        Keep(_s.AddRibbonButton(b with { Run = Safe(b.Run), Enabled = Safe(b.Enabled), Checked = Safe(b.Checked), Visible = Safe(b.Visible) }));

    public IDisposable AddCommand(AddonCommand c) => Keep(_s.AddCommand(c with { Run = Safe(c.Run) }));

    public IDisposable AddContextMenuItem(ContextMenuEntry item) =>
        Keep(_s.AddContextMenuItem(item with { Run = Safe(item.Run), Visible = Safe(item.Visible), Enabled = Safe(item.Enabled) }));

    public IDisposable AddCanvasOverlay(ICanvasOverlay overlay) => Keep(_s.AddCanvasOverlay(new SafeOverlay(overlay, Manifest.Id)));

    public IDisposable AddToSlot(string slot, Func<SlotContext, Control?> make) => Keep(AddonSlots.Add(slot, make, this));

    public IDisposable AddJobHook(IJobHook hook) => Keep(JobHooks.Add(hook));

    public IDisposable Publish<T>(T service) where T : class
    {
        if (_manager.Services.TryGetValue(typeof(T), out var had) && had.Owner != Manifest.Id)
            throw new InvalidOperationException($"{typeof(T).Name} is already offered by \"{had.Owner}\".");
        _manager.Services[typeof(T)] = (service, Manifest.Id);
        return Keep(new AddonUndo(() => _manager.Services.Remove(typeof(T))));
    }

    public T? Find<T>() where T : class => _manager.Services.TryGetValue(typeof(T), out var s) ? s.Service as T : null;

    /// <summary>Take away everything the add-on added, newest first.</summary>
    internal void RemoveAll()
    {
        foreach (var (_, u) in _unhook) u();
        _unhook.Clear();
        for (int i = _added.Count - 1; i >= 0; i--)
            try { _added[i].Dispose(); }
            catch (Exception e) { AppLog.Error($"Add-on {Manifest.Id} removing", e); }
        _added.Clear();
        _s.RefreshCommands();
        _s.InvalidateCanvas();
    }

    sealed class SafeOverlay(ICanvasOverlay inner, string id) : ICanvasOverlay
    {
        bool _broken;
        public CanvasLayer Layer => inner.Layer;
        public void Draw(Avalonia.Media.DrawingContext ctx, CanvasDrawInfo info)
        {
            if (_broken) return;
            try { inner.Draw(ctx, info); }
            catch (Exception e)
            {
                _broken = true;
                AppLog.Error($"Add-on {id} drawing", e);
            }
        }
    }
}
