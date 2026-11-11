namespace PhotonCanvas.Core;

/// <summary>
/// One object lying on the flat bed that marks go onto: a pen, a dowel or a bottle on its side (round), or a
/// raised flat piece (a box lid, a block in a jig). On the canvas it's a rectangle: <see cref="LengthMm"/>
/// along its axis and <see cref="AcrossMm"/> across, centred on <see cref="Centre"/>. Anything drawn inside
/// that rectangle is marked on the object.
/// <para>
/// On a round object the rectangle is the object's top unrolled: a distance across it on the canvas is the
/// same distance round the object's surface, measured from the top line, so a 10 mm logo comes out 10 mm
/// round the pen however far round it goes. Seen from above that lands at <c>R sin(s / R)</c> from the top
/// line (<see cref="ToBed"/>); without <see cref="Wrap"/> it's dropped straight down instead.
/// </para>
/// </summary>
/// <param name="Name">What it's called in warnings ("Pen 1").</param>
/// <param name="Centre">The middle of its rectangle on the canvas (mm): on a round object, a point on its top line.</param>
/// <param name="AxisDeg">Which way it lies: 0 = along X (left to right), 90 = along Y (front to back).</param>
/// <param name="LengthMm">How long the rectangle is, along the axis.</param>
/// <param name="AcrossMm">How wide the rectangle is across the axis: on a round object, the unrolled width.</param>
/// <param name="RadiusMm">Radius of a round object; 0 = flat.</param>
/// <param name="TopMm">Height of its top above the bed (mm), as the focus measures it.</param>
public sealed record WrapZone(string Name, Vec2 Centre, double AxisDeg, double LengthMm, double AcrossMm, double RadiusMm, double TopMm)
{
    /// <summary>Round only: the design is wrapped round the surface (true), or dropped straight down onto it.</summary>
    public bool Wrap { get; init; } = true;

    /// <summary>
    /// Round only: slow down where the surface slopes away from the beam. The spot spreads over more of a
    /// sloping surface (by 1 / cos of the slope) and a move across the slope covers more of it, so each mm²
    /// gets less light; going slower by cos of the slope puts the same in as on the top.
    /// </summary>
    public bool EvenOut { get; init; } = true;

    /// <summary>The slowest <see cref="EvenOut"/> goes: a quarter of the layer's speed (about 75° round).</summary>
    public const double SlowestShare = 0.25;

    public bool Round => RadiusMm > 0;

    Vec2 Dir
    {
        get
        {
            double t = AxisDeg * Math.PI / 180;
            return new(Math.Cos(t), Math.Sin(t));
        }
    }

    Vec2 Perp => new(-Dir.Y, Dir.X);

    /// <summary>A canvas or bed point as (along the axis, across it) from <see cref="Centre"/>.</summary>
    public (double Along, double Across) Local(Vec2 p)
    {
        var d = p - Centre;
        return (d.Dot(Dir), d.Dot(Perp));
    }

    public Vec2 World(double along, double across) => Centre + Dir * along + Perp * across;

    /// <summary>Is this canvas point on the object's rectangle?</summary>
    public bool Holds(Vec2 page)
    {
        var (a, s) = Local(page);
        return Math.Abs(a) <= LengthMm / 2 + 1e-6 && Math.Abs(s) <= AcrossMm / 2 + 1e-6;
    }

    /// <summary>Across on the canvas (round the surface) → across on the bed (seen from above).</summary>
    public double ToBedAcross(double s) => Round && Wrap ? RadiusMm * Math.Sin(Math.Clamp(s / RadiusMm, -Math.PI / 2, Math.PI / 2)) : s;

    /// <summary>Across on the bed → across on the canvas (the inverse of <see cref="ToBedAcross"/>).</summary>
    public double ToPageAcross(double u) => Round && Wrap ? RadiusMm * Math.Asin(Math.Clamp(u / RadiusMm, -1, 1)) : u;

    /// <summary>Where a canvas point on the object lands on the bed, seen from above.</summary>
    public Vec2 ToBed(Vec2 page)
    {
        var (a, s) = Local(page);
        return World(a, ToBedAcross(s));
    }

    /// <summary>Half the width of where the object's marks land on the bed, seen from above.</summary>
    public double HalfFootprint => Math.Abs(ToBedAcross(AcrossMm / 2));

    /// <summary>Is this bed point where the object's marks land (within <paramref name="margin"/> mm)?</summary>
    public bool Covers(Vec2 bed, double margin = 0)
    {
        var (a, u) = Local(bed);
        return Math.Abs(a) <= LengthMm / 2 + margin && Math.Abs(u) <= HalfFootprint + margin;
    }

