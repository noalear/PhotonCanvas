using Clipper2Lib;

namespace PhotonCanvas.Addon.Furniture;

/// <summary>
/// A long-case (grandfather) clock built like one: a wide base on bracket feet, a narrower trunk with a
/// glazed door, and a wide hood with the dial sunk behind a bezel, under a crown with finials. The front
/// and back carry the shape; two sides slot between them, a seat board and a bottom slot through the
/// sides; the hood's and base's sides are glued on over the sides, and a moulded cap on top takes the
/// sides' tabs and the crown's. A real pendulum hangs from the seat board through a slot, its bob
/// showing through the trunk door, free to swing.
/// </summary>
public static class Clocks
{
    public static void Make(Maker m)
    {
        var it = m.Item;
        double W = it.WidthMm, D = it.DepthMm, H = it.HeightMm, T = m.T, f = m.Fit, sw = m.SlotW;
        var look = Fronts.Look(m);
        // The sides' outer faces are a sheet in from the hood's and base's sides; the trunk's front stands a little proud of them.
        double xs = T, a = Math.Max(0.3, T - 1.15);
        double Wi = W - 4 * T, Di = D - 2 * T;
        if (Wi < 3.2) { m.Bad($"The clock is too narrow for a pendulum inside: make it at least {4 * T + 3.2:0.#} mm wide."); return; }
        if (Di < T + 1.6) { m.Bad($"The clock is too shallow for a pendulum inside: make it at least {3 * T + 1.6:0.#} mm deep."); return; }

        bool gothic = look == FrontLook.Gothic;
        var crown = it.Crown == Crown.Pediment ? Crown.Pediment : Crown.Arch;
        double finR = Geo.Clamp(W * 0.055, 0.55, 0.9);
        double crownH = Geo.Clamp(Math.Min(W * 0.36, H * 0.12), 2.8, 7) + (gothic ? 1.2 : 0), finH = 0.6 + 2 * finR + (gothic ? 1.2 : 0);
        double capT = T, o = Geo.Clamp(W * 0.04, 0.4, 0.8);
        double hoodH = Geo.Clamp(Math.Min(W * 0.85, H * 0.24), 6.5, 16), baseH = Geo.Clamp(H * 0.18, 4.6, 14);
        double hy1 = H - crownH - finH - capT, hy0 = hy1 - hoodH;
        double kick = Geo.Clamp(baseH * 0.24, 1.3, 2.4);
        // The sides' tabs go into the trunk's front just above the base and just under the hood, the glazed door between.
        double th = Geo.Clamp(T * 0.8, 1.6, 3);
        double minTrunk = 2 * (th + 1.6) + 1.5;
        if (hy0 - baseH < minTrunk) { m.Bad($"The clock is too low for a trunk: make it at least {H + minTrunk - (hy0 - baseH):0.#} mm high."); return; }

        // ---- sizes the parts share ----
        double xc = W / 2;
        double tLow = baseH + 0.5 + th / 2, tHigh = hy0 - 0.3 - th / 2;
        double winW = Math.Min(Wi - 0.4, W * 0.5), winY0 = tLow + th / 2 + f / 2 + 1.15, winY1 = tHigh - th / 2 - f / 2 - 1.15;
        var window = gothic ? Fronts.PointedRect(xc - winW / 2, winY0, xc + winW / 2, winY1) : Fronts.ArchedRect(xc - winW / 2, winY0, xc + winW / 2, winY1);
        double dialR = Math.Min(W * 0.34, hoodH / 2 - 1.5), dialY = hy0 + hoodH * 0.5;
        // Where the sides' tabs go into the front and back: once in the base and twice up the trunk (none in the hood, where the dial is).
        double trunk = hy0 - baseH;
        double[] tabYs = [baseH * 0.5, tLow, tHigh];
        double seatY = hy0 - T, bottomY = kick + 1.15;
        var slots = new PathsD();
        foreach (var x in new[] { xs + T / 2, W - xs - T / 2 })
            foreach (var y in tabYs) slots.Add(Geo.Rect(x - sw / 2, y - (th + f) / 2, x + sw / 2, y + (th + f) / 2));
        PathsD Feet(double w) => [WoodFace.RoundRect(w * 0.2, -3, w * 0.8, kick, Math.Min(1.2, kick * 0.7))];

        // ---- the front and back: base, trunk, hood ----
        PathsD Body() => Geo.Solid(
        [
            Geo.Rect(0, 0, W, baseH),
            Geo.Rect(a, baseH - 0.01, W - a, hy0 + 0.01),
            Geo.Rect(0, hy0, W, hy1),
        ]);
        {
            var etched = new PathsD();
            var lines = new List<PathD>();
            // The dial: a chapter ring with the hours left light, a bezel ring, the hands at ten past ten.
            Dial(xc, dialY, dialR, etched, lines);
            // Mouldings where the trunk meets the hood and the base.
            lines.Add([new(0, hy0 + 0.35), new(W, hy0 + 0.35)]);
            lines.Add([new(0, baseH - 0.35), new(W, baseH - 0.35)]);
            lines.Add([new(0, hy1 - 0.45), new(W, hy1 - 0.45)]);
            m.Add("Front", Body(), [.. slots, window, .. Feet(W)], [Pose.Front(0, 0, D - T / 2)], grainAlongU: false, etched: etched, lines: lines);
            m.Add("Back", Body(), [.. slots, .. Feet(W)], [Pose.Back(W, 0, T / 2)], grainAlongU: false);
        }

        // ---- the sides (flat: u = z from the back, v = y), tabs out to the front and back and up into the cap ----
        double sideTw = Geo.Clamp(Di * 0.3, 1.2, 3), capZ = T + Math.Max(Di * 0.3, sideTw / 2 + 0.2);
        var side = new PathsD { Geo.Rect(T, 0, D - T, hy1) };
        foreach (var y in tabYs)
        {
            side.Add(Geo.Rect(-0.01 + 0, y - th / 2, T + 0.01, y + th / 2));
            side.Add(Geo.Rect(D - T - 0.01, y - th / 2, D, y + th / 2));
        }
        side.Add(Geo.Rect(capZ - sideTw / 2, hy1 - 0.01, capZ + sideTw / 2, hy1 + capT));
        // Plates' slots: the bottom and the seat board.
        double plateTw = Geo.Clamp(Di * 0.3, 1.2, 3);
        double inset = Math.Max(Di * 0.27, 1.15 + (plateTw + f) / 2);
        double[] plateAt = Di - 2 * inset < plateTw + f + 1.2 ? [D / 2] : [T + inset, D - T - inset];
        var sideHoles = new PathsD();
        foreach (var y in new[] { bottomY, seatY })
            foreach (var z in plateAt) sideHoles.Add(Geo.Rect(z - (plateTw + f) / 2, y - f / 2, z + (plateTw + f) / 2, y + T + f / 2));
        sideHoles.Add(WoodFace.RoundRect(T + Di * 0.2, -3, T + Di * 0.8, kick, Math.Min(1.2, kick * 0.7)));
        m.Add("Left side", side, sideHoles, [Pose.Left(xs + T / 2, 0, 0)], grainAlongU: false, face: [Geo.Rect(T, 0, D - T, hy1)]);
        PathsD Mirror(PathsD q) => Geo.Map(q, p => new PointD(D - p.x, p.y));
        m.Add("Right side", Mirror(side), Mirror(sideHoles), [Pose.Right(W - xs - T / 2, 0, D)], grainAlongU: false, face: [Geo.Rect(T, 0, D - T, hy1)]);

        // ---- the bottom and the seat board (flat: u = x from the sides' insides, v = z from the front's inside back) ----
        foreach (var (name, y) in new[] { ("Bottom", bottomY), ("Seat board", seatY) })
        {
            var plate = new PathsD { Geo.Rect(0, 0, Wi, Di) };
            foreach (var z in plateAt)
            {
                double v = D - T - z;
                plate.Add(Geo.Rect(-T, v - plateTw / 2, 0.01, v + plateTw / 2));
                plate.Add(Geo.Rect(Wi - 0.01, v - plateTw / 2, Wi + T, v + plateTw / 2));
            }
            var holes = new PathsD();
            if (name == "Seat board")
            {
                // The pendulum's rod hangs through this slot, loose enough to swing.
                double rv = D - T - D / 2;
                holes.Add(Geo.Rect(Wi / 2 - (1.0 + f + 0.25) / 2, rv - sw / 2, Wi / 2 + (1.0 + f + 0.25) / 2, rv + sw / 2));
            }
            m.Add(name, plate, holes, [Pose.Up(2 * T, y + T / 2, D - T)], grainAlongU: true);
        }

        // ---- the pendulum: a hanger resting on the seat board, the rod down through its slot, the bob ----
        {
            double bobR = Geo.Clamp(Math.Min(winW / 2 + 0.35, Math.Min(2.2, (winY1 - winY0) * 0.2)), 1.0, Wi / 2 - 0.25);
            double bobY = Math.Min(winY0 + Math.Max(bobR + 0.9, (winY1 - winY0) * 0.22), seatY - bobR - 1.5);
            double hangW = Math.Min(Wi - 0.6, 3.2), hangH = 1.0;
            var p = new PathsD
            {
                Geo.Rect(xc - 0.5, bobY, xc + 0.5, hy0 + 0.01),
                WoodFace.RoundRect(xc - hangW / 2, hy0, xc + hangW / 2, hy0 + hangH, 0.3),
                Geo.Circle(xc, bobY, bobR, 0.02),
            };
            var lines = new List<PathD> { WoodFace.Dot(xc, bobY, bobR * 0.62), new PathD { new(xc - 0.25, hy0 - 0.4), new(xc + 0.25, hy0 - 0.4) } };
            var etched = new PathsD { Geo.Circle(xc - bobR * 0.3, bobY + bobR * 0.3, bobR * 0.22, 0.02) };
            m.Add("Pendulum", Geo.Solid(p), [], [Pose.Front(0, 0, D / 2)], grainAlongU: false, face: [], etched: etched, lines: lines);
        }

        // ---- the hood's and base's sides, glued on over the sides between the front and back ----
        if (!m.PushFit)
        {
            var hood = new PathsD { Geo.Rect(0, 0, Di, hoodH) };
            var etched = new PathsD();
            var lines = new List<PathD>();
            // A small side window and a panel line round it.
            double sw2 = Math.Min(Di * 0.45, 2.6), sy0 = hoodH * 0.28, sy1 = hoodH * 0.75;
            if (sw2 > 1.2) etched.AddRange(Fronts.Outline([gothic ? Fronts.PointedRect(Di / 2 - sw2 / 2, sy0, Di / 2 + sw2 / 2, sy1) : Fronts.ArchedRect(Di / 2 - sw2 / 2, sy0, Di / 2 + sw2 / 2, sy1)], 0.35));
            m.Add("Hood side", hood, [], [Pose.Left(T / 2, hy0, T), Pose.Right(W - T / 2, hy0, D - T)], grainAlongU: false, etched: etched, lines: lines).Glue();
            var plinth = new PathsD { Geo.Rect(0, 0, Di, baseH) };
            var pl = new List<PathD> { new PathD { new(0, baseH - 0.35), new(Di, baseH - 0.35) } };
            m.Add("Base side", plinth, Feet(Di), [Pose.Left(T / 2, 0, T), Pose.Right(W - T / 2, 0, D - T)], grainAlongU: false, lines: pl).Glue();
        }

        // ---- on the front: the dial's bezel, the trunk's glazed door, the base's raised panel ----
        if (!m.PushFit)
        {
            double bx0 = 0.3, bx1 = W - 0.3, by0 = 0.3, by1 = hoodH - 0.3;
            var bezel = new PathsD { Geo.Rect(bx0, by0, bx1, by1) };
            var hole = new PathsD { Geo.Circle(xc, dialY - hy0, dialR + 0.15, 0.02) };
            var etched = new PathsD();
            var lines = new List<PathD>();
            // Columns up each side of the dial: a shaft with fluting, a capital and a base.
            double colW = Math.Min(1.4, (W - 2 * dialR) / 2 - 0.9);
            if (colW >= 0.8)
                foreach (var cx in new[] { bx0 + 0.25 + colW / 2, bx1 - 0.25 - colW / 2 })
                {
                    etched.Add(Geo.Rect(cx - colW / 2 - 0.15, by0 + 0.2, cx + colW / 2 + 0.15, by0 + 0.75));
                    etched.Add(Geo.Rect(cx - colW / 2 - 0.15, by1 - 0.75, cx + colW / 2 + 0.15, by1 - 0.2));
                    lines.Add([new(cx - colW / 2, by0 + 0.75), new(cx - colW / 2, by1 - 0.75)]);
                    lines.Add([new(cx + colW / 2, by0 + 0.75), new(cx + colW / 2, by1 - 0.75)]);
                    lines.Add([new(cx, by0 + 1.1), new(cx, by1 - 1.1)]);
                }
            lines.Add(WoodFace.Dot(xc, dialY - hy0, dialR + 0.6));
            m.Add("Dial bezel", bezel, hole, [Pose.Front(0, hy0, D + T / 2)], grainAlongU: true, etched: etched, lines: lines).Glue();

            double dx0 = a + 0.5, dx1 = W - a - 0.5, dy0 = baseH + 0.4, dy1 = hy0 - 0.35;
            var door = new PathsD { Geo.Rect(dx0, dy0, dx1, dy1) };
            var dl = new List<PathD>();
            var de = new PathsD();
            // A beading round the glass, hinges on the left, a keyhole on the right.
            dl.Add(Geo.Loop(Geo.Offset([window], 0.45).FirstOrDefault() ?? window));
            foreach (var hy in new[] { dy0 + 1.2, dy1 - 1.8 }) de.Add(Geo.Rect(dx0 + 0.15, hy, dx0 + 0.5, hy + 0.6));
            Fronts.Keyhole(dx1 - Math.Max(0.6, (dx1 - dx0 - winW) / 4), (winY0 + winY1) / 2, 0.35, dl);
            m.Add("Trunk door", Geo.Move(door, 0, -baseH), Geo.Move([window], 0, -baseH), [Pose.Front(0, baseH, D + T / 2)], grainAlongU: false, etched: Geo.Move(de, 0, -baseH), lines: Geo.Map(dl, q => new PointD(q.x, q.y - baseH))).Glue();

            double px0 = 1.0, px1 = W - 1.0, py0 = kick + 0.5, py1 = baseH - 0.7;
            if (py1 - py0 > 2.2)
            {
                var pe = new PathsD();
                var pln = new List<PathD>();
                Fronts.Panel(look, px0 + 0.3, py0 + 0.3, px1 - 0.3, py1 - 0.3, pe, pln);
                m.Add("Base panel", [Geo.Rect(px0, py0, px1, py1)], [], [Pose.Front(0, 0, D + T / 2)], grainAlongU: true, etched: pe, lines: pln).Glue();
            }
        }

        // ---- the cap: over the front, back and hood sides, taking the sides' tabs and the crown's ----
        double crownTw = Geo.Clamp(W * 0.16, 1.4, 3.2), zc = D - T / 2 - 1.1;
        double[] crownTabs = W - 2 * (W * 0.26) >= crownTw + 1.4 ? [W * 0.26, W - W * 0.26] : [W / 2];
        {
            double Wc = W + 2 * o, Dc = D + 2 * o;
            var cap = new PathsD { Geo.Rect(0, 0, Wc, Dc) };
            var holes = new PathsD();
            foreach (var x in new[] { xs + T / 2, W - xs - T / 2 })
                holes.Add(Geo.Rect(x + o - sw / 2, Dc - (capZ + o) - (sideTw + f) / 2, x + o + sw / 2, Dc - (capZ + o) + (sideTw + f) / 2));
            foreach (var x in crownTabs) holes.Add(Geo.Rect(x + o - (crownTw + f) / 2, Dc - (zc + o) - sw / 2, x + o + (crownTw + f) / 2, Dc - (zc + o) + sw / 2));
            var lines = new List<PathD> { Geo.Loop(Geo.Rect(0.35, 0.35, Wc - 0.35, Dc - 0.35)) };
            m.Add("Cap", cap, holes, [Pose.Up(-o, hy1 + capT / 2, D + o)], grainAlongU: true, lines: lines);
        }

        // ---- the crown on the cap ----
        {
            double hb = Geo.Clamp(crownH * 0.28, 0.9, 1.5);
            var shape = new PathsD { Geo.Rect(0, 0, W, hb) };
            double top = crownH;
            if (gothic)
            {
                // A pointed gable with crockets, a spire on top and pinnacles at the ends.
                var gable = Fronts.PointedRect(W * 0.16, hb - 0.01, W * 0.84, top);
                shape.Add(gable);
                // Crockets: knobs sitting on the gable's two edges, up to the top.
                var edge = gable.Where(q => q.y > hb + (top - hb) * 0.3 && q.y < top - 0.6).ToList();
                for (int i = 0; i < edge.Count; i += Math.Max(1, edge.Count / 6)) shape.Add(Geo.Circle(edge[i].x, edge[i].y, 0.5, 0.02));
                foreach (var x in new[] { finR + 0.1, W - finR - 0.1 })
                {
                    shape.Add(Geo.Rect(x - finR, hb - 0.01, x + finR, hb + crownH * 0.45));
                    shape.Add(new PathD { new(x - finR, hb + crownH * 0.45 - 0.01), new(x + finR, hb + crownH * 0.45 - 0.01), new(x, hb + crownH * 0.45 + 2.0) });
                }
                shape.Add(Geo.Rect(xc - 0.55, top - 0.6, xc + 0.55, top + 0.9));
                shape.Add(new PathD { new(xc - 0.75, top + 0.8), new(xc + 0.75, top + 0.8), new(xc, top + finH) });
            }
            else
            {
                if (crown == Crown.Pediment) shape.Add([new(0, hb - 0.01), new(W, hb - 0.01), new(xc, top)]);
                else
                {
                    double rise = top - hb, r = (W * W / 4 + rise * rise) / (2 * rise), cy = hb + rise - r, a0 = Math.Asin(Geo.Clamp(W / 2 / r, -1, 1));
                    var arc = new PathD { new(0, hb - 0.01) };
                    arc.AddRange(Geo.Arc(xc, cy, r, Math.PI / 2 - a0, Math.PI / 2 + a0, 0.02).Reverse());
                    arc.Add(new PointD(W, hb - 0.01));
                    shape.Add(arc);
                }
                double neck = Math.Max(1.0, finR * 1.3);
                shape.Add(Geo.Rect(xc - neck / 2, top - 0.6, xc + neck / 2, top + 0.7));
                shape.Add(Geo.Circle(xc, top + 0.6 + finR, finR, 0.02));
                foreach (var x in new[] { finR + 0.15, W - finR - 0.15 })
                {
                    shape.Add(Geo.Rect(x - finR, hb - 0.01, x + finR, hb + 0.7));
                    shape.Add(Geo.Circle(x, hb + 0.6 + finR * 0.9, finR * 0.9, 0.02));
                }
            }
            foreach (var x in crownTabs) shape.Add(Geo.Rect(x - crownTw / 2, -capT, x + crownTw / 2, 0.01));
            var lines = new List<PathD> { new() { new(0, hb), new(W, hb) } };
            var etched = new PathsD();
            double rr = Math.Min((top - hb) * 0.3, W * 0.1);
            if (rr > 0.5)
            {
                double cy = hb + (top - hb) * 0.36;
                if (gothic) lines.Add(Geo.Loop(Cabinets.Trefoil(xc, cy, rr)));
                else if (look == FrontLook.Carved) lines.Add(Geo.Loop(Fronts.Star(xc, cy, rr, rr * 0.5, 8)));
                else { lines.Add(WoodFace.Dot(xc, cy, rr)); etched.Add(Geo.Circle(xc, cy, rr * 0.4, 0.02)); }
            }
            m.Add("Crown", Geo.Solid(shape), [], [Pose.Front(0, hy1 + capT, zc)], grainAlongU: true, face: [Geo.Rect(0, 0, W, top)], etched: etched, lines: lines);
        }
        m.Note("Push the bottom and the seat board into one side, add the other side, then the front and back over the sides' tabs. " +
               "Hang the pendulum through the seat board's slot before the cap goes on" +
               (m.PushFit ? ", press the cap onto the sides' tabs and stand the crown in its slots." : "; glue the hood and base sides, the bezel, the trunk door and the base panel on, press the cap onto the sides' tabs and stand the crown in its slots."));
    }

