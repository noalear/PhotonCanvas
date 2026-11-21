using Avalonia.Controls;
using Avalonia.Controls.Diagnostics;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;

namespace PhotonCanvas.App;

/// <summary>
/// Tooltips never take a click: a click where a tooltip shows goes to whatever is under it.
/// </summary>
/// <remarks>
/// A tooltip opens just under the pointer. Near the bottom of the screen (the job bar) there's no room
/// there, so Avalonia puts it above the pointer instead but keeps its 20 px drop, which leaves the
/// tooltip's bottom edge over the pointer. On Windows a tooltip is a little window of its own, so the
/// click landed on the tooltip (closing it) instead of the button: Ship It or Start needed a second
/// click whenever the pointer had rested on it long enough for its tooltip to show (2026-11-21).
/// </remarks>
static class ClickThroughTips
{
    static bool _installed;

    /// <summary>For every window in the program (once; later calls do nothing).</summary>
    public static void Install()
    {
        if (_installed) return;
        _installed = true;
        // Loaded comes each time a tooltip opens: each opening gets a new popup to show it in.
        Control.LoadedEvent.AddClassHandler<ToolTip>((tip, _) => Through(tip), RoutingStrategies.Direct);
    }

    /// <summary>The popup showing this tooltip lets clicks through (on Windows, to the window beneath).</summary>
    internal static void Through(ToolTip tip)
    {
        if (tip is IPopupHostProvider { PopupHost: { } host }) host.IsHitTestVisible = false;
    }
}
