using Clipper2Lib;

namespace PhotonCanvas.Addon.Scatter;

/// <summary>
/// A fence section cut in one piece: posts with picket, rail, plank or wattle between them, and a foot
/// slotted across each post with a halving joint so the section stands up on its own.
/// </summary>
public static class Fences
{
    public static void Make(Maker m)
    {
        var it = m.Item;
        double L = it.WidthMm, H = it.HeightMm, T = m.T, f = m.Fit, sw = m.SlotW;
        double pw = Math.Max(1.5, Math.Min(T * 0.9, 3));
        double hf = Math.Clamp(H * 0.22, 2.5, 4), footW = sw + 2 * Maker.Bridge + 0.2, Lf = Math.Clamp(H * 0.6, 6, 14);
        bool feet = it.Feet;
        if (H < (feet ? hf + 5 : 5)) { m.Bad($"The fence is too low: make it at least {(feet ? hf + 5 : 5):0.#} mm high."); return; }
        if (L < 2 * footW + 4) { m.Bad($"The fence is too short: make it at least {2 * footW + 4:0.#} mm long."); return; }

        int bays = Math.Max(1, (int)Math.Ceiling((L - pw) / 30));
        var posts = Enumerable.Range(0, bays + 1).Select(k => pw / 2 + k * (L - pw) / bays).ToList();
        if (feet) { posts[0] = Math.Max(posts[0], footW / 2); posts[^1] = Math.Min(posts[^1], L - footW / 2); }

        var postArea = new PathsD();
        foreach (var x in posts)
        {
            postArea.Add(Chamfered(x - pw / 2, 0, x + pw / 2, H, Math.Min(0.35, pw * 0.2)));
            if (feet) postArea.Add(Geo.Rect(x - footW / 2, 0, x + footW / 2, hf + 0.8));
        }
        var rails = new PathsD();
        var boards = new PathsD();
        var etched = new PathsD();
        var lines = new List<PathD>();
        for (int b = 0; b < bays; b++)
        {
            double xa = posts[b] + pw / 2 - 0.01, xb = posts[b + 1] - pw / 2 + 0.01;
            if (xb - xa < 2) continue;
            switch (it.Style)
            {
                case FenceStyle.Picket: Pickets(m, xa, xb, H, rails, boards); break;
                case FenceStyle.PostAndRail: Rails(m, xa, xb, H, rails); break;
                case FenceStyle.Planks: Planks(m, xa, xb, H, boards, etched); break;
                case FenceStyle.Wattle: Wattle(m, xa, xb, H, boards, etched); break;
            }
        }
        var outline = Geo.Union(Geo.Union(postArea, rails), boards);
        var holes = new PathsD();
        if (feet) foreach (var x in posts) holes.Add(Geo.Rect(x - sw / 2, -1, x + sw / 2, hf / 2 + f / 2));

        // Grain up the posts and boards, along the rails.
        var (pe, pl) = m.Wood(Geo.Union(postArea), alongU: false);
        etched.AddRange(pe); lines.AddRange(pl);
        if (rails.Count > 0) { var (re, rl) = m.Wood(Geo.Subtract(Geo.Union(rails), Geo.Union(postArea)), alongU: true); etched.AddRange(re); lines.AddRange(rl); }
        if (boards.Count > 0 && it.Style != FenceStyle.Wattle)
        {
            var (be, bl) = m.Wood(Geo.Subtract(Geo.Union(boards), Geo.Union(Geo.Union(postArea), Geo.Union(rails))), alongU: false);
            etched.AddRange(be); lines.AddRange(bl);
        }
        m.Add("Fence", outline, holes, [Pose.Front(0, 0, feet ? Lf / 2 : 0)], grainAlongU: false, wood: false, etched: etched, lines: lines);

        if (feet)
        {
            var foot = new PathsD { new PathD { new(0, 0), new(Lf, 0), new(Lf, hf * 0.45), new(Lf * 0.75, hf), new(Lf * 0.25, hf), new(0, hf * 0.45) } };
            var fh = new PathsD { Geo.Rect(Lf / 2 - sw / 2, hf / 2 - f / 2, Lf / 2 + sw / 2, hf + 1) };
            m.Add("Foot", foot, fh, posts.Select(x => Pose.Left(x, 0, 0)), grainAlongU: true);
            m.Note("Slide a foot up into the slot under each post, across the fence; " + (m.PushFit ? "the ribs hold them square." : "a drop of glue holds them square."));
        }
        else m.Note("Without feet, glue the fence to a base or stand it in a groove in your terrain.");
    }

    /// <summary>A rectangle with its top corners cut off.</summary>
    static PathD Chamfered(double x0, double y0, double x1, double y1, double c) =>
        [new(x0, y0), new(x1, y0), new(x1, y1 - c), new(x1 - c, y1), new(x0 + c, y1), new(x0, y1 - c)];

