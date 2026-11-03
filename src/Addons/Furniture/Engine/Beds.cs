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
        switch (m.Item.BedStyle)
        {
            case BedStyle.FourPoster: Framed(m, fourPoster: true); return;
            case BedStyle.Bunk: Framed(m, fourPoster: false); return;
            case BedStyle.Pallet: Pallet(m); return;
        }
        var it = m.Item;
        double L = it.WidthMm, D = it.DepthMm, Hm = it.MattressMm, T = m.T, f = m.Fit, sw = m.SlotW;
        double Hh = Math.Max(it.HeightMm, Hm + 1), Hf = Math.Max(it.FootMm, Hm + 1);
        double si = 1.0, legs = it.LegMm > 0 ? it.LegMm : Math.Max(2.5, T + 0.5);
        double yb = Hm - T, rh = Geo.Clamp(yb * 0.35, 2.2, 3.5), th = Math.Max(1.2, rh - 1);
        if (yb - rh < 1.5) { m.Bad($"The mattress is too low for a {T:0.##} mm sheet: make it at least {T + 1.5 + 2.2:0.#} mm up."); return; }
        if (L - 2 * T < 8) { m.Bad($"The bed is too short: make it at least {2 * T + 8:0.#} mm long."); return; }
        if (D < 2 * legs + 4) { m.Bad($"The bed is too narrow: make it at least {2 * legs + 4:0.#} mm wide."); return; }

        double Db = D - 2 * si, Lb = L - 2 * T;
        double tw = Geo.Clamp(Db * 0.2, 1.2, 5);
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

    /// <summary>
    /// A four-poster (posts up to a canopy on two rails, the boards between the posts) or a bunk bed (two
    /// mattresses between tall boards, a ladder cut in the foot board and a guard rail along the top bunk).
    /// </summary>
    static void Framed(Maker m, bool fourPoster)
    {
        var it = m.Item;
        var look = Fronts.Look(m);
        double L = it.WidthMm, D = it.DepthMm, T = m.T, f = m.Fit, sw = m.SlotW, H = it.HeightMm;
        double si = 1.0, legs = Math.Max(it.LegMm > 0 ? it.LegMm : Math.Max(2.5, T + 0.5), fourPoster ? sw + 2.3 : 0);
        double rh = Geo.Clamp((it.MattressMm - T) * 0.35, 2.2, 3.5), th = Math.Max(1.2, rh - 1);
        // Mattress tops: one, or the bunk's two (the upper one high enough to sit up under).
        var levels = new List<double> { it.MattressMm };
        double clear = Math.Max(it.FootMm, 10);
        if (!fourPoster) levels.Add(it.MattressMm + clear);
        double guardH = 2.0, guardY = levels[^1] + 1.2;
        string what = fourPoster ? "four-poster" : "bunk bed";
        if (levels[0] - T - rh < 1.5) { m.Bad($"The mattress is too low for a {T:0.##} mm sheet: make it at least {T + 1.5 + 2.2:0.#} mm up."); return; }
        if (L - 2 * T < 8) { m.Bad($"The bed is too short: make it at least {2 * T + 8:0.#} mm long."); return; }
        if (D < 2 * legs + 4) { m.Bad($"The bed is too narrow: make it at least {2 * legs + 4:0.#} mm wide."); return; }
        double need = fourPoster ? levels[0] + 8 : guardY + guardH + 1.5;
        if (H < need) { m.Bad($"The {what} is too low: make it at least {need:0.#} mm high."); return; }

        double Db = D - 2 * si, Lb = L - 2 * T;
        double tw = Geo.Clamp(Db * 0.2, 1.2, 5);
        double[] tabV = [Db * 0.28, Db * 0.72];
        double[] railZ = [si + T / 2, D - si - T / 2];

        // ---- the head and foot boards (flat: u = z, v = y) ----
        double headPanel = fourPoster ? Math.Min(H - 4, Math.Max(levels[0] + 6, H * 0.45)) : H;
        double footPanel = fourPoster ? Math.Min(H - 4, Math.Max(levels[0] + 2, it.FootMm)) : H;
        PathsD Board(double panel)
        {
            if (!fourPoster) return [Shapes.Topped(BackTop.Rounded, 0, 0, D, H, Math.Min(2, D * 0.12))];
            var o = new PathsD { Geo.Rect(0, 0, legs, H), Geo.Rect(D - legs, 0, D, H) };
            o.Add(Shapes.Topped(look == FrontLook.Gothic ? BackTop.Pointed : BackTop.Arched, legs - 0.01, 0, D - legs + 0.01, panel, Math.Min(Db * 0.25, panel * 0.3)));
            // The posts' tabs up into the canopy.
            foreach (var u in new[] { legs / 2, D - legs / 2 }) o.Add(Geo.Rect(u - legs * 0.25, H - 0.01, u + legs * 0.25, H + T));
            return o;
        }
        var holes = new PathsD { WoodFace.RoundRect(legs, -3, D - legs, levels[0] - T - rh - 1.2, Math.Min(1, (D - 2 * legs) / 2)) };
        foreach (var top in levels)
        {
            double yb = top - T, yr = yb - rh / 2;
            foreach (var v in tabV)
            {
                double z = D - si - v;
                holes.Add(Geo.Rect(z - (tw + f) / 2, yb - f / 2, z + (tw + f) / 2, yb + T + f / 2));
            }
            foreach (var z in railZ) holes.Add(Geo.Rect(z - sw / 2, yr - (th + f) / 2, z + sw / 2, yr + (th + f) / 2));
        }
        // The canopy rails go through the posts, near their tops.
        double crh = 3.5, cth = 1.8, cry = H - 1.3 - cth / 2;
        if (fourPoster) foreach (var z in new[] { legs / 2, D - legs / 2 }) holes.Add(Geo.Rect(z - sw / 2, cry - (cth + f) / 2, z + sw / 2, cry + (cth + f) / 2));
        // The bunk's guard rail along the front.
        double gz = D - si - T / 2, gth = 1.0;
        if (!fourPoster) holes.Add(Geo.Rect(gz - sw / 2, guardY + guardH / 2 - (gth + f) / 2, gz + sw / 2, guardY + guardH / 2 + (gth + f) / 2));
        var footHoles = new PathsD(holes);
        if (!fourPoster)
        {
            // A ladder up the foot board, near the front.
            double z1 = D - legs - 0.8, z0 = Math.Max(z1 - 4.5, legs + 1.2), ya = levels[0] + 1.6, yb = levels[1] - T - rh - 1.2;
            for (double y = ya; y + 1.8 <= yb; y += 3.0) footHoles.Add(Geo.Rect(z0, y, z1, y + 2.0));
        }
        var (he, hl) = BoardDetail(look, D, legs, headPanel, fourPoster);
        m.Add("Head board", Board(headPanel), holes, [Pose.Left(T / 2, 0, 0)], grainAlongU: false, planks: true, plankMm: 3, etched: he, lines: hl);
        m.Add("Foot board", Board(footPanel), footHoles, [Pose.Left(L - T / 2, 0, 0)], grainAlongU: false, planks: true, plankMm: 3);

        // ---- mattresses and their rails ----
        for (int i = 0; i < levels.Count; i++)
        {
            double yb = levels[i] - T;
            var mat = new PathsD { Geo.Rect(0, 0, Lb, Db) };
            foreach (var v in tabV)
            {
                mat.Add(Geo.Rect(-T, v - tw / 2, 0.01, v + tw / 2));
                mat.Add(Geo.Rect(Lb - 0.01, v - tw / 2, Lb + T, v + tw / 2));
            }
            var etched = new PathsD();
            var lines = new List<PathD>();
            if (it.Bedding) Bedding(Lb, Db, etched, lines);
            string n = levels.Count > 1 ? (i == 0 ? "Lower mattress" : "Upper mattress") : "Mattress";
            m.Add(n, mat, [], [Pose.Up(T, yb + T / 2, D - si)], grainAlongU: true, face: [], etched: etched, lines: lines);
            var rail = new PathsD
            {
                Geo.Rect(0, 0, Lb, rh),
                Geo.Rect(-T, (rh - th) / 2, 0.01, (rh + th) / 2),
                Geo.Rect(Lb - 0.01, (rh - th) / 2, Lb + T, (rh + th) / 2),
            };
            m.Add(levels.Count > 1 ? (i == 0 ? "Lower side rail" : "Upper side rail") : "Side rail", rail, [], railZ.Select(z => Pose.Front(T, yb - rh, z)), grainAlongU: true, face: [Geo.Rect(0, 0, Lb, rh)]);
        }
        if (!fourPoster)
        {
            var guard = new PathsD
            {
                Geo.Rect(0, 0, Lb, guardH),
                Geo.Rect(-T, (guardH - gth) / 2, 0.01, (guardH + gth) / 2),
                Geo.Rect(Lb - 0.01, (guardH - gth) / 2, Lb + T, (guardH + gth) / 2),
            };
            m.Add("Guard rail", guard, [], [Pose.Front(T, guardY, gz)], grainAlongU: true, face: [Geo.Rect(0, 0, Lb, guardH)]);
            m.Note("Put both mattresses, their rails and the guard rail into the head board, then the foot board (with the ladder) on their other ends.");
            return;
        }

        // ---- a four-poster's canopy: two rails between the posts, a cloth on top ----
        double rtw = Geo.Clamp(Lb * 0.12, 1.5, 4);
        double[] rtAt = [Lb * 0.25, Lb * 0.75];
        var crail = new PathsD
        {
            Geo.Rect(0, 0, Lb, crh),
            Geo.Rect(-T, cry - (H - crh) - cth / 2, 0.01, cry - (H - crh) + cth / 2),
            Geo.Rect(Lb - 0.01, cry - (H - crh) - cth / 2, Lb + T, cry - (H - crh) + cth / 2),
        };
        foreach (var u in rtAt) crail.Add(Geo.Rect(u - rtw / 2, crh - 0.01, u + rtw / 2, crh + T));
        var vl = new List<PathD>();
        // The valance: a scalloped hem scored along the rail.
        for (double x = 0; x < Lb - 0.5; x += 2.5) vl.Add([.. Geo.Arc(x + 1.25, 1.2, 1.25, Math.PI, Math.PI * 2, 0.02)]);
        m.Add("Canopy rail", crail, [], [Pose.Front(T, H - crh, legs / 2), Pose.Front(T, H - crh, D - legs / 2)], grainAlongU: true, face: [], lines: vl);
        double o = 1.2, Lc = L + 2 * o, Dc = D + 2 * o;
        var cslots = new PathsD();
        foreach (var xs in new[] { T / 2, L - T / 2 })
            foreach (var z in new[] { legs / 2, D - legs / 2 })
                cslots.Add(Geo.Rect(xs + o - sw / 2, Dc - (z + o) - (legs * 0.5 + f) / 2, xs + o + sw / 2, Dc - (z + o) + (legs * 0.5 + f) / 2));
        foreach (var z in new[] { legs / 2, D - legs / 2 })
            foreach (var u in rtAt)
                cslots.Add(Geo.Rect(T + u + o - (rtw + f) / 2, Dc - (z + o) - sw / 2, T + u + o + (rtw + f) / 2, Dc - (z + o) + sw / 2));
        var cl = new List<PathD>();
        // Folds in the cloth, gathered to the middle.
        for (double x = 2; x < Lc - 1.5; x += 2.2) cl.Add([new(x, 0.5), new(Lc / 2 + (x - Lc / 2) * 0.6, Dc / 2), new(x, Dc - 0.5)]);
        m.Add("Canopy", [Geo.Rect(0, 0, Lc, Dc)], cslots, [Pose.Up(-o, H + T / 2, D + o)], grainAlongU: true, face: [], lines: cl);
        m.Note("Put the mattress and rails into the head board, the canopy rails through its posts, then the foot board on; press the canopy down on top last.");
    }

    /// <summary>A carved panel on a four-poster's head board, or a padded one on a bunk.</summary>
    static (PathsD, List<PathD>) BoardDetail(FrontLook look, double D, double legs, double panel, bool fourPoster)
    {
        var etched = new PathsD();
        var lines = new List<PathD>();
        if (fourPoster && panel > 8)
        {
            double r = Math.Min((D - 2 * legs) * 0.22, panel * 0.15);
            Cabinets.Symbol(look, D / 2, panel * 0.68, r, etched, lines);
        }
        return (etched, lines);
    }

    /// <summary>A pallet bed: a straw mattress on two low sleepers whose tabs go up into it, a fur thrown over it.</summary>
    static void Pallet(Maker m)
    {
        var it = m.Item;
        double L = it.WidthMm, D = it.DepthMm, T = m.T, f = m.Fit, sw = m.SlotW;
        double hs = Math.Max(2.4, it.MattressMm - T);
        if (L < 10 || D < 6) { m.Bad("The pallet is too small: make it at least 10 mm long and 6 mm wide."); return; }
        double tw = Geo.Clamp(D * 0.2, 1.2, 4);
        double[] tabAt = [D * 0.25, D * 0.75];
        double[] xs = [L * 0.18, L * 0.82];
        var sleeper = new PathsD { Geo.Rect(0, 0, D, hs) };
        foreach (var u in tabAt) sleeper.Add(Geo.Rect(u - tw / 2, hs - 0.01, u + tw / 2, hs + T));
        m.Add("Sleeper", sleeper, hs > 3.2 ? [WoodFace.RoundRect(D * 0.3, -3, D * 0.7, hs - 2.2, 0.5)] : [], [.. xs.Select(x => Pose.Left(x, 0, 0))], grainAlongU: true);
        var slots = new PathsD();
        foreach (var x in xs)
            foreach (var u in tabAt) slots.Add(Geo.Rect(x - sw / 2, D - u - (tw + f) / 2, x + sw / 2, D - u + (tw + f) / 2));
        var etched = new PathsD();
        var lines = new List<PathD>();
        // Straw ticking: stitched lines; a fur (a hide's outline) over the foot end.
        for (double y = 1.2; y < D - 0.8; y += 1.6) lines.Add([new(0.6, y), new(L - 0.6, y)]);
        if (it.Bedding && L > 14) lines.Add(Geo.Loop(Rugs.Hide(L * 0.62, D / 2, L * 0.36, D * 0.48, m.Rnd)));
        m.Add("Mattress", [WoodFace.RoundRect(0, 0, L, D, Math.Min(1.5, D * 0.15))], slots, [Pose.Up(0, hs + T / 2, D)], grainAlongU: true, face: [], etched: etched, lines: lines);
        m.Note("Push the mattress down onto the two sleepers' tabs.");
    }
}
