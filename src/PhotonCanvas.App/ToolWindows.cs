using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using PhotonCanvas.Device;
using PhotonCanvas.Doc;
using static PhotonCanvas.App.UiKit;

namespace PhotonCanvas.App;

/// <summary>Search the installed fonts, star the ones you use, hide the ones you never want to see.</summary>
public sealed class FontWindow : Window
{
    readonly AppSettings _settings;
    readonly IReadOnlyList<string> _all = TextLayout.FontFamilies();
    readonly TextBox _search = new() { Watermark = "Search fonts" };
    readonly TextBox _sample = new() { Text = "The quick brown fox 0123" };
    readonly Segmented _filter = new(("All", null), ("Starred", null), ("Hidden", null));
    readonly ListBox _list = new();
    readonly TextBlock _count = Hint();

    /// <summary>Stars or hidden fonts changed.</summary>
    public event Action? FontsChanged;

    public FontWindow(AppSettings settings)
    {
        _settings = settings;
        Title = "Fonts";
        Width = 640;
        Height = 640;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");
        _filter.SelectedIndex = 0;
        _search.TextChanged += (_, _) => Fill();
        _sample.TextChanged += (_, _) => Fill();
        _filter.Changed += _ => Fill();
        var top = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                Row(_search, _sample),
                new DockPanel { Children = { _count.With(c => DockPanel.SetDock(c, Dock.Right)), _filter } },
                Hint("Starred fonts come first in the text font list; hidden ones are left out of it (text already using one keeps it)."),
            },
        };
        DockPanel.SetDock(top, Dock.Top);
        Content = new DockPanel { Margin = new Thickness(14), Children = { top, _list } };
        _list.Margin = new(0, 8, 0, 0);
        Fill();
    }

    void Fill()
    {
        var q = _search.Text?.Trim() ?? "";
        var fav = _settings.FontFavourites.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var hidden = _settings.FontHidden.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var fonts = _all.Where(f => q.Length == 0 || f.Contains(q, StringComparison.OrdinalIgnoreCase))
            .Where(f => _filter.SelectedIndex switch { 1 => fav.Contains(f), 2 => hidden.Contains(f), _ => true })
            .OrderByDescending(f => fav.Contains(f)).ToList();
        _count.Text = $"{fonts.Count} of {_all.Count} fonts";
        string sample = string.IsNullOrEmpty(_sample.Text) ? "Aa" : _sample.Text;
        _list.ItemsSource = fonts.Select(f =>
        {
            var star = new ToggleButton { Content = "★", IsChecked = fav.Contains(f), Padding = new(6, 0), VerticalAlignment = VerticalAlignment.Center };
            ToolTip.SetTip(star, "Star: show first in the font list");
            star.IsCheckedChanged += (_, _) => Toggle(_settings.FontFavourites, f, star.IsChecked == true);
            var hide = new ToggleButton { Content = "Hide", IsChecked = hidden.Contains(f), Padding = new(6, 0), VerticalAlignment = VerticalAlignment.Center };
            hide.IsCheckedChanged += (_, _) => Toggle(_settings.FontHidden, f, hide.IsChecked == true);
            var row = new Grid { ColumnDefinitions = new("Auto,170,*,Auto"), ColumnSpacing = 8, Opacity = hidden.Contains(f) ? 0.5 : 1 };
            row.Children.Add(star);
            row.Children.Add(new TextBlock { Text = f, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, [Grid.ColumnProperty] = 1 });
            row.Children.Add(new TextBlock { Text = sample, FontFamily = new FontFamily(f), FontSize = 20, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, [Grid.ColumnProperty] = 2 });
            row.Children.Add(hide.With(h => Grid.SetColumn(h, 3)));
            return row;
        }).ToList();
    }

    void Toggle(List<string> list, string font, bool on)
    {
        list.RemoveAll(x => string.Equals(x, font, StringComparison.OrdinalIgnoreCase));
        if (on) list.Add(font);
        _settings.Save();
        FontsChanged?.Invoke();
    }
}

/// <summary>Designs kept as templates: opening one starts a new, untitled design, so the template itself never changes.</summary>
public sealed class TemplatesWindow : Window
{
    readonly string _folder;
    readonly ListBox _list = new();
    readonly TextBlock _empty = Hint("No templates yet. Make a design you'll reuse, then File → Save as template.");

    /// <summary>The template to start from, or null.</summary>
    public string? Chosen { get; private set; }

