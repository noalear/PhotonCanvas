using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PhotonCanvas.Doc.Help;
using static PhotonCanvas.App.UiKit;

namespace PhotonCanvas.App;

/// <summary>What a tutorial needs from the main window: the things it points at, and whether a step is done.</summary>
public interface ITutorialHost
{
    Window Main { get; }

    /// <summary>
    /// A landmark of the main window (ribbon:Tab/Label, tab:Name, header:Name, panel:Name, canvas),
    /// made visible (its ribbon tab chosen, its panel unfolded); null when it isn't one or isn't there.
    /// </summary>
    Control? Landmark(string kind, string value);

    /// <summary>What's counted at the start of a step, to see what the user has done since.</summary>
    TutorialSnapshot Counts();

    /// <summary>Whether a wait is met (selection, shapes:+1, tool:Rect…); null when it's not one the host knows.</summary>
    bool? Met(string kind, string value, TutorialSnapshot atStart);
}

/// <summary>The design as a step starts: shapes, layers, zoom, and what's selected (to wait for a new selection, not an old one).</summary>
public readonly record struct TutorialSnapshot(int Shapes, int Layers, double Zoom, int Selected, int SelectionKey);

/// <summary>The tutorials, as a table of contents: by topic, each with what it covers and how long it is.</summary>
public sealed class TutorialsWindow : Window
{
    static TutorialsWindow? _open;
    readonly HelpLibrary _lib;
    readonly Action<string> _start;
    readonly Action<string> _readGuide;
    readonly TextBox _search = new() { Watermark = "Find a tutorial" };
    readonly StackPanel _list = new() { Spacing = 4 };

    public static void Show(Window owner, Action<string> start, Action<string> readGuide)
    {
        if (_open is null)
        {
            _open = new TutorialsWindow(HelpFiles.Library, start, readGuide);
            _open.Closed += (_, _) => _open = null;
            _open.Show(owner);
        }
        else _open.Activate();
    }

    /// <summary>Close it (a tutorial has started: it would only be in the way).</summary>
    public static void CloseIfOpen() => _open?.Close();

    TutorialsWindow(HelpLibrary lib, Action<string> start, Action<string> readGuide)
    {
        _lib = lib;
        _start = start;
        _readGuide = readGuide;
        Title = "Tutorials";
        Width = 760;
        Height = 820;
        MinWidth = 520;
        Background = Brush("Bg1");
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _search.TextChanged += (_, _) => Fill();
        var intro = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap, Foreground = Brush("TextMuted"),
            Text = "Pick what you'd like to learn. A small card walks you through it a step at a time and points at each button and setting as you go. " +
                   "You can stop any time; nothing is changed that you don't do yourself.",
        };
        var top = new StackPanel { Spacing = 10, Margin = new(20, 18, 20, 10), Children = { new TextBlock { Text = "Tutorials", FontSize = 24, FontWeight = FontWeight.SemiBold }, intro, _search } };
        var dock = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        dock.Children.Add(top);
        dock.Children.Add(new ScrollViewer { Content = new Border { Padding = new(20, 0, 20, 24), Child = _list } });
        Content = dock;
        Fill();
    }

    /// <summary>The order topics come in (others after, alphabetically).</summary>
    static readonly string[] TopicOrder =
    [
        "Getting started", "The workspace", "Drawing", "Editing shapes", "Arranging", "Importing and exporting", "Layers and settings",
        "Pictures", "Camera", "Materials and tests", "Running jobs", "Work area", "Machine and settings", "3D and slices",
    ];

    void Fill()
    {
        _list.Children.Clear();
        var words = (_search.Text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var shown = _lib.Tutorials.Where(t => words.All(w => (t.Title + " " + t.Summary + " " + t.Category).Contains(w, StringComparison.OrdinalIgnoreCase))).ToList();
        if (_lib.Tutorials.Count == 0) _list.Children.Add(Hint($"No tutorials were found (looked in {Path.Combine(_lib.Folder, "tutorials")})."));
        else if (shown.Count == 0) _list.Children.Add(Hint("No tutorial matches. Try a shorter word."));
        foreach (var topic in shown.GroupBy(t => t.Category).OrderBy(g => Array.IndexOf(TopicOrder, g.Key) is var i && i >= 0 ? i : 100).ThenBy(g => g.Key))
        {
            _list.Children.Add(new TextBlock { Text = topic.Key.ToUpperInvariant(), Classes = { "h" }, Margin = new(0, 14, 0, 2) });
            foreach (var t in topic.OrderBy(t => t.Order).ThenBy(t => t.Title))
            {
                var start = new Button { Content = "Start", Classes = { "primary" }, VerticalAlignment = VerticalAlignment.Center, MinWidth = 72, HorizontalContentAlignment = HorizontalAlignment.Center };
                var id = t.Id;
                start.Click += (_, _) => _start(id);
                var text = new StackPanel
                {
                    Spacing = 2,
                    Children =
                    {
                        new TextBlock { Text = t.Title, FontWeight = FontWeight.SemiBold, FontSize = 14.5, TextWrapping = TextWrapping.Wrap },
                        new TextBlock { Text = t.Summary, Foreground = Brush("TextMuted"), TextWrapping = TextWrapping.Wrap },
                        new TextBlock { Text = $"{t.Steps.Count} steps", FontSize = 11.5, Foreground = Brush("TextMuted") },
                    },
                };
                var row = new DockPanel();
                var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
                if (t.Page is { } page)
                {
                    var read = new Button { Content = "Read" };
                    ToolTip.SetTip(read, "Open the guide's full explanation");
                    read.Click += (_, _) => _readGuide(page);
                    right.Children.Add(read);
                }
                right.Children.Add(start);
                DockPanel.SetDock(right, Dock.Right);
                row.Children.Add(right);
                text.Margin = new(0, 0, 12, 0);
                row.Children.Add(text);
                _list.Children.Add(new Border { Classes = { "card" }, Padding = new(14, 10), Child = row });
            }
        }
    }
}

