using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PhotonCanvas.App.Addons;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Help;

namespace PhotonCanvas.App;

/// <summary>
/// What a part of the program says about itself for the right-click help (<see cref="ContextHelp"/>):
/// the add-ons that made it, and the page, tutorial or tutorial targets that explain it.
/// </summary>
public sealed record HelpSubject
{
    /// <summary>Add-ons that made it (the main one first).</summary>
    public IReadOnlyList<string> Addons { get; init; } = [];
    /// <summary>The page ("file.md#anchor") about exactly this.</summary>
    public string? Page { get; init; }
    /// <summary>The tutorial (id) about exactly this.</summary>
    public string? Tutorial { get; init; }
    /// <summary>Tutorial step targets that point at it ("panel:Layers", "canvas").</summary>
    public IReadOnlyList<string> Targets { get; init; } = [];
    /// <summary>
    /// True: this is the thing itself (a shape), so labels of the controls inside it don't count.
    /// False: it's an area of the program (a panel), whose page is used when nothing inside it is explained.
    /// </summary>
    public bool Exact { get; init; }
}

/// <summary>
/// Right-click on anything: <b>Disable add-on</b> (the add-on that made what was clicked),
/// <b>See tutorial</b> and <b>See help</b> (its own tutorial and section of the guide), at the end of
/// the right-click menu that opens there, or in a menu of their own where there's none.
///
/// Nothing needs adding anywhere for it to work: what was clicked is worked out from the controls
/// themselves. Add-ons are known by the load context their window or panel classes came from, and
/// their ribbon buttons and slot panels are marked when added; help is found by the labels on and
/// around what was clicked, as the guide names them in bold (<see cref="HelpFinder"/>). Parts of the
/// main window that can say more (the canvas: the shapes picked; the panels: their page) do so with
/// <see cref="Describe"/>.
/// </summary>
public static class ContextHelp
{
    /// <summary>Marks the menu items added here, so a menu shown again gets fresh ones.</summary>
    const string Mark = "pc-context-help";

    static MainWindow? _main;
    static WeakReference<Control>? _target;
    static long _targetAt;
    static readonly ConditionalWeakTable<Control, Func<HelpSubject?>> Described = new();
    static readonly ConditionalWeakTable<Control, string> Owners = new();

