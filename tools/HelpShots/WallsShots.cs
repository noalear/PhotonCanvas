using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using PhotonCanvas.Addon.Walls;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Materials;

/// <summary>The wall maker's pictures (in its own help/images): a castle wall, a timber-framed house and the connectors.</summary>
static class WallsShots
{
    const BindingFlags Any = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;

    static T F<T>(object o, string name) => (T)o.GetType().GetField(name, Any)!.GetValue(o)!;

    public static void Run(Window owner, Document doc, MachineProfile machine, MaterialLibrary lib, Action<int> settle)
    {
        var folder = Path.Combine(Environment.GetEnvironmentVariable("PHOTONCANVAS_HELP")!, "..", "..", "src", "Addons", "Walls", "help", "images");
        Directory.CreateDirectory(folder);

        void Shot(string name, WallSpec spec, int tab)
        {
            // No data folder: the pictures show the settings given here, not the last ones used.
            var win = new WallsWindow(doc, machine, lib, null, null);
            typeof(WallsWindow).GetMethod("Load", Any)!.Invoke(win, [spec]);
            win.Show(owner);
            settle(300);
            var tabs = F<Border>(win, "_tabsHost").Child as TabControl ?? throw new InvalidOperationException("no tabs");
            tabs.SelectedIndex = tab;
            var t = (Task)typeof(WallsWindow).GetMethod("Rebuild", Any)!.Invoke(win, null)!;
            for (int i = 0; i < 400 && !t.IsCompleted; i++) settle(50);
            settle(600);
            win.CaptureRenderedFrame()?.Save(Path.Combine(folder, name + ".png"));
            Console.WriteLine($"walls: {name}.png");
            win.Close();
            settle(200);
        }

        Shot("window-walls-castle", new WallSpec
        {
            LengthMm = 120, HeightMm = 60, Texture = "Brick", Top = TopStyle.Battlements, Walkway = true,
            Door = DoorKind.DoorwayAndDoor, Windows = 2, WindowShape = OpeningShape.ArrowSlit, WindowWidthMm = 2, WindowHeightMm = 12, SillMm = 24,
            Connectors = new() { [ConnectorKind.Corner] = 2, [ConnectorKind.EndCap] = 1 },
        }, 2);
        Shot("window-walls-house", new WallSpec
        {
            LengthMm = 140, HeightMm = 55, Texture = "Timber frame", SizeA = 3, SizeB = 20, GrooveMm = 0.5,
            Door = DoorKind.ClosedDoor, DoorShape = OpeningShape.Rectangle, DoorOffsetMm = -30, Windows = 2, WindowShape = OpeningShape.Rectangle, WindowWidthMm = 12, WindowHeightMm = 14, Bars = WindowBars.Cross, SillMm = 26,
            Mounts = 2, MountHeightMm = 20, Accessories = Accessory.Sconces, Connectors = [],
        }, 3);
        Shot("window-walls-connectors", new WallSpec
        {
            LengthMm = 100, HeightMm = 45, Texture = "Fieldstone", SizeA = 9, SizeB = 6, GrooveMm = 0.9, Top = TopStyle.Ruined, Seed = 3,
            Connectors = new() { [ConnectorKind.Straight] = 1, [ConnectorKind.Corner] = 1, [ConnectorKind.Angle135] = 1, [ConnectorKind.Tee] = 1, [ConnectorKind.EndCap] = 1 },
        }, 5);
    }
}
