using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using PhotonCanvas.Core;
using PhotonCanvas.Doc.Import;
using static PhotonCanvas.App.UiKit;

namespace PhotonCanvas.App;

/// <summary>
/// Picking a 3D model by sight: every STL and OBJ in a folder as a shaded thumbnail (drawn here,
/// nothing to install), with its name and size. Thumbnails are kept, so a folder opens instantly
/// the next time. Double-click (or Open) to use one.
/// </summary>
public sealed class ModelBrowserWindow : Window
{
    static readonly string[] Extensions = [".stl", ".obj"];
    static string CacheDir => Path.Combine(AppIdentity.DataDir, "model-thumbs");

    readonly AppSettings _settings;
    readonly TextBlock _folder = new() { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    readonly TextBox _search = new() { Watermark = "Search by name", Width = 220 };
    readonly CheckBox _subfolders = new() { Content = "Include subfolders" };
    readonly WrapPanel _tiles = new();
    readonly TextBlock _status = new() { Classes = { "hint" }, VerticalAlignment = VerticalAlignment.Center };
    readonly Button _open = new() { Content = "Open", Classes = { "primary" }, MinWidth = 110, IsEnabled = false };
    readonly SemaphoreSlim _drawing = new(2);
    CancellationTokenSource? _cts;
    string? _selected;
    Border? _selectedTile;

    /// <summary>The model chosen (full path), or null.</summary>
    public string? Chosen { get; private set; }

    public ModelBrowserWindow(AppSettings settings)
    {
        _settings = settings;
        Title = "Open a 3D model";
        Width = 980;
        Height = 700;
        MinWidth = 640;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");

        var choose = Button("Choose folder…", "IcImport");
        choose.Click += async (_, _) => await ChooseFolder();
        var browse = Button("Browse files…", tip: "The usual file dialog.");
        browse.Click += async (_, _) => await BrowseFiles();
        _search.TextChanged += (_, _) => Filter();
        _subfolders.IsCheckedChanged += (_, _) => Load();
        _open.Click += (_, _) => Finish(_selected);
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 100 };
        cancel.Click += (_, _) => Close();
        Closing += (_, _) => _cts?.Cancel();

        var top = new DockPanel { Margin = new(0, 0, 0, 10) };
        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _search, _subfolders } };
        DockPanel.SetDock(choose, Dock.Left);
        DockPanel.SetDock(right, Dock.Right);
        top.Children.Add(choose);
        top.Children.Add(right);
        top.Children.Add(new Border { Margin = new(10, 0), Child = _folder });
        var bottom = new DockPanel { Margin = new(0, 10, 0, 0) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { browse, cancel, _open } };
        DockPanel.SetDock(buttons, Dock.Right);
        bottom.Children.Add(buttons);
        bottom.Children.Add(_status);
        var root = new DockPanel { Margin = new(16) };
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(top);
        root.Children.Add(bottom);
        root.Children.Add(new Border { Background = Brush("Bg0"), CornerRadius = new(8), Padding = new(8), Child = new ScrollViewer { Content = _tiles } });
        Content = root;
        Opened += (_, _) => Load();
    }

    string? Folder => _settings.ModelFolder is { } f && Directory.Exists(f) ? f : null;

    async Task ChooseFolder()
    {
        var start = Folder is { } f ? await StorageProvider.TryGetFolderFromPathAsync(f) : null;
        var picked = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Folder with 3D models", SuggestedStartLocation = start });
        if (picked.Count == 0 || picked[0].TryGetLocalPath() is not { } path) return;
        _settings.ModelFolder = path;
        _settings.Save();
        Load();
    }

    async Task BrowseFiles()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "3D model to slice",
            FileTypeFilter = [new FilePickerFileType("3D models (STL, OBJ)") { Patterns = ["*.stl", "*.obj"] }],
        });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path) Finish(path);
    }

    void Finish(string? path)
    {
        if (path is null) return;
        _settings.ModelFolder = Path.GetDirectoryName(path);
        _settings.Save();
        Chosen = path;
        Close();
    }

    void Load()
    {
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        _tiles.Children.Clear();
        _selected = null;
        _selectedTile = null;
        _open.IsEnabled = false;
        if (Folder is not { } folder)
        {
            _folder.Text = "No folder chosen yet.";
            _status.Text = "Choose the folder where you keep your 3D models.";
            return;
        }
        _folder.Text = folder;
        List<string> files;
        try
        {
            files = [.. Directory.EnumerateFiles(folder, "*", _subfolders.IsChecked == true ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
                .Where(f => Extensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .OrderBy(f => Path.GetFileName(f), StringComparer.CurrentCultureIgnoreCase)
                .Take(2000)];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _status.Text = $"Couldn't read the folder: {e.Message}";
            return;
        }
        _status.Text = files.Count == 0 ? "No STL or OBJ files here." : $"{files.Count} model{(files.Count == 1 ? "" : "s")}.";
        foreach (var f in files) _tiles.Children.Add(Tile(f, cts.Token));
        Filter();
    }

    void Filter()
    {
        var q = _search.Text?.Trim() ?? "";
        foreach (var t in _tiles.Children.OfType<Border>())
            t.IsVisible = q.Length == 0 || (t.Tag as string ?? "").Contains(q, StringComparison.CurrentCultureIgnoreCase);
    }

    Border Tile(string path, CancellationToken ct)
    {
        var image = new Image { Width = 150, Height = 150, Stretch = Stretch.Uniform };
        var placeholder = new TextBlock { Text = "…", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Classes = { "muted" } };
        var info = new FileInfo(path);
        var tile = new Border
        {
            Width = 172, Margin = new(5), Padding = new(8), CornerRadius = new(8), Background = Brush("Bg2"),
            BorderThickness = new(2), BorderBrush = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand),
            Tag = Path.GetFileName(path),
            Child = new StackPanel
            {
                Spacing = 4,
                Children =
                {
                    new Panel { Width = 150, Height = 150, Children = { placeholder, image } },
                    new TextBlock { Text = Path.GetFileNameWithoutExtension(path), TextTrimming = TextTrimming.CharacterEllipsis, FontWeight = FontWeight.SemiBold },
                    new TextBlock { Text = $"{Path.GetExtension(path).TrimStart('.').ToUpperInvariant()} · {Size(info.Length)}", Classes = { "hint" } },
                },
            },
        };
        ToolTip.SetTip(tile, path);
        tile.PointerPressed += (_, e) =>
        {
            if (e.ClickCount >= 2) { Finish(path); return; }
            if (_selectedTile is { } old) old.BorderBrush = Brushes.Transparent;
            tile.BorderBrush = Brush("Accent");
            _selectedTile = tile;
            _selected = path;
            _open.IsEnabled = true;
        };
        _ = Draw(path, info, image, placeholder, ct);
        return tile;
    }

    static string Size(long bytes) => bytes >= 1 << 20 ? $"{bytes / (double)(1 << 20):0.#} MB" : $"{Math.Max(1, bytes / 1024)} KB";

    /// <summary>The thumbnail: from the cache when the file hasn't changed, else drawn (two at a time) and kept.</summary>
    async Task Draw(string path, FileInfo info, Image image, TextBlock placeholder, CancellationToken ct)
    {
        var key = $"{path}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
        var cache = Path.Combine(CacheDir, $"{Fnv(key):x16}.png");
        byte[]? png = null;
        try
        {
            if (File.Exists(cache)) png = await File.ReadAllBytesAsync(cache, ct);
            else
            {
                await _drawing.WaitAsync(ct);
                try
                {
                    png = await Task.Run(() =>
                    {
                        var mesh = StlDepth.Load(path);
                        return MeshPicture.Render(mesh, 300).EncodePng();
                    }, ct);
                }
                finally { _drawing.Release(); }
                Directory.CreateDirectory(CacheDir);
                await File.WriteAllBytesAsync(cache, png, ct);
            }
        }
        catch (OperationCanceledException) { return; }
        catch (Exception e) when (e is FormatException or IOException or UnauthorizedAccessException or OutOfMemoryException or ArgumentException)
        {
            Dispatcher.UIThread.Post(() => placeholder.Text = "Can't read it");
            return;
        }
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                using var ms = new MemoryStream(png!);
                image.Source = new Avalonia.Media.Imaging.Bitmap(ms);
                placeholder.IsVisible = false;
            }
            catch (ArgumentException) { placeholder.Text = "Can't read it"; }
        });
    }

    static ulong Fnv(string s)
    {
        ulong h = 14695981039346656037;
        foreach (char c in s) { h ^= c; h *= 1099511628211; }
        return h;
    }
}
