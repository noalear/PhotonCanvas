using Clipper2Lib;
using PhotonCanvas.Addon.Terrain;

namespace PhotonCanvas.Addon.Furniture;

/// <summary>
/// The armoury: a long weapon rack (polearms standing through a holder, swords point-down in a rest in
/// front, posts with the theme's finials) and a suit of plate armour on a plinth, built as two profiles
/// halved together so it stands round, with the theme's helm.
/// </summary>
public static partial class TownWalls
{
    // ---- the weapon rack ----

    /// <summary>
    /// A weapon rack: two posts on sled feet, a holder between them that the polearms (spears, halberds and
    /// axes) stand through, a rest in front for swords standing point-down on their crossguards, iron bands
    /// round the joints and the theme's finial on each post.
    /// </summary>
    public static void Armoury(Maker m)
    {
        var it = m.Item;
        double T = m.T, F = m.Fit, W = it.WidthMm, D = it.DepthMm, H = it.HeightMm, k = H / 30, sw = T + F;
        string theme = m.Theme ?? TerrainThemes.Medieval;
        double xs0 = 1.2 + T / 2, xs1 = W - 1.2 - T / 2, xi0 = xs0 + T / 2 + 0.02, xi1 = xs1 - T / 2 - 0.02, Li = xi1 - xi0;
        double sp = Math.Max(4.6, 4.8 * k);
        int n = Math.Min(Geo.Clamp(it.Rows, 1, 12), (int)Math.Floor((Li - 1.0) / sp));
        if (n < 1) { m.Bad($"The weapon rack is too narrow: make it at least {Math.Ceiling(2 * (1.2 + T) + 1 + sp):0} mm wide."); return; }
        if (D < sw + 4.8) { m.Bad($"The weapon rack is too shallow: make it at least {Math.Ceiling(sw + 4.8):0} mm deep."); return; }
        if (H < 20) { m.Bad("The weapon rack is too low: make it at least 20 mm high."); return; }
        var b = new Build(m, LookOf(theme) with { Face = "Wood planks (upright)", A = 2.6, B = 30, G = 0.4 }, k);
        // Polearms stand at the back through the holder; swords in front, if it's deep enough.
        double zp0 = 1.8 + sw / 2;
        bool swords = D >= zp0 + T / 2 + 0.4 + sw + 2.4 + 0.6 && Li >= 9;
        double zp = swords ? zp0 : D / 2;
        double zh1 = swords ? zp + sw / 2 + 1.2 : D - 0.6;
        double zr0 = zp + T / 2 + 0.4, zr1 = D - 0.6, zs = (zr0 + zr1) / 2;
        double yh = Math.Round(H * 0.6, 1), ys = Math.Round(Math.Max(T + 5, H * 0.3), 1);
        double pw = Math.Min(Math.Max(3.2, 3.4 * k), zh1 - 0.6);
        // The base: planks across.
        var base_ = new Flat { Name = "Base", Y = T, Texture = "Wood planks (across)", A = 3, B = 30, G = 0.4 };
        base_.Outline.Add(Fronts.Chamfered(0, -D, W, 0, 0.8));
        b.Flats.Add(base_);
        var holder = new Flat { Name = "Holder", Y = yh, Texture = "Wood planks (across)", A = 3, B = 30, G = 0.4 };
        holder.Outline.Add(Build.XZRect(xi0, 0.6, xi1, zh1));
        b.Flats.Add(holder);
        Flat? rest = null;
        if (swords)
        {
            rest = new Flat { Name = "Sword rest", Y = ys, Texture = "Wood planks (across)", A = 3, B = 30, G = 0.4 };
            rest.Outline.Add(Build.XZRect(xi0, zr0, xi1, zr1));
            b.Flats.Add(rest);
        }
        // The posts: a sled foot, a panel up to the sword rest, then a post up past the holder to its finial.
        double zq0 = 0.6, zq1 = 0.6 + pw, fin = Math.Max(2.6, 3.2 * k), ytop = H - fin;
        foreach (var (x, f) in new[] { (xs0, -V3.UnitX), (xs1, V3.UnitX) })
        {
            var a = new V3(x, 0, 0);
            var p = b.Upright("Post", a, V3.UnitZ, D, f);
            double zfront = swords ? zr1 : zh1, ylow = swords ? ys + 1.8 : T + 2.4;
            var pts = new List<(double, double)> { (0.6, T), (D - 0.6, T), (D - 0.6, T + 1.6), (zfront, T + 2.4) };
            if (swords) pts.AddRange([(zfront, ylow), (zh1, ylow + Math.Max(2.0, zfront - zh1))]);
            pts.AddRange([(zh1, yh + 1.4), (zq1, yh + 2.6), (zq1, ytop), (zq0, ytop), (zq0, T)]);
            p.Outline.Add(ZY(p, a, pts));
            p.Outline.Add(ZY(p, a, Finial(theme, zq0, zq1, ytop, H)));
            foreach (var (u0, u1) in TabsZ(b, p, a, 0.6, D - 0.6, 1.0)) b.DownInto(base_, p, u0, u1);
            foreach (var (u0, u1) in TabsZ(b, p, a, 0.6, zh1, 0.4)) b.FlatInto(holder, p, u0, u1);
            if (rest is not null) foreach (var (u0, u1) in TabsZ(b, p, a, zr0, zr1, 0.4)) b.FlatInto(rest, p, u0, u1);
            // Iron bands over the joints, studded.
            foreach (var (yb0, yb1, z1) in new[] { (yh - T - 0.8, yh + 0.9, zh1), (T + 0.2, T + 1.4, D - 0.6) })
            {
                double u0 = UZ(p, a, 0.6), u1 = UZ(p, a, z1);
                var band = Geo.Rect(Math.Min(u0, u1), yb0, Math.Max(u0, u1), yb1);
                p.Etched.Add(band);
                for (double z = 1.4; z < z1 - 0.4; z += 2.4) p.Lines.Add(WoodFace.Dot(UZ(p, a, z), (yb0 + yb1) / 2, 0.22));
            }
            if (rest is not null)
            {
                double u0 = UZ(p, a, zr0 - 0.4), u1 = UZ(p, a, zr1);
                p.Etched.Add(Geo.Rect(Math.Min(u0, u1), ys - T - 0.8, Math.Max(u0, u1), ys + 0.9));
            }
        }
        // The polearms: shafts through the holder (notched open at the back so they slide in), tabs into the base.
        double pitch = Li / n;
        for (int i = 0; i < n; i++)
        {
            double c = (i + 0.5) * pitch, x = xi0 + c, top = H - (i % 2 == 1 ? 1.6 * k : 0.2) - (i % 3 == 2 ? 1.2 * k : 0);
            var up = b.Upright(PolearmName(i), new V3(x, 0, zp), V3.UnitX, 0, V3.UnitZ);
            up.O = new V3(x, 0, zp);
            up.Plain = true;
            Polearm(i, theme, top, T, k, up.Outline, up.Etched, up.Lines);
            b.DownInto(base_, up, -0.6, 0.6);
            holder.Holes.Add(Build.XZRect(x - 0.6 - F / 2, -1, x + 0.6 + F / 2, zp + sw / 2));
        }
        // The swords, point-down through the rest, each tab into the base.
        if (rest is not null)
        {
            double ssp = Math.Max(5.0, 5.2 * k);
            int ns = Math.Max(1, (int)Math.Floor((Li - 1.0) / ssp));
            double sp2 = Li / ns;
            for (int i = 0; i < ns; i++)
            {
                double x = xi0 + (i + 0.5) * sp2;
                var up = b.Upright("Sword", new V3(x, 0, zs), V3.UnitX, 0, V3.UnitZ);
                up.O = new V3(x, 0, zs);
                up.Plain = true;
                double gl = Math.Max(3.0, 3.6 * k) + (i % 2) * 0.8;
                Sword(theme, T, ys, gl, k, up.Outline, up.Etched, up.Lines);
                b.DownInto(base_, up, -0.6, 0.6);
                rest.Holes.Add(Build.XZRect(x - 0.8 - F / 2, zs - sw / 2, x + 0.8 + F / 2, zs + sw / 2));
            }
        }
        b.Finish();
        m.Note("Slide the holder" + (swords ? " and the sword rest" : "") + " into the posts and push the posts into the base, slide each polearm into its notch and down into the base" + (swords ? ", then drop the swords point-first through the rest." : "."));
    }

