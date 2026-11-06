using Clipper2Lib;

namespace PhotonCanvas.Addon.Terrain;

/// <summary>
/// Every preset texture the terrain makers etch (brick, stone, planks, metal and the themes' own),
/// drawn once here so a wall, a ruin, a bridge and a floor made with the same sizes, wear and
/// variation match stone for stone. A texture is made of raised pieces (what's between them is etched
/// away), extra areas etched as they are (pits, weathered patches, vents) and lines scored as they are
/// (grain, fine cracks, scratches, seams), over a face from (0, 0) to (length, height) in millimetres
/// with y up, running a little over every edge (the maker clips them to its piece).
///
/// Each piece is then aged by <see cref="Weathering"/>, so the etch varies the way real walls do:
/// gaps of different widths, knocked corners, pitted and weathered faces, cracks.
///
/// Compiled into each terrain add-on that draws textures (see its project file): nothing is shared at run time.
/// </summary>
internal static class TextureEngine
{
    /// <summary>
    /// A preset: its two sizes' names (null = not used) and defaults (mm, for 28 mm figures), whether it
    /// has a groove (and its default), whether its layout is random, its default detail size (mm), what
    /// it's made of and what it looks like.
    /// </summary>
    public sealed record Preset(string Name, string? SizeALabel, double SizeA, string? SizeBLabel, double SizeB, bool UsesGroove, double Groove, double Detail, Stuff Stuff, string About);

    public static readonly IReadOnlyList<Preset> Presets =
    [
        new("Brick", "Brick length", 8, "Brick height", 3.5, true, 0.6, 0.5, Stuff.Brick, "Bricks in running bond, each row moved along half a brick: worn edges, knocked corners and the odd spalled or cracked brick."),
        new("Stone blocks", "Block length", 12, "Course height", 6, true, 0.8, 0.6, Stuff.Stone, "Cut stone in rows of different heights, blocks of different lengths, weathered and chipped."),
        new("Fieldstone", "Stone size", 9, "Course height", 6, true, 0.9, 0.6, Stuff.Stone, "Rough, rounded stones of uneven shape, roughly in rows."),
        new("Tiles", "Tile width", 6, "Tile height", 6, true, 0.6, 0.4, Stuff.Tile, "Square tiles or blocks in straight rows and columns, the odd one chipped or cracked."),
        new("Wood planks (upright)", "Plank width", 5, "Plank length", 35, true, 0.5, 0.5, Stuff.Wood, "Upright boards with butt joints, flowing grain, knots, splits and nail holes."),
        new("Wood planks (across)", "Plank width", 5, "Plank length", 35, true, 0.5, 0.5, Stuff.Wood, "Boards running along the wall, with flowing grain, knots, splits and nail holes."),
        new("Timber frame", "Beam width", 3, "Bay width", 20, false, 0.5, 0.5, Stuff.Wood, "Tudor style: posts, rails and braces stand out with their grain, the plaster between them is etched and cracked."),
        new("Corrugated metal", "Ridge pitch", 2.5, null, 0, false, 0, 0.5, Stuff.Metal, "Upright ridges, dented and scratched, for sheds and industrial or sci-fi scenery."),
        new("Metal plates", "Plate width", 20, "Plate height", 14, true, 0.5, 0.5, Stuff.Metal, "Riveted plates, each row moved along half a plate, with patch plates, weld seams, scratches and rust pits."),
        new("Hexagons", "Hexagon size", 8, null, 0, true, 0.7, 0.5, Stuff.Metal, "Hexagonal tiles or armour, some with an inset face, for sci-fi walls."),
        new(ThemeTextures.DungeonBlocks, "Block length", 14, "Course height", 8, true, 0.9, 0.7, Stuff.Stone, "Big, worn stone blocks, chipped, spalled, pitted and cracked: dungeons, crypts and keeps."),
        new(ThemeTextures.CaveRock, "Rock size", 16, "Layer height", 10, true, 1.0, 0.8, Stuff.SoftStone, "Rough rock in uneven layers with bedding lines, split by cracks and pitted: caves, cliffs and mines."),
        new(ThemeTextures.SciFiPanels, "Panel width", 16, "Panel height", 12, true, 0.5, 0.6, Stuff.Metal, "Wide bays between bolted pillars: a stepped armour plate in a deep recess with pipes, cables and control boxes, vents along the floor and angled plates along the top, scratched and worn."),
        new(CyberWalls.HabConcrete, "Panel width", 18, "Panel height", 12, true, 0.5, 0.5, Stuff.Stone, "Cyberpunk city concrete: cast panels in courses with tie holes, a plinth, and the services bolted on (downpipes, air-conditioning units by the windows, vents, junction boxes and conduit), laid out afresh on every wall."),
        new(CyberWalls.SteelCladding, "Bay width", 20, "Sheet height", 16, true, 0.5, 0.5, Stuff.Metal, "A steel frame bolted through gusset plates with corrugated sheets between, patch plates and cross braces here and there, vents and conduit: sheds, depots and slum stacks."),
        new(CyberWalls.Shopfront, "Panel width", 18, "Panel height", 12, true, 0.5, 0.5, Stuff.Stone, "A cyberpunk street front: concrete with a roll-up shutter, a long sign box, torn posters, tags, a meter box and conduit."),
        new(CyberWalls.GlassFacade, "Mullion spacing", 12, "Band spacing", 24, true, 0.5, 0.5, Stuff.Metal, "A tower's curtain wall: mullions and spandrel bands standing proud, the glass sunk between with transoms, blinds and ceiling lights."),
        new(ThemeTextures.Sandstone, "Block length", 16, "Course height", 7, true, 0.7, 0.7, Stuff.SoftStone, "Long, soft-cornered sandstone blocks eaten by the wind: rounded edges, wind lines and clusters of pits."),
        new(ThemeTextures.Adobe, "Patch size", 14, "Brick height", 3, true, 0.5, 0.6, Stuff.Plaster, "Smooth mud plaster with ragged patches fallen away to show worn bricks beneath, and spreading cracks."),
    ];

