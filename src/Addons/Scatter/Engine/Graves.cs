using Clipper2Lib;

namespace PhotonCanvas.Addon.Scatter;

/// <summary>
/// A gravestone: a headstone whose tab goes down through a slot in a base, or in the back of a grave
/// slab. Lettering and a border are scored on its face; weathering chips its edges and scores cracks.
/// </summary>
public static class Graves
{
    public static void Make(Maker m)
    {
        var it = m.Item;
        double W = it.WidthMm, H = it.HeightMm, T = m.T, f = m.Fit, sw = m.SlotW, B = Maker.Bridge, k = m.Scale;
        if (W < 4) { m.Bad("The gravestone is too narrow: make it at least 4 mm wide."); return; }
        var shape = it.Shape;
        double bw = Math.Clamp(W * 0.34, 1.6, Math.Max(1.6, W * 0.5));
        double need = shape switch
        {
            StoneShape.Rounded => W / 2 + 2,
            StoneShape.Pointed => W * 0.87 + 2,
            StoneShape.Cross or StoneShape.Celtic => W / 2 + bw / 2 + 3,
            _ => 4,
        };
        if (H < need) { m.Bad($"The gravestone is too low for its width and shape: make it at least {need:0.#} mm high, or narrower."); return; }

        // The stone, flat: u across, v up from the top of the base.
        var stone = Outline(shape, W, H, bw, out double ya);
        var etched = new PathsD();
        var lines = new List<PathD>();
        if (it.Inscription) Lettering(shape, W, H, bw, ya, k, stone, etched, lines);
        if (it.Weathered) Weather(stone, W, H, k, m.Rnd, lines);
        double tw = Math.Clamp(W * 0.45, 1.2, 8);
        if (shape is StoneShape.Cross or StoneShape.Celtic) tw = Math.Min(tw, W * 0.6 - 0.4);
        stone.Add(Geo.Rect(W / 2 - tw / 2, -T, W / 2 + tw / 2, 0.01));

        // The base, or a grave slab in front of the stone.
        double mz = Math.Max(B + f + 0.2, 1.6), mb = Math.Clamp(W * 0.15, 1.2, 3);
        double slab = Math.Max(0, it.SlabMm), Wb = W + 2 * mb, Db = T + 2 * mz + slab;
        double zc = mz + T / 2;
        var basePoly = new PathsD { Chamfer(0, 0, Wb, Db, Math.Min(0.5, mz * 0.3)) };
        var baseSlot = new PathsD { Joints.Centred(mb + W / 2, Db - zc, tw + f, sw) };
        var baseEtch = new PathsD();
        var baseLines = new List<PathD>();
        if (slab > 0)
        {
            // A groove round the slab, and a cross on it.
            var inner = new PathsD { Geo.Rect(0.8, 0.8, Wb - 0.8, Db - 2 * mz - T - 0.4) };
            if (Db - 2 * mz - T - 1.6 > 1.5 && Wb > 3)
            {
                baseEtch.AddRange(Geo.Subtract(Geo.Offset(inner, 0.15), Geo.Offset(inner, -0.15)));
                if (it.Inscription)
                {
                    double cx = Wb / 2, cy = (0.8 + Db - 2 * mz - T - 0.4) / 2, ch = Math.Min(slab * 0.5, Wb * 0.6), cw = ch * 0.6;
                    baseLines.Add([new(cx, cy - ch / 2), new(cx, cy + ch / 2)]);
                    baseLines.Add([new(cx - cw / 2, cy + ch * 0.18), new(cx + cw / 2, cy + ch * 0.18)]);
                }
            }
            if (it.Weathered) Weather(basePoly, Wb, Db - 2 * mz - T, k, m.Rnd, baseLines, chip: false);
        }
        m.Add("Headstone", stone, [], [Pose.Front(mb, T, zc)], grainAlongU: false, wood: false, etched: etched, lines: lines);
        m.Add(slab > 0 ? "Grave slab" : "Base", basePoly, baseSlot, [Pose.Up(0, T / 2, Db)], grainAlongU: true, wood: false, etched: baseEtch, lines: baseLines);
        m.Note("Push the headstone's tab down into the slot" + (slab > 0 ? " at the back of the slab." : " in the base."));
    }

