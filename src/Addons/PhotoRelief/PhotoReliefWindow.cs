using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using PhotonCanvas.App;
using PhotonCanvas.Core;
using PhotonCanvas.Core.Imaging;
using PhotonCanvas.Doc;
using static PhotonCanvas.App.UiKit;

namespace PhotonCanvas.Addon.PhotoRelief;

/// <summary>
/// Photo to relief: pick a photo (the one selected, or a file), a style, what the subject is, the
/// plaque and how it's carved, and see it lit as carved wood or metal; then put its depth map in the
/// design on a depth-slices layer, or save it as a picture.
/// </summary>
public sealed class PhotoReliefWindow : Window
{
    readonly Document _doc;
    readonly MachineProfile _machine;
    readonly string? _dataFolder;
    readonly Action<string>? _status;
    readonly (ReliefSpec Spec, ReliefPhoto Photo, HashSet<Guid> Ids, Vec2 Centre, Guid LayerId)? _editing;
    readonly ImageShape? _fromPicture;
    ReliefPhoto? _photo;

    // Photo
    readonly TextBlock _photoName = new() { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    readonly ComboBox _style = Combo([.. ReliefStyles.All.Select(s => s.Name)]);
    readonly TextBlock _styleAbout = Hint();
    readonly ComboBox _high = Combo("Lighter parts", "Darker parts");
    readonly ComboBox _subject = Combo("The whole photo", "All but a plain background", "Its see-through parts");
    readonly NumericUpDown _tolerance = Number(0, 100, 2, "0");
    readonly ToggleButton _pick = new() { Content = "Click the background", HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
    readonly Button _forget = Button("Forget the clicks");
    readonly ComboBox _background = Combo("Carved down", "Not carved (sunk)");
    readonly NumericUpDown _backDepth = Number(0, 100, 5, "0");
    Control _plainRows = null!, _backDepthRow = null!, _backgroundRow = null!;

    // Shape
    readonly ComboBox _shape = Combo("No plaque (just the picture)", "Rectangle", "Rounded rectangle", "Oval", "Circle");
    readonly NumericUpDown _width = Number(5, 3000, 5, "0.#"), _height = Number(5, 3000, 5, "0.#");
    readonly CheckBox _keep = new() { Content = "Keep the photo's shape", IsChecked = true };
    readonly NumericUpDown _corner = Number(0, 500, 1, "0.#"), _rim = Number(0, 100, 0.5m, "0.#");
    readonly NumericUpDown _zoom = Number(100, 800, 10, "0");
    readonly Button _recentre = Button("Centre the photo again", tip: "The whole photo, centred, as it was before it was moved or zoomed.");
    Control _heightRow = null!, _cornerRow = null!, _rimRow = null!;
    readonly TextBlock _widthLabel = new() { Text = "Width (mm)", VerticalAlignment = VerticalAlignment.Center, Classes = { "muted" } };

    // Carving
    readonly NumericUpDown _detail = Number(0, 100, 5, "0"), _form = Number(0, 100, 5, "0"), _even = Number(0, 100, 5, "0"), _smooth = Number(0, 100, 5, "0");
    readonly NumericUpDown _raise = Number(0, 100, 5, "0"), _rounding = Number(0, 200, 1, "0.#"), _depth = Number(0.1m, 30, 0.1m, "0.##");

    // Output
    readonly NumericUpDown _ppm = Number(1, 40, 1, "0.#");
    readonly CheckBox _cutLine = new() { Content = "A cut line round the plaque" };
    readonly CheckBox _replace = new() { Content = "Replace the photo with its relief", IsChecked = true };

    // Preview and results
    readonly ReliefView _view = new();
    readonly ComboBox _look = Combo("Wood", "Metal", "Depth map", "Photo");
    readonly TextBlock _summary = new() { TextWrapping = TextWrapping.Wrap };
    readonly TextBlock _warning = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brush("Warn") };
    readonly TextBlock _busy = new() { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Classes = { "muted" } };
    readonly Button _make = new() { Content = "Put in the design", Classes = { "primary" }, MinWidth = 150 };
    readonly Button _save = new() { Content = "Save depth map…", MinWidth = 130 };
    readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(160) };

