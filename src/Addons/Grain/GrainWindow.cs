using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using PhotonCanvas.App;
using PhotonCanvas.App.Addons;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using static PhotonCanvas.App.UiKit;

namespace PhotonCanvas.Addon.Grain;

/// <summary>
/// Grain-aware engraving: map the grain from the camera picture, choose what each layer does with
/// it, check it with a test burn, and place a picture on the grain. Stays open beside the design.
/// </summary>
sealed class GrainWindow : Window
{
    readonly IAddonHost _host;
    readonly GrainOverlay _overlay;

    readonly TextBlock _mapInfo = new() { TextWrapping = TextWrapping.Wrap };
    readonly ComboBox _mapOver = new() { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = new[] { "Around the design", "The selected shapes (the board's outline)", "The whole camera picture" }, SelectedIndex = 0 };
    readonly ComboBox _show = new() { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = new[] { "Nothing", "The grain map", "Where this layer's power is lowered" }, SelectedIndex = 1 };
    readonly Button _takePhoto = Button("Take a camera picture", tip: "A new overview picture of the bed. Nothing moves and the laser stays off.");
    readonly Button _map = Button("Map the grain");
    readonly Button _forget = Button("Forget the map");

    readonly ComboBox _layer = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly ComboBox _mode = new() { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = new[] { "Off", "Even it out", "Bring out the grain" } };
    readonly NumericUpDown _strength = Number(0, (decimal)(GrainSettings.MaxStrength * 100), 5, "0'%'");
    readonly CheckBox _darkDarker = new() { Content = "Dark grain burns darker on this wood" };
    readonly TextBlock _layerInfo = Hint();

    readonly Button _addTest = Button("Add a grain test");
    readonly Button _readTest = Button("Read the test");
    readonly Button _useTest = Button("Use the test's settings");
    readonly TextBlock _testInfo = new() { TextWrapping = TextWrapping.Wrap };

    readonly CheckBox _turns = new() { Content = "Allow quarter turns" };
    readonly Button _place = Button("Find the best place");
    readonly TextBlock _placeInfo = new() { TextWrapping = TextWrapping.Wrap };

    List<Layer> _layers = [];
    bool _loading;
    bool _busy;
    /// <summary>The camera picture the map was made from: a test must be read from a newer one.</summary>
    CameraPhoto? _mappedFrom;
    GrainTest.Result? _lastTest;