    public static Preset? Find(string? name) => Presets.FirstOrDefault(p => p.Name == name);

    /// <summary>
    /// The texture's raised pieces, etched areas and scored lines over a face <paramref name="length"/> ×
    /// <paramref name="height"/> mm. <paramref name="x0"/>–<paramref name="x1"/> is the part between plain
    /// ends and <paramref name="top"/> the top of the body (the timber frame lines its posts and top plate
    /// up with them). <paramref name="a"/>, <paramref name="b"/> are the texture's two sizes,
    /// <paramref name="groove"/> the gap between pieces, <paramref name="wear"/> 0–1 (0.5 normal) and
    /// <paramref name="detail"/> the size of the smallest marks (0 = the preset's own). Empty for a name
    /// that isn't a preset.
    /// </summary>
    public static (PathsD Raised, PathsD Etched, List<PathD> Lines) Make(string name, double length, double height, double x0, double x1, double top,
        double a, double b, double groove, int seed, double wear = 0.5, double detail = 0, bool linesOnly = false, IReadOnlyList<RectD>? avoid = null)
    {
        if (Find(name) is not { } p) return ([], [], []);
        a = Math.Max(0.5, a);
        b = Math.Max(0.5, b);
        // The groove can't be wider than a third of a piece (of the one size, when there's only one).
        double g = Math.Clamp(groove, 0.05, (p.SizeBLabel is null ? a : Math.Min(a, b)) / 3);
        // Too many pieces to draw sensibly (a tiny size over a big face): nothing rather than a hang.
        double cells = length * height / (a * (p.SizeBLabel is null ? a : b));
        if (cells > 40000 || length <= 0 || height <= 0) return ([], [], []);
        var t = new Texture(seed, new Wear(wear, detail > 0 ? detail : p.Detail, linesOnly), p.Stuff);
        switch (name)
        {
            case "Brick": t.Bricks(length, height, a, b, g, 0.5); break;
            case "Tiles": t.Bricks(length, height, a, b, g, 0); break;
            case "Stone blocks": t.Blocks(length, height, a, b, g, 0.6, 0.9, 0.75, 0.5); break;
            case "Fieldstone": t.Fieldstone(length, height, a, b, g); break;
            case "Wood planks (upright)": t.Planks(length, height, a, b, g, upright: true); break;
            case "Wood planks (across)": t.Planks(length, height, a, b, g, upright: false); break;
            case "Timber frame": t.Timber(length, x0, x1, top, a, b); break;
            case "Corrugated metal": t.Corrugated(length, height, a); break;
            case "Metal plates": t.Plates(length, height, a, b, g); break;
            case "Hexagons": t.Hexagons(length, height, a, g); break;
            case ThemeTextures.DungeonBlocks: t.Blocks(length, height, a, b, g, 0.7, 0.6, 0.85, 0.3); break;
            case ThemeTextures.CaveRock: t.Cave(length, height, a, b, g); break;
            case ThemeTextures.SciFiPanels: new SciFi(t).Make(length, height, a, b, g); break;
            case CyberWalls.HabConcrete: new CyberWalls(t, avoid ?? []).Concrete(length, height, a, b, g); break;
            case CyberWalls.SteelCladding: new CyberWalls(t, avoid ?? []).Cladding(length, height, a, b, g); break;
            case CyberWalls.Shopfront: new CyberWalls(t, avoid ?? []).Shop(length, height, a, b, g); break;
            case CyberWalls.GlassFacade: new CyberWalls(t, avoid ?? []).Glass(length, height, a, b, g); break;
            case ThemeTextures.Sandstone: t.Sand(length, height, a, b, g); break;
            case ThemeTextures.Adobe: t.Plaster(length, height, a, b, g); break;
        }
        return (t.Raised, Poly.Union(t.Etched), t.Lines);
    }

    /// <summary>One texture being drawn: the pieces so far, and the weathering that ages them as they're added.</summary>
    internal sealed class Texture(int seed, Wear wear, Stuff stuff)
    {
        public readonly PathsD Raised = [];
        public readonly PathsD Etched = [];
        public readonly List<PathD> Lines = [];
        public readonly Random Rnd = new(seed);
        public readonly Weathering Weather = new(seed, wear);
        public readonly Wear Wear = wear;
        public double D => Weather.Detail;
        public Noise Noise => Weather.Noise;

        /// <summary>Adds a raised piece, worn as what it's made of.</summary>
        public void Piece(PathD p, Stuff? as_ = null) => Raised.AddRange(Weather.Piece(p, as_ ?? stuff, Etched, Lines));

