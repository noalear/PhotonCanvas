namespace PhotonCanvas.Doc.Help;

/// <summary>
/// What a right-click was on, as the guide can be searched for it: the labels shown on and around it
/// (most specific first), its ribbon command, the window it's in, the add-on that made it, and an
/// explicit page or tutorial when the program says exactly which.
/// </summary>
public sealed record HelpQuery
{
    /// <summary>Words shown on or beside the thing: a button's text, a setting's name, a tooltip. Most specific first.</summary>
    public IReadOnlyList<string> Labels { get; init; } = [];
    /// <summary>"Tab/Label" when it's a ribbon button.</summary>
    public string? Ribbon { get; init; }
    /// <summary>The class name of the window it's in (as in &lt;!-- windows: … --&gt;), null for the main window.</summary>
    public string? Window { get; init; }
    /// <summary>The help folder of the add-on that made it: its pages and tutorials come first.</summary>
    public string? Folder { get; init; }
    /// <summary>A page the program names outright ("layers.md#a-layers-card").</summary>
    public string? Page { get; init; }
    /// <summary>The part of the program it's in ("layers.md"): its page when nothing in it says more.</summary>
    public string? Area { get; init; }
    /// <summary>A tutorial the program names outright (its id).</summary>
    public string? Tutorial { get; init; }
    /// <summary>Tutorial step targets that point at it ("panel:Layers", "canvas").</summary>
    public IReadOnlyList<string> Targets { get; init; } = [];
}

/// <summary>
/// Finds the guide's page (and section) and the tutorial for something right-clicked
/// (<see cref="HelpQuery"/>): a named page or tutorial; else the section that names its label in bold
/// (as every ribbon button and setting is named, see docs/help/AUTHORING.md); else the page of its
/// window; else its add-on's own page. Tutorials likewise: one with a step pointing at it, else one
/// about the page found, else its add-on's.
/// </summary>
public static class HelpFinder
{
    /// <summary>Labels too common to say anything about where they are.</summary>
    static readonly HashSet<string> Common = new(StringComparer.OrdinalIgnoreCase)
    {
        "OK", "Cancel", "Close", "Apply", "Yes", "No", "Next", "Back", "Done", "Save", "Open", "Delete", "Remove", "Add",
        "Help", "More", "Less", "Show", "Hide", "On", "Off", "All", "None", "Reset", "Clear", "Edit", "Name", "Browse",
        "Settings", "Options", "Preview", "Start", "Stop", "Undo", "Redo",
    };

    /// <summary>A label as written in the guide: without trailing dots, colons and spaces.</summary>
    public static string Clean(string label) => label.Trim().TrimEnd(':', '…', '.').Trim();

    static bool Useful(string label) => label.Length >= 2 && label.Length <= 80 && !Common.Contains(label) && label.Any(char.IsLetter);

    /// <summary>The pages that came from an add-on's help folder.</summary>
    public static IEnumerable<HelpPage> PagesIn(HelpLibrary lib, string folder) =>
        lib.Pages.Values.Where(p => SameFolder(lib.FolderFor(p.File), folder));

    /// <summary>The tutorials that came from an add-on's help folder.</summary>
    public static IEnumerable<Tutorial> TutorialsIn(HelpLibrary lib, string folder) =>
        lib.Tutorials.Where(t => SameFolder(lib.FolderForTutorial(t.Id), folder)).OrderBy(t => t.Order).ThenBy(t => t.Title);

    static bool SameFolder(string a, string b) =>
        string.Equals(Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);

    /// <summary>An add-on's main page: the first of its pages in the contents, else the first by title.</summary>
    public static HelpPage? MainPage(HelpLibrary lib, string folder)
    {
        var mine = PagesIn(lib, folder).ToDictionary(p => p.File, StringComparer.OrdinalIgnoreCase);
        if (mine.Count == 0) return null;
        foreach (var e in Flatten(lib.Contents))
            if (mine.TryGetValue(e.File, out var p)) return p;
        return mine.Values.OrderBy(p => p.Title, StringComparer.CurrentCultureIgnoreCase).First();
    }

