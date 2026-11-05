using Clipper2Lib;
using PhotonCanvas.Addon.Terrain;

namespace PhotonCanvas.Addon.Furniture;

/// <summary>What a ship fitting is: a cryo pod, a med bay bed under a scanner, a mess table with its stools fixed to the deck, a server rack, crew bunks, a cockpit console, a rifle rack, fuel drums or a reactor core.</summary>
public enum ShipStyle { CryoPod, MedBed, MessTable, ServerRack, Bunks, Console, RifleRack, FuelDrums, Reactor }

/// <summary>
/// Ship fittings, built like the town pieces (uprights tabbed into flat pieces) so they stand up in the
/// same way: a cryo pod, a med bay bed with its scanner arch and monitor, a mess table with stools on
/// arms from the deck plate, and a rack of servers.
/// </summary>
public static partial class TownWalls
{
    public static void Ship(Maker m)
    {
        switch (m.Item.Ship)
        {
            case ShipStyle.MedBed: MedBed(m); break;
            case ShipStyle.MessTable: MessTable(m); break;
            case ShipStyle.ServerRack: ServerRack(m); break;
            case ShipStyle.Bunks: ShipBunks(m); break;
            case ShipStyle.Console: ShipConsole(m); break;
            case ShipStyle.RifleRack: RifleRack(m); break;
            case ShipStyle.FuelDrums: FuelDrums(m); break;
            case ShipStyle.Reactor: ShipReactor(m); break;
            default: CryoPod(m); break;
        }
    }

    /// <summary>The size each fitting starts at (28 mm figures), used when the kind is picked.</summary>
    public static FurnitureItem ShipSized(FurnitureItem it, ShipStyle s) => s switch
    {
        ShipStyle.MedBed => it with { Ship = s, WidthMm = 34, DepthMm = 22, HeightMm = 26 },
        ShipStyle.MessTable => it with { Ship = s, WidthMm = 36, DepthMm = 24, HeightMm = 13 },
        ShipStyle.ServerRack => it with { Ship = s, WidthMm = 20, DepthMm = 11, HeightMm = 32 },
        ShipStyle.Bunks => it with { Ship = s, WidthMm = 34, DepthMm = 15, HeightMm = 28 },
        ShipStyle.Console => it with { Ship = s, WidthMm = 30, DepthMm = 18, HeightMm = 20 },
        ShipStyle.RifleRack => it with { Ship = s, WidthMm = 32, DepthMm = 9, HeightMm = 26 },
        ShipStyle.FuelDrums => it with { Ship = s, WidthMm = 24, DepthMm = 22, HeightMm = 14 },
        ShipStyle.Reactor => it with { Ship = s, WidthMm = 18, DepthMm = 18, HeightMm = 26 },
        _ => it with { Ship = s, WidthMm = 22, DepthMm = 15, HeightMm = 32 },
    };

    /// <summary>A build in the ship's metal plates, its detail sized to <paramref name="k"/>.</summary>
    static Build Metal(Maker m, double k) =>
        new(m, LookOf(TerrainThemes.SciFi) with { Face = ThemeTextures.SciFiPanels, A = 8, B = 6, G = 0.4 }, k);

    /// <summary>Diagonal hazard stripes etched across the box (x0, y0)–(x1, y1), clipped to it.</summary>
    static PathsD Hazard(double x0, double y0, double x1, double y1, double pitch = 1.6)
    {
        var stripes = new PathsD();
        double h = y1 - y0;
        for (double x = x0 - h; x < x1; x += pitch * 2)
            stripes.Add([new(x, y0), new(x + pitch, y0), new(x + pitch + h, y1), new(x + h, y1)]);
        return Geo.Intersect(stripes, [Geo.Rect(x0, y0, x1, y1)]);
    }

    /// <summary>u along an upright made along +z (from <paramref name="a"/>) for the room's z.</summary>
    static double UZ(Up up, V3 a, double z) => Build.UOf(up, a, V3.UnitZ, z - a.Z);

    // ---- the cryo pod ----

