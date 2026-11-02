using System.Text;
using System.Text.RegularExpressions;

namespace PhotonCanvas.Doc.Help;

/// <summary>Something written inline: plain, bold, italic, a key or control name, or a link.</summary>
public abstract record Inline;
public sealed record TextRun(string Text, bool Bold = false, bool Italic = false) : Inline;
/// <summary>`Like this`: a key, a button's or a field's name.</summary>
public sealed record CodeRun(string Text) : Inline;
/// <summary>[text](target): another page (page.md, page.md#section, #section), a tutorial (tutorial:id) or a web address.</summary>
public sealed record LinkRun(string Text, string Target) : Inline;

/// <summary>One block of a page.</summary>
public abstract record Block;
public sealed record Heading(int Level, string Text, string Anchor) : Block;
public sealed record Paragraph(IReadOnlyList<Inline> Inlines) : Block;
public sealed record ListBlock(bool Numbered, IReadOnlyList<ListItem> Items) : Block;
public sealed record ListItem(IReadOnlyList<Inline> Inlines, IReadOnlyList<ListItem> Children);
/// <summary>![caption](images/file.png) on a line of its own.</summary>
public sealed record Figure(string Caption, string Source) : Block;
/// <summary>&gt; **Tip:** … — Kind is "tip", "note", "warning" or "safety" ("note" when unmarked).</summary>
public sealed record Callout(string Kind, IReadOnlyList<Inline> Inlines) : Block;
public sealed record Table(IReadOnlyList<IReadOnlyList<IReadOnlyList<Inline>>> Rows) : Block;
public sealed record CodeBlock(string Text) : Block;
public sealed record Rule : Block;

/// <summary>A page of the guide.</summary>
public sealed record HelpPage(string File, string Title, IReadOnlyList<Block> Blocks)
{
    /// <summary>Window classes this page (or one of its sections) explains, for F1: type name → anchor ("" = the page).</summary>
    public IReadOnlyDictionary<string, string> Windows { get; init; } = new Dictionary<string, string>();

    /// <summary>The page's text, flattened, by section (for search).</summary>
    public IEnumerable<(string Anchor, string Heading, string Text)> Sections()
    {
        string anchor = "", heading = Title;
        var sb = new StringBuilder();
        foreach (var b in Blocks)
        {
            if (b is Heading { Level: >= 2 } h)
            {
                yield return (anchor, heading, sb.ToString());
                anchor = h.Anchor;
                heading = h.Text;
                sb.Clear();
                continue;
            }
            sb.Append(HelpText.Plain(b)).Append(' ');
        }
        yield return (anchor, heading, sb.ToString());
    }
}

/// <summary>One entry of the contents: a page, and the pages under it.</summary>
public sealed record ContentsEntry(string Title, string File, IReadOnlyList<ContentsEntry> Children);

/// <summary>A tutorial: a short guided walk through one feature, a step at a time.</summary>
public sealed record Tutorial(string Id, string Title, string Category, string Summary, IReadOnlyList<TutorialStep> Steps)
{
    /// <summary>Order within its category (lower first).</summary>
    public int Order { get; init; } = 100;
    /// <summary>A help page that explains the feature fully.</summary>
    public string? Page { get; init; }
}

/// <summary>
/// One step: what to do, said briefly. <see cref="Target"/> is what to point at (see
/// <see cref="TutorialTargets"/>), <see cref="Wait"/> what the step waits for before moving on by itself.
/// </summary>
public sealed record TutorialStep(string Title, IReadOnlyList<Block> Body, string? Target, string? Wait);

/// <summary>Plain text of blocks and inlines.</summary>
public static class HelpText
{
    public static string Plain(IEnumerable<Inline> inlines) => string.Concat(inlines.Select(i => i switch
    {
        TextRun t => t.Text,
        CodeRun c => c.Text,
        LinkRun l => l.Text,
        _ => "",
    }));

    public static string Plain(Block b) => b switch
    {
        Heading h => h.Text,
        Paragraph p => Plain(p.Inlines),
        ListBlock l => string.Join(" ", Items(l.Items).Select(i => Plain(i.Inlines))),
        Figure f => f.Caption,
        Callout c => Plain(c.Inlines),
        Table t => string.Join(" ", t.Rows.SelectMany(r => r).Select(Plain)),
        CodeBlock c => c.Text,
        _ => "",
    };

