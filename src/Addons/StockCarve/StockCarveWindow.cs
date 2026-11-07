using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using PhotonCanvas.Addon.RotaryFinish;
using PhotonCanvas.App;
using PhotonCanvas.App.Addons;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Import;
using PhotonCanvas.Doc.Materials;
using static PhotonCanvas.App.UiKit;
using Tri = PhotonCanvas.Doc.Import.StlDepth.Triangle;

namespace PhotonCanvas.Addon.StockCarve;

/// <summary>What the window had last time (kept in the add-on's data folder).</summary>
public sealed record StockCarveSaved
{
    public string? StockFile { get; init; }
    public string? ModelFile { get; init; }
    public Placement Placement { get; init; } = new();
    public CarveOptions Carve { get; init; } = new();
    public bool KeepSize { get; init; } = true;
    public double SpareMm { get; init; } = 0.2;
    public bool ShowStock { get; init; } = true;
}

/// <summary>
/// Carve stock to a model: the scanned part (or a model of it), the model to carve out of it, placed by hand
/// or by Best fit, shown together in 3D with the model red where it sticks out of the stock, and a page that
/// engraves the part down to the model on the rotary.
/// </summary>
public sealed class StockCarveWindow : Window
{
    readonly IAddonHost _host;
    StockMap? _stock;
    TargetModel? _model;
    string? _stockFile, _modelFile;
    List<Tri>? _placed;
    FitReport? _report;
    bool _loading;
    CancellationTokenSource? _work;

