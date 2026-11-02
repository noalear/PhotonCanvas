using Clipper2Lib;

namespace PhotonCanvas.Addon.GameKit;

/// <summary>Card stands (for holding a hand) and deck trays (for the piles on the table).</summary>
public static class Cards
{
    /// <summary>How far the cards lean back in a card stand's slots (degrees).</summary>
    public const double LeanDeg = 15;

    /// <summary>The card stand's sizes worked out: each row's height, the slots' depth and the rails'.</summary>
    public sealed record StandPlan(double[] RowHeights, double RowDepth, double SlotDepth, double RailHeight, double[] RailZ, double[] RibX);

    public static StandPlan PlanStand(AccessoryItem it, double T)
    {
        double L = it.WidthMm, D = it.DepthMm, H = it.HeightMm;
        int k = Math.Clamp(it.Tiers, 1, 6), nr = Math.Clamp(it.Ribs, 2, 12);
        double h0 = k == 1 ? H : H * 0.55;
        var heights = Enumerable.Range(0, k).Select(i => k == 1 ? H : h0 + (H - h0) * i / (k - 1)).ToArray();
        double hr = Math.Clamp(h0 * 0.3, 5, 12), sd = Math.Clamp(h0 * 0.45, 5, 14);
        double e = Math.Max(8, 3 * T);
        double[] ribs = [.. Enumerable.Range(0, nr).Select(i => e + i * (L - 2 * e) / (nr - 1))];
        double[] rails = [Math.Max(D * 0.2, T + 2), Math.Min(D * 0.8, D - T - 2)];
        return new(heights, D / k, sd, hr, rails, ribs);
    }

    /// <summary>
    /// A card stand: identical upright ribs, each with a slanted slot in every row, joined by a front
    /// and a back rail in halving joints (a slot from the bottom of the rib, one from the top of the rail).
    /// </summary>
    public static void Stand(Maker m)
    {
        var it = m.Item;
        double L = it.WidthMm, D = it.DepthMm, H = it.HeightMm, T = m.T, sw = m.SlotW, g = Math.Clamp(it.SlotMm, 0.5, 20);
        if (L <= 0 || D <= 0 || H <= 0) { m.Bad("Give it a size: length, depth and height must be more than 0."); return; }
        var p = PlanStand(it, T);
        int k = p.RowHeights.Length;
        double lean = LeanDeg * Math.PI / 180, sl = Math.Sin(lean), cl = Math.Cos(lean);
        if (L < 2 * Math.Max(8, 3 * T) + 20) { m.Bad($"The card stand is too short: make it at least {2 * Math.Max(8, 3 * T) + 20:0} mm long."); return; }
        if (p.RowDepth < Math.Max(12, 3 * g + 4)) { m.Bad($"Each row needs at least {Math.Max(12, 3 * g + 4):0} mm of depth: make the stand deeper or have fewer rows."); return; }
        if (p.RowHeights[0] - p.SlotDepth * cl - g / 2 < p.RailHeight / 2 + Maker.Bridge + m.Fit) { m.Bad($"The front row is too low for its slot: make the stand at least {(p.RailHeight / 2 + Maker.Bridge + p.SlotDepth + g) / (k == 1 ? 1 : 0.55):0} mm high."); return; }
        if (k > 1 && p.RowHeights[1] - p.RowHeights[0] < 5) { m.Bad("The rows are too close in height to see the cards behind: make the stand taller or have fewer rows."); return; }

        // The rib: steps from the front (z = D) back, the slot in each.
        var steps = new PathD { new(0, 0), new(D, 0) };
        for (int i = 0; i < k; i++)
        {
            double zf = D - i * p.RowDepth, zb = D - (i + 1) * p.RowDepth;
            steps.Add(new PointD(zf, p.RowHeights[i]));
            steps.Add(new PointD(zb, p.RowHeights[i]));
        }
        var slots = new PathsD();
        for (int i = 0; i < k; i++)
        {
            double top = p.RowHeights[i], zt = D - i * p.RowDepth - p.RowDepth * 0.55;
            var bottom = new PointD(zt + p.SlotDepth * sl, top - p.SlotDepth * cl);
            var above = new PointD(zt - 3 * sl, top + 3 * cl);
            slots.Add(Geo.Strip(above, bottom, g));
            slots.Add(Geo.Circle(bottom.x, bottom.y, g / 2, 0.01));
        }
        foreach (var z in p.RailZ) slots.Add(Geo.Rect(z - sw / 2, -1, z + sw / 2, p.RailHeight / 2));
        var rib = m.Plate("Rib", [.. p.RibX.Select(x => Pose.Left(x, 0, 0))]);
        rib.Body.AddRange(Geo.Subtract([steps], Geo.Union(slots)));

        // The rails, with a slot down from the top for each rib.
        var railSlots = new PathsD(p.RibX.Select(x => Geo.Rect(x - sw / 2, p.RailHeight / 2, x + sw / 2, p.RailHeight + 1)));
        foreach (var (z, name) in new[] { (p.RailZ[1], "Front rail"), (p.RailZ[0], "Back rail") })
        {
            var rail = m.Plate(name, Pose.Front(0, 0, z));
            rail.Body.AddRange(Geo.Subtract([Geo.Rect(0, 0, L, p.RailHeight)], railSlots));
            if (name == "Front rail") rail.Face = [Geo.Rect(0, 0, L, p.RailHeight)];
        }
        m.Note($"{k} row{(k == 1 ? "" : "s")} of {g:0.#} mm slots. Slide the ribs down onto the rails (the rails' slots up, the ribs' down), the tall end of the ribs at the back.");
    }

