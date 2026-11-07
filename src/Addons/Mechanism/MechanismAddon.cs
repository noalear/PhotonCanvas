using PhotonCanvas.App.Addons;

namespace PhotonCanvas.Addon.Mechanism;

/// <summary>
/// Import → Generate → Mechanisms, its command and "Edit mechanism…" on the right-click menu, opening
/// <see cref="MechanismWindow"/>: gears, a rack and pinion, planetary gears, a cam and follower or an
/// automaton box, checked moving before they're cut. The geometry and the motion check are in Engine/.
/// </summary>
public sealed class MechanismAddon : IAddon
{
    /// <summary>Two gears meshing (24 × 24).</summary>
    const string Icon = "F0 M15.05 10.94 L16.66 12.1 L15.93 13.53 L14.04 12.91 L13.51 13.51 L12.91 14.04 L13.53 15.93 L12.1 16.66 L10.94 15.05 L10.15 15.22 L9.35 15.3 L8.75 17.19 L7.16 16.94 L7.17 14.95 L6.43 14.64 L5.74 14.22 L4.14 15.4 L3 14.26 L4.18 12.66 L3.76 11.97 L3.45 11.23 L1.46 11.24 L1.21 9.65 L3.1 9.05 L3.18 8.25 L3.35 7.46 L1.74 6.3 L2.47 4.87 L4.36 5.49 L4.89 4.89 L5.49 4.36 L4.87 2.47 L6.3 1.74 L7.46 3.35 L8.25 3.18 L9.05 3.1 L9.65 1.21 L11.24 1.46 L11.23 3.45 L11.97 3.76 L12.66 4.18 L14.26 3 L15.4 4.14 L14.22 5.74 L14.64 6.43 L14.95 7.17 L16.94 7.16 L17.19 8.75 L15.3 9.35 L15.22 10.15Z M9.2 6.8a2.4 2.4 0 1 0 0 4.8a2.4 2.4 0 1 0 0 -4.8z M20.84 19.38 L21.82 20.8 L20.8 21.82 L19.38 20.84 L18.56 21.17 L17.68 21.3 L16.94 22.86 L15.55 22.49 L15.68 20.76 L14.98 20.22 L14.44 19.52 L12.71 19.65 L12.34 18.26 L13.9 17.52 L14.03 16.64 L14.36 15.82 L13.38 14.4 L14.4 13.38 L15.82 14.36 L16.64 14.03 L17.52 13.9 L18.26 12.34 L19.65 12.71 L19.52 14.44 L20.22 14.98 L20.76 15.68 L22.49 15.55 L22.86 16.94 L21.3 17.68 L21.17 18.56Z M17.6 16.2a1.4 1.4 0 1 0 0 2.8a1.4 1.4 0 1 0 0 -2.8z";

    IAddonHost _host = null!;
    MechanismWindow? _window;

    public void Start(IAddonHost host)
    {
        _host = host;
        host.AddRibbonButton(new RibbonButton("Import", "Generate", "Mechanisms", Icon, Open)
        {
            Tip = "Gears, a rack and pinion, planetary gears, cams and automaton boxes, checked moving before you cut them",
            Large = true,
            Order = 11,
        });
        host.AddCommand(new AddonCommand("mechanism.open", "Shapes", "Mechanism maker…", "", Open));
        host.AddContextMenuItem(new ContextMenuEntry("Edit mechanism…", Open) { Visible = Selected });
    }

    /// <summary>Is a part of a mechanism selected (cheap: no settings read).</summary>
    bool Selected()
    {
        var sel = _host.Document.Selection;
        return sel.Count > 0 && _host.Document.State.Shapes.Any(s => sel.Contains(s.Id) && s.AddonData.ContainsKey(Placement.IdKey));
    }

    /// <summary>Opens the maker: on the mechanism selected, if one is, to change it; otherwise for a new one.</summary>
    void Open()
    {
        if (_window is not null) { _window.Activate(); return; }
        _ = _host.Guard(async () =>
        {
            var editing = Placement.Find(_host.Document.State, _host.Document.Selection);
            _window = new MechanismWindow(_host.Document, _host.Machine, _host.Library, _host.DataFolder, editing);
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