    public GrainWindow(IAddonHost host, GrainOverlay overlay)
    {
        _host = host;
        _overlay = overlay;
        Title = "Grain-aware engraving";
        Width = 440;
        Height = 760;
        MinWidth = 380;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");

        _strength.Value = 25;
        _takePhoto.Click += async (_, _) => await Run(TakePhoto);
        _map.Click += async (_, _) => await Run(MapGrain);
        _forget.Click += (_, _) => Commit("Forget the grain map", GrainProject.WithMap(_host.Document.State, null));
        _show.SelectionChanged += (_, _) => ShowOverlay();
        _layer.SelectionChanged += (_, _) => { if (!_loading) LoadLayer(); };
        _mode.SelectionChanged += (_, _) => SaveLayer();
        _strength.ValueChanged += (_, _) => SaveLayer();
        _darkDarker.IsCheckedChanged += (_, _) => SaveLayer();
        _addTest.Click += (_, _) => AddTest();
        _readTest.Click += async (_, _) => await Run(ReadTest);
        _useTest.Click += (_, _) => UseTest();
        _place.Click += async (_, _) => await Run(Place);

        var help = Button("Help");
        help.Click += (_, _) => _host.ShowHelp("grain.md");
        var close = new Button { Content = "Close", IsCancel = true, MinWidth = 90 };
        close.Click += (_, _) => Close();
        var bottom = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 10, 0, 0), Children = { help, close } };
        DockPanel.SetDock(bottom, Dock.Bottom);

        var body = new StackPanel
        {
            Spacing = 12,
            Children =
            {
                Hint("Wood's grain burns unevenly: the same power comes out darker on some bands than others. Map the grain from the camera picture and each layer can follow it."),
                Explained("1. Map the grain",
                    "Put the board on the bed, take a camera picture, and map the grain where you'll engrave. Nothing moves and the laser stays off.",
                    _mapInfo,
                    Field("Map over", _mapOver, tip: "Around the design: the layers' shapes and 10 mm round them. The selected shapes: select the board's outline (pick it with the magic wand) to map just the board. The whole camera picture: everything the camera sees."),
                    Row(_takePhoto, _map),
                    Field("Show", _show, tip: "Laid over the camera picture on the canvas while this window is open."),
                    _forget),
                Explained("2. What each layer does",
                    "Even it out: less power where the grain would burn darker, for photos and fills that come out even. Bring out the grain: the opposite, for an engraving that shows the wood's figure. Engrave (lines) layers and pictures in shades of grey or dithered can follow the grain.",
                    Field("Layer", _layer),
                    Field("Grain", _mode),
                    Field("Strength", _strength, Hint("At most this much less power, on the grain that needs it most."), tip: "How much less power at most. A grain test measures it for your wood; 20–30 % is a fair start."),
                    _darkDarker,
                    _layerInfo),
                Explained("3. Measure it (optional)",
                    "Two strips burned across the grain, at the layer's power and at 70 %, on a page of their own. Burn that page, take a new camera picture, and Read the test: it works out the strength for your wood.",
                    Row(_addTest, _readTest),
                    _testInfo,
                    _useTest),
                Explained("Place a picture on the grain",
                    "Moves the selected picture to where its dark parts fall on dark grain and its light parts on light grain, so the wood's figure becomes part of it. Map the whole board first (Map over: the selected shapes, with the board's outline selected).",
                    _turns,
                    _place,
                    _placeInfo),
            },
        };
        Content = new DockPanel { Margin = new Thickness(14), Children = { bottom, new ScrollViewer { Content = body } } };

        _host.DocumentChanged += Refresh;
        _host.PhotoChanged += Refresh;
        Closed += (_, _) =>
        {
            _host.DocumentChanged -= Refresh;
            _host.PhotoChanged -= Refresh;
        };
        _layer.SelectedIndex = -1;
        Refresh();
    }

    DocState State => _host.Document.State;

    async Task Run(Func<Task> work)
    {
        if (_busy) return;
        _busy = true;
        Refresh();
        try { await _host.Guard(work); }
        finally
        {
            _busy = false;
            Refresh();
        }
    }

    void Commit(string label, DocState next, IEnumerable<Guid>? select = null, string? coalesce = null)
    {
        _host.Document.Commit(label, next, select, coalesce);
        Refresh();
    }

    // ---- the map ----

    async Task TakePhoto()
    {
        if (!_host.IsConnected) { _mapInfo.Text = "Connect the laser to take a camera picture."; return; }
        _host.Status("Taking a camera picture…");
        if (await _host.TakePhotoAsync() is null) _mapInfo.Text = "The camera picture didn't come. Check the connection and try again.";
    }

    async Task MapGrain()
    {
        if (_host.Photo is not { } photo) { _mapInfo.Text = "Take a camera picture first (the board on the bed, the lid closed)."; return; }
        var s = State;
        Rect2 region;
        Func<Vec2, bool>? inside = null;
        switch (_mapOver.SelectedIndex)
        {
            case 1:
                var sel = s.Shapes.Where(sh => _host.Document.Selection.Contains(sh.Id)).ToList();
                if (sel.Count == 0) { _mapInfo.Text = "Select the board's outline (or the shapes to map under) first."; return; }
                region = s.Bounds(sel.Select(sh => sh.Id));
                var outlines = sel.OfType<PathShape>().SelectMany(p => p.World.Flatten(0.1)).Where(f => f.Closed && f.Points.Count >= 3).Select(f => f.Points).ToList();
                if (outlines.Count > 0) inside = p => outlines.Count(o => Geom.Inside(o, p)) % 2 == 1;
                break;
            case 2:
                region = photo.Area;
                break;
            default:
                var ids = JobBuilder.OutputLayers(s).SelectMany(s.ShapesFor).Select(sh => sh.Id).ToList();
                region = ids.Count == 0 ? photo.Area : s.Bounds(ids).Inflate(10);
                break;
        }
        _mapInfo.Text = "Mapping the grain…";
        var map = await Task.Run(() =>
        {
            var m = GrainMap.FromPhoto(photo.Image, photo.Area, photo.MmPerPx, region);
            return inside is null ? m : m.Masked(inside);
        });
        _mappedFrom = photo;
        Commit("Map the grain", GrainProject.WithMap(State, map));
        if (_show.SelectedIndex == 0) _show.SelectedIndex = 1;
    }

    static string Describe(GrainMap map)
    {
        var a = map.Area;
        string dir = double.IsNaN(map.AngleDeg) ? "no clear direction" : $"runs at {map.AngleDeg:0}°";
        string strength = map.ContrastPct < 2 ? "hardly shows" : map.ContrastPct < 6 ? "shows faintly" : map.ContrastPct < 15 ? "shows clearly" : "shows strongly";
        string known = map.KnownFraction < 0.98 ? $", {map.KnownFraction:P0} of it on the board" : "";
        return $"Mapped {a.Width:0} × {a.Height:0} mm at {a.MinX:0}, {a.MinY:0}{known}. The grain {strength} ({map.ContrastPct:0.#} % lighter to darker) and {dir}.";
    }

    void ShowOverlay()
    {
        var s = State;
        var map = GrainProject.Map(s);
        if (map is null || _show.SelectedIndex == 0) _overlay.Clear();
        else if (_show.SelectedIndex == 1) _overlay.ShowMap(map);
        else if (Selected() is { } l && GrainProject.For(s, l.Id) is { Active: true } set) _overlay.ShowPower(GrainHook.Field(map, set));
        else _overlay.Clear();
        _host.InvalidateCanvas();
    }

    // ---- the layers ----

    Layer? Selected() => _layer.SelectedIndex >= 0 && _layer.SelectedIndex < _layers.Count ? _layers[_layer.SelectedIndex] : null;

    void LoadLayer()
    {
        _loading = true;
        try
        {
            var l = Selected();
            var set = l is null ? GrainSettings.None : GrainProject.For(State, l.Id);
            bool can = l is not null && set.TestFactor is null;
            _mode.IsEnabled = _strength.IsEnabled = _darkDarker.IsEnabled = can;
            _mode.SelectedIndex = (int)set.Mode;
            _strength.Value = (decimal)Math.Round(set.Mode == GrainMode.Off && set.Strength == 0 ? 25 : set.Strength * 100);
            _darkDarker.IsChecked = set.DarkBurnsDarker;
            _layerInfo.Text = l is null ? "No Engrave or Picture layer yet."
                : set.TestFactor is { } f ? $"A grain test strip at {f:P0} of the tested power: always burned at its own power."
                : Why(l) ?? (set.Active && GrainProject.Map(State) is null ? "Map the grain first: until then it runs at its own power." : "");
        }
        finally { _loading = false; }
        ShowOverlay();
    }

    /// <summary>Why the layer can't follow the grain as it's set (null when it can).</summary>
    static string? Why(Layer l) => l.Mode switch
    {
        LayerMode.Fill when l.FillStyle == FillStyle.Offset => "Offset fill can't change power along its lines: switch the layer to Lines to follow the grain.",
        LayerMode.Image when l.ImageMode == ImageEngraveMode.Slices => "Depth slices can't follow the grain.",
        LayerMode.Image when l.ImageMode == ImageEngraveMode.Dither && l.Dither is Core.Imaging.DitherMethod.Threshold or Core.Imaging.DitherMethod.Sketch => "Threshold and sketch pictures are all-or-nothing: pick a dither or Grayscale to follow the grain.",
        _ => null,
    };

    void SaveLayer()
    {
        if (_loading || Selected() is not { } l) return;
        var old = GrainProject.For(State, l.Id);
        if (old.TestFactor is not null) return;
        var mode = (GrainMode)Math.Max(0, _mode.SelectedIndex);
        var set = new GrainSettings(mode, (double)(_strength.Value ?? 25) / 100, _darkDarker.IsChecked != false);
        if (set == old) return;
        // Off with nothing else changed: the add-on's note is taken off the layer.
        if (mode == GrainMode.Off && set.Strength == 0.25 && set.DarkBurnsDarker) set = GrainSettings.None;
        Commit("Grain settings", GrainProject.WithLayer(State, l.Id, set), coalesce: $"grain-{l.Id}");
    }

    // ---- the test ----

    void AddTest()
    {
        var s = State;
        if (Selected() is not { } l || GrainProject.For(s, l.Id).TestFactor is not null) { _testInfo.Text = "Pick the layer to test above."; return; }
        var map = GrainProject.Map(s);
        var sel = s.Bounds(_host.Document.Selection);
        var centre = !sel.IsEmpty ? sel.Center : map is not null ? map.Area.Center : new Vec2(_host.Machine.FieldW / 2, _host.Machine.FieldH / 2);
        var (next, strips) = GrainTest.AddOnOwnPage(s, l.Id, centre, map?.AngleDeg ?? double.NaN);
        Commit("Add a grain test", next, strips);
        _testInfo.Text = "The strips are on a new page, \"Grain test\" (a page is its own job). Move them onto a spare part of the board, across the grain, and Ship It. "
            + "When they're burned, come back here and Read the test. If you move them off the mapped area, map the grain again before burning.";
    }

    async Task ReadTest()
    {
        var s = State;
        if (GrainProject.Map(s) is not { } map) { _testInfo.Text = "Map the grain first (before the strips are burned)."; return; }
        var strips = GrainTest.Strips(s);
        if (strips.Count == 0) { _testInfo.Text = "There's no grain test in this project. Add one first."; return; }
        CameraPhoto? after;
        if (_host.IsConnected)
        {
            _testInfo.Text = "Taking a camera picture of the burned strips…";
            after = await _host.TakePhotoAsync();
        }
        else after = _host.Photo;
        if (after is null) { _testInfo.Text = "Take a camera picture of the burned strips (connect the laser)."; return; }
        if (ReferenceEquals(after, _mappedFrom)) { _testInfo.Text = "That's the camera picture the grain was mapped from. Burn the strips, then take a new one."; return; }
        var result = await Task.Run(() => GrainTest.Read(map, strips, after.Image, after.Area));
        _lastTest = result;
        _testInfo.Text = result.Message + (result.Calibration is { } c ? $" (measured at {c.Samples} places; the model explains {c.Fit:P0} of what was seen)" : "");
        if (result.Calibration is { } cal) Commit("Read the grain test", GrainProject.WithCalibration(State, cal));
    }

    void UseTest()
    {
        var s = State;
        if (GrainProject.Calibration(s) is not { } cal) return;
        // The tested layer if it's on this page, else the one picked.
        var target = GrainTest.TestedLayer(s) is { } id && s.FindLayer(id) is not null ? id : Selected()?.Id;
        if (target is not { } t) return;
        var old = GrainProject.For(s, t);
        var set = new GrainSettings(old.Mode == GrainMode.Off ? GrainMode.Even : old.Mode, cal.Strength, cal.DarkBurnsDarker);
        Commit("Use the grain test's settings", GrainProject.WithLayer(s, t, set));
        int i = _layers.FindIndex(l => l.Id == t);
        if (i >= 0) _layer.SelectedIndex = i;
        LoadLayer();
    }

    // ---- placing a picture ----

    async Task Place()
    {
        var s = State;
        if (GrainProject.Map(s) is not { } map) { _placeInfo.Text = "Map the board's grain first."; return; }
        if (_host.SelectedPicture is not { } img) { _placeInfo.Text = "Select one picture first."; return; }
        bool turns = _turns.IsChecked == true;
        _placeInfo.Text = "Looking for the best place…";
        var r = await Task.Run(() => GrainPlacement.Best(map, img, null, turns));
        if (r is null) { _placeInfo.Text = "The picture doesn't fit on the mapped board. Map the whole board, or make the picture smaller."; return; }
        if (!double.IsNaN(r.ScoreHere) && r.ScoreHere >= r.Score - 0.005) { _placeInfo.Text = $"It's already in the best place (matches the grain {r.Score:0.00})."; return; }
        var moved = img.With(transform: r.Transform);
        int at = s.Shapes.IndexOf(img);
        if (at < 0) return;
        Commit("Place on the grain", s with { Shapes = s.Shapes.SetItem(at, moved) }, [img.Id]);
        string here = double.IsNaN(r.ScoreHere) ? "" : $", {r.ScoreHere:0.00} where it was";
        string turn = r.TurnDeg != 0 ? $", turned {r.TurnDeg:0}°" : "";
        _placeInfo.Text = $"Moved {r.Move.Length:0} mm{turn}. It matches the grain {r.Score:0.00} there (1 = perfectly{here}). Undo puts it back."
            + (r.Score < 0.15 ? " That's weak: this grain doesn't have much to offer this picture." : "");
    }

    // ---- keeping up with the design ----

    void Refresh()
    {
        var s = State;
        var map = GrainProject.Map(s);
        _loading = true;
        try
        {
            var keep = Selected()?.Id ?? _host.Document.ActiveLayer;
            _layers = [.. s.Layers.Where(l => l.Mode is LayerMode.Fill or LayerMode.Image)];
            _layer.ItemsSource = _layers.Select(l => l.ParentId is null ? l.Name : $"  {l.Name}").ToList();
            int i = _layers.FindIndex(l => l.Id == keep);
            _layer.SelectedIndex = i >= 0 ? i : _layers.Count > 0 ? 0 : -1;
        }
        finally { _loading = false; }
        _mapInfo.Text = map is null
            ? _host.Photo is null ? "No camera picture yet." : "Not mapped yet."
            : Describe(map);
        _takePhoto.IsEnabled = !_busy && _host.IsConnected;
        _map.IsEnabled = !_busy && _host.Photo is not null;
        _forget.IsEnabled = !_busy && map is not null;
        _addTest.IsEnabled = !_busy && Selected() is not null;
        _readTest.IsEnabled = !_busy && map is not null && GrainTest.Strips(s).Count > 0;
        _useTest.IsEnabled = !_busy && GrainProject.Calibration(s) is not null;
        _useTest.Content = GrainProject.Calibration(s) is { } cal
            ? $"Use the test's settings ({(cal.DarkBurnsDarker ? "dark" : "light")} grain, {cal.Strength:P0})"
            : "Use the test's settings";
        _place.IsEnabled = !_busy && map is not null && _host.SelectedPicture is not null;
        LoadLayer();
    }
}
