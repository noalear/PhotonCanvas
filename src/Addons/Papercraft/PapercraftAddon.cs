using PhotonCanvas.App.Addons;

namespace PhotonCanvas.Addon.Papercraft;

/// <summary>Fold-up papercraft: one button (Home → Bring in) and a command that open the papercraft window.</summary>
public sealed class PapercraftAddon : IAddon
{
    IAddonHost? _host;
    PapercraftWindow? _open;

    public void Start(IAddonHost host)
    {
        _host = host;
        const string tip = "Unfold a 3D model (STL, OBJ, STEP) into flat pieces of card with cut lines, fold lines, glue tabs and numbered edges, to fold up into the model";
        host.AddRibbonButton(new RibbonButton("Home", "Bring in", "3D to papercraft", "IcPrism", Open) { Tip = tip });
        host.AddCommand(new AddonCommand("papercraft.open", "Shapes", "3D to papercraft…", "", Open));
    }

    void Open()
    {
        if (_host is not { } host) return;
        if (_open is { } already) { already.Activate(); return; }
        _ = host.Guard(async () =>
        {
            var w = new PapercraftWindow(host.Document, host.Machine, host.AppSettings);
            _open = w;
            w.Closed += (_, _) => { if (_open == w) _open = null; };
            await w.ShowDialog(host.Owner);
        });
    }

    public void Stop()
    {
        _open?.Close();
        _open = null;
        _host = null;
    }
}
