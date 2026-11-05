using Clipper2Lib;

namespace PhotonCanvas.Addon.Furniture;

/// <summary>
/// Seats with arms: two shaped sides (the arms and legs in one), a back between them whose top
/// widens over the arms (a pew's back stays between its tall ends), a seat and a rail under its
/// front edge, all tabbed into the sides. A throne stands on a two-step dais that the sides' feet go
/// down into. Armchairs and sofas are upholstered (a cushion and buttons etched on).
/// </summary>
public static class Armchairs
{
    const double BackIn = 1.0, FrontIn = 1.0;

    public static void Make(Maker m)
    {
        var it = m.Item;
        var style = it.Seat;
        var look = Fronts.Look(m);
        double W = it.WidthMm, D = it.DepthMm, H = it.HeightMm, T = m.T, f = m.Fit, sw = m.SlotW;
        bool throne = style == SeatStyle.Throne, pew = style == SeatStyle.Pew, command = style == SeatStyle.Command;
        bool settle = style == SeatStyle.Settle, carved = style is SeatStyle.Wooden or SeatStyle.Settle;
        // A carved wing chair: buttoned like an armchair, with the carved chair's arches, pinnacles and a quatrefoil.
        bool reading = style == SeatStyle.Reading;
        bool soft = style is SeatStyle.Armchair or SeatStyle.Sofa or SeatStyle.Throne or SeatStyle.Command or SeatStyle.Reading;
        string what = style switch { SeatStyle.Throne => "throne", SeatStyle.Sofa => "sofa", SeatStyle.Pew => "pew", SeatStyle.Command or SeatStyle.Wooden or SeatStyle.Reading => "chair", SeatStyle.Settle => "settle", _ => "armchair" };
        // A throne's dais: two steps, the sides' feet go down through them.
        double dais = throne ? 2 * T : 0, step = throne ? Geo.Clamp(Math.Min(W, D) * 0.12, 1.5, 4) : 0;
        double y0 = dais;
        double seat = it.SeatMm;
        double arm = Geo.Clamp(it.ArmMm, seat + 2, H - 2);
        double apron = Geo.Clamp((seat - y0 - T) * 0.35, 1.5, 3.5), yApron = seat - T - apron;
        // A settle's front is a deep panel down nearly to the floor, like a chest under the seat.
        if (settle && seat - T - y0 - 0.8 >= 4) { apron = seat - T - y0 - 0.8; yApron = y0 + 0.8; }
        if (W - 2 * T < 5) { m.Bad($"The {what} is too narrow: make it at least {2 * T + 5:0.#} mm wide."); return; }
        if (D < BackIn + T + FrontIn + T + 4) { m.Bad($"The {what} is too shallow: make it at least {BackIn + FrontIn + 2 * T + 4:0.#} mm deep."); return; }
        if (seat - y0 - T < 3.5) { m.Bad($"The seat is too low: make it at least {y0 + T + 3.5:0.#} mm up."); return; }
        if (H < arm + 2) { m.Bad($"The back is too low: make the {what} at least {arm + 2:0.#} mm high."); return; }

        double Li = W - 2 * T, zb = BackIn + T, zf = D - FrontIn - T / 2;
        double rtw = Geo.Clamp(Li * 0.15, 1.2, 4);
        double[] railAt = Li < 10 ? [Li / 2] : [Li * 0.25, Li * 0.75];
        // ---- the sides (flat: u = z from the back, v = y) ----
        double legs = Geo.Clamp(D * 0.16, 1.6, 3.5);
        var side = new PathsD();
        if (pew)
        {
            // A pew's end: tall at the back, down in a curve to the arm at the front.
            double top = H, endW = Geo.Clamp(D * 0.3, zb + 1.5, D * 0.5);
            var p = new PathD { new(0, y0), new(D, y0), new(D, arm - 1) };
            p.AddRange(Geo.Arc(D - 1, arm - 1, 1, 0, Math.PI / 2, 0.02).Skip(1));
            for (int i = 1; i <= 12; i++)
            {
                double t = i / 12.0, s = t * t * (3 - 2 * t);
                p.Add(new(D - 1 - (D - 1 - endW) * t, arm + (top - arm - 1) * s));
            }
            var shaped = Shapes.Topped(look == FrontLook.Gothic ? BackTop.Pointed : BackTop.Rounded, 0, top - 4, endW, top, Math.Min(endW * 0.6, 3));
            side = Geo.Union([p, Geo.Rect(0, y0, endW, top - 2), shaped]);
        }
        else
        {
            double r = Math.Min(command ? 0.6 : 1.6, (arm - seat) * 0.6);
            var p = new PathD { new(0, y0), new(D, y0), new(D, arm - r) };
            p.AddRange(Geo.Arc(D - r, arm - r, r, 0, Math.PI / 2, 0.02).Skip(1));
            p.Add(new(0, arm));
            if (command)
            {
                // Angled: the arm slopes up to a console at the front.
                p = [new(0, y0), new(D * 0.82, y0), new(D, y0 + (arm - y0) * 0.35), new(D, arm + 0.6), new(D * 0.55, arm), new(0, arm)];
            }
            side.Add(p);
        }
        var holes = new PathsD();
        if (!throne && !command && yApron - 1.2 > 1.5)
            holes.Add(WoodFace.RoundRect(legs, -3, D - legs, yApron - 1.2, Math.Min(1.5, (D - 2 * legs) / 2)));
        // The seat's tabs.
        double seatD = D - zb;
        double stw = Geo.Clamp(seatD * 0.2, 1.2, 4);
        double[] seatAt = seatD < 8 ? [seatD / 2] : [seatD * 0.25, seatD * 0.75];
        foreach (var c in seatAt) holes.Add(Geo.Rect(zb + c - (stw + f) / 2, seat - T - f / 2, zb + c + (stw + f) / 2, seat + f / 2));
        // The back's tabs: under the seat (if there's room), and between the seat and the arm (or higher on a pew).
        double backLow = yApron, sideTop = pew ? H - 3 : arm;
        var backTabs = new List<(double Y, double H)>();
        void Span(double a, double b)
        {
            double h = Math.Min(Math.Min((b - a) * 0.5, 5), b - a - 2.3);
            if (h >= 0.8) backTabs.Add(((a + b) / 2, h));
        }
        Span(backLow, seat - T);
        if (pew && sideTop - seat >= 2 * (2.3 + 0.8 * 2)) { Span(seat, (seat + sideTop) / 2); Span((seat + sideTop) / 2, sideTop); }
        else Span(seat, sideTop);
        if (backTabs.Count == 0) { m.Bad($"The back has no room for its tabs: make the {(pew ? "ends" : "arms")} higher above the seat."); return; }
        foreach (var (yc, h) in backTabs) holes.Add(Geo.Rect(BackIn - f / 2, yc - (h + f) / 2, BackIn + T + f / 2, yc + (h + f) / 2));
        if (carved || reading || throne && look != FrontLook.Metal && m.Theme != PhotonCanvas.Addon.Terrain.TerrainThemes.Cave)
        {
            // An arcade of little pointed arches pierced through each side under the arm.
            double a0 = BackIn + T + 1.4, a1 = D - 1.2, ya = seat + 1.0, yt = arm - 1.1;
            int n = (int)Math.Floor((a1 - a0 + 1.1) / 3.1);
            if (n >= 1 && yt - ya >= 3)
            {
                double aw = (a1 - a0 - (n - 1) * 1.1) / n;
                for (int i = 0; i < n; i++) { double u = a0 + i * (aw + 1.1); holes.Add(Fronts.PointedRect(u, ya, u + aw, yt)); }
            }
        }
        // Feet down through the dais.
        double ftw = Geo.Clamp(D * 0.2, 1.5, 4);
        double[] feetAt = [D * 0.25, D * 0.75];
        if (throne) foreach (var u in feetAt) side.Add(Geo.Rect(u - ftw / 2, 0, u + ftw / 2, y0 + 0.01));
        var etchedS = new PathsD();
        var linesS = new List<PathD>();
        if (pew && look != FrontLook.Metal)
        {
            // A pointed panel carved down the pew's tall end.
            double endW = Geo.Clamp(D * 0.3, zb + 1.5, D * 0.5), a = zb + 0.5, b = endW - 0.6;
            if (b - a >= 2 && H - 2.6 - (arm + 0.6) > 4) etchedS.AddRange(Fronts.Outline([Fronts.PointedRect(a, arm + 0.6, b, H - 2.6)], 0.4));
        }
        // A wooden throne's sides: an arcade of pointed arches pierced under the seat.
        // (A cave chief's seat stays rough.)
        bool regal = throne && look != FrontLook.Metal && m.Theme != PhotonCanvas.Addon.Terrain.TerrainThemes.Cave;
        if (regal && D - 1.2 - (zb + 0.6) > 5 && yApron - 0.6 - (y0 + 1.2) >= 3.2)
        {
            double a0 = zb + 0.8, a1 = D - 1.4, ya = y0 + 1.2, yt = yApron - 0.8;
            int n = Math.Max(2, (int)Math.Floor((a1 - a0 + 1.1) / 3.1));
            double aw = (a1 - a0 - (n - 1) * 1.1) / n;
            for (int i = 0; i < n; i++) { double u = a0 + i * (aw + 1.1); holes.Add(Fronts.PointedRect(u, ya, u + aw, yt)); }
        }
        else if (throne || command)
        {
            double a = zb + 0.6, b = D - 1.2;
            if (b - a > 3 && seat - T - y0 > 4) Fronts.Panel(look, a, y0 + 1.2, b, yApron - 0.6, etchedS, linesS);
        }
        m.Add("Left side", side, holes, [Pose.Left(T / 2, 0, 0)], grainAlongU: true, etched: etchedS, lines: linesS);
        PathsD Mirror(PathsD a) => Geo.Map(a, p => new PointD(D - p.x, p.y));
        m.Add("Right side", Mirror(side), Mirror(holes), [Pose.Right(W - T / 2, 0, D)], grainAlongU: true, etched: Mirror(etchedS), lines: Geo.Map(linesS, p => new PointD(D - p.x, p.y)));

        // ---- the back (flat: u = x from the left side's outside, v = y from its bottom) ----
        double yb = backLow;
        var back = new PathsD { Geo.Rect(T, 0, T + Li, sideTop - yb) };
        double rise = 0;
        var backHoles = new PathsD();
        if (!pew)
        {
            rise = it.BackTop switch { BackTop.Square => 0, BackTop.Winged => Math.Min(W * 0.35, (H - arm) * 0.4), BackTop.Pointed => Math.Min(W * 0.5, (H - arm) * 0.5), _ => Math.Min(W * 0.3, (H - arm) * 0.45) };
            back.Add(Shapes.Topped(it.BackTop, 0, sideTop - yb - 0.01, W, H - yb, rise));
            if ((carved || reading || regal) && rise > 2)
            {
                // Pinnacles up the back's corners, each topped with a ball.
                double pw = Math.Max(1.6, T * 0.8), ptop = H - yb - rise * 0.25;
                foreach (var x in new[] { 0.0, W - pw })
                {
                    back.Add(Geo.Rect(x, sideTop - yb - 0.01, x + pw, ptop - pw * 0.9));
                    back.Add(Geo.Circle(x + pw / 2, ptop - pw / 2, pw / 2 + 0.05, 0.02));
                }
            }
        }
        if (carved && !settle && rise > 3)
        {
            // A trefoil pierced through the top of the back.
            double r = Math.Min(Li * 0.13, rise * 0.22);
            if (r > 0.9) backHoles.AddRange(Geo.Offset(Geo.Offset([Cabinets.Trefoil(W / 2, H - yb - rise * 0.55 - r * 0.2, r)], 0.45, round: true), -0.45, round: true));
        }
        foreach (var (yc, h) in backTabs)
        {
            back.Add(Geo.Rect(0, yc - yb - h / 2, T + 0.01, yc - yb + h / 2));
            back.Add(Geo.Rect(T + Li - 0.01, yc - yb - h / 2, W, yc - yb + h / 2));
        }
        var etchedB = new PathsD();
        var linesB = new List<PathD>();
        double cushionTop = H - yb - (pew ? 0 : Math.Min(3, (H - arm) * 0.3)) - 1;
        if ((reading || regal) && rise > 3.5)
        {
            // The buttoned back stops under the crest, a quatrefoil pierced through the crest.
            cushionTop = H - yb - rise - 0.3;
            double r = Math.Min(Li * 0.1, rise * 0.16);
            if (r > 0.6) backHoles.AddRange(Geo.Offset(Geo.Offset(Candles.Quatrefoil(W / 2, H - yb - rise * (regal ? 0.45 : 0.5), r), 0.2, round: true), -0.2, round: true));
        }
        if (soft && !command) Shapes.Tufted(T + 0.8, seat - yb + 0.5, T + Li - 0.8, cushionTop, buttons: style != SeatStyle.Sofa, etchedB, linesB);
        if (command)
        {
            // A padded headrest and ribs.
            double a = T + Li * 0.15, b = T + Li * 0.85;
            for (double y = seat - yb + 1.5; y < cushionTop - 1; y += 1.4) linesB.Add([new(a, y), new(b, y)]);
        }
        if (carved)
        {
            double y0b = seat - yb + 0.8, y1b = H - yb - rise - 0.8;
            if (settle)
            {
                // An arcade of tall pointed panels across the back.
                int n = Math.Max(2, (int)Math.Round(Li / 5.0));
                double pw = (Li - 1.2 - (n - 1) * 0.9) / n;
                if (pw > 1.6 && y1b - y0b > 3) for (int i = 0; i < n; i++) { double x = T + 0.6 + i * (pw + 0.9); etchedB.AddRange(Fronts.Outline([Fronts.PointedRect(x, y0b, x + pw, y1b)], 0.4)); }
            }
            else if (y1b - y0b > 4)
            {
                // A sunk pointed panel with the theme's symbol in it.
                etchedB.AddRange(Fronts.Outline([Fronts.PointedRect(T + 0.8, y0b, T + Li - 0.8, y1b + rise * 0.3)], 0.4));
                double r = Math.Min(Li * 0.25, (y1b - y0b) * 0.28);
                if (r > 1) Cabinets.Symbol(look, m.Theme, W / 2, (y0b + y1b) / 2, r, etchedB, linesB);
            }
        }
        if (regal && rise > 3.5)
        {
            // The crest: a moulding following its pointed top round the pierced quatrefoil.
            var crest = Shapes.Topped(it.BackTop == BackTop.Square ? BackTop.Pointed : it.BackTop, T + 0.6, H - yb - rise - 0.1, W - T - 0.6, H - yb - 0.9, Math.Max(0.5, rise - 1.2));
            etchedB.AddRange(Fronts.Outline([crest], 0.4));
        }
        else if (throne && H - arm > 6)
        {
            // A crest on the back: a shield, a sun or a cog, high up.
            double r = Math.Min(W * 0.16, (H - arm) * 0.25);
            Cabinets.Symbol(look, m.Theme, W / 2, H - yb - Math.Min(W * 0.35, (H - arm) * 0.4) * 0.3 - r * 1.3, r, etchedB, linesB);
        }
        if (pew && look != FrontLook.Metal)
        {
            // An arcade of tall pointed panels carved across the pew's back.
            double y0b = seat - yb + 0.8, y1b = sideTop - yb - 0.8;
            int n = Math.Max(2, (int)Math.Round(Li / 5.5));
            double pw = (Li - 1.2 - (n - 1) * 0.9) / n;
            if (pw > 1.6 && y1b - y0b > 3) for (int i = 0; i < n; i++) { double x = T + 0.6 + i * (pw + 0.9); etchedB.AddRange(Fronts.Outline([Fronts.PointedRect(x, y0b, x + pw, y1b)], 0.4)); }
        }
        else if (style == SeatStyle.Sofa || pew)
        {
            int seats = Geo.Clamp(it.Columns, 1, 8);
            for (int i = 1; i < seats; i++)
            {
                double x = T + Li * i / seats;
                linesB.Add([new(x, seat - yb + 0.5), new(x, cushionTop)]);
            }
        }
        if (throne && m.Theme == PhotonCanvas.Addon.Terrain.TerrainThemes.Dungeon && W >= 12)
        {
            // A warlord's crown of spikes along the back's top: iron spikes and tusks, the middle ones tallest.
            var solid = Geo.Solid(back);
            var spikes = new PathsD();
            int n = Math.Max(5, (int)Math.Round((W - 2 * T) / 2.2)) | 1;
            for (int i = 0; i < n; i++)
            {
                double x = T + 0.9 + (W - 2 * T - 1.8) * i / (n - 1), mid = 1 - Math.Abs(2.0 * i / (n - 1) - 1);
                var col = Geo.Intersect(solid, [Geo.Rect(x - 0.05, -100, x + 0.05, 1000)]);
                if (col.Count == 0) continue;
                double top = Geo.Bounds(col).bottom, len = Geo.Clamp(W * 0.3, 3.5, 8) * (0.6 + 0.6 * mid), lean = (x - W / 2) / W * 2.4;
                spikes.Add(new PathD { new(x - 1.3, top - 1.5), new(x + 1.3, top - 1.5), new(x + lean, top + len) });
            }
            back.AddRange(Geo.Offset(Geo.Offset(Geo.Solid(spikes), -0.5), 0.5, round: true));
        }
        m.Add("Back", Geo.Solid(back), backHoles, [Pose.Front(0, yb, BackIn + T / 2)], grainAlongU: false, face: soft ? [] : null, etched: etchedB, lines: linesB);

        // ---- the seat (flat: u = x from the left side's inside, v from the front back) ----
        var plate = new PathsD { Geo.Rect(0, 0, Li, seatD) };
        foreach (var c in seatAt)
        {
            double v = seatD - c;
            plate.Add(Geo.Rect(-T, v - stw / 2, 0.01, v + stw / 2));
            plate.Add(Geo.Rect(Li - 0.01, v - stw / 2, Li + T, v + stw / 2));
        }
        var seatSlots = new PathsD();
        foreach (var u in railAt) seatSlots.Add(Geo.Rect(u - (rtw + f) / 2, D - zf - sw / 2, u + (rtw + f) / 2, D - zf + sw / 2));
        var etchedP = new PathsD();
        var linesP = new List<PathD>();
        if (soft)
        {
            int seats = style is SeatStyle.Sofa ? Geo.Clamp(it.Columns, 1, 8) : 1;
            for (int i = 0; i < seats; i++) Shapes.Tufted(Li * i / seats + 0.2, 0.3, Li * (i + 1) / seats - 0.2, seatD - 0.3, buttons: false, etchedP, linesP);
        }
        m.Add("Seat", plate, seatSlots, [Pose.Up(T, seat - T / 2, D)], grainAlongU: true, face: soft ? [] : null, etched: etchedP, lines: linesP, planks: !soft, plankMm: 3);

        // ---- the rail under the seat's front, its tabs up through the seat ----
        var rail = new PathsD { Geo.Rect(0, 0, Li, apron) };
        foreach (var u in railAt) rail.Add(Geo.Rect(u - rtw / 2, apron - 0.01, u + rtw / 2, apron + T));
        var etchedR = new PathsD();
        var linesR = new List<PathD>();
        var railHoles = new PathsD();
        if (settle && apron >= 4)
        {
            // The settle's front: a panel for each seat with a quatrefoil pierced through it.
            int n = Geo.Clamp(it.Columns, 1, 8) + 1;
            double pw = (Li - 1.2 - (n - 1) * 1.0) / n;
            for (int i = 0; i < n && pw > 2.4; i++)
            {
                double x = 0.6 + i * (pw + 1.0);
                etchedR.AddRange(Fronts.Outline(x, 0.6, x + pw, apron - 0.6, 0.4));
                double r = Math.Min(pw, apron - 1.2) * 0.2;
                if (r > 0.7) railHoles.AddRange(Geo.Offset(Geo.Offset(Candles.Quatrefoil(x + pw / 2, apron / 2, r), 0.4, round: true), -0.4, round: true));
            }
        }
        else if (look == FrontLook.Gothic && Li > 6) for (double x = 1.5; x < Li - 1; x += 2.2) linesR.Add(Geo.Loop(Fronts.PointedRect(x - 0.6, 0.5, x + 0.6, apron - 0.4)));
        m.Add("Front rail", rail, railHoles, [Pose.Front(T, yApron, zf)], grainAlongU: true, face: [Geo.Rect(0, 0, Li, apron)], etched: etchedR, lines: linesR);

        // ---- a throne's dais: two steps with slots for the feet ----
        if (throne)
        {
            for (int k = 0; k < 2; k++)
            {
                double e = step * (2 - k);
                double Wd = W + 2 * e, Dd = D + 2 * e;
                var slots = new PathsD();
                foreach (var xs in new[] { T / 2, W - T / 2 })
                    foreach (var u in feetAt)
                    {
                        double x = xs + e, v = Dd - (u + e);
                        slots.Add(Geo.Rect(x - sw / 2, v - (ftw + f) / 2, x + sw / 2, v + (ftw + f) / 2));
                    }
                var de = new PathsD();
                var dl = new List<PathD>();
                if (k == 0) dl.Add(Geo.Loop(Geo.Rect(e * 0.5, e * 0.5, Wd - e * 0.5, Dd - e * 0.5)));
                m.Add(k == 0 ? "Dais (bottom step)" : "Dais (top step)", [Geo.Rect(0, 0, Wd, Dd)], slots, [Pose.Up(-e, k * T + T / 2, D + e)], grainAlongU: true, etched: de, lines: dl);
            }
        }
        m.Note((throne ? "Push the sides' feet into the dais, then " : "") + "slide the seat, the back and the front rail into one side, and push the other side on.");
    }
}
