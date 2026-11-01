using PhotonCanvas.App.Addons;
using PhotonCanvas.Doc;

namespace PhotonCanvas.Addon.Warp;

/// <summary>Arrange → Transform → Warp, its command and the right-click item, opening <see cref="WarpWindow"/>.</summary>
public sealed class WarpAddon : IAddon
{
    IAddonHost _host = null!;

    public void Start(IAddonHost host)
    {
        _host = host;
        host.AddRibbonButton(new RibbonButton("Arrange", "Transform", "Warp", "IcWarp", Open)
        {
            Tip = "Warp the selection: perspective taper, arch, bulge or wave",
            Large = true,
            Enabled = HasPaths,
        });
        host.AddCommand(new AddonCommand("shape.warp", "Shapes", "Warp…", "", Open));
        host.AddContextMenuItem(new ContextMenuEntry("Warp…", Open) { Visible = () => host.Document.Selection.Count > 0, Enabled = HasPaths });
    }

    bool HasPaths() => _host.Document.State.Shapes.Any(s => _host.Document.Selection.Contains(s.Id) && s is PathShape);

    void Open()
    {
        if (HasPaths()) _ = _host.Guard(() => new WarpWindow(_host.Document).ShowDialog(_host.Owner));
    }

    public void Stop() { }
}
