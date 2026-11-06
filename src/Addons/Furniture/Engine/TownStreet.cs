using Clipper2Lib;
using PhotonCanvas.Addon.Terrain;

namespace PhotonCanvas.Addon.Furniture;

/// <summary>A street piece: a street lamp, a statue on its pedestal, a signpost, a market cross, a wooden watchtower or a city block's fire escape.</summary>
public enum StreetStyle { Lamp, Statue, Signpost, MarketCross, Watchtower, FireEscape }

/// <summary>Street pieces for the city builder: lamps, statues, signposts, market crosses and watchtowers.</summary>
public static partial class TownWalls
{
    /// <summary>The street piece at the size it starts at (28 mm figures, 25.4 mm squares).</summary>
    public static FurnitureItem StreetSized(FurnitureItem it, StreetStyle s) => s switch
    {
        StreetStyle.Statue => it with { Street = s, WidthMm = 18, DepthMm = 18, HeightMm = 46 },
        StreetStyle.Signpost => it with { Street = s, WidthMm = 26, DepthMm = 20, HeightMm = 40 },
        StreetStyle.MarketCross => it with { Street = s, WidthMm = 25.4, DepthMm = 25.4, HeightMm = 50 },
        StreetStyle.Watchtower => it with { Street = s, WidthMm = 38, DepthMm = 38, HeightMm = 88 },
        StreetStyle.FireEscape => FireEscapeFor(it with { WidthMm = 50.8, DepthMm = 16 }, 2, 51),
        _ => it with { Street = s, WidthMm = 13, DepthMm = 13, HeightMm = 46 },
    };

    /// <summary>The street piece the item describes.</summary>
    public static void Street(Maker m)
    {
        switch (m.Item.Street)
        {
            case StreetStyle.Statue: Statue(m); break;
            case StreetStyle.Signpost: Signpost(m); break;
            case StreetStyle.MarketCross: MarketCross(m); break;
            case StreetStyle.Watchtower: Watchtower(m); break;
            case StreetStyle.FireEscape: FireEscape(m); break;
            default: Lamp(m); break;
        }
    }

    // ---- shared by the round pieces ----

    /// <summary>
    /// Two profiles halved together standing at (cx, cz): the first along x with its face to the front, the
    /// second along z with its face to the right (its u running front to back). Each is drawn about u = its
    /// half-length. One is slotted down from the top to <paramref name="ym"/>, the other up from the bottom.
    /// </summary>
    static (Up A, Up B) Crossed(Build b, string name, double cx, double cz, double halfA, double halfB, double ym, bool aFromTop = true)
    {
        var a = b.Upright(name, new V3(cx - halfA, 0, cz), V3.UnitX, 2 * halfA, V3.UnitZ);
        var c = b.Upright(name + " (crossing)", new V3(cx, 0, cz - halfB), V3.UnitZ, 2 * halfB, V3.UnitX);
        a.Plain = c.Plain = true;
        double sw = b.M.SlotW, top = b.M.Item.HeightMm + 5;
        a.Holes.Add(aFromTop ? Geo.Rect(halfA - sw / 2, ym, halfA + sw / 2, top) : Geo.Rect(halfA - sw / 2, -5, halfA + sw / 2, ym));
        c.Holes.Add(aFromTop ? Geo.Rect(halfB - sw / 2, -5, halfB + sw / 2, ym) : Geo.Rect(halfB - sw / 2, ym, halfB + sw / 2, top));
        return (a, c);
    }

    /// <summary>
    /// The highest height (searching down from <paramref name="y1"/>) where the shape is at least
    /// <paramref name="half"/> wide either side of u = c, so a piece slotted down from the top can stop
    /// there with both its halves wide enough to stand.
    /// </summary>
    static double CapAt(PathsD shape, double c, double half, double y0, double y1)
    {
        var solid = Geo.Solid(shape);
        for (double y = y1; y > y0; y -= 0.1)
        {
            var probe = new PathsD { Geo.Rect(c - half, y - 0.6, c + half, y) };
            if (Geo.Area(Geo.Subtract(probe, solid)) < 1e-3) return y;
        }
        return y0;
    }

    /// <summary>A ring (r0 to r1 round x, y) as quarter-degree wedges, so it stays a ring when outlines are filled and joined.</summary>
    static PathsD Annulus(double x, double y, double r0, double r1)
    {
        int n = Math.Max(24, Geo.Segments(r1, 0.02));
        var q = new PathsD();
        for (int i = 0; i < n; i++)
        {
            double a0 = 2 * Math.PI * i / n, a1 = 2 * Math.PI * (i + 1) / n;
            q.Add([new(x + r0 * Math.Cos(a0), y + r0 * Math.Sin(a0)), new(x + r1 * Math.Cos(a0), y + r1 * Math.Sin(a0)), new(x + r1 * Math.Cos(a1), y + r1 * Math.Sin(a1)), new(x + r0 * Math.Cos(a1), y + r0 * Math.Sin(a1))]);
        }
        return q;
    }

    /// <summary>The shape cut off flat at height <paramref name="y"/>.</summary>
    static PathsD Below(PathsD shape, double y) => Geo.Intersect(Geo.Solid(shape), [Geo.Rect(-1e4, -1e4, 1e4, y)]);

