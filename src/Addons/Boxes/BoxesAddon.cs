using PhotonCanvas.App.Addons;
using PhotonCanvas.Doc;

namespace PhotonCanvas.Addon.Boxes;

/// <summary>
/// Import → Generate → Boxes and Home → Cut lines → Split with joints, their commands, and "Edit
/// box…" and "Split with joints…" on the right-click menu. The box maker is <see cref="BoxesWindow"/>,
/// the joints and box styles are in Engine/.
/// </summary>
public sealed class BoxesAddon : IAddon
{
    /// <summary>An open box with finger joints, its lid lifted (24 × 24).</summary>
    const string BoxIcon = "M3 10 L12 14 L21 10 L21 19 L12 23 L3 19 Z M12 14 L12 23 M3 10 L4.6 9.3 L4.6 10.7 L6.2 10 L6.2 8.6 L7.8 7.9 M21 10 L19.4 9.3 L19.4 10.7 L17.8 10 L17.8 8.6 L16.2 7.9 "
        + "M4 6 L13 2 L21 5.5 L12 9.5 Z";
    /// <summary>A panel cut in two with a dovetail between (24 × 24).</summary>
    const string SplitIcon = "M2 5 H11 V9 L13.5 8 V16 L11 15 V19 H2 Z M22 5 H13 V7.2 L15.5 6.2 V17.8 L13 16.8 V19 H22 Z";

    IAddonHost _host = null!;
    BoxesWindow? _box;

    public void Start(IAddonHost host)
    {
        _host = host;
        host.AddRibbonButton(new RibbonButton("Import", "Generate", "Boxes", BoxIcon, OpenBox)
        {
            Large = true,
            Tip = "Boxes that slot together: finger-jointed with lift-off, sliding or hinged lids and dividers, rounded with bent corners, or with a lid that bends over the back",
            Order = 15,
        });
        host.AddRibbonButton(new RibbonButton("Home", "Cut lines", "Split with joints", SplitIcon, OpenSplit)
        {
            Tip = "Cut the selected outline into pieces that fit the bed, joined back together with dovetails, jigsaw knobs or fingers",
            Enabled = () => Splittable().Count != 0,
            Order = 20,
        });
        host.AddCommand(new AddonCommand("boxes.open", "Shapes", "Box maker…", "", OpenBox));
        host.AddCommand(new AddonCommand("boxes.split", "Shapes", "Split with joints…", "", OpenSplit));
        host.AddContextMenuItem(new ContextMenuEntry("Edit box…", OpenBox) { Visible = () => Placement.Find(_host.Document.State, _host.Document.Selection) is not null });
        host.AddContextMenuItem(new ContextMenuEntry("Split with joints…", OpenSplit) { Visible = () => Splittable().Count != 0 });
    }

    /// <summary>The selected outlines that can be split: closed, on their own.</summary>
    List<PathShape> Splittable()
    {
        var sel = _host.Document.Selection;
        if (sel.Count == 0) return [];
        return [.. _host.Document.State.Shapes.OfType<PathShape>().Where(s => sel.Contains(s.Id) && s.Geometry.Subpaths.Any() && s.Geometry.Subpaths.All(p => p.Closed))];
    }

    void OpenBox()
    {
        if (_box is { IsVisible: true }) { _box.Activate(); return; }
        var editing = Placement.Find(_host.Document.State, _host.Document.Selection);
        var window = new BoxesWindow(_host.Document, _host.Machine, _host.Library, _host.DataFolder, editing);
        _box = window;
        window.Closed += (_, _) => { if (ReferenceEquals(_box, window)) _box = null; };
        _ = _host.Guard(() => window.ShowDialog(_host.Owner));
    }

    void OpenSplit()
    {
        var shapes = Splittable();
        if (shapes.Count == 0) { _host.Status("Select a closed outline to split first."); return; }
        var window = new SplitWindow(_host.Document, _host.Machine, shapes);
        _ = _host.Guard(() => window.ShowDialog(_host.Owner));
    }

    public void Stop()
    {
        _box?.Close();
        _box = null;
    }
}
