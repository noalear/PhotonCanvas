using Clipper2Lib;

namespace PhotonCanvas.Addon.Furniture;

/// <summary>
/// Tables and benches: a top with slots, two end frames whose tabs go up through them (four legs,
/// a solid slab or a trestle), and rails low down whose tabs go through the frames. Round tables and
/// stools: a top on two legs crossed with a halving joint.
/// </summary>
public static class Tables
{
    public static void Make(Maker m)
    {
        var it = m.Item;
        if (it.Kind == ItemKind.Stool || it.Legs == LegStyle.Round) { Crossed(m); return; }
        double W = it.WidthMm, D = it.DepthMm, H = it.HeightMm, T = m.T, f = m.Fit, sw = m.SlotW;
        var style = it.Legs;
        bool rails = it.Stretcher;
        double legs = it.LegMm > 0 ? it.LegMm : Math.Max(2.5, T + 0.5);
        if (rails && style == LegStyle.Legs) legs = Math.Max(legs, sw + 2 * Maker.Bridge);
        double e = Math.Clamp(W * 0.06, 1.5, 6), si = Math.Clamp(D * 0.06, 1.0, 4);
        double Df = D - 2 * si, Hf = H - T, apron = Math.Clamp(Hf * 0.22, 1.5, 4);
        string what = it.Kind == ItemKind.Bench ? "bench" : "table";
        if (Hf < 4) { m.Bad($"The {what} is too low for a {T:0.##} mm sheet: make it at least {T + 4:0.#} mm high."); return; }
        if (W - 2 * e - 2 * T < 4) { m.Bad($"The {what} is too short: make it at least {2 * e + 2 * T + 4:0.#} mm long."); return; }
        if (style == LegStyle.Legs && Df < 2 * legs + 1.5) style = LegStyle.Slab;
        if (Df < 3) { m.Bad($"The {what} is too narrow: make it at least {2 * si + 3:0.#} mm deep."); return; }

        // Where the frames' tabs go: over the legs, or spread along a solid end.
        double[] tabAt;
        double tw;
        if (style == LegStyle.Legs) { tw = Math.Max(1.2, legs - 1); tabAt = [legs / 2, Df - legs / 2]; }
        else if (Df < 7) { tw = Math.Max(1.2, Df * 0.35); tabAt = [Df / 2]; }
        else { tw = Math.Clamp(Df * 0.2, 1.2, 5); tabAt = [Df * 0.25, Df * 0.75]; }

        double xf1 = e + T / 2, xf2 = W - e - T / 2;
        double rh = Math.Clamp(Hf * 0.2, 2, 4), th = rh - 1;
        // Rails: one down the middle through a slab or trestle, or one along each side through the legs.
        double[] railAt = !rails ? [] : style == LegStyle.Legs ? [legs / 2, Df - legs / 2] : [Df / 2];
        double footCut = 0, ys = 0, post = Math.Max(legs, sw + 2 * Maker.Bridge), foot = Math.Clamp(Hf * 0.18, 1.5, 3);
        if (style == LegStyle.Slab) footCut = Math.Min(Hf * 0.3, Df * 0.3);
        ys = style switch
        {
            LegStyle.Legs => Math.Max(rh / 2 + 1, Hf * 0.25),
            LegStyle.Slab => Math.Max(footCut + rh / 2 + Maker.Bridge, Hf * 0.3),
            _ => foot + (Hf - apron - foot) * 0.45,
        };
        // The rail has to go through solid sheet: the legs below the apron's cut-out, a slab below its tabs, a trestle's post.
        double railTop = style == LegStyle.Slab ? Hf - Maker.Bridge : Hf - apron - 0.8;
        if (railAt.Length > 0 && ys + rh / 2 > railTop)
        {
            m.Warn($"The {what} is too low for a rail between the legs: it has none.");
            railAt = [];
        }

        // The end frames.
        var outline = new PathsD { Geo.Rect(0, 0, Df, Hf) };
        foreach (var u in tabAt) outline.Add(Geo.Rect(u - tw / 2, Hf - 0.01, u + tw / 2, Hf + T));
        var holes = new PathsD();
        switch (style)
        {
            case LegStyle.Legs:
                holes.Add(WoodFace.RoundRect(legs, -3, Df - legs, Hf - apron, Math.Min(1, (Df - 2 * legs) / 2)));
                break;
            case LegStyle.Slab:
            {
                double fw = Math.Max(Df * 0.4, 1.5);
                holes.Add(WoodFace.RoundRect(Df / 2 - fw / 2, -3, Df / 2 + fw / 2, footCut, fw / 2));
                break;
            }
            default:
                // A trestle: a bar under the top, a post, and a foot with a gap under its middle.
                holes.Add(Geo.Rect(-1, foot, Df / 2 - post / 2, Hf - apron));
                holes.Add(Geo.Rect(Df / 2 + post / 2, foot, Df + 1, Hf - apron));
                holes.Add(Geo.Rect(Df * 0.3, -1, Df * 0.7, Math.Min(0.6, foot / 3)));
                break;
        }
        foreach (var u in railAt) holes.Add(Geo.Rect(u - sw / 2, ys - (th + f) / 2, u + sw / 2, ys + (th + f) / 2));
        m.Add(style == LegStyle.Legs ? "Leg frame" : "End", outline, holes,
            [Pose.Left(xf1, 0, si), Pose.Left(xf2, 0, si)], grainAlongU: false, face: [Geo.Rect(0, 0, Df, Hf)]);

        // The top, its slots over the frames' tabs (seen from above, v runs from the front to the back).
        var slots = new PathsD();
        foreach (var xf in new[] { xf1, xf2 })
            foreach (var u in tabAt)
            {
                double v = D - si - u;
                slots.Add(Geo.Rect(xf - sw / 2, v - (tw + f) / 2, xf + sw / 2, v + (tw + f) / 2));
            }
        m.Add("Top", [Geo.Rect(0, 0, W, D)], slots, [Pose.Up(0, H - T / 2, D)], grainAlongU: true, planks: true, plankMm: 3);

        // The rails, their tabs through the frames and flush with the outside.
        if (railAt.Length > 0)
        {
            double L = xf2 - xf1 - T;
            var rail = new PathsD
            {
                Geo.Rect(0, 0, L, rh),
                Geo.Rect(-T, (rh - th) / 2, 0.01, (rh + th) / 2),
                Geo.Rect(L - 0.01, (rh - th) / 2, L + T, (rh + th) / 2),
            };
            m.Add("Rail", rail, [], railAt.Select(u => Pose.Front(xf1 + T / 2, ys - rh / 2, si + u)), grainAlongU: true, face: [Geo.Rect(0, 0, L, rh)]);
        }
        if (!rails && style != LegStyle.Legs) m.Note(m.PushFit ? "Without a rail the ends are held only by the top: add the rail to keep them square." : "Without a rail the ends are held only by the top: glue them square, or add the rail.");
    }

