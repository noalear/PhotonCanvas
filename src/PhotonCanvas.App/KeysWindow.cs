using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using PhotonCanvas.Core;
using PhotonCanvas.Device;
using static PhotonCanvas.App.UiKit;

namespace PhotonCanvas.App;

/// <summary>
/// Sets up the three connection keys. They are not shipped with this program:
/// the user's own xTool Studio is searched for them (or they are pasted in),
/// and each is checked against a fingerprint before it is saved.
/// </summary>
public sealed class KeysWindow : Window
{
    readonly TextBox _request = KeyBox(), _reply = KeyBox(), _pairing = KeyBox();
    readonly TextBlock _requestOk = Mark(), _replyOk = Mark(), _pairingOk = Mark();
    readonly Button _auto = new() { Content = "Find them in my xTool Studio", Classes = { "primary" }, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
    readonly Button _choose = Button("Choose xTool Studio's program file…", tip: "Usually C:\\Users\\<you>\\AppData\\Local\\Programs\\xTool Studio\\resources\\app.asar");
    readonly ProgressBar _progress = new() { Minimum = 0, Maximum = 1, IsVisible = false, Height = 6 };
    readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    readonly Button _save = new() { Content = "Save", Classes = { "primary" }, IsDefault = true, MinWidth = 100, IsEnabled = false };
    CancellationTokenSource? _scan;

    /// <summary>True once valid keys were saved.</summary>
    public bool Saved { get; private set; }

    public KeysWindow()
    {
        Title = "Connection keys";
        Width = 560;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");

        var keys = MachineKeys.Load();
        _request.Text = keys.DiscoveryRequest;
        _reply.Text = keys.DiscoveryReply;
        _pairing.Text = keys.Pairing;
        foreach (var b in new[] { _request, _reply, _pairing }) b.TextChanged += (_, _) => Validate();

        _auto.Click += async (_, _) => await AutoFind();
        _choose.Click += async (_, _) => await ChooseFile();
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 100 };
        cancel.Click += (_, _) => Close();
        _save.Click += (_, _) => Save();
        Closing += (_, _) => _scan?.Cancel();

        var manual = new Expander
        {
            Header = "Or type / paste them in",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Content = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    KeyRow("Search key", _request, _requestOk),
                    KeyRow("Answer key", _reply, _replyOk),
                    KeyRow("Pairing key", _pairing, _pairingOk),
                    Hint("Each key is checked as you type. A ✓ means it's correct; nothing is saved until all three are."),
                },
            },
        };

        Content = new Border
        {
            Padding = new Thickness(20),
            Child = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    new TextBlock { Text = "One-time setup", FontSize = 18, FontWeight = FontWeight.SemiBold },
                    new TextBlock
                    {
                        TextWrapping = TextWrapping.Wrap,
                        Text = "To find your laser on the network and connect to it, " + AppIdentity.ProductName + " needs three short codes that xTool machines expect. " +
                               "They don't come with this program. If xTool Studio is installed on this computer, " + AppIdentity.ProductName + " can read them from it for you.",
                    },
                    Hint("Only those three codes are read, they stay on this computer, and nothing is sent anywhere. " +
                         "Don't have xTool Studio? Install it from xTool's website, run this once, then you can uninstall it."),
                    _auto,
                    _choose,
                    _progress,
                    _status,
                    manual,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        Children = { cancel, _save },
                    },
                },
            },
        };
        Validate();
        if (!KeyFinder.LikelyLocations().Any())
            _status.Text = "xTool Studio wasn't found in the usual places. If it's installed somewhere else, use \"Choose xTool Studio's program file…\".";
    }

    static TextBox KeyBox() => new() { FontFamily = new FontFamily("Cascadia Mono,Consolas,monospace"), FontSize = 12, Watermark = "not set" };
    static TextBlock Mark() => new() { Width = 22, FontSize = 15, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };

    static Control KeyRow(string label, TextBox box, TextBlock ok)
    {
        var g = new Grid { ColumnDefinitions = new("100,*,26") };
        var l = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Classes = { "muted" } };
        g.Children.Add(l);
        Grid.SetColumn(box, 1);
        g.Children.Add(box);
        Grid.SetColumn(ok, 2);
        g.Children.Add(ok);
        return g;
    }

    MachineKeys Current => new(_request.Text?.Trim() ?? "", _reply.Text?.Trim() ?? "", _pairing.Text?.Trim() ?? "");

    void Validate()
    {
        var k = Current;
        Show(_requestOk, k.DiscoveryRequest, k.RequestValid);
        Show(_replyOk, k.DiscoveryReply, k.ReplyValid);
        Show(_pairingOk, k.Pairing, k.PairingValid);
        _save.IsEnabled = k.IsComplete;
    }

    static void Show(TextBlock mark, string value, bool valid)
    {
        mark.Text = value.Length == 0 ? "" : valid ? "✓" : "✗";
        mark.Foreground = valid ? Brush("Ok") : Brush("Danger");
        ToolTip.SetTip(mark, value.Length == 0 ? null : valid ? "Correct" : "This isn't the right key");
    }

    async Task AutoFind()
    {
        var file = KeyFinder.LikelyLocations().FirstOrDefault();
        if (file is null)
        {
            _status.Text = "xTool Studio wasn't found in the usual places. Use \"Choose xTool Studio's program file…\" to point at it.";
            return;
        }
        await ScanFile(file);
    }

    async Task ChooseFile()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose xTool Studio's app.asar (in its \"resources\" folder)",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("xTool Studio program file") { Patterns = ["app.asar", "*.asar"] }, FilePickerFileTypes.All],
        });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path) return;
        await ScanFile(path);
    }

    async Task ScanFile(string file)
    {
        _scan?.Cancel();
        _scan = new CancellationTokenSource();
        var ct = _scan.Token;
        _auto.IsEnabled = _choose.IsEnabled = false;
        _progress.IsVisible = true;
        _progress.Value = 0;
        _status.Text = "Looking… (this takes a few seconds)";
        try
        {
            var progress = new Progress<double>(p => _progress.Value = p);
            var found = await Task.Run(() => KeyFinder.Scan(file, progress, ct), ct);
            // Keep anything already entered that the scan didn't find.
            if (found.DiscoveryRequest.Length > 0) _request.Text = found.DiscoveryRequest;
            if (found.DiscoveryReply.Length > 0) _reply.Text = found.DiscoveryReply;
            if (found.Pairing.Length > 0) _pairing.Text = found.Pairing;
            Validate();
            int n = new[] { found.DiscoveryRequest, found.DiscoveryReply, found.Pairing }.Count(s => s.Length > 0);
            _status.Text = Current.IsComplete
                ? "Found all three keys. Click Save."
                : n == 0
                    ? "No keys found in that file. Make sure it's xTool Studio's app.asar. (A very new or very old xTool Studio may store them differently.)"
                    : $"Found {n} of 3 keys. The rest can be typed in below.";
        }
        catch (OperationCanceledException) { }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _status.Text = $"Couldn't read that file: {e.Message}";
        }
        finally
        {
            _auto.IsEnabled = _choose.IsEnabled = true;
            _progress.IsVisible = false;
        }
    }

    void Save()
    {
        var k = Current;
        if (!k.IsComplete) return;
        try
        {
            k.Save();
            Saved = true;
            Close();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _status.Text = $"Couldn't save: {e.Message}";
        }
    }
}
