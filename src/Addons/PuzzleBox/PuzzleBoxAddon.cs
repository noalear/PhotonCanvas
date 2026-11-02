using PhotonCanvas.App.Addons;

namespace PhotonCanvas.Addon.PuzzleBox;

/// <summary>
/// Home → Draw → Puzzle box, its command and "Edit puzzle box…" on the right-click menu, opening
/// <see cref="PuzzleBoxWindow"/>: layered boxes with sliding-panel tops and hidden drawers. It only
/// adds shapes to the design; nothing is sent to the machine.
/// </summary>
public sealed class PuzzleBoxAddon : IAddon
{
    /// <summary>A box seen from the front, its top in sliding panels and a hidden drawer below (24 × 24).</summary>
    const string Icon = "M3 6h18v15H3z M5 8v3h4V8z M10 8v3h4V8z M15 8v3h4V8z M5 13h14v1.2H5z M5 16.5h14v1.6H5z M10.5 18.6h3v1h-3z";

    IAddonHost _host = null!;
    PuzzleBoxWindow? _window;

    public void Start(IAddonHost host)
    {
        _host = host;
        host.AddRibbonButton(new RibbonButton("Home", "Draw", "Puzzle box", Icon, Open)
        {
            Tip = "Boxes with secrets: sliding-panel tops that only come out in the right order, and drawers hidden in the layers",
            Large = true,
            Order = 32,
        });
        host.AddCommand(new AddonCommand("puzzlebox.open", "Shapes", "Puzzle box…", "", Open));
        host.AddContextMenuItem(new ContextMenuEntry("Edit puzzle box…", Open) { Visible = Selected });
    }

    bool Selected()
    {
        var sel = _host.Document.Selection;
        return sel.Count > 0 && _host.Document.State.Shapes.Any(s => sel.Contains(s.Id) && s.AddonData.ContainsKey(Placement.IdKey));
    }

    /// <summary>Opens the maker: on the box selected, if it's one of ours, to change it; otherwise for a new one.</summary>
    void Open()
    {
        if (_window is not null) { _window.Activate(); return; }
        _ = _host.Guard(async () =>
        {
            var editing = Placement.Find(_host.Document.State, _host.Document.Selection);
            _window = new PuzzleBoxWindow(_host.Document, _host.Machine, _host.Library, _host.DataFolder, editing);
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
