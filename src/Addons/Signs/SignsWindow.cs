using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Bitmap = Avalonia.Media.Imaging.Bitmap;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using PhotonCanvas.Addon.Borders;
using PhotonCanvas.App;
using PhotonCanvas.Core;
using PhotonCanvas.Core.Imaging;
using PhotonCanvas.Doc;
using static PhotonCanvas.App.UiKit;

namespace PhotonCanvas.Addon.Signs;

/// <summary>
/// The sign maker: pick a ready-made sign, type your words, choose how they're carved, the sign's
/// shape and border, holes and motifs, and see it carved; then put its depth map in the design on a
/// depth-slices layer (with its cut lines), or save it as a picture.
/// </summary>
public sealed class SignsWindow : Window
{
    const int LineCount = 4;

    readonly Document _doc;
    readonly MachineProfile _machine;
    readonly string? _dataFolder;
    readonly Action<string>? _status;
    readonly (SignSpec Spec, HashSet<Guid> Ids, Vec2 TopLeft, Guid LayerId)? _editing;

    // Signs
    readonly ListBox _gallery = new() { SelectionMode = SelectionMode.Single };
    readonly TextBlock _about = Hint();
    readonly Dictionary<string, Bitmap> _thumbs = [];
    CancellationTokenSource? _thumbCancel;

    // Words
    readonly TextBox[] _text = new TextBox[LineCount];
    readonly NumericUpDown[] _size = new NumericUpDown[LineCount];
    readonly NumericUpDown[] _bend = new NumericUpDown[LineCount];
    readonly ComboBox _font = new() { HorizontalAlignment = HorizontalAlignment.Stretch, MaxDropDownHeight = 420 };
    readonly CheckBox _bold = new() { Content = "Bold" }, _italic = new() { Content = "Italic" };
    readonly NumericUpDown _spacing = Number(-30, 200, 2, "0"), _gap = Number(0, 300, 5, "0");
    readonly CheckBox _fit = new() { Content = "As big as fits inside the border", IsChecked = true };
    readonly NumericUpDown _letter = Number(1, 500, 1, "0.#"), _margin = Number(0, 100, 0.5m, "0.#"), _offset = Number(-500, 500, 1, "0.#");

    // Lettering
    readonly ComboBox _style = Combo("V-carved", "Sunk (flat bottom)", "Raised", "Domed (raised and rounded)", "Chiselled (raised with a ridge)");
    readonly TextBlock _styleAbout = Hint();
    readonly NumericUpDown _depth = Number(0.1m, 20, 0.1m, "0.##"), _angle = Number(20, 150, 5, "0");
    readonly ComboBox _edge = Combo("Square", "Chamfer", "Rounded");
    readonly NumericUpDown _edgeMm = Number(0.05m, 20, 0.1m, "0.##");
    readonly ComboBox _texture = Combo("Smooth", "Hammered", "Stippled", "Lines");
    readonly NumericUpDown _textureMm = Number(0.05m, 5, 0.05m, "0.##");
    Control _angleRow = null!, _edgeRow = null!, _edgeMmRow = null!, _textureRow = null!, _textureMmRow = null!;
    TextBlock _edgeMmLabel = null!;

    // Shape and border
    readonly ComboBox _shape = Combo("Rectangle", "Rounded corners", "Cut corners", "Oval", "Circle", "Arched top", "Round ends");
    readonly NumericUpDown _w = Number(20, 3000, 5, "0.#"), _h = Number(15, 3000, 5, "0.#"), _corner = Number(0, 500, 1, "0.#"), _rise = Number(1, 1500, 1, "0.#");
    readonly ComboBox _border = new() { HorizontalAlignment = HorizontalAlignment.Stretch, MaxDropDownHeight = 420 };
    readonly NumericUpDown _bw = Number(1, 200, 0.5m, "0.#"), _bdepth = Number(0.1m, 20, 0.1m, "0.##"), _edgeMargin = Number(0, 100, 0.5m, "0.#");
    readonly ComboBox _panel = Combo("At the surface", "Level with the border's inner edge");
    Control _heightRow = null!, _cornerRow = null!, _riseRow = null!, _borderRows = null!;
    TextBlock _widthLabel = null!, _cornerLabel = null!;

    // Extras
    readonly ComboBox _decoration = Combo([.. Motifs.Names]);
    readonly CheckBox _rule = new() { Content = "A rule under the first line" };
    readonly ComboBox _holes = Combo("None", "One each side", "Both top corners", "All four corners", "One at the top, to hang it");
    readonly NumericUpDown _holeMm = Number(1, 20, 0.5m, "0.#");
    readonly CheckBox _countersink = new() { Content = "Countersink them", IsChecked = true };
    readonly NumericUpDown _ppm = Number(1, 40, 1, "0.#"), _soften = Number(0, 3, 0.05m, "0.##");
    readonly CheckBox _cutLines = new() { Content = "A cut line round the sign" };
    Control _holeRows = null!;

