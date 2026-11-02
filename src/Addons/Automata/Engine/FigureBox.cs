using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Clipper2Lib;
using PhotonCanvas.Addon.Mechanism;
using PhotonCanvas.Core;
using Mech = PhotonCanvas.Addon.Mechanism.Mechanism;

namespace PhotonCanvas.Addon.Automata;

/// <summary>How a figure on the box moves.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<FigureMove>))]
public enum FigureMove
{
    /// <summary>Rides straight up and down on its cam's rod.</summary>
    Bob,
    /// <summary>On the end of a lever that its cam's rod tips up and down: nods, pecks, waves, hammers.</summary>
    Nod,
    /// <summary>On a turntable driven round by a wheel on the shaft.</summary>
    Spin,
}

/// <summary>One figure on the box and what moves it.</summary>
public sealed record FigureStation
{
    public FigureMove Move { get; init; } = FigureMove.Bob;
    /// <summary>Which cut-out (<see cref="Figures"/>); empty for none.</summary>
    public string Figure { get; init; } = "bird";
    /// <summary>The figure's height (mm).</summary>
    public double FigureMm { get; init; } = 40;
    /// <summary>Face it the other way.</summary>
    public bool Flip { get; init; }

    // ---- a cam (Bob and Nod) ----
    public ImmutableList<CamStep> Steps { get; init; } = CamPresets.Default;
    public double BaseRadiusMm { get; init; } = 22;
    public FollowerFoot Foot { get; init; } = FollowerFoot.Round;
    public double FootMm { get; init; } = 12;
    /// <summary>How far into its steps this cam is when the handle starts (degrees).</summary>
    public double StartDeg { get; init; }

    // ---- a lever (Nod) ----
    /// <summary>From the rod that pushes the lever to the lever's pivot (mm).</summary>
    public double PivotMm { get; init; } = 28;
    /// <summary>From the pivot to where the figure stands on the lever (mm).</summary>
    public double ArmMm { get; init; } = 48;
    /// <summary>The figure is on the far side of the pivot, so it dips as the rod rises (a peck, a hammer blow).</summary>
    public bool Dips { get; init; }

    // ---- a turntable (Spin) ----
    /// <summary>The drive wheel's radius where its rubber band touches the turntable (mm).</summary>
    public double WheelMm { get; init; } = 20;
    /// <summary>From the turntable's spindle to where the wheel touches it (mm): smaller spins faster.</summary>
    public double OffsetMm { get; init; } = 14;

    public string MoveWords => Move switch { FigureMove.Bob => "bobs", FigureMove.Nod => Dips ? "pecks" : "nods", _ => "spins" };
}

/// <summary>
/// A crank box with cut-out figures on top: turning the handle turns a shaft along the box, and each figure is
/// moved by its own cam (a rod that bobs it up and down, or tips a lever it stands on) or by a wheel on the
/// shaft that drives a turntable round under it. The box goes together with tabs in slots like the mechanism
/// maker's automaton box; the rods slide in slots in the top and a shelf under it.
/// </summary>
public sealed record FigureBoxSpec : ToySpec
{
    public ImmutableList<FigureStation> Stations { get; init; } = Presets[0].Stations;
    /// <summary>The shaft. Square by default: it grips the cams and wheels and sets their timing.</summary>
    public Axle Axle { get; init; } = new() { Square = true };
    /// <summary>How far the handle is from the shaft (mm).</summary>
    public double CrankMm { get; init; } = 28;
    /// <summary>The handle turns clockwise (looking at the handle's side) instead of anticlockwise.</summary>
    public bool Clockwise { get; init; }
    /// <summary>The dowel the levers pivot on (mm across).</summary>
    public double PinMm { get; init; } = 3;
    /// <summary>The dowel the turntables turn on (mm across).</summary>
    public double SpindleMm { get; init; } = 5;
    /// <summary>The rubber band or O-ring cord round each drive wheel (mm thick).</summary>
    public double BandMm { get; init; } = 2.5;

    public const int MaxStations = 6;
    /// <summary>Room for rods and spindles to slide or turn in their slots and holes (mm).</summary>
    public const double SlideGap = 0.4;
    /// <summary>How tall the joint is where a figure slots onto its rod (mm).</summary>
    public const double JointMm = 14;
    /// <summary>How wide a lever is (mm).</summary>
    public const double LeverW = 10;
    /// <summary>How wide each of a lever's posts is (mm).</summary>
    public const double PostW = 14;

    /// <summary>Ready-made boxes to start from.</summary>
    public sealed record Preset(string Name, ImmutableList<FigureStation> Stations);

    static readonly ImmutableList<CamStep> Hop = CamPresets.All[5].Steps;
    static readonly ImmutableList<CamStep> UpDown = CamPresets.All[0].Steps;
    static readonly ImmutableList<CamStep> TwoBumps = CamPresets.All[2].Steps;

    public static readonly ImmutableArray<Preset> All = Presets;

    static ImmutableArray<Preset> Presets =>
    [
        new("A bit of everything", [
            new() { Move = FigureMove.Bob, Figure = "fish", Steps = TwoBumps, FigureMm = 30 },
            new() { Move = FigureMove.Nod, Figure = "bird", Dips = true, Steps = Hop, StartDeg = 90, FigureMm = 26, ArmMm = 24, PivotMm = 20 },
            new() { Move = FigureMove.Spin, Figure = "person", FigureMm = 42 },
        ]),
        new("Pecking birds", [
            new() { Move = FigureMove.Nod, Figure = "bird", Dips = true, Steps = Hop, FigureMm = 30, ArmMm = 30, PivotMm = 24 },
            new() { Move = FigureMove.Nod, Figure = "bird", Dips = true, Steps = Hop, StartDeg = 150, FigureMm = 30, ArmMm = 30, PivotMm = 24 },
        ]),
        new("Boats on the waves", [
            new() { Move = FigureMove.Bob, Figure = "boat", Steps = UpDown, FigureMm = 36 },
            new() { Move = FigureMove.Bob, Figure = "boat", Steps = UpDown, StartDeg = 120, FigureMm = 36, Flip = true },
            new() { Move = FigureMove.Bob, Figure = "boat", Steps = UpDown, StartDeg = 240, FigureMm = 36 },
        ]),
        new("Dancer and flowers", [
            new() { Move = FigureMove.Bob, Figure = "flower", Steps = UpDown, FigureMm = 36 },
            new() { Move = FigureMove.Spin, Figure = "person", FigureMm = 46 },
            new() { Move = FigureMove.Bob, Figure = "flower", Steps = UpDown, StartDeg = 180, FigureMm = 36, Flip = true },
        ]),
        new("Nodding cat", [
            new() { Move = FigureMove.Nod, Figure = "cat", Steps = CamPresets.All[1].Steps, FigureMm = 36, ArmMm = 52, PivotMm = 28 },
        ]),
        new("Rocket launch", [
            new() { Move = FigureMove.Bob, Figure = "rocket", Steps = CamPresets.All[6].Steps, FigureMm = 50, Foot = FollowerFoot.Round, FootMm = 10, BaseRadiusMm = 24 },
            new() { Move = FigureMove.Spin, Figure = "star", FigureMm = 30, OffsetMm = 10 },
        ]),
    ];

