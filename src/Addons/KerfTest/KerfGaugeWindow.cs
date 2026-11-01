using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using PhotonCanvas.App;
using PhotonCanvas.App.Addons;
using PhotonCanvas.Core;
using PhotonCanvas.Core.Imaging;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Materials;
using static PhotonCanvas.App.UiKit;
using Bitmap = Avalonia.Media.Imaging.Bitmap;

namespace PhotonCanvas.Addon.KerfTest;

/// <summary>
/// Fits first time. Make: a tapered-slot kerf gauge at the cut settings to measure, placed where the
/// close-up camera sees. Read: push the keys in, take a close-up photo (or open any photo and click
/// the top slot's two ends), and where each key stopped is found and turned into the kerf, the slot
/// fit and the sheet's real thickness; drag a marker if it's off, or type what the scales say. Save
/// it on the material (the recipe it was cut with), and put the kerf on the design's cut layers.
/// </summary>
public sealed class KerfGaugeWindow : Window
{
    readonly IAddonHost _host;
    Document Doc => _host.Document;

    // Making the gauge
    sealed record Source(string Label, LaserSettings Laser, MaterialRef? Material, double? ThicknessMm);
    List<Source> _sources = [];
    readonly ComboBox _source = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly ComboBox _light = new() { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = new[] { "Diode (blue)", "MOPA (red)" }, SelectedIndex = 0 };
    readonly NumericUpDown _power = Number(1, 100, 5, "0.#"), _speed = Number(0.5m, 15000, 1, "0.#"), _passes = Number(1, 100, 1, "0"), _thick = Number(0.1m, 30, 0.1m, "0.0#");
    readonly CheckBox _inCloseUp = new() { Content = "Where the close-up camera sees it (read from a close-up photo)" };
    readonly TextBlock _plan = new() { TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.SemiBold };
    readonly Button _add = new() { Content = "Add the gauge to the design", Classes = { "primary" }, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };

    // Reading it
    readonly GaugeView _view = new() { Height = 330, Margin = new(0, 4, 0, 0) };
    readonly TextBlock _prompt = new() { TextWrapping = TextWrapping.Wrap };
    readonly Button _closeUp = Button("Close-up photo", "IcZoomIn", "A close-up photo of the gauge, laid on the bed: the sharpest reading. Needs the close-up camera's position at this focus height (Camera → Close-up position). Nothing moves.");
    readonly Button _overview = Button("Camera photo", "IcCamera", "A fresh overview camera photo. Coarser than a close-up: check the markers.");
    readonly Button _file = Button("Open a photo…", "IcOpen", "Any photo of the gauge taken square on (a phone held over it works): then click the top slot's mouth and its closed end.");
    readonly NumericUpDown _kerfRead = Number(-0.05m, 0.5m, 0.005m, "0.000"), _fitRead = Number(-1, 0.4m, 0.01m, "+0.00;−0.00;0.00");
    readonly TextBlock _result = new() { TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.SemiBold };
    readonly ComboBox _material = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly TextBox _newName = new() { Watermark = "e.g. Birch plywood" };
    readonly Button _save = new() { Content = "Save to the library", Classes = { "primary" }, IsEnabled = false };
    readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    readonly ComboBox _useOn = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly Button _use = new() { Content = "Put the kerf on this layer", IsEnabled = false };
    List<Layer> _useLayers = [];

    // The photo and what was read from it
    RasterImage? _img;
    Func<Vec2, Vec2>? _map;
    string _photoFrom = "";
    readonly List<Vec2> _clicks = [];
    double? _flatDepth, _edgeDepth;
    bool _flatSure, _edgeSure;
    bool _typing;