    /// <summary>The surface's height above the bed under a bed point (mm).</summary>
    public double HeightAt(Vec2 bed)
    {
        if (!Round) return TopMm;
        double u = Math.Min(Math.Abs(Local(bed).Across), RadiusMm);
        return TopMm - RadiusMm + Math.Sqrt(RadiusMm * RadiusMm - u * u);
    }

    /// <summary>How far round from the top a bed point is (degrees, 0 on the top line).</summary>
    public double SlopeDegAt(Vec2 bed) => Round ? Math.Asin(Math.Min(1, Math.Abs(Local(bed).Across) / RadiusMm)) * 180 / Math.PI : 0;

    /// <summary>Share of the layer's speed to go at over a bed point (1 = full speed): see <see cref="EvenOut"/>.</summary>
    public double SpeedAt(Vec2 bed) =>
        EvenOut && Round ? Math.Max(SlowestShare, Math.Cos(SlopeDegAt(bed) * Math.PI / 180)) : 1;

    /// <summary>The rectangle on the canvas, corner by corner.</summary>
    public Vec2[] Outline() => Rectangle(AcrossMm / 2);

    /// <summary>Where its marks land on the bed, seen from above, corner by corner.</summary>
    public Vec2[] Footprint() => Rectangle(HalfFootprint);

    Vec2[] Rectangle(double half) =>
        [World(-LengthMm / 2, -half), World(LengthMm / 2, -half), World(LengthMm / 2, half), World(-LengthMm / 2, half)];

    public Rect2 Bounds => Rect2.Bounds(Outline());
}

/// <summary>
/// Marks drawn on objects' rectangles (<see cref="WrapZone"/>) moved to where they really land on the bed:
/// wrapped round a round object's surface, left as they are on a flat one, and everything off the objects
/// left where it is. Lines are cut into short steps first so they bend with the surface; a fill's lines are
/// worked out on the canvas (evenly spaced round the surface) and then moved; a picture is laid out again
/// on its own grid, each dot taken from where it lands on the canvas. Also says which objects were used,
/// how far round them the marks go, and what runs off an object's edge.
/// </summary>
public sealed class SurfaceWrap
{
    public IReadOnlyList<WrapZone> Zones { get; }

    /// <summary>Lines on an object are cut into steps this long before they're moved, so they follow its curve.</summary>
    public const double StepMm = 0.25;

    public SurfaceWrap(IEnumerable<WrapZone> zones) { Zones = [.. zones]; }

    /// <summary>The objects that something was marked on, with how far round from the top the marks go (degrees).</summary>
    public Dictionary<WrapZone, double> Used { get; } = [];
    /// <summary>Objects with a line or fill that runs off their edge (only the part on them is wrapped).</summary>
    public HashSet<WrapZone> RunsOff { get; } = [];
    /// <summary>Objects with a fill whose lines don't run along them (slower, and stepped at every height).</summary>
    public HashSet<WrapZone> FillAcross { get; } = [];
    /// <summary>Something is marked off every object (so it needs a height of its own).</summary>
    public bool AnyElsewhere { get; private set; }

    /// <summary>Two objects whose rectangles overlap (null when none do).</summary>
    public (WrapZone A, WrapZone B)? Overlap()
    {
        for (int i = 0; i < Zones.Count; i++)
            for (int j = i + 1; j < Zones.Count; j++)
                if (Overlaps(Zones[i], Zones[j])) return (Zones[i], Zones[j]);
        return null;
    }

    /// <summary>Do two rectangles overlap (separating axis test, touching edges don't count)?</summary>
    static bool Overlaps(WrapZone a, WrapZone b)
    {
        Vec2[] pa = a.Outline(), pb = b.Outline();
        foreach (var poly in new[] { pa, pb })
            for (int k = 0; k < 4; k++)
            {
                var e = poly[(k + 1) % 4] - poly[k];
                var n = new Vec2(-e.Y, e.X);
                double a0 = pa.Min(n.Dot), a1 = pa.Max(n.Dot), b0 = pb.Min(n.Dot), b1 = pb.Max(n.Dot);
                if (a1 <= b0 + 1e-6 || b1 <= a0 + 1e-6) return false;
            }
        return true;
    }

    /// <summary>The object a canvas point is on, if any.</summary>
    public WrapZone? ZoneAt(Vec2 page)
    {
        foreach (var z in Zones) if (z.Holds(page)) return z;
        return null;
    }

