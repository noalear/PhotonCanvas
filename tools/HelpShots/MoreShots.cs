using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using PhotonCanvas.App;
using PhotonCanvas.Addon.Slicer;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Import;
using PhotonCanvas.Doc.Materials;
using Bitmap = Avalonia.Media.Imaging.Bitmap;

/// <summary>
/// The guide's pictures that need something in them first: 3D models in the slicing window, a
/// heat-ordered sheet, and windows with lists (undo steps, earlier versions, templates, jobs).
/// </summary>
static class MoreShots
{
    const BindingFlags Any = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;

    static T F<T>(object o, string name) => (T)o.GetType().GetField(name, Any)!.GetValue(o)!;

    /// <summary>An add-on's pictures go in its own help folder (src/Addons/<folder>/help/images).</summary>
    static void AddonShot(Bitmap? bmp, string addonFolder, string name) =>
        bmp?.Save(Path.Combine(Environment.GetEnvironmentVariable("PHOTONCANVAS_HELP")!, "..", "..", "src", "Addons", addonFolder, "help", "images", name + ".png"));

    public static void Run(MainWindow w, Document doc, CanvasView canvas, LayersPanel layers, Control editor, string data,
        Action<Bitmap?, string, string> save, Func<Control, Bitmap?> shot, Action<int> settle, Action reset, DocState sample, DocsSim.Ids ids)
    {
        var settings = F<AppSettings>(w, "_settings");
        var lib = F<MaterialLibrary>(w, "_library");
        void Click(Control b) => b.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        void Slice(StackSliceWindow sw)
        {
            settle(100);
            var t = (Task)typeof(StackSliceWindow).GetMethod("Reslice", Any)!.Invoke(sw, null)!;
            for (int i = 0; i < 600 && !t.IsCompleted; i++) settle(50);
            settle(600);
        }

        // --- 3D models, made here: a chess pawn, a vase and an egg, in a models folder for the browser.
        var models = @"C:\3D models";
        bool ownFolder = !Directory.Exists(models);
        try { Directory.CreateDirectory(models); }
        catch (Exception) { models = Path.Combine(data, "models"); ownFolder = false; Directory.CreateDirectory(models); }
        var pawn = Revolve(Pawn());
        var vase = Revolve(Vase());
        var egg = Revolve(Egg());
        WriteStl(Path.Combine(models, "pawn.stl"), pawn);
        WriteStl(Path.Combine(models, "vase.stl"), vase);
        WriteStl(Path.Combine(models, "egg.stl"), egg);
        settings.ModelFolder = models;
        reset();
        var slicer = new StackSliceWindow(doc, canvas.Machine, settings, lib);
        slicer.Show(w);
        settle(500);
        slicer.Load(pawn, "pawn.stl");
        Slice(slicer);
        save(slicer.CaptureRenderedFrame(), "window-3d-slices-pawn", "Stacked slices: a chess pawn, solid view");
        void Summary(string name, string what)
        {
            if (F<TextBlock>(slicer, "_summary").FindAncestorOfType<ScrollViewer>() is not { } side) return;
            side.ScrollToEnd();
            settle(500);
            save(slicer.CaptureRenderedFrame(), name, what);
            side.ScrollToHome();
            settle(300);
        }
        Summary("window-3d-slices-summary", "Stacked slices: the foot of the settings, with the summary");
        // The booklet's sides to look from, each a picture of the model from that side (drawn in the
        // background): the block animal, which looks different from each side, unlike the pawn.
        slicer.Load([.. Block(0, 0, 0, 6, 6, 12), .. Block(44, 0, 0, 50, 6, 12), .. Block(0, 14, 0, 6, 20, 12), .. Block(44, 14, 0, 50, 20, 12),
            .. Block(0, 0, 12, 50, 20, 27), .. Block(40, 5, 27, 60, 15, 39), .. Block(52, 5, 21, 60, 15, 27)], "animal.stl");
        Slice(slicer);
        settle(2000);
        if (F<Avalonia.Controls.Primitives.UniformGrid>(slicer, "_bookletView") is { } picks)
        {
            picks.BringIntoView();
            settle(500);
            save(slicer.CaptureRenderedFrame(), "window-3d-slices-booklet-view", "Stacked slices: Booklet seen from, a block animal from each side");
        }
        slicer.Load(pawn, "pawn.stl");
        Slice(slicer);
        F<CheckBox>(slicer, "_shape").IsChecked = true;
        Slice(slicer);
        Summary("window-3d-slices-shaping-summary", "Stacked slices with Shape the edges ticked: the summary's edge shaping line");
        F<CheckBox>(slicer, "_shape").IsChecked = false;
        Slice(slicer);
        // Pins cut from a dowel: the settings and the picture of the cuts.
        F<CheckBox>(slicer, "_cutPins").IsChecked = true;
        settle(400);
        F<TextBlock>(slicer, "_pinNote").BringIntoView();
        settle(600);
        save(slicer.CaptureRenderedFrame(), "window-3d-slices-dowel-pins", "Stacked slices with Cut the pins from a dowel too ticked: the dowel's settings and the cuts");
        F<CheckBox>(slicer, "_cutPins").IsChecked = false;
        F<TextBlock>(slicer, "_summary").FindAncestorOfType<ScrollViewer>()?.ScrollToHome();
        settle(300);
        F<ComboBox>(slicer, "_look").SelectedIndex = 1;
        F<Slider>(slicer, "_from").Value = 6;
        F<Slider>(slicer, "_to").Value = 14;
        settle(2500);
        save(slicer.CaptureRenderedFrame(), "window-3d-slices-outlines", "Stacked slices, Outlines view, slices 6 to 14 shown");
        F<ComboBox>(slicer, "_look").SelectedIndex = 0;
        F<Slider>(slicer, "_from").Value = F<Slider>(slicer, "_from").Minimum;
        F<Slider>(slicer, "_to").Value = F<Slider>(slicer, "_to").Maximum;
        slicer.Load(egg, "egg.stl");
        F<ComboBox>(slicer, "_method").SelectedIndex = 1;
        Slice(slicer);
        save(slicer.CaptureRenderedFrame(), "window-3d-slices-egg-crate", "Egg crate: an egg as a slotted grid of sheets");
        slicer.Load(vase, "vase.stl");
        F<ComboBox>(slicer, "_method").SelectedIndex = 2;
        Slice(slicer);
        save(slicer.CaptureRenderedFrame(), "window-3d-slices-radial", "Radial: a vase as fins on rings");
        // The pawn made: the work area after.
        slicer.Load(pawn, "pawn.stl");
        F<ComboBox>(slicer, "_method").SelectedIndex = 0;
        Slice(slicer);
        Click(F<Avalonia.Controls.Button>(slicer, "_make"));
        settle(3000);
        slicer.Close();
        settle(300);
        canvas.FitRect(doc.State.Bounds(doc.State.Shapes.Select(x => x.Id).ToHashSet()).Inflate(25));
        settle(800);
        save(w.CaptureRenderedFrame(), "workspace-after-slices", "The work area after Make the slices: the parts numbered, on the three Slices layers");
        // The assembly booklet of a four-legged animal: its parts list, two steps with several pieces each, and its chin built off to the side.
        var animal = PhotonCanvas.Doc.Geometry.StackSlicer.Slice([.. Block(0, 0, 0, 6, 6, 12), .. Block(44, 0, 0, 50, 6, 12), .. Block(0, 14, 0, 6, 20, 12), .. Block(44, 14, 0, 50, 20, 12),
            .. Block(0, 0, 12, 50, 20, 27), .. Block(40, 5, 27, 60, 15, 39), .. Block(52, 5, 21, 60, 15, 27)], new PhotonCanvas.Doc.Geometry.SliceOptions { ThicknessMm = 3, HeightMm = 39, PinMm = 2 });
        var booklet = new PhotonCanvas.Doc.Geometry.BookletOptions { Title = "Animal", PinMm = 2 };
        save(new Bitmap(new MemoryStream(PhotonCanvas.Doc.Geometry.AssemblyBooklet.RenderPng(animal, booklet, 1, 1.2f))), "booklet-parts", "Assembly booklet: the parts list, every part to the same scale");
        save(new Bitmap(new MemoryStream(PhotonCanvas.Doc.Geometry.AssemblyBooklet.RenderPng(animal, booklet, 2, 1.2f))), "booklet-steps", "Assembly booklet: steps 1 and 2, the legs in four pieces each");
        save(new Bitmap(new MemoryStream(PhotonCanvas.Doc.Geometry.AssemblyBooklet.RenderPng(animal, booklet, 6, 1.2f))), "booklet-side-build", "Assembly booklet: steps 9 and 10, the chin built off to the side joining under the head");
        // The model browser, with its pictures drawn.
        var browser = new ModelBrowserWindow(settings);
        browser.Show(w);
        settle(6000);
        save(browser.CaptureRenderedFrame(), "window-model-browser", "Open a 3D model: the models folder as pictures");
        browser.Close();
        settle(200);
        if (ownFolder) try { Directory.Delete(models, true); } catch (Exception) { }
        // The pawn as a relief (Picture → 3D model), seen from the front.
        var relief = new PhotonCanvas.Addon.Relief.StlWindow();
        relief.Show(w);
        settle(300);
        typeof(PhotonCanvas.Addon.Relief.StlWindow).GetField("_tris", Any)!.SetValue(relief, pawn);
        typeof(PhotonCanvas.Addon.Relief.StlWindow).GetField("_name", Any)!.SetValue(relief, "pawn");
        F<ComboBox>(relief, "_view").SelectedIndex = 1;
        settle(3000);
        AddonShot(relief.CaptureRenderedFrame(), "Relief", "window-picture-3d-model");
        relief.Close();

        // Depth outlines, on a made-up depthmap: domed pebbles piled on each other.
        {
            const int pw = 1200, ph = 600;
            var z = new double[pw * ph];
            var rnd = new Random(7);
            for (int k = 0; k < 26; k++)
            {
                double cx = rnd.Next(pw), cy = rnd.Next(ph), rx = 90 + rnd.Next(110), ry = 60 + rnd.Next(80), top = 0.35 + 0.6 * k / 26;
                for (int y = Math.Max(0, (int)(cy - ry)); y < Math.Min(ph, (int)(cy + ry)); y++)
                    for (int x = Math.Max(0, (int)(cx - rx)); x < Math.Min(pw, (int)(cx + rx)); x++)
                    {
                        double d = Math.Pow((x - cx) / rx, 2) + Math.Pow((y - cy) / ry, 2);
                        if (d < 1) z[y * pw + x] = Math.Max(z[y * pw + x], top - 0.25 * d);
                    }
            }
            var rgba = new byte[pw * ph * 4];
            for (int i = 0; i < z.Length; i++)
            {
                byte v = (byte)Math.Clamp(z[i] * 255, 0, 255);
                rgba[i * 4] = rgba[i * 4 + 1] = rgba[i * 4 + 2] = v;
                rgba[i * 4 + 3] = 255;
            }
            var depth = new ImageShape { Name = "pebbles depthmap", LayerId = doc.State.Layers[0].Id, Source = ImageSource.FromEncoded(new PhotonCanvas.Core.Imaging.RasterImage(pw, ph, rgba).EncodePng()), SizeMm = new Vec2(120, 60) };
            var outlines = new PhotonCanvas.Addon.DepthOutlines.DepthOutlinesWindow(doc, depth);
            outlines.Show(w);
            F<Slider>(outlines, "_amount").Value = 45;
            settle(3000);
            AddonShot(outlines.CaptureRenderedFrame(), "DepthOutlines", "window-picture-depth-outlines");
            outlines.Close();
        }

        // --- Spread the heat: a thin aluminium mask, a plate with rows of holes.
        reset();
        var hs = DocState.New();
        var heat = hs.Layers[0] with { Name = "Cut 0.3 mm aluminium", Laser = LaserSettings.Red(90, 300, 200, 65).WithPasses(4), Order = PathOrder.Heat };
        hs = hs with { Layers = [heat] };
        var parts = new List<Shape> { new PathShape { Name = "Plate", LayerId = heat.Id, Geometry = PathGeometry.RoundRect(30, 40, 160, 110, 6, 6) } };
        for (int r = 0; r < 5; r++)
            for (int c = 0; c < 8; c++)
                parts.Add(new PathShape { Name = $"Hole {r * 8 + c + 1}", LayerId = heat.Id, Geometry = PathGeometry.Ellipse(45 + c * 18.5, 55 + r * 20, 5, 5) });
        hs = Ops.AddShapes(hs, parts);
        doc.Reset(hs, null);
        canvas.FitField();
        layers.Activate(heat.Id);
        settle(1000);
        save(shot(editor), "layer-settings-heat", "Layer settings of a cut layer in the Spread the heat order");
        var job = F<JobBar>(w, "_job");
        var planRow = F<Control>(job, "_planRow");
        // The job bar rebuilds on a timer after a change (not run here): rebuilt straight away instead.
        Task Rebuild() => (Task)typeof(JobBar).GetMethod("Rebuild", Any)!.Invoke(job, null)!;
        var built = Rebuild();
        for (int i = 0; i < 600 && !built.IsCompleted; i++) settle(50);
        // The finer model takes long enough to see the planning bar.
        doc.Reset(Ops.UpdateLayers(doc.State, new HashSet<Guid> { heat.Id }, l => l with { HeatFine = true }), null);
        layers.Activate(heat.Id);
        var fine = Rebuild();
        for (int i = 0; i < 600 && !planRow.IsVisible; i++) settle(50);
        settle(1500);
        if (planRow.IsVisible) save(shot(job), "job-bar-heat-planning", "The job bar while the heat order is worked out");
        else Console.WriteLine("no planning bar seen");
        for (int i = 0; i < 1200 && !fine.IsCompleted; i++) settle(50);
        doc.Reset(hs, null);
        layers.Activate(heat.Id);
        job.ShowOrder = true;
        settle(8000);
        save(w.CaptureRenderedFrame(), "processing-order-heat", "The processing order of a heat-ordered plate: the gradient and its key");
        job.ShowOrder = false;
        settle(300);

        // --- Undo history with some steps, one of them undone.
        reset();
        doc.Commit("Move", Ops.Move(doc.State, new HashSet<Guid> { ids.Outline }, 5, 0), [ids.Outline]);
        doc.Commit("Turn", Ops.SetRotation(doc.State, new HashSet<Guid> { ids.Badge }, 15), [ids.Badge]);
        doc.Commit("Layer settings", Ops.UpdateLayers(doc.State, new HashSet<Guid> { ids.Cut }, l => l with { Laser = l.Laser.WithPasses(2) }));
        doc.Commit("Delete", Ops.Delete(doc.State, new HashSet<Guid> { ids.Ring }));
        doc.Undo();
        settle(300);
        var history = new HistoryWindow(doc);
        history.Show(w);
        settle(800);
        save(history.CaptureRenderedFrame(), "window-history", "Undo history with four steps, the last one undone");
        history.Close();

        // --- Earlier versions of a saved design.
        var design = Path.Combine(data, "Keyring tag" + DocFile.Extension);
        for (int k = 3; k >= 1; k--)
        {
            DocFile.Save(sample, design);
            File.SetLastWriteTime(design, DateTime.Now.AddDays(-k).AddHours(-k * 3));
            VersionsWindow.Keep(design);
        }
        DocFile.Save(sample, design);
        var versions = new VersionsWindow(design);
        versions.Show(w);
        settle(1200);
        if (versions.GetVisualDescendants().OfType<ListBox>().FirstOrDefault() is { ItemCount: > 0 } vl) { vl.SelectedIndex = 0; settle(800); }
        save(versions.CaptureRenderedFrame(), "window-earlier-versions", "Earlier versions of a saved design");
        versions.Close();

        // --- Templates.
        var templates = Path.Combine(AppIdentity.DataDir, "templates");
        Directory.CreateDirectory(templates);
        DocFile.Save(sample, Path.Combine(templates, "Keyring tag" + DocFile.Extension));
        DocFile.Save(hs, Path.Combine(templates, "Aluminium mask" + DocFile.Extension));
        var tw = new TemplatesWindow(templates);
        tw.Show(w);
        settle(1200);
        if (tw.GetVisualDescendants().OfType<ListBox>().FirstOrDefault() is { ItemCount: > 0 } tl) { tl.SelectedIndex = 0; settle(800); }
        save(tw.CaptureRenderedFrame(), "window-new-from-template", "New from template, with two templates");
        tw.Close();

        // --- Job history.
        JobHistory.Add(new JobRecord(DateTime.Now.AddDays(-2).AddHours(-3), "Keyring tag", 260, 271, true, 5, 150.6, 100));
        JobHistory.Add(new JobRecord(DateTime.Now.AddDays(-1).AddHours(-1), "Aluminium mask", 940, 1012, true, 1, 160, 110));
        JobHistory.Add(new JobRecord(DateTime.Now.AddHours(-2), "Coaster set", 1380, 620, false, 3, 200, 200));
        var jobs = new JobHistoryWindow();
        jobs.Show(w);
        settle(800);
        save(jobs.CaptureRenderedFrame(), "window-job-history", "Job history with three jobs, one stopped early");
        jobs.Close();

        // --- Offset of an open line (its Line ends row shows).
        reset();
        var line = new PathShape { Name = "Line", LayerId = ids.Score, Geometry = PathGeometry.FromSvgPathData("M 60 170 C 90 140 130 200 170 165") };
        doc.Commit("Line", Ops.AddShapes(doc.State, [line]), [line.Id]);
        settle(300);
        var offset = new OffsetWindow(doc, settings);
        offset.Show(w);
        settle(1000);
        save(offset.CaptureRenderedFrame(), "window-offset-open-line", "Offset with an open line selected: its Line ends row");
        offset.Close();

        // --- Warp, in the modes that need more settings.
        reset();
        doc.Select([ids.Outline]);
        settle(200);
        var warp = new PhotonCanvas.Addon.Warp.WarpWindow(doc);
        warp.Show(w);
        settle(600);
        foreach (var (index, name) in new[] { (4, "round-object"), (6, "four-corners") })
        {
            F<ComboBox>(warp, "_mode").SelectedIndex = index;
            settle(900);
            AddonShot(warp.CaptureRenderedFrame(), "Warp", $"window-warp-{name}");
        }
        warp.Close();

        // --- Leather and fabric (add-on, run by its command): a card wallet's outside and its pocket.
        {
            reset();
            var ls = DocState.New();
            var outside = new PathShape { Name = "Outside", LayerId = ls.Layers[0].Id, Geometry = PathGeometry.RoundRect(15, 30, 85, 62, 8, 8) };
            var pocket = new PathShape { Name = "Pocket", LayerId = ls.Layers[0].Id, Geometry = PathGeometry.RoundRect(110, 30, 85, 40, 8, 8) };
            doc.Reset(Ops.AddShapes(ls, [outside, pocket]), null);
            canvas.FitRect(new Rect2(0, 10, 230, 200));
            doc.Select([outside.Id, pocket.Id]);
            settle(300);
            var addonCommands = (IEnumerable<HotCommand>)typeof(MainWindow).GetField("_addonCommands", Any)!.GetValue(w)!;
            addonCommands.FirstOrDefault(c => c.Id == "leatherfabric.open")?.Run();
            settle(1500);
            if (w.OwnedWindows.FirstOrDefault(x => x.GetType().Name == "SewnPatternWindow") is { } sewn)
            {
                var mirror = sewn.GetVisualDescendants().OfType<CheckBox>().First(c => (c.Content as string)?.StartsWith("Also make the mirror") == true);
                mirror.IsChecked = true;
                settle(900);
                AddonShot(sewn.CaptureRenderedFrame(), "LeatherFabric", "window-leather-fabric");
                sewn.Position = new PixelPoint(w.Position.X + 2000, w.Position.Y);
                canvas.FitRect(new Rect2(0, 10, 230, 200));
                settle(900);
                AddonShot(w.CaptureRenderedFrame(), "LeatherFabric", "workspace-leather-pieces");
                sewn.Close();
                settle(300);
            }
        }

        // --- Split onto pieces: a 400 × 150 mm design over four 200 × 75 mm boards laid two by two.
        {
            reset();
            var ps = DocState.New();
            var cutL = ps.Layers[0] with { Name = "Cut" };
            var picL = Layer.Default(1, LayerMode.Image) with { Name = "Picture", Dpi = 254 };
            var scoreL = Layer.Default(2, LayerMode.Score) with { Name = "Score" };
            ps = ps with { Layers = [scoreL, picL, cutL] };
            var landscape = (ImageShape)sample.Shapes.First(x => x.Id == ids.Picture);
            var boards = new List<Shape>();
            for (int r = 0; r < 2; r++)
                for (int c = 0; c < 2; c++)
                    boards.Add(new PathShape { Name = $"Board {r * 2 + c + 1}", LayerId = cutL.Id, Geometry = PathGeometry.Rect(10 + c * 200, 30 + r * 75, 200, 75) });
            var picture = new ImageShape { Name = "Landscape", LayerId = picL.Id, Source = landscape.Source, SizeMm = new Vec2(380, 130), Transform = Affine.Translate(20, 40) };
            var sun = new PathShape { Name = "Sun", LayerId = cutL.Id, Geometry = PathGeometry.Ellipse(210, 105, 28, 28) };
            var title = TextShape.Create("ALONG THE RIDGE", new TextStyle("Arial", 16, Bold: true), scoreL.Id, Affine.Translate(130, 150));
            ps = Ops.AddShapes(ps, [.. boards, picture, sun, title]);
            ps = MaterialOutline.Mark(ps, boards.Select(x => x.Id).ToHashSet());
            doc.Reset(ps, null);
            canvas.FitRect(new Rect2(-10, 0, 430, 200));
            doc.Select(doc.State.Shapes.Select(x => x.Id));
            settle(800);
            save(w.CaptureRenderedFrame(), "workspace-pieces-layout", "Four boards as material outlines, the design over them, all selected");
            // Split onto pieces is an add-on: run its command.
            var commands = (IEnumerable<HotCommand>)typeof(MainWindow).GetField("_addonCommands", Any)!.GetValue(w)!;
            commands.FirstOrDefault(c => c.Id == "area.split")?.Run();
            settle(3000);
            // The job bar rebuilds on a timer after a change (not run here): rebuilt straight away.
            var bar = F<JobBar>(w, "_job");
            void Rebuilt()
            {
                var t = (Task)typeof(JobBar).GetMethod("Rebuild", Any)!.Invoke(bar, null)!;
                for (int i = 0; i < 200 && !t.IsCompleted; i++) settle(50);
                settle(300);
            }
            Rebuilt();
            var lineUp = w.OwnedWindows.FirstOrDefault(x => x.GetType().Name == "LineUpWindow");
            if (lineUp is not null) { lineUp.Position = new PixelPoint(w.Position.X + 2000, w.Position.Y); settle(300); }
            canvas.FitRect(new Rect2(-200, -10, 240, 240));
            settle(800);
            doc.Commit("Show page", Pages.SwitchTo(doc.State, 2), []);
            canvas.FitRect(new Rect2(-200, -10, 240, 240));
            Rebuilt();
            settle(800);
            save(w.CaptureRenderedFrame(), "workspace-pieces-2", "Piece 2 of 4: moved onto the bed, piece 1 greyed off its left edge");
            if (lineUp is not null)
            {
                save(lineUp.CaptureRenderedFrame(), "window-camera-line-up-pieces", "Line up the pieces, on piece 2, not connected");
                lineUp.Close();
            }
            settle(200);
        }

        // --- Object snaps: a line from a box's corner, about to touch a circle (the Tangent marker).
        {
            reset();
            var os = DocState.New();
            var cutL = os.Layers[0];
            os = Ops.AddShapes(os,
            [
                new PathShape { Name = "Box", LayerId = cutL.Id, Geometry = PathGeometry.Rect(40, 40, 60, 35) },
                new PathShape { Name = "Circle", LayerId = cutL.Id, Geometry = PathGeometry.Ellipse(150, 120, 25, 25) },
            ]);
            doc.Reset(os, null);
            canvas.FitRect(new Rect2(25, 25, 190, 160));
            settle(400);
            canvas.Tool = Tool.Line;
            var toScreen = typeof(CanvasView).GetMethod("ToScreen", Any, [typeof(Vec2)])!;
            Point At(Vec2 mm) => canvas.TranslatePoint((Point)toScreen.Invoke(canvas, [mm])!, w)!.Value;
            var start = new Vec2(100, 75);
            var press = At(start + new Vec2(0.4, 0.3));
            w.MouseMove(press, Avalonia.Input.RawInputModifiers.None);
            settle(100);
            w.MouseDown(press, Avalonia.Input.MouseButton.Left);
            settle(40);
            w.MouseUp(press, Avalonia.Input.MouseButton.Left);
            settle(150);
            var c = new Vec2(150, 120);
            double dist = start.Dist(c), a0 = Math.Atan2(start.Y - c.Y, start.X - c.X), b0 = Math.Acos(25 / dist);
            var touch = c + new Vec2(Math.Cos(a0 - b0), Math.Sin(a0 - b0)) * 25;
            w.MouseMove(At(touch + new Vec2(0.4, 0.3)), Avalonia.Input.RawInputModifiers.None);
            settle(300);
            save(shot(canvas), "canvas-object-snaps", "Drawing a line from a box's corner: the Tangent marker on the circle, the length and angle by the pointer");
            canvas.Tool = Tool.Select;
            settle(100);
            // A selected line: its grips instead of the box.
            var gripLine = new PathShape { Name = "Line", LayerId = cutL.Id, Geometry = PathGeometry.Line(new Vec2(100, 75), new Vec2(140, 140)) };
            doc.Commit("Line", Ops.AddShapes(doc.State, [gripLine]), [gripLine.Id]);
            settle(400);
            save(shot(canvas), "canvas-line-grips", "A selected line: a grip at each end and in the middle, and its length and angle");
        }

        // --- The Selection panel's Picture and Text cards (below the size and place, so drawn on their own).
        reset();
        var props = F<Control>(w, "_props");
        doc.Select([ids.Picture]);
        settle(600);
        save(shot(F<Border>(props, "_picCard")), "panel-selection-picture-card", "The Selection panel's Picture card: tone, trace, depthmap, remove background");
        doc.Select([ids.Text]);
        settle(600);
        save(shot(F<Border>(props, "_textCard")), "panel-selection-text-card", "The Selection panel's Text card: the text, font, size and style");
        doc.Select([]);

        // --- Layers: two edited together, a sub-layer, and a picture on a layer that can't burn it.
        reset();
        typeof(LayersPanel).GetMethod("SetSelection", Any)!.Invoke(layers, [new[] { ids.Cut, ids.Fill }, ids.Cut]);
        settle(800);
        save(shot(editor), "layer-settings-several", "Layer settings while two layers are edited together: values that differ show as mixed");
        reset();
        layers.AddSubLayer(doc.State.Layers.First(l => l.Id == ids.Cut));
        doc.Commit("Move to layer", Ops.MoveToLayer(doc.State, new HashSet<Guid> { ids.Picture }, ids.Score), [ids.Picture]);
        doc.Select([]);
        settle(800);
        save(shot(layers), "panel-layers-sublayer", "The Layers panel with a sub-layer under the cut layer, and a picture on a Score layer (warned)");

        // --- Several pages, and Edit points on a heart.
        reset();
        doc.Commit("Add page", Pages.Add(doc.State, "Lid"));
        doc.Commit("Add page", Pages.Add(doc.State, "Base"));
        doc.Commit("Page", Pages.SwitchTo(doc.State, 0));
        settle(500);
        if (w.FindControl<ContentControl>("PagesHost") is { } pagesHost) save(shot(pagesHost), "pages-bar", "The pages bar with three pages");
        reset();
        var heart = new PathShape { Name = "Heart", LayerId = ids.Score, Geometry = PathGeometry.FromSvgPathData("M 110 210 C 80 190 75 165 95 160 C 105 157 110 165 110 170 C 110 165 115 157 125 160 C 145 165 140 190 110 210 Z") };
        doc.Commit("Heart", Ops.AddShapes(doc.State, [heart]), [heart.Id]);
        canvas.Tool = Tool.Nodes;
        canvas.FitRect(doc.State.Bounds(new HashSet<Guid> { heart.Id }).Inflate(10));
        settle(800);
        save(shot(canvas), "canvas-edit-points", "Edit points on a heart: its corner and curve points");
        canvas.Tool = Tool.Select;
        reset();
    }

