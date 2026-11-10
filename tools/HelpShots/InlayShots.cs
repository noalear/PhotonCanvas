using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using PhotonCanvas.Addon.Inlay;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Geometry;

/// <summary>
/// The inlay and marquetry add-on's pictures (in its own help/images): a little landscape of four
/// woods cut into a background veneer, and one wood's pieces packed on their page.
/// </summary>
static class InlayShots
{
    const BindingFlags Any = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;

    static T F<T>(object o, string name) => (T)o.GetType().GetField(name, Any)!.GetValue(o)!;

    /// <summary>Hills, a sun and a tree: a layer for each wood, nearer things on top.</summary>
    public static (DocState State, List<Guid> Ids) Landscape()
    {
        var s = DocState.New();
        var ids = new List<Guid>();
        Layer Wood(string name, uint colour)
        {
            var l = Layer.Default(s.Layers.Count, LayerMode.Cut) with { Name = name, Color = colour };
            s = Ops.AddLayer(s, l);
            return l;
        }
        void Add(Layer l, PathGeometry g)
        {
            var p = new PathShape { Name = l.Name, LayerId = l.Id, Geometry = g };
            s = Ops.AddShapes(s, [p]);
            ids.Add(p.Id);
        }
        var far = Wood("Olive ash", 0x9DB36B);
        var near = Wood("Walnut", 0x6B4A2E);
        var sun = Wood("Padauk", 0xE07A3C);
        var tree = Wood("Wenge", 0x3B2F2A);
        var cloud = Wood("Holly", 0xF4F1EA);
        double x0 = 40, y0 = 40, w = 140, h = 90;
        var frame = PathGeometry.Rect(x0, y0, w, h);
        PathGeometry Clip(PathGeometry g) => ShapeGeometry.Boolean(g, frame, BoolOp.Intersect);
        Add(sun, PathGeometry.Ellipse(x0 + 102, y0 + 26, 13, 13));
        Add(far, Clip(ShapeGeometry.Union([PathGeometry.Ellipse(x0 + 30, y0 + 95, 70, 40), PathGeometry.Ellipse(x0 + 115, y0 + 100, 60, 45)])));
        Add(near, Clip(PathGeometry.Ellipse(x0 + 60, y0 + 120, 110, 42)));
        Add(tree, ShapeGeometry.Union([PathGeometry.Rect(x0 + 32, y0 + 48, 5, 24), PathGeometry.Ellipse(x0 + 34.5, y0 + 40, 14, 15)]));
        // A fence of walnut posts and a rail in front of the hills, and two holly clouds.
        for (int i = 0; i < 5; i++)
            Add(near, PathGeometry.Polyline([new(x0 + 70 + i * 12, y0 + 82), new(x0 + 70 + i * 12, y0 + 64), new(x0 + 72 + i * 12, y0 + 61), new(x0 + 74 + i * 12, y0 + 64), new(x0 + 74 + i * 12, y0 + 82)], true));
        foreach (var (cx, cy) in new[] { (x0 + 62, y0 + 16), (x0 + 128, y0 + 44) })
            Add(cloud, ShapeGeometry.Union([PathGeometry.Ellipse(cx, cy, 11, 5), PathGeometry.Ellipse(cx - 5, cy - 3, 6, 5), PathGeometry.Ellipse(cx + 4, cy - 4, 6, 6)]));
        return (s, ids);
    }

    public static void Run(Window owner, Document doc, MachineProfile machine, Action<int> settle)
    {
        var folder = Path.Combine(Environment.GetEnvironmentVariable("PHOTONCANVAS_HELP")!, "..", "..", "src", "Addons", "Inlay", "help", "images");
        Directory.CreateDirectory(folder);
        var (state, ids) = Landscape();
        doc.Reset(state, null);
        doc.Select(ids);
        settle(300);

        // No data folder: the picture shows the settings given here, not the last ones used.
        var win = new InlayWindow(doc, null, machine);
        win.Show(owner);
        settle(300);
        F<ComboBox>(win, "_into").SelectedIndex = (int)InlayInto.Holes;
        var sheet = F<object>(win, "_sheet");
        F<TextBox>(sheet, "Name").Text = "Sycamore";
        F<NumericUpDown>(win, "_fit").Value = 0.04m;
        F<CheckBox>(win, "_pack").IsChecked = true;
        settle(400);
        F<InlayPreview>(win, "_preview").SeeAll();
        settle(1200);
        win.CaptureRenderedFrame()?.Save(Path.Combine(folder, "window-inlay-marquetry.png"));
        Console.WriteLine("inlay: window-inlay-marquetry.png");

        // Made: the walnut's pieces packed on their page, numbered.
        typeof(InlayWindow).GetMethod("Make", Any)!.Invoke(win, null);
        settle(300);
        var s = doc.State;
        doc.Commit("Show the walnut", Pages.SwitchTo(s, Pages.Names(s).IndexOf(InlayMaker.Prefix + "Walnut")));
        settle(600);
        owner.FindControl<PhotonCanvas.App.CanvasView>("Canvas")?.FitDesign();
        settle(1500);
        owner.CaptureRenderedFrame()?.Save(Path.Combine(folder, "inlay-pages.png"));
        Console.WriteLine("inlay: inlay-pages.png");
    }
}
