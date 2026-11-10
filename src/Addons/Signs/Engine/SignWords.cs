using System.Collections.Concurrent;
using PhotonCanvas.Core;
using PhotonCanvas.Doc;

namespace PhotonCanvas.Addon.Signs;

/// <summary>
/// The words (and the motifs and rule that go with them) laid out on the sign as closed outlines in
/// mm: each line from the font, stacked and centred, then made as big as fits inside the border (or
/// the size asked for). Outlines are filled by the non-zero rule, so letters that overlap merge.
/// </summary>
public sealed class SignWords
{
    /// <summary>Every outline, in the sign's mm.</summary>
    public IReadOnlyList<Vec2[]> Outlines { get; }
    /// <summary>The capitals' height on a 100% line, as made (mm).</summary>
    public double LetterHeightMm { get; }
    /// <summary>Everything the words cover (empty with no words).</summary>
    public Rect2 Box { get; }
    /// <summary>The words keep clear of the border, the edge and the screw holes.</summary>
    public bool Fits { get; }
    /// <summary>Each line's capitals' height (mm), in order.</summary>
    public IReadOnlyList<double> LineHeights { get; }

    SignWords(List<Vec2[]> outlines, double letter, bool fits, List<double> lines)
    {
        Outlines = outlines;
        LetterHeightMm = letter;
        Fits = fits;
        LineHeights = lines;
        var box = Rect2.Empty;
        foreach (var o in outlines) foreach (var p in o) box = box.Include(p);
        Box = box;
    }

    /// <summary>Capitals this high on a 100% line while laying out (mm); scaled afterwards.</summary>
    const double Nominal = 10;

