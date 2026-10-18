using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using PhotonCanvas.Doc;
using static PhotonCanvas.App.UiKit;

namespace PhotonCanvas.App;

/// <summary>
/// The project's pages, as tabs above the work area: click one to show it, + adds one, and the
/// right-click menu renames, copies, moves or deletes. Each page is its own job.
/// </summary>
public sealed class PagesBar : UserControl
{
    readonly Document _doc;
    readonly Window _owner;
    readonly StackPanel _tabs = new() { Orientation = Orientation.Horizontal, Spacing = 2 };
    List<string> _shown = [];
    int _shownIndex = -1;

    public PagesBar(Document doc, Window owner)
    {
        _doc = doc;
        _owner = owner;
        var add = new Button { Classes = { "icon" }, Padding = new(6, 2), Content = new PathIcon { Data = Icon("IcPlus"), Width = 11, Height = 11 }, VerticalAlignment = VerticalAlignment.Center };
        ToolTip.SetTip(add, "Add a page (same layers, nothing on it). Each page is its own job.");
        add.Click += (_, _) => _doc.Commit("Add page", Pages.Add(_doc.State), []);
        Content = new Border
        {
            Background = Brush("Bg1"),
            BorderBrush = Brush("Line"),
            BorderThickness = new(0, 0, 0, 1),
            Padding = new(8, 3),
            Child = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { _tabs, add } },
            },
        };
        _doc.Changed += _ => Refresh();
        Refresh();
    }

    void Refresh()
    {
        var names = Pages.Names(_doc.State);
        if (names.SequenceEqual(_shown) && _shownIndex == _doc.State.PageIndex) return;
        _shown = names;
        _shownIndex = _doc.State.PageIndex;
        _tabs.Children.Clear();
        for (int i = 0; i < names.Count; i++)
        {
            int index = i;
            bool current = i == _doc.State.PageIndex;
            var tab = new ToggleButton
            {
                Content = names[i],
                IsChecked = current,
                Classes = { "rbtab" },
                Padding = new(12, 3),
                FontSize = 12,
            };
            tab.Click += (_, _) =>
            {
                tab.IsChecked = index == _doc.State.PageIndex;
                if (index != _doc.State.PageIndex) _doc.Commit($"Show {names[index]}", Pages.SwitchTo(_doc.State, index), []);
            };
            tab.DoubleTapped += (_, _) => { if (index == _doc.State.PageIndex) _ = RenameCurrent(); };
            tab.ContextMenu = new ContextMenu
            {
                Items =
                {
                    Item("Rename…", () => { Show(index); _ = RenameCurrent(); }),
                    Item("Duplicate", () => { Show(index); _doc.Commit("Duplicate page", Pages.Duplicate(_doc.State), []); }),
                    Item("Move left", () => { Show(index); _doc.Commit("Move page", Pages.Move(_doc.State, -1)); }, index > 0),
                    Item("Move right", () => { Show(index); _doc.Commit("Move page", Pages.Move(_doc.State, 1)); }, index < names.Count - 1),
                    new Separator(),
                    Item("Delete page", () => { Show(index); _doc.Commit("Delete page", Pages.Delete(_doc.State), []); }, names.Count > 1),
                },
            };
            _tabs.Children.Add(tab);
        }
    }

    void Show(int index)
    {
        if (index != _doc.State.PageIndex) _doc.Commit("Show page", Pages.SwitchTo(_doc.State, index), []);
    }

    static MenuItem Item(string header, Action run, bool enabled = true)
    {
        var m = new MenuItem { Header = header, IsEnabled = enabled };
        m.Click += (_, _) => run();
        return m;
    }

    async Task RenameCurrent()
    {
        var name = await Dialogs.Prompt(_owner, "Rename page", "Name of this page:", _doc.State.PageName);
        if (!string.IsNullOrWhiteSpace(name)) _doc.Commit("Rename page", Pages.Rename(_doc.State, name));
    }
}
