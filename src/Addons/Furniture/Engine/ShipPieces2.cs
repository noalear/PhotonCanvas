using Clipper2Lib;

namespace PhotonCanvas.Addon.Furniture;

/// <summary>
/// More ship fittings: crew bunks over a storage locker with a ladder up the end, a cockpit console
/// with three screens, a rack of rifles, a pallet of fuel drums and a reactor core in its cage.
/// </summary>
public static partial class TownWalls
{
    /// <summary>(z, y) points turned into an upright made along +z from <paramref name="a"/>.</summary>
    static PathD ZY(Up up, V3 a, IEnumerable<(double Z, double Y)> pts) => [.. pts.Select(p => new PointD(UZ(up, a, p.Z), p.Y))];

    /// <summary>Tabs placed by room z (z0–z1), turned into the upright's u.</summary>
    static IEnumerable<(double, double)> TabsZ(Build b, Up up, V3 a, double z0, double z1, double edge)
    {
        foreach (var (t0, t1) in b.Tabs(z0, z1, edge))
        {
            double u0 = UZ(up, a, t0), u1 = UZ(up, a, t1);
            yield return (Math.Min(u0, u1), Math.Max(u0, u1));
        }
    }

    // ---- crew bunks ----

    /// <summary>
    /// Two crew bunks, one over the other, between two end frames: a storage locker with drawers under the
    /// lower bunk, a rail along the upper one, a ladder cut up the front of one end, mattresses and pillows.
    /// </summary>
    static void ShipBunks(Maker m)
    {
        var it = m.Item;
        double T = m.T, W = it.WidthMm, D = it.DepthMm, H = it.HeightMm, k = H / 28;
        double lw = Math.Max(4.6, 4.6 * k);
        if (W < 20 + 2 * T || D < lw + 8 + T) { m.Bad($"The bunks are too small: make them at least {20 + 2 * T:0.#} long and {lw + 8 + T:0.#} mm deep."); return; }
        double yf = T + 1.2, y1 = Math.Round(Math.Max(yf + T + 3.2, H * 0.3), 1), y2 = Math.Round(H * 0.66, 1);
        if (H < 18 || y2 - y1 < 2 * T + 4 || H - y2 < T + 4) { m.Bad("The bunks are too low: make them at least 18 mm high."); return; }
        var b = Metal(m, k);
        double xi0 = T + 0.02, xi1 = W - T - 0.02, zb = 0.6, zf = D - 0.6, zs = zf - 1.2 - T / 2;
        Flat Deck(string name, double y)
        {
            var f = new Flat { Name = name, Y = y, Texture = "none" };
            f.Outline.Add(Build.XZRect(xi0, zb, xi1, zf));
            b.Flats.Add(f);
            return f;
        }
        var floor = Deck("Locker floor", yf);
        var lower = Deck("Lower bunk", y1);
        var upper = Deck("Upper bunk", y2);
        lower.Lines.Add(Geo.Loop(Build.XZRect(xi0 + 0.6, zb + 0.6, xi1 - 0.6, zf - 0.6)));
        upper.Lines.Add(Geo.Loop(Build.XZRect(xi0 + 0.6, zb + 0.6, xi1 - 0.6, zf - 0.6)));
        // The end frames: one with the ladder up its front.
        foreach (var (x, f, ladder) in new[] { (T / 2, -V3.UnitX, false), (W - T / 2, V3.UnitX, true) })
        {
            var a = new V3(x, 0, 0);
            var e = b.Upright("Bunk end", a, V3.UnitZ, D, f);
            e.Plain = true;
            e.Outline.Add(Fronts.Chamfered(0, 0, D, H, Math.Max(1.6, 2 * k)));
            e.Outline.Add(Geo.Rect(0, 0, D, H / 2));
            // Feet: a gap under the locker floor.
            double zt1 = zf - lw - 1.0;
            foreach (var deck in new[] { floor, lower, upper })
                foreach (var (u0, u1) in TabsZ(b, e, a, zb + 0.6, zt1, 0.4)) b.FlatInto(deck, e, u0, u1);
            double ua = UZ(e, a, zb + 1.4), ub = UZ(e, a, zt1 - 0.4);
            double wu0 = Math.Min(ua, ub), wu1 = Math.Max(ua, ub);
            // Windows between the bunks and over the top one.
            foreach (var (w0, w1) in new[] { (y1 + 1.4, y2 - T - 1.4), (y2 + 1.4, H - Math.Max(2.4, 2.6 * k)) })
                if (w1 - w0 > 2.4 && wu1 - wu0 > 3) e.Holes.Add(WoodFace.RoundRect(wu0, w0, wu1, w1, Math.Min(1.4, (w1 - w0) / 2 - 0.01)));
            e.Lines.Add(Geo.Loop(WoodFace.RoundRect(wu0, yf + 0.8, wu1, y1 - T - 0.8, 0.6)));
            for (double y = yf + 1.6; y < y1 - T - 1.4; y += 1.2) e.Etched.Add(Geo.Rect(wu0 + 1.0, y, wu0 + (wu1 - wu0) * 0.45, y + 0.5));
            e.Etched.AddRange(Hazard(0.4, 0.4, D - 0.4, 1.2, 0.8));
            if (ladder)
            {
                // Rungs: holes up the front strip, a stile left at the front edge.
                double la = UZ(e, a, zf - lw + 1.2), lb = UZ(e, a, zf - 1.2), pitch = Math.Max(3.0, 3.2 * k);
                for (double y = 1.8; y + pitch - 1.2 < y2 + 3 * k; y += pitch)
                    e.Holes.Add(Geo.Rect(Math.Min(la, lb), y, Math.Max(la, lb), y + pitch - 1.2));
            }
            else
            {
                double lu = UZ(e, a, zf - lw / 2);
                for (double y = y2 + 1.6; y < H - 3; y += 1.4) e.Lines.Add([new(lu - 1.2, y), new(lu + 1.2, y)]);
            }
            for (int i = 0; i < 2; i++) e.Lines.Add(WoodFace.Dot(i == 0 ? 0.9 : D - 0.9, H - Math.Max(1.6, 2 * k) - 0.6, 0.22));
        }
        // The locker under the lower bunk: drawers along the front, a plain back.
        double L = xi1 - xi0;
        var front = b.Upright("Locker front", new V3(xi0, 0, zs), V3.UnitX, L, V3.UnitZ);
        var back = b.Upright("Locker back", new V3(xi0, 0, zb + 1.2 + T / 2), V3.UnitX, L, -V3.UnitZ);
        foreach (var p in new[] { front, back })
        {
            p.Plain = true;
            p.Outline.Add(Geo.Rect(0, yf, L, y1 - T));
            foreach (var (t0, t1) in b.Tabs(0, L, 1.5)) { b.DownInto(floor, p, t0, t1); b.UpInto(lower, p, t0, t1); }
        }
        int drawers = Math.Max(2, (int)Math.Round(L / (10 * k)));
        double dw = (L - 1.0) / drawers;
        for (int i = 0; i < drawers; i++)
        {
            double d0 = 0.5 + i * dw + 0.3, d1 = 0.5 + (i + 1) * dw - 0.3, dy0 = yf + 0.6, dy1 = y1 - T - 0.6;
            front.Lines.Add(Geo.Loop(Fronts.Chamfered(d0, dy0, d1, dy1, 0.5)));
            double hc = (d0 + d1) / 2, hy = dy1 - Math.Min(1.4, (dy1 - dy0) * 0.3);
            front.Etched.Add(WoodFace.RoundRect(hc - Math.Min(2.2, dw * 0.22), hy - 0.35, hc + Math.Min(2.2, dw * 0.22), hy + 0.35, 0.3));
            front.Lines.Add(WoodFace.Dot(d1 - 0.9, dy0 + 0.8, 0.2));
            front.Lines.Add(Geo.Loop(Geo.Rect(d0 + 0.8, dy0 + 0.6, d0 + Math.Min(4.0, dw * 0.4), dy0 + 1.4)));
        }
        // The rail along the upper bunk, open at the ladder end.
        double rh = Math.Max(3.0, 3.4 * k), rl = L - lw - 1.6;
        var rail = b.Upright("Bunk rail", new V3(xi0, 0, zs), V3.UnitX, rl, V3.UnitZ);
        rail.Plain = true;
        rail.Outline.Add(Geo.Rect(0, y2, rl, y2 + rh));
        rail.Outline.Add(WoodFace.RoundRect(rl - 2.0, y2, rl, y2 + rh + 0.8, 0.8));
        foreach (var (t0, t1) in b.Tabs(0, rl, 1.2)) b.DownInto(upper, rail, t0, t1);
        double sy0 = y2 + 1.2, sy1 = y2 + rh - 1.2;
        if (sy1 - sy0 > 0.6)
            for (double x = 1.6; x + 2.4 < rl - 2.4; x += 3.4) rail.Holes.Add(WoodFace.RoundRect(x, sy0, x + 2.4, sy1, Math.Min(0.4, (sy1 - sy0) / 2 - 0.01)));
        // Mattresses and pillows, glued on (or drawn on the bunks for push-fit).
        foreach (var (deck, z1) in new[] { (lower, zf - 0.4), (upper, zs - T / 2 - 0.2) })
        {
            double mx0 = xi0 + 0.4, mx1 = xi1 - 0.4, mz0 = zb + 0.4, pl1 = mx0 + Math.Min(6 * k, (mx1 - mx0) * 0.22);
            var mat = Fronts.Chamfered(mx0, -z1, mx1, -mz0, 0.8);
            var pil = WoodFace.RoundRect(mx0 + 0.6, -(z1 - 0.8), pl1, -(mz0 + 0.8), 1.0);
            if (b.Glue)
            {
                var mt = new Flat { Name = "Mattress", Y = deck.Y + T, Texture = "none", Glued = true };
                mt.Outline.Add(mat);
                for (double x = pl1 + 3; x < mx1 - 1.5; x += 3.6) mt.Lines.Add([new(x, -(z1 - 0.8)), new(x, -(mz0 + 0.8))]);
                mt.Lines.Add([new(mx1 - 6 * k, -(z1 - 0.6)), new(mx1 - 6 * k, -(mz0 + 0.6))]);
                b.Flats.Add(mt);
                var pw = new Flat { Name = "Pillow", Y = deck.Y + 2 * T, Texture = "none", Glued = true };
                pw.Outline.Add(pil);
                pw.Lines.Add([new((mx0 + pl1) / 2 + 0.3, -(z1 - 1.6)), new((mx0 + pl1) / 2 + 0.3, -(mz0 + 1.6))]);
                b.Flats.Add(pw);
            }
            else
            {
                deck.Lines.Add(Geo.Loop(mat));
                deck.Lines.Add(Geo.Loop(pil));
            }
        }
        b.Finish();
        m.Note("Push the locker front and back into the floor, put the floor and both bunks' tabs through one end frame, push the other end on, then the rail into the upper bunk" + (b.Glue ? "; glue on the mattresses and pillows." : "."));
    }

