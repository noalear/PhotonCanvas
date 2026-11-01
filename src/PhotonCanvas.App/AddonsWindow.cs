using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using PhotonCanvas.App.Addons;
using static PhotonCanvas.App.UiKit;

namespace PhotonCanvas.App;

/// <summary>
/// Settings → Add-ons: every add-on, grouped by kind, with a switch to turn it on or off right
/// away, Remove for ones the person installed, and Install from file.
/// </summary>
public sealed class AddonsWindow : Window
{
    readonly AddonManager _addons;
    readonly StackPanel _list = new() { Spacing = 14 };
    readonly TextBlock _status = Hint();

    public AddonsWindow(AddonManager addons)
    {
        _addons = addons;
        Title = "Add-ons";
        Width = 640;
        Height = 620;
        MinWidth = 480;
        MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");

        var install = Button("Install from file…", tip: $"Add an add-on from a {AddonManager.Extension} file (or update one you have).");
        install.Click += async (_, _) => await Install();
        var folder = Button("Open add-ons folder", tip: "Where the add-ons you install are kept.");
        folder.Click += (_, _) => OpenFolder();
        var close = new Button { Content = "Close", IsCancel = true, MinWidth = 90 };
        close.Click += (_, _) => Close();

        var top = new StackPanel
        {
            Spacing = 4,
            Margin = new(20, 18, 20, 8),
            Children =
            {
                new TextBlock { Text = "Add-ons", FontSize = 18, FontWeight = FontWeight.SemiBold },
                Hint("Extra features. Turn each on or off here: it takes effect straight away, no restart. The ones that come with PhotonCanvas can be turned off; ones you installed can be removed."),
            },
        };
        var bottom = new DockPanel { Margin = new(20, 8, 20, 16), LastChildFill = false };
        var left = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { install, folder } };
        DockPanel.SetDock(left, Dock.Left);
        DockPanel.SetDock(close, Dock.Right);
        bottom.Children.Add(left);
        bottom.Children.Add(close);
        _status.Margin = new(20, 0, 20, 0);

        var root = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(bottom, Dock.Bottom);
        DockPanel.SetDock(_status, Dock.Bottom);
        root.Children.Add(top);
        root.Children.Add(bottom);
        root.Children.Add(_status);
        root.Children.Add(new ScrollViewer { Content = _list, Padding = new(20, 4, 20, 4) });
        Content = root;

        _addons.Changed += Fill;
        Closed += (_, _) => _addons.Changed -= Fill;
        _addons.Scan();
        Fill();
    }

    void Fill()
    {
        _list.Children.Clear();
        if (_addons.Entries.Count == 0)
        {
            _list.Children.Add(Hint($"No add-ons found. They go in {AddonManager.BuiltInFolder} (with the program) or {AddonManager.UserFolder} (installed)."));
            return;
        }
        foreach (var group in _addons.Entries.GroupBy(e => e.Manifest.Category).OrderBy(g => g.Key == "Other" ? "~" : g.Key))
        {
            var items = new StackPanel { Spacing = 8 };
            foreach (var e in group) items.Children.Add(Row(e));
            _list.Children.Add(new StackPanel { Spacing = 6, Children = { new TextBlock { Text = group.Key.ToUpperInvariant(), Classes = { "h" } }, items } });
        }
    }

    Control Row(AddonEntry e)
    {
        var m = e.Manifest;
        var on = new ToggleSwitch { IsChecked = e.IsOn, OnContent = "On", OffContent = "Off", VerticalAlignment = VerticalAlignment.Top };
        ToolTip.SetTip(on, e.IsOn ? $"Turn {m.Name} off" : $"Turn {m.Name} on");
        on.IsCheckedChanged += (_, _) =>
        {
            bool want = on.IsChecked == true;
            if (want == e.IsOn) return;
            bool ok = _addons.Set(e.Id, want);
            _status.Text = !ok ? $"{m.Name}: {e.Problem}" : want ? $"{m.Name} is on." : $"{m.Name} is off.";
        };
        var title = new TextBlock { Text = m.Name, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
        var meta = Muted($"{m.Version}{(m.Author is { Length: > 0 } a ? " · " + a : "")} · {(m.BuiltIn ? "comes with PhotonCanvas" : "installed")}");
        var text = new StackPanel { Spacing = 3, Children = { title, meta } };
        if (m.Description.Length > 0) text.Children.Add(new TextBlock { Text = m.Description, TextWrapping = TextWrapping.Wrap });
        if (m.Requires.Length > 0) text.Children.Add(Muted("Needs: " + ListWords(m.Requires.Select(r => _addons.Find(r)?.Manifest.Name ?? r))));
        if (e.Problem is { } p) text.Children.Add(new TextBlock { Text = "⚠ " + p, Foreground = Brush("Warn"), TextWrapping = TextWrapping.Wrap });

        var right = new StackPanel { Spacing = 6, HorizontalAlignment = HorizontalAlignment.Right, Children = { on } };
        if (AddonManager.CanRemove(e))
        {
            var remove = Button("Remove", tip: $"Turn {m.Name} off and delete it from this computer.");
            remove.Click += async (_, _) =>
            {
                if (!await Dialogs.Confirm(this, "Remove add-on", $"Remove \"{m.Name}\"? It's turned off and deleted from this computer (designs keep anything it saved in them).", "Remove")) return;
                try
                {
                    _addons.Remove(e.Id);
                    _status.Text = $"{m.Name} removed.";
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
                {
                    _status.Text = $"Couldn't remove {m.Name}: {ex.Message}";
                }
            };
            right.Children.Add(remove);
        }
        var grid = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 12 };
        grid.Children.Add(text);
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        return new Border { BorderBrush = Brush("Line"), BorderThickness = new(1), CornerRadius = new(8), Padding = new(12, 10), Child = grid };
    }

    async Task Install()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Install an add-on",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("PhotonCanvas add-on") { Patterns = ["*" + AddonManager.Extension, "*.zip"] }],
        });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path) return;
        try
        {
            var e = _addons.Install(path);
            _status.Text = e.IsOn ? $"{e.Manifest.Name} {e.Manifest.Version} installed and on." : $"{e.Manifest.Name} installed, but it couldn't start: {e.Problem}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or FormatException or InvalidDataException or System.Text.Json.JsonException)
        {
            _status.Text = $"Couldn't install it: {ex.Message}";
        }
    }

    void OpenFolder()
    {
        try
        {
            Directory.CreateDirectory(AddonManager.UserFolder);
            Process.Start(new ProcessStartInfo { FileName = AddonManager.UserFolder, UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            _status.Text = $"The add-ons folder is {AddonManager.UserFolder}.";
        }
    }
}
