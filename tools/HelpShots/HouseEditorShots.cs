using System.Reflection;
using Avalonia.Headless;
using Avalonia.Controls;
using Avalonia.VisualTree;
using PhotonCanvas.Addon.HouseEditor;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Materials;

/// <summary>The building editor's pictures (in its own help/images): a two-storey house on each floor's plan (with its walls cut away, then its roof on and picked), and its pieces.</summary>
static class HouseEditorShots
{
    const BindingFlags Any = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;

    static T F<T>(object o, string name) => (T)o.GetType().GetField(name, Any)!.GetValue(o)!;

    public static void Run(Window owner, Document doc, MachineProfile machine, MaterialLibrary lib, Action<int> settle)
    {
        var folder = Path.Combine(Environment.GetEnvironmentVariable("PHOTONCANVAS_HELP")!, "..", "..", "src", "Addons", "HouseEditor", "help", "images");
        Directory.CreateDirectory(folder);
        var data = Path.Combine(Path.GetTempPath(), "photoncanvas-helpshots-houseeditor");

        void Shot(string name, HouseDesign design, int level, int walls, int view, int side, Action<HouseEditorWindow>? also = null)
        {
            Directory.CreateDirectory(data);
            File.WriteAllText(Path.Combine(data, "last.json"), design.ToJson());
            var win = new HouseEditorWindow(doc, machine, lib, data, null);
            win.Show(owner);
            settle(300);
            typeof(HouseEditorWindow).GetMethod("ShowLevel", Any)!.Invoke(win, [level]);
            F<ComboBox>(win, "_walls").SelectedIndex = walls;
            var tabs = win.GetVisualDescendants().OfType<TabControl>().ToList();
            also?.Invoke(win);
            var t = (Task)typeof(HouseEditorWindow).GetMethod("Rebuild", Any)!.Invoke(win, null)!;
            for (int i = 0; i < 600 && !t.IsCompleted; i++) settle(50);
            foreach (var tc in tabs)
            {
                if (tc.Items.Count == 2 && tc.Items[0] is TabItem { Header: "Put together" }) tc.SelectedIndex = view;
                if (tc.Items.Count == 2 && tc.Items[0] is TabItem { Header: "Picked" }) tc.SelectedIndex = side;
            }
            settle(900);
            win.CaptureRenderedFrame()?.Save(Path.Combine(folder, name + ".png"));
            Console.WriteLine($"houseeditor: {name}.png");
            win.Close();
            settle(200);
        }

        var sample = HouseDesign.Sample();
        Shot("window-houseeditor-ground", sample, 0, 1, 0, 1);
        Shot("window-houseeditor-upstairs", sample, 1, 0, 0, 0, w =>
        {
            var plan = F<PlanEditor>(w, "_plan");
            plan.Tool = PlanTool.Roof;
            plan.SelectRoof(0);
        });
        Shot("window-houseeditor-pieces", sample, 0, 1, 1, 1);
    }
}
