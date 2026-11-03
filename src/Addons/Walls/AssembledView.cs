using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Clipper2Lib;
using PhotonCanvas.Addon.Walls.Model;
using PhotonCanvas.App;
using PhotonCanvas.App.View3D;

namespace PhotonCanvas.Addon.Walls;

/// <summary>
/// One wall put together, with a column at each end, drawn in 3D from in front, above and to one side:
/// every piece at its thickness where it sits, the etched faces showing their texture. Drag to turn it round,
/// scroll to zoom, middle-drag or the arrow keys to move it.
/// </summary>
public sealed class AssembledView : Model3DView
{
    WallKit? _kit;
    double _thickness;
    bool _open;

    /// <summary>Doors and shutters swung open, a portcullis slid up.</summary>
    public bool Open
    {
        get => _open;
        set { if (_open == value) return; _open = value; Show(_kit, _thickness, Message); }
    }

    /// <summary>The wall has a door or shutters that open, or a portcullis.</summary>
    public bool HasMoving => _kit?.Parts.Any(p => p.HingeU is not null || p.SlideMm > 0) == true;

    static readonly SlabStyle Look = new(
        SlabStyle.Lit(Color.FromRgb(0xDD, 0xBE, 0x92), new Vec3(0.35, 0.85, 0.45)),
        Color.FromRgb(0x4A, 0x33, 0x1E), LineWidth.Px(0.6),
        Color.FromRgb(0x8C, 0x61, 0x37),
        Color.FromRgb(0x6B, 0x47, 0x28), new LineWidth(0.05, 0.3, 0.9));

    public AssembledView() : base(28, 22) { }

    /// <summary>Shows a kit put together (or, with none, a message saying why there's nothing to show).</summary>
    public void Show(WallKit? kit, double thickness, string? message = null)
    {
        Scene.Clear();
        Message = message;
        _kit = kit;
        _thickness = thickness;
        if (kit is not null)
        {
            double T = thickness;
            foreach (var part in kit.Parts)
            {
                if (part.Poses.Count == 0) continue;
                var outline = Geo.Tidy(Clipper.SimplifyPaths(part.Cut, 0.02));
                foreach (var pose in part.Poses)
                    Scene.AddSlab(outline, Frame(_open ? BoxWall.Opened(part, pose) : pose), -T / 2, T / 2, Look, part.Etched.Count > 0 ? part.Etched : null, part.Lines.Count > 0 ? part.Lines : null);
            }
            if (Scene.IsEmpty) Message ??= "Nothing to show.";
        }
        InvalidateVisual();
    }

    static Frame3 Frame(Pose p) => new(new(p.O.X, p.O.Y, p.O.Z), new(p.U.X, p.U.Y, p.U.Z), new(p.V.X, p.V.Y, p.V.Z));
}
