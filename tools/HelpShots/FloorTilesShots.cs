using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using PhotonCanvas.Addon.FloorTiles;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Materials;

/// <summary>The floor tile maker's pictures (in its own help/images): a dungeon board, loose flagstone tiles and cobbles with clips.</summary>
static class FloorTilesShots
{
    const BindingFlags Any = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;

    public static void Run(Window owner, Document doc, MachineProfile machine, MaterialLibrary lib, Action<int> settle)
    {
        var folder = Path.Combine(Environment.GetEnvironmentVariable("PHOTONCANVAS_HELP")!, "..", "..", "src", "Addons", "FloorTiles", "help", "images");
        Directory.CreateDirectory(folder);

        void Shot(string name, TileSpec spec, int tab)
        {
            // No data folder: the pictures show the settings given here, not the last ones used.
            var win = new FloorTilesWindow(doc, machine, lib, null, null);
            typeof(FloorTilesWindow).GetMethod("Load", Any)!.Invoke(win, [spec]);
            win.Show(owner);
            settle(300);
            var tabs = win.GetVisualDescendantsOf<TabControl>().First();
            tabs.SelectedIndex = tab;
            var t = (Task)typeof(FloorTilesWindow).GetMethod("Rebuild", Any)!.Invoke(win, null)!;
            for (int i = 0; i < 400 && !t.IsCompleted; i++) settle(50);
            settle(600);
            win.CaptureRenderedFrame()?.Save(Path.Combine(folder, name + ".png"));
            Console.WriteLine($"floortiles: {name}.png");
            win.Close();
            settle(200);
        }

        Shot("window-floortiles-dungeon", new TileSpec { BoardAcross = 6, BoardDown = 4, Texture = "Dungeon flagstones", SizeA = 50, GrooveMm = 0.8 }, 0);
        Shot("window-floortiles-flagstones", new TileSpec
        {
            Mode = TileMode.Tiles, Texture = "Flagstones", SizeA = 10, GrooveMm = 0.8, Grid = GridMarks.Scored, Seed = 4,
            Tiles = [new() { Across = 2, Down = 2, Count = 2 }, new() { Across = 2, Down = 1, Count = 2, Plain = PlainSides.One }, new() { Across = 1, Down = 1, Count = 2, Plain = PlainSides.Corner }],
        }, 1);
        Shot("window-floortiles-clips", new TileSpec { BoardAcross = 4, BoardDown = 3, TileAcross = 2, TileDown = 3, Texture = "Floorboards", SizeA = 5.08, SizeB = 60, GrooveMm = 0.5, Join = TileJoin.Clips, BaseLayer = true }, 2);
        Shot("window-floortiles-base", new TileSpec { BoardAcross = 4, BoardDown = 4, Texture = "Dungeon flagstones", SizeA = 50, GrooveMm = 0.8, BaseLayer = true, PlainBorder = false }, 2);
    }

    static IEnumerable<T> GetVisualDescendantsOf<T>(this Avalonia.Visual v) => Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(v).OfType<T>();
}
