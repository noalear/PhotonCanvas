using Clipper2Lib;

namespace PhotonCanvas.Addon.Furniture;

/// <summary>
/// Coffins, sarcophagi and sleeper pods: layers of sheet stacked into a solid shape, two keys through
/// them all (strips standing in slots) to line them up as they're glued; the top layer is the carved
/// lid. A sarcophagus stands on a wider plinth with its lid's lip overhanging and an effigy on top.
/// </summary>
public static class Coffins
{
    public static void Make(Maker m)
    {
        var it = m.Item;
        var style = it.Coffin;
        var look = Fronts.Look(m);
        double L = it.WidthMm, D = it.DepthMm, H = it.HeightMm, T = m.T, f = m.Fit, sw = m.SlotW;
        string what = style switch { CoffinStyle.Sarcophagus => "sarcophagus", CoffinStyle.Pod => "pod", _ => "coffin" };
        int n = Math.Max(2, (int)Math.Round(H / T));
        if (L < 10 || D < 5) { m.Bad($"The {what} is too small: make it at least 10 mm long and 5 mm wide."); return; }
        if (style == CoffinStyle.Sarcophagus && n < 3) n = 3;

        PathD Body(double grow)
        {
            PathD p = style switch
            {
                CoffinStyle.Coffin => [new(0, D * 0.18), new(L * 0.27, 0), new(L, D * 0.26), new(L, D * 0.74), new(L * 0.27, D), new(0, D * 0.82)],
                CoffinStyle.Pod => WoodFace.RoundRect(0, 0, L, D, D / 2 - 0.01),
                _ => Fronts.Chamfered(0, 0, L, D, Math.Min(1.0, D * 0.1)),
            };
            return grow == 0 ? p : Geo.Offset([p], grow, round: style == CoffinStyle.Pod).OrderByDescending(q => Math.Abs(Clipper.Area(q))).First();
        }
        // How much each layer grows (+) or shrinks (−) from the body, bottom to top.
        var grows = new double[n];
        if (style == CoffinStyle.Sarcophagus)
        {
            grows[0] = 0.8;
            grows[n - 2] = 0.4;
            grows[n - 1] = -0.6;
        }
        else if (style == CoffinStyle.Coffin) grows[n - 1] = 0.3;
        else grows[0] = -0.4;

        // The keys: strips through every layer, at both ends, along the middle.
        double kw = 2.0, Hk = n * T;
        double[] keys = style == CoffinStyle.Coffin ? [L * 0.22, L * 0.78] : [L * 0.13, L * 0.87];
        var slots = new PathsD();
        foreach (var x in keys) slots.Add(Geo.Rect(x - (kw + f) / 2, D / 2 - sw / 2, x + (kw + f) / 2, D / 2 + sw / 2));
        var rnd = m.Rnd;
        for (int k = 0; k < n;)
        {
            bool top = k == n - 1;
            // Identical middle layers are one piece cut several times.
            int next = k + 1;
            if (k > 0 && !top) while (next < n - 1 && grows[next] == grows[k]) next++;
            var poses = Enumerable.Range(k, next - k).Select(j => Pose.Up(0, j * T + T / 2, D)).ToList();
            var etched = new PathsD();
            var lines = new List<PathD>();
            if (top) Lid(m, style, look, L, D, grows[k], rnd, etched, lines);
            string name = k == 0 ? (style == CoffinStyle.Sarcophagus ? "Plinth" : "Base") : top ? "Lid" : style == CoffinStyle.Sarcophagus && k == n - 2 ? "Lid's lip" : "Layer";
            m.Add(name, [Body(grows[k])], slots, poses, grainAlongU: true, face: top && style != CoffinStyle.Coffin ? [] : top ? null : [], etched: etched, lines: lines, planks: top, plankMm: D / 3);
            k = next;
        }
        if (RaisedCross(m, style, look))
        {
            // A cross glued on the lid, standing proud of it, lying along it with its head towards the wide end.
            double bar = Geo.Clamp(D * 0.13, 1.4, 2.4), cl = L * 0.6, x0 = L * 0.16, cz = D / 2, arm = Math.Min(D * 0.62, cl * 0.5), ax = x0 + cl * 0.3;
            var cross = new PathsD
            {
                Geo.Rect(x0, cz - bar / 2, x0 + cl, cz + bar / 2),
                Geo.Rect(ax - bar / 2, cz - arm / 2, ax + bar / 2, cz + arm / 2),
            };
            // Flared ends.
            foreach (var (x, z, along) in new[] { (x0, cz, true), (x0 + cl, cz, true), (ax, cz - arm / 2, false), (ax, cz + arm / 2, false) })
                cross.Add(along ? Geo.Rect(x - bar * 0.35, z - bar * 0.85, x + bar * 0.35, z + bar * 0.85) : Geo.Rect(x - bar * 0.85, z - bar * 0.35, x + bar * 0.85, z + bar * 0.35));
            var cl2 = new List<PathD> { Geo.Loop(Rugs.Ellipse(ax, cz, bar * 0.32, bar * 0.32)) };
            m.Add("Lid cross", Geo.Solid(cross), [], [Pose.Up(0, n * T + T / 2, D)], grainAlongU: true, face: [], lines: cl2).Glue();
            m.Note("Glue the cross along the lid.");
        }
        var key = new PathsD { Geo.Rect(0, 0, kw, Hk) };
        m.Add("Key", key, [], [.. keys.Select(x => Pose.Front(x - kw / 2, 0, D / 2))], grainAlongU: false, face: []);
        m.Note($"Stand the two keys in the base and slide the layers down over them one by one, gluing each{(n > 2 ? "" : "")}; the lid goes on last.");
    }

