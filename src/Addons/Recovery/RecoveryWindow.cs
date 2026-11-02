using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using PhotonCanvas.App;
using PhotonCanvas.App.Addons;
using PhotonCanvas.Core;
using PhotonCanvas.Core.Camera;
using PhotonCanvas.Doc;
using static PhotonCanvas.App.UiKit;

namespace PhotonCanvas.Addon.Recovery;

/// <summary>
/// Finds a knocked piece from a photo of the bed and makes the rest of the job on a new page. While
/// open, the canvas shows where the design's lines land on the piece: grey for what's already
/// marked, green for what's still to do. Nothing is sent or moved on the machine from here.
/// </summary>
public sealed class RecoveryWindow : Window
{
    readonly IAddonHost _host;

    readonly Button _photo = Button("Take a photo", "IcLive", "A new photo from the camera above the bed. Nothing moves.");
    readonly TextBlock _photoState = Hint();
    readonly RadioButton _all = new() { Content = "Everything on this page", GroupName = "moved", IsChecked = true };
    readonly RadioButton _selected = new() { Content = "Only the selected shapes", GroupName = "moved" };
    readonly ComboBox _turn = new() { ItemsSource = new[] { "10°", "30°", "90°", "Any angle" }, SelectedIndex = 1, HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly NumericUpDown _reach = Number(2, 150, 5, "0");
    readonly Button _find = Button("Find the piece", "IcFitPiece", "Look for what's already marked in the photo, and work out how far the piece moved and turned");
    readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.SemiBold };
    readonly TextBlock _detail = Hint();
    readonly StackPanel _shapes = new() { Spacing = 4 };
    readonly Border _shapesBox;
    readonly Button _again = Button("Find again with these", tip: "Fit to the unticked shapes only: the ones you say are already marked");
    readonly Button _make = Button("Make the rest of the job", tip: "A new page after this one: the ticked shapes moved onto the piece, the unticked ones kept as grey reference that isn't sent");

    CameraPhoto? _shot;
    Found? _found;
    CancellationTokenSource? _cts;
    readonly IDisposable _overlay;

    /// <summary>A finished search: the design it was made for, what was looked for, and the answer.</summary>
    sealed record Found(DocState State, HashSet<Guid> Moved, List<MarkedShape> Marked, Recovery Result, Dictionary<Guid, CheckBox> Again);

