using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using PhotonCanvas.Addon.Slicer;
using PhotonCanvas.App;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using static PhotonCanvas.App.UiKit;
using Tri = PhotonCanvas.Doc.Import.StlDepth.Triangle;

namespace PhotonCanvas.Addon.Topo;

/// <summary>
/// Topo map stacks: find a place (or open an elevation file), choose the area, the map's shape
/// and size and how many layers of material, and see the layers in colour. Make the layers hands
/// the terrain, as a solid, to 3D to slices, which cuts it into numbered parts with marks, pins and
/// a booklet. Also: the contour lines to engrave flat, or the terrain as an STL.
/// </summary>
public sealed class TopoWindow : Window
{
    readonly Document _doc;
    readonly MachineProfile _machine;
    readonly AppSettings? _settings;
    readonly PhotonCanvas.Doc.Materials.MaterialLibrary? _library;
    readonly Action? _redraw;
    readonly HttpClient _http;
    readonly TerrainTiles _tiles;

    // Where the heights are from.
    DemGrid? _grid;
    DemGrid? _fileGrid; // a file's heights as read (its size on the ground can be set by hand)
    bool _fromFile;
    string _name = "";
    bool _searched;
    TopoSurface? _surface;
    List<Tri>? _mesh;
    CancellationTokenSource? _fetchCts, _workCts;
    int _work;
    readonly DispatcherTimer _later = new() { Interval = TimeSpan.FromMilliseconds(220) };
    readonly DispatcherTimer _refetch = new() { Interval = TimeSpan.FromMilliseconds(500) };
    bool _quiet;

    readonly TopoPreview _preview = new() { MinHeight = 420 };
    readonly TextBox _find = new() { Watermark = "A place, coordinates, or a map link", HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly Button _findButton = Button("Find", tip: "Look the place up (OpenStreetMap) or read the coordinates or map link, and get the heights there (Terrain Tiles on AWS).");
    readonly ComboBox _results = new() { HorizontalAlignment = HorizontalAlignment.Stretch, IsVisible = false };
    List<Place> _found = [];
    readonly Button _openFile = Button("Open an elevation file…", tip: "GeoTIFF (.tif), SRTM (.hgt), ESRI ASCII grid (.asc), or a heightmap picture (.png), from the USGS National Map, Copernicus, OpenTopography and the like.");
    readonly TextBlock _where = new() { TextWrapping = TextWrapping.Wrap, Text = "No place yet." };
    readonly NumericUpDown _areaKm = Number(0.1m, 2000, 1, "0.##");
    readonly NumericUpDown _fileKm = Number(0.01m, 5000, 1, "0.##");
    Control _fileKmRow = null!, _unitsRow = null!;
    readonly ComboBox _units = new() { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = new[] { "Metres", "Feet", "Just levels (no units)" }, SelectedIndex = 0 };

    readonly ComboBox _frame = new() { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = new[] { "Rectangle", "Circle", "Hexagon" }, SelectedIndex = 0 };
    readonly NumericUpDown _width = Number(10, 2000, 10, "0.#"), _depth = Number(10, 2000, 10, "0.#");
    Control _depthRow = null!;
    readonly NumericUpDown _thick = Number(0.3m, 30, 0.5m, "0.##");
    readonly NumericUpDown _layers = Number(1, 120, 1, "0"), _base = Number(0, 10, 1, "0");
    readonly ComboBox _bottom = new() { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = new[] { "The lowest point", "Sea level (the sea stays flat)", "A height I choose" }, SelectedIndex = 0 };
    readonly NumericUpDown _bottomHeight = Number(-11000, 9000, 10, "0.#");
    Control _bottomHeightRow = null!;
    readonly Slider _smooth = new() { Minimum = 0, Maximum = 4, Value = 1, TickFrequency = 0.25, IsSnapToTickEnabled = true };
    readonly ComboBox _detail = new() { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = new[] { "Coarse (quick)", "Normal", "Fine" }, SelectedIndex = 1 };
    static readonly int[] Details = [180, 300, 450];

    readonly TextBlock _summary = new() { TextWrapping = TextWrapping.Wrap };
    readonly TextBlock _credit = new() { TextWrapping = TextWrapping.Wrap, Classes = { "hint" }, FontSize = 11 };
    readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Classes = { "hint" } };
    readonly Button _make = new() { Content = "Make the layers…", Classes = { "primary" }, IsEnabled = false, MinWidth = 150 };
    readonly Button _contours = Button("Contour lines", tip: "The map flat instead: its contour lines (one at each layer's height) to engrave, and its outline to cut, on new layers of this design.");
    readonly Button _stl = Button("Save as STL…", tip: "The terrain as a 3D model (millimetres, as sized here), for a 3D printer, or to open in 3D to slices later.");

