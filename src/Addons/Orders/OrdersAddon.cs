using PhotonCanvas.App.Addons;

namespace PhotonCanvas.Addon.Orders;

/// <summary>
/// Import → Orders → Orders: Etsy and Shopify orders (a CSV export or straight from the shop) made
/// into ready-to-burn sheets, each piece with its customer's text; proofs and a packing list.
/// Marks pieces burned when their sheet finishes on the machine.
/// </summary>
public sealed class OrdersAddon : IAddon
{
    IAddonHost _host = null!;
    OrdersWindow? _window;

    public const string Icon = "F0 M5 2h14v20l-2.33-1.5L14.33 22 12 20.5 9.67 22l-2.34-1.5L5 22z M7 4v14.4l.33-.2 2.34 1.5L12 18.2l2.33 1.5 2.34-1.5.33.2V4z M9 7h6v2H9z M9 11h6v2H9z M9 15h4v2H9z";

    public void Start(IAddonHost host)
    {
        _host = host;
        host.AddRibbonButton(new RibbonButton("Import", "Orders", "Orders", Icon, Open)
        {
            Tip = "Bring in Etsy or Shopify orders and turn them into sheets ready to burn, each piece with its customer's text; proofs and a packing list too",
            Large = true,
            GroupAfter = "Generate",
            Order = 10,
        });
        host.AddCommand(new AddonCommand("orders.open", "Import", "Orders to sheet…", "", Open));
        host.JobFinished += Finished;
    }

    void Open()
    {
        if (_window is { } open) { open.Activate(); return; }
        _ = _host.Guard(async () =>
        {
            _window = new OrdersWindow(_host);
            try { await _window.ShowDialog(_host.Owner); }
            finally { _window = null; }
        });
    }

    /// <summary>A job from this session finished: the order pieces on the page on screen are burned.</summary>
    void Finished(string? _)
    {
        var found = SheetMaker.OnScreen(_host.Document.State);
        if (found.Count == 0) return;
        var path = OrdersWindow.BookPath(_host);
        var book = OrderBook.Load(path);
        var page = _host.Document.State.PageName;
        int n = 0;
        foreach (var key in found.Select(f => f.Key).Distinct())
        {
            if (book.Find(key) is not { Status: ItemStatus.ToBurn or ItemStatus.OnSheet }) continue;
            book.Update(key, i => i with { Status = ItemStatus.Burned, Burned = DateTime.Now, Sheet = page });
            n++;
        }
        if (n == 0) return;
        try { book.Save(path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _host.Status($"Couldn't note the orders as burned: {e.Message}"); return; }
        _window?.Reload();
        _host.Status($"{n} order line{(n == 1 ? "" : "s")} on “{page}” marked burned (Import → Orders).");
    }

    public void Stop() => _window?.Close();
}
