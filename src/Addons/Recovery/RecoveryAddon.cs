using PhotonCanvas.App.Addons;

namespace PhotonCanvas.Addon.Recovery;

/// <summary>
/// Knocked-piece recovery: when the material is bumped halfway through a job, a camera photo finds
/// the marks already on it, and the rest of the job goes on a new page, moved to line up with them.
/// Nothing is sent from here: the new page goes to the machine with the program's own Ship It.
/// </summary>
public sealed class RecoveryAddon : IAddon
{
    IAddonHost? _host;
    RecoveryWindow? _window;

    public void Start(IAddonHost host)
    {
        _host = host;
        host.AddRibbonButton(new RibbonButton("Camera", "Place", "Knocked piece", "IcFitPiece", Open)
        {
            Large = true,
            Tip = "The material moved during a job? The camera finds what's already marked on it, and the rest of the job is moved to line up, on a new page",
        });
        host.AddCommand(new AddonCommand("recovery.open", "Camera", "Knocked piece…", "", Open));
    }

    void Open()
    {
        if (_host is not { } host) return;
        if (_window is { } open) { open.Activate(); return; }
        _ = host.Guard(() =>
        {
            var w = _window = new RecoveryWindow(host);
            w.Closed += (_, _) => { if (ReferenceEquals(_window, w)) _window = null; };
            w.Show(host.Owner);
            return Task.CompletedTask;
        });
    }

    public void Stop()
    {
        _window?.Close();
        _window = null;
        _host = null;
    }
}
