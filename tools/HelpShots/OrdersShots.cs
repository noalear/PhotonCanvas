using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using PhotonCanvas.Addon.Orders;
using PhotonCanvas.App;
using PhotonCanvas.Core;

/// <summary>
/// The orders add-on's pictures (in its own help/images): the order list with the sample orders and
/// a line's proof, the products window, the shops window, the sheets it makes, and the packing list.
/// </summary>
static class OrdersShots
{
    const BindingFlags Any = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;

    public static void Run(MainWindow w, Action<int> settle)
    {
        var folder = Path.Combine(Environment.GetEnvironmentVariable("PHOTONCANVAS_HELP")!, "..", "..", "src", "Addons", "Orders", "help", "images");
        Directory.CreateDirectory(folder);
        void Save(Window win, string name)
        {
            settle(900);
            win.CaptureRenderedFrame()?.Save(Path.Combine(folder, name + ".png"));
            Console.WriteLine($"orders: {name}.png");
        }

        // The host comes from the add-on itself (its window, opened from the ribbon); the windows
        // shown here are made from this program's copy of the add-on, on the same data folder.
        var ribbon = (Ribbon)typeof(MainWindow).GetField("_ribbon", Any)!.GetValue(w)!;
        if (!ribbon.Commands.TryGetValue("Import/Orders", out var button)) { Console.WriteLine("no Import/Orders button (add-on not built?)"); return; }
        button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        settle(800);
        if (w.OwnedWindows.FirstOrDefault(x => x.GetType().Name == nameof(OrdersWindow)) is not { } first) { Console.WriteLine("Orders didn't open"); return; }
        var host = (PhotonCanvas.App.Addons.IAddonHost)first.GetType().GetField("_host", Any)!.GetValue(first)!;
        first.Close();
        settle(200);
        foreach (var f in new[] { "orders.json", "products.json", "shops.json" }) File.Delete(Path.Combine(host.DataFolder, f));
        if (Directory.Exists(Path.Combine(host.DataFolder, "designs"))) Directory.Delete(Path.Combine(host.DataFolder, "designs"), true);

        var orders = new OrdersWindow(host);
        orders.Show(w);
        settle(300);
        typeof(OrdersWindow).GetMethod("SampleOrders", Any)!.Invoke(orders, null);
        var book = OrderBook.Load(OrdersWindow.BookPath(host));
        var emma = book.Items.First(i => i.Buyer == "Emma Larsen");
        typeof(OrdersWindow).GetField("_selected", Any)!.SetValue(orders, emma.Key);
        typeof(OrdersWindow).GetMethod("ShowList", Any)!.Invoke(orders, null);
        Save(orders, "orders-window");

        var productsPath = Path.Combine(host.DataFolder, "products.json");
        var products = new ProductsWindow(host, ProductList.Load(productsPath), productsPath, null, emma);
        products.Show(w);
        Save(products, "orders-products");
        products.Close();

        var shops = new ShopsWindow(host, Path.Combine(host.DataFolder, "shops.json"));
        shops.Show(w);
        Save(shops, "orders-shops");
        shops.Close();

        // The sheets, on the work area behind the window.
        typeof(OrdersWindow).GetMethod("MakeSheets", Any)!.Invoke(orders, null);
        settle(300);
        orders.Close();
        settle(300);
        host.FitWorkArea();
        Save(w, "orders-sheet");

        var sheets = SheetMaker.Read(host.Document.State);
        book = OrderBook.Load(OrdersWindow.BookPath(host));
        var png = PackingList.FirstPagePng(sheets, book.Find, new Rect2(0, 0, host.Machine.FieldW, host.Machine.FieldH), false, DateTime.Now, 1.5f);
        File.WriteAllBytes(Path.Combine(folder, "orders-packing-list.png"), png);
        Console.WriteLine("orders: orders-packing-list.png");

        foreach (var f in new[] { "orders.json", "products.json", "shops.json" }) File.Delete(Path.Combine(host.DataFolder, f));
        if (Directory.Exists(Path.Combine(host.DataFolder, "designs"))) Directory.Delete(Path.Combine(host.DataFolder, "designs"), true);
    }
}
