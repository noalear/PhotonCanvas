using Clipper2Lib;
using PhotonCanvas.Addon.Terrain;

namespace PhotonCanvas.Addon.Furniture;

/// <summary>How doors, drawers and panels are drawn on a piece in a theme.</summary>
public enum FrontLook
{
    /// <summary>Boards with iron strap hinges and a ring pull (village, dungeon, cave).</summary>
    Plank,
    /// <summary>A raised panel with a carved star (desert).</summary>
    Carved,
    /// <summary>A raised panel under a pointed arch, a keyhole escutcheon (gothic).</summary>
    Gothic,
    /// <summary>Chamfered metal panels, rivets and bar handles (sci-fi).</summary>
    Metal,
}

/// <summary>
/// The detail on the fronts of cabinets, desks and the like: doors, drawers, panels, handles, all
/// etched (areas) and scored (lines) in a piece's flat coordinates (mm, y up), and windows cut through.
/// </summary>
public static class Fronts
{
    public const double Groove = WoodFace.GrooveMm;

    public static FrontLook Look(Maker m) => m.IsMetal ? FrontLook.Metal : m.Theme switch
    {
        TerrainThemes.SciFi => FrontLook.Metal,
        TerrainThemes.Gothic => FrontLook.Gothic,
        TerrainThemes.Desert => FrontLook.Carved,
        _ => FrontLook.Plank,
    };

    /// <summary>A groove round a rectangle (its edge, as an etched band).</summary>
    public static PathsD Outline(double x0, double y0, double x1, double y1, double w = Groove)
    {
        var r = new PathsD { Geo.Rect(x0, y0, x1, y1) };
        return Geo.Subtract(Geo.Offset(r, w / 2), Geo.Offset(r, -w / 2));
    }

    /// <summary>A groove round any outline.</summary>
    public static PathsD Outline(PathsD shape, double w = Groove) => Geo.Subtract(Geo.Offset(shape, w / 2), Geo.Offset(shape, -w / 2));

    /// <summary>A rectangle with its corners cut off.</summary>
    public static PathD Chamfered(double x0, double y0, double x1, double y1, double c)
    {
        c = Math.Max(0, Math.Min(c, Math.Min(x1 - x0, y1 - y0) / 2 - 1e-3));
        return [new(x0 + c, y0), new(x1 - c, y0), new(x1, y0 + c), new(x1, y1 - c), new(x1 - c, y1), new(x0 + c, y1), new(x0, y1 - c), new(x0, y0 + c)];
    }

    /// <summary>A rectangle with a pointed (gothic) top: straight sides up to where the arch springs.</summary>
    public static PathD PointedRect(double x0, double y0, double x1, double y1)
    {
        double w = x1 - x0, rise = Math.Min(w * 0.75, (y1 - y0) * 0.6), spring = y1 - rise;
        var p = new PathD { new(x0, y0), new(x1, y0), new(x1, spring) };
        // Two arcs of radius w meeting at the top middle (an equilateral arch squashed to the rise).
        int n = 12;
        for (int i = 1; i <= n; i++)
        {
            double t = (double)i / n;
            double a = t * Math.PI / 3;
            p.Add(new PointD(x0 + w * Math.Cos(a), spring + rise * Math.Sin(a) / Math.Sin(Math.PI / 3)));
        }
        for (int i = 1; i < n; i++)
        {
            double t = (double)i / n;
            double a = Math.PI * 2 / 3 + t * Math.PI / 3;
            p.Add(new PointD(x1 + w * Math.Cos(a), spring + rise * Math.Sin(a) / Math.Sin(Math.PI / 3)));
        }
        p.Add(new PointD(x0, spring));
        return p;
    }

