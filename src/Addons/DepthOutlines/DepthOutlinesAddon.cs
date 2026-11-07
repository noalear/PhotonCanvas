using PhotonCanvas.App.Addons;

namespace PhotonCanvas.Addon.DepthOutlines;

/// <summary>Picture → Trace → Depth outlines (and its command), opening <see cref="DepthOutlinesWindow"/>.</summary>
public sealed class DepthOutlinesAddon : IAddon
{
    IAddonHost _host = null!;

    public void Start(IAddonHost host)
    {
        _host = host;
        host.AddRibbonButton(new RibbonButton("Picture", "Trace", "Depth outlines", "IcTrace", Open)
        {
            Tip = "Find the edges in a depthmap and mark them as lines over the relief (one laser line, a set width, or swelling with the edge), so it looks less washed out",
            Enabled = () => host.SelectedPicture is not null,
            Order = 11,
        });
        host.AddCommand(new AddonCommand("picture.depthoutlines", "Shapes", "Depth outlines…", "", Open));
    }

    void Open()
    {
        if (_host.SelectedPicture is { } im) _ = _host.Guard(() => new DepthOutlinesWindow(_host.Document, im).ShowDialog(_host.Owner));
    }

    public void Stop() { }
}
