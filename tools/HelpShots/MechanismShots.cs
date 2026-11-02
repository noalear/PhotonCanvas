using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using PhotonCanvas.Addon.Mechanism;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Materials;

/// <summary>
/// The mechanism maker's pictures (in its own help/images): each kind stopped part way round, cut with
/// a material from the library, and a cam the motion check finds problems with.
/// </summary>
static class MechanismShots
{
    const BindingFlags Any = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;

    static T F<T>(object o, string name) => (T)o.GetType().GetField(name, Any)!.GetValue(o)!;

    public static void Run(Window owner, Document doc, MachineProfile machine, MaterialLibrary lib, Action<int> settle)
    {
        var folder = Path.Combine(Environment.GetEnvironmentVariable("PHOTONCANVAS_HELP")!, "..", "..", "src", "Addons", "Mechanism", "help", "images");
        Directory.CreateDirectory(folder);

        void Shot(string name, int kind, MechSpec? spec, double turnedDeg, Action<MechanismWindow>? before = null, Action<MechanismWindow>? after = null)
        {
            // No data folder: the pictures show the settings given here, not the last ones used.
            var win = new MechanismWindow(doc, machine, lib);
            if (spec is not null) F<MechSpec[]>(win, "_specs")[kind] = spec;
            F<TabControl>(win, "_kind").SelectedIndex = kind;
            // Cut with the library's first cut settings (its kerf on the cut layer).
            var cut = F<ComboBox>(win, "_cutWith");
            if (cut.ItemCount > 1) cut.SelectedIndex = 1;
            before?.Invoke(win);
            win.Show(owner);
            settle(400);
            if (after is not null)
            {
                after(win);
                settle(400);
            }
            var t = (Task)typeof(MechanismWindow).GetMethod("BuildNow", Any)!.Invoke(win, null)!;
            for (int i = 0; i < 400 && !t.IsCompleted; i++) settle(50);
            var view = F<MotionView>(win, "_view");
            view.Playing = false;
            view.Input = turnedDeg * Math.PI / 180;
            settle(800);
            win.CaptureRenderedFrame()?.Save(Path.Combine(folder, name + ".png"));
            Console.WriteLine($"mechanism: {name}.png");
            win.Close();
            settle(200);
        }

        Shot("window-mechanism-gears", 0, null, 50);
        // Holes and handles: a D hole in A for a motor shaft, a handle in B 25 mm out, B's settings open.
        Shot("window-mechanism-holes", 0, new GearTrainSpec
        {
            Handle = false,
            Gears = [new() { Teeth = 36, Hole = new Hole { Shape = HoleShape.D, SizeMm = 5, FlatMm = 4.5, Fit = HoleFit.Snug } }, new() { Teeth = 30, Handle = new Crank { RadiusMm = 25 } }],
        }, 30, before: w =>
        {
            F<HashSet<int>>(w, "_openGears").Add(1);
            typeof(MechanismWindow).GetField("_shownKind", Any)!.SetValue(w, -1);
            typeof(MechanismWindow).GetMethod("ShowKind", Any)!.Invoke(w, null);
        }, after: w =>
        {
            // Down to gear B's hole and handle.
            var scroll = w.GetVisualDescendants().OfType<ScrollViewer>().First();
            scroll.Offset = new Avalonia.Vector(0, Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height - 200));
        });
        Shot("window-mechanism-rack", 1, null, 140);
        Shot("window-mechanism-planetary", 2, null, 40);
        Shot("window-mechanism-cam", 3, new CamSpec { Steps = CamPresets.All[6].Steps }, 250);
        Shot("window-mechanism-automaton", 4, null, 70);
        // A rise far too steep for so small a cam: the check marks it red, all the way round the strip.
        Shot("window-mechanism-problem", 3, new CamSpec { Steps = [new(CamMove.Rise, 40, 20), new(CamMove.Fall, 320, 20)], BaseRadiusMm = 15 }, 20);
    }
}
