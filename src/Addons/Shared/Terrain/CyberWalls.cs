using Clipper2Lib;

namespace PhotonCanvas.Addon.Terrain;

/// <summary>
/// The cyberpunk city's outside walls, drawn from the ChatGPT reference sheets Kyle picked (project files
/// references/cyberpunk-exteriors-1.png and cyberpunk-wall-surfaces-2.png). Four kinds, each laid out afresh from
/// its seed so no two walls repeat:
/// <list type="bullet">
/// <item>Hab concrete: cast concrete panels in courses with tie holes at their corners, a plinth along the foot,
/// and the services bolted on: downpipes with brackets and a shoe at the foot, air-conditioning units beside
/// the windows, vent grilles, junction boxes fed by conduit.</item>
/// <item>Steel cladding: a steel frame of posts and rails bolted through gusset plates, corrugated sheets between
/// (upright or across), now and then a flat patch plate or a cross brace, with vents and conduit.</item>
/// <item>Shopfront: the street level: concrete with a roll-up shutter (its housing, guides and a hazard-striped
/// foot), a long sign box, torn posters, tags, a meter box and conduit.</item>
/// <item>Glass facade: a tower's curtain wall: mullions and spandrel bands standing proud, the glass sunk between
/// them with transoms, blinds and ceiling lights inside.</item>
/// </list>
/// What's bolted on keeps clear of the wall's doors and windows (<paramref name="avoid"/>), and the air-conditioning
/// units hang beside the windows, as on the reference.
/// </summary>
internal sealed class CyberWalls(TextureEngine.Texture t, IReadOnlyList<RectD> avoid)
{
    public const string HabConcrete = "Hab concrete";
    public const string SteelCladding = "Steel cladding";
    public const string Shopfront = "Shopfront";
    public const string GlassFacade = "Glass facade";

    readonly Random _rnd = t.Rnd;
    readonly List<RectD> _taken = [];
    double _g, _d, _len, _h;

    bool LinesOnly => t.Wear.LinesOnly;

    double R(double a, double b) => a + (b - a) * _rnd.NextDouble();

    void Begin(double len, double h, double g)
    {
        _len = len;
        _h = h;
        _g = Math.Max(0.25, g);
        _d = Math.Max(0.3, t.D);
    }

    // ---- small parts ----

    double BoltR => Math.Clamp(_d * 0.34, 0.2, 0.5);

    void Hole(PathD p)
    {
        if (LinesOnly) t.Score(Poly.Loop(p));
        else t.Etch(p);
    }

    void Bolt(double x, double y) => Hole(Poly.Circle(x, y, BoltR));

    /// <summary>A machined plate, crisp (dented now and then on a well-worn wall).</summary>
    void Plate(PathD p)
    {
        if (t.Wear.Amount > 0.6 && t.Weather.Random.NextDouble() < (t.Wear.Amount - 0.6) * 1.5) t.Raised.AddRange(t.Weather.Piece(p, Stuff.Metal, t.Etched, t.Lines));
        else t.Clean(p);
        if (t.Weather.Random.NextDouble() < 0.15 + t.Wear.Amount * 0.4) t.Weather.Surface([p], Stuff.Metal, t.Etched, t.Lines);
    }

    /// <summary>Takes everything raised so far out of an area (with a groove's gap round it), so something can stand in front of it.</summary>
    void ClearFor(PathD area, double gap = -1)
    {
        var kept = Poly.Subtract([.. t.Raised], Poly.Offset([area], gap < 0 ? _g : gap));
        t.Raised.Clear();
        t.Raised.AddRange(kept);
    }

    /// <summary>Whether a box is clear of the doors and windows and of what's bolted on already (and inside the face, if asked).</summary>
    bool Free(double x0, double y0, double x1, double y1, double margin = 1, bool inside = true)
    {
        if (inside && (x0 < 0.5 || x1 > _len - 0.5 || y0 < 0 || y1 > _h)) return false;
        foreach (var a in avoid)
            if (x0 < a.right + margin && x1 > a.left - margin && y0 < a.bottom + margin && y1 > a.top - margin) return false;
        foreach (var a in _taken)
            if (x0 < a.right + margin && x1 > a.left - margin && y0 < a.bottom + margin && y1 > a.top - margin) return false;
        return true;
    }

    void Take(double x0, double y0, double x1, double y1) => _taken.Add(new RectD(x0, y0, x1, y1));

