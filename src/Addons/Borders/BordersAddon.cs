using PhotonCanvas.App.Addons;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;

namespace PhotonCanvas.Addon.Borders;

/// <summary>
/// Import → Generate → Borders, its command and "Edit border…" on the right-click menu, opening
/// <see cref="BordersWindow"/>: carved borders for picture frames, cabinet edges and furniture trim,
/// made into a depth map on a depth-slices layer. The patterns and layouts are in Engine/. Machine → Work
/// area → Wrap a pattern opens it on a round part in the rotary, and a job check makes sure such a wrap is sent
/// for the Object Ø it was made for.
/// </summary>
public sealed class BordersAddon : IAddon
{
    /// <summary>A picture frame with a carved band of beads round it (24 × 24).</summary>
    const string Icon = "F0 M2 2 H22 V22 H2Z M7 7 V17 H17 V7Z "
        + "M4.6 3.4a1.2 1.2 0 1 0 0 2.4a1.2 1.2 0 1 0 0 -2.4z M4.6 18.2a1.2 1.2 0 1 0 0 2.4a1.2 1.2 0 1 0 0 -2.4z M9.53 3.4a1.2 1.2 0 1 0 0 2.4a1.2 1.2 0 1 0 0 -2.4z M9.53 18.2a1.2 1.2 0 1 0 0 2.4a1.2 1.2 0 1 0 0 -2.4z "
        + "M14.47 3.4a1.2 1.2 0 1 0 0 2.4a1.2 1.2 0 1 0 0 -2.4z M14.47 18.2a1.2 1.2 0 1 0 0 2.4a1.2 1.2 0 1 0 0 -2.4z M19.4 3.4a1.2 1.2 0 1 0 0 2.4a1.2 1.2 0 1 0 0 -2.4z M19.4 18.2a1.2 1.2 0 1 0 0 2.4a1.2 1.2 0 1 0 0 -2.4z "
        + "M4.6 8.33a1.2 1.2 0 1 0 0 2.4a1.2 1.2 0 1 0 0 -2.4z M19.4 8.33a1.2 1.2 0 1 0 0 2.4a1.2 1.2 0 1 0 0 -2.4z M4.6 13.27a1.2 1.2 0 1 0 0 2.4a1.2 1.2 0 1 0 0 -2.4z M19.4 13.27a1.2 1.2 0 1 0 0 2.4a1.2 1.2 0 1 0 0 -2.4z";

    /// <summary>A round part with a spiral band wound along it (24 × 24).</summary>
    const string WrapIcon = "F0 M6 4 H18 A2 8 0 0 1 18 20 H6 A2 8 0 0 1 6 4Z M6 5.4 A1.2 6.6 0 0 0 6 18.6 H18 A1.2 6.6 0 0 0 18 5.4Z "
        + "M7.6 5.4 L10.4 5.4 L7.4 18.6 L4.6 18.6Z M12.6 5.4 L15.4 5.4 L12.4 18.6 L9.6 18.6Z M17.6 5.4 L19.2 5.4 L17.4 18.6 L14.6 18.6Z";

    IAddonHost _host = null!;
    BordersWindow? _window;

    public void Start(IAddonHost host)
    {
        _host = host;
        host.AddRibbonButton(new RibbonButton("Import", "Generate", "Borders", Icon, Open)
        {
            Tip = "Carved borders for picture frames, cabinet edges and furniture trim (egg and dart, rope, Greek key, acanthus and more), made into a depth map to carve",
            Large = true,
            Order = 14,
        });
        host.AddRibbonButton(new RibbonButton("Machine", "Work area", "Wrap a pattern", WrapIcon, OpenRotary)
        {
            Tip = "Wrap a carved border (rope, Greek key, egg and dart and more) round a pen, a handle or a table leg in the rotary: rings, flutes or a spiral that meets itself round the back",
            Order = 22,
        });
        host.AddCommand(new AddonCommand("borders.open", "Shapes", "Border maker…", "", Open));
        host.AddCommand(new AddonCommand("borders.rotary", "Laser", "Wrap a pattern round a rotary part…", "", OpenRotary));
        host.AddContextMenuItem(new ContextMenuEntry("Edit border…", Open) { Visible = Selected });
        host.AddJobHook(new RotaryWrapCheck());
    }

    /// <summary>The Object Ø Machine → Work mode is set to, when it's the rotary.</summary>
    double? WorkModeDiameter() => WorkModes.From(_host.AppSettings) is WorkMode.Rotary r ? r.CircumferenceMm / Math.PI : null;

    void Open() => Open(rotary: false);

    void OpenRotary() => Open(rotary: true);

    /// <summary>Is a border picture selected (cheap: its spec isn't read).</summary>
    bool Selected()
    {
        var sel = _host.Document.Selection;
        return sel.Count > 0 && _host.Document.State.Shapes.Any(s => sel.Contains(s.Id) && s.AddonData.ContainsKey(BorderOutput.SpecKey));
    }

    /// <summary>Opens the maker: on the border selected, if one is, to change it; otherwise for a new one.</summary>
    void Open(bool rotary)
    {
        if (_window is not null) { _window.Activate(); return; }
        _ = _host.Guard(async () =>
        {
            var editing = BorderOutput.Find(_host.Document.State, _host.Document.Selection);
            _window = new BordersWindow(_host.Document, _host.Machine, _host.DataFolder, _host.Status, editing, WorkModeDiameter(), rotary && editing is null);
            try
            {
                await _window.ShowDialog(_host.Owner);
                if (_window.MadeOnLayer is { } layer) _host.ActivateLayer(layer);
            }
            finally { _window = null; }
        });
    }

    public void Stop()
    {
        _window?.Close();
        _window = null;
    }
}
