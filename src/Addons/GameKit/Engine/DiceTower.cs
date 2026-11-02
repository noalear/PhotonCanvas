using Clipper2Lib;

namespace PhotonCanvas.Addon.GameKit;

/// <summary>
/// A dice tower: two sides that everything else tabs into (the back, a front with a gate at the
/// bottom, a floor that runs out under the gate into a tray, the tray's front rim, and ramps that slope
/// back and forth down the inside). Dice dropped in the top tumble from ramp to ramp and roll out of
/// the gate into the tray.
/// </summary>
/// <remarks>
/// Room coordinates: x across (0 to the width), y up from the table, z from the back of the tower
/// (0) to the front of the tray.
/// </remarks>
public static class DiceTower
{
    /// <summary>How steep the ramps are (degrees).</summary>
    public const double RampDeg = 35;

    /// <summary>What the inside of the tower comes to: where each ramp is, and what it all needs.</summary>
    public sealed record Layout(double E, double FloorTop, double Z0, double Z1, double Gap, double Clear, double GateTop, double TopNeeded,
                                List<(double ZHigh, double YHigh, double ZLow, double YLow)> Ramps);

    /// <summary>The ramps from the bottom up: high and low ends of each one's middle (mid-thickness) line.</summary>
    public static Layout Plan(AccessoryItem it, double T)
    {
        double D = it.DepthMm, die = Math.Max(5, it.DieMm);
        double E = Math.Max(2, T), floorTop = E + T;
        double z0 = E + T, z1 = D - E - T;
        double a = RampDeg * Math.PI / 180, s = Math.Sin(a), c = Math.Cos(a), tan = Math.Tan(a);
        double hv = T / (2 * c), clear = 1.2 * die, gap = 1.15 * die;
        // Ends kept clear of the walls: a ramp's end is square to it, so its corners reach past its middle line.
        double inset = T / 2 * s + 0.05;
        var ramps = new List<(double, double, double, double)>();
        int n = Math.Clamp(it.Baffles, 1, 12);
        double Mid(int j, double z)
        {
            var (zh, yh, zl, yl) = ramps[j];
            return yl + (yh - yl) * (z - zl) / (zh - zl);
        }
        for (int j = 0; j < n; j++)
        {
            bool highAtBack = j % 2 == 0;
            double zh = highAtBack ? z0 + inset : z1 - inset;
            double zl = j == 0 ? z1 - inset : highAtBack ? z1 - gap : z0 + gap;
            double yl = j == 0 ? floorTop + T / 2 * c + 0.3 : Mid(j - 1, zl) + 2 * hv + clear;
            double yh = yl + Math.Abs(zh - zl) * tan;
            ramps.Add((zh, yh, zl, yl));
        }
        double gateTop = floorTop + Math.Max(1.3 * die, 8);
        double topNeeded = ramps.Count > 0 ? ramps[^1].Item2 + hv + 6 : gateTop;
        return new(E, floorTop, z0, z1, gap, clear, gateTop, topNeeded, ramps);
    }

    /// <summary>How deep the battlements' gaps are.</summary>
    public static double CrenelDepth(double H) => Math.Clamp(H * 0.06, 5, 12);