    /// <summary>The words for <paramref name="spec"/> on <paramref name="outline"/>, kept <paramref name="clear"/> mm from the edge and away from the holes.</summary>
    public static SignWords Make(SignSpec spec, SignOutline outline, double clear, IReadOnlyList<Vec2> holes, double holeClear)
    {
        var lines = spec.Words;
        if (lines.Count == 0) return new([], spec.FitWords ? 0 : spec.LetterHeightMm, true, []);
        string font = SignFonts.Resolve(spec.Font);
        double cap = SignFonts.CapRatio(font, spec.Bold, spec.Italic);
        var laid = new List<(List<Vec2[]> Polys, Rect2 Box, double Cap)>();
        foreach (var line in lines)
        {
            double capH = Nominal * Math.Clamp(line.SizePct, 5, 400) / 100;
            var style = new TextStyle(
                FontFamily: font, SizeMm: capH / cap, Bold: spec.Bold, Italic: spec.Italic,
                LetterSpacingMm: spec.LetterSpacingPct / 100 * capH, Align: TextAlign.Center,
                BendDeg: Math.Clamp(line.BendDeg, -300, 300));
            var g = TextLayout.Build(line.Text.Trim(), style);
            var polys = g.Flatten(0.008).Where(f => f.Points.Count >= 3).Select(f => f.Points.ToArray()).ToList();
            var box = Rect2.Bounds(polys.SelectMany(p => p));
            if (box.IsEmpty) continue;
            laid.Add((polys, box, capH));
        }
        if (laid.Count == 0) return new([], spec.FitWords ? 0 : spec.LetterHeightMm, true, []);

        // Stack the lines, each centred, with the gap asked for between them.
        var all = new List<Vec2[]>();
        double y = 0;
        var placed = new List<Rect2>();
        for (int i = 0; i < laid.Count; i++)
        {
            var (polys, box, capH) = laid[i];
            if (i > 0)
            {
                double gap = spec.LineGapPct / 100 * Math.Max(capH, laid[i - 1].Cap);
                if (i == 1 && spec.Rule) gap += 0.9 * laid[0].Cap;
                y += gap;
            }
            var shift = new Vec2(-box.Center.X, y - box.MinY);
            foreach (var p in polys) all.Add([.. p.Select(q => q + shift)]);
            placed.Add(new Rect2(box.MinX + shift.X, y, box.MaxX + shift.X, y + box.Height));
            y += box.Height;
        }

        // Motifs either side of the first line, level with its ends.
        if (spec.Decoration != Decoration.None)
        {
            var first = placed[0];
            double s = 0.9 * laid[0].Cap, gap = 0.55 * s;
            var firstPolys = all.Take(laid[0].Polys.Count).ToList();
            double EndY(bool left)
            {
                double edge = left ? first.MinX + 0.12 * first.Width : first.MaxX - 0.12 * first.Width;
                var ys = firstPolys.SelectMany(p => p).Where(p => left ? p.X <= edge : p.X >= edge).Select(p => p.Y).ToList();
                return ys.Count == 0 ? first.Center.Y : (ys.Min() + ys.Max()) / 2;
            }
            all.AddRange(Motifs.Make(spec.Decoration, new Vec2(first.MinX - gap - s / 2, EndY(true)), s, false));
            all.AddRange(Motifs.Make(spec.Decoration, new Vec2(first.MaxX + gap + s / 2, EndY(false)), s, true));
        }
        // A rule under the first line.
        if (spec.Rule && placed.Count > 1)
        {
            double ry = (placed[0].MaxY + placed[1].MinY) / 2;
            double len = Math.Max(0.6 * Math.Max(placed[0].Width, placed[1].Width), 3 * laid[0].Cap);
            all.AddRange(Motifs.Rule(new Vec2(0, ry), len, laid[0].Cap));
        }

        var block = Rect2.Bounds(all.SelectMany(p => p));
        var centre = new Vec2(outline.W / 2, outline.H / 2 + spec.OffsetYMm);
        var pts = all.SelectMany(p => p).Select(p => p - block.Center).ToArray();

        bool Ok(double k)
        {
            foreach (var q in pts)
            {
                double x = centre.X + q.X * k, yy = centre.Y + q.Y * k;
                if (outline.Inset(x, yy) < clear) return false;
                foreach (var h in holes)
                    if ((x - h.X) * (x - h.X) + (yy - h.Y) * (yy - h.Y) < holeClear * holeClear) return false;
            }
            return true;
        }

        double scale;
        bool fits;
        if (spec.FitWords)
        {
            // Grow until something touches, then home in on where.
            double max = Math.Max(outline.W / Math.Max(1e-6, block.Width), outline.H / Math.Max(1e-6, block.Height)) * 1.05;
            double good = 0, bad = max;
            const int Steps = 60;
            for (int i = 1; i <= Steps; i++)
            {
                double k = max * i / Steps;
                if (Ok(k)) good = k; else { bad = k; break; }
            }
            if (good >= max) bad = max;
            for (int i = 0; i < 24 && bad - good > 1e-4; i++)
            {
                double mid = (good + bad) / 2;
                if (Ok(mid)) good = mid; else bad = mid;
            }
            scale = good;
            fits = good > 0;
        }
        else
        {
            scale = spec.LetterHeightMm / Nominal;
            fits = Ok(scale);
        }
        var outlines = all.Select(p => p.Select(q => centre + (q - block.Center) * scale).ToArray()).ToList();
        return new(outlines, Nominal * scale, fits, [.. laid.Select(l => l.Cap * scale)]);
    }
}

/// <summary>The fonts the sign uses: the one asked for if it's installed, else a serif that is.</summary>
public static class SignFonts
{
    /// <summary>Serif faces for carving, best first, found on most computers.</summary>
    public static readonly IReadOnlyList<string> Preferred =
        ["Georgia", "Times New Roman", "Cambria", "Book Antiqua", "Palatino Linotype", "Constantia", "DejaVu Serif", "Liberation Serif", "Noto Serif"];

    static IReadOnlyList<string>? _families;
    static readonly ConcurrentDictionary<string, double> Caps = new();

    public static IReadOnlyList<string> Families => _families ??= TextLayout.FontFamilies();

    public static bool Installed(string name) => Families.Any(f => string.Equals(f, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>The font to use for <paramref name="name"/>.</summary>
    public static string Resolve(string name)
    {
        if (!string.IsNullOrWhiteSpace(name) && Installed(name)) return name;
        return Preferred.FirstOrDefault(Installed) ?? Families.FirstOrDefault() ?? name;
    }

    /// <summary>Capitals' height as a share of the font size, measured on an H.</summary>
    public static double CapRatio(string font, bool bold, bool italic) =>
        Caps.GetOrAdd($"{font}|{bold}|{italic}", _ =>
        {
            var b = TextLayout.Build("H", new TextStyle(FontFamily: font, SizeMm: 100, Bold: bold, Italic: italic)).Bounds();
            return b.IsEmpty || !(b.Height > 5) ? 0.7 : b.Height / 100;
        });
}
