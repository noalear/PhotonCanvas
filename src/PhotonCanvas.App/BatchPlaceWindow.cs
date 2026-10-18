using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using PhotonCanvas.Core;
using PhotonCanvas.Core.Camera;
using PhotonCanvas.Core.Imaging;
using PhotonCanvas.Device;
using PhotonCanvas.Doc;
using SkiaSharp;
using static PhotonCanvas.App.UiKit;

namespace PhotonCanvas.App;

/// <summary>Where one copy of the design goes: the move from where the design is now.</summary>
public sealed record Placement(Affine Move, FoundPiece Piece);

/// <summary>
/// Batch placing: photographs the whole bed, finds every piece lying on it (whatever isn't the
/// bed), and puts a copy of the selected design on each, turned to match and optionally snapped
/// to the piece's edges. Nothing changes until Apply. Copies are numbered back to front, left
/// to right, so serial numbers and spreadsheet rows count up in that order.
/// </summary>
public sealed class BatchPlaceWindow : Window
{
    const double MmPerPx = 0.4;

    readonly MachinePanel _machine;
    readonly IReadOnlyList<IReadOnlyList<Vec2>> _outline;
    readonly Rect2 _bounds;
    readonly Action<List<Placement>> _apply;
    readonly MachineProfile _profile = MachineProfile.F2Ultra;

