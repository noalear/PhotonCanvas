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
    (int R0, int C0, int R1, int C1) _section;
    (int R, int C)? _sectionFirst;
    bool _pickingSection;
    readonly Dictionary<(int, int), ColourSwatch> _byCell = [];

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

    RasterImage? _image;
    string _method = "";
    Homography? _mmToPx;
    readonly List<Vec2> _corners = [];
    readonly List<Vec2> _greyClicks = [];
    bool _pickingGrey;
    (double R, double G, double B)? _balance;
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
        _section = (0, 0, Math.Max(0, _rows - 1), Math.Max(0, _cols - 1));
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
        reset.Click += (_, _) => { _corners.Clear(); _mmToPx = null; Remeasure(); };
        ToolTip.SetTip(_grey, "Click two opposite corners of a grey or white card in the picture: its colour is taken as neutral, which corrects the light's tint.");
        _grey.Click += (_, _) => { _pickingGrey = true; _greyClicks.Clear(); UpdateStep(); };
        _view.Clicked += OnPicked;
        _distinct.ValueChanged += (_, _) => ShowResults();
        _material.ItemsSource = lib.Materials.Select(m => m.Label).Prepend("New material…").ToList();
        _material.SelectedIndex = 0;
        _material.SelectionChanged += (_, _) => _newName.IsVisible = _material.SelectedIndex == 0;
        _paletteName.Text = $"Colour test {DateTime.Now:yyyy-MM-dd}";
        _save.Click += (_, _) => Save();
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
        var top = new StackPanel { Spacing = 6, Margin = new(0, 0, 0, 8), Children = { tools, tools2, lights, _step, _status } };
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
                new TextBlock { Text = "COLOUR MAP", Classes = { "h" }, Margin = new(0, 4, 0, 0) },
                _map,
                Hint("Each dot is a square, placed by hue and saturation (grey in the middle). Ringed dots are the clearly different ones."),
                new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 8,
                    Children = { new TextBlock { Text = "Clearly different if ΔE ≥", VerticalAlignment = VerticalAlignment.Center, Classes = { "muted" } }, _distinct, _distinctText },
                },
                _verdict,
                new TextBlock { Text = "SAVE", Classes = { "h" }, Margin = new(0, 4, 0, 0) },
                Field("Material", _material),
                _newName,
                Field("Palette name", _paletteName),
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _save, _csv } },
            },
        };
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
        UpdateStep();
        ShowResults();
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
                var img = await Task.Run(() => RasterImage.Decode(jpg));
                SetImage(img, "close-up camera");
                _status.Text = $"Close-up photo, lights front {_front.Value:0}% / back {_back.Value:0}%. " +
                               (_section == (0, 0, _rows - 1, _cols - 1) ? "If it shows only part of the grid, say which squares first." : "");
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
        _section = (0, 0, _rows - 1, _cols - 1);
        SetImage(p.Image, "overview camera");
        // The camera picture is already mapped to the bed: no corners to click.
        var a = p.Area;
        Vec2 Px(Vec2 mm) => new((mm.X - a.MinX) / p.MmPerPx, (mm.Y - a.MinY) / p.MmPerPx);
        _mmToPx = Homography.Fit([.. new[] { a.TopLeft, new Vec2(a.MaxX, a.MinY), a.BottomRight, new Vec2(a.MinX, a.MaxY) }.Select(m => (m, Px(m)))]);
        _status.Text = "Using the machine's camera picture. Its colours are only a rough guide.";
        Remeasure();
    }

    void SetImage(RasterImage img, string method)
    {
        _image = img;
        _method = method;
        _corners.Clear();
        _greyClicks.Clear();
        _balance = null;
        _mmToPx = null;
        var old = _view.Bitmap;
        using (var ms = new MemoryStream(img.EncodeJpeg(2400, 92))) _view.Bitmap = new Bitmap(ms);
        old?.Dispose();
        _view.ImageSize = new(img.Width, img.Height);
        Remeasure();
    }

    /// <summary>Where the corner squares are, in the order they're clicked.</summary>
    IReadOnlyList<(string Name, ColourTestCell Cell)> CornerCells()
    {
        var (r0, c0, r1, c1) = _section;
        return [("top-left", Cell(r0, c0)), ("top-right", Cell(r0, c1)), ("bottom-right", Cell(r1, c1)), ("bottom-left", Cell(r1, c0))];
    }

    bool InSection(int r, int c) => r >= _section.R0 && r <= _section.R1 && c >= _section.C0 && c <= _section.C1;

    ColourTestCell Cell(int r, int c) => _cells.FirstOrDefault(x => x.Row == r && x.Col == c) ?? _cells[0];

    void UpdateStep()
    {
        if (_cells.Count == 0) return;
        if (_pickingSection) _step.Text = _sectionFirst is null ? "On the chart, click the first square this photo shows." : "Now the last square it shows (the opposite corner of the block).";
        else if (_image is null) _step.Text = "Put the burned test under the close-up camera and take a close-up photo (or open a scan).";
        else if (_pickingGrey) _step.Text = _greyClicks.Count == 0 ? "Click one corner of the grey card." : "Now the opposite corner.";
        else if (_mmToPx is null && _corners.Count < 4)
        {
            var (name, cell) = CornerCells()[_corners.Count];
            _step.Text = $"Click the middle of the {name} square (R{cell.Row + 1} C{cell.Col + 1}).";
        }
        else _step.Text = _byCell.Count < _cells.Count
            ? $"Measured {_byCell.Count} of {_cells.Count} squares. Check the outlines sit inside each square. For the rest, slide the plate, take another photo and say which squares it shows."
            : "All squares measured. Check the outlines sit inside each square; if not, click the corners again.";
        _view.Corners = _corners.ToList();
        _view.Grey = _greyClicks.Count == 2 ? [_greyClicks[0], _greyClicks[1]] : null;
        _view.InvalidateVisual();
    }

    void OnPicked(Vec2 px)
    {
        if (_image is null) return;
        if (_pickingGrey)
        {
            _greyClicks.Add(px);
            if (_greyClicks.Count == 2)
            {
                _pickingGrey = false;
                var (a, b) = (_greyClicks[0], _greyClicks[1]);
                _balance = ColourMeasure.WhiteBalance(_image, [new(a.X, a.Y), new(b.X, a.Y), new(b.X, b.Y), new(a.X, b.Y)]);
                if (!_method.Contains("grey card")) _method += " with grey card";
                Remeasure();
            }
            UpdateStep();
            return;
        }
        if (_mmToPx is not null || _corners.Count >= 4) return;
        _corners.Add(px);
        if (_corners.Count == 4)
        {
            // Corner squares' centres in the design ↔ where they were clicked.
            var pairs = CornerCells().Select((c, i) => (c.Cell.Bounds.Center, _corners[i])).ToList();
            _mmToPx = Homography.Fit(pairs);
            if (_mmToPx is null) { _corners.Clear(); _status.Text = "Those four points don't make a grid: click the corners again."; }
            Remeasure();
        }
        UpdateStep();
    }

    void Remeasure()
    {
        // The squares in this photo are (re)measured; ones measured from earlier photos stay.
        if (_image is { } img && _mmToPx is { } h)
            foreach (var s in ColourMeasure.Measure(img, h, _cells.Where(c => InSection(c.Row, c.Col)).ToList(), _balance))
                _byCell[(s.Row, s.Col)] = s;
        _measured = _byCell.Values.OrderBy(s => s.Row).ThenBy(s => s.Col).ToList();
        // Outlines of the areas read, so a wrong click shows.
        _view.Regions = _mmToPx is { } m
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
                BorderThickness = new(1), BorderBrush = InSection(r, c) ? Brush("Accent") : Brush("Line"),
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
                BorderThickness = new(distinct.Contains(s) ? 2 : 0), BorderBrush = Brushes.White,
                Child = s.Spread > ColourMeasure.BlotchySpread ? new TextBlock { Text = "!", FontWeight = FontWeight.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } : null,
                Cursor = new Cursor(StandardCursorType.Hand),
            };
            ToolTip.SetTip(face, $"R{s.Row + 1} C{s.Col + 1}: {s.Settings.Summary()}\nL {s.L:0.0}  a {s.A:0.0}  b {s.B:0.0}  ·  evenness ΔE {s.Spread:0.0}{(s.Use ? "" : "  ·  left out")}");
            face.PointerPressed += (_, _) =>
            {
                if (_pickingSection) { PickSection(sw.Row, sw.Col); return; }
                _byCell[(sw.Row, sw.Col)] = sw with { Use = !sw.Use };
                _measured = _byCell.Values.OrderBy(x => x.Row).ThenBy(x => x.Col).ToList();
                ShowResults();
            };
            Grid.SetRow(face, s.Row);
            Grid.SetColumn(face, s.Col);
            _swatches.Children.Add(face);
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
        _section = (Math.Min(first.R, r), Math.Min(first.C, c), Math.Max(first.R, r), Math.Max(first.C, c));
        _sectionFirst = null;
        _pickingSection = false;
        _corners.Clear();
        _mmToPx = null;
        _view.Regions = [];
        _status.Text = $"This photo shows R{_section.R0 + 1}–{_section.R1 + 1}, C{_section.C0 + 1}–{_section.C1 + 1}: click its four corner squares.";
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
        var palette = new ColourPalette
        {
            Name = string.IsNullOrWhiteSpace(_paletteName.Text) ? $"Colour test {DateTime.Now:yyyy-MM-dd}" : _paletteName.Text.Trim(),
            FocusMm = _focus,
            Method = _machine is not null && _method.Contains("camera") ? $"{_method}, lights front {_front.Value:0}% / back {_back.Value:0}%" : _method,
            Swatches = [.. _measured],
        };
        _lib.Upsert(m with { Palettes = m.Palettes.Add(palette) });
        _lib.Save();
        _material.ItemsSource = _lib.Materials.Select(x => x.Label).Prepend("New material…").ToList();
        _material.SelectedIndex = _lib.Materials.FindIndex(x => x.Id == m.Id) + 1;
        _status.Text = $"Saved “{palette.Name}” to {m.Label}: {palette.Swatches.Count} squares, {ColourMeasure.Distinct(palette.Swatches, _distinct.Value).Count} clearly different colours.";
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
        var sb = new StringBuilder("row,col,power_pct,speed_mm_s,lines_per_cm,frequency_khz,pulse_ns,L,a,b,sRGB,evenness_dE,used\n");
        foreach (var s in _measured)
        {
            var (r, g, b) = ColourScience.LabToSrgb(s.Colour);
            var l = s.Settings.Laser;
            sb.AppendLine(string.Join(",", s.Row + 1, s.Col + 1, l.Power.ToString(inv), l.Speed.ToString(inv), s.Settings.LinesPerCm.ToString(inv), l.FrequencyKhz, l.PulseWidthNs,
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
