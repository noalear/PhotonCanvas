using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using static PhotonCanvas.App.UiKit;

namespace PhotonCanvas.App;

/// <summary>
/// Settings → Machines: the lasers the user has. The F2 Ultra is built in; GRBL machines (most
/// hobby lasers, and others after a controller swap) can be added and described. The one in use
/// sets the work area and how jobs are sent.
/// </summary>
public sealed class MachinesPanel : UserControl
{
    readonly AppSettings _settings;
    readonly StackPanel _list = new() { Spacing = 6 };
    readonly StackPanel _editor = new() { Spacing = 8 };
    Guid _editing;

    /// <summary>The machine in use, or one of them, changed.</summary>
    public event Action? Changed;

    public MachinesPanel(AppSettings settings)
    {
        _settings = settings;
        var add = new Button { Content = "Add a GRBL machine" };
        add.Click += (_, _) => Add(new MachineSetup { Name = "My laser (GRBL)" });
        var fsl = new Button { Content = "Add a Full Spectrum Hobby 20×12" };
        ToolTip.SetTip(fsl, "A starting point for a Full Spectrum Laser 5th Gen Hobby 20×12 (45 W CO2) after its controller is swapped for a GRBL board: its bed and a CO2's speeds. Check the origin and power scale against the new controller.");
        fsl.Click += (_, _) => Add(MachineSetup.FullSpectrumHobby);
        Content = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                new TextBlock { Text = "MACHINES", Classes = { "h" } },
                Hint("The machine in use sets the work area and how jobs go to it: the F2 Ultra over Wi-Fi, a GRBL machine over USB (Ship It opens the GRBL sender)."),
                _list,
                Row(add, fsl),
                _editor,
            },
        };
        Fill();
    }

    void Add(MachineSetup m)
    {
        _settings.Machines.Add(m);
        _settings.Save();
        _editing = m.Id;
        Fill();
    }

    void Fill()
    {
        _list.Children.Clear();
        var active = _settings.ActiveSetup();
        foreach (var m in new[] { MachineSetup.F2Ultra }.Concat(_settings.Machines))
        {
            var use = new RadioButton { GroupName = "machine", IsChecked = m.Id == active.Id, VerticalAlignment = VerticalAlignment.Center };
            var id = m.Id;
            use.IsCheckedChanged += (_, _) =>
            {
                if (use.IsChecked != true || _settings.ActiveMachine == id) return;
                _settings.ActiveMachine = id == MachineSetup.F2Ultra.Id ? null : id;
                _settings.Save();
                Changed?.Invoke();
            };
            ToolTip.SetTip(use, "Use this machine");
            var edit = new Button { Content = "Edit", IsVisible = m.Kind == MachineKind.Grbl };
            edit.Click += (_, _) => { _editing = id; Fill(); };
            var row = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(use, Dock.Left);
            DockPanel.SetDock(edit, Dock.Right);
            row.Children.Add(use);
            row.Children.Add(edit);
            row.Children.Add(new StackPanel
            {
                Margin = new(6, 0), VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    new TextBlock { Text = m.Name, FontWeight = FontWeight.SemiBold },
                    new TextBlock { Text = m.Kind == MachineKind.XTool ? "Built in · over Wi-Fi · 220 × 220 mm" : $"GRBL over USB{(m.Port.Length > 0 ? $" ({m.Port})" : "")} · {m.Laser} {m.PowerW:0.#} W · {m.BedW:0.#} × {m.BedH:0.#} mm", Classes = { "muted" }, FontSize = 12 },
                },
            });
            _list.Children.Add(new Border { Background = Brush("Bg2"), CornerRadius = new(8), Padding = new(8, 6), Child = row });
        }
        Editor();
    }

    void Editor()
    {
        _editor.Children.Clear();
        int at = _settings.Machines.FindIndex(m => m.Id == _editing);
        if (at < 0) return;
        var m = _settings.Machines[at];
        void Set(Func<MachineSetup, MachineSetup> change)
        {
            int i = _settings.Machines.FindIndex(x => x.Id == _editing);
            if (i < 0) return;
            _settings.Machines[i] = change(_settings.Machines[i]);
            _settings.Save();
            if (_settings.ActiveMachine == _editing) Changed?.Invoke();
        }
        NumericUpDown Num(double value, double min, double max, double step, string fmt, Func<MachineSetup, double, MachineSetup> apply)
        {
            var n = Number((decimal)min, (decimal)max, (decimal)step, fmt);
            n.Value = (decimal)Math.Clamp(value, min, max);
            n.ValueChanged += (_, _) => { if (n.Value is { } v) Set(x => apply(x, (double)v)); };
            return n;
        }
        var name = new TextBox { Text = m.Name };
        name.LostFocus += (_, _) => { Set(x => x with { Name = string.IsNullOrWhiteSpace(name.Text) ? x.Name : name.Text.Trim() }); Fill(); };
        var laser = new ComboBox { ItemsSource = new[] { "CO2", "Diode", "Fiber" }, SelectedItem = m.Laser, HorizontalAlignment = HorizontalAlignment.Stretch };
        laser.SelectionChanged += (_, _) => { if (laser.SelectedItem is string l) Set(x => x with { Laser = l }); };
        var port = new ComboBox { ItemsSource = GrblLink.Ports().Prepend(m.Port).Where(p => p.Length > 0).Distinct().ToList(), SelectedItem = m.Port.Length > 0 ? m.Port : null, PlaceholderText = "Plug the machine in, then pick its port", HorizontalAlignment = HorizontalAlignment.Stretch };
        port.SelectionChanged += (_, _) => { if (port.SelectedItem is string p) Set(x => x with { Port = p }); };
        var origin = new ComboBox
        {
            ItemsSource = new[] { "Front left, Y going back (the usual)", "Back left, Y going forward as negative numbers", "Back left, Y going forward as positive numbers" },
            SelectedIndex = (int)m.Origin, HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        origin.SelectionChanged += (_, _) => Set(x => x with { Origin = (GrblOrigin)Math.Max(0, origin.SelectedIndex) });
        var air = new CheckBox { Content = "Air assist on while marking (M8)", IsChecked = m.AirAssist };
        air.IsCheckedChanged += (_, _) => Set(x => x with { AirAssist = air.IsChecked == true });
        var delete = new Button { Content = "Delete this machine" };
        delete.Click += (_, _) =>
        {
            _settings.Machines.RemoveAll(x => x.Id == _editing);
            if (_settings.ActiveMachine == _editing) { _settings.ActiveMachine = null; Changed?.Invoke(); }
            _settings.Save();
            _editing = Guid.Empty;
            Fill();
        };
        _editor.Children.Add(new TextBlock { Text = $"EDIT: {m.Name.ToUpperInvariant()}", Classes = { "h" }, Margin = new(0, 8, 0, 0) });
        _editor.Children.Add(Field("Name", name));
        _editor.Children.Add(Field("Laser", laser));
        _editor.Children.Add(Field("Laser power (W)", Num(m.PowerW, 1, 500, 1, "0.#", (x, v) => x with { PowerW = v })));
        _editor.Children.Add(Field("Bed width (mm)", Num(m.BedW, 10, 3000, 1, "0.#", (x, v) => x with { BedW = v }), tip: "Left to right: the work area the head can reach."));
        _editor.Children.Add(Field("Bed depth (mm)", Num(m.BedH, 10, 3000, 1, "0.#", (x, v) => x with { BedH = v }), tip: "Front to back."));
        _editor.Children.Add(Field("Fastest (mm/s)", Num(m.MaxSpeed, 1, 3000, 10, "0", (x, v) => x with { MaxSpeed = v }), tip: "The fastest it moves while marking (GRBL's $110/$111, in mm/s rather than mm/min). Faster settings are refused."));
        _editor.Children.Add(Field("Full power is S", Num(m.SMax, 1, 100000, 1, "0", (x, v) => x with { SMax = (int)v }), tip: "GRBL's $30: the S for 100% (usually 1000; 255 on some boards)."));
        _editor.Children.Add(Field("Where 0, 0 is", origin, tip: "Which corner the controller counts from. Frame a small design and see where the head goes: if it's mirrored front to back, pick another."));
        _editor.Children.Add(Field("Port", port, tip: "The USB serial port the controller shows up as (COM3 on Windows, /dev/ttyUSB0 or /dev/tty.usbserial on Linux and Mac)."));
        _editor.Children.Add(Field("Speed (baud)", Num(m.Baud, 9600, 2000000, 100, "0", (x, v) => x with { Baud = (int)v }), tip: "Usually 115200."));
        _editor.Children.Add(Field("Picture overscan (mm)", Num(m.RasterOverscanMm, 0, 50, 0.5, "0.#", (x, v) => x with { RasterOverscanMm = v }), tip: "Each picture row runs this far past both ends with the laser off, so the head is at speed where it burns. More for heavy CO2 gantries."));
        _editor.Children.Add(air);
        _editor.Children.Add(Hint("GRBL must be in laser mode ($32=1): moves without S then don't fire, and power follows speed (M4) so corners don't scorch."));
        _editor.Children.Add(delete);
    }
}