    /// <param name="tileFolder">Where fetched height tiles are kept (null: not kept).</param>
    /// <param name="redraw">Redraws the canvas after contour lines are added.</param>
    public TopoWindow(Document doc, MachineProfile machine, AppSettings? settings, PhotonCanvas.Doc.Materials.MaterialLibrary? library, HttpClient http, string? tileFolder, Action? redraw = null)
    {
        _doc = doc;
        _machine = machine;
        _settings = settings;
        _library = library;
        _redraw = redraw;
        _http = http;
        _tiles = new TerrainTiles(http, tileFolder);
        Title = "Topo map";
        Width = 1140;
        Height = 780;
        MinWidth = 880;
        MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");

        double room = Math.Min(machine.FieldW, machine.FieldH);
        _width.Value = (decimal)Math.Clamp(Math.Floor(room * 0.8 / 10) * 10, 50, 300);
        _depth.Value = _width.Value;
        _thick.Value = 3;
        _layers.Value = 10;
        _base.Value = 1;
        _areaKm.Value = 10;
        _fileKm.Value = 10;
        _preview.Message = "Find a place to start: a mountain, a lake, an island, your town. Or open an elevation file.";

        _findButton.Click += async (_, _) => await Find();
        _find.KeyDown += async (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; await Find(); } };
        _results.SelectionChanged += async (_, _) =>
        {
            if (_quiet || _results.SelectedIndex < 0 || _results.SelectedIndex >= _found.Count) return;
            await GoTo(_found[_results.SelectedIndex]);
        };
        _openFile.Click += async (_, _) => await OpenFile();
        _areaKm.ValueChanged += (_, _) => { if (!_quiet) AreaSizeTyped(); };
        _fileKm.ValueChanged += (_, _) => { if (!_quiet) FileSize(); };
        _units.SelectionChanged += (_, _) => { if (!_quiet) FileSize(); };
        _frame.SelectionChanged += (_, _) => { _depthRow.IsVisible = _frame.SelectedIndex == 0; ShapeChanged(); };
        foreach (var n in new[] { _width, _depth }) n.ValueChanged += (_, _) => { if (!_quiet) ShapeChanged(); };
        foreach (var n in new[] { _thick, _layers, _base, _bottomHeight }) n.ValueChanged += (_, _) => Later();
        _bottom.SelectionChanged += (_, _) => { _bottomHeightRow.IsVisible = _bottom.SelectedIndex == 2; Later(); };
        _smooth.PropertyChanged += (_, e) => { if (e.Property == Avalonia.Controls.Primitives.RangeBase.ValueProperty) Later(); };
        _detail.SelectionChanged += (_, _) => { if (_fromFile) Later(); else Refetch(force: true); };
        _later.Tick += (_, _) => { _later.Stop(); _ = Remake(); };
        _refetch.Tick += (_, _) => { _refetch.Stop(); _ = Fetch(); };
        _preview.AreaChanged += final =>
        {
            ShowAreaSize();
            if (final && !_fromFile && NeedsFetch()) Refetch(force: false);
            else Later();
        };
        _make.Click += async (_, _) => await MakeLayers();
        _contours.Click += async (_, _) => await Contours();
        _stl.Click += async (_, _) => await SaveStl();
        _contours.IsEnabled = _stl.IsEnabled = false;
        Closing += (_, _) => { _fetchCts?.Cancel(); _workCts?.Cancel(); _later.Stop(); _refetch.Stop(); };

        var findRow = new Grid { ColumnDefinitions = new("*,8,Auto") };
        findRow.Children.Add(_find);
        Grid.SetColumn(_findButton, 2);
        findRow.Children.Add(_findButton);

