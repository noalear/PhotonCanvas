using Clipper2Lib;

namespace PhotonCanvas.Addon.Terrain;

/// <summary>
/// The sci-fi panels texture: a bulkhead built the way real ones are, not a grid of the same plate.
/// A kick plate of vents runs along the foot; above it the wall is split into bays of different widths,
/// some divided by bolted ribs, and each bay is a stack of modules of different heights: inset plates,
/// recessed panels with stiffeners, vent grilles, hatches with handles, pipe runs with clamps, clusters
/// of small greebles (ports, slots, knurled blocks), light strips, display panels and the odd band of
/// hazard stripes. Panel width and height set the bays and modules; the detail size sets everything
/// finer (slats, bolts, ports, pipes). Wear scratches, dings and rust-pits the metal.
/// </summary>
internal sealed class SciFi(TextureEngine.Texture t)
{
    readonly Random _rnd = t.Rnd;
    double _g, _d;

    enum Module { Inset, Recessed, Vent, Hatch, Pipes, Greebles, Hazard, Light, Display }

    static readonly (Module Kind, double Weight)[] Weights =
    [
        (Module.Inset, 30), (Module.Recessed, 7), (Module.Vent, 10), (Module.Hatch, 10), (Module.Pipes, 10),
        (Module.Greebles, 13), (Module.Hazard, 5), (Module.Light, 7), (Module.Display, 5),
    ];

    Module Pick()
    {
        double total = Weights.Sum(w => w.Weight), r = _rnd.NextDouble() * total;
        foreach (var (kind, weight) in Weights)
        {
            if (r < weight) return kind;
            r -= weight;
        }
        return Module.Inset;
    }

    double Pick(params double[] options) => options[_rnd.Next(options.Length)];

    public void Make(double len, double h, double a, double b, double g)
    {
        _g = g;
        _d = Math.Max(0.3, t.D);
        double rib = Math.Clamp(_d * 2.6, 1.2, Math.Max(1.2, a * 0.2));

        // The kick plate along the foot.
        double kick = Math.Clamp(b * 0.4, 2.5, 7);
        bool vents = _rnd.NextDouble() < 0.7;
        for (double x = -a * _rnd.NextDouble(); x < len + a;)
        {
            double w = a * Pick(1, 1.5, 2);
            double x0 = x + g / 2, x1 = x + w - g / 2, y0 = g / 2, y1 = kick - g / 2;
            double pick = _rnd.NextDouble();
            if (pick < 0.12) Hazard(x0, y0, x1, y1);
            else if (vents || pick < 0.4) Vent(x0, y0, x1, y1, horizontal: false);
            else Inset(x0, y0, x1, y1);
            x += w;
        }

        // Bays above it, some with ribs between.
        for (double x = -a * _rnd.NextDouble(); x < len + a;)
        {
            double w = a * Pick(1, 1, 1, 1.5, 2);
            bool ribLeft = _rnd.NextDouble() < 0.5;
            double bx0 = x + (ribLeft ? rib + g : g / 2), bx1 = x + w - g / 2;
            if (ribLeft) Rib(x + g / 2, kick + g / 2, x + rib + g / 2, h + b);
            for (double y = kick; y < h + b;)
            {
                double mh = b * Pick(0.5, 1, 1, 1, 1.5);
                if (bx1 - bx0 < a * 0.8) mh = b * Pick(0.5, 1, 1.5, 2);
                Draw(Pick(), bx0, y + g / 2, bx1, y + mh - g / 2);
                y += mh;
            }
            x += w;
        }
    }

