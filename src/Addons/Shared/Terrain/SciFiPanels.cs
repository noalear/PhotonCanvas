using Clipper2Lib;

namespace PhotonCanvas.Addon.Terrain;

/// <summary>
/// The sci-fi panels texture: a hard-surface corridor wall. Heavy pillars stand a bay apart, each
/// with a wider block at its foot and its head, a long slot or a slatted grille up its shaft and a
/// small light. Between them every bay reads in bands: a kick block with a vent; a skirt plate with
/// a big slatted vent between small ones; then a deep service recess, half covered by a big armour
/// plate with a stepped, chamfered top edge and bolts along it, and in the open part above it the
/// services: pipes that run along and bend down behind the plate, clamped, a cable bundle, a control
/// box with a grille and buttons, or a junction box the cables run into; and at the top a cornice,
/// a bar with angled plates under it and small grilles in their corners. Two bay designs take turns,
/// mirrored, with a hatch or a console now and then. Big calm plates, detail grouped where the
/// services are. Carved as a relief, what stands in the recess sits part way down (the relief maker
/// sets anything standing in a wide recess lower), so the layers read. Panel width is the bay,
/// panel height sets the bands; the detail size sets bolts, slats, pipes and cables.
/// </summary>
internal sealed class SciFi(TextureEngine.Texture t)
{
    readonly Random _rnd = t.Rnd;
    double _g, _d;

    bool LinesOnly => t.Wear.LinesOnly;

    enum Bay { Pipes, Cables, Hatch, Console }

    T Pick<T>(params T[] options) => options[_rnd.Next(options.Length)];

    public void Make(double len, double h, double a, double b, double g)
    {
        _g = Math.Max(0.25, g);
        _d = Math.Max(0.3, t.D);
        // Panel width is about half a bay's open width (the armour plate's half): the bay is two of them and a pillar.
        double pillar = Math.Clamp(a * 0.32, 2.6, 7);
        double bay = Math.Max(10, a * 1.9 + pillar);
        double kick = Math.Clamp(b * 0.32, 2.2, 5.5);
        double skirt = Math.Clamp(b * 0.55, 3, 9);
        double cornice = h > 24 ? Math.Clamp(b * 0.6, 3.2, 9) : 0;
        double cap = Math.Clamp(b * 0.4, 2.4, 6);

        double start = -bay * _rnd.NextDouble();
        var designA = Pick(Bay.Pipes, Bay.Cables);
        var designB = designA == Bay.Pipes ? Bay.Cables : Bay.Pipes;
        bool lastSpecial = true;
        int i = 0;
        for (double x = start; x < len + bay; x += bay, i++)
        {
            double bx0 = x + pillar / 2 + _g, bx1 = x + bay - pillar / 2 - _g;
            bool flip = i % 2 == 1;
            var design = i % 2 == 0 ? designA : designB;
            if (!lastSpecial && _rnd.NextDouble() < 0.18) { design = Pick(Bay.Hatch, Bay.Console); lastSpecial = true; }
            else lastSpecial = false;

            Kick(bx0, bx1, kick);
            Skirt(bx0, kick + _g / 2, bx1, kick + skirt - _g / 2);
            double m0 = kick + skirt + _g / 2, m1 = h - cornice - _g / 2;
            Main(design, bx0, m0, bx1, m1, flip);
            if (cornice > 0) Cornice(bx0, h - cornice + _g / 2, bx1, h + 1);
        }
        int k = 0;
        for (double x = start; x < len + 2 * bay; x += bay, k++) Pillar(x, pillar, cap, h, major: k % 2 == 0);
    }

    // ---- small parts ----

    double BoltR => Math.Clamp(_d * 0.36, 0.2, 0.55);

    void Hole(PathD p)
    {
        if (LinesOnly) t.Score(Poly.Loop(p));
        else t.Etch(p);
    }

    void Bolt(double x, double y) => Hole(Poly.Circle(x, y, BoltR));

