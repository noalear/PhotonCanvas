using Avalonia.Controls;
using PhotonCanvas.App.Addons;

namespace PhotonCanvas.App;

/// <summary>
/// The tests: one Tests button (Materials &amp; Tests → Tests) opens a window with all of them in a list,
/// the program's and the add-ons'. An add-on adds one as a ribbon button on the "Tests" tab, which
/// isn't on the ribbon (<see cref="RibbonPlaces.Tests"/>); its group is the heading it's listed under.
/// </summary>
public partial class MainWindow
{
    Button? _testsButton;
    readonly List<TestEntry> _tests = [];
    int _testArrivals;
    /// <summary>The test a tutorial step points at: the Tests window opens on it.</summary>
    string? _testWanted;

    IDisposable AddTest(string category, string label, string icon, Action run, string? tip = null, Func<bool>? enabled = null,
        Func<bool>? visible = null, int order = 0, string? addonId = null)
    {
        var t = new TestEntry(category, label, icon, run, tip, enabled, visible, order, _testArrivals++, addonId);
        _tests.Add(t);
        // A tutorial's ribbon:Tests/Label and the help find the Tests button for it.
        if (_testsButton is { } b) _ribbon.Alias(RibbonPlaces.Tests, label, b);
        return new AddonUndo(() =>
        {
            _tests.Remove(t);
            if (!_tests.Any(x => x.Label == label)) _ribbon.Unalias(RibbonPlaces.Tests, label);
        });
    }

    /// <summary>The Tests window, on <paramref name="pick"/> (else the test a tutorial points at, else the last one opened).</summary>
    async Task ShowTests(string? pick)
    {
        pick ??= _testWanted ?? _settings.LastTest;
        _testWanted = null;
        var shown = _tests.Where(t => t.Visible?.Invoke() ?? true).ToList();
        var w = new TestsWindow(shown, pick);
        await w.ShowDialog(this);
        if (w.Chosen is not { } chosen) return;
        _settings.LastTest = chosen.Label;
        _settings.Save();
        chosen.Run();
    }
}

/// <summary>One test in the Tests window.</summary>
public sealed record TestEntry(string Category, string Label, string Icon, Action Run, string? Tip, Func<bool>? Enabled, Func<bool>? Visible,
    int Order, int Arrived, string? AddonId);
