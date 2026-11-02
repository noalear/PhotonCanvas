using PhotonCanvas.App.Addons;
using PhotonCanvas.Doc;

namespace PhotonCanvas.Addon.Handwriting;

/// <summary>
/// Home → Draw → Handwriting: write in a font made from your own handwriting (fill in a printed
/// sheet, photograph it), as single lines for fast engraving.
/// </summary>
public sealed class HandwritingAddon : IAddon
{
    IAddonHost _host = null!;

    /// <summary>A pen over a wavy line.</summary>
    const string IconPath = "M15.6 2.6l3.8 3.8-9.2 9.2-4.6 1 1-4.6z M2 20.2c1.6-1.6 3.4-1.6 5 0s3.4 1.6 5 0 3.4-1.6 5 0 3.4 1.6 5 0v2c-1.6 1.6-3.4 1.6-5 0s-3.4-1.6-5 0-3.4 1.6-5 0-3.4-1.6-5 0z";

    public void Start(IAddonHost host)
    {
        _host = host;
        host.AddRibbonButton(new RibbonButton("Home", "Draw", "Handwriting", IconPath, Open)
        {
            Tip = "Write in your own handwriting, as single lines for fast engraving (make the font from a sheet you fill in)",
            Large = true,
        });
        host.AddCommand(new AddonCommand("handwriting.write", "Shapes", "Write in my handwriting…", "", Open));
        host.AddCommand(new AddonCommand("handwriting.font", "Shapes", "Make a handwriting font…", "", MakeFont));
        host.AddContextMenuItem(new ContextMenuEntry("Change the handwriting…", Open) { Visible = () => Selected() is not null });
    }

    /// <summary>The one selected shape, when it's handwriting this add-on wrote.</summary>
    PathShape? Selected()
    {
        var doc = _host.Document;
        if (doc.Selection.Count != 1) return null;
        return doc.State.Shapes.FirstOrDefault(s => doc.Selection.Contains(s.Id)) is PathShape p && p.AddonData.ContainsKey(HandwritingWindow.TextKey) ? p : null;
    }

    void Open() => _ = _host.Guard(() => new HandwritingWindow(_host, Selected()).ShowDialog(_host.Owner));

    void MakeFont() => _ = _host.Guard(() => new HandwritingFontWindow(_host, new FontStore(Path.Combine(_host.DataFolder, "fonts")), null).ShowDialog<string?>(_host.Owner));

    public void Stop() { }
}