    void Draw(Module m, double x0, double y0, double x1, double y1)
    {
        double w = x1 - x0, h = y1 - y0;
        if (w < 1 || h < 1) return;
        // Small modules can't hold the fussier kinds: they fall back to a plate.
        if (Math.Min(w, h) < _d * 6 && m is Module.Hatch or Module.Pipes or Module.Display or Module.Recessed) m = Module.Inset;
        if (Math.Min(w, h) < _d * 4 && m is Module.Vent or Module.Greebles or Module.Light) m = Module.Inset;
        switch (m)
        {
            case Module.Inset: Inset(x0, y0, x1, y1); break;
            case Module.Recessed: Recessed(x0, y0, x1, y1); break;
            case Module.Vent: Vent(x0, y0, x1, y1, horizontal: w > h * 0.8); break;
            case Module.Hatch: Hatch(x0, y0, x1, y1); break;
            case Module.Pipes: Pipes(x0, y0, x1, y1); break;
            case Module.Greebles: Greebles(x0, y0, x1, y1, 0); break;
            case Module.Hazard: Hazard(x0, y0, x1, y1); break;
            case Module.Light: Light(x0, y0, x1, y1); break;
            case Module.Display: Display(x0, y0, x1, y1); break;
        }
    }

    // ---- pieces ----

    double Chamfer(double w, double h) => Math.Min(w, h) * 0.12;

    double BoltR => Math.Clamp(_d * 0.38, 0.2, 0.6);

    void Bolt(double x, double y)
    {
        if (t.Wear.LinesOnly) t.Score(Poly.Loop(Poly.Circle(x, y, BoltR)));
        else t.Etch(Poly.Circle(x, y, BoltR));
    }

    /// <summary>A plain plate, its corners cut, worn as metal.</summary>
    PathsD Plate(double x0, double y0, double x1, double y1, double c)
    {
        var worn = t.Weather.Piece(Poly.Chamfered(x0, y0, x1, y1, c), Stuff.Metal, t.Etched, t.Lines);
        t.Raised.AddRange(worn);
        return worn;
    }

    /// <summary>A frame of four bars round a recess (left and right full height, top and bottom between).</summary>
    void Frame(double x0, double y0, double x1, double y1, double bar, out double ix0, out double iy0, out double ix1, out double iy1)
    {
        double gap = Math.Max(0.25, _g * 0.6);
        t.Clean(Poly.Rect(x0, y0, x0 + bar, y1));
        t.Clean(Poly.Rect(x1 - bar, y0, x1, y1));
        t.Clean(Poly.Rect(x0 + bar, y0, x1 - bar, y0 + bar));
        t.Clean(Poly.Rect(x0 + bar, y1 - bar, x1 - bar, y1));
        ix0 = x0 + bar + gap;
        iy0 = y0 + bar + gap;
        ix1 = x1 - bar - gap;
        iy1 = y1 - bar - gap;
        t.Weather.Surface([Poly.Rect(x0, y0, x1, y1)], Stuff.Metal, t.Etched, t.Lines);
    }

    /// <summary>A groove ring at <paramref name="inset"/> in from the plate's edge.</summary>
    void Ring(double x0, double y0, double x1, double y1, double inset, double c)
    {
        double gr = Math.Max(0.3, _g * 0.7);
        if (x1 - x0 < 2 * (inset + gr) + 0.8 || y1 - y0 < 2 * (inset + gr) + 0.8) return;
        var outer = Poly.Chamfered(x0 + inset, y0 + inset, x1 - inset, y1 - inset, c);
        var inner = Poly.Chamfered(x0 + inset + gr, y0 + inset + gr, x1 - inset - gr, y1 - inset - gr, Math.Max(0, c - gr * 0.4));
        if (t.Wear.LinesOnly) { t.Score(Poly.Loop(outer)); return; }
        t.Etched.AddRange(Poly.Subtract([outer], [inner]));
    }

    void Rib(double x0, double y0, double x1, double y1)
    {
        t.Clean(Poly.Rect(x0, y0, x1, y1));
        double cx = (x0 + x1) / 2, step = Math.Max(3, _d * 8);
        for (double y = y0 + step / 2; y < y1; y += step) Bolt(cx, y);
        t.Weather.Surface([Poly.Rect(x0, y0, x1, y1)], Stuff.Metal, t.Etched, t.Lines);
    }

