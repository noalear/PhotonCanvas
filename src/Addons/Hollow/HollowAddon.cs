using Avalonia.Controls;
using Avalonia.Layout;
using PhotonCanvas.App.Addons;
using PhotonCanvas.Doc.Geometry;
using static PhotonCanvas.App.UiKit;

namespace PhotonCanvas.Addon.Hollow;

/// <summary>
/// Hollow stacked models: "Hollow it out" and its settings in 3D to slices (the stacked-layer
/// settings), which add a <see cref="HollowSlices"/> step to the slicing.
/// </summary>
public sealed class HollowAddon : IAddon
{
    public void Start(IAddonHost host) => host.AddToSlot("stackslice.stacked", ctx => ctx.Data is IStackSliceSlot slot ? new HollowPanel(slot) : null);

    public void Stop() { }
}

/// <summary>The settings, in one 3D to slices window.</summary>
sealed class HollowPanel : StackPanel
{
    readonly IStackSliceSlot _slot;
    readonly CheckBox _on = new() { Content = "Hollow it out" };
    readonly NumericUpDown _wall = Number(1, 50, 0.5m, "0.#"), _channel = Number(0, 30, 0.5m, "0.#"), _magnet = Number(0, 40, 0.5m, "0.#"), _magnetThick = Number(0.5m, 20, 0.5m, "0.#"), _magnets = Number(1, 8, 1, "0");
    readonly CheckBox _openBottom = new() { Content = "Open bottom" }, _openTop = new() { Content = "Open top" };
    readonly StackPanel _settings;
    readonly Control _magnetThickRow, _magnetsRow;

    public HollowPanel(IStackSliceSlot slot)
    {
        _slot = slot;
        Spacing = 10;
        _wall.Value = 5; _channel.Value = 0; _magnet.Value = 0; _magnetThick.Value = 3; _magnets.Value = 2;
        ToolTip.SetTip(_on, "Keep a shell round the outside of each slice and cut the middle out: less material, less glue, a lighter model. The top and bottom stay closed.");
        ToolTip.SetTip(_openBottom, "The hollow runs out through the base: a lamp shade, or a cover that goes over a light.");
        ToolTip.SetTip(_openTop, "The hollow runs out through the top: a pot or a bowl.");
        _settings = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                Field("Wall (mm)", _wall, tip: "How thick the shell is, sideways and at the top and bottom. Thicker is stronger and leaves room for the pins."),
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, Children = { _openBottom, _openTop } },
                Field("Wire hole Ø", _channel, tip: "A hole straight up through the solid base into the hollow, for a wire or to push an LED in. 0 = none."),
                Field("Magnet Ø", _magnet, tip: "Round pockets in the base for magnets, so the model sticks to a fridge or a steel base. 0 = none."),
                (_magnetThickRow = Field("Magnet thickness", _magnetThick, tip: "The pockets go up through as many slices as this takes, with solid above them.")),
                (_magnetsRow = Field("Magnets", _magnets, tip: "How many pockets, spread as far apart as the base allows.")),
            },
        };
        Children.Add(_on);
        Children.Add(_settings);
        _on.IsCheckedChanged += (_, _) => Changed();
        _openBottom.IsCheckedChanged += (_, _) => Changed();
        _openTop.IsCheckedChanged += (_, _) => Changed();
        foreach (var n in new[] { _wall, _channel, _magnet, _magnetThick, _magnets }) n.ValueChanged += (_, _) => Changed();
        Shown();
    }

    /// <summary>Its settings only when it's on; the magnets' thickness and count only with a magnet size.</summary>
    void Shown()
    {
        _settings.IsVisible = _on.IsChecked == true;
        _magnetThickRow.IsVisible = _magnetsRow.IsVisible = (_magnet.Value ?? 0) > 0;
    }

    void Changed()
    {
        Shown();
        _slot.SetModifier(this, _on.IsChecked != true ? null : new HollowSlices(new HollowOptions
        {
            WallMm = (double)(_wall.Value ?? 5),
            OpenBottom = _openBottom.IsChecked == true,
            OpenTop = _openTop.IsChecked == true,
            ChannelMm = (double)(_channel.Value ?? 0),
            MagnetMm = (double)(_magnet.Value ?? 0),
            MagnetThickMm = (double)(_magnetThick.Value ?? 3),
            Magnets = (int)(_magnets.Value ?? 2),
        }));
    }
}
