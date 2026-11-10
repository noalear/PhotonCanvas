using PhotonCanvas.App.Addons;
using PhotonCanvas.Doc;

namespace PhotonCanvas.Addon.Inlay;

/// <summary>
/// Home → Cut lines → Inlay (and Materials &amp; Tests → Tests → Inlay and marquetry, and the right-click
/// item), opening <see cref="InlayWindow"/>: the selected shapes as pieces of different woods that fit
/// together, and a fit test to find the gap. The geometry is in Engine/.
/// </summary>
public sealed class InlayAddon : IAddon
{
    /// <summary>A board with a star set into it, the star's piece lifted out beside it (24 × 24).</summary>
    const string Icon = "F0 M2 6 H16 V20 H2Z M9 8.6 L10.2 11.6 L13.4 11.8 L10.9 13.8 L11.8 16.9 L9 15.2 L6.2 16.9 L7.1 13.8 L4.6 11.8 L7.8 11.6Z "
        + "M18.5 2 L19.5 4.4 L22 4.6 L20.1 6.2 L20.7 8.7 L18.5 7.4 L16.3 8.7 L16.9 6.2 L15 4.6 L17.5 4.4Z";

    IAddonHost _host = null!;
    InlayWindow? _window;

    public void Start(IAddonHost host)
    {
        _host = host;
        host.AddRibbonButton(new RibbonButton("Home", "Cut lines", "Inlay", Icon, Open)
        {
            Tip = "Inlay and marquetry: the selected shapes as pieces of different woods (a wood per layer), each cut with its kerf allowed for, and a pocket or holes they fit into",
            Order = 30,
        });
        host.AddRibbonButton(new RibbonButton("Tests", "Use your results", "Inlay and marquetry", Icon, Open)
        {
            Tip = "Pieces of different woods that fit: the pocket depth from your etch-rate test, each wood's kerf from its cut recipe, and a fit test for the gap",
        });
        host.AddCommand(new AddonCommand("inlay.open", "Shapes", "Inlay and marquetry…", "", Open));
        host.AddContextMenuItem(new ContextMenuEntry("Inlay and marquetry…", Open) { Visible = HasShapes });
    }

    bool HasShapes()
    {
        var sel = _host.Document.Selection;
        return sel.Count > 0 && _host.Document.State.Shapes.Any(s => sel.Contains(s.Id) && s is PathShape);
    }

    void Open()
    {
        if (_window is not null) { _window.Activate(); return; }
        _ = _host.Guard(async () =>
        {
            _window = new InlayWindow(_host.Document, _host.Library, _host.Machine, _host.DataFolder, _host.Status);
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
