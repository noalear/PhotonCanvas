using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using PhotonCanvas.App.Addons;
using PhotonCanvas.Doc;
using static PhotonCanvas.App.UiKit;

namespace PhotonCanvas.Addon.ProductionLog;

/// <summary>
/// The production log: every job sent from here, newest first, with what each one marked. Search it,
/// mark items spoiled, add a note, export it as a spreadsheet, and carry the open design's serial
/// number and row on from its last finished run.
/// </summary>
public sealed class ProductionLogWindow : Window
{
    const int Shown = 300;

    readonly IAddonHost _host;
    readonly LogStore _store;
    readonly LogBook _book;
    string? _selected;

    readonly TextBox _search = new() { Watermark = "Serial number, text, design or note", HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly CheckBox _thisDesign = new() { Content = "Only this design", VerticalAlignment = VerticalAlignment.Center };
    readonly Button _export = new() { Content = "Export…" };
    readonly StackPanel _list = new() { Spacing = 2 };
    readonly TextBlock _count = Hint();
    readonly StackPanel _details = new() { Spacing = 10 };
    readonly TextBlock _next = new() { TextWrapping = TextWrapping.Wrap };
    readonly TextBlock _carryAbout = Hint();
    readonly Button _carry = new() { Content = "Carry on", Classes = { "primary" }, MinWidth = 120 };
    readonly CheckBox _warn = new() { Content = "Warn before marking a serial or row again" };
    readonly TextBox _note = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 48, Watermark = "An order number, a customer, what went wrong…" };
    readonly Control _notePanel;
    string? _noteFor;

    public ProductionLogWindow(IAddonHost host, LogStore store)
    {
        _host = host;
        _store = store;
        _book = store.Book;
        Title = "Production log";
        Width = 1040;
        Height = 700;
        MinWidth = 760;
        MinHeight = 460;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");

        _warn.IsChecked = store.Settings.WarnAgain;
        ToolTip.SetTip(_warn, "The job's checks warn when the log says a serial number or spreadsheet row of this design was already marked in a finished run.");
        ToolTip.SetTip(_thisDesign, "Show only the runs of the design that's open.");
        ToolTip.SetTip(_export, "Save the log as a spreadsheet (CSV): one line per item marked.");
        ToolTip.SetTip(_carry, "Set the open design's serial number and spreadsheet row to the ones after its last finished run in the log.");
        ToolTip.SetTip(_note, "Kept with this run in the log, and searched.");

        _notePanel = Labelled("Note", _note);
        Content = Layout();
        _search.TextChanged += (_, _) => Refresh();
        _thisDesign.IsCheckedChanged += (_, _) => Refresh();
        _warn.IsCheckedChanged += (_, _) => _store.Settings = _store.Settings with { WarnAgain = _warn.IsChecked == true };
        _export.Click += async (_, _) => await Export();
        _carry.Click += (_, _) => CarryOn();
        _note.LostFocus += (_, _) => KeepNote();
        _book.Changed += OnChanged;
        _host.DocumentChanged += ShowNext;
        Closing += (_, _) => KeepNote();
        Closed += (_, _) =>
        {
            _book.Changed -= OnChanged;
            _host.DocumentChanged -= ShowNext;
        };
        _selected = _book.Runs.Count > 0 ? _book.Runs[^1].Id : null;
        Refresh();
        ShowNext();
    }

    void OnChanged() => Dispatcher.UIThread.Post(Refresh);

