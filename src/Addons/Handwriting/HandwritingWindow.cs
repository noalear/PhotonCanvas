using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using PhotonCanvas.App;
using PhotonCanvas.App.Addons;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using static PhotonCanvas.App.UiKit;
using PathGeometry = PhotonCanvas.Doc.PathGeometry;

namespace PhotonCanvas.Addon.Handwriting;

/// <summary>
/// Write words in a handwriting font: previewed as you type, added as single lines on a Score
/// layer (or the selected handwriting updated). Fonts are made in <see cref="HandwritingFontWindow"/>.
/// </summary>
public sealed class HandwritingWindow : Window
{
    readonly IAddonHost _host;
    readonly FontStore _store;
    readonly PathShape? _editing;
    readonly ComboBox _font = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly TextBox _words = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 96, Watermark = "Type the words to write" };
    readonly NumericUpDown _size = Number(1, 300, 1, "0.#"), _letter = Number(-50, 100, 2, "0"), _word = Number(10, 300, 5, "0"), _line = Number(80, 500, 10, "0"), _slant = Number(-30, 30, 1, "0");
    readonly ComboBox _align = new() { ItemsSource = new[] { "Left", "Centre", "Right" }, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly Slider _variation = new() { Minimum = 0, Maximum = 100, Value = 30 };
    readonly TextBlock _info = new() { TextWrapping = TextWrapping.Wrap };
    readonly StrokePreview _preview = new();
    readonly Button _add = new() { Classes = { "primary" }, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
    readonly Button _delete = Button("Delete font", tip: "Delete this handwriting font (designs written with it keep their lines)");
    readonly Button _export = Button("Export…", tip: "Save the font for another computer, or as an SVG font for other programs");
    readonly DispatcherTimer _later = new() { Interval = TimeSpan.FromMilliseconds(120) };
    List<HandFont> _fonts = [];
    HandFont? Current => _font.SelectedIndex >= 0 && _font.SelectedIndex < _fonts.Count ? _fonts[_font.SelectedIndex] : null;
    int _seed = 1;
    HandWriting? _result;
    bool _loading;

    public const string TextKey = "handwriting.text", SettingsKey = "handwriting.style";

    string SettingsFile => Path.Combine(_host.DataFolder, "last-style.json");

    public HandwritingWindow(IAddonHost host, PathShape? editing)
    {
        _host = host;
        _store = new FontStore(Path.Combine(host.DataFolder, "fonts"));
        _editing = editing;
        Title = editing is null ? "Handwriting" : "Handwriting: change the words";
        Width = 1000;
        Height = 720;
        MinWidth = 760;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");

        var style = (editing is not null ? HandStyle.FromJson(editing.AddonData.GetValueOrDefault(SettingsKey)) : null)
            ?? (File.Exists(SettingsFile) ? HandStyle.FromJson(SafeRead(SettingsFile)) : null) ?? new HandStyle();
        _loading = true;
        _size.Value = (decimal)style.SizeMm;
        _letter.Value = (decimal)Math.Round(style.LetterSpacing * 100);
        _word.Value = (decimal)Math.Round(style.WordSpacing * 100);
        _line.Value = (decimal)Math.Round(style.LineSpacing * 100);
        _slant.Value = (decimal)style.SlantDeg;
        _align.SelectedIndex = (int)style.Align;
        _variation.Value = style.Variation * 100;
        _seed = style.Seed;
        _words.Text = editing?.AddonData.GetValueOrDefault(TextKey) ?? "";
        LoadFonts(style.Font);
        _loading = false;

        foreach (var n in new[] { _size, _letter, _word, _line, _slant }) n.ValueChanged += (_, _) => Later();
        _align.SelectionChanged += (_, _) => Later();
        _variation.ValueChanged += (_, _) => Later();
        _words.TextChanged += (_, _) => Later();
        _font.SelectionChanged += (_, _) => { if (!_loading) { Buttons(); Later(); } };
        _later.Tick += (_, _) => { _later.Stop(); Make(); };

        var make = Button("Make a font…", tip: "Print a sheet, write your letters on it, photograph it: a font of your own handwriting");
        make.Click += async (_, _) => await MakeFont();
        var addFile = Button("Add a font file…", tip: "A handwriting font saved on another computer (a .json file)");
        addFile.Click += async (_, _) => await _host.Guard(AddFontFile);
        _export.Flyout = new MenuFlyout
        {
            Items =
            {
                new MenuItem { Header = "Font file, to use on another computer…" }.With(m => m.Click += async (_, _) => await _host.Guard(ExportFile)),
                new MenuItem { Header = "SVG font, for other programs…" }.With(m => m.Click += async (_, _) => await _host.Guard(ExportSvg)),
            },
        };
        _delete.Click += async (_, _) => await _host.Guard(DeleteFont);
        var shuffle = Button("Another variation", tip: "The same words with a different wobble and different versions of letters written more than once");
        shuffle.Click += (_, _) => { _seed = _seed % 9973 + 1; Make(); };
        _add.Content = editing is null ? "Add to the design" : "Change the writing";
        _add.Click += (_, _) => Add();
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 90, HorizontalAlignment = HorizontalAlignment.Right };
        cancel.Click += (_, _) => Close();

        var side = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                Explained("Font", "Your handwriting, read from a sheet you filled in. Make one per person, or one per pen.",
                    _font,
                    new WrapPanel { Orientation = Orientation.Horizontal, Children = { make.With(b => b.Margin = new(0, 0, 6, 6)), addFile.With(b => b.Margin = new(0, 0, 6, 6)), _export.With(b => b.Margin = new(0, 0, 6, 6)), _delete.With(b => b.Margin = new(0, 0, 0, 6)) } }),
                Labelled("Words", _words),
                Wide("Capital height (mm)", _size, "How tall capital letters are. Small letters come out at the size you wrote them, next to your capitals."),
                Wide("Letters apart (%)", _letter, "Space between letters, as a share of the capital height."),
                Wide("Words apart (%)", _word, "Space for a space, as a share of the capital height."),
                Wide("Line spacing (%)", _line, "Baseline to baseline, as a share of the capital height."),
                Wide("Slant (°)", _slant, "Lean the letters: + to the right, − to the left. 0 = as you wrote them."),
                Wide("Align", _align, null),
                Wide("Natural variation", _variation, "0: every letter exactly as on your sheet. More: each letter a little bigger or smaller, turned or lifted, so words look less stamped."),
                shuffle,
                _info,
            },
        };
        var bottom = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 8, Margin = new(0, 10, 0, 0), Children = { _add, cancel.With(c => Grid.SetColumn(c, 1)) } };
        var left = new DockPanel { Width = 360, Margin = new(0, 0, 16, 0) };
        DockPanel.SetDock(bottom, Dock.Bottom);
        left.Children.Add(bottom);
        left.Children.Add(new ScrollViewer { Content = side.With(p => p.Margin = new(0, 0, 14, 0)), HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });
        var body = new DockPanel { Margin = new(16) };
        DockPanel.SetDock(left, Dock.Left);
        body.Children.Add(left);
        body.Children.Add(new Border { Background = Brush("Bg0"), CornerRadius = new(8), ClipToBounds = true, Child = _preview });
        Content = body;
        Buttons();
        Opened += (_, _) => { Make(); _words.Focus(); };
    }

    /// <summary>A setting with a label wide enough for its name.</summary>
    static Control Wide(string label, Control editor, string? tip)
    {
        var grid = new Grid { ColumnDefinitions = new("140,*") };
        var lbl = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Classes = { "muted" } };
        grid.Children.Add(lbl);
        Grid.SetColumn(editor, 1);
        grid.Children.Add(editor);
        if (tip is not null) { ToolTip.SetTip(lbl, tip); ToolTip.SetTip(editor, tip); }
        return grid;
    }

    static string? SafeRead(string path)
    {
        try { return File.ReadAllText(path); }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    void LoadFonts(string? pick)
    {
        _fonts = _store.All();
        bool was = _loading;
        _loading = true;
        _font.ItemsSource = _fonts.Count == 0 ? new[] { "(no handwriting fonts yet)" } : _fonts.Select(f => $"{f.Name}  ·  {f.Glyphs.Count} characters" + (f.Count > f.Glyphs.Count ? $", {f.Count} versions" : "")).ToArray();
        int i = _fonts.FindIndex(f => string.Equals(f.Name, pick, StringComparison.CurrentCultureIgnoreCase));
        _font.SelectedIndex = _fonts.Count == 0 ? 0 : Math.Max(0, i);
        _font.IsEnabled = _fonts.Count > 0;
        _loading = was;
        Buttons();
    }

    void Buttons()
    {
        _delete.IsEnabled = _export.IsEnabled = Current is not null;
    }

    HandStyle Style() => new()
    {
        Font = Current?.Name ?? "",
        SizeMm = (double)(_size.Value ?? 10),
        LetterSpacing = (double)(_letter.Value ?? 12) / 100,
        WordSpacing = (double)(_word.Value ?? 50) / 100,
        LineSpacing = (double)(_line.Value ?? 190) / 100,
        SlantDeg = (double)(_slant.Value ?? 0),
        Align = (TextAlign)Math.Max(0, _align.SelectedIndex),
        Variation = _variation.Value / 100,
        Seed = _seed,
    };

    void Later() { if (_loading) return; _later.Stop(); _later.Start(); }

    void Make()
    {
        var font = Current;
        string text = _words.Text ?? "";
        if (font is null)
        {
            _result = null;
            _preview.Show(PathGeometry.Empty, "No handwriting font yet.\nClick Make a font… to print a sheet, fill it in and read it back.");
            _info.Text = "";
            _add.IsEnabled = false;
            return;
        }
        var style = Style();
        _result = HandLayout.Write(font, text, style);
        _preview.Show(_result.Geometry, string.IsNullOrWhiteSpace(text) ? "Type some words on the left." : null);
        var parts = new List<string>();
        if (!_result.Geometry.IsEmpty)
        {
            var b = _result.Geometry.Bounds();
            parts.Add($"{_result.Letters} letter{(_result.Letters == 1 ? "" : "s")}, {b.Width:0.#} × {b.Height:0.#} mm.");
        }
        if (_result.Missing.Count > 0) parts.Add($"Not in this font (left as a gap): {string.Join(" ", _result.Missing)}");
        if (_result.StandIns.Count > 0) parts.Add($"Written with a stand-in: {string.Join(" ", _result.StandIns)}");
        _info.Text = string.Join("\n", parts);
        _add.IsEnabled = !_result.Geometry.IsEmpty;
    }

    async Task MakeFont()
    {
        var name = await new HandwritingFontWindow(_host, _store, Current?.Name).ShowDialog<string?>(this);
        if (name is null) return;
        LoadFonts(name);
        Make();
    }

    async Task AddFontFile()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Add a handwriting font",
            AllowMultiple = true,
            FileTypeFilter = [new FilePickerFileType("Handwriting font") { Patterns = ["*.json"] }],
        });
        string? last = null;
        foreach (var f in files)
        {
            if (f.TryGetLocalPath() is not { } path) continue;
            HandFont font;
            try { font = HandFont.FromJson(await File.ReadAllTextAsync(path)); }
            catch (FormatException e) { await Dialogs.Message(this, "Add a font file", $"{Path.GetFileName(path)}: {e.Message}"); continue; }
            if (_store.Find(font.Name) is not null
                && !await Dialogs.Confirm(this, "Add a font file", $"There's already a handwriting font called \"{font.Name}\". Replace it?", "Replace", "Keep mine")) continue;
            _store.Save(font);
            last = font.Name;
        }
        if (last is null) return;
        LoadFonts(last);
        Make();
    }

    async Task ExportFile()
    {
        if (Current is not { } font) return;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save the handwriting font",
            SuggestedFileName = FontStore.SafeFileName(font.Name) + ".json",
            DefaultExtension = "json",
            FileTypeChoices = [new FilePickerFileType("Handwriting font") { Patterns = ["*.json"] }],
        });
        if (file?.TryGetLocalPath() is not { } path) return;
        await File.WriteAllTextAsync(path, font.ToJson());
        _host.Status($"Saved the handwriting font \"{font.Name}\" to {Path.GetFileName(path)}.");
    }

    async Task ExportSvg()
    {
        if (Current is not { } font) return;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save as an SVG font",
            SuggestedFileName = FontStore.SafeFileName(font.Name) + ".svg",
            DefaultExtension = "svg",
            FileTypeChoices = [new FilePickerFileType("SVG font") { Patterns = ["*.svg"] }],
        });
        if (file?.TryGetLocalPath() is not { } path) return;
        var st = Style();
        await File.WriteAllTextAsync(path, font.ToSvgFont(st.LetterSpacing, st.WordSpacing));
        _host.Status($"Saved \"{font.Name}\" as an SVG font ({Path.GetFileName(path)}).");
    }

    async Task DeleteFont()
    {
        if (Current is not { } font) return;
        if (!await Dialogs.Confirm(this, "Delete font", $"Delete the handwriting font \"{font.Name}\"? Writing already in designs stays as it is, but can't be changed with this font any more.", "Delete", "Keep it")) return;
        _store.Delete(font.Name);
        LoadFonts(null);
        Make();
    }

    void Add()
    {
        if (_result is null || _result.Geometry.IsEmpty || Current is null) return;
        var style = Style();
        try { Directory.CreateDirectory(_host.DataFolder); File.WriteAllText(SettingsFile, style.ToJson()); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        var doc = _host.Document;
        var state = doc.State;
        string text = _words.Text ?? "";
        string name = "Handwriting · " + FirstLine(text);
        var data = (_editing?.AddonData ?? System.Collections.Immutable.ImmutableSortedDictionary<string, string>.Empty)
            .SetItem(TextKey, text).SetItem(SettingsKey, style.ToJson());
        if (_editing is not null && state.Shapes.FirstOrDefault(s => s.Id == _editing.Id) is PathShape old)
        {
            var shape = new PathShape
            {
                Id = old.Id, Name = old.Name.StartsWith("Handwriting", StringComparison.Ordinal) ? name : old.Name,
                LayerId = old.LayerId, GroupId = old.GroupId, Transform = old.Transform, Locked = old.Locked,
                Geometry = _result.Geometry, AddonData = data,
            };
            doc.Commit("Change handwriting", Ops.Replace(state, shape), [shape.Id]);
        }
        else
        {
            var layer = state.FindLayer(doc.ActiveLayer) is { Mode: LayerMode.Score, Locked: false, Visible: true } active ? active
                : state.Layers.FirstOrDefault(l => l.Mode == LayerMode.Score && l.Visible && !l.Locked);
            if (layer is null)
            {
                layer = Layer.Default(state.Layers.Count, LayerMode.Score) with { Name = "Handwriting" };
                state = Ops.AddLayer(state, layer);
            }
            var b = _result.Geometry.Bounds();
            var m = _host.Machine;
            var at = new Vec2(Math.Max(0, (m.FieldW - b.Width) / 2), Math.Max(0, (m.FieldH - b.Height) / 2));
            var shape = new PathShape { Name = name, LayerId = layer.Id, Transform = Affine.Translate(at), Geometry = _result.Geometry, AddonData = data };
            doc.Commit("Handwriting", Ops.AddShapes(state, [shape]), [shape.Id]);
            _host.ActivateLayer(layer.Id);
        }
        _host.Status($"Wrote {_result.Letters} letter{(_result.Letters == 1 ? "" : "s")} in \"{Current.Name}\" as single lines.");
        Close();
    }

    static string FirstLine(string t)
    {
        var line = t.Replace("\r", "").Split('\n')[0].Trim();
        return line.Length > 24 ? line[..24] + "…" : line.Length == 0 ? "words" : line;
    }
}

