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
    readonly ComboBox _dither = new() { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = new[] { "Jarvis (smooth)", "Floyd–Steinberg", "Stucki", "Atkinson (contrasty)", "Bayer (pattern)", "Threshold (line art)" } };
    readonly TextBlock _lpcmHint = Hint(), _dpiHint = Hint(), _passHint = Hint(), _freqHint = Hint();
    readonly Control _lpcmRow, _dpiRow, _ditherRow, _dotRow;

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
    readonly ComboBox _imageMode = new() { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = new[] { "Dots (dithered)", "Greyscale (experimental)" } };
    readonly NumericUpDown _grayMin = Number(0, 100, 1, "0.#");
    readonly ComboBox _pulse = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly NumericUpDown _freq = Number(1, 4000, 5, "0");
    readonly Control _cutExtras, _fillExtras, _imageExtras, _grayRow, _pulseRow, _freqRow;
    readonly TextBlock _moreNothing = Hint("Nothing more for this kind of layer.");

    // material
    readonly ComboBox _material = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
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

        _cutExtras = new StackPanel
        {
            Spacing = 6,
            Children =
            {
                Field("Kerf (mm)", _kerf, tip: "Width of the cut line. Outlines move out by half of it so parts come out at their drawn size (holes stay true too). 0 = off."),
                Field("Tabs", _tabs, tip: "Small uncut bridges in each closed outline so parts stay in the sheet. 0 = none."),
                Field("Tab width (mm)", _tabWidth),
                Field("Overcut (mm)", _overcut, tip: "Keep cutting this far past the start so the outline closes cleanly. Ignored when tabs are on."),
                Field("Lead-in (mm)", _leadIn, tip: "Start each outline with a short entry line in the waste, so the start mark isn't on the part."),
                Field("Step down/pass", _stepDown, tip: "Lower the head this many mm before each pass after the first, so the focus follows the cut into thick material. The head goes back up afterwards. 0 = off."),
                Field("First step down", _stepFirst, tip: "With step-down: also lower the head this much before the first pass (mm)."),
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
        _fillExtras = new StackPanel
        {
            Spacing = 6,
            Children =
            {
                Field("Scan angle (°)", _fillAngle, tip: "Direction of the fill lines. 0 = left to right."),
                _crosshatch,
                Field("Overscan (mm)", _overscan, tip: "Each line starts and ends this far outside the shape with the laser off, so edges burn evenly."),
            },
        };
        _grayRow = Field("Lightest power %", _grayMin, tip: "Power for the palest tones that still burn; the darkest tones get the layer's power.");
        _imageExtras = new StackPanel
        {
            Spacing = 6,
            Children =
            {
                Field("Picture mode", _imageMode, tip: "Dots: every dot at full power, tone from dot spacing (proven). Greyscale: each dot's power follows the picture (not yet tested on your machine — try it on scrap)."),
                _grayRow,
                _dotRow,
            },
        };
        _pulseRow = Field("Pulse width", _pulse, tip: "MOPA only. How long each laser pulse lasts. Short pulses mark crisply; long pulses put in more heat.");
        _freqRow = Field("Frequency", _freq, _freqHint, "MOPA only. Pulses per second (kHz). Higher = pulses overlap more, smoother lines, less energy per pulse.");
        _more.Content = new StackPanel { Spacing = 6, Children = { _cutExtras, _fillExtras, _imageExtras, _pulseRow, _freqRow, _moreNothing } };
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
                _lpcmRow, _dpiRow, _ditherRow,
                Field("Material", _material),
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
        _imageMode.SelectionChanged += (_, _) => EditLayers("Picture mode", l => l with { ImageMode = (ImageEngraveMode)Math.Max(0, _imageMode.SelectedIndex) }, _imageMode.SelectedIndex >= 0);
        _grayMin.ValueChanged += (_, _) => EditLayers("Lightest power", l => l with { GrayMinPower = (double)_grayMin.Value!.Value }, _grayMin.Value.HasValue, "graymin");
        _pulse.SelectionChanged += (_, _) =>
        {
            if (_pulse.SelectedIndex < 0) return;
            var lim = _machine.Mopa[_pulse.SelectedIndex];
            // Keep the frequency where it can (colour work tunes it), within what this pulse width allows.
            EditLayers("Pulse width", l => l with { Laser = l.Laser with { PulseWidthNs = lim.PulseWidthNs, FrequencyKhz = lim.Safe(l.Laser.FrequencyKhz) } }, true);
        };
        _freq.ValueChanged += (_, _) => EditLayers("Frequency", l =>
        {
            int f = (int)_freq.Value!.Value;
            // Out of the range the source is protected from (long pulses): up to full power.
            return l with { Laser = l.Laser with { FrequencyKhz = _machine.MopaLimitFor(l.Laser.PulseWidthNs) is { } lim ? lim.Safe(f) : f } };
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
        _lpcmRow.IsVisible = anyFill;
        _dpiRow.IsVisible = _ditherRow.IsVisible = anyImage;
        _cutExtras.IsVisible = anyCut;
        _fillExtras.IsVisible = anyFill;
        _imageExtras.IsVisible = anyImage;
        _pulseRow.IsVisible = _freqRow.IsVisible = anyRed;
        _moreNothing.IsVisible = !anyCut && !anyFill && !anyImage && !anyRed;
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
        _freqHint.Text = lim is not { } lm ? "" : FrequencyHint(lm, Same(l => l.Laser.FrequencyKhz));
        _freqHint.Foreground = lim is { } lw && Same(l => l.Laser.FrequencyKhz) is { } fw && fw < lw.DefaultKhz ? Brush("Warn") : Brush("TextMuted");
    }

    /// <summary>What the source can do at this pulse width and frequency, in plain words.</summary>
    static string FrequencyHint(MopaLimit lm, int? f)
    {
        string range = $"Full power from {lm.DefaultKhz} to {lm.MaxKhz} kHz at {lm.PulseWidthNs} ns.";
        if (lm.GapFromKhz is { } g) range += $" {g}–{lm.DefaultKhz - 1} kHz is skipped to protect the source.";
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
        EditLayers($"Material: {m.Label}", l => l with { Material = new PhotonCanvas.Doc.Materials.MaterialRef(m.Id, m.Label) }, true);
    }

    void RecipeChosen()
    {
        if (_updating || Library is not { } lib || _recipe.SelectedIndex < 0 || _recipe.SelectedIndex >= _recipeChoices.Count) return;
        var layer = EditLayersSet().FirstOrDefault();
        if (layer?.Material is not { } mref || lib.Find(mref.MaterialId) is not { } m) return;
        ApplyRecipe(m, _recipeChoices[_recipe.SelectedIndex]);
    }

    /// <summary>Put a recipe's settings on the selected layers and remember which it was.</summary>
    public void ApplyRecipe(PhotonCanvas.Doc.Materials.Material m, PhotonCanvas.Doc.Materials.Recipe r) =>
        EditLayers($"Apply {m.Label} · {r.Title}", l => r.Settings.ApplyTo(l) with { Material = new PhotonCanvas.Doc.Materials.MaterialRef(m.Id, m.Label, r.Id, r.Title) }, true);
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
