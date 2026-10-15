using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using PhotonCanvas.Core;
using PhotonCanvas.Core.Camera;
using PhotonCanvas.Device;
using SkiaSharp;
using static PhotonCanvas.App.UiKit;

namespace PhotonCanvas.App;

/// <summary>
/// Snaps the selected design onto a real piece seen by the camera: checks the
/// height first (the camera picture is only true at the right height), takes a
/// photo, finds the piece's edges and fits the outline to them (move, turn and
/// optionally resize). Nothing changes until Apply.
/// </summary>
public sealed class FitToCameraWindow : Window
{
    const double MmPerPx = 0.15;

    readonly MachinePanel _machine;
    readonly IReadOnlyList<IReadOnlyList<Vec2>> _outline;
    readonly Rect2 _bounds;
    readonly Action<FitResult> _apply;
    readonly MachineProfile _profile = MachineProfile.F2Ultra;

    readonly NumericUpDown _distance = Number(0, 60, 0.1m, "0.0");
    readonly TextBlock _heightState = new() { TextWrapping = TextWrapping.Wrap };
    readonly Button _focus = Button("Auto-focus now", tip: "The machine measures the material under its focus sensor and moves to it. Put the piece under the sensor first, or type the distance.");
    readonly ComboBox _rotation = new() { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = new[] { "A little (±30°)", "Any angle" }, SelectedIndex = 0 };
    readonly CheckBox _resize = new() { Content = "Allow resizing (±15 %)", IsChecked = false };
    readonly NumericUpDown _search = Number(2, 80, 5, "0");
    readonly Button _fit = new() { Content = "Take photo and fit", Classes = { "primary" }, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
    readonly Button _applyButton = new() { Content = "Apply", Classes = { "primary" }, MinWidth = 110, IsEnabled = false };
    readonly Image _preview = new() { Stretch = Stretch.Uniform, MaxHeight = 360 };
    readonly TextBlock _result = new() { TextWrapping = TextWrapping.Wrap };
    readonly ProgressBar _busy = new() { IsIndeterminate = true, IsVisible = false, Height = 4 };
    FitResult? _last;
    CancellationTokenSource? _cts;

    public FitToCameraWindow(MachinePanel machine, IReadOnlyList<IReadOnlyList<Vec2>> outline, Action<FitResult> apply)
    {
        _machine = machine;
        _outline = outline;
        _bounds = Rect2.Bounds(outline.SelectMany(p => p));
        _apply = apply;
        Title = "Fit to piece in camera";
        Width = 560;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");

        _distance.Value = machine.Distance is { } d ? (decimal)Math.Round(d, 1) : null;
        _distance.ValueChanged += (_, _) => CheckHeight();
        _focus.Click += async (_, _) => await AutoFocus();
        _search.Value = 20;
        _fit.Click += async (_, _) => await RunFit();
        _applyButton.Click += (_, _) => { if (_last is { } r) { _apply(r); Close(); } };
        var close = new Button { Content = "Close", IsCancel = true, MinWidth = 100 };
        close.Click += (_, _) => Close();
        Closing += (_, _) => _cts?.Cancel();
        ToolTip.SetTip(_rotation, "How much the piece may be turned compared with your design.");
        ToolTip.SetTip(_resize, "Let the design grow or shrink to match the piece (keeps proportions).");

        Content = new Border
        {
            Padding = new Thickness(20),
            Child = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    new TextBlock
                    {
                        TextWrapping = TextWrapping.Wrap,
                        Text = "Moves your selected design onto the real piece the camera sees. First place the design roughly over the piece " +
                               "(turn on Camera on work area to see it), then fit.",
                    },
                    new TextBlock { Text = "1  HEIGHT", Classes = { "h" } },
                    Hint("The camera picture only lines up at the right height: use the distance to the top of the piece."),
                    Field("Distance (mm)", _distance, tip: "Distance from the laser to the top of the piece, as the machine's focus uses it."),
                    _focus,
                    _heightState,
                    new TextBlock { Text = "2  FIT", Classes = { "h" }, Margin = new(0, 6, 0, 0) },
                    Field("Turn", _rotation),
                    _resize,
                    Field("Search (mm)", _search, tip: "How far from where it is now the design may move."),
                    _fit,
                    _busy,
                    new Border { Background = Brush("Bg0"), CornerRadius = new(6), MinHeight = 60, Child = _preview, ClipToBounds = true },
                    _result,
                    new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { close, _applyButton } },
                },
            },
        };
        CheckHeight();
    }

    double? DistanceValue => _distance.Value is { } v ? (double)v : null;

    /// <summary>The fit is only allowed when the camera is calibrated for this height.</summary>
    bool CheckHeight()
    {
        bool ok = false;
        if (!_machine.IsConnected) _heightState.Text = "⚠ Connect to the machine first.";
        else if (DistanceValue is not { } d) _heightState.Text = "⚠ Enter the distance, or click Auto-focus now.";
        else if (_machine.CameraMapFor(d) is not { } choice) _heightState.Text = "⚠ The camera isn't calibrated yet (Camera tab → Calibrate).";
        else
        {
            ok = choice.Exact;
            _heightState.Text = (ok ? "✓ Camera " : "⚠ Camera ") + choice.Description + "." +
                (ok ? "" : " Fitting is off until the camera is calibrated near this height.");
            if (ok && _machine.Distance is { } md && Math.Abs(md - d) > 0.3)
                _heightState.Text += $"\nNote: the machine's own focus is at {md:0.0} mm. Make sure it's focused on the piece before cutting.";
        }
        _heightState.Foreground = ok ? Brush("Ok") : Brush("Warn");
        _focus.IsEnabled = _machine.IsConnected;
        _fit.IsEnabled = ok;
        return ok;
    }

    async Task AutoFocus()
    {
        _focus.IsEnabled = false;
        _heightState.Text = "Focusing… the head moves.";
        try
        {
            if (await _machine.AutoFocusNowAsync() is { } d) _distance.Value = (decimal)Math.Round(d, 1);
        }
        finally { _focus.IsEnabled = true; CheckHeight(); }
    }

    async Task RunFit()
    {
        if (!CheckHeight() || DistanceValue is not { } dist) return;
        var opt = new FitOptions
        {
            MaxRotationDeg = _rotation.SelectedIndex == 1 ? 180 : 30,
            MaxScaleChange = _resize.IsChecked == true ? 0.15 : 0,
            SearchMm = (double)(_search.Value ?? 20),
        };
        // Photograph the design's surroundings, plus room for it to move, turn and grow.
        double reach = opt.SearchMm + Math.Max(_bounds.Width, _bounds.Height) * (opt.MaxRotationDeg > 30 ? 0.5 : 0.25) + 5;
        var area = new Rect2(Math.Max(0, _bounds.MinX - reach), Math.Max(0, _bounds.MinY - reach),
            Math.Min(_profile.FieldW, _bounds.MaxX + reach), Math.Min(_profile.FieldH, _bounds.MaxY + reach));

        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        _fit.IsEnabled = _applyButton.IsEnabled = false;
        _busy.IsVisible = true;
        _result.Text = "Taking a photo…";
        try
        {
            using var photo = await _machine.CaptureAreaAsync(area, MmPerPx, dist);
            _result.Text = "Looking for the piece…";
            var gray = CameraWarp.ToGray(photo);
            var fit = await Task.Run(() => ShapeFit.Fit(gray, area, MmPerPx, _outline, opt, cts.Token), cts.Token);
            _last = fit;
            ShowPreview(photo, area, fit);
            string quality = fit.OnEdge >= 0.75 ? "Good match" : fit.OnEdge >= 0.5 ? "Partial match: check it before applying" : "Poor match: the piece may be elsewhere, too faint, or a different shape";
            _result.Text = $"{quality}. {fit.OnEdge:P0} of the outline sits on the piece's edge" +
                (double.IsFinite(fit.RmsMm) ? $" (typically {fit.RmsMm:0.00} mm off)" : "") + ".\n" +
                $"Move {fit.Offset.X:+0.0;-0.0} mm across, {fit.Offset.Y:+0.0;-0.0} mm down, turn {fit.AngleDeg:+0.0;-0.0}°" +
                (Math.Abs(fit.Scale - 1) > 0.0005 ? $", size {fit.Scale * 100:0.0} %" : "") + ".";
            _applyButton.IsEnabled = fit.OnEdge > 0.2;
        }
        catch (OperationCanceledException) { }
        catch (DeviceException e) { _result.Text = $"Couldn't take the photo: {e.Message}"; }
        catch (FormatException e) { _result.Text = $"Couldn't read the camera picture: {e.Message}"; }
        catch (InvalidOperationException e) { _result.Text = e.Message; }
        finally
        {
            _busy.IsVisible = false;
            CheckHeight();
        }
    }

    /// <summary>The photo with the design where it is now (grey) and where it would go (green).</summary>
    void ShowPreview(SKBitmap photo, Rect2 area, FitResult fit)
    {
        using var surface = SKSurface.Create(new SKImageInfo(photo.Width, photo.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        var c = surface.Canvas;
        c.DrawBitmap(photo, 0, 0);
        float stroke = Math.Max(1.5f, photo.Width / 400f);
        using var before = new SKPaint { Color = new SKColor(255, 255, 255, 140), Style = SKPaintStyle.Stroke, StrokeWidth = stroke, IsAntialias = true, PathEffect = SKPathEffect.CreateDash([6, 5], 0) };
        using var after = new SKPaint { Color = new SKColor(60, 230, 120), Style = SKPaintStyle.Stroke, StrokeWidth = stroke * 1.3f, IsAntialias = true };
        foreach (var path in _outline)
        {
            using (var was = ToPath(path, p => p)) c.DrawPath(was, before);
            using (var will = ToPath(path, fit.Apply)) c.DrawPath(will, after);
        }
        using var img = surface.Snapshot();
        using var bmp = SKBitmap.FromImage(img);
        var old = _preview.Source as IDisposable;
        _preview.Source = CameraImaging.ToAvalonia(bmp);
        old?.Dispose();

        SKPath ToPath(IReadOnlyList<Vec2> pts, Func<Vec2, Vec2> map)
        {
            var sk = new SKPath();
            for (int i = 0; i < pts.Count; i++)
            {
                var p = map(pts[i]);
                float x = (float)((p.X - area.MinX) / MmPerPx), y = (float)((p.Y - area.MinY) / MmPerPx);
                if (i == 0) sk.MoveTo(x, y); else sk.LineTo(x, y);
            }
            return sk;
        }
    }
}
