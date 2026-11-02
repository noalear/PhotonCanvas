using Clipper2Lib;
using PhotonCanvas.Addon.Buildings;
using PhotonCanvas.Addon.FloorTiles;
using PhotonCanvas.Addon.Walls;
using Geo = PhotonCanvas.Addon.Walls.Geo;
using Pose = PhotonCanvas.Addon.Furniture.Pose;
using Severity = PhotonCanvas.Addon.Buildings.Severity;
using Finding = PhotonCanvas.Addon.Buildings.Finding;

namespace PhotonCanvas.Addon.MapTerrain;

/// <summary>
/// The floor under a plan, cut into tiles the way the floor tile maker cuts them: blocks of up to
/// so many squares each way (a block whose floor squares don't touch makes a tile for each part), with
/// the floor tile maker's joints on every square of every edge where two tiles meet (its jigsaw tab and
/// socket, so any tile fits its neighbour, or its clip sockets), plain where the floor ends. Round the
/// outside, under the walls, the tiles reach out far enough for the walls and columns to stand on them.
/// Each tile gets its own layout of the texture, kept off that rim.
/// </summary>
public static class MapTiles
{
    /// <summary>A square's outline in the plan's flat coordinates (mm, y up, the plan's bottom left corner at 0, 0).</summary>
    static PathD Cell(int x, int y, int down, double g) => Geo.Rect(x * g, (down - y - 1) * g, (x + 1) * g, (down - y) * g);

    /// <summary>A grid corner in the flat coordinates.</summary>
    static PointD Vertex(int x, int y, int down, double g) => new(x * g, (down - y) * g);

    /// <summary>How far the tiles reach out past the floor under the walls (mm).</summary>
    public static double Rim(MapSpec s) => Math.Max(s.ColumnMm, MapKitMaker.WallThickness(s)) / 2 + 0.5;