    /// <summary>A rectangle with a round top.</summary>
    public static PathD ArchedRect(double x0, double y0, double x1, double y1)
    {
        double w = x1 - x0, r = w / 2, spring = Math.Max(y0, y1 - r);
        var p = new PathD { new(x0, y0), new(x1, y0), new(x1, spring) };
        p.AddRange(Geo.Arc(x0 + r, spring, r, 0, Math.PI).Skip(1));
        return p;
    }

    /// <summary>
    /// A door in the rectangle: its edge as a groove, and the look's detail inside it. The handle goes on
    /// the side away from the hinges.
    /// </summary>
    public static void Door(FrontLook look, double x0, double y0, double x1, double y1, bool hingeLeft, Random rnd, PathsD etched, List<PathD> lines, bool handle = true)
    {
        double w = x1 - x0, h = y1 - y0;
        if (w < 1.5 || h < 1.5) return;
        double hx = hingeLeft ? x1 - Math.Min(1.2, w * 0.18) : x0 + Math.Min(1.2, w * 0.18), hy = y0 + h * 0.5;
        switch (look)
        {
            case FrontLook.Metal:
            {
                double c = Math.Min(1.0, Math.Min(w, h) * 0.15);
                lines.Add(Geo.Loop(Chamfered(x0, y0, x1, y1, c)));
                double i = Math.Min(1.0, Math.Min(w, h) * 0.15);
                if (w > 4 && h > 4) lines.Add(Geo.Loop(Chamfered(x0 + i, y0 + i, x1 - i, y1 - i, c)));
                foreach (var (x, y) in new[] { (x0 + i * 0.5, y0 + i * 0.5), (x1 - i * 0.5, y0 + i * 0.5), (x0 + i * 0.5, y1 - i * 0.5), (x1 - i * 0.5, y1 - i * 0.5) })
                    if (w > 3 && h > 3) lines.Add(WoodFace.Dot(x, y, 0.15));
                if (handle) etched.Add(WoodFace.RoundRect(hx - 0.25, hy - Math.Min(1.6, h * 0.15), hx + 0.25, hy + Math.Min(1.6, h * 0.15), 0.25));
                break;
            }
            case FrontLook.Gothic:
            {
                etched.AddRange(Outline(x0, y0, x1, y1));
                double i = Math.Min(1.1, Math.Min(w, h) * 0.16);
                if (w > 3 && h > 4)
                {
                    var panel = PointedRect(x0 + i, y0 + i, x1 - i, y1 - i);
                    etched.AddRange(Outline([panel]));
                    var inner = PointedRect(x0 + i * 1.8, y0 + i * 1.8, x1 - i * 1.8, y1 - i * 1.8);
                    if (w > 5) lines.Add(Geo.Loop(inner));
                }
                if (handle) Keyhole(hx, hy, Math.Min(0.9, w * 0.12), lines);
                break;
            }
            case FrontLook.Carved:
            {
                etched.AddRange(Outline(x0, y0, x1, y1));
                double i = Math.Min(1.0, Math.Min(w, h) * 0.15);
                if (w > 3 && h > 3)
                {
                    etched.AddRange(Outline(x0 + i, y0 + i, x1 - i, y1 - i));
                    double r = Math.Min(w, h) * 0.5 - i * 1.8;
                    if (r > 0.8) lines.Add(Geo.Loop(Star((x0 + x1) / 2, (y0 + y1) / 2 + (h > w * 1.4 ? h * 0.12 : 0), r, r * 0.55, 8)));
                }
                if (handle) { lines.Add(WoodFace.Dot(hx, hy, 0.3)); etched.Add(Geo.Circle(hx, hy, 0.12, 0.01)); }
                break;
            }
            default:
            {
                etched.AddRange(Outline(x0, y0, x1, y1));
                // Boards: grooves up the door.
                int boards = Math.Max(1, (int)Math.Round(w / 2.4));
                for (int b = 1; b < boards; b++)
                {
                    double x = x0 + w * b / boards;
                    etched.Add(Geo.Rect(x - Groove / 2, y0, x + Groove / 2, y1));
                }
                // Strap hinges near the top and bottom, from the hinge side, with nails.
                double sh = Geo.Clamp(h * 0.06, 0.6, 1.0), reach = w * 0.62;
                if (h > 5 && w > 2.5)
                    foreach (var y in new[] { y0 + h * 0.18, y1 - h * 0.18 })
                    {
                        double sx0 = hingeLeft ? x0 : x1 - reach, sx1 = hingeLeft ? x0 + reach : x1;
                        var strap = new PathD { new(sx0, y - sh / 2), new(sx1, y - sh / 2), new(hingeLeft ? sx1 + sh * 0.6 : sx1, y), new(sx1, y + sh / 2), new(sx0, y + sh / 2), new(hingeLeft ? sx0 : sx0 - sh * 0.6, y) };
                        etched.Add(Geo.Tidy([strap]).FirstOrDefault() ?? strap);
                    }
                if (handle)
                {
                    double r = Geo.Clamp(w * 0.08, 0.35, 0.6);
                    lines.Add(WoodFace.Dot(hx, hy, r));
                    etched.Add(Geo.Circle(hx, hy + r, 0.16, 0.01));
                }
                break;
            }
        }
    }

