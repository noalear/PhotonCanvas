using Clipper2Lib;

namespace PhotonCanvas.Addon.Terrain;

/// <summary>
/// The sci-fi panels texture, laid out the way a corridor or bulkhead wall is built, not as a grid
/// of unrelated widgets. The wall is a row of identical bays between heavy structural ribs, and it
/// reads in bands from the floor up: a kick plate with a vent under each bay, the lower field, a
/// service band of pipes that runs the whole length (clamped at every rib), the upper field, and a
/// top band with a light in each bay. The bays follow a repeating pattern (every bay alike, or two
/// designs taking turns), with a special one now and then (a maintenance hatch, a big vent, a
/// console, a junction box on the pipes) and never two side by side, so most of the wall is big calm
/// plates and the detail is grouped where it would be. Plates are machined: crisp chamfers and
/// seams, an inset groove, a few bolts at the corners. Panel width sets the bays, panel height the
/// bands; the detail size sets the bolts, slats, pipes and grooves. Wear scuffs, scratches and pits
/// the metal; a well-worn wall gets dented plates too.
/// </summary>
internal sealed class SciFi(TextureEngine.Texture t)
{
    readonly Random _rnd = t.Rnd;
    double _g, _d;

    bool LinesOnly => t.Wear.LinesOnly;

    /// <summary>What fills the field above the service band.</summary>
    enum Upper { Plate, Split, Recess, Console, Pipework }
    /// <summary>What fills the field below it.</summary>
    enum Lower { Plate, Vent, Hatch, Ribbed }

    T Pick<T>(params T[] options) => options[_rnd.Next(options.Length)];

    public void Make(double len, double h, double a, double b, double g)
    {
        _g = Math.Max(0.25, g);
        _d = Math.Max(0.3, t.D);
        double bay = Math.Max(6, a);
        double rib = Math.Clamp(bay * 0.16, 1.4, 4.5);
        double kick = Math.Clamp(b * 0.45, 2.5, 7);
        double top = h > 22 ? Math.Clamp(b * 0.35, 2.2, 5.5) : 0;
        double f0 = kick, f1 = h - top;

        // The service band: pipes the whole length of the wall, low down (or high up), on a wall tall enough.
        bool service = f1 - f0 > b * 2.4 && _rnd.NextDouble() < 0.8;
        double band = Math.Clamp(b * 0.5, 3, 8);
        double bandY = f0 + (f1 - f0) * Pick(0.3, 0.32, 0.36, 0.58) - band / 2;
        // Without one, the field is still split into a lower and an upper plate, about a third of the way up.
        double split = service ? bandY : f0 + (f1 - f0) * Pick(0.3, 0.35, 0.4);
        double upperFrom = service ? bandY + band : split;

        // Two bay designs and how they take turns.
        var mainU = Pick(Upper.Plate, Upper.Plate, Upper.Split, Upper.Recess);
        var altU = Pick(Upper.Plate, Upper.Split, Upper.Recess);
        var mainL = Pick(Lower.Plate, Lower.Plate, Lower.Ribbed);
        var altL = Pick(Lower.Plate, Lower.Vent, Lower.Ribbed);
        string pattern = Pick("A", "AB", "AB", "AAB", "ABBA");
        bool mirror = _rnd.NextDouble() < 0.5;

        double start = -bay * _rnd.NextDouble();
        var ribs = new List<double>();
        var junctions = new List<double>();
        bool lastSpecial = true;
        int i = 0;
        for (double x = start; x < len + bay; x += bay, i++)
        {
            ribs.Add(x);
            double bx0 = x + rib / 2 + _g / 2, bx1 = x + bay - rib / 2 - _g / 2;
            bool alt = pattern[i % pattern.Length] == 'B';
            var u = alt ? altU : mainU;
            var lo = alt ? altL : mainL;
            // A special bay now and then, never two side by side.
            bool special = !lastSpecial && _rnd.NextDouble() < 0.22;
            bool hatch = false, junction = false;
            if (special)
            {
                switch (_rnd.Next(5))
                {
                    case 0: case 1: lo = Lower.Hatch; hatch = true; break;
                    case 2: lo = Lower.Vent; break;
                    case 3: u = Upper.Console; break;
                    default: if (service) junction = true; else u = Upper.Pipework; break;
                }
            }
            lastSpecial = special;
            bool flip = mirror && i % 2 == 1;

            Kick(bx0, bx1, kick, hatch);
            LowerField(lo, bx0, f0 + _g / 2, bx1, split - _g / 2, flip);
            UpperField(u, bx0, upperFrom + _g / 2, bx1, f1 - _g / 2, flip);
            if (top > 0) TopBand(bx0, f1 + _g / 2, bx1, h + 1);
            if (junction) junctions.Add((bx0 + bx1) / 2);
        }
        foreach (var x in ribs) Rib(x, rib, h, service ? bandY : double.NaN, band, kick, f1);
        if (service) Service(start - bay, len + 2 * bay, bandY, band, ribs, rib);
        // Junction boxes go over the pipes, so after them.
        foreach (var jx in junctions) Junction(jx, bandY, band, bay);
    }

