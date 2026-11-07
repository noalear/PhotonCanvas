using PhotonCanvas.App.Addons;

namespace PhotonCanvas.Addon.Roofs;

/// <summary>
/// Mini-Designer → Buildings → Roofs and Towers, their commands and "Edit roof or tower…" on the right-click menu,
/// opening <see cref="RoofsWindow"/>: roofs that lift off the wall maker's buildings, and round towers
/// stacked from rings. It only adds shapes to the design; nothing is sent to the machine.
/// </summary>
public sealed class RoofsAddon : IAddon
{
    /// <summary>A house's roof with shingles (24 × 24).</summary>
    const string RoofIcon = "F0 M12 2.5L22.5 11.5l-1.3 1.5L12 5.1 2.8 13 1.5 11.5z M5 12.2l7-6 7 6V21H5z M7 13.2V19h3.5v-4h3v4H17v-5.8l-5-4.3z";
    /// <summary>A round tower with battlements (24 × 24).</summary>
    const string TowerIcon = "F0 M5 2h3v2h2.5V2h3v2H16V2h3v5h-1.5v14h-11V7H5z M7 6v-.5h10V6z M8.5 8h7v11h-2v-3.5a1.5 1.5 0 0 0-3 0V19h-2z M11.3 10h1.4v3h-1.4z";

    IAddonHost _host = null!;
    RoofsWindow? _window;

    public void Start(IAddonHost host)
    {
        _host = host;
        host.AddRibbonButton(new RibbonButton("Mini-Designer", "Buildings", "Roofs", RoofIcon, () => Open(tower: false))
        {
            Tip = "Pitched, hipped, lean-to and cone roofs with shingles, slates, tiles or thatch etched on, made to lift off buildings from Walls",
            Large = true,
            Order = 13,
        });
        host.AddRibbonButton(new RibbonButton("Mini-Designer", "Buildings", "Towers", TowerIcon, () => Open(tower: true))
        {
            Tip = "Round towers stacked from rings, with stonework, floors, a door, windows, battlements or a cone roof, and slots for walls",
            Large = true,
            Order = 14,
        });
        host.AddCommand(new AddonCommand("roofs.open", "Shapes", "Roofs…", "", () => Open(tower: false)));
        host.AddCommand(new AddonCommand("roofs.tower", "Shapes", "Round towers…", "", () => Open(tower: true)));
        host.AddContextMenuItem(new ContextMenuEntry("Edit roof or tower…", () => Open(tower: false)) { Visible = Selected });
    }

    bool Selected()
    {
        var sel = _host.Document.Selection;
        return sel.Count > 0 && _host.Document.State.Shapes.Any(s => sel.Contains(s.Id) && s.AddonData.ContainsKey(Placement.IdKey));
    }

    /// <summary>Opens the maker: on the roof or tower selected, if it's ours, to change it; otherwise for a new one.</summary>
    void Open(bool tower)
    {
        if (_window is not null) { _window.Activate(); return; }
        _ = _host.Guard(async () =>
        {
            var editing = Placement.Find(_host.Document.State, _host.Document.Selection);
            _window = new RoofsWindow(_host.Document, _host.Machine, _host.Library, _host.DataFolder, editing, editing is { } e ? e.Spec.Tower is not null : tower);
            try { await _window.ShowDialog(_host.Owner); }
            finally { _window = null; }
        });
    }

    public void Stop()
    {
        _window?.Close();
        _window = null;
    }
}