    static IEnumerable<ContentsEntry> Flatten(IEnumerable<ContentsEntry> entries) => entries.SelectMany(e => Flatten(e.Children).Prepend(e));

    /// <summary>The page and section ("" = the top) that explain it, or null when the guide has nothing better than its contents.</summary>
    public static (string File, string Anchor)? Page(HelpLibrary lib, HelpQuery q)
    {
        if (q.Page is { Length: > 0 } named && Split(named) is var (nf, na) && lib.Pages.TryGetValue(nf, out var np))
            return (np.File, np.Blocks.OfType<Heading>().Any(h => h.Anchor == na) ? na : "");

        var window = q.Window is { Length: > 0 } w ? lib.ForWindow(w) : null;
        var folderPages = q.Folder is { } f ? PagesIn(lib, f).Select(p => p.File).ToHashSet(StringComparer.OrdinalIgnoreCase) : [];

        // The section naming it, in the right place: on its window's page or its add-on's pages (or,
        // for the main window, anywhere).
        var labels = q.Labels.Select(Clean).Where(Useful).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (q.Ribbon is { } rb && rb.IndexOf('/') is var slash and > 0) labels.Insert(0, Clean(rb[(slash + 1)..]));
        // Its add-on's own pages first (another add-on may have a section called "Walls" too).
        foreach (bool ownOnly in folderPages.Count > 0 ? new[] { true, false } : [false])
        {
            foreach (var label in labels)
            {
                (int Score, string File, string Anchor)? best = null;
                foreach (var page in lib.Pages.Values)
                {
                    bool inFolder = folderPages.Contains(page.File), onWindowPage = window is { } wp && wp.File.Equals(page.File, StringComparison.OrdinalIgnoreCase);
                    if (ownOnly && !inFolder) continue;
                    if (q.Window is not null && window is not null && !inFolder && !onWindowPage) continue;
                    if (q.Window is not null && window is null && folderPages.Count > 0 && !inFolder) continue;
                    foreach (var (anchor, score) in Mentions(page, label))
                    {
                        int s = score + (inFolder ? 4 : 0) + (onWindowPage ? 3 : 0) + (onWindowPage && window!.Value.Anchor == anchor ? 1 : 0);
                        if (best is null || s > best.Value.Score) best = (s, page.File, anchor);
                    }
                }
                if (best is { } b) return (b.File, b.Anchor);
            }
        }

        if (window is { } found) return found;
        if (q.Area is { Length: > 0 } area && Split(area) is var (af, aa) && lib.Pages.TryGetValue(af, out var ap))
            return (ap.File, ap.Blocks.OfType<Heading>().Any(h => h.Anchor == aa) ? aa : "");
        if (q.Folder is { } folder && MainPage(lib, folder) is { } main) return (main.File, "");
        return null;
    }

