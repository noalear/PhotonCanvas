using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using static PhotonCanvas.Remote.App.Ui;

namespace PhotonCanvas.Remote.App;

/// <summary>
/// The whole app: a header saying what the machine is doing, the Machine, Designs and History
/// pages with tabs along the bottom, and on top of them alarms, messages, questions and sheets.
/// Without a saved computer it starts on the connect page.
/// </summary>
public sealed class MainView : UserControl
{
    readonly IPhone _phone;
    readonly ConnectionStore _store;
    readonly Ellipse _dot = new() { Width = 10, Height = 10, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock _title = new() { FontSize = 17, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap };
    readonly TextBlock _subtitle = Text("", "muted");
    readonly Button _menu = new() { Content = "⋯", Width = 48, MinHeight = 40, Background = Brushes.Transparent, FontSize = 20, HorizontalAlignment = HorizontalAlignment.Right };
    readonly Border _header;
    readonly ContentControl _page = new();
    readonly Grid _nav = new() { ColumnDefinitions = new("*,*,*") };
    readonly Border _navBar;
    readonly Border _alert;
    readonly TextBlock _alertText = new() { Foreground = Brushes.White, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
    readonly Border _toast;
    readonly TextBlock _toastText = new();
    readonly Panel _overlays = new();
    readonly Grid _root = new() { RowDefinitions = new("Auto,*,Auto") };
    readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(4.5) };
    Session? _session;
    MachinePage? _machinePage;
    DesignsPage? _designsPage;
    HistoryPage? _historyPage;
    string _current = "";

    public MainView(IPhone phone)
    {
        _phone = phone;
        _store = new ConnectionStore(phone.DataFolder);
        Background = Brush("Bg0");

        _menu.Click += (_, _) => ShowMenu();
        _header = new Border
        {
            Padding = new(16, 10, 8, 8),
            BorderBrush = Brush("Line"),
            BorderThickness = new(0, 0, 0, 1),
            Child = new Grid
            {
                ColumnDefinitions = new("*,Auto"),
                Children =
                {
                    new StackPanel
                    {
                        Spacing = 2,
                        Children =
                        {
                            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _dot, _title } },
                            _subtitle,
                        },
                    },
                    _menu.With(m => Grid.SetColumn(m, 1)),
                },
            },
        };
        foreach (var (name, i) in new[] { ("Machine", 0), ("Designs", 1), ("History", 2) })
        {
            var b = Btn(name, "nav", () => Show(name));
            b.Tag = name;
            Grid.SetColumn(b, i);
            _nav.Children.Add(b);
        }
        _navBar = new Border { Background = Brush("Bg1"), BorderBrush = Brush("Line"), BorderThickness = new(0, 1, 0, 0), Child = _nav };

        _alert = new Border
        {
            Background = Brush("Danger"),
            CornerRadius = new(10),
            Padding = new(14, 10),
            Margin = new(12, 8),
            VerticalAlignment = VerticalAlignment.Top,
            IsVisible = false,
            Child = new Grid
            {
                ColumnDefinitions = new("*,Auto"),
                ColumnSpacing = 8,
                Children = { _alertText, Btn("OK", null, () => _alert!.IsVisible = false).With(b => { Grid.SetColumn(b, 1); b.MinHeight = 36; b.Background = new SolidColorBrush(Color.FromArgb(60, 0, 0, 0)); b.Foreground = Brushes.White; }) },
            },
        };
        _toast = new Border
        {
            Background = Brush("Bg2"),
            BorderBrush = Brush("Line"),
            BorderThickness = new(1),
            CornerRadius = new(10),
            Padding = new(14, 12),
            Margin = new(16, 0, 16, 80),
            VerticalAlignment = VerticalAlignment.Bottom,
            IsVisible = false,
            Child = _toastText,
        };
        _toastTimer.Tick += (_, _) => { _toast.IsVisible = false; _toastTimer.Stop(); };

        _root.Children.Add(_header);
        _root.Children.Add(_page.With(p => Grid.SetRow(p, 1)));
        _root.Children.Add(_navBar.With(n => Grid.SetRow(n, 2)));
        Content = new Panel { Children = { _root, _alert, _toast, _overlays } };