    /// <summary>
    /// A paved octagonal base for a round piece (a round one of grass and stones in a cave), on a wider
    /// glued step when there's glue.
    /// </summary>
    static Flat StreetBase(Build b, double cx, double cz, double R, double yb, string theme, bool glue)
    {
        bool round = theme == TerrainThemes.Cave, metal = theme == TerrainThemes.SciFi;
        double r = glue ? R - 1.0 : R;
        var bs = new Flat { Name = "Base", Y = yb, Texture = "none" };
        void Paving(Flat f, double rr)
        {
            if (round) { f.Outline.Add(Geo.Circle(cx, -cz, rr, 0.02)); return; }
            f.Outline.Add(Octagon(cx, cz, rr));
            f.Lines.Add(Geo.Loop(Octagon(cx, cz, rr - 0.7)));
        }
        Paving(bs, r);
        if (round) Ground(bs, cx, -cz, r, false, b.M.Rnd);
        else
        {
            // Flags laid round the foot, or deck plates with bolts.
            for (int i = 0; i < 8; i++)
            {
                double ang = Math.PI / 8 + i * Math.PI / 4, ca = Math.Cos(ang) / Math.Cos(Math.PI / 8), sa = Math.Sin(ang) / Math.Cos(Math.PI / 8);
                if (metal) bs.Lines.Add(WoodFace.Dot(cx + ca * (r - 1.4), -(cz + sa * (r - 1.4)), 0.25));
                else bs.Lines.Add([new(cx + ca * (r * 0.55), -(cz + sa * (r * 0.55))), new(cx + ca * (r - 0.7), -(cz + sa * (r - 0.7)))]);
            }
            if (!metal) bs.Lines.Add(Geo.Loop(Octagon(cx, cz, r * 0.55)));
            else bs.Etched.AddRange(Geo.Subtract([Octagon(cx, cz, r - 0.7)], [Octagon(cx, cz, r - 1.2)]));
        }
        b.Flats.Add(bs);
        if (glue)
        {
            var st = new Flat { Name = "Base step", Y = b.T, Texture = "none", Glued = true };
            Paving(st, R);
            b.Flats.Add(st);
        }
        return bs;
    }

    /// <summary>Two tabs down into the base from each of the crossed profiles, clear of each other's slots.</summary>
    static void StandIn(Build b, Flat bs, Up A, Up B, double halfA, double halfB)
    {
        double a = b.M.SlotW / 2 + 2.2, tab = 1.8;
        foreach (var (up, c) in new[] { (A, halfA), (B, halfB) })
            foreach (var s in new[] { -1.0, 1.0 })
                b.DownInto(bs, up, c + s * a - tab / 2, c + s * a + tab / 2);
    }

    /// <summary>What a profile is drawn from: its outline (cut off for the piece slotted from the top), the bits on only one of the two, and its detail.</summary>
    sealed class Drawing
    {
        public PathsD Both = [], OnlyA = [], OnlyB = [], Etched = [];
        public List<PathD> Lines = [];
        /// <summary>Where the piece slotted down from the top is cut off flat (null: not cut).</summary>
        public double? CapA;

        public void Into(Up a, Up b)
        {
            var both = Geo.Solid(Both);
            // Outlines to the outline, and the holes they leave (inside a ring, say) to the holes.
            void Add(Up up, PathsD shape)
            {
                up.Outline.AddRange(shape.Where(Clipper.IsPositive));
                up.Holes.AddRange(shape.Where(q => !Clipper.IsPositive(q)));
            }
            Add(a, CapA is { } y ? Below(both, y) : both);
            a.Outline.AddRange(OnlyA);
            Add(b, both);
            b.Outline.AddRange(OnlyB);
            foreach (var up in new[] { a, b })
            {
                up.Etched.AddRange(Etched);
                up.Lines.AddRange(Lines);
            }
        }
    }

    // ---- the street lamp ----

