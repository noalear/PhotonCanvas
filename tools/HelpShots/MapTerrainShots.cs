using System.Reflection;
using Avalonia.Headless;
using Avalonia.Controls;
using PhotonCanvas.Addon.MapTerrain;
using PhotonCanvas.Core;
using PhotonCanvas.Core.Imaging;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Materials;

/// <summary>The map maker's pictures (in its own help/images): the sample dungeon's plan and put together, and a battle map read into the plan.</summary>
static class MapTerrainShots
{
    const BindingFlags Any = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;

    static T F<T>(object o, string name) => (T)o.GetType().GetField(name, Any)!.GetValue(o)!;

    public static void Run(Window owner, Document doc, MachineProfile machine, MaterialLibrary lib, Action<int> settle)
    {
        var folder = Path.Combine(Environment.GetEnvironmentVariable("PHOTONCANVAS_HELP")!, "..", "..", "src", "Addons", "MapTerrain", "help", "images");
        Directory.CreateDirectory(folder);
        // A scratch data folder holding the map to show, so the pictures don't depend on the last one made.
        var data = Path.Combine(Path.GetTempPath(), "photoncanvas-helpshots-mapterrain");

        void Shot(string name, MapSpec spec, int tab, int view, RasterImage? picture = null)
        {
            Directory.CreateDirectory(data);
            File.WriteAllText(Path.Combine(data, "last.json"), spec.ToJson());
            var win = new MapWindow(doc, machine, lib, data, null);
            win.Show(owner);
            settle(300);
            if (picture is not null)
            {
                var load = win.UsePicture(picture, null);
                for (int i = 0; i < 600 && !load.IsCompleted; i++) settle(50);
            }
            F<TabControl>(win, "_tabs").SelectedIndex = tab;
            F<TabControl>(win, "_views").SelectedIndex = view;
            var t = (Task)typeof(MapWindow).GetMethod("Rebuild", Any)!.Invoke(win, null)!;
            for (int i = 0; i < 600 && !t.IsCompleted; i++) settle(50);
            settle(800);
            win.CaptureRenderedFrame()?.Save(Path.Combine(folder, name + ".png"));
            Console.WriteLine($"mapterrain: {name}.png");
            win.Close();
            settle(200);
        }

        Shot("window-mapterrain-plan", MapSpec.Sample(), 0, 0);
        Shot("window-mapterrain-3d", MapSpec.Sample(), 1, 1);
        Shot("window-mapterrain-battlemap", MapSpec.Sample() with { Plan = MapPlan.Empty(10, 8) }, 0, 0, BattleMap());
    }

    /// <summary>A battle map like a virtual tabletop's: stone-flagged rooms and a corridor in dark rock, a grid over it all.</summary>
    static RasterImage BattleMap()
    {
        const int p = 56, across = 16, down = 12;
        var plan = new PlanGrid(across, down);
        void Room(int x0, int y0, int x1, int y1) { for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++) plan[x, y] = true; }
        Room(1, 1, 5, 5); Room(6, 3, 9, 3); Room(10, 1, 14, 8); Room(2, 6, 3, 10); Room(4, 9, 9, 10);
        int w = across * p, h = down * p;
        var px = new byte[w * h * 4];
        var rnd = new Random(5);
        // Each flagstone's own shade.
        var shade = new double[across * 2, down * 2];
        for (int i = 0; i < across * 2; i++) for (int j = 0; j < down * 2; j++) shade[i, j] = (rnd.NextDouble() - 0.5) * 0.12;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int cx = x / p, cy = y / p;
                double r, g, b;
                if (plan[cx, cy])
                {
                    double s = 0.74 + shade[x * 2 / p, y * 2 / p] + (rnd.NextDouble() - 0.5) * 0.05;
                    if (x % (p / 2) < 2 || y % (p / 2) < 2) s -= 0.18;   // the joints between flagstones
                    r = s; g = s * 0.95; b = s * 0.85;
                }
                else
                {
                    double s = 0.16 + (rnd.NextDouble() - 0.5) * 0.08 + 0.05 * Math.Sin(x * 0.07) * Math.Cos(y * 0.05);
                    r = s; g = s * 0.95; b = s * 1.05;
                }
                if (x % p == 0 || y % p == 0) { r *= 0.7; g *= 0.7; b *= 0.7; }
                int k = (y * w + x) * 4;
                px[k] = (byte)Math.Clamp(r * 255, 0, 255); px[k + 1] = (byte)Math.Clamp(g * 255, 0, 255); px[k + 2] = (byte)Math.Clamp(b * 255, 0, 255); px[k + 3] = 255;
            }
        return new RasterImage(w, h, px);
    }
}
