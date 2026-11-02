using System.Reflection;
using Avalonia.Headless;
using Avalonia.Controls;
using PhotonCanvas.Addon.Buildings;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Materials;

/// <summary>The building maker's pictures (in its own help/images): a tavern put together, a cottage cut away, the watchtower's wall pieces.</summary>
static class BuildingsShots
{
    const BindingFlags Any = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;

    static T F<T>(object o, string name) => (T)o.GetType().GetField(name, Any)!.GetValue(o)!;

    public static void Run(Window owner, Document doc, MachineProfile machine, MaterialLibrary lib, Action<int> settle)
    {
        var folder = Path.Combine(Environment.GetEnvironmentVariable("PHOTONCANVAS_HELP")!, "..", "..", "src", "Addons", "Buildings", "help", "images");
        Directory.CreateDirectory(folder);
        // A scratch data folder holding the building to show, so the pictures don't depend on the last one made.
        var data = Path.Combine(Path.GetTempPath(), "photoncanvas-helpshots-buildings");

        void Shot(string name, BuildingSpec spec, int tab, int view, int show)
        {
            Directory.CreateDirectory(data);
            File.WriteAllText(Path.Combine(data, "last.json"), spec.ToJson());
            var win = new BuildingWindow(doc, machine, lib, data, null);
            win.Show(owner);
            settle(300);
            F<TabControl>(win, "_tabs").SelectedIndex = tab;
            F<TabControl>(win, "_views").SelectedIndex = view;
            var t = (Task)typeof(BuildingWindow).GetMethod("Rebuild", Any)!.Invoke(win, null)!;
            for (int i = 0; i < 600 && !t.IsCompleted; i++) settle(50);
            F<ComboBox>(win, "_show").SelectedIndex = show;
            settle(800);
            win.CaptureRenderedFrame()?.Save(Path.Combine(folder, name + ".png"));
            Console.WriteLine($"buildings: {name}.png");
            win.Close();
            settle(200);
        }

        Shot("window-buildings-tavern", BuildingSpec.Preset(BuildingKind.Tavern), 0, 0, 0);
        Shot("window-buildings-cottage", BuildingSpec.Preset(BuildingKind.Cottage), 2, 0, 1);
        Shot("window-buildings-tower-pieces", BuildingSpec.Preset(BuildingKind.Watchtower), 1, 1, 0);
    }
}