/// <summary>
/// Runs a tutorial: a small card that stays on top (and can be dragged anywhere) with the step's
/// instructions, Back and Next; the control the step is about is ringed in the accent colour (its
/// ribbon tab opened, its panel unfolded, its section expanded); a step that waits for something to be
/// done moves on by itself once it is.
/// </summary>
public sealed class TutorialRunner
{
    static TutorialRunner? _running;
    readonly ITutorialHost _host;
    readonly Tutorial _tutorial;
    readonly Action<string> _readGuide;
    readonly Window _card;
    readonly ContentControl _body = new();
    readonly TextBlock _counter = new() { Foreground = Brush("TextMuted"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12.5, IsVisible = false };
    readonly Button _back = new() { Content = "Back" };
    readonly Button _next = new() { Content = "Next", Classes = { "primary" }, MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center };
    readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromMilliseconds(250) };
    int _step = -1;
    TutorialSnapshot _atStart;
    Control? _ring;
    Visual? _ringed;
    bool _moved, _done;
    int _pulse;

    /// <summary>Start a tutorial (stopping one already running).</summary>
    public static void Start(ITutorialHost host, Tutorial tutorial, Action<string> readGuide)
    {
        _running?.Stop();
        _running = new TutorialRunner(host, tutorial, readGuide);
        _running.Go(0);
    }

    /// <summary>The tutorial running, if any (for the harness and tests of tutorials).</summary>
    public static TutorialRunner? Running => _running;
    public int StepIndex => _step;
    public Tutorial Tutorial => _tutorial;
    /// <summary>What the current step points at (null if nothing, or it couldn't be found).</summary>
    public Visual? Pointed => _ringed;
    public Window Card => _card;

