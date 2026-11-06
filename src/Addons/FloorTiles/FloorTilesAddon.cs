using PhotonCanvas.App.Addons;

namespace PhotonCanvas.Addon.FloorTiles;

/// <summary>
/// Designer → Terrain → Floor tiles, its command and "Edit floor tiles…" on the right-click menu, opening
/// <see cref="FloorTilesWindow"/>: textured floor tiles for tabletop games that lock together on a
/// square grid. It only adds shapes to the design; nothing is sent to the machine.
/// </summary>
public sealed class FloorTilesAddon : IAddon
{
    /// <summary>Four flagstones fitted together with jigsaw tabs (24 × 24).</summary>
    const string Icon = "F0 M3 3h8v3.2a1.6 1.6 0 1 0 0 1.6V11H7.8a1.6 1.6 0 1 1-1.6 0H3z M13 3h8v8h-3.2a1.6 1.6 0 1 0-1.6 0H13V7.8a1.6 1.6 0 1 1 0-1.6z M3 13h3.2a1.6 1.6 0 1 0 1.6 0H11v3.2a1.6 1.6 0 1 1 0 1.6V21H3z M13 13h3.2a1.6 1.6 0 1 1 1.6 0H21v8h-8v-3.2a1.6 1.6 0 1 0 0-1.6z";

    IAddonHost _host = null!;
    FloorTilesWindow? _window;

    public void Start(IAddonHost host)
    {
        _host = host;
        host.AddRibbonButton(new RibbonButton("Designer", "Terrain", "Floor tiles", Icon, Open)
        {
            Tip = "Floor tiles for tabletop games with flagstones, floorboards, cobbles or your own texture etched on, that lock together on a square grid",
            Large = true,
            Order = 10,
        });
        host.AddCommand(new AddonCommand("floortiles.open", "Shapes", "Floor tiles…", "", Open));
        host.AddContextMenuItem(new ContextMenuEntry("Edit floor tiles…", Open) { Visible = Selected });
    }

    bool Selected()
    {
        var sel = _host.Document.Selection;
        return sel.Count > 0 && _host.Document.State.Shapes.Any(s => sel.Contains(s.Id) && s.AddonData.ContainsKey(TilePlacement.IdKey));
    }

    /// <summary>Opens the maker: on the tiles selected, if they are ours, to change them; otherwise for new ones.</summary>
    void Open()
    {
        if (_window is not null) { _window.Activate(); return; }
        _ = _host.Guard(async () =>
        {
            var editing = TilePlacement.Find(_host.Document.State, _host.Document.Selection);
            _window = new FloorTilesWindow(_host.Document, _host.Machine, _host.Library, _host.DataFolder, editing);
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
