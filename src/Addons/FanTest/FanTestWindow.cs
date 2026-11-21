using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using PhotonCanvas.App;
using PhotonCanvas.App.Addons;
using PhotonCanvas.Core;
using PhotonCanvas.Device;
using static PhotonCanvas.App.UiKit;

namespace PhotonCanvas.Addon.FanTest;

/// <summary>
/// Tries the ways the exhaust fan's speed might be set, one at a time, so Kyle can hear which (if
/// any) works: read what the machine reports, write the stored fan speed some xTool machines have,
/// send one fan command now, or load a laser-off job that steps the fan through its gears. Only fan
/// commands are ever sent (<see cref="FanCommands.IsFanOnly"/>), and each send is confirmed first.
/// </summary>
public sealed class FanTestWindow : Window
{
    static readonly FontFamily Mono = new("Consolas, Menlo, monospace");
    static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    readonly IAddonHost _host;

    readonly Button _read = Button("Read the fan's state", tip: "Ask the machine what it reports about the exhaust fan and its stored fan settings. Only reads.");
    readonly TextBlock _readNote = Hint();

    readonly NumericUpDown _speed = Number(FanCommands.MinSpeedPct, FanCommands.MaxSpeedPct, 10, "0");
    readonly Button _writeSpeed = Button("Write to the machine", tip: "Store this fan speed on the machine (smokeFanSpeed). Only changes a fan setting.");

    readonly ComboBox _target = new()
    {
        ItemsSource = new[] { "The machine itself", "Duct fan port (P70)", "IF2 inline fan port (P78)" },
        SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch,
    };
    readonly ComboBox _control = new()
    {
        ItemsSource = new[] { "Manual: hold this gear (A)", "Automatic at this gear (B), as jobs send" },
        SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch,
    };
    readonly NumericUpDown _gear = Number(0, FanCommands.MaxGear, 1, "0");
    readonly SelectableTextBlock _line = new() { FontFamily = Mono, FontSize = 13 };
    readonly Button _send = Button("Send now", tip: "Send this one fan command straight away (it asks first)");

    readonly ComboBox _jobControl = new()
    {
        ItemsSource = new[] { "Automatic gears (B), as jobs send", "Manual gears (A)" },
        SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch,
    };
    readonly NumericUpDown _stepSeconds = Number(5, 60, 5, "0");
    readonly Button _sendJob = Button("Load the fan job", tip: "Upload a laser-off job that steps the fan through gears 1 to 4. It waits for the machine's button.");

    readonly TextBox _log = new()
    {
        IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, FontFamily = Mono, FontSize = 12,
        Height = 170, HorizontalAlignment = HorizontalAlignment.Stretch,
    };
    readonly TextBlock _connected = new() { TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.SemiBold };

    bool _busy;

