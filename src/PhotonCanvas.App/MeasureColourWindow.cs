using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using PhotonCanvas.Core;
using PhotonCanvas.Core.Camera;
using PhotonCanvas.Core.Imaging;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Colour;
using PhotonCanvas.Doc.Materials;
using static PhotonCanvas.App.UiKit;
using Bitmap = Avalonia.Media.Imaging.Bitmap;

namespace PhotonCanvas.App;

/// <summary>
/// Measure a burned colour test: take a close-up camera photo of it (the truest colour), or open
/// a scan or photo, click the four corner squares, and every square's colour is read. The
/// close-up camera sees only part of the bed, so a test can be measured in sections: slide the
/// plate, photograph the next block of squares, say which block it is, click its corners; the
/// results add up. The machine's lights can be set from here, and are saved with the palette. Shows what the plate really gave (as swatches and as a
/// colour map), how many clearly different colours that is, and saves them to the material
/// library as a palette for colour pictures.
/// <para>Gratings change with the angle they're seen from, so in Gratings mode the test is
/// photographed from five fixed views (straight, and 45° from each side, the lamp kept still).
/// Each view is measured on its own; the views play as a synchronised animation (also saved as
/// a GIF), and the set is saved to the library with a summary of the strongest squares.</para>
/// </summary>
public sealed class MeasureColourWindow : Window
{
    readonly IReadOnlyList<ColourTestCell> _cells;
    readonly MaterialLibrary _lib;
    readonly CameraPhoto? _camera;
    readonly double? _focus;
    readonly int _rows, _cols;
    readonly MachinePanel? _machine;
    readonly Slider _front = new() { Minimum = 0, Maximum = 100, Width = 120, VerticalAlignment = VerticalAlignment.Center };
    readonly Slider _back = new() { Minimum = 0, Maximum = 100, Width = 120, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock _frontText = new() { MinWidth = 34, VerticalAlignment = VerticalAlignment.Center }, _backText = new() { MinWidth = 34, VerticalAlignment = VerticalAlignment.Center };
    readonly Button _block = new() { Content = "Which squares are in this photo?" };
    bool _settingLights;
    // The block of squares the current photo shows (rows/columns, inclusive), and picking it on the chart.
    (int R, int C)? _sectionFirst;
    bool _pickingSection;

    readonly PickView _view = new();
    readonly TextBlock _step = new() { TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.SemiBold };
    readonly TextBlock _status = Hint();
    readonly Grid _swatches = new();
    readonly ColourMap _map = new() { Height = 230 };
    readonly TextBlock _verdict = new() { TextWrapping = TextWrapping.Wrap };
    readonly Slider _distinct = new() { Minimum = 2, Maximum = 15, Value = 5, Width = 140 };
    readonly TextBlock _distinctText = new() { VerticalAlignment = VerticalAlignment.Center, MinWidth = 36 };
    readonly ComboBox _material = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly TextBox _newName = new() { Watermark = "e.g. 304 stainless, brushed" };
    readonly TextBox _paletteName = new();
    readonly Button _save = new() { Content = "Save palette", Classes = { "primary" }, IsEnabled = false, MinWidth = 130 };
    readonly Button _csv = new() { Content = "Export CSV…", IsEnabled = false };
    readonly Button _grey = new() { Content = "Pick grey card…" };

    readonly List<Vec2> _greyClicks = [];

    /// <summary>One photo of the test and what was read from it (oxide mode uses one; gratings one per view).</summary>
    sealed class Shot
    {
        public RasterImage? Image;
        public Bitmap? Display;
        public string Method = "";
        public Homography? MmToPx;
        public readonly List<Vec2> Corners = [];
        public (double R, double G, double B)? Balance;
        public double? PhotoTurn; // a close-up photo, turned this far before showing
        // The block of squares this photo shows (rows/columns, inclusive).
        public (int R0, int C0, int R1, int C1) Section;
        public readonly Dictionary<(int, int), ColourSwatch> ByCell = [];
        public Lab? Bare; // the metal between the squares
        // Gratings: the grid as this view saw it, straightened.
        public RasterImage? Frame;
        public Bitmap? FrameBitmap;
    }

    readonly Shot[] _shots;
    Shot _s;

    // Gratings: five fixed views, played together.
    readonly ComboBox _mode = new() { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = new[] { "Oxide colours: one photo", "Gratings: five views" }, SelectedIndex = 0 };
    readonly Segmented _viewPick = new([.. GratingViews.All.Select(v => (GratingViews.Short(v), (string?)null))]);
    readonly TextBlock _viewsState = new() { Classes = { "hint" }, TextWrapping = TextWrapping.Wrap };
    readonly Image _anim = new() { Stretch = Stretch.Uniform, MaxHeight = 280 };
    readonly TextBlock _animLabel = new() { Classes = { "muted" }, HorizontalAlignment = HorizontalAlignment.Center };
    readonly Avalonia.Threading.DispatcherTimer _animTimer = new() { Interval = TimeSpan.FromMilliseconds(900) };
    int _animAt;
    readonly Button _gif = new() { Content = "Save GIF…", IsEnabled = false };
    readonly TextBox _notes = new() { Watermark = "Where the lamp was, how far the camera was (e.g. desk lamp above the back edge, phone 1.5 m away, zoomed)", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 50 };
    readonly StackPanel _gratingTop = new() { Spacing = 6 }, _gratingRight = new() { Spacing = 8 }, _oxideRight = new() { Spacing = 10 };
    bool Gratings => _mode.SelectedIndex == 1;
    GratingView CurrentView => GratingViews.All[Array.IndexOf(_shots, _s)];
    bool _pickingGrey;
    List<ColourSwatch> _measured = [];

    public MeasureColourWindow(DocState state, MaterialLibrary lib, CameraPhoto? camera, double? focusMm, MachinePanel? machine = null)
    {
        _machine = machine;
        _cells = ColourTest.Cells(state);
        _lib = lib;
        _camera = camera;
        _focus = focusMm;
        _rows = _cells.Count == 0 ? 0 : _cells.Max(c => c.Row) + 1;
        _cols = _cells.Count == 0 ? 0 : _cells.Max(c => c.Col) + 1;
        _shots = [.. Enumerable.Range(0, 5).Select(_ => new Shot { Section = (0, 0, Math.Max(0, _rows - 1), Math.Max(0, _cols - 1)) })];
        _s = _shots[0];
        Title = "Measure colours";
        Width = 1240;
        Height = 820;
        MinWidth = 900;
        MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");

        var open = Button("Open scan or photo…", "IcImport", "A flatbed scan is best (even light, no glare). A phone photo works if it's taken straight on in soft, even light, ideally with a grey card in view.");
        open.Click += async (_, _) => await OpenImage();
        var closeUp = Button("Close-up photo", "IcZoomIn", "Photograph the plate with the close-up camera in the laser head: the truest colours. It sees only part of the bed, so put a block of squares under it (measure the rest the same way after).");
        closeUp.Classes.Add("primary");
        closeUp.HorizontalAlignment = HorizontalAlignment.Left;
        closeUp.IsEnabled = machine?.IsConnected == true;
        closeUp.Click += async (_, _) => await CloseUpPhoto(closeUp);
        var cam = Button("Overview photo", "IcCamera", "The overview camera's picture: quick, but its colour isn't calibrated and its lights shine at an angle. Fine for a first look.");
        cam.IsEnabled = camera is not null;
        cam.Click += (_, _) => UseCamera();
        ToolTip.SetTip(_block, "Click the first and the last square of the block this photo shows, on the chart on the right. Its corners are what you click in the photo.");
        _block.Click += (_, _) => { _pickingSection = true; _sectionFirst = null; ShowResults(); UpdateStep(); };

        // The machine's lights: what the camera sees by. Saved with the palette.
        if (machine is not null)
        {
            var (f, b) = machine.Lights;
            _front.Value = f;
            _back.Value = b;
            machine.StateChanged += LightsFromMachine;
            Closed += (_, _) => machine.StateChanged -= LightsFromMachine;
        }
        _front.ValueChanged += (_, _) => LightsChanged();
        _back.ValueChanged += (_, _) => LightsChanged();
        ShowLightText();
        var reset = Button("Click the corners again");
        reset.Click += (_, _) => { _s.Corners.Clear(); _s.MmToPx = null; Remeasure(); };
        ToolTip.SetTip(_grey, "Click two opposite corners of a grey or white card in the picture: its colour is taken as neutral, which corrects the light's tint.");
        _grey.Click += (_, _) => { _pickingGrey = true; _greyClicks.Clear(); UpdateStep(); };
        _view.Clicked += OnPicked;
        _distinct.ValueChanged += (_, _) => ShowResults();
        _material.ItemsSource = lib.Materials.Select(m => m.Label).Prepend("New material…").ToList();
        _material.SelectedIndex = 0;
        _material.SelectionChanged += (_, _) => _newName.IsVisible = _material.SelectedIndex == 0;
        _paletteName.Text = $"Colour test {DateTime.Now:yyyy-MM-dd}";
        _save.Click += (_, _) => Save();
        // A grating test (overscan on every square) starts in Gratings mode.
        _mode.SelectedIndex = _cells.Count > 0 && _cells.All(c => c.Layer.OverscanMm > 0) ? 1 : 0;
        _mode.SelectionChanged += (_, _) => ModeChanged();
        _viewPick.SelectedIndex = 0;
        _viewPick.Changed += i => SelectShot(i);
        _animTimer.Tick += (_, _) => NextFrame();
        Closed += (_, _) => _animTimer.Stop();
        _gif.Click += async (_, _) => await SaveGif();
        ToolTip.SetTip(_gif, "Save the views as an animated GIF: the grid as each view saw it, one after another.");
        _gratingTop.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8,
            Children = { new TextBlock { Text = "View", Classes = { "muted" }, VerticalAlignment = VerticalAlignment.Center }, new Border { Width = 470, Child = _viewPick } },
        });
        _gratingTop.Children.Add(_viewsState);
        _gratingRight.Children.Add(new TextBlock { Text = "THE VIEWS TOGETHER", Classes = { "h" } });
        _gratingRight.Children.Add(new Border { Background = Brush("Bg0"), CornerRadius = new(6), MinHeight = 120, Child = _anim, ClipToBounds = true });
        _gratingRight.Children.Add(_animLabel);
        _gratingRight.Children.Add(_gif);
        _csv.Click += async (_, _) => await ExportCsv();

