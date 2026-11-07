using Avalonia.Controls;
using Avalonia.Threading;
using PhotonCanvas.App.Addons;

namespace PhotonCanvas.Addon.RecipeTuner;

/// <summary>
/// Materials & Tests → Tests → Recipe tuner: suggests the next settings to try from the ratings in the
/// material library, puts them in the design as a round of tries, and takes the ratings back. When
/// a round finishes burning, the tuner opens on its rating tab. Never sends or burns anything itself.
/// </summary>
public sealed class RecipeTunerAddon : IAddon
{
    IAddonHost _host = null!;
    TriesStore _store = null!;
    Window? _open;

    public void Start(IAddonHost host)
    {
        _host = host;
        _store = TriesStore.Load(host.DataFolder);
        host.AddRibbonButton(new RibbonButton("Tests", "Find the settings", "Recipe tuner", "IcBulb", () => Open(false))
        {
            Large = true,
            Tip = "Suggests the next settings to try: learns from the stars on your recipes (this material's and similar ones'), makes a small round of tries, and takes your ratings back",
            Order = 0,
        });
        host.AddCommand(new AddonCommand("recipe-tuner.open", "Materials", "Recipe tuner…", "", () => Open(false)));
        host.JobFinished += Finished;
    }

    public void Stop()
    {
        _open?.Close();
        _open = null;
    }

    void Finished(string? from)
    {
        if (TuneTries.InDesign(_host.Document.State)) Dispatcher.UIThread.Post(() => Open(true));
    }

    void Open(bool rate) => _ = _host.Guard(async () =>
    {
        if (_open is not null) { _open.Activate(); return; }
        var w = new RecipeTunerWindow(_host, _store, rate);
        _open = w;
        w.Closed += (_, _) => { if (_open == w) _open = null; };
        await w.ShowDialog(_host.Owner);
    });
}