    static IEnumerable<ListItem> Items(IEnumerable<ListItem> items) => items.SelectMany(i => Items(i.Children).Prepend(i));

    /// <summary>Every link and image in some blocks.</summary>
    public static IEnumerable<string> Links(IEnumerable<Block> blocks) => blocks.SelectMany(b => b switch
    {
        Paragraph p => LinksIn(p.Inlines),
        ListBlock l => Items(l.Items).SelectMany(i => LinksIn(i.Inlines)),
        Callout c => LinksIn(c.Inlines),
        Table t => t.Rows.SelectMany(r => r).SelectMany(LinksIn),
        _ => [],
    });

    static IEnumerable<string> LinksIn(IEnumerable<Inline> inlines) => inlines.OfType<LinkRun>().Select(l => l.Target);
}

/// <summary>
/// Reads the guide's pages and tutorials: a small, fixed part of Markdown (see docs/help/AUTHORING.md),
/// so pages read the same on GitHub as in the program.
/// </summary>
public static partial class HelpMarkdown
{
    /// <summary>The anchor a heading gets, as GitHub makes it: lower case, spaces to dashes, punctuation dropped.</summary>
    public static string Anchor(string heading)
    {
        var sb = new StringBuilder();
        foreach (char c in heading.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c) || c == '-' || c == '_') sb.Append(c);
            else if (c == ' ') sb.Append('-');
        }
        return sb.ToString();
    }

    [GeneratedRegex(@"^<!--\s*([\w-]+)\s*:\s*(.*?)\s*-->\s*$")]
    private static partial Regex Directive();

    [GeneratedRegex(@"^!\[([^\]]*)\]\(([^)\s]+)\)\s*$")]
    private static partial Regex ImageLine();

    [GeneratedRegex(@"^(\s*)([-*]|\d+[.)])\s+(.*)$")]
    private static partial Regex ListLine();

    /// <summary>A page: its title (the first # heading), its blocks, and its window directives.</summary>
    public static HelpPage Page(string file, string text)
    {
        var (blocks, directives) = Parse(text);
        var title = blocks.OfType<Heading>().FirstOrDefault(h => h.Level == 1)?.Text ?? Path.GetFileNameWithoutExtension(file);
        var windows = new Dictionary<string, string>();
        foreach (var (key, value, anchor) in directives)
            if (key == "windows")
                foreach (var w in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    windows.TryAdd(w, anchor);
        return new HelpPage(file, title, blocks) { Windows = windows };
    }

    /// <summary>
    /// The contents: a list of links to pages, nested by indentation
    /// (<c>- [Title](page.md)</c>, two spaces more for the pages under it).
    /// </summary>
    public static IReadOnlyList<ContentsEntry> Contents(string text)
    {
        var (blocks, _) = Parse(text);
        static IReadOnlyList<ContentsEntry> Entries(IEnumerable<ListItem> items) =>
            [.. items.Select(i => i.Inlines.OfType<LinkRun>().FirstOrDefault() is { } l ? new ContentsEntry(l.Text, l.Target, Entries(i.Children)) : null).OfType<ContentsEntry>()];
        return blocks.OfType<ListBlock>().SelectMany(l => Entries(l.Items)).ToList();
    }

    /// <summary>
    /// A tutorial: # title, then <c>&lt;!-- category: … --&gt;</c>, <c>summary</c>, <c>order</c> and
    /// <c>page</c> directives, a short introduction, and a ## heading per step, each with optional
    /// <c>target</c> and <c>wait</c> directives.
    /// </summary>
    public static Tutorial Tutorial(string file, string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        string title = Path.GetFileNameWithoutExtension(file), category = "Other", summary = "", id = Path.GetFileNameWithoutExtension(file);
        string? page = null;
        int order = 100;
        var steps = new List<TutorialStep>();
        string? stepTitle = null, target = null, wait = null;
        var body = new List<string>();
        void Flush()
        {
            var (blocks, _) = Parse(string.Join("\n", body));
            if (stepTitle is not null) steps.Add(new TutorialStep(stepTitle, blocks, target, wait));
            else if (blocks.Count > 0) steps.Add(new TutorialStep(title, blocks, target, wait)); // the introduction: the first step
            body.Clear();
            target = wait = null;
        }
        foreach (var raw in lines)
        {
            var line = raw.TrimEnd();
            if (line.StartsWith("# ")) { title = line[2..].Trim(); continue; }
            if (Directive().Match(line) is { Success: true } d)
            {
                string key = d.Groups[1].Value.ToLowerInvariant(), value = d.Groups[2].Value;
                switch (key)
                {
                    case "category": category = value; break;
                    case "summary": summary = value; break;
                    case "id": id = value; break;
                    case "page": page = value; break;
                    case "order": int.TryParse(value, out order); break;
                    case "target": target = value; break;
                    case "wait": wait = value; break;
                }
                continue;
            }
            if (line.StartsWith("## "))
            {
                Flush();
                stepTitle = line[3..].Trim();
                continue;
            }
            body.Add(line);
        }
        Flush();
        return new Tutorial(id, title, category, summary, steps) { Order = order, Page = page };
    }

    /// <summary>The blocks of some text, and its directives (key, value, the anchor of the section they're in).</summary>
    public static (List<Block> Blocks, List<(string Key, string Value, string Anchor)> Directives) Parse(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var blocks = new List<Block>();
        var directives = new List<(string, string, string)>();
        string anchor = "";
        int i = 0;
        while (i < lines.Length)
        {
            var line = lines[i];
            var t = line.Trim();
            if (t.Length == 0) { i++; continue; }
            if (Directive().Match(t) is { Success: true } d)
            {
                directives.Add((d.Groups[1].Value.ToLowerInvariant(), d.Groups[2].Value, anchor));
                i++;
                continue;
            }
            if (t.StartsWith("<!--"))
            {
                // Any other comment: skipped, to its end.
                while (i < lines.Length && !lines[i].Contains("-->")) i++;
                i++;
                continue;
            }
            if (t.StartsWith('#'))
            {
                int level = t.TakeWhile(c => c == '#').Count();
                var text2 = t[level..].Trim();
                var h = new Heading(level, text2, Anchor(text2));
                if (level >= 2) anchor = h.Anchor;
                blocks.Add(h);
                i++;
                continue;
            }
            if (t.StartsWith("```"))
            {
                var sb = new StringBuilder();
                i++;
                while (i < lines.Length && !lines[i].Trim().StartsWith("```")) sb.AppendLine(lines[i++]);
                i++;
                blocks.Add(new CodeBlock(sb.ToString().TrimEnd()));
                continue;
            }
            if (t is "---" or "***")
            {
                blocks.Add(new Rule());
                i++;
                continue;
            }
            if (ImageLine().Match(t) is { Success: true } img)
            {
                blocks.Add(new Figure(img.Groups[1].Value, img.Groups[2].Value));
                i++;
                continue;
            }
            if (t.StartsWith('>'))
            {
                var sb = new StringBuilder();
                while (i < lines.Length && lines[i].TrimStart().StartsWith('>'))
                {
                    sb.Append(lines[i].TrimStart()[1..].Trim()).Append(' ');
                    i++;
                }
                blocks.Add(CalloutOf(sb.ToString().Trim()));
                continue;
            }
            if (t.StartsWith('|'))
            {
                var rows = new List<IReadOnlyList<IReadOnlyList<Inline>>>();
                while (i < lines.Length && lines[i].Trim().StartsWith('|'))
                {
                    var cells = lines[i].Trim().Trim('|').Split('|').Select(c => c.Trim()).ToList();
                    if (!cells.All(c => c.Length > 0 && c.All(ch => ch is '-' or ':')))
                        rows.Add([.. cells.Select(c => (IReadOnlyList<Inline>)Inlines(c))]);
                    i++;
                }
                blocks.Add(new Table(rows));
                continue;
            }
            if (ListLine().Match(line) is { Success: true } first)
            {
                var (list, next) = ParseList(lines, i, first.Groups[1].Value.Length);
                blocks.Add(list);
                i = next;
                continue;
            }
            // A paragraph: lines up to a blank one or another kind of block.
            var para = new StringBuilder(t);
            i++;
            while (i < lines.Length)
            {
                var n = lines[i].Trim();
                if (n.Length == 0 || n.StartsWith('#') || n.StartsWith('>') || n.StartsWith('|') || n.StartsWith("```") || n.StartsWith("<!--")
                    || ImageLine().IsMatch(n) || ListLine().IsMatch(lines[i])) break;
                para.Append(' ').Append(n);
                i++;
            }
            blocks.Add(new Paragraph(Inlines(para.ToString())));
        }
        return (blocks, directives);
    }

    static Callout CalloutOf(string text)
    {
        foreach (var kind in new[] { "Tip", "Note", "Warning", "Safety" })
        {
            var mark = $"**{kind}:**";
            if (text.StartsWith(mark, StringComparison.OrdinalIgnoreCase))
                return new Callout(kind.ToLowerInvariant(), Inlines(text[mark.Length..].Trim()));
        }
        return new Callout("note", Inlines(text));
    }

    static (ListBlock List, int Next) ParseList(string[] lines, int start, int indent)
    {
        var items = new List<ListItem>();
        bool numbered = char.IsDigit(ListLine().Match(lines[start]).Groups[2].Value[0]);
        int i = start;
        while (i < lines.Length)
        {
            var m = ListLine().Match(lines[i]);
            if (!m.Success)
            {
                // A wrapped line of the item above (indented text), or the end of the list.
                if (lines[i].Trim().Length > 0 && lines[i].Length - lines[i].TrimStart().Length > indent && items.Count > 0)
                {
                    var last = items[^1];
                    items[^1] = last with { Inlines = [.. last.Inlines, new TextRun(" "), .. Inlines(lines[i].Trim())] };
                    i++;
                    continue;
                }
                break;
            }
            int ind = m.Groups[1].Value.Length;
            if (ind < indent) break;
            if (ind > indent)
            {
                // Items under the one above.
                var (sub, next) = ParseList(lines, i, ind);
                if (items.Count > 0) items[^1] = items[^1] with { Children = [.. items[^1].Children, .. sub.Items] };
                i = next;
                continue;
            }
            // A different kind of marker (- after 1.) starts another list.
            if (char.IsDigit(m.Groups[2].Value[0]) != numbered) break;
            items.Add(new ListItem(Inlines(m.Groups[3].Value), []));
            i++;
        }
        return (new ListBlock(numbered, items), i);
    }

    /// <summary>The inlines of a line: **bold**, *italic*, `code`, [link](target), plain text.</summary>
    public static List<Inline> Inlines(string s)
    {
        var o = new List<Inline>();
        var plain = new StringBuilder();
        bool bold = false, italic = false;
        void Emit()
        {
            if (plain.Length > 0) o.Add(new TextRun(plain.ToString(), bold, italic));
            plain.Clear();
        }
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '\\' && i + 1 < s.Length) { plain.Append(s[++i]); continue; }
            if (c == '*' && i + 1 < s.Length && s[i + 1] == '*') { Emit(); bold = !bold; i++; continue; }
            if (c == '*' && (italic || i + 1 < s.Length && !char.IsWhiteSpace(s[i + 1]))) { Emit(); italic = !italic; continue; }
            if (c == '`')
            {
                int end = s.IndexOf('`', i + 1);
                if (end > i) { Emit(); o.Add(new CodeRun(s[(i + 1)..end])); i = end; continue; }
            }
            if (c == '[')
            {
                int close = s.IndexOf("](", i + 1, StringComparison.Ordinal);
                int end = close > 0 ? s.IndexOf(')', close + 2) : -1;
                if (close > i && end > close)
                {
                    Emit();
                    o.Add(new LinkRun(s[(i + 1)..close], s[(close + 2)..end]));
                    i = end;
                    continue;
                }
            }
            plain.Append(c);
        }
        Emit();
        return o;
    }
}

