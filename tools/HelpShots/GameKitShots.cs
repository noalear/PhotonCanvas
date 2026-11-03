using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using PhotonCanvas.Addon.GameKit;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Materials;

/// <summary>The game accessories maker's pictures (in its own help/images): a dice tower put together and its pieces.</summary>
static class GameKitShots
{
    const BindingFlags Any = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;

    static T F<T>(object o, string name) => (T)o.GetType().GetField(name, Any)!.GetValue(o)!;

    public static void Run(Window owner, Document doc, MachineProfile machine, MaterialLibrary lib, Action<int> settle)
    {
        var folder = Path.Combine(Environment.GetEnvironmentVariable("PHOTONCANVAS_HELP")!, "..", "..", "src", "Addons", "GameKit", "help", "images");
        // PHOTONCANVAS_GAMEKIT_ALL=<folder>: every kind, put together and in pieces, into that folder (for checking by eye).
        var all = Environment.GetEnvironmentVariable("PHOTONCANVAS_GAMEKIT_ALL");
        Directory.CreateDirectory(all ?? folder);
        // A scratch data folder holding the set to show, so the pictures don't depend on the last one used.
        var data = Path.Combine(Path.GetTempPath(), "photoncanvas-helpshots-gamekit");

        void Shot(string into, string name, AccessorySet set, int item, int view, int left = 0)
        {
            Directory.CreateDirectory(data);
            File.WriteAllText(Path.Combine(data, "last.json"), set.ToJson());
            var win = new GameKitWindow(doc, machine, lib, data, null);
            win.Show(owner);
            settle(300);
            F<ListBox>(win, "_list").SelectedIndex = item;
            F<TabControl>(win, "_views").SelectedIndex = view;
            if (left > 0) Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(win).OfType<TabControl>().First(t => t != F<TabControl>(win, "_views")).SelectedIndex = left;
            settle(100);
            var t = (Task)typeof(GameKitWindow).GetMethod("Rebuild", Any)!.Invoke(win, null)!;
            for (int i = 0; i < 400 && !t.IsCompleted; i++) settle(50);
            settle(600);
            win.CaptureRenderedFrame()?.Save(Path.Combine(into, name + ".png"));
            Console.WriteLine($"gamekit: {name}.png");
            win.Close();
            settle(200);
        }

        var table = new AccessorySet
        {
            Items = [.. Defaults.Kinds.Select(k => Defaults.For(k.Kind) with { Names = k.Kind == AccessoryKind.Initiative ? "Aria\nBorin\nCass\nGoblin boss" : "" })],
        };
        if (all is not null)
        {
            for (int i = 0; i < table.Items.Count; i++)
                for (int v = 0; v < 2; v++)
                    Shot(all, $"{table.Items[i].Kind}-{(v == 0 ? "together" : "pieces")}", table, i, v);
            return;
        }
        Shot(folder, "window-gamekit-tower", table, 0, 0);
        Shot(folder, "window-gamekit-tower-pieces", table, 0, 1);
        // The markings: a gauge in the Medieval design with a title, and templates in the Gothic one.
        var marked = marked0();
        Shot(folder, "window-gamekit-markings", marked, 0, 1, left: 1);
        Shot(folder, "window-gamekit-template-marked", marked, 2, 1, left: 1);
        static AccessorySet marked0() => new AccessorySet
        {
            Items =
            [
                Defaults.For(AccessoryKind.MoveGauge) with { Segments = 6, Marks = MarkDesigns.Apply(new Markings(), "Medieval") with { Title = "MOVE" } },
                Defaults.For(AccessoryKind.Ruler) with { Marks = MarkDesigns.Apply(new Markings(), "Sci-fi") with { Title = "RANGE" } },
                Defaults.For(AccessoryKind.Template) with { LengthUnits = 2, Marks = MarkDesigns.Apply(new Markings(), "Gothic") with { Title = "FIREBALL", Numbers = Numbering.Numbers } },
            ],
        };
    }
}
