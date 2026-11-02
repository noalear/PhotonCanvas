using PhotonCanvas.App.Addons;
using PhotonCanvas.Doc;

namespace PhotonCanvas.Addon.Stencil;

/// <summary>Home → Outline → Stencil bridges, its command and right-click items, opening <see cref="StencilWindow"/>.</summary>
public sealed class StencilAddon : IAddon
{
    /// <summary>An O cut through at the top and bottom: two half rings.</summary>
    const string IconPath = "M13 3.06A9 9 0 0 1 13 20.94V17.92A6 6 0 0 0 13 6.08ZM11 3.06A9 9 0 0 0 11 20.94V17.92A6 6 0 0 1 11 6.08Z";

    IAddonHost _host = null!;
    StencilWindow? _window;

    public void Start(IAddonHost host)
    {
        _host = host;
        host.AddRibbonButton(new RibbonButton("Home", "Outline", "Stencil bridges", IconPath, Open)
        {
            Tip = "Keep the middles of O, A, R and B in a cut-out stencil: short bridges across the letters, placed for you",
            Enabled = HasLetters,
        });
        host.AddCommand(new AddonCommand("stencil.bridges", "Shapes", "Stencil bridges…", "", Open));
        host.AddCommand(new AddonCommand("stencil.takeout", "Shapes", "Take stencil bridges out", "", TakeOut));
        host.AddContextMenuItem(new ContextMenuEntry("Stencil bridges…", Open) { Visible = HasLetters });
        host.AddContextMenuItem(new ContextMenuEntry("Take stencil bridges out", TakeOut) { Visible = HasBridged });
    }

    bool HasLetters() => StencilEdit.Selected(_host.Document.State, _host.Document.Selection).Count > 0;

    bool HasBridged() => _host.Document.State.Shapes.Any(s => _host.Document.Selection.Contains(s.Id) && StencilEdit.IsBridged(s));

    void Open()
    {
        if (!HasLetters()) { _host.Status("Select the letters to cut as a stencil first (a text, or shapes with holes)."); return; }
        _ = _host.Guard(async () =>
        {
            var input = StencilEdit.Gather(_host.Document.State, _host.Document.Selection);
            _window = new StencilWindow(_host.Document, input, _host.DataFolder, _host.Status);
            try { await _window.ShowDialog(_host.Owner); }
            finally { _window = null; }
        });
    }

    void TakeOut()
    {
        var doc = _host.Document;
        var ids = doc.State.Shapes.Where(s => doc.Selection.Contains(s.Id) && StencilEdit.IsBridged(s)).Select(s => s.Id).ToList();
        if (ids.Count == 0) { _host.Status("None of the selected shapes has stencil bridges."); return; }
        doc.Commit("Take stencil bridges out", StencilEdit.TakeOut(doc.State, ids));
        _host.Status($"Took the bridges out of {ids.Count} shape{(ids.Count == 1 ? "" : "s")}: they're back as they were.");
    }

    public void Stop() => _window?.Close();
}