    public KerfGaugeWindow(IAddonHost host, bool showResults)
    {
        _host = host;
        Title = "Kerf gauge";
        Width = 820;
        Height = 860;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");
        _power.Value = 100; _speed.Value = 10; _passes.Value = 1; _thick.Value = 3;
        _source.SelectionChanged += (_, _) => FromSource();
        foreach (var n in new[] { _power, _speed, _passes, _thick }) n.ValueChanged += (_, _) => Plan();
        _light.SelectionChanged += (_, _) => Plan();
        _inCloseUp.IsChecked = _host.CloseUpSees() is not null;
        _inCloseUp.IsEnabled = _inCloseUp.IsChecked == true;
        _inCloseUp.IsCheckedChanged += (_, _) => Plan();
        _add.Click += (_, _) => AddGauge();

        var make = new StackPanel
        {
            Spacing = 12, Margin = new(0, 12, 0, 0),
            Children =
            {
                new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap,
                    Text = "Cuts a small window with two tapered slots off its side, and the keys to try in them. Push a key into each slot until it stops: " +
                           "where it stops shows the kerf (top slot, key lying flat) and how wide a slot grips the sheet (bottom slot, key standing on its edge). " +
                           "Each 0.01 mm of kerf moves the key about 0.2 mm, so a photo reads it finely. Saved on the material, the kerf and fit are used by the box generator, slices, dowel pins and inlays.",
                },
                Field("Cut with", _source, tip: "The cut settings to measure: a cut recipe from your library (the result goes back onto it), or a cut layer in this design. Use settings that cut right through cleanly."),
                Row(Labelled("Laser", _light), Labelled("Power (%)", _power), Labelled("Speed (mm/s)", _speed), Labelled("Passes", _passes)),
                Field("Thickness (mm)", _thick, tip: "What the sheet is sold as. The bottom slot is drawn around it, and the reading says what it really is. Under 1.5 mm there's only the top slot."),
                _inCloseUp,
                _plan,
                Hint("Cut on the honeycomb, in the material the parts will be made from, and stay with it. The scales are marked lightly first."),
                _add,
                Hint("Adding the gauge closes this window so you can frame it and send it. When the job finishes, this window opens again on Read it (or open Laser → Kerf gauge)."),
            },
        };

        _closeUp.IsEnabled = _host.CloseUpSees() is not null;
        _closeUp.Click += async (_, _) => await Take(async () => await _host.CloseUpPhotoAsync(), "the close-up photo",
            "Couldn't take a close-up photo: connect the machine, at the focus height the close-up position was saved for.");
        _overview.Click += async (_, _) => await Take(async () => await _host.TakePhotoAsync() ?? _host.Photo, "the camera photo", "No camera photo: connect the machine first.");
        _file.Click += async (_, _) => await OpenFile();
        _view.Clicked += Clicked;
        _view.TipMoved += (edge, depth) => { if (edge) { _edgeDepth = depth; _edgeSure = true; } else { _flatDepth = depth; _flatSure = true; } Show(); };
        _kerfRead.ValueChanged += (_, _) => Typed(false);
        _fitRead.ValueChanged += (_, _) => Typed(true);
        _kerfRead.Value = null; _fitRead.Value = null;
        _material.SelectionChanged += (_, _) => _newName.IsVisible = _material.SelectedIndex == 0;
        _save.Click += (_, _) => Save();
        _use.Click += (_, _) => UseOnLayer();
        var remove = Button("Remove the gauge from the design", "IcTrash", "Delete the gauge's two layers and everything on them.");
        remove.Click += (_, _) => { Doc.Commit("Remove kerf gauge", KerfGauge.Remove(Doc.State)); Reset(); };

