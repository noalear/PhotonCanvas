using PhotonCanvas.App.Addons;

namespace PhotonCanvas.Addon.Buildings;

/// <summary>
/// Mini-Designer → Buildings → Buildings, its command and "Edit building…" on the right-click menu, opening
/// <see cref="BuildingWindow"/>: a whole building (cottage, tavern, watchtower) made at once with the
/// wall maker and the furniture maker. It only adds shapes to the design; nothing is sent to the machine.
/// </summary>
public sealed class BuildingsAddon : IAddon
{
    /// <summary>A house with a door and a window (24 × 24).</summary>
    const string Icon = "F0 M12 2.5L1.5 11h2.5v10h16V11h2.5z M10 21v-5.5h4V21z M6.5 12.5h3v3h-3z M14.5 12.5h3v3h-3z";

    IAddonHost _host = null!;
    BuildingWindow? _window;

    public void Start(IAddonHost host)
    {
        _host = host;
        host.AddRibbonButton(new RibbonButton("Mini-Designer", "Buildings", "Buildings", Icon, Open)
        {
            Tip = "A whole cottage, tavern or watchtower at once: every wall, column, floor, door and piece of furniture, sized for your figures",
            Large = true,
            Order = 11,
        });
        host.AddCommand(new AddonCommand("buildings.open", "Shapes", "Whole buildings…", "", Open));
        host.AddContextMenuItem(new ContextMenuEntry("Edit building…", Open) { Visible = Selected });
    }

    bool Selected()
    {
        var sel = _host.Document.Selection;
        return sel.Count > 0 && _host.Document.State.Shapes.Any(s => sel.Contains(s.Id) && s.AddonData.ContainsKey(Placement.IdKey));
    }

    /// <summary>Opens the maker: on the building selected, if it's one of ours, to change it; otherwise for a new one.</summary>
    void Open()
    {
        if (_window is not null) { _window.Activate(); return; }
        _ = _host.Guard(async () =>
        {
            var editing = Placement.Find(_host.Document.State, _host.Document.Selection);
            _window = new BuildingWindow(_host.Document, _host.Machine, _host.Library, _host.DataFolder, editing);
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