    // Preview and results
    readonly SignView _view = new();
    readonly ComboBox _look = Combo("Wood", "Gilt", "Metal", "Depth map");
    readonly TextBlock _summary = new() { TextWrapping = TextWrapping.Wrap };
    readonly TextBlock _warning = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brush("Warn") };
    readonly TextBlock _busy = new() { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Classes = { "muted" } };
    readonly Button _make = new() { Content = "Put in the design", Classes = { "primary" }, MinWidth = 150 };
    readonly Button _save = new() { Content = "Save depth map…", MinWidth = 130 };
    readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(160) };

    /// <summary>The border list: none, every design the border maker comes with, and the sign's own if it's another.</summary>
    readonly List<BorderDesign?> Borders = [null, .. BorderPresets.All];

    SignSpec _spec;
    bool _loading, _wordsTouched;
    SignPlan? _plan;

    /// <summary>The layer the sign was put on (null = it wasn't put in the design).</summary>
    public Guid? MadeOnLayer { get; private set; }

    public SignsWindow(Document doc, MachineProfile machine, string? dataFolder, Action<string>? status = null,
        (SignSpec Spec, HashSet<Guid> Ids, Vec2 TopLeft, Guid LayerId)? editing = null)
    {
        _doc = doc;
        _machine = machine;
        _dataFolder = dataFolder;
        _status = status;
        _editing = editing;
        Title = "Sign maker";
        Width = 1240;
        Height = 840;
        MinWidth = 1000;
        MinHeight = 660;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");

        _spec = editing?.Spec ?? LastUsed.Load(dataFolder) ?? SignPresets.Default;
        if (_spec.Border is { } own && BorderIndex(own) == 0) Borders.Add(own with { Category = BorderPresets.MyDesigns });
        _wordsTouched = editing is not null;

        var tabs = new TabControl
        {
            Items =
            {
                Tab("Signs", SignsTab()),
                Tab("Words", WordsTab()),
                Tab("Lettering", LetteringTab()),
                Tab("Shape", ShapeTab()),
                Tab("Extras", ExtrasTab()),
            },
        };
        var left = new Border { Width = 470, Padding = new Thickness(14, 10, 8, 0), Child = tabs };

        var fit = new Button { Content = "See it all" };
        ToolTip.SetTip(fit, "Fit the whole sign in the view again (or double-click it). Scroll to zoom in on a letter; drag to move.");
        fit.Click += (_, _) => _view.Fit();
        var bar = new Grid { ColumnDefinitions = new("Auto,140,*,Auto"), ColumnSpacing = 8, Margin = new Thickness(0, 0, 0, 6) };
        bar.Children.Add(new TextBlock { Text = "Show as", VerticalAlignment = VerticalAlignment.Center, Classes = { "muted" } });
        Grid.SetColumn(_look, 1);
        bar.Children.Add(_look);
        var hint = Muted("Scroll to zoom, drag to move");
        hint.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(hint, 2);
        bar.Children.Add(hint);
        Grid.SetColumn(fit, 3);
        bar.Children.Add(fit);

        var results = new StackPanel { Spacing = 6, Children = { _summary, _warning } };
        var right = new Grid { RowDefinitions = new("Auto,*,Auto"), Margin = new Thickness(0, 14, 14, 0) };
        right.Children.Add(bar);
        var frame = new Border { CornerRadius = new(8), ClipToBounds = true, BorderBrush = Brush("Line"), BorderThickness = new(1), Child = _view };
        Grid.SetRow(frame, 1);
        right.Children.Add(frame);
        var scroll = new ScrollViewer { Content = results, MaxHeight = 150, Margin = new Thickness(0, 8, 0, 0), HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(scroll, 2);
        right.Children.Add(scroll);

        var bottom = new Grid { ColumnDefinitions = new("*,Auto,Auto,Auto"), Margin = new Thickness(14, 8, 14, 14), ColumnSpacing = 10 };
        bottom.Children.Add(_busy);
        Grid.SetColumn(_save, 1);
        bottom.Children.Add(_save);
        Grid.SetColumn(_make, 2);
        bottom.Children.Add(_make);
        var close = new Button { Content = "Close", IsCancel = true, MinWidth = 90 };
        close.Click += (_, _) => Close();
        Grid.SetColumn(close, 3);
        bottom.Children.Add(close);

        var body = new Grid { ColumnDefinitions = new("Auto,*"), RowDefinitions = new("*,Auto") };
        body.Children.Add(left);
        Grid.SetColumn(right, 1);
        body.Children.Add(right);
        Grid.SetRow(bottom, 1);
        Grid.SetColumnSpan(bottom, 2);
        body.Children.Add(bottom);
        Content = body;

        if (editing is not null) _make.Content = "Update in the design";
        _make.Click += async (_, _) => await Safely(Make);
        _save.Click += async (_, _) => await Safely(SaveFile);
        _debounce.Tick += (_, _) => { _debounce.Stop(); Rebuild(); };
        Closed += (_, _) => { _debounce.Stop(); _thumbCancel?.Cancel(); foreach (var b in _thumbs.Values) b.Dispose(); _thumbs.Clear(); };

        FillGallery();
        Load(_spec);
        Hook();
        Rebuild();
    }

    /// <summary>Runs a button's work, saying what went wrong instead of letting it take the program down.</summary>
    async Task Safely(Func<Task> work)
    {
        try { await work(); }
        catch (Exception e)
        {
            _busy.Text = $"Couldn't do it: {e.Message}";
            _make.IsEnabled = _save.IsEnabled = _plan is not null;
        }
    }

    static TabItem Tab(string header, Control content) => new()
    {
        Header = header,
        Content = new ScrollViewer { Content = content, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 8, 8, 8) },
    };

    static ComboBox Combo(params string[] items) => new() { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = items, SelectedIndex = 0 };

    static Control F(string label, Control editor, string? tip = null) => Field(label, editor, null, tip);

    static (Control Row, TextBlock Label) FL(string label, Control editor, string? tip = null)
    {
        var row = Field(label, editor, null, tip);
        var text = ((Grid)row).Children.OfType<TextBlock>().First();
        return (row, text);
    }

    // ---- the tabs ----

    Control SignsTab()
    {
        _gallery.Height = 560;
        return new StackPanel
        {
            Spacing = 8,
            Children =
            {
                Hint("Pick a sign to start from, then change anything you like on the other tabs. Once you've typed your own words, picking another sign keeps them."),
                _gallery,
                _about,
            },
        };
    }

    Control WordsTab()
    {
        var grid = new Grid { ColumnDefinitions = new("*,62,62"), ColumnSpacing = 6, RowSpacing = 6 };
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        void Head(string text, int col, string tip)
        {
            var t = Muted(text);
            ToolTip.SetTip(t, tip);
            Grid.SetColumn(t, col);
            grid.Children.Add(t);
        }
        Head("Words, a line each", 0, "Up to four lines. Leave a line empty to skip it.");
        Head("Size %", 1, "This line's letters against a line at 100%.");
        Head("Curve °", 2, "Bends the line round a circle: + arches it up, − curves it down like a smile. 0 is straight.");
        for (int i = 0; i < LineCount; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            _text[i] = new TextBox { Watermark = i == 0 ? "The first line" : "Another line (optional)" };
            // No spin buttons: they'd leave no room for the number. The arrow keys and the wheel still step it.
            _size[i] = Number(10, 300, 5, "0").With(n => n.ShowButtonSpinner = false);
            _bend[i] = Number(-300, 300, 5, "0").With(n => n.ShowButtonSpinner = false);
            Grid.SetRow(_text[i], i + 1);
            Grid.SetRow(_size[i], i + 1);
            Grid.SetRow(_bend[i], i + 1);
            Grid.SetColumn(_size[i], 1);
            Grid.SetColumn(_bend[i], 2);
            grid.Children.Add(_text[i]);
            grid.Children.Add(_size[i]);
            grid.Children.Add(_bend[i]);
        }
        _font.ItemsSource = SignFonts.Families;
        ToolTip.SetTip(_fit, "Make the words as big as they can be inside the border, keeping the Space round them clear. Untick to give the letters' height yourself.");
        return new StackPanel
        {
            Spacing = 8,
            Children =
            {
                grid,
                F("Font", _font, "Any font on this computer. Serif fonts (Georgia, Times New Roman) carve well; very thin fonts don't."),
                Row(_bold, _italic),
                F("Letter spacing %", _spacing, "Extra space between letters, as a share of the capitals' height. Capitals often look better a little spaced out."),
                F("Line gap %", _gap, "Space between lines, as a share of the capitals' height."),
                _fit,
                F("Letter height", _letter, "The capitals' height on a 100% line (mm). When the words are made as big as fits, this shows the height they came out."),
                F("Space round", _margin, "Space kept clear between the words and the border or the screw holes (mm)."),
                F("Move down", _offset, "Moves the words down (or up, below 0) from the middle of the sign (mm)."),
            },
        };
    }

    Control LetteringTab()
    {
        _angleRow = F("V angle", _angle, "The angle of the V across each stroke (degrees): 60° is a usual V bit; a smaller angle carves deeper for the same width.");
        _edgeRow = F("Edge", _edge, "Square: straight walls. Chamfer: a bevel. Rounded: rounded over.");
        (_edgeMmRow, _edgeMmLabel) = FL("Edge width", _edgeMm, "How wide the bevel or rounding is, in from the letter's outline (mm).");
        _textureRow = F("Background", _texture, "The carved-away panel round raised letters: smooth, hammered, stippled with little pits, or fine level lines.");
        _textureMmRow = F("Texture height", _textureMm, "How much the texture stands up from the panel (mm).");
        return new StackPanel
        {
            Spacing = 8,
            Children =
            {
                F("Letters", _style, "How the words are carved."),
                _styleAbout,
                F("Depth", _depth, "How deep the letters are carved, or how far raised letters stand up (mm)."),
                _angleRow,
                _edgeRow,
                _edgeMmRow,
                _textureRow,
                _textureMmRow,
            },
        };
    }

    Control ShapeTab()
    {
        Control widthRow;
        (widthRow, _widthLabel) = FL("Width", _w, "The sign's width (mm); a circle's diameter.");
        _heightRow = F("Height", _h, "The sign's height (mm).");
        (_cornerRow, _cornerLabel) = FL("Corner radius", _corner, "How round the corners are (mm).");
        _riseRow = F("Arch rise", _rise, "How far the top of the arch rises above where the straight sides start (mm). Half the width makes a round arch.");
        _border.ItemsSource = Borders.Select(b => b is null ? "No border" : $"{b.Name}  ·  {Category(b)}").ToList();
        _borderRows = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                F("Border width", _bw, "How wide the border is across (mm). Every band is made wider or narrower in proportion."),
                F("Border depth", _bdepth, "How deep the border's deepest part is carved (mm)."),
                F("Plain edge", _edgeMargin, "A flat strip left between the sign's edge and the border (mm). 0 runs the border right to the edge."),
                F("Panel", _panel, "Where the panel inside the border sits: at the board's surface, or level with the border's inner edge (sunk below the surface when the border falls toward the middle)."),
            },
        };
        return new StackPanel
        {
            Spacing = 8,
            Children =
            {
                F("Shape", _shape, "The sign's outline."),
                widthRow,
                _heightRow,
                _cornerRow,
                _riseRow,
                Muted("Border").With(t => t.Margin = new Thickness(0, 8, 0, 0)),
                F("Design", _border, "A carved border round the edge, from the border maker: picture frames, cabinet and furniture edges, or any ornament on its own band."),
                _borderRows,
                Hint("The border follows the sign's edge: its patterns turn on the diagonal at square corners and bend round curves, with whole repeats along each side."),
            },
        };
    }

    static string Category(BorderDesign d) => d.Category switch
    {
        BorderPresets.Frames => "frame",
        BorderPresets.Edges => "edge",
        BorderPresets.Bands => "band",
        _ => "your own",
    };

    Control ExtrasTab()
    {
        _holeRows = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                F("Hole size", _holeMm, "The screw holes' diameter (mm). 4.5 suits a No. 8 (4 mm) screw."),
                _countersink,
            },
        };
        ToolTip.SetTip(_countersink, "Carve a 90° cone round each hole, so a countersunk screw head sits flush.");
        ToolTip.SetTip(_rule, "A carved line with a diamond in its middle, between the first and second lines.");
        ToolTip.SetTip(_cutLines, "Put a cut line round the sign on the Cut layer, to cut it out after carving. The screw holes are always cut.");
        return new StackPanel
        {
            Spacing = 8,
            Children =
            {
                F("Motif", _decoration, "A small carved motif either side of the first line, carved the same way as the letters."),
                _rule,
                F("Screw holes", _holes, "Holes to put the sign up with, inside the border. They're cut through on the Cut layer."),
                _holeRows,
                Muted("Output").With(t => t.Margin = new Thickness(0, 8, 0, 0)),
                F("Pixels per mm", _ppm, "The depth map's detail. 10 per mm (254 dpi) suits most carving; more only makes bigger files."),
                F("Soften (mm)", _soften, "Blurs the heights a little, rounding sharp walls off: kinder to wood. 0 = crisp."),
                _cutLines,
                Hint("The depth map is a greyscale picture: white is the surface (not carved) and black is the deepest. " +
                     "It goes on its own Picture layer set to Depth slices, where you set how many slices and the power and passes that carve to the depth you made it. " +
                     "Outside the sign and in the screw holes the picture is see-through, so nothing is burned there."),
            },
        };
    }

    // ---- loading and reading the settings ----

    void Load(SignSpec s)
    {
        _loading = true;
        _spec = s;
        for (int i = 0; i < LineCount; i++)
        {
            var line = i < s.Lines.Count ? s.Lines[i] : new SignLine();
            _text[i].Text = line.Text;
            _size[i].Value = (decimal)Math.Clamp(line.SizePct, 10, 300);
            _bend[i].Value = (decimal)Math.Clamp(line.BendDeg, -300, 300);
        }
        string font = SignFonts.Resolve(s.Font);
        _font.SelectedItem = SignFonts.Families.FirstOrDefault(f => string.Equals(f, font, StringComparison.OrdinalIgnoreCase));
        _bold.IsChecked = s.Bold;
        _italic.IsChecked = s.Italic;
        _spacing.Value = (decimal)s.LetterSpacingPct;
        _gap.Value = (decimal)s.LineGapPct;
        _fit.IsChecked = s.FitWords;
        _letter.Value = (decimal)Math.Clamp(s.LetterHeightMm, 1, 500);
        _margin.Value = (decimal)s.MarginMm;
        _offset.Value = (decimal)s.OffsetYMm;

        _style.SelectedIndex = (int)s.Style;
        _depth.Value = (decimal)s.LetterDepthMm;
        _angle.Value = (decimal)Math.Clamp(s.VAngleDeg, 20, 150);
        _edge.SelectedIndex = (int)s.Edge;
        _edgeMm.Value = (decimal)Math.Max(0.05, s.EdgeMm);
        _texture.SelectedIndex = (int)s.Texture;
        _textureMm.Value = (decimal)Math.Max(0.05, s.TextureMm);

        _shape.SelectedIndex = (int)s.Shape;
        _w.Value = (decimal)s.WidthMm;
        _h.Value = (decimal)s.HeightMm;
        _corner.Value = (decimal)s.CornerMm;
        _rise.Value = (decimal)Math.Max(1, s.ArchRiseMm);
        _border.SelectedIndex = BorderIndex(s.Border);
        _bw.Value = (decimal)Math.Max(1, Math.Round(s.BorderWidth > 0 ? s.BorderWidth : 10, 1));
        _bdepth.Value = (decimal)Math.Max(0.1, Math.Round(s.BorderDepthMm > 0 ? s.BorderDepthMm : s.Border?.DepthMm ?? 2, 2));
        _edgeMargin.Value = (decimal)s.EdgeMarginMm;
        _panel.SelectedIndex = (int)s.Panel;

        _decoration.SelectedIndex = (int)s.Decoration;
        _rule.IsChecked = s.Rule;
        _holes.SelectedIndex = (int)s.Holes;
        _holeMm.Value = (decimal)s.HoleMm;
        _countersink.IsChecked = s.Countersink;
        _ppm.Value = (decimal)s.PixelsPerMm;
        _soften.Value = (decimal)s.SoftenMm;
        _cutLines.IsChecked = s.CutLines;
        _loading = false;
        ShowRows();
    }

    /// <summary>Where a border design is in the list: by its name and category (a changed copy from the border maker is added).</summary>
    int BorderIndex(BorderDesign? d)
    {
        if (d is null) return 0;
        for (int i = 1; i < Borders.Count; i++)
            if (Borders[i]!.Name == d.Name && (Borders[i]!.Category == d.Category || i == Borders.Count - 1 && Borders[i]!.Category == BorderPresets.MyDesigns)) return i;
        return 0;
    }

    static double V(NumericUpDown n, double fallback) => n.Value is { } v ? (double)v : fallback;

    SignSpec Spec()
    {
        var lines = new List<SignLine>();
        for (int i = 0; i < LineCount; i++)
            lines.Add(new SignLine(_text[i].Text ?? "", V(_size[i], 100), V(_bend[i], 0)));
        while (lines.Count > 1 && string.IsNullOrWhiteSpace(lines[^1].Text)) lines.RemoveAt(lines.Count - 1);
        var border = Borders[Math.Max(0, _border.SelectedIndex)];
        return _spec with
        {
            Lines = lines,
            Font = _font.SelectedItem as string ?? _spec.Font,
            Bold = _bold.IsChecked == true,
            Italic = _italic.IsChecked == true,
            LetterSpacingPct = V(_spacing, 0),
            LineGapPct = V(_gap, 35),
            FitWords = _fit.IsChecked == true,
            LetterHeightMm = V(_letter, 20),
            MarginMm = V(_margin, 5),
            OffsetYMm = V(_offset, 0),
            Style = (LetterStyle)Math.Max(0, _style.SelectedIndex),
            LetterDepthMm = V(_depth, 2),
            VAngleDeg = V(_angle, 60),
            Edge = (LetterEdge)Math.Max(0, _edge.SelectedIndex),
            EdgeMm = V(_edgeMm, 0.8),
            Texture = (Background)Math.Max(0, _texture.SelectedIndex),
            TextureMm = V(_textureMm, 0.3),
            Shape = (SignShape)Math.Max(0, _shape.SelectedIndex),
            WidthMm = V(_w, 200),
            HeightMm = V(_h, 110),
            CornerMm = V(_corner, 14),
            ArchRiseMm = V(_rise, 30),
            Border = border,
            BorderWidthMm = V(_bw, 10),
            BorderDepthMm = V(_bdepth, 2),
            EdgeMarginMm = V(_edgeMargin, 0),
            Panel = (PanelLevel)Math.Max(0, _panel.SelectedIndex),
            Decoration = (Decoration)Math.Max(0, _decoration.SelectedIndex),
            Rule = _rule.IsChecked == true,
            Holes = (SignHoles)Math.Max(0, _holes.SelectedIndex),
            HoleMm = V(_holeMm, 4.5),
            Countersink = _countersink.IsChecked == true,
            PixelsPerMm = V(_ppm, 10),
            SoftenMm = V(_soften, 0),
            CutLines = _cutLines.IsChecked == true,
        };
    }

    void Hook()
    {
        for (int i = 0; i < LineCount; i++)
        {
            _text[i].TextChanged += (_, _) => { if (!_loading) { _wordsTouched = true; Changed(); } };
            _size[i].ValueChanged += (_, _) => Changed();
            _bend[i].ValueChanged += (_, _) => Changed();
        }
        foreach (var n in new[] { _spacing, _gap, _letter, _margin, _offset, _depth, _angle, _edgeMm, _textureMm, _w, _h, _corner, _rise, _bw, _bdepth, _edgeMargin, _holeMm, _ppm, _soften })
            n.ValueChanged += (_, _) => Changed();
        foreach (var c in new[] { _font, _edge, _texture, _panel, _decoration })
            c.SelectionChanged += (_, _) => Changed();
        foreach (var c in new[] { _style, _shape, _holes })
            c.SelectionChanged += (_, _) => { ShowRows(); Changed(); };
        _border.SelectionChanged += (_, _) =>
        {
            // A new border comes at its own width and depth.
            if (!_loading && Borders[Math.Max(0, _border.SelectedIndex)] is { } b)
            {
                _loading = true;
                _bw.Value = (decimal)Math.Round(Math.Clamp(b.WidthMm, 1, 200), 1);
                _bdepth.Value = (decimal)Math.Round(Math.Clamp(b.DepthMm, 0.1, 20), 2);
                _loading = false;
            }
            ShowRows();
            Changed();
        };
        foreach (var c in new[] { _bold, _italic, _rule, _countersink, _cutLines })
            c.IsCheckedChanged += (_, _) => Changed();
        _fit.IsCheckedChanged += (_, _) => { ShowRows(); Changed(); };
        _look.SelectionChanged += (_, _) => _view.Look = (PreviewLook)Math.Max(0, _look.SelectedIndex);
        _gallery.SelectionChanged += (_, _) => Pick();
    }

    void ShowRows()
    {
        var style = (LetterStyle)Math.Max(0, _style.SelectedIndex);
        _styleAbout.Text = style switch
        {
            LetterStyle.VCarved => "A V groove down each stroke, deepest in the middle, as a V bit carves it. Wide strokes get a flat bottom at the depth you set. Crisp and traditional.",
            LetterStyle.Sunk => "Each letter carved down to a flat floor, with straight, bevelled or rounded edges.",
            LetterStyle.Raised => "The panel round the letters is carved away, so they stand up. The panel can have a texture.",
            LetterStyle.Domed => "Raised letters rounded over like cushions. A rounding of half the strokes' width or more rounds them fully.",
            _ => "Raised letters with a ridge down the middle of each stroke: a V carving turned inside out.",
        };
        _angleRow.IsVisible = style is LetterStyle.VCarved or LetterStyle.Chiselled;
        _edgeRow.IsVisible = style is LetterStyle.Sunk or LetterStyle.Raised;
        _edgeMmRow.IsVisible = style == LetterStyle.Domed || (_edgeRow.IsVisible && _edge.SelectedIndex > 0);
        _edgeMmLabel.Text = style == LetterStyle.Domed ? "Rounding" : "Edge width";
        _textureRow.IsVisible = _textureMmRow.IsVisible = style is LetterStyle.Raised or LetterStyle.Domed or LetterStyle.Chiselled;

        var shape = (SignShape)Math.Max(0, _shape.SelectedIndex);
        _widthLabel.Text = shape == SignShape.Circle ? "Diameter" : "Width";
        _heightRow.IsVisible = shape != SignShape.Circle;
        _cornerRow.IsVisible = shape is SignShape.Rounded or SignShape.CutCorners or SignShape.Arched;
        _cornerLabel.Text = shape switch { SignShape.CutCorners => "Corner cut", SignShape.Arched => "Shoulders", _ => "Corner radius" };
        _riseRow.IsVisible = shape == SignShape.Arched;
        _borderRows.IsVisible = _border.SelectedIndex > 0;
        _holeRows.IsVisible = _holes.SelectedIndex > 0;
        _letter.IsEnabled = _fit.IsChecked != true;
    }

    void Changed()
    {
        if (_loading) return;
        _debounce.Stop();
        _debounce.Start();
    }

    // ---- the gallery ----

    void FillGallery()
    {
        var items = new List<Control>();
        var todo = new List<(SignPreset, Image)>();
        foreach (var p in SignPresets.All)
        {
            var img = new Image { Width = 120, Height = 74, Stretch = Stretch.Uniform };
            if (_thumbs.TryGetValue(p.Name, out var bmp)) img.Source = bmp; else todo.Add((p, img));
            var row = new Grid { ColumnDefinitions = new("120,*"), ColumnSpacing = 10, Tag = p, Margin = new Thickness(0, 2) };
            row.Children.Add(img);
            var (w, h) = p.Spec.Size();
            var text = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    new TextBlock { Text = p.Name, FontWeight = FontWeight.SemiBold },
                    new TextBlock { Text = p.About, Classes = { "muted" }, FontSize = 11, TextWrapping = TextWrapping.Wrap },
                    new TextBlock { Text = $"{w:0} × {h:0} mm", Classes = { "muted" }, FontSize = 11 },
                },
            };
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            items.Add(row);
        }
        _loading = true;
        _gallery.ItemsSource = items;
        _gallery.SelectedIndex = SignPresets.All.ToList().FindIndex(p => p.Name == _spec.Name);
        _loading = false;
        _ = Thumbnails(todo);
    }

    /// <summary>Draws the gallery's little pictures in the background, one at a time.</summary>
    async Task Thumbnails(List<(SignPreset Preset, Image Img)> todo)
    {
        _thumbCancel?.Cancel();
        var cancel = _thumbCancel = new CancellationTokenSource();
        foreach (var (p, img) in todo)
        {
            if (cancel.IsCancellationRequested) return;
            try
            {
                var bmp = await Task.Run(() => Thumbnail(p.Spec), cancel.Token);
                if (cancel.IsCancellationRequested) { bmp?.Dispose(); return; }
                if (bmp is null) continue;
                if (_thumbs.Remove(p.Name, out var old)) old.Dispose();
                _thumbs[p.Name] = bmp;
                img.Source = bmp;
            }
            catch (OperationCanceledException) { return; }
        }
    }

    /// <summary>The sign, lit, about 240 pixels wide.</summary>
    static Bitmap? Thumbnail(SignSpec spec)
    {
        if (spec.Problem() is not null) return null;
        var plan = new SignPlan(spec);
        double mmPerPx = Math.Max(plan.SizeW / 240, plan.SizeH / 148);
        int cols = Math.Max(1, (int)Math.Ceiling(plan.SizeW / mmPerPx)), rows = Math.Max(1, (int)Math.Ceiling(plan.SizeH / mmPerPx));
        var h = plan.Render(0, 0, mmPerPx, cols, rows);
        return CameraImaging.ToAvaloniaRgba(Shading.Shade(h, cols, rows, mmPerPx, PreviewLook.Wood, plan.Top, plan.Lowest));
    }

    void Pick()
    {
        if (_loading || _gallery.SelectedItem is not Grid { Tag: SignPreset p }) return;
        var next = p.Spec;
        // Your own words stay; everything else comes from the sign picked.
        if (_wordsTouched) next = next with { Lines = Spec().Lines };
        Load(next);
        _about.Text = p.About + (_wordsTouched ? " Your words are kept." : "");
        Changed();
    }

    // ---- the preview and the summary ----

    void Rebuild()
    {
        var spec = Spec();
        _spec = spec;
        _warning.Text = "";
        if (spec.Problem() is { } problem)
        {
            _plan = null;
            _view.Show(null, problem);
            _summary.Text = "";
            _make.IsEnabled = _save.IsEnabled = false;
            return;
        }
        var plan = new SignPlan(spec);
        _plan = plan;
        _view.Show(plan);
        _make.IsEnabled = _save.IsEnabled = true;
        if (spec.FitWords && plan.Words.LetterHeightMm > 0)
        {
            _loading = true;
            _letter.Value = (decimal)Math.Clamp(Math.Round(plan.Words.LetterHeightMm, 1), 1, 500);
            _loading = false;
        }
        _summary.Text = Summary(plan);
        _warning.Text = Warnings(plan);
        _warning.IsVisible = _warning.Text.Length > 0;
    }

    static string ShapeName(SignSpec s) => s.Shape switch
    {
        SignShape.Rectangle => "rectangular",
        SignShape.Rounded => "round-cornered",
        SignShape.CutCorners => "cut-cornered",
        SignShape.Oval => "oval",
        SignShape.Circle => "round",
        SignShape.Arched => "arch-topped",
        _ => "round-ended",
    };

    static string StyleName(SignSpec s) => s.Style switch
    {
        LetterStyle.VCarved => $"V-carved ({s.VAngleDeg:0}°) up to {s.LetterDepthMm:0.##} mm deep",
        LetterStyle.Sunk => $"sunk {s.LetterDepthMm:0.##} mm",
        LetterStyle.Raised => $"raised {s.LetterDepthMm:0.##} mm",
        LetterStyle.Domed => $"domed, standing up {s.LetterDepthMm:0.##} mm",
        _ => $"chiselled, standing up {s.LetterDepthMm:0.##} mm",
    };

    static string Summary(SignPlan plan)
    {
        var s = plan.Spec;
        var lines = new List<string>();
        string size = s.Shape == SignShape.Circle ? $"{plan.SizeW:0.#} mm across" : $"{plan.SizeW:0.#} × {plan.SizeH:0.#} mm";
        string words = s.Words.Count == 0 ? "no words yet" : $"the words {StyleName(s)}, capitals {plan.Words.LetterHeightMm:0.#} mm high";
        string shape = ShapeName(s);
        lines.Add($"{("aeiou".Contains(shape[0]) ? "An" : "A")} {shape} sign {size}, {words}.");
        if (plan.Border is { } b)
        {
            string name = s.Border!.Name;
            lines.Add($"Border: {name}, {b.WidthMm:0.#} mm wide and {b.DepthMm:0.##} mm deep." + (plan.PanelDepth > 0.01 ? $" The panel inside it is {plan.PanelDepth:0.##} mm below the surface." : ""));
            foreach (var g in plan.Repeats().GroupBy(r => r.Band))
            {
                var band = b.Bands[g.Key];
                var runs = g.ToList();
                string orn = Ornaments.Info(band.Ornament).Name;
                lines.Add(runs.Count == 1
                    ? $"{orn}: {runs[0].Repeats} all the way round, {runs[0].PitchMm:0.#} mm apart."
                    : $"{orn}: {ListWords(runs.Select(r => $"{r.Repeats} along the {r.Run.ToLowerInvariant()}"))}.");
            }
        }
        if (plan.Holes.Count > 0)
            lines.Add($"{plan.Holes.Count} screw hole{(plan.Holes.Count == 1 ? "" : "s")}, {s.HoleMm:0.#} mm" + (s.Countersink ? $", countersunk to {s.CountersinkMm:0.#} mm." : "."));
        double ppm = SignOutput.PixelsPerMm(plan);
        string px = ppm < s.PixelsPerMm - 0.01 ? $" (lowered to {ppm:0.#} per mm to stay within {SignOutput.MaxSide} pixels a side)" : $" ({s.PixelsPerMm:0.#} per mm)";
        lines.Add($"Depth map: {Math.Round(plan.SizeW * ppm):0} × {Math.Round(plan.SizeH * ppm):0} pixels{px}. White is the surface, black is {plan.Top:0.##} mm down.");
        return string.Join("\n", lines);
    }

    string Warnings(SignPlan plan)
    {
        var s = plan.Spec;
        var w = new List<string>();
        double fw = _machine.FieldW, fh = _machine.FieldH;
        if (!((plan.SizeW <= fw && plan.SizeH <= fh) || (plan.SizeW <= fh && plan.SizeH <= fw)))
            w.Add($"It's bigger than the work area ({fw:0} × {fh:0} mm).");
        if (s.Words.Count > 0 && !plan.Words.Fits)
            w.Add(s.FitWords
                ? "There's no room for the words: make the sign bigger, the border narrower, or the space round the words smaller."
                : "The words run into the border, the edge or a screw hole: make the letters smaller, or tick \"As big as fits\".");
        if (!SignFonts.Installed(s.Font))
            w.Add($"The font \"{s.Font}\" isn't on this computer: {SignFonts.Resolve(s.Font)} is used instead.");
        if (s.Words.Count > 0 && plan.Words.LineHeights.Count > 0 && plan.Words.LineHeights.Min() < 5)
            w.Add($"Some letters are only {plan.Words.LineHeights.Min():0.#} mm high: fine detail that small may not carve cleanly.");
        if (s.Holes != SignHoles.None && plan.Holes.Count == 0)
            w.Add("There's no room for the screw holes inside the border.");
        return string.Join("\n", w);
    }

    // ---- making it ----

    async Task<RasterImage?> Map(SignPlan plan)
    {
        _busy.Text = "Making the depth map…";
        _make.IsEnabled = _save.IsEnabled = false;
        try
        {
            return await Task.Run(() => SignOutput.DepthMap(plan));
        }
        catch (Exception e) when (e is OutOfMemoryException or ArgumentException)
        {
            _busy.Text = $"Couldn't make it: {e.Message}";
            return null;
        }
        finally { _make.IsEnabled = _save.IsEnabled = _plan is not null; }
    }

    async Task Make()
    {
        Rebuild();
        if (_plan is not { } plan) return;
        var map = await Map(plan);
        if (map is null) return;
        var state = _doc.State;
        Vec2? at = null;
        Layer? layer = null;
        if (_editing is { } ed)
        {
            state = Ops.Delete(state, ed.Ids);
            at = ed.TopLeft;
            layer = state.Layers.FirstOrDefault(l => l.Id == ed.LayerId);
        }
        var (next, ids, layerId) = SignOutput.Add(state, plan, map, _machine, at, layer);
        _doc.Commit(_editing is null ? "Sign" : "Change sign", next, ids);
        MadeOnLayer = layerId;
        LastUsed.Store(_dataFolder, plan.Spec);
        _status?.Invoke($"Sign: {plan.SizeW:0.#} × {plan.SizeH:0.#} mm, on the layer \"{next.Layers.First(l => l.Id == layerId).Name}\" (depth slices).");
        Close();
    }

    async Task SaveFile()
    {
        Rebuild();
        if (_plan is not { } plan) return;
        string name = string.Join(" ", plan.Spec.Words.Select(l => l.Text.Trim()));
        foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, ' ');
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save depth map",
            SuggestedFileName = $"{(name.Length > 0 ? name : "Sign")} depth map.png",
            DefaultExtension = "png",
            FileTypeChoices = [new FilePickerFileType("PNG picture") { Patterns = ["*.png"] }],
        });
        if (file?.TryGetLocalPath() is not { } path) return;
        var map = await Map(plan);
        if (map is null) return;
        try
        {
            await File.WriteAllBytesAsync(path, map.EncodePng());
            LastUsed.Store(_dataFolder, plan.Spec);
            _busy.Text = $"Saved {Path.GetFileName(path)}.";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _busy.Text = $"Couldn't save it: {e.Message}"; }
    }
}

/// <summary>The settings last used, kept in the add-on's data folder.</summary>
static class LastUsed
{
    const string FileName = "last.json";

    public static SignSpec? Load(string? folder)
    {
        if (folder is null) return null;
        try
        {
            var path = Path.Combine(folder, FileName);
            return File.Exists(path) ? SignSpec.FromJson(File.ReadAllText(path)) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    public static void Store(string? folder, SignSpec spec)
    {
        if (folder is null) return;
        try
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, FileName), spec.ToJson());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* not remembering isn't worth an error */ }
    }
}