        var side = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                Explained("Place", "Type a place and press Find, or paste coordinates or a link from a web map.",
                    findRow, _results, _openFile, _where,
                    Field("Area across (km)", _areaKm, tip: "How much ground the map covers, west to east. The wheel over the picture changes it too."),
                    _fileKmRow = Field("Picture across (km)", _fileKm, tip: "A heightmap picture doesn't say how big it is on the ground: the whole picture's width, west to east."),
                    _unitsRow = Field("Heights in", _units, tip: "What the file's heights are measured in. Only changes the heights shown and how much taller than true the hills come out."),
                    Hint("Drag the outline in the picture to move the area.")),
                Explained("Model", "The map's shape and size, and the material the layers are cut from.",
                    Field("Shape", _frame),
                    Field("Width (mm)", _width, tip: "Across, west to east. A circle's diameter; a hexagon's width corner to corner."),
                    _depthRow = Field("Depth (mm)", _depth, tip: "Front to back, south to north."),
                    Field("Material (mm)", _thick, tip: "The thickness of the sheets: one layer each."),
                    Field("Land layers", _layers, tip: "Layers from the bottom height up to the highest point. More layers: taller hills and more cutting."),
                    Field("Base layers", _base, tip: "Whole layers of the map's shape underneath everything, for it to stand on."),
                    Field("Start from", _bottom, tip: "The height the first land layer starts from. Sea level keeps the sea flat (heights below it are left out)."),
                    _bottomHeightRow = Field("Start height", _bottomHeight, tip: "In the heights' own units (metres, or feet for a file in feet). Everything below it is left on the base."),
                    Field("Smooth", _smooth, tip: "Evens out the ground first, so layers come out as smooth shapes instead of many tiny islands and ragged edges."),
                    Field("Detail", _detail, tip: "How finely the ground is sampled: Fine follows small valleys better, Coarse slices faster.")),
            },
        };
        _fileKmRow.IsVisible = _unitsRow.IsVisible = false;
        _bottomHeightRow.IsVisible = false;

        var close = new Button { Content = "Close", IsCancel = true, MinWidth = 100 };
        close.Click += (_, _) => Close();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = new(0, 10, 0, 0), Children = { close, _stl, _contours, _make } };
        var left = new DockPanel();
        // Under the picture, always in sight: what the map comes to, what's happening, and whose heights they are.
        var under = new StackPanel { Spacing = 4, Margin = new(0, 8, 0, 0), Children = { _summary, _status, _credit } };
        DockPanel.SetDock(under, Dock.Bottom);
        left.Children.Add(under);
        left.Children.Add(new Border { CornerRadius = new(8), ClipToBounds = true, Child = _preview });
        var grid = new Grid { ColumnDefinitions = new("*,16,370") };
        grid.Children.Add(left);
        var scroll = new ScrollViewer { Content = side };
        Grid.SetColumn(scroll, 2);
        grid.Children.Add(scroll);
        var root = new DockPanel { Margin = new(16) };
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        root.Children.Add(grid);
        Content = root;
    }

    TopoOptions Options() => new()
    {
        Frame = (TopoFrame)Math.Max(0, _frame.SelectedIndex),
        WidthMm = (double)(_width.Value ?? 150),
        DepthMm = (double)(_depth.Value ?? 150),
        ThicknessMm = (double)(_thick.Value ?? 3),
        Layers = (int)(_layers.Value ?? 10),
        BaseLayers = (int)(_base.Value ?? 1),
        Bottom = (TopoBottom)Math.Max(0, _bottom.SelectedIndex),
        BottomHeight = (double)(_bottomHeight.Value ?? 0),
        Smooth = _smooth.Value,
        Detail = Details[Math.Clamp(_detail.SelectedIndex, 0, Details.Length - 1)],
    };

    /// <summary>The map's outline across its area, 0..1, Y down (for the picture).</summary>
    static List<Vec2> UnitShape(TopoOptions o) =>
        [.. TopoSurface.Shape(o).Select(p => new Vec2(p.X / o.WidthMm, 1 - p.Y / o.ModelDepthMm))];

    void Later()
    {
        _later.Stop();
        _later.Start();
    }

    // ---------------------------------------------------------------- finding a place

    async Task Find()
    {
        var text = _find.Text?.Trim() ?? "";
        if (text.Length == 0) return;
        if (Places.Parse(text) is { } p)
        {
            _results.IsVisible = false;
            await GoTo(p);
            return;
        }
        _status.Text = $"Looking for \"{text}\"…";
        _findButton.IsEnabled = false;
        try
        {
            _found = await Places.SearchAsync(_http, text);
            _searched = true;
            if (_found.Count == 0) { _status.Text = $"Nothing found called \"{text}\". Try adding the country, or paste coordinates."; return; }
            _quiet = true;
            _results.ItemsSource = _found.Select(f => f.Name).ToList();
            _results.SelectedIndex = 0;
            _quiet = false;
            _results.IsVisible = _found.Count > 1;
            ToolTip.SetTip(_results, "Other places with that name: pick the one you mean.");
            await GoTo(_found[0]);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            _status.Text = $"Couldn't search for places (is the internet connected?): {e.Message}. Coordinates (46.85, -121.76) or a map link work without searching.";
        }
        finally { _findButton.IsEnabled = true; }
    }

    async Task GoTo(Place p)
    {
        _fromFile = false;
        _fileGrid = null;
        _fileKmRow.IsVisible = _unitsRow.IsVisible = false;
        _preview.Roams = true;
        _name = Places.Short(p.Name);
        // The area: the place's own size with a margin round it, else 10 km.
        double across = p.WidthM is { } w && p.HeightM is { } h ? Math.Max(w, h * Options().Aspect) * 1.25 : p.WidthM ?? 10_000;
        across = Math.Clamp(across, 500, 1_000_000);
        _quiet = true;
        _areaKm.Value = (decimal)Math.Round(across / 1000, across < 10_000 ? 2 : 1);
        _quiet = false;
        _where.Text = $"{p.Name}\n{Places.Format(p.Lat, p.Lon)}";
        _centre = (p.Lat, p.Lon);
        _grid = null; // a new place: fetch round it, not round where the old area was
        await Fetch();
    }

    (double Lat, double Lon) _centre;

    /// <summary>The area's middle on Earth now (it may have been dragged).</summary>
    (double Lat, double Lon) AreaCentre()
    {
        if (_grid is not { Lat: { } la, Lon: { } lo } g) return _centre;
        var a = _preview.Area;
        return TerrainTiles.Offset(la, lo, a.X - g.WidthM / 2, g.HeightM / 2 - a.Y);
    }

    double AreaWidthM() => (double)(_areaKm.Value ?? 10) * 1000;

    /// <summary>Downloaded ground: the area went off it, or is so much bigger or smaller that it needs other tiles.</summary>
    bool NeedsFetch()
    {
        if (_fromFile || _grid is not { } g) return false;
        var a = _preview.Area;
        bool off = a.Left < -1 || a.Top < -1 || a.Left + a.WidthM > g.WidthM + 1 || a.Top + a.HeightM > g.HeightM + 1;
        double want = Math.Max(a.WidthM, a.HeightM) / (Options().Detail - 1);
        return off || g.Dx > want * 1.6 || g.Dx < want / 1.6;
    }

    void Refetch(bool force)
    {
        if (_fromFile || _grid is null && !force) return;
        _refetch.Stop();
        _refetch.Start();
    }

    /// <summary>The heights round the area (twice its size, so it can be moved about before more are needed).</summary>
    async Task Fetch()
    {
        if (_fromFile) return;
        var (lat, lon) = _grid is null ? _centre : AreaCentre();
        _centre = (lat, lon);
        var o = Options();
        double w = _grid is null ? AreaWidthM() : _preview.Area.WidthM, h = w / o.Aspect;
        double spacing = Math.Max(w, h) / (o.Detail - 1);
        _fetchCts?.Cancel();
        var cts = _fetchCts = new CancellationTokenSource();
        _preview.Message = _grid is null ? "Getting the heights…" : null;
        _status.Text = "Getting the heights…";
        try
        {
            var progress = new Progress<string>(t => { if (!cts.IsCancellationRequested) _status.Text = t; });
            var g = await _tiles.GridAsync(lat, lon, 2 * w, 2 * h, spacing, _name, progress, cts.Token);
            if (cts.IsCancellationRequested) return;
            SetGround(g, new TopoArea(g.WidthM / 2, g.HeightM / 2, w, h));
            _credit.Text = TerrainTiles.Credit + (_searched ? " " + Places.SearchCredit : "");
            _status.Text = "";
        }
        catch (OperationCanceledException) { }
        catch (Exception e) when (e is HttpRequestException or IOException or FormatException)
        {
            if (cts.IsCancellationRequested) return;
            _status.Text = e.Message;
            _preview.Message = _grid is null ? "Couldn't get the heights: is the internet connected?" : null;
        }
    }

    void SetGround(DemGrid g, TopoArea area)
    {
        _grid = g;
        var (gw, gh, px) = TopoPictures.Ground(g, 900);
        _preview.ShowGround(TopoPictures.Bitmap(gw, gh, px), g.WidthM, g.HeightM);
        _preview.SetArea(area, UnitShape(Options()));
        _preview.Message = null;
        ShowAreaSize();
        _ = Remake();
    }

    // ---------------------------------------------------------------- files

    async Task OpenFile()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Elevation file",
            FileTypeFilter =
            [
                new FilePickerFileType("Elevation files (GeoTIFF, SRTM, ASCII grid, heightmap)") { Patterns = ElevationFiles.Patterns },
                new FilePickerFileType("All files") { Patterns = ["*"] },
            ],
        });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path) return;
        _status.Text = $"Reading {Path.GetFileName(path)}…";
        try
        {
            var g = await Task.Run(() => ElevationFiles.Load(path));
            UseFile(g);
            _status.Text = "";
        }
        catch (Exception e) when (e is FormatException or IOException or UnauthorizedAccessException or OutOfMemoryException)
        {
            _status.Text = $"Couldn't read it: {e.Message}";
        }
    }

    /// <summary>Use heights read from a file (also how the tests and the help pictures load one).</summary>
    public void UseFile(DemGrid g)
    {
        _fetchCts?.Cancel();
        _fromFile = true;
        _fileGrid = g;
        _name = g.Name;
        _preview.Roams = false;
        _results.IsVisible = false;
        _quiet = true;
        _fileKmRow.IsVisible = !g.Georeferenced;
        _unitsRow.IsVisible = true;
        _units.SelectedIndex = (int)g.Units;
        if (!g.Georeferenced) _fileKm.Value = (decimal)Math.Round(g.WidthM / 1000, 2);
        _quiet = false;
        var (lo, hi) = g.Range();
        _where.Text = $"{g.Name} ({g.Source}): {g.Width:N0} × {g.Height:N0} points" +
                      (g.Georeferenced ? $", {g.WidthM / 1000:0.##} × {g.HeightM / 1000:0.##} km" : ", its size on the ground not known: set it below") +
                      (g.Lat is { } la && g.Lon is { } lo2 ? $"\n{Places.Format(la, lo2)}" : "") +
                      $"\nHeights {lo:N0} to {hi:N0}.";
        _credit.Text = $"Heights: {g.Name} ({g.Source}).";
        ApplyFile(fit: true);
    }

    void FileSize()
    {
        if (_fileGrid is null) return;
        ApplyFile(fit: false);
    }

    void ApplyFile(bool fit)
    {
        if (_fileGrid is not { } f) return;
        var g = f with { Units = (HeightUnits)Math.Max(0, _units.SelectedIndex) };
        if (!f.Georeferenced)
        {
            double km = (double)(_fileKm.Value ?? 10);
            double dx = km * 1000 / (f.Width - 1);
            g = g with { Dx = dx, Dy = dx };
        }
        var old = _grid;
        var area = fit || old is null ? TopoSurface.Fit(g, Options().Aspect)
            // Same part of the file as before, at the file's new size.
            : new TopoArea(_preview.Area.X / old.WidthM * g.WidthM, _preview.Area.Y / old.HeightM * g.HeightM, _preview.Area.WidthM / old.WidthM * g.WidthM, _preview.Area.HeightM / old.WidthM * g.WidthM);
        SetGround(g, area);
    }

    // ---------------------------------------------------------------- the area and the model

    void ShowAreaSize()
    {
        _quiet = true;
        _areaKm.Value = (decimal)Math.Clamp(Math.Round(_preview.Area.WidthM / 1000, _preview.Area.WidthM < 10_000 ? 2 : 1), 0.1, 2000);
        _quiet = false;
    }

    void AreaSizeTyped()
    {
        if (_grid is null) return;
        var a = _preview.Area;
        double w = AreaWidthM(), h = w / Options().Aspect;
        if (_fromFile)
        {
            double k = Math.Min(1, Math.Min(_grid.WidthM / w, _grid.HeightM / h));
            w *= k; h *= k;
            a = new TopoArea(Math.Clamp(a.X, w / 2, _grid.WidthM - w / 2), Math.Clamp(a.Y, h / 2, _grid.HeightM - h / 2), w, h);
        }
        else a = a with { WidthM = w, HeightM = h };
        _preview.SetArea(a, UnitShape(Options()));
        if (!_fromFile && NeedsFetch()) Refetch(force: false);
        else Later();
    }

    void ShapeChanged()
    {
        if (_grid is null) return;
        // The area takes the map's proportions, keeping its width.
        var o = Options();
        var a = _preview.Area;
        double w = a.WidthM, h = w / o.Aspect;
        if (_fromFile && h > _grid.HeightM) { h = _grid.HeightM; w = h * o.Aspect; }
        _preview.SetArea(new TopoArea(a.X, a.Y, w, h), UnitShape(o));
        ShowAreaSize();
        if (!_fromFile && NeedsFetch()) Refetch(force: false);
        else Later();
    }

    async Task Remake()
    {
        if (_grid is not { } g) return;
        _workCts?.Cancel();
        var cts = _workCts = new CancellationTokenSource();
        int work = ++_work;
        var o = Options();
        var area = _preview.Area;
        _mesh = null;
        try
        {
            var (surface, px) = await Task.Run(() =>
            {
                var s = TopoSurface.Make(g, area, o, cts.Token);
                return (s, TopoPictures.Layers(s));
            }, cts.Token);
            if (work != _work) return;
            _surface = surface;
            _preview.ShowLayers(TopoPictures.Bitmap(surface.Nx, surface.Ny, px));
            _summary.Text = Summary(surface, g);
            _make.IsEnabled = _contours.IsEnabled = _stl.IsEnabled = true;
        }
        catch (OperationCanceledException) { }
        catch (ArgumentException e)
        {
            if (work != _work) return;
            _surface = null;
            _preview.ShowLayers(null);
            _summary.Text = e.Message;
            _make.IsEnabled = _contours.IsEnabled = _stl.IsEnabled = false;
        }
    }

    string Summary(TopoSurface s, DemGrid g)
    {
        var o = s.Options;
        int all = o.BaseLayers + o.Layers;
        var a = s.Area;
        string size = g.Georeferenced ? $"{a.WidthM / 1000:0.##} × {a.HeightM / 1000:0.##} km of ground, at 1 : {Round(a.WidthM * 1000 / o.WidthMm):N0}. " : "";
        string ex = s.Exaggeration is { } e
            ? e >= 1.15 ? $" The hills come out {e:0.#}× taller than true{(e > 8 ? " (a lot: fewer layers, or a bigger area, looks more natural)" : "")}."
            : e <= 0.87 ? $" The hills come out flatter than true ({e:0.##}×): more layers, or a smaller area, shows more of them." : " The hills come out about as tall as they really are."
            : "";
        string fits = Math.Max(o.WidthMm, o.ModelDepthMm) > Math.Max(_machine.FieldW, _machine.FieldH) || Math.Min(o.WidthMm, o.ModelDepthMm) > Math.Min(_machine.FieldW, _machine.FieldH)
            ? $"\n⚠ {o.WidthMm:0} × {o.ModelDepthMm:0} mm is bigger than the bed ({_machine.FieldW:0} × {_machine.FieldH:0} mm): make the map smaller."
            : "";
        string bottom = o.Bottom == TopoBottom.SeaLevel && s.Low < 0 ? " The sea is the base." : "";
        return $"{all} layers of {o.ThicknessMm:0.##} mm ({o.BaseLayers} base, {o.Layers} land): {o.WidthMm:0} × {o.ModelDepthMm:0} mm, {s.StackMm:0.#} mm tall. " + size +
               $"Heights {s.Say(s.Low)} to {s.Say(s.High)}: each layer is {s.Say(s.Step)}, the first land layer starting at {s.Say(s.LevelOf(1))} and the top one at {s.Say(s.LevelOf(o.Layers))}." +
               bottom + ex + fits;
    }

    static double Round(double v)
    {
        if (!(v > 0)) return v;
        double p = Math.Pow(10, Math.Floor(Math.Log10(v)) - 1);
        return Math.Round(v / p) * p;
    }

    async Task<List<Tri>?> Mesh()
    {
        if (_surface is not { } s) return null;
        if (_mesh is { } m) return m;
        _status.Text = "Making the solid…";
        var mesh = await Task.Run(() => s.Mesh());
        _status.Text = "";
        if (ReferenceEquals(_surface, s)) _mesh = mesh;
        return mesh;
    }

    string ModelName => string.IsNullOrWhiteSpace(_name) ? "Topo map" : _name;

    /// <summary>Hands the terrain to 3D to slices: numbered parts, the next layer's outline marked, pins, booklet.</summary>
    async Task MakeLayers()
    {
        if (_surface is not { } s || await Mesh() is not { } mesh) return;
        var w = new StackSliceWindow(_doc, _machine, _settings, _library) { Title = $"Stacked slices: {ModelName}" };
        // Islands on a layer are separate parts, glued where the layer below shows their outline: no joining strips.
        w.Load(mesh, ModelName, s.Options.ThicknessMm, keepJoined: false);
        await w.ShowDialog(this);
    }

    /// <summary>The map flat: contour lines at each layer's height on a Score layer, the outline on a Cut layer, in the middle of the work area.</summary>
    async Task Contours()
    {
        if (_surface is not { } s || await Mesh() is not { } mesh) return;
        var o = s.Options;
        var heights = Enumerable.Range(1, o.Layers).Select(s.LevelOf).ToList();
        var lines = await Task.Run(() => s.Contours(mesh, heights));
        var at = new Vec2((_machine.FieldW - o.WidthMm) / 2, (_machine.FieldH - o.ModelDepthMm) / 2);
        var st = _doc.State;
        var lineLayer = Layer.Default(st.Layers.Count, LayerMode.Score) with { Name = $"Contours · {ModelName}" };
        st = Ops.AddLayer(st, lineLayer);
        var cutLayer = Layer.Default(st.Layers.Count, LayerMode.Cut) with { Name = $"Outline · {ModelName}" };
        st = Ops.AddLayer(st, cutLayer);
        var contour = new PathShape { Name = $"Contour lines · {ModelName}", LayerId = lineLayer.Id, Geometry = s.OnBed(lines, at, closed: false) };
        var outline = new PathShape { Name = $"Map outline · {ModelName}", LayerId = cutLayer.Id, Geometry = s.OnBed([s.Outline], at, closed: true) };
        _doc.Commit($"Contour lines of {ModelName}", Ops.AddShapes(st, [contour, outline]), [contour.Id, outline.Id]);
        _redraw?.Invoke();
        _status.Text = $"Added {lines.Count:N0} contour lines (layer \"{lineLayer.Name}\") and the map's outline (layer \"{cutLayer.Name}\") to the design.";
    }

    async Task SaveStl()
    {
        if (await Mesh() is not { } mesh) return;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save the terrain as STL",
            SuggestedFileName = string.Concat(ModelName.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch)) + ".stl",
            DefaultExtension = "stl",
            FileTypeChoices = [new FilePickerFileType("STL 3D model") { Patterns = ["*.stl"] }],
        });
        if (file?.TryGetLocalPath() is not { } path) return;
        try
        {
            await Task.Run(() => TopoSurface.SaveStl(mesh, path, ModelName));
            _status.Text = $"Saved {Path.GetFileName(path)} ({mesh.Count:N0} triangles).";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _status.Text = $"Couldn't save it: {e.Message}"; }
    }
}
