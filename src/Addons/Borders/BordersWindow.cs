using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Bitmap = Avalonia.Media.Imaging.Bitmap;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using PhotonCanvas.App;
using PhotonCanvas.Core;
using PhotonCanvas.Core.Imaging;
using PhotonCanvas.Doc;
using static PhotonCanvas.App.UiKit;

namespace PhotonCanvas.Addon.Borders;

/// <summary>
/// The border maker: pick a design (frames, cabinet and furniture edges, ornament bands, your own),
/// say what it goes round and how big, change its bands if you like, and see it carved; then put its
/// depth map in the design on a depth-slices layer, or save it as a picture.
/// </summary>
public sealed class BordersWindow : Window
{
    readonly Document _doc;
    readonly MachineProfile _machine;
    readonly string? _dataFolder;
    readonly Action<string>? _status;
    readonly (BorderSpec Spec, HashSet<Guid> Ids, Vec2 TopLeft, Guid LayerId)? _editing;

    // Design
    readonly ComboBox _category = Combo([.. BorderPresets.Categories]);
    readonly ListBox _gallery = new() { SelectionMode = SelectionMode.Single };
    readonly TextBlock _about = Hint();
    readonly TextBox _myName = new() { Watermark = "A name for it" };
    readonly Button _saveMine = Button("Save as my design", tip: "Keep the design as it is now (its bands, width and depth) under My designs.");
    readonly Button _deleteMine = Button("Delete from my designs");
    readonly List<BorderDesign> _mine;
    readonly Dictionary<string, Bitmap> _thumbs = [];
    CancellationTokenSource? _thumbCancel;

    // Size
    readonly ComboBox _shape = Combo("A frame", "An oval or circle", "A strip", "A round part on the rotary");
    readonly ComboBox _sizeIs = Combo("The outside", "The opening");
    readonly NumericUpDown _fw = Number(10, 3000, 5, "0.#"), _fh = Number(10, 3000, 5, "0.#");
    readonly ComboBox _corners = Combo("Mitred", "Corner blocks", "Rounded");
    readonly NumericUpDown _radius = Number(1, 1000, 1, "0.#");
    readonly CheckBox _four = new() { Content = "Four separate mitred sticks" };
    readonly ComboBox _middle = Combo("Left as it is", "Level with the inner edge", "Carved to the bottom");
    readonly NumericUpDown _length = Number(5, 5000, 10, "0.#"), _maxPiece = Number(0, 5000, 10, "0.#");
    readonly CheckBox _mitredEnds = new() { Content = "Mitred ends" };
    readonly NumericUpDown _width = Number(1, 300, 1, "0.#"), _depth = Number(0.05m, 20, 0.1m, "0.##");
    readonly CheckBox _motifCorners = new() { Content = "A motif on each corner" };
    readonly CheckBox _centre = new() { Content = "A whole motif in the middle of each side", IsChecked = true };
    Control _frameRows = null!, _stripRows = null!, _radiusRow = null!, _cornerRow = null!, _middleRow = null!;

    // Round a rotary part
    readonly NumericUpDown _partD = Number(2, 119, 0.5m, "0.##");
    readonly Button _useWorkMode = Button("Use Work mode's", tip: "The Object Ø Machine → Work mode → Rotary is set to.");
    readonly ComboBox _wrap = Combo("Round it (rings)", "Along it (flutes)", "A spiral along it");
    readonly NumericUpDown _rings = Number(1, 60, 1, "0"), _round = Number(1, 60, 1, "0"), _starts = Number(1, 60, 1, "0");
    readonly NumericUpDown _gap = Number(0, 500, 1, "0.#"), _wrapLength = Number(5, 1000, 5, "0.#");
    readonly CheckBox _mirrored = new() { Content = "Mirrored pair (inner edges meet in the middle)" };
    readonly CheckBox _turned = new() { Content = "Turned round (the outer edge on the other side)" };
    readonly CheckBox _leftHand = new() { Content = "Twists the other way (left-handed)" };
    readonly double? _workModeD;
    Control _rotaryRows = null!, _ringsRow = null!, _roundRow = null!, _startsRow = null!, _gapRow = null!, _wrapLengthRow = null!, _widthRow = null!;

    // Bands
    readonly ProfileView _profile = new();
    readonly ListBox _bands = new() { SelectionMode = SelectionMode.Single, MaxHeight = 170 };
    readonly NumericUpDown _bw = Number(0.2m, 300, 0.5m, "0.##"), _btop = Number(0, 20, 0.1m, "0.##"), _bdrop = Number(0, 20, 0.1m, "0.##");
    readonly NumericUpDown _bcount = Number(1, 12, 1, "0"), _bcarve = Number(0, 20, 0.1m, "0.##"), _brepeat = Number(0, 500, 0.5m, "0.##");
    readonly ComboBox _bprofile = Combo([.. Profiles.All.Select(p => p.Name)]);
    readonly ComboBox _bornament = Combo([.. Ornaments.All.Select(o => o.Name)]);
    readonly CheckBox _bflip = new() { Content = "Turned round (falls toward the outer edge)" };
    readonly TextBlock _bandAbout = Hint();
    Control _countRow = null!, _bandEditor = null!;

    // Output
    readonly NumericUpDown _ppm = Number(1, 40, 1, "0.#"), _soften = Number(0, 3, 0.05m, "0.##");
    readonly CheckBox _cutLines = new() { Content = "Cut lines round the outside and the opening" };

