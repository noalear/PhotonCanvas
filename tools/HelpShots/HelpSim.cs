using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using PhotonCanvas.App;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Help;
using SkiaSharp;
using Block = PhotonCanvas.Doc.Help.Block;
using Inline = PhotonCanvas.Doc.Help.Inline;

/// <summary>
/// The help guide and the tutorials in the real window: screenshots of the guide, the tutorials list
/// and a tutorial running; then every tutorial walked step by step (ribbon buttons clicked as a user
/// would, so the windows later steps point into are open), reporting steps whose target can't be found.
/// </summary>
static class HelpSim
{
    const BindingFlags Any = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;

    public static void Run(string imagesDir, Action pump, string? only = null)
    {
        _pump = pump;
        var data = Path.Combine(Path.GetTempPath(), "photoncanvas-help-data");
        if (Directory.Exists(data)) Directory.Delete(data, true);
        Environment.SetEnvironmentVariable(AppIdentity.DataDirVariable, data);

        var w = new MainWindow { Width = 1500, Height = 920, ConnectOnOpen = false };
        T F<T>(string name) => (T)typeof(MainWindow).GetField(name, Any)!.GetValue(w)!;
        var doc = F<Document>("_doc");
        w.Show();
        Settle(400);
        var canvas = w.FindControl<CanvasView>("Canvas")!;
        var sample = DocsSim.Sample(out var ids);
        void Reset()
        {
            TutorialRunner.Running?.Stop();
            foreach (var o in w.OwnedWindows.ToList()) o.Close();
            doc.Reset(sample, null);
            canvas.Tool = Tool.Select;
            canvas.FitField();
            Settle(200);
        }
        Reset();

        if (only is null or "shots")
        {
            w.ShowHelp();
            Settle(800);
            var help = w.OwnedWindows.OfType<HelpWindow>().First();
            help.Width = 1240; help.Height = 860;
            Settle(400);
            help.Go("layer-settings.md", "");
            Settle(800);
            help.CaptureRenderedFrame()?.Save(Path.Combine(imagesDir, "help-guide-page.png"));
            var search = (TextBox)typeof(HelpWindow).GetField("_search", Any)!.GetValue(help)!;
            search.Text = "kerf";
            Settle(600);
            help.CaptureRenderedFrame()?.Save(Path.Combine(imagesDir, "help-guide-search.png"));
            help.Close();
            w.ShowTutorials();
            Settle(800);
            var tuts = w.OwnedWindows.OfType<TutorialsWindow>().First();
            tuts.CaptureRenderedFrame()?.Save(Path.Combine(imagesDir, "tutorials-window.png"));
            tuts.Close();
            Settle(200);
            // A tutorial running: the card and a ringed button, put together as the screen shows them.
            if (HelpFiles.Library.Tutorials.FirstOrDefault(t => t.Id == "first-job") is { } first)
            {
                w.StartTutorial(first.Id);
                Settle(500);
                var run = TutorialRunner.Running!;
                while (run.StepIndex < first.Steps.Count - 1 && run.Pointed is null) { run.Next(); Settle(400); }
                Settle(800);
                Compose(w, run.Card, Path.Combine(imagesDir, "tutorial-running.png"));
                run.Stop();
            }
            Reset();
        }

        if (only is null or "walk")
        {
            int bad = 0, steps = 0, pointed = 0, walked = 0;
            var lines = new List<string>();
            var onlyIds = Environment.GetEnvironmentVariable("HELP_WALK_ONLY")?.Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
            foreach (var t in HelpFiles.Library.Tutorials)
            {
                if (onlyIds is not null && !onlyIds.Contains(t.Id)) continue;
                walked++;
                Reset();
                // Most tutorials work on something: the outline (or the picture, for the Picture tab's);
                // two shapes where a step waits for two, the text for the ones about letters.
                bool pictures = (t.Steps.Any(s => s.Target?.StartsWith("ribbon:Picture/") == true) || t.Category == "Pictures") && t.Id != "pic-shapes-to-picture";
                if (t.Steps.Any(s => s.Wait == "selection:2")) doc.Select([ids.Ring, ids.Outline]);
                else if (t.Id.Contains("stencil") || t.Id.Contains("text")) doc.Select([ids.Text]);
                else doc.Select([pictures ? ids.Picture : ids.Outline]);
                Settle(100);
                w.StartTutorial(t.Id);
                Settle(300);
                var run = TutorialRunner.Running;
                if (run is null) { lines.Add($"{t.Id}: didn't start"); bad++; continue; }
                for (int i = 0; i < t.Steps.Count; i++)
                {
                    var step = t.Steps[i];
                    Settle(250);
                    steps++;
                    if (step.Target is { } target)
                    {
                        if (run.Pointed is Control c)
                        {
                            pointed++;
                            // Do what the user would, so what it opens is there for the next steps: ribbon and top
                            // buttons are clicked (and the menu item the step names, for a button with a menu);
                            // a button that opens or closes a window is clicked; a tab or a page in a list is picked.
                            try { Act(c, step); } catch (Exception e) { Console.WriteLine($"  act {t.Id} step {i + 1}: {e.Message}"); }
                        }
                        else
                        {
                            bad++;
                            lines.Add($"{t.Id} step {i + 1} \"{step.Title}\": can't find {target}");
                            Console.WriteLine("  MISSING " + lines[^1]);
                        }
                    }
                    if (TutorialRunner.Running != run) break;
                    // A step whose wait was met has already moved on by itself: don't skip the next one.
                    if (i < t.Steps.Count - 1 && run.StepIndex == i) run.Next();
                }
                run.Stop();
            }
            Console.WriteLine($"Walked {walked} tutorials, {steps} steps: {pointed} targets found, {bad} not.");
            foreach (var l in lines) Console.WriteLine("  " + l);
        }
    }

