using Avalonia.Controls;
using Avalonia.Headless;
using PhotonCanvas.App;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;

/// <summary>
/// Surfaces (Machine → Surfaces): two 10 mm pens side by side with a mark on each, the window (with where the
/// marks land, seen from above) and the canvas with the pens' rectangles.
/// </summary>
static class SurfacesShots
{
    public static void Run(Window owner, Document doc, MachinePanel machine, Action<int> settle)
    {
        var folder = Path.Combine(Environment.GetEnvironmentVariable("PHOTONCANVAS_HELP")!, "images");
        var pen = new MarkSurface { Name = "Pen 1", Centre = new Vec2(110, 100), AxisDeg = 0, LengthMm = 90, DiameterMm = 10, TopMm = 10 };
        var pen2 = pen with { Id = Guid.NewGuid(), Name = "Pen 2", Centre = new Vec2(110, 114) };
        var s = DocState.New();
        var layer = s.Layers[0] with { Name = "Mark", Mode = LayerMode.Score };
        s = s with { Layers = [layer], Surfaces = new MarkSurfaces { Items = [pen, pen2] } };
        // A badge on each pen: a ring and a band round it, and a line along it.
        var marks = new List<PathGeometry>();
        foreach (var y in new[] { 100.0, 114.0 })
        {
            marks.Add(PathGeometry.Ellipse(85, y, 4.2, 4.2));
            marks.Add(PathGeometry.Rect(95, y - 4.5, 30, 9));
            marks.Add(PathGeometry.Line(new Vec2(130, y - 3), new Vec2(150, y - 3)));
            marks.Add(PathGeometry.Line(new Vec2(130, y + 3), new Vec2(150, y + 3)));
        }
        s = Ops.AddShapes(s, [.. marks.Select(g => (Shape)new PathShape { LayerId = layer.Id, Geometry = g })]);
        doc.Reset(s, null);
        settle(600);

        var win = new SurfacesWindow(doc, machine);
        win.Show(owner);
        settle(1200);
        win.CaptureRenderedFrame()?.Save(Path.Combine(folder, "window-machine-surfaces.png"));
        Console.WriteLine("surfaces: window-machine-surfaces.png");
        win.Close();
        settle(300);
        var canvas = owner.FindControl<CanvasView>("Canvas")!;
        canvas.FitRect(new Rect2(50, 80, 170, 130));
        settle(500);
        owner.CaptureRenderedFrame()?.Save(Path.Combine(folder, "surfaces-on-canvas.png"));
        Console.WriteLine("surfaces: surfaces-on-canvas.png");
    }
}
