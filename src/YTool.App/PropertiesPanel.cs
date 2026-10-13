using System.Collections.Immutable;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using YTool.Core;
using YTool.Doc;
using static YTool.App.UiKit;

namespace YTool.App;

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
    readonly NumericUpDown _rot = Number(-360, 360, 15, "0.#");
    readonly CheckBox _lockRatio = new() { Content = "Keep proportions", IsChecked = true };
    readonly ComboBox _shapeLayer = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly TextBlock _selInfo = Hint();

    // layer settings
    readonly TextBlock _layerTitle = new() { Classes = { "h" } };
    readonly ComboBox _mode = new() { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = new[] { "Cut (outline)", "Score (light outline)", "Fill (engrave inside)" } };
    readonly ComboBox _light = new() { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = new[] { "Blue diode 455 nm", "Red fiber 1064 nm" } };
    readonly NumericUpDown _power = Number(0, 100, 1, "0.#");
    readonly NumericUpDown _speed = Number(2, 15000, 5, "0.#");
    readonly NumericUpDown _passes = Number(1, 10000, 1, "0");
    readonly NumericUpDown _lpcm = Number(1, 1000, 10, "0.#");
    readonly ComboBox _pulse = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly NumericUpDown _freq = Number(1, 4000, 5, "0");
    readonly TextBlock _powerHint = Hint(), _speedHint = Hint(), _passHint = Hint(), _lpcmHint = Hint(), _freqHint = Hint();
    readonly Control _lpcmRow, _pulseRow, _freqRow;
    readonly ComboBox _presets = new() { HorizontalAlignment = HorizontalAlignment.Stretch, PlaceholderText = "Apply a saved preset…" };

    public PropertiesPanel(Document doc, LayersPanel layers, AppSettings settings)
    {
        _doc = doc;
        _layers = layers;
        _settings = settings;
        _pulse.ItemsSource = _machine.Mopa.Select(m => $"{m.PulseWidthNs} ns").ToList();

        // ---- selection card ----
        var apply = Button("Apply rotation", tip: "Rotate the selection about its centre by this many degrees (clockwise).");
        apply.Click += (_, _) => RotateSelection();
        var center = Button("Centre in work area", tip: "Move the selection so it sits in the middle of the 220 × 220 mm field.");
        center.Click += (_, _) => CenterInField();
        _selCard = Card("Selection",
            Row(Field2("X", _x), Field2("Y", _y)),
            Row(Field2("W", _w), Field2("H", _h)),
            _lockRatio,
            Row(Field2("Angle", _rot), apply),
            Field("Layer", _shapeLayer, tip: "Move the selected shapes to another layer (Ctrl+1…9 also works)."),
            center,
            _selInfo);
        _x.ValueChanged += (_, _) => MoveTo();
        _y.ValueChanged += (_, _) => MoveTo();
        _w.ValueChanged += (_, _) => Resize(true);
        _h.ValueChanged += (_, _) => Resize(false);
        _shapeLayer.SelectionChanged += (_, _) => ChangeShapeLayer();

        // ---- layer settings card ----
        _lpcmRow = Field("Lines / cm", _lpcm, _lpcmHint, "How densely a fill is scanned. More lines = darker, smoother, slower.");
        _pulseRow = Field("Pulse width", _pulse, tip: "Fiber only. How long each laser pulse lasts. Short pulses mark crisply; long pulses put in more heat.");
        _freqRow = Field("Frequency", _freq, _freqHint, "Fiber only. Pulses per second (kHz). Higher = pulses overlap more, smoother lines, less energy per pulse.");
        var savePreset = Button("Save as preset…", tip: "Remember these settings under a name, e.g. \"3 mm basswood – cut\".");
        savePreset.Click += async (_, _) => await SavePreset();
        var delPreset = Button("Delete preset", tip: "Remove the preset currently shown in the list.");
        delPreset.Click += (_, _) => DeletePreset();
        var layerCard = new Border
        {
            Classes = { "card" },
            Child = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    _layerTitle,
                    Field("Mode", _mode, tip: "Cut and Score both follow the outline (Score usually lighter, and run first). Fill scans back and forth to engrave the inside of closed shapes."),
                    Field("Laser", _light, tip: "Blue: 40 W diode. Red: 60 W MOPA fiber. Blue diode for wood, leather, acrylic, paper. Red fiber for metals and some plastics."),
                    Field("Power", _power, _powerHint, "Percent of the laser's full power."),
                    Field("Speed", _speed, _speedHint, "How fast the beam moves while firing. Slower = deeper/darker."),
                    Field("Passes", _passes, _passHint, "How many times this layer is processed."),
                    _lpcmRow, _pulseRow, _freqRow,
                    new Separator { Margin = new(0, 4) },
                    _presets,
                    Row(savePreset, delPreset),
                },
            },
        };
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
        _presets.SelectionChanged += (_, _) => ApplyPreset();

        Content = new ScrollViewer { Content = new StackPanel { Children = { _selCard, layerCard } } };
        _doc.Changed += _ => Refresh();
        _layers.SelectionChanged += Refresh;
        RefreshPresets();
        Refresh();
    }

    static Control Field2(string label, Control editor)
    {
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

    void RefreshSelection()
    {
        var sel = _doc.Selection;
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
        _rot.Value ??= 0;

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
        _lpcmRow.IsVisible = anyFill;
        _pulseRow.IsVisible = _freqRow.IsVisible = anyRed;
        var pw = Same(l => l.Laser.PulseWidthNs);
        _pulse.SelectedIndex = pw is { } p ? _machine.Mopa.ToList().FindIndex(x => x.PulseWidthNs == p) : -1;
        _pulse.PlaceholderText = pw is null ? "mixed" : "";
        Num(_freq, Same(l => (double)l.Laser.FrequencyKhz));
        var lim = pw is { } pv ? _machine.MopaLimitFor(pv) : null;
        _freq.Maximum = lim?.MaxKhz ?? 4000;

        // Plain-language hints with the exact values sent.
        _powerHint.Text = Same(l => l.Laser.Power) is { } pow ? $"Sent as S{(long)Math.Round(pow * 10)} (per-mille of full power)." : "Each layer keeps its own value.";
        _speedHint.Text = Same(l => l.Laser.Speed) is { } sp ? $"Sent as F{(long)Math.Round(sp * 60)} (mm per minute)." : "Each layer keeps its own value.";
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

    void RotateSelection()
    {
        if (_doc.Selection.Count == 0 || _rot.Value is not { } deg || deg == 0) return;
        var c = _doc.State.Bounds(_doc.Selection).Center;
        _doc.Commit("Rotate", Ops.Transform(_doc.State, _doc.Selection, Affine.RotateAbout(c, (double)deg)));
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

    // ---- presets ----------------------------------------------------------------

    void RefreshPresets()
    {
        _updating = true;
        _presets.ItemsSource = _settings.Presets.Select(p => $"{p.Name}  ·  {p.Mode} {p.Laser.Power:0.#}% {p.Laser.Speed:0.#} mm/s ×{p.Laser.Passes}").ToList();
        _presets.SelectedIndex = -1;
        _updating = false;
    }

    void ApplyPreset()
    {
        if (_updating || _presets.SelectedIndex < 0) return;
        var p = _settings.Presets[_presets.SelectedIndex];
        EditLayers($"Apply preset \"{p.Name}\"", l => l with { Mode = p.Mode, Laser = p.Laser, LinesPerCm = p.LinesPerCm }, true);
    }

    async Task SavePreset()
    {
        var l = EditLayersSet().FirstOrDefault(x => x.Id == _doc.ActiveLayer) ?? EditLayersSet().FirstOrDefault();
        if (l is null || TopLevel.GetTopLevel(this) is not Window owner) return;
        var name = await Dialogs.Prompt(owner, "Save preset", "Name for these settings:", l.Name);
        if (string.IsNullOrWhiteSpace(name)) return;
        _settings.Presets.RemoveAll(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        _settings.Presets.Add(new Preset(name.Trim(), l.Mode, l.Laser, l.LinesPerCm));
        _settings.Presets.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
        _settings.Save();
        RefreshPresets();
    }

    void DeletePreset()
    {
        int i = _presets.SelectedIndex;
        if (i < 0 || i >= _settings.Presets.Count) return;
        _settings.Presets.RemoveAt(i);
        _settings.Save();
        RefreshPresets();
    }
}
