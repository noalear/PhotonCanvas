using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Materials;
using static PhotonCanvas.App.UiKit;

using PhotonCanvas.App;

namespace PhotonCanvas.Addon.MaterialTests;

/// <summary>
/// Etch rate: how deep one pass of a setting goes. A ladder of small circles, each burned a
/// different number of passes, round by round; the first one that goes right through the material
/// gives thickness ÷ passes. Depths read on the shallower circles show how much it slows with depth.
/// </summary>
public sealed class EtchTestWindow : Window
{
    readonly Document _doc;
    readonly MaterialLibrary _lib;
    readonly MachineProfile _machine;
    readonly Func<CameraPhoto?> _photo;
    readonly Func<Task> _updatePhoto;
    readonly Func<double, ImageMapChoiceInfo?> _cameraFor;
    readonly TextBlock _photoNote = new() { TextWrapping = TextWrapping.Wrap, Classes = { "hint" } };
    readonly EtchChart _chart = new();

    // Making the test
    readonly ComboBox _light = new() { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = new[] { "Diode (blue)", "MOPA (red)" }, SelectedIndex = 0 };
    readonly NumericUpDown _power = Number(1, 100, 5, "0.#"), _speed = Number(1, 15000, 10, "0"), _lpcm = Number(10, 1000, 10, "0");
    readonly NumericUpDown _first = Number(1, 5000, 1, "0"), _step = Number(1, 1000, 1, "0"), _count = Number(2, 60, 1, "0"), _dia = Number(2, 20, 0.5m, "0.#"), _turn = Number(0, 90, 5, "0");
    readonly TextBlock _plan = new() { TextWrapping = TextWrapping.Wrap };
    // Reading the results
    readonly ComboBox _material = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly TextBox _newName = new() { Watermark = "e.g. Birch plywood" };
    readonly NumericUpDown _thick = Number(0.1m, 100, 0.1m, "0.##");
    readonly WrapPanel _circles = new();
    readonly Dictionary<int, NumericUpDown> _depths = [];
    readonly TextBlock _result = new() { TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.SemiBold };
    readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    readonly Button _save = new() { Content = "Save to the library", Classes = { "primary" }, IsEnabled = false };
    int? _firstThrough;
    bool _noneThrough;

    /// <summary>How well the camera picture lines up at a material height: exact or not, and why.</summary>
    public sealed record ImageMapChoiceInfo(bool Exact, string Description);

