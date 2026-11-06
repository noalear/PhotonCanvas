using System.Text.RegularExpressions;

namespace PhotonCanvas.Tests;

/// <summary>
/// Every colour and icon the code asks the app for by name is one the app has: a missing one throws, and
/// thrown while a view draws itself it takes the whole program down (the city builder's Furniture tool did).
/// </summary>
public class ResourceKeyTests
{
    static HashSet<string> Keys()
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(Path.Combine(TestPaths.Root, "src", "PhotonCanvas.App"), "*.axaml", SearchOption.AllDirectories))
            foreach (Match m in Regex.Matches(File.ReadAllText(file), "x:Key=\"([^\"]+)\""))
                keys.Add(m.Groups[1].Value);
        return keys;
    }

    static IEnumerable<string> Sources() =>
        Directory.EnumerateFiles(Path.Combine(TestPaths.Root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    [Fact]
    public void Every_brush_and_icon_asked_for_by_name_exists()
    {
        var keys = Keys();
        Assert.Contains("TextMain", keys);
        var missing = new List<string>();
        int seen = 0;
        foreach (var file in Sources())
        {
            var text = File.ReadAllText(file);
            // UiKit.Brush("…") and UiKit.Icon("…"), called with or without UiKit (IconOrPath has a fallback of its own).
            foreach (Match m in Regex.Matches(text, @"(?<![\w.])(?:UiKit\.)?(Brush|Icon)\(""([A-Za-z0-9]+)""\)"))
            {
                seen++;
                if (!keys.Contains(m.Groups[2].Value))
                    missing.Add($"{Path.GetRelativePath(TestPaths.Root, file)}: {m.Groups[1].Value}(\"{m.Groups[2].Value}\")");
            }
        }
        Assert.True(seen > 100, $"only {seen} found: the pattern has stopped matching");
        Assert.True(missing.Count == 0, "Not in the app's resources:\n" + string.Join("\n", missing.Distinct()));
    }
}