    public RecoveryWindow(IAddonHost host)
    {
        _host = host;
        Title = "Knocked piece";
        Width = 500;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");

        _reach.Value = 30;
        _photo.Click += async (_, _) => await TakePhoto();
        _find.Click += async (_, _) => await Run(null);
        _again.Click += async (_, _) => await Run(_found is { } f ? f.Again.Where(kv => kv.Value.IsChecked != true).Select(kv => kv.Key).ToHashSet() : null);
        _make.Click += (_, _) => MakeRest();
        _selected.IsCheckedChanged += (_, _) => Forget();
        _turn.SelectionChanged += (_, _) => Forget();
        _reach.ValueChanged += (_, _) => Forget();
        _host.PhotoChanged += OnPhoto;
        _host.DocumentChanged += OnDocument;
        _overlay = _host.AddCanvasOverlay(new Overlay(this));
        Closed += (_, _) =>
        {
            _cts?.Cancel();
            _host.PhotoChanged -= OnPhoto;
            _host.DocumentChanged -= OnDocument;
            _overlay.Dispose();
            _host.InvalidateCanvas();
        };

        var close = new Button { Content = "Close", IsCancel = true, MinWidth = 100 };
        close.Click += (_, _) => Close();
        _shapesBox = new Border
        {
            IsVisible = false,
            Child = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock { Text = "WHAT'S ALREADY MARKED", Classes = { "h" } },
                    Hint("Ticked shapes are marked on the new page; unticked ones are left out. Untick anything already done that was missed, then Find again with these."),
                    new ScrollViewer { MaxHeight = 220, Content = _shapes },
                    _again,
                },
            },
        };

        Content = new Border
        {
            Padding = new Thickness(18),
            Child = new StackPanel
            {
                Spacing = 9,
                Children =
                {
                    new TextBlock { Text = "THE MATERIAL MOVED DURING A JOB", Classes = { "h" } },
                    Hint("Pause or stop the job on the machine, and leave the piece where it is now. The camera finds what's already marked on it, and the rest of the job goes on a new page, moved to line up. This page stays as it was."),
                    new TextBlock { Text = "PHOTO", Classes = { "h" }, Margin = new(0, 4, 0, 0) },
                    _photo,
                    _photoState,
                    new TextBlock { Text = "WHAT MOVED", Classes = { "h" }, Margin = new(0, 4, 0, 0) },
                    _all,
                    _selected,
                    Field("Turned up to", _turn, tip: "How far the piece may have turned, either way. Keep it small unless it really turned a lot: a smaller search is quicker and harder to fool."),
                    Field("Moved up to (mm)", _reach, tip: "How far the piece may have moved."),
                    _find,
                    _status,
                    _detail,
                    _shapesBox,
                    new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { _make, close } },
                },
            },
        };
        _shot = _host.Photo;
        ShowState();
    }

    void OnPhoto()
    {
        _shot = _host.Photo;
        ShowState();
    }

    void OnDocument()
    {
        if (_found is { } f && !ReferenceEquals(f.State.Shapes, _host.Document.State.Shapes)) Forget();
        else ShowState();
    }

    /// <summary>Drop a result that no longer fits the design or the settings.</summary>
    void Forget()
    {
        if (_found is null) { ShowState(); return; }
        _found = null;
        _shapesBox.IsVisible = false;
        Say("The design or the settings changed: find the piece again.", warn: false);
        _detail.Text = "";
        _host.InvalidateCanvas();
        ShowState();
    }

    void ShowState()
    {
        int sel = _host.Document.Selection.Count;
        _selected.Content = sel > 0 ? $"Only the selected shapes ({sel})" : "Only the selected shapes (none selected)";
        _selected.IsEnabled = sel > 0;
        if (sel == 0 && _selected.IsChecked == true) _all.IsChecked = true;
        _photo.IsEnabled = _host.IsConnected && _cts is null;
        _photoState.Text = _shot is null
            ? (_host.IsConnected ? "No photo yet: take one." : "Connect to the machine to take a photo.")
            : (_host.IsConnected ? "Using the latest photo. Take a new one if the piece has moved since." : "Not connected: using the last photo the camera took.");
        _find.IsEnabled = _shot is not null && _cts is null;
        _again.IsEnabled = _found is not null && _cts is null;
        _make.IsEnabled = _found is not null && _cts is null;
    }

    async Task TakePhoto()
    {
        _photo.IsEnabled = false;
        Say("Taking a photo…", warn: false);
        try
        {
            var p = await _host.TakePhotoAsync();
            if (p is null) { Say("Couldn't take a photo: is the machine connected and the camera calibrated?", warn: true); return; }
            _shot = p;
            Say("Photo taken.", warn: false);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            Say($"Couldn't take a photo: {e.Message}", warn: true);
        }
        finally { ShowState(); }
    }

    async Task Run(HashSet<Guid>? knownDone)
    {
        if (_shot is not { } shot) return;
        var state = _host.Document.State;
        var moved = _selected.IsChecked == true && _host.Document.Selection.Count > 0
            ? Ops.WithGroups(state, _host.Document.Selection)
            : state.Shapes.Select(s => s.Id).ToHashSet();
        var marked = KnockRecovery.Marked(state).Where(m => moved.Contains(m.Id)).ToList();
        if (marked.Count == 0)
        {
            Say("Nothing that moved is marked by the job: there's nothing to look for.", warn: true);
            return;
        }
        var opt = new RecoverOptions
        {
            MaxTurnDeg = _turn.SelectedIndex switch { 0 => 10, 2 => 90, 3 => 180, _ => 30 },
            SearchMm = (double)(_reach.Value ?? 30),
        };

        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        Say(knownDone is null ? "Looking for the marks…" : "Fitting to the shapes you left unticked…", warn: false);
        _detail.Text = "";
        ShowState();
        try
        {
            var gray = Gray(shot);
            var result = await Task.Run(() => KnockRecovery.Find(gray, shot.Area, shot.MmPerPx, marked, opt, knownDone, cts.Token), cts.Token);
            if (!ReferenceEquals(state.Shapes, _host.Document.State.Shapes)) { Say("The design changed while looking: find the piece again.", warn: true); return; }
            ShowResult(new Found(state, moved, marked, result, []));
        }
        catch (OperationCanceledException) { }
        catch (ArgumentException e) { Say(e.Message, warn: true); }
        finally
        {
            if (ReferenceEquals(_cts, cts)) _cts = null;
            ShowState();
        }
    }

    void ShowResult(Found f)
    {
        var r = f.Result;
        _shapes.Children.Clear();
        foreach (var s in r.Shapes)
        {
            string what = s.State switch
            {
                MarkState.Done => $"already marked ({s.Seen:P0} of it seen)",
                MarkState.Partly => $"partly marked ({s.Seen:P0} seen): its marked part would be marked twice",
                _ => "not marked yet",
            };
            var box = new CheckBox { IsChecked = s.State != MarkState.Done, Content = $"{s.Label}: {what}" };
            box.IsCheckedChanged += (_, _) => _host.InvalidateCanvas();
            f.Again[s.Id] = box;
            _shapes.Children.Add(box);
        }
        _found = f;
        _shapesBox.IsVisible = true;

        string across = r.Offset.X >= 0 ? "right" : "left", down = r.Offset.Y >= 0 ? "towards the front" : "towards the back";
        string turned = Math.Abs(r.AngleDeg) < 0.05 ? "not turned" : $"turned {Math.Abs(r.AngleDeg):0.0}° {(r.AngleDeg > 0 ? "clockwise" : "anticlockwise")}";
        string found = $"Moved {Math.Abs(r.Offset.X):0.0} mm {across} and {Math.Abs(r.Offset.Y):0.0} mm {down}, {turned}.";
        if (r.OtherTurnDeg is { } other)
            Say($"{found} But the marks look much the same turned to {other:0}°, so the turn can't be told from them. Check the green lines on the canvas; unticking a shape that isn't round and finding again may help.", warn: true);
        else if (!r.Trusted)
            Say($"{found} Only {r.Matched:P0} of the marks were found, so this may be wrong. Check the lines on the canvas sit on the marks.", warn: true);
        else
            Say($"✓ {found}", warn: false, ok: true);
        int done = r.Shapes.Count(s => s.State == MarkState.Done), partly = r.Shapes.Count(s => s.State == MarkState.Partly);
        _detail.Text = $"{r.Matched:P0} of the finished marks found" + (double.IsFinite(r.RmsMm) ? $", about {r.RmsMm:0.00} mm off" : "") +
            $"; the far end of the job should land within about {r.FarErrorMm:0.0} mm. {done} shape{(done == 1 ? "" : "s")} already marked" +
            (partly > 0 ? $", {partly} partly" : "") + ". On the canvas: grey = left out, green = still to do.";
        _host.InvalidateCanvas();
    }

    void MakeRest()
    {
        if (_found is not { } f) return;
        if (!ReferenceEquals(f.State.Shapes, _host.Document.State.Shapes)) { Forget(); return; }
        var leaveOut = f.Again.Where(kv => kv.Value.IsChecked != true).Select(kv => kv.Key).ToHashSet();
        var next = KnockRecovery.RestPage(_host.Document.State, f.Moved, leaveOut, f.Result.Move);
        _host.Document.Commit("Knocked piece: the rest of the job", next, []);
        _host.Status($"The rest of the job is on the page \"{next.PageName}\". Check it over the camera picture, then Ship It.");
        Close();
    }

    void Say(string text, bool warn, bool ok = false)
    {
        _status.Text = text;
        _status.Foreground = warn ? Brush("Warn") : ok ? Brush("Ok") : Brush("TextMain");
    }

    /// <summary>The photo in grey; where the camera can't see is black.</summary>
    static GrayImage Gray(CameraPhoto p)
    {
        var img = p.Image;
        var g = new byte[img.Width * img.Height];
        var px = img.Rgba;
        for (int i = 0; i < g.Length; i++)
        {
            int a = px[i * 4 + 3];
            g[i] = a == 0 ? (byte)0 : (byte)((px[i * 4] * 77 + px[i * 4 + 1] * 150 + px[i * 4 + 2] * 29) >> 8);
        }
        return new GrayImage(img.Width, img.Height, g);
    }

    /// <summary>Where the design's lines land on the piece: grey = left out, green = still to do.</summary>
    sealed class Overlay(RecoveryWindow w) : ICanvasOverlay
    {
        static readonly IBrush Todo = new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E));
        static readonly IBrush Done = new SolidColorBrush(Color.FromArgb(0xC0, 0x9C, 0xA3, 0xAF));

        public CanvasLayer Layer => CanvasLayer.OverShapes;

        public void Draw(DrawingContext ctx, CanvasDrawInfo info)
        {
            if (w._found is not { } f) return;
            double px = 1 / Math.Max(info.PixelsPerMm, 1e-6);
            var todo = new Pen(Todo, 2 * px);
            var done = new Pen(Done, 1.5 * px, new DashStyle([3, 2], 0));
            foreach (var m in f.Marked)
            {
                bool again = !f.Again.TryGetValue(m.Id, out var box) || box.IsChecked == true;
                var geo = new StreamGeometry();
                using (var g = geo.Open())
                    foreach (var line in m.Lines)
                    {
                        if (line.Count < 2) continue;
                        var a = f.Result.Move.Apply(line[0]);
                        g.BeginFigure(new Point(a.X, a.Y), false);
                        for (int i = 1; i < line.Count; i++)
                        {
                            var b = f.Result.Move.Apply(line[i]);
                            g.LineTo(new Point(b.X, b.Y));
                        }
                        g.EndFigure(false);
                    }
                ctx.DrawGeometry(null, again ? todo : done, geo);
            }
        }
    }
}
