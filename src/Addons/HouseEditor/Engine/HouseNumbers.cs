using Clipper2Lib;
using PhotonCanvas.Addon.Buildings;
using Geo = PhotonCanvas.Addon.Walls.Geo;

namespace PhotonCanvas.Addon.HouseEditor;

/// <summary>
/// Every piece of a building gets a number, in the order it's put together (the floor tiles and base first,
/// the roofs last), the same number on every copy of it and on every piece that's just like it. The number
/// is scored small on the piece, as single lines, where it's clear of the texture (low on a wall, so it's
/// near the ground once the building stands); on a piece etched all over, a little patch is left plain for
/// it. The building booklet shows the same numbers.
/// </summary>
public static class HouseNumbers
{
    /// <summary>
    /// The kit with its pieces numbered in the order they're put together (<see cref="HouseSteps"/>), and each
    /// piece in the 3D model carrying its piece's number. Pieces that come out just alike share a number.
    /// </summary>
    public static HouseKit Numbered(HouseKit kit)
    {
        if (kit.Pieces.Count == 0) return kit;
        var key = kit.Pieces.Select(p => Key(p.Cut, p.Etched)).ToList();
        // Which pieces each solid is (by shape), and so the first step each piece is used in.
        var pieceOf = new Dictionary<string, int>();
        for (int i = 0; i < kit.Pieces.Count; i++) pieceOf.TryAdd(key[i], i);
        var solidPiece = kit.Solids.Select(s => pieceOf.TryGetValue(Key(s.Outline, s.Etched ?? []), out int i) ? i : -1).ToList();
        var stage = HouseSteps.Stages(kit.Solids);
        var first = Enumerable.Repeat(double.MaxValue, kit.Pieces.Count).ToArray();
        for (int j = 0; j < kit.Solids.Count; j++)
            if (solidPiece[j] >= 0) first[solidPiece[j]] = Math.Min(first[solidPiece[j]], stage[j]);
        // A piece with nothing of its own in the model (a clip, a peg) goes with the piece before it.
        for (int i = 0; i < first.Length; i++)
            if (first[i] == double.MaxValue) first[i] = i > 0 ? first[i - 1] : 0;

        // Numbered in that order; pieces alike share the first one's number.
        var number = new int[kit.Pieces.Count];
        var byKey = new Dictionary<string, int>();
        int next = 0;
        foreach (int i in Enumerable.Range(0, kit.Pieces.Count).OrderBy(i => first[i]).ThenBy(i => i))
        {
            if (!byKey.TryGetValue(key[i], out int n)) byKey[key[i]] = n = ++next;
            number[i] = n;
        }
        var pieces = kit.Pieces.Select((p, i) => p with { Number = number[i] }).ToList();
        var solids = kit.Solids.Select((s, j) => solidPiece[j] >= 0 ? s with { Number = number[solidPiece[j]] } : s).ToList();
        return kit with { Pieces = pieces, Solids = solids };
    }

    /// <summary>What a piece is, to tell it from others: its outline and etching, to a tenth of a millimetre.</summary>
    static string Key(PathsD cut, PathsD etched)
    {
        var tidy = Geo.Tidy(cut);
        var b = Geo.Bounds(tidy);
        double area = tidy.Sum(p => Clipper.Area(p)), etch = etched.Count == 0 ? 0 : Geo.Area(etched);
        int points = tidy.Sum(p => p.Count);
        return FormattableString.Invariant($"{b.right - b.left:0.0}|{b.bottom - b.top:0.0}|{area:0}|{points}|{etch:0}");
    }

    // ---- marking the pieces ----

    /// <summary>How tall the numbers are scored at 28 mm figures (mm), and the smallest they're made.</summary>
    public const double HeightMm = 3, SmallestMm = 1.8;