    /// <summary>
    /// A cryo pod standing on a hazard-striped base: two sides with light strips and vents, the sleeper
    /// etched on the back panel inside, a canopy frame with a tall rounded window and a control panel
    /// under it, a hood bulging over the front with a fan on top, and coolant tubes up the sides.
    /// </summary>
    static void CryoPod(Maker m)
    {
        var it = m.Item;
        double T = m.T, W = it.WidthMm, D = it.DepthMm, H = it.HeightMm, k = H / 32;
        if (W < 12 + 2 * T || D < 4 * T + 5) { m.Bad($"The cryo pod is too small: make it at least {12 + 2 * T:0.#} wide and {4 * T + 5:0.#} mm deep."); return; }
        if (H < 20) { m.Bad("The cryo pod is too low: make it at least 20 mm high."); return; }
        var b = Metal(m, k);
        double x0 = b.Glue ? T + 0.6 : 1.6, x1 = W - x0, zs0 = 1.4, zs1 = D - 0.8, ytop = H - Math.Max(2.4, 3 * k);
        double bulge = Math.Min(2.2 * k, D * 0.15);
        // The base and the hood: rounded out at the front.
        PathD Front(double xa, double xb, double z0, double z1, double out_)
        {
            var p = new PathD { new(xa, -z0) };
            for (int i = 0; i <= 12; i++)
            {
                double t = i / 12.0, x = xa + (xb - xa) * t;
                p.Add(new PointD(x, -(z1 + out_ * Math.Sin(t * Math.PI))));
            }
            p.Add(new PointD(xb, -z0));
            return p;
        }
        var bs = new Flat { Name = "Base", Y = T, Texture = "none" };
        bs.Outline.Add(Front(0, W, 0, D - bulge, bulge));
        bs.Lines.Add(Geo.Loop(Front(0.6, W - 0.6, 0.6, D - bulge - 0.4, bulge * 0.8)));
        bs.Etched.AddRange(Hazard(1.4, -(D - bulge + 0.2), W - 1.4, -(D - bulge - 1.2)));
        b.Flats.Add(bs);
        var hood = new Flat { Name = "Hood", Y = ytop, Texture = "none" };
        hood.Outline.Add(Front(x0 - 1.2, x1 + 1.2, zs0 - 1.2, zs1 - bulge * 0.4, bulge));
        hood.Lines.Add(Geo.Loop(Front(x0 - 0.6, x1 + 0.6, zs0 - 0.6, zs1 - bulge * 0.4 - 0.6, bulge * 0.8)));
        b.Flats.Add(hood);
        // The sides.
        foreach (var (x, facing) in new[] { (x0 + T / 2, -V3.UnitX), (x1 - T / 2, V3.UnitX) })
        {
            var a = new V3(x, 0, zs0);
            var s = b.Upright("Pod side", a, V3.UnitZ, zs1 - zs0, facing);
            s.Plain = true;
            s.Outline.Add(Geo.Rect(0, T, s.Len, ytop - T));
            foreach (var (t0, t1) in b.Tabs(0, s.Len, 1.5)) { b.DownInto(bs, s, t0, t1); b.UpInto(hood, s, t0, t1); }
            // A light strip up the front, vents behind it, and panel seams.
            double uf0 = UZ(s, a, zs1 - 1.0), uf1 = UZ(s, a, zs1 - 2.2);
            s.Etched.Add(Geo.Rect(Math.Min(uf0, uf1), T + 2.4, Math.Max(uf0, uf1), ytop - T - 2.4));
            double ub = UZ(s, a, zs0 + (zs1 - zs0) * 0.35);
            for (double y = T + 3; y < ytop - T - 3; y += 1.4) s.Etched.Add(Geo.Rect(ub - 1.6, y, ub + 1.6, y + 0.6));
            s.Lines.Add([new(0.3, T + 1.4), new(s.Len - 0.3, T + 1.4)]);
            s.Lines.Add([new(0.3, ytop - T - 1.4), new(s.Len - 0.3, ytop - T - 1.4)]);
            s.Lines.Add(Geo.Loop(Fronts.Chamfered(Math.Min(UZ(s, a, zs0 + 1), UZ(s, a, zs1 - 3)), T + 2.4, Math.Max(UZ(s, a, zs0 + 1), UZ(s, a, zs1 - 3)), ytop - T - 2.4, 1.0)));
            for (int i = 0; i < 4; i++) s.Lines.Add(WoodFace.Dot(i % 2 == 0 ? 0.8 : s.Len - 0.8, i < 2 ? T + 0.8 : ytop - T - 0.8, 0.2));
        }
        double xi0 = x0 + T + 0.02, xi1 = x1 - T - 0.02;
        // The back, with the sleeper on it.
        double zb = zs0 + T + 0.6 + T / 2;
        var back = b.Upright("Pod back", new V3(xi0, 0, zb), V3.UnitX, xi1 - xi0, V3.UnitZ);
        back.Plain = true;
        back.Outline.Add(Geo.Rect(0, T, back.Len, ytop - T));
        foreach (var (t0, t1) in b.Tabs(0, back.Len, 1.5)) { b.DownInto(bs, back, t0, t1); b.UpInto(hood, back, t0, t1); }
        double cx = back.Len / 2, yw0 = T + Math.Max(5, 6 * k), yw1 = ytop - T - Math.Max(1.6, 2 * k);
        Sleeper(cx, yw0 + 0.6, yw1 - 0.6, Math.Min(back.Len * 0.36, (yw1 - yw0) * 0.2), back.Etched, back.Lines, m.Rnd);
        // The canopy: a frame with a tall rounded window, the control panel under it.
        double zc = zs1 - T / 2 - 0.6;
        var can = b.Upright("Canopy", new V3(xi0, 0, zc), V3.UnitX, xi1 - xi0, V3.UnitZ);
        can.Plain = true;
        double L = can.Len, side = Math.Max(1.6, L * 0.12);
        can.Outline.Add(Geo.Rect(0, T, L, ytop - T));
        var win = WoodFace.RoundRect(side, yw0, L - side, yw1, Math.Min((L - 2 * side) / 2 - 0.01, 3 * k));
        can.Holes.Add(win);
        can.Lines.Add(Geo.Loop(Geo.Offset([win], 0.6).FirstOrDefault() ?? win));
        foreach (var (t0, t1) in b.Tabs(0, L, 1.5)) { b.DownInto(bs, can, t0, t1); b.UpInto(hood, can, t0, t1); }
        // The control panel: a readout, buttons and a row of lights.
        double py0 = T + 0.8, py1 = yw0 - 1.2;
        if (py1 - py0 > 2.2)
        {
            can.Etched.Add(Geo.Rect(side, py1 - Math.Min(2.2, (py1 - py0) * 0.45), L * 0.55, py1));
            for (int i = 0; i < 3; i++) can.Etched.Add(Geo.Circle(L * 0.66 + i * 1.2, py1 - 0.9, 0.35, 0.01));
            for (double x = side; x < L - side; x += 1.0) can.Lines.Add(WoodFace.Dot(x + 0.3, py0 + 0.6, 0.15));
        }
        foreach (var x in new[] { side / 2, L - side / 2 })
            for (double y = yw0 + 1; y < yw1 - 1; y += 2.4) can.Lines.Add([new(x - 0.4, y), new(x + 0.4, y)]);
        if (b.Glue)
        {
            // A fan housing on the hood and coolant tubes up the sides.
            var fan = new Flat { Name = "Fan housing", Y = ytop + T, Texture = "none", Glued = true };
            double fr = Math.Min((x1 - x0) * 0.3, (zs1 - zs0) * 0.32), fx = W / 2, fz = (zs0 + zs1) / 2 - 0.4;
            fan.Outline.Add(Fronts.Chamfered(fx - fr - 0.8, -(fz + fr + 0.8), fx + fr + 0.8, -(fz - fr - 0.8), 1.0));
            fan.Etched.Add(Geo.Circle(fx, -fz, fr, 0.01));
            fan.Etched.Add(Geo.Circle(fx, -fz, fr * 0.25, 0.01));
            for (int i = 0; i < 6; i++)
            {
                double a0 = i * Math.PI / 3;
                fan.Lines.Add([.. Geo.Arc(fx, -fz, fr * 0.65, a0, a0 + 0.9)]);
            }
            b.Flats.Add(fan);
            foreach (var (x, facing) in new[] { (x0 - T / 2, -V3.UnitX), (x1 + T / 2, V3.UnitX) })
            {
                var a = new V3(x, 0, zs1 - 3.4);
                var tube = b.Upright("Coolant tube", a, V3.UnitZ, 1.8, facing);
                tube.Glued = tube.Plain = true;
                tube.Outline.Add(Geo.Rect(0, T + 1.2, 1.8, ytop - T - 0.1));
                for (double y = T + 2.4; y < ytop - T - 1.0; y += 2.6) tube.Etched.Add(Geo.Rect(0, y, 1.8, y + 0.5));
            }
        }
        b.Finish();
        m.Note("Push the sides, back and canopy down into the base and the hood onto their tabs" + (b.Glue ? "; glue the fan housing on the hood and the coolant tubes up the sides." : ".") + " For glass, glue clear plastic behind the window before the canopy goes in.");
    }

