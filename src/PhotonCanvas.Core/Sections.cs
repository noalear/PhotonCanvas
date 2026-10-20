namespace PhotonCanvas.Core;

/// <summary>
/// Marking a design bigger than the work area in sections: each block is cut to one section's
/// rectangle and moved into the work area. Between sections the material is moved by hand by
/// the same amount the other way (against a fence or stop), so the pieces meet.
/// </summary>
public static class Sections
{
    /// <summary>
    /// The parts of <paramref name="b"/> inside <paramref name="clip"/> (design mm), moved by
    /// <paramref name="shift"/>. Nothing comes back when nothing of it is inside.
    /// </summary>
    /// <summary>A picture laid out at an angle, with only its dots inside <paramref name="clip"/>, moved by <paramref name="shift"/>; null when none are.</summary>
    static DotBlock? ClipDots(DotBlock d, Rect2 clip, Vec2 shift)
    {
        var bmp = new Bitmap(d.Bitmap.Width, d.Bitmap.Height);
        byte[]? levels = d.Levels is null ? null : new byte[d.Levels.Length];
        bool any = false;
        for (int r = 0; r < bmp.Height; r++)
            for (int c = 0; c < bmp.Width; c++)
            {
                if (!d.Bitmap[c, r] || !clip.Contains(d.At(c, r))) continue;
                bmp[c, r] = true;
                if (levels is not null) levels[r * bmp.Width + c] = d.Levels![r * bmp.Width + c];
                any = true;
            }
        return any ? d with { Bitmap = bmp, Levels = levels, Origin = d.Origin + shift, PassBlocks = null } : null;
    }

    public static IEnumerable<Block> Cut(Block b, Rect2 clip, Vec2 shift)
    {
        switch (b)
        {
            case VectorBlock v:
                var pts = v.Path.EmittedPoints();
                if (pts.TrueForAll(clip.Contains))
                {
                    var moved = pts.Select(p => p + shift).ToList();
                    yield return v with { Path = v.Path.Closed ? Polyline.Loop(moved.Take(moved.Count - 1)) : Polyline.Open(moved) };
                    yield break;
                }
                foreach (var piece in ClipPolyline(pts, clip))
                    if (piece.Count >= 2) yield return v with { Path = Polyline.Open(piece.Select(p => p + shift)) };
                break;
            case FillBlock f:
                var inside = f.ClipRect is { } old ? Intersect(old, clip) : clip;
                if (inside.IsEmpty) yield break;
                yield return f with
                {
                    Polygons = [.. f.Polygons.Select(poly => (IReadOnlyList<Vec2>)[.. poly.Select(p => p + shift)])],
                    Mask = f.Mask?.Shifted(shift),
                    ClipRect = Move(inside, shift),
                };
                break;
            case LoopsBlock l:
                var pieces = new List<IReadOnlyList<Vec2>>();
                bool allIn = l.Loops.All(loop => loop.All(clip.Contains));
                foreach (var loop in l.Loops)
                {
                    if (allIn) { pieces.Add([.. loop.Select(p => p + shift)]); continue; }
                    var line = l.Closed && loop.Count > 0 ? [.. loop, loop[0]] : loop.ToList();
                    foreach (var piece in ClipPolyline(line, clip))
                        if (piece.Count >= 2) pieces.Add([.. piece.Select(p => p + shift)]);
                }
                if (pieces.Count > 0) yield return l with { Loops = pieces, Closed = allIn && l.Closed };
                break;
            case PointsBlock p:
                var inPts = p.Points.Where(clip.Contains).Select(x => x + shift).ToList();
                if (inPts.Count > 0) yield return p with { Points = inPts };
                break;
            case DotBlock { AngleDeg: not 0 } or DotBlock { PassBlocks: not null }:
                // Rows at an angle: the dots outside the section are dropped, the grid stays.
                var dd = (DotBlock)b;
                var passes = (dd.PassBlocks ?? [dd]).Select(pb => ClipDots(pb, clip, shift)).ToList();
                if (passes.All(pb => pb is null)) yield break;
                // A pass with nothing in the section stays as an empty one (it burns nothing).
                var kept = passes.Select((pb, i) => pb ?? (dd.PassBlocks ?? [dd])[i] with { Bitmap = new Bitmap(1, 1), Levels = null, Origin = (dd.PassBlocks ?? [dd])[i].Origin + shift }).ToList();
                yield return dd.PassBlocks is null ? kept[0] : kept[0] with { PassBlocks = kept };
                break;
            case DotBlock d:
                double pitch = 25.4 / d.Dpi;
                int c0 = Math.Max(0, (int)Math.Ceiling((clip.MinX - d.Origin.X) / pitch - 1e-9));
                int c1 = Math.Min(d.Bitmap.Width - 1, (int)Math.Floor((clip.MaxX - d.Origin.X) / pitch - 1e-9));
                int r0 = Math.Max(0, (int)Math.Ceiling((clip.MinY - d.Origin.Y) / pitch - 1e-9));
                int r1 = Math.Min(d.Bitmap.Height - 1, (int)Math.Floor((clip.MaxY - d.Origin.Y) / pitch - 1e-9));
                if (c1 < c0 || r1 < r0) yield break;
                var bmp = new Bitmap(c1 - c0 + 1, r1 - r0 + 1);
                byte[]? levels = d.Levels is null ? null : new byte[bmp.Width * bmp.Height];
                bool any = false;
                for (int r = r0; r <= r1; r++)
                    for (int c = c0; c <= c1; c++)
                    {
                        if (!d.Bitmap[c, r]) continue;
                        bmp[c - c0, r - r0] = true;
                        if (levels is not null) levels[(r - r0) * bmp.Width + c - c0] = d.Levels![r * d.Bitmap.Width + c];
                        any = true;
                    }
                if (any) yield return d with { Bitmap = bmp, Origin = new Vec2(d.Origin.X + c0 * pitch, d.Origin.Y + r0 * pitch) + shift, Levels = levels };
                break;
        }
    }

