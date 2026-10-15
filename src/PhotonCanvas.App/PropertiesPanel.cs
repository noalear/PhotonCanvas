using System.Collections.Immutable;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using PhotonCanvas.Core;
using PhotonCanvas.Core.Imaging;
using PhotonCanvas.Doc;
using static PhotonCanvas.App.UiKit;

namespace PhotonCanvas.App;

/// <summary>
/// Selection geometry and layer settings. Every setting says, in plain words,
/// what it does and the exact value that is sent to the machine.
/// </summary>
public sealed class PropertiesPanel : UserControl
{
    readonly Document _doc;
    readonly LayersPanel _layers;
    readonly AppSettings _settings;
    readonly MachineProfile _machine = MachineProfile.F2Ultra;
    bool _updating;

    // selection
    readonly Border _selCard;
    readonly NumericUpDown _x = Number(-1000, 1000, 1, "0.###"), _y = Number(-1000, 1000, 1, "0.###");
    readonly NumericUpDown _w = Number(0.01m, 2000, 1, "0.###"), _h = Number(0.01m, 2000, 1, "0.###");
    readonly NumericUpDown _rot = Number(-180, 180, 15, "0.#");
    readonly NumericUpDown _scaleX = Number(0.1m, 100000, 5, "0.#"), _scaleY = Number(0.1m, 100000, 5, "0.#");
    readonly CheckBox _lockRatio = new() { Content = "Keep proportions", IsChecked = true };
    readonly ComboBox _shapeLayer = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly TextBlock _selInfo = Hint();

    // layer settings
    readonly TextBlock _layerTitle = new() { Classes = { "h" } };
    readonly ComboBox _mode = new() { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = new[] { "Cut through", "Score a line", "Engrave area (fill)", "Engrave picture" } };
    readonly CheckBox _advanced = new() { Content = "Show advanced settings" };
    readonly NumericUpDown _dpi = Number(50, 1000, 10, "0");
    readonly NumericUpDown _dotUs = Number(10, 10000, 10, "0");
    readonly ComboBox _dither = new() { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = new[] { "Jarvis (smooth, default)", "Floyd–Steinberg", "Stucki", "Atkinson (high contrast)", "Bayer (regular pattern)", "Threshold (line art)" } };
    readonly TextBlock _dpiHint = Hint();
    readonly Control _dpiRow, _dotRow, _ditherRow;

    // picture
    readonly Border _picCard;
    readonly Slider _bright = new() { Minimum = -1, Maximum = 1 };
    readonly Slider _contrast = new() { Minimum = -1, Maximum = 1 };
    readonly Slider _gamma = new() { Minimum = 0.2, Maximum = 3, Value = 1 };
    readonly CheckBox _invertPic = new() { Content = "Invert (for dark materials)" };

    /// <summary>The user asked to trace a picture.</summary>
    public event Action<ImageShape>? TraceRequested;

    /// <summary>The user asked for a depthmap of a picture (DepthCharge).</summary>
    public event Action<ImageShape>? DepthmapRequested;

    /// <summary>The user asked to snap the selection onto a piece seen by the camera.</summary>
    public event Action? FitToCameraRequested;

    // text
    readonly Border _textCard;
    readonly TextBox _textBox = new() { AcceptsReturn = true, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MinHeight = 60 };
    readonly ComboBox _font = new() { HorizontalAlignment = HorizontalAlignment.Stretch, MaxDropDownHeight = 420 };
    readonly NumericUpDown _textSize = Number(0.5m, 500, 1, "0.##");
    readonly CheckBox _bold = new() { Content = "Bold" }, _italic = new() { Content = "Italic" };
    readonly NumericUpDown _letter = Number(-50, 200, 0.1m, "0.##"), _lineSp = Number(0.5m, 5, 0.1m, "0.##");
    readonly ComboBox _align = new() { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = new[] { "Left", "Centre", "Right" } };
    readonly NumericUpDown _bend = Number(-360, 360, 5, "0.#");
    IReadOnlyList<string> _fonts = [];

