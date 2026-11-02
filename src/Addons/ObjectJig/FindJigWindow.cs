using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using PhotonCanvas.App;
using PhotonCanvas.App.Addons;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using SkiaSharp;
using static PhotonCanvas.App.UiKit;
using PathGeometry = PhotonCanvas.Doc.PathGeometry;

namespace PhotonCanvas.Addon.ObjectJig;

/// <summary>
/// Find the jig: photographs the bed, finds the jig wherever it lies and however it's turned, and moves
/// everything on the objects page onto it. Nothing changes until Apply (one undo step). Only the
/// camera is used: the head never moves and the laser stays off.
/// </summary>
public sealed class FindJigWindow : Window
{
    const double MmPerPx = 0.4;

    readonly IAddonHost _host;
    readonly JigShapes _jig;
    readonly NumericUpDown _distance = Number(0, 200, 0.1m, "0.0");
    readonly TextBlock _cameraState = new() { TextWrapping = TextWrapping.Wrap };
    readonly Slider _tolerance = new() { Minimum = 3, Maximum = 60, Value = 18, HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly Button _find = new() { Content = "Take photo and find the jig", Classes = { "primary" }, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
    readonly Button _apply = new() { Content = "Apply", Classes = { "primary" }, MinWidth = 110, IsEnabled = false };
    readonly Image _preview = new() { Stretch = Stretch.Uniform, MaxHeight = 400 };
    readonly TextBlock _result = new() { TextWrapping = TextWrapping.Wrap };
    readonly ProgressBar _busy = new() { IsIndeterminate = true, IsVisible = false, Height = 4 };
    JigFound? _last;
    CancellationTokenSource? _cts;

    internal FindJigWindow(IAddonHost host, JigShapes jig)
    {
        _host = host;
        _jig = jig;
        Title = "Find the jig";
        Width = 620;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");

        _distance.Value = host.FocusDistanceMm is { } d ? (decimal)Math.Round(d, 1) : null;
        _distance.ValueChanged += (_, _) => CheckCamera();
        _find.Click += async (_, _) => await Find();
        _apply.Click += (_, _) => Apply();
        var close = new Button { Content = "Close", IsCancel = true, MinWidth = 100 };
        close.Click += (_, _) => Close();
        Closing += (_, _) => _cts?.Cancel();
        ToolTip.SetTip(_tolerance, "How different from the bed a colour must be to count as the jig. Raise it if the bed's pattern shows up; lower it if a pale jig is missed.");

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
                        Text = "Lay the jig anywhere on the bed, any way round, with nothing touching it (the objects can be in it). " +
                               "The camera finds it and everything on this page moves onto it.",
                    },
                    Field("Distance (mm)", _distance, tip: "From the laser to the top of the jig (measure the focus on the jig)."),
                    Hint(jig.ThicknessMm > 0 ? $"This jig is {jig.ThicknessMm:0.##} mm tall: the distance to its top is that much less than to the bed." : ""),
                    _cameraState,
                    Field("Tolerance", _tolerance),
                    _find,
                    _busy,
                    new Border { Background = Brush("Bg0"), CornerRadius = new(6), MinHeight = 60, Child = _preview, ClipToBounds = true },
                    _result,
                    new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { close, _apply } },
                },
            },
        };
        CheckCamera();
    }

    bool CheckCamera()
    {
        bool ok = false;
        string text;
        if (!_host.IsConnected) text = "Connect to the machine first.";
        else if (_distance.Value is not { } d) text = "Enter the distance to the top of the jig.";
        else if (!_host.CameraCalibratedFor((double)d)) text = "The camera isn't calibrated near this height yet (Camera tab → Calibrate).";
        else { ok = true; text = "Camera ready."; }
        _cameraState.Text = (ok ? "✓ " : "⚠ ") + text;
        _cameraState.Foreground = Brush(ok ? "Ok" : "Warn");
        _find.IsEnabled = ok;
        return ok;
    }

    async Task Find()
    {
        if (!CheckCamera() || _distance.Value is not { } d) return;
        var area = new Rect2(0, 0, _host.Machine.FieldW, _host.Machine.FieldH);
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        _find.IsEnabled = _apply.IsEnabled = false;
        _busy.IsVisible = true;
        _result.Text = "Taking a photo of the bed…";
        try
        {
            using var photo = await _host.PhotographAreaAsync(area, MmPerPx, (double)d);
            _result.Text = "Looking for the jig…";
            var raster = CameraImaging.ToRaster(photo);
            double tol = _tolerance.Value;
            var found = await Task.Run(() => JigFinder.Find(raster, area, MmPerPx, _jig, tol, cts.Token), cts.Token);
            _last = found;
            ShowPreview(photo, area, found);
            var centre = _jig.Outline.Bounds(0.1).Center;
            var (mm, deg) = found.Change(centre);
            _result.Text = $"Found the jig {mm:0.#} mm from where the page has it, turned {deg:0.#}°" +
                (found.Refined ? $" (its edges matched: {found.OnEdge:P0})." : ".") +
                " Check the green outline sits on the jig, then Apply." +
                (found.Warnings.Count > 0 ? "\n" + string.Join("\n", found.Warnings.Select(w => "⚠ " + w)) : "");
            _apply.IsEnabled = true;
        }
        catch (OperationCanceledException) { }
        catch (InvalidOperationException e) { _result.Text = e.Message; }
        catch (Exception e) when (e is FormatException or IOException || e.GetType().Name == "DeviceException") { _result.Text = $"Couldn't take the photo: {e.Message}"; }
        finally
        {
            _busy.IsVisible = false;
            CheckCamera();
        }
    }

    void Apply()
    {
        if (_last is not { } f) return;
        var doc = _host.Document;
        var ids = doc.State.Shapes.Select(s => s.Id).ToHashSet();
        doc.Commit("Find the jig", Ops.Transform(doc.State, ids, f.Move), doc.Selection);
        _host.Status("The page is lined up with the jig. Measure the focus on an object's top before Ship It.");
        Close();
    }

    /// <summary>The photo with what the camera saw (orange) and the jig as it'll be placed (green).</summary>
    void ShowPreview(SKBitmap photo, Rect2 area, JigFound found)
    {
        using var surface = SKSurface.Create(new SKImageInfo(photo.Width, photo.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        var c = surface.Canvas;
        c.DrawBitmap(photo, 0, 0);
        float stroke = Math.Max(1.5f, photo.Width / 350f);
        using var seen = new SKPaint { Color = new SKColor(255, 181, 71, 220), Style = SKPaintStyle.Stroke, StrokeWidth = stroke, IsAntialias = true };
        using var placed = new SKPaint { Color = new SKColor(60, 230, 120), Style = SKPaintStyle.Stroke, StrokeWidth = stroke, IsAntialias = true };
        SKPoint Px(Vec2 p) => new((float)((p.X - area.MinX) / MmPerPx), (float)((p.Y - area.MinY) / MmPerPx));
        void Draw(PathGeometry g, Affine t, SKPaint paint)
        {
            foreach (var f in g.Flatten(0.2))
            {
                if (f.Points.Count < 2) continue;
                using var sk = new SKPath();
                for (int k = 0; k < f.Points.Count; k++) { var q = Px(t.Apply(f.Points[k])); if (k == 0) sk.MoveTo(q); else sk.LineTo(q); }
                if (f.Closed) sk.Close();
                c.DrawPath(sk, paint);
            }
        }
        Draw(found.Found, Affine.Identity, seen);
        Draw(_jig.Outline, found.Move, placed);
        Draw(_jig.Pockets, found.Move, placed);
        using var img = surface.Snapshot();
        using var bmp = SKBitmap.FromImage(img);
        var old = _preview.Source as IDisposable;
        _preview.Source = CameraImaging.ToAvalonia(bmp);
        old?.Dispose();
    }
}