    // A box from corner to corner (for the booklet's animal).
    static List<StlDepth.Triangle> Block(double x0, double y0, double z0, double x1, double y1, double z1)
    {
        StlDepth.Vec3 P(double x, double y, double z) => new(x, y, z);
        IEnumerable<StlDepth.Triangle> Quad(StlDepth.Vec3 a, StlDepth.Vec3 b, StlDepth.Vec3 c, StlDepth.Vec3 d) => [new(a, b, c), new(a, c, d)];
        return
        [
            .. Quad(P(x0, y0, z0), P(x1, y0, z0), P(x1, y1, z0), P(x0, y1, z0)), .. Quad(P(x0, y0, z1), P(x0, y1, z1), P(x1, y1, z1), P(x1, y0, z1)),
            .. Quad(P(x0, y0, z0), P(x0, y0, z1), P(x1, y0, z1), P(x1, y0, z0)), .. Quad(P(x0, y1, z0), P(x1, y1, z0), P(x1, y1, z1), P(x0, y1, z1)),
            .. Quad(P(x0, y0, z0), P(x0, y1, z0), P(x0, y1, z1), P(x0, y0, z1)), .. Quad(P(x1, y0, z0), P(x1, y0, z1), P(x1, y1, z1), P(x1, y1, z0)),
        ];
    }