    // ---- the cockpit console ----

    /// <summary>
    /// A cockpit console: a desk on two pedestals with a vented front, a keyboard and rows of buttons on top,
    /// and three screens standing on it (the middle one tallest, the outer two angled in) showing a radar, a
    /// readout and a graph.
    /// </summary>
    static void ShipConsole(Maker m)
    {
        var it = m.Item;
        double T = m.T, W = it.WidthMm, D = it.DepthMm, H = it.HeightMm, k = H / 20;
        if (W < 20 || D < 11 + 1.5 * T + 3.2 * k) { m.Bad($"The console is too small: make it at least 20 wide and {Math.Ceiling(11 + 1.5 * T + 3.2 * k):0} mm deep."); return; }
        double yk = Math.Round(Math.Max(3 * T + 3, H * 0.42), 1);
        if (H - yk < 7) { m.Bad("The console is too low for its screens: make it taller."); return; }
        var b = Metal(m, k);
        double xs0 = 1.4 + T / 2, xs1 = W - 1.4 - T / 2, zb = 1.6, zf = D - 1.0, zsc = zb + 1.5 * T + 1.4, zso = zsc + 3.2 * k;
        var plate = new Flat { Name = "Deck plate", Y = T, Texture = "none" };
        plate.Outline.Add(Fronts.Chamfered(0, -D, W, 0, 1.2));
        plate.Etched.AddRange(Hazard(0.8, -D + 0.6, 4.0, -D + 1.6, 0.8));
        plate.Etched.AddRange(Hazard(W - 4.0, -D + 0.6, W - 0.8, -D + 1.6, 0.8));
        b.Flats.Add(plate);
        var desk = new Flat { Name = "Desk top", Y = yk, Texture = "none" };
        desk.Outline.Add(Fronts.Chamfered(0, -D, W, 0, 1.4));
        desk.Lines.Add(Geo.Loop(Fronts.Chamfered(0.6, -D + 0.6, W - 0.6, -1.0, 1.0)));
        b.Flats.Add(desk);
        // The pedestals.
        foreach (var (x, f) in new[] { (xs0, -V3.UnitX), (xs1, V3.UnitX) })
        {
            var a = new V3(x, 0, 0);
            var s = b.Upright("Pedestal", a, V3.UnitZ, D, f);
            s.Plain = true;
            s.Outline.Add(ZY(s, a, [(zb, T), (zf - 1.6, T), (zf, T + 1.6), (zf, yk - T), (zb, yk - T)]));
            foreach (var (u0, u1) in TabsZ(b, s, a, zb, zf, 1.2)) b.DownInto(plate, s, u0, u1);
            // Up into the desk in front of the screens, clear of their slots.
            foreach (var (u0, u1) in TabsZ(b, s, a, Math.Min(zso + 1.6, zf - 3.2), zf, 0.4)) b.UpInto(desk, s, u0, u1);
            double ua = UZ(s, a, zb + 1.0), ub = UZ(s, a, zf - 1.0);
            s.Lines.Add(Geo.Loop(Fronts.Chamfered(Math.Min(ua, ub), T + 2.2, Math.Max(ua, ub), yk - T - 0.8, 0.6)));
            double uv = UZ(s, a, (zb + zf) / 2);
            for (double y = T + 3.0; y < yk - T - 1.4; y += 1.2) s.Etched.Add(Geo.Rect(uv - 2.0, y, uv + 2.0, y + 0.5));
            double uh0 = UZ(s, a, zf - 4.0), uh1 = UZ(s, a, zf - 0.8);
            s.Etched.AddRange(Hazard(Math.Min(uh0, uh1), T + 0.4, Math.Max(uh0, uh1), T + 1.6, 0.7));
        }
        // The front (set back for the knees' sake) and the back.
        double fx0 = xs0 + T / 2 + 0.02, fx1 = xs1 - T / 2 - 0.02, L = fx1 - fx0;
        var front = b.Upright("Console front", new V3(fx0, 0, zf - 2.4 - T / 2), V3.UnitX, L, V3.UnitZ);
        var back = b.Upright("Console back", new V3(fx0, 0, zb + T / 2), V3.UnitX, L, -V3.UnitZ);
        foreach (var p in new[] { front, back })
        {
            p.Plain = true;
            p.Outline.Add(Geo.Rect(0, T, L, yk - T));
            foreach (var (t0, t1) in b.Tabs(0, L, 1.5)) { b.DownInto(plate, p, t0, t1); b.UpInto(desk, p, t0, t1); }
        }
        int panels = Math.Max(2, (int)Math.Round(L / (8 * k)));
        double pw_ = L / panels;
        for (int i = 0; i < panels; i++)
        {
            double a0 = i * pw_ + 0.5, a1 = (i + 1) * pw_ - 0.5;
            front.Lines.Add(Geo.Loop(Fronts.Chamfered(a0, T + 0.8, a1, yk - T - 0.8, 0.7)));
            if (i % 2 == 0) for (double y = T + 1.8; y < yk - T - 1.6; y += 1.2) front.Etched.Add(Geo.Rect(a0 + 1.0, y, a1 - 1.0, y + 0.5));
            else { front.Etched.Add(Geo.Circle((a0 + a1) / 2, (T + yk - T) / 2, Math.Min(1.4, (a1 - a0) * 0.2), 0.01)); front.Lines.Add(WoodFace.Dot((a0 + a1) / 2, (T + yk - T) / 2, Math.Min(0.6, (a1 - a0) * 0.08))); }
        }
        // The desk: a keyboard in front of the middle screen, buttons and sliders either side.
        double kz0 = D - 2.0, kz1 = D - Math.Min(6.0 * k, D * 0.4), kx0 = W * 0.33, kx1 = W * 0.67;
        PathsD keys = [];
        for (double z = kz1 + 0.3; z < kz0 - 0.6; z += 1.1)
            for (double x = kx0 + 0.4; x < kx1 - 0.7; x += 1.1) keys.Add(Geo.Rect(x, -(z + 0.7), x + 0.7, -z));
        if (b.Glue)
        {
            var kb = new Flat { Name = "Keyboard", Y = yk + T, Texture = "none", Glued = true };
            kb.Outline.Add(Fronts.Chamfered(kx0, -kz0, kx1, -kz1, 0.5));
            kb.Etched.AddRange(keys);
            b.Flats.Add(kb);
        }
        else desk.Etched.AddRange(keys);
        foreach (var (x0, x1) in new[] { (1.4, kx0 - 1.2), (kx1 + 1.2, W - 1.4) })
        {
            for (double x = x0 + 0.6; x < x1 - 0.6; x += 1.4)
            {
                desk.Etched.Add(Geo.Circle(x, -(D - 2.4), 0.45, 0.01));
                desk.Lines.Add(WoodFace.Dot(x, -(D - 4.0), 0.35));
            }
            desk.Etched.Add(Geo.Rect(x0 + 0.4, -(D - 5.2), x1 - 0.4, -(D - 5.8)));
        }
        // The screens: the middle one square on, the outer two angled in.
        double sw = Math.Round(W * 0.34, 1), gap = 1.8;
        double mx0 = W / 2 - sw / 2, mx1 = W / 2 + sw / 2;
        void Screen(string name, V3 a, V3 dir, double len, double top, int kind)
        {
            var facing = new V3(-dir.Z, 0, dir.X);
            var s = b.Upright(name, a, dir, len, facing);
            s.Plain = true;
            double c = len / 2, nw = Math.Max(2.4, 2.6 * k), ys0 = yk + Math.Max(1.8, 2.2 * k);
            s.Outline.Add(Geo.Rect(c - nw / 2 - 1.0, yk, c + nw / 2 + 1.0, yk + 1.0));
            s.Outline.Add(Geo.Rect(c - nw / 2, yk, c + nw / 2, ys0 + 0.02));
            s.Outline.Add(WoodFace.RoundRect(0, ys0, len, top, 0.8));
            b.DownInto(desk, s, c - nw / 2 + 0.2, c + nw / 2 - 0.2);
            double gx0 = 0.8, gx1 = len - 0.8, gy0 = ys0 + 0.8, gy1 = top - 0.8;
            s.Etched.Add(Geo.Rect(gx0, gy0, gx1, gy1));
            s.Lines.Add(WoodFace.Dot(len - 0.5, ys0 + 0.4, 0.15));
            double gw = gx1 - gx0, gh = gy1 - gy0, cx = (gx0 + gx1) / 2, cy = (gy0 + gy1) / 2;
            switch (kind)
            {
                case 0:
                    // A radar: rings, a sweep and blips.
                    double r = Math.Min(gw, gh) * 0.42;
                    for (int i = 1; i <= 3; i++) s.Lines.Add(Geo.Loop(Geo.Circle(cx, cy, r * i / 3, 0.02)));
                    s.Lines.Add([new(cx - r, cy), new(cx + r, cy)]);
                    s.Lines.Add([new(cx, cy - r), new(cx, cy + r)]);
                    s.Lines.Add([new(cx, cy), new(cx + r * 0.7, cy + r * 0.7)]);
                    s.Lines.Add(WoodFace.Dot(cx - r * 0.45, cy + r * 0.3, 0.25));
                    s.Lines.Add(WoodFace.Dot(cx + r * 0.2, cy - r * 0.55, 0.25));
                    if (gw > 2 * r + 3)
                        for (double y = gy0 + 0.6; y < gy1 - 0.4; y += 1.0) { s.Lines.Add([new(gx0 + 0.4, y), new(gx0 + 0.4 + (cx - r - gx0 - 0.8) * (0.4 + 0.6 * Math.Abs(Math.Sin(y * 2.3))), y)]); }
                    break;
                case 1:
                    // A readout: lines of text and a bar.
                    for (double y = gy1 - 0.8; y > gy0 + 1.6; y -= 0.9) s.Lines.Add([new(gx0 + 0.5, y), new(gx0 + 0.5 + (gw - 1) * (0.3 + 0.6 * Math.Abs(Math.Sin(y * 3.1))), y)]);
                    s.Lines.Add(Geo.Loop(Geo.Rect(gx0 + 0.5, gy0 + 0.4, gx1 - 0.5, gy0 + 1.2)));
                    break;
                default:
                    // A graph: a wave over a grid.
                    for (double x = gx0 + gw / 4; x < gx1 - 0.1; x += gw / 4) s.Lines.Add([new(x, gy0 + 0.3), new(x, gy1 - 0.3)]);
                    var wave = new PathD();
                    for (int i = 0; i <= 24; i++) { double t = i / 24.0; wave.Add(new PointD(gx0 + 0.4 + (gw - 0.8) * t, cy + gh * 0.3 * Math.Sin(t * Math.PI * 3) * (0.5 + 0.5 * t))); }
                    s.Lines.Add(wave);
                    break;
            }
        }
        Screen("Main screen", new V3(mx0, 0, zsc), V3.UnitX, sw, H, 0);
        foreach (var (outer, inner, kind) in new[] { (new V3(1.4, 0, zso), new V3(mx0 - gap, 0, zsc), 1), (new V3(mx1 + gap, 0, zsc), new V3(W - 1.4, 0, zso), 2) })
        {
            var d = inner - outer;
            double len = Math.Sqrt(d.X * d.X + d.Z * d.Z);
            Screen("Side screen", outer, d * (1 / len), len, H - Math.Max(1.6, 2 * k), kind);
        }
        b.Finish();
        m.Note("Push the pedestals, front and back into the deck plate and the desk top onto them, then stand the screens in their slots" + (b.Glue ? "; glue the keyboard on in front of the middle screen." : "."));
    }

