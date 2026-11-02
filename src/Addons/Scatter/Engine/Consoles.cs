using Clipper2Lib;

namespace PhotonCanvas.Addon.Scatter;

/// <summary>
/// A sci-fi control console: a finger-jointed cabinet (open underneath) with a keypad on its top and a
/// screen standing at the back, its two tabs pushed down through slots in the top. The metal detail
/// (panel lines, rivets, vents, the keys and the readout on the screen) is etched and scored.
/// </summary>
public static class Consoles
{
    public static void Make(Maker m)
    {
        var it = m.Item;
        double W = it.WidthMm, D = it.DepthMm, H = it.HeightMm, T = m.T, f = m.Fit, sw = m.SlotW, B = Maker.Bridge;
        double minSide = 3 * T + 2;
        if (W < minSide || D < minSide) { m.Bad($"The console is too small for a {T:0.##} mm sheet: make it at least {minSide:0.#} mm wide and deep."); return; }
        double Hc = Math.Round(Math.Clamp(H * 0.55, minSide, H - 4), 2), Hs = H - Hc;
        if (H < minSide + 4) { m.Bad($"The console is too low: make it at least {minSide + 4:0.#} mm high (the desk and its screen)."); return; }

        // The screen stands near the back, on two tabs through the top.
        double zs = T + B + sw / 2 + 0.3, margin = T + 0.8, Ws = W - 2 * margin;
        double tw = Math.Clamp(Ws * 0.18, 1.5, 5);
        double[] tabs = [W * 0.27, W * 0.73];
        if (Ws < 2 * tw + 2) tabs = [W / 2];

        var box = Joints.Box([W, Hc, D], T, [true, true, false, true, true, true], ["Left side", "Right side", "Bottom", "Top", "Back", "Front"]);
        foreach (var p in box)
        {
            var holes = new PathsD();
            var etched = new PathsD();
            var lines = new List<PathD>();
            switch (p.Name)
            {
                case "Top":
                    foreach (var x in tabs) holes.Add(Joints.Centred(x, D - zs, tw + f, sw));
                    Keypad(p.Lu, T + 0.8, D - zs - sw / 2 - B - 0.4, T + 0.6, etched, lines);
                    break;
                case "Front":
                    Panel(p.Lu, p.Lv, T, lines);
                    Vents(p.Lu, p.Lv, T, etched);
                    break;
                default:
                    Panel(p.Lu, p.Lv, T, lines);
                    break;
            }
            m.Add(p.Name, p.Cut, holes, [p.Pose], grainAlongU: true, wood: false, etched: etched, lines: lines);
        }

        // The screen: angled top corners, a bezel and a readout.
        double ch = Math.Min(Ws, Hs) * 0.18;
        var screen = new PathsD { new PathD { new(0, 0), new(Ws, 0), new(Ws, Hs - ch), new(Ws - ch, Hs), new(ch, Hs), new(0, Hs - ch) } };
        foreach (var x in tabs) screen.Add(Geo.Rect(x - margin - tw / 2, -T, x - margin + tw / 2, 0.01));
        var se = new PathsD();
        var sl = new List<PathD>();
        double bz = Math.Clamp(Math.Min(Ws, Hs) * 0.1, 0.8, 2);
        if (Ws > 2 * bz + 2 && Hs > 2 * bz + 2)
        {
            var inner = Geo.Rect(bz, bz, Ws - bz, Hs - bz - ch * 0.6);
            se.AddRange(Geo.Subtract(Geo.Offset([inner], 0.3), [inner]));
            Readout(m, bz + 0.6, bz + 0.6, Ws - bz - 0.6, Hs - bz - ch * 0.6 - 0.6, sl);
        }
        m.Add("Screen", screen, [], [Pose.Front(margin, Hc, zs)], grainAlongU: true, wood: false, etched: se, lines: sl);
        m.Note("Glue the cabinet's sides together (the fingers line them up), then push the screen's tabs down into the slots in the top.");
    }