    /// <summary>A plate, crisp as machined (now and then dented on a well-worn wall), scuffed as the wear says.</summary>
    void Plate(PathD p)
    {
        if (t.Wear.Amount > 0.6 && t.Weather.Random.NextDouble() < (t.Wear.Amount - 0.6) * 1.5) t.Raised.AddRange(t.Weather.Piece(p, Stuff.Metal, t.Etched, t.Lines));
        else t.Clean(p);
        if (t.Weather.Random.NextDouble() < 0.2 + t.Wear.Amount * 0.5) t.Weather.Surface([p], Stuff.Metal, t.Etched, t.Lines);
    }

    /// <summary>A groove following a plate's outline, <paramref name="inset"/> in from its edge: an inner panel line.</summary>
    void Ring(PathD plate, double inset)
    {
        double gr = Math.Max(0.3, _g * 0.7);
        var outer = Poly.Offset([plate], -inset);
        var inner = Poly.Offset([plate], -inset - gr);
        if (outer.Count == 0 || inner.Count == 0) return;
        if (LinesOnly) { foreach (var o in outer) t.Score(Poly.Loop(o)); return; }
        t.Etched.AddRange(Poly.Subtract(outer, inner));
    }

    /// <summary>Takes everything raised so far out of an area (with a groove's gap round it), so something can be drawn over it.</summary>
    void ClearFor(PathD area)
    {
        var kept = Poly.Subtract([.. t.Raised], Poly.Offset([area], _g));
        t.Raised.Clear();
        t.Raised.AddRange(kept);
    }

    /// <summary>Slats across a recessed slot: the slot etched, raised bars in it.</summary>
    void Grille(double x0, double y0, double x1, double y1, bool upright = false)
    {
        if (x1 - x0 < 1 || y1 - y0 < 1) return;
        double r = Math.Min(x1 - x0, y1 - y0) * 0.2;
        var slot = Poly.Rounded(x0, y0, x1, y1, r);
        if (LinesOnly) { t.Score(Poly.Loop(slot)); }
        // The slot is left open (no raised piece there), so the recess shows; slats stand in it.
        else ClearFor(slot);
        double pitch = Math.Max(0.8, _d * 1.4), slat = pitch * 0.5, inset = Math.Max(0.4, _g);
        if (upright)
            for (double x = x0 + inset + (x1 - x0 - 2 * inset - slat) % pitch / 2; x + slat <= x1 - inset; x += pitch) Slat(Poly.Rect(x, y0 + inset, x + slat, y1 - inset));
        else
            for (double y = y0 + inset + (y1 - y0 - 2 * inset - slat) % pitch / 2; y + slat <= y1 - inset; y += pitch) Slat(Poly.Rect(x0 + inset, y, x1 - inset, y + slat));
    }

    void Slat(PathD p)
    {
        if (LinesOnly) t.Score(Poly.Loop(p));
        else t.Clean(p);
    }

    /// <summary>A row of small upright slots, centred on <paramref name="cx"/>.</summary>
    void Slots(double cx, double y0, double y1, int n)
    {
        double pitch = Math.Max(0.9, _d * 1.5), w = pitch * 0.45;
        double x = cx - ((n - 1) * pitch + w) / 2;
        for (int k = 0; k < n; k++, x += pitch) Hole(Poly.Rounded(x, y0, x + w, y1, w * 0.45));
    }

    // ---- the pillars ----