/// <summary>
/// What tutorial steps can point at, and wait for. Targets: <c>ribbon:Tab/Label</c>, <c>tab:Tab</c>,
/// <c>header:File|Save|Undo|Redo|Settings|Help</c>, <c>panel:Layers|Selection|Layer settings|Job|Machine</c>,
/// <c>canvas</c>, <c>field:Label</c>, <c>button:Text</c>, <c>tabitem:Header</c>, <c>text:Text</c>,
/// <c>window:Title</c>. Waits: <c>window:Title</c>, <c>closed:Title</c>, <c>selection</c>,
/// <c>selection:N</c>, <c>shapes:+N</c>, <c>tool:Name</c>, <c>tab:Name</c>, <c>layer-mode:Mode</c>,
/// <c>layers:+N</c>, <c>zoom</c>, <c>picture-selected</c>.
/// </summary>
public static class TutorialTargets
{
    public static readonly string[] TargetKinds = ["ribbon", "tab", "header", "panel", "canvas", "field", "button", "tabitem", "text", "window"];
    public static readonly string[] WaitKinds = ["window", "closed", "selection", "shapes", "tool", "tab", "layer-mode", "layers", "zoom", "picture-selected"];
    public static readonly string[] Headers = ["File", "Save", "Undo", "Redo", "Settings", "Help"];
    public static readonly string[] Panels = ["Layers", "Selection", "Layer settings", "Job", "Machine"];

