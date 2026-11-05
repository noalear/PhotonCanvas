using Clipper2Lib;

namespace PhotonCanvas.Addon.Furniture;

/// <summary>
/// A cook fire out in the room: a ring of stones on a hearth stone, logs and flames standing in it, and
/// an A-frame at each end with a fork at the top carrying a spit across. A roast is skewered on the spit
/// and a cauldron hangs from it. The A-frames' and the fire's tabs go down through the hearth stone; the
/// spit lies in the forks; the stones, the roast and the cauldron are glued on.
/// </summary>
public static class CookFire
{
    public static void Make(Maker m)
    {
        var it = m.Item;
        var rnd = m.Rnd;
        double W = it.WidthMm, D = it.DepthMm, H = it.HeightMm, T = m.T, f = m.Fit, sw = m.SlotW;
        if (W < 16) { m.Bad("The cook fire is too narrow: make it at least 16 mm wide."); return; }
        if (D < 9) { m.Bad("The cook fire is too shallow: make it at least 9 mm deep."); return; }
        if (H < 13) { m.Bad("The cook fire is too low: make it at least 13 mm high."); return; }
        double zc = D / 2, px = 2.2 + sw / 2, tw = 1.5, rh = 1.4;
        double spread = Math.Min(zc - 1.9, H * 0.3);
        double[] feet = [zc - spread, zc + spread];

        // ---- the ring of stones and the fire inside it ----
        double s = Geo.Clamp(Math.Min(W, D) * 0.17, 1.8, 2.8);
        double rx = Math.Min(W / 2 - px - T / 2 - 0.6, W * 0.32), rz = Math.Min(zc - 0.4, D * 0.42);
        double rxi = rx - s, rzi = rz - s;
        double fw = Geo.Clamp(rxi * 1.5, 3, 9), fh = Geo.Clamp(H * 0.42, 4, 11), ftw = Geo.Clamp(fw * 0.22, 1.2, 2);
        double[] fireTabs = [fw * 0.28, fw * 0.72];

        // ---- the hearth stone (flat: u = x, v = D − z) ----
        var slots = new PathsD();
        foreach (var x in new[] { px, W - px })
            foreach (var z in feet)
                slots.Add(Geo.Rect(x - sw / 2, D - z - tw / 2 - f / 2, x + sw / 2, D - z + tw / 2 + f / 2));
        foreach (var u in fireTabs)
            slots.Add(Geo.Rect(W / 2 - fw / 2 + u - ftw / 2 - f / 2, D - zc - sw / 2, W / 2 - fw / 2 + u + ftw / 2 + f / 2, D - zc + sw / 2));
        var hearthE = new PathsD();
        var hearthL = new List<PathD>();
        // Ash in the ring, a few flags round it.
        hearthE.Add(Rugs.Ellipse(W / 2, D - zc, rxi + 0.3, rzi + 0.3));
        foreach (var x in new[] { 0.8, W - 0.8 })
            hearthL.Add([new(x, 0.6), new(x, D - 0.6)]);
        m.Add("Hearth stone", [WoodFace.RoundRect(0, 0, W, D, 1.5)], slots, [Pose.Up(0, T / 2, D)], grainAlongU: true, face: [], etched: hearthE, lines: hearthL);

        // The stones: lumps round an oval, each its own outline.
        var stones = new PathsD();
        var stoneL = new List<PathD>();
        double ra = rx - s / 2, rb = rz - s / 2, perim = Math.PI * (3 * (ra + rb) - Math.Sqrt((3 * ra + rb) * (ra + 3 * rb)));
        int n = Math.Max(8, (int)Math.Round(perim / (s * 0.9)));
        for (int i = 0; i < n; i++)
        {
            double a = 2 * Math.PI * i / n, cx = W / 2 + (rx - s / 2) * Math.Cos(a), cy = D - zc + (rz - s / 2) * Math.Sin(a);
            var lump = Lump(cx, cy, s * 0.62, rnd);
            stones.AddRange(lump);
            stoneL.AddRange(lump.Select(Geo.Loop));
        }
        var ring = Geo.Subtract(Geo.Union(stones, [Rugs.Ellipse(W / 2, D - zc, rx - s * 0.5, rz - s * 0.5)]), [Rugs.Ellipse(W / 2, D - zc, rxi, rzi)]);
        if (!m.PushFit) m.Add("Ring of stones", ring, [], [Pose.Up(0, T * 1.5, D)], grainAlongU: true, face: [], lines: stoneL).Glue();

        var (fire, fireL, fireE) = Logs(fw, fh, ftw, fireTabs, T);
        m.Add("Fire", fire, [], [Pose.Front(W / 2 - fw / 2, T, zc)], grainAlongU: true, face: [], etched: fireE, lines: fireL);

        // ---- the A-frames (flat: u = z, v = y) ----
        double top = H, nb = top - rh - 0.3, fork = sw + 2.4;
        var frame = new PathsD();
        foreach (var z in feet)
        {
            frame.Add(Geo.Strip(new PointD(z, T), new PointD(zc + (z < zc ? -0.4 : 0.4), nb - 0.6), 1.6));
            frame.Add(Geo.Rect(z - tw / 2, 0, z + tw / 2, T + 0.3));
            // A splayed foot on the hearth stone.
            frame.Add(Geo.Rect(z - 1.3, T, z + 1.3, T + 1.1));
        }
        double barY = T + (nb - T) * 0.38, barHalf = spread * (1 - (barY - T) / (nb - 0.6 - T)) + 0.2;
        frame.Add(Geo.Rect(zc - barHalf, barY - 0.6, zc + barHalf, barY + 0.6));
        frame.Add(Geo.Rect(zc - fork / 2, nb - 1.6, zc + fork / 2, top));
        var frameCut = Geo.Subtract(Geo.Solid(frame), [Geo.Rect(zc - sw / 2, nb, zc + sw / 2, top + 1)]);
        var frameL = new List<PathD>();
        // Iron bands round the fork and pegs through the cross bar.
        frameL.Add([new(zc - fork / 2, nb - 0.9), new(zc + fork / 2, nb - 0.9)]);
        foreach (var dz in new[] { -barHalf + 0.6, barHalf - 0.6 }) frameL.Add(WoodFace.Dot(zc + dz, barY, 0.25));
        m.Add("Spit frame", frameCut, [], [Pose.Left(px, 0, 0), Pose.Left(W - px, 0, 0)], grainAlongU: false, lines: frameL);

        // ---- the spit, lying in the forks, with a crank on the left ----
        double x0 = px - T / 2 - 1.8, x1 = W - px + T / 2 + 1.2;
        var spit = new PathsD { Geo.Rect(x0, nb, x1, nb + rh), Geo.Rect(x0, nb - 2.4, x0 + 1.2, nb + rh), Geo.Rect(x0 - 1.4, nb - 2.4, x0 + 1.2, nb - 1.2) };
        m.Add("Spit", Geo.Union(spit), [], [Pose.Front(0, 0, zc)], grainAlongU: true, face: []);

        // ---- a roast skewered on it and a cauldron hanging from it, glued on its front (left off for a push fit) ----
        if (m.PushFit)
        {
            m.Note("Push the A-frames' and the fire's tabs down into the hearth stone and lay the spit in the forks.");
            return;
        }
        double rcx = W / 2 - Math.Min(W * 0.17, rxi * 0.55), rcy = nb + rh / 2, brx = Geo.Clamp(W * 0.12, 2.2, 3.6), bry = brx * 0.7;
        var roast = new PathsD { Rugs.Ellipse(rcx, rcy, brx, bry) };
        var roastL = new List<PathD>();
        foreach (var sgn in new[] { -1, 1 })
        {
            // A plump drumstick each side, tipped up and out, the bone's knuckle showing past it (a bird roasting on its back).
            double dx = rcx + sgn * brx * 0.62, dy = rcy + bry * 0.55;
            roast.AddRange(Geo.Turn([Rugs.Ellipse(0, 0, brx * 0.55, bry * 0.42)], sgn * -40, dx, dy));
            double tx = dx + sgn * brx * 0.62, ty = dy + bry * 0.62;
            roast.Add(Geo.Strip(new PointD(dx, dy), new PointD(tx, ty), 1.1));
            roast.Add(Geo.Circle(tx, ty, 0.7, 0.01));
            roastL.Add([.. Geo.Turn([Rugs.Ellipse(0, 0, brx * 0.5, bry * 0.37)], sgn * -40, dx, dy)[0], Geo.Turn([Rugs.Ellipse(0, 0, brx * 0.5, bry * 0.37)], sgn * -40, dx, dy)[0][0]]);
        }
        roast = Geo.Solid(roast);
        roastL.AddRange(roast.Select(Geo.Loop));
        // A wing tucked along the side and the crisp skin's lines.
        roastL.Add([.. Geo.Arc(rcx - brx * 0.1, rcy - bry * 0.05, bry * 0.55, Math.PI * 0.9, Math.PI * 1.45)]);
        for (int i = -1; i <= 1; i++) roastL.Add([new(rcx + i * brx * 0.3 - 0.2, rcy - bry * 0.55), new(rcx + i * brx * 0.3 + 0.2, rcy - bry * 0.2)]);
        m.Add("Roast", roast, [], [Pose.Front(0, 0, zc + T)], grainAlongU: true, face: [], lines: roastL).Glue();

        double pcx = W / 2 + Math.Min(W * 0.17, rxi * 0.6), pw = Geo.Clamp(W * 0.22, 3.6, 6), ph = pw * 0.7, pTop = nb - Geo.Clamp(ph * 0.55, 1.6, 2.6);
        var pot = new PathsD
        {
            // A round belly narrowing a little to the rim.
            Geo.Intersect([Rugs.Ellipse(pcx, pTop - ph * 0.48, pw / 2, ph * 0.6)], [Geo.Rect(pcx - pw, pTop - ph * 1.2, pcx + pw, pTop)])[0],
            Geo.Rect(pcx - pw / 2 - 0.4, pTop - 0.8, pcx + pw / 2 + 0.4, pTop + 0.4),
        };
        // The bail: an arc of iron up to a hook over the spit.
        var bail = new PathD();
        for (int i = 0; i <= 16; i++)
        {
            double t = Math.PI * i / 16;
            bail.Add(new PointD(pcx - pw / 2 * Math.Cos(t), pTop + (nb + rh * 0.8 - pTop) * Math.Sin(t)));
        }
        pot.AddRange(Clipper.InflatePaths([bail], 0.55, JoinType.Round, EndType.Round, 2, 4));
        pot = Geo.Solid(pot);
        var potE = Geo.Intersect([Rugs.Ellipse(pcx, pTop - ph * 0.48, pw / 2 - 0.4, ph * 0.6 - 0.4)], [Geo.Rect(pcx - pw, pTop - ph * 1.2, pcx + pw, pTop - 1.0)]);
        var potL = new List<PathD> { new() { new(pcx - pw / 2 - 0.4, pTop - 0.2), new(pcx + pw / 2 + 0.4, pTop - 0.2) } };
        m.Add("Cauldron", pot, [], [Pose.Front(0, 0, zc + T)], grainAlongU: true, face: [], etched: potE, lines: potL).Glue();

        m.Note("Push the A-frames' and the fire's tabs down into the hearth stone, glue the ring of stones round the fire, lay the spit in the forks, and glue the roast and the cauldron on the spit's front.");
    }

