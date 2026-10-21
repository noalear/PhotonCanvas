using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using PhotonCanvas.Core;
using PhotonCanvas.Core.Imaging;
using PhotonCanvas.Doc;
using static PhotonCanvas.App.UiKit;

namespace PhotonCanvas.App;

/// <summary>
/// The active layer's settings (bottom of the right column): what the
/// laser does, with which source, how hard and how fast, then the material it is for.
/// The rarely changed settings fold away under "More". Editing several selected layers
/// changes them all; values that differ show as "mixed".
/// </summary>
public sealed class LayerEditor : UserControl
{
    readonly Document _doc;
    readonly LayersPanel _layers;
    readonly AppSettings _settings;
    readonly MachineProfile _machine = MachineProfile.F2Ultra;
    bool _updating;

    readonly TextBlock _several = new() { Classes = { "hint" }, IsVisible = false };
    readonly TextBlock _title = new() { Classes = { "h" }, Margin = new(0), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    readonly Border _swatch = new() { Width = 4, Height = 14, CornerRadius = new(2), Margin = new(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
    readonly Segmented _mode = new(("Cut", "IcCut"), ("Score", "IcLine"), ("Engrave", "IcPreview"), ("Picture", "IcImage"));
    readonly Segmented _light = new(("Diode · blue", null), ("MOPA", null));
    readonly NumericUpDown _power = Num(0, 100, 1, "0.#"), _speed = Num(2, 15000, 5, "0.#"), _passes = Num(1, 10000, 1, "0");
    readonly NumericUpDown _lpcm = Number(1, (decimal)FillBlock.MaxLinesPerCm, 10, "0.#");
    readonly NumericUpDown _dpi = Number(50, 1000, 10, "0");
    readonly NumericUpDown _dotUs = Number(10, 10000, 10, "0");
    readonly ComboBox _dither = new() { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = new[] { "Jarvis (smooth)", "Floyd–Steinberg", "Stucki", "Atkinson (contrasty)", "Bayer (pattern)", "Threshold (line art)", "Halftone (dots)", "Newsprint (coarse dots)", "Line screen (engraved lines)", "Sketch (edges only)" } };
    readonly NumericUpDown _cpi = Num(2, 500, 1, "0.#"), _screenAngle = Num(-180, 180, 15, "0.#"), _dotWidth = Number(0, 1, 0.005m, "0.###");
    readonly Control _screenRow;
    readonly CheckBox _bidir = new() { Content = "Burn in both directions" }, _together = new() { Content = "All shapes in one scan" };
    readonly CheckBox _outlineAfter = new() { Content = "Trace the outline after filling" }, _scanReverse = new() { Content = "Scan from the other side" }, _spread = new() { Content = "Spread the lines out (less heat)" }, _scatter = new() { Content = "…in no set pattern (no rows)", Margin = new(22, 0, 0, 0) }, _deepestFirst = new() { Content = "Deepest slice first" };
    readonly NumericUpDown _crossAngle = Number(-180, 180, 15, "0.#"), _dwell = Number(0, 60000, 50, "0"), _tabPower = Number(0, 100, 1, "0.#");
    readonly Control _crossRow, _stepRows;
    readonly ComboBox _order = new() { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = new[] { "Least travel (insides first)", "As drawn", "Back to front (away from smoke)" } };
    readonly CheckBox _startNearest = new() { Content = "Start outlines nearest the laser" }, _removeOverlaps = new() { Content = "Cut shared edges once" };
    readonly NumericUpDown _leadOut = Number(0, 10, 0.5m, "0.##");
    readonly NumericUpDown _perf = Num(0, 100, 0.5m, "0.##"), _perfGap = Num(0.05m, 100, 0.5m, "0.##"), _dotMode = Number(0, 50, 0.1m, "0.###");
    readonly NumericUpDown _ramp = Number(0, 10, 0.1m, "0.##");
    readonly CheckBox _passThrough = new() { Content = "Pass-through (the file's own pixels)" };
    // The same setting as _bidir, shown with the picture settings (a control has one place).
    readonly CheckBox _bidirPic = new() { Content = "Burn rows in both directions" };
    // Scan angle, crosshatch and turning per pass for pictures (the same layer settings as for fills).
    readonly NumericUpDown _picAngle = Number(-180, 180, 15, "0.#"), _picCrossAngle = Number(-180, 180, 15, "0.#"), _picTurn = Number(-180, 180, 5, "0.#");
    readonly CheckBox _picCross = new() { Content = "Crosshatch (each pass twice, the second turned)" };
    readonly TextBlock _picPasses = new() { Classes = { "hint" }, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    Control? _picCrossRow, _picTurnRows;
    readonly Control _rampRow;
    readonly TextBlock _lpcmHint = Hint(), _dpiHint = Hint(), _passHint = Hint(), _freqHint = Hint();
    readonly Control _lpcmRow, _dpiRow, _ditherRow, _dotRow, _imageModeRow;

    // more settings
    readonly Expander _more = new() { Header = "More settings", HorizontalAlignment = HorizontalAlignment.Stretch, Padding = new(8, 6) };
    readonly NumericUpDown _kerf = Number(-1, 1, 0.01m, "0.###"), _tabs = Number(0, 100, 1, "0"), _tabWidth = Number(0.1m, 10, 0.1m, "0.##");
    readonly NumericUpDown _overcut = Number(0, 10, 0.1m, "0.##"), _leadIn = Number(0, 10, 0.5m, "0.##");
    readonly NumericUpDown _stepDown = Number(0, 2, 0.1m, "0.##"), _stepFirst = Number(0, 2, 0.05m, "0.##");
    readonly CheckBox _wobble = new() { Content = "Wobble (MOPA)" };
    readonly NumericUpDown _wobbleD = Number(0.01m, 5, 0.01m, "0.###"), _wobbleS = Number(0.005m, 5, 0.005m, "0.###");
    readonly Control _wobbleRows;
    readonly NumericUpDown _fillAngle = Number(-180, 180, 15, "0.#"), _overscan = Number(0, 20, 0.5m, "0.##");
    readonly CheckBox _crosshatch = new() { Content = "Crosshatch (second pass at 90°)" };
    readonly Segmented _fillStyle = new(("Lines", null), ("Offset", null));
    readonly NumericUpDown _angleStep = Number(-180, 180, 1, "0.#");
    readonly CheckBox _turnRuns = new() { Content = "Keep turning from run to run" };
    readonly TextBlock _runCount = new() { Classes = { "muted" }, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
    readonly Button _resetRuns = new() { Content = "Start again", Padding = new(8, 2) };
    readonly Control _fillStyleRow, _lineRows, _runRow;
    readonly NumericUpDown _focusOff = Number(-10, 10, 0.1m, "0.##");
    // depth slices
    readonly NumericUpDown _slices = Number(2, DepthSlices.MaxCount, 1, "0");
    readonly CheckBox _negative = new() { Content = "Light = deep (negative)" };
    readonly NumericUpDown _dropMm = Num(0, 2, 0.01m, "0.###"), _dropEvery = Num(1, DepthSlices.MaxCount, 1, "0");
    readonly NumericUpDown _cleanEvery = Number(0, DepthSlices.MaxCount, 1, "0"), _cleanPower = Num(0, 100, 1, "0.#"), _cleanSpeed = Num(2, 15000, 50, "0");
    readonly NumericUpDown _cleanLpcm = Num(1, (decimal)FillBlock.MaxLinesPerCm, 10, "0"), _cleanFreq = Number(1, 4000, 5, "0");
    readonly CheckBox _cleanWhole = new() { Content = "Clean the whole picture (else only the slice just done)" };
    readonly TextBlock _sliceHint = Hint();
    readonly Control _sliceExtras, _cleanRows, _cleanFreqRow;
    readonly ComboBox _imageMode = new() { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = new[] { "Dots (dithered)", "Greyscale (experimental)", "Depth slices (relief)" } };
    readonly NumericUpDown _grayMin = Number(0, 100, 1, "0.#");
    readonly ComboBox _pulse = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly NumericUpDown _freq = Number(1, 4000, 5, "0");
    readonly Control _cutExtras, _fillExtras, _imageExtras, _grayRow, _pulseRow, _freqRow;
    readonly TextBlock _moreNothing = Hint("Nothing more for this kind of layer.");

    // material
    readonly ComboBox _material = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly CheckBox _reflective = new() { Content = "Highly reflective material" };
    readonly ComboBox _recipe = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly TextBlock _recipeInfo = Hint();
    readonly Image _recipePhoto = new() { MaxHeight = 110, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left, IsVisible = false };

    /// <summary>The material library (set by the main window).</summary>
    public PhotonCanvas.Doc.Materials.MaterialLibrary? Library { get; set; }

    /// <summary>Save the active layer's settings to the library.</summary>
    public event Action<Layer>? SaveToLibraryRequested;
    /// <summary>Open the library window.</summary>
    public event Action? LibraryRequested;

    static NumericUpDown Num(decimal min, decimal max, decimal step, string format)
    {
        var n = Number(min, max, step, format);
        n.ShowButtonSpinner = false; // narrow boxes: the wheel and arrow keys still step them
        n.HorizontalContentAlignment = HorizontalAlignment.Center;
        return n;
    }

    static Control Labelled(string label, Control editor, string tip)
    {
        var p = new StackPanel { Spacing = 2, Children = { new TextBlock { Text = label, Classes = { "muted" }, FontSize = 11 }, editor } };
        ToolTip.SetTip(p, tip);
        return p;
    }

    public LayerEditor(Document doc, LayersPanel layers, AppSettings settings)
    {
        _doc = doc;
        _layers = layers;
        _settings = settings;
        _pulse.ItemsSource = _machine.Mopa.Select(m => $"{m.PulseWidthNs} ns").ToList();

        ToolTip.SetTip(_mode, "Cut and Score follow the outline (Score is lighter and runs first). Engrave scans back and forth to burn the inside of closed shapes. Picture engraves photos as fine dots.");
        ToolTip.SetTip(_light, "Blue: 40 W diode, for wood, leather, acrylic, paper. MOPA: the 60 W MOPA fiber laser, for metals (including colour on stainless) and some plastics.");
        var trio = Row(
            Labelled("Power %", _power, "Percent of the laser's full power."),
            Labelled("Speed mm/s", _speed, "How fast the beam moves while firing. Slower = deeper and darker."),
            Labelled("Passes", _passes, "How many times this layer is processed."));

        _lpcmRow = Field("Lines / cm", _lpcm, _lpcmHint, "How densely an area is scanned. More lines = darker, smoother, slower.");
        _dpiRow = Field("Resolution", _dpi, _dpiHint, "Dots per inch. Higher = finer detail, slower. 150–300 suits most wood.");
        _ditherRow = Field("Dithering", _dither, tip: "How grey tones become dots. Jarvis suits photos; Threshold suits logos and line art.");
        _dotRow = Field("Dot time µs", _dotUs, tip: "How long each dot fires, in microseconds. Longer = darker dots.");
        _screenRow = Row(Labelled("Cells / inch", _cpi, "How many screen cells per inch: fewer = bigger, bolder dots or lines. Must be well under the resolution (dpi)."),
                         Labelled("Screen angle °", _screenAngle, "Direction of the screen. 45° hides the pattern best for dots; any angle for lines."));
        _imageModeRow = Field("Picture mode", _imageMode, tip: "Dots: every dot at full power, tone from dot spacing (proven). Greyscale: each dot's power follows the picture (not yet tested on your machine). " +
                                                              "Depth slices: the picture is a depth map (dark = deep), carved in thin slices one after another: relief on metal with the MOPA, or on wood.");

        _cutExtras = new StackPanel
        {
            Spacing = 6,
            Children =
            {
                Field("Kerf (mm)", _kerf, tip: "Width of the cut line. Outlines move out by half of it so parts come out at their drawn size (holes stay true too). 0 = off."),
                Field("Tabs", _tabs, tip: "Small uncut bridges in each closed outline so parts stay in the sheet. 0 = none."),
                Field("Tab width (mm)", _tabWidth),
                Field("Tab power %", _tabPower, tip: "Cut the tabs at this power instead of leaving them: thin bridges that snap out cleanly. 0 = tabs left uncut."),
                Field("Overcut (mm)", _overcut, tip: "Keep cutting this far past the start so the outline closes cleanly. Ignored when tabs are on."),
                Field("Lead-out (mm)", _leadOut, tip: "End each outline with a short exit line into the waste, so the end mark isn't on the part either."),
                Field("Order", _order, tip: "Least travel: holes and insides first, then the nearest next. As drawn: the order you drew or imported them. Back to front: from the top of the work area down, so smoke drifts over what's finished."),
                _startNearest,
                _removeOverlaps,
                Field("Lead-in (mm)", _leadIn, tip: "Start each outline with a short entry line in the waste, so the start mark isn't on the part."),
                Row(Labelled("Perforate: burn mm", _perf, "Perforation: burn this far, then skip the gap, along every line (fold lines, tear-off). 0 = a continuous line."),
                    Labelled("then skip mm", _perfGap, "The gap between perforation dashes.")),
                Field("Dot mode every", _dotMode, tip: "Dot mode: a single pulse every this many mm instead of a line (stippled marks, dotted lines). The dot time is set under the picture settings' Dot time. 0 = off."),

                _wobble,
            },
        };
        _wobbleRows = new StackPanel
        {
            Spacing = 6,
            Children =
            {
                Field("Wobble size (mm)", _wobbleD, tip: "Diameter of the small circles the beam traces while moving. Wider, smoother marks and cuts on metal."),
                Field("Wobble spacing", _wobbleS, tip: "How far the circles move forward per loop (mm). Smaller = denser, slower."),
            },
        };
        ((StackPanel)_cutExtras).Children.Add(_wobbleRows);
        ToolTip.SetTip(_wobble, "MOPA only: the beam traces small overlapping circles along the path.");
        _fillStyleRow = Field("Fill style", _fillStyle, tip: "Lines: straight back and forth. Offset: the outline stepped inward, like contour lines: follows the shape, so edges come out cleaner (good for text and logos).");
        ToolTip.SetTip(_resetRuns, "Count runs from 0 again, so the next run starts at the scan angle as set.");
        _runRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _runCount, _resetRuns } };
        ToolTip.SetTip(_turnRuns, "Each run of the job starts where the last one's turning left off, so repeating the job doesn't repeat the line pattern.");
        _crossRow = Field("Second pass at (°)", _crossAngle, tip: "Crosshatch: the second pass's angle from the first. 90° is the classic crosshatch; others give finer patterns.");
        _lineRows = new StackPanel
        {
            Spacing = 6,
            Children =
            {
                Field("Scan angle (°)", _fillAngle, tip: "Direction of the fill lines. 0 = left to right."),
                _crosshatch,
                Field("Overscan (mm)", _overscan, tip: "Each line starts and ends this far outside the shape with the laser off, so edges burn evenly."),
                _crossRow,
                _bidir,
                _together,
                _scanReverse,
                _outlineAfter,
                Field("Pause per line (ms)", _dwell, tip: "Wait this long before each line, so heat can leave (acrylic, thin or heat-sensitive material). 0 = none."),
                Field("Dot width (mm)", _dotWidth, tip: "Dot width correction: each burned line is made shorter by this much (half at each end), so the beam's width doesn't make fills wider than drawn. About the beam width: 0.03–0.08 mm. 0 = off."),
                Field("Turn per pass (°)", _angleStep, tip: "Turn the scan lines this much more on every pass (and every depth slice), so the line pattern doesn't build up into stripes or ridges. " +
                                                              "Something odd like 37° or 23° spreads the angles best. 0 = every pass the same."),
                _turnRuns,
                _runRow,
            },
        };
        _fillExtras = new StackPanel { Spacing = 6, Children = { _fillStyleRow, _lineRows } };
        _stepRows = new StackPanel
        {
            Spacing = 6,
            Children =
            {
                Field("Step down/pass", _stepDown, tip: "Lower the head this many mm before each pass after the first, so the focus follows the cut or engraving down (thick material, deep engraving). The head goes back up afterwards. 0 = off."),
                Field("First step down", _stepFirst, tip: "With step-down: also lower the head this much before the first pass (mm)."),
            },
        };
        ToolTip.SetTip(_outlineAfter, "After filling each shape, trace its outline at the same settings: hides ragged edges.");
        ToolTip.SetTip(_scanReverse, "Fill from the far side back (bottom to top): keeps the smoke away from what's still to engrave, depending on how your air flows.");
        ToolTip.SetTip(_spread, "Burn every few lines first, then the ones between, so no line is burned straight after the one beside it and the heat has time to leave: " +
                                "less scorching, less warping of thin plastic, more even colours on metal. Hardly any slower on a galvo. " +
                                "On wood pictures it can leave visible rows (lines burned beside already-charred ones come out darker); Laser → Line test shows both.");
        ToolTip.SetTip(_deepestFirst, "Engrave the deepest (smallest) slices first and the widest last: often a smoother floor (noise reduction).");
        _grayRow = Field("Lightest power %", _grayMin, tip: "Power for the palest tones that still burn; the darkest tones get the layer's power.");
        _rampRow = Field("Ramp (mm)", _ramp, tip: "Threshold only: power climbs from the lightest power at each edge to full power this far in, so the sides slope (rubber stamps, cleaner edges). 0 = off.");
        ToolTip.SetTip(_bidirPic, "Off: every row burns left to right and the head goes back unlit. Slower, but rows can't be shifted against each other by the direction (a woven look). Laser → Line test compares both.");
        ToolTip.SetTip(_bidir, "Off: every line burns in the same direction and the beam returns unlit. More even on some materials, a little slower.");
        ToolTip.SetTip(_together, "Scan all of this layer's shapes in one go instead of one shape at a time: fewer jumps for lots of small shapes. Where shapes overlap they cancel out.");
        ToolTip.SetTip(_passThrough, "Engrave the picture's own pixels, one dot each, with no resampling (for pictures already prepared at the right size). Never closer together than the resolution above: a picture with more pixels than that is resampled down, so it can't put in more heat than you set.");
        _cleanFreqRow = Field("Cleanup kHz", _cleanFreq, tip: "MOPA: the cleanup pass's frequency (its pulse width stays the layer's).");
        _cleanRows = new StackPanel
        {
            Spacing = 6,
            Children =
            {
                Row(Labelled("Cleanup %", _cleanPower, "Power of the cleanup pass: gentle, to clear debris and smooth the floor rather than cut deeper."),
                    Labelled("Speed mm/s", _cleanSpeed, "Speed of the cleanup pass."),
                    Labelled("Lines / cm", _cleanLpcm, "Line density of the cleanup pass.")),
                _cleanFreqRow,
                _cleanWhole,
            },
        };
        _sliceExtras = new StackPanel
        {
            Spacing = 6,
            Children =
            {
                Field("Slices", _slices, _sliceHint, "How many steps from the surface to the deepest point. Each slice engraves everything at least that deep. More slices = smoother relief, longer job."),
                _negative,
                Row(Labelled("Lower focus mm", _dropMm, "Follow the floor down: lower the focus this much every few slices. About the depth those slices remove. 0 = off."),
                    Labelled("every … slices", _dropEvery, "How many slices between focus drops.")),
                _deepestFirst,
                Field("Cleanup every", _cleanEvery, tip: "A cleanup pass after every this many slices (0 = none). Clears debris and recast so the next slices cut cleanly."),
                _cleanRows,
            },
        };
        ToolTip.SetTip(_negative, "For depth maps where light means deep (most are dark = deep).");
        _picCrossRow = Field("Second pass at (°)", _picCrossAngle, tip: "Crosshatch: how far the second copy of each pass is turned from the first. 90° is the classic crosshatch.");
        _picTurnRows = new StackPanel
        {
            Spacing = 6,
            Children =
            {
                Field("Scan angle (°)", _picAngle, tip: "The direction the rows run. 0 = left to right."),
                Field("Turn per pass (°)", _picTurn, tip: "With several passes, each one runs its rows this much further round, so no row lies on top of another and the row pattern averages out. " +
                                                          "Pull the power back so all the passes together make the darkness you want. 0 = every pass the same way."),
                _picCross,
                _picCrossRow,
                _picPasses,
            },
        };
        _imageExtras = new StackPanel
        {
            Spacing = 6,
            Children =
            {
                _grayRow,
                _rampRow,
                _passThrough,
                _bidirPic,
                _picTurnRows,
                _dotRow,
                _sliceExtras,
            },
        };
        _pulseRow = Field("Pulse width", _pulse, tip: "MOPA only. How long each laser pulse lasts. Short pulses mark crisply; long pulses put in more heat.");
        _freqRow = Field("Frequency", _freq, _freqHint, "MOPA only. Pulses per second (kHz). Higher = pulses overlap more, smoother lines, less energy per pulse.");
        var focusRow = Field("Focus offset (mm)", _focusOff, tip: "Move the focus for this layer: + = deeper into the material (the head goes lower), − = above it (a softer, wider beam). 0 = at the surface. The head goes back afterwards.");
        _more.Content = new StackPanel { Spacing = 6, Children = { _cutExtras, _fillExtras, _stepRows, _imageExtras, _spread, _scatter, _pulseRow, _freqRow, focusRow, _moreNothing } };
        _more.IsExpanded = _settings.ShowAdvanced;
        _more.Expanded += (_, _) => { _settings.ShowAdvanced = true; _settings.Save(); };
        _more.Collapsed += (_, _) => { _settings.ShowAdvanced = false; _settings.Save(); };
        ToolTip.SetTip(_more, "Kerf, tabs, overcut, lead-in, step-down, wobble, scan angle, overscan, dot time and MOPA pulse settings. The defaults work for most jobs.");

        var saveToLib = Button("Save to library…", tip: "Remember these settings for a material, with how well they worked (and a photo of the result).");
        saveToLib.Click += (_, _) =>
        {
            var l = EditLayersSet().FirstOrDefault(x => x.Id == _doc.ActiveLayer) ?? EditLayersSet().FirstOrDefault();
            if (l is not null) SaveToLibraryRequested?.Invoke(l);
        };
        var openLib = Button("Library…", tip: "Your material and cutting library: browse, edit, share.");
        openLib.Click += (_, _) => LibraryRequested?.Invoke();
        ToolTip.SetTip(_material, "What this layer is for. Picking a material lists its saved settings below.");
        ToolTip.SetTip(_reflective, "Bare copper, brass, aluminium, silver, gold, chrome or mirror finishes. The MOPA then keeps out of the frequencies that bounce the most back into the fiber (long pulses only), and the job reminds you how to cut reflections. Set by the material's tag.");
        _reflective.IsCheckedChanged += (_, _) => EditLayers(_reflective.IsChecked == true ? "Highly reflective" : "Not reflective", l =>
        {
            var next = l with { Reflective = _reflective.IsChecked == true };
            // Turning it on moves a frequency in the protected range up to full power.
            return _machine.MopaLimitFor(l.Laser.PulseWidthNs) is { } lim ? next with { Laser = next.Laser with { FrequencyKhz = lim.Safe(next.Laser.FrequencyKhz, next.Reflective) } } : next;
        }, _reflective.IsChecked.HasValue);
        ToolTip.SetTip(_recipe, "Apply settings saved for this material (★ rating, ✓ = tested).");

        Content = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new StackPanel { Orientation = Orientation.Horizontal, Children = { _swatch, _title } },
                _several,
                _mode,
                _light,
                trio,
                _passHint,
                _lpcmRow, _dpiRow, _imageModeRow, _ditherRow, _screenRow,
                Field("Material", _material),
                _reflective,
                Field("Saved settings", _recipe),
                _recipeInfo,
                _recipePhoto,
                Row(saveToLib, openLib),
                _more,
            },
        };

        _mode.Changed += i => EditLayers("Layer mode", l => l with { Mode = (LayerMode)i }, true);
        _light.Changed += i => EditLayers("Laser source", l => l with { Laser = l.Laser with { Light = i == 1 ? LightSource.Red : LightSource.Blue } }, true);
        _power.ValueChanged += (_, _) => EditLayers("Power", l => l with { Laser = l.Laser with { Power = (double)_power.Value!.Value } }, _power.Value.HasValue, "power");
        _speed.ValueChanged += (_, _) => EditLayers("Speed", l => l with { Laser = l.Laser with { Speed = (double)_speed.Value!.Value } }, _speed.Value.HasValue, "speed");
        _passes.ValueChanged += (_, _) => EditLayers("Passes", l => l with { Laser = l.Laser with { Passes = (int)_passes.Value!.Value } }, _passes.Value.HasValue, "passes");
        _lpcm.ValueChanged += (_, _) => EditLayers("Line density", l => l with { LinesPerCm = (double)_lpcm.Value!.Value }, _lpcm.Value.HasValue, "lpcm");
        _dpi.ValueChanged += (_, _) => EditLayers("Resolution", l => l with { Dpi = (double)_dpi.Value!.Value }, _dpi.Value.HasValue, "dpi");
        _dotUs.ValueChanged += (_, _) => EditLayers("Dot time", l => l with { DotUs = (int)(Math.Round(_dotUs.Value!.Value / 10) * 10) }, _dotUs.Value.HasValue, "dot");
        _dither.SelectionChanged += (_, _) => EditLayers("Dithering", l => l with { Dither = (DitherMethod)Math.Max(0, _dither.SelectedIndex) }, _dither.SelectedIndex >= 0);
        _kerf.ValueChanged += (_, _) => EditLayers("Kerf", l => l with { KerfMm = (double)_kerf.Value!.Value }, _kerf.Value.HasValue, "kerf");
        _tabs.ValueChanged += (_, _) => EditLayers("Tabs", l => l with { TabCount = (int)_tabs.Value!.Value }, _tabs.Value.HasValue, "tabs");
        _tabWidth.ValueChanged += (_, _) => EditLayers("Tab width", l => l with { TabWidthMm = (double)_tabWidth.Value!.Value }, _tabWidth.Value.HasValue, "tabw");
        _overcut.ValueChanged += (_, _) => EditLayers("Overcut", l => l with { OvercutMm = (double)_overcut.Value!.Value }, _overcut.Value.HasValue, "overcut");
        _leadIn.ValueChanged += (_, _) => EditLayers("Lead-in", l => l with { LeadInMm = (double)_leadIn.Value!.Value }, _leadIn.Value.HasValue, "leadin");
        _stepDown.ValueChanged += (_, _) => EditLayers("Step down", l => l with { StepDownMm = (double)_stepDown.Value!.Value }, _stepDown.Value.HasValue, "stepdown");
        _stepFirst.ValueChanged += (_, _) => EditLayers("First step down", l => l with { StepDownFirstMm = (double)_stepFirst.Value!.Value }, _stepFirst.Value.HasValue, "stepfirst");
        _wobble.IsCheckedChanged += (_, _) => EditLayers("Wobble", l => l with { Wobble = _wobble.IsChecked == true }, _wobble.IsChecked.HasValue);
        _wobbleD.ValueChanged += (_, _) => EditLayers("Wobble size", l => l with { WobbleDiameterMm = (double)_wobbleD.Value!.Value }, _wobbleD.Value.HasValue, "wobbled");
        _wobbleS.ValueChanged += (_, _) => EditLayers("Wobble spacing", l => l with { WobbleSpacingMm = (double)_wobbleS.Value!.Value }, _wobbleS.Value.HasValue, "wobbles");
        _fillAngle.ValueChanged += (_, _) => EditLayers("Scan angle", l => l with { FillAngleDeg = (double)_fillAngle.Value!.Value }, _fillAngle.Value.HasValue, "fangle");
        _overscan.ValueChanged += (_, _) => EditLayers("Overscan", l => l with { OverscanMm = (double)_overscan.Value!.Value }, _overscan.Value.HasValue, "overscan");
        _crosshatch.IsCheckedChanged += (_, _) => EditLayers("Crosshatch", l => l with { Crosshatch = _crosshatch.IsChecked == true }, _crosshatch.IsChecked.HasValue);
        _imageMode.SelectionChanged += (_, _) => EditLayers("Picture mode", l =>
        {
            var mode = (ImageEngraveMode)Math.Max(0, _imageMode.SelectedIndex);
            // Slices start turning by an odd angle, so the line pattern doesn't stack up.
            return l with { ImageMode = mode, AngleStepDeg = mode == ImageEngraveMode.Slices && l.ImageMode != mode && l.AngleStepDeg == 0 ? 37 : l.AngleStepDeg };
        }, _imageMode.SelectedIndex >= 0);
        _fillStyle.Changed += i => EditLayers("Fill style", l => l with { FillStyle = (FillStyle)i }, true);
        _order.SelectionChanged += (_, _) => EditLayers("Path order", l => l with { Order = (PathOrder)Math.Max(0, _order.SelectedIndex) }, _order.SelectedIndex >= 0);
        _startNearest.IsCheckedChanged += (_, _) => EditLayers("Start nearest", l => l with { StartNearest = _startNearest.IsChecked == true }, _startNearest.IsChecked.HasValue);
        _removeOverlaps.IsCheckedChanged += (_, _) => EditLayers("Cut shared edges once", l => l with { RemoveOverlaps = _removeOverlaps.IsChecked == true }, _removeOverlaps.IsChecked.HasValue);
        _leadOut.ValueChanged += (_, _) => EditLayers("Lead-out", l => l with { LeadOutMm = (double)_leadOut.Value!.Value }, _leadOut.Value.HasValue, "leadout");
        ToolTip.SetTip(_startNearest, "Each closed outline starts where the laser already is: less travel. Off: outlines start where they were drawn.");
        ToolTip.SetTip(_removeOverlaps, "Where two shapes share an edge (parts side by side), cut it only once: cleaner edges, less time, less scorching.");
        _crossAngle.ValueChanged += (_, _) => EditLayers("Crosshatch angle", l => l with { CrossAngleDeg = (double)_crossAngle.Value!.Value }, _crossAngle.Value.HasValue, "crossangle");
        _dwell.ValueChanged += (_, _) => EditLayers("Pause per line", l => l with { LineDwellMs = (int)_dwell.Value!.Value }, _dwell.Value.HasValue, "dwell");
        _tabPower.ValueChanged += (_, _) => EditLayers("Tab power", l => l with { TabPowerPct = (double)_tabPower.Value!.Value }, _tabPower.Value.HasValue, "tabpower");
        _outlineAfter.IsCheckedChanged += (_, _) => EditLayers("Outline after fill", l => l with { OutlineAfter = _outlineAfter.IsChecked == true }, _outlineAfter.IsChecked.HasValue);
        _scanReverse.IsCheckedChanged += (_, _) => EditLayers("Scan direction", l => l with { ScanReverse = _scanReverse.IsChecked == true }, _scanReverse.IsChecked.HasValue);
        _spread.IsCheckedChanged += (_, _) => EditLayers("Spread lines", l => l with { SpreadLines = _spread.IsChecked == true }, _spread.IsChecked.HasValue);
        _scatter.IsCheckedChanged += (_, _) => EditLayers("Scatter lines", l => l with { SpreadScatter = _scatter.IsChecked == true }, _scatter.IsChecked.HasValue);
        ToolTip.SetTip(_scatter, "Spread the lines in no set order (each still away from the last), so there's no repeating pattern of early and late lines to show as rows. Laser → Line test compares it.");
        _deepestFirst.IsCheckedChanged += (_, _) => EditLayers("Deepest slice first", l => l with { Slices = l.Slices with { DeepestFirst = _deepestFirst.IsChecked == true } }, _deepestFirst.IsChecked.HasValue);
        _bidir.IsCheckedChanged += (_, _) => EditLayers("Both directions", l => l with { Bidirectional = _bidir.IsChecked == true }, _bidir.IsChecked.HasValue);
        _picAngle.ValueChanged += (_, _) => EditLayers("Scan angle", l => l with { FillAngleDeg = (double)_picAngle.Value!.Value }, _picAngle.Value.HasValue, "fangle");
        _picTurn.ValueChanged += (_, _) => EditLayers("Turn per pass", l => l with { AngleStepDeg = (double)_picTurn.Value!.Value }, _picTurn.Value.HasValue, "anglestep");
        _picCrossAngle.ValueChanged += (_, _) => EditLayers("Crosshatch angle", l => l with { CrossAngleDeg = (double)_picCrossAngle.Value!.Value }, _picCrossAngle.Value.HasValue, "crossangle");
        _picCross.IsCheckedChanged += (_, _) => EditLayers("Crosshatch", l => l with { Crosshatch = _picCross.IsChecked == true }, _picCross.IsChecked.HasValue);
        _bidirPic.IsCheckedChanged += (_, _) => EditLayers("Both directions", l => l with { Bidirectional = _bidirPic.IsChecked == true }, _bidirPic.IsChecked.HasValue);
        _together.IsCheckedChanged += (_, _) => EditLayers("All shapes in one scan", l => l with { FillTogether = _together.IsChecked == true }, _together.IsChecked.HasValue);
        _perf.ValueChanged += (_, _) => EditLayers("Perforation", l => l with { PerforateMm = (double)_perf.Value!.Value }, _perf.Value.HasValue, "perf");
        _perfGap.ValueChanged += (_, _) => EditLayers("Perforation gap", l => l with { PerforateGapMm = (double)_perfGap.Value!.Value }, _perfGap.Value.HasValue, "perfgap");
        _dotMode.ValueChanged += (_, _) => EditLayers("Dot mode", l => l with { DotModeMm = (double)_dotMode.Value!.Value }, _dotMode.Value.HasValue, "dotmode");
        _ramp.ValueChanged += (_, _) => EditLayers("Ramp", l => l with { RampMm = (double)_ramp.Value!.Value }, _ramp.Value.HasValue, "ramp");
        _passThrough.IsCheckedChanged += (_, _) => EditLayers("Pass-through", l => l with { PassThrough = _passThrough.IsChecked == true }, _passThrough.IsChecked.HasValue);
        _cpi.ValueChanged += (_, _) => EditLayers("Screen cells", l => l with { HalftoneCpi = (double)_cpi.Value!.Value }, _cpi.Value.HasValue, "cpi");
        _screenAngle.ValueChanged += (_, _) => EditLayers("Screen angle", l => l with { HalftoneAngleDeg = (double)_screenAngle.Value!.Value }, _screenAngle.Value.HasValue, "sangle");
        _dotWidth.ValueChanged += (_, _) => EditLayers("Dot width correction", l => l with { DotWidthMm = (double)_dotWidth.Value!.Value }, _dotWidth.Value.HasValue, "dotwidth");
        _angleStep.ValueChanged += (_, _) => EditLayers("Turn per pass", l => l with { AngleStepDeg = (double)_angleStep.Value!.Value }, _angleStep.Value.HasValue, "anglestep");
        _turnRuns.IsCheckedChanged += (_, _) => EditLayers("Turn from run to run", l => l with { TurnEachRun = _turnRuns.IsChecked == true }, _turnRuns.IsChecked.HasValue);
        _resetRuns.Click += (_, _) => EditLayers("Start the turning again", l => l with { RunCount = 0 }, true);
        _focusOff.ValueChanged += (_, _) => EditLayers("Focus offset", l => l with { FocusOffsetMm = (double)_focusOff.Value!.Value }, _focusOff.Value.HasValue, "focusoff");
        _slices.ValueChanged += (_, _) => EditLayers("Slices", l => l with { Slices = l.Slices with { Count = (int)_slices.Value!.Value } }, _slices.Value.HasValue, "slices");
        _negative.IsCheckedChanged += (_, _) => EditLayers("Light = deep", l => l with { Slices = l.Slices with { Negative = _negative.IsChecked == true } }, _negative.IsChecked.HasValue);
        _dropMm.ValueChanged += (_, _) => EditLayers("Lower focus", l => l with { Slices = l.Slices with { DropMm = (double)_dropMm.Value!.Value } }, _dropMm.Value.HasValue, "dropmm");
        _dropEvery.ValueChanged += (_, _) => EditLayers("Lower focus every", l => l with { Slices = l.Slices with { DropEvery = (int)_dropEvery.Value!.Value } }, _dropEvery.Value.HasValue, "dropevery");
        _cleanEvery.ValueChanged += (_, _) => EditLayers("Cleanup every", l => l with { Slices = l.Slices with { CleanupEvery = (int)_cleanEvery.Value!.Value } }, _cleanEvery.Value.HasValue, "cleanevery");
        _cleanPower.ValueChanged += (_, _) => EditLayers("Cleanup power", l => l with { Slices = l.Slices with { CleanupLaser = l.Slices.CleanupLaser with { Power = (double)_cleanPower.Value!.Value } } }, _cleanPower.Value.HasValue, "cleanpower");
        _cleanSpeed.ValueChanged += (_, _) => EditLayers("Cleanup speed", l => l with { Slices = l.Slices with { CleanupLaser = l.Slices.CleanupLaser with { Speed = (double)_cleanSpeed.Value!.Value } } }, _cleanSpeed.Value.HasValue, "cleanspeed");
        _cleanLpcm.ValueChanged += (_, _) => EditLayers("Cleanup lines", l => l with { Slices = l.Slices with { CleanupLinesPerCm = (double)_cleanLpcm.Value!.Value } }, _cleanLpcm.Value.HasValue, "cleanlpcm");
        _cleanFreq.ValueChanged += (_, _) => EditLayers("Cleanup frequency", l =>
        {
            int f = (int)_cleanFreq.Value!.Value;
            int safe = _machine.MopaLimitFor(l.Laser.PulseWidthNs) is { } lim ? lim.Safe(f, l.Reflective) : f;
            return l with { Slices = l.Slices with { CleanupLaser = l.Slices.CleanupLaser with { FrequencyKhz = safe, PulseWidthNs = l.Laser.PulseWidthNs } } };
        }, _cleanFreq.Value.HasValue, "cleanfreq");
        _cleanWhole.IsCheckedChanged += (_, _) => EditLayers("Cleanup area", l => l with { Slices = l.Slices with { CleanupWhole = _cleanWhole.IsChecked == true } }, _cleanWhole.IsChecked.HasValue);
        _grayMin.ValueChanged += (_, _) => EditLayers("Lightest power", l => l with { GrayMinPower = (double)_grayMin.Value!.Value }, _grayMin.Value.HasValue, "graymin");
        _pulse.SelectionChanged += (_, _) =>
        {
            if (_pulse.SelectedIndex < 0) return;
            var lim = _machine.Mopa[_pulse.SelectedIndex];
            // Keep the frequency where it can (colour work tunes it), within what this pulse width allows.
            EditLayers("Pulse width", l => l with { Laser = l.Laser with { PulseWidthNs = lim.PulseWidthNs, FrequencyKhz = lim.Safe(l.Laser.FrequencyKhz, l.Reflective) } }, true);
        };
        _freq.ValueChanged += (_, _) => EditLayers("Frequency", l =>
        {
            int f = (int)_freq.Value!.Value;
            // Out of the range the source is protected from (long pulses): up to full power.
            return l with { Laser = l.Laser with { FrequencyKhz = _machine.MopaLimitFor(l.Laser.PulseWidthNs) is { } lim ? lim.Safe(f, l.Reflective) : f } };
        }, _freq.Value.HasValue, "freq");
        _material.SelectionChanged += (_, _) => MaterialChosen();
        _recipe.SelectionChanged += (_, _) => RecipeChosen();

        _doc.Changed += _ => Refresh();
        _layers.SelectionChanged += Refresh;
        Refresh();
    }

    IEnumerable<Layer> EditLayersSet() => _doc.State.Layers.Where(l => _layers.SelectedLayers.Contains(l.Id));

    void EditLayers(string label, Func<Layer, Layer> edit, bool valid, string? coalesce = null)
    {
        if (_updating || !valid) return;
        var ids = _layers.SelectedLayers;
        if (ids.Count == 0) return;
        _doc.Commit(ids.Count > 1 ? $"{label} ({ids.Count} layers)" : label, Ops.UpdateLayers(_doc.State, ids, edit),
            coalesceKey: coalesce is null ? null : $"{coalesce}:{string.Join(",", ids)}");
    }

    void Refresh()
    {
        if (_doc.InEdit) return;
        _updating = true;
        try { RefreshLayer(); }
        finally { _updating = false; }
    }

    void RefreshLayer()
    {
        var ls = EditLayersSet().ToList();
        if (ls.Count == 0) return;
        _several.Text = $"Editing {ls.Count} layers together. Values that differ show as “mixed”.";
        _several.IsVisible = ls.Count > 1;
        _title.Text = ls.Count == 1 ? $"LAYER · {ls[0].Name.ToUpperInvariant()}" : $"{ls.Count} LAYERS";
        var c = ls[0].Color;
        _swatch.Background = new SolidColorBrush(Color.FromRgb((byte)(c >> 16), (byte)(c >> 8), (byte)c));
        _swatch.IsVisible = ls.Count == 1;
        RefreshMaterial();

        T? Same<T>(Func<Layer, T> f) where T : struct
        {
            var first = f(ls[0]);
            return ls.All(l => EqualityComparer<T>.Default.Equals(f(l), first)) ? first : null;
        }
        void Num(NumericUpDown n, double? v)
        {
            n.Value = v is { } x ? Math.Clamp((decimal)x, n.Minimum, n.Maximum) : null;
            n.Watermark = v is null ? "mixed" : null;
        }

        _mode.SelectedIndex = Same(l => l.Mode) is { } m ? (int)m : -1;
        _light.SelectedIndex = Same(l => l.Laser.Light) is { } li ? (int)li : -1;
        Num(_power, Same(l => l.Laser.Power));
        Num(_speed, Same(l => l.Laser.Speed));
        Num(_passes, Same(l => (double)l.Laser.Passes));
        Num(_lpcm, Same(l => l.LinesPerCm));

        bool anyFill = ls.Any(l => l.Mode == LayerMode.Fill), anyRed = ls.Any(l => l.Laser.Light == LightSource.Red);
        bool anyImage = ls.Any(l => l.Mode == LayerMode.Image), anyCut = ls.Any(l => l.Mode is LayerMode.Cut or LayerMode.Score);
        bool anySlices = ls.Any(l => l.Mode == LayerMode.Image && l.ImageMode == ImageEngraveMode.Slices);
        bool allSlices = anyImage && ls.Where(l => l.Mode == LayerMode.Image).All(l => l.ImageMode == ImageEngraveMode.Slices);
        bool allOffset = anyFill && !anySlices && ls.Where(l => l.Mode == LayerMode.Fill).All(l => l.FillStyle == FillStyle.Offset);
        _lpcmRow.IsVisible = anyFill || anySlices;
        _dpiRow.IsVisible = _imageModeRow.IsVisible = anyImage;
        _ditherRow.IsVisible = _dotRow.IsVisible = anyImage && !allSlices;
        _screenRow.IsVisible = anyImage && !allSlices && ls.Any(l => l.Mode == LayerMode.Image && l.ImageMode == ImageEngraveMode.Dither && l.Dither is DitherMethod.Halftone or DitherMethod.LineScreen);
        Num(_cpi, Same(l => l.HalftoneCpi));
        Num(_screenAngle, Same(l => l.HalftoneAngleDeg));
        Num(_dotWidth, Same(l => l.DotWidthMm));
        _bidir.IsChecked = Same(l => l.Bidirectional);
        _together.IsChecked = Same(l => l.FillTogether);
        _bidir.IsVisible = _together.IsVisible = _scanReverse.IsVisible = anyFill && !allOffset;
        _bidirPic.IsChecked = Same(l => l.Bidirectional);
        _bidirPic.IsVisible = anyImage && !allSlices;
        _picTurnRows!.IsVisible = anyImage && !allSlices;
        Num(_picAngle, Same(l => l.FillAngleDeg));
        Num(_picTurn, Same(l => l.AngleStepDeg));
        Num(_picCrossAngle, Same(l => l.CrossAngleDeg));
        _picCross.IsChecked = Same(l => l.Crosshatch);
        _picCrossRow!.IsVisible = ls.Any(l => l.Crosshatch);
        _picPasses.Text = ls.Count == 1 ? PassAngles(ls[0]) : "";
        _outlineAfter.IsVisible = anyFill;
        _crossRow.IsVisible = anyFill && ls.Any(l => l.Crosshatch);
        _stepRows.IsVisible = anyCut || anyFill;
        Num(_crossAngle, Same(l => l.CrossAngleDeg));
        _order.SelectedIndex = Same(l => l.Order) is { } od ? (int)od : -1;
        _startNearest.IsChecked = Same(l => l.StartNearest);
        _removeOverlaps.IsChecked = Same(l => l.RemoveOverlaps);
        Num(_leadOut, Same(l => l.LeadOutMm));
        Num(_dwell, Same(l => (double)l.LineDwellMs));
        Num(_tabPower, Same(l => l.TabPowerPct));
        _outlineAfter.IsChecked = Same(l => l.OutlineAfter);
        _scanReverse.IsChecked = Same(l => l.ScanReverse);
        _spread.IsChecked = Same(l => l.SpreadLines);
        _scatter.IsChecked = Same(l => l.SpreadScatter);
        _spread.IsVisible = (anyFill && !allOffset) || (anyImage && !allSlices);
        _scatter.IsVisible = _spread.IsVisible && ls.Any(l => l.SpreadLines);
        _deepestFirst.IsChecked = Same(l => l.Slices.DeepestFirst);
        Num(_perf, Same(l => l.PerforateMm));
        Num(_perfGap, Same(l => l.PerforateGapMm));
        Num(_dotMode, Same(l => l.DotModeMm));
        Num(_ramp, Same(l => l.RampMm));
        _rampRow.IsVisible = anyImage && ls.Any(l => l.Dither == DitherMethod.Threshold && l.ImageMode == ImageEngraveMode.Dither);
        _passThrough.IsChecked = Same(l => l.PassThrough);
        _passThrough.IsVisible = anyImage && !allSlices;
        if (anyCut && ls.Any(l => l.DotModeMm > 0)) _dotRow.IsVisible = true;
        _cutExtras.IsVisible = anyCut;
        _fillExtras.IsVisible = anyFill || anySlices;
        _fillStyleRow.IsVisible = anyFill;
        _lineRows.IsVisible = !allOffset;
        _crosshatch.IsVisible = anyFill && !allOffset;
        _imageExtras.IsVisible = anyImage;
        _sliceExtras.IsVisible = anySlices;
        _cleanRows.IsVisible = anySlices && ls.Any(l => l.Slices.CleanupEvery > 0);
        _cleanFreqRow.IsVisible = anyRed;
        _pulseRow.IsVisible = _freqRow.IsVisible = anyRed;
        _moreNothing.IsVisible = false;
        _fillStyle.SelectedIndex = Same(l => l.FillStyle) is { } fs ? (int)fs : -1;
        Num(_angleStep, Same(l => l.AngleStepDeg));
        _turnRuns.IsChecked = Same(l => l.TurnEachRun);
        int runs = ls.Max(l => l.RunCount);
        _runCount.Text = runs == 0 ? "Not run yet" : $"Run {runs} time{(runs == 1 ? "" : "s")} so far";
        _runRow.IsVisible = ls.Any(l => l.TurnEachRun);
        _resetRuns.IsEnabled = runs > 0;
        Num(_focusOff, Same(l => l.FocusOffsetMm));
        Num(_slices, Same(l => (double)l.Slices.Count));
        _negative.IsChecked = Same(l => l.Slices.Negative);
        Num(_dropMm, Same(l => l.Slices.DropMm));
        Num(_dropEvery, Same(l => (double)l.Slices.DropEvery));
        Num(_cleanEvery, Same(l => (double)l.Slices.CleanupEvery));
        Num(_cleanPower, Same(l => l.Slices.CleanupLaser.Power));
        Num(_cleanSpeed, Same(l => l.Slices.CleanupLaser.Speed));
        Num(_cleanLpcm, Same(l => l.Slices.CleanupLinesPerCm));
        Num(_cleanFreq, Same(l => (double)l.Slices.CleanupLaser.FrequencyKhz));
        _cleanWhole.IsChecked = Same(l => l.Slices.CleanupWhole);
        _sliceHint.Text = Same(l => l.Slices.Count) is { } sc && Same(l => l.Dpi) is { } sdpi
            ? $"{sc} slices, depth cells {25.4 / sdpi:0.###} mm. How deep each slice cuts depends on power, speed, lines and passes: try a few on scrap and measure."
            : "";
        Num(_kerf, Same(l => l.KerfMm));
        Num(_tabs, Same(l => (double)l.TabCount));
        Num(_tabWidth, Same(l => l.TabWidthMm));
        Num(_overcut, Same(l => l.OvercutMm));
        Num(_leadIn, Same(l => l.LeadInMm));
        Num(_stepDown, Same(l => l.StepDownMm));
        Num(_stepFirst, Same(l => l.StepDownFirstMm));
        _wobble.IsChecked = Same(l => l.Wobble);
        Num(_wobbleD, Same(l => l.WobbleDiameterMm));
        Num(_wobbleS, Same(l => l.WobbleSpacingMm));
        Num(_fillAngle, Same(l => l.FillAngleDeg));
        Num(_overscan, Same(l => l.OverscanMm));
        _crosshatch.IsChecked = Same(l => l.Crosshatch);
        var im = Same(l => l.ImageMode);
        _imageMode.SelectedIndex = im is { } imv ? (int)imv : -1;
        _grayRow.IsVisible = ls.Any(l => l.ImageMode == ImageEngraveMode.Grayscale);
        Num(_grayMin, Same(l => l.GrayMinPower));
        Num(_dpi, Same(l => l.Dpi));
        Num(_dotUs, Same(l => (double)l.DotUs));
        var dith = Same(l => l.Dither);
        _dither.SelectedIndex = dith is { } dm ? (int)dm : -1;
        _dither.PlaceholderText = dith is null ? "mixed" : "";
        _dpiHint.Text = Same(l => l.Dpi) is { } dpi ? $"Dots {25.4 / dpi:0.###} mm apart." : "";
        _wobble.IsVisible = anyRed;
        _wobbleRows.IsVisible = anyRed && ls.Any(l => l.Wobble);
        var pw = Same(l => l.Laser.PulseWidthNs);
        _pulse.SelectedIndex = pw is { } p ? _machine.Mopa.ToList().FindIndex(x => x.PulseWidthNs == p) : -1;
        _pulse.PlaceholderText = pw is null ? "mixed" : "";
        Num(_freq, Same(l => (double)l.Laser.FrequencyKhz));
        var lim = pw is { } pv ? _machine.MopaLimitFor(pv) : null;
        _freq.Maximum = lim?.MaxKhz ?? 4000;

        // Plain-language hints with the exact values sent.
        int passes = Same(l => l.Laser.Passes) ?? 2;
        _passHint.Text = passes <= 1 ? ""
            : _settings.WholeJobPasses ? "Passes run over the whole job, one round at a time (set in the job bar)."
            : "Each path repeats all its passes before the next one.";
        _passHint.IsVisible = _passHint.Text.Length > 0;
        _lpcmHint.Text = Same(l => l.LinesPerCm) is { } lp
            ? $"Lines {10 / lp:0.###} mm apart." + (lp > 1000 ? " Very fine: much longer jobs and more heat (thin sheet can warp)." : "")
            : "";
        _reflective.IsChecked = Same(l => l.Reflective);
        _freqHint.Text = lim is not { } lm ? "" : FrequencyHint(lm, Same(l => l.Laser.FrequencyKhz), ls.Any(l => l.Reflective));
        _freqHint.Foreground = lim is { } lw && Same(l => l.Laser.FrequencyKhz) is { } fw && fw < lw.DefaultKhz ? Brush("Warn") : Brush("TextMuted");
    }

    /// <summary>What the source can do at this pulse width and frequency, in plain words.</summary>
    static string FrequencyHint(MopaLimit lm, int? f, bool reflective)
    {
        string range = $"Full power from {lm.DefaultKhz} to {lm.MaxKhz} kHz at {lm.PulseWidthNs} ns.";
        if (reflective && lm.GapFromKhz is { } g) range += $" On this reflective material {g}–{lm.DefaultKhz - 1} kHz is skipped to protect the source.";
        if (f is not { } khz || khz >= lm.DefaultKhz) return range;
        double share = lm.PowerAvailable(khz);
        return $"At {khz} kHz each pulse is already as big as the source allows, so it gives only about {share * 100:0}% of its 60 W " +
               $"(100% power here ≈ {share * 100:0}% at full power). Fine for colour and gentle marks. " + range;
    }

    // ---- material ---------------------------------------------------------------

    List<PhotonCanvas.Doc.Materials.Recipe> _recipeChoices = [];

    /// <summary>Re-read the library (after it changed) and show the layer's material.</summary>
    public void RefreshMaterials()
    {
        _updating = true;
        try { RefreshMaterial(); }
        finally { _updating = false; }
    }

    void RefreshMaterial()
    {
        var lib = Library;
        var ls = EditLayersSet().ToList();
        var mats = lib?.Materials ?? [];
        _material.ItemsSource = mats.Select(m => m.Label).Prepend("None").ToList();
        var ids = ls.Select(l => l.Material?.MaterialId).Distinct().ToList();
        var mat = ids.Count == 1 && ids[0] is { } id ? lib?.Find(id) : null;
        _material.SelectedIndex = ids.Count != 1 ? -1 : mat is null ? (ids[0] is null ? 0 : -1) : mats.IndexOf(mat) + 1;
        _material.PlaceholderText = ids.Count != 1 ? "mixed" : ls.FirstOrDefault()?.Material is { } missing && mat is null ? $"{missing.Label} (not in your library)" : "";

        _recipeChoices = mat?.Recipes.OrderBy(r => r.Purpose).ThenByDescending(r => r.Rating).ToList() ?? [];
        _recipe.ItemsSource = _recipeChoices.Select(r => $"{r.Purpose} · {r.Title}  {PhotonCanvas.App.Stars.Text(r.Rating)}{(r.Tested ? "  ✓" : "")}").ToList();
        _recipe.IsEnabled = _recipeChoices.Count > 0;
        _recipe.PlaceholderText = mat is null ? "Pick a material first" : _recipeChoices.Count == 0 ? "None saved yet" : "Apply saved settings…";
        var layer = ls.FirstOrDefault(l => l.Id == _doc.ActiveLayer) ?? ls.FirstOrDefault();
        var used = layer?.Material?.RecipeId is { } rid ? _recipeChoices.FirstOrDefault(r => r.Id == rid) : null;
        _recipe.SelectedIndex = used is null ? -1 : _recipeChoices.IndexOf(used);

        _recipeInfo.Text = "";
        _recipePhoto.IsVisible = false;
        if (used is not null && layer is not null)
        {
            bool same = PhotonCanvas.Doc.Materials.ProcessSettings.From(layer) == used.Settings;
            string who = !used.Tested ? "untested" : used.TestedBy.Length == 0 ? "tested by you" : $"tested by {used.TestedBy}";
            _recipeInfo.Text = (same ? $"Using “{used.Title}”, {who}." : $"Changed since applying “{used.Title}”.") +
                (used.FocusMm is { } f ? $" Focus {f:0.0} mm." : "") + (used.Notes.Length > 0 ? $"\n{used.Notes}" : "");
            if (lib?.PhotoPath(used.Photo) is { } path)
            {
                try
                {
                    var old = _recipePhoto.Source as IDisposable;
                    _recipePhoto.Source = new Avalonia.Media.Imaging.Bitmap(path);
                    old?.Dispose();
                    _recipePhoto.IsVisible = true;
                }
                catch (Exception e) when (e is ArgumentException or IOException) { }
            }
        }
        else if (mat is null && lib is { Materials.Count: 0 })
            _recipeInfo.Text = "Your library is empty: set up a layer that works, then Save to library.";
        _recipeInfo.IsVisible = _recipeInfo.Text.Length > 0;
    }

    void MaterialChosen()
    {
        if (_updating || Library is not { } lib || _material.SelectedIndex < 0) return;
        if (_material.SelectedIndex == 0)
        {
            EditLayers("No material", l => l with { Material = null }, true);
            return;
        }
        var m = lib.Materials[_material.SelectedIndex - 1];
        EditLayers($"Material: {m.Label}", l => WithTone(WithReflective(l with { Material = new PhotonCanvas.Doc.Materials.MaterialRef(m.Id, m.Label) }, m.HighlyReflective), m), true);
    }

    void RecipeChosen()
    {
        if (_updating || Library is not { } lib || _recipe.SelectedIndex < 0 || _recipe.SelectedIndex >= _recipeChoices.Count) return;
        var layer = EditLayersSet().FirstOrDefault();
        if (layer?.Material is not { } mref || lib.Find(mref.MaterialId) is not { } m) return;
        ApplyRecipe(m, _recipeChoices[_recipe.SelectedIndex]);
    }

    /// <summary>
    /// Put a recipe's settings on the selected layers and remember which it was. With several layers
    /// selected (shapes on different layers picked together), only the layers it suits take it: a
    /// picture recipe goes on layers with pictures, a cut, score or fill recipe on layers with
    /// outlines, so picking a photo's settings doesn't turn the cut layer next to it into a picture layer.
    /// </summary>
    public void ApplyRecipe(PhotonCanvas.Doc.Materials.Material m, PhotonCanvas.Doc.Materials.Recipe r)
    {
        if (_updating) return;
        var ids = _layers.SelectedLayers;
        if (ids.Count == 0) return;
        var suited = ids.Where(id => _doc.State.FindLayer(id) is { } l && Suits(l, r.Settings.Mode)).ToHashSet();
        IReadOnlySet<Guid> targets = ids.Count > 1 && suited.Count > 0 ? suited : ids;
        _doc.Commit(targets.Count > 1 ? $"Apply {m.Label} · {r.Title} ({targets.Count} layers)" : $"Apply {m.Label} · {r.Title}",
            Ops.UpdateLayers(_doc.State, targets, l => WithReflective(r.Settings.ApplyTo(l) with { Material = new PhotonCanvas.Doc.Materials.MaterialRef(m.Id, m.Label, r.Id, r.Title) }, m.HighlyReflective)));
    }

    /// <summary>Whether settings of this kind fit what's on the layer (empty layers take anything).</summary>
    bool Suits(Layer l, LayerMode mode)
    {
        var on = _doc.State.ShapesFor(l).ToList();
        if (on.Count == 0) return true;
        bool pictures = on.OfType<ImageShape>().Any(), outlines = on.OfType<PathShape>().Any();
        return mode == LayerMode.Image ? pictures : outlines;
    }

    /// <summary>What a picture layer's passes do, spelled out: "3 passes, rows at 0°, 60° and 120°; each at 30% (90% in all)".</summary>
    static string PassAngles(Layer l)
    {
        int passes = Math.Max(1, l.Laser.Passes);
        bool turns = l.AngleStepDeg != 0 && passes > 1;
        if (!turns && !l.Crosshatch && l.FillAngleDeg % 360 == 0) return passes > 1 ? $"{passes} passes, all with rows left to right." : "";
        var angles = new List<double>();
        for (int p = 0; p < Math.Min(passes, 8); p++)
        {
            double a = l.FillAngleDeg + (turns ? p * l.AngleStepDeg : 0);
            angles.Add(a);
            if (l.Crosshatch) angles.Add(a + l.CrossAngleDeg);
        }
        static string Deg(double a) => $"{((a % 180) + 180) % 180:0.#}°";
        int burns = passes * (l.Crosshatch ? 2 : 1);
        string list = string.Join(", ", angles.Select(Deg)) + (passes > 8 ? ", …" : "");
        return $"{burns} burn{(burns == 1 ? "" : "s")}, rows at {list}; each at {l.Laser.Power:0.#}%, so the darkest spots get {burns}× that.";
    }

    /// <summary>
    /// A picture layer on a material that has had a tone test takes its correction (from the tested
    /// settings nearest the layer's own), so its pictures come out, and show, as they look.
    /// </summary>
    static Layer WithTone(Layer l, PhotonCanvas.Doc.Materials.Material m)
    {
        if (l.Mode != LayerMode.Image) return l;
        var tested = m.Recipes.Where(r => r.Settings.MaterialTone is not null)
            .OrderBy(r => Math.Abs(r.Settings.Laser.Power - l.Laser.Power) + Math.Abs(r.Settings.Laser.Speed - l.Laser.Speed) / 10 + Math.Abs(r.Settings.Dpi - l.Dpi) / 20)
            .ThenByDescending(r => r.TestedOn)
            .FirstOrDefault();
        return tested is null ? l : l with { MaterialTone = tested.Settings.MaterialTone };
    }

    /// <summary>The layer takes the material's reflective tag (and a frequency that respects it).</summary>
    Layer WithReflective(Layer l, bool reflective)
    {
        var next = l with { Reflective = reflective };
        return _machine.MopaLimitFor(next.Laser.PulseWidthNs) is { } lim ? next with { Laser = next.Laser with { FrequencyKhz = lim.Safe(next.Laser.FrequencyKhz, reflective) } } : next;
    }
}

/// <summary>A row of mutually exclusive choices (one is lit). -1 = none (mixed values).</summary>
public sealed class Segmented : UserControl
{
    readonly List<ToggleButton> _buttons = [];
    int _index = -1;

