using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using static PhotonCanvas.Remote.App.Ui;

namespace PhotonCanvas.Remote.App;

/// <summary>The designs on the computer: tap one to check it, load it, look at the bed and start.</summary>
public sealed class DesignsPage : UserControl
{
    readonly MainView _host;
    readonly Session _s;
    readonly TextBox _search = new() { Watermark = "Find a design" };
    readonly TextBlock _line = Text("", "muted");
    readonly StackPanel _list = new() { Spacing = 8 };
    IReadOnlyList<ProjectItem> _designs = [];
    bool _loading;

    public DesignsPage(MainView host, Session s)
    {
        _host = host;
        _s = s;
        _search.TextChanged += (_, _) => Draw();
        var refresh = Btn("Refresh", null, async () => await Load());
        refresh.HorizontalAlignment = HorizontalAlignment.Right;
        refresh.MinHeight = 46;
        Content = new DockPanel
        {
            Margin = new(12),
            Children =
            {
                new StackPanel
                {
                    Spacing = 8,
                    Margin = new(0, 0, 0, 8),
                    Children = { new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 8, Children = { _search, refresh.With(r => Grid.SetColumn(r, 1)) } }, _line },
                }.With(p => DockPanel.SetDock(p, Dock.Top)),
                new ScrollViewer { Content = _list },
            },
        };
    }

    /// <summary>The tab was opened: fetch the list the first time.</summary>
    public void Opened()
    {
        if (_designs.Count == 0 && !_loading) _ = Load();
    }

    async Task Load()
    {
        _loading = true;
        _line.Text = "Looking…";
        try
        {
            _designs = await _s.Remote.ProjectsAsync();
            Draw();
        }
        catch (RemoteError e) { _line.Text = e.Message; }
        _loading = false;
    }

    void Draw()
    {
        var q = (_search.Text ?? "").Trim();
        var shown = _designs.Where(d => q.Length == 0 || d.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || d.Folder.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        _line.Text = _designs.Count == 0
            ? "No designs found. Save one in PhotonCanvas, or add a folder in Remote access on the computer."
            : $"{shown.Count} design{(shown.Count == 1 ? "" : "s")}: recent ones first, then your project folders.";
        _list.Children.Clear();
        foreach (var d in shown)
        {
            var name = Text(d.Name).With(t => { t.FontWeight = FontWeight.SemiBold; t.TextWrapping = TextWrapping.NoWrap; t.TextTrimming = TextTrimming.CharacterEllipsis; });
            var info = Text($"{d.Modified.ToLocalTime():g} · {d.Folder}", "muted");
            var badge = new Border
            {
                Background = Brush("Bg2"),
                CornerRadius = new(6),
                Padding = new(7, 2),
                VerticalAlignment = VerticalAlignment.Center,
                IsVisible = d.Recent,
                Child = Text("Recent", "muted").With(t => t.FontSize = 11),
            };
            var item = new Button
            {
                Classes = { "item" },
                Content = new Grid
                {
                    ColumnDefinitions = new("*,Auto"),
                    ColumnSpacing = 8,
                    Children = { new StackPanel { Spacing = 2, Children = { name, info } }, badge.With(b => Grid.SetColumn(b, 1)) },
                },
            };
            item.Click += (_, _) => new DesignSheet(_host, _s, d).Open();
            _list.Children.Add(item);
        }
    }
}