    /// <summary>A round or square top on two legs crossed with a halving joint (stools and round tables).</summary>
    static void Crossed(Maker m)
    {
        var it = m.Item;
        bool round = it.Kind == ItemKind.Stool ? it.Top == TopShape.Round : true;
        double S = it.WidthMm, H = it.HeightMm, T = m.T, f = m.Fit, sw = m.SlotW;
        string what = it.Kind == ItemKind.Stool ? "stool" : "table";
        double Hf = H - T;
        if (Hf < 4) { m.Bad($"The {what} is too low for a {T:0.##} mm sheet: make it at least {T + 4:0.#} mm high."); return; }
        double Sl = round ? S * 0.72 : S - 2 * Math.Clamp(S * 0.06, 0.8, 4);
        if (Sl < sw + 3) { m.Bad($"The {what} is too small across for a {T:0.##} mm sheet: make it at least {(sw + 3) / (round ? 0.72 : 0.88):0.#} mm."); return; }
        // Two tabs on each leg, near its ends, where they fit inside the top with sheet round their slots;
        // otherwise one tab in the middle of the leg that's slotted from below (the other leg is open there).
        double tw = Math.Clamp(Sl * 0.15, 1.2, 4);
        double a = Sl / 2 - Math.Max(tw / 2 + 0.8, Sl * 0.12);
        double reach = round ? S / 2 : S / 2;
        bool four = a - tw / 2 >= sw / 2 + Maker.Bridge && a + (tw + f) / 2 <= reach - Maker.Bridge;
        if (!four) tw = Math.Max(T, Math.Min(Sl * 0.35, Sl - 2));
        double fw = Math.Max(Sl * 0.4, sw + 2), fh = Math.Min(Hf * 0.3, Hf / 2 - 1.5);
        PathsD Leg(bool fromTop)
        {
            var o = new PathsD { Geo.Rect(0, 0, Sl, Hf) };
            if (four) foreach (var s in new[] { -1, 1 }) o.Add(Geo.Rect(Sl / 2 + s * a - tw / 2, Hf - 0.01, Sl / 2 + s * a + tw / 2, Hf + T));
            else if (!fromTop) o.Add(Geo.Rect(Sl / 2 - tw / 2, Hf - 0.01, Sl / 2 + tw / 2, Hf + T));
            return o;
        }
        PathsD LegHoles(bool fromTop)
        {
            var h = new PathsD();
            if (fh > 0.8) h.Add(WoodFace.RoundRect(Sl / 2 - fw / 2, -3, Sl / 2 + fw / 2, fh, fw / 2));
            h.Add(fromTop ? Geo.Rect(Sl / 2 - sw / 2, Hf / 2, Sl / 2 + sw / 2, Hf + T + 1) : Geo.Rect(Sl / 2 - sw / 2, -1, Sl / 2 + sw / 2, Hf / 2));
            return h;
        }
        double o0 = (S - Sl) / 2;
        var face = new PathsD { Geo.Rect(0, 0, Sl, Hf) };
        m.Add("Leg (slot at the top)", Leg(true), LegHoles(true), [Pose.Front(o0, 0, S / 2)], grainAlongU: false, face: face);
        m.Add("Leg (slot at the bottom)", Leg(false), LegHoles(false), [Pose.Left(S / 2, 0, o0)], grainAlongU: false, face: face);

        var slots = new PathsD();
        if (four)
            foreach (var s in new[] { -1, 1 })
            {
                double x = S / 2 + s * a;
                slots.Add(Geo.Rect(x - (tw + f) / 2, S / 2 - sw / 2, x + (tw + f) / 2, S / 2 + sw / 2));
                slots.Add(Geo.Rect(S / 2 - sw / 2, x - (tw + f) / 2, S / 2 + sw / 2, x + (tw + f) / 2));
            }
        else slots.Add(Geo.Rect(S / 2 - sw / 2, S / 2 - (tw + f) / 2, S / 2 + sw / 2, S / 2 + (tw + f) / 2));
        var top = round ? new PathsD { Geo.Circle(S / 2, S / 2, S / 2) } : [Geo.Rect(0, 0, S, S)];
        m.Add(it.Kind == ItemKind.Stool ? "Seat" : "Top", top, slots, [Pose.Up(0, H - T / 2, S)], grainAlongU: true, planks: it.Kind != ItemKind.Stool, plankMm: 3);
        m.Note("Slide the two legs together at the middle, then push the top down onto their tabs.");
    }
}
