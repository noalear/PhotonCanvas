using PhotonCanvas.App.Addons;

namespace PhotonCanvas.Addon.Quote;

/// <summary>Laser → Job → Quote: what the design costs to make and what to charge, as a PDF quote or text.</summary>
public sealed class QuoteAddon : IAddon
{
    IAddonHost _host = null!;
    QuoteWindow? _window;

    public void Start(IAddonHost host)
    {
        _host = host;
        host.AddRibbonButton(new RibbonButton("Laser", "Job", "Quote", "F0 M5 2h14v20H5z M7 4v16h10V4z M9 7h6v2H9z M9 11h6v2H9z M9 15h4v2H9z", Open)
        {
            Tip = "What this design costs you to make (material, laser time, electricity, your time) and the price to charge, as a quote to print or send",
            Order = 20,
        });
        host.AddCommand(new AddonCommand("quote.open", "Job", "Quote and cost…", "", Open));
    }

    void Open()
    {
        if (_window is { } open) { open.Activate(); return; }
        _ = _host.Guard(async () =>
        {
            _window = new QuoteWindow(_host);
            try { await _window.ShowDialog(_host.Owner); }
            finally { _window = null; }
        });
    }

    public void Stop() => _window?.Close();
}
