using Clipper2Lib;

namespace PhotonCanvas.Addon.Scatter;

/// <summary>
/// A square well: four finger-jointed walls with stonework etched on them. With a roof, the left and
/// right walls rise in the middle as posts with a gable on top; a winch bar slides through both posts,
/// and the two halves of the roof lie on the gables, their tabs up through them.
/// </summary>
public static class Wells
{
    public static void Make(Maker m)
    {
        var it = m.Item;
        double W = it.WidthMm, D = it.DepthMm, Hw = it.HeightMm, T = m.T, f = m.Fit, B = Maker.Bridge;
        // Room for three fingers up each corner (the corners are all that hold the walls together).
        double min = Math.Max(3 * T + 2, 2 * T + 3 * Maker.Bridge * 1.2 + 0.05);
        if (W < min || D < min || Hw < min) { m.Bad($"The well is too small for a {T:0.##} mm sheet: make each side and the wall's height at least {min:0.#} mm."); return; }
        var walls = Joints.Box([W, Hw, D], T, [true, true, false, false, true, true], ["Left wall", "Right wall", "Bottom", "Top", "Back wall", "Front wall"]);
        string texture = StoneFace.Find(it.Texture) is not null ? it.Texture : StoneFace.Presets[0].Name;

        // The roof, its posts and gables.
        bool roof = it.Roof;
        double pitch = 38 * Math.PI / 180, gw = D / 2 * 0.7, wa = Math.Clamp(T, 1.6, 2.5);
        double pw = Math.Max(wa + f + 2 * B, 3), gb = Math.Clamp(gw * 0.3, 1.5, 3);
        double apex = it.TopMm - T * Math.Cos(pitch), eave = apex - gw * Math.Tan(pitch);
        double yw = Hw + (eave - gb - Hw) * 0.5;
        if (roof && eave - gb - Hw < 5)
        {
            m.Bad($"The roof is too low over the wall for its posts and winch: make the top at least {Hw + 5 + gb + gw * Math.Tan(pitch) + T * Math.Cos(pitch):0.#} mm high, or the wall lower.");
            return;
        }
        var front = new Slope(D / 2 + gw, eave, D / 2, apex);
        var back = new Slope(D / 2 - gw, eave, D / 2, apex);
        double Ls = front.Length, rtw = Math.Clamp(Ls * 0.3, 1.2, 3);

        foreach (var p in walls)
        {
            var outline = new PathsD(p.Cut);
            var holes = new PathsD();
            var etched = StoneFace.Grooves(texture, p.Lu, p.Lv, m.Scale, m.Rnd);
            var lines = new List<PathD>();
            if (roof && p.Name is "Left wall" or "Right wall")
            {
                double x = p.Name == "Left wall" ? T / 2 : W - T / 2;
                var pose = p.Pose;
                PathD ZY(params (double Z, double Y)[] pts) => Geo.Positive([.. pts.Select(q => Joints.Flat(pose, new V3(x, q.Y, q.Z)))]);
                var post = new PathsD
                {
                    ZY((D / 2 - pw / 2, Hw - 0.01), (D / 2 + pw / 2, Hw - 0.01), (D / 2 + pw / 2, eave), (D / 2 - pw / 2, eave)),
                    ZY((D / 2 - gw, eave - gb), (D / 2 + gw, eave - gb), (D / 2 + gw, eave + 1), (D / 2, apex + 1), (D / 2 - gw, eave + 1)),
                };
                var top = Geo.Intersect(Geo.Intersect(Geo.Union(post), front.Below(pose, x)), back.Below(pose, x));
                var (we, wl) = m.Wood(top, alongU: false);
                etched.AddRange(we);
                lines.AddRange(wl);
                outline = Geo.Union(outline, top);
                outline.Add(front.Tab(pose, x, Ls / 2, rtw, T));
                outline.Add(back.Tab(pose, x, Ls / 2, rtw, T));
                holes.Add(ZY((D / 2 - (wa + f) / 2, yw - (T + f) / 2), (D / 2 + (wa + f) / 2, yw - (T + f) / 2), (D / 2 + (wa + f) / 2, yw + (T + f) / 2), (D / 2 - (wa + f) / 2, yw + (T + f) / 2)));
            }
            m.Add(p.Name, outline, holes, [p.Pose], grainAlongU: true, wood: false, etched: etched, lines: lines);
        }
        if (!roof)
        {
            m.Note("Push the four walls together at the corners, their fingers between each other's.");
            return;
        }

        // The winch bar, lying flat through the posts, its crank outside the right post.
        double ext = 1.5, crank = Math.Clamp(D * 0.15, 2, 4), L = W + 2 * ext + 1.5;
        var winch = new PathsD { Geo.Rect(0, 0, L, wa), Geo.Rect(L - 1.2, -crank, L, 0.01) };
        m.Add("Winch", winch, [], [Pose.Up(-ext, yw, D / 2 + wa / 2)], grainAlongU: true);

        // The two halves of the roof.
        double ox = 1.5, o = Math.Clamp(D * 0.12, 1.5, 4);
        double before = (D / 2 + o - gw) / Math.Abs(front.Dz);
        foreach (var (name, s) in new[] { ("Roof (front)", front), ("Roof (back)", back) })
        {
            var pose = s.Panel(-ox, W + ox, before, T);
            double Wr = W + 2 * ox, Lr = before + Ls;
            var slots = new PathsD();
            foreach (var x in new[] { T / 2, W - T / 2 }) slots.Add(s.Slot(pose, x, Ls / 2, rtw, m.SlotW, f));
            var etched = it.Shingles ? StoneFace.Shingles(Wr, Lr, 2.4 * m.Scale, 1.8 * m.Scale, Math.Clamp(0.3 * m.Scale, 0.2, 0.5)) : [];
            m.Add(name, [Geo.Rect(0, 0, Wr, Lr)], slots, [pose], grainAlongU: true, wood: !it.Shingles, etched: etched);
        }
        m.Note("Push the four walls together at the corners, slide the winch through both posts, crank end last, then press the two halves of the roof down onto the gables' tabs.");
    }
}