    /// <summary>A step that starts "Click …": the user clicks its button before going on.</summary>
    static bool SaysClick(TutorialStep step) =>
        step.Body.FirstOrDefault() is Paragraph p && HelpText.Plain(p.Inlines).TrimStart().StartsWith("Click", StringComparison.OrdinalIgnoreCase);

    /// <summary>The bold words in a step: the names of what to click.</summary>
    static IEnumerable<string> BoldIn(IEnumerable<Block> blocks)
    {
        IEnumerable<Inline> Runs(Block b) => b switch
        {
            Paragraph p => p.Inlines,
            ListBlock l => l.Items.SelectMany(Item),
            Callout co => co.Inlines,
            _ => [],
        };
        IEnumerable<Inline> Item(ListItem it) => it.Inlines.Concat(it.Children.SelectMany(Item));
        return blocks.SelectMany(Runs).OfType<TextRun>().Where(r => r.Bold).Select(r => r.Text.Trim());
    }

    /// <summary>What a user does at a step, in the walk: see where it's called.</summary>
    static void Act(Control c, TutorialStep step)
    {
        string target = step.Target ?? "";
        bool opens = step.Wait is { } wt && (wt.StartsWith("window:") || wt.StartsWith("closed:") || wt.StartsWith("tab:"));
        void Click(Control b) => b.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        if (c is Avalonia.Controls.Button { Flyout: MenuFlyout menu })
        {
            // The menu item the step names in bold ("then **Circles by size…**").
            // "**File → Save as template…**" names the menu and the item.
            var names = BoldIn(step.Body).SelectMany(n => n.Split('→', StringSplitOptions.TrimEntries)).ToList();
            var item = Items(menu.Items).FirstOrDefault(mi => names.Any(n => string.Equals((mi.Header as string)?.Trim(), n, StringComparison.OrdinalIgnoreCase)));
            if (item is not null)
            {
                if (item.Command is { } cmd) cmd.Execute(item.CommandParameter);
                else item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            }
            else Console.WriteLine($"  no menu item among [{string.Join(", ", names)}] for {target}");
        }
        else if (target.StartsWith("ribbon:") || target.StartsWith("header:"))
        {
            Click(c);
            // A test opens through the Tests window, with the step's test already picked: click Open.
            if (target.StartsWith("ribbon:Tests/") && step.Wait is { } w && w != "window:Tests" && TopLevel.GetTopLevel(c) is Window main)
            {
                Settle(400);
                if (main.OwnedWindows.OfType<TestsWindow>().FirstOrDefault() is { } tests
                    && tests.GetVisualDescendants().OfType<Avalonia.Controls.Button>().FirstOrDefault(b => b.Content as string == "Open") is { } open)
                    Click(open);
            }
        }
        else if (c is TabItem ti) ti.IsSelected = true;
        else if (target.StartsWith("text:") && c.FindAncestorOfType<ListBoxItem>() is { } li && li.FindAncestorOfType<ListBox>() is { } lb)
            lb.SelectedItem = lb.ItemFromContainer(li) ?? li;
        else if ((opens || SaysClick(step)) && c is Avalonia.Controls.Button and not Avalonia.Controls.Primitives.ToggleButton) Click(c);
        else return;
        Settle(700);
    }

    static IEnumerable<MenuItem> Items(IEnumerable<object?> items)
    {
        foreach (var o in items)
            if (o is MenuItem mi)
            {
                yield return mi;
                foreach (var sub in Items(mi.Items)) yield return sub;
            }
    }

    static Action _pump = () => { };

    static void Settle(int ms) { for (int i = 0; i < ms / 25; i++) { _pump(); Thread.Sleep(25); } }

    /// <summary>The main window with the tutorial card drawn where it sits.</summary>
    static void Compose(Window main, Window card, string path)
    {
        var a = Path.GetTempFileName();
        var b = Path.GetTempFileName();
        main.CaptureRenderedFrame()?.Save(a);
        card.CaptureRenderedFrame()?.Save(b);
        using var mainBmp = SKBitmap.Decode(a);
        using var cardBmp = SKBitmap.Decode(b);
        if (mainBmp is null || cardBmp is null) return;
        using var canvas = new SKCanvas(mainBmp);
        int x = card.Position.X - main.Position.X, y = card.Position.Y - main.Position.Y;
        if (x < 0 || y < 0 || x + cardBmp.Width > mainBmp.Width || y + cardBmp.Height > mainBmp.Height)
        {
            x = mainBmp.Width - cardBmp.Width - 40;
            y = mainBmp.Height - cardBmp.Height - 150;
        }
        using (var shadow = new SKPaint { Color = new SKColor(0, 0, 0, 120), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 10) })
            canvas.DrawRect(x + 4, y + 6, cardBmp.Width, cardBmp.Height, shadow);
        canvas.DrawBitmap(cardBmp, x, y);
        using var img = SKImage.FromBitmap(mainBmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, data.ToArray());
    }
}
