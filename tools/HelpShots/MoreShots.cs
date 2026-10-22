using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using PhotonCanvas.App;
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
        F<CheckBox>(slicer, "_shape").IsChecked = true;
        Slice(slicer);
        save(slicer.CaptureRenderedFrame(), "window-3d-slices-shaping", "Stacked slices with Shape the edges ticked");
        Summary("window-3d-slices-shaping-summary", "Stacked slices with Shape the edges ticked: the summary's edge shaping line");
        F<CheckBox>(slicer, "_shape").IsChecked = false;
        Slice(slicer);
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
        // The model browser, with its pictures drawn.
        var browser = new ModelBrowserWindow(settings);
        browser.Show(w);
        settle(6000);
        save(browser.CaptureRenderedFrame(), "window-model-browser", "Open a 3D model: the models folder as pictures");
        browser.Close();
        settle(200);
        if (ownFolder) try { Directory.Delete(models, true); } catch (Exception) { }
        // The pawn as a relief (Picture → 3D model), seen from the front.
        var relief = new StlWindow();
        relief.Show(w);
        settle(300);
        typeof(StlWindow).GetField("_tris", Any)!.SetValue(relief, pawn);
        typeof(StlWindow).GetField("_name", Any)!.SetValue(relief, "pawn");
        F<ComboBox>(relief, "_view").SelectedIndex = 1;
        settle(3000);
        save(relief.CaptureRenderedFrame(), "window-picture-3d-model", "3D model to relief: a chess pawn seen from the front, as a depth map");
        relief.Close();

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
        var warp = new WarpWindow(doc);
        warp.Show(w);
        settle(600);
        foreach (var (index, name) in new[] { (4, "round-object"), (5, "cone"), (6, "four-corners") })
        {
            F<ComboBox>(warp, "_mode").SelectedIndex = index;
            settle(900);
            save(warp.CaptureRenderedFrame(), $"window-warp-{name}", $"Warp, {name.Replace('-', ' ')} mode");
        }
        warp.Close();

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
