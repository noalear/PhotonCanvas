using Clipper2Lib;
using PhotonCanvas.Addon.Walls;

namespace PhotonCanvas.Addon.Bases;

/// <summary>One size of base as it stacks up: its layers from the bottom one up (the top one textured), and how many there are.</summary>
public sealed record BaseStack(string Name, List<Part> Layers, int Count);

/// <summary>A movement tray as it goes together: its bottom plate, its frame layers (the top one last), where each base sits and the base it holds.</summary>
public sealed record TrayKit(Part? Bottom, List<Part> Frames, List<PointD> Spots, PathsD Base, int Count);

/// <summary>Everything a set of bases and trays is cut from, with what was found while making it.</summary>
public sealed record BaseKit(List<Part> Parts, List<Finding> Findings, List<BaseStack> Stacks, TrayKit? Tray, int Magnets)
{
    public bool Ok => !Findings.Any(f => f.Level == Severity.Bad);
    public int PieceCount => Parts.Sum(p => p.Copies);
    public int BaseCount => Stacks.Sum(s => s.Count);
}

/// <summary>
/// Makes bases: each one's outline, stacked in layers that step in (the top one textured, every base
/// with its own layout of stones or cracks), a slot for the figure and a magnet hole in the bottom
/// layer if asked for; and movement trays with a pocket for each base.
/// </summary>
public static class BaseKitMaker
{
    public const int MaxBases = 300;
    public const string TrayName = "Movement tray";

    static string N(double v) => v.ToString("0.##", System.Globalization.CultureInfo.CurrentCulture);

    public static BaseKit Make(BaseSpec s)
    {
        var findings = new List<Finding>();
        var parts = new List<Part>();
        var stacks = new List<BaseStack>();
        if (!(s.ThicknessMm > 0)) { findings.Add(new(Severity.Bad, "Give the sheet's thickness.")); return new(parts, findings, stacks, null, 0); }
        var sizes = s.Bases.Where(b => b.Count > 0).ToList();
        int total = sizes.Sum(b => b.Count);
        if (total > MaxBases) { findings.Add(new(Severity.Bad, $"That's {total} bases: make at most {MaxBases} at a time.")); return new(parts, findings, stacks, null, 0); }
        if (total == 0 && !s.MakeTray) { findings.Add(new(Severity.Bad, "There are no bases to make: add some, or make a movement tray.")); return new(parts, findings, stacks, null, 0); }

        int layers = s.LayerCount, magnets = 0, seed = s.Seed * 7919, made = 0;
        foreach (var size in sizes)
        {
            if (!Check(size.Shape, size.WidthMm, size.DepthMm, findings)) return new(parts, findings, stacks, null, 0);
            string label = BaseShapes.Label(size.Shape, size.WidthMm, size.DepthMm);
            var outline = BaseShapes.Outline(size.Shape, size.WidthMm, size.DepthMm, s.CornerMm);
            var (bw, bd) = BaseShapes.Box(size.Shape, size.WidthMm, size.DepthMm);
            var layerCuts = Layers(s, outline, layers, label, findings);
            if (layerCuts is null) return new(parts, findings, stacks, null, 0);

            // Under the top: the plain layers, all the same for every base of this size.
            var lower = new List<Part>();
            for (int i = 0; i < layers - 1; i++)
            {
                string name = layers == 2 ? $"{label} base · bottom" : $"{label} base · layer {i + 1}";
                var p = new Part(name, layerCuts[i], [], [], size.Count);
                lower.Add(p);
                parts.Add(p);
            }
            if (layerCuts.Magnet) magnets += size.Count;

            // Every top its own texture.
            Part? first = null;
            for (int k = 0; k < size.Count; k++)
            {
                var top = layerCuts[layers - 1];
                var (etched, lines) = Texture(s, top, bw, bd, seed + made++ * 104729);
                var p = new Part(size.Count > 1 ? $"{label} base {k + 1}" : $"{label} base", top, etched, lines, 1);
                parts.Add(p);
                first ??= p;
            }
            stacks.Add(new BaseStack($"{label} base", [.. lower, first!], size.Count));
        }

        TrayKit? tray = null;
        if (s.MakeTray)
        {
            tray = Tray(s, findings);
            if (tray is null) return new(parts, findings, stacks, null, magnets);
            if (tray.Bottom is { } b) parts.Add(b);
            parts.AddRange(tray.Frames);
        }
        Notes(s, sizes, tray, magnets, findings);
        return new(parts, findings, stacks, tray, magnets);
    }

    static bool Check(BaseShape shape, double w, double d, List<Finding> findings)
    {
        if (!(w >= 5) || w > 400 || shape is BaseShape.Oval or BaseShape.Rectangle && (!(d >= 5) || d > 400))
        {
            findings.Add(new(Severity.Bad, "Make each base between 5 and 400 mm."));
            return false;
        }
        return true;
    }

