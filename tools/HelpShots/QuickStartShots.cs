using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Bitmap = Avalonia.Media.Imaging.Bitmap;
using PhotonCanvas.App;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;

/// <summary>
/// The pictures in the F2 Ultra UV quick-start guide (docs/quick-start-f2-ultra-uv.pdf): the program set to
/// the F2 Ultra UV with a small first job, the welcome window's steps, the settings pages, the calibration
/// and test grid windows. Saved to a folder of their own, not the help guide's images.
/// </summary>
static class QuickStartShots
{
    const BindingFlags Any = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;

    public static void Run(string folder, Action pump)
    {
        void Settle(int ms) { for (int i = 0; i < ms / 25; i++) { pump(); Thread.Sleep(25); } }
        var data = Path.Combine(Path.GetTempPath(), "photoncanvas-quickstart-data");
        if (Directory.Exists(data)) Directory.Delete(data, true);
        Environment.SetEnvironmentVariable(AppIdentity.DataDirVariable, data);
        Directory.CreateDirectory(folder);
        void Save(Bitmap? bmp, string name)
        {
            if (bmp is null) { Console.WriteLine($"no picture: {name}"); return; }
            bmp.Save(Path.Combine(folder, name + ".png"));
            Console.WriteLine($"{name}.png {bmp.PixelSize.Width}x{bmp.PixelSize.Height}");
        }
        Bitmap? Shot(Control c)
        {
            var size = c.Bounds.Size;
            if (size.Width < 2 || size.Height < 2) return null;
            pump();
            var rtb = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(size.Width), (int)Math.Ceiling(size.Height)), new Vector(96, 96));
            rtb.Render(c);
            return rtb;
        }

        var w = new MainWindow { Width = 1500, Height = 920, ConnectOnOpen = false };
        T F<T>(string name) => (T)typeof(MainWindow).GetField(name, Any)!.GetValue(w)!;
        object? Call(string method, params object[] args) => typeof(MainWindow).GetMethod(method, Any)!.Invoke(w, args);
        var settings = F<AppSettings>("_settings");
        var doc = F<Document>("_doc");
        w.Show();
        Settle(400);

        // The F2 Ultra UV, as picked in the welcome window.
        var uv = MachineSetup.ForDeviceCode("GS009-CLASS-4")!;
        settings.ActiveMachine = uv.Id;
        settings.Save();
        Call("ApplyEquipment");
        Settle(300);

        // The welcome window: safety, connection keys and "Your equipment" with the UV picked.
        // Its camera step as it shows before connecting (nothing here reaches a machine).
        var camera = new WelcomeWindow.CameraSetup(() => settings.ActiveSetup(), () => false, () => Task.CompletedTask, _ => Task.CompletedTask, () => false);
        var welcome = new WelcomeWindow(firstRun: true, () => Task.FromResult(false), () => false, settings, camera);
        welcome.Show(w);
        var go = typeof(WelcomeWindow).GetMethod("Go", Any)!;
        foreach (var (page, name) in new[] { (1, "welcome-safety"), (2, "welcome-keys"), (3, "welcome-equipment"), (4, "welcome-camera") })
        {
            go.Invoke(welcome, [page]);
            Settle(500);
            Save(welcome.CaptureRenderedFrame(), name);
        }
        // The same step for the F2 Ultra (blue diode and MOPA), then back to the UV.
        settings.ActiveMachine = MachineSetup.F2Ultra.Id;
        go.Invoke(welcome, [4]);
        Settle(500);
        Save(welcome.CaptureRenderedFrame(), "welcome-camera-f2-ultra");
        settings.ActiveMachine = uv.Id;
        welcome.Close();
        Settle(200);

        // A first job: a 30 mm square outline and a name tag, marked with the UV laser.
        var s = DocState.New();
        var mark = s.Layers[0] with { Name = "First test (UV)", Mode = LayerMode.Score, Laser = LaserSettings.Blue(40, 500) with { FrequencyKhz = 40 } };
        var fill = Layer.Default(1, LayerMode.Fill) with { Name = "Engraved name", Laser = LaserSettings.Blue(30, 1000) with { FrequencyKhz = 40 }, LinesPerCm = 200 };
        s = s with { Layers = [mark, fill] };
        var square = new PathShape { Name = "Test square", LayerId = mark.Id, Geometry = PathGeometry.Rect(85, 85, 30, 30) };
        var text = TextShape.Create("UV", new TextStyle("Arial", 12, Bold: true), fill.Id, Affine.Translate(93, 96));
        s = Ops.AddShapes(s, [square, text]);
        doc.Reset(s, null);
        var canvas = w.FindControl<CanvasView>("Canvas")!;
        canvas.FitField();
        var layers = F<LayersPanel>("_layers");
        layers.Activate(mark.Id);
        doc.Select([square.Id]);
        F<JobBar>("_job").Refresh();
        Settle(1500);
        Save(w.CaptureRenderedFrame(), "workspace-uv");
        Save(Shot(F<Control>("_layerEditor")), "layer-settings-uv");
        Save(Shot(w.FindControl<ContentControl>("JobHost")!), "job-bar");
        Save(Shot(F<Control>("_hud")), "machine-panel");
        var ribbon = F<Ribbon>("_ribbon");
        foreach (var tab in new[] { "Home", "Machine", "Materials & Tests" })
        {
            ribbon.Select(tab);
            Settle(300);
            Save(Shot(w.FindControl<ContentControl>("RibbonHost")!), $"ribbon-{Slug(tab)}");
        }
        ribbon.Select("Home");

        // Settings pages.
        foreach (var page in new[] { "Machine", "Machines", "Equipment", "Updates" })
        {
            var before = w.OwnedWindows.ToList();
            _ = Call("ShowSettings", page);
            Settle(900);
            foreach (var nw in w.OwnedWindows.Except(before).ToList())
            {
                if (nw.GetVisualDescendants().OfType<ListBox>().FirstOrDefault() is { } nav)
                    for (int i = 0; i < nav.ItemCount; i++)
                        if (nav.Items[i] is ListBoxItem { Content: Control c } && c.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault()?.Text == page
                            || nav.Items[i] is ListBoxItem { Content: string t } && t == page)
                        {
                            nav.SelectedIndex = i;
                            Settle(500);
                        }
                Save(nw.CaptureRenderedFrame(), $"settings-{Slug(page)}");
                nw.Close();
                Settle(150);
            }
        }

        // The connection keys window.
        var keys = new KeysWindow();
        keys.Show(w);
        Settle(500);
        Save(keys.CaptureRenderedFrame(), "keys");
        keys.Close();

        // Camera calibration and the test grid, for the UV.
        var cal = new CalibrationWindow(F<MachinePanel>("_machine"), settings, null);
        cal.Show(w);
        Settle(800);
        Save(cal.CaptureRenderedFrame(), "calibration");
        cal.Close();
        var grid = new MaterialTestWindow(doc.State, settings.TextFont, uv.Profile);
        grid.Show(w);
        Settle(600);
        Save(grid.CaptureRenderedFrame(), "test-grid-uv");
        grid.Close();
        w.Close();
    }

    static string Slug(string s)
    {
        var sb = new System.Text.StringBuilder();
        foreach (char c in s.ToLowerInvariant())
            if (char.IsLetterOrDigit(c)) sb.Append(c);
            else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
        return sb.ToString().Trim('-');
    }
}