    /// <summary>A stone: a rounded lump of six or seven sides.</summary>
    static PathsD Lump(double cx, double cy, double r, Random rnd)
    {
        var p = new PathD();
        int k = 6 + rnd.Next(2);
        double a0 = rnd.NextDouble() * Math.PI;
        for (int i = 0; i < k; i++)
        {
            double a = a0 + i * 2 * Math.PI / k, rr = r * (0.85 + 0.2 * rnd.NextDouble());
            p.Add(new PointD(cx + rr * Math.Cos(a), cy + rr * Math.Sin(a)));
        }
        var u = Geo.Solid([p]);
        var soft = Geo.Offset(Geo.Offset(u, -0.25), 0.25, round: true);
        return soft.Count > 0 ? soft : u;
    }

    /// <summary>Two crossed logs with flames rising from them, tabs down from the bottom.</summary>
    static (PathsD, List<PathD>, PathsD) Logs(double w, double h, double tw, double[] tabs, double T)
    {
        double lh = Math.Min(1.6, h * 0.3);
        var shape = new PathsD { WoodFace.RoundRect(0, 0, w, lh, lh / 2) };
        shape.AddRange(Geo.Turn([WoodFace.RoundRect(-w * 0.38, -lh / 2, w * 0.38, lh / 2, lh / 2)], 14, w * 0.45, lh * 1.1));
        shape.AddRange(Geo.Turn([WoodFace.RoundRect(-w * 0.33, -lh / 2, w * 0.33, lh / 2, lh / 2)], -16, w * 0.58, lh * 1.2));
        var fl = Profiles.Flames(w / 2, lh, w * 0.8, h - lh);
        shape.AddRange(fl);
        var etched = Geo.Intersect(Profiles.Flames(w / 2, lh, w * 0.42, (h - lh) * 0.55), fl);
        var lines = new List<PathD> { WoodFace.Dot(lh * 0.5, lh / 2, lh * 0.3), WoodFace.Dot(w - lh * 0.5, lh / 2, lh * 0.3) };
        shape = Geo.Intersect(Geo.Solid(shape), [Geo.Rect(-1, 0, w + 1, h + 1)]);
        foreach (var u in tabs) shape.Add(Geo.Rect(u - tw / 2, -T, u + tw / 2, 0.01));
        var all = Geo.Union(shape);
        return (all, Geo.ClipLines(lines, all), Geo.Intersect(etched, all));
    }
}