    // ---- small parts ----

    double BoltR => Math.Clamp(_d * 0.38, 0.2, 0.6);

    /// <summary>A hole, slot or recess cut into a plate: etched, or outlined when the texture is only scored.</summary>
    void Hole(PathD p)
    {
        if (LinesOnly) t.Score(Poly.Loop(p));
        else t.Etch(p);
    }

    void Bolt(double x, double y) => Hole(Poly.Circle(x, y, BoltR));

    /// <summary>A plate: crisp, as machined, or (on a well-worn wall, now and then) dented; scuffed and scratched as the wear says.</summary>
    void Plate(PathD p)
    {
        if (t.Wear.Amount > 0.6 && t.Weather.Random.NextDouble() < (t.Wear.Amount - 0.6) * 1.5) t.Raised.AddRange(t.Weather.Piece(p, Stuff.Metal, t.Etched, t.Lines));
        else t.Clean(p);
        if (t.Weather.Random.NextDouble() < 0.25 + t.Wear.Amount * 0.6) t.Weather.Surface([p], Stuff.Metal, t.Etched, t.Lines);
    }

    /// <summary>A groove ring following a plate's outline, <paramref name="inset"/> in from its edge.</summary>
    void Ring(PathD plate, double inset)
    {
        double gr = Math.Max(0.3, _g * 0.7);
        var outer = Poly.Offset([plate], -inset);
        var inner = Poly.Offset([plate], -inset - gr);
        if (outer.Count == 0 || inner.Count == 0) return;
        if (LinesOnly) { foreach (var o in outer) t.Score(Poly.Loop(o)); return; }
        t.Etched.AddRange(Poly.Subtract(outer, inner));
    }

    /// <summary>Bolts in from each corner of a box.</summary>
    void CornerBolts(double x0, double y0, double x1, double y1, double inset)
    {
        if (x1 - x0 < inset * 3 || y1 - y0 < inset * 3) return;
        Bolt(x0 + inset, y0 + inset);
        Bolt(x1 - inset, y0 + inset);
        Bolt(x1 - inset, y1 - inset);
        Bolt(x0 + inset, y1 - inset);
    }

    // ---- the bands ----

    /// <summary>The kick plate under a bay: its top corners cut, a vent in the middle (hazard stripes under a hatch).</summary>
    void Kick(double x0, double x1, double kick, bool hazard)
    {
        double y0 = _g / 2, y1 = kick - _g / 2, w = x1 - x0, hgt = y1 - y0;
        if (w < 2 || hgt < 1) return;
        double c = Math.Min(hgt * 0.35, w * 0.1);
        Plate(Poly.Cut(x0, y0, x1, y1, c, false, false, true, true));
        double inset = Math.Max(0.6, hgt * 0.22);
        if (hazard)
        {
            Stripes(x0 + inset, y0 + inset, x1 - inset, y1 - inset, Math.Max(0.6, hgt * 0.25));
            return;
        }
        // A row of upright slots, centred.
        double pitch = Math.Max(0.9, _d * 1.6), slot = pitch * 0.5, vw = w * 0.6;
        int n = Math.Max(1, (int)((vw + pitch - slot) / pitch));
        double vx = (x0 + x1) / 2 - ((n - 1) * pitch + slot) / 2;
        for (int k = 0; k < n; k++) Hole(Poly.Rounded(vx + k * pitch, y0 + inset, vx + k * pitch + slot, y1 - inset, slot * 0.45));
    }

