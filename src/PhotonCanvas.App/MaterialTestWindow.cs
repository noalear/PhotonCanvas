using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using static PhotonCanvas.App.UiKit;

namespace PhotonCanvas.App;

/// <summary>
/// Makes a test grid: power × speed, focus, line spacing, or any two settings. Burn it on a
/// scrap of the material, pick the square that looks best, and use its settings (each
/// square's layer is named after them).
/// </summary>
public sealed class MaterialTestWindow : Window
{
    static readonly TestParam[] Params = [TestParam.Power, TestParam.Speed, TestParam.LineInterval, TestParam.Focus, TestParam.Frequency, TestParam.PulseWidth, TestParam.Passes];
    static readonly string[] ParamNames = ["Power %", "Speed mm/s", "Line gap mm", "Focus mm", "Frequency kHz (MOPA)", "Pulse width ns (MOPA)", "Passes"];

    readonly ComboBox _preset = new() { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = new[] { "Power × speed", "Focus: find the sharpest height", "Line spacing: how close the fill lines go", "Any two settings", "One square at chosen settings" } };
    readonly ComboBox _mode = new() { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = new[] { "Engrave (filled squares)", "Score (outlines)", "Cut (outlines)" }, SelectedIndex = 0 };
    readonly ComboBox _light = new() { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = new[] { "Blue diode (wood, leather, acrylic, paper…)", "MOPA (metals, some plastics)" }, SelectedIndex = 0 };
    readonly ComboBox _across = new() { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = ParamNames }, _down = new() { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = ParamNames };
    readonly NumericUpDown _aMin = Number(-10, 15000, 1, "0.###"), _aMax = Number(-10, 15000, 1, "0.###"), _aSteps = Number(1, 20, 1, "0");
    readonly NumericUpDown _dMin = Number(-10, 15000, 1, "0.###"), _dMax = Number(-10, 15000, 1, "0.###"), _dSteps = Number(1, 20, 1, "0");
    readonly NumericUpDown _power = Number(1, 100, 5, "0.#"), _speed = Number(2, 15000, 10, "0.#"), _gap = Number(0.001m, 10, 0.01m, "0.###");
    readonly NumericUpDown _focus = Number(-10, 10, 0.5m, "0.##"), _freq = Number(1, 4000, 5, "0"), _passes = Number(1, 100, 1, "0");
    readonly ComboBox _pulse = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly NumericUpDown _cell = Number(3, 30, 1, "0.#");
    readonly Control _axisRows, _powerRow, _speedRow, _gapRow, _focusRow, _freqRow, _pulseRow, _passesRow;
    readonly TextBlock _summary = Hint(), _about = new() { TextWrapping = TextWrapping.Wrap };
    readonly MachineProfile _machine = MachineProfile.F2Ultra;
    readonly DocState _state;
    readonly string _font;
    bool _setting;

    /// <summary>The options chosen, or null if cancelled.</summary>
    public MaterialTestOptions? Result { get; private set; }