    /// <summary>Slats across a slot: the slot left sunk, raised bars across it.</summary>
    void Grille(double x0, double y0, double x1, double y1, bool upright = false)
    {
        if (x1 - x0 < 1 || y1 - y0 < 1) return;
        var frame = Poly.Rect(x0, y0, x1, y1);
        ClearFor(frame);
        Plate(frame);
        double rim = Math.Max(0.5, Math.Min(x1 - x0, y1 - y0) * 0.12);
        var slot = Poly.Rect(x0 + rim, y0 + rim, x1 - rim, y1 - rim);
        if (LinesOnly) { t.Score(Poly.Loop(slot)); return; }
        ClearFor(slot, 0);
        double pitch = Math.Max(0.8, _d * 1.4), slat = pitch * 0.5;
        if (upright)
            for (double x = x0 + rim + pitch * 0.25; x + slat <= x1 - rim; x += pitch) t.Clean(Poly.Rect(x, y0 + rim, x + slat, y1 - rim));
        else
            for (double y = y0 + rim + pitch * 0.25; y + slat <= y1 - rim; y += pitch) t.Clean(Poly.Rect(x0 + rim, y, x1 - rim, y + slat));
    }

    /// <summary>A pipe or conduit along a line, standing proud, with a groove's gap round it.</summary>
    void Tube(PathD line, double w)
    {
        if (line.Count < 2) return;
        var body = Poly.Union([.. line.Zip(line.Skip(1)).Select(p => Poly.Strip(p.First, p.Second, w)), .. line.Select(p => Poly.Circle(p.x, p.y, w / 2))]);
        foreach (var b in body) ClearFor(b);
        if (LinesOnly) { foreach (var b in body) t.Score(Poly.Loop(b)); return; }
        t.Raised.AddRange(body);
    }

    /// <summary>A collar round a tube at a point: a little wider band across it.</summary>
    void Collar(double x, double y, double w, bool upright)
    {
        double a = w * 0.75, b = Math.Max(0.6, w * 0.45);
        var c = upright ? Poly.Rect(x - a, y - b / 2, x + a, y + b / 2) : Poly.Rect(x - b / 2, y - a, x + b / 2, y + a);
        ClearFor(c, Math.Max(0.2, _g * 0.6));
        Plate(c);
    }

    // ---- fittings ----

    /// <summary>A downpipe the full height: a hopper at the top, brackets up it, a shoe kicking out at the foot.</summary>
    bool Downpipe(double x, double foot)
    {
        double w = Math.Clamp(_h * 0.05, 1.6, 3.2);
        if (!Free(x - w * 1.4, 0, x + w * 1.4, _h, 0.6, inside: false)) return false;
        bool left = _rnd.NextDouble() < 0.5;
        double shoe = Math.Max(w * 1.6, 2.5);
        var line = new PathD { new(x, _h + 1), new(x, foot + shoe), new(x + (left ? -shoe : shoe), foot + w * 0.3) };
        Tube(line, w);
        // The hopper under the gutter.
        var hopper = new PathD { new(x - w * 1.3, _h + 1), new(x + w * 1.3, _h + 1), new(x + w * 1.3, _h - w * 1.2), new(x + w * 0.5, _h - w * 2.2), new(x - w * 0.5, _h - w * 2.2), new(x - w * 1.3, _h - w * 1.2) };
        if (_h > w * 8) { ClearFor(hopper); Plate(hopper); }
        for (double y = foot + shoe + R(4, 8); y < _h - w * 3; y += R(9, 14)) Collar(x, y, w, upright: true);
        Take(x - w * 1.4, 0, x + w * 1.4, _h);
        return true;
    }

