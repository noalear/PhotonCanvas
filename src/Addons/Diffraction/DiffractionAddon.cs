using PhotonCanvas.App.Addons;
using PhotonCanvas.Doc;

namespace PhotonCanvas.Addon.DiffractionPictures;

/// <summary>Picture → Turn it into → Diffraction: grating layers over a picture (MOPA on polished stainless).</summary>
public sealed class DiffractionAddon : IAddon
{
    IAddonHost _host = null!;

    public void Start(IAddonHost host)
    {
        _host = host;
        host.AddRibbonButton(new RibbonButton("Picture", "Turn it into", "Diffraction", "IcPrism", () =>
        {
            if (host.SelectedPicture is { } im) _ = host.Guard(() => Make(im));
        })
        {
            Tip = "Turn the picture into tiny diffraction gratings (MOPA on polished stainless): it shimmers with rainbow colours as it tilts",
            Large = true,
            Enabled = () => host.SelectedPicture is not null,
            // Settings → Equipment can still hide it for machines without a MOPA.
            Visible = () => host.AppSettings.ShowDiffraction,
        });
    }

    public void Stop() { }

    /// <summary>Grating layers over a picture; the picture itself stops being sent when it's alone on its layer.</summary>
    async Task Make(ImageShape picture)
    {
        var w = new DiffractionWindow(picture);
        await w.ShowDialog(_host.Owner);
        if (w.Result is not { } o) return;
        var doc = _host.Document;
        var (state, added) = PhotonCanvas.Doc.Colour.Diffraction.Add(doc.State, picture, o);
        if (added.Count == 0) return;
        bool alone = state.ShapesOn(picture.LayerId).Count() == 1;
        if (alone) state = Ops.UpdateLayers(state, new HashSet<Guid> { picture.LayerId }, l => l with { Output = false });
        doc.Commit("Diffraction picture", state, added);
        _host.Status(alone
            ? $"Added {added.Count} grating layers. The picture's own layer is now a guide only (not sent)."
            : $"Added {added.Count} grating layers. The picture shares its layer, which is still sent: turn its output off if you only want the gratings.");
    }
}
