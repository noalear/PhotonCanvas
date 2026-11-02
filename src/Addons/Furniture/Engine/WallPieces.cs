using Clipper2Lib;

namespace PhotonCanvas.Addon.Furniture;

/// <summary>
/// Pieces that go on the wall maker's walls.
/// <para>
/// A door that swings: the door hangs in front of the doorway (a little bigger than it), on two
/// pins cut as part of the door that drop into two clips. Each clip is a small flat piece with a slot
/// as wide as the wall, slid onto the side of the doorway (one on the floor, one high up), with a round
/// hole in front of the wall for the pin. Lift the door and it comes off again. Works with square,
/// round-arched and pointed doorways, as the wall maker cuts them.
/// </para>
/// <para>A shelf: a plank on two brackets whose tabs plug into the wall's mount points.</para>
/// </summary>
public static class WallPieces
{
    /// <summary>Room the pins have in their holes, each side (mm).</summary>
    public const double PinPlay = 0.15;
    /// <summary>Room left round the door as it swings past the wall and the clips (mm).</summary>
    public const double Swing = 0.3;
    /// <summary>Sheet round the pin hole, and in the clip's prongs and bridge (mm).</summary>
    public const double ClipMaterial = 1.2;
    /// <summary>The gap under the door (mm).</summary>
    public const double Floor = 0.3;

    /// <summary>The numbers a door and its clips are made from, worked out from the doorway, the wall and the sheet.</summary>
    public sealed record DoorPlan(double Pin, double HoleR, double AxisZ, double Gap, double HingeOverlap, double NotchTo, double Lift, double TopClipY, double Grip, double StraightTo);

    /// <summary>A doorway as the wall maker cuts it: its bottom-left corner at x, y.</summary>
    public static PathsD Doorway(DoorShape shape, double x, double y, double w, double h)
    {
        switch (shape)
        {
            case DoorShape.Arched:
            {
                double spring = Math.Max(y, y + h - w / 2);
                var p = new PathD { new(x, y), new(x + w, y), new(x + w, spring) };
                p.AddRange(Geo.Arc(x + w / 2, spring, w / 2, 0, Math.PI).Skip(1));
                return [p];
            }
            case DoorShape.Pointed:
            {
                double rise = w * Math.Sqrt(3) / 2, spring = Math.Max(y, y + h - rise);
                var p = new PathD { new(x, y), new(x + w, y), new(x + w, spring) };
                p.AddRange(Geo.Arc(x, spring, w, 0, Math.PI / 3).Skip(1));
                p.AddRange(Geo.Arc(x + w, spring, w, Math.PI * 2 / 3, Math.PI).Skip(1));
                return [Geo.Tidy([p]).FirstOrDefault() ?? p];
            }
            default:
                return [Geo.Rect(x, y, x + w, y + h)];
        }
    }

    /// <summary>How high the doorway's sides are straight (where the clips can grip), from the bottom.</summary>
    public static double StraightTo(DoorShape shape, double w, double h) => shape switch
    {
        DoorShape.Arched => Math.Max(0, h - w / 2),
        DoorShape.Pointed => Math.Max(0, h - w * Math.Sqrt(3) / 2),
        _ => h,
    };

    public static DoorPlan Plan(FurnitureItem it, double T, double fit)
    {
        double Wt = Math.Max(0.5, it.WallMm), w = it.WidthMm, h = it.HeightMm;
        double pin = Math.Clamp(T, 1.2, 2.5);
        double r = Math.Sqrt(pin * pin + T * T) / 2 + PinPlay;
        double zf = Wt / 2 + fit / 2;
        // The pin's axis in front of the wall: far enough out for a strip of clip between the hole and the wall.
        double axisZ = Math.Max(zf + ClipMaterial + r, Wt / 2 + 0.4 + T / 2);
        double gap = axisZ - Wt / 2 - T / 2;
        // How far the door can reach over the wall on the hinge side and still swing clear of it.
        double reach = axisZ - Wt / 2 - Swing;
        double over = Math.Min(Math.Max(0, it.OverlapMm), Math.Sqrt(Math.Max(0, reach * reach - T * T / 4)));
        double notchTo = Math.Max(r + ClipMaterial, ClipMaterial) + Swing;
        double lift = T + 0.3;
        double straight = StraightTo(it.Shape, w, h);
        // The top clip as high as the straight sides go; under a square top, low enough to leave the door 2.5 mm over its notch.
        double topClip = straight - T - 0.2;
        if (it.Shape == DoorShape.Rectangle) topClip = Math.Min(topClip, h + Math.Max(0, it.OverlapMm) - T - Swing - 2.5);
        return new DoorPlan(pin, r, axisZ, gap, over, notchTo, lift, topClip, Math.Max(3, 2 * T), straight);
    }