    /// <summary>A dial: a bezel, a dark chapter ring with Roman-style hour marks left light in it, a centre boss and hands at ten past ten.</summary>
    static void Dial(double cx, double cy, double r, PathsD etched, List<PathD> lines)
    {
        lines.Add(WoodFace.Dot(cx, cy, r));
        var ring = Geo.Subtract([Geo.Circle(cx, cy, r * 0.94, 0.01)], [Geo.Circle(cx, cy, r * 0.62, 0.01)]);
        var marks = new PathsD();
        for (int h = 0; h < 12; h++)
        {
            double a = Math.PI / 2 - h * Math.PI / 6;
            var (c, s) = (Math.Cos(a), Math.Sin(a));
            // I, II, III… as one, two or three strokes, a V at the quarters: drawn as strokes side by side.
            int strokes = h % 3 == 0 ? 3 : h % 2 == 0 ? 2 : 1;
            double mw = 0.16, gap = 0.12, span = strokes * mw + (strokes - 1) * gap;
            for (int k = 0; k < strokes; k++)
            {
                double side = -span / 2 + k * (mw + gap) + mw / 2;
                PointD P(double rad, double sd) => new(cx + c * rad - s * sd, cy + s * rad + c * sd);
                marks.Add([P(r * 0.66, side - mw / 2), P(r * 0.9, side - mw / 2), P(r * 0.9, side + mw / 2), P(r * 0.66, side + mw / 2)]);
            }
        }
        etched.AddRange(Geo.Subtract(ring, Geo.Solid(marks)));
        double ah = Math.PI / 2 - 10.2 * Math.PI / 6, am = Math.PI / 2 - Math.PI / 3;
        etched.AddRange(Clipper.InflatePaths(new PathsD { new PathD { new(cx, cy), new(cx + Math.Cos(ah) * r * 0.45, cy + Math.Sin(ah) * r * 0.45) } }, 0.14, JoinType.Round, EndType.Round, 2, 4));
        etched.AddRange(Clipper.InflatePaths(new PathsD { new PathD { new(cx, cy), new(cx + Math.Cos(am) * r * 0.6, cy + Math.Sin(am) * r * 0.6) } }, 0.09, JoinType.Round, EndType.Round, 2, 4));
        etched.Add(Geo.Circle(cx, cy, Math.Max(0.22, r * 0.1), 0.01));
        lines.Add(WoodFace.Dot(cx, cy, r * 0.4));
    }
}
