using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using PhotonCanvas.Addon.Bridges;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Materials;

/// <summary>The bridge maker's pictures (in its own help/images): an arched plank footbridge, a rope bridge's pieces and a stone bridge.</summary>
static class BridgesShots
{
    const BindingFlags Any = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;

    static T F<T>(object o, string name) => (T)o.GetType().GetField(name, Any)!.GetValue(o)!;

    public static void Run(Window owner, Document doc, MachineProfile machine, MaterialLibrary lib, Action<int> settle)
    {
        var folder = Path.Combine(Environment.GetEnvironmentVariable("PHOTONCANVAS_HELP")!, "..", "..", "src", "Addons", "Bridges", "help", "images");
        Directory.CreateDirectory(folder);
        // A scratch data folder holding the bridge to show, so the pictures don't depend on the last one used.
        var data = Path.Combine(Path.GetTempPath(), "photoncanvas-helpshots-bridges");

        void Shot(string name, BridgeSpec spec, int view, double scroll = 0)
        {
            Directory.CreateDirectory(data);
            File.WriteAllText(Path.Combine(data, "last.json"), spec.ToJson());
            var win = new BridgesWindow(doc, machine, lib, data, null);
            win.Show(owner);
            settle(300);
            F<TabControl>(win, "_views").SelectedIndex = view;
            settle(100);
            var t = (Task)typeof(BridgesWindow).GetMethod("Rebuild", Any)!.Invoke(win, null)!;
            for (int i = 0; i < 600 && !t.IsCompleted; i++) settle(50);
            settle(600);
            win.CaptureRenderedFrame()?.Save(Path.Combine(folder, name + ".png"));
            Console.WriteLine($"bridges: {name}.png");
            win.Close();
            settle(200);
        }

        Shot("window-bridges-plank", Defaults.For(BridgeKind.Plank) with { RiseMm = 10 }, 0);
        Shot("window-bridges-rope", Defaults.For(BridgeKind.Rope), 1);
        Shot("window-bridges-stone", Defaults.For(BridgeKind.Stone) with
        {
            Arches = 3, ArchSpanMm = 28, LengthMm = 150, HeightMm = 28, SpringMm = 4, Road = RoadShape.Humped, EndHeightMm = 14,
        }, 0);
    }
}