    /// <summary>
    /// A street lamp: a post of two profiles halved together on a paved base, a moulded plinth at its foot
    /// and the theme's head on top: a lantern with a pyramid roof on scrolled brackets and a ladder bar
    /// (medieval), a pointed lantern under a spire with pinnacles and a cross (gothic), a pierced brass
    /// lantern under an onion dome (desert), an iron brazier of flames (dungeon), a bound torch on a log
    /// post (cave) or a light pylon with an antenna (sci-fi).
    /// </summary>
    static void Lamp(Maker m)
    {
        var it = m.Item;
        double T = m.T, W = it.WidthMm, D = it.DepthMm, H = it.HeightMm, sw = m.SlotW, k = H / 46;
        string theme = m.Theme ?? TerrainThemes.Medieval;
        bool glue = !m.PushFit;
        double a = sw / 2 + 2.2, tab = 1.8, fh = Math.Max(a + tab / 2 + 0.7, 3.2 * k), R = Math.Min(W, D) / 2;
        if (R < fh + 1.0) { m.Bad($"The street lamp is too narrow for its foot: make it at least {Math.Ceiling(2 * (fh + 1.0)):0} mm across."); return; }
        if (H < 30) { m.Bad("The street lamp is too low: make it at least 30 mm high."); return; }
        var b = new Build(m, LookOf(theme), k);
        double cx = W / 2, cz = D / 2, yb = glue ? 2 * T : T;
        var bs = StreetBase(b, cx, cz, R, yb, theme, glue);
        // Anything on the post's middle at least this wide, so the halves either side of a slot stand.
        double nk = sw / 2 + 1.3, hc = Math.Max(sw / 2 + 1.4, 1.5 * k), y1 = yb + 3.0 * k;
        double lw = Math.Max(hc + 0.9, 2.9 * k), yS = H - 13 * k;
        double ym = Math.Round((y1 + 2.2 * k + yS) / 2, 1);
        var (A, B) = Crossed(b, "Lamp post", cx, cz, R, R, ym);
        StandIn(b, bs, A, B, R, R);
        var d = new Drawing();
        PointD P(double t, double y) => new(R + t, y);
        // The post on its moulded plinth.
        bool log = theme == TerrainThemes.Cave, metal = theme == TerrainThemes.SciFi;
        d.Both.Add(Lathe(R, [(fh, yb), (fh, y1 - 0.6), (fh - 0.6, y1), (hc + 1.1, y1), (hc + 1.1, y1 + 1.2 * k), (hc, y1 + 2.2 * k), (hc, yS + 0.5)]));
        d.Lines.Add(Geo.Loop(Geo.Rect(R - fh + 0.6, yb + 0.6, R + fh - 0.6, y1 - 1.0)));
        if (metal) d.Etched.AddRange(Hazard(R - fh + 0.6, yb + 0.6, R + fh - 0.6, y1 - 1.0, 0.8));
        if (log)
        {
            // Bark and a knot or two.
            for (double t = -hc + 0.6; t < hc - 0.4; t += 0.9)
            {
                var l = new PathD();
                for (double y = y1 + 2.4 * k; y < yS - 0.6; y += 1.1) l.Add(P(t + (m.Rnd.NextDouble() - 0.5) * 0.25, y));
                if (l.Count > 1) d.Lines.Add(l);
            }
            d.Lines.Add(Geo.Loop(Rugs.Ellipse(R + hc * 0.3, (y1 + yS) * 0.45, 0.5, 0.8)));
        }
        else if (theme == TerrainThemes.Dungeon)
        {
            // A twisted iron post with a ring round it.
            for (double y = y1 + 2.6 * k; y < yS - 2.4; y += 1.5 * k) d.Lines.Add([P(-hc + 0.3, y), P(hc - 0.3, y + 1.0)]);
            double yr = (y1 + yS) / 2;
            d.Both.Add(Geo.Rect(R - hc - 0.6, yr, R + hc + 0.6, yr + 1.3));
        }
        else
            foreach (var s in new[] { -1.0, 1.0 }) d.Lines.Add([P(s * hc * 0.45, y1 + 2.6 * k), P(s * hc * 0.45, yS - 2.0 * k)]);
        switch (theme)
        {
            case TerrainThemes.Dungeon:
            case TerrainThemes.Cave:
            {
                // A brazier, or a torch bound with rags, with flames rising out of it.
                double yT;
                if (log)
                {
                    yT = yS + 4.6 * k;
                    d.Both.Add(Lathe(R, [(nk, yS - 1.2), (lw, yS + 1.2 * k), (lw, yT - 0.5), (lw - 0.5, yT)]));
                    for (double y = yS + 1.4 * k; y < yT - 0.8; y += 1.1 * k) { d.Lines.Add([P(-lw + 0.3, y), P(lw - 0.3, y + 0.8)]); d.Lines.Add([P(-lw + 0.3, y + 0.8), P(lw - 0.3, y)]); }
                    // Lashing below the head.
                    d.Both.Add(Geo.Rect(R - hc - 0.5, yS - 3.4 * k, R + hc + 0.5, yS - 3.4 * k + 1.3));
                }
                else
                {
                    yT = yS + 4.0 * k;
                    double rim = lw + 1.2;
                    d.Both.Add(Lathe(R, [(nk, yS - 0.8), (lw + 0.8, yT - 1.0 * k), (rim, yT - 1.0 * k), (rim, yT)]));
                    d.Lines.Add([P(-lw - 0.4, yT - 1.0 * k + 0.4), P(lw + 0.4, yT - 1.0 * k + 0.4)]);
                    for (double t = -lw + 0.6; t < lw; t += 1.2) d.Lines.Add([P(t * 0.5, yS + 0.2), P(t, yT - 1.0 * k)]);
                    // Spikes at the corners of the rim.
                    foreach (var s in new[] { -1.0, 1.0 })
                        d.Both.Add([P(s * (rim - 2.0), yT - 0.1), P(s * rim, yT - 0.1), P(s * (rim - 0.4), yT + 1.9)]);
                }
                double fh2 = H - yT;
                d.OnlyB.AddRange(Geo.Move(Profiles.Flames(0, 0, 2 * lw * 0.85, fh2 + 0.3), R, yT - 0.3));
                // The piece slotted from the top gets a flame either side of its slot.
                double fw = Math.Min(2.4, lw - sw / 2 - 0.5);
                if (fw >= 1.6)
                    foreach (var s in new[] { -1.0, 1.0 })
                        d.OnlyA.AddRange(Geo.Move(Profiles.Flames(0, 0, fw, fh2 * 0.8), R + s * (sw / 2 + 0.25 + fw / 2), yT - 0.3));
                d.CapA = yT;
                break;
            }
            case TerrainThemes.SciFi:
            {
                // A light housing on a tapered neck, its lamp glowing underneath, an antenna and a beacon on top.
                double yh = yS + 2.8 * k;
                d.Both.Add(Lathe(R, [(hc, yS - 2.6 * k), (Math.Max(nk, lw * 0.7), yS + 0.1)]));
                d.Both.Add(Fronts.Chamfered(R - lw - 0.6, yS, R + lw + 0.6, yh, 0.7));
                d.Etched.Add(Geo.Rect(R - lw, yS + 0.4, R + lw, yS + 1.3 * k));
                d.Lines.Add([P(-lw, yh - 0.7), P(lw, yh - 0.7)]);
                double rb = Math.Max(0.8, 0.9 * k);
                d.OnlyB.Add(Geo.Rect(R - 0.6, yh - 0.3, R + 0.6, H - 2 * rb + 0.3));
                d.OnlyB.Add(Geo.Rect(R - 1.8, H - 4.2 * k, R + 1.8, H - 4.2 * k + 1.2));
                d.OnlyB.Add(Geo.Circle(R, H - rb, rb, 0.02));
                d.CapA = yh;
                break;
            }
            default:
            {
                // A lantern on a capital and brackets.
                double yl0 = yS + 1.0, gothic = theme == TerrainThemes.Gothic ? 1 : 0, desert = theme == TerrainThemes.Desert ? 1 : 0;
                double rb = Math.Max(0.8, 0.9 * k), yA = H - (gothic > 0 ? 3.4 * k : 2 * rb + 0.4), yl1 = yl0 + (desert > 0 ? 5.0 : 5.6) * k;
                if (desert > 0)
                {
                    // A swelling brass lantern, pierced all over, under an onion dome.
                    double neck = Math.Max(nk, lw * 0.6);
                    d.Both.Add(Lathe(R, [(hc, yS - 1.0 * k), (neck, yS), (lw, yS + 2.0 * k), (lw, yl1 - 1.4 * k), (neck, yl1), (lw + 0.5, yl1 + 1.8 * k), (Math.Max(nk, lw * 0.75), yl1 + 3.4 * k), (Math.Max(nk, 1.2), yA - 0.6)]));
                    d.Both.Add(Geo.Rect(R - lw - 0.5, yl1 - 0.2, R + lw + 0.5, yl1 + 0.9));
                    for (double y = yS + 2.4 * k; y < yl1 - 1.6 * k; y += 1.2)
                        for (double t = -lw + 0.8; t < lw - 0.6; t += 1.2) d.Etched.Add(Geo.Circle(R + t + (((int)(y * 10) % 2 == 0) ? 0 : 0.6), y, 0.32, 0.01));
                    d.OnlyB.Add(Geo.Rect(R - 0.6, yA - 1.0, R + 0.6, H - 2 * rb + 0.3));
                    d.OnlyB.Add(Geo.Circle(R, H - rb, rb, 0.02));
                    d.CapA = CapAt(d.Both, R, nk, yl1, yA);
                    break;
                }
                // The capital, the brackets and the tray the lantern stands on.
                d.Both.Add(Lathe(R, [(hc, yS - 1.2 * k), (lw + 0.5, yS - 0.1), (lw + 0.5, yl0)]));
                foreach (var s in new[] { -1.0, 1.0 })
                {
                    d.Both.Add(Geo.Strip(P(s * (hc - 0.3), yS - 4.4 * k), P(s * (lw - 0.2), yS - 0.6), 1.2));
                    d.Lines.Add(WoodFace.Dot(R + s * (hc + 0.5), yS - 3.6 * k, 0.25));
                }
                // The lantern, its glass glowing.
                d.Both.Add(Geo.Rect(R - lw, yl0 - 0.1, R + lw, yl1 + 0.1));
                if (gothic > 0)
                {
                    d.Etched.Add(Fronts.PointedRect(R - lw + 0.6, yl0 + 0.6, R + lw - 0.6, yl1 - 0.3));
                    d.Lines.Add([P(-lw * 0.45, yl0 + 0.6), P(-lw * 0.45, yl1 - 1.6)]);
                    d.Lines.Add([P(lw * 0.45, yl0 + 0.6), P(lw * 0.45, yl1 - 1.6)]);
                    // A spire with a pinnacle at each corner and a cross on top.
                    d.Both.Add(new PathD { P(-lw - 0.9, yl1), P(lw + 0.9, yl1), P(lw + 0.9, yl1 + 1.0), P(0.7, yA), P(-0.7, yA), P(-lw - 0.9, yl1 + 1.0) });
                    foreach (var s in new[] { -1.0, 1.0 })
                        d.Both.Add(new PathD { P(s * (lw + 0.2) - 0.7, yl1 + 0.5), P(s * (lw + 0.2) + 0.7, yl1 + 0.5), P(s * (lw + 0.2) + 0.7, yl1 + 2.2 * k), P(s * (lw + 0.2), yl1 + 3.4 * k), P(s * (lw + 0.2) - 0.7, yl1 + 2.2 * k) });
                    d.OnlyB.Add(Geo.Rect(R - 0.6, yA - 0.5, R + 0.6, H));
                    d.OnlyB.Add(Geo.Rect(R - 1.7, H - 2.4 * k, R + 1.7, H - 2.4 * k + 1.2));
                }
                else
                {
                    d.Etched.Add(Geo.Rect(R - lw + 0.6, yl0 + 0.6, R + lw - 0.6, yl1 - 0.6));
                    d.Lines.Add([P(-lw + 0.6, (yl0 + yl1) / 2), P(lw - 0.6, (yl0 + yl1) / 2)]);
                    // A pyramid roof with a ball on top.
                    d.Both.Add(new PathD { P(-lw - 1.0, yl1), P(lw + 1.0, yl1), P(lw + 1.0, yl1 + 1.0), P(0, yA), P(-lw - 1.0, yl1 + 1.0) });
                    d.Lines.Add([P(-lw - 0.6, yl1 + 0.5), P(lw + 0.6, yl1 + 0.5)]);
                    d.OnlyB.Add(Geo.Rect(R - 0.6, yA - 1.2, R + 0.6, H - 2 * rb + 0.3));
                    d.OnlyB.Add(Geo.Circle(R, H - rb, rb, 0.02));
                    // A bar across the post for the lamplighter's ladder.
                    double yx = yS - 7.2 * k;
                    if (yx > y1 + 3) d.OnlyA.Add(Geo.Rect(R - 3.6 * k, yx, R + 3.6 * k, yx + 1.3));
                }
                foreach (var s in new[] { -1.0, 1.0 }) d.Lines.Add([P(s * lw * 0.5, yl0 + 0.6), P(s * lw * 0.5, yl1 - 0.6)]);
                d.CapA = CapAt(d.Both, R, nk, yl1, yA);
                break;
            }
        }
        d.Into(A, B);
        b.Finish();
        m.Note("Slide the two halves of the post together by their slots, then push its four feet into the base" + (glue ? " and glue the base onto its step." : "."));
    }

