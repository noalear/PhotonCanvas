using Clipper2Lib;

namespace PhotonCanvas.Addon.Furniture;

/// <summary>
/// A candelabra built up like a real one: a baluster stem with knops and an urn, S-scrolled arms with
/// curls under them springing from a medallion, a cup and a wide drip pan under every taper, the middle
/// candle standing highest. The front shape stands in a stepped, domed foot that crosses it (halving
/// joint), and turned bosses are glued on both faces of the medallion and knops so the stem reads round
/// from every side, not as a flat cut-out.
/// </summary>
public static class Candles
{
    static PathsD Bar(PathD line, double w) => Clipper.InflatePaths([line], w / 2, JoinType.Round, EndType.Round, 2, 4);

    /// <summary>A cubic curve from a to d through the pulls b and c, as a line.</summary>
    public static PathD Curve(PointD a, PointD b, PointD c, PointD d, int n = 20)
    {
        var p = new PathD();
        for (int i = 0; i <= n; i++)
        {
            double t = i / (double)n, s = 1 - t;
            p.Add(new PointD(s * s * s * a.x + 3 * s * s * t * b.x + 3 * s * t * t * c.x + t * t * t * d.x,
                             s * s * s * a.y + 3 * s * s * t * b.y + 3 * s * t * t * c.y + t * t * t * d.y));
        }
        return p;
    }

    /// <summary>A curl: a bar winding round <paramref name="turns"/> of a circle that shrinks as it goes, ending in a ball.</summary>
    public static PathsD Curl(double cx, double cy, double r, double a0, double turns, double w, int dir)
    {
        var p = new PathD();
        int n = 24;
        for (int i = 0; i <= n; i++)
        {
            double t = i / (double)n, a = a0 + dir * t * turns * 2 * Math.PI, rr = r * (1 - 0.45 * t);
            p.Add(new PointD(cx + rr * Math.Cos(a), cy + rr * Math.Sin(a)));
        }
        var all = Bar(p, w);
        all.Add(Geo.Circle(p[^1].x, p[^1].y, w * 0.62, 0.02));
        return all;
    }

    /// <summary>A quatrefoil: four lobes round (cx, cy), each of radius r.</summary>
    public static PathsD Quatrefoil(double cx, double cy, double r)
    {
        var q = new PathsD();
        for (int k = 0; k < 4; k++)
        {
            double a = k * Math.PI / 2;
            q.Add(Geo.Circle(cx + Math.Cos(a) * r * 0.8, cy + Math.Sin(a) * r * 0.8, r, 0.02));
        }
        q.Add(Geo.Circle(cx, cy, r * 0.9, 0.02));
        return Geo.Solid(q);
    }

    /// <summary>The stepped, domed foot seen from the side: a band on the floor, a step, a dome up to the neck.</summary>
    static PathsD FootShape(double xc, double w, double h, double neck)
    {
        double band = Math.Max(1.0, Math.Min(1.2, h * 0.3)), step = Math.Max(1.0, Math.Min(1.1, h * 0.25));
        var f = new PathsD
        {
            WoodFace.RoundRect(xc - w / 2, 0, xc + w / 2, band, 0.3),
            WoodFace.RoundRect(xc - w * 0.4, band - 0.01, xc + w * 0.4, band + step, 0.25),
        };
        double d0 = band + step - 0.01;
        // The dome: a quarter ellipse each side, up to the neck.
        var dome = new PathD();
        for (int i = 0; i <= 16; i++)
        {
            double a = Math.PI * i / 16;
            dome.Add(new PointD(xc + Math.Cos(a) * w * 0.32, d0 + Math.Sin(a) * (h - d0)));
        }
        f.Add(dome);
        f.Add(Geo.Rect(xc - neck / 2, d0, xc + neck / 2, h + 0.3));
        return Geo.Intersect(Geo.Solid(f), [Geo.Rect(xc - w, 0, xc + w, h + 0.3)]);
    }