    public event Action<int>? Changed;

    public Segmented(params (string Label, string? Icon)[] items)
    {
        var grid = new Grid { ColumnDefinitions = new(string.Join(",", items.Select(_ => "*"))) };
        for (int i = 0; i < items.Length; i++)
        {
            var (label, icon) = items[i];
            object content = label;
            if (icon is not null)
                content = new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 4, HorizontalAlignment = HorizontalAlignment.Center,
                    Children =
                    {
                        new PathIcon { Data = Icon(icon), Width = 12, Height = 12, VerticalAlignment = VerticalAlignment.Center },
                        new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center },
                    },
                };
            var b = new ToggleButton { Content = content, Classes = { "seg" } };
            if (i == 0) b.Classes.Add("first");
            if (i == items.Length - 1) b.Classes.Add("last");
            if (i > 0) b.Margin = new(-1, 0, 0, 0);
            int at = i;
            b.Click += (_, _) =>
            {
                b.IsChecked = true; // clicking the lit one keeps it lit
                if (_index == at) return;
                SelectedIndex = at;
                Changed?.Invoke(at);
            };
            Grid.SetColumn(b, i);
            grid.Children.Add(b);
            _buttons.Add(b);
        }
        Content = grid;
    }

    public int SelectedIndex
    {
        get => _index;
        set
        {
            _index = value;
            for (int i = 0; i < _buttons.Count; i++) _buttons[i].IsChecked = i == value;
        }
    }
}
