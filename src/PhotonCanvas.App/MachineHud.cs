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
/// The machine, where you look while setting up a job: a floating panel in the corner of the
/// work area. Status at a glance (click it to connect, or for machine settings), then the
/// things you reach for at the machine: the camera picture and live view, the lights,
/// auto-focus and the focus height (nudge it or type it). Folds down to just the status.
/// </summary>
public sealed class MachineHud : UserControl
{
    readonly MachinePanel _m;
    readonly AppSettings _settings;
    readonly Border _dot = new() { Width = 9, Height = 9, CornerRadius = new(5), VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock _status = new() { FontSize = 12.5, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
    readonly Button _pill = new() { Classes = { "chip" }, Padding = new(10, 4) };
    readonly Button _fold = new() { Classes = { "icon" }, MinWidth = 24, MinHeight = 24, Padding = new(2) };
    readonly StackPanel _body;

    readonly Button _photo, _focus;
    readonly ToggleButton _camera = new() { Classes = { "rb" } }, _live = new() { Classes = { "rb" } };
    readonly Button _lights;
    readonly Slider _opacity = new() { Minimum = 0.1, Maximum = 1, Width = 120 };
    readonly Slider _front = new() { Minimum = 0, Maximum = 100, Width = 160 }, _back = new() { Minimum = 0, Maximum = 100, Width = 160 };
    readonly NumericUpDown _height = Number(0, 150, 0.5m, "0.0");
    readonly Button _up, _down;
    readonly TextBlock _focusNote = new() { Classes = { "hint" }, MaxWidth = 230, IsVisible = false };
    readonly Button _connect = new() { Classes = { "chip" }, Content = "Connect", Margin = new(6, 0, 0, 0) };
    readonly DispatcherTimer _focusDelay = new() { Interval = TimeSpan.FromMilliseconds(700) };
    bool _updating, _pending;

    /// <summary>Open machine settings.</summary>
    public event Action? SettingsRequested;

    /// <summary>The controls were folded or shown.</summary>
    public event Action? FoldChanged;

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
        HorizontalAlignment = HorizontalAlignment.Right;
        VerticalAlignment = VerticalAlignment.Top;
        Margin = new(0, 12, 12, 0);

        // ---- status ----
        _pill.Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7, Children = { _dot, _status } };
        _pill.Click += (_, _) => PillMenu().Open(_pill);
        _connect.Click += async (_, _) => await _m.ConnectNowAsync();
        ToolTip.SetTip(_connect, "Connect to the last machine, or the one found on the network.");
        _fold.Click += (_, _) => { _settings.MachineHudFolded = !_settings.MachineHudFolded; _settings.Save(); Update(); FoldChanged?.Invoke(); };

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
        _focus = Tip(new Button { Classes = { "rb" }, Content = IconLabel("IcFocus", "Auto-focus") }, "The machine measures the material under the head and moves to the right focus height. Place your material first.");
        _focus.Click += async (_, _) => await _m.AutoFocusNowAsync();
        _height.ShowButtonSpinner = false;
        _height.Width = 64;
        _height.MinHeight = 26;
        _height.FontSize = 12;
        _height.HorizontalContentAlignment = HorizontalAlignment.Center;
        ToolTip.SetTip(_height, "Focus distance: the gap between the laser head and the material, in mm. Type a value and press Enter to move the head there.");
        _height.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            if (_height.Value is { } v) _ = MoveTo((double)v);
        };
        _up = Tip(new Button { Classes = { "rb" }, Content = "▲", Width = 26, Padding = new(0), HorizontalContentAlignment = HorizontalAlignment.Center }, "Raise the head 1 mm (Shift: 0.1 mm). It moves when you stop clicking.");
        _down = Tip(new Button { Classes = { "rb" }, Content = "▼", Width = 26, Padding = new(0), HorizontalContentAlignment = HorizontalAlignment.Center }, "Lower the head 1 mm (Shift: 0.1 mm). It moves when you stop clicking.");
        _up.AddHandler(PointerReleasedEvent, (_, e) => Nudge(e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 0.1 : 1), Avalonia.Interactivity.RoutingStrategies.Tunnel);
        _down.AddHandler(PointerReleasedEvent, (_, e) => Nudge(e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -0.1 : -1), Avalonia.Interactivity.RoutingStrategies.Tunnel);
        _focusDelay.Tick += (_, _) =>
        {
            _focusDelay.Stop();
            if (_height.Value is { } v) _ = MoveTo((double)v);
        };

