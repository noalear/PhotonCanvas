using PhotonCanvas.App.Addons;

// Not ...Addon.Generators: inside that namespace "Generators" would mean it, not the Doc class that draws the projects.
namespace PhotonCanvas.Addon.ReadyMade;

/// <summary>Home → Draw → Generators (and its command), opening <see cref="GeneratorsWindow"/>.</summary>
public sealed class GeneratorsAddon : IAddon
{
    IAddonHost _host = null!;
    GeneratorsWindow? _open;

    public void Start(IAddonHost host)
    {
        _host = host;
        host.AddRibbonButton(new RibbonButton("Home", "Draw", "Generators", "IcTestGrid", Open)
        {
            Large = true,
            Tip = "Finger-jointed boxes, keychains and tags, living hinges, earrings, and the selection in several sizes",
        });
        host.AddCommand(new AddonCommand("generators.open", "Shapes", "Generators…", "", Open));
    }

    void Open()
    {
        if (_open is { IsVisible: true }) { _open.Activate(); return; }
        // The library's cut recipes (with the kerf the Kerf gauge or Cut test measured) for the box.
        var window = new GeneratorsWindow(_host.Document, _host.Machine, _host.Library);
        _open = window;
        window.Closed += (_, _) => { if (ReferenceEquals(_open, window)) _open = null; };
        _ = _host.Guard(() => window.ShowDialog(_host.Owner));
    }

    public void Stop()
    {
        _open?.Close();
        _open = null;
    }
}