    /// <summary>An air-conditioning unit: a box with a round fan grille, a slatted side, feet on a bracket under it and its pipe running down.</summary>
    bool AirCon(double x0, double y0, double size)
    {
        double w = size, h = size * 0.72;
        if (!Free(x0 - 0.5, y0 - h * 0.35, x0 + w + 0.5, y0 + h + 0.5, 0.6)) return false;
        var box = Poly.Rounded(x0, y0, x0 + w, y0 + h, Math.Min(w, h) * 0.06);
        ClearFor(box);
        Plate(box);
        double r = h * 0.36, cx = x0 + h * 0.5, cy = y0 + h * 0.5;
        // The fan: a sunk ring with a hub and blades standing in it.
        if (LinesOnly) t.Score(Poly.Loop(Poly.Circle(cx, cy, r)));
        else
        {
            ClearFor(Poly.Circle(cx, cy, r), 0);
            t.Clean(Poly.Circle(cx, cy, r * 0.22));
            int blades = 5;
            double spin = _rnd.NextDouble() * Math.PI;
            for (int k = 0; k < blades; k++)
            {
                double a = spin + k * 2 * Math.PI / blades;
                var tip = new PointD(cx + Math.Cos(a) * r * 0.86, cy + Math.Sin(a) * r * 0.86);
                var tip2 = new PointD(cx + Math.Cos(a + 0.45) * r * 0.86, cy + Math.Sin(a + 0.45) * r * 0.86);
                t.Clean([new(cx + Math.Cos(a - 0.4) * r * 0.2, cy + Math.Sin(a - 0.4) * r * 0.2), tip, tip2, new(cx + Math.Cos(a + 0.4) * r * 0.2, cy + Math.Sin(a + 0.4) * r * 0.2)]);
            }
        }
        // Slats on the other side, a badge over them.
        double sx0 = x0 + h * 1.02, sx1 = x0 + w - h * 0.1;
        if (sx1 - sx0 > 1.2)
        {
            double pitch = Math.Max(0.8, _d * 1.3);
            for (double y = y0 + h * 0.15; y < y0 + h * 0.62; y += pitch) Hole(Poly.Rect(sx0, y, sx1, y + pitch * 0.45));
            Hole(Poly.Rect(sx0, y0 + h * 0.72, sx0 + (sx1 - sx0) * 0.6, y0 + h * 0.84));
        }
        // Two angle brackets under it, and the pipe out of its side.
        double bh = h * 0.3;
        foreach (double bx in new[] { x0 + w * 0.18, x0 + w * 0.82 })
        {
            var leg = new PathD { new(bx - 0.5, y0), new(bx + 0.5, y0), new(bx + 0.5, y0 - bh), new(bx - 0.5, y0 - bh) };
            ClearFor(leg, 0.25);
            t.Clean(leg);
        }
        Take(x0 - 0.5, y0 - bh, x0 + w + 0.5, y0 + h + 0.5);
        double pw = Math.Max(0.7, _d * 1.1);
        if (y0 > h * 0.6 && _rnd.NextDouble() < 0.75)
        {
            double px = x0 + w + pw * 1.2, end = Math.Max(1, y0 - R(h * 0.6, h * 1.6));
            if (Free(px - pw, end, px + pw, y0 + h * 0.5, 0.3))
            {
                Tube([new(x0 + w - 0.2, y0 + h * 0.4), new(px, y0 + h * 0.4), new(px, end)], pw);
                Take(px - pw, end, px + pw, y0 + h * 0.5);
            }
        }
        return true;
    }

    /// <summary>A junction box (a plate with a lid line and bolts, a warning triangle on it) with conduit running into it.</summary>
    bool Junction(double cx, double cy, double size, bool feed)
    {
        double w = size, h = size * 1.25;
        double x0 = cx - w / 2, y0 = cy - h / 2;
        if (!Free(x0, y0, x0 + w, y0 + h, 0.8)) return false;
        double cw = Math.Max(0.7, _d * 1.0);
        if (feed)
        {
            // Two conduits from the top, one turning along, or straight up out of the face.
            double run = R(w * 1.5, w * 4) * (_rnd.NextDouble() < 0.5 ? -1 : 1);
            double up = Math.Min(_h + 1, y0 + h + R(h * 0.8, h * 2.5));
            for (int k = 0; k < 2; k++)
            {
                double px = x0 + w * (0.3 + 0.4 * k), turnY = up - k * cw * 1.6;
                var line = new PathD { new(px, y0 + h - 0.2), new(px, turnY) };
                if (up < _h) line.Add(new(px + run, turnY));
                Tube(line, cw);
            }
            Take(Math.Min(x0, x0 + run), y0, Math.Max(x0 + w, x0 + w + run), up);
        }
        var box = Poly.Rect(x0, y0, x0 + w, y0 + h);
        ClearFor(box);
        Plate(box);
        double i = Math.Max(0.5, w * 0.12);
        if (!LinesOnly) t.Score([new(x0 + i, y0 + h * 0.28), new(x0 + w - i, y0 + h * 0.28)]);
        Bolt(x0 + i, y0 + h - i); Bolt(x0 + w - i, y0 + h - i); Bolt(x0 + i, y0 + i); Bolt(x0 + w - i, y0 + i);
        double tw = w * 0.38, tx = cx, ty = y0 + h * 0.6;
        if (tw > 1.2) Hole([new(tx - tw / 2, ty - tw * 0.4), new(tx + tw / 2, ty - tw * 0.4), new(tx, ty + tw * 0.48)]);
        Take(x0, y0, x0 + w, y0 + h);
        return true;
    }

    /// <summary>A louvred vent: a framed plate with slats across a sunk slot.</summary>
    bool Vent(double x0, double y0, double w, double h)
    {
        if (!Free(x0, y0, x0 + w, y0 + h, 0.8)) return false;
        Grille(x0, y0, x0 + w, y0 + h);
        double i = Math.Max(0.4, Math.Min(w, h) * 0.08);
        if (w > 6) { Bolt(x0 + i, y0 + i); Bolt(x0 + w - i, y0 + i); Bolt(x0 + i, y0 + h - i); Bolt(x0 + w - i, y0 + h - i); }
        Take(x0, y0, x0 + w, y0 + h);
        return true;
    }

