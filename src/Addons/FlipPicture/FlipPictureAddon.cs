using PhotonCanvas.App.Addons;
using PhotonCanvas.Doc;

namespace PhotonCanvas.Addon.Flip;

/// <summary>
/// Flip pictures on metal: a Picture → Flip picture button that weaves two to four selected
/// pictures into one polished stainless plate, each showing from its own tilt.
/// </summary>
public sealed class FlipPictureAddon : IAddon
{
    IAddonHost _host = null!;
    FlipPictureWindow? _open;

    public void Start(IAddonHost host)
    {
        _host = host;
        host.AddRibbonButton(new RibbonButton("Picture", "Effects", "Flip picture", "IcPrism", Open)
        {
            Large = true,
            Tip = "Two to four pictures in one polished stainless plate, each seen from its own tilt (MOPA): select 2–4 pictures",
            Enabled = () => SelectedPictures().Count is >= 2 and <= FlipOptions.MaxViews,
            Order = 15,
        });
        host.AddCommand(new AddonCommand("flip-picture.open", "Pictures", "Flip picture…", "", Open));
    }

    public void Stop()
    {
        _open?.Close();
        _open = null;
    }

    /// <summary>The selected pictures, in the order they were added (picture A first).</summary>
    List<ImageShape> SelectedPictures()
    {
        var doc = _host.Document;
        return doc.State.Shapes.OfType<ImageShape>().Where(s => doc.Selection.Contains(s.Id)).ToList();
    }

    void Open() => _ = _host.Guard(Run);

    /// <summary>Flip layers over the first selected picture; the pictures stop being sent when they're alone on their layers.</summary>
    async Task Run()
    {
        var pictures = SelectedPictures();
        if (pictures.Count is < 2 or > FlipOptions.MaxViews)
        {
            _host.Status($"Select two to {FlipOptions.MaxViews} pictures first (Shift-click), then Flip picture.");
            return;
        }
        if (_host.Machine.Mopa.Count == 0)
        {
            _host.Status("Flip pictures need a MOPA (fiber) laser: choose a machine that has one.");
            return;
        }
        var w = _open = new FlipPictureWindow(pictures, _host.Library, _host.Machine);
        try { await w.ShowDialog(_host.Owner); }
        finally { _open = null; }
        if (w.Result is not { } o) return;
        var doc = _host.Document;
        var (state, added) = FlipPicture.Add(doc.State, pictures, o, _host.Machine);
        if (added.Count == 0) return;
        var alone = pictures.Select(p => p.LayerId).Distinct()
            .Where(id => state.ShapesOn(id).All(x => x is ImageShape im && pictures.Any(p => p.Id == im.Id))).ToHashSet();
        if (alone.Count > 0) state = Ops.UpdateLayers(state, alone, l => l with { Output = false });
        doc.Commit("Flip picture", state, added);
        _host.Status($"Added {added.Count} flip layers over picture A." + (alone.Count > 0
            ? " The pictures' own layers are now guides only (not sent)."
            : " The pictures share a layer with other shapes, which is still sent: turn its output off if you only want the gratings."));
    }
}
