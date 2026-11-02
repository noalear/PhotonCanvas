using Clipper2Lib;

namespace PhotonCanvas.Addon.Furniture;

/// <summary>
/// Chairs: a back (the back legs, the seat rail and the back rest in one piece), a seat whose tabs go
/// into the back, and a front leg frame whose tabs go up through the seat.
/// </summary>
public static class Seating
{
    public static void Chair(Maker m)
    {
        var it = m.Item;
        double W = it.WidthMm, D = it.DepthMm, Hs = it.SeatMm, Hb = it.HeightMm, T = m.T, f = m.Fit, sw = m.SlotW;
        double legs = it.LegMm > 0 ? it.LegMm : Math.Max(2.2, T + 0.4);
        double apron = Math.Clamp((Hs - T) * 0.22, 1.2, 3);
        double si = 0.5, fi = 1.0;
        if (Hs - T < 4) { m.Bad($"The seat is too low for a {T:0.##} mm sheet: make it at least {T + 4:0.#} mm high."); return; }
        if (Hb < Hs + 3) { m.Bad($"The back is too low: make the chair at least {Hs + 3:0.#} mm high (the seat is {Hs:0.#} mm)."); return; }
        if (W < 2 * legs + 2 + 2 * si) { m.Bad($"The chair is too narrow: make it at least {2 * legs + 2 + 2 * si:0.#} mm wide."); return; }
        if (D < T + fi + T + 3) { m.Bad($"The seat is too shallow: make the chair at least {2 * T + fi + 3:0.#} mm deep."); return; }

        // The seat: from the back's front face to the front, its tabs back through the back.
        double tw = Math.Clamp(W * 0.18, 1.2, 4);
        double[] backTabs = [W * 0.27, W * 0.73];
        var seat = new PathsD { Geo.Rect(0, 0, W, D - T) };
        foreach (var u in backTabs) seat.Add(Geo.Rect(u - tw / 2, D - T - 0.01, u + tw / 2, D));
        // Its slots for the front frame's tabs, over the front legs.
        double Wf = W - 2 * si, ftw = Math.Max(1.2, legs - 1), zf = D - fi - T / 2;
        double[] frontTabs = [legs / 2, Wf - legs / 2];
        var seatSlots = new PathsD();
        foreach (var u in frontTabs)
            seatSlots.Add(Geo.Rect(si + u - (ftw + f) / 2, D - zf - sw / 2, si + u + (ftw + f) / 2, D - zf + sw / 2));
        m.Add("Seat", seat, seatSlots, [Pose.Up(0, Hs - T / 2, D)], grainAlongU: true, face: [Geo.Rect(0, 0, W, D - T)], planks: true, plankMm: 3);

        // The back: back legs, the rail under the seat, and the back rest.
        var back = new PathsD { WoodFace.RoundRect(0, -2, W, Hb, Math.Min(0.8, legs / 2)) };
        back = Geo.Intersect(back, [Geo.Rect(0, 0, W, Hb)]);
        var holes = new PathsD { WoodFace.RoundRect(legs, -3, W - legs, Hs - T - apron, Math.Min(0.8, (W - 2 * legs) / 2)) };
        foreach (var u in backTabs) holes.Add(Geo.Rect(u - (tw + f) / 2, Hs - T - f / 2, u + (tw + f) / 2, Hs + f / 2));
        double r0 = Hs + 1.2, rail = Math.Clamp((Hb - Hs) * 0.18, 1.2, 3), r1 = Hb - rail, rw = W - 2 * legs;
        const double bar = 1.2, gapMin = 1.0;
        if (r1 - r0 > gapMin && it.Back != BackStyle.Panel)
        {
            if (it.Back == BackStyle.Slats)
            {
                int n = (int)Math.Floor((rw - gapMin) / (bar + gapMin));
                double gap = (rw - n * bar) / (n + 1);
                if (gap >= gapMin)
                    for (int i = 0; i <= n; i++)
                    {
                        double x0 = legs + i * (gap + bar);
                        holes.Add(Geo.Rect(x0, r0, x0 + gap, r1));
                    }
            }
            else
            {
                int n = (int)Math.Floor((r1 - r0 - 1.5) / (bar + 1.5));
                double gap = (r1 - r0 - n * bar) / (n + 1);
                if (rw >= gapMin)
                    for (int i = 0; i <= n; i++)
                    {
                        double y0 = r0 + i * (gap + bar);
                        holes.Add(Geo.Rect(legs, y0, W - legs, y0 + gap));
                    }
            }
        }
        m.Add("Back", back, holes, [Pose.Front(0, 0, T / 2)], grainAlongU: false);

        // The front legs and the rail between them, its tabs up through the seat.
        var front = new PathsD { Geo.Rect(0, 0, Wf, Hs - T) };
        foreach (var u in frontTabs) front.Add(Geo.Rect(u - ftw / 2, Hs - T - 0.01, u + ftw / 2, Hs));
        m.Add("Front legs", front, [WoodFace.RoundRect(legs, -3, Wf - legs, Hs - T - apron, Math.Min(0.8, (Wf - 2 * legs) / 2))],
            [Pose.Front(si, 0, zf)], grainAlongU: false, face: [Geo.Rect(0, 0, Wf, Hs - T)]);
        m.Note("Push the seat's tabs into the back, then the front legs' tabs up through the seat.");
    }
}
