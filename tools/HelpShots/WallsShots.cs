using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using PhotonCanvas.Addon.Walls;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Materials;

/// <summary>The wall maker's pictures (in its own help/images): a castle wall, a timber-framed house, a ruin, the connectors and a flat wall.</summary>
static class WallsShots
{
    const BindingFlags Any = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;

    static T F<T>(object o, string name) => (T)o.GetType().GetField(name, Any)!.GetValue(o)!;

    public static void Run(Window owner, Document doc, MachineProfile machine, MaterialLibrary lib, Action<int> settle)
    {
        var folder = Path.Combine(Environment.GetEnvironmentVariable("PHOTONCANVAS_HELP")!, "..", "..", "src", "Addons", "Walls", "help", "images");
        Directory.CreateDirectory(folder);

        void Shot(string name, WallSpec spec, int tab, int view = 0)
        {
            // No data folder: the pictures show the settings given here, not the last ones used.
            var win = new WallsWindow(doc, machine, lib, null, null);
            typeof(WallsWindow).GetMethod("Load", Any)!.Invoke(win, [spec]);
            win.Show(owner);
            settle(300);
            var tabs = F<Border>(win, "_tabsHost").Child as TabControl ?? throw new InvalidOperationException("no tabs");
            tabs.SelectedIndex = tab;
            F<TabControl>(win, "_views").SelectedIndex = view;
            var t = (Task)typeof(WallsWindow).GetMethod("Rebuild", Any)!.Invoke(win, null)!;
            for (int i = 0; i < 400 && !t.IsCompleted; i++) settle(50);
            settle(600);
            win.CaptureRenderedFrame()?.Save(Path.Combine(folder, name + ".png"));
            Console.WriteLine($"walls: {name}.png");
            win.Close();
            settle(200);
        }

        Shot("window-walls-castle", WallSpec.NewBoxed with
        {
            LengthMm = 120, HeightMm = 64, Texture = "Stone blocks", SizeA = 12, SizeB = 6, GrooveMm = 0.8, Top = TopStyle.Battlements, Walkway = true, WalkwayDepthMm = 18,
            Door = DoorKind.DoorwayAndDoor, Windows = 2, WindowShape = OpeningShape.ArrowSlit, WindowWidthMm = 2.5, WindowHeightMm = 12, SillMm = 26,
            ColumnTop = ColumnTop.Battlements, ColumnBase = ColumnBase.SteppedPlinth,
            Connectors = new() { [ConnectorKind.Corner] = 2, [ConnectorKind.EndCap] = 1 },
        }, 2);
        Shot("window-walls-house", WallSpec.NewBoxed with
        {
            LengthMm = 140, HeightMm = 55, Texture = "Timber frame", SizeA = 3, SizeB = 20, GrooveMm = 0.5, TextureSides = true, EndsIntoColumns = false,
            Door = DoorKind.ClosedDoor, DoorShape = OpeningShape.Rectangle, DoorOffsetMm = -30, Windows = 2, WindowShape = OpeningShape.Rectangle, WindowWidthMm = 12, WindowHeightMm = 14, Bars = WindowBars.Cross, SillMm = 26,
            Mounts = 2, MountHeightMm = 20, Accessories = Accessory.Sconces, Connectors = [],
        }, 3, 1);
        Shot("window-walls-connectors", WallSpec.NewBoxed with
        {
            LengthMm = 100, HeightMm = 50, Texture = "Fieldstone", SizeA = 9, SizeB = 6, GrooveMm = 0.9, Broken = RuinStyle.Crumbled, Seed = 3, ColumnTop = ColumnTop.SteppedCap,
            Connectors = new() { [ConnectorKind.Straight] = 1, [ConnectorKind.Corner] = 1, [ConnectorKind.Tee] = 1, [ConnectorKind.EndCap] = 1 },
        }, 5);
        Shot("window-walls-mounts", WallSpec.NewBoxed with
        {
            LengthMm = 120, HeightMm = 55, Texture = "Stone blocks", SizeA = 12, SizeB = 6, GrooveMm = 0.8, AccessoryTexture = "Wood planks (across)", ShelfDepthMm = 12,
            MountPoints =
            [
                new() { XMm = 22, YMm = 24, Item = MountItem.Sconce },
                new() { XMm = 60, YMm = 30, Item = MountItem.Shelf, Holes = 2, SpacingMm = 30 },
                new() { XMm = 98, YMm = 26, Item = MountItem.Peg, Holes = 2, SpacingMm = 10 },
            ],
            Connectors = new() { [ConnectorKind.Corner] = 2 },
        }, 4, 1);
        Shot("window-walls-ruins", WallSpec.NewBoxed with
        {
            LengthMm = 130, HeightMm = 60, Texture = "Brick", Top = TopStyle.Battlements, Merlons = MerlonPattern.Alternating, MerlonHeightMm = 8, MerlonStepMm = 3,
            Broken = RuinStyle.Breach, RuinDepth = 0.45, RuinAt = RuinPlace.Middle, Seed = 4, Windows = 2, WindowShape = OpeningShape.ArrowSlit, WindowWidthMm = 2.5, WindowHeightMm = 12, SillMm = 20,
            Connectors = new() { [ConnectorKind.Corner] = 2 },
        }, 2);
        Shot("window-walls-flat", new WallSpec
        {
            LengthMm = 100, HeightMm = 45, Texture = "Brick", Top = TopStyle.Battlements,
            Connectors = new() { [ConnectorKind.Corner] = 2, [ConnectorKind.Angle135] = 1 },
        }, 0, 1);
    }
}
