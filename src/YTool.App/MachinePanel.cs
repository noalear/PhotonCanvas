using System.Net;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Bitmap = Avalonia.Media.Imaging.Bitmap;
using Avalonia.Threading;
using YTool.Core;
using YTool.Core.Camera;
using YTool.Device;
using static YTool.App.UiKit;

namespace YTool.App;

/// <summary>
/// Connection to the F2 Ultra: find, connect, status, lid-open setting, live
/// camera (snapshot polling; this firmware has no video stream) and an event
/// log. Nothing here moves the head or fires the laser; jobs only start from
/// the machine's own button.
/// </summary>
public sealed class MachinePanel : UserControl
{
    readonly AppSettings _settings;
    readonly MachineProfile _machine = MachineProfile.F2Ultra;
    DeviceClient? _client;
    CancellationTokenSource? _poll, _camera;
    Task _cameraTask = Task.CompletedTask;

    readonly TextBox _ip = new() { Watermark = "Machine IP address, e.g. 192.168.1.50" };
    readonly ComboBox _found = new() { HorizontalAlignment = HorizontalAlignment.Stretch, PlaceholderText = "Machines found on the network", IsVisible = false };
    readonly Button _find = Button("Find machines", tip: $"Search the local network (needs Windows Firewall to allow {AppIdentity.ProductName}).");
    readonly Button _connect = new() { Classes = { "primary" }, Content = "Connect" };
    readonly TextBlock _connState = Hint();
    readonly CheckBox _autoConnect = new() { Content = "Connect automatically when the program starts" };
    readonly TextBlock _info = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
    readonly TextBlock _mode = new() { FontWeight = FontWeight.SemiBold };
    readonly TextBlock _distance = Hint();
    readonly CheckBox _lidOpen = new() { Content = "Allow starting with the lid open", IsEnabled = false };
    readonly CheckBox _beeper = new() { Content = "Beeper on", IsEnabled = false };
    readonly Button _focus = new() { Content = "Auto-focus", IsEnabled = false, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
    readonly TextBlock _focusState = Hint();
    readonly ToggleSwitch _live = new() { OnContent = "Live view on", OffContent = "Live view off", IsEnabled = false };
    readonly ComboBox _stream = new() { ItemsSource = new[] { "Overview", "Close-up" }, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly Image _image = new() { Stretch = Stretch.Uniform };
    readonly RotateTransform _imageRotation = new();
    readonly NumericUpDown _straighten = Number(-180, 180, 0.5m, "0.#");
    readonly Control _straightenRow;
    readonly TextBlock _camInfo = Hint("Live view refreshes as fast as the machine allows (about 3–4 frames a second). Cameras are dark while a job runs because the lights are off.");
    readonly TextBox _log = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Cascadia Mono,Consolas,monospace"), FontSize = 11, Height = 160 };
    readonly List<string> _logLines = [];
    bool _updatingLid;
    MachineKeys _keys = MachineKeys.Load();
    readonly TextBlock _keysState = new() { TextWrapping = TextWrapping.Wrap };
    readonly Button _keysButton = Button("Set up connection keys…", tip: "One-time setup: reads the codes xTool machines expect from your own xTool Studio.");

    // Camera on the work area
    List<CameraCalibration> _calibrations = CalibrationStore.Load(CalibrationStore.DefaultPath);
    readonly ToggleSwitch _onCanvas = new() { OnContent = "Camera on work area", OffContent = "Camera on work area" };
    readonly Slider _opacity = new() { Minimum = 0.15, Maximum = 1, SmallChange = 0.05, LargeChange = 0.1 };
    readonly TextBlock _calState = Hint();
    readonly Slider _frontLight = new() { Minimum = 0, Maximum = 100, SmallChange = 1, LargeChange = 10, IsEnabled = false };
    readonly Slider _backLight = new() { Minimum = 0, Maximum = 100, SmallChange = 1, LargeChange = 10, IsEnabled = false };
    readonly TextBlock _frontText = Hint(), _backText = Hint();
    bool _updatingLights;
    int _lightTicket;
    int _warping;
    bool _needsWarmup = true;
    // Read by the live-view thread; mirrors the toggle.
    volatile bool _overlayWanted;

    public bool IsConnected => _client?.IsConnected == true;
    /// <summary>Material distance from the head's Z (mm), when known.</summary>
    public double? Distance { get; private set; }
    public string Mode { get; private set; } = "";

    /// <summary>Short text for the status bar.</summary>
    public event Action<string>? StatusChanged;
    public event Action? ConnectionChanged;
    /// <summary>A device event (on the UI thread).</summary>
    public event Action<DeviceEvent>? DeviceEventReceived;
    /// <summary>New straightened camera picture of the work area (null = hide). On the UI thread.</summary>
    public event Action<Bitmap?>? OverlayChanged;
    public event Action<double>? OverlayOpacityChanged;

    public MachinePanel(AppSettings settings)
    {
        _settings = settings;
        _ip.Text = settings.LastMachineIp;
        _autoConnect.IsChecked = settings.AutoConnect;
        _autoConnect.IsCheckedChanged += (_, _) => { _settings.AutoConnect = _autoConnect.IsChecked == true; _settings.Save(); };
        ToolTip.SetTip(_autoConnect, "Tries the last machine first, then searches the network.");
        _find.Click += async (_, _) => await Find();
        _found.SelectionChanged += (_, _) => { if (_found.SelectedItem is ComboBoxItem { Tag: string ip }) _ip.Text = ip; };
        _connect.Click += async (_, _) => await ToggleConnect();
        _keysButton.Click += async (_, _) => await SetUpKeys();
        _ip.KeyDown += async (_, e) => { if (e.Key == Avalonia.Input.Key.Enter && !IsConnected) await ToggleConnect(); };
        _lidOpen.IsCheckedChanged += async (_, _) => await SetLidOpen();
        _beeper.IsCheckedChanged += async (_, _) => await SetBeeper();
        _frontLight.ValueChanged += async (_, _) => await LightsChanged();
        _backLight.ValueChanged += async (_, _) => await LightsChanged();
        _focus.Click += async (_, _) => await AutoFocus();
        ToolTip.SetTip(_focus, "The machine measures the material under the head and moves to the right focus height. Place your material first.");
        ToolTip.SetTip(_beeper, "The machine's beeps for button presses and jobs. Stored on the machine. The firmware only offers on or off, not a volume.");
        _live.IsCheckedChanged += (_, _) => ToggleLive();
        ToolTip.SetTip(_lidOpen, "Stored on the machine (xTool calls it \"Enclosure-opened processing\"). When on, the start button works with the lid up. Wear laser safety glasses.");

        var snap = Button("Save snapshot…", "IcCamera");
        snap.Click += async (_, _) => await SaveSnapshot();
        // The close-up camera sits rotated in the head; straighten it for display.
        var rotated = new LayoutTransformControl { LayoutTransform = _imageRotation, Child = _image, MaxHeight = 300 };
        var imgBorder = new Border { Background = Brush("Bg0"), CornerRadius = new(6), MinHeight = 120, Child = rotated, ClipToBounds = true };
        _straighten.Value = (decimal)_settings.CloseUpRotationDeg;
        _straighten.ValueChanged += (_, _) =>
        {
            _settings.CloseUpRotationDeg = (double)(_straighten.Value ?? 0);
            _settings.Save();
            if (_shownStream == 1) _imageRotation.Angle = _settings.CloseUpRotationDeg;
        };
        _straightenRow = Field("Straighten", _straighten, Hint("Degrees clockwise. The close-up camera is mounted at an angle; about 31° on the F2 Ultra."),
            "Rotates the close-up picture for display only. Saved snapshots stay as the camera took them.");
        _straightenRow.IsVisible = false;
        _stream.SelectionChanged += (_, _) =>
        {
            _straightenRow.IsVisible = _stream.SelectedIndex == 1;
            if (_live.IsChecked == true) { ToggleLive(); ToggleLive(); }
        };

        var refresh = Button("Update photo", "IcCamera", "Take a new overview photo and lay it over the work area.");
        refresh.Click += async (_, _) => await RefreshOverlay();
        var calibrate = Button("Calibrate camera…", tip: "Burn a dot grid and measure it, so the camera picture lines up with where the laser marks.");
        calibrate.Click += async (_, _) => await OpenCalibration();
        _opacity.Value = _settings.CameraOpacity;
        _opacity.ValueChanged += (_, _) => { _settings.CameraOpacity = _opacity.Value; _settings.Save(); OverlayOpacityChanged?.Invoke(_opacity.Value); };
        // Always start with the camera on the work area when it is calibrated; a photo is taken once connected.
        _onCanvas.IsChecked = _calibrations.Count > 0;
        _overlayWanted = _onCanvas.IsChecked == true;
        _onCanvas.IsCheckedChanged += async (_, _) =>
        {
            _overlayWanted = _onCanvas.IsChecked == true;
            _settings.CameraOnWorkArea = _overlayWanted;
            _settings.Save();
            await ToggleOverlay();
        };
        ToolTip.SetTip(_onCanvas, "Lays the overview camera, straightened and to scale, under your design. With live view on (Overview), it updates continuously.");

        Content = new ScrollViewer
        {
            Content = new StackPanel
            {
                Children =
                {
                    Card("Connection", _keysState, _keysButton, _ip, _found, Row(_find, _connect), _autoConnect, _connState, _info),
                    Card("Status", _mode, _distance, _focus, _focusState, _lidOpen, _beeper),
                    Card("Work area camera", _onCanvas, Field("Opacity", _opacity), Row(refresh, calibrate), _calState),
                    Card("Camera lights", LightRow("Front", _frontLight, _frontText), LightRow("Back", _backLight, _backText),
                        Hint("Brightness of the machine's fill lights for the cameras. Saved on the machine.")),
                    Card("Camera", Row(_live, _stream), imgBorder, _straightenRow, snap, _camInfo),
                    Card("Machine events", _log),
                },
            },
        };
        SetDisconnectedUi("Not connected.");
        UpdateCalState();
        UpdateKeysState();
    }

    void UpdateKeysState()
    {
        bool ok = _keys.IsComplete;
        _keysState.Text = ok ? "" : "⚠ Before the first connection, " + AppIdentity.ProductName + " needs its connection keys (a one-time setup).";
        _keysState.IsVisible = !ok;
        _keysButton.IsVisible = !ok;
    }

    /// <summary>Open the connection-keys setup. True when valid keys are in place afterwards.</summary>
    public async Task<bool> SetUpKeys()
    {
        if (TopLevel.GetTopLevel(this) is not Window owner) return false;
        var w = new KeysWindow();
        await w.ShowDialog(owner);
        _keys = MachineKeys.Load();
        UpdateKeysState();
        return _keys.IsComplete;
    }

    /// <summary>Keys present, or the user is walked through setting them up.</summary>
    async Task<bool> EnsureKeys() => _keys.IsComplete || await SetUpKeys();

    public bool HasKeys => _keys.IsComplete;

    public double OverlayOpacity => _opacity.Value;

    static Control LightRow(string label, Slider s, TextBlock value)
    {
        var g = new Grid { ColumnDefinitions = new("60,*,44") };
        g.Children.Add(new TextBlock { Text = label, Classes = { "muted" }, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(s, 1);
        g.Children.Add(s);
        value.VerticalAlignment = VerticalAlignment.Center;
        value.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(value, 2);
        g.Children.Add(value);
        return g;
    }

    void ShowLights(int front, int back)
    {
        _updatingLights = true;
        _frontLight.Value = front;
        _backLight.Value = back;
        _frontText.Text = front == 0 ? "off" : $"{front}%";
        _backText.Text = back == 0 ? "off" : $"{back}%";
        _updatingLights = false;
    }

    /// <summary>Send the slider values once they stop moving.</summary>
    async Task LightsChanged()
    {
        _frontText.Text = _frontLight.Value < 0.5 ? "off" : $"{_frontLight.Value:0}%";
        _backText.Text = _backLight.Value < 0.5 ? "off" : $"{_backLight.Value:0}%";
        if (_updatingLights || _client is not { } c) return;
        int ticket = ++_lightTicket;
        await Task.Delay(250);
        if (ticket != _lightTicket) return;
        try
        {
            await c.SetFillLightsAsync((int)Math.Round(_frontLight.Value), (int)Math.Round(_backLight.Value));
            if (_onCanvas.IsChecked == true && _live.IsChecked != true)
            {
                await Task.Delay(600); // let the camera adjust to the new light
                await RefreshOverlay();
            }
        }
        catch (DeviceException e) { Log($"Couldn't change the lights: {e.Message}"); }
    }

    async Task RefreshLights()
    {
        if (_client is not { } c) return;
        try
        {
            var (f, b) = await c.GetFillLightsAsync();
            ShowLights(f, b);
        }
        catch (DeviceException e) { Log($"Couldn't read the light settings: {e.Message}"); }
    }

    // ---- running job (used by the Job tab) -------------------------------------------

    /// <summary>Machine mode changed (P_IDLE, P_WORK, P_WORKING, P_PAUSE…). On the UI thread.</summary>
    public event Action<string>? ModeChanged;

    public Task PauseJobAsync() => _client?.PauseJobAsync() ?? Task.CompletedTask;
    public Task ResumeJobAsync() => _client?.ResumeJobAsync() ?? Task.CompletedTask;
    public Task StopJobAsync() => _client?.StopJobAsync() ?? Task.CompletedTask;

    bool _loggedProgress;
    public async Task<JobProgress?> GetJobProgressAsync()
    {
        if (_client is not { } c) return null;
        var p = await c.GetJobProgressAsync();
        if (!_loggedProgress)
        {
            _loggedProgress = true;
            AppLog.Write("progress", p.Raw?.ToJsonString() ?? "null"); // shape not yet documented
        }
        return p;
    }

    void UpdateCalState()
    {
        if (_calibrations.Count == 0)
        {
            _calState.Text = "Not calibrated yet. Calibrate once per material distance you use (about 5 minutes and a scrap sheet).";
            return;
        }
        var list = string.Join(", ", _calibrations.OrderBy(c => c.DistanceMm).Select(c => $"{c.DistanceMm:0.#} mm (±{c.RmsMm:0.00})"));
        var choice = CalibrationSet.For(_calibrations, Distance);
        _calState.Text = $"Calibrated at {list}." + (choice is null ? "" : $"\nNow using: {choice.Description}.");
        _calState.Foreground = choice is { Exact: false } && Distance is not null ? Brush("Warn") : Brush("TextMuted");
    }

    async Task OpenCalibration()
    {
        if (TopLevel.GetTopLevel(this) is not Window owner) return;
        var w = new CalibrationWindow(this, _settings, Distance);
        w.Saved += cal =>
        {
            _calibrations = CalibrationStore.With(_calibrations, cal);
            try { CalibrationStore.Save(CalibrationStore.DefaultPath, _calibrations); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Log($"Couldn't save the calibration: {e.Message}"); }
            UpdateCalState();
            Log($"Camera calibrated at {cal.DistanceMm:0.#} mm (average error {cal.RmsMm:0.00} mm).");
        };
        await w.ShowDialog(owner);
        if (_onCanvas.IsChecked == true) await RefreshOverlay();
    }

    async Task ToggleOverlay()
    {
        if (_onCanvas.IsChecked != true)
        {
            OverlayChanged?.Invoke(null);
            return;
        }
        if (_calibrations.Count == 0)
        {
            _onCanvas.IsChecked = false;
            if (TopLevel.GetTopLevel(this) is Window owner)
                await Dialogs.Message(owner, "Calibrate first", "The camera needs calibrating before its picture can be placed accurately on the work area. Click Calibrate camera.");
            return;
        }
        await RefreshOverlay();
    }

    async Task RefreshOverlay()
    {
        if (_onCanvas.IsChecked != true) return;
        if (!IsConnected)
        {
            _calState.Text = "Connect to the machine to take a photo.";
            return;
        }
        try
        {
            if (_needsWarmup)
            {
                // The first frame after connecting is usually blown out while exposure settles.
                _needsWarmup = false;
                await SnapshotAsync(0);
            }
            var jpg = await SnapshotAsync(0);
            // Still too bright? Retake until it settles.
            for (int retry = 0; retry < 3 && await Task.Run(() => CameraWarp.IsOverexposed(jpg)); retry++)
            {
                await Task.Delay(400);
                jpg = await SnapshotAsync(0);
            }
            await WarpForCanvas(jpg);
        }
        catch (DeviceException e) { Log($"Photo failed: {e.Message}"); }
        catch (FormatException e) { Log($"Couldn't read the camera picture: {e.Message}"); }
    }

    /// <summary>Straighten a photo onto the 220 × 220 mm field and hand it to the canvas. Skips if one is already in progress.</summary>
    async Task WarpForCanvas(byte[] jpg)
    {
        if (Interlocked.Exchange(ref _warping, 1) == 1) return;
        try
        {
            var choice = CalibrationSet.For(_calibrations, Distance);
            if (choice is null) return;
            var area = new Rect2(0, 0, _machine.FieldW, _machine.FieldH);
            var bmp = await Task.Run(() =>
            {
                using var src = CameraWarp.Decode(jpg, 2400);
                using var warped = CameraWarp.Warp(src, choice.Map, area, 0.2);
                return CameraImaging.ToAvalonia(warped);
            });
            Ui(() =>
            {
                if (_onCanvas.IsChecked == true) OverlayChanged?.Invoke(bmp);
                else bmp.Dispose();
                UpdateCalState();
            });
        }
        catch (FormatException e) { Ui(() => Log($"Couldn't read the camera picture: {e.Message}")); }
        finally { Interlocked.Exchange(ref _warping, 0); }
    }

    /// <summary>One camera photo (0 = overview, 1 = close-up), pausing live view around it.</summary>
    public async Task<byte[]> SnapshotAsync(int stream)
    {
        if (_client is not { } c || !c.IsConnected) throw new DeviceException(DeviceException.NotConnectedCode, "Not connected to a machine.");
        var resume = await PauseLive();
        try { return await c.SnapshotAsync(stream); }
        finally { resume(); }
    }

    void Ui(Action a) => Dispatcher.UIThread.Post(a);

    void Log(string line)
    {
        AppLog.Write("machine", line);
        _logLines.Add($"{DateTime.Now:HH:mm:ss}  {line}");
        if (_logLines.Count > 200) _logLines.RemoveRange(0, _logLines.Count - 200);
        _log.Text = string.Join("\n", _logLines);
        _log.CaretIndex = _log.Text.Length;
    }

    int _shownStream = -1;

    /// <summary>Display a camera frame, straightened if it came from the close-up camera.</summary>
    void ShowFrame(Bitmap bmp, int stream)
    {
        var old = _image.Source as Bitmap;
        _image.Source = bmp;
        old?.Dispose();
        _shownStream = stream;
        _imageRotation.Angle = stream == 1 ? _settings.CloseUpRotationDeg : 0;
    }

    async Task Find()
    {
        if (!await EnsureKeys()) return;
        _find.IsEnabled = false;
        try { await FindDevices(); }
        finally { _find.IsEnabled = true; }
    }

    /// <summary>Search the network, list what answered, and return it.</summary>
    async Task<IReadOnlyList<DiscoveredDevice>> FindDevices()
    {
        _connState.Text = "Searching the network…";
        try
        {
            var found = await Discovery.DiscoverAsync(_keys, TimeSpan.FromSeconds(4));
            var items = found.Select(d => new ComboBoxItem
            {
                Content = $"{d.Name ?? "xTool"} · {d.DeviceCode ?? "?"} · {d.Ip}",
                Tag = d.Ip,
            }).ToList();
            _found.ItemsSource = items;
            _found.IsVisible = items.Count > 0;
            if (items.Count > 0) _found.SelectedIndex = 0;
            _connState.Text = items.Count switch
            {
                0 => $"No machines answered. Check the machine is on and on this network, and that Windows Firewall allows {AppIdentity.ProductName}. You can also type the IP address.",
                1 => "Found 1 machine.",
                _ => $"Found {items.Count} machines.",
            };
            return found;
        }
        catch (Exception e) when (e is System.Net.Sockets.SocketException or IOException or InvalidOperationException)
        {
            _connState.Text = $"Search failed: {e.Message}";
            AppLog.Error("discovery", e);
            return [];
        }
    }

    async Task ToggleConnect()
    {
        if (_connecting) return;
        if (_client is not null)
        {
            await Disconnect("Disconnected.");
            return;
        }
        var ip = _ip.Text?.Trim() ?? "";
        if (!IPAddress.TryParse(ip, out _))
        {
            _connState.Text = "Enter the machine's IP address (or use Find machines).";
            return;
        }
        if (!await EnsureKeys()) return;
        await ConnectTo(ip);
    }

    /// <summary>
    /// On startup: the last machine first, then whatever answers on the network
    /// (F2 Ultras first). Quietly gives up if nothing is there.
    /// </summary>
    public async Task AutoConnectAsync()
    {
        if (!_settings.AutoConnect || _client is not null || _connecting) return;
        if (!_keys.IsComplete)
        {
            _connState.Text = "Set up the connection keys to connect (button above).";
            return;
        }
        var tried = new HashSet<string>();
        if (_settings.LastMachineIp is { } last && IPAddress.TryParse(last, out _))
        {
            tried.Add(last);
            if (await ConnectTo(last, auto: true)) return;
        }
        if (_client is not null) return; // connected by hand meanwhile
        var found = await FindDevices();
        foreach (var d in found.OrderByDescending(d => d.DeviceCode == _machine.DeviceCode))
        {
            if (_client is not null || !tried.Add(d.Ip)) continue;
            if (await ConnectTo(d.Ip, auto: true)) return;
        }
        if (_client is null)
            _connState.Text = found.Count == 0 && tried.Count == 0
                ? "No machine found yet. Turn it on, then click Find machines or Connect."
                : "Couldn't connect automatically. Check the machine is on, then click Connect.";
    }

    bool _connecting;

    /// <summary>Connect and pair. Returns false (with the reason shown) on failure.</summary>
    async Task<bool> ConnectTo(string ip, bool auto = false)
    {
        if (_connecting || _client is not null) return _client is not null;
        _connecting = true;
        _connect.IsEnabled = false;
        _ip.Text = ip;
        _connState.Text = auto ? $"Connecting automatically to {ip}…" : $"Connecting to {ip}…";
        var client = new DeviceClient(ip, new DeviceClientOptions { PairingKey = _keys.Pairing });
        client.EventReceived += ev => Ui(() => OnDeviceEvent(ev));
        client.Disconnected += ex => Ui(() => _ = Disconnect($"Connection lost: {ex.Message}"));
        try
        {
            await client.ConnectAsync(pair: true, onButtonPressRequired: () => Ui(() => _connState.Text = "Press the button on the machine to allow this computer."));
            _client = client;
            _settings.LastMachineIp = ip;
            _settings.Save();
            var info = await client.GetMachineInfoAsync();
            AppLog.Write("machine-info", info.Raw.ToJsonString());
            _info.Text = $"Serial {info.Sn ?? "?"} · firmware {info.Firmware ?? "?"}";
            if (!info.AcceptsTextGcode)
                _info.Text += "\n⚠ This firmware does not list text G-code support; sending may fail.";
            _connState.Text = $"Connected to {ip}.";
            _connect.Content = "Disconnect";
            _connect.Classes.Remove("primary");
            _lidOpen.IsEnabled = _beeper.IsEnabled = _live.IsEnabled = _focus.IsEnabled = true;
            await RefreshLid();
            await RefreshBeeper();
            await RefreshLights();
            _frontLight.IsEnabled = _backLight.IsEnabled = true;
            _needsWarmup = true;
            Log(auto ? $"Connected automatically to {ip}." : "Connected.");
            StartPolling();
            ConnectionChanged?.Invoke();
            if (_onCanvas.IsChecked == true) _ = RefreshOverlay(); // camera was on last time
            return true;
        }
        catch (DeviceException e)
        {
            await client.DisposeAsync();
            _client = null;
            _connState.Text = e.Message;
            AppLog.Write("machine", $"Connect to {ip} failed: {e.Message}");
        }
        catch (Exception e) when (e is IOException or System.Net.WebSockets.WebSocketException or OperationCanceledException or System.Net.Sockets.SocketException)
        {
            await client.DisposeAsync();
            _client = null;
            _connState.Text = $"Couldn't connect to {ip}: {e.Message}";
            AppLog.Write("machine", $"Connect to {ip} failed: {e.Message}");
        }
        finally
        {
            _connecting = false;
            _connect.IsEnabled = true;
        }
        return false;
    }

    async Task Disconnect(string why)
    {
        var c = _client;
        if (c is null) return;
        _client = null;
        _poll?.Cancel();
        _camera?.Cancel();
        SetDisconnectedUi(why);
        Log(why);
        ConnectionChanged?.Invoke();
        await c.DisposeAsync();
    }

    void SetDisconnectedUi(string why)
    {
        _connState.Text = why;
        _connect.Content = "Connect";
        if (!_connect.Classes.Contains("primary")) _connect.Classes.Add("primary");
        _mode.Text = "Offline";
        _distance.Text = "";
        Distance = null;
        Mode = "";
        _updatingLid = true;
        _lidOpen.IsEnabled = false;
        _beeper.IsEnabled = false;
        _focus.IsEnabled = false;
        _frontLight.IsEnabled = _backLight.IsEnabled = false;
        _focusState.Text = "";
        _updatingLid = false;
        _live.IsChecked = false;
        _live.IsEnabled = false;
        StatusChanged?.Invoke("Machine: offline");
    }

    /// <summary>Close the connection when the app exits.</summary>
    public void Shutdown()
    {
        _poll?.Cancel();
        _camera?.Cancel();
        var c = _client;
        _client = null;
        if (c is not null) _ = Task.Run(async () => await c.DisposeAsync());
    }

    void StartPolling()
    {
        _poll?.Cancel();
        var cts = _poll = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            while (!cts.IsCancellationRequested && _client is { } c)
            {
                try
                {
                    var mode = await c.GetRuntimeModeAsync(cts.Token);
                    double? dist = null;
                    try { dist = await c.GetHeadZAsync(cts.Token) - _machine.ZOffset; }
                    catch (DeviceException) { }
                    Ui(() => ShowStatus(mode, dist));
                }
                catch (DeviceException) { }
                catch (OperationCanceledException) { break; }
                try { await Task.Delay(3000, cts.Token); } catch (OperationCanceledException) { break; }
            }
        });
    }

    static string Describe(string mode) => mode switch
    {
        "P_IDLE" => "Ready",
        "P_WORK" => "Job loaded – waiting for the start button",
        "P_WORKING" => "Running a job",
        "P_PAUSE" => "Paused",
        "P_WORK_DONE" => "Job finished",
        "P_FINISH" => "Job finished",
        "P_SLEEP" => "Asleep",
        "P_AUTOFOCUS" => "Auto-focusing",
        "P_MEASURE" => "Measuring",
        "" => "Unknown",
        _ => mode,
    };

    void ShowStatus(string mode, double? dist)
    {
        bool changed = mode != Mode;
        Mode = mode;
        if (changed) ModeChanged?.Invoke(mode);
        Distance = dist;
        _mode.Text = Describe(mode);
        _distance.Text = dist is { } d ? $"Focus distance {d:0.0} mm (head Z {d + _machine.ZOffset:0.00}). Set it on the machine or in its focus menu." : "";
        StatusChanged?.Invoke($"Machine: {Describe(mode)}" + (dist is { } dd ? $" · distance {dd:0.0} mm" : ""));
    }

    void OnDeviceEvent(DeviceEvent ev)
    {
        // Keep the raw message: the firmware's event shapes vary and are only partly documented.
        AppLog.Write("event", ev.Raw.ToJsonString());
        try { DescribeEvent(ev); }
        catch (Exception e) when (e is InvalidOperationException or FormatException or KeyNotFoundException)
        {
            AppLog.Error("device event", e);
            Log($"{ev.Module} {ev.Type} (unrecognised details)".Trim());
        }
    }

    /// <summary>
    /// "failed" means the job was cancelled. The machine has been seen sending
    /// the result both as an object ({"result":"failed",...}) and as plain text.
    /// </summary>
    static bool WasCancelled(DeviceEvent ev) => ev.Info switch
    {
        System.Text.Json.Nodes.JsonObject o => o.TryGetPropertyValue("result", out var r) && r?.ToString() == "failed",
        _ => ev.InfoText?.Contains("fail", StringComparison.OrdinalIgnoreCase) == true,
    };

    /// <summary>Mode from a MODE_CHANGE event: plain text, or {"mode":"P_IDLE",...} on firmware 40.54.</summary>
    static string? ModeOf(DeviceEvent ev) =>
        ev.Info is System.Text.Json.Nodes.JsonObject o ? o["mode"]?.ToString() : ev.InfoText;

    void DescribeEvent(DeviceEvent ev)
    {
        if (ev.Type == "MODE_CHANGE")
        {
            if (ModeOf(ev) is { Length: > 0 } mode && mode != Mode)
            {
                ShowStatus(mode, Distance);
                Log($"Machine: {Describe(mode)}.");
            }
            DeviceEventReceived?.Invoke(ev);
            return;
        }
        if (ev.Module == "DEVICE_CONFIG" && ev.Info is System.Text.Json.Nodes.JsonObject cfg)
        {
            // The machine pushes its settings after any change (ours or on its own screen).
            _updatingLid = true;
            try
            {
                if (cfg["beepEnable"]?.GetValueKind() is System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False)
                    _beeper.IsChecked = cfg["beepEnable"]!.GetValue<bool>();
                if (cfg["gapCheckWithKey"]?.GetValueKind() is System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False)
                    _lidOpen.IsChecked = !cfg["gapCheckWithKey"]!.GetValue<bool>();
                if (cfg["fillLightBrightFront"] is System.Text.Json.Nodes.JsonValue fl && cfg["fillLightBrightBack"] is System.Text.Json.Nodes.JsonValue bl
                    && fl.TryGetValue<double>(out var fr) && bl.TryGetValue<double>(out var br))
                    ShowLights(DeviceClient.LightToPercent(fr), DeviceClient.LightToPercent(br));
            }
            finally { _updatingLid = false; }
            DeviceEventReceived?.Invoke(ev);
            return;
        }
        var text = ev.Type switch
        {
            "WORK_PREPARED" => "Job loaded. Press the start button on the machine.",
            "WORK_STARTED" => "Job started.",
            "WORK_PAUSED" => "Job paused.",
            "WORK_STOPED" => "Job stopped.",
            "WORK_FINISHED" => WasCancelled(ev) ? "Job cancelled." : "Job finished.",
            _ => $"{ev.Module} {ev.Type} {ev.InfoText}".Trim(),
        };
        if (ev.Module == "GAP") text = $"Lid {(ev.Type == "OPEN" ? "opened" : ev.Type == "CLOSE" ? "closed" : ev.Type)}.";
        Log(text);
        DeviceEventReceived?.Invoke(ev);
    }

    async Task RefreshLid()
    {
        if (_client is not { } c) return;
        try
        {
            var on = await c.GetLidOpenProcessingAsync();
            _updatingLid = true;
            _lidOpen.IsChecked = on;
        }
        catch (DeviceException e) { Log($"Couldn't read the lid setting: {e.Message}"); }
        finally { _updatingLid = false; }
    }

    async Task AutoFocus()
    {
        if (_client is not { } c) return;
        if (Mode is not ("P_IDLE" or "P_WORK_DONE" or "P_FINISH" or ""))
        {
            _focusState.Text = $"The machine is busy ({Describe(Mode)}). Try again when it's ready.";
            return;
        }
        _focus.IsEnabled = false;
        _focusState.Text = "Focusing… the head will move. Press the machine's button to stop.";
        Log("Auto-focus started.");
        try
        {
            double z = await c.AutoFocusAsync();
            double dist = z - _machine.ZOffset;
            ShowStatus("P_IDLE", dist);
            _focusState.Text = $"Focused: material distance {dist:0.0} mm.";
            Log($"Auto-focus finished: distance {dist:0.0} mm (head Z {z:0.00}).");
            UpdateCalState();
            if (_onCanvas.IsChecked == true) await RefreshOverlay(); // the camera view depends on the distance
        }
        catch (DeviceException e)
        {
            _focusState.Text = e.Message;
            Log($"Auto-focus: {e.Message}");
        }
        finally { _focus.IsEnabled = IsConnected; }
    }

    public const string LidOpenWarning =
        "With this on, the machine will run with its lid open. The lid is what blocks the laser's light, smoke and sparks. " +
        "With it open:\n\n" +
        "• Direct or reflected laser light can permanently blind you or anyone nearby in a fraction of a second. Everyone in the room must wear safety glasses rated for this laser's wavelengths (455 nm blue and 1064 nm infrared).\n" +
        "• Smoke and fumes escape into the room, and fires can start or spread faster. Never leave the machine running unattended.\n" +
        "• Keep children and pets away.\n\n" +
        "This setting is stored on the machine and stays on until you turn it off. You use it entirely at your own risk; this software comes with no warranty.";

    async Task RefreshBeeper()
    {
        if (_client is not { } c) return;
        try
        {
            var on = await c.GetBeeperEnabledAsync();
            _updatingLid = true;
            _beeper.IsChecked = on;
        }
        catch (DeviceException e) { Log($"Couldn't read the beeper setting: {e.Message}"); }
        finally { _updatingLid = false; }
    }

    async Task SetBeeper()
    {
        if (_updatingLid || _client is not { } c || _beeper.IsChecked is not bool on) return;
        try
        {
            await c.SetBeeperEnabledAsync(on);
            Log(on ? "Beeper turned on." : "Beeper turned off.");
        }
        catch (DeviceException e) { Log($"Couldn't change the beeper setting: {e.Message}"); }
        await RefreshBeeper();
    }

    async Task SetLidOpen()
    {
        if (_updatingLid || _client is not { } c || _lidOpen.IsChecked is not bool on) return;
        if (on && TopLevel.GetTopLevel(this) is Window owner &&
            !await Dialogs.Confirm(owner, "Allow starting with the lid open?", LidOpenWarning, "I understand, allow it", "Keep it off"))
        {
            await RefreshLid();
            return;
        }
        try
        {
            await c.SetLidOpenProcessingAsync(on);
            Log(on ? "Lid-open start allowed." : "Lid must be closed to start.");
        }
        catch (DeviceException e) { Log($"Couldn't change the lid setting: {e.Message}"); }
        await RefreshLid();
    }

    void ToggleLive()
    {
        _camera?.Cancel();
        _camera = null;
        if (_live.IsChecked != true || _client is null) return;
        var cts = _camera = new CancellationTokenSource();
        int stream = Math.Max(0, _stream.SelectedIndex);
        _cameraTask = Task.Run(async () =>
        {
            int failures = 0;
            while (!cts.IsCancellationRequested && _client is { } c)
            {
                try
                {
                    var t0 = DateTime.UtcNow;
                    var jpg = await c.SnapshotAsync(stream, cts.Token);
                    using var ms = new MemoryStream(jpg);
                    // The cameras are 4000×3000; decoding at panel size keeps this light.
                    var bmp = Bitmap.DecodeToWidth(ms, 1000);
                    var fps = 1 / Math.Max(0.05, (DateTime.UtcNow - t0).TotalSeconds);
                    failures = 0;
                    if (stream == 0 && _overlayWanted) _ = WarpForCanvas(jpg);
                    Ui(() =>
                    {
                        if (cts.IsCancellationRequested) { bmp.Dispose(); return; }
                        ShowFrame(bmp, stream);
                        _camInfo.Text = $"{(stream == 0 ? "Overview" : "Close-up")} · {fps:0.0} frames/s";
                    });
                }
                catch (OperationCanceledException) { break; }
                catch (DeviceException e)
                {
                    if (++failures >= 3)
                    {
                        Ui(() => { Log($"Live view stopped: {e.Message}"); _live.IsChecked = false; });
                        break;
                    }
                    try { await Task.Delay(500, cts.Token); } catch (OperationCanceledException) { break; }
                }
            }
        });
    }

    async Task SaveSnapshot()
    {
        if (_client is not { } c || TopLevel.GetTopLevel(this) is not Window owner) return;
        var resume = await PauseLive();
        try
        {
            var jpg = await c.SnapshotAsync(Math.Max(0, _stream.SelectedIndex));
            resume();
            resume = () => { };
            var file = await owner.StorageProvider.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
            {
                Title = "Save snapshot",
                DefaultExtension = "jpg",
                SuggestedFileName = $"snapshot_{DateTime.Now:yyyyMMdd_HHmmss}.jpg",
            });
            if (file?.TryGetLocalPath() is { } path) await File.WriteAllBytesAsync(path, jpg);
        }
        catch (DeviceException e) { await Dialogs.Message(owner, "Snapshot failed", e.Message); }
        finally { resume(); }
    }

    /// <summary>Upload a job. The machine then waits for its start button.</summary>
    public async Task UploadJobAsync(byte[] xf, IProgress<double>? progress, CancellationToken ct, bool replaceLoadedJob = false)
    {
        if (_client is not { } c || !c.IsConnected) throw new DeviceException(DeviceException.NotConnectedCode, "Not connected to a machine.");
        var resume = await PauseLive();
        try
        {
            await c.UploadJobAsync(xf, progress, ct, replaceLoadedJob);
            Log("Job uploaded. Press the start button on the machine.");
        }
        finally { resume(); }
    }

    /// <summary>
    /// Snapshots, saves and uploads all use the one file channel, so live view
    /// stops (after its current frame) and the returned action restarts it.
    /// </summary>
    async Task<Action> PauseLive()
    {
        bool wasLive = _live.IsChecked == true;
        _camera?.Cancel();
        try { await _cameraTask; } catch (OperationCanceledException) { }
        return () => { if (wasLive && _live.IsChecked == true) ToggleLive(); };
    }
}
