using Clipper2Lib;

namespace PhotonCanvas.Addon.Scatter;

/// <summary>
/// A palm tree: a curved trunk with rings scored up it, standing on a tab in a slot in a round base, and
/// a crown of fronds lying across its top on a second tab. The fronds' midribs and leaflets are scored.
/// </summary>
public static class Palms
{
    public static void Make(Maker m)
    {
        var it = m.Item;
        double W = it.WidthMm, D = it.DepthMm, H = it.HeightMm, T = m.T, f = m.Fit, sw = m.SlotW, B = Maker.Bridge;
        double Rc = W / 2, Rb = D / 2, Ht = H - 2 * T;
        double wb = Math.Clamp(D * 0.3, 2.5, 6), wt = Math.Max(wb * 0.65, 2.2);
        double tw0 = Math.Clamp(wb * 0.6, 1.2, 4), tw = Math.Clamp(wt * 0.6, 1.2, 4);
        if (Rb < tw0 / 2 + B + 1.5) { m.Bad($"The palm's base is too small: make it at least {2 * (tw0 / 2 + B + 1.5):0.#} mm across."); return; }
        if (Rc < 6) { m.Bad("The palm's fronds are too short: make them at least 12 mm across."); return; }
        if (Ht < 8) { m.Bad($"The palm is too low: make it at least {8 + 2 * T:0.#} mm high."); return; }

        // The trunk, flat: a curve up from the base's middle, leaning over towards the top.
        double xb = Rb, zc = Rb, lean = Math.Min(Ht * 0.18, Rc * 0.6);
        PointD C(double s) { double a = (1 - s) * (1 - s), b = 2 * s * (1 - s), c = s * s; return new(a * xb + b * xb + c * (xb + lean), b * Ht * 0.55 + c * Ht); }
        var left = new PathD();
        var right = new PathD();
        const int n = 48;
        // A little past both ends, so trimming it level at the foot and the top leaves it full width there.
        for (int i = 0; i <= n; i++)
        {
            double s = -0.05 + 1.2 * i / n, sc = Math.Clamp(s, 0, 1);
            var p = C(s);
            var q = C(s + 0.01);
            var o = C(s - 0.01);
            double dx = q.x - o.x, dy = q.y - o.y, len = Math.Sqrt(dx * dx + dy * dy);
            double w = wb + (wt - wb) * sc + (sc < 0.12 ? wb * 0.45 * (1 - sc / 0.12) : 0);
            left.Add(new PointD(p.x - dy / len * w / 2, p.y + dx / len * w / 2));
            right.Add(new PointD(p.x + dy / len * w / 2, p.y - dx / len * w / 2));
        }
        left.Reverse();
        var trunk = new PathD(right);
        trunk.AddRange(left);
        var top = C(1);
        var body = Geo.Intersect([Geo.Positive(trunk)], [Geo.Rect(-100, 0, 100 + W, Ht)]);
        body.Add(Geo.Rect(xb - tw0 / 2, -T, xb + tw0 / 2, 0.01));
        body.Add(Geo.Rect(top.x - tw / 2, Ht - 0.01, top.x + tw / 2, Ht + T));
        // Rings up the trunk: a shallow V across it every so often.
        var rings = new List<PathD>();
        double step = Math.Clamp(wb * 0.4, 1.0, 1.8) / Ht;
        for (double s = 0.1; s < 0.97; s += step)
        {
            var p = C(s);
            var q = C(s + 0.01);
            double dx = q.x - p.x, dy = q.y - p.y, len = Math.Sqrt(dx * dx + dy * dy), w = wb + (wt - wb) * s;
            double nx = -dy / len, ny = dx / len, sag = w * 0.15;
            rings.Add([new(p.x - nx * w, p.y - ny * w + sag), new(p.x - dx / len * sag, p.y - dy / len * sag), new(p.x + nx * w, p.y + ny * w + sag)]);
        }
        m.Add("Trunk", body, [], [Pose.Front(0, T, zc)], grainAlongU: false, wood: false, lines: rings);

        // The base: a low mound of sand with the trunk's slot in its middle.
        var bw = Stacks.Wobble.Make(m.Rnd, 0.08);
        var baseCut = new PathsD { Stacks.Blob(Rb, Rb, Rb, Rb, bw) };
        var baseLines = new List<PathD>();
        for (int i = 0; i < (int)(Rb * Rb / 5); i++)
        {
            double a = m.Rnd.NextDouble() * Math.PI * 2, r = (tw0 / 2 + B + 0.8) + (Rb - 1 - tw0 / 2 - B - 0.8) * Math.Sqrt(m.Rnd.NextDouble());
            baseLines.Add(WoodFace.Dot(Rb + Math.Cos(a) * r, Rb + Math.Sin(a) * r, 0.2));
        }
        m.Add("Base", baseCut, [Joints.Centred(Rb, Rb, tw0 + f, sw)], [Pose.Up(xb - Rb, T / 2, zc + Rb)], grainAlongU: true, wood: false, lines: baseLines);

        // The crown: fronds round a middle that holds the trunk's top tab.
        int fronds = Rc >= 10 ? 8 : 6;
        double wf = Math.Clamp(Rc * 0.24, 2, 7), hub = Math.Sqrt(Math.Pow((tw + f) / 2, 2) + Math.Pow(sw / 2, 2)) + B + 0.3;
        var crown = new PathsD { Geo.Circle(Rc, Rc, Math.Max(hub, wf * 0.6)) };
        var crownLines = new List<PathD>();
        double a0 = m.Rnd.NextDouble() * Math.PI;
        for (int i = 0; i < fronds; i++)
        {
            double a = a0 + i * 2 * Math.PI / fronds + (m.Rnd.NextDouble() - 0.5) * 0.3, L = Rc * (0.85 + 0.15 * m.Rnd.NextDouble()), bend = L * 0.12 * (m.Rnd.NextDouble() < 0.5 ? -1 : 1);
            var (leaf, rib, leaflets) = Frond(Rc, Rc, a, L, wf, bend, hub);
            crown.Add(leaf);
            crownLines.Add(rib);
            crownLines.AddRange(leaflets);
        }
        m.Add("Fronds", Stacks.Biggest(Stacks.Smooth(Geo.Subtract(Geo.Union(crown), [Joints.Centred(Rc, Rc, tw + f, sw)]))), [], [Pose.Up(top.x - Rc, H - T / 2, zc + Rc)], grainAlongU: true, wood: false, lines: crownLines);
        m.Note("Push the trunk's foot down into the base and the fronds down onto its top.");
    }

