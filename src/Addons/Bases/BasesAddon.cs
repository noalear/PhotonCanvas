using PhotonCanvas.App.Addons;

namespace PhotonCanvas.Addon.Bases;

/// <summary>
/// Designer → Minis & games → Bases, its command and "Edit bases…" on the right-click menu, opening
/// <see cref="BasesWindow"/>: textured bases for tabletop figures and movement trays to carry them.
/// It only adds shapes to the design; nothing is sent to the machine.
/// </summary>
public sealed class BasesAddon : IAddon
{
    /// <summary>A movement tray with three round bases in it (24 × 24).</summary>
    const string Icon = "F0 M3 6h18a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H3a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2z M6 8.8a3.2 3.2 0 1 0 0 6.4a3.2 3.2 0 1 0 0-6.4z M12 8.8a3.2 3.2 0 1 0 0 6.4a3.2 3.2 0 1 0 0-6.4z M18 8.8a3.2 3.2 0 1 0 0 6.4a3.2 3.2 0 1 0 0-6.4z M6 9.8a2.2 2.2 0 1 1 0 4.4a2.2 2.2 0 1 1 0-4.4z M12 9.8a2.2 2.2 0 1 1 0 4.4a2.2 2.2 0 1 1 0-4.4z M18 9.8a2.2 2.2 0 1 1 0 4.4a2.2 2.2 0 1 1 0-4.4z";

    IAddonHost _host = null!;
    BasesWindow? _window;

    public void Start(IAddonHost host)
    {
        _host = host;
        host.AddRibbonButton(new RibbonButton("Designer", "Minis & games", "Bases", Icon, Open)
        {
            Tip = "Bases for tabletop figures with cracked earth, rocks, grass, flagstones or deck plates etched on, and movement trays to carry them",
            Large = true,
            Order = 10,
        });
        host.AddCommand(new AddonCommand("bases.open", "Shapes", "Miniature bases…", "", Open));
        host.AddContextMenuItem(new ContextMenuEntry("Edit bases…", Open) { Visible = Selected });
    }

    bool Selected()
    {
        var sel = _host.Document.Selection;
        return sel.Count > 0 && _host.Document.State.Shapes.Any(s => sel.Contains(s.Id) && s.AddonData.ContainsKey(BasePlacement.IdKey));
    }

    /// <summary>Opens the maker: on the bases selected, if they are ours, to change them; otherwise for new ones.</summary>
    void Open()
    {
        if (_window is not null) { _window.Activate(); return; }
        _ = _host.Guard(async () =>
        {
            var editing = BasePlacement.Find(_host.Document.State, _host.Document.Selection);
            _window = new BasesWindow(_host.Document, _host.Machine, _host.Library, _host.DataFolder, editing);
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
