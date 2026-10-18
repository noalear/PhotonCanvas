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
/// The inspector: what is selected (size, position, angle, layer), plus text and picture
/// settings when one is. Layer settings live with the layers.
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
    readonly Border _nothing;
    readonly TextBlock _nothingInfo = Hint();

    // picture
    readonly Border _picCard;
    readonly Slider _bright = new() { Minimum = -1, Maximum = 1 };
    readonly Slider _contrast = new() { Minimum = -1, Maximum = 1 };
    readonly Slider _gamma = new() { Minimum = 0.2, Maximum = 3, Value = 1 };
    readonly CheckBox _invertPic = new() { Content = "Invert (for dark materials)" };
    readonly Slider _sharpen = new() { Minimum = 0, Maximum = 3 };

    /// <summary>The user asked to trace a picture.</summary>
    public event Action<ImageShape>? TraceRequested;

    /// <summary>The user asked for a depthmap of a picture (DepthCharge).</summary>
    public event Action<ImageShape>? DepthmapRequested;

    /// <summary>The user asked to remove a picture's background.</summary>
    public event Action<ImageShape>? RemoveBackgroundRequested;

    /// <summary>The user asked to snap the selection onto a piece seen by the camera.</summary>
    public event Action? FitToCameraRequested;

    // text
    readonly Border _textCard;
    readonly TextBox _textBox = new() { AcceptsReturn = true, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MinHeight = 60 };
    readonly ComboBox _font = new() { HorizontalAlignment = HorizontalAlignment.Stretch, MaxDropDownHeight = 420 };
    readonly NumericUpDown _textSize = Number(0.5m, 500, 1, "0.##");
    readonly CheckBox _bold = new() { Content = "Bold" }, _italic = new() { Content = "Italic" }, _weld = new() { Content = "Weld letters together" }, _single = new() { Content = "Single line (one stroke)" };
    readonly NumericUpDown _letter = Number(-50, 200, 0.1m, "0.##"), _lineSp = Number(0.5m, 5, 0.1m, "0.##");
    readonly ComboBox _align = new() { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = new[] { "Left", "Centre", "Right" } };
    readonly NumericUpDown _bend = Number(-360, 360, 5, "0.#");
    readonly TextBlock _fieldsHint = Hint();
    IReadOnlyList<string> _fonts = [];

    // editable shape
    readonly Border _paramCard;
    readonly NumericUpDown _pw = Number(0.01m, 2000, 1, "0.##"), _ph = Number(0.01m, 2000, 1, "0.##"), _pCorner = Number(0, 1000, 0.5m, "0.##"), _pSides = Number(3, 64, 1, "0"), _pInner = Number(5, 95, 5, "0");
    readonly Control _cornerRow, _sidesRow, _innerRow;
    readonly NumericUpDown _fitW = Number(0, 2000, 1, "0.##"), _fitH = Number(0, 2000, 1, "0.##");

    // code
    readonly Border _codeCard;
    readonly TextBlock _codeInfo = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };

    /// <summary>The user asked for the font manager.</summary>
    public event Action? FontsRequested;

    /// <summary>Re-read the fonts: starred ones first, hidden ones left out.</summary>
    public void RefreshFonts()
    {
        var all = TextLayout.FontFamilies();
        var fav = _settings.FontFavourites.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var hidden = _settings.FontHidden.ToHashSet(StringComparer.OrdinalIgnoreCase);
        _fonts = [.. all.Where(f => fav.Contains(f)), .. all.Where(f => !fav.Contains(f) && !hidden.Contains(f))];
        bool was = _updating;
        _updating = true;
        try
        {
            _font.ItemsSource = _fonts;
            if (SelectedText() is { } t) _font.SelectedItem = _fonts.FirstOrDefault(f => f.Equals(t.Style.FontFamily, StringComparison.OrdinalIgnoreCase));
        }
        finally { _updating = was; }
    }

    /// <summary>The user asked for the serial number and spreadsheet settings.</summary>
    public event Action? VariablesRequested;

    /// <summary>The user asked to edit the selected code.</summary>
    public event Action<Guid>? CodeEditRequested;

    /// <summary>Put the cursor in the text box (after creating or double-clicking text).</summary>
    public void FocusText(bool selectAll = false)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _textBox.Focus();
            if (selectAll) _textBox.SelectAll();
        });
    }
    public PropertiesPanel(Document doc, LayersPanel layers, AppSettings settings)
    {
        _doc = doc;
        _layers = layers;
        _settings = settings;

        _nothing = Card("Nothing selected", _nothingInfo,
            Hint("Click a shape to see its size, position and angle here. Text and pictures get their own settings too."),
            Hint("Layer settings (power, speed, material) live with each layer on the left; the job bar at the bottom shows what will run and how long it takes."));
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

        var trace = Button("Trace into vectors…", tip: "Turn the picture into cut/engrave outlines (Ctrl+T).");
        trace.Click += (_, _) => { if (SelectedPicture() is { } im) TraceRequested?.Invoke(im); };
        var depth = Button("Depthmap…", tip: "Turn the picture into a relief depthmap with DepthCharge.");
        depth.Click += (_, _) => { if (SelectedPicture() is { } im) DepthmapRequested?.Invoke(im); };
        var removeBg = Button("Remove background…", tip: "Make the backdrop see-through so it isn't engraved.");
        removeBg.Click += (_, _) => { if (SelectedPicture() is { } im) RemoveBackgroundRequested?.Invoke(im); };
        var resetTone = Button("Reset");
        resetTone.Click += (_, _) => EditPicture("Reset picture", _ => ToneAdjust.None);
        _picCard = Card("Picture",
            Slider("Brightness", _bright), Slider("Contrast", _contrast), Slider("Gamma", _gamma), Slider("Sharpen", _sharpen), _invertPic,
            Hint("These change how the picture engraves (Image layers), not the file."),
            Row(trace, depth),
            Row(removeBg, resetTone));
        _bright.ValueChanged += (_, _) => EditPicture("Picture brightness", t => t with { Brightness = _bright.Value }, "bright");
        _contrast.ValueChanged += (_, _) => EditPicture("Picture contrast", t => t with { Contrast = _contrast.Value }, "contrast");
        _gamma.ValueChanged += (_, _) => EditPicture("Picture gamma", t => t with { Gamma = _gamma.Value }, "gamma");
        _invertPic.IsCheckedChanged += (_, _) => EditPicture("Invert picture", t => t with { Invert = _invertPic.IsChecked == true });
        _sharpen.ValueChanged += (_, _) => EditPicture("Sharpen picture", t => t with { Sharpen = _sharpen.Value }, "sharpen");
        ToolTip.SetTip(_sharpen, "Bring out edges and fine detail before engraving (unsharp mask). 0 = off; about 1 is a good start.");
        // ---- text card ----
        RefreshFonts();
        var toPaths = Button("Convert to paths", tip: "Turn the text into ordinary shapes (no longer editable as text).");
        toPaths.Click += (_, _) =>
        {
            if (SelectedText() is { } t)
                _doc.Commit("Convert to paths", Ops.Replace(_doc.State, new PathShape { Id = t.Id, Name = t.Name, LayerId = t.LayerId, GroupId = t.GroupId, Transform = t.Transform, Geometry = t.Geometry }));
        };
        _textCard = Card("Text",
            _textBox,
            _fieldsHint,
            FieldMenu.Create(_textBox, () => _doc.State.Vars, () => VariablesRequested?.Invoke()),
            Field("Font", _font),
            Button("Fonts…", "IcFonts", "Star the fonts you use (they come first) and hide the rest.").With(b => b.Click += (_, _) => FontsRequested?.Invoke()),
            Row(Field2("Size mm", _textSize), new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _bold, _italic } }),
            Field("Letter spacing", _letter, tip: "Extra space between letters, mm (negative tightens)."),
            Field("Line spacing", _lineSp, tip: "Line height as a multiple of the size."),
            Field("Align", _align),
            _weld,
            _single,
            Row(Field2("Fit W", _fitW), Field2("Fit H", _fitH)),
            Field("Curve (°)", _bend, tip: "Bend each line along an arc: how many degrees of a circle it covers. Positive arches up, negative curves down, 0 straight."),
            toPaths);
        _textBox.TextChanged += (_, _) => EditText(t => (_textBox.Text ?? "", t.Style), "text");
        _font.SelectionChanged += (_, _) => { if (_font.SelectedItem is string f) EditText(t => (t.Text, t.Style with { FontFamily = f }), null); };
        _textSize.ValueChanged += (_, _) => { if (_textSize.Value is { } v) EditText(t => (t.Text, t.Style with { SizeMm = (double)v }), "tsize"); };
        _bold.IsCheckedChanged += (_, _) => EditText(t => (t.Text, t.Style with { Bold = _bold.IsChecked == true }), null);
        _italic.IsCheckedChanged += (_, _) => EditText(t => (t.Text, t.Style with { Italic = _italic.IsChecked == true }), null);
        _weld.IsCheckedChanged += (_, _) => EditText(t => (t.Text, t.Style with { Weld = _weld.IsChecked == true }), null);
        ToolTip.SetTip(_fitW, "Make the text exactly this wide (mm), whatever it says: variable text that always fits its space. 0 = off.");
        ToolTip.SetTip(_fitH, "Make the text this tall (mm). With both, it fits inside the box. 0 = off.");
        _fitW.ValueChanged += (_, _) => { if (_fitW.Value is { } v) EditText(t => (t.Text, t.Style with { FitWidthMm = (double)v }), "fitw"); };
        _fitH.ValueChanged += (_, _) => { if (_fitH.Value is { } v) EditText(t => (t.Text, t.Style with { FitHeightMm = (double)v }), "fith"); };
        _single.IsCheckedChanged += (_, _) => EditText(t => (t.Text, t.Style with { SingleLine = _single.IsChecked == true }), null);
        ToolTip.SetTip(_single, "One line down the middle of each letter's strokes instead of its outline: much faster engraving with a clean, pen-drawn look. Put it on a Score layer. Works best with plain, even-weight fonts.");
        ToolTip.SetTip(_weld, "Merge letters that touch or overlap into one outline (script fonts, tight letter spacing), so the laser doesn't cut where they meet.");
        _letter.ValueChanged += (_, _) => { if (_letter.Value is { } v) EditText(t => (t.Text, t.Style with { LetterSpacingMm = (double)v }), "letter"); };
        _lineSp.ValueChanged += (_, _) => { if (_lineSp.Value is { } v) EditText(t => (t.Text, t.Style with { LineSpacing = (double)v }), "line"); };
        _align.SelectionChanged += (_, _) => { if (_align.SelectedIndex >= 0) EditText(t => (t.Text, t.Style with { Align = (TextAlign)_align.SelectedIndex }), null); };
        _bend.ValueChanged += (_, _) => { if (_bend.Value is { } v) EditText(t => (t.Text, t.Style with { BendDeg = (double)v }), "bend"); };

        var editCode = Button("Edit code…", "IcCode", "Change what the code says, its kind, size or margin (or double-click it).");
        editCode.Click += (_, _) => { if (SelectedCode() is { } c) CodeEditRequested?.Invoke(c.Id); };
        var vars = Button("Variables…", "IcVariables", "Serial number and spreadsheet that fill in fields.");
        vars.Click += (_, _) => VariablesRequested?.Invoke();
        _codeCard = Card("Code", _codeInfo, Row(editCode, vars));

        _cornerRow = Field("Corner radius", _pCorner, tip: "Rounded corners, mm (0 = square).");
        _sidesRow = Field("Sides / points", _pSides);
        _innerRow = Field("Point depth %", _pInner, tip: "How far in the star's inner corners are, as a share of its size: smaller = spikier.");
        _paramCard = Card("Shape", Row(Field2("W", _pw), Field2("H", _ph)), _cornerRow, _sidesRow, _innerRow,
            Hint("Stays editable until you edit its points (convert it to a path first: Ctrl+Shift+C)."));
        foreach (var n in new[] { _pw, _ph, _pCorner, _pSides, _pInner }) n.ValueChanged += (_, _) => EditParam();

        Content = new ScrollViewer { Content = new StackPanel { Children = { _nothing, _selCard, _textCard, _paramCard, _codeCard, _picCard } } };
        _doc.Changed += _ => Refresh();
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

    // ---- refresh -------------------------------------------------------------

    void Refresh()
    {
        _updating = true;
        try
        {
            RefreshSelection();
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

    void EditParam()
    {
        if (_updating || _doc.Selection.Count != 1 || _doc.State.FindShape(_doc.Selection.First()) is not ParamShape p) return;
        var next = p.Edit((double)(_pw.Value ?? 10), (double)(_ph.Value ?? 10), (double)(_pCorner.Value ?? 0), (int)(_pSides.Value ?? 6), (double)(_pInner.Value ?? 50) / 100);
        _doc.Commit($"Edit {ParamShape.KindName(p.Kind).ToLowerInvariant()}", Ops.Replace(_doc.State, next), coalesceKey: $"param:{p.Id}");
    }

    CodeShape? SelectedCode() =>
        _doc.Selection.Count == 1 && _doc.State.FindShape(_doc.Selection.First()) is CodeShape c ? c : null;

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
        var param = _doc.Selection.Count == 1 && _doc.State.FindShape(_doc.Selection.First()) is ParamShape ps0 ? ps0 : null;
        _paramCard.IsVisible = param is not null;
        if (param is not null)
        {
            var (sw, sh) = param.SeenSize;
            _pw.Value = (decimal)Math.Round(sw, 3);
            _ph.Value = (decimal)Math.Round(sh, 3);
            _pCorner.Value = (decimal)param.CornerMm;
            _pSides.Value = param.Sides;
            _pInner.Value = (decimal)Math.Round(param.Inner * 100);
            _cornerRow.IsVisible = param.Kind == ParamKind.Rectangle;
            _sidesRow.IsVisible = param.Kind is ParamKind.Polygon or ParamKind.Star;
            _innerRow.IsVisible = param.Kind == ParamKind.Star;
        }
        var code = SelectedCode();
        _codeCard.IsVisible = code is not null;
        if (code is not null)
            _codeInfo.Text = $"{CodeShape.KindName(code.Spec.Kind)}: “{code.Spec.Content}”" +
                             (code.Shown is { } cs ? $"\nNext run marks “{cs}”." : "") +
                             (code.Error is { } err ? $"\n⚠ {err}" : "");
        if (txt is not null)
        {
            if (_textBox.Text != txt.Text) _textBox.Text = txt.Text;
            _fieldsHint.Text = txt.Shown is { } shown ? $"Next run marks: {shown.Replace('\n', ' ')}" : "";
            _fieldsHint.IsVisible = txt.Shown is not null;
            _font.SelectedItem = _fonts.FirstOrDefault(f => f.Equals(txt.Style.FontFamily, StringComparison.OrdinalIgnoreCase));
            _font.PlaceholderText = txt.Style.FontFamily;
            _textSize.Value = (decimal)txt.Style.SizeMm;
            _bold.IsChecked = txt.Style.Bold;
            _italic.IsChecked = txt.Style.Italic;
            _weld.IsChecked = txt.Style.Weld;
            _single.IsChecked = txt.Style.SingleLine;
            _fitW.Value = (decimal)txt.Style.FitWidthMm;
            _fitH.Value = (decimal)txt.Style.FitHeightMm;
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
            _sharpen.Value = pic.Tone.Sharpen;
        }
        _selCard.IsVisible = sel.Count > 0;
        _nothing.IsVisible = sel.Count == 0;
        if (sel.Count == 0)
        {
            int n = _doc.State.Shapes.Count;
            var all = _doc.State.Bounds(_doc.State.Shapes.Select(x => x.Id).ToHashSet());
            _nothingInfo.Text = n == 0 ? "The design is empty." : $"The design has {n} shape{(n == 1 ? "" : "s")} on {_doc.State.Layers.Count} layer{(_doc.State.Layers.Count == 1 ? "" : "s")}, {all.Width:0.#} × {all.Height:0.#} mm.";
            return;
        }
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
}