    /// <summary>A sleeper lying in the pod, seen from the front: head, shoulders, arms crossed, frost round the edge.</summary>
    static void Sleeper(double cx, double y0, double y1, double w, PathsD etched, List<PathD> lines, Random rnd)
    {
        double h = y1 - y0;
        if (h < 6 || w < 1.5) return;
        double hr = Math.Min(w * 0.3, h * 0.08), hy = y1 - hr * 1.2 - h * 0.03;
        // A frosted glow round the sleeper, then the sleeper cut out of it in a lighter outline.
        var glow = WoodFace.RoundRect(cx - w * 0.95, y0, cx + w * 0.95, y1, Math.Min(w * 0.9, 2.4));
        var head = Rugs.Ellipse(cx, hy, hr, hr * 1.2);
        double sy = hy - hr * 1.2 - 0.3;
        var body = new PathD
        {
            new(cx - w * 0.22, sy), new(cx - w * 0.5, sy - hr * 0.5), new(cx - w * 0.52, y0 + h * 0.42),
            new(cx - w * 0.3, y0 + h * 0.05), new(cx + w * 0.3, y0 + h * 0.05), new(cx + w * 0.52, y0 + h * 0.42),
            new(cx + w * 0.5, sy - hr * 0.5), new(cx + w * 0.22, sy),
        };
        var figure = Geo.Union([head, body]);
        // The arms crossed over the chest and the legs apart: thin lines left in the glow's colour.
        double ay = sy - h * 0.2;
        var gaps = new PathsD
        {
            Geo.Strip(new(cx - w * 0.5, ay + h * 0.08), new(cx + w * 0.34, ay - h * 0.03), 0.45),
            Geo.Strip(new(cx + w * 0.5, ay + h * 0.08), new(cx - w * 0.34, ay - h * 0.03), 0.45),
            Geo.Strip(new(cx, y0 + h * 0.04), new(cx, y0 + h * 0.38), 0.45),
        };
        etched.AddRange(Geo.Subtract([glow], Geo.Offset(figure, 0.45)));
        etched.AddRange(Geo.Subtract(figure, Geo.Union(gaps)));
        // Frost creeping in from the corners.
        for (int i = 0; i < 12; i++)
        {
            double x = cx + (rnd.NextDouble() < 0.5 ? -1 : 1) * w * (0.6 + rnd.NextDouble() * 0.3), y = y0 + rnd.NextDouble() * h;
            double a = rnd.NextDouble() * Math.PI;
            lines.Add([new(x - Math.Cos(a) * 0.7, y - Math.Sin(a) * 0.7), new(x + Math.Cos(a) * 0.7, y + Math.Sin(a) * 0.7)]);
            lines.Add([new(x - Math.Cos(a + 1.2) * 0.5, y - Math.Sin(a + 1.2) * 0.5), new(x + Math.Cos(a + 1.2) * 0.5, y + Math.Sin(a + 1.2) * 0.5)]);
        }
    }