    public MaterialTestWindow(DocState state, string font)
    {
        _state = state;
        _font = font;
        Title = "Test grid";
        Width = 520;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");
        _pulse.ItemsSource = _machine.Mopa.Select(m => $"{m.PulseWidthNs} ns").ToList();

        _axisRows = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                Row(Field2("Across", _across), Field2("from", _aMin), Field2("to", _aMax), Field2("steps", _aSteps)),
                Row(Field2("Down", _down), Field2("from", _dMin), Field2("to", _dMax), Field2("steps", _dSteps)),
            },
        };
        if (_axisRows is StackPanel sp)
            foreach (var g in sp.Children.OfType<Grid>()) g.ColumnDefinitions = new("2*,*,*,0.7*");
        foreach (var n in new[] { _aMin, _aMax, _aSteps, _dMin, _dMax, _dSteps, _power, _speed, _gap, _focus, _freq, _passes })
        {
            n.ShowButtonSpinner = false; // narrow boxes: the wheel and arrow keys still step them
            n.HorizontalContentAlignment = HorizontalAlignment.Center;
        }
        _powerRow = Small("Power %", _power);
        _speedRow = Small("Speed mm/s", _speed);
        _gapRow = Small("Line gap mm", _gap, "Distance between fill lines. 0.1 mm = 100 lines per cm.");
        _focusRow = Small("Focus mm", _focus, "Focus offset for every square: + = deeper into the material (head lower).");
        _freqRow = Small("kHz", _freq);
        _pulseRow = Small("Pulse width", _pulse);
        _passesRow = Small("Passes", _passes);

        _preset.SelectionChanged += (_, _) => Preset();
        _mode.SelectionChanged += (_, _) => Update();
        _light.SelectionChanged += (_, _) => Preset();
        _across.SelectionChanged += (_, _) => AxisChanged(true);
        _down.SelectionChanged += (_, _) => AxisChanged(false);
        _pulse.SelectionChanged += (_, _) => Update();
        foreach (var n in new[] { _aMin, _aMax, _aSteps, _dMin, _dMax, _dSteps, _power, _speed, _gap, _focus, _freq, _passes, _cell }) n.ValueChanged += (_, _) => Update();

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
                    _about,
                    Field("Test", _preset),
                    Field("Laser", _light),
                    Field("Squares", _mode),
                    _axisRows,
                    new TextBlock { Text = "THE REST", Classes = { "h" } },
                    new WrapPanel { Children = { _powerRow, _speedRow, _gapRow, _focusRow, _pulseRow, _freqRow, _passesRow } },
                    Field("Square size (mm)", _cell),
                    _summary,
                    Hint("Each square gets its own layer named after its settings, so you can read them off afterwards. Labels go on a light \"Test labels\" layer."),
                    new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { cancel, add } },
                },
            },
        };
        _preset.SelectedIndex = 0;
    }

    static Control Small(string label, Control editor, string? tip = null)
    {
        var p = new StackPanel { Width = 112, Margin = new(0, 0, 8, 6), Spacing = 2, Children = { new TextBlock { Text = label, Classes = { "muted" }, FontSize = 12 }, editor } };
        if (tip is not null) ToolTip.SetTip(p, tip);
        return p;
    }

    static Control Field2(string label, Control editor) => new StackPanel
    {
        Spacing = 2,
        Children = { new TextBlock { Text = label, Classes = { "muted" }, FontSize = 12 }, editor },
    };

    bool Red => _light.SelectedIndex == 1;

    /// <summary>Starting values for the chosen kind of test and laser.</summary>
    void Preset()
    {
        if (_preset.SelectedIndex < 0) return;
        var light = Red ? LightSource.Red : LightSource.Blue;
        var o = _preset.SelectedIndex switch
        {
            1 => MaterialTestOptions.FocusTest(light),
            2 => MaterialTestOptions.IntervalTest(light),
            _ => Red
                ? new MaterialTestOptions { Light = light, AcrossMin = 20, AcrossMax = 100, DownMin = 200, DownMax = 2000, Speed = 1000, FrequencyKhz = 65, PulseWidthNs = 200 }
                : new MaterialTestOptions { Light = light },
        };
        _setting = true;
        try
        {
            _about.Text = _preset.SelectedIndex switch
            {
                1 => "Sparse lines at different focus heights: the thinnest, sharpest lines are in focus. Use that offset as the layer's focus offset, or move the head by it.",
                2 => "Fills with the lines closer and closer together. Look for where the lines just merge into an even tone: closer than that only adds heat and time.",
                3 => "Pick any two settings to sweep, one across and one down; the rest stay as set below.",
                4 => "A single square at the settings below, with its settings written next to it: to check one idea quickly.",
                _ => "Not sure what power and speed to use? Burn this on a scrap of your material: power goes up left to right, speed down top to bottom. Pick the square you like and use its numbers.",
            };
            _axisRows.IsEnabled = true;
            _across.SelectedIndex = Array.IndexOf(Params, o.Across);
            _down.SelectedIndex = Array.IndexOf(Params, o.Down);
            Set(_aMin, o.AcrossMin); Set(_aMax, o.AcrossMax); Set(_aSteps, o.AcrossSteps);
            Set(_dMin, o.DownMin); Set(_dMax, o.DownMax); Set(_dSteps, o.DownSteps);
            Set(_power, o.Power); Set(_speed, o.Speed); Set(_gap, 10 / o.LinesPerCm); Set(_focus, o.FocusMm);
            Set(_freq, o.FrequencyKhz); Set(_passes, o.Passes); Set(_cell, o.CellMm);
            _pulse.SelectedIndex = _machine.Mopa.ToList().FindIndex(m => m.PulseWidthNs == o.PulseWidthNs);
            if (_pulse.SelectedIndex < 0) _pulse.SelectedIndex = _machine.Mopa.Count - 1;
            if (_preset.SelectedIndex is 1 or 2) _mode.SelectedIndex = 0;
            if (_preset.SelectedIndex == 4) { Set(_aSteps, 1); Set(_dSteps, 1); }
        }
        finally { _setting = false; }
        Update();
    }

    static void Set(NumericUpDown n, double v) => n.Value = Math.Clamp((decimal)v, n.Minimum, n.Maximum);

    /// <summary>A newly picked axis starts with a sensible range for that setting.</summary>
    void AxisChanged(bool across)
    {
        if (_setting) return;
        var box = across ? _across : _down;
        if (box.SelectedIndex < 0) return;
        var (min, max, steps) = Params[box.SelectedIndex] switch
        {
            TestParam.Power => (Red ? 20.0 : 10.0, Red ? 100.0 : 90.0, 5),
            TestParam.Speed => (Red ? 200.0 : 50.0, Red ? 2000.0 : 400.0, 5),
            TestParam.LineInterval => (0.01, 0.1, 10),
            TestParam.Focus => (-3.0, 3.0, 13),
            TestParam.Frequency => (50.0, 500.0, 6),
            TestParam.PulseWidth => (2.0, 500.0, 6),
            _ => (1.0, 5.0, 5),
        };
        _setting = true;
        try
        {
            if (across) { Set(_aMin, min); Set(_aMax, max); Set(_aSteps, steps); }
            else { Set(_dMin, min); Set(_dMax, max); Set(_dSteps, steps); }
        }
        finally { _setting = false; }
        Update();
    }

    MaterialTestOptions Options() => new()
    {
        Mode = _mode.SelectedIndex switch { 0 => LayerMode.Fill, 1 => LayerMode.Score, _ => LayerMode.Cut },
        Light = Red ? LightSource.Red : LightSource.Blue,
        Across = Params[Math.Max(0, _across.SelectedIndex)],
        AcrossMin = (double)(_aMin.Value ?? 0),
        AcrossMax = (double)(_aMax.Value ?? 0),
        AcrossSteps = (int)(_aSteps.Value ?? 5),
        Down = Params[Math.Max(0, _down.SelectedIndex)],
        DownMin = (double)(_dMin.Value ?? 0),
        DownMax = (double)(_dMax.Value ?? 0),
        DownSteps = (int)(_dSteps.Value ?? 5),
        Power = (double)(_power.Value ?? 50),
        Speed = Math.Clamp((double)(_speed.Value ?? 200), _machine.MinSpeed, _machine.MaxSpeed),
        LinesPerCm = Math.Clamp(10 / (double)(_gap.Value ?? 0.1m), 1, FillBlock.MaxLinesPerCm),
        FocusMm = (double)(_focus.Value ?? 0),
        FrequencyKhz = (int)(_freq.Value ?? 65),
        PulseWidthNs = _pulse.SelectedIndex >= 0 ? _machine.Mopa[_pulse.SelectedIndex].PulseWidthNs : 200,
        Passes = (int)(_passes.Value ?? 1),
        CellMm = (double)(_cell.Value ?? 10),
        GapMm = (double)(_cell.Value ?? 10) < 8 ? 2 : 3,
        LabelMm = (double)(_cell.Value ?? 10) < 8 ? 2.5 : 3,
        Font = _font,
    };

    void Update()
    {
        if (_setting) return;
        var o = Options();
        bool Swept(TestParam p) => o.Across == p || o.Down == p;
        _powerRow.IsVisible = !Swept(TestParam.Power);
        _speedRow.IsVisible = !Swept(TestParam.Speed);
        _gapRow.IsVisible = o.Mode == LayerMode.Fill && !Swept(TestParam.LineInterval);
        _focusRow.IsVisible = !Swept(TestParam.Focus);
        _freqRow.IsVisible = Red && !Swept(TestParam.Frequency);
        _pulseRow.IsVisible = Red && !Swept(TestParam.PulseWidth);
        _passesRow.IsVisible = !Swept(TestParam.Passes);
        var b = MaterialTest.Bounds(_state, o);
        int n = MaterialTest.Values(o.Across, o.AcrossMin, o.AcrossMax, o.AcrossSteps, _machine).Count * MaterialTest.Values(o.Down, o.DownMin, o.DownMax, o.DownSteps, _machine).Count;
        bool fits = b.MaxX <= _machine.FieldW && b.MaxY <= _machine.FieldH;
        string warn = o.Across == o.Down ? " ⚠ Across and down are the same setting: pick two different ones." : "";
        if (!Red && (Swept(TestParam.Frequency) || Swept(TestParam.PulseWidth))) warn += " ⚠ Frequency and pulse width are MOPA settings: the diode ignores them.";
        if (o.Mode != LayerMode.Fill && Swept(TestParam.LineInterval)) warn += " ⚠ Line gap only matters for engraved (filled) squares.";
        _summary.Text = $"{n} squares, about {b.MaxX - b.MinX:0} × {b.MaxY - b.MinY:0} mm, placed at the top-left of the work area." +
                        (fits ? "" : " ⚠ That's bigger than the work area: use fewer steps or smaller squares.") + warn;
    }
}