    /// <summary>
    /// The pieces with their numbers scored on them (<see cref="Piece.Marks"/>), <paramref name="heightMm"/> tall
    /// or smaller where a piece is small. A piece too small for even the smallest number is left unmarked.
    /// </summary>
    public static List<Piece> Marked(IEnumerable<Piece> pieces, double heightMm = HeightMm)
    {
        return [.. pieces.Select(p => p.Number > 0 ? Mark(p, heightMm) : p)];
    }

    /// <summary>One piece with its number scored on it (clear of its texture, or on a patch left plain for it).</summary>
    public static Piece Mark(Piece p, double heightMm = HeightMm)
    {
        string text = Text(p.Number);
        for (double h = heightMm; h >= SmallestMm - 1e-9; h -= 0.4)
        {
            double w = Width(text, h);
            if (Spot(p.Cut, p.Etched, p.Lines, w, Height(text, h), clearOfTexture: true) is { } at)
                return p with { Marks = Write(text, at.X, at.Y, h) };
        }
        // Etched all over: a patch left plain for it, as big as the biggest number that fits.
        for (double h = heightMm; h >= SmallestMm - 1e-9; h -= 0.4)
        {
            double w = Width(text, h), th = Height(text, h);
            if (Spot(p.Cut, [], [], w, th, clearOfTexture: false) is not { } at) continue;
            double m = Margin(h);
            PathsD patch = [Geo.Rect(at.X - w / 2 - m, at.Y - th / 2 - m, at.X + w / 2 + m, at.Y + th / 2 + m)];
            var b = Geo.Bounds(p.Cut);
            PathsD outside = Geo.Subtract([Geo.Rect(b.left - 1, b.top - 1, b.right + 1, b.bottom + 1)], patch);
            return p with { Etched = Geo.Subtract(p.Etched, patch), Lines = Geo.ClipLines(p.Lines, outside), Marks = Write(text, at.X, at.Y, h) };
        }
        return p;
    }

    /// <summary>Room kept round a number (mm).</summary>
    static double Margin(double h) => Math.Max(0.5, h * 0.2);

    /// <summary>
    /// Where a number <paramref name="w"/> × <paramref name="h"/> mm (its middle) can go on a piece, with room round it,
    /// inside its outline and (when asked) clear of its texture and lines: as low on the piece as it can, and then as near
    /// the middle across. Null when there's nowhere.
    /// </summary>
    public static (double X, double Y)? Spot(PathsD cut, PathsD etched, List<PathD> lines, double w, double h, bool clearOfTexture)
    {
        var b = Geo.Bounds(cut);
        double W = b.right - b.left, H = b.bottom - b.top, m = Margin(h) + 0.3;
        if (W < w + 2 * m || H < h + 2 * m) return null;
        double r = Math.Max(0.2, Math.Max(W, H) / 900);
        int nx = (int)Math.Ceiling(W / r) + 1, ny = (int)Math.Ceiling(H / r) + 1;
        var bad = new bool[nx * ny];
        // Outside the piece (or in a hole) is no good.
        var inside = Fill(cut, b.left, b.top, r, nx, ny);
        for (int i = 0; i < bad.Length; i++) bad[i] = !inside[i];
        if (clearOfTexture)
        {
            // Overlapping etched areas (grain over planks) count wherever any of them is.
            var etch = Fill(Geo.Union(etched), b.left, b.top, r, nx, ny);
            for (int i = 0; i < bad.Length; i++) bad[i] |= etch[i];
            foreach (var line in lines)
                for (int k = 1; k < line.Count; k++) Stamp(bad, line[k - 1], line[k], b.left, b.top, r, nx, ny);
        }
        // Counts of bad cells (summed areas), to test every window at once.
        var sum = new int[(nx + 1) * (ny + 1)];
        for (int y = 0; y < ny; y++)
        {
            int row = 0;
            for (int x = 0; x < nx; x++)
            {
                if (bad[y * nx + x]) row++;
                sum[(y + 1) * (nx + 1) + x + 1] = sum[y * (nx + 1) + x + 1] + row;
            }
        }
        int cw = (int)Math.Ceiling((w + 2 * m) / r) + 1, ch = (int)Math.Ceiling((h + 2 * m) / r) + 1;
        double mid = (b.left + b.right) / 2;
        for (int y = 0; y + ch <= ny; y++)
        {
            int best = -1;
            double bestD = double.MaxValue;
            for (int x = 0; x + cw <= nx; x++)
            {
                int n = sum[(y + ch) * (nx + 1) + x + cw] - sum[y * (nx + 1) + x + cw] - sum[(y + ch) * (nx + 1) + x] + sum[y * (nx + 1) + x];
                if (n != 0) continue;
                double d = Math.Abs(b.left + (x + cw / 2.0) * r - mid);
                if (d < bestD) { bestD = d; best = x; }
            }
            if (best >= 0) return (b.left + (best + cw / 2.0) * r, b.top + (y + ch / 2.0) * r);
        }
        return null;
    }

