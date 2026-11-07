using PhotonCanvas.App.Addons;

namespace PhotonCanvas.Addon.Furniture;

/// <summary>
/// Mini-Designer → Furniture & props → Furniture, its command and "Edit furniture…" on the right-click menu, opening
/// <see cref="FurnitureWindow"/>: flat-pack furniture for tabletop games and models. It only adds
/// shapes to the design; nothing is sent to the machine.
/// </summary>
public sealed class FurnitureAddon : IAddon
{
    /// <summary>A table and a chair (24 × 24).</summary>
    const string Icon = "M2 8h12v2.2H2z M3 10.2h2v9.3H3z M11 10.2h2v9.3h-2z M16.5 4h2v15.5h-2z M16.5 12h5.5v2.2h-5.5z M20 14.2h2v5.3h-2z";

    IAddonHost _host = null!;
    FurnitureWindow? _window;

    public void Start(IAddonHost host)
    {
        _host = host;
        host.AddRibbonButton(new RibbonButton("Mini-Designer", "Furniture & props", "Furniture", Icon, Open)
        {
            Tip = "Tables, benches, stools, chairs, bookshelves, chests, crates and beds that slot together, and doors and shelves for walls, for tabletop games and models",
            Large = true,
            Order = 10,
        });
        host.AddCommand(new AddonCommand("furniture.open", "Shapes", "Mini furniture…", "", Open));
        host.AddContextMenuItem(new ContextMenuEntry("Edit furniture…", Open) { Visible = Selected });
    }

    bool Selected()
    {
        var sel = _host.Document.Selection;
        return sel.Count > 0 && _host.Document.State.Shapes.Any(s => sel.Contains(s.Id) && s.AddonData.ContainsKey(Placement.IdKey));
    }

    /// <summary>Opens the maker: on the furniture selected, if it's ours, to change it; otherwise for a new set.</summary>
    void Open()
    {
        if (_window is not null) { _window.Activate(); return; }
        _ = _host.Guard(async () =>
        {
            var editing = Placement.Find(_host.Document.State, _host.Document.Selection);
            _window = new FurnitureWindow(_host.Document, _host.Machine, _host.Library, _host.DataFolder, editing);
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
