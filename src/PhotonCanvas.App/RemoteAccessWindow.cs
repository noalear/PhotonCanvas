using System.Diagnostics;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Microsoft.Win32;
using PhotonCanvas.Doc;
using static PhotonCanvas.App.UiKit;

namespace PhotonCanvas.App;

/// <summary>
/// The remote server (PhotonCanvas.Server): a separate program, so the phone keeps working when
/// this one is closed. Found beside this program (server folder) or, running from source, in its
/// build output.
/// </summary>
public static class RemoteServer
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string RunName = "PhotonCanvas Server";
    const string ProcessName = "PhotonCanvas.Server";
    static string ExeName => OperatingSystem.IsWindows() ? ProcessName + ".exe" : ProcessName;

    public static string? Find()
    {
        var beside = Path.Combine(AppContext.BaseDirectory, "server", ExeName);
        if (File.Exists(beside)) return beside;
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            var bin = Path.Combine(d.FullName, "src", "PhotonCanvas.Server", "bin");
            if (Directory.Exists(bin))
                return Directory.EnumerateFiles(bin, ExeName, SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
        }
        return null;
    }

    /// <summary>Does a server answer on this computer, with this access code?</summary>
    public static async Task<bool> IsRunningAsync(RemoteConfig c)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(1.5) };
            using var req = new HttpRequestMessage(HttpMethod.Get, $"http://localhost:{c.Port}/api/info");
            req.Headers.Authorization = new("Bearer", c.AccessCode);
            using var res = await http.SendAsync(req);
            return res.IsSuccessStatusCode;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException) { return false; }
    }

    /// <summary>Start the server (it carries on after this program closes). Null, or why it couldn't.</summary>
    public static string? Start()
    {
        if (Find() is not { } exe) return "The server program wasn't found. It belongs in the server folder beside PhotonCanvas.";
        try
        {
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(exe)! })?.Dispose();
            return null;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            return e.Message;
        }
    }

    /// <summary>Stop every running server on this computer.</summary>
    public static void Stop()
    {
        foreach (var p in Process.GetProcessesByName(ProcessName))
        {
            try
            {
                p.Kill();
                p.WaitForExit(3000);
            }
            catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            finally { p.Dispose(); }
        }
    }

    /// <summary>Is the server started when Windows starts (a Run entry for this user)?</summary>
    public static bool StartsWithWindows
    {
        get
        {
            if (!OperatingSystem.IsWindows()) return false;
            using var k = Registry.CurrentUser.OpenSubKey(RunKey);
            return k?.GetValue(RunName) is string;
        }
    }

    public static void SetStartsWithWindows(bool on)
    {
        if (!OperatingSystem.IsWindows()) return;
        using var k = Registry.CurrentUser.CreateSubKey(RunKey);
        if (on && Find() is { } exe) k.SetValue(RunName, $"\"{exe}\"");
        else k.DeleteValue(RunName, false);
    }
}

/// <summary>
/// Remote access: start the server, connect a phone (QR code or address and access code), choose
/// the folders whose designs the phone lists. The phone page (and the Android app) talk to the server.
/// </summary>
public sealed class RemoteAccessWindow : Window
{
    readonly RemoteConfig _config = RemoteConfig.LoadOrCreate();
    readonly TextBlock _state = new() { FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
    readonly Button _start = Button("Start server", "IcSend", "Start the server. It keeps running when PhotonCanvas is closed.");
    readonly Button _stop = Button("Stop server", "IcStop", "Stop the server: phones can't connect until it's started again.");
    readonly CheckBox _withWindows = new() { Content = "Start the server when Windows starts" };
    readonly ComboBox _address = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly Image _qr = new() { Width = 200, Height = 200, Stretch = Stretch.Uniform };
    readonly TextBlock _url = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
    readonly TextBox _code = new() { IsReadOnly = true, FontFamily = new FontFamily("Consolas, Menlo, monospace"), FontSize = 20, HorizontalContentAlignment = HorizontalAlignment.Center };
    readonly ListBox _folders = new() { MinHeight = 60, MaxHeight = 120 };
    readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromSeconds(2) };
    List<(string Address, bool Tailscale)> _addresses = [];
    bool? _running;
    bool _busy;