    // ---- the med bay bed ----

    /// <summary>
    /// A med bay bed on a deck plate: the bed on a panelled pedestal with a red cross, a mattress and pillow,
    /// a scanner arch standing over it with a glowing inner edge and a lens at its top, and a monitor on a post
    /// at the head showing a heartbeat.
    /// </summary>
    static void MedBed(Maker m)
    {
        var it = m.Item;
        double T = m.T, F = m.Fit, W = it.WidthMm, D = it.DepthMm, H = it.HeightMm, k = H / 26;
        double leg = Math.Max(2.4, 2.6 * k), bw = Math.Round(Math.Min(13 * k, D - 2 * (leg + 1.2)), 1);
        if (W < 24 || bw < 8) { m.Bad($"The med bay bed is too small: make it at least 24 long and {8 + 2 * (leg + 1.2):0.#} mm wide."); return; }
        double yd = Math.Round(Math.Max(7, 9 * k), 1);
        if (H < yd + 12) { m.Bad("The med bay bed is too low for its scanner: make it taller."); return; }
        var b = Metal(m, k);
        var plate = new Flat { Name = "Deck plate", Y = T, Texture = "none" };
        plate.Outline.Add(Fronts.Chamfered(0, -D, W, 0, 1.6));
        plate.Lines.Add(Geo.Loop(Fronts.Chamfered(0.6, -D + 0.6, W - 0.6, -0.6, 1.2)));
        foreach (var (xa, xb) in new[] { (0.8, 4.0), (W - 4.0, W - 0.8) }) plate.Etched.AddRange(Hazard(xa, -D + 0.8, xb, -D + 2.0));
        b.Flats.Add(plate);
        double zb0 = D / 2 - bw / 2, zb1 = D / 2 + bw / 2, xb0 = 1.2, xb1 = W - 1.2;
        // The bed and its pedestal.
        var deck = new Flat { Name = "Bed", Y = yd, Texture = "none" };
        deck.Outline.Add(WoodFace.RoundRect(xb0, -zb1, xb1, -zb0, 1.0));
        deck.Lines.Add(Geo.Loop(WoodFace.RoundRect(xb0 + 0.8, -zb1 + 0.8, xb1 - 0.8, -zb0 - 0.8, 0.6)));
        b.Flats.Add(deck);
        double pz0 = zb0 + 1.6, pz1 = zb1 - 1.6, px0 = xb0 + 3, px1 = xb1 - 3;
        var pf = b.Upright("Pedestal", new V3(px0, 0, pz1 - T / 2), V3.UnitX, px1 - px0, V3.UnitZ);
        var pb = b.Upright("Pedestal", new V3(px0, 0, pz0 + T / 2), V3.UnitX, px1 - px0, -V3.UnitZ);
        var pl = b.Upright("Pedestal end", new V3(px0 + T / 2, 0, pz0 + T), V3.UnitZ, pz1 - pz0 - 2 * T, -V3.UnitX);
        var pr = b.Upright("Pedestal end", new V3(px1 - T / 2, 0, pz0 + T), V3.UnitZ, pz1 - pz0 - 2 * T, V3.UnitX);
        foreach (var p in new[] { pf, pb, pl, pr })
        {
            p.Plain = true;
            p.Outline.Add(Geo.Rect(0, T, p.Len, yd - T));
            double e = p == pl || p == pr ? 0.8 : T + 1.2;
            foreach (var (t0, t1) in b.Tabs(0, p.Len, e)) { b.DownInto(plate, p, t0, t1); b.UpInto(deck, p, t0, t1); }
            if (p == pf || p == pb)
            {
                // Panels, and a red cross in the middle one.
                int n = 3;
                double pw = (p.Len - 1.2) / n;
                for (int i = 0; i < n; i++)
                {
                    double a = 0.6 + i * pw + 0.4, c = 0.6 + (i + 1) * pw - 0.4;
                    p.Lines.Add(Geo.Loop(Fronts.Chamfered(a, T + 0.8, c, yd - T - 0.8, 0.6)));
                    if (i == 1)
                    {
                        double cc = (a + c) / 2, cy = (T + yd - T) / 2, r = Math.Min(c - a, yd - 2 * T - 1.6) * 0.32, t = r * 0.36;
                        p.Etched.Add(Geo.Rect(cc - r, cy - t, cc + r, cy + t));
                        p.Etched.Add(Geo.Rect(cc - t, cy - r, cc + t, cy + r));
                    }
                }
            }
        }
        // The scanner arch over the middle of the bed.
        double xa_ = W * 0.52, za0 = zb0 - leg - 0.6, za1 = zb1 + leg + 0.6;
        var arch = b.Upright("Scanner arch", new V3(xa_, 0, za0), V3.UnitZ, za1 - za0, V3.UnitX);
        arch.Plain = true;
        double aL = arch.Len, yi = yd + T + Math.Max(5, 6 * k), ya = Math.Min(H - 0.01, yi + Math.Max(3, 3.4 * k));
        double ro = Math.Min(aL / 2 - 0.01, 5 * k);
        arch.Outline.Add(WoodFace.RoundRect(0, T, aL, ya, ro));
        arch.Outline.Add(Geo.Rect(0, T, aL, ya - ro));
        double hu0 = UZ(arch, new V3(xa_, 0, za0), zb0 - 0.4), hu1 = UZ(arch, new V3(xa_, 0, za0), zb1 + 0.4);
        var hole = WoodFace.RoundRect(Math.Min(hu0, hu1), -ro - 1, Math.Max(hu0, hu1), yi, Math.Min(ro - 1, (Math.Abs(hu1 - hu0)) / 2 - 0.01));
        arch.Holes.Add(hole);
        arch.Etched.AddRange(Geo.Subtract(Geo.Offset([hole], 0.7), [hole]));
        arch.Lines.Add(Geo.Loop(WoodFace.RoundRect(0.5, T + 0.5, aL - 0.5, ya - 0.5, Math.Max(0.4, ro - 0.5))));
        // The lens at the top.
        arch.Etched.Add(Geo.Circle(aL / 2, (yi + ya) / 2 + 0.2, Math.Min(1.0, (ya - yi) * 0.3), 0.01));
        foreach (var (u0, u1) in new[] { (0.3, Math.Min(hu0, hu1) - 0.3), (Math.Max(hu0, hu1) + 0.3, aL - 0.3) })
            b.DownInto(plate, arch, u0, u1);
        // The monitor on a post at the head, on the near side.
        double zm = Math.Min(D - 1.2 - T / 2, zb1 + 1.2 + T / 2), mw = Math.Min(9 * k, W * 0.28), mx0 = W - 1.6 - mw;
        if (zm - T / 2 > zb1 + 0.2)
        {
            var mon = b.Upright("Monitor", new V3(mx0, 0, zm), V3.UnitX, mw, V3.UnitZ);
            mon.Plain = true;
            double pw = Math.Max(1.6, 1.8 * k), px = mw - pw - 0.6, sy0 = yd + T + 2, sy1 = Math.Min(ya, sy0 + Math.Max(5, 6 * k));
            mon.Outline.Add(Geo.Rect(px, T, px + pw, sy0 + 0.02));
            mon.Outline.Add(Geo.Rect(px - 0.8, T, px + pw + 0.8, T + 1.2));
            mon.Outline.Add(WoodFace.RoundRect(0, sy0, mw, sy1, 0.6));
            mon.Etched.Add(Geo.Rect(0.7, sy0 + 0.7, mw - 0.7, sy1 - 0.7));
            // A heartbeat across the screen (scored over the etched glow).
            double my = (sy0 + sy1) / 2, sx = 1.0, ex = mw - 1.0, dx = (ex - sx) / 10;
            mon.Lines.Add([new(sx, my), new(sx + 3 * dx, my), new(sx + 3.6 * dx, my + 1.4), new(sx + 4.2 * dx, my - 1.6), new(sx + 4.8 * dx, my + 0.4), new(sx + 5.4 * dx, my), new(ex, my)]);
            b.DownInto(plate, mon, px + 0.2, px + pw - 0.2);
        }
        if (b.Glue)
        {
            var mat = new Flat { Name = "Mattress", Y = yd + T, Texture = "none", Glued = true };
            mat.Outline.Add(WoodFace.RoundRect(xb0 + 0.6, -zb1 + 0.6, xb1 - 0.6, -zb0 - 0.6, 1.0));
            for (double x = xb0 + 4; x < xb1 - 2; x += 4) mat.Lines.Add([new(x, -zb1 + 1.2), new(x, -zb0 - 1.2)]);
            b.Flats.Add(mat);
            var pil = new Flat { Name = "Pillow", Y = yd + 2 * T, Texture = "none", Glued = true };
            double pl0 = xb1 - 1.4 - Math.Min(7 * k, (xb1 - xb0) * 0.22);
            pil.Outline.Add(WoodFace.RoundRect(pl0, -zb1 + 1.6, xb1 - 1.4, -zb0 - 1.6, 1.2));
            pil.Lines.Add([new((pl0 + xb1 - 1.4) / 2, -zb1 + 2.4), new((pl0 + xb1 - 1.4) / 2, -zb0 - 2.4)]);
            b.Flats.Add(pil);
        }
        b.Finish();
        m.Note("Push the pedestal into the deck plate and the bed onto it, then stand the scanner arch over the bed and the monitor at its head" + (b.Glue ? "; glue on the mattress and pillow." : "."));
    }

