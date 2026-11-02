using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using PhotonCanvas.Addon.Furniture;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Materials;

/// <summary>The furniture maker's pictures (in its own help/images): a chair put together, a chest's pieces and a wall door.</summary>
static class FurnitureShots
{
    const BindingFlags Any = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;

    static T F<T>(object o, string name) => (T)o.GetType().GetField(name, Any)!.GetValue(o)!;

    public static void Run(Window owner, Document doc, MachineProfile machine, MaterialLibrary lib, Action<int> settle)
    {
        var folder = Path.Combine(Environment.GetEnvironmentVariable("PHOTONCANVAS_HELP")!, "..", "..", "src", "Addons", "Furniture", "help", "images");
        Directory.CreateDirectory(folder);
        // A scratch data folder holding the set to show, so the pictures don't depend on the last one used.
        var data = Path.Combine(Path.GetTempPath(), "photoncanvas-helpshots-furniture");

        void Shot(string name, FurnitureSet set, int item, int view)
        {
            Directory.CreateDirectory(data);
            File.WriteAllText(Path.Combine(data, "last.json"), set.ToJson());
            var win = new FurnitureWindow(doc, machine, lib, data, null);
            win.Show(owner);
            settle(300);
            F<ListBox>(win, "_list").SelectedIndex = item;
            F<TabControl>(win, "_views").SelectedIndex = view;
            settle(100);
            var t = (Task)typeof(FurnitureWindow).GetMethod("Rebuild", Any)!.Invoke(win, null)!;
            for (int i = 0; i < 400 && !t.IsCompleted; i++) settle(50);
            settle(600);
            win.CaptureRenderedFrame()?.Save(Path.Combine(folder, name + ".png"));
            Console.WriteLine($"furniture: {name}.png");
            win.Close();
            settle(200);
        }

        var tavern = new FurnitureSet
        {
            Items =
            [
                Defaults.For(ItemKind.Table) with { Count = 2 },
                Defaults.For(ItemKind.Chair) with { Count = 4 },
                Defaults.For(ItemKind.Bench) with { Count = 2 },
                Defaults.For(ItemKind.Bookshelf),
                Defaults.For(ItemKind.Chest),
            ],
        };
        Shot("window-furniture-chair", tavern, 1, 0);
        Shot("window-furniture-pieces", tavern, 4, 1);
        Shot("window-furniture-door", new FurnitureSet { Items = [Defaults.For(ItemKind.WallDoor), Defaults.For(ItemKind.WallShelf)] }, 0, 0);
    }
}
