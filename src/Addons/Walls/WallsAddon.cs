using PhotonCanvas.App.Addons;

namespace PhotonCanvas.Addon.Walls;

/// <summary>
/// Mini-Designer → Buildings → Walls, its command and "Edit walls…" on the right-click menu, opening
/// <see cref="WallsWindow"/>: textured wall panels for tabletop games and models, and the columns that
/// join them. It only adds shapes to the design; nothing is sent to the machine.
/// </summary>
public sealed class WallsAddon : IAddon
{
    /// <summary>A brick wall (24 × 24).</summary>
    const string Icon = "F0 M2 4h20v16H2z M3.5 5.5v3.25h7.75V5.5z M12.75 5.5v3.25h7.75V5.5z M3.5 10.25v3.5h3.5v-3.5z M8.5 10.25v3.5h7v-3.5z M17 10.25v3.5h3.5v-3.5z M3.5 15.25v3.25h7.75v-3.25z M12.75 15.25v3.25h7.75v-3.25z";

    IAddonHost _host = null!;
    WallsWindow? _window;

    public void Start(IAddonHost host)
    {
        _host = host;
        host.AddRibbonButton(new RibbonButton("Mini-Designer", "Buildings", "Walls", Icon, Open)
        {
            Tip = "Wall panels with brick, stone, wood or your own texture etched on, doors, windows, battlements and mount points, and columns to join them",
            Large = true,
            Order = 12,
        });
        host.AddCommand(new AddonCommand("walls.open", "Shapes", "Textured walls…", "", Open));
        host.AddContextMenuItem(new ContextMenuEntry("Edit walls…", Open) { Visible = Selected });
    }

    bool Selected()
    {
        var sel = _host.Document.Selection;
        return sel.Count > 0 && _host.Document.State.Shapes.Any(s => sel.Contains(s.Id) && s.AddonData.ContainsKey(Placement.IdKey));
    }

    /// <summary>Opens the maker: on the walls selected, if they are ours, to change them; otherwise for new ones.</summary>
    void Open()
    {
        if (_window is not null) { _window.Activate(); return; }
        _ = _host.Guard(async () =>
        {
            var editing = Placement.Find(_host.Document.State, _host.Document.Selection);
            _window = new WallsWindow(_host.Document, _host.Machine, _host.Library, _host.DataFolder, editing);
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