    /// <summary>The band along the top: a plate over each bay with a light set in it.</summary>
    void TopBand(double x0, double y0, double x1, double y1)
    {
        double w = x1 - x0, hgt = y1 - 1 - y0;
        if (w < 2 || hgt < 1) return;
        Plate(Poly.Cut(x0, y0, x1, y1, Math.Min(hgt * 0.4, w * 0.1), true, true, false, false));
        double lh = Math.Clamp(hgt * 0.32, 0.6, _d * 2), cy = y0 + hgt * 0.5;
        Hole(Poly.Rounded(x0 + w * 0.22, cy - lh / 2, x1 - w * 0.22, cy + lh / 2, lh / 2));
    }

    /// <summary>A structural rib: full height, broken round the service band (where a clamp holds the pipes), with bolts at its ends.</summary>
    void Rib(double cx, double rib, double h, double bandY, double band, double kick, double f1)
    {
        double x0 = cx - rib / 2, x1 = cx + rib / 2;
        (double, double)[] spans = double.IsNaN(bandY) ? [(0.0, h + 1)] : [(0.0, bandY - _g / 2), (bandY + band + _g / 2, h + 1)];
        foreach (var (y0, y1) in spans)
        {
            if (y1 - y0 < 1) continue;
            t.Clean(Poly.Rect(x0, y0, x1, y1));
            // A channel down a wide rib, so it reads as a built-up beam.
            if (rib > 2.6)
            {
                double cw = Math.Max(0.3, _g * 0.6);
                if (LinesOnly) t.Score([new(cx, y0 + rib), new(cx, Math.Min(y1, h) - rib)]);
                else if (Math.Min(y1, h) - rib - (y0 + rib) > 1) t.Etch(Poly.Rect(cx - cw / 2, y0 + rib, cx + cw / 2, Math.Min(y1, h) - rib));
            }
            double by0 = y0 + Math.Max(rib * 0.6, BoltR * 3), by1 = Math.Min(y1, h) - Math.Max(rib * 0.6, BoltR * 3);
            if (by1 - by0 > BoltR * 4)
            {
                Bolt(cx, by0);
                Bolt(cx, by1);
            }
        }
        // Where the rib meets the kick plate and the top band: a bolted collar (scored).
        t.Score([new(x0, kick), new(x1, kick)]);
        if (f1 < h) t.Score([new(x0, f1), new(x1, f1)]);
    }

    /// <summary>The pipes along the service band: a rail top and bottom, two or three pipes, a clamp over them at every rib.</summary>
    void Service(double x0, double x1, double y, double band, List<double> ribs, double rib)
    {
        double rail = Math.Max(0.6, Math.Min(_d * 1.1, band * 0.14));
        t.Clean(Poly.Rect(x0, y, x1, y + rail));
        t.Clean(Poly.Rect(x0, y + band - rail, x1, y + band));
        double p0 = y + rail + _g, p1 = y + band - rail - _g;
        if (p1 - p0 < 1) return;
        // Pipes of two sizes, the fat one first (or last), packed into the band.
        var sizes = Pick(new[] { 0.55, 0.45 }, new[] { 0.4, 0.6 }, new[] { 0.35, 0.3, 0.35 }, new[] { 0.5, 0.25, 0.25 });
        double avail = p1 - p0 - _g * (sizes.Length - 1);
        var pipes = new List<(double From, double To)>();
        double v = p0;
        foreach (var s in sizes)
        {
            double d = avail * s;
            pipes.Add((v, v + d));
            v += d + _g;
        }
        double clampW = Math.Max(rib * 0.9, _d * 2);
        foreach (var (f, to) in pipes)
        {
            double from = x0;
            foreach (var r in ribs.Append(x1 + clampW + _g))
            {
                double end = r - clampW / 2 - _g;
                if (end - from > 0.6) t.Clean(Poly.Rect(from, f, end, to));
                from = r + clampW / 2 + _g;
            }
            // A line along each fat pipe, so it reads round; flanges every so often on the thin ones.
            if (to - f > _d * 2.6)
            {
                double m = f + (to - f) * 0.66;
                if (LinesOnly || to - f > _d * 4) t.Score([new(x0, m), new(x1, m)]);
            }
        }
        foreach (var r in ribs)
        {
            var clamp = Poly.Rect(r - clampW / 2, y + rail + _g * 0.3, r + clampW / 2, y + band - rail - _g * 0.3);
            t.Clean(clamp);
            Bolt(r, y + band / 2);
        }
    }