    static Rect2 Move(Rect2 r, Vec2 d) => new(r.MinX + d.X, r.MinY + d.Y, r.MaxX + d.X, r.MaxY + d.Y);

    static Rect2 Intersect(Rect2 a, Rect2 b)
    {
        double x0 = Math.Max(a.MinX, b.MinX), y0 = Math.Max(a.MinY, b.MinY), x1 = Math.Min(a.MaxX, b.MaxX), y1 = Math.Min(a.MaxY, b.MaxY);
        return x1 <= x0 || y1 <= y0 ? Rect2.Empty : new Rect2(x0, y0, x1, y1);
    }

    /// <summary>The runs of a polyline that lie inside the rectangle (Liang–Barsky on each segment).</summary>
    public static List<List<Vec2>> ClipPolyline(IReadOnlyList<Vec2> pts, Rect2 r)
    {
        var o = new List<List<Vec2>>();
        List<Vec2>? run = null;
        for (int i = 0; i + 1 < pts.Count; i++)
        {
            if (ClipSegment(pts[i], pts[i + 1], r) is not { } seg)
            {
                run = null;
                continue;
            }
            if (run is null || run[^1].Dist(seg.A) > 1e-9)
            {
                run = [seg.A];
                o.Add(run);
            }
            run.Add(seg.B);
            if (seg.B.Dist(pts[i + 1]) > 1e-9) run = null; // left the rectangle
        }
        return o;
    }

    static (Vec2 A, Vec2 B)? ClipSegment(Vec2 a, Vec2 b, Rect2 r)
    {
        double t0 = 0, t1 = 1, dx = b.X - a.X, dy = b.Y - a.Y;
        bool Clip(double p, double q)
        {
            if (Math.Abs(p) < 1e-15) return q >= 0;
            double t = q / p;
            if (p < 0) { if (t > t1) return false; if (t > t0) t0 = t; }
            else { if (t < t0) return false; if (t < t1) t1 = t; }
            return true;
        }
        if (!(Clip(-dx, a.X - r.MinX) && Clip(dx, r.MaxX - a.X) && Clip(-dy, a.Y - r.MinY) && Clip(dy, r.MaxY - a.Y))) return null;
        if (t1 - t0 < 1e-12) return null;
        return (a + (b - a) * t0, a + (b - a) * t1);
    }
}
