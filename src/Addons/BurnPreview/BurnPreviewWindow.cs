using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Bitmap = Avalonia.Media.Imaging.Bitmap;
using Avalonia.Threading;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Materials;
using PhotonCanvas.App;
using PhotonCanvas.App.Addons;
using static PhotonCanvas.App.UiKit;

namespace PhotonCanvas.Addon.BurnPreview;

/// <summary>
/// "See it before you burn it": the design drawn as it will come out, on the camera's picture of
/// the actual material, with each layer's darkness and colour taken from the user's own test
/// grids. Follows the design and the camera while it's open. Never sends anything to the machine.
/// </summary>
public sealed class BurnPreviewWindow : Window
{
    /// <summary>Room round the design, mm.</summary>
    const double MarginMm = 8;
    /// <summary>The preview is worked out at no more than this many pixels on its long side.</summary>
    const int MaxSide = 1600;

    readonly IAddonHost _host;

    readonly Image _image = new() { Stretch = Stretch.Uniform };
    readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
    readonly StackPanel _legend = new() { Spacing = 6 };
    readonly Button _compare = Button("Hold to see before", tip: "Hold down to see the material as it is now; let go to see it burned.");
    readonly Button _refresh = Button("New camera photo", "IcCamera", "Take a fresh picture of the bed (the camera only: nothing moves and the laser stays off).");
    readonly CheckBox _onCanvas = new() { Content = "Show on the canvas", IsChecked = false };
    readonly ProgressBar _busy = new() { IsIndeterminate = true, IsVisible = false, Height = 3 };