    /// <summary>A junction box on the pipes, half-way along a bay.</summary>
    void Junction(double cx, double y, double band, double bay)
    {
        double w = Math.Min(bay * 0.45, band * 1.6), x0 = cx - w / 2, x1 = cx + w / 2, y0 = y - band * 0.2, y1 = y + band * 1.2;
        // Cleared out of the pipes and rails first, then drawn over them.
        var box = Poly.Cut(x0, y0, x1, y1, Math.Min(w, y1 - y0) * 0.15, true, true, true, true);
        var gapAround = Poly.Offset([box], _g);
        var keep = Poly.Subtract([.. t.Raised], gapAround);
        t.Raised.Clear();
        t.Raised.AddRange(keep);
        Plate(box);
        Ring(box, Math.Max(0.6, _d * 0.9));
        CornerBolts(x0, y0, x1, y1, Math.Max(0.7, _d * 1.4));
    }

    // ---- the fields ----

    void LowerField(Lower kind, double x0, double y0, double x1, double y1, bool flip)
    {
        double w = x1 - x0, hgt = y1 - y0;
        if (w < 2 || hgt < 2) return;
        double c = Math.Min(w, hgt) * 0.18;
        switch (kind)
        {
            case Lower.Plate:
            {
                // The top corners cut, mirroring the upper field's bottom ones.
                var p = Poly.Cut(x0, y0, x1, y1, c, false, false, true, true);
                Plate(p);
                Ring(p, Math.Clamp(Math.Min(w, hgt) * 0.12, 0.7, _d * 2.5));
                break;
            }
            case Lower.Ribbed:
            {
                // A plate stiffened by raised bars the long way, in a recess.
                Frame(x0, y0, x1, y1, Math.Clamp(Math.Min(w, hgt) * 0.12, 0.8, _d * 2.4), out var ix0, out var iy0, out var ix1, out var iy1);
                int n = Math.Max(2, Math.Min(4, (int)((iy1 - iy0) / Math.Max(1.5, _d * 3))));
                double pitch = (iy1 - iy0) / n;
                for (int k = 0; k < n; k++) t.Clean(Poly.Rect(ix0 + _g, iy0 + k * pitch + _g / 2, ix1 - _g, iy0 + (k + 1) * pitch - _g / 2));
                break;
            }
            case Lower.Vent:
            {
                Frame(x0, y0, x1, y1, Math.Clamp(Math.Min(w, hgt) * 0.12, 0.8, _d * 2.4), out var ix0, out var iy0, out var ix1, out var iy1);
                Slats(ix0, iy0, ix1, iy1);
                break;
            }
            case Lower.Hatch:
            {
                // A frame round a door with rounded corners; the door bolted shut, a grip on the side away from its hinges.
                var frame = Poly.Cut(x0, y0, x1, y1, c, false, false, true, true);
                double inset = Math.Clamp(Math.Min(w, hgt) * 0.12, 0.9, _d * 2.6);
                double dx0 = x0 + inset, dy0 = y0 + inset, dx1 = x1 - inset, dy1 = y1 - inset;
                if (dx1 - dx0 < 2 || dy1 - dy0 < 2) { Plate(frame); break; }
                var door = Poly.Rounded(dx0, dy0, dx1, dy1, Math.Min(dx1 - dx0, dy1 - dy0) * 0.2);
                Plate(frame);
                if (!LinesOnly)
                {
                    // The door stands in its own opening: a gap all round it, so it reads as a separate piece.
                    var opening = Poly.Offset([door], _g);
                    var kept = Poly.Subtract([.. t.Raised], opening);
                    t.Raised.Clear();
                    t.Raised.AddRange(kept);
                }
                Plate(door);
                if (LinesOnly) t.Score(Poly.Loop(door));
                double bi = Math.Max(BoltR * 2.5, Math.Min(dx1 - dx0, dy1 - dy0) * 0.14);
                CornerBolts(dx0, dy0, dx1, dy1, bi);
                double gl = Math.Clamp((dy1 - dy0) * 0.35, _d * 2, _d * 6), gw = Math.Max(0.7, _d * 1.2);
                double gx = flip ? dx0 + bi * 1.1 : dx1 - bi * 1.1, gy = (dy0 + dy1) / 2;
                Hole(Poly.Rounded(gx - gw / 2, gy - gl / 2, gx + gw / 2, gy + gl / 2, gw / 2));
                break;
            }
        }
    }

