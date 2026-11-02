using Clipper2Lib;

namespace PhotonCanvas.Addon.Scatter;

/// <summary>
/// Scenery built up from flat layers glued one on another, like the barrels: stalagmites, giant
/// mushrooms, boulders and urns. Each layer is a slice of the shape; the outline of the layer above is
/// scored on it so the stack goes together the right way round, and a core strip standing up through
/// slots in the lower layers lines them up while the glue sets. A layer that comes apart in more than
/// one piece (the tops of a clump of stalagmites) is cut as separate pieces.
/// </summary>
public static class Stacks
{
    /// <summary>A shape's wobble round its middle: a few waves of random size and phase, the same at every height.</summary>
    public sealed record Wobble(double[] Amp, double[] Phase)
    {
        public static Wobble Make(Random rnd, double amount)
        {
            int n = 4;
            var amp = new double[n];
            var phase = new double[n];
            for (int k = 0; k < n; k++)
            {
                amp[k] = amount * (0.4 + 0.6 * rnd.NextDouble()) / (k + 1);
                phase[k] = rnd.NextDouble() * Math.PI * 2;
            }
            return new(amp, phase);
        }

        public double At(double a, double twist = 0)
        {
            double s = 1;
            for (int k = 0; k < Amp.Length; k++) s += Amp[k] * Math.Cos((k + 2) * a + Phase[k] + twist * (k + 1));
            return s;
        }
    }

    /// <summary>An outline round (cx, cy): <paramref name="rx"/> by <paramref name="ry"/> across, wobbling.</summary>
    public static PathD Blob(double cx, double cy, double rx, double ry, Wobble? w, double twist = 0)
    {
        double r = Math.Max(rx, ry);
        int n = Math.Clamp(Geo.Segments(r, 0.02), 24, 160);
        // Small slices wobble less, so they don't come out spiky.
        double k = Math.Clamp((r - 0.8) / 2.5, 0, 1);
        var p = new PathD(n);
        for (int i = 0; i < n; i++)
        {
            double a = 2 * Math.PI * i / n, s = w is null ? 1 : 1 + (w.At(a, twist) - 1) * k;
            p.Add(new PointD(cx + Math.Cos(a) * rx * s, cy + Math.Sin(a) * ry * s));
        }
        return p;
    }

    /// <summary>
    /// Rounds off anything narrower than about a millimetre, which would break off or burn away: rounded
    /// first, then opened the way <see cref="Maker.Thin"/> checks it (a round opening can leave a narrow
    /// waist where two rounded bits only just overlap).
    /// </summary>
    public static PathsD Smooth(PathsD a, double r = 0.5)
    {
        if (a.Count == 0) return a;
        var s = Geo.Offset(Geo.Offset(a, -r, round: true), r, round: true);
        double w = Maker.Bridge * 0.9;
        for (int i = 0; i < 4 && Maker.Thin(s) > 0.01; i++) s = Geo.Offset(Geo.Offset(s, -w / 2), w / 2);
        return s;
    }

    /// <summary>Only the biggest piece of an area (smoothing can cut off a sliver).</summary>
    public static PathsD Biggest(PathsD a)
    {
        var outers = Geo.Tidy(a).Where(p => Clipper.Area(p) > 0).ToList();
        if (outers.Count < 2) return a;
        var big = outers.MaxBy(p => Clipper.Area(p))!;
        return Geo.Intersect(a, [big]);
    }

