using PhotonCanvas.App.Addons;
using PhotonCanvas.Doc;

namespace PhotonCanvas.Addon.ImageMask;

/// <summary>Picture → Shape → Image mask and Remove mask: set or clear a picture's <see cref="ImageShape.Clip"/>.</summary>
public sealed class ImageMaskAddon : IAddon
{
    IAddonHost _host = null!;

    public void Start(IAddonHost host)
    {
        _host = host;
        host.AddRibbonButton(new RibbonButton("Picture", "Shape", "Image mask", "IcIntersect", Mask)
        {
            Tip = "Select a picture and a closed shape: the shape masks the picture, so only the part inside it is shown and engraved",
            Large = true,
            GroupAfter = "Turn it into",
            Enabled = () => host.SelectedPicture is not null || Selected().OfType<ImageShape>().Any(),
        });
        host.AddRibbonButton(new RibbonButton("Picture", "Shape", "Remove mask", "IcExclude", Unmask)
        {
            Tip = "Take the image mask off: the whole picture is shown and engraved again",
            Enabled = () => host.SelectedPicture is { Clip: not null },
        });
    }

    List<Shape> Selected() => [.. _host.Document.State.Shapes.Where(s => _host.Document.Selection.Contains(s.Id))];

    void Mask()
    {
        var doc = _host.Document;
        var sel = Selected();
        var pic = sel.OfType<ImageShape>().FirstOrDefault();
        var outline = sel.OfType<PathShape>().FirstOrDefault(p => p.World.Subpaths.Any(sp => sp.Closed));
        if (pic is not null && outline is null && sel.OfType<PathShape>().Any())
        {
            _host.Status("Image mask: the selected shape isn't a closed outline (its ends don't join), so it can't mask the picture. Close it with Tidy lines first, or draw a closed shape.");
            return;
        }
        if (pic is null || outline is null) { _host.Status("Select a picture and the closed shape to mask it with."); return; }
        doc.Commit("Image mask", Ops.Replace(doc.State, pic.WithClip(new PathGeometry(outline.World.Subpaths.Where(sp => sp.Closed)))), [pic.Id]);
        _host.Status("Image mask: only the part inside the shape is shown and engraved. The shape is still there (delete it or turn its layer's output off if it shouldn't be marked).");
    }

    void Unmask()
    {
        if (_host.SelectedPicture is { Clip: not null } im) _host.Document.Commit("Remove image mask", Ops.Replace(_host.Document.State, im.WithClip(null)), [im.Id]);
    }

    public void Stop() { }
}
