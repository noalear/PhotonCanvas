using Avalonia.Controls;
using Avalonia.Layout;

namespace PhotonCanvas.App;

/// <summary>
/// The machine's fill lights, front and back, as two sliders: for the light a test photo is taken
/// in (glare off the burn, or a dark close-up). Changes go to the machine as they're made; they
/// change the next photo, not the burn.
/// </summary>
public sealed class LightControls : StackPanel
{
    readonly Func<(int Front, int Back)> _get;
    readonly Action<int, int> _set;
    readonly Slider _front = new() { Minimum = 0, Maximum = 100, Width = 140, VerticalAlignment = VerticalAlignment.Center };
    readonly Slider _back = new() { Minimum = 0, Maximum = 100, Width = 140, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock _frontText = new() { MinWidth = 34, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock _backText = new() { MinWidth = 34, VerticalAlignment = VerticalAlignment.Center };
    bool _setting;

    public LightControls(Func<(int Front, int Back)> get, Action<int, int> set)
    {
        _get = get;
        _set = set;
        Spacing = 4;
        Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 6,
            Children =
            {
                new TextBlock { Text = "Lights: front", Classes = { "muted" }, VerticalAlignment = VerticalAlignment.Center }, _front, _frontText,
                new TextBlock { Text = "back", Classes = { "muted" }, VerticalAlignment = VerticalAlignment.Center, Margin = new(10, 0, 0, 0) }, _back, _backText,
            },
        });
        Children.Add(new TextBlock { Classes = { "hint" }, TextWrapping = Avalonia.Media.TextWrapping.Wrap, Text = "For the photo only: less light cuts glare off dark burns, more brings out a dim close-up. Take the photo again after changing them." });
        ToolTip.SetTip(_front, "The front fill light, 0 (off) to 100%.");
        ToolTip.SetTip(_back, "The back fill light, 0 (off) to 100%.");
        _front.ValueChanged += (_, _) => Changed();
        _back.ValueChanged += (_, _) => Changed();
        Refresh();
    }

    /// <summary>Show the machine's lights as they are now.</summary>
    public void Refresh()
    {
        _setting = true;
        var (f, b) = _get();
        _front.Value = f;
        _back.Value = b;
        _setting = false;
        Show();
    }

    void Changed()
    {
        Show();
        if (!_setting) _set((int)Math.Round(_front.Value), (int)Math.Round(_back.Value));
    }

    void Show()
    {
        _frontText.Text = _front.Value < 0.5 ? "off" : $"{_front.Value:0}%";
        _backText.Text = _back.Value < 0.5 ? "off" : $"{_back.Value:0}%";
    }
}
