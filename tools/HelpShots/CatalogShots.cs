using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using PhotonCanvas.Addon.Furniture;

/// <summary>
/// Every ready-made furniture piece put together, one picture each, to set beside its reference
/// (CATALOG_REVIEW=&lt;folder&gt;, `shots catalog`; CATALOG_ONLY=id prefixes, comma separated).
/// </summary>
static class CatalogShots
{
    public static void Run(Window owner, Action<int> settle)
    {
        var to = Environment.GetEnvironmentVariable("CATALOG_REVIEW");
        if (string.IsNullOrEmpty(to)) { Console.WriteLine("catalog: set CATALOG_REVIEW to a folder"); return; }
        Directory.CreateDirectory(to);
        var only = Environment.GetEnvironmentVariable("CATALOG_ONLY")?.Split(',', StringSplitOptions.RemoveEmptyEntries);
        int size = int.TryParse(Environment.GetEnvironmentVariable("CATALOG_SIZE"), out var s) ? s : 420;
        foreach (var e in FurnitureCatalog.All)
        {
            if (only is not null && !only.Any(o => e.Id.StartsWith(o, StringComparison.Ordinal))) continue;
            var set = FurnitureThemes.Apply(new FurnitureSet { Items = [e.Item] }, e.Theme);
            var item = set.Items[0];
            var kit = FurnitureKit.Make(item, set);
            var a = new AssembledView();
            a.Show(kit, set.ThicknessMm);
            var win = new Window { Width = size, Height = size, Content = new Border { Background = new SolidColorBrush(Color.FromRgb(0xF4, 0xF1, 0xEA)), Child = a } };
            win.Show(owner);
            settle(200);
            // CATALOG_TURN=dx,dy turns the view first (pixels of drag), e.g. -150,0 to look from the front.
            if (Environment.GetEnvironmentVariable("CATALOG_TURN") is { Length: > 0 } turn)
            {
                var d = turn.Split(',').Select(double.Parse).ToArray();
                a.Turn(d[0], d[1]);
            }
            a.Fit();
            settle(200);
            win.CaptureRenderedFrame()?.Save(Path.Combine(to, e.Id + ".png"));
            Console.WriteLine($"catalog: {e.Id}.png");
            win.Close();
            settle(50);
        }
    }
}
