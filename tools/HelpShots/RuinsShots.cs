using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using PhotonCanvas.Addon.Ruins;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Materials;

/// <summary>The ruins maker's pictures (in its own help/images): a broken brick wall, a wall on its base, rubble piles and pillars.</summary>
static class RuinsShots
{
    const BindingFlags Any = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;

    static T F<T>(object o, string name) => (T)o.GetType().GetField(name, Any)!.GetValue(o)!;

    public static void Run(Window owner, Document doc, MachineProfile machine, MaterialLibrary lib, Action<int> settle)
    {
        var folder = Path.Combine(Environment.GetEnvironmentVariable("PHOTONCANVAS_HELP")!, "..", "..", "src", "Addons", "Ruins", "help", "images");
        Directory.CreateDirectory(folder);

        void Shot(string name, RuinSpec spec, int tab)
        {
            // No data folder: the pictures show the settings given here, not the last ones used.
            var win = new RuinsWindow(doc, machine, lib, null, null);
            typeof(RuinsWindow).GetMethod("Load", Any)!.Invoke(win, [spec]);
            win.Show(owner);
            settle(300);
            var tabs = F<Border>(win, "_tabsHost").Child as TabControl ?? throw new InvalidOperationException("no tabs");
            tabs.SelectedIndex = tab;
            var t = (Task)typeof(RuinsWindow).GetMethod("Rebuild", Any)!.Invoke(win, null)!;
            for (int i = 0; i < 400 && !t.IsCompleted; i++) settle(50);
            settle(600);
            win.CaptureRenderedFrame()?.Save(Path.Combine(folder, name + ".png"));
            Console.WriteLine($"ruins: {name}.png");
            win.Close();
            settle(200);
        }

        Shot("window-ruins-wall", new RuinSpec
        {
            LengthMm = 120, HeightMm = 55, Texture = "Brick", Seed = 4, Break = BreakShape.Dip, BreakDepth = 0.6,
            Breaches = 2, BreachMm = 12, KnockedOut = 4, Cracks = 3, LooseStones = 0, Chunks = 0,
        }, 2);
        Shot("window-ruins-base", new RuinSpec
        {
            LengthMm = 90, HeightMm = 45, Texture = "Fieldstone", SizeA = 9, SizeB = 6, GrooveMm = 0.9, Seed = 3,
            LeftEnd = EndKind.Broken, RightEnd = EndKind.Broken, Break = BreakShape.FallsRight, BreakDepth = 0.65,
            KnockedOut = 2, Cracks = 1, Base = true, BaseMm = 18, PileHeightMm = 9, LooseStones = 0, Chunks = 0,
        }, 3);
        Shot("window-ruins-rubble", new RuinSpec
        {
            Wall = false, Texture = "Stone blocks", SizeA = 8, SizeB = 5, GrooveMm = 0.7, Seed = 2,
            Piles = 2, PileWidthMm = 45, PileDepthMm = 32, PileHeightMm = 12, LooseStones = 0, Chunks = 0,
            Pillars = 2, PillarMm = 16, PillarHeightMm = 30,
        }, 4);
    }
}