        if (_store.Load() is { } saved) Connect(new HttpRemote(saved));
        else ShowConnect();
        if (CrashLog.TakeLast(phone.DataFolder) is { } report)
            Dispatcher.UIThread.Post(() => ShowReport("The app closed unexpectedly last time", report));
    }

    /// <summary>What went wrong, with a button to copy it (to send to whoever looks after the app).</summary>
    public void ShowReport(string title, string details)
    {
        var text = new SelectableTextBlock { Text = details, FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = Brush("TextMuted") };
        var copy = Btn("Copy details", "primary");
        copy.Click += async (_, _) =>
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is { } clip) await clip.SetTextAsync(details);
            Toast("Copied.");
        };
        ShowSheet(new StackPanel { Spacing = 10, Children = { Text("The app kept going, but this went wrong. Copy the details and send them along.", "muted"), copy, text } }, title);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (TopLevel.GetTopLevel(this) is not { } top) return;
        // Android: the system's back gesture closes what's on top first.
        top.BackRequested += (_, args) => { if (CloseTop()) args.Handled = true; };
        // Keep clear of the status bar, the camera cut-out and the gesture bar.
        if (top.InsetsManager is { } insets)
        {
            Padding = insets.SafeAreaPadding;
            insets.SafeAreaChanged += (_, a) => Padding = a.SafeAreaPadding;
        }
    }

    // ---- connecting ----

    public void ShowConnect(string? error = null)
    {
        _session?.Dispose();
        _session = null;
        _machinePage = null;
        _designsPage = null;
        _historyPage = null;
        _overlays.Children.Clear();
        _alert.IsVisible = false;
        _header.IsVisible = false;
        _navBar.IsVisible = false;
        _page.Content = new ConnectPage(this, _phone, _store.Load(), error);
        _current = "";
    }

    /// <summary>Use this server (or the demo) from now on.</summary>
    public void Connect(IRemote remote)
    {
        _session?.Dispose();
        var s = _session = new Session(remote);
        s.Changed += () => UpdateHeader();
        s.CodeRefused += message => { _store.Forget(); ShowConnect(message); };
        s.JobEvent += OnJob;
        s.Alarm += a => ShowAlarm(a.Text);
        _machinePage = new MachinePage(this, s);
        _designsPage = new DesignsPage(this, s);
        _historyPage = new HistoryPage(this, s);
        _header.IsVisible = true;
        _navBar.IsVisible = true;
        UpdateHeader();
        Show("Machine");
    }

    /// <summary>Remember this computer for next time.</summary>
    public void Remember(ConnectionInfo c) => _store.Save(c);

    void UpdateHeader()
    {
        if (_session is not { } s) return;
        var st = s.Status;
        bool ok = s.Live && st is { Connected: true };
        _dot.Fill = !s.Live || st is not { Connected: true } ? Brush("Danger") : st.Mode == "P_WORKING" ? Brush("Warn") : Brush("Ok");
        _title.Text = st?.Name is { Length: > 0 } n ? n : s.Remote.IsDemo ? "Demo machine" : "PhotonCanvas Remote";
        _subtitle.Text = !s.Live ? (s.Problem ?? $"Connecting to {s.Remote.Name}…") : ok ? st!.State : "The computer isn't connected to the machine";
        _machinePage?.Update();
    }

    void Show(string page)
    {
        _current = page;
        foreach (var b in _nav.Children.OfType<Button>()) b.Classes.Set("on", (string?)b.Tag == page);
        _page.Content = page switch
        {
            "Designs" => _designsPage,
            "History" => _historyPage,
            _ => _machinePage,
        };
        if (page == "Designs") _designsPage?.Opened();
        if (page == "History") _historyPage?.Opened();
    }

    void OnJob(JobEvent e)
    {
        if (e.Kind is "finished" or "stopped")
        {
            Toast(e.Text ?? $"\"{e.Design}\" {e.Kind}.");
            _phone.Vibrate(false);
        }
        else if (e.Kind == "started") Toast($"\"{e.Design}\" started.");
        if (e.Kind == "picture" && _current == "History") _historyPage?.Opened();
    }

    public void ShowAlarm(string text)
    {
        _alertText.Text = text;
        _alert.IsVisible = true;
        _phone.Vibrate(true);
    }

    public void Toast(string text)
    {
        _toastText.Text = text;
        _toast.IsVisible = true;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    // ---- things on top ----

    /// <summary>A sheet rising from the bottom (a design, a menu); Close() or the back gesture closes it.</summary>
    public Sheet ShowSheet(Control content, string title)
    {
        var sheet = new Sheet(this, content, title);
        _overlays.Children.Add(sheet);
        return sheet;
    }

    public void Close(Control overlay) => _overlays.Children.Remove(overlay);

    bool CloseTop()
    {
        if (_overlays.Children.Count == 0) return false;
        if (_overlays.Children[^1] is Question q) q.Answer(false);
        else _overlays.Children.RemoveAt(_overlays.Children.Count - 1);
        return true;
    }

    /// <summary>Ask before doing something that can't be undone. True for <paramref name="yes"/>.</summary>
    public Task<bool> Confirm(string title, string message, string yes, string yesClass = "primary")
    {
        var q = new Question(this, title, message, yes, yesClass);
        _overlays.Children.Add(q);
        return q.Result;
    }

    /// <summary>A picture full screen; a tap closes it.</summary>
    public void ShowPicture(byte[] image, string caption)
    {
        var view = new Border
        {
            Background = Brushes.Black,
            Child = new Grid
            {
                RowDefinitions = new("*,Auto"),
                Children =
                {
                    new Image { Source = Picture(image), Stretch = Stretch.Uniform },
                    Text(caption, "muted").With(t => { Grid.SetRow(t, 1); t.Margin = new(16); t.HorizontalAlignment = HorizontalAlignment.Center; }),
                },
            },
        };
        view.PointerReleased += (_, _) => Close(view);
        _overlays.Children.Add(view);
    }

    void ShowMenu()
    {
        Sheet? sheet = null;
        var list = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                Text(_session is { } s ? $"Connected to {s.Remote.Name}." : "", "muted"),
                Btn("Change computer", null, () => { sheet!.Close(); _store.Forget(); ShowConnect(); }),
                Btn("About and privacy", null, () => { sheet!.Close(); ShowSheet(AboutPage.Build(_phone), "About"); }),
            },
        };
        sheet = ShowSheet(list, "PhotonCanvas Remote");
    }
}