    /// <summary>Tries a fitting at a few random spots until one is free.</summary>
    bool Try(Func<double, double, bool> put, double x0, double x1, double y0, double y1, int tries = 8)
    {
        for (int k = 0; k < tries; k++)
            if (x1 > x0 && y1 > y0 && put(R(x0, x1), R(y0, y1))) return true;
        return false;
    }

    /// <summary>
    /// What's bolted on: an air-conditioning unit beside some of the windows (or anywhere on a blank wall), a
    /// downpipe or two, vents and a junction box with its conduit, as many as the wall's length takes.
    /// </summary>
    void Services(double foot, double perMm, bool aircon = true, bool pipes = true)
    {
        double ac = Math.Clamp(_h * 0.2, 6, 13);
        int n = Math.Max(1, (int)Math.Round(_len * perMm));
        // Air-conditioning beside windows first (the ones off the ground), as on the hab block's front.
        if (aircon)
        {
            foreach (var win in avoid.Where(a => a.top > _h * 0.25 && a.bottom - a.top < _h * 0.6).OrderBy(_ => _rnd.Next()))
            {
                if (_rnd.NextDouble() > 0.55) continue;
                bool right = _rnd.NextDouble() < 0.5;
                double y = Math.Max(foot + ac * 0.4, win.top + R(-ac * 0.1, ac * 0.3));
                double x = right ? win.right + R(1.2, 2.5) : win.left - R(1.2, 2.5) - ac;
                if (!AirCon(x, y, ac)) AirCon(right ? win.left - 2 - ac : win.right + 2, y, ac);
            }
            if (!avoid.Any() || _rnd.NextDouble() < 0.4)
                for (int k = 0; k < Math.Max(1, n / 2); k++)
                    Try((x, y) => AirCon(x, y, ac), 1, _len - ac - 1, Math.Max(foot + ac * 0.4, _h * 0.3), _h - ac * 0.9);
        }
        if (pipes && _len > 30)
        {
            int downs = _rnd.NextDouble() < 0.35 ? 2 : 1;
            for (int k = 0; k < downs; k++)
                for (int tries = 0; tries < 10; tries++)
                    if (Downpipe(R(3, _len - 3), foot)) break;
        }
        for (int k = 0; k < n; k++)
        {
            switch (_rnd.Next(3))
            {
                case 0:
                    double vw = R(5, 9), vh = vw * R(0.45, 0.7);
                    Try((x, y) => Vent(x, y, vw, vh), 1, _len - vw - 1, foot + 1, _h - vh - 1.5);
                    break;
                case 1:
                    double js = R(3, 4.5);
                    Try((x, y) => Junction(x, y, js, true), js, _len - js, foot + js * 1.5, _h * 0.7);
                    break;
                default:
                    double sv = R(3.5, 5.5);
                    Try((x, y) => Vent(x, y, sv, sv * 0.6), 1, _len - sv - 1, foot + 0.5, foot + sv * 1.6);
                    break;
            }
        }
    }

    // ---- hab concrete ----

    /// <summary>Concrete panels in courses (heights about <paramref name="b"/>, lengths about <paramref name="a"/>), tie holes at their corners, a plinth, and the services.</summary>
    public void Concrete(double len, double h, double a, double b, double g, bool services = true)
    {
        Begin(len, h, g);
        double plinth = Math.Clamp(b * 0.35, 2, 5);
        Panels(len, h, a, b, plinth);
        // The plinth along the foot: a plain band, now and then a step.
        t.Piece(Poly.Rect(-1, -1, len + 1, plinth - _g / 2), Stuff.Stone);
        if (services) Services(plinth, 1 / 38.0);
    }

