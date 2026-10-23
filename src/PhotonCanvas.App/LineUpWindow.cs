using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using PhotonCanvas.Core;
using PhotonCanvas.Core.Camera;
using PhotonCanvas.Device;
using PhotonCanvas.Doc;
using static PhotonCanvas.App.UiKit;

namespace PhotonCanvas.App;

/// <summary>
/// Lines up a page of a design split across pieces (Split onto pieces) with the pieces on the bed.
/// While following, the camera looks again every second or two, finds this page's piece and the
/// edges it can see of the pieces laid beside it, and moves the whole page onto them: no clicking.
/// Once it's steady it holds still unless a piece is bumped. Nothing is ever sent from here, and it
/// waits while the machine is busy (a job running, framing).
/// </summary>
public sealed class LineUpWindow : Window
{
    const double MmPerPx = 0.2;
    /// <summary>Fits with less of the outline on an edge than this aren't used.</summary>
    const double GoodEnough = 0.45;
    /// <summary>Moves smaller than this (mm, at the piece's corners) are left alone.</summary>
    const double StillMm = 0.1;
    /// <summary>Once steady, only a move bigger than this (a piece bumped) is followed.</summary>
    const double BumpMm = 0.4;

    readonly Document _doc;
    readonly MachinePanel _machine;
    readonly MachineProfile _profile;