    // A profile turned round the Z axis: (radius, height) pairs from the bottom up.
    static List<StlDepth.Triangle> Revolve(List<(double R, double Z)> profile, int n = 72)
    {
        var tris = new List<StlDepth.Triangle>();
        StlDepth.Vec3 P(double r, double z, int j) => new(r * Math.Cos(2 * Math.PI * j / n), r * Math.Sin(2 * Math.PI * j / n), z);
        for (int k = 0; k + 1 < profile.Count; k++)
        {
            var (r0, z0) = profile[k];
            var (r1, z1) = profile[k + 1];
            for (int j = 0; j < n; j++)
            {
                var a = P(r0, z0, j); var b = P(r0, z0, j + 1); var c = P(r1, z1, j + 1); var d = P(r1, z1, j);
                if (r0 > 1e-9) tris.Add(new(a, b, c));
                if (r1 > 1e-9) tris.Add(new(a, c, d));
            }
        }
        var (rb, zb) = profile[0];
        var (rt, zt) = profile[^1];
        for (int j = 0; j < n; j++)
        {
            if (rb > 1e-9) tris.Add(new(new(0, 0, zb), P(rb, zb, j + 1), P(rb, zb, j)));
            if (rt > 1e-9) tris.Add(new(new(0, 0, zt), P(rt, zt, j), P(rt, zt, j + 1)));
        }
        return tris;
    }