    /// <summary>
    /// A pillar: a shaft the whole height, a wider block at its foot and its head with their inner
    /// corners cut, bolts where they meet, and up the shaft a long slot (or on every other one a
    /// slatted grille) and a small square light under it.
    /// </summary>
    void Pillar(double cx, double w, double cap, double h, bool major)
    {
        double x0 = cx - w / 2, x1 = cx + w / 2, wide = w * 0.18;
        var foot = Poly.Cut(x0 - wide, 0, x1 + wide, cap, wide * 1.2, false, false, true, true);
        var head = Poly.Cut(x0 - wide, h - cap, x1 + wide, h + 1, wide * 1.2, true, true, false, false);
        foreach (var p in new[] { foot, head }) ClearFor(p);
        t.Clean(foot);
        t.Clean(head);
        var shaft = Poly.Rect(x0, cap + _g, x1, h - cap - _g);
        ClearFor(shaft);
        t.Clean(shaft);
        double s0 = cap + _g, s1 = h - cap - _g, sh = s1 - s0;
        if (sh < 4) return;
        // Bolts just above the foot and below the head.
        double bi = Math.Max(BoltR * 2.4, w * 0.22);
        Bolt(x0 + bi, s0 + bi); Bolt(x1 - bi, s0 + bi);
        Bolt(x0 + bi, s1 - bi); Bolt(x1 - bi, s1 - bi);
        double sw = Math.Max(0.8, w * 0.34);
        if (major && w > _d * 3)
            Grille(cx - sw * 0.75, s0 + sh * 0.4, cx + sw * 0.75, s0 + sh * 0.85);
        else
            Hole(Poly.Rounded(cx - sw / 2, s0 + sh * 0.42, cx + sw / 2, s0 + sh * 0.8, sw / 2));
        double ls = Math.Max(0.8, w * 0.3);
        Hole(Poly.Rect(cx - ls / 2, s0 + sh * 0.16, cx + ls / 2, s0 + sh * 0.16 + ls));
        // Two short stripes on the minor pillars, a maker's mark.
        if (!major && !LinesOnly && w > _d * 4)
            for (int k = 0; k < 2; k++)
            {
                double y = s0 + sh * 0.3 + k * ls * 0.9;
                t.Etch([new(cx - sw * 0.6, y), new(cx + sw * 0.6, y + ls * 0.35), new(cx + sw * 0.6, y + ls * 0.6), new(cx - sw * 0.6, y + ls * 0.25)]);
            }
    }

    // ---- the bands ----

    void Kick(double x0, double x1, double kick)
    {
        double y0 = _g / 2, y1 = kick - _g / 2, w = x1 - x0;
        if (w < 2 || y1 - y0 < 1) return;
        Plate(Poly.Cut(x0, y0, x1, y1, Math.Min((y1 - y0) * 0.4, w * 0.08), false, false, true, true));
        double inset = Math.Max(0.5, (y1 - y0) * 0.25);
        Slots(x0 + w * 0.5, y0 + inset, y1 - inset, Math.Max(3, (int)(w * 0.3 / Math.Max(0.9, _d * 1.5))));
    }

    /// <summary>The skirt: a plate with its top corners cut, a big slatted vent in the middle and three slots each side.</summary>
    void Skirt(double x0, double y0, double x1, double y1)
    {
        double w = x1 - x0, hgt = y1 - y0;
        if (w < 4 || hgt < 2) return;
        Plate(Poly.Cut(x0, y0, x1, y1, Math.Min(hgt * 0.35, w * 0.1), false, false, true, true));
        double inset = Math.Max(0.6, hgt * 0.2);
        Grille(x0 + w * 0.28, y0 + inset, x1 - w * 0.28, y1 - inset);
        if (w > _d * 18)
        {
            Slots(x0 + w * 0.14, y0 + inset * 1.3, y1 - inset * 1.3, 3);
            Slots(x1 - w * 0.14, y0 + inset * 1.3, y1 - inset * 1.3, 3);
        }
    }