    /// <summary>The preset these stations are, or null when they've been changed.</summary>
    public static Preset? Find(IReadOnlyList<FigureStation> stations) =>
        All.FirstOrDefault(p => p.Stations.Count == stations.Count && p.Stations.Zip(stations).All(z => Same(z.First, z.Second)));

    /// <summary>The same settings (a record's own equality would compare the lists of steps by reference).</summary>
    public static bool Same(FigureStation a, FigureStation b) => a with { Steps = [] } == b with { Steps = [] } && a.Steps.SequenceEqual(b.Steps);

    /// <summary>A station's cam, lever or turntable worked out, and where it sits along the box.</summary>
    sealed class Plan
    {
        public required FigureStation S;
        public required int I;
        public CamDesign? Cam;
        public double Start;
        public Paths64? Figure;
        public Rect2 FigureBox;
        /// <summary>Where along the box its rod (or drive wheel) is.</summary>
        public double X;
        /// <summary>How far it reaches either side of <see cref="X"/>.</summary>
        public double Left, Right;
        public double BarW = 8;
        /// <summary>From the foot's centre (or face) up to the rod's top.</summary>
        public double RodLen;
        // A lever: its shape (pivot at the origin, along +x towards the rod), the pivot's height, and the tilt round the turn.
        public Paths64? Lever;
        public double PivotZ;
        public double[] Tilt = [];
        // A turntable.
        public double TableR, WheelMid, Flange, Ratio, PlatformR;
        public double SpindleX => X + S.OffsetMm;
    }