    public RemoteAccessWindow()
    {
        Title = "Remote access";
        Width = 560;
        Height = 760;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");

        _start.Click += async (_, _) => await StartServer();
        _stop.Click += async (_, _) => { RemoteServer.Stop(); await Refresh(); };
        _withWindows.IsChecked = RemoteServer.StartsWithWindows;
        _withWindows.IsVisible = OperatingSystem.IsWindows();
        _withWindows.IsCheckedChanged += (_, _) => RemoteServer.SetStartsWithWindows(_withWindows.IsChecked == true);
        ToolTip.SetTip(_withWindows, "Then the phone works whenever this computer is on, even before you open PhotonCanvas.");
        _address.SelectionChanged += (_, _) => ShowQr();
        _code.Text = _config.AccessCode;

        var newCode = Button("New code", null, "Make a new access code. Phones that used the old one must scan the new QR code (or type the new code).");
        newCode.Click += async (_, _) => await NewCode();
        var addFolder = Button("Add folder…", "IcOpen");
        addFolder.Click += async (_, _) => await AddFolder();
        var removeFolder = Button("Remove", "IcTrash");
        removeFolder.Click += async (_, _) => await RemoveFolder();
        var done = new Button { Content = "Done", Classes = { "primary" }, IsDefault = true, MinWidth = 100, HorizontalAlignment = HorizontalAlignment.Right };
        done.Click += (_, _) => Close();

        Content = new DockPanel
        {
            Children =
            {
                new Border { [DockPanel.DockProperty] = Dock.Bottom, Padding = new(16, 10), Child = done },
                new ScrollViewer
                {
                    Content = new StackPanel
                    {
                        Margin = new(16),
                        Spacing = 12,
                        Children =
                        {
                            new TextBlock
                            {
                                Text = "Watch and run the laser from your phone: its state, the camera, pause and stop, your designs, and starting a job. The phone talks to this computer, which must stay on (PhotonCanvas itself can be closed).",
                                TextWrapping = TextWrapping.Wrap,
                            },
                            Card("Server", _state, Row(_start, _stop), _withWindows,
                                Hint("The first time it starts, Windows may ask whether PhotonCanvas Server can use the network. Allow it on private networks, or the phone can't reach it.")),
                            Card("Connect your phone",
                                Labelled("Address", _address),
                                // A white margin of about four modules round the code, as scanners expect.
                                new Border { Background = Brushes.White, Padding = new(24), CornerRadius = new(8), HorizontalAlignment = HorizontalAlignment.Center, Child = _qr },
                                Hint("Scan the code with the phone's camera: the page opens and connects. Or open the address in the phone's browser and type the access code. Add the page to the home screen to open it like an app."),
                                _url,
                                Labelled("Access code", _code),
                                Row(newCode, new Panel())),
                            Card("Designs the phone can load",
                                Hint("Your recent designs in PhotonCanvas, and the designs in these folders (and the folders inside them). With no folders, Documents is searched."),
                                _folders,
                                Row(addFolder, removeFolder)),
                            Card("Safety",
                                Hint("Starting a job from the phone fires the laser, with nobody at the machine unless you are. The phone only starts a job after a picture of the bed less than 2 minutes old, taken with that job loaded, so look at it properly: material in place, nothing in the way, the lid as you want it."),
                                Hint("The machine blanks its cameras while a job runs, so you can't watch the job itself. Its flame alarm still works, and the phone shows the machine's alarms. Stop works from the phone at any time.")),
                            Card("Away from home",
                                Hint("The phone reaches this computer on your home Wi-Fi. To use it from anywhere, install Tailscale (free) on this computer and on the phone, sign in to both, and use the Tailscale address above. Don't open a port on your router for it.")),
                        },
                    },
                },
            },
        };

        ShowFolders();
        ShowAddresses();
        _poll.Tick += async (_, _) => await Refresh();
        Opened += async (_, _) => { await Refresh(); _poll.Start(); };
        Closed += (_, _) => _poll.Stop();
    }