        /// <summary>Adds a raised piece exactly as drawn.</summary>
        public void Clean(PathD p) => Raised.Add(p);

        public void Etch(PathD p) => Etched.Add(p);

        public void Score(PathD p)
        {
            if (p.Count >= 2) Lines.Add(p);
        }

        static PathD Box(double x0, double y0, double x1, double y1, double g) => Poly.Rect(x0 + g / 2, y0 + g / 2, x1 - g / 2, y1 - g / 2);

        // ---- brick, tiles, blocks ----

        public void Bricks(double len, double h, double a, double b, double g, double shift)
        {
            int rows = (int)Math.Ceiling(h / b) + 1;
            for (int r = 0; r < rows; r++)
            {
                double y = r * b, off = r % 2 == 1 ? shift * a : 0;
                for (double x = -off; x < len + a; x += a) Piece(Box(x, y, x + a, y + b, g));
            }
        }

        /// <summary>Courses of blocks: heights within ±<paramref name="hv"/>/2, lengths from <paramref name="lMin"/> to <paramref name="lMin"/>+<paramref name="lv"/> blocks.</summary>
        public void Blocks(double len, double h, double a, double b, double g, double lMin, double lv, double hMin, double hv)
        {
            for (double y = 0; y < h + b;)
            {
                double hh = b * (hMin + hv * Rnd.NextDouble());
                for (double x = -a * Rnd.NextDouble(); x < len + a;)
                {
                    double w = a * (lMin + lv * Rnd.NextDouble());
                    // Now and then a block is split into two smaller ones stacked (a repair or a shim course).
                    if (Rnd.NextDouble() < 0.04 && hh > b * 0.8 && w > a * 0.6)
                    {
                        double m = y + hh * (0.4 + 0.2 * Rnd.NextDouble());
                        Piece(Box(x, y, x + w, m, g));
                        Piece(Box(x, m, x + w, y + hh, g));
                    }
                    else Piece(Box(x, y, x + w, y + hh, g));
                    x += w;
                }
                y += hh;
            }
        }

        // ---- wandering rows: fieldstone and cave rock ----

        sealed class Rows(double x0, double step, int n)
        {
            public double At(double[] e, double x)
            {
                double t = Math.Clamp((x - x0) / step, 0, n - 1.0001);
                int i = (int)t;
                return e[i] + (e[i + 1] - e[i]) * (t - i);
            }

            public List<PointD> Along(double[] e, double xa, double xb)
            {
                var pts = new List<PointD> { new(xa, At(e, xa)) };
                for (int i = Math.Max(0, (int)Math.Floor((xa - x0) / step) + 1); i < n && x0 + i * step < xb - 1e-6; i++) pts.Add(new(x0 + i * step, e[i]));
                pts.Add(new(xb, At(e, xb)));
                return pts;
            }
        }

        /// <summary>Rows whose edges wander up and down, split into stones with leaning sides, shrunk and rounded off.</summary>
        public void Fieldstone(double len, double h, double a, double b, double g)
        {
            double step = Math.Max(0.8, a / 3), x0 = -a, x1 = len + a;
            int n = (int)Math.Ceiling((x1 - x0) / step) + 1;
            var rows = new Rows(x0, step, n);
            double[] Edge(double y) => [.. Enumerable.Range(0, n).Select(_ => y + (Rnd.NextDouble() - 0.5) * 0.45 * b)];
            double ya = -b * 0.5;
            var lower = Edge(ya);
            double round = Math.Min(a, b) * 0.2;
            while (ya < h + b)
            {
                double yb = ya + b * (0.75 + 0.5 * Rnd.NextDouble());
                var upper = Edge(yb);
                double side = x0 - a * Rnd.NextDouble();
                double lowX = side + (Rnd.NextDouble() - 0.5) * a * 0.3, upX = side + (Rnd.NextDouble() - 0.5) * a * 0.3;
                while (Math.Min(lowX, upX) < x1)
                {
                    // Stones of very different sizes: now and then a small one tucked in between.
                    double size = Rnd.NextDouble() < 0.18 ? 0.35 + 0.25 * Rnd.NextDouble() : 0.6 + 0.8 * Rnd.NextDouble();
                    double next = Math.Max(lowX, upX) + a * size;
                    double nLow = next + (Rnd.NextDouble() - 0.5) * a * 0.3, nUp = next + (Rnd.NextDouble() - 0.5) * a * 0.3;
                    var stone = new PathD(rows.Along(lower, lowX, nLow));
                    var topEdge = rows.Along(upper, upX, nUp);
                    topEdge.Reverse();
                    stone.AddRange(topEdge);
                    foreach (var p in Poly.Offset(Poly.Offset([stone], -(g / 2 + round), round: true), round, round: true)) Piece(p);
                    lowX = nLow;
                    upX = nUp;
                }
                ya = yb;
                lower = upper;
            }
        }

