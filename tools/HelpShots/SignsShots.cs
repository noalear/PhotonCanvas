using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using PhotonCanvas.Addon.Signs;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;

/// <summary>
/// The sign maker's pictures (in its own help/images): the house name sign with the gallery, two
/// lines of words, a raised house number, an arched sign's shape and border, and a sign put in the design.
/// </summary>
static class SignsShots
{
    const BindingFlags Any = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;

    static T F<T>(object o, string name) => (T)o.GetType().GetField(name, Any)!.GetValue(o)!;

    public static void Run(Window owner, Document doc, MachineProfile machine, Action<int> settle)
    {
        var folder = Path.Combine(Environment.GetEnvironmentVariable("PHOTONCANVAS_HELP")!, "..", "..", "src", "Addons", "Signs", "help", "images");
        Directory.CreateDirectory(folder);

        // A font that's on this computer, so the pictures don't show the missing-font warning.
        SignSpec Preset(string name) { var s = SignPresets.All.First(p => p.Name == name).Spec; return s with { Font = SignFonts.Resolve(s.Font) }; }

        SignsWindow Open(SignSpec spec, int tab, int look)
        {
            // No data folder: the pictures show the settings given here, not the last ones used.
            var win = new SignsWindow(doc, machine, null);
            win.Show(owner);
            settle(200);
            typeof(SignsWindow).GetMethod("Load", Any)!.Invoke(win, [spec]);
            var gallery = F<ListBox>(win, "_gallery");
            typeof(SignsWindow).GetField("_loading", Any)!.SetValue(win, true);
            gallery.SelectedIndex = SignPresets.All.ToList().FindIndex(p => p.Name == spec.Name);
            typeof(SignsWindow).GetField("_loading", Any)!.SetValue(win, false);
            F<ComboBox>(win, "_look").SelectedIndex = look;
            win.GetVisualDescendants().OfType<TabControl>().First().SelectedIndex = tab;
            settle(300);
            // Timers don't run here: the preview is remade and drawn now.
            typeof(SignsWindow).GetMethod("Rebuild", Any)!.Invoke(win, null);
            var view = F<SignView>(win, "_view");
            view.Fit();
            var t = (Task)typeof(SignView).GetMethod("Draw", Any)!.Invoke(view, null)!;
            for (int i = 0; i < 600 && !t.IsCompleted; i++) settle(50);
            settle(1500);
            return win;
        }

        void Save(SignsWindow win, string name)
        {
            settle(800);
            win.CaptureRenderedFrame()?.Save(Path.Combine(folder, name + ".png"));
            Console.WriteLine($"signs: {name}.png");
            win.Close();
            settle(200);
        }

        Save(Open(Preset("House name"), 0, 0), "window-signs");
        Save(Open(Preset("Arched house sign"), 1, 0), "window-signs-words");
        Save(Open(Preset("House number"), 2, 0), "window-signs-lettering");
        Save(Open(Preset("Memorial plaque") with { Shape = SignShape.Arched, ArchRiseMm = 40, HeightMm = 140 }, 3, 1), "window-signs-shape");

        // Put in the design: the workshop sign on its own depth-slices layer, cut lines round it and its holes.
        doc.Reset(DocState.New(), null);
        var plan = new SignPlan(Preset("Workshop"));
        var (state, ids, _) = SignOutput.Add(doc.State, plan, SignOutput.DepthMap(plan), machine);
        doc.Commit("Sign", state, ids);
        settle(1200);
        owner.CaptureRenderedFrame()?.Save(Path.Combine(folder, "signs-in-design.png"));
        Console.WriteLine("signs: signs-in-design.png");
    }
}
