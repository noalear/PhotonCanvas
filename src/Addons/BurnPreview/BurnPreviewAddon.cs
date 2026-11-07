using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using PhotonCanvas.App;
using PhotonCanvas.App.Addons;
using PhotonCanvas.Core;
using Bitmap = Avalonia.Media.Imaging.Bitmap;

namespace PhotonCanvas.Addon.BurnPreview;

/// <summary>
/// "See it before you burn it" as an add-on: a window with the finished piece drawn on the camera's
/// picture of the material, and a toggle that draws the same on the canvas in place of the camera
/// picture. Only reads the design, the camera picture and the material library; never sends
/// anything to the machine.
/// </summary>
public sealed class BurnPreviewAddon : IAddon
{
    IAddonHost _host = null!;
    BurnPreviewWindow? _window;
    bool _onCanvas;
    readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(350) };
    CancellationTokenSource? _cts;

    /// <summary>What the canvas shows: the burned picture and the bed area it covers.</summary>
    (Bitmap Image, Rect2 Area, bool AllMeasured)? _canvas;

    public void Start(IAddonHost host)
    {
        _host = host;
        _debounce.Tick += OnTick;
        host.AddRibbonButton(new RibbonButton("View", "Show", "See it burned", "IcPalette", Open)
        {
            Tip = "See the finished piece before you burn it: your design drawn on the camera's picture of the material, " +
                  "each layer as dark and in the colour your own test grids say it comes out",
        });
        host.AddRibbonButton(new RibbonButton("View", "Show", "Burned look on work area", "IcPalette", () => SetCanvas(!_onCanvas))
        {
            Tip = "Show the finished piece right on the work area: your design drawn onto the camera's picture of the material as dark and in the colours your tests say it comes out, while you keep editing (turn Camera on work area on to see the material). Click again for the plain picture",
            Checked = () => _onCanvas,
        });
        host.AddCommand(new AddonCommand("burn-preview.open", "View", "See it burned…", "", Open));
        host.AddCommand(new AddonCommand("burn-preview.canvas", "View", "Burned look on work area", "", () => SetCanvas(!_onCanvas)));
        host.AddCanvasOverlay(new BedOverlay(this));
        host.AddCanvasOverlay(new BadgeOverlay(this));
        host.DocumentChanged += Changed;
        host.PhotoChanged += Changed;
    }

    public void Stop()
    {
        _debounce.Stop();
        _debounce.Tick -= OnTick;
        _cts?.Cancel();
        _window?.Close();
        _window = null;
        var old = _canvas;
        _canvas = null;
        old?.Image.Dispose();
    }

    void Open()
    {
        if (_window is { } open) { open.Activate(); return; }
        var w = _window = new BurnPreviewWindow(_host, () => _onCanvas, SetCanvas);
        w.Closed += (_, _) => { if (ReferenceEquals(_window, w)) _window = null; };
        w.Show(_host.Owner);
    }

    void SetCanvas(bool on)
    {
        _onCanvas = on;
        _host.RefreshCommands();
        _window?.CanvasToggled();
        if (on) _ = Redraw();
        else
        {
            _cts?.Cancel();
            var old = _canvas;
            _canvas = null;
            _host.InvalidateCanvas();
            old?.Image.Dispose();
        }
    }

    void Changed()
    {
        if (!_onCanvas) return;
        _debounce.Stop();
        _debounce.Start();
    }

    void OnTick(object? sender, EventArgs e)
    {
        _debounce.Stop();
        _ = Redraw();
    }

    /// <summary>The whole camera picture burned (or, with none, a stand-in round the design).</summary>
    async Task Redraw()
    {
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        var state = _host.Document.State;
        var photo = _host.Photo;
        var library = _host.Library;
        try
        {
            var made = await Task.Run<(Bitmap Image, Rect2 Area, bool AllMeasured)?>(() =>
            {
                BedPicture bed;
                if (photo is not null) bed = new BedPicture(photo.Image, photo.Area, photo.MmPerPx);
                else if (BurnPreview.DesignBounds(state) is { IsEmpty: false } d)
                    bed = BurnPreview.Bed(state, null, library, new Rect2(d.MinX - 5, d.MinY - 5, d.MaxX + 5, d.MaxY + 5));
                else return null;
                var r = BurnPreview.Render(bed, state, library, cancel: cts.Token);
                return ((Bitmap)Bitmaps.From(r.Picture.Image), r.Picture.Area, r.AllMeasured && photo is not null);
            }, cts.Token);
            if (cts.IsCancellationRequested || !_onCanvas) { made?.Image.Dispose(); return; }
            var old = _canvas;
            _canvas = made;
            _host.InvalidateCanvas();
            old?.Image.Dispose();
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>The burned picture, over the camera's, under the design's outlines.</summary>
    sealed class BedOverlay(BurnPreviewAddon owner) : ICanvasOverlay
    {
        public CanvasLayer Layer => CanvasLayer.UnderShapes;

        public void Draw(DrawingContext ctx, CanvasDrawInfo info)
        {
            if (!owner._onCanvas || owner._canvas is not { } c) return;
            var a = c.Area;
            ctx.DrawImage(c.Image, new Rect(0, 0, c.Image.PixelSize.Width, c.Image.PixelSize.Height), new Rect(a.MinX, a.MinY, a.Width, a.Height));
        }
    }

    /// <summary>A small label in the corner, so a burned look is never taken for the camera's own picture.</summary>
    sealed class BadgeOverlay(BurnPreviewAddon owner) : ICanvasOverlay
    {
        public CanvasLayer Layer => CanvasLayer.Screen;

        public void Draw(DrawingContext ctx, CanvasDrawInfo info)
        {
            if (!owner._onCanvas) return;
            string text = owner._canvas is null ? "Burned look: working…"
                : owner._canvas.Value.AllMeasured ? "Burned look · from your tests"
                : "Burned look · some layers guessed (See it burned says which)";
            var ft = new FormattedText(text, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Inter"), 11.5, Brushes.White);
            var corner = info.ToScreen(new Vec2(info.Visible.MinX, info.Visible.MaxY));
            var box = new Rect(corner.X + 28, corner.Y - ft.Height - 22, ft.Width + 16, ft.Height + 8);
            ctx.FillRectangle(new SolidColorBrush(Color.FromArgb(215, 22, 26, 33)), box, 5);
            ctx.DrawText(ft, new Point(box.X + 8, box.Y + 4));
        }
    }
}