    // ---- the rifle rack ----

    /// <summary>A rifle standing on its butt at (cx, y0), <paramref name="len"/> long: stock, grip, magazine, scope, handguard, barrel and muzzle.</summary>
    static PathsD Rifle(double cx, double y0, double len, double k)
    {
        double s = Math.Max(1, k), bw = 0.6 * s;
        return
        [
            new PathD { new(cx - 0.8 * s, y0), new(cx + 1.0 * s, y0), new(cx + 1.0 * s, y0 + 0.06 * len), new(cx + 0.6 * s, y0 + 0.22 * len), new(cx - 0.8 * s, y0 + 0.22 * len) },
            Geo.Rect(cx - 0.9 * s, y0 + 0.2 * len, cx + 0.8 * s, y0 + 0.56 * len),
            new PathD { new(cx + 0.7 * s, y0 + 0.26 * len), new(cx + 1.9 * s, y0 + 0.23 * len), new(cx + 2.0 * s, y0 + 0.23 * len + 1.2 * s), new(cx + 0.7 * s, y0 + 0.26 * len + 1.4 * s) },
            Geo.Rect(cx + 0.7 * s, y0 + 0.38 * len, cx + 2.1 * s, y0 + 0.38 * len + 1.2 * s),
            Geo.Rect(cx - 2.0 * s, y0 + 0.42 * len, cx - 0.8 * s, y0 + 0.56 * len),
            Geo.Rect(cx - 0.75 * s, y0 + 0.56 * len, cx + 0.75 * s, y0 + 0.8 * len),
            Geo.Rect(cx - bw, y0 + 0.79 * len, cx + bw, y0 + len - 1.2),
            Geo.Rect(cx - 0.75 * s, y0 + len - 1.6, cx + 0.75 * s, y0 + len),
        ];
    }

