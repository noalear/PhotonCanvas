using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using static PhotonCanvas.App.UiKit;

namespace PhotonCanvas.App;

/// <summary>
/// Turns a picture into a depthmap with the user's DepthCharge: pick a model, generate,
/// tune the tone live (no new inference), optionally crop/mask in DepthCharge's own
/// editor, then add the depthmap to the design or save it.
/// </summary>
public sealed class DepthChargeWindow : Window
{
    sealed record ModelChoice(string Id, string Label, int[] Sizes, int DefaultSize, string Hint);

    static readonly ModelChoice[] Models =
    [
        new("quality", "DA3 Large (best all-round)", [518, 728, 1036], 1036, "Whole-image depth. DepthCharge's default; best for photos and most artwork."),
        new("hybrid", "DA3 + fine detail (research)", [518, 728, 1036], 1036, "DA3 for the overall shape plus Depth Pro's fine detail. Needs the Depth Pro download."),
        new("relief", "Local close-ups (experimental)", [518, 728, 1036], 518, "Overlapping close-ups for coins and ornament. Slower; can disturb the overall shape."),
        new("depthpro", "Depth Pro (research only)", [1536], 1536, "Apple's model, sharp boundaries. Non-commercial research licence."),
        new("fast", "V2 Small (fast)", [518], 518, "Quick baseline."),
    ];

    readonly DepthChargeService _service;
    readonly AppSettings _settings;
    readonly byte[] _source;
    readonly string _name;
    readonly Action<byte[], string> _addToDesign;

    readonly TextBlock _where = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
    readonly Button _chooseFolder = Button("Choose DepthCharge folder…");
    readonly ComboBox _model = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly ComboBox _size = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly TextBlock _modelHint = Hint();
    readonly Button _generate = new() { Content = "Generate depthmap", Classes = { "primary" }, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
    readonly ProgressBar _busy = new() { IsIndeterminate = true, IsVisible = false, Height = 4 };
    readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };

    readonly Slider _gamma = new() { Minimum = 0.2, Maximum = 3, Value = 1, SmallChange = 0.05 };
    readonly Slider _smooth = new() { Minimum = 0, Maximum = 5, Value = 0, SmallChange = 0.5 };
    readonly Slider _black = new() { Minimum = 0, Maximum = 99, Value = 0 };
    readonly Slider _white = new() { Minimum = 1, Maximum = 100, Value = 100 };
    readonly Slider _clip = new() { Minimum = 0, Maximum = 10, Value = 0, SmallChange = 0.5 };
    readonly ComboBox _polarity = new() { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = new[] { "Emboss · raised relief", "Deboss · recessed relief" }, SelectedIndex = 0 };
    readonly Slider _artwork = new() { Minimum = 0, Maximum = 1, Value = 0.25 };
    readonly Slider _blend = new() { Minimum = 0, Maximum = 2, Value = 0.5 };
    readonly Slider _scale = new() { Minimum = 2, Maximum = 64, Value = 12 };
    readonly Slider _texture = new() { Minimum = 0, Maximum = 1, Value = 0 };
    readonly CheckBox _removeBg = new() { Content = "Remove plain background" };
    readonly Control _artworkRow, _hybridRows;
    readonly Border _toneCard;

    readonly NumericUpDown _longest = Number(64, 8192, 256, "0");
    readonly ComboBox _bits = new() { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = new[] { "8-bit (for laser software)", "16-bit (master for editing)" }, SelectedIndex = 0 };
    readonly Image _sourceView = new() { Stretch = Stretch.Uniform };
    readonly Image _depthView = new() { Stretch = Stretch.Uniform };
    readonly Button _add = new() { Content = "Add depthmap to design", Classes = { "primary" }, IsEnabled = false, MinWidth = 180 };
    readonly Button _save = new() { Content = "Save PNG…", IsEnabled = false, MinWidth = 110 };
    readonly Button _open = new() { Content = "Open in DepthCharge (crop, mask, 3D)", IsEnabled = false };

    string? _jobId;
    string? _jobModel;
    int _previewTicket;
    CancellationTokenSource? _cts;

