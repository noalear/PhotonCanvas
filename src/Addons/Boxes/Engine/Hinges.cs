using Clipper2Lib;

namespace PhotonCanvas.Addon.Boxes;

/// <summary>A living hinge's slits: rows of cuts across the bend, staggered so the sheet between them twists.</summary>
public sealed record HingeStyle(double SpacingMm, double CutMm, double GapMm)
{
    /// <summary>Sensible slits for a sheet <paramref name="t"/> thick, across a strip <paramref name="across"/> wide (0s are filled in).</summary>
    public static HingeStyle For(double t, double across, double spacing = 0, double cut = 0, double gap = 0)
    {
        double s = spacing > 0 ? spacing : Math.Clamp(0.5 * t, 1, 3);
        double g = gap > 0 ? gap : Math.Clamp(t, 2, 4);
        double c = cut > 0 ? cut : Math.Clamp((across - 2 * g) / 2.6, 6, 30);
        return new(s, c, g);
    }
}

public static class Hinges
{
    /// <summary>
    /// The slits that let a strip bend across [<paramref name="a0"/>, <paramref name="a1"/>] (along the
    /// bend), between <paramref name="b0"/> and <paramref name="b1"/> across it. Every other row runs out
    /// through the strip's edges, the rest stop short of them; the cuts lie along b. With
    /// <paramref name="alongU"/> a is the flat u coordinate (slits up and down), else v (slits across).
    /// </summary>
    public static List<PathD> Slits(double a0, double a1, double b0, double b1, HingeStyle h, bool alongU)
    {
        var lines = new List<PathD>();
        double len = a1 - a0, across = b1 - b0;
        if (len <= 0 || across <= 0) return lines;
        int rows = Math.Max(1, (int)Math.Floor(len / h.SpacingMm) + 1);
        double first = (a0 + a1) / 2 - (rows - 1) * h.SpacingMm / 2;
        double period = h.CutMm + h.GapMm;
        for (int r = 0; r < rows; r++)
        {
            double a = first + r * h.SpacingMm;
            bool inner = r % 2 == 0;
            double lo = inner ? b0 + h.GapMm / 2 : b0, hi = inner ? b1 - h.GapMm / 2 : b1;
            double start = inner ? b0 + h.GapMm / 2 : b0 + h.GapMm / 2 + period / 2 - period;
            for (double b = start; b < b1; b += period)
            {
                double s = Math.Max(lo, b), e = Math.Min(hi, b + h.CutMm);
                if (e - s < 0.5) continue;
                lines.Add(alongU ? [new(a, s), new(a, e)] : [new(s, a), new(e, a)]);
            }
        }
        return lines;
    }
}