    /// <summary>The object whose marks land on this bed point, if any.</summary>
    public WrapZone? ZoneUnder(Vec2 bed, double margin = 0.25)
    {
        foreach (var z in Zones) if (z.Covers(bed, 0)) return z;
        foreach (var z in Zones) if (z.Covers(bed, margin)) return z;
        return null;
    }

    /// <summary>Where a canvas point lands on the bed.</summary>
    public Vec2 ToBed(Vec2 page) => ZoneAt(page) is { } z ? z.ToBed(page) : page;

    /// <summary>
    /// The canvas point that lands on a bed point: null where nothing can (beside a round object's marks,
    /// inside its rectangle, the canvas is wrapped away round the object).
    /// </summary>
    public Vec2? ToPage(Vec2 bed)
    {
        foreach (var z in Zones)
            if (z.Covers(bed))
            {
                var (a, u) = z.Local(bed);
                return z.World(a, z.ToPageAcross(u));
            }
        foreach (var z in Zones) if (z.Holds(bed)) return null;
        return bed;
    }

    /// <summary>The surface's height under a bed point, or null off every object.</summary>
    public double? HeightAt(Vec2 bed) => ZoneUnder(bed) is { } z ? z.HeightAt(bed) : null;

    /// <summary>Share of the layer's speed to go at over a bed point (1 off the objects).</summary>
    public double SpeedAt(Vec2 bed) => ZoneUnder(bed) is { } z ? z.SpeedAt(bed) : 1;

    bool Touches(Rect2 r) => !r.IsEmpty && Zones.Any(z =>
    {
        var b = z.Bounds;
        return r.MinX <= b.MaxX && r.MaxX >= b.MinX && r.MinY <= b.MaxY && r.MaxY >= b.MinY;
    });

    void Note(WrapZone? z, Vec2 page)
    {
        if (z is null) { AnyElsewhere = true; return; }
        double deg = z.Round ? (z.Wrap ? Math.Abs(z.Local(page).Across) / z.RadiusMm * 180 / Math.PI : Math.Asin(Math.Min(1, Math.Abs(z.Local(page).Across) / z.RadiusMm)) * 180 / Math.PI) : 0;
        Used[z] = Math.Max(Used.GetValueOrDefault(z), deg);
    }

    /// <summary>A path (canvas) moved onto the bed: cut into short steps where it's on an object, so it bends with it.</summary>
    public List<Vec2> MapPath(IReadOnlyList<Vec2> pts)
    {
        var o = new List<Vec2>(pts.Count * 2);
        if (pts.Count == 0) return o;
        var first = ZoneAt(pts[0]);
        Note(first, pts[0]);
        o.Add(first?.ToBed(pts[0]) ?? pts[0]);
        var was = first;
        for (int i = 1; i < pts.Count; i++)
        {
            Vec2 a = pts[i - 1], b = pts[i];
            int n = Touches(Rect2.Bounds([a, b])) ? Math.Max(1, (int)Math.Ceiling(a.Dist(b) / StepMm)) : 1;
            for (int k = 1; k <= n; k++)
            {
                var p = k == n ? b : Vec2.Lerp(a, b, (double)k / n);
                var z = ZoneAt(p);
                Note(z, p);
                if (!ReferenceEquals(z, was))
                {
                    if (was is not null) RunsOff.Add(was);
                    if (z is not null) RunsOff.Add(z);
                    was = z;
                }
                o.Add(z?.ToBed(p) ?? p);
            }
        }
        return o;
    }

    /// <summary>A block (laid out on the canvas) moved to where it lands on the bed. Blocks off every object come back as they were.</summary>
    public Block Apply(Block b)
    {
        switch (b)
        {
            case VectorBlock v:
            {
                var pts = v.Path.EmittedPoints();
                if (!Touches(Rect2.Bounds(pts))) { AnyElsewhere |= pts.Count > 0; return v; }
                return v with { Path = Polyline.Open(MapPath(pts)) };
            }
            case LoopsBlock l:
            {
                if (!Touches(Rect2.Bounds(l.Loops.SelectMany(x => x)))) { AnyElsewhere |= l.Loops.Count > 0; return l; }
                var loops = new List<IReadOnlyList<Vec2>>();
                foreach (var loop in l.Loops)
                {
                    if (loop.Count == 0) continue;
                    if (!l.Closed) { loops.Add(MapPath(loop)); continue; }
                    var m = MapPath([.. loop, loop[0]]);
                    m.RemoveAt(m.Count - 1);
                    loops.Add(m);
                }
                return l with { Loops = loops };
            }
            case PointsBlock p:
                return p with
                {
                    Points = [.. p.Points.Select(x =>
                    {
                        var z = ZoneAt(x);
                        Note(z, x);
                        return z?.ToBed(x) ?? x;
                    })],
                };
            case FillBlock f:
                return WrapFill(f);
            case DotBlock d:
                if (d.PassBlocks is { Count: > 0 } pb) return WrapDots(d) with { PassBlocks = [.. pb.Select(WrapDots)] };
                return WrapDots(d);
            default:
                return b;
        }
    }

