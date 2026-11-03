using PhotonCanvas.App.Addons;

namespace PhotonCanvas.Addon.HouseEditor;

/// <summary>
/// Home → Draw → Build, its command and "Edit in the building editor…" on the right-click menu, opening
/// <see cref="HouseEditorWindow"/>: a building drawn floor by floor on a grid, its walls, columns, floors
/// and tiles made by the wall maker and the whole-building kits. It only adds shapes to the design;
/// nothing is sent to the machine.
/// </summary>
public sealed class HouseEditorAddon : IAddon
{
    /// <summary>A floor plan: an outer wall, an inner wall with a doorway, a door (24 × 24).</summary>
    const string Icon = "F0 M2 2h20v20H2z M4 4v16h16V4z M11 4h2v6h-2z M11 14h2v6h-2z M4 15h3v2H4z";

    IAddonHost _host = null!;
    HouseEditorWindow? _window;

    public void Start(IAddonHost host)
    {
        _host = host;
        host.AddRibbonButton(new RibbonButton("Home", "Draw", "Build", Icon, Open)
        {
            Tip = "Draw a building floor by floor, like a house in a life-sim game: rooms, walls at any angle, doors and windows anywhere, then cut every piece",
            Large = true,
            Order = 26,
        });
        host.AddCommand(new AddonCommand("houseeditor.open", "Shapes", "Building editor…", "", Open));
        host.AddContextMenuItem(new ContextMenuEntry("Edit in the building editor…", Open) { Visible = Selected });
    }

    bool Selected()
    {
        var sel = _host.Document.Selection;
        return sel.Count > 0 && _host.Document.State.Shapes.Any(s => sel.Contains(s.Id) && s.AddonData.ContainsKey(HousePlacement.IdKey));
    }

    /// <summary>Opens the editor: on the building selected, if it's one of ours, to change it; otherwise on the last one.</summary>
    void Open()
    {
        if (_window is not null) { _window.Activate(); return; }
        _ = _host.Guard(async () =>
        {
            var editing = HousePlacement.Find(_host.Document.State, _host.Document.Selection);
            _window = new HouseEditorWindow(_host.Document, _host.Machine, _host.Library, _host.DataFolder, editing);
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
