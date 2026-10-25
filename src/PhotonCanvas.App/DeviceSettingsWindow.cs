using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using PhotonCanvas.Device;
using static PhotonCanvas.App.UiKit;

namespace PhotonCanvas.App;

/// <summary>
/// The machine's own settings, stored on it: its name and versions, the safety alarms and
/// switches, the exhaust run-on and sleep, galvo timing for each laser, Wi-Fi, the state of the
/// interlocks, and keeping the last job on the machine. Nothing here moves the head or fires
/// the laser.
/// </summary>
public sealed class DeviceSettingsWindow : Window
{
    readonly DeviceClient _c;
    readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Classes = { "hint" } };
    bool _loading;

    // About
    readonly TextBox _name = new() { MaxLength = 15, Width = 200 };
    readonly TextBlock _about = new() { TextWrapping = TextWrapping.Wrap };
    // Safety
    readonly CheckBox _flame = new() { Content = "Flame alarm: stop and warn when the camera sees fire" };
    readonly CheckBox _moved = new() { Content = "Stop when the machine is moved or tilted" };
    readonly CheckBox _lidStops = new() { Content = "Stop when the lid is opened" };
    readonly CheckBox _overheat = new() { Content = "MOPA laser overheat alarm" };
    readonly CheckBox _beep = new() { Content = "Beeper" };
    // Exhaust and sleep
    readonly NumericUpDown _runOn = Number(1, 600, 5, "0");
    readonly CheckBox _purifierBeep = new() { Content = "Purifier beeps (when one is connected)" };
    readonly CheckBox _sleep = new() { Content = "Go to sleep when left idle" };
    // Timing
    readonly Dictionary<(string Laser, string Key), NumericUpDown> _timing = [];
    // Wi-Fi
    readonly TextBlock _wifiNow = new() { TextWrapping = TextWrapping.Wrap };
    readonly ComboBox _networks = new() { HorizontalAlignment = HorizontalAlignment.Stretch, PlaceholderText = "Scan first" };
    readonly TextBox _password = new() { PasswordChar = '•', Watermark = "Wi-Fi password" };
    // Interlocks
    readonly TextBlock _locks = new() { TextWrapping = TextWrapping.Wrap };
    // Keep the last job
    readonly TextBox _keepName = new() { MaxLength = 15, Watermark = "e.g. Coaster1", Width = 200 };

    public DeviceSettingsWindow(DeviceClient client)
    {
        _c = client;
        Title = "Device settings";
        Width = 740;
        Height = 640;
        MinWidth = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");
        _runOn.Value = 10;

        var rename = Button("Rename");
        rename.Click += async (_, _) => await Do("Renamed.", () => _c.SetDeviceNameAsync(_name.Text ?? ""));
        Wire(_flame, "flameAlarm", on => on, "Turn the flame alarm off?", FlameOffWarning);
        Wire(_moved, "workingMode", on => on ? "NORMAL" : "HANDLE", "Keep running when moved?", MovedOffWarning);
        Wire(_lidStops, "gapCheck", on => on, "Keep running with the lid open?", LidStopOffWarning);
        Wire(_overheat, "laserTempAlarmEnable", on => on, "Turn the overheat alarm off?", OverheatOffWarning);
        Wire(_beep, "beepEnable", on => on, null, null);
        Wire(_purifierBeep, "purifierBeepEnable", on => on, null, null);
        Wire(_sleep, "autoSleepEnable", on => on, null, null);
        _runOn.ValueChanged += async (_, _) =>
        {
            if (_loading || _runOn.Value is not { } v) return;
            await Do($"The exhaust keeps running {v:0} s after a job.", () => _c.SetConfigAsync("purifierTimeout", (int)v));
        };

        var timingGrid = new Grid { ColumnDefinitions = new("*,120,120"), RowSpacing = 6, ColumnSpacing = 8 };
        timingGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        AddCell(timingGrid, new TextBlock { Text = "Diode (blue)", Classes = { "muted" } }, 0, 1);
        AddCell(timingGrid, new TextBlock { Text = "MOPA (red)", Classes = { "muted" } }, 0, 2);
        int row = 1;
        foreach (var (key, label) in DeviceSettings.TimingKeys)
        {
            timingGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            AddCell(timingGrid, new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center }, row, 0);
            int col = 1;
            foreach (var laser in new[] { "blue", "red" })
            {
                var n = Number(-10000, 10000, 10, "0");
                _timing[(laser, key)] = n;
                AddCell(timingGrid, n, row, col++);
            }
            row++;
        }
        var saveTiming = new Button { Content = "Save timing", Classes = { "primary" } };
        saveTiming.Click += async (_, _) => await SaveTiming();
        var resetBlue = Button("Factory timing: diode");
        resetBlue.Click += async (_, _) => await ResetTiming("blue");
        var resetRed = Button("Factory timing: MOPA");
        resetRed.Click += async (_, _) => await ResetTiming("red");

        var scan = Button("Scan for networks");
        scan.Click += async (_, _) => await Scan();
        var join = new Button { Content = "Join", Classes = { "primary" } };
        join.Click += async (_, _) => await Join();
        var refreshLocks = Button("Check again");
        refreshLocks.Click += async (_, _) => await RefreshLocks();
        var keep = Button("Keep the last job on the machine");
        keep.Click += async (_, _) => await Do($"The last job is kept on the machine as {_keepName.Text}: run it again from the machine's screen.", () => _c.KeepLastJobAsync(_keepName.Text?.Trim() ?? ""));

        var tabs = new TabControl
        {
            Items =
            {
                Page("Machine",
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _name, rename } },
                    Hint("Up to 15 characters: shown on the machine and when searching for it."),
                    _about),
                Page("Safety",
                    _flame, _moved, _lidStops, _overheat, _beep,
                    Hint("Stored on the machine, so they apply whichever program sends the job. \"Allow starting with the lid open\" is in the machine panel.")),
                Page("Exhaust & sleep",
                    Field("Exhaust run-on (s)", _runOn, tip: "How long the exhaust or purifier keeps running after a job, to clear the smoke."),
                    _purifierBeep, _sleep),
                Page("Galvo timing",
                    Hint("Delays that line the laser up with the mirrors' movement: raise the on delay if lines start with a dot or gap, the off delay if they end with a blob, the corner delay if corners round off. " +
                         "Values are shown as xTool Studio shows them. Change them a little at a time and burn a test between changes."),
                    timingGrid,
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { saveTiming, resetBlue, resetRed } }),
                Page("Wi-Fi",
                    _wifiNow, scan, Field("Network", _networks), Field("Password", _password), join,
                    Hint("The machine changes over to the new network straight away: this connection drops, and it's found again on the new network.")),
                Page("Interlocks",
                    _locks, refreshLocks,
                    Hint("What the machine's own safety sensors report right now.")),
                Page("Keep a job",
                    Hint("Keep the last finished job on the machine to run again from its own screen, without the computer. Needs the touchscreen controller."),
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _keepName, keep } }),
            },
        };
        Content = new DockPanel
        {
            Margin = new(16),
            Children = { new Border { [DockPanel.DockProperty] = Dock.Bottom, Padding = new(0, 8, 0, 0), Child = _status }, tabs },
        };
        Opened += async (_, _) => await LoadAll();
    }

    static TabItem Page(string header, params Control[] content)
    {
        var panel = new StackPanel { Spacing = 10, Margin = new(0, 12, 8, 0) };
        panel.Children.AddRange(content);
        return new TabItem { Header = header, Content = new ScrollViewer { Content = panel } };
    }

    static void AddCell(Grid g, Control c, int row, int col)
    {
        Grid.SetRow(c, row);
        Grid.SetColumn(c, col);
        g.Children.Add(c);
    }

    const string StoredOnMachine =
        "This setting is stored on the machine and stays off until you turn it on again, whichever program sends the job. " +
        "You use it entirely at your own risk; this software comes with no warranty.";

    public const string FlameOffWarning =
        "With this off, the machine won't stop or warn you when its camera sees fire. Lasers set material alight, and a small flame can become a fire in seconds.\n\n" +
        "• Never leave a job running unattended, not even for a moment.\n" +
        "• Keep a fire extinguisher suitable for electrical fires within reach, and know how to use it.\n" +
        "• Only turn it off if it gives false alarms on a material you know, and turn it back on afterwards.\n\n" + StoredOnMachine;

    public const string MovedOffWarning =
        "With this off, the machine keeps firing if it is knocked, moved or tilted during a job. The beam can then land outside the material: on the bed, the enclosure, or out through the window.\n\n" +
        "• Direct or reflected laser light can permanently blind you or anyone nearby in a fraction of a second.\n" +
        "• A beam in the wrong place can start a fire.\n" +
        "• Keep the machine on a stable surface, never leave it running unattended, and keep children and pets away.\n\n" + StoredOnMachine;

    public const string LidStopOffWarning =
        "With this off, the machine keeps running when the lid is opened during a job. The lid is what blocks the laser's light, smoke and sparks. With it open:\n\n" +
        "• Direct or reflected laser light can permanently blind you or anyone nearby in a fraction of a second. Everyone in the room must wear safety glasses rated for this laser's wavelengths (455 nm blue and 1064 nm infrared).\n" +
        "• Smoke and fumes escape into the room, and fires can start or spread faster. Never leave the machine running unattended.\n" +
        "• Keep children and pets away.\n\n" + StoredOnMachine;

    public const string OverheatOffWarning =
        "With this off, the machine won't stop when the MOPA laser gets too hot.\n\n" +
        "• Running too hot can permanently damage the laser source, which is costly to replace.\n" +
        "• An overheating machine is a fire risk. Never leave a job running unattended.\n" +
        "• If the alarm goes off, let the machine cool and check its ventilation and room temperature instead of turning the alarm off.\n\n" + StoredOnMachine;

    /// <summary>A switch stored on the machine; turning a safety one off asks first, with the full warning.</summary>
    void Wire(CheckBox box, string key, Func<bool, JsonNode> value, string? offTitle, string? offWarning)
    {
        box.IsCheckedChanged += async (_, _) =>
        {
            if (_loading || box.IsChecked is not bool on) return;
            if (!on && offTitle is not null && !await Dialogs.Confirm(this, offTitle, offWarning!, "I understand, turn it off", "Keep it on"))
            {
                _loading = true;
                box.IsChecked = true;
                _loading = false;
                return;
            }
            await Do($"{box.Content}: {(on ? "on" : "off")}.", () => _c.SetConfigAsync(key, value(on)));
        };
    }

    async Task Do(string done, Func<Task> action)
    {
        try
        {
            await action();
            _status.Text = done;
            _status.Foreground = Brush("Ok");
        }
        catch (Exception e) when (e is DeviceException or ArgumentException)
        {
            _status.Text = e.Message;
            _status.Foreground = Brush("Warn");
        }
    }

    async Task LoadAll()
    {
        _loading = true;
        try
        {
            var info = await _c.GetMachineInfoAsync();
            var raw = info.Raw;
            _name.Text = raw["deviceName"]?.ToString() ?? "";
            var lines = new List<string>
            {
                $"Serial {info.Sn ?? "?"} · firmware {info.Firmware ?? "?"}",
                $"Model {raw["machineType"] ?? "?"} {raw["machineSubType"]}".Trim(),
            };
            var ips = new[] { ("Wi-Fi", "wlan0-ip"), ("USB", "usb0-ip"), ("Ethernet", "eth0-0-ip") }
                .Select(x => raw[x.Item2]?.ToString() is { Length: > 0 } ip ? $"{x.Item1} {ip}" : null).OfType<string>().ToList();
            if (ips.Count > 0) lines.Add(string.Join(" · ", ips));
            try
            {
                var (online, offline) = await _c.GetJobCountsAsync();
                lines.Add($"Jobs run: {online + offline} ({online} from a computer, {offline} from its own screen)");
            }
            catch (DeviceException) { }
            _about.Text = string.Join("\n", lines);

            var cfg = await _c.GetConfigAsync(DeviceSettings.Keys);
            Set(_flame, cfg["flameAlarm"]);
            _moved.IsChecked = cfg["workingMode"]?.ToString() is { } m ? m != "HANDLE" : null;
            Set(_lidStops, cfg["gapCheck"]);
            Set(_overheat, cfg["laserTempAlarmEnable"]);
            Set(_beep, cfg["beepEnable"]);
            Set(_purifierBeep, cfg["purifierBeepEnable"]);
            Set(_sleep, cfg["autoSleepEnable"]);
            if (cfg["purifierTimeout"] is JsonValue pv && pv.TryGetValue<double>(out var t)) _runOn.Value = (decimal)Math.Clamp(t, 1, 600);
            await LoadTiming();
            try { _wifiNow.Text = await _c.ConnectedWifiAsync() is { Length: > 0 } ssid ? $"On Wi-Fi network “{ssid}”." : "Not on Wi-Fi."; }
            catch (DeviceException) { _wifiNow.Text = ""; }
            await RefreshLocks();
            _status.Text = "Read from the machine.";
        }
        catch (DeviceException e)
        {
            _status.Text = $"Couldn't read the machine's settings: {e.Message}";
            _status.Foreground = Brush("Warn");
        }
        finally { _loading = false; }

        static void Set(CheckBox b, JsonNode? v) => b.IsChecked = v is JsonValue j && j.TryGetValue<bool>(out var on) ? on : null;
    }

    async Task LoadTiming()
    {
        foreach (var laser in new[] { "blue", "red" })
        {
            try
            {
                var t = await _c.GetTimingAsync(laser);
                foreach (var (key, _) in DeviceSettings.TimingKeys)
                    _timing[(laser, key)].Value = t.TryGetValue(key, out var v) ? (decimal)Math.Round(v * 10) : null;
            }
            catch (DeviceException) { }
        }
    }

    async Task SaveTiming()
    {
        if (!await Dialogs.Confirm(this, "Save galvo timing?", "The new delays are stored on the machine and used by every job from now on. Burn a test pattern afterwards to check.", "Save", "Cancel")) return;
        await Do("Timing saved on the machine.", async () =>
        {
            var current = new Dictionary<string, Dictionary<string, double>>();
            foreach (var laser in new[] { "blue", "red" }) current[laser] = await _c.GetTimingAsync(laser);
            foreach (var ((laser, key), box) in _timing)
            {
                if (box.Value is not { } shown) continue;
                // As xTool Studio stores them: shown ÷ 10, in whole steps of 10.
                double stored = Math.Ceiling((double)shown / 10) * 10 / 10;
                if (current[laser].TryGetValue(key, out var was) && Math.Abs(was - stored) < 1e-9) continue;
                await _c.SetTimingAsync(laser, key, stored);
            }
            await LoadTiming();
        });
    }

    async Task ResetTiming(string laser)
    {
        if (!await Dialogs.Confirm(this, "Back to factory timing?", $"The {(laser == "red" ? "MOPA" : "diode")} laser's delays go back to how they came.", "Reset", "Cancel")) return;
        await Do("Factory timing restored.", async () => { await _c.ResetTimingAsync(laser); await LoadTiming(); });
    }

    async Task Scan()
    {
        _status.Text = "Scanning…";
        try
        {
            var nets = await _c.ScanWifiAsync();
            _networks.ItemsSource = nets.Select(n => n.Signal is { } s ? $"{n.Ssid}  ({s})" : n.Ssid).ToList();
            _networks.Tag = nets;
            if (nets.Count > 0) _networks.SelectedIndex = 0;
            _status.Text = nets.Count == 0 ? "No networks found." : $"{nets.Count} networks found.";
        }
        catch (DeviceException e) { _status.Text = $"Couldn't scan: {e.Message}"; }
    }

    async Task Join()
    {
        if (_networks.Tag is not List<WifiNetwork> nets || _networks.SelectedIndex < 0) { _status.Text = "Scan and pick a network first."; return; }
        var ssid = nets[_networks.SelectedIndex].Ssid;
        if (!await Dialogs.Confirm(this, "Join this network?", $"The machine moves to “{ssid}”. This connection will drop; connect again once it's on the new network (Find machines).", "Join", "Cancel")) return;
        await Do($"The machine is joining “{ssid}”.", () => _c.JoinWifiAsync(ssid, _password.Text ?? ""));
    }

    async Task RefreshLocks()
    {
        var lines = new List<string>();
        async Task One(string type, string label, Func<JsonObject, string?> say)
        {
            try { if (say(await _c.GetPeripheralAsync(type)) is { } s) lines.Add($"{label}: {s}"); }
            catch (DeviceException) { }
        }
        await One("gap", "Lid", o => (o["stateMCU"] ?? o["stateRK3562"] ?? o["state"])?.ToString() is { } v ? (v == "off" ? "closed" : "open") : null);
        await One("machine_lock", "Safety key", o => o["state"]?.ToString() is { } v ? (v == "on" ? "in" : "out") : null);
        await One("smoking_fan", "Exhaust fan", o => o["state"]?.ToString() is { } v ? (v == "install" ? "connected" : "not connected") : null);
        await One("emergency_stop", "Emergency stop", o => o["state"]?.ToString() is { } v ? (v == "on" ? "PRESSED" : "released") : null);
        _locks.Text = lines.Count == 0 ? "The machine didn't report its sensors." : string.Join("\n", lines);
    }
}