    // ---- modules ----

    void Inset(double x0, double y0, double x1, double y1)
    {
        double w = x1 - x0, h = y1 - y0, c = Chamfer(w, h);
        Plate(x0, y0, x1, y1, c);
        double inset = Math.Clamp(Math.Min(w, h) * 0.13, 0.7, _d * 3);
        Ring(x0, y0, x1, y1, inset, c * 0.6);
        // Bolts in the corners of the rim, if it's wide enough to hold them.
        if (inset > BoltR * 2.6)
            foreach (var (bx, by) in new[] { (x0 + inset / 2 + c * 0.3, y0 + inset / 2 + c * 0.3), (x1 - inset / 2 - c * 0.3, y0 + inset / 2 + c * 0.3), (x1 - inset / 2 - c * 0.3, y1 - inset / 2 - c * 0.3), (x0 + inset / 2 + c * 0.3, y1 - inset / 2 - c * 0.3) })
                Bolt(bx, by);
        // A seam or two across the middle plate, as if it were made of smaller sheets.
        double ix0 = x0 + inset * 2, ix1 = x1 - inset * 2, iy0 = y0 + inset * 2, iy1 = y1 - inset * 2;
        if (ix1 - ix0 > 4 && iy1 - iy0 > 4 && _rnd.NextDouble() < 0.5)
        {
            if (w > h) { double sx = ix0 + (ix1 - ix0) * (0.35 + 0.3 * _rnd.NextDouble()); t.Score([new(sx, iy0), new(sx, iy1)]); }
            else { double sy = iy0 + (iy1 - iy0) * (0.35 + 0.3 * _rnd.NextDouble()); t.Score([new(ix0, sy), new(ix1, sy)]); }
        }
        // Now and then a stencilled label plate.
        if (_rnd.NextDouble() < 0.2 && ix1 - ix0 > 6 && iy1 - iy0 > 3)
        {
            double lw = Math.Min(ix1 - ix0, _d * 9), lh = Math.Min(iy1 - iy0, _d * 3), lx = ix0 + (ix1 - ix0 - lw) * _rnd.NextDouble(), ly = iy1 - lh;
            t.Score(Poly.Loop(Poly.Rect(lx, ly, lx + lw, ly + lh)));
            for (double tx = lx + _d; tx < lx + lw - _d; tx += _d * (1.2 + _rnd.NextDouble())) t.Score([new(tx, ly + lh * 0.5), new(Math.Min(lx + lw - _d * 0.6, tx + _d * (0.5 + _rnd.NextDouble())), ly + lh * 0.5)]);
        }
    }

    void Recessed(double x0, double y0, double x1, double y1)
    {
        double bar = Math.Clamp(Math.Min(x1 - x0, y1 - y0) * 0.14, 0.9, _d * 3);
        Frame(x0, y0, x1, y1, bar, out var ix0, out var iy0, out var ix1, out var iy1);
        if (ix1 - ix0 < 1 || iy1 - iy0 < 1) return;
        // Stiffeners across the recess: two or three raised ribs, the long way.
        int n = 2 + _rnd.Next(2);
        double gap = Math.Max(0.4, _g);
        bool across = ix1 - ix0 >= iy1 - iy0;
        double span = across ? iy1 - iy0 : ix1 - ix0, rw = Math.Max(0.6, Math.Min(_d * 1.4, span / (n * 2 + 1)));
        for (int i = 0; i < n; i++)
        {
            double m = (i + 1.0) / (n + 1);
            if (across) { double cy = iy0 + span * m; t.Clean(Poly.Rect(ix0 + gap, cy - rw / 2, ix1 - gap, cy + rw / 2)); }
            else { double cx = ix0 + span * m; t.Clean(Poly.Rect(cx - rw / 2, iy0 + gap, cx + rw / 2, iy1 - gap)); }
        }
    }

