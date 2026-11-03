using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Clipper2Lib;
using PhotonCanvas.Addon.Furniture;
using PhotonCanvas.App;
using PhotonCanvas.App.View3D;
using Geo = PhotonCanvas.Addon.Walls.Geo;

namespace PhotonCanvas.Addon.Buildings;

/// <summary>What the 3D view shows of the building.</summary>
public enum ViewMode { Whole, CutAway, Storey }

/// <summary>
/// The building put together, drawn in 3D from above and to one side: every piece at its thickness where
/// it sits, the etched faces showing their texture. Drag to turn it round, scroll to zoom, middle-drag or the
/// arrow keys to move it. It can cut the front and
/// right-hand walls (and the roof) away to show the rooms, or show one storey on its own.
/// </summary>
public sealed class BuildingView : Model3DView
{
    BuildingKit? _kit;

    public ViewMode Mode { get; private set; } = ViewMode.Whole;
    public int Storey { get; private set; }

    static readonly Vec3 Light = new(0.35, 0.85, 0.45);

    static SlabStyle LookOf(Color colour) => new(
        SlabStyle.Lit(colour, Light),
        Color.FromRgb(0x4A, 0x33, 0x1E), LineWidth.Px(0.6),
        Color.FromRgb(0x7E, 0x5A, 0x38),
        Color.FromRgb(0x6B, 0x47, 0x28), new LineWidth(0.05, 0.25, 0.8));

    static readonly SlabStyle Wood = LookOf(Color.FromRgb(0xDD, 0xBE, 0x92)), Stone = LookOf(Color.FromRgb(0xD2, 0xC6, 0xB4));

    public BuildingView() : base(32, 26) { }

    /// <summary>Shows a building (or, with none, a message saying why there's nothing to show).</summary>
    public void Show(BuildingKit? kit, string? message = null)
    {
        _kit = kit;
        Message = message;
        Build();
    }

    public void SetMode(ViewMode mode, int storey = 0)
    {
        Mode = mode;
        Storey = storey;
        Build();
    }

    bool Shown(Solid s) => Mode switch
    {
        ViewMode.CutAway => s.Side is not (WallSide.Front or WallSide.Right or WallSide.Roof),
        ViewMode.Storey => s.Storey == Storey && s.Side is not (WallSide.Front or WallSide.Right or WallSide.Roof),
        // Inside the roof, out of sight: left out, to save drawing them.
        _ => !(s.Side == WallSide.Roof && (s.Name.StartsWith("Roof truss", StringComparison.Ordinal) || s.Name.StartsWith("Roof locator", StringComparison.Ordinal))),
    };

    void Build()
    {
        Scene.Clear();
        if (_kit is { } kit)
        {
            var shown = kit.Solids.Where(Shown).ToList();
            foreach (var s in shown) Add(s, sides: s.Side != WallSide.Ground);
            // The floor tiles lie edge to edge: their sides are drawn only round the outside of them all
            // (the sides between them can't be seen).
            if (shown.FirstOrDefault(s => s.Side == WallSide.Ground) is { } first)
            {
                var all = new PathsD();
                foreach (var t in shown.Where(s => s.Side == WallSide.Ground))
                {
                    var d = t.Pose.O - first.Pose.O;
                    all.AddRange(Geo.Move(t.Outline, d.Dot(first.Pose.U), d.Dot(first.Pose.V)));
                }
                // Grown and shrunk back, to close the fit gaps between the jigsaw joins.
                Add(first with { Outline = Geo.Offset(Geo.Offset(Geo.Union(all), 0.4), -0.4) }, faces: false);
            }
        }
        InvalidateVisual();
    }

    void Add(Solid s, bool faces = true, bool sides = true)
    {
        double T = s.Thickness;
        var p = s.Pose;
        bool wood = s.IsFurniture || s.Side is WallSide.Floor or WallSide.Roof;
        var outline = Geo.Tidy(Clipper.SimplifyPaths(s.Outline, 0.05));
        var frame = new Frame3(new(p.O.X, p.O.Y, p.O.Z), new(p.U.X, p.U.Y, p.U.Z), new(p.V.X, p.V.Y, p.V.Z));
        Scene.AddSlab(outline, frame, -T / 2, T / 2, wood ? Wood : Stone, s.Etched, s.Lines, faces: faces, sides: sides);
    }
}
