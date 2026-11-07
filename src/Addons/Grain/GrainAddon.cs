using PhotonCanvas.App.Addons;

namespace PhotonCanvas.Addon.Grain;

/// <summary>
/// Grain-aware engraving: map the wood's grain from the camera picture, then lower the power where
/// it would burn darker (even engravings), or where it would burn lighter (bring the grain out), and
/// place pictures so their darks fall on dark grain.
/// </summary>
public sealed class GrainAddon : IAddon
{
    IAddonHost _host = null!;
    GrainWindow? _window;
    readonly GrainOverlay _overlay = new();

    /// <summary>The ribbon icon: a board with grain lines across it.</summary>
    const string Icon = "M3 4h18v16H3z M5 6v2.6c2.6-1.4 4.6 1.4 7 0s4.4-1.4 7 0V6z M5 10.8v2.4c2.6-1.4 4.6 1.4 7 0s4.4-1.4 7 0v-2.4c-2.6-1.4-4.6 1.4-7 0s-4.4-1.4-7 0z M5 15.4V18h14v-2.6c-2.6-1.4-4.6 1.4-7 0s-4.4-1.4-7 0z";

    public void Start(IAddonHost host)
    {
        _host = host;
        host.AddJobHook(new GrainHook());
        host.AddCanvasOverlay(_overlay);
        host.AddRibbonButton(new RibbonButton("Picture", "Effects", "Grain", Icon, Open)
        {
            Large = true,
            Tip = "Grain-aware engraving: map the wood's grain from the camera picture and even out the engraving, or bring the grain out",
            Order = 17,
        });
        host.AddCommand(new AddonCommand("grain.open", "Picture", "Grain-aware engraving…", "", Open));
        host.AddContextMenuItem(new ContextMenuEntry("Place on the grain…", Open) { Visible = () => host.SelectedPicture is not null });
    }

    void Open()
    {
        if (_window is { } w) { w.Activate(); return; }
        _window = new GrainWindow(_host, _overlay);
        _window.Closed += (_, _) => { _window = null; _overlay.Clear(); _host.InvalidateCanvas(); };
        _window.Show(_host.Owner);
    }

    public void Stop()
    {
        _window?.Close();
        _window = null;
        _overlay.Clear();
    }
}
