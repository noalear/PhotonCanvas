using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using static PhotonCanvas.App.UiKit;

namespace PhotonCanvas.App;

/// <summary>
/// Every test in one place (Materials &amp; Tests → Tests): pick one from the list, read what it finds out,
/// and open it. The tests themselves are the windows they always were; this only chooses between them.
/// </summary>
public sealed class TestsWindow : Window
{
    /// <summary>The headings the list goes in, in this order (an add-on's own heading comes after them).</summary>
    public static readonly string[] Categories =
        ["Find the settings", "Pictures", "Colour on metal", "Use your results", "Machine checks", "Experimental", "More tests"];

    readonly ComboBox _list = new() { HorizontalAlignment = HorizontalAlignment.Stretch, MaxDropDownHeight = 520 };
    readonly PathIcon _icon = new() { Width = 30, Height = 30, VerticalAlignment = VerticalAlignment.Top };
    readonly TextBlock _name = new() { FontSize = 16, FontWeight = FontWeight.SemiBold };
    readonly TextBlock _heading = Muted("");
    readonly TextBlock _about = new() { TextWrapping = TextWrapping.Wrap, LineHeight = 19 };
    readonly TextBlock _unavailable = Hint("Not ready yet: it needs something first (a selection, a burned test or a connected machine). See help.");
    readonly Button _open = new() { Content = "Open", IsDefault = true, Classes = { "primary" }, MinWidth = 100 };

    /// <summary>The test to open, once the window has closed (null: cancelled).</summary>
    public TestEntry? Chosen { get; private set; }

    public TestsWindow(IReadOnlyList<TestEntry> tests, string? pick)
    {
        Title = "Tests";
        Width = 560;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");

        // Headings (not pickable) and the tests under each.
        int Rank(string c) => Array.IndexOf(Categories, c) is var i and >= 0 ? i : Categories.Length;
        var items = new List<ComboBoxItem>();
        ComboBoxItem? first = null, wanted = null;
        foreach (var group in tests.GroupBy(t => t.Category).OrderBy(g => Rank(g.Key)).ThenBy(g => g.Key))
        {
            items.Add(new ComboBoxItem
            {
                IsEnabled = false,
                Focusable = false,
                Content = new TextBlock { Text = group.Key, FontWeight = FontWeight.SemiBold, Foreground = Brush("TextMuted"), FontSize = 11.5, Margin = new(0, 4, 0, 0) },
            });
            foreach (var t in group.OrderBy(t => t.Order).ThenBy(t => t.Arrived))
            {
                var item = new ComboBoxItem
                {
                    Tag = t,
                    Content = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 8,
                        Margin = new(10, 0, 0, 0),
                        Children =
                        {
                            new PathIcon { Data = IconOrPath(t.Icon), Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center },
                            new TextBlock { Text = t.Label, VerticalAlignment = VerticalAlignment.Center },
                        },
                    },
                };
                items.Add(item);
                first ??= item;
                if (wanted is null && string.Equals(t.Label, pick, StringComparison.OrdinalIgnoreCase)) wanted = item;
            }
        }
        _list.ItemsSource = items;
        _list.SelectionChanged += (_, _) => ShowChoice();
        _list.SelectedItem = wanted ?? first;

        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 90 };
        cancel.Click += (_, _) => Close();
        _open.Click += (_, _) => OpenChosen();
        // Enter in the list opens it, like the Open button.
        _list.DropDownClosed += (_, _) => _open.Focus();

        var details = new Border
        {
            Background = Brush("Bg2"),
            BorderBrush = Brush("Line"),
            BorderThickness = new(1),
            CornerRadius = new(6),
            Padding = new(14),
            MinHeight = 120,
            Child = new DockPanel
            {
                Children =
                {
                    _icon.With(i => { DockPanel.SetDock(i, Dock.Left); i.Margin = new(0, 2, 14, 0); }),
                    new StackPanel { Spacing = 6, Children = { new StackPanel { Spacing = 1, Children = { _name, _heading } }, _about, _unavailable } },
                },
            },
        };

        Content = new Border
        {
            Padding = new Thickness(18),
            Child = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    Muted("Pick a test to see what it finds out, then open it. What a test finds is saved with the material in your library."),
                    Field("Test", _list),
                    details,
                    Hint("Calibration done once for the machine (the camera, the close-up position, lining the lasers up, the kerf gauge) is on Machine → Calibrate."),
                    new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { cancel, _open } },
                },
            },
        };
        ShowChoice();
    }

    TestEntry? Selected => (_list.SelectedItem as ComboBoxItem)?.Tag as TestEntry;

    void ShowChoice()
    {
        var t = Selected;
        _icon.Data = t is null ? null : IconOrPath(t.Icon);
        _name.Text = t?.Label ?? "No tests";
        _heading.Text = t?.Category ?? "";
        _about.Text = t?.Tip ?? (t is null ? "Turn on the Material tests add-on (View → Add-ons) for the cut, etch, tone and line tests." : "");
        bool ready = t is not null && (t.Enabled?.Invoke() ?? true);
        _unavailable.IsVisible = t is not null && !ready;
        _open.IsEnabled = ready;
    }

    void OpenChosen()
    {
        if (Selected is not { } t || !(t.Enabled?.Invoke() ?? true)) return;
        Chosen = t;
        Close();
    }
}
