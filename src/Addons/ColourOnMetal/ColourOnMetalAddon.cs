using PhotonCanvas.App.Addons;
using PhotonCanvas.Doc;

namespace PhotonCanvas.Addon.ColourOnMetal;

/// <summary>
/// Colour on metal (MOPA on stainless or titanium): Laser → Colour test and Measure colours, and
/// Picture → Turn it into → Colour on metal, which uses the measured palettes.
/// </summary>
public sealed class ColourOnMetalAddon : IAddon
{
    IAddonHost _host = null!;

    public void Start(IAddonHost host)
    {
        _host = host;
        // Settings → Equipment can still hide them on machines without a MOPA.
        bool Shown() => host.AppSettings.ShowColourOnMetal;
        host.AddRibbonButton(new RibbonButton("Picture", "Turn it into", "Colour on metal", "IcPalette", () =>
        {
            if (host.SelectedPicture is { } im) _ = host.Guard(() => MakePicture(im));
        })
        {
            Tip = "The picture in the colours your plate really made (a measured palette): one layer per colour, MOPA on stainless or titanium",
            Large = true,
            Enabled = () => host.SelectedPicture is not null,
            Visible = Shown,
        });
        host.AddRibbonButton(new RibbonButton("Laser", "Colour on metal", "Colour test", "IcPalette", () => _ = host.Guard(AddTest))
        {
            Tip = "Add a MOPA colour test grid: one setting across, another down, to see which colours your plate really gives",
            Large = true,
            Visible = Shown,
            GroupAfter = "Materials",
        });
        host.AddRibbonButton(new RibbonButton("Laser", "Colour on metal", "Measure colours", "IcEyedropper", () => _ = host.Guard(Measure))
        {
            Tip = "Read the colours of a burned colour test from a scan or photo, and save them as a palette",
            Large = true,
            Visible = Shown,
            GroupAfter = "Materials",
        });
    }

    public void Stop() { }

    /// <summary>Colour layers over a picture from a measured palette; the picture stops being sent when it's alone on its layer.</summary>
    async Task MakePicture(ImageShape picture)
    {
        var w = new ColourPictureWindow(picture, _host.Library);
        await w.ShowDialog(_host.Owner);
        if (w.Result is not { } r) return;
        var doc = _host.Document;
        var (state, added) = PhotonCanvas.Doc.Colour.ColourPicture.Add(doc.State, picture, r.Palette, r.Options);
        if (added.Count == 0) return;
        bool alone = state.ShapesOn(picture.LayerId).Count() == 1;
        if (alone) state = Ops.UpdateLayers(state, new HashSet<Guid> { picture.LayerId }, l => l with { Output = false });
        doc.Commit("Colour picture", state, added);
        _host.Status($"Added {added.Count} colour layers." + (alone ? " The picture's own layer is now a guide only (not sent)." : " The picture shares its layer, which is still sent: turn its output off if you only want the colours."));
    }

    async Task AddTest()
    {
        var w = new ColourTestWindow(_host.AppSettings.TextFont);
        await w.ShowDialog(_host.Owner);
        if (w.Result is not { } o) return;
        var doc = _host.Document;
        var (state, added) = PhotonCanvas.Doc.Colour.ColourTest.Add(doc.State, o);
        doc.Commit("Colour test", state, added);
        _host.FitWorkArea();
    }

    async Task Measure()
    {
        var w = new MeasureColourWindow(_host.Document.State, _host.Library, _host.Photo, _host.FocusDistanceMm, _host.MachinePanel);
        await w.ShowDialog(_host.Owner);
        _host.RefreshLibrary();
    }
}
