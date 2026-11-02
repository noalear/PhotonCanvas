using Clipper2Lib;

namespace PhotonCanvas.Addon.Scatter;

/// <summary>
/// A barrel: discs stacked up, wider in the middle than at the ends, with a strip standing up through
/// the middle discs (hidden inside) that lines them up while the glue sets. Hoops are discs a little
/// wider than their neighbours. The lid has its staves, a groove round the rim and a bung scored on;
/// an open barrel has a rim on top and water inside.
/// </summary>
public static class Barrels
{
    enum Layer { Bottom, Holed, Solid, Water, Lid, Rim }

    public static void Make(Maker m)
    {
        var it = m.Item;
        double T = m.T, f = m.Fit, R = it.WidthMm / 2;
        int n = (int)Math.Round(it.HeightMm / T);
        if (n < 3) { m.Bad($"The barrel is too low for a {T:0.##} mm sheet: make it at least {3 * T:0.#} mm high (it's {3} layers or more)."); return; }
        if (R < 2.5) { m.Bad("The barrel is too thin: make it at least 5 mm across."); return; }
        if (n > 60) { m.Bad($"That's {n} layers: use a thicker sheet, or make the barrel lower."); return; }
        bool open = it.Open && n >= 4;
        if (it.Open && !open) m.Warn($"An open barrel needs four layers or more: make it at least {4 * T:0.#} mm high. This one has a lid.");

        double hoop = it.Hoops ? Math.Clamp(R * 0.06, 0.25, 0.6) : 0;
        double rb = R - hoop, re = rb * 0.84, half = n * T / 2;
        double Radius(int i)
        {
            double y = (i + 0.5) * T, k = (y - half) / half;
            return re + (rb - re) * (1 - k * k);
        }
        var hoops = new HashSet<int>();
        if (it.Hoops && n >= 5) { hoops.Add(1); hoops.Add(n - 2); }
        if (it.Hoops && n >= 9) hoops.Add(n / 2);

        // The core runs through every layer between the bottom and the top solid one.
        int topSolid = open ? n - 2 : n - 1;
        int coreFrom = 1, coreTo = topSolid - 1;
        double rmin = Enumerable.Range(1, Math.Max(1, n - 2)).Min(Radius) * R / Enumerable.Range(0, n).Max(i => Radius(i) + (hoops.Contains(i) ? hoop : 0));
        double c = Math.Clamp(rmin * 0.8, 1.4, 5);
        bool core = coreTo >= coreFrom && Math.Sqrt(Math.Pow((c + f) / 2, 2) + Math.Pow((T + f) / 2, 2)) <= rmin - Maker.Bridge;
        if (!core && coreTo >= coreFrom)
        {
            c = Math.Max(0.8, 2 * Math.Sqrt(Math.Max(0, Math.Pow(rmin - Maker.Bridge, 2) - Math.Pow((T + f) / 2, 2))) - f);
            core = c >= 1.2;
        }
        if (!core) m.Note("The layers are too small for a core to line them up: stack them on a cocktail stick, or by eye.");

        // Each layer's shape; layers alike are cut as copies of one piece.
        // Scaled so the widest layer is as wide as asked.
        double widest = Enumerable.Range(0, n).Max(i => Radius(i) + (hoops.Contains(i) ? hoop : 0)), scale = R / widest;
        var layers = new List<(Layer Kind, double R, int Index)>();
        for (int i = 0; i < n; i++)
        {
            double r = (Radius(i) + (hoops.Contains(i) ? hoop : 0)) * scale;
            var kind = i == 0 ? Layer.Bottom
                : i == n - 1 ? (open ? Layer.Rim : Layer.Lid)
                : open && i == n - 2 ? Layer.Water
                : core && i >= coreFrom && i <= coreTo ? Layer.Holed : Layer.Solid;
            layers.Add((kind, Math.Round(r, 3), i));
        }
        double rim = Math.Max(Maker.Bridge * 1.1, R * 0.12);
        foreach (var g in layers.GroupBy(l => (l.Kind, l.R)).OrderBy(g => g.Min(l => l.Index)))
        {
            var (kind, r) = g.Key;
            var outline = new PathsD { Geo.Circle(R, R, r) };
            var holes = new PathsD();
            var etched = new PathsD();
            var lines = new List<PathD>();
            switch (kind)
            {
                case Layer.Holed: holes.Add(Joints.Centred(R, R, c + f, T + f)); break;
                case Layer.Rim: holes.Add(Geo.Circle(R, R, r - rim)); break;
                case Layer.Lid: if (it.Metal) DrumLid(R, r, lines); else Lid(R, r, etched, lines); break;
                case Layer.Water: Water(R, r, m.Rnd, lines); break;
            }
            var idx = g.Select(l => l.Index).ToList();
            string name = kind switch
            {
                Layer.Bottom => "Bottom",
                Layer.Lid => "Lid",
                Layer.Rim => "Rim",
                Layer.Water => "Water",
                _ => (idx.Count == 1 ? "Layer " : "Layers ") + string.Join(" and ", idx.Select(i => (i + 1).ToString())) + (hoops.Contains(idx[0]) ? " (hoop)" : ""),
            };
            m.Add(name, outline, holes, idx.Select(i => Pose.Up(0, i * T + T / 2, 2 * R)), grainAlongU: true, wood: false, etched: etched, lines: lines);
        }
        if (core)
        {
            int count = coreTo - coreFrom + 1;
            m.Add("Core", [Geo.Rect(0, 0, c, count * T)], [], [Pose.Front(R - c / 2, coreFrom * T, R)], grainAlongU: false, wood: false);
        }
        m.Note($"{n} layers, {n * T:0.#} mm high. " + (core
            ? "Glue the core upright in the middle of the bottom disc, thread the layers on over it in order (smallest at the ends)" + (m.PushFit ? " (their ribs grip it)" : "") + ", then glue the " + (open ? "water and the rim" : "lid") + " on top."
            : "Glue the layers in order, smallest at the ends."));
    }

