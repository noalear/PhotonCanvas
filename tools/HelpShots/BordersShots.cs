using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using PhotonCanvas.Addon.Borders;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;

/// <summary>
/// The border maker's pictures (in its own help/images): a gilt frame on the Design tab, an oval,
/// a band being changed, a cabinet edge as its depth map, a corner close up, and the border put in the design.
/// </summary>
static class BordersShots
{
    const BindingFlags Any = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;

    static T F<T>(object o, string name) => (T)o.GetType().GetField(name, Any)!.GetValue(o)!;

    public static void Run(Window owner, Document doc, MachineProfile machine, Action<int> settle)
    {
        var folder = Path.Combine(Environment.GetEnvironmentVariable("PHOTONCANVAS_HELP")!, "..", "..", "src", "Addons", "Borders", "help", "images");
        Directory.CreateDirectory(folder);

        BordersWindow Open(BorderSpec spec, int tab, int look)
        {
            // No data folder: the pictures show the settings given here, not the last ones used.
            var win = new BordersWindow(doc, machine, null);
            win.Show(owner);
            settle(200);
            typeof(BordersWindow).GetMethod("Load", Any)!.Invoke(win, [spec]);
            var category = F<ComboBox>(win, "_category");
            category.SelectedIndex = -1;
            category.SelectedIndex = Math.Max(0, BorderPresets.Categories.ToList().IndexOf(spec.Design.Category));
            F<ComboBox>(win, "_look").SelectedIndex = look;
            win.GetVisualDescendants().OfType<TabControl>().First().SelectedIndex = tab;
            settle(300);
            Redraw(win);
            return win;
        }

        // Timers don't run here: the preview is remade and drawn now.
        void Redraw(BordersWindow win)
        {
            typeof(BordersWindow).GetMethod("Rebuild", Any)!.Invoke(win, null);
            var view = F<BorderView>(win, "_view");
            view.Fit();
            Draw(view);
        }

        void Draw(BorderView view)
        {
            var t = (Task)typeof(BorderView).GetMethod("Draw", Any)!.Invoke(view, null)!;
            for (int i = 0; i < 600 && !t.IsCompleted; i++) settle(50);
            settle(1500);
        }

        void Save(BordersWindow win, string name)
        {
            settle(600);
            win.CaptureRenderedFrame()?.Save(Path.Combine(folder, name + ".png"));
            Console.WriteLine($"borders: {name}.png");
        }

        BorderDesign Design(string name) => BorderPresets.All.First(d => d.Name == name);

        // A gilt frame, the gallery of frames beside it.
        var gilt = new BorderSpec { Design = Design("Classic gilt"), Layout = BorderLayout.Frame };
        var w = Open(gilt, 0, 1);
        Save(w, "window-borders");
        // The same frame, a corner close up.
        var view = F<BorderView>(w, "_view");
        typeof(BorderView).GetField("_scale", Any)!.SetValue(view, 9.0);
        typeof(BorderView).GetField("_centre", Any)!.SetValue(view, new Point(32, 32));
        typeof(BorderView).GetField("_fitted", Any)!.SetValue(view, false);
        view.InvalidateVisual();
        Draw(view);
        Save(w, "window-borders-corner");
        w.Close();
        settle(200);

        // An oval mirror frame on the Size tab.
        w = Open(new BorderSpec { Design = Design("Acanthus"), Layout = BorderLayout.Oval, FrameWidthMm = 170, FrameHeightMm = 220 }, 1, 0);
        Save(w, "window-borders-oval");
        w.Close();
        settle(200);

        // Changing a band: the Louis XVI frame's laurel bead chosen in the cross-section, corner blocks with rosettes.
        var louis = Design("Louis XVI");
        w = Open(new BorderSpec { Design = louis, Layout = BorderLayout.Frame, FrameWidthMm = 180, FrameHeightMm = 220, Corners = CornerStyle.Blocks }, 2, 0);
        int laurel = Math.Max(0, louis.Bands.ToList().FindIndex(b => b.Ornament != Ornament.None));
        F<ListBox>(w, "_bands").SelectedIndex = laurel;
        Redraw(w);
        Save(w, "window-borders-bands");
        w.Close();
        settle(200);

        // A cabinet edge as a strip, shown as its depth map, on the Output tab.
        w = Open(new BorderSpec { Design = Design("Egg and dart crown"), Layout = BorderLayout.Strip, LengthMm = 200, MitredEnds = true }, 3, 3);
        Save(w, "window-borders-edge");
        w.Close();
        settle(200);

        // Round a rotary part: a double rope spiral up a table leg, seen on the part, on the Size tab.
        BorderDesign Band(string name) => BorderPresets.BandDesigns.First(d => d.Name == name);
        w = Open(new BorderSpec { Design = Band("Rope"), Layout = BorderLayout.Rotary, PartDiameterMm = 40, Wrap = WrapWay.Spiral, WrapCopies = 2, WidthMm = 14, WrapLengthMm = 150 }, 1, 0);
        Save(w, "window-borders-rotary");
        // Three rope rings round a pen, unrolled.
        F<CheckBox>(w, "_onPart").IsChecked = false;
        typeof(BordersWindow).GetMethod("Load", Any)!.Invoke(w, [new BorderSpec { Design = Band("Rope"), Layout = BorderLayout.Rotary, PartDiameterMm = 20, Wrap = WrapWay.Round, WrapCopies = 3, WidthMm = 8, WrapGapMm = 6 }]);
        settle(300);
        Redraw(w);
        Save(w, "window-borders-rotary-flat");
        w.Close();
        settle(200);

        // Put in the design: the frame on its own depth-slices layer.
        doc.Reset(DocState.New(), null);
        var plan = new BorderPlan(gilt);
        var maps = plan.Pieces().Select(p => (p, BorderOutput.DepthMap(plan, p))).ToList();
        var (state, ids, _) = BorderOutput.Add(doc.State, plan, maps, machine);
        doc.Commit("Border", state, ids);
        settle(1200);
        owner.CaptureRenderedFrame()?.Save(Path.Combine(folder, "borders-in-design.png"));
        Console.WriteLine("borders: borders-in-design.png");
    }
}