    public TemplatesWindow(string folder)
    {
        _folder = folder;
        Title = "New from template";
        Width = 420;
        Height = 460;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");
        var use = new Button { Content = "Start from it", Classes = { "primary" }, IsDefault = true, MinWidth = 110 };
        var delete = new Button { Content = "Delete" };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 90 };
        use.Click += (_, _) => { if (_list.SelectedItem is string n) { Chosen = Path.Combine(_folder, n + DocFile.Extension); Close(); } };
        _list.DoubleTapped += (_, _) => { if (_list.SelectedItem is string n) { Chosen = Path.Combine(_folder, n + DocFile.Extension); Close(); } };
        delete.Click += async (_, _) =>
        {
            if (_list.SelectedItem is not string n) return;
            if (!await Dialogs.Confirm(this, "Delete template?", $"Delete the template \"{n}\"? Designs made from it aren't affected.", "Delete", "Cancel")) return;
            try { File.Delete(Path.Combine(_folder, n + DocFile.Extension)); } catch (IOException) { }
            Fill();
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = new(0, 10, 0, 0), Children = { delete, cancel, use } };
        DockPanel.SetDock(buttons, Dock.Bottom);
        DockPanel.SetDock(_empty, Dock.Top);
        Content = new DockPanel { Margin = new Thickness(14), Children = { buttons, _empty, _list } };
        Fill();
    }

    void Fill()
    {
        var names = Directory.Exists(_folder)
            ? Directory.GetFiles(_folder, "*" + DocFile.Extension).Select(Path.GetFileNameWithoutExtension).OfType<string>().Order(StringComparer.CurrentCultureIgnoreCase).ToList()
            : [];
        _list.ItemsSource = names;
        if (names.Count > 0) _list.SelectedIndex = 0;
        _empty.IsVisible = names.Count == 0;
    }
}

/// <summary>A command that can have keyboard shortcuts. Keys are like "Ctrl+Shift+K"; several are separated by "|".</summary>
public sealed record HotCommand(string Id, string Group, string Name, string Default, Action Run, bool AlsoWhileTyping = false);

/// <summary>Change the keyboard shortcuts: click a command's keys, press the new ones.</summary>
public sealed class HotkeysWindow : Window
{
    readonly IReadOnlyList<HotCommand> _commands;
    readonly AppSettings _settings;
    readonly StackPanel _rows = new() { Spacing = 2 };
    readonly TextBlock _note = Hint();
    HotCommand? _capturing;

    public HotkeysWindow(IReadOnlyList<HotCommand> commands, AppSettings settings)
    {
        _commands = commands;
        _settings = settings;
        Title = "Keyboard shortcuts";
        Width = 560;
        Height = 700;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");
        var resetAll = new Button { Content = "All back to the defaults" };
        resetAll.Click += (_, _) => { _settings.Hotkeys.Clear(); _settings.Save(); Fill(); };
        var close = new Button { Content = "Close", IsCancel = true, MinWidth = 90 };
        close.Click += (_, _) => Close();
        var top = new StackPanel { Spacing = 6, Children = { Hint("Click a shortcut, then press the new keys (Esc cancels, Backspace removes it). Mouse and arrow-key moves can't be changed."), _note } };
        var bottom = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = new(0, 10, 0, 0), Children = { resetAll, close } };
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(bottom, Dock.Bottom);
        Content = new DockPanel { Margin = new Thickness(14), Children = { top, bottom, new ScrollViewer { Content = _rows, Margin = new(0, 8, 0, 0) } } };
        AddHandler(KeyDownEvent, OnKey, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        Fill();
    }

    public static string Keys(HotCommand c, AppSettings s) => s.Hotkeys.TryGetValue(c.Id, out var k) ? k : c.Default;