    void Vent(double x0, double y0, double x1, double y1, bool horizontal)
    {
        double bar = Math.Clamp(Math.Min(x1 - x0, y1 - y0) * 0.14, 0.7, _d * 2.2);
        Frame(x0, y0, x1, y1, bar, out var ix0, out var iy0, out var ix1, out var iy1);
        if (ix1 - ix0 < 0.8 || iy1 - iy0 < 0.8) return;
        double pitch = Math.Max(0.9, _d * 1.6), slat = pitch * 0.5;
        if (horizontal)
        {
            int n = Math.Max(1, (int)Math.Floor((iy1 - iy0 + pitch - slat) / pitch));
            double y = iy0 + (iy1 - iy0 - ((n - 1) * pitch + slat)) / 2;
            // A divider bar down the middle of a wide grille.
            bool split = ix1 - ix0 > 14;
            double mx = (ix0 + ix1) / 2, half = Math.Max(0.5, slat);
            for (int i = 0; i < n; i++, y += pitch)
            {
                if (split)
                {
                    t.Clean(Poly.Rect(ix0, y, mx - half - pitch * 0.3, y + slat));
                    t.Clean(Poly.Rect(mx + half + pitch * 0.3, y, ix1, y + slat));
                }
                else t.Clean(Poly.Rect(ix0, y, ix1, y + slat));
            }
            if (split) t.Clean(Poly.Rect(mx - half, iy0, mx + half, iy1));
        }
        else
        {
            int n = Math.Max(1, (int)Math.Floor((ix1 - ix0 + pitch - slat) / pitch));
            double x = ix0 + (ix1 - ix0 - ((n - 1) * pitch + slat)) / 2;
            for (int i = 0; i < n; i++, x += pitch) t.Clean(Poly.Rect(x, iy0, x + slat, iy1));
        }
    }

    /// <summary>
    /// A hatch: a heavy-chamfered plate with its door outlined in a groove, hinges down one side, a
    /// recessed grip on the other and, on a big one, a strip of hazard stripes along its foot.
    /// </summary>
    void Hatch(double x0, double y0, double x1, double y1)
    {
        double w = x1 - x0, h = y1 - y0, c = Math.Min(w, h) * 0.22;
        Plate(x0, y0, x1, y1, c);
        double inset = Math.Clamp(Math.Min(w, h) * 0.1, 0.7, _d * 2);
        Ring(x0, y0, x1, y1, inset, c * 0.8);
        bool hingeLeft = _rnd.NextDouble() < 0.5;
        bool upright = h >= w * 0.7;
        double hl = Math.Min((upright ? h : w) * 0.16, _d * 4), hwid = Math.Max(0.6, _d * 1.2);
        // Hinges straddle the door's groove on one side (or along the top of a wide, low hatch).
        foreach (double f in new[] { 0.25, 0.75 })
        {
            PathD hinge = upright
                ? Poly.Rect((hingeLeft ? x0 + inset : x1 - inset) - hwid / 2, y0 + h * f - hl / 2, (hingeLeft ? x0 + inset : x1 - inset) + hwid / 2, y0 + h * f + hl / 2)
                : Poly.Rect(x0 + w * f - hl / 2, y1 - inset - hwid / 2, x0 + w * f + hl / 2, y1 - inset + hwid / 2);
            if (t.Wear.LinesOnly) t.Score(Poly.Loop(hinge)); else t.Etch(hinge);
        }
        // One grip, near the opening edge: a long slot with a bar scored across it.
        double gl = Math.Clamp((upright ? h : w) * 0.22, _d * 2.5, _d * 7), gw = Math.Max(0.7, _d * 1.3);
        double gx = upright ? (hingeLeft ? x1 - inset * 2.6 - gw / 2 : x0 + inset * 2.6 + gw / 2) : x0 + w / 2;
        double gy = upright ? y0 + h * 0.5 : y0 + inset * 2.6 + gw / 2;
        var grip = upright ? Poly.Rounded(gx - gw / 2, gy - gl / 2, gx + gw / 2, gy + gl / 2, gw / 2) : Poly.Rounded(gx - gl / 2, gy - gw / 2, gx + gl / 2, gy + gw / 2, gw / 2);
        if (t.Wear.LinesOnly) t.Score(Poly.Loop(grip)); else t.Etch(grip);
        // Hazard stripes along the foot of a big hatch.
        if (h > _d * 14 && w > _d * 10 && _rnd.NextDouble() < 0.6)
        {
            double band = Math.Min(h * 0.1, _d * 2.5), bx0 = x0 + inset * 2.4, bx1 = x1 - inset * 2.4;
            if (upright && hingeLeft) bx0 += hwid; else if (upright) bx1 -= hwid;
            Stripes(bx0, y0 + inset * 2.4, bx1, y0 + inset * 2.4 + band, Math.Max(0.5, band * 0.45));
        }
    }