    void Panels(double len, double h, double a, double b, double y0)
    {
        bool stagger = _rnd.NextDouble() < 0.45;
        double off = -a * _rnd.NextDouble();
        int row = 0;
        for (double y = y0; y < h + b; row++)
        {
            double hh = b * R(0.85, 1.2);
            double x = stagger ? off - (row % 2) * a * 0.5 : off;
            while (x < len + a)
            {
                double w = a * (_rnd.NextDouble() < 0.15 ? R(0.45, 0.6) : R(0.9, 1.15));
                var p = Poly.Rect(x + _g / 2, y + _g / 2, x + w - _g / 2, y + hh - _g / 2);
                // Cast concrete: crisp, the odd corner knocked, pitted as the wear says.
                t.Piece(p, Stuff.Tile);
                // Tie holes: four near the corners, or two along the top on a short panel.
                if (_rnd.NextDouble() < 0.75 && w > 4 && hh > 4)
                {
                    double i = Math.Clamp(Math.Min(w, hh) * 0.13, 0.9, 2.2);
                    double tr = Math.Clamp(_d * 0.5, 0.3, 0.6);
                    void Tie(double px, double py) => Hole(Poly.Circle(px, py, tr));
                    Tie(x + i, y + hh - i); Tie(x + w - i, y + hh - i);
                    if (w > a * 0.7) { Tie(x + i, y + i); Tie(x + w - i, y + i); }
                }
                // A rust or rain streak down from a seam now and then.
                if (!LinesOnly && _rnd.NextDouble() < 0.12 + t.Wear.Amount * 0.2)
                {
                    double sx = x + w * R(0.2, 0.8), len2 = hh * R(0.3, 0.8);
                    t.Score([new(sx, y + hh - _g), new(sx + R(-0.3, 0.3), y + hh - _g - len2)]);
                }
                x += w;
            }
            y += hh;
        }
    }

    // ---- steel cladding ----

    /// <summary>A steel frame (bays about <paramref name="a"/> wide, sheets about <paramref name="b"/> high) with corrugated sheets between, patches, braces and services.</summary>
    public void Cladding(double len, double h, double a, double b, double g)
    {
        Begin(len, h, g);
        double post = Math.Clamp(a * 0.12, 1.6, 3.2), rail = post * 0.85;
        double plinth = Math.Clamp(b * 0.25, 2, 4);
        t.Piece(Poly.Rect(-1, -1, len + 1, plinth - _g / 2), Stuff.Stone);
        // Rails: two or three courses of sheet up the storey, at heights that vary a little.
        var rails = new List<double> { plinth };
        for (double y = plinth + b * R(0.8, 1.2); y < h - b * 0.4; y += b * R(0.8, 1.2)) rails.Add(y);
        rails.Add(h + 1);
        var posts = new List<double>();
        for (double x = -a * _rnd.NextDouble(); x < len + a; x += a * R(0.85, 1.2)) posts.Add(x);
        double pitch = Math.Max(1.2, _d * 2.6);
        bool upright = _rnd.NextDouble() < 0.7;
        for (int i = 0; i + 1 < posts.Count; i++)
            for (int j = 0; j + 1 < rails.Count; j++)
            {
                double x0 = posts[i] + post / 2 + _g, x1 = posts[i + 1] - post / 2 - _g, y0 = rails[j] + rail / 2 + _g, y1 = rails[j + 1] - rail / 2 - _g;
                if (x1 - x0 < 1 || y1 - y0 < 1) continue;
                double roll = _rnd.NextDouble();
                if (roll < 0.12)
                {
                    // A flat patch plate, riveted round.
                    Plate(Poly.Rect(x0, y0, x1, y1));
                    double i0 = Math.Max(0.7, _d);
                    for (double rx = x0 + i0; rx < x1 - i0 / 2; rx += Math.Max(2, (x1 - x0 - 2 * i0) / Math.Max(1, Math.Round((x1 - x0) / 4)))) { Bolt(rx, y0 + i0); Bolt(rx, y1 - i0); }
                }
                else
                {
                    // Corrugated: ridges standing proud, upright on most walls, across now and then.
                    bool up = roll < 0.85 ? upright : !upright;
                    if (up)
                        for (double x = x0 + pitch * R(0, 0.5); x + pitch * 0.45 < x1; x += pitch) t.Piece(Poly.Rect(x, y0, x + pitch * 0.5, y1), Stuff.Metal);
                    else
                        for (double y = y0 + pitch * R(0, 0.5); y + pitch * 0.45 < y1; y += pitch) t.Piece(Poly.Rect(x0, y, x1, y + pitch * 0.5), Stuff.Metal);
                    // A cross brace over one bay in five.
                    if (_rnd.NextDouble() < 0.1 && x1 - x0 > 6 && y1 - y0 > 6)
                    {
                        double bw = rail * 0.9;
                        Tube([new(x0, y0), new(x1, y1)], bw);
                        Tube([new(x0, y1), new(x1, y0)], bw);
                    }
                }
            }
        // The frame over the sheets: posts, rails, and a bolted gusset where they cross.
        foreach (double x in posts)
        {
            var p = Poly.Rect(x - post / 2, plinth, x + post / 2, h + 1);
            ClearFor(p);
            Plate(p);
        }
        foreach (double y in rails.Skip(1).SkipLast(1))
        {
            var r = Poly.Rect(-1, y - rail / 2, len + 1, y + rail / 2);
            ClearFor(r);
            Plate(r);
        }
        foreach (double x in posts)
            foreach (double y in rails.Skip(1).SkipLast(1))
            {
                double s = post * 1.25;
                var gp = Poly.Rect(x - s, y - s, x + s, y + s);
                ClearFor(gp, Math.Max(0.2, _g * 0.6));
                Plate(gp);
                double i = s * 0.55;
                Bolt(x - i, y - i); Bolt(x + i, y - i); Bolt(x - i, y + i); Bolt(x + i, y + i);
            }
        Services(plinth, 1 / 45.0, aircon: _rnd.NextDouble() < 0.5);
    }

