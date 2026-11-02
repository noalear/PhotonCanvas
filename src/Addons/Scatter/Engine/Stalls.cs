using Clipper2Lib;

namespace PhotonCanvas.Addon.Scatter;

/// <summary>
/// A market stall: two side frames (a post at the front and a taller one at the back, the counter's
/// end and a bar along the top) joined by a counter top and a counter front whose tabs go through
/// them, and an awning lying on the frames' sloping tops, their tabs up through it.
/// </summary>
public static class Stalls
{
    public static void Make(Maker m)
    {
        var it = m.Item;
        double W = it.WidthMm, D = it.DepthMm, H = it.HeightMm, Hc = it.CounterMm, rise = Math.Max(0, it.RiseMm);
        double T = m.T, f = m.Fit, sw = m.SlotW, B = Maker.Bridge;
        double p = Math.Max(1.6, Math.Min(T * 1.1, 3)), bar = Math.Clamp(H * 0.08, 1.6, 3), rail = Math.Clamp(H * 0.05, 1.2, 2.2);
        // The counter's depth: room for a tab between the front post and its back, and room behind it for the stallholder.
        double dcMin = p + 2 * B + 1.2 + f + 0.5, Dc = Math.Min(Math.Max(D * 0.4, dcMin), D - p - 1.5);
        if (W < 4 * T + 6) { m.Bad($"The stall is too narrow: make it at least {4 * T + 6:0.#} mm wide."); return; }
        if (D < dcMin + p + 1.5) { m.Bad($"The stall is too shallow: make it at least {dcMin + p + 1.5:0.#} mm deep."); return; }
        if (Hc < 4 || Hc > H - bar - 3) { m.Bad($"The counter has to be between 4 mm high and {H - bar - 3:0.#} mm (under the awning)."); return; }

        // The counter top's tab on each frame, between the counter's back and the front post.
        double zlo = D - Dc + B, zhi = D - p - f / 2 - B;
        double ctw = Math.Clamp(zhi - zlo - f, 1.2, 3), czc = (zlo + zhi) / 2;
        if (zhi - zlo < 1.2 + f) { m.Bad("The counter is too shallow for its tabs: make the stall deeper."); return; }
        // The counter front, just behind the front edge.
        double zf = D - B - T / 2, fth = Math.Clamp(Hc * 0.22, 1.2, 3);
        double[] fty = [Hc * 0.3, Hc * 0.72];

        var slope = new Slope(D, H, 0, H + rise);
        double Ls = slope.Length, atw = Math.Clamp(Ls * 0.15, 1.2, 4);
        double[] at = [Ls * 0.22, Ls * 0.78];

        // The side frames, each drawn where it stands (so the right one is the left one turned round, etched side out).
        foreach (var (name, x, pose) in new[] { ("Left side", T / 2, Pose.Left(T / 2, 0, 0)), ("Right side", W - T / 2, Pose.Right(W - T / 2, 0, D)) })
        {
            PathD ZY(params (double Z, double Y)[] pts) => Geo.Positive([.. pts.Select(q => Joints.Flat(pose, new V3(x, q.Y, q.Z)))]);
            PathD Box(double z0, double y0, double z1, double y1) => ZY((z0, y0), (z1, y0), (z1, y1), (z0, y1));
            double top0 = H + rise, top1 = H;
            var frame = new PathsD
            {
                Box(0, 0, p, top0 + 1),                       // back post
                Box(D - p, 0, D, top1 + 1),                   // front post
                Box(D - Dc, 0, D, Hc),                         // the counter's end
                Box(0, 0, D, rail),                            // a rail along the ground
                ZY((0, top0 - bar), (D, top1 - bar), (D, top1 + 1), (0, top0 + 1)), // the bar under the awning
            };
            var outline = Geo.Intersect(Geo.Union(frame), slope.Below(pose, x));
            foreach (var t in at) outline.Add(slope.Tab(pose, x, t, atw, T));
            outline.Add(Box(czc - ctw / 2, Hc - 0.01, czc + ctw / 2, Hc + T));
            var holes = new PathsD();
            foreach (var y in fty) holes.Add(Box(zf - sw / 2, y - (fth + f) / 2, zf + sw / 2, y + (fth + f) / 2));
            m.Add(name, outline, holes, [pose], grainAlongU: false);
        }

        // The counter top: notched round the front posts, the frames' tabs up through it.
        double oh = 1.5;
        var ct = new PathsD { Geo.Rect(0, 0, W + 2 * oh, Dc) };
        var ctHoles = new PathsD
        {
            Geo.Rect(-1, -1, oh + T + f / 2, p + f / 2),
            Geo.Rect(W + oh - T - f / 2, -1, W + 2 * oh + 1, p + f / 2),
        };
        foreach (var x in new[] { T / 2, W - T / 2 }) ctHoles.Add(Joints.Centred(x + oh, D - czc, sw, ctw + f));
        m.Add("Counter top", ct, ctHoles, [Pose.Up(-oh, Hc + T / 2, D)], grainAlongU: true, planks: true, plankMm: 2.5);

        // The counter front, its tabs through the frames.
        double Lf = W - 2 * T;
        var front = new PathsD { Geo.Rect(0, 0, Lf, Hc) };
        foreach (var y in fty)
        {
            front.Add(Geo.Rect(-T, y - fth / 2, 0.01, y + fth / 2));
            front.Add(Geo.Rect(Lf - 0.01, y - fth / 2, Lf + T, y + fth / 2));
        }
        m.Add("Counter front", front, [], [Pose.Front(T, 0, zf)], grainAlongU: false, face: [Geo.Rect(0, 0, Lf, Hc)], planks: true, plankMm: 2.4);

        // The awning.
        double o = Math.Clamp(D * 0.12, 1.5, 4), Wa = W + 2 * o, La = Ls + 2 * o;
        var pose2 = slope.Panel(-o, W + o, o, T);
        var awning = new PathsD { Geo.Rect(0, 0, Wa, La) };
        double rs = Math.Clamp(Wa / 24, 0.9, 2.2);
        if (it.Scallops)
        {
            int n = Math.Max(2, (int)Math.Round(Wa / (2 * rs)));
            double step = Wa / n, r = step / 2;
            for (int i = 0; i < n; i++) awning.Add(Geo.Circle(step * (i + 0.5), 0.01, r));
            awning = Geo.Intersect(Geo.Union(awning), [Geo.Rect(0, -r, Wa, La)]);
        }
        var slots = new PathsD();
        foreach (var x in new[] { T / 2, W - T / 2 })
            foreach (var t in at) slots.Add(slope.Slot(pose2, x, t, atw, sw, f));
        var stripes = new PathsD();
        var lines = new List<PathD>();
        if (it.Stripes)
        {
            int n = Math.Max(3, (int)Math.Round(Wa / 3.2));
            if (n % 2 == 0) n++;
            double sw2 = Wa / n;
            for (int i = 1; i < n; i += 2) stripes.Add(Geo.Rect(i * sw2, -5, (i + 1) * sw2, La + 1));
        }
        // A hem along the front.
        lines.Add([new(0, 0.6), new(Wa, 0.6)]);
        m.Add("Awning", awning, slots, [pose2], grainAlongU: false, wood: false, etched: stripes, lines: lines);

        m.Note("Push the counter front into one side, add the other side, press the counter top down over the frames' tabs, then the awning on top.");
    }
}
