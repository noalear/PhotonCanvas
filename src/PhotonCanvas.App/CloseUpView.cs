using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using static PhotonCanvas.App.UiKit;

namespace PhotonCanvas.App;

/// <summary>
/// The close-up camera (in the laser head) in a corner of the work area: live, or one photo at a
/// time, straightened, at two sizes. Opening it shows a fresh photo; live view streams it (the
/// machine sends one camera at a time, so the overview's live view pauses meanwhile).
/// </summary>
public sealed class CloseUpView : UserControl
{
    readonly MachinePanel _m;
    readonly Image _image = new() { Stretch = Stretch.Uniform };
    readonly RotateTransform _turn = new();
    readonly ToggleButton _live = new() { Classes = { "rb" } };
    readonly Button _photo = new() { Classes = { "rb" } }, _save = new() { Classes = { "rb" } }, _size = new() { Classes = { "rb" } }, _close = new() { Classes = { "icon" } };
    readonly TextBlock _note = new() { Classes = { "hint" }, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new(16) };
    readonly Border _frame;
    bool _large;
    byte[]? _lastJpg;

    /// <summary>The viewer was closed.</summary>
    public event Action? Closed;

    static Control IconLabel(string icon, string text) => new StackPanel
    {
        Orientation = Orientation.Horizontal, Spacing = 5,
        Children =
        {
            new PathIcon { Data = Icon(icon), Width = 13, Height = 13, VerticalAlignment = VerticalAlignment.Center },
            new TextBlock { Text = text, FontSize = 12, VerticalAlignment = VerticalAlignment.Center },
        },
    };

    public CloseUpView(MachinePanel machine)
    {
        _m = machine;
        HorizontalAlignment = HorizontalAlignment.Right;
        VerticalAlignment = VerticalAlignment.Bottom;
        Margin = new(0, 0, 16, 16);

        _live.Content = IconLabel("IcLive", "Live");
        ToolTip.SetTip(_live, "Keep the close-up updating. The overview camera's live view pauses meanwhile: the machine sends one camera at a time.");
        _live.Click += (_, _) => { _m.CloseUpLive = _live.IsChecked == true; Update(); };
        _photo.Content = IconLabel("IcCamera", "Photo");
        ToolTip.SetTip(_photo, "Take one close-up photo.");
        _photo.Click += async (_, _) => await TakePhoto();
        _save.Content = IconLabel("IcSave", "Save");
        ToolTip.SetTip(_save, "Save the close-up photo as it came from the camera (full size).");
        _save.Click += async (_, _) => await SavePhoto();
        _size.Click += (_, _) => { _large = !_large; Update(); };
        _close.Content = new PathIcon { Data = Icon("IcPlus"), Width = 12, Height = 12, RenderTransform = new RotateTransform(45) };
        ToolTip.SetTip(_close, "Close the close-up camera");
        _close.Click += (_, _) => Close();

        // The camera sits at an angle in the head: turn its picture so the bed looks straight.
        var turned = new LayoutTransformControl { LayoutTransform = _turn, Child = _image };
        _frame = new Border
        {
            Background = Brush("Bg0"), CornerRadius = new(6), ClipToBounds = true,
            Child = new Panel { Children = { turned, _note } },
        };
        var title = new TextBlock { Text = "CLOSE-UP CAMERA", Classes = { "h" }, Margin = new(2, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        var bar = new DockPanel { Margin = new(0, 0, 0, 6) };
        DockPanel.SetDock(title, Dock.Left);
        DockPanel.SetDock(_close, Dock.Right);
        bar.Children.Add(title);
        bar.Children.Add(_close);
        bar.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, HorizontalAlignment = HorizontalAlignment.Right, Children = { _live, _photo, _save, _size } });
        Content = new Border { Classes = { "hud" }, Child = new DockPanel { Children = { bar, _frame } } };
        DockPanel.SetDock(bar, Dock.Top);

        _m.CloseUpFrame += OnFrame;
        _m.StateChanged += Update;
        Update();
    }

    /// <summary>Show it and take a first photo.</summary>
    public async Task OpenAsync()
    {
        IsVisible = true;
        Update();
        if (_image.Source is null) await TakePhoto();
    }

    public void Close()
    {
        if (_m.CloseUpLive) _m.CloseUpLive = false;
        IsVisible = false;
        Closed?.Invoke();
    }

    void OnFrame(Bitmap bmp)
    {
        _image.Source = bmp;
        _note.IsVisible = false;
    }

    async Task TakePhoto()
    {
        if (!_m.IsConnected) { Update(); return; }
        _photo.IsEnabled = false;
        _note.Text = "Taking a photo…";
        _note.IsVisible = _image.Source is null;
        try { _lastJpg = await _m.CloseUpPhotoAsync() ?? _lastJpg; }
        catch (PhotonCanvas.Device.DeviceException e) { _note.Text = $"Couldn't take a photo: {e.Message}"; _note.IsVisible = true; }
        finally { Update(); }
    }

    async Task SavePhoto()
    {
        if (TopLevel.GetTopLevel(this) is not Window owner) return;
        byte[]? jpg = _lastJpg;
        if (_m.IsConnected)
        {
            try { jpg = await _m.SnapshotAsync(1); }
            catch (PhotonCanvas.Device.DeviceException) { }
        }
        if (jpg is null) return;
        var file = await owner.StorageProvider.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
        {
            Title = "Save close-up photo",
            DefaultExtension = "jpg",
            SuggestedFileName = $"closeup_{DateTime.Now:yyyyMMdd_HHmmss}.jpg",
        });
        if (file?.TryGetLocalPath() is { } path) await File.WriteAllBytesAsync(path, jpg);
    }

    void Update()
    {
        bool on = _m.IsConnected;
        _turn.Angle = _m.CloseUpRotationDeg;
        _live.IsEnabled = _photo.IsEnabled = on;
        _live.IsChecked = _m.CloseUpLive;
        _save.IsEnabled = on || _lastJpg is not null;
        _size.Content = IconLabel(_large ? "IcZoomOut" : "IcZoomIn", _large ? "Smaller" : "Bigger");
        _frame.Width = _large ? 640 : 380;
        _frame.Height = _large ? 480 : 285;
        if (!on && _image.Source is null)
        {
            _note.Text = "Connect the machine to see through the close-up camera.";
            _note.IsVisible = true;
        }
    }
}
