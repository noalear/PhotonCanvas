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
/// Checks an xTool inline fan follows the speed commands, one at a time: read what the machine
/// reports, send one fan command now, or load a laser-off job that steps the fan through its gears. Only fan
/// commands are ever sent (<see cref="FanCommands.IsFanOnly"/>), and each send is confirmed first.
/// </summary>
public sealed class FanTestWindow : Window
{
    static readonly FontFamily Mono = new("Consolas, Menlo, monospace");
    static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    readonly IAddonHost _host;

    readonly Button _read = Button("Read the fan's state", tip: "Ask the machine what it reports about the exhaust fan and its stored fan settings. Only reads.");
    readonly TextBlock _readNote = Hint();


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

        _gear.Value = 1;
        _stepSeconds.Value = 15;
        _target.SelectionChanged += (_, _) => ShowLine();
        _control.SelectionChanged += (_, _) => ShowLine();
        _gear.ValueChanged += (_, _) => ShowLine();
        _read.Click += async (_, _) => await ReadState();
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
                        Hint("Check that your xTool inline fan follows the speed commands. (The F2 Ultra's built-in fan has no speed control.) Everything here only touches the fan: the laser stays off and the head doesn't move. Each send asks first and is written in the log below, so you can tell what you heard after which command."),
                        _connected,
                        Explained("1. What the machine says",
                            "Reads the exhaust fan's state and the machine's stored fan settings (the run-on time).",
                            _read, _readNote),
                        Explained("2. One fan command now",
                            "M9064 is the command xTool uses to set a fan's gear (0 off, 1 slowest, 4 fastest). Jobs send it automatically; manual holds the gear. The ports pass it on to xTool's add-on duct fans. If the fan is quiet between jobs, a manual gear may be what turns it on.",
                            Field("Send to", _target), Field("Kind", _control), Field("Gear", _gear), _line, _send),
                        Explained("3. Laser-off fan job",
                            "Loads a job that only runs the fan: gear 1, 2, 3 and 4 in turn, each for the time below, sent to the machine and both duct fan ports. Nothing is marked and the head doesn't move. Press the machine's button to start it, and listen: if the inline fan steps up four times, the job's fan gear reaches it. After the job, the fan keeps going for the exhaust run-on time (Device settings).",
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
        foreach (var b in new[] { _read, _send, _sendJob }) b.IsEnabled = !_busy;
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
        _readNote.Text = fan["state"]?.ToString() == "install" ? "The machine reports an exhaust fan connected." : "The machine doesn't report an exhaust fan connected.";
    });

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
            job = new Job(machine, []) { Created = DateTimeOffset.Now.ToString("yyyy_MM_dd_HH_mm_ss"), FanGear = 1, PurifierSpeed = _host.AppSettings.JobPurifierSpeed };
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
