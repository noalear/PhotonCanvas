using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;
using static PhotonCanvas.App.UiKit;

namespace PhotonCanvas.App;

/// <summary>Every job run from this program: when, which design, how long.</summary>
public sealed class JobHistoryWindow : Window
{
    public JobHistoryWindow()
    {
        Title = "Job history";
        Width = 640;
        Height = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");
        var all = JobHistory.All();
        all.Reverse();
        var list = new StackPanel { Spacing = 2 };
        foreach (var r in all)
            list.Children.Add(new Grid
            {
                ColumnDefinitions = new("150,*,90,90"),
                Children =
                {
                    new TextBlock { Text = r.When.ToString("g"), Classes = { "muted" } },
                    new TextBlock { Text = $"{r.Design} · {r.Layers} layer{(r.Layers == 1 ? "" : "s")} · {r.WidthMm:0.#} × {r.HeightMm:0.#} mm", TextTrimming = TextTrimming.CharacterEllipsis, [Grid.ColumnProperty] = 1 },
                    new TextBlock { Text = Clock(r.TookS), [Grid.ColumnProperty] = 2 },
                    new TextBlock { Text = r.Finished ? "finished" : "stopped", Foreground = Brush(r.Finished ? "Ok" : "Warn"), [Grid.ColumnProperty] = 3 },
                },
            });
        double total = all.Where(r => r.Finished).Sum(r => r.TookS);
        var head = new TextBlock { Text = all.Count == 0 ? "No jobs yet." : $"{all.Count} jobs, {Duration(total)} of marking in all.", Margin = new(0, 0, 0, 8) };
        DockPanel.SetDock(head, Dock.Top);
        Content = new DockPanel { Margin = new Thickness(14), Children = { head, new ScrollViewer { Content = list } } };
    }
}

/// <summary>Earlier saved versions of the open design (kept each time it's saved over).</summary>
public sealed class VersionsWindow : Window
{
    /// <summary>The version to open as a new design, or null.</summary>
    public string? Chosen { get; private set; }

    public static string Folder(string designPath) =>
        Path.Combine(AppIdentity.DataDir, "versions", Path.GetFileNameWithoutExtension(designPath) + "-" + Fnv(designPath.ToLowerInvariant()).ToString("x8"));

    /// <summary>A hash that stays the same from run to run (string.GetHashCode doesn't).</summary>
    static uint Fnv(string s)
    {
        uint h = 2166136261;
        foreach (char c in s) h = (h ^ c) * 16777619;
        return h;
    }

    /// <summary>Keep a copy of the file before it's saved over (the last 20 are kept).</summary>
    public static void Keep(string designPath)
    {
        try
        {
            if (!File.Exists(designPath)) return;
            var dir = Folder(designPath);
            Directory.CreateDirectory(dir);
            File.Copy(designPath, Path.Combine(dir, File.GetLastWriteTime(designPath).ToString("yyyy-MM-dd HH-mm-ss") + DocFile.Extension), overwrite: true);
            foreach (var old in Directory.GetFiles(dir).OrderByDescending(f => f).Skip(20)) File.Delete(old);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>When a kept copy was saved, as a person reads it: "Wednesday, October 21, 2026, 12:23 AM · yesterday".</summary>
    public static string Saved(string versionPath)
    {
        var name = Path.GetFileNameWithoutExtension(versionPath);
        if (!DateTime.TryParseExact(name, "yyyy-MM-dd HH-mm-ss", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var at)) return name;
        int days = (DateTime.Today - at.Date).Days;
        string ago = days switch { 0 => "today", 1 => "yesterday", < 0 => "", _ => $"{days} days ago" };
        return $"{at:D}, {at:t}" + (ago.Length > 0 ? $" · {ago}" : "");
    }

    public VersionsWindow(string designPath)
    {
        Title = "Earlier versions";
        Width = 420;
        Height = 460;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");
        var dir = Folder(designPath);
        var files = Directory.Exists(dir) ? Directory.GetFiles(dir).OrderByDescending(f => f).ToList() : [];
        var list = new ListBox { ItemsSource = files.Select(Saved).ToList() };
        var open = new Button { Content = "Open as a new design", Classes = { "primary" }, IsDefault = true };
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        open.Click += (_, _) => { if (list.SelectedIndex >= 0) { Chosen = files[list.SelectedIndex]; Close(); } };
        cancel.Click += (_, _) => Close();
        var head = Hint(files.Count == 0 ? "No earlier versions yet: one is kept each time you save over the file." : $"The last {files.Count} saves of {Path.GetFileName(designPath)} (newest first). Opening one doesn't change the file.");
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = new(0, 10, 0, 0), Children = { cancel, open } };
        DockPanel.SetDock(head, Dock.Top);
        DockPanel.SetDock(buttons, Dock.Bottom);
        Content = new DockPanel { Margin = new Thickness(14), Children = { head, buttons, list } };
    }
}

/// <summary>Play the job back on the work area: where the laser will be, at any moment, sped up.</summary>
public sealed class RunPreviewWindow : Window
{
    readonly BuiltJob _job;
    readonly CanvasView _canvas;
    readonly Slider _time = new() { Minimum = 0 };
    readonly ComboBox _speed = new() { ItemsSource = new[] { "×1", "×5", "×20", "×100" }, SelectedIndex = 2, Width = 80 };
    readonly Button _play = new() { Content = "Play", MinWidth = 80, Classes = { "primary" } };
    readonly TextBlock _at = new() { VerticalAlignment = VerticalAlignment.Center, MinWidth = 120 };
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(40) };
    DateTime _last;

    public RunPreviewWindow(BuiltJob job, CanvasView canvas)
    {
        _job = job;
        _canvas = canvas;
        Title = "Run preview";
        Width = 520;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");
        double total = job.Timeline.Count == 0 ? job.EstimateS : job.Timeline[^1].End;
        _time.Maximum = Math.Max(0.1, total);
        _time.ValueChanged += (_, _) => ShowTime();
        _play.Click += (_, _) =>
        {
            if (_timer.IsEnabled) { _timer.Stop(); _play.Content = "Play"; return; }
            if (_time.Value >= _time.Maximum) _time.Value = 0;
            _last = DateTime.Now;
            _timer.Start();
            _play.Content = "Pause";
        };
        _timer.Tick += (_, _) =>
        {
            var now = DateTime.Now;
            double k = _speed.SelectedIndex switch { 0 => 1, 1 => 5, 3 => 100, _ => 20 };
            _time.Value = Math.Min(_time.Maximum, _time.Value + (now - _last).TotalSeconds * k);
            _last = now;
            if (_time.Value >= _time.Maximum) { _timer.Stop(); _play.Content = "Play"; }
        };
        Closed += (_, _) => { _timer.Stop(); _canvas.ShowRun(null, null); };
        Content = new StackPanel
        {
            Margin = new Thickness(14),
            Spacing = 8,
            Children =
            {
                Hint("Where the laser will be at each moment of the job (by the time estimate): marked paths turn green, the one being marked amber."),
                _time,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { _play, _speed, _at } },
            },
        };
        ShowTime();
    }

    void ShowTime()
    {
        _canvas.ShowRun(_job, RunTracker.At(_job, _time.Value));
        _at.Text = $"{Clock(_time.Value)} of {Clock(_time.Maximum)}";
    }
}
