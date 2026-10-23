using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using PhotonCanvas.Doc;
using static PhotonCanvas.App.UiKit;

namespace PhotonCanvas.App;

/// <summary>
/// Browse ready-made shapes and your own saved ones; click to add one to the design. Stays open
/// so you can add several.
/// </summary>
public sealed class ShapeLibraryWindow : Window
{
    readonly string _userFolder;
    readonly Action<LibraryShape> _insert;
    readonly Func<IReadOnlyList<PathShape>> _selection;
    readonly ListBox _categories = new();
    readonly TextBox _search = new() { Watermark = "Search shapes" };
    readonly WrapPanel _tiles = new();
    readonly TextBlock _status = Hint();
    List<LibraryShape> _user = [];

    public ShapeLibraryWindow(string userFolder, Action<LibraryShape> insert, Func<IReadOnlyList<PathShape>> selection)
    {
        _userFolder = userFolder;
        _insert = insert;
        _selection = selection;
        Title = "Shapes";
        Width = 760;
        Height = 620;
        MinWidth = 520;
        MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");

        _categories.ItemsSource = ShapeLibrary.Categories.Prepend("All").ToList();
        _categories.SelectedIndex = 0;
        _categories.SelectionChanged += (_, _) => Fill();
        _search.TextChanged += (_, _) => Fill();
        var save = Button("Save selection…", tip: "Keep the selected shapes as one shape in My shapes, to reuse in any design.");
        save.Click += async (_, _) => await SaveSelection();
        var folder = Button("Open My shapes folder", tip: "Your shapes are design files in this folder: copy them to share, or drop in .pcanvas files from others.");
        folder.Click += (_, _) =>
        {
            Directory.CreateDirectory(_userFolder);
            try { Process.Start(new ProcessStartInfo(_userFolder) { UseShellExecute = true }); }
            catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { _status.Text = e.Message; }
        };
        var reload = Button("Refresh");
        reload.Click += (_, _) => { _user = ShapeLibrary.LoadUser(_userFolder); Fill(); };

        var left = new DockPanel { Width = 170, Margin = new(0, 0, 12, 0) };
        var leftBottom = new StackPanel { Spacing = 6, Margin = new(0, 8, 0, 0), Children = { save, folder, reload } };
        DockPanel.SetDock(leftBottom, Dock.Bottom);
        left.Children.Add(leftBottom);
        left.Children.Add(_categories);
        var right = new DockPanel();
        var top = new StackPanel { Spacing = 6, Margin = new(0, 0, 0, 8), Children = { _search, Hint("Click a shape to add it to the design (40 mm across; real-size shapes at their size). The window stays open.") } };
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(_status, Dock.Bottom);
        right.Children.Add(top);
        right.Children.Add(_status);
        right.Children.Add(new ScrollViewer { Content = _tiles });
        var grid = new Grid { ColumnDefinitions = new("Auto,*") };
        grid.Children.Add(left);
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        Content = new Border { Padding = new Thickness(14), Child = grid };
        _user = ShapeLibrary.LoadUser(_userFolder);
        Fill();
    }

    void Fill()
    {
        var cat = _categories.SelectedItem as string ?? "All";
        var q = _search.Text?.Trim() ?? "";
        var shapes = ShapeLibrary.BuiltIn.Concat(_user)
            .Where(s => cat == "All" || s.Category == cat)
            .Where(s => q.Length == 0 || s.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase) || s.Category.Contains(q, StringComparison.CurrentCultureIgnoreCase));
        _tiles.Children.Clear();
        foreach (var s in shapes) _tiles.Children.Add(Tile(s));
        if (_tiles.Children.Count == 0)
            _tiles.Children.Add(Hint(cat == ShapeLibrary.UserCategory ? "No shapes of your own yet: select something in the design and click Save selection to My shapes." : "Nothing matches."));
    }

    Control Tile(LibraryShape s)
    {
        var thumb = new ShapeThumb(new PathShape { Geometry = s.Geometry }, Color.Parse("#7FB2FF"), filled: false, width: 96, height: 72) { HorizontalAlignment = HorizontalAlignment.Center };
        var b = new Button
        {
            Width = 116, Height = 112, Margin = new(0, 0, 8, 8), Padding = new(6),
            Content = new StackPanel
            {
                Spacing = 4,
                Children = { thumb, new TextBlock { Text = s.Name, FontSize = 11, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis } },
            },
        };
        ToolTip.SetTip(b, $"{s.Name} ({s.Category})" + (s.RealSize ? " · added at its real size" : ""));
        b.Click += (_, _) => { _insert(s); _status.Text = $"Added {s.Name}."; };
        if (s.IsUser)
        {
            var del = new MenuItem { Header = "Delete from My shapes" };
            del.Click += async (_, _) =>
            {
                if (!await Dialogs.Confirm(this, "Delete shape", $"Delete \"{s.Name}\" from your shapes?", "Delete", "Keep")) return;
                try { File.Delete(s.File!); } catch (IOException e) { _status.Text = e.Message; }
                _user = ShapeLibrary.LoadUser(_userFolder);
                Fill();
            };
            b.ContextMenu = new ContextMenu { Items = { del } };
        }
        return b;
    }

    async Task SaveSelection()
    {
        var shapes = _selection();
        if (shapes.Count == 0)
        {
            _status.Text = "Select the shapes to save in the design first.";
            return;
        }
        if (await Dialogs.Prompt(this, "Save to My shapes", "Name:", shapes[0].Name is { Length: > 0 } n ? n : "My shape") is not { Length: > 0 } name) return;
        try
        {
            ShapeLibrary.SaveUser(_userFolder, name.Trim(), shapes);
            _user = ShapeLibrary.LoadUser(_userFolder);
            _categories.SelectedItem = ShapeLibrary.UserCategory;
            Fill();
            _status.Text = $"Saved \"{name.Trim()}\".";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _status.Text = e.Message; }
    }
}
