using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Clipper2Lib;
using PhotonCanvas.App.View3D;
using Geo = PhotonCanvas.Addon.Walls.Geo;

namespace PhotonCanvas.Addon.HouseEditor;

/// <summary>
/// How much of the walls the 3D view shows: all of them and the roofs, all of them with the roofs off, the ones between
/// you and the rooms cut away (and the roofs off), or none (just the columns and floors).
/// </summary>
public enum WallsShown { Up, RoofOff, CutAway, Down }

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

    /// <summary>What's picked: a warm orange, so it stands out from the wood and stone.</summary>
    static readonly SlabStyle PickedLook = LookOf(Color.FromRgb(0xF0, 0x9A, 0x48));

    static readonly SlabStyle Wood = LookOf(Color.FromRgb(0xDD, 0xBE, 0x92)), Stone = LookOf(Color.FromRgb(0xD2, 0xC6, 0xB4)),
        Column = LookOf(Color.FromRgb(0xC4, 0xB6, 0xA2)), Roof = LookOf(Color.FromRgb(0xB9, 0x9A, 0x86));

    public HouseView() : base(35, 32) { }

    PlanPick? _pick;
    HouseDesign? _pickDesign;
    // The pieces of what's picked (worked out each time the building is drawn again).
    Func<HouseSolid, bool>? _picked;

    /// <summary>A click on the building: the piece clicked (null for none) and where on it (mm, in the building).</summary>
    public event Action<HouseSolid?, Furniture.V3>? Picked;

    /// <summary>Shows what's picked on the plan picked in 3D too (in orange).</summary>
    public void ShowPicked(PlanPick? pick, HouseDesign design)
    {
        bool same = pick == _pick;
        _pick = pick;
        _pickDesign = design;
        if (!same) Build();
    }

    protected override string Hint => "Click to pick · drag to turn · middle-drag or arrows to move · scroll to zoom";

    protected override void Clicked(Point at, KeyModifiers keys)
    {
        if (_kit is null) return;
        var hit = HitAt(at);
        Picked?.Invoke(hit?.Tag as HouseSolid, hit is { } h ? new Furniture.V3(h.Point.X, h.Point.Y, h.Point.Z) : default);
    }

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
        _picked = _pick is { } pick && _pickDesign is { } design && _kit is { } k ? HousePicking.Matcher(design, k, pick) : null;
        if (_kit is { } kit)
        {
            var groupOf = new Dictionary<(double, double, double, int), int>();
            // The roofs come off unless everything's shown with them on, to see in.
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
            foreach (var p in kit.Props.Where(p => p.Level <= UpTo)) AddBanana(V(p.At), p.TurnDeg);
        }
        InvalidateVisual();
    }

    static Vec3 V(Furniture.V3 v) => new(v.X, v.Y, v.Z);

    void Add(HouseSolid s, int group, bool faces = true, bool sides = true)
    {
        double T = s.Thickness;
        var p = DoorsOpen && s.Open is { } open ? open : s.Pose;
        var style = s.Kind switch { SolidKind.Floor or SolidKind.Furniture or SolidKind.Railing => Wood, SolidKind.Roof => Roof, SolidKind.Column => Column, _ => Stone };
        if (_picked?.Invoke(s) == true) style = PickedLook;
        var outline = Geo.Tidy(Clipper.SimplifyPaths(s.Outline, 0.05));
        var frame = new Frame3(V(p.O), V(p.U), V(p.V));
        Scene.AddSlab(outline, frame, -T / 2, T / 2, style, s.Etched, s.Lines, group: Math.Min(group, 63), faces: faces, sides: sides, etchDepth: EtchDepth,
            backEtched: s.BackEtched, backScored: s.BackLines, tag: s);
    }

    // ---- a banana for scale ----

    static readonly Color BananaYellow = Color.FromRgb(0xF0, 0xCF, 0x3A), BananaGreen = Color.FromRgb(0x9A, 0xA8, 0x3C), BananaBrown = Color.FromRgb(0x5A, 0x43, 0x22);

    /// <summary>
    /// A banana lying on the floor at <paramref name="at"/>, life size: a curved, slightly five-sided tube,
    /// tapered to a stalk at one end and a dark tip at the other, its ends curling up off the floor.
    /// </summary>
    void AddBanana(Vec3 at, double turnDeg)
    {
        const int Along = 28, Round = 10;
        double arc = 190, radius = 125, theta = arc / radius;
        double turn = turnDeg * Math.PI / 180, ct = Math.Cos(turn), st = Math.Sin(turn);
        // The banana's own x along it, z towards its inside curve, y up; turned about the upright and moved into place.
        Vec3 Place(double x, double y, double z) => at + new Vec3(x * ct + z * st, y, -x * st + z * ct);
        static double Smooth(double a, double b, double t) { t = Math.Clamp((t - a) / (b - a), 0, 1); return t * t * (3 - 2 * t); }
        // How thick it is along its length: a thin stalk, the body, a narrow tip.
        static double Thick(double t) => 3.5 + 13.5 * Smooth(0.06, 0.3, t) * (1 - Smooth(0.82, 1, t)) + 1.2 * Smooth(0.95, 1, t);
        var rings = new Vec3[Along + 1][];
        var middles = new Vec3[Along + 1];
        double sag = radius * (1 - Math.Cos(theta / 2));
        for (int i = 0; i <= Along; i++)
        {
            double t = (double)i / Along, a = (t - 0.5) * theta;
            double r = Thick(t);
            // The spine: an arc bowed away from z, its ends lifted.
            var spine = new Vec3(radius * Math.Sin(a), 17 + 14 * Math.Pow(2 * t - 1, 2), radius * (1 - Math.Cos(a)) - sag / 2);
            var side = new Vec3(-Math.Sin(a), 0, Math.Cos(a));
            var up = new Vec3(0, 1, 0);
            middles[i] = Place(spine.X, spine.Y, spine.Z);
            rings[i] = new Vec3[Round];
            for (int j = 0; j < Round; j++)
            {
                double phi = 2 * Math.PI * j / Round, k = r * (1 + 0.07 * Math.Cos(5 * phi));
                var p = spine + side * (Math.Cos(phi) * k) + up * (Math.Sin(phi) * k * 0.92);
                rings[i][j] = Place(p.X, p.Y, p.Z);
            }
        }
        var light = Light.Unit;
        Color Lit(Color c, Vec3 n) => Scene3D.Shade(c, 0.58 + 0.42 * Math.Max(0, n.Dot(light)));
        Color ColourAt(double t) => t < 0.07 ? BananaGreen : t > 0.96 ? BananaBrown : BananaYellow;
        void Tri(Vec3 a, Vec3 b, Vec3 c, Vec3 middle, Color colour)
        {
            var n = (b - a).Cross(c - a);
            if (n.Length < 1e-9) return;
            n = n.Unit;
            if (n.Dot((a + b + c) * (1.0 / 3) - middle) < 0) n = -n;
            Scene.AddFace([[a, b, c]], n, Lit(colour, n));
        }
        for (int i = 0; i < Along; i++)
        {
            var colour = ColourAt((i + 0.5) / Along);
            var middle = (middles[i] + middles[i + 1]) * 0.5;
            for (int j = 0; j < Round; j++)
            {
                int j2 = (j + 1) % Round;
                Tri(rings[i][j], rings[i + 1][j], rings[i + 1][j2], middle, colour);
                Tri(rings[i][j], rings[i + 1][j2], rings[i][j2], middle, colour);
            }
        }
        // The ends closed.
        foreach (var (i, colour, outward) in new[] { (0, BananaBrown, middles[0] - middles[1]), (Along, BananaBrown, middles[Along] - middles[Along - 1]) })
        {
            var n = outward.Unit;
            Scene.AddFace([rings[i]], n, Lit(colour, n));
        }
    }
}