    TutorialRunner(ITutorialHost host, Tutorial tutorial, Action<string> readGuide)
    {
        _host = host;
        _tutorial = tutorial;
        _readGuide = readGuide;
        var close = new Button { Content = "✕", Classes = { "icon" }, Padding = new(6, 2), VerticalAlignment = VerticalAlignment.Center };
        ToolTip.SetTip(close, "Stop the tutorial");
        close.Click += (_, _) => Stop();
        var title = new TextBlock { Text = tutorial.Title, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        var head = new DockPanel { Background = Brush("Bg3"), Cursor = new Cursor(StandardCursorType.SizeAll) };
        var label = new TextBlock { Text = "TUTORIAL", Classes = { "h" }, Margin = new(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(label, Dock.Left);
        DockPanel.SetDock(close, Dock.Right);
        DockPanel.SetDock(_counter, Dock.Right);
        _counter.Margin = new(8, 0);
        head.Children.Add(label);
        head.Children.Add(close);
        head.Children.Add(_counter);
        head.Children.Add(title);
        var headBox = new Border { Background = Brush("Bg3"), Padding = new(12, 8), Child = head };
        _back.Click += (_, _) => Go(_step - 1);
        _next.Click += (_, _) => Next();
        var foot = new DockPanel { Margin = new(14, 4, 14, 12) };
        var nav = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { _back, _next } };
        DockPanel.SetDock(nav, Dock.Right);
        foot.Children.Add(nav);
        if (tutorial.Page is { } page)
        {
            var read = new TextBlock { Text = "Read the guide", Foreground = Brush("Accent"), TextDecorations = TextDecorations.Underline, Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center, FontSize = 12.5 };
            read.PointerPressed += (_, e) => { e.Handled = true; _readGuide(page); };
            foot.Children.Add(read);
        }
        var body = new StackPanel { Spacing = 8, Margin = new(14, 10, 14, 6), Children = { _body, _status } };
        _card = new Window
        {
            Title = "Tutorial", Width = 400, SizeToContent = SizeToContent.Height, CanResize = false, Topmost = true, ShowInTaskbar = false,
            SystemDecorations = SystemDecorations.None, Background = Brush("Bg2"),
            Content = new Border
            {
                BorderBrush = Brush("Accent"), BorderThickness = new(1.5), CornerRadius = new(10), ClipToBounds = true,
                Child = new DockPanel { Children = { Docked(headBox, Avalonia.Controls.Dock.Top), Docked(foot, Avalonia.Controls.Dock.Bottom), body } },
            },
        };
        head.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(head).Properties.IsLeftButtonPressed) return;
            _moved = true;
            _card.BeginMoveDrag(e);
        };
        _card.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Stop();
            else if (e.Key is Key.Right or Key.Enter) Next();
            else if (e.Key == Key.Left) Go(_step - 1);
        };
        _poll.Tick += (_, _) => Tick();
        _card.Show(host.Main);
    }

    static T Docked<T>(T c, Dock d) where T : Control
    {
        DockPanel.SetDock(c, d);
        return c;
    }

    /// <summary>On to the next step, or finished after the last.</summary>
    public void Next()
    {
        if (_step >= _tutorial.Steps.Count - 1) Stop();
        else Go(_step + 1);
    }

    void Go(int i)
    {
        if (i < 0 || i >= _tutorial.Steps.Count) return;
        _step = i;
        _done = false;
        var step = _tutorial.Steps[i];
        _atStart = _host.Counts();
        _counter.Text = $"{i + 1} / {_tutorial.Steps.Count}";
        var r = new HelpRenderer(HelpFiles.Library.FolderForTutorial(_tutorial.Id), t => { if (t.StartsWith("tutorial:")) { } else _readGuide(t); }) { Compact = true };
        var blocks = new List<Block>();
        if (i > 0 || step.Title != _tutorial.Title) blocks.Add(new Heading(2, step.Title, ""));
        blocks.AddRange(step.Body);
        _body.Content = r.Render(blocks);
        _back.IsEnabled = i > 0;
        _next.Content = i == _tutorial.Steps.Count - 1 ? "Finish" : "Next";
        _status.IsVisible = false;
        Point(step.Target);
        Place();
        if (step.Wait is not null)
        {
            _status.Text = "When you've done this, the tutorial moves on by itself (or press Next).";
            _status.Foreground = Brush("TextMuted");
            _status.IsVisible = true;
            _poll.Start();
        }
        else if (_ring is null) _poll.Stop();
    }

    void Tick()
    {
        try { KeepRinged(); }
        catch (Exception e) { AppLog.Write("tutorial", $"{_tutorial.Id}: {e.Message}"); Unring(); }
        if (_done || _step < 0 || _tutorial.Steps[_step].Wait is not { } wait) return;
        bool met;
        try { met = Met(wait); }
        catch (Exception e) { AppLog.Write("tutorial", $"{_tutorial.Id}: waiting for {wait}: {e.Message}"); met = false; }
        if (!met) return;
        _done = true;
        _status.Text = "✓ Done!";
        _status.Foreground = Brush("Ok");
        int step = _step;
        DispatcherTimer.RunOnce(() => { if (_step == step && _step < _tutorial.Steps.Count - 1) Go(_step + 1); }, TimeSpan.FromMilliseconds(700));
    }

    /// <summary>Whether the step's wait is met.</summary>
    bool Met(string wait)
    {
        var (kind, value) = TutorialTargets.Split(wait);
        switch (kind)
        {
            case "window":
                return Windows().Any(w => w.Title?.StartsWith(value, StringComparison.OrdinalIgnoreCase) == true);
            case "closed":
                return !Windows().Any(w => w.Title?.StartsWith(value, StringComparison.OrdinalIgnoreCase) == true);
            default:
                return _host.Met(kind, value, _atStart) ?? false;
        }
    }

    /// <summary>The program's windows, newest first, leaving out this card and the help.</summary>
    IEnumerable<Window> Windows() =>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Windows.Reverse().Where(w => w != _card && w is not HelpWindow and not TutorialsWindow)
        ?? Owned(_host.Main).Append(_host.Main).Where(w => w != _card);

    /// <summary>The windows a window opened, and the ones they opened (the model browser from Stacked slices), newest first.</summary>
    static IEnumerable<Window> Owned(Window w) => w.OwnedWindows.Reverse().SelectMany(o => Owned(o).Append(o));

    /// <summary>Ring the step's target, wherever it is.</summary>
    void Point(string? target)
    {
        Unring();
        if (target is null) return;
        Control? c;
        // A tutorial never takes the program down: if the target can't be looked for, the step just isn't ringed.
        try { c = Find(target); }
        catch (Exception e) { AppLog.Write("tutorial", $"{_tutorial.Id}: looking for {target}: {e.Message}"); return; }
        if (c is null) return;
        Reveal(c);
        Ring(c);
        // Once its section is open, the copy now showing may be another (the same name for another kind of layer).
        Dispatcher.UIThread.Post(() =>
        {
            if (Find(target) is { IsEffectivelyVisible: true } again && again != _ringed)
            {
                Unring();
                Ring(again);
            }
        }, DispatcherPriority.Background);
    }

    /// <summary>A control the step points at: the main window's landmarks, or a field, button, text or tab in any open window.</summary>
    Control? Find(string target)
    {
        var (kind, value) = TutorialTargets.Split(target);
        if (kind is "ribbon" or "tab" or "header" or "panel" or "canvas") return _host.Landmark(kind, value);
        foreach (var w in Windows())
        {
            if (kind == "window" && w.Title?.StartsWith(value, StringComparison.OrdinalIgnoreCase) == true) return w.Content as Control;
            var found = FindIn(w, kind, value);
            if (found is not null) return found;
        }
        return null;
    }

    /// <summary>
    /// A setting, button, tab or text in a window. The side panels are searched before the rest of
    /// the window (a setting called Order is the layer's, not the job bar's chip), and what's showing
    /// before what isn't (the same name in a section for another kind of layer).
    /// </summary>
    static Control? FindIn(Window w, string kind, string value)
    {
        var everything = w.GetVisualDescendants().OfType<Control>().Concat(w.GetLogicalDescendantsOf()).Distinct().ToList();
        // The main window's side panels (windows built in code have no names to look up).
        var names = NameScope.GetNameScope(w);
        var panels = names is null ? [] : new[] { "RightDock", "LeftDock" }.Select(n => names.Find<Control>(n)).OfType<Control>().ToList();
        bool InPanel(Control c) => panels.Any(p => p == c || p.IsVisualAncestorOf(c) || IsLogicalAncestor(p, c));
        var ordered = everything.Where(InPanel).Concat(everything.Where(c => !InPanel(c))).ToList();
        foreach (bool visibleOnly in new[] { true, false })
        {
            var pool = visibleOnly ? ordered.Where(c => c.IsEffectivelyVisible).ToList() : ordered;
            if (Match(pool, kind, value, exact: true) is { } exact) return exact;
            // A button whose text changes as it's used ("Save chosen squares (3)"): by how its text starts.
            if (kind == "button" && Match(pool, kind, value, exact: false) is { } prefix) return prefix;
        }
        return null;
    }

    static bool IsLogicalAncestor(Control ancestor, Control c)
    {
        for (var p = c.Parent; p is not null; p = p.Parent)
            if (p == ancestor) return true;
        return false;
    }

    static Control? Match(IEnumerable<Control> pool, string kind, string value, bool exact)
    {
        foreach (var c in pool)
            switch (kind)
            {
                case "field" when c is TextBlock t && Same(t.Text, value):
                    // A setting: its name and the box beside or under it.
                    return t.Parent as Control ?? t;
                case "button" when c is Avalonia.Controls.Button or ToggleButton or CheckBox or RadioButton
                                   && (exact ? Same(ContentText(c), value) : ContentText(c)?.StartsWith(value, StringComparison.OrdinalIgnoreCase) == true):
                    return c;
                case "tabitem" when c is TabItem ti && Same(ti.Header?.ToString(), value):
                    return ti;
                case "text" when c is TextBlock t2 && t2.Text?.Contains(value, StringComparison.OrdinalIgnoreCase) == true:
                    return t2;
            }
        return null;
    }

    static bool Same(string? a, string b) => a is not null && string.Equals(a.Trim().TrimEnd(':', '…'), b.Trim().TrimEnd(':', '…'), StringComparison.OrdinalIgnoreCase);

    static string? ContentText(Control c) => c switch
    {
        ContentControl { Content: string s } => s,
        ContentControl { Content: Control inner } => inner is TextBlock tb ? tb.Text : inner.GetLogicalDescendantsOf().OfType<TextBlock>().FirstOrDefault()?.Text,
        _ => null,
    };

    /// <summary>Make a control visible: its sections expanded, its tab chosen, scrolled into view.</summary>
    static void Reveal(Control c)
    {
        for (var a = c.Parent as Control ?? c.GetVisualParent() as Control; a is not null; a = a.Parent as Control ?? a.GetVisualParent() as Control)
        {
            if (a is Expander ex) ex.IsExpanded = true;
            if (a is TabItem ti && ti.Parent is TabControl tc) tc.SelectedItem = ti;
        }
        Dispatcher.UIThread.Post(() => c.BringIntoView(), DispatcherPriority.Background);
    }

    /// <summary>
    /// Ring a control: a border on its window's overlay layer, placed from where the control really is
    /// (and moved with it, as the layout changes).
    /// </summary>
    void Ring(Control c)
    {
        if (OverlayLayer.GetOverlayLayer(c) is not { } layer) return;
        _ring = new Border
        {
            BorderBrush = Brush("Accent"), BorderThickness = new(3), CornerRadius = new(7), IsHitTestVisible = false,
            BoxShadow = new BoxShadows(new BoxShadow { Blur = 14, Spread = 2, Color = Color.FromArgb(150, 76, 123, 217) }),
        };
        layer.Children.Add(_ring);
        _ringLayer = layer;
        _ringed = c;
        _pulse = 0;
        PlaceRing();
        _poll.Start();
    }

    Panel? _ringLayer;

    void PlaceRing()
    {
        if (_ring is null || _ringLayer is null || _ringed is not Control c) return;
        if (!c.IsEffectivelyVisible || c.TranslatePoint(new Point(0, 0), _ringLayer) is not { } p) { _ring.IsVisible = false; return; }
        _ring.IsVisible = true;
        Canvas.SetLeft(_ring, p.X - 5);
        Canvas.SetTop(_ring, p.Y - 5);
        _ring.Width = c.Bounds.Width + 10;
        _ring.Height = c.Bounds.Height + 10;
    }

    /// <summary>Pulse the ring and keep it on its control; ring it again if the control was rebuilt (layer settings are, on edits).</summary>
    void KeepRinged()
    {
        if (_ringed is Control c && !c.IsAttachedToVisualTree() && _step >= 0)
        {
            Point(_tutorial.Steps[_step].Target);
            return;
        }
        if (_ring is not null) _ring.Opacity = 0.55 + 0.45 * Math.Abs(Math.Sin(++_pulse * 0.35));
        PlaceRing();
    }

    void Unring()
    {
        if (_ring is not null) _ringLayer?.Children.Remove(_ring);
        _ring = null;
        _ringLayer = null;
        _ringed = null;
    }

    /// <summary>
    /// Put the card beside what the step points at, not over it (unless the user has moved the card):
    /// under the ribbon for a ribbon button, to the left of something on the right of the window, to
    /// the right of something on the left, else below it. With nothing to point at, bottom right.
    /// </summary>
    void Place()
    {
        if (_moved) return;
        Dispatcher.UIThread.Post(() =>
        {
            var main = _host.Main;
            var screen = main.Screens.ScreenFromWindow(main)?.WorkingArea;
            double scale = main.RenderScaling;
            double cw = _card.Bounds.Width > 0 ? _card.Bounds.Width : 400, ch = _card.Bounds.Height > 0 ? _card.Bounds.Height : 260;
            var mainPos = main.Position;
            PixelPoint at;
            if (_ringed is Control c && c.GetVisualRoot() is TopLevel top && c.TranslatePoint(new Point(0, 0), top) is { } p)
            {
                // In device-independent units, from the target's window's corner.
                var origin = top is Window tw ? tw.Position : mainPos;
                double ox = origin.X / scale, oy = origin.Y / scale, tw2 = top.Bounds.Width;
                double x = ox + p.X, y = oy + p.Y, w = c.Bounds.Width, h = c.Bounds.Height;
                double cx, cy;
                if (c.FindAncestorOfType<Ribbon>() is { } ribbon && ribbon.TranslatePoint(new Point(0, ribbon.Bounds.Height), top) is { } rb)
                { cx = x - 20; cy = oy + rb.Y + 10; }
                else if (p.X + w / 2 > tw2 * 0.6) { cx = x - cw - 18; cy = y + h / 2 - ch / 2; }
                else if (p.X + w / 2 < tw2 * 0.3) { cx = x + w + 18; cy = y + h / 2 - ch / 2; }
                else { cx = x + w / 2 - cw / 2; cy = y + h + 16; }
                at = new PixelPoint((int)(cx * scale), (int)(cy * scale));
            }
            else
                at = new PixelPoint(mainPos.X + (int)((main.Bounds.Width - cw - 40) * scale), mainPos.Y + (int)((main.Bounds.Height - ch - 150) * scale));
            if (screen is { } s)
                at = new PixelPoint(Math.Clamp(at.X, s.X + 8, Math.Max(s.X + 8, s.Right - (int)(cw * scale) - 8)), Math.Clamp(at.Y, s.Y + 8, Math.Max(s.Y + 8, s.Bottom - (int)(ch * scale) - 8)));
            _card.Position = at;
        }, DispatcherPriority.Background);
    }

    public void Stop()
    {
        _poll.Stop();
        Unring();
        _card.Close();
        if (_running == this) _running = null;
    }
}

public static class LogicalExtensions
{
    /// <summary>Every logical descendant (controls not drawn yet included).</summary>
    public static IEnumerable<Control> GetLogicalDescendantsOf(this Control root)
    {
        var stack = new Stack<Avalonia.LogicalTree.ILogical>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            foreach (var child in n.LogicalChildren)
            {
                if (child is Control c) yield return c;
                stack.Push(child);
            }
        }
    }
}
