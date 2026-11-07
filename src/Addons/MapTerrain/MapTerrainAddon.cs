using PhotonCanvas.App.Addons;

namespace PhotonCanvas.Addon.MapTerrain;

/// <summary>
/// Mini-Designer → Town → Map to terrain, its command and "Edit map terrain…" on the right-click menu, opening
/// <see cref="MapWindow"/>: draw a floor plan or read a battle map, and get every wall, column, door
/// and floor tile for it from the wall, floor tile and furniture makers. It only adds shapes to the
/// design; nothing is sent to the machine.
/// </summary>
public sealed class MapTerrainAddon : IAddon
{
    /// <summary>A floor plan: an outline with walls inside it (24 × 24).</summary>
    const string Icon = "F0 M2.5 2.5h19v19h-19z M4.5 4.5v15h15v-15z M10 4.5h2v7h-2z M12 13.5h7.5v2H12z M4.5 11.5h3v2h-3z";

    IAddonHost _host = null!;
    MapWindow? _window;

    public void Start(IAddonHost host)
    {
        _host = host;
        host.AddRibbonButton(new RibbonButton("Mini-Designer", "Town", "Map to terrain", Icon, Open)
        {
            Tip = "Draw a floor plan or read a battle map, and get every wall, column, door and floor tile for it",
            Large = true,
            Order = 11,
        });
        host.AddCommand(new AddonCommand("mapterrain.open", "Shapes", "Map to terrain…", "", Open));
        host.AddContextMenuItem(new ContextMenuEntry("Edit map terrain…", Open) { Visible = Selected });
    }

    bool Selected()
    {
        var sel = _host.Document.Selection;
        return sel.Count > 0 && _host.Document.State.Shapes.Any(s => sel.Contains(s.Id) && s.AddonData.ContainsKey(MapPlacement.IdKey));
    }

    /// <summary>Opens the maker: on the terrain selected, if it's one of ours, to change it; otherwise for a new one.</summary>
    void Open()
    {
        if (_window is not null) { _window.Activate(); return; }
        _ = _host.Guard(async () =>
        {
            var editing = MapPlacement.Find(_host.Document.State, _host.Document.Selection);
            _window = new MapWindow(_host.Document, _host.Machine, _host.Library, _host.DataFolder, editing);
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
