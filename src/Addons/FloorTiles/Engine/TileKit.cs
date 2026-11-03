using Clipper2Lib;
using PhotonCanvas.Addon.Walls;

namespace PhotonCanvas.Addon.FloorTiles;

/// <summary>Where a tile goes on the board (mm, y up, its corner at <paramref name="X"/>, <paramref name="Y"/>) and its size without tabs.</summary>
public sealed record BoardSpot(Part Tile, double X, double Y, double W, double H, Sides Plain);

/// <summary>Everything a set of floor tiles is cut from, with what was found while making it.</summary>
public sealed record TileKit(List<Part> Parts, List<Finding> Findings, List<BoardSpot> Board, int Clips, int Magnets)
{
    public bool Ok => !Findings.Any(f => f.Level == Severity.Bad);
    public int PieceCount => Parts.Sum(p => p.Copies);
    /// <summary>The tiles (not their base layers or the clips).</summary>
    public IEnumerable<Part> Tiles => Parts.Where(p => !p.Name.EndsWith(TileKitMaker.BaseSuffix, StringComparison.Ordinal) && p.Name != TileKitMaker.ClipName);
}

/// <summary>
/// Makes floor tiles on a square grid: each tile's outline with the joints on its edges (see
/// <see cref="Joints"/>), its texture and grid marks, a plain base layer under it if asked for (or
/// needed for magnets), and the clips. A board is split into tiles of the size asked, with straight
/// edges round the outside; loose tiles come in the sizes and numbers listed. Every tile gets its own
/// layout of stones or boards.
/// </summary>
public static class TileKitMaker
{
    public const int MaxTiles = 400;
    public const string BaseSuffix = " · base";
    public const string ClipName = "Clip";

    public static Sides PlainFor(PlainSides p) => p switch
    {
        PlainSides.One => Sides.Bottom,
        PlainSides.Corner => Sides.Bottom | Sides.Left,
        PlainSides.Opposite => Sides.Bottom | Sides.Top,
        PlainSides.Three => Sides.Bottom | Sides.Left | Sides.Top,
        PlainSides.All => Sides.All,
        _ => Sides.None,
    };

    static int Count(Sides s) => ((int)s & 1) + ((int)s >> 1 & 1) + ((int)s >> 2 & 1) + ((int)s >> 3 & 1);

    /// <summary>A column's letters: A … Z, AA, AB …</summary>
    public static string Column(int i) => i < 26 ? ((char)('A' + i)).ToString() : Column(i / 26 - 1) + (char)('A' + i % 26);

    static string Squares(double v) => v.ToString("0.##", System.Globalization.CultureInfo.CurrentCulture);

    sealed record Job(string Name, double W, double H, Sides Plain, int Seed, double X, double Y, bool OnBoard);

    public static TileKit Make(TileSpec s)
    {
        var findings = new List<Finding>();
        var parts = new List<Part>();
        var board = new List<BoardSpot>();
        double g = s.SquareMm;
        if (!(g >= 5) || g > 300) { findings.Add(new(Severity.Bad, "Make the grid square between 5 and 300 mm.")); return new(parts, findings, board, 0, 0); }
        if (!(s.ThicknessMm > 0)) { findings.Add(new(Severity.Bad, "Give the sheet's thickness.")); return new(parts, findings, board, 0, 0); }

        var jobs = Jobs(s, findings);
        if (jobs.Count == 0) { if (findings.Count == 0) findings.Add(new(Severity.Bad, "There are no tiles to make: add some.")); return new(parts, findings, board, 0, 0); }
        if (jobs.Count > MaxTiles) { findings.Add(new(Severity.Bad, $"That's {jobs.Count} tiles: make at most {MaxTiles} at a time (bigger tiles, or a smaller board).")); return new(parts, findings, board, 0, 0); }
        if (!CheckJoints(s, findings)) return new(parts, findings, board, 0, 0);

        int clipSockets = 0, magnets = 0;
        foreach (var job in jobs)
        {
            var t = Tile(s, job.W, job.H, job.Plain, job.Seed);
            var part = new Part(job.Name, t.Cut, t.Etched, t.Lines, 1);
            parts.Add(part);
            if (job.OnBoard) board.Add(new BoardSpot(part, job.X, job.Y, job.W, job.H, job.Plain));
            if (t.Base is { } b) parts.Add(new Part(job.Name + BaseSuffix, b, [], t.BaseLines, 1));
            clipSockets += t.ClipSockets;
            magnets += t.Magnets;
        }
        int clips = (clipSockets + 1) / 2;
        if (clips > 0) parts.Add(new Part(ClipName, [Joints.Clip(s.TabMm, s.FitMm)], [], [], clips));

        Notes(s, jobs, clips, magnets, findings);
        return new(parts, findings, board, clips, magnets);
    }