    /// <summary>A drawer front: its edge, a panel line in some looks, and a handle in the middle (two on a wide one).</summary>
    public static void Drawer(FrontLook look, double x0, double y0, double x1, double y1, PathsD etched, List<PathD> lines)
    {
        double w = x1 - x0, h = y1 - y0;
        if (w < 1.2 || h < 1.0) return;
        double cy = (y0 + y1) / 2;
        double[] at = w > 9 ? [x0 + w * 0.25, x0 + w * 0.75] : [(x0 + x1) / 2];
        switch (look)
        {
            case FrontLook.Metal:
                lines.Add(Geo.Loop(Chamfered(x0, y0, x1, y1, Math.Min(0.6, h * 0.2))));
                foreach (var x in at) etched.Add(WoodFace.RoundRect(x - Math.Min(1.4, w * 0.15), cy - 0.22, x + Math.Min(1.4, w * 0.15), cy + 0.22, 0.2));
                break;
            case FrontLook.Gothic:
                etched.AddRange(Outline(x0, y0, x1, y1));
                if (h > 2.5 && w > 3) lines.Add(Geo.Loop(Geo.Rect(x0 + 0.6, y0 + 0.6, x1 - 0.6, y1 - 0.6)));
                foreach (var x in at) { lines.Add(WoodFace.Dot(x, cy + Math.Min(0.3, h * 0.12), 0.18)); lines.Add(HalfRing(x, cy + Math.Min(0.3, h * 0.12), Math.Min(0.7, h * 0.3))); }
                break;
            case FrontLook.Carved:
                etched.AddRange(Outline(x0, y0, x1, y1));
                foreach (var x in at) { lines.Add(WoodFace.Dot(x, cy, Math.Min(0.3, h * 0.2))); etched.Add(Geo.Circle(x, cy, 0.12, 0.01)); }
                break;
            default:
                etched.AddRange(Outline(x0, y0, x1, y1));
                foreach (var x in at) { lines.Add(WoodFace.Dot(x, cy, Math.Min(0.4, h * 0.25))); etched.Add(Geo.Circle(x, cy + Math.Min(0.4, h * 0.25), 0.14, 0.01)); }
                break;
        }
    }