    Bitmap? _before, _after;
    CancellationTokenSource? _cts;
    readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(350) };

    readonly Func<bool> _canvasOn;
    readonly Action<bool> _setCanvas;

    /// <param name="canvasOn">Is the burned look shown on the canvas; <paramref name="setCanvas"/> turns it on or off.</param>
    public BurnPreviewWindow(IAddonHost host, Func<bool> canvasOn, Action<bool> setCanvas)
    {
        _host = host;
        _canvasOn = canvasOn;
        _setCanvas = setCanvas;
        Title = "See it before you burn it";
        Width = 900;
        Height = 640;
        MinWidth = 600;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");

        _debounce.Tick += (_, _) => { _debounce.Stop(); _ = Redraw(); };
        _host.DocumentChanged += Changed;
        _host.PhotoChanged += Changed;
        Closed += (_, _) =>
        {
            _host.DocumentChanged -= Changed;
            _host.PhotoChanged -= Changed;
            _debounce.Stop();
            _cts?.Cancel();
        };
        _compare.AddHandler(PointerPressedEvent, (_, _) => { if (_before is not null) _image.Source = _before; }, handledEventsToo: true);
        _compare.AddHandler(PointerReleasedEvent, (_, _) => { if (_after is not null) _image.Source = _after; }, handledEventsToo: true);
        _compare.AddHandler(PointerCaptureLostEvent, (_, _) => { if (_after is not null) _image.Source = _after; }, handledEventsToo: true);
        _refresh.Click += async (_, _) => await NewPhoto();
        _onCanvas.IsChecked = canvasOn();
        _onCanvas.IsCheckedChanged += (_, _) => { if (_onCanvas.IsChecked != _canvasOn()) _setCanvas(_onCanvas.IsChecked == true); };
        ToolTip.SetTip(_onCanvas, "Draw the burned look in place of the camera picture on the work area (turn Camera on work area on to see it).");

        var side = new StackPanel
        {
            Spacing = 10,
            Width = 270,
            Children =
            {
                new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap,
                    Text = "Your design as it will come out, drawn on the camera's picture of the material on the bed. " +
                           "Each layer's darkness and colour come from your own colour and tone tests on that material.",
                },
                _status,
                _busy,
                new StackPanel { Spacing = 6, Children = { _compare, _refresh, _onCanvas } },
                new TextBlock { Text = "LAYERS", Classes = { "h" }, Margin = new(0, 6, 0, 0) },
                new ScrollViewer { Content = _legend, MaxHeight = 320 },
            },
        };
        var close = new Button { Content = "Close", IsCancel = true, MinWidth = 100, HorizontalAlignment = HorizontalAlignment.Right };
        close.Click += (_, _) => Close();
        var grid = new Grid { ColumnDefinitions = new("*,Auto"), RowDefinitions = new("*,Auto"), Margin = new(16) };
        var picture = new Border { Background = Brush("Bg0"), CornerRadius = new(6), ClipToBounds = true, Margin = new(0, 0, 14, 0), Child = _image };
        grid.Children.Add(picture);
        Grid.SetColumn(side, 1);
        grid.Children.Add(side);
        Grid.SetRow(close, 1);
        Grid.SetColumnSpan(close, 2);
        close.Margin = new(0, 10, 0, 0);
        grid.Children.Add(close);
        Content = grid;
        Opened += (_, _) => _ = Redraw();
    }

    void Changed() { _debounce.Stop(); _debounce.Start(); }

    /// <summary>The canvas toggle was changed elsewhere (the ribbon): show it here too.</summary>
    public void CanvasToggled() => _onCanvas.IsChecked = _canvasOn();

    async Task NewPhoto()
    {
        if (!_host.IsConnected) { _status.Text = "Connect to the machine to take a camera picture."; _status.Foreground = Brush("Warn"); return; }
        _refresh.IsEnabled = false;
        _status.Text = "Taking a camera picture…";
        try
        {
            if (await _host.TakePhotoAsync() is null) _status.Text = "No camera picture came back.";
        }
        catch (Exception e) when (e is PhotonCanvas.Device.DeviceException or InvalidOperationException or TimeoutException) { _status.Text = $"Couldn't take a picture: {e.Message}"; }
        finally { _refresh.IsEnabled = true; }
        await Redraw();
    }

    async Task Redraw()
    {
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        var state = _host.Document.State;
        var photo = _host.Photo;
        var library = _host.Library;
        _busy.IsVisible = true;
        try
        {
            var made = await Task.Run(() => Make(state, photo, library, cts.Token), cts.Token);
            if (cts.IsCancellationRequested) return;
            if (made is null)
            {
                _status.Text = "Nothing to show: put something on a layer that's sent to the machine.";
                SetImages(null, null);
                _legend.Children.Clear();
                return;
            }
            var (before, crop) = made.Value;
            SetImages(Bitmaps.From(before.Image), Bitmaps.From(crop.Picture.Image));
            _status.Text = photo is null
                ? "No camera picture yet, so it's drawn on a stand-in for the material. Take a camera picture for the real thing."
                : crop.AllMeasured ? "Every layer is drawn from your own tests." : "Some layers are a guess: they have no test on this material yet (see below).";
            _status.Foreground = photo is not null && crop.AllMeasured ? Brush("Ok") : Brush("Warn");
            ShowLegend(crop);
        }
        catch (OperationCanceledException) { }
        finally { if (ReferenceEquals(cts, _cts)) _busy.IsVisible = false; }
    }

    /// <summary>Before and after, over the design and a little round it.</summary>
    static (BedPicture Before, BurnPreviewResult After)? Make(DocState state, CameraPhoto? photo, MaterialLibrary library, CancellationToken cancel)
    {
        if (BurnPreview.DesignBounds(state) is not { IsEmpty: false } design) return null;
        var area = new Rect2(design.MinX - MarginMm, design.MinY - MarginMm, design.MaxX + MarginMm, design.MaxY + MarginMm);
        var bed = BurnPreview.Bed(state, photo is null ? null : new BedPicture(photo.Image, photo.Area, photo.MmPerPx), library, area, MaxSide);
        return (bed, BurnPreview.Render(bed, state, library, cancel: cancel));
    }

    void SetImages(Bitmap? before, Bitmap? after)
    {
        var oldBefore = _before;
        var oldAfter = _after;
        _before = before;
        _after = after;
        _image.Source = after;
        oldBefore?.Dispose();
        oldAfter?.Dispose();
    }

    void ShowLegend(BurnPreviewResult r)
    {
        _legend.Children.Clear();
        foreach (var l in r.Layers)
        {
            var look = l.Look;
            var swatch = new Border
            {
                Width = 22, Height = 22, CornerRadius = new(4), VerticalAlignment = VerticalAlignment.Top,
                Background = new SolidColorBrush(Color.FromRgb((byte)(look.Bare >> 16), (byte)(look.Bare >> 8), (byte)look.Bare)),
                Child = new Border
                {
                    Margin = new(4), CornerRadius = new(2), Opacity = Math.Clamp(look.Strength, 0.15, 1),
                    Background = new SolidColorBrush(look.ThroughCut ? Color.FromRgb(0x17, 0x18, 0x1B) : Color.FromRgb((byte)(look.Mark >> 16), (byte)(look.Mark >> 8), (byte)look.Mark)),
                },
            };
            string from = look.Source switch
            {
                LookSource.ColourTest => "✓ measured",
                LookSource.ToneTest => "✓ measured",
                LookSource.NearestColourTest => "≈ nearest test",
                _ => "? guess",
            };
            var text = new StackPanel
            {
                Spacing = 1,
                Children =
                {
                    new TextBlock { Text = $"{l.Name} · {l.Mode.ToString().ToLowerInvariant()}", FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap },
                    new TextBlock { Text = from, FontSize = 11.5, Foreground = look.Source == LookSource.Estimate ? Brush("Warn") : Brush("Ok") },
                    Muted(Capital(look.About)).With(t => t.TextWrapping = TextWrapping.Wrap),
                },
            };
            _legend.Children.Add(new DockPanel { Children = { swatch.With(s => { DockPanel.SetDock(s, Dock.Left); s.Margin = new(0, 2, 8, 0); }), text } });
        }
    }

    static string Capital(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
