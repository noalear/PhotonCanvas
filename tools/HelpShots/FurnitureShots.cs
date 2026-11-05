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

        void Shot(string name, FurnitureSet set, int item, int view, int tab = 0)
        {
            Directory.CreateDirectory(data);
            File.WriteAllText(Path.Combine(data, "last.json"), set.ToJson());
            var win = new FurnitureWindow(doc, machine, lib, data, null);
            win.Show(owner);
            settle(300);
            F<ListBox>(win, "_list").SelectedIndex = item;
            F<TabControl>(win, "_views").SelectedIndex = view;
            if (((Grid)win.Content!).Children[0] is Border { Child: TabControl left }) left.SelectedIndex = tab;
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
        Shot("window-furniture-push-fit", tavern with { PushFit = true }, 4, 1, 1);
        Shot("window-furniture-door", new FurnitureSet { Items = [Defaults.For(ItemKind.WallDoor), Defaults.For(ItemKind.WallShelf)] }, 0, 0);

        // Ready-made pieces: a gothic hall, a medieval bedroom, a starship's bridge and a crypt.
        FurnitureItem C(string id) => FurnitureCatalog.Find(id)!.Item;
        var gothic = FurnitureThemes.Apply(new FurnitureSet { Items = [C("got-grand-fireplace"), C("got-wing-chair"), C("got-grandfather-clock"), C("got-glass-bookcase"), C("got-candelabra")] }, "Gothic");
        Shot("window-furniture-ready-made", gothic, 0, 0);
        Shot("window-furniture-clock", gothic, 2, 0);
        var bedroom = FurnitureThemes.Apply(new FurnitureSet { Items = [C("med-four-poster-bed"), C("med-wardrobe"), C("med-chest-of-drawers"), C("med-bedroom-rug")] }, "Medieval");
        Shot("window-furniture-four-poster", bedroom, 0, 0);
        Shot("window-furniture-wardrobe", bedroom, 1, 0);
        var bridge = FurnitureThemes.Apply(new FurnitureSet { Items = [C("sci-captains-chair"), C("sci-helm-console"), C("sci-lockers"), C("sci-rifle-rack"), C("sci-sleeper-pod")] }, "Sci-fi");
        Shot("window-furniture-console", bridge, 1, 0);
        Shot("window-furniture-lockers", bridge, 2, 0);
        Shot("window-furniture-cryo-pod", bridge, 4, 0);
        var ship = FurnitureThemes.Apply(new FurnitureSet { Items = [C("sci-reactor-core"), C("sci-crew-bunks"), C("sci-fuel-drums"), C("sci-medical-bed"), C("sci-mess-table")] }, "Sci-fi");
        Shot("window-furniture-reactor", ship, 0, 0);
        Shot("window-furniture-crew-bunks", ship, 1, 0);
        Shot("window-furniture-med-bay", ship, 3, 0);
        var med = FurnitureThemes.Apply(new FurnitureSet { Items = [C("med-throne"), C("med-weapon-rack"), C("med-armour-stand"), C("med-smithy-forge"), C("med-dresser-with-plates")] }, "Medieval");
        Shot("window-furniture-throne", med, 0, 0);
        Shot("window-furniture-weapon-rack", med, 1, 0);
        Shot("window-furniture-armour", med, 2, 0);
        Shot("window-furniture-forge", med, 3, 0);
        Shot("window-furniture-dresser", med, 4, 0);
        var street = FurnitureThemes.Apply(new FurnitureSet { Items = [C("med-watchtower"), C("med-statue"), C("med-street-lamp"), C("med-market-cross"), C("got-statue")] }, "Medieval");
        Shot("window-furniture-watchtower", street, 0, 0);
        Shot("window-furniture-statue", street, 1, 0);
        Shot("window-furniture-angel", street, 4, 0);
        var crypt = FurnitureThemes.Apply(new FurnitureSet { Items = [C("got-sarcophagus"), C("got-coffin"), C("got-tracery-screen")] }, "Gothic");
        Shot("window-furniture-sarcophagus", crypt, 0, 0);
        Shot("window-furniture-screen", crypt, 2, 0);
    }
}