    FillBlock WrapFill(FillBlock f)
    {
        // Quick look first: the area it fills (working out every line of a big fill takes a while).
        var area = f.Lines is not null ? Rect2.Empty : f.Mask is { } mask ? mask.FilledBounds() : Rect2.Bounds(f.Polygons.SelectMany(p => p));
        if (!area.IsEmpty && !Touches(area.Inflate(1))) { AnyElsewhere = true; return f; }
        var lines = Job.FillLines(f);
        if (!Touches(Rect2.Bounds(lines.SelectMany(s => new[] { s.Start, s.End })))) { AnyElsewhere |= lines.Count > 0; return f; }
        var o = new List<Job.FillSegment>(lines.Count);
        foreach (var s in lines)
        {
            var za = ZoneAt(s.Start);
            var d = s.End - s.Start;
            double len = d.Length;
            // A line along an object, all on it, stays a straight line: just its ends move.
            if (za is not null && ReferenceEquals(za, ZoneAt(s.End)) && (!za.Round || Math.Abs(za.Local(s.Start).Across - za.Local(s.End).Across) < 1e-6))
            {
                Note(za, s.Start);
                Note(za, s.End);
                o.Add(s with { Start = za.ToBed(s.Start), End = za.ToBed(s.End) });
                continue;
            }
            if (!Touches(Rect2.Bounds([s.Start, s.End]))) { AnyElsewhere = true; o.Add(s); continue; }
            // Otherwise in short pieces, each moved, one after the other along the line.
            if ((za ?? ZoneAt(s.End)) is { Round: true } across) FillAcross.Add(across);
            int n = Math.Max(1, (int)Math.Ceiling(len / StepMm));
            var was = za;
            Note(za, s.Start);
            for (int k = 0; k < n; k++)
            {
                double t0 = (double)k / n, t1 = (double)(k + 1) / n;
                Vec2 a = Vec2.Lerp(s.Start, s.End, t0), b = Vec2.Lerp(s.Start, s.End, t1);
                var z = ZoneAt(Vec2.Lerp(a, b, 0.5));
                Note(z, b);
                if (!ReferenceEquals(z, was))
                {
                    if (was is not null) RunsOff.Add(was);
                    if (z is not null) RunsOff.Add(z);
                    was = z;
                }
                o.Add(new Job.FillSegment(s.Y, s.X0 + (s.X1 - s.X0) * t0, s.X0 + (s.X1 - s.X0) * t1, z?.ToBed(a) ?? a, z?.ToBed(b) ?? b));
            }
        }
        // Moved lines are kept as they are: the polygons stay for the record (and Sections), but aren't used to make lines again.
        return f with { Lines = o, OverscanMm = 0, LineDwellMs = 0 };
    }

    /// <summary>A picture laid out again on its own grid: each dot taken from the canvas point that lands on it.</summary>
    DotBlock WrapDots(DotBlock d)
    {
        int w = d.Bitmap.Width, h = d.Bitmap.Height;
        if (w == 0 || h == 0) return d;
        Vec2[] corners = [d.At(0, 0), d.At(w - 1, 0), d.At(w - 1, h - 1), d.At(0, h - 1)];
        if (!Touches(Rect2.Bounds(corners))) { AnyElsewhere = true; return d; }
        double pitch = 25.4 / d.Dpi;
        double a = d.AngleDeg * Math.PI / 180, cos = Math.Cos(a), sin = Math.Sin(a);
        var bmp = new Bitmap(w, h);
        byte[]? levels = d.Levels is null ? null : new byte[w * h];
        for (int r = 0; r < h; r++)
            for (int c = 0; c < w; c++)
            {
                var at = d.At(c, r);
                if (ToPage(at) is not { } src) continue;
                var rel = (src - d.Origin) / pitch;
                int cs = (int)Math.Round(rel.X * cos + rel.Y * sin), rs = (int)Math.Round(-rel.X * sin + rel.Y * cos);
                if (cs < 0 || rs < 0 || cs >= w || rs >= h || !d.Bitmap[cs, rs]) continue;
                bmp[c, r] = true;
                if (levels is not null) levels[r * w + c] = d.Levels![rs * w + cs];
                Note(ZoneAt(src), src);
            }
        return d with { Bitmap = bmp, Levels = levels };
    }
}