    public override Mech Build(CancellationToken ct = default)
    {
        int n = Stations.Count;
        if (n < 1) throw new InvalidOperationException("Add a figure.");
        if (n > MaxStations) throw new InvalidOperationException($"Use at most {MaxStations} figures.");
        int dir = Clockwise ? -1 : 1;
        double t = ThicknessMm, fit = SlotFitMm, fitLen = 0.2;
        var mech = new Mech { Title = "Automaton" };
        var bore = new Bore(null, Axle, false, Material);
        double heldR = bore.Shaft.OuterR;
        const int S = CamDesign.Samples;

        // ---- each station's moving parts, worked out on their own ----
        var plans = new List<Plan>();
        for (int i = 0; i < n; i++)
        {
            ct.ThrowIfCancellationRequested();
            var s = Stations[i];
            var p = new Plan { S = s, I = i, Start = s.StartDeg * Math.PI / 180 };
            // A pecking figure faces away from its pivot, so its head is what dips.
            p.Figure = Figures.Make(s.Figure, Math.Clamp(s.FigureMm, 10, 200), s.Flip ^ (s.Move == FigureMove.Nod && s.Dips));
            p.FigureBox = p.Figure is null ? new Rect2(0, 0, 0, 0) : Poly.Bounds(p.Figure);
            if (s.Move != FigureMove.Spin)
            {
                try { p.Cam = new CamDesign(s.Steps, s.BaseRadiusMm, s.Foot, s.FootMm, dir, ct); }
                catch (InvalidOperationException e) { throw new InvalidOperationException($"Figure {i + 1}'s cam: {char.ToLowerInvariant(e.Message[0])}{e.Message[1..]}", e); }
                p.BarW = p.Cam.Foot == FollowerFoot.Round ? Math.Clamp(1.6 * p.Cam.FootR, 6, 10) : 10;
            }
            else
            {
                double c = Math.Clamp(BandMm, 1, 6);
                p.WheelMid = Math.Max(heldR + 5, s.WheelMm - c);
                p.Flange = p.WheelMid + c / 2;
                double offset = s.OffsetMm;
                p.Ratio = (p.WheelMid + c) / Math.Max(1, offset);
                p.TableR = Math.Ceiling(offset + 1.5 * t + 6);
                p.PlatformR = Math.Max(14, SpindleMm / 2 + 8);
            }
            plans.Add(p);
        }
        var cams = plans.Where(p => p.Cam is not null).ToList();
        var spins = plans.Where(p => p.S.Move == FigureMove.Spin).ToList();

        // ---- heights: the shaft, the shelf, the top ----
        double rMax = Math.Max(cams.Count > 0 ? cams.Max(p => p.Cam!.MaxRadius) : 0, spins.Count > 0 ? spins.Max(p => p.Flange) : 0);
        // High enough for the cams and wheels, and for the handle to clear the base at the bottom of its turn.
        double crankW = Math.Max(12, 2 * heldR + 8);
        double zs = Math.Max(Math.Ceiling(rMax + 5), Math.Ceiling(CrankMm + crankW / 2 + 2));
        double hiFoot = cams.Count > 0 ? cams.Max(p => zs + p.Cam!.Height.Max() + p.Cam.FootTop) : 0;
        double hiTable = spins.Count > 0 ? spins.Max(p => zs + p.WheelMid + Math.Clamp(BandMm, 1, 6) + t) : 0;
        double zShelf = Math.Ceiling(Math.Max(hiFoot + 5, hiTable + 4));
        double guide = Math.Max(16, 5 * t);
        double H = zShelf + t + guide;
        double topSurf = H + t;

        // ---- rods, levers, turntables: how long, how high, how far they reach along the box ----
        foreach (var p in plans)
        {
            var s = p.S;
            double hw = p.Figure is null ? 0 : Math.Max(-p.FigureBox.MinX, p.FigureBox.MaxX);
            switch (s.Move)
            {
                case FigureMove.Bob:
                {
                    double plinth = PlinthW(p.BarW);
                    p.RodLen = topSurf + JointMm + 2 - (zs + p.Cam!.Height.Min());
                    double reach = Math.Max(Math.Max(hw, plinth / 2), t / 2 + 6);
                    p.Left = p.Right = reach + 3;
                    break;
                }
                case FigureMove.Nod:
                {
                    var d = p.Cam!;
                    double a = Math.Max(14, s.PivotMm), arm = Math.Max(0, s.ArmMm), h = LeverW / 2;
                    // The lever rests on the rod's top: level when the rod is half way up.
                    double lo = d.Height.Min(), hi = d.Height.Max(), mid = (lo + hi) / 2;
                    p.Tilt = new double[S];
                    for (int k = 0; k < S; k++) p.Tilt[k] = TiltFor(a, d.Height[k] - mid - h, h);
                    // Long enough that the rod's top stays under it, however far it tips (the rod meets it further out as it tips).
                    double steep = Math.Min(Math.Max(-p.Tilt.Min(), p.Tilt.Max()), 1.2);
                    double u0 = s.Dips ? -arm - 6 : -6, u1 = Math.Max(Math.Max(a + 8, a / Math.Cos(steep) + 6), s.Dips ? 0 : arm + 8);
                    var lever = new Paths64 { Shapes.RoundRect(u0, -h, u1, h, 3) };
                    if (p.Figure is not null)
                    {
                        double at = s.Dips ? -arm : arm;
                        var fig = Poly.Move(p.Figure, 0, at, h - 0.5);
                        lever = Poly.Union(lever, fig);
                    }
                    p.Lever = lever;
                    // As low as the lever (and its figure) ever reaches, below the pivot.
                    double low = 0, minX = 0, maxX = 0;
                    foreach (var th in new[] { p.Tilt.Min(), p.Tilt.Max(), 0.0 })
                    {
                        var b = Poly.Bounds(Poly.Move(lever, th));
                        low = Math.Min(low, b.MinY);
                        minX = Math.Min(minX, b.MinX);
                        maxX = Math.Max(maxX, b.MaxX);
                    }
                    // Pivot high enough that nothing hits the top; the rod is as long as that makes it.
                    double needPivot = topSurf + 2 - low;
                    double rodTopMid = needPivot - h;
                    p.RodLen = Math.Max(rodTopMid - (zs + mid), topSurf + 2 - (zs + lo));
                    p.PivotZ = zs + mid + p.RodLen + h;
                    // Along the box, measured from the rod.
                    p.Left = Math.Max(-(minX - a), a + PostW / 2) + 3;
                    p.Right = Math.Max(maxX - a, t / 2 + 6) + 3;
                    break;
                }
                default:
                {
                    double reach = Math.Max(Math.Max(p.TableR, hw), p.PlatformR);
                    p.Left = Math.Max(1.5 * t + 3, reach - s.OffsetMm) + 3;
                    p.Right = s.OffsetMm + reach + 3;
                    break;
                }
            }
        }

        // ---- along the box: each station in a space of its own, so no two figures ever meet ----
        double cursor = 0;
        foreach (var p in plans)
        {
            double w = Math.Max(24, p.Left + p.Right);
            double extra = w - (p.Left + p.Right);
            p.X = cursor + p.Left + extra / 2;
            cursor += w;
        }
        double W = cursor;
        foreach (var p in plans) p.X -= W / 2;

        double camD = cams.Count > 0 ? cams.Max(p => Math.Max(2 * p.Cam!.MaxRadius + 14, 2 * p.Cam.FootR + 10)) : 0;
        double tableD = spins.Count > 0 ? spins.Max(p => 2 * p.TableR + 10) : 0;
        double D = Math.Ceiling(Math.Max(Math.Max(camD, tableD), 50));
        double maxBar = plans.Max(p => p.BarW);
        double ds = Math.Max(maxBar + 12, Math.Max(20, SpindleMm + 12));
        double stw = Math.Round(ds * 0.6, 1);
        double tw = Math.Round(D / 4, 1);
        double ob = 8, ot = 3;
        double bx = W / 2 + t + ob, by = D / 2 + ob, tx = W / 2 + t + ot;
        double spinHole = SpindleMm / 2 + Fits.Allowance(HoleFit.Free, Material) / 2;
        double pinHeld = PinMm / 2 + Fits.Allowance(HoleFit.Snug, Material) / 2;
        double pinFree = PinMm / 2 + Fits.Allowance(HoleFit.Free, Material) / 2;
        double postY = t + 0.25;   // each post's middle, either side of its lever

        // ---- the box ----
        var sideSlots = new Paths64();
        foreach (var sx in new[] { -1, 1 })
            foreach (var sy in new[] { -1, 1 })
                sideSlots.Add(Shapes.Box(new Vec2(sx * (W / 2 + t / 2), sy * D / 4), t + fit, tw + fitLen));
        var rodSlots = new Paths64(cams.Select(p => Shapes.Box(new Vec2(p.X, 0), t + SlideGap, p.BarW + SlideGap)));
        var spindleHoles = new Paths64(spins.Select(p => Poly.Circle(new Vec2(p.SpindleX, 0), spinHole, 0.003)));
        var postSlots = new Paths64();
        foreach (var p in plans.Where(p => p.S.Move == FigureMove.Nod))
            foreach (var sy in new[] { -1, 1 })
                postSlots.Add(Shapes.Box(new Vec2(p.X - Math.Max(14, p.S.PivotMm), sy * postY), PostW - 4 + fitLen, t + fit));

        var basePart = Poly.Subtract([Shapes.RoundRect(-bx, -by, bx, by, 4)], sideSlots);
        mech.Parts.Add(new Part { Name = "Base", Shape = basePart, Label = new PartLabel("Base", new Vec2(0, -by + ob / 2), 3.5) });
        var topPart = Poly.Subtract([Shapes.RoundRect(-tx, -D / 2, tx, D / 2, 3)], Poly.Union(Poly.Union(sideSlots, rodSlots), Poly.Union(spindleHoles, postSlots)));
        mech.Parts.Add(new Part { Name = "Top", Shape = topPart, Label = new PartLabel("Top", new Vec2(0, -D / 2 + 4.5), 3.5) });
        var shelfPart = Poly.Subtract(
            Poly.Union([Poly.Rect(-W / 2, -ds / 2, W / 2, ds / 2), Poly.Rect(-W / 2 - t, -stw / 2, -W / 2 + 0.01, stw / 2), Poly.Rect(W / 2 - 0.01, -stw / 2, W / 2 + t, stw / 2)]),
            Poly.Union(rodSlots, spindleHoles));
        mech.Parts.Add(new Part { Name = "Shelf", Shape = shelfPart });
        var sideOutline = new Paths64 { Poly.Rect(-D / 2, 0, D / 2, H) };
        foreach (var sy in new[] { -1, 1 })
        {
            sideOutline.Add(Poly.Rect(sy * D / 4 - tw / 2, -t, sy * D / 4 + tw / 2, 0.01));
            sideOutline.Add(Poly.Rect(sy * D / 4 - tw / 2, H - 0.01, sy * D / 4 + tw / 2, H + t));
        }
        var side = Poly.Subtract(Poly.Union(sideOutline), [bore.Frame(new Vec2(0, zs), true), Shapes.Box(new Vec2(0, zShelf + t / 2), stw + fitLen, t + fit)]);
        mech.Parts.Add(new Part { Name = "Side", Shape = side, Copies = 2, Label = zs - heldR > 14 ? new PartLabel("Side", new Vec2(0, 6), 3.5) : null });

        // ---- each station's parts ----
        var camCut = new Dictionary<int, Paths64>();
        var rodSolid = new Dictionary<int, Paths64>();
        var rodPart = new Dictionary<int, Paths64>();
        var camLines = new Dictionary<int, List<List<Vec2>>>();
        var figParts = new Dictionary<int, Paths64>();
        var postPart = new Dictionary<int, Paths64>();
        int bands = 0, coins = 0;
        foreach (var p in plans)
        {
            ct.ThrowIfCancellationRequested();
            int i = p.I;
            string no = $"{i + 1}";
            if (p.Cam is { } d)
            {
                double turn0 = -d.TurnAt(p.Start);
                camCut[i] = Poly.Subtract(d.Outline, [bore.Cut(default, turn0)]);
                double room = d.MinRadius - heldR;
                var lines = new List<List<Vec2>>();
                if (room > 4) lines.Add([Shapes.Polar(heldR + 1.5, turn0), Shapes.Polar(heldR + Math.Min(7, room - 1.5), turn0)]);
                if (room > 7) lines.AddRange(Shapes.TurnArrow(default, heldR + room * 0.55, turn0 + Math.PI - dir * 0.55, turn0 + Math.PI + dir * 0.55));
                camLines[i] = lines;
                mech.Parts.Add(new Part
                {
                    Name = $"Cam {no}",
                    Shape = camCut[i],
                    Marks = lines,
                    Label = room > 8 ? new PartLabel($"Cam {no}", Shapes.Polar(heldR + room * 0.5, turn0 + Math.PI / 2), Math.Clamp(room * 0.22, 2.5, 4)) : null,
                });
                rodSolid[i] = d.FollowerShape(p.BarW, p.RodLen);
                var rod = rodSolid[i];
                if (p.S.Move == FigureMove.Bob && p.Figure is not null)
                    rod = Poly.Subtract(rod, [Poly.Rect(-(t + fit) / 2, p.RodLen - JointMm / 2, (t + fit) / 2, p.RodLen + 1)]);
                rodPart[i] = rod;
                mech.Parts.Add(new Part
                {
                    Name = $"Rod {no}",
                    Shape = rod,
                    Label = new PartLabel(no, new Vec2(0, (d.FootTop + 3 + p.RodLen - JointMm) / 2), Math.Clamp(p.BarW - 3, 2.5, 4), 90),
                });
            }
            switch (p.S.Move)
            {
                case FigureMove.Bob when p.Figure is not null:
                {
                    double plinth = PlinthW(p.BarW);
                    var fig = Poly.Union(Poly.Move(p.Figure, 0, 0, JointMm - 0.5), [Poly.Rect(-plinth / 2, 0, plinth / 2, JointMm)]);
                    fig = Poly.Subtract(fig, [Poly.Rect(-(t + fit) / 2, -1, (t + fit) / 2, JointMm / 2)]);
                    figParts[i] = fig;
                    mech.Parts.Add(new Part { Name = $"Figure {no}", Shape = fig, Label = new PartLabel(no, new Vec2(plinth / 2 - 4, JointMm * 0.72), 3) });
                    break;
                }
                case FigureMove.Nod:
                {
                    double a = Math.Max(14, p.S.PivotMm);
                    var lever = Poly.Subtract(p.Lever!, [Poly.Circle(default, pinFree, 0.003)]);
                    var marks = new List<List<Vec2>>();
                    // The figure's side outweighs the rod's: a coin glued on at the rod's end keeps the lever down on the rod.
                    if (p.S.Dips && Moment(p.Lever!) < 0)
                    {
                        coins++;
                        double cx = Poly.Bounds(p.Lever!).MaxX - 7;
                        marks.Add([.. Enumerable.Range(0, 25).Select(k => Shapes.Polar(3.5, 2 * Math.PI * k / 24) + new Vec2(cx, 0))]);
                    }
                    figParts[i] = lever;
                    mech.Parts.Add(new Part { Name = $"Lever {no}", Shape = lever, Marks = marks, Label = a > 20 ? new PartLabel(no, new Vec2(a / 2, 0), 3.5) : null });
                    // Two posts hold its pivot pin, one either side of it, slotted into the top.
                    double ph = p.PivotZ - topSurf;
                    var post = Poly.Union([Shapes.RoundRect(-PostW / 2, 0, PostW / 2, ph + 7, 3), Poly.Rect(-PostW / 2 + 2, -t, PostW / 2 - 2, 0.01)]);
                    post = Poly.Subtract(post, [Poly.Circle(new Vec2(0, ph), pinHeld, 0.003)]);
                    postPart[i] = post;
                    mech.Parts.Add(new Part { Name = $"Post {no}", Shape = post, Copies = 2 });
                    break;
                }
                case FigureMove.Spin:
                {
                    bands++;
                    double held = SpindleMm / 2 + Fits.Allowance(HoleFit.Snug, Material) / 2;
                    mech.Parts.Add(new Part { Name = $"Drive wheel {no}", Shape = Poly.Subtract([Poly.Circle(default, p.WheelMid, 0.003)], [bore.Cut(default)]) });
                    mech.Parts.Add(new Part { Name = $"Wheel side {no}", Shape = Poly.Subtract([Poly.Circle(default, p.Flange, 0.003)], [bore.Cut(default)]), Copies = 2 });
                    mech.Parts.Add(new Part
                    {
                        Name = $"Turntable {no}",
                        Shape = Poly.Subtract([Poly.Circle(default, p.TableR, 0.003)], [Poly.Circle(default, held, 0.003)]),
                        Marks = [[new Vec2(held + 2, 0), new Vec2(p.TableR - 3, 0)]],
                    });
                    mech.Parts.Add(new Part { Name = $"Stand {no}", Shape = Poly.Subtract([Poly.Circle(default, p.PlatformR, 0.003)], [Poly.Circle(default, held, 0.003)]) });
                    if (p.Figure is not null)
                    {
                        double tab = Math.Min(10, 2 * p.PlatformR - 8);
                        mech.Parts.Add(new Part { Name = $"Stand top {no}", Shape = Poly.Subtract([Poly.Circle(default, p.PlatformR, 0.003)], [Shapes.Box(default, tab + fitLen, t + fit)]) });
                        double plinth = Math.Max(tab + 8, 16);
                        var fig = Poly.Union([Poly.Rect(-plinth / 2, 0, plinth / 2, 4), Poly.Rect(-tab / 2, -t, tab / 2, 0.01)]);
                        fig = Poly.Union(fig, Poly.Move(p.Figure, 0, 0, 3.5));
                        figParts[i] = fig;
                        mech.Parts.Add(new Part { Name = $"Figure {no}", Shape = fig });
                    }
                    break;
                }
            }
        }

        // ---- the handle ----
        double cw = crankW;
        double pegR = Axle.SizeMm / 2;
        var crank = Poly.Subtract(Shapes.Link(default, new Vec2(CrankMm, 0), cw), [bore.Frame(default, false), Poly.Circle(new Vec2(CrankMm, 0), pegR - 0.03, 0.003)]);
        mech.Parts.Add(new Part { Name = "Crank", Shape = crank });
        mech.Parts.Add(new Part { Name = "Collar", Shape = Poly.Subtract([Poly.Circle(default, heldR + 5, 0.003)], [bore.Frame(default, false)]) });

        double shaftLen = W + 2 * t + 2 * (t + 1) + 4;
        mech.Shopping.Add($"A shaft: {bore.ShaftWords} dowel, {Math.Ceiling(shaftLen):0} mm long");
        mech.Shopping.Add($"A handle: {Axle.SizeMm:0.#} mm round dowel, about 30 mm long");
        int levers = plans.Count(p => p.S.Move == FigureMove.Nod);
        if (levers > 0) mech.Shopping.Add($"{(levers == 1 ? "A pivot pin" : $"{levers} pivot pins")}: {PinMm:0.#} mm dowel (a bamboo skewer does), {Math.Ceiling(5 * t + 4):0} mm long{(levers > 1 ? " each" : "")}");
        foreach (var p in spins)
        {
            double len = topSurf + 2 - (zs + p.WheelMid + Math.Clamp(BandMm, 1, 6)) + t;
            mech.Shopping.Add($"For figure {p.I + 1}'s turntable: a spindle, {SpindleMm:0.#} mm round dowel {Math.Ceiling(len):0} mm long");
        }
        if (bands > 0) mech.Shopping.Add($"{(bands == 1 ? "A rubber band or O-ring" : $"{bands} rubber bands or O-rings")} about {BandMm:0.#} mm thick, to grip round {(bands == 1 ? "the drive wheel" : "each drive wheel")} (about {spins.Max(p => 2 * Math.PI * p.WheelMid * 0.8):0} mm round, stretched)");
        if (coins > 0) mech.Shopping.Add(coins == 1 ? "A small coin or washer to glue on the marked circle of the lever" : "Small coins or washers to glue on the marked circles of the levers");
        mech.Shopping.Add("Wood glue");

        // ---- motion: heights and turns round the handle's turn ----
        double RodTop(Plan p, double a) => zs + p.Cam!.HeightAt(a + p.Start) + p.RodLen;
        double TiltAt(Plan p, double a) => Lerp(p.Tilt, a + p.Start);
        double SpinAt(Plan p, double a) => dir * p.Ratio * a;
        double xL = -W / 2 - t - 1 - t, xC = W / 2 + t + 1;

        // ---- from the front: the box, and every figure moving ----
        var front = new List<Body>
        {
            new("Base", [Poly.Rect(-bx, -t, bx, 0)], Tints.Frame, _ => Pose.Zero) { Faint = true },
            new("Side", [Poly.Rect(-W / 2 - t, 0, -W / 2, H)], Tints.Frame, _ => Pose.Zero) { Faint = true },
            new("Side", [Poly.Rect(W / 2, 0, W / 2 + t, H)], Tints.Frame, _ => Pose.Zero) { Faint = true },
            new("Shelf", [Poly.Rect(-W / 2, zShelf, W / 2, zShelf + t)], Tints.Frame, _ => Pose.Zero) { Faint = true },
            new("Top", [Poly.Rect(-tx, H, tx, H + t)], Tints.Frame, _ => Pose.Zero) { Faint = true },
            new("Shaft", [Poly.Rect(xL - 1, zs - bore.Shaft.SizeMm / 2, xC + t + 2, zs + bore.Shaft.SizeMm / 2)], Tints.Steel, _ => Pose.Zero),
            new("Collar", [Poly.Rect(xL, zs - heldR - 5, xL + t, zs + heldR + 5)], Tints.Of(7), _ => Pose.Zero),
        };
        double topMost = topSurf;
        foreach (var p in plans)
        {
            int i = p.I;
            string no = $"{i + 1}";
            uint c = Tints.Of(i), c2 = Tints.Of(i + 4);
            double x = p.X;
            if (p.Cam is { } d)
            {
                front.Add(new Body($"Cam {no}", [Poly.Rect(x - t / 2, zs - d.MaxRadius, x + t / 2, zs + d.MaxRadius)], c, _ => Pose.Zero)
                {
                    ShapeAt = a => [Poly.Rect(x - t / 2, zs + d.BottomAt(a + p.Start), x + t / 2, zs + d.TopAt(a + p.Start))],
                });
                var pp = p;
                front.Add(new Body($"Rod {no}", [Poly.Rect(x - t / 2, -d.FootBottom, x + t / 2, p.RodLen)], c2, a => new Pose(new Vec2(0, zs + d.HeightAt(a + pp.Start)), 0)));
            }
            switch (p.S.Move)
            {
                case FigureMove.Bob when figParts.TryGetValue(i, out var fig):
                {
                    var pp = p;
                    front.Add(new Body($"Figure {no}", fig, c2, a => new Pose(new Vec2(x, RodTop(pp, a) - JointMm), 0)));
                    topMost = Math.Max(topMost, zs + p.Cam!.Height.Max() + p.RodLen - JointMm + Poly.Bounds(fig).MaxY);
                    break;
                }
                case FigureMove.Nod:
                {
                    var pp = p;
                    double px = x - Math.Max(14, p.S.PivotMm);
                    front.Add(new Body($"Post {no}", Poly.Move(postPart[i], 0, px, topSurf), Tints.Frame, _ => Pose.Zero));
                    front.Add(new Body($"Lever {no}", figParts[i], c2, a => new Pose(new Vec2(px, pp.PivotZ), TiltAt(pp, a))));
                    front.Add(new Body("Pin", [Poly.Circle(default, PinMm / 2, 0.01)], Tints.Steel, _ => new Pose(new Vec2(px, pp.PivotZ), 0)));
                    var lb = Poly.Bounds(Poly.Move(p.Lever!, p.Tilt.Max()));
                    var lb2 = Poly.Bounds(Poly.Move(p.Lever!, p.Tilt.Min()));
                    topMost = Math.Max(topMost, p.PivotZ + Math.Max(lb.MaxY, lb2.MaxY));
                    break;
                }
                case FigureMove.Spin:
                {
                    var pp = p;
                    double band = Math.Clamp(BandMm, 1, 6), xs = p.SpindleX, zt = zs + p.WheelMid + band;
                    front.Add(new Body($"Drive wheel {no}", [Poly.Rect(x - 1.5 * t, zs - p.Flange, x + 1.5 * t, zs + p.Flange)], c, _ => Pose.Zero));
                    front.Add(new Body("Band", [Poly.Rect(x - band / 2, zs - p.WheelMid - band, x + band / 2, zs + p.WheelMid + band)], 0x3A3A3A, _ => Pose.Zero));
                    front.Add(new Body($"Turntable {no}", [Poly.Rect(xs - p.TableR, zt, xs + p.TableR, zt + t)], c2, _ => Pose.Zero));
                    double standZ = topSurf + 2;
                    front.Add(new Body("Spindle", [Poly.Rect(xs - SpindleMm / 2, zt, xs + SpindleMm / 2, standZ + t)], Tints.Steel, _ => Pose.Zero));
                    front.Add(new Body($"Stand {no}", [Poly.Rect(xs - p.PlatformR, standZ, xs + p.PlatformR, standZ + 2 * t)], c2, _ => Pose.Zero)
                    {
                        ShapeAt = a => [Poly.Rect(xs - p.PlatformR, standZ, xs + p.PlatformR, standZ + 2 * t)],
                    });
                    if (figParts.TryGetValue(i, out var fig))
                    {
                        var placed = Poly.Move(fig, 0, xs, standZ + 2 * t);
                        front.Add(new Body($"Figure {no}", placed, c2, _ => Pose.Zero)
                        {
                            ShapeAt = a => SquashX(placed, xs, Math.Cos(SpinAt(pp, a))),
                        });
                        topMost = Math.Max(topMost, standZ + 2 * t + Poly.Bounds(fig).MaxY);
                    }
                    break;
                }
            }
        }
        front.Add(new Body("Crank", [Poly.Rect(xC, zs - CrankMm - cw / 2, xC + t, zs + CrankMm + cw / 2)], Tints.Of(7), _ => Pose.Zero)
        {
            ShapeAt = a =>
            {
                double zh = zs + CrankMm * Math.Sin(dir * a);
                return [Poly.Rect(xC, Math.Min(zs, zh) - cw / 2, xC + t, Math.Max(zs, zh) + cw / 2)];
            },
        });
        front.Add(new Body("Handle", [Poly.Rect(xC + t, zs - pegR, xC + t + 25, zs + pegR)], Tints.Steel, a => new Pose(new Vec2(0, CrankMm * Math.Sin(dir * a)), 0)));
        double leftMost = Math.Min(-bx, plans.Min(p => p.X - p.Left));
        double rightMost = Math.Max(xC + t + 25, plans.Max(p => p.X + p.Right));
        mech.Panels.Add(new PlanarPanel
        {
            Title = "From the front",
            Bodies = front,
            Extent = new Rect2(leftMost, -t, rightMost, topMost).Inflate(3),
            Weight = 1.6,
        });

        // ---- from the handle's side: each cam pushing its rod ----
        foreach (var p in cams)
        {
            var d = p.Cam!;
            int i = p.I;
            string no = $"{i + 1}";
            double gap = (p.BarW + SlideGap) / 2;
            var pp = p;
            Pose Up(double a) => new(new Vec2(0, zs + d.HeightAt(a + pp.Start)), 0);
            var bodies = new List<Body>
            {
                new("Side", side, Tints.Frame, _ => Pose.Zero) { Faint = true },
                new("Base", [Poly.Rect(-by, -t, by, 0)], Tints.Frame, _ => Pose.Zero) { Faint = true },
                new("Shelf", [Poly.Rect(-ds / 2, zShelf, -gap, zShelf + t), Poly.Rect(gap, zShelf, ds / 2, zShelf + t)], Tints.Frame, _ => Pose.Zero) { Faint = true },
                new("Top", [Poly.Rect(-D / 2, H, -gap, H + t), Poly.Rect(gap, H, D / 2, H + t)], Tints.Frame, _ => Pose.Zero) { Faint = true },
                new($"Cam {no}", camCut[i], Tints.Of(i), a => new Pose(new Vec2(0, zs), d.TurnAt(a + pp.Start))) { IsInput = true, Lines = camLines[i] },
                new($"Rod {no}", rodPart[i], Tints.Of(i + 4), Up),
                new("Shaft", [bore.ShaftOutline()], Tints.Steel, a => new Pose(new Vec2(0, zs), dir * a)),
            };
            double top = zs + d.Height.Max() + p.RodLen;
            if (p.S.Move == FigureMove.Bob && figParts.TryGetValue(i, out var fig))
            {
                double fh = Poly.Bounds(fig).MaxY;
                bodies.Add(new Body($"Figure {no}", [Poly.Rect(-t / 2, p.RodLen - JointMm, t / 2, p.RodLen - JointMm + fh)], Tints.Of(i + 4), Up));
                top += fh - JointMm;
            }
            else if (p.S.Move == FigureMove.Nod)
            {
                bodies.Add(new Body($"Lever {no}", [Poly.Rect(-t / 2, 0, t / 2, LeverW)], Tints.Of(i + 4), a => new Pose(new Vec2(0, RodTop(pp, a)), 0)));
                foreach (var sy in new[] { -1, 1 })
                    bodies.Add(new Body($"Post {no}", [Poly.Rect(sy * postY - t / 2, H + t, sy * postY + t / 2, p.PivotZ + 7)], Tints.Frame, _ => Pose.Zero) { Faint = true });
                top = Math.Max(top + LeverW, p.PivotZ + 7);
            }
            mech.Panels.Add(new PlanarPanel
            {
                Title = $"Figure {no}'s cam, from the handle's side",
                Bodies = bodies,
                Extent = new Rect2(-by, -t, by, Math.Max(top, H + t)).Inflate(2),
                InputPivot = new Vec2(0, zs),
                InputSense = dir,
            });
        }

        // ---- from above: the turntables going round ----
        if (spins.Count > 0)
        {
            var above = new List<Body>
            {
                new("Top", [Poly.Rect(-W / 2 - t, -D / 2, W / 2 + t, D / 2)], Tints.Frame, _ => Pose.Zero) { Faint = true },
                new("Shaft", [Poly.Rect(xL - 1, -bore.Shaft.SizeMm / 2, xC + t + 2, bore.Shaft.SizeMm / 2)], Tints.Steel, _ => Pose.Zero),
            };
            foreach (var p in spins)
            {
                int i = p.I;
                var pp = p;
                double xs = p.SpindleX;
                above.Add(new Body($"Drive wheel {i + 1}", [Poly.Rect(p.X - 1.5 * t, -p.Flange, p.X + 1.5 * t, p.Flange)], Tints.Of(i), _ => Pose.Zero));
                var table = new Paths64 { Poly.Circle(default, p.TableR, 0.01) };
                above.Add(new Body($"Turntable {i + 1}", table, Tints.Of(i + 4), a => new Pose(new Vec2(xs, 0), SpinAt(pp, a)))
                {
                    Lines = [[new Vec2(SpindleMm / 2 + 2, 0), new Vec2(p.TableR - 2, 0)]],
                });
                if (p.Figure is not null)
                {
                    above.Add(new Body($"Figure {i + 1}", [Poly.Rect(p.FigureBox.MinX, -t / 2, p.FigureBox.MaxX, t / 2)], Tints.Of(i + 4), a => new Pose(new Vec2(xs, 0), SpinAt(pp, a))));
                }
            }
            mech.Panels.Add(new PlanarPanel
            {
                Title = spins.Count == 1 ? "The turntable, from above" : "The turntables, from above",
                Bodies = above,
                Extent = new Rect2(-W / 2 - t - 2, -D / 2 - 2, W / 2 + t + 2, D / 2 + 2),
            });
        }

        // ---- charts ----
        var bobs = plans.Where(p => p.S.Move == FigureMove.Bob).ToList();
        if (bobs.Count > 0)
            mech.Panels.Add(Chart(bobs.Select(p => ($"Figure {p.I + 1}", Tints.Of(p.I + 4), (Func<double, double>)(a => p.Cam!.LiftAt(a + p.Start)))), "How high each figure is", "mm"));
        var nods = plans.Where(p => p.S.Move == FigureMove.Nod).ToList();
        if (nods.Count > 0)
            mech.Panels.Add(Chart(nods.Select(p => ($"Figure {p.I + 1}", Tints.Of(p.I + 4), (Func<double, double>)(a => (p.S.Dips ? -1 : 1) * TiltAt(p, a) * 180 / Math.PI))), "How far each figure tips", "°"));

        // ---- the motion check ----
        double rpm = double.PositiveInfinity;
        foreach (var p in cams)
        {
            ct.ThrowIfCancellationRequested();
            rpm = Math.Min(rpm, p.Cam!.Check(mech, $"Figure {p.I + 1}'s cam", p.Start, heldR, camCut[p.I], rodSolid[p.I]));
        }
        if (cams.Count > 0) mech.Findings.Add(CamDesign.SpeedFinding(rpm, cams.Count > 1));
        foreach (var p in nods)
        {
            double lo = p.Tilt.Min() * 180 / Math.PI, hi = p.Tilt.Max() * 180 / Math.PI;
            double swing = hi - lo;
            string who = $"Figure {p.I + 1}";
            if (Math.Max(-lo, hi) > 55)
            {
                mech.Findings.Add(new(Severity.Bad, $"{who}'s lever tips {Math.Max(-lo, hi):0}° each way: the rod would slip off it. Move the pivot further from the rod, or give the cam less lift."));
                mech.MarkWhere(a => Math.Abs(TiltAt(p, a)) > 55 * Math.PI / 180, Severity.Bad);
            }
            else if (Math.Max(-lo, hi) > 35)
                mech.Findings.Add(new(Severity.Warn, $"{who}'s lever tips {Math.Max(-lo, hi):0}° each way (under 35° runs smoothly); the rod pushes it at a slant. Move the pivot further from the rod for a gentler nod."));
            else
                mech.Findings.Add(new(Severity.Good, $"{who} {(p.S.Dips ? "pecks" : "nods")} through {swing:0}°: its lever tips {Math.Max(-lo, hi):0}° either way of level."));
            if (p.S.Dips && Moment(p.Lever!) < 0)
                mech.Findings.Add(new(Severity.Note, $"{who} is heavier than its lever's other end, so it would lift the lever off the rod: glue a small coin or washer on the circle marked at the rod's end."));
        }
        foreach (var p in spins)
        {
            string who = $"Figure {p.I + 1}";
            double clear = p.S.OffsetMm - (1.5 * t + SpindleMm / 2);
            if (clear < 1)
            {
                mech.Findings.Add(new(Severity.Bad, $"{who}'s drive wheel would rub its spindle: set the turntable's offset to at least {Math.Ceiling(1.5 * t + SpindleMm / 2 + 1):0} mm."));
                mech.Mark(0, Mech.StripSamples, Severity.Bad);
            }
            else if (p.Ratio > 4)
                mech.Findings.Add(new(Severity.Warn, $"{who} turns {p.Ratio:0.#} times for each turn of the handle: that fast, the band may slip. A bigger offset turns it slower and grips better."));
            else
                mech.Findings.Add(new(Severity.Good, $"{who} turns {p.Ratio:0.##} time{(Math.Abs(p.Ratio - 1) < 0.005 ? "" : "s")} for each turn of the handle{(p.Ratio < 0.5 ? ", slowly" : "")}."));
        }
        if (n > 1) mech.Findings.Add(new(Severity.Good, "Each figure has its own space along the box, so they never touch each other."));
        bool crankLow = zs - CrankMm - cw / 2 < 1;
        if (CrankMm < heldR + pegR + 4)
            mech.Findings.Add(new(Severity.Bad, $"The handle is too close to the shaft: make the crank at least {Math.Ceiling(heldR + pegR + 4):0} mm."));
        if (crankLow)
        {
            mech.Findings.Add(new(Severity.Bad, $"The crank hits the base at the bottom of its turn: make it {Math.Floor(zs - cw / 2 - 1):0} mm or shorter."));
            mech.MarkWhere(a => zs + CrankMm * Math.Sin(dir * a) - cw / 2 < 1, Severity.Bad);
        }
        else mech.Findings.Add(new(Severity.Good, "The handle clears the base all the way round."));
        mech.Clashing = a =>
        {
            var names = new List<string>();
            foreach (var p in cams)
                if (p.Cam!.Trouble[CamDesign.Index(a + p.Start)]) names.AddRange([$"Cam {p.I + 1}", $"Rod {p.I + 1}"]);
            if (crankLow && zs + CrankMm * Math.Sin(dir * a) - cw / 2 < 1) names.AddRange(["Crank", "Base"]);
            return names;
        };

        double outerW = 2 * bx, outerD = 2 * by, outerH = H + 2 * t;
        var what = plans.Select(p => $"{(p.Figure is null ? "a rod" : Figures.Called(p.S.Figure))} that {p.S.MoveWords}").ToList();
        mech.Summary = $"A box {outerW:0} × {outerD:0} mm and {outerH:0} mm tall with {Say.And(what)}.";
        return mech;
    }