    /// <summary>Each layer's cut, the bottom first, and whether the bottom has a magnet hole.</summary>
    sealed class LayerCuts(List<PathsD> cuts, bool magnet)
    {
        public PathsD this[int i] => cuts[i];
        public bool Magnet => magnet;
    }

    static LayerCuts? Layers(BaseSpec s, PathsD outline, int layers, string label, List<Finding> findings)
    {
        double step = Math.Max(0, s.StepMm);
        var b = Geo.Bounds(outline);
        double smallest = Math.Min(b.right - b.left, b.bottom - b.top);
        if (smallest - 2 * step * (layers - 1) < 4)
        {
            findings.Add(new(Severity.Bad, $"The {label} base's top would be too small with {layers} layers each {N(step)} mm in: make the step smaller."));
            return null;
        }
        var cuts = new List<PathsD>();
        for (int i = 0; i < layers; i++) cuts.Add(BaseShapes.Inset(outline, step * i));
        double cx = (b.left + b.right) / 2, cy = (b.top + b.bottom) / 2;

        // A slot for the figure's tab: through every layer but the bottom one, so the tab can't push through.
        if (s.Fixing == FigureFixing.Slot)
        {
            double l = Math.Max(1, s.SlotLengthMm), w = Math.Max(0.5, s.SlotWidthMm);
            var slot = new PathsD { Geo.Rect(cx - l / 2, cy - w / 2, cx + l / 2, cy + w / 2) };
            var top = cuts[layers - 1];
            if (Geo.Area(Geo.Subtract(slot, Geo.Offset(top, -1.5))) > 1e-6)
            {
                findings.Add(new(Severity.Bad, $"The slot is too long for the {label} base's top: make it shorter than {N(Math.Floor((smallest - 2 * step * (layers - 1) - 3) * 10) / 10)} mm."));
                return null;
            }
            for (int i = layers == 1 ? 0 : 1; i < layers; i++) cuts[i] = Geo.Tidy(Geo.Subtract(cuts[i], slot));
        }

        bool magnet = false;
        if (s.MagnetMm > 0)
        {
            double r = s.MagnetMm / 2 + Math.Max(0, s.FitMm) / 4;
            var hole = new PathsD { Geo.Circle(cx, cy, r) };
            if (Geo.Area(Geo.Subtract(hole, Geo.Offset(cuts[0], -1.2))) > 1e-6)
            {
                findings.Add(new(Severity.Bad, $"A {N(s.MagnetMm)} mm magnet is too big for the {label} base: use a smaller one."));
                return null;
            }
            cuts[0] = Geo.Tidy(Geo.Subtract(cuts[0], hole));
            magnet = true;
        }
        return new LayerCuts(cuts, magnet);
    }

    /// <summary>The texture on a top, kept inside its rim and off the spot for the feet: etched areas and scored lines.</summary>
    internal static (PathsD Etched, List<PathD> Lines) Texture(BaseSpec s, PathsD top, double w, double h, int seed, bool rim = true)
    {
        var lines = new List<PathD>();
        var area = top;
        if (rim && s.RimMm > 0)
        {
            var outer = Geo.Tidy(top).Where(p => Clipper.Area(p) > 0).ToList();
            area = Geo.Offset(top, -s.RimMm);
            // The rim's edge, scored round the outside only (not round the slot).
            lines.AddRange(Geo.Offset(new PathsD(outer), -s.RimMm).Where(p => Clipper.Area(p) > 0).Select(Geo.Loop));
        }
        if (s.SpotMm > 0)
        {
            var b = Geo.Bounds(top);
            area = Geo.Subtract(area, [Geo.Circle((b.left + b.right) / 2, (b.top + b.bottom) / 2, s.SpotMm / 2)]);
        }
        if (Geo.Area(area) < 1e-3) return ([], lines);

        var t = GroundTextures.Make(s, w, h, seed);
        var etched = new PathsD();
        double g = Math.Clamp(s.GrooveMm, 0.05, 3);
        if (s.Etch == EtchStyle.Grooves)
        {
            var mortar = t.Raised.Count > 0 ? Geo.Subtract([Geo.Rect(-w - 5, -h - 5, 2 * w + 5, 2 * h + 5)], Geo.Union(t.Raised)) : [];
            var rings = t.Outlined.Count > 0 ? Geo.Subtract(Geo.Offset(t.Outlined, g), Geo.Union(t.Outlined)) : [];
            etched.AddRange(Geo.Intersect(Geo.Union(Geo.Union(mortar, t.Etched), rings), area));
        }
        else
        {
            lines.AddRange(Geo.ClipLines(t.Raised.Select(Geo.Loop), area));
            lines.AddRange(Geo.ClipLines(t.Etched.Select(Geo.Loop), area));
            lines.AddRange(Geo.ClipLines(t.Outlined.Select(Geo.Loop), area));
        }
        lines.AddRange(Geo.ClipLines(t.Lines, area));
        return (Geo.Tidy(etched), lines);
    }