    /// <summary>The cornice: a bar along the top, angled plates under it sloping down from the pillars, small grilles in the corners.</summary>
    void Cornice(double x0, double y0, double x1, double y1)
    {
        double w = x1 - x0, hgt = y1 - 1 - y0;
        if (w < 4 || hgt < 2) return;
        double bar = hgt * 0.32;
        Plate(Poly.Rect(x0, y1 - 1 - bar, x1, y1));
        double py1 = y1 - 1 - bar - _g, slope = Math.Min(w * 0.18, (py1 - y0) * 1.2);
        // The angled plate: full width under the bar, its lower corners cut steeply back.
        PathD angled = [new(x0, py1), new(x0, py1 - (py1 - y0) * 0.35), new(x0 + slope, y0), new(x1 - slope, y0), new(x1, py1 - (py1 - y0) * 0.35), new(x1, py1)];
        Plate(angled);
        // Small grilles near its ends.
        double gw = Math.Min(w * 0.16, _d * 9), gh = (py1 - y0) * 0.38, gy = py1 - (py1 - y0) * 0.25 - gh;
        if (gh > 0.9)
        {
            Grille(x0 + slope * 0.9, gy, x0 + slope * 0.9 + gw, gy + gh, upright: true);
            Grille(x1 - slope * 0.9 - gw, gy, x1 - slope * 0.9, gy + gh, upright: true);
        }
        Bolt(x0 + w * 0.5 - bar, y1 - 1 - bar / 2);
        Bolt(x0 + w * 0.5 + bar, y1 - 1 - bar / 2);
    }

    // ---- the main field: a recess, an armour plate, the services ----

    void Main(Bay design, double x0, double y0, double x1, double y1, bool flip)
    {
        double w = x1 - x0, hgt = y1 - y0;
        if (w < 4 || hgt < 4) return;
        // The recess itself is the space left open (nothing raised): it's etched as the deepest part.
        // A thin raised lip round it, so it reads as a cut-out in the wall.
        double lip = Math.Max(0.6, _d * 1.1);
        t.Clean(Poly.Rect(x0, y1 - lip, x1, y1));
        t.Clean(Poly.Rect(x0, y0, x0 + lip, y1 - lip - _g));
        t.Clean(Poly.Rect(x1 - lip, y0, x1, y1 - lip - _g));
        double ix0 = x0 + lip + _g, ix1 = x1 - lip - _g, iy1 = y1 - lip - _g;
        switch (design)
        {
            case Bay.Hatch: Hatch(ix0, y0, ix1, iy1, flip); return;
            case Bay.Console: Console(ix0, y0, ix1, iy1, flip); return;
        }
        // The armour plate over the lower part, its top edge stepped: low on one side, high on the other.
        double lowTop = y0 + hgt * 0.52, highTop = y0 + hgt * 0.68, step = Math.Min(highTop - lowTop, w * 0.2);
        double sx = flip ? ix0 + (ix1 - ix0) * 0.62 : ix0 + (ix1 - ix0) * 0.38;
        double cc = Math.Min(w * 0.06, _d * 2.4);
        PathD plate = flip
            ? [new(ix0, y0), new(ix1, y0), new(ix1, lowTop - cc), new(ix1 - cc, lowTop), new(sx + step, lowTop), new(sx, highTop), new(ix0 + cc, highTop), new(ix0, highTop - cc)]
            : [new(ix0, y0), new(ix1, y0), new(ix1, highTop - cc), new(ix1 - cc, highTop), new(sx + step, highTop), new(sx, lowTop), new(ix0 + cc, lowTop), new(ix0, lowTop - cc)];
        // A seam across it with angled ends: the lower band is a second plate.
        double seamY = y0 + (lowTop - y0) * 0.42;
        PathD below = [new(ix0 - 1, y0 - 1), new(ix1 + 1, y0 - 1), new(ix1 + 1, seamY - cc), new(ix1 - cc * 1.5, seamY), new(ix0 + cc * 1.5, seamY), new(ix0 - 1, seamY - cc)];
        foreach (var p in Poly.Offset(Poly.Intersect([plate], [below]), -_g / 2)) Plate(p);
        foreach (var p in Poly.Offset(Poly.Subtract([plate], [below]), -_g / 2))
        {
            Plate(p);
            Ring(p, Math.Max(1.2, _d * 2.4));
        }
        // Bolts along the top edge and down the sides.
        double bi = Math.Max(BoltR * 2.5, _d * 1.1), pitch = Math.Max(bi * 3, _d * 5);
        double topL = flip ? highTop : lowTop, topR = flip ? lowTop : highTop;
        for (double bx = ix0 + bi + cc; bx < ix1 - bi - cc; bx += pitch)
        {
            double top = bx < sx ? topL : bx > sx + step ? topR : double.NaN;
            if (!double.IsNaN(top)) Bolt(bx, top - bi);
        }
        Bolt(ix0 + bi, seamY + bi); Bolt(ix1 - bi, seamY + bi);
        Bolt(ix0 + bi, y0 + bi); Bolt(ix1 - bi, y0 + bi);

        // The services, in the open recess above the plate.
        if (design == Bay.Pipes) PipesAndBox(ix0, ix1, sx, step, topL, topR, iy1, flip);
        else CablesAndJunction(ix0, ix1, Math.Max(topL, topR), iy1, flip);
    }