    public DepthChargeWindow(DepthChargeService service, AppSettings settings, byte[] sourceImage, string name, Action<byte[], string> addToDesign)
    {
        _service = service;
        _settings = settings;
        _source = sourceImage;
        _name = name;
        _addToDesign = addToDesign;
        Title = "Depthmap with DepthCharge";
        Width = 1040;
        Height = 760;
        MinWidth = 820;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");

        _model.ItemsSource = Models.Select(m => m.Label).ToList();
        _model.SelectionChanged += (_, _) => ModelChanged();
        _chooseFolder.Click += async (_, _) => await ChooseFolder();
        _generate.Click += async (_, _) => await Generate();
        _longest.Value = 2048;
        foreach (var s in new[] { _gamma, _smooth, _black, _white, _clip, _artwork, _blend, _scale, _texture }) s.ValueChanged += (_, _) => SchedulePreview();
        _polarity.SelectionChanged += (_, _) => SchedulePreview();
        _removeBg.IsCheckedChanged += (_, _) => SchedulePreview();
        _add.Click += async (_, _) => await Add();
        _save.Click += async (_, _) => await Save();
        _open.Click += (_, _) => { if (_jobId is { } id) OpenUrl($"{DepthChargeService.Url}/?job={id}"); };
        ToolTip.SetTip(_open, "DepthCharge's own editor: crop rectangle/circle, masks, colour masks and the 3D preview. Crops and masks you save there are used here: click Refresh preview afterwards.");
        var refresh = Button("Refresh preview", tip: "Fetch the depthmap again (after editing it in DepthCharge).");
        refresh.Click += (_, _) => SchedulePreview();
        Closing += (_, _) => _cts?.Cancel();

        _artworkRow = Labeled("Artwork detail", _artwork, "Brightness-assisted detail for local reconstruction. Keeps lettering, but shadows and paint can become false depth. 0 = model geometry only.");
        _hybridRows = new StackPanel
        {
            Spacing = 6,
            Children =
            {
                Labeled("Model detail", _blend, "How much of Depth Pro's fine detail is added on top of DA3 (0–2)."),
                Labeled("Detail scale", _scale, "Size of the detail added, in pixels at 1024 px."),
                Labeled("Image texture", _texture, "Adds picture brightness as detail. Can turn pigment or shadows into false depth."),
                _removeBg,
            },
        };
        _toneCard = Card("Shape the relief",
            Labeled("Midtone lift", _gamma, "Brightens or darkens the middle heights (gamma)."),
            Labeled("Smoothing", _smooth, "Depth-preserving smoothing; also smooths the exported depth."),
            Labeled("Black point", _black, "Percent of the range treated as deepest."),
            Labeled("White point", _white, "Percent of the range treated as shallowest."),
            Labeled("Ignore tails %", _clip, "Ignore this percent of extreme depths at each end before scaling (0.5–2 tames edge spikes)."),
            Field("Direction", _polarity, tip: "Emboss raises the subject; deboss sinks it. White is always shallow, black deep."),
            _artworkRow, _hybridRows,
            Row(refresh, _open));
        _toneCard.IsEnabled = false;

        var left = new ScrollViewer
        {
            Content = new StackPanel
            {
                Margin = new Thickness(0, 0, 12, 0),
                Children =
                {
                    Card("DepthCharge", _where, _chooseFolder),
                    Card("Estimate depth", Field("Model", _model), Field("Detail", _size), _modelHint, _generate, _busy, _status),
                    _toneCard,
                    Card("Output", Field("Longest edge (px)", _longest, tip: "Pixel size of the depthmap added or saved. Bigger only interpolates; it doesn't add detail."),
                        Field("Save as", _bits, tip: "Only for Save PNG. The design always uses 8 bits per pixel."),
                        Hint("White = shallow (left alone), black = deepest. Added as a picture over the original, on its own layer.")),
                },
            },
        };
        Grid.SetColumn(left, 0);
        var previews = new Grid { ColumnDefinitions = new("*,8,*"), RowDefinitions = new("Auto,*") };
        void Pane(string title, Image img, int col)
        {
            var t = new TextBlock { Text = title, Classes = { "h" }, Margin = new(0, 0, 0, 6) };
            Grid.SetColumn(t, col);
            previews.Children.Add(t);
            var b = new Border { Background = Brush("Bg0"), CornerRadius = new(6), Child = img, ClipToBounds = true };
            Grid.SetColumn(b, col);
            Grid.SetRow(b, 1);
            previews.Children.Add(b);
        }
        Pane("PICTURE", _sourceView, 0);
        Pane("DEPTHMAP", _depthView, 2);
        Grid.SetColumn(previews, 1);
        var close = new Button { Content = "Close", IsCancel = true, MinWidth = 100 };
        close.Click += (_, _) => Close();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = new(0, 12, 0, 0), Children = { close, _save, _add } };
        DockPanel.SetDock(buttons, Dock.Bottom);
        Content = new Border
        {
            Padding = new Thickness(16),
            Child = new DockPanel
            {
                Children =
                {
                    buttons,
                    new Grid { ColumnDefinitions = new("360,*"), Children = { left, previews } },
                },
            },
        };

