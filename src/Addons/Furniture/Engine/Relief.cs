using Clipper2Lib;
using PhotonCanvas.Addon.Terrain;

namespace PhotonCanvas.Addon.Furniture;

/// <summary>What a raised leaf on a front is.</summary>
public enum LeafKind { Door, Drawer, Panel }

/// <summary>One door, drawer front or panel on a front (flat, in the front's own coordinates).</summary>
public readonly record struct Leaf(double X0, double Y0, double X1, double Y1, LeafKind Kind, bool HingeLeft = true, bool Handle = true)
{
    public double W => X1 - X0;
    public double H => Y1 - Y0;
}

/// <summary>
/// Doors, drawer fronts and panels cut as their own pieces and glued on a front, so they stand proud of
/// it the way real ones do. A door or panel big enough is a frame with an opening (a pointed arch, a round
/// arch or a chamfered window, to suit the look), and the front behind shows through it as a sunk panel
/// carved with a lily, a star or vents. Dungeon and cave doors are solid boards with strap hinges.
/// Drawers are solid, with their handles. With a push fit nothing is glued, so the front is etched instead.
/// </summary>
public static class Relief
{
    /// <summary>Whether a front gets raised leaves (otherwise it's etched as before).</summary>
    public static bool Use(Maker m) => !m.PushFit;

    /// <summary>
    /// Adds the leaves as glued pieces on a front whose flat (0, 0) sits at <paramref name="origin"/>, facing +Z
    /// (its face at that z); what shows through their openings is carved on the front (<paramref name="frontEtched"/>,
    /// <paramref name="frontLines"/>, in the front's coordinates).
    /// </summary>
    public static void Add(Maker m, FrontLook look, IReadOnlyList<Leaf> leaves, V3 origin, PathsD frontEtched, List<PathD> frontLines)
    {
        bool boards = look == FrontLook.Plank && m.Theme is TerrainThemes.Dungeon or TerrainThemes.Cave;
        // Leaves the same size and the same way round are one piece cut several times.
        var groups = leaves.GroupBy(l => (Math.Round(l.W, 2), Math.Round(l.H, 2), l.Kind, l.HingeLeft, l.Handle)).ToList();
        int doors = 0, drawers = 0, panels = 0;
        foreach (var g in groups)
        {
            var first = g.First();
            double w = first.W, h = first.H;
            if (w < 1.6 || h < 1.2)
            {
                // Too small to cut as its own piece: etched on the front as before.
                foreach (var l in g)
                    switch (l.Kind)
                    {
                        case LeafKind.Drawer: Fronts.Drawer(look, l.X0, l.Y0, l.X1, l.Y1, frontEtched, frontLines); break;
                        case LeafKind.Door: Fronts.Door(look, l.X0, l.Y0, l.X1, l.Y1, l.HingeLeft, m.Rnd, frontEtched, frontLines, l.Handle); break;
                        default: Fronts.Panel(look, l.X0, l.Y0, l.X1, l.Y1, frontEtched, frontLines); break;
                    }
                continue;
            }
            var outline = new PathsD { Geo.Rect(0, 0, w, h) };
            var holes = new PathsD();
            var etched = new PathsD();
            var lines = new List<PathD>();
            var opening = Opening(look, first.Kind, w, h, boards);
            if (opening is not null)
            {
                holes.Add(opening);
                // The edge of the opening moulded: a line just outside it.
                lines.Add(Geo.Loop(Geo.Offset([opening], 0.35).FirstOrDefault() ?? opening));
                foreach (var l in g) Carve(look, m.Theme, Geo.Move([opening], l.X0, l.Y0), m.Rnd, frontEtched, frontLines);
            }
            switch (first.Kind)
            {
                case LeafKind.Drawer:
                    Fronts.Drawer(look, 0, 0, w, h, etched, lines);
                    break;
                case LeafKind.Door when opening is null:
                    Fronts.Door(look, 0, 0, w, h, first.HingeLeft, m.Rnd, etched, lines, first.Handle);
                    break;
                case LeafKind.Door:
                    Hardware(look, w, h, first.HingeLeft, first.Handle, opening, etched, lines);
                    break;
            }
            string name = first.Kind switch
            {
                LeafKind.Drawer => groups.Count(q => q.Key.Kind == LeafKind.Drawer) > 1 ? $"Drawer front {++drawers}" : "Drawer front",
                LeafKind.Panel => groups.Count(q => q.Key.Kind == LeafKind.Panel) > 1 ? $"Panel {++panels}" : "Panel",
                _ => groups.Count(q => q.Key.Kind == LeafKind.Door) > 1 ? $"Door {++doors}" : "Door",
            };
            var poses = g.Select(l => Pose.Front(origin.X + l.X0, origin.Y + l.Y0, origin.Z + m.T / 2)).ToList();
            m.Add(name, outline, holes, poses, grainAlongU: first.Kind == LeafKind.Drawer, etched: etched, lines: lines).Glue();
        }
    }