    void Pipes(double x0, double y0, double x1, double y1)
    {
        double w = x1 - x0, h = y1 - y0;
        bool across = w >= h;
        // A thin rail top and bottom (or each side), the pipes between.
        double rail = Math.Max(0.6, _d), gap = Math.Max(0.35, _g * 0.8);
        double a0 = across ? y0 : x0, a1 = across ? y1 : x1, l0 = across ? x0 : y0, l1 = across ? x1 : y1;
        PathD R(double u0, double v0, double u1, double v1) => across ? Poly.Rect(u0, v0, u1, v1) : Poly.Rect(v0, u0, v1, u1);
        t.Clean(R(l0, a0, l1, a0 + rail));
        t.Clean(R(l0, a1 - rail, l1, a1));
        double p0 = a0 + rail + gap, p1 = a1 - rail - gap;
        if (p1 - p0 < 1) return;
        // Pipes of two or three sizes, packed across.
        var pipes = new List<(double From, double To)>();
        for (double v = p0; v < p1 - 0.6;)
        {
            double d = Math.Min(p1 - v, _d * Pick(1.4, 2, 2.8, 3.6));
            if (d < 0.6) break;
            pipes.Add((v, v + d));
            v += d + gap;
        }
        // Clamps across them all, every so often; the pipes stop short of each clamp.
        double clampW = Math.Max(0.8, _d * 1.8), pitch = Math.Max(clampW * 4, Math.Min(l1 - l0, _d * 14));
        var clamps = new List<double>();
        for (double u = l0 + pitch * (0.3 + 0.4 * _rnd.NextDouble()); u < l1 - clampW; u += pitch) clamps.Add(u);
        foreach (var u in clamps) t.Clean(R(u, p0 - gap * 0.4, u + clampW, p1 + gap * 0.4));
        foreach (var (f, to) in pipes)
        {
            double start = l0;
            foreach (var u in clamps.Append(l1 + gap))
            {
                double end = u - gap;
                if (end - start > 0.6) t.Clean(R(start, f, end, to));
                start = u + clampW + gap;
            }
            // A highlight line along each fat pipe, so it reads round.
            if (to - f > _d * 2.4)
            {
                double m = f + (to - f) * 0.62;
                t.Score(across ? [new(l0, m), new(l1, m)] : [new(m, l0), new(m, l1)]);
            }
        }
        t.Weather.Surface([Poly.Rect(x0, y0, x1, y1)], Stuff.Metal, t.Etched, t.Lines);
    }