    /// <summary>How wide the little plinth under a bobbing figure is (mm): wide enough not to drop through the top.</summary>
    static double PlinthW(double barW) => Math.Max(22, barW + 12);

    /// <summary>
    /// The tilt of a lever resting on a rod's top: the pivot <paramref name="a"/> mm along from the rod, the rod's
    /// top <paramref name="d"/> mm above the pivot, the lever's underside <paramref name="h"/> mm below its pivot.
    /// </summary>
    internal static double TiltFor(double a, double d, double h)
    {
        // The rod's top is on the lever's underside: −a·sinθ + d·cosθ = −h.
        double r = Math.Sqrt(a * a + d * d);
        double beta = Math.Atan2(a, d);
        return Math.Acos(Math.Clamp(-h / r, -1, 1)) - beta;
    }

    /// <summary>The lever's area times its distance from the pivot (positive on the rod's side): which way it leans.</summary>
    static double Moment(Paths64 lever)
    {
        double sum = 0;
        var b = Poly.Bounds(lever);
        for (double x = Math.Floor(b.MinX); x < b.MaxX; x += 1)
        {
            var slice = Poly.Clip(lever, new Rect2(x, b.MinY - 1, x + 1, b.MaxY + 1));
            sum += Poly.Area(slice) * (x + 0.5);
        }
        return sum;
    }

