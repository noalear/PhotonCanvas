using PhotonCanvas.App.Addons;

namespace PhotonCanvas.Addon.GameKit;

/// <summary>
/// Mini-Designer → Accessories → Game kit, its command and "Edit game accessories…" on the right-click menu, opening
/// <see cref="GameKitWindow"/>: dice towers, card stands, deck trays, initiative trackers, movement
/// gauges, rulers and templates for tabletop games. It only adds shapes to the design; nothing is sent
/// to the machine.
/// </summary>
public sealed class GameKitAddon : IAddon
{
    /// <summary>A die on a tower with battlements (24 × 24).</summary>
    const string Icon = "M4 3h3v2h2V3h3v2h2V3h3v18H4z M7.5 13.5h6v6h-6z M14 13.5h5.5v6H14z M9.2 15.2a.9.9 0 1 0 .01 0z M11.8 17.8a.9.9 0 1 0 .01 0z";

    IAddonHost _host = null!;
    GameKitWindow? _window;

    public void Start(IAddonHost host)
    {
        _host = host;
        host.AddRibbonButton(new RibbonButton("Mini-Designer", "Accessories", "Game kit", Icon, Open)
        {
            Tip = "Dice towers, card stands, deck trays, initiative trackers, movement gauges, range rulers and area templates for tabletop games",
            Large = true,
            Order = 11,
        });
        host.AddCommand(new AddonCommand("gamekit.open", "Shapes", "Game accessories…", "", Open));
        host.AddContextMenuItem(new ContextMenuEntry("Edit game accessories…", Open) { Visible = Selected });
    }

    bool Selected()
    {
        var sel = _host.Document.Selection;
        return sel.Count > 0 && _host.Document.State.Shapes.Any(s => sel.Contains(s.Id) && s.AddonData.ContainsKey(Placement.IdKey));
    }

    /// <summary>Opens the maker: on the accessories selected, if they're ours, to change them; otherwise for a new set.</summary>
    void Open()
    {
        if (_window is not null) { _window.Activate(); return; }
        _ = _host.Guard(async () =>
        {
            var editing = Placement.Find(_host.Document.State, _host.Document.Selection);
            _window = new GameKitWindow(_host.Document, _host.Machine, _host.Library, _host.DataFolder, editing);
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
