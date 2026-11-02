using Clipper2Lib;

namespace PhotonCanvas.Addon.Scatter;

/// <summary>
/// A two-wheeled cart: an open finger-jointed bed on two beams whose tabs go up through its bottom; an
/// axle that slides through the beams and the wheels' hubs; and shafts on the beams reaching forward
/// down to the ground (or a short leg at the front instead), so it stands level.
/// </summary>
public static class Carts
{
    public static void Make(Maker m)
    {
        var it = m.Item;
        double W = it.WidthMm, D = it.DepthMm, hb = it.HeightMm, T = m.T, f = m.Fit, sw = m.SlotW, B = Maker.Bridge;
        double R = it.WheelMm / 2;
        double minSide = 3 * T + 2;
        if (W < minSide || D < minSide) { m.Bad($"The cart's bed is too small for a {T:0.##} mm sheet: make it at least {minSide:0.#} mm each way."); return; }
        if (hb < 2 * T + 1) { m.Bad($"The cart's sides are too low for a {T:0.##} mm sheet: make them at least {2 * T + 1:0.#} mm."); return; }
        double a = Math.Clamp(R * 0.32, 2, 3.5);
        double hub = Math.Sqrt(Math.Pow((a + f) / 2, 2) + Math.Pow((T + f) / 2, 2)) + B;
        if (R < hub + 1.5) { m.Bad($"The wheels are too small for a {T:0.##} mm sheet: make them at least {2 * (hub + 1.5):0.#} mm across."); return; }

        // Heights: the axle at the wheels' middle, the beams round it, the bed on the beams.
        double ya = R - (T + f) / 2 - B - 0.3, yb = R + (T + f) / 2 + B + 0.5;
        double gap = 0.8, zc = D / 2;
        double xb = T + B + sw / 2 + 0.3;
        if (W - 2 * xb < 1) { m.Bad($"The cart is too narrow for its beams: make it at least {2 * xb + 1:0.#} mm wide."); return; }

        // The bed.
        var bed = Joints.Box([W, hb, D], T, [true, true, true, false, true, true], ["Left side", "Right side", "Bottom", "Top", "Back", "Front"]);
        double zb0 = T + B + 0.5, zb1 = D - T - B - 0.5;
        double tw = Math.Clamp((zb1 - zb0) * 0.18, 1.2, 4);
        double[] tabs = [zb0 + (zb1 - zb0) * 0.22, zb0 + (zb1 - zb0) * 0.78];
        foreach (var p in bed)
        {
            var holes = new PathsD();
            if (p.Name == "Bottom")
                foreach (var x in new[] { xb, W - xb })
                    foreach (var z in tabs) holes.Add(Joints.Centred(x, z, sw, tw + f));
            m.Add(p.Name, p.Cut, holes, [p.Pose.Moved(0, yb, 0)], grainAlongU: true, face: [Geo.Rect(0, 0, p.Lu, p.Lv)], planks: true, plankMm: 2.2);
        }

        // The beams, with the shafts (or a leg) on the front.
        double sh = Math.Clamp(T * 1.1, 1.6, 2.6), Ls = Math.Clamp(D * 0.7, 8, 24);
        var beam = new PathsD { Geo.Rect(zb0, ya, zb1, yb) };
        if (it.Shafts)
        {
            var from = new PointD(zb1 - sh, (ya + yb) / 2);
            var to = new PointD(D + Ls, sh / 2);
            beam.Add(Geo.Strip(from, to, sh));
        }
        else beam.Add(Geo.Rect(zb1 - sh * 1.2, 0, zb1, yb));
        var body = Geo.Intersect(Geo.Union(beam), [Geo.Rect(-100, 0, D + Ls + 100, yb)]);
        foreach (var z in tabs) body.Add(Geo.Rect(z - tw / 2, yb - 0.01, z + tw / 2, yb + T));
        var axleHole = new PathsD { Joints.Centred(zc, R, a + f, T + f) };
        m.Add(it.Shafts ? "Beam and shaft" : "Beam", body, axleHole, [Pose.Left(xb, 0, 0), Pose.Left(W - xb, 0, 0)], grainAlongU: true);

        // The axle, lying flat through the beams and the hubs.
        double xa0 = -gap - T - 1, xa1 = W + gap + T + 1;
        m.Add("Axle", [Geo.Rect(0, 0, xa1 - xa0, a)], [], [Pose.Up(xa0, R, zc + a / 2)], grainAlongU: true);

        // The wheels.
        var (wheel, wheelHoles, etched, lines) = Wheel(R, hub, a, T, f);
        m.Add("Wheel", wheel, wheelHoles, [Pose.Left(-gap - T / 2, 0, zc - R), Pose.Right(W + gap + T / 2, 0, zc + R)], grainAlongU: true, wood: false, etched: etched, lines: lines);

        m.Note("Push the beams' tabs up into the bed's bottom, slide the axle through the beams, then push a wheel onto each end of it. " +
               (it.Shafts ? "The cart rests level on its wheels and the shafts' ends." : "The cart rests level on its wheels and the leg at the front."));
    }

    /// <summary>A spoked wheel: an iron tyre, spokes cut out between (or etched when the wheel's too small), and a hub that fits the axle.</summary>
    static (PathsD Cut, PathsD Holes, PathsD Etched, List<PathD> Lines) Wheel(double R, double hub, double a, double T, double f)
    {
        double rim = Math.Clamp(R * 0.14, 1.0, 1.8), inner = R - rim, sp = Math.Max(Maker.Bridge * 1.15, R * 0.1);
        int spokes = R >= 9 ? 8 : 6;
        var cut = new PathsD { Geo.Circle(R, R, R) };
        var holes = new PathsD { Joints.Centred(R, R, a + f, T + f) };
        var etched = new PathsD();
        var lines = new List<PathD> { Geo.Loop(Geo.Circle(R, R, R - rim * 0.45)), Geo.Loop(Geo.Circle(R, R, hub - 0.15)) };
        var ring = Geo.Subtract([Geo.Circle(R, R, inner)], [Geo.Circle(R, R, hub)]);
        var bars = new PathsD();
        for (int i = 0; i < spokes; i++)
        {
            double ang = Math.PI * 2 * i / spokes + Math.PI / spokes;
            bars.Add(Geo.Strip(new PointD(R, R), new PointD(R + Math.Cos(ang) * R, R + Math.Sin(ang) * R), sp));
        }
        var windows = Geo.Offset(Geo.Offset(Geo.Subtract(ring, bars), -0.25, round: true), 0.25, round: true);
        // Cut the windows out if they're big enough to cut cleanly; otherwise etch them.
        double narrow = 2 * Math.PI * hub / spokes - sp;
        if (inner - hub >= 1.6 && narrow >= 1.0) holes.AddRange(windows);
        else etched.AddRange(windows);
        // Nails round the tyre.
        for (int i = 0; i < spokes * 2; i++)
        {
            double ang = Math.PI * 2 * i / (spokes * 2);
            lines.Add(WoodFace.Dot(R + Math.Cos(ang) * (R - rim * 0.72), R + Math.Sin(ang) * (R - rim * 0.72), 0.14));
        }
        return (cut, holes, etched, lines);
    }
}
