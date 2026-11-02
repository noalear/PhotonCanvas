using PhotonCanvas.App.Addons;
using PhotonCanvas.Core.Imaging;
using PhotonCanvas.Doc;

namespace PhotonCanvas.Addon.CmykSplit;

/// <summary>Picture → Edit → CMYK split: four ink plates, each on its own halftone layer.</summary>
public sealed class CmykSplitAddon : IAddon
{
    IAddonHost _host = null!;

    public void Start(IAddonHost host)
    {
        _host = host;
        host.AddRibbonButton(new RibbonButton("Picture", "Edit", "CMYK split", "IcLayers", () => { if (host.SelectedPicture is { } im) Split(im); })
        {
            Tip = TipText,
            Enabled = () => host.SelectedPicture is not null,
        });
    }

    const string TipText = "Four pictures, one per ink (cyan, magenta, yellow, black), each on its own layer: for screen printing";

    void Split(ImageShape im)
    {
        var doc = _host.Document;
        // Separated as the picture looks with its tone settings; each plate keeps its clip and sharpening.
        var plates = PictureTools.CmykSplit(im.Source.Pixels, im.Tone);
        var plateTone = ToneAdjust.None with { Sharpen = im.Tone.Sharpen, SharpenRadiusMm = im.Tone.SharpenRadiusMm };
        var state = doc.State;
        var made = new List<Shape>();
        uint[] colours = [0x00A3E0, 0xE0007A, 0xF2C800, 0x222222];
        for (int i = 0; i < plates.Length; i++)
        {
            var layer = Layer.Default(state.Layers.Count, LayerMode.Image) with { Name = $"{plates[i].Ink} · {im.Name}", Color = colours[i], Dither = DitherMethod.Halftone, HalftoneAngleDeg = new[] { 15.0, 75, 0, 45 }[i] };
            state = Ops.AddLayer(state, layer);
            var plate = new ImageShape { Name = $"{plates[i].Ink} plate", LayerId = layer.Id, Source = ImageSource.FromEncoded(plates[i].Plate.EncodePng()), SizeMm = im.SizeMm, Transform = im.Transform, Clip = im.Clip, Tone = plateTone };
            made.Add(plate);
        }
        doc.Commit("CMYK split", Ops.AddShapes(state, made), made.Select(m => m.Id));
        _host.Status("Four ink layers added (halftone at the usual screen angles). Mark each on its own screen; Laser → Layer by layer can ship them one layer at a time.");
    }

    public void Stop() { }
}
