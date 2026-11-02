using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using PhotonCanvas.Addon.Stairs;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Materials;

/// <summary>The floor and stairs maker's pictures (in its own help/images): open stairs, spiral stairs and a hooked ladder.</summary>
static class StairsShots
{
    const BindingFlags Any = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;

    static T F<T>(object o, string name) => (T)o.GetType().GetField(name, Any)!.GetValue(o)!;

    public static void Run(Window owner, Document doc, MachineProfile machine, MaterialLibrary lib, Action<int> settle)
    {
        var folder = Path.Combine(Environment.GetEnvironmentVariable("PHOTONCANVAS_HELP")!, "..", "..", "src", "Addons", "Stairs", "help", "images");
        Directory.CreateDirectory(folder);

        void Shot(string name, FloorSpec spec, int tab, bool lift = false)
        {
            // No data folder: the pictures show the settings given here, not the last ones used.
            var win = new StairsWindow(doc, machine, lib, null, null);
            typeof(StairsWindow).GetMethod("Load", Any)!.Invoke(win, [spec]);
            win.Show(owner);
            settle(300);
            F<TabControl>(win, "_tabs").SelectedIndex = tab;
            F<CheckBox>(win, "_lift").IsChecked = lift;
            var t = (Task)typeof(StairsWindow).GetMethod("Rebuild", Any)!.Invoke(win, null)!;
            for (int i = 0; i < 400 && !t.IsCompleted; i++) settle(50);
            settle(600);
            win.CaptureRenderedFrame()?.Save(Path.Combine(folder, name + ".png"));
            Console.WriteLine($"stairs: {name}.png");
            win.Close();
            settle(200);
        }

        Shot("window-stairs-open", new FloorSpec { WallHeightMm = 51, Way = WayUp.Stairs, Rail = Railing.Left }, 3);
        Shot("window-stairs-spiral", new FloorSpec { WallHeightMm = 60, Way = WayUp.Spiral, Texture = "Stone blocks", SizeA = 12, SizeB = 6, GrooveMm = 0.8 }, 3, lift: true);
        Shot("window-stairs-ladder", new FloorSpec { WallHeightMm = 51, Way = WayUp.HookedLadder, Trapdoor = true, Edge = FloorEdge.Overhang, OverhangMm = 6 }, 2);
    }
}
