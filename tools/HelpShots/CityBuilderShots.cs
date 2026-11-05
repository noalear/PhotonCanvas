using System.Reflection;
using Avalonia.Headless;
using Avalonia.Controls;
using PhotonCanvas.Addon.CityBuilder;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Materials;

/// <summary>The city builder's pictures (in its own help/images): the sample village on the plan and in 3D, and the sample battle map read into a town.</summary>
static class CityBuilderShots
{
    const BindingFlags Any = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;

    static T F<T>(object o, string name) => (T)o.GetType().GetField(name, Any)!.GetValue(o)!;

    public static void Run(Window owner, Document doc, MachineProfile machine, MaterialLibrary lib, Action<int> settle)
    {
        var folder = Path.Combine(Environment.GetEnvironmentVariable("PHOTONCANVAS_HELP")!, "..", "..", "src", "Addons", "CityBuilder", "help", "images");
        Directory.CreateDirectory(folder);
        // A scratch data folder, so the pictures don't depend on the last map made.
        var data = Path.Combine(Path.GetTempPath(), "photoncanvas-helpshots-citybuilder");

        void Shot(string name, CityMap map, int left, int view, Func<CityWindow, Task>? also = null)
        {
            Directory.CreateDirectory(data);
            File.WriteAllText(Path.Combine(data, "last.json"), map.ToJson());
            var win = new CityWindow(doc, machine, lib, data);
            win.Show(owner);
            settle(300);
            if (also is not null)
            {
                var t = also(win);
                for (int i = 0; i < 1200 && !t.IsCompleted; i++) settle(50);
            }
            foreach (var tc in win.GetLogicalDescendantsOf<TabControl>())
            {
                if (tc.Items.Count == 4 && tc.Items[0] is TabItem { Header: "Map" }) tc.SelectedIndex = left;
                if (tc.Items.Count == 2 && tc.Items[0] is TabItem { Header: "Plan" }) tc.SelectedIndex = view;
            }
            settle(1500);
            win.CaptureRenderedFrame()?.Save(Path.Combine(folder, name + ".png"));
            Console.WriteLine($"citybuilder: {name}.png");
            win.Close();
            settle(200);
        }

        var village = CityWindow.Sample();
        Shot("window-citybuilder-plan", village, 2, 0);
        Shot("window-citybuilder-3d", village, 0, 1);
        Shot("window-citybuilder-picked", village, 3, 0, w =>
        {
            F<CityPlan>(w, "_plan").Select(village.Buildings[0].Id);
            return Task.CompletedTask;
        });
        Shot("window-citybuilder-battlemap", CityMap.Empty(40, 28), 1, 0, async w =>
        {
            await w.UsePicture(SampleBattleMap.Make(), null);
            await w.ReadPicture();
        });
        Shot("window-citybuilder-battlemap-3d", CityMap.Empty(40, 28), 1, 1, async w =>
        {
            await w.UsePicture(SampleBattleMap.Make(), null);
            await w.ReadPicture();
        });
    }

    static IEnumerable<T> GetLogicalDescendantsOf<T>(this Control c) => Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(c).OfType<T>();
}
