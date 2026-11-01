using PhotonCanvas.App.Addons;

namespace PhotonCanvas.Addon.KerfTest;

/// <summary>Laser → Materials → Kerf gauge (and its command), opening <see cref="KerfGaugeWindow"/>.</summary>
public sealed class KerfTestAddon : IAddon
{
    /// <summary>A frame with a tapered slot and a key half way in.</summary>
    const string Icon = "M2 4h20v16H2z M4 6v12h6l10-4.2v-3.6L10 6z M5 10.6h8.5v2.8H5z";

    IAddonHost _host = null!;
    KerfGaugeWindow? _open;

    public void Start(IAddonHost host)
    {
        _host = host;
        host.AddRibbonButton(new RibbonButton("Laser", "Materials", "Kerf gauge", Icon, Open)
        {
            Tip = "Fits first time: cut a tiny tapered-slot gauge, push its keys in, and a close-up photo reads the kerf and the slot fit, saved on the material for finger joints, slices, dowels and inlays",
        });
        host.AddCommand(new AddonCommand("kerftest.open", "Materials", "Kerf gauge…", "", Open));
    }

    void Open()
    {
        if (_open is { IsVisible: true }) { _open.Activate(); return; }
        var window = new KerfGaugeWindow(_host, KerfGauge.Find(_host.Document.State) is not null);
        _open = window;
        window.Closed += (_, _) => { if (ReferenceEquals(_open, window)) _open = null; };
        _ = _host.Guard(() => window.ShowDialog(_host.Owner));
    }

    public void Stop()
    {
        _open?.Close();
        _open = null;
    }
}