/// <summary>
/// One design: what it would do (as Ship It shows it) and a picture of what will be marked; then
/// load it, look at the picture of the bed, and start.
/// </summary>
public sealed class DesignSheet
{
    readonly MainView _host;
    readonly Session _s;
    readonly ProjectItem _design;
    readonly Image _preview = PictureBox(260);
    readonly Border _previewBox;
    readonly SelectableTextBlock _text = new() { Text = "Preparing the job (as the desktop app's Ship It would)…", TextWrapping = TextWrapping.Wrap, FontSize = 14 };
    readonly TextBlock _error = Text("", "error");
    readonly Image _pic = PictureBox();
    readonly TextBlock _picLine = Text("", "muted");
    readonly Button _load, _again, _start;
    readonly Avalonia.Threading.DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(5) };
    Sheet? _sheet;
    bool _loaded;

    public DesignSheet(MainView host, Session s, ProjectItem design)
    {
        _host = host;
        _s = s;
        _design = design;
        _load = Btn("Load onto machine", "primary", async () => await Load());
        _again = Btn("New picture", "primary", async () => await Again());
        _start = Btn("Start job", "go", async () => await Start());
        _load.IsEnabled = false;
        _again.IsVisible = _start.IsVisible = false;
        _error.IsVisible = false;
        _preview.IsVisible = true;
        _previewBox = new Border { Background = Brushes.White, CornerRadius = new(8), Padding = new(6), Child = _preview, IsVisible = false };
        _pic.PointerReleased += (_, _) => { if (_s.LastPicture is { } p) _host.ShowPicture(p.Image, $"The bed at {p.TakenAt.ToLocalTime():T}"); };
        _tick.Tick += (_, _) => UpdateStart();
    }

    Control Body() => new StackPanel
    {
        Spacing = 10,
        Children =
        {
            _previewBox,
            _text, _error, _pic, _picLine,
            Row(_load, _again, _start),
        },
    };

    public void Open()
    {
        _sheet = _host.ShowSheet(Body(), _design.Name);
        _sheet.DetachedFromVisualTree += (_, _) => _tick.Stop();
        _tick.Start();
        _ = Check();
    }

    async Task Check()
    {
        try
        {
            var c = await _s.Remote.CheckAsync(_design.Id);
            _text.Text = c.Text;
            if (c.Refusal is { } why) Error($"Can't send: {why}");
            _load.IsEnabled = c.Refusal is null;
            var png = await _s.Remote.ImageAsync(c.Preview, 900);
            _preview.Source = Picture(png);
            _previewBox.IsVisible = _preview.Source is not null;
        }
        catch (RemoteError e)
        {
            _text.Text = "";
            Error(e.Message);
        }
    }

    async Task Load()
    {
        _load.IsEnabled = false;
        _load.Content = "Loading…";
        try
        {
            var r = await _s.Remote.LoadAsync(_design.Id);
            _loaded = true;
            _text.Text = r.Text;
            _previewBox.IsVisible = false;
            _load.IsVisible = false;
            if (r.Picture is { } p)
                _s.LastPicture = new ShownPicture(await _s.Remote.ImageAsync(p.Url, 1280), p.Id, p.TakenAt, p.Blanked, r.TaskId);
            _again.IsVisible = _start.IsVisible = true;
            ShowPicture();
        }
        catch (RemoteError e)
        {
            Error(e.Message);
            _load.IsEnabled = true;
        }
        _load.Content = "Load onto machine";
    }

    async Task Again()
    {
        _again.IsEnabled = false;
        try
        {
            await _s.TakePictureAsync("overview", 1280);
            ShowPicture();
        }
        catch (RemoteError e) { Error(e.Message); }
        _again.IsEnabled = true;
    }

    void ShowPicture()
    {
        if (_s.LastPicture is not { } p) return;
        _pic.Source = Picture(p.Image);
        _pic.IsVisible = true;
        _picLine.Text = $"The bed at {p.TakenAt.ToLocalTime():T} · tap to enlarge";
        UpdateStart();
    }

    void UpdateStart()
    {
        if (!_loaded) return;
        // The machine reports the job as loaded a moment after the load returns.
        var job = _s.Status?.Job ?? null;
        bool ok = _s.LastPicture is { } p && (p.StartsJob(job, DateTimeOffset.Now) || job is null && p.Id is not null && DateTimeOffset.Now - p.TakenAt < TimeSpan.FromSeconds(115));
        _start.IsEnabled = ok;
        if (!ok && _s.LastPicture is { } old && DateTimeOffset.Now - old.TakenAt >= TimeSpan.FromSeconds(115)) _picLine.Text = "That picture is too old to start on: take a new one.";
    }

    async Task Start()
    {
        if (_s.LastPicture?.Id is not { } pic) return;
        if (!await _host.Confirm($"Start \"{_design.Name}\" now?", "The laser will fire. You looked at the bed a moment ago: the material is in place and nothing is in the way?", "Start job", "go")) return;
        _start.IsEnabled = false;
        try
        {
            await _s.Remote.StartAsync(pic);
            _host.Toast("Started.");
            _sheet?.Close();
        }
        catch (RemoteError e)
        {
            Error(e.Message);
            UpdateStart();
        }
    }

    void Error(string text)
    {
        _error.Text = text;
        _error.IsVisible = text.Length > 0;
    }
}
