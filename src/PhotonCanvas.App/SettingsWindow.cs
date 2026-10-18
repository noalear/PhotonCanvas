using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using static PhotonCanvas.App.UiKit;

namespace PhotonCanvas.App;

/// <summary>
/// Settings: the things you set up once. The machine page is the full machine panel
/// (connection, camera calibration, lights, camera views, machine options and its event
/// log); everyday machine controls live on the work area instead.
/// </summary>
public sealed class SettingsWindow : Window
{
    readonly ContentControl _page = new();
    readonly MachinePanel _machine;

    public sealed record Item(string Label, string Tip, Func<Task> Run);

    public SettingsWindow(MachinePanel machine, string startPage, IReadOnlyList<(string Section, IReadOnlyList<Item> Actions)> pages,
        IReadOnlyList<(string Section, Control Page)>? panels = null)
    {
        _machine = machine;
        Title = "Settings";
        Width = 760;
        Height = 720;
        MinWidth = 600;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");

        var nav = new ListBox { Background = Brushes.Transparent, Width = 180, Margin = new(8, 12, 0, 12) };
        var names = new List<string> { "Machine" };
        foreach (var p in panels ?? []) names.Add(p.Section);
        names.AddRange(pages.Select(p => p.Section));
        nav.ItemsSource = names;
        nav.SelectionChanged += (_, _) =>
        {
            if (nav.SelectedItem is not string name) return;
            if (name == "Machine")
            {
                _page.Content = _machine;
                return;
            }
            _page.Content = null;
            if (panels?.FirstOrDefault(p => p.Section == name) is { Page: { } panel })
            {
                _page.Content = new ScrollViewer { Content = panel };
                return;
            }
            var (section, actions) = pages.First(p => p.Section == name);
            var list = new StackPanel { Spacing = 10, Margin = new(0, 4, 0, 0) };
            list.Children.Add(new TextBlock { Text = section.ToUpperInvariant(), Classes = { "h" } });
            foreach (var a in actions)
            {
                var b = new Button { Content = a.Label, MinWidth = 220, HorizontalAlignment = HorizontalAlignment.Left };
                b.Click += async (_, _) => await a.Run();
                list.Children.Add(new StackPanel { Spacing = 3, Children = { b, Hint(a.Tip) } });
            }
            _page.Content = new ScrollViewer { Content = list };
        };
        var body = new Border { Padding = new(16, 12, 16, 12), Child = _page };
        var root = new DockPanel();
        DockPanel.SetDock(nav, Dock.Left);
        root.Children.Add(nav);
        root.Children.Add(new Border { Width = 1, Background = Brush("Line"), Margin = new(8, 0, 0, 0), [DockPanel.DockProperty] = Dock.Left });
        root.Children.Add(body);
        Content = root;
        nav.SelectedItem = names.Contains(startPage) ? startPage : "Machine";
        // The machine panel lives on after this window closes: let go of it.
        Closed += (_, _) => _page.Content = null;
    }
}