    /// <summary>Whether a plank coffin gets a cross cut to glue on its lid (else it's etched on).</summary>
    static bool RaisedCross(Maker m, CoffinStyle style, FrontLook look) => style == CoffinStyle.Coffin && look == FrontLook.Plank && !m.PushFit && m.Item.DepthMm >= 8;

    static void Lid(Maker m, CoffinStyle style, FrontLook look, double L, double D, double grow, Random rnd, PathsD etched, List<PathD> lines)
    {
        switch (style)
        {
            case CoffinStyle.Sarcophagus:
            {
                // An effigy: head on a pillow, the body under a robe, hands folded on a sword.
                double cz = D / 2, w = D * 0.42;
                etched.AddRange(Fronts.Outline([Geo.Offset([Fronts.Chamfered(0, 0, L, D, Math.Min(1.0, D * 0.1))], grow - 0.6)[0]], 0.3));
                lines.Add(Geo.Loop(Geo.Rect(L * 0.06, cz - w * 0.75, L * 0.17, cz + w * 0.75)));
                lines.Add(WoodFace.Dot(L * 0.17, cz, w * 0.38));
                var robe = new PathD { new(L * 0.24, cz - w), new(L * 0.86, cz - w * 0.55), new(L * 0.86, cz + w * 0.55), new(L * 0.24, cz + w) };
                lines.Add(Geo.Loop(robe));
                for (double x = L * 0.55; x < L * 0.84; x += 1.0) lines.Add([new(x, cz - w * 0.6), new(x + 0.3, cz + w * 0.6)]);
                lines.Add([new(L * 0.3, cz), new(L * 0.82, cz)]);
                lines.Add([new(L * 0.36, cz - w * 0.4), new(L * 0.36, cz + w * 0.4)]);
                etched.Add(Rugs.Ellipse(L * 0.42, cz, w * 0.22, w * 0.32));
                lines.Add([new(L * 0.87, cz - w * 0.5), new(L * 0.92, cz - w * 0.5)]);
                lines.Add([new(L * 0.87, cz + w * 0.5), new(L * 0.92, cz + w * 0.5)]);
                break;
            }
            case CoffinStyle.Pod:
            {
                // A window over the sleeper, panel lines and lights.
                var win = Fronts.Chamfered(L * 0.15, D * 0.2, L * 0.65, D * 0.8, Math.Min(1.2, D * 0.12));
                var face = new PathsD { Rugs.Ellipse(L * 0.25, D / 2, D * 0.13, D * 0.16), Geo.Rect(L * 0.32, D * 0.33, L * 0.64, D * 0.67) };
                etched.AddRange(Geo.Subtract([win], Geo.Union(face)));
                lines.Add([new(L * 0.7, 0.6), new(L * 0.7, D - 0.6)]);
                for (double y = D * 0.3; y < D * 0.75; y += 0.9) lines.Add(WoodFace.Dot(L * 0.82, y, 0.2));
                break;
            }
            default:
            {
                double r = Math.Min(D * 0.3, L * 0.12);
                double cx = L * 0.32, cz = D / 2;
                if (RaisedCross(m, style, look)) { }
                else if (look == FrontLook.Plank)
                {
                    // A cross on the lid, lying along it.
                    var cross = new PathsD
                    {
                        Geo.Rect(cx - r * 1.6, cz - r * 0.12, cx + r * 1.6, cz + r * 0.12),
                        Geo.Rect(cx - r * 0.85, cz - r * 0.75, cx - r * 0.6, cz + r * 0.75),
                    };
                    etched.AddRange(Geo.Union(cross));
                }
                else Cabinets.Symbol(look, m.Theme, cx, cz, r, etched, lines);
                // Handles along the sides.
                foreach (var x in new[] { L * 0.35, L * 0.65 })
                    foreach (var z in new[] { 0.5, D - 0.5 }) lines.Add([new(x - 0.8, z), new(x + 0.8, z)]);
                break;
            }
        }
    }
}