    // ---- the signpost ----

    /// <summary>
    /// A signpost at a crossroads: a post of two profiles halved together on a mound, four arrow boards
    /// lettered and nailed on pointing four ways at different heights, a cap on top and the theme's finial
    /// (a ball, a crow, a skull on a spike, a horned skull, an onion knob, or holo boards and an antenna).
    /// </summary>
    static void Signpost(Maker m)
    {
        var it = m.Item;
        double T = m.T, W = it.WidthMm, D = it.DepthMm, H = it.HeightMm, sw = m.SlotW, k = H / 40;
        string theme = m.Theme ?? TerrainThemes.Medieval;
        bool glue = !m.PushFit, metal = theme == TerrainThemes.SciFi;
        double a = sw / 2 + 2.2, tab = 1.8, fh = Math.Max(a + tab / 2 + 0.8, 2.8 * k), hc = Math.Max(sw / 2 + 1.4, 1.6 * k), nk = sw / 2 + 1.3;
        double bh = Math.Max(2.8, 3.2 * k), need = hc + 1.2 + 1.6 * bh;
        if (W / 2 < need || D / 2 < need) { m.Bad($"The signpost is too small for its boards: make it at least {Math.Ceiling(2 * need):0} mm each way."); return; }
        if (H < 26) { m.Bad("The signpost is too low: make it at least 26 mm high."); return; }
        var b = new Build(m, LookOf(theme), k);
        double cx = W / 2, cz = D / 2, yb = glue ? 2 * T : T, Rb = Math.Max(fh + 1.6, Math.Min(W, D) * 0.3);
        var bs = StreetBase(b, cx, cz, Rb, yb, theme == TerrainThemes.SciFi ? theme : TerrainThemes.Cave, glue);
        double yTop = H - 6.0 * k, y1 = yb + 2.2 * k, ym = Math.Round((y1 + yTop) / 2 - 2 * k, 1);
        double hA = W / 2, hB = D / 2;
        var (A, B) = Crossed(b, "Signpost", cx, cz, hA, hB, ym);
        StandIn(b, bs, A, B, hA, hB);
        foreach (var (up, c, along) in new[] { (A, hA, true), (B, hB, false) })
        {
            PointD P(double t, double y) => new(c + t, y);
            var o = up.Outline;
            var post = new PathsD { Lathe(c, [(fh, yb), (fh, yb + 1.0), (hc + 0.6, y1), (hc, y1 + 0.8), (hc, yTop)]) };
            // The cap: a little roof over the post.
            post.Add(new PathD { P(-hc - 0.8, yTop - 0.2), P(hc + 0.8, yTop - 0.2), P(hc + 0.8, yTop + 0.8), P(0, yTop + 0.8 + 1.8 * k), P(-hc - 0.8, yTop + 0.8) });
            // The boards: on the front profile pointing left high up and right lower down, on the side one
            // forwards and back in between, each through the post a little way.
            double L = c - 0.4, y0a = yTop - 1.2 - bh, gap = 0.9 * k;
            var boards = along
                ? new[] { (-1.0, y0a), (1.0, y0a - 2 * (bh + gap)) }
                : new[] { (-1.0, y0a - (bh + gap)), (1.0, y0a - 3 * (bh + gap)) };
            foreach (var (dir, y0) in boards)
            {
                if (y0 < y1 + 1.5) continue;
                double near = -dir * (hc + 1.2), far = dir * L, tip = Math.Min(bh * 0.5, L * 0.3);
                var board = metal
                    ? new PathD { P(near, y0), P(far - dir * tip, y0), P(far, y0 + bh / 2), P(far - dir * tip, y0 + bh), P(near, y0 + bh) }
                    : new PathD { P(near, y0), P(far - dir * tip, y0), P(far, y0 + bh / 2), P(far - dir * tip, y0 + bh), P(near, y0 + bh), P(near + dir * 0.6, y0 + bh / 2) };
                post.Add(board);
                if (metal)
                {
                    // A holo board: a glowing strip and chevrons pointing the way.
                    up.Etched.Add(Geo.Rect(c + Math.Min(near, far) + 0.6, y0 + 0.5, c + Math.Max(near, far) - 0.6, y0 + 0.9));
                    for (double t = hc + 1.2; t < L - tip - 0.6; t += 1.4)
                        up.Lines.Add([P(dir * t, y0 + bh * 0.3), P(dir * (t + 0.6), y0 + bh * 0.55), P(dir * t, y0 + bh * 0.8)]);
                }
                else
                {
                    up.Lines.Add(Geo.Loop(Geo.Offset([board], -0.45).FirstOrDefault() ?? board));
                    // Lettering: words of strokes along the board.
                    double t = hc + 0.9;
                    while (t < L - tip - 0.8)
                    {
                        int n = 2 + m.Rnd.Next(4);
                        for (int i = 0; i < n && t < L - tip - 0.8; i++, t += 0.42)
                            up.Lines.Add([P(dir * t, y0 + bh * 0.32), P(dir * t, y0 + bh * (0.6 + 0.12 * m.Rnd.NextDouble()))]);
                        t += 0.7;
                    }
                    up.Lines.Add(WoodFace.Dot(c + dir * (hc - 0.7), y0 + bh / 2, 0.22));
                }
            }
            var shape = Geo.Solid(post);
            if (along) o.AddRange(Below(shape, CapAt(shape, c, nk, yTop, yTop + 0.8 + 1.8 * k)));
            else
            {
                o.AddRange(shape);
                Finial(theme, up, c, yTop + 0.8 + 1.8 * k - 0.6, H, k);
            }
            // Grain up the post.
            if (!metal) foreach (var s in new[] { -1.0, 1.0 }) up.Lines.Add([P(s * hc * 0.4, y1 + 1.2), P(s * hc * 0.4, yTop - 0.8)]);
            else up.Lines.Add(Geo.Loop(Geo.Rect(c - hc + 0.5, y1 + 1.5, c + hc - 0.5, y1 + 4.0)));
        }
        b.Finish();
        m.Note("Slide the two halves of the signpost together by their slots, then push its four feet into the mound" + (glue ? " and glue the mound onto its base." : "."));
    }

