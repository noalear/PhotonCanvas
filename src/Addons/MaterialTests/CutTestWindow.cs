using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Materials;
using static PhotonCanvas.App.UiKit;

using PhotonCanvas.App;

namespace PhotonCanvas.Addon.MaterialTests;

/// <summary>
/// Finding the cut: a grid of small squares at speeds across and passes (or power) down; click the
/// ones that dropped out and the quickest of them is the setting. Then a kerf and fit test at that
/// setting: a square to measure (the kerf) and a comb of slots to slide it into (the fit for slotted
/// builds). Both go into the material library.
/// </summary>
public sealed class CutTestWindow : Window
{
    readonly Document _doc;
    readonly MaterialLibrary _lib;
    readonly MachineProfile _machine;
    readonly Func<CameraPhoto?> _photo;
    readonly Func<Task> _updatePhoto;

    // Making the test
    readonly ComboBox _light = new() { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = new[] { "Diode (blue)", "MOPA (red)" }, SelectedIndex = 0 };
    readonly NumericUpDown _sMin = Number(1, 15000, 1, "0.#"), _sMax = Number(1, 15000, 1, "0.#"), _sSteps = Number(1, 12, 1, "0");
    readonly RadioButton _byPasses = new() { Content = "More passes on each row", GroupName = "cutdown", IsChecked = true };
    readonly RadioButton _byPower = new() { Content = "More power on each row", GroupName = "cutdown" };
    readonly NumericUpDown _dMin = Number(1, 100, 1, "0.#"), _dMax = Number(1, 100, 1, "0.#"), _dSteps = Number(1, 10, 1, "0");
    readonly NumericUpDown _power = Number(1, 100, 5, "0.#"), _passes = Number(1, 50, 1, "0"), _cell = Number(4, 20, 1, "0.#");
    readonly TextBlock _dMinLabel = Muted(""), _dMaxLabel = Muted("");
    readonly Control _powerCol, _stepsCol, _passesCol;
    readonly ContentControl _third = new();
    readonly CutTestPreview _preview = new() { Margin = new(4) };
    readonly TextBlock _speedList = Hint(), _downList = Hint();
    readonly TextBlock _plan = new() { TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.SemiBold };
    readonly Button _add = new() { Content = "Add the test to the design", Classes = { "primary" }, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
    // Reading the results
    readonly ComboBox _material = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly TextBox _newName = new() { Watermark = "e.g. Basswood" };
    readonly NumericUpDown _thick = Number(0.1m, 100, 0.1m, "0.##");
    readonly Grid _cells = new();
    readonly HashSet<Guid> _through = [];
    // What was ticked and picked, kept while the program runs: the window opens again after the kerf test
    // is burned, and the squares shouldn't need clicking again (the test's layers are new each time, so
    // another test's squares never match).
    static readonly HashSet<Guid> Ticked = [];
    static Guid? Picked;
    readonly ComboBox _use = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    List<Layer> _useChoices = [];
    readonly Button _kerfTest = new() { Content = "Kerf and fit test at this setting…", HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center, IsEnabled = false };
    readonly StackPanel _kerfPanel = new() { Spacing = 8 };
    readonly TextBlock _kerfAbout = new() { TextWrapping = TextWrapping.Wrap };
    readonly NumericUpDown _measured = Number(0, 200, 0.01m, "0.00");
    readonly ComboBox _snug = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly TextBlock _result = new() { TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.SemiBold };
    readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    readonly Button _save = new() { Content = "Save to the library", Classes = { "primary" }, IsEnabled = false };
    static readonly KerfTestOptions KerfDefaults = new();

    public CutTestWindow(Document doc, MaterialLibrary lib, MachineProfile machine, Func<CameraPhoto?> photo, Func<Task> updatePhoto, bool showResults)
    {
        _doc = doc;
        _lib = lib;
        _machine = machine;
        _photo = photo;
        _updatePhoto = updatePhoto;
        Title = "Cut test";
        Width = 780;
        Height = 780;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");
        _sMin.Value = 4; _sMax.Value = 24; _sSteps.Value = 6;
        _dMin.Value = 1; _dMax.Value = 3; _dSteps.Value = 3;
        _power.Value = 100; _passes.Value = 1; _cell.Value = 8; _thick.Value = 3;
        foreach (var n in new[] { _sMin, _sMax, _sSteps, _dMin, _dMax, _dSteps, _power, _passes, _cell }) n.ValueChanged += (_, _) => Plan();
        _light.SelectionChanged += (_, _) => Plan();
        void Switch(bool power)
        {
            // Sensible ranges for what the rows try (the other button may not have let go yet, so it's passed in).
            if (power) { _dMin.Value = 60; _dMax.Value = 100; _dSteps.Value = 5; }
            else { _dMin.Value = 1; _dMax.Value = 3; }
            Plan();
        }
        _byPasses.IsCheckedChanged += (_, _) => { if (_byPasses.IsChecked == true) Switch(false); };
        _byPower.IsCheckedChanged += (_, _) => { if (_byPower.IsChecked == true) Switch(true); };
        _powerCol = Labelled("Power for every square (%)", _power);
        _stepsCol = Labelled("How many", _dSteps);
        _passesCol = Labelled("Passes for every square", _passes);

        var add = _add;
        add.Click += (_, _) => AddTest();
        var make = new StackPanel
        {
            Spacing = 12, Margin = new(0, 12, 0, 0),
            Children =
            {
                new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap,
                    Text = "This cuts a grid of little squares, each with different settings. Afterwards, the squares that fell out (or push out with a finger) were cut right through. The quickest of those is your cut setting.",
                },
                Field("Laser", _light),
                new Border { Background = Brush("Bg0"), CornerRadius = new(8), Padding = new(10), Child = _preview },
                Explained("Across: speed", "Each column is cut at a different speed. Slower cuts deeper.",
                    Row(Labelled("Slowest (mm/s)", _sMin), Labelled("Fastest (mm/s)", _sMax), Labelled("How many speeds", _sSteps)), _speedList),
                Explained("Down: passes or power", "Each row adds more passes (or more power) to go deeper.",
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, Children = { _byPasses, _byPower } },
                    Row(Labelled(_dMinLabel, _dMin), Labelled(_dMaxLabel, _dMax), _third),
                    _passesCol, _downList),
                Field("Square size (mm)", _cell, tip: "Big enough to see whether it dropped, small enough not to tip up into the head once it's loose."),
                _plan,
                Hint("Cut on the honeycomb or pins (with air assist on, if you have it), and stay with it: slow cuts can flare up. The numbers are only lightly marked."),
                add,
                Hint("Adding the test closes this window so you can move it, frame it and send it. When the job finishes, this window opens again on Read the results (or open it with Laser → Cut test)."),
            },
        };

