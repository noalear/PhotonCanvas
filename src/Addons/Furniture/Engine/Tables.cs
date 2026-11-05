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
            {
                // A trestle: a bar under the top, a post, and a foot with a gap under its middle. Curved
                // brackets join the post to the bar and flare it into the foot, and the bar's ends are cut back.
                double px0 = Df / 2 - post / 2, px1 = Df / 2 + post / 2, gy0 = foot, gy1 = Hf - apron;
                double rb = Math.Min(Math.Min(post * 0.9, (gy1 - gy0) * 0.3), px0 * 0.6);
                foreach (var left in new[] { true, false })
                {
                    var hole = new PathsD { left ? Geo.Rect(-1, gy0, px0, gy1) : Geo.Rect(px1, gy0, Df + 1, gy1) };
                    if (rb > 0.6)
                    {
                        double cx = left ? px0 - rb : px1 + rb, x0 = Math.Min(cx, left ? px0 : px1), x1 = Math.Max(cx, left ? px0 : px1);
                        foreach (var (y0, y1, cy) in new[] { (gy0, gy0 + rb, gy0 + rb), (gy1 - rb, gy1, gy1 - rb) })
                            hole = Geo.Subtract(hole, Geo.Subtract([Geo.Rect(x0, y0, x1, y1)], [Geo.Circle(cx, cy, rb, 0.02)]));
                        // The bar's ends and the foot's toes sloped off.
                        double ex = left ? 0 : Df, s = left ? 1 : -1, c = Math.Min(apron * 0.5, rb);
                        hole.Add(new PathD { new(ex - s, gy1 - 0.01), new(ex + s * c, gy1 - 0.01), new(ex - s, gy1 + c) });
                        double ct = Math.Min(foot * 0.45, rb);
                        hole.Add(new PathD { new(ex - s, gy0 + 0.01), new(ex + s * ct * 1.6, gy0 + 0.01), new(ex - s, gy0 - ct) });
                    }
                    holes.AddRange(Geo.Solid(hole));
                }
                holes.Add(Geo.Rect(Df * 0.3, -1, Df * 0.7, Math.Min(0.6, foot / 3)));
                break;
            }
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
        // A workbench: a tool rack standing along the back and a vice on the front corner, their tabs down through the top.
        if (it.ToolRack) Workbench(m, W, D, H, T, e, xf1, xf2, slots);
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
            // A trestle's stretcher carries the theme's symbol in the middle.
            var re = new PathsD();
            var rl = new List<PathD>();
            if (style == LegStyle.Trestle && rh * 0.4 > 0.9 && Fronts.Look(m) != FrontLook.Metal) Cabinets.Symbol(Fronts.Look(m), m.Theme, L / 2, rh / 2, rh * 0.4, re, rl);
            m.Add("Rail", rail, [], railAt.Select(u => Pose.Front(xf1 + T / 2, ys - rh / 2, si + u)), grainAlongU: true, face: [Geo.Rect(0, 0, L, rh)], etched: re, lines: rl);
        }
        if (!rails && style != LegStyle.Legs) m.Note(m.PushFit ? "Without a rail the ends are held only by the top: add the rail to keep them square." : "Without a rail the ends are held only by the top: glue them square, or add the rail.");
    }

    /// <summary>
    /// A workbench's tool rack (a board standing along the back of the top with tools hung on pegs) and a vice on the
    /// front right (seen from the side: its jaws, screw and tommy bar), each with tabs down through slots added to the top's.
    /// </summary>
    static void Workbench(Maker m, double W, double D, double H, double T, double e, double xf1, double xf2, PathsD topSlots)
    {
        double f = m.Fit, sw = m.SlotW, tw = 2.4;
        double zr = T / 2 + Maker.Bridge + 0.3, Hr = Geo.Clamp(H * 0.6, 6, 10), Wr = W - 2 * e;
        if (Wr >= 10 && D >= T + 2 * Maker.Bridge + 6)
        {
            var board = new PathsD { WoodFace.RoundRect(0, 0, Wr, Hr, 0.8), Geo.Rect(0, 0, Wr, Hr - 1) };
            double[] tabs = [Wr * 0.25, Wr * 0.75];
            foreach (var u in tabs)
            {
                board.Add(Geo.Rect(u - tw / 2, -T, u + tw / 2, 0.01));
                topSlots.Add(Geo.Rect(e + u - (tw + f) / 2, D - zr - sw / 2, e + u + (tw + f) / 2, D - zr + sw / 2));
            }
            var etched = new PathsD();
            var lines = new List<PathD> { new() { new(0.4, Hr - 1.6), new(Wr - 0.4, Hr - 1.6) } };
            Contents.Tools(0.8, 0.8, Wr - 0.8, Hr - 2.2, m.Rnd, etched, lines);
            m.Add("Tool rack", Geo.Solid(board), [], [Pose.Front(e, H, zr)], grainAlongU: true, etched: etched, lines: lines);
        }
        // The vice, seen from the right: a fixed jaw on a block, the loose jaw on the screw, the screw out past the front
        // with a tommy bar through its end.
        double xc = xf2 - sw - 2.2, z0 = D - 3.6;
        if (xc - xf1 > 8 && D >= 8)
        {
            var v = new PathsD
            {
                Geo.Rect(0, 0, 2.4, 1.2),
                Geo.Rect(0, 0, 1.4, 4.0),
                Geo.Rect(2.2, 1.0, 3.4, 4.0),
                Geo.Rect(1.3, 1.8, 5.6, 2.8),
                Geo.Rect(5.0, -1.4, 6.0, 3.6),
                Geo.Circle(5.5, 3.6, 0.6, 0.02),
                Geo.Circle(5.5, -1.4, 0.6, 0.02),
                Geo.Rect(0.3, -T, 0.3 + 1.9, 0.01),
            };
            var vl = new List<PathD> { new() { new(3.4, 2.0), new(5.0, 2.6) }, new() { new(3.4, 2.6), new(5.0, 2.0) }, new() { new(0.2, 3.5), new(1.2, 3.5) }, new() { new(2.4, 3.5), new(3.2, 3.5) } };
            topSlots.Add(Geo.Rect(xc - sw / 2, D - (z0 + 2.2 + f / 2), xc + sw / 2, D - (z0 + 0.3 - f / 2)));
            m.Add("Vice", Geo.Solid(v), [], [Pose.Left(xc, H, z0)], grainAlongU: true, face: [], lines: vl);
        }
        m.Note("Push the tool rack and the vice into the slots in the top.");
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
        // Each crossed piece is two legs, splayed a little, and a rail under the top: the arch between the legs
        // goes up to the rail, and the halving joint is in the rail. Too small for that: a slab with a notch.
        double lw = Geo.Clamp(Sl * 0.17, 1.5, 3.2), rail = Geo.Clamp(Hf * 0.28, 2.3, 4);
        double splay = Math.Min(lw * 0.45, Math.Max(0, ((round ? S * 0.92 : S) - Sl) / 2));
        bool legged = Hf - rail >= 1.8 && Sl - 2 * lw >= sw + 0.6 && rail / 2 >= Maker.Bridge + f;
        PathsD Leg(bool fromTop)
        {
            var o = new PathsD { legged ? [new(-splay, 0), new(Sl + splay, 0), new(Sl, Hf), new(0, Hf)] : Geo.Rect(0, 0, Sl, Hf) };
            if (four) foreach (var s in new[] { -1, 1 }) o.Add(Geo.Rect(Sl / 2 + s * a - tw / 2, Hf - 0.01, Sl / 2 + s * a + tw / 2, Hf + T));
            else if (!fromTop) o.Add(Geo.Rect(Sl / 2 - tw / 2, Hf - 0.01, Sl / 2 + tw / 2, Hf + T));
            return o;
        }
        PathsD LegHoles(bool fromTop)
        {
            var h = new PathsD();
            if (legged)
            {
                // The arch between the legs (its inner edges parallel to the outer ones), corners rounded.
                double top = Hf - rail;
                double In(double y) => lw - splay * (1 - y / Hf);
                var arch = new PathsD { new PathD { new(In(-3), -3), new(Sl - In(-3), -3), new(Sl - In(top), top), new(In(top), top) } };
                double rr = Math.Min(0.8, (Sl - 2 * lw) / 3);
                h.AddRange(Geo.Offset(Geo.Offset(arch, -rr), rr, round: true));
                // The halving joint, in the rail: from the top down to its middle, or from the arch up to it.
                h.Add(fromTop ? Geo.Rect(Sl / 2 - sw / 2, Hf - rail / 2, Sl / 2 + sw / 2, Hf + T + 1) : Geo.Rect(Sl / 2 - sw / 2, top - 0.5, Sl / 2 + sw / 2, Hf - rail / 2));
                return h;
            }
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
