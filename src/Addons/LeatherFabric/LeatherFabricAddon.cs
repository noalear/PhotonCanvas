using PhotonCanvas.App.Addons;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Extensions;

namespace PhotonCanvas.Addon.LeatherFabric;

/// <summary>Home → Sewing → Leather &amp; fabric, its command and right-click item, and the job checks for sewn pieces.</summary>
public sealed class LeatherFabricAddon : IAddon
{
    /// <summary>A needle with a row of stitches beside it (24 × 24).</summary>
    const string NeedleIcon = "M20 2.2a1.8 1.8 0 0 1 1.3 3.1L7.4 19.2l-3.6 1.6 1.6-3.6L19.3 3.3c.2-.2.4-.4.7-.6zM19.6 4.3l-.9.9.6.6.9-.9zM2 2h1.6v3H2zm0 5.5h1.6v3H2zm0 5.5h1.6v3H2z";

    IAddonHost _host = null!;
    SewnPatternWindow? _window;

    public void Start(IAddonHost host)
    {
        _host = host;
        host.AddRibbonButton(new RibbonButton("Home", "Sewing", "Leather & fabric", NeedleIcon, Open)
        {
            Tip = "Pieces for sewn goods from the selected outlines: seam allowance, stitch holes, notches and mirrored pairs",
            Large = true,
            Enabled = HasOutlines,
            GroupAfter = "Outline",
        });
        host.AddCommand(new AddonCommand("leatherfabric.open", "Shapes", "Leather and fabric pieces…", "", Open));
        host.AddContextMenuItem(new ContextMenuEntry("Leather and fabric pieces…", Open) { Visible = () => host.Document.Selection.Count > 0, Enabled = HasOutlines });
        host.AddJobHook(new Checks());
    }

    bool HasOutlines() => SewnBuild.Sources(_host.Document.State, _host.Document.Selection).Count > 0;

    void Open()
    {
        if (!HasOutlines()) { _host.Status("Select the outlines of the pieces first."); return; }
        _ = _host.Guard(async () =>
        {
            _window = new SewnPatternWindow(_host);
            try { await _window.ShowDialog(_host.Owner); }
            finally { _window = null; }
        });
    }

    public void Stop() => _window?.Close();

    sealed class Checks : IJobHook
    {
        public string Name => "Leather and fabric";
        public IEnumerable<string> Check(DocState state, JobOptions options) => SewnBuild.Check(state);
    }
}
