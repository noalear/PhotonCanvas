using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using PhotonCanvas.Addon.ProductionLog;
using PhotonCanvas.App;
using PhotonCanvas.App.Addons;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;

/// <summary>
/// The production log add-on's picture (in its own help/images): a few days of serial-numbered pens
/// and name tags, the open design reopened from before its last run so Carry on has something to say.
/// </summary>
static class ProductionLogShots
{
    const BindingFlags Any = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;

    static T F<T>(object o, string name) => (T)o.GetType().GetField(name, Any)!.GetValue(o)!;

    public static void Run(MainWindow w, Document doc, Action<int> settle)
    {
        var folder = Path.Combine(Environment.GetEnvironmentVariable("PHOTONCANVAS_HELP")!, "..", "..", "src", "Addons", "ProductionLog", "help", "images");
        Directory.CreateDirectory(folder);
        var ribbon = F<Ribbon>(w, "_ribbon");
        if (!ribbon.Commands.TryGetValue("Machine/Production log", out var button)) { Console.WriteLine("no Machine/Production log button (add-on not built?)"); return; }
        button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        settle(500);
        // The add-on runs in its own load context: its window is used only for the host; the log shown is made here.
        if (w.OwnedWindows.FirstOrDefault(x => x.GetType().Name == nameof(ProductionLogWindow)) is not { } first) { Console.WriteLine("Production log didn't open"); return; }
        var host = F<IAddonHost>(first, "_host");
        first.Close();
        settle(200);

        // The design: a pen barrel with its serial number and a QR code, reopened at serial 11 (not saved after the last run).
        var s = DocState.New();
        s = Ops.AddLayer(s, Layer.Default(1, LayerMode.Fill) with { Name = "Serial", Laser = LaserSettings.Red(45, 1200, 30, 120), LinesPerCm = 300 });
        var serialLayer = s.Layers[^1];
        var text = TextShape.Create("PC-{serial:0000}", new TextStyle("Arial", 3), serialLayer.Id, Affine.Translate(20, 20));
        s = Ops.AddShapes(s with { Vars = new VariableData { Serial = 11, SerialEnd = 500 } }, [text]);
        doc.Reset(s, Path.Combine(Path.GetTempPath(), "Pen barrels.pcanvas"));
        settle(200);

        var data = Path.Combine(Path.GetTempPath(), "photoncanvas-helpshots-productionlog");
        if (Directory.Exists(data)) Directory.Delete(data, true);
        var store = new LogStore(data);
        var book = store.Book;
        var day = new DateTimeOffset(DateTime.Today.AddDays(-2).AddHours(9));
        string pen = "Serial: Fill, 45% at 1200 mm/s × 1 (MOPA 30 ns 120 kHz), Anodized aluminium – Black anodized, white";
        LoggedRun Pens(long from, int count, DateTimeOffset at) => new()
        {
            SentAt = at,
            Design = "Pen barrels",
            DesignFile = @"C:\Users\Me\Documents\Pen barrels.pcanvas",
            Items = [.. Enumerable.Range(0, count).Select(i => new MarkedItem { Index = i, Serial = from + i, Marks = [$"Text: PC-{from + i:0000}", $"QR code: https://example.com/p/{from + i}"] })],
            Settings = [pen],
            EstimateS = 41 * count,
            WidthMm = 128,
            HeightMm = 96,
            Machine = "F2 Ultra",
            Who = "Shop",
        };
        void Done(LoggedRun r, double minutes, bool finished = true)
        {
            book.Add(r);
            book.Started(r.Id, r.SentAt.AddSeconds(20));
            book.Ended(r.Id, r.SentAt.AddSeconds(20).AddMinutes(minutes), finished);
        }
        var names = new[] { ("Ada", "1041"), ("Ben", "1041"), ("Cleo", "1042"), ("Dev", "1043"), ("Ezra", "1043"), ("Fern", "1044") };
        var tags = new LoggedRun
        {
            SentAt = day,
            Design = "Pet tags",
            Items = [.. names.Select((n, i) => new MarkedItem { Index = i, Row = i, Sheet = "orders.csv", RowValues = [new("Name", n.Item1), new("Order", n.Item2)], Marks = [$"Text: {n.Item1}", $"Text: Order {n.Item2}"] })],
            Settings = ["Names: Fill, 80% at 300 mm/s × 2 (diode), Brass – Deep engrave"],
            EstimateS = 410,
            WidthMm = 150,
            HeightMm = 30,
            Machine = "F2 Ultra",
            Who = "Shop",
        };
        Done(tags, 7.1);
        Done(Pens(1, 10, day.AddDays(1)), 6.8);
        Done(Pens(11, 10, day.AddDays(1).AddHours(2)), 2.2, finished: false);
        var redo = Pens(11, 10, day.AddDays(1).AddHours(2).AddMinutes(10));
        Done(redo, 6.9);
        book.SetSpoiled(redo.Id, 3, true);
        book.SetNote(redo.Id, "Order 1052: Hannah's pens. PC-0014 smudged, marked again on the next batch.");

        var win = new ProductionLogWindow(host, store);
        win.Show(w);
        settle(300);
        win.GetType().GetField("_selected", Any)!.SetValue(win, redo.Id);
        win.GetType().GetMethod("Refresh", Any)!.Invoke(win, null);
        settle(600);
        win.CaptureRenderedFrame()?.Save(Path.Combine(folder, "window-production-log.png"));
        Console.WriteLine("production log: window-production-log.png");
        win.Close();
        settle(200);
    }
}
