using PhotonCanvas.App.Addons;

namespace PhotonCanvas.Addon.Scatter;

/// <summary>
/// Home → Draw → Scatter, its command and "Edit scatter terrain…" on the right-click menu, opening
/// <see cref="ScatterWindow"/>: barrels, fences, carts, market stalls, wells and gravestones for
/// tabletop games. It only adds shapes to the design; nothing is sent to the machine.
/// </summary>
public sealed class ScatterAddon : IAddon
{
    /// <summary>A barrel and a bit of fence (24 × 24).</summary>
    const string Icon = "M4.5 4h6c1 2 1.5 4.5 1.5 8s-.5 6-1.5 8h-6c-1-2-1.5-4.5-1.5-8s.5-6 1.5-8z M14.5 9l1.25-2 1.25 2v11h-2.5z M19 9l1.25-2 1.25 2v11H19z M14 11.5h8v1.5h-8z M14 16h8v1.5h-8z";

    IAddonHost _host = null!;
    ScatterWindow? _window;

    public void Start(IAddonHost host)
    {
        _host = host;
        host.AddRibbonButton(new RibbonButton("Home", "Draw", "Scatter", Icon, Open)
        {
            Tip = "Barrels, fences, carts, market stalls, wells and gravestones that slot together, for tabletop games and models",
            Large = true,
            Order = 23,
        });
        host.AddCommand(new AddonCommand("scatter.open", "Shapes", "Scatter terrain…", "", Open));
        host.AddContextMenuItem(new ContextMenuEntry("Edit scatter terrain…", Open) { Visible = Selected });
    }

    bool Selected()
    {
        var sel = _host.Document.Selection;
        return sel.Count > 0 && _host.Document.State.Shapes.Any(s => sel.Contains(s.Id) && s.AddonData.ContainsKey(Placement.IdKey));
    }

    /// <summary>Opens the maker: on the scenery selected, if it's ours, to change it; otherwise for a new set.</summary>
    void Open()
    {
        if (_window is not null) { _window.Activate(); return; }
        _ = _host.Guard(async () =>
        {
            var editing = Placement.Find(_host.Document.State, _host.Document.Selection);
            _window = new ScatterWindow(_host.Document, _host.Machine, _host.Library, _host.DataFolder, editing);
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
