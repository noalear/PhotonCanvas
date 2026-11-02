using Clipper2Lib;

namespace PhotonCanvas.Addon.Furniture;

/// <summary>
/// Beds: a head board and a foot board (with legs), a mattress panel whose tabs go through both, and
/// two side rails under its edges whose tabs go through both too. A pillow and a turned-down blanket
/// can be etched on the mattress.
/// </summary>
public static class Beds
{
    public static void Bed(Maker m)
    {
        var it = m.Item;
        double L = it.WidthMm, D = it.DepthMm, Hm = it.MattressMm, T = m.T, f = m.Fit, sw = m.SlotW;
        double Hh = Math.Max(it.HeightMm, Hm + 1), Hf = Math.Max(it.FootMm, Hm + 1);
        double si = 1.0, legs = it.LegMm > 0 ? it.LegMm : Math.Max(2.5, T + 0.5);
        double yb = Hm - T, rh = Math.Clamp(yb * 0.35, 2.2, 3.5), th = Math.Max(1.2, rh - 1);
        if (yb - rh < 1.5) { m.Bad($"The mattress is too low for a {T:0.##} mm sheet: make it at least {T + 1.5 + 2.2:0.#} mm up."); return; }
        if (L - 2 * T < 8) { m.Bad($"The bed is too short: make it at least {2 * T + 8:0.#} mm long."); return; }
        if (D < 2 * legs + 4) { m.Bad($"The bed is too narrow: make it at least {2 * legs + 4:0.#} mm wide."); return; }

        double Db = D - 2 * si, Lb = L - 2 * T;
        double tw = Math.Clamp(Db * 0.2, 1.2, 5);
        double[] tabV = [Db * 0.28, Db * 0.72];
        double[] railZ = [si + T / 2, D - si - T / 2];
        double yr = yb - rh / 2;

        // Head and foot boards: legs at the sides, slots for the mattress's and the rails' tabs.
        PathsD Board(double h)
        {
            var o = new PathsD { WoodFace.RoundRect(0, -3, D, h, Math.Min(2, D * 0.12)) };
            return Geo.Intersect(o, [Geo.Rect(0, 0, D, h)]);
        }
        var holes = new PathsD { WoodFace.RoundRect(legs, -3, D - legs, yr - th / 2 - 1.2, Math.Min(1, (D - 2 * legs) / 2)) };
        foreach (var v in tabV)
        {
            double z = D - si - v;
            holes.Add(Geo.Rect(z - (tw + f) / 2, yb - f / 2, z + (tw + f) / 2, yb + T + f / 2));
        }
        foreach (var z in railZ) holes.Add(Geo.Rect(z - sw / 2, yr - (th + f) / 2, z + sw / 2, yr + (th + f) / 2));
        m.Add("Head board", Board(Hh), holes, [Pose.Left(T / 2, 0, 0)], grainAlongU: false, planks: true, plankMm: 3);
        m.Add("Foot board", Board(Hf), holes, [Pose.Left(L - T / 2, 0, 0)], grainAlongU: false, planks: true, plankMm: 3);

        // The mattress (seen from above: v from the far side to the near one), its tabs through the boards.
        var mat = new PathsD { Geo.Rect(0, 0, Lb, Db) };
        foreach (var v in tabV)
        {
            mat.Add(Geo.Rect(-T, v - tw / 2, 0.01, v + tw / 2));
            mat.Add(Geo.Rect(Lb - 0.01, v - tw / 2, Lb + T, v + tw / 2));
        }
        var etched = new PathsD();
        var lines = new List<PathD>();
        if (it.Bedding) Bedding(Lb, Db, etched, lines);
        m.Add("Mattress", mat, [], [Pose.Up(T, yb + T / 2, D - si)], grainAlongU: true, face: [], etched: etched, lines: lines);

        // Side rails under the mattress's edges.
        var rail = new PathsD
        {
            Geo.Rect(0, 0, Lb, rh),
            Geo.Rect(-T, (rh - th) / 2, 0.01, (rh + th) / 2),
            Geo.Rect(Lb - 0.01, (rh - th) / 2, Lb + T, (rh + th) / 2),
        };
        m.Add("Side rail", rail, [], railZ.Select(z => Pose.Front(T, yb - rh, z)), grainAlongU: true, face: [Geo.Rect(0, 0, Lb, rh)]);
        m.Note("Put the mattress and the rails into the head board, then the foot board on their other ends.");
    }

    /// <summary>A pillow at the head and a blanket turned down over the rest, quilted.</summary>
    static void Bedding(double Lb, double Db, PathsD etched, List<PathD> lines)
    {
        double m = Math.Min(0.6, Db * 0.05);
        double pl = Math.Min(Lb * 0.18, 6), pw = Db - 2 * m - Math.Min(2, Db * 0.12);
        if (pl > 1.5 && pw > 2)
        {
            double v0 = (Db - pw) / 2;
            lines.Add(Geo.Loop(WoodFace.RoundRect(m + 0.4, v0, m + 0.4 + pl, v0 + pw, Math.Min(1.2, pl / 3))));
            lines.Add([new(m + 0.4 + pl * 0.5, v0 + pw * 0.15), new(m + 0.4 + pl * 0.45, v0 + pw * 0.5), new(m + 0.4 + pl * 0.5, v0 + pw * 0.85)]);
        }
        double b0 = Math.Min(Lb * 0.32, pl + 2.5), fold = Math.Min(2.5, (Lb - b0) * 0.15);
        if (Lb - b0 < 4) return;
        // The turned-down fold: etched; the blanket's edge and quilting: scored.
        etched.Add(Geo.Rect(b0, m, b0 + fold, Db - m));
        lines.Add(Geo.Loop(Geo.Rect(b0, m, Lb - m, Db - m)));
        double step = Math.Max(2.5, Math.Min(4, Db / 4));
        for (double s = b0 + fold + step; s < Lb - m - 0.5; s += step) lines.Add([new(s, m), new(s, Db - m)]);
        for (double s = m + step; s < Db - m - 0.5; s += step) lines.Add([new(b0 + fold, s), new(Lb - m, s)]);
    }
}
