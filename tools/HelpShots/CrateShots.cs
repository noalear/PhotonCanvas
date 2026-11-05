using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using PhotonCanvas.Addon.Furniture;

/// <summary>
/// Crates in every style, to look over (CRATE_REVIEW=&lt;folder&gt;, `shots crates`): each put together,
/// and its front, end and lid flat, close up.
/// </summary>
static class CrateShots
{
    public static void Run(Window owner, Action<int> settle)
    {
        var to = Environment.GetEnvironmentVariable("CRATE_REVIEW");
        if (string.IsNullOrEmpty(to)) { Console.WriteLine("crates: set CRATE_REVIEW to a folder"); return; }
        Directory.CreateDirectory(to);
        // CRATE_SIZE=width,depth,height (mm); 12 mm cubes by default.
        var dims = (Environment.GetEnvironmentVariable("CRATE_SIZE") ?? "12,12,12").Split(',').Select(double.Parse).ToArray();
        var details = (Environment.GetEnvironmentVariable("CRATE_DETAIL") ?? "Planks").Split(',').Select(Enum.Parse<WoodDetail>).ToList();

        void Save(Control view, string name, int w, int h)
        {
            var win = new Window { Width = w, Height = h, Content = new Border { Background = new SolidColorBrush(Color.FromRgb(0xF4, 0xF1, 0xEA)), Child = view } };
            win.Show(owner);
            settle(300);
            if (view is AssembledView a) { a.Fit(); settle(200); }
            win.CaptureRenderedFrame()?.Save(Path.Combine(to, name + ".png"));
            Console.WriteLine($"crates: {name}.png");
            win.Close();
            settle(100);
        }

        foreach (var detail in details)
            for (int i = 0; i < Crates.StyleNames.Length; i++)
            {
                var item = Defaults.For(ItemKind.Crate) with { WidthMm = dims[0], DepthMm = dims[1], HeightMm = dims[2], Bands = i > 0, CrateStyle = (CrateStyle)Math.Max(0, i - 1) };
                var set = new FurnitureSet { Items = [item], Detail = detail };
                var kit = FurnitureKit.Make(item, set);
                string name = $"{dims[0]:0}x{dims[2]:0}-{detail}-{i}-{(i == 0 ? "Plain" : ((CrateStyle)(i - 1)).ToString())}";
                var a = new AssembledView();
                a.Show(kit, set.ThicknessMm);
                Save(a, name + "-together", 560, 480);
                var flat = kit with { Parts = [.. kit.Parts.Where(p => p.Name is "Front" or "Left end" or "Lid")] };
                var pv = new PiecesView();
                pv.Show(flat);
                Save(pv, name + "-faces", 900, 360);
            }
    }
}
