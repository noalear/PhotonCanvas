using PhotonCanvas.App.Addons;

namespace PhotonCanvas.Addon.BendHinge;

/// <summary>"Bend to shape": a button in Home → Draw that opens the bend-to-shape hinge window.</summary>
public sealed class BendHingeAddon : IAddon
{
    /// <summary>An arch with slits in it.</summary>
    const string Icon = "M3 20A9 9 0 0 1 21 20H18A6 6 0 0 0 6 20Z M11.5 11.8h1v1.4h-1z M7.54 14.84 6.55 13.85 5.85 14.55 6.84 15.54z M17.16 15.54 18.15 14.55 17.45 13.85 16.46 14.84z";

    IAddonHost? _host;
    BendHingeWindow? _open;

    public void Start(IAddonHost host)
    {
        _host = host;
        host.AddRibbonButton(new RibbonButton("Home", "Draw", "Bend to shape", Icon, Open)
        {
            Large = true,
            Tip = "A living hinge that bends a flat panel into the selected curve: slits only where it bends, closer where it bends tighter",
        });
        host.AddCommand(new AddonCommand("bendhinge.open", "Shapes", "Bend to shape…", "", Open));
    }

    void Open()
    {
        if (_host is not { } host) return;
        _ = host.Guard(async () =>
        {
            _open = new BendHingeWindow(host);
            try { await _open.ShowDialog(host.Owner); }
            finally { _open = null; }
        });
    }

    public void Stop()
    {
        _open?.Close();
        _open = null;
        _host = null;
    }
}