    /// <summary>The tiles to make: a board's, cut up in columns and rows, or the loose ones listed.</summary>
    static List<Job> Jobs(TileSpec s, List<Finding> findings)
    {
        double g = s.SquareMm;
        var jobs = new List<Job>();
        int seed = s.Seed * 7919;
        if (s.Mode == TileMode.Board)
        {
            if (!(s.BoardAcross >= 0.5) || !(s.BoardDown >= 0.5)) { findings.Add(new(Severity.Bad, "Make the board at least half a square each way.")); return jobs; }
            if (s.BoardAcross * s.BoardDown > 40000) { findings.Add(new(Severity.Bad, "That board is too big.")); return jobs; }
            var cols = Split(s.BoardAcross, Math.Max(1, s.TileAcross));
            var rows = Split(s.BoardDown, Math.Max(1, s.TileDown));
            if (cols.Count * rows.Count > MaxTiles) { findings.Add(new(Severity.Bad, $"That's {cols.Count * rows.Count} tiles: make at most {MaxTiles} at a time (bigger tiles, or a smaller board).")); return jobs; }
            double x = 0;
            for (int i = 0; i < cols.Count; i++)
            {
                double y = 0;
                for (int j = 0; j < rows.Count; j++)
                {
                    var plain = Sides.None;
                    if (s.PlainBorder)
                    {
                        if (i == 0) plain |= Sides.Left;
                        if (i == cols.Count - 1) plain |= Sides.Right;
                        if (j == 0) plain |= Sides.Bottom;
                        if (j == rows.Count - 1) plain |= Sides.Top;
                    }
                    // Rows are numbered from the top, as the board lies on the screen.
                    jobs.Add(new Job($"Tile {Column(i)}{rows.Count - j}", cols[i] * g, rows[j] * g, plain, seed + jobs.Count * 104729, x, y, true));
                    y += rows[j] * g;
                }
                x += cols[i] * g;
            }
            if (cols[^1] < 0.5 || rows[^1] < 0.5)
                findings.Add(new(Severity.Warn, $"The last {(cols[^1] < 0.5 ? "column" : "row")} is a strip only {Math.Min(cols[^1], rows[^1]) * g:0.#} mm wide: easily broken. Make the board a little bigger or smaller."));
            if (Frac(s.BoardAcross) || Frac(s.BoardDown))
                findings.Add(new(Severity.Note, "The board isn't a whole number of squares: the far column or row is narrower, and where only part of a square meets the next tile there's no joint."));
        }
        else
        {
            foreach (var t in s.Tiles)
            {
                if (t.Count <= 0) continue;
                if (!(t.Across >= 0.5) || !(t.Down >= 0.5) || t.Across > 100 || t.Down > 100) { findings.Add(new(Severity.Bad, "Make each tile between half a square and 100 squares each way.")); return []; }
                var plain = PlainFor(t.Plain);
                string kind = t.Plain switch
                {
                    PlainSides.One => " edge",
                    PlainSides.Corner => " corner",
                    PlainSides.Opposite => " corridor",
                    PlainSides.Three => " end",
                    PlainSides.All => " plain",
                    _ => "",
                };
                for (int k = 0; k < t.Count; k++)
                {
                    string name = $"{Squares(t.Across)} × {Squares(t.Down)}{kind} tile" + (t.Count > 1 ? $" ({k + 1} of {t.Count})" : "");
                    jobs.Add(new Job(name, t.Across * g, t.Down * g, plain, seed + jobs.Count * 104729, 0, 0, false));
                    if (jobs.Count > MaxTiles) return jobs;
                }
            }
            if (s.Tiles.Any(t => t.Count > 0 && (Frac(t.Across) || Frac(t.Down))))
                findings.Add(new(Severity.Note, "Where a tile is only part of a square, that part has no joint: the tile holds on by its whole squares."));
        }
        return jobs;
    }