    /// <summary>The finial on top of a post (z0–z1 wide, from y0 to y1), in the theme's style, as (z, y).</summary>
    static IEnumerable<(double, double)> Finial(string theme, double z0, double z1, double y0, double y1)
    {
        double c = (z0 + z1) / 2, w = z1 - z0, h = y1 - y0;
        switch (theme)
        {
            case TerrainThemes.Gothic:
                // A crocketed spire.
                return [(z0 - 0.3, y0 - 0.01), (z1 + 0.3, y0 - 0.01), (z1 + 0.3, y0 + 0.6), (c + w * 0.3, y0 + h * 0.45), (c + w * 0.4, y0 + h * 0.55), (c + 0.5, y1), (c - 0.5, y1), (c - w * 0.4, y0 + h * 0.55), (c - w * 0.3, y0 + h * 0.45), (z0 - 0.3, y0 + 0.6)];
            case TerrainThemes.Desert:
                // An onion dome.
                return [(z0, y0 - 0.01), (z1, y0 - 0.01), (z1 + 0.4, y0 + h * 0.35), (c + w * 0.25, y0 + h * 0.75), (c + 0.5, y1), (c - 0.5, y1), (c - w * 0.25, y0 + h * 0.75), (z0 - 0.4, y0 + h * 0.35)];
            case TerrainThemes.Cave:
                // A rough-cut log end.
                return [(z0, y0 - 0.01), (z1, y0 - 0.01), (z1, y0 + h * 0.6), (c + w * 0.2, y1), (c - w * 0.1, y0 + h * 0.75), (z0, y0 + h * 0.9)];
            case TerrainThemes.Dungeon:
                // An iron spike on a collar.
                return [(z0 - 0.4, y0 - 0.01), (z1 + 0.4, y0 - 0.01), (z1 + 0.4, y0 + 0.9), (c + 0.6, y0 + 1.1), (c + 0.5, y1), (c - 0.5, y1), (c - 0.6, y0 + 1.1), (z0 - 0.4, y0 + 0.9)];
            default:
                // A pyramid cap on a moulding.
                return [(z0 - 0.4, y0 - 0.01), (z1 + 0.4, y0 - 0.01), (z1 + 0.4, y0 + 0.8), (z1, y0 + 1.2), (c + 0.5, y1), (c - 0.5, y1), (z0, y0 + 1.2), (z0 - 0.4, y0 + 0.8)];
        }
    }