    void Fill()
    {
        _rows.Children.Clear();
        foreach (var g in _commands.GroupBy(c => c.Group))
        {
            _rows.Children.Add(new TextBlock { Text = g.Key.ToUpperInvariant(), Classes = { "h" }, Margin = new(0, 10, 0, 2) });
            foreach (var c in g)
            {
                var keys = Keys(c, _settings);
                var b = new Button
                {
                    Content = _capturing == c ? "Press the keys…" : keys.Length == 0 ? "—" : keys.Replace("|", "  or  "),
                    MinWidth = 180, HorizontalContentAlignment = HorizontalAlignment.Left,
                    Classes = { _capturing == c ? "primary" : "" },
                };
                b.Click += (_, _) => { _capturing = c; Fill(); };
                var reset = new Button { Content = "Default", IsEnabled = _settings.Hotkeys.ContainsKey(c.Id), Padding = new(8, 2) };
                reset.Click += (_, _) => { _settings.Hotkeys.Remove(c.Id); _settings.Save(); Fill(); };
                var row = new Grid { ColumnDefinitions = new("*,Auto,Auto"), ColumnSpacing = 8 };
                row.Children.Add(new TextBlock { Text = c.Name, VerticalAlignment = VerticalAlignment.Center });
                row.Children.Add(b.With(x => Grid.SetColumn(x, 1)));
                row.Children.Add(reset.With(x => Grid.SetColumn(x, 2)));
                _rows.Children.Add(row);
            }
        }
    }

    void OnKey(object? sender, KeyEventArgs e)
    {
        if (_capturing is not { } c) return;
        e.Handled = true;
        if (e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin) return;
        if (e.Key == Key.Escape) { _capturing = null; Fill(); return; }
        string keys = e.Key == Key.Back && e.KeyModifiers == KeyModifiers.None ? "" : new KeyGesture(e.Key, e.KeyModifiers).ToString();
        _note.Text = "";
        if (keys.Length > 0)
            foreach (var other in _commands.Where(o => o != c))
            {
                var parts = Keys(other, _settings).Split('|', StringSplitOptions.RemoveEmptyEntries).ToList();
                if (parts.RemoveAll(p => string.Equals(p, keys, StringComparison.OrdinalIgnoreCase)) == 0) continue;
                _settings.Hotkeys[other.Id] = string.Join("|", parts);
                _note.Text = $"{keys} was “{other.Name}”: that one has no shortcut now.";
            }
        _settings.Hotkeys[c.Id] = keys;
        _settings.Save();
        _capturing = null;
        Fill();
    }
}

/// <summary>
/// Send G-code and M-code lines straight to the machine, and keep snippets as macros. For
/// advanced use and testing: commands run at once, outside any job.
/// </summary>
public sealed class ConsoleWindow : Window
{
    readonly MachinePanel _machine;
    readonly AppSettings _settings;
    readonly TextBox _log = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Consolas, Menlo, monospace"), FontSize = 12 };
    readonly TextBox _input = new() { Watermark = "e.g. M114 (Enter sends)", FontFamily = new FontFamily("Consolas, Menlo, monospace") };
    readonly StackPanel _macros = new() { Spacing = 4 };
    readonly List<string> _history = [];
    int _historyAt;
    bool _agreed;

