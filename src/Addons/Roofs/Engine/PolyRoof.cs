using Clipper2Lib;
using PhotonCanvas.Addon.Walls;

namespace PhotonCanvas.Addon.Roofs;

/// <summary>
/// A pointed roof over a regular many-sided building (a hexagon, an octagon, a round tower of flat
/// walls): a slope over every wall, rising to a point. Like the roof maker's other roofs it lifts off: a
/// base plate as big as the outside of what it sits on, a locator under it inside the walls, and on the
/// base two boards standing crossed (slotted together halfway) that hold the slopes up. The slopes are
/// triangles with their shingles etched on, glued to the boards and to each other; a cap lies over each hip.
/// </summary>
/// <remarks>
/// The model's middle is the roof's point: X and Y across (the plan), Z up from the top of what it sits
/// on, as in <see cref="RoofMaker"/>. The base plate is Z 0 to T.
/// </remarks>
public static class PolyRoof
{
    /// <summary>
    /// A pointed roof on a regular polygon with <paramref name="sides"/> sides (an even number), its base
    /// <paramref name="apothem"/> from the middle to each side, the first side facing <paramref name="firstFacingDeg"/>
    /// (degrees anticlockwise from X). The shingles, slope, eaves, sheet, fit and caps come from <paramref name="s"/>.
    /// <paramref name="locator"/> is the frame under the base (on the plan, empty for none), <paramref name="locatorLayers"/> thick.
    /// </summary>
    public static RoofKit Pointed(RoofSpec s, int sides, double apothem, double firstFacingDeg, PathsD locator, int locatorLayers)
    {
        var findings = new List<Finding>();
        var parts = new List<RoofPart>();
        double T = Math.Max(0.3, s.ThicknessMm), A = apothem, fit = Math.Max(0, s.FitMm), z0 = T;
        int n = sides;
        if (n < 4 || n % 2 != 0) { findings.Add(new(Severity.Bad, "A pointed roof needs an even number of sides.")); return new(parts, findings, T); }
        if (A < 15) { findings.Add(new(Severity.Bad, "The roof is too small: make it at least 30 mm across.")); return new(parts, findings, T); }
        double pitch = Math.Clamp(s.PitchDeg, 15, 75) * Math.PI / 180, t = Math.Tan(pitch), c = Math.Cos(pitch), sn = Math.Sin(pitch);
        double e = Math.Max(0, s.EaveMm), half = Math.PI / n, step = 2 * Math.PI / n, a0 = firstFacingDeg * Math.PI / 180;
        V3 Normal(int j) => new(Math.Cos(a0 + j * step), Math.Sin(a0 + j * step), 0);
        var up = V3.UnitZ;

        // The slopes: one triangle over each side, its inside face on the line from the base's outer edge (at its top) up to the point.
        double hw = (A + e * c) * Math.Tan(half), S = A / c + e;
        PathD tri = [new(0, 0), new(2 * hw, 0), new(hw, S)];
        var slopePoses = new List<Pose>();
        for (int j = 0; j < n; j++)
        {
            var nj = Normal(j);
            var u = up.Cross(nj);
            var v = nj * -c + up * sn;
            var eave = nj * (A + e * c) + up * (z0 - e * sn);
            slopePoses.Add(new Pose(eave - u * hw, u, v));
        }
        var slope = RoofMaker.Slope(s, "Slope", [tri], slopePoses[0]);
        parts.Add(slope with { Poses = slopePoses });

        // Two boards standing crossed through the middle, under the slopes' fall lines: their tops follow the
        // slopes' inside faces, flattened round the point so they stay under the slopes either side.
        double flat = T / 2 / Math.Tan(half) + 0.2;
        double reach = A - Math.Max(RoofMaker.BridgeMm + T, 3 / t);
        double top = (A - flat) * t;
        if (reach < flat + 8 || top < 6) { findings.Add(new(Severity.Bad, "The roof is too small or too flat for the boards that hold it up: make it bigger or steeper.")); return new(parts, findings, T); }
        int m = Math.Max(1, (int)Math.Round(n / 4.0));
        // Boards crossing at an angle other than square need a wider slot: the other board runs across this one's thickness at a slant.
        double cross = Math.Max(0.3, Math.Abs(Math.Sin(m * step)));
        double slotW = T * (1 + Math.Abs(Math.Cos(m * step))) / cross + fit;
        PathD Profile() => [new(-reach, 0), new(reach, 0), new(reach, (A - reach) * t), new(flat, top), new(-flat, top), new(-reach, (A - reach) * t)];
        double tabW = Math.Clamp(reach * 0.25, 5, 12);
        var tabs = new PathsD { Geo.Rect(-reach * 0.6 - tabW / 2, -T, -reach * 0.6 + tabW / 2, 0.01), Geo.Rect(reach * 0.6 - tabW / 2, -T, reach * 0.6 + tabW / 2, 0.01) };
        var slots = new PathsD();
        foreach (var (name, j, fromTop) in new[] { ("Cross board", 0, true), ("Cross board (slotted below)", m, false) })
        {
            var nj = Normal(j);
            var u = new V3(nj.X, nj.Y, 0);
            var pose = new Pose(new V3(0, 0, z0) - u.Cross(up) * (T / 2), u, up);
            var halving = fromTop ? Geo.Rect(-slotW / 2, top / 2, slotW / 2, top + 1) : Geo.Rect(-slotW / 2, -T - 1, slotW / 2, top / 2);
            var cut = Geo.Subtract(Geo.Union([Profile()], tabs), [halving]);
            if (Geo.Pieces(cut) != 1) { findings.Add(new(Severity.Bad, "The roof is too small for its crossed boards: make it bigger or steeper.")); return new(parts, findings, T); }
            parts.Add(new RoofPart(name, cut, [], [], 1, [pose]));
            // The base's slots for its tabs.
            foreach (var tab in tabs)
            {
                var b = Clipper.GetBounds(tab);
                PathD foot = [Plan(pose.At(b.left, 0, 0)), Plan(pose.At(b.right, 0, 0)), Plan(pose.At(b.right, 0, T)), Plan(pose.At(b.left, 0, T))];
                slots.AddRange(Geo.Offset([foot], fit / 2));
            }
        }

        // The base and the locator under it.
        var baseOutline = Connectors.Regular(n, A, firstFacingDeg);
        parts.Insert(0, new RoofPart("Base", Geo.Subtract([baseOutline], Geo.Union(slots)), [], [], 1, [Pose.Flat(new V3(0, 0, 0))]));
        if (locator.Count > 0 && locatorLayers > 0)
            parts.Insert(1, new RoofPart("Locator", Geo.Tidy(locator), [], [], locatorLayers, [.. Enumerable.Range(0, locatorLayers).Select(i => Pose.Flat(new V3(0, 0, -(i + 1) * T)))]));

        // A cap piece on each slope along its two hips: every other slope's runs on over its neighbours'
        // edges, so the joints close up tight and the caps meet at the point.
        if (s.Caps)
        {
            var na = Normal(0) * sn + up * c;
            var nb = Normal(1) * sn + up * c;
            double phi = Math.Acos(Math.Clamp(na.Dot(nb), -1, 1));
            var over = slopePoses.Where((_, j) => j % 2 == 0).ToList();
            var under = slopePoses.Where((_, j) => j % 2 == 1).ToList();
            // At the point every cap stops where it meets the one across from it (an edge with no length there).
            PathD pointed = [tri[0], tri[1], tri[2], tri[2]];
            double across = 2 * Math.Atan(t);
            // Round the point, each cap keeps to its own half of the slopes either side (the slopes' middles
            // spread out flat about its hips), so the caps that run on over the slopes between never meet.
            double beta = Math.Atan2(hw, S), spread = 2 * beta * 0.9;
            var apex = tri[2];
            PathsD wedge = [[apex, new(apex.x + 1e3 * Math.Sin(spread), apex.y - 1e3 * Math.Cos(spread)), new(apex.x - 1e3 * Math.Sin(spread), apex.y - 1e3 * Math.Cos(spread))]];
            RoofPart Kept(RoofPart cap) => cap with { Cut = Geo.Tidy(Geo.Intersect(cap.Cut, wedge)), Etched = Geo.Intersect(cap.Etched, wedge), Lines = Geo.ClipLines(cap.Lines, wedge) };
            parts.Add(Kept(RoofMaker.Caps(s, "Cap (over)", pointed, [new RoofMaker.Seam(1, phi, true), new RoofMaker.Seam(2, across, false), new RoofMaker.Seam(3, phi, true)], T, over)));
            if (under.Count > 0) parts.Add(Kept(RoofMaker.Caps(s, "Cap (under)", pointed, [new RoofMaker.Seam(1, phi, false), new RoofMaker.Seam(2, across, false), new RoofMaker.Seam(3, phi, false)], T, under)));
            findings.Add(new(Severity.Note, "Glue a bead or a cocktail stick on top as a finial, where the caps meet at the point."));
        }
        findings.Add(new(Severity.Note, $"The point is {A * t + T:0.#} mm above the base ({A * t + 2 * T:0.#} mm above what it sits on)."));
        findings.Add(new(Severity.Note, "Slot the two cross boards together halfway and stand them in the base's slots; glue the slopes to them and to each other along the hips, then the caps on top: the ones marked under first, then the others over them. The whole roof lifts off."));
        return RoofMaker.Finish(parts, findings, T, s.Roofs);
    }

    static PointD Plan(V3 p) => new(p.X, p.Y);
}
