using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Colour;
using static PhotonCanvas.App.UiKit;

namespace PhotonCanvas.App;

/// <summary>
/// Makes a colour test grid for the MOPA laser on metal: one setting across, another down, the
/// rest fixed. Burn it on the plate you'll use, then measure it (Laser → Measure colours) to
/// see which colours that plate really gives.
/// </summary>
public sealed class ColourTestWindow : Window
{
    static readonly ColourParam[] Params = [ColourParam.Speed, ColourParam.LinesPerCm, ColourParam.Power, ColourParam.FrequencyKhz, ColourParam.PulseWidthNs];
    static string[] ParamNames => Params.Select(ColourTest.Name).ToArray();

    readonly ComboBox _across = new() { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = ParamNames, SelectedIndex = 3 };
    readonly ComboBox _down = new() { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = ParamNames, SelectedIndex = 2 };
    readonly NumericUpDown _aMin = Number(0, 15000, 10, "0.#"), _aMax = Number(0, 15000, 10, "0.#"), _aSteps = Number(1, 20, 1, "0");
    readonly NumericUpDown _dMin = Number(0, 15000, 10, "0.#"), _dMax = Number(0, 15000, 10, "0.#"), _dSteps = Number(1, 20, 1, "0");
    readonly NumericUpDown _power = Number(1, 100, 5, "0.#"), _speed = Number(2, 15000, 50, "0"), _lpcm = Number(10, (decimal)FillBlock.MaxLinesPerCm, 50, "0");
    readonly NumericUpDown _freq = Number(1, 4000, 10, "0");
    readonly ComboBox _pulse = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly NumericUpDown _cell = Number(2, 20, 0.5m, "0.#"), _gap = Number(0.5m, 10, 0.5m, "0.#");
    readonly Control _powerRow, _speedRow, _lpcmRow, _freqRow, _pulseRow;
    readonly TextBlock _summary = Hint();
    readonly ComboBox _kind = new() { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = new[] { "Oxide colours (stainless, titanium)", "Diffraction gratings (polished stainless)" }, SelectedIndex = 0 };
    readonly TextBlock _about = new() { TextWrapping = TextWrapping.Wrap };
    readonly CheckBox _spread = new() { Content = "Spread the heat: run each square where the plate is coolest", IsChecked = true };
    readonly ComboBox _sheet = new() { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = SheetMetal.All.Select(m => m.Name).ToList() };
    readonly NumericUpDown _thick = Number(0.05m, 5, 0.05m, "0.0#");
    readonly ComboBox _held = new() { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = SpreadHeat.Backings.Select(b => b.Name).ToList(), SelectedIndex = 0 };
    readonly TextBlock _orderHint = Hint();
    bool _loading;
    readonly MachineProfile _machine = MachineProfile.F2Ultra;
    readonly string _font;

    /// <summary>The options chosen, or null if cancelled.</summary>
    public ColourTestOptions? Result { get; private set; }

    public ColourTestWindow(string font)
    {
        _font = font;
        Title = "Colour test (MOPA)";
        Width = 540;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");

        _pulse.ItemsSource = _machine.Mopa.Select(m => $"{m.PulseWidthNs} ns").ToList();
        _pulse.SelectedIndex = _machine.Mopa.ToList().FindIndex(m => m.PulseWidthNs == 6);
        _kind.SelectionChanged += (_, _) => Load(_kind.SelectedIndex == 1 ? ColourTestOptions.Gratings : new ColourTestOptions());
        _across.SelectionChanged += (_, _) => { if (_loading) return; Defaults(_across, _aMin, _aMax); Update(); };
        _down.SelectionChanged += (_, _) => { if (_loading) return; Defaults(_down, _dMin, _dMax); Update(); };
        _pulse.SelectionChanged += (_, _) => Update();
        _sheet.SelectedIndex = SheetMetal.All.ToList().FindIndex(m => m.Name == "Stainless steel");
        _thick.Value = 0.5m;
        _spread.IsCheckedChanged += (_, _) => Update();
        foreach (var n in new[] { _aMin, _aMax, _aSteps, _dMin, _dMax, _dSteps, _power, _speed, _lpcm, _freq, _cell, _gap }) n.ValueChanged += (_, _) => { if (!_loading) Update(); };

        _powerRow = Field("Power %", _power);
        _speedRow = Field("Speed mm/s", _speed);
        _lpcmRow = Field("Lines/cm", _lpcm, tip: "Fill line density. 5000 lines/cm = 0.002 mm apart, the finest this machine takes. Finer = longer and hotter.");
        _freqRow = Field("Frequency kHz", _freq, tip: "Brought down to the most the chosen pulse width allows.");
        _pulseRow = Field("Pulse width", _pulse);

        var add = new Button { Content = "Add to design", Classes = { "primary" }, IsDefault = true, MinWidth = 120 };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 100 };
        add.Click += (_, _) => { Result = Options(); Close(); };
        cancel.Click += (_, _) => Close();

