using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using PhotonCanvas.Addon.Roofs;
using PhotonCanvas.Addon.Walls;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Materials;

/// <summary>The roof and tower maker's pictures (in its own help/images): a cottage roof, its pieces, a hipped roof and two towers.</summary>
static class RoofsShots
{
    const BindingFlags Any = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;

    static T F<T>(object o, string name) => (T)o.GetType().GetField(name, Any)!.GetValue(o)!;

    public static void Run(Window owner, Document doc, MachineProfile machine, MaterialLibrary lib, Action<int> settle)
    {
        var folder = Path.Combine(Environment.GetEnvironmentVariable("PHOTONCANVAS_HELP")!, "..", "..", "src", "Addons", "Roofs", "help", "images");
        Directory.CreateDirectory(folder);

        void Shot(string name, RoofsDesign design, int tab, int view)
        {
            // No data folder: the pictures show the settings given here, not the last ones used.
            var win = new RoofsWindow(doc, machine, lib, null, null, design.Tower is not null);
            typeof(RoofsWindow).GetMethod("Load", Any)!.Invoke(win, [design]);
            win.Show(owner);
            settle(300);
            var tabs = F<Border>(win, "_tabsHost").Child as TabControl ?? throw new InvalidOperationException("no tabs");
            tabs.SelectedIndex = tab;
            F<TabControl>(win, "_views").SelectedIndex = view;
            var t = (Task)typeof(RoofsWindow).GetMethod("Rebuild", Any)!.Invoke(win, null)!;
            for (int i = 0; i < 600 && !t.IsCompleted; i++) settle(50);
            settle(600);
            win.CaptureRenderedFrame()?.Save(Path.Combine(folder, name + ".png"));
            Console.WriteLine($"roofs: {name}.png");
            win.Close();
            settle(200);
        }

        var cottage = RoofMaker.FitToWalls(new RoofSpec { Shingle = "Wooden shingles", ShingleA = 5, ShingleB = 5, ShingleGrooveMm = 0.6, Window = GableWindow.Arched },
            new WallSpec { LengthMm = 140, Texture = "Timber frame", SizeA = 3, SizeB = 20, GrooveMm = 0.5 }, new WallSpec { LengthMm = 90, Texture = "Timber frame" });
        Shot("window-roofs-cottage", new RoofsDesign { Roof = cottage }, 0, 0);
        Shot("window-roofs-pieces", new RoofsDesign { Roof = cottage }, 1, 1);
        Shot("window-roofs-hipped", new RoofsDesign { Roof = new RoofSpec { Style = RoofStyle.Hip, LengthMm = 150, WidthMm = 110, PitchDeg = 40, Shingle = "Slate" } }, 1, 0);
        Shot("window-towers-castle", new RoofsDesign { Tower = new TowerSpec { DiameterMm = 64, HeightMm = 96, Join = ConnectorKind.Corner, JoinFirstDeg = 0, WallHeightMm = 60, DoorAngleDeg = 225 } }, 4, 0);
        Shot("window-towers-cone", new RoofsDesign { Tower = new TowerSpec { DiameterMm = 50, HeightMm = 66, Storeys = 1, Texture = "Brick", SizeA = 7, SizeB = 3, GrooveMm = 0.7, Top = TowerTop.Cone, WindowShape = OpeningShape.Arched, WindowWidthMm = 6, WindowHeightMm = 10, SillMm = 14 } }, 2, 0);
    }
}
