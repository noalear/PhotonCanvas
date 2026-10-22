using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using static PhotonCanvas.App.UiKit;

namespace PhotonCanvas.App;

/// <summary>
/// The machine, at the right end of the ribbon whichever tab is open: status at a glance
/// (click it to connect, or for machine settings), then the things you reach for at the
/// machine: the camera picture and live view, the lights, auto-focus and the focus height
/// (nudge it or type it). Only the status and Connect show while disconnected.
/// </summary>
public sealed class MachineHud : UserControl
{
    readonly MachinePanel _m;
    readonly AppSettings _settings;
    readonly Border _dot = new() { Width = 9, Height = 9, CornerRadius = new(5), VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock _status = new() { FontSize = 12.5, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
    readonly Button _pill = new() { Classes = { "chip" }, Padding = new(10, 4) };
    readonly StackPanel _body;
    readonly StackPanel _cameraRow;
    string? _note; // shown in place of the status while the head moves, or after a focus error

    readonly Button _photo, _focus;
    readonly ToggleButton _camera = new() { Classes = { "rb" } }, _live = new() { Classes = { "rb" } };
    readonly Button _lights;
    readonly Slider _opacity = new() { Minimum = 0.1, Maximum = 1, Width = 120 };
    readonly Slider _front = new() { Minimum = 0, Maximum = 100, Width = 160 }, _back = new() { Minimum = 0, Maximum = 100, Width = 160 };
    // Focus height: ▼ [21.6 mm] ▲ as one control.
    readonly TextBox _height = new()
    {
        Width = 74, MinHeight = 0, Height = 26, Padding = new(2, 0), FontSize = 12.5,
        BorderThickness = new(0), Background = Avalonia.Media.Brushes.Transparent,
        HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
    };
    double? _heightValue;
    readonly Button _up, _down;
    readonly Button _connect = new() { Classes = { "chip" }, Content = "Connect", Margin = new(6, 0, 0, 0) };
    readonly DispatcherTimer _focusDelay = new() { Interval = TimeSpan.FromMilliseconds(700) };
    bool _updating, _pending;

    /// <summary>Open machine settings.</summary>
    public event Action? SettingsRequested;

    /// <summary>Open the close-up camera on the work area.</summary>
    public event Action? CloseUpRequested;

    /// <summary>Calibrate the camera.</summary>
    public event Action? CalibrateRequested;

    static Control IconLabel(string icon, string text) => new StackPanel
    {
        Orientation = Orientation.Horizontal, Spacing = 5,
        Children =
        {
            new PathIcon { Data = Icon(icon), Width = 14, Height = 14, VerticalAlignment = VerticalAlignment.Center },
            new TextBlock { Text = text, FontSize = 12, VerticalAlignment = VerticalAlignment.Center },
        },
    };

    static T Tip<T>(T c, string tip) where T : Control
    {
        ToolTip.SetTip(c, tip);
        return c;
    }

    public MachineHud(MachinePanel machine, AppSettings settings)
    {
        _m = machine;
        _settings = settings;
        VerticalAlignment = VerticalAlignment.Stretch;

        // ---- status ----
        _pill.Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7, Children = { _dot, _status } };
        _pill.Click += (_, _) => PillMenu().Open(_pill);
        _connect.Click += async (_, _) => await _m.ConnectNowAsync();
        ToolTip.SetTip(_connect, "Connect to the last machine, or the one found on the network.");

        // ---- camera ----
        _photo = Tip(new Button { Classes = { "rb" }, Content = IconLabel("IcCamera", "Photo") }, "Take a new photo of the bed and lay it under your design.");
        _photo.Click += async (_, _) => await _m.UpdatePhotoAsync();
        _camera.Content = IconLabel("IcImage", "Show");
        ToolTip.SetTip(_camera, "Show the camera picture under the design (straightened and to scale). Right-click for its opacity.");
        _camera.Click += (_, _) =>
        {
            if (!_m.HasCalibration) { _camera.IsChecked = false; CalibrateRequested?.Invoke(); return; }
            _m.CameraOnWorkArea = _camera.IsChecked == true;
        };
        _opacity.Value = _m.OverlayOpacity;
        _opacity.ValueChanged += (_, _) => { if (!_updating) _m.OverlayOpacity = _opacity.Value; };
        _camera.ContextFlyout = new Flyout
        {
            Content = new StackPanel { Spacing = 4, Children = { new TextBlock { Text = "CAMERA PICTURE OPACITY", Classes = { "h" } }, _opacity } },
        };
        _live.Content = IconLabel("IcLive", "Live");
        ToolTip.SetTip(_live, "Keep the camera picture updating (live view).");
        _live.Click += (_, _) =>
        {
            if (_live.IsChecked == true && !_m.CameraOnWorkArea && _m.HasCalibration) _m.CameraOnWorkArea = true;
            _m.LiveView = _live.IsChecked == true;
        };

        // ---- lights ----
        _lights = Tip(new Button { Classes = { "rb" }, Content = IconLabel("IcBulb", "Lights") }, "The machine's fill lights for the cameras.");
        _front.ValueChanged += (_, _) => LightsChanged();
        _back.ValueChanged += (_, _) => LightsChanged();
        _lights.Flyout = new Flyout
        {
            Placement = PlacementMode.BottomEdgeAlignedRight,
            Content = new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    new TextBlock { Text = "CAMERA LIGHTS", Classes = { "h" } },
                    new TextBlock { Text = "Front", Classes = { "muted" } }, _front,
                    new TextBlock { Text = "Back", Classes = { "muted" } }, _back,
                    Hint("Saved on the machine."),
                },
            },
        };