    static string PolearmName(int i) => (i % 4) switch { 1 => "Halberd", 3 => "Battle axe", _ => "Spear" };

    /// <summary>A polearm standing on its tab at u = 0: a spear, a halberd or a double-bladed battle axe.</summary>
    static void Polearm(int i, string theme, double top, double T, double k, PathsD outline, PathsD etched, List<PathD> lines)
    {
        double s = Math.Max(1, k), hw = 0.6;
        switch (i % 4)
        {
            case 1:
            {
                // Halberd: a spike on top, an axe blade one side and a back spike the other.
                double yb0 = top - 7.6 * s, yb1 = top - 3.8 * s;
                outline.Add(Geo.Rect(-hw, T, hw, top - 3.0 * s));
                outline.Add([new(-0.7, top - 3.4 * s), new(0.7, top - 3.4 * s), new(0.5, top), new(-0.5, top)]);
                var blade = new PathD { new(0.4, yb0 + 0.8) };
                for (int j = 0; j <= 8; j++) { double t = j / 8.0, a = -0.9 + 1.8 * t; blade.Add(new PointD(3.0 * s + 0.5 * Math.Cos(a) - 0.5, yb0 + (yb1 - yb0) * t)); }
                blade.Add(new PointD(0.4, yb1 - 0.4));
                outline.Add(blade);
                outline.Add([new(-0.4, top - 6.4 * s), new(-2.6 * s, top - 5.8 * s), new(-2.6 * s, top - 4.7 * s), new(-0.4, top - 4.6 * s)]);
                lines.Add([new(2.2 * s, yb0 + 1.0), new(2.5 * s, yb1 - 0.6)]);
                etched.Add(Geo.Rect(-0.75, yb0 - 1.6, 0.75, yb0 - 0.9));
                break;
            }
            case 3:
            {
                // Battle axe: two crescent blades and a short spike.
                double yb0 = top - 6.6 * s, yb1 = top - 1.6 * s;
                outline.Add(Geo.Rect(-hw, T, hw, top - 0.6));
                outline.Add([new(-0.6, top - 1.4), new(0.6, top - 1.4), new(0.5, top), new(-0.5, top)]);
                foreach (var sg in new[] { -1, 1 })
                {
                    var blade = new PathD { new(sg * 0.4, yb0 + 1.6) };
                    for (int j = 0; j <= 10; j++) { double t = j / 10.0, a = -1.0 + 2.0 * t; blade.Add(new PointD(sg * (2.3 * s + 0.8 * s * Math.Cos(a)), yb0 + (yb1 - yb0) * t)); }
                    blade.Add(new PointD(sg * 0.4, yb1 - 1.4));
                    outline.Add(sg < 0 ? Clipper.ReversePath(blade) : blade);
                    lines.Add([new(sg * 2.3 * s, yb0 + 1.0), new(sg * 2.6 * s, (yb0 + yb1) / 2), new(sg * 2.3 * s, yb1 - 1.0)]);
                }
                etched.Add(Geo.Rect(-0.75, yb0 - 0.6, 0.75, yb0 + 0.2));
                break;
            }
            default:
            {
                // Spear: a leaf blade on a socket.
                double hl = 4.6 * s, ys = top - hl;
                outline.Add(Geo.Rect(-hw, T, hw, ys - 0.6));
                outline.Add(Geo.Rect(-0.85, ys - 1.8, 0.85, ys + 0.1));
                outline.Add([new(-0.7, ys), new(0.7, ys), new(1.25 * s, ys + hl * 0.4), new(0.5, top), new(-0.5, top), new(-1.25 * s, ys + hl * 0.4)]);
                lines.Add([new(0, ys + 0.4), new(0, top - 0.8)]);
                lines.Add([new(-0.85, ys - 1.2), new(0.85, ys - 1.2)]);
                break;
            }
        }
        // Grip wraps low on the shaft.
        for (double y = T + 7 * s; y < T + 9.5 * s; y += 0.7) lines.Add([new(-hw, y), new(hw, y + 0.3)]);
    }