    // ---- the shopfront ----

    /// <summary>The street level: concrete panels with a roll-up shutter, a sign box, posters, tags and services.</summary>
    public void Shop(double len, double h, double a, double b, double g)
    {
        Begin(len, h, g);
        double plinth = Math.Clamp(b * 0.3, 2, 4);
        Panels(len, h, a * 1.3, b * 1.3, plinth);
        t.Piece(Poly.Rect(-1, -1, len + 1, plinth - _g / 2), Stuff.Stone);
        // A roll-up shutter where there's room for one (about a door and a half wide, most of the storey high).
        double sw = Math.Clamp(h * 0.55, 14, 34), sh = Math.Min(h * 0.62, sw * 1.05);
        for (int k = 0; k < 10; k++)
        {
            double x = R(1.5, len - sw - 1.5);
            if (len - sw < 3) break;
            if (Shutter(x, 0, sw, sh)) break;
        }
        // A long sign box high on the wall.
        double bw = Math.Clamp(len * R(0.3, 0.5), 14, 60), bh = Math.Clamp(h * 0.12, 3.5, 8);
        Try((x, y) => SignBox(x, y, bw, bh), 1.5, len - bw - 1.5, h * 0.66, h - bh - 2, 12);
        // Posters and tags low down, beside the doors.
        int posters = Math.Max(1, (int)(len / 40));
        for (int k = 0; k < posters; k++)
        {
            double pw = R(4, 7), ph = pw * R(1.25, 1.5);
            Try((x, y) => Poster(x, y, pw, ph), 1, len - pw - 1, plinth + 2, h * 0.55);
        }
        int tags = Math.Max(1, (int)(len / 50));
        for (int k = 0; k < tags; k++) Try((x, y) => Tag(x, y, R(7, 12), R(3, 5)), 1, len - 13, plinth + 1, h * 0.4);
        Services(plinth, 1 / 70.0, aircon: _rnd.NextDouble() < 0.5, pipes: _rnd.NextDouble() < 0.6);
    }

    /// <summary>A roll-up shutter: a housing box on top, guide rails each side, slats, hazard stripes along its foot.</summary>
    bool Shutter(double x0, double y0, double w, double h)
    {
        if (!Free(x0 - 1.5, y0, x0 + w + 1.5, y0 + h + 3, 0.8, inside: false)) return false;
        double hb = Math.Clamp(h * 0.12, 2.5, 5), guide = Math.Clamp(w * 0.05, 1, 2);
        var whole = Poly.Rect(x0 - guide, y0, x0 + w + guide, y0 + h + hb);
        ClearFor(whole);
        var housing = Poly.Rect(x0 - guide, y0 + h, x0 + w + guide, y0 + h + hb);
        Plate(housing);
        Bolt(x0 + guide, y0 + h + hb / 2); Bolt(x0 + w - guide, y0 + h + hb / 2);
        foreach (double gx in new[] { x0 - guide, x0 + w })
            Plate(Poly.Rect(gx, y0, gx + guide, y0 + h - _g));
        // Slats: raised strips with sunk lines between, the bottom bar heavier.
        double pitch = Math.Max(1.1, _d * 2.2), bar = Math.Max(1.5, h * 0.06);
        double sx0 = x0 + _g, sx1 = x0 + w - _g;
        for (double y = y0 + bar + _g; y + pitch * 0.4 < y0 + h - _g; y += pitch)
            if (LinesOnly) t.Score([new(sx0, y), new(sx1, y)]);
            else t.Clean(Poly.Rect(sx0, y + pitch * 0.18, sx1, y + pitch - pitch * 0.18));
        var foot = Poly.Rect(sx0, y0 + 0.3, sx1, y0 + bar);
        Plate(foot);
        // Hazard stripes sunk across the foot bar.
        if (!LinesOnly)
        {
            double sw = bar * 1.1;
            for (double x = sx0 - bar; x < sx1; x += sw * 2)
            {
                var stripe = Poly.Intersect([new PathD { new(x, y0 + 0.3), new(x + sw, y0 + 0.3), new(x + sw + bar, y0 + bar), new(x + bar, y0 + bar) }], [foot]);
                foreach (var s in stripe) t.Etch(s);
            }
        }
        // A handle near the foot.
        double hx = x0 + w / 2;
        Hole(Poly.Rect(hx - w * 0.06, y0 + bar + pitch * 0.4, hx + w * 0.06, y0 + bar + pitch * 0.9));
        Take(x0 - guide, y0, x0 + w + guide, y0 + h + hb);
        return true;
    }

