using Avalonia.Threading;
using PhotonCanvas.App.Addons;

namespace PhotonCanvas.Addon.KerfTest;

/// <summary>Machine → Calibrate → Kerf gauge (and its command), opening <see cref="KerfGaugeWindow"/>.</summary>
public sealed class KerfTestAddon : IAddon
{
    /// <summary>A frame with a tapered slot and a key half way in.</summary>
    const string Icon = "M2 4h20v16H2z M4 6v12h6l10-4.2v-3.6L10 6z M5 10.6h8.5v2.8H5z";

    IAddonHost _host = null!;
    KerfGaugeWindow? _open;

    public void Start(IAddonHost host)
    {
        _host = host;
        host.AddRibbonButton(new RibbonButton("Machine", "Calibrate", "Kerf gauge", Icon, Open)
        {
            Order = 10,
            Tip = "Fits first time: cut a tiny tapered-slot gauge, push its keys in, and a close-up photo reads the kerf and the slot fit, saved on the material for finger joints, slices, dowels and inlays",
        });
        host.AddCommand(new AddonCommand("kerftest.open", "Materials", "Kerf gauge…", "", Open));
        // Show where the close-up camera looks while a gauge is in the design, and open Read it when the job finishes.
        host.ShowCloseUpAreaWhile(() => KerfGauge.Find(host.Document.State) is not null);
        host.JobFinished += Finished;
    }

    void Finished(string? from)
    {
        if (KerfGauge.Find(_host.Document.State) is not null) Dispatcher.UIThread.Post(Open);
    }

    void Open()
    {
        if (_open is { IsVisible: true }) { _open.Activate(); return; }
        var window = new KerfGaugeWindow(_host, KerfGauge.Find(_host.Document.State) is not null);
        _open = window;
        window.Closed += (_, _) => { if (ReferenceEquals(_open, window)) _open = null; };
        _ = _host.Guard(async () =>
        {
            await window.ShowDialog(_host.Owner);
            // Saved readings show up in the program's material lists.
            _host.RefreshLibrary();
        });
    }

    public void Stop()
    {
        _open?.Close();
        _open = null;
    }
}
