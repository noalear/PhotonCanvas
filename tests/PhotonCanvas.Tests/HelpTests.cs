using System.Text.RegularExpressions;
using PhotonCanvas.Doc.Help;
using Xunit;

namespace PhotonCanvas.Tests;

/// <summary>
/// The help guide and the tutorials (docs/help) against the program: links and pictures exist, tutorial
/// steps point at things that exist, and every ribbon button, layer setting and window is in the guide
/// (so a new feature can't be added without its page and tutorial). tools/helpcheck.py does the same
/// without building, with a report of what's missing.
/// </summary>
public partial class HelpTests
{
    static readonly string Root = FindRoot();
    static readonly string HelpDir = Path.Combine(Root, "docs", "help");
    static readonly string AppDir = Path.Combine(Root, "src", "PhotonCanvas.App");
    static readonly Lazy<HelpLibrary> Lib = new(() => HelpLibrary.Load(HelpDir));
    static readonly Lazy<Dictionary<string, string>> Sources = new(() =>
        Directory.GetFiles(AppDir, "*.cs").ToDictionary(f => Path.GetFileName(f), File.ReadAllText));

    static string FindRoot()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "PhotonCanvas.slnx"))) return d.FullName;
        throw new InvalidOperationException("The source folder (PhotonCanvas.slnx) wasn't found above the tests.");
    }

    /// <summary>"Tab/Label" of every ribbon button, from MainWindow's ribbon.</summary>
    static HashSet<string> RibbonCommands()
    {
        var text = Sources.Value["MainWindow.axaml.cs"];
        var tabs = Regex.Matches(text, @"var (\w+) = _ribbon\.Tab\(""([^""]+)""").ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value);
        var groups = Regex.Matches(text, @"var (\w+) = (\w+)\.Group\(""([^""]+)""").Where(m => tabs.ContainsKey(m.Groups[2].Value))
            .ToDictionary(m => m.Groups[1].Value, m => tabs[m.Groups[2].Value]);
        var o = new HashSet<string>();
        foreach (Match m in Regex.Matches(text, @"\b(\w+)\.(?:Large|Small|LargeToggle|SmallToggle)\(""([^""]+)"""))
            if (groups.TryGetValue(m.Groups[1].Value, out var tab)) o.Add($"{tab}/{m.Groups[2].Value}");
        foreach (Match m in Regex.Matches(text, @"Tool(?:Large|Small)\((\w+), ""([^""]+)"""))
            if (groups.TryGetValue(m.Groups[1].Value, out var tab)) o.Add($"{tab}/{m.Groups[2].Value}");
        // The program's own tests: listed in the Tests window, reached as "Tests/Label".
        foreach (Match m in Regex.Matches(Sources.Value["MainWindow.axaml.cs"], @"\bAddTest\(""[^""]+"", ""([^""]+)"""))
            o.Add($"Tests/{m.Groups[1].Value}");
        foreach (Match m in Regex.Matches(text, @"(\w+)\.Icons\(\d+,(.*?)\);", RegexOptions.Singleline))
            if (groups.TryGetValue(m.Groups[1].Value, out var tab))
                foreach (Match t in Regex.Matches(m.Groups[2].Value, @"\(""Ic\w+"", ""([^""]+)"""))
                    o.Add($"{tab}/{t.Groups[1].Value}");
        return o;
    }

    static string AllPagesRaw() => string.Join("\n", Directory.GetFiles(HelpDir, "*.md").Where(f => !f.EndsWith("AUTHORING.md")).Select(File.ReadAllText));

    static string AllTutorialsRaw() => Directory.Exists(Path.Combine(HelpDir, "tutorials"))
        ? string.Join("\n", Directory.GetFiles(Path.Combine(HelpDir, "tutorials"), "*.md").Select(File.ReadAllText)) : "";

    static bool Documented(string raw, string label) => raw.Contains($"**{label}**") || raw.Contains($"`{label}`");

    [Fact]
    public void TheMarkdownIsReadAsWritten()
    {
        var (blocks, dirs) = HelpMarkdown.Parse("""
            # Title
            <!-- windows: CutTestWindow -->
            Some **bold**, *italic*, `Ctrl+S` and a [link](page.md#a-section).

            ## A section
            - one
              - under it
            - two
            1. first

            ![A caption](images/x.png)

            > **Tip:** do this.

            | Name | What |
            |---|---|
            | **A** | it |
            """);
        Assert.Equal(["Heading", "Paragraph", "Heading", "ListBlock", "ListBlock", "Figure", "Callout", "Table"], blocks.Select(b => b.GetType().Name));
        var p = (Paragraph)blocks[1];
        Assert.Contains(p.Inlines, i => i is TextRun { Bold: true, Text: "bold" });
        Assert.Contains(p.Inlines, i => i is TextRun { Italic: true, Text: "italic" });
        Assert.Contains(p.Inlines, i => i is CodeRun { Text: "Ctrl+S" });
        Assert.Contains(p.Inlines, i => i is LinkRun { Text: "link", Target: "page.md#a-section" });
        Assert.Equal("a-section", ((Heading)blocks[2]).Anchor);
        Assert.Single(((ListBlock)blocks[3]).Items[0].Children);
        Assert.Equal("tip", ((Callout)blocks[6]).Kind);
        Assert.Equal(2, ((Table)blocks[7]).Rows.Count);
        Assert.Equal(("windows", "CutTestWindow", ""), dirs.Single());
        Assert.Equal("spread-the-heat", HelpMarkdown.Anchor("Spread the heat"));
        Assert.Equal("keep-under-c", HelpMarkdown.Anchor("Keep under +°C"));
    }

    [Fact]
    public void TutorialsAreReadStepByStep()
    {
        var t = HelpMarkdown.Tutorial("mat-cut-test.md", """
            # Cut test
            <!-- category: Materials and tests -->
            <!-- summary: Find the cut. -->
            <!-- page: materials-and-tests.md#cut-test -->
            Why it's useful.

            ## Open it
            <!-- target: ribbon:Tests/Cut test -->
            <!-- wait: window:Cut test -->
            Click **Cut test**.

            ## Set the speeds
            <!-- target: field:Speeds -->
            Type them.
            """);
        Assert.Equal(("mat-cut-test", "Cut test", "Materials and tests", "Find the cut."), (t.Id, t.Title, t.Category, t.Summary));
        Assert.Equal("materials-and-tests.md#cut-test", t.Page);
        Assert.Equal(["Cut test", "Open it", "Set the speeds"], t.Steps.Select(s => s.Title));
        Assert.Equal(("ribbon:Tests/Cut test", "window:Cut test"), (t.Steps[1].Target, t.Steps[1].Wait));
        Assert.Null(t.Steps[0].Target);
    }

    [Fact]
    public void EveryLinkAndPictureExists()
    {
        var lib = Lib.Value;
        var problems = new List<string>();
        var tutorials = lib.Tutorials.Select(t => t.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        void Check(string where, string target, string page)
        {
            if (target.StartsWith("http")) return;
            if (target.StartsWith("tutorial:")) { if (!tutorials.Contains(target[9..])) problems.Add($"{where}: no tutorial {target}"); return; }
            int hash = target.IndexOf('#');
            string file = hash < 0 ? target : target[..hash], anchor = hash < 0 ? "" : target[(hash + 1)..];
            if (file.Length == 0) file = page;
            if (!lib.Pages.TryGetValue(Path.GetFileName(file), out var p)) { problems.Add($"{where}: no page {target}"); return; }
            if (anchor.Length > 0 && !p.Blocks.OfType<Heading>().Any(h => h.Anchor == anchor)) problems.Add($"{where}: no section {target}");
        }
        foreach (var e in Flatten(lib.Contents)) Check("contents.md", e.File, "");
        foreach (var page in lib.Pages.Values)
        {
            foreach (var link in HelpText.Links(page.Blocks)) Check(page.File, link, page.File);
            foreach (var fig in page.Blocks.OfType<Figure>())
                if (!File.Exists(Path.Combine(HelpDir, fig.Source))) problems.Add($"{page.File}: no picture {fig.Source}");
        }
        foreach (var t in lib.Tutorials)
        {
            if (t.Page is { } pg) Check($"tutorial {t.Id}", pg, "");
            foreach (var s in t.Steps) foreach (var link in HelpText.Links(s.Body)) Check($"tutorial {t.Id}", link, "");
        }
        // Every page is in the contents.
        var listed = Flatten(lib.Contents).Select(e => e.File).ToHashSet(StringComparer.OrdinalIgnoreCase);
        problems.AddRange(lib.Pages.Keys.Where(p => !listed.Contains(p)).Select(p => $"{p} isn't in contents.md"));
        // Every picture is shown somewhere (a retaken screenshot nothing uses is left behind otherwise).
        var shown = lib.Pages.Values.SelectMany(p => p.Blocks.OfType<Figure>()).Select(f => Path.GetFileName(f.Source)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        problems.AddRange(Directory.GetFiles(Path.Combine(HelpDir, "images")).Select(Path.GetFileName).Where(f => !shown.Contains(f!)).Select(f => $"images/{f} isn't on any page"));
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    static IEnumerable<ContentsEntry> Flatten(IEnumerable<ContentsEntry> entries) => entries.SelectMany(e => Flatten(e.Children).Prepend(e));

    static readonly HashSet<string> Categories =
    [
        "Getting started", "The workspace", "Drawing", "Editing shapes", "Arranging", "Importing and exporting", "Layers and settings",
        "Pictures", "Camera", "Materials and tests", "Running jobs", "Work area", "Machine and settings", "3D and slices",
    ];

    [Fact]
    public void EveryTutorialStepPointsAtSomethingThatExists()
    {
        var commands = RibbonCommands();
        var tabs = commands.Select(c => c[..c.IndexOf('/')]).ToHashSet();
        var literals = Sources.Value.Values.SelectMany(t => Regex.Matches(t, @"\$?""((?:[^""\\]|\\.)*)""").Select(m => m.Groups[1].Value)).ToHashSet();
        // And the labels written in XAML.
        foreach (var x in Directory.GetFiles(AppDir, "*.axaml"))
            foreach (Match m in Regex.Matches(File.ReadAllText(x), @"(?:Text|Content|Header|Watermark|Title)=""([^""]*)"""))
                literals.Add(m.Groups[1].Value);
        string[] tools = ["select", "rect", "ellipse", "polygon", "star", "line", "pen", "text", "nodes", "pan", "wand", "magnetic", "measure", "trim", "align2", "scissors", "tabs", "start", "eraser", "arc", "centreline"];
        string[] modes = ["cut", "score", "fill", "image"];
        var problems = new List<string>();
        foreach (var t in Lib.Value.Tutorials)
        {
            if (!Categories.Contains(t.Category)) problems.Add($"{t.Id}: unknown category '{t.Category}'");
            if (t.Summary.Length == 0) problems.Add($"{t.Id}: no summary");
            if (t.Steps.Count < 3) problems.Add($"{t.Id}: fewer than 2 steps after the introduction");
            foreach (var s in t.Steps)
            {
                if (s.Target is { } target)
                {
                    var (kind, value) = TutorialTargets.Split(target);
                    bool ok = kind switch
                    {
                        "ribbon" => commands.Contains(value),
                        "tab" => tabs.Contains(value),
                        "header" => TutorialTargets.Headers.Contains(value, StringComparer.OrdinalIgnoreCase),
                        "panel" => TutorialTargets.Panels.Contains(value, StringComparer.OrdinalIgnoreCase),
                        "canvas" => true,
                        "field" or "button" or "tabitem" or "window" => literals.Any(l => string.Equals(l.Trim().TrimEnd(':', '…'), value, StringComparison.OrdinalIgnoreCase) || l.StartsWith(value, StringComparison.OrdinalIgnoreCase)),
                        "text" => literals.Any(l => l.Contains(value, StringComparison.OrdinalIgnoreCase)),
                        _ => false,
                    };
                    if (!ok) problems.Add($"{t.Id} / {s.Title}: target '{target}' doesn't exist");
                }
                if (s.Wait is { } wait)
                {
                    var (kind, value) = TutorialTargets.Split(wait);
                    bool ok = kind switch
                    {
                        "tool" => tools.Contains(value.ToLowerInvariant()),
                        "layer-mode" => modes.Contains(value.ToLowerInvariant()),
                        "tab" => tabs.Contains(value),
                        _ => TutorialTargets.WaitKinds.Contains(kind),
                    };
                    if (!ok) problems.Add($"{t.Id} / {s.Title}: wait '{wait}' isn't one the program knows");
                }
            }
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void EveryRibbonButtonIsExplainedAndHasATutorial()
    {
        var raw = AllPagesRaw();
        var tutorials = AllTutorialsRaw();
        var commands = RibbonCommands();
        Assert.True(commands.Count > 100, $"only {commands.Count} ribbon buttons found: has the ribbon's code changed shape?");
        var unexplained = commands.Where(c => !Documented(raw, c[(c.IndexOf('/') + 1)..])).Order().ToList();
        var untaught = commands.Where(c => !tutorials.Contains($"ribbon:{c}")).Order().ToList();
        Assert.True(unexplained.Count == 0, "Ribbon buttons not in the help guide (as **Label**):\n" + string.Join("\n", unexplained));
        Assert.True(untaught.Count == 0, "Ribbon buttons no tutorial points at (ribbon:Tab/Label):\n" + string.Join("\n", untaught));
    }

    [Fact]
    public void EveryLayerSettingIsExplained()
    {
        var editor = Sources.Value["LayerEditor.cs"];
        var labels = Regex.Matches(editor, @"(?:Field|Labelled)\(""([^""]+)""").Select(m => m.Groups[1].Value).ToHashSet();
        var raw = AllPagesRaw();
        var missing = labels.Where(l => !Documented(raw, l)).Order().ToList();
        Assert.True(missing.Count == 0, "Layer settings not in the help guide (as **Label**):\n" + string.Join("\n", missing));
    }

    [Fact]
    public void EveryWindowHasItsPage()
    {
        var windows = Sources.Value.Values.SelectMany(t => Regex.Matches(t, @"class (\w+) : Window\b").Select(m => m.Groups[1].Value))
            .Where(w => w != "MainWindow").ToHashSet();
        var claimed = Lib.Value.Pages.Values.SelectMany(p => p.Windows.Keys).ToHashSet();
        var missing = windows.Where(w => !claimed.Contains(w)).Order().ToList();
        Assert.True(missing.Count == 0, "Windows no help page claims (<!-- windows: Name -->), so F1 has nowhere to go:\n" + string.Join("\n", missing));
    }
}