        _material.SelectionChanged += (_, _) => { _newName.IsVisible = _material.SelectedIndex == 0; if (_material.SelectedIndex > 0 && _lib.Materials[_material.SelectedIndex - 1].ThicknessMm is { } t) _thick.Value = (decimal)t; };
        _thick.ValueChanged += (_, _) => Compute();
        _through.UnionWith(Ticked);
        _use.SelectionChanged += (_, _) =>
        {
            if (Chosen is { } c) Picked = c.Id;
            Compute();
        };
        _measured.ValueChanged += (_, _) => Compute();
        _measured.Watermark = "e.g. 19.82";
        _snug.ItemsSource = KerfDefaults.Fits.Select(f => $"{CutTest.FitLabel(f)} (slot {f:+0.0#;−0.0#;0} mm on the thickness)").Prepend("Not tried yet").ToList();
        _snug.SelectedIndex = 0;
        _snug.SelectionChanged += (_, _) => Compute();
        var update = Button("Update photo", "IcCamera", "A fresh camera photo of the cut test, for the thumbnails.");
        update.Click += async (_, _) => { update.IsEnabled = false; try { await _updatePhoto(); } finally { update.IsEnabled = true; } BuildCells(); };
        _kerfTest.Click += (_, _) => AddKerfTest();
        _save.Click += (_, _) => Save();
        _kerfPanel.Children.Add(new TextBlock { Text = "Kerf and fit", FontWeight = FontWeight.SemiBold, Margin = new(0, 8, 0, 0) });
        _kerfPanel.Children.Add(_kerfAbout);
        _kerfPanel.Children.Add(Field("Square is (mm)", _measured, tip: "Across the square with calipers, a couple of places; the average. What it's short of the drawn size is the kerf."));
        _kerfPanel.Children.Add(Field("Snuggest slot", _snug, tip: "Slide the square (it's the same material) into each slot of the comb: the one it goes into with a firm push and stays."));
        var read = new StackPanel
        {
            Spacing = 8, Margin = new(0, 12, 0, 0),
            Children =
            {
                Field("Material", _material), _newName, Field("Thickness (mm)", _thick, tip: "Measured with calipers; it's kept with the settings."),
                Hint("Click every square that went right through (dropped out, or pushes out without tearing). The quickest of them is picked below; choose another if its edges were better."),
                Row(update),
                new ScrollViewer { Content = _cells, MaxHeight = 330, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto },
                Field("Use", _use),
                _kerfTest,
                _kerfPanel,
                _result, _status, _save,
            },
        };
        var tabs = new TabControl { Items = { new TabItem { Header = "Make the test", Content = new ScrollViewer { Content = make } }, new TabItem { Header = "Read the results", Content = new ScrollViewer { Content = read } } } };
        tabs.SelectedIndex = showResults ? 1 : 0;
        Content = new Border { Padding = new(16), Child = tabs };
        RefreshMaterials();
        Plan();
        BuildCells();
        Compute();
    }

    /// <summary>Rows of passes at most (more is spread out over the range).</summary>
    const int MaxRows = 10;

    /// <summary>Is there a cut test (or a kerf test) in the design to read?</summary>
    public static bool InDesign(DocState s) => s.Layers.Any(CutTest.IsCell) || CutTest.KerfLayer(s) is not null;

    CutTestOptions Options() => new()
    {
        Light = _light.SelectedIndex == 1 ? LightSource.Red : LightSource.Blue,
        SpeedMin = (double)(_sMin.Value ?? 4), SpeedMax = (double)(_sMax.Value ?? 24), SpeedSteps = (int)(_sSteps.Value ?? 6),
        Down = _byPower.IsChecked == true ? CutTestDown.Power : CutTestDown.Passes,
        DownMin = (double)(_dMin.Value ?? 1), DownMax = (double)(_dMax.Value ?? 3),
        // Passes are whole numbers: every one from the fewest to the most.
        DownSteps = _byPower.IsChecked == true ? (int)(_dSteps.Value ?? 5) : Math.Min(MaxRows, (int)Math.Abs((double)((_dMax.Value ?? 3) - (_dMin.Value ?? 1))) + 1),
        Power = (double)(_power.Value ?? 100), Passes = (int)(_passes.Value ?? 1),
        CellMm = (double)(_cell.Value ?? 8),
    };

    void Plan()
    {
        var o = Options();
        bool power = o.Down == CutTestDown.Power;
        // The third box: the power for every square (rows of passes), or how many powers (rows of power).
        _third.Content = power ? _stepsCol : _powerCol;
        _passesCol.IsVisible = power;
        _dMinLabel.Text = power ? "Lowest power (%)" : "Fewest passes";
        _dMaxLabel.Text = power ? "Highest power (%)" : "Most passes";
        var speeds = MaterialTest.Values(TestParam.Speed, o.SpeedMin, o.SpeedMax, o.SpeedSteps, _machine);
        var down = MaterialTest.Values(power ? TestParam.Power : TestParam.Passes, o.DownMin, o.DownMax, o.DownSteps, _machine);
        _speedList.Text = $"Tries {ListWords(speeds.Select(v => $"{v:0.#}"))} mm/s.";
        _downList.Text = power
            ? $"Tries {ListWords(down.Select(v => $"{v:0.#}"))}% power, {o.Passes} pass{(o.Passes == 1 ? "" : "es")} each."
            : $"Tries {ListWords(down.Select(v => $"{v:0}"))} pass{(down.Count == 1 && down[0] == 1 ? "" : "es")}, all at {o.Power:0.#}% power.";
        _preview.Show(speeds, down, power, o.Power, o.Passes);
        try
        {
            var (s, _) = CutTest.Add(DocState.New() with { Layers = [] }, o, _machine);
            var job = JobBuilder.Build(s, new JobOptions());
            int n = s.Layers.Count(CutTest.IsCell);
            var size = s.Bounds(s.Shapes.Select(x => x.Id).ToHashSet());
            bool fits = size.Width <= _machine.FieldW - 4 && size.Height <= _machine.FieldH - 4;
            _plan.Text = $"{n} squares · {size.Width:0} × {size.Height:0} mm of material · about {Duration(job.EstimateS)} to cut" +
                         (fits ? "" : $"\n⚠ That's bigger than the bed ({_machine.FieldW:0} × {_machine.FieldH:0} mm): try fewer speeds or rows, or smaller squares.");
            if (fits) _plan.ClearValue(TextBlock.ForegroundProperty); else _plan.Foreground = Brush("Warn");
            _add.IsEnabled = fits;
        }
        catch (Exception e) when (e is JobException or InvalidOperationException or OutsideWorkAreaException)
        {
            _plan.Text = e.Message;
            _add.IsEnabled = false;
        }
    }

    void AddTest()
    {
        var o = Options();
        var (probe, _) = CutTest.Add(DocState.New() with { Layers = [] }, o with { Origin = new Vec2(0, 0) }, _machine);
        var size = probe.Bounds(probe.Shapes.Select(x => x.Id).ToHashSet());
        var at = new Vec2(_machine.FieldW / 2 - size.Width / 2, _machine.FieldH / 2 - size.Height / 2);
        var (s, added) = CutTest.Add(_doc.State, o with { Origin = at }, _machine);
        _doc.Commit("Cut test", s, added);
        AppLog.Write("cut test", $"Test added: speeds {o.SpeedMin:0.#}–{o.SpeedMax:0.#} mm/s × {(o.Down == CutTestDown.Passes ? "passes" : "power")} {o.DownMin:0.#}–{o.DownMax:0.#}.");
        Close();
    }

    List<(Layer Layer, Rect2 Bounds)> Cells() =>
        [.. _doc.State.Layers.Where(CutTest.IsCell)
            .Select(l => (l, _doc.State.Bounds(_doc.State.ShapesOn(l.Id).Select(s => s.Id).ToHashSet())))
            .Where(x => !x.Item2.IsEmpty)];

    /// <summary>The squares laid out as on the bed: rows top to bottom, columns left to right.</summary>
    void BuildCells()
    {
        _cells.Children.Clear();
        _cells.RowDefinitions.Clear();
        _cells.ColumnDefinitions.Clear();
        var cells = Cells();
        if (cells.Count == 0)
        {
            _cells.Children.Add(Hint("There's no cut test in this design yet: make one on the first tab."));
            UseChoices();
            return;
        }
        double tol = cells.Min(c => c.Bounds.Height) / 2;
        var rows = Cluster(cells.Select(c => c.Bounds.Center.Y), tol);
        var cols = Cluster(cells.Select(c => c.Bounds.Center.X), cells.Min(c => c.Bounds.Width) / 2);
        foreach (var _ in rows) _cells.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        foreach (var _ in cols) _cells.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        var photo = _photo();
        foreach (var (layer, bounds) in cells)
        {
            Control face = new TextBlock { Text = $"{layer.Laser.Speed:0.#}\n{layer.Laser.Passes}× {layer.Laser.Power:0}%", TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontSize = 12 };
            if (photo is not null && MaterialLibrary.CropPhoto(photo.Image, photo.Area, photo.MmPerPx, bounds.Inflate(1.5), 120) is { } jpg)
            {
                try { using var ms = new MemoryStream(jpg); face = new Image { Source = new Avalonia.Media.Imaging.Bitmap(ms), Stretch = Stretch.UniformToFill }; }
                catch (ArgumentException) { }
            }
            bool through = _through.Contains(layer.Id);
            var b = new Button
            {
                Width = 78, Height = 78, Padding = new(2), Margin = new(3), Content = face,
                BorderThickness = new(3), BorderBrush = through ? Brush("Ok") : Brushes.Transparent,
                Opacity = _through.Count > 0 && !through ? 0.6 : 1,
            };
            ToolTip.SetTip(b, $"{CutTest.Describe(layer.Laser)}: click if it went through" + (through ? " (marked ✓)" : ""));
            var id = layer.Id;
            b.Click += (_, _) =>
            {
                if (_through.Remove(id)) Ticked.Remove(id);
                else { _through.Add(id); Ticked.Add(id); }
                BuildCells();
            };
            var cell = new StackPanel { Children = { b, new TextBlock { Text = (through ? "✓ " : "") + $"{layer.Laser.Speed:0.#} mm/s {layer.Laser.Passes}×", FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center } } };
            Grid.SetRow(cell, Nearest(rows, bounds.Center.Y));
            Grid.SetColumn(cell, Nearest(cols, bounds.Center.X));
            _cells.Children.Add(cell);
        }
        UseChoices();
    }

    static List<double> Cluster(IEnumerable<double> values, double tol)
    {
        var centres = new List<double>();
        foreach (var v in values.Order())
            if (centres.Count == 0 || v - centres[^1] > tol) centres.Add(v);
        return centres;
    }

    static int Nearest(List<double> centres, double v) => centres.IndexOf(centres.MinBy(c => Math.Abs(c - v)));

    /// <summary>The squares that went through, quickest first; the quickest picked.</summary>
    void UseChoices()
    {
        var keep = _use.SelectedIndex >= 0 && _use.SelectedIndex < _useChoices.Count ? _useChoices[_use.SelectedIndex].Id : Picked;
        _useChoices = [.. Cells().Select(c => c.Layer).Where(l => _through.Contains(l.Id)).OrderBy(l => CutTest.SecondsPerMm(l.Laser)).ThenBy(l => l.Laser.Power)];
        _use.ItemsSource = _useChoices.Count == 0 ? new[] { "Click the squares that went through" } :
            _useChoices.Select((l, i) => CutTest.Describe(l.Laser) + (i == 0 ? " (the quickest)" : "")).ToList();
        int at = keep is { } k ? _useChoices.FindIndex(l => l.Id == k) : -1;
        _use.SelectedIndex = at >= 0 ? at : 0;
        _use.IsEnabled = _useChoices.Count > 0;
        Compute();
    }

    Layer? Chosen => _use.SelectedIndex >= 0 && _use.SelectedIndex < _useChoices.Count ? _useChoices[_use.SelectedIndex] : null;

    void AddKerfTest()
    {
        if (Chosen is not { } chosen) return;
        var o = KerfDefaults with { Laser = chosen.Laser, ThicknessMm = (double)(_thick.Value ?? 3) };
        var (probe, _) = CutTest.AddKerf(DocState.New() with { Layers = [] }, o with { Origin = new Vec2(0, 0) });
        var size = probe.Bounds(probe.Shapes.Select(x => x.Id).ToHashSet());
        var s = _doc.State;
        // On fresh material just below the grid (or above it, if there's no room), else the middle of the bed.
        var grid = s.Bounds(s.Shapes.Where(x => s.Layers.FirstOrDefault(l => l.Id == x.LayerId) is { } l && (CutTest.IsCell(l) || l.Name == "Cut test labels")).Select(x => x.Id).ToHashSet());
        var at = new Vec2(_machine.FieldW / 2 - size.Width / 2, _machine.FieldH / 2 - size.Height / 2);
        if (!grid.IsEmpty)
        {
            double x = Math.Clamp(grid.MinX, 2, Math.Max(2, _machine.FieldW - size.Width - 2));
            if (grid.MaxY + 6 + size.Height <= _machine.FieldH - 2) at = new Vec2(x, grid.MaxY + 6);
            else if (grid.MinY - 6 - size.Height >= 2) at = new Vec2(x, grid.MinY - 6 - size.Height);
        }
        // One kerf test at a time: an older one goes.
        if (CutTest.KerfLayer(s) is { } old)
        {
            var labels = s.Layers.FirstOrDefault(l => l.Name == "Kerf test labels");
            var gone = new HashSet<Guid> { old.Id };
            if (labels is not null) gone.Add(labels.Id);
            s = Ops.DeleteLayers(s, gone, null);
        }
        var (next, added) = CutTest.AddKerf(s, o with { Origin = at });
        _doc.Commit("Kerf and fit test", next, added);
        AppLog.Write("cut test", $"Kerf test added at {CutTest.Describe(chosen.Laser)}, {o.ThicknessMm:0.##} mm.");
        Close();
    }

    void Compute()
    {
        var lines = new List<string>();
        var kerfLayer = CutTest.KerfLayer(_doc.State);
        _kerfTest.IsEnabled = Chosen is not null;
        _kerfPanel.IsVisible = kerfLayer is not null;
        double t = (double)(_thick.Value ?? 0);
        if (Chosen is { } c)
            lines.Add($"Cut: {CutTest.Describe(c.Laser)} ({Speed(c.Laser)}).");
        if (kerfLayer is not null)
        {
            _kerfAbout.Text = $"The kerf test was cut at {CutTest.Describe(kerfLayer.Laser)}: a {KerfDefaults.SquareMm:0} mm square and a comb of slots, each the thickness plus the fit marked under it.";
            if (_measured.Value is > 0 and var mv)
            {
                double kerf = CutTest.Kerf(KerfDefaults.SquareMm, (double)mv);
                lines.Add(kerf > 1
                    ? "⚠ That's more than 1 mm short: measure again (across the square, not corner to corner)."
                    : $"Kerf {kerf:0.00} mm: set it on the cut layer and parts come out the size they're drawn.");
                if (_snug.SelectedIndex > 0)
                {
                    double fit = KerfDefaults.Fits[_snug.SelectedIndex - 1];
                    lines.Add($"Slot fit for slotted builds: {CutTest.SlotFit(fit, kerf, true):+0.00;−0.00;0.00} mm with the cut layer's kerf at {kerf:0.00}; {CutTest.SlotFit(fit, kerf, false):+0.00;−0.00;0.00} mm with it at 0.");
                }
            }
        }
        _result.Text = string.Join("\n", lines);
        _save.IsEnabled = Chosen is not null || kerfLayer is not null && _measured.Value is > 0;
    }

    static string Speed(LaserSettings l) => $"{1 / CutTest.SecondsPerMm(l):0.#} mm of cut a second";

    void RefreshMaterials()
    {
        _material.ItemsSource = _lib.Materials.Select(m => m.Label).Prepend("New material…").ToList();
        _material.SelectedIndex = 0;
    }

    void Save()
    {
        try { SaveCore(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException or ArgumentException or NotSupportedException)
        {
            AppLog.Error("cut test save", e);
            _status.Text = $"Couldn't save: {e.Message} ({_lib.FilePath})";
            _status.Foreground = Brush("Warn");
        }
    }

    void SaveCore()
    {
        var kerfLayer = CutTest.KerfLayer(_doc.State);
        // The kerf test's setting when it has been measured (it was cut at the setting chosen); otherwise the square picked.
        bool measured = kerfLayer is not null && _measured.Value is > 0;
        var source = measured ? kerfLayer! : Chosen;
        if (source is null) { _status.Text = "Click the squares that went through first."; return; }
        Material m;
        if (_material.SelectedIndex <= 0)
        {
            var name = _newName.Text?.Trim();
            if (string.IsNullOrEmpty(name)) { _status.Text = "Give the material a name."; return; }
            m = new Material { Name = name, Category = "Other", ThicknessMm = (double)(_thick.Value ?? 0), HighlyReflective = Material.LooksReflective(name) };
        }
        else m = _lib.Materials[_material.SelectedIndex - 1];
        double t = (double)(_thick.Value ?? 0);
        double? kerf = measured ? CutTest.Kerf(KerfDefaults.SquareMm, (double)_measured.Value!.Value) : null;
        double? fit = measured && _snug.SelectedIndex > 0 ? CutTest.SlotFit(KerfDefaults.Fits[_snug.SelectedIndex - 1], kerf!.Value, true) : null;
        var notes = new List<string> { $"Cut test on {t:0.##} mm: through at {CutTest.Describe(source.Laser)}." };
        var others = Cells().Select(c => c.Layer).Where(l => _through.Contains(l.Id) && l.Id != source.Id).Select(l => CutTest.Describe(l.Laser)).ToList();
        if (others.Count > 0) notes.Add($"Also through: {string.Join("; ", others)}.");
        if (kerf is { } k) notes.Add($"Kerf {k:0.00} mm ({KerfDefaults.SquareMm:0} mm square measured {_measured.Value:0.00}).");
        if (fit is { } f) notes.Add($"Slot fit {f:+0.00;−0.00;0.00} mm with that kerf on the cut layer.");
        var settings = ProcessSettings.From(source with { Mode = LayerMode.Cut, KerfMm = kerf ?? 0 });
        var recipe = new Recipe
        {
            Purpose = RecipePurpose.Cut,
            Title = $"Cuts {t:0.##} mm: {source.Laser.Speed:0.#} mm/s × {source.Laser.Passes}" + (kerf is { } kk ? $", kerf {kk:0.00}" : ""),
            Settings = settings,
            Tested = true,
            TestedOn = DateTime.Now,
            SlotFitMm = fit,
            Notes = string.Join(" ", notes),
        };
        _lib.Upsert(m with { Recipes = m.Recipes.Add(recipe), ThicknessMm = m.ThicknessMm ?? t });
        _lib.Save();
        var check = MaterialLibrary.Load(_lib.Folder).Materials.FirstOrDefault(x => x.Id == m.Id)?.Recipes.Any(x => x.Id == recipe.Id) == true;
        AppLog.Write("cut test", $"Saved \"{recipe.Title}\" to {m.Label} in {_lib.FilePath}; read back: {(check ? "found" : "MISSING")}.");
        _status.Text = check ? $"Saved to {m.Label}: \"{recipe.Title}\"." : $"⚠ Saving to {m.Label} didn't stick. Details are in the log.";
        _status.Foreground = check ? Brush("Ok") : Brush("Warn");
        RefreshMaterials();
    }
}
