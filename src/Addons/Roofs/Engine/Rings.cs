using Clipper2Lib;
using PhotonCanvas.Addon.Walls;

namespace PhotonCanvas.Addon.Roofs;

/// <summary>
/// Round towers and cone roofs stacked from rings of sheet. A laser can't etch round a tower, so the
/// stonework is cut into each ring's edge instead: a small notch at every joint between blocks, the
/// joints moving along from course to course, and the layer lines between rings read as the bed
/// joints. A cone is stacked from rings that get smaller step by step; their edges are scalloped or
/// notched so the steps read as courses of shingles.
/// </summary>
/// <remarks>Each ring is drawn round (0, 0); in the model the tower's middle is at X = 0, Y = 0 and Z runs up from the ground.</remarks>
public static class Rings
{
    /// <summary>The wall maker's textures that work on a ring's edge.</summary>
    public static readonly IReadOnlyList<string> Textures = ["Stone blocks", "Brick", "Fieldstone", "Tiles", "Wood planks (upright)", "Corrugated metal", Walls.Textures.Plain];

    /// <summary>How deep the notches between blocks go (mm), for a groove of this width.</summary>
    public static double NotchDepth(double groove) => Math.Clamp(groove * 0.8, 0.25, 1.2);

    /// <summary>One course's blocks: where each starts and ends (radians) and how far it stands out (mm).</summary>
    public sealed record Block(double From, double To, double Out);

    public static List<Block> Course(TowerSpec s, double r, int course)
    {
        double a = Math.Max(1, s.SizeA), circ = 2 * Math.PI * r;
        var rnd = new Random(s.Seed * 7919 + course * 104729 + 17);
        var blocks = new List<Block>();
        switch (s.Texture)
        {
            case "Brick" or "Tiles" or "Wood planks (upright)":
            {
                int n = Math.Max(3, (int)Math.Round(circ / a));
                double shift = s.Texture == "Brick" && course % 2 == 1 ? Math.PI / n : 0;
                for (int i = 0; i < n; i++) blocks.Add(new(shift + 2 * Math.PI * i / n, shift + 2 * Math.PI * (i + 1) / n, 0));
                break;
            }
            case "Stone blocks" or "Fieldstone":
            {
                bool rough = s.Texture == "Fieldstone";
                var lengths = new List<double>();
                double sum = 0;
                while (sum < circ - a * 0.6) { double l = a * (0.6 + 0.9 * rnd.NextDouble()); lengths.Add(l); sum += l; }
                if (lengths.Count < 3) { lengths = [circ / 3, circ / 3, circ / 3]; sum = circ; }
                double k = 2 * Math.PI / sum, at = rnd.NextDouble() * 2 * Math.PI;
                foreach (var l in lengths)
                {
                    double outBy = rough ? (rnd.NextDouble() - 0.6) * 0.5 : 0;
                    blocks.Add(new(at, at + l * k, outBy));
                    at += l * k;
                }
                break;
            }
        }
        return blocks;
    }

    /// <summary>A ring's outer edge for a course of the tower: round, with the stonework's notches.</summary>
    public static PathD Masonry(TowerSpec s, double r, int course)
    {
        int steps = Math.Clamp((int)Math.Ceiling(2 * Math.PI * r / 0.08), 96, 6000);
        double g = Math.Max(0.1, s.GrooveMm), depth = NotchDepth(g);
        if (s.Texture == "Corrugated metal")
        {
            int n = Math.Max(6, (int)Math.Round(2 * Math.PI * r / Math.Max(1, s.SizeA)));
            return Shapes.Polar(a => r - 0.4 * (0.5 - 0.5 * Math.Cos(n * a)), steps);
        }
        var blocks = Course(s, r, course);
        if (blocks.Count == 0) return Shapes.Polar(_ => r, Shapes.Steps(r));
        bool rough = s.Texture == "Fieldstone";
        double half = (rough ? g * 0.8 : g / 2) / r;
        return Shapes.Polar(a =>
        {
            foreach (var b in blocks)
            {
                double t = Wrap(a - b.From);
                double len = b.To - b.From;
                if (t > len) continue;
                double edge = Math.Min(t, len - t);
                double rr = r + b.Out;
                if (edge >= half) return rr;
                double f = edge / half;
                // Square-ish notches for cut stone and brick, rounded ones for fieldstone.
                return rr - depth * (rough ? 0.5 + 0.5 * Math.Cos(f * Math.PI) : Math.Clamp((1 - f) * 2.5, 0, 1));
            }
            return r;
        }, steps);
    }

