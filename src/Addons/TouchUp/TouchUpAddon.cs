using PhotonCanvas.App.Addons;

namespace PhotonCanvas.Addon.TouchUp;

/// <summary>
/// Burn, look, touch up: a button on the Camera tab opens the touch-up window. Everything it adds
/// to a design is a page of its own (sent with Ship It like any job); nothing here moves the head
/// or fires the laser.
/// </summary>
public sealed class TouchUpAddon : IAddon
{
    IAddonHost? _host;
    TouchUpWindow? _window;

    public void Start(IAddonHost host)
    {
        _host = host;
        host.AddRibbonButton(new RibbonButton("Camera", "Check the burn", "Touch up", "IcEyedropper", Open)
        {
            Large = true,
            Tip = "Burn, look, touch up: photograph a finished engraving, see where it came out too light, and add a touch-up job that goes over only those parts",
            Enabled = () => TouchUpAnalysis.Candidates(TouchUpPage.DesignOf(host.Document.State) ?? host.Document.State).Any(),
        });
        host.AddCommand(new AddonCommand("touchup.open", "Camera", "Touch up an engraving…", "", Open));
        host.DocumentChanged += Refresh;
    }

    void Refresh() => _host?.RefreshCommands();

    void Open()
    {
        if (_host is not { } host) return;
        if (_window is { IsVisible: true }) { _window.Activate(); return; }
        _ = host.Guard(async () =>
        {
            _window = new TouchUpWindow(host);
            try { await _window.ShowDialog(host.Owner); }
            finally { _window = null; }
        });
    }

    public void Stop()
    {
        if (_host is { } host) host.DocumentChanged -= Refresh;
        _window?.Close();
        _window = null;
        _host = null;
    }
}
