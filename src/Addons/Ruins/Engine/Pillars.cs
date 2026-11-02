using Clipper2Lib;

namespace PhotonCanvas.Addon.Ruins;

/// <summary>
/// Broken pillars: a stack of round, square or eight-sided layers on a pin, whole at the bottom, the top
/// few layers bitten away more and more (each still sitting on the one under it), on a square plinth.
/// </summary>
public static class Pillars
{
    public const int MaxPillars = 20;

    public static PathD Outline(PillarShape shape, double across)
    {
        double r = across / 2;
        switch (shape)
        {
            case PillarShape.Square: return Geo.Rect(-r, -r, r, r);
            case PillarShape.Octagon:
            {
                double c = r / Math.Cos(Math.PI / 8);
                return [.. Enumerable.Range(0, 8).Select(i => new PointD(c * Math.Cos(Math.PI / 8 + i * Math.PI / 4), c * Math.Sin(Math.PI / 8 + i * Math.PI / 4)))];
            }
            default: return Geo.Circle(0, 0, r);
        }
    }

    public static void Make(RuinSpec s, List<Part> parts, List<View> views, List<Finding> findings)
    {
        int count = Math.Clamp(s.Pillars, 0, MaxPillars);
        if (count == 0) return;
        double D = Math.Clamp(s.PillarMm, 3, 300), T = Math.Max(0.3, s.ThicknessMm);
        int n = Math.Clamp((int)Math.Round(s.PillarHeightMm / T), 1, 200);
        int broken = n <= 1 ? 0 : Math.Clamp((int)Math.Round(n * 0.3), 1, Math.Min(n - 1, 8));
        int whole = n - broken;
        var outline = new PathsD { Outline(s.PillarShape, D) };
        var pin = s.PinMm > 0 ? new PathsD { Geo.Circle(0, 0, s.PinMm / 2) } : [];
        if (pin.Count > 0 && s.PinMm > D - 2) { findings.Add(new(Severity.Bad, $"The pin hole is too big for a {D:0.#} mm pillar: make it under {D - 2:0.#} mm.")); return; }
        var layer = Geo.Subtract(outline, pin);
        var plinth = s.Plinth ? Geo.Subtract([Geo.Rect(-D / 2 - 2, -D / 2 - 2, D / 2 + 2, D / 2 + 2)], pin) : null;

        if (plinth is not null) parts.Add(new Part("Pillar plinth", plinth, [], [], count));
        parts.Add(new Part("Pillar layer", layer, [], [], whole * count));
        var rnd = new Random(s.Seed * 7919 + 7333);
        for (int p = 0; p < count; p++)
        {
            string who = count == 1 ? "Pillar" : $"Pillar {p + 1}";
            var stack = new List<Part>();
            if (plinth is not null) stack.Add(new Part("plinth", plinth, [], [], 1));
            stack.Add(new Part("layer", layer, [], [], 1));
            PathsD prev = outline;
            for (int j = 1; j <= broken; j++)
            {
                double sev = (double)j / broken;
                var piece = prev;
                int bites = 1 + (sev > 0.5 ? 1 : 0) + (rnd.NextDouble() < 0.3 ? 1 : 0);
                double start = rnd.NextDouble() * 2 * Math.PI;
                for (int k = 0; k < bites; k++)
                {
                    double ang = start + k * (2 * Math.PI / bites) + (rnd.NextDouble() - 0.5) * 0.8;
                    double rr = D * (0.22 + 0.33 * sev) * (0.8 + 0.4 * rnd.NextDouble());
                    var bite = Rough.Lumpy(Rough.Ellipse(Math.Cos(ang) * D / 2, Math.Sin(ang) * D / 2, rr, rr * (0.7 + 0.5 * rnd.NextDouble()), 48), rr * 0.25, Math.Max(2, rr), rnd, 0.4);
                    piece = Geo.Subtract(piece, [bite]);
                }
                // Sits on the layer under it.
                piece = Rough.Largest(Geo.Intersect(piece, Geo.Offset(prev, -0.3)));
                if (piece.Count == 0 || Geo.Area(piece) < D * D * 0.06) break;
                prev = piece;
                // The pin hole only where it's well inside what's left.
                var withPin = pin.Count > 0 && Geo.Area(Geo.Subtract(Geo.Offset(pin, 0.8), piece)) < 1e-3 ? Geo.Subtract(piece, pin) : piece;
                var part = new Part($"{who} broken layer {j}", withPin, [], [], 1);
                parts.Add(part);
                stack.Add(part);
            }
            views.Add(new View($"{who}, from above", stack));
        }
        double tall = (n + (s.Plinth ? 1 : 0)) * T;
        findings.Add(new(Severity.Note, $"Each pillar is {whole} whole layer{(whole == 1 ? "" : "s")} and up to {broken} broken one{(broken == 1 ? "" : "s")}{(s.Plinth ? " on a plinth" : "")}, {tall:0.#} mm at its highest." +
            (pin.Count > 0 ? $" Line them up on a {s.PinMm:0.#} mm pin (a cocktail stick) and glue them." : "")));
    }
}