    /// <summary>
    /// Adds the layers (flat, u = x and v = <paramref name="z0"/> − z; bottom first) as pieces: the
    /// next layer's outline scored on each (when <paramref name="score"/>), a core <paramref name="coreW"/>
    /// wide through slots round <paramref name="core"/> in the layers above the bottom one for as far up
    /// as it fits, and any extra detail from <paramref name="detail"/>. Layers alike are cut as copies of one piece.
    /// </summary>
    public static void Add(Maker m, List<PathsD> layers, double z0, PointD core, double coreW, bool score,
                           Func<int, PathsD, (PathsD Etched, List<PathD> Lines)>? detail = null, string? what = null)
    {
        double T = m.T, f = m.Fit, B = Maker.Bridge;
        int n = layers.Count;
        while (n > 0 && Geo.Area(layers[n - 1]) < 1e-6) n--;
        if (n < 2) { m.Bad($"The {what ?? "piece"} is too low or too small for a {T:0.##} mm sheet."); return; }

        // How far up the core goes: every layer it passes must have sheet all round its slot.
        var slot = Joints.Centred(core.x, core.y, coreW + f, T + f);
        var round = Geo.Offset([slot], B);
        int coreTo = 0;
        for (int i = 1; i < n - 1; i++)
        {
            if (Geo.Area(Geo.Subtract(round, layers[i])) > 1e-4) break;
            coreTo = i;
        }
        bool hasCore = coreTo >= 2;

        var groups = new Dictionary<string, (PathsD Outline, PathsD Holes, PathsD Etched, List<PathD> Lines, List<int> Layers)>();
        var order = new List<string>();
        for (int i = 0; i < n; i++)
        {
            var layer = Geo.Tidy(layers[i]);
            var holes = new PathsD();
            if (hasCore && i >= 1 && i <= coreTo) holes.Add(slot);
            var cut = Geo.Subtract(layer, holes);
            var etched = new PathsD();
            var lines = new List<PathD>();
            if (score && i + 1 < n) lines.AddRange(Geo.Tidy(layers[i + 1]).Select(Geo.Loop));
            if (detail is not null) { var (de, dl) = detail(i, layer); etched.AddRange(de); lines.AddRange(dl); }
            int piece = 0;
            foreach (var outer in Geo.Tidy(cut).Where(p => Clipper.Area(p) > 0).OrderBy(p => Clipper.GetBounds(p).left))
            {
                var whole = new PathsD { outer };
                if (Geo.Area(whole) < 2) continue;
                var mine = Geo.Intersect(cut, whole);
                var inner = Geo.Subtract(whole, mine);
                string key = Key(mine) + "|" + Key(lines) + "|" + Key(etched);
                if (!groups.TryGetValue(key, out var g))
                {
                    g = (whole, inner, etched, lines, []);
                    groups[key] = g;
                    order.Add(key);
                }
                g.Layers.Add(i * 100 + piece++);
            }
        }
        bool split = groups.Values.Any(g => g.Layers.Any(l => l % 100 > 0));
        foreach (var key in order)
        {
            var g = groups[key];
            var idx = g.Layers;
            string Label(int l) => (l / 100 + 1).ToString() + (split ? "abcdefghij"[Math.Min(9, l % 100)].ToString() : "");
            string name = (idx.Count == 1 ? "Layer " : "Layers ") + string.Join(" and ", idx.Select(Label));
            m.Add(name, g.Outline, g.Holes, idx.Select(l => Pose.Up(0, (l / 100) * T + T / 2, z0)), grainAlongU: true, wood: false, etched: g.Etched, lines: g.Lines);
        }
        if (hasCore)
            m.Add("Core", [Geo.Rect(0, 0, coreW, coreTo * T)], [], [Pose.Front(core.x - coreW / 2, T, z0 - core.y)], grainAlongU: false, wood: false);
        m.Note($"{n} layers, {n * T:0.#} mm high. " + (hasCore
            ? "Glue the core upright in its place on the bottom layer, thread the layers on over it in order" + (m.PushFit ? " (their ribs grip it)" : "") + ", and glue the rest on top, each on the outline scored on the one below."
            : "Glue the layers in order, each on the outline scored on the one below."));
    }

    static string Key(PathsD a) => string.Join(";", a.Select(p => string.Join(" ", p.Select(q => $"{q.x:0.###},{q.y:0.###}"))));
    static string Key(List<PathD> a) => string.Join(";", a.Select(p => string.Join(" ", p.Select(q => $"{q.x:0.###},{q.y:0.###}"))));

    // ---- the shapes ----