        var focusRow = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 3,
            Children =
            {
                new TextBlock { Text = "Focus", FontSize = 12, Foreground = Brush("TextMuted"), VerticalAlignment = VerticalAlignment.Center, Margin = new(4, 0, 4, 0) },
                _down, _height, _up,
                new TextBlock { Text = "mm", FontSize = 12, Foreground = Brush("TextMuted"), VerticalAlignment = VerticalAlignment.Center, Margin = new(2, 0, 6, 0) },
                _focus,
            },
        };
        _body = new StackPanel
        {
            Spacing = 4, Margin = new(0, 6, 0, 0),
            Children =
            {
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Children = { _photo, _camera, _live, _lights } },
                focusRow,
                _focusNote,
            },
        };
        var head = new DockPanel { Children = { _fold, _connect, _pill } };
        DockPanel.SetDock(_fold, Dock.Right);
        DockPanel.SetDock(_connect, Dock.Right);
        _fold.Margin = new(6, 0, 0, 0);
        Content = new Border { Classes = { "hud" }, Child = new StackPanel { Children = { head, _body } } };

        _m.StateChanged += Update;
        _m.ConnectionChanged += Update;
        _m.ModeChanged += _ => Update();
        Update();
    }

    /// <summary>Show or fold the controls after the setting changed elsewhere.</summary>
    public void Refold() => Update();

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
        double from = _height.Value is { } v ? (double)v : _m.Distance ?? 0;
        _height.Value = (decimal)Math.Clamp(Math.Round(from + mm, 1), 0, 150);
        _pending = true;
        _focusNote.Text = $"Moving to {_height.Value:0.0} mm…";
        _focusNote.IsVisible = true;
        _focusDelay.Stop();
        _focusDelay.Start();
    }

    async Task MoveTo(double mm)
    {
        _focusDelay.Stop();
        _pending = false;
        if (_m.Distance is { } d && Math.Abs(d - mm) < 0.05) { Update(); return; }
        _focusNote.Text = $"Moving the head to {mm:0.0} mm…";
        _focusNote.IsVisible = true;
        var error = await _m.MoveFocusAsync(mm);
        _focusNote.Text = error ?? "";
        _focusNote.IsVisible = error is not null;
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
                : mode is "P_WORKING" ? ("#4F8CFF", "Running")
                : mode is "P_PAUSE" or "P_WORK" ? ("#F5A524", _m.ModeText)
                : _m.IsFraming ? ("#F5A524", "Framing")
                : mode is "P_ERROR" or "P_SLEEP" ? ("#F25555", _m.ModeText)
                : ("#2FC27A", _m.ModeText);
            _dot.Background = new SolidColorBrush(Color.Parse(colour));
            _status.Text = on ? $"F2 Ultra · {text}" : text;
            ToolTip.SetTip(_pill, on ? "Click to disconnect, calibrate the camera or open machine settings." : "Click to connect (or for machine settings).");

            // Not connected: just the status and Connect. Connected: the controls, unless folded away.
            bool folded = _settings.MachineHudFolded;
            _body.IsVisible = on && !folded;
            _fold.IsVisible = on;
            _connect.IsVisible = !on;
            _fold.Content = new TextBlock { Text = folded ? "▾" : "▴", FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center };
            ToolTip.SetTip(_fold, folded ? "Show the camera, light and focus controls" : "Fold the machine controls away");

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
            if (!_pending && !_height.IsKeyboardFocusWithin)
                _height.Value = _m.Distance is { } d ? (decimal)Math.Round(d, 1) : null;
            _height.Watermark = on ? "–" : "";
        }
        finally { _updating = false; }
    }
}