    /// <summary>A plain panel in a frame (counters, desk ends): no handle.</summary>
    public static void Panel(FrontLook look, double x0, double y0, double x1, double y1, PathsD etched, List<PathD> lines)
    {
        double w = x1 - x0, h = y1 - y0;
        if (w < 1.5 || h < 1.5) return;
        double i = Math.Min(1.0, Math.Min(w, h) * 0.18);
        switch (look)
        {
            case FrontLook.Metal:
                lines.Add(Geo.Loop(Chamfered(x0, y0, x1, y1, Math.Min(1.0, Math.Min(w, h) * 0.2))));
                if (w > 3 && h > 3)
                {
                    lines.Add(Geo.Loop(Chamfered(x0 + i, y0 + i, x1 - i, y1 - i, Math.Min(0.8, Math.Min(w, h) * 0.15))));
                    foreach (var (x, y) in new[] { (x0 + i / 2, y0 + i / 2), (x1 - i / 2, y0 + i / 2), (x0 + i / 2, y1 - i / 2), (x1 - i / 2, y1 - i / 2) }) lines.Add(WoodFace.Dot(x, y, 0.14));
                }
                break;
            case FrontLook.Gothic:
                etched.AddRange(Outline(x0, y0, x1, y1));
                if (w > 2.5 && h > 3) etched.AddRange(Outline([PointedRect(x0 + i, y0 + i, x1 - i, y1 - i)]));
                // Linen-fold: wavy lines down the panel.
                if (w > 4 && h > 6)
                    for (double x = x0 + i * 2; x < x1 - i * 1.8; x += 0.9)
                        lines.Add([new(x, y0 + i * 1.8), new(x + 0.2, y0 + h * 0.35), new(x - 0.2, y0 + h * 0.55), new(x, y1 - i * 1.8 - Math.Min(w * 0.4, h * 0.3))]);
                break;
            case FrontLook.Carved:
                etched.AddRange(Outline(x0, y0, x1, y1));
                if (w > 3 && h > 3)
                {
                    etched.AddRange(Outline(x0 + i, y0 + i, x1 - i, y1 - i));
                    double r = Math.Min(w, h) * 0.5 - i * 1.8;
                    if (r > 0.8) lines.Add(Geo.Loop(Star((x0 + x1) / 2, (y0 + y1) / 2, r, r * 0.55, 8)));
                }
                break;
            default:
                etched.AddRange(Outline(x0, y0, x1, y1));
                if (w > 3 && h > 3) lines.Add(Geo.Loop(Geo.Rect(x0 + i, y0 + i, x1 - i, y1 - i)));
                break;
        }
    }

    /// <summary>A star with <paramref name="points"/> points.</summary>
    public static PathD Star(double cx, double cy, double r, double inner, int points)
    {
        var p = new PathD();
        for (int i = 0; i < points * 2; i++)
        {
            double a = Math.PI / 2 + Math.PI * i / points, rr = i % 2 == 0 ? r : inner;
            p.Add(new PointD(cx + rr * Math.Cos(a), cy + rr * Math.Sin(a)));
        }
        return p;
    }

    /// <summary>A keyhole in a round escutcheon.</summary>
    public static void Keyhole(double cx, double cy, double r, List<PathD> lines)
    {
        lines.Add(WoodFace.Dot(cx, cy, r));
        lines.Add(WoodFace.Dot(cx, cy + r * 0.2, r * 0.22));
        lines.Add([new(cx, cy), new(cx, cy - r * 0.55)]);
    }

    /// <summary>The bottom half of a ring: a drop handle hanging from a pin.</summary>
    public static PathD HalfRing(double cx, double cy, double r) => [.. Geo.Arc(cx, cy, r, Math.PI, Math.PI * 2)];