    /// <summary>A sword standing point-down at u = 0: the blade through the rest (ys), the crossguard on it, the grip and pommel above.</summary>
    static void Sword(string theme, double T, double ys, double gl, double k, PathsD outline, PathsD etched, List<PathD> lines)
    {
        double s = Math.Max(1, k), gw = 2.2 * s;
        outline.Add([new(-0.6, T), new(0.6, T), new(0.8, ys - 0.5), new(0.8, ys + 0.5), new(-0.8, ys + 0.5), new(-0.8, ys - 0.5)]);
        bool curved = theme == TerrainThemes.Desert;
        // The crossguard: curled ends, or swept down for a desert sword.
        if (curved) outline.Add([new(-gw, ys), new(-gw + 0.8, ys + 0.3), new(gw - 0.8, ys + 0.3), new(gw, ys), new(gw, ys + 1.4), new(-gw, ys + 1.4)]);
        else
        {
            outline.Add(Geo.Rect(-gw + 0.5, ys, gw - 0.5, ys + 1.0));
            outline.Add(Geo.Circle(-gw + 0.6, ys + 0.62, 0.6, 0.01));
            outline.Add(Geo.Circle(gw - 0.6, ys + 0.62, 0.6, 0.01));
        }
        outline.Add(Geo.Rect(-0.6, ys + 0.9, 0.6, ys + 1.0 + gl));
        double pr = theme == TerrainThemes.Gothic ? 0.95 : 0.85;
        outline.Add(Geo.Circle(0, ys + 1.0 + gl + pr * 0.7, pr, 0.01));
        etched.Add(Geo.Rect(-0.2, T + 1.6, 0.2, ys - T - 0.6));
        for (double y = ys + 1.5; y < ys + 0.8 + gl; y += 0.6) lines.Add([new(-0.6, y), new(0.6, y + 0.3)]);
        lines.Add(WoodFace.Dot(0, ys + 1.0 + gl + pr * 0.7, pr * 0.4));
    }

    // ---- the suit of armour ----