    /// <summary>A cluster of small boxes, split again and again: ports, slotted blocks, knurled blocks, buttons.</summary>
    void Greebles(double x0, double y0, double x1, double y1, int depth)
    {
        double w = x1 - x0, h = y1 - y0, gap = Math.Max(0.35, _g * 0.8);
        bool split = depth < 3 && Math.Max(w, h) > _d * 7 && (depth == 0 || _rnd.NextDouble() < 0.7);
        if (split)
        {
            bool vertical = w > h ? _rnd.NextDouble() < 0.8 : _rnd.NextDouble() < 0.2;
            double f = 0.3 + 0.4 * _rnd.NextDouble();
            if (vertical)
            {
                double m = x0 + w * f;
                Greebles(x0, y0, m - gap / 2, y1, depth + 1);
                Greebles(m + gap / 2, y0, x1, y1, depth + 1);
            }
            else
            {
                double m = y0 + h * f;
                Greebles(x0, y0, x1, m - gap / 2, depth + 1);
                Greebles(x0, m + gap / 2, x1, y1, depth + 1);
            }
            return;
        }
        if (w < 0.8 || h < 0.8) return;
        double s = Math.Min(w, h);
        Plate(x0, y0, x1, y1, s * 0.1);
        double r = _rnd.NextDouble();
        if (t.Wear.LinesOnly) return;
        if (r < 0.25 && s > _d * 3)
        {
            // A port: a ring with a plug in the middle.
            double cx = (x0 + x1) / 2, cy = (y0 + y1) / 2, pr = s * 0.32;
            t.Etched.AddRange(Poly.Subtract([Poly.Circle(cx, cy, pr)], [Poly.Circle(cx, cy, pr * 0.55)]));
            t.Etch(Poly.Circle(cx, cy, pr * 0.2));
        }
        else if (r < 0.5 && s > _d * 2.5)
        {
            // Slots across the block.
            bool across = w >= h;
            double pitch = Math.Max(0.8, _d * 1.4), sw = pitch * 0.45, inset = Math.Max(0.5, s * 0.18);
            double from = (across ? x0 : y0) + inset, to = (across ? x1 : y1) - inset;
            for (double u = from; u + sw <= to; u += pitch)
                t.Etch(across ? Poly.Rect(u, y0 + inset, u + sw, y1 - inset) : Poly.Rect(x0 + inset, u, x1 - inset, u + sw));
        }
        else if (r < 0.65 && s > _d * 3)
        {
            // Knurled: a grid of little pits.
            double pitch = Math.Max(0.6, _d * 1.1), q = pitch * 0.4, inset = Math.Max(0.5, s * 0.15);
            for (double x = x0 + inset; x + q <= x1 - inset; x += pitch)
                for (double y = y0 + inset; y + q <= y1 - inset; y += pitch)
                    t.Etch(Poly.Rect(x, y, x + q, y + q));
        }
        else if (r < 0.8 && w > _d * 4)
        {
            // A row of buttons.
            double br = Math.Clamp(s * 0.18, 0.25, _d * 0.7), pitch = br * 3;
            int n = Math.Max(1, (int)((w - br * 2) / pitch));
            double startX = (x0 + x1) / 2 - (n - 1) * pitch / 2;
            for (int i = 0; i < n; i++) t.Etch(Poly.Circle(startX + i * pitch, (y0 + y1) / 2, br));
        }
        else if (s > _d * 3)
        {
            // An inset face.
            double inset = Math.Max(0.4, s * 0.16);
            Ring(x0, y0, x1, y1, inset, 0);
        }
    }

    void Hazard(double x0, double y0, double x1, double y1)
    {
        double w = x1 - x0, h = y1 - y0;
        Plate(x0, y0, x1, y1, Chamfer(w, h) * 0.5);
        double inset = Math.Clamp(Math.Min(w, h) * 0.12, 0.5, _d * 1.6);
        Stripes(x0 + inset, y0 + inset, x1 - inset, y1 - inset, Math.Max(0.6, Math.Min(h, w) * 0.22));
    }