/// <summary>
/// Sending a job to a GRBL machine over USB: connect, home or unlock, frame the job's outline
/// with the laser off, start, hold, resume and stop, with progress. The G-code can be saved too.
/// </summary>
public sealed class GrblWindow : Window
{
    readonly MachineSetup _machine;
    readonly List<string> _gcode;
    readonly Rect2 _bounds;
    readonly GrblLink _link = new();
    readonly ComboBox _port = new() { MinWidth = 160 };
    readonly TextBlock _state = new() { VerticalAlignment = VerticalAlignment.Center };
    readonly ProgressBar _progress = new() { Minimum = 0, Maximum = 1, Height = 8 };
    readonly TextBlock _note = new() { TextWrapping = TextWrapping.Wrap };
    readonly Button _connect = new() { Content = "Connect" }, _home = new() { Content = "Home ($H)" }, _unlock = new() { Content = "Unlock ($X)" },
        _frame = new() { Content = "Frame" }, _start = new() { Content = "Start", Classes = { "primary" } }, _hold = new() { Content = "Pause" }, _stop = new() { Content = "Stop" };
    CancellationTokenSource? _run;
    bool _held;

    public GrblWindow(MachineSetup machine, string gcode, Rect2 bounds, string title)
    {
        _machine = machine;
        _gcode = [.. gcode.Split('\n')];
        _bounds = bounds;
        Title = $"Send to {machine.Name}";
        Width = 620;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");
        _port.ItemsSource = GrblLink.Ports().Prepend(machine.Port).Where(p => p.Length > 0).Distinct().ToList();
        _port.SelectedItem = machine.Port.Length > 0 ? machine.Port : (_port.ItemsSource as List<string>)?.FirstOrDefault();
        _connect.Click += async (_, _) => await Connect();
        _home.Click += async (_, _) => await Do("Homing…", () => _link.CommandAsync("$H", TimeSpan.FromSeconds(60)));
        _unlock.Click += async (_, _) => await Do("Unlocking…", () => _link.CommandAsync("$X", TimeSpan.FromSeconds(5)));
        _frame.Click += async (_, _) => await Do("Framing (laser off)…", () => _link.StreamAsync([.. GrblWriter.Frame(_bounds, _machine).Split('\n')], null, CancellationToken.None));
        _start.Click += async (_, _) => await Start();
        _hold.Click += (_, _) =>
        {
            if (_held) _link.Resume(); else _link.Hold();
            _held = !_held;
            _hold.Content = _held ? "Resume" : "Pause";
        };
        _stop.Click += (_, _) => { _run?.Cancel(); _link.Reset(); _note.Text = "Stopped: the laser is off. Home again before the next job if the position may have been lost."; };
        var save = new Button { Content = "Save G-code…" };
        save.Click += async (_, _) => await Save(title);
        _link.StatusChanged += s => Dispatcher.UIThread.Post(() => _state.Text = $"{s.State} · X {s.X:0.0} Y {s.Y:0.0}");
        int lines = _gcode.Count(l => l.Split(';')[0].Trim().Length > 0);
        Content = new StackPanel
        {
            Spacing = 10, Margin = new(16),
            Children =
            {
                new TextBlock { Text = machine.Describe, FontWeight = FontWeight.SemiBold },
                Hint($"{title}: {lines:N0} lines of GRBL G-code, {bounds.Width:0.#} × {bounds.Height:0.#} mm."),
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { new TextBlock { Text = "Port", VerticalAlignment = VerticalAlignment.Center }, _port, _connect, _state } },
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _home, _unlock, _frame } },
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _start, _hold, _stop, save } },
                _progress,
                _note,
                Hint(machine.Laser == "CO2"
                    ? "Stay with the machine while it runs, lid closed, air assist and extraction on: a CO2 laser can set the material alight. Stop cuts the laser at once."
                    : "Stay with the machine while it runs, with eye protection for the laser's light and extraction on. Stop cuts the laser at once."),
            },
        };
        Enable();
        Closing += async (_, _) => { _run?.Cancel(); await _link.DisposeAsync(); };
    }

    void Enable()
    {
        bool on = _link.IsOpen, running = _run is not null;
        _connect.Content = on ? "Disconnect" : "Connect";
        _home.IsEnabled = _unlock.IsEnabled = _frame.IsEnabled = on && !running;
        _start.IsEnabled = on && !running;
        _hold.IsEnabled = _stop.IsEnabled = on && running;
        _port.IsEnabled = !on;
    }

    async Task Connect()
    {
        if (_link.IsOpen) { await _link.DisposeAsync(); _state.Text = ""; Enable(); return; }
        if (_port.SelectedItem is not string port) { _note.Text = "Pick the machine's port (plug it in first)."; return; }
        try
        {
            _note.Text = $"Connecting on {port}…";
            await _link.OpenAsync(port, _machine.Baud);
            _note.Text = $"Connected: {_link.Version}. Home it (or Unlock), then Frame to check where the job goes.";
        }
        catch (Exception e) when (e is GrblException or IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            _note.Text = e is UnauthorizedAccessException ? $"{port} is in use by another program (close LightBurn or a terminal)." : e.Message;
        }
        Enable();
    }

    async Task Do(string what, Func<Task> action)
    {
        _note.Text = what;
        _run = new CancellationTokenSource();
        Enable();
        try { await action(); _note.Text = what.TrimEnd('…') + ": done."; }
        catch (Exception e) when (e is GrblException or IOException or OperationCanceledException or InvalidOperationException) { _note.Text = e.Message; }
        finally { _run = null; Enable(); }
    }

    async Task Start()
    {
        int total = _gcode.Count(l => l.Split(';')[0].Trim().Length > 0);
        _run = new CancellationTokenSource();
        _held = false;
        _hold.Content = "Pause";
        Enable();
        var started = DateTime.Now;
        var progress = new Progress<int>(n =>
        {
            _progress.Value = (double)n / Math.Max(1, total);
            var gone = DateTime.Now - started;
            var left = n > 50 ? TimeSpan.FromSeconds(gone.TotalSeconds * (total - n) / n) : (TimeSpan?)null;
            _note.Text = $"Running: line {n:N0} of {total:N0}" + (left is { } l ? $" · about {Duration(l.TotalSeconds)} left" : "");
        });
        try
        {
            await _link.StreamAsync(_gcode, progress, _run.Token);
            _note.Text = $"Finished in {Duration((DateTime.Now - started).TotalSeconds)}.";
            AppLog.Write("grbl", $"Job finished on {_machine.Name} in {(DateTime.Now - started).TotalSeconds:0} s.");
        }
        catch (OperationCanceledException) { }
        catch (Exception e) when (e is GrblException or IOException or InvalidOperationException or TimeoutException)
        {
            _link.Reset();
            _note.Text = $"Stopped: {e.Message}";
            AppLog.Write("grbl", $"Job stopped on {_machine.Name}: {e.Message}");
        }
        finally { _run = null; Enable(); }
    }

    async Task Save(string title)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
        {
            Title = "Save G-code", DefaultExtension = "gcode", SuggestedFileName = $"{(title.Length > 0 ? title : "job")}.gcode",
        });
        if (file?.TryGetLocalPath() is { } path) await File.WriteAllLinesAsync(path, _gcode);
    }
}