        // ---- focus ----
        _focus = Tip(new Button
        {
            Classes = { "chip" }, Height = 28, Padding = new(10, 0), CornerRadius = new(6), VerticalContentAlignment = VerticalAlignment.Center,
            Content = IconLabel("IcFocus", "Auto-focus"),
        }, "The machine measures the material under the head and moves to the right focus height. Place your material first.");
        _focus.Click += async (_, _) => await _m.AutoFocusNowAsync();
        ToolTip.SetTip(_height, "Focus distance: the gap between the laser head and the material. Type a value and press Enter to move the head there; Esc puts it back.");
        _height.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                if (ParseHeight(_height.Text) is { } v) _ = MoveTo(v);
                else ShowHeight(_heightValue);
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                ShowHeight(_m.Distance);
            }
        };
        _height.GotFocus += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(_height.SelectAll);
        _height.LostFocus += (_, _) => ShowHeight(_heightValue);
        Button Arrow(string glyph, string tip, Thickness corner)
        {
            var b = new Button
            {
                Content = new TextBlock { Text = glyph, FontSize = 10 }, Width = 26, Height = 28, Padding = new(0),
                Background = Brush("Bg3"), BorderThickness = new(0), CornerRadius = new(corner.Left, corner.Top, corner.Right, corner.Bottom),
                HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
            };
            return Tip(b, tip);
        }
        _down = Arrow("▼", "Lower the head 1 mm (Shift: 0.1 mm). It moves when you stop clicking.", new(5, 0, 0, 5));
        _up = Arrow("▲", "Raise the head 1 mm (Shift: 0.1 mm). It moves when you stop clicking.", new(0, 5, 5, 0));
        _up.AddHandler(PointerReleasedEvent, (_, e) => Nudge(e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 0.1 : 1), Avalonia.Interactivity.RoutingStrategies.Tunnel);
        _down.AddHandler(PointerReleasedEvent, (_, e) => Nudge(e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -0.1 : -1), Avalonia.Interactivity.RoutingStrategies.Tunnel);
        _focusDelay.Tick += (_, _) =>
        {
            _focusDelay.Stop();
            if (_heightValue is { } v) _ = MoveTo(v);
        };

        var stepper = new Border
        {
            BorderBrush = Brush("Line"), BorderThickness = new(1), CornerRadius = new(6), Background = Brush("Bg1"), ClipToBounds = true,
            Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { _down, _height, _up } },
        };
        var focusRow = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 6,
            Children =
            {
                new TextBlock { Text = "Focus", FontSize = 12, Foreground = Brush("TextMuted"), VerticalAlignment = VerticalAlignment.Center, Margin = new(2, 0, 0, 0) },
                stepper,
                _focus,
            },
        };
        var closeUp = Tip(new Button { Classes = { "rb" }, Content = IconLabel("IcZoomIn", "Close-up") }, "The close-up camera in the laser head, on the work area.");
        closeUp.Click += (_, _) => CloseUpRequested?.Invoke();
        _cameraRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Children = { _photo, _camera, _live, closeUp, _lights } };
        _body = focusRow;
        // Three narrow rows: status (and auto-focus), camera and lights, focus height.
        var top = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { _pill, _connect } };
        _connect.Margin = new(0);
        _status.MaxWidth = 170;
        _status.TextTrimming = TextTrimming.CharacterEllipsis;
        var grid = new Grid { RowDefinitions = new("Auto,Auto,Auto"), Margin = new(0, 4, 0, 2), RowSpacing = 4, VerticalAlignment = VerticalAlignment.Center };
        grid.Children.Add(top);
        Grid.SetRow(_cameraRow, 1);
        grid.Children.Add(_cameraRow);
        Grid.SetRow(_body, 2);
        grid.Children.Add(_body);
        Content = grid;

        _m.StateChanged += Update;
        _m.ConnectionChanged += Update;
        _m.ModeChanged += _ => Update();
        Update();
    }

    void ShowHeight(double? mm)
    {
        _heightValue = mm is { } v ? Math.Round(v, 1) : null;
        _height.Text = _heightValue is { } h ? $"{h:0.0} mm" : "– mm";
    }

    /// <summary>"21.6", "21.6 mm", "21,6" → 21.6; null if it isn't a number in range.</summary>
    static double? ParseHeight(string? text)
    {
        var t = (text ?? "").Replace("mm", "", StringComparison.OrdinalIgnoreCase).Replace(',', '.').Trim();
        return double.TryParse(t, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) && v is >= 0 and <= 150 ? v : null;
    }

    ContextMenu PillMenu()
    {
        var menu = new ContextMenu();
        if (_m.IsConnected)
        {
            var off = new MenuItem { Header = "Disconnect" };
            off.Click += async (_, _) => await _m.DisconnectAsync();
            menu.Items.Add(off);
        }
        else
        {
            var on = new MenuItem { Header = "Connect" };
            on.Click += async (_, _) => await _m.ConnectNowAsync();
            menu.Items.Add(on);
        }
        var cal = new MenuItem { Header = "Calibrate camera…" };
        cal.Click += (_, _) => CalibrateRequested?.Invoke();
        var set = new MenuItem { Header = "Machine settings…" };
        set.Click += (_, _) => SettingsRequested?.Invoke();
        menu.Items.Add(new Separator());
        menu.Items.Add(cal);
        menu.Items.Add(set);
        return menu;
    }

    void LightsChanged()
    {
        if (_updating) return;
        _m.SetLights((int)Math.Round(_front.Value), (int)Math.Round(_back.Value));
    }

    void Nudge(double mm)
    {
        if (!_m.IsConnected || _m.IsBusyFocusing) return;
        double from = _heightValue ?? _m.Distance ?? 0;
        ShowHeight(Math.Clamp(Math.Round(from + mm, 1), 0, 150));
        _pending = true;
        _note = $"Moving to {_heightValue:0.0} mm…";
        Update();
        _focusDelay.Stop();
        _focusDelay.Start();
    }

    async Task MoveTo(double mm)
    {
        _focusDelay.Stop();
        _pending = false;
        if (_m.Distance is { } d && Math.Abs(d - mm) < 0.05) { Update(); return; }
        _note = $"Moving to {mm:0.0} mm…";
        Update();
        var error = await _m.MoveFocusAsync(mm);
        _note = error;
        // An error stays up for a while, then the status comes back.
        if (error is not null) DispatcherTimer.RunOnce(() => { if (_note == error) { _note = null; Update(); } }, TimeSpan.FromSeconds(8));
        Update();
    }

    void Update()
    {
        _updating = true;
        try
        {
            bool on = _m.IsConnected;
            string mode = _m.Mode;
            var (colour, text) = !on ? ("#6B7380", "Not connected")
                : mode is "P_WORKING" ? ("#4C7BD9", "Running")
                : mode is "P_PAUSE" or "P_WORK" ? ("#D6A24A", _m.ModeText)
                : _m.IsFraming ? ("#D6A24A", "Framing")
                : mode is "P_ERROR" or "P_SLEEP" ? ("#C9554E", _m.ModeText)
                : ("#4FAF83", _m.ModeText);
            _dot.Background = new SolidColorBrush(Color.Parse(colour));
            if (!on) _note = null;
            _status.Text = _note ?? (on ? $"{_m.ModelName ?? _settings.ActiveSetup().Name.Replace("xTool ", "")} · {text}" : text);
            ToolTip.SetTip(_pill, (_note is { } n ? n + "\n\n" : "") +
                (on ? "Click to disconnect, calibrate the camera or open machine settings." : "Click to connect (or for machine settings)."));

            // Not connected: just the status and Connect.
            _body.IsVisible = _cameraRow.IsVisible = on;
            _connect.IsVisible = !on;

            bool idle = on && mode is "P_IDLE" or "P_WORK_DONE" or "P_FINISH" or "";
            _photo.IsEnabled = on && _m.HasCalibration;
            _camera.IsChecked = _m.CameraOnWorkArea;
            _live.IsEnabled = on;
            _live.IsChecked = _m.LiveView;
            _lights.IsEnabled = on;
            _opacity.Value = _m.OverlayOpacity;
            var (f, b) = _m.Lights;
            _front.Value = f;
            _back.Value = b;
            _focus.IsEnabled = idle && !_m.IsBusyFocusing;
            _up.IsEnabled = _down.IsEnabled = _height.IsEnabled = idle && !_m.IsBusyFocusing;
            if (!_pending && !_height.IsKeyboardFocusWithin) ShowHeight(_m.Distance);
        }
        finally { _updating = false; }
    }
}
