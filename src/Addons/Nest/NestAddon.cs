using PhotonCanvas.App.Addons;

namespace PhotonCanvas.Addon.Nest;

/// <summary>Arrange → Repeat → Nest, its shortcut (Ctrl+Alt+N) and the right-click item, opening <see cref="NestWindow"/>.</summary>
public sealed class NestAddon : IAddon
{
    IAddonHost _host = null!;

    public void Start(IAddonHost host)
    {
        _host = host;
        host.AddRibbonButton(new RibbonButton("Arrange", "Repeat", "Nest", "IcNest", Open)
        {
            Tip = "Pack copies or parts onto the work area or a piece, as tightly as they fit (Ctrl+Alt+N)",
            Large = true,
            Enabled = () => host.Document.Selection.Count > 0,
        });
        host.AddCommand(new AddonCommand("shape.nest", "Shapes", "Nest…", "Ctrl+Alt+N", Open));
        host.AddContextMenuItem(new ContextMenuEntry("Nest on material…", Open) { Visible = () => host.Document.Selection.Count > 0, Gesture = "Ctrl+Alt+N" });
    }

    void Open()
    {
        if (_host.Document.Selection.Count == 0) return;
        _ = _host.Guard(() => new NestWindow(_host.Document, _host.Document.Selection, _host.Machine).ShowDialog(_host.Owner));
    }

    public void Stop() { }
}
