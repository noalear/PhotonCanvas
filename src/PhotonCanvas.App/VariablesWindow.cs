using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using PhotonCanvas.Doc;
using static PhotonCanvas.App.UiKit;

namespace PhotonCanvas.App;

/// <summary>
/// What fills in the fields of text and codes: the serial number (next, step, last), whether
/// they move on after each run, and a spreadsheet (CSV) whose headings become fields.
/// </summary>
public sealed class VariablesWindow : Window
{
    readonly NumericUpDown _serial = Number(-999_999_999, 999_999_999, 1, "0");
    readonly NumericUpDown _step = Number(-1_000_000, 1_000_000, 1, "0");
    readonly CheckBox _hasEnd = new() { Content = "Stop after" };
    readonly NumericUpDown _end = Number(-999_999_999, 999_999_999, 1, "0");
    readonly CheckBox _advance = new() { Content = "After each finished run, go on to the next serial number and spreadsheet row" };
    readonly TextBlock _csvInfo = Hint();
    readonly NumericUpDown _row = Number(1, 1, 1, "0");
    readonly StackPanel _rowPreview = new() { Spacing = 2 };
    readonly StackPanel _fields = new() { Spacing = 2 };
    readonly Button _removeCsv = new() { Content = "Remove" };
    VariableData _v;

    /// <summary>The new settings, or null if cancelled.</summary>
    public VariableData? Result { get; private set; }

    public VariablesWindow(VariableData current)
    {
        _v = current;
        Title = "Variables: serial numbers and spreadsheet";
        Width = 560;
        Height = 700;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");

        _serial.Value = current.Serial;
        _step.Value = current.SerialStep;
        _hasEnd.IsChecked = current.SerialEnd is not null;
        _end.Value = current.SerialEnd ?? current.Serial + 99;
        _end.IsEnabled = current.SerialEnd is not null;
        _advance.IsChecked = current.AdvanceAfterRun;
        _hasEnd.IsCheckedChanged += (_, _) => _end.IsEnabled = _hasEnd.IsChecked == true;
        ToolTip.SetTip(_serial, "The number the next run marks wherever {serial} is used.");
        ToolTip.SetTip(_step, "Added after each run (1 counts up, −1 counts down, 10 skips).");
        ToolTip.SetTip(_end, "The last number to mark: past it, the job won't send, so nothing gets a duplicate.");
        _row.ValueChanged += (_, _) => ShowRow();

        var load = Button("Load CSV…", tip: "A spreadsheet saved as CSV (comma, semicolon or tab separated). The first row must be the headings: each becomes a field, e.g. {Name}.");
        load.Click += async (_, _) => await LoadCsv();
        _removeCsv.Click += (_, _) => { _v = _v with { CsvName = "", CsvText = "", CsvRow = 0 }; ShowCsv(); };

        var ok = new Button { Content = "OK", Classes = { "primary" }, IsDefault = true, MinWidth = 100 };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 100 };
        ok.Click += (_, _) => { Result = Values(); Close(); };
        cancel.Click += (_, _) => Close();