    /// <summary>Which cells' middles are inside the areas (even-odd), row by row: each edge's crossings put in the rows it spans.</summary>
    static bool[] Fill(PathsD areas, double x0, double y0, double r, int nx, int ny)
    {
        var cells = new bool[nx * ny];
        if (areas.Count == 0) return cells;
        var rows = new List<double>?[ny];
        foreach (var p in areas)
            for (int i = 0, j = p.Count - 1; i < p.Count; j = i++)
            {
                var a = p[j];
                var c = p[i];
                if (a.y == c.y) continue;
                double lo = Math.Min(a.y, c.y), hi = Math.Max(a.y, c.y);
                // Rows whose middles (y0 + (y + 0.5) r) lie in [lo, hi).
                int from = Math.Max(0, (int)Math.Ceiling((lo - y0) / r - 0.5)), to = Math.Min(ny - 1, (int)Math.Ceiling((hi - y0) / r - 0.5) - 1);
                for (int y = from; y <= to; y++)
                {
                    double py = y0 + (y + 0.5) * r;
                    (rows[y] ??= []).Add(a.x + (py - a.y) / (c.y - a.y) * (c.x - a.x));
                }
            }
        for (int y = 0; y < ny; y++)
        {
            if (rows[y] is not { } xs) continue;
            xs.Sort();
            for (int k = 0; k + 1 < xs.Count; k += 2)
            {
                int from = Math.Max(0, (int)Math.Ceiling((xs[k] - x0) / r - 0.5)), to = Math.Min(nx - 1, (int)Math.Floor((xs[k + 1] - x0) / r - 0.5));
                for (int x = from; x <= to; x++) cells[y * nx + x] = true;
            }
        }
        return cells;
    }

