using PhotonCanvas.App.Addons;

namespace PhotonCanvas.Addon.FanTest;

/// <summary>Machine → Settings → Fan test: try the exhaust fan's speed commands, one confirmed command at a time.</summary>
public sealed class FanTestAddon : IAddon
{
    FanTestWindow? _open;

    /// <summary>A three-bladed fan round a hub (24 × 24).</summary>
    const string FanIcon = "M12 10a2 2 0 1 0 0 4a2 2 0 1 0 0-4z M12 2.5c2.6 0 3.6 2.1 2.6 4.6L13.4 10h-2.8L9.4 7.1C8.4 4.6 9.4 2.5 12 2.5z M20.2 16.7c-1.3 2.25-3.6 2.4-5.3.2L13.2 14.4l1.4-2.4 3 .3c2.75.45 3.9 2.15 2.6 4.4z M3.8 16.7c-1.3-2.25-.15-3.95 2.6-4.4l3-.3 1.4 2.4-1.7 2.5c-1.7 2.2-4 2.05-5.3-.2z";

    public void Start(IAddonHost host)
    {
        host.AddRibbonButton(new RibbonButton("Machine", "Settings", "Fan test", FanIcon, () =>
        {
            if (_open is { IsVisible: true }) { _open.Activate(); return; }
            _open = new FanTestWindow(host);
            _open.Closed += (_, _) => _open = null;
            _open.Show(host.Owner);
        })
        {
            Tip = "Find out which command, if any, makes the exhaust fan quieter: read what the machine reports, send fan-only commands, run a laser-off job that steps the fan through its gears",
            Order = 30,
        });
    }

    public void Stop()
    {
        _open?.Close();
        _open = null;
    }
}