    /// <summary>One frond from the middle (cx, cy) out along angle <paramref name="a"/>: its outline, its midrib and its leaflets.</summary>
    static (PathD Leaf, PathD Rib, List<PathD> Leaflets) Frond(double cx, double cy, double a, double L, double w, double bend, double from)
    {
        double ux = Math.Cos(a), uy = Math.Sin(a), nx = -uy, ny = ux;
        PointD At(double s, double side) => new(cx + ux * s * L + nx * (bend * s * s + side), cy + uy * s * L + ny * (bend * s * s + side));
        double Half(double s) => w / 2 * Math.Pow(Math.Sin(Math.PI * Math.Min(1, s * 1.05)), 0.7);
        var l = new PathD();
        var r = new PathD();
        const int n = 30;
        for (int i = 0; i <= n; i++)
        {
            double s = i / (double)n;
            l.Add(At(s, Half(s)));
            r.Add(At(s, -Half(s)));
        }
        r.Reverse();
        var leaf = new PathD(l);
        leaf.AddRange(r);
        var rib = new PathD();
        double s0 = Math.Min(0.5, from / L);
        for (int i = 0; i <= 20; i++) rib.Add(At(s0 + (0.92 - s0) * i / 20.0, 0));
        var leaflets = new List<PathD>();
        for (double s = s0 + 0.05; s < 0.9; s += 1.0 / L)
            foreach (var side in new[] { -1, 1 })
                leaflets.Add([At(s, 0), At(s + 0.06, side * Half(s + 0.06))]);
        return (Geo.Positive(leaf), rib, leaflets);
    }
}
