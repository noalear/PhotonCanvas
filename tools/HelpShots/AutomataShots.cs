using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using PhotonCanvas.Addon.Automata;
using PhotonCanvas.Addon.Mechanism;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Materials;

/// <summary>The toys and automata maker's pictures (in its own help/images): the figure box moving, a music box strip and a music box comb.</summary>
static class AutomataShots
{
    const BindingFlags Any = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;

    static T F<T>(object o, string name) => (T)o.GetType().GetField(name, Any)!.GetValue(o)!;

    public static void Run(Window owner, Document doc, MachineProfile machine, MaterialLibrary lib, Action<int> settle)
    {
        var folder = Path.Combine(Environment.GetEnvironmentVariable("PHOTONCANVAS_HELP")!, "..", "..", "src", "Addons", "Automata", "help", "images");
        Directory.CreateDirectory(folder);

        void Shot(string name, ToySpec spec, double turnedDeg)
        {
            // No data folder: the pictures show the settings given here, not the last ones used.
            var win = new AutomataWindow(doc, machine, lib);
            typeof(AutomataWindow).GetMethod("ShowToy", Any)!.Invoke(win, [spec]);
            var cut = F<ComboBox>(win, "_cutWith");
            if (cut.ItemCount > 1) cut.SelectedIndex = 1;
            win.Show(owner);
            settle(400);
            var t = (Task)typeof(AutomataWindow).GetMethod("BuildNow", Any)!.Invoke(win, null)!;
            for (int i = 0; i < 400 && !t.IsCompleted; i++) settle(50);
            var view = F<MotionView>(win, "_view");
            view.Playing = false;
            view.Input = turnedDeg * Math.PI / 180;
            settle(800);
            win.CaptureRenderedFrame()?.Save(Path.Combine(folder, name + ".png"));
            Console.WriteLine($"automata: {name}.png");
            win.Close();
            settle(200);
        }

        Shot("window-automata-figures", new FigureBoxSpec(), 100);
        Shot("window-automata-strip", new StripSpec(), 1500);
        Shot("window-automata-comb", new CombSpec(), 6.35 * 360);
    }
}
