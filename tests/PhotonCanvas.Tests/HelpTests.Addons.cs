using System.Text.RegularExpressions;
using PhotonCanvas.Doc.Help;
using Xunit;

namespace PhotonCanvas.Tests;

/// <summary>
/// The same checks for each add-on in src/Addons: its help/ folder has a page and a tutorial, its
/// links, pictures and tutorial steps exist (in its own help or the program's), and its ribbon
/// buttons and windows are explained.
/// </summary>
public partial class HelpTests
{
    static string AddonsDir => Path.Combine(Root, "src", "Addons");

    static IEnumerable<string> AddonNames() => (Directory.Exists(AddonsDir) ? Directory.GetDirectories(AddonsDir) : [])
        .Where(dir => File.Exists(Path.Combine(dir, "addon.json"))).Select(dir => Path.GetFileName(dir)!).Order();

    public static TheoryData<string> Addons()
    {
        var d = new TheoryData<string>();
        foreach (var name in AddonNames()) d.Add(name);
        return d;
    }

    static string AddonSources(string addon) => string.Join("\n", Directory.GetFiles(Path.Combine(AddonsDir, addon), "*.cs", SearchOption.AllDirectories)
        .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
        .Select(File.ReadAllText));

    /// <summary>"Tab/Label" of each ribbon button the add-on adds (new RibbonButton("Tab", "Group", "Label", …)).</summary>
    static HashSet<string> AddonRibbon(string src) =>
        Regex.Matches(src, @"new RibbonButton\(\s*""([^""]+)"",\s*""[^""]+"",\s*""([^""]+)""").Select(m => $"{m.Groups[1].Value}/{m.Groups[2].Value}").ToHashSet();

    [Theory]
    [MemberData(nameof(Addons))]
    public void EveryAddonExplainsItself(string addon)
    {
        var help = Path.Combine(AddonsDir, addon, "help");
        Assert.True(Directory.Exists(help), $"{addon} has no help/ folder (docs/addons.md: every add-on needs a page and a tutorial).");
        var own = HelpLibrary.Load(help);
        Assert.True(own.Pages.Count > 0, $"{addon}/help has no page.");
        Assert.True(own.Tutorials.Count > 0, $"{addon}/help/tutorials has no tutorial.");
        var lib = Lib.Value.WithAddons([(addon, help)]);
        var src = AddonSources(addon);
        var problems = new List<string>();

        // Links and pictures.
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
        foreach (var page in own.Pages.Values)
        {
            if (Lib.Value.Pages.ContainsKey(page.File)) problems.Add($"{page.File}: the program's guide already has a page with that name");
            foreach (var link in HelpText.Links(page.Blocks)) Check(page.File, link, page.File);
            foreach (var fig in page.Blocks.OfType<Figure>())
                if (!File.Exists(Path.Combine(help, fig.Source))) problems.Add($"{page.File}: no picture {fig.Source} in {addon}/help");
        }
        foreach (var t in own.Tutorials)
        {
            if (t.Page is { } pg) Check($"tutorial {t.Id}", pg, "");
            foreach (var s in t.Steps) foreach (var link in HelpText.Links(s.Body)) Check($"tutorial {t.Id}", link, "");
            if (!Categories.Contains(t.Category)) problems.Add($"{t.Id}: unknown category '{t.Category}'");
        }

        // Ribbon buttons: explained on its pages, and a tutorial points at each.
        var raw = string.Join("\n", Directory.GetFiles(help, "*.md").Select(File.ReadAllText));
        var tutRaw = Directory.Exists(Path.Combine(help, "tutorials")) ? string.Join("\n", Directory.GetFiles(Path.Combine(help, "tutorials"), "*.md").Select(File.ReadAllText)) : "";
        foreach (var c in AddonRibbon(src))
        {
            if (!Documented(raw, c[(c.IndexOf('/') + 1)..])) problems.Add($"ribbon button {c} isn't on {addon}'s pages (as **Label**)");
            if (!tutRaw.Contains($"ribbon:{c}")) problems.Add($"no tutorial of {addon} points at ribbon:{c}");
        }

        // Windows: each has its page (F1).
        var claimed = own.Pages.Values.SelectMany(p => p.Windows.Keys).ToHashSet();
        foreach (Match m in Regex.Matches(src, @"class (\w+) : Window\b"))
            if (!claimed.Contains(m.Groups[1].Value)) problems.Add($"window {m.Groups[1].Value} has no page in {addon}/help (<!-- windows: {m.Groups[1].Value} -->)");

        // Tutorial targets: the program's ribbon plus this add-on's buttons; labels from both sources.
        var commands = RibbonCommands().Concat(AddonRibbon(src)).ToHashSet();
        var literals = Sources.Value.Values.Append(src).SelectMany(t => Regex.Matches(t, @"\$?""((?:[^""\\]|\\.)*)""").Select(m => m.Groups[1].Value)).ToHashSet();
        foreach (var t in own.Tutorials)
            foreach (var s in t.Steps)
                if (s.Target is { } target)
                {
                    var (kind, value) = TutorialTargets.Split(target);
                    bool ok = kind switch
                    {
                        "ribbon" => commands.Contains(value),
                        "tab" => commands.Any(c => c.StartsWith(value + "/")),
                        "header" => TutorialTargets.Headers.Contains(value, StringComparer.OrdinalIgnoreCase),
                        "panel" => TutorialTargets.Panels.Contains(value, StringComparer.OrdinalIgnoreCase),
                        "canvas" => true,
                        "field" or "button" or "tabitem" or "window" => literals.Any(l => string.Equals(l.Trim().TrimEnd(':', '…'), value, StringComparison.OrdinalIgnoreCase) || l.StartsWith(value, StringComparison.OrdinalIgnoreCase)),
                        "text" => literals.Any(l => l.Contains(value, StringComparison.OrdinalIgnoreCase)),
                        _ => false,
                    };
                    if (!ok) problems.Add($"{t.Id} / {s.Title}: target '{target}' doesn't exist");
                }
        Assert.True(problems.Count == 0, $"{addon}:\n" + string.Join("\n", problems));
    }

    [Fact]
    public void TheProgramsGuideDoesNotLinkIntoAddons()
    {
        // An add-on's pages come and go with it, so the program's own pages never link to them.
        var addonPages = AddonNames().SelectMany(a => Directory.Exists(Path.Combine(AddonsDir, a, "help"))
            ? Directory.GetFiles(Path.Combine(AddonsDir, a, "help"), "*.md").Select(Path.GetFileName) : []).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var bad = Lib.Value.Pages.Values.SelectMany(p => HelpText.Links(p.Blocks).Select(l => (p.File, l)))
            .Where(x => addonPages.Contains(Path.GetFileName(x.l.Split('#')[0]))).Select(x => $"{x.File} → {x.l}").ToList();
        Assert.True(bad.Count == 0, "Links from the program's guide into an add-on's pages:\n" + string.Join("\n", bad));
    }
}