    public static void Make(MapSpec s, PlanGrid grid, MapLayout? layout, List<Piece> pieces, List<Solid> solids, List<Finding> findings, CancellationToken ct)
    {
        double g = s.SquareMm, T = s.SheetMm, fit = Math.Max(0, s.FitMm);
        int W = grid.Across, D = grid.Down, size = Math.Clamp(s.TileSquares, 1, 20);

        // The tiles: each block's floor squares, split into the parts that touch.
        var tileOf = new int[W, D];
        for (int x = 0; x < W; x++) for (int y = 0; y < D; y++) tileOf[x, y] = -1;
        var tiles = new List<List<(int X, int Y)>>();
        for (int by = 0; by < D; by += size)
            for (int bx = 0; bx < W; bx += size)
                for (int y = by; y < Math.Min(D, by + size); y++)
                    for (int x = bx; x < Math.Min(W, bx + size); x++)
                    {
                        if (!grid[x, y] || tileOf[x, y] >= 0) continue;
                        var cells = new List<(int, int)>();
                        var todo = new Stack<(int X, int Y)>();
                        todo.Push((x, y));
                        tileOf[x, y] = tiles.Count;
                        while (todo.Count > 0)
                        {
                            var c = todo.Pop();
                            cells.Add(c);
                            foreach (var (nx, ny) in new[] { (c.X + 1, c.Y), (c.X - 1, c.Y), (c.X, c.Y + 1), (c.X, c.Y - 1) })
                                if (nx >= bx && ny >= by && nx < bx + size && ny < by + size && grid[nx, ny] && tileOf[nx, ny] < 0)
                                {
                                    tileOf[nx, ny] = tiles.Count;
                                    todo.Push((nx, ny));
                                }
                        }
                        tiles.Add(cells);
                    }
        if (tiles.Count > MapKitMaker.MaxTiles) { findings.Add(new(Severity.Bad, $"That's {tiles.Count} floor tiles: make at most {MapKitMaker.MaxTiles} at a time (bigger tiles, or a smaller map).")); return; }

        if (s.Join == MapJoin.Jigsaw)
        {
            double d = s.TabMm, bridge = g / 4 - d / 2 - fit / 4, most = Math.Floor((g / 2 - 2.4) * 10) / 10;
            if (!(d >= 1.5)) { findings.Add(new(Severity.Bad, "Make the tabs at least 1.5 mm.")); return; }
            if (bridge < 1.2 || Joints.TabReach(d) > g * 0.4) { findings.Add(new(Severity.Bad, $"The tabs are too big for {g:0.#} mm squares: make the tab size {most:0.#} mm or less.")); return; }
        }
        else if (s.Join == MapJoin.Clips)
        {
            var (_, wide, reach) = Joints.ClipSize(s.TabMm);
            if (!(s.TabMm >= 2)) { findings.Add(new(Severity.Bad, "Make the clips at least 2 mm.")); return; }
            if (wide / 2 + 1.5 > g / 2 || reach > g * 0.4) { findings.Add(new(Severity.Bad, $"The clips are too big for {g:0.#} mm squares: make the clip size smaller.")); return; }
        }

        // The rim under the walls: a strip on the outside of every wall at the floor's edge, and a square round every column.
        var rims = new List<(PathsD Area, int Tile)>();
        var loose = new PathsD();
        var floorArea = new PathsD();
        for (int x = 0; x < W; x++) for (int y = 0; y < D; y++) if (grid[x, y]) floorArea.Add(Cell(x, y, D, g));
        floorArea = Geo.Union(floorArea);
        double r = Rim(s);
        if (layout is not null && s.UnderWalls && s.Walls)
        {
            foreach (var p in layout.Panels)
                for (int i = 0; i < p.Squares; i++)
                {
                    var e = p.Edge(i);
                    var (a, b) = e.Sides;
                    bool fa = grid[a.X, a.Y], fb = grid[b.X, b.Y];
                    if (fa == fb) continue;
                    var (from, to) = e.Ends;
                    var p0 = Vertex(from.X, from.Y, D, g);
                    var p1 = Vertex(to.X, to.Y, D, g);
                    // Out from the floor: away from the floor square.
                    PathD strip = e.Across
                        ? (fa ? Geo.Rect(p0.x, p0.y - r, p1.x, p0.y) : Geo.Rect(p0.x, p0.y, p1.x, p0.y + r))
                        : (fa ? Geo.Rect(p0.x, p1.y, p0.x + r, p0.y) : Geo.Rect(p0.x - r, p1.y, p0.x, p0.y));
                    var owner = fa ? a : b;
                    rims.Add(([strip], tileOf[owner.X, owner.Y]));
                }
            foreach (var c in layout.Columns)
            {
                var v = Vertex(c.X, c.Y, D, g);
                loose.Add(Geo.Rect(v.x - r, v.y - r, v.x + r, v.y + r));
            }
        }
        // Each strip goes to the tile it runs along; what's left round the columns to the tile it touches most.
        var taken = new PathsD();
        var rimOf = Enumerable.Range(0, tiles.Count).Select(_ => new PathsD()).ToList();
        foreach (var (area, t) in rims)
        {
            var part = Geo.Subtract(Geo.Subtract(area, floorArea), taken);
            if (Geo.Area(part) < 1e-3) continue;
            rimOf[t].AddRange(part);
            taken = Geo.Union(taken, part);
        }
        var tileArea = tiles.Select(cells => Geo.Union(new PathsD(cells.Select(c => Cell(c.X, c.Y, D, g))))).ToList();
        foreach (var piece in Geo.Tidy(Geo.Subtract(Geo.Subtract(Geo.Union(loose), floorArea), taken)).Where(p => Clipper.Area(p) > 1e-3))
        {
            var grown = Geo.Offset([piece], 0.3);
            int best = -1;
            double most = 1e-4;
            for (int t = 0; t < tiles.Count; t++)
            {
                double touch = Geo.Area(Geo.Intersect(grown, Geo.Union(tileArea[t], rimOf[t])));
                if (touch > most) { most = touch; best = t; }
            }
            if (best >= 0) { rimOf[best].Add(piece); taken = Geo.Union(taken, [piece]); }
        }

        // Every tile: its squares and rim, the joints where it meets another tile, its texture.
        var preset = FloorTextures.Preset(s.FloorTexture) ?? FloorTextures.Preset("Dungeon flagstones")!;
        var (sa, sb) = FloorTextures.SizesFor(preset, g);
        var texSpec = new TileSpec
        {
            SquareMm = g, ThicknessMm = T, Texture = preset.Name, SizeA = sa, SizeB = sb, GrooveMm = preset.Groove,
            Seed = s.Seed, Etch = s.Etch, Join = TileJoin.None,
        };
        int shared = 0, rimmed = 0;
        for (int t = 0; t < tiles.Count; t++)
        {
            ct.ThrowIfCancellationRequested();
            var cells = tiles[t];
            var tabs = new PathsD();
            var sockets = new PathsD();
            foreach (var (x, y) in cells)
            {
                var c0 = Vertex(x, y + 1, D, g);   // the square's bottom left corner (y up)
                // Going round the square anticlockwise (y up): bottom, right, top, left; the square beyond each.
                var sides = new (Sides Side, PointD Start, PointD Along, PointD Out, int Nx, int Ny)[]
                {
                    (Sides.Bottom, c0, new(1, 0), new(0, -1), x, y + 1),
                    (Sides.Right, new(c0.x + g, c0.y), new(0, 1), new(1, 0), x + 1, y),
                    (Sides.Top, new(c0.x + g, c0.y + g), new(-1, 0), new(0, 1), x, y - 1),
                    (Sides.Left, new(c0.x, c0.y + g), new(0, -1), new(-1, 0), x - 1, y),
                };
                foreach (var sd in sides)
                {
                    if (!grid[sd.Nx, sd.Ny] || tileOf[sd.Nx, sd.Ny] == t) continue;
                    var e = new Joints.Edge(sd.Side, sd.Start, sd.Along, sd.Out, g);
                    switch (s.Join)
                    {
                        case MapJoin.Jigsaw:
                            tabs.AddRange(Joints.Tab(e, g / 4, s.TabMm, fit));
                            sockets.AddRange(Joints.Socket(e, 3 * g / 4, s.TabMm, fit));
                            break;
                        case MapJoin.Clips:
                            sockets.Add(Joints.ClipSocket(e, g / 2, s.TabMm, fit));
                            break;
                    }
                    shared++;
                }
            }
            var squares = tileArea[t];
            var rim = Geo.Union(rimOf[t]);
            if (rim.Count > 0) rimmed++;
            var top = Geo.Tidy(Geo.Subtract(Geo.Union(Geo.Union(squares, rim), tabs), Geo.Union(sockets)));
            // The texture, laid out from the tile's corner on the grid, over its squares and tabs but not the rim.
            int cx0 = cells.Min(c => c.X), cx1 = cells.Max(c => c.X), cy0 = cells.Min(c => c.Y), cy1 = cells.Max(c => c.Y);
            double ox = cx0 * g, oy = (D - cy1 - 1) * g, w = (cx1 - cx0 + 1) * g, h = (cy1 - cy0 + 1) * g;
            var local = Geo.Move(top, -ox, -oy);
            var faceArea = Geo.Move(Geo.Subtract(top, rim), -ox, -oy);
            var tex = FloorTextures.Make(texSpec, w, h, s.Seed * 7919 + t * 104729);
            var etched = new PathsD();
            var lines = new List<PathD>();
            if (s.Etch == EtchStyle.Grooves)
            {
                var mortar = tex.Raised.Count > 0 ? Geo.Subtract([Geo.Rect(-g - 1, -g - 1, w + g + 1, h + g + 1)], Geo.Union(tex.Raised)) : [];
                etched.AddRange(Geo.Intersect(Geo.Union(mortar, tex.Etched), faceArea));
            }
            else
            {
                lines.AddRange(Geo.ClipLines(tex.Raised.Select(Geo.Loop), faceArea));
                lines.AddRange(Geo.ClipLines(tex.Etched.Select(Geo.Loop), faceArea));
            }
            lines.AddRange(Geo.ClipLines(tex.Lines, faceArea));
            var first = cells.OrderBy(c => c.Y).ThenBy(c => c.X).First();
            string name = $"Floor tile {MapPlan.Square(first.X, first.Y)}";
            var etchedT = Geo.Tidy(etched);
            pieces.Add(new Piece(name, Sheet.Walls, local, etchedT, lines, 1));
            // Lying where it goes: local (u, v) is (X − ox, (D·g − Z) − oy).
            solids.Add(new Solid("Floor tile", local, Pose.Up(ox, T / 2, D * g - oy), T, MapKitMaker.Nil(etchedT), MapKitMaker.Nil(lines), 0, WallSide.Ground, false));
        }
        int clips = s.Join == MapJoin.Clips ? shared / 2 : 0;
        if (clips > 0) pieces.Add(new Piece("Floor tile clip", Sheet.Walls, [Joints.Clip(s.TabMm, fit)], [], [], clips));

        string join = s.Join switch
        {
            MapJoin.Jigsaw => " that lock together with jigsaw edges",
            MapJoin.Clips => $" joined with {clips} clip{(clips == 1 ? "" : "s")}",
            _ => " with plain edges",
        };
        findings.Add(new(Severity.Note, $"{tiles.Count} floor tile{(tiles.Count == 1 ? "" : "s")} of up to {size} × {size} squares{join}{(rimmed > 0 ? "; round the outside they reach under the walls" : "")}."));
    }

}
