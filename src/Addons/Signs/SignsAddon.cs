using PhotonCanvas.App.Addons;

namespace PhotonCanvas.Addon.Signs;

/// <summary>
/// Import → Generate → Signs, its command and "Edit sign…" on the right-click menu, opening
/// <see cref="SignsWindow"/>: house signs, name plaques and door signs with carved lettering, framed
/// with a border from the border maker, made into a depth map on a depth-slices layer. The layout
/// and carving are in Engine/.
/// </summary>
public sealed class SignsAddon : IAddon
{
    /// <summary>An oval sign with a line of lettering across it and a hole at the top (24 × 24).</summary>
    const string Icon = "F0 M12 3 C17.5 3 22 7 22 12 C22 17 17.5 21 12 21 C6.5 21 2 17 2 12 C2 7 6.5 3 12 3Z "
        + "M12 5 C7.6 5 4 8.1 4 12 C4 15.9 7.6 19 12 19 C16.4 19 20 15.9 20 12 C20 8.1 16.4 5 12 5Z "
        + "M6.5 10.5 H8.3 L9.2 13.2 L10.1 10.5 H11.2 L12.1 13.2 L13 10.5 H14.8 L13 15 H11.6 L10.65 12.4 L9.7 15 H8.3Z "
        + "M15.4 10.5 H17.6 V15 H15.4Z";

    IAddonHost _host = null!;
    SignsWindow? _window;

    public void Start(IAddonHost host)
    {
        _host = host;
        host.AddRibbonButton(new RibbonButton("Import", "Generate", "Signs", Icon, Open)
        {
            Tip = "House signs, name plaques and door signs: your words V-carved, sunk or raised, with a carved border, made into a depth map to carve",
            Large = true,
            Order = 15,
        });
        host.AddCommand(new AddonCommand("signs.open", "Shapes", "Sign maker…", "", Open));
        host.AddContextMenuItem(new ContextMenuEntry("Edit sign…", Open) { Visible = Selected });
    }

    /// <summary>Is a sign picture selected (cheap: its spec isn't read).</summary>
    bool Selected()
    {
        var sel = _host.Document.Selection;
        return sel.Count > 0 && _host.Document.State.Shapes.Any(s => sel.Contains(s.Id) && s.AddonData.ContainsKey(SignOutput.SpecKey));
    }

    /// <summary>Opens the maker: on the sign selected, if one is, to change it; otherwise for a new one.</summary>
    void Open()
    {
        if (_window is not null) { _window.Activate(); return; }
        _ = _host.Guard(async () =>
        {
            var editing = SignOutput.Find(_host.Document.State, _host.Document.Selection);
            _window = new SignsWindow(_host.Document, _host.Machine, _host.DataFolder, _host.Status, editing);
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