        /// <summary>
        /// Layers of rock whose edges wander a long way up and down, split into lumps by leaning cracks
        /// that bend on their way; bedding lines run through the rock along its layers.
        /// </summary>
        public void Cave(double len, double h, double a, double b, double g)
        {
            double step = Math.Max(0.8, a / 5), x0 = -a, x1 = len + a;
            int n = (int)Math.Ceiling((x1 - x0) / step) + 1;
            var rows = new Rows(x0, step, n);
            double tilt = (Rnd.NextDouble() - 0.5) * 0.25;
            double[] Edge(double y)
            {
                double o = Rnd.NextDouble() * 1000;
                return [.. Enumerable.Range(0, n).Select(i =>
                {
                    double x = x0 + i * step;
                    return y + tilt * x + b * 0.45 * Noise.Fbm(x + o, o, a * 2.2, 3) + (Rnd.NextDouble() - 0.5) * 0.12 * b;
                })];
            }
            double ya = -b * 0.6;
            var lower = Edge(ya);
            while (ya < h + b + Math.Abs(tilt) * len)
            {
                double yb = ya + b * (0.6 + 0.8 * Rnd.NextDouble());
                var upper = Edge(yb);
                double side = x0 - a * Rnd.NextDouble();
                double lowX = side + (Rnd.NextDouble() - 0.5) * a * 0.6, upX = side + (Rnd.NextDouble() - 0.5) * a * 0.6;
                while (Math.Min(lowX, upX) < x1)
                {
                    double next = Math.Max(lowX, upX) + a * (0.5 + 1.3 * Rnd.NextDouble());
                    double nLow = next + (Rnd.NextDouble() - 0.5) * a * 0.7, nUp = next + (Rnd.NextDouble() - 0.5) * a * 0.7;
                    var rock = new PathD(rows.Along(lower, lowX, nLow));
                    // The crack up the right side bends twice on its way.
                    double yl = rows.At(lower, nLow), yu = rows.At(upper, nUp);
                    for (int k = 1; k <= 2; k++)
                    {
                        double t = k / 3.0;
                        rock.Add(new PointD(nLow + (nUp - nLow) * t + (Rnd.NextDouble() - 0.5) * a * 0.3, yl + (yu - yl) * t));
                    }
                    var topEdge = rows.Along(upper, upX, nUp);
                    topEdge.Reverse();
                    rock.AddRange(topEdge);
                    var shrunk = Poly.Offset([rock], -g / 2);
                    foreach (var p in shrunk)
                    {
                        var worn = Weather.Piece(p, Stuff.SoftStone, Etched, Lines);
                        Raised.AddRange(worn);
                        // Bedding lines along the layer, broken here and there.
                        var rb = Poly.Bounds(p);
                        if (rb.bottom - rb.top > D * 4)
                        {
                            int count = 1 + Rnd.Next(3);
                            var found = new List<PathD>();
                            for (int k = 0; k < count; k++)
                            {
                                double f = (k + 0.5 + (Rnd.NextDouble() - 0.5) * 0.6) / count;
                                var line = new PathD();
                                for (double x = rb.left; x <= rb.right; x += step * 0.5)
                                {
                                    double y = rows.At(lower, x) + (rows.At(upper, x) - rows.At(lower, x)) * f + Noise.At(x / (D * 4), f * 10) * D * 0.4;
                                    line.Add(new PointD(x, y));
                                }
                                // Broken into dashes of different lengths.
                                double run = 0, on = a * (0.3 + Rnd.NextDouble()), off2 = D * (1 + 3 * Rnd.NextDouble());
                                var dash = new PathD();
                                for (int i = 0; i < line.Count; i++)
                                {
                                    if (i > 0) run += Poly.Dist(line[i - 1], line[i]);
                                    if (run < on) dash.Add(line[i]);
                                    else if (run < on + off2) { if (dash.Count >= 2) found.Add(dash); dash = []; }
                                    else { run = 0; on = a * (0.3 + Rnd.NextDouble()); off2 = D * (1 + 3 * Rnd.NextDouble()); }
                                }
                                if (dash.Count >= 2) found.Add(dash);
                            }
                            Lines.AddRange(Poly.ClipLines(found, Poly.Offset(worn, -Math.Max(0.2, D * 0.4))));
                        }
                    }
                    lowX = nLow;
                    upX = nUp;
                }
                ya = yb;
                lower = upper;
            }
        }

        // ---- wood ----

        public void Planks(double len, double h, double a, double b, double g, bool upright)
        {
            // Drawn upright on a face turned if need be, then turned back.
            double across = upright ? len : h, along = upright ? h : len;
            var raised = new PathsD();
            var etched = new PathsD();
            var lines = new List<PathD>();
            for (double x = 0; x < across + a; x += a)
            {
                for (double y = -b * Rnd.NextDouble(); y < along + b;)
                {
                    double l = b * (0.6 + 0.8 * Rnd.NextDouble());
                    // Butt joints: a board ends a little short now and then, so the joints aren't all alike.
                    double y0 = y + g / 2, y1 = y + l - g / 2 - (Rnd.NextDouble() < 0.3 ? g * Rnd.NextDouble() : 0);
                    var board = Box(x, y, x + a, y + l, g);
                    board[0] = new PointD(board[0].x, y0);
                    board[1] = new PointD(board[1].x, y0);
                    board[2] = new PointD(board[2].x, y1);
                    board[3] = new PointD(board[3].x, y1);
                    var worn = Weather.Piece(board, Stuff.Wood, etched, lines);
                    raised.AddRange(worn);
                    Grain(x + g / 2, y0, x + a - g / 2, y1, worn, etched, lines);
                    y += l;
                }
            }
            if (upright)
            {
                Raised.AddRange(raised);
                Etched.AddRange(etched);
                Lines.AddRange(lines);
                return;
            }
            static PointD Swap(PointD p) => new(p.y, p.x);
            Raised.AddRange(Poly.Map(raised, Swap));
            Etched.AddRange(Poly.Map(etched, Swap));
            Lines.AddRange(Poly.Map(lines, Swap));
        }

