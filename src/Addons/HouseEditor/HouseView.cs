using Avalonia.Media;
using Clipper2Lib;
using PhotonCanvas.App.View3D;
using Geo = PhotonCanvas.Addon.Walls.Geo;

namespace PhotonCanvas.Addon.HouseEditor;

/// <summary>How much of the walls the 3D view shows: all of them and the roofs, the ones between you and the rooms cut away (and the roofs off), or none (just the columns and floors).</summary>
public enum WallsShown { Up, CutAway, Down }

/// <summary>
/// The building put together in 3D: every piece at its thickness where it sits, the etched faces showing
/// their texture. With the walls cut away, the walls on the near side that face you (or face away from
/// you) are left out wherever it's turned, so you can see into the rooms; it can also show the floors only
/// up to one of them.
/// </summary>
public sealed class HouseView : Model3DView
{
    HouseKit? _kit;
    Camera3D _camera;
    /// <summary>Each group of wall pieces (a wall each, up to 63): which way it faces and where its middle is.</summary>
    readonly List<(Vec3 Facing, Vec3 Middle)> _groups = [];

    public WallsShown Walls { get; private set; } = WallsShown.CutAway;
    /// <summary>The top floor shown (0 = the ground floor); everything above is left out.</summary>
    public int UpTo { get; private set; } = int.MaxValue;
    /// <summary>How deep the engraving is shown sunk into the pieces (mm); 0 paints it on flat.</summary>
    public double EtchDepth { get; private set; } = 0.6;

    static readonly Vec3 Light = new(0.35, 0.85, 0.45);

    static SlabStyle LookOf(Color colour) => new(
        SlabStyle.Lit(colour, Light),
        Color.FromRgb(0x4A, 0x33, 0x1E), LineWidth.Px(0.6),
        Color.FromRgb(0x7E, 0x5A, 0x38),
        Color.FromRgb(0x6B, 0x47, 0x28), new LineWidth(0.05, 0.25, 0.8));

    static readonly SlabStyle Wood = LookOf(Color.FromRgb(0xDD, 0xBE, 0x92)), Stone = LookOf(Color.FromRgb(0xD2, 0xC6, 0xB4)),
        Column = LookOf(Color.FromRgb(0xC4, 0xB6, 0xA2)), Roof = LookOf(Color.FromRgb(0xB9, 0x9A, 0x86));

    public HouseView() : base(35, 32) { }

    /// <summary>Shows a building (or, with none, a message saying why there's nothing to show).</summary>
    public void Show(HouseKit? kit, string? message = null)
    {
        _kit = kit;
        Message = message;
        Build();
    }

    public void SetWalls(WallsShown walls)
    {
        Walls = walls;
        Build();
    }

    /// <summary>The doors shown swung open (and portcullises up), to see which way they go.</summary>
    public bool DoorsOpen { get; private set; }

    public void SetDoorsOpen(bool open)
    {
        DoorsOpen = open;
        Build();
    }

    public void SetEtchDepth(double mm)
    {
        EtchDepth = Math.Max(0, mm);
        Build();
    }

    public void SetUpTo(int level)
    {
        UpTo = level;
        Build();
    }

    protected override Camera3D CameraAt(double yaw, double pitch) => _camera = Camera3D.YUp(yaw, pitch);

    /// <summary>The walls in front: those on your side of the middle that face towards you or away.</summary>
    protected override ulong HiddenGroups
    {
        get
        {
            if (Walls != WallsShown.CutAway) return 0;
            var t = new Vec3(_camera.Toward.X, 0, _camera.Toward.Z).Unit;
            ulong hide = 0;
            for (int g = 0; g < _groups.Count; g++)
            {
                var (f, m) = _groups[g];
                double facing = Math.Abs(f.Dot(t)), near = new Vec3(m.X, 0, m.Z).Dot(t);
                if (facing > 0.38 && near > -2) hide |= 1UL << (g + 1);
            }
            return hide;
        }
    }

    void Build()
    {
        Scene.Clear();
        _groups.Clear();
        if (_kit is { } kit)
        {
            var groupOf = new Dictionary<(double, double, double, int), int>();
            // The roofs come off with the walls cut away or down, to see in.
            var shown = kit.Solids.Where(s => s.Level <= UpTo && !(Walls == WallsShown.Down && s.Kind == SolidKind.Wall) && !(Walls != WallsShown.Up && s.Kind == SolidKind.Roof)).ToList();
            foreach (var s in shown.Where(s => s.Kind != SolidKind.Ground))
            {
                int group = 0;
                if (s.Kind == SolidKind.Wall || (s.Kind == SolidKind.Furniture && s.Facing.Length > 0))
                {
                    var key = (Math.Round(s.Middle.X, 1), Math.Round(s.Middle.Z, 1), Math.Round(s.Middle.Y, 1), s.Level);
                    if (!groupOf.TryGetValue(key, out group))
                    {
                        // Past 63 walls, walls share a group: the last one's direction decides.
                        if (_groups.Count < 63) _groups.Add((V(s.Facing), V(s.Middle)));
                        group = groupOf[key] = _groups.Count;
                    }
                    // Only the top floor's walls are cut away when floors above are hidden; below, the near walls go too.
                }
                Add(s, group);
            }
            // The floor tiles lie edge to edge: their sides are drawn only round the outside of them all.
            var tiles = shown.Where(s => s.Kind == SolidKind.Ground).ToList();
            foreach (var t in tiles) Add(t, 0, sides: false);
            if (tiles.Count > 0)
            {
                var first = tiles[0];
                var all = new PathsD();
                foreach (var t in tiles)
                {
                    var d = t.Pose.O - first.Pose.O;
                    all.AddRange(Geo.Move(t.Outline, d.Dot(first.Pose.U), d.Dot(first.Pose.V)));
                }
                Add(first with { Outline = Geo.Offset(Geo.Offset(Geo.Union(all), 0.4), -0.4) }, 0, faces: false);
            }
        }
        InvalidateVisual();
    }

    static Vec3 V(Furniture.V3 v) => new(v.X, v.Y, v.Z);

    void Add(HouseSolid s, int group, bool faces = true, bool sides = true)
    {
        double T = s.Thickness;
        var p = DoorsOpen && s.Open is { } open ? open : s.Pose;
        var style = s.Kind switch { SolidKind.Floor or SolidKind.Furniture or SolidKind.Railing => Wood, SolidKind.Roof => Roof, SolidKind.Column => Column, _ => Stone };
        var outline = Geo.Tidy(Clipper.SimplifyPaths(s.Outline, 0.05));
        var frame = new Frame3(V(p.O), V(p.U), V(p.V));
        Scene.AddSlab(outline, frame, -T / 2, T / 2, style, s.Etched, s.Lines, group: Math.Min(group, 63), faces: faces, sides: sides, etchDepth: EtchDepth);
    }
}