    static bool Frac(double v) => Math.Abs(v - Math.Round(v)) > 1e-6;

    /// <summary>A length in squares split into pieces of <paramref name="step"/> squares, the last one shorter if need be.</summary>
    static List<double> Split(double total, int step)
    {
        var res = new List<double>();
        double left = total;
        while (left > step + 1e-6) { res.Add(step); left -= step; }
        res.Add(Math.Round(left, 6));
        return res;
    }

    /// <summary>Do the tabs, clips or magnets fit the squares? Says why not if they don't.</summary>
    static bool CheckJoints(TileSpec s, List<Finding> findings)
    {
        double g = s.SquareMm, d = s.TabMm, fit = Math.Max(0, s.FitMm);
        switch (s.Join)
        {
            case TileJoin.Jigsaw:
            {
                // Next to each corner a socket comes a quarter of a square in: what's left between it and the corner holds the tab beside it.
                double bridge = g / 4 - d / 2 - fit / 4, most = Math.Floor((g / 2 - 2.4) * 10) / 10;
                if (!(d >= 1.5)) { findings.Add(new(Severity.Bad, "Make the tabs at least 1.5 mm.")); return false; }
                if (bridge < 1.2 || Joints.TabReach(d) > g * 0.4) { findings.Add(new(Severity.Bad, $"The tabs are too big for {g:0.#} mm squares: make the tab size {most:0.#} mm or less.")); return false; }
                if (bridge < 2) findings.Add(new(Severity.Warn, $"Only {bridge:0.#} mm is left between the sockets and the tiles' corners: it may snap in thin plywood. Smaller tabs are stronger there."));
                if (d * 0.55 < 2.5) findings.Add(new(Severity.Warn, $"The tabs' necks are {d * 0.55:0.#} mm wide: they may snap. Bigger tabs are stronger."));
                if (s.BaseLayer) findings.Add(new(Severity.Note, "The jigsaw is cut in the base layer only: the tops are plain squares that sit edge to edge over it and hide the joints."));
                break;
            }
            case TileJoin.Clips:
            {
                var (neck, wide, reach) = Joints.ClipSize(d);
                if (!(d >= 2)) { findings.Add(new(Severity.Bad, "Make the clips at least 2 mm.")); return false; }
                if (wide / 2 + 1.5 > g / 2 || reach > g * 0.4) { findings.Add(new(Severity.Bad, $"The clips are too big for {g:0.#} mm squares: make the clip size {Math.Floor(Math.Min((g / 2 - 1.5) / 0.65, g * 0.4) * 10) / 10:0.#} mm or less.")); return false; }
                if (neck - fit / 2 < 2) findings.Add(new(Severity.Warn, $"The clips are only {neck - fit / 2:0.#} mm wide in the middle: they may snap. Make them bigger."));
                if (!s.BaseLayer) findings.Add(new(Severity.Note, "The clips show on top of the floor. Tick Base layer to hide them underneath: the sockets are then only in the base layer."));
                break;
            }
            case TileJoin.Magnets:
                if (!(s.MagnetMm >= 1)) { findings.Add(new(Severity.Bad, "Give the magnets' size.")); return false; }
                if (Joints.MagnetPlaces(g, s.MagnetMm, fit) is null) { findings.Add(new(Severity.Bad, $"{s.MagnetMm:0.#} mm magnets are too big for {g:0.#} mm squares: two go on every square of every edge. Use smaller magnets.")); return false; }
                break;
        }
        return true;
    }