    /// <summary>Where each base sits in a tray (its box's corner), from the front row back, and the tray's inside size.</summary>
    public static (List<PointD> Corners, double W, double H) Layout(TraySpec t)
    {
        var (bw, bd) = BaseShapes.Box(t.Shape, t.WidthMm, t.DepthMm);
        double gap = Math.Max(0, t.GapMm), px = bw + gap, py = bd + gap;
        bool stagger = t.Stagger && t.Rows > 1 && t.Columns > 0;
        // Round bases in staggered rows nest into each other's gaps; other shapes just shift along.
        if (stagger && t.Shape == BaseShape.Round) py = Math.Max(bd * 0.5, Math.Sqrt(Math.Max(0, px * px - px * px / 4)));
        var corners = new List<PointD>();
        for (int j = 0; j < t.Rows; j++)
            for (int i = 0; i < t.Columns; i++)
            {
                if (stagger && j % 2 == 1 && i == t.Columns - 1) continue;   // a staggered row is one short, so the tray stays square
                corners.Add(new PointD(i * px + (stagger && j % 2 == 1 ? px / 2 : 0), j * py));
            }
        double w = (t.Columns - 1) * px + bw, h = (t.Rows - 1) * py + bd;
        return (corners, w, h);
    }

    static TrayKit? Tray(BaseSpec s, List<Finding> findings)
    {
        var t = s.Tray;
        if (!Check(t.Shape, t.WidthMm, t.DepthMm, findings)) return null;
        if (t.Columns < 1 || t.Rows < 1 || t.Columns * t.Rows > 200) { findings.Add(new(Severity.Bad, "Make the tray between 1 and 200 bases.")); return null; }
        if (!(t.BorderMm >= 1.5)) { findings.Add(new(Severity.Bad, "Make the tray's border at least 1.5 mm.")); return null; }
        if (t.Stagger && t.Rows > 1 && t.Columns < 2) { findings.Add(new(Severity.Bad, "Staggered rows need at least 2 bases across.")); return null; }

        double clear = Math.Max(0, t.ClearMm), border = t.BorderMm;
        var outline = BaseShapes.Outline(t.Shape, t.WidthMm, t.DepthMm, s.CornerMm);
        var (corners, iw, ih) = Layout(t);
        var bases = new PathsD(corners.SelectMany(c => Geo.Move(outline, c.x, c.y)));
        PathsD pockets;
        if (t.Style == TrayStyle.Open) pockets = [BaseShapes.RoundedRect(-clear, -clear, iw + clear, ih + clear, Math.Max(0, t.CornerMm - border))];
        else
        {
            pockets = Geo.Union(Geo.Offset(bases, clear, round: true));
            if (t.GapMm < 0.8 + 2 * clear && corners.Count > 1)
                findings.Add(new(Severity.Warn, $"The walls between the pockets are {Math.Max(0, t.GapMm - 2 * clear):0.##} mm thick or less: they'll break or burn away. Leave a gap of {0.8 + 2 * clear:0.#} mm or more, or make an open tray."));
        }
        var pb = Geo.Bounds(pockets);
        var frameOutline = new PathsD { BaseShapes.RoundedRect(pb.left - border, pb.top - border, pb.right + border, pb.bottom + border, t.CornerMm) };
        // Everything moved so the tray's corner is at 0, 0.
        double dx = -(pb.left - border), dy = -(pb.top - border);
        frameOutline = Geo.Move(frameOutline, dx, dy);
        pockets = Geo.Move(pockets, dx, dy);
        var spots = corners.Select(c => new PointD(c.x + dx, c.y + dy)).ToList();
        double fw = pb.right - pb.left + 2 * border, fh = pb.bottom - pb.top + 2 * border;

        string name = TrayName;
        var frameCut = Geo.Tidy(Geo.Subtract(frameOutline, pockets));
        var frames = new List<Part>();
        int count = Math.Max(1, t.Count), layers = Math.Clamp(t.FrameLayers, 1, 4);
        for (int i = 0; i < layers - 1; i++) frames.Add(new Part(layers == 2 ? $"{name} · lower frame" : $"{name} · frame {i + 1}", frameCut, [], [], count));
        var (etched, lines) = t.Textured ? Texture(s with { SpotMm = 0, RimMm = 0 }, frameCut, fw, fh, s.Seed * 31 + 7, rim: false) : ([], []);
        frames.Add(new Part(layers > 1 ? $"{name} · top frame" : $"{name} · frame", frameCut, etched, lines, count));

        Part? bottom = null;
        if (t.Bottom)
        {
            var (bw, bd) = BaseShapes.Box(t.Shape, t.WidthMm, t.DepthMm);
            var cut = frameOutline;
            if (t.Holes != TrayHoles.None)
            {
                double r = t.Holes == TrayHoles.Magnets ? Math.Max(0.5, s.MagnetMm > 0 ? s.MagnetMm : 5) / 2 + Math.Max(0, s.FitMm) / 4 : Math.Clamp(Math.Min(bw, bd) * 0.25, 2, 8);
                if (2 * r > Math.Min(bw, bd) - 2) { findings.Add(new(Severity.Bad, "The holes in the tray's bottom are too big for its bases: use smaller magnets.")); return null; }
                cut = Geo.Tidy(Geo.Subtract(cut, new PathsD(spots.Select(c => Geo.Circle(c.x + bw / 2, c.y + bd / 2, r)))));
            }
            bottom = new Part($"{name} · bottom", cut, [], [], count);
        }
        else if (t.Style == TrayStyle.Open) findings.Add(new(Severity.Warn, "An open tray with no bottom is just a frame: the bases slide about inside it."));
        return new TrayKit(bottom, frames, spots, outline, count);
    }