    double _panX, _panY;
    List<PhotoPoint> _picks = [];
    bool _loading;
    CancellationTokenSource? _cancel;
    int _generation;

    /// <summary>The layer the relief was put on (null = it wasn't put in the design).</summary>
    public Guid? MadeOnLayer { get; private set; }

    public PhotoReliefWindow(Document doc, MachineProfile machine, string? dataFolder, Action<string>? status = null,
        ImageShape? fromPicture = null, (ReliefSpec Spec, ReliefPhoto Photo, HashSet<Guid> Ids, Vec2 Centre, Guid LayerId)? editing = null)
    {
        _doc = doc;
        _machine = machine;
        _dataFolder = dataFolder;
        _status = status;
        _editing = editing;
        _fromPicture = editing is null ? fromPicture : null;
        Title = "Photo to relief";
        Width = 1180;
        Height = 800;
        MinWidth = 940;
        MinHeight = 620;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");

        if (editing is { } ed) _photo = ed.Photo;
        else if (fromPicture is not null) _photo = new ReliefPhoto(fromPicture.Source.Pixels, fromPicture.Name);
        var spec = editing?.Spec ?? LastUsed.Load(dataFolder) ?? ReliefStyles.All[0].Apply(new ReliefSpec());
        // A new photo starts centred, with nothing clicked on it.
        if (editing is null) spec = spec with { Zoom = 1, PanX = 0, PanY = 0, Picks = [] };

        var tabs = new TabControl
        {
            Items =
            {
                Tab("Photo", PhotoTab()),
                Tab("Shape", ShapeTab()),
                Tab("Carving", CarvingTab()),
                Tab("Output", OutputTab()),
            },
        };
        var left = new Border { Width = 420, Padding = new Thickness(14, 10, 8, 0), Child = tabs };

        var bar = new Grid { ColumnDefinitions = new("Auto,140,*"), ColumnSpacing = 8, Margin = new Thickness(0, 0, 0, 6) };
        bar.Children.Add(new TextBlock { Text = "Show as", VerticalAlignment = VerticalAlignment.Center, Classes = { "muted" } });
        Grid.SetColumn(_look, 1);
        bar.Children.Add(_look);
        var hint = Muted("Drag to move the photo, scroll to zoom it");
        hint.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(hint, 2);
        bar.Children.Add(hint);

        var results = new StackPanel { Spacing = 6, Children = { _summary, _warning } };
        var right = new Grid { RowDefinitions = new("Auto,*,Auto"), Margin = new Thickness(0, 14, 14, 0) };
        right.Children.Add(bar);
        var frame = new Border { CornerRadius = new(8), ClipToBounds = true, BorderBrush = Brush("Line"), BorderThickness = new(1), Child = _view };
        Grid.SetRow(frame, 1);
        right.Children.Add(frame);
        var scroll = new ScrollViewer { Content = results, MaxHeight = 140, Margin = new Thickness(0, 8, 0, 0), HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
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
        _debounce.Tick += (_, _) => { _debounce.Stop(); _ = Rebuild(); };
        _view.Dragged += Drag;
        _view.Zoomed += ZoomBy;
        _view.Picked += Pick;
        _view.SizeChanged += (_, _) => Changed();
        Closed += (_, _) => { _debounce.Stop(); _cancel?.Cancel(); };

        Load(spec);
        Hook();
        ShowPhotoName();
        _ = Rebuild();
    }

    /// <summary>Runs a button's work, saying what went wrong instead of letting it take the program down.</summary>
    async Task Safely(Func<Task> work)
    {
        try { await work(); }
        catch (Exception e)
        {
            _busy.Text = $"Couldn't do it: {e.Message}";
            _make.IsEnabled = _save.IsEnabled = _photo is not null;
        }
    }

    static TabItem Tab(string header, Control content) => new()
    {
        Header = header,
        Content = new ScrollViewer { Content = content, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 8, 8, 8) },
    };