    /// <summary>A finial standing on the piece that's whole at the top, from <paramref name="y0"/> to <paramref name="H"/>.</summary>
    static void Finial(string theme, Up up, double c, double y0, double H, double k)
    {
        var o = up.Outline;
        double rb = Math.Max(0.8, 0.9 * k);
        switch (theme)
        {
            case TerrainThemes.Gothic:
            {
                // A crow perched on the cap, looking out.
                double yc = y0 + 0.2;
                o.Add(Rugs.Ellipse(c + 0.2, yc + 1.2, 1.9, 1.15));
                o.Add(Geo.Circle(c + 1.7, yc + 2.5, 0.85, 0.02));
                o.Add(Rugs.Ellipse(c + 2.3, yc + 2.4, 1.3, 0.62));
                o.Add(new PathD { new(c - 1.2, yc + 0.6), new(c - 3.2, yc + 1.0), new(c - 3.0, yc + 2.2), new(c - 1.0, yc + 1.8) });
                o.Add(Geo.Rect(c - 0.6, y0 - 0.6, c + 0.6, yc + 0.6));
                up.Lines.Add([new(c - 1.0, yc + 1.5), new(c + 0.8, yc + 1.0)]);
                up.Etched.Add(Geo.Circle(c + 1.9, yc + 2.7, 0.22, 0.01));
                break;
            }
            case TerrainThemes.Dungeon:
            case TerrainThemes.Cave:
            {
                // A skull on a spike (horned, in a cave).
                double r = Math.Max(1.5, 1.5 * k), ys = H - r - (theme == TerrainThemes.Cave ? 1.6 : 0);
                o.Add(Geo.Rect(c - 0.6, y0 - 0.6, c + 0.6, ys));
                o.Add(Geo.Circle(c, ys + 0.2, r, 0.02));
                o.Add(WoodFace.RoundRect(c - r * 0.6, ys - r * 1.05, c + r * 0.6, ys - r * 0.2, 0.3));
                foreach (var s in new[] { -1.0, 1.0 }) up.Etched.Add(Rugs.Ellipse(c + s * r * 0.38, ys + 0.1, r * 0.25, r * 0.3));
                up.Lines.Add([new(c - r * 0.4, ys - r * 0.6), new(c + r * 0.4, ys - r * 0.6)]);
                if (theme == TerrainThemes.Cave)
                    foreach (var s in new[] { -1.0, 1.0 })
                        o.Add(Geo.Strip(new(c + s * r * 0.6, ys + r * 0.6), new(c + s * (r + 1.4), H - 0.6), 1.2));
                break;
            }
            case TerrainThemes.Desert:
                o.Add(Lathe(c, [(0.7, y0 - 0.6), (0.7, y0 + 0.6), (rb + 0.5, y0 + 1.6), (0.65, H - 1.0), (0.65, H)]));
                break;
            case TerrainThemes.SciFi:
                o.Add(Geo.Rect(c - 0.6, y0 - 0.6, c + 0.6, H - 2 * rb + 0.3));
                o.Add(Geo.Rect(c - 1.8, H - 3.8 * k, c + 1.8, H - 3.8 * k + 1.2));
                o.Add(Geo.Circle(c, H - rb, rb, 0.02));
                break;
            default:
                o.Add(Geo.Rect(c - 0.6, y0 - 0.6, c + 0.6, H - 2 * rb + 0.3));
                o.Add(Geo.Circle(c, H - rb, rb, 0.02));
                break;
        }
    }