    public EtchTestWindow(Document doc, MaterialLibrary lib, MachineProfile machine, Func<CameraPhoto?> photo, Func<Task> updatePhoto, bool showResults,
        Func<double, ImageMapChoiceInfo?>? cameraFor = null)
    {
        _doc = doc;
        _lib = lib;
        _machine = machine;
        _photo = photo;
        _updatePhoto = updatePhoto;
        _cameraFor = cameraFor ?? (_ => null);
        Title = "Etch rate test";
        Width = 760;
        Height = 760;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");
        _power.Value = 60; _speed.Value = 200; _lpcm.Value = 100;
        _first.Value = 1; _step.Value = 1; _count.Value = 20; _dia.Value = 5; _turn.Value = 30; _thick.Value = 3;
        foreach (var n in new[] { _power, _speed, _lpcm, _first, _step, _count, _dia, _turn }) n.ValueChanged += (_, _) => Plan();
        _light.SelectionChanged += (_, _) => Plan();

        var add = new Button { Content = "Add the test to the design", Classes = { "primary" }, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
        add.Click += (_, _) => AddTest();
        var make = new StackPanel
        {
            Spacing = 8, Margin = new(0, 12, 0, 0),
            Children =
            {
                Hint("One setting, burned into small circles: 1 pass, 2 passes, 3 … Burn it, then on the next tab click the first circle that went right through."),
                LaserChoice(Field("Laser", _light), machine), Field("Power %", _power), Field("Speed mm/s", _speed), Field("Lines per cm", _lpcm),
                Field("Turn per pass °", _turn, tip: "Turning the fill lines each pass keeps a deep floor even. Use what you'll use for real."),
                Field("First circle", _first, tip: "Passes on the first circle."), Field("Each next +", _step, tip: "Passes added on each next circle."), Field("Circles", _count), Field("Circle Ø (mm)", _dia),
                _plan,
                Hint("Run it round by round (the job bar's Passes setting) so every circle cools between passes. Put scrap under the material: circles that go through keep burning into what's below. Stay with it: deep passes can flare up."),
                add,
                Hint("Adding the test closes this window so you can move it, frame it and send it. When that job finishes, this window opens again on Read the results (or open it with Materials & Tests → Tests → Etch rate)."),
            },
        };

        _material.SelectionChanged += (_, _) => { _newName.IsVisible = _material.SelectedIndex == 0; if (_material.SelectedIndex > 0 && _lib.Materials[_material.SelectedIndex - 1].ThicknessMm is { } t) _thick.Value = (decimal)t; };
        _thick.ValueChanged += (_, _) => Compute();
        var update = Button("Update photo", "IcCamera", "A fresh camera photo of the burned test, for the thumbnails.");
        update.Click += async (_, _) => { update.IsEnabled = false; try { await _updatePhoto(); } finally { update.IsEnabled = true; } BuildCircles(); };
        var none = Button("None went through");
        none.Click += (_, _) => { _firstThrough = null; _noneThrough = true; BuildCircles(); Compute(); };
        _save.Click += (_, _) => Save();
        var read = new StackPanel
        {
            Spacing = 8, Margin = new(0, 12, 0, 0),
            Children =
            {
                Field("Material", _material), _newName, Field("Thickness (mm)", _thick, tip: "Measure it with calipers: the result is only as good as this."),
                Hint("Click the first circle that went right through (hold it up to the light). Optionally, type the depth of some shallower ones (a depth gauge or the calipers' tail) to see how much it slows down deeper."),
                Row(update, none),
                _photoNote,
                new ScrollViewer { Content = _circles, MaxHeight = 360 },
                _chart,
                _result, _status, _save,
            },
        };
        var tabs = new TabControl { Items = { new TabItem { Header = "Make the test", Content = new ScrollViewer { Content = make } }, new TabItem { Header = "Read the results", Content = new ScrollViewer { Content = read } } } };
        tabs.SelectedIndex = showResults ? 1 : 0;
        Content = new Border { Padding = new(16), Child = tabs };
        RefreshMaterials();
        Plan();
        BuildCircles();
        Compute();
    }

    LaserSettings Laser()
    {
        var l = LaserSettings.Blue((double)(_power.Value ?? 60), (double)(_speed.Value ?? 200));
        return _light.SelectedIndex == 1 ? l with { Light = LightSource.Red } : l;
    }

    EtchTestOptions Options() => new()
    {
        Laser = Laser(),
        LinesPerCm = (double)(_lpcm.Value ?? 100),
        TurnPerPassDeg = (double)(_turn.Value ?? 30),
        FirstPasses = (int)(_first.Value ?? 1),
        StepPasses = (int)(_step.Value ?? 1),
        Circles = (int)(_count.Value ?? 20),
        DiameterMm = (double)(_dia.Value ?? 5),
    };

    void Plan()
    {
        var o = Options();
        try
        {
            var (s, _) = EtchTest.Add(DocState.New() with { Layers = [] }, o with { Origin = new Vec2(10, 10) });
            var job = JobBuilder.Build(s, new JobOptions { WholeJobPasses = true });
            _plan.Text = $"Circles get {o.Ladder[0]} to {o.Ladder[^1]} passes; {job.Bounds.Width:0} × {job.Bounds.Height:0} mm; about {Duration(job.EstimateS)} to burn.";
        }
        catch (Exception e) when (e is JobException or InvalidOperationException) { _plan.Text = e.Message; }
    }

    void AddTest()
    {
        var o = Options();
        // Centred on the bed, or by the selection if there is one.
        var (probe, _) = EtchTest.Add(DocState.New() with { Layers = [] }, o with { Origin = new Vec2(0, 0) });
        var size = probe.Bounds(probe.Shapes.Select(x => x.Id).ToHashSet());
        var at = new Vec2(_machine.FieldW / 2 - size.Width / 2, _machine.FieldH / 2 - size.Height / 2);
        var (s, added) = EtchTest.Add(_doc.State, o with { Origin = at });
        _doc.Commit("Etch rate test", s, added);
        AppLog.Write("etch", $"Test added: {o.Ladder.Count} circles, {o.Ladder[0]}–{o.Ladder[^1]} passes, {o.Laser.Power:0.#}% {o.Laser.Speed:0.#} mm/s.");
        // Out of the way, so the test can be moved, framed and sent.
        Close();
    }

    List<(Layer Layer, int Passes, Rect2 Bounds)> Circles() =>
        [.. _doc.State.Layers.Select(l => (l, EtchTest.PassesOf(l))).Where(x => x.Item2 is not null)
            .Select(x => (x.l, x.Item2!.Value, _doc.State.Bounds(_doc.State.ShapesOn(x.l.Id).Select(s => s.Id).ToHashSet())))
            .Where(x => !x.Item3.IsEmpty).OrderBy(x => x.Item2)];

    void BuildCircles()
    {
        // The depth boxes are kept (with what's typed in them): take them off the old rows first.
        foreach (var row in _circles.Children.OfType<StackPanel>()) row.Children.Clear();
        _circles.Children.Clear();
        var photo = _photo();
        // The photo only lines up where the camera is calibrated for the material's height.
        _photoNote.Text = photo is { DistanceMm: { } dist } && _cameraFor(dist) is { Exact: false } cam
            ? $"⚠ The photo may show the wrong spot: the camera is {cam.Description}. Calibrate the camera at this material's height (Machine → Calibrate camera), or at a second height so any height can be worked out."
            : "";
        foreach (var (layer, passes, bounds) in Circles())
        {
            Control face = new TextBlock { Text = $"{passes}", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontSize = 16 };
            if (photo is not null && MaterialLibrary.CropPhoto(photo.Image, photo.Area, photo.MmPerPx, bounds.Inflate(1.5), 120) is { } jpg)
            {
                try { using var ms = new MemoryStream(jpg); face = new Image { Source = new Avalonia.Media.Imaging.Bitmap(ms), Stretch = Stretch.UniformToFill }; }
                catch (ArgumentException) { }
            }
            bool through = _firstThrough is { } f && passes >= f;
            var b = new Button
            {
                Width = 74, Height = 74, Padding = new(2), Content = face,
                BorderThickness = new(3), BorderBrush = _firstThrough == passes ? Brush("Accent") : Brushes.Transparent,
                Opacity = _firstThrough is not null && !through ? 0.6 : 1,
            };
            ToolTip.SetTip(b, $"{passes} passes: click if this is the first that went through");
            int p = passes;
            b.Click += (_, _) => { _firstThrough = p; _noneThrough = false; BuildCircles(); Compute(); };
            if (!_depths.TryGetValue(passes, out var depth))
            {
                depth = Number(0, 100, 0.05m, "0.00");
                depth.Watermark = "depth";
                depth.ValueChanged += (_, _) => Compute();
                _depths[passes] = depth;
            }
            depth.Width = 74;
            depth.ShowButtonSpinner = false;
            _circles.Children.Add(new StackPanel
            {
                Margin = new(3), Spacing = 2,
                Children = { b, new TextBlock { Text = $"{passes} pass{(passes == 1 ? "" : "es")}" + (through ? " ✓" : ""), FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center }, depth },
            });
        }
        if (_circles.Children.Count == 0) _circles.Children.Add(Hint("There's no etch test in this design yet: make one on the first tab."));
    }

    void Compute()
    {
        var circles = Circles();
        double t = (double)(_thick.Value ?? 0);
        var lines = new List<string>();
        EtchRate? rate = null;
        if (_firstThrough is { } f && t > 0)
        {
            int? before = circles.Where(c => c.Passes < f).Select(c => (int?)c.Passes).LastOrDefault();
            rate = EtchTest.FromBreakthrough(t, f, before);
            lines.Add($"About {rate.MmPerPass:0.###} mm per pass (between {rate.LowMm:0.###} and {rate.HighMm:0.###}): {rate.Basis}. This is the average all the way through.");
        }
        else if (_noneThrough && circles.Count > 0 && t > 0)
        {
            rate = EtchTest.NoneThrough(t, circles[^1].Passes);
            lines.Add($"Less than {rate.HighMm:0.###} mm per pass ({rate.Basis}). Try more passes or more power.");
        }
        var readings = _depths.Where(d => d.Value.Value is > 0).Select(d => (d.Key, (double)d.Value.Value!.Value)).ToList();
        if (readings.Count > 0)
        {
            var c = EtchTest.FromDepths(readings);
            lines.Add($"From the depths: {c.FirstPassMm:0.###} mm on the first pass, {c.AverageMm:0.###} mm a pass on average" +
                      (readings.Count >= 3 ? $", {Trend(c)} by the deepest reading" + (readings.Count >= 4 ? $" (the readings fit within ±{c.RmsMm:0.00} mm)." : ".") : "."));
            if (readings.Count >= 3 && c.SlowdownPercent < -10)
                lines.Add("Getting faster deeper is unusual: often the shallow holes read low (char in the bottom, or the gauge not reaching the floor). Worth measuring those again.");
        }
        _result.Text = string.Join("\n", lines);
        int? notThrough = _firstThrough is { } ft ? circles.Where(x => x.Passes < ft).Select(x => (int?)x.Passes).LastOrDefault() : null;
        _chart.Show(t, circles.Count > 0 ? circles[^1].Passes : 20, _firstThrough, notThrough, readings,
            readings.Count > 0 ? EtchTest.FromDepths(readings) : null);
        _save.IsEnabled = rate is { MmPerPass: > 0 } || readings.Count > 0;
        _save.Tag = (rate, readings);
    }

    /// <summary>"slowing by 30%" or "getting faster by 52%" (a negative slowdown).</summary>
    static string Trend(EtchCurve c) => Math.Abs(c.SlowdownPercent) < 5 ? "about the same all the way"
        : c.SlowdownPercent > 0 ? $"slowing by {c.SlowdownPercent:0}%" : $"getting faster by {-c.SlowdownPercent:0}%";

    void RefreshMaterials()
    {
        _material.ItemsSource = _lib.Materials.Select(m => m.Label).Prepend("New material…").ToList();
        _material.SelectedIndex = 0;
    }

    void Save()
    {
        AppLog.Write("etch", $"Save pressed. Library file: {_lib.FilePath}");
        try { SaveCore(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException or ArgumentException or NotSupportedException)
        {
            AppLog.Error("etch save", e);
            _status.Text = $"Couldn't save: {e.Message} ({_lib.FilePath})";
            _status.Foreground = Brush("Warn");
        }
    }

    void SaveCore()
    {
        var circles = Circles();
        if (circles.Count == 0) { _status.Text = "There's no etch test in the design to save."; AppLog.Write("etch", "Not saved: no etch test in the design."); return; }
        if (_save.Tag is not ValueTuple<EtchRate?, List<(int, double)>> tag) { _status.Text = "Pick the first circle that went through (or type depths) first."; AppLog.Write("etch", $"Not saved: no result (tag {_save.Tag?.GetType().Name ?? "null"})."); return; }
        var (rate, readings) = tag;
        Material m;
        if (_material.SelectedIndex <= 0)
        {
            var name = _newName.Text?.Trim();
            if (string.IsNullOrEmpty(name)) { _status.Text = "Give the material a name."; AppLog.Write("etch", "Not saved: the new material has no name."); return; }
            m = new Material { Name = name, Category = "Other", ThicknessMm = (double)(_thick.Value ?? 0), HighlyReflective = Material.LooksReflective(name) };
        }
        else m = _lib.Materials[_material.SelectedIndex - 1];
        var layer = circles[0].Layer;
        var curve = readings.Count > 0 ? EtchTest.FromDepths(readings) : null;
        double perPass = rate is { MmPerPass: > 0 } r ? r.MmPerPass : curve!.AverageMm;
        var notes = new List<string>();
        if (rate is { MmPerPass: > 0 } rr) notes.Add($"Etch test on {_thick.Value:0.##} mm: {rr.Basis} ({rr.LowMm:0.###}–{rr.HighMm:0.###} mm per pass).");
        if (curve is not null)
            notes.Add($"Depths ({string.Join(", ", readings.OrderBy(x => x.Item1).Select(x => $"{x.Item1} passes {x.Item2:0.##} mm"))}): first pass {curve.FirstPassMm:0.###} mm, " +
                      (readings.Count >= 3 ? $"{Trend(curve)} going down." : "too few readings to tell how it changes going down."));
        var recipe = new Recipe
        {
            Purpose = RecipePurpose.Engrave,
            Title = $"Etch {perPass:0.###} mm a pass",
            Settings = ProcessSettings.From(layer with { Laser = layer.Laser with { Passes = 1 } }),
            Tested = true,
            TestedOn = DateTime.Now,
            EtchMmPerPass = perPass,
            Etch = new EtchRecord((double)(_thick.Value ?? 0), _firstThrough,
                _firstThrough is { } ft ? circles.Where(x => x.Passes < ft).Select(x => (int?)x.Passes).LastOrDefault() : null,
                [.. readings.OrderBy(x => x.Item1).Select(x => new EtchReading(x.Item1, x.Item2))]),
            Notes = string.Join(" ", notes),
        };
        _lib.Upsert(m with { Recipes = m.Recipes.Add(recipe) });
        _lib.Save();
        // Read it back from the file, so "saved" means it's really there.
        var check = MaterialLibrary.Load(_lib.Folder).Materials.FirstOrDefault(x => x.Id == m.Id)?.Recipes.Any(x => x.Id == recipe.Id) == true;
        var info = new FileInfo(_lib.FilePath);
        AppLog.Write("etch", $"Saved \"{recipe.Title}\" to {m.Label} in {info.FullName} ({(info.Exists ? info.Length : 0)} bytes, written {(info.Exists ? info.LastWriteTime.ToString("HH:mm:ss") : "never")}); read back: {(check ? "found" : "MISSING")}.");
        _status.Text = check ? $"Saved to {m.Label} ({info.FullName})." : $"⚠ Saving to {m.Label} didn't stick: it isn't in {info.FullName}. Details are in the log.";
        _status.Foreground = check ? Brush("Ok") : Brush("Warn");
        RefreshMaterials();
    }
}