    /// <summary>Marks the cells a scored line runs through (and their neighbours).</summary>
    static void Stamp(bool[] cells, PointD a, PointD c, double x0, double y0, double r, int nx, int ny)
    {
        double len = Math.Sqrt((c.x - a.x) * (c.x - a.x) + (c.y - a.y) * (c.y - a.y));
        int steps = Math.Max(1, (int)Math.Ceiling(len / (r / 2)));
        for (int s = 0; s <= steps; s++)
        {
            double t = (double)s / steps;
            int cx = (int)((a.x + (c.x - a.x) * t - x0) / r), cy = (int)((a.y + (c.y - a.y) * t - y0) / r);
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int x = cx + dx, y = cy + dy;
                    if (x >= 0 && y >= 0 && x < nx && y < ny) cells[y * nx + x] = true;
                }
        }
    }

    // ---- the numbers, as single strokes ----

    /// <summary>A number as it's scored: underlined when it would read as another number upside down (6 and 9, 16 and 91).</summary>
    public static string Text(int n) => n.ToString(System.Globalization.CultureInfo.InvariantCulture);

    static bool Underlined(string text)
    {
        if (text.Any(c => c is not ('0' or '1' or '6' or '8' or '9'))) return false;
        var turned = new string([.. text.Reverse().Select(c => c switch { '6' => '9', '9' => '6', _ => c })]);
        return turned != text;
    }

    const double Wide = 0.6, Gap = 0.28;

    /// <summary>How wide a number is when it's <paramref name="h"/> tall (mm).</summary>
    public static double Width(string text, double h) => h * (text.Length * Wide + (text.Length - 1) * Gap);

    /// <summary>How tall a number's box is when its digits are <paramref name="h"/> tall (mm): taller with an underline.</summary>
    public static double Height(string text, double h) => Underlined(text) ? h * (1 + Drop) : h;

    const double Drop = 0.3;

    /// <summary>
    /// A number as single strokes, its digits <paramref name="h"/> tall, the middle of its box (<see cref="Width"/> ×
    /// <see cref="Height"/>) at (<paramref name="cx"/>, <paramref name="cy"/>), y up.
    /// </summary>
    public static List<PathD> Write(string text, double cx, double cy, double h)
    {
        var lines = new List<PathD>();
        double x = cx - Width(text, h) / 2, y = cy - Height(text, h) / 2 + (Underlined(text) ? Drop * h : 0);
        foreach (char ch in text)
        {
            if (ch is >= '0' and <= '9')
                foreach (var stroke in Digit(ch - '0'))
                    lines.Add([.. stroke.Select(p => new PointD(x + p.x * h, y + p.y * h))]);
            x += (Wide + Gap) * h;
        }
        if (Underlined(text))
        {
            double under = cy - Height(text, h) / 2;
            lines.Add([new PointD(cx - Width(text, h) / 2, under), new PointD(cx + Width(text, h) / 2, under)]);
        }
        return lines;
    }

    /// <summary>A digit's strokes in a box 0.6 wide and 1 tall (y up).</summary>
    static List<PathD> Digit(int d)
    {
        static PathD Oval(double cx, double cy, double rx, double ry, double a0 = 0, double a1 = 360, int steps = 20)
        {
            var p = new PathD();
            for (int i = 0; i <= steps; i++)
            {
                double a = (a0 + (a1 - a0) * i / steps) * Math.PI / 180;
                p.Add(new PointD(cx + rx * Math.Cos(a), cy + ry * Math.Sin(a)));
            }
            return p;
        }
        static PathD P(params double[] xy)
        {
            var p = new PathD();
            for (int i = 0; i + 1 < xy.Length; i += 2) p.Add(new PointD(xy[i], xy[i + 1]));
            return p;
        }
        static PathD Join(PathD a, PathD b) => [.. a, .. b];
        switch (d)
        {
            case 0: return [Oval(0.3, 0.5, 0.3, 0.5)];
            case 1: return [P(0.1, 0.78, 0.33, 1, 0.33, 0)];
            case 2: return [Join(Oval(0.3, 0.72, 0.29, 0.28, 160, -20, 12), P(0.03, 0, 0.6, 0))];
            case 3: return [Join(Oval(0.29, 0.76, 0.26, 0.24, 150, -90, 12), Oval(0.3, 0.27, 0.3, 0.27, 90, -150, 14))];
            case 4: return [P(0.45, 0, 0.45, 1, 0, 0.3, 0.6, 0.3)];
            case 5: return [Join(P(0.56, 1, 0.1, 1, 0.05, 0.56), Oval(0.3, 0.3, 0.3, 0.3, 130, -140, 16))];
            case 6: return [Join(Oval(0.33, 0.62, 0.27, 0.38, 60, 180, 8), Oval(0.3, 0.3, 0.3, 0.3, 180, 540, 20))];
            case 7: return [P(0, 1, 0.6, 1, 0.18, 0)];
            case 8: return [Oval(0.3, 0.76, 0.24, 0.24, -90, 270), Oval(0.3, 0.27, 0.3, 0.27, 90, 450)];
            case 9: return [.. Digit(6).Select(s => new PathD(s.Select(p => new PointD(0.6 - p.x, 1 - p.y))))];
            default: return [];
        }
    }
}
