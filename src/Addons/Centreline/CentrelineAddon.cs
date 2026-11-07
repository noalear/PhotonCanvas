using PhotonCanvas.App.Addons;
using PhotonCanvas.Doc;
using PhotonCanvas.Doc.Tracing;

namespace PhotonCanvas.Addon.CentrelineTrace;

/// <summary>Picture → Trace → Centre-line trace: one line down the middle of each dark stroke, on a Score layer.</summary>
public sealed class CentrelineAddon : IAddon
{
    IAddonHost _host = null!;

    public void Start(IAddonHost host)
    {
        _host = host;
        host.AddRibbonButton(new RibbonButton("Picture", "Trace", "Centre-line trace", "IcPen", () => { if (host.SelectedPicture is { } im) _ = host.Guard(() => Trace(im)); })
        {
            Tip = TipText,
            Enabled = () => host.SelectedPicture is not null,
            Order = 10,
        });
    }

    const string TipText = "One line down the middle of each stroke (handwriting, line art), on a Score layer";

    async Task Trace(ImageShape im)
    {
        var src = im.Source.Pixels;
        var tone = im.Tone;
        var (geometry, n) = await Task.Run(() =>
        {
            var ink = new bool[src.Width * src.Height];
            for (int i = 0; i < ink.Length; i++)
            {
                double lum = (0.2126 * src.Rgba[i * 4] + 0.7152 * src.Rgba[i * 4 + 1] + 0.0722 * src.Rgba[i * 4 + 2]) / 255;
                ink[i] = src.Rgba[i * 4 + 3] > 127 && tone.Apply(lum) < 0.5;
            }
            var lines = Centreline.Trace(ink, src.Width, src.Height);
            return (Centreline.ToGeometry(lines, im.PixelToWorld), lines.Count);
        });
        if (n == 0) { _host.Status("No dark strokes found to trace."); return; }
        var doc = _host.Document;
        var state = doc.State;
        var layer = state.Layers.FirstOrDefault(l => l.Mode == LayerMode.Score && l.Visible && !l.Locked);
        if (layer is null)
        {
            layer = Layer.Default(state.Layers.Count, LayerMode.Score) with { Name = "Centre lines" };
            state = Ops.AddLayer(state, layer);
        }
        var shape = new PathShape { Name = $"Centre lines · {im.Name}", LayerId = layer.Id, Geometry = geometry };
        doc.Commit("Centre-line trace", Ops.AddShapes(state, [shape]), [shape.Id]);
        _host.Status($"Traced {n} line{(n == 1 ? "" : "s")} down the middle of the strokes.");
    }

    public void Stop() { }
}
