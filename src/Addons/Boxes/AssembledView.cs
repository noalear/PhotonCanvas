using Avalonia.Media;
using Clipper2Lib;
using PhotonCanvas.App.View3D;

namespace PhotonCanvas.Addon.Boxes;

/// <summary>
/// The box put together, in 3D: every piece at the sheet's thickness where it sits, a bent wall
/// or lid drawn as narrow flat stretches round its bend, the living hinge's slits on it. The lid
/// can be shown open (lifted off, slid out or swung up). Drag to turn it round, scroll to zoom,
/// middle-drag or the arrow keys to move it.
/// </summary>
public sealed class AssembledView : Model3DView
{
    static readonly SlabStyle Look = new(
        SlabStyle.Lit(Color.FromRgb(0xDD, 0xBE, 0x92), new Vec3(0.35, 0.85, 0.45)),
        Color.FromRgb(0x4A, 0x33, 0x1E), LineWidth.Px(0.7),
        Color.FromRgb(0x8C, 0x61, 0x37),
        Color.FromRgb(0x6B, 0x47, 0x28), new LineWidth(0.05, 0.3, 0.9));

    BoxKit? _kit;
    double _open;

    public AssembledView() : base(32, 26) { }

    /// <summary>How far the lid is open: 0 shut, 1 as far as it goes.</summary>
    public double LidOpen
    {
        get => _open;
        set { _open = Math.Clamp(value, 0, 1); Rebuild(); }
    }

    /// <summary>Shows a box put together (or, with none, a message saying why there's nothing to show).</summary>
    public void Show(BoxKit? kit, string? message = null)
    {
        _kit = kit;
        Message = message;
        Rebuild();
    }

    void Rebuild()
    {
        Scene.Clear();
        if (_kit is { } kit)
        {
            double t = kit.ThicknessMm;
            foreach (var part in kit.Parts)
            {
                var outline = Geo.Tidy(Clipper.SimplifyPaths(part.Cut, 0.02));
                Pose Moved(Pose p) => part.Group == 1 && _open > 0 ? kit.Lid.Apply(p, _open) : p;
                if (part.Facets is { } facets)
                    foreach (var (clip, pose) in facets)
                    {
                        var bit = Geo.Intersect(outline, clip);
                        if (bit.Count == 0) continue;
                        var lines = Geo.ClipLines(part.Slits, clip);
                        Scene.AddSlab(bit, Frame(Moved(pose)), -t / 2, t / 2, Look, null, lines.Count > 0 ? lines : null);
                    }
                else
                    foreach (var pose in part.Poses)
                        Scene.AddSlab(outline, Frame(Moved(pose)), -t / 2, t / 2, Look, null, part.Slits.Count > 0 ? part.Slits : null);
            }
        }
        InvalidateVisual();
    }

    static Frame3 Frame(Pose p) => new(new(p.O.X, p.O.Y, p.O.Z), new(p.U.X, p.U.Y, p.U.Z), new(p.V.X, p.V.Y, p.V.Z));
}