    static double Lerp(double[] a, double phi)
    {
        double x = phi / (2 * Math.PI) * a.Length;
        x -= a.Length * Math.Floor(x / a.Length);
        int i = Math.Min(a.Length - 1, (int)x);
        double t = x - i;
        return a[i] * (1 - t) + a[(i + 1) % a.Length] * t;
    }

    /// <summary>A flat figure turned about an upright axis at <paramref name="x0"/>, seen from the front: squashed across by <paramref name="k"/> (its cosine).</summary>
    static Paths64 SquashX(Paths64 shape, double x0, double k)
    {
        if (Math.Abs(k) < 0.03) k = 0.03 * Math.Sign(k == 0 ? 1 : k);
        var o = new Paths64(shape.Count);
        foreach (var path in shape)
        {
            var p = new Path64(path.Count);
            foreach (var pt in path)
            {
                var v = Poly.V(pt);
                p.Add(Poly.P(x0 + (v.X - x0) * k, v.Y));
            }
            if (k < 0) p.Reverse();
            o.Add(p);
        }
        return o;
    }

    static ChartPanel Chart(IEnumerable<(string Name, uint Colour, Func<double, double> Value)> lines, string title, string unit) => new()
    {
        Title = title,
        Unit = unit,
        Weight = 0.55,
        Series = [.. lines.Select(l => new Series(l.Name, l.Colour, [.. Enumerable.Range(0, 360).Select(i => l.Value(2 * Math.PI * i / 360))]))],
    };
}