    /// <summary>Two pipes along the recess that bend down behind the plate's low side, clamped; a control box in the other corner.</summary>
    void PipesAndBox(double x0, double x1, double sx, double step, double topL, double topR, double y1, bool flip)
    {
        double low = Math.Min(topL, topR), high = Math.Max(topL, topR), space = y1 - high;
        if (space < 2) return;
        double pw = Math.Clamp(space * 0.16, 0.7, _d * 2.2), gap = Math.Max(_g, pw * 0.35);
        // The control box, against the high side of the plate.
        double bw = Math.Min((x1 - x0) * 0.34, space * 1.4), bh = Math.Min(space - _g * 2, bw * 0.75);
        double bx0 = flip ? x0 + _g : x1 - _g - bw, bx1 = bx0 + bw, by0 = high + _g, by1 = by0 + bh;
        // Pipes: run along at the top of the recess from the box to the far end, then turn down to the low plate.
        double py = Math.Min(y1 - gap - pw * 2 - gap, by1 - pw * 2 - gap);
        double turn = flip ? x1 - (x1 - x0) * 0.18 : x0 + (x1 - x0) * 0.18;
        for (int k = 0; k < 2; k++)
        {
            double yA = py + k * (pw + gap), off = k * (pw + gap);
            double tx = flip ? turn - off : turn + off;
            double from = flip ? bx1 + gap : bx0 - gap;
            var line = new PathD { new(from, yA + pw / 2), new(tx, yA + pw / 2), new(tx, low + _g) };
            Tube(line, pw);
        }
        // Clamps across the pair.
        double cw = Math.Max(0.7, pw * 0.9);
        foreach (double f in new[] { 0.35, 0.7 })
        {
            double cx = flip ? turn - (turn - bx1) * f : turn + (bx0 - turn) * f;
            var clamp = Poly.Rect(cx - cw / 2, py - gap * 0.8, cx + cw / 2, py + 2 * pw + gap * 1.8);
            ClearFor(clamp);
            t.Clean(clamp);
        }
        // The box: a chamfered plate with a slatted grille and a column of buttons.
        var box = Poly.Chamfered(bx0, by0, bx1, by1, Math.Min(bw, bh) * 0.12);
        ClearFor(box);
        Plate(box);
        double inset = Math.Max(0.5, Math.Min(bw, bh) * 0.12);
        Grille(bx0 + inset, by0 + inset, bx0 + bw * 0.6, by1 - inset);
        double kb = Math.Max(0.6, Math.Min(bw * 0.16, (bh - 2 * inset) / 3.8));
        for (int k = 0; k < 3; k++)
        {
            double ky = by1 - inset - kb - k * kb * 1.35;
            if (ky < by0 + inset) break;
            Hole(Poly.Rect(bx1 - inset - kb, ky, bx1 - inset, ky + kb));
        }
        Bolt(bx0 + inset * 0.6, by1 - inset * 0.6); Bolt(bx1 - inset * 0.6, by0 + inset * 0.6);
    }