    /// <summary>
    /// A rifle rack: a back of armour plate between two sides, a lit header on top, an ammo drawer under
    /// a shelf, and on the shelf a row of rifles held by a clamp bar (one piece, standing in slots).
    /// </summary>
    static void RifleRack(Maker m)
    {
        var it = m.Item;
        double T = m.T, W = it.WidthMm, D = it.DepthMm, H = it.HeightMm, k = H / 26;
        double sp = 4.8 * Math.Max(1, k);
        if (W < 2 * T + 2.4 + 2 * sp + 1 || D < 2 * T + 4.6) { m.Bad($"The rifle rack is too small: make it at least {Math.Ceiling(2 * T + 3.4 + 2 * sp):0} wide and {2 * T + 4.6:0.#} mm deep."); return; }
        double yb = Math.Round(T + Math.Max(T + 3, 5 * k), 1), yt = H - T;
        double len = yt - 1.0 - yb;
        if (len < 12) { m.Bad("The rifle rack is too low for its rifles: make it at least " + Math.Ceiling(H + 12 - len) + " mm high."); return; }
        var b = Metal(m, k);
        double xs0 = 1.2 + T / 2, xs1 = W - 1.2 - T / 2, xi0 = xs0 + T / 2 + 0.02, xi1 = xs1 - T / 2 - 0.02, zb = 0.6, zf = D - 0.6;
        var plinth = new Flat { Name = "Plinth", Y = T, Texture = "none" };
        plinth.Outline.Add(Build.XZRect(0, 0, W, D));
        plinth.Etched.AddRange(Hazard(0.6, -D + 0.3, W - 0.6, -D + 1.1, 0.8));
        b.Flats.Add(plinth);
        var top = new Flat { Name = "Header", Y = H, Texture = "none" };
        top.Outline.Add(Build.XZRect(0, 0, W, D));
        top.Lines.Add(Geo.Loop(Build.XZRect(0.6, 0.6, W - 0.6, D - 0.6)));
        top.Etched.Add(Build.XZRect(W * 0.2, D - 1.6, W * 0.8, D - 1.0));
        b.Flats.Add(top);
        var shelf = new Flat { Name = "Shelf", Y = yb, Texture = "none" };
        shelf.Outline.Add(Build.XZRect(xi0, zb + 1.2 + T + 0.02, xi1, zf));
        b.Flats.Add(shelf);
        foreach (var (x, f) in new[] { (xs0, -V3.UnitX), (xs1, V3.UnitX) })
        {
            var a = new V3(x, 0, 0);
            var s = b.Upright("Rack side", a, V3.UnitZ, D, f);
            s.Plain = true;
            s.Outline.Add(ZY(s, a, [(zb, T), (zf, T), (zf, yb + 1.0), (zf - 1.6, yb + 2.6), (zf - 1.6, yt - 3.0), (zf, yt - 1.4), (zf, yt), (zb, yt)]));
            foreach (var (u0, u1) in TabsZ(b, s, a, zb, zf - 1.6, 0.8)) { b.DownInto(plinth, s, u0, u1); b.UpInto(top, s, u0, u1); }
            foreach (var (u0, u1) in TabsZ(b, s, a, zb + 1.2 + T + 0.4, zf - 0.4, 0.4)) b.FlatInto(shelf, s, u0, u1);
            double ua = UZ(s, a, zb + 1.0), ub = UZ(s, a, zf - 2.6);
            s.Lines.Add(Geo.Loop(Fronts.Chamfered(Math.Min(ua, ub), yb + 1.6, Math.Max(ua, ub), yt - 1.6, 0.8)));
            double uc = UZ(s, a, (zb + zf - 1.6) / 2);
            for (double y = yb + 3; y < yt - 3; y += 1.6) s.Etched.Add(Geo.Rect(uc - 1.0, y, uc + 1.0, y + 0.6));
        }
        double L = xi1 - xi0;
        var back = b.Upright("Rack back", new V3(xi0, 0, zb + 1.2 + T / 2), V3.UnitX, L, -V3.UnitZ);
        back.Plain = true;
        back.Outline.Add(Geo.Rect(0, T, L, yt));
        back.Lines.Add(Geo.Loop(Geo.Rect(0.6, yb + 0.6, L - 0.6, yt - 0.6)));
        back.Etched.Add(Geo.Rect(0.6, yt - 2.0, L - 0.6, yt - 1.2));
        foreach (var (t0, t1) in b.Tabs(0, L, 1.5)) { b.DownInto(plinth, back, t0, t1); b.UpInto(top, back, t0, t1); }
        // The ammo drawer under the shelf.
        var drawer = b.Upright("Ammo drawer", new V3(xi0, 0, zf - 1.2 - T / 2), V3.UnitX, L, V3.UnitZ);
        drawer.Plain = true;
        drawer.Outline.Add(Geo.Rect(0, T, L, yb - T));
        // Down into the plinth only: the shelf rests on it, the rifles' tabs come down behind it.
        foreach (var (t0, t1) in b.Tabs(0, L, 1.5)) b.DownInto(plinth, drawer, t0, t1);
        for (int i = 0; i < 2; i++)
        {
            double d0 = 0.5 + i * (L - 1) / 2 + 0.3, d1 = 0.5 + (i + 1) * (L - 1) / 2 - 0.3, dy0 = T + 0.5, dy1 = yb - T - 0.5;
            drawer.Lines.Add(Geo.Loop(Fronts.Chamfered(d0, dy0, d1, dy1, 0.4)));
            double c = (d0 + d1) / 2, hy = (dy0 + dy1) / 2;
            drawer.Etched.Add(WoodFace.RoundRect(c - 1.8, hy - 0.35, c + 1.8, hy + 0.35, 0.3));
            drawer.Lines.Add(Geo.Loop(Geo.Rect(d0 + 0.6, dy1 - 1.2, d0 + Math.Min(3.4, (d1 - d0) * 0.3), dy1 - 0.5)));
        }
        // The rifles, one piece with their clamp bar, standing in the shelf.
        double zr = (zb + 1.2 + T + zf - 1.2 - T) / 2 + 0.3;
        int n = Math.Max(2, (int)Math.Floor((L - 1) / sp));
        double pitch = L / n;
        var row = b.Upright("Rifles", new V3(xi0, 0, zr), V3.UnitX, L, V3.UnitZ);
        row.Plain = true;
        double yc = yb + len * 0.66, ch = Math.Max(1.6, 1.8 * k);
        for (int i = 0; i < n; i++)
        {
            double cx = (i + 0.5) * pitch;
            row.Outline.AddRange(Rifle(cx, yb, len, k));
            b.DownInto(shelf, row, cx - 0.6, cx + 0.6);
            double s = Math.Max(1, k);
            row.Lines.Add([new(cx - 0.9 * s + 0.3, yb + 0.3 * len), new(cx + 0.8 * s - 0.3, yb + 0.3 * len)]);
            for (double y = yb + 0.6 * len; y < yb + 0.78 * len; y += 1.2) row.Lines.Add([new(cx - 0.4 * s, y), new(cx + 0.4 * s, y)]);
            row.Etched.Add(Geo.Rect(cx - 0.3 * s, yb + 0.05 * len, cx + 0.3 * s, yb + 0.19 * len));
            row.Etched.Add(Geo.Rect(cx - 0.4 * s, yb + 0.32 * len, cx + 0.4 * s, yb + 0.52 * len));
            row.Lines.Add(WoodFace.Dot(cx + 1.4 * s, yb + 0.38 * len + 0.6 * s, 0.18));
        }
        row.Outline.Add(Geo.Rect(0.2, yc, L - 0.2, yc + ch));
        row.Etched.Add(Geo.Rect(0.8, yc + 0.5, L - 0.8, yc + ch - 0.5));
        b.Finish();
        m.Note("Push the back and the ammo drawer front into the plinth, put the shelf's tabs through the sides and the sides onto the plinth, stand the rifles in the shelf's slots and push the header on.");
    }

