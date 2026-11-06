using PhotonCanvas.App.Addons;

namespace PhotonCanvas.Addon.Ruins;

/// <summary>
/// Designer → Terrain → Ruins, its command and "Edit ruins…" on the right-click menu, opening
/// <see cref="RuinsWindow"/>: broken walls, rubble, fallen stones and broken pillars for tabletop games
/// and models. It only adds shapes to the design; nothing is sent to the machine.
/// </summary>
public sealed class RuinsAddon : IAddon
{
    /// <summary>A broken brick wall with a stone fallen at its foot (24 × 24).</summary>
    const string Icon = "M2 20V9h4V6h4v4h3V8h3v5h3v2h3v5z M3.5 18.5h7v-2.5h-7z M12 18.5h8.5v-2.5H12z M3.5 14.5h3.5v-3.5h-3.5z M8.5 14.5h7v-3h-7z M7.5 9.5h1V7.5h-1z";

    IAddonHost _host = null!;
    RuinsWindow? _window;

    public void Start(IAddonHost host)
    {
        _host = host;
        host.AddRibbonButton(new RibbonButton("Designer", "Terrain", "Ruins", Icon, Open)
        {
            Tip = "Broken wall sections, rubble piles, fallen stones and broken pillars, in the wall maker's textures, for tabletop games and models",
            Large = true,
            Order = 11,
        });
        host.AddCommand(new AddonCommand("ruins.open", "Shapes", "Ruins and rubble…", "", Open));
        host.AddContextMenuItem(new ContextMenuEntry("Edit ruins…", Open) { Visible = Selected });
    }

    bool Selected()
    {
        var sel = _host.Document.Selection;
        return sel.Count > 0 && _host.Document.State.Shapes.Any(s => sel.Contains(s.Id) && s.AddonData.ContainsKey(Placement.IdKey));
    }

    /// <summary>Opens the maker: on the ruins selected, if they are ours, to change them; otherwise for new ones.</summary>
    void Open()
    {
        if (_window is not null) { _window.Activate(); return; }
        _ = _host.Guard(async () =>
        {
            var editing = Placement.Find(_host.Document.State, _host.Document.Selection);
            _window = new RuinsWindow(_host.Document, _host.Machine, _host.Library, _host.DataFolder, editing);
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
