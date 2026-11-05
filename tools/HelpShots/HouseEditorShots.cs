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
        Shot("window-houseeditor-furnish", sample, 0, 1, 0, 0, w =>
        {
            var plan = F<PlanEditor>(w, "_plan");
            typeof(HouseEditorWindow).GetMethod("PickTool", Any)!.Invoke(w, [PlanTool.Furnish]);
            plan.SelectItem(0);
            F<CheckBox>(w, "_doorsOpen").IsChecked = true;
        });

        // Saved buildings: three kept, then one being put in beside a cottage.
        var prefabs = Path.Combine(data, "prefabs");
        if (Directory.Exists(prefabs)) Directory.Delete(prefabs, true);
        void Keep(HousePrefab p) => HousePrefabs.Save(prefabs, p, PrefabPictures.Png(HouseKitMaker.Make(p.Design)));
        var inn = new HousePrefab { Name = "Riverside inn", Type = "Inn", Theme = sample.Style.Theme, District = "Docks", Tags = ["two floors", "terrace", "stable yard"], Notes = "Stairs in the back room, rooms to let upstairs.", Design = sample };
        Keep(inn);
        Keep(new HousePrefab { Name = "Two-room cottage", Type = "Cottage", Theme = sample.Style.Theme, District = "Residential", Tags = ["small", "starter"], Design = HouseDesign.Starter() });
        Keep(inn with { Id = Guid.NewGuid(), Name = "Corner tavern", Type = "Tavern", District = "Market", Tags = ["corner"], Design = sample.Turned(1) });
        {
            var lib2 = new SavedBuildingsWindow(prefabs, inn.Id);
            lib2.Show(owner);
            settle(900);
            lib2.CaptureRenderedFrame()?.Save(Path.Combine(folder, "window-houseeditor-saved.png"));
            Console.WriteLine("houseeditor: window-houseeditor-saved.png");
            lib2.Close();
            settle(200);
        }
        Shot("window-houseeditor-place", HouseDesign.Starter(), 0, 1, 0, 0, w =>
        {
            var plan = F<PlanEditor>(w, "_plan");
            typeof(HouseEditorWindow).GetMethod("PickTool", Any)!.Invoke(w, [PlanTool.Place]);
            plan.SetStamp(sample, inn.Name);
            typeof(HouseEditorWindow).GetMethod("ShowStamp", Any)!.Invoke(w, null);
            // Framed round the cottage and the inn going in beside it, the pointer over where it goes.
            double W = plan.Bounds.Width, H = plan.Bounds.Height, x0 = 10, y0 = 10, x1 = 26, y1 = 19;
            double k = Math.Min(W / (x1 - x0), H / (y1 - y0));
            typeof(PlanEditor).GetField("_scale", Any)!.SetValue(plan, k);
            typeof(PlanEditor).GetField("_offset", Any)!.SetValue(plan, new Avalonia.Vector((W - (x1 + x0) * k) / 2, (H - (y1 + y0) * k) / 2));
            typeof(PlanEditor).GetField("_fitted", Any)!.SetValue(plan, true);
            typeof(PlanEditor).GetField("_inside", Any)!.SetValue(plan, true);
            typeof(PlanEditor).GetField("_mouse", Any)!.SetValue(plan, new Avalonia.Point((20.5 * k) + (W - (x1 + x0) * k) / 2, (14.5 * k) + (H - (y1 + y0) * k) / 2));
            plan.InvalidateVisual();
        });
    }
}