    readonly TextBlock _stockNote = new() { Classes = { "hint" }, TextWrapping = TextWrapping.Wrap }, _modelNote = new() { Classes = { "hint" }, TextWrapping = TextWrapping.Wrap };
    readonly NumericUpDown _turnX = Angle(), _turnY = Angle(), _turnZ = Angle(), _spin = Angle();
    readonly NumericUpDown _size = Number(1, 5000, 1, "0.#"), _along = Number(-1000, 1000, 0.5m, "0.##"), _side = Number(-500, 500, 0.25m, "0.##"), _up = Number(-500, 500, 0.25m, "0.##");
    readonly CheckBox _keepSize = new() { Content = "Keep the model's size", IsChecked = true };
    readonly NumericUpDown _spare = Number(0, 10, 0.05m, "0.0#");
    readonly Button _fit = UiKit.Button("Best fit", tip: "Turns the model round the axis and moves it so it sticks out of the stock as little as possible, with the room to spare as even as it can be. Your own turns (X, Y, Z) are kept.");
    readonly ComboBox _with = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly NumericUpDown _pass = Number(0.01m, 2, 0.01m, "0.0##"), _band = Number(0.2m, 5, 0.1m, "0.#"), _deepest = Number(0.2m, 30, 0.5m, "0.#"),
        _leave = Number(0, 3, 0.05m, "0.0#"), _core = Number(0, 50, 0.5m, "0.#"), _detail = Number(0.1m, 2, 0.05m, "0.0#");
    readonly CheckBox _pastEnds = new() { Content = "Carve past the model's ends too" };
    readonly CheckBox _showStock = new() { Content = "Show the stock", IsChecked = true };
    readonly List<(Material M, Recipe R)> _etches;
    readonly FitView _view = new();
    readonly TextBlock _summary = new() { TextWrapping = TextWrapping.Wrap };
    readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Classes = { "muted" } };
    readonly Button _make = new() { Content = "Put the engraving in the design", Classes = { "primary" }, MinWidth = 200 };
    readonly Button _profile = UiKit.Button("Profile cuts first…", tip: "Takes the bulk of the waste off first: the part turned to each view (top, side, the 45s, the 22.5s) and cut straight down through round the model's outline, the camera checking each cut went through.");
    ProfileCutWindow? _profileWindow;
    readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(150) };

    static NumericUpDown Angle() => Number(-360, 360, 5, "0.#");

    public StockCarveWindow(IAddonHost host)
    {
        _host = host;
        Title = "Carve stock to a model";
        Width = 1150;
        Height = 800;
        MinWidth = 880;
        MinHeight = 580;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");

        _etches = [.. (host.Library?.Materials ?? []).SelectMany(m => m.Recipes.Where(r => r.EtchMmPerPass is > 0 && r.Settings.Mode == LayerMode.Fill).Select(r => (m, r)))];
        _with.ItemsSource = _etches.Select(x => $"{x.M.Label} · {x.R.Title} ({x.R.EtchMmPerPass:0.###} mm a pass)").Prepend("Default engrave settings").ToList();
        _with.SelectedIndex = _etches.Count > 0 ? 1 : 0;

        var openStock = UiKit.Button("Last scan", tip: "The part as Machine → Scan the part last measured it, in the rotary now.");
        var stockFile = UiKit.Button("Stock model…", tip: "A 3D model of the stock in the rotary's frame (the STL Scan the part saves): X along the axis, the axis at Y = Z = 0, +Z up at the start.");
        var openModel = UiKit.Button("Open a 3D model…", tip: "The shape to carve out of the stock (STL, OBJ or STEP).");
        openStock.Click += (_, _) => UseLastScan();
        stockFile.Click += async (_, _) => { if (await PickModel("Stock model") is { } p) UseStockFile(p); };
        openModel.Click += async (_, _) => { if (await PickModel("Model to carve") is { } p) UseModelFile(p, fresh: true); };

        Button Quarter(NumericUpDown n, string axis)
        {
            var b = UiKit.Button($"{axis} +90°", tip: $"Turns the model a quarter turn about its own {axis} axis.");
            b.Click += (_, _) => n.Value = (decimal)Wrap180((double)(n.Value ?? 0) + 90);
            return b;
        }

        var left = new StackPanel
        {
            // A set width: the long hints otherwise measure wider than the scroller shows, and the cards run under its bar.
            Width = 404,
            Spacing = 12,
            Children =
            {
                Explained("Stock", "The part in the rotary, as scanned. Keep it in the chuck from the scan to the carving.",
                    Row(openStock, stockFile), _stockNote),
                Explained("Model", "What to carve out of the stock.", openModel, _modelNote),
                Explained("Place it", "Turn, size and move the model to where it should be in the stock. Red on the model sticks out of the stock.",
                    Field("Quarter turn", Row(Quarter(_turnX, "X"), Quarter(_turnY, "Y"), Quarter(_turnZ, "Z"))),
                    Field("Turn X (°)", _turnX, tip: "Turns the model about its own X axis (through its middle). Its X runs along the rotary's axis."),
                    Field("Turn Y (°)", _turnY, tip: "Turns the model about its own Y axis."),
                    Field("Turn Z (°)", _turnZ, tip: "Turns the model about its own Z axis."),
                    Field("Size (%)", _size, tip: "The model's size: 100 % = as modelled."),
                    Field("Round axis (°)", _spin, tip: "Turns the model round the rotary's axis, the way a design runs down the page."),
                    Field("Along (mm)", _along, tip: "Where the model's middle is along the axis: the design's X."),
                    Field("Off axis, side (mm)", _side, tip: "Moves the model's middle off the axis, sideways (as the part sits at the start)."),
                    Field("Off axis, up (mm)", _up, tip: "Moves the model's middle off the axis, up towards the laser (as the part sits at the start).")),
                Explained("Best fit", "Finds the place where the model fits the stock best.",
                    Row(_fit, _keepSize),
                    Field("Room to spare (mm)", _spare, tip: "Best fit tries to keep at least this much stock over the model everywhere.")),
                Explained("Carve", "The engraving that takes the stock down to the model.",
                    Field("Engrave with", _with, tip: "Settings from the material library with a measured depth a pass (Materials & Tests → Tests → Etch rate test). Every focus height uses them."),
                    Field("Depth a pass (mm)", _pass, tip: "How deep one pass goes on a flat piece with these settings. Nearer the axis the lines are closer together, so the passes go deeper there: that's allowed for."),
                    Field("Focus band (mm)", _band, tip: "How far the surface may be from the focus. The part isn't round, so its surface is split into bands by how far out it is, each with its own focus height."),
                    Field("Deepest (mm)", _deepest, tip: "Never engraves deeper than this anywhere; spots that need more are left."),
                    Field("Leave on (mm)", _leave, tip: "Stops this far short of the model, to sand off."),
                    Field("Core kept (mm)", _core, tip: "Never carves closer to the axis than this, so the part stays strong in the chuck."),
                    Field("Detail (mm)", _detail, tip: "How finely the surface is mapped. Smaller follows the shape closer but takes longer to make."),
                    _pastEnds),
                _showStock,
            },
        };
        ToolTip.SetTip(_keepSize, "On: Best fit only turns and moves the model. Off: it also makes it as big as fits in the stock.");
        ToolTip.SetTip(_pastEnds, "Off: only the stock between the model's ends is carved (the rest, with the end in the chuck, is left). On: the stock past its ends is carved down to the core too.");
        var leftScroll = new ScrollViewer
        {
            Width = 440, Padding = new Thickness(14, 10, 16, 10), Content = left,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };

        var right = new Grid { RowDefinitions = new("*,Auto"), Margin = new Thickness(0, 10, 14, 0), RowSpacing = 8 };
        right.Children.Add(new Border { BorderBrush = Brush("Line"), BorderThickness = new(1), CornerRadius = new(6), ClipToBounds = true, Child = _view });
        var sumScroll = new ScrollViewer { Content = _summary, MaxHeight = 150, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(sumScroll, 1);
        right.Children.Add(sumScroll);

        var bottom = new Grid { ColumnDefinitions = new("*,Auto,Auto,Auto"), Margin = new Thickness(14, 8, 14, 14), ColumnSpacing = 12 };
        bottom.Children.Add(_status);
        Grid.SetColumn(_profile, 1);
        bottom.Children.Add(_profile);
        Grid.SetColumn(_make, 2);
        bottom.Children.Add(_make);
        var close = new Button { Content = "Close", IsCancel = true, MinWidth = 100 };
        close.Click += (_, _) => Close();
        Grid.SetColumn(close, 3);
        bottom.Children.Add(close);

        var body = new Grid { ColumnDefinitions = new("Auto,*"), RowDefinitions = new("*,Auto") };
        body.Children.Add(leftScroll);
        Grid.SetColumn(right, 1);
        body.Children.Add(right);
        Grid.SetRow(bottom, 1);
        Grid.SetColumnSpan(bottom, 2);
        body.Children.Add(bottom);
        Content = body;

        _debounce.Tick += (_, _) => { _debounce.Stop(); Update(); };
        foreach (var n in new[] { _turnX, _turnY, _turnZ, _size, _spin, _along, _side, _up }) n.ValueChanged += (_, _) => Changed();
        foreach (var n in new[] { _pass, _band, _deepest, _leave, _core, _detail, _spare }) n.ValueChanged += (_, _) => Save();
        _pastEnds.IsCheckedChanged += (_, _) => Save();
        _keepSize.IsCheckedChanged += (_, _) => Save();
        _showStock.IsCheckedChanged += (_, _) => { _view.ShowStock = _showStock.IsChecked == true; Save(); };
        _with.SelectionChanged += (_, _) => { if (Etch() is { R.EtchMmPerPass: { } e }) _pass.Value = (decimal)e; Save(); };
        _fit.Click += async (_, _) => await BestFit();
        _make.Click += async (_, _) => await Make();
        _profile.Click += (_, _) => OpenProfileCuts();
        Closed += (_, _) => { _work?.Cancel(); _profileWindow?.Close(); Save(); };

        Load();
    }

    (Material M, Recipe R)? Etch() => _with.SelectedIndex >= 1 && _with.SelectedIndex <= _etches.Count ? _etches[_with.SelectedIndex - 1] : null;

    static double Wrap180(double d) => ((d % 360) + 540) % 360 - 180;

    async Task<string?> PickModel(string title)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            FileTypeFilter =
            [
                new FilePickerFileType("3D models") { Patterns = [.. StlDepth.Extensions.Select(e => "*" + e)] },
                new FilePickerFileType("All files") { Patterns = ["*"] },
            ],
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    void UseLastScan()
    {
        var (stock, note) = ScanLink.LastScan(_host.AppSettings);
        if (stock is null) { _stockNote.Text = note; return; }
        SetStock(stock, null, note);
    }

    void UseStockFile(string path)
    {
        try
        {
            var stock = StockMap.FromMesh(StlDepth.Load(path));
            SetStock(stock, path, $"{Path.GetFileName(path)}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException or ArgumentException)
        {
            _stockNote.Text = $"Couldn't read it: {e.Message}";
        }
    }

    void SetStock(StockMap stock, string? file, string note)
    {
        bool first = _stock is null;
        _stock = stock;
        _stockFile = file;
        _stockNote.Text = $"{note}. {stock.LengthMm:0.#} mm long (X {stock.MinX:0.#} to {stock.MaxX:0.#}), up to {2 * stock.MaxRadius:0.#} mm across" +
                          (stock.WholeTurn ? "." : ", part of a turn (only the angles scanned are carved).");
        // A model placed before there was any stock: put it in the stock's middle.
        if (first && _model is not null && !_loading) _along.Value = (decimal)Math.Round((stock.MinX + stock.MaxX) / 2, 2);
        Changed();
    }

    void UseModelFile(string path, bool fresh)
    {
        try
        {
            var model = new TargetModel(StlDepth.Load(path));
            _model = model;
            _modelFile = path;
            _modelNote.Text = $"{Path.GetFileName(path)}: {model.Size.X:0.#} × {model.Size.Y:0.#} × {model.Size.Z:0.#} mm, {model.Mesh.Count:N0} triangles";
            if (fresh) SetPlacement(model.StartIn(_stock));
            Changed();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException or ArgumentException)
        {
            _modelNote.Text = $"Couldn't read it: {e.Message}";
        }
    }

    Placement Placement() => new()
    {
        TurnXDeg = (double)(_turnX.Value ?? 0), TurnYDeg = (double)(_turnY.Value ?? 0), TurnZDeg = (double)(_turnZ.Value ?? 0),
        Scale = (double)(_size.Value ?? 100) / 100, SpinDeg = (double)(_spin.Value ?? 0),
        AlongMm = (double)(_along.Value ?? 0), SideMm = (double)(_side.Value ?? 0), UpMm = (double)(_up.Value ?? 0),
    };

    void SetPlacement(Placement p)
    {
        bool was = _loading;
        _loading = true;
        _turnX.Value = (decimal)Math.Round(p.TurnXDeg, 1);
        _turnY.Value = (decimal)Math.Round(p.TurnYDeg, 1);
        _turnZ.Value = (decimal)Math.Round(p.TurnZDeg, 1);
        _size.Value = (decimal)Math.Clamp(Math.Round(p.Scale * 100, 1), 1, 5000);
        _spin.Value = (decimal)Math.Round(Wrap180(p.SpinDeg), 1);
        _along.Value = (decimal)Math.Clamp(Math.Round(p.AlongMm, 2), -1000, 1000);
        _side.Value = (decimal)Math.Clamp(Math.Round(p.SideMm, 2), -500, 500);
        _up.Value = (decimal)Math.Clamp(Math.Round(p.UpMm, 2), -500, 500);
        _loading = was;
    }

    CarveOptions Options() => new()
    {
        PassDepthMm = (double)(_pass.Value ?? 0.1m), FocusBandMm = (double)(_band.Value ?? 1), MaxDepthMm = (double)(_deepest.Value ?? 5),
        LeaveMm = (double)(_leave.Value ?? 0), CoreMm = (double)(_core.Value ?? 2), DetailMm = (double)(_detail.Value ?? 0.25m),
        PastEnds = _pastEnds.IsChecked == true,
    };

    void Changed()
    {
        if (_loading) return;
        _debounce.Stop();
        _debounce.Start();
    }

    /// <summary>Places the model, compares it with the stock, and shows it.</summary>
    void Update()
    {
        Save();
        _placed = _model?.Placed(Placement());
        _report = _stock is not null && _placed is not null ? StockCarve.Compare(_stock, _placed) : null;
        string? message = _stock is null && _model is null ? "Choose the stock (the last scan, or a model of it) and the model to carve out of it." : null;
        _view.Show(_stock, _placed, _report?.StickOut, message);
        _summary.Text = Summary();
        _make.IsEnabled = _stock is not null && _model is not null;
        _fit.IsEnabled = _profile.IsEnabled = _make.IsEnabled;
    }

    /// <summary>The profile cuts for the model where it's placed now, in the stock as it is now.</summary>
    void OpenProfileCuts()
    {
        if (_stock is not { } stock || _model is not { } model) return;
        if (_profileWindow is { } w) { w.Activate(); return; }
        if (!stock.WholeTurn)
        {
            _status.Text = "Profile cuts need the stock all the way round: scan a whole turn, or pick the standard bar it is.";
            return;
        }
        var p = Placement();
        _profileWindow = new ProfileCutWindow(_host, stock, model.Placed(p), p, _modelFile, (rest, note) => SetStock(rest, null, note));
        _profileWindow.Closed += (_, _) => _profileWindow = null;
        _profileWindow.Show(this);
    }

    string Summary()
    {
        if (_stock is null) return "No stock yet: click Last scan (after Machine → Scan the part), or open a model of the stock.";
        if (_model is null || _report is null) return "Open the model to carve out of the stock.";
        var r = _report;
        string s;
        if (double.IsNaN(r.WorstMm)) s = "The model isn't anywhere the stock was measured.";
        else if (r.Fits) s = $"✓ The model fits inside the stock, with {-r.WorstMm:0.##} mm to spare at the tightest spot.";
        else s = $"⚠ The model sticks out of the stock by up to {r.WorstMm:0.##} mm, over {r.OutsideMm2 / 100:0.##} cm² ({100 * r.OutsideMm2 / Math.Max(1e-9, r.AreaMm2):0.#} % of its surface; red). Those spots won't be there after carving: move or turn it, try Best fit, or make it smaller.";
        if (r.PastEnds) s += " Some of the model is past the ends of the stock.";
        return s;
    }

    async Task BestFit()
    {
        if (_stock is not { } stock || _model is not { } model) return;
        _work?.Cancel();
        var cts = _work = new CancellationTokenSource();
        var start = Placement();
        bool keep = _keepSize.IsChecked == true;
        double spare = (double)(_spare.Value ?? 0);
        _fit.IsEnabled = _make.IsEnabled = false;
        _status.Text = keep ? "Finding the best fit…" : "Finding the best fit and the biggest size that fits…";
        try
        {
            var p = await Task.Run(() => StockCarve.BestFit(stock, model, start, keep, spare, cts.Token));
            if (cts.IsCancellationRequested) return;
            SetPlacement(p);
            Update();
            _status.Text = keep ? "Best fit: turned and moved, the same size." : $"Best fit: turned, moved and sized to {p.Scale * 100:0.#} %.";
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (_work == cts) _work = null;
            _fit.IsEnabled = _make.IsEnabled = _stock is not null && _model is not null;
        }
    }

    async Task Make()
    {
        if (_stock is not { } stock || _model is not { } model) return;
        _work?.Cancel();
        var cts = _work = new CancellationTokenSource();
        var placed = model.Placed(Placement());
        var o = Options();
        var engrave = Etch() is { } e
            ? e.R.Settings.ApplyTo(Layer.Default(0, LayerMode.Fill)) with { Material = new MaterialRef(e.M.Id, e.M.Label, e.R.Id, e.R.Title) }
            : Layer.Default(0, LayerMode.Fill);
        _fit.IsEnabled = _make.IsEnabled = false;
        _status.Text = "Working out the engraving…";
        try
        {
            var plan = await Task.Run(() => StockCarve.Plan(stock, placed, o, cts.Token));
            if (cts.IsCancellationRequested) return;
            var text = StockCarve.Describe(plan, o);
            if (plan.Bands.Count == 0)
            {
                _status.Text = text;
                return;
            }
            string name = $"{(_modelFile is null ? "Model" : Path.GetFileNameWithoutExtension(_modelFile))} carved on the rotary";
            var doc = _host.Document;
            doc.Commit("Carve stock to a model", Pages.Insert(doc.State, [StockCarve.Page(plan, engrave, name)]));
            _host.InvalidateCanvas();
            _summary.Text = Summary() + "\n\n" + text;
            _status.Text = $"Added the page \"{name}\".";
        }
        catch (ArgumentException ex) { _status.Text = ex.Message; }
        catch (OperationCanceledException) { }
        finally
        {
            if (_work == cts) _work = null;
            _fit.IsEnabled = _make.IsEnabled = _stock is not null && _model is not null;
        }
    }

    string SavedPath => Path.Combine(_host.DataFolder, "last.json");

    void Load()
    {
        _loading = true;
        try
        {
            var saved = File.Exists(SavedPath) ? JsonSerializer.Deserialize<StockCarveSaved>(File.ReadAllText(SavedPath)) : null;
            saved ??= new StockCarveSaved();
            SetPlacement(saved.Placement);
            var c = saved.Carve;
            _pass.Value = (decimal)c.PassDepthMm; _band.Value = (decimal)c.FocusBandMm; _deepest.Value = (decimal)c.MaxDepthMm;
            _leave.Value = (decimal)c.LeaveMm; _core.Value = (decimal)c.CoreMm; _detail.Value = (decimal)c.DetailMm;
            _pastEnds.IsChecked = c.PastEnds;
            _keepSize.IsChecked = saved.KeepSize;
            _spare.Value = (decimal)saved.SpareMm;
            _showStock.IsChecked = saved.ShowStock;
            _view.ShowStock = saved.ShowStock;
            if (saved.StockFile is { } sf && File.Exists(sf)) UseStockFile(sf);
            else
            {
                var (stock, note) = ScanLink.LastScan(_host.AppSettings);
                if (stock is not null) SetStock(stock, null, note);
                else _stockNote.Text = note;
            }
            if (saved.ModelFile is { } mf && File.Exists(mf)) UseModelFile(mf, fresh: false);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or ArgumentException) { }
        finally { _loading = false; }
        Update();
    }

    void Save()
    {
        if (_loading) return;
        try
        {
            Directory.CreateDirectory(_host.DataFolder);
            var saved = new StockCarveSaved
            {
                StockFile = _stockFile, ModelFile = _modelFile, Placement = Placement(), Carve = Options(),
                KeepSize = _keepSize.IsChecked == true, SpareMm = (double)(_spare.Value ?? 0), ShowStock = _showStock.IsChecked == true,
            };
            File.WriteAllText(SavedPath, JsonSerializer.Serialize(saved, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