        var read = new StackPanel
        {
            Spacing = 10, Margin = new(0, 12, 0, 0),
            Children =
            {
                new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap,
                    Text = "1. Lift the window piece and the keys out; don't move the sheet. 2. Push one key, lying flat, into the top slot as far as a firm push takes it. " +
                           "3. Stand the other key on its long edge and push it along the bottom slot until it stops. 4. Take a photo.",
                },
                Row(_closeUp, _overview, _file),
                _prompt,
                new Border { Background = Brush("Bg0"), CornerRadius = new(8), Padding = new(4), Child = _view },
                Hint("Click or drag along a slot to move its marker to the key's tip. Or read the scales by eye and type them in:"),
                Row(Labelled("Top scale: kerf (mm)", _kerfRead), Labelled("Bottom scale: fit (mm)", _fitRead)),
                _result,
                Explained("Save it", "On the recipe the gauge was cut with (the cut settings get the kerf, the recipe the slot fit), or as a new tested cut recipe on the material.",
                    Field("Material", _material), _newName, _save, _status),
                Explained("Use it now", "Cut layers set to this kerf make parts that come out the size they're drawn, so parts drawn to fit press together snugly.",
                    Field("Layer", _useOn), _use),
                Row(remove),
            },
        };
        var tabs = new TabControl
        {
            Items =
            {
                new TabItem { Header = "Make the gauge", Content = new ScrollViewer { Content = make } },
                new TabItem { Header = "Read it", Content = new ScrollViewer { Content = read } },
            },
        };
        tabs.SelectedIndex = showResults ? 1 : 0;
        Content = new Border { Padding = new(16), Child = tabs };
        Sources();
        Plan();
        Reset();
        // A close-up photo of a gauge that's just been cut is what's wanted most often.
        if (showResults && _host.Photo is { } photo && KerfGauge.Find(Doc.State) is { } g && photo.Area.Contains(g.Flat.Mouth) && photo.Area.Contains(g.Flat.End))
            Use(photo, "the camera photo");
    }

    // ---- Making ----------------------------------------------------------------

    void Sources()
    {
        _sources = [];
        foreach (var m in _host.Library.Materials)
            foreach (var r in m.Recipes.Where(r => r.Settings.Mode == LayerMode.Cut))
                _sources.Add(new Source($"{m.Label} · {r.Title}" + (r.Settings.KerfMm > 0 ? $" (kerf {r.Settings.KerfMm:0.###} now)" : ""), r.Settings.Laser, new MaterialRef(m.Id, m.Label, r.Id, r.Title), m.ThicknessMm));
        foreach (var l in Doc.State.Layers.Where(l => l.Mode == LayerMode.Cut && l.ParentId is null && !KerfGauge.IsGaugeLayer(l) && !CutTest.IsCell(l)))
            _sources.Add(new Source($"Layer \"{l.Name}\" · {CutTest.Describe(l.Laser)}", l.Laser, l.Material, null));
        _sources.Add(new Source("Type them in", LaserSettings.Blue(100, 10), null, null));
        _source.ItemsSource = _sources.Select(x => x.Label).ToList();
        _source.SelectedIndex = 0;
    }

    void FromSource()
    {
        if (_source.SelectedIndex < 0 || _source.SelectedIndex >= _sources.Count) return;
        var s = _sources[_source.SelectedIndex];
        _light.SelectedIndex = s.Laser.Light == LightSource.Red ? 1 : 0;
        _power.Value = (decimal)Math.Clamp(s.Laser.Power, 1, 100);
        _speed.Value = (decimal)Math.Clamp(s.Laser.Speed, 0.5, 15000);
        _passes.Value = Math.Clamp(s.Laser.Passes, 1, 100);
        if (s.ThicknessMm is { } t) _thick.Value = (decimal)Math.Clamp(t, 0.1, 30);
        Plan();
    }

    Source? Chosen => _source.SelectedIndex >= 0 && _source.SelectedIndex < _sources.Count ? _sources[_source.SelectedIndex] : null;

    KerfGaugeOptions Options()
    {
        var src = Chosen;
        var laser = (src?.Laser ?? LaserSettings.Blue(100, 10)) with
        {
            Light = _light.SelectedIndex == 1 ? LightSource.Red : LightSource.Blue,
            Power = (double)(_power.Value ?? 100),
            Speed = (double)(_speed.Value ?? 10),
            Passes = (int)(_passes.Value ?? 1),
        };
        // Changed settings aren't the recipe's any more: the result then goes on as a new recipe.
        bool same = src?.Laser is { } was && was.Light == laser.Light && was.Power == laser.Power && was.Speed == laser.Speed && was.Passes == laser.Passes;
        return new KerfGaugeOptions { Laser = laser, ThicknessMm = (double)(_thick.Value ?? 3), Material = same ? src?.Material : src?.Material is { } m ? m with { RecipeId = null, RecipeTitle = null } : null };
    }

    /// <summary>The options sized and placed: in the close-up camera's view when that's wanted (null if it doesn't fit), else the middle of the bed.</summary>
    KerfGaugeOptions? Placed()
    {
        var o = Options();
        if (_inCloseUp.IsChecked == true && _host.CloseUpSees() is { } sees)
        {
            if (KerfGauge.TaperToFit(o, new Vec2(sees.Width - 2, sees.Height - 2)) is not { } taper) return null;
            o = o with { TaperMm = taper };
            var size = KerfGauge.Size(o);
            return o with { Origin = sees.Center - size / 2 };
        }
        o = o with { TaperMm = 14 };
        var all = KerfGauge.Size(o);
        return o with { Origin = new Vec2(_host.Machine.FieldW / 2, _host.Machine.FieldH / 2) - all / 2 };
    }

    void Plan()
    {
        if (Placed() is not { } o)
        {
            var sees = _host.CloseUpSees()!.Value;
            _plan.Text = $"The gauge doesn't fit where the close-up camera sees ({sees.Width:0} × {sees.Height:0} mm): untick it to put it in the middle of the bed and read it from another photo.";
            _add.IsEnabled = false;
            return;
        }
        try
        {
            var (s, _) = KerfGauge.Add(DocState.New() with { Layers = [] }, o);
            var job = JobBuilder.Build(s, new JobOptions());
            var size = KerfGauge.Size(o);
            bool edge = KerfGauge.HasEdgeSlot(o.ThicknessMm);
            _plan.Text = $"{size.X:0} × {size.Y:0} mm of material, slots {o.TaperMm:0} mm long · {(edge ? "two keys" : "one key (no fit slot under 1.5 mm)")} · about {Duration(job.EstimateS)} to cut" +
                         (_inCloseUp.IsChecked == true ? " · where the close-up camera sees it" : " · in the middle of the bed");
            _plan.ClearValue(TextBlock.ForegroundProperty);
            _add.IsEnabled = true;
        }
        catch (Exception e) when (e is JobException or InvalidOperationException or OutsideWorkAreaException)
        {
            _plan.Text = e.Message;
            _plan.Foreground = Brush("Warn");
            _add.IsEnabled = false;
        }
    }

    void AddGauge()
    {
        if (Placed() is not { } o) return;
        var (s, ids) = KerfGauge.Add(Doc.State, o);
        Doc.Commit("Kerf gauge", s, ids);
        _host.Status($"Kerf gauge added: {CutTest.Describe(o.Laser)}, {o.ThicknessMm:0.##} mm. Frame it and send it.");
        Close();
    }

    // ---- Reading -----------------------------------------------------------------

    /// <summary>Start again from the design's gauge: no photo, nothing read.</summary>
    void Reset()
    {
        _img = null; _map = null; _clicks.Clear();
        _flatDepth = _edgeDepth = null;
        Lists();
        Show();
    }

    async Task Take(Func<Task<CameraPhoto?>> take, string what, string failed)
    {
        foreach (var b in new[] { _closeUp, _overview, _file }) b.IsEnabled = false;
        try
        {
            if (await take() is { } photo) Use(photo, what);
            else _prompt.Text = failed;
        }
        finally
        {
            _closeUp.IsEnabled = _host.CloseUpSees() is not null;
            _overview.IsEnabled = _file.IsEnabled = true;
        }
    }

    void Use(CameraPhoto photo, string what)
    {
        _img = photo.Image;
        _photoFrom = what;
        _clicks.Clear();
        Mapped(KerfGaugePhoto.OnBed(photo.Area, photo.MmPerPx));
    }

    async Task OpenFile()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "A photo of the kerf gauge",
            FileTypeFilter = [new FilePickerFileType("Pictures") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.tif", "*.tiff", "*.bmp", "*.webp"] }],
        });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path) return;
        try { _img = await Task.Run(() => RasterImage.Decode(File.ReadAllBytes(path))); }
        catch (Exception e) when (e is IOException or FormatException or UnauthorizedAccessException) { _prompt.Text = $"Couldn't open it: {e.Message}"; return; }
        _photoFrom = Path.GetFileName(path);
        _map = null;
        _clicks.Clear();
        _flatDepth = _edgeDepth = null;
        Show();
    }

    void Clicked(Vec2 px)
    {
        if (_img is null || _map is not null || KerfGauge.Find(Doc.State) is not { } g) return;
        _clicks.Add(px);
        if (_clicks.Count < 2) { Show(); return; }
        var map = KerfGaugePhoto.FromTwoPoints(g.Flat.Mouth, g.Flat.End, _clicks[0], _clicks[1]);
        _clicks.Clear();
        if (map is null) { Show(); return; }
        Mapped(map);
    }

    /// <summary>A photo with its map: line the mouth up with the window's edge in it, then look for the keys.</summary>
    void Mapped(Func<Vec2, Vec2> map)
    {
        if (_img is null || KerfGauge.Find(Doc.State) is not { } g) { Show(); return; }
        var img = _img;
        if (KerfGaugePhoto.MouthShift(img, map, g) is { } shift && Math.Abs(shift) <= 1.5)
        {
            var along = g.Flat.Along * shift;
            var raw = map;
            map = mm => raw(mm + along);
        }
        _map = map;
        var flat = KerfGaugePhoto.FindTip(img, map, g.Flat);
        var edge = g.Edge is { } e ? KerfGaugePhoto.FindTip(img, map, e) : null;
        _flatDepth = flat?.DepthMm; _flatSure = flat?.Sure == true;
        _edgeDepth = edge?.DepthMm; _edgeSure = edge?.Sure == true;
        AppLog.Write("kerf gauge", $"Read from {_photoFrom}: flat {flat?.DepthMm:0.00} mm ({flat?.Confidence:0.0}), edge {edge?.DepthMm:0.00} mm ({edge?.Confidence:0.0}).");
        Show();
    }

    void Typed(bool edge)
    {
        if (_typing || KerfGauge.Find(Doc.State) is not { } g) return;
        if (edge)
        {
            if (_fitRead.Value is { } f && g.Edge is not null) { _edgeDepth = g.DepthForFit((double)f); _edgeSure = true; }
        }
        else if (_kerfRead.Value is { } k) { _flatDepth = g.DepthForKerf((double)k); _flatSure = true; }
        Show(fromTyping: true);
    }

    /// <summary>Show the photo (or the prompt for clicks), the markers and what they mean.</summary>
    void Show(bool fromTyping = false)
    {
        var g = KerfGauge.Find(Doc.State);
        _fitRead.IsEnabled = g?.Edge is not null;
        if (g is null)
        {
            _prompt.Text = "There's no kerf gauge in this design: make one on the first tab.";
            _view.Clear();
            _result.Text = "";
            _save.IsEnabled = _use.IsEnabled = false;
            return;
        }
        if (_img is null)
            _prompt.Text = "Take a photo of the gauge with the keys pushed in (a close-up photo reads best), or read the scales by eye and type them in below.";
        else if (_map is null)
        {
            _prompt.Text = _clicks.Count == 0 ? "Click the middle of the top slot's mouth (where it opens into the window)." : "Now click the middle of the top slot's closed end.";
            _view.ShowPhoto(_img, _clicks);
        }
        else
        {
            var area = KerfGauge.Area(Doc.State).Inflate(1.5);
            bool inPhoto = KerfGaugePhoto.FindTip(_img, _map, g.Flat) is not null;
            _prompt.Text = !inPhoto ? $"The gauge isn't in {_photoFrom}: keep it inside the close-up camera's outline, or open a photo." :
                (_flatDepth is null ? "The key in the top slot wasn't found: click where its tip is. " : !_flatSure ? "Not sure of the key in the top slot (orange): check its marker. " : "") +
                (g.Edge is not null && (_edgeDepth is null || !_edgeSure) ? "Check the bottom slot's marker (orange), or click where its key stops." : "");
            if (_flatSure && (g.Edge is null || _edgeSure) && inPhoto) _prompt.Text = $"Found both keys in {_photoFrom}. Drag a marker if it isn't at the tip.";
            _view.ShowGauge(KerfGaugePhoto.Resample(_img, _map, area, Math.Clamp(700 / Math.Max(1, area.Width), 8, 40)), area, g);
        }
        _view.Marks(g, _flatDepth, _flatSure, _edgeDepth, _edgeSure);

        var reading = new KerfGaugeReading(_flatDepth, _edgeDepth);
        var lines = new List<string>();
        if (reading.Kerf(g) is { } k)
        {
            bool off = _flatDepth is { } fd && (fd <= KerfGaugePhoto.EndMarginMm + 0.05 || fd >= g.Flat.Length - KerfGaugePhoto.EndMarginMm - 0.05);
            lines.Add(off ? "⚠ The flat key is at the end of its slot: the kerf is off the scale. Check the key went in, or make the gauge again." :
                $"Kerf {k:0.000} mm: set on a cut layer, parts come out the size they're drawn and parts drawn to fit press together snugly.");
        }
        if (reading.SlotFit(g) is { } fit)
        {
            lines.Add($"Slot fit {fit:+0.00;−0.00;0.00} mm: slots as wide as the thickness {(fit < 0 ? "less" : "plus")} {Math.Abs(fit):0.00} mm grip a standing piece" +
                      (reading.Kerf(g) is not null ? ", with the kerf on the cut layer." : " (read the top slot too for the kerf; this is with no kerf on the layer)."));
            if (reading.Thickness(g) is { } t && g.ThicknessMm is { } nominal)
                lines.Add($"The sheet is really {t:0.00} mm thick (sold as {nominal:0.##}).");
        }
        _result.Text = string.Join("\n", lines);
        if (!fromTyping)
        {
            _typing = true;
            _kerfRead.Value = reading.Kerf(g) is { } kk ? (decimal)Math.Round(Math.Clamp(kk, -0.05, 0.5), 3) : null;
            _fitRead.Value = reading.RawFit(g) is { } rf ? (decimal)Math.Round(Math.Clamp(rf, -1, 0.4), 2) : null;
            _typing = false;
        }
        _save.IsEnabled = reading.Kerf(g) is not null;
        _use.IsEnabled = reading.Kerf(g) is not null && _useLayers.Count > 0;
    }

    void Lists()
    {
        var cut = KerfGauge.CutLayer(Doc.State);
        var mats = _host.Library.Materials;
        _material.ItemsSource = mats.Select(m => m.Label).Prepend("New material…").ToList();
        int at = cut?.Material is { } r ? mats.FindIndex(m => m.Id == r.MaterialId) : -1;
        _material.SelectedIndex = at + 1;
        _newName.IsVisible = _material.SelectedIndex == 0;
        _useLayers = [.. Doc.State.Layers.Where(l => l.Mode == LayerMode.Cut && l.ParentId is null && !KerfGauge.IsGaugeLayer(l) && !CutTest.IsCell(l))];
        _useOn.ItemsSource = _useLayers.Select(l => $"{l.Name} · kerf {l.KerfMm:0.###} now").ToList();
        _useOn.SelectedIndex = _useLayers.Count > 0 ? 0 : -1;
    }

    void Save()
    {
        try { SaveCore(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException or ArgumentException or NotSupportedException)
        {
            AppLog.Error("kerf gauge save", e);
            _status.Text = $"Couldn't save: {e.Message} ({_host.Library.FilePath})";
            _status.Foreground = Brush("Warn");
        }
    }

    void SaveCore()
    {
        var lib = _host.Library;
        if (KerfGauge.Find(Doc.State) is not { } g || KerfGauge.CutLayer(Doc.State) is not { } cut) return;
        var reading = new KerfGaugeReading(_flatDepth, _edgeDepth);
        if (reading.Kerf(g) is not { } kerf) return;
        Material m;
        if (_material.SelectedIndex <= 0)
        {
            var name = _newName.Text?.Trim();
            if (string.IsNullOrEmpty(name)) { _status.Text = "Give the material a name."; _status.Foreground = Brush("Warn"); return; }
            m = new Material { Name = name, Category = "Other", ThicknessMm = g.ThicknessMm, HighlyReflective = Material.LooksReflective(name) };
        }
        else m = lib.Materials[_material.SelectedIndex - 1];
        var (saved, recipe) = KerfGaugeLibrary.Save(m, cut, Math.Round(kerf, 3), reading.SlotFit(g) is { } f ? Math.Round(f, 2) : null,
            reading.Thickness(g) is { } t ? Math.Round(t, 2) : null, g.ThicknessMm, DateTime.Now);
        // The photo of the keys in their slots, kept with the recipe (beside any photo it already has).
        if (_img is not null && _map is not null)
        {
            var jpg = KerfGaugePhoto.Resample(_img, _map, KerfGauge.Area(Doc.State).Inflate(1.5), 20).EncodeJpeg(800);
            if (recipe.Photo is null)
            {
                var named = recipe with { Photo = lib.SavePhoto(recipe.Id, jpg) };
                saved = saved with { Recipes = saved.Recipes.Replace(recipe, named) };
                recipe = named;
            }
            else lib.SaveFile($"{recipe.Id:N}-kerf-gauge.jpg", jpg);
        }
        lib.Upsert(saved);
        lib.Save();
        bool back = MaterialLibrary.Load(lib.Folder).Materials.FirstOrDefault(x => x.Id == saved.Id)?.Recipes.Any(x => x.Id == recipe.Id && Math.Abs(x.Settings.KerfMm - recipe.Settings.KerfMm) < 1e-9) == true;
        AppLog.Write("kerf gauge", $"Saved kerf {recipe.Settings.KerfMm:0.000}, fit {recipe.SlotFitMm} to \"{recipe.Title}\" on {saved.Label}; read back: {(back ? "found" : "MISSING")}.");
        _status.Text = back ? $"Saved on {saved.Label}: \"{recipe.Title}\" has kerf {recipe.Settings.KerfMm:0.000} mm" + (recipe.SlotFitMm is { } sf ? $" and slot fit {sf:+0.00;−0.00;0.00} mm." : ".") +
                              " Pick it under Cut with in the box generator, slices, dowel pins or inlay."
                            : $"⚠ Saving to {saved.Label} didn't stick. Details are in the log.";
        _status.Foreground = back ? Brush("Ok") : Brush("Warn");
        Lists();
    }

    void UseOnLayer()
    {
        if (KerfGauge.Find(Doc.State) is not { } g || new KerfGaugeReading(_flatDepth, _edgeDepth).Kerf(g) is not { } kerf) return;
        if (_useOn.SelectedIndex < 0 || _useOn.SelectedIndex >= _useLayers.Count) return;
        var target = _useLayers[_useOn.SelectedIndex];
        double k = Math.Round(Math.Max(0, kerf), 3);
        Doc.Commit($"Kerf {k:0.000} mm on {target.Name}", Ops.UpdateLayers(Doc.State, new HashSet<Guid> { target.Id }, l => l with { KerfMm = k }));
        _status.Text = $"\"{target.Name}\" now allows {k:0.000} mm for the kerf.";
        _status.Foreground = Brush("Ok");
        Lists();
    }

    /// <summary>
    /// The photo: either the whole of it (to click the slot's two ends) or the gauge's part of it laid
    /// straight, with the slots outlined and a marker across each where its key stops. Click or drag
    /// along a slot to move its marker.
    /// </summary>
    sealed class GaugeView : Control
    {
        Bitmap? _bitmap;
        Size _pixels;
        Rect2 _area;              // the bed area shown, when showing the gauge
        bool _gaugeMode;
        KerfGauge? _gauge;
        List<Vec2> _clicks = [];
        (double? Depth, bool Sure) _flat, _edge;
        bool? _dragging; // null: no; false: flat slot; true: edge slot

        public event Action<Vec2>? Clicked;
        public event Action<bool, double>? TipMoved;

        public GaugeView() => ClipToBounds = true;

        public void Clear() { _bitmap = null; _gauge = null; InvalidateVisual(); }

        static Bitmap ToBitmap(RasterImage img, int maxSide)
        {
            using var ms = new MemoryStream(img.EncodePng(maxSide));
            return new Bitmap(ms);
        }

        public void ShowPhoto(RasterImage img, List<Vec2> clicks)
        {
            _bitmap = ToBitmap(img, 1600);
            _pixels = new Size(img.Width, img.Height);
            _gaugeMode = false;
            _clicks = [.. clicks];
            InvalidateVisual();
        }

        public void ShowGauge(RasterImage img, Rect2 area, KerfGauge g)
        {
            _bitmap = ToBitmap(img, 0);
            _pixels = new Size(img.Width, img.Height);
            _area = area;
            _gauge = g;
            _gaugeMode = true;
            InvalidateVisual();
        }

        public void Marks(KerfGauge g, double? flat, bool flatSure, double? edge, bool edgeSure)
        {
            _gauge = g;
            (_flat, _edge) = ((flat, flatSure), (edge, edgeSure));
            InvalidateVisual();
        }

        Rect Fit()
        {
            if (_bitmap is null || _pixels.Width <= 0) return default;
            double k = Math.Min(Bounds.Width / _pixels.Width, Bounds.Height / _pixels.Height);
            double w = _pixels.Width * k, h = _pixels.Height * k;
            return new Rect((Bounds.Width - w) / 2, (Bounds.Height - h) / 2, w, h);
        }

        Point ToScreen(Vec2 mm)
        {
            var r = Fit();
            return new Point(r.X + (mm.X - _area.MinX) / _area.Width * r.Width, r.Y + (mm.Y - _area.MinY) / _area.Height * r.Height);
        }

        Vec2 ToMm(Point p)
        {
            var r = Fit();
            return new Vec2(_area.MinX + (p.X - r.X) / r.Width * _area.Width, _area.MinY + (p.Y - r.Y) / r.Height * _area.Height);
        }

        public override void Render(DrawingContext ctx)
        {
            ctx.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
            if (_bitmap is null)
            {
                var text = new FormattedText("No photo yet", System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 14, Brush("TextMuted"));
                ctx.DrawText(text, new Point((Bounds.Width - text.Width) / 2, (Bounds.Height - text.Height) / 2));
                return;
            }
            var r = Fit();
            ctx.DrawImage(_bitmap, r);
            var accent = new Pen(Brush("Accent"), 2);
            if (!_gaugeMode)
            {
                foreach (var c in _clicks)
                {
                    var p = new Point(r.X + c.X / _pixels.Width * r.Width, r.Y + c.Y / _pixels.Height * r.Height);
                    ctx.DrawEllipse(null, new Pen(Brushes.Black, 4), p, 7, 7);
                    ctx.DrawEllipse(null, accent, p, 7, 7);
                }
                return;
            }
            if (_gauge is not { } g) return;
            var slots = new List<(GaugeSlot Slot, (double? Depth, bool Sure) Mark)> { (g.Flat, _flat) };
            if (g.Edge is { } e) slots.Add((e, _edge));
            var outline = new Pen(new SolidColorBrush(Color.FromArgb(150, 255, 255, 255)), 1, DashStyle.Dash);
            foreach (var (slot, mark) in slots)
            {
                foreach (int side in new[] { -1, 1 })
                    ctx.DrawLine(outline, ToScreen(slot.Mouth + slot.Across * side * slot.MouthWidthMm / 2), ToScreen(slot.End + slot.Across * side * slot.EndWidthMm / 2));
                if (mark.Depth is not { } d) continue;
                var c = slot.At(Math.Clamp(d, 0, slot.Length));
                double half = slot.WidthAt(d) / 2 + 1.2;
                var a = ToScreen(c - slot.Across * half); var b = ToScreen(c + slot.Across * half);
                ctx.DrawLine(new Pen(Brushes.Black, 5), a, b);
                ctx.DrawLine(new Pen(mark.Sure ? Brush("Ok") : Brush("Warn"), 3), a, b);
            }
        }

        /// <summary>The slot a point is on (or beside), and how far in: within a millimetre and a half of its sides.</summary>
        (bool Edge, double Depth)? OnSlot(Vec2 mm)
        {
            if (_gauge is not { } g) return null;
            (bool, double)? best = null; double bestOff = double.PositiveInfinity;
            foreach (var (edge, slot) in new[] { (false, g.Flat), (true, g.Edge) })
            {
                if (slot is null) continue;
                double d = slot.DepthOf(mm), off = Math.Abs((mm - slot.Mouth).Dot(slot.Across));
                if (d < -0.5 || d > slot.Length + 0.5 || off > slot.WidthAt(d) / 2 + 1.5 || off >= bestOff) continue;
                best = (edge, Math.Clamp(d, 0, slot.Length)); bestOff = off;
            }
            return best;
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            var r = Fit();
            var p = e.GetPosition(this);
            if (_bitmap is null || !r.Contains(p)) return;
            if (!_gaugeMode)
            {
                Clicked?.Invoke(new Vec2((p.X - r.X) / r.Width * _pixels.Width, (p.Y - r.Y) / r.Height * _pixels.Height));
                return;
            }
            if (OnSlot(ToMm(p)) is { } hit)
            {
                _dragging = hit.Edge;
                TipMoved?.Invoke(hit.Edge, hit.Depth);
                e.Pointer.Capture(this);
            }
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            if (_dragging is not { } edge || _gauge is not { } g) return;
            var slot = edge ? g.Edge : g.Flat;
            if (slot is null) return;
            TipMoved?.Invoke(edge, Math.Clamp(slot.DepthOf(ToMm(e.GetPosition(this))), 0, slot.Length));
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            _dragging = null;
            e.Pointer.Capture(null);
        }
    }
}