    static double Wrap(double a)
    {
        a %= 2 * Math.PI;
        return a < 0 ? a + 2 * Math.PI : a;
    }

    /// <summary>A cone ring's edge: scalloped like round-ended shingles, notched like slates, or plain.</summary>
    public static PathD ConeEdgeOutline(ConeEdge edge, double r, double shingle, double groove, double step, int layer)
    {
        int n = Math.Max(5, (int)Math.Round(2 * Math.PI * r / Math.Max(1, shingle)));
        double phase = layer % 2 == 1 ? Math.PI / n : 0;
        int steps = Math.Clamp((int)Math.Ceiling(2 * Math.PI * r / 0.08), 64, 6000);
        switch (edge)
        {
            case ConeEdge.Scallops:
            {
                double d = Math.Min(0.8, step * 0.45);
                return Shapes.Polar(a => r - d + d * Math.Abs(Math.Sin(n * (a - phase) / 2)), steps);
            }
            case ConeEdge.Slates:
            {
                double depth = Math.Min(NotchDepth(groove), step * 0.6), half = Math.Max(0.1, groove) / 2 / r;
                return Shapes.Polar(a =>
                {
                    double t = Wrap(a - phase) % (2 * Math.PI / n), edgeA = Math.Min(t, 2 * Math.PI / n - t);
                    return edgeA >= half ? r : r - depth * Math.Clamp((1 - edgeA / half) * 2.5, 0, 1);
                }, steps);
            }
            default: return Geo.Circle(0, 0, r);
        }
    }

    /// <summary>The layer's number, scored on its top face in the band at the back of the ring (it's hidden once stacked).</summary>
    static List<PathD> Numbered(int n, double rIn, double rOut)
    {
        double band = rOut - rIn;
        if (band < 3.5) return [];
        double h = Math.Clamp(band * 0.45, 1.6, 3.5), mid = (rIn + rOut) / 2;
        return Shapes.Number(n, -Shapes.NumberWidth(n, h) / 2, mid - h / 2, h);
    }

    /// <summary>Short scored lines across the ring band at the edges of a gap in the layer above, to line it up.</summary>
    static List<PathD> Ticks(IEnumerable<(double Deg, double Width)> gaps, double rIn, double rOut)
    {
        var lines = new List<PathD>();
        foreach (var (deg, w) in gaps)
            foreach (double side in new[] { -1.0, 1.0 })
            {
                double a = deg * Math.PI / 180, nx = -Math.Sin(a), ny = Math.Cos(a), off = side * w / 2;
                lines.Add([new(Math.Cos(a) * (rIn + 0.6) + nx * off, Math.Sin(a) * (rIn + 0.6) + ny * off), new(Math.Cos(a) * (rOut - 0.8) + nx * off, Math.Sin(a) * (rOut - 0.8) + ny * off)]);
            }
        return lines;
    }

    // ---- cones ----

