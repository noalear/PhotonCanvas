using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Clipper2Lib;
using PhotonCanvas.Addon.Walls;
using PhotonCanvas.App;
using PhotonCanvas.App.View3D;

namespace PhotonCanvas.Addon.Stairs;

/// <summary>
/// The floor and the way up put together in the room, drawn in 3D from above and to one side: every
/// piece at its thickness where it sits, the etched face showing its detail, and the walls and their
/// columns faint behind. Drag to turn it round, scroll to zoom, middle-drag or the arrow keys to move it; the floor
/// can be lifted off to see under it.
/// </summary>
public sealed class AssembledView : Model3DView
{
    bool _liftFloor;

    /// <summary>The floor's group in the scene, left out when it's lifted off.</summary>
    const int FloorGroup = 1;

    static readonly SlabStyle Look = new(
        SlabStyle.Lit(Color.FromRgb(0xDD, 0xBE, 0x92), new Vec3(0.35, 0.85, 0.45)),
        ((ISolidColorBrush)PiecesView.EdgeBrush).Color, LineWidth.Px(0.7),
        ((ISolidColorBrush)PiecesView.Burnt).Color,
        ((ISolidColorBrush)PiecesView.ScoreBrush).Color, new LineWidth(0.05, 0.3, 0.9));

    /// <summary>The walls and their columns: faint, see-through, so the stairs show behind them.</summary>
    static readonly SlabStyle GhostLook = Look with
    {
        Paint = Faint(SlabStyle.Lit(Color.FromRgb(0xB8, 0xB8, 0xC0), new Vec3(0.35, 0.85, 0.45))),
        Edge = Color.FromArgb(0x70, 0x60, 0x60, 0x70),
        EdgeWidth = LineWidth.Px(0.6),
    };

    static Func<Vec3, bool, Color> Faint(Func<Vec3, bool, Color> paint) => (n, side) => { var c = paint(n, side); return Color.FromArgb(0x80, c.R, c.G, c.B); };

    public AssembledView() : base(215, 34) { }

    /// <summary>Leave the floor out of the picture, to see what's under it.</summary>
    public bool LiftFloor
    {
        get => _liftFloor;
        set { _liftFloor = value; InvalidateVisual(); }
    }

    /// <summary>Shows a kit put together (or, with none, a message saying why there's nothing to show).</summary>
    public void Show(FloorKit? kit, double thickness, string? message = null)
    {
        Scene.Clear();
        Message = message;
        if (kit is not null)
        {
            foreach (var g in kit.Ghosts)
                Scene.AddSlab(Geo.Tidy(Clipper.SimplifyPaths(g.Outline, 0.02)), Frame(g.Pose), -g.Thick / 2, g.Thick / 2, GhostLook);
            double T = thickness;
            foreach (var part in kit.Parts)
            {
                var outline = Geo.Tidy(Clipper.SimplifyPaths(part.Cut, 0.02));
                foreach (var pose in part.Poses)
                    Scene.AddSlab(outline, Frame(pose), -T / 2, T / 2, Look, part.Etched.Count > 0 ? part.Etched : null, part.Lines.Count > 0 ? part.Lines : null,
                        group: part.Name.StartsWith("Floor", StringComparison.Ordinal) ? FloorGroup : 0);
            }
        }
        InvalidateVisual();
    }

    static Frame3 Frame(Pose p) => new(new(p.O.X, p.O.Y, p.O.Z), new(p.U.X, p.U.Y, p.U.Z), new(p.V.X, p.V.Y, p.V.Z));

    protected override ulong HiddenGroups => _liftFloor ? 1UL << FloorGroup : 0;
}