    /// <summary>A long sign box: a deep frame, its face sunk with a tube border and a few bars of lettering standing in it.</summary>
    bool SignBox(double x0, double y0, double w, double h)
    {
        if (!Free(x0, y0, x0 + w, y0 + h, 1)) return false;
        var box = Poly.Rounded(x0, y0, x0 + w, y0 + h, h * 0.25);
        ClearFor(box);
        Plate(box);
        double rim = Math.Max(0.6, h * 0.16);
        var face = Poly.Rounded(x0 + rim, y0 + rim, x0 + w - rim, y0 + h - rim, h * 0.15);
        if (LinesOnly) t.Score(Poly.Loop(face));
        else
        {
            ClearFor(face, 0);
            // The tube round the inside, then "lettering": blocks of different lengths along the middle.
            double tw = Math.Max(0.45, _d * 0.7);
            var ring = Poly.Subtract(Poly.Offset([face], -tw * 0.8), Poly.Offset([face], -tw * 1.8));
            t.Raised.AddRange(ring);
            double lx = x0 + rim + tw * 3, lh = (h - 2 * rim) * 0.36, ly = y0 + h / 2 - lh / 2;
            while (lx < x0 + w - rim - tw * 3)
            {
                double lw = Math.Min(R(lh * 0.6, lh * 1.4), x0 + w - rim - tw * 3 - lx);
                if (lw > 0.5) t.Clean(Poly.Rect(lx, ly, lx + lw, ly + lh));
                lx += lw + R(lh * 0.35, lh * 0.9);
            }
        }
        // Brackets off each end into the wall.
        foreach (double bx in new[] { x0 - 1.2, x0 + w })
        {
            var br = Poly.Rect(bx, y0 + h * 0.3, bx + 1.2, y0 + h * 0.7);
            ClearFor(br, 0.2);
            t.Clean(br);
        }
        Take(x0 - 1.2, y0, x0 + w + 1.2, y0 + h);
        return true;
    }

    /// <summary>A poster pasted on, a little askew, a corner torn off, a picture block and lines of print on it.</summary>
    bool Poster(double x0, double y0, double w, double h)
    {
        if (!Free(x0, y0, x0 + w, y0 + h, 0.6)) return false;
        double turn = R(-0.08, 0.08), cx = x0 + w / 2, cy = y0 + h / 2;
        PointD Rot(double x, double y) => new(cx + (x - cx) * Math.Cos(turn) - (y - cy) * Math.Sin(turn), cy + (x - cx) * Math.Sin(turn) + (y - cy) * Math.Cos(turn));
        double tear = Math.Min(w, h) * R(0.15, 0.35);
        var sheet = new PathD { Rot(x0, y0), Rot(x0 + w, y0), Rot(x0 + w, y0 + h - tear), Rot(x0 + w - tear * 0.4, y0 + h - tear * 0.6), Rot(x0 + w - tear, y0 + h), Rot(x0, y0 + h) };
        if (_rnd.NextDouble() < 0.5) (sheet[0], sheet[1]) = (Rot(x0 + tear * 0.5, y0 + tear * 0.3), sheet[1]);
        ClearFor(sheet, Math.Max(0.2, _g * 0.5));
        t.Clean(sheet);
        // A picture block on top, lines of print under it.
        double m = Math.Max(0.6, w * 0.12);
        var pic = new PathD { Rot(x0 + m, y0 + h * 0.42), Rot(x0 + w - m, y0 + h * 0.42), Rot(x0 + w - m, y0 + h - m - tear * 0.5), Rot(x0 + m, y0 + h - m - tear * 0.5) };
        Hole(pic);
        for (double y = y0 + h * 0.32; y > y0 + m; y -= Math.Max(0.9, h * 0.1))
            t.Score([Rot(x0 + m, y), Rot(x0 + m + (w - 2 * m) * R(0.5, 1), y)]);
        Take(x0, y0, x0 + w, y0 + h);
        return true;
    }

