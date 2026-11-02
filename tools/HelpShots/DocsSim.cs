using System.Reflection;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Bitmap = Avalonia.Media.Imaging.Bitmap;
using Avalonia.VisualTree;
using PhotonCanvas.App;
using PhotonCanvas.Core;
using PhotonCanvas.Core.Imaging;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Materials;

/// <summary>
/// Screenshots for the help guide: the program with a sample design, every ribbon tab, the panels,
/// the layer settings of each kind of layer, and every window the ribbon's buttons open (each of its
/// tabs). Saved to docs/help/images, with an index of what each is.
/// </summary>
static class DocsSim
{
    const BindingFlags Any = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;
    static readonly StringBuilder Index = new();

    static string Slug(string s)
    {
        var sb = new StringBuilder();
        foreach (char c in s.ToLowerInvariant())
            if (char.IsLetterOrDigit(c)) sb.Append(c);
            else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
        return sb.ToString().Trim('-');
    }

    public static void Run(string imagesDir, Action pump, string? only = null)
    {
        void Settle(int ms) { for (int i = 0; i < ms / 25; i++) { pump(); Thread.Sleep(25); } }
        // Fresh settings and library: the pictures show the defaults.
        var data = Path.Combine(Path.GetTempPath(), "photoncanvas-docs-data");
        if (Directory.Exists(data)) Directory.Delete(data, true);
        Environment.SetEnvironmentVariable(AppIdentity.DataDirVariable, data);
        Directory.CreateDirectory(imagesDir);
        void Save(Bitmap? bmp, string name, string what)
        {
            if (bmp is null) { Console.WriteLine($"no picture: {name}"); return; }
            bmp.Save(Path.Combine(ImageHome.For(imagesDir, name + ".png"), name + ".png"));
            Index.AppendLine($"{name}.png\t{bmp.PixelSize.Width}x{bmp.PixelSize.Height}\t{what}");
        }
        Bitmap? Shot(Control c)
        {
            var size = c.Bounds.Size;
            if (size.Width < 2 || size.Height < 2) return null;
            // Drawn on the window's own background (a control alone has none).
            var bg = (Avalonia.Media.IBrush)Application.Current!.FindResource("Bg1")!;
            Avalonia.Media.IBrush? was = null;
            switch (c)
            {
                case TemplatedControl tc: was = tc.Background; tc.Background ??= bg; break;
                case Panel p: was = p.Background; p.Background ??= bg; break;
                case Border b: was = b.Background; b.Background ??= bg; break;
            }
            pump();
            var rtb = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(size.Width), (int)Math.Ceiling(size.Height)), new Vector(96, 96));
            rtb.Render(c);
            switch (c)
            {
                case TemplatedControl tc: tc.Background = was; break;
                case Panel p: p.Background = was; break;
                case Border b: b.Background = was; break;
            }
            return rtb;
        }

        var w = new MainWindow { Width = 1500, Height = 920, ConnectOnOpen = false };
        T F<T>(string name) => (T)typeof(MainWindow).GetField(name, Any)!.GetValue(w)!;
        var doc = F<Document>("_doc");
        var lib = F<MaterialLibrary>("_library");
        SeedLibrary(lib);
        w.Show();
        Settle(400);
        var canvas = w.FindControl<CanvasView>("Canvas")!;
        var ribbon = F<Ribbon>("_ribbon");
        var layers = F<LayersPanel>("_layers");
        var editor = F<LayerEditor>("_layerEditor");
        var sample = Sample(out var ids);
        void Reset()
        {
            foreach (var o in w.OwnedWindows.ToList()) o.Close();
            doc.Reset(sample, null);
            canvas.Tool = Tool.Select;
            canvas.FitField();
            Settle(200);
        }
        Reset();
        layers.Activate(ids.Cut);
        Settle(600);

        if (only is null or "main")
        {
            doc.Select([ids.Outline]);
            Settle(600);
            Save(w.CaptureRenderedFrame(), "workspace", "The whole window with a sample design, the outline selected");
            foreach (var tab in ribbon.TabNames.ToList())
            {
                ribbon.Select(tab);
                Settle(300);
                Save(Shot(w.FindControl<ContentControl>("RibbonHost")!), $"ribbon-{Slug(tab)}", $"The {tab} tab of the ribbon");
            }
            ribbon.Select("Home");
            Save(Shot(layers), "panel-layers", "The Layers panel");
            Save(Shot(F<Control>("_props")), "panel-selection", "The Selection panel with a shape selected");
            Save(Shot(w.FindControl<ContentControl>("JobHost")!), "job-bar", "The job bar");
            Save(Shot(F<Control>("_hud")), "machine-panel", "The machine panel at the right end of the ribbon (not connected)");
            // The layer settings of each kind of layer, with More settings open.
            var more = typeof(LayerEditor).GetField("_more", Any)!.GetValue(editor) as Expander;
            foreach (var (id, name) in new[] { (ids.Cut, "cut"), (ids.Score, "score"), (ids.Fill, "fill"), (ids.Image, "picture"), (ids.Mopa, "mopa") })
            {
                layers.Activate(id);
                if (more is not null) more.IsExpanded = false;
                Settle(400);
                Save(Shot(editor), $"layer-settings-{name}", $"Layer settings of a {name} layer");
                if (more is not null) more.IsExpanded = true;
                Settle(400);
                Save(Shot(editor), $"layer-settings-{name}-more", $"Layer settings of a {name} layer, More settings open");
            }
            if (more is not null) more.IsExpanded = false;
            layers.Activate(ids.Cut);
            // The tool options bar for the star and for Pick piece.
            foreach (var (tool, name) in new[] { (Tool.Star, "star"), (Tool.Polygon, "polygon"), (Tool.Wand, "pick-piece") })
            {
                canvas.Tool = tool;
                Settle(400);
                if (w.FindControl<Border>("ToolOptions") is { IsVisible: true } bar) Save(Shot(bar), $"tool-options-{name}", $"The tool options bar above the work area, {name} tool");
            }
            canvas.Tool = Tool.Select;
            // The layers after a CMYK split of the picture.
            doc.Select([ids.Picture]);
            Settle(200);
            typeof(MainWindow).GetMethod("SplitCmyk", Any)!.Invoke(w, [doc.State.Shapes.OfType<ImageShape>().First(x => x.Id == ids.Picture)]);
            Settle(600);
            Save(Shot(layers), "panel-layers-cmyk", "The Layers panel after CMYK split: one layer per ink");
            Reset();
            layers.Activate(ids.Cut);
            Settle(300);
            // The processing order on the work area.
            var job = F<JobBar>("_job");
            job.ShowOrder = true;
            Settle(2500);
            Save(w.CaptureRenderedFrame(), "processing-order", "The work area with Show order on: the order gradient and its key");
            job.ShowOrder = false;
        }

        if (only is null or "windows")
        {
            // Every window a ribbon button opens.
            var skip = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Home/Import", "Home/Picture", "Home/By colour", "Home/Import as…", "Home/Select by…", "Home/More…", "Home/Paste",
                "Picture/Import picture", "Picture/Depthmap", "Picture/3D model", "Picture/Depth outlines",
                "Camera/Calibrate", "Camera/Focus on selection",
                "Camera/Thickness", "Camera/Cut round prints",
                "Laser/Split onto pieces", "Camera/Line up pieces", // set up in MoreShots
                "Laser/Frame", "Laser/Ship It", "Laser/Export .xf", "Laser/Export G-code", "Laser/Time-lapse", "Laser/Quick on lid close", "Laser/Console",
                "View/Reset panels",
            };
            foreach (var (key, control) in ribbon.Commands.ToList())
            {
                if (skip.Contains(key) || control is ToggleButton) continue;
                Reset();
                string tab = key[..key.IndexOf('/')];
                // What the command works on: a picture, a few shapes, or the outline.
                if (tab == "Picture" || key.EndsWith("Colour on metal") || key.EndsWith("Diffraction")) doc.Select([ids.Picture]);
                else if (tab == "Arrange" || key.Contains("Weld") || key.Contains("Subtract") || key.Contains("Intersect") || key.Contains("Exclude") || key.EndsWith("Cut shapes") || key.EndsWith("Mask with shape")) doc.Select([ids.Outline, ids.Badge, ids.Ring]);
                else if (key.EndsWith("Text on path")) doc.Select([ids.Text, ids.Ring]);
                else doc.Select([ids.Outline]);
                Settle(150);
                var before = w.OwnedWindows.ToList();
                try { control.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent)); }
                catch (Exception e) { Console.WriteLine($"{key}: {e.GetType().Name} {e.Message}"); continue; }
                Settle(900);
                foreach (var nw in w.OwnedWindows.Except(before).ToList())
                {
                    string name = $"window-{Slug(key.Replace('/', ' '))}";
                    CaptureWindow(nw, name, $"{key}: {nw.Title} ({nw.GetType().Name})", Save, Settle);
                    nw.Close();
                    Settle(150);
                }
            }
            // Windows from the menus and settings.
            foreach (var (method, args, name) in new (string, object[], string)[]
            {
                ("ShowSettings", ["Machine"], "settings"),
                ("ShowHotkeys", [], "keyboard-shortcuts"),
                ("ShowGettingStarted", [], "getting-started"),
                ("ShowFonts", [], "fonts"),
                ("NewFromTemplate", [], "new-from-template"),
                ("ShowLibrary", [], "material-library"),
                ("ShowGridSettings", [], "grid-settings"),
                ("ShowWorkMode", [], "work-mode"),
                ("ShowSections", [], "sections"),
            })
            {
                Reset();
                doc.Select([ids.Outline]);
                var mi = typeof(MainWindow).GetMethod(method, Any);
                if (mi is null) { Console.WriteLine($"no method {method}"); continue; }
                var before = w.OwnedWindows.ToList();
                try { _ = mi.Invoke(w, args); }
                catch (Exception e) { Console.WriteLine($"{method}: {e.InnerException?.Message ?? e.Message}"); continue; }
                Settle(900);
                foreach (var nw in w.OwnedWindows.Except(before).ToList())
                {
                    // The library with a material open, so its recipes show.
                    if (name == "material-library" && nw.GetVisualDescendants().OfType<ListBox>().FirstOrDefault() is { ItemCount: > 0 } list)
                    {
                        list.SelectedIndex = 0;
                        Settle(500);
                    }
                    if (nw is SettingsWindow sw) CaptureSettings(sw, Save, Settle);
                    else CaptureWindow(nw, $"window-{name}", $"{method}: {nw.Title} ({nw.GetType().Name})", Save, Settle);
                    nw.Close();
                    Settle(150);
                }
            }
        }
        if (only is null or "windows" or "extra")
        {
            // Windows opened from menus and panels rather than the ribbon.
            void Capture(string name, string what, Action open, Guid? select = null)
            {
                Reset();
                if (select is { } id) doc.Select([id]);
                Settle(150);
                var before = w.OwnedWindows.ToList();
                try { open(); } catch (Exception e) { Console.WriteLine($"{name}: {e.InnerException?.Message ?? e.Message}"); return; }
                Settle(900);
                foreach (var nw in w.OwnedWindows.Except(before).ToList())
                {
                    CaptureWindow(nw, name, $"{what}: {nw.Title} ({nw.GetType().Name})", Save, Settle);
                    nw.Close();
                    Settle(150);
                }
            }
            Task Call(string method, params object[] args) => (typeof(MainWindow).GetMethod(method, Any)!.Invoke(w, args) as Task) ?? Task.CompletedTask;
            Capture("window-circles-by-size", "Home → Select by… → Circles by size…", () => new CirclesWindow(doc).Show(w));
            Capture("window-console", "Laser → Console", () => _ = Call("ShowConsole"));
            Capture("window-save-to-library", "Layer settings → Save to library…", () =>
            {
                var save = editor.GetVisualDescendants().OfType<Avalonia.Controls.Button>().FirstOrDefault(b => (b.Content as string ?? "").StartsWith("Save to library"));
                save?.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
            });
        }
        if (only is null or "more") MoreShots.Run(w, doc, canvas, layers, editor, data, Save, Shot, Settle, Reset, sample, ids);
        if (only is null or "more" or "mechanism") { Reset(); MechanismShots.Run(w, doc, canvas.Machine, lib, Settle); }
        if (only is null or "more" or "walls") { Reset(); WallsShots.Run(w, doc, canvas.Machine, lib, Settle); }
        if (only is null or "more" or "furniture") { Reset(); FurnitureShots.Run(w, doc, canvas.Machine, lib, Settle); }
        if (only is null or "more" or "ruins") { Reset(); RuinsShots.Run(w, doc, canvas.Machine, lib, Settle); }
        if (only is null or "more" or "scatter") { Reset(); ScatterShots.Run(w, doc, canvas.Machine, lib, Settle); }
        if (only is null or "more" or "stairs") { Reset(); StairsShots.Run(w, doc, canvas.Machine, lib, Settle); }
        if (only is null or "more" or "buildings") { Reset(); BuildingsShots.Run(w, doc, canvas.Machine, lib, Settle); }
        if (only is null or "more" or "gamekit") { Reset(); GameKitShots.Run(w, doc, canvas.Machine, lib, Settle); }
        if (only is null or "more" or "floortiles") { Reset(); FloorTilesShots.Run(w, doc, canvas.Machine, lib, Settle); }
        if (only is null or "more" or "roofs") { Reset(); RoofsShots.Run(w, doc, canvas.Machine, lib, Settle); }
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "images-index.txt"), Index.ToString());
        Console.WriteLine(Index.ToString());
    }

    static void CaptureWindow(Window nw, string name, string what, Action<Bitmap?, string, string> save, Action<int> settle)
    {
        settle(300);
        var tabs = nw.GetVisualDescendants().OfType<TabControl>().FirstOrDefault();
        if (tabs is null || tabs.ItemCount < 2)
        {
            save(nw.CaptureRenderedFrame(), name, what);
            return;
        }
        for (int i = 0; i < tabs.ItemCount; i++)
        {
            tabs.SelectedIndex = i;
            settle(400);
            string header = (tabs.Items[i] as TabItem)?.Header?.ToString() ?? $"tab {i + 1}";
            save(nw.CaptureRenderedFrame(), $"{name}--{Slug(header)}", $"{what}, tab \"{header}\"");
        }
    }

    static void CaptureSettings(SettingsWindow sw, Action<Bitmap?, string, string> save, Action<int> settle)
    {
        settle(300);
        var nav = sw.GetVisualDescendants().OfType<ListBox>().FirstOrDefault();
        if (nav is null) { save(sw.CaptureRenderedFrame(), "window-settings", "Settings"); return; }
        for (int i = 0; i < nav.ItemCount; i++)
        {
            nav.SelectedIndex = i;
            settle(500);
            var item = nav.Items[i];
            string label = item is ListBoxItem li ? li.Content?.ToString() ?? $"page {i}" : item?.ToString() ?? $"page {i}";
            if (item is ListBoxItem { Content: Control c }) label = c.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault()?.Text ?? label;
            save(sw.CaptureRenderedFrame(), $"window-settings--{Slug(label)}", $"Settings, page \"{label}\"");
        }
    }

    internal record Ids(Guid Cut, Guid Score, Guid Fill, Guid Image, Guid Mopa, Guid Outline, Guid Badge, Guid Ring, Guid Text, Guid Picture);

    /// <summary>A sample design: a cut outline with holes, a scored ring and text, an engraved badge, a picture, a MOPA mark.</summary>
    internal static DocState Sample(out Ids ids)
    {
        var s = DocState.New();
        var score = Layer.Default(0, LayerMode.Score) with { Name = "Score lines", Laser = LaserSettings.Blue(25, 120) };
        var fill = Layer.Default(1, LayerMode.Fill) with { Name = "Engrave badge", Laser = LaserSettings.Blue(40, 300), LinesPerCm = 120 };
        var image = Layer.Default(2, LayerMode.Image) with { Name = "Photo", Laser = LaserSettings.Blue(30, 200), Dpi = 300 };
        var mopa = Layer.Default(3, LayerMode.Score) with { Name = "MOPA mark", Laser = LaserSettings.Red(60, 800, 45, 132) };
        var cut = s.Layers[0] with { Name = "Cut 3 mm basswood", Laser = LaserSettings.Blue(80, 12).WithPasses(3) };
        s = s with { Layers = [score, fill, image, mopa, cut] };
        var outline = new PathShape { Name = "Tag outline", LayerId = cut.Id, Geometry = PathGeometry.RoundRect(40, 50, 140, 100, 10, 10) };
        var hole = new PathShape { Name = "Hanging hole", LayerId = cut.Id, Geometry = PathGeometry.Ellipse(55, 65, 4, 4) };
        var ring = new PathShape { Name = "Ring", LayerId = score.Id, Geometry = PathGeometry.Ellipse(90, 100, 30, 30) };
        var badge = new PathShape { Name = "Star", LayerId = fill.Id, Geometry = PathGeometry.FromSvgPathData("M90 78 L96 93 L112 93 L99 103 L104 119 L90 109 L76 119 L81 103 L68 93 L84 93 Z") };
        var text = TextShape.Create("PhotonCanvas", new TextStyle("Arial", 9, Bold: true), score.Id, Affine.Translate(128, 135));
        var mark = new PathShape { Name = "Serial line", LayerId = mopa.Id, Geometry = PathGeometry.Rect(130, 60, 40, 6) };
        // A picture: a soft landscape drawn here (no outside image needed).
        int pw = 360, ph = 240;
        var px = new byte[pw * ph * 4];
        for (int y = 0; y < ph; y++)
            for (int x = 0; x < pw; x++)
            {
                double sky = y / (double)ph, hill = 150 + 30 * Math.Sin(x / 40.0) + 12 * Math.Sin(x / 13.0), sun = Math.Sqrt((x - 260) * (x - 260) + (y - 60) * (y - 60));
                double v = y > hill ? 70 + 40 * Math.Sin(x / 9.0 + y / 7.0) * 0.3 + (y - hill) * 0.4 : sun < 28 ? 245 : 120 + 110 * sky;
                byte g = (byte)Math.Clamp(v, 0, 255);
                int o = (y * pw + x) * 4;
                px[o] = (byte)Math.Clamp(g * 0.85, 0, 255); px[o + 1] = (byte)Math.Clamp(g * 0.95, 0, 255); px[o + 2] = g; px[o + 3] = 255;
            }
        var picture = new ImageShape { Name = "Landscape", LayerId = image.Id, Source = ImageSource.FromEncoded(new RasterImage(pw, ph, px).ToPng()), SizeMm = new Vec2(60, 40), Transform = Affine.Translate(120, 80) };
        s = Ops.AddShapes(s, [outline, hole, ring, badge, text, mark, picture]);
        ids = new Ids(cut.Id, score.Id, fill.Id, image.Id, mopa.Id, outline.Id, badge.Id, ring.Id, text.Id, picture.Id);
        return s;
    }

    static void SeedLibrary(MaterialLibrary lib)
    {
        if (lib.Materials.Count > 0) return;
        lib.Upsert(new Material
        {
            Name = "Basswood", ThicknessMm = 3, Category = "Wood",
            Recipes =
            [
                new Recipe { Purpose = RecipePurpose.Cut, Title = "Clean cut (example)", Rating = 4, Settings = new ProcessSettings { Mode = LayerMode.Cut, Laser = LaserSettings.Blue(80, 12).WithPasses(3) } },
                new Recipe { Purpose = RecipePurpose.Engrave, Title = "Dark engrave (example)", Rating = 3, Settings = new ProcessSettings { Mode = LayerMode.Fill, Laser = LaserSettings.Blue(40, 300), LinesPerCm = 120 } },
            ],
        });
        lib.Upsert(new Material
        {
            Name = "Stainless steel", ThicknessMm = 0.5, Category = "Metal",
            Recipes = [new Recipe { Purpose = RecipePurpose.Mark, Title = "Black mark (example)", Rating = 3, Settings = new ProcessSettings { Mode = LayerMode.Fill, Laser = LaserSettings.Red(60, 800, 200, 40), LinesPerCm = 200 } }],
        });
        lib.Save();
    }
}

/// <summary>
/// Where a picture is saved: the help/images folder of the add-on whose pages show it (src/Addons/*/help),
/// else the program's own images folder.
/// </summary>
static class ImageHome
{
    static Dictionary<string, string>? _map;

    public static string For(string programImages, string file)
    {
        _map ??= Build(programImages);
        return _map.TryGetValue(file, out var dir) ? dir : programImages;
    }

    static Dictionary<string, string> Build(string programImages)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var addons = Path.GetFullPath(Path.Combine(programImages, "..", "..", "..", "src", "Addons"));
        if (!Directory.Exists(addons)) return map;
        foreach (var help in Directory.GetDirectories(addons).Select(d => Path.Combine(d, "help")).Where(Directory.Exists))
            foreach (var md in Directory.GetFiles(help, "*.md", SearchOption.AllDirectories))
                foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(md), @"\]\(images/([^)]+)\)"))
                    map[m.Groups[1].Value] = Path.Combine(help, "images");
        return map;
    }
}