    /// <summary>A bundle of three cables waving along the recess into a junction box, clamped.</summary>
    void CablesAndJunction(double x0, double x1, double top, double y1, bool flip)
    {
        double space = y1 - top;
        if (space < 2) return;
        double bw = Math.Min((x1 - x0) * 0.2, space * 0.9), bh = Math.Min(space - _g * 2, bw * 0.8);
        double jx0 = flip ? x1 - _g - bw - (x1 - x0) * 0.12 : x0 + _g + (x1 - x0) * 0.12, jx1 = jx0 + bw;
        double jy1 = y1 - _g, jy0 = jy1 - bh;
        double cw = Math.Clamp(space * 0.1, 0.5, _d * 1.4), gap = Math.Max(_g * 0.8, cw * 0.4);
        double cy = jy0 + bh * 0.5;
        // From the box's side to the far end of the recess, gently waving.
        double from = flip ? jx0 - gap : jx1 + gap, to = flip ? x0 - 1 : x1 + 1;
        for (int k = -1; k <= 1; k++)
        {
            var line = new PathD();
            double amp = Math.Min(space * 0.08, cw * 1.2), wave = Math.Max(4, (x1 - x0) * 0.35);
            int n = 24;
            for (int s = 0; s <= n; s++)
            {
                double u = (double)s / n, x = from + (to - from) * u;
                line.Add(new(x, cy + k * (cw + gap) + amp * Math.Sin(u * (x1 - x0) / wave * Math.PI * 2) * u));
            }
            Tube(line, cw);
        }
        // Two leads down from the box into the plate below.
        for (int k = 0; k < 2; k++)
        {
            double lx = jx0 + bw * (0.3 + 0.4 * k);
            Tube([new(lx, jy0 - gap), new(lx, top + _g)], cw);
        }
        foreach (double f in new[] { 0.4, 0.75 })
        {
            double cx = from + (to - from) * f, h3 = 3 * cw + 2 * gap;
            var clamp = Poly.Rect(cx - cw * 0.7, cy - h3 / 2 - gap, cx + cw * 0.7, cy + h3 / 2 + gap);
            ClearFor(clamp);
            t.Clean(clamp);
        }
        var box = Poly.Chamfered(jx0, jy0, jx1, jy1, Math.Min(bw, bh) * 0.15);
        ClearFor(box);
        Plate(box);
        double inset = Math.Max(0.45, Math.Min(bw, bh) * 0.16);
        Bolt(jx0 + inset, jy0 + inset); Bolt(jx1 - inset, jy0 + inset); Bolt(jx1 - inset, jy1 - inset); Bolt(jx0 + inset, jy1 - inset);
        if (LinesOnly) t.Score([new(jx0 + inset * 2, (jy0 + jy1) / 2), new(jx1 - inset * 2, (jy0 + jy1) / 2)]);
        else t.Etch(Poly.Rect(jx0 + inset * 2, (jy0 + jy1) / 2 - _g * 0.4, jx1 - inset * 2, (jy0 + jy1) / 2 + _g * 0.4));
    }

    /// <summary>A pipe or cable: a raised strip of width <paramref name="w"/> along a line, with rounded joints.</summary>
    void Tube(PathD line, double w)
    {
        var tube = Clipper.InflatePaths(new PathsD { line }, w / 2, JoinType.Round, EndType.Butt, 2, 4);
        foreach (var p in tube)
        {
            if (LinesOnly) t.Score(Poly.Loop(p));
            else t.Clean(p);
        }
        // A line along a fat pipe so it reads round.
        if (w > _d * 2.4) t.Score(Poly.Map(line, q => q));
    }