    // ---- the mess table ----

    /// <summary>
    /// A mess table and its stools on one deck plate: the top on a spine and two end legs, the stools round it
    /// each on a post from an arm of the plate, with padded seats.
    /// </summary>
    static void MessTable(Maker m)
    {
        var it = m.Item;
        double T = m.T, W = it.WidthMm, D = it.DepthMm, H = it.HeightMm, k = H / 13;
        double tw = Math.Round(Math.Min(D * 0.42, 11 * k), 1), rs = Math.Max(2.4, 2.6 * k), ys = Math.Round(H * 0.56 + T, 1);
        if (W < 20 || D < tw + 4 * rs + 2.8) { m.Bad($"The mess table is too small: make it at least 20 long and {Math.Ceiling(tw + 4 * rs + 2.8):0} mm wide."); return; }
        if (H < 9 || ys < 2 * T + 3) { m.Bad("The mess table is too low: make it at least 9 mm high."); return; }
        var b = Metal(m, k);
        double xt0 = 2.2, xt1 = W - 2.2, zt0 = D / 2 - tw / 2, zt1 = D / 2 + tw / 2;
        int per = Math.Max(2, (int)Math.Round((xt1 - xt0) / (9.5 * k)));
        var stools = new List<(double X, double Z)>();
        for (int i = 0; i < per; i++)
        {
            double x = xt0 + (xt1 - xt0) * (i + 0.5) / per;
            stools.Add((x, zt0 - 1.2 - rs));
            stools.Add((x, zt1 + 1.2 + rs));
        }
        // The deck plate: under the table and out to each stool on an arm.
        var plate = new Flat { Name = "Deck plate", Y = T, Texture = "none" };
        plate.Outline.Add(WoodFace.RoundRect(xt0 - 0.6, -(zt1 + 0.4), xt1 + 0.6, -(zt0 - 0.4), tw * 0.35));
        double fw = Math.Max(1.6, rs * 0.6), rp = Math.Max(rs * 0.85, fw + 1.2);
        foreach (var (x, z) in stools)
        {
            plate.Outline.Add(Geo.Circle(x, -z, rp, 0.02));
            plate.Outline.Add(Geo.Strip(new(x, -z), new(x, -D / 2), Math.Max(2.4, 2.4 * k)));
            plate.Lines.Add(Geo.Loop(Geo.Circle(x, -z, rp - 0.5, 0.02)));
        }
        plate.Lines.Add(Geo.Loop(WoodFace.RoundRect(xt0, -(zt1 - 0.2), xt1, -(zt0 + 0.2), tw * 0.3)));
        b.Flats.Add(plate);
        // The top: rounded ends, a lit strip down the middle.
        var top = new Flat { Name = "Table top", Y = H, Texture = "none" };
        top.Outline.Add(WoodFace.RoundRect(xt0 - 1.2, -(zt1 + 0.8), xt1 + 1.2, -(zt0 - 0.8), tw * 0.42));
        top.Lines.Add(Geo.Loop(WoodFace.RoundRect(xt0 - 0.4, -zt1, xt1 + 0.4, -zt0, tw * 0.36)));
        top.Etched.Add(Geo.Rect(xt0 + tw * 0.5, -D / 2 - 0.5, xt1 - tw * 0.5, -D / 2 + 0.5));
        b.Flats.Add(top);
        // The legs: two ends across and a spine between them.
        double le = tw * 0.5 - 1.2, xl0 = xt0 + 3, xl1 = xt1 - 3;
        foreach (var (x, f) in new[] { (xl0, -V3.UnitX), (xl1, V3.UnitX) })
        {
            var end = b.Upright("Table leg", new V3(x, 0, D / 2 - le), V3.UnitZ, 2 * le, f);
            end.Plain = true;
            double c = le, foot = le, neck = Math.Max(1.6, le * 0.45), dyf = Math.Max(1.4, (foot - neck) * 0.6);
            end.Outline.Add(new PathD { new(c - foot, T), new(c + foot, T), new(c + foot, T + 1.0), new(c + neck, T + (H - 2 * T) * 0.45), new(c + neck, H - T - 1.2 - dyf), new(c + foot, H - T - 1.2), new(c + foot, H - T), new(c - foot, H - T), new(c - foot, H - T - 1.2), new(c - neck, H - T - 1.2 - dyf), new(c - neck, T + (H - 2 * T) * 0.45), new(c - foot, T + 1.0) });
            end.Etched.Add(Geo.Rect(c - 0.4, T + 1.6, c + 0.4, H - T - 1.6));
            b.DownInto(plate, end, c - foot + 0.4, c - neck + 0.2);
            b.DownInto(plate, end, c + neck - 0.2, c + foot - 0.4);
            b.UpInto(top, end, c - foot + 0.6, c + foot - 0.6);
        }
        var spine = b.Upright("Table spine", new V3(xl0 + T / 2 + 0.6, 0, D / 2), V3.UnitX, xl1 - xl0 - T - 1.2, V3.UnitZ);
        spine.Plain = true;
        spine.Outline.Add(Geo.Rect(0, T, spine.Len, H - T));
        // Arches cut along it, lights between.
        int arches = Math.Max(1, (int)Math.Round(spine.Len / (8 * k)));
        double aw = spine.Len / arches;
        for (int i = 0; i < arches; i++)
        {
            double a = i * aw + (i == 0 ? 2.4 : 1.2), c = (i + 1) * aw - (i == arches - 1 ? 2.4 : 1.2);
            if (c - a > 2 && H - 2 * T > 6) spine.Holes.Add(WoodFace.RoundRect(a, -3, c, H - T - 2.4, Math.Min((c - a) / 2 - 0.01, 2)));
        }
        foreach (var (t0, t1) in b.Tabs(0, spine.Len, 2.5)) b.UpInto(top, spine, t0, t1);
        // Down into the plate from the piers between the arches.
        b.DownInto(plate, spine, 0.3, 2.1);
        b.DownInto(plate, spine, spine.Len - 2.1, spine.Len - 0.3);
        for (int i = 1; i < arches; i++) b.DownInto(plate, spine, i * aw - 0.8, i * aw + 0.8);
        // The stools: a post up from each arm, a seat on it.
        foreach (var (x, z) in stools)
        {
            var seat = new Flat { Name = "Stool seat", Y = ys, Texture = "none" };
            seat.Outline.Add(Geo.Circle(x, -z, rs, 0.02));
            seat.Lines.Add(Geo.Loop(Geo.Circle(x, -z, rs - 0.5, 0.02)));
            seat.Lines.Add(Geo.Loop(Geo.Circle(x, -z, rs * 0.45, 0.02)));
            b.Flats.Add(seat);
            double pw = Math.Max(1.6, 1.8 * k), fl = Math.Min(rs - 0.5, pw / 2 + 1.0);
            var post = b.Upright("Stool post", new V3(x - fw, 0, z), V3.UnitX, 2 * fw, V3.UnitZ);
            post.Plain = true;
            // A pedestal: a flared foot, a slim column and a flared head under the seat.
            post.Outline.Add(new PathD
            {
                new(0, T), new(2 * fw, T), new(2 * fw, T + 0.9), new(fw + pw / 2, T + 2.2), new(fw + pw / 2, ys - T - 1.8),
                new(fw + fl, ys - T - 0.6), new(fw + fl, ys - T), new(fw - fl, ys - T), new(fw - fl, ys - T - 0.6),
                new(fw - pw / 2, ys - T - 1.8), new(fw - pw / 2, T + 2.2), new(0, T + 0.9),
            });
            post.Lines.Add([new(fw, T + 2.4), new(fw, ys - T - 2.0)]);
            b.DownInto(plate, post, 0.3, 2 * fw - 0.3);
            b.UpInto(seat, post, fw - pw / 2 + 0.2, fw + pw / 2 - 0.2);
        }
        b.Finish();
        m.Note("Push the table's legs and spine into the deck plate and the top onto them, then each stool's post into its arm and its seat on top.");
    }