        /// <summary>
        /// A board's grain (it runs up the board): lines that flow along it, bending round the odd knot and
        /// fading out, nail holes at each end and, on a worn board, a split from one end.
        /// </summary>
        void Grain(double x0, double y0, double x1, double y1, PathsD board, PathsD etched, List<PathD> lines)
        {
            double w = x1 - x0, l = y1 - y0;
            if (w < 1.2 || l < 3) return;
            var found = new List<PathD>();
            // A knot or two on some boards: a small etched heart with a ring round it; the grain bends round it.
            var knots = new List<(double X, double Y, double R)>();
            if (Rnd.NextDouble() < 0.35 && w > 2.5 && l > 10)
            {
                double r = Math.Min(w * 0.18, D * (0.6 + 0.6 * Rnd.NextDouble()));
                double kx = x0 + w * (0.3 + 0.4 * Rnd.NextDouble()), ky = y0 + l * (0.2 + 0.6 * Rnd.NextDouble());
                knots.Add((kx, ky, r));
                double lean = (Rnd.NextDouble() - 0.5) * 0.4;
                if (!Wear.LinesOnly) etched.Add(Poly.Ellipse(kx, ky, r * 0.55, r * 0.8, lean));
                found.Add(Poly.Loop(Poly.Ellipse(kx, ky, r * 1.25, r * 1.9)));
            }
            // Grain lines: about one every 0.9 mm across the board (at least one), wavy and bending round knots.
            int count = Math.Clamp((int)Math.Round(w / Math.Max(0.7, D * 1.6)), 1, 8);
            double phase = Rnd.NextDouble() * 1000;
            for (int k = 0; k < count; k++)
            {
                double gx = x0 + w * (k + 0.5) / count + (Rnd.NextDouble() - 0.5) * w / count * 0.5;
                // Some lines start late or stop early, as grain runs out of a sawn board.
                double from = y0 + (Rnd.NextDouble() < 0.4 ? l * 0.35 * Rnd.NextDouble() : 0.6);
                double to = y1 - (Rnd.NextDouble() < 0.4 ? l * 0.35 * Rnd.NextDouble() : 0.6);
                var line = new PathD();
                for (double t = from; t <= to; t += Math.Max(0.4, D * 0.8))
                {
                    double x = gx + Noise.Fbm(gx * 3 + phase, t, D * 14, 2) * w * 0.12;
                    foreach (var (kx, ky, r) in knots)
                    {
                        double dy = (t - ky) / (r * 3), push = Math.Exp(-dy * dy) * r * 1.6;
                        x += x >= kx ? push : -push;
                    }
                    line.Add(new PointD(Math.Clamp(x, x0 + 0.15, x1 - 0.15), t));
                }
                if (line.Count >= 2) found.Add(line);
            }
            // Nails: two at each end, if the board's wide enough (their wobble from the wear's own dice, so the layout never depends on it).
            var wr = Weather.Random;
            if (w >= 2.5 && l >= 6 && !Wear.LinesOnly)
                foreach (double ny in new[] { y0 + Math.Min(1.5, l * 0.15), y1 - Math.Min(1.5, l * 0.15) })
                    foreach (double nx in new[] { x0 + w * 0.28, x1 - w * 0.28 })
                        etched.Add(Poly.Circle(nx + (wr.NextDouble() - 0.5) * 0.15, ny + (wr.NextDouble() - 0.5) * 0.15, Math.Clamp(D * 0.35, 0.18, 0.35)));
            lines.AddRange(Poly.ClipLines(found, Poly.Offset(board, -0.12)));
        }