    static PathD Chamfer(double x0, double y0, double x1, double y1, double c) =>
        [new(x0 + c, y0), new(x1 - c, y0), new(x1, y0 + c), new(x1, y1 - c), new(x1 - c, y1), new(x0 + c, y1), new(x0, y1 - c), new(x0, y0 + c)];

    /// <summary>The stone's outline; <paramref name="ya"/> is where a cross's arms start.</summary>
    static PathsD Outline(StoneShape shape, double W, double H, double bw, out double ya)
    {
        ya = 0;
        switch (shape)
        {
            case StoneShape.Rounded:
                return Geo.Union([Geo.Rect(0, 0, W, H - W / 2), Geo.Circle(W / 2, H - W / 2, W / 2)]);
            case StoneShape.Pointed:
            {
                double ys = H - W * Math.Sqrt(3) / 2;
                var p = new PathD { new(0, 0), new(W, 0) };
                // Two arcs, each centred on the other side, meeting at the point.
                p.AddRange(Geo.Arc(0, ys, W, 0, Math.PI / 3));
                p.AddRange(Geo.Arc(W, ys, W, Math.PI * 2 / 3, Math.PI).Skip(1));
                return Geo.Union([p]);
            }
            case StoneShape.Cross:
            case StoneShape.Celtic:
            {
                double arm = W / 2 - bw / 2;
                ya = H - bw - arm;
                var parts = new PathsD
                {
                    Geo.Rect(W / 2 - bw / 2, 0, W / 2 + bw / 2, H),
                    Geo.Rect(0, ya, W, ya + bw),
                    Geo.Rect(W * 0.2, 0, W * 0.8, Math.Min(ya * 0.18, 2.2)),
                };
                if (shape == StoneShape.Celtic)
                {
                    double cy = ya + bw / 2, rr = W / 2 * 0.66, rw = Math.Max(Maker.Bridge * 1.15, W * 0.09);
                    parts.AddRange(Geo.Subtract([Geo.Circle(W / 2, cy, rr)], [Geo.Circle(W / 2, cy, rr - rw)]));
                }
                return Geo.Union(parts);
            }
            default:
            {
                double c = W * 0.12;
                return [[new(0, 0), new(W, 0), new(W, H - c), new(W - c, H), new(c, H), new(0, H - c)]];
            }
        }
    }

    /// <summary>"R.I.P." and lines of words scored on, with a border; on a cross, a boss and the ring's groove.</summary>
    static void Lettering(StoneShape shape, double W, double H, double bw, double ya, double k, PathsD stone, PathsD etched, List<PathD> lines)
    {
        if (shape is StoneShape.Cross or StoneShape.Celtic)
        {
            double cy = ya + bw / 2;
            lines.Add(WoodFace.Dot(W / 2, cy, Math.Min(bw * 0.28, 1.2)));
            if (shape == StoneShape.Celtic)
            {
                double rr = W / 2 * 0.66, rw = Math.Max(Maker.Bridge * 1.15, W * 0.09);
                lines.Add(Geo.Loop(Geo.Circle(W / 2, cy, rr - rw / 2)));
            }
            // Words down the shaft.
            double y = ya - 1.2 * k;
            for (int i = 0; i < 3 && y > 2.5; i++, y -= 1.1 * k)
                lines.Add([new(W / 2 - bw * 0.3, y), new(W / 2 + bw * 0.3, y)]);
            return;
        }
        // A border just inside the edge.
        foreach (var p in Geo.Offset(stone, -Math.Clamp(0.6 * k, 0.4, 1.2))) lines.Add(Geo.Loop(p));
        double lh = Math.Clamp(W * 0.13, 0.9, Math.Max(0.9, 2.2 * k)), top = shape == StoneShape.Square ? H * 0.82 : H - W / 2 * (shape == StoneShape.Pointed ? 0.9 : 0.55);
        lines.AddRange(Rip(W / 2, top - lh, lh));
        // Lines of words: dashes of different lengths.
        var rnd = new Random((int)(W * 100 + H * 7));
        double ly = top - lh * 2.1;
        double[] widths = [0.62, 0.48, 0.55, 0.4];
        for (int i = 0; i < widths.Length && ly > 1.6 * k; i++, ly -= lh * 1.15)
        {
            double half = W * widths[i] / 2, x = W / 2 - half;
            while (x < W / 2 + half - 0.4)
            {
                double w = Math.Min(W / 2 + half - x, 0.6 + rnd.NextDouble() * 1.6 * k);
                lines.Add([new(x, ly), new(x + w, ly)]);
                x += w + 0.35 * k;
            }
        }
    }