    // ---- the server rack ----

    /// <summary>
    /// A rack of servers: two vented sides, a back, a plinth and a top with fans, shelves pushed through the
    /// sides, and on each shelf a unit's faceplate standing in a slot: drive bays with lights, a switch with
    /// rows of ports, or a readout.
    /// </summary>
    static void ServerRack(Maker m)
    {
        var it = m.Item;
        double T = m.T, W = it.WidthMm, D = it.DepthMm, H = it.HeightMm, k = H / 32;
        if (W < 4 * T + 8 || D < 3 * T + 4) { m.Bad($"The server rack is too small: make it at least {4 * T + 8:0.#} wide and {3 * T + 4:0.#} mm deep."); return; }
        if (H < 16) { m.Bad("The server rack is too low: make it at least 16 mm high."); return; }
        var b = Metal(m, k);
        double x0 = 1.2, x1 = W - 1.2, z0 = 1.2, z1 = D - 0.6, ytop = H, yb = T + Math.Max(1.6, 2 * k);
        var plinth = new Flat { Name = "Plinth", Y = yb, Texture = "none" };
        // Between the sides (its tabs go through them), out to the front.
        plinth.Outline.Add(Build.XZRect(x0 + T + 0.02, z0, x1 - T - 0.02, D));
        plinth.Etched.AddRange(Hazard(x0 + T + 0.4, -D + 0.4, x1 - T - 0.4, -D + 1.4, 1.0));
        b.Flats.Add(plinth);
        var top = new Flat { Name = "Top", Y = ytop, Texture = "none" };
        top.Outline.Add(Build.XZRect(0, 0, W, D));
        double fr = Math.Min((W - 3) / 4.4, (D - 2) / 2.4);
        foreach (var fx in new[] { W * 0.28, W * 0.72 })
        {
            top.Etched.Add(Geo.Circle(fx, -D / 2, fr, 0.01));
            top.Lines.Add(Geo.Loop(Geo.Circle(fx, -D / 2, fr * 0.3, 0.01)));
            for (int i = 0; i < 5; i++) { double a = i * Math.PI * 2 / 5; top.Lines.Add([.. Geo.Arc(fx, -D / 2, fr * 0.65, a, a + 0.8)]); }
        }
        b.Flats.Add(top);
        var sides = new List<Up>();
        foreach (var (x, f) in new[] { (x0 + T / 2, -V3.UnitX), (x1 - T / 2, V3.UnitX) })
        {
            var a = new V3(x, 0, z0);
            var s = b.Upright("Rack side", a, V3.UnitZ, z1 - z0, f);
            s.Plain = true;
            s.Outline.Add(Geo.Rect(0, 0, s.Len, ytop - T));
            // Feet at the corners, the plinth's tabs between.
            s.Holes.Add(Geo.Rect(1.4, -1, s.Len - 1.4, yb - T));
            foreach (var (t0, t1) in b.Tabs(1.4, s.Len - 1.4, 0.6)) b.FlatInto(plinth, s, t0, t1);
            foreach (var (t0, t1) in b.Tabs(0, s.Len, 1.2)) b.UpInto(top, s, t0, t1);
            // Vents and a cable channel.
            for (double y = yb + 2; y < ytop - T - 2; y += 1.6) s.Etched.Add(Geo.Rect(s.Len * 0.3, y, s.Len * 0.7, y + 0.6));
            s.Lines.Add(Geo.Loop(Geo.Rect(0.6, yb + 0.8, s.Len - 0.6, ytop - T - 0.8)));
            sides.Add(s);
        }
        double xi0 = x0 + T + 0.02, xi1 = x1 - T - 0.02;
        var back = b.Upright("Rack back", new V3(xi0, 0, z0 + T / 2), V3.UnitX, xi1 - xi0, -V3.UnitZ);
        back.Plain = true;
        back.Outline.Add(Geo.Rect(0, yb, back.Len, ytop - T));
        foreach (var (t0, t1) in b.Tabs(0, back.Len, 1.5)) { b.DownInto(plinth, back, t0, t1); b.UpInto(top, back, t0, t1); }
        // The shelves and the units on them.
        double pitch = Math.Max(4.4 + T, 5.6 * k), room = ytop - T - yb - 0.6;
        int rows = Math.Max(2, (int)Math.Floor(room / pitch));
        pitch = room / rows;
        double sz0 = z0 + T + 0.02, sz1 = z1 - 0.3;
        for (int r = 0; r < rows; r++)
        {
            double y = r == 0 ? yb : yb + r * pitch;
            Flat shelf;
            if (r == 0) shelf = plinth;
            else
            {
                shelf = new Flat { Name = "Shelf", Y = y, Texture = "none" };
                shelf.Outline.Add(Build.XZRect(xi0, sz0, xi1, sz1));
                b.Flats.Add(shelf);
                foreach (var s in sides) b.FlatInto(shelf, s, Math.Min(UZ(s, new V3(0, 0, z0), sz0 + 1.2), UZ(s, new V3(0, 0, z0), sz1 - 1.6)), Math.Max(UZ(s, new V3(0, 0, z0), sz0 + 1.2), UZ(s, new V3(0, 0, z0), sz1 - 1.6)));
            }
            // The unit's faceplate, standing in a slot near the shelf's front.
            double zf = sz1 - 1.2 - T / 2, top_ = (r == rows - 1 ? ytop - T : yb + (r + 1) * pitch - T) - 0.3;
            var face = b.Upright("Server", new V3(xi0 + 0.3, 0, zf), V3.UnitX, xi1 - xi0 - 0.6, V3.UnitZ);
            face.Plain = true;
            double L = face.Len;
            face.Outline.Add(Geo.Rect(0, y, L, top_));
            b.DownInto(shelf, face, L * 0.3, L * 0.7);
            ServerFace(face, r % 3, y + 0.5, top_ - 0.5, L);
        }
        b.Finish();
        m.Note("Push the sides onto the plinth's tabs and the back into it, slide the shelves through the sides, stand each server in its shelf and push the top on.");
    }