    public ConsoleWindow(MachinePanel machine, AppSettings settings)
    {
        _machine = machine;
        _settings = settings;
        Title = "G-code console";
        Width = 820;
        Height = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");
        var send = new Button { Content = "Send", Classes = { "primary" }, MinWidth = 80 };
        send.Click += async (_, _) => await SendInput();
        _input.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter) { e.Handled = true; await SendInput(); }
            else if (e.Key == Key.Up && _history.Count > 0) { _historyAt = Math.Max(0, _historyAt - 1); _input.Text = _history[_historyAt]; e.Handled = true; }
            else if (e.Key == Key.Down && _history.Count > 0) { _historyAt = Math.Min(_history.Count, _historyAt + 1); _input.Text = _historyAt < _history.Count ? _history[_historyAt] : ""; e.Handled = true; }
        };
        var newMacro = Button("New macro…", tip: "Keep one or more lines of G-code under a name, to run with one click.");
        newMacro.Click += async (_, _) => await EditMacro(null);
        var warn = new Border
        {
            Background = Brush("Bg2"), CornerRadius = new(6), Padding = new(10, 6),
            Child = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Text = "⚠ Commands run on the machine straight away, outside a job: they can move the head and fire the laser. " +
                       "Only send what you understand, and wear safety glasses.",
            },
        };
        var inputRow = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 8, Children = { _input, send.With(b => Grid.SetColumn(b, 1)) } };
        var left = new DockPanel();
        DockPanel.SetDock(warn, Dock.Top);
        DockPanel.SetDock(inputRow, Dock.Bottom);
        warn.Margin = new(0, 0, 0, 8);
        inputRow.Margin = new(0, 8, 0, 0);
        left.Children.Add(warn);
        left.Children.Add(inputRow);
        left.Children.Add(_log);
        var right = new DockPanel { Width = 230, Margin = new(12, 0, 0, 0) };
        var head = new StackPanel { Spacing = 6, Children = { new TextBlock { Text = "MACROS", Classes = { "h" } }, newMacro } };
        DockPanel.SetDock(head, Dock.Top);
        right.Children.Add(head);
        right.Children.Add(new ScrollViewer { Content = _macros, Margin = new(0, 8, 0, 0) });
        var grid = new Grid { ColumnDefinitions = new("*,Auto") };
        grid.Children.Add(left);
        grid.Children.Add(right.With(r => Grid.SetColumn(r, 1)));
        Content = new Border { Padding = new Thickness(14), Child = grid };
        FillMacros();
        Append(_machine.IsConnected ? "Connected. Type a command and press Enter." : "Not connected: connect the machine first (right end of the ribbon).");
        Opened += (_, _) => _input.Focus();
    }

    void Append(string line)
    {
        _log.Text = (_log.Text ?? "") + line + "\n";
        _log.CaretIndex = _log.Text.Length;
    }

    async Task<bool> Agree()
    {
        if (_agreed) return true;
        _agreed = await Dialogs.Confirm(this, "Send commands to the machine?",
            "Commands from this console run straight away and can move the head or fire the laser. Nothing here waits for the machine's start button.\n\n" +
            "Send only commands you understand, with the lid closed and safety glasses on.", "I understand, send", "Cancel");
        return _agreed;
    }

    async Task SendInput()
    {
        var text = (_input.Text ?? "").Trim();
        if (text.Length == 0) return;
        _history.Add(text);
        _historyAt = _history.Count;
        _input.Text = "";
        await Run([text]);
    }

    async Task Run(IEnumerable<string> lines)
    {
        if (!_machine.IsConnected) { Append("Not connected."); return; }
        if (!await Agree()) return;
        foreach (var raw in lines)
        {
            var line = raw.Split(';')[0].Trim(); // ; starts a comment
            if (line.Length == 0) continue;
            Append($"> {line}");
            try
            {
                var reply = await _machine.SendCommandAsync(line);
                Append(reply.Length == 0 || reply == "null" ? "  ok" : $"  {reply}");
            }
            catch (DeviceException e)
            {
                Append($"  ✗ {e.Message}");
                break;
            }
        }
    }

    void FillMacros()
    {
        _macros.Children.Clear();
        if (_settings.Macros.Count == 0) _macros.Children.Add(Hint("None yet."));
        foreach (var m in _settings.Macros)
        {
            var run = new Button { Content = m.Name, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left };
            ToolTip.SetTip(run, m.Gcode);
            run.Click += async (_, _) => await Run(m.Gcode.Replace("\r\n", "\n").Split('\n'));
            var edit = new Button { Content = "Edit", Padding = new(6, 2) };
            edit.Click += async (_, _) => await EditMacro(m);
            _macros.Children.Add(new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 4, Children = { run, edit.With(e => Grid.SetColumn(e, 1)) } });
        }
    }

    async Task EditMacro(Macro? m)
    {
        var name = new TextBox { Text = m?.Name ?? "", Watermark = "Name" };
        var code = new TextBox { Text = m?.Gcode ?? "", AcceptsReturn = true, Height = 180, FontFamily = new FontFamily("Consolas, Menlo, monospace"), Watermark = "One command per line; ; starts a comment" };
        var ok = new Button { Content = "Save", Classes = { "primary" }, MinWidth = 90 };
        var del = new Button { Content = "Delete", IsVisible = m is not null };
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        var w = new Window
        {
            Title = m is null ? "New macro" : "Edit macro", Width = 420, SizeToContent = SizeToContent.Height, CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Brush("Bg1"),
            Content = new StackPanel
            {
                Margin = new Thickness(14), Spacing = 8,
                Children = { name, code, new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { del, cancel, ok } } },
            },
        };
        ok.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(name.Text) || string.IsNullOrWhiteSpace(code.Text)) return;
            var target = m ?? new Macro();
            target.Name = name.Text.Trim();
            target.Gcode = code.Text.Trim();
            if (m is null) _settings.Macros.Add(target);
            _settings.Save();
            w.Close();
        };
        del.Click += (_, _) => { if (m is not null) _settings.Macros.Remove(m); _settings.Save(); w.Close(); };
        cancel.Click += (_, _) => w.Close();
        await w.ShowDialog(this);
        FillMacros();
    }
}