        public void Timber(double len, double fx0, double fx1, double top, double beam, double bay)
        {
            double span = Math.Max(beam * 3, fx1 - fx0);
            int bays = Math.Max(1, (int)Math.Round(span / bay));
            double w = span / bays;
            var beams = new List<(PathD Shape, bool Upright)>
            {
                (Poly.Rect(-1, -1, len + 1, beam), false),                                  // sill
                (Poly.Rect(-1, top - beam, len + 1, top + 1), false),                       // top plate
                (Poly.Rect(-1, top * 0.5 - beam / 2, len + 1, top * 0.5 + beam / 2), false), // middle rail
            };
            for (int i = 0; i <= bays; i++)
            {
                double x = fx0 + i * w;
                beams.Add((Poly.Rect(x - beam / 2, -1, x + beam / 2, top + 1), true));
            }
            // A brace across the lower half of every other bay, leaning alternately.
            var braces = new PathsD();
            for (int i = 0; i < bays; i += 2)
            {
                double xa = fx0 + i * w, xb = xa + w;
                var (p, q) = (i / 2) % 2 == 0 ? (new PointD(xa, 0), new PointD(xb, top * 0.5)) : (new PointD(xb, 0), new PointD(xa, top * 0.5));
                braces.Add(Poly.Strip(p, q, beam));
            }
            var all = Poly.Union([.. beams.Select(b => b.Shape), .. braces]);
            // The frame's edges worn a little; the plaster between cracked.
            var worn = new PathsD();
            foreach (var p in all)
                worn.Add(Clipper.Area(p) > 0 ? Poly.Roughen(p, Noise, Math.Min(beam * 0.12, D * 0.25 * Wear.Amount * 2), D * 2, Math.Max(0.2, D * 0.4)) : p);
            worn = Poly.Union(worn);
            Raised.AddRange(worn);
            // Grain along each beam.
            var grain = new List<PathD>();
            foreach (var (shape, upright) in beams)
            {
                var bb = Poly.Bounds(shape);
                int n = Math.Clamp((int)(beam / Math.Max(0.8, D * 1.8)), 1, 4);
                for (int k = 0; k < n; k++)
                {
                    var line = new PathD();
                    double o = Rnd.NextDouble() * 1000;
                    if (upright)
                    {
                        double gx = bb.left + (bb.right - bb.left) * (k + 0.5) / n;
                        for (double y = bb.top; y <= bb.bottom; y += 0.8) line.Add(new PointD(gx + Noise.Fbm(o, y, D * 12, 2) * beam * 0.12, y));
                    }
                    else
                    {
                        double gy = bb.top + (bb.bottom - bb.top) * (k + 0.5) / n;
                        for (double x = bb.left; x <= bb.right; x += 0.8) line.Add(new PointD(x, gy + Noise.Fbm(x, o, D * 12, 2) * beam * 0.12));
                    }
                    grain.Add(line);
                }
            }
            Lines.AddRange(Poly.ClipLines(grain, Poly.Offset(worn, -0.2)));
            // Hairline cracks in the plaster panels.
            var plaster = Poly.Subtract([Poly.Rect(-1, -1, len + 1, top + 1)], Poly.Offset(worn, D * 0.4));
            var wr = Weather.Random;
            int cracks = (int)Math.Round(bays * Wear.Amount * 2 * (0.5 + wr.NextDouble()));
            var found = new List<PathD>();
            for (int i = 0; i < cracks; i++)
            {
                double cx = fx0 + span * wr.NextDouble(), cy = top * wr.NextDouble();
                found.Add(Crack(cx, cy, wr.NextDouble() * Math.PI * 2, bay * (0.2 + 0.3 * wr.NextDouble()), wr));
            }
            Lines.AddRange(Poly.ClipLines(found, plaster));
        }

        // ---- metal ----

        public void Corrugated(double len, double h, double pitch)
        {
            var ridges = new PathsD();
            for (double x = 0; x < len + pitch; x += pitch) ridges.Add(Poly.Rect(x, -1, x + pitch / 2, h + 1));
            Etched.AddRange(ridges);
            var wr = Weather.Random;
            // Dents across the ridges and rust holes, more of them the more worn; sheet seams now and then.
            double k = Wear.Amount * 2;
            int dents = (int)Math.Round(len * h / 900 * k * (0.5 + wr.NextDouble()));
            for (int i = 0; i < dents; i++)
            {
                double cx = len * wr.NextDouble(), cy = h * wr.NextDouble(), r = pitch * (0.6 + 0.8 * wr.NextDouble());
                if (!Wear.LinesOnly) Etched.Add(Poly.Blob(cx, cy, r, r * 0.7, 0.6, wr, wr.NextDouble() * 3));
            }
            double sheet = Math.Max(pitch * 6, 30 + 20 * wr.NextDouble());
            for (double y = sheet; y < h; y += sheet)
            {
                // An overlap: a line of rivets along it.
                Score([new(-1, y), new(len + 1, y)]);
                for (double x = pitch * 0.75; x < len; x += pitch * 2) Score(Poly.Loop(Poly.Circle(x, y + 0.8, Math.Clamp(D * 0.3, 0.2, 0.4))));
            }
            var shape = new PathsD { Poly.Rect(-1, -1, len + 1, h + 1) };
            int n = (int)Math.Round(len * h / 250 * k);
            var found = new List<PathD>();
            for (int i = 0; i < n; i++)
            {
                double x = len * wr.NextDouble(), y = h * wr.NextDouble(), a2 = (wr.NextDouble() - 0.5) * 0.8, l = D * (2 + 5 * wr.NextDouble());
                found.Add([new(x, y), new(x + Math.Cos(a2) * l, y + Math.Sin(a2) * l)]);
            }
            Lines.AddRange(Poly.ClipLines(found, shape));
        }

