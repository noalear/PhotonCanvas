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
        // Right-click on anything: disable its add-on, its tutorial, its help.
        ContextHelp.Install(this);
        DescribeForHelp();
        _doc.Changed += _ => _documentChanged?.Invoke();
        _machine.PhotoChanged += _ => _photoChanged?.Invoke();
        _job.RunFinished += from => _jobFinished?.Invoke(from);
        _job.Sent += sent => _jobSent?.Invoke(sent);
        _job.RunStarted += name => _jobStarted?.Invoke(name);
        _job.RunEnded += finished => _jobEnded?.Invoke(finished);
        // Add-ons coming and going change buttons and the panels' add-on parts.
        _addons.Changed += () =>
        {
            _ribbon.Refresh();
            _props.Refresh();
        };
        // Started once the window opens (ConnectOnOpen is only known then). Tests start without
        // them, so they see the program alone, unless PHOTONCANVAS_ADDONS=1.
        bool started = false;
        Opened += (_, _) =>
        {
            if (started) return;
            started = true;
            if (!ConnectOnOpen && Environment.GetEnvironmentVariable("PHOTONCANVAS_ADDONS") != "1") return;
            try { _addons.StartAll(); }
            catch (Exception e) { AppLog.Error("Starting add-ons", e); }
            // Tabs only add-ons fill (Designer) hide if none did.
            _ribbon.Refresh();
        };
    }

    void SetMachine(MachineProfile m)
    {
        bool changed = !ReferenceEquals(Canvas.Machine, m);
        Canvas.Machine = m;
        _layers.Uv = m.Uv is not null;
        if (changed) _machine.MachineChanged();
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
    void IAddonServices.RefreshLibrary() => _layerEditor.RefreshMaterials();
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
    MachinePanel IAddonServices.MachinePanel => _machine;
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
    void IAddonServices.FitWorkArea() => Canvas.FitField();
    void IAddonServices.ActivateLayer(Guid layerId) => _layers.Activate(layerId);
    void IAddonServices.RefreshCommands() => _ribbon.Refresh();

    Action? _documentChanged, _machineChanged, _photoChanged;
    event Action? IAddonServices.DocumentChanged { add => _documentChanged += value; remove => _documentChanged -= value; }
    event Action? IAddonServices.MachineChanged { add => _machineChanged += value; remove => _machineChanged -= value; }
    event Action? IAddonServices.PhotoChanged { add => _photoChanged += value; remove => _photoChanged -= value; }
    Action<string?>? _jobFinished;
    event Action<string?>? IAddonServices.JobFinished { add => _jobFinished += value; remove => _jobFinished -= value; }
    Action<UploadedJob>? _jobSent;
    event Action<UploadedJob>? IAddonServices.JobSent { add => _jobSent += value; remove => _jobSent -= value; }
    Action<string>? _jobStarted;
    Action<bool>? _jobEnded;
    event Action<string>? IAddonServices.JobStarted { add => _jobStarted += value; remove => _jobStarted -= value; }
    event Action<bool>? IAddonServices.JobEnded { add => _jobEnded += value; remove => _jobEnded -= value; }

    IDisposable IAddonServices.AddRibbonButton(RibbonButton b)
    {
        // An add-on made for where a button used to be lands where it is now.
        var (tab, group) = RibbonPlaces.Map(b.Tab, b.Group);
        b = b with { Tab = tab, Group = group };
        // Tests aren't ribbon buttons: they're listed in the Tests window.
        if (tab == RibbonPlaces.Tests)
            return AddTest(group, b.Label, b.Icon, b.Run, b.Tip, b.Enabled, b.Visible, b.Order, b.AddonId);
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
            if (b.AddonId is { } owner) ContextHelp.Own(c, owner);
            return c;
        }, b.GroupAfter, b.Order);
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

    readonly List<Func<bool>> _closeUpAreaWhen = [];

    /// <summary>Does an add-on want the close-up camera's area shown now.</summary>
    bool AddonsWantCloseUpArea() => _closeUpAreaWhen.Any(f => f());

    IDisposable IAddonServices.ShowCloseUpAreaWhile(Func<bool> when)
    {
        _closeUpAreaWhen.Add(when);
        ShowCloseUpArea();
        return new AddonUndo(() =>
        {
            _closeUpAreaWhen.Remove(when);
            ShowCloseUpArea();
        });
    }

    IDisposable IAddonServices.AddHelp(AddonManifest manifest, string folder) => HelpFiles.AddAddon(manifest.Name, folder);
}
