using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using PhotonCanvas.Addon.Bases;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Materials;

/// <summary>The base maker's pictures (in its own help/images): cracked earth bases, swamp and rocky ovals, and a movement tray.</summary>
static class BasesShots
{
    const BindingFlags Any = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;

    public static void Run(Window owner, Document doc, MachineProfile machine, MaterialLibrary lib, Action<int> settle)
    {
        var folder = Path.Combine(Environment.GetEnvironmentVariable("PHOTONCANVAS_HELP")!, "..", "..", "src", "Addons", "Bases", "help", "images");
        Directory.CreateDirectory(folder);

        void Shot(string name, BaseSpec spec, int tab)
        {
            // No data folder: the pictures show the settings given here, not the last ones used.
            var win = new BasesWindow(doc, machine, lib, null, null);
            typeof(BasesWindow).GetMethod("Load", Any)!.Invoke(win, [spec]);
            win.Show(owner);
            settle(300);
            var tabs = win.GetVisualDescendantsOf<TabControl>().First();
            tabs.SelectedIndex = tab;
            var t = (Task)typeof(BasesWindow).GetMethod("Rebuild", Any)!.Invoke(win, null)!;
            for (int i = 0; i < 400 && !t.IsCompleted; i++) settle(50);
            settle(600);
            win.CaptureRenderedFrame()?.Save(Path.Combine(folder, name + ".png"));
            Console.WriteLine($"bases: {name}.png");
            win.Close();
            settle(200);
        }

        Shot("window-bases-earth", new BaseSpec
        {
            Bases = [new() { Shape = BaseShape.Round, WidthMm = 25, Count = 10 }, new() { Shape = BaseShape.Round, WidthMm = 40, Count = 5 }],
            Layers = 2, StepMm = 0.8, MagnetMm = 5,
        }, 0);
        Shot("window-bases-swamp", new BaseSpec
        {
            Bases = [new() { Shape = BaseShape.Oval, WidthMm = 75, DepthMm = 42, Count = 3 }, new() { Shape = BaseShape.Round, WidthMm = 50, Count = 2 }, new() { Shape = BaseShape.Hexagon, WidthMm = 30, Count = 4 }],
            Texture = "Swamp", SizeA = 9, SizeB = 30, GrooveMm = 0.5, Seed = 3,
        }, 1);
        Shot("window-bases-tray", new BaseSpec
        {
            Bases = [new() { Shape = BaseShape.Round, WidthMm = 25, Count = 9 }],
            Texture = "Rocky ground", SizeA = 6, SizeB = 30, GrooveMm = 0.5,
            MakeTray = true, Tray = new TraySpec { Columns = 5, Rows = 2, Stagger = true },
        }, 2);
    }

    static IEnumerable<T> GetVisualDescendantsOf<T>(this Avalonia.Visual v) => Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(v).OfType<T>();
}