    /// <summary>Staves across the lid, a groove round its rim and a bung.</summary>
    static void Lid(double c, double r, PathsD etched, List<PathD> lines)
    {
        double g = Math.Clamp(r * 0.05, 0.2, 0.35);
        var ring = Geo.Circle(c, c, r - Math.Max(0.4, r * 0.1));
        etched.AddRange(Geo.Subtract([ring], [Geo.Circle(c, c, r - Math.Max(0.4, r * 0.1) - g)]));
        double inner = r - Math.Max(0.4, r * 0.1) - g;
        double step = Math.Clamp(r * 0.32, 1.0, 2.5);
        for (double x = c - inner + step; x < c + inner - step * 0.4; x += step)
        {
            double h = Math.Sqrt(Math.Max(0, inner * inner - (x - c) * (x - c)));
            lines.Add([new(x, c - h), new(x, c + h)]);
        }
        lines.Add(WoodFace.Dot(c + inner * 0.45, c, Math.Clamp(r * 0.1, 0.25, 0.8)));
    }

    /// <summary>A drum's lid: the rolled rim, a filler cap and a vent.</summary>
    static void DrumLid(double c, double r, List<PathD> lines)
    {
        double rim = Math.Max(0.4, r * 0.12);
        lines.Add(Geo.Loop(Geo.Circle(c, c, r - rim)));
        lines.Add(Geo.Loop(Geo.Circle(c, c, r - rim - 0.25)));
        double cap = Math.Clamp(r * 0.16, 0.4, 1.4), at = (r - rim) * 0.55;
        lines.Add(WoodFace.Dot(c + at, c, cap));
        lines.Add(WoodFace.Dot(c + at, c, cap * 0.55));
        lines.Add(WoodFace.Dot(c - at, c, Math.Max(0.25, cap * 0.5)));
    }

    /// <summary>Rings of ripples on the water.</summary>
    static void Water(double c, double r, Random rnd, List<PathD> lines)
    {
        double cx = c + (rnd.NextDouble() - 0.5) * r * 0.3, cy = c + (rnd.NextDouble() - 0.5) * r * 0.3;
        for (double rr = r * 0.18; rr < r * 0.8; rr += Math.Max(0.5, r * 0.16))
        {
            double a0 = rnd.NextDouble() * Math.PI * 2;
            lines.Add([.. Geo.Arc(cx, cy, rr, a0, a0 + Math.PI * (1.1 + rnd.NextDouble() * 0.6))]);
        }
    }
}