        var left = new DockPanel();
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { closeUp, cam, open } };
        var tools2 = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { _block, reset, _grey } };
        foreach (var b in tools.Children.OfType<Button>().Concat(tools2.Children.OfType<Button>())) b.HorizontalAlignment = HorizontalAlignment.Left;
        bool lightsOn = machine?.IsConnected == true;
        var lights = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8, IsEnabled = lightsOn,
            Children =
            {
                new PathIcon { Data = Icon("IcBulb"), Width = 14, Height = 14, VerticalAlignment = VerticalAlignment.Center },
                new TextBlock { Text = "Front", Classes = { "muted" }, VerticalAlignment = VerticalAlignment.Center }, _front, _frontText,
                new TextBlock { Text = "Back", Classes = { "muted" }, VerticalAlignment = VerticalAlignment.Center, Margin = new(8, 0, 0, 0) }, _back, _backText,
                new TextBlock { Text = lightsOn ? "Keep them the same for every photo you compare." : "Connect the machine to set its lights.", Classes = { "hint" }, VerticalAlignment = VerticalAlignment.Center, Margin = new(8, 0, 0, 0) },
            },
        };
        var top = new StackPanel { Spacing = 6, Margin = new(0, 0, 0, 8), Children = { Field("Measure", _mode), _gratingTop, tools, tools2, lights, _step, _status } };
        DockPanel.SetDock(top, Dock.Top);
        left.Children.Add(top);
        left.Children.Add(new Border { Background = Brush("Bg0"), CornerRadius = new(6), ClipToBounds = true, Child = _view });

        var right = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                new TextBlock { Text = "WHAT THE PLATE GAVE", Classes = { "h" } },
                _swatches,
                Hint("Click a square to leave it out (blotchy or burnt ones are left out already: marked !)."),
                _oxideRight,
                _gratingRight,
                _verdict,
                new TextBlock { Text = "SAVE", Classes = { "h" }, Margin = new(0, 4, 0, 0) },
                Field("Material", _material),
                _newName,
                Field("Name", _paletteName),
                _notes,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _save, _csv } },
            },
        };
        _oxideRight.Children.Add(new TextBlock { Text = "COLOUR MAP", Classes = { "h" }, Margin = new(0, 4, 0, 0) });
        _oxideRight.Children.Add(_map);
        _oxideRight.Children.Add(Hint("Each dot is a square, placed by hue and saturation (grey in the middle). Ringed dots are the clearly different ones."));
        _oxideRight.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8,
            Children = { new TextBlock { Text = "Clearly different if ΔE ≥", VerticalAlignment = VerticalAlignment.Center, Classes = { "muted" } }, _distinct, _distinctText },
        });
        var grid = new Grid { ColumnDefinitions = new("*,16,420") };
        grid.Children.Add(left);
        var rs = new ScrollViewer { Content = right };
        Grid.SetColumn(rs, 2);
        grid.Children.Add(rs);
        Content = new Border { Padding = new(16), Child = grid };

        if (_cells.Count == 0)
        {
            _step.Text = "There's no colour test in this design. Add one with Laser → Colour test, burn it, then come back here with a scan or photo of it.";
            open.IsEnabled = cam.IsEnabled = closeUp.IsEnabled = false;
        }
        ModeChanged();
    }

    // ---- gratings: views -----------------------------------------------------------------

    void ModeChanged()
    {
        bool g = Gratings;
        _gratingTop.IsVisible = _gratingRight.IsVisible = _notes.IsVisible = g;
        _oxideRight.IsVisible = !g;
        _save.Content = g ? "Save grating set" : "Save palette";
        if (string.IsNullOrWhiteSpace(_paletteName.Text) || _paletteName.Text.StartsWith("Colour test ") || _paletteName.Text.StartsWith("Gratings "))
            _paletteName.Text = $"{(g ? "Gratings" : "Colour test")} {DateTime.Now:yyyy-MM-dd}";
        SelectShot(g ? Math.Max(0, _viewPick.SelectedIndex) : 0);
        if (g) _animTimer.Start(); else _animTimer.Stop();
    }

    /// <summary>Switch to one view's photo (or the single oxide photo).</summary>
    void SelectShot(int i)
    {
        _s = _shots[Math.Clamp(i, 0, _shots.Length - 1)];
        _greyClicks.Clear();
        _pickingGrey = _pickingSection = false;
        _view.Bitmap = _s.Display;
        _view.ImageSize = _s.Image is { } img ? new Size(img.Width, img.Height) : default;
        Remeasure();
    }

    /// <summary>The next view's straightened picture in the animation.</summary>
    void NextFrame()
    {
        var frames = _shots.Select((s, i) => (s, i)).Where(x => x.s.FrameBitmap is not null).ToList();
        if (frames.Count == 0) { _anim.Source = null; _animLabel.Text = "Measure a view to see it here."; return; }
        _animAt = (_animAt + 1) % frames.Count;
        var (shot, i) = frames[_animAt];
        _anim.Source = shot.FrameBitmap;
        _animLabel.Text = $"{GratingViews.Short(GratingViews.All[i])}  ({_animAt + 1} of {frames.Count})";
    }

    /// <summary>The grid as the current view saw it, straightened and labelled (for the animation and the library).</summary>
    void MakeFrame()
    {
        if (!Gratings || _s.Image is not { } img || _s.MmToPx is not { } h || _cells.Count == 0) return;
        var area = _cells.Select(c => c.Bounds).Aggregate(Rect2.Empty, (a, b) => a.Union(b)).Inflate(1.5);
        area = new Rect2(area.MinX, area.MinY, area.MaxX, area.MaxY + area.Height * 0.16); // room below for the caption
        double pxPerMm = Math.Clamp(640 / Math.Max(1, area.Width), 4, 20);
        _s.Frame = GratingMeasure.Straighten(img, h, area, pxPerMm).WithCaption(GratingViews.Short(CurrentView));
        _s.FrameBitmap?.Dispose();
        using var ms = new MemoryStream(_s.Frame.EncodePng());
        _s.FrameBitmap = new Bitmap(ms);
        _gif.IsEnabled = true;
    }

    async Task SaveGif()
    {
        var frames = _shots.Where(s => s.Frame is not null).Select(s => s.Frame!).ToList();
        if (frames.Count == 0) return;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save the views as a GIF",
            DefaultExtension = "gif",
            SuggestedFileName = (_paletteName.Text ?? "gratings") + ".gif",
            FileTypeChoices = [new FilePickerFileType("GIF") { Patterns = ["*.gif"] }],
        });
        if (file?.TryGetLocalPath() is not { } path) return;
        await File.WriteAllBytesAsync(path, await Task.Run(() => GifEncoder.Encode(frames, 90)));
        _status.Text = $"Saved {Path.GetFileName(path)} ({frames.Count} views).";
    }

    // ---- picture and corners ------------------------------------------------------------

    async Task OpenImage()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Scan or photo of the burned colour test",
            FileTypeFilter = [new FilePickerFileType("Pictures") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.tif", "*.tiff", "*.bmp", "*.webp"] }],
        });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path) return;
        try
        {
            var bytes = await File.ReadAllBytesAsync(path);
            var img = await Task.Run(() => RasterImage.Decode(bytes));
            _s.PhotoTurn = null;
            SetImage(img, Path.GetExtension(path).ToLowerInvariant() is ".tif" or ".tiff" or ".png" ? "scan" : "photo");
            _status.Text = $"{Path.GetFileName(path)} · {img.Width} × {img.Height} px";
        }
        catch (Exception e) when (e is IOException or FormatException or UnauthorizedAccessException or ArgumentException)
        {
            _status.Text = $"Couldn't open it: {e.Message}";
        }
    }

    /// <summary>A close-up photo becomes the picture to measure (earlier sections stay measured).</summary>
    async Task CloseUpPhoto(Button button)
    {
        if (_machine is null) return;
        button.IsEnabled = false;
        _status.Text = "Taking a close-up photo…";
        try
        {
            if (await _machine.CloseUpPhotoAsync() is { } jpg)
            {
                // Turned the way the camera sits, so the grid reads the same way round as on screen.
                double turn = _machine.CloseUpRotationDeg;
                var img = await Task.Run(() => RasterImage.Decode(jpg).Rotated(turn));
                _s.PhotoTurn = turn;
                SetImage(img, "close-up camera");
                _status.Text = $"Close-up photo, lights front {_front.Value:0}% / back {_back.Value:0}%. " +
                               (_s.Section == (0, 0, _rows - 1, _cols - 1) ? "If it shows only part of the grid, say which squares first." : "");
            }
        }
        catch (Exception e) when (e is PhotonCanvas.Device.DeviceException or FormatException or ArgumentException)
        {
            _status.Text = $"Couldn't take the photo: {e.Message}";
        }
        finally { button.IsEnabled = _machine.IsConnected; }
    }

    void LightsChanged()
    {
        ShowLightText();
        if (_settingLights || _machine is null) return;
        _machine.SetLights((int)Math.Round(_front.Value), (int)Math.Round(_back.Value));
    }

    void LightsFromMachine()
    {
        if (_machine is null) return;
        _settingLights = true;
        var (f, b) = _machine.Lights;
        _front.Value = f;
        _back.Value = b;
        _settingLights = false;
        ShowLightText();
    }

    void ShowLightText()
    {
        _frontText.Text = _front.Value < 0.5 ? "off" : $"{_front.Value:0}%";
        _backText.Text = _back.Value < 0.5 ? "off" : $"{_back.Value:0}%";
    }

    void UseCamera()
    {
        if (_camera is not { } p) return;
        _s.PhotoTurn = null;
        _s.Section = (0, 0, _rows - 1, _cols - 1);
        SetImage(p.Image, "overview camera");
        // The camera picture is already mapped to the bed: no corners to click.
        var a = p.Area;
        Vec2 Px(Vec2 mm) => new((mm.X - a.MinX) / p.MmPerPx, (mm.Y - a.MinY) / p.MmPerPx);
        _s.MmToPx = Homography.Fit([.. new[] { a.TopLeft, new Vec2(a.MaxX, a.MinY), a.BottomRight, new Vec2(a.MinX, a.MaxY) }.Select(m => (m, Px(m)))]);
        _status.Text = "Using the machine's camera picture. Its colours are only a rough guide.";
        Remeasure();
    }

    void SetImage(RasterImage img, string method)
    {
        _s.Image = img;
        _s.Method = method;
        _s.Corners.Clear();
        _greyClicks.Clear();
        _s.Balance = null;
        _s.MmToPx = null;
        _s.Display?.Dispose();
        using (var ms = new MemoryStream(img.EncodeJpeg(2400, 92))) _s.Display = new Bitmap(ms);
        _view.Bitmap = _s.Display;
        _s.Frame = null;
        _s.FrameBitmap?.Dispose();
        _s.FrameBitmap = null;
        _view.ImageSize = new(img.Width, img.Height);
        Remeasure();
    }

    /// <summary>Where the corner squares are, in the order they're clicked.</summary>
    IReadOnlyList<(string Name, ColourTestCell Cell)> CornerCells()
    {
        var (r0, c0, r1, c1) = _s.Section;
        return [("top-left", Cell(r0, c0)), ("top-right", Cell(r0, c1)), ("bottom-right", Cell(r1, c1)), ("bottom-left", Cell(r1, c0))];
    }

    /// <summary>A square as it's labelled on the burned plate, e.g. "200 kHz and 20% power".</summary>
    string Describe(ColourTestCell cell)
    {
        static string Value(ColourParam p, Layer l) => p switch
        {
            ColourParam.Power => $"{l.Laser.Power:0.#}% power",
            ColourParam.Speed => $"{l.Laser.Speed:0.#} mm/s",
            ColourParam.LinesPerCm => $"{l.LinesPerCm:0} lines/cm",
            ColourParam.FrequencyKhz => $"{l.Laser.FrequencyKhz} kHz",
            _ => $"{l.Laser.PulseWidthNs} ns",
        };
        static double Of(ColourParam p, Layer l) => p switch
        {
            ColourParam.Power => l.Laser.Power,
            ColourParam.Speed => l.Laser.Speed,
            ColourParam.LinesPerCm => l.LinesPerCm,
            ColourParam.FrequencyKhz => l.Laser.FrequencyKhz,
            _ => l.Laser.PulseWidthNs,
        };
        // Which setting changes across the grid, and which down it.
        ColourParam? Varies(ColourTestCell a, ColourTestCell? b) => b is null ? null
            : Enum.GetValues<ColourParam>().Cast<ColourParam?>().FirstOrDefault(p => Math.Abs(Of(p!.Value, a.Layer) - Of(p.Value, b.Layer)) > 1e-9);
        var origin = Cell(0, 0);
        var across = Varies(origin, _cols > 1 ? Cell(0, 1) : null);
        var down = Varies(origin, _rows > 1 ? Cell(1, 0) : null);
        var parts = new List<string>();
        if (across is { } a) parts.Add(Value(a, cell.Layer));
        if (down is { } d) parts.Add(Value(d, cell.Layer));
        return parts.Count == 0 ? $"row {cell.Row + 1}, column {cell.Col + 1}" : string.Join(" and ", parts);
    }

    /// <summary>The square to click next in the photo (outlined on the chart), if any.</summary>
    (int R, int C)? NextCorner => _s.Image is not null && !_pickingGrey && !_pickingSection && _s.MmToPx is null && _s.Corners.Count < 4
        ? (CornerCells()[_s.Corners.Count].Cell.Row, CornerCells()[_s.Corners.Count].Cell.Col) : null;

    bool InSection(int r, int c) => r >= _s.Section.R0 && r <= _s.Section.R1 && c >= _s.Section.C0 && c <= _s.Section.C1;

    ColourTestCell Cell(int r, int c) => _cells.FirstOrDefault(x => x.Row == r && x.Col == c) ?? _cells[0];

    void UpdateStep()
    {
        if (_cells.Count == 0) return;
        if (_pickingSection) _step.Text = _sectionFirst is null ? "On the chart, click the first square this photo shows." : "Now the last square it shows (the opposite corner of the block).";
        else if (_s.Image is null) _step.Text = Gratings
            ? $"{GratingViews.Short(CurrentView)}: photograph the plate {GratingViews.Long(CurrentView)}, the lamp where it stays for every view, then open the photo here." +
              (CurrentView == GratingView.Straight ? " (The close-up camera works for this one.)" : "")
            : "Put the burned test under the close-up camera and take a close-up photo (or open a scan).";
        else if (_pickingGrey) _step.Text = _greyClicks.Count == 0 ? "Click one corner of the grey card." : "Now the opposite corner.";
        else if (_s.MmToPx is null && _s.Corners.Count < 4)
        {
            var (name, cell) = CornerCells()[_s.Corners.Count];
            _step.Text = $"{_s.Corners.Count + 1} of 4: click the middle of the square at {Describe(cell)}, the {name} corner (outlined on the chart).";
        }
        else _step.Text = _s.ByCell.Count < _cells.Count
            ? $"Measured {_s.ByCell.Count} of {_cells.Count} squares. Check the outlines sit inside each square. For the rest, slide the plate, take another photo and say which squares it shows."
            : "All squares measured. Check the outlines sit inside each square; if not, click the corners again.";
        _view.Corners = _s.Corners.ToList();
        _view.Grey = _greyClicks.Count == 2 ? [_greyClicks[0], _greyClicks[1]] : null;
        _view.InvalidateVisual();
    }

    void OnPicked(Vec2 px)
    {
        if (_s.Image is null) return;
        if (_pickingGrey)
        {
            _greyClicks.Add(px);
            if (_greyClicks.Count == 2)
            {
                _pickingGrey = false;
                var (a, b) = (_greyClicks[0], _greyClicks[1]);
                _s.Balance = ColourMeasure.WhiteBalance(_s.Image, [new(a.X, a.Y), new(b.X, a.Y), new(b.X, b.Y), new(a.X, b.Y)]);
                if (!_s.Method.Contains("grey card")) _s.Method += " with grey card";
                Remeasure();
            }
            UpdateStep();
            return;
        }
        if (_s.MmToPx is not null || _s.Corners.Count >= 4) return;
        _s.Corners.Add(px);
        if (_s.Corners.Count == 4)
        {
            // Corner squares' centres in the design ↔ where they were clicked.
            var pairs = CornerCells().Select((c, i) => (c.Cell.Bounds.Center, _s.Corners[i])).ToList();
            _s.MmToPx = Homography.Fit(pairs);
            if (_s.MmToPx is null) { _s.Corners.Clear(); _status.Text = "Those four points don't make a grid: click the corners again."; }
            else LearnTurn(_s.MmToPx.Value, pairs[0].Center);
            _s.Frame = null; // the straightened view is redone from the new corners
            Remeasure();
        }
        UpdateStep();
        ShowResults(); // moves the outline on the chart to the next corner
    }

    /// <summary>
    /// From the clicked corners: how far off straight the close-up photo still is. The camera's
    /// angle is corrected by that, so the next photos (and the close-up view) come out straight.
    /// </summary>
    void LearnTurn(Homography h, Vec2 atMm)
    {
        if (_s.PhotoTurn is not { } turned || _machine is null) return;
        var o = h.Apply(atMm);
        var x = h.Apply(atMm + new Vec2(1, 0)) - o;
        var y = h.Apply(atMm + new Vec2(0, 1)) - o;
        if (x.X * y.Y - x.Y * y.X < 0)
        {
            _status.Text = "This photo looks mirrored. The measurement is fine, but the close-up view can't be straightened by turning alone.";
            return;
        }
        double off = Math.Atan2(x.Y, x.X) * 180 / Math.PI;
        if (Math.Abs(off) < 0.5) return;
        _machine.LearnCloseUpRotation(turned - off);
        _status.Text = $"Learned the close-up camera's angle ({_machine.CloseUpRotationDeg:0.#}°): the next photos come out straight.";
    }

    void Remeasure()
    {
        // The squares in this photo are (re)measured; ones measured from earlier photos stay.
        if (_s.Image is { } img && _s.MmToPx is { } h)
        {
            var inSection = _cells.Where(c => InSection(c.Row, c.Col)).ToList();
            foreach (var s in ColourMeasure.Measure(img, h, inSection, _s.Balance))
                _s.ByCell[(s.Row, s.Col)] = s;
            _s.Bare = ColourMeasure.MeasureBare(img, h, inSection, _s.Balance) ?? _s.Bare;
        }
        _measured = _s.ByCell.Values.OrderBy(s => s.Row).ThenBy(s => s.Col).ToList();
        if (_s.MmToPx is not null && _s.Frame is null) MakeFrame();
        // Outlines of the areas read, so a wrong click shows.
        _view.Regions = _s.MmToPx is { } m
            ? _cells.Where(c => InSection(c.Row, c.Col)).Select(c =>
            {
                var r = c.Bounds;
                double hw = r.Width * 0.3, hh = r.Height * 0.3;
                var ctr = r.Center;
                return new[] { new Vec2(ctr.X - hw, ctr.Y - hh), new(ctr.X + hw, ctr.Y - hh), new(ctr.X + hw, ctr.Y + hh), new(ctr.X - hw, ctr.Y + hh) }.Select(m.Apply).ToArray();
            }).ToList()
            : [];
        UpdateStep();
        ShowResults();
    }

    // ---- results ---------------------------------------------------------------------------

    static IBrush Fill(ColourSwatch s)
    {
        var (r, g, b) = ColourScience.LabToSrgb(s.Colour);
        return new SolidColorBrush(Color.FromRgb(r, g, b));
    }

    void ShowResults()
    {
        _distinctText.Text = $"{_distinct.Value:0}";
        _swatches.Children.Clear();
        _swatches.RowDefinitions.Clear();
        _swatches.ColumnDefinitions.Clear();
        _save.IsEnabled = _csv.IsEnabled = _measured.Count > 0;
        var distinct = ColourMeasure.Distinct(_measured, _distinct.Value);
        _map.Show(_measured, distinct);
        for (int c = 0; c < _cols; c++) _swatches.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        for (int r = 0; r < _rows; r++) _swatches.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        double size = Math.Clamp(380.0 / Math.Max(_cols, 1) - 3, 14, 40);
        var measured = _measured.ToDictionary(s => (s.Row, s.Col));
        foreach (var cell in _cells.Where(c => !measured.ContainsKey((c.Row, c.Col))))
        {
            var (r, c) = (cell.Row, cell.Col);
            var empty = new Border
            {
                Width = size, Height = size, Margin = new(1.5), CornerRadius = new(3), Background = Brush("Bg0"),
                BorderThickness = new(NextCorner == (r, c) ? 3 : 1), BorderBrush = NextCorner == (r, c) ? Brushes.White : InSection(r, c) ? Brush("Accent") : Brush("Line"),
                Cursor = new Cursor(StandardCursorType.Hand),
            };
            ToolTip.SetTip(empty, $"R{r + 1} C{c + 1}: not measured yet");
            empty.PointerPressed += (_, _) => { if (_pickingSection) PickSection(r, c); };
            Grid.SetRow(empty, r);
            Grid.SetColumn(empty, c);
            _swatches.Children.Add(empty);
        }
        foreach (var s in _measured)
        {
            var sw = s;
            var face = new Border
            {
                Width = size, Height = size, Margin = new(1.5), CornerRadius = new(3),
                Background = Fill(s), Opacity = s.Use ? 1 : 0.35,
                BorderThickness = new(NextCorner == (s.Row, s.Col) ? 3 : distinct.Contains(s) ? 2 : 0), BorderBrush = Brushes.White,
                Child = s.Spread > ColourMeasure.BlotchySpread ? new TextBlock { Text = "!", FontWeight = FontWeight.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } : null,
                Cursor = new Cursor(StandardCursorType.Hand),
            };
            ToolTip.SetTip(face, $"R{s.Row + 1} C{s.Col + 1}: {s.Settings.Summary()}\nL {s.L:0.0}  a {s.A:0.0}  b {s.B:0.0}  ·  evenness ΔE {s.Spread:0.0}{(s.Use ? "" : "  ·  left out")}");
            face.PointerPressed += (_, _) =>
            {
                if (_pickingSection) { PickSection(sw.Row, sw.Col); return; }
                _s.ByCell[(sw.Row, sw.Col)] = sw with { Use = !sw.Use };
                _measured = _s.ByCell.Values.OrderBy(x => x.Row).ThenBy(x => x.Col).ToList();
                ShowResults();
            };
            Grid.SetRow(face, s.Row);
            Grid.SetColumn(face, s.Col);
            _swatches.Children.Add(face);
        }

        if (Gratings)
        {
            _viewsState.Text = string.Join("   ", _shots.Select((s, i) => $"{GratingViews.Short(GratingViews.All[i])} {(s.ByCell.Count == 0 ? "–" : s.ByCell.Count == _cells.Count ? "✓" : $"{s.ByCell.Count}/{_cells.Count}")}"));
            _save.IsEnabled = _csv.IsEnabled = _shots.Any(s => s.ByCell.Count > 0);
            var scores = GratingMeasure.Score(ViewResults());
            _verdict.Text = scores.Count == 0 ? "" :
                "Strongest squares across the views:\n" + string.Join("\n", scores.Take(5).Select(sc =>
                    $"• R{sc.Row + 1} C{sc.Col + 1} ({Describe(Cell(sc.Row, sc.Col))}): colour strength {sc.Vividness:0}, " +
                    (sc.LightsUpAt.Count == 0 ? "weak everywhere" : $"lights up at {string.Join(", ", sc.LightsUpAt.Select(GratingViews.Short))}")));
            return;
        }
        if (_measured.Count == 0)
        {
            _verdict.Text = "";
            return;
        }
        int used = _measured.Count(s => s.Use);
        double lMin = _measured.Where(s => s.Use).Select(s => s.L).DefaultIfEmpty(0).Min(), lMax = _measured.Where(s => s.Use).Select(s => s.L).DefaultIfEmpty(0).Max();
        double cMax = _measured.Where(s => s.Use).Select(s => s.Colour.Chroma).DefaultIfEmpty(0).Max();
        var hues = distinct.Where(s => s.Colour.Chroma > 12).Select(s => { var (r, g, b) = ColourScience.LabToSrgb(s.Colour); return HueName(Diffraction.HsvHue(r, g, b)); }).Distinct().ToList();
        _verdict.Text = $"{distinct.Count} clearly different colours from {used} usable squares (of {_measured.Count}). " +
                        $"Lightness {lMin:0}–{lMax:0}, strongest colour chroma {cMax:0}." +
                        (hues.Count > 0 ? $" Hues reached: {string.Join(", ", hues)}." : " No strongly coloured squares: try more energy, other pulse widths, or a different plate.");
    }

    /// <summary>Picking the block of squares a photo shows: first square, then the opposite one.</summary>
    void PickSection(int r, int c)
    {
        if (_sectionFirst is not { } first)
        {
            _sectionFirst = (r, c);
            UpdateStep();
            return;
        }
        _s.Section = (Math.Min(first.R, r), Math.Min(first.C, c), Math.Max(first.R, r), Math.Max(first.C, c));
        _sectionFirst = null;
        _pickingSection = false;
        _s.Corners.Clear();
        _s.MmToPx = null;
        _view.Regions = [];
        _status.Text = $"This photo shows R{_s.Section.R0 + 1}–{_s.Section.R1 + 1}, C{_s.Section.C0 + 1}–{_s.Section.C1 + 1}: click its four corner squares.";
        ShowResults();
        UpdateStep();
    }

    /// <summary>A plain name for a colour-wheel (HSV) hue.</summary>
    static string HueName(double h) => h switch
    {
        < 15 or >= 335 => "red/pink",
        < 40 => "orange/brown",
        < 65 => "gold/yellow",
        < 150 => "green",
        < 200 => "teal",
        < 255 => "blue",
        < 290 => "violet",
        _ => "purple/magenta",
    };

    void Save()
    {
        if (_measured.Count == 0) return;
        Material m;
        if (_material.SelectedIndex <= 0)
        {
            var name = _newName.Text?.Trim();
            if (string.IsNullOrEmpty(name)) { _status.Text = "Give the material a name."; _newName.Focus(); return; }
            m = new Material { Name = name, Category = "Metal", HighlyReflective = Material.LooksReflective(name) };
        }
        else m = _lib.Materials[_material.SelectedIndex - 1];
        if (Gratings)
        {
            SaveGratings(m);
            return;
        }
        var palette = new ColourPalette
        {
            Name = string.IsNullOrWhiteSpace(_paletteName.Text) ? $"Colour test {DateTime.Now:yyyy-MM-dd}" : _paletteName.Text.Trim(),
            FocusMm = _focus,
            Method = _machine is not null && _s.Method.Contains("camera") ? $"{_s.Method}, lights front {_front.Value:0}% / back {_back.Value:0}%" : _s.Method,
            Swatches = [.. _measured],
            BareL = _s.Bare?.L,
            BareA = _s.Bare?.A,
            BareB = _s.Bare?.B,
        };
        _lib.Upsert(m with { Palettes = m.Palettes.Add(palette) });
        _lib.Save();
        _material.ItemsSource = _lib.Materials.Select(x => x.Label).Prepend("New material…").ToList();
        _material.SelectedIndex = _lib.Materials.FindIndex(x => x.Id == m.Id) + 1;
        _status.Text = $"Saved “{palette.Name}” to {m.Label}: {palette.Swatches.Count} squares, {ColourMeasure.Distinct(palette.Swatches, _distinct.Value).Count} clearly different colours.";
    }

    /// <summary>Each measured view's squares (with their view).</summary>
    List<GratingViewResult> ViewResults() => [.. _shots.Select((s, i) => (s, i)).Where(x => x.s.ByCell.Count > 0).Select(x => new GratingViewResult
    {
        View = GratingViews.All[x.i],
        Swatches = [.. x.s.ByCell.Values.OrderBy(v => v.Row).ThenBy(v => v.Col)],
    })];

    void SaveGratings(Material m)
    {
        var id = Guid.NewGuid();
        var views = new List<GratingViewResult>();
        foreach (var v in ViewResults())
        {
            var shot = _shots[GratingViews.All.ToList().IndexOf(v.View)];
            string? frame = shot.Frame is { } f ? _lib.SaveFile($"{id:N}-{v.View}.png".ToLowerInvariant(), f.EncodePng()) : null;
            views.Add(v with { Frame = frame });
        }
        var frames = _shots.Where(s => s.Frame is not null).Select(s => s.Frame!).ToList();
        string? gif = frames.Count > 0 ? _lib.SaveFile($"{id:N}.gif", GifEncoder.Encode(frames, 90)) : null;
        string lights = _machine is not null ? $"Machine lights front {_front.Value:0}% / back {_back.Value:0}%. " : "";
        var set = new GratingSet
        {
            Id = id,
            Name = string.IsNullOrWhiteSpace(_paletteName.Text) ? $"Gratings {DateTime.Now:yyyy-MM-dd}" : _paletteName.Text.Trim(),
            FocusMm = _focus,
            Notes = (lights + (_notes.Text?.Trim() ?? "")).Trim(),
            Views = [.. views],
            Gif = gif,
        };
        _lib.Upsert(m with { Gratings = m.Gratings.Add(set) });
        _lib.Save();
        _material.ItemsSource = _lib.Materials.Select(x => x.Label).Prepend("New material…").ToList();
        _material.SelectedIndex = _lib.Materials.FindIndex(x => x.Id == m.Id) + 1;
        _status.Text = $"Saved “{set.Name}” to {m.Label}: {views.Count} of 5 views.";
    }

    async Task ExportCsv()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export measured colours",
            DefaultExtension = "csv",
            SuggestedFileName = (_paletteName.Text ?? "colours") + ".csv",
            FileTypeChoices = [new FilePickerFileType("CSV") { Patterns = ["*.csv"] }],
        });
        if (file?.TryGetLocalPath() is not { } path) return;
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder("view,row,col,power_pct,speed_mm_s,lines_per_cm,frequency_khz,pulse_ns,L,a,b,sRGB,evenness_dE,used\n");
        var rows = Gratings
            ? ViewResults().SelectMany(v => v.Swatches.Select(s => (View: GratingViews.Short(v.View), S: s)))
            : _measured.Select(s => (View: "", S: s));
        foreach (var (view, s) in rows)
        {
            var (r, g, b) = ColourScience.LabToSrgb(s.Colour);
            var l = s.Settings.Laser;
            sb.AppendLine(string.Join(",", view, s.Row + 1, s.Col + 1, l.Power.ToString(inv), l.Speed.ToString(inv), s.Settings.LinesPerCm.ToString(inv), l.FrequencyKhz, l.PulseWidthNs,
                s.L.ToString("0.00", inv), s.A.ToString("0.00", inv), s.B.ToString("0.00", inv), $"#{r:X2}{g:X2}{b:X2}", s.Spread.ToString("0.00", inv), s.Use ? 1 : 0));
        }
        await File.WriteAllTextAsync(path, sb.ToString());
        _status.Text = $"Exported {_measured.Count} squares to {Path.GetFileName(path)}.";
    }

    // ---- the picture, with clicks ----------------------------------------------------------

    /// <summary>Shows the picture fitted to the space; clicks are reported in picture pixels.</summary>
    sealed class PickView : Control
    {
        public Bitmap? Bitmap { get; set; }
        public Size ImageSize { get; set; }
        public List<Vec2> Corners { get; set; } = [];
        public Vec2[]? Grey { get; set; }
        public List<Vec2[]> Regions { get; set; } = [];
        public event Action<Vec2>? Clicked;

        public PickView() => Cursor = new Cursor(StandardCursorType.Cross);

        (double Scale, Point Offset) Fit()
        {
            if (ImageSize.Width <= 0) return (1, default);
            double s = Math.Min(Bounds.Width / ImageSize.Width, Bounds.Height / ImageSize.Height);
            return (s, new Point((Bounds.Width - ImageSize.Width * s) / 2, (Bounds.Height - ImageSize.Height * s) / 2));
        }

        public override void Render(DrawingContext ctx)
        {
            ctx.DrawRectangle(Brushes.Transparent, null, new Rect(Bounds.Size));
            if (Bitmap is null) return;
            var (s, o) = Fit();
            ctx.DrawImage(Bitmap, new Rect(o.X, o.Y, ImageSize.Width * s, ImageSize.Height * s));
            Point P(Vec2 v) => new(o.X + v.X * s, o.Y + v.Y * s);
            var outline = new Pen(new SolidColorBrush(Color.Parse("#E7E9EE")), 1);
            var shadow = new Pen(new SolidColorBrush(Colors.Black, 0.6), 2.5);
            foreach (var q in Regions)
            {
                var g = new PolylineGeometry(q.Select(P).ToList(), true);
                ctx.DrawGeometry(null, shadow, g);
                ctx.DrawGeometry(null, outline, g);
            }
            if (Grey is { } gr)
                ctx.DrawRectangle(null, new Pen(Brushes.White, 1.5, new DashStyle([4, 3], 0)), new Rect(P(gr[0]), P(gr[1])));
            var accent = new SolidColorBrush(Color.Parse("#4C7BD9"));
            for (int i = 0; i < Corners.Count; i++)
            {
                var c = P(Corners[i]);
                ctx.DrawEllipse(accent, new Pen(Brushes.White, 1.5), c, 7, 7);
                var t = new FormattedText($"{i + 1}", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default, weight: FontWeight.Bold), 10, Brushes.White);
                ctx.DrawText(t, new Point(c.X - t.Width / 2, c.Y - t.Height / 2));
            }
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            if (Bitmap is null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            var (s, o) = Fit();
            var p = e.GetPosition(this);
            var px = new Vec2((p.X - o.X) / s, (p.Y - o.Y) / s);
            if (px.X < 0 || px.Y < 0 || px.X > ImageSize.Width || px.Y > ImageSize.Height) return;
            Clicked?.Invoke(px);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            InvalidateVisual();
            return base.ArrangeOverride(finalSize);
        }
    }

    /// <summary>The measured colours by hue and saturation (CIELAB a*, b*), grey at the centre.</summary>
    sealed class ColourMap : Control
    {
        IReadOnlyList<ColourSwatch> _all = [];
        HashSet<ColourSwatch> _distinct = [];

        public void Show(IReadOnlyList<ColourSwatch> all, IReadOnlyList<ColourSwatch> distinct)
        {
            _all = all;
            _distinct = distinct.ToHashSet();
            InvalidateVisual();
        }

        public override void Render(DrawingContext ctx)
        {
            var r = new Rect(Bounds.Size);
            ctx.DrawRectangle(new SolidColorBrush(Color.Parse("#0E1014")), null, r, 6, 6);
            double range = Math.Max(30, _all.Select(s => Math.Max(Math.Abs(s.A), Math.Abs(s.B))).DefaultIfEmpty(0).Max() * 1.15);
            var c = r.Center;
            double half = Math.Min(r.Width, r.Height) / 2 - 10;
            var axis = new Pen(new SolidColorBrush(Color.Parse("#2C323D")), 1);
            ctx.DrawLine(axis, new Point(c.X - half, c.Y), new Point(c.X + half, c.Y));
            ctx.DrawLine(axis, new Point(c.X, c.Y - half), new Point(c.X, c.Y + half));
            ctx.DrawEllipse(null, axis, c, half, half);
            var muted = new SolidColorBrush(Color.Parse("#8F98A6"));
            void Tag(string s, double x, double y)
            {
                var t = new FormattedText(s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default), 10, muted);
                ctx.DrawText(t, new Point(x - t.Width / 2, y - t.Height / 2));
            }
            Tag("red", c.X + half - 12, c.Y - 8);
            Tag("green", c.X - half + 14, c.Y - 8);
            Tag("yellow", c.X + 22, c.Y - half + 6);
            Tag("blue", c.X + 16, c.Y + half - 6);
            foreach (var s in _all.OrderBy(s => _distinct.Contains(s)))
            {
                var p = new Point(c.X + s.A / range * half, c.Y - s.B / range * half);
                var (rr, gg, bb) = ColourScience.LabToSrgb(s.Colour);
                var fill = new SolidColorBrush(Color.FromRgb(rr, gg, bb), s.Use ? 1 : 0.3);
                ctx.DrawEllipse(fill, _distinct.Contains(s) ? new Pen(Brushes.White, 1.5) : null, p, 5, 5);
            }
        }
    }
}