    readonly TextBlock _title = new() { Classes = { "h" } };
    readonly Button _prev = Button("◀ Previous piece"), _next = Button("Next piece ▶");
    readonly TextBlock _how = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    readonly NumericUpDown _distance = Number(0, 60, 0.1m, "0.0");
    readonly Button _focus = Button("Auto-focus now", tip: "The machine measures the material under its focus sensor and moves to it. Put a piece under the sensor first, or type the distance.");
    readonly TextBlock _heightState = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    readonly ToggleSwitch _follow = new() { OnContent = "Following the pieces", OffContent = "Not following", IsChecked = false };
    readonly Button _once = Button("Line up once", tip: "One photo and one fit, then stop.");
    readonly TextBlock _status = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap, FontWeight = Avalonia.Media.FontWeight.SemiBold };
    readonly TextBlock _detail = Hint();

    CancellationTokenSource? _cts;
    bool _fitting, _settled;
    int _steady;
    string _followingPage = "";

    public LineUpWindow(Document doc, MachinePanel machine, MachineProfile profile)
    {
        _doc = doc;
        _machine = machine;
        _profile = profile;
        Title = "Line up the pieces";
        Width = 470;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");

        _distance.Value = machine.Distance is { } d ? (decimal)Math.Round(d, 1) : null;
        _distance.ValueChanged += (_, _) => CheckHeight();
        _focus.Click += async (_, _) => await AutoFocus();
        _prev.Click += (_, _) => GoToPiece(-1);
        _next.Click += (_, _) => GoToPiece(+1);
        _follow.IsCheckedChanged += (_, _) => { if (_follow.IsChecked == true) StartFollowing(); else StopFollowing("Not following: the design stays where it is."); };
        _once.Click += async (_, _) => await FitOnce(CancellationToken.None, manual: true);
        ToolTip.SetTip(_follow, "Look again every second or two and keep the design on the pieces. It pauses while a job runs.");
        var close = new Button { Content = "Close", IsCancel = true, MinWidth = 100 };
        close.Click += (_, _) => Close();
        Closing += (_, _) => _cts?.Cancel();
        _doc.Changed += OnDocChanged;
        Closed += (_, _) => _doc.Changed -= OnDocChanged;

        Content = new Border
        {
            Padding = new Thickness(18),
            Child = new StackPanel
            {
                Spacing = 9,
                Children =
                {
                    _title,
                    _how,
                    Row(_prev, _next),
                    new TextBlock { Text = "HEIGHT", Classes = { "h" }, Margin = new(0, 4, 0, 0) },
                    Hint("The camera picture only lines up at the right height: the distance to the top of the pieces."),
                    Field("Distance (mm)", _distance, tip: "Distance from the laser to the top of the pieces, as the machine's focus uses it."),
                    _focus,
                    _heightState,
                    new TextBlock { Text = "LINE UP", Classes = { "h" }, Margin = new(0, 4, 0, 0) },
                    Row(_follow, _once),
                    _status,
                    _detail,
                    new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { close } },
                },
            },
        };
        ShowPage();
        if (CheckHeight() && Piece() is not null) _follow.IsChecked = true;
    }

    /// <summary>This page's piece (its material outline) and the other pieces' outlines, or null when it isn't a piece's page.</summary>
    (PathShape Piece, List<PathShape> Others)? Piece()
    {
        var s = _doc.State;
        if (MaterialOutline.On(s) is not [var piece, ..]) return null;
        var othersLayer = s.Layers.FirstOrDefault(l => l.Name == PieceSplit.OthersLayer);
        var others = othersLayer is null ? [] : s.Shapes.OfType<PathShape>()
            .Where(x => x.LayerId == othersLayer.Id && x.Name.StartsWith("Piece ") && x.Geometry.Subpaths.Any(sp => sp.Closed)).ToList();
        return (piece, others);
    }

    static bool IsPiecePage(IEnumerable<Layer> layers) => layers.Any(l => l.Name == PieceSplit.OthersLayer && !l.Output);

    void ShowPage()
    {
        var s = _doc.State;
        var names = Pages.Names(s);
        var pages = PiecePages();
        int at = pages.IndexOf(s.PageIndex);
        _prev.IsEnabled = at > 0;
        _next.IsEnabled = at >= 0 && at < pages.Count - 1;
        if (Piece() is not { } p)
        {
            _title.Text = "NO PIECE ON THIS PAGE";
            _how.Text = "This page has no piece to line up. Lay out the pieces as material outlines, put the design over them, select it all and use Laser → Split onto pieces; then open a piece's page.";
            return;
        }
        _title.Text = names[s.PageIndex].ToUpperInvariant();
        _how.Text = p.Others.Count == 0 || at <= 0
            ? $"Lay {p.Piece.Name.ToLowerInvariant()} on the bed under its outline. The camera finds it and moves the design onto it; when it says steady, Ship It."
            : $"Lay {p.Piece.Name.ToLowerInvariant()} on the bed under its outline, with the pieces already done beside it as they'll go together (they show grey). " +
              "Whatever the camera can see of their edges lines the design up too. When it says steady, take away any done piece that's in the way, and Ship It.";
    }

    /// <summary>The page numbers of the pieces' pages, in order.</summary>
    List<int> PiecePages()
    {
        var s = _doc.State;
        var list = new List<int>();
        int n = s.OtherPages.Count + 1;
        for (int i = 0, other = 0; i < n; i++)
        {
            var layers = i == s.PageIndex ? s.Layers : s.OtherPages[other++].Layers;
            if (IsPiecePage(layers)) list.Add(i);
        }
        return list;
    }

    void GoToPiece(int by)
    {
        var pages = PiecePages();
        int at = pages.IndexOf(_doc.State.PageIndex);
        int to = at < 0 ? (pages.Count > 0 ? 0 : -1) : Math.Clamp(at + by, 0, pages.Count - 1);
        if (to < 0 || pages[to] == _doc.State.PageIndex) return;
        _doc.Commit("Show page", Pages.SwitchTo(_doc.State, pages[to]), []);
    }

    void OnDocChanged(bool _)
    {
        if (_doc.State.PageName != _followingPage)
        {
            // Another page: start again, from where its design is now.
            _settled = false;
            _steady = 0;
            _followingPage = _doc.State.PageName;
            ShowPage();
        }
    }

    double? DistanceValue => _distance.Value is { } v ? (double)v : null;

    bool CheckHeight()
    {
        bool ok = false;
        if (!_machine.IsConnected) _heightState.Text = "⚠ Connect to the machine first.";
        else if (DistanceValue is not { } d) _heightState.Text = "⚠ Enter the distance, or click Auto-focus now.";
        else if (_machine.CameraMapFor(d) is not { } choice) _heightState.Text = "⚠ The camera isn't calibrated yet (Camera tab → Calibrate).";
        else
        {
            ok = choice.Exact;
            _heightState.Text = (ok ? "✓ Camera " : "⚠ Camera ") + choice.Description + "." + (ok ? "" : " Lining up is off until the camera is calibrated near this height.");
        }
        _heightState.Foreground = ok ? Brush("Ok") : Brush("Warn");
        _focus.IsEnabled = _machine.IsConnected;
        _once.IsEnabled = ok && !_fitting;
        _follow.IsEnabled = ok || _follow.IsChecked == true;
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

    void StartFollowing()
    {
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        _settled = false;
        _steady = 0;
        _followingPage = _doc.State.PageName;
        // The camera picture under the design, refreshed with every look.
        if (_machine.HasCalibration) _machine.CameraOnWorkArea = true;
        _ = Follow(cts.Token);
    }

    void StopFollowing(string why)
    {
        _cts?.Cancel();
        _cts = null;
        _status.Text = why;
        _status.Foreground = Brush("TextMain");
    }

    async Task Follow(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await FitOnce(ct, manual: false);
            try { await Task.Delay(_settled ? 1500 : 600, ct); }
            catch (OperationCanceledException) { return; }
        }
    }

    /// <summary>One photo and one fit; the design is moved if the fit is good and the move worth making.</summary>
    async Task FitOnce(CancellationToken ct, bool manual)
    {
        if (_fitting || !IsVisible) return;
        if (!CheckHeight() || DistanceValue is not { } dist) { Say("Waiting for the height (above).", warn: true); return; }
        if (!_machine.IsConnected) { Say("Not connected.", warn: true); return; }
        if (_machine.Mode is not ("P_IDLE" or "P_WORK_DONE" or "P_FINISH" or "" or "P_READY") || _machine.IsFraming)
        {
            Say($"Waiting: the machine is busy ({_machine.ModeText}). The design isn't moved while it works.", warn: false);
            return;
        }
        if (Piece() is not { } page) { Say("This page has no piece to line up.", warn: true); return; }

        var field = new Rect2(0, 0, _profile.FieldW, _profile.FieldH);
        var seen = field.Inflate(-2); // the picture's very edge isn't trusted
        // What the camera can see: the piece, and the other pieces' edges inside the work area.
        var own = Outline(page.Piece, seen);
        var near = page.Others.SelectMany(o => Outline(o, seen)).ToList();
        if (own.Count == 0) { Say($"{page.Piece.Name} is outside the camera's view: it must lie in the work area.", warn: true); return; }
        double search = _settled ? 6 : 30;
        var all = own.Concat(near).ToList();
        var box = Rect2.Bounds(all.SelectMany(p => p)).Inflate(search + 5);
        var area = new Rect2(Math.Max(field.MinX, box.MinX), Math.Max(field.MinY, box.MinY), Math.Min(field.MaxX, box.MaxX), Math.Min(field.MaxY, box.MaxY));
        var opt = new FitOptions { MaxRotationDeg = 20, MaxScaleChange = 0, SearchMm = search };
        string pageName = _doc.State.PageName;
        var pieceBefore = page.Piece.Bounds;

        _fitting = true;
        CheckHeight();
        try
        {
            if (manual) Say("Taking a photo…", warn: false);
            using var photo = await _machine.CaptureAreaAsync(area, MmPerPx, dist);
            ct.ThrowIfCancellationRequested();
            var gray = CameraWarp.ToGray(photo);
            var fit = await Task.Run(() => ShapeFit.Fit(gray, area, MmPerPx, all, opt, ct), ct);
            // The pieces beside it may have been taken away: then the piece alone.
            if (fit.OnEdge < GoodEnough && near.Count > 0)
            {
                var alone = await Task.Run(() => ShapeFit.Fit(gray, area, MmPerPx, own, opt, ct), ct);
                if (alone.OnEdge > fit.OnEdge) fit = alone;
            }
            ct.ThrowIfCancellationRequested();
            if (_doc.State.PageName != pageName || Piece() is not { } now || now.Piece.Bounds != pieceBefore) return; // changed meanwhile
            string match = $"{fit.OnEdge:P0} of the edges found" + (double.IsFinite(fit.RmsMm) ? $", about {fit.RmsMm:0.00} mm off" : "");
            if (fit.OnEdge < GoodEnough)
            {
                _steady = 0;
                _settled = false;
                Say($"Can't see {page.Piece.Name.ToLowerInvariant()} well ({match}). Check it's under its outline, uncovered and well lit.", warn: true);
                return;
            }
            var b = Rect2.Bounds(own.SelectMany(p => p));
            double radius = Math.Sqrt(b.Width * b.Width + b.Height * b.Height) / 2;
            double moveMm = Math.Sqrt(fit.Offset.X * fit.Offset.X + fit.Offset.Y * fit.Offset.Y) + Math.Abs(fit.AngleDeg) * Math.PI / 180 * radius;
            if (moveMm < StillMm || _settled && moveMm < BumpMm)
            {
                _steady++;
                _settled = _steady >= 2;
                Say(_settled ? $"✓ Lined up and steady ({match}). Ship It when you're ready." : $"Lined up ({match}). Checking it's steady…", warn: false, ok: _settled);
                _detail.Text = "";
                return;
            }
            var move = Affine.RotateAbout(fit.Center, fit.AngleDeg).Then(Affine.Translate(fit.Offset));
            var ids = _doc.State.Shapes.Select(x => x.Id).ToHashSet();
            try { _doc.Commit("Line up the pieces", Ops.Transform(_doc.State, ids, move), null, $"lineup:{pageName}"); }
            catch (InvalidOperationException) { return; } // something is being dragged: next time
            _steady = 0;
            _settled = false;
            Say($"Moved onto the pieces ({match}).", warn: false);
            _detail.Text = $"{fit.Offset.X:+0.00;-0.00} mm across, {fit.Offset.Y:+0.00;-0.00} mm down, turned {fit.AngleDeg:+0.00;-0.00}°.";
        }
        catch (OperationCanceledException) { }
        catch (DeviceException e) { Say($"Couldn't take the photo: {e.Message}", warn: true); }
        catch (FormatException e) { Say($"Couldn't read the camera picture: {e.Message}", warn: true); }
        catch (InvalidOperationException e) { Say(e.Message, warn: true); }
        finally
        {
            _fitting = false;
            CheckHeight();
        }
    }

    void Say(string text, bool warn, bool ok = false)
    {
        _status.Text = text;
        _status.Foreground = warn ? Brush("Warn") : ok ? Brush("Ok") : Brush("TextMain");
    }

    /// <summary>A shape's closed outlines (world mm) as lines, cut to the part inside <paramref name="view"/>.</summary>
    static List<IReadOnlyList<Vec2>> Outline(PathShape shape, Rect2 view) =>
        ShapeFit.InView(shape.World.Flatten(0.05).Where(f => f.Closed).Select(f => (IReadOnlyList<Vec2>)f.Points), view);
}
