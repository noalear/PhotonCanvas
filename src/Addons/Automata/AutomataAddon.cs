using PhotonCanvas.App.Addons;

namespace PhotonCanvas.Addon.Automata;

/// <summary>
/// Home → Draw → Automata, its command and "Edit toy…" on the right-click menu, opening
/// <see cref="AutomataWindow"/>: a crank box whose cut-out figures bob, nod, peck and spin, or a punched strip
/// for a paper-strip music box, or a spring steel comb to play it. The geometry and the checks are in Engine/; the cams, motion view and
/// placement come from the mechanism maker (addon.json "requires" it).
/// </summary>
public sealed class AutomataAddon : IAddon
{
    /// <summary>A crank turning a cam under a little figure (24 × 24).</summary>
    const string Icon = "F1 M3 13 H21 V21 H3 Z M5 15 V19 H19 V15 Z M12 17 m-2 0 a2 2 0 1 0 4 0 a2 2 0 1 0 -4 0 M11.2 9 H12.8 V14.5 H11.2 Z M12 3 a2 2 0 1 1 -0.01 0 Z M9 7.6 H15 V9.2 H9 Z M21 17 H23 V19 H21 Z";

    IAddonHost _host = null!;
    AutomataWindow? _window;

    public void Start(IAddonHost host)
    {
        _host = host;
        host.AddRibbonButton(new RibbonButton("Home", "Draw", "Automata", Icon, Open)
        {
            Tip = "Crank boxes with cut-out figures that bob, nod, peck and spin, punched strips for paper-strip music boxes, and steel combs to play them",
            Large = true,
            Order = 33,
        });
        host.AddCommand(new AddonCommand("automata.open", "Shapes", "Toys and automata…", "", Open));
        host.AddContextMenuItem(new ContextMenuEntry("Edit toy…", Open) { Visible = Selected });
    }

    /// <summary>Is a part of a toy selected (cheap: no settings read).</summary>
    bool Selected()
    {
        var sel = _host.Document.Selection;
        return sel.Count > 0 && _host.Document.State.Shapes.Any(s => sel.Contains(s.Id) && s.AddonData.ContainsKey(ToyPlacement.Target.IdKey));
    }

    /// <summary>Opens the maker: on the toy selected, if one is, to change it; otherwise for a new one.</summary>
    void Open()
    {
        if (_window is not null) { _window.Activate(); return; }
        _ = _host.Guard(async () =>
        {
            var editing = ToyPlacement.Find(_host.Document.State, _host.Document.Selection);
            _window = new AutomataWindow(_host.Document, _host.Machine, _host.Library, _host.DataFolder, editing);
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