    public static void Door(Maker m)
    {
        var it = m.Item;
        double w = it.WidthMm, h = it.HeightMm, T = m.T, f = m.Fit, Wt = Math.Max(0.5, it.WallMm), o = Math.Max(0, it.OverlapMm);
        if (w < 4 || h < 6) { m.Bad("The doorway is too small: make it at least 4 mm wide and 6 mm high."); return; }
        var p = Plan(it, T, f);
        double yt = p.TopClipY, s = p.Pin, x0 = -p.HingeOverlap;
        double lowNotchTop = T + Swing, topNotchBottom = yt - p.Lift - Swing;
        if (topNotchBottom - lowNotchTop < 2.5)
        {
            double need = h + (lowNotchTop + 2.5 - topNotchBottom);
            m.Bad(p.StraightTo < h
                ? $"The doorway's sides are too short below the arch ({p.StraightTo:0.#} mm) for the two clips: make the doorway at least {need:0.#} mm high, or narrower."
                : $"The doorway is too low for the two clips: make it at least {need:0.#} mm high.");
            return;
        }

        // The door: the doorway grown all round, cut off just above the floor and short of the wall on the hinge side.
        var outline = Geo.Intersect(Geo.Offset(Doorway(it.Shape, 0, 0, w, h), o, round: true), [Geo.Rect(x0, Floor, w + o + 1, h + o + 1)]);
        var notches = new PathsD
        {
            Geo.Rect(x0 - 1, -1, p.NotchTo, lowNotchTop),
            Geo.Rect(x0 - 1, topNotchBottom, p.NotchTo, yt + T + Swing),
        };
        var pins = new PathsD
        {
            Geo.Rect(-s / 2, 0, s / 2, lowNotchTop + 0.01),
            Geo.Rect(-s / 2, yt, s / 2, yt + T + Swing + 0.01),
        };
        var face = Geo.Subtract(outline, notches);
        var leaf = Geo.Union(face, pins);
        var (etched, lines) = DoorDetail(face, x0, w, o, p, yt, T);
        var leafPose = Pose.Front(0, 0, p.AxisZ);
        var clip = Clip(p, Wt, T, f);
        if (it.Hinge == HingeSide.Right)
        {
            PointD Mirror(PointD q) => new(w - q.x, q.y);
            leaf = Geo.Tidy(Geo.Map(leaf, Mirror));
            face = Geo.Tidy(Geo.Map(face, Mirror));
            etched = Geo.Tidy(Geo.Map(etched, Mirror));
            lines = Geo.Map(lines, Mirror);
            clip = Geo.Tidy(Geo.Map(clip, Mirror));
        }
        m.Add("Door", leaf, [], [leafPose], grainAlongU: false, face: face, etched: etched, lines: lines, planks: true, plankMm: 3.2);
        m.Add("Door clip", clip, [], [Pose.Down(0, T / 2, 0), Pose.Down(0, yt + T / 2, 0)], grainAlongU: true, face: []);
        m.Note($"Slide one clip onto the {(it.Hinge == HingeSide.Left ? "left" : "right")} side of the doorway on the floor and the other {yt:0.#} mm up (its bottom), " +
               $"round holes to the front. Lift the door, drop its pins into the holes, and it swings outwards.");
        m.Note($"The door stands {p.Gap:0.#} mm off the wall and covers the doorway by {o:0.#} mm ({p.HingeOverlap:0.#} mm on the hinge side) for a {Wt:0.#} mm wall.");
        if (o > p.HingeOverlap + 0.05) m.Note($"On the hinge side it only reaches {p.HingeOverlap:0.#} mm over the wall, so it can swing.");
    }

    /// <summary>A clip, seen from above (x along the wall, the doorway's side at x = 0; z out of the wall).</summary>
    static PathsD Clip(DoorPlan p, double Wt, double T, double fit)
    {
        double zf = Wt / 2 + fit / 2, d = p.Grip, mat = ClipMaterial;
        var body = new PathsD
        {
            Geo.Rect(-d, -zf - mat, mat, -zf),          // the prong behind the wall
            Geo.Rect(-d, zf, mat, p.AxisZ),               // the prong in front, out to the pin
            Geo.Rect(0, -zf - mat, mat, p.AxisZ),         // the bridge across the doorway's side
            Geo.Circle(0, p.AxisZ, p.HoleR + mat),
        };
        var holes = new PathsD { Geo.Rect(-d - 1, -zf, 0, zf), Geo.Circle(0, p.AxisZ, p.HoleR) };
        return Geo.Subtract(Geo.Union(body), Geo.Union(holes));
    }