/// <summary>A panel rising from the bottom over the page, with a title and a Close button.</summary>
public sealed class Sheet : Border
{
    readonly MainView _host;

    public Sheet(MainView host, Control content, string title)
    {
        _host = host;
        Background = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0));
        var close = Btn("Close", null, Close);
        close.HorizontalAlignment = HorizontalAlignment.Right;
        close.MinWidth = 90;
        close.MinHeight = 40;
        Child = new Border
        {
            Background = Brush("Bg0"),
            CornerRadius = new(16, 16, 0, 0),
            VerticalAlignment = VerticalAlignment.Bottom,
            MaxHeight = 4000,
            Padding = new(16, 12, 16, 16),
            Child = new DockPanel
            {
                Children =
                {
                    new Grid
                    {
                        ColumnDefinitions = new("*,Auto"),
                        Margin = new(0, 0, 0, 10),
                        Children = { Text(title).With(t => { t.FontSize = 19; t.FontWeight = FontWeight.SemiBold; t.VerticalAlignment = VerticalAlignment.Center; }), close.With(c => Grid.SetColumn(c, 1)) },
                    }.With(g => DockPanel.SetDock(g, Dock.Top)),
                    new ScrollViewer { Content = content },
                },
            },
        };
        // Tapping the dimmed part above closes it.
        PointerReleased += (_, e) => { if (e.Source == this) Close(); };
    }

    public void Close() => _host.Close(this);
}

/// <summary>A question in the middle of the screen: Cancel or the action.</summary>
public sealed class Question : Border
{
    readonly MainView _host;
    readonly TaskCompletionSource<bool> _result = new();

    public Question(MainView host, string title, string message, string yes, string yesClass)
    {
        _host = host;
        Background = new SolidColorBrush(Color.FromArgb(160, 0, 0, 0));
        Child = new Border
        {
            Classes = { "card" },
            Margin = new(20),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    Text(title).With(t => { t.FontSize = 18; t.FontWeight = FontWeight.SemiBold; }),
                    Text(message),
                    Row(Btn("Cancel", null, () => Answer(false)), Btn(yes, yesClass, () => Answer(true))),
                },
            },
        };
    }

    public Task<bool> Result => _result.Task;

    public void Answer(bool yes)
    {
        _host.Close(this);
        _result.TrySetResult(yes);
    }
}

static class ControlExtensions
{
    /// <summary>Set something up inline and hand it back (for building in one expression).</summary>
    public static T With<T>(this T o, Action<T> setup)
    {
        setup(o);
        return o;
    }
}