    public FanTestWindow(IAddonHost host)
    {
        _host = host;
        Title = "Exhaust fan test";
        Width = 600;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");

        _speed.Value = FanCommands.MaxSpeedPct;
        _gear.Value = 1;
        _stepSeconds.Value = 15;
        _target.SelectionChanged += (_, _) => ShowLine();
        _control.SelectionChanged += (_, _) => ShowLine();
        _gear.ValueChanged += (_, _) => ShowLine();
        _read.Click += async (_, _) => await ReadState();
        _writeSpeed.Click += async (_, _) => await WriteSpeed();
        _send.Click += async (_, _) => await SendNow();
        _sendJob.Click += async (_, _) => await SendJob();

        var close = new Button { Content = "Close", IsCancel = true, MinWidth = 100, HorizontalAlignment = HorizontalAlignment.Right };
        close.Click += (_, _) => Close();

        Content = new ScrollViewer
        {
            MaxHeight = 780,
            Content = new Border
            {
                Padding = new Thickness(18),
                Child = new StackPanel
                {
                    Spacing = 10,
                    Children =
                    {
                        Hint("Find out which command, if any, changes your exhaust fan's speed. Everything here only touches the fan: the laser stays off and the head doesn't move. Each send asks first and is written in the log below, so you can tell what you heard after which command."),
                        _connected,
                        Explained("1. What the machine says",
                            "Reads the exhaust fan's state and the machine's stored fan settings. A stored fan speed (smokeFanSpeed) in the answer means this machine knows that setting.",
                            _read, _readNote),
                        Explained("2. Stored fan speed (experimental)",
                            "xTool's P2S keeps its fan speed as a stored setting, 10 to 100%. The F2 Ultra may keep it too, though xTool Studio doesn't show it. Write a low speed, then run the fan job (4) or any job and listen. Put it back to 100% if you don't hear a change.",
                            Field("Fan speed (%)", _speed), _writeSpeed),
                        Explained("3. One fan command now",
                            "M9064 is the command xTool uses to set a fan's gear (0 off, 1 slowest, 4 fastest). Jobs send it automatically; manual holds the gear. The ports pass it on to xTool's add-on duct fans. If the fan is quiet between jobs, a manual gear may be what turns it on.",
                            Field("Send to", _target), Field("Kind", _control), Field("Gear", _gear), _line, _send),
                        Explained("4. Laser-off fan job",
                            "Loads a job that only runs the fan: gear 1, 2, 3 and 4 in turn, each for the time below, sent to the machine and both duct fan ports. Nothing is marked and the head doesn't move. Press the machine's button to start it, and listen: if the fan steps up four times, the job's fan gear works; if it's at full speed throughout, the machine ignores it. After the job, the fan keeps going for the exhaust run-on time (Device settings).",
                            Field("Gears", _jobControl), Field("Seconds per gear", _stepSeconds), _sendJob),
                        new TextBlock { Text = "LOG", Classes = { "h" } },
                        _log,
                        close,
                    },
                },
            },
        };
        _host.MachineChanged += ShowConnected;
        Opened += (_, _) => ShowConnected();
        Closed += (_, _) => _host.MachineChanged -= ShowConnected;
        ShowLine();
        ShowConnected();
    }

    DeviceClient? Client => _host.IsConnected ? _host.MachinePanel.Client : null;

    void ShowConnected()
    {
        bool on = _host.IsConnected;
        _connected.Text = on ? "" : "Connect to the machine first (Connect at the right end of the ribbon).";
        _connected.IsVisible = !on;
        foreach (var b in new[] { _read, _writeSpeed, _send, _sendJob }) b.IsEnabled = !_busy;
    }

    FanTarget Target => (FanTarget)Math.Max(0, _target.SelectedIndex);
    FanControl Control => _control.SelectedIndex == 1 ? FanControl.Auto : FanControl.Manual;
    int GearValue => (int)(_gear.Value ?? 1);

    string CurrentLine => FanCommands.Gear(Target, Control, GearValue);

    void ShowLine() => _line.Text = CurrentLine;

    void Log(string text)
    {
        _log.Text += $"{DateTime.Now:HH:mm:ss}  {text}\n";
        _log.CaretIndex = _log.Text.Length;
    }

    /// <summary>Runs one step with the buttons off, logging any error instead of throwing.</summary>
    async Task Run(Func<DeviceClient, Task> work)
    {
        if (_busy) return;
        if (Client is not { } c) { ShowConnected(); Log("Not connected to the machine."); return; }
        _busy = true;
        ShowConnected();
        try { await work(c); }
        catch (Exception e) when (e is DeviceException or JobException or OperationCanceledException or InvalidOperationException or ArgumentException)
        {
            Log($"Failed: {e.Message}");
        }
        finally
        {
            _busy = false;
            ShowConnected();
        }
    }

    async Task ReadState() => await Run(async c =>
    {
        var fan = await c.GetPeripheralAsync("smoking_fan");
        Log($"Exhaust fan (peripheral smoking_fan): {fan.ToJsonString()}");
        var cfg = await c.GetConfigAsync(FanCommands.ConfigKeys);
        Log($"Stored fan settings: {(cfg.Count == 0 ? "none reported" : cfg.ToJsonString(Indented))}");
        bool known = cfg.ContainsKey(FanCommands.SpeedConfigKey);
        _readNote.Text = known
            ? $"The machine has a stored fan speed: {cfg[FanCommands.SpeedConfigKey]?.ToJsonString()}. Try 2."
            : "The machine didn't report a stored fan speed, so 2 probably won't work on it. Try 3 and 4.";
        if (known && cfg[FanCommands.SpeedConfigKey] is JsonValue v && v.TryGetValue<double>(out var pct))
            _speed.Value = (decimal)Math.Clamp(pct, FanCommands.MinSpeedPct, FanCommands.MaxSpeedPct);
    });