    // ---- fuel drums ----

    /// <summary>
    /// Three fuel drums on a skid, two in front and one behind: glued up from rings (ribs standing out,
    /// a lid with bungs and a flame sign), or for push-fit two drum profiles halved together between a lid
    /// and the skid, a label band round each.
    /// </summary>
    static void FuelDrums(Maker m)
    {
        var it = m.Item;
        double T = m.T, F = m.Fit, W = it.WidthMm, D = it.DepthMm, H = it.HeightMm, k = H / 14, sw = T + F;
        double r = Math.Floor(Math.Min((W / 2 - 1.9) / 2, (D - 3.47) / 3.732) * 10) / 10;
        if (r < Math.Max(3.4, sw / 2 + 2.6)) { m.Bad($"The fuel drums are too small: make them at least {Math.Ceiling(2 * (2 * Math.Max(3.4, sw / 2 + 2.6) + 1.9)):0} wide and {Math.Ceiling(3.732 * Math.Max(3.4, sw / 2 + 2.6) + 3.47):0} mm deep."); return; }
        int layers = (int)Math.Round((H - T) / T);
        if (layers < 3) { m.Bad("The fuel drums are too low: make them taller."); return; }
        var b = Metal(m, k);
        double yTop = T + layers * T;
        double zfront = D - 1.3 - r, zback = zfront - 1.732 * (r + 0.5);
        var drums = new[] { (W / 2 - r - 0.5, zfront), (W / 2 + r + 0.5, zfront), (W / 2, zback) };
        var skid = new Flat { Name = "Skid", Y = T, Texture = "none" };
        skid.Outline.Add(Fronts.Chamfered(0, -D, W, 0, 1.6));
        skid.Lines.Add(Geo.Loop(Fronts.Chamfered(0.6, -D + 0.6, W - 0.6, -0.6, 1.2)));
        skid.Etched.AddRange(Hazard(0.8, -D + 0.8, W - 0.8, -D + 1.8, 0.8));
        b.Flats.Add(skid);
        void Lid(PathsD etched, List<PathD> lines, double cx, double cz, double rr)
        {
            lines.Add(Geo.Loop(Geo.Circle(cx, -cz, rr - 0.6, 0.02)));
            etched.Add(Geo.Circle(cx + rr * 0.48, -cz - rr * 0.1, Math.Max(0.7, rr * 0.16), 0.01));
            lines.Add(WoodFace.Dot(cx + rr * 0.48, -cz - rr * 0.1, Math.Max(0.35, rr * 0.08)));
            etched.Add(Geo.Circle(cx - rr * 0.55, -cz + rr * 0.2, Math.Max(0.5, rr * 0.1), 0.01));
            double fs = rr * 0.36;
            var diamond = new PathD { new(cx - rr * 0.05, -cz - fs * 1.1), new(cx - rr * 0.05 + fs, -cz), new(cx - rr * 0.05, -cz + fs * 1.1), new(cx - rr * 0.05 - fs, -cz) };
            lines.Add(Geo.Loop(diamond));
            etched.AddRange(Geo.Intersect(Profiles.Flames(cx - rr * 0.05, -cz - fs * 0.55, fs * 0.8, fs * 1.1), [diamond]));
        }
        foreach (var (cx, cz) in drums)
        {
            if (b.Glue)
            {
                // Rings stacked up: a chime at the foot, two ribs, the lid on top.
                int rib1 = Math.Max(1, layers / 3), rib2 = Math.Max(rib1 + 1, layers - 1 - layers / 3);
                for (int i = 0; i < layers; i++)
                {
                    bool lid = i == layers - 1, rib = i == rib1 || i == rib2;
                    double rr = rib ? r + 0.4 : i == 0 ? r - 0.2 : r;
                    var f = new Flat { Name = lid ? "Drum lid" : rib ? "Drum rib" : "Drum ring", Y = T + (i + 1) * T, Texture = "none", Glued = true };
                    f.Outline.Add(Geo.Circle(cx, -cz, rr, 0.02));
                    if (lid) Lid(f.Etched, f.Lines, cx, cz, rr);
                    b.Flats.Add(f);
                }
            }
            else
            {
                // Two profiles halved together between the skid and the lid.
                var lidF = new Flat { Name = "Drum lid", Y = yTop, Texture = "none" };
                lidF.Outline.Add(Geo.Circle(cx, -cz, r, 0.02));
                Lid(lidF.Etched, lidF.Lines, cx, cz, r);
                b.Flats.Add(lidF);
                double ym = (T + yTop - T) / 2, ry1 = T + (yTop - 2 * T) * 0.3, ry2 = T + (yTop - 2 * T) * 0.72;
                for (int p = 0; p < 2; p++)
                {
                    var dir = p == 0 ? V3.UnitX : V3.UnitZ;
                    var facing = new V3(-dir.Z, 0, dir.X);
                    var up = b.Upright(p == 0 ? "Drum" : "Drum (crossing)", new V3(cx, 0, cz) - dir * r, dir, 2 * r, facing);
                    up.Plain = true;
                    up.Outline.Add(Geo.Rect(0.1, T, 2 * r - 0.1, yTop - T));
                    foreach (var ry in new[] { ry1, ry2 }) up.Outline.Add(Geo.Rect(-0.3, ry - 0.6, 2 * r + 0.3, ry + 0.6));
                    up.Holes.Add(p == 0 ? Geo.Rect(r - sw / 2, ym, r + sw / 2, H + 5) : Geo.Rect(r - sw / 2, -5, r + sw / 2, ym));
                    double off = sw / 2 + 1.4, e = 1.3;
                    if (p == 0)
                    {
                        b.DownInto(skid, up, r - 1.0, r + 1.0);
                        b.UpInto(lidF, up, e, r - off);
                        b.UpInto(lidF, up, r + off, 2 * r - e);
                    }
                    else
                    {
                        b.UpInto(lidF, up, r - 1.0, r + 1.0);
                        b.DownInto(skid, up, e, r - off);
                        b.DownInto(skid, up, r + off, 2 * r - e);
                    }
                    // The label band between the ribs, the flame left standing in it.
                    double ly0 = ry1 + 1.0, ly1 = ry2 - 1.0;
                    if (ly1 - ly0 > 2)
                    {
                        var band = new PathsD { Geo.Rect(0.4, ly0, 2 * r - 0.4, ly1) };
                        double fw = Math.Min(2 * r * 0.22, (ly1 - ly0) * 0.5);
                        up.Etched.AddRange(Geo.Subtract(band, Geo.Union([.. Profiles.Flames(r * 0.5, ly0 + (ly1 - ly0) * 0.2, fw, (ly1 - ly0) * 0.6), .. Profiles.Flames(r * 1.5, ly0 + (ly1 - ly0) * 0.2, fw, (ly1 - ly0) * 0.6)])));
                    }
                }
            }
        }
        b.Finish();
        m.Note(b.Glue ? "Glue each drum's rings up on the skid, chime first, lid last." : "Slide each drum's two profiles together, push them into the skid and push the lid on.");
    }

