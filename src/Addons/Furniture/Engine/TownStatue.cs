using Clipper2Lib;
using PhotonCanvas.Addon.Terrain;

namespace PhotonCanvas.Addon.Furniture;

public static partial class TownWalls
{
    // ---- the statue ----

    /// <summary>
    /// A statue on a pedestal: a plinth, four panelled sides (an inscription on the front, the theme's
    /// symbol on the sides) under a cornice slab, and on it a figure of two profiles halved together at the
    /// hips: the front slotted up between the legs, the side down to the hips and stopping at the shoulders,
    /// with the head made whole by glued pieces in front and behind. A knight leaning on his sword with a
    /// kite shield (medieval), a winged angel with a halo and a sword (gothic), a robed spearman with a
    /// turban and a round shield (desert), a horned warlord with a great axe (dungeon), a horned stone idol
    /// (cave) or a trooper in power armour with a rifle across his chest (sci-fi).
    /// </summary>
    static void Statue(Maker m)
    {
        var it = m.Item;
        double T = m.T, W = it.WidthMm, D = it.DepthMm, H = it.HeightMm, sw = m.SlotW;
        string theme = m.Theme ?? TerrainThemes.Medieval;
        bool glue = !m.PushFit, metal = theme == TerrainThemes.SciFi;
        double yp = Math.Round(H * 0.3, 1), s = (H - yp) / 31, px = 1.6;
        double needW = 2 * (7.8 * s + 0.6), needD = 2 * (5.0 * s + 0.6);
        if (s < 0.85 || W < needW || D < needD)
        {
            double s0 = Math.Max(0.85, s);
            m.Bad($"The statue is too small: make it at least {Math.Ceiling(2 * (7.8 * s0 + 0.6)):0} wide, {Math.Ceiling(2 * (5.0 * s0 + 0.6)):0} deep and {Math.Ceiling(31 * 0.85 / 0.7):0} mm high.");
            return;
        }
        var look = LookOf(theme);
        var b = new Build(m, theme == TerrainThemes.Cave ? look with { Face = "Stone blocks", A = 6, B = 4, G = 0.9 } : look, H / 46);
        double cx = W / 2, cz = D / 2;
        // The pedestal: a plinth, four sides and the slab on top.
        var plinth = new Flat { Name = "Plinth", Y = T, Texture = "none" };
        plinth.Outline.Add(Fronts.Chamfered(0, -D, W, 0, 1.0));
        plinth.Lines.Add(Geo.Loop(Fronts.Chamfered(0.6, -D + 0.6, W - 0.6, -0.6, 0.6)));
        b.Flats.Add(plinth);
        var slab = new Flat { Name = "Pedestal top", Y = yp, Texture = "none" };
        slab.Outline.Add(Fronts.Chamfered(px - 1.0, -(D - px + 1.0), W - px + 1.0, -(px - 1.0), 0.6));
        slab.Lines.Add(Geo.Loop(Fronts.Chamfered(px - 0.4, -(D - px + 0.4), W - px + 0.4, -(px - 0.4), 0.4)));
        b.Flats.Add(slab);
        if (glue)
        {
            var ring = new Flat { Name = "Base moulding", Y = 2 * T, Texture = "none", Glued = true };
            ring.Outline.Add(Build.XZRect(px - 1.2, px - 1.2, W - px + 1.2, D - px + 1.2));
            ring.Holes.Add(Build.XZRect(px, px, W - px, D - px));
            b.Flats.Add(ring);
        }
        var sides = new[]
        {
            (b.Upright("Pedestal front", new V3(px, 0, D - px - T / 2), V3.UnitX, W - 2 * px, V3.UnitZ), true, 0),
            (b.Upright("Pedestal back", new V3(px, 0, px + T / 2), V3.UnitX, W - 2 * px, -V3.UnitZ), true, 1),
            (b.Upright("Pedestal side", new V3(px + T / 2, 0, px + T), V3.UnitZ, D - 2 * px - 2 * T, -V3.UnitX), false, 2),
            (b.Upright("Pedestal side", new V3(W - px - T / 2, 0, px + T), V3.UnitZ, D - 2 * px - 2 * T, V3.UnitX), false, 3),
        };
        foreach (var (p, wide, i) in sides)
        {
            p.Plain = theme != TerrainThemes.Cave;
            double L = p.Len, y0 = T, y1 = yp - T;
            p.Outline.Add(Geo.Rect(0, y0, L, y1));
            foreach (var (t0, t1) in b.Tabs(0, L, wide ? T + 1.4 : 1.2)) { b.DownInto(plinth, p, t0, t1); b.UpInto(slab, p, t0, t1); }
            // Mouldings along the top and foot, a sunk panel between.
            p.Lines.Add([new(0.2, y1 - 0.9), new(L - 0.2, y1 - 0.9)]);
            p.Lines.Add([new(0.2, y0 + 1.0), new(L - 0.2, y0 + 1.0)]);
            double inset = wide ? 1.6 + T : 1.4, py0 = y0 + 1.9, py1 = y1 - 1.8;
            if (py1 - py0 < 3 || L - 2 * inset < 3) continue;
            if (metal) p.Etched.AddRange(Hazard(0.4, y0 + 0.2, L - 0.4, y0 + 0.8, 0.8));
            if (i == 0)
            {
                // The inscription on a plaque.
                p.Etched.AddRange(Fronts.Outline(inset, py0, L - inset, py1, 0.35));
                if (metal) p.Etched.Add(Geo.Rect(inset + 0.8, (py0 + py1) / 2 - 0.4, L - inset - 0.8, (py0 + py1) / 2 + 0.4));
                else
                    for (double y = py1 - 1.4; y > py0 + 0.8; y -= 1.3)
                    {
                        double x = inset + 1.0;
                        while (x < L - inset - 1.2)
                        {
                            int n = 2 + m.Rnd.Next(4);
                            for (int j = 0; j < n && x < L - inset - 1.2; j++, x += 0.4) p.Lines.Add([new(x, y - 0.35), new(x, y + 0.25 + 0.15 * m.Rnd.NextDouble())]);
                            x += 0.7;
                        }
                    }
            }
            else
            {
                p.Etched.AddRange(Fronts.Outline(inset, py0, L - inset, py1, 0.35));
                if (i >= 2) Cabinets.Symbol(Fronts.Look(m), theme, L / 2, (py0 + py1) / 2, Math.Min(L - 2 * inset, py1 - py0) * 0.3, p.Etched, p.Lines);
            }
        }
        // The figure, drawn in units of s from its feet on the slab: t across (to its left as you look at
        // it), w front to back (forwards positive).
        double hA = W / 2 - 0.6, hB = D / 2 - 0.6, ym = yp + 12 * s;
        var (A, B) = Crossed(b, "Statue", cx, cz, hA, hB, ym, aFromTop: false);
        PointD PA(double t, double y) => new(hA + t * s, yp + y * s);
        PointD PB(double w, double y) => new(hB - w * s, yp + y * s);
        PathD MA(PathD p) => Geo.Map(p, q => PA(q.x, q.y));
        PathD MB(PathD p) => Geo.Map(p, q => PB(q.x, q.y));
        PathD Poly(params (double X, double Y)[] p) => [.. p.Select(q => new PointD(q.X, q.Y))];
        PathD Mirror(PathD p) => Clipper.ReversePath([.. p.Select(q => new PointD(-q.x, q.y))]);
        void FA(params PathD[] ps) { foreach (var p in ps) A.Outline.Add(MA(p)); }
        void FB(params PathD[] ps) { foreach (var p in ps) B.Outline.Add(MB(p)); }
        void Both(PathD p) { FA(p); FA(Mirror(p)); }
        void LA(params (double X, double Y)[] p) => A.Lines.Add(MA(Poly(p)));
        void LB(params (double X, double Y)[] p) => B.Lines.Add(MB(Poly(p)));
        void EA(PathD p) => A.Etched.Add(MA(p));
        // The head made whole by glued pieces in front and behind: its middle height, how far it reaches
        // forwards and back, and its half height.
        double hy = 26.4, hf = 2.4, hb = 2.4, hh = 2.2;
        bool backTab = true, visor = false;
        // Lines on the plates of armoured legs, seen from the front and the side.
        void ArmouredLegs()
        {
            Both(Poly((-3.7, 0), (-0.2, 0), (-0.2, 1.0), (-3.7, 1.0)));
            Both(Poly((-3.3, 1.0), (-0.2, 1.0), (-0.2, 12.6), (-3.0, 12.6), (-3.4, 7.0), (-3.0, 6.0)));
            foreach (var g in new[] { -1.0, 1.0 })
            {
                LA((g * 1.2, 6.0), (g * 3.0, 6.0));
                LA((g * 1.2, 7.0), (g * 3.4, 7.0));
                LA((g * 1.2, 1.5), (g * 3.3, 1.5));
                A.Lines.Add(WoodFace.Dot(PA(g * 2.2, 6.5).x, PA(g * 2.2, 6.5).y, 0.22));
            }
            FB(Poly((-2.0, 0), (3.7, 0), (3.7, 0.7), (2.8, 1.4), (-1.6, 1.6), (-2.0, 1.0)));
            FB(Poly((-1.6, 1.4), (1.4, 1.4), (1.5, 6.4), (2.0, 7.0), (1.6, 8.0), (1.7, 12.6), (-1.8, 12.6), (-1.9, 7.0), (-1.6, 6.0)));
            LB((-1.5, 1.6), (1.4, 1.6));
            LB((-1.8, 7.0), (2.0, 7.0));
        }
        // A robe falling to the ground, with folds.
        void Robe()
        {
            Both(Poly((-3.8, 0), (0, 0), (0, 14.6), (-2.9, 14.6), (-3.2, 8.0)));
            foreach (var t in new[] { -2.6, -1.6, 1.6, 2.6 }) LA((t, 0.6), (t * 0.85, 9.0));
            FB(Poly((-3.7, 0), (3.7, 0), (2.7, 8.0), (2.4, 14.6), (-2.4, 14.6), (-2.8, 8.0)));
            foreach (var w in new[] { -2.4, 0.0, 2.2 }) LB((w, 0.6), (w * 0.8, 8.0));
        }
        // A plain torso from the hips to the shoulders.
        void Torso(double sh)
        {
            Both(Poly((-3.0, 14.4), (0, 14.4), (0, 23.4), (-1.9, 23.4), (-sh, 22.0), (-3.3, 19.6)));
            FA(Geo.Rect(-1.2, 23.2, 1.2, 24.8));
            FB(Poly((-2.4, 14.4), (2.5, 14.4), (2.9, 18.5), (2.7, 21.5), (1.9, 23.4), (-1.9, 23.4), (-2.5, 21.0), (-2.4, 17.5)));
        }
        switch (theme)
        {
            case TerrainThemes.Gothic:
            {
                // An angel: wings spread behind, a halo round the head, a sword point-down at its side.
                Robe();
                Torso(3.4);
                var wing = Poly((-2.4, 22.6), (-4.2, 25.4), (-6.0, 27.4), (-6.9, 27.8), (-6.6, 24.6), (-6.9, 21.0), (-6.4, 17.6), (-6.3, 13.8), (-5.2, 15.6), (-4.6, 13.2), (-3.8, 16.2), (-3.0, 18.6));
                Both(wing);
                foreach (var g in new[] { -1.0, 1.0 })
                    for (int i = 0; i < 4; i++)
                        LA((g * 3.2, 20.5 - i * 1.2), (g * (5.8 - i * 0.2), 26.2 - i * 3.0));
                FA(Fronts.ArchedRect(-2.1, 23.9, 2.1, 28.2));
                foreach (var q in Annulus(0, 26.6, 2.0, 3.3)) FA(q);
                // The sword, its hand on the hilt.
                FA(Poly((3.0, 21.4), (4.6, 21.0), (5.0, 17.4), (4.9, 13.8), (3.6, 13.8), (3.4, 17.4)));
                FA(Geo.Circle(4.3, 13.6, 0.95, 0.02), Geo.Rect(3.75, 0, 4.85, 11.4), Geo.Rect(2.9, 11.2, 5.9, 12.4), Geo.Rect(3.7, 12.3, 4.9, 13.1));
                LA((4.3, 1.2), (4.3, 10.8));
                LA((-2.6, 19.0), (-0.6, 18.2), (1.2, 19.4));
                foreach (var g in new[] { -1.0, 1.0 }) EA(Rugs.Ellipse(g * 0.7, 26.4, 0.3, 0.18));
                EA(Geo.Rect(-2.9, 14.4, 2.9, 15.0));
                FB(Poly((-2.0, 23.2), (-3.0, 24.8), (-4.4, 26.6), (-4.8, 24.0), (-4.7, 16.0), (-3.4, 14.2), (-2.2, 16.0)));
                hy = 26.2; hf = 2.0; hb = 2.6; hh = 2.1;
                break;
            }
            case TerrainThemes.Desert:
            {
                // A spearman in robes and a turban, a round shield on his arm.
                Robe();
                Torso(3.6);
                FA(Rugs.Ellipse(0, 25.6, 1.7, 1.9), Rugs.Ellipse(0, 27.6, 2.5, 1.6), Poly((-0.8, 28.8), (0.8, 28.8), (0, 30.4)));
                LA((-2.2, 27.2), (0, 28.4), (2.2, 27.2));
                LA((-2.3, 28.0), (0, 26.8), (2.3, 28.0));
                FA(Poly((3.0, 21.4), (4.8, 21.0), (5.4, 18.6), (5.6, 16.0), (4.3, 15.8), (3.8, 18.4)));
                FA(Geo.Circle(5.0, 16.2, 0.95, 0.02), Geo.Rect(4.45, 0, 5.55, 28.6), Poly((4.1, 28.4), (5.9, 28.4), (5.0, 31.0)));
                FA(Poly((-3.0, 21.4), (-4.6, 21.0), (-4.9, 18.6), (-3.6, 18.4)), Geo.Circle(-4.4, 16.6, 2.9, 0.02));
                A.Lines.Add(Geo.Loop(MA(Geo.Circle(-4.4, 16.6, 2.3, 0.02))));
                EA(Geo.Circle(-4.4, 16.6, 0.8, 0.02));
                LA((-2.8, 21.2), (2.6, 14.8));
                LA((-2.6, 21.6), (2.9, 15.2));
                foreach (var g in new[] { -1.0, 1.0 }) EA(Rugs.Ellipse(g * 0.6, 25.8, 0.28, 0.16));
                FB(Geo.Rect(2.2, 13.6, 3.4, 19.4));
                hy = 26.6; hf = 2.2; hb = 2.2; hh = 2.6;
                break;
            }
            case TerrainThemes.Dungeon:
            {
                // A horned warlord in a cloak, a great axe in his right hand, his left a fist.
                FA(Poly((-4.6, 1.0), (4.6, 1.0), (4.0, 21.6), (-4.0, 21.6)));
                ArmouredLegs();
                Both(Poly((-3.8, 8.6), (0, 8.6), (0, 15.0), (-3.3, 15.0)));
                for (double y = 9.4; y < 14.8; y += 1.0)
                    for (double t = -3.2; t < 3.2; t += 1.0) A.Lines.Add([.. Geo.Arc(PA(t, y).x, PA(t, y).y, 0.45 * s, Math.PI, 2 * Math.PI)]);
                Torso(3.8);
                foreach (var g in new[] { -1.0, 1.0 })
                {
                    FA(Rugs.Ellipse(g * 4.1, 21.8, 2.1, 1.5));
                    FA(Poly((g * 3.6, 22.6), (g * 5.4, 22.6), (g * 4.8, 25.0)));
                }
                FA(Fronts.ArchedRect(-2.2, 24.4, 2.2, 28.4));
                var horn = Poly((1.9, 26.4), (3.4, 27.2), (4.3, 28.9), (4.4, 30.4), (3.3, 30.0), (3.0, 28.4), (1.9, 28.1));
                FA(horn, Mirror(horn));
                EA(Geo.Rect(-1.6, 26.6, 1.6, 27.1));
                FA(Poly((3.2, 21.4), (5.0, 21.0), (5.6, 17.4), (5.8, 14.6), (4.3, 14.4), (3.8, 17.4)));
                FA(Geo.Circle(5.15, 14.8, 1.0, 0.02), Geo.Rect(4.6, 0, 5.7, 24.0), Poly((4.35, 23.9), (5.95, 23.9), (5.15, 26.2)));
                FA(Poly((5.6, 18.4), (7.0, 17.4), (7.7, 20.0), (7.0, 22.6), (5.6, 21.6)));
                LA((6.9, 17.8), (7.3, 20.0), (6.9, 22.2));
                FA(Poly((-3.2, 21.4), (-5.0, 21.0), (-5.6, 17.6), (-4.4, 15.2), (-3.4, 16.4)), Geo.Circle(-4.6, 15.6, 1.0, 0.02));
                Cabinets.Symbol(FrontLook.Plank, theme, PA(0, 19.0).x, PA(0, 19.0).y, 1.5 * s, A.Etched, A.Lines);
                FB(Poly((-1.9, 23.2), (-2.9, 22.4), (-3.5, 14.5), (-4.0, 4.0), (-4.4, 0), (-1.8, 0), (-1.8, 12.6)));
                FB(Poly((-2.6, 8.6), (2.7, 8.6), (2.5, 15.0), (-2.4, 15.0)));
                hy = 26.4; hf = 2.6; hb = 2.4; hh = 2.0; visor = true;
                break;
            }
            case TerrainThemes.Cave:
            {
                // A squat stone idol: a big belly, arms folded over it, a huge head with horns.
                Both(Poly((-4.2, 0), (0, 0), (0, 7.6), (-4.0, 7.6)));
                FA(Rugs.Ellipse(0, 13.0, 5.0, 6.0));
                foreach (var g in new[] { -1.0, 1.0 }) FA(Rugs.Ellipse(g * 4.8, 15.0, 1.4, 3.6));
                FA(WoodFace.RoundRect(-4.0, 18.4, 4.0, 27.0, 1.6));
                var horn = Poly((3.2, 25.6), (5.2, 26.8), (6.4, 29.6), (5.6, 30.4), (4.6, 28.2), (3.0, 27.0));
                FA(horn, Mirror(horn));
                foreach (var g in new[] { -1.0, 1.0 })
                {
                    EA(Rugs.Ellipse(g * 1.6, 23.8, 0.9, 0.7));
                    LA((g * 0.4, 22.2), (g * 0.9, 21.2));
                    LA((g * 3.4, 15.8), (g * 0.4, 14.6));
                }
                LA((-3.0, 25.4), (0, 24.8), (3.0, 25.4));
                EA(Geo.Rect(-2.0, 19.6, 2.0, 20.6));
                for (double t = -1.6; t < 1.8; t += 0.8) LA((t, 19.6), (t, 20.6));
                LA((-3.6, 7.6), (3.6, 7.6));
                Cabinets.Symbol(FrontLook.Plank, theme, PA(0, 10.8).x, PA(0, 10.8).y, 1.6 * s, A.Etched, A.Lines);
                FB(Poly((-3.6, 0), (3.6, 0), (3.2, 7.6), (-3.0, 7.6)));
                FB(Rugs.Ellipse(0.6, 13.0, 3.8, 5.8));
                hy = 22.8; hf = 3.0; hb = 2.6; hh = 4.0;
                break;
            }
            case TerrainThemes.SciFi:
            {
                // A trooper in power armour, a rifle across his chest.
                Both(Poly((-4.0, 0), (0, 0), (0, 1.6), (-4.0, 1.6)));
                Both(Poly((-3.6, 1.4), (0, 1.4), (0, 12.6), (-3.5, 12.6), (-3.9, 7.4), (-3.4, 6.0)));
                FA(Fronts.Chamfered(-4.0, 11.0, 4.0, 14.6, 0.8), Fronts.Chamfered(-3.8, 14.2, 3.8, 22.6, 1.2), Geo.Rect(-3.2, 21.8, 3.2, 24.6));
                foreach (var g in new[] { -1.0, 1.0 })
                {
                    FA(Fronts.Chamfered(Math.Min(g * 3.0, g * 6.0), 20.2, Math.Max(g * 3.0, g * 6.0), 23.8, 1.0));
                    FA(Poly((g * 3.4, 20.6), (g * 5.4, 20.4), (g * 5.6, 16.6), (g * 4.2, 16.0)));
                    LA((g * 1.0, 6.7), (g * 3.6, 6.7));
                    LA((g * 3.2, 21.0), (g * 5.8, 21.0));
                }
                FA(Geo.Strip(new(5.4, 14.6), new(-5.2, 22.0), 1.7), Fronts.Chamfered(-0.6, 15.0, 1.4, 17.6, 0.3), Geo.Rect(4.0, 13.8, 6.0, 15.6));
                FA(WoodFace.RoundRect(-2.4, 23.6, 2.4, 28.6, 2.0), Geo.Rect(2.1, 25.4, 3.3, 27.4));
                EA(Geo.Rect(-1.9, 26.0, 1.9, 27.2));
                LA((-3.6, 18.0), (3.6, 18.0));
                LA((-2.0, 12.4), (2.0, 12.4));
                Cabinets.Symbol(FrontLook.Metal, theme, PA(-2.6, 13.0).x, PA(-2.6, 13.0).y, 1.0 * s, A.Etched, A.Lines);
                FB(Poly((-2.2, 0), (3.8, 0), (3.8, 1.6), (-2.2, 1.6)));
                FB(Poly((-1.6, 1.4), (1.4, 1.4), (1.6, 6.4), (2.2, 7.4), (1.8, 8.4), (1.8, 12.6), (-1.8, 12.6), (-1.9, 7.0), (-1.6, 6.0)));
                FB(Poly((-2.6, 11.0), (2.6, 11.0), (2.8, 18.5), (3.0, 22.6), (-2.6, 22.6)));
                FB(Fronts.Chamfered(-5.0, 15.0, -2.2, 23.6, 0.8), Geo.Rect(2.6, 15.0, 3.8, 21.0));
                LB((-4.6, 19.0), (-2.4, 19.0));
                hy = 26.1; hf = 2.6; hb = 2.6; hh = 2.4; backTab = false; visor = true;
                break;
            }
            default:
            {
                // A knight leaning on his sword, a kite shield on his arm and a crest on his helm.
                ArmouredLegs();
                Both(Poly((-3.7, 8.8), (0, 8.8), (0, 15.2), (-3.1, 15.2)));
                Torso(3.8);
                foreach (var g in new[] { -1.0, 1.0 }) FA(Rugs.Ellipse(g * 4.0, 21.8, 2.0, 1.4));
                FA(Fronts.ArchedRect(-2.2, 24.4, 2.2, 28.8), Geo.Rect(-0.7, 28.0, 0.7, 29.6), Rugs.Ellipse(0.5, 29.8, 1.7, 1.1));
                EA(Geo.Rect(-1.6, 26.8, 1.6, 27.3));
                FA(Poly((3.2, 21.4), (5.2, 21.2), (5.7, 17.4), (5.6, 13.6), (3.9, 13.6), (3.7, 17.4)));
                FA(Geo.Circle(4.9, 13.4, 1.0, 0.02), Geo.Rect(4.4, 0, 5.5, 11.2), Geo.Rect(3.3, 11.0, 6.5, 12.2), Geo.Rect(4.3, 12.0, 5.5, 13.0));
                LA((4.95, 1.2), (4.95, 10.6));
                FA(Poly((-3.2, 21.4), (-5.0, 21.0), (-5.4, 18.0), (-3.6, 18.0)));
                var shield = Poly((-6.6, 19.8), (-2.0, 19.8), (-2.0, 13.5), (-4.3, 8.4), (-6.6, 13.5));
                FA(shield);
                A.Lines.Add(Geo.Loop(MA(Geo.Offset([shield], -0.5).FirstOrDefault() ?? shield)));
                Cabinets.Symbol(Fronts.Look(m), theme, PA(-4.3, 15.6).x, PA(-4.3, 15.6).y, 1.5 * s, A.Etched, A.Lines);
                EA(Geo.Rect(-3.2, 14.6, 3.2, 15.4));
                FB(Poly((-2.6, 8.8), (2.7, 8.8), (2.5, 15.2), (-2.4, 15.2)));
                FB(Geo.Rect(2.4, 10.0, 3.5, 19.8));
                FB(Poly((-1.9, 23.2), (-2.9, 22.4), (-3.5, 14.5), (-4.0, 4.0), (-4.4, 0), (-1.8, 0), (-1.8, 12.6)));
                LB((-2.4, 15.0), (-3.8, 3.0));
                visor = true;
                break;
            }
        }
        if (theme is not (TerrainThemes.Cave or TerrainThemes.SciFi))
        {
            FB(Rugs.Ellipse(0, 21.8, 2.5, 1.5));
            B.Lines.Add(Geo.Loop(MB(Rugs.Ellipse(0, 21.8, 1.9, 1.0))));
        }
        // Into the slab: a tab under each foot of the front, one under the toes of the side and (if the robe or
        // cloak reaches the ground behind) one at the back.
        double l0 = sw / 2 + 0.6, l1 = sw / 2 + 2.0;
        foreach (var g in new[] { -1.0, 1.0 }) b.DownInto(slab, A, hA + Math.Min(g * l0, g * l1), hA + Math.Max(g * l0, g * l1));
        double z0 = sw / 2 + 1.25, z1 = sw / 2 + 2.45;
        b.DownInto(slab, B, hB - z1, hB - z0);
        if (backTab) b.DownInto(slab, B, hB + z0, hB + z1);
        // Glued, the head is made whole in front and behind.
        if (glue)
        {
            double y0 = yp + (hy - hh) * s, y1 = yp + (hy + hh) * s;
            foreach (var (front, reach) in new[] { (true, hf), (false, hb) })
            {
                double r = Math.Max(reach * s, T / 2 + 1.6), za = cz + (front ? T / 2 : -T / 2), zb = cz + (front ? r : -r);
                var g = b.Upright(front ? "Face" : "Back of the head", new V3(cx, 0, Math.Min(za, zb)), V3.UnitZ, Math.Abs(zb - za), V3.UnitX);
                g.Glued = g.Plain = true;
                var ga = new V3(cx, 0, Math.Min(za, zb));
                var head = Rugs.Ellipse(UZ(g, ga, cz), (y0 + y1) / 2, r, (y1 - y0) / 2);
                double ua = UZ(g, ga, za), ub = UZ(g, ga, zb);
                g.Outline.AddRange(Geo.Intersect([head], [Geo.Rect(Math.Min(ua, ub), y0 - 1, Math.Max(ua, ub), y1 + 1)]));
                if (front && visor) g.Etched.Add(Geo.Rect(Math.Min(ua, ub), (y0 + y1) / 2, Math.Max(ua, ub), (y0 + y1) / 2 + 0.5 * s));
            }
        }
        b.Finish();
        m.Note("Push the pedestal's sides into the plinth and the top onto them. Slide the side of the figure down into the front, crossing at the hips, and stand it in the top" + (glue ? "; glue the pedestal onto its moulding and the face and back of the head either side of the head." : "."));
    }
}
