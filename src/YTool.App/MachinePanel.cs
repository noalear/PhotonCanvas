using System.Net;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Bitmap = Avalonia.Media.Imaging.Bitmap;
using Avalonia.Threading;
using YTool.Core;
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
    readonly Button _find = Button("Find machines", tip: "Search the local network (needs Windows Firewall to allow YTool Studio).");
    readonly Button _connect = new() { Classes = { "primary" }, Content = "Connect" };
    readonly TextBlock _connState = Hint();
    readonly TextBlock _info = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
    readonly TextBlock _mode = new() { FontWeight = FontWeight.SemiBold };
    readonly TextBlock _distance = Hint();
    readonly CheckBox _lidOpen = new() { Content = "Allow starting with the lid open", IsEnabled = false };
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

    public bool IsConnected => _client?.IsConnected == true;
    /// <summary>Material distance from the head's Z (mm), when known.</summary>
    public double? Distance { get; private set; }
    public string Mode { get; private set; } = "";

    /// <summary>Short text for the status bar.</summary>
    public event Action<string>? StatusChanged;
    public event Action? ConnectionChanged;
    /// <summary>A device event (on the UI thread).</summary>
    public event Action<DeviceEvent>? DeviceEventReceived;

    public MachinePanel(AppSettings settings)
    {
        _settings = settings;
        _ip.Text = settings.LastMachineIp;
        _find.Click += async (_, _) => await Find();
        _found.SelectionChanged += (_, _) => { if (_found.SelectedItem is ComboBoxItem { Tag: string ip }) _ip.Text = ip; };
        _connect.Click += async (_, _) => await ToggleConnect();
        _ip.KeyDown += async (_, e) => { if (e.Key == Avalonia.Input.Key.Enter && !IsConnected) await ToggleConnect(); };
        _lidOpen.IsCheckedChanged += async (_, _) => await SetLidOpen();
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
        _straightenRow = Field("Straighten", _straighten, Hint("Degrees clockwise. The close-up camera is mounted at an angle; 31.2° was measured from your bed slats."),
            "Rotates the close-up picture for display only. Saved snapshots stay as the camera took them.");
        _straightenRow.IsVisible = false;
        _stream.SelectionChanged += (_, _) =>
        {
            _straightenRow.IsVisible = _stream.SelectedIndex == 1;
            if (_live.IsChecked == true) { ToggleLive(); ToggleLive(); }
        };

        Content = new ScrollViewer
        {
            Content = new StackPanel
            {
                Children =
                {
                    Card("Connection", _ip, _found, Row(_find, _connect), _connState, _info),
                    Card("Status", _mode, _distance, _lidOpen),
                    Card("Camera", Row(_live, _stream), imgBorder, _straightenRow, snap, _camInfo),
                    Card("Machine events", _log),
                },
            },
        };
        SetDisconnectedUi("Not connected.");
    }

    void Ui(Action a) => Dispatcher.UIThread.Post(a);

    void Log(string line)
    {
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
        _find.IsEnabled = false;
        _connState.Text = "Searching the network…";
        try
        {
            var found = await Discovery.DiscoverAsync(TimeSpan.FromSeconds(4));
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
                0 => "No machines answered. Check the machine is on and on this network, and that Windows Firewall allows YTool Studio. You can also type the IP address.",
                1 => "Found 1 machine.",
                _ => $"Found {items.Count} machines.",
            };
        }
        catch (Exception e) when (e is System.Net.Sockets.SocketException or IOException or InvalidOperationException)
        {
            _connState.Text = $"Search failed: {e.Message}";
        }
        finally { _find.IsEnabled = true; }
    }

    async Task ToggleConnect()
    {
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
        _connect.IsEnabled = false;
        _connState.Text = $"Connecting to {ip}…";
        var client = new DeviceClient(ip);
        client.EventReceived += ev => Ui(() => OnDeviceEvent(ev));
        client.Disconnected += ex => Ui(() => _ = Disconnect($"Connection lost: {ex.Message}"));
        try
        {
            await client.ConnectAsync(pair: true, onButtonPressRequired: () => Ui(() => _connState.Text = "Press the button on the machine to allow this computer."));
            _client = client;
            _settings.LastMachineIp = ip;
            _settings.Save();
            var info = await client.GetMachineInfoAsync();
            _info.Text = $"Serial {info.Sn ?? "?"} · firmware {info.Firmware ?? "?"}";
            if (!info.AcceptsTextGcode)
                _info.Text += "\n⚠ This firmware does not list text G-code support; sending may fail.";
            _connState.Text = $"Connected to {ip}.";
            _connect.Content = "Disconnect";
            _connect.Classes.Remove("primary");
            _lidOpen.IsEnabled = _live.IsEnabled = true;
            await RefreshLid();
            Log("Connected.");
            StartPolling();
            ConnectionChanged?.Invoke();
        }
        catch (DeviceException e)
        {
            await client.DisposeAsync();
            _connState.Text = e.Message;
        }
        catch (Exception e) when (e is IOException or System.Net.WebSockets.WebSocketException or OperationCanceledException or System.Net.Sockets.SocketException)
        {
            await client.DisposeAsync();
            _connState.Text = $"Couldn't connect to {ip}: {e.Message}";
        }
        finally { _connect.IsEnabled = true; }
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
        "P_SLEEP" => "Asleep",
        "P_AUTOFOCUS" => "Auto-focusing",
        "P_MEASURE" => "Measuring",
        "" => "Unknown",
        _ => mode,
    };

    void ShowStatus(string mode, double? dist)
    {
        Mode = mode;
        Distance = dist;
        _mode.Text = Describe(mode);
        _distance.Text = dist is { } d ? $"Focus distance {d:0.0} mm (head Z {d + _machine.ZOffset:0.00}). Set it on the machine or in its focus menu." : "";
        StatusChanged?.Invoke($"Machine: {Describe(mode)}" + (dist is { } dd ? $" · distance {dd:0.0} mm" : ""));
    }

    void OnDeviceEvent(DeviceEvent ev)
    {
        var text = ev.Type switch
        {
            "WORK_PREPARED" => "Job loaded. Press the start button on the machine.",
            "WORK_STARTED" => "Job started.",
            "WORK_PAUSED" => "Job paused.",
            "WORK_STOPED" => "Job stopped.",
            "WORK_FINISHED" => ev.Info?["result"]?.ToString() is "failed" ? "Job cancelled." : "Job finished.",
            _ => $"{ev.Module} {ev.Type} {ev.InfoText}".Trim(),
        };
        if (ev.Module == "GAP") text = $"Lid {(ev.Type == "OPEN" ? "opened" : ev.Type == "CLOSE" ? "closed" : ev.Type)}.";
        Log(text);
        if (ev.Type == "MODE_CHANGE" && ev.InfoText is { } m) ShowStatus(m, Distance);
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

    async Task SetLidOpen()
    {
        if (_updatingLid || _client is not { } c || _lidOpen.IsChecked is not bool on) return;
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
    public async Task UploadJobAsync(byte[] xf, IProgress<double>? progress, CancellationToken ct)
    {
        if (_client is not { } c || !c.IsConnected) throw new DeviceException(DeviceException.NotConnectedCode, "Not connected to a machine.");
        var resume = await PauseLive();
        try
        {
            await c.UploadJobAsync(xf, progress, ct);
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