        try { using var ms = new MemoryStream(sourceImage); _sourceView.Source = new Bitmap(ms); }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException) { }
        _model.SelectedIndex = 0;
        Opened += async (_, _) => await Connect();
    }

    static Control Labeled(string label, Slider s, string tip)
    {
        var value = new TextBlock { MinWidth = 40, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right, FontSize = 12 };
        void Show() => value.Text = s.Maximum > 10 ? $"{s.Value:0}" : $"{s.Value:0.##}";
        s.ValueChanged += (_, _) => Show();
        Show();
        var g = new Grid { ColumnDefinitions = new("110,*,Auto") };
        var l = new TextBlock { Text = label, Classes = { "muted" }, VerticalAlignment = VerticalAlignment.Center };
        g.Children.Add(l);
        Grid.SetColumn(s, 1); g.Children.Add(s);
        Grid.SetColumn(value, 2); g.Children.Add(value);
        ToolTip.SetTip(g, tip);
        return g;
    }

    ModelChoice CurrentModel => Models[Math.Max(0, _model.SelectedIndex)];

    void ModelChanged()
    {
        var m = CurrentModel;
        _size.ItemsSource = m.Sizes.Select(s => s switch { 518 => "Standard · 518 px", 728 => "Fine · 728 px", 1036 => "Maximum · 1036 px", _ => $"{s} px" }).ToList();
        _size.SelectedIndex = Array.IndexOf(m.Sizes, m.DefaultSize);
        _modelHint.Text = m.Hint;
    }

    DepthChargeHealth? _health;

    async Task Connect()
    {
        var folder = DepthChargeService.Find(_settings.DepthChargeFolder);
        _health = await _service.HealthAsync();
        if (_health is null && folder is null)
        {
            _where.Text = "DepthCharge wasn't found. Point to its folder (the one with Start DepthCharge.cmd), after running its Setup.";
            _generate.IsEnabled = false;
            return;
        }
        _where.Text = _health is not null ? "Connected to the running DepthCharge." : $"Found in {folder}. It starts in the background when you generate.";
        _generate.IsEnabled = true;
        ApplyHealth();
    }

    void ApplyHealth()
    {
        if (_health is not { } h) return;
        _where.Text = $"Connected · {(h.Gpu ? "graphics card (DirectML)" : "processor only, slower")}" + (h.DepthPro ? " · Depth Pro installed" : "");
        // Depth Pro models need the separate research download.
        if (!h.DepthPro && CurrentModel.Id is "hybrid" or "depthpro") _model.SelectedIndex = 0;
    }

    async Task ChooseFolder()
    {
        var picked = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "DepthCharge folder", AllowMultiple = false });
        if (picked.Count == 0 || picked[0].TryGetLocalPath() is not { } path) return;
        if (!DepthChargeService.IsUsable(path))
        {
            _where.Text = "That folder isn't a set-up DepthCharge (it needs runtime\\python\\python.exe and app.py; run its Setup.cmd once).";
            return;
        }
        _settings.DepthChargeFolder = path;
        _settings.Save();
        await Connect();
    }

    async Task Generate()
    {
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        var m = CurrentModel;
        int size = m.Sizes[Math.Max(0, _size.SelectedIndex)];
        _generate.IsEnabled = false;
        _busy.IsVisible = true;
        try
        {
            var folder = DepthChargeService.Find(_settings.DepthChargeFolder) ?? "";
            _health = await _service.EnsureRunningAsync(folder, new Progress<string>(s => _status.Text = s), cts.Token);
            ApplyHealth();
            if (m.Id is "hybrid" or "depthpro" && !_health.DepthPro)
            {
                _status.Text = "This model needs Depth Pro, which isn't downloaded in DepthCharge (see its README: download_depthpro.py).";
                return;
            }
            _status.Text = "Loading the model and estimating depth… (the first run takes longer)";
            var job = await _service.SubmitAsync(_source, _name, m.Id, size, cts.Token);
            var started = DateTime.Now;
            while (job.Status is "queued" or "running")
            {
                await Task.Delay(600, cts.Token);
                job = await _service.StatusAsync(job.Id, cts.Token);
                _status.Text = job.Status == "queued" ? "Waiting in DepthCharge's queue…"
                    : $"Estimating depth… {(DateTime.Now - started).TotalSeconds:0} s" + (job.PatchesDone is { } n ? $" · {n} close-ups" : "");
            }
            if (job.Status != "ready")
            {
                _status.Text = $"DepthCharge couldn't make the depthmap: {job.Error ?? job.Status}";
                return;
            }
            _jobId = job.Id;
            _jobModel = m.Id;
            _status.Text = $"Done{(job.Seconds is { } s ? $" in {s:0.0} s" : "")}. Shape it with the sliders; they don't re-run the model.";
            _artworkRow.IsVisible = m.Id == "relief";
            _hybridRows.IsVisible = m.Id == "hybrid";
            _toneCard.IsEnabled = _add.IsEnabled = _save.IsEnabled = _open.IsEnabled = true;
            SchedulePreview();
        }
        catch (OperationCanceledException) { }
        catch (Exception e) when (e is InvalidOperationException or TimeoutException or HttpRequestException or System.ComponentModel.Win32Exception)
        {
            _status.Text = e.Message;
            AppLog.Write("depthcharge", e.ToString());
        }
        finally
        {
            _busy.IsVisible = false;
            _generate.IsEnabled = true;
        }
    }

    DepthTone Tone => new()
    {
        Gamma = _gamma.Value,
        Smooth = _smooth.Value,
        Black = Math.Min(_black.Value, _white.Value - 1),
        White = _white.Value,
        Clip = _clip.Value,
        Deboss = _polarity.SelectedIndex == 1,
        ArtworkDetail = _jobModel == "relief" ? _artwork.Value : 0,
        Blend = _blend.Value,
        DetailScale = _scale.Value,
        Texture = _texture.Value,
        RemoveBackground = _removeBg.IsChecked == true,
    };

    void SchedulePreview()
    {
        if (_jobId is not { } id) return;
        int ticket = ++_previewTicket;
        _ = Later();

        async Task Later()
        {
            await Task.Delay(200);
            if (ticket != _previewTicket) return;
            try
            {
                var png = await _service.DepthPngAsync(id, Tone, 900);
                if (ticket != _previewTicket) return;
                var old = _depthView.Source as IDisposable;
                using var ms = new MemoryStream(png);
                _depthView.Source = new Bitmap(ms);
                old?.Dispose();
            }
            catch (Exception e) when (e is InvalidOperationException or HttpRequestException or TaskCanceledException)
            {
                _status.Text = e.Message;
            }
        }
    }

    async Task<byte[]?> Export(int bits)
    {
        if (_jobId is not { } id) return null;
        try { return await _service.DepthPngAsync(id, Tone, (int)(_longest.Value ?? 2048), bits); }
        catch (Exception e) when (e is InvalidOperationException or HttpRequestException or TaskCanceledException)
        {
            _status.Text = e.Message;
            return null;
        }
    }

    async Task Add()
    {
        _add.IsEnabled = false;
        try
        {
            if (await Export(8) is { } png)
            {
                _addToDesign(png, $"{_name} depthmap");
                Close();
            }
        }
        finally { _add.IsEnabled = _jobId is not null; }
    }

    async Task Save()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save depthmap",
            SuggestedFileName = $"{_name}-depthmap.png",
            DefaultExtension = "png",
            FileTypeChoices = [new FilePickerFileType("PNG image") { Patterns = ["*.png"] }],
        });
        if (file?.TryGetLocalPath() is not { } path) return;
        int bits = _bits.SelectedIndex == 1 ? 16 : 8;
        if (await Export(bits) is { } png)
        {
            await File.WriteAllBytesAsync(path, png);
            _status.Text = $"Saved {bits}-bit depthmap to {path}.";
        }
    }

    static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { AppLog.Write("depthcharge", e.Message); }
    }
}
