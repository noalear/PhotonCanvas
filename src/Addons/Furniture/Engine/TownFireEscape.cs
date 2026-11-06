using Clipper2Lib;
using PhotonCanvas.Addon.Terrain;

namespace PhotonCanvas.Addon.Furniture;

/// <summary>A city block's fire escape: landings up the outside of a building, stairs between them and a drop ladder to the street.</summary>
public static partial class TownWalls
{
    /// <summary>How high a fire escape's top rail stands over its top landing, for its depth (mm).</summary>
    public static double FireEscapeRail(double depthMm) => Math.Round(8 * depthMm / 16, 1);

    /// <summary>The fire escape for a building whose floors are <paramref name="floorMm"/> apart, with a landing at each of <paramref name="landings"/> floors above the street.</summary>
    public static FurnitureItem FireEscapeFor(FurnitureItem it, int landings, double floorMm) =>
        it with { Street = StreetStyle.FireEscape, Rows = Math.Clamp(landings, 1, 8), HeightMm = Math.Round(Math.Clamp(landings, 1, 8) * floorMm + FireEscapeRail(it.DepthMm), 1) };

    /// <summary>
    /// A fire escape standing against a wall (its back, at z = 0): a back frame of two posts and a band at each
    /// landing, open between so the windows behind show; the landings (etched grating, a hatch over each flight)
    /// slotted through it, each with a railing of bars along its front and ends; a stair flight between each
    /// landing and the next, turning back each time; and a drop ladder from the first landing to the street.
    /// The <see cref="FurnitureItem.Rows"/> landings are evenly spaced, the top one the rail's height under the top.
    /// </summary>
    static void FireEscape(Maker m)
    {
        var it = m.Item;
        double T = m.T, W = it.WidthMm, D = it.DepthMm, H = it.HeightMm, k = D / 16;
        int n = Math.Clamp(it.Rows, 1, 8);
        double rh = FireEscapeRail(D), P = (H - rh) / n;
        if (W < 30 || D < 3 * T + 8) { m.Bad($"The fire escape is too small: make it at least 30 wide and {3 * T + 8:0.#} mm deep."); return; }
        if (P < 3 * T + 14) { m.Bad($"The fire escape's landings are too close together: make it taller or give it fewer landings (they're {P:0.#} mm apart)."); return; }
        var b = new Build(m, LookOf(TerrainThemes.SciFi) with { Face = "Metal plates", A = 8, B = 6, G = 0.4 }, k);
        double pw = Math.Max(2.4, 2.6 * k), band = Math.Max(1.6, 1.8 * k), bar = Math.Max(1.0, 0.9 * k), gap = Math.Max(1.8, 2.4 * k);
        double zr = D - Lip - T / 2, xs0 = Lip + T + 0.8, xs1 = W - Lip - T - 0.8;
        double zf = Math.Round(T + (zr - T / 2 - T) * 0.45, 2);
        var ys = Enumerable.Range(1, n).Select(i => Math.Round(P * i, 2)).ToList();

        // Bars between two rails over (u0, y0)–(u1, y1): the open parts between them.
        PathsD Bars(double u0, double y0, double u1, double y1)
        {
            if (u1 - u0 < 2 * bar || y1 - y0 < 1) return [];
            int count = Math.Max(1, (int)Math.Round((u1 - u0 + bar) / (gap + bar)));
            double g = (u1 - u0 - (count - 1) * bar) / count;
            var open = new PathsD();
            for (int i = 0; i < count; i++) open.Add(Geo.Rect(u0 + i * (g + bar), y0, u0 + i * (g + bar) + g, y1));
            return open;
        }

        // The back frame, against the wall.
        var back = b.Upright("Back frame", new V3(0, 0, T / 2), V3.UnitX, W, V3.UnitZ);
        back.Plain = true;
        back.Outline.Add(Geo.Rect(0, 0, W, H));
        var bands = new List<(double Y0, double Y1)> { (0, band) };
        foreach (var y in ys) bands.Add((y - T - band, y + band));
        bands.Add((H - band, H));
        for (int i = 0; i + 1 < bands.Count; i++)
            if (bands[i + 1].Y0 - bands[i].Y1 > 2) back.Holes.Add(Geo.Rect(pw, bands[i].Y1, W - pw, bands[i + 1].Y0));
        foreach (var (y0, y1) in bands.Skip(1).SkipLast(1)) foreach (var u in new[] { pw / 2, W - pw / 2 }) back.Lines.Add(WoodFace.Dot(u, (y0 + y1) / 2, 0.22));

        var landings = new List<Flat>();
        for (int i = 0; i < n; i++)
        {
            double y = ys[i];
            var f = new Flat { Name = "Landing", Y = y, Texture = "none" };
            f.Outline.Add(Build.XZRect(0, T + 0.02, W, D));
            // The grating: slots across it, and a frame round the edge.
            for (double x = 1.4; x < W - 1.4; x += 1.5 * k)
                f.Etched.Add(Build.XZRect(x, T + 1.0, x + 0.55 * k, zr - T / 2 - 0.8));
            f.Lines.Add(Geo.Loop(Build.XZRect(0.6, T + 0.6, W - 0.6, D - 0.6)));
            foreach (var (t0, t1) in b.Tabs(0, W, pw + 1)) b.FlatInto(f, back, t0, t1);
            b.Flats.Add(f);
            landings.Add(f);

            // Its railing: along the front, and across each end.
            var front = b.Upright("Landing rail", new V3(0, 0, zr), V3.UnitX, W, V3.UnitZ);
            front.Plain = true;
            front.Outline.Add(Geo.Rect(0, y, W, y + rh));
            front.Holes.AddRange(Bars(band, y + band, W - band, y + rh - band));
            foreach (var (t0, t1) in b.Tabs(0, W, 2)) b.DownInto(f, front, t0, t1);
            foreach (var (x, facing) in new[] { (Lip + T / 2, -V3.UnitX), (W - Lip - T / 2, V3.UnitX) })
            {
                double z0 = T + 0.4, len = zr - T / 2 - 0.4 - z0;
                var end = b.Upright("Landing end rail", new V3(x, 0, z0), V3.UnitZ, len, facing);
                end.Plain = true;
                end.Outline.Add(Geo.Rect(0, y, len, y + rh));
                end.Holes.AddRange(Bars(band, y + band, len - band, y + rh - band));
                foreach (var (t0, t1) in b.Tabs(0, len, 1.2)) b.DownInto(f, end, t0, t1);
            }
        }

        // A flight between each landing and the next, turning back each time: a stringer with the steps along its top.
        for (int i = 0; i + 1 < n; i++)
        {
            double ya = ys[i], yb = ys[i + 1] - T, rise = yb - ya, L = xs1 - xs0;
            bool rightward = i % 2 == 0;
            var s = b.Upright("Stair flight", new V3(xs0, 0, zf), V3.UnitX, L, V3.UnitZ);
            s.Plain = true;
            double U(double along) => rightward ? along : L - along;
            int steps = Math.Max(4, (int)Math.Round(rise / (3.6 * k)));
            double run = L / steps, r = rise / steps, hb = Math.Max(4.0, 4.2 * k) + r, sb = Math.Min(L * 0.5, L * hb / rise);
            var pts = new PathD { new(U(0), ya) };
            for (int j = 0; j < steps; j++)
            {
                pts.Add(new PointD(U(j * run), ya + (j + 1) * r));
                pts.Add(new PointD(U((j + 1) * run), ya + (j + 1) * r));
            }
            pts.Add(new PointD(U(L), yb - hb));
            pts.Add(new PointD(U(sb), ya));
            s.Outline.Add(rightward ? pts : new PathD(Enumerable.Reverse(pts)));
            // The tread nosings, scored.
            for (int j = 0; j < steps; j++) s.Lines.Add([new(U(j * run + 0.3), ya + (j + 1) * r - 0.5), new(U((j + 1) * run - 0.3), ya + (j + 1) * r - 0.5)]);
            double a0 = U(0.4), a1 = U(Math.Max(0.4 + 2, sb - 0.4));
            b.DownInto(landings[i], s, Math.Min(a0, a1), Math.Max(a0, a1));
            double c0 = U(L - Math.Min(3.0, run * 1.2)), c1 = U(L - 0.3);
            b.UpInto(landings[i + 1], s, Math.Min(c0, c1), Math.Max(c0, c1));
            // The hatch it comes up through, marked on the landing above.
            double hx0 = xs0 + Math.Min(U(L - run * 3), U(L - 3.4)), hx1 = xs0 + Math.Max(U(L - run * 3), U(L - 3.4));
            landings[i + 1].Lines.Add(Geo.Loop(Build.XZRect(hx0, zf - 3 * k, hx1, zf + 3 * k)));
        }

        // The drop ladder from the first landing to the street, at the far end from the first flight.
        {
            double lw = Math.Max(6.0, 7 * k), rw = Math.Max(1.6, 1.6 * k), rp = Math.Max(3.0, 3.4 * k), y1 = ys[0] - T;
            double x0 = n > 1 ? xs1 - lw : xs0, zl = Math.Round(zf + (zr - T / 2 - zf) * 0.55, 2);
            if (zl - zf < T + 0.6) zl = zf + T + 0.6;
            var lad = b.Upright("Drop ladder", new V3(x0, 0, zl), V3.UnitX, lw, V3.UnitZ);
            lad.Plain = true;
            lad.Outline.Add(Geo.Rect(0, 0, lw, y1));
            var gaps = new PathsD { Geo.Rect(rw, -1, lw - rw, y1 - band) };
            var rungs = new PathsD();
            for (double y = rp; y < y1 - band - 1.4; y += rp) rungs.Add(Geo.Rect(rw - 0.1, y, lw - rw + 0.1, y + 1.0));
            lad.Holes.AddRange(Geo.Subtract(gaps, Geo.Union(rungs)));
            foreach (var (t0, t1) in new[] { (0.2, rw), (lw - rw, lw - 0.2) }) b.UpInto(landings[0], lad, t0, t1);
            landings[0].Lines.Add(Geo.Loop(Build.XZRect(x0 - 0.4, zl - 3 * k, x0 + lw + 0.4, zl + 0.6)));
        }
        b.Finish();
    }
}
