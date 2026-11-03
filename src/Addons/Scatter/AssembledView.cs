using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Clipper2Lib;
using PhotonCanvas.App;
using PhotonCanvas.App.View3D;

namespace PhotonCanvas.Addon.Scatter;

/// <summary>
/// One piece of scenery put together, drawn in 3D from above and to one side: every piece at its
/// thickness where it sits, the etched face showing its detail. Drag to turn it round, scroll to zoom,
/// middle-drag or the arrow keys to move it.
/// </summary>
public sealed class AssembledView : Model3DView
{
    static readonly SlabStyle Look = new(
        SlabStyle.Lit(Color.FromRgb(0xDD, 0xBE, 0x92), new Vec3(0.35, 0.85, 0.45)),
        ((ISolidColorBrush)PiecesView.EdgeBrush).Color, LineWidth.Px(0.7),
        ((ISolidColorBrush)PiecesView.Burnt).Color,
        ((ISolidColorBrush)PiecesView.ScoreBrush).Color, new LineWidth(0.05, 0.3, 0.9));

    public AssembledView() : base(32, 24) { }

    /// <summary>Shows a kit put together (or, with none, a message saying why there's nothing to show).</summary>
    public void Show(ItemKit? kit, double thickness, string? message = null)
    {
        Scene.Clear();
        Message = message;
        if (kit is not null)
        {
            double T = thickness;
            foreach (var part in kit.Parts)
            {
                var outline = Geo.Tidy(Clipper.SimplifyPaths(part.Cut, 0.02));
                foreach (var pose in part.Poses)
                    Scene.AddSlab(outline, Frame(pose), -T / 2, T / 2, Look, part.Etched.Count > 0 ? part.Etched : null, part.Lines.Count > 0 ? part.Lines : null);
            }
        }
        InvalidateVisual();
    }

    static Frame3 Frame(Pose p) => new(new(p.O.X, p.O.Y, p.O.Z), new(p.U.X, p.U.Y, p.U.Z), new(p.V.X, p.V.Y, p.V.Z));
}
