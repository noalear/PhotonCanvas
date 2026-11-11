using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using SkiaSharp;
using PhotonCanvas.Core;
using PhotonCanvas.Core.Camera;
using PhotonCanvas.Device;
using PhotonCanvas.Doc;
using static PhotonCanvas.App.UiKit;
using Bitmap = Avalonia.Media.Imaging.Bitmap;

namespace PhotonCanvas.App;

/// <summary>
/// Camera calibration: burn a dot grid, photograph it, locate the dots
/// (4 clicks, the rest found automatically), check the fit, save it for the
/// current material distance.
/// </summary>
public sealed class CalibrationWindow : Window
{
    readonly MachinePanel _machine;
    readonly AppSettings _settings;
    readonly ComboBox _markStyle = new() { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = new[] { "Dots", "Crosshairs" } };
    readonly ComboBox _markSize = new() { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = SizeChoices.Select(s => $"{s:0.#} mm").ToList() };
    static readonly double[] SizeChoices = [1, 2, 4];
    /// <summary>The flat bed of the machine in use (the F2 Ultra, or another xTool such as the F2 Ultra UV).</summary>
    readonly MachineProfile _profile;
    /// <summary>"blue diode", or "UV laser" on the F2 Ultra UV (which burns the marks with its one laser).</summary>
    string LaserName => _profile.Uv is not null ? "UV laser" : "blue diode";
    readonly IReadOnlyList<Vec2> _grid;

    readonly PhotoView _photo = new();
    readonly NumericUpDown _power = Number(1, 100, 1, "0.#");
    readonly NumericUpDown _speed = Number(2, 15000, 10, "0.#");
    readonly NumericUpDown _distance = Number(0, 150, 0.5m, "0.0#");
    readonly TextBlock _burnState = Hint();
    readonly TextBlock _focusState = new() { TextWrapping = TextWrapping.Wrap };
    readonly NumericUpDown _typedFocus = Number(0, 150, 0.5m, "0.0#");
    /// <summary>The material distance focused to in this window (null = not yet).</summary>
    double? _focusedAt;
    readonly TextBlock _photoState = Hint();
    readonly TextBlock _instruction = new() { TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.SemiBold };
    readonly TextBlock _result = new() { TextWrapping = TextWrapping.Wrap };
    readonly ToggleSwitch _check = new() { OnContent = "Showing straightened check", OffContent = "Show straightened check", IsEnabled = false };
    readonly Button _save = new() { Classes = { "primary" }, Content = "Save calibration", IsEnabled = false };

    // Photo state
    byte[]? _jpeg;
    Bitmap? _display;          // photo shown while locating
    Bitmap? _checkImage;       // straightened top-down view
    GrayImage? _gray;          // full resolution, for finding dots
    int _imgW, _imgH;

    // Located marks, in full-resolution pixels; index = grid index.
    readonly Vec2?[] _found;
    readonly bool[] _ignored;
    readonly List<Vec2> _cornerClicks = [];
    int? _selected;
    CameraCalibration? _fit;

    /// <summary>Raised when a calibration is saved.</summary>
    public event Action<CameraCalibration>? Saved;