    void UpperField(Upper kind, double x0, double y0, double x1, double y1, bool flip)
    {
        double w = x1 - x0, hgt = y1 - y0;
        if (w < 2 || hgt < 2) return;
        double c = Math.Min(w, hgt) * 0.2;
        switch (kind)
        {
            case Upper.Plate:
            {
                var p = Poly.Cut(x0, y0, x1, y1, c, true, true, false, false);
                Plate(p);
                double inset = Math.Clamp(Math.Min(w, hgt) * 0.1, 0.7, _d * 2.5);
                Ring(p, inset);
                if (inset > BoltR * 2.6) { Bolt(x0 + inset / 2, y1 - inset / 2); Bolt(x1 - inset / 2, y1 - inset / 2); }
                // A tall plate is two sheets: a seam scored across inside the groove, about two thirds of the way up.
                if (hgt > w * 1.8 && w > inset * 6)
                {
                    double sy = y0 + hgt * 0.64, sx0 = x0 + inset * 2.2, sx1 = x1 - inset * 2.2;
                    t.Score([new(sx0, sy), new(sx1, sy)]);
                }
                break;
            }
            case Upper.Split:
            {
                // Two plates meeting along a seam with a 45° jog in it, the jog to one side.
                var whole = Poly.Cut(x0, y0, x1, y1, c, true, true, false, false);
                double ys = y0 + hgt * 0.42, jog = Math.Min(hgt * 0.18, w * 0.2), xm = flip ? x0 + w * 0.62 : x0 + w * 0.38;
                PathD below = flip
                    ? [new(x0 - 1, y0 - 1), new(x1 + 1, y0 - 1), new(x1 + 1, ys), new(xm + jog, ys), new(xm, ys + jog), new(x0 - 1, ys + jog)]
                    : [new(x0 - 1, y0 - 1), new(x1 + 1, y0 - 1), new(x1 + 1, ys + jog), new(xm + jog, ys + jog), new(xm, ys), new(x0 - 1, ys)];
                var lowerPart = Poly.Offset(Poly.Intersect([whole], [below]), -_g / 2);
                var upperPart = Poly.Offset(Poly.Subtract([whole], [below]), -_g / 2);
                foreach (var p in lowerPart) Plate(p);
                foreach (var p in upperPart)
                {
                    Plate(p);
                    Ring(p, Math.Clamp(Math.Min(w, hgt) * 0.08, 0.6, _d * 2));
                }
                break;
            }
            case Upper.Recess:
            {
                // A raised frame round a deep channel, a plate standing in the middle.
                double bar = Math.Clamp(Math.Min(w, hgt) * 0.1, 0.8, _d * 2.4);
                Frame(x0, y0, x1, y1, bar, out var ix0, out var iy0, out var ix1, out var iy1);
                double ch = Math.Max(0.6, _d * 1.2);
                if (ix1 - ix0 > ch * 3 && iy1 - iy0 > ch * 3)
                {
                    var inner = Poly.Chamfered(ix0 + ch, iy0 + ch, ix1 - ch, iy1 - ch, Math.Min(ix1 - ix0, iy1 - iy0) * 0.12);
                    Plate(inner);
                    CornerBolts(ix0 + ch, iy0 + ch, ix1 - ch, iy1 - ch, Math.Max(BoltR * 2.5, _d * 1.2));
                }
                break;
            }
            case Upper.Console:
            {
                // A plate with a screen let into it and a row of keys under the screen.
                var p = Poly.Cut(x0, y0, x1, y1, c, true, true, false, false);
                double inset = Math.Clamp(Math.Min(w, hgt) * 0.14, 0.9, _d * 2.6);
                double sx0 = x0 + inset, sx1 = x1 - inset, sy0 = y0 + hgt * 0.4, sy1 = y1 - inset;
                if (sx1 - sx0 < 2 || sy1 - sy0 < 2) { Plate(p); break; }
                var screen = Poly.Rounded(sx0, sy0, sx1, sy1, Math.Min(0.8, _d));
                if (LinesOnly) { Plate(p); t.Score(Poly.Loop(screen)); }
                else foreach (var piece in Poly.Subtract([p], [screen])) t.Clean(piece);
                double ky0 = y0 + inset + c * 0.5, ky1 = sy0 - inset * 0.6, kw = Math.Max(0.8, _d * 1.4);
                if (ky1 - ky0 > 0.6)
                {
                    int n = Math.Max(1, (int)((sx1 - sx0 + kw * 0.6) / (kw * 1.6)));
                    double kx = (sx0 + sx1) / 2 - (n * kw * 1.6 - kw * 0.6) / 2;
                    for (int k = 0; k < n; k++) Hole(Poly.Rect(kx + k * kw * 1.6, ky0, kx + k * kw * 1.6 + kw, ky1));
                }
                break;
            }
            case Upper.Pipework:
            {
                // Pipes running up the bay in a recess, clamped top and bottom.
                Frame(x0, y0, x1, y1, Math.Clamp(Math.Min(w, hgt) * 0.1, 0.8, _d * 2.4), out var ix0, out var iy0, out var ix1, out var iy1);
                int n = w > _d * 10 ? 3 : 2;
                double pw = (ix1 - ix0 - _g * (n + 1)) / n;
                if (pw < 0.6) break;
                double clamp = Math.Max(0.8, _d * 1.6);
                for (int k = 0; k < n; k++)
                {
                    double px = ix0 + _g + k * (pw + _g);
                    t.Clean(Poly.Rect(px, iy0 + clamp + _g * 2, px + pw, iy1 - clamp - _g * 2));
                }
                t.Clean(Poly.Rect(ix0 + _g / 2, iy0 + _g, ix1 - _g / 2, iy0 + _g + clamp));
                t.Clean(Poly.Rect(ix0 + _g / 2, iy1 - _g - clamp, ix1 - _g / 2, iy1 - _g));
                break;
            }
        }
    }