    /// <summary>A sprayed tag: a few looping scored strokes, a drip or two.</summary>
    bool Tag(double x0, double y0, double w, double h)
    {
        if (!Free(x0, y0, x0 + w, y0 + h, 0.4)) return false;
        int strokes = _rnd.Next(2, 4);
        for (int s = 0; s < strokes; s++)
        {
            var line = new PathD();
            double ph = _rnd.NextDouble() * 6, f = R(2.5, 4.5), yy = y0 + h * R(0.2, 0.8), amp = h * R(0.25, 0.45);
            for (int i = 0; i <= 40; i++)
            {
                double u = i / 40.0, x = x0 + w * u + Math.Sin(u * f * 7 + ph) * w * 0.04;
                line.Add(new(x, yy + amp * Math.Sin(u * f * Math.PI * 2 + ph) * (0.6 + 0.4 * Math.Cos(u * 9 + ph))));
            }
            t.Score(line);
        }
        for (int k = 0; k < 2; k++)
        {
            double dx = x0 + w * R(0.1, 0.9);
            t.Score([new(dx, y0 + h * 0.3), new(dx, y0 + h * 0.3 - R(1, 3))]);
        }
        Take(x0, y0, x0 + w, y0 + h);
        return true;
    }

    // ---- the glass facade ----

    /// <summary>
    /// A curtain wall: mullions about <paramref name="a"/> apart standing proud, spandrel bands (about <paramref name="b"/>
    /// apart up the face) across them, and the glass sunk between with transoms, blinds and ceiling lights inside.
    /// </summary>
    public void Glass(double len, double h, double a, double b, double g)
    {
        Begin(len, h, g);
        double mull = Math.Clamp(a * 0.16, 1.4, 3.2), band = Math.Clamp(b * 0.3, 2.5, 8);
        double plinth = Math.Clamp(b * 0.2, 1.5, 4);
        // The floor bands: one at the foot, one or two more up the storey.
        var bands = new List<double> { 0 };
        int across = Math.Max(1, (int)Math.Round(h / Math.Max(10, b)));
        for (int k = 1; k <= across; k++) bands.Add(h * k / across);
        // Everything starts sunk (the glass), the frame is raised over it.
        double start = -a * _rnd.NextDouble();
        var mullions = new List<double>();
        bool doubled = _rnd.NextDouble() < 0.35;
        for (double x = start; x < len + a; x += a) mullions.Add(x);
        for (int i = 0; i + 1 < mullions.Count; i++)
            for (int j = 0; j + 1 < bands.Count; j++)
            {
                double x0 = mullions[i] + mull / 2 + _g, x1 = mullions[i + 1] - mull / 2 - _g;
                double y0 = bands[j] + (j == 0 ? plinth : band / 2) + _g, y1 = bands[j + 1] - band / 2 - _g;
                if (x1 - x0 < 1 || y1 - y0 < 1) continue;
                // The glass: sunk; a transom across it, and inside now a light, now blinds part way down.
                double tr = y0 + (y1 - y0) * 0.72;
                if (LinesOnly) t.Score([new(x0, tr), new(x1, tr)]);
                else t.Clean(Poly.Rect(x0, tr - 0.3, x1, tr + 0.3));
                if (doubled && x1 - x0 > 5) t.Clean(Poly.Rect((x0 + x1) / 2 - 0.3, y0, (x0 + x1) / 2 + 0.3, y1));
                double roll = _rnd.NextDouble();
                if (roll < 0.45)
                {
                    double lw = (x1 - x0) * R(0.3, 0.55), lx = R(x0 + 0.8, x1 - 0.8 - lw), ly = tr - (tr - y0) * R(0.1, 0.25);
                    t.Clean(Poly.Rect(lx, ly - 0.35, lx + lw, ly + 0.35));
                }
                else if (roll < 0.7)
                {
                    double down = tr - (tr - y0) * R(0.2, 0.7);
                    for (double y = tr - 0.9; y > down; y -= Math.Max(0.8, _d * 1.4)) t.Score([new(x0 + 0.3, y), new(x1 - 0.3, y)]);
                }
            }
        foreach (double x in mullions) Plate(Poly.Rect(x - mull / 2, plinth, x + mull / 2, h + 1));
        for (int j = 0; j < bands.Count; j++)
        {
            double y = bands[j];
            var r = j == 0 ? Poly.Rect(-1, -1, len + 1, plinth) : Poly.Rect(-1, y - band / 2, len + 1, y + band / 2);
            ClearFor(r);
            Plate(r);
            // A seam along the spandrel, and a bolt over each mullion.
            if (j > 0)
            {
                if (!LinesOnly) t.Score([new(-1, y), new(len + 1, y)]);
                foreach (double x in mullions) Bolt(x, y + band * 0.25);
            }
        }
        // Fewer services on glass: a vent low down and a junction box now and then.
        if (_rnd.NextDouble() < 0.5) Try((x, y) => Vent(x, y, R(5, 8), R(2.5, 4)), 1, len - 9, plinth + 0.5, plinth + 3, 6);
    }
}