        public void Plates(double len, double h, double a, double b, double g)
        {
            double rivet = Math.Clamp(Math.Min(a, b) * 0.03, 0.2, 0.45), inset = Math.Max(1, rivet * 3);
            int rows = (int)Math.Ceiling(h / b) + 1;
            for (int r = 0; r < rows; r++)
            {
                double y = r * b, off = r % 2 == 1 ? a / 2 : 0;
                for (double x = -off; x < len + a; x += a)
                {
                    Piece(Box(x, y, x + a, y + b, g));
                    Rivets(x + inset, y + inset, x + a - inset, y + b - inset, rivet, 3);
                    // Patches and welds are wear, so they come from the wear's own dice.
                    var wr = Weather.Random;
                    double pick = wr.NextDouble();
                    if (pick < 0.12 * Wear.Amount * 2 && a > 8 && b > 8)
                    {
                        // A patch plate riveted over a hole: a smaller raised plate with its own ring of rivets.
                        double pw = a * (0.25 + 0.2 * wr.NextDouble()), ph = b * (0.25 + 0.2 * wr.NextDouble());
                        double px = x + inset * 2 + (a - pw - inset * 4) * wr.NextDouble(), py = y + inset * 2 + (b - ph - inset * 4) * wr.NextDouble();
                        Score(Poly.Loop(Poly.Rect(px, py, px + pw, py + ph)));
                        Score(Poly.Loop(Poly.Rect(px + 0.25, py + 0.25, px + pw - 0.25, py + ph - 0.25)));
                        Rivets(px + rivet * 2.5, py + rivet * 2.5, px + pw - rivet * 2.5, py + ph - rivet * 2.5, rivet * 0.8, 2);
                    }
                    else if (pick < 0.2)
                    {
                        // A weld seam across the plate: a wavy bead.
                        double sy = y + b * (0.3 + 0.4 * wr.NextDouble());
                        var bead = new PathD();
                        for (double sx = x + inset; sx <= x + a - inset; sx += 0.35) bead.Add(new PointD(sx, sy + 0.18 * Math.Sin(sx * 9)));
                        Score(bead);
                    }
                }
            }
        }

        void Rivets(double x0, double y0, double x1, double y1, double r, double spacing)
        {
            if (x1 <= x0 || y1 <= y0) return;
            int nx = Math.Max(1, (int)Math.Round((x1 - x0) / spacing)), ny = Math.Max(1, (int)Math.Round((y1 - y0) / spacing));
            for (int i = 0; i <= nx; i++)
            {
                Score(Poly.Loop(Poly.Circle(x0 + (x1 - x0) * i / nx, y0, r)));
                Score(Poly.Loop(Poly.Circle(x0 + (x1 - x0) * i / nx, y1, r)));
            }
            for (int j = 1; j < ny; j++)
            {
                Score(Poly.Loop(Poly.Circle(x0, y0 + (y1 - y0) * j / ny, r)));
                Score(Poly.Loop(Poly.Circle(x1, y0 + (y1 - y0) * j / ny, r)));
            }
        }

        public void Hexagons(double len, double h, double size, double g)
        {
            // Flat-topped hexagons "size" across their flats, in columns; some have an inset face (a groove ring).
            double r = size / Math.Sqrt(3), dx = 1.5 * r, inner = r - g / Math.Sqrt(3);
            int col = 0;
            PathD Hex(double cx, double cy, double rr)
            {
                var hex = new PathD(6);
                for (int k = 0; k < 6; k++) hex.Add(new PointD(cx + rr * Math.Cos(k * Math.PI / 3), cy + rr * Math.Sin(k * Math.PI / 3)));
                return hex;
            }
            for (double x = 0; x < len + size; x += dx, col++)
                for (double y = col % 2 == 0 ? 0 : size / 2; y < h + size; y += size)
                {
                    var hex = Hex(x, y, inner);
                    Piece(hex);
                    if (Rnd.NextDouble() < 0.3 && inner > 2.5)
                    {
                        // Inset: a groove ring a little in from the edge, so the face looks set into a rim.
                        double ring = Math.Max(0.6, inner * 0.22), gap = Math.Max(0.3, g * 0.6);
                        Etched.AddRange(Poly.Subtract([Hex(x, y, inner - ring)], [Hex(x, y, inner - ring - gap)]));
                    }
                }
        }

        // ---- sandstone and adobe ----

        /// <summary>Courses of long sandstone blocks with soft, wind-eaten edges, wind lines along them and clusters of pits.</summary>
        public void Sand(double len, double h, double a, double b, double g)
        {
            for (double y = 0; y < h + b;)
            {
                double hh = b * (0.85 + 0.3 * Rnd.NextDouble());
                for (double x = -a * Rnd.NextDouble(); x < len + a;)
                {
                    double w = a * (0.75 + 0.8 * Rnd.NextDouble());
                    double x0 = x + g / 2, y0 = y + g / 2, x1 = x + w - g / 2, y1 = y + hh - g / 2;
                    x += w;
                    if (x1 - x0 < 1 || y1 - y0 < 1) continue;
                    double r = Math.Min(x1 - x0, y1 - y0) * 0.22;
                    var block = Poly.Rounded(x0, y0, x1, y1, r);
                    var worn = Weather.Piece(block, Stuff.SoftStone, Etched, Lines);
                    Raised.AddRange(worn);
                    // One to three wind-worn lines along the block.
                    int count = 1 + Rnd.Next(3);
                    var found = new List<PathD>();
                    for (int k = 0; k < count; k++)
                    {
                        double ly = y0 + (y1 - y0) * (0.2 + 0.6 * Rnd.NextDouble());
                        double la = x0 + (x1 - x0) * 0.3 * Rnd.NextDouble(), lb = x1 - (x1 - x0) * 0.3 * Rnd.NextDouble();
                        double amp = Math.Min(0.35, (y1 - y0) * 0.06), wave = 4 + 6 * Rnd.NextDouble(), phase = Rnd.NextDouble() * 6.28;
                        var line = new PathD();
                        for (double t = la; t <= lb; t += 0.5) line.Add(new PointD(t, ly + amp * Math.Sin(t / wave * 6.28 + phase) + Noise.At(t / 3, ly) * 0.12));
                        found.Add(line);
                    }
                    Lines.AddRange(Poly.ClipLines(found, Poly.Offset(worn, -0.2)));
                    // Honeycomb weathering: a cluster of pits eaten into some blocks.
                    var wr = Weather.Random;
                    if (!Wear.LinesOnly && wr.NextDouble() < 0.35 * Wear.Amount * 2)
                    {
                        double cx = x0 + (x1 - x0) * (0.2 + 0.6 * wr.NextDouble()), cy = y0 + (y1 - y0) * (0.25 + 0.5 * wr.NextDouble());
                        var pits = new PathsD();
                        int n = 3 + wr.Next(6);
                        for (int k = 0; k < n; k++)
                        {
                            double pr = Math.Max(0.15, D * (0.25 + 0.35 * wr.NextDouble()));
                            pits.Add(Poly.Blob(cx + (wr.NextDouble() - 0.5) * D * 4, cy + (wr.NextDouble() - 0.5) * D * 2.5, pr * 1.2, pr, 0.7, wr));
                        }
                        Etched.AddRange(Poly.Intersect(pits, Poly.Offset(worn, -0.25)));
                    }
                }
                y += hh;
            }
        }