    static ComboBox Combo(params string[] items) => new() { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = items, SelectedIndex = 0 };

    static Control F(string label, Control editor, string? tip = null) => Field(label, editor, null, tip);

    static double V(NumericUpDown n, double fallback) => n.Value is { } v ? (double)v : fallback;

    // ---- the tabs ----

    Control PhotoTab()
    {
        var open = Button("Open photo…", tip: "A photo or picture file (JPEG, PNG, BMP, WebP…). A portrait or a pet against a plain wall or sheet works best.");
        open.Click += async (_, _) => await Safely(OpenPhoto);
        _plainRows = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                F("Tolerance", _tolerance, "How different a colour may be from the background's and still count as background. Raise it if bits of background are left; lower it if the subject's edges are eaten into."),
                Row(_pick, _forget),
                Hint("Background left behind (or a busy one)? Switch on Click the background and click it on the picture: its colour counts as background too."),
            },
        };
        ToolTip.SetTip(_pick, "While this is on, a click on the picture marks that place as background (switch Show as to Photo to see what you're clicking).");
        _backgroundRow = F("Background", _background, "Carved down: a flat field round the subject, so the subject stands up out of it. Not carved: the background stays as the surface and the subject is carved down into it, rounded inside its outline (a sunk relief).");
        _backDepthRow = F("Field depth (%)", _backDepth, "How far down the background field is, as a share of the whole depth: 100 carves it to the bottom.");
        return new StackPanel
        {
            Spacing = 8,
            Children =
            {
                Row(open, _photoName),
                F("Style", _style, "A starting point: picking one sets everything on the Photo, Shape and Carving tabs except the size and where the photo sits."),
                _styleAbout,
                F("Higher is", _high, "Which parts of the photo stand up: usually the lighter ones (a lit face, a pale pet). Pick Darker parts for a dark subject on a light background."),
                F("The subject is", _subject, "What's raised and rounded. All but a plain background finds the subject against a plain wall, sheet or sky; the whole photo makes a relief of everything; see-through parts uses a photo whose background is already removed (Remove background on the Picture tab)."),
                _plainRows,
                _backgroundRow,
                _backDepthRow,
            },
        };
    }

    Control ShapeTab()
    {
        _heightRow = F("Height (mm)", _height);
        _cornerRow = F("Corner radius (mm)", _corner);
        _rimRow = F("Rim (mm)", _rim, "A raised rim this wide round the plaque's edge, flat on top and rounded down to the field. 0 = none.");
        var widthRow = new Grid { ColumnDefinitions = new("110,*") };
        widthRow.Children.Add(_widthLabel);
        Grid.SetColumn(_width, 1);
        widthRow.Children.Add(_width);
        _recentre.Click += (_, _) =>
        {
            _panX = _panY = 0;
            _loading = true;
            _zoom.Value = 100;
            _loading = false;
            Changed();
        };
        return new StackPanel
        {
            Spacing = 8,
            Children =
            {
                F("Plaque", _shape, "The shape it's carved on. Outside an oval, circle or rounded plaque nothing is carved; tick the cut line on the Output tab to cut it out."),
                widthRow,
                _keep,
                _heightRow,
                _cornerRow,
                _rimRow,
                F("Photo zoom (%)", _zoom, "100 = the photo just fills the plaque. More crops in closer, for example on a face."),
                _recentre,
                Hint("Drag the picture on the right to move the photo on the plaque, and scroll on it to zoom."),
            },
        };
    }

    Control CarvingTab() => new StackPanel
    {
        Spacing = 8,
        Children =
        {
            F("Fine detail", _detail, "Hair, fur, wrinkles, texture (0–100). 50 is as in the photo; more makes it crisper, less makes it softer."),
            F("Broad shading", _form, "The big light and dark areas (0–100). 50 is as in the photo. Lower it when one side of a face is in shadow, so it doesn't carve lopsided."),
            F("Even out", _even, "Big jumps between light and dark (dark hair on a pale face, a shadow) are flattened so they don't carve as cliffs or pits, while small detail is kept (0–100)."),
            F("Smooth", _smooth, "Smooths skin, film grain and noise but keeps the edges (0–100)."),
            F("Raise the subject", _raise, "How much of the depth goes to raising the subject as one rounded shape, like a cameo (0–100). The rest is its detail."),
            F("Rounding (mm)", _rounding, "How far in from the subject's outline it takes to round up to full height. Big = a soft pillow; small = a crisp, steep edge; 0 = a flat-topped step."),
            F("Depth (mm)", _depth, "How deep you mean to carve the deepest part: for the preview and the summary. The depth-slices layer's slices, power and passes set how deep it really goes."),
            Hint("The depth map is grey: white is the surface and black the deepest. The layer it goes on is set to Depth slices (relief)."),
        },
    };

    Control OutputTab() => new StackPanel
    {
        Spacing = 8,
        Children =
        {
            F("Pixels per mm", _ppm, "The depth map's detail. 10 (254 dpi) is plenty for most reliefs; more makes a bigger file and a slower job."),
            _cutLine,
            _replace,
            Hint("Put in the design makes a new Picture layer set to Depth slices (relief), with the depth map on it. The photo is kept with it, so Edit photo relief… (right-click it) can change it later."),
        },
    };

    // ---- the fields ----

    void Load(ReliefSpec s)
    {
        _loading = true;
        int st = ReliefStyles.All.ToList().FindIndex(x => x.Name == s.Style);
        _style.SelectedIndex = Math.Max(0, st);
        _styleAbout.Text = ReliefStyles.All[Math.Max(0, st)].About;
        _high.SelectedIndex = s.DarkIsHigh ? 1 : 0;
        _subject.SelectedIndex = (int)s.Subject;
        _tolerance.Value = (decimal)Math.Clamp(s.Tolerance, 0, 100);
        _picks = [.. s.Picks];
        _background.SelectedIndex = (int)s.Background;
        _backDepth.Value = (decimal)Math.Clamp(s.BackgroundDepth, 0, 100);
        _shape.SelectedIndex = (int)s.Shape;
        _width.Value = (decimal)Math.Clamp(s.WidthMm, 5, 3000);
        _height.Value = (decimal)Math.Clamp(s.HeightMm, 5, 3000);
        _keep.IsChecked = s.KeepPhotoShape;
        _corner.Value = (decimal)Math.Clamp(s.CornerMm, 0, 500);
        _rim.Value = (decimal)Math.Clamp(s.RimMm, 0, 100);
        _zoom.Value = (decimal)Math.Round(Math.Clamp(s.Zoom, 1, 8) * 100);
        _panX = s.PanX;
        _panY = s.PanY;
        SetCarving(s);
        _ppm.Value = (decimal)Math.Clamp(s.PixelsPerMm, 1, 40);
        _cutLine.IsChecked = s.CutLine;
        _loading = false;
        ShowRows();
    }

    void SetCarving(ReliefSpec s)
    {
        _detail.Value = (decimal)Math.Clamp(s.Detail, 0, 100);
        _form.Value = (decimal)Math.Clamp(s.Form, 0, 100);
        _even.Value = (decimal)Math.Clamp(s.EvenOut, 0, 100);
        _smooth.Value = (decimal)Math.Clamp(s.Smooth, 0, 100);
        _raise.Value = (decimal)Math.Clamp(s.Raise, 0, 100);
        _rounding.Value = (decimal)Math.Clamp(s.RoundingMm, 0, 200);
        _depth.Value = (decimal)Math.Clamp(s.DepthMm, 0.1, 30);
    }

    /// <summary>The spec the fields say.</summary>
    ReliefSpec Spec() => new()
    {
        Style = ReliefStyles.All[Math.Max(0, _style.SelectedIndex)].Name,
        DarkIsHigh = _high.SelectedIndex == 1,
        Subject = (SubjectFrom)Math.Max(0, _subject.SelectedIndex),
        Tolerance = V(_tolerance, 22),
        Picks = [.. _picks],
        Background = (BackgroundStyle)Math.Max(0, _background.SelectedIndex),
        BackgroundDepth = V(_backDepth, 70),
        Shape = (PlaqueShape)Math.Max(0, _shape.SelectedIndex),
        WidthMm = V(_width, 100),
        HeightMm = V(_height, 130),
        KeepPhotoShape = _keep.IsChecked == true,
        CornerMm = V(_corner, 10),
        RimMm = V(_rim, 0),
        Zoom = V(_zoom, 100) / 100,
        PanX = _panX,
        PanY = _panY,
        Detail = V(_detail, 55),
        Form = V(_form, 35),
        EvenOut = V(_even, 70),
        Smooth = V(_smooth, 40),
        Raise = V(_raise, 50),
        RoundingMm = V(_rounding, 20),
        DepthMm = V(_depth, 3),
        PixelsPerMm = V(_ppm, 10),
        CutLine = _cutLine.IsChecked == true,
    };

    void Hook()
    {
        foreach (var c in new[] { _high, _subject, _background, _shape, _look })
            c.SelectionChanged += (_, _) => { ShowRows(); Changed(); };
        foreach (var n in new[] { _tolerance, _backDepth, _width, _height, _corner, _rim, _zoom, _detail, _form, _even, _smooth, _raise, _rounding, _depth, _ppm })
            n.ValueChanged += (_, _) => Changed();
        foreach (var b in new[] { _keep, _cutLine })
            b.IsCheckedChanged += (_, _) => { ShowRows(); Changed(); };
        _style.SelectionChanged += (_, _) =>
        {
            if (_loading || _style.SelectedIndex < 0) return;
            var picked = ReliefStyles.All[_style.SelectedIndex];
            Load(picked.Apply(Spec()) with { Style = picked.Name });
            Changed();
        };
        _pick.IsCheckedChanged += (_, _) =>
        {
            _view.Picking = _pick.IsChecked == true;
            // Clicking the background is easiest on the photo itself.
            if (_view.Picking && _look.SelectedIndex != 3) _look.SelectedIndex = 3;
        };
        _forget.Click += (_, _) => { _picks.Clear(); ShowRows(); Changed(); };
    }

    void ShowRows()
    {
        var subject = (SubjectFrom)Math.Max(0, _subject.SelectedIndex);
        _plainRows.IsVisible = subject == SubjectFrom.PlainBackground;
        _backgroundRow.IsVisible = subject != SubjectFrom.WholePicture;
        _backDepthRow.IsVisible = subject != SubjectFrom.WholePicture && _background.SelectedIndex == 0;
        _forget.IsEnabled = _picks.Count > 0;
        _forget.Content = _picks.Count > 0 ? $"Forget the clicks ({_picks.Count})" : "Forget the clicks";
        var shape = (PlaqueShape)Math.Max(0, _shape.SelectedIndex);
        bool circle = shape == PlaqueShape.Circle;
        _widthLabel.Text = circle ? "Diameter (mm)" : "Width (mm)";
        _keep.IsVisible = !circle;
        _heightRow.IsVisible = !circle && _keep.IsChecked != true;
        _cornerRow.IsVisible = shape == PlaqueShape.Rounded;
        _rimRow.IsVisible = shape != PlaqueShape.None;
        _cutLine.IsVisible = shape != PlaqueShape.None;
        _replace.IsVisible = _fromPicture is not null;
        _styleAbout.Text = ReliefStyles.All[Math.Max(0, _style.SelectedIndex)].About;
    }

    void Changed()
    {
        if (_loading) return;
        _debounce.Stop();
        _debounce.Start();
    }

    void ShowPhotoName() => _photoName.Text = _photo is null ? "No photo yet" : $"{_photo.Name} ({_photo.Width} × {_photo.Height})";

    // ---- moving the photo, clicking the background ----

    void Drag(double dxMm, double dyMm)
    {
        if (_photo is null) return;
        var spec = Spec();
        var (wMm, hMm) = spec.Size(_photo.Width, _photo.Height);
        var (_, _, ww, wh) = PhotoDepth.Window(spec, _photo);
        double slackX = _photo.Width - ww, slackY = _photo.Height - wh;
        // Dragging the picture right shows more of the photo's left.
        if (slackX > 0.5) _panX = Math.Clamp(_panX - 2 * dxMm * ww / wMm / slackX, -1, 1);
        if (slackY > 0.5) _panY = Math.Clamp(_panY - 2 * dyMm * wh / hMm / slackY, -1, 1);
        Changed();
    }

    void ZoomBy(double factor)
    {
        double z = Math.Clamp(V(_zoom, 100) * factor, 100, 800);
        _zoom.Value = (decimal)Math.Round(z);
    }

    void Pick(double fx, double fy)
    {
        if (_photo is null) return;
        var (wx, wy, ww, wh) = PhotoDepth.Window(Spec(), _photo);
        _picks.Add(new PhotoPoint((wx + fx * ww) / _photo.Width, (wy + fy * wh) / _photo.Height));
        if (_subject.SelectedIndex != (int)SubjectFrom.PlainBackground) _subject.SelectedIndex = (int)SubjectFrom.PlainBackground;
        ShowRows();
        Changed();
    }

    async Task OpenPhoto()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Photo",
            FileTypeFilter = [new FilePickerFileType("Photos and pictures") { Patterns = ["*.jpg", "*.jpeg", "*.png", "*.bmp", "*.webp", "*.gif"] }],
        });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path) return;
        try
        {
            var bytes = await File.ReadAllBytesAsync(path);
            var img = await Task.Run(() => RasterImage.Decode(bytes));
            _photo = new ReliefPhoto(img, Path.GetFileNameWithoutExtension(path));
            _panX = _panY = 0;
            _picks.Clear();
            _loading = true;
            _zoom.Value = 100;
            _loading = false;
            ShowPhotoName();
            ShowRows();
            await Rebuild();
        }
        catch (Exception e) when (e is IOException or FormatException or UnauthorizedAccessException) { _busy.Text = $"Couldn't open it: {e.Message}"; }
    }

    // ---- the preview and the summary ----

    /// <summary>Remakes the preview at the view's size (the last one asked for wins).</summary>
    public async Task Rebuild()
    {
        _warning.Text = "";
        _warning.IsVisible = false;
        if (_photo is not { } photo)
        {
            _view.Show(null, "Open a photo to start (or select a picture in your design first, then open this).");
            _summary.Text = "";
            _make.IsEnabled = _save.IsEnabled = false;
            return;
        }
        var spec = Spec();
        if (spec.Problem(photo.Width, photo.Height) is { } problem)
        {
            _view.Show(null, problem);
            _summary.Text = "";
            _make.IsEnabled = _save.IsEnabled = false;
            return;
        }
        _make.IsEnabled = _save.IsEnabled = true;
        var (wMm, hMm) = spec.Size(photo.Width, photo.Height);
        _view.PlaqueMm = new Size(wMm, hMm);
        double dpr = RenderScaling;
        double room = Math.Max(_view.Bounds.Width, 400) * dpr, roomH = Math.Max(_view.Bounds.Height, 400) * dpr;
        double k = Math.Min(Math.Min(room / wMm, roomH / hMm), Math.Min(900 / Math.Max(wMm, hMm), ReliefOutput.PixelsPerMm(spec, wMm, hMm)));
        int cols = Math.Max(16, (int)Math.Round(wMm * k)), rows = Math.Max(16, (int)Math.Round(hMm * k));
        var look = (ReliefLook)Math.Max(0, _look.SelectedIndex);
        _cancel?.Cancel();
        var cancel = _cancel = new CancellationTokenSource();
        int gen = ++_generation;
        try
        {
            var (result, picture) = await Task.Run(() =>
            {
                var r = PhotoDepth.Make(spec, photo, cols, rows, cancel.Token);
                var img = look == ReliefLook.Photo ? ReliefShading.Photo(spec, photo, r) : ReliefShading.Shade(r, look, spec.DepthMm);
                return (r, img);
            }, cancel.Token);
            if (gen != _generation) return;
            _view.Show(CameraImaging.ToAvaloniaRgba(picture));
            _summary.Text = Summary(spec, photo, result);
            _warning.Text = Warnings(spec, photo, result);
            _warning.IsVisible = _warning.Text.Length > 0;
        }
        catch (OperationCanceledException) { }
        catch (Exception e) when (e is OutOfMemoryException or ArgumentException or InvalidOperationException or IndexOutOfRangeException)
        {
            if (gen == _generation) _view.Show(null, $"Couldn't make it: {e.Message}");
        }
    }

    string Summary(ReliefSpec s, ReliefPhoto photo, ReliefResult r)
    {
        var lines = new List<string>();
        string what = s.Shape switch
        {
            PlaqueShape.Rectangle => "A rectangular plaque",
            PlaqueShape.Rounded => "A rounded plaque",
            PlaqueShape.Oval => "An oval plaque",
            PlaqueShape.Circle => "A round plaque",
            _ => "A relief",
        };
        string size = s.Shape == PlaqueShape.Circle ? $"{r.WidthMm:0.#} mm across" : $"{r.WidthMm:0.#} × {r.HeightMm:0.#} mm";
        string rim = s.HasPlaque && s.RimMm > 0 ? $", with a {s.RimMm:0.#} mm rim" : "";
        lines.Add($"{what} {size}{rim}, carved up to {s.DepthMm:0.##} mm deep.");
        if (s.Subject != SubjectFrom.WholePicture && !r.BackgroundNotFound)
        {
            string back = s.Background == BackgroundStyle.NotCarved
                ? "carved down into the surface; the background isn't carved"
                : $"standing up from a field {s.BackgroundDepth / 100 * s.DepthMm:0.##} mm down";
            lines.Add($"The subject covers {r.SubjectShare:P0} of it, {back}.");
        }
        var (cols, rows) = ReliefOutput.Pixels(s, photo);
        double ppm = ReliefOutput.PixelsPerMm(s, r.WidthMm, r.HeightMm);
        string px = ppm < s.PixelsPerMm - 0.01 ? $" (lowered to {ppm:0.#} per mm to stay within {ReliefOutput.MaxSide} pixels a side)" : $" ({ppm:0.#} per mm)";
        lines.Add($"Depth map: {cols} × {rows} pixels{px}. White is the surface, black is {s.DepthMm:0.##} mm down.");
        return string.Join("\n", lines);
    }

    string Warnings(ReliefSpec s, ReliefPhoto photo, ReliefResult r)
    {
        var w = new List<string>();
        double fw = _machine.FieldW, fh = _machine.FieldH;
        if (!((r.WidthMm <= fw && r.HeightMm <= fh) || (r.WidthMm <= fh && r.HeightMm <= fw)))
            w.Add($"It's bigger than the work area ({fw:0} × {fh:0} mm): make it smaller.");
        if (r.BackgroundNotFound)
            w.Add("No plain background was found round the edges, so the whole photo is carved. Click the background on the picture, or raise the Tolerance.");
        else if (r.BusyBackground && s.Picks.Count == 0)
            w.Add("The background isn't plain, so the subject found may be wrong: look at it with Show as Photo or Depth map. Click the background to help, or choose The whole photo.");
        var (wx, wy, ww, wh) = PhotoDepth.Window(s, photo);
        double photoPerMm = ww / r.WidthMm;
        if (photoPerMm < 3) w.Add($"The photo has only {photoPerMm:0.#} pixels per mm here: fine detail will be soft. A bigger photo, or a smaller relief, carves crisper.");
        return string.Join("\n", w);
    }

    // ---- making it ----

    async Task<ReliefResult?> Full(ReliefSpec spec, ReliefPhoto photo)
    {
        _busy.Text = "Making the depth map…";
        _make.IsEnabled = _save.IsEnabled = false;
        try { return await Task.Run(() => ReliefOutput.Full(spec, photo)); }
        catch (Exception e) when (e is OutOfMemoryException or ArgumentException)
        {
            _busy.Text = $"Couldn't make it: {e.Message}";
            return null;
        }
        finally { _make.IsEnabled = _save.IsEnabled = true; }
    }

    async Task Make()
    {
        if (_photo is not { } photo) return;
        var spec = Spec();
        if (spec.Problem(photo.Width, photo.Height) is not null) return;
        var r = await Full(spec, photo);
        if (r is null) return;
        var map = PhotoDepth.DepthMap(r);
        var state = _doc.State;
        Vec2? centre = null;
        Layer? layer = null;
        if (_editing is { } ed)
        {
            state = Ops.Delete(state, ed.Ids);
            centre = ed.Centre;
            layer = state.Layers.FirstOrDefault(l => l.Id == ed.LayerId);
        }
        else if (_fromPicture is { } pic && state.Shapes.Any(x => x.Id == pic.Id))
        {
            var b = pic.Bounds;
            centre = new Vec2((b.MinX + b.MaxX) / 2, (b.MinY + b.MaxY) / 2);
            if (_replace.IsChecked == true) state = Ops.Delete(state, new HashSet<Guid> { pic.Id });
        }
        var (next, ids, layerId) = ReliefOutput.Add(state, spec, photo, map, r.WidthMm, r.HeightMm, _machine, centre, layer);
        _doc.Commit(_editing is null ? "Photo relief" : "Change photo relief", next, ids);
        MadeOnLayer = layerId;
        LastUsed.Store(_dataFolder, spec);
        _status?.Invoke($"Photo relief: {photo.Name}, {r.WidthMm:0.#} × {r.HeightMm:0.#} mm, on the layer \"{next.Layers.First(l => l.Id == layerId).Name}\" (depth slices).");
        Close();
    }

    async Task SaveFile()
    {
        if (_photo is not { } photo) return;
        var spec = Spec();
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save depth map",
            SuggestedFileName = $"{photo.Name} relief depth map.png",
            DefaultExtension = "png",
            FileTypeChoices = [new FilePickerFileType("PNG picture") { Patterns = ["*.png"] }],
        });
        if (file?.TryGetLocalPath() is not { } path) return;
        var r = await Full(spec, photo);
        if (r is null) return;
        try
        {
            await File.WriteAllBytesAsync(path, PhotoDepth.DepthMap(r).EncodePng());
            LastUsed.Store(_dataFolder, spec);
            _busy.Text = $"Saved {Path.GetFileName(path)}.";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _busy.Text = $"Couldn't save it: {e.Message}"; }
    }
}

/// <summary>The settings last used (not the photo), kept in the add-on's data folder.</summary>
static class LastUsed
{
    const string FileName = "last.json";

    public static ReliefSpec? Load(string? folder)
    {
        if (folder is null) return null;
        try
        {
            var path = Path.Combine(folder, FileName);
            return File.Exists(path) ? ReliefSpec.FromJson(File.ReadAllText(path)) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    public static void Store(string? folder, ReliefSpec spec)
    {
        if (folder is null) return;
        try
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, FileName), (spec with { Picks = [], PanX = 0, PanY = 0, Zoom = 1 }).ToJson());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* not remembering isn't worth an error */ }
    }
}