    sealed record TileResult(PathsD Cut, PathsD Etched, List<PathD> Lines, PathsD? Base, List<PathD> BaseLines, int ClipSockets, int Magnets);

    /// <summary>One tile, <paramref name="w"/> × <paramref name="h"/> mm, with its joints on every side not in <paramref name="plain"/>.</summary>
    static TileResult Tile(TileSpec s, double w, double h, Sides plain, int seed)
    {
        double g = s.SquareMm, fit = Math.Max(0, s.FitMm);
        var rect = new PathsD { Geo.Rect(0, 0, w, h) };
        var tabs = new PathsD();
        var sockets = new PathsD();
        var magnetHoles = new PathsD();
        var rings = new List<PathD>();
        int magnets = 0;
        foreach (var e in Joints.Edges(w, h))
        {
            if ((plain & e.Side) != 0) continue;
            switch (s.Join)
            {
                case TileJoin.Jigsaw:
                    foreach (var t in Joints.Along(e, g, 0.25)) tabs.AddRange(Joints.Tab(e, t, s.TabMm, fit));
                    foreach (var t in Joints.Along(e, g, 0.75)) sockets.AddRange(Joints.Socket(e, t, s.TabMm, fit));
                    break;
                case TileJoin.Clips:
                    foreach (var t in Joints.Along(e, g, 0.5)) sockets.Add(Joints.ClipSocket(e, t, s.TabMm, fit));
                    break;
                case TileJoin.Magnets when Joints.MagnetPlaces(g, s.MagnetMm, fit) is { } places:
                {
                    var (share, inset) = places;
                    double r = s.MagnetMm / 2 + fit / 4;
                    // North up in the ringed holes (the first along each square, going anticlockwise), south up in the others.
                    foreach (var t in Joints.Along(e, g, share))
                    {
                        var c = e.At(t, -inset);
                        magnetHoles.Add(Geo.Circle(c.x, c.y, r));
                        rings.Add(Geo.Loop(Geo.Circle(c.x, c.y, r + 0.8)));
                        magnets++;
                    }
                    foreach (var t in Joints.Along(e, g, 1 - share))
                    {
                        var c = e.At(t, -inset);
                        magnetHoles.Add(Geo.Circle(c.x, c.y, r));
                        magnets++;
                    }
                    break;
                }
            }
        }

        PathsD top, baseCut;
        int clipSockets = s.Join == TileJoin.Clips ? sockets.Count : 0;
        switch (s.Join)
        {
            case TileJoin.Jigsaw:
                // With a base layer the jigsaw is in the base only: the tops are plain and sit edge to edge over it, hiding the joints.
                var jigsaw = Geo.Tidy(Geo.Subtract(Geo.Union(rect, tabs), Geo.Union(sockets)));
                top = s.BaseLayer ? rect : jigsaw;
                baseCut = jigsaw;
                break;
            case TileJoin.Clips:
                var notched = Geo.Tidy(Geo.Subtract(rect, sockets));
                top = s.BaseLayer ? rect : notched;
                baseCut = notched;
                break;
            case TileJoin.Magnets:
                top = rect;
                baseCut = Geo.Tidy(Geo.Subtract(rect, magnetHoles));
                break;
            default:
                top = rect;
                baseCut = rect;
                break;
        }

        // The texture, over the whole tile and its tabs.
        var tex = FloorTextures.Make(s, w, h, seed);
        var etched = new PathsD();
        var lines = new List<PathD>();
        if (s.Etch == EtchStyle.Grooves)
        {
            var mortar = tex.Raised.Count > 0 ? Geo.Subtract([Geo.Rect(-g - 1, -g - 1, w + g + 1, h + g + 1)], Geo.Union(tex.Raised)) : [];
            etched.AddRange(Geo.Intersect(Geo.Union(mortar, tex.Etched), top));
        }
        else
        {
            lines.AddRange(Geo.ClipLines(tex.Raised.Select(Geo.Loop), top));
            lines.AddRange(Geo.ClipLines(tex.Etched.Select(Geo.Loop), top));
        }
        lines.AddRange(Geo.ClipLines(tex.Lines, top));

        // The grid squares inside the tile (its edges are the cut).
        if (s.Grid != GridMarks.None)
        {
            var grid = new List<PathD>();
            for (double x = g; x < w - 0.01; x += g) grid.Add([new(x, -g), new(x, h + g)]);
            for (double y = g; y < h - 0.01; y += g) grid.Add([new(-g, y), new(w + g, y)]);
            if (s.Grid == GridMarks.Scored) lines.AddRange(Geo.ClipLines(grid, top));
            else
            {
                double gw = Math.Clamp(s.GrooveMm, 0.3, 2);
                var strips = new PathsD(grid.Select(l => Textures.Strip(l[0], l[1], gw)));
                etched = Geo.Union(etched, Geo.Intersect(Geo.Union(strips), top));
            }
        }
        return new(top, Geo.Tidy(etched), lines, s.HasBase ? baseCut : null, rings, clipSockets, magnets);
    }