    async Task Refresh()
    {
        if (_busy) return;
        bool running = await RemoteServer.IsRunningAsync(_config);
        if (running != _running) ShowAddresses();
        _running = running;
        bool found = RemoteServer.Find() is not null;
        _state.Text = running ? "Running: phones can connect."
            : found ? "Not running: start it to use the phone."
            : "The server program wasn't found (it belongs in the server folder beside PhotonCanvas).";
        _state.Foreground = running ? Brush("Ok") : Brush("TextMain");
        _start.IsEnabled = !running && found;
        _stop.IsEnabled = running;
    }

    async Task StartServer()
    {
        _busy = true;
        _state.Text = "Starting…";
        _start.IsEnabled = false;
        if (RemoteServer.Start() is { } error)
        {
            _busy = false;
            await Dialogs.Message(this, "Couldn't start the server", error);
            await Refresh();
            return;
        }
        // It takes a moment to start listening.
        for (int i = 0; i < 20 && !await RemoteServer.IsRunningAsync(_config); i++) await Task.Delay(500);
        _busy = false;
        await Refresh();
        if (_running != true)
            await Dialogs.Message(this, "The server didn't start", $"It may already be running with other settings, or port {_config.Port} is taken. Its log is in the logs folder of {PhotonCanvas.Core.AppIdentity.DataDir}.");
    }

    /// <summary>Settings changed: a running server picks them up when it starts again.</summary>
    async Task Restart()
    {
        if (_running != true) return;
        _busy = true;
        RemoteServer.Stop();
        RemoteServer.Start();
        for (int i = 0; i < 20 && !await RemoteServer.IsRunningAsync(_config); i++) await Task.Delay(500);
        _busy = false;
        await Refresh();
    }

    async Task NewCode()
    {
        if (!await Dialogs.Confirm(this, "Make a new access code?", "Phones that used the old code must scan the new QR code, or type the new code.", "New code", "Cancel")) return;
        _config.AccessCode = RemoteConfig.NewAccessCode();
        _config.Save();
        _code.Text = _config.AccessCode;
        ShowQr();
        await Restart();
    }

    async Task AddFolder()
    {
        var picked = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Folder with designs for the phone" });
        if (picked.Count == 0 || picked[0].TryGetLocalPath() is not { } path) return;
        if (_config.ProjectFolders.Contains(path, StringComparer.OrdinalIgnoreCase)) return;
        _config.ProjectFolders.Add(path);
        _config.Save();
        ShowFolders();
        await Restart();
    }

    async Task RemoveFolder()
    {
        if (_folders.SelectedItem is not string path) return;
        _config.ProjectFolders.Remove(path);
        _config.Save();
        ShowFolders();
        await Restart();
    }

    void ShowFolders() => _folders.ItemsSource = _config.ProjectFolders.ToList();

    void ShowAddresses()
    {
        _addresses = RemoteConfig.Addresses();
        int keep = Math.Max(0, _address.SelectedIndex);
        _address.ItemsSource = _addresses.Count == 0
            ? ["No network found"]
            : _addresses.Select(a => a.Tailscale ? $"{a.Address}  (Tailscale: from anywhere)" : $"{a.Address}  (home network)").ToList();
        _address.SelectedIndex = Math.Min(keep, Math.Max(0, (_address.ItemsSource as System.Collections.IList)!.Count - 1));
        ShowQr();
    }

    /// <summary>The address and the code as a QR code: scanning opens the page already connected (the code follows #, so it never reaches the server's log).</summary>
    void ShowQr()
    {
        if (_address.SelectedIndex < 0 || _address.SelectedIndex >= _addresses.Count)
        {
            _qr.Source = null;
            _url.Text = "Connect this computer to your network first.";
            return;
        }
        string site = $"http://{_addresses[_address.SelectedIndex].Address}:{_config.Port}";
        _url.Text = $"Address: {site}";
        var (cols, rows, dark) = CodeShape.Encode(CodeKind.QR, $"{site}/#code={_config.AccessCode}");
        var sb = new StringBuilder();
        for (int y = 0; y < rows; y++)
            for (int x = 0; x < cols; x++)
                if (dark[y * cols + x]) sb.Append($"M{x},{y}h1v1h-1z");
        _qr.Source = new DrawingImage(new GeometryDrawing { Geometry = Geometry.Parse(sb.ToString()), Brush = Brushes.Black });
    }
}