    /// <summary>A panel line round the face, inside the joints, with angled corners and a rivet at each.</summary>
    static void Panel(double Lu, double Lv, double T, List<PathD> lines)
    {
        double i = T + 0.7, c = Math.Min(Lu, Lv) * 0.12;
        if (Lu - 2 * i < 3 || Lv - 2 * i < 3) return;
        lines.Add(Geo.Loop([new(i + c, i), new(Lu - i - c, i), new(Lu - i, i + c), new(Lu - i, Lv - i - c), new(Lu - i - c, Lv - i), new(i + c, Lv - i), new(i, Lv - i - c), new(i, i + c)]));
        double r = 0.3, o = i + 0.9;
        if (Lu - 2 * o > 2 && Lv - 2 * o > 2)
            foreach (var (x, y) in new[] { (o, o), (Lu - o, o), (Lu - o, Lv - o), (o, Lv - o) }) lines.Add(WoodFace.Dot(x, y, r));
    }

    /// <summary>Rows of etched vent slots low on the front.</summary>
    static void Vents(double Lu, double Lv, double T, PathsD etched)
    {
        double i = T + 1.6, w = (Lu - 2 * i) * 0.5, h = 0.5, step = 1.1;
        if (w < 3) return;
        int rows = Math.Clamp((int)((Lv * 0.45 - i) / step), 0, 5);
        for (int k = 0; k < rows; k++)
            etched.Add(WoodFace.RoundRect(Lu / 2 - w / 2, i + k * step, Lu / 2 + w / 2, i + k * step + h, h / 2));
    }

    /// <summary>Keys etched in a grid on the top between <paramref name="v0"/> and <paramref name="v1"/> (from the front), and a few buttons.</summary>
    static void Keypad(double Lu, double v0, double v1, double side, PathsD etched, List<PathD> lines)
    {
        double k = 0.9, gap = 0.5;
        int cols = (int)((Lu - 2 * side - 3) / (k + gap)), rows = (int)((v1 - v0) / (k + gap));
        if (cols < 2 || rows < 1) return;
        cols = Math.Min(cols, 16);
        rows = Math.Min(rows, 4);
        double x0 = side + 2.5;
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
                etched.Add(Geo.Rect(x0 + c * (k + gap), v0 + r * (k + gap), x0 + c * (k + gap) + k, v0 + r * (k + gap) + k));
        // A couple of round buttons to one side.
        double bx = side + 1.2;
        for (int r = 0; r < Math.Min(rows, 3); r++) lines.Add(WoodFace.Dot(bx, v0 + r * (k + gap) + k / 2, 0.45));
    }

    /// <summary>A readout on the screen: a trace, some bars and lines of text.</summary>
    static void Readout(Maker m, double x0, double y0, double x1, double y1, List<PathD> lines)
    {
        double w = x1 - x0, h = y1 - y0;
        if (w < 3 || h < 2) return;
        var trace = new PathD();
        double ph = m.Rnd.NextDouble() * 6, yc = y0 + h * 0.7;
        for (double x = x0; x <= x0 + w * 0.6; x += 0.25)
            trace.Add(new PointD(x, yc + h * 0.15 * Math.Sin((x - x0) * 1.7 + ph) * Math.Sin((x - x0) * 0.4 + ph)));
        lines.Add(trace);
        // Bars to the right.
        int bars = Math.Max(2, (int)(w * 0.35 / 0.9));
        for (int i = 0; i < bars; i++)
        {
            double x = x0 + w * 0.65 + i * 0.9, top = y0 + h * (0.45 + 0.5 * m.Rnd.NextDouble());
            if (x > x1 - 0.2) break;
            lines.Add([new(x, y0 + h * 0.45), new(x, top)]);
        }
        // Lines of text along the bottom.
        for (double y = y0 + 0.3; y < y0 + h * 0.38; y += 0.7)
            lines.Add([new(x0, y), new(x0 + w * (0.4 + 0.5 * m.Rnd.NextDouble()), y)]);
    }
}
