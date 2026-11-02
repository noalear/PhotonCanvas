using PhotonCanvas.App.Addons;

namespace PhotonCanvas.Addon.Stairs;

/// <summary>
/// Home → Draw → Stairs &amp; floors, its command and "Edit floor and stairs…" on the right-click menu,
/// opening <see cref="StairsWindow"/>: an upper floor that sits on the wall maker's walls, and stairs or
/// a ladder up to it. It only adds shapes to the design; nothing is sent to the machine.
/// </summary>
public sealed class StairsAddon : IAddon
{
    /// <summary>A flight of stairs up to a floor (24 × 24).</summary>
    const string Icon = "M2 20h4v-3.5h4V13h4V9.5h4V6h4v2h-2v3.5h-4V15h-4v3.5H8V22H2z M2 3h20v1.6H2z";

    IAddonHost _host = null!;
    StairsWindow? _window;

    public void Start(IAddonHost host)
    {
        _host = host;
        host.AddRibbonButton(new RibbonButton("Home", "Draw", "Stairs & floors", Icon, Open)
        {
            Tip = "An upper floor that sits on your walls, with an opening, and stairs (open, solid or spiral) or a ladder up to it",
            Large = true,
            Order = 23,
        });
        host.AddCommand(new AddonCommand("stairs.open", "Shapes", "Floors, stairs and ladders…", "", Open));
        host.AddContextMenuItem(new ContextMenuEntry("Edit floor and stairs…", Open) { Visible = Selected });
    }

    bool Selected()
    {
        var sel = _host.Document.Selection;
        return sel.Count > 0 && _host.Document.State.Shapes.Any(s => sel.Contains(s.Id) && s.AddonData.ContainsKey(Placement.IdKey));
    }

    /// <summary>Opens the maker: on the floor selected, if it's ours, to change it; otherwise for a new one.</summary>
    void Open()
    {
        if (_window is not null) { _window.Activate(); return; }
        _ = _host.Guard(async () =>
        {
            var editing = Placement.Find(_host.Document.State, _host.Document.Selection);
            _window = new StairsWindow(_host.Document, _host.Machine, _host.Library, _host.DataFolder, editing);
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
