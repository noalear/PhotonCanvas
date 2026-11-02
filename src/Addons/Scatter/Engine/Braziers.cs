using Clipper2Lib;

namespace PhotonCanvas.Addon.Scatter;

/// <summary>
/// A brazier: a fire bowl on splayed legs with a brace between them, and flames rising out of it. Two
/// copies of the outline cross at right angles with a halving joint: one is slotted from the top down
/// to the middle of the bowl, the other from the bottom up to it, so they slide together into a
/// four-legged brazier.
/// </summary>
public static class Braziers
{
    public static void Make(Maker m)
    {
        var it = m.Item;
        double W = it.WidthMm, H = it.HeightMm, T = m.T, sw = m.SlotW, f = m.Fit;
        if (W < 6) { m.Bad("The brazier is too narrow: make it at least 6 mm across."); return; }
        double hb = Math.Clamp(W * 0.35, 3, 9), hf = it.Flames ? Math.Clamp(H * 0.3, 3, W * 0.9) : 0;
        double yt = H - hf, yb = yt - hb, need = hb + hf + 4;
        if (H < need) { m.Bad($"The brazier is too low for its width: make it at least {need:0.#} mm high, or narrower."); return; }
        if (W < sw + 4) { m.Bad($"The brazier is too narrow for a {T:0.##} mm sheet: make it at least {sw + 4:0.#} mm across."); return; }

        // The bowl: a rim, and a rounded underside.
        double rimH = Math.Clamp(hb * 0.22, 0.9, 1.6), cx = W / 2;
        var bowl = new PathD { new(0, yt), new(0, yt - rimH) };
        foreach (var q in Geo.Arc(cx, yt - rimH, W / 2 * 0.92, Math.PI, 2 * Math.PI)) bowl.Add(new PointD(q.x, yt - rimH + (q.y - (yt - rimH)) * (hb - rimH) / (W / 2 * 0.92)));
        bowl.Add(new PointD(W, yt - rimH));
        bowl.Add(new PointD(W, yt));
        var shape = new PathsD { Geo.Positive(bowl) };

        // Legs from under the bowl out to the floor, with feet and a brace.
        double lw = Math.Clamp(W * 0.09, 1.3, 2.6), foot = lw * 1.7, spread = W / 2 - foot / 2;
        foreach (var side in new[] { -1, 1 })
        {
            shape.Add(Geo.Strip(new PointD(cx + side * W * 0.2, yb + hb * 0.4), new PointD(cx + side * spread, lw * 0.5), lw));
            shape.Add(Geo.Rect(cx + side * spread - foot / 2, 0, cx + side * spread + foot / 2, Math.Max(0.9, lw * 0.6)));
        }
        double braceY = yb * 0.4, braceH = Math.Clamp(lw * 0.75, 1.1, 2);
        double braceX = W * 0.2 + (spread - W * 0.2) * (1 - (braceY - lw * 0.5) / (yb + hb * 0.4 - lw * 0.5));
        if (yb > 4) shape.Add(Geo.Rect(cx - braceX, braceY - braceH / 2, cx + braceX, braceY + braceH / 2));

        // Flames: three (or five) tongues, the middle one tallest.
        var lines = new List<PathD>();
        if (it.Flames)
        {
            int tongues = W >= 18 ? 5 : 3;
            double span = W * 0.8, r = span / (tongues * 1.5);
            shape.Add(Geo.Rect(cx - span / 2, yt - 0.01, cx + span / 2, yt + Math.Min(r, hf * 0.3)));
            for (int i = 0; i < tongues; i++)
            {
                double x = cx - span / 2 + span * (i + 0.5) / tongues, mid = Math.Abs(i - (tongues - 1) / 2.0) / Math.Max(1, (tongues - 1) / 2.0);
                double h = hf * (1 - 0.35 * mid) * (0.9 + 0.1 * m.Rnd.NextDouble()), lean = (m.Rnd.NextDouble() - 0.5) * r * 1.2;
                shape.Add(Tongue(x, yt, r * (1.1 - 0.2 * mid), h, lean));
                if (h > 3) lines.Add(Geo.Loop(Tongue(x + lean * 0.1, yt + h * 0.12, r * 0.45, h * 0.6, lean * 0.6)));
            }
        }
        var outline = Stacks.Smooth(Geo.Union(shape));

        // The bowl's rim and a band, scored.
        lines.Add([new(0, yt - rimH), new(W, yt - rimH)]);
        double band = yt - rimH - (hb - rimH) * 0.45;
        lines.Add([new(0, band), new(W, band)]);

        // The halving joint, at the middle of the bowl.
        double yj = (yb + yt) / 2;
        var fromTop = new PathsD { Geo.Rect(cx - sw / 2, yj - f / 2, cx + sw / 2, H + 1) };
        var fromBottom = new PathsD { Geo.Rect(cx - sw / 2, -1, cx + sw / 2, yj + f / 2) };
        // Smoothed again once slotted: the slot through the middle flame leaves slivers beside it.
        m.Add("Brazier (slot at the top)", Stacks.Biggest(Stacks.Smooth(Geo.Subtract(outline, fromTop))), [], [Pose.Front(0, 0, W / 2)], grainAlongU: true, wood: false, lines: lines);
        m.Add("Brazier (slot at the bottom)", Stacks.Biggest(Stacks.Smooth(Geo.Subtract(outline, fromBottom))), [], [Pose.Left(W / 2, 0, 0)], grainAlongU: true, wood: false, lines: lines);
        m.Note("Slide the two halves together across each other, slot into slot, so it stands on four legs" + (it.Flames ? ". Paint the flames, or cut them from a translucent sheet." : "."));
    }

    /// <summary>A flame tongue standing on (x, y0): round at the foot, pointed at the top, leaning a little.</summary>
    static PathD Tongue(double x, double y0, double r, double h, double lean)
    {
        var left = new PathD();
        var right = new PathD();
        const int n = 24;
        for (int i = 0; i <= n; i++)
        {
            double s = i / (double)n, hw = r * Math.Pow(1 - s, 0.8) * Math.Min(1, 0.55 + s * 3);
            double xc = x + lean * s * s, y = y0 + s * h;
            left.Add(new PointD(xc - hw, y));
            right.Add(new PointD(xc + hw, y));
        }
        right.Reverse();
        var p = new PathD(right);
        p.AddRange(left);
        return Geo.Positive(p);
    }
}