    // cut / fill / image extras
    readonly NumericUpDown _kerf = Number(-1, 1, 0.01m, "0.###"), _tabs = Number(0, 100, 1, "0"), _tabWidth = Number(0.1m, 10, 0.1m, "0.##");
    readonly NumericUpDown _overcut = Number(0, 10, 0.1m, "0.##"), _leadIn = Number(0, 10, 0.5m, "0.##");
    readonly NumericUpDown _fillAngle = Number(-180, 180, 15, "0.#"), _overscan = Number(0, 20, 0.5m, "0.##");
    readonly CheckBox _crosshatch = new() { Content = "Crosshatch (second pass at 90°)" };
    readonly ComboBox _imageMode = new() { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = new[] { "Dots (dithered)", "Greyscale (experimental)" } };
    readonly NumericUpDown _grayMin = Number(0, 100, 1, "0.#");
    readonly Control _cutExtras, _fillExtras, _imageExtras, _grayRow;

    /// <summary>Put the cursor in the text box (after creating or double-clicking text).</summary>
    public void FocusText(bool selectAll = false)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _textBox.Focus();
            if (selectAll) _textBox.SelectAll();
        });
    }
    readonly ComboBox _light = new() { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = new[] { "Blue diode 455 nm", "Red fiber 1064 nm" } };
    readonly NumericUpDown _power = Number(0, 100, 1, "0.#");
    readonly NumericUpDown _speed = Number(2, 15000, 5, "0.#");
    readonly NumericUpDown _passes = Number(1, 10000, 1, "0");
    readonly NumericUpDown _lpcm = Number(1, 1000, 10, "0.#");
    readonly ComboBox _pulse = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly NumericUpDown _freq = Number(1, 4000, 5, "0");
    readonly TextBlock _passHint = Hint(), _lpcmHint = Hint(), _freqHint = Hint();
    readonly Control _lpcmRow, _pulseRow, _freqRow;
    readonly ComboBox _material = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly ComboBox _recipe = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly TextBlock _recipeInfo = Hint();
    readonly Image _recipePhoto = new() { MaxHeight = 110, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left, IsVisible = false };

    public PropertiesPanel(Document doc, LayersPanel layers, AppSettings settings)
    {
        _doc = doc;
        _layers = layers;
        _settings = settings;
        _pulse.ItemsSource = _machine.Mopa.Select(m => $"{m.PulseWidthNs} ns").ToList();

        // ---- selection card ----
        var center = Button("Centre in work area", tip: "Move the selection so it sits in the middle of the 220 × 220 mm field.");
        var fitCam = Button("Fit to piece in camera…", "IcCamera", "Snap the selection onto the real piece the camera sees: moves, turns and (optionally) resizes it to match (Ctrl+Shift+F).");
        fitCam.Click += (_, _) => FitToCameraRequested?.Invoke();
        center.Click += (_, _) => CenterInField();
        _selCard = Card("Selection",
            Row(Field2("X", _x), Field2("Y", _y)),
            Row(Field2("W", _w), Field2("H", _h)),
            Row(Field2("Scale %", _scaleX), Field2("", _scaleY)),
            _lockRatio,
            Field("Angle °", _rot, tip: "The shape's own rotation, clockwise. 0 = as drawn or imported. Changes apply straight away."),
            Field("Layer", _shapeLayer, tip: "Move the selected shapes to another layer (Ctrl+1…9 also works)."),
            center,
            fitCam,
            _selInfo);
        _x.ValueChanged += (_, _) => MoveTo();
        _y.ValueChanged += (_, _) => MoveTo();
        _w.ValueChanged += (_, _) => Resize(true);
        _scaleX.ValueChanged += (_, _) => Rescale(true);
        _scaleY.ValueChanged += (_, _) => Rescale(false);
        _rot.ValueChanged += (_, _) => RotateTo();
        ToolTip.SetTip(_scaleX, "Size relative to the original, horizontally (in the shape's own direction).");
        ToolTip.SetTip(_scaleY, "Size relative to the original, vertically.");
        _h.ValueChanged += (_, _) => Resize(false);
        _shapeLayer.SelectionChanged += (_, _) => ChangeShapeLayer();

        // ---- layer settings card ----
        _lpcmRow = Field("Lines / cm", _lpcm, _lpcmHint, "How densely a fill is scanned. More lines = darker, smoother, slower.");
        _dpiRow = Field("Resolution", _dpi, _dpiHint, "Dots per inch. Higher = finer detail, slower. 150–300 suits most wood.");
        _dotRow = Field("Dot time", _dotUs, tip: "How long each dot fires, in microseconds. Longer = darker dots.");
        _ditherRow = Field("Dithering", _dither, tip: "How grey tones become dots. Jarvis suits photos; Threshold suits logos and line art.");
        _dpi.ValueChanged += (_, _) => EditLayers("Resolution", l => l with { Dpi = (double)_dpi.Value!.Value }, _dpi.Value.HasValue, "dpi");
        _dotUs.ValueChanged += (_, _) => EditLayers("Dot time", l => l with { DotUs = (int)(Math.Round(_dotUs.Value!.Value / 10) * 10) }, _dotUs.Value.HasValue, "dot");
        _dither.SelectionChanged += (_, _) => EditLayers("Dithering", l => l with { Dither = (DitherMethod)Math.Max(0, _dither.SelectedIndex) }, _dither.SelectedIndex >= 0);

        var trace = Button("Trace into vectors…", tip: "Turn the picture into cut/engrave outlines (Ctrl+T).");
        trace.Click += (_, _) => { if (SelectedPicture() is { } im) TraceRequested?.Invoke(im); };
        var depth = Button("Depthmap…", tip: "Turn the picture into a relief depthmap with DepthCharge.");
        depth.Click += (_, _) => { if (SelectedPicture() is { } im) DepthmapRequested?.Invoke(im); };
        var resetTone = Button("Reset");
        resetTone.Click += (_, _) => EditPicture("Reset picture", _ => ToneAdjust.None);
        _picCard = Card("Picture",
            Slider("Brightness", _bright), Slider("Contrast", _contrast), Slider("Gamma", _gamma), _invertPic,
            Hint("These change how the picture engraves (Image layers), not the file."),
            Row(trace, depth),
            resetTone);
        _bright.ValueChanged += (_, _) => EditPicture("Picture brightness", t => t with { Brightness = _bright.Value }, "bright");
        _contrast.ValueChanged += (_, _) => EditPicture("Picture contrast", t => t with { Contrast = _contrast.Value }, "contrast");
        _gamma.ValueChanged += (_, _) => EditPicture("Picture gamma", t => t with { Gamma = _gamma.Value }, "gamma");
        _invertPic.IsCheckedChanged += (_, _) => EditPicture("Invert picture", t => t with { Invert = _invertPic.IsChecked == true });
        _cutExtras = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                Field("Kerf (mm)", _kerf, tip: "Width of the cut line. Outlines move out by half of it so parts come out at their drawn size (holes stay true too). 0 = off."),
                Field("Tabs", _tabs, tip: "Small uncut bridges in each closed outline so parts stay in the sheet. 0 = none."),
                Field("Tab width (mm)", _tabWidth),
                Field("Overcut (mm)", _overcut, tip: "Keep cutting this far past the start so the outline closes cleanly. Ignored when tabs are on."),
                Field("Lead-in (mm)", _leadIn, tip: "Start each outline with a short entry line in the waste, so the start mark isn't on the part."),
            },
        };
        _fillExtras = new StackPanel
        {
            Spacing = 8,
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
            Spacing = 8,
            Children =
            {
                Field("Picture mode", _imageMode, tip: "Dots: every dot at full power, tone from dot spacing (proven). Greyscale: each dot's power follows the picture (not yet tested on your machine — try it on scrap)."),
                _grayRow,
            },
        };
        _kerf.ValueChanged += (_, _) => EditLayers("Kerf", l => l with { KerfMm = (double)_kerf.Value!.Value }, _kerf.Value.HasValue, "kerf");
        _tabs.ValueChanged += (_, _) => EditLayers("Tabs", l => l with { TabCount = (int)_tabs.Value!.Value }, _tabs.Value.HasValue, "tabs");
        _tabWidth.ValueChanged += (_, _) => EditLayers("Tab width", l => l with { TabWidthMm = (double)_tabWidth.Value!.Value }, _tabWidth.Value.HasValue, "tabw");
        _overcut.ValueChanged += (_, _) => EditLayers("Overcut", l => l with { OvercutMm = (double)_overcut.Value!.Value }, _overcut.Value.HasValue, "overcut");
        _leadIn.ValueChanged += (_, _) => EditLayers("Lead-in", l => l with { LeadInMm = (double)_leadIn.Value!.Value }, _leadIn.Value.HasValue, "leadin");
        _fillAngle.ValueChanged += (_, _) => EditLayers("Scan angle", l => l with { FillAngleDeg = (double)_fillAngle.Value!.Value }, _fillAngle.Value.HasValue, "fangle");
        _overscan.ValueChanged += (_, _) => EditLayers("Overscan", l => l with { OverscanMm = (double)_overscan.Value!.Value }, _overscan.Value.HasValue, "overscan");
        _crosshatch.IsCheckedChanged += (_, _) => EditLayers("Crosshatch", l => l with { Crosshatch = _crosshatch.IsChecked == true }, _crosshatch.IsChecked.HasValue);
        _imageMode.SelectionChanged += (_, _) => EditLayers("Picture mode", l => l with { ImageMode = (ImageEngraveMode)Math.Max(0, _imageMode.SelectedIndex) }, _imageMode.SelectedIndex >= 0);
        _grayMin.ValueChanged += (_, _) => EditLayers("Lightest power", l => l with { GrayMinPower = (double)_grayMin.Value!.Value }, _grayMin.Value.HasValue, "graymin");

        // ---- text card ----
        _fonts = TextLayout.FontFamilies();
        _font.ItemsSource = _fonts;
        var toPaths = Button("Convert to paths", tip: "Turn the text into ordinary shapes (no longer editable as text).");
        toPaths.Click += (_, _) =>
        {
            if (SelectedText() is { } t)
                _doc.Commit("Convert to paths", Ops.Replace(_doc.State, new PathShape { Id = t.Id, Name = t.Name, LayerId = t.LayerId, GroupId = t.GroupId, Transform = t.Transform, Geometry = t.Geometry }));
        };
        _textCard = Card("Text",
            _textBox,
            Field("Font", _font),
            Row(Field2("Size mm", _textSize), new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _bold, _italic } }),
            Field("Letter spacing", _letter, tip: "Extra space between letters, mm (negative tightens)."),
            Field("Line spacing", _lineSp, tip: "Line height as a multiple of the size."),
            Field("Align", _align),
            Field("Curve (°)", _bend, tip: "Bend each line along an arc: how many degrees of a circle it covers. Positive arches up, negative curves down, 0 straight."),
            toPaths);
        _textBox.TextChanged += (_, _) => EditText(t => (_textBox.Text ?? "", t.Style), "text");
        _font.SelectionChanged += (_, _) => { if (_font.SelectedItem is string f) EditText(t => (t.Text, t.Style with { FontFamily = f }), null); };
        _textSize.ValueChanged += (_, _) => { if (_textSize.Value is { } v) EditText(t => (t.Text, t.Style with { SizeMm = (double)v }), "tsize"); };
        _bold.IsCheckedChanged += (_, _) => EditText(t => (t.Text, t.Style with { Bold = _bold.IsChecked == true }), null);
        _italic.IsCheckedChanged += (_, _) => EditText(t => (t.Text, t.Style with { Italic = _italic.IsChecked == true }), null);
        _letter.ValueChanged += (_, _) => { if (_letter.Value is { } v) EditText(t => (t.Text, t.Style with { LetterSpacingMm = (double)v }), "letter"); };
        _lineSp.ValueChanged += (_, _) => { if (_lineSp.Value is { } v) EditText(t => (t.Text, t.Style with { LineSpacing = (double)v }), "line"); };
        _align.SelectionChanged += (_, _) => { if (_align.SelectedIndex >= 0) EditText(t => (t.Text, t.Style with { Align = (TextAlign)_align.SelectedIndex }), null); };
        _bend.ValueChanged += (_, _) => { if (_bend.Value is { } v) EditText(t => (t.Text, t.Style with { BendDeg = (double)v }), "bend"); };

        _pulseRow = Field("Pulse width", _pulse, tip: "Fiber only. How long each laser pulse lasts. Short pulses mark crisply; long pulses put in more heat.");
        _freqRow = Field("Frequency", _freq, _freqHint, "Fiber only. Pulses per second (kHz). Higher = pulses overlap more, smoother lines, less energy per pulse.");
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
        var layerCard = new Border
        {
            Classes = { "card" },
            Child = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    _layerTitle,
                    Field("What to do", _mode, tip: "Cut and Score both follow the outline (Score is lighter, and runs first). Engrave the area scans back and forth to burn the inside of closed shapes. Pictures engrave as fine dots."),
                    Field("Laser", _light, tip: "Blue: 40 W diode. Red: 60 W MOPA fiber. Blue diode for wood, leather, acrylic, paper. Red fiber for metals and some plastics."),
                    Field("Power %", _power, tip: "Percent of the laser's full power."),
                    Field("Speed mm/s", _speed, tip: "How fast the beam moves while firing, mm/s. Slower = deeper/darker."),
                    Field("Passes", _passes, _passHint, "How many times this layer is processed."),
                    _lpcmRow, _dpiRow, _dotRow, _ditherRow, _pulseRow, _freqRow,
                    _cutExtras, _fillExtras, _imageExtras,
                    _advanced,
                    new Separator { Margin = new(0, 4) },
                    Field("Material", _material),
                    Field("Settings", _recipe),
                    _recipeInfo,
                    _recipePhoto,
                    Row(saveToLib, openLib),
                },
            },
        };
        _advanced.IsChecked = _settings.ShowAdvanced;
        ToolTip.SetTip(_advanced, "Kerf, tabs, overcut, lead-in, scan angle, overscan, dot time and fiber pulse settings. The defaults work for most jobs.");
        _advanced.IsCheckedChanged += (_, _) => { _settings.ShowAdvanced = _advanced.IsChecked == true; _settings.Save(); RefreshLayer(); };
        _mode.SelectionChanged += (_, _) => EditLayers("Layer mode", l => l with { Mode = (LayerMode)Math.Max(0, _mode.SelectedIndex) }, _mode.SelectedIndex >= 0);
        _light.SelectionChanged += (_, _) => EditLayers("Laser source", l => l with { Laser = l.Laser with { Light = _light.SelectedIndex == 1 ? LightSource.Red : LightSource.Blue } }, _light.SelectedIndex >= 0);
        _power.ValueChanged += (_, _) => EditLayers("Power", l => l with { Laser = l.Laser with { Power = (double)_power.Value!.Value } }, _power.Value.HasValue, "power");
        _speed.ValueChanged += (_, _) => EditLayers("Speed", l => l with { Laser = l.Laser with { Speed = (double)_speed.Value!.Value } }, _speed.Value.HasValue, "speed");
        _passes.ValueChanged += (_, _) => EditLayers("Passes", l => l with { Laser = l.Laser with { Passes = (int)_passes.Value!.Value } }, _passes.Value.HasValue, "passes");
        _lpcm.ValueChanged += (_, _) => EditLayers("Line density", l => l with { LinesPerCm = (double)_lpcm.Value!.Value }, _lpcm.Value.HasValue, "lpcm");
        _pulse.SelectionChanged += (_, _) =>
        {
            if (_pulse.SelectedIndex < 0) return;
            var lim = _machine.Mopa[_pulse.SelectedIndex];
            // A new pulse width resets the frequency to the source's recommended value for it.
            EditLayers("Pulse width", l => l with { Laser = l.Laser with { PulseWidthNs = lim.PulseWidthNs, FrequencyKhz = lim.DefaultKhz } }, true);
        };
        _freq.ValueChanged += (_, _) => EditLayers("Frequency", l => l with { Laser = l.Laser with { FrequencyKhz = (int)_freq.Value!.Value } }, _freq.Value.HasValue, "freq");
        _material.SelectionChanged += (_, _) => MaterialChosen();
        _recipe.SelectionChanged += (_, _) => RecipeChosen();

        Content = new ScrollViewer { Content = new StackPanel { Children = { _selCard, _textCard, _picCard, layerCard } } };
        _doc.Changed += _ => Refresh();
        _layers.SelectionChanged += Refresh;
        Refresh();
    }

    static Control Field2(string label, Control editor)
    {
        // Half-width boxes: no spinner buttons, so the number itself has room (arrow keys and the wheel still step it).
        if (editor is NumericUpDown n) n.ShowButtonSpinner = false;
        var g = new Grid { ColumnDefinitions = new("Auto,*"), ColumnSpacing = 6 };
        g.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Classes = { "muted" }, MinWidth = 14 });
        Grid.SetColumn(editor, 1);
        g.Children.Add(editor);
        return g;
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

    // ---- refresh -------------------------------------------------------------

    void Refresh()
    {
        _updating = true;
        try
        {
            RefreshSelection();
            RefreshLayer();
        }
        finally { _updating = false; }
    }

    static Control Slider(string label, Slider s) => new StackPanel
    {
        Children = { new TextBlock { Text = label, Classes = { "muted" } }, s },
    };

    TextShape? SelectedText() =>
        _doc.Selection.Count == 1 && _doc.State.FindShape(_doc.Selection.First()) is TextShape t ? t : null;

    void EditText(Func<TextShape, (string Text, TextStyle Style)> edit, string? coalesce)
    {
        if (_updating || SelectedText() is not { } t) return;
        var (text, style) = edit(t);
        if (text == t.Text && style == t.Style) return;
        _settings.TextFont = style.FontFamily;
        _settings.TextSizeMm = style.SizeMm;
        _doc.Commit("Edit text", Ops.Replace(_doc.State, t.Edit(text, style)), coalesceKey: coalesce is null ? null : $"{coalesce}:{t.Id}");
    }

    ImageShape? SelectedPicture() =>
        _doc.Selection.Count == 1 && _doc.State.FindShape(_doc.Selection.First()) is ImageShape im ? im : null;

    void EditPicture(string label, Func<ToneAdjust, ToneAdjust> edit, string? coalesce = null)
    {
        if (_updating || SelectedPicture() is not { } im) return;
        var tone = edit(im.Tone);
        if (tone == im.Tone) return;
        _doc.Commit(label, Ops.Replace(_doc.State, im.WithTone(tone)), coalesceKey: coalesce is null ? null : $"{coalesce}:{im.Id}");
    }

    void RefreshSelection()
    {
        var sel = _doc.Selection;
        var pic = SelectedPicture();
        _picCard.IsVisible = pic is not null;
        var txt = SelectedText();
        _textCard.IsVisible = txt is not null;
        if (txt is not null)
        {
            if (_textBox.Text != txt.Text) _textBox.Text = txt.Text;
            _font.SelectedItem = _fonts.FirstOrDefault(f => f.Equals(txt.Style.FontFamily, StringComparison.OrdinalIgnoreCase));
            _font.PlaceholderText = txt.Style.FontFamily;
            _textSize.Value = (decimal)txt.Style.SizeMm;
            _bold.IsChecked = txt.Style.Bold;
            _italic.IsChecked = txt.Style.Italic;
            _letter.Value = (decimal)txt.Style.LetterSpacingMm;
            _lineSp.Value = (decimal)txt.Style.LineSpacing;
            _align.SelectedIndex = (int)txt.Style.Align;
            _bend.Value = (decimal)txt.Style.BendDeg;
        }
        if (pic is not null)
        {
            _bright.Value = pic.Tone.Brightness;
            _contrast.Value = pic.Tone.Contrast;
            _gamma.Value = pic.Tone.Gamma;
            _invertPic.IsChecked = pic.Tone.Invert;
        }
        _selCard.IsVisible = sel.Count > 0;
        if (sel.Count == 0) return;
        var b = _doc.State.Bounds(sel);
        if (b.IsEmpty) return;
        Set(_x, b.MinX);
        Set(_y, b.MinY);
        Set(_w, Math.Max(0.01, b.Width));
        Set(_h, Math.Max(0.01, b.Height));
        _w.IsEnabled = b.Width > 1e-6;
        _h.IsEnabled = b.Height > 1e-6;
        var picked = _doc.State.Shapes.Where(s => sel.Contains(s.Id)).ToList();
        double? Common(Func<Shape, double> f, double tol)
        {
            double v0 = f(picked[0]);
            return picked.All(p => Math.Abs(f(p) - v0) < tol) ? v0 : null;
        }
        var rot = Common(p => p.Transform.RotationDeg, 0.01);
        _rot.Value = rot is { } r ? (decimal)Math.Round(Math.Abs(r) < 0.005 ? 0 : r, 2) : null;
        _rot.Watermark = rot is null ? "mixed" : null;
        var sx = Common(p => p.Transform.ScaleX, 1e-6);
        var sy = Common(p => Math.Abs(p.Transform.ScaleY), 1e-6);
        _scaleX.Value = sx is { } a ? (decimal)Math.Round(a * 100, 2) : null;
        _scaleY.Value = sy is { } b2 ? (decimal)Math.Round(b2 * 100, 2) : null;
        _scaleX.Watermark = sx is null ? "mixed" : null;
        _scaleY.Watermark = sy is null ? "mixed" : null;

        var layers = _doc.State.Layers;
        _shapeLayer.ItemsSource = layers.Select(l => l.Name).ToList();
        var shapeLayers = _doc.State.Shapes.Where(s => sel.Contains(s.Id)).Select(s => s.LayerId).Distinct().ToList();
        _shapeLayer.SelectedIndex = shapeLayers.Count == 1 ? layers.FindIndex(l => l.Id == shapeLayers[0]) : -1;
        _shapeLayer.PlaceholderText = shapeLayers.Count > 1 ? "Several layers" : "";

        bool outside = b.MinX < -1e-6 || b.MinY < -1e-6 || b.MaxX > _machine.FieldW + 1e-6 || b.MaxY > _machine.FieldH + 1e-6;
        _selInfo.Text = (sel.Count == 1 ? "1 shape selected" : $"{sel.Count} shapes selected") +
                        (outside ? ". ⚠ Partly outside the work area – it can't be sent like this." : ".");
        _selInfo.Foreground = outside ? Brush("Warn") : Brush("TextMuted");
    }

    static void Set(NumericUpDown n, double v)
    {
        var d = (decimal)Math.Round(v, 3);
        if (n.Value != d) n.Value = Math.Clamp(d, n.Minimum, n.Maximum);
    }

    void RefreshLayer()
    {
        var ls = EditLayersSet().ToList();
        if (ls.Count == 0) return;
        _layerTitle.Text = ls.Count == 1 ? $"LAYER · {ls[0].Name.ToUpperInvariant()}" : $"{ls.Count} LAYERS · EDITING TOGETHER";
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

        var mode = Same(l => l.Mode);
        _mode.SelectedIndex = mode is { } m ? (int)m : -1;
        _mode.PlaceholderText = mode is null ? "mixed" : "";
        var light = Same(l => l.Laser.Light);
        _light.SelectedIndex = light is { } li ? (int)li : -1;
        _light.PlaceholderText = light is null ? "mixed" : "";
        Num(_power, Same(l => l.Laser.Power));
        Num(_speed, Same(l => l.Laser.Speed));
        Num(_passes, Same(l => (double)l.Laser.Passes));
        Num(_lpcm, Same(l => l.LinesPerCm));

        bool anyFill = ls.Any(l => l.Mode == LayerMode.Fill), anyRed = ls.Any(l => l.Laser.Light == LightSource.Red);
        bool anyImage = ls.Any(l => l.Mode == LayerMode.Image);
        _lpcmRow.IsVisible = anyFill;
        bool adv = _settings.ShowAdvanced;
        _dpiRow.IsVisible = _ditherRow.IsVisible = anyImage;
        _dotRow.IsVisible = anyImage && adv;
        _cutExtras.IsVisible = adv && ls.Any(l => l.Mode is LayerMode.Cut or LayerMode.Score);
        _fillExtras.IsVisible = adv && anyFill;
        _imageExtras.IsVisible = adv && anyImage;
        Num(_kerf, Same(l => l.KerfMm));
        Num(_tabs, Same(l => (double)l.TabCount));
        Num(_tabWidth, Same(l => l.TabWidthMm));
        Num(_overcut, Same(l => l.OvercutMm));
        Num(_leadIn, Same(l => l.LeadInMm));
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
        _pulseRow.IsVisible = _freqRow.IsVisible = anyRed && adv;
        var pw = Same(l => l.Laser.PulseWidthNs);
        _pulse.SelectedIndex = pw is { } p ? _machine.Mopa.ToList().FindIndex(x => x.PulseWidthNs == p) : -1;
        _pulse.PlaceholderText = pw is null ? "mixed" : "";
        Num(_freq, Same(l => (double)l.Laser.FrequencyKhz));
        var lim = pw is { } pv ? _machine.MopaLimitFor(pv) : null;
        _freq.Maximum = lim?.MaxKhz ?? 4000;

        // Plain-language hints with the exact values sent.
        _passHint.Text = _settings.WholeJobPasses
            ? "Passes run over the whole job, one round at a time (set in Job)."
            : "Each path repeats all its passes before the next one (stock xTool behaviour).";
        _lpcmHint.Text = Same(l => l.LinesPerCm) is { } lp ? $"Lines {10 / lp:0.###} mm apart." : "";
        _freqHint.Text = lim is { } lm ? $"This pulse width allows 1–{lm.MaxKhz} kHz (recommended {lm.DefaultKhz})." : "";
    }

    // ---- selection edits -------------------------------------------------------

    void MoveTo()
    {
        if (_updating || _doc.Selection.Count == 0 || _x.Value is null || _y.Value is null) return;
        var b = _doc.State.Bounds(_doc.Selection);
        double dx = (double)_x.Value.Value - b.MinX, dy = (double)_y.Value.Value - b.MinY;
        if (Math.Abs(dx) < 1e-9 && Math.Abs(dy) < 1e-9) return;
        _doc.Commit("Move", Ops.Move(_doc.State, _doc.Selection, dx, dy), coalesceKey: "pos");
    }

    void Resize(bool widthChanged)
    {
        if (_updating || _doc.Selection.Count == 0 || _w.Value is null || _h.Value is null) return;
        var b = _doc.State.Bounds(_doc.Selection);
        double sx = b.Width > 1e-9 ? (double)_w.Value.Value / b.Width : 1;
        double sy = b.Height > 1e-9 ? (double)_h.Value.Value / b.Height : 1;
        if (_lockRatio.IsChecked == true)
        {
            if (widthChanged) sy = b.Height > 1e-9 ? sx : 1;
            else sx = b.Width > 1e-9 ? sy : 1;
        }
        if (Math.Abs(sx - 1) < 1e-9 && Math.Abs(sy - 1) < 1e-9) return;
        _doc.Commit("Resize", Ops.Transform(_doc.State, _doc.Selection, Affine.ScaleAbout(b.TopLeft, sx, sy)), coalesceKey: "size");
    }

    void RotateTo()
    {
        if (_updating || _doc.Selection.Count == 0 || _rot.Value is not { } deg) return;
        _doc.Commit("Rotate", Ops.SetRotation(_doc.State, _doc.Selection, (double)deg), coalesceKey: "angle");
    }

    void Rescale(bool xChanged)
    {
        if (_updating || _doc.Selection.Count == 0) return;
        var pics = _doc.State.Shapes.Where(s => _doc.Selection.Contains(s.Id)).ToList();
        double curX = pics[0].Transform.ScaleX * 100, curY = Math.Abs(pics[0].Transform.ScaleY) * 100;
        double x = (double)(_scaleX.Value ?? (decimal)curX), y = (double)(_scaleY.Value ?? (decimal)curY);
        if (_lockRatio.IsChecked == true)
        {
            // Keep the shape's proportions: both axes change by the same factor.
            double f = xChanged ? x / Math.Max(1e-9, curX) : y / Math.Max(1e-9, curY);
            x = curX * f;
            y = curY * f;
        }
        _doc.Commit("Scale", Ops.SetScale(_doc.State, _doc.Selection, x / 100, y / 100), coalesceKey: "scale");
    }

    void CenterInField()
    {
        if (_doc.Selection.Count == 0) return;
        var b = _doc.State.Bounds(_doc.Selection);
        var c = new Vec2(_machine.FieldW / 2, _machine.FieldH / 2);
        _doc.Commit("Centre in work area", Ops.Move(_doc.State, _doc.Selection, c.X - b.Center.X, c.Y - b.Center.Y));
    }

    void ChangeShapeLayer()
    {
        if (_updating || _shapeLayer.SelectedIndex < 0 || _doc.Selection.Count == 0) return;
        var layer = _doc.State.Layers[_shapeLayer.SelectedIndex];
        _doc.Commit("Move to layer", Ops.MoveToLayer(_doc.State, _doc.Selection, layer.Id));
    }

    // ---- material ---------------------------------------------------------------

    /// <summary>The material library (set by the main window).</summary>
    public PhotonCanvas.Doc.Materials.MaterialLibrary? Library { get; set; }

    /// <summary>Save the active layer's settings to the library.</summary>
    public event Action<Layer>? SaveToLibraryRequested;
    /// <summary>Open the library window.</summary>
    public event Action? LibraryRequested;

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
        _recipe.PlaceholderText = mat is null ? "Pick a material first" : _recipeChoices.Count == 0 ? "No saved settings yet" : "Apply saved settings…";
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