    /// <summary>The tutorial that teaches it, or null.</summary>
    public static Tutorial? Tutorial(HelpLibrary lib, HelpQuery q, (string File, string Anchor)? page = null)
    {
        if (q.Tutorial is { Length: > 0 } id && lib.Tutorials.FirstOrDefault(t => t.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) is { } nt) return nt;
        page ??= Page(lib, q);
        var mine = q.Folder is { } f ? TutorialsIn(lib, f).Select(t => t.Id).ToHashSet(StringComparer.OrdinalIgnoreCase) : [];
        string? pageFile = page?.File;

        // A step pointing at it. Its ribbon button anywhere; its other names only in its add-on's
        // tutorials or ones about the page it's explained on (a "Generate" button is in many windows).
        Tutorial? Pointing(List<(string Target, bool Anywhere)> targets)
        {
            (int Score, Tutorial T)? best = null;
            for (int ti = 0; ti < targets.Count && best is null; ti++)
            {
                var (target, anywhere) = targets[ti];
                foreach (var t in lib.Tutorials)
                {
                    bool ours = mine.Contains(t.Id), aboutPage = pageFile is not null && Split(t.Page ?? "") is var (tf, _) && tf.Equals(pageFile, StringComparison.OrdinalIgnoreCase);
                    if (!anywhere && !ours && !aboutPage) continue;
                    int step = t.Steps.ToList().FindIndex(s => s.Target is { } st && SameTarget(st, target));
                    if (step < 0) continue;
                    int targeted = t.Steps.Take(step).Count(s => s.Target is not null);
                    // A tutorial of its own add-on, about its page, that starts with it.
                    int s = (ours ? 20 : 0) + (aboutPage ? 10 : 0) + Math.Max(0, 5 - targeted);
                    if (best is null || s > best.Value.Score) best = (s, t);
                }
            }
            return best?.T;
        }
        var named = new List<(string, bool)>();
        if (q.Ribbon is { } rb) named.Add(("ribbon:" + rb, true));
        foreach (var l in q.Labels.Select(Clean).Where(Useful).Distinct(StringComparer.OrdinalIgnoreCase))
            foreach (var kind in new[] { "button", "field", "tabitem", "header", "panel" })
                named.Add(($"{kind}:{l}", q.Window is null));
        if (Pointing(named) is { } byName) return byName;

        // One about the page (the very section first).
        if (page is { } pg)
        {
            var about = lib.Tutorials.Where(t => Split(t.Page ?? "") is var (tf, _) && tf.Equals(pg.File, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(t => Split(t.Page!).Anchor == pg.Anchor && pg.Anchor.Length > 0)
                .ThenByDescending(t => mine.Contains(t.Id)).ThenBy(t => t.Order).FirstOrDefault();
            if (about is not null) return about;
        }
        if (q.Folder is { } folder && TutorialsIn(lib, folder).FirstOrDefault() is { } own) return own;
        // Last: one pointing at the part of the program it's in.
        return Pointing([.. q.Targets.Select(t => (t, true))]);
    }

    static (string File, string Anchor) Split(string target)
    {
        int hash = target.IndexOf('#');
        return hash < 0 ? (target, "") : (target[..hash], target[(hash + 1)..]);
    }

    static bool SameTarget(string a, string b)
    {
        var (ka, va) = TutorialTargets.Split(a);
        var (kb, vb) = TutorialTargets.Split(b);
        return ka.Equals(kb, StringComparison.OrdinalIgnoreCase) && Clean(va).Equals(Clean(vb), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The sections of a page that name a label: as their heading (best) or in bold or as `code`.</summary>
    static IEnumerable<(string Anchor, int Score)> Mentions(HelpPage page, string label)
    {
        string anchor = "";
        bool Named(IEnumerable<Inline> inlines) => inlines.Any(i => i switch
        {
            TextRun { Bold: true } t => Clean(t.Text).Equals(label, StringComparison.OrdinalIgnoreCase),
            CodeRun c => Clean(c.Text).Equals(label, StringComparison.OrdinalIgnoreCase),
            _ => false,
        });
        static IEnumerable<ListItem> Items(IEnumerable<ListItem> items) => items.SelectMany(i => Items(i.Children).Prepend(i));
        if (Clean(page.Title).Equals(label, StringComparison.OrdinalIgnoreCase)) yield return ("", 8);
        var seen = new HashSet<string>();
        foreach (var b in page.Blocks)
        {
            if (b is Heading h)
            {
                if (h.Level >= 2) anchor = h.Anchor;
                if (h.Level >= 2 && Clean(h.Text).Equals(label, StringComparison.OrdinalIgnoreCase)) yield return (anchor, 7);
                continue;
            }
            bool named = b switch
            {
                Paragraph p => Named(p.Inlines),
                ListBlock l => Items(l.Items).Any(i => Named(i.Inlines)),
                Callout c => Named(c.Inlines),
                Table t => t.Rows.SelectMany(r => r).Any(Named),
                _ => false,
            };
            // The first section that names it is the one about it; later ones only refer to it.
            if (named && seen.Add(anchor)) yield return (anchor, seen.Count == 1 ? 3 : 1);
        }
    }
}