    /// <summary>R.I.P. in strokes, centred on <paramref name="cx"/>, its letters <paramref name="h"/> high from <paramref name="y"/>.</summary>
    static IEnumerable<PathD> Rip(double cx, double y, double h)
    {
        double w = h * 0.6, gap = h * 0.32, dot = h * 0.08, total = 3 * w + 3 * gap + 2 * dot - w * 0.6;
        double x = cx - total / 2;
        PathD L(params (double X, double Y)[] p) => [.. p.Select(q => new PointD(x + q.X * h, y + q.Y * h))];
        // R
        yield return L((0, 0), (0, 1), (0.42, 1), (0.58, 0.9), (0.58, 0.62), (0.42, 0.52), (0, 0.52));
        yield return L((0.22, 0.52), (0.6, 0));
        x += w + gap * 0.5;
        yield return WoodFace.Dot(x + dot, y + dot, dot);
        x += 2 * dot + gap * 0.5;
        // I
        yield return L((0, 0), (0, 1));
        x += gap * 0.5;
        yield return WoodFace.Dot(x + dot, y + dot, dot);
        x += 2 * dot + gap * 0.5;
        // P
        yield return L((0, 0), (0, 1), (0.42, 1), (0.58, 0.9), (0.58, 0.62), (0.42, 0.52), (0, 0.52));
        x += w + gap * 0.5;
        yield return WoodFace.Dot(x + dot, y + dot, dot);
    }

    /// <summary>Chips out of the upper edges (if <paramref name="chip"/>) and a crack or two.</summary>
    static void Weather(PathsD outline, double W, double H, double k, Random rnd, List<PathD> lines, bool chip = true)
    {
        var chips = new PathsD();
        var edge = Geo.Tidy(outline).OrderByDescending(p => Math.Abs(Clipper.Area(p))).First();
        int n = chip ? 2 + rnd.Next(3) : 0;
        for (int tries = 0; tries < 40 && chips.Count < n; tries++)
        {
            var p = edge[rnd.Next(edge.Count)];
            if (p.y < H * 0.35) continue;
            double s = (0.35 + rnd.NextDouble() * 0.5) * k;
            double a = rnd.NextDouble() * Math.PI * 2;
            chips.Add([new(p.x + Math.Cos(a) * s, p.y + Math.Sin(a) * s), new(p.x + Math.Cos(a + 2.2) * s, p.y + Math.Sin(a + 2.2) * s), new(p.x + Math.Cos(a + 4.1) * s * 0.8, p.y + Math.Sin(a + 4.1) * s * 0.8)]);
        }
        if (chips.Count > 0)
        {
            var chipped = Geo.Subtract(outline, chips);
            // Keep the chips small: a chip mustn't break off a bit of the stone.
            if (Geo.Pieces(chipped) == Geo.Pieces(outline)) { outline.Clear(); outline.AddRange(chipped); }
        }
        int cracks = 1 + rnd.Next(2);
        for (int c = 0; c < cracks; c++)
        {
            double x = W * (0.2 + rnd.NextDouble() * 0.6), y = H * (0.75 + rnd.NextDouble() * 0.2);
            var line = new PathD { new(x, y + 2) };
            double ang = -Math.PI / 2 + (rnd.NextDouble() - 0.5) * 1.2;
            for (int s = 0; s < 4; s++)
            {
                ang += (rnd.NextDouble() - 0.5) * 1.0;
                x += Math.Cos(ang) * 0.9 * k;
                y += Math.Sin(ang) * 0.9 * k;
                line.Add(new(x, y));
            }
            lines.Add(line);
        }
    }
}