    static List<(double, double)> Pawn()
    {
        var p = new List<(double, double)> { (12, 0), (12, 2.5), (10.5, 3.5), (9.5, 5), (10, 6), (8.5, 7), (7, 9), (5.5, 13), (4.6, 18), (4.4, 21), (7.2, 22), (7.2, 23.5), (4.6, 24.5) };
        for (double a = -45; a <= 90.01; a += 7.5)
        {
            double t = a * Math.PI / 180;
            p.Add((Math.Abs(a - 90) < 1e-6 ? 0 : 6.5 * Math.Cos(t), 30.5 + 6.5 * Math.Sin(t)));
        }
        return p;
    }

    static List<(double, double)> Vase()
    {
        var p = new List<(double, double)>();
        for (int i = 0; i <= 40; i++)
        {
            double z = 60.0 * i / 40;
            p.Add((9 + 9 * Math.Sin(Math.PI * z / 75) + 2.2 * Math.Sin(z / 4.5), z));
        }
        return p;
    }

    static List<(double, double)> Egg()
    {
        var p = new List<(double, double)>();
        for (int i = 0; i <= 36; i++)
        {
            double t = Math.PI * i / 36;
            p.Add((i == 0 || i == 36 ? 0 : 20 * Math.Sin(t) * (1 - 0.18 * Math.Cos(t)), 26 * (1 - Math.Cos(t))));
        }
        return p;
    }

    static void WriteStl(string path, List<StlDepth.Triangle> tris)
    {
        using var f = new BinaryWriter(File.Create(path));
        f.Write(new byte[80]);
        f.Write((uint)tris.Count);
        foreach (var t in tris)
        {
            f.Write(0f); f.Write(0f); f.Write(0f);
            foreach (var v in new[] { t.A, t.B, t.C }) { f.Write((float)v.X); f.Write((float)v.Y); f.Write((float)v.Z); }
            f.Write((ushort)0);
        }
    }
}
