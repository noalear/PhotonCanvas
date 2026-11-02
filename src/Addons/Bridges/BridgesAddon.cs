using PhotonCanvas.App.Addons;

namespace PhotonCanvas.Addon.Bridges;

/// <summary>
/// Home → Draw → Bridges, its command and "Edit bridge…" on the right-click menu, opening
/// <see cref="BridgesWindow"/>: plank, rope and stone arch bridges for tabletop games and models. It only
/// adds shapes to the design; nothing is sent to the machine.
/// </summary>
public sealed class BridgesAddon : IAddon
{
    /// <summary>An arch bridge with a railing (24 × 24).</summary>
    const string Icon = "M1 13h22v2.2h-1.6C20.6 11.9 16.6 9.6 12 9.6S3.4 11.9 2.6 15.2H1z M3 6.5h1.8v6.3H3z M19.2 6.5H21v6.3h-1.8z M11.1 6.5h1.8v3H11.1z M3 6.5h18v1.6H3z M1 19h22v1.6H1z";

    IAddonHost _host = null!;
    BridgesWindow? _window;

    public void Start(IAddonHost host)
    {
        _host = host;
        host.AddRibbonButton(new RibbonButton("Home", "Draw", "Bridges", Icon, Open)
        {
            Tip = "Plank footbridges, sagging rope bridges and stone arch bridges that slot together, for tabletop games and models",
            Large = true,
            Order = 24,
        });
        host.AddCommand(new AddonCommand("bridges.open", "Shapes", "Miniature bridges…", "", Open));
        host.AddContextMenuItem(new ContextMenuEntry("Edit bridge…", Open) { Visible = Selected });
    }

    bool Selected()
    {
        var sel = _host.Document.Selection;
        return sel.Count > 0 && _host.Document.State.Shapes.Any(s => sel.Contains(s.Id) && s.AddonData.ContainsKey(Placement.IdKey));
    }

    /// <summary>Opens the maker: on the bridge selected, if it's ours, to change it; otherwise for a new one.</summary>
    void Open()
    {
        if (_window is not null) { _window.Activate(); return; }
        _ = _host.Guard(async () =>
        {
            var editing = Placement.Find(_host.Document.State, _host.Document.Selection);
            _window = new BridgesWindow(_host.Document, _host.Machine, _host.Library, _host.DataFolder, editing);
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
