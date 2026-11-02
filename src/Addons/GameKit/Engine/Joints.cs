using Clipper2Lib;

namespace PhotonCanvas.Addon.GameKit;

/// <summary>Finger-jointed boxes: the furniture maker's (Furniture/Engine/Storage.cs), the same joints.</summary>
public static class Joints
{
    /// <summary>One face of a box: its outline (flat), where it sits, and its size along u and v.</summary>
    public sealed record Panel(string Name, PathsD Cut, Pose Pose, double Lu, double Lv);

    /// <summary>
    /// The panels of a box <paramref name="size"/> (x, y, z) outside, with finger joints along every
    /// edge where two panels meet. Each edge's fingers alternate between its two panels; corners go to
    /// the front and back, then the ends, then the bottom and top. <paramref name="has"/> = left, right,
    /// bottom, top, back, front.
    /// </summary>
    public static List<Panel> Box(double[] size, double T, bool[] has)
    {
        static int Rank(int axis) => axis == 2 ? 3 : axis == 0 ? 2 : 1;
        bool Has(int axis, int side) => has[axis * 2 + side];
        int Segments(double len)
        {
            double ft = Math.Max(1.5 * T, 2);
            int n = (int)Math.Round(len / ft);
            if (n % 2 == 0) n = Math.Max(1, n - 1);
            // At least three, so every edge interlocks, while the fingers stay wide enough not to break.
            if (n < 3 && len >= 3 * 1.0 * 1.2) n = 3;
            return Math.Max(1, n);
        }
        string[] names = ["Left end", "Right end", "Bottom", "Lid", "Back", "Front"];
        var panels = new List<Panel>();
        for (int a = 0; a < 3; a++)
            for (int s = 0; s < 2; s++)
            {
                if (!Has(a, s)) continue;
                // The face's two axes (b along u, c along v) and how the flat coordinates map from the room's.
                int b, c;
                Pose pose;
                double W = size[0], H = size[1], D = size[2];
                switch (a)
                {
                    case 2:
                        b = 0; c = 1;
                        pose = s == 1 ? Pose.Front(0, 0, D - T / 2) : Pose.Back(W, 0, T / 2);
                        break;
                    case 0:
                        b = 2; c = 1;
                        pose = s == 0 ? Pose.Left(T / 2, 0, 0) : Pose.Right(W - T / 2, 0, D);
                        break;
                    default:
                        b = 0; c = 2;
                        pose = s == 0 ? Pose.Down(0, T / 2, 0) : Pose.Up(0, H - T / 2, D);
                        break;
                }
                var rects = new PathsD();
                (double, double)[] Zones(double L) => [(0, T), (T, L - T), (L - T, L)];
                var zb = Zones(size[b]);
                var zc = Zones(size[c]);
                for (int ib = 0; ib < 3; ib++)
                    for (int ic = 0; ic < 3; ic++)
                    {
                        var (b0, b1) = zb[ib];
                        var (c0, c1) = zc[ic];
                        if (b1 - b0 <= 1e-9 || c1 - c0 <= 1e-9) continue;
                        bool endB = ib != 1, endC = ic != 1;
                        if (!endB && !endC) { rects.Add(Geo.Rect(b0, c0, b1, c1)); continue; }
                        if (endB && endC)
                        {
                            int owner = a;
                            if (Has(b, ib == 0 ? 0 : 1) && Rank(b) > Rank(owner)) owner = b;
                            if (Has(c, ic == 0 ? 0 : 1) && Rank(c) > Rank(owner)) owner = c;
                            if (owner == a) rects.Add(Geo.Rect(b0, c0, b1, c1));
                            continue;
                        }
                        // An edge: shared with the face across it, if there is one, in alternating fingers.
                        int other = endB ? b : c, otherSide = endB ? (ib == 0 ? 0 : 1) : (ic == 0 ? 0 : 1);
                        if (!Has(other, otherSide)) { rects.Add(Geo.Rect(b0, c0, b1, c1)); continue; }
                        bool even = Rank(a) > Rank(other);
                        double from = endB ? c0 : b0, to = endB ? c1 : b1;
                        int nseg = Segments(to - from);
                        for (int k = 0; k < nseg; k++)
                        {
                            if ((k % 2 == 0) != even) continue;
                            double s0 = from + (to - from) * k / nseg, s1 = from + (to - from) * (k + 1) / nseg;
                            rects.Add(endB ? Geo.Rect(b0, s0, b1, s1) : Geo.Rect(s0, c0, s1, c1));
                        }
                    }
                // Room coordinates → the panel's flat ones.
                double Coord(V3 p, int axis) => axis == 0 ? p.X : axis == 1 ? p.Y : p.Z;
                V3 Room(double gb, double gc)
                {
                    double[] g = [0, 0, 0];
                    g[b] = gb; g[c] = gc; g[a] = Coord(pose.O, a);
                    return new V3(g[0], g[1], g[2]);
                }
                var flat = Geo.Map(Geo.Union(rects), p => { var l = pose.Local(Room(p.x, p.y)); return new PointD(l.U, l.V); });
                panels.Add(new Panel(names[a * 2 + s], Geo.Tidy(flat), pose, size[b], size[c]));
            }
        return panels;
    }

}