    /// <summary>Hook every window's right-click (once, by the main window).</summary>
    internal static void Install(MainWindow main)
    {
        bool first = _main is null;
        _main = main;
        if (!first) return;
        // Tunnel: seen before any control's own handler opens its menu, so a menu already set on the
        // control (or text box) gets the items before it shows, and menus opened in code know what for.
        Control.ContextRequestedEvent.AddClassHandler<TopLevel>(Requested, RoutingStrategies.Tunnel, handledEventsToo: true);
        // Bubble, not handled: nothing there has a menu, so show one with just these.
        Control.ContextRequestedEvent.AddClassHandler<TopLevel>(Unanswered, RoutingStrategies.Bubble);
        // A menu opened in code (the canvas's, a layer's): add them to it.
        MenuBase.OpenedEvent.AddClassHandler<ContextMenu>((menu, _) => Opened(menu));
        // A menu opened by a plain click (the machine pill's) isn't a right-click's.
        InputElement.PointerPressedEvent.AddClassHandler<TopLevel>((_, e) =>
        {
            if (!e.GetCurrentPoint(null).Properties.IsRightButtonPressed) _target = null;
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    /// <summary>Say what a control (and what's inside it) is, for the help items. Asked each time it's right-clicked.</summary>
    public static void Describe(Control c, Func<HelpSubject?> subject) => Described.AddOrUpdate(c, subject);

    /// <summary>A control an add-on made (its ribbon button, its panel in a program window).</summary>
    internal static void Own(Control c, string addonId) => Owners.AddOrUpdate(c, addonId);

    static void Requested(TopLevel top, ContextRequestedEventArgs e)
    {
        if (e.Source is not Control source || InMenu(source)) return;
        _target = new WeakReference<Control>(source);
        _targetAt = Environment.TickCount64;
        // The menu or flyout that will open: the nearest one set on the control or above it.
        for (Control? c = source; c is not null; c = Up(c))
        {
            if (c.ContextMenu is { } menu)
            {
                if (menu.ItemsSource is null) Fill(menu.Items, source);
                return;
            }
            if (c.ContextFlyout is MenuFlyout flyout)
            {
                if (flyout.ItemsSource is null) Fill(flyout.Items, source);
                return;
            }
            if (c.ContextFlyout is not null) return;
        }
    }

    static void Unanswered(TopLevel top, ContextRequestedEventArgs e)
    {
        if (e.Handled || e.Source is not Control source || InMenu(source) || _main is null) return;
        var menu = new ContextMenu();
        Fill(menu.Items, source);
        e.Handled = true;
        menu.Open(source);
    }

    static void Opened(ContextMenu menu)
    {
        if (menu.ItemsSource is not null || menu.Items.OfType<Control>().Any(i => Mark.Equals(i.Tag))) return;
        // Only a menu opening for this right-click, not one opened later by something else.
        if (_target?.TryGetTarget(out var target) != true || target is null || Environment.TickCount64 - _targetAt > 1500) return;
        _target = null;
        Fill(menu.Items, target);
    }

    /// <summary>Is this inside a menu that's open (a right-click on a menu item isn't about the program).</summary>
    static bool InMenu(Control c)
    {
        for (Control? a = c; a is not null; a = Up(a))
            if (a is MenuItem or ContextMenu or MenuFlyoutPresenter) return true;
        return false;
    }

    /// <summary>
    /// The control holding this one: for a part of a control's template (the text box inside a number
    /// box), that control; else its visual parent; else its logical one (out of a popup).
    /// </summary>
    static Control? Up(Control c) => c.TemplatedParent as Control ?? c.GetVisualParent() as Control ?? c.Parent as Control;

    /// <summary>The control a person sees as the thing: not a part of its template.</summary>
    static Control Owning(Control c)
    {
        while (c.TemplatedParent is Control owner) c = owner;
        return c;
    }

    // ---- what was clicked -------------------------------------------------------------

    /// <summary>What the guide knows about a control, and the add-ons that made it (the main one first).</summary>
    public static (HelpQuery Query, IReadOnlyList<AddonEntry> Addons) Resolve(Control target)
    {
        var labels = new List<string>();
        var owners = new List<string>();
        string? ribbon = null, window = null, page = null, tutorial = null, area = null;
        var targets = new List<string>();
        bool exact = false, fieldFound = false;
        int depth = 0;
        for (Control? c = Owning(target); c is not null; c = Up(c), depth++)
        {
            if (Owners.TryGetValue(c, out var owned)) AddOwner(owners, owned);
            if (AddonOf(c.GetType()) is { } fromType) AddOwner(owners, fromType);
            if (Described.TryGetValue(c, out var describe) && Safe(describe) is { } s)
            {
                foreach (var a in s.Addons) AddOwner(owners, a);
                if (s.Exact && !exact && page is null && ribbon is null)
                {
                    exact = true;
                    labels.Clear();
                    page = s.Page;
                    tutorial = s.Tutorial;
                    targets.AddRange(s.Targets);
                }
                else if (!s.Exact && !exact && area is null)
                {
                    area = s.Page;
                    targets.AddRange(s.Targets);
                }
            }
            if (c is Window w)
            {
                // A panel floated off the main window is in a plain Window: still the main window's.
                bool ownWindow = w is not MainWindow && w.GetType() != typeof(Window);
                if (ownWindow) window = w.GetType().Name;
                if (!exact && ownWindow && w.Title is { Length: > 0 } title) labels.Add(title);
                break;
            }
            if (exact) continue;
            ribbon ??= _main?.RibbonCommandOf(c);
            if (depth == 0 && c is TextBlock { Text: { Length: > 0 } own }) labels.Add(own);
            if (depth <= 3 && TextOf(c) is { Length: > 0 } text) labels.Add(text);
            if (depth <= 3 && ToolTip.GetTip(c) is string tip) labels.Add(FirstLine(tip));
            if (!fieldFound && depth <= 6 && FieldLabel(c) is { } field)
            {
                labels.Add(field);
                fieldFound = true;
            }
            switch (c)
            {
                case Expander { Header: string h }: labels.Add(h); break;
                case HeaderedContentControl { Header: string h }: labels.Add(h); break;
                case TabControl { SelectedItem: TabItem { Header: string tab } }: labels.Add(tab); break;
            }
        }
        var entries = owners.Select(id => _main?.Addons.Find(id)).OfType<AddonEntry>().Where(e => e.IsOn).ToList();
        // The main one: the innermost that no other one found here needs (a building's walls are the building kit's).
        var main = entries.FirstOrDefault(e => !entries.Any(o => o != e && o.Manifest.Requires.Contains(e.Id, StringComparer.OrdinalIgnoreCase))) ?? entries.FirstOrDefault();
        if (main is not null) entries = [main, .. entries.Where(e => e != main)];
        var query = new HelpQuery
        {
            Labels = labels.Select(l => l.Trim()).Where(l => l.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            Ribbon = ribbon,
            Window = window,
            Folder = main is null ? null : Path.Combine(main.Manifest.Folder, "help"),
            Page = page,
            Tutorial = tutorial,
            Area = main is null ? area : null,
            Targets = targets,
        };
        return (query, entries);
    }

    static HelpSubject? Safe(Func<HelpSubject?> f)
    {
        try { return f(); }
        catch (Exception e)
        {
            AppLog.Write("help", $"Right-click help: {e.Message}");
            return null;
        }
    }

    static void AddOwner(List<string> owners, string id)
    {
        if (!owners.Contains(id, StringComparer.OrdinalIgnoreCase)) owners.Add(id);
    }

    /// <summary>The add-on whose assembly a class came from (its windows and panels).</summary>
    static string? AddonOf(Type t) => AssemblyLoadContext.GetLoadContext(t.Assembly) is AddonLoadContext ctx ? ctx.Manifest.Id : null;

    static string FirstLine(string s)
    {
        int nl = s.IndexOfAny(['\n', '\r']);
        return nl < 0 ? s : s[..nl];
    }

    /// <summary>The words on a button, check box, tab or menu.</summary>
    static string? TextOf(Control c) => c switch
    {
        ContentControl { Content: string s } => s,
        ContentControl { Content: TextBlock tb } => tb.Text,
        ContentControl { Content: Control inner } when c is Button or ToggleButton or CheckBox or RadioButton =>
            inner.GetLogicalDescendants().OfType<TextBlock>().FirstOrDefault(t => !string.IsNullOrWhiteSpace(t.Text))?.Text,
        TabItem { Header: string h } => h,
        _ => null,
    };

    /// <summary>
    /// A setting's name: the label beside or above it (<see cref="UiKit.Field"/>, <see cref="UiKit.Labelled"/>),
    /// a text in the small row or stack the control is in. Not one of a template's own parts (a watermark).
    /// </summary>
    static string? FieldLabel(Control c)
    {
        if (c.Parent is not Panel p || p.Children.Count is < 2 or > 3) return null;
        return p.Children.OfType<TextBlock>().FirstOrDefault(t => t != c && t.TemplatedParent is null && !string.IsNullOrWhiteSpace(t.Text))?.Text;
    }

    /// <summary>What a design's shapes are, for the help: the add-ons that made them (by their notes on them), or what kind of shape.</summary>
    /// <param name="plain">The page for plain shapes (default: the canvas's right-click menu).</param>
    public static HelpSubject Shapes(IReadOnlyCollection<Shape> shapes, string plain = "canvas.md#the-right-click-menu")
    {
        if (shapes.Count == 0) return new HelpSubject { Exact = true, Page = "canvas.md", Targets = ["canvas"] };
        var running = _main?.Addons.Entries.Where(e => e.IsOn).Select(e => e.Id).ToList() ?? [];
        // An add-on's notes start with its id: "walls.id", "grain.map" (or are just the id).
        var owners = shapes.SelectMany(s => s.AddonData.Keys)
            .Select(k => running.Where(id => k.Equals(id, StringComparison.OrdinalIgnoreCase) || k.StartsWith(id + ".", StringComparison.OrdinalIgnoreCase)).MaxBy(id => id.Length))
            .OfType<string>().GroupBy(id => id, StringComparer.OrdinalIgnoreCase).OrderByDescending(g => g.Count()).Select(g => g.Key).ToList();
        if (owners.Count > 0) return new HelpSubject { Exact = true, Addons = owners };
        string page = shapes.All(s => s is ImageShape) ? "pictures.md"
            : shapes.All(s => s is TextShape) ? "drawing.md#text"
            : shapes.All(s => s is CodeShape) ? "drawing.md#codes"
            : plain;
        return new HelpSubject { Exact = true, Page = page, Targets = ["canvas"] };
    }

    // ---- the menu items -----------------------------------------------------------------

    /// <summary>Take out the items added last time, and add them again for what was clicked now.</summary>
    static void Fill(ItemCollection items, Control target)
    {
        foreach (var old in items.OfType<Control>().Where(i => Mark.Equals(i.Tag)).ToList()) items.Remove(old);
        if (_main is not { } main) return;
        List<Control> mine;
        try { mine = Items(main, target); }
        catch (Exception e)
        {
            AppLog.Error("Right-click help", e);
            return;
        }
        if (items.Count > 0 && items[^1] is not Separator) mine.Insert(0, new Separator { Tag = Mark });
        foreach (var m in mine) items.Add(m);
    }

    static List<Control> Items(MainWindow main, Control target)
    {
        var (query, addons) = Resolve(target);
        var lib = HelpFiles.Library;
        var page = HelpFinder.Page(lib, query);
        var tutorial = HelpFinder.Tutorial(lib, query, page);
        var list = new List<Control>();

        if (addons.Count == 0)
            list.Add(new MenuItem { Header = "Disable add-on (this is part of PhotonCanvas)", IsEnabled = false, Tag = Mark });
        foreach (var a in addons)
        {
            var item = new MenuItem { Header = $"Disable add-on: {a.Manifest.Name}", Tag = Mark };
            var id = a.Id;
            item.Click += (_, _) => Dispatcher.UIThread.Post(() => _ = ((IAddonServices)main).Guard(() => Disable(main, id)));
            list.Add(item);
        }

        var tut = new MenuItem { Header = tutorial is null ? "See tutorials…" : $"See tutorial: {Short(tutorial.Title)}", Tag = Mark };
        tut.Click += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            if (tutorial is null) main.ShowTutorials();
            else main.StartTutorial(tutorial.Id);
        });
        list.Add(tut);

        string helpTitle = page is { } pg && lib.Pages.TryGetValue(pg.File, out var p)
            ? (pg.Anchor.Length > 0 ? p.Blocks.OfType<Heading>().FirstOrDefault(h => h.Anchor == pg.Anchor)?.Text ?? p.Title : p.Title)
            : "";
        var help = new MenuItem { Header = page is null ? "See help" : $"See help: {Short(helpTitle)}", Tag = Mark };
        help.Click += (_, _) => Dispatcher.UIThread.Post(() => main.ShowHelp(page?.File, page is { Anchor.Length: > 0 } a ? a.Anchor : null));
        list.Add(help);
        return list;
    }

    static string Short(string s) => s.Length <= 48 ? s : s[..46].TrimEnd() + "…";

    /// <summary>Turn an add-on off (and the ones that need it, after asking), saying how to get it back.</summary>
    static async Task Disable(MainWindow main, string id)
    {
        if (main.Addons.Find(id) is not { IsOn: true } entry) return;
        var needing = NeededBy(main.Addons, id).Select(e => e.Manifest.Name).ToList();
        if (needing.Count > 0 && !await Dialogs.Confirm(main, "Disable add-on",
                $"{UiKit.ListWords(needing)} {(needing.Count == 1 ? "needs" : "need")} {entry.Manifest.Name}, so {(needing.Count == 1 ? "it goes" : "they go")} off too.\n\nYou can turn them back on in Settings → Add-ons.",
                "Turn them off", "Cancel"))
            return;
        main.Addons.Set(id, false);
        ((IAddonServices)main).Status(entry.IsOn
            ? $"{entry.Manifest.Name} couldn't be turned off."
            : $"{entry.Manifest.Name} is off. Turn it back on in Settings → Add-ons.");
    }

    /// <summary>The running add-ons that need this one, directly or through another.</summary>
    static List<AddonEntry> NeededBy(AddonManager addons, string id)
    {
        var found = new List<AddonEntry>();
        var queue = new Queue<string>([id]);
        while (queue.TryDequeue(out var next))
            foreach (var e in addons.Entries.Where(e => e.IsOn && e.Manifest.Requires.Contains(next, StringComparer.OrdinalIgnoreCase) && !found.Contains(e)))
            {
                found.Add(e);
                queue.Enqueue(e.Id);
            }
        return found;
    }
}
