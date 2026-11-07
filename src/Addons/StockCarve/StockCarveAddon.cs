using PhotonCanvas.App.Addons;

namespace PhotonCanvas.Addon.StockCarve;

/// <summary>
/// Carve stock to a model (Laser → Work area): a part scanned in the rotary, a model lined up inside it, and a
/// page that engraves the part down to the model's shape as it turns.
/// </summary>
public sealed class StockCarveAddon : IAddon
{
    IAddonHost _host = null!;
    StockCarveWindow? _window;

    public void Start(IAddonHost host)
    {
        _host = host;
        host.AddRibbonButton(new RibbonButton("Laser", "Work area", "Carve to model", "IcDepth", Open)
        {
            Tip = "Line a 3D model up inside the part scanned in the rotary, see where it sticks out, and engrave the part down to the model's shape",
            Order = 20,
        });
        host.AddCommand(new AddonCommand("stock-carve.open", "Laser", "Carve stock to a model…", "", Open));
    }

    void Open()
    {
        if (_window is { } w) { w.Activate(); return; }
        _ = _host.Guard(() =>
        {
            _window = new StockCarveWindow(_host);
            _window.Closed += (_, _) => _window = null;
            _window.Show(_host.Owner);
            return Task.CompletedTask;
        });
    }

    public void Stop()
    {
        _window?.Close();
        _window = null;
    }
}