    /// <summary>Diagonal hazard stripes in a box, with its edge scored.</summary>
    void Stripes(double x0, double y0, double x1, double y1, double sw)
    {
        if (x1 - x0 < 1 || y1 - y0 < 0.6) return;
        double band = y1 - y0;
        if (!t.Wear.LinesOnly)
        {
            var stripes = new PathsD();
            for (double sx = x0 - band - sw; sx < x1 + band; sx += sw * 2)
                stripes.Add([new(sx, y0), new(sx + sw, y0), new(sx + sw + band, y1), new(sx + band, y1)]);
            t.Etched.AddRange(Poly.Intersect(stripes, [Poly.Rect(x0, y0, x1, y1)]));
        }
        t.Score(Poly.Loop(Poly.Rect(x0, y0, x1, y1)));
    }

    void Light(double x0, double y0, double x1, double y1)
    {
        double w = x1 - x0, h = y1 - y0;
        Plate(x0, y0, x1, y1, Chamfer(w, h));
        bool across = w >= h;
        double th = Math.Clamp(Math.Min(w, h) * 0.16, 0.6, _d * 1.6), end = Math.Max(1.2, _d * 2.5);
        double cx = (x0 + x1) / 2, cy = (y0 + y1) / 2;
        var strip = across
            ? Poly.Rounded(x0 + end, cy - th / 2, x1 - end, cy + th / 2, th / 2)
            : Poly.Rounded(cx - th / 2, y0 + end, cx + th / 2, y1 - end, th / 2);
        if (t.Wear.LinesOnly) t.Score(Poly.Loop(strip)); else t.Etch(strip);
        // Its housing: lines either side, and a bolt at each end.
        double o = th / 2 + Math.Max(0.4, _d * 0.7);
        if (across)
        {
            t.Score([new(x0 + end * 0.6, cy - o), new(x1 - end * 0.6, cy - o)]);
            t.Score([new(x0 + end * 0.6, cy + o), new(x1 - end * 0.6, cy + o)]);
            Bolt(x0 + end * 0.45, cy);
            Bolt(x1 - end * 0.45, cy);
        }
        else
        {
            t.Score([new(cx - o, y0 + end * 0.6), new(cx - o, y1 - end * 0.6)]);
            t.Score([new(cx + o, y0 + end * 0.6), new(cx + o, y1 - end * 0.6)]);
            Bolt(cx, y0 + end * 0.45);
            Bolt(cx, y1 - end * 0.45);
        }
    }

    void Display(double x0, double y0, double x1, double y1)
    {
        double w = x1 - x0, h = y1 - y0;
        Plate(x0, y0, x1, y1, Chamfer(w, h));
        double inset = Math.Clamp(Math.Min(w, h) * 0.15, 0.8, _d * 2.5);
        double sx0 = x0 + inset, sx1 = x1 - inset, sy0 = y0 + h * 0.35, sy1 = y1 - inset;
        if (sx1 - sx0 < 2 || sy1 - sy0 < 2) return;
        var screen = Poly.Rounded(sx0, sy0, sx1, sy1, Math.Min(0.6, _d));
        if (t.Wear.LinesOnly) t.Score(Poly.Loop(screen)); else t.Etch(screen);
        // A frame line round the screen, and a row of keys below it.
        t.Score(Poly.Loop(Poly.Rect(sx0 - 0.4, sy0 - 0.4, sx1 + 0.4, sy1 + 0.4)));
        double ky = (y0 + inset + sy0) / 2, kw = Math.Max(0.6, _d * 1.2), kh = Math.Max(0.4, Math.Min((sy0 - y0 - inset) * 0.5, _d));
        for (double kx = sx0; kx + kw <= sx1; kx += kw * 1.7)
        {
            var key = Poly.Rect(kx, ky - kh / 2, kx + kw, ky + kh / 2);
            if (t.Wear.LinesOnly) t.Score(Poly.Loop(key)); else t.Etch(key);
        }
    }
}