    public static void Candelabra(Maker m)
    {
        var it = m.Item;
        double W = it.WidthMm, D = it.DepthMm, H = it.HeightMm, sw = m.SlotW, T = m.T, f = m.Fit;
        double xc = W / 2, stem = Geo.Clamp(W * 0.11, 1.15, 1.6);
        // The foot: as wide as the depth (and wide enough round its slot), its cross piece as high as half the dome.
        double footW = Math.Max(Math.Min(D, W * 0.75), sw + 2 * Maker.Bridge + 1.6), footH = Geo.Clamp(H * 0.14, 2.6, 4.2);
        double crossH = Math.Max(2.3, footH * 0.6);
        if (H < 12 || W < 6) { m.Bad("Make the candelabra at least 12 mm high and 6 mm wide."); return; }

        double flameH = Geo.Clamp(H * 0.075, 1.5, 2.4), taper = Geo.Clamp(H * 0.13, 2.4, 4.2);
        double topY = H - flameH - taper;                       // the middle pan's top
        int each = W >= 11 ? 2 : 1;
        double reach = W / 2 - Math.Max(1.35, W * 0.11);         // the outer arms' cups
        double sideY = topY - Geo.Clamp(H * 0.06, 1.0, 2.0);     // the side pans' tops
        double medR = Geo.Clamp(W * 0.12, 1.35, 2.0), medY = sideY - Geo.Clamp(H * 0.15, 3.4, 5.5);
        if (medY - medR < footH + 3) medY = footH + 3 + medR;

        var shape = new PathsD();
        var etched = new PathsD();
        var lines = new List<PathD>();
        shape.AddRange(FootShape(xc, footW, footH, stem + 0.6));

        // The stem: a bar with an urn below the medallion and knops above the foot and under the medallion.
        shape.AddRange(Bar([new(xc, footH), new(xc, topY)], stem));
        double urnY = footH + (medY - medR - footH) * 0.55, urnH = Math.Max(1.6, (medY - medR - footH) * 0.36);
        var knops = new List<(double Y, double Rx, double Ry)>
        {
            (footH + 0.7, stem * 1.05, 0.55),
            (urnY, stem * 1.0 + 0.35, urnH / 2),
            (medY - medR - 0.45, stem * 0.95, 0.5),
            (medY + medR + 0.4, stem * 0.85, 0.45),
        };
        foreach (var (y, rx, ry) in knops) shape.Add(Rugs.Ellipse(xc, y, rx, ry));
        // Rings scored round the urn and the knops, the way a turned stem has them.
        lines.Add([new(xc - stem, urnY + urnH * 0.28), new(xc + stem, urnY + urnH * 0.28)]);
        lines.Add([new(xc - stem, urnY - urnH * 0.28), new(xc + stem, urnY - urnH * 0.28)]);

        // The medallion: a quatrefoil with a quatrefoil etched in it.
        var med = Quatrefoil(xc, medY, medR * 0.62);
        shape.AddRange(med);
        lines.Add(Geo.Loop(Geo.Offset(med, -0.28, round: true).FirstOrDefault() ?? []));
        etched.AddRange(Quatrefoil(xc, medY, medR * 0.24));

        // The arms: S-scrolls from the medallion, dipping, then sweeping up to the cups; a curl under each.
        var cups = new List<(double X, double Y, double Pan)> { (xc, topY, 1.35) };
        double armW = Math.Max(1.05, stem * 0.85);
        for (int k = 1; k <= each; k++)
        {
            bool outer = k == each;
            double rx = each == 1 ? reach : reach * (k == 1 ? 0.5 : 1.0);
            double cy = !outer ? sideY - Math.Min(0.9, H * 0.035) : sideY;
            // The outer arms leave the medallion's sides, swoop down and out, and turn up to their cups; the
            // inner ones rise from its shoulders in a gentle curve.
            double from = outer ? medY - medR * 0.1 : medY + medR * 0.45;
            double dip = outer ? medY - Geo.Clamp(H * 0.07, 1.3, 2.2) : from - 0.3;
            foreach (var s in new[] { -1, 1 })
            {
                var arm = outer
                    ? Curve(new(xc + s * medR * 0.5, from), new(xc + s * rx * 0.35, dip - 0.4), new(xc + s * rx * 1.08, dip - 0.3), new(xc + s * rx, cy - 1.3))
                    : Curve(new(xc + s * medR * 0.3, from), new(xc + s * rx * 0.7, from - 0.2), new(xc + s * rx, from + 0.4), new(xc + s * rx, cy - 1.3));
                shape.AddRange(Bar(arm, armW));
                if (outer)
                {
                    // A curl hanging under the arm's low sweep.
                    double ccx = xc + s * rx * 0.58, ccy = dip - 1.25;
                    if (ccy - 1.2 > urnY + urnH / 2)
                        shape.AddRange(Curl(ccx, ccy, 0.95, Math.PI / 2, 0.72, Math.Max(1.0, armW * 0.9), -s));
                }
                cups.Add((xc + s * rx, cy, k == each ? 1.3 : 1.2));
            }
        }

        foreach (var (x, y, pan) in cups)
        {
            // A vase-shaped cup on the arm, a wide drip pan on it, the taper and its flame.
            shape.Add(new PathD { new(x - 0.55, y - 1.5), new(x + 0.55, y - 1.5), new(x + 0.95, y - 0.5), new(x - 0.95, y - 0.5) });
            shape.Add(Rugs.Ellipse(x, y - 1.55, 0.7, 0.45));
            shape.Add(WoodFace.RoundRect(x - pan, y - 0.62, x + pan, y + 0.35, 0.3));
            shape.Add(Geo.Rect(x - 0.56, y, x + 0.56, y + taper));
            var flame = Profiles.Flames(x, y + taper - 0.5, 1.35, flameH + 0.5);
            shape.AddRange(flame);
            etched.AddRange(Geo.Intersect(Geo.Offset(flame, -0.18), [Geo.Rect(x - 2, y + taper + 0.05, x + 2, H + 1)]));
            lines.Add([new(x - pan + 0.2, y - 0.14), new(x + pan - 0.2, y - 0.14)]);
            // Wax run down the taper, and the wick's end.
            lines.Add([new(x - 0.22, y + taper - 0.15), new(x - 0.22, y + taper * 0.5)]);
            lines.Add([new(x + 0.18, y + taper - 0.15), new(x + 0.18, y + taper * 0.78)]);
        }

        // Nothing narrower than the laser can leave standing.
        var outline = Geo.Open(Geo.Solid(shape), Maker.Bridge * 0.96);
        // The foot's halving slot: up from the bottom, half the cross piece's height.
        var holes = new PathsD { Geo.Rect(xc - sw / 2, -1, xc + sw / 2, crossH / 2) };
        m.Add("Candelabra", outline, holes, [Pose.Front(0, 0, D / 2)], grainAlongU: false, etched: etched, lines: lines);

        // The cross piece of the foot, seen from the side: the band and the step, slotted down from its top.
        double cw = footW;
        var cross = Geo.Open(Geo.Intersect(FootShape(cw / 2, cw, footH, stem + 0.6), [Geo.Rect(0, 0, cw, crossH)]), Maker.Bridge * 0.96);
        var crossSlot = new PathsD { Geo.Rect(cw / 2 - sw / 2, crossH / 2, cw / 2 + sw / 2, crossH + 1) };
        double z0 = D / 2 - cw / 2;
        m.Add("Foot", cross, crossSlot, [Pose.Left(xc, 0, z0)], grainAlongU: true);

        // Turned bosses glued on both faces: the medallion's, and the urn's.
        var boss = Geo.Solid([Geo.Circle(medR, medR, medR * 0.95, 0.02)]);
        var bossLines = new List<PathD> { WoodFace.Dot(medR, medR, medR * 0.62) };
        var bossEtch = Quatrefoil(medR, medR, medR * 0.22);
        var urn = Geo.Solid([Rugs.Ellipse(stem, urnH / 2, stem * 0.95, urnH / 2), Geo.Rect(stem * 0.45, -0.1, stem * 1.55, urnH + 0.1)]);
        urn = Geo.Intersect(urn, [Geo.Rect(0, 0, 2 * stem, urnH)]);
        if (m.PushFit) { m.Note("Slide the foot's cross piece onto the bottom of the candelabra, crossing it."); return; }
        m.Add("Medallion boss", boss, [], [Pose.Front(xc - medR, medY - medR, D / 2 + T), Pose.Back(xc + medR, medY - medR, D / 2 - T)], grainAlongU: true, etched: bossEtch, lines: bossLines).Glue();
        if (urnH >= 1.6)
            m.Add("Urn boss", urn, [], [Pose.Front(xc - stem, urnY - urnH / 2, D / 2 + T), Pose.Back(xc + stem, urnY - urnH / 2, D / 2 - T)], grainAlongU: false,
                lines: [[new(0, urnH * 0.22), new(2 * stem, urnH * 0.22)], [new(0, urnH * 0.78), new(2 * stem, urnH * 0.78)]]).Glue();
        _ = f;
        m.Note("Slide the foot's cross piece onto the bottom of the candelabra, crossing it, and glue a boss on each face of the medallion and the urn.");
    }
}