    // ---- the reactor core ----

    /// <summary>
    /// A reactor core in its cage: an eight-sided base (a hazard-striped plate under a step), a glowing core
    /// of two profiles halved together with collars standing out round it, four struts on the diagonals, and
    /// an eight-sided cap with a fan housing on top.
    /// </summary>
    static void ShipReactor(Maker m)
    {
        var it = m.Item;
        double T = m.T, F = m.Fit, H = it.HeightMm, k = H / 26, sw = T + F;
        double R = Math.Min(it.WidthMm, it.DepthMm) / 2, cx = it.WidthMm / 2, cz = it.DepthMm / 2;
        double cw = Math.Round(Math.Max(sw + 4.4, R * 0.8), 1), ri = Math.Max(cw / 2 + 0.6, Math.Sqrt(2) * (T / 2 + 0.3) + T / 2 + 0.2);
        double Rc = R - 0.8, ro = Rc - 1.4;
        if (ro - ri < 2.0) { m.Bad($"The reactor is too narrow: make it at least {Math.Ceiling(2 * (ri + 2.0 + 2.2)):0} mm across."); return; }
        bool glue = !m.PushFit;
        double yb = glue ? 2 * T : T, yc = glue ? H - T : H;
        if (yc - T - yb < 12) { m.Bad("The reactor is too low: make it at least " + Math.Ceiling(H + 12 - (yc - T - yb)) + " mm high."); return; }
        var b = Metal(m, k);
        double Rb = b.Glue ? R - 1.2 : R;
        var base_ = new Flat { Name = "Base", Y = yb, Texture = "none" };
        base_.Outline.Add(Octagon(cx, cz, Rb));
        base_.Lines.Add(Geo.Loop(Octagon(cx, cz, Rb - 0.6)));
        b.Flats.Add(base_);
        var ring = new PathsD { Octagon(cx, cz, R - 0.4) };
        ring = Geo.Subtract(ring, [Octagon(cx, cz, R - 1.6)]);
        if (b.Glue)
        {
            var plate = new Flat { Name = "Base plate", Y = T, Texture = "none", Glued = true };
            plate.Outline.Add(Octagon(cx, cz, R));
            plate.Etched.AddRange(Geo.Intersect(Hazard(cx - R, -cz - R, cx + R, -cz + R, 0.9), ring));
            b.Flats.Add(plate);
        }
        else base_.Etched.AddRange(Geo.Intersect(Hazard(cx - R, -cz - R, cx + R, -cz + R, 0.9), ring));
        for (int i = 0; i < 8; i++)
        {
            double a = Math.PI / 8 + i * Math.PI / 4, rv = (Rb - 1.0) / Math.Cos(Math.PI / 8);
            base_.Lines.Add(WoodFace.Dot(cx + Math.Cos(a) * rv, -(cz + Math.Sin(a) * rv), 0.25));
        }
        var cap = new Flat { Name = "Cap", Y = yc, Texture = "none" };
        cap.Outline.Add(Octagon(cx, cz, Rc));
        cap.Lines.Add(Geo.Loop(Octagon(cx, cz, Rc - 0.6)));
        for (int i = 0; i < 8; i++)
        {
            double a = i * Math.PI / 4 + Math.PI / 8, c0 = Math.Cos(a), s0 = Math.Sin(a);
            cap.Etched.Add(Geo.Strip(new(cx + c0 * (Rc * 0.62), -(cz + s0 * (Rc * 0.62))), new(cx + c0 * (Rc - 1.2), -(cz + s0 * (Rc - 1.2))), 0.5));
        }
        b.Flats.Add(cap);
        if (b.Glue)
        {
            var dome = new Flat { Name = "Fan housing", Y = H, Texture = "none", Glued = true };
            double rd = Rc * 0.56;
            dome.Outline.Add(Octagon(cx, cz, rd));
            dome.Etched.Add(Geo.Circle(cx, -cz, rd * 0.8, 0.01));
            dome.Lines.Add(Geo.Loop(Geo.Circle(cx, -cz, rd * 0.25, 0.01)));
            for (int i = 0; i < 5; i++) { double a0 = i * Math.PI * 2 / 5; dome.Lines.Add([.. Geo.Arc(cx, -cz, rd * 0.55, a0, a0 + 0.8)]); }
            b.Flats.Add(dome);
        }
        // The core: two profiles halved together, collars standing out, the glow between them.
        double y0 = yb, y1 = yc - T, ym = (y0 + y1) / 2;
        int rings = Math.Max(2, (int)Math.Round((y1 - y0) / (5.5 * k)));
        var collars = new List<double>();
        for (int i = 0; i <= rings; i++) collars.Add(y0 + 1.0 + (y1 - y0 - 2.0) * i / rings);
        for (int p = 0; p < 2; p++)
        {
            var dir = p == 0 ? V3.UnitX : V3.UnitZ;
            var up = b.Upright(p == 0 ? "Core" : "Core (crossing)", new V3(cx, 0, cz) - dir * (cw / 2), dir, cw, new V3(-dir.Z, 0, dir.X));
            up.Plain = true;
            up.Outline.Add(Geo.Rect(0.4, y0, cw - 0.4, y1));
            foreach (var y in collars) up.Outline.Add(Geo.Rect(-0.5, y - 0.8, cw + 0.5, y + 0.8));
            up.Holes.Add(p == 0 ? Geo.Rect(cw / 2 - sw / 2, ym, cw / 2 + sw / 2, H + 5) : Geo.Rect(cw / 2 - sw / 2, -5, cw / 2 + sw / 2, ym));
            if (p == 0) b.DownInto(base_, up, cw / 2 - 1.1, cw / 2 + 1.1);
            else b.UpInto(cap, up, cw / 2 - 1.1, cw / 2 + 1.1);
            var glow = new PathsD();
            for (int i = 0; i < rings; i++) glow.Add(Geo.Rect(0.9, collars[i] + 1.2, cw - 0.9, collars[i + 1] - 1.2));
            up.Etched.AddRange(glow);
            for (int i = 0; i < rings; i++)
                for (double x = 1.6; x < cw - 1.2; x += 1.2)
                    up.Lines.Add([new(x, collars[i] + 1.6), new(x, collars[i + 1] - 1.6)]);
            foreach (var y in collars) { up.Lines.Add(WoodFace.Dot(0.2, y, 0.2)); up.Lines.Add(WoodFace.Dot(cw - 0.2, y, 0.2)); }
        }
        // The struts on the diagonals.
        for (int i = 0; i < 4; i++)
        {
            double a = Math.PI / 4 + i * Math.PI / 2;
            var dir = new V3(Math.Cos(a), 0, Math.Sin(a));
            double ls = ro - ri;
            var st = b.Upright("Strut", new V3(cx, 0, cz) + dir * ri, dir, ls, new V3(-dir.Z, 0, dir.X));
            st.Plain = true;
            st.Outline.Add(Geo.Rect(0, y0, ls, y1));
            st.Outline.Add(Fronts.Chamfered(0, y0, ls + 1.0, y0 + 2.4, 0.5));
            st.Outline.Add(Fronts.Chamfered(0, y1 - 2.4, ls + 1.0, y1, 0.5));
            b.DownInto(base_, st, 0.3, ls - 0.3);
            b.UpInto(cap, st, 0.3, ls - 0.3);
            st.Etched.Add(Geo.Rect(ls * 0.5 - 0.35, y0 + 3.2, ls * 0.5 + 0.35, y1 - 3.2));
            for (double y = y0 + 4; y < y1 - 3.5; y += 3.0) st.Lines.Add(WoodFace.Dot(ls - 0.5, y, 0.18));
        }
        b.Finish();
        m.Note("Push the core into the base, slide its crossing half down over it, push the four struts into the base and the cap onto everything" + (b.Glue ? "; glue the base onto its plate and the fan housing on the cap." : "."));
    }

}