    /// <summary>The opening in a door's or panel's frame, or null for a solid one (drawers, boards, small ones).</summary>
    static PathD? Opening(FrontLook look, LeafKind kind, double w, double h, bool boards)
    {
        if (kind == LeafKind.Drawer || boards) return null;
        double side = Geo.Clamp(Math.Min(w, h) * 0.2, Maker.Bridge * 1.08, 1.7), top = side * 1.1, bottom = side * 1.35;
        if (w - 2 * side < 1.4 || h - top - bottom < 2.2) return null;
        double x0 = side, x1 = w - side, y0 = bottom, y1 = h - top;
        return look switch
        {
            FrontLook.Gothic or FrontLook.Plank => h - top - bottom > (w - 2 * side) * 0.9 ? Fronts.PointedRect(x0, y0, x1, y1) : Geo.Rect(x0, y0, x1, y1),
            FrontLook.Carved => h - top - bottom > (w - 2 * side) * 0.7 ? Fronts.ArchedRect(x0, y0, x1, y1) : Geo.Rect(x0, y0, x1, y1),
            _ => Fronts.Chamfered(x0, y0, x1, y1, Math.Min(0.8, (x1 - x0) * 0.2)),
        };
    }

    /// <summary>Strap hinges across the frame's hinge side and a ring pull on the other stile.</summary>
    static void Hardware(FrontLook look, double w, double h, bool hingeLeft, bool handle, PathD opening, PathsD etched, List<PathD> lines)
    {
        var b = Clipper.GetBounds(opening);
        double stile = hingeLeft ? b.left : w - b.right;
        if (look == FrontLook.Metal)
        {
            // Rivets round the frame, a bar handle.
            foreach (var (x, y) in new[] { (b.left / 2, b.top / 2), (w - (w - b.right) / 2, b.top / 2), (b.left / 2, h - (h - b.bottom) / 2), (w - (w - b.right) / 2, h - (h - b.bottom) / 2) })
                lines.Add(WoodFace.Dot(x, y, 0.15));
            if (handle)
            {
                double hx = hingeLeft ? w - (w - b.right) / 2 : b.left / 2;
                etched.Add(WoodFace.RoundRect(hx - 0.22, h * 0.42, hx + 0.22, h * 0.58, 0.2));
            }
            return;
        }
        // Hinges: short straps on the hinge stile, top and bottom, with nail heads.
        double sh = Geo.Clamp(h * 0.05, 0.5, 0.8);
        if (look != FrontLook.Carved)
            foreach (var y in new[] { b.top * 0.5 + h * 0.08, h - (h - b.bottom) * 0.5 - h * 0.08 })
            {
                double sx0 = hingeLeft ? 0 : w - stile * 0.9, sx1 = hingeLeft ? stile * 0.9 : w;
                etched.Add(WoodFace.RoundRect(sx0, y - sh / 2, sx1, y + sh / 2, sh * 0.45));
            }
        if (!handle) return;
        // A ring pull hanging from a boss on the other stile, at the middle.
        double rx = hingeLeft ? w - (w - b.right) / 2 : b.left / 2, ry = h * 0.5, r = Geo.Clamp(Math.Min(w - b.right, b.left) * 0.32, 0.3, 0.55);
        lines.Add(WoodFace.Dot(rx, ry - r * 0.9, r));
        etched.Add(Geo.Circle(rx, ry, Math.Max(0.16, r * 0.4), 0.01));
    }

    /// <summary>What's carved on the sunk panel seen through an opening.</summary>
    static void Carve(FrontLook look, string theme, PathsD opening, Random rnd, PathsD etched, List<PathD> lines)
    {
        var b = Geo.Bounds(opening);
        double w = b.right - b.left, h = b.bottom - b.top, cx = (b.left + b.right) / 2;
        var inside = Geo.Offset(opening, -0.35);
        if (inside.Count == 0) return;
        var e = new PathsD();
        var l = new List<PathD>();
        switch (look)
        {
            case FrontLook.Metal:
                for (double y = b.top + h * 0.2; y < b.bottom - h * 0.2; y += 0.75) e.Add(Geo.Rect(b.left + w * 0.18, y, b.right - w * 0.18, y + 0.3));
                break;
            case FrontLook.Carved:
            {
                double r = Math.Min(w, h) * 0.4;
                if (r > 0.7) { l.Add(Geo.Loop(Fronts.Star(cx, b.top + h * 0.45, r, r * 0.55, 8))); l.Add(Geo.Loop(Fronts.Star(cx, b.top + h * 0.45, r * 0.5, r * 0.28, 8))); }
                break;
            }
            default:
            {
                // A lily over a scroll of vine, or just the lily on a small panel.
                double r = Math.Min(w * 0.42, h * 0.22);
                if (r >= 0.55) Pictures.Lily(cx, b.top + h * 0.38, r, e, l);
                if (h > w * 1.4 && w > 2.2)
                {
                    double y0 = b.top + h * 0.12, y1 = b.top + h * 0.3;
                    var vine = new PathD();
                    for (int i = 0; i <= 16; i++)
                    {
                        double t = i / 16.0;
                        vine.Add(new PointD(cx + Math.Sin(t * Math.PI * 2) * w * 0.28, y0 + (y1 - y0) * t));
                    }
                    l.Add(vine);
                    l.Add(WoodFace.Dot(cx - w * 0.2, y0 + (y1 - y0) * 0.25, Math.Min(0.35, w * 0.08)));
                    l.Add(WoodFace.Dot(cx + w * 0.2, y0 + (y1 - y0) * 0.75, Math.Min(0.35, w * 0.08)));
                }
                if (theme == TerrainThemes.Gothic && h > 4)
                    l.Add(Geo.Loop(Cabinets.Trefoil(cx, b.bottom - Math.Min(w * 0.3, 1.2) - 0.4, Math.Min(w * 0.28, 1.1))));
                break;
            }
        }
        etched.AddRange(Geo.Intersect(Geo.Union(e), inside));
        lines.AddRange(Geo.ClipLines(l, inside));
    }
}