    public CalibrationWindow(MachinePanel machine, AppSettings settings, double? defaultDistance)
    {
        _machine = machine;
        _settings = settings;
        _profile = settings.ActiveSetup() is { Kind: MachineKind.XTool } x ? x.Profile : MachineProfile.F2Ultra;
        _markStyle.SelectedIndex = settings.CalibMarkStyle == MarkStyle.Cross ? 1 : 0;
        int sizeIdx = Array.FindIndex(SizeChoices, s => Math.Abs(s - settings.CalibMarkSizeMm) < 1e-9);
        _markSize.SelectedIndex = sizeIdx >= 0 ? sizeIdx : 1;
        _markStyle.SelectionChanged += (_, _) => SaveMarkChoice();
        _markSize.SelectionChanged += (_, _) => SaveMarkChoice();
        _grid = CalibrationMarks.Grid(_profile);
        _found = new Vec2?[_grid.Count];
        _ignored = new bool[_grid.Count];
        Title = "Camera calibration";
        Width = 1400;
        Height = 900;
        MinWidth = 1000;
        MinHeight = 650;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg0");

        _power.Value = 30;
        _speed.Value = 150;
        _distance.Value = (decimal)Math.Round(defaultDistance ?? machine.Distance ?? 0, 1);

        var autoFocus = new Button { Content = "Auto-focus on the material", Classes = { "primary" }, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
        autoFocus.Click += async (_, _) =>
        {
            if (!_machine.IsConnected) { _focusState.Text = "Connect to the machine first."; return; }
            autoFocus.IsEnabled = false;
            _focusState.Text = "Focusing… the head moves.";
            _focusState.Foreground = Brush("TextMain");
            try { Focused(await _machine.AutoFocusNowAsync(), "auto-focus"); }
            finally { autoFocus.IsEnabled = true; }
        };
        _typedFocus.Value = (decimal)Math.Round(machine.Distance ?? 3, 1);
        var moveFocus = Button("Move the head to it", tip: "When auto-focus can't see the material (glass, very dark or shiny): measure its thickness and type it.");
        moveFocus.Click += async (_, _) =>
        {
            if (!_machine.IsConnected) { _focusState.Text = "Connect to the machine first."; return; }
            double mm = (double)(_typedFocus.Value ?? 0);
            var error = await _machine.MoveFocusAsync(mm);
            Focused(error is null ? mm : null, error ?? "typed height");
        };
        var send = Button("Send marks to machine", "IcSend");
        send.Click += async (_, _) => await SendMarks();
        var take = Button("Take photo", "IcCamera", "Overview camera photo. Lights must be on (they are, unless a job is running).");
        take.Click += async (_, _) => await TakePhoto();
        var open = Button("Open photo file…", tip: "Use a photo you saved earlier from the overview camera.");
        open.Click += async (_, _) => await OpenPhoto();
        var restart = Button("Start over");
        restart.Click += (_, _) => ResetMarks();
        _save.Click += (_, _) => SaveFit();
        _check.IsCheckedChanged += (_, _) => UpdateView();
        _photo.Clicked += OnPhotoClick;
        _photo.RightClicked += OnPhotoRightClick;
        _distance.ValueChanged += (_, _) => Refit();

        var side = new StackPanel
        {
            Children =
            {
                Card("1 · Focus on the material",
                    Hint($"Lay a flat scrap sheet (wood or card) over the whole {_profile.FieldW:0} × {_profile.FieldH:0} mm area first, then focus on it. Out of focus, the marks come out faint or wide and the camera is calibrated for the wrong height."),
                    autoFocus,
                    Field("Or its height", _typedFocus, tip: "Material thickness (mm), when auto-focus can't see it."),
                    moveFocus,
                    _focusState),
                Card("2 · Burn marks",
                    Hint("PhotonCanvas burns 25 marks in a grid over the whole area, plus one small dot beside the top-left mark and two beside the top-right mark."),
                    Field("Marks", _markStyle, tip: "Dots are easiest to spot; crosshairs make the centre easiest to judge by eye. Either way PhotonCanvas finds the exact centre itself."),
                    Field("Mark size", _markSize, Hint("Smaller marks are easier to judge but fainter. Use the same choice when you take the photo."), "Dot diameter, or crosshair width."),
                    Field("Power", _power, tip: "Laser power for the dots (the blue diode, or the UV laser on the F2 Ultra UV)."),
                    Field("Speed", _speed, tip: "Slower = darker marks."),
                    send, _burnState),
                Card("3 · Photo", Row(take, open), _photoState),
                Card("4 · Locate the marks", _instruction, restart,
                    Hint("You don't need to click the exact centre: PhotonCanvas snaps to the middle of the nearest mark. Scroll to zoom, right-drag to pan. To fix a mark: click its marker, then click the mark. Right-click a marker to ignore that mark.")),
                Card("5 · Check and save",
                    Field("Distance", _distance, Hint("Material distance (mm) the marks were burned at. Saved calibrations are matched to the machine's distance."), "Focus distance when the marks were burned."),
                    _result, _check, _save),
            },
        };
        var grid = new Grid { ColumnDefinitions = new("*,360") };
        var photoBorder = new Border { Child = _photo, Margin = new(12) };
        grid.Children.Add(photoBorder);
        var scroll = new ScrollViewer { Content = side, Padding = new(0, 12, 12, 12) };
        Grid.SetColumn(scroll, 1);
        grid.Children.Add(scroll);
        Content = grid;
        Closed += (_, _) => { _display?.Dispose(); _checkImage?.Dispose(); };
        ResetMarks();
        UpdateConnectionHints();
        _focusState.Text = machine.IsConnected ? "Not focused in this window yet." : "Connect to the machine to focus.";
        _focusState.Foreground = Brush("Warn");
    }

    /// <summary>After focusing: where it is, or why not; a focus at the very top of the head's travel means it didn't find the material.</summary>
    void Focused(double? distance, string how)
    {
        if (distance is not { } d)
        {
            _focusedAt = null;
            _focusState.Text = $"⚠ Not focused ({how}). Try again, or type the material's height and move the head to it.";
            _focusState.Foreground = Brush("Warn");
            return;
        }
        if (d >= 149)
        {
            _focusedAt = null;
            _focusState.Text = $"⚠ The head ended at the top of its travel ({d:0.0} mm): it didn't find the material. Check the material is under the head and try again, or type its height.";
            _focusState.Foreground = Brush("Warn");
            return;
        }
        _focusedAt = d;
        _distance.Value = (decimal)Math.Round(d, 1);
        _focusState.Text = $"✓ Focused at {d:0.0} mm ({how}).";
        _focusState.Foreground = Brush("Ok");
    }

    void UpdateConnectionHints()
    {
        _burnState.Text = _machine.IsConnected ? "" : "Connect to the machine (Machine, at the right end of the ribbon) to send the marks. If they are already burned, skip to step 3.";
        _photoState.Text = _machine.IsConnected ? "" : "Not connected: you can open a saved overview photo instead.";
    }

    // ---- 1: burn -----------------------------------------------------------------

    async Task SendMarks()
    {
        if (!_machine.IsConnected)
        {
            await Dialogs.Message(this, "Not connected", "Connect to the machine first (Machine, at the right end of the ribbon).");
            return;
        }
        var laser = LaserSettings.Blue((double)(_power.Value ?? 30), (double)(_speed.Value ?? 150));
        byte[] xf;
        try
        {
            laser.Validate(_profile);
            var job = CalibrationMarks.Build(_profile, laser, Spec);
            var motion = job.Render();
            var corners = new Vec2[] { new(5, 5), new(215, 5), new(215, 215), new(5, 215), new(5, 5) };
            var preview = _grid.Select(p => (IReadOnlyList<Vec2>)Geom.Arc(p.X, p.Y, 2, 2, 0, 360, 0.5)).ToList();
            xf = XfPackage.Build(new() { Motion = motion, Border = job.Border(corners), PreviewJpeg = XfPackage.PreviewJpeg(preview), Light = _profile.OnThisMachine(laser).Light.Wire(), EstimatedSeconds = job.EstimateSeconds() });
        }
        catch (JobException e)
        {
            await Dialogs.Message(this, "Can't make the marks", e.Message);
            return;
        }
        if (_focusedAt is null &&
            !await Dialogs.Confirm(this, "Not focused yet",
                "You haven't focused on the material in this window (step 1). Burned out of focus, the marks come out faint or wide and a sheet is wasted." +
                (_machine.Distance is { } fd ? $"\n\nThe machine's focus is at {fd:0.0} mm" + (fd >= 149 ? ", the top of its travel: almost certainly wrong." : ".") : ""),
                "Burn anyway", "Go back and focus"))
            return;
        var dist = _machine.Distance is { } d ? $"Machine focus distance: {d:0.0} mm.\n" : "";
        if (!await Dialogs.Confirm(this, "Burn calibration marks",
                $"25 {(Spec.Style == MarkStyle.Cross ? "crosshairs" : "dots")} ({Spec.SizeMm:0.#} mm) plus 3 orientation dots across the whole {_profile.FieldW:0} × {_profile.FieldH:0} mm area, {LaserName} {laser.Power:0.#}% at {laser.Speed:0.#} mm/s.\n{dist}\n" +
                "Make sure scrap material covers the whole area. After upload the machine waits; nothing fires until you press its start button." +
                (_machine.Mode == "P_WORK" ? "\n\nA job that's loaded on the machine but not started will be replaced." : ""),
                "Upload", "Cancel")) return;
        try
        {
            _burnState.Text = "Uploading…";
            await _machine.UploadJobAsync(xf, null, CancellationToken.None, replaceLoadedJob: true, name: "Camera calibration marks");
            _burnState.Text = "Uploaded. Press the start button on the machine. When it finishes, take the photo (step 3).";
            if (_machine.Distance is { } md) _distance.Value = (decimal)Math.Round(md, 1);
        }
        catch (DeviceException e)
        {
            _burnState.Text = "Upload failed.";
            await Dialogs.Message(this, "Upload failed", e.Message);
        }
    }

    // ---- 2: photo ------------------------------------------------------------------

    async Task TakePhoto()
    {
        if (!_machine.IsConnected)
        {
            await Dialogs.Message(this, "Not connected", "Connect to the machine (Machine, at the right end of the ribbon), or open a saved photo.");
            return;
        }
        try
        {
            _photoState.Text = "Taking photo…";
            var jpg = await _machine.SnapshotAsync(0);
            if (_machine.Distance is { } md) _distance.Value = (decimal)Math.Round(md, 1);
            await LoadPhoto(jpg);
        }
        catch (DeviceException e)
        {
            _photoState.Text = "Photo failed.";
            await Dialogs.Message(this, "Photo failed", e.Message);
        }
    }

    async Task OpenPhoto()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open overview camera photo",
            FileTypeFilter = [new FilePickerFileType("JPEG photo") { Patterns = ["*.jpg", "*.jpeg"] }],
        });
        if (files.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;
        try { await LoadPhoto(await File.ReadAllBytesAsync(path)); }
        catch (Exception e) when (e is IOException or FormatException)
        {
            await Dialogs.Message(this, "Couldn't open photo", e.Message);
        }
    }

    async Task LoadPhoto(byte[] jpg)
    {
        _photoState.Text = "Reading photo…";
        var (display, gray, w, h) = await Task.Run(() =>
        {
            using var full = CameraWarp.Decode(jpg);
            var g = CameraWarp.ToGray(full);
            using var small = full.Width > 2400 ? full.Resize(new SKImageInfo(2400, full.Height * 2400 / full.Width, SKColorType.Bgra8888, SKAlphaType.Premul), SKFilterQuality.Medium)! : full.Copy();
            return (CameraImaging.ToAvalonia(small), g, full.Width, full.Height);
        });
        _jpeg = jpg;
        var oldDisplay = _display;
        _display = display;
        _gray = gray;
        _imgW = w;
        _imgH = h;
        _photoState.Text = $"Photo {w} × {h}.";
        var oldCheck = _checkImage;
        _checkImage = null; // belongs to the previous photo
        ResetMarks();
        oldDisplay?.Dispose();
        oldCheck?.Dispose();
    }

    // ---- 3: locate ---------------------------------------------------------------------

    static readonly string[] CornerNames = ["Top Left", "Top Right", "Bottom Right", "Bottom Left"];
    static readonly string[] CornerPrompts = [.. CornerNames.Select(n => $"{n}: click the centre of the {n} mark.")];

    /// <summary>
    /// Where corner <paramref name="k"/> should be in the photo (normalised), and how much of the
    /// photo to show around it: from the corners already clicked when there are enough, else from
    /// the camera's saved calibration, else that corner's quarter of the photo.
    /// </summary>
    (Vec2 At, double Width) ExpectedCorner(int k)
    {
        var clicked = _cornerClicks.Select(Norm).ToList();
        double ratio = _imgW > 0 ? (double)_imgH / _imgW : 1;
        // Two corners clicked: the square's other corners follow (a quarter turn of the top edge).
        if (clicked.Count >= 2)
        {
            Vec2 tl = clicked[0], tr = clicked[1];
            // Work in units of the photo's width, so a square stays square.
            var edge = new Vec2(tr.X - tl.X, (tr.Y - tl.Y) * ratio);
            var down = new Vec2(-edge.Y, edge.X);
            var br = clicked.Count >= 3 ? clicked[2] : new Vec2(tr.X + down.X, tr.Y + down.Y / ratio);
            var bl = new Vec2(tl.X + (br.X - tr.X), tl.Y + (br.Y - tr.Y));
            double width = Math.Max(0.15, edge.Length * 0.45);
            return (k == 2 ? br : bl, width);
        }
        if (_machine.CameraMapFor((double)(_distance.Value ?? 0)) is { } choice)
        {
            var at = choice.Map.ToImage(_grid[CalibrationMarks.Corners[k]]);
            var nearby = choice.Map.ToImage(_grid[CalibrationMarks.Corners[k]] + new Vec2(40, 0));
            // A known first click moves the guess for the second by the same amount.
            if (clicked.Count == 1)
            {
                var was = choice.Map.ToImage(_grid[CalibrationMarks.Corners[0]]);
                at = new Vec2(at.X + clicked[0].X - was.X, at.Y + clicked[0].Y - was.Y);
            }
            // About 70 mm of the bed around it, so the neighbouring marks show too if the guess is off.
            if (at.X is > -0.1 and < 1.1 && at.Y is > -0.1 and < 1.1) return (at, Math.Clamp(Math.Abs(nearby.X - at.X) * 1.8, 0.2, 0.6));
        }
        // Nothing to go on: that corner's quarter of the photo.
        return (new Vec2(k is 0 or 3 ? 0.25 : 0.75, k is 0 or 1 ? 0.25 : 0.75), 0.5);
    }

    /// <summary>Show where the next corner is expected, zoomed in on it; the whole photo once all four are in.</summary>
    void PromptCorner()
    {
        if (_gray is null) { _photo.Expect = null; return; }
        int k = _cornerClicks.Count;
        if (k >= 4)
        {
            _photo.Expect = null;
            _photo.Fit();
            return;
        }
        var (at, width) = ExpectedCorner(k);
        _photo.Expect = (at, CornerNames[k]);
        _photo.ZoomTo(at, width);
    }

    void ResetMarks()
    {
        Array.Clear(_found);
        Array.Clear(_ignored);
        _cornerClicks.Clear();
        _selected = null;
        _fit = null;
        _check.IsChecked = false;
        _check.IsEnabled = false;
        _save.IsEnabled = false;
        _result.Text = "";
        _instruction.Text = _gray is null ? "Take or open a photo first." : CornerPrompts[0];
        UpdateView();
        // After the photo view has its size.
        Avalonia.Threading.Dispatcher.UIThread.Post(PromptCorner, Avalonia.Threading.DispatcherPriority.Background);
    }

    Vec2 Px(Vec2 norm) => new(norm.X * _imgW, norm.Y * _imgH);
    Vec2 Norm(Vec2 px) => new(px.X / _imgW, px.Y / _imgH);

    /// <summary>Rough pixels per mm, from the corner clicks.</summary>
    MarkSpec Spec => new(_markStyle.SelectedIndex == 1 ? MarkStyle.Cross : MarkStyle.Dot, SizeChoices[Math.Max(0, _markSize.SelectedIndex)]);

    void SaveMarkChoice()
    {
        if (_markStyle.SelectedIndex < 0 || _markSize.SelectedIndex < 0) return;
        _settings.CalibMarkStyle = Spec.Style;
        _settings.CalibMarkSizeMm = Spec.SizeMm;
        _settings.Save();
    }

    double PxPerMm()
    {
        var tl = _found[CalibrationMarks.Corners[0]];
        var tr = _found[CalibrationMarks.Corners[1]];
        if (tl is { } a && tr is { } b) return a.Dist(b) / _grid[CalibrationMarks.Corners[0]].Dist(_grid[CalibrationMarks.Corners[1]]);
        return 6;
    }

    void OnPhotoClick(Vec2 norm)
    {
        if (_gray is null || _check.IsChecked == true) return;
        var px = Px(norm);
        if (_cornerClicks.Count < 4)
        {
            _cornerClicks.Add(px);
            int idx = CalibrationMarks.Corners[_cornerClicks.Count - 1];
            _found[idx] = MarkFinder.Find(_gray, px, (Spec.SearchRadiusMm + 1.5) * PxPerMm()) ?? px;
            if (_cornerClicks.Count < 4) _instruction.Text = CornerPrompts[_cornerClicks.Count];
            else LocateAll();
            UpdateView();
            PromptCorner();
            return;
        }
        // Select a marker, or move the selected one here.
        int? near = NearestMarker(norm);
        if (_selected is null || near is not null && near != _selected)
        {
            _selected = near;
            _instruction.Text = near is null ? _instruction.Text : $"Dot {near + 1} selected. Click its centre in the photo to move it.";
        }
        else if (_selected is { } sel)
        {
            _found[sel] = MarkFinder.Find(_gray, px, Math.Max(2, Spec.SearchRadiusMm * 0.7) * PxPerMm()) ?? px;
            _ignored[sel] = false;
            _selected = null;
            Refit();
        }
        UpdateView();
    }

    void OnPhotoRightClick(Vec2 norm)
    {
        if (_cornerClicks.Count < 4 || _check.IsChecked == true) return;
        if (NearestMarker(norm) is not { } i) return;
        _ignored[i] = !_ignored[i];
        _selected = null;
        Refit();
        UpdateView();
    }

    /// <summary>Where dot <paramref name="i"/> is shown: where it was found, else where the fit predicts it.</summary>
    Vec2? MarkerPos(int i) =>
        _found[i] is { } f ? Norm(f) : _fit is not null && _cornerClicks.Count >= 4 ? _fit.ToImage(_grid[i]) : null;

    int? NearestMarker(Vec2 norm)
    {
        int? best = null;
        double bestD = 16;
        for (int i = 0; i < _found.Length; i++)
        {
            if (MarkerPos(i) is not { } at) continue;
            double d = _photo.ScreenDistance(at, norm);
            if (d < bestD) { bestD = d; best = i; }
        }
        return best;
    }

    /// <summary>Predict every dot from what's known so far and look for it there.</summary>
    void LocateAll()
    {
        if (_gray is null) return;
        double r = Spec.SearchRadiusMm * PxPerMm();
        for (int pass = 0; pass < 2; pass++)
        {
            var known = Enumerable.Range(0, _grid.Count).Where(i => _found[i] is not null && !_ignored[i])
                .Select(i => new CalibrationPoint(_grid[i], Norm(_found[i]!.Value))).ToList();
            CameraCalibration guessMap;
            try { guessMap = CameraCalibration.Fit(known, 0); }
            catch (CalibrationException e)
            {
                _instruction.Text = $"{e.Message} Start over and click the four corner dots.";
                return;
            }
            for (int i = 0; i < _grid.Count; i++)
            {
                if (CalibrationMarks.Corners.Contains(i) && pass == 0) continue;
                var guess = Px(guessMap.ToImage(_grid[i]));
                if (guess.X < 0 || guess.Y < 0 || guess.X >= _imgW || guess.Y >= _imgH) continue;
                if (MarkFinder.Find(_gray, guess, r) is { } f) _found[i] = f;
            }
        }
        int n = _found.Count(f => f is not null);
        _instruction.Text = n == _grid.Count
            ? "All 25 marks found. Check that every marker sits on its mark, then look at the result below."
            : $"Found {n} of {_grid.Count} marks. Missing ones are shown in orange at their predicted spot: click one, then click its mark.";
        // Show predictions for missing dots so they can be placed by hand.
        Refit();
    }

    void Refit()
    {
        if (_imgW == 0) return;
        var pts = Enumerable.Range(0, _grid.Count).Where(i => _found[i] is not null && !_ignored[i])
            .Select(i => new CalibrationPoint(_grid[i], Norm(_found[i]!.Value))).ToList();
        try
        {
            _fit = CameraCalibration.Fit(pts, (double)(_distance.Value ?? 0));
        }
        catch (CalibrationException e)
        {
            _fit = null;
            _result.Text = e.Message;
            _save.IsEnabled = _check.IsEnabled = false;
            UpdateView();
            return;
        }
        string quality = _fit.MaxMm <= 0.3 ? "Excellent." : _fit.MaxMm <= 0.6 ? "Good." : _fit.MaxMm <= 1.2 ? "Usable, but check the worst marks." : "Poor: a marker is probably on the wrong mark.";
        _result.Text = $"{pts.Count} marks used. Average error {_fit.RmsMm:0.00} mm, worst {_fit.MaxMm:0.00} mm. {quality}" +
                       (pts.Count < 16 ? "\nUse at least 16 marks for full lens correction." : "");
        _save.IsEnabled = pts.Count >= 4;
        _check.IsEnabled = _jpeg is not null;
        UpdateCheckImage();
        UpdateView();
    }

    void UpdateCheckImage()
    {
        if (_fit is null || _jpeg is null) return;
        var fit = _fit;
        var jpeg = _jpeg;
        _ = Task.Run(() =>
        {
            using var src = CameraWarp.Decode(jpeg, 2400);
            using var warped = CameraWarp.Warp(src, fit, new Rect2(0, 0, _profile.FieldW, _profile.FieldH), 0.2);
            return CameraImaging.ToAvalonia(warped);
        }).ContinueWith(t =>
        {
            if (t.Exception is { } e) AppLog.Error("calibration check image", e);
            if (!t.IsCompletedSuccessfully) return;
            if (!ReferenceEquals(fit, _fit))
            {
                t.Result.Dispose(); // a newer fit superseded this one
                return;
            }
            var old = _checkImage;
            _checkImage = t.Result;
            UpdateView();
            old?.Dispose();
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    void UpdateView()
    {
        if (_check.IsChecked == true && _checkImage is not null)
        {
            // Straightened view: every dot should sit inside its target ring.
            _photo.Image = _checkImage;
            _photo.Markers = _grid.Select((g, i) => new PhotoMarker(new(g.X / _profile.FieldW, g.Y / _profile.FieldH), "", Color.Parse("#2FC27A"), Hollow: true)).ToList();
            return;
        }
        _photo.Image = _display;
        var markers = new List<PhotoMarker>();
        var green = Color.Parse("#2FC27A");
        var orange = Color.Parse("#F5A524");
        var grey = Color.Parse("#8F98A6");
        for (int i = 0; i < _grid.Count; i++)
        {
            if (MarkerPos(i) is not { } a) continue;
            var color = _ignored[i] ? grey : _found[i] is null ? orange : green;
            string label = $"{i + 1}";
            if (_fit is not null && _found[i] is not null && !_ignored[i])
            {
                int k = Enumerable.Range(0, i).Count(j => _found[j] is not null && !_ignored[j]);
                if (k < _fit.ErrorsMm.Count && _fit.ErrorsMm[k] > 0.6) { color = orange; label += $" · {_fit.ErrorsMm[k]:0.0} mm"; }
            }
            markers.Add(new PhotoMarker(a, label, color, _selected == i, _ignored[i]));
        }
        _photo.Markers = markers;
    }

    // ---- 4: save -----------------------------------------------------------------------

    void SaveFit()
    {
        if (_fit is null) return;
        Saved?.Invoke(_fit);
        _result.Text += $"\nSaved for {_fit.DistanceMm:0.#} mm.";
    }
}
