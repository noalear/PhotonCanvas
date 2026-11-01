using PhotonCanvas.App.Addons;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;

namespace PhotonCanvas.Addon.Relief;

/// <summary>Picture → Relief: a 3D model (STL) as a depth map on its own slices layer, and Relief preview.</summary>
public sealed class ReliefAddon : IAddon
{
    IAddonHost _host = null!;
    readonly List<ReliefPreviewWindow> _open = [];

    public void Start(IAddonHost host)
    {
        _host = host;
        host.AddRibbonButton(new RibbonButton("Picture", "Relief", "3D model", "IcDepth", () => _ = host.Guard(AddModel))
        {
            Tip = "A 3D model (STL) as a depth map, on a depth-slices layer",
            Large = true,
            GroupAfter = "Edit",
        });
        host.AddRibbonButton(new RibbonButton("Picture", "Relief", "Relief preview", "IcEye", Preview)
        {
            Tip = "See a depth map as the lit relief it will carve",
            Enabled = () => host.SelectedPicture is not null,
            GroupAfter = "Edit",
        });
        host.AddCommand(new AddonCommand("picture.stl", "Shapes", "3D model relief…", "", () => _ = host.Guard(AddModel)));
    }

    public void Stop()
    {
        foreach (var w in _open.ToList()) w.Close();
        _open.Clear();
    }

    void Preview()
    {
        if (_host.SelectedPicture is not { } im) return;
        var w = new ReliefPreviewWindow(im);
        _open.Add(w);
        w.Closed += (_, _) => _open.Remove(w);
        w.Show(_host.Owner);
    }

    async Task AddModel()
    {
        var w = new StlWindow();
        await w.ShowDialog(_host.Owner);
        if (w.Result is not { } r) return;
        var doc = _host.Document;
        var state = doc.State;
        var layer = Layer.Default(state.Layers.Count, LayerMode.Image) with
        {
            Name = $"Relief · {r.Name}", ImageMode = ImageEngraveMode.Slices, Dpi = 254, LinesPerCm = 200, AngleStepDeg = 37,
        };
        state = Ops.AddLayer(state, layer);
        var m = _host.Machine;
        var at = new Vec2(m.FieldW / 2 - r.SizeMm.X / 2, m.FieldH / 2 - r.SizeMm.Y / 2);
        var pic = new ImageShape { Name = r.Name, LayerId = layer.Id, Source = ImageSource.FromEncoded(r.Depth.EncodePng()), SizeMm = r.SizeMm, Transform = Affine.Translate(at) };
        doc.Commit("3D model relief", Ops.AddShapes(state, [pic]), [pic.Id]);
        doc.SetActiveLayer(layer.Id);
    }
}