    /// <summary>The deck tray's outside size (x, y, z) and its piles' inside size.</summary>
    public static (double W, double H, double D, double PileW, double PileD) TraySize(AccessoryItem it, double T)
    {
        const double clear = 1.5;
        int n = Math.Clamp(it.Compartments, 1, 8);
        double pw = it.CardWMm + clear, pd = it.CardHMm + clear;
        return (n * pw + (n + 1) * T, it.HeightMm + T, pd + 2 * T, pw, pd);
    }

    /// <summary>
    /// A deck tray: a finger-jointed box open at the top, cards lying flat in each pile, dividers tabbed
    /// into the bottom, front and back, and a finger notch at the front and back of each pile.
    /// </summary>
    public static void Tray(Maker m)
    {
        var it = m.Item;
        double T = m.T;
        int n = Math.Clamp(it.Compartments, 1, 8);
        if (it.CardWMm < 15 || it.CardHMm < 15) { m.Bad("Give the cards' size: at least 15 mm each way."); return; }
        if (it.HeightMm < 4) { m.Bad("The tray needs to be at least 4 mm deep inside."); return; }
        var (W, H, D, pw, _) = TraySize(it, T);
        var panels = Joints.Box([W, H, D], T, [true, true, true, false, true, true]);
        var plates = new Dictionary<string, Plate>();
        double rn = Math.Min(Math.Min(pw * 0.2, it.HeightMm * 0.55), 12);
        foreach (var pn in panels)
        {
            var plate = m.Plate(pn.Name, pn.Pose);
            plate.Body.AddRange(pn.Cut);
            if (pn.Name is "Front" or "Back")
            {
                var notches = new PathsD();
                for (int c = 0; c < n; c++)
                {
                    double x = T + c * (pw + T) + pw / 2, u = pn.Name == "Front" ? x : W - x;
                    notches.Add(Geo.Circle(u, H, rn, 0.02));
                }
                plate.Body.Clear();
                plate.Body.AddRange(Geo.Subtract(pn.Cut, notches));
            }
            if (pn.Name != "Bottom") plate.Face = [Geo.Rect(0, 0, pn.Lu, pn.Lv)];
            plates[pn.Name] = plate;
        }
        if (n > 1)
        {
            var poses = Enumerable.Range(1, n - 1).Select(k => Pose.Left(k * (pw + T) + T / 2, 0, 0)).ToArray();
            var div = m.Plate("Divider", poses);
            double hd = H - T;
            div.Body.Add(Geo.Rect(T, T, D - T, H));
            double tw = Math.Clamp((D - 2 * T) * 0.15, 4, 12);
            foreach (var (a, b) in GameKitMaker.Spots(T + 3, D - T - 3, 2, tw)) m.Tab(div, Geo.Rect(a, 0, b, T + 0.01), plates["Bottom"]);
            foreach (var (a, b) in GameKitMaker.Spots(T + 2, H - Math.Max(2, rn * 0.2), 1, Math.Min(10, hd * 0.5)))
            {
                m.Tab(div, Geo.Rect(0, a, T + 0.01, b), plates["Back"]);
                m.Tab(div, Geo.Rect(D - T - 0.01, a, D, b), plates["Front"]);
            }
        }
        m.Note($"{n} pile{(n == 1 ? "" : "s")} of {it.CardWMm:0.#} × {it.CardHMm:0.#} mm cards, {it.HeightMm:0.#} mm deep. Push the sides together at the corners, drop the bottom in{(n > 1 ? ", then slide the dividers down into their slots" : "")}.");
    }
}
