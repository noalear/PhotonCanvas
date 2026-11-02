using Clipper2Lib;

namespace PhotonCanvas.Addon.GameKit;

/// <summary>
/// An initiative tracker: a base glued up from layers, the upper ones with a groove along them, and a
/// tile for each player or monster that stands in the groove, its name or number etched on its front.
/// A base too long for the bed is cut in pieces, the joins staggered from layer to layer so the
/// glued-up base is solid.
/// </summary>
public static class Initiative
{
    /// <summary>Space between tiles in the groove (mm).</summary>
    public const double Spacing = 2;

    /// <summary>The base's sizes: its length and width, the groove's start, length and width, and how many grooved layers.</summary>
    public sealed record BasePlan(double Length, double Width, double End, double GrooveLength, double GrooveWidth, int GroovedLayers);

    public static BasePlan Plan(AccessoryItem it, double T, double fit)
    {
        int n = Math.Clamp(it.Tiles, 1, 40);
        double e = Math.Max(6, 2 * T), lg = n * (it.TileWMm + Spacing) + Spacing;
        double gw = T + Math.Max(fit, 0.2) + 0.3;
        int layers = Math.Max(1, (int)Math.Ceiling(5 / T - 1e-9));
        return new(lg + 2 * e, Math.Max(16, 5 * T), e, lg, gw, layers);
    }

    /// <summary>What each tile says: the names in order (one a line, or split by commas), numbers for the rest.</summary>
    public static List<string> TileLabels(AccessoryItem it)
    {
        int n = Math.Clamp(it.Tiles, 1, 40);
        var names = (it.Names ?? "").Split(['\n', ','], StringSplitOptions.None).Select(s => s.Trim()).ToList();
        return [.. Enumerable.Range(0, n).Select(i => i < names.Count && names[i].Length > 0 ? names[i] : (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture))];
    }

    public static void Make(Maker m)
    {
        var it = m.Item;
        double T = m.T, tw = it.TileWMm, th = it.TileHMm;
        if (it.Tiles < 1) { m.Bad("Have at least one tile."); return; }
        if (tw < 8) { m.Bad("Make the tiles at least 8 mm wide."); return; }
        var p = Plan(it, T, m.Fit);
        double depth = p.GroovedLayers * T;
        if (th < depth + 10) { m.Bad($"Make the tiles at least {depth + 10:0} mm high: {depth:0.#} mm of each stands in the groove."); return; }
        double Lb = p.Length, Bw = p.Width;

        // ---- the base, layer by layer (u along it, v from the front back) ----
        var groove = Geo.Rect(p.End, Bw / 2 - p.GrooveWidth / 2, p.End + p.GrooveLength, Bw / 2 + p.GrooveWidth / 2);
        int pieces = Lb <= m.MaxLength ? 1 : (int)Math.Ceiling(Lb / m.MaxLength);
        for (int j = 0; j <= p.GroovedLayers; j++)
        {
            var whole = j == 0 ? new PathsD { Geo.Rect(0, 0, Lb, Bw) } : Geo.Subtract([Geo.Rect(0, 0, Lb, Bw)], [groove]);
            string name = j == 0 ? "Base bottom" : j == p.GroovedLayers ? "Base top" : $"Base layer {j + 1}";
            var pose = Pose.Up(0, T / 2 + j * T, Bw);
            // Joins: halfway between the even layers' on the odd layers.
            var cuts = new List<double> { 0 };
            double step = Lb / pieces;
            for (int i = 1; pieces > 1 && i < pieces + (j % 2); i++) cuts.Add(step * (i - (j % 2) * 0.5));
            cuts.Add(Lb);
            for (int i = 0; i + 1 < cuts.Count; i++)
            {
                var bit = Geo.Intersect(whole, [Geo.Rect(cuts[i], -1, cuts[i + 1], Bw + 1)]);
                if (Geo.Area(bit) < 1) continue;
                var plate = m.Plate(cuts.Count > 2 ? $"{name}, piece {i + 1}" : name, pose);
                plate.Body.AddRange(bit);
                plate.MayBreak = true;
                if (j == p.GroovedLayers && j > 0) plate.Face = Geo.Intersect([Geo.Rect(0, 0, Lb, Bw)], [Geo.Rect(cuts[i], -1, cuts[i + 1], Bw + 1)]);
            }
        }

        // ---- the tiles ----
        var labels = TileLabels(it);
        double r = it.Top switch { TileTop.Arched => tw / 2, TileTop.Rounded => tw * 0.25, _ => 0 };
        if (th - r < depth + 6) r = Math.Max(0, th - depth - 6);
        var outline = Tile(tw, th, r);
        double lo = depth + 1.5, hi = th - r * 0.45 - 1.5;
        for (int i = 0; i < labels.Count; i++)
        {
            double x = p.End + Spacing + i * (tw + Spacing);
            var tile = m.Plate($"Tile {i + 1}", Pose.Front(x, T, Bw / 2));
            tile.Body.Add(outline);
            string text = labels[i];
            // A long name with a space goes on two lines.
            if (text.Length > 7 && text.Contains(' ')) { int sp = text.IndexOf(' ', text.Length / 3); text = sp > 0 ? text[..sp] + "\n" + text[(sp + 1)..] : text; }
            tile.Etched.AddRange(Labels.Text(text, m.Set.Font, tw / 2, (lo + hi) / 2, tw - 3, Math.Min(hi - lo, tw * 0.9)));
        }
        m.Note($"{labels.Count} tiles in a {p.GrooveLength:0} mm groove, {depth:0.#} mm deep." + (pieces > 1 ? $" The base is in {pieces} lengths to fit the bed: glue its layers up with the joins staggered." : " Glue the base's layers up, the grooves over each other."));
    }

    /// <summary>A tile's outline: square at the bottom, its top corners rounded by <paramref name="r"/>.</summary>
    public static PathD Tile(double w, double h, double r)
    {
        if (r <= 0.01) return Geo.Rect(0, 0, w, h);
        r = Math.Min(r, w / 2 - 1e-6);
        var p = new PathD { new(0, 0), new(w, 0) };
        p.AddRange(Geo.Arc(w - r, h - r, r, 0, Math.PI / 2, 0.01));
        p.AddRange(Geo.Arc(r, h - r, r, Math.PI / 2, Math.PI, 0.01));
        return p;
    }
}