    /// <summary>
    /// Window panes cut through a rectangle, with bars between them at least a bridge wide: about
    /// <paramref name="pane"/> mm each. The top row follows <paramref name="top"/> (pointed or round), if given.
    /// </summary>
    public static PathsD Panes(double x0, double y0, double x1, double y1, double pane, double bar, Func<double, double, double, double, PathD>? top = null)
    {
        double w = x1 - x0, h = y1 - y0;
        if (w < 1 || h < 1) return [];
        int nx = Math.Max(1, (int)Math.Round((w + bar) / (pane + bar))), ny = Math.Max(1, (int)Math.Round((h + bar) / (pane + bar)));
        double pw = (w - (nx - 1) * bar) / nx, ph = (h - (ny - 1) * bar) / ny;
        if (pw < 0.8 || ph < 0.8) return [];
        var glass = new PathsD();
        for (int i = 0; i < nx; i++)
            for (int j = 0; j < ny; j++)
                glass.Add(Geo.Rect(x0 + i * (pw + bar), y0 + j * (ph + bar), x0 + i * (pw + bar) + pw, y0 + j * (ph + bar) + ph));
        if (top is not null) glass = Geo.Intersect(glass, [top(x0, y0, x1, y1)]);
        return [.. glass.Where(p => Math.Abs(Clipper.Area(p)) > 0.4)];
    }

    /// <summary>
    /// A lattice cut through a rectangle: diagonal bars <paramref name="bar"/> wide crossing at about
    /// <paramref name="cell"/> mm, leaving diamond holes (desert screens, cabinet doors).
    /// </summary>
    public static PathsD Lattice(double x0, double y0, double x1, double y1, double cell, double bar)
    {
        double w = x1 - x0, h = y1 - y0;
        if (w < cell * 0.8 || h < cell * 0.8) return [];
        var holes = new PathsD();
        double d = cell / Math.Sqrt(2);
        // Diamonds on a grid turned 45°, each shrunk by the bar.
        double half = cell / 2 - bar / Math.Sqrt(2);
        if (half <= 0.3) return [];
        int nx = (int)Math.Ceiling(w / cell) + 1, ny = (int)Math.Ceiling(h / cell) + 1;
        var cx0 = (x0 + x1) / 2 - nx * cell / 2;
        var cy0 = (y0 + y1) / 2 - ny * cell / 2;
        for (int i = 0; i <= nx; i++)
            for (int j = 0; j <= ny; j++)
                foreach (var off in new[] { 0.0, 0.5 })
                {
                    double cx = cx0 + (i + off) * cell, cy = cy0 + (j + off) * cell;
                    holes.Add([new(cx, cy - half), new(cx + half, cy), new(cx, cy + half), new(cx - half, cy)]);
                }
        _ = d;
        var inside = Geo.Intersect(holes, [Geo.Rect(x0, y0, x1, y1)]);
        // Keep only whole-enough holes: slivers at the edge would leave bars too thin.
        return [.. Geo.Tidy(inside).Where(p => Math.Abs(Clipper.Area(p)) > half * half * 0.9)];
    }

    /// <summary>
    /// Iron bars: tall slots cut between upright bars, broken by a flat cross band (or two on a tall door),
    /// with rivets where the bars cross a band. Returns the slots (holes); the rivets go on <paramref name="lines"/>.
    /// </summary>
    public static PathsD Bars(double x0, double y0, double x1, double y1, double gap, double bar, List<PathD> lines)
    {
        double w = x1 - x0, h = y1 - y0;
        int n = (int)Math.Floor((w + bar) / (gap + bar));
        if (n < 1 || h < 3) return [];
        double g = (w - (n - 1) * bar) / n;
        int bands = h > 22 ? 2 : 1;
        double bh = 1.2, seg = (h - bands * bh) / (bands + 1);
        if (seg < 1.5) { bands = 0; seg = h; }
        var holes = new PathsD();
        for (int i = 0; i < n; i++)
        {
            double a = x0 + i * (g + bar);
            for (int k = 0; k <= bands; k++)
            {
                double b0 = y0 + k * (seg + bh);
                holes.Add(WoodFace.RoundRect(a, b0, a + g, b0 + seg, Math.Min(0.25, g * 0.3)));
            }
        }
        for (int k = 1; k <= bands; k++)
        {
            double yb = y0 + k * seg + (k - 0.5) * bh;
            for (int i = 1; i < n; i++) lines.Add(WoodFace.Dot(x0 + i * (g + bar) - bar / 2, yb, 0.2));
        }
        return holes;
    }
}