    /// <summary>Braces across the planks, strap hinges by the clips and a ring handle on the latch side.</summary>
    static (PathsD Etched, List<PathD> Lines) DoorDetail(PathsD face, double x0, double w, double o, DoorPlan p, double yt, double T)
    {
        var etched = new PathsD();
        var lines = new List<PathD>();
        var b = Geo.Bounds(face);
        double top = b.bottom, g = WoodFace.GrooveMm;
        // Two braces (ledges) across the door, grooved round.
        double bh = Math.Max(1.4, (top - Floor) * 0.06);
        foreach (var y in new[] { Floor + (top - Floor) * 0.18, Floor + (top - Floor) * 0.6 })
        {
            var brace = Geo.Intersect([Geo.Rect(x0 + 0.8, y, w + o - 0.8, y + bh)], Geo.Offset(face, -0.8));
            etched.AddRange(Geo.Subtract(Geo.Offset(brace, g / 2), Geo.Offset(brace, -g / 2)));
        }
        // Strap hinges from the clips across the door.
        double len = Math.Min(w * 0.55, 12);
        foreach (var y in new[] { T + Swing + 0.6, yt + T + Swing + 0.6 })
        {
            var strap = new PathsD { WoodFace.RoundRect(p.NotchTo - 0.5, y, p.NotchTo + len, y + 1.1, 0.5) };
            etched.AddRange(Geo.Intersect(strap, face));
            for (double x = p.NotchTo + 1; x < p.NotchTo + len - 0.5; x += 3) lines.Add(WoodFace.Dot(x, y + 0.55, 0.15));
        }
        // A ring handle on the latch side.
        double hx = w - Math.Max(2, w * 0.15), hy = Math.Min(top * 0.45, 14);
        lines.Add(WoodFace.Dot(hx, hy, Math.Clamp(w * 0.06, 0.6, 1.4)));
        lines.Add(WoodFace.Dot(hx, hy + Math.Clamp(w * 0.06, 0.6, 1.4), 0.3));
        return (Geo.Union(etched), lines);
    }

    /// <summary>A shelf on two brackets whose tabs plug into the wall's mount points.</summary>
    public static void Shelf(Maker m)
    {
        var it = m.Item;
        double L = it.WidthMm, Dp = it.DepthMm, T = m.T, Wt = Math.Max(0.5, it.WallMm), slotH = Math.Clamp(it.MountSlotMm, 2, 30);
        double size = Math.Clamp(Math.Min(it.HeightMm, Dp), 2, 40);
        double slotW = it.WallSheetMm + 0.15;
        if (L < 4 || Dp < 2) { m.Bad("The shelf is too small: make it at least 4 mm long and 2 mm deep."); return; }
        if (T > slotW + 0.01) { m.Bad($"A {T:0.##} mm sheet is too thick for the wall's {slotW:0.##} mm mount slots: cut the brackets from {it.WallSheetMm:0.##} mm sheet or thinner."); return; }
        if (T < slotW - 0.5) m.Warn($"The brackets ({T:0.##} mm) are thinner than the wall's mount slots ({slotW:0.##} mm): glue them in so they don't lean.");

        // The bracket, side-on: its tab through the wall (u less than 0), the triangle standing out from it.
        double tabH = slotH - 0.2, tabL = Wt - 0.2;
        var bracket = Geo.Union(new PathsD
        {
            Geo.Rect(-tabL, 0.1, 0.01, 0.1 + tabH),
            new PathD { new(0, slotH + 1), new(0, slotH + 1 - size), new(size, slotH + 1) },
            Geo.Rect(0, Math.Min(0, slotH + 1 - size), 1.2, slotH + 1),
        });
        double top = slotH + 1;
        double[] at = [L * 0.2, L * 0.8];
        m.Add("Shelf bracket", bracket, [], at.Select(x => Pose.Left(x, 0, 0)), grainAlongU: true, face: []);
        m.Add("Shelf", [Geo.Rect(0, 0, L, Dp)], [], [Pose.Up(0, top + T / 2, Dp)], grainAlongU: true);
        m.Note($"Plug the brackets into two mount points about {at[1] - at[0]:0} mm apart and glue the shelf on top; the shelf sits {top:0.#} mm above the bottom of the slots.");
    }
}