    static void Notes(TileSpec s, List<Job> jobs, int clips, int magnets, List<Finding> findings)
    {
        double g = s.SquareMm;
        if (s.Mode == TileMode.Board)
        {
            int corners = jobs.Count(j => Count(j.Plain) == 2 && (j.Plain is (Sides.Bottom | Sides.Left) or (Sides.Bottom | Sides.Right) or (Sides.Top | Sides.Left) or (Sides.Top | Sides.Right)));
            int edges = jobs.Count(j => Count(j.Plain) == 1);
            int middle = jobs.Count(j => j.Plain == Sides.None);
            var kinds = new List<string>();
            if (corners > 0) kinds.Add($"{corners} corner{(corners == 1 ? "" : "s")}");
            if (edges > 0) kinds.Add($"{edges} edge{(edges == 1 ? "" : "s")}");
            if (middle > 0) kinds.Add($"{middle} middle");
            int other = jobs.Count - corners - edges - middle;
            if (other > 0) kinds.Add($"{other} other");
            string split = s.PlainBorder && kinds.Count > 1 ? $": {string.Join(", ", kinds)}" : "";
            findings.Add(new(Severity.Note, $"A board of {Squares(s.BoardAcross)} × {Squares(s.BoardDown)} squares ({s.BoardAcross * g:0.#} × {s.BoardDown * g:0.#} mm) in {jobs.Count} tile{(jobs.Count == 1 ? "" : "s")}{split}."));
            if (!s.PlainBorder && s.Join != TileJoin.None)
                findings.Add(new(Severity.Note, Frac(s.BoardAcross) || Frac(s.BoardDown)
                    ? "The outside edges have joints too, so the board fits against others. The part squares at its far edge have none: line boards up by their whole squares."
                    : "The outside edges have joints too, so the board fits against any other made on the same grid, turned any way round."));
        }
        if (clips > 0) findings.Add(new(Severity.Note, $"{clips} clip{(clips == 1 ? "" : "s")} to join them: push one into each pair of sockets that meet{(s.BaseLayer ? ", from underneath" : "")}."));
        if (s.Join == TileJoin.Magnets && magnets > 0)
            findings.Add(new(Severity.Note, $"{magnets} magnets, {s.MagnetMm:0.#} mm across and no thicker than {s.ThicknessMm:0.#} mm: glue them into the base layer before it goes under the tile, north side up in the holes with a ring round them and south side up in the others."));
        if (s.HasBase) findings.Add(new(Severity.Note, $"Each tile has a base layer glued under it: {2 * s.ThicknessMm:0.#} mm thick together."));
    }
}