    /// <summary>A stalagmite, or a clump of them on one base: each a wobbling spire that leans a little.</summary>
    public static void Stalagmite(Maker m)
    {
        var it = m.Item;
        double T = m.T, R0 = it.WidthMm / 2, H = it.HeightMm;
        if (R0 < 2.5) { m.Bad("The stalagmite is too thin: make it at least 5 mm across at the base."); return; }
        int n = (int)Math.Round(H / T);
        if (n < 3) { m.Bad($"The stalagmite is too low for a {T:0.##} mm sheet: make it at least {3 * T:0.#} mm high."); return; }
        if (n > 80) { m.Bad($"That's {n} layers: use a thicker sheet, or make it lower."); return; }
        int count = Math.Clamp(it.Cluster, 1, 5);
        var rnd = m.Rnd;
        var spires = new List<(double X, double Y, double R, double H, double Lean, double LeanA, Wobble W)>
        {
            (0, 0, R0, H, R0 * 0.3 * rnd.NextDouble(), rnd.NextDouble() * Math.PI * 2, Wobble.Make(rnd, 0.16)),
        };
        double a0 = rnd.NextDouble() * Math.PI * 2;
        for (int j = 1; j < count; j++)
        {
            double r = R0 * (0.42 + 0.25 * rnd.NextDouble()), h = H * (0.35 + 0.3 * rnd.NextDouble());
            double a = a0 + (j - 1) * 2 * Math.PI / Math.Max(1, count - 1) + (rnd.NextDouble() - 0.5) * 0.6, d = (R0 + r) * 0.62;
            spires.Add((Math.Cos(a) * d, Math.Sin(a) * d, r, h, r * 0.3 * rnd.NextDouble(), a + (rnd.NextDouble() - 0.5), Wobble.Make(rnd, 0.16)));
        }
        double reach = spires.Max(s => Math.Max(Math.Abs(s.X), Math.Abs(s.Y)) + s.R * 1.3) + 1, c = reach;
        var layers = new List<PathsD>();
        for (int i = 0; i < n; i++)
        {
            double y = i * T;
            var slice = new PathsD();
            foreach (var s in spires)
            {
                double t = y / s.H;
                if (t >= 1) continue;
                double r = s.R * Math.Pow(1 - t, 0.85);
                if (r < 0.85) continue;
                double lean = s.Lean * t * t;
                slice.Add(Blob(c + s.X + Math.Cos(s.LeanA) * lean, c + s.Y + Math.Sin(s.LeanA) * lean, r, r, s.W, t * 2.5));
            }
            layers.Add(Smooth(Geo.Union(slice)));
        }
        Add(m, layers, 2 * c, new PointD(c, c), Math.Clamp(R0 * 0.3, 1.4, 3), score: true, what: "stalagmite");
    }

