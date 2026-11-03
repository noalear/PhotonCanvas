using PhotonCanvas.Doc.Help;
using Xunit;

namespace PhotonCanvas.Tests;

/// <summary>
/// The right-click menu's See help and See tutorial (<see cref="HelpFinder"/>): every ribbon button,
/// the program's and each add-on's, leads to the section that explains it and to a tutorial that
/// shows it; a setting in a window to its window's page; a panel to its own page when nothing in it
/// is explained by itself.
/// </summary>
public partial class HelpTests
{
    [Fact]
    public void RightClickingARibbonButtonFindsItsSectionAndTutorial()
    {
        var lib = Lib.Value;
        var problems = new List<string>();
        foreach (var c in RibbonCommands().Order())
        {
            var q = new HelpQuery { Ribbon = c, Area = "workspace.md#the-ribbon" };
            string label = c[(c.IndexOf('/') + 1)..];
            if (HelpFinder.Page(lib, q) is not { } page) problems.Add($"{c}: no page");
            else if (!Names(lib.Pages[page.File], page.Anchor, label)) problems.Add($"{c}: {page.File}#{page.Anchor} doesn't name **{label}**");
            if (HelpFinder.Tutorial(lib, q) is not { } t) problems.Add($"{c}: no tutorial");
            else if (!t.Steps.Any(s => string.Equals(s.Target, "ribbon:" + c, StringComparison.OrdinalIgnoreCase))) problems.Add($"{c}: tutorial {t.Id} doesn't point at it");
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Theory]
    [MemberData(nameof(Addons))]
    public void RightClickingAnAddonsButtonFindsItsOwnHelp(string addon)
    {
        var help = Path.Combine(AddonsDir, addon, "help");
        var lib = Lib.Value.WithAddons(AddonNames().Where(a => Directory.Exists(Path.Combine(AddonsDir, a, "help"))).Select(a => (a, Path.Combine(AddonsDir, a, "help"))));
        var mine = HelpFinder.PagesIn(lib, help).Select(p => p.File).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var problems = new List<string>();
        foreach (var c in AddonRibbon(AddonSources(addon)).Order())
        {
            var q = new HelpQuery { Ribbon = c, Folder = help };
            string label = c[(c.IndexOf('/') + 1)..];
            if (HelpFinder.Page(lib, q) is not { } page) problems.Add($"{c}: no page");
            else if (!mine.Contains(page.File)) problems.Add($"{c}: {page.File} isn't one of {addon}'s pages");
            else if (!Names(lib.Pages[page.File], page.Anchor, label)) problems.Add($"{c}: {page.File}#{page.Anchor} doesn't name **{label}**");
            if (HelpFinder.Tutorial(lib, q) is not { } t || !t.Steps.Any(s => string.Equals(s.Target, "ribbon:" + c, StringComparison.OrdinalIgnoreCase)))
                problems.Add($"{c}: no tutorial pointing at it");
        }
        // Anywhere else in its windows, or a shape it made: its own page and tutorial at least.
        var anything = new HelpQuery { Folder = help, Labels = ["Something no page mentions"] };
        if (HelpFinder.Page(lib, anything) is not { } any || !mine.Contains(any.File)) problems.Add("no page of its own for things it made");
        if (HelpFinder.Tutorial(lib, anything) is not { } tut || !HelpFinder.TutorialsIn(lib, help).Contains(tut)) problems.Add("no tutorial of its own for things it made");
        Assert.True(problems.Count == 0, $"{addon}:\n" + string.Join("\n", problems));
    }

    [Fact]
    public void ASettingInAWindowLeadsToThatWindowsPage()
    {
        var lib = Lib.Value;
        // A setting the window's page names: that section.
        var named = HelpFinder.Page(lib, new HelpQuery { Window = "GridWindow", Labels = ["Something not in the guide", "Grid settings"] });
        Assert.Equal(("canvas.md", "grid-settings"), named);
        // A setting it doesn't: the window's own section, not some other page that happens to use the word.
        var plain = HelpFinder.Page(lib, new HelpQuery { Window = "GridWindow", Labels = ["Text"] });
        Assert.Equal(lib.ForWindow("GridWindow"), plain);
    }

    [Fact]
    public void APanelLeadsToItsPageWhenNothingInItIsExplained()
    {
        var lib = Lib.Value;
        var q = new HelpQuery { Labels = ["Zebra crossing"], Area = "layers.md", Targets = ["panel:Layers"] };
        Assert.Equal(("layers.md", ""), HelpFinder.Page(lib, q));
        var t = HelpFinder.Tutorial(lib, q);
        Assert.NotNull(t);
        Assert.StartsWith("layers.md", t!.Page);
        // A shape the program names outright: that section, and a tutorial about it.
        var text = new HelpQuery { Page = "drawing.md#text", Targets = ["canvas"] };
        Assert.Equal(("drawing.md", "text"), HelpFinder.Page(lib, text));
        Assert.StartsWith("drawing.md#text", HelpFinder.Tutorial(lib, text)?.Page);
        // Nothing known: no page (the guide opens at its start) and no tutorial (the list opens).
        Assert.Null(HelpFinder.Page(lib, new HelpQuery { Labels = ["Zebra crossing"] }));
    }

    /// <summary>Does this section (or, for "", the page's top) name the label as its heading, in bold or as `code`.</summary>
    static bool Names(HelpPage page, string anchor, string label)
    {
        var raw = new List<Block>();
        string at = "";
        foreach (var b in page.Blocks)
        {
            if (b is Heading { Level: >= 2 } h) at = h.Anchor;
            if (at == anchor) raw.Add(b);
        }
        string clean = HelpFinder.Clean(label);
        bool Inl(IEnumerable<Inline> i) => i.Any(x => x is TextRun { Bold: true } t && HelpFinder.Clean(t.Text).Equals(clean, StringComparison.OrdinalIgnoreCase)
                                                    || x is CodeRun c && HelpFinder.Clean(c.Text).Equals(clean, StringComparison.OrdinalIgnoreCase));
        static IEnumerable<ListItem> Items(IEnumerable<ListItem> items) => items.SelectMany(i => Items(i.Children).Prepend(i));
        return HelpFinder.Clean(page.Title).Equals(clean, StringComparison.OrdinalIgnoreCase) || raw.Any(b => b switch
        {
            Heading h => HelpFinder.Clean(h.Text).Equals(clean, StringComparison.OrdinalIgnoreCase),
            Paragraph p => Inl(p.Inlines),
            ListBlock l => Items(l.Items).Any(i => Inl(i.Inlines)),
            Callout c => Inl(c.Inlines),
            Table t => t.Rows.SelectMany(r => r).Any(Inl),
            _ => false,
        });
    }
}
