using PhotonCanvas.App.Addons;
using PhotonCanvas.Doc;

namespace PhotonCanvas.Addon.PhotoRelief;

/// <summary>
/// Import → 3D &amp; maps → Photo relief, its command, and on the right-click menu "Make a relief
/// from this photo…" (a picture) and "Edit photo relief…" (one it made), opening
/// <see cref="PhotoReliefWindow"/>: a photo or portrait turned into a carved relief, made into a
/// depth map on a depth-slices layer. The method is in Engine/.
/// </summary>
public sealed class PhotoReliefAddon : IAddon
{
    /// <summary>An oval plaque with a head and shoulders on it (24 × 24).</summary>
    const string Icon = "F0 M12 1.5 C17.5 1.5 21 6.2 21 12 C21 17.8 17.5 22.5 12 22.5 C6.5 22.5 3 17.8 3 12 C3 6.2 6.5 1.5 12 1.5Z "
        + "M12 3.5 C7.8 3.5 5 7.3 5 12 C5 16.7 7.8 20.5 12 20.5 C16.2 20.5 19 16.7 19 12 C19 7.3 16.2 3.5 12 3.5Z "
        + "M12 6.5 C13.9 6.5 15.2 8 15.2 10 C15.2 12 13.9 13.6 12 13.6 C10.1 13.6 8.8 12 8.8 10 C8.8 8 10.1 6.5 12 6.5Z "
        + "M6.6 18.2 C7.4 15.9 9.4 14.6 12 14.6 C14.6 14.6 16.6 15.9 17.4 18.2 C16.1 19.6 14.2 20.5 12 20.5 C9.8 20.5 7.9 19.6 6.6 18.2Z";

    IAddonHost _host = null!;
    PhotoReliefWindow? _window;

    public void Start(IAddonHost host)
    {
        _host = host;
        host.AddRibbonButton(new RibbonButton("Import", "3D & maps", "Photo relief", Icon, Open)
        {
            Tip = "Turn a photo or portrait into a carved relief: the subject raised and rounded, its shading made into depth, on a plaque if you like",
            Large = true,
            Order = 14,
        });
        host.AddCommand(new AddonCommand("photorelief.open", "Shapes", "Photo to relief…", "", Open));
        host.AddContextMenuItem(new ContextMenuEntry("Make a relief from this photo…", Open) { Visible = () => Selected() == false });
        host.AddContextMenuItem(new ContextMenuEntry("Edit photo relief…", Open) { Visible = () => Selected() == true });
    }

    /// <summary>True when one of our reliefs is selected, false for another picture, null for neither.</summary>
    bool? Selected()
    {
        var sel = _host.Document.Selection;
        if (sel.Count == 0) return null;
        bool picture = false;
        foreach (var s in _host.Document.State.Shapes)
        {
            if (!sel.Contains(s.Id) || s is not ImageShape) continue;
            if (s.AddonData.ContainsKey(ReliefOutput.SpecKey)) return true;
            picture = true;
        }
        return picture ? false : null;
    }

    /// <summary>Opens it: on the relief selected, to change it; on the picture selected, to make one from it; otherwise to open a photo.</summary>
    void Open()
    {
        if (_window is not null) { _window.Activate(); return; }
        _ = _host.Guard(async () =>
        {
            var editing = ReliefOutput.Find(_host.Document.State, _host.Document.Selection);
            var picture = editing is null ? _host.SelectedPicture : null;
            _window = new PhotoReliefWindow(_host.Document, _host.Machine, _host.DataFolder, _host.Status, picture, editing);
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
