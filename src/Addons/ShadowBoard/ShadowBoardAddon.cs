using PhotonCanvas.App.Addons;
using PhotonCanvas.Core;
using PhotonCanvas.Core.Imaging;
using PhotonCanvas.Doc;

namespace PhotonCanvas.Addons.ShadowBoard;

/// <summary>
/// The shadow board maker as an add-on: a button on the Laser tab and a command, both gone again
/// when it's turned off. Only the camera is used (a photo of the bed): it never moves the head or
/// fires the laser.
/// </summary>
public sealed class ShadowBoardAddon : IAddon
{
    IAddonHost _host = null!;
    ShadowBoardWindow? _open;

    public void Start(IAddonHost host)
    {
        _host = host;
        host.AddRibbonButton(new RibbonButton("Laser", "Make", "Shadow board", "IcFitPiece", Open)
        {
            Tip = "Lay your tools on the bed: the camera outlines them and makes a drawer insert with a pocket, finger notches and a label for each",
        });
        host.AddCommand(new AddonCommand("shadowboard.open", "Shapes", "Shadow board…", "", Open));
    }

    void Open() => _ = _host.Guard(async () =>
    {
        if (_open is { } w) { w.Activate(); return; }
        _open = new ShadowBoardWindow(new Host(_host));
        _open.Closed += (_, _) => _open = null;
        await _open.ShowDialog(_host.Owner);
    });

    public void Stop()
    {
        _open?.Close();
        _open = null;
    }

    /// <summary>What the window needs, from the add-on host.</summary>
    sealed class Host(IAddonHost h) : IShadowBoardHost
    {
        public Document Document => h.Document;

        public IReadOnlyList<PathShape> SelectedPaths =>
            [.. h.Document.State.Shapes.Where(s => h.Document.Selection.Contains(s.Id)).OfType<PathShape>().Where(p => p.Geometry.Subpaths.Any(sp => sp.Closed))];

        public Rect2 WorkArea => new(0, 0, h.Machine.FieldW, h.Machine.FieldH);

        public double? MeasuredDistanceMm => h.FocusDistanceMm;

        public (bool Ready, string Status) CameraStatus(double distanceMm) =>
            !h.IsConnected ? (false, "Connect to the machine first.")
            : !h.CameraCalibratedFor(distanceMm) ? (false, "The camera isn't calibrated near this height yet (Camera tab → Calibrate).")
            : (true, "Camera ready.");

        public async Task<RasterImage> PhotographBedAsync(Rect2 area, double mmPerPx, double distanceMm)
        {
            using var bmp = await h.PhotographAreaAsync(area, mmPerPx, distanceMm);
            return await Task.Run(() =>
            {
                var px = bmp.Pixels;
                var rgba = new byte[px.Length * 4];
                for (int i = 0; i < px.Length; i++)
                {
                    rgba[i * 4] = px[i].Red; rgba[i * 4 + 1] = px[i].Green; rgba[i * 4 + 2] = px[i].Blue; rgba[i * 4 + 3] = px[i].Alpha;
                }
                return new RasterImage(bmp.Width, bmp.Height, rgba);
            });
        }

        public IReadOnlyList<ShadowRecipe> CutRecipes =>
            [.. h.Library.Materials.SelectMany(m => m.Recipes.Where(r => r.Settings.Mode == LayerMode.Cut)
                .Select(r => new ShadowRecipe($"{m.Label} · {r.Title}", r.Settings.ApplyTo(Layer.Default(0, LayerMode.Cut)))))];

        public IReadOnlyList<ShadowRecipe> EngraveRecipes =>
            [.. h.Library.Materials.SelectMany(m => m.Recipes.Where(r => r.Settings.Mode == LayerMode.Fill)
                .Select(r => new ShadowRecipe($"{m.Label} · {r.Title}" + (r.EtchMmPerPass is { } e ? $" ({e:0.###} mm a pass)" : ""),
                    r.Settings.ApplyTo(Layer.Default(0, LayerMode.Fill)), r.EtchMmPerPass)))];
    }
}