    /// <summary>A giant mushroom, or a clump of up to three of different sizes, on a mossy base.</summary>
    public static void Mushroom(Maker m)
    {
        var it = m.Item;
        double T = m.T, C0 = it.WidthMm / 2, H = it.HeightMm;
        if (C0 < 3) { m.Bad("The mushroom's cap is too small: make it at least 6 mm across."); return; }
        int n = (int)Math.Round(H / T);
        if (n < 5) { m.Bad($"The mushroom is too low for a {T:0.##} mm sheet: make it at least {5 * T:0.#} mm high (five layers or more)."); return; }
        if (n > 80) { m.Bad($"That's {n} layers: use a thicker sheet, or make it lower."); return; }
        int count = Math.Clamp(it.Cluster, 1, 3);
        var rnd = m.Rnd;
        double s0 = Math.Max(1.8, C0 * 0.34);
        var shrooms = new List<(double X, double Y, double Cap, double Stem, int N, Wobble W)> { (0, 0, C0, s0, n, Wobble.Make(rnd, 0.06)) };
        double a0 = rnd.NextDouble() * Math.PI * 2;
        for (int j = 1; j < count; j++)
        {
            double k = j == 1 ? 0.6 : 0.45, cap = C0 * k, stem = Math.Max(1.4, s0 * (k + 0.1));
            int nj = (int)Math.Round(n * (j == 1 ? 0.62 : 0.45));
            if (nj < 5) continue;
            double a = a0 + (j == 1 ? 0 : 2.3), d = s0 + cap + 0.6;
            shrooms.Add((Math.Cos(a) * d, Math.Sin(a) * d, cap, stem, nj, Wobble.Make(rnd, 0.06)));
        }
        double c = shrooms.Max(s => Math.Max(Math.Abs(s.X), Math.Abs(s.Y)) + s.Cap * 1.1) + 1;
        // Each mushroom's layers: its stalk (wider at the foot), then its cap, a dome.
        var caps = new Dictionary<(int Layer, int Shroom), (double R, double Next)>();
        var layers = new List<PathsD>();
        var baseShape = new PathsD();
        foreach (var s in shrooms)
        {
            baseShape.Add(Blob(c + s.X, c + s.Y, s.Stem * 1.9, s.Stem * 1.9, s.W));
            if (s.X != 0 || s.Y != 0) baseShape.Add(Geo.Strip(new PointD(c, c), new PointD(c + s.X, c + s.Y), Math.Min(s0, s.Stem) * 2));
        }
        layers.Add(Smooth(Geo.Union(baseShape)));
        for (int i = 1; i < n; i++)
        {
            var slice = new PathsD();
            for (int j = 0; j < shrooms.Count; j++)
            {
                var s = shrooms[j];
                if (i >= s.N) continue;
                int nc = Math.Clamp((int)Math.Round(s.N * 0.3), 2, s.N - 3), first = s.N - nc;
                double R(int k) => k >= nc ? 0 : s.Cap * Math.Sqrt(Math.Max(0.05, 1 - Math.Pow((k + 0.5) / nc, 2)));
                if (i < first)
                {
                    double r = s.Stem * (i == 1 ? 1.25 : 1);
                    slice.Add(Geo.Circle(c + s.X, c + s.Y, r, 0.02));
                }
                else
                {
                    double r = Math.Max(1, R(i - first));
                    slice.Add(Blob(c + s.X, c + s.Y, r, r, s.W));
                    caps[(i, j)] = (r, i - first + 1 < nc ? Math.Max(1, R(i - first + 1)) : 0);
                }
            }
            layers.Add(Smooth(Geo.Union(slice)));
        }
        // Spots on the caps, where they show.
        var spots = new Dictionary<int, List<PathD>>();
        foreach (var ((layer, j), (r, next)) in caps)
        {
            var s = shrooms[j];
            if (!spots.TryGetValue(layer, out var list)) spots[layer] = list = [];
            double inner = next > 0 ? next + 0.4 : 0;
            int count2 = Math.Max(1, (int)((r * r - inner * inner) / 6));
            for (int k = 0; k < count2; k++)
            {
                double rad = inner + (r - 0.6 - inner) * Math.Sqrt(rnd.NextDouble()), a = rnd.NextDouble() * Math.PI * 2;
                if (rad <= inner) continue;
                list.Add(WoodFace.Dot(c + s.X + Math.Cos(a) * rad, c + s.Y + Math.Sin(a) * rad, Math.Clamp(r * 0.07, 0.25, 0.7)));
            }
        }
        Add(m, layers, 2 * c, new PointD(c, c), 1.4, score: true, (i, _) => ([], spots.TryGetValue(i, out var l) ? l : []), "mushroom");
    }