    static void Notes(BaseSpec s, List<BaseSize> sizes, TrayKit? tray, int magnets, List<Finding> findings)
    {
        int layers = s.LayerCount;
        if (sizes.Count > 0)
        {
            int total = sizes.Sum(b => b.Count);
            findings.Add(new(Severity.Note, $"{total} base{(total == 1 ? "" : "s")}" + (layers > 1 ? $", {layers} layers each: {N(layers * s.ThicknessMm)} mm thick when glued" : $", {N(s.ThicknessMm)} mm thick") + "."));
            if (s.MagnetMm > 0 && s.Layers <= 1) findings.Add(new(Severity.Note, "A magnet needs a layer under the top to sit in: each base has a bottom layer with the hole."));
            if (magnets > 0) findings.Add(new(Severity.Note, $"{magnets} magnet{(magnets == 1 ? "" : "s")}, {N(s.MagnetMm)} mm across and no thicker than {N(s.ThicknessMm)} mm: glue one into each bottom layer, all the same way up, before the top goes on."));
            if (s.Fixing == FigureFixing.Slot) findings.Add(new(Severity.Note, layers > 1 ? "The slot goes through the top layers; the bottom one closes it underneath." : "The slot goes right through: glue the figure's tab in from underneath."));
            if (s.Etch == EtchStyle.Grooves && GroundTextures.Preset(s.Texture) is { UsesGroove: true } && s.GrooveMm < 0.3)
                findings.Add(new(Severity.Warn, $"Grooves narrower than 0.3 mm may not show once painted: try 0.4 mm or more."));
        }
        if (tray is not null)
        {
            var t = s.Tray;
            int held = tray.Spots.Count;
            double deep = Math.Clamp(t.FrameLayers, 1, 4) * s.ThicknessMm;
            findings.Add(new(Severity.Note, $"{(tray.Count > 1 ? $"{tray.Count} movement trays" : "A movement tray")} for {held} {BaseShapes.Label(t.Shape, t.WidthMm, t.DepthMm).ToLowerInvariant()} bases, pockets {N(deep)} mm deep."));
            if (sizes.Count > 0 && deep < layers * s.ThicknessMm - 1e-6 && t.Style == TrayStyle.Pockets)
                findings.Add(new(Severity.Note, $"The bases are {N(layers * s.ThicknessMm)} mm thick, so they stand {N(layers * s.ThicknessMm - deep)} mm proud of the tray: easy to pick up."));
            if (sizes.Count > 0 && !sizes.Any(b => b.Shape == t.Shape && Math.Abs(b.WidthMm - t.WidthMm) < 1e-6 && (t.Shape is BaseShape.Round or BaseShape.Square or BaseShape.Hexagon || Math.Abs(b.DepthMm - t.DepthMm) < 1e-6)))
                findings.Add(new(Severity.Warn, "The tray is for a different size of base than any in the list."));
            if (t.Holes == TrayHoles.Magnets) findings.Add(new(Severity.Note, "Magnets in the tray's bottom hold magnetised bases: put them in the other way up to the bases' magnets so they pull, not push."));
            if (s.MagnetMm > 0 && s.LayerCount > 1 && t.Holes == TrayHoles.PushOut) findings.Add(new(Severity.Note, "Push the bases out of their pockets through the holes underneath."));
        }
    }
}