        Content = new Border
        {
            Padding = new Thickness(20),
            Child = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    Field("Test", _kind),
                    _about,
                    Row(Field2("Across", _across), Field2("from", _aMin), Field2("to", _aMax), Field2("steps", _aSteps)),
                    Row(Field2("Down", _down), Field2("from", _dMin), Field2("to", _dMax), Field2("steps", _dSteps)),
                    new TextBlock { Text = "THE REST", Classes = { "h" }, Margin = new(0, 6, 0, 0) },
                    _powerRow, _speedRow, _lpcmRow, _freqRow, _pulseRow,
                    Row(Field2("Square (mm)", _cell), Field2("Gap (mm)", _gap)),
                    _spread,
                    Row(Field2("Sheet metal", _sheet), Field2("Thickness (mm)", _thick), Field2("How it's held", _held)),
                    _orderHint,
                    _summary,
                    Hint("Focus on the plate first and keep that focus for everything you mark with these colours: a millimetre off changes them. " +
                         "Use a clean, flat plate with the same finish as the real piece."),
                    new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { cancel, add } },
                },
            },
        };
        Load(new ColourTestOptions());
    }

    static Control Field2(string label, Control editor) => new StackPanel
    {
        Spacing = 2,
        Children = { new TextBlock { Text = label, Classes = { "muted" }, FontSize = 12 }, editor },
    };

    /// <summary>Fill every box from a set of options (a preset).</summary>
    void Load(ColourTestOptions o)
    {
        _loading = true;
        _across.SelectedIndex = Array.IndexOf(Params, o.Across);
        _down.SelectedIndex = Array.IndexOf(Params, o.Down);
        _aMin.Value = (decimal)o.AcrossMin; _aMax.Value = (decimal)o.AcrossMax; _aSteps.Value = o.AcrossSteps;
        _dMin.Value = (decimal)o.DownMin; _dMax.Value = (decimal)o.DownMax; _dSteps.Value = o.DownSteps;
        _power.Value = (decimal)o.Power; _speed.Value = (decimal)o.Speed; _lpcm.Value = (decimal)Math.Round(o.LinesPerCm); _freq.Value = o.FrequencyKhz;
        _pulse.SelectedIndex = _machine.Mopa.ToList().FindIndex(m => m.PulseWidthNs == o.PulseWidthNs);
        _cell.Value = (decimal)o.CellMm; _gap.Value = (decimal)o.GapMm;
        _loading = false;
        _about.Text = Gratings
            ? "Each pulse leaves a mark a set distance from the last (speed ÷ frequency: 300 mm/s at 300 kHz = 1 µm), and a row of them diffracts light like a grating. " +
              "Tilt the burned test under a lamp: the best squares show bright rainbow colours. Use polished (mirror) stainless, held flat."
            : "Colour on stainless steel and titanium comes from a thin oxide layer: its thickness, set by how much energy goes in, decides the colour. " +
              "This grid tries a range of settings on your plate so you can see, and measure, which colours it really gives.";
        Title = Gratings ? "Grating test (MOPA)" : "Colour test (MOPA)";
        Update();
    }

    bool Gratings => _kind.SelectedIndex == 1;

    static ColourParam Param(ComboBox c) => Params[Math.Max(0, c.SelectedIndex)];

    /// <summary>A sensible starting range for a setting.</summary>
    static void Defaults(ComboBox which, NumericUpDown min, NumericUpDown max)
    {
        (decimal a, decimal b) = Param(which) switch
        {
            ColourParam.Speed => (200m, 1600m),
            ColourParam.LinesPerCm => (300m, 1000m),
            ColourParam.Power => (20m, 60m),
            ColourParam.FrequencyKhz => (200m, 1500m),
            _ => (4m, 200m),
        };
        min.Value = a;
        max.Value = b;
    }

    ColourTestOptions Options() => new()
    {
        Across = Param(_across),
        AcrossMin = (double)(_aMin.Value ?? 0), AcrossMax = (double)(_aMax.Value ?? 0), AcrossSteps = (int)(_aSteps.Value ?? 1),
        Down = Param(_down),
        DownMin = (double)(_dMin.Value ?? 0), DownMax = (double)(_dMax.Value ?? 0), DownSteps = (int)(_dSteps.Value ?? 1),
        Power = (double)(_power.Value ?? 40),
        Speed = (double)(_speed.Value ?? 800),
        LinesPerCm = (double)(_lpcm.Value ?? 600),
        FrequencyKhz = (int)(_freq.Value ?? 300),
        PulseWidthNs = _machine.Mopa[Math.Max(0, _pulse.SelectedIndex)].PulseWidthNs,
        CellMm = (double)(_cell.Value ?? 5),
        GapMm = (double)(_gap.Value ?? 2),
        Font = _font,
        OverscanMm = Gratings ? ColourTestOptions.Gratings.OverscanMm : 0,
        LabelPitch = Gratings,
        Spread = _spread.IsChecked == true
            ? new SpreadHeat
            {
                On = true,
                Sheet = SheetMetal.All[Math.Max(0, _sheet.SelectedIndex)].Name,
                ThicknessMm = (double)(_thick.Value ?? 0.5m),
                Backing = SpreadHeat.Backings[Math.Max(0, _held.SelectedIndex)].Backing,
            }
            : null,
    };

    void Update()
    {
        var o = Options();
        _sheet.IsEnabled = _thick.IsEnabled = _held.IsEnabled = o.Spread is not null;
        _orderHint.Text = o.Spread is not null
            ? "Each square runs where the plate is coolest, far from the one just done, with waits only where the plate needs them, so heat from one square doesn't shift its neighbour's colour or warp the plate out of focus. " +
              "The rest (how warm it may get, a fixed cool-down) is under Heat in the job bar."
            : "The squares run from the least energy to the most, so the heat builds up gradually, but the hottest squares run last on a plate that's already warm.";
        // The swept settings come from the grid, so hide their fixed boxes.
        var swept = new HashSet<ColourParam> { o.Across, o.Down };
        _powerRow.IsVisible = !swept.Contains(ColourParam.Power);
        _speedRow.IsVisible = !swept.Contains(ColourParam.Speed);
        _lpcmRow.IsVisible = !swept.Contains(ColourParam.LinesPerCm);
        _freqRow.IsVisible = !swept.Contains(ColourParam.FrequencyKhz);
        _pulseRow.IsVisible = !swept.Contains(ColourParam.PulseWidthNs);
        if (o.Across == o.Down)
        {
            _summary.Text = "⚠ Across and down need to be different settings.";
            return;
        }
        var (after, ids) = ColourTest.Add(DocState.New(), o with { Spread = null }, _machine); // just the test, for its size and time (waits come on top)
        var set = ids.ToHashSet();
        var b = after.Shapes.Where(x => set.Contains(x.Id)).Select(x => x.Bounds).Aggregate(Rect2.Empty, (a, x) => a.Union(x));
        int n = ColourTest.Cells(after).Count;
        bool fits = b.MaxX <= _machine.FieldW && b.MaxY <= _machine.FieldH;
        string time = "";
        try { time = $", about {Duration(JobBuilder.Build(after, new JobOptions { WholeJobPasses = false }).EstimateS)} to mark"; }
        catch (JobException) { }
        string pitches = "";
        if (o.Across == ColourParam.FrequencyKhz || o.Down == ColourParam.FrequencyKhz)
        {
            var (lo, hi) = o.Across == ColourParam.FrequencyKhz ? (o.AcrossMin, o.AcrossMax) : (o.DownMin, o.DownMax);
            pitches = $" Pulse spacing {Doc.Colour.Diffraction.PitchUm(o.Speed, (int)Math.Max(1, hi)):0.00}–{Doc.Colour.Diffraction.PitchUm(o.Speed, (int)Math.Max(1, lo)):0.00} µm.";
        }
        _summary.Text = $"{n} squares, {b.Width:0} × {b.Height:0} mm{time}{(o.Spread is not null ? " plus any cooling waits" : "")}.{pitches}" + (fits ? "" : " ⚠ That's bigger than the work area: use fewer steps or smaller squares.");
    }
}