    readonly NumericUpDown _distance = Number(0, 60, 0.1m, "0.0");
    readonly TextBlock _heightState = new() { TextWrapping = TextWrapping.Wrap };
    readonly Slider _tolerance = new() { Minimum = 3, Maximum = 60, Value = 18, HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly NumericUpDown _minArea = Number(5, 20000, 10, "0");
    readonly CheckBox _turn = new() { Content = "Turn each copy to match its piece", IsChecked = true };
    readonly CheckBox _snap = new() { Content = "Snap each copy to its piece's edges (when the design is the piece's outline)", IsChecked = false };
    readonly Button _find = new() { Content = "Take photo and find pieces", Classes = { "primary" }, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
    readonly Button _applyButton = new() { Content = "Apply", Classes = { "primary" }, MinWidth = 110, IsEnabled = false };
    readonly Image _preview = new() { Stretch = Stretch.Uniform, MaxHeight = 380 };
    readonly TextBlock _result = new() { TextWrapping = TextWrapping.Wrap };
    readonly ProgressBar _busy = new() { IsIndeterminate = true, IsVisible = false, Height = 4 };
    List<Placement>? _last;
    CancellationTokenSource? _cts;

    public BatchPlaceWindow(MachinePanel machine, IReadOnlyList<IReadOnlyList<Vec2>> outline, Action<List<Placement>> apply)
    {
        _machine = machine;
        _outline = outline;
        _bounds = Rect2.Bounds(outline.SelectMany(p => p));
        _apply = apply;
        Title = "Place on every piece";
        Width = 600;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");

        _distance.Value = machine.Distance is { } d ? (decimal)Math.Round(d, 1) : null;
        _distance.ValueChanged += (_, _) => CheckHeight();
        _minArea.Value = 40;
        _find.Click += async (_, _) => await Find();
        _applyButton.Click += (_, _) => { if (_last is { Count: > 0 } r) { _apply(r); Close(); } };
        var close = new Button { Content = "Close", IsCancel = true, MinWidth = 100 };
        close.Click += (_, _) => Close();
        Closing += (_, _) => _cts?.Cancel();
        ToolTip.SetTip(_tolerance, "How different from the bed a colour must be to count as a piece. Raise it if the bed's pattern shows up as pieces; lower it if pale pieces are missed.");

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
                        Text = "Lay the pieces on the bed, apart from each other. A copy of the selected design goes on every piece the camera finds; " +
                               "texts and codes with fields get the next serial number or spreadsheet row on each one.",
                    },
                    new TextBlock { Text = "1  HEIGHT", Classes = { "h" } },
                    Hint("The camera picture only lines up at the right height: use the distance to the top of the pieces (all the same thickness)."),
                    Field("Distance (mm)", _distance, tip: "Distance from the laser to the top of the pieces, as the machine's focus uses it."),
                    _heightState,
                    new TextBlock { Text = "2  FIND", Classes = { "h" }, Margin = new(0, 6, 0, 0) },
                    Field("Tolerance", _tolerance),
                    Field("Smallest piece (mm²)", _minArea, tip: "Specks, scraps and reflections smaller than this are ignored."),
                    _turn,
                    _snap,
                    _find,
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

    bool CheckHeight()
    {
        bool ok = false;
        if (!_machine.IsConnected) _heightState.Text = "⚠ Connect to the machine first.";
        else if (DistanceValue is not { } d) _heightState.Text = "⚠ Enter the distance to the top of the pieces.";
        else if (_machine.CameraMapFor(d) is not { } choice) _heightState.Text = "⚠ The camera isn't calibrated yet (Camera tab → Calibrate).";
        else
        {
            ok = choice.Exact;
            _heightState.Text = (ok ? "✓ Camera " : "⚠ Camera ") + choice.Description + "." + (ok ? "" : " Finding is off until the camera is calibrated near this height.");
        }
        _heightState.Foreground = ok ? Brush("Ok") : Brush("Warn");
        _find.IsEnabled = ok;
        return ok;
    }

    async Task Find()
    {
        if (!CheckHeight() || DistanceValue is not { } dist) return;
        var area = new Rect2(0, 0, _profile.FieldW, _profile.FieldH);
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        _find.IsEnabled = _applyButton.IsEnabled = false;
        _busy.IsVisible = true;
        _result.Text = "Taking a photo of the bed…";
        try
        {
            using var photo = await _machine.CaptureAreaAsync(area, MmPerPx, dist);
            _result.Text = "Looking for pieces…";
            var raster = CameraImaging.ToRaster(photo);
            var gray = CameraWarp.ToGray(photo);
            double tol = _tolerance.Value, minArea = (double)(_minArea.Value ?? 40);
            bool turn = _turn.IsChecked == true, snap = _snap.IsChecked == true;
            var placements = await Task.Run(() =>
            {
                var keep = BackgroundRemoval.Mask(raster, new BackgroundOptions { Tolerance = tol, FromEdges = true, Shrink = 1, Feather = 0 });
                var pieces = PieceFinder.Find(keep, raster.Width, raster.Height, area, MmPerPx, minArea);
                return Place(pieces, turn, snap ? gray : null, area, cts.Token);
            }, cts.Token);
            _last = placements;
            ShowPreview(photo, area, placements);
            _result.Text = placements.Count switch
            {
                0 => "No pieces found. Check the pieces stand out from the bed (try a higher or lower tolerance), and that they're inside the work area.",
                1 => "1 piece found.",
                _ => $"{placements.Count} pieces found, numbered in the order they'll take serial numbers / rows.",
            };
            _applyButton.IsEnabled = placements.Count > 0;
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

    /// <summary>A move per piece: the design's middle onto the piece's, turned to match, then snapped to the edges when asked.</summary>
    List<Placement> Place(List<FoundPiece> pieces, bool turn, GrayImage? snapTo, Rect2 area, CancellationToken ct)
    {
        var c0 = _bounds.Center;
        double designAxis = PieceFinder.LongAxisDeg(_outline.SelectMany(p => p));
        var o = new List<Placement>();
        foreach (var p in pieces)
        {
            ct.ThrowIfCancellationRequested();
            double angle = turn ? PieceFinder.TurnOnto(designAxis, p.AngleDeg) : 0;
            var move = Affine.RotateAbout(c0, angle).Then(Affine.Translate(p.Centre - c0));
            if (snapTo is not null)
            {
                var moved = _outline.Select(path => (IReadOnlyList<Vec2>)path.Select(move.Apply).ToList()).ToList();
                var fit = ShapeFit.Fit(snapTo, area, MmPerPx, moved, new FitOptions { MaxRotationDeg = 15, MaxScaleChange = 0, SearchMm = 8 }, ct);
                if (fit.OnEdge >= 0.5)
                    move = move.Then(Affine.ScaleAbout(fit.Center, fit.Scale, fit.Scale).Then(Affine.RotateAbout(fit.Center, fit.AngleDeg)).Then(Affine.Translate(fit.Offset)));
            }
            o.Add(new Placement(move, p));
        }
        return o;
    }

    /// <summary>The photo with each piece's long axis and its numbered copy (green).</summary>
    void ShowPreview(SKBitmap photo, Rect2 area, List<Placement> placements)
    {
        using var surface = SKSurface.Create(new SKImageInfo(photo.Width, photo.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        var c = surface.Canvas;
        c.DrawBitmap(photo, 0, 0);
        float stroke = Math.Max(1.5f, photo.Width / 350f);
        using var copy = new SKPaint { Color = new SKColor(60, 230, 120), Style = SKPaintStyle.Stroke, StrokeWidth = stroke, IsAntialias = true };
        using var axis = new SKPaint { Color = new SKColor(255, 181, 71, 200), Style = SKPaintStyle.Stroke, StrokeWidth = stroke * 0.8f, IsAntialias = true };
        float size = Math.Max(12, photo.Width / 40f);
        using var label = new SKPaint { Color = SKColors.White, IsAntialias = true, TextSize = size };
        using var labelBack = new SKPaint { Color = new SKColor(20, 22, 28, 200), IsAntialias = true };
        SKPoint Px(Vec2 p) => new((float)((p.X - area.MinX) / MmPerPx), (float)((p.Y - area.MinY) / MmPerPx));
        for (int i = 0; i < placements.Count; i++)
        {
            var (move, piece) = (placements[i].Move, placements[i].Piece);
            double r = piece.AngleDeg * Math.PI / 180, half = piece.LengthMm / 2;
            var d = new Vec2(Math.Cos(r), Math.Sin(r)) * half;
            c.DrawLine(Px(piece.Centre - d), Px(piece.Centre + d), axis);
            foreach (var path in _outline)
            {
                using var sk = new SKPath();
                for (int k = 0; k < path.Count; k++) { var q = Px(move.Apply(path[k])); if (k == 0) sk.MoveTo(q); else sk.LineTo(q); }
                c.DrawPath(sk, copy);
            }
            var at = Px(piece.Centre);
            var text = $"{i + 1}";
            float tw = label.MeasureText(text);
            c.DrawRoundRect(at.X - tw / 2 - 4, at.Y - size * 0.8f, tw + 8, size * 1.1f, 4, 4, labelBack);
            c.DrawText(text, at.X - tw / 2, at.Y + size * 0.15f, label);
        }
        using var img = surface.Snapshot();
        using var bmp = SKBitmap.FromImage(img);
        var old = _preview.Source as IDisposable;
        _preview.Source = CameraImaging.ToAvalonia(bmp);
        old?.Dispose();
    }
}
