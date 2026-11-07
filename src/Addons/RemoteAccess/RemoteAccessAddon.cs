using PhotonCanvas.App.Addons;

namespace PhotonCanvas.Addon.RemoteAccess;

/// <summary>Machine → Settings → Remote access: the phone remote's server, QR code and settings.</summary>
public sealed class RemoteAccessAddon : IAddon
{
    RemoteAccessWindow? _open;

    public void Start(IAddonHost host)
    {
        host.AddRibbonButton(new RibbonButton("Machine", "Settings", "Remote access", "IcPhone", () =>
        {
            if (_open is { IsVisible: true }) { _open.Activate(); return; }
            _open = new RemoteAccessWindow();
            _open.Closed += (_, _) => _open = null;
            _open.Show(host.Owner);
        })
        {
            Tip = "Watch and run the laser from your phone: start the remote server, connect the phone with a QR code, choose the designs it can load",
            Large = true,
        });
    }

    /// <summary>Closes the window; a server that's running keeps running (it's a program of its own).</summary>
    public void Stop()
    {
        _open?.Close();
        _open = null;
    }
}