    public static void Make(Maker m)
    {
        var it = m.Item;
        double W = it.WidthMm, D = it.DepthMm, H = it.HeightMm, L = it.TrayMm, R = it.RimMm, T = m.T, die = it.DieMm;
        if (W <= 0 || D <= 0 || H <= 0) { m.Bad("Give it a size: width, depth and height must be more than 0."); return; }
        var p = Plan(it, T);
        double E = p.E, Wi = W - 2 * T, z0 = p.Z0, z1 = p.Z1, Di = z1 - z0, Zt = D + L, zr1 = Zt - E - T;
        double ch = it.Battlements ? CrenelDepth(H) : 0;
        if (die < 5) { m.Bad("The biggest die must be at least 5 mm."); return; }
        if (Wi < die * 1.4) { m.Bad($"The tower is too narrow for {die:0.#} mm dice: make it at least {die * 1.4 + 2 * T:0} mm wide."); return; }
        if (Di - p.Gap < die * 0.8) { m.Bad($"The tower is too shallow for {die:0.#} mm dice: make it at least {die * 0.8 + p.Gap + 2 * (E + T):0} mm deep."); return; }
        if (L < die) { m.Bad($"The tray is too short to catch {die:0.#} mm dice: make it at least {die:0} mm long."); return; }
        if (R < p.FloorTop + 6) { m.Bad($"The tray's sides are too low: make them at least {p.FloorTop + 6:0.#} mm high."); return; }
        if (R > H - ch - 10) { m.Bad("The tray's sides are as high as the tower: make them lower."); return; }
        if (H - ch < p.TopNeeded) { m.Bad($"{it.Baffles} ramps for {die:0.#} mm dice need a tower {p.TopNeeded + ch:0} mm high: make it taller, use fewer ramps or a smaller die."); return; }
        double legW = Math.Max(5, T + 2), gateW = Wi - 2 * legW;
        if (gateW < die * 1.3) { m.Bad($"The gate is too narrow for {die:0.#} mm dice: make the tower wider."); return; }

        // ---- the two sides ----
        var profile = new PathsD { new PathD { new(0, 0), new(Zt, 0), new(Zt, R), new(D, R), new(D, H), new(0, H) } };
        var sideCrenels = new PathsD();
        if (it.Battlements)
            foreach (var (a, b) in Crenels(0, D, ch, z0 + 1.5)) sideCrenels.Add(Geo.Rect(a, H - ch, b, H + 1));
        var sideBody = Geo.Subtract(profile, sideCrenels);
        var left = m.Plate("Left side", Pose.Left(T / 2, 0, 0));
        left.Body.AddRange(sideBody);
        var right = m.Plate("Right side", Pose.Right(W - T / 2, 0, Zt));
        right.Body.AddRange(Geo.Map(sideBody, q => new PointD(Zt - q.x, q.y)));
        foreach (var side in new[] { left, right }) { side.Face = Geo.Union(side.Body); side.GrainAlongU = false; }

        // Each edge's tabs: on the plate's u = −T…0 and Wi…Wi+T, into whichever side is there.
        void EdgeTabs(Plate plate, IEnumerable<(double A, double B)> spots)
        {
            foreach (var (a, b) in spots)
            {
                m.Tab(plate, Geo.Rect(-T, a, 0.01, b), left, right);
                m.Tab(plate, Geo.Rect(Wi - 0.01, a, Wi + T, b), left, right);
            }
        }
        double tw = Math.Clamp(H * 0.08, 6, 14);

        // ---- the back: u runs right to left (it faces out the back) ----
        var back = m.Plate("Back", Pose.Back(W - T, 0, E + T / 2));
        back.Body.AddRange(Geo.Subtract([Geo.Rect(0, 0, Wi, H)], PanelCrenels(W, T, H, ch)));
        back.Face = [Geo.Rect(0, 0, Wi, H)];
        back.GrainAlongU = false;
        EdgeTabs(back, GameKitMaker.Spots(p.FloorTop + 2, H - ch - E, 3, tw));

        // ---- the front, with the gate the dice come out of ----
        double Hf = H - p.FloorTop, gh = p.GateTop - p.FloorTop, gr = Math.Min(gateW / 2, gh * 0.6);
        var gate = Geo.Union(new PathsD
        {
            Geo.Rect(legW, -1, Wi - legW, gh - gr),
            Geo.Rect(legW + gr, -1, Wi - legW - gr, gh),
            Geo.Circle(legW + gr, gh - gr, gr, 0.02),
            Geo.Circle(Wi - legW - gr, gh - gr, gr, 0.02),
        });
        var front = m.Plate("Front", Pose.Front(T, p.FloorTop, z1 + T / 2));
        front.Body.AddRange(Geo.Subtract(Geo.Subtract([Geo.Rect(0, 0, Wi, Hf)], gate), Geo.Move(PanelCrenels(W, T, H, ch), 0, -p.FloorTop)));
        front.Face = [Geo.Rect(0, 0, Wi, Hf)];
        front.GrainAlongU = false;
        EdgeTabs(front, GameKitMaker.Spots(1.5, Hf - ch - E, 3, tw));

        // ---- the floor, from the back to the tray's rim (v runs from the rim back) ----
        var floor = m.Plate("Floor", Pose.Up(T, E + T / 2, zr1));
        double Lf = zr1 - z0;
        floor.Body.Add(Geo.Rect(0, 0, Wi, Lf));
        double V(double z) => zr1 - z;
        double ftw = Math.Clamp(Lf * 0.08, 6, 14);
        var floorSpots = GameKitMaker.Spots(z0 + 3, z1 - 3, 2, ftw).Concat(GameKitMaker.Spots(z1 + T + 3, zr1 - 3, L > 45 ? 2 : 1, ftw))
            .Select(sp => (V(sp.B), V(sp.A)));
        EdgeTabs(floor, floorSpots);

        // ---- the tray's rim ----
        var rim = m.Plate("Tray rim", Pose.Front(T, 0, zr1 + T / 2));
        rim.Body.Add(Geo.Rect(0, 0, Wi, R));
        rim.Face = [Geo.Rect(0, 0, Wi, R)];
        EdgeTabs(rim, GameKitMaker.Spots(p.FloorTop + Maker.Bridge + 0.5, R - 2, 1, Math.Min(12, R)));

        // ---- ramps: the bottom one out of the gate, the others back and forth above it ----
        double ang = RampDeg * Math.PI / 180, sn = Math.Sin(ang), cs = Math.Cos(ang);
        Pose RampPose((double ZHigh, double YHigh, double ZLow, double YLow) r)
        {
            bool highAtBack = r.ZHigh < r.ZLow;
            // u across, v up the slope; the top face up.
            return highAtBack
                ? new Pose(new V3(T, r.YLow, r.ZLow), V3.UnitX, new V3(0, sn, -cs))
                : new Pose(new V3(W - T, r.YLow, r.ZLow), -V3.UnitX, new V3(0, sn, cs));
        }
        double Len((double ZHigh, double YHigh, double ZLow, double YLow) r) => Math.Abs(r.ZHigh - r.ZLow) / cs;
        void RampTabs(Plate plate, double lr)
        {
            var spots = lr < 30 ? GameKitMaker.Spots(lr * 0.3, lr * 0.7, 1, lr * 0.35) : [(lr * 0.15, lr * 0.15 + Math.Min(12, lr * 0.25)), (lr * 0.85 - Math.Min(12, lr * 0.25), lr * 0.85)];
            EdgeTabs(plate, spots);
        }
        var bottom = p.Ramps[0];
        double lb = Len(bottom);
        var bottomRamp = m.Plate("Bottom ramp", RampPose(bottom));
        bottomRamp.Body.Add(Geo.Rect(0, 0, Wi, lb));
        RampTabs(bottomRamp, lb);
        if (p.Ramps.Count > 1)
        {
            double lr = Len(p.Ramps[1]);
            var ramp = m.Plate("Ramp", [.. p.Ramps.Skip(1).Select(RampPose)]);
            ramp.Body.Add(Geo.Rect(0, 0, Wi, lr));
            RampTabs(ramp, lr);
        }

        m.Note($"Dice drop {p.Ramps.Count} ramp{(p.Ramps.Count == 1 ? "" : "s")} and come out of a {gateW:0} × {gh:0} mm gate. For dice up to {die:0.#} mm across.");
        m.Note("Push the ramps, back, front, floor and rim into one side (etched faces out), then press the other side on. A little glue in each slot keeps it square.");
    }

    /// <summary>
    /// The gaps in battlements along an edge from <paramref name="a"/> to <paramref name="b"/>: an odd number
    /// of equal stretches, solid ones at both ends at least <paramref name="minEnd"/> long.
    /// </summary>
    public static List<(double A, double B)> Crenels(double a, double b, double depth, double minEnd)
    {
        double len = b - a;
        int n = (int)Math.Round(len / Math.Max(1, depth * 1.4));
        if (n % 2 == 0) n--;
        while (n > 3 && len / n < minEnd) n -= 2;
        var list = new List<(double, double)>();
        if (n < 3 || len / n < minEnd) return list;
        for (int k = 1; k < n; k += 2) list.Add((a + len * k / n, a + len * (k + 1) / n));
        return list;
    }

    /// <summary>The battlements' gaps on the back or front, laid out across the whole width so the corners stay solid.</summary>
    static PathsD PanelCrenels(double W, double T, double H, double ch)
    {
        var gaps = new PathsD();
        if (ch <= 0) return gaps;
        foreach (var (a, b) in Crenels(0, W, ch, T + 2)) gaps.Add(Geo.Rect(a - T, H - ch, b - T, H + 1));
        return gaps;
    }
}