    /// <summary>The kind and the rest of a target or wait ("ribbon:Home/Text" → ("ribbon", "Home/Text")).</summary>
    public static (string Kind, string Value) Split(string s)
    {
        int c = s.IndexOf(':');
        return c < 0 ? (s.Trim().ToLowerInvariant(), "") : (s[..c].Trim().ToLowerInvariant(), s[(c + 1)..].Trim());
    }
}

/// <summary>The guide on disk: its contents, pages and tutorials.</summary>
public sealed class HelpLibrary
{
    public string Folder { get; }
    public IReadOnlyList<ContentsEntry> Contents { get; }
    public IReadOnlyDictionary<string, HelpPage> Pages { get; }
    public IReadOnlyList<Tutorial> Tutorials { get; }

    /// <summary>Pages and tutorials that live in another folder (an add-on's help), by page file or tutorial id.</summary>
    readonly IReadOnlyDictionary<string, string> _folders;

    HelpLibrary(string folder, IReadOnlyList<ContentsEntry> contents, IReadOnlyDictionary<string, HelpPage> pages, IReadOnlyList<Tutorial> tutorials,
        IReadOnlyDictionary<string, string>? folders = null)
    {
        Folder = folder;
        Contents = contents;
        Pages = pages;
        Tutorials = tutorials;
        _folders = folders ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The folder a page's pictures are in (the guide's, or the add-on's whose page it is).</summary>
    public string FolderFor(string pageFile) => _folders.GetValueOrDefault(pageFile) ?? Folder;

    /// <summary>The folder a tutorial's pictures are in.</summary>
    public string FolderForTutorial(string tutorialId) => _folders.GetValueOrDefault("tutorial:" + tutorialId) ?? Folder;

    /// <summary>
    /// The guide with add-ons' help folders joined in: their pages (listed under "Add-ons" in the
    /// contents, by their own contents.md if they have one) and tutorials. A page whose file name is
    /// already taken is left out.
    /// </summary>
    public HelpLibrary WithAddons(IEnumerable<(string Name, string Folder)> addons)
    {
        var pages = new Dictionary<string, HelpPage>(Pages, StringComparer.OrdinalIgnoreCase);
        var folders = new Dictionary<string, string>(_folders, StringComparer.OrdinalIgnoreCase);
        var tutorials = Tutorials.ToList();
        var entries = new List<ContentsEntry>();
        foreach (var (name, folder) in addons.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var lib = Load(folder);
            var added = new List<HelpPage>();
            foreach (var page in lib.Pages.Values)
                if (pages.TryAdd(page.File, page))
                {
                    folders[page.File] = folder;
                    added.Add(page);
                }
            foreach (var tut in lib.Tutorials.Where(x => !tutorials.Any(have => have.Id == x.Id)))
            {
                tutorials.Add(tut);
                folders["tutorial:" + tut.Id] = folder;
            }
            if (added.Count == 0) continue;
            var children = lib.Contents.Count > 0 ? lib.Contents : [.. added.OrderBy(p => p.Title).Select(p => new ContentsEntry(p.Title, p.File, []))];
            entries.Add(children.Count == 1 ? children[0] : new ContentsEntry(name, children[0].File, children));
        }
        var contents = Contents.ToList();
        if (entries.Count > 0) contents.Add(new ContentsEntry("Add-ons", entries[0].File, entries));
        return new HelpLibrary(Folder, contents, pages,
            [.. tutorials.OrderBy(t => t.Category).ThenBy(t => t.Order).ThenBy(t => t.Title)], folders);
    }

    /// <summary>Read the guide from a folder holding contents.md, the pages, images/ and tutorials/.</summary>
    public static HelpLibrary Load(string folder)
    {
        var contentsFile = Path.Combine(folder, "contents.md");
        var contents = File.Exists(contentsFile) ? HelpMarkdown.Contents(File.ReadAllText(contentsFile)) : [];
        var pages = Directory.Exists(folder)
            ? Directory.GetFiles(folder, "*.md").Where(f => !Path.GetFileName(f).Equals("contents.md", StringComparison.OrdinalIgnoreCase) && !Path.GetFileName(f).Equals("AUTHORING.md", StringComparison.OrdinalIgnoreCase))
                .Select(f => HelpMarkdown.Page(Path.GetFileName(f), File.ReadAllText(f))).ToDictionary(p => p.File, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, HelpPage>(StringComparer.OrdinalIgnoreCase);
        var tutorialsDir = Path.Combine(folder, "tutorials");
        var tutorials = Directory.Exists(tutorialsDir)
            ? Directory.GetFiles(tutorialsDir, "*.md").Select(f => HelpMarkdown.Tutorial(Path.GetFileName(f), File.ReadAllText(f)))
                .OrderBy(t => t.Category).ThenBy(t => t.Order).ThenBy(t => t.Title).ToList()
            : [];
        return new HelpLibrary(folder, contents, pages, tutorials);
    }

    /// <summary>The page and section for a window class (F1 in that window), if the guide explains it.</summary>
    public (string File, string Anchor)? ForWindow(string typeName)
    {
        foreach (var p in Pages.Values)
            if (p.Windows.TryGetValue(typeName, out var anchor)) return (p.File, anchor);
        return null;
    }

    /// <summary>Sections matching every word of <paramref name="query"/>, best first (title, then heading, then text).</summary>
    public IReadOnlyList<(HelpPage Page, string Anchor, string Heading, string Snippet)> Search(string query, int max = 60)
    {
        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0) return [];
        var hits = new List<(int Score, HelpPage Page, string Anchor, string Heading, string Snippet)>();
        foreach (var page in Pages.Values)
            foreach (var (anchor, heading, text) in page.Sections())
            {
                int score = 0;
                bool all = true;
                foreach (var w in words)
                {
                    bool inHeading = heading.Contains(w, StringComparison.OrdinalIgnoreCase), inText = text.Contains(w, StringComparison.OrdinalIgnoreCase);
                    if (!inHeading && !inText) { all = false; break; }
                    score += (inHeading ? 10 : 0) + (inText ? 1 : 0) + (page.Title.Contains(w, StringComparison.OrdinalIgnoreCase) ? 3 : 0);
                }
                if (all) hits.Add((score, page, anchor, heading, Snippet(text, words[0])));
            }
        return [.. hits.OrderByDescending(h => h.Score).ThenBy(h => h.Page.Title).Take(max).Select(h => (h.Page, h.Anchor, h.Heading, h.Snippet))];
    }

    static string Snippet(string text, string word)
    {
        int at = text.IndexOf(word, StringComparison.OrdinalIgnoreCase);
        if (at < 0) return text.Length > 140 ? text[..140] + "…" : text;
        int from = Math.Max(0, at - 50);
        var s = text.Substring(from, Math.Min(140, text.Length - from));
        return (from > 0 ? "…" : "") + s.Trim() + (from + s.Length < text.Length ? "…" : "");
    }
}