    /// <summary>A frame of four bars round a recess (the sides full height, top and bottom between); the recess inside is returned.</summary>
    void Frame(double x0, double y0, double x1, double y1, double bar, out double ix0, out double iy0, out double ix1, out double iy1)
    {
        t.Clean(Poly.Rect(x0, y0, x0 + bar, y1));
        t.Clean(Poly.Rect(x1 - bar, y0, x1, y1));
        t.Clean(Poly.Rect(x0 + bar, y0, x1 - bar, y0 + bar));
        t.Clean(Poly.Rect(x0 + bar, y1 - bar, x1 - bar, y1));
        double gap = Math.Max(0.25, _g * 0.6);
        ix0 = x0 + bar + gap;
        iy0 = y0 + bar + gap;
        ix1 = x1 - bar - gap;
        iy1 = y1 - bar - gap;
        if (t.Weather.Random.NextDouble() < 0.25 + t.Wear.Amount * 0.6) t.Weather.Surface([Poly.Rect(x0, y0, x1, y1)], Stuff.Metal, t.Etched, t.Lines);
    }

    /// <summary>Horizontal slats across a recess, with a divider down the middle of a wide one.</summary>
    void Slats(double x0, double y0, double x1, double y1)
    {
        if (x1 - x0 < 0.8 || y1 - y0 < 0.8) return;
        double pitch = Math.Max(0.9, _d * 1.6), slat = pitch * 0.5;
        int n = Math.Max(1, (int)Math.Floor((y1 - y0 + pitch - slat) / pitch));
        double y = y0 + (y1 - y0 - ((n - 1) * pitch + slat)) / 2;
        bool split = x1 - x0 > 12;
        double mx = (x0 + x1) / 2, half = Math.Max(0.5, slat);
        for (int k = 0; k < n; k++, y += pitch)
        {
            if (split)
            {
                t.Clean(Poly.Rect(x0, y, mx - half - pitch * 0.3, y + slat));
                t.Clean(Poly.Rect(mx + half + pitch * 0.3, y, x1, y + slat));
            }
            else t.Clean(Poly.Rect(x0, y, x1, y + slat));
        }
        if (split) t.Clean(Poly.Rect(mx - half, y0, mx + half, y1));
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