    /// <summary>A maintenance hatch filling the recess: a heavy frame, a rounded door in it, bolts, a grip and hazard stripes along its foot.</summary>
    void Hatch(double x0, double y0, double x1, double y1, bool flip)
    {
        double w = x1 - x0, hgt = y1 - y0;
        var frame = Poly.Cut(x0, y0, x1, y1, Math.Min(w, hgt) * 0.12, true, true, true, true);
        Plate(frame);
        double inset = Math.Clamp(Math.Min(w, hgt) * 0.12, 1, _d * 3);
        double dx0 = x0 + inset, dy0 = y0 + inset * 1.6, dx1 = x1 - inset, dy1 = y1 - inset;
        if (dx1 - dx0 < 3 || dy1 - dy0 < 3) return;
        var door = Poly.Rounded(dx0, dy0, dx1, dy1, Math.Min(dx1 - dx0, dy1 - dy0) * 0.18);
        if (!LinesOnly) ClearFor(door);
        Plate(door);
        if (LinesOnly) t.Score(Poly.Loop(door));
        double bi = Math.Max(BoltR * 2.5, Math.Min(dx1 - dx0, dy1 - dy0) * 0.1);
        Bolt(dx0 + bi, dy0 + bi); Bolt(dx1 - bi, dy0 + bi); Bolt(dx1 - bi, dy1 - bi); Bolt(dx0 + bi, dy1 - bi);
        double gl = Math.Clamp((dy1 - dy0) * 0.3, _d * 2, _d * 7), gw = Math.Max(0.7, _d * 1.2);
        double gx = flip ? dx0 + bi * 1.4 : dx1 - bi * 1.4, gy = (dy0 + dy1) / 2;
        Hole(Poly.Rounded(gx - gw / 2, gy - gl / 2, gx + gw / 2, gy + gl / 2, gw / 2));
        Stripes(x0 + inset, y0 + inset * 0.35, x1 - inset, y0 + inset * 1.25, Math.Max(0.6, inset * 0.45));
    }

    /// <summary>A console filling the recess: a sloped housing with a screen, and rows of keys under it.</summary>
    void Console(double x0, double y0, double x1, double y1, bool flip)
    {
        double w = x1 - x0, hgt = y1 - y0;
        // The housing stands in the middle of the recess, the rest left open with a pipe down each side.
        double hx0 = x0 + w * 0.14, hx1 = x1 - w * 0.14, hy0 = y0 + hgt * 0.12, hy1 = y1 - hgt * 0.1;
        var housing = Poly.Cut(hx0, hy0, hx1, hy1, Math.Min(hx1 - hx0, hy1 - hy0) * 0.14, false, false, true, true);
        Plate(housing);
        double inset = Math.Max(0.8, (hx1 - hx0) * 0.1);
        double sx0 = hx0 + inset, sx1 = hx1 - inset, sy0 = hy0 + (hy1 - hy0) * 0.45, sy1 = hy1 - inset;
        if (sx1 - sx0 > 2 && sy1 - sy0 > 2)
        {
            var screen = Poly.Rounded(sx0, sy0, sx1, sy1, Math.Min(0.8, _d));
            if (LinesOnly) t.Score(Poly.Loop(screen)); else ClearFor(screen);
        }
        double kw = Math.Max(0.8, _d * 1.3), ky = hy0 + inset;
        for (int row = 0; row < 2 && ky + kw < sy0 - inset * 0.5; row++, ky += kw * 1.5)
            for (double kx = sx0; kx + kw <= sx1; kx += kw * 1.6) Hole(Poly.Rect(kx, ky, kx + kw, ky + kw * 0.8));
        double pw = Math.Max(0.7, _d * 1.4);
        Tube([new(x0 + w * 0.07, y0 + _g), new(x0 + w * 0.07, y1 - _g)], pw);
        Tube([new(x1 - w * 0.07, y0 + _g), new(x1 - w * 0.07, y1 - _g)], pw);
    }

    /// <summary>Diagonal hazard stripes in a box, with its edge scored.</summary>
    void Stripes(double x0, double y0, double x1, double y1, double sw)
    {
        if (x1 - x0 < 1 || y1 - y0 < 0.6) return;
        double band = y1 - y0;
        if (!LinesOnly)
        {
            var stripes = new PathsD();
            for (double sx = x0 - band - sw; sx < x1 + band; sx += sw * 2)
                stripes.Add([new(sx, y0), new(sx + sw, y0), new(sx + sw + band, y1), new(sx + band, y1)]);
            t.Etched.AddRange(Poly.Intersect(stripes, [Poly.Rect(x0, y0, x1, y1)]));
        }
        t.Score(Poly.Loop(Poly.Rect(x0, y0, x1, y1)));
    }
}
