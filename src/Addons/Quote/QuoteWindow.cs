using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using PhotonCanvas.App;
using PhotonCanvas.App.Addons;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Export;
using static PhotonCanvas.App.UiKit;

namespace PhotonCanvas.Addon.Quote;

/// <summary>
/// What the design on screen costs to make and what to charge for it: material, laser time,
/// electricity, your time and extras, marked up into a price, copied as text or saved as a PDF quote.
/// </summary>
public sealed class QuoteWindow : Window
{
    readonly IAddonHost _host;
    readonly string _ratesPath;
    readonly QuoteRates _rates;
    QuoteJob _job;
    readonly string _jobJsonAtStart;
    readonly DocState _state;
    readonly string _design;
    readonly List<MaterialUse> _materials;
    double? _estimateS, _lastRunS;
    string? _buildError;
    bool _loading = true, _timeKnown;
    string? _savedPath;

    // This job.
    readonly TextBox _title = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly TextBox _customer = new() { Watermark = "Name, company or order", HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly NumericUpDown _runs = Number(1, 100000, 1, "0");
    readonly NumericUpDown _pieces = Number(1, 100000, 1, "0");
    readonly ComboBox _timeFrom = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly TextBlock _timeSays = Hint();

    // Material.
    readonly StackPanel _materialRows = new() { Spacing = 8 };
    readonly Dictionary<string, (NumericUpDown Price, NumericUpDown W, NumericUpDown H)> _sheetEditors = [];
    readonly CheckBox _customerMaterial = new() { Content = "The customer brings the material" };
    readonly CheckBox _wholeSheets = new() { Content = "Charge whole sheets" };
    readonly NumericUpDown _waste = Number(0, 300, 5, "0.#");

    // Extras.
    readonly StackPanel _extraRows = new() { Spacing = 6 };
    readonly List<(TextBox Name, NumericUpDown Amount, CheckBox EachRun)> _extras = [];

    // Rates.
    readonly NumericUpDown _machine = Money();
    readonly NumericUpDown _power = Number(0, 20000, 10, "0");
    readonly NumericUpDown _kwh = Number(0, 100, 0.01m, "0.00#");
    readonly NumericUpDown _labour = Money();
    readonly NumericUpDown _setup = Number(0, 10000, 1, "0.#");
    readonly NumericUpDown _handling = Number(0, 1000, 0.5m, "0.#");

    // Price.
    readonly NumericUpDown _markup = Number(-90, 1000, 5, "0.#");
    readonly NumericUpDown _minimum = Money();
    readonly NumericUpDown _roundTo = Number(0, 1000, 0.05m, "0.00");
    readonly TextBox _taxName = new() { Width = 90 };
    readonly NumericUpDown _tax = Number(0, 100, 0.25m, "0.##");
    readonly TextBox _currency = new() { Width = 60, MaxLength = 4 };

    // On the quote.
    readonly TextBox _business = new() { Watermark = "Your business name", HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly TextBox _contact = new() { Watermark = "Email, phone, website (one per line)", AcceptsReturn = true, MinHeight = 56, TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly TextBox _note = new() { AcceptsReturn = true, MinHeight = 44, TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly CheckBox _breakdown = new() { Content = "Show what the price is made of" };
    readonly ComboBox _paper = new() { ItemsSource = new[] { "US Letter", "A4" }, HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly NumericUpDown _number = Number(1, 999999, 1, "0");

    // The answer.
    readonly TextBlock _total = new() { FontSize = 28, FontWeight = FontWeight.SemiBold };
    readonly TextBlock _perPiece = new() { Classes = { "muted" } };
    readonly Grid _costGrid = new() { ColumnDefinitions = new("*,Auto"), RowSpacing = 4, ColumnSpacing = 10 };
    readonly TextBlock _profit = new() { TextWrapping = TextWrapping.Wrap };
    readonly TextBlock _notes = new() { TextWrapping = TextWrapping.Wrap, Classes = { "hint" } };
    readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Classes = { "hint" } };
    readonly Button _open = new() { Content = "Open it", IsVisible = false };
    readonly Button _copy = new() { Content = "Copy as text", HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
    readonly Button _save = new() { Content = "Save quote…", Classes = { "primary" }, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
    QuoteResult? _result;

    static NumericUpDown Money() => Number(0, 1000000, 0.5m, "0.00");

    public QuoteWindow(IAddonHost host)
    {
        _host = host;
        _ratesPath = Path.Combine(host.DataFolder, "rates.json");
        _rates = QuoteRates.Load(_ratesPath);
        _state = host.Document.State;
        _job = QuoteJob.From(_state);
        _jobJsonAtStart = _job.ToJson();
        _design = host.Document.FilePath is { } f ? Path.GetFileNameWithoutExtension(f) : "Untitled";
        _materials = QuoteMath.Materials(_state);

        Title = "Quote and cost";
        Width = 940;
        Height = 720;
        MinWidth = 760;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");

        Load();
        Content = Layout();
        Wire();
        _loading = false;
        Recompute();
        Opened += async (_, _) => await WorkOutTime();
        Closing += (_, _) => KeepEverything();
    }

    // ---- Building the window ----

    Control Layout()
    {
        _title.Watermark = _design;
        ToolTip.SetTip(_title, "What the quote calls the job. Empty: the design's file name.");
        _timeFrom.ItemsSource = new[] { "Working it out…" };
        _timeFrom.SelectedIndex = 0;

        var job = Explained("This job", $"The page on screen: {Describe(_materials)}.",
            Field("Job", _title),
            Field("Customer", _customer),
            Field("Runs", _runs, tip: "How many times the job is run on the machine (one run = the whole page once)."),
            Field("Pieces a run", _pieces, tip: "How many finished pieces one run makes, for the price of one piece."),
            Field("Time from", _timeFrom, _timeSays, "Where a run's time comes from: the estimate (corrected by how long your past jobs really took), or how long this design really took the last time it finished."));

        BuildMaterialRows();
        var material = Explained("Material", "What a whole sheet costs and how big it is. Each material keeps its price for every quote. The part used is the box around its parts, plus the waste.",
            _materialRows,
            Field("Waste", _waste, tip: "Added on top of the material that's used, %: offcuts, the edges, a spoiled piece now and then."),
            _wholeSheets, _customerMaterial);

        var add = new Button { Content = "Add a line" };
        add.Click += (_, _) => { AddExtraRow(new ExtraLine("", 0, false)); Recompute(); };
        var extras = Explained("Extras", "Anything else to charge: a box, a stand, masking, design work. Tick each run for things every run needs.", _extraRows, add);

        var rates = Explained("Your rates", "The same for every quote.",
            Field("Machine / hour", _machine, tip: "What running the machine costs you per hour of laser time: its wear, the laser source's life, paying it off."),
            Field("Power draw (W)", _power, tip: "What the machine, the exhaust and the purifier draw together while a job runs. 350 W is a rough guess; a plug-in power meter tells you."),
            Field("Price / kWh", _kwh, tip: "What you pay for electricity, per kWh (from your bill)."),
            Field("Your time / hour", _labour),
            Field("Set-up (min)", _setup, tip: "Your time once per order: the design, a test piece, talking to the customer."),
            Field("Each run (min)", _handling, tip: "Your time each run: putting the material in, taking the pieces out, cleaning them up."));

        var taxRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { _taxName, _tax, new TextBlock { Text = "%", VerticalAlignment = VerticalAlignment.Center } } };
        var price = Explained("Price", "The costs, plus the markup, at least the minimum, rounded up; then the tax.",
            Field("Markup (%)", _markup, tip: "Added on top of everything the job costs you."),
            Field("Minimum", _minimum, tip: "The least you charge for an order. 0 = no minimum."),
            Field("Round up to", _roundTo, tip: "The price is rounded up to a multiple of this, e.g. 0.50 or 1. 0 = to the cent."),
            Field("Tax", taxRow, tip: "Its name on the quote and its rate. 0 = no tax line."),
            Field("Currency", _currency, tip: "The symbol in front of every amount. Nothing is converted."));

        var paper = Explained("On the quote", "Your details and the small print, for every quote.",
            Field("Business", _business),
            Field("Contact", _contact),
            Field("Note", _note, tip: "Printed at the foot of the quote: how long it's valid, payment, delivery."),
            _breakdown,
            Field("Paper", _paper),
            Field("Next number", _number, tip: "The number the next saved quote gets (Q-0001, Q-0002…). It goes up each time you save one."));

        var left = new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Content = new StackPanel { Spacing = 12, Margin = new Thickness(16, 16, 12, 16), Children = { job, material, extras, rates, price, paper } },
        };

        var close = new Button { Content = "Close", IsCancel = true, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
        close.Click += (_, _) => Close();
        ToolTip.SetTip(_copy, "The quote as text, ready to paste into an email or a message.");
        ToolTip.SetTip(_save, "Save the quote as a one-page PDF to print or send.");
        _copy.Click += async (_, _) => await CopyText();
        _save.Click += async (_, _) => await SavePdf();
        _open.Click += (_, _) => OpenSaved();
        var right = new Border
        {
            Background = Brush("Bg2"),
            BorderBrush = Brush("Line"),
            BorderThickness = new Thickness(1, 0, 0, 0),
            Padding = new Thickness(16),
            Child = new DockPanel
            {
                LastChildFill = true,
                Children =
                {
                    new StackPanel { Spacing = 8, Children = { _copy, _save, close } }.With(p => DockPanel.SetDock(p, Dock.Bottom)),
                    new ScrollViewer
                    {
                        Content = new StackPanel
                        {
                            Spacing = 8,
                            Children =
                            {
                                Muted("PRICE TO CHARGE"),
                                _total,
                                _perPiece,
                                new Border { Height = 1, Background = Brush("Line"), Margin = new Thickness(0, 6) },
                                Muted("WHAT IT COSTS YOU"),
                                _costGrid,
                                _profit,
                                _notes,
                                _status,
                                _open,
                            },
                        },
                    },
                },
            },
        };
        var grid = new Grid { ColumnDefinitions = new("*,330") };
        grid.Children.Add(left);
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        return grid;
    }

    static string Describe(IReadOnlyList<MaterialUse> materials) => materials.Count == 0
        ? "nothing on it goes to the laser yet"
        : ListWords(materials.Select(m => $"{m.Label} ({m.WidthMm:0} × {m.HeightMm:0} mm)"));

    void BuildMaterialRows()
    {
        if (_materials.Count == 0) _materialRows.Children.Add(Hint("No material: nothing on the page goes to the laser."));
        foreach (var m in _materials)
        {
            var have = _rates.Sheets.GetValueOrDefault(m.Key) ?? new SheetPrice(0, 300, 300);
            var price = Money().With(n => n.Value = (decimal)have.Price);
            var w = Number(1, 10000, 10, "0.#").With(n => { n.Value = (decimal)Math.Max(1, have.WidthMm); n.Width = 130; });
            var h = Number(1, 10000, 10, "0.#").With(n => { n.Value = (decimal)Math.Max(1, have.HeightMm); n.Width = 130; });
            ToolTip.SetTip(price, $"What one sheet of {m.Label} costs you.");
            ToolTip.SetTip(w, "Sheet width, mm");
            ToolTip.SetTip(h, "Sheet height, mm");
            _sheetEditors[m.Key] = (price, w, h);
            var size = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children = { w, new TextBlock { Text = "×", VerticalAlignment = VerticalAlignment.Center }, h, new TextBlock { Text = "mm", VerticalAlignment = VerticalAlignment.Center } },
            };
            _materialRows.Children.Add(new StackPanel
            {
                Spacing = 4,
                Children =
                {
                    new TextBlock { Text = m.Key == QuoteMath.OtherKey ? "Material (layers with no library material)" : m.Label, FontWeight = FontWeight.SemiBold },
                    Field("Sheet price", price),
                    Field("Sheet size", size),
                },
            });
        }
    }

    void AddExtraRow(ExtraLine e)
    {
        var name = new TextBox { Text = e.Name, Watermark = "e.g. Gift box", HorizontalAlignment = HorizontalAlignment.Stretch };
        var amount = Money().With(n => { n.Value = (decimal)Math.Max(0, e.Amount); n.Width = 120; });
        var each = new CheckBox { Content = "each run", IsChecked = e.EachRun };
        var remove = new Button { Content = "✕", Padding = new Thickness(8, 4) };
        ToolTip.SetTip(remove, "Take this line off");
        var row = new Grid { ColumnDefinitions = new("*,Auto,Auto,Auto"), ColumnSpacing = 6 };
        foreach (var (c, i) in new Control[] { name, amount, each, remove }.Select((c, i) => (c, i)))
        {
            Grid.SetColumn(c, i);
            row.Children.Add(c);
        }
        var entry = (name, amount, each);
        _extras.Add(entry);
        _extraRows.Children.Add(row);
        name.TextChanged += (_, _) => Recompute();
        amount.ValueChanged += (_, _) => Recompute();
        each.IsCheckedChanged += (_, _) => Recompute();
        remove.Click += (_, _) =>
        {
            _extras.Remove(entry);
            _extraRows.Children.Remove(row);
            Recompute();
        };
    }

    // ---- Settings in and out ----

    void Load()
    {
        _title.Text = _job.Title;
        _customer.Text = _job.Customer;
        _runs.Value = Math.Max(1, _job.Runs);
        _pieces.Value = Math.Max(1, _job.PiecesPerRun);
        _customerMaterial.IsChecked = _job.CustomerMaterial;
        _wholeSheets.IsChecked = _job.WholeSheets;
        foreach (var e in _job.Extras) AddExtraRow(e);

        _waste.Value = (decimal)_rates.WastePct;
        _machine.Value = (decimal)_rates.MachinePerHour;
        _power.Value = (decimal)_rates.PowerW;
        _kwh.Value = (decimal)_rates.PerKwh;
        _labour.Value = (decimal)_rates.LabourPerHour;
        _setup.Value = (decimal)_rates.SetupMin;
        _handling.Value = (decimal)_rates.HandlingMin;
        _markup.Value = (decimal)_rates.MarkupPct;
        _minimum.Value = (decimal)_rates.Minimum;
        _roundTo.Value = (decimal)_rates.RoundTo;
        _taxName.Text = _rates.TaxName;
        _tax.Value = (decimal)_rates.TaxPct;
        _currency.Text = _rates.Currency;
        _business.Text = _rates.Business;
        _contact.Text = _rates.Contact;
        _note.Text = _rates.Note;
        _breakdown.IsChecked = _rates.ShowBreakdown;
        _paper.SelectedIndex = _rates.Letter ? 0 : 1;
        _number.Value = Math.Max(1, _rates.NextNumber);
    }

    void Wire()
    {
        foreach (var n in new[] { _runs, _pieces, _waste, _machine, _power, _kwh, _labour, _setup, _handling, _markup, _minimum, _roundTo, _tax, _number })
            n.ValueChanged += (_, _) => Recompute();
        foreach (var (p, w, h) in _sheetEditors.Values)
            foreach (var n in new[] { p, w, h }) n.ValueChanged += (_, _) => Recompute();
        foreach (var t in new[] { _title, _customer, _taxName, _currency, _business, _contact, _note })
            t.TextChanged += (_, _) => Recompute();
        foreach (var c in new[] { _customerMaterial, _wholeSheets, _breakdown })
            c.IsCheckedChanged += (_, _) => Recompute();
        _customerMaterial.IsCheckedChanged += (_, _) => { foreach (var (p, w, h) in _sheetEditors.Values) p.IsEnabled = w.IsEnabled = h.IsEnabled = _customerMaterial.IsChecked != true; };
        _timeFrom.SelectionChanged += (_, _) => Recompute();
        _paper.SelectionChanged += (_, _) => Recompute();
    }

    static double V(NumericUpDown n, double fallback = 0) => n.Value is { } v ? (double)v : fallback;

    /// <summary>The controls into the rates and this job's quote.</summary>
    void Read()
    {
        _rates.WastePct = V(_waste);
        _rates.MachinePerHour = V(_machine);
        _rates.PowerW = V(_power);
        _rates.PerKwh = V(_kwh);
        _rates.LabourPerHour = V(_labour);
        _rates.SetupMin = V(_setup);
        _rates.HandlingMin = V(_handling);
        _rates.MarkupPct = V(_markup);
        _rates.Minimum = V(_minimum);
        _rates.RoundTo = V(_roundTo);
        _rates.TaxName = _taxName.Text ?? "";
        _rates.TaxPct = V(_tax);
        _rates.Currency = _currency.Text ?? "";
        _rates.Business = _business.Text ?? "";
        _rates.Contact = _contact.Text ?? "";
        _rates.Note = _note.Text ?? "";
        _rates.ShowBreakdown = _breakdown.IsChecked == true;
        _rates.Letter = _paper.SelectedIndex != 1;
        _rates.NextNumber = (int)V(_number, 1);
        foreach (var (key, (p, w, h)) in _sheetEditors)
            _rates.Sheets[key] = new SheetPrice(V(p), V(w, 300), V(h, 300));

        _job = _job with
        {
            Title = _title.Text?.Trim() ?? "",
            Customer = _customer.Text ?? "",
            Runs = (int)V(_runs, 1),
            PiecesPerRun = (int)V(_pieces, 1),
            CustomerMaterial = _customerMaterial.IsChecked == true,
            WholeSheets = _wholeSheets.IsChecked == true,
            Time = !_timeKnown ? _job.Time : _timeFrom.SelectedIndex == 1 ? TimeSource.LastRun : TimeSource.Estimate,
            Extras = [.. _extras.Select(x => new ExtraLine(x.Name.Text ?? "", V(x.Amount), x.EachRun.IsChecked == true))],
        };
    }

    string JobTitle => _job.Title.Length > 0 ? _job.Title : _design;

    double? RunSeconds => _job.Time == TimeSource.LastRun && _lastRunS is { } real ? real : _estimateS;

    // ---- Working it out ----

    /// <summary>Builds the job once (as Ship It would, nothing is sent) for its time, and looks for a real run of it.</summary>
    async Task WorkOutTime()
    {
        var state = _state;
        var opt = JobSettings.From(_host.AppSettings);
        try
        {
            var job = await Task.Run(() => JobBuilder.Build(state, opt));
            // What past jobs showed: real time over the estimate (see the job bar).
            double scale = _host.AppSettings.JobTimeModel == 2 ? Math.Clamp(_host.AppSettings.JobTimeScale, 0.3, 5) : 1;
            _estimateS = job.EstimateS * scale;
            _lastRunS = await Task.Run(() => QuoteMath.LastRun(JobHistory.All(), _design, job.EstimateS));
        }
        catch (JobException e) { _buildError = e.Message; }
        catch (Exception e) when (e is OperationCanceledException or ArgumentException or InvalidOperationException) { _buildError = e.Message; }

        _loading = true;
        var items = new List<string> { _estimateS is { } est ? $"The estimate: {Duration(est)} a run" : "The estimate: not known" };
        if (_lastRunS is { } last) items.Add($"Last real run: {Duration(last)}");
        _timeFrom.ItemsSource = items;
        _timeFrom.SelectedIndex = _job.Time == TimeSource.LastRun && _lastRunS is not null ? 1 : 0;
        _timeSays.Text = _buildError is { } err ? $"The job can't be made yet, so its time isn't known: {err}"
            : _lastRunS is null ? "From the estimate, corrected by how long your past jobs really took. Once this design has run to the end, its real time can be used."
            : "This design has run before: its real time can be used.";
        _loading = false;
        _timeKnown = true;
        Recompute();
    }

    void Recompute()
    {
        if (_loading) return;
        Read();
        _result = QuoteMath.Price(RunSeconds ?? 0, _materials, _job, _rates, JobTitle);
        var q = _result;
        string Money(double v) => QuoteMath.Format(v, _rates.Currency);

        _total.Text = Money(q.Total);
        _perPiece.Text = (q.Tax > 0 ? $"{Money(q.Subtotal)} + {Money(q.Tax)} {(_rates.TaxName.Trim().Length > 0 ? _rates.TaxName.Trim().ToLowerInvariant() : "tax")}" : "")
            + (q.Pieces > 1 ? $"{(q.Tax > 0 ? " · " : "")}{Money(q.PerPiece)} a piece ({q.Pieces} pieces)" : "");
        _perPiece.IsVisible = _perPiece.Text.Length > 0;

        _costGrid.Children.Clear();
        _costGrid.RowDefinitions.Clear();
        int row = 0;
        void Line(string name, string detail, string amount, bool strong = false)
        {
            _costGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var left = new StackPanel { Children = { new TextBlock { Text = name, FontWeight = strong ? FontWeight.SemiBold : FontWeight.Normal, TextWrapping = TextWrapping.Wrap } } };
            if (detail.Length > 0) left.Children.Add(new TextBlock { Text = detail, Classes = { "hint" }, TextWrapping = TextWrapping.Wrap });
            var right = new TextBlock { Text = amount, FontWeight = strong ? FontWeight.SemiBold : FontWeight.Normal, HorizontalAlignment = HorizontalAlignment.Right };
            Grid.SetRow(left, row);
            Grid.SetRow(right, row);
            Grid.SetColumn(right, 1);
            _costGrid.Children.Add(left);
            _costGrid.Children.Add(right);
            row++;
        }
        foreach (var c in q.Costs) Line(c.Name, c.Detail, Money(c.Amount));
        Line("Costs", "", Money(q.Cost), strong: true);

        double perHour = q.LaserHours > 0 ? q.Profit / q.LaserHours : 0;
        _profit.Text = RunSeconds is null
            ? "The laser time isn't known yet, so it isn't in the price."
            : $"That leaves {Money(q.Profit)} over the costs" + (q.LaserHours > 0.01 ? $": {Money(perHour)} per hour of laser time." : ".");
        _notes.Text = string.Join("\n", q.Notes);
        _notes.IsVisible = q.Notes.Count > 0;
        _copy.IsEnabled = _save.IsEnabled = _materials.Count > 0 || _job.Extras.Count > 0;
    }

    // ---- Out ----

    byte[]? Picture()
    {
        // Only what goes to the laser, small enough for a page.
        var s = _state with { Layers = [.. _state.Layers.Select(l => l.Output ? l : l with { Visible = false })] };
        var box = JobBuilder.OutputLayers(s).SelectMany(s.SentFor).Select(sh => sh.Bounds).Where(b => !b.IsEmpty).Aggregate(Rect2.Empty, (a, b) => a.Union(b));
        if (box.IsEmpty) return null;
        double dpi = Math.Clamp(900 / (Math.Max(box.Width, box.Height) / 25.4), 10, 200);
        try { return DesignExport.Png(s, dpi); }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException) { return null; }
    }

    async Task CopyText()
    {
        Recompute();
        if (_result is not { } q || Clipboard is not { } cb) return;
        await cb.SetTextAsync(QuoteMath.Text(q, _job, _rates, JobTitle, _rates.NextNumber, DateTime.Today));
        KeepRates();
        _status.Text = "Copied. Paste it into an email or a message.";
    }

    async Task SavePdf()
    {
        Recompute();
        if (_result is not { } q) return;
        int number = _rates.NextNumber;
        string name = string.Concat($"Quote {QuoteMath.NumberText(number)} {JobTitle}".Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save the quote",
            DefaultExtension = "pdf",
            SuggestedFileName = name + ".pdf",
            FileTypeChoices = [new FilePickerFileType("PDF") { Patterns = ["*.pdf"] }],
        });
        if (file?.TryGetLocalPath() is not { } path) return;
        var paper = new QuotePaper(JobTitle, number, DateTime.Today, Picture());
        try
        {
            await Task.Run(() => QuotePdf.Save(path, q, _job, _rates, paper));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _status.Text = $"Couldn't save the quote (is it open in a PDF viewer?): {e.Message}";
            return;
        }
        _rates.NextNumber = number + 1;
        _loading = true;
        _number.Value = _rates.NextNumber;
        _loading = false;
        KeepRates();
        _savedPath = path;
        _open.IsVisible = true;
        _status.Text = $"Quote {QuoteMath.NumberText(number)} saved: {path}";
        _host.Status($"Quote {QuoteMath.NumberText(number)} saved");
    }

    void OpenSaved()
    {
        if (_savedPath is null) return;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_savedPath) { UseShellExecute = true })?.Dispose(); }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or PlatformNotSupportedException) { _status.Text = $"Couldn't open it: {e.Message}"; }
    }

    void KeepRates()
    {
        try { _rates.Save(_ratesPath); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _status.Text = $"Couldn't save your rates: {e.Message}"; }
    }

    /// <summary>On closing: the rates for next time, and this job's quote in the design (one undo step, only when it changed).</summary>
    void KeepEverything()
    {
        Read();
        KeepRates();
        var json = _job.ToJson();
        if (json == _jobJsonAtStart || !ReferenceEquals(_host.Document.State, _state)) return;
        var s = _host.Document.State;
        _host.Document.Commit("Quote details", s with { AddonData = s.AddonData.SetItem(QuoteJob.Key, json) }, _host.Document.Selection);
    }
}