    // ---- the market cross ----

    /// <summary>
    /// A market cross: three octagonal steps threaded over the foot of a shaft of two profiles halved
    /// together, its socle resting on the top step, a knop halfway up and the theme's head: a wheel-head
    /// cross (medieval), a pinnacled tabernacle with a spire (gothic), a little domed kiosk (desert), a
    /// broken cross hung with a chain and shackle (dungeon), a totem of stacked faces under horns (cave) or
    /// a beacon in a ring (sci-fi).
    /// </summary>
    static void MarketCross(Maker m)
    {
        var it = m.Item;
        double T = m.T, F = m.Fit, W = it.WidthMm, D = it.DepthMm, H = it.HeightMm, sw = m.SlotW, k = H / 50;
        string theme = m.Theme ?? TerrainThemes.Medieval;
        bool metal = theme == TerrainThemes.SciFi;
        double R = Math.Min(W, D) / 2, st = 2.6 * k, nk = sw / 2 + 1.3;
        double fw = Math.Max(sw / 2 + 1.5, 2.6 * k), hc = Math.Max(sw / 2 + 1.3, 1.9 * k);
        if (theme == TerrainThemes.Cave) hc = Math.Max(hc, 2.6 * k);
        if (R - 2 * st < fw + 2.4) { m.Bad($"The market cross is too small for its steps: make it at least {Math.Ceiling(2 * (fw + 2.4 + 2 * st)):0} mm across."); return; }
        if (H < 30) { m.Bad("The market cross is too low: make it at least 30 mm high."); return; }
        var face = theme == TerrainThemes.Cave ? LookOf(theme) with { Face = "Stone blocks", A = 6, B = 4, G = 0.9 } : LookOf(theme);
        var b = new Build(m, face, k);
        double cx = W / 2, cz = D / 2;
        // The steps, each one or more layers of the sheet, every layer with a cross-shaped hole for the shaft.
        int per = Math.Max(1, (int)Math.Round(Math.Max(T, 2.4 * k) / T)), layers = 3 * per;
        double ys = layers * T;
        for (int j = 0; j < layers; j++)
        {
            int step = j / per;
            double ap = R - step * st;
            var f = new Flat { Name = $"Step {step + 1}" + (per > 1 ? $" ({j % per + 1})" : ""), Y = (j + 1) * T, Texture = "none" };
            f.Outline.Add(Octagon(cx, cz, ap));
            f.Holes.Add(Build.XZRect(cx - fw - F / 2, cz - (T + F) / 2, cx + fw + F / 2, cz + (T + F) / 2));
            f.Holes.Add(Build.XZRect(cx - (T + F) / 2, cz - fw - F / 2, cx + (T + F) / 2, cz + fw + F / 2));
            if (j % per == per - 1)
            {
                // The worn edge of the tread and the joints between its stones.
                f.Lines.Add(Geo.Loop(Octagon(cx, cz, ap - 0.7)));
                for (int i = 0; i < 8; i++)
                {
                    double ang = Math.PI / 8 + i * Math.PI / 4, ca = Math.Cos(ang) / Math.Cos(Math.PI / 8), sa = Math.Sin(ang) / Math.Cos(Math.PI / 8);
                    double r0 = step == 2 ? fw + 1.4 : ap - st + 0.3;
                    if (metal) f.Lines.Add(WoodFace.Dot(cx + ca * (ap - 1.3), -(cz + sa * (ap - 1.3)), 0.25));
                    else f.Lines.Add([new(cx + ca * r0, -(cz + sa * r0)), new(cx + ca * (ap - 0.7), -(cz + sa * (ap - 0.7)))]);
                }
                if (metal) f.Etched.AddRange(Hazard(cx - ap * 0.4, -(cz + ap - 0.4), cx + ap * 0.4, -(cz + ap - 1.4), 0.8));
            }
            b.Flats.Add(f);
        }
        double yH = H - 15 * k, ym = Math.Round(ys + (yH - ys) * 0.42, 1);
        var (A, B) = Crossed(b, "Cross shaft", cx, cz, R, R, ym);
        var d = new Drawing();
        PointD P(double t, double y) => new(R + t, y);
        // The foot through the steps, the socle on the top one, the shaft with a knop halfway.
        double so = fw + 1.2, yk = (ys + yH) / 2;
        d.Both.Add(Lathe(R, [(fw, 0), (fw, ys), (so, ys), (so, ys + 1.6 * k), (hc + 0.6, ys + 2.8 * k), (hc, ys + 3.4 * k), (hc, yH)]));
        d.Both.Add(Lathe(R, [(hc, yk - 1.4 * k), (hc + 0.8, yk - 0.7 * k), (hc + 0.8, yk + 0.7 * k), (hc, yk + 1.4 * k)]));
        foreach (var s in new[] { -1.0, 1.0 }) d.Lines.Add([P(s * hc * 0.45, ys + 3.6 * k), P(s * hc * 0.45, yH - 1.2)]);
        d.Lines.Add([P(-hc - 0.6, yk), P(hc + 0.6, yk)]);
        switch (theme)
        {
            case TerrainThemes.Gothic:
            {
                // A tabernacle: a gabled box with pointed niches, pinnacles at its corners, a spire and a cross.
                double bw = Math.Max(nk + 0.6, 3.6 * k), y0 = yH + 1.4 * k, y1 = y0 + 6.0 * k, yA = H - 3.0 * k;
                d.Both.Add(Lathe(R, [(hc, yH - 0.5), (bw + 0.6, y0 - 0.2), (bw + 0.6, y0 + 0.8)]));
                d.Both.Add(Geo.Rect(R - bw, y0, R + bw, y1));
                foreach (var s in new[] { -1.0, 1.0 })
                {
                    d.Etched.Add(Fronts.PointedRect(R + s * bw * 0.5 - bw * 0.32, y0 + 1.2, R + s * bw * 0.5 + bw * 0.32, y1 - 0.8));
                    d.Both.Add(new PathD { P(s * (bw + 0.1) - 0.7, y1 - 1.0), P(s * (bw + 0.1) + 0.7, y1 - 1.0), P(s * (bw + 0.1) + 0.7, y1 + 1.8 * k), P(s * (bw + 0.1), y1 + 3.2 * k), P(s * (bw + 0.1) - 0.7, y1 + 1.8 * k) });
                }
                d.Both.Add(new PathD { P(-bw - 0.6, y1), P(bw + 0.6, y1), P(bw + 0.6, y1 + 0.9), P(0.7, yA), P(-0.7, yA), P(-bw - 0.6, y1 + 0.9) });
                d.Lines.Add([P(-bw * 0.5, y1 + 2.0), P(0, yA - 1.5), P(bw * 0.5, y1 + 2.0)]);
                d.OnlyB.Add(Geo.Rect(R - 0.65, yA - 0.5, R + 0.65, H));
                d.OnlyB.Add(Geo.Rect(R - 1.9, H - 2.4 * k, R + 1.9, H - 2.4 * k + 1.3));
                d.CapA = CapAt(d.Both, R, nk, y1, yA);
                break;
            }
            case TerrainThemes.Desert:
            {
                // A little kiosk: a keyhole arch on each face under an onion dome, a spike and a ball on top.
                double bw = Math.Max(nk + 0.6, 3.4 * k), y0 = yH + 1.4 * k, y1 = y0 + 5.4 * k, rb = Math.Max(0.8, 0.9 * k), yA = H - 2 * rb - 1.6 * k;
                d.Both.Add(Lathe(R, [(hc, yH - 0.5), (bw + 0.7, y0 - 0.2), (bw + 0.7, y0 + 0.8)]));
                d.Both.Add(Geo.Rect(R - bw, y0, R + bw, y1));
                d.Both.Add(Geo.Rect(R - bw - 0.6, y1 - 0.2, R + bw + 0.6, y1 + 0.9));
                d.Both.Add(Lathe(R, [(bw * 0.8, y1 + 0.8), (bw + 0.6, y1 + 2.6 * k), (Math.Max(nk, bw * 0.6), y1 + 4.6 * k), (nk, yA)]));
                d.Etched.Add(Fronts.ArchedRect(R - bw * 0.55, y0 + 0.9, R + bw * 0.55, y1 - 1.2));
                d.Lines.Add(Geo.Loop(Geo.Circle(R, y1 - 2.0 * k, bw * 0.45, 0.02)));
                d.OnlyB.Add(Lathe(R, [(0.65, yA - 0.6), (0.65, H - 2 * rb + 0.3)]));
                d.OnlyB.Add(Geo.Circle(R, H - rb, rb, 0.02));
                d.CapA = yA;
                break;
            }
            case TerrainThemes.Cave:
            {
                // A totem of three stacked faces, horns spreading from the top one.
                double fwid = hc + 1.4, fhgt = (H - 2.4 * k - yH) / 3;
                for (int i = 0; i < 3; i++)
                {
                    double y0 = yH - 0.2 + i * fhgt, y1 = y0 + fhgt;
                    d.Both.Add(Fronts.Chamfered(R - fwid + (i == 1 ? 0.5 : 0), y0, R + fwid - (i == 1 ? 0.5 : 0), y1 + 0.2, 0.8));
                    foreach (var s in new[] { -1.0, 1.0 }) d.Etched.Add(Rugs.Ellipse(R + s * fwid * 0.42, y0 + fhgt * 0.62, 0.55, 0.42));
                    d.Lines.Add([P(-fwid * 0.45, y0 + fhgt * 0.28), P(0, y0 + fhgt * 0.2), P(fwid * 0.45, y0 + fhgt * 0.28)]);
                    d.Lines.Add([P(0, y0 + fhgt * 0.55), P(0, y0 + fhgt * 0.38)]);
                    d.Lines.Add([P(-fwid + 0.3, y1), P(fwid - 0.3, y1)]);
                }
                double yt = yH - 0.2 + 3 * fhgt;
                foreach (var s in new[] { -1.0, 1.0 })
                    d.Both.Add(new PathD { P(s * (fwid - 1.6), yt - 0.4), P(s * (fwid + 1.0), yt + 0.4), P(s * (fwid + 2.4), H - 0.2), P(s * (fwid + 1.2), H - 0.6), P(s * (fwid - 0.2), yt + 1.4) });
                d.CapA = null;
                break;
            }
            case TerrainThemes.SciFi:
            {
                // A beacon: a tapered spire through a glowing ring, a light at its tip.
                double rr = Math.Max(nk + 1.6, 4.2 * k), yc = yH + 5.0 * k, rb = Math.Max(0.9, 1.0 * k);
                d.Both.Add(Lathe(R, [(hc + 0.8, yH - 0.5), (hc + 0.8, yH + 1.0), (Math.Max(nk, hc * 0.8), yH + 1.6), (nk, H - 2 * rb - 1.0)]));
                d.Both.AddRange(Annulus(R, yc, rr - 1.3, rr));
                d.Etched.AddRange(Geo.Subtract([Geo.Circle(R, yc, rr - 0.35, 0.02)], [Geo.Circle(R, yc, rr - 0.95, 0.02)]));
                foreach (var s in new[] { -1.0, 1.0 }) d.Lines.Add([P(s * (nk - 0.5), yH + 2.0), P(s * (nk - 0.5), H - 2 * rb - 1.6)]);
                d.OnlyB.Add(Geo.Rect(R - 0.65, H - 2 * rb - 1.4, R + 0.65, H - rb));
                d.OnlyB.Add(Geo.Circle(R, H - rb, rb, 0.02));
                d.CapA = H - 2 * rb - 1.0;
                break;
            }
            default:
            {
                // A wheel-head cross (broken, in a dungeon, with a chain and shackle hanging from its arm).
                bool broken = theme == TerrainThemes.Dungeon;
                double rr = Math.Max(hc + 2.6, 4.4 * k), aw = Math.Max(rr + 1.6, 5.2 * k), ah = Math.Max(2.4, 2.6 * k), yc = H - rr - 1.8 * k;
                d.Both.Add(Lathe(R, [(hc, yH - 0.5), (hc + 0.7, yH + 0.4), (hc + 0.7, yH + 1.4), (hc, yH + 2.0), (hc, H)]));
                if (!broken)
                {
                    d.Both.Add(Geo.Rect(R - aw, yc - ah / 2, R + aw, yc + ah / 2));
                    d.Both.AddRange(Annulus(R, yc, rr - 1.3, rr));
                    foreach (var s in new[] { -1.0, 1.0 }) d.Lines.Add([P(s * (aw - 0.7), yc - ah / 2 + 0.5), P(s * (aw - 0.7), yc + ah / 2 - 0.5)]);
                    d.Lines.Add(Geo.Loop(Geo.Circle(R, yc, Math.Min(hc, ah / 2) * 0.7, 0.02)));
                    d.Lines.Add([P(-hc + 0.5, H - 0.6), P(hc - 0.5, H - 0.6)]);
                    d.CapA = H;
                }
                else
                {
                    // Snapped off at the top, the right arm gone, the stump jagged.
                    d.Both.Add(Geo.Rect(R - aw, yc - ah / 2, R + hc, yc + ah / 2));
                    var stub = new PathD { P(hc - 0.1, yc - ah / 2), P(hc + 1.6, yc - ah / 2), P(hc + 1.1, yc), P(hc + 1.8, yc + ah / 2), P(hc - 0.1, yc + ah / 2) };
                    d.Both.Add(stub);
                    double yb2 = H - 2.2 * k;
                    var cut = new PathD { P(-hc - 1, yb2), P(-hc * 0.4, yb2 + 1.4 * k), P(0.2, yb2 + 0.4 * k), P(hc * 0.6, yb2 + 1.8 * k), P(hc + 1, yb2 + 0.8 * k), P(hc + 1, H + 5), P(-hc - 1, H + 5) };
                    d.Both = Geo.Subtract(Geo.Solid(d.Both), [cut]);
                    // The chain down from the left arm, links lined, a shackle at its end.
                    double xc = -aw + 1.4, yl = yc - ah / 2, ylo = Math.Max(yH + 1.0, yl - 6.0 * k);
                    d.Both.Add(Geo.Rect(R + xc - 0.6, ylo + 1.5, R + xc + 0.6, yl + 0.1));
                    for (double y = ylo + 2.0; y < yl - 0.4; y += 0.9) d.Lines.Add(Geo.Loop(Rugs.Ellipse(R + xc, y + 0.45, 0.3, 0.42)));
                    d.Both.AddRange(Annulus(R + xc, ylo + 0.6, 0.45, 1.6));
                    d.CapA = null;
                }
                break;
            }
        }
        d.Into(A, B);
        b.Finish();
        m.Note("Slide the two halves of the shaft together by their slots, then thread the steps onto its foot from below, the smallest first, till the top one meets the socle.");
    }
}