    Control Layout()
    {
        var top = new Grid { ColumnDefinitions = new("*,Auto,Auto"), ColumnSpacing = 10 };
        top.Children.Add(Field("Search", _search));
        Grid.SetColumn(_thisDesign, 1);
        top.Children.Add(_thisDesign);
        Grid.SetColumn(_export, 2);
        top.Children.Add(_export);

        var left = new DockPanel();
        DockPanel.SetDock(_count, Dock.Bottom);
        _count.Margin = new(4, 6, 0, 0);
        left.Children.Add(_count);
        left.Children.Add(new Border
        {
            BorderBrush = Brush("Line"),
            BorderThickness = new(1),
            CornerRadius = new(8),
            Child = new ScrollViewer { Content = _list, Padding = new(4) },
        });

        var right = new Border
        {
            BorderBrush = Brush("Line"),
            BorderThickness = new(1),
            CornerRadius = new(8),
            Child = new ScrollViewer { Content = _details, Padding = new(14, 12) },
        };

        var middle = new Grid { ColumnDefinitions = new("380,*"), ColumnSpacing = 12 };
        middle.Children.Add(left);
        Grid.SetColumn(right, 1);
        middle.Children.Add(right);

        var carryText = new StackPanel { Spacing = 4, Children = { _next, _carryAbout } };
        var carryRow = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 12 };
        carryRow.Children.Add(carryText);
        _carry.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(_carry, 1);
        carryRow.Children.Add(_carry);
        var bottom = Explained("Carry on", "Where the open design's next run starts. After a stop, a crash or a design that wasn't saved, Carry on sets it to just after the last run the log says finished.",
            carryRow, _warn);