        /// <summary>
        /// Plaster over the whole face, with ragged patches fallen away where worn bricks show (brick height
        /// <paramref name="b"/>, two and a half times as long), and cracks spreading through the plaster.
        /// </summary>
        public void Plaster(double len, double h, double a, double b, double g)
        {
            var face = Poly.Rect(-1, -1, len + 1, h + 1);
            double k = Wear.Amount * 2;
            // Every patch that could fall is placed (so the layout never depends on the wear); the wear says how many have.
            int most = Math.Max(1, (int)Math.Round(len * h / (a * a * 5) * 1.6));
            int count = Math.Max(1, (int)Math.Round(len * h / (a * a * 5) * (0.4 + 0.6 * k)));
            var patches = new PathsD();
            for (int i = 0; i < most; i++)
            {
                double cx = len * Rnd.NextDouble(), cy = h * (0.05 + 0.9 * Rnd.NextDouble()), r = a / 2 * (0.5 + 0.7 * Rnd.NextDouble());
                var blob = Poly.Blob(cx, cy, r * 1.3, r * 0.8, 1, Rnd, (Rnd.NextDouble() - 0.5) * 0.6);
                if (i < count) patches.Add(Poly.Roughen(blob, Noise, Math.Min(r * 0.3, D * 1.2), D * 1.5, Math.Max(0.2, D * 0.35)));
            }
            var holes = Poly.Union(patches);
            var plaster = Poly.Subtract([face], holes);
            Raised.AddRange(plaster);
            Weather.Surface(plaster, Stuff.Plaster, Etched, Lines);
            // The plaster's broken edge: a lip line a little in from it.
            Lines.AddRange(Poly.ClipLines(Poly.Offset(holes, Math.Max(0.35, D * 0.6)).Select(Poly.Loop), plaster));
            // Bricks in the holes, worn.
            var inHoles = Poly.Offset(holes, -g);
            double bl = b * 2.5;
            int rows = (int)Math.Ceiling(h / b) + 2;
            var hb = Poly.Bounds(holes);
            for (int row = -1; row < rows; row++)
            {
                double y = row * b, off = row % 2 != 0 ? bl / 2 : 0;
                if (y + b < hb.top || y > hb.bottom) continue;
                for (double x = -off - bl; x < len + bl; x += bl)
                {
                    if (x + bl < hb.left || x > hb.right) continue;
                    foreach (var p in Poly.Intersect([Box(x, y, x + bl, y + b, g)], inHoles))
                        if (Clipper.Area(p) > 0.3) Piece(p, Stuff.Brick);
                }
            }
            // Cracks spreading through the plaster, forking.
            var wr = Weather.Random;
            int cracks = Math.Max(1, (int)Math.Round(len * h / (a * a * 4) * k * 0.7));
            var found = new List<PathD>();
            for (int i = 0; i < cracks; i++)
            {
                double cx = len * wr.NextDouble(), cy = h * wr.NextDouble(), l = a * (0.4 + 0.6 * wr.NextDouble()), dir = wr.NextDouble() * Math.PI * 2;
                var main = Crack(cx, cy, dir, l, wr);
                found.Add(main);
                if (main.Count > 3 && wr.NextDouble() < 0.6) found.Add(Crack(main[main.Count / 2].x, main[main.Count / 2].y, dir + (wr.NextDouble() < 0.5 ? 0.8 : -0.8), l * 0.5, wr));
            }
            // Cracks stop at the patches' edges.
            Lines.AddRange(Poly.ClipLines(found, plaster));
        }

        /// <summary>A crack wandering from a point in about the given direction.</summary>
        public PathD Crack(double x, double y, double dir, double length, Random rnd)
        {
            var p = new PathD { new(x, y) };
            double step = Math.Max(0.3, D * 0.8);
            for (double run = 0; run < length; run += step)
            {
                dir += (rnd.NextDouble() - 0.5) * 0.9;
                x += Math.Cos(dir) * step;
                y += Math.Sin(dir) * step;
                p.Add(new PointD(x, y));
            }
            return p;
        }
    }
}
