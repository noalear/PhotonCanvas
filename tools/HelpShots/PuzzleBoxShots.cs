using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using PhotonCanvas.Addon.PuzzleBox;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Materials;

/// <summary>The puzzle box maker's pictures (in its own help/images): a puzzle box with a secret drawer shut, part way open, seen inside, and its pieces.</summary>
static class PuzzleBoxShots
{
    const BindingFlags Any = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;

    static T F<T>(object o, string name) => (T)o.GetType().GetField(name, Any)!.GetValue(o)!;

    public static void Run(Window owner, Document doc, MachineProfile machine, MaterialLibrary lib, Action<int> settle)
    {
        var folder = Path.Combine(Environment.GetEnvironmentVariable("PHOTONCANVAS_HELP")!, "..", "..", "src", "Addons", "PuzzleBox", "help", "images");
        // PHOTONCANVAS_PUZZLEBOX_ALL=<folder>: every kind, shut, open, inside and in pieces, into that folder (for checking by eye).
        var all = Environment.GetEnvironmentVariable("PHOTONCANVAS_PUZZLEBOX_ALL");
        Directory.CreateDirectory(all ?? folder);
        // A scratch data folder holding the box to show, so the pictures don't depend on the last one used.
        var data = Path.Combine(Path.GetTempPath(), "photoncanvas-helpshots-puzzlebox");

        void Shot(string into, string name, BoxSpec spec, int view, Func<BoxDesign, int>? step = null, bool inside = false)
        {
            Directory.CreateDirectory(data);
            File.WriteAllText(Path.Combine(data, "last.json"), spec.ToJson());
            var win = new PuzzleBoxWindow(doc, machine, lib, data, null);
            win.Show(owner);
            settle(300);
            F<TabControl>(win, "_views").SelectedIndex = view;
            settle(100);
            var t = (Task)typeof(PuzzleBoxWindow).GetMethod("Rebuild", Any)!.Invoke(win, null)!;
            for (int i = 0; i < 400 && !t.IsCompleted; i++) settle(50);
            settle(300);
            var kit = F<BoxKit?>(win, "_kit");
            if (step is not null && kit?.Design is { Solution: not null } d) F<Slider>(win, "_step").Value = step(d);
            if (inside) F<CheckBox>(win, "_inside").IsChecked = true;
            settle(500);
            win.CaptureRenderedFrame()?.Save(Path.Combine(into, name + ".png"));
            Console.WriteLine($"puzzlebox: {name}.png");
            win.Close();
            settle(200);
        }

        if (all is not null)
        {
            for (int i = 0; i < Presets.Kinds.Count; i++)
            {
                var spec = Presets.Kinds[i].Spec with { Message = "Well done" };
                Shot(all, $"{i}-shut", spec, 0);
                Shot(all, $"{i}-inside", spec, 0, inside: true);
                Shot(all, $"{i}-open", spec, 0, d => d.Solution!.Moves.Count);
                Shot(all, $"{i}-first", spec, 0, d => 1);
                Shot(all, $"{i}-pieces", spec, 1);
            }
            return;
        }
        var box = Presets.Kinds[2].Spec with { Message = "Well done!" };
        Shot(folder, "window-puzzlebox", box, 0);
        Shot(folder, "window-puzzlebox-opening", box, 0, d => d.Solution!.BoxOpenAt);
        Shot(folder, "window-puzzlebox-inside", box, 0, inside: true);
        Shot(folder, "window-puzzlebox-drawer", box, 0, d => d.Solution!.Moves.Count);
        Shot(folder, "window-puzzlebox-pieces", box, 1);
    }
}
