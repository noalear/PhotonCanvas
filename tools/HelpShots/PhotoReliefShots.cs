using System.Reflection;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.VisualTree;
using PhotonCanvas.Addon.PhotoRelief;
using PhotonCanvas.Core;
using PhotonCanvas.Core.Imaging;
using PhotonCanvas.Doc;

/// <summary>
/// The Photo to relief pictures (in its own help/images): a portrait on an oval plaque, the Carving
/// tab with a coin, clicking the background on the photo, and the relief put in the design. The photo
/// is Grace Hopper's official U.S. Navy portrait (public domain), in tools/HelpShots/samples.
/// </summary>
static class PhotoReliefShots
{
    const BindingFlags Any = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;

    static T F<T>(object o, string name) => (T)o.GetType().GetField(name, Any)!.GetValue(o)!;

    public static void Run(Window owner, Document doc, MachineProfile machine, Action<int> settle)
    {
        var repo = Path.Combine(Environment.GetEnvironmentVariable("PHOTONCANVAS_HELP")!, "..", "..");
        var folder = Path.Combine(repo, "src", "Addons", "PhotoRelief", "help", "images");
        Directory.CreateDirectory(folder);
        var bytes = File.ReadAllBytes(Path.Combine(repo, "tools", "HelpShots", "samples", "grace-hopper.jpg"));
        var img = RasterImage.Decode(bytes);
        double k = 90.0 / img.Width;
        var start = DocState.New();
        var picture = new ImageShape
        {
            Name = "Grace Hopper", LayerId = start.Layers[0].Id, Source = ImageSource.FromEncoded(bytes),
            SizeMm = new Vec2(img.Width * k, img.Height * k), Transform = Affine.Translate(machine.FieldW / 2 - 45, 20),
        };
        doc.Reset(Ops.AddShapes(start, [picture]), null);
        // The light parts of the flag behind her, clicked as background.
        PhotoPoint[] flag = [new(0.92, 0.08), new(0.88, 0.42), new(0.1, 0.3)];

        PhotoReliefWindow Open(ReliefSpec spec, int tab, int look)
        {
            var win = new PhotoReliefWindow(doc, machine, null, null, picture);
            win.Show(owner);
            settle(200);
            typeof(PhotoReliefWindow).GetMethod("Load", Any)!.Invoke(win, [spec]);
            F<ComboBox>(win, "_look").SelectedIndex = look;
            win.GetVisualDescendants().OfType<TabControl>().First().SelectedIndex = tab;
            settle(300);
            Redraw(win);
            return win;
        }

        // Timers don't run here: the preview is remade now.
        void Redraw(PhotoReliefWindow win)
        {
            var t = win.Rebuild();
            for (int i = 0; i < 600 && !t.IsCompleted; i++) settle(50);
            settle(800);
        }

        void Save(Window win, string name)
        {
            settle(400);
            win.CaptureRenderedFrame()?.Save(Path.Combine(folder, name + ".png"));
            Console.WriteLine($"photorelief: {name}.png");
        }

        ReliefSpec Style(string name, ReliefSpec s) => ReliefStyles.Find(name)!.Apply(s) with { Style = name };

        // A portrait on an oval plaque, as carved wood, on the Photo tab.
        var portrait = Style("Portrait", new ReliefSpec { WidthMm = 100, Picks = flag });
        var w = Open(portrait, 0, 0);
        Save(w, "window-photo-relief");
        w.Close();
        settle(200);

        // Clicking the background: the photo itself, Click the background switched on.
        w = Open(portrait, 0, 3);
        F<ToggleButton>(w, "_pick").IsChecked = true;
        F<ComboBox>(w, "_look").SelectedIndex = 3;
        Redraw(w);
        Save(w, "window-photo-relief-pick");
        w.Close();
        settle(200);

        // A coin of her face, zoomed in, as metal, on the Carving tab.
        var coin = Style("Coin or medal", new ReliefSpec { WidthMm = 60, Picks = flag, Zoom = 1.7, PanY = -0.55 });
        w = Open(coin, 2, 1);
        Save(w, "window-photo-relief-coin");
        w.Close();
        settle(200);

        // The depth map, on the Shape tab.
        w = Open(portrait, 1, 2);
        Save(w, "window-photo-relief-depth");
        w.Close();
        settle(200);

        // Put in the design: the relief where the photo was, on its own depth-slices layer.
        var photo = new ReliefPhoto(picture.Source.Pixels, picture.Name);
        var r = ReliefOutput.Full(portrait with { PixelsPerMm = 6 }, photo);
        var b = picture.Bounds;
        var state = Ops.Delete(doc.State, new HashSet<Guid> { picture.Id });
        var (next, ids, _) = ReliefOutput.Add(state, portrait, photo, PhotoDepth.DepthMap(r), r.WidthMm, r.HeightMm, machine,
            new Vec2((b.MinX + b.MaxX) / 2, (b.MinY + b.MaxY) / 2));
        doc.Commit("Photo relief", next, ids);
        settle(1200);
        owner.CaptureRenderedFrame()?.Save(Path.Combine(folder, "photo-relief-in-design.png"));
        Console.WriteLine("photorelief: photo-relief-in-design.png");
    }
}