    /// <summary>A server's faceplate: drive bays with their lights, a switch with rows of ports, or a readout with bars.</summary>
    static void ServerFace(Up f, int kind, double y0, double y1, double L)
    {
        double h = y1 - y0;
        f.Lines.Add(Geo.Loop(Geo.Rect(0.4, y0, L - 0.4, y1)));
        foreach (var x in new[] { 1.0, L - 1.0 }) f.Lines.Add(WoodFace.Dot(x, (y0 + y1) / 2, 0.22));
        switch (kind)
        {
            case 0:
                for (double x = 2.0; x < L - 2.4; x += 1.6)
                {
                    f.Etched.Add(Geo.Rect(x, y0 + 0.6, x + 1.0, y1 - 1.4));
                    f.Lines.Add(WoodFace.Dot(x + 0.5, y1 - 0.7, 0.18));
                }
                break;
            case 1:
                for (double y = y0 + 0.9; y < y1 - 0.6; y += 1.2)
                    for (double x = 2.0; x < L - 2.4; x += 1.1) f.Etched.Add(Geo.Rect(x, y, x + 0.7, y + 0.6));
                break;
            default:
                f.Etched.Add(Geo.Rect(2.0, y0 + 0.6, L * 0.55, y1 - 0.6));
                for (double x = 2.6; x < L * 0.55 - 0.6; x += 1.0) f.Lines.Add([new(x, y0 + 1.0), new(x, y0 + 1.0 + (h - 2) * (0.3 + 0.6 * Math.Abs(Math.Sin(x * 1.7))))]);
                for (int i = 0; i < 3; i++) f.Etched.Add(Geo.Circle(L * 0.65 + i * 1.3, (y0 + y1) / 2, 0.4, 0.01));
                break;
        }
    }
}