    /// <summary>A boulder: a lumpy rock, flat underneath, with a crack or two on top.</summary>
    public static void Boulder(Maker m)
    {
        var it = m.Item;
        double T = m.T, W = it.WidthMm, D = it.DepthMm, H = it.HeightMm;
        if (Math.Min(W, D) < 5) { m.Bad("The boulder is too small: make it at least 5 mm each way."); return; }
        int n = (int)Math.Round(H / T);
        if (n < 2) { m.Bad($"The boulder is too low for a {T:0.##} mm sheet: make it at least {2 * T:0.#} mm high."); return; }
        if (n > 80) { m.Bad($"That's {n} layers: use a thicker sheet, or make it lower."); return; }
        var rnd = m.Rnd;
        var w = Wobble.Make(rnd, 0.22);
        double yc = H * 0.38, cx = W / 2 + 1, cy = D / 2 + 1;
        var layers = new List<PathsD>();
        for (int i = 0; i < n; i++)
        {
            double y = (i + 0.5) * T, t = y >= yc ? (y - yc) / (H - yc) : (yc - y) / (yc * 1.6);
            double k = Math.Sqrt(Math.Max(0, 1 - t * t));
            double rx = W / 2 * k, ry = D / 2 * k;
            if (Math.Min(rx, ry) < 1.2) break;
            layers.Add(Smooth([Blob(cx, cy, rx, ry, w, i * 0.08)]));
        }
        var top = layers.Count - 1;
        (PathsD, List<PathD>) Cracks(int i, PathsD layer)
        {
            if (i != top && i != top - 1) return ([], []);
            var b = Geo.Bounds(layer);
            var lines = new List<PathD>();
            for (int k = 0; k < 2; k++)
            {
                var p = new PathD();
                double x = b.left + (b.right - b.left) * (0.2 + 0.6 * rnd.NextDouble()), y = b.top + (b.bottom - b.top) * (0.2 + 0.6 * rnd.NextDouble());
                double a = rnd.NextDouble() * Math.PI * 2, len = Math.Min(b.right - b.left, b.bottom - b.top) * 0.5;
                for (double s = 0; s <= len; s += 0.6)
                {
                    p.Add(new PointD(x, y));
                    a += (rnd.NextDouble() - 0.5) * 0.9;
                    x += Math.Cos(a) * 0.6; y += Math.Sin(a) * 0.6;
                }
                lines.Add(p);
            }
            return ([], lines);
        }
        Add(m, layers, D + 2, new PointD(cx, cy), Math.Clamp(Math.Min(W, D) * 0.12, 1.4, 3), score: true, Cracks, "boulder");
    }

    /// <summary>An urn: a foot, a round belly, a narrow neck and a lip; open at the neck if asked.</summary>
    public static void Urn(Maker m)
    {
        var it = m.Item;
        double T = m.T, R = it.WidthMm / 2, H = it.HeightMm;
        if (R < 2.5) { m.Bad("The urn is too thin: make it at least 5 mm across."); return; }
        int n = (int)Math.Round(H / T);
        if (n < 5) { m.Bad($"The urn is too low for a {T:0.##} mm sheet: make it at least {5 * T:0.#} mm high (five layers or more)."); return; }
        if (n > 80) { m.Bad($"That's {n} layers: use a thicker sheet, or make it lower."); return; }
        double Profile(double t) => t < 0.1 ? 0.55 : t < 0.78 ? 0.5 + 0.5 * Math.Sin(Math.PI * (t - 0.1) / 0.68) : t < 0.9 ? 0.42 : 0.56;
        double rim = Math.Max(1.1, R * 0.12), c = R + 0.5;
        var radii = Enumerable.Range(0, n).Select(i => Math.Round(R * Profile((i + 0.5) / n), 3)).ToList();
        // Open: the neck and lip are rings, if the neck's wide enough for a hole.
        int openFrom = n;
        if (it.Open)
        {
            openFrom = Enumerable.Range(1, n - 1).First(i => (i + 0.5) / n >= 0.78 || i == n - 1);
            if (radii.Skip(openFrom).Min() - rim < 0.6 || openFrom < 2) { openFrom = n; m.Warn("The urn's neck is too narrow to open: make it wider. This one is closed."); }
        }
        double hole = openFrom < n ? radii.Skip(openFrom).Min() - rim : 0;
        var layers = radii.Select((r, i) => i >= openFrom
            ? Geo.Subtract([Geo.Circle(c, c, r, 0.02)], [Geo.Circle(c, c, hole, 0.02)])
            : new PathsD { Geo.Circle(c, c, r, 0.02) }).ToList();
        // A band round the shoulder where it shows.
        (PathsD, List<PathD>) Band(int i, PathsD layer)
        {
            if (i + 1 >= n || radii[i] - radii[i + 1] < 0.9) return ([], []);
            double r = (radii[i] + radii[i + 1]) / 2;
            return ([], [Geo.Loop(Geo.Circle(c, c, r, 0.02))]);
        }
        Add(m, layers, 2 * c, new PointD(c, c), Math.Clamp(R * 0.25, 1.4, 3), score: false, Band, "urn");
    }
}