        var dock = new DockPanel { Margin = new(16) };
        DockPanel.SetDock(top, Dock.Top);
        top.Margin = new(0, 0, 0, 12);
        dock.Children.Add(top);
        DockPanel.SetDock(bottom, Dock.Bottom);
        bottom.Margin = new(0, 12, 0, 0);
        dock.Children.Add(bottom);
        dock.Children.Add(middle);
        return dock;
    }

    string OpenDesign => LogRuns.DesignName(_host.Document.FilePath);

    IEnumerable<LoggedRun> Matching()
    {
        var runs = _book.Runs.AsEnumerable().Reverse();
        if (_thisDesign.IsChecked == true) runs = runs.Where(r => string.Equals(r.Design, OpenDesign, StringComparison.OrdinalIgnoreCase));
        var words = (_search.Text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length > 0)
            runs = runs.Where(r => r.SearchText() is var t && words.All(w => t.Contains(w, StringComparison.OrdinalIgnoreCase)));
        return runs;
    }

    void Refresh()
    {
        var all = Matching().ToList();
        _list.Children.Clear();
        foreach (var r in all.Take(Shown)) _list.Children.Add(Entry(r));
        _count.Text = _book.Runs.Count == 0 ? "Nothing yet: each job sent to the machine is written down here."
            : all.Count == 0 ? "No run matches."
            : all.Count > Shown ? $"The latest {Shown} of {all.Count} runs. Search to find older ones."
            : all.Count == 1 ? "1 run." : $"{all.Count} runs.";
        if (_selected is null || all.All(r => r.Id != _selected)) _selected = all.FirstOrDefault()?.Id;
        ShowDetails();
        ShowNext();
    }

    Control Entry(LoggedRun r)
    {
        var status = LogRuns.Status(r, _book.LiveId);
        var when = r.SentAt.ToLocalTime();
        var b = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Background = r.Id == _selected ? Brush("Bg3") : Brushes.Transparent,
            Padding = new(8, 6),
            Content = new StackPanel
            {
                Spacing = 2,
                Children =
                {
                    new TextBlock { Text = $"{when:d MMM yyyy HH:mm} · {r.Design}{(r.Part is { } p ? $" ({p})" : "")}", FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis },
                    new TextBlock
                    {
                        Text = $"{LogRuns.StatusText(status)} · {r.ItemsText()}{(r.TookS is { } t ? $" · {Clock(t)}" : "")}",
                        Foreground = Brush(status switch { RunStatus.Finished => "Ok", RunStatus.Stopped or RunStatus.Unknown => "Warn", _ => "TextMuted" }),
                        FontSize = 12,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                    },
                },
            },
        };
        b.Click += (_, _) =>
        {
            KeepNote();
            _selected = r.Id;
            Refresh();
        };
        return b;
    }

    void ShowDetails()
    {
        _details.Children.Clear();
        if (_selected is null || _book.Find(_selected) is not { } r)
        {
            _details.Children.Add(Hint("Pick a run on the left to see what it marked."));
            return;
        }
        var status = LogRuns.Status(r, _book.LiveId);
        _details.Children.Add(new TextBlock { Text = r.Design + (r.Part is { } p ? $" ({p})" : ""), FontSize = 18, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
        _details.Children.Add(new TextBlock { Text = LogRuns.StatusText(status) + status switch
        {
            RunStatus.Unknown => ": PhotonCanvas closed, or lost the machine, while it ran. Check the pieces before marking them again.",
            RunStatus.NotRun => ": another job was sent in its place, or PhotonCanvas closed before it started.",
            RunStatus.Stopped => ": it was stopped before the end. The serial number and row didn't move on.",
            _ when !r.CompletesRun && status == RunStatus.Finished => $" ({r.Part}). The items count as marked once the last part finishes.",
            _ => ".",
        }, TextWrapping = TextWrapping.Wrap });

        var facts = new Grid { ColumnDefinitions = new("110,*"), RowSpacing = 3 };
        int row = 0;
        void Fact(string label, string value)
        {
            facts.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var l = Muted(label);
            Grid.SetRow(l, row);
            var v = new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap };
            Grid.SetRow(v, row);
            Grid.SetColumn(v, 1);
            facts.Children.Add(l);
            facts.Children.Add(v);
            row++;
        }
        Fact("Sent", $"{r.SentAt.ToLocalTime():dddd d MMMM yyyy, HH:mm:ss}");
        if (r.StartedAt is { } s) Fact("Started", $"{s.ToLocalTime():HH:mm:ss}");
        if (r.EndedAt is { } e) Fact("Ended", $"{e.ToLocalTime():HH:mm:ss}{(r.TookS is { } t ? $" (ran {Clock(t)}, estimate {Clock(r.EstimateS)})" : "")}");
        else Fact("Estimate", Clock(r.EstimateS));
        Fact("Size", $"{r.WidthMm:0.#} × {r.HeightMm:0.#} mm");
        Fact("Machine", r.Machine);
        if (r.Who.Length > 0) Fact("Who", r.Who);
        if (r.DesignFile is { } f) Fact("File", f);
        _details.Children.Add(facts);

        if (r.Items.Count > 0)
        {
            bool here = string.Equals(r.Design, OpenDesign, StringComparison.OrdinalIgnoreCase);
            var items = new StackPanel { Spacing = 8 };
            foreach (var item in r.Items) items.Children.Add(ItemRow(r, item, here));
            _details.Children.Add(Explained("Marked", here
                ? "Each item this run marked. Tick Spoiled for one that came out wrong; Mark again sets the open design back to it."
                : $"Each item this run marked. Tick Spoiled for one that came out wrong. Open {r.Design} to mark one again.", items));
        }
        else _details.Children.Add(Hint("Nothing in this design varies from run to run (no serial number, date or spreadsheet fields)."));

        if (r.Settings.Count > 0)
        {
            var settings = new StackPanel { Spacing = 2 };
            foreach (var line in r.Settings) settings.Children.Add(new TextBlock { Text = line, TextWrapping = TextWrapping.Wrap, FontSize = 12 });
            _details.Children.Add(Explained("Settings", "The layers it ran, as they were when it was sent.", settings));
        }

        if (_noteFor != r.Id)
        {
            _noteFor = r.Id;
            _note.Text = r.Note;
        }
        _details.Children.Add(_notePanel);
    }

    Control ItemRow(LoggedRun r, MarkedItem item, bool here)
    {
        var spoiled = new CheckBox { Content = "Spoiled", IsChecked = r.IsSpoiled(item.Index), VerticalAlignment = VerticalAlignment.Top };
        ToolTip.SetTip(spoiled, "It came out wrong: the log doesn't count it as marked, so marking it again isn't warned about.");
        spoiled.IsCheckedChanged += (_, _) => _book.SetSpoiled(r.Id, item.Index, spoiled.IsChecked == true);
        var again = new Button { Content = "Mark again", IsEnabled = here && (item.Serial is not null || item.Row is not null), VerticalAlignment = VerticalAlignment.Top };
        ToolTip.SetTip(again, here ? "Set the open design's serial number and row back to this item, to mark it again (Ctrl+Z goes back)." : $"Open {r.Design} first.");
        again.Click += (_, _) => GoBackTo(item);

        var text = new StackPanel { Spacing = 2 };
        text.Children.Add(new TextBlock { Text = r.Items.Count > 1 ? $"{item.Index + 1}. {item.Label()}" : item.Label(), FontWeight = FontWeight.SemiBold });
        foreach (var m in item.Marks) text.Children.Add(new TextBlock { Text = m, TextWrapping = TextWrapping.Wrap, FontSize = 12 });
        if (item.RowText() is { Length: > 0 } cells) text.Children.Add(new TextBlock { Text = $"{item.Sheet}: {cells}", TextWrapping = TextWrapping.Wrap, FontSize = 12, Classes = { "muted" } });

        var g = new Grid { ColumnDefinitions = new("*,Auto,Auto"), ColumnSpacing = 8 };
        g.Children.Add(text);
        Grid.SetColumn(spoiled, 1);
        g.Children.Add(spoiled);
        Grid.SetColumn(again, 2);
        g.Children.Add(again);
        return g;
    }

    void KeepNote()
    {
        if (_noteFor is not { } id || _book.Find(id) is not { } r) return;
        var text = (_note.Text ?? "").Trim();
        if (text != r.Note) _book.SetNote(id, text);
    }

    /// <summary>The bottom line: what the open design's next run marks, and what Carry on would change it to.</summary>
    void ShowNext()
    {
        var state = _host.Document.State;
        var design = OpenDesign;
        var items = LogRuns.Items(state);
        var marks = VariableText.Describe(state);
        _next.Text = marks is null ? $"{design}: nothing in it varies from run to run." : $"{design}: the next run marks {marks}.";
        _carry.IsEnabled = false;
        if (items.Count == 0 || !items.Any(i => i.Serial is not null || i.Row is not null))
        {
            _carryAbout.Text = "Carry on is for designs with a {serial} or spreadsheet fields (Home → Serial & codes).";
            return;
        }
        if (design == "Untitled")
        {
            _carryAbout.Text = "Save the design first: the log finds its runs by the design's file name.";
            return;
        }
        if (LogRuns.CarryOn(_book.Runs, design, state.Vars) is not { } carry)
        {
            _carryAbout.Text = "The log has no finished run of this design yet.";
            return;
        }
        var v = carry.Vars;
        var change = new List<string>();
        if (items.Any(i => i.Serial is not null) && v.Serial != state.Vars.Serial) change.Add($"serial {v.Serial}");
        if (items.Any(i => i.Row is not null) && v.CsvRow != state.Vars.CsvRow) change.Add($"row {v.CsvRow + 1}");
        if (change.Count == 0)
        {
            _carryAbout.Text = $"Already carrying on after {carry.From}.";
            return;
        }
        _carryAbout.Text = $"Carry on starts at {ListWords(change)}: after {carry.From}.";
        _carry.IsEnabled = true;
    }

    void CarryOn()
    {
        var state = _host.Document.State;
        if (LogRuns.CarryOn(_book.Runs, OpenDesign, state.Vars) is not { } carry) return;
        _host.Document.Commit("Carry on from the production log", state with { Vars = carry.Vars });
        _host.Status($"Carrying on: the next run marks {VariableText.Describe(_host.Document.State)}. (Ctrl+Z goes back.)");
        ShowNext();
    }

    void GoBackTo(MarkedItem item)
    {
        var state = _host.Document.State;
        _host.Document.Commit("Mark again from the production log", state with { Vars = LogRuns.GoBackTo(item, state.Vars) });
        _host.Status($"Marking again: the next run marks {VariableText.Describe(_host.Document.State)}. After it, use Carry on in the production log to go back to where you were.");
        ShowNext();
    }

    async Task Export()
    {
        KeepNote();
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export the production log",
            DefaultExtension = "csv",
            SuggestedFileName = $"Production log {DateTime.Now:yyyy-MM-dd}.csv",
            FileTypeChoices = [new FilePickerFileType("Spreadsheet (CSV)") { Patterns = ["*.csv"] }],
        });
        if (file?.TryGetLocalPath() is not { } path) return;
        var runs = Matching().ToList();
        try
        {
            await File.WriteAllTextAsync(path, LogRuns.Csv(runs, _book.LiveId), new System.Text.UTF8Encoding(true));
            _host.Status($"Production log: {runs.Count} run{(runs.Count == 1 ? "" : "s")} saved to {Path.GetFileName(path)}.");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            await PhotonCanvas.App.Dialogs.Message(this, "Couldn't save the log", e.Message);
        }
    }
}