/// <summary>Single lines drawn like ink on card, fitted to the control, with a message when there's nothing.</summary>
public sealed class StrokePreview : Control
{
    Geometry? _geo;
    Rect2 _area;
    string? _message;

    public void Show(PathGeometry g, string? message)
    {
        _message = message;
        _geo = null;
        _area = Rect2.Empty;
        if (!g.IsEmpty)
        {
            var sg = new StreamGeometry();
            using (var ctx = sg.Open())
                foreach (var f in g.Flatten(0.01))
                {
                    if (f.Points.Count < 2) continue;
                    ctx.BeginFigure(new Point(f.Points[0].X, f.Points[0].Y), false);
                    for (int i = 1; i < f.Points.Count; i++) ctx.LineTo(new Point(f.Points[i].X, f.Points[i].Y));
                    ctx.EndFigure(f.Closed);
                }
            _geo = sg;
            _area = g.Bounds();
        }
        InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        ctx.FillRectangle(new SolidColorBrush(Color.Parse("#F3EAD8")), new Rect(Bounds.Size));
        if (_geo is not null && !_area.IsEmpty)
        {
            double w = Math.Max(_area.Width, 1), h = Math.Max(_area.Height, 1);
            double k = Math.Min((Bounds.Width - 48) / w, (Bounds.Height - 48) / h);
            k = Math.Max(0.01, k);
            var m = Matrix.CreateTranslation(-_area.MinX - _area.Width / 2, -_area.MinY - _area.Height / 2) * Matrix.CreateScale(k, k) * Matrix.CreateTranslation(Bounds.Width / 2, Bounds.Height / 2);
            using (ctx.PushTransform(m))
                ctx.DrawGeometry(null, new Pen(new SolidColorBrush(Color.Parse("#3B2616")), Math.Clamp(1.6 / k, 0.02, 2), lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), _geo);
        }
        if (_message is not null)
        {
            var t = new FormattedText(_message, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 14, new SolidColorBrush(Color.Parse("#7A6A55")))
            {
                TextAlignment = TextAlignment.Center,
                MaxTextWidth = Math.Max(100, Bounds.Width - 60),
            };
            ctx.DrawText(t, new Point((Bounds.Width - t.MaxTextWidth) / 2, _geo is null ? (Bounds.Height - t.Height) / 2 : Bounds.Height - t.Height - 24));
        }
    }
}
