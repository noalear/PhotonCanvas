using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using PhotonCanvas.Addon.Scatter;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Materials;

/// <summary>The scatter terrain maker's pictures (in its own help/images): a village's worth on the list, a well, a cart's pieces and a graveyard.</summary>
static class ScatterShots
{
    const BindingFlags Any = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;

    static T F<T>(object o, string name) => (T)o.GetType().GetField(name, Any)!.GetValue(o)!;

    public static void Run(Window owner, Document doc, MachineProfile machine, MaterialLibrary lib, Action<int> settle)
    {
        var folder = Path.Combine(Environment.GetEnvironmentVariable("PHOTONCANVAS_HELP")!, "..", "..", "src", "Addons", "Scatter", "help", "images");
        // SCATTER_REVIEW=<folder>: also every kind put together and in pieces there, to look over.
        var review = Environment.GetEnvironmentVariable("SCATTER_REVIEW");
        Directory.CreateDirectory(folder);
        // A scratch data folder holding the set to show, so the pictures don't depend on the last one used.
        var data = Path.Combine(Path.GetTempPath(), "photoncanvas-helpshots-scatter");

        void Shot(string to, string name, ScatterSet set, int item, int view)
        {
            Directory.CreateDirectory(data);
            File.WriteAllText(Path.Combine(data, "last.json"), set.ToJson());
            var win = new ScatterWindow(doc, machine, lib, data, null);
            win.Show(owner);
            settle(300);
            F<ListBox>(win, "_list").SelectedIndex = item;
            F<TabControl>(win, "_views").SelectedIndex = view;
            settle(100);
            var t = (Task)typeof(ScatterWindow).GetMethod("Rebuild", Any)!.Invoke(win, null)!;
            for (int i = 0; i < 400 && !t.IsCompleted; i++) settle(50);
            settle(600);
            win.CaptureRenderedFrame()?.Save(Path.Combine(to, name + ".png"));
            Console.WriteLine($"scatter: {name}.png");
            win.Close();
            settle(200);
        }

        var village = new ScatterSet
        {
            Items =
            [
                Defaults.For(ItemKind.MarketStall),
                Defaults.For(ItemKind.Barrel) with { Count = 4 },
                Defaults.For(ItemKind.Fence) with { Count = 3 },
                Defaults.For(ItemKind.Cart),
                Defaults.For(ItemKind.Well),
            ],
        };
        Shot(folder, "window-scatter-stall", village, 0, 0);
        Shot(folder, "window-scatter-well", village, 4, 0);
        Shot(folder, "window-scatter-pieces", village, 3, 1);
        var graveyard = new ScatterSet
        {
            Items =
            [
                Defaults.For(ItemKind.Gravestone) with { Count = 3 },
                Defaults.For(ItemKind.Gravestone) with { Shape = StoneShape.Celtic, WidthMm = 10, HeightMm = 17, SlabMm = 18 },
                Defaults.For(ItemKind.Gravestone) with { Shape = StoneShape.Pointed, Weathered = true },
                Defaults.For(ItemKind.Fence) with { Style = FenceStyle.Wattle, Count = 4 },
            ],
        };
        Shot(folder, "window-scatter-graves", graveyard, 1, 0);
        if (review is null) return;
        Directory.CreateDirectory(review);
        var every = new ScatterSet { Items = [.. Defaults.Kinds.Select(k => Defaults.For(k.Kind))] };
        for (int i = 0; i < every.Items.Count; i++)
        {
            Shot(review, $"{every.Items[i].Kind}-together", every, i, 0);
            Shot(review, $"{every.Items[i].Kind}-pieces", every, i, 1);
        }
        var styles = new ScatterSet { Items = [.. Enum.GetValues<FenceStyle>().Select(s => Defaults.For(ItemKind.Fence) with { Style = s }), .. Enum.GetValues<StoneShape>().Select(s => Defaults.For(ItemKind.Gravestone) with { Shape = s, SlabMm = 12, Weathered = true }), Defaults.For(ItemKind.Barrel) with { Open = true, WidthMm = 14, HeightMm = 18 }] };
        for (int i = 0; i < styles.Items.Count; i++) Shot(review, $"style-{i}-{styles.Items[i].Kind}", styles, i, 1);
    }
}
