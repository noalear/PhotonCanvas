using Avalonia.Controls;
using Avalonia.Input;
using PhotonCanvas.App.Addons;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Materials;

namespace PhotonCanvas.App;

/// <summary>The main window's side of add-ons (docs/addons.md): what they can use and add.</summary>
public partial class MainWindow : IAddonServices
{
    AddonManager? _addons;
    readonly List<HotCommand> _addonCommands = [];
    readonly List<ContextMenuEntry> _addonMenu = [];

    /// <summary>The add-ons: found, started, stopped, installed and removed here.</summary>
    internal AddonManager Addons => _addons!;

    void StartAddons()
    {
        _addons = new AddonManager(this,
            id => _settings.Addons.TryGetValue(id, out var on) ? on : null,
            (id, on) =>
            {
                _settings.Addons[id] = on;
                _settings.Save();
            });
        _doc.Changed += _ => _documentChanged?.Invoke();
        _machine.PhotoChanged += _ => _photoChanged?.Invoke();
        // Screenshots and tests start without them, so they show the program alone.
        if (!ConnectOnOpen && Environment.GetEnvironmentVariable("PHOTONCANVAS_ADDONS") != "1") return;
        try { _addons.StartAll(); }
        catch (Exception e) { AppLog.Error("Starting add-ons", e); }
    }

    void SetMachine(MachineProfile m)
    {
        bool changed = !ReferenceEquals(Canvas.Machine, m);
        Canvas.Machine = m;
        if (changed) _machineChanged?.Invoke();
    }

    /// <summary>Settings → Add-ons.</summary>
    Task ShowAddons() => new AddonsWindow(Addons).ShowDialog(this);

    void AddAddonMenuItems(ContextMenu menu)
    {
        var shown = _addonMenu.Where(i => i.Visible?.Invoke() ?? true).ToList();
        if (shown.Count == 0) return;
        menu.Items.Add(new Separator());
        foreach (var i in shown)
        {
            var m = new MenuItem { Header = i.Header, IsEnabled = i.Enabled?.Invoke() ?? true };
            if (i.Gesture is { } g)
                try { m.InputGesture = KeyGesture.Parse(g); }
                catch (ArgumentException) { }
            m.Click += (_, _) => i.Run();
            menu.Items.Add(m);
        }
    }

    // ---- IAddonServices ----------------------------------------------------------

    Window IAddonServices.Owner => this;
    Document IAddonServices.Document => _doc;
    MachineProfile IAddonServices.Machine => Canvas.Machine;
    AppSettings IAddonServices.AppSettings => _settings;
    void IAddonServices.SaveAppSettings() => _settings.Save();
    MaterialLibrary IAddonServices.Library => _library;
    ImageShape? IAddonServices.SelectedPicture => SelectedPicture();
    bool IAddonServices.IsConnected => _machine.IsConnected;
    CameraPhoto? IAddonServices.Photo => Canvas.Photo;

    async Task<CameraPhoto?> IAddonServices.TakePhotoAsync()
    {
        if (!_machine.IsConnected) return null;
        await _machine.UpdatePhotoAsync();
        return Canvas.Photo;
    }

    Task<CameraPhoto?> IAddonServices.CloseUpPhotoAsync() => _machine.CloseUpBedPhotoAsync();
    Rect2? IAddonServices.CloseUpSees() => _machine.CloseUpSees();
    (int Front, int Back) IAddonServices.Lights => _machine.Lights;
    void IAddonServices.SetLights(int front, int back) => _machine.SetLights(front, back);
    double? IAddonServices.FocusDistanceMm => _machine.Distance;
    bool IAddonServices.CameraCalibratedFor(double distanceMm) => _machine.CameraMapFor(distanceMm) is not null;
    Task<SkiaSharp.SKBitmap> IAddonServices.PhotographAreaAsync(Rect2 area, double mmPerPx, double distanceMm) =>
        _machine.IsConnected ? _machine.CaptureAreaAsync(area, mmPerPx, distanceMm) : throw new InvalidOperationException("Connect the machine first.");
    (BuiltJob Job, DateTimeOffset SentAt)? IAddonServices.LastSentJob => _job.LastSent;
    void IAddonServices.Status(string text) => StatusText.Text = text;

    async Task IAddonServices.Guard(Func<Task> work)
    {
        // An add-on's bug shows as a message, never closes the program.
        try { await work(); }
        catch (Exception e)
        {
            AppLog.Error("Add-on", e);
            await Dialogs.Message(this, "Something went wrong", e.Message);
        }
    }

    void IAddonServices.ShowHelp(string file, string? anchor) => ShowHelp(file, anchor);
    void IAddonServices.InvalidateCanvas() => Canvas.InvalidateVisual();
    void IAddonServices.RefreshCommands() => _ribbon.Refresh();

    Action? _documentChanged, _machineChanged, _photoChanged;
    event Action? IAddonServices.DocumentChanged { add => _documentChanged += value; remove => _documentChanged -= value; }
    event Action? IAddonServices.MachineChanged { add => _machineChanged += value; remove => _machineChanged -= value; }
    event Action? IAddonServices.PhotoChanged { add => _photoChanged += value; remove => _photoChanged -= value; }

    IDisposable IAddonServices.AddRibbonButton(RibbonButton b)
    {
        var undo = _ribbon.AddExtra(b.Tab, b.Group, g =>
        {
            Control c = (b.Large, b.Checked is not null) switch
            {
                (true, true) => g.LargeToggle(b.Label, b.Icon, _ => b.Run(), b.Tip, b.Enabled, b.Checked),
                (true, false) => g.Large(b.Label, b.Icon, b.Run, b.Tip, b.Enabled),
                (false, true) => g.SmallToggle(b.Label, b.Icon, _ => b.Run(), b.Tip, b.Enabled, b.Checked),
                (false, false) => g.Small(b.Label, b.Icon, b.Run, b.Tip, b.Enabled),
            };
            if (b.Visible is { } v) _ribbon.ShowWhen(c, v);
            return c;
        });
        _ribbon.Refresh();
        return undo;
    }

    IDisposable IAddonServices.AddCommand(AddonCommand c)
    {
        var hot = new HotCommand(c.Id, c.Category, c.Title, c.Keys, c.Run);
        _addonCommands.Add(hot);
        return new AddonUndo(() => _addonCommands.Remove(hot));
    }

    IDisposable IAddonServices.AddContextMenuItem(ContextMenuEntry item)
    {
        _addonMenu.Add(item);
        return new AddonUndo(() => _addonMenu.Remove(item));
    }

    IDisposable IAddonServices.AddCanvasOverlay(ICanvasOverlay overlay)
    {
        Canvas.Overlays.Add(overlay);
        Canvas.InvalidateVisual();
        return new AddonUndo(() =>
        {
            Canvas.Overlays.Remove(overlay);
            Canvas.InvalidateVisual();
        });
    }

    IDisposable IAddonServices.AddHelp(AddonManifest manifest, string folder) => HelpFiles.AddAddon(manifest.Name, folder);
}