    /// <summary>
    /// A cone stacked from rings: the bottom one <paramref name="eaveR"/> across its middle, each one
    /// above it a step smaller so the steps follow the slope. Hollow while they're wide enough; the top
    /// ones are solid with a hole for a cocktail stick as the spire's point.
    /// </summary>
    public static List<RoofPart> Cone(double eaveR, double pitchDeg, double T, double ringMm, ConeEdge edge, double shingle, double groove, double pin, double firstInner, double z0, int numberFrom, List<Finding> findings)
    {
        var parts = new List<RoofPart>();
        double step = T / Math.Tan(Math.Clamp(pitchDeg, 20, 80) * Math.PI / 180);
        double rMin = Math.Max(1.5, pin / 2 + 1.2);
        int k = 0;
        for (double r = eaveR; r >= rMin && k < 400; r -= step, k++)
        {
            var outline = ConeEdgeOutline(edge, r, shingle, groove, step, k);
            double inner = k == 0 && firstInner > 0 ? Math.Min(firstInner, r - 3) : r - Math.Max(3, ringMm);
            PathsD cut;
            if (inner > pin / 2 + 2) cut = Shapes.Ring(outline, inner);
            else
            {
                inner = 0;
                cut = pin > 0 ? Geo.Subtract([outline], [Geo.Circle(0, 0, pin / 2)]) : [outline];
            }
            var lines = Numbered(numberFrom + k, inner > 0 ? inner : Math.Max(pin / 2, r * 0.25), r - 0.8);
            parts.Add(new RoofPart($"Cone ring {numberFrom + k}", Geo.Tidy(cut), [], lines, 1, [Pose.Flat(new V3(0, 0, z0 + k * T))]));
        }
        if (k == 0) findings.Add(new(Severity.Bad, "The cone is too small to make."));
        else findings.Add(new(Severity.Note, $"The cone is {k} rings ({k * T:0.#} mm) high{(pin > 0 ? $": push a {pin:0.#} mm cocktail stick down through its top as the spire's point" : "")}."));
        return parts;
    }

    /// <summary>A cone roof on its own (<see cref="RoofStyle.Cone"/>): for a round tower <see cref="RoofSpec.WidthMm"/> across.</summary>
    public static RoofKit ConeRoof(RoofSpec s, List<Finding> findings)
    {
        double T = Math.Max(0.3, s.ThicknessMm), R = s.WidthMm / 2;
        var parts = new List<RoofPart>();
        if (R < 8) { findings.Add(new(Severity.Bad, "The tower is too narrow for a cone: make it at least 16 mm across.")); return new(parts, findings, T); }
        double inner = s.InsetMm > 0 ? R - s.InsetMm : 0;
        parts.AddRange(Cone(R + Math.Max(0, s.EaveMm), Math.Max(30, s.PitchDeg), T, s.ConeRingMm, s.ConeEdge, s.ShingleA, s.ShingleGrooveMm, s.PinMm, inner, 0, 1, findings));
        if (inner > 3)
        {
            double lo = inner - RoofMaker.ClearMm;
            int layers = Math.Max(1, (int)Math.Ceiling((Math.Max(0, s.RestAboveWallsMm) + RoofMaker.GripMm) / T - 1e-6));
            parts.Add(new RoofPart("Locator ring", LocatorRing(lo), [], [], layers, [.. Enumerable.Range(0, layers).Select(i => Pose.Flat(new V3(0, 0, -(i + 1) * T)))]));
            findings.Add(new(Severity.Note, "Glue the locator ring under the middle of the bottom ring: it drops into the tower so the roof lifts off but can't slide."));
        }
        return RoofMaker.Finish(parts, findings, T, s.Roofs);
    }

    static PathsD LocatorRing(double r) => r - 5 > 3 ? Shapes.Ring(Geo.Circle(0, 0, r), r - 5) : [Geo.Circle(0, 0, r)];

    // ---- towers ----

    /// <summary>Something cut through the tower's wall: where it faces (degrees), its outline seen from outside (x across, y up from the ground) and its name.</summary>
    sealed record Opening(double Deg, PathsD Shape);

    public static RoofKit Tower(TowerSpec s)
    {
        var findings = new List<Finding>();
        var parts = new List<RoofPart>();
        double T = Math.Max(0.3, s.ThicknessMm), R = s.DiameterMm / 2, ring = Math.Max(2, s.RingMm), bore = R - ring;
        if (R < 8) { findings.Add(new(Severity.Bad, "The tower is too narrow: make it at least 16 mm across.")); return new(parts, findings, T); }
        if (bore < 3) { findings.Add(new(Severity.Bad, "The rings are wider than the tower allows: make them narrower or the tower wider.")); return new(parts, findings, T); }
        int N = Math.Max(2, (int)Math.Round(s.HeightMm / T));
        int parapet = s.Top == TowerTop.Battlements ? Math.Clamp((int)Math.Round(s.ParapetMm / T), 2, N - 2) : 0;
        int body = N - parapet;
        int storeys = Math.Clamp(s.Storeys, 1, Math.Max(1, body / 3));
        // Floors: the ground floor, one at the foot of each storey above it, and the deck behind the battlements.
        var floors = Enumerable.Range(0, storeys).Select(i => (int)Math.Round((double)body * i / storeys)).ToList();
        if (parapet > 0) floors.Add(body);
        var floorSet = floors.ToHashSet();
        int merlons = parapet > 0 ? Math.Clamp((int)Math.Round(s.MerlonHeightMm / T), 1, parapet - 1) : 0;
        int perCourse = Math.Max(1, (int)Math.Round(Math.Max(T, s.SizeB) / T));
        double fit = Math.Max(0, s.FitMm);

        // Wall slots: radial notches from the outside, as high as the walls.
        var slotDirs = s.Join is { } join ? Connectors.Directions(join, s.JoinAngleDeg).Select(d => d + s.JoinFirstDeg).ToArray() : [];
        double slotW = s.WallMm + fit, slotDepth = Math.Max(0.5, s.SlotDepthMm);
        int slotLayers = slotDirs.Length > 0 ? Math.Min(N, (int)Math.Ceiling(s.WallHeightMm / T - 0.02)) : 0;
        if (slotDirs.Length > 0)
        {
            if (slotDepth > ring - 1.5) findings.Add(new(Severity.Bad, $"The wall slots ({slotDepth:0.#} mm deep) leave too little ring: make the rings wider than {slotDepth + 1.5:0.#} mm or the slots shallower."));
            if (2 * Math.Asin(Math.Min(1, (slotW / 2 + 1) / (R - slotDepth))) * slotDirs.Length > 2 * Math.PI) findings.Add(new(Severity.Bad, "The wall slots run into each other: make the tower wider."));
            double toEnd = R - slotDepth + WallKitMaker.EndClearMm;
            findings.Add(new(Severity.Note, $"Walls go {slotDepth:0.#} mm into the tower; their ends are {toEnd:0.#} mm from its middle (so between two towers, the panel is the centres' distance less {2 * toEnd:0.#} mm)."));
            if (slotLayers > 0 && floors.Any(f => f > 0 && f < slotLayers)) findings.Add(new(Severity.Note, "The walls reach above the first floor, so the storeys they pass can't lift apart while the walls are in."));
        }

        // Doors and windows, as shapes in the wall seen from outside, each with the way it faces.
        var openings = new List<Opening>();
        var used = slotDirs.Select(d => (Deg: d, Half: (slotW / 2 + 3) / R * 180 / Math.PI)).ToList();
        if (s.Door)
        {
            double w = Math.Max(3, s.DoorWidthMm), h = Math.Max(3, s.DoorHeightMm);
            int top = floors.Count > 1 ? floors[1] : body;
            if (T + h > top * T - T) findings.Add(new(Severity.Bad, $"The door is too tall for the ground storey: make it lower than {top * T - 2 * T:0.#} mm, or have fewer storeys."));
            else if (w > 2 * (R - ring) * 0.9) findings.Add(new(Severity.Bad, "The door is too wide for the tower."));
            else if (used.Any(u => Near(u.Deg, s.DoorAngleDeg) < u.Half + (w / 2) / R * 180 / Math.PI)) findings.Add(new(Severity.Bad, "The door is where a wall joins the tower: turn it to face another way."));
            else
            {
                openings.Add(new(s.DoorAngleDeg, WallKitMaker.Opening(s.DoorShape is OpeningShape.Round or OpeningShape.ArrowSlit ? OpeningShape.Arched : s.DoorShape, -w / 2, T, w, h)));
                used.Add((s.DoorAngleDeg, (w / 2 + 3) / R * 180 / Math.PI));
            }
        }
        if (s.WindowsPerStorey > 0)
        {
            double w = Math.Max(1, s.WindowWidthMm), h = s.WindowShape == OpeningShape.Round ? w : Math.Max(1, s.WindowHeightMm);
            int n = s.WindowsPerStorey, missing = 0;
            for (int st = 0; st < storeys; st++)
            {
                int foot = floors[st], top = st + 1 < floors.Count ? floors[st + 1] : N;
                double y0 = (foot + 1) * T + s.SillMm;
                if (y0 + h > top * T - T) { missing += n; continue; }
                for (int j = 0; j < n; j++)
                {
                    double want = s.DoorAngleDeg + (j + 0.5) * 360.0 / n + st * 180.0 / n * 0.5, half = (w / 2 + 2) / R * 180 / Math.PI;
                    double? got = null;
                    for (int tryN = 0; tryN <= 12 && got is null; tryN++)
                        foreach (double sgn in tryN == 0 ? new[] { 0.0 } : new[] { 1.0, -1.0 })
                        {
                            double a = want + sgn * tryN * 5;
                            var near = used.Where(u => true).ToList();
                            bool clashes = st == 0 ? near.Any(u => Near(u.Deg, a) < u.Half + half) : slotDirs.Any(d => Near(d, a) < (slotW / 2 + 3) / R * 180 / Math.PI + half && y0 < slotLayers * T);
                            if (!clashes) { got = a; break; }
                        }
                    if (got is not { } ang) { missing++; continue; }
                    openings.Add(new(ang, WallKitMaker.Opening(s.WindowShape, -w / 2, y0, w, h)));
                    if (st == 0) used.Add((ang, half));
                }
            }
            if (missing > 0) findings.Add(new(Severity.Warn, missing == n * storeys ? "No window fits: lower the sill, make them shorter or the storeys taller." : $"{missing} window{(missing == 1 ? "" : "s")} didn't fit."));
        }

        // The layers, from the ground up.
        var locatorPoses = new List<Pose>();
        double crenelDeg = 0, merlonDeg = 0;
        int crenels = 0;
        if (merlons > 0)
        {
            double circ = 2 * Math.PI * R;
            crenels = Math.Max(3, (int)Math.Round(circ / (Math.Max(1, s.MerlonWidthMm) + Math.Max(1, s.CrenelWidthMm))));
            crenelDeg = Math.Max(1, s.CrenelWidthMm) / circ * 360;
            merlonDeg = 360.0 / crenels;
        }
        var planks = s.FloorPlanks ? Walls.Textures.Make(new WallSpec { Texture = "Wood planks (across)", SizeA = 5, SizeB = 30, GrooveMm = 0.5, Seed = s.Seed }, new Face(2 * bore + 2, 2 * bore + 2, 0, 2 * bore + 2, 2 * bore + 2)) : TextureParts.None;
        planks = new TextureParts(Geo.Move(planks.Raised, -bore - 1, -bore - 1), Geo.Move(planks.Etched, -bore - 1, -bore - 1), Geo.Map(planks.Lines, p => new PointD(p.x - bore - 1, p.y - bore - 1)));
        List<(double Deg, double Width)> GapsAt(int layer)
        {
            var gaps = new List<(double, double)>();
            double zm = (layer + 0.5) * T;
            foreach (var o in openings)
            {
                var band = Geo.Intersect(o.Shape, [Geo.Rect(-1000, zm - 0.05, 1000, zm + 0.05)]);
                if (band.Count == 0) continue;
                var (x0, _, x1, _) = Shapes.Box(band);
                gaps.Add((o.Deg, x1 - x0));
            }
            if (merlons > 0 && layer >= N - merlons)
                for (int i = 0; i < crenels; i++) gaps.Add((s.DoorAngleDeg + merlonDeg * (i + 0.5), 2 * R * Math.Sin(crenelDeg / 2 * Math.PI / 180)));
            return gaps;
        }
        bool split = false;
        for (int i = 0; i < N; i++)
        {
            int course = i / perCourse;
            var outline = Masonry(s, R, course);
            bool isFloor = floorSet.Contains(i);
            PathsD cut = isFloor ? [outline] : Shapes.Ring(outline, bore);
            var etched = new PathsD();
            var lines = new List<PathD>();
            var gaps = isFloor ? [] : GapsAt(i);
            if (gaps.Count > 0)
            {
                var holes = new PathsD(gaps.Select(g => Shapes.Radial(g.Deg, bore - 2, R + 5, g.Width)));
                cut = Geo.Subtract(cut, Geo.Union(holes));
            }
            if (i < slotLayers)
                cut = Geo.Subtract(cut, Geo.Union(new PathsD(slotDirs.Select(d => Shapes.Radial(d, R - slotDepth, R + 5, slotW)))));
            if (isFloor)
            {
                var deck = new PathsD { Geo.Circle(0, 0, bore - 0.3) };
                var (pe, pl) = Shingles.Apply(planks, deck, EtchStyle.Grooves);
                etched.AddRange(pe);
                lines.AddRange(pl);
            }
            // Marks for lining up a gap in the layer above, if this layer hasn't the same gap.
            if (i + 1 < N && !floorSet.Contains(i + 1))
            {
                var above = GapsAt(i + 1);
                var mine = gaps.Select(g => g.Deg).ToList();
                lines.AddRange(Ticks(above.Where(g => !mine.Any(m => Near(m, g.Deg) < 0.01)), isFloor ? bore + 0.2 : bore, R - NotchDepth(s.GrooveMm)));
            }
            lines.AddRange(Numbered(i + 1, isFloor ? bore + 0.3 : bore, R - NotchDepth(s.GrooveMm) - 0.3));
            int pieces = Geo.Pieces(cut);
            if (pieces > 1) split = true;
            string name = isFloor ? (i == 0 ? "Ground floor" : i == body && parapet > 0 ? "Deck" : "Floor") + $" (layer {i + 1})" : $"Ring {i + 1}" + (pieces > 1 ? $" ({pieces} pieces)" : "");
            parts.Add(new RoofPart(name, Geo.Tidy(cut), Geo.Union(etched), lines, 1, [Pose.Flat(new V3(0, 0, i * T))]));
            if (isFloor && i > 0) locatorPoses.Add(Pose.Flat(new V3(0, 0, (i - 1) * T)));
        }
        if (locatorPoses.Count > 0)
        {
            parts.Add(new RoofPart("Locator ring", LocatorRing(bore - RoofMaker.ClearMm), [], [], locatorPoses.Count, locatorPoses));
            findings.Add(new(Severity.Note, $"Each storey lifts off the one below: glue a locator ring under the middle of its floor."));
        }
        if (s.Top == TowerTop.Cone)
        {
            parts.AddRange(Cone(R + Math.Max(0, s.ConeEaveMm), s.ConePitchDeg, T, Math.Max(3, s.RingMm * 0.75), s.ConeEdge, Math.Max(2, s.SizeA / 2), s.GrooveMm, s.PinMm, bore, N * T, N + 1, findings));
            parts.Add(new RoofPart("Roof locator ring", LocatorRing(bore - RoofMaker.ClearMm), [], [], 1, [Pose.Flat(new V3(0, 0, (N - 1) * T))]));
        }
        findings.Add(new(Severity.Note, $"The tower is {N} layers ({N * T:0.#} mm) high. Each ring has its number scored on top: stack them in order and glue them."));
        if (split) findings.Add(new(Severity.Note, "Rings with doors, windows or battlements come in pieces: the marks on the ring below show where the gaps go."));
        return RoofMaker.Finish(parts, findings, T, s.Towers);
    }

    static double Near(double a, double b)
    {
        double d = Math.Abs((a - b) % 360);
        return d > 180 ? 360 - d : d;
    }
}
