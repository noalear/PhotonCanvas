using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using PhotonCanvas.Addon.Boxes;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Materials;

/// <summary>
/// The box maker's pictures (in its own help/images): a finger-jointed box put together, a hinged one
/// open with dividers, a rounded box's pieces, a box with a bending lid, and a panel split with dovetails.
/// </summary>
static class BoxesShots
{
    const BindingFlags Any = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;

    static T F<T>(object o, string name) => (T)o.GetType().GetField(name, Any)!.GetValue(o)!;

    public static void Run(Window owner, Document doc, MachineProfile machine, MaterialLibrary lib, Action<int> settle)
    {
        var folder = Path.Combine(Environment.GetEnvironmentVariable("PHOTONCANVAS_HELP")!, "..", "..", "src", "Addons", "Boxes", "help", "images");
        Directory.CreateDirectory(folder);

        void Shot(string name, BoxSpec spec, int view, int tab = 0, bool open = false)
        {
            // No data folder: the pictures show the settings given here, not the last ones used.
            var win = new BoxesWindow(doc, machine, lib, null, null);
            win.Show(owner);
            settle(200);
            typeof(BoxesWindow).GetMethod("Load", Any)!.Invoke(win, [spec]);
            var tabs = win.GetVisualDescendants().OfType<TabControl>().ToList();
            tabs[0].SelectedIndex = tab;
            tabs[^1].SelectedIndex = view;
            F<CheckBox>(win, "_open").IsChecked = open;
            settle(100);
            // Timers don't run here: the box is made now.
            var t = win.RebuildNow();
            for (int i = 0; i < 400 && !t.IsCompleted; i++) settle(50);
            F<AssembledView>(win, "_assembled").LidOpen = open ? 1 : 0;
            settle(800);
            win.CaptureRenderedFrame()?.Save(Path.Combine(folder, name + ".png"));
            Console.WriteLine($"boxes: {name}.png");
            win.Close();
            settle(200);
        }

        Shot("window-boxes", new BoxSpec { WidthMm = 120, DepthMm = 80, HeightMm = 50 }, 0);
        Shot("window-boxes-hinged", new BoxSpec { WidthMm = 120, DepthMm = 80, HeightMm = 50, Lid = LidKind.Hinged, DividersAcross = 2, DividersAlong = 1 }, 0, tab: 1, open: true);
        Shot("window-boxes-rounded", new BoxSpec { Style = BoxStyle.Rounded, WidthMm = 100, DepthMm = 70, HeightMm = 40, CornerRadiusMm = 18 }, 1, tab: 2);
        Shot("window-boxes-bending", new BoxSpec { Style = BoxStyle.BendingLid, WidthMm = 110, DepthMm = 75, HeightMm = 45, BendRadiusMm = 12 }, 0);

        // A panel longer than the bed, split with dovetails.
        var layer = doc.State.Layers.FirstOrDefault(l => l.Mode == LayerMode.Cut) ?? doc.State.Layers[0];
        var panel = new PathShape { Name = "Shelf", LayerId = layer.Id, Geometry = PathGeometry.RoundRect(10, 40, 380, 120, 6, 6) };
        doc.Commit("Shelf", Ops.AddShapes(doc.State, [panel]), [panel.Id]);
        var split = new SplitWindow(doc, machine, [panel]);
        split.Show(owner);
        settle(800);
        split.CaptureRenderedFrame()?.Save(Path.Combine(folder, "window-split.png"));
        Console.WriteLine("boxes: window-split.png");
        split.Close();
        settle(200);
    }
}
