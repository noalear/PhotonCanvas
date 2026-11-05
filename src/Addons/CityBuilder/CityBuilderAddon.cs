using PhotonCanvas.App.Addons;

namespace PhotonCanvas.Addon.CityBuilder;

/// <summary>
/// Home → Draw → City, and its command, opening <see cref="CityWindow"/>: a town laid out on a big map
/// (or read from a battle map picture), its buildings made in and opened in the building editor. It only adds
/// shapes to the design when asked to put a building's pieces in; nothing is sent to the machine.
/// </summary>
public sealed class CityBuilderAddon : IAddon
{
    /// <summary>Three buildings of different heights on a street (24 × 24).</summary>
    const string Icon = "F0 M2 21h20v1.5H2z M3 12l3-3 3 3v9H3z M10 8l4-4 4 4v13h-8z M12 11h2v2h-2z M12 15h2v2h-2z M19 14h3v7h-3z";

    IAddonHost _host = null!;
    CityWindow? _window;

    public void Start(IAddonHost host)
    {
        _host = host;
        host.AddRibbonButton(new RibbonButton("Home", "Draw", "City", Icon, Open)
        {
            Tip = "Lay out a whole town on a big map, or read one from a battle map picture: roads, water, buildings, walls and trees, in a quick 3D view",
            Large = true,
            Order = 27,
        });
        host.AddCommand(new AddonCommand("citybuilder.open", "Shapes", "City builder…", "", Open));
    }

    void Open()
    {
        if (_window is not null) { _window.Activate(); return; }
        _ = _host.Guard(async () =>
        {
            _window = new CityWindow(_host.Document, _host.Machine, _host.Library, _host.DataFolder);
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