    async Task WriteSpeed()
    {
        int pct = (int)(_speed.Value ?? FanCommands.MaxSpeedPct);
        if (!await Dialogs.Confirm(this, "Store a fan speed",
            $"Store \"{FanCommands.SpeedConfigKey}\" = {pct} on the machine?\n\nThis is the stored fan speed xTool's P2S uses. The F2 Ultra may use it, ignore it, or keep it without using it. It only changes a fan setting: nothing moves and the laser stays off. Write 100 here to put it back.",
            "Store it")) return;
        await Run(async c =>
        {
            await c.SetConfigAsync(FanCommands.SpeedConfigKey, JsonValue.Create(pct));
            Log($"Stored {FanCommands.SpeedConfigKey} = {pct}.");
            var back = await c.GetConfigAsync([FanCommands.SpeedConfigKey]);
            Log(back.ContainsKey(FanCommands.SpeedConfigKey)
                ? $"Read back: {back[FanCommands.SpeedConfigKey]?.ToJsonString()}. Now run a job (or the fan job) and listen."
                : "Read back: the machine doesn't report it, so it probably didn't keep it.");
        });
    }

    async Task SendNow()
    {
        string line = CurrentLine;
        if (!FanCommands.IsFanOnly(line)) { Log($"Not sent: \"{line}\" is not a fan command."); return; }
        if (!await Dialogs.Confirm(this, "Send a fan command",
            $"Send this to the machine now?\n\n{line}\n\nIt only sets a fan's gear: nothing moves and the laser stays off. Gear 0 turns that fan off; send gear 3 or 4 to bring it back.",
            "Send it")) return;
        await Run(async _ =>
        {
            Log($"Sent: {line}");
            string reply = await _host.MachinePanel.SendCommandAsync(line);
            Log(reply.Length == 0 ? "  (no reply)" : $"  Reply: {reply}");
        });
    }

    async Task SendJob()
    {
        var control = _jobControl.SelectedIndex == 1 ? FanControl.Manual : FanControl.Auto;
        double seconds = (double)(_stepSeconds.Value ?? 15);
        var steps = Enumerable.Range(1, FanCommands.MaxGear)
            .Select(g => new FanStep(FanCommands.Everywhere(control, g), seconds, $"fan gear {g}"))
            .ToList();
        var machine = _host.Machine;
        Job job;
        string motion;
        try
        {
            job = new Job(machine, []) { Created = DateTimeOffset.Now.ToString("yyyy_MM_dd_HH_mm_ss"), FanGear = 1, PurifierSpeed = _host.AppSettings.PurifierSpeed };
            motion = job.FanTest(steps);
        }
        catch (JobException e) { Log($"Couldn't make the fan job: {e.Message}"); return; }
        double total = seconds * steps.Count;
        if (!await Dialogs.Confirm(this, "Load the fan job",
            $"Load a {TimeText.Duration(total)} laser-off job that steps the fan through gears 1 to 4 ({(control == FanControl.Auto ? "automatic, B" : "manual, A")}), {seconds:0} s each?\n\nNothing is marked and the head doesn't move. It waits for you to press the machine's button. A job already loaded but not started has to be cancelled on the machine first.",
            "Load it")) return;
        double mid = machine.FieldW / 2, midY = machine.FieldH / 2;
        byte[] xf = XfPackage.Build(new()
        {
            Motion = motion,
            Border = job.Border([new(mid - 5, midY), new(mid + 5, midY)]),
            PreviewJpeg = XfPackage.PreviewJpeg([]),
            Light = "blue",
            EstimatedSeconds = total + Job.StartS,
        });
        await Run(async _ =>
        {
            await _host.MachinePanel.UploadJobAsync(xf, null, CancellationToken.None, name: "Fan test");
            Log($"Fan job loaded: gears 1 to 4 ({(control == FanControl.Auto ? "B" : "A")}), {seconds:0} s each. Press the machine's button and listen.");
        });
    }
}