    // Preview and results
    readonly BorderView _view = new();
    readonly ComboBox _look = Combo("Wood", "Gilt", "Metal", "Depth map");
    readonly CheckBox _onPart = new() { Content = "On the part", IsChecked = true };
    readonly TextBlock _viewHint = Muted("Scroll to zoom, drag to move");
    readonly TextBlock _summary = new() { TextWrapping = TextWrapping.Wrap };
    readonly TextBlock _warning = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brush("Warn") };
    readonly TextBlock _busy = new() { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Classes = { "muted" } };
    readonly Button _make = new() { Content = "Put in the design", Classes = { "primary" }, MinWidth = 150 };
    readonly Button _save = new() { Content = "Save depth map…", MinWidth = 130 };
    readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(160) };

    BorderDesign _design;
    int _band;
    bool _loading;
    BorderPlan? _plan;

    /// <summary>The layer the border was put on (null = it wasn't put in the design).</summary>
    public Guid? MadeOnLayer { get; private set; }

    /// <param name="workModeDiameterMm">The Object Ø Machine → Work mode is set to, when it's the rotary (else null).</param>
    /// <param name="rotary">Open on a border round a rotary part (Machine → Work area → Wrap a pattern).</param>
    public BordersWindow(Document doc, MachineProfile machine, string? dataFolder, Action<string>? status = null,
        (BorderSpec Spec, HashSet<Guid> Ids, Vec2 TopLeft, Guid LayerId)? editing = null, double? workModeDiameterMm = null, bool rotary = false)
    {
        _doc = doc;
        _workModeD = workModeDiameterMm;
        _machine = machine;
        _dataFolder = dataFolder;
        _status = status;
        _editing = editing;
        _mine = MyDesigns.Load(dataFolder);
        Title = "Border maker";
        Width = 1220;
        Height = 820;
        MinWidth = 980;
        MinHeight = 640;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");

        var spec = editing?.Spec ?? LastUsed.Load(dataFolder) ?? new BorderSpec();
        if (editing is null && rotary)
        {
            // Wrapping round a part: an ornament band suits it better than a whole frame moulding.
            spec = spec with
            {
                Layout = BorderLayout.Rotary,
                PartDiameterMm = workModeDiameterMm ?? spec.PartDiameterMm,
                Design = spec.Layout == BorderLayout.Rotary || spec.Design.Category != BorderPresets.Frames ? spec.Design : BorderPresets.All.First(d => d.Name == "Rope"),
                WidthMm = spec.Layout == BorderLayout.Rotary ? spec.WidthMm : 0,
                DepthMm = spec.Layout == BorderLayout.Rotary ? spec.DepthMm : 0,
            };
        }
        _design = spec.Design;

        var tabs = new TabControl
        {
            Items =
            {
                Tab("Design", DesignTab()),
                Tab("Size", SizeTab()),
                Tab("Bands", BandsTab()),
                Tab("Output", OutputTab()),
            },
        };
        var left = new Border { Width = 440, Padding = new Thickness(14, 10, 8, 0), Child = tabs };

        var fit = new Button { Content = "See it all" };
        ToolTip.SetTip(fit, "Fit the whole border in the view again (or double-click it). Scroll to zoom in on a corner; drag to move.");
        fit.Click += (_, _) => _view.Fit();
        var bar = new Grid { ColumnDefinitions = new("Auto,140,*,Auto"), ColumnSpacing = 8, Margin = new Thickness(0, 0, 0, 6) };
        bar.Children.Add(new TextBlock { Text = "Show as", VerticalAlignment = VerticalAlignment.Center, Classes = { "muted" } });
        Grid.SetColumn(_look, 1);
        bar.Children.Add(_look);
        var hint = _viewHint;
        hint.VerticalAlignment = VerticalAlignment.Center;
        _onPart.VerticalAlignment = VerticalAlignment.Center;
        ToolTip.SetTip(_onPart, "See the border round the part, from above as the laser sees it (drag up and down to turn it). Off: the part unrolled, as the depth map is made; the dashed line faces the laser at the start.");
        var mid = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { _onPart, hint } };
        Grid.SetColumn(mid, 2);
        bar.Children.Add(mid);
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

        Load(spec);
        Hook();
        _view.OnThePart = _onPart.IsChecked == true;
        // The list fills when the category changes; it may already be the one wanted.
        int cat = Math.Max(0, BorderPresets.Categories.ToList().IndexOf(_design.Category));
        if (_category.SelectedIndex == cat) FillGallery(); else _category.SelectedIndex = cat;
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

    // ---- the tabs ----

    Control DesignTab()
    {
        _gallery.Height = 430;
        _deleteMine.Click += (_, _) => DeleteMine();
        _saveMine.Click += (_, _) => SaveMine();
        return new StackPanel
        {
            Spacing = 8,
            Children =
            {
                F("Show", _category, "Which designs to list: picture frames, cabinet and furniture edges, each ornament on its own band, or the ones you've saved."),
                _gallery,
                _about,
                Row(_myName, _saveMine),
                _deleteMine,
            },
        };
    }

    Control SizeTab()
    {
        _frameRows = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                F("Size is", _sizeIs, "The outside of the frame, or the opening inside it (the picture or mirror it holds)."),
                F("Width (mm)", _fw),
                F("Height (mm)", _fh),
            },
        };
        _cornerRow = F("Corners", _corners, "Mitred: the sides meet on the diagonal, as a frame's sticks do. Corner blocks: a square with a rosette in each corner. Rounded: the pattern bends round.");
        _radiusRow = F("Corner radius", _radius, "How round the outside corners are (mm).");
        _middleRow = F("Middle", _middle, "What happens inside the frame. Left as it is: not carved (for a frame you'll cut the opening out of, or a mirror). Level with the inner edge, or carved to the bottom: a sunk panel, for a door or a lid.");
        _stripRows = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                F("Length (mm)", _length, "Along the strip's outer edge."),
                _mitredEnds,
                F("Longest piece", _maxPiece, "A strip longer than this is made in pieces that carry on from each other, to carve one after the other (mm). 0 = one piece."),
            },
        };
        _useWorkMode.IsVisible = _workModeD is not null;
        _useWorkMode.Click += (_, _) => { if (_workModeD is { } d) _partD.Value = (decimal)Math.Round(d, 2); };
        _ringsRow = F("Rings", _rings, "How many rings along the part.");
        _gapRow = F("Gap (mm)", _gap, "The plain space between one ring and the next, along the part.");
        _roundRow = F("Copies round", _round, "How many copies side by side all the way round. Each is made as wide as fits, so they meet without a gap.");
        _startsRow = F("Starts", _starts, "How many spirals wind round side by side: 1 is a single twist, 2 a double barley twist, and so on.");
        _wrapLengthRow = F("Length (mm)", _wrapLength, "How far along the part the flutes or the spiral run.");
        _rotaryRows = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                F("Part Ø (mm)", Row(_partD, _useWorkMode), "The part's diameter where the border goes. The pattern is made to go once round exactly this size, so it meets itself round the back: set Machine → Work mode's Object Ø to the same."),
                F("Wraps", _wrap, "Round it: rings, the border's width along the part. Along it: copies running the length of the part side by side all the way round, like flutes. A spiral: the border winds along the part, like a barley twist or a rope."),
                _ringsRow,
                _gapRow,
                _roundRow,
                _startsRow,
                _wrapLengthRow,
                _mirrored,
                _turned,
                _leftHand,
            },
        };
        ToolTip.SetTip(_mirrored, "Each copy is the design and its mirror image side by side, so it's the same both ways across: a frame moulding becomes a collar, an edge a bead.");
        ToolTip.SetTip(_turned, "The design turned round across: its outer edge on the other side (with Mirrored pair, the outer edges meet in the middle instead).");
        ToolTip.SetTip(_leftHand, "The spiral winds the other way, like a left-hand thread.");
        ToolTip.SetTip(_mitredEnds, "Cut both ends at 45° (the outer edge longest), to meet another strip at a corner.");
        ToolTip.SetTip(_four, "Make the frame as four sticks with mitred ends, each its own picture, to carve one at a time and glue up.");
        ToolTip.SetTip(_motifCorners, "Put a motif right on each corner (or each end of a strip). Off: the gap between two motifs sits there.");
        ToolTip.SetTip(_centre, "Fit the repeats so a whole motif sits in the middle of every side.");
        return new StackPanel
        {
            Spacing = 8,
            Children =
            {
                F("Goes round", _shape, "A frame (a rectangle), an oval or circle, a straight strip for an edge or trim, or a round part turning in the rotary (a pen, a handle, a table leg)."),
                _rotaryRows,
                _frameRows,
                _cornerRow,
                _radiusRow,
                _four,
                _middleRow,
                _stripRows,
                (_widthRow = F("Border width", _width, "How wide the border is across (mm). Every band is made wider or narrower in proportion.")),
                F("Depth", _depth, "How deep the deepest part is carved below the surface (mm). Every height is scaled to suit."),
                _motifCorners,
                _centre,
            },
        };
    }

    Control BandsTab()
    {
        var add = Button("Add", tip: "A new band at the inner edge.");
        var copy = Button("Copy", tip: "Another band the same as the chosen one, beside it.");
        var remove = Button("Remove");
        var up = Button("Out", tip: "Move the chosen band toward the outer edge.");
        var down = Button("In", tip: "Move the chosen band toward the inner edge.");
        add.Click += (_, _) => EditBands(l => { l.Add(new BorderBand { WidthMm = 4, TopMm = _design.TopMm }); return l.Count - 1; });
        copy.Click += (_, _) => EditBands(l => { if (_band < 0 || _band >= l.Count) return _band; l.Insert(_band + 1, l[_band]); return _band + 1; });
        remove.Click += (_, _) => EditBands(l => { if (l.Count <= 1 || _band < 0 || _band >= l.Count) return _band; l.RemoveAt(_band); return Math.Min(_band, l.Count - 1); });
        up.Click += (_, _) => EditBands(l => { if (_band <= 0 || _band >= l.Count) return _band; (l[_band - 1], l[_band]) = (l[_band], l[_band - 1]); return _band - 1; });
        down.Click += (_, _) => EditBands(l => { if (_band < 0 || _band >= l.Count - 1) return _band; (l[_band + 1], l[_band]) = (l[_band], l[_band + 1]); return _band + 1; });
        _profile.Picked += i => _bands.SelectedIndex = i;
        _countRow = F("How many", _bcount, "Reeds, flutes or steps across the band; strands of a plait or rope; lines of waves.");
        _bandEditor = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                F("Width (mm)", _bw, "How wide this band is across."),
                F("Top (mm)", _btop, "The band's highest level, above the deepest point of the border."),
                F("Profile", _bprofile, "Its shape across, like a moulding plane or router bit leaves it."),
                F("Drop (mm)", _bdrop, "How far the profile falls from its top: the cove's or ogee's fall, the bead's height."),
                _countRow,
                _bflip,
                F("Ornament", _bornament, "The pattern carved along it."),
                F("Carve depth", _bcarve, "How deep the ornament is carved into the band's surface (mm)."),
                F("Repeat (mm)", _brepeat, "One repeat's length along the band, before it's fitted to the sides. 0 = its natural length for the band's width."),
                _bandAbout,
            },
        };
        return new StackPanel
        {
            Spacing = 8,
            Children =
            {
                Hint("The border cut across, outer edge on the left. Click a band to change it; carving is shown darker."),
                new Border { CornerRadius = new(6), ClipToBounds = true, Child = _profile },
                _bands,
                Row(add, copy, remove, up, down),
                _bandEditor,
            },
        };
    }

    Control OutputTab()
    {
        return new StackPanel
        {
            Spacing = 8,
            Children =
            {
                F("Pixels per mm", _ppm, "The depth map's detail. 10 per mm (254 dpi) suits most carving; more only makes bigger files."),
                F("Soften (mm)", _soften, "Blurs the heights a little, rounding sharp walls off: kinder to wood. 0 = crisp."),
                _cutLines,
                Hint("The depth map is a greyscale picture: white is the surface (not carved) and black is the deepest. " +
                     "It goes on its own Picture layer set to Depth slices, where you set how many slices and the power and passes that carve to the depth you made it. " +
                     "Where it isn't carved (outside the border, or the middle left as it is) the picture is see-through, so nothing is burned there."),
            },
        };
    }

    // ---- loading and reading the settings ----

    void Load(BorderSpec s)
    {
        _loading = true;
        _design = s.Design;
        _shape.SelectedIndex = s.Layout switch { BorderLayout.Oval => 1, BorderLayout.Strip => 2, BorderLayout.Rotary => 3, _ => 0 };
        _sizeIs.SelectedIndex = s.SizeIsOpening ? 1 : 0;
        _fw.Value = (decimal)s.FrameWidthMm;
        _fh.Value = (decimal)s.FrameHeightMm;
        _corners.SelectedIndex = (int)s.Corners;
        _radius.Value = (decimal)s.CornerRadiusMm;
        _four.IsChecked = s.FourPieces;
        _middle.SelectedIndex = (int)s.Middle;
        _length.Value = (decimal)s.LengthMm;
        _mitredEnds.IsChecked = s.MitredEnds;
        _maxPiece.Value = (decimal)s.MaxPieceMm;
        _width.Value = (decimal)Math.Round(s.BorderWidth, 2);
        _depth.Value = (decimal)Math.Round(s.DepthMm > 0 ? s.DepthMm : s.Design.DepthMm, 2);
        _motifCorners.IsChecked = s.MotifOnCorners;
        _centre.IsChecked = s.CentreEachSide;
        _ppm.Value = (decimal)s.PixelsPerMm;
        _soften.Value = (decimal)s.SoftenMm;
        _cutLines.IsChecked = s.CutLines;
        _partD.Value = (decimal)Math.Clamp(Math.Round(s.PartDiameterMm, 2), 2, 119);
        _wrap.SelectedIndex = (int)s.Wrap;
        _rings.Value = _round.Value = _starts.Value = Math.Clamp(s.WrapCopies, 1, 60);
        _gap.Value = (decimal)Math.Max(0, s.WrapGapMm);
        _wrapLength.Value = (decimal)Math.Clamp(s.WrapLengthMm, 5, 1000);
        _mirrored.IsChecked = s.WrapMirrored;
        _turned.IsChecked = s.WrapTurned;
        _leftHand.IsChecked = s.WrapLeftHand;
        if (s.Layout == BorderLayout.Rotary && s.Wrap == WrapWay.Along) _width.Value = (decimal)Math.Round(s.Design.WidthMm, 2);
        _band = 0;
        _loading = false;
        ShowBands();
        ShowRows();
    }

    static double V(NumericUpDown n, double fallback) => n.Value is { } v ? (double)v : fallback;

    /// <summary>The design at the Border width and Depth set on the Size tab: the bands as they'll be made.</summary>
    BorderDesign Real() => Spec().Sized();

    WrapWay Way => (WrapWay)Math.Max(0, _wrap.SelectedIndex);

    BorderSpec Spec() => new()
    {
        Design = _design,
        Layout = _shape.SelectedIndex switch { 1 => BorderLayout.Oval, 2 => BorderLayout.Strip, 3 => BorderLayout.Rotary, _ => BorderLayout.Frame },
        PartDiameterMm = V(_partD, 25),
        Wrap = Way,
        WrapCopies = (int)V(Way switch { WrapWay.Along => _round, WrapWay.Spiral => _starts, _ => _rings }, 1),
        WrapGapMm = V(_gap, 0),
        WrapLengthMm = V(_wrapLength, 100),
        WrapMirrored = _mirrored.IsChecked == true,
        WrapTurned = _turned.IsChecked == true,
        WrapLeftHand = _leftHand.IsChecked == true,
        WidthMm = V(_width, _design.WidthMm),
        DepthMm = V(_depth, _design.DepthMm),
        LengthMm = V(_length, 300),
        MitredEnds = _mitredEnds.IsChecked == true,
        MaxPieceMm = V(_maxPiece, 0),
        FrameWidthMm = V(_fw, 180),
        FrameHeightMm = V(_fh, 220),
        SizeIsOpening = _sizeIs.SelectedIndex == 1,
        Corners = (CornerStyle)Math.Max(0, _corners.SelectedIndex),
        CornerRadiusMm = V(_radius, 30),
        FourPieces = _four.IsChecked == true,
        Middle = (MiddleFill)Math.Max(0, _middle.SelectedIndex),
        MotifOnCorners = _motifCorners.IsChecked == true,
        CentreEachSide = _centre.IsChecked == true,
        PixelsPerMm = V(_ppm, 10),
        SoftenMm = V(_soften, 0),
        CutLines = _cutLines.IsChecked == true,
    };

    void Hook()
    {
        foreach (var n in new[] { _fw, _fh, _radius, _length, _maxPiece, _width, _depth, _ppm, _soften, _gap, _wrapLength })
            n.ValueChanged += (_, _) => Changed();
        // These change how wide flutes round a part come out, so the bands are shown again.
        foreach (var n in new[] { _partD, _rings, _round, _starts })
            n.ValueChanged += (_, _) => { Changed(); if (!_loading && _shape.SelectedIndex == 3 && Way == WrapWay.Along) ShowBands(); };
        foreach (var c in new[] { _mirrored, _turned, _leftHand })
            c.IsCheckedChanged += (_, _) => { Changed(); if (!_loading && _shape.SelectedIndex == 3 && Way == WrapWay.Along) ShowBands(); };
        _wrap.SelectionChanged += (_, _) => { ShowRows(); Changed(); if (!_loading) ShowBands(); };
        _onPart.IsCheckedChanged += (_, _) => { _view.OnThePart = _onPart.IsChecked == true; ShowRows(); };
        // The bands are shown at the border's width and depth.
        foreach (var n in new[] { _width, _depth })
            n.ValueChanged += (_, _) => { if (!_loading) ShowBands(); };
        foreach (var c in new[] { _sizeIs, _corners, _middle })
            c.SelectionChanged += (_, _) => { ShowRows(); Changed(); };
        _shape.SelectionChanged += (_, _) => { ShowRows(); Changed(); if (!_loading) ShowBands(); };
        foreach (var c in new[] { _four, _mitredEnds, _motifCorners, _centre, _cutLines })
            c.IsCheckedChanged += (_, _) => Changed();
        _look.SelectionChanged += (_, _) => _view.Look = (PreviewLook)Math.Max(0, _look.SelectedIndex);
        _category.SelectionChanged += (_, _) => FillGallery();
        _gallery.SelectionChanged += (_, _) => Pick();
        _bands.SelectionChanged += (_, _) =>
        {
            if (_loading || _bands.SelectedIndex < 0) return;
            _band = _bands.SelectedIndex;
            ShowBand();
            _profile.Show(Real(), _band);
        };
        foreach (var n in new[] { _bw, _btop, _bdrop, _bcount, _bcarve, _brepeat })
            n.ValueChanged += (_, _) => BandEdited();
        _bprofile.SelectionChanged += (_, _) => BandEdited();
        _bornament.SelectionChanged += (_, _) => BandEdited();
        _bflip.IsCheckedChanged += (_, _) => BandEdited();
    }

    void ShowRows()
    {
        int shape = _shape.SelectedIndex;
        bool rotary = shape == 3;
        var way = Way;
        _frameRows.IsVisible = shape is 0 or 1;
        _cornerRow.IsVisible = shape == 0;
        _radiusRow.IsVisible = shape == 0 && _corners.SelectedIndex == 2;
        _four.IsVisible = shape == 0 && _corners.SelectedIndex == 0;
        _middleRow.IsVisible = shape is 0 or 1;
        _stripRows.IsVisible = shape == 2;
        _rotaryRows.IsVisible = rotary;
        _ringsRow.IsVisible = _gapRow.IsVisible = way == WrapWay.Round;
        _roundRow.IsVisible = way == WrapWay.Along;
        _startsRow.IsVisible = _leftHand.IsVisible = way == WrapWay.Spiral;
        _wrapLengthRow.IsVisible = way != WrapWay.Round;
        // Flutes are made as wide as fits round the part.
        _widthRow.IsEnabled = !(rotary && way == WrapWay.Along);
        _cutLines.IsVisible = !rotary;
        _onPart.IsVisible = rotary;
        _viewHint.Text = rotary && _onPart.IsChecked == true ? "Drag up and down to turn it, scroll to zoom" : "Scroll to zoom, drag to move";
        _motifCorners.Content = shape == 2 || rotary ? "A motif on each end" : "A motif on each corner";
        _motifCorners.IsVisible = shape == 2 || (shape == 0 && _corners.SelectedIndex == 0) || (rotary && way == WrapWay.Along);
        _centre.Content = rotary ? (way == WrapWay.Round ? "A whole motif facing the laser at the start" : "A whole motif in the middle")
            : shape == 2 ? "A whole motif in the middle" : shape == 0 && _corners.SelectedIndex != 2 ? "A whole motif in the middle of each side" : "A whole motif at the top, bottom and sides";
        _centre.IsVisible = !(rotary && way == WrapWay.Spiral);
    }

    void Changed()
    {
        if (_loading) return;
        _debounce.Stop();
        _debounce.Start();
    }

    // ---- the gallery ----

    sealed record GalleryItem(BorderDesign Design, Image Thumb);

    void FillGallery()
    {
        string cat = BorderPresets.Categories[Math.Max(0, _category.SelectedIndex)];
        var designs = cat == BorderPresets.MyDesigns ? _mine : BorderPresets.All.Where(d => d.Category == cat).ToList();
        _deleteMine.IsVisible = cat == BorderPresets.MyDesigns && _mine.Count > 0;
        var items = new List<Control>();
        var todo = new List<(BorderDesign, Image)>();
        foreach (var d in designs)
        {
            var img = new Image { Width = 150, Height = 46, Stretch = Stretch.Uniform };
            if (_thumbs.TryGetValue(Key(d), out var bmp)) img.Source = bmp; else todo.Add((d, img));
            var row = new Grid { ColumnDefinitions = new("150,*"), ColumnSpacing = 10, Tag = d };
            row.Children.Add(img);
            var text = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                Children = { new TextBlock { Text = d.Name, FontWeight = FontWeight.SemiBold }, new TextBlock { Text = $"{d.WidthMm:0.#} mm wide, {d.DepthMm:0.#} mm deep", Classes = { "muted" }, FontSize = 11 } },
            };
            ToolTip.SetTip(row, d.About);
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            items.Add(row);
        }
        _loading = true;
        _gallery.ItemsSource = items;
        int at = designs.FindIndex(d => d.Name == _design.Name && d.Category == _design.Category);
        _gallery.SelectedIndex = at;
        _loading = false;
        if (designs.Count == 0 && cat == BorderPresets.MyDesigns)
            _about.Text = "Nothing saved yet. Change a design (its bands, width or depth), give it a name below and click Save as my design.";
        else if (at >= 0) _about.Text = designs[at].About;
        _ = Thumbnails(todo);
    }

    static string Key(BorderDesign d) => d.Category + "|" + d.Name + "|" + BorderSpec.Write(d.Bands);

    /// <summary>Draws the gallery's little pictures in the background, a few at a time.</summary>
    async Task Thumbnails(List<(BorderDesign Design, Image Img)> todo)
    {
        _thumbCancel?.Cancel();
        var cancel = _thumbCancel = new CancellationTokenSource();
        foreach (var (d, img) in todo)
        {
            if (cancel.IsCancellationRequested) return;
            try
            {
                var bmp = await Task.Run(() => Thumbnail(d), cancel.Token);
                if (cancel.IsCancellationRequested) { bmp?.Dispose(); return; }
                if (bmp is null) continue;
                if (_thumbs.Remove(Key(d), out var old)) old.Dispose();
                _thumbs[Key(d)] = bmp;
                img.Source = bmp;
            }
            catch (OperationCanceledException) { return; }
        }
    }

    /// <summary>A short length of the design, lit, 300 × 92 pixels.</summary>
    static Bitmap? Thumbnail(BorderDesign d)
    {
        if (d.Bands.Count == 0 || !(d.WidthMm > 0)) return null;
        const int Cols = 300, Rows = 92;
        double mmPerPx = d.WidthMm / Rows;
        var spec = new BorderSpec { Design = d, Layout = BorderLayout.Strip, LengthMm = Cols * mmPerPx, CentreEachSide = false };
        if (spec.Problem() is not null) return null;
        var plan = new BorderPlan(spec);
        var h = plan.Render(plan.Pieces()[0], 0, 0, mmPerPx, Cols, Rows);
        return CameraImaging.ToAvaloniaRgba(Shading.Shade(h, Cols, Rows, mmPerPx, PreviewLook.Wood, plan.Top, plan.Lowest));
    }

    void Pick()
    {
        if (_loading || _gallery.SelectedItem is not Grid { Tag: BorderDesign d }) return;
        bool wasFrame = _shape.SelectedIndex is 0 or 1;
        _loading = true;
        _design = d;
        _width.Value = (decimal)Math.Round(d.WidthMm, 2);
        _depth.Value = (decimal)Math.Round(d.DepthMm, 2);
        // Frames go round; edges are strips. Bands and your own keep what you had, and so does a rotary part.
        if (_shape.SelectedIndex != 3)
        {
            if (d.Category == BorderPresets.Frames && !wasFrame) _shape.SelectedIndex = 0;
            if (d.Category == BorderPresets.Edges && wasFrame) _shape.SelectedIndex = 2;
        }
        _loading = false;
        _about.Text = d.About;
        _myName.Text = d.Category == BorderPresets.MyDesigns ? d.Name : "";
        _band = 0;
        ShowBands();
        ShowRows();
        Changed();
    }

    void SaveMine()
    {
        string name = (_myName.Text ?? "").Trim();
        if (name.Length == 0) { _myName.Focus(); _busy.Text = "Give your design a name first."; return; }
        // Kept at the width and depth it's shown at now.
        var sized = Spec().Sized() with { Name = name, Category = BorderPresets.MyDesigns, About = string.IsNullOrWhiteSpace(_design.About) ? "Your own design." : _design.About };
        _mine.RemoveAll(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));
        _mine.Add(sized);
        MyDesigns.Store(_dataFolder, _mine);
        _design = sized;
        _busy.Text = $"Saved \"{name}\" in My designs.";
        _category.SelectedIndex = BorderPresets.Categories.ToList().IndexOf(BorderPresets.MyDesigns);
        FillGallery();
    }

    void DeleteMine()
    {
        if (_gallery.SelectedItem is not Grid { Tag: BorderDesign d } || d.Category != BorderPresets.MyDesigns) return;
        _mine.RemoveAll(m => m.Name == d.Name);
        MyDesigns.Store(_dataFolder, _mine);
        _busy.Text = $"Deleted \"{d.Name}\" from My designs.";
        FillGallery();
    }

    // ---- bands ----

    static List<string> BandNames(BorderDesign d) => [.. d.Bands.Select((b, i) =>
    {
        string what = b.Ornament != Ornament.None ? $"{Profiles.Info(b.Profile).Name} · {Ornaments.Info(b.Ornament).Name}" : Profiles.Info(b.Profile).Name;
        return $"{i + 1}. {what} · {b.WidthMm:0.#} mm";
    })];

    void ShowBands()
    {
        var real = Real();
        bool was = _loading;
        _loading = true;
        _bands.ItemsSource = BandNames(real);
        _band = Math.Clamp(_band, 0, Math.Max(0, real.Bands.Count - 1));
        _bands.SelectedIndex = real.Bands.Count > 0 ? _band : -1;
        _loading = was;
        ShowBand();
        _profile.Show(real, _band);
    }

    void ShowBand()
    {
        _bandEditor.IsVisible = _band >= 0 && _band < _design.Bands.Count;
        if (!_bandEditor.IsVisible) return;
        var b = Real().Bands[_band];
        bool was = _loading;
        _loading = true;
        _bw.Value = (decimal)Math.Round(b.WidthMm, 2);
        _btop.Value = (decimal)Math.Round(b.TopMm, 2);
        _bprofile.SelectedIndex = Profiles.All.ToList().FindIndex(p => p.Kind == b.Profile);
        _bdrop.Value = (decimal)Math.Round(b.DropMm, 2);
        _bcount.Value = Math.Clamp(b.Count, 1, 12);
        _bornament.SelectedIndex = Ornaments.All.ToList().FindIndex(o => o.Kind == b.Ornament);
        _bcarve.Value = (decimal)Math.Round(b.CarveMm, 2);
        _brepeat.Value = (decimal)Math.Round(b.RepeatMm, 2);
        _bflip.IsChecked = b.Flip;
        _loading = was;
        ShowBandRows(b);
    }

    void ShowBandRows(BorderBand b)
    {
        _countRow.IsVisible = Profiles.Info(b.Profile).UsesCount || Ornaments.Info(b.Ornament).UsesCount;
        string profile = Profiles.Info(b.Profile).About, ornament = b.Ornament == Ornament.None ? "" : " " + Ornaments.Info(b.Ornament).About;
        _bandAbout.Text = profile + ornament;
    }

    /// <summary>Changes the design's bands; <paramref name="edit"/> returns the band to choose afterwards. The design becomes your own changed copy.</summary>
    void EditBands(Func<List<BorderBand>, int> edit)
    {
        var list = Real().Bands.ToList();
        _band = edit(list);
        Edited(list);
        ShowBands();
    }

    void BandEdited()
    {
        if (_loading || _band < 0 || _band >= _design.Bands.Count) return;
        var list = Real().Bands.ToList();
        var b = list[_band] with
        {
            WidthMm = V(_bw, list[_band].WidthMm),
            TopMm = V(_btop, list[_band].TopMm),
            Profile = Profiles.All[Math.Max(0, _bprofile.SelectedIndex)].Kind,
            DropMm = V(_bdrop, 0),
            Count = (int)V(_bcount, 3),
            Ornament = Ornaments.All[Math.Max(0, _bornament.SelectedIndex)].Kind,
            CarveMm = V(_bcarve, 0),
            RepeatMm = V(_brepeat, 0),
            Flip = _bflip.IsChecked == true,
        };
        // A new ornament on a band that had none gets something to carve.
        if (b.Ornament != Ornament.None && list[_band].Ornament == Ornament.None && b.CarveMm <= 0)
        {
            b = b with { CarveMm = Math.Round(Math.Max(0.3, 0.5 * b.TopMm), 1) };
            _loading = true;
            _bcarve.Value = (decimal)b.CarveMm;
            _loading = false;
        }
        list[_band] = b;
        Edited(list);
        ShowBandRows(b);
        // Keep the list's words up to date without losing the choice.
        _loading = true;
        _bands.ItemsSource = BandNames(_design);
        _bands.SelectedIndex = _band;
        _loading = false;
        _profile.Show(Real(), _band);
    }

    /// <summary>
    /// The bands changed (given at the size they're made): the design is now the person's own (unsaved),
    /// made of those bands, and the width and depth fields follow it.
    /// </summary>
    void Edited(List<BorderBand> bands)
    {
        string name = _design.Category == BorderPresets.MyDesigns || _design.Name.EndsWith(" (changed)") ? _design.Name : _design.Name + " (changed)";
        _design = _design with { Bands = bands, Name = name };
        _loading = true;
        if (!(_shape.SelectedIndex == 3 && Way == WrapWay.Along)) _width.Value = (decimal)Math.Round(Math.Max(1, _design.WidthMm), 2);
        _depth.Value = (decimal)Math.Round(Math.Max(0.05, _design.DepthMm), 2);
        _loading = false;
        if (_myName.Text is null or "") _myName.Text = name.Replace(" (changed)", " (mine)");
        Changed();
    }

    // ---- the preview and the summary ----

    void Rebuild()
    {
        var spec = Spec();
        _warning.Text = "";
        if (spec.Problem() is { } problem)
        {
            _plan = null;
            _view.Show(null, problem);
            _summary.Text = "";
            _make.IsEnabled = _save.IsEnabled = false;
            return;
        }
        var plan = new BorderPlan(spec);
        _plan = plan;
        _view.Show(plan);
        _make.IsEnabled = _save.IsEnabled = true;
        _summary.Text = Summary(plan);
        _warning.Text = Warnings(plan);
        _warning.IsVisible = _warning.Text.Length > 0;
    }

    string Summary(BorderPlan plan)
    {
        var s = plan.Spec;
        var lines = new List<string>();
        string depth = $"carved up to {plan.Top - plan.Lowest:0.##} mm deep";
        switch (s.Layout)
        {
            case BorderLayout.Rotary:
                lines.Add(RotarySummary(plan, depth));
                break;
            case BorderLayout.Strip:
                lines.Add($"{plan.Design.Name}: a strip {plan.SizeW:0.#} mm long and {plan.Width:0.#} mm wide, {depth}.");
                break;
            default:
                string what = s.Layout == BorderLayout.Oval ? (Math.Abs(plan.SizeW - plan.SizeH) < 0.01 ? "A round frame" : "An oval frame") : "A frame";
                lines.Add($"{plan.Design.Name}: {what.ToLowerInvariant()} {plan.SizeW:0.#} × {plan.SizeH:0.#} mm outside, the opening {plan.SizeW - 2 * plan.Width:0.#} × {plan.SizeH - 2 * plan.Width:0.#} mm, the border {plan.Width:0.#} mm wide, {depth}.");
                break;
        }
        foreach (var g in plan.Repeats().GroupBy(r => r.Band))
        {
            var band = plan.Design.Bands[g.Key];
            var runs = g.ToList();
            string name = Ornaments.Info(band.Ornament).Name;
            if (runs.Count == 4)
                lines.Add(runs[0].Repeats == runs[1].Repeats
                    ? $"{name}: {runs[0].Repeats} on each side, {runs[0].PitchMm:0.#} mm apart."
                    : $"{name}: {runs[0].Repeats} along the top and bottom ({runs[0].PitchMm:0.#} mm apart), {runs[1].Repeats} down each side ({runs[1].PitchMm:0.#} mm).");
            else if (s.Layout == BorderLayout.Rotary)
                lines.Add(s.Wrap switch
                {
                    WrapWay.Along => $"{name}: {runs[0].Repeats} along each copy, {runs[0].PitchMm:0.#} mm apart.",
                    WrapWay.Spiral => $"{name}: {runs[0].Repeats} each time round, {runs[0].PitchMm:0.#} mm apart along the spiral, so it carries on unbroken.",
                    _ => $"{name}: {runs[0].Repeats} round the part, {runs[0].PitchMm:0.#} mm apart, meeting itself round the back.",
                });
            else
                lines.Add($"{name}: {runs[0].Repeats} {(s.Layout == BorderLayout.Strip ? "along it" : "all the way round")}, {runs[0].PitchMm:0.#} mm apart.");
        }
        var pieces = plan.Pieces();
        var sizes = pieces.Select(p => { double ppm = BorderOutput.PixelsPerMm(s, p); return $"{Math.Round(p.WidthMm * ppm):0} × {Math.Round(p.HeightMm * ppm):0}"; }).Distinct().ToList();
        double used = pieces.Min(p => BorderOutput.PixelsPerMm(s, p));
        string px = used < s.PixelsPerMm - 0.01 ? $" (lowered to {used:0.#} per mm to stay within {BorderOutput.MaxSide} pixels a side)" : $" ({s.PixelsPerMm:0.#} per mm)";
        lines.Add(pieces.Count == 1
            ? $"Depth map: {sizes[0]} pixels{px}. White is the surface, black is {plan.Top - plan.Lowest:0.##} mm down."
            : $"{pieces.Count} pieces ({ListWords(pieces.Select(p => p.Name.ToLowerInvariant()))}), depth maps {string.Join(" and ", sizes)} pixels{px}. White is the surface, black is {plan.Top - plan.Lowest:0.##} mm down.");
        return string.Join("\n", lines);
    }

    /// <summary>What a rotary wrap is, in a sentence.</summary>
    static string RotarySummary(BorderPlan plan, string depth)
    {
        var s = plan.Spec;
        int k = Math.Max(1, s.WrapCopies);
        string part = $"a Ø {s.PartDiameterMm:0.##} mm part ({plan.SizeH:0.#} mm round)";
        string pair = s.WrapMirrored ? " (the design and its mirror image)" : "";
        return s.Wrap switch
        {
            WrapWay.Along => $"{plan.Design.Name}: {k} cop{(k == 1 ? "y" : "ies")}{pair} side by side round {part}, each {plan.Width * (s.WrapMirrored ? 2 : 1):0.##} mm wide to fit, {plan.SizeW:0.#} mm along it, {depth}.",
            WrapWay.Spiral => $"{plan.Design.Name}: a {(k == 1 ? "single" : $"{k}-start")} {(s.WrapLeftHand ? "left" : "right")}-handed spiral{pair} round {part}, {plan.Width * (s.WrapMirrored ? 2 : 1):0.#} mm across each band, one turn every {plan.SpiralLeadMm:0.#} mm along it ({Math.Atan2(plan.SizeH, plan.SpiralLeadMm) * 180 / Math.PI:0}° to the part's length), {plan.SizeW:0.#} mm long, {depth}.",
            _ => $"{plan.Design.Name}: {(k == 1 ? "a ring" : $"{k} rings")}{pair} round {part}, {plan.Width * (s.WrapMirrored ? 2 : 1):0.#} mm wide{(k > 1 ? $" with {s.WrapGapMm:0.#} mm between" : "")}, {plan.SizeW:0.#} mm along it, {depth}.",
        } + " The picture is the part unrolled, once round: it goes in the design centred on the rotary start line.";
    }

    string Warnings(BorderPlan plan)
    {
        var s = plan.Spec;
        var w = new List<string>();
        double fw = _machine.FieldW, fh = _machine.FieldH;
        var pieces = plan.Pieces();
        bool fits = pieces.All(p => (p.WidthMm <= fw && p.HeightMm <= fh) || (p.WidthMm <= fh && p.HeightMm <= fw));
        if (s.Layout == BorderLayout.Rotary)
        {
            fits = true;
            if (plan.SizeW > fw)
                w.Add($"It's {plan.SizeW:0} mm along the part, more than the laser reaches across ({fw:0} mm): make it shorter (or fewer rings).");
            if (_workModeD is not { } wd)
                w.Add($"Machine → Work mode isn't the rotary: set it to Rotary with Object Ø {s.PartDiameterMm:0.##} mm before you send it.");
            else if (Math.Abs(Math.PI * wd - plan.SizeH) > RotaryWrapCheck.ToleranceMm)
                w.Add($"Machine → Work mode's Object Ø is {wd:0.##} mm, but this is made for Ø {s.PartDiameterMm:0.##} mm: click Use Work mode's (or change Object Ø), or the pattern won't meet itself round the back.");
        }
        if (!fits)
        {
            string fix = s.Layout switch
            {
                BorderLayout.Strip => " Set a Longest piece to make it in lengths that fit, carved one after another.",
                BorderLayout.Frame when s.Corners == CornerStyle.Mitred && !s.FourPieces => " Tick Four separate mitred sticks to carve it a side at a time.",
                _ => "",
            };
            w.Add($"It's bigger than the work area ({fw:0} × {fh:0} mm).{fix}");
        }
        foreach (var b in plan.Design.Bands)
            if (b.Ornament != Ornament.None && b.WidthMm < 3)
            {
                w.Add($"The {Ornaments.Info(b.Ornament).Name.ToLowerInvariant()} band is only {b.WidthMm:0.#} mm wide: fine detail that small may not carve cleanly.");
                break;
            }
        return string.Join("\n", w);
    }

    // ---- making it ----

    async Task<List<(BorderPiece Piece, RasterImage Map)>?> Maps(BorderPlan plan)
    {
        _busy.Text = "Making the depth map…";
        _make.IsEnabled = _save.IsEnabled = false;
        try
        {
            return await Task.Run(() => plan.Pieces().Select(p => (p, BorderOutput.DepthMap(plan, p))).ToList());
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
        var maps = await Maps(plan);
        if (maps is null) return;
        var state = _doc.State;
        Vec2? at = null;
        Layer? layer = null;
        if (_editing is { } ed)
        {
            state = Ops.Delete(state, ed.Ids);
            at = ed.TopLeft;
            layer = state.Layers.FirstOrDefault(l => l.Id == ed.LayerId);
        }
        var (next, ids, layerId) = BorderOutput.Add(state, plan, maps, _machine, at, layer);
        _doc.Commit(_editing is null ? "Border" : "Change border", next, ids);
        MadeOnLayer = layerId;
        LastUsed.Store(_dataFolder, plan.Spec);
        _status?.Invoke(plan.Spec.Layout == BorderLayout.Rotary
            ? $"Rotary wrap: {plan.Design.Name}, {plan.SizeW:0.#} mm along a Ø {plan.Spec.PartDiameterMm:0.##} mm part, on the layer \"{next.Layers.First(l => l.Id == layerId).Name}\" (depth slices). Machine → Work mode: Rotary, Object Ø {plan.Spec.PartDiameterMm:0.##} mm."
            : $"Border: {plan.Design.Name}, {plan.SizeW:0.#} × {plan.SizeH:0.#} mm, on the layer \"{next.Layers.First(l => l.Id == layerId).Name}\" (depth slices).");
        Close();
    }

    async Task SaveFile()
    {
        Rebuild();
        if (_plan is not { } plan) return;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save depth map",
            SuggestedFileName = $"{plan.Design.Name.Replace(" (changed)", "")} depth map.png",
            DefaultExtension = "png",
            FileTypeChoices = [new FilePickerFileType("PNG picture") { Patterns = ["*.png"] }],
        });
        if (file?.TryGetLocalPath() is not { } path) return;
        var maps = await Maps(plan);
        if (maps is null) return;
        try
        {
            var written = new List<string>();
            foreach (var (piece, map) in maps)
            {
                string target = maps.Count == 1 ? path
                    : Path.Combine(Path.GetDirectoryName(path) ?? "", $"{Path.GetFileNameWithoutExtension(path)} - {piece.Name.ToLowerInvariant()}.png");
                await File.WriteAllBytesAsync(target, map.EncodePng());
                written.Add(Path.GetFileName(target));
            }
            LastUsed.Store(_dataFolder, plan.Spec);
            _busy.Text = $"Saved {ListWords(written)}.";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _busy.Text = $"Couldn't save it: {e.Message}"; }
    }
}

/// <summary>The settings last used, kept in the add-on's data folder.</summary>
static class LastUsed
{
    const string FileName = "last.json";

    public static BorderSpec? Load(string? folder)
    {
        if (folder is null) return null;
        try
        {
            var path = Path.Combine(folder, FileName);
            return File.Exists(path) ? BorderSpec.FromJson(File.ReadAllText(path)) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    public static void Store(string? folder, BorderSpec spec)
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

/// <summary>The designs the person saved, kept in the add-on's data folder.</summary>
static class MyDesigns
{
    const string FileName = "my-designs.json";

    public static List<BorderDesign> Load(string? folder)
    {
        if (folder is null) return [];
        try
        {
            var path = Path.Combine(folder, FileName);
            if (!File.Exists(path)) return [];
            return BorderSpec.Read<List<BorderDesign>>(File.ReadAllText(path)) ?? [];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return []; }
    }

    public static void Store(string? folder, List<BorderDesign> designs)
    {
        if (folder is null) return;
        try
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, FileName), BorderSpec.Write(designs));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* the designs stay for this session */ }
    }
}