    /// <summary>Two rails and pointed pales, a gap at the bottom.</summary>
    static void Pickets(Maker m, double xa, double xb, double H, PathsD rails, PathsD boards)
    {
        double rh = Math.Clamp(H * 0.08, 1.1, 1.8);
        foreach (var y in new[] { H * 0.25, H * 0.68 }) rails.Add(Geo.Rect(xa, y - rh / 2, xb, y + rh / 2));
        double pale = Math.Clamp(H * 0.1, 1.3, 2.2), bay = xb - xa;
        int n = Math.Max(1, (int)Math.Floor((bay - pale * 0.6) / (pale * 1.75)));
        double gap = (bay - n * pale) / (n + 1);
        for (int i = 0; i < n; i++)
        {
            double x0 = xa + gap + i * (pale + gap), x1 = x0 + pale, xc = (x0 + x1) / 2;
            double top = H * 0.9 + (m.Rnd.NextDouble() - 0.5) * 0.4, flat = Maker.Bridge * 1.05, ch = Math.Max(0.05, (pale - flat) / 2);
            boards.Add([new(x0, 0.8), new(x1, 0.8), new(x1, top - ch), new(xc + flat / 2, top), new(xc - flat / 2, top), new(x0, top - ch)]);
        }
    }

    /// <summary>Rough rails between the posts, not quite level.</summary>
    static void Rails(Maker m, double xa, double xb, double H, PathsD rails)
    {
        double rh = Math.Clamp(H * 0.11, 1.3, 2.4);
        double[] at = H >= 10 ? [0.22, 0.52, 0.82] : [0.3, 0.75];
        foreach (var k in at)
        {
            double y = H * k, wob = Math.Min(0.25, rh * 0.15);
            double ya = y + (m.Rnd.NextDouble() - 0.5) * 2 * wob, yb = y + (m.Rnd.NextDouble() - 0.5) * 2 * wob;
            rails.Add([new(xa, ya - rh / 2), new(xb, yb - rh / 2), new(xb, yb + rh / 2), new(xa, ya + rh / 2)]);
        }
    }

    /// <summary>Upright boards side by side, their tops uneven, with a groove between each.</summary>
    static void Planks(Maker m, double xa, double xb, double H, PathsD boards, PathsD etched)
    {
        double bay = xb - xa;
        int n = Math.Max(1, (int)Math.Round(bay / 2.4));
        double w = bay / n;
        for (int i = 0; i < n; i++)
        {
            double x0 = xa + i * w, top = H * 0.88 + (m.Rnd.NextDouble() - 0.5) * 0.8;
            boards.Add(Geo.Rect(x0, 0.5, x0 + w + (i < n - 1 ? 0.01 : 0), top));
            if (i > 0) etched.Add(Geo.Rect(x0 - WoodFace.GrooveMm / 2, 0.4, x0 + WoodFace.GrooveMm / 2, H));
        }
        // Two battens across, scored.
        foreach (var y in new[] { H * 0.25, H * 0.7 })
            etched.AddRange(Geo.Subtract([Geo.Rect(xa, y - 0.7, xb, y + 0.7)], [Geo.Rect(xa + 0.3, y - 0.45, xb - 0.3, y + 0.45)]));
    }

    /// <summary>Woven hurdle: stakes poking out of the top, the weave etched between them.</summary>
    static void Wattle(Maker m, double xa, double xb, double H, PathsD boards, PathsD etched)
    {
        double bay = xb - xa, top = H * 0.82, row = Math.Clamp(H * 0.08, 0.9, 1.4), stake = 1.2;
        int n = Math.Max(1, (int)Math.Round(bay / 3.2));
        double step = bay / (n + 1);
        // The panel, its top a little uneven.
        var p = new PathD { new(xa, 0.6), new(xb, 0.6) };
        for (double x = xb; x >= xa - 1e-9; x -= Math.Max(0.8, bay / 12)) p.Add(new(Math.Max(xa, x), top + (m.Rnd.NextDouble() - 0.5) * 0.5));
        boards.Add(p);
        var stakes = new List<double>();
        for (int i = 1; i <= n; i++)
        {
            double x = xa + i * step;
            stakes.Add(x);
            boards.Add(Geo.Rect(x - stake / 2, 0.6, x + stake / 2, Math.Min(H, top + 1.1 + m.Rnd.NextDouble() * 0.4)));
        }
        int r = 0;
        for (double y = 0.6 + row; y < top - 0.3; y += row, r++)
        {
            // The groove between two withies bows round the stakes, in front of one and behind the next.
            var upper = new PathD();
            var lower = new PathD();
            for (double x = xa; x <= xb + 1e-9; x += Math.Max(0.2, step / 8))
            {
                double off = 0.22 * Math.Sin(Math.PI * (x - xa) / step + r * Math.PI);
                upper.Add(new(x, y + off + 0.13));
                lower.Add(new(x, y + off - 0.13));
            }
            lower.Reverse();
            upper.AddRange(lower);
            etched.Add(upper);
            // The withies go in front of one stake and behind the next, so the stakes show in every other row.
            for (int i = 0; i < stakes.Count; i++)
                if ((i + r) % 2 == 0)
                    foreach (var side in new[] { -1, 1 })
                        etched.Add(Geo.Rect(stakes[i] + side * stake / 2 - 0.13, y - row, stakes[i] + side * stake / 2 + 0.13, y));
        }
    }
}