    /// <summary>
    /// A suit of plate armour on a plinth: a front profile (helm, pauldrons, arms, breastplate, tassets and
    /// legs) and a side profile (breast, tassets, knees and sabatons) halved together at the chest, the
    /// theme's helm and its symbol on the breastplate; glued, a visor and a back plate stand out of the helm.
    /// </summary>
    public static void ArmourSuit(Maker m)
    {
        var it = m.Item;
        double T = m.T, F = m.Fit, W = it.WidthMm, D = it.DepthMm, H = it.HeightMm, sw = T + F;
        string theme = m.Theme ?? TerrainThemes.Medieval;
        bool glue = !m.PushFit, sci = theme == TerrainThemes.SciFi;
        double yb = glue ? 2 * T : T;
        double s = Math.Min((H - yb) / 30, Math.Min(W / 11.6, D / 8.4));
        if (s < 0.7) { m.Bad($"The suit of armour is too small: make it at least {Math.Ceiling(11.6 * 0.7):0} wide, {Math.Ceiling(8.4 * 0.7):0} deep and {Math.Ceiling(30 * 0.7 + yb):0} mm high."); return; }
        var b = new Build(m, LookOf(theme), H / 32);
        double c = W / 2, cz = D / 2;
        PointD P(double x, double y) => new(c + x * s, yb + y * s);
        PathD Poly(params (double X, double Y)[] p) => [.. p.Select(q => P(q.X, q.Y))];
        PathD Mirror(PathD p) => Clipper.ReversePath([.. p.Select(q => new PointD(2 * c - q.x, q.y))]);
        // The plinth.
        var plinth = new Flat { Name = "Plinth", Y = yb, Texture = "none" };
        plinth.Outline.Add(Fronts.Chamfered(0.3, -(D - 0.3), W - 0.3, -0.3, 1.0));
        plinth.Lines.Add(Geo.Loop(Fronts.Chamfered(1.2, -(D - 1.2), W - 1.2, -1.2, 0.7)));
        b.Flats.Add(plinth);
        if (glue)
        {
            var step = new Flat { Name = "Plinth base", Y = T, Texture = "none", Glued = true };
            step.Outline.Add(Fronts.Chamfered(0, -D, W, 0, 1.2));
            if (sci) step.Etched.AddRange(Hazard(0.4, -D + 0.3, W - 0.4, -D + 1.0, 0.7));
            b.Flats.Add(step);
        }
        double ym = yb + 18 * s;
        // The front.
        var front = b.Upright("Armour", new V3(0, 0, cz), V3.UnitX, W, V3.UnitZ);
        front.Plain = true;
        var fo = front.Outline;
        var leg = Poly((0.9, 0), (3.2, 0), (3.2, 0.9), (2.7, 1.5), (2.6, 6.2), (3.0, 7.0), (2.7, 8.0), (2.9, 12.6), (0.9, 12.6));
        fo.Add(leg);
        fo.Add(Mirror(leg));
        fo.Add(Poly((-3.3, 11.0), (3.3, 11.0), (2.8, 15.6), (-2.8, 15.6)));
        fo.Add(Poly((-2.8, 15.4), (2.8, 15.4), (3.3, 19.8), (3.5, 22.2), (1.8, 23.6), (-1.8, 23.6), (-3.5, 22.2), (-3.3, 19.8)));
        var arm = sci
            ? Poly((3.0, 22.0), (5.0, 21.6), (5.4, 17.8), (5.2, 14.0), (5.6, 13.4), (5.4, 11.4), (3.4, 11.4), (3.2, 13.4), (3.0, 17.0))
            : Poly((3.0, 21.6), (4.9, 21.2), (5.2, 17.8), (5.0, 17.0), (4.9, 14.0), (5.3, 13.4), (5.1, 11.6), (3.4, 11.6), (3.2, 13.4), (3.0, 17.0));
        fo.Add(arm);
        fo.Add(Mirror(arm));
        foreach (var sg in new[] { -1, 1 })
            fo.Add(sci ? Fronts.Chamfered(c + sg * 3.9 * s - 2.0 * s, yb + 20.6 * s, c + sg * 3.9 * s + 2.0 * s, yb + 23.4 * s, 0.8 * s)
                       : Rugs.Ellipse(c + sg * 3.9 * s, yb + 22.0 * s, 1.9 * s, 1.4 * s));
        fo.Add(Poly((-1.9, 23.3), (1.9, 23.3), (1.9, 25.0), (-1.9, 25.0)));
        // The helm, the theme's way.
        double hx = 2.2;
        switch (theme)
        {
            case TerrainThemes.Gothic:
                fo.Add(Fronts.PointedRect(c - hx * s, yb + 24.8 * s, c + hx * s, yb + 30 * s));
                break;
            case TerrainThemes.Dungeon:
                fo.Add(Fronts.ArchedRect(c - hx * s, yb + 24.8 * s, c + hx * s, yb + 29.0 * s));
                var horn = Poly((1.9, 26.4), (3.4, 27.2), (4.3, 28.9), (4.4, 30.0), (3.3, 30.0), (3.0, 28.4), (1.9, 28.1));
                fo.Add(horn);
                fo.Add(Mirror(horn));
                break;
            case TerrainThemes.Desert:
                fo.Add(Fronts.ArchedRect(c - hx * s, yb + 24.8 * s, c + hx * s, yb + 28.6 * s));
                fo.Add(Poly((-0.6, 28.0), (0.6, 28.0), (0.55, 30.0), (-0.55, 30.0)));
                break;
            case TerrainThemes.SciFi:
                fo.Add(WoodFace.RoundRect(c - 2.4 * s, yb + 24.8 * s, c + 2.4 * s, yb + 29.6 * s, 2.0 * s));
                break;
            default:
                fo.Add(Fronts.ArchedRect(c - hx * s, yb + 24.8 * s, c + hx * s, yb + 29.0 * s));
                // A comb along the top.
                fo.Add(Geo.Rect(c - Math.Max(0.6, 0.6 * s), yb + 28.2 * s, c + Math.Max(0.6, 0.6 * s), yb + 30 * s));
                break;
        }
        // Plate lines, rivets, the visor and the theme's symbol on the breast.
        var fl = front.Lines;
        foreach (var sg in new[] { -1, 1 })
        {
            fl.Add([P(sg * 1.0, 1.5), P(sg * 2.7, 1.5)]);
            fl.Add([P(sg * 1.0, 7.0), P(sg * 3.0, 7.0)]);
            fl.Add([P(sg * 1.0, 8.0), P(sg * 2.7, 8.0)]);
            fl.Add([P(sg * 1.0, 10.2), P(sg * 2.8, 10.2)]);
            fl.Add([P(sg * 3.1, 21.0), P(sg * 3.2, 11.8)]);
            fl.Add([P(sg * 3.4, 17.6), P(sg * 5.1, 17.4)]);
            fl.Add([P(sg * 3.3, 13.6), P(sg * 5.2, 13.4)]);
            fl.Add([P(sg * 2.4, 22.6), P(sg * 5.4, 21.4)]);
            fl.Add(WoodFace.Dot(c + sg * 2.4 * s, yb + 14.4 * s, 0.2));
            fl.Add(WoodFace.Dot(c + sg * 2.9 * s, yb + 21.0 * s, 0.2));
        }
        for (double y = 12.2; y < 15.0; y += 1.2) fl.Add([P(-3.0, y), P(3.0, y)]);
        fl.Add([P(0, 16.2), P(0, 18.0)]);
        front.Etched.Add(Geo.Rect(c - 2.8 * s, yb + 15.4 * s, c + 2.8 * s, yb + 16.0 * s));
        if (sci)
        {
            front.Etched.Add(Geo.Rect(c - 1.8 * s, yb + 26.6 * s, c + 1.8 * s, yb + 27.8 * s));
            front.Etched.Add(Geo.Circle(c, yb + 20.6 * s, 0.9 * s, 0.01));
            fl.Add(Geo.Loop(Geo.Circle(c, yb + 20.6 * s, 1.4 * s, 0.01)));
        }
        else
        {
            front.Etched.Add(Geo.Rect(c - 1.6 * s, yb + 27.2 * s, c + 1.6 * s, yb + 27.7 * s));
            for (int i = -1; i <= 1; i++) fl.Add(WoodFace.Dot(c + i * 0.8 * s, yb + 26.2 * s, 0.18));
            Cabinets.Symbol(Fronts.Look(m), theme, c, yb + 20.6 * s, 1.3 * s, front.Etched, fl);
        }
        // Halved together: the front slotted up from between the legs, the side down from the gorget.
        front.Holes.Add(Geo.Rect(c - sw / 2, -5, c + sw / 2, ym));
        foreach (var sg in new[] { -1, 1 })
        {
            double t0 = c + sg * 2.6 * s, t1 = c + sg * Math.Max(sw / 2 + 0.25, 1.3 * s);
            b.DownInto(plinth, front, Math.Min(t0, t1), Math.Max(t0, t1));
        }
        // The side (made along +z, seen from the right).
        var a = new V3(c, 0, 0);
        var side = b.Upright("Armour (side)", a, V3.UnitZ, D, V3.UnitX);
        side.Plain = true;
        (double, double) Z(double z, double y) => (cz + z * s, yb + y * s);
        double toe = Math.Max(3.6 * s, sw / 2 + 2.9) / s;
        side.Outline.Add(ZY(side, a, [Z(-2.0, 0), Z(toe, 0), Z(toe, 0.7), Z(toe - 0.9, 1.3), Z(1.6, 1.6), Z(-1.6, 1.6), Z(-2.0, 1.0)]));
        side.Outline.Add(ZY(side, a, [Z(-1.5, 1.4), Z(1.3, 1.4), Z(1.4, 6.4), Z(1.9, 7.0), Z(1.5, 8.0), Z(1.6, 12.6), Z(-1.7, 12.6), Z(-1.8, 7.0), Z(-1.5, 6.0)]));
        side.Outline.Add(ZY(side, a, [Z(-2.3, 11.0), Z(2.8, 11.0), Z(2.4, 15.6), Z(-2.1, 15.6)]));
        side.Outline.Add(ZY(side, a, [Z(-2.1, 15.4), Z(2.4, 15.4), Z(2.9, 18.5), Z(2.6, 21.5), Z(1.8, 23.4), Z(-1.8, 23.4), Z(-2.3, 21.0), Z(-2.2, 17.5)]));
        side.Outline.Add(ZY(side, a, [Z(-2.3, 23.2), Z(2.3, 23.2), Z(2.3, 25.0), Z(-2.3, 25.0)]));
        var pauldron = Rugs.Ellipse(UZ(side, a, cz), yb + 22.2 * s, 2.5 * s, 1.5 * s);
        side.Outline.Add(pauldron);
        side.Holes.Add(Geo.Rect(UZ(side, a, cz) - sw / 2, ym, UZ(side, a, cz) + sw / 2, H + 5));
        {
            double t0 = UZ(side, a, cz + sw / 2 + 1.25), t1 = UZ(side, a, cz + Math.Min(toe * s - 0.3, sw / 2 + 2.6));
            b.DownInto(plinth, side, Math.Min(t0, t1), Math.Max(t0, t1));
        }
        foreach (var (z0, y0, z1, y1) in new[] { (-1.4, 7.0, 1.9, 7.0), (-1.5, 1.5, 1.4, 1.5), (-2.1, 12.4, 2.7, 12.4), (-2.1, 13.8, 2.6, 13.8), (-2.2, 16.0, 2.6, 16.0) })
            side.Lines.Add([new(UZ(side, a, cz + z0 * s), yb + y0 * s), new(UZ(side, a, cz + z1 * s), yb + y1 * s)]);
        side.Lines.Add(Geo.Loop(Rugs.Ellipse(UZ(side, a, cz), yb + 22.2 * s, 1.9 * s, 1.0 * s)));
        // Glued, the helm stands out in front and behind: a visor and a back plate.
        if (glue)
        {
            double hy0 = yb + 25.0 * s, hy1 = yb + (theme == TerrainThemes.Dungeon ? 28.6 : 29.0) * s;
            foreach (var (z0, z1, name) in new[] { (T / 2, 2.8 * s, "Visor"), (-2.5 * s, -T / 2, "Helm back") })
            {
                var g = b.Upright(name, new V3(c, 0, cz + z0), V3.UnitZ, z1 - z0, V3.UnitX);
                g.Glued = g.Plain = true;
                var ga = new V3(c, 0, cz + z0);
                if (z1 > 0)
                    g.Outline.Add(ZY(g, ga, [(cz + z0, hy0), (cz + z1 - 0.4 * s, hy0), (cz + z1, hy0 + (hy1 - hy0) * 0.4), (cz + z1 - 0.5 * s, hy0 + (hy1 - hy0) * 0.75), (cz + z0, hy1)]));
                else
                    g.Outline.Add(ZY(g, ga, [(cz + z0, hy0 + 0.6 * s), (cz + z1, hy0), (cz + z1, hy1 - 0.3 * s), (cz + z0 + 0.5 * s, hy1 - 0.8 * s)]));
                if (z1 > 0) g.Etched.Add(Geo.Rect(Math.Min(UZ(g, ga, cz + z0), UZ(g, ga, cz + z1)), yb + 27.2 * s, Math.Max(UZ(g, ga, cz + z0), UZ(g, ga, cz + z1)), yb + 27.7 * s));
            }
        }
        b.Finish();
        m.Note("Slide the side profile down into the front one, crossing at the chest, and push both into the plinth" + (glue ? "; glue the plinth onto its base and the visor and helm back either side of the helm." : "."));
    }
}