        var body = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap,
                    Text = "Type fields into any text or code (QR, barcode): they're filled in on the work area and when the job is sent. " +
                           "Use “Insert field” in the text's settings, or type them, e.g. Part {serial:0000} made {date}.",
                },
                Card("Serial number",
                    Row(Field("Next number", _serial), Field("Step", _step)),
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _hasEnd, _end } },
                    _advance,
                    Hint("Moving on happens when the machine reports the run finished. Ctrl+Z puts it back if a run went wrong.")),
                Card("Spreadsheet",
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { load, _removeCsv } },
                    _csvInfo,
                    Field("Row to mark next", _row),
                    _rowPreview),
                Card("Fields you can use", _fields),
            },
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = new(0, 10, 0, 0), Children = { cancel, ok } };
        var dock = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Bottom);
        dock.Children.Add(buttons);
        dock.Children.Add(new ScrollViewer { Content = body });
        Content = new Border { Padding = new Thickness(16), Child = dock };
        ShowCsv();
    }

    VariableData Values() => _v with
    {
        Serial = (long)(_serial.Value ?? 1),
        SerialStep = (long)(_step.Value ?? 1),
        SerialEnd = _hasEnd.IsChecked == true ? (long)(_end.Value ?? 0) : null,
        AdvanceAfterRun = _advance.IsChecked == true,
        CsvRow = _v.HasCsv ? (int)(_row.Value ?? 1) - 1 : 0,
    };

    async Task LoadCsv()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Spreadsheet (CSV)",
            FileTypeFilter = [new FilePickerFileType("CSV spreadsheet") { Patterns = ["*.csv", "*.txt", "*.tsv"] }],
        });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path) return;
        string text;
        try { text = await File.ReadAllTextAsync(path); }
        catch (IOException e) { _csvInfo.Text = $"Couldn't read it: {e.Message}"; return; }
        if (text.Length > 5_000_000) { _csvInfo.Text = "That file is too big for a spreadsheet of fields (over 5 MB)."; return; }
        _v = _v with { CsvName = Path.GetFileName(path), CsvText = text, CsvRow = 0 };
        ShowCsv();
    }

    void ShowCsv()
    {
        var t = _v.Table;
        _removeCsv.IsEnabled = _v.HasCsv;
        _row.IsEnabled = _v.HasCsv && t.Rows.Length > 0;
        _row.Maximum = Math.Max(1, t.Rows.Length + 1); // one past the end = "all done"
        _row.Value = Math.Clamp(_v.CsvRow + 1, 1, (int)_row.Maximum);
        _csvInfo.Text = !_v.HasCsv
            ? "None loaded. Load one to mark a different name, number or address on each run."
            : $"{_v.CsvName}: {t.Rows.Length} row{(t.Rows.Length == 1 ? "" : "s")}, headings {string.Join(", ", t.Headers.Select(h => "{" + h.Trim() + "}"))}.";
        _fields.Children.Clear();
        foreach (var (field, meaning) in VariableText.Builtins.Concat(t.Headers.Select(h => ("{" + h.Trim() + "}", "Spreadsheet column"))))
            _fields.Children.Add(new Grid
            {
                ColumnDefinitions = new("150,*"),
                Children =
                {
                    new SelectableTextBlock { Text = field, FontFamily = new FontFamily("Consolas, Menlo, monospace") },
                    new TextBlock { Text = meaning, Classes = { "muted" }, [Grid.ColumnProperty] = 1 },
                },
            });
        ShowRow();
    }

    void ShowRow()
    {
        _rowPreview.Children.Clear();
        var t = _v.Table;
        if (!_v.HasCsv) return;
        int r = (int)(_row.Value ?? 1) - 1;
        if (r >= t.Rows.Length)
        {
            _rowPreview.Children.Add(Hint("Past the last row: the job won't send until you pick a row."));
            return;
        }
        for (int c = 0; c < t.Headers.Length; c++)
            _rowPreview.Children.Add(new TextBlock { Text = $"{t.Headers[c].Trim()}: {t.Cell(r, c)}", FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis });
    }
}

/// <summary>The "Insert field" menu for a text box: the built-in fields and the spreadsheet's headings.</summary>
public static class FieldMenu
{
    public static Button Create(TextBox box, Func<VariableData> vars, Action? openVariables = null)
    {
        var b = Button("Insert field", "IcVariables", "Put a field at the cursor: a serial number, the date, or a spreadsheet column. It's filled in on the work area and when the job is sent.");
        var flyout = new MenuFlyout();
        b.Flyout = flyout;
        flyout.Opening += (_, _) =>
        {
            var items = new List<MenuItem>();
            foreach (var (field, meaning) in VariableText.Builtins) items.Add(Item($"{meaning}   {field}", field));
            var t = vars().Table;
            if (t.Headers.Length > 0)
                foreach (var h in t.Headers) items.Add(Item($"Spreadsheet: {h.Trim()}", "{" + h.Trim() + "}"));
            var list = new List<object>(items);
            if (openVariables is not null)
            {
                list.Add(new Separator());
                var more = new MenuItem { Header = "Serial number and spreadsheet…" };
                more.Click += (_, _) => openVariables();
                list.Add(more);
            }
            flyout.ItemsSource = list;
        };
        return b;

        MenuItem Item(string header, string field)
        {
            var m = new MenuItem { Header = header };
            m.Click += (_, _) =>
            {
                var text = box.Text ?? "";
                int at = Math.Clamp(box.CaretIndex, 0, text.Length);
                int selStart = Math.Min(box.SelectionStart, box.SelectionEnd), selEnd = Math.Max(box.SelectionStart, box.SelectionEnd);
                if (selEnd > selStart && selEnd <= text.Length) { text = text.Remove(selStart, selEnd - selStart); at = selStart; }
                box.Text = text.Insert(at, field);
                box.CaretIndex = at + field.Length;
                box.Focus();
            };
            return m;
        }
    }
}
